using Binance.Net.Enums;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Spot;
using Google.Protobuf.Compiler;
using MySql.Data.MySqlClient;
using MySqlX.XDevAPI.Common;
using Nancy.Json;
using Org.BouncyCastle.Ocsp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CoinSvr
{
    public sealed class HedgeGridManager
    {
        public readonly string _symbol;
        private readonly IExchange _ex;
        private readonly RiskConfig _rc;
        //private readonly TradeDataCollector _collector;

        private bool _isProcessingOrder = false; // 주문 처리 중임을 알리는 플래그

        private readonly SemaphoreSlim _sem = new SemaphoreSlim(1, 1);

        public long _positionId;
        private decimal _initialBudget = 10m;
        private decimal _leverage = 10m;

        private List<Trade> _longTrades = new();
        private List<Trade> _shortTrades = new();

        private decimal _longTotalQty;
        private decimal _longAvgPrice;
        private decimal _longTotalCost;

        private decimal _shortTotalQty;
        private decimal _shortAvgPrice;
        private decimal _shortTotalCost;

        private decimal _recoveryBasePrice;
        private string _recoverySide = "NONE";
        private const decimal RECOVERY_EXIT_PCT = 0.35m;
        private const decimal HEDGE_RATIO = 1.10m;

        private int _TotalTrades;

        private DateTime _lastAddTime = DateTime.MinValue;
        private decimal _targetPnl = 0.02m;
        private decimal _maxBudgetUsed = 0m;

        private DateTime _lastDbUpdate = DateTime.MinValue;
        private const int DB_UPDATE_INTERVAL_SEC = 60;

        private DateTime _entryTime;

        public DateTime _lastSyncTime = DateTime.MinValue;
        private DateTime _lastSyncOrderTime = DateTime.MinValue;
        public DateTime LongMarginAddDt { get; set; } = DateTime.MinValue;
        public DateTime ShortMarginAddDt { get; set; } = DateTime.MinValue;
        public DateTime _lastAddPositionTime { get; set; } = DateTime.MinValue;

        private DateTime _idChangedTime = DateTime.MinValue; // ID 변경 시점 기록
        public int CfgMaxTrade { get; set; }
        private DateTime _lastOrderTime = DateTime.MinValue;
        private decimal _maxRecordedTotalPnl = 0m;

        const decimal MIN_EXECUTION_USD = 9.0m;
        const decimal MIN_RECOVERY_USD = 20.0m;
        const int RECOVERY_COOLDOWN_MIN = 60;        // 🚩 수선 후 강제 휴식 (10분)

        private DateTime _lastRecoveryTime = DateTime.MinValue; // [추가] 마지막 리밸런싱 시간 기록

        // 필터 타입별 마지막 로그 시간을 저장 (메모리 내 관리)
        private readonly ConcurrentDictionary<string, DateTime> _lastFilterLogTimes = new();

        private DateTime _lastReHedgeTime = DateTime.MinValue;
        private int _reHedgeCount = 0; // 특정 사이클 내 리헤징 횟수 제한용
        private const int REHEDGE_COOLDOWN_MINUTES = 5;

        private decimal _pendingRecoveryUsd = 0m; // 🚩 수선 대기 자금 (증거금 기준)
        private string _pendingTargetSide = "NONE";  // "LONG" 또는 "SHORT" (추매할 방향)
        private DateTime _lastAddFailTime = DateTime.MinValue;
        private DateTime _lastOnTickTime = DateTime.Now;
        private DateTime _lastDebugSyncLogTime = DateTime.MinValue; // 🔍 로그 쿨타임용
        private DateTime _lastRecoveryExitTime = DateTime.MinValue; // 💸 익절 후 재매수 방지용
        private DateTime _lastTickLogTime = DateTime.MinValue;
        private DateTime _totalImmunityTime = DateTime.MinValue;
        private DateTime _totalImmunityTime2 = DateTime.MinValue;
        private DateTime _lastGoldenLogTime = DateTime.MinValue; // 로그 전용 쿨타임 변수

        private int _longSyncRejectCount = 0; // 클래스 멤버 변수로 선언
        private int _shortSyncRejectCount = 0;
        public int _version = 1; // 기본값 1 (기존 로직)


        private decimal _lastTpPrice = 0m; // 메모리 상의 마지막 익절가

        /// <summary>
        /// 최근 1시간 내 로그 출력 여부를 확인하기 위한 타임스탬프
        /// </summary>
        private DateTime _lastConfidenceLog = DateTime.Now.AddHours(-1);

        /// <summary>
        /// 최근 24시간 동안 관측된 AI 예측의 최대 신뢰도 (0.0 ~ 1.0)
        /// </summary>
        private decimal _maxConfidence24h = 0m;
        private decimal _maxNotionalCap = 50.0m; // 기본값

        /// <summary>
        /// API 지연을 방지하기 위한 로컬 가상 격리 잔고
        /// </summary>
        public decimal VirtualIsolatedWallet { get; set; }

        /// <summary>
        /// API 데이터와 가상 잔고를 동기화한 마지막 시간
        /// </summary>
        public DateTime LastSyncDt { get; set; }

        public HedgeGridManager(string symbol, IExchange ex, RiskConfig rc)
        {
            _symbol = symbol;
            _ex = ex;
            _rc = rc;
            //_collector = new TradeDataCollector();

            _initialBudget = rc.HedgeGridInitialBudget;
            _leverage = rc.Leverage;
            _targetPnl = rc.HedgeGridTargetProfitPct;
            CfgMaxTrade = rc.HedgeGridMaxTotalTrades;
        }

        // ✅ 락 헬퍼 (변수 할당 에러 방지를 위해 튜플 반환 적극 사용)
        private async Task<T> WithLock<T>(Func<Task<T>> action)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // 3초 동안 기다려보고 락을 못 잡으면 예외 대신 기본값 반환
            if (await _sem.WaitAsync(3000).ConfigureAwait(false))
            {
                try
                {
                    if (sw.ElapsedMilliseconds > 500) UI($"ℹ️ [LOCK-WAIT] <{_symbol}> 락 획득 소요: {sw.ElapsedMilliseconds}ms");
                    return await action();
                }
                finally { _sem.Release(); }
            }
            else
            {
                UI($"⚠️ [LOCK-TIMEOUT] <{_symbol}> 락 획득 실패 (3s)");
                return default; // 또는 적절한 에러 처리
            }
        }
        private async Task WithLock(Func<Task> action)
        {
            if (await _sem.WaitAsync(3000).ConfigureAwait(false))
            {
                try { await action(); }
                finally { _sem.Release(); }
            }
            else
            {
                UI($"⚠️ [LOCK-TIMEOUT] <{_symbol}> 락 획득 실패 (3s)");
            }
        }
        

        public async Task<bool> InitializeAsync(decimal initPrice, bool isLong)
        {
            try
            {
                _version = 9;
                _lastOrderTime = DateTime.Now;
                var notional = _initialBudget * _leverage;
                var qty = _ex.RoundQty(_symbol, notional / initPrice);
                _entryTime = DateTime.Now;

                var ret = await _ex.PlaceMarketOrderAsync(_symbol, isLong ? Side.Buy : Side.Sell, qty, false);
                if (ret == null) { UI($"❌ [GRID] <{_symbol}> 초기 주문 실패"); return false; }

                _positionId = await Ob.db._dbMaria.HedgeGrid_InsertAsync(new HedgeGridPositionRow { symbol = _symbol, initial_budget = _initialBudget, entry_time = DateTime.Now, entry_time_utc = DateTime.Now, long_total_qty = ret.Quantity, long_avg_price = initPrice, status = "OPEN", CfgMaxTrade = CfgMaxTrade, version = _version });
                _idChangedTime = DateTime.Now;
                await WithLock(async () =>
                {
                    var estimatedCost = ret.Quantity * initPrice / _leverage;
                    if (isLong)
                    {
                        _longTotalQty = ret.Quantity; _longAvgPrice = initPrice; _longTotalCost = estimatedCost;
                        _longTrades.Add(new Trade { Time = DateTime.Now, Side = "LONG", Qty = ret.Quantity, Price = initPrice, Cost = estimatedCost, Reason = "INITIAL" });
                    }
                    else
                    {
                        _shortTotalQty = ret.Quantity; _shortAvgPrice = initPrice; _shortTotalCost = estimatedCost;
                        _shortTrades.Add(new Trade { Time = DateTime.Now, Side = "SHORT", Qty = ret.Quantity, Price = initPrice, Cost = estimatedCost, Reason = "INITIAL" });
                    }
                    await Ob.db._dbMaria.HedgeGrid_AddTradeAsync(new HedgeGridTradeRow
                    {
                        position_id = _positionId,
                        trade_time = DateTime.Now,
                        trade_time_utc = DateTime.Now,
                        side = isLong ? "LONG" : "SHORT",
                        qty = ret.Quantity,
                        price = initPrice,
                        cost = ret.Quantity * initPrice / _leverage,
                        reason = "INITIAL"
                    });
                    _lastTpPrice = 0m;
                    _maxBudgetUsed = _longTotalCost + _shortTotalCost;
                    _TotalTrades = 1;
                    _lastAddTime = DateTime.Now;
                    _maxNotionalCap = 40m;

                });
                //_collector.RecordEntry(_symbol, isLong ? "LONG" : "SHORT", snap, btcSnap, decision, _positionId);

                UI($"✅ [GRID] <{_symbol}> ID={_positionId} {(isLong ? "LONG" : "SHORT")} {ret.Quantity} @ {initPrice:F4}");
                UI_ENTER($"✅ [GRID] <{_symbol}> 초기진입 {(isLong ? "LONG" : "SHORT")} @ {initPrice:F4}");
                return true;
            }
            catch (Exception ex) { UI($"❌ [GRID-INIT] <{_symbol}>: {ex.Message}"); return false; }
        }
        private bool IsDangerTime(bool isLong, decimal currentPrice, decimal ema200_5m)
        {
            var now = DateTime.Now;

            // 5분봉 EMA 기준 추세 판단 (묵직한 추세)
            // 가격이 5분봉 EMA200보다 위에 있으면 상승장(Bull), 아래면 하락장(Bear)
            bool isBullTrend = currentPrice > ema200_5m;

            // ━━━ 1. 공통 최우선 방어 (9:00 ~ 9:05) ━━━
            if (now.Hour == 9 && now.Minute <= 5) return true;

            // ━━━ 2. 추세 기반 비대칭 방어 (9:05 ~ 9:15) ━━━
            if (now.Hour == 9 && now.Minute <= 15)
            {
                if (isBullTrend) // 📈 상승 추세
                {
                    // 상승장인데 숏(Short) 진입 시도? 15분까지 엄격히 차단
                    if (!isLong) return true;
                }
                else // 📉 하락 추세
                {
                    // 하락장인데 롱(Long) 진입 시도? 15분까지 엄격히 차단
                    if (isLong) return true;
                }
            }

            // ━━━ 3. 미 증시 개장 위험 시간 ━━━
            if (IsUSMarketOpenDanger()) return true;

            return false;
        }

        private bool IsUSMarketOpenDanger()
        {
            DateTime now = DateTime.Now;
            bool isDST = IsUSDaylightSavingTime(now);
            int startHour = isDST ? 22 : 23;
            int startMinute = 30;

            DateTime openTime = new DateTime(now.Year, now.Month, now.Day, startHour, startMinute, 0);
            DateTime dangerStart = openTime.AddMinutes(-5);
            DateTime dangerEnd = openTime.AddMinutes(10);

            return now >= dangerStart && now <= dangerEnd;
        }

        private bool IsUSDaylightSavingTime(DateTime date)
        {
            DateTime marchBegin = new DateTime(date.Year, 3, 8);
            while (marchBegin.DayOfWeek != DayOfWeek.Sunday) marchBegin = marchBegin.AddDays(1);
            DateTime novEnd = new DateTime(date.Year, 11, 1);
            while (novEnd.DayOfWeek != DayOfWeek.Sunday) novEnd = novEnd.AddDays(1);
            return date >= marchBegin && date < novEnd;
        }
     

        public static async Task<HedgeGridManager> RestoreFromDbAsync(HedgeGridPositionRow row, IExchange ex, RiskConfig rc)
        {
            var trades = await Ob.db._dbMaria.HedgeGrid_SelectTradesAsync(row.id);
            var lastTradeTime = trades.Where(t => t.side == "LONG" || t.side == "SHORT")
                                      .Select(t => t.trade_time).DefaultIfEmpty(row.entry_time).Max();

            var manager = new HedgeGridManager(row.symbol, ex, rc)
            {
                _positionId = row.id,
                _initialBudget = row.initial_budget,
                _entryTime = row.entry_time,
                _lastAddTime = lastTradeTime,
                _maxBudgetUsed = row.max_budget_used ?? 0m,
                _leverage = rc.Leverage,
                _maxRecordedTotalPnl = row.max_recorded_pnl ?? 0m,
                CfgMaxTrade = row.CfgMaxTrade,
                _longTotalQty = row.long_total_qty,
                _longAvgPrice = row.long_avg_price ?? 0,
                _longTotalCost = row.long_total_cost,
                _shortTotalQty = row.short_total_qty,
                _shortAvgPrice = row.short_avg_price ?? 0,
                _shortTotalCost = row.short_total_cost,
                _recoveryBasePrice = row.recovery_base_price ?? 0,
                _recoverySide = row.recovery_side ?? "NONE",
                _TotalTrades = row.total_trades,
                _pendingRecoveryUsd = row.pending_recovery_usd,
                _pendingTargetSide = row.pending_target_side ?? "NONE",
                _lastTpPrice = row.last_tp_price ?? 0m,
                _idChangedTime = DateTime.Now,
                _version = row.version ?? 1,
                _maxNotionalCap = row.max_notional_cap ?? 40m
            };

            if (manager._lastTpPrice <= 0)
            {
                manager._lastTpPrice = row.long_total_qty >= row.short_total_qty
                                       ? (row.long_avg_price ?? 0)
                                       : (row.short_avg_price ?? 0);

                manager.UI($"⚠️ [INIT-TP-PRICE] <{row.symbol}> 기존 익절가 없음 -> 평단({manager._lastTpPrice:F4})으로 기준 설정");
            }

            foreach (var t in trades)
            {
                var tr = new Trade { Price = t.price, Qty = t.qty, Cost = t.cost, Time = t.trade_time_utc, Reason = t.reason, Side = t.side };

                if (t.side == "LONG" || t.side == "CLOSE_LONG" || t.side == "PARTIAL_LONG")
                {
                    manager._longTrades.Add(tr);
                    if (t.side == "PARTIAL_LONG" && t.trade_time > manager._lastRebalanceTime)
                        manager._lastRebalanceTime = t.trade_time;
                }
                else if (t.side == "SHORT" || t.side == "CLOSE_SHORT" || t.side == "PARTIAL_SHORT")
                {
                    manager._shortTrades.Add(tr);
                    if (t.side == "PARTIAL_SHORT" && t.trade_time > manager._lastRebalanceTime)
                        manager._lastRebalanceTime = t.trade_time;
                }
            }
            if (manager._maxRecordedTotalPnl > 0)
                manager.UI($"[RESTORE] {row.symbol} 데이터 복구 완료 (PnL최고점:{manager._maxRecordedTotalPnl:P2})");
            return manager;
        }

        public async Task<bool> OnTick(decimal price)
        {
            if (_positionId == 0) return true;

            // 버전별 로직 분기
            if (_version == 1)
            {
                return await OnTickV1(price); // 기존 OnTick 로직
            }
            else
            {
                return await OnTickV2(price); // 신규 기계적 헷징 로직
            }
        }
        private double _peakPnlPct = 0;
        private async Task<bool> OnTickV1(decimal price)
        {
            try
            {
                var st = Ob.bot.GetOrCreateState(_symbol);
                if (st == null) return true;

                double currentPrice = (double)price;

                string Query = "select * from abuy2way where bCoin = '" + this._symbol + "' and status = 0 ORDER BY bDate DESC, bTime DESC limit 0, 1";
                DataTable dt = await Ob.db.SelectQueryAsync(Query);
                if (dt.Rows.Count == 0) return true;

                var buy = await this.ConvBuy(dt.Rows[0]);
                if (buy == null) return true;

                double Ratio = ((buy.LongBaseMoney - currentPrice) / buy.LongBaseMoney) * 100;
                double pnlLongPct = buy.LongPnl / buy.LongInvestMoney * 100;
                TimeSpan tsLastScal = Ob.app.NowTime() - buy.LongScalDt;

                // A. 트레일링 스톱 익절
                double activationPct = (buy.LongScalingin > 0) ? 0.7 : 1.0;
                double trailingGap = 0.2;

                if (pnlLongPct > _peakPnlPct)
                    _peakPnlPct = pnlLongPct;
                else if (pnlLongPct <= 0)  // 추가
                    _peakPnlPct = 0;

                if (pnlLongPct >= activationPct)
                {

                    if (_peakPnlPct - pnlLongPct >= trailingGap)
                    {
                        if (pnlLongPct < 0)
                        {
                            _peakPnlPct = 0;
                            return true;
                        }

                        Ob.ui.SetText($"<{this._symbol}> [트레일링청산] 고점:{_peakPnlPct:F2}% 현재:{pnlLongPct:F2}% 추매:{buy.LongScalingin}");

                        var (ret1, _) = await ClosePisition(buy, currentPrice, 1, 0, "", buy.LongPnl, 0, true, DateTime.Now);
                        if (ret1 != null)
                        {
                            _peakPnlPct = 0;
                            buy.Status = "1";
                            await this.InUpData(buy, false);
                            return true;
                        }
                    }
                }
                return true;

                // B. 5단계 분할 추매 (ML 기반)
                // 기본 조건: Confidence >= 0.61 + Direction == UP
                if (buy.LongStatus == "0" && Ratio > 5.0 && st.Confidence >= 0.52m && st.Direction == "UP")
                {
                    double additionalQty = 0;
                    string stepLabel = "";

                    // 1차: 기본 신뢰도 + 방향 확인
                    if (buy.LongScalingin == 0 && tsLastScal.TotalHours >= 12 && Ratio > 5.0)
                    {
                        additionalQty = buy.LongQty * 0.05;
                        stepLabel = "1차(24h/3%/Confidence)";
                    }
                    // 2차: ADX 추세 확인 추가
                    else if (buy.LongScalingin == 1 && tsLastScal.TotalHours >= 24 && Ratio > 5.0)
                    {
                        additionalQty = buy.LongQty * 0.05;
                        stepLabel = "2차(48h/4%/ADX)";
                    }
                    // 3차: 신뢰도 상향
                    else if (buy.LongScalingin == 2 && tsLastScal.TotalHours >= 36 && Ratio > 5.0 && st.Confidence >= 0.52m)
                    {
                        additionalQty = buy.LongQty * 0.05;
                        stepLabel = "3차(72h/5%/Confidence62)";
                    }
                    // 4차: 강한 추세 + 고신뢰도
                    else if (buy.LongScalingin == 3 && tsLastScal.TotalHours >= 48 && Ratio > 5.0 && st.Confidence >= 0.52m)
                    {
                        additionalQty = buy.LongQty * 0.05;
                        stepLabel = "4차(96h/6%/Confidence63)";
                    }
                    // 5차: 최고 신뢰도 + 강한 추세 필수
                    else if (buy.LongScalingin == 4 && tsLastScal.TotalHours >= 60 && Ratio > 5.0 && st.Confidence >= 0.52m)
                    {
                        additionalQty = buy.LongQty * 0.05;
                        stepLabel = "5차(120h/7%/Confidence65)";
                    }

                    if (additionalQty > 0)
                    {
                        if (additionalQty * currentPrice < 10.1)
                            additionalQty = 10.1 / currentPrice;

                        var filteredQty = await this.CalculateQuantityAsync(this._symbol, (decimal)additionalQty);
                        if (filteredQty <= 0) return true;

                        Ob.ui.SetText($"<{this._symbol}> [{stepLabel}] 실행 >> 하락률:{Ratio:F1}% Confidence:{st.Confidence:P0} ADX:{st.Adx:F1}");

                        var result = await this.PlaceFuturesOrder_LONG_HEDGE(this._symbol, filteredQty ?? 0, 0, false);
                        if (result != null)
                        {
                            await Task.Delay(1000);
                            var positions = await this.GetPositions();
                            var longUnit = positions.FirstOrDefault(p => p.Symbol.Equals(this._symbol, StringComparison.OrdinalIgnoreCase) && p.Quantity > 0);
                            if (longUnit != null)
                            {
                                buy.LongInvestMoney = (double)Math.Abs(longUnit.Quantity) * (double)longUnit.BreakEvenPrice;
                                buy.LongQty = (double)Math.Abs(longUnit.Quantity);
                                buy.LongScalingin += 1;
                                _peakPnlPct = 0;
                                buy.LongScalDt = Ob.app.NowTime();
                                buy.ScalDt = Ob.app.NowTime();
                                await this.InUpData(buy, false);
                            }
                        }
                    }
                }

                buy.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("OnTickV1", ex);
                return true;
            }
        }
        public async Task<IEnumerable<BinancePositionDetailsUsdt>> GetPositions()
        {
            var result = await Ob.client.UsdFuturesApi.Account.GetPositionInformationAsync(this._symbol);
            if (result.Success)
            {
                return result.Data;
            }
            else
            {
                Console.WriteLine("Error fetching positions: " + result.Error.Message);
                return null;
            }
        }
        public async Task<decimal?> CalculateQuantityAsync(string symbol, decimal rawQty)
        {
            try
            {
                // 1) ExchangeInfo가 로드되어 있는지 확인
                if (Ob.exInfo == null) return null;

                // 2) 심볼 정보 찾기 (대소문자 무시)
                var symbolInfo = Ob.exInfo.Symbols
                    .FirstOrDefault(s => s.Name.Equals(symbol, StringComparison.OrdinalIgnoreCase));
                if (symbolInfo == null)
                {
                    Ob.ui.SetText($"<{this._symbol}>[계산-error: symbolInfo not found]");
                    return null;
                }

                // 3) LotSize 필터 꺼내기
                var lotSizeFilter = symbolInfo.Filters.OfType<BinanceSymbolLotSizeFilter>().FirstOrDefault();
                if (lotSizeFilter == null)
                {
                    Ob.ui.SetText($"<{this._symbol}>[계산-error: LotSize filter missing]");
                    return null;
                }

                decimal stepSize = lotSizeFilter.StepSize;      // 주문 단위 (예: 0.0001)
                decimal minQty = lotSizeFilter.MinQuantity;   // 최소 주문 수량 (예: 0.001)

                // 4) 원시 수량을 stepSize 단위로 내림
                //    rawQty / stepSize 을 내림(floor)한 뒤 다시 stepSize 곱
                decimal factor = Math.Floor(rawQty / stepSize);
                decimal rounded = factor * stepSize;

                // 5) 최소 수량 미달 시 null 반환
                if (rounded < minQty)
                {
                    Ob.ui.SetText($"<{this._symbol}>[계산-최소값 미달: minQty={minQty} / rounded={rounded}]");
                    return null;
                }

                return rounded;
            }
            catch (Exception ex)
            {
                Ob.ui.SetText($"<{this._symbol}>[계산-exception] {ex.Message}");
                return null;
            }
        }
        public async Task<BinanceFuturesOrder> PlaceFuturesOrder_LONG_HEDGE(string symbol, decimal quantity, int leverage, bool reduce)
        {
            try
            {
                //await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, 50m, FuturesMarginChangeDirectionType.Add);await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, 50m, FuturesMarginChangeDirectionType.Add);

                Ob.ui.SetText("<" + this._symbol + ">[" + symbol + " <LONG_HEDGE>] " + quantity.ToString());
                // 주문 요청 보내기 (LIMIT 주문)
                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol,                // 거래 심볼 (예: BTCUSDT)
                    OrderSide.Buy,         // "Buy" 또는 "Sell" (롱/숏 포지션)
                    FuturesOrderType.Market,       // 주문 유형 (LIMIT)
                    quantity,              // 주문 수량
                    null,                 // 주문 가격
                    positionSide: Binance.Net.Enums.PositionSide.Long
                );

                if (result.Success)
                {
                    Ob.ui.SetText("<" + this._symbol + ">[" + symbol + " <LONG_HEDGE-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + this._symbol + ">[" + symbol + " <LONG_HEDGE-오류>] " + result.Error.Message);
                    return null; // 실패한 경우 null 반환
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("PlaceFuturesOrder_SHORT_HEDGE<" + this._symbol + ">", ex);
                return null;
            }

            //ystem.Threading.Thread.Sleep(100);
        }
        public async Task<bool> InUpData(Abuy2Way buy, bool bInsert)
        {
            try
            {
                string query;

                if (bInsert)
                {
                    if (buy.LongScalDt == DateTime.MinValue) buy.LongScalDt = Ob.app.NowTime();
                    if (buy.ShortScalDt == DateTime.MinValue) buy.ShortScalDt = Ob.app.NowTime();

                    query =
                        $"INSERT INTO abuy2way (" +
                        // 1. 기본 키 및 문자열/날짜 컬럼
                        $"BuyId, BDate, BTime, BCoin, " +
                        // 2. 숫자형 컬럼(기본값 CurrentMoney은 자동으로 DEFAULT 0이므로 생략 가능)
                        $"StartMoney, LongStartMoney, ShortStartMoney, " +
                        $"LongInvestMoney, ShortInvestMoney, LongStartInvestMoney, ShortStartInvestMoney, " +
                        $"LongExecMoney, ShortExecMoney, LongBaseMoney, ShortBaseMoney, " +
                        // 3. 상태/숫자/날짜/문자열 컬럼
                        $"LongStatus, LongQty, LongPnl, LongCloseMoney, LongCloseDt, LongCloseParam, " +
                        $"ShortStatus, ShortQty, ShortPnl, ShortCloseMoney, ShortCloseDt, ShortCloseParam, " +
                        $"Status, StartDt, LongStartDt, ShortStartDt, LongScalDt, ShortScalDt, CloseDt, " +
                        // 4. 나머지 문자열/숫자 컬럼
                        $"LongParam, ShortParam, LongScalingin, ShortScalingin, LongScalQty, LongScalMoney, ShortScalQty, ShortScalMoney, LongCount, ShortCount" +
                        $") VALUES (" +
                        // 1. 기본 키 및 문자열/날짜 값
                        $"'{buy.BuyId}', " +
                        $"'{buy.BDate}', " +
                        $"'{buy.BTime}', " +
                        $"'{buy.BCoin}', " +
                        // 2. 숫자형 값
                        $"{buy.StartMoney}, " +
                        $"{buy.LongStartMoney}, " +
                        $"{buy.ShortStartMoney}, " +
                        $"{buy.LongInvestMoney}, " +
                        $"{buy.ShortInvestMoney}, " +
                        $"{buy.LongStartInvestMoney}, " +
                        $"{buy.ShortStartInvestMoney}, " +
                        $"{buy.LongExecMoney}, " +
                        $"{buy.ShortExecMoney}, " +
                        $"{buy.LongBaseMoney}, " +
                        $"{buy.ShortBaseMoney}, " +
                        // 3. 상태/숫자/날짜/문자열 값
                        $"'{buy.LongStatus}', " +
                        $"{buy.LongQty}, " +
                        $"{buy.LongPnl}, " +
                        $"{buy.LongCloseMoney}, " +
                        $"'{buy.LongCloseDt.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                        $"{(string.IsNullOrEmpty(buy.LongCloseParam) ? "''" : $"'{buy.LongCloseParam.Replace("'", "''")}'")}, " +
                        $"'{buy.ShortStatus}', " +
                        $"{buy.ShortQty}, " +
                        $"{buy.ShortPnl}, " +
                        $"{buy.ShortCloseMoney}, " +
                        $"'{buy.ShortCloseDt.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                        $"{(string.IsNullOrEmpty(buy.ShortCloseParam) ? "''" : $"'{buy.ShortCloseParam.Replace("'", "''")}'")}, " +
                        $"'{buy.Status}', " +
                        $"'{buy.StartDt:yyyy-MM-dd HH:mm:ss}', " +
                        $"'{buy.LongStartDt:yyyy-MM-dd HH:mm:ss}', " +
                        $"'{buy.ShortStartDt:yyyy-MM-dd HH:mm:ss}', " +
                        $"'{buy.LongScalDt:yyyy-MM-dd HH:mm:ss}', " +
                        $"'{buy.ShortScalDt:yyyy-MM-dd HH:mm:ss}', " +
                        $"'{buy.CloseDt.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                        // 4. 나머지 문자열/숫자 값
                        $"{(string.IsNullOrEmpty(buy.LongParam) ? "''" : $"'{buy.LongParam.Replace("'", "''")}'")}, " +
                        $"{(string.IsNullOrEmpty(buy.ShortParam) ? "''" : $"'{buy.ShortParam.Replace("'", "''")}'")}, " +
                        $"{buy.LongScalingin}, " +
                        $"{buy.ShortScalingin}, " +
                        $"{buy.LongScalQty}, " +
                        $"{buy.LongScalMoney}, " +
                        $"{buy.ShortScalQty}, " +
                        $"{buy.ShortScalMoney}," +
                        $"{buy.LongCount}," +
                        $"{buy.ShortCount}" +
                        $");";
                }
                else
                {
                    query =
                        $"UPDATE abuy2way SET " +
                        // 문자열/날짜 컬럼
                        // 숫자형 컬럼
                        $"LongInvestMoney = {buy.LongInvestMoney}, " +
                        $"ShortInvestMoney = {buy.ShortInvestMoney}, " +
                        $"LongExecMoney = {buy.LongExecMoney}, " +
                        $"ShortExecMoney = {buy.ShortExecMoney}, " +
                        $"LongBaseMoney = {buy.LongBaseMoney}, " +
                        $"ShortBaseMoney = {buy.ShortBaseMoney}, " +

                        // 상태/숫자/날짜/문자열 컬럼
                        $"LongStatus = '{buy.LongStatus}', " +
                        $"LongQty = {buy.LongQty}, " +
                        $"LongPnl = {buy.LongPnl}, " +
                        $"LongCloseMoney = {buy.LongCloseMoney}, " +
                        $"LongCloseDt = '{buy.LongCloseDt.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                        $"LongCloseParam = {(string.IsNullOrEmpty(buy.LongCloseParam) ? "''" : $"'{buy.LongCloseParam.Replace("'", "''")}'")}, " +

                        $"ShortStatus = '{buy.ShortStatus}', " +
                        $"ShortQty = {buy.ShortQty}, " +
                        $"ShortPnl = {buy.ShortPnl}, " +
                        $"ShortCloseMoney = {buy.ShortCloseMoney}, " +
                        $"ShortCloseDt = '{buy.ShortCloseDt.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                        $"ShortCloseParam = {(string.IsNullOrEmpty(buy.ShortCloseParam) ? "''" : $"'{buy.ShortCloseParam.Replace("'", "''")}'")}, " +

                        // 문자열/날짜 컬럼
                        $"Status = '{buy.Status}', " +
                        $"CloseDt = '{buy.CloseDt.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                        $"LongParam = {(string.IsNullOrEmpty(buy.LongParam) ? "''" : $"'{buy.LongParam.Replace("'", "''")}'")}, " +
                        $"ShortParam = {(string.IsNullOrEmpty(buy.ShortParam) ? "''" : $"'{buy.ShortParam.Replace("'", "''")}'")}, " +

                        // 숫자형 컬럼
                        $"LongScalingin = {buy.LongScalingin}, " +
                        $"ShortScalingin = {buy.ShortScalingin}, " +
                        $"LongScalQty = {buy.LongScalQty}, " +
                        $"LongScalMoney = {buy.LongScalMoney}, " +
                        $"ShortScalQty = {buy.ShortScalQty}, " +
                        $"ShortScalMoney = {buy.ShortScalMoney}, " +
                        $"LongCount = {buy.LongCount}, " +
                        $"ShortCount = {buy.ShortCount}, " +
                        $"ScalDt = '{buy.ScalDt.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                        $"LongScalDt = '{buy.LongScalDt.ToString("yyyy-MM-dd HH:mm:ss")}', " +
                        $"ShortScalDt = '{buy.ShortScalDt.ToString("yyyy-MM-dd HH:mm:ss")}' " +

                        // WHERE 절 (기본 키 BuyId)
                        $"WHERE BuyId = '{buy.BuyId}';";
                }

                var ret = await Ob.db.ExecuteQueryAsync(query, false);
                return ret;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("InUpData", ex);
                return false;
            }
        }
        public async Task<(BinanceFuturesOrder, BinanceFuturesOrder)> ClosePisition(Abuy2Way buy, double currentPrice, int type, int bInsert, string json, double LongPnl, double ShortPnl, bool bRemove, DateTime nDate)
        {
            try
            {
                BinanceFuturesOrder ret1 = null;
                BinanceFuturesOrder ret2 = null;
                if (type == 1)
                {
                    buy.LongCloseMoney = currentPrice;
                    buy.LongCloseDt = nDate;
                    buy.LongStatus = "1";
                    buy.LongPnl = LongPnl;
                    buy.LongParam = json;
                    buy.LongCount += 1;
                    buy.ShortCount = 0;
                    buy.LongScalingin = 0;
                    buy.LongScalQty = 0;
                    buy.LongScalMoney = 0;
                    buy.LongScalDt = DateTime.MinValue;
                    ret1 = await this.PlaceFuturesOrder_CLOSE_HEDGE(_symbol, (decimal)buy.LongQty, false);
                }
                else if (type == 2)
                {
                    buy.ShortCloseMoney = currentPrice;
                    buy.ShortCloseDt = nDate;
                    buy.ShortStatus = "1";
                    buy.ShortPnl = ShortPnl;
                    buy.ShortParam = json;
                    buy.ShortCount += 1;
                    buy.LongCount = 0;
                    buy.ShortScalingin = 0;
                    buy.ShortScalQty = 0;
                    buy.ShortScalMoney = 0;
                    buy.ShortScalDt = DateTime.MinValue;
                    ret2 = await this.PlaceFuturesOrder_CLOSE_HEDGE(_symbol, (decimal)buy.ShortQty, true);
                }
                else if (type == 3)
                {
                    buy.LongCloseMoney = currentPrice;
                    buy.LongCloseDt = nDate;
                    buy.LongStatus = "1";
                    buy.LongPnl = LongPnl;
                    buy.LongParam = json;
                    buy.LongCount += 1;
                    buy.ShortCloseMoney = currentPrice;
                    buy.ShortCloseDt = nDate;
                    buy.ShortStatus = "1";
                    buy.ShortPnl = ShortPnl;
                    buy.ShortParam = json;
                    buy.ShortCount += 1;
                    ret1 = await this.PlaceFuturesOrder_CLOSE_HEDGE(_symbol, (decimal)buy.LongQty, false);
                    ret2 = await this.PlaceFuturesOrder_CLOSE_HEDGE(_symbol, (decimal)buy.ShortQty, true);
                }
                else if (type == 4)
                {
                    buy.LongStatus = "1";
                    buy.LongPnl = 0;
                }
                else if (type == 5)
                {
                    buy.ShortStatus = "1";
                    buy.ShortPnl = 0;
                }
                if (bInsert == 1)
                {
                    buy.Status = "1";
                    buy.CloseDt = nDate;
                }
                return (ret1, ret2);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("ClosePisition", ex);
                return (null, null);
            }
        }
        public async Task<BinanceFuturesOrder> PlaceFuturesOrder_CLOSE_HEDGE(string symbol, decimal closeQuantity, bool closeShortSide)
        {
            if (closeShortSide)
            {
                // 숏 포지션만 청산
                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: OrderSide.Buy,
                    type: FuturesOrderType.Market,
                    quantity: closeQuantity,
                    positionSide: PositionSide.Short
                );
                if (result.Success)
                {
                    Ob.ui.SetText("<" + _symbol + ">[" + symbol + "<LONG_CLOSE-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + _symbol + ">[" + symbol + "<LONG_CLOSE-오류>] " + result.Error.Message);
                    return null; // 실패한 경우 null 반환
                }
            }
            else
            {
                // 롱 포지션만 청산
                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: OrderSide.Sell,
                    type: FuturesOrderType.Market,
                    quantity: closeQuantity,
                    positionSide: PositionSide.Long
                );
                if (result.Success)
                {
                    Ob.ui.SetText("<" + _symbol + ">[" + symbol + "<SHORT_CLOSE-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + _symbol + ">[" + symbol + "<SHORT_CLOSE-오류>] " + result.Error.Message);
                    return null; // 실패한 경우 null 반환
                }
            }
        }
        public async Task<Abuy2Way> ConvBuy(DataRow dr)
        {
            try
            {
                var buy = new Abuy2Way();

                // ────────────────────────────────────────────────
                // 1) 문자열 컬럼 (CamelCase 컬럼명을 그대로 사용)
                // ────────────────────────────────────────────────
                buy.BuyId = dr["BuyId"].ToString();
                buy.BDate = dr["BDate"].ToString();
                buy.BTime = dr["BTime"].ToString();
                buy.BCoin = dr["BCoin"].ToString();
                buy.LongStatus = dr["LongStatus"].ToString();
                buy.ShortStatus = dr["ShortStatus"].ToString();
                buy.Status = dr["Status"].ToString();

                // ────────────────────────────────────────────────
                // 2) 숫자(double) 컬럼
                // ────────────────────────────────────────────────
                // ※ DBNull 체크 후 Convert.ToDouble
                buy.CurrentMoney = dr["CurrentMoney"] != DBNull.Value ? Convert.ToDouble(dr["CurrentMoney"]) : 0;
                buy.StartMoney = dr["StartMoney"] != DBNull.Value ? Convert.ToDouble(dr["StartMoney"]) : 0;
                buy.LongStartMoney = dr["LongStartMoney"] != DBNull.Value ? Convert.ToDouble(dr["LongStartMoney"]) : 0;
                buy.ShortStartMoney = dr["ShortStartMoney"] != DBNull.Value ? Convert.ToDouble(dr["ShortStartMoney"]) : 0;
                buy.LongInvestMoney = dr["LongInvestMoney"] != DBNull.Value ? Convert.ToDouble(dr["LongInvestMoney"]) : 0;
                buy.ShortInvestMoney = dr["ShortInvestMoney"] != DBNull.Value ? Convert.ToDouble(dr["ShortInvestMoney"]) : 0;
                buy.LongStartInvestMoney = dr["LongStartInvestMoney"] != DBNull.Value ? Convert.ToDouble(dr["LongStartInvestMoney"]) : 0;
                buy.ShortStartInvestMoney = dr["ShortStartInvestMoney"] != DBNull.Value ? Convert.ToDouble(dr["ShortStartInvestMoney"]) : 0;
                buy.LongExecMoney = dr["LongExecMoney"] != DBNull.Value ? Convert.ToDouble(dr["LongExecMoney"]) : 0;
                buy.ShortExecMoney = dr["ShortExecMoney"] != DBNull.Value ? Convert.ToDouble(dr["ShortExecMoney"]) : 0;
                buy.LongBaseMoney = dr["LongBaseMoney"] != DBNull.Value ? Convert.ToDouble(dr["LongBaseMoney"]) : 0;
                buy.ShortBaseMoney = dr["ShortBaseMoney"] != DBNull.Value ? Convert.ToDouble(dr["ShortBaseMoney"]) : 0;
                buy.LongQty = dr["LongQty"] != DBNull.Value ? Convert.ToDouble(dr["LongQty"]) : 0;
                buy.ShortQty = dr["ShortQty"] != DBNull.Value ? Convert.ToDouble(dr["ShortQty"]) : 0;
                buy.LongPnl = dr["LongPnl"] != DBNull.Value ? Convert.ToDouble(dr["LongPnl"]) : 0;
                buy.ShortPnl = dr["ShortPnl"] != DBNull.Value ? Convert.ToDouble(dr["ShortPnl"]) : 0;
                buy.LongCloseMoney = dr["LongCloseMoney"] != DBNull.Value ? Convert.ToDouble(dr["LongCloseMoney"]) : 0;
                buy.ShortCloseMoney = dr["ShortCloseMoney"] != DBNull.Value ? Convert.ToDouble(dr["ShortCloseMoney"]) : 0;
                buy.LongScalQty = dr["LongScalQty"] != DBNull.Value ? Convert.ToDouble(dr["LongScalQty"]) : 0;
                buy.LongScalMoney = dr["LongScalMoney"] != DBNull.Value ? Convert.ToDouble(dr["LongScalMoney"]) : 0;
                buy.ShortScalQty = dr["ShortScalQty"] != DBNull.Value ? Convert.ToDouble(dr["ShortScalQty"]) : 0;
                buy.ShortScalMoney = dr["ShortScalMoney"] != DBNull.Value ? Convert.ToDouble(dr["ShortScalMoney"]) : 0;
                buy.LongCount = dr["LongCount"] != DBNull.Value ? Convert.ToInt32(dr["LongCount"]) : 0;
                buy.ShortCount = dr["ShortCount"] != DBNull.Value ? Convert.ToInt32(dr["ShortCount"]) : 0;

                // ────────────────────────────────────────────────
                // 3) 날짜/시간(datetime) 컬럼 (NULL 허용 컬럼은 DBNull 체크)
                // ────────────────────────────────────────────────
                if (dr["LongCloseDt"] != DBNull.Value)
                    buy.LongCloseDt = DateTime.Parse(dr["LongCloseDt"].ToString());
                if (dr["ShortCloseDt"] != DBNull.Value)
                    buy.ShortCloseDt = DateTime.Parse(dr["ShortCloseDt"].ToString());
                if (dr["StartDt"] != DBNull.Value)
                    buy.StartDt = DateTime.Parse(dr["StartDt"].ToString());
                if (dr["LongStartDt"] != DBNull.Value)
                    buy.LongStartDt = DateTime.Parse(dr["LongStartDt"].ToString());
                if (dr["ShortStartDt"] != DBNull.Value)
                    buy.ShortStartDt = DateTime.Parse(dr["ShortStartDt"].ToString());
                if (dr["CloseDt"] != DBNull.Value)
                    buy.CloseDt = DateTime.Parse(dr["CloseDt"].ToString());
                if (dr["RegisterDt"] != DBNull.Value)
                    buy.RegisterDt = DateTime.Parse(dr["RegisterDt"].ToString());
                if (dr["ScalDt"] != DBNull.Value)
                    buy.ScalDt = DateTime.Parse(dr["ScalDt"].ToString());

                if (dr["LongScalDt"] != DBNull.Value)
                    buy.LongScalDt = DateTime.Parse(dr["LongScalDt"].ToString());
                if (dr["ShortScalDt"] != DBNull.Value)
                    buy.ShortScalDt = DateTime.Parse(dr["ShortScalDt"].ToString());


                // ────────────────────────────────────────────────
                // 4) TEXT (string) 컬럼
                // ────────────────────────────────────────────────
                buy.LongCloseParam = dr["LongCloseParam"] != DBNull.Value ? dr["LongCloseParam"].ToString() : null;
                buy.ShortCloseParam = dr["ShortCloseParam"] != DBNull.Value ? dr["ShortCloseParam"].ToString() : null;
                buy.LongParam = dr["LongParam"] != DBNull.Value ? dr["LongParam"].ToString() : null;
                buy.ShortParam = dr["ShortParam"] != DBNull.Value ? dr["ShortParam"].ToString() : null;

                // ────────────────────────────────────────────────
                // 5) TINYINT(1) 컬럼 (bool로 변환 가능)
                // ────────────────────────────────────────────────
                buy.LongScalingin = dr["LongScalingin"] != DBNull.Value ? Convert.ToInt32(dr["LongScalingin"]) : 0;
                buy.ShortScalingin = dr["ShortScalingin"] != DBNull.Value ? Convert.ToInt32(dr["ShortScalingin"]) : 0;

                return buy;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("ConvBuy", ex);
                return null;
            }
        }
        private async Task<bool> OnTickV2(decimal price)
        {
            TimeSpan heldTime = DateTime.Now - _entryTime;
            var st = Ob.bot.GetOrCreateState(_symbol);
            if (st == null)
            {
                await CloseAll(price, $"V2-EXIT({"정보없음"}, Peak:{_maxRecordedTotalPnl:P2})");
                await UpdateDB(price);
                return false;
            }

            if ((DateTime.Now - _lastConfidenceLog).TotalMinutes >= 60)
            {
                if (st.Confidence > _maxConfidence24h) _maxConfidence24h = st.Confidence;

                int currentScore = (st.Confidence >= 0.62m ? 1 : 0)
                                 + (st.Adx > 15m ? 1 : 0)
                                 + (((st.Direction == "UP" && price > st.Open1m) || (st.Direction == "DOWN" && price < st.Open1m)) ? 1 : 0);

                UI($"📊 [V2-MON] <{_symbol}> {heldTime.Days}d | AI:{st.Confidence:P0} (Max:{_maxConfidence24h:P0}) | Score:{currentScore}/3 | ADX:{st.Adx:F1}");
                _lastConfidenceLog = DateTime.Now;

                if (heldTime.TotalHours % 24 < 1) _maxConfidence24h = 0;
            }

            decimal longVal = _longTotalQty * price;
            decimal shortVal = _shortTotalQty * price;
            decimal totalNotional = longVal + shortVal;
            decimal totalCost = _longTotalCost + _shortTotalCost;
            decimal totalPnlUsd = ((price - _longAvgPrice) * _longTotalQty) + ((_shortAvgPrice - price) * _shortTotalQty);
            decimal currentPnlPct = totalNotional > 0 ? totalPnlUsd / totalNotional : 0;

            double holdDays = heldTime.TotalDays;
            decimal finalActivationPct = 0.015m;
            decimal finalCallbackPct = 0.003m;
            string modeTag = "정상익절";
            bool deadlockBreakMode = false;

            if (holdDays >= 14)
            {
                finalActivationPct = 0.005m; finalCallbackPct = 0.001m;
                modeTag = "14일-본절탈출"; deadlockBreakMode = true;
            }
            else if (holdDays >= 10)
            {
                finalActivationPct = 0.010m; finalCallbackPct = 0.002m;
                modeTag = "10일-하이브리드돌파"; deadlockBreakMode = true;
            }
            else if (holdDays >= 7)
            {
                finalActivationPct = 0.010m; finalCallbackPct = 0.002m;
                modeTag = "7일-타겟하향";
            }
            else if (holdDays >= 5)
            {
                finalActivationPct = 0.012m; finalCallbackPct = 0.003m;
                modeTag = "5일-타겟하향";
            }

            if (await TryAutoRebalanceAsync(price, st)) return true;

            if (currentPnlPct > _maxRecordedTotalPnl)
                _maxRecordedTotalPnl = currentPnlPct;
            else if (currentPnlPct <= 0)
                _maxRecordedTotalPnl = 0m;

            if (_maxRecordedTotalPnl >= finalActivationPct)
            {
                if (currentPnlPct <= _maxRecordedTotalPnl - finalCallbackPct)
                {
                    await CloseAll(price, $"V2-EXIT({modeTag}, Peak:{_maxRecordedTotalPnl:P2})");
                    await UpdateDB(price);
                    return false;
                }
            }
            if ((DateTime.Now - _lastDbUpdate).TotalSeconds >= 60)
            {
                await UpdateDB(price);
                await SyncConfigFromDbAsync();
                _lastDbUpdate = DateTime.Now;
            }

            return true;

            if (longVal > 10 && shortVal < 5 && (price - _longAvgPrice) / _longAvgPrice <= -0.035m)
            {
                decimal hedgeQty = _ex.GetMinGuaranteedQtyV2(_symbol, (_longTotalQty * _longAvgPrice) * 1.5m, price);
                await AddShort(price, "V2-HEDGE-3.5%", hedgeQty);
                return true;
            }
            if (shortVal > 10 && longVal < 5 && (_shortAvgPrice - price) / _shortAvgPrice <= -0.035m)
            {
                decimal hedgeQty = _ex.GetMinGuaranteedQtyV2(_symbol, (_shortTotalQty * _shortAvgPrice) * 1.5m, price);
                await AddLong(price, "V2-HEDGE-3.5%", hedgeQty);
                return true;
            }

            bool skipScaling = false;
            string skipReason = "";
            decimal maxNotionalCap = _maxNotionalCap;
            bool isHedged = longVal >= 10m && shortVal >= 10m;

            // ★ 단방향 쏠림 방지
            const decimal MAX_SIDE_RATIO = 2.5m;
            bool longDominated = shortVal > 1m && longVal / shortVal > MAX_SIDE_RATIO;
            bool shortDominated = longVal > 1m && shortVal / longVal > MAX_SIDE_RATIO;

            if (isHedged)
            {
                if (!skipScaling)
                {
                    decimal range5m = st.RecentLow5m > 0
                        ? (st.RecentHigh5m - st.RecentLow5m) / st.RecentLow5m
                        : 0;

                    if (range5m > 0.08m) { skipScaling = true; }
                    else if (range5m > 0.05m && (DateTime.Now - _lastAddTime).TotalMinutes < 15) { skipScaling = true; }
                }

                if (!skipScaling && totalCost < maxNotionalCap)
                {
                    bool canAddLong = true;
                    bool canAddShort = true;

                    decimal lLoss = (price - _longAvgPrice) / _longAvgPrice;
                    decimal sLoss = (_shortAvgPrice - price) / _shortAvgPrice;
                    if (lLoss < 0 && sLoss < 0)
                    {
                        if (Math.Abs(sLoss) > Math.Abs(lLoss) * 1.5m) canAddLong = false;
                        else if (Math.Abs(lLoss) > Math.Abs(sLoss) * 1.5m) canAddShort = false;
                    }

                    if ((st.Direction == "UP" && !canAddLong) || (st.Direction == "DOWN" && !canAddShort))
                    {
                        skipScaling = true; skipReason = $"손실비대칭(L:{lLoss:P1}/S:{sLoss:P1})";
                    }
                }
            }

            decimal longPnlPct = _longAvgPrice > 0 ? (price - _longAvgPrice) / _longAvgPrice : 0;
            decimal shortPnlPct = _shortAvgPrice > 0 ? (_shortAvgPrice - price) / _shortAvgPrice : 0;
            decimal pnlGap = longPnlPct - shortPnlPct;

            if (isHedged && holdDays >= 10 && !skipScaling && totalCost < maxNotionalCap)
            {
                decimal dL = Math.Abs(price - _longAvgPrice);
                decimal dS = Math.Abs(price - _shortAvgPrice);

                if (dS > dL * 1.5m && st.Direction == "DOWN" && price < _longAvgPrice * 0.95m)
                {
                    decimal safeQty = GetSmartQty(30m, price, st.Direction == "UP", totalCost, maxNotionalCap);
                    await AddShort(price, "V2-DELTA-FOCUS-S", safeQty); return true;
                }
                else if (dL > dS * 1.5m && st.Direction == "UP" && price > _shortAvgPrice * 1.05m)
                {
                    decimal safeQty = GetSmartQty(30m, price, st.Direction == "UP", totalCost, maxNotionalCap);
                    await AddLong(price, "V2-DELTA-FOCUS-L", safeQty); return true;
                }
            }

            if (isHedged && !skipScaling && (DateTime.Now - _lastAddTime).TotalMinutes >= 5 && st.Confidence >= 0.61m)
            {
                if (totalCost < maxNotionalCap)
                {
                    decimal capUsageRatio = totalCost / maxNotionalCap;
                    bool inDeadlock = false;

                    if (capUsageRatio >= 0.70m)
                    {
                        decimal ratio = shortVal > 0 ? longVal / shortVal : 999m;
                        inDeadlock = ratio >= 0.85m && ratio <= 1.15m;
                    }

                    bool canBreakDeadlock = false;
                    if (deadlockBreakMode && inDeadlock && heldTime.TotalHours >= 24)
                    {
                        int score = (st.Adx > 15m ? 1 : 0) +
                                    (((st.Direction == "UP" && price > st.Open1m) || (st.Direction == "DOWN" && price < st.Open1m)) ? 1 : 0);
                        if (score >= (holdDays >= 14 ? 1 : 2)) canBreakDeadlock = true;
                    }

                    if (inDeadlock && !canBreakDeadlock)
                    {
                        bool strongSignal = st.Adx > 25m
                                         && ((st.Direction == "UP" && price > st.Open1m) ||
                                             (st.Direction == "DOWN" && price < st.Open1m));
                        if (strongSignal)
                        {
                            decimal targetSideVal = st.Direction == "UP" ? longVal : shortVal;
                            decimal oppSideVal = st.Direction == "UP" ? shortVal : longVal;
                            decimal needUsd = (oppSideVal * 1.2m) - targetSideVal;
                            if (needUsd > 0)
                            {
                                decimal breakUsd = Math.Clamp(needUsd, 10m, 30m);
                                decimal breakQty = GetSmartQty(breakUsd, price, st.Direction == "UP", totalCost, maxNotionalCap);
                                if (st.Direction == "UP") { await AddLong(price, "V2-DEADLOCK-BREAK-L", breakQty); }
                                else { await AddShort(price, "V2-DEADLOCK-BREAK-S", breakQty); }
                                return true;
                            }
                        }
                    }

                    if (!inDeadlock || canBreakDeadlock)
                    {
                        bool trailingActive = _maxRecordedTotalPnl >= finalActivationPct;

                        // ★ !longDominated 추가
                        if (longPnlPct > 0.01m && st.Direction == "UP" && longVal < shortVal * 1.5m && !trailingActive && !longDominated)
                        {
                            decimal fireUsd = capUsageRatio switch { <= 0.25m => 10m, <= 0.50m => 15m, <= 0.75m => 20m, _ => 25m };
                            decimal fireQty = GetSmartQty(fireUsd, price, st.Direction == "UP", totalCost, maxNotionalCap);
                            await AddLong(price, "V2-FIRE-L", fireQty);
                            return true;
                        }
                        // ★ !shortDominated 추가
                        if (shortPnlPct > 0.01m && st.Direction == "DOWN" && shortVal < longVal * 1.5m && !trailingActive && !shortDominated)
                        {
                            decimal fireUsd = capUsageRatio switch { <= 0.25m => 10m, <= 0.50m => 15m, <= 0.75m => 20m, _ => 25m };
                            decimal fireQty = GetSmartQty(fireUsd, price, st.Direction == "UP", totalCost, maxNotionalCap);
                            await AddShort(price, "V2-FIRE-S", fireQty);
                            return true;
                        }

                        decimal addUsd = canBreakDeadlock ? 15m :
                                         capUsageRatio switch { <= 0.25m => 10m, <= 0.50m => 15m, <= 0.75m => 20m, _ => 25m };

                        string reasonTag = canBreakDeadlock ? "V2-HYBRID-BREAK" : "V2-MICRO";
                        decimal safeQty = _ex.GetMinGuaranteedQtyV2(_symbol, addUsd, price);

                        // ★ 쏠림 방향 차단
                        if (st.Direction == "UP" && !longDominated) { await AddLong(price, reasonTag + "-L", safeQty); }
                        else if (st.Direction == "DOWN" && !shortDominated) { await AddShort(price, reasonTag + "-S", safeQty); }
                        // 쏠린 경우 아무것도 안 함
                        return true;
                    }
                }
            }

            if ((DateTime.Now - _lastDbUpdate).TotalSeconds >= 60)
            {
                await UpdateDB(price);
                await SyncConfigFromDbAsync();
                _lastDbUpdate = DateTime.Now;
            }

            return true;
        }
        // ━━━━━ [자동 리밸런싱] 노셔널 초과 시 양쪽 동일 수량 부분 청산 ━━━━━
        private DateTime _lastRebalanceTime = DateTime.MinValue;

        private async Task<bool> TryAutoRebalanceAsync(decimal price, RtSymbolState snap)
        {
            if (_longTotalQty <= 0 || _shortTotalQty <= 0) return false;

            var st = Ob.bot.GetOrCreateState(_symbol);
            if (st != null)
            {
                decimal range5m = snap.RecentLow5m > 0
                    ? (snap.RecentHigh5m - snap.RecentLow5m) / snap.RecentLow5m
                    : 0;
                if (range5m > 0.03m)
                {
                    //UI($"⏸️ [REBALANCE-SKIP] <{_symbol}> 변동성 과열({range5m:P1}) - 리밸런싱 연기");
                    return false;
                }
            }

            decimal totalNotional = (_longTotalQty + _shortTotalQty) * price;
            decimal notionalCap = _maxNotionalCap * _leverage;

            bool timeElapsed = (DateTime.Now - _lastAddTime).TotalDays >= 5;
            bool notionalHigh = totalNotional >= notionalCap * 0.7m;
            
            bool rebalanceCooldown = (DateTime.Now - _lastRebalanceTime).TotalDays >= 1;

            if (!notionalHigh || !rebalanceCooldown) return false;

            decimal longPnlPerUnit = price - _longAvgPrice;
            decimal shortPnlPerUnit = _shortAvgPrice - price;

            bool longIsBigger = _longTotalQty >= _shortTotalQty;
            decimal bigQty = longIsBigger ? _longTotalQty : _shortTotalQty;
            decimal smallQty = longIsBigger ? _shortTotalQty : _longTotalQty;
            decimal bigPnlPU = longIsBigger ? longPnlPerUnit : shortPnlPerUnit;
            decimal smallPnlPU = longIsBigger ? shortPnlPerUnit : longPnlPerUnit;

            if (Math.Abs(smallPnlPU) < 0.000001m) return false;

            decimal qBig = _ex.RoundQty(_symbol, bigQty * 0.25m);
            if (qBig <= 0) return false;

            // ✅ 청산 실현 PnL 합 = 0이 되도록 수량 계산
            decimal qSmall = _ex.RoundQty(_symbol, -qBig * bigPnlPU / smallPnlPU);

            if (qSmall <= 0 || qSmall > smallQty) return false;

            // 예상 실현 PnL 로그
            decimal expectedPnl = qBig * bigPnlPU + qSmall * smallPnlPU;

            decimal qLong = longIsBigger ? qBig : qSmall;
            decimal qShort = !longIsBigger ? qBig : qSmall;

            UI($"⚖️ <{_symbol}>[AUTO-REBALANCE] 노셔널 {totalNotional:F0} / 한도 {notionalCap:F0} → 롱 {qLong}개 / 숏 {qShort}개 청산 (예상실현PnL:{expectedPnl:F2})");

            decimal totalQtyBefore = _longTotalQty + _shortTotalQty;

            var longOk = await ClosePartialLongAsync(price, qLong, "AUTO-REBALANCE");
            var shortOk = await ClosePartialShortAsync(price, qShort, "AUTO-REBALANCE");

            if (longOk && !shortOk)
                UI($"⚠️ [REBALANCE-WARN] <{_symbol}> 롱 청산 성공, 숏 청산 실패 - 불균형 주의");
            else if (!longOk && shortOk)
                UI($"⚠️ [REBALANCE-WARN] <{_symbol}> 숏 청산 성공, 롱 청산 실패 - 불균형 주의");

            if (longOk || shortOk)
            {
                _lastRebalanceTime = DateTime.Now;

                if (_version == 3)
                {
                    decimal closedQty = qLong + qShort;
                    decimal closeRatio = totalQtyBefore > 0 ? closedQty / totalQtyBefore : 0;

                    int tradesToReduce = (int)Math.Floor(_TotalTrades * closeRatio);
                    tradesToReduce = Math.Max(tradesToReduce, 1);

                    int beforeTrades = _TotalTrades;

                    await WithLock(async () =>
                    {
                        _TotalTrades = Math.Max(0, _TotalTrades - tradesToReduce);
                    });

                    UI($"📉 [REBALANCE-TRADES] <{_symbol}> TotalTrades {beforeTrades} → {_TotalTrades} (비율:{closeRatio:P0})");

                    await UpdateDB(price);
                    await SyncConfigFromDbAsync();
                    _lastDbUpdate = DateTime.Now;
                }
            }

            return longOk || shortOk;
        }
        private async Task<bool> ClosePartialLongAsync(decimal price, decimal qty, string reason)
        {
            try
            {
                var ret = await _ex.PlaceMarketOrderAsync(_symbol, Side.Sell, qty, true);
                if (ret != null)
                {
                    _lastOrderTime = DateTime.Now;
                    await WithLock(async () =>
                    {
                        _longTotalQty -= ret.Quantity;
                        if (_longTotalQty < 0) _longTotalQty = 0;
                        _longTotalCost = _longTotalQty * _longAvgPrice / _leverage;
                        _longTrades.Add(new Trade { Time = DateTime.Now, Side = "PARTIAL_LONG", Qty = ret.Quantity, Price = price, Reason = reason });
                    });
                    await Ob.db._dbMaria.HedgeGrid_AddTradeAsync(new HedgeGridTradeRow
                    {
                        position_id = _positionId,
                        trade_time = DateTime.Now,
                        trade_time_utc = DateTime.Now,
                        side = "PARTIAL_LONG",
                        qty = ret.Quantity,
                        price = price,
                        cost = ret.Quantity * price / _leverage,
                        reason = reason
                    });
                    UI($"✅ [PARTIAL-CLOSE-L] <{_symbol}> -{ret.Quantity} @ {price:F4} ({reason})");
                    await UpdateDB(price);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR($"ClosePartialLongAsync<{_symbol}>", ex);
                return false;
            }
        }

        private async Task<bool> ClosePartialShortAsync(decimal price, decimal qty, string reason)
        {
            try
            {
                var ret = await _ex.PlaceMarketOrderAsync(_symbol, Side.Buy, qty, true);
                if (ret != null)
                {
                    _lastOrderTime = DateTime.Now;
                    await WithLock(async () =>
                    {
                        _shortTotalQty -= ret.Quantity;
                        if (_shortTotalQty < 0) _shortTotalQty = 0;
                        _shortTotalCost = _shortTotalQty * _shortAvgPrice / _leverage;
                        _shortTrades.Add(new Trade { Time = DateTime.Now, Side = "PARTIAL_SHORT", Qty = ret.Quantity, Price = price, Reason = reason });
                    });
                    await Ob.db._dbMaria.HedgeGrid_AddTradeAsync(new HedgeGridTradeRow
                    {
                        position_id = _positionId,
                        trade_time = DateTime.Now,
                        trade_time_utc = DateTime.Now,
                        side = "PARTIAL_SHORT",
                        qty = ret.Quantity,
                        price = price,
                        cost = ret.Quantity * price / _leverage,
                        reason = reason
                    });
                    UI($"✅ [PARTIAL-CLOSE-S] <{_symbol}> -{ret.Quantity} @ {price:F4} ({reason})");
                    await UpdateDB(price);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR($"ClosePartialShortAsync<{_symbol}>", ex);
                return false;
            }
        }
        decimal GetBoostedQty(decimal baseUsd, decimal pnlGap, bool isLong, decimal totalCost, decimal maxCap, decimal price)
        {
            bool aligned = isLong ? pnlGap > 0.03m : pnlGap < -0.03m;
            if (aligned)
            {
                decimal mul = Math.Abs(pnlGap) > 0.07m ? 1.5m : 1.25m;
                decimal boosted = baseUsd * mul;
                if (totalCost + boosted <= maxCap) baseUsd = boosted;
            }
            return _ex.GetMinGuaranteedQtyV2(_symbol, baseUsd, price);
        }
        private decimal GetSmartQty(decimal baseUsd, decimal price, bool isLong, decimal totalCost, decimal maxCap)
        {
            if (_longAvgPrice <= 0 || _shortAvgPrice <= 0)
                return _ex.GetMinGuaranteedQtyV2(_symbol, baseUsd, price);

            decimal longVal = _longTotalQty * price;
            decimal shortVal = _shortTotalQty * price;

            // 1. 달러 PnL 우위
            decimal longPnlUsd = (price - _longAvgPrice) * _longTotalQty;
            decimal shortPnlUsd = (_shortAvgPrice - price) * _shortTotalQty;
            decimal pnlEdge = longPnlUsd - shortPnlUsd; // 양수=롱우위

            // 2. 평단 회수 효율 (추매 1달러당 평단 개선량)
            decimal longBreakEven = Math.Max(_longAvgPrice - price, 0);
            decimal shortBreakEven = Math.Max(price - _shortAvgPrice, 0);
            decimal longImprove = longVal > 0 ? longBreakEven / longVal : 0;
            decimal shortImprove = shortVal > 0 ? shortBreakEven / shortVal : 0;

            // 3. 부스트 판단
            bool aligned = isLong
                ? (pnlEdge > 0 && longImprove > shortImprove)   // 롱이 이기고 + 롱 추매가 효율적
                : (pnlEdge < 0 && shortImprove > longImprove);  // 숏이 이기고 + 숏 추매가 효율적

            if (aligned)
            {
                decimal improveDiff = Math.Abs(longImprove - shortImprove);
                decimal mul = improveDiff > 0.005m ? 1.5m : 1.25m;
                decimal boosted = baseUsd * mul;
                if (totalCost + boosted <= maxCap) baseUsd = boosted;
            }

            return _ex.GetMinGuaranteedQtyV2(_symbol, baseUsd, price);
        }
        // [신규] 비대칭 수량 계산 함수
        private decimal GetAsymmetricQty(bool isLong, decimal mult, int trades)
        {
            decimal currentPrice = _ex.GetLastPrice(_symbol);
            if (currentPrice <= 0) return 0;

            // ✅ 핵심: 증거금(5$) × 레버리지(10) = 50$어치 수량을 기본으로 계산
            decimal baseNotional = _initialBudget * _leverage;
            decimal weight = 1.0m + (trades * 0.25m);
            decimal targetNotional = baseNotional * weight;

            // 바이낸스 최소 5$ 제한 방어 (여유 있게 5.5$)
            if (targetNotional < 5.5m) targetNotional = 5.5m;

            decimal targetQty = targetNotional / currentPrice;

            // 비대칭 델타 확보 로직 (기존 유지)
            decimal oppQty = isLong ? _shortTotalQty : _longTotalQty;
            decimal myQty = isLong ? _longTotalQty : _shortTotalQty;
            if (trades >= 8 && oppQty > targetQty) targetQty = (oppQty * 1.1m) - myQty;

            return _ex.GetMinGuaranteedQtyV2(_symbol, targetNotional, _ex.GetLastPrice(_symbol));
        }
        private decimal AdjustQtyForEscape(decimal currentQty, decimal price, bool isLong)
        {
            // 1. 목표 탈출 지점 설정 (현재가 대비 2% 반등 시 본절 탈출 목표)
            decimal targetDistancePct = 0.02m;
            decimal targetPrice = isLong ? price * (1 + targetDistancePct) : price * (1 - targetDistancePct);

            // 2. 현재 상태 추출
            decimal curQty = isLong ? _longTotalQty : _shortTotalQty;
            decimal curAvg = isLong ? _longAvgPrice : _shortAvgPrice;

            // 3. 탈출에 필요한 수량 역산 (수학적 델타)
            decimal priceDiff = Math.Max(price * 0.0001m, Math.Abs(price - targetPrice));
            // 함수 내부의 안전 장치(4번 항목 근처)에 아래 내용을 추가/수정하세요.
            decimal requiredQty = curQty * Math.Abs(curAvg - targetPrice) / priceDiff;

            // ✅ 추가: 구출 수량이 최소 5.5$는 되도록 보정
            decimal minRequiredQty = 5.5m / price;
            if (requiredQty < minRequiredQty) requiredQty = minRequiredQty;

            // 기존 maxSafetyQty 계산 (레버리지 적용된 25배 캡)
            decimal maxSafetyQty = (_initialBudget * _leverage * 25.0m) / price;
            decimal finalAdjustedQty = Math.Min(requiredQty, maxSafetyQty);

            // 5. 최종 수량 결정 및 소수점 절삭
            return _ex.RoundQty(_symbol, Math.Max(currentQty, finalAdjustedQty));
        }

        // [신규] 데드락(탈출 불가) 시뮬레이션 함수
        private bool IsDeadlockTrap(decimal addQty, decimal currentPrice, bool isLong)
        {
            decimal nLQty = _longTotalQty + (isLong ? addQty : 0);
            decimal nSQty = _shortTotalQty + (isLong ? 0 : addQty);

            decimal diff = nLQty - nSQty;
            if (Math.Abs(diff) < 0.00000001m) return true; // 🚩 분모가 0이 되는 상황 방어

            // 1. 델타(수량 차이) 체크: 양방향 수량이 너무 비슷하면(20% 미만 차이) 탈출 불가능
            decimal totalQty = nLQty + nSQty;
            if (Math.Abs(nLQty - nSQty) < totalQty * 0.2m) return true;

            // 2. 익절가(Break-even) 시뮬레이션
            decimal nLAvg = isLong ? ((_longTotalQty * _longAvgPrice) + (addQty * currentPrice)) / nLQty : _longAvgPrice;
            decimal nSAvg = !isLong ? ((_shortTotalQty * _shortAvgPrice) + (addQty * currentPrice)) / nSQty : _shortAvgPrice;

            // 델타를 이용한 산술적 탈출 가격 도출
            // (target - nLAvg) * nLQty + (nSAvg - target) * nSQty = 0
            decimal exitPrice = (nLAvg * nLQty - nSAvg * nSQty) / (nLQty - nSQty);

            // 3. 현실성 판단: 현재가 대비 탈출가가 5% 이상 멀면 "데드락"으로 간주
            decimal requiredMove = Math.Abs(exitPrice - currentPrice) / currentPrice;

            return requiredMove > 0.05m; // 5% 초과 무빙 필요 시 진입 거절
        }

        public async Task UpdateFromExchangePosition(BinancePositionDetailsUsdt longPos, BinancePositionDetailsUsdt shortPos)
        {
            // 1. 주문 직후 5초 동안은 동기화를 건너뜁니다.
            if ((DateTime.Now - _lastOrderTime).TotalSeconds < 5) return;

            bool updated = false;
            bool syncRejected = false;

            // 2. WithLock의 반환값을 bool? 타입으로 받아서 락 성공 여부를 확인합니다.
            // 락 실패 시 null이나 false가 반환되도록 구조화합니다.
            var lockResult = await WithLock<bool?>(async () =>
            {
                bool isInitialSync = (_lastSyncTime == DateTime.MinValue);
                bool innerUpdated = false;

                // --- LONG 포지션 검증 및 동기화 ---
                if (longPos != null && longPos.Quantity != 0)
                {
                    var exQty = Math.Abs(longPos.Quantity);
                    decimal diffPct = _longTotalQty > 0 ? Math.Abs(exQty - _longTotalQty) / _longTotalQty : 0;

                    // [수정] 60회 연속 불일치 시에만 API 신뢰
                    if (!isInitialSync && _longTotalQty > 100 && diffPct > 0.50m)
                    {
                        _longSyncRejectCount++;

                        if (_longSyncRejectCount < 60)
                        {
                            if (_longSyncRejectCount % 10 == 0) // 로그 폭주 방지를 위해 10회마다 출력
                                UI($"⚠️ [SYNC-REJECT-L] <{_symbol}> ({_longSyncRejectCount}/60) 롱 수량 급변 무시 중. 메모리:{_longTotalQty} -> API:{exQty}");

                            syncRejected = true;
                            return false;
                        }

                        UI($"🔄 [SYNC-FORCE-L] <{_symbol}> 60회 연속 불일치 발생. API 수량({exQty})으로 강제 동기화합니다.");
                    }

                    if (Math.Abs(exQty - _longTotalQty) > 0.001m || _longAvgPrice != longPos.EntryPrice)
                    {
                        if (!isInitialSync) UI($"⚠️ [SYNC-LONG] <{_symbol}> 수량 보정 ({_longTotalQty}->{exQty})");
                        _longTotalQty = exQty;
                        _longAvgPrice = longPos.EntryPrice;
                        _longTotalCost = _longTotalQty * _longAvgPrice / _leverage;
                        innerUpdated = true;
                        _longSyncRejectCount = 0; // 성공 시 카운트 초기화
                    }
                }
                else if (_longTotalQty != 0)
                {
                    _longTotalQty = 0; _longAvgPrice = 0; _longTotalCost = 0;
                    innerUpdated = true;
                    _longSyncRejectCount = 0;
                }

                // --- SHORT 포지션 검증 및 동기화 ---
                if (shortPos != null && shortPos.Quantity != 0)
                {
                    var exQty = Math.Abs(shortPos.Quantity);
                    decimal diffPct = _shortTotalQty > 0 ? Math.Abs(exQty - _shortTotalQty) / _shortTotalQty : 0;

                    if (!isInitialSync && _shortTotalQty > 100 && diffPct > 0.50m)
                    {
                        _shortSyncRejectCount++;

                        if (_shortSyncRejectCount < 60)
                        {
                            if (_shortSyncRejectCount % 10 == 0)
                                UI($"⚠️ [SYNC-REJECT-S] <{_symbol}> ({_shortSyncRejectCount}/60) 숏 수량 급변 무시 중. 메모리:{_shortTotalQty} -> API:{exQty}");

                            syncRejected = true;
                            return false;
                        }

                        UI($"🔄 [SYNC-FORCE-S] <{_symbol}> 60회 연속 불일치 발생. API 수량({exQty})으로 강제 동기화합니다.");
                    }

                    if (Math.Abs(exQty - _shortTotalQty) > 0.001m || _shortAvgPrice != shortPos.EntryPrice)
                    {
                        if (!isInitialSync) UI($"⚠️ [SYNC-SHORT] <{_symbol}> 수량 보정 ({_shortTotalQty}->{exQty})");
                        _shortTotalQty = exQty;
                        _shortAvgPrice = shortPos.EntryPrice;
                        _shortTotalCost = _shortTotalQty * _shortAvgPrice / _leverage;
                        innerUpdated = true;
                        _shortSyncRejectCount = 0; // 성공 시 카운트 초기화
                    }
                }
                else if (_shortTotalQty != 0)
                {
                    _shortTotalQty = 0; _shortAvgPrice = 0; _shortTotalCost = 0;
                    innerUpdated = true;
                    _shortSyncRejectCount = 0;
                }

                _lastSyncTime = DateTime.Now;
                updated = innerUpdated;
                return true;
            });

            // 3. [추가] 락 획득 실패 시 로그 처리
            if (lockResult == null) // WithLock에서 세마포어 대기 타임아웃 발생 시
            {
                UI($"🔥 [SYNC-LOCK-FAIL] <{_symbol}> 동기화 락 획득 실패 (엔진 병목 의심)");
                return;
            }

            // 4. [부하 방지] 락 밖에서 DB 업데이트
            if (!syncRejected && updated)
            {
                decimal currentPrice = _ex.GetLastPrice(_symbol);
                if (currentPrice > 0) await UpdateDB(currentPrice);
            }

            // 5. [실패 감시] 락은 성공했으나 어떤 이유로든 데이터가 계속 옛날 것일 때
            if ((DateTime.Now - _lastSyncTime).TotalSeconds > 15 && _lastSyncTime != DateTime.MinValue)
            {
                UI($"🔥 [SYNC-STALL] <{_symbol}> 데이터 갱신 중단됨 ({(DateTime.Now - _lastSyncTime).TotalSeconds:F0}초 경과)");
            }
        }
        private int _externalEmptyCount = 0;
        private const int EXTERNAL_CONFIRM_COUNT = 1000; // 3회 연속 비어야 확정

        public async Task CheckExternalLiquidation(BinancePositionDetailsUsdt? longPos, BinancePositionDetailsUsdt? shortPos)
        {
            if (_positionId == 0) return;

            double secSinceIdChange = (DateTime.Now - _idChangedTime).TotalSeconds;
            if (_isProcessingOrder || secSinceIdChange < 60) return;

            bool exLongEmpty = (longPos == null || longPos.Quantity == 0);
            bool exShortEmpty = (shortPos == null || shortPos.Quantity == 0);

            if (exLongEmpty && exShortEmpty)
            {
                if (_externalEmptyCount % 10 == 0) // 10번에 1번만 실제 체크
                {
                    var accountInfo = await Ob.client.UsdFuturesApi.Account.GetAccountInfoV3Async();
                    if (accountInfo.Success)
                    {
                        var symPositions = accountInfo.Data.Positions
                            .Where(p => p.Symbol == _symbol && p.PositionAmount != 0);

                        if (symPositions.Any())
                        {
                            UI($"⚠️ [EXTERNAL-SKIP] <{_symbol}> AccountV3 포지션 확인됨, 오탐 무시");
                            _externalEmptyCount = 0;
                            return;
                        }
                    }
                }

                _externalEmptyCount++;
                UI($"⚠️ [EXTERNAL-CHECK] <{_symbol}> 거래소 잔고 없음 ({_externalEmptyCount}/{EXTERNAL_CONFIRM_COUNT}회)");

                if (_externalEmptyCount >= EXTERNAL_CONFIRM_COUNT)
                {
                    _externalEmptyCount = 0;
                    UI($"🚨 [EXTERNAL-DETECTED] <{_symbol}> {EXTERNAL_CONFIRM_COUNT}회 연속 확인 -> DB 강제 종료");
                    decimal lastPrice = _ex.GetLastPrice(_symbol);
                    await ForceCloseInternalState(lastPrice, "EXTERNAL_CLOSED");
                }
            }
            else
            {
                // 정상 데이터 수신 시 카운트 리셋
                if (_externalEmptyCount > 0)
                {
                    UI($"✅ [EXTERNAL-RESET] <{_symbol}> 거래소 데이터 복구 (카운트 리셋)");
                    _externalEmptyCount = 0;
                }
            }
        }
        private async Task<BinanceFuturesOrder> AddLong(decimal price, string reason, decimal? qty = null)
        {
            // 1. 수량 결정 로직
            decimal targetQty = await WithLock<decimal>(async () =>
            {
                decimal rawQty;
                if (qty.HasValue)
                {
                    rawQty = qty.Value; // 전략 레이어에서 계산된 수량 우선
                }
                else if (_longTotalQty == 0 && _shortTotalQty > 0)
                {
                    // 헷지 동기화 (숏 수량의 1.05배)
                    rawQty = _shortTotalQty * 1.10m;
                    UI($"🛡️ [HEDGE-SYNC] <{_symbol}> 최초 롱 헷지: 숏 수량({_shortTotalQty}) 복사(+10%)");
                }
                else
                {
                    // Fallback: 인자 없을 시 1.0m(라이트 기본) 배수 적용
                    rawQty = GetAsymmetricQty(true, 1.0m, _longTrades.Count);
                }
                return _ex.RoundQty(_symbol, rawQty);
            });

            if (targetQty <= 0) return null;

            decimal oldAvg = _longAvgPrice;
            decimal activationPct = 0.015m;
            decimal oldTp = oldAvg * (1 + activationPct);

            UI($"📉 [GRID-ADD-LONG] TRY <{_symbol}> +{targetQty} @ {price:F4} (Size:{targetQty * price:F2} USDT) ({reason})");

            // 2. 주문 실행
            var ret = await _ex.PlaceMarketOrderAsync(_symbol, Side.Buy, targetQty, false);
            if (ret != null)
            {
                _lastOrderTime = DateTime.Now;
                await WithLock(async () => {
                    _longTotalQty += ret.Quantity;
                    _longTotalCost += (ret.Quantity * price / _leverage);
                    _longAvgPrice = _longTotalCost * _leverage / _longTotalQty;

                    _recoveryBasePrice = 0;
                    _recoverySide = "NONE";

                    _longTrades.Add(new Trade
                    {
                        Time = DateTime.Now,
                        Side = "LONG",
                        Qty = ret.Quantity,
                        Price = price,
                        Cost = ret.Quantity * price / _leverage,
                        Reason = reason
                    });

                    _maxBudgetUsed = Math.Max(_maxBudgetUsed, _longTotalCost + _shortTotalCost);
                    LongMarginAddDt = DateTime.Now; _lastAddTime = DateTime.Now;
                    _lastAddTime = DateTime.Now;
                    decimal improvement = oldAvg > 0 ? (oldAvg - _longAvgPrice) / oldAvg : 0;
                    decimal newTp = _longAvgPrice * (1 + activationPct);
                    decimal distance = (newTp - price) / price;

                    UI($"📈 [GRID-ADD-LONG] <{_symbol}> +{ret.Quantity} @ {price:F4} (Size:{ret.Quantity * price:F2} USDT) ({reason})");
                    if (oldAvg > 0)
                    {
                        UI($"   └ [평단개선] {oldAvg:F4} -> {_longAvgPrice:F4} ({improvement:P2})");
                        UI($"   └ [목표가] {newTp:F4} (거리: {distance:P2} ▲)");
                    }

                    // 3. DB 기록 (로컬 시간으로 통일)
                    await Ob.db._dbMaria.HedgeGrid_AddTradeAsync(new HedgeGridTradeRow
                    {
                        position_id = _positionId,
                        trade_time = DateTime.Now,        // 로컬 시간
                        trade_time_utc = DateTime.Now,    // ✅ 사용자님 요청대로 Now로 통일
                        side = "LONG",
                        qty = ret.Quantity,
                        price = price,
                        cost = ret.Quantity * price / _leverage,
                        reason = reason
                    });
                });
                await UpdateDB(price);
            }
            return ret;
        }

        private async Task<BinanceFuturesOrder> AddShort(decimal price, string reason, decimal? qty = null)
        {
            // 1. 수량 결정 로직
            decimal targetQty = await WithLock<decimal>(async () =>
            {
                decimal rawQty;
                if (qty.HasValue)
                {
                    rawQty = qty.Value;
                }
                else if (_shortTotalQty == 0 && _longTotalQty > 0)
                {
                    rawQty = _longTotalQty * 1.10m;
                    UI($"🛡️ [HEDGE-SYNC] <{_symbol}> 최초 숏 헷지: 롱 수량({_longTotalQty}) 복사(+10%)");
                }
                else
                {
                    rawQty = GetAsymmetricQty(false, 1.0m, _shortTrades.Count);
                }
                return _ex.RoundQty(_symbol, rawQty);
            });

            if (targetQty <= 0) return null;

            decimal oldAvg = _shortAvgPrice;
            decimal activationPct = 0.015m;
            decimal oldTp = oldAvg * (1 - activationPct);

            UI($"📉 [GRID-ADD-SHORT] TRY <{_symbol}> +{targetQty} @ {price:F4} (Size:{targetQty * price:F2} USDT) ({reason})");
            // 2. 주문 실행
            var ret = await _ex.PlaceMarketOrderAsync(_symbol, Side.Sell, targetQty, false);
            if (ret != null)
            {
                _lastOrderTime = DateTime.Now;
                await WithLock(async () => {
                    _shortTotalQty += ret.Quantity;
                    _shortTotalCost += (ret.Quantity * price / _leverage);
                    _shortAvgPrice = _shortTotalCost * _leverage / _shortTotalQty;

                    _recoveryBasePrice = 0;
                    _recoverySide = "NONE";

                    _shortTrades.Add(new Trade
                    {
                        Time = DateTime.Now,
                        Side = "SHORT",
                        Qty = ret.Quantity,
                        Price = price,
                        Cost = ret.Quantity * price / _leverage,
                        Reason = reason
                    });

                    _maxBudgetUsed = Math.Max(_maxBudgetUsed, _longTotalCost + _shortTotalCost);
                    ShortMarginAddDt = DateTime.Now; _lastAddTime = DateTime.Now;
                    _lastAddTime = DateTime.Now;
                    decimal improvement = oldAvg > 0 ? (_shortAvgPrice - oldAvg) / oldAvg : 0;
                    decimal newTp = _shortAvgPrice * (1 - activationPct);
                    decimal distance = (price - newTp) / price;

                    UI($"📉 [GRID-ADD-SHORT] <{_symbol}> +{ret.Quantity} @ {price:F4} (Size:{ret.Quantity * price:F2} USDT) ({reason})");
                    if (oldAvg > 0)
                    {
                        UI($"   └ [평단개선] {oldAvg:F4} -> {_shortAvgPrice:F4} ({improvement:P2})");
                        UI($"   └ [목표가] {newTp:F4} (거리: {distance:P2} ▼)");
                    }

                    // 3. DB 기록 (로컬 시간으로 통일)
                    await Ob.db._dbMaria.HedgeGrid_AddTradeAsync(new HedgeGridTradeRow
                    {
                        position_id = _positionId,
                        trade_time = DateTime.Now,        // 로컬 시간
                        trade_time_utc = DateTime.Now,    // ✅ Now로 통일
                        side = "SHORT",
                        qty = ret.Quantity,
                        price = price,
                        cost = ret.Quantity * price / _leverage,
                        reason = reason
                    });
                });
                await UpdateDB(price);
            }
            return ret;
        }

        private async Task CloseAll(decimal price, string reason)
        {
            TimeSpan elapsed = DateTime.Now - _entryTime;
            string durationStr = $"{(int)elapsed.TotalDays}d {elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s";

            var (qL, qS, totalTrades, holdMins, finalSize) = await WithLock(async () =>
                (_longTotalQty, _shortTotalQty, _TotalTrades, (DateTime.Now - _entryTime).TotalMinutes, _longTotalQty + _shortTotalQty));

            _lastOrderTime = DateTime.Now;
            if (qL > 0)
            {
                await _ex.PlaceMarketOrderAsync(_symbol, Side.Sell, qL, true);
                // ✅ 롱 청산 내역 기록
                await Ob.db._dbMaria.HedgeGrid_AddTradeAsync(new HedgeGridTradeRow
                {
                    position_id = _positionId,
                    trade_time = DateTime.Now,
                    trade_time_utc = DateTime.Now,
                    side = "CLOSE_LONG",
                    qty = qL,
                    price = price,
                    cost = qL * price / _leverage,
                    reason = reason
                });
            }
            if (qS > 0)
            {
                await _ex.PlaceMarketOrderAsync(_symbol, Side.Buy, qS, true);
                // ✅ 숏 청산 내역 기록
                await Ob.db._dbMaria.HedgeGrid_AddTradeAsync(new HedgeGridTradeRow
                {
                    position_id = _positionId,
                    trade_time = DateTime.Now,
                    trade_time_utc = DateTime.Now,
                    side = "CLOSE_SHORT",
                    qty = qS,
                    price = price,
                    cost = qS * price / _leverage,
                    reason = reason
                });
            }
            decimal pnl = await WithLock(async () => {
                decimal p = ((price - _longAvgPrice) * _longTotalQty) + ((_shortAvgPrice - price) * _shortTotalQty);
                _longTotalQty = 0; _shortTotalQty = 0; _longTrades.Clear(); _shortTrades.Clear(); return p;
            });

            //_collector?.UpdateStopLossLabels(_positionId, pnl);
            //int addCount = Math.Max(0, totalTrades - 2);
            //_collector?.UpdateLabels(_positionId, (int)holdMins, addCount);
            //_collector?.UpdateAddLabels(_positionId, addCount, (int)holdMins, finalSize);
            _lastTpPrice = price;

            await Ob.db._dbMaria.HedgeGrid_CloseAsync(_positionId, price, DateTime.Now, reason, pnl, 0);
            UI($"✅ [GRID-CLOSE] <{_symbol}> {reason} 완전청산 | PnL: {pnl:F4} USDT | Duration: {durationStr}");
            _positionId = 0; _maxRecordedTotalPnl = 0;
        }

        private async Task ForceCloseInternalState(decimal price, string reason) { await CloseAll(price, reason); }

        private async Task UpdateDB(decimal price)
        {
            // 1. WithLock<T>를 사용하여 기존에 업데이트하던 필드들만 담은 객체 생성
            var row = await WithLock<HedgeGridPositionRow>(async () =>
            {
                // 기존 계산 로직 유지
                decimal lPnlUsd = _longTotalQty > 0 ? (price - _longAvgPrice) * _longTotalQty : 0m;
                decimal sPnlUsd = _shortTotalQty > 0 ? (_shortAvgPrice - price) * _shortTotalQty : 0m;
                decimal totalPnlUsd = lPnlUsd + sPnlUsd;

                decimal totalVal = (_longTotalQty * _longAvgPrice) + (_shortTotalQty * _shortAvgPrice);
                decimal totalPnlPct = totalVal > 0 ? (totalPnlUsd / totalVal) : 0m;

                decimal lPnlPercent = (_longTotalQty > 0 && _longAvgPrice > 0) ? ((price - _longAvgPrice) / _longAvgPrice) * 100m : 0m;
                decimal sPnlPercent = (_shortTotalQty > 0 && _shortAvgPrice > 0) ? ((_shortAvgPrice - price) / _shortAvgPrice) * 100m : 0m;
                decimal pnlGap = Math.Abs(lPnlPercent - sPnlPercent);

                decimal currentLongCost = _longTotalQty * _longAvgPrice;
                decimal currentShortCost = _shortTotalQty * _shortAvgPrice;
                decimal currentTotalCost = currentLongCost + currentShortCost;

                decimal currentLongMargin = (_longTotalQty * _longAvgPrice) / _leverage;
                decimal currentShortMargin = (_shortTotalQty * _shortAvgPrice) / _leverage;
                decimal currentTotalMargin = currentLongMargin + currentShortMargin;

                // 기존 HedgeGridPositionRow 클래스 사용
                return new HedgeGridPositionRow
                {
                    id = _positionId,
                    symbol = _symbol, // 큐에서 식별용

                    // 기존 인자 매핑
                    long_total_qty = _longTotalQty,
                    long_avg_price = _longAvgPrice,
                    long_total_cost = currentLongMargin,
                    long_position_count = _longTrades.Count,

                    short_total_qty = _shortTotalQty,
                    short_avg_price = _shortAvgPrice,
                    short_total_cost = currentShortMargin,
                    short_position_count = _shortTrades.Count,

                    current_price = price,
                    total_pnl_usdt = totalPnlUsd,
                    total_pnl_pct = totalPnlPct,
                    max_budget_used = currentTotalMargin,

                    long_pnl_usdt = lPnlUsd,
                    short_pnl_usdt = sPnlUsd,
                    total_trades = _TotalTrades,
                    recovery_base_price = _recoveryBasePrice,
                    recovery_side = _recoverySide,
                    max_recorded_pnl = _maxRecordedTotalPnl,

                    long_pnl_percent = lPnlPercent,
                    short_pnl_percent = sPnlPercent,
                    last_tp_price = _lastTpPrice,
                    pending_recovery_usd = _pendingRecoveryUsd, // 🚩 예약금 저장
                    pending_target_side = _pendingTargetSide,   // 🚩 예약 방향 저장

                    pnl_gap = pnlGap,
                    version = _version

                };
            });

            // 2. 락 밖에서 큐에 넣기 (DB 응답을 기다리지 않음)
            if (row != null && row.id > 0)
            {
                DbBatchManager.Enqueue(row);
            }
        }

        private async Task SyncConfigFromDbAsync()
        {
            if (_positionId == 0) return;

            try
            {
                // DB에서 현재 포지션의 최신 정보를 다시 읽어옴
                var latestRow = await Ob.db._dbMaria.HedgeGrid_SelectPositionAsync(_positionId);

                if (latestRow != null && latestRow.CfgMaxTrade != this.CfgMaxTrade)
                {
                    UI($"🔄 [CONFIG-SYNC] <{_symbol}> max_trade 변경 감지: {this.CfgMaxTrade} -> {latestRow.CfgMaxTrade}");

                    this.CfgMaxTrade = latestRow.CfgMaxTrade;
                }
                if (latestRow != null && latestRow.max_notional_cap != this._maxNotionalCap)
                {
                    UI($"🔄 [CONFIG-SYNC] <{_symbol}> max_notional_cap 변경 감지: {this._maxNotionalCap} -> {latestRow.max_notional_cap}");

                    this._maxNotionalCap = latestRow.max_notional_cap == null ? 40 : latestRow.max_notional_cap.Value;
                }
            }
            catch (Exception ex)
            {
                UI($"⚠️ [SYNC-ERR] <{_symbol}> 설정 로드 실패: {ex.Message}");
            }
        }

        private void UI(string msg) { try { Ob.ui?.SetText(msg); } catch { } }
        private void UI_ENTER(string msg) { try { Ob.ui?.SetEnter(msg); } catch { } }
        private class Trade { public DateTime Time { get; set; } = DateTime.Now; public string Side { get; set; } public decimal Qty { get; set; } public decimal Price { get; set; } public decimal Cost { get; set; } public string Reason { get; set; } }
    }
    public static class DbBatchManager
    {
        // 심볼별로 최신 상태 하나만 유지 (Dictionary 사용으로 중복 방지)
        private static ConcurrentDictionary<string, HedgeGridPositionRow> _updateBuffer = new ConcurrentDictionary<string, HedgeGridPositionRow>();
        private static System.Timers.Timer _batchTimer;

        static DbBatchManager()
        {
            _batchTimer = new System.Timers.Timer(3000); // 1초마다 실행
            _batchTimer.Elapsed += async (s, e) => await FlushToDb();
            _batchTimer.Start();
        }

        public static void Enqueue(HedgeGridPositionRow row)
        {
            // 최신 데이터로 덮어쓰기
            _updateBuffer[row.symbol] = row;
        }

        private static async Task FlushToDb()
        {
            if (_updateBuffer.IsEmpty) return;

            // 버퍼 복사 및 비우기
            var items = _updateBuffer.Values.ToList();
            _updateBuffer.Clear();

            if (items.Count == 0) return;

            try
            {
                // MariaDB 벌크 업데이트 실행 (아래 3번 항목에서 구현)
                await Ob.db._dbMaria.BulkUpdateHedgePositionsAsync(items);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB-BATCH-ERROR] {ex.Message}");
            }
        }
    }
    public static class FilterLogBatchManager
    {
        private static ConcurrentQueue<object> _logQueue = new ConcurrentQueue<object>();
        private static System.Timers.Timer _flushTimer;

        static FilterLogBatchManager()
        {
            _flushTimer = new System.Timers.Timer(2000); // 2초마다 DB에 쏟아붓기
            _flushTimer.Elapsed += async (s, e) => await FlushLogs();
            _flushTimer.Start();
        }

        public static void Enqueue(object log) => _logQueue.Enqueue(log);

        private static async Task FlushLogs()
        {
            if (_logQueue.IsEmpty) return;

            var logs = new List<object>();
            while (_logQueue.TryDequeue(out var log)) logs.Add(log);

            if (logs.Count > 0)
            {
                // MariaDB 벌크 인서트 호출
                await Ob.db._dbMaria.BulkInsertFilterLogsAsync(logs);
            }
        }
    }
    public class RiskConfig
    {
        // ========== 기본 설정 ==========
        public decimal Leverage { get; set; } = 10m;
        public decimal EntryNotionalPct { get; set; } = 0.10m;  // 잔고의 10%
        public int MaxConcurrentEntries { get; set; } = 125;

        // ========== 신호 필터 ==========
        public double AdxMin { get; set; } = 15.0;
        public decimal VolumeSpikeRatio { get; set; } = 2.0m;
        public decimal MomentumSpikeRatio { get; set; } = 0.015m;

        // ========== Order Flow ==========
        public bool UseOrderFlow { get; set; } = true;
        public double DeltaBiasMin { get; set; } = 0.40;

        // ========== 손실 제한 ==========
        public bool UseConsecutiveLossBreak { get; set; } = false;
        public int MaxConsecutiveLosses { get; set; } = 3;
        public bool UseDailyLossLimit { get; set; } = false;
        public decimal DailyLossLimitUsd { get; set; } = 50m;

        // ========== 헷지-그리드 전략 ==========
        public bool UseHedgeGrid { get; set; } = true;
        public decimal HedgeMultiplier { get; set; } = 1.5m;
        public int MaxGridLevels { get; set; } = 5;              // 최대 그리드 레벨
        public decimal GridSpacing { get; set; } = 0.003m;       // 그리드 간격 0.3%
        public decimal HedgeProfitTarget { get; set; } = 0.001m; // 청산 목표 +0.1%

        // ========== 포트폴리오 리스크 ==========
        public decimal PortfolioRiskCap { get; set; } = 0.10m;   // 포트폴리오 리스크 한도 10%

        // ========== ✅ HedgeGrid 전략 설정 ==========
        public decimal HedgeGridInitialBudget { get; set; } = 5m;        // 초기 진입 금액 (USDT)
        public decimal HedgeGridAddPositionPct { get; set; } = 0.3m;      // 추가 진입 비율 (초기금액의 30%)
        public decimal HedgeGridTargetProfitPct { get; set; } = 0.02m;    // 익절 목표 (2%)
        public decimal HedgeGridMinGapForClose { get; set; } = 0.01m;     // 최소 청산 갭 (1%)
                                                                          // 2. 손절 기준을 대폭 완화 (기존 물린 애들 즉시 사망 방지)
                                                                          // 상황이 안정되면 나중에 다시 -0.05(-5%)로 돌려놓으세요.
        public decimal HedgeGridEmergencyStopPct { get; set; } = -0.30m;  // 긴급 손절 (-5%)
        public decimal HedgeGridDoubleLossPct { get; set; } = -0.20m;     // 양방향 손실 기준 (-5%)
        public decimal HedgeGridGapLossGap { get; set; } = 0.10m;         // GAP_LOSS 갭 기준 (10%)
        public decimal HedgeGridGapLossPnl { get; set; } = -0.20m;        // GAP_LOSS PnL 기준 (-1%)
        public decimal HedgeGridHedgeTriggerPct { get; set; } = -0.05m;   // 헤지 진입 손실 기준 (-5%)
        public decimal HedgeGridNarrowGapMinProfit { get; set; } = 0.01m; // 갭축소 청산 최소수익 (1%)


        public int HedgeGridMaxTotalTrades { get; set; } = 10;
        public int HedgeGridDoubleLossMinTrades { get; set; } = 10;       // 0.8배 구간 끝날 때쯤 체크
        public int HedgeGridGapLossMinTrades { get; set; } = 10;          // 1.2배 들어가기 직전에 체크
    }
}