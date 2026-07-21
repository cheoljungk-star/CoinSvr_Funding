using Binance.Net.Clients;
using Binance.Net.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoinSvr
{
    public sealed class BinanceStreamManager
    {
        private readonly BinanceSocketClient _socketClient;
        private readonly PortfolioBot _bot;
        private readonly IReadOnlyList<string> _symbols;


        private BinanceSocketClient[] _socketClients = new BinanceSocketClient[5];
        private DateTime[] _lastReceivedByGroup = new DateTime[5];
        private List<string>[] _symbolsByGroup = new List<string>[5];

        private DateTime _lastReceived = DateTime.Now;
        private System.Threading.Timer _healthCheckTimer;
        private ConcurrentDictionary<string, DateTime> _lastBookTickerTime = new();
        private ConcurrentDictionary<string, ConcurrentQueue<Action>> _tickerQueues = new();

        public BinanceStreamManager(PortfolioBot bot, IEnumerable<string> symbols)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _symbols = symbols?.ToList() ?? throw new ArgumentNullException(nameof(symbols));
            _socketClient = new BinanceSocketClient();

            for (int i = 0; i < 5; i++)
            {
                _socketClients[i] = new BinanceSocketClient();
                _lastReceivedByGroup[i] = DateTime.Now;
            }

            _healthCheckTimer = new System.Threading.Timer(_ => CheckHealth(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        }

        public async Task InitializeAsync()
        {
            UI($"✅ [STEP-1] 실시간 소켓 연결 시작...");
            await StartAllStreams(_symbols.ToList());
            UI($"✅ [INIT] 모든 초기화 완료");
        }
        public async Task StartAllStreams(List<string> symbols)
        {
            foreach (var symbol in symbols) InitSymbolQueue(symbol);

            var tasks = new List<Task>();
            int groupSize = (symbols.Count + 4) / 5;

            for (int i = 0; i < 5; i++)
            {
                var group = symbols.Skip(i * groupSize).Take(groupSize).ToList();
                if (group.Any())
                {
                    _symbolsByGroup[i] = group;
                    tasks.Add(StartGroup(_socketClients[i], group, i));
                }
            }
            await Task.WhenAll(tasks);
        }
        private void ProcessKlineUpdate(string symbol, Binance.Net.Interfaces.IBinanceStreamKline data, ConcurrentDictionary<string, ConcurrentQueue<Action>> targetQueueMap, Action<string, Candle> botMethod, int groupId)
        {
            try
            {
                _lastReceivedByGroup[groupId] = DateTime.Now;
                if (data.Final)
                {
                    var bar = new Candle(data.OpenTime, (decimal)data.OpenPrice, (decimal)data.HighPrice, (decimal)data.LowPrice, (decimal)data.ClosePrice, (decimal)data.Volume, (decimal)data.TakerBuyBaseVolume);
                    if (targetQueueMap.TryGetValue(symbol, out var queue))
                    {
                        queue.Enqueue(() => botMethod(symbol, bar));
                    }
                }
            }
            catch { }
        }
        private async Task StartGroup(BinanceSocketClient client, List<string> symbols, int groupId)
        {
            try
            {
                UI($"[GROUP-{groupId}] 소켓 그룹 연결 시작 ({symbols.Count}개)");
                await client.UsdFuturesApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(
                    symbols, x =>
                    {
                        try
                        {
                            _lastReceived = DateTime.Now;
                            _lastReceivedByGroup[groupId] = DateTime.Now;
                            var symbol = x.Data.Symbol;

                            // 봇에 포지션이 있으면 0.1초, 없으면 0.5초 간격으로 처리 (과부하 방지)
                            bool hasPosition = _bot.hedgeGrids.ContainsKey(symbol);
                            var minInterval = hasPosition ? 100 : 500;

                            if (_lastBookTickerTime.TryGetValue(symbol, out var last))
                            {
                                if ((DateTime.Now - last).TotalMilliseconds < minInterval) return;
                            }
                            _lastBookTickerTime[symbol] = DateTime.Now;

                            if (_tickerQueues.TryGetValue(symbol, out var queue))
                            {
                                // 최신 호가만 큐에 남기고 이전 건 버림 (누적 방지)
                                while (queue.TryDequeue(out _)) { }
                                queue.Enqueue(() => _bot.OnBookTicker(symbol, x.Data.BestBidPrice, x.Data.BestBidQuantity, x.Data.BestAskPrice, x.Data.BestAskQuantity));
                            }
                        }
                        catch { }
                    });

                UI($"[GROUP-{groupId}] 연결 성공");
            }
            catch (Exception ex)
            {
                UI($"❌ [GROUP-{groupId}] 연결 오류: {ex.Message}");
            }
        }
        private void CheckHealth()
        {
            for (int i = 0; i < 5; i++)
            {

                var elapsed = (DateTime.Now - _lastReceivedByGroup[i]).TotalSeconds;
                if (elapsed > 120)
                {
                    UI($"⚠️ [WS-TIMEOUT-GROUP-{i}] {elapsed:F0}초간 응답 없음 - 재연결 시도...");
                    _ = ReconnectGroup(i);
                }
            }
        }

        private async Task ReconnectGroup(int groupId)
        {
            try
            {
                try { await _socketClients[groupId].UnsubscribeAllAsync(); } catch { }
                _socketClients[groupId] = new BinanceSocketClient();
                var symbols = _symbolsByGroup[groupId];
                if (symbols != null && symbols.Any()) await StartGroup(_socketClients[groupId], symbols, groupId);
                _lastReceivedByGroup[groupId] = DateTime.Now;
            }
            catch (Exception ex)
            {
                UI($"❌ [RECONNECT-FAIL-{groupId}] {ex.Message}");
                await Task.Delay(3000);
                _ = ReconnectGroup(groupId);
            }
        }
        private void InitSymbolQueue(string symbol)
        {
            _tickerQueues[symbol] = new ConcurrentQueue<Action>();
            _ = Task.Run(() => ProcessTickerQueue(symbol, _tickerQueues[symbol]));
        }
        private async Task ProcessTickerQueue(string symbol, ConcurrentQueue<Action> queue)
        {
            while (true)
            {
                try
                {
                    int skippedCount = 0;
                    Action latestAction = null;
                    // 호가는 쌓아둘 필요 없이 가장 최신 것 하나만 꺼냅니다.
                    while (queue.TryDequeue(out var action))
                    {
                        if (latestAction != null) skippedCount++; // 실행되지 못하고 버려지는 데이터 카운트
                        latestAction = action;
                    }

                    if (latestAction != null)
                    {
                        if (skippedCount > 50) // 한 번에 50개 이상의 가격 데이터를 건너뛰었다면 지연 발생
                        {
                            UI($"⚠️ [TICKER-SKIP] {symbol} 가격 데이터 {skippedCount}개 건너뜀 (처리 속도 저하)");
                        }
                        latestAction();
                    }
                    else
                    {
                        // 스레드를 점유하지 않고 5ms 동안 시스템에 반환합니다.
                        await Task.Delay(5).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    UI($"🔥 [ERR-Ticker] {symbol}: {ex.Message}");
                    await Task.Delay(100); // 에러 발생 시 잠시 대기
                }
            }
        }

        private void UI(string msg) { try { Ob.ui?.SetText(msg); } catch { } }
    }
}