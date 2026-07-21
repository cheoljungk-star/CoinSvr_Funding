using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Futures.Socket;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Sockets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoinSvr
{
    /// <summary>
    /// 펀딩비 반락(C전략) 매니저
    /// - 소켓 1개로 UserData + MarkPrice 통합 관리
    /// - FundingFee 이벤트 수신 → 반락 모멘텀 확인 → 진입 → 되돌림/타임아웃 → 청산
    /// </summary>
    public sealed class FundingHedgeManager
    {
        private const int LEVERAGE = 5;
        private const int DISPATCH_BEFORE_FUNDING_SEC = 15;
        private const decimal MIN_RESERVE = 800m;

        // 테스트 상수 (실서비스 전환 시 교체)
        private const int TEST_CANDIDATE_COUNT = 4; // 참고: 사용자가 로컬에서 10으로 변경해 사용 중일 수 있음
        private const decimal ROUND_TRIP_FEE_PCT = 0.08m; // 시장가 왕복(진입+청산) 예상 수수료 - 테이커 0.04%*2 근사
        private const decimal TEST_NOTIONAL = 100m;

        private CancellationTokenSource? _cts;
        private BinanceSocketClient? _socketClient;
        private UpdateSubscription? _markPriceSub;
        private UpdateSubscription? _bookTickerSub;
        private UpdateSubscription? _userDataSub;
        private string? _listenKey;

        private readonly ConcurrentDictionary<string, List<(DateTime Time, decimal Price)>> _priceHistory = new();

        private readonly ConcurrentDictionary<string, decimal> _markPrices = new();
        // BookTicker 기반 실시간 중간가 - MarkPrice(1초)보다 훨씬 촘촘함(사실상 이벤트단위).
        // 트레일링 청산/진입 모멘텀 판단처럼 초단위 미시움직임이 중요한 곳에만 사용.
        private readonly ConcurrentDictionary<string, (decimal Bid, decimal Ask, DateTime Time)> _bookTicker = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastBookTickerProcessTime = new(); // 심볼별 처리 스로틀
        private readonly ConcurrentDictionary<string, List<(DateTime Time, decimal Mid)>> _bookTickerHistory = new(); // 최근 N틱 롤링버퍼(모멘텀 판단용)
        private const int BOOK_TICKER_MIN_INTERVAL_MS = 100; // 과부하 방지 스로틀(레거시 그리드봇과 동일 값 차용)
        private const int BOOK_TICKER_HISTORY_MAX = 50; // 심볼당 보관 틱 수 상한
        // 트레일링청산 감시 중인 심볼 - 이 심볼은 위 100ms 스로틀을 건너뛰고 매 이벤트 즉시 처리 +
        // 폴링(50ms 주기 확인) 대신 이벤트 도착 즉시 깨어나도록 신호(TCS)를 준다. 2026-07-20 사람과의
        // 대화 세션: giveback이 설정치를 초과하는 원인이 폴링 갭(및 이 스로틀)에서 비롯된다는 근본원인
        // 분석에 따라 도입 - 감시 대상이 아닌 심볼은 기존 스로틀 그대로 유지되어 부하 증가 없음.
        private readonly ConcurrentDictionary<string, byte> _activeExitMonitors = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _bookTickerTickSignals = new();

        // 청산 감시 시작 - 이 심볼은 이후 BookTicker 이벤트를 스로틀 없이 즉시 처리하고 신호를 보낸다.
        public void RegisterExitMonitor(string symbol)
        {
            _activeExitMonitors[symbol] = 1;
            _bookTickerTickSignals[symbol] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // 청산 감시 종료 - 반드시 finally에서 호출해 스로틀 예외를 해제한다(안 지우면 그 심볼이 계속
        // 무제한으로 처리되어 다음 후보 사이클에서 불필요한 부하가 남음).
        public void UnregisterExitMonitor(string symbol)
        {
            _activeExitMonitors.TryRemove(symbol, out _);
            _bookTickerTickSignals.TryRemove(symbol, out _);
        }

        // 다음 BookTicker 이벤트(또는 timeout)까지 대기 - 폴링 대신 이벤트 수신 즉시 깨어남.
        // RegisterExitMonitor를 먼저 호출하지 않았으면(등록 안 된 심볼) 즉시 리턴(호출부가 폴백 폴링하게 됨).
        public async Task WaitForNextBookTickerTickAsync(string symbol, TimeSpan timeout, CancellationToken ct)
        {
            if (!_bookTickerTickSignals.TryGetValue(symbol, out var tcs)) return;
            try { await Task.WhenAny(tcs.Task, Task.Delay(timeout, ct)); }
            catch (TaskCanceledException) { }
        }
        private readonly ConcurrentDictionary<string, decimal> _fundingRates = new();
        private readonly ConcurrentDictionary<string, DateTime> _nextFundingTimes = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastEventTimes = new();
        private readonly ConcurrentDictionary<string, decimal> _positionQty = new();
        private readonly ConcurrentDictionary<string, decimal> _lastKnownRealizedPnl = new(); // 심볼_방향 별 마지막 누적(cr)값 - 델타 계산용
        private readonly ConcurrentDictionary<string, decimal> _lastRealizedPnlDelta = new();   // 심볼_방향 별 가장 최근 델타(이번 이벤트에서 실현된 실제 손익 USDT)
        private readonly ConcurrentDictionary<string, DateTime> _lastRealizedPnlDeltaTime = new(); // 위 델타가 갱신된 시각 - 특정 청산 이후 새 델타 도착 대기용
        private readonly ConcurrentDictionary<string, TaskCompletionSource<decimal>> _fundingWaiters = new();
        private readonly ConcurrentDictionary<string, FundingHedger> _active = new();
        private TimeSpan _serverTimeOffset = TimeSpan.Zero;

        private StrategyConfig _config = StrategyConfig.LoadOrDefault();
        private decimal _dailyRealizedPnl = 0m;
        private DateTime _dailyResetDate = DateTime.UtcNow.Date;
        private bool _tradingHalted = false;

        public static DateTime? DebugForceFundingTime = null;
        //public static DateTime? DebugForceFundingTime = DateTime.UtcNow.AddMinutes(1);
        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public DateTime GetNextFundingTime(string symbol) =>
            _nextFundingTimes.TryGetValue(symbol, out var t) ? t : DateTime.MinValue;

        public DateTime GetLastEventTime(string symbol) =>
            _lastEventTimes.TryGetValue(symbol, out var t) ? t : DateTime.MinValue;

        // ─── Public API ───────────────────────────────────────────

        public void Start()
        {
            if (IsRunning) { UI("⚠️ [FUNDING-MGR] 이미 실행 중"); return; }
            _cts = new CancellationTokenSource();

            _socketClient = new BinanceSocketClient(opts =>
            {
                opts.ApiCredentials = Ob.client.ClientOptions.ApiCredentials;
            });

            _ = InitAsync(_cts.Token);
            UI("🚀 [FUNDING-MGR] 시작 (C전략)");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _ = CleanupSocketAsync();
            UI("🛑 [FUNDING-MGR] 종료 요청");
        }

        // ─── 소켓 인프라 (외부 접근용) ───────────────────────────

        public decimal GetMarkPrice(string symbol) =>
            _markPrices.TryGetValue(symbol, out var p) ? p : 0m;

        public decimal GetFundingRate(string symbol) =>
            _fundingRates.TryGetValue(symbol, out var r) ? r : 0m;

        public decimal GetPositionQty(string symbol, PositionSide side) =>
            _positionQty.TryGetValue($"{symbol}_{side}", out var q) ? q : 0m;

        // 소켓 캐시가 갱신 안 됐을 가능성(ListenKey 만료 등) 대비 REST 직접 조회 폴백
        public async Task<decimal> GetPositionQtyRestAsync(string symbol, PositionSide side)
        {
            try
            {
                var result = await Ob.client.UsdFuturesApi.Account.GetPositionInformationAsync(symbol, ct: CancellationToken.None);
                if (!result.Success || result.Data == null) return 0m;
                var pos = result.Data.FirstOrDefault(p => p.Symbol == symbol && p.PositionSide == side);
                decimal qty = pos != null ? Math.Abs(pos.Quantity) : 0m;
                if (qty > 0) _positionQty[$"{symbol}_{side}"] = qty; // 캐시 복구
                return qty;
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] GetPositionQtyRest({symbol}): {ex.Message}"); return 0m; }
        }

        // 청산 주문 직후 호출 - OnAccountUpdate로 새 실현손익 델타(실제 체결 기준)가 도착할 때까지
        // 짧게 폴링 대기 후 반환. sinceUtc 이후 갱신된 델타만 유효로 간주(이전 사이클의 값 재사용 방지).
        public async Task<decimal?> WaitForRealizedPnlDeltaAsync(string symbol, PositionSide side, DateTime sinceUtc, int timeoutMs = 3000)
        {
            string key = $"{symbol}_{side}";
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (_lastRealizedPnlDeltaTime.TryGetValue(key, out var t) && t >= sinceUtc
                    && _lastRealizedPnlDelta.TryGetValue(key, out var d))
                    return d;
                await Task.Delay(150);
            }
            return null;
        }

        public Task<decimal> WaitForFundingFeeAsync(string symbol)
        {
            var tcs = new TaskCompletionSource<decimal>(TaskCreationOptions.RunContinuationsAsynchronously);
            _fundingWaiters[symbol] = tcs;
            return tcs.Task;
        }

        public async Task<bool> PlaceOrderViaSocketAsync(
            string symbol, OrderSide side, FuturesOrderType type,
            decimal qty, PositionSide posSide, bool reduceOnly, bool isBait)
        {
            // 미끼(bait)만 실거래, 나머지(C전략 본진입/청산)는 항상 dry-run으로 강제.
            if (FundingHedger.DebugDryRun || !isBait)
            {
                UI($"🧪 [DRY-RUN{(isBait ? "" : "-비미끼강제")}] {symbol} {side} {posSide} qty={qty} reduceOnly={reduceOnly}");
                return true;
            }

            string posDir = reduceOnly
                ? (side == OrderSide.Buy ? "숏클로즈" : "롱클로즈")
                : (side == OrderSide.Buy ? "롱오픈" : "숏오픈");

            for (int i = 1; i <= 2; i++)
            {
                try
                {
                    UI($"📋 [WS-ORDER] {symbol} {posDir} qty={qty} ({i}/2)");

                    var result = await _socketClient!.UsdFuturesApi.Trading.PlaceOrderAsync(
                        symbol, side, type, qty, positionSide: posSide);

                    if (result.Success) return true;

                    string errMsg = result.Error?.Message ?? "";
                    if (errMsg.Contains("TradFi-Perps") || errMsg.Contains("Pre-Market") || errMsg.Contains("sign"))
                    {
                        UI($"🚫 [WS-ORDER] {symbol} 거래불가 즉시 중단: {errMsg}");
                        return false;
                    }
                    UI($"⚠️ [WS-ORDER] {symbol} 실패 ({i}/2): {errMsg}");
                }
                catch (Exception ex)
                {
                    UI($"❌ [WS-ORDER] {symbol} 예외 ({i}/2): {ex.Message}");
                }

                if (i < 2) await Task.Delay(200);
            }

            UI($"🚨 [WS-ORDER] {symbol} 최종 실패 → REST 폴백");
            return await PlaceOrderViaRestAsync(symbol, side, type, qty, posSide, reduceOnly);
        }

        // ─── Init ─────────────────────────────────────────────────

        private async Task InitAsync(CancellationToken ct)
        {
            try
            {
                await SyncServerTimeAsync();
                await SubscribeUserDataAsync(ct);
                _ = ListenKeyKeepAliveLoopAsync(ct);
                await RestorePositionsAsync(ct);
                _ = MainLoopAsync(ct);
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] InitAsync: {ex.Message}"); }
        }

        // ListenKey는 방치 시 약 60분 후 만료됨. 30분 주기로 keepalive 핑을 보내
        // 소켓 UserData 구독이 끊기지 않도록 유지. keepalive 실패(만료 등) 시 재구독.
        private async Task ListenKeyKeepAliveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(30), ct);
                    try
                    {
                        if (string.IsNullOrEmpty(_listenKey))
                        {
                            UI("⚠️ [FUNDING-MGR] ListenKey 없음 - 재구독 시도");
                            await SubscribeUserDataAsync(ct);
                            continue;
                        }
                        var result = await Ob.client.UsdFuturesApi.Account.KeepAliveUserStreamAsync(_listenKey, ct: ct);
                        if (result.Success)
                            UI("🕐 [FUNDING-MGR] ListenKey keepalive 성공");
                        else
                        {
                            UI($"⚠️ [FUNDING-MGR] ListenKey keepalive 실패: {result.Error?.Message} - 재구독");
                            await SubscribeUserDataAsync(ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        UI($"❌ [FUNDING-MGR] ListenKey keepalive 예외: {ex.Message} - 재구독 시도");
                        try { await SubscribeUserDataAsync(ct); } catch { }
                    }
                }
            }
            catch (TaskCanceledException) { }
        }

        private async Task SyncServerTimeAsync()
        {
            try
            {
                var before = DateTime.UtcNow;
                var result = await Ob.client.UsdFuturesApi.ExchangeData.GetServerTimeAsync();
                var after = DateTime.UtcNow;
                if (result.Success)
                {
                    _serverTimeOffset = result.Data - after + (after - before) / 2;
                    FundingHedger.ServerTimeOffset = _serverTimeOffset;
                    UI($"🕐 [FUNDING-MGR] 서버시간 동기화: 오프셋 {_serverTimeOffset.TotalMilliseconds:F0}ms");
                }
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] SyncServerTime: {ex.Message}"); }
        }

        // ─── UserData 구독 ────────────────────────────────────────

        private async Task SubscribeUserDataAsync(CancellationToken ct)
        {
            try
            {
                // 기존 구독 있으면 정리 후 재구독
                if (_userDataSub != null)
                {
                    try { await _socketClient!.UnsubscribeAsync(_userDataSub); } catch { }
                    _userDataSub = null;
                }

                var listenKeyResult = await Ob.client.UsdFuturesApi.Account.StartUserStreamAsync(ct: ct);
                if (!listenKeyResult.Success)
                {
                    UI($"❌ [FUNDING-MGR] ListenKey 발급 실패: {listenKeyResult.Error?.Message}");
                    return;
                }
                _listenKey = listenKeyResult.Data;

                var sub = await _socketClient!.UsdFuturesApi.Account.SubscribeToUserDataUpdatesAsync(
                    listenKeyResult.Data,
                    onAccountUpdate: msg => OnAccountUpdate(msg.Data),
                    onOrderUpdate: null,
                    onListenKeyExpired: ev =>
                    {
                        UI("⚠️ [FUNDING-MGR] ListenKey 만료 - 재구독 시도");
                        _listenKey = null;
                        SubscribeUserDataAsync(_cts?.Token ?? CancellationToken.None); // fire-and-forget
                    },
                    onStrategyUpdate: null,
                    onGridUpdate: null,
                    onConditionalOrderTriggerRejectUpdate: null,
                    ct: ct);

                if (sub.Success) { _userDataSub = sub.Data; UI("✅ [FUNDING-MGR] UserData 구독 완료"); }
                else UI($"❌ [FUNDING-MGR] UserData 구독 실패: {sub.Error?.Message}");
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] SubscribeUserData: {ex.Message}"); }
        }

        private void OnAccountUpdate(BinanceFuturesStreamAccountUpdate data)
        {
            try
            {
                // 포지션 수량 캐시 갱신 (항상)
                if (data.UpdateData?.Positions != null)
                    foreach (var pos in data.UpdateData.Positions)
                        _positionQty[$"{pos.Symbol}_{pos.PositionSide}"] = Math.Abs(pos.Quantity);

                // 실현손익 누적 (사유 무관 - 청산/펀딩비 전부 반영) + 일일 서킷브레이커
                AccumulateDailyPnlAndCheckHalt(data);

                // FundingFee 이벤트 - 정산 시각 동일하므로 전체 트리거
                if (data.UpdateData?.Reason != AccountUpdateReason.FundingFee) return;

                // 트리거 최우선
                foreach (var kv in _fundingWaiters.ToArray())
                    if (_fundingWaiters.TryRemove(kv.Key, out var tcs))
                        tcs.TrySetResult(0m);

                // 로깅 비동기
                _ = Task.Run(() =>
                {
                    if (data.UpdateData.Positions != null)
                        foreach (var pos in data.UpdateData.Positions)
                            UI($"💸 {pos.Symbol} 펀딩비: {pos.RealizedPnl:+0.0000;-0.0000}$");
                });
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] OnAccountUpdate: {ex.Message}"); }
        }

        // 일일 실현손익 누적(UTC 기준 하루 단위 리셋) + DailyLossLimitUsdt 초과 시 거래 중단.
        // 서킷브레이커 발동 여부는 자동화(Claude Code)가 못 건드리는 안전장치 값 기준.
        //
        // 주의: pos.RealizedPnl(바이낸스 "cr" 필드)은 이벤트당 증분값이 아니라
        // 그 심볼/포지션 생애주기 누적값이다. 매 이벤트마다 그대로 더하면 같은 손익이
        // 여러 번 중복 카운트된다. 심볼별 "마지막으로 본 누적값"과의 차이(delta)만 더해야 한다.
        private void AccumulateDailyPnlAndCheckHalt(BinanceFuturesStreamAccountUpdate data)
        {
            try
            {
                if (DateTime.UtcNow.Date != _dailyResetDate)
                {
                    UI($"🔄 [FUNDING-MGR] 일일 손익 리셋 (전일 누적: {_dailyRealizedPnl:+0.00;-0.00}$)");
                    _dailyRealizedPnl = 0m;
                    _dailyResetDate = DateTime.UtcNow.Date;
                    _tradingHalted = false;
                    _lastKnownRealizedPnl.Clear(); // 새 하루 시작 - 기준값도 리셋
                }

                if (data.UpdateData?.Positions == null) return;
                foreach (var pos in data.UpdateData.Positions)
                {
                    string key = $"{pos.Symbol}_{pos.PositionSide}";
                    decimal current = pos.RealizedPnl;
                    decimal previous = _lastKnownRealizedPnl.GetValueOrDefault(key, current);

                    decimal delta = current - previous;
                    // 포지션이 완전히 청산되면 거래소 쪽에서 cr이 0으로 리셋되는 경우가 있어
                    // 이때 delta가 큰 음수로 튀는 걸 방지 (급감 시 리셋으로 간주하고 기준값만 갱신)
                    if (delta < 0 && Math.Abs(current) < 0.0001m)
                    {
                        _lastKnownRealizedPnl[key] = current;
                        continue;
                    }

                    if (delta != 0)
                    {
                        _dailyRealizedPnl += delta;
                        _lastRealizedPnlDelta[key] = delta;
                        _lastRealizedPnlDeltaTime[key] = DateTime.UtcNow;
                        UI($"📒 [FUNDING-MGR] {pos.Symbol} 실현손익 반영: {delta:+0.0000;-0.0000}$ (누적cr={current:F4}$) → 일일누적={_dailyRealizedPnl:+0.00;-0.00}$ / 한도={_config.DailyLossLimitUsdt:F2}$");
                    }
                    _lastKnownRealizedPnl[key] = current;
                }

                if (!_tradingHalted && _dailyRealizedPnl <= -_config.DailyLossLimitUsdt)
                {
                    _tradingHalted = true;
                    UI($"🚨🚨 [FUNDING-MGR] 일일 손실 한도 도달! 누적={_dailyRealizedPnl:F2}$ / 한도={_config.DailyLossLimitUsdt:F2}$ - 신규 진입 중단 (내일 UTC 리셋까지)");
                }
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] AccumulateDailyPnl: {ex.Message}"); }
        }

        // ─── MarkPrice 구독 ───────────────────────────────────────

        private async Task SubscribeMarkPricesAsync(IEnumerable<string> symbols, CancellationToken ct)
        {
            try
            {
                var list = symbols.ToList();
                if (!list.Any()) return;

                if (_markPriceSub != null)
                    await _socketClient!.UnsubscribeAsync(_markPriceSub);

                var firstSymbol = list[0];

                var sub = await _socketClient!.UsdFuturesApi.ExchangeData
                    .SubscribeToMarkPriceUpdatesAsync(
                        list,
                        1000,
                        (DataEvent<BinanceFuturesUsdtStreamMarkPrice> u) =>
                        {
                            if (u.Data.Symbol == firstSymbol)
                            {
                                _serverTimeOffset = u.Data.EventTime - DateTime.UtcNow;
                                FundingHedger.ServerTimeOffset = _serverTimeOffset;
                            }
                            _markPrices[u.Data.Symbol] = u.Data.MarkPrice;
                            _fundingRates[u.Data.Symbol] = u.Data.FundingRate ?? 0m;
                            _nextFundingTimes[u.Data.Symbol] = DebugForceFundingTime ?? u.Data.NextFundingTime;
                            _lastEventTimes[u.Data.Symbol] = u.Data.EventTime;
                            _priceHistory.AddOrUpdate(u.Data.Symbol,
                            _ => new List<(DateTime, decimal)> { (u.Data.EventTime, u.Data.MarkPrice) },
                            (_, list) => { lock (list) { list.Add((u.Data.EventTime, u.Data.MarkPrice)); } return list; });
                        },
                        ct);

                if (sub.Success) { _markPriceSub = sub.Data; UI($"✅ [FUNDING-MGR] MarkPrice 구독: {string.Join(",", list)}"); }
                else UI($"❌ [FUNDING-MGR] MarkPrice 구독 실패: {sub.Error?.Message}");
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] SubscribeMarkPrices: {ex.Message}"); }
        }

        public List<(DateTime Time, decimal Price)> GetPriceHistory(string symbol) => _priceHistory.TryGetValue(symbol, out var h) ? h : new();

        // ─── BookTicker 구독 (실시간 중간가, 트레일링/모멘텀 판단용) ─────

        private async Task SubscribeBookTickerAsync(IEnumerable<string> symbols, CancellationToken ct)
        {
            try
            {
                var list = symbols.ToList();
                if (!list.Any()) return;

                if (_bookTickerSub != null)
                    await _socketClient!.UnsubscribeAsync(_bookTickerSub);

                var sub = await _socketClient!.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(
                    list,
                    x =>
                    {
                        try
                        {
                            var symbol = x.Data.Symbol;
                            bool isExitMonitored = _activeExitMonitors.ContainsKey(symbol);

                            // 과부하 방지 - 심볼당 최소 간격 이하로 온 업데이트는 스킵 (최신값은 어차피 다음 이벤트로 덮임)
                            // 단, 트레일링청산 감시 중인 심볼(RegisterExitMonitor)은 이 스로틀을 건너뛰고
                            // 매 이벤트를 즉시 처리한다 - giveback 초과 근본원인(폴링/스로틀 지연) 완화용.
                            if (!isExitMonitored
                                && _lastBookTickerProcessTime.TryGetValue(symbol, out var last)
                                && (DateTime.UtcNow - last).TotalMilliseconds < BOOK_TICKER_MIN_INTERVAL_MS)
                                return;
                            _lastBookTickerProcessTime[symbol] = DateTime.UtcNow;

                            decimal mid = (x.Data.BestBidPrice + x.Data.BestAskPrice) / 2m;
                            var now = DateTime.UtcNow;
                            _bookTicker[symbol] = (x.Data.BestBidPrice, x.Data.BestAskPrice, now);

                            _bookTickerHistory.AddOrUpdate(symbol,
                                _ => new List<(DateTime, decimal)> { (now, mid) },
                                (_, list) =>
                                {
                                    lock (list)
                                    {
                                        list.Add((now, mid));
                                        if (list.Count > BOOK_TICKER_HISTORY_MAX)
                                            list.RemoveRange(0, list.Count - BOOK_TICKER_HISTORY_MAX);
                                    }
                                    return list;
                                });

                            // 청산 감시 중이면 대기 중인 WaitForNextBookTickerTickAsync를 즉시 깨운다.
                            if (isExitMonitored && _bookTickerTickSignals.TryGetValue(symbol, out var tcs))
                            {
                                var freshTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                if (_bookTickerTickSignals.TryUpdate(symbol, freshTcs, tcs))
                                    tcs.TrySetResult(true);
                            }
                        }
                        catch { }
                    },
                    ct);

                if (!sub.Success) UI($"❌ [FUNDING-MGR] BookTicker 구독 실패: {sub.Error?.Message}");
                else _bookTickerSub = sub.Data;
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] SubscribeBookTicker: {ex.Message}"); }
        }

        private async Task UnsubscribeBookTickerAsync()
        {
            try
            {
                if (_bookTickerSub != null)
                {
                    await _socketClient!.UnsubscribeAsync(_bookTickerSub);
                    _bookTickerSub = null;
                }
                _bookTicker.Clear();
                _bookTickerHistory.Clear();
                _lastBookTickerProcessTime.Clear();
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] UnsubscribeBookTicker: {ex.Message}"); }
        }

        // 실시간 중간가 - 없으면 MarkPrice로 폴백(구독 직후 아직 첫 이벤트 안 왔을 때 등)
        public decimal GetMidPrice(string symbol)
        {
            if (_bookTicker.TryGetValue(symbol, out var bt) && bt.Bid > 0 && bt.Ask > 0)
                return (bt.Bid + bt.Ask) / 2m;
            return GetMarkPrice(symbol);
        }

        // 시장가 주문 시 실제 체결 방향 가격 - Buy는 Ask, Sell은 Bid 기준 (mid보다 스프레드만큼 불리)
        // 진입/청산 시뮬레이션 가격을 mid 대신 이걸로 써야 paper 지표가 실제 체결에 가까워짐
        public decimal GetSidePrice(string symbol, OrderSide side)
        {
            if (_bookTicker.TryGetValue(symbol, out var bt) && bt.Bid > 0 && bt.Ask > 0)
                return side == OrderSide.Buy ? bt.Ask : bt.Bid;
            return GetMarkPrice(symbol); // 폴백
        }

        // 최근 N틱 중간가 히스토리 (모멘텀 연속성 판단용) - 데이터 없으면 빈 리스트
        public List<(DateTime Time, decimal Mid)> GetBookTickerHistory(string symbol) =>
            _bookTickerHistory.TryGetValue(symbol, out var h) ? h : new();

        private async Task UnsubscribeMarkPricesAsync()
        {
            try
            {
                if (_markPriceSub != null)
                {
                    await _socketClient!.UnsubscribeAsync(_markPriceSub);
                    _markPriceSub = null;
                }
                _markPrices.Clear();
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] UnsubscribeMarkPrices: {ex.Message}"); }
        }

        // ─── 포지션 복구 ──────────────────────────────────────────

        private async Task RestorePositionsAsync(CancellationToken ct)
        {
            try
            {
                var posResult = await Ob.client.UsdFuturesApi.Account.GetPositionInformationAsync(ct: ct);
                if (!posResult.Success || posResult.Data == null) return;

                var activePos = posResult.Data.Where(p => Math.Abs(p.Quantity) > 0).ToList();
                if (!activePos.Any()) return;

                var symbols = activePos.Select(p => p.Symbol).Distinct().ToList();
                await SubscribeMarkPricesAsync(symbols, ct);
                await Task.Delay(500);

                foreach (var pos in activePos)
                {
                    if (_active.ContainsKey(pos.Symbol)) continue;

                    var markResult = await Ob.client.UsdFuturesApi.ExchangeData.GetMarkPriceAsync(pos.Symbol);
                    if (!markResult.Success) continue;

                    decimal fundingRate = markResult.Data.FundingRate ?? 0m;
                    DateTime nextFunding = markResult.Data.NextFundingTime;
                    decimal qty = Math.Abs(pos.Quantity);

                    UI($"🔄 [FUNDING-MGR] {pos.Symbol} 복구: {pos.PositionSide} {qty} → C청산 흐름 재개");

                    var hedger = new FundingHedger(pos.Symbol, fundingRate, nextFunding, qty * pos.MarkPrice, this, _config);
                    hedger.RestoreState(qty);
                    _active[pos.Symbol] = hedger;
                    _ = RunCloseAsync(hedger, ct);
                }
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] RestorePositions: {ex.Message}"); }
        }

        // ─── Main Loop ────────────────────────────────────────────

        private async Task MainLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await SyncServerTimeAsync();

                    if (DebugForceFundingTime == null)
                    {
                        DateTime now = DateTime.UtcNow;
                        DateTime nextHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
                        // 실제로 계산에 쓰이는 건 정산 직전 30~40초 이내 데이터뿐(직전추세 필터가 30틱=30초 참조,
                        // 스냅샷A/B도 T-3s/T-1.5s). T-5분부터 구독해봐야 그 앞 4분+는 로그만 부풀릴 뿐 안 쓰임.
                        // 레버리지 설정(REST) 여유까지 감안해 T-1분으로 단축.
                        DateTime scanTime = nextHour.AddMinutes(-1);
                        TimeSpan waitTime = scanTime - DateTime.UtcNow;

                        if (waitTime > TimeSpan.Zero)
                        {
                            UI($"⏳ [FUNDING-MGR] 다음 스캔: {scanTime:HH:mm:ss} UTC ({waitTime.TotalMinutes:F0}분 후)");
                            await Task.Delay(waitTime, ct);
                        }
                    }
                    else
                        UI("🧪 [TEST] DebugForceFundingTime 설정됨");

                    // 매 사이클 시작 시 config 재로드 (Claude Code가 파일만 수정하면 재시작 없이 반영)
                    _config = StrategyConfig.LoadOrDefault();

                    if (_tradingHalted)
                    {
                        UI($"⛔ [FUNDING-MGR] 일일 손실한도로 거래 중단 중 (누적={_dailyRealizedPnl:F2}$) - 사이클 스킵");
                        await Task.Delay(TimeSpan.FromMinutes(5), ct);
                        continue;
                    }

                    try { await ScanAndDispatchAsync(ct); }
                    catch (Exception ex) { UI($"❌ [FUNDING-MGR] 스캔 사이클 예외: {ex.Message}"); }

                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                }
            }
            catch (TaskCanceledException) { }
        }

        private async Task ScanAndDispatchAsync(CancellationToken ct)
        {
            var premiumResult = await Ob.client.UsdFuturesApi.ExchangeData.GetMarkPricesAsync(ct: ct);
            if (!premiumResult.Success || premiumResult.Data == null)
            {
                UI($"❌ [FUNDING-MGR] 마크프라이스 조회 실패: {premiumResult.Error?.Message}");
                return;
            }

            var withFunding = premiumResult.Data
                .Where(p => p.Symbol.EndsWith("USDT") && p.FundingRate.HasValue && p.FundingRate.Value != 0)
                .ToList();

            if (!withFunding.Any()) { UI("⚠️ [FUNDING-MGR] 펀딩비 데이터 없음"); return; }

            DateTime soonestFunding = withFunding.Min(p => p.NextFundingTime);
            DateTime effectiveFundingTime = DebugForceFundingTime ?? soonestFunding;

            // 최소 펀딩비 임계값(MinFundingPct) 이상인 코인을 우선 후보로 채택.
            // 미달 코인만 있으면 어쩔 수 없이 채워넣되(백필) 로그로 구분 표시 —
            // 표본 수집 단계라 완전히 스킵하지 않고 낮은 우선순위로 채운다.
            // 단, 백필도 무한정 허용하지 않는다: |펀딩비|가 왕복 수수료 추정치(ROUND_TRIP_FEE_PCT)도
            // 못 넘는 코인은 반락 기대폭이 수수료조차 못 건질 가능성이 높아 애초에 후보에서 제외한다
            // (편입 시 확정손실에 가까운 트레이드를 억지로 채우지 않기 위함 - 이 경우 그 사이클은 후보가 모자란 채로 진행).
            // tick/price 비율 필터(2026-07-15 신규): GWEIUSDT 사례로 발견 - tickSize가 가격 대비 크면
            // (예: 0.21%) 진입+청산 왕복만으로 구조적 스프레드 손실이 상시 발생함. 시장 상황과 무관하게
            // 절대 안 좁혀지는 비용이라 사전에 걸러낸다. THEUSDT/1000XECUSDT급 giveback 극단치는 이 비율이
            // 정상범위(0.02%대)였음이 확인됐으므로(별개 문제), 이 필터로 그 문제까지 잡히진 않는다.
            var beforeTickFilter = withFunding
                 .Where(p => Math.Abs((p.NextFundingTime - soonestFunding).TotalMinutes) < 5)
                 .ToList();

            // ratio==0은 "정보 없음"(GetTickSize 조회 실패)이라 안전하게 통과시킴
            var tickExcluded = beforeTickFilter
                 .Select(p => (Item: p, RatioPct: FundingHedger.GetTickPriceRatioPct(p.Symbol, p.MarkPrice)))
                 .Where(x => x.RatioPct > 0 && x.RatioPct > _config.MaxTickPriceRatioPct)
                 .ToList();
            if (tickExcluded.Any())
                UI($"🔍 [FUNDING-MGR] tick/price 비율 초과로 제외({_config.MaxTickPriceRatioPct}% 초과): " +
                   string.Join(", ", tickExcluded.Select(e => $"{e.Item.Symbol}({e.RatioPct:F3}%)")));

            var excludedSymbols = tickExcluded.Select(x => x.Item.Symbol).ToHashSet();
            var pool = beforeTickFilter
                 .Where(p => !excludedSymbols.Contains(p.Symbol))
                 .OrderByDescending(p => Math.Abs(p.FundingRate!.Value))
                 .ToList();

            var strong = pool.Where(p => Math.Abs(p.FundingRate!.Value) >= _config.MinFundingPct / 100m).ToList();
            var weak = pool
                 .Where(p => Math.Abs(p.FundingRate!.Value) < _config.MinFundingPct / 100m
                          && Math.Abs(p.FundingRate!.Value) >= ROUND_TRIP_FEE_PCT / 100m)
                 .ToList();

            var candidates = strong.Take(TEST_CANDIDATE_COUNT).ToList();
            if (candidates.Count < TEST_CANDIDATE_COUNT)
            {
                int need = TEST_CANDIDATE_COUNT - candidates.Count;
                var backfill = weak.Take(need).ToList();
                if (backfill.Any())
                    UI($"⚠️ [FUNDING-MGR] |펀딩비|≥{_config.MinFundingPct}% 후보 부족({candidates.Count}개) - 저펀딩 {backfill.Count}개 백필(수수료{ROUND_TRIP_FEE_PCT}% 이상만): " +
                       string.Join(", ", backfill.Select(c => $"{c.Symbol}({c.FundingRate:P3})")));
                candidates.AddRange(backfill);

                int stillShort = TEST_CANDIDATE_COUNT - candidates.Count;
                if (stillShort > 0)
                    UI($"⚠️ [FUNDING-MGR] 수수료 하한({ROUND_TRIP_FEE_PCT}%)도 못 넘는 코인들은 백필 제외 - 이번 사이클 후보 {candidates.Count}개로 진행({stillShort}개 부족)");
            }

            UI($"📊 [FUNDING-MGR] 다음 펀딩 {soonestFunding:HH:mm:ss} / 후보 {candidates.Count}개: " +
               string.Join(", ", candidates.Select(c => $"{c.Symbol}({c.FundingRate:P3})")));

            var validCandidates = new List<(string Symbol, decimal FundingRate, DateTime FundingTime)>();
            foreach (var c in candidates)
            {
                bool ok = await SetLeverageAsync(c.Symbol, ct);
                if (ok) validCandidates.Add((c.Symbol, c.FundingRate!.Value, effectiveFundingTime));
                else UI($"⚠️ [FUNDING-MGR] {c.Symbol} 레버리지 설정 실패 - 제외");
            }

            if (!validCandidates.Any()) { UI("⚠️ [FUNDING-MGR] 유효 후보 없음"); return; }

            await SubscribeMarkPricesAsync(validCandidates.Select(c => c.Symbol), ct);
            await SubscribeBookTickerAsync(validCandidates.Select(c => c.Symbol), ct);
            // 첫 MarkPrice 이벤트 수신까지 대기
            await WaitForMarkPriceEventAsync(validCandidates[0].Symbol, 9999, ct);

            // 이미 _lastEventTimes 채워진 상태 → EventTime 기준으로 대기
            if (!_lastEventTimes.TryGetValue(validCandidates[0].Symbol, out var lastEvent)
                || !_nextFundingTimes.TryGetValue(validCandidates[0].Symbol, out var nextFunding)
                || (nextFunding - lastEvent).TotalSeconds < DISPATCH_BEFORE_FUNDING_SEC)
            {
                UI("⚠️ [FUNDING-MGR] 배분 시점 이미 경과 - 스킵");
                await UnsubscribeMarkPricesAsync();
                await UnsubscribeBookTickerAsync();
                return;
            }

            UI($"⏳ [FUNDING-MGR] 배분 시점까지 대기 (T-{DISPATCH_BEFORE_FUNDING_SEC}s) | 잔여={nextFunding - lastEvent:mm\\:ss\\.fff}");
            await WaitForMarkPriceEventAsync(validCandidates[0].Symbol, DISPATCH_BEFORE_FUNDING_SEC, ct);

            await DispatchAsync(validCandidates, ct);
        }

        // ─── 자금 배분 및 실행 (C전략) ────────────────────────────

        private async Task DispatchAsync(
            List<(string Symbol, decimal FundingRate, DateTime FundingTime)> candidates,
            CancellationToken ct)
        {
            // 테스트: 잔고/한도 체크 없이 TEST_NOTIONAL 고정
            var hedgers = candidates
                .Where(c => !_active.ContainsKey(c.Symbol))
                .Select(c =>
                {
                    var h = new FundingHedger(c.Symbol, c.FundingRate, c.FundingTime, TEST_NOTIONAL, this, _config);
                    _active[c.Symbol] = h;
                    return h;
                })
                .ToList();

            if (!hedgers.Any()) { UI("⚠️ [FUNDING-MGR] 배정된 코인 없음"); return; }

            UI($"🎯 [FUNDING-MGR] {hedgers.Count}개 코인 C전략 준비");

            // T-3s 스냅샷 A (분석용 로그 유지)
            await WaitForMarkPriceEventAsync(hedgers[0].Symbol, 3.0, ct);
            foreach (var h in hedgers) h.TakeSnapshotA();

            // T-1.5s 스냅샷 B + 수량계산 + 직전추세 필터 (분석용 로그 + 사전 수량계산 유지)
            await WaitForMarkPriceEventAsync(hedgers[0].Symbol, 1.5, ct);
            foreach (var h in hedgers) h.PrepareEntry();

            // PrepareEntry 실패(추세필터/마크가격없음/펀딩비소멸 등)한 hedger는 RunCloseAsync가 호출 안 돼
            // _active에서 영구히 제거되지 않는 누수(leak)가 발생함 - 여기서 즉시 제거해 다음 사이클부터 재후보 가능하게 함.
            // 그 중 PreTrendSkipPct로 스킵된 케이스는 실거래 없이 사후 시뮬레이션 로깅(skipped_results.jsonl)을 백그라운드로 시작.
            var notPrepared = hedgers.Where(h => !h.IsPrepared).ToList();
            var skipSimTasks = new List<Task>();
            if (notPrepared.Any())
            {
                foreach (var h in notPrepared)
                {
                    _active.TryRemove(h.Symbol, out _);
                    UI($"🧹 [FUNDING-MGR] {h.Symbol} PrepareEntry 실패 - _active 누수 방지 제거");
                    if (h.SkippedByTrendFilter)
                        skipSimTasks.Add(h.LogSkippedOutcomeAsync(ct));
                }
            }

            if (hedgers.All(h => !h.IsPrepared))
            {
                UI("⚠️ [FUNDING-MGR] 모든 후보 스킵됨");
                // 스킵 시뮬레이션이 MarkPrice 구독에 의존하므로, 구독 해제 전에 완료까지 대기
                if (skipSimTasks.Any()) await Task.WhenAll(skipSimTasks);
                await UnsubscribeMarkPricesAsync();
                await UnsubscribeBookTickerAsync();
                return;
            }

            var prepared = hedgers.Where(h => h.IsPrepared).ToList();

            // 미끼 선정: |펀딩비| 최소 순으로 순회하며 실제 진입 가능한(수량>0) 첫 후보를 채택.
            // 1차 시도(최소금액×1.1)에서 전부 실패하면(고가 코인뿐인 경우) 후보군 안에서
            // 금액을 단계적으로 올려가며 재시도 - 반드시 후보 중 하나는 미끼로 확보.
            // 미끼 진입 자체가 실패(거래불가 등)하면 즉시 다음 후보로 넘어간다.
            var baitCandidates = prepared.OrderBy(h => Math.Abs(h.FundingRate)).ToList();
            decimal[] baitMultipliers = { 1.1m, 3m, 10m, 30m, 100m };
            FundingHedger? bait = null;

            // 정산 T-1s까지 대기한 뒤 미끼 진입 시도 - 진입 자체가 실패(거래불가 등)하면
            // ResetBaitMode 후 다음 후보로 즉시 재시도한다. 실패한 심볼은 이번 사이클 미끼 후보에서 제외.
            await WaitForMarkPriceEventAsync(prepared[0].Symbol, 1.0, ct);

            foreach (var mult in baitMultipliers)
            {
                // baitCandidates를 직접 foreach하면서 아래서 Remove하면 "Collection was modified" 예외 발생
                // (스캔 사이클 예외로 반복 재현되던 원인) - 스냅샷(ToList)을 순회하고 원본만 수정한다.
                foreach (var c in baitCandidates.ToList())
                {
                    if (c.IsBait) continue; // 이미 이번 루프에서 시도했다 실패한 후보는 SetBaitMode/ResetBaitMode로 상태 관리되므로 중복 스킵

                    decimal minNotional = FundingHedger.GetMinNotional(c.Symbol) * mult;
                    c.SetBaitMode(minNotional);
                    if (!c.RecalcBaitQty())
                    {
                        UI($"⚠️ [FUNDING-MGR] {c.Symbol} 미끼 수량계산 실패(금액 {minNotional:F2}U, 배수 x{mult}) - 다음으로");
                        c.ResetBaitMode();
                        continue;
                    }

                    UI($"🪤 [FUNDING-MGR] 미끼 지정: {c.Symbol} (금액 {minNotional:F2}U, 배수 x{mult})");
                    bool entered = await c.ExecuteBaitEntryAsync(ct);
                    if (entered)
                    {
                        bait = c;
                        break;
                    }

                    // 진입 자체가 실패(거래불가 등) - 이 심볼은 이번 사이클 미끼 후보에서 완전히 제외하고 다음 후보 시도
                    UI($"🚨 [FUNDING-MGR] {c.Symbol} 미끼 진입 실패 - 다음 후보로 즉시 재시도");
                    c.ResetBaitMode();
                    baitCandidates.Remove(c); // 재시도 루프(배수 상승)에서도 다시 안 뽑히게 제외
                }
                if (bait != null) break;
                if (!baitCandidates.Any()) break; // 모든 후보 소진 - 배수 올려도 의미없음
            }
            if (bait == null)
            {
                UI("🚨 [FUNDING-MGR] 모든 후보/배수 시도에도 미끼 확보 실패 - 미끼 없이 진행(타임아웃 폴백)");
            }
            // 주의: 미끼 진입은 위 루프 안에서 ExecuteBaitEntryAsync로 이미 1회 실행 완료됨.
            // (이전 버전에 있던 "루프 종료 후 재차 ExecuteBaitEntryAsync 호출" 블록은 중복 주문 버그라 제거함)

            // 각 hedger가 FundingFee 이벤트 수신 → 반락모멘텀 확인 → C진입 → 되돌림/타임아웃 → 청산
            await Task.WhenAll(prepared.Select(h => RunCloseAsync(h, ct)));
            if (skipSimTasks.Any()) await Task.WhenAll(skipSimTasks); // 스킵 시뮬레이션도 구독 해제 전 완료 대기 (혼합 케이스 대응)

            // 반락 추적 로그(청산 후 15s) + 스킵 검증 로그(15s) 위한 유예
            await Task.Delay(30000, ct);

            await UnsubscribeMarkPricesAsync();
            await UnsubscribeBookTickerAsync();
            UI("✅ [FUNDING-MGR] 이번 사이클 완료");
        }

        private async Task RunCloseAsync(FundingHedger hedger, CancellationToken ct)
        {
            try { await hedger.CloseAsync(ct); }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] RunClose {hedger.Symbol}: {ex.Message}"); }
            finally { _active.TryRemove(hedger.Symbol, out _); }
        }

        // ─── REST 폴백 주문 ───────────────────────────────────────

        private async Task<bool> PlaceOrderViaRestAsync(
            string symbol, OrderSide side, FuturesOrderType type,
            decimal qty, PositionSide posSide, bool reduceOnly)
        {
            try
            {
                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol, side, type, qty, positionSide: posSide);
                if (result.Success) return true;
                UI($"❌ [REST-ORDER] {symbol} 실패: {result.Error?.Message}");
                return false;
            }
            catch (Exception ex) { UI($"❌ [REST-ORDER] {symbol} 예외: {ex.Message}"); return false; }
        }

        // ─── Helpers ──────────────────────────────────────────────

        private async Task WaitForMarkPriceEventAsync(string symbol, double offsetSeconds, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_nextFundingTimes.TryGetValue(symbol, out var next)
                        && _lastEventTimes.TryGetValue(symbol, out var eventTime))
                    {
                        if (offsetSeconds >= 9999) return; // 첫 이벤트 수신 확인용

                        var remain = (next - eventTime).TotalSeconds;
                        if (remain < 2)
                        {
                            var targetTime = next.AddSeconds(-offsetSeconds);
                            while (DateTime.UtcNow + _serverTimeOffset < targetTime && !ct.IsCancellationRequested)
                                await Task.Delay(5, ct);
                            return;
                        }
                        // 스냅샷 등 2초 이상 남은 오프셋 대기
                        if (remain <= offsetSeconds) return;
                    }
                    await Task.Delay(100, ct);
                }
            }
            catch (TaskCanceledException) { }
        }

        private async Task WaitUntilAsync(DateTime targetUtc, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TimeSpan remain = targetUtc - (DateTime.UtcNow + _serverTimeOffset);
                if (remain.TotalMilliseconds <= 0) return;
                int delayMs = remain.TotalSeconds > 5 ? 1000 : 50;
                try { await Task.Delay(delayMs, ct); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task<bool> SetLeverageAsync(string symbol, CancellationToken ct)
        {
            try
            {
                var marginResult = await Ob.client.UsdFuturesApi.Account.ChangeMarginTypeAsync(
                    symbol: symbol, marginType: FuturesMarginType.Cross, ct: ct);
                if (!marginResult.Success)
                {
                    string marginError = marginResult.Error?.Message ?? "";
                    if (!marginError.Contains("No need to change") && !marginError.Contains("already"))
                        UI($"⚠️ [FUNDING-MGR] {symbol} 크로스마진 설정 실패: {marginError}");
                }

                var result = await Ob.client.UsdFuturesApi.Account.ChangeInitialLeverageAsync(symbol, LEVERAGE, ct: ct);
                if (result.Success) return true;
                string error = result.Error?.Message ?? "";
                return error.Contains("No need to change") || error.Contains("already");
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] SetLeverage({symbol}): {ex.Message}"); return false; }
        }

        private async Task CleanupSocketAsync()
        {
            try
            {
                if (_socketClient != null)
                {
                    await _socketClient.UnsubscribeAllAsync();
                    _socketClient.Dispose();
                    _socketClient = null;
                }
            }
            catch (Exception ex) { UI($"❌ [FUNDING-MGR] CleanupSocket: {ex.Message}"); }
        }

        private void UI(string msg) { try { Ob.ui?.SetText(msg); } catch { } }
    }
}