using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace CoinSvr
{
    public sealed class PortfolioBot
    {
        private readonly IExchange ex;
        private readonly RiskConfig rc;
        // ✅ Concurrent 컬렉션 사용
        private readonly ConcurrentDictionary<string, RtSymbolState> states = new();
        private readonly ConcurrentDictionary<string, DateTime> cooldownUntil = new();

        // ✅ 비동기 락 (SemaphoreSlim)
        private readonly SemaphoreSlim _hedgeLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, bool> hedge_entering = new();


        public readonly ConcurrentDictionary<string, HedgeGridManager> hedgeGrids = new();



        public PortfolioBot(IExchange ex, RiskConfig rc)
        {
            this.ex = ex;
            this.rc = rc;

            UI("[INIT] 봇 초기화 완료");
        }
        public void OnBookTicker(string symbol, decimal bid, decimal bidQty, decimal ask, decimal askQty)
        {
            var st = GetOrCreateState(symbol);
            if (st == null) return;
            var mid = (bid + ask) / 2m;
            var ts = DateTime.Now;

            st.LastPrice = mid;
            decimal totalQty = bidQty + askQty;

            var obSnap = new ObSnap(ts, bid, ask, bidQty, askQty, ask - bid);
            st.OnOb(obSnap);

            // 비동기 호출 (Fire and Forget)
            _ = CheckEntrySignal(symbol, st);
        }

        public void OnBookTicker(string symbol, decimal bid, decimal ask) => OnBookTicker(symbol, bid, 0, ask, 0);

        public async Task OnTimerTick()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tasks = hedgeGrids.ToArray().Select(async kvp =>
            {
                var symbol = kvp.Key;
                var grid = kvp.Value;

                try
                {
                    if (states.TryGetValue(symbol, out var st))
                    {
                        // [수정] OnTick의 리턴값(bool)을 여기서 기다려 받습니다.
                        bool stillAlive = await grid.OnTick(st.LastPrice).ConfigureAwait(false);

                        // [수정] 결과가 false(청산 완료 등)라면 이 작업 안에서 즉시 삭제합니다.
                        if (!stillAlive)
                        {
                            hedgeGrids.TryRemove(symbol, out _);
                            UI($"🎯 [GRID-REMOVED] {symbol} 청산 완료");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    // 특정 코인 로직에서 에러가 나도 다른 코인들은 계속 돌아가야 하므로 여기서 예외를 잡습니다.
                    UI($"❌ [GRID-ERROR] {symbol}: {ex.Message}");
                }
            });

            // 2. 150여 개의 모든 작업을 동시에 시작하고, 모두 끝날 때까지 기다립니다.
            await Task.WhenAll(tasks);
            sw.Stop();
            if (sw.ElapsedMilliseconds > 2000) UI($"⚠️ [PERF] OnTimerTick 지연: {sw.ElapsedMilliseconds}ms (대상: {hedgeGrids.Count}개)");
        }

        // ✅ 비동기 Lock 적용 (Semaphore)
        private async Task CheckEntrySignal(string symbol, RtSymbolState st)
        {
            if (symbol == "LABUSDT" || symbol == "BABYUSDT" || symbol == "HUMAUSDT") return;
            bool gridActive = hedge_entering.ContainsKey(symbol) || hedgeGrids.ContainsKey(symbol);
            if (gridActive) return;
            if (cooldownUntil.TryGetValue(symbol, out var until) && DateTime.Now < until) return;
            if (rc.MaxConcurrentEntries < hedgeGrids.Count) return;

            if (st.ShouldTrade)
            {
                bool isLong = st.Direction == "UP";
                bool isShort = st.Direction == "DOWN";
                if (!isLong && !isShort) return;
                if (Ob.AvailableBalance < (decimal)Ob.MY_ACCOUNT.SafeMoney) return;

                // 🔒 Semaphore 대기
                await _hedgeLock.WaitAsync();
                try
                {
                    if (!hedgeGrids.ContainsKey(symbol) && hedge_entering.TryAdd(symbol, true))
                    {
                        await StartHedgeGrid(symbol, st.LastPrice, isLong);
                    }
                }
                finally
                {
                    _hedgeLock.Release();
                }
            }
        }

        private async Task StartHedgeGrid(string symbol, decimal price, bool isLong)
        {
            try
            {
                var grid = new HedgeGridManager(symbol, ex, rc);
                grid.CfgMaxTrade = rc.HedgeGridMaxTotalTrades;
                var state = GetState(symbol);
                if (state == null) return;
                if (await grid.InitializeAsync(price, isLong))
                {
                    hedgeGrids[symbol] = grid;
                    UI($"🎯 시작 >>  [GRID-START] {symbol} {(isLong ? "LONG" : "SHORT")} @ {price:F4}");
                }
            }
            catch (Exception ex)
            {
                UI($"❌ [GRID-START] {symbol}: {ex.Message}");
            }
            finally
            {
                hedge_entering.TryRemove(symbol, out _);
            }
        }

        public async Task RestoreOpenPositionsAsync()
        {
            try
            {
                UI("[RESTORE] DB에서 포지션 복구 시작...");
                int gridRestored = 0;
                int duplicatesCleaned = 0;

                if (rc.UseHedgeGrid)
                {
                    var allRows1 = await Ob.db._dbMaria.HedgeGrid_SelectOpenAsyncV1().ConfigureAwait(false);
                    foreach (var group in allRows1)
                    {
                        try
                        {
                            var grid = await HedgeGridManager.RestoreFromDbAsync(group, ex, rc).ConfigureAwait(false);
                            hedgeGrids[group.symbol] = grid;
                            gridRestored++;
                        }
                        catch (Exception ex2)
                        {
                            UI($"❌ [RESTORE-GRID-ERROR] {group.symbol}: {ex2.Message}");
                        }
                    }

                    var allRows = await Ob.db._dbMaria.HedgeGrid_SelectOpenAsync().ConfigureAwait(false);
                    var groupedRows = allRows.GroupBy(r => r.symbol);

                    foreach (var group in groupedRows)
                    {
                        var sortedRows = group.OrderByDescending(r => r.id).ToList();
                        var newestRow = sortedRows.First();

                        if (sortedRows.Count > 1)
                        {
                            var oldRows = sortedRows.Skip(1);
                            foreach (var oldRow in oldRows)
                            {
                                try
                                {
                                    await Ob.db._dbMaria.HedgeGrid_CloseAsync(oldRow.id, (decimal)oldRow.current_price, DateTime.Now, "DUPLICATE_CLEANUP", 0, 0);
                                    duplicatesCleaned++;
                                }
                                catch { }
                            }
                        }

                        try
                        {
                            var grid = await HedgeGridManager.RestoreFromDbAsync(newestRow, ex, rc).ConfigureAwait(false);
                            hedgeGrids[newestRow.symbol] = grid;
                            gridRestored++;
                        }
                        catch (Exception ex2)
                        {
                            UI($"❌ [RESTORE-GRID-ERROR] {newestRow.symbol}: {ex2.Message}");
                        }
                    }
                }
                UI($"[RESTORE] 완료! (복구: {gridRestored}건, 중복제거: {duplicatesCleaned}건)");
            }
            catch (Exception exc)
            {
                UI($"❌ [RESTORE-ERROR] {exc.Message}");
            }
        }

        public RtSymbolState GetOrCreateState(string symbol)
        {
            return states.GetOrAdd(symbol, _ => new RtSymbolState(symbol));
        }

        public RtSymbolState GetState(string symbol) => GetOrCreateState(symbol);

        private void UI(string msg) { try { Ob.ui?.SetText(msg); } catch { } }
        private void UI_ENTER(string msg) { try { Ob.ui?.SetEnter(msg); } catch { } }
    }
}