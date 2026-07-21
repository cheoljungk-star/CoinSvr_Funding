using Binance.Net.Enums;
using Binance.Net.Objects.Models.Futures;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoinSvr
{
    /// <summary>
    /// LAB 펀딩비 스냅샷 수령 전략
    /// - 펀딩 5초 전: 롱 진입
    /// - 펀딩 지급 후 1초: 롱 청산
    /// - 펀딩비 양수면 사이클 스킵
    /// (펀딩비가 스냅샷 정산이라 헤지/숏청산 불필요, 롱만 짧게 보유)
    /// </summary>
    public sealed class LabFundingHedger
    {
        private readonly IExchange _ex = new MockExchange();
        private const string SYMBOL = "LABUSDT";
        private const decimal QTY = 100m;
        private const int PRE_ENTRY_SEC = 5;    // 펀딩 5초 전 진입
        private const int POST_FUNDING_SEC = 1; // 펀딩 후 1초 뒤 청산

        private CancellationTokenSource? _cts;
        private bool _positionActive = false;
        private decimal _activeQty = QTY;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        // ─── Public API ───────────────────────────────────────────

        public async Task StartAsync()
        {
            try
            {
                if (IsRunning) { UI("⚠️ 이미 실행 중"); return; }

                UI("🚀 [LAB-FUNDING] 기존 포지션 확인 중...");

                var posResult = await Ob.client.UsdFuturesApi.Account
                    .GetPositionInformationAsync(SYMBOL);

                decimal currentLong = 0m;
                if (posResult.Success && posResult.Data != null)
                {
                    foreach (var p in posResult.Data)
                        if (p.PositionSide == PositionSide.Long) currentLong = Math.Abs(p.Quantity);
                }
                UI($"📊 [LAB-FUNDING] 현재 롱: {currentLong}");

                if (currentLong > 0)
                {
                    UI("🧹 [LAB-FUNDING] 잔여 롱 포지션 정리");
                    await PlaceOrderWithRetryAsync(Side.Sell, currentLong, reduceOnly: true);
                }
                _positionActive = false;

                _cts = new CancellationTokenSource();
                _ = RunLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                UI($"❌ [LAB-FUNDING] StartAsync: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                UI("🛑 [LAB-FUNDING] 종료 요청");
                _cts?.Cancel();

                if (_positionActive)
                {
                    await PlaceOrderWithRetryAsync(Side.Sell, _activeQty, reduceOnly: true);
                    _positionActive = false;
                }

                UI("✅ [LAB-FUNDING] 종료 및 청산 완료");
            }
            catch (Exception ex)
            {
                UI($"❌ [LAB-FUNDING] StopAsync: {ex.Message}");
            }
        }

        // ─── Private ──────────────────────────────────────────────

        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 1. 펀딩비 + 다음 펀딩 시각 조회
                    var (rate, nextFunding) = await GetFundingInfoAsync(ct);
                    UI($"📊 [LAB-FUNDING] 현재 펀딩비: {rate:P4} / 다음 펀딩: {nextFunding:HH:mm:ss}");

                    if (rate > 0)
                    {
                        UI("⚠️ [LAB-FUNDING] 펀딩비 양수 - 1분마다 재확인 대기");
                        await Task.Delay(TimeSpan.FromMinutes(1), ct);
                        continue;
                    }

                    // 2. 펀딩 5초 전까지 대기 (30초마다 시각 재확인, 펀딩주기 변경 대응)
                    DateTime lastLogTime = DateTime.MinValue;

                    while (!ct.IsCancellationRequested)
                    {
                        var (_, nextFundingCheck) = await GetFundingInfoAsync(ct);

                        if (Math.Abs((nextFundingCheck - nextFunding).TotalSeconds) > 5)
                        {
                            UI($"🔄 [LAB-FUNDING] 펀딩 시각 변경 감지: {nextFunding:HH:mm:ss} → {nextFundingCheck:HH:mm:ss}");
                            nextFunding = nextFundingCheck;
                        }

                        TimeSpan remain = nextFunding - DateTime.UtcNow;

                        if ((DateTime.UtcNow - lastLogTime).TotalMinutes >= 10)
                        {
                            UI($"⏳ [LAB-FUNDING] 다음 펀딩까지 {remain.Hours:D2}:{remain.Minutes:D2}:{remain.Seconds:D2} 남음");
                            lastLogTime = DateTime.UtcNow;
                        }

                        // 펀딩 30초 전부터는 짧은 간격으로 정밀 체크
                        if (remain.TotalSeconds <= 30)
                        {
                            if (remain.TotalSeconds <= PRE_ENTRY_SEC) break;
                            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                            continue;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    }

                    if (ct.IsCancellationRequested) break;

                    // 3. 진입 직전 펀딩비 재확인 (대기 중 양수 전환 가능성)
                    var (rateCheck, _) = await GetFundingInfoAsync(ct);
                    if (rateCheck > 0)
                    {
                        UI("⚠️ [LAB-FUNDING] 진입 직전 펀딩비 양수 전환 - 이번 사이클 스킵");
                        while (!ct.IsCancellationRequested && DateTime.UtcNow < nextFunding.AddSeconds(POST_FUNDING_SEC))
                            await Task.Delay(TimeSpan.FromSeconds(1), ct);
                        continue;
                    }

                    // 4. 롱 진입 (펀딩 5초 전)
                    await OpenLongAsync(ct);
                    if (!_positionActive) { continue; } // 진입 실패 시 다음 루프 재시도

                    // 5. 펀딩 지급 + 1초까지 대기
                    while (!ct.IsCancellationRequested)
                    {
                        if (DateTime.UtcNow >= nextFunding.AddSeconds(POST_FUNDING_SEC))
                            break;
                        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                    }

                    // 6. 롱 청산 (취소 요청 시에도 청산은 수행)
                    await CloseLongAsync(ct.IsCancellationRequested ? CancellationToken.None : ct);

                    if (ct.IsCancellationRequested) break;
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                UI($"❌ [LAB-FUNDING] RunLoop 예외: {ex.Message}");
            }
        }

        private async Task OpenLongAsync(CancellationToken ct)
        {
            try
            {
                decimal price = await GetCurrentPriceAsync();
                decimal qty = GetAffordableQty(price);
                if (qty <= 0)
                {
                    UI("❌ [LAB-FUNDING] 가용 금액 부족 - 진입 불가");
                    return;
                }

                UI($"📥 [LAB-FUNDING] 펀딩 임박 - 롱 진입 ({qty})");
                var order = await PlaceOrderWithRetryAsync(Side.Buy, qty, ct: ct);

                if (order != null)
                {
                    _positionActive = true;
                    _activeQty = qty;
                    UI($"✅ [LAB-FUNDING] 진입 완료 (롱 {qty} 보유)");
                }
                else
                {
                    UI("🚨 [LAB-FUNDING] 진입 실패");
                    _positionActive = false;
                }
            }
            catch (Exception ex) { UI($"❌ [LAB-FUNDING] OpenLong: {ex.Message}"); }
        }

        private async Task CloseLongAsync(CancellationToken ct)
        {
            try
            {
                if (!_positionActive) return;

                UI($"📤 [LAB-FUNDING] 펀딩 수령 완료 - 롱 청산 ({_activeQty})");
                var order = await PlaceOrderWithRetryAsync(Side.Sell, _activeQty, reduceOnly: true, ct: ct);
                if (order != null) UI("✅ [LAB-FUNDING] 청산 완료 (무포지션 복귀)");
                else UI("🚨 [LAB-FUNDING] 청산 실패 → 수동 확인 필요");

                _positionActive = false;
            }
            catch (Exception ex) { UI($"❌ [LAB-FUNDING] CloseLong: {ex.Message}"); }
        }

        /// <summary>가용 잔고 기준으로 진입 가능한 수량 계산 (단방향 마진 기준)</summary>
        private decimal GetAffordableQty(decimal price)
        {
            try
            {
                if (price <= 0)
                {
                    UI("❌ [LAB-FUNDING] 가격 조회 실패 - 수량 계산 불가");
                    return 0;
                }

                decimal balance = Ob.AvailableBalance;
                const int leverage = 10;
                decimal maxNotional = balance * leverage * 0.95m; // 안전버퍼 5%
                decimal maxQtyByBalance = maxNotional / price;

                decimal targetQty = Math.Min(QTY, maxQtyByBalance);
                decimal rounded = _ex.RoundQty(SYMBOL, targetQty);

                if (rounded < QTY)
                    UI($"⚠️ [LAB-FUNDING] 가용금액 부족 - 목표 {QTY} → 조정 {rounded} (잔고 {balance:F2}$)");

                return rounded;
            }
            catch (Exception ex)
            {
                UI($"❌ [LAB-FUNDING] GetAffordableQty: {ex.Message}");
                return 0m;
            }
        }

        /// <summary>Binance 마크프라이스 직접 조회 (MockExchange는 가격 피드가 없어 항상 0 반환됨)</summary>
        private async Task<decimal> GetCurrentPriceAsync()
        {
            try
            {
                var result = await Ob.client.UsdFuturesApi.ExchangeData.GetMarkPriceAsync(SYMBOL);
                if (result.Success && result.Data != null)
                    return result.Data.MarkPrice;
            }
            catch (Exception ex)
            {
                UI($"❌ [LAB-FUNDING] GetCurrentPrice: {ex.Message}");
            }
            return 0m;
        }

        private async Task<BinanceFuturesOrder?> PlaceOrderWithRetryAsync(
            Side side, decimal qty, bool reduceOnly = false, int maxRetry = 3, CancellationToken ct = default)
        {
            for (int i = 1; i <= maxRetry; i++)
            {
                try
                {
                    var order = await _ex.PlaceMarketOrderAsync(SYMBOL, side, qty, reduceOnly);
                    if (order != null) return order;
                    UI($"⚠️ [LAB-FUNDING] 주문 실패 ({i}/{maxRetry}) side={side} qty={qty} - 1초 후 재시도");
                }
                catch (Exception ex)
                {
                    UI($"❌ [LAB-FUNDING] 주문 예외 ({i}/{maxRetry}): {ex.Message}");
                }
                if (i < maxRetry) await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            UI($"🚨 [LAB-FUNDING] 주문 최종 실패 side={side} qty={qty} → 수동 확인 필요");
            return null;
        }

        private async Task<(decimal Rate, DateTime NextFunding)> GetFundingInfoAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await Ob.client.UsdFuturesApi.ExchangeData.GetMarkPriceAsync(SYMBOL);
                    if (result.Success && result.Data != null)
                        return (result.Data.FundingRate ?? 0m, result.Data.NextFundingTime);

                    UI("⚠️ [LAB-FUNDING] 펀딩 정보 조회 실패 - 10초 후 재시도");
                }
                catch (Exception ex)
                {
                    UI($"❌ [LAB-FUNDING] GetFundingInfo: {ex.Message} - 10초 후 재시도");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
            return (-1m, DateTime.UtcNow);
        }

        private void UI(string msg) { try { Ob.ui?.SetText(msg); } catch { } }
    }
}