using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Futures.Socket;
using CryptoExchange.Net.Objects.Sockets;
using DinaVoip;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Parameters;
using RabbitMQ.Client;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoinSvr
{
    public class Ob
    {
        public static FrmMain ui;
        public static App app = new App();
        
        public static DB_SVC db;
        public static string DB_CONNECTION;
        public static object db_lock = new object();
        public static List<string> db_exec_Queue = new List<string>();
        public static SvcExecDB thread_ExecDB;
        public static select_OrderBook_All OrderBook_All;

        public static BinanceRestClient client;
        public static BinanceSocketClient socketClient;
        public static BinanceSocketClient socketClient_postion;
        public static List<UpdateSubscription> subscriptions = new List<UpdateSubscription>();

        public static string apiKey = "";
        public static string apiSecretKey = "";

        public static string PF = "0";
        public static string SL = "0";

        public static bool IsAccount = false;
        public static bool IsInit = false;
        public static int tick = 750;
        public static int tmr_tick = 2;
        public static int bBuys = 0;

        public static ConcurrentDictionary<string, COIN_OBJECT_> CoinHT = new ConcurrentDictionary<string, COIN_OBJECT_>();

        public static BinanceFuturesUsdtExchangeInfo exInfo = null;

        public static string _listenKey;
        public static Timer _keepAliveTimer;

        public static readonly Dictionary<string, BinanceFuturesStreamPosition> positionCache= new Dictionary<string, BinanceFuturesStreamPosition>();

        public static int MQ_INIT = 1;
        public static ConnectionFactory factory;
        public static RabbitMQ.Client.IConnection connection;
        public static IChannel channel;

        public static string REMOTE_IP = "127.0.0.1";
        public static string ENC_KEY = "xaddhyts_sdfdaedkshdfnshsdiflsho";

        public static Access MY_ACCOUNT;

        
        public static int WindowSize = 500;
        public static double AbsThreshold = 5.0;  // ±500%

        public static double Profit_Ratio = 2.0;

        public static decimal AvailableBalance = 0;

        public static BinanceFuturesAccountInfoV3 FAccount;
        public static DateTime LastAccountUpdateDt { get; set; } = DateTime.MinValue;
        public static PortfolioBot bot;
        //public static List<double> strengthList = new List<double>();
    }
    public class COIN_OBJECT_
    {
        public string coin;

        public select_TRANSACTION transation;

        public DateTime dt;
        public double asks_quantity;
        public double bids_quantity;
        public double quantity;
        public double asks_price;
        public double bids_price;
        public double price;
        public bool bRefresh;
        public int signal = -1;
        public double bbLower_15m;
        public double bbMiddle_15m;
        public double bbUpper_15m;
        public bool isMacdImproving;
        public bool isBBLowerHit;
        public double rsi_15m = 99;
        public List<double> close = new List<double>();
    }
    public class Abuy2Way : IDisposable
    {
        public string BuyId { get; set; }
        public string BDate { get; set; }
        public string BTime { get; set; }
        public string BCoin { get; set; }
        public double CurrentMoney { get; set; }
        public double StartMoney { get; set; }
        public double LongStartMoney { get; set; }
        public double ShortStartMoney { get; set; }
        public double LongInvestMoney { get; set; }
        public double ShortInvestMoney { get; set; }
        public double LongStartInvestMoney { get; set; }
        public double ShortStartInvestMoney { get; set; }
        public double LongExecMoney { get; set; }
        public double ShortExecMoney { get; set; }
        public double LongBaseMoney { get; set; }
        public double ShortBaseMoney { get; set; }
        public string LongStatus { get; set; }
        public double LongQty { get; set; }
        public double LongPnl { get; set; }
        public double LongCloseMoney { get; set; }
        public DateTime LongCloseDt { get; set; }
        public string LongCloseParam { get; set; }
        public string ShortStatus { get; set; }
        public double ShortQty { get; set; }
        public double ShortPnl { get; set; }
        public double ShortCloseMoney { get; set; }
        public DateTime ShortCloseDt { get; set; }
        public string ShortCloseParam { get; set; }
        public string Status { get; set; }
        public DateTime StartDt { get; set; }
        public DateTime LongStartDt { get; set; }
        public DateTime ShortStartDt { get; set; }
        public DateTime CloseDt { get; set; }
        public DateTime RegisterDt { get; set; }
        public string LongParam { get; set; }
        public string ShortParam { get; set; }
        public int LongScalingin { get; set; }
        public int ShortScalingin { get; set; }
        public double LongScalQty { get; set; }
        public double LongScalMoney { get; set; }
        public double ShortScalQty { get; set; }
        public double ShortScalMoney { get; set; }
        public DateTime ScalDt { get; set; }

        public DateTime LongScalDt { get; set; }
        public DateTime ShortScalDt { get; set; }
        public int LongCount { get; set; }
        public int ShortCount { get; set; }
        public object Clone()
        {
            return this.MemberwiseClone();
        }
        public void Dispose()
        {
            // GC가 소멸자를 굳이 호출하지 않도록 Suppress
            GC.SuppressFinalize(this);
        }
        ~Abuy2Way()
        {
            this.Dispose();
        }

    }
    public class NEW_PRICE_
    {
        public string asks;
        public string bids;
    }
    public class TwoHourKline : IBinanceKline
    {
        public DateTime OpenTime { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public DateTime CloseTime { get; set; }
        public decimal Volume { get; set; }
        public decimal QuoteVolume { get; set; }
        public long TradeCount { get; set; }
        public decimal TakerBuyBase { get; set; }
        public decimal TakerBuyQuote { get; set; }
        public decimal TakerBuyBaseVolume { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public decimal TakerBuyQuoteVolume { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        int IBinanceKline.TradeCount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
    public class FourHourIndicators : ICloneable
    {
        // 2시간봉 / 4시간봉에서 넘겨주는 값들을 저장할 프로퍼티들
        public double Ema20 { get; set; }
        public double Ema50 { get; set; }
        public double Adx { get; set; }
        public double AdxPlus { get; set; }
        public double AdxMinus { get; set; }

        public double EmaLong { get; set; }
        public double EmaShort { get; set; }

        public FourHourIndicators() { }

        public FourHourIndicators(
            double ema20,
            double ema50,
            double adx,
            double adxPlus,
            double adxMinus
        )
        {
            EmaShort = ema20;
            EmaLong = ema50;
            Ema20 = ema20;
            Ema50 = ema50;
            Adx = adx;
            AdxPlus = adxPlus;
            AdxMinus = adxMinus;
        }
        public object Clone()
        {
            return new FourHourIndicators
            {
                Ema20 = this.Ema20,
                Ema50 = this.Ema50,
                Adx = this.Adx,
                AdxPlus = this.AdxPlus,
                AdxMinus = this.AdxMinus,
                EmaLong = this.EmaLong,
                EmaShort = this.EmaShort
            };
        }
    }
    public interface IExchange
    {
        // 마켓/스탑 주문
        Task<BinanceFuturesOrder> PlaceMarketOrderAsync(string symbol, Side side, decimal qty, bool reduceOnly = false);
        bool PlaceStopOrderAsync(string symbol, Side side, decimal qty, decimal stopPrice, bool reduceOnly = true);
        bool AmendStopOrderAsync(string symbol, decimal newStopPrice);
        bool CancelAll(string symbol);

        // 계정/종목 메타
        decimal GetUsdtBalance();
        decimal RoundQty(string symbol, decimal qty);
        decimal GetMinQty(string symbol);

        decimal GetLastPrice(string symbol);
        decimal GetMinGuaranteedQtyV2(string symbol, decimal targetUsd, decimal price);

        // 시세(백필용). 실시간은 네가 던져줌.
        Task<IReadOnlyList<Candle>> GetRecent1m(string symbol, int limit);
        IReadOnlyList<Candle> GetRecent10s(string symbol, int buckets);
    }
    public sealed class MockExchange : IExchange
    {
        private readonly ConcurrentDictionary<string, decimal> _lastPx = new();
        private readonly ConcurrentDictionary<string, int> _qtyDecimals = new(); // 심볼별 소숫점 자리
        private readonly Random _rng = new Random();

        public MockExchange(Dictionary<string, decimal>? seedPrices = null, int defaultQtyDecimals = 3)
        {
            if (seedPrices != null)
                foreach (var kv in seedPrices)
                    _lastPx[kv.Key] = kv.Value;

            // 기본 소숫점 자리수(테스트용)
            foreach (var sym in seedPrices ?? new Dictionary<string, decimal>())
                _qtyDecimals[sym.Key] = defaultQtyDecimals;
        }

        // ========== 주문/스톱 관련(무조건 true 반환) ==========
        public async Task<BinanceFuturesOrder> PlaceMarketOrderAsync(string symbol, Side side, decimal qty, bool reduceOnly = false)
        {
            Ob.ui?.SetText($"[ORDER] <{symbol}> MARKET {side} qty={qty} reduceOnly={reduceOnly}");

            try
            {
                OrderSide orderSide;
                PositionSide positionSide;
                string action;

                if (reduceOnly)
                {
                    // 청산: 반대 방향 주문
                    if (side == Side.Sell)
                    {
                        // LONG 포지션 청산
                        orderSide = OrderSide.Sell;
                        positionSide = Binance.Net.Enums.PositionSide.Long;
                        action = "CLOSE LONG";
                    }
                    else
                    {
                        // SHORT 포지션 청산
                        orderSide = OrderSide.Buy;
                        positionSide = Binance.Net.Enums.PositionSide.Short;
                        action = "CLOSE SHORT";
                    }
                }
                else
                {
                    // 진입
                    if (side == Side.Buy)
                    {
                        orderSide = OrderSide.Buy;
                        positionSide = Binance.Net.Enums.PositionSide.Long;
                        action = "OPEN LONG";
                    }
                    else
                    {
                        orderSide = OrderSide.Sell;
                        positionSide = Binance.Net.Enums.PositionSide.Short;
                        action = "OPEN SHORT";
                    }
                }

                Ob.ui.SetText($"<{symbol}>[{action}] {qty}");

                // ✅ reduceOnly 파라미터 없이 호출 (PositionSide로만 구분)
                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol,
                    orderSide,
                    FuturesOrderType.Market,
                    qty,
                    null,
                    positionSide: positionSide
                // reduceOnly 파라미터 아예 안 보냄
                );

                if (result.Success)
                {
                    Ob.ui.SetText($"<{symbol}>[SUCCESS] {result.Data}");
                    return result.Data;
                }
                else
                {
                    Ob.ui.SetText($"<{symbol}>[ERROR] {result.Error.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR($"PlaceMarketOrder<{symbol}>", ex);
                return null;
            }
        }
        public bool PlaceStopOrderAsync(string symbol, Side side, decimal qty, decimal stopPrice, bool reduceOnly = true)
        {
            Ob.ui?.SetText($"[ORDER] <{symbol}> STOP {side} qty={qty} stop={stopPrice:F6} reduceOnly={reduceOnly}");
            return true;
        }
        public bool AmendStopOrderAsync(string symbol, decimal newStopPrice)
        {
            Ob.ui?.SetText($"[ORDER] <{symbol}> AMEND STOP -> {newStopPrice:F6}");
            return true;
        }
        public bool CancelAll(string symbol)
        {
            Ob.ui?.SetText($"[ORDER] <{symbol}> CANCEL ALL");
            return true;
        }
        // ✅ GetLastPrice 구현
        public decimal GetLastPrice(string symbol)
        {
            if (_lastPx.TryGetValue(symbol, out var price))
                return price;
            return 0m; // 또는 기본값
        }

        // 실시간 가격 업데이트 (BookTicker 등에서 호출)
        public void UpdateLastPrice(string symbol, decimal price)
        {
            _lastPx[symbol] = price;
        }
        // ========== 계정/종목 메타 ==========
        public decimal GetUsdtBalance() => 1_000m; // 고정 잔고

        private static string? GetStr(object obj, string prop)
        {
            var pi = obj.GetType().GetProperty(prop);
            return pi?.GetValue(obj) as string;
        }
        private static decimal? GetDec(object obj, params string[] path)
        {
            object? cur = obj;
            foreach (var p in path)
            {
                if (cur == null) return null;
                var pi = cur.GetType().GetProperty(p);
                if (pi == null) return null;
                cur = pi.GetValue(cur);
            }
            if (cur == null) return null;
            try { return (decimal)Convert.ChangeType(cur, typeof(decimal)); } catch { return null; }
        }
        private static int? GetInt(object obj, string prop)
        {
            var pi = obj.GetType().GetProperty(prop);
            if (pi == null) return null;
            var v = pi.GetValue(obj);
            if (v == null) return null;
            try { return (int)Convert.ChangeType(v, typeof(int)); } catch { return null; }
        }
        public static object? FindSymbolInfo(string symbol)
        {
            var info = Ob.exInfo;
            if (info == null) return null;
            // exInfo.Symbols 의 각 항목에서 Name 또는 Symbol 속성을 비교
            foreach (var s in info.Symbols)
            {
                var name = GetStr(s, "Name") ?? GetStr(s, "Symbol");
                if (!string.IsNullOrEmpty(name) &&
                    string.Equals(name, symbol, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }
        private static decimal FloorToStep(decimal value, decimal step)
        {
            if (step <= 0) return value;
            return Math.Floor(value / step) * step;
        }

        // === RoundQty / GetMinQty 교체 구현 ===
        public decimal RoundQty(string symbol, decimal qty)
        {
            var s = FindSymbolInfo(symbol);
            if (s == null)
            {
                // exInfo가 아직 없으면 보수적으로 3자리 반올림
                return Math.Round(qty, 3, MidpointRounding.AwayFromZero);
            }

            // LOT_SIZE 또는 MARKET_LOT_SIZE 의 stepSize / minQty 시도
            var step =
                GetDec(s, "LotSizeFilter", "StepSize") ??
                GetDec(s, "MarketLotSizeFilter", "StepSize");

            // 일부 환경은 정밀도 제공
            if (step == null)
            {
                var qp = GetInt(s, "QuantityPrecision");
                if (qp.HasValue && qp.Value >= 0 && qp.Value <= 18)
                    step = (decimal)Math.Pow(10, -qp.Value);
            }

            // 최종 폴백
            var stepSize = step ?? 0.001m;

            var rounded = FloorToStep(qty, stepSize);

            // minQty 아래면 0으로 반환(주문 불가 신호). 원하면 Math.Max(rounded, minQty)로 클램프해도 됨.
            var minQty =
                GetDec(s, "LotSizeFilter", "MinQuantity") ??
                GetDec(s, "MarketLotSizeFilter", "MinQuantity") ??
                0.001m;

            if (rounded < minQty) return 0m;
            return rounded;
        }

        public decimal GetMinQty(string symbol)
        {
            var s = FindSymbolInfo(symbol);
            if (s == null)
                return 0.001m;

            var minQty =
                GetDec(s, "LotSizeFilter", "MinQuantity") ??
                GetDec(s, "MarketLotSizeFilter", "MinQuantity");

            // 폴백
            if (minQty == null)
            {
                // 정밀도만 있는 경우, 한 스텝을 최소로 가정
                var qp = GetInt(s, "QuantityPrecision");
                if (qp.HasValue && qp.Value >= 0 && qp.Value <= 18)
                    return (decimal)Math.Pow(10, -qp.Value);
                return 0.001m;
            }
            return minQty.Value;
        }
        public decimal GetMinGuaranteedQtyV2(string symbol, decimal targetUsd, decimal price)
        {
            // 1. 종목별 최소 Notional 설정 ($10 / ETH $20) + 안전 버퍼 0.1$
            decimal minNotional = symbol.Contains("ETHUSDT") || symbol.Contains("LINKUSDT") || symbol.Contains("ETCUSDT") ? 20.1m : 10.1m;
            decimal finalTargetUsd = Math.Max(targetUsd, minNotional);

            // 2. 기초 수량 계산 (target / price)
            decimal rawQty = finalTargetUsd / price;

            // 3. 제공해주신 로직을 통한 StepSize 추출
            var s = FindSymbolInfo(symbol); // _ex에 있는 메서드 활용
            decimal stepSize = 0.001m; // 폴백

            if (s != null)
            {
                var step = GetDec(s, "LotSizeFilter", "StepSize") ??
                           GetDec(s, "MarketLotSizeFilter", "StepSize");

                if (step == null)
                {
                    var qp = GetInt(s, "QuantityPrecision");
                    if (qp.HasValue) step = (decimal)Math.Pow(10, -qp.Value);
                }
                stepSize = step ?? 0.001m;
            }

            // 4. [핵심] Floor 대신 Ceiling 처리 (수학적 올림)
            // 예: 0.004038 / 0.001 = 4.038 -> Ceiling(4.038) = 5 -> 5 * 0.001 = 0.005
            decimal roundedQty = Math.Ceiling(rawQty / stepSize) * stepSize;

            // 5. 제공해주신 GetMinQty 로직으로 최종 바닥 확인
            decimal minQty = GetMinQty(symbol);
            return Math.Max(roundedQty, minQty);
        }

        // ========== 시세(백필) ==========
        public async Task<IReadOnlyList<Candle>> GetRecent1m(string symbol, int limit)
        {
            try
            {
                var r = await Ob.client.UsdFuturesApi.ExchangeData
                        .GetKlinesAsync(symbol, KlineInterval.OneMinute, limit: limit).ConfigureAwait(false);

                if (!r.Success)
                {
                    Ob.ui?.SetText($"<{symbol}> GetRecent1m 실패: {r.Error}");
                    return new List<Candle>();
                }

                return r.Data.Select(k => new Candle(
                    k.OpenTime,                   // Timestamp
                    (decimal)k.OpenPrice,         // Open
                    (decimal)k.HighPrice,         // High
                    (decimal)k.LowPrice,          // Low
                    (decimal)k.ClosePrice,        // Close
                    (decimal)k.Volume,            // Volume
                    (decimal)k.TakerBuyBaseVolume // ✅ [수정완료] 인터페이스에 맞는 이름 사용
                )).OrderBy(c => c.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                Ob.ui?.SetText($"<{symbol}> GetRecent1m 예외: {ex.Message}");
                return new List<Candle>();
            }
        }
        public IReadOnlyList<Candle> GetRecent10s(string symbol, int buckets)
        {
            // 실시간 AggTick으로 10초 버킷이 채워지므로 별도 REST 필요 없음
            return new List<Candle>();
        }
    }
    public sealed class RtSymbolState
    {
        public readonly string Symbol;

        public ObSnap? LastOb;
        public decimal LastPrice;

        private DateTime _lastPredTime = DateTime.MinValue;

        public bool ShouldTrade;
        public decimal Confidence;
        public decimal _maxConfidence24h;
        public string Direction;
        public decimal Adx;
        public decimal Open1m;
        public decimal RecentHigh5m { get; set; }  // 최근 5분 최고가
        public decimal RecentLow5m { get; set; }


        public RtSymbolState(string symbol)
        {
            Symbol = symbol;
        }
        public void OnOb(ObSnap ob) => LastOb = ob;

        private void UI(string msg) { try { Ob.ui?.SetText(msg); } catch { } }
    }
    public record ObSnap(
        DateTime TimeUtc,
        decimal BestBid,
        decimal BestAsk,
        decimal BidQty,  // 최우선 호가 수량
        decimal AskQty,
        decimal Spread   // BestAsk - BestBid
    );

    // 캔들 (1분 봉 / 10초 버킷 등 모두 동일 구조로 사용)
    public record Candle(
         DateTime Timestamp,     // OpenTimeUtc -> Timestamp로 이름 통일 (사용 편의상)
         decimal Open,
         decimal High,
         decimal Low,
         decimal Close,
         decimal Volume,
         decimal TakerBuyVol     // ✅ [추가됨] 시장가 매수 체결량 (Buying Pressure)
     );

    public enum TradeSignal
    {
        LONG,
        SHORT,
        HOLD
    }
    public enum PositionAction
    {
        ACCUMULATE, // 누적 진입 유지
        RESET,      // 기존 포지션 정리 후 재진입
        HOLD,        // 변화 없음 (기존 유지)
        NONE        // 변화 없음 (기존 유지)
    }

    [Table("access")]
    public class Access
    {
        [Key]
        [Column("id")]
        [JsonProperty("id")]
        public int Id { get; set; }

        [Column("Alias")]
        [MaxLength(50)]
        [JsonProperty("Alias")]
        public string? Alias { get; set; }

        [Column("MY_ID")]
        [MaxLength(20)]
        [JsonProperty("MY_ID")]
        public string? MyId { get; set; }

        [Column("MY_PWD")]
        [MaxLength(20)]
        [JsonProperty("MY_PWD")]
        public string? MyPwd { get; set; }

        [Column("InvestedMoney")]
        [JsonProperty("InvestedMoney")]
        public double InvestedMoney { get; set; }

        [Column("QueueName")]
        [MaxLength(100)]
        [JsonProperty("QueueName")]
        public string? QueueName { get; set; }

        [Column("Payment_Asset")]
        [MaxLength(50)]
        [JsonProperty("Payment_Asset")]
        public string? PaymentAsset { get; set; }

        [Column("Payment_Address")]
        [MaxLength(500)]
        [JsonProperty("Payment_Address")]
        public string? PaymentAddress { get; set; }

        [Column("Payment_Network")]
        [MaxLength(50)]
        [JsonProperty("Payment_Network")]
        public string? PaymentNetwork { get; set; }

        [Column("Payment_Tag")]
        [MaxLength(500)]
        [JsonProperty("Payment_Tag")]
        public string? PaymentTag { get; set; }

        [Column("Payment_Amount")]
        [JsonProperty("Payment_Amount")]
        public double? PaymentAmount { get; set; }

        [Column("Payment_Ratio")]
        [JsonProperty("Payment_Ratio")]
        public double? PaymentRatio { get; set; }

        [Column("Payment_Value")]
        [JsonProperty("Payment_Value")]
        public double? PaymentValue { get; set; }

        [Column("RunPayment")]
        [JsonProperty("RunPayment")]
        public double? RunPayment { get; set; }

        [Column("RunFee")]
        [JsonProperty("RunFee")]
        public double? RunFee { get; set; }

        [Column("MaxInvest")]
        [JsonProperty("MaxInvest")]
        public double MaxInvest { get; set; }

        [Column("SafeMoney")]
        [JsonProperty("SafeMoney")]
        public double SafeMoney { get; set; }

        [Column("apiKey")]
        [MaxLength(500)]
        [JsonProperty("apiKey")]
        public string? ApiKey { get; set; }

        [Column("apiSecretKey")]
        [MaxLength(500)]
        [JsonProperty("apiSecretKey")]
        public string? ApiSecretKey { get; set; }

        [Column("Register_dt")]
        [JsonProperty("Register_dt")]
        public DateTime? RegisterDt { get; set; }

        [Column("Payment_dt")]
        [JsonProperty("Payment_dt")]
        public DateTime? PaymentDt { get; set; }

        [Column("Conn_dt")]
        [JsonProperty("Conn_dt")]
        public DateTime? ConnDt { get; set; }

        [Column("Use_YN")]
        [MaxLength(1)]
        [JsonProperty("Use_YN")]
        public string? UseYn { get; set; }
    }

}
