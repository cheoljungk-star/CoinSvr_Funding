using Binance.Net.Enums;
using Binance.Net.Objects.Models.Futures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoinSvr
{
    public sealed class FundingHedger
    {
        public static bool DebugDryRun = false;

        public string Symbol { get; }
        public decimal FundingRate { get; }
        public DateTime FundingTime { get; }

        private decimal _assignedNotional;
        public decimal AssignedNotional
        {
            get => _assignedNotional;
            set => _assignedNotional = _coinLimit > 0 ? Math.Min(value, _coinLimit) : value;
        }

        public decimal FilledQty { get; private set; }
        public bool IsFinished { get; private set; }
        public bool WasSkipped { get; private set; }
        public bool IsEntered { get; private set; }
        public bool IsPrepared { get; private set; }
        public bool IsBait { get; private set; }
        public bool SkippedByTrendFilter { get; private set; } // PreTrendSkipPct에 걸려 스킵된 경우만 true - 사후 시뮬레이션 로깅(LogSkippedOutcomeAsync) 대상 판별용

        private decimal _snapA;
        private decimal _snapB;
        private decimal _peakProfitPct = 0m; // A/B/C 사후분석용 - 보유중 최고수익률 기록
        private decimal _preparedQty;
        private decimal _baitPreparedQty;
        private readonly decimal _coinLimit;
        private decimal? _trend30Pct = null; // PrepareEntry에서 계산한 직전30초 추세(%) - null이면 "데이터부족(hist<30)", 0이면 "진짜 추세없음"을 구분하기 위해 nullable로 관리. 사후분석용(A/B/C 어느 전략이 어떤 추세 국면에 유리한지 판단 근거)
        private readonly FundingHedgeManager _manager;

        // C전략: 펀딩비 정산 후 반락 방향으로 진입
        // 양수 펀딩: 정산 전 숏 쏠림 → 정산 후 반등(상승) → C는 롱
        // 음수 펀딩: 정산 전 롱 쏠림 → 정산 후 반락(하락) → C는 숏
        private readonly OrderSide _mainOrderSide;
        private readonly OrderSide _mainCloseOrderSide;
        private readonly PositionSide _mainPosSide;

        // 미끼(펀딩비 방향, FundingFee 이벤트 트리거용) - 메인(C)과 반대
        private readonly OrderSide _baitOrderSide;
        private readonly OrderSide _baitCloseOrderSide;
        private readonly PositionSide _baitPosSide;

        // ─── C전략 상수 (타이밍 튜닝은 데이터 축적 후) ───────────
        private const int FUNDING_WAIT_TIMEOUT_MS = 3000;   // 정산 트리거 대기 타임아웃 (고정, 자동화 미대상)
        private readonly StrategyConfig _cfg;                // 튜닝 파라미터 (Claude Code 자동화 대상)

        public DateTime _snapATime = DateTime.MinValue;
        public DateTime _snapBTime = DateTime.MinValue;
        public DateTime _entryTime = DateTime.MinValue;
        private decimal _entryPrice = 0m;
        public static TimeSpan ServerTimeOffset = TimeSpan.Zero;
        private DateTime NowUtc => DateTime.UtcNow + ServerTimeOffset;

        public FundingHedger(string symbol, decimal fundingRate, DateTime fundingTime,
            decimal assignedNotional, FundingHedgeManager manager, StrategyConfig config, decimal coinLimit = decimal.MaxValue)
        {
            _cfg = config;
            Symbol = symbol;
            FundingRate = fundingRate;
            FundingTime = fundingTime;
            _manager = manager;
            _coinLimit = coinLimit;
            AssignedNotional = assignedNotional;

            if (fundingRate > 0)
            {
                // 양수: 정산 전 숏이 수령(우리쪽 지불방향=숏) → 정산 후 반등 → C는 롱
                _baitOrderSide = OrderSide.Sell;
                _baitCloseOrderSide = OrderSide.Buy;
                _baitPosSide = PositionSide.Short;

                _mainOrderSide = OrderSide.Buy;
                _mainCloseOrderSide = OrderSide.Sell;
                _mainPosSide = PositionSide.Long;
            }
            else
            {
                // 음수: 정산 전 롱이 수령(우리쪽 지불방향=롱) → 정산 후 반락 → C는 숏
                _baitOrderSide = OrderSide.Buy;
                _baitCloseOrderSide = OrderSide.Sell;
                _baitPosSide = PositionSide.Long;

                _mainOrderSide = OrderSide.Sell;
                _mainCloseOrderSide = OrderSide.Buy;
                _mainPosSide = PositionSide.Short;
            }
        }

        public void RestoreState(decimal filledQty)
        {
            FilledQty = filledQty;
            IsEntered = true;
            IsPrepared = true;
            UI($"🔄 [FUNDING-{Symbol}] 상태 복구: FilledQty={filledQty}");
        }

        private decimal _baitNotional;

        // 이 hedger를 미끼로 지정 (펀딩비 방향 소액 진입 → FundingFee 이벤트 트리거)
        // AssignedNotional(C전략 진입금액)은 건드리지 않고 미끼 전용 금액만 별도 보관
        public void SetBaitMode(decimal minNotional)
        {
            IsBait = true;
            _baitNotional = minNotional;
        }

        public void ResetBaitMode() => IsBait = false;

        // ─── 스냅샷 A (T-3s) ──────────────────────────────────────

        public void TakeSnapshotA()
        {
            try
            {
                var hist = _manager.GetPriceHistory(Symbol);
                lock (hist)
                {
                    if (hist.Count > 0)
                    {
                        _snapA = hist[hist.Count - 1].Price;
                        _snapATime = hist[hist.Count - 1].Time; // 실제 EventTime(호출시각 아님)
                    }
                    else
                    {
                        _snapA = _manager.GetMarkPrice(Symbol); // 이력 없으면 캐시값 폴백
                        _snapATime = DateTime.UtcNow;
                    }
                }

                UI($"📸 [FUNDING-{Symbol}] 스냅샷 A: {_snapA:F6} @ {_snapATime:HH:mm:ss.fff} (펀딩비 {FundingRate:P4}, C방향 {_mainOrderSide}{(IsBait ? ", 미끼" : "")})");
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] TakeSnapshotA: {ex.Message}"); }
        }

        // ─── 스냅샷 B + 수량계산 + 직전추세 필터 (T-1.5s) ────────

        public bool PrepareEntry()
        {
            try
            {
                var histForSnapB = _manager.GetPriceHistory(Symbol);
                lock (histForSnapB)
                {
                    if (histForSnapB.Count > 0)
                    {
                        _snapB = histForSnapB[histForSnapB.Count - 1].Price;
                        _snapBTime = histForSnapB[histForSnapB.Count - 1].Time; // 실제 EventTime
                    }
                }
                if (_snapB <= 0)
                {
                    _snapB = _manager.GetMarkPrice(Symbol); // 이력 없으면 캐시값 폴백
                    _snapBTime = DateTime.UtcNow;
                }
                if (_snapB <= 0)
                {
                    UI($"❌ [FUNDING-{Symbol}] 마크가격 없음 - 스킵");
                    WasSkipped = true;
                    return false;
                }

                bool sameTickAsSnapA = _snapATime != DateTime.MinValue && _snapBTime == _snapATime;
                decimal gapPct = _snapA > 0 ? Math.Abs(_snapB - _snapA) / _snapA : 0m;
                UI($"📸 [FUNDING-{Symbol}] 스냅샷 B: {_snapB:F6} @ {_snapBTime:HH:mm:ss.fff} / 갭 {gapPct:P4} vs 펀딩비 {Math.Abs(FundingRate):P4}" +
                   (sameTickAsSnapA ? " ⚠️SnapA와동일tick" : ""));

                decimal currentRate = _manager.GetFundingRate(Symbol);
                if (Math.Abs(currentRate) < 0.0001m)
                {
                    UI($"❌ [FUNDING-{Symbol}] 펀딩비 소멸 ({currentRate:P4}) - 스킵");
                    WasSkipped = true;
                    return false;
                }

                // ── 직전 30초 추세 필터 ──
                // 쏠림방향(음수펀딩=하락 / 양수펀딩=상승)으로 이미 급행 중이면
                // 반락이 선반영된 상태 → 정산 후 되돌림에 물릴 위험 → 스킵
                // (분석: 강한 순방향 추세 그룹 평균 -0.19%, 나머지 +0.09%)
                var hist = _manager.GetPriceHistory(Symbol);
                lock (hist)
                {
                    if (hist.Count >= 30)
                    {
                        decimal p30 = hist[hist.Count - 30].Price;
                        decimal pNow = hist[hist.Count - 1].Price;
                        if (p30 > 0)
                        {
                            decimal trend30 = (pNow - p30) / p30 * 100;
                            _trend30Pct = trend30; // 데이터 충분 + 계산됨 - 필드에 저장(LogTradeResultJsonl/LogSkippedOutcomeAsync 사후분석용)
                            bool skipCond = FundingRate < 0
                                ? trend30 <= -_cfg.PreTrendSkipPct   // 음수펀딩: 하락 급행이 위험
                                : trend30 >= _cfg.PreTrendSkipPct;   // 양수펀딩: 상승 급행이 위험
                            UI($"📈 [FUNDING-{Symbol}] 직전30s 추세: {trend30:+0.0000;-0.0000}%{(skipCond ? " ← 쏠림방향 급행" : "")}");
                            if (skipCond && !IsBait) // 미끼는 트리거 확보용이라 추세 무관 진행
                            {
                                UI($"🚫 [FUNDING-{Symbol}] 직전추세 필터 - C진입 스킵");
                                SkippedByTrendFilter = true; // 사후 시뮬레이션 로깅 대상으로 표시 - 실거래 없이 정산 후 가격흐름만 추적해 A/B/C_Sim 기록
                                WasSkipped = true;
                                return false;
                            }
                        }
                        // p30 <= 0인 예외 케이스는 _trend30Pct가 null로 남음(비정상 가격 데이터 - 사실상 발생 안 함)
                    }
                    // hist.Count < 30이면 _trend30Pct는 null로 남음 - "데이터부족"과 "추세=0%"를 구분
                }

                if (IsBait)
                {
                    decimal rawBaitQty = _baitNotional / _snapB;
                    decimal step = GetStepSize();
                    _baitPreparedQty = FloorToStep(rawBaitQty, step);
                    if (_baitPreparedQty <= 0)
                    {
                        UI($"❌ [FUNDING-{Symbol}] 미끼 수량 계산 실패 - 스킵");
                        WasSkipped = true;
                        return false;
                    }
                    UI($"🔢 [FUNDING-{Symbol}] 미끼 수량계산: {rawBaitQty:F6} → {_baitPreparedQty} (금액 {_baitNotional:F2}U)");
                }

                // C(반락) 수량은 정산 후 최신가로 재계산할 것이므로 여기선 예비값만
                _preparedQty = CalcQty(_snapB);
                if (_preparedQty <= 0)
                {
                    UI($"❌ [FUNDING-{Symbol}] 수량 계산 실패 - 스킵");
                    WasSkipped = true;
                    return false;
                }

                IsPrepared = true;
                return true;
            }
            catch (Exception ex)
            {
                UI($"❌ [FUNDING-{Symbol}] PrepareEntry 예외: {ex.Message}");
                WasSkipped = true;
                return false;
            }
        }

        public void RecalcQty()
        {
            try
            {
                decimal price = _manager.GetMarkPrice(Symbol);
                if (price > 0) _preparedQty = CalcQty(price);
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] RecalcQty: {ex.Message}"); }
        }

        // SetBaitMode 호출 직후 사용 - 미끼 전용 수량 계산 (PrepareEntry 이후 시점이라 별도 계산 필요)
        public bool RecalcBaitQty()
        {
            try
            {
                decimal price = _manager.GetMarkPrice(Symbol);
                if (price <= 0) return false;
                decimal rawBaitQty = _baitNotional / price;
                decimal step = GetStepSize();
                _baitPreparedQty = FloorToStep(rawBaitQty, step);
                UI($"🔢 [FUNDING-{Symbol}] 미끼 수량계산: {rawBaitQty:F6} → {_baitPreparedQty} (금액 {_baitNotional:F2}U)");
                return _baitPreparedQty > 0;
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] RecalcBaitQty: {ex.Message}"); return false; }
        }

        // ─── 미끼 진입 (정산 전, 펀딩비 방향) ────────────────────

        public async Task<bool> ExecuteBaitEntryAsync(CancellationToken ct)
        {
            try
            {
                UI($"🪤 [FUNDING-{Symbol}] 미끼 진입 시도 (qty={_baitPreparedQty})");
                bool ok = await _manager.PlaceOrderViaSocketAsync(
                    Symbol, _baitOrderSide, FuturesOrderType.Market, _baitPreparedQty, _baitPosSide, reduceOnly: false, isBait: true);
                if (ok)
                    UI($"✅ [FUNDING-{Symbol}] 미끼 진입 완료 ({_baitPreparedQty})");
                else
                    UI($"🚨 [FUNDING-{Symbol}] 미끼 진입 실패");
                return ok;
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] ExecuteBaitEntry 예외: {ex.Message}"); return false; }
        }

        private async Task CloseBaitAsync()
        {
            try
            {
                decimal qty = _manager.GetPositionQty(Symbol, _baitPosSide);
                if (qty <= 0)
                {
                    // 소켓 캐시 미갱신(ListenKey 만료 등) 대비 REST 폴백
                    UI($"⚠️ [FUNDING-{Symbol}] 미끼 캐시 수량 0 - REST 재조회");
                    qty = await _manager.GetPositionQtyRestAsync(Symbol, _baitPosSide);
                }
                if (qty <= 0)
                {
                    UI($"⚠️ [FUNDING-{Symbol}] 미끼 청산할 수량 없음 (REST 확인 결과도 0)");
                    return;
                }
                bool ok = await _manager.PlaceOrderViaSocketAsync(
                    Symbol, _baitCloseOrderSide, FuturesOrderType.Market, qty, _baitPosSide, reduceOnly: true, isBait: true);
                UI(ok ? $"✅ [FUNDING-{Symbol}] 미끼 청산 완료 ({qty})" : $"🚨 [FUNDING-{Symbol}] 미끼 청산 실패 ({qty})");
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] CloseBait: {ex.Message}"); }
        }

        // 안전빵 폴백 전용: 정산 후보군과 무관하게 별도 심볼로 진입한 미끼를
        // 트리거 수신까지만 대기 후 청산. (다른 hedger의 CloseAsync 흐름을 타지 않음)
        public async Task RunBaitOnlyCloseAsync(CancellationToken ct)
        {
            try
            {
                var fundingTask = _manager.WaitForFundingFeeAsync(Symbol);
                await Task.WhenAny(fundingTask, Task.Delay(FUNDING_WAIT_TIMEOUT_MS), Task.Delay(Timeout.Infinite, ct));
                await CloseBaitAsync();
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] RunBaitOnlyClose: {ex.Message}"); }
        }

        // ─── C전략 진입 (정산 직후, 모멘텀 확인 후) ──────────────

        public async Task ExecuteEntryAsync(CancellationToken ct)
        {
            try
            {
                var entryStart = DateTime.UtcNow;
                var serverNow = DateTime.UtcNow + FundingHedger.ServerTimeOffset;
                var nextFunding = _manager.GetNextFundingTime(Symbol);
                var lastEvent = _manager.GetLastEventTime(Symbol);
                UI($"📋 [FUNDING-{Symbol}] C진입시도 | 로컬={entryStart:HH:mm:ss.fff} | 서버={serverNow:HH:mm:ss.fff} | NextFunding={nextFunding:HH:mm:ss.fff} | LastEvent={lastEvent:HH:mm:ss.fff}");
                _entryTime = DateTime.UtcNow;
                bool ok = await _manager.PlaceOrderViaSocketAsync(
                    Symbol, _mainOrderSide, FuturesOrderType.Market, _preparedQty, _mainPosSide, reduceOnly: false, isBait: false);

                // 청산(CloseMainAsync)과 동일하게 주문 완료 '후' 스냅샷으로 통일.
                // 기존엔 주문 전 스냅샷이라 진입~체결 사이 유리한 방향 변동분이 paper에 반영돼
                // C_ProfitPct가 실제 체결보다 낙관적으로 왜곡됐음(8·9회차 paper/실측 괴리 원인 추정).
                _entryPrice = _manager.GetSidePrice(Symbol, _mainOrderSide); // 진입 주문 방향 기준 체결가 (mid 아님 - 스프레드 반영)

                if (ok)
                {
                    FilledQty = _preparedQty;
                    IsEntered = true;
                    var elapsed = (DateTime.UtcNow - entryStart).TotalMilliseconds;
                    UI($"✅ [FUNDING-{Symbol}] C진입완료 ({_preparedQty}) | 소요={elapsed:F0}ms");
                }
                else
                {
                    var elapsed = (DateTime.UtcNow - entryStart).TotalMilliseconds;
                    UI($"🚨 [FUNDING-{Symbol}] C주문 실패 | 소요={elapsed:F0}ms");
                    WasSkipped = true;
                }
            }
            catch (Exception ex)
            {
                UI($"❌ [FUNDING-{Symbol}] ExecuteEntry 예외: {ex.Message}");
                WasSkipped = true;
            }
        }

        // ─── 모멘텀 감지 ─────────────────────────────────────────

        // 최근 ticks개 틱이 모두 반락방향(우리 포지션 이익방향)이면 true.
        // maxWaitMs 내 미확인 시 false (진입 스킵용).
        private async Task<bool> WaitForEntryMomentumAsync(CancellationToken ct)
        {
            try
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(_cfg.EntryMaxWaitMs);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    if (HasConsecutiveMove(_cfg.EntryConfirmTicks, favorable: true))
                        return true;
                    await Task.Delay(200, ct);
                }
                return false;
            }
            catch (TaskCanceledException) { return false; }
            catch { return false; }
        }

        // 보유 중: 진입가 대비 현재 수익률을 계속 추적하며 peak(최고 수익)을 갱신.
        // peak이 _cfg.TrailMinPeakPct 이상 쌓인 뒤, 현재 수익이 peak 대비 _cfg.TrailGivebackPct%만큼
        // 반납되면 청산. 1~2틱 노이즈성 역행은 peak가 크게 안 깎이므로 무시되고,
        // 진짜 반락이 꺾이는 지점만 잡아내 큰 반락은 끝까지 태우면서 되돌림 손실은 제한.
        // maxMs 경과 시 타임아웃 청산.
        private const decimal TRAIL_MIN_GIVEBACK_ABS_PCT = 0.03m; // 절대 반납폭 최소치 - peak이 문턱을 막 넘은 직후 분모가 작아 사소한 흔들림도 비율로는 폭등해 보이는 착시 방지
        private const int TRAIL_GIVEBACK_CONFIRM_TICKS = 2;        // 반납 조건 연속 충족 틱 수(디바운스)
        // 2026-07-20 사람과의 대화 세션: 실거래 giveback이 설정치(35%)를 45~55%까지 초과하는 패턴이
        // 수십 사이클째 반복 확인됨 - 원인은 정산 직후 급격한 반락 구간에서 폴링(50ms) 1~2틱 사이에
        // 비율이 한번에 임계치를 훌쩍 넘어버리는데, 디바운스(2틱 연속확인)가 그 순간에도 그대로
        // 적용되어 확인용 1틱(~50ms)만큼 반납이 더 진행된 뒤에야 청산되기 때문(로그 실측: 트리거~청산
        // 완료 시각이 동일 ms로 찍혀 주문지연은 원인이 아님을 확인, 폴링 갭 자체가 원인).
        // 이미 임계치의 1.3배를 넘는 명백한 급락은 노이즈일 수 없으므로 디바운스를 생략하고 즉시 청산해
        // 추가 1틱만큼의 반납 누적을 막는다(완전 제거는 폴링 구조상 불가능 - 완화 조치).
        private const decimal TRAIL_GIVEBACK_HARD_MULT = 1.3m;

        private async Task WaitForReversalOrTimeoutAsync(int maxMs, CancellationToken ct)
        {
            try
            {
                decimal peakProfitPct = 0m;
                int givebackHitCount = 0;
                var deadline = DateTime.UtcNow.AddMilliseconds(maxMs);
                // 2026-07-20 사람과의 대화 세션: 50ms 고정폴링 대신 BookTicker 이벤트 수신 즉시 반응하도록
                // 변경 - 이 심볼은 아래 등록 구간 동안 100ms 과부하방지 스로틀도 함께 해제되어(RegisterExitMonitor
                // 참고) 반납이 실제 진행되는 속도에 최대한 맞춰 즉시 감지/청산할 수 있다.
                _manager.RegisterExitMonitor(Symbol);
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var remain = deadline - DateTime.UtcNow;
                        if (remain <= TimeSpan.Zero) break;

                        // 청산 주문 방향 기준 체결가 (mid 아님) - BookTicker 실시간, MarkPrice(1초)보다 정밀
                        decimal price = _manager.GetSidePrice(Symbol, _mainCloseOrderSide);
                        if (price > 0 && _entryPrice > 0)
                        {
                            decimal change = (price - _entryPrice) / _entryPrice * 100m;
                            // 숏이면 하락이 이익이므로 부호 반전해 "수익률"로 통일
                            decimal profitPct = _mainPosSide == PositionSide.Short ? -change : change;

                            if (profitPct > peakProfitPct) peakProfitPct = profitPct;
                            _peakProfitPct = peakProfitPct;

                            if (peakProfitPct >= _cfg.TrailMinPeakPct)
                            {
                                decimal giveback = peakProfitPct - profitPct;
                                decimal givebackRatio = giveback / peakProfitPct * 100m;

                                // 절대 반납폭(giveback)이 노이즈 수준을 넘고 비율도 임계치를 넘는 두 조건을
                                // 모두 만족해야 카운트. 여기에 더해 연속 확인틱까지 요구해 순간 노이즈를 이중 필터링.
                                if (giveback >= TRAIL_MIN_GIVEBACK_ABS_PCT && givebackRatio >= _cfg.TrailGivebackPct)
                                {
                                    if (givebackRatio >= _cfg.TrailGivebackPct * TRAIL_GIVEBACK_HARD_MULT)
                                    {
                                        UI($"⚡ [FUNDING-{Symbol}] 급격한 반납 감지(임계치 x{TRAIL_GIVEBACK_HARD_MULT} 초과) - 디바운스 생략 즉시청산 - peak={peakProfitPct:+0.0000;-0.0000}% 현재={profitPct:+0.0000;-0.0000}% 반납={givebackRatio:F0}% (절대반납={giveback:F4}%, 방향별체결가기준)");
                                        return;
                                    }
                                    givebackHitCount++;
                                    if (givebackHitCount >= TRAIL_GIVEBACK_CONFIRM_TICKS)
                                    {
                                        UI($"↩️ [FUNDING-{Symbol}] 트레일링청산 - peak={peakProfitPct:+0.0000;-0.0000}% 현재={profitPct:+0.0000;-0.0000}% 반납={givebackRatio:F0}% (절대반납={giveback:F4}%, {TRAIL_GIVEBACK_CONFIRM_TICKS}틱 연속확인, 방향별체결가기준)");
                                        return;
                                    }
                                }
                                else
                                {
                                    givebackHitCount = 0;
                                }
                            }
                        }
                        // 폴링 대신 다음 BookTicker 이벤트(또는 남은 시한) 도착까지 대기 - 이벤트 즉시 재평가
                        await _manager.WaitForNextBookTickerTickAsync(Symbol, remain, ct);
                    }
                }
                finally { _manager.UnregisterExitMonitor(Symbol); }
                UI($"⏰ [FUNDING-{Symbol}] 보유 타임아웃({_cfg.MaxHoldMs}ms) - 청산 (peak={peakProfitPct:+0.0000;-0.0000}%)");
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] WaitForReversal: {ex.Message}"); }
        }

        // 최근 ticks개 틱 연속 이동 판정.
        // favorable=true: 포지션 이익방향(숏=하락, 롱=상승) 연속이면 true
        // favorable=false: 포지션 손실방향(숏=상승, 롱=하락) 연속이면 true
        private bool HasConsecutiveMove(int ticks, bool favorable)
        {
            try
            {
                // BookTicker 롤링 히스토리 사용 - MarkPrice(1초)는 보합(같은 값) 틱이 잦아
                // "연속성 깨짐" 오판정으로 모멘텀 확인 자체가 과도하게 실패하는 문제가 있었음.
                var hist = _manager.GetBookTickerHistory(Symbol);
                List<(DateTime Time, decimal Mid)> tail;
                lock (hist)
                {
                    if (hist.Count < ticks + 1) return false;
                    tail = hist.Skip(hist.Count - (ticks + 1)).ToList();
                }
                for (int k = 1; k < tail.Count; k++)
                {
                    decimal prev = tail[k - 1].Mid;
                    decimal cur = tail[k].Mid;
                    bool up = cur > prev;
                    bool down = cur < prev;
                    // 보합(같은 가격)은 연속성 깨짐으로 처리
                    bool favorableMove = _mainPosSide == PositionSide.Short ? down : up;
                    bool wanted = favorable ? favorableMove
                                            : (_mainPosSide == PositionSide.Short ? up : down);
                    if (!wanted) return false;
                }
                return true;
            }
            catch { return false; }
        }

        // ─── C전략 메인 흐름 ─────────────────────────────────────
        // 정산 트리거 대기 → 미끼 청산 → 반락모멘텀 확인 → C진입
        // → 최소보유 → 되돌림 감지/타임아웃 → 청산

        public async Task CloseAsync(CancellationToken ct)
        {
            try
            {
                // 복구된 포지션이면 바로 청산 흐름으로
                if (IsEntered)
                {
                    await Task.Delay(_cfg.MinHoldMs, ct);
                    await WaitForReversalOrTimeoutAsync(_cfg.MaxHoldMs - _cfg.MinHoldMs, ct);
                    await CloseMainAsync();
                    return;
                }

                if (!IsPrepared) { IsFinished = true; return; }

                UI($"⏳ [FUNDING-{Symbol}] 펀딩 정산 트리거 대기 (C전략)...");
                var fundingTask = _manager.WaitForFundingFeeAsync(Symbol);
                var winner = await Task.WhenAny(fundingTask, Task.Delay(FUNDING_WAIT_TIMEOUT_MS), Task.Delay(Timeout.Infinite, ct));

                if (IsBait) await CloseBaitAsync();

                if (winner != fundingTask)
                {
                    if (ct.IsCancellationRequested)
                    {
                        UI($"⚠️ [FUNDING-{Symbol}] 취소 - C진입 스킵");
                    }
                    else
                    {
                        UI($"⚠️ [FUNDING-{Symbol}] 정산 트리거 미수신({FUNDING_WAIT_TIMEOUT_MS}ms) - C진입 스킵");
                        // 트리거 자체를 못 받아 실제 정산시각을 모름 - 후보 선정 시 알고 있던 FundingTime(거래소 정산 스케줄)을 앵커로 사용
                        await RunSkipSimulationCoreAsync(ct, "FundingTriggerTimeout", FundingTime);
                    }
                    WasSkipped = true;
                    return;
                }

                DateTime settleReceivedTime = DateTime.UtcNow; // 트리거 수신 시각 - 아래 모멘텀미확인 케이스의 사후시뮬 앵커로 재사용

                // 미끼는 트리거 역할 종료. C진입은 추세필터 통과한 non-bait만? → 미끼도 C진입은 수행
                // (미끼는 PrepareEntry에서 추세필터를 우회했으므로 여기서 재확인)
                if (IsBait)
                {
                    // 미끼 심볼도 C진입 전 동일 모멘텀 조건 적용 (아래 공통 로직)
                }

                UI($"💰 [FUNDING-{Symbol}] 정산 확인 → 반락 모멘텀 확인 중 ({_cfg.EntryConfirmTicks}틱, 최대 {_cfg.EntryMaxWaitMs}ms)");
                bool confirmed = await WaitForEntryMomentumAsync(ct);
                if (!confirmed)
                {
                    UI($"🚫 [FUNDING-{Symbol}] 반락 모멘텀 미확인 - C진입 스킵");
                    // 정산은 확인했으나 반락 조짐이 안 보여 진입 안 한 케이스 - 트리거 수신 시각을 앵커로 사후시뮬
                    await RunSkipSimulationCoreAsync(ct, "MomentumNotConfirmed", settleReceivedTime);
                    WasSkipped = true;
                    return;
                }

                UI($"💰 [FUNDING-{Symbol}] 반락 모멘텀 확인 → C진입 (반락방향 {_mainOrderSide})");
                RecalcQty();
                if (_preparedQty <= 0)
                {
                    UI($"❌ [FUNDING-{Symbol}] C수량 계산 실패 - 스킵");
                    WasSkipped = true;
                    return;
                }
                await ExecuteEntryAsync(ct);
                if (!IsEntered) return;

                // 최소 보유 후 되돌림 감지 시 조기청산, 최대 _cfg.MaxHoldMs
                await Task.Delay(_cfg.MinHoldMs, ct);
                await WaitForReversalOrTimeoutAsync(_cfg.MaxHoldMs - _cfg.MinHoldMs, ct);
                await CloseMainAsync();
            }
            catch (TaskCanceledException)
            {
                if (IsEntered)
                {
                    UI($"⚠️ [FUNDING-{Symbol}] 취소 → 강제 청산");
                    await CloseMainAsync();
                }
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] CloseAsync: {ex.Message}"); }
            finally { IsFinished = true; }
        }
        // 스킵된 케이스의 사후 시뮬레이션 공통 로직 - 실거래 없이 정산 후 가격 흐름만 관찰해
        // A/B/C를 만약 진입했다면의 근사 결과를 skipped_results.jsonl에 기록.
        // settleTimeAnchor: 이 시점부터 MaxHoldMs만큼의 가격 창을 관찰(실제 트리거 수신시각 or FundingTime 근사).
        private async Task RunSkipSimulationCoreAsync(CancellationToken ct, string skipReason, DateTime settleTimeAnchor)
        {
            try
            {
                DateTime deadline = settleTimeAnchor.AddMilliseconds(_cfg.MaxHoldMs);
                var remain = deadline - DateTime.UtcNow;
                if (remain > TimeSpan.Zero) await Task.Delay(remain, ct);

                List<(DateTime Time, decimal Price)> window;
                var history = _manager.GetPriceHistory(Symbol);
                lock (history)
                {
                    window = history.Where(p => p.Time >= settleTimeAnchor).OrderBy(p => p.Time).ToList();
                }
                if (window.Count < 2)
                {
                    UI($"🧪 [FUNDING-{Symbol}] 스킵기록({skipReason}): 정산 후 가격 데이터 부족 - 기록 안 함");
                    return;
                }

                // 가상 반락진입가 - 실체결 아님(MarkPrice 근사, 방향별 체결가 미적용). C_ProfitPct_Sim 계산 기준점.
                decimal simEntryPrice = window[0].Price;
                decimal peak = 0m;
                int givebackHitCount = 0;
                decimal simClosePrice = window[window.Count - 1].Price;
                bool exited = false;

                for (int i = 1; i < window.Count && !exited; i++)
                {
                    decimal change = (window[i].Price - simEntryPrice) / simEntryPrice * 100m;
                    decimal profitPct = _mainPosSide == PositionSide.Short ? -change : change;
                    if (profitPct > peak) peak = profitPct;

                    if (peak >= _cfg.TrailMinPeakPct)
                    {
                        decimal giveback = peak - profitPct;
                        decimal givebackRatio = peak > 0 ? giveback / peak * 100m : 0m;
                        if (giveback >= TRAIL_MIN_GIVEBACK_ABS_PCT && givebackRatio >= _cfg.TrailGivebackPct)
                        {
                            if (givebackRatio >= _cfg.TrailGivebackPct * TRAIL_GIVEBACK_HARD_MULT)
                            {
                                simClosePrice = window[i].Price;
                                exited = true;
                            }
                            else
                            {
                                givebackHitCount++;
                                if (givebackHitCount >= TRAIL_GIVEBACK_CONFIRM_TICKS)
                                {
                                    simClosePrice = window[i].Price;
                                    exited = true;
                                }
                            }
                        }
                        else givebackHitCount = 0;
                    }
                }

                decimal finalChange = (simClosePrice - simEntryPrice) / simEntryPrice * 100m;
                decimal cProfitPctSim = _mainPosSide == PositionSide.Short ? -finalChange : finalChange;

                // A/B_Est는 실거래와 동일한 산식(정산 전 스냅샷 기반) - 반락 여부와 무관하게 그대로 계산 가능
                bool fundingDirIsShort = _mainPosSide == PositionSide.Long;
                decimal aRaw = _snapA > 0 ? (_snapB - _snapA) / _snapA * 100m : 0m;
                decimal bRaw = _snapB > 0 && simEntryPrice > 0 ? (simEntryPrice - _snapB) / _snapB * 100m : 0m;
                decimal aProfitPct = fundingDirIsShort ? -aRaw : aRaw;
                // B_Est 버그수정(2026-07-15): 기존엔 SnapB→진입가 가격변동분만 계산하고
                // B전략의 핵심 수익원인 펀딩비 자체가 계산식에서 누락되어 있었음(기록만 되고 미반영).
                // B는 항상 펀딩비를 "수령"하는 방향으로 진입하는 게 전제이므로 항상 +로 가산.
                decimal bProfitPct = (fundingDirIsShort ? -bRaw : bRaw) + Math.Abs(FundingRate) * 100m;

                var record = new
                {
                    Timestamp = DateTime.UtcNow,
                    Symbol,
                    FundingRatePct = FundingRate * 100m,
                    MainPosSide = _mainPosSide.ToString(),
                    IsSkipped = true,
                    SnapA_EventTime = _snapATime,
                    SnapB_EventTime = _snapBTime,
                    SnapSameTick = _snapATime != DateTime.MinValue && _snapATime == _snapBTime, // true면 두 스냅샷이 같은 tick(캐시재읽기) 확정
                    SkipReason = skipReason,   // "PreTrendSkip" / "FundingTriggerTimeout" / "MomentumNotConfirmed"
                    Trend30Pct = _trend30Pct,
                    C_ProfitPct_Sim = cProfitPctSim,   // C를 진입했다면의 근사 수익률(MarkPrice 기반, 실체결가 아님)
                    C_PeakProfitPct_Sim = peak,
                    SnapAPrice = _snapA,
                    SnapBPrice = _snapB,
                    A_ProfitPct_Est = aProfitPct,
                    B_ProfitPct_Est = bProfitPct,
                    Config = new
                    {
                        _cfg.TrailGivebackPct,
                        _cfg.TrailMinPeakPct,
                        _cfg.MinFundingPct,
                        _cfg.EntryConfirmTicks,
                        _cfg.EntryMaxWaitMs,
                        _cfg.PreTrendSkipPct,
                        _cfg.MinHoldMs,
                        _cfg.MaxHoldMs
                    }
                };
                string path = Path.Combine(AppContext.BaseDirectory, "skipped_results.jsonl"); // 실거래 로그(trade_results.jsonl)와 분리
                File.AppendAllText(path, System.Text.Json.JsonSerializer.Serialize(record) + Environment.NewLine);
                UI($"🧪 [FUNDING-{Symbol}] 스킵기록 완료({skipReason}): C_Sim={cProfitPctSim:+0.0000;-0.0000}%, A_Est={aProfitPct:+0.0000;-0.0000}%, B_Est={bProfitPct:+0.0000;-0.0000}%");
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] RunSkipSimulationCoreAsync({skipReason}): {ex.Message}"); }
        }

        private async Task CloseMainAsync()
        {
            try
            {
                decimal qty = _manager.GetPositionQty(Symbol, _mainPosSide);
                if (qty <= 0) qty = FilledQty;
                if (qty <= 0)
                {
                    UI($"⚠️ [FUNDING-{Symbol}] 캐시/FilledQty 수량 0 - REST 재조회");
                    qty = await _manager.GetPositionQtyRestAsync(Symbol, _mainPosSide);
                }

                if (qty <= 0)
                {
                    UI($"⚠️ [FUNDING-{Symbol}] 청산할 수량 없음 (REST 확인 결과도 0)");
                    return;
                }

                DateTime closeRequestTime = DateTime.UtcNow;
                bool ok = await _manager.PlaceOrderViaSocketAsync(
                    Symbol, _mainCloseOrderSide, FuturesOrderType.Market, qty, _mainPosSide, reduceOnly: true, isBait: false);

                // 청산주문 직후 스냅샷 - PnL 델타 대기(최대3s) 중 가격변동으로 close값이 오염되는 것 방지
                decimal closeSnapshotPrice = _manager.GetSidePrice(Symbol, _mainCloseOrderSide);

                if (ok)
                    UI($"✅ [FUNDING-{Symbol}] C청산 완료 ({qty})");
                else
                    UI($"🚨 [FUNDING-{Symbol}] C청산 실패 - 수동 확인 필요!");

                // 실제 체결 기준 실현손익(RealizedPnl) 델타 대기 - MarkPrice 추정치(C_ProfitPct)와
                // 나란히 기록해 슬리피지/추정오차를 검증할 수 있게 함. lock 밖에서 await(동기 lock 안에서 불가).
                decimal? actualRealizedPnlUsdt = ok
                    ? await _manager.WaitForRealizedPnlDeltaAsync(Symbol, _mainPosSide, closeRequestTime)
                    : null;
                if (ok && actualRealizedPnlUsdt == null)
                    UI($"⚠️ [FUNDING-{Symbol}] 실제 실현손익 델타 미수신(타임아웃) - 추정치만 기록");

                var history = _manager.GetPriceHistory(Symbol);
                lock (history)
                {
                    // entry/close 모두 히스토리 조회 대신 실제 체결 방향가 필드로 통일 (ExecuteEntryAsync/CloseMainAsync에서 GetSidePrice로 캡처한 값)
                    var close = (Time: closeRequestTime, Price: closeSnapshotPrice);

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"📊 [{Symbol}] 가격 히스토리:");
                    foreach (var h in history)
                    {
                        string tag = "";
                        var candidates = new (string Name, DateTime Time)[] { ("스냅샷A", _snapATime), ("스냅샷B", _snapBTime), ("진입", _entryTime), ("청산", close.Time) };
                        var closest = candidates
                            .Select(c => (c.Name, Diff: Math.Abs((h.Time - c.Time).TotalMilliseconds)))
                            .Where(c => c.Diff < 600)
                            .OrderBy(c => c.Diff)
                            .FirstOrDefault();
                        if (closest.Name != null) tag = $" ◀ {closest.Name}";
                        sb.AppendLine($"  {h.Time:HH:mm:ss.fff} {h.Price:F6}{tag}");
                    }
                    decimal entryVsCloseDiff = _entryPrice > 0 ? (close.Price - _entryPrice) / _entryPrice * 100 : 0;
                    sb.AppendLine();
                    sb.Append($"  C진입→청산 변화(추정): {entryVsCloseDiff:+0.0000;-0.0000}%");
                    if (actualRealizedPnlUsdt != null)
                        sb.Append($" | 실제 실현손익: {actualRealizedPnlUsdt:+0.0000;-0.0000}$");
                    UI(sb.ToString());

                    // Claude Code 자동화용 구조화 로그 - 매 트레이드마다 한 줄(JSON) 추가.
                    // 텍스트 로그 정규식 파싱 대신 이 파일 하나만 읽으면 분석 가능하게.
                    LogTradeResultJsonl(_entryPrice, close.Price, entryVsCloseDiff, actualRealizedPnlUsdt);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(15000);
                            var hist2 = _manager.GetPriceHistory(Symbol);
                            lock (hist2)
                            {
                                var afterClose = hist2.Where(p => p.Time > close.Time).ToList();
                                if (afterClose.Any())
                                {
                                    var sb2 = new System.Text.StringBuilder();
                                    sb2.AppendLine($"📉 [{Symbol}] 청산 후 반락 추적:");
                                    foreach (var p in afterClose)
                                    {
                                        decimal diffFromClose = (p.Price - close.Price) / close.Price * 100;
                                        sb2.AppendLine($"  {p.Time:HH:mm:ss.fff} {p.Price:F6} (청산대비 {diffFromClose:+0.0000;-0.0000}%)");
                                    }
                                    UI(sb2.ToString());
                                }
                            }
                        }
                        catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] 반락추적: {ex.Message}"); }
                    });
                }
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] CloseMain: {ex.Message}"); }
        }

        // ─── 스킵 케이스 사후 시뮬레이션 (신규) ──────────────────
        // PreTrendSkipPct로 C진입 자체를 안 한 케이스의 사후 결과 기록.
        // 실거래 없음(주문 전혀 안 함) - 이미 구독 중인 MarkPrice 흐름만 관찰해
        // A/B/C를 만약 진입했다면의 근사 결과를 skipped_results.jsonl에 남긴다.
        // 목적: trade_results.jsonl만으로는 "필터가 걸러낸 케이스"가 통째로 빠진 편향 데이터가 되므로,
        // 그 빈틈을 메워 필터 자체가 올바른 판단이었는지도 사후 검증 가능하게 함.
        // PreTrendSkipPct로 PrepareEntry에서 스킵된 케이스 전용 - 이 경로는 CloseAsync 자체가 호출되지 않으므로
        // (DispatchAsync가 IsPrepared=false hedger는 RunCloseAsync에 넘기지 않음) 정산 트리거를 직접 대기해야 함.
        // FundingTriggerTimeout/MomentumNotConfirmed 케이스는 CloseAsync 내부에서 RunSkipSimulationCoreAsync를 직접 호출함(이미 트리거를 확인한 상태이므로).
        public async Task LogSkippedOutcomeAsync(CancellationToken ct)
        {
            try
            {
                var fundingTask = _manager.WaitForFundingFeeAsync(Symbol);
                var winner = await Task.WhenAny(fundingTask, Task.Delay(FUNDING_WAIT_TIMEOUT_MS), Task.Delay(Timeout.Infinite, ct));
                if (winner != fundingTask)
                {
                    UI($"🧪 [FUNDING-{Symbol}] 스킵기록(PreTrendSkip): 정산 트리거 미수신 - 기록 안 함");
                    return;
                }
                DateTime settleTime = DateTime.UtcNow;
                await RunSkipSimulationCoreAsync(ct, "PreTrendSkip", settleTime);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] LogSkippedOutcomeAsync: {ex.Message}"); }
        }

        // ─── 유틸 ─────────────────────────────────────────────────

        private decimal GetStepSize()
        {
            try
            {
                var symbolInfo = MockExchange.FindSymbolInfo(Symbol);
                if (symbolInfo != null)
                {
                    var pi = symbolInfo.GetType().GetProperty("LotSizeFilter");
                    var filter = pi?.GetValue(symbolInfo);
                    if (filter != null)
                    {
                        var sp = filter.GetType().GetProperty("StepSize");
                        var val = sp?.GetValue(filter);
                        if (val != null && decimal.TryParse(val.ToString(), out decimal s) && s > 0)
                            return s;
                    }
                }
            }
            catch { }
            return 1m;
        }

        // 심볼별 최소 주문 금액(USDT) 조회 - LotSizeFilter와 동일 리플렉션 패턴
        public static decimal GetMinNotional(string symbol)
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
                            var np = f.GetType().GetProperty("MinNotional");
                            if (np != null)
                            {
                                var val = np.GetValue(f);
                                if (val != null && decimal.TryParse(val.ToString(), out decimal m) && m > 0)
                                    return m;
                            }
                        }
                    }
                }
            }
            catch { }
            return 5m;
        }

        // 심볼별 최소 호가단위(tickSize) 조회 - GetMinNotional과 동일 리플렉션 패턴.
        // GWEIUSDT 사례(2026-07-15)로 발견: tickSize가 가격 대비 크면(예: 0.21%) 진입+청산 왕복만으로
        // 구조적 스프레드 손실이 발생함 - 후보선정 필터(tick/price 비율)용.
        public static decimal GetTickSize(string symbol)
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
                            var tp = f.GetType().GetProperty("TickSize");
                            if (tp != null)
                            {
                                var val = tp.GetValue(f);
                                if (val != null && decimal.TryParse(val.ToString(), out decimal t) && t > 0)
                                    return t;
                            }
                        }
                    }
                }
            }
            catch { }
            return 0m; // 0이면 호출측에서 필터 적용 안 함(정보 없음 - 안전하게 통과)
        }

        // tick/price 비율(%) - 클수록 왕복 스프레드 비용이 구조적으로 큰 심볼.
        public static decimal GetTickPriceRatioPct(string symbol, decimal currentPrice)
        {
            if (currentPrice <= 0) return 0m;
            decimal tick = GetTickSize(symbol);
            if (tick <= 0) return 0m;
            return tick / currentPrice * 100m;
        }

        private decimal FloorToStep(decimal qty, decimal step) =>
            Math.Floor(qty / step) * step;

        private decimal CalcQty(decimal price)
        {
            try
            {
                if (price <= 0) return 0;
                decimal rawQty = AssignedNotional / price;
                decimal step = GetStepSize();
                decimal qty = FloorToStep(rawQty, step);
                UI($"🔢 [FUNDING-{Symbol}] 수량계산: {rawQty:F6} → {qty}");
                return qty;
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] CalcQty: {ex.Message}"); return 0; }
        }

        // Claude Code 자동화가 파싱할 구조화 트레이드 결과 로그.
        // trade_results.jsonl에 한 줄씩 append (JSON Lines 포맷).
        // A/B/C 세 플랜을 사후 계산할 수 있도록 원시 가격 스냅샷을 함께 남긴다.
        //   A = 정산 전 추세를 타고 정산 직전 익절 (SnapAPrice → SnapBPrice, 쏠림방향 기준)
        //   B = 정산 직전 진입해 펀딩비 수령 후 정리 (SnapBPrice → FundingSettlePrice, 펀딩방향 기준. 구 전략)
        //   C = 정산 후 반락방향 진입 → 트레일링 청산 (현재 실행 전략. EntryPrice → ClosePrice)
        private void LogTradeResultJsonl(decimal entryPrice, decimal closePrice, decimal changePct, decimal? actualRealizedPnlUsdt)
        {
            try
            {
                decimal profitPct = _mainPosSide == PositionSide.Short ? -changePct : changePct;

                decimal? actualProfitPct = actualRealizedPnlUsdt.HasValue && AssignedNotional > 0
                    ? actualRealizedPnlUsdt.Value / AssignedNotional * 100m
                    : (decimal?)null;
                decimal? estimateErrorPct = actualProfitPct.HasValue ? profitPct - actualProfitPct.Value : null;

                bool fundingDirIsShort = _mainPosSide == PositionSide.Long;
                decimal aRaw = _snapA > 0 ? (_snapB - _snapA) / _snapA * 100m : 0m;
                decimal bRaw = _snapB > 0 && entryPrice > 0 ? (entryPrice - _snapB) / _snapB * 100m : 0m;
                decimal aProfitPct = fundingDirIsShort ? -aRaw : aRaw;
                // B_Est 버그수정(2026-07-15): 위 RunSkipSimulationCoreAsync와 동일 사유 - 펀딩비 자체 가산.
                decimal bProfitPct = (fundingDirIsShort ? -bRaw : bRaw) + Math.Abs(FundingRate) * 100m;

                var record = new
                {
                    Timestamp = DateTime.UtcNow,
                    Symbol,
                    FundingRatePct = FundingRate * 100m,
                    MainPosSide = _mainPosSide.ToString(),
                    IsBait,
                    SnapA_EventTime = _snapATime,
                    SnapB_EventTime = _snapBTime,
                    SnapSameTick = _snapATime != DateTime.MinValue && _snapATime == _snapBTime,
                    Trend30Pct = _trend30Pct, // nullable - null=데이터부족(hist<30 등), 값 있음=실제 계산된 추세(0%도 유효한 "추세없음"으로 구분됨)
                    C_EntryPrice = entryPrice,
                    C_ClosePrice = closePrice,
                    C_ProfitPct = profitPct,
                    C_PeakProfitPct = _peakProfitPct,
                    Actual_RealizedPnlUsdt = actualRealizedPnlUsdt,
                    Actual_ProfitPct = actualProfitPct,
                    Estimate_Error_Pct = estimateErrorPct,
                    SnapAPrice = _snapA,
                    SnapBPrice = _snapB,
                    A_ProfitPct_Est = aProfitPct,
                    B_ProfitPct_Est = bProfitPct,
                    Config = new
                    {
                        _cfg.TrailGivebackPct,
                        _cfg.TrailMinPeakPct,
                        _cfg.MinFundingPct,
                        _cfg.EntryConfirmTicks,
                        _cfg.EntryMaxWaitMs,
                        _cfg.PreTrendSkipPct,
                        _cfg.MinHoldMs,
                        _cfg.MaxHoldMs
                    }
                };
                string path = Path.Combine(AppContext.BaseDirectory, "trade_results.jsonl");
                File.AppendAllText(path, System.Text.Json.JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch (Exception ex) { UI($"❌ [FUNDING-{Symbol}] LogTradeResultJsonl: {ex.Message}"); }
        }

        private void UI(string msg) { try { Ob.ui?.SetText(msg); } catch { } }
    }
}