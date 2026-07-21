using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Futures.Socket;
using Binance.Net.Objects.Models.Spot.Margin;
using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Sockets;
using MySql.Data.MySqlClient;
using MySqlX.XDevAPI;
using MySqlX.XDevAPI.Common;
using MySqlX.XDevAPI.Relational;
using Nancy.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Ocsp;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CoinSvr
{
    public class select_OrderBook_All
    {
        Task MainTask;
        public Thread MainThread2;

        static CancellationTokenSource cts = new CancellationTokenSource();
        CancellationToken ct = cts.Token;
        
        public select_OrderBook_All()
        {
            DataProc();
            MainTask = Task.Run(() => SelectPosition(ct));

            MainThread2 = new Thread(new ThreadStart(this.SelectAccount));
            MainThread2.IsBackground = true;
            MainThread2.Start();
            //Task t1 = new Task(new Action(SelectPosition));
            //t1.Start();
        }
        public async Task DataProc()
        {
            try
            {
                Ob.socketClient = new BinanceSocketClient();

                var symbols = new List<string> { };
                foreach (var de in Ob.CoinHT)
                {
                    symbols.Add(de.Key.ToString());
                }

                foreach (var symbol in symbols)
                {
                    try
                    {
                        //var result = await Ob.socketClient.UsdFuturesApi.ExchangeData.SubscribeToTradeUpdatesAsync(symbol.ToUpper(), data =>
                        //{
                        //    //Ob.ui.SetText($"[{symbol}]{data.Data.TradeTime}: {data.Data.Quantity} @ {data.Data.Price}");
                        //    if (Ob.CoinHT.ContainsKey(data.Symbol.ToUpper()))
                        //    {
                        //        var o = (COIN_OBJECT_)Ob.CoinHT[data.Symbol.ToUpper()];

                        //        NEW_PRICE_ now_Price = new NEW_PRICE_();
                        //        now_Price.bids = data.Data.Price.ToString();
                        //        now_Price.asks = data.Data.Price.ToString();
                        //        //Logger logger = LogManager.GetLogger("signal");
                        //        //logger.Info($"<{data.Symbol.ToUpper()}> Current = {data.Data.Price.ToString("#,0.######")}");
                        //        o.transation.enQueue(now_Price);
                        //    }
                        //});

                        //if (result.Success)
                        //{
                        //    Ob.subscriptions.Add(result.Data);
                        //    Ob.ui.SetText($"[{symbol}] 구독 성공");
                        //}
                        //else
                        //{
                        //    Ob.ui.SetText($"[{symbol}] 구독 실패 : {result.Error}");
                        //}
                        await Ob.socketClient_postion.UsdFuturesApi.ExchangeData.SubscribeToMarkPriceUpdatesAsync(symbol, 1000, onMessage: async dataEvent =>
                        {
                            if (Ob.positionCache == null) return;
                            if (!Ob.positionCache.TryGetValue(symbol, out var pos) || pos.Quantity == 0) return;
                            var mark = dataEvent.Data;
                            
                            int Leverage = 10;
                            decimal isolatedMargin = pos.IsolatedMargin;
                            if(isolatedMargin > 0 || pos.MarginType == FuturesMarginType.Isolated)
                            {
                                decimal maintenanceMargin = Math.Abs(pos.Quantity) * mark.MarkPrice / Leverage;
                                decimal remainingPct = isolatedMargin / maintenanceMargin * 100;

                                if (remainingPct < 20)
                                {
                                    decimal marginToAdd = isolatedMargin * 0.20m; // 증거금의 15% 추가
                                    Ob.ui.SetText($"<{pos.Symbol}:{pos.PositionSide.ToString().ToUpper()}> 증거금 추가(0) : {marginToAdd:F4} USDT");
                                    await PositionMarginLongShortAsync(symbol, marginToAdd, pos.PositionSide);

                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Ob.ui.SetText("-----> ERROR [symbols<" + symbol + ">]" + ex.Message);
                    }
                }
                await this.SubscribeUserData();
               
            }
            catch (Exception ex)
            {
                Ob.ui.SetText("-----> ERROR [DataProc 1]" + ex.Message);
            }
        }
        public async Task SubscribeUserData()
        {
            await Ob.app.GeneListenKey();
            var sub = await Ob.socketClient_postion.UsdFuturesApi.Account.SubscribeToUserDataUpdatesAsync(Ob._listenKey,
                    onAccountUpdate: dataEvent =>
                    {
                        var acct = dataEvent.Data.UpdateData;

                        foreach (var p in acct.Positions)
                            Ob.positionCache[p.Symbol] = p;

                        if (dataEvent.Data.Event == "MARGIN_CALL")
                        {
                            Ob.ui.SetText($"!!!!! MARGIN CALL ALERT !!!!!");
                        }
                        Ob.ui.SetText($"<USER-DATA>[{dataEvent.Data.Event}] >> {dataEvent.Data.ToString()}");
                        foreach (var position in acct.Positions)
                        {
                            Ob.ui.SetText($"<USER-POSITIONS> >> {position.ToString()}");
                            if(Ob.CoinHT.ContainsKey(position.Symbol))
                            {
                                var o = (COIN_OBJECT_)Ob.CoinHT[position.Symbol.ToUpper()];
                                if(position.PositionSide == PositionSide.Long)
                                {
                                    o.transation.MarginLong = (double)position.IsolatedMargin;
                                }
                                else if(position.PositionSide == PositionSide.Short)
                                {
                                    o.transation.MarginShort = (double)position.IsolatedMargin;
                                }
                            }
                        }
                    },
                    onListenKeyExpired: async _ =>
                    {
                        Ob.ui.SetText($"[ERROR]SubscribeToUserDataUpdatesAsync 키 만료");
                        await this.SubscribeUserData();
                    }
                );
            if (!sub.Success)
            {
                Ob.ui.SetText($"스트림 구독 실패 : " + sub.Error);
                await CleanupAsync();
                return;
            }
            Ob.ui.SetText($"스트림 구독 시작");
        }
        //public async Task PositionMarginAsync(string symbol, decimal amount)
        //{
        //    var result = await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, amount, FuturesMarginChangeDirectionType.Add);
        //    if (result.Success)
        //    {
        //        Ob.ui.SetText("<" + symbol + ">[" + symbol + "<ModifyPositionMarginAsync-성공>] (" + amount.ToString() + ") " + result.Data.Message.ToString());
        //        return;
        //    }
        //    else
        //    {
        //        Ob.ui.SetText("<" + symbol + ">[" + symbol + "<ModifyPositionMarginAsync-오류>] " + result.Error.Message);
        //        return;
        //    }
        //}
        public async Task PositionMarginLongShortAsync(string symbol, decimal amount, PositionSide side)
        {
            // 내부 재시도 정책: transient/rate-limit 오류에 대해 최대 3회 재시도
            const int maxAttempts = 3;

            static bool IsRateLimitOrTransient(Exception ex)
            {
                if (ex == null) return false;
                var msg = ex.Message ?? string.Empty;
                if (msg.Contains("429") || msg.Contains("418", StringComparison.OrdinalIgnoreCase)) return true;
                if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return true;
                return ex is HttpRequestException
                    || ex is System.Net.Sockets.SocketException
                    || (ex is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested);
            }

            static bool IsRateLimitOrTransientErrorMessage(string? errMsg)
            {
                if (string.IsNullOrEmpty(errMsg)) return false;
                if (errMsg.Contains("429") || errMsg.Contains("418", StringComparison.OrdinalIgnoreCase)) return true;
                if (errMsg.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    FuturesMarginChangeDirectionType type = amount >= 0
                        ? FuturesMarginChangeDirectionType.Add
                        : FuturesMarginChangeDirectionType.Reduce;

                    decimal absAmount = Math.Abs(amount);

                    var result = await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, absAmount, type, side);

                    if (result.Success)
                    {
                        Ob.ui.SetText("<" + symbol + ">[" + symbol + " - " + side.ToString().ToUpper() + " [" + type.ToString().ToUpper() + "] <ModifyPositionMarginAsync-성공>] (" + amount.ToString() + ") " + result.Data.Message.ToString());
                        return;
                    }
                    else
                    {
                        // 실패 응답은 로그로 남기고, transient 여부에 따라 재시도 여부 결정
                        var errMsg = result.Error?.Message ?? result.Error?.ToString() ?? "Unknown error";
                        Ob.ui.SetText("<" + symbol + ">[" + symbol + " - " + side.ToString().ToUpper() + " [" + type.ToString().ToUpper() + "] <ModifyPositionMarginAsync-오류>] " + errMsg);

                        if (IsRateLimitOrTransientErrorMessage(errMsg) && attempt < maxAttempts - 1)
                        {
                            // 지수 백오프
                            var delay = 200 * (int)Math.Pow(2, attempt) + Random.Shared.Next(0, 120);
                            await Task.Delay(delay);
                            continue;
                        }

                        // 비정상/비재시도 오류인 경우 그대로 종료 (예외 전파하지 않음)
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // 내부에서 전부 잡아서 로깅만 하고 전파하지 않음 — 호출자는 실패로 인한 예외를 받지 않음
                    Ob.app._ERROR("PositionMarginLongShortAsync", ex);

                    if (!IsRateLimitOrTransient(ex) || attempt >= maxAttempts - 1)
                    {
                        // 재시도 불가 오류거나 마지막 시도이면 종료
                        return;
                    }

                    // transient 오류일 경우 재시도 (지수 백오프)
                    var delay = 200 * (int)Math.Pow(2, attempt) + Random.Shared.Next(0, 120);
                    await Task.Delay(delay);
                }
            }
        }
        public async Task CleanupAsync()
        {
            Ob._keepAliveTimer?.Dispose();

            if (!string.IsNullOrEmpty(Ob._listenKey) && Ob.client != null)
            {
                var stopRes = await Ob.client.UsdFuturesApi.Account.StopUserStreamAsync(Ob._listenKey);
                if (stopRes.Success)
                    Ob.ui.SetText($"listenKey 삭제 완료");
                else
                    Ob.ui.SetText($"listenKey 삭제 실패: {stopRes.Error}");

                Ob._listenKey = null;
            }

            Ob.client?.Dispose();
            Ob.socketClient?.Dispose();
            Ob.socketClient_postion?.Dispose();
        }
      
        public async Task SelectPosition(CancellationToken ct)
        {
            try
            {
                // API 요청 병목 현상을 방지하기 위한 세마포어
                using var postGate = new SemaphoreSlim(100);

                Task RunWithGate(Func<CancellationToken, Task> action) => GateWrapperAsync(action, ct);

                async Task GateWrapperAsync(Func<CancellationToken, Task> action, CancellationToken ct)
                {
                    await postGate.WaitAsync(ct);
                    try
                    {
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            try
                            {
                                await action(ct);
                                return;
                            }
                            catch (Exception ex) when (IsRateLimitOrTransient(ex) && attempt < 1 && !ct.IsCancellationRequested)
                            {
                                var delay = 200 * (int)Math.Pow(2, attempt) + Random.Shared.Next(0, 120);
                                await Task.Delay(delay, ct);
                            }
                        }
                    }
                    finally { postGate.Release(); }
                }

                static bool IsRateLimitOrTransient(Exception ex)
                {
                    var msg = ex.Message ?? string.Empty;
                    if (msg.Contains("429") || msg.Contains("418", StringComparison.OrdinalIgnoreCase)) return true;
                    if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return true;
                    return ex is HttpRequestException || ex is System.Net.Sockets.SocketException || (ex is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested);
                }

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // [STEP 0] 계정 정보 유효성 체크
                        if (Ob.FAccount == null)
                        {
                            await Task.Delay(500, ct);
                            continue;
                        }

                        var addOps = new List<Func<Task>>();
                        var removeOps = new List<Func<Task>>();
                        var syncData = new Dictionary<string, (BinancePositionDetailsUsdt longPos, BinancePositionDetailsUsdt shortPos)>();

                        var positions = await this.GetPositions();

                        if (positions != null)
                        {
                            // --- [STEP 1] 계좌 레벨 상태 및 3단계 파라미터 확정 ---
                            decimal marginBalance = Ob.FAccount.TotalMarginBalance > 0 ? Ob.FAccount.TotalMarginBalance : 1;
                            decimal totalInitialMargin = Ob.FAccount.TotalInitialMargin;
                            decimal marginUsageRatio = (totalInitialMargin / marginBalance) * 100;

                            // 사용률 연동 3단계 피드백 루프 (현재 52.8% -> 2단계 모드 작동)
                            var (usageFactor, recoveryThreshold, recoveryCooldown) = marginUsageRatio switch
                            {
                                > 55.0m => (0.75m, 10.0m, 2), // 0.5 -> 0.75로 상향
                                > 45.0m => (0.85m, 15.0m, 3), // 0.7 -> 0.85로 상향 (현재 이 단계)
                                _ => (1.00m, 20.0m, 5)
                            };

                            // [체크포인트] UpdateTime 필드가 없다면 WebSocket 수신 시각 등을 사용하세요.
                            DateTime lastAccountUpdateDt = Ob.LastAccountUpdateDt;
                            var v1DbQueries = new List<string>();
                            // --- [STEP 2] 포지션 루프 (데이터 수집 및 마진 로직 계산) ---
                            foreach (var position in positions)
                            {
                                try
                                {
                                    if (position.EntryPrice == 0) continue;
                                    if (position.Symbol == "LABUSDT" || position.Symbol == "BABYUSDT" || position.Symbol == "HUMAUSDT") continue;
                                    if (!Ob.bot.hedgeGrids.TryGetValue(position.Symbol, out var grid)) continue;
                                    if (!Ob.CoinHT.ContainsKey(position.Symbol)) continue;

                                    if (grid._version == 1)
                                    {
                                        string dbPrefix = "Long";
                                        decimal quantity = Math.Abs(position.Quantity);
                                        decimal investMoney = position.BreakEvenPrice * quantity;
                                        decimal isolatedMargin = position.IsolatedMargin;

                                        v1DbQueries.Add($@"UPDATE abuy2way 
                                            SET CurrentMoney={position.MarkPrice}, 
                                            {dbPrefix}Qty={quantity}, 
                                            {dbPrefix}Pnl={position.UnrealizedPnl}, 
                                            {dbPrefix}BaseMoney={position.BreakEvenPrice}, 
                                            {dbPrefix}InvestMoney={investMoney}, 
                                            {(isolatedMargin != 0 ? $"{dbPrefix}Margin={isolatedMargin}," : "")} 
                                            updateDt=now() 
                                        WHERE bCoin = '{position.Symbol}' AND status = 0 
                                        ORDER BY StartDt DESC LIMIT 1");
                                    }

                                    // [🔴 수정: 필수] syncData 채우기 (동기화 로직용)
                                    if (!syncData.ContainsKey(position.Symbol)) syncData[position.Symbol] = (null, null);
                                    var currentSync = syncData[position.Symbol];
                                    bool isLong = position.PositionSide == PositionSide.Long;
                                    syncData[position.Symbol] = isLong ? (position, currentSync.shortPos) : (currentSync.longPos, position);
                                    if (position.MarginType == FuturesMarginType.Cross)
                                    {
                                        continue;
                                    }

                                    // [🔴 수정] 변수 선언을 로직 A, B 공용으로 루프 상단에 배치
                                    var capSymbol = position.Symbol;
                                    var capSide = position.PositionSide;
                                    var capGrid = grid;

                                    // [🟡 수정] 가상 잔고 동기화 (서버 업데이트가 있을 때만 갱신)
                                    if (grid.VirtualIsolatedWallet == 0 || grid.LastSyncDt < lastAccountUpdateDt)
                                    {
                                        grid.VirtualIsolatedWallet = position.IsolatedWallet;
                                        grid.LastSyncDt = lastAccountUpdateDt;
                                    }

                                    // 주요 변수 설정
                                    decimal liquidationPrice = position.LiquidationPrice;
                                    decimal markPrice = position.MarkPrice;
                                    decimal initialMargin = Math.Abs(position.Notional) / position.Leverage;
                                    decimal pnl = position.UnrealizedPnl;
                                    decimal lossCover = pnl < 0 ? Math.Abs(pnl) : 0;
                                    decimal pnlRatio = Math.Abs(position.Notional) > 0 ? Math.Abs(pnl / position.Notional) : 0;
                                    decimal distancePercent = (liquidationPrice > 0) ? Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100) : 999m;

                                    DateTime lastAddDt = isLong ? grid.LongMarginAddDt : grid.ShortMarginAddDt;
                                    bool bAddPending = false;

                                    // 4. [로직 A] 증거금 추가 (청산 방어 - 상한선 적용 버전)
                                    if (distancePercent <= 3.0m)
                                    {
                                        // [전략 수정] 40%를 투입하되, 1회 최대 투입액을 200 USDT로 제한
                                        // 이렇게 하면 규모가 큰 종목도 루프를 돌며 단계적으로 방어하게 됩니다.
                                        decimal marginToAdd = Math.Max(position.IsolatedMargin * 0.40m, 10.0m);
                                        marginToAdd = Math.Min(marginToAdd, 200.0m); // 1회 최대 200 USDT 캡 (지적 사항 반영)

                                        decimal availableBalance = Ob.AvailableBalance;

                                        // 가용 자산이 부족한 경우 (최대한 쥐어짜서 방어)
                                        if (availableBalance < marginToAdd * 1.1m)
                                        {
                                            marginToAdd = availableBalance * 0.9m;
                                            if (marginToAdd < 5.0m)
                                            {
                                                Ob.ui.SetText($"<{capSymbol}> 🚨 가용자산 고갈(5불미만) 방어 포기");
                                                continue;
                                            }
                                        }

                                        if ((DateTime.Now - lastAddDt).TotalSeconds > 1)
                                        {
                                            // 선반영 (가상 장부)
                                            capGrid.VirtualIsolatedWallet += marginToAdd;
                                            if (isLong) capGrid.LongMarginAddDt = DateTime.Now;
                                            else capGrid.ShortMarginAddDt = DateTime.Now;

                                            bAddPending = true;

                                            var capAddAmt = marginToAdd;
                                            addOps.Add(() => RunWithGate(async token => {
                                                try
                                                {
                                                    await PositionMarginLongShortAsync(capSymbol, capAddAmt, capSide);
                                                }
                                                catch (Exception ex)
                                                {
                                                    // 방어 실패 시 롤백 (장부 무결성)
                                                    capGrid.VirtualIsolatedWallet -= capAddAmt;
                                                    Ob.ui.SetText($"<{capSymbol}> 🛡️ 방어 실패(롤백): {ex.Message}");
                                                }
                                            }));

                                            Ob.ui.SetText($"<{capSymbol}> 🛡️ 증거금 방어 : {capAddAmt:F2} USDT (상한선적용, 거리:{distancePercent:F2}%)");
                                        }
                                    }
                                    // 5. [로직 B] 증거금 회수 (손실분 차감 정밀 계산)
                                    if (!bAddPending && distancePercent > 5.0m)
                                    {
                                        // 유지 마진 버퍼 (포지션 가치의 2%)
                                        decimal maintBuffer = Math.Abs(position.Notional) * 0.02m;

                                        // [🔴 최종 수정] IsolatedWallet(원금) - 필수증거금 - 손실분(lossCover) - 유지버퍼
                                        // STRKUSDT 사례: 4996.97 - 1692.27 - 3417.08 - 338.45 = -2450 (음수 -> 회수 안함 ✅)
                                        decimal maxCanWithdraw = position.IsolatedWallet - initialMargin - lossCover - maintBuffer;

                                        // 전략적 목표 버퍼 (usageFactor 연동)
                                        decimal bufferMultiplier = distancePercent switch
                                        {
                                            > 25.0m => 0.05m,
                                            > 15.0m => 0.10m,
                                            _ => 0.20m
                                        };
                                        decimal ourTargetBuffer = initialMargin * bufferMultiplier * usageFactor;

                                        // 최종 회수 가능액 (슬랙 85% 유지)
                                        decimal rawRemovable = maxCanWithdraw - ourTargetBuffer;
                                        decimal safeRemovable = Math.Floor((rawRemovable * 0.85m) * 100) / 100m;

                                        // 실행 조건 체크
                                        if (safeRemovable >= recoveryThreshold)
                                        {
                                            if ((DateTime.Now - lastAddDt).TotalSeconds > recoveryCooldown)
                                            {
                                                capGrid.VirtualIsolatedWallet -= safeRemovable;
                                                if (isLong) capGrid.LongMarginAddDt = DateTime.Now;
                                                else capGrid.ShortMarginAddDt = DateTime.Now;

                                                var capRemAmt = safeRemovable;
                                                removeOps.Add(() => RunWithGate(async token => {
                                                    try
                                                    {
                                                        await PositionMarginLongShortAsync(capSymbol, -capRemAmt, capSide);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        capGrid.VirtualIsolatedWallet += capRemAmt; // 실패 시 롤백
                                                        Ob.ui.SetText($"<{capSymbol}> 회수 실패(롤백): {ex.Message}");
                                                    }
                                                }));

                                                Ob.ui.SetText($"<{capSymbol}> ⚡ 증거금 회수 : {capRemAmt:F2} USDT");
                                            }
                                        }
                                    }
                                    if (grid._version == 1)
                                    {
                                        string dbPrefix = isLong ? "Long" : "Short";
                                        decimal quantity = Math.Abs(position.Quantity);
                                        decimal investMoney = position.BreakEvenPrice * quantity;
                                        decimal isolatedMargin = position.IsolatedMargin;

                                        v1DbQueries.Add($@"UPDATE abuy2way 
                                        SET CurrentMoney={position.MarkPrice}, 
                                            {dbPrefix}Qty={quantity}, 
                                            {dbPrefix}Pnl={position.UnrealizedPnl}, 
                                            {dbPrefix}BaseMoney={position.BreakEvenPrice}, 
                                            {dbPrefix}InvestMoney={investMoney}, 
                                            {(isolatedMargin != 0 ? $"{dbPrefix}Margin={isolatedMargin}," : "")} 
                                            updateDt=now() 
                                        WHERE bCoin = '{position.Symbol}' AND status = 0 
                                        ORDER BY StartDt DESC LIMIT 1");
                                    }

                                }
                                catch (Exception ex) { Ob.ui.SetText($"Error in {position.Symbol}: {ex.Message}"); }
                            }

                            // --- [STEP 3] 실행 및 동기화 (Ops 수행) ---
                            // 방어 작업이 있으면 방어부터, 없으면 회수 진행
                            if (addOps.Count > 0) await Task.WhenAll(addOps.Select(start => start()));
                            else if (removeOps.Count > 0) await Task.WhenAll(removeOps.Select(start => start()));
                            
                            // STEP 3 이후 추가
                            var closedV1Updates = new List<string>();
                            foreach (var kv in Ob.bot.hedgeGrids)
                            {
                                var sym = kv.Key;
                                var g = kv.Value;
                                if (g._version != 1) continue;
                                if (syncData.ContainsKey(sym)) continue; // 살아있는 포지션은 이미 처리됨

                                // 포지션이 없음 → 수량/pnl 0으로 초기화
                                closedV1Updates.Add($@"UPDATE abuy2way 
                                    SET LongQty=0, LongPnl=0, ShortQty=0, ShortPnl=0
                                    WHERE bCoin='{sym}' AND status=0
                                    ORDER BY StartDt DESC LIMIT 1");
                            }
                            if (closedV1Updates.Count > 0)
                                _ = Ob.db.ExecuteBatchAsync(closedV1Updates);

                            // 포지션 동기화 및 청산 체크 (syncData 기반)
                            var syncTasks = Ob.bot.hedgeGrids.Values.Select(async grid =>
                            {
                                try
                                {
                                    if (grid._positionId == 0) return;
                                    if (grid._version == 1) return;
                                    syncData.TryGetValue(grid._symbol, out var pos);

                                    // [주의] UpdateFromExchangePosition의 리턴 타입이 void(Task)인지 확인하세요.
                                    await grid.UpdateFromExchangePosition(pos.longPos, pos.shortPos);
                                    await grid.CheckExternalLiquidation(pos.longPos, pos.shortPos);
                                }
                                catch (Exception ex) { Ob.ui?.SetText($"❌ [SYNC-ERROR] {grid._symbol}: {ex.Message}"); }
                            }).ToList();

                            await Task.WhenAll(syncTasks);

                            if (v1DbQueries.Count > 0)
                                _ = Ob.db.ExecuteBatchAsync(v1DbQueries); // fire-and-forget
                        }
                    }
                    catch (Exception ex) { Ob.app._ERROR("SelectPosition Loop", ex); }

                    await Task.Delay(1000, ct); // 1초 대기 후 다음 루프
                }
            }
            catch (Exception ex) { Ob.app._ERROR("SelectPosition Critical", ex); }
        }
        public async void SelectAccount()
        {
            try
            {
                while (true)
                {
                    try
                    {
                        if (Ob.MQ_INIT == 0)
                        {
                            await Ob.app.StartMQ();
                        }

                        Ob.tick++;
                        //if (Ob.tick == 299)
                        //{
                        //    Ob.ui.SetText("TICK : " + Ob.tick.ToString());
                        //}
                        if (Ob.tick > 150)
                        {
                            Ob.tick = 0;


                            var ret = await Ob.client.UsdFuturesApi.Account.GetAccountInfoV3Async();
                            double SwapBNB = 0;
                            double Bnb = 0;
                            if (ret.Success)
                            {
                                Ob.ui.SetText($"Account : {ret.Data.ToString()}");
                                Ob.AvailableBalance = ret.Data.AvailableBalance;
                                Ob.FAccount = ret.Data;
                                Ob.LastAccountUpdateDt = DateTime.Now;

                                foreach (var asset in ret.Data.Assets)
                                {
                                    if (asset.Asset == "BNB")
                                    {
                                        Bnb = (double)asset.WalletBalance;
                                        if (asset.WalletBalance < 0.01m)
                                        {
                                            //견적 요청
                                            var quoteResult = await Ob.client.UsdFuturesApi.Trading.ConvertQuoteRequestAsync(fromAsset: "USDT", toAsset: "BNB", 100m);
                                            if (!quoteResult.Success)
                                            {
                                                Ob.ui.SetText("SWAP BNB Fail <ConvertQuoteRequestAsync> >> " + quoteResult.Error.Message);
                                                break;
                                            }

                                            //Convert 실행
                                            var acceptResult = await Ob.client.UsdFuturesApi.Trading.ConvertAcceptQuoteAsync(quoteResult.Data.QuoteId);
                                            if (!acceptResult.Success)
                                            {
                                                Ob.ui.SetText("SWAP BNB Fail <ConvertAcceptQuoteAsync> >> " + quoteResult.Error.Message);
                                                break;
                                            }
                                            Ob.ui.SetText($"SWAP USDT TO BNB <{quoteResult.Data.FromQuantity}$>");
                                            SwapBNB = (double)quoteResult.Data.FromQuantity;
                                        }
                                    }
                                }
                                string nDate = Ob.app.NowTime().ToString("yyyyMMdd");
                                string Query = $"select * from accountinfo where nDate = '{nDate}' AND Alias = '{Ob.MY_ACCOUNT.Alias}'";
                                DataTable dt = await Ob.db.SelectQueryAsync(Query);

                                double Prev = 0;
                                double TranserFee = 0;
                                double Deposit = 0;
                                double Profit = 0;

                                if (dt.Rows.Count > 0)
                                {
                                    double TmpSwapBNB = (double)dt.Rows[0]["Swap_Bnb"];
                                    SwapBNB += TmpSwapBNB;

                                    TranserFee = (double)dt.Rows[0]["TranserFee"];
                                    Deposit = (double)dt.Rows[0]["Deposit"];
                                    Profit = (double)dt.Rows[0]["Profit"];
                                }

                                string pDate = Ob.app.NowTime().AddDays(-1).ToString("yyyyMMdd");
                                Query = $"select * from accountinfo where nDate = '{pDate}' AND Alias = '{Ob.MY_ACCOUNT.Alias}'";
                                dt = await Ob.db.SelectQueryAsync(Query);



                                if (dt.Rows.Count > 0)
                                {
                                    Prev = (double)dt.Rows[0]["TotalWalletBalance"];
                                    Profit = await Ob.app.DayPayment();
                                }
                                


                                double today1 = (double)ret.Data.TotalWalletBalance;
                                double money1 = (today1 + SwapBNB + TranserFee - Deposit) - Prev;

                                string iQuery = "INSERT INTO AccountInfo (nDate, Alias, Today, TotalInitialMargin, TotalMaintenanceMargin, TotalWalletBalance, TotalUnrealizedProfit, TotalMarginBalance, TotalPositionInitialMargin, TotalOpenOrderInitialMargin, TotalCrossWalletBalance, TotalCrossUnrealizedPnl, AvailableBalance, MaxWithdrawQuantity, Swap_Bnb, Bnb, Grid_Count, update_dt)";
                                iQuery += " VALUES(";
                                iQuery += "'" + nDate + "' ";
                                iQuery += ", '" + Ob.MY_ACCOUNT.Alias + "' ";
                                iQuery += ", " + money1 + " ";
                                iQuery += ", " + ret.Data.TotalInitialMargin + "";
                                iQuery += ", " + ret.Data.TotalMaintenanceMargin + "";
                                iQuery += ", " + ret.Data.TotalWalletBalance + "";
                                iQuery += ", " + ret.Data.TotalUnrealizedProfit + "";
                                iQuery += ", " + ret.Data.TotalMarginBalance + "";
                                iQuery += ", " + ret.Data.TotalPositionInitialMargin + "";
                                iQuery += ", " + ret.Data.TotalOpenOrderInitialMargin + "";
                                iQuery += ", " + ret.Data.TotalCrossWalletBalance + "";
                                iQuery += ", " + ret.Data.TotalCrossUnrealizedPnl + "";
                                iQuery += ", " + ret.Data.AvailableBalance + "";
                                iQuery += ", " + ret.Data.MaxWithdrawQuantity + "";
                                iQuery += ", " + SwapBNB + "";
                                iQuery += ", " + ret.Data.Positions.Length + "";
                                iQuery += ", " + Bnb + "";
                                iQuery += "," + " now()";
                                iQuery += " )";
                                iQuery += " ON DUPLICATE KEY UPDATE ";
                                iQuery += "Today=" + money1 + ", TotalInitialMargin=" + ret.Data.TotalInitialMargin + ", TotalMaintenanceMargin=" + ret.Data.TotalMaintenanceMargin + ", TotalWalletBalance=" + ret.Data.TotalWalletBalance + ", TotalUnrealizedProfit=" + ret.Data.TotalUnrealizedProfit + ", TotalMarginBalance=" + ret.Data.TotalMarginBalance + ", TotalPositionInitialMargin=" + ret.Data.TotalPositionInitialMargin + ", TotalOpenOrderInitialMargin=" + ret.Data.TotalOpenOrderInitialMargin + ", TotalCrossWalletBalance=" + ret.Data.TotalCrossWalletBalance + ", TotalCrossUnrealizedPnl=" + ret.Data.TotalCrossUnrealizedPnl + ", AvailableBalance=" + ret.Data.AvailableBalance + ", Swap_Bnb=" + SwapBNB + ", Bnb=" + Bnb.ToString("#,0.0000") + ", MaxWithdrawQuantity=" + ret.Data.MaxWithdrawQuantity + ", Grid_Count=" + ret.Data.Positions.Length + ", update_dt=now()";

                                await Ob.db.ExecuteQueryAsync(iQuery);
                            }

                            var Payload = new
                            {
                                ID = Ob.MY_ACCOUNT.Alias,
                                Coins = Ob.CoinHT.Keys.ToList()
                            };

                            string body = JsonConvert.SerializeObject(Payload, Formatting.None);
                            var aliveRet = await Ob.app.AppAlive(body.ToString());
                            Ob.ui.SetText($"[Alive] {aliveRet}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Ob.app._ERROR("SelectAccount", ex);
                    }

                    await Task.Delay(1000);
                }
            }
            catch { }
        }
        public async Task<IEnumerable<BinancePositionDetailsUsdt>> GetPositions()
        {
            var result = await Ob.client.UsdFuturesApi.Account.GetPositionInformationAsync();
            if (result.Success)
            {
                decimal ComputeNotional(BinancePositionDetailsUsdt p)
                {
                    return Math.Abs(
                        p.Notional != 0m ? p.Notional :
                        (p.MarkPrice != 0m ? p.MarkPrice * p.Quantity :
                        (p.EntryPrice != 0m ? p.EntryPrice * p.Quantity : 0m))
                    );
                }

                var openPositions = result.Data
                .Where(p => p.Quantity != 0).OrderByDescending(p => ComputeNotional(p))
                .ToList();
                return openPositions;
            }
            else
            {
                Console.WriteLine("Error fetching positions: " + result.Error.Message);
                return null;
            }
        }
    }
}
