using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Futures.Socket;
using CryptoExchange.Net.Objects.Sockets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CoinSvr
{
    // ⚠️ 신규 독립 모듈(2026-07-16 초안, 미검증) - CoinSvr_Scalp/FundingHedger 어느 쪽 코드도 건드리지 않음.
    // 목적: 펀딩비와 무관하게, 가격+거래대금 동시 스파이크가 뜬 심볼을 모멘텀 방향으로 짧게(1~5분) 추격
    //       진입 후 트레일링스탑/고정SL/타임아웃 중 먼저 오는 조건으로 정리하는 "단순 테스트 로직".
    // 자본/리스크(DailyLossLimitUsdt)는 FundingHedger와 완전히 별도 카운터로 관리.
    // 전제: 계정 포지션모드는 FundingHedger와 동일 계정을 쓴다는 가정 하에 Hedge Mode(PositionSide.Long/Short)로 작성함.
    //       one-way 모드 계정이면 PlaceOrderAsync의 positionSide 인자를 빼고 써야 함 - 배포 전 계정 설정 확인 필요.
    public sealed class SpikeScalpManager
    {
        // FundingHedger.DebugDryRun과 완전 분리된 SpikeScalp 전용 플래그.
        // 기본값 true(안전) - 실거래 전환은 반드시 명시적으로 false 세팅해야 함.
        public static bool DebugDryRun = true;

        private readonly SpikeScalpConfig _cfg;
        private BinanceSocketClient? _socketClient;
        private UpdateSubscription? _markPriceSub;
        private UpdateSubscription? _tickerSub;
        private UpdateSubscription? _bookTickerSub;

        // ── 1단계: 광역 스캔용 롤링 버퍼 (심볼당 고정폭 큐 - 무제한 List 아님, 300+심볼 메모리 방지) ──
        private readonly ConcurrentDictionary<string, Queue<(DateTime Time, decimal Price)>> _wideHistory = new();
        private readonly ConcurrentDictionary<string, Queue<(DateTime Time, decimal QuoteVolume)>> _wideVolumeHistory = new();
        // 24hr 티커가 이미 실어오는 PriceChangePercent를 심볼별 최신값으로만 캐시(24h 이력 버퍼링 불필요).
        // 참고용으로만 로깅 - 실제 진입 필터는 아래 1h 기준(_trend1h)을 사용(2026-07-18 백테스트로 확정).
        private readonly ConcurrentDictionary<string, decimal> _trend24h = new();
        // 1h 추세 롤링 버퍼 + 최신 계산값 캐시(진입 필터에 실제로 사용).
        private readonly ConcurrentDictionary<string, Queue<(DateTime Time, decimal Price)>> _trend1hHistory = new();
        private readonly ConcurrentDictionary<string, decimal> _trend1h = new();

        // ── 2단계: 정밀추적 대상 ──
        private readonly ConcurrentDictionary<string, SpikeTarget> _activeTargets = new();
        private readonly ConcurrentDictionary<string, DateTime> _cooldown = new();
        // 2026-07-21 신규: WideSpread 가상추적 중복방지용 - 같은 심볼이 300s 시뮬레이션 창 안에서
        // 다시 걸릴 때마다 겹치는 시뮬레이션을 또 띄우면 사실상 같은 시세변동을 여러 번 세게 되므로.
        private readonly ConcurrentDictionary<string, DateTime> _spreadSimCooldown = new();
        private readonly ConcurrentDictionary<string, (decimal Bid, decimal Ask, DateTime Time)> _bookTicker = new();

        // ── 리스크 카운터 (FundingHedger의 DailyLossLimitUsdt와 완전 별도) ──
        private readonly object _lossLock = new();
        private decimal _dailyRealizedLossUsdt = 0m;
        private DateTime _lossCounterDate = DateTime.UtcNow.Date;
        // 2026-07-19 신규: 심볼별 당일 누적손실(MaxDailyLossPerSymbolUsdt 한도 판정용) - _lossLock으로 함께 보호.
        private readonly Dictionary<string, decimal> _dailySymbolLossUsdt = new();

        private static string ResultsPath => Path.Combine(AppContext.BaseDirectory, "spike_scalp_results.jsonl");

        public SpikeScalpManager(SpikeScalpConfig cfg) { _cfg = cfg; }

        // ─── 진입점 ─────────────────────────────────────────────────

        public async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                _socketClient = new BinanceSocketClient(opts =>
                {
                    opts.ApiCredentials = Ob.client.ClientOptions.ApiCredentials;
                });

                await SubscribeWideScanAsync(ct);
                UI("✅ [SPIKE] 광역스캔 구독 시작(가격+거래대금, 전체심볼)");

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                    // 소켓 재연결 감시는 SDK 자동재연결에 위임 - 여기선 카운터 리셋/헬스체크만
                    ResetDailyCounterIfNeeded();
                }
            }
            catch (Exception ex) { UI($"❌ [SPIKE] RunLoopAsync: {ex.Message}"); }
        }

        // ─── 1단계: 광역 스캔 구독 (전체 USDT-M 심볼, 소켓 1~2개) ─────

        private async Task SubscribeWideScanAsync(CancellationToken ct)
        {
            try
            {
                // ⚠️ 메서드명은 설치된 Binance.Net 버전에 따라 다를 수 있음(전체시장 combined stream:
                // !markPrice@arr@1s, !ticker@arr) - 빌드 시 아래 두 호출의 정확한 시그니처를 라이브러리
                // 버전에 맞춰 확인/수정할 것. 개념: 심볼 리스트 없이 "전체 마켓"을 구독하는 API를 찾는다.
                var markSub = await _socketClient!.UsdFuturesApi.ExchangeData.SubscribeToAllMarkPriceUpdatesAsync(
                    1000,
                    (DataEvent<BinanceFuturesUsdtStreamMarkPrice[]> u) =>
                    {
                        foreach (var d in u.Data)
                        {
                            if (!d.Symbol.EndsWith("USDT", StringComparison.Ordinal)) continue; // USDC 등 제외
                            OnWidePriceUpdate(d.Symbol, d.MarkPrice, d.EventTime);
                        }
                    },
                    ct);
                if (markSub.Success) _markPriceSub = markSub.Data;
                else UI($"❌ [SPIKE] 전체마크프라이스 구독 실패: {markSub.Error?.Message}");

                var tickerSub = await _socketClient!.UsdFuturesApi.ExchangeData.SubscribeToAllTickerUpdatesAsync(
                    (DataEvent<IBinance24HPrice[]> u) =>
                    {
                        foreach (var d in u.Data)
                        {
                            if (!d.Symbol.EndsWith("USDT", StringComparison.Ordinal)) continue; // USDC 등 제외
                            OnWideVolumeUpdate(d.Symbol, d.QuoteVolume, DateTime.UtcNow);
                            _trend24h[d.Symbol] = d.PriceChangePercent; // 24hr 티커가 이미 실어오는 변동률 그대로 캐시
                        }
                    },
                    ct);
                if (tickerSub.Success) _tickerSub = tickerSub.Data;
                else UI($"❌ [SPIKE] 전체티커 구독 실패: {tickerSub.Error?.Message}");
            }
            catch (Exception ex) { UI($"❌ [SPIKE] SubscribeWideScanAsync: {ex.Message}"); }
        }

        private void OnWidePriceUpdate(string symbol, decimal price, DateTime eventTime)
        {
            try
            {
                if (price <= 0) return;

                // 1h 추세 버퍼 갱신 - 스파이크 발생 여부와 무관하게 매 tick마다 채워야 하므로
                // 아래 스파이크 감지 조기반환들보다 먼저 처리한다.
                var tq = _trend1hHistory.GetOrAdd(symbol, _ => new Queue<(DateTime, decimal)>());
                lock (tq)
                {
                    tq.Enqueue((eventTime, price));
                    while (tq.Count > 0 && (eventTime - tq.Peek().Time).TotalSeconds > _cfg.Trend1hWindowSec)
                        tq.Dequeue();

                    // 윈도우가 아직 90% 이상 채워지지 않았으면(재시작 직후 등) 부정확한 값이라 캐시하지 않음
                    if (tq.Count > 0 && (eventTime - tq.Peek().Time).TotalSeconds >= _cfg.Trend1hWindowSec * 0.9)
                    {
                        decimal oldest1h = tq.Peek().Price;
                        if (oldest1h > 0)
                            _trend1h[symbol] = (price - oldest1h) / oldest1h * 100m;
                    }
                }

                var q = _wideHistory.GetOrAdd(symbol, _ => new Queue<(DateTime, decimal)>());
                decimal changePct;
                lock (q)
                {
                    q.Enqueue((eventTime, price));
                    while (q.Count > 0 && (eventTime - q.Peek().Time).TotalSeconds > _cfg.WideWindowSec)
                        q.Dequeue();

                    if (q.Count < 5 + _cfg.ConfirmTicks) return; // 방향확인용 표본 추가 확보
                    decimal oldest = q.Peek().Price;
                    if (oldest <= 0) return;
                    changePct = (price - oldest) / oldest * 100m;

                    // 방향 확인: 최근 ConfirmTicks 구간이 스파이크 방향과 반대로 꺾이고 있으면(이미 반락 시작) 배제
                    var recent = q.ToArray();
                    var tail = recent.Skip(Math.Max(0, recent.Length - _cfg.ConfirmTicks - 1)).ToArray();
                    for (int i = 1; i < tail.Length; i++)
                    {
                        decimal step = tail[i].Price - tail[i - 1].Price;
                        if (Math.Sign(step) != 0 && Math.Sign(step) != Math.Sign(changePct)) return;
                    }
                }

                if (Math.Abs(changePct) < _cfg.SpikeThresholdPct) return;
                if (FundingHedger.GetTickPriceRatioPct(symbol, price) > _cfg.MaxTickPriceRatioPct) return; // 스프레드 구조적으로 큰 심볼 제외
                if (!IsVolumeSpiking(symbol, eventTime)) return;

                // 여기까지 왔으면 "스파이크 감지"는 확정 - 이후 스킵 사유를 명시적으로 로깅(튜닝 분석용)
                string? skipReason = null;
                decimal? trendAlignedTrend1h = null;
                bool alignedBreakoutOverride = false; // 2026-07-19 신규, 아래 설명
                if (_activeTargets.ContainsKey(symbol)) skipReason = "AlreadyActive";
                else if (_cooldown.TryGetValue(symbol, out var until) && DateTime.UtcNow < until) skipReason = "Cooldown";
                else if (_activeTargets.Count >= _cfg.MaxConcurrentPositions) skipReason = "MaxConcurrent";
                else if (!HasRiskBudget()) skipReason = "DailyLossLimit";
                else if (!HasSymbolRiskBudget(symbol)) skipReason = "SymbolDailyLossLimit"; // 2026-07-19 신규
                else if (_trend1h.TryGetValue(symbol, out var trend1h)
                    && Math.Abs(trend1h) >= _cfg.Min1hAlignedVetoPct
                    && Math.Sign(trend1h) == Math.Sign(changePct))
                {
                    // 2026-07-19 사람과의 대화 세션: spike_scalp_trendveto_sim.jsonl 표본(1,413건, 여러
                    // 심볼 분산)에서 "진짜 초반 돌파"(|스파이크| 3~10%대)는 순방향 탑승 시 Fwd300이
                    // 뚜렷이 플러스(3~5%대 +0.60%, 5~10%대 +0.29%)인 반면, 그 이상 극단화된 스파이크
                    // (10%+)는 +0.05%로 사실상 무의미/꼭대기 근처(ESPORTSUSDT 단일사례에선 마이너스)임을
                    // 확인 - 그 구간에서만 스킵 대신 순방향 진입을 허용한다. 범위 밖(3% 미만 또는 10%
                    // 초과)은 근거 없음/약한신호이므로 기존처럼 그대로 스킵.
                    decimal absChange = Math.Abs(changePct);
                    if (absChange >= _cfg.LargeSpikeSimThresholdPct && absChange <= _cfg.AlignedBreakoutMaxSpikePct)
                    {
                        alignedBreakoutOverride = true;
                    }
                    else
                    {
                        skipReason = "TrendAligned1h"; // 1h추세와 스파이크가 같은 방향(따라가기)이면 스킵 - 2026-07-18 백테스트로 방향 확정
                        trendAlignedTrend1h = trend1h;
                    }
                }

                if (skipReason != null)
                {
                    LogSkipped(symbol, changePct, skipReason);
                    // 2026-07-18 사람과의 대화 세션: 큰 스파이크(진짜 돌파일수록 1h추세와 같은 방향일 확률이
                    // 높아 구조적으로 여기서 전량 거부당함 - "순방향이었으면 어땠을지" 데이터가 0건이 되는
                    // 문제를 발견해 가상추적(실주문 없음)으로 보완. 위 alignedBreakoutOverride 구간으로
                    // 승격된 스파이크는 이제 실제로 진입하므로 더 이상 가상추적 대상이 아니다(그 구간
                    // 밖, 즉 여전히 스킵되는 케이스에서만 계속 가상추적해 향후 구간 확장 여부를 검증).
                    if (skipReason == "TrendAligned1h" && Math.Abs(changePct) >= _cfg.LargeSpikeSimThresholdPct)
                        _ = SimulateTrendVetoForwardAsync(symbol, changePct, price, trendAlignedTrend1h ?? 0m);
                    return;
                }

                _ = TryPromoteToTargetAsync(symbol, changePct, price, alignedBreakoutOverride);
            }
            catch (Exception ex) { UI($"❌ [SPIKE] OnWidePriceUpdate({symbol}): {ex.Message}"); }
        }

        private void OnWideVolumeUpdate(string symbol, decimal quoteVolume, DateTime eventTime)
        {
            try
            {
                if (quoteVolume <= 0) return;
                var q = _wideVolumeHistory.GetOrAdd(symbol, _ => new Queue<(DateTime, decimal)>());
                lock (q)
                {
                    q.Enqueue((eventTime, quoteVolume));
                    while (q.Count > 0 && (eventTime - q.Peek().Time).TotalSeconds > _cfg.BaselineWindowSec)
                        q.Dequeue();
                }
            }
            catch (Exception ex) { UI($"❌ [SPIKE] OnWideVolumeUpdate({symbol}): {ex.Message}"); }
        }

        // 최근 WideWindowSec 동안의 거래대금 증가분이, 전체 BaselineWindowSec 기준 평균 대비
        // VolumeSpikeMultiplier배 이상이면 true. (가격변동 트리거와 AND 조건으로 결합됨)
        private bool IsVolumeSpiking(string symbol, DateTime now)
        {
            if (!_wideVolumeHistory.TryGetValue(symbol, out var q)) return false;
            lock (q)
            {
                if (q.Count < 5) return false;
                var samples = q.ToArray();
                var oldest = samples[0];
                var latest = samples[^1];

                double totalSec = (latest.Time - oldest.Time).TotalSeconds;
                if (totalSec < _cfg.WideWindowSec * 2) return false; // 기준선 표본 부족

                var shortStartCandidates = samples.Where(s => (now - s.Time).TotalSeconds >= _cfg.WideWindowSec).ToArray();
                if (shortStartCandidates.Length == 0) return false;
                var shortStart = shortStartCandidates[^1];

                decimal shortDelta = latest.QuoteVolume - shortStart.QuoteVolume;
                if (shortDelta <= 0) return false;

                decimal baselineRatePerWindow = (latest.QuoteVolume - oldest.QuoteVolume) / (decimal)totalSec * _cfg.WideWindowSec;
                if (baselineRatePerWindow <= 0) return false;

                return (shortDelta / baselineRatePerWindow) >= _cfg.VolumeSpikeMultiplier;
            }
        }

        // ─── 2단계: 정밀추적 승격 + 진입 ─────────────────────────────

        private async Task TryPromoteToTargetAsync(string symbol, decimal spikeChangePct, decimal triggerPrice, bool alignedBreakoutOverride = false)
        {
            try
            {
                ResetDailyCounterIfNeeded();
                if (!HasRiskBudget())
                {
                    UI($"🛑 [SPIKE] 일일손실한도({_cfg.DailyLossLimitUsdt}) 소진 - {symbol} 진입 스킵");
                    LogSkipped(symbol, spikeChangePct, "DailyLossLimit");
                    return;
                }
                if (!HasSymbolRiskBudget(symbol)) // 2026-07-19 신규
                {
                    UI($"🛑 [SPIKE] {symbol} 심볼별 일일손실한도({_cfg.MaxDailyLossPerSymbolUsdt}) 소진 - 진입 스킵");
                    LogSkipped(symbol, spikeChangePct, "SymbolDailyLossLimit");
                    return;
                }
                if (_activeTargets.Count >= _cfg.MaxConcurrentPositions)
                {
                    LogSkipped(symbol, spikeChangePct, "MaxConcurrent");
                    return;
                }

                // 2026-07-20 사람과의 대화 세션: tickSize/가격 비율(MaxTickPriceRatioPct)로는 못 잡는
                // 저유동성(넓은 실시간 호가스프레드) 심볼 제외 - 광역스캔은 전 심볼 BookTicker를
                // 구독하지 않으므로, 스파이크가 실제로 확정된 이 시점에만 REST로 1회 조회한다.
                try
                {
                    var bookPrice = await Ob.client.UsdFuturesApi.ExchangeData.GetBookPriceAsync(symbol);
                    if (bookPrice.Success && bookPrice.Data != null)
                    {
                        decimal bid = bookPrice.Data.BestBidPrice, ask = bookPrice.Data.BestAskPrice;
                        decimal mid = (bid + ask) / 2m;
                        if (mid > 0)
                        {
                            decimal spreadPct = (ask - bid) / mid * 100m;
                            if (spreadPct > _cfg.MaxSpreadPct)
                            {
                                UI($"🔍 [SPIKE] {symbol} 실시간 스프레드 초과({_cfg.MaxSpreadPct}% 초과, 실측 {spreadPct:F3}%) - 저유동성으로 제외");
                                LogSkipped(symbol, spikeChangePct, "WideSpread", spreadPct);
                                // 2026-07-21 사람과의 대화 세션: MaxSpreadPct(0.03%) 임계값이 적정한지
                                // 판단하려면 여기서 거른 후보를 실제로 진입했으면 어땠을지 가상추적 데이터가
                                // 필요 - TrendAligned1h 스킵과 동일한 가상추적 패턴 적용(스프레드 구간별
                                // Fwd*Pct 비교용). 같은 심볼이 시뮬레이션 창(300s) 안에서 반복 스킵되는
                                // 경우는 중복샘플이라 건너뜀.
                                if (!_spreadSimCooldown.TryGetValue(symbol, out var simUntil) || DateTime.UtcNow >= simUntil)
                                {
                                    _spreadSimCooldown[symbol] = DateTime.UtcNow.AddSeconds(300);
                                    _ = SimulateWideSpreadForwardAsync(symbol, spikeChangePct, triggerPrice, spreadPct);
                                }
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex) { UI($"⚠️ [SPIKE] {symbol} 스프레드 조회 실패({ex.Message}) - 필터 건너뛰고 진행"); }

                var side = spikeChangePct > 0 ? OrderSide.Buy : OrderSide.Sell;
                var target = new SpikeTarget(this, symbol, side, triggerPrice, spikeChangePct, _cfg, alignedBreakoutOverride);
                if (!_activeTargets.TryAdd(symbol, target))
                {
                    LogSkipped(symbol, spikeChangePct, "AlreadyActive"); // 동시호출 경합 - 드묾
                    return;
                }

                // 진입 확정 시점에 바로 백그라운드로 RSI(14)/%B(20) 조회 시작(fire-and-forget) - 청산까지
                // 걸리는 시간(수십초~수분) 동안 REST왕복이 끝나므로 LogResult 시점엔 대부분 채워져 있음.
                // 2026-07-18 사람과의 대화 세션 백테스트 결론: 진입필터로는 안 씀, 사후분석용 로그 전용.
                _ = target.FetchIndicatorsAsync();

                await ResubscribeBookTickerAsync(CancellationToken.None);
                bool entered = await target.EnterAsync();
                if (!entered)
                {
                    LogSkipped(symbol, spikeChangePct, "EntryFailed"); // 수량계산 실패/레버리지설정 실패/주문실패
                    _activeTargets.TryRemove(symbol, out _);
                    await ResubscribeBookTickerAsync(CancellationToken.None);
                    return;
                }

                target.Arm(async (realizedUsdt, exitReason) =>
                {
                    RecordRealizedPnl(symbol, realizedUsdt);
                    _activeTargets.TryRemove(symbol, out _);
                    _cooldown[symbol] = DateTime.UtcNow.AddMinutes(_cfg.CooldownMinutes);
                    await ResubscribeBookTickerAsync(CancellationToken.None);
                    LogResult(target, realizedUsdt, exitReason);
                });
                _ = target.RunTimeoutWatchdogAsync();
                _ = target.RunStaleObservationAsync();
            }
            catch (Exception ex) { UI($"❌ [SPIKE] TryPromoteToTargetAsync({symbol}): {ex.Message}"); }
        }

        // 활성 타겟 전체를 하나의 BookTicker 구독으로 묶어서 재구독(FundingHedgeManager와 동일 패턴).
        private async Task ResubscribeBookTickerAsync(CancellationToken ct)
        {
            try
            {
                if (_bookTickerSub != null)
                {
                    await _socketClient!.UnsubscribeAsync(_bookTickerSub);
                    _bookTickerSub = null;
                }
                var symbols = _activeTargets.Keys.ToList();
                if (symbols.Count == 0) return;

                var sub = await _socketClient!.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(
                    symbols,
                    x =>
                    {
                        try
                        {
                            _bookTicker[x.Data.Symbol] = (x.Data.BestBidPrice, x.Data.BestAskPrice, DateTime.UtcNow);
                            if (_activeTargets.TryGetValue(x.Data.Symbol, out var t))
                                t.OnPriceUpdate(x.Data.BestBidPrice, x.Data.BestAskPrice);
                        }
                        catch (Exception ex) { UI($"❌ [SPIKE] BookTicker처리({x.Data.Symbol}): {ex.Message}"); }
                    },
                    ct);
                if (sub.Success) _bookTickerSub = sub.Data;
                else UI($"❌ [SPIKE] BookTicker 재구독 실패: {sub.Error?.Message}");
            }
            catch (Exception ex) { UI($"❌ [SPIKE] ResubscribeBookTickerAsync: {ex.Message}"); }
        }

        // 보유중인 포지션을 "지금 청산하면" 받을 가격 기준(롱=Bid로 매도, 숏=Ask로 매수청산)
        // maxAgeMs 지정 시 _bookTicker 캐시가 그보다 오래된 값이면 0(미가용)으로 취급한다 - 2026-07-19
        // "스테일 진입가" 버그 수정: 같은 심볼을 재진입할 때 ResubscribeBookTickerAsync 직후 새 틱이
        // 아직 안 들어온 상태에서 몇 시간 전 캐시값을 "지금 체결가"로 오인해 EntryPrice가 이미
        // 시장과 동떨어진 값으로 기록되고, 그 결과 PeakPct가 청산 때까지 계속 0에 머무는 현상이
        // 반복 관측됨(진입시 재조회 로직에서만 이 인자를 사용, 기존 호출부는 영향 없음).
        internal decimal GetBookTickerPrice(string symbol, OrderSide side, int? maxAgeMs = null)
        {
            if (!_bookTicker.TryGetValue(symbol, out var bt)) return 0m;
            if (maxAgeMs.HasValue && (DateTime.UtcNow - bt.Time).TotalMilliseconds > maxAgeMs.Value) return 0m;
            return side == OrderSide.Buy ? bt.Bid : bt.Ask;
        }

        // ─── 리스크 카운터 ─────────────────────────────────────────

        private void ResetDailyCounterIfNeeded()
        {
            lock (_lossLock)
            {
                if (DateTime.UtcNow.Date != _lossCounterDate)
                {
                    _lossCounterDate = DateTime.UtcNow.Date;
                    _dailyRealizedLossUsdt = 0m;
                    _dailySymbolLossUsdt.Clear();
                }
            }
        }

        private bool HasRiskBudget()
        {
            lock (_lossLock) { return _dailyRealizedLossUsdt < _cfg.DailyLossLimitUsdt; }
        }

        // 2026-07-19 신규: 심볼 하나가 당일 MaxDailyLossPerSymbolUsdt 이상 손실을 냈으면 false
        // (ESPORTSUSDT류 특정 심볼 반복손실 집중 대응). MaxDailyLossPerSymbolUsdt<=0이면 비활성화.
        private bool HasSymbolRiskBudget(string symbol)
        {
            if (_cfg.MaxDailyLossPerSymbolUsdt <= 0) return true;
            lock (_lossLock)
            {
                return !_dailySymbolLossUsdt.TryGetValue(symbol, out var loss) || loss < _cfg.MaxDailyLossPerSymbolUsdt;
            }
        }

        private void RecordRealizedPnl(string symbol, decimal pnlUsdt)
        {
            lock (_lossLock)
            {
                if (pnlUsdt < 0)
                {
                    _dailyRealizedLossUsdt += Math.Abs(pnlUsdt);
                    _dailySymbolLossUsdt.TryGetValue(symbol, out var prev);
                    _dailySymbolLossUsdt[symbol] = prev + Math.Abs(pnlUsdt);
                }
            }
        }

        private readonly ConcurrentDictionary<string, bool> _leverageSetDone = new();

        internal async Task<bool> EnsureLeverageAsync(string symbol)
        {
            if (_leverageSetDone.ContainsKey(symbol)) return true;
            try
            {
                var marginResult = await Ob.client.UsdFuturesApi.Account.ChangeMarginTypeAsync(
                    symbol: symbol, marginType: Binance.Net.Enums.FuturesMarginType.Cross);
                // 이미 Cross로 설정된 심볼은 실패(중복설정 에러)가 정상이므로 무시
                var leverageResult = await Ob.client.UsdFuturesApi.Account.ChangeInitialLeverageAsync(symbol, _cfg.Leverage);
                if (!leverageResult.Success)
                {
                    UI($"❌ [SPIKE] {symbol} 레버리지 설정 실패: {leverageResult.Error?.Message}");
                    return false;
                }
                _leverageSetDone[symbol] = true;
                return true;
            }
            catch (Exception ex) { UI($"❌ [SPIKE] EnsureLeverageAsync({symbol}): {ex.Message}"); return false; }
        }

        // ─── 주문 실행 (REST, 단순 MVP - 소켓주문 미사용) ─────────────

        internal async Task<bool> PlaceOrderAsync(string symbol, OrderSide side, decimal qty, PositionSide posSide, bool reduceOnly)
        {
            try
            {
                if (DebugDryRun)
                {
                    UI($"🧪 [SPIKE-DRY-RUN] {symbol} {side} {posSide} qty={qty} reduceOnly={reduceOnly}");
                    return true;
                }
                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol, side, FuturesOrderType.Market, qty, positionSide: posSide);
                // ⚠️ reduceOnly는 Hedge모드에서 API가 거부할 수 있어 실제 호출엔 안 넘김(기존 FundingHedgeManager와
                // 동일 패턴) - positionSide+side 조합만으로 오픈/클로즈가 결정됨. reduceOnly 파라미터는 로그용.
                if (!result.Success)
                    UI($"❌ [SPIKE-ORDER] {symbol} {side} 실패: {result.Error?.Message}");
                return result.Success;
            }
            catch (Exception ex) { UI($"❌ [SPIKE-ORDER] {symbol} 예외: {ex.Message}"); return false; }
        }

        // ─── 수량 계산 헬퍼 (FundingHedger.cs 안 건드리고 동일 패턴 자체 구현) ─────

        internal static decimal GetStepSize(string symbol)
        {
            try
            {
                var symbolInfo = MockExchange.FindSymbolInfo(symbol);
                if (symbolInfo != null)
                {
                    var pi = symbolInfo.GetType().GetProperty("Filters");
                    var filters = pi?.GetValue(symbolInfo) as System.Collections.IEnumerable;
                    if (filters != null)
                    {
                        foreach (var f in filters)
                        {
                            var sp = f.GetType().GetProperty("StepSize");
                            if (sp != null)
                            {
                                var val = sp.GetValue(f);
                                if (val != null && decimal.TryParse(val.ToString(), out decimal s) && s > 0)
                                    return s;
                            }
                        }
                    }
                }
            }
            catch { }
            return 1m;
        }

        internal static decimal FloorToStep(decimal qty, decimal step)
        {
            if (step <= 0) return qty;
            return Math.Floor(qty / step) * step;
        }

        internal static decimal CalcQty(string symbol, decimal notional, decimal price)
        {
            if (price <= 0) return 0m;
            decimal raw = notional / price;
            decimal step = GetStepSize(symbol);
            decimal minNotional = FundingHedger.GetMinNotional(symbol);
            decimal qty = FloorToStep(raw, step);
            if (qty * price < minNotional) return 0m; // 최소주문금액 미달 - 진입 포기
            return qty;
        }

        // ─── RSI(14)/%B(20) 보조지표 (2026-07-18 사람과의 대화 세션 백테스트 결론 반영) ─────
        // 1h 캔들 종가 기준(과거 백테스트 backtest_indicators.py와 동일 정의: Wilder RSI(14),
        // 20기간 볼린저 %B) - 진입 필터에는 쓰지 않고 spike_scalp_results.jsonl에 로그로만 남긴다.

        internal static decimal? CalcRsi14(IReadOnlyList<decimal> closes, int period = 14)
        {
            int n = closes.Count;
            if (n < period + 1) return null;
            var gains = new decimal[n - 1];
            var losses = new decimal[n - 1];
            for (int i = 1; i < n; i++)
            {
                decimal diff = closes[i] - closes[i - 1];
                gains[i - 1] = diff > 0 ? diff : 0m;
                losses[i - 1] = diff < 0 ? -diff : 0m;
            }
            decimal avgGain = gains.Take(period).Sum() / period;
            decimal avgLoss = losses.Take(period).Sum() / period;
            for (int i = period; i < gains.Length; i++)
            {
                avgGain = (avgGain * (period - 1) + gains[i]) / period;
                avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
            }
            if (avgLoss == 0m) return 100m;
            decimal rs = avgGain / avgLoss;
            return 100m - 100m / (1m + rs);
        }

        internal static decimal? CalcPctB20(IReadOnlyList<decimal> closes, int period = 20)
        {
            int n = closes.Count;
            if (n < period) return null;
            var window = closes.Skip(n - period).Take(period).ToList();
            decimal mean = window.Average();
            decimal variance = window.Select(c => (c - mean) * (c - mean)).Sum() / period;
            decimal std = (decimal)Math.Sqrt((double)variance);
            if (std == 0m) return 0.5m;
            decimal upper = mean + 2m * std;
            decimal lower = mean - 2m * std;
            return (closes[n - 1] - lower) / (upper - lower);
        }

        private static string SkippedPath => Path.Combine(AppContext.BaseDirectory, "spike_scalp_skipped.jsonl");

        // 스파이크는 감지됐으나(가격+거래대금 조건 충족) 진입하지 않은 케이스 기록 - 임계값 튜닝시
        // "얼마나 많은 기회를 캡/쿨다운 때문에 놓쳤는지" 판단용(FundingHedger의 skipped_results.jsonl과 동일 취지).
        private void LogSkipped(string symbol, decimal spikeChangePct, string skipReason, decimal? spreadPct = null)
        {
            try
            {
                _trend24h.TryGetValue(symbol, out var trend24h);
                _trend1h.TryGetValue(symbol, out var trend1h);
                var record = new
                {
                    Timestamp = DateTime.UtcNow,
                    Symbol = symbol,
                    SpikeChangePct = spikeChangePct,
                    SkipReason = skipReason,
                    SpreadPct = spreadPct, // WideSpread 스킵일 때만 값 존재, 그 외 스킵사유는 null
                    ActiveCount = _activeTargets.Count,
                    Trend24hPct = trend24h,
                    Trend1hPct = trend1h
                };
                File.AppendAllText(SkippedPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch (Exception ex) { UI($"❌ [SPIKE] LogSkipped: {ex.Message}"); }
        }

        // ─── 결과 로깅 (spike_scalp_results.jsonl - 사후분석용) ─────

        private void LogResult(SpikeTarget t, decimal realizedUsdt, string exitReason)
        {
            try
            {
                _trend24h.TryGetValue(t.Symbol, out var trend24h);
                _trend1h.TryGetValue(t.Symbol, out var trend1h);
                var record = new
                {
                    // ExecuteExitAsync에서 찍은 실제 청산시각 - LogPostExit의 Timestamp와 동일 값을 써서
                    // 두 파일을 (Symbol,Timestamp)로 조인할 수 있게 한다(2026-07-18 신규).
                    Timestamp = t.ExitTimestamp,
                    // 2026-07-21 신규: spike_scalp_stale_check.jsonl(스테일컷 관측 로그)과 (Symbol,
                    // EntryTimestamp)로 조인하기 위한 필드.
                    EntryTimestamp = t.EntryTimestamp,
                    Symbol = t.Symbol,
                    Side = t.Side.ToString(),
                    SpikeChangePct = t.SpikeChangePct,
                    TriggerPrice = t.TriggerPrice,
                    EntryPrice = t.EntryPrice,
                    // 진입슬리피지(방향보정, %) - 양수면 트리거가격보다 유리하게(추세 방향으로 더 간 후)
                    // 체결됐다는 뜻. 2026-07-20 사람과의 대화 세션: "PeakPct=0으로 끝나는 트레이드는 진입가
                    // 자체가 이미 국지적 고점/저점 근처에서 체결된 것 아니냐"는 가설을 검증하기 위해 추가
                    // (기존엔 TriggerPrice가 로그에 없어 EntryPriceIsFallback=false 케이스의 스테일 원인을
                    // 구분할 수 없었음).
                    EntrySlippagePct = t.TriggerPrice > 0
                        ? (t.Side == OrderSide.Buy
                            ? (t.EntryPrice - t.TriggerPrice) / t.TriggerPrice * 100m
                            : (t.TriggerPrice - t.EntryPrice) / t.TriggerPrice * 100m)
                        : (decimal?)null,
                    // true면 신선한 체결가 재조회 실패로 TriggerPrice를 그대로 EntryPrice로 썼다는 뜻
                    // (2026-07-19 신규 - "스테일 진입가"/PeakPct=0 빈도와의 상관관계를 앞으로 검증하기 위한 필드).
                    EntryPriceIsFallback = t.EntryPriceIsFallback,
                    ExitReason = exitReason,
                    RealizedUsdt = realizedUsdt,
                    PeakPct = t.PeakPct,
                    Trend24hPct = trend24h,
                    Trend1hPct = trend1h,
                    // 2026-07-18 대화세션 백테스트 결론: 방향성보정값(스파이크 방향 기준 재투영), 로그 전용·필터 미사용.
                    // null이면 REST 조회가 청산 시점까지 못 끝난 것(짧은 보유시간 트레이드에서 발생 가능).
                    DirRsi14 = t.DirRsi14,
                    DirPctB20 = t.DirPctB20,
                    // 2026-07-19 신규: true면 TrendAligned1h인데 "초반 돌파" 구간이라 순방향 진입이
                    // 허용된 케이스 - 이 필드로 override 적용 트레이드만 따로 뽑아 검증 가능.
                    AlignedBreakoutOverride = t.AlignedBreakoutOverride
                };
                File.AppendAllText(ResultsPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch (Exception ex) { UI($"❌ [SPIKE] LogResult: {ex.Message}"); }
        }

        // ─── 청산 후 가격추적 로그 (spike_scalp_postexit.jsonl - TP/SL 적정성 판단용, 2026-07-18 신규) ─

        internal decimal? GetLatestWidePrice(string symbol)
        {
            if (!_wideHistory.TryGetValue(symbol, out var q)) return null;
            lock (q)
            {
                if (q.Count == 0) return null;
                return q.ToArray()[^1].Price; // 큐는 스파이크 감지 여부와 무관하게 전 심볼 매tick 갱신됨
            }
        }

        private static string PostExitPath => Path.Combine(AppContext.BaseDirectory, "spike_scalp_postexit.jsonl");

        internal void LogPostExit(SpikeTarget t, decimal? fwd30sPct, decimal? fwd60sPct, decimal? fwd180sPct, decimal? fwd300sPct)
        {
            try
            {
                var record = new
                {
                    Timestamp = t.ExitTimestamp, // spike_scalp_results.jsonl의 Timestamp와 동일값 - 조인키
                    Symbol = t.Symbol,
                    Side = t.Side.ToString(),
                    ExitReason = t.LastExitReason,
                    ExitPrice = t.ExitPrice,
                    PeakPct = t.PeakPct,
                    // 부호는 "원래 포지션 방향 기준" 재투영값 - 양수면 청산 안 했을 때 더 벌었을 방향으로
                    // 계속 움직였다는 뜻(조기청산/과도한 SL·giveback 신호), 음수면 청산 이후 반전(타이밍 적절).
                    Fwd30sPct = fwd30sPct,
                    Fwd60sPct = fwd60sPct,
                    Fwd180sPct = fwd180sPct,
                    Fwd300sPct = fwd300sPct
                };
                File.AppendAllText(PostExitPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch (Exception ex) { UI($"❌ [SPIKE] LogPostExit: {ex.Message}"); }
        }

        // ─── 추세일치 거부 가상추적 (spike_scalp_trendveto_sim.jsonl - 2026-07-18 신규) ──────
        // TrendAligned1h로 거부된 "큰" 스파이크에 대해 실주문 없이 "순방향(추세추종)으로 탔으면
        // 어땠을지"를 가상 가격추적으로 기록. 사람과의 대화 세션에서 제기된 "짧은 60s 윈도우로 잡는
        // 스파이크는 대부분 잔파도라 역방향(평균회귀)이 맞지만, 진짜 몇십%급 돌파는 순방향이 맞을 수
        // 있는데 지금 구조로는 그 경우가 전량 거부당해 검증 데이터가 0건"이라는 문제 보완용. 필터
        // 로직에는 영향 없음(로그 전용).

        private static string TrendVetoSimPath => Path.Combine(AppContext.BaseDirectory, "spike_scalp_trendveto_sim.jsonl");

        private static decimal? ComputeSimFwdPct(OrderSide side, decimal basePrice, decimal? price)
        {
            if (!price.HasValue || basePrice <= 0) return null;
            return side == OrderSide.Buy
                ? (price.Value - basePrice) / basePrice * 100m
                : (basePrice - price.Value) / basePrice * 100m;
        }

        private async Task SimulateTrendVetoForwardAsync(string symbol, decimal spikeChangePct, decimal triggerPrice, decimal trend1hPct)
        {
            try
            {
                // 순방향(=스파이크 방향, 추세추종) 기준 재투영 - 양수면 추세추종이 이겼을 거란 뜻,
                // 음수면 지금처럼 거부한 게 맞았다는 뜻(반락).
                var side = spikeChangePct > 0 ? OrderSide.Buy : OrderSide.Sell;

                await Task.Delay(30000);
                decimal? p30 = ComputeSimFwdPct(side, triggerPrice, GetLatestWidePrice(symbol));
                await Task.Delay(30000); // 누적 60s
                decimal? p60 = ComputeSimFwdPct(side, triggerPrice, GetLatestWidePrice(symbol));
                await Task.Delay(120000); // 누적 180s
                decimal? p180 = ComputeSimFwdPct(side, triggerPrice, GetLatestWidePrice(symbol));
                await Task.Delay(120000); // 누적 300s
                decimal? p300 = ComputeSimFwdPct(side, triggerPrice, GetLatestWidePrice(symbol));

                var record = new
                {
                    Timestamp = DateTime.UtcNow,
                    Symbol = symbol,
                    Side = side.ToString(),
                    SpikeChangePct = spikeChangePct,
                    Trend1hPct = trend1hPct,
                    TriggerPrice = triggerPrice,
                    Fwd30sPct = p30,
                    Fwd60sPct = p60,
                    Fwd180sPct = p180,
                    Fwd300sPct = p300
                };
                File.AppendAllText(TrendVetoSimPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch (Exception ex) { UI($"❌ [SPIKE] SimulateTrendVetoForwardAsync({symbol}): {ex.Message}"); }
        }

        // 2026-07-21 사람과의 대화 세션: MaxSpreadPct(0.03%) 임계값이 적정한지(너무 타이트해서
        // 실익 있는 후보까지 거르는 건 아닌지, 반대로 완화하면 슬리피지로 손해인지) 판단하려면
        // WideSpread로 거른 후보도 TrendAligned1h와 동일하게 "실제 진입했으면 어땠을지" 가상추적이
        // 필요 - 위 SimulateTrendVetoForwardAsync와 완전히 동일한 방식(TriggerPrice 기준 순방향
        // 재투영, 30/60/180/300s)이되 SpreadPct를 같이 기록해 추후 스프레드 구간별로 묶어 비교할 수
        // 있게 한다. 스프레드 자체의 슬리피지 비용은 여기서 차감하지 않음(원본 모멘텀 수익률만 기록) -
        // 분석 시 SpreadPct/2 정도를 왕복 슬리피지 근사치로 빼고 판단할 것. 필터 로직에는 영향 없음.
        private static string WideSpreadSimPath => Path.Combine(AppContext.BaseDirectory, "spike_scalp_widespread_sim.jsonl");

        private async Task SimulateWideSpreadForwardAsync(string symbol, decimal spikeChangePct, decimal triggerPrice, decimal spreadPct)
        {
            try
            {
                var side = spikeChangePct > 0 ? OrderSide.Buy : OrderSide.Sell;

                await Task.Delay(30000);
                decimal? p30 = ComputeSimFwdPct(side, triggerPrice, GetLatestWidePrice(symbol));
                await Task.Delay(30000); // 누적 60s
                decimal? p60 = ComputeSimFwdPct(side, triggerPrice, GetLatestWidePrice(symbol));
                await Task.Delay(120000); // 누적 180s
                decimal? p180 = ComputeSimFwdPct(side, triggerPrice, GetLatestWidePrice(symbol));
                await Task.Delay(120000); // 누적 300s
                decimal? p300 = ComputeSimFwdPct(side, triggerPrice, GetLatestWidePrice(symbol));

                var record = new
                {
                    Timestamp = DateTime.UtcNow,
                    Symbol = symbol,
                    Side = side.ToString(),
                    SpikeChangePct = spikeChangePct,
                    SpreadPct = spreadPct,
                    TriggerPrice = triggerPrice,
                    Fwd30sPct = p30,
                    Fwd60sPct = p60,
                    Fwd180sPct = p180,
                    Fwd300sPct = p300
                };
                File.AppendAllText(WideSpreadSimPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch (Exception ex) { UI($"❌ [SPIKE] SimulateWideSpreadForwardAsync({symbol}): {ex.Message}"); }
        }

        internal void UI(string msg) { try { Ob.ui?.SetText(msg); } catch { } }
    }

    // ─── 개별 스파이크 타겟 (진입~청산 라이프사이클) ─────────────────

    internal sealed class SpikeTarget
    {
        private readonly SpikeScalpManager _mgr;
        private readonly SpikeScalpConfig _cfg;

        public string Symbol { get; }
        public OrderSide Side { get; }
        public decimal TriggerPrice { get; }
        public decimal SpikeChangePct { get; }
        public decimal EntryPrice { get; private set; }
        // true면 신선한 BookTicker 재조회에 실패해 TriggerPrice로 폴백한 경우(2026-07-19 신규,
        // "스테일 진입가" 버그 검증용 - 향후 사이클에서 이 필드로 PeakPct=0 빈도와의 상관관계 확인 가능).
        public bool EntryPriceIsFallback { get; private set; }
        public decimal Qty { get; private set; }
        public decimal PeakPct { get; private set; }
        public decimal? DirRsi14 { get; private set; }
        public decimal? DirPctB20 { get; private set; }
        public DateTime EntryTimestamp => _entryTime;
        public DateTime ExitTimestamp { get; private set; }
        public decimal ExitPrice { get; private set; }
        public string LastExitReason { get; private set; } = "";
        // true면 TrendAligned1h에 걸렸을 스파이크인데 "초반 돌파" 구간(AlignedBreakoutMaxSpikePct 이내)
        // 이라 스킵 대신 순방향 진입이 허용된 경우(2026-07-19 신규 - trendveto_sim 1,413건 근거).
        public bool AlignedBreakoutOverride { get; }

        private int _exiting = 0; // 중복청산 방지(콜백 동시호출 대비)
        private int _slHitCount = 0; // SL 연속확인 카운터(2026-07-20 신규, 단일틱 노이즈 방지)
        private DateTime _entryTime;
        private Func<decimal, string, Task>? _onExit;
        private volatile bool _armed = false;
        private PositionSide PosSide => Side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;

        public SpikeTarget(SpikeScalpManager mgr, string symbol, OrderSide side, decimal triggerPrice, decimal spikeChangePct, SpikeScalpConfig cfg, bool alignedBreakoutOverride = false)
        {
            _mgr = mgr; Symbol = symbol; Side = side; TriggerPrice = triggerPrice; SpikeChangePct = spikeChangePct; _cfg = cfg;
            AlignedBreakoutOverride = alignedBreakoutOverride;
        }
        public void Arm(Func<decimal, string, Task> onExit)
        {
            _entryTime = DateTime.UtcNow;
            PeakPct = 0m;
            _onExit = onExit;
            _armed = true; // 여기서부터 OnPriceUpdate가 실제 판정 시작
        }
        public async Task<bool> EnterAsync()
        {
            try
            {
                Qty = SpikeScalpManager.CalcQty(Symbol, _cfg.TestNotional, TriggerPrice);
                if (Qty <= 0)
                {
                    _mgr.UI($"⚠️ [SPIKE] {Symbol} 수량계산 실패(최소주문금액 미달) - 진입 포기");
                    return false;
                }

                // 2026-07-20 사람과의 대화 세션: SL 중 절반 가까이가 진입 5초 이내 발생하는 "즉발형"으로
                // 나타남 - postexit Fwd값 분석 결과 이 그룹은 청산 후에도 계속 불리한 방향으로 움직여서
                // (SL이 과도했던 게 아니라 진입 자체가 이미 반전 이후였던 것으로 확인됨). 광역스캔(1s단위
                // MarkPrice)으로 스파이크를 감지한 뒤 실제 주문까지 걸리는 지연 동안 이미 반전된 경우를
                // 주문 직전에 한 번 더 확인해 거른다. 데이터가 아직 없으면(신규 구독 직후 등) 판단
                // 보류하고 기존처럼 그대로 진행(기존 EntryPriceIsFallback 폴백과 동일한 관례).
                var preCheckSide = Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                decimal preEntryPrice = 0m;
                for (int i = 0; i < 3; i++)
                {
                    preEntryPrice = _mgr.GetBookTickerPrice(Symbol, preCheckSide, maxAgeMs: 1000);
                    if (preEntryPrice > 0) break;
                    await Task.Delay(100);
                }
                if (preEntryPrice > 0)
                {
                    decimal rawDrift = (preEntryPrice - TriggerPrice) / TriggerPrice * 100m;
                    decimal driftPct = Side == OrderSide.Buy ? rawDrift : -rawDrift; // 스파이크 방향 기준 재투영
                    if (driftPct < -_cfg.MaxEntryReversalPct)
                    {
                        _mgr.UI($"🚫 [SPIKE] {Symbol} 진입 직전 이미 반전 감지(트리거대비 {driftPct:+0.00;-0.00}%, 임계 -{_cfg.MaxEntryReversalPct}%) - 진입 취소");
                        return false;
                    }
                }

                if (!SpikeScalpManager.DebugDryRun && !await _mgr.EnsureLeverageAsync(Symbol))
                {
                    _mgr.UI($"⚠️ [SPIKE] {Symbol} 레버리지 설정 실패 - 진입 포기");
                    return false;
                }
                bool ok = await _mgr.PlaceOrderAsync(Symbol, Side, Qty, PosSide, reduceOnly: false);
                if (!ok) return false;

                // 체결가 재조회: 진입은 반대방향 호가로 체결됨(Long=Ask매수, Short=Bid매도) -> side 반전해서 조회.
                // 구독 직후라 피드가 아직 안 들어왔을 수 있어 짧게 재시도, 끝내 없으면 TriggerPrice로 폴백.
                // ⚠️ maxAgeMs 필수 지정(2026-07-19 수정): 같은 심볼을 재진입할 때 직전
                // ResubscribeBookTickerAsync 재구독 후 새 틱이 아직 안 들어온 상태에서 몇 시간 전
                // 캐시값이 남아있으면(dictionary가 재구독으로 안 지워짐) 그걸 "지금 체결가"로 오인하는
                // 버그가 있었음 - 1000ms보다 오래된 캐시는 미가용 취급해 재시도/폴백을 타도록 강제.
                var fillSide = Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                decimal fillPrice = 0m;
                for (int i = 0; i < 5; i++)
                {
                    fillPrice = _mgr.GetBookTickerPrice(Symbol, fillSide, maxAgeMs: 1000);
                    if (fillPrice > 0) break;
                    await Task.Delay(100);
                }
                EntryPriceIsFallback = fillPrice <= 0;
                EntryPrice = fillPrice > 0 ? fillPrice : TriggerPrice;
                if (fillPrice <= 0)
                    _mgr.UI($"⚠️ [SPIKE] {Symbol} 체결가 재조회 실패(신선도 미달 포함) - TriggerPrice로 폴백");

                _mgr.UI($"🎯 [SPIKE] {Symbol} {Side} 진입 qty={Qty} @ {EntryPrice:F6} (트리거대비 {(EntryPrice - TriggerPrice) / TriggerPrice * 100m:+0.00;-0.00}%, 스파이크 {SpikeChangePct:+0.00;-0.00}%)");
                return true;
            }
            catch (Exception ex) { _mgr.UI($"❌ [SPIKE] EnterAsync({Symbol}): {ex.Message}"); return false; }
        }

        // 1h 캔들 종가로 RSI(14)/%B(20)를 조회해 스파이크 방향 기준으로 재투영한 값을 캐싱한다.
        // 진입 필터에는 쓰지 않음(2026-07-18 백테스트 결론) - LogResult에 실릴 로그 전용 값.
        public async Task FetchIndicatorsAsync()
        {
            try
            {
                var r = await Ob.client.UsdFuturesApi.ExchangeData
                    .GetKlinesAsync(Symbol, KlineInterval.OneHour, limit: 30).ConfigureAwait(false);
                if (!r.Success) return;
                var closes = r.Data.OrderBy(k => k.OpenTime).Select(k => (decimal)k.ClosePrice).ToList();
                bool isBuy = Side == OrderSide.Buy;
                decimal? rsi = SpikeScalpManager.CalcRsi14(closes);
                decimal? pctb = SpikeScalpManager.CalcPctB20(closes);
                if (rsi.HasValue) DirRsi14 = isBuy ? rsi.Value : 100m - rsi.Value;
                if (pctb.HasValue) DirPctB20 = isBuy ? pctb.Value : 1m - pctb.Value;
            }
            catch (Exception ex) { _mgr.UI($"❌ [SPIKE] FetchIndicatorsAsync({Symbol}): {ex.Message}"); }
        }

        public async void OnPriceUpdate(decimal bid, decimal ask)
        {
            try
            {
                if (!_armed) return;                      // EntryPrice 세팅 전이면 무시
                if (Volatile.Read(ref _exiting) != 0) return;
                if (EntryPrice <= 0) return;               // 방어적 이중체크
                decimal price = Side == OrderSide.Buy ? bid : ask;

                decimal pnlPct = Side == OrderSide.Buy
                    ? (price - EntryPrice) / EntryPrice * 100m
                    : (EntryPrice - price) / EntryPrice * 100m;
                if (pnlPct > PeakPct) PeakPct = pnlPct;

                bool trailArmed = PeakPct >= _cfg.TrailArmPct;
                bool trailHit = trailArmed && (PeakPct - pnlPct) >= _cfg.TrailGivebackPct / 100m * PeakPct;

                // 2026-07-20 사람과의 대화 세션: SL만 연속확인 디바운스 추가(트레일은 범위 밖, 손대지
                // 않음) - 단일 틱(호가 노이즈)만으로 즉시 손절되는 것을 줄이기 위함. SL조건을 다시
                // 벗어나면 카운터 리셋.
                bool slCondition = pnlPct <= -_cfg.StopLossPct;
                _slHitCount = slCondition ? _slHitCount + 1 : 0;
                bool hitSL = _slHitCount >= _cfg.SlConfirmTicks;
                if (!trailHit && !hitSL) return;

                if (Interlocked.CompareExchange(ref _exiting, 1, 0) != 0) return; // 딱 한번만
                await ExecuteExitAsync(pnlPct, trailHit ? "TRAIL" : "SL");
            }
            catch (Exception ex) { _mgr.UI($"❌ [SPIKE] OnPriceUpdate({Symbol}): {ex.Message}"); }
        }

        // 타임아웃 전용 저빈도 워치독(가격정체시에도 청산되도록) - 1초 간격이면 충분
        public async Task RunTimeoutWatchdogAsync()
        {
            try
            {
                while (Volatile.Read(ref _exiting) == 0)
                {
                    if ((DateTime.UtcNow - _entryTime).TotalMilliseconds >= _cfg.MaxHoldMs)
                    {
                        if (Interlocked.CompareExchange(ref _exiting, 1, 0) != 0) return;
                        decimal price = _mgr.GetBookTickerPrice(Symbol, Side);
                        decimal pnlPct = price > 0
                            ? (Side == OrderSide.Buy ? (price - EntryPrice) / EntryPrice * 100m : (EntryPrice - price) / EntryPrice * 100m)
                            : 0m;
                        await ExecuteExitAsync(pnlPct, "TIMEOUT");
                        return;
                    }
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex) { _mgr.UI($"❌ [SPIKE] RunTimeoutWatchdogAsync({Symbol}): {ex.Message}"); }
        }

        // 2026-07-21 사람과의 대화 세션: "SL(-0.3%)까지 기다리지 말고 진입 후 N초간 스테일(pnl이
        // 거의 안 움직임)이면 미리 손절하면 어떨까"는 아이디어를 검증하기 위한 순수 관측 로그 -
        // 청산 로직에는 전혀 관여하지 않고 5초/10초 시점의 pnl만 기록한다. 몇 사이클 쌓이면
        // spike_scalp_results.jsonl과 (Symbol,EntryTimestamp)로 조인해 "그 시점에 이미 스테일이었던
        // 트레이드 중 나중에 TRAIL로 이긴 비율"을 계산 → 그 비율이 낮으면 그때 실제 컷 로직 도입.
        private static string StaleCheckPath => Path.Combine(AppContext.BaseDirectory, "spike_scalp_stale_check.jsonl");

        public async Task RunStaleObservationAsync()
        {
            try
            {
                await Task.Delay(5000);
                LogStaleCheckpoint(5);
                await Task.Delay(5000); // 누적 10s
                LogStaleCheckpoint(10);
            }
            catch (Exception ex) { _mgr.UI($"❌ [SPIKE] RunStaleObservationAsync({Symbol}): {ex.Message}"); }
        }

        private void LogStaleCheckpoint(int elapsedSec)
        {
            try
            {
                bool alreadyExited = Volatile.Read(ref _exiting) != 0;
                decimal? pnlPct = null;
                if (!alreadyExited)
                {
                    decimal price = _mgr.GetBookTickerPrice(Symbol, Side);
                    if (price > 0)
                        pnlPct = Side == OrderSide.Buy
                            ? (price - EntryPrice) / EntryPrice * 100m
                            : (EntryPrice - price) / EntryPrice * 100m;
                }
                var record = new
                {
                    Timestamp = DateTime.UtcNow,
                    Symbol,
                    EntryTimestamp,
                    ElapsedSec = elapsedSec,
                    PnlPct = pnlPct,
                    AlreadyExited = alreadyExited,
                    PeakPctSoFar = PeakPct
                };
                File.AppendAllText(StaleCheckPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch (Exception ex) { _mgr.UI($"❌ [SPIKE] LogStaleCheckpoint({Symbol}): {ex.Message}"); }
        }

        private async Task ExecuteExitAsync(decimal pnlPct, string reason)
        {
            try
            {
                bool closed = await _mgr.PlaceOrderAsync(Symbol, Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy, Qty, PosSide, reduceOnly: true);
                if (!closed) _mgr.UI($"🚨 [SPIKE] {Symbol} 청산주문 실패 - 수동확인 필요 ({reason})");

                decimal realizedUsdt = pnlPct / 100m * (Qty * EntryPrice);
                ExitTimestamp = DateTime.UtcNow;
                LastExitReason = reason;
                // OnPriceUpdate/타임아웃워치독에서 pnlPct를 산출한 방식의 역산 - 실제 체결가가 아니라
                // 근사치(EntryPrice 기준)지만, 청산 이후 가격변화 방향을 재는 기준점으로는 충분함.
                ExitPrice = Side == OrderSide.Buy ? EntryPrice * (1 + pnlPct / 100m) : EntryPrice * (1 - pnlPct / 100m);
                _mgr.UI($"🏁 [SPIKE] {Symbol} 청산({reason}) pnl={pnlPct:+0.00;-0.00}% peak={PeakPct:F2}% realized≈{realizedUsdt:F2}USDT");

                // 청산 이후 가격이 어떻게 움직였는지(TP/SL 이후 추세) 별도로 추적 - fire-and-forget.
                // 2026-07-18 사람과의 대화 세션: "TP/SL 이후 데이터를 안 남기면 giveback/SL 비율이
                // 적정한지 판단할 근거가 없다"는 지적 반영 - spike_scalp_postexit.jsonl에 별도 기록.
                _ = TrackPostExitAsync();

                if (_onExit != null) await _onExit(realizedUsdt, reason);
            }
            catch (Exception ex) { _mgr.UI($"❌ [SPIKE] ExecuteExitAsync({Symbol}): {ex.Message}"); }
        }

        // 청산 후 30s/60s/180s/300s 시점의 가격을 광역스캔 버퍼(_wideHistory, 전 심볼 항상 갱신)에서
        // 읽어 원래 포지션 방향 기준으로 재투영한 변화율을 기록한다(양수=청산 안 했으면 더 벌었을
        // 방향으로 계속 움직임 - 조기청산 신호, 음수=청산 이후 반전 - 청산 타이밍이 맞았다는 신호).
        // ExitPrice 기준점 자체가 근사치이므로 이 값도 근사치임에 유의.
        private async Task TrackPostExitAsync()
        {
            try
            {
                await Task.Delay(30000);
                decimal? p30 = ComputeFwdPct(_mgr.GetLatestWidePrice(Symbol));
                await Task.Delay(30000); // 누적 60s
                decimal? p60 = ComputeFwdPct(_mgr.GetLatestWidePrice(Symbol));
                await Task.Delay(120000); // 누적 180s
                decimal? p180 = ComputeFwdPct(_mgr.GetLatestWidePrice(Symbol));
                await Task.Delay(120000); // 누적 300s
                decimal? p300 = ComputeFwdPct(_mgr.GetLatestWidePrice(Symbol));
                _mgr.LogPostExit(this, p30, p60, p180, p300);
            }
            catch (Exception ex) { _mgr.UI($"❌ [SPIKE] TrackPostExitAsync({Symbol}): {ex.Message}"); }
        }

        private decimal? ComputeFwdPct(decimal? price)
        {
            if (!price.HasValue || ExitPrice <= 0) return null;
            return Side == OrderSide.Buy
                ? (price.Value - ExitPrice) / ExitPrice * 100m
                : (ExitPrice - price.Value) / ExitPrice * 100m;
        }
    }
}