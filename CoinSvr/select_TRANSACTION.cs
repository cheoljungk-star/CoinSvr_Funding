using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Futures.Socket;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.SharedApis;
using CryptoExchange.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Mysqlx.Datatypes;
using MySqlX.XDevAPI;
using MySqlX.XDevAPI.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Time;
using Org.BouncyCastle.Asn1.Misc;
using Org.BouncyCastle.Asn1.Mozilla;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC.Multiplier;
using Org.BouncyCastle.Ocsp;
using RabbitMQ.Client;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Eventing.Reader;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp.Unsafe;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CoinSvr
{
    public class select_TRANSACTION
    {
        Hashtable ht;
        private string Coin = "";

        public string now_ask = "0";
        public string now_bid = "0";

        public double buy_money = 0;
        public double buy_All_money = 0;
        public double buy_count = 0;
        public double max_money = double.MinValue;
        public double min_money = double.MaxValue;

        public double max_touch = 0;
        public double min_touch = 0;

        public string buyId = "";
        public string[] buyorderId = new string[3];
        public string[] buyDone = new string[3];
        public string[] bMoneys = new string[3];
        public string sellorderId = "";

        public int main_percent = 1;
        public double last_Sell_money = 0;

        public Hashtable block_ht = new Hashtable();

        public string isRUN = "";
        public double actionMoney = 0;
        public DateTime chk_bdt;

        public DateTime LastDateTime;

        public int coin_step = 0;
        public int try_buy_step = 2;

        public string[] bDateTime2;
        public int buy_mode = 1;
        public DateTime LastSellDateTime = Ob.app.NowTime();

        public List<NEW_PRICE_> Tick_Queue = new List<NEW_PRICE_>();
        public object _Queue_lock = new object();

        private readonly object queueLock = new object();

        public double LossSum = 0;
        public double dNowPrice = 0;
        public double nMaxMoney = 0;
        public double nMinMoney = 0;

        public double nowPNL = 0;
        
        public DateTime LongmarginAddDt = DateTime.MinValue;
        public DateTime ShortmarginAddDt = DateTime.MinValue;

        public string Method = "";

        public string cureentTimeKey = "";
        public string prevTimeKey = "";
        public double currentATR_15m = 0;
        public double currentATR_15m_AVG = 0;
        public double currentATR_THRESHOLD = 0;
        public double currentRSI_15m = 0;
        public double currentRSI_1h = 0;
        public double currentEMA7_1h = 0;
        public double currentEMA21_1h = 0;
        public double currentEMA20_2h = 0;
        public double currentEMA50_2h = 0;
        public double currentBBLOWER_15m = 0;
        public double currentBBMIDDLE_15m = 0;
        public double currentBBUPPER_15m = 0;
        public double currentPRICE_15m = 0;
        public double currentMACDHIST_1h = 0;
        public double currentMACDHIST_15m = 0;
        public double currentADX = 0;
        public double currentADXPlus = 0;
        public double currentADXMinus = 0;
        public FourHourIndicators currentIND1H = new FourHourIndicators();
        public FourHourIndicators currentIND4H = new FourHourIndicators();
        public List<double> currentCLOSE_15m = new List<double>();

        public bool currentvolumeOk = false;
        public bool currentisBullishDivergence = false;
        public bool currentisBearishDivergence = false;

        public DateTime IndicatorTime = DateTime.MinValue;

        private string lastReason = "";

        public bool bbBuy;
        public bool bPosition_Long;
        public bool bPosition_Short;
        
        public bool bReverse_Ing = false;

        public int bPosition_Long_Cnt = 0;
        public int bPosition_Short_Cnt = 0;

        public BinancePositionDetailsUsdt nPosition_Long_1 = null;
        public BinancePositionDetailsUsdt nPosition_Short_1 = null;

        public BinancePositionDetailsUsdt nPosition_Long = null;
        public BinancePositionDetailsUsdt nPosition_Short = null;

        public readonly object nPosition_Long_Lock = new object();
        public readonly object nPosition_Short_Lock = new object();

        // 코인별 마지막 MQ 전송 시간을 저장하는 딕셔너리 (Thread-Safe)
        private static ConcurrentDictionary<string, DateTime> _lastMqSendTime = new ConcurrentDictionary<string, DateTime>();

        public double MarginLong = 0;
        public double MarginShort = 0;

        public DateTime EvtTime = DateTime.MinValue;

        private string name = "g";
        private bool bException = false;
        private bool bException2 = false;

        private double InvestedMoney = 100;
        private double MaxMoney = 1000;

        private (
            double atr_15m,
            double avgAtr_15m,
            double atr_threshold,
            double rsi_15m,
            double bbLower_15m,
            double bbMiddle_15m,
            double bbUpper_15m,
            double price_15m,
            double macdHist_15m,
            double ema7_15m,          // ← 15분봉 EMA (단기)
            double ema21_15m,         // ← 15분봉 EMA (장기)
            double ema7_1h,
            double ema21_1h,
            double rsi_1h,
            double macdHist_1h,
            double ema20_2h,
            double ema50_2h,
            double adx_2h,
            double adx_plus_2h,
            double adx_minus_2h,
            double adx_15m,
            double adx_plus_15m,
            double adx_minus_15m,
            List<double> closes15m,
            List<double> volumes15m,   // ← 15분봉 거래량 리스트
            FourHourIndicators ind1h,
            FourHourIndicators ind4h,
            List<double> closes1h,
            DateTime openTime
        ) PrevIndicator;

        private (
            double atr_15m,
            double avgAtr_15m,
            double atr_threshold,
            double rsi_15m,
            double bbLower_15m,
            double bbMiddle_15m,
            double bbUpper_15m,
            double price_15m,
            double macdHist_15m,
            double ema7_15m,          // ← 15분봉 EMA (단기)
            double ema21_15m,         // ← 15분봉 EMA (장기)
            double ema7_1h,
            double ema21_1h,
            double rsi_1h,
            double macdHist_1h,
            double ema20_2h,
            double ema50_2h,
            double adx_2h,
            double adx_plus_2h,
            double adx_minus_2h,
            double adx_15m,
            double adx_plus_15m,
            double adx_minus_15m,
            List<double> closes15m,
            List<double> volumes15m,   // ← 15분봉 거래량 리스트
            FourHourIndicators ind1h,
            FourHourIndicators ind4h,
            List<double> closes1h,
            DateTime openTime
        ) CurrIndicator;

        private int ThisSignal = -1;
        private bool ThisisMacdImproving = false;
        private bool ThisisisBBLowerHit = false;
        private double rsi_15m = -1;

        public Thread MainThread;

        private BinanceStreamManager BinanceStreamManager;
        private DateTime LastOpenTime = DateTime.MinValue;
        public select_TRANSACTION(string coin)
        {
            this.Coin = coin;
        }
        public async void set_Option(string Run, double money, string status, DateTime buyDt, int signal)
        {
            try
            {
                this.isRUN = Run;
                this.actionMoney = money;
                this.ThisSignal = signal;
                this.MaxMoney = money;

                Ob.ui.SetText("<" + this.Coin + "> 최대 투자금 $" + money.ToString("#,0.##") + "");

                //this.BinanceStreamManager = new BinanceStreamManager(this.Coin);
                //await BinanceStreamManager.InitializeAsync();
                //MainThread = new Thread(new ThreadStart(this.DataProc));
                //MainThread.IsBackground = true;
                //MainThread.Start();
                ////Task t1 = new Task(new Action(DataProc));
                ////t1.Start();

            }
            catch (Exception ex)
            {
                Ob.app._ERROR("set_Option<" + this.Coin + ">", ex);
            }

        }
        public void enQueue(NEW_PRICE_ o)
        {
            try
            {
                lock (this._Queue_lock)
                {
                    this.Tick_Queue.Add(o);
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("enQueue<" + this.Coin + ">", ex);
            }
        }
        DateTime last15mBarTime = DateTime.MinValue;
        DateTime last1hBarTime = DateTime.MinValue;
        DateTime last4hBarTime = DateTime.MinValue;
        public async void DataProc()
        {
            try
            {
                while (true)
                {
                    try
                    {
                        bool shouldSleep = true;
                        lock (this._Queue_lock)
                        {
                            if (this.Tick_Queue.Count != 0)
                            {
                                shouldSleep = false;
                            }
                        }
                        if (shouldSleep)
                        {
                            await Task.Delay(1);
                        }
                        else
                        {
                            NEW_PRICE_ eventData;
                            lock (this._Queue_lock)
                            {
                                eventData = this.Tick_Queue[this.Tick_Queue.Count-1];
                                this.Tick_Queue = new List<NEW_PRICE_>();
                            }
                            TimeSpan ts = Ob.app.NowTime() - this.IndicatorTime;
                            if (ts.TotalSeconds > 1)
                            {
                                this.IndicatorTime = Ob.app.NowTime();
                                this.dNowPrice = double.Parse(eventData.asks);

                                // 1) 새 스냅샷 가져오기
                                var newInd = this.CurrIndicator;
                                bool pNull = false;
                                if(this.PrevIndicator.closes15m == null)
                                {
                                    pNull = true;
                                }
                                if (newInd.openTime != this.LastOpenTime || pNull)
                                {
                                    var oldSnap = this.CurrIndicator;

                                    // 3) CurrIndicator 갱신
                                    this.CurrIndicator = newInd;

                                    // 4) 보관해 둔 값을 PrevIndicator에 할당
                                    this.PrevIndicator = oldSnap;

                                    // 5) 참조 타입 필드만 깊은 복사
                                    if (oldSnap.closes15m != null) this.PrevIndicator.closes15m = new List<double>(oldSnap.closes15m);
                                    if (oldSnap.volumes15m != null) this.PrevIndicator.volumes15m = new List<double>(oldSnap.volumes15m);
                                    if (oldSnap.closes1h != null) this.PrevIndicator.closes1h = new List<double>(oldSnap.closes1h);

                                    // FourHourIndicators가 클래스라면 Clone 메서드 사용
                                    if (oldSnap.ind1h != null) this.PrevIndicator.ind1h = (FourHourIndicators)oldSnap.ind1h.Clone();
                                    if (oldSnap.ind4h != null) this.PrevIndicator.ind4h = (FourHourIndicators)oldSnap.ind4h.Clone();

                                    this.LastOpenTime = newInd.openTime;
                                }else
                                {
                                    this.CurrIndicator = newInd;
                                }

                                //this.currentATR_15m = VAL.atr_15m;
                                //this.currentATR_15m_AVG = VAL.avgAtr_15m;
                                //this.currentATR_THRESHOLD = VAL.atr_threshold;
                                //this.currentRSI_15m = VAL.rsi_15m;
                                //this.currentRSI_1h = VAL.rsi_1h;
                                //this.currentBBLOWER_15m = VAL.bbLower_15m;
                                //this.currentBBMIDDLE_15m = VAL.bbMiddle_15m;
                                //this.currentBBUPPER_15m = VAL.bbUpper_15m;
                                //this.currentPRICE_15m = VAL.price_15m;
                                //this.currentEMA7_1h = VAL.ema7_1h;
                                //this.currentEMA21_1h = VAL.ema21_1h;
                                //this.currentEMA20_2h = VAL.ema20_2h;
                                //this.currentEMA50_2h = VAL.ema50_2h;
                                //this.currentMACDHIST_15m = VAL.macdHist_15m;
                                //this.currentMACDHIST_1h = VAL.macdHist_1h;
                                //this.currentCLOSE_15m = VAL.closes15m;
                                //this.currentADX = VAL.adx;
                                //this.currentADXPlus = VAL.adx_plus;
                                //this.currentADXMinus = VAL.adx_minus;
                                //this.currentIND4H = VAL.ind4h;
                                //this.currentIND1H = VAL.ind1h;
                            }
                            if (this.isRUN == "1")
                            {
                                await this.selectPriceNew(eventData.bids, eventData.asks);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Ob.app._ERROR("DataProc In", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("DataProc", ex);
            }
        }
        public double CalculateRSI(List<double> closePrices, int period = 14)
        {
            try
            {
                if (closePrices == null || closePrices.Count < period + 1)
                    throw new ArgumentException("가격 데이터가 부족합니다.");

                double gain = 0;
                double loss = 0;

                // 초기 평균 구간 계산
                for (int i = 1; i <= period; i++)
                {
                    double change = closePrices[i] - closePrices[i - 1];
                    if (change > 0)
                        gain += change;
                    else
                        loss -= change; // loss는 양수로 누적
                }

                gain /= period;
                loss /= period;

                // RSI 계산 시작
                for (int i = period + 1; i < closePrices.Count; i++)
                {
                    double change = closePrices[i] - closePrices[i - 1];
                    double currentGain = change > 0 ? change : 0;
                    double currentLoss = change < 0 ? -change : 0;

                    gain = (gain * (period - 1) + currentGain) / period;
                    loss = (loss * (period - 1) + currentLoss) / period;
                }

                if (loss == 0)
                    return 100;

                double rs = gain / loss;
                double rsi = 100 - (100 / (1 + rs));
                return rsi;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("CalculateRSI <" + this.Coin + ">", ex);
                return -1;
            }

        }

        public double CalculateATR(List<IBinanceKline> klines, int period)
        {
            try
            {
                List<double> trList = new List<double>();

                for (int i = 1; i < klines.Count; i++)
                {
                    double high = (double)klines[i].HighPrice;
                    double low = (double)klines[i].LowPrice;
                    double prevClose = (double)klines[i - 1].ClosePrice;

                    double tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
                    trList.Add(tr);
                }
                var lastN = trList.Skip(trList.Count - period).Take(period);
                double atr = lastN.Average();

                return atr;
            }
            catch (Exception ex)
            {

                Ob.app._ERROR("CalculateATR <" + this.Coin + ">", ex);
                return -1;
            }

        }
        public static double CalculateEMA(List<double> prices, int period)
        {
            double multiplier = 2.0 / (period + 1);
            double ema = prices.Take(period).Average(); // 초기값: SMA

            for (int i = period; i < prices.Count; i++)
            {
                ema = (prices[i] - ema) * multiplier + ema;
            }

            return ema;
        }
        public static (double Upper, double Middle, double Lower) CalculateBollingerBands(List<double> prices, int period = 20, double multiplier = 2.0)
        {
            var last = prices.Count >= period ? prices.Skip(prices.Count - period).ToList() : prices;

            double sma = last.Average();
            double stdDev = Math.Sqrt(last.Select(p => Math.Pow(p - sma, 2)).Average());

            double upper = sma + multiplier * stdDev;
            double lower = sma - multiplier * stdDev;

            return (Upper: upper, Middle: sma, Lower: lower);
        }

        public static (double Macd, double Signal, double Histogram) CalculateMACD(List<double> prices, int shortPeriod = 12, int longPeriod = 26, int signalPeriod = 9)
        {
            var emaShort = CalculateEMA(prices, shortPeriod);
            var emaLong = CalculateEMA(prices, longPeriod);
            var macdLine = emaShort - emaLong;

            // Signal line 계산용 MACD 히스토리 모의 생성
            var macdHistory = prices.Select((_, i) =>
            {
                if (i < longPeriod) return 0.0;
                var shortEma = CalculateEMA(prices.Take(i + 1).ToList(), shortPeriod);
                var longEma = CalculateEMA(prices.Take(i + 1).ToList(), longPeriod);
                return shortEma - longEma;
            }).ToList();

            var signalLine = CalculateEMA(macdHistory, signalPeriod);
            var histogram = macdLine - signalLine;

            return (Macd: macdLine, Signal: signalLine, Histogram: histogram);
        }
        public (List<double> plusDI, List<double> minusDI, List<double> adx) CalculateADX(List<double> highs, List<double> lows, List<double> closes, int period)
        {
            int len = highs.Count;
            if (lows.Count != len || closes.Count != len || len <= period)
                return (null, null, null);

            var tr = new double[len];
            var plusDM = new double[len];
            var minusDM = new double[len];

            // 1) True Range, +DM, -DM
            for (int i = 1; i < len; i++)
            {
                double highDiff = highs[i] - highs[i - 1];
                double lowDiff = lows[i - 1] - lows[i];

                plusDM[i] = (highDiff > lowDiff && highDiff > 0) ? highDiff : 0;
                minusDM[i] = (lowDiff > highDiff && lowDiff > 0) ? lowDiff : 0;

                double highLow = highs[i] - lows[i];
                double highClose = Math.Abs(highs[i] - closes[i - 1]);
                double lowClose = Math.Abs(lows[i] - closes[i - 1]);
                tr[i] = Math.Max(highLow, Math.Max(highClose, lowClose));
            }

            // 2) Wilder’s smoothing 초기 값 (sum of first 'period' values)
            var smoothedTR = new double[len];
            var smoothedPlusDM = new double[len];
            var smoothedMinusDM = new double[len];

            smoothedTR[period] = tr.Skip(1).Take(period).Sum();
            smoothedPlusDM[period] = plusDM.Skip(1).Take(period).Sum();
            smoothedMinusDM[period] = minusDM.Skip(1).Take(period).Sum();

            // 3) Wilder’s smoothing (이후 값들)
            for (int i = period + 1; i < len; i++)
            {
                smoothedTR[i] = smoothedTR[i - 1] - (smoothedTR[i - 1] / period) + tr[i];
                smoothedPlusDM[i] = smoothedPlusDM[i - 1] - (smoothedPlusDM[i - 1] / period) + plusDM[i];
                smoothedMinusDM[i] = smoothedMinusDM[i - 1] - (smoothedMinusDM[i - 1] / period) + minusDM[i];
            }

            // 4) +DI, -DI 계산
            var plusDI = new List<double>(new double[len]);
            var minusDI = new List<double>(new double[len]);
            for (int i = period; i < len; i++)
            {
                plusDI[i] = smoothedPlusDM[i] / smoothedTR[i] * 100;
                minusDI[i] = smoothedMinusDM[i] / smoothedTR[i] * 100;
            }

            // 5) DX 계산
            var dx = new double[len];
            for (int i = period; i < len; i++)
            {
                double sum = plusDI[i] + minusDI[i];
                dx[i] = (sum == 0) ? 0 : Math.Abs(plusDI[i] - minusDI[i]) / sum * 100;
            }

            // 6) ADX 초기 값: 첫 DX(period 부터 period*2-1 까지 평균)
            var adx = new List<double>(new double[len]);
            adx[period * 2] = dx.Skip(period).Take(period).Average();

            // 7) ADX Wilder’s smoothing
            for (int i = period * 2 + 1; i < len; i++)
            {
                adx[i] = ((adx[i - 1] * (period - 1)) + dx[i]) / period;
            }

            return (plusDI, minusDI, adx);
        }
        public List<IBinanceKline> ToFourHour(List<IBinanceKline> klines1h)
        {
            // 4시간 단위 버킷 키: 0,4,8,12,16,20시의 DateTime
            var groups = klines1h
                .GroupBy(k =>
                {
                    var dt = k.OpenTime;
                    var hourBucket = (dt.Hour / 4) * 4;
                    return new DateTime(
                        dt.Year, dt.Month, dt.Day,
                        hourBucket, 0, 0,
                        dt.Kind
                    );
                })
                .OrderBy(g => g.Key);

            var klines4h = new List<IBinanceKline>();
            foreach (var g in groups)
            {
                var bucket = g.OrderBy(k => k.OpenTime).ToList();
                if (bucket.Count < 4)
                    continue;   // 4개 미만이면 건너뜀

                var first = bucket.First();
                var last = bucket.Last();

                // 집계된 값들
                var high = bucket.Max(k => k.HighPrice);
                var low = bucket.Min(k => k.LowPrice);
                var volume = bucket.Sum(k => k.Volume);

                // IBinanceKline 타입으로 생성 (TwoHourKline와 동일한 구조, 필요하면 FourHourKline 클래스로 따로 만드세요)
                IBinanceKline fourHourKline = new TwoHourKline
                {
                    OpenTime = first.OpenTime,
                    OpenPrice = first.OpenPrice,
                    HighPrice = high,
                    LowPrice = low,
                    ClosePrice = last.ClosePrice,
                    CloseTime = last.CloseTime,
                    Volume = volume,
                };

                klines4h.Add(fourHourKline);
            }

            return klines4h;
        }
        public List<IBinanceKline> ToTwoHour(List<IBinanceKline> klines1h)
        {
            // 2시간 단위 버킷 키: 0,2,4,…22시의 DateTime
            var groups = klines1h
                .GroupBy(k =>
                {
                    var dt = k.OpenTime;
                    var hourBucket = (dt.Hour / 2) * 2;
                    return new DateTime(
                        dt.Year, dt.Month, dt.Day,
                        hourBucket, 0, 0,
                        dt.Kind
                    );
                })
                .OrderBy(g => g.Key);

            var klines2h = new List<IBinanceKline>();
            foreach (var g in groups)
            {
                var bucket = g.OrderBy(k => k.OpenTime).ToList();
                if (bucket.Count < 2) continue;

                var first = bucket.First();
                var last = bucket.Last();

                // 집계된 값들
                var high = bucket.Max(k => k.HighPrice);
                var low = bucket.Min(k => k.LowPrice);
                var volume = bucket.Sum(k => k.Volume);

                // IBinanceKline 타입으로 생성
                IBinanceKline twoHourKline = new TwoHourKline
                {
                    OpenTime = first.OpenTime,
                    OpenPrice = first.OpenPrice,
                    HighPrice = high,
                    LowPrice = low,
                    ClosePrice = last.ClosePrice,
                    CloseTime = last.CloseTime,
                    Volume = volume,
                };
                klines2h.Add(twoHourKline);
            }

            return klines2h;
        }
        public List<double> CalculateRSIList(List<double> closes, int period)
        {
            int n = closes.Count;
            var rsiList = Enumerable.Repeat(double.NaN, n).ToList();
            if (n <= period)
                return rsiList;

            // 1) period 구간 초기 평균 이득/손실 계산
            double gainSum = 0, lossSum = 0;
            for (int i = 1; i <= period; i++)
            {
                double change = closes[i] - closes[i - 1];
                if (change > 0) gainSum += change;
                else lossSum += -change;
            }
            double avgGain = gainSum / period;
            double avgLoss = lossSum / period;

            // 2) period 시점 RSI
            double rs = avgLoss == 0 ? double.PositiveInfinity : avgGain / avgLoss;
            rsiList[period] = avgLoss == 0 ? 100 : 100 - (100 / (1 + rs));

            // 3) 이후 구간을 와일더 방식으로 계산
            for (int i = period + 1; i < n; i++)
            {
                double change = closes[i] - closes[i - 1];
                double gain = change > 0 ? change : 0;
                double loss = change < 0 ? -change : 0;

                avgGain = ((avgGain * (period - 1)) + gain) / period;
                avgLoss = ((avgLoss * (period - 1)) + loss) / period;

                rs = avgLoss == 0 ? double.PositiveInfinity : avgGain / avgLoss;
                rsiList[i] = avgLoss == 0 ? 100 : 100 - (100 / (1 + rs));
            }

            return rsiList;
        }
        
       

        //public async Task<(double atr_15m, double avgAtr_15m, double atr_threshold, double rsi_15m, double bbLower_15m, double bbMiddle_15m, double bbUpper_15m, double price_15m, double macdHist_15m, double ema7_1h, double ema21_1h, double rsi_1h, double macdHist_1h, double ema20_2h, double ema50_2h, double adx, double adx_plus, double adx_minus, List<double> closes15m, FourHourIndicators ind1h, FourHourIndicators ind4h)> GetMTFIndicators()
        //{
        //    try
        //    {
        //        string symbol = this.Coin;

        //        // 1. 15분봉
        //        var klines15mResult = await Ob.client.UsdFuturesApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.FifteenMinutes, limit: 100);
        //        if (!klines15mResult.Success || klines15mResult.Data.Count() < 30)
        //        {
        //            Ob.ui.SetText($"<{symbol}> 15분봉 지표 조회 실패 : {klines15mResult.Error}");
        //            return (-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, new List<double>(), new FourHourIndicators(), new FourHourIndicators());
        //        }

        //        var klines15m = klines15mResult.Data.ToList();
        //        var closes15m = klines15m.Select(k => (double)k.ClosePrice).ToList();
        //        double price_15m = closes15m.Last();

        //        var volumes15m = klines15m.Select(k => (double)k.Volume).ToList();

        //        double avgVol15m = volumes15m
        //        .Skip(Math.Max(0, volumes15m.Count - 20))
        //        .Average();
        //        // 마지막 봉 거래량
        //        double lastVol15m = volumes15m.Last();
        //        // 필터 통과 여부
        //        bool volumeOk = lastVol15m > avgVol15m * 1.2;
        //        this.currentvolumeOk = volumeOk;
        //        List<double> rsiList15m = this.CalculateRSIList(closes15m, 14);

        //        // 강세 다이버전스: 가격 저점이 낮아지는데 RSI 저점은 높아질 때
        //        bool isBullishDivergence =
        //            closes15m[^2] < closes15m[^1] &&
        //            rsiList15m[^2] > rsiList15m[^1];

        //        // 약세 다이버전스: 가격 고점이 높아지는데 RSI 고점은 낮아질 때
        //        bool isBearishDivergence =
        //            closes15m[^2] > closes15m[^1] &&
        //            rsiList15m[^2] < rsiList15m[^1];

        //        this.currentisBullishDivergence = isBullishDivergence;
        //        this.currentisBearishDivergence = isBearishDivergence;

        //        double atr_15m = CalculateATR(klines15m, 14);
        //        double rsi_15m = CalculateRSI(closes15m, 14);
        //        var (bbUpper, bbMiddle, bbLower) = CalculateBollingerBands(closes15m);

        //        // 2. 1시간봉
        //        var klines1hResult = await Ob.client.UsdFuturesApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.OneHour, limit: 100);
        //        if (!klines1hResult.Success || klines1hResult.Data.Count() < 30)
        //        {
        //            Ob.ui.SetText($"<{symbol}> 1시간봉 지표 조회 실패 : {klines1hResult.Error}");
        //            return (-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, new List<double>(), new FourHourIndicators(), new FourHourIndicators());
        //        }

        //        // 2. 4시간봉
        //        var kl4hResult = await Ob.client.UsdFuturesApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.FourHour, limit: 100);
        //        if (!kl4hResult.Success || kl4hResult.Data.Count() < 30)
        //        {
        //            Ob.ui.SetText($"<{symbol}> 1시간봉 지표 조회 실패 : {kl4hResult.Error}");
        //            return (-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, new List<double>(), new FourHourIndicators(), new FourHourIndicators());
        //        }
        //        var kl4h = kl4hResult.Data;

        //        var klines1h = klines1hResult.Data.ToList();
        //        var highs1h = klines1h.Select(k => (double)k.HighPrice).ToList();
        //        var lows1h = klines1h.Select(k => (double)k.LowPrice).ToList();
        //        var closes1h = klines1h.Select(k => (double)k.ClosePrice).ToList();

        //        double ema7_1h = CalculateEMA(closes1h, 7);
        //        double ema21_1h = CalculateEMA(closes1h, 21);
        //        double rsi_1h = CalculateRSI(closes1h, 14);
        //        var (_, _, macdHist_1h) = CalculateMACD(closes1h);
        //        var (_, _, macdHist_15m) = CalculateMACD(closes15m);

        //        double ema20_1h = CalculateEMA(closes1h, 20);
        //        double ema50_1h = CalculateEMA(closes1h, 50);

        //        var (plusDI1h, minusDI1h, adx1h) =
        //            CalculateADX(highs: highs1h, lows: lows1h, closes: closes1h, period: 14);

        //        double adx1hh = adx1h?.Last() ?? -1;
        //        double adxPlus1h = plusDI1h.Last();
        //        double adxMinus1h = minusDI1h.Last();

        //        var ind1h = new FourHourIndicators(
        //           ema20_1h,
        //           ema50_1h,
        //           adx1hh,
        //           adxPlus1h,
        //           adxMinus1h
        //      );

        //        // 3. 2시간봉
        //        var kl2h = ToTwoHour(klines1hResult.Data.ToList());

        //        var highs2h = kl2h.Select(k => (double)k.HighPrice).ToList();
        //        var lows2h = kl2h.Select(k => (double)k.LowPrice).ToList();
        //        var closes2h = kl2h.Select(k => (double)k.ClosePrice).ToList();

        //        double ema20_2h = CalculateEMA(closes2h, 20);
        //        double ema50_2h = CalculateEMA(closes2h, 50);


        //        //4시간봉
        //        var highs4h = kl4h.Select(k => (double)k.HighPrice).ToList();
        //        var lows4h = kl4h.Select(k => (double)k.LowPrice).ToList();
        //        var closes4h = kl4h.Select(k => (double)k.ClosePrice).ToList();

        //        double ema20_4h = CalculateEMA(closes4h, 20);
        //        double ema50_4h = CalculateEMA(closes4h, 50);

        //        var (plusDI4h, minusDI4h, adx4h) =
        //            CalculateADX(highs: highs4h, lows: lows4h, closes: closes4h, period: 14);

        //        double adx4hh = adx4h?.Last() ?? -1;
        //        double adxPlus4h = plusDI4h.Last();
        //        double adxMinus4h = minusDI4h.Last();

        //        // ─── 3-1. 4시간봉 지표 묶기 ───
        //        var ind4h = new FourHourIndicators(
        //            ema20_2h,
        //            ema50_2h,
        //            adx4hh,
        //            adxPlus4h,
        //            adxMinus4h
        //        );

        //        var atrList = new List<double>();
        //        for (int i = 14; i <= klines15m.Count - 14; i++)
        //        {
        //            var sub = klines15m.Skip(i - 14).Take(14).ToList();
        //            atrList.Add(CalculateATR(sub, 14));
        //        }

        //        double avgAtr_15m = atrList.Average();

        //        var sorted = atrList.OrderBy(v => v).ToList();
        //        int N = sorted.Count;
        //        double rank = 0.8 * (N - 1);
        //        int lo = (int)Math.Floor(rank);
        //        int hi = (int)Math.Ceiling(rank);
        //        double weight = rank - lo;

        //        double p80Atr = (hi == lo)
        //            ? sorted[lo]
        //            : sorted[lo] * (1 - weight) + sorted[hi] * weight;

        //        double threshold = Math.Max(2 * avgAtr_15m, p80Atr);

        //        var (plusDI2h, minusDI2h, adx2h) = this.CalculateADX(highs: highs2h, lows: lows2h, closes: closes2h, period: 14);

        //        double adx = -1;
        //        double adxPlus = -1;
        //        double adxMinus = -1;

        //        if (adx2h != null)
        //        {
        //            adx = adx2h.Last();
        //            adxPlus = plusDI2h.Last();
        //            adxMinus = minusDI2h.Last();
        //        }
        //        return (atr_15m, avgAtr_15m, threshold, rsi_15m, bbLower, bbMiddle, bbUpper, price_15m, macdHist_15m, ema7_1h, ema21_1h, rsi_1h, macdHist_1h, ema20_2h, ema50_2h, adx, adxPlus, adxMinus, closes15m, ind1h, ind4h);
        //    }
        //    catch (Exception ex)
        //    {
        //        Ob.app._ERROR($"GetMTFIndicators <{this.Coin}>", ex);
        //        return (-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, new List<double>(), new FourHourIndicators(), new FourHourIndicators());
        //    }
        //}

        public async Task selectPriceNew(string bid, string ask)
        {
            try
            {
                this.dNowPrice = double.Parse(ask);
                this.Method = "4";
                await this.CheckCoin3(Ob.app.NowTime(), this.dNowPrice);
                //var r = await this.CheckCoin(DateTime.Now, this.dNowPrice);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("selectPriceNew", ex);
                Ob.ui.SetText("-----==><" + this.Coin + "> RETURN : " + "");
            }
        }
        public async Task<decimal> CalculateBuyQuantityAsync(string symbol, double entryPrice, double totalUSDT)
        {
            try
            {
                if (Ob.exInfo == null) return 0;

                var symbolInfo = Ob.exInfo.Symbols.FirstOrDefault(s => s.Name.Equals(symbol, StringComparison.OrdinalIgnoreCase));
                if (symbolInfo == null)
                {
                    Ob.ui.SetText("<" + this.Coin + ">[계산-symbolInfo]");
                    return 0;
                }

                BinanceSymbolLotSizeFilter lotSize = (BinanceSymbolLotSizeFilter)symbolInfo.Filters.FirstOrDefault(f => f.FilterType == SymbolFilterType.LotSize);

                if (lotSize == null)
                {
                    Ob.ui.SetText("<" + this.Coin + ">[계산-lotSize]");
                    return 0;
                }

                //var stepSize = decimal.Parse(lotSize.ToString());
                //var minQty = decimal.Parse(lotSize.ToString());


                decimal stepSize = lotSize.StepSize;
                decimal minQty = lotSize.MinQuantity;
                decimal rawQty = (decimal)(totalUSDT / entryPrice);
                decimal roundedQty = FloorToStep(rawQty, stepSize);
                // 3. 최소 수량 조건 확인
                if (roundedQty < minQty)
                {
                    Ob.ui.SetText("<" + this.Coin + ">[계산-최소값]" + minQty.ToString() + "/" + roundedQty);
                    return minQty;
                }
                return roundedQty;
            }
            catch
            {
                return 0;
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
                    Ob.ui.SetText($"<{this.Coin}>[계산-error: symbolInfo not found]");
                    return null;
                }

                // 3) LotSize 필터 꺼내기
                var lotSizeFilter = symbolInfo.Filters.OfType<BinanceSymbolLotSizeFilter>().FirstOrDefault();
                if (lotSizeFilter == null)
                {
                    Ob.ui.SetText($"<{this.Coin}>[계산-error: LotSize filter missing]");
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
                    Ob.ui.SetText($"<{this.Coin}>[계산-최소값 미달: minQty={minQty} / rounded={rounded}]");
                    return null;
                }

                return rounded;
            }
            catch (Exception ex)
            {
                Ob.ui.SetText($"<{this.Coin}>[계산-exception] {ex.Message}");
                return null;
            }
        }

        // stepSize 단위로 버림 반올림
        private decimal FloorToStep(decimal value, decimal step)
        {
            var precision = BitConverter.GetBytes(decimal.GetBits(step)[3])[2]; // step의 소수점 자리수
            var factor = (decimal)Math.Pow(10, precision);
            return Math.Floor(value * factor) / factor;
        }
        public TradeSignal EvaluateMarketAndSuggestAction(double emaShort, double emaLong, double rsi, double atr, double previousAtr)
        {
            bool isTrendingUp = emaShort > emaLong;
            bool isTrendingDown = emaShort < emaLong;
            bool atrSpike = atr > previousAtr * 1.5;

            if (isTrendingUp && rsi > 60 && atrSpike)
            {
                Ob.ui.SetText("<" + this.Coin + ">[추세] 롱 진입 조건 만족: 상승 추세 + 매수 모멘텀 + 변동성 상승");
                return TradeSignal.LONG;
            }
            else if (isTrendingDown && rsi < 40 && atrSpike)
            {
                Ob.ui.SetText("<" + this.Coin + ">[추세] 숏 진입 조건 만족: 하락 추세 + 매도 모멘텀 + 변동성 상승");
                return TradeSignal.SHORT;
            }
            else
            {
                return TradeSignal.HOLD;
            }
        }
        public PositionAction DecidePositionAction(double rsi, double atr, double prevAtr, double emaShort, double emaLong, bool isLong)
        {
            bool atrSpike = atr > prevAtr * 1.5;
            bool emaReversal = isLong ? emaShort < emaLong : emaShort > emaLong;
            bool rsiOversold = rsi < 30;
            bool rsiOverbought = rsi > 70;

            if (atrSpike && emaReversal)
            {
                Ob.ui.SetText("<" + this.Coin + ">[추세-ACTION] 추세 반전 + 변동성 급등 → 포지션 정리 후 재진입");
                return PositionAction.RESET;
            }

            if (isLong && rsiOversold && emaShort > emaLong)
            {
                Ob.ui.SetText("<" + this.Coin + ">[추세-ACTION] 과매도 + 상승 추세 유지 → 누적 매수 계속");
                return PositionAction.ACCUMULATE;
            }

            if (!isLong && rsiOverbought && emaShort < emaLong)
            {
                Ob.ui.SetText("<" + this.Coin + ">[추세-ACTION] 과매수 + 하락 추세 유지 → 누적 숏 유지");
                return PositionAction.HOLD;
            }
            return PositionAction.NONE;
        }
        public PositionAction ShouldReversePositionHybrid(double price, double entryPrice, double emaShort, double emaLong, double prevEmaShort, double prevEmaLong, double rsi, double atr, double prevAtr, bool isLong, double unrealizedPnl, double positionCost)
        {
            // 빠르게 대응: 강한 반전 조건
            bool strongReversal = atr > prevAtr * 1.5 &&
                                   ((isLong && emaShort < emaLong && rsi < 40) ||
                                    (!isLong && emaShort > emaLong && rsi > 60));

            double unrealizedLossPercent = positionCost > 0 ? -unrealizedPnl / positionCost : 0;

            // 신중 대응: 점진적 손실일 경우 → 조건 강화
            bool gradualLossBase = unrealizedLossPercent >= 0.05 && // -5% 손실 이상
                          ((isLong && emaShort < emaLong && rsi < 50) ||
                           (!isLong && emaShort > emaLong && rsi > 50));

            bool emaConfirmed = isLong
                ? (emaShort < emaLong && prevEmaShort < prevEmaLong)
                : (emaShort > emaLong && prevEmaShort > prevEmaLong);

            bool gradualLoss = gradualLossBase && emaConfirmed;

            if (strongReversal)
            {
                Ob.ui.SetText("<" + this.Coin + ">[추세-ACTION] 추세 반전 + 변동성 급등 → 포지션 정리 후 재진입");
            }
            if (gradualLoss)
            {
                Ob.ui.SetText("<" + this.Coin + ">[추세-ACTION] 추세 반전 + 점진적 손실 → 포지션 정리 후 재진입");
            }
            if (strongReversal || gradualLoss)
            {
                return PositionAction.RESET;
            }
            else
            {
                return PositionAction.HOLD;
            }
        }
        public double MaxwellBoltzmann(double x, double a)
        {
            return Math.Sqrt(2 / Math.PI) * Math.Pow(x * x, 1.5) / Math.Pow(a, 3) * Math.Exp(-(x * x) / (2 * a * a));
        }
        public double GetDynamicEntryProbability(double atrRatio, double rsi_1h, double rsi_15m, double macdHist_1h, double price_15m, double bbLower, double bbUpper)
        {
            // 1. 기본 확률: 맥스웰 분포 기반
            double baseProb = MaxwellBoltzmann(atrRatio, 1.0);  // 기본 분포 0~0.4

            // 2. RSI 필터: 중립(45~55)에 가까울수록 진입 확률 낮춤
            double rsiPenalty = Math.Abs(rsi_15m - 50) / 50.0;  // 0~1 → 0이면 50 근처 (위험), 1이면 극단값 (좋음)

            // 3. MACD 보정: 모멘텀 클수록 진입 확률 가중
            double macdBoost = Math.Min(1.0, Math.Abs(macdHist_1h) / 5.0);  // max 1.0

            // 4. BB 돌파 보정: 하단 이하 or 상단 이상일 경우 가산
            bool bbBreak = price_15m < bbLower * 0.995 || price_15m > bbUpper * 1.005;
            double bbBonus = bbBreak ? 0.1 : 0.0;

            // 5. 조합
            double prob = baseProb * rsiPenalty + macdBoost * 0.1 + bbBonus;

            // 6. 상한 제한
            return Math.Min(prob, 0.95);
        }
        public (TradeSignal, string) GetMTFEntrySignal(double emaShort_1h, double emaLong_1h, double rsi_1h, double macdHist_1h, double rsi_15m, double price_15m, double bbLower_15m, double bbMiddle_15m, double bbUpper_15m, double atr_15m, double avgAtr_15m, List<double> closes15m, double adx, bool bADD)
        {
            ////롱 진입 조건
            //bool longTrend = emaShort_1h > emaLong_1h && rsi_1h > 55 && macdHist_1h > 0;
            //bool longTiming = rsi_15m < 35 && price_15m < bbLower_15m && atr_15m > avgAtr_15m;

            ////숏 진입 조건
            //bool shortTrend = emaShort_1h < emaLong_1h && rsi_1h < 45 && macdHist_1h < 0;
            //bool shortTiming = rsi_15m > 65 && price_15m > bbUpper_15m && atr_15m > avgAtr_15m;


            /*맥스웰-볼츠만분포 추가, 이걸 쓰면 진입 숫자가 줄어들고 더 확실한 진입이 가능하다고 함*/
            //double atrRatio = atr_15m / avgAtr_15m;
            //double entryProb = GetDynamicEntryProbability(atrRatio, rsi_1h, rsi_15m, macdHist_1h, price_15m, bbLower_15m, bbUpper_15m);
            //if (new Random().NextDouble() < entryProb)
            //{
            //      Ob.ui.SetText($"[{this.Coin}] 진입 확률 통과 ✅ ({entryProb:0.####})");
            //}
            /*맥스웰-볼츠만분포 추가, 이걸 쓰면 진입 숫자가 줄어들고 더 확실한 진입이 가능하다고 함*/

            // 롱 진입 조건 (완화)
            bool longTrend =
                emaShort_1h > emaLong_1h &&
                rsi_1h > 50 &&
                macdHist_1h >= -3;

            bool longTimingPullback =
                rsi_15m < 40 &&
                price_15m <= bbLower_15m * 1.01 &&
                atr_15m >= avgAtr_15m * 0.95 &&
                adx >= 20 &&
                this.currentvolumeOk &&
                this.currentisBullishDivergence;

            bool longTimingMomentum =
                rsi_15m > 60 &&
                price_15m >= bbMiddle_15m &&
                adx >= 20;


            bool trend4hLong =
                this.currentIND4H.EmaShort > this.currentIND4H.EmaLong &&
                this.currentIND4H.Adx > 25;

            bool trend4hShort =
                this.currentIND4H.EmaShort < this.currentIND4H.EmaLong &&
                this.currentIND4H.Adx > 25;

            bool trend1hLong =
                this.currentIND1H.EmaShort > this.currentIND1H.EmaLong &&
                this.currentIND1H.Adx > 25;

            bool trend1hShort =
                this.currentIND1H.EmaShort < this.currentIND1H.EmaLong &&
                this.currentIND1H.Adx > 25;

            bool longTiming = (longTimingPullback || (longTimingMomentum && trend4hLong && trend1hLong));

            bool shortTrend =
                emaShort_1h < emaLong_1h &&
                rsi_1h < 50 &&
                macdHist_1h <= 3;

            bool shortTimingPullback =
                rsi_15m > 60 &&
                price_15m >= bbUpper_15m * 0.99 &&
                atr_15m >= avgAtr_15m * 0.95 &&
                adx >= 20 &&
                this.currentvolumeOk &&
                this.currentisBearishDivergence;

            bool shortTimingMomentum =
                rsi_15m < 40 &&
                price_15m <= bbMiddle_15m &&
                adx >= 20;


            bool shortTiming = (shortTimingPullback || (shortTimingMomentum && trend4hShort && trend1hShort));

            

            if (bADD)
            {
                longTrend =
                      emaShort_1h > emaLong_1h &&
                      rsi_1h > 53 &&
                      macdHist_1h >= -1;

                longTimingPullback =
                    rsi_15m < 38 &&
                    price_15m <= bbLower_15m * 1 &&
                    atr_15m >= avgAtr_15m * 1 &&
                    adx >= 23 &&
                    this.currentvolumeOk &&
                    this.currentisBullishDivergence;

                longTimingMomentum =
                    rsi_15m > 60 &&
                    price_15m >= bbMiddle_15m &&
                    adx >= 23;

                longTiming = (longTimingPullback || (longTimingMomentum && trend4hLong && trend1hLong));

                shortTrend =
                    emaShort_1h < emaLong_1h &&
                    rsi_1h < 48 &&
                    macdHist_1h <= 1;

                shortTimingPullback =
                   rsi_15m > 62 &&
                   price_15m >= bbUpper_15m * 1 &&
                   atr_15m >= avgAtr_15m * 1 &&
                   adx >= 23 &&
                   this.currentvolumeOk &&
                   this.currentisBearishDivergence;

                shortTimingMomentum =
                    rsi_15m < 40 &&
                    price_15m <= bbMiddle_15m &&
                    adx >= 23;

                shortTiming = (shortTimingPullback || (shortTimingMomentum && trend4hShort && trend1hShort));
            }

            bool isBreakout = IsBollingerBreakout(closes15m, bbUpper_15m, bbLower_15m, price_15m);
            string BBData = "NONE";
            if (isBreakout)
            {
                BBData = price_15m > bbUpper_15m ? "LONG" : "SHORT";
            }
            if (bADD) isBreakout = false;

            string ADDText = bADD ? "1" : "0";

            
            string signalLabel = "NONE";
            string SigValue = "";
            if (longTrend && longTiming)
            {
                SigValue = "SIGNAL";
                signalLabel = "LONG";
            }
            else if (shortTrend && shortTiming)
            {
                SigValue = "SIGNAL";
                signalLabel = "SHORT";
            }
            if (isBreakout)
            {
                SigValue = "BREAKOUT";
            }
            string logMessage = "";
            if (!string.IsNullOrEmpty(SigValue))
            {
                var logEntry = new
                {
                    COIN = this.Coin,
                    SIGVALUE = SigValue,
                    SIGNALLABEL = signalLabel,
                    SCALINGIN = bADD,
                    CONDITIONS = new
                    {
                        LONGTREND = longTrend,
                        LONGPULLBACK = longTimingPullback,
                        LONGMOMENTUM = longTimingMomentum,
                        VOLUMEOK = this.currentvolumeOk,
                        BULLISHDIV = this.currentisBullishDivergence,
                        TREND4HLONG = trend4hLong,
                        TREND1HLONG = trend1hLong,
                        LONGTIMING = longTiming,
                        SHORTTREND = shortTrend,
                        SHORTPULLBACK = shortTimingPullback,
                        SHORTMOMENTUM = shortTimingMomentum,
                        BEARISHDIV = this.currentisBearishDivergence,
                        TREND4HSHORT = trend4hShort,
                        TREND1HSHORT = trend1hShort,
                        SHORTTIMING = shortTiming
                    },
                    VALUES = new
                    {
                        EMASHORT_1H = emaShort_1h,
                        EMALONG_1H = emaLong_1h,
                        RSI1H = rsi_1h,
                        MACD = macdHist_1h,
                        RSI15M = rsi_15m,
                        PRICE = price_15m,
                        BBLOWER_15M = bbLower_15m,
                        BBMIDDLE_15M = bbMiddle_15m,
                        BBUPPER_15M = bbUpper_15m,
                        ATR15M = atr_15m,
                        ATR_THRESHOLD = this.currentATR_THRESHOLD,
                        ADX = adx
                    },
                    BREAKOUT = new
                    {
                        ISBREAKOUT = isBreakout,
                        BBDATA = BBData,
                        PRICE = price_15m,
                        LOWERBB = bbLower_15m,
                        UPPERBB = bbUpper_15m,
                        BBGAP = bbUpper_15m - bbLower_15m
                    },
                    CURRENT_TIME = Ob.app.NowTime().ToString("yyy-MM-dd HH:mm:ss.fff")
                };

                logMessage = JsonConvert.SerializeObject(
                    logEntry,
                    Formatting.None 
                );

                Ob.ui.SetText(logMessage);
            }
            
            if (longTrend && longTiming)
            {
                return (TradeSignal.LONG, logMessage);
            }
            else if (shortTrend && shortTiming)
            {
                return (TradeSignal.SHORT, logMessage);
            }
            else if (isBreakout)
            {
                return ((price_15m > bbUpper_15m ? TradeSignal.LONG : TradeSignal.SHORT), logMessage);
            }
            else
            {
                string reasonCode = "";
                string reasonDetail = "";
                
                if (bADD)
                {
                    if (!longTrend || !longTiming || !trend4hLong || !trend1hLong)
                    {
                        //롱 진입 실패 이유 분석
                        if (!(emaShort_1h > emaLong_1h))
                        {
                            reasonCode += "LONG:EMA↓ ";
                            reasonDetail += $"LONG:EMA↓({emaShort_1h:0.##} < {emaLong_1h:0.##}) ";
                        }
                        if (!(rsi_1h > 53))
                        {
                            reasonCode += "LONG:RSI1H↓ ";
                            reasonDetail += $"LONG:RSI1H↓({rsi_1h:0.##}) ";
                        }
                        if (!(macdHist_1h >= -1))
                        {
                            reasonCode += "LONG:MACD↓ ";
                            reasonDetail += $"LONG:MACD↓({macdHist_1h:0.##}) ";
                        }
                        if (!(rsi_15m < 38))
                        {
                            reasonCode += "LONG:RSI15M↑ ";
                            reasonDetail += $"LONG:RSI15M↑({rsi_15m:0.##}) ";
                        }
                        if (!(price_15m <= bbLower_15m * 1))
                        {
                            reasonCode += "LONG:BB↑ ";
                            reasonDetail += $"LONG:BB↑({price_15m:0.####} > {bbLower_15m * 1.01:0.####}) ";
                        }
                        if (!(atr_15m >= avgAtr_15m * 1))
                        {
                            reasonCode += "LONG:ATR↓ ";
                            reasonDetail += $"LONG:ATR↓({atr_15m:0.####} < {avgAtr_15m * 0.95:0.####}) ";
                        }
                        if (!(adx >= 23))
                        {
                            reasonCode += "LONG:ADX↓ ";
                            reasonDetail += $"LONG:ADX↓({adx:0.####}) ";
                        }
                        if (!(price_15m >= bbMiddle_15m))
                        {
                            reasonCode += "LONG:BBMIDDLE↓ ";
                            reasonDetail += $"LONG:BBMIDDLE↓({price_15m:0.####} < {bbMiddle_15m:0.####}) ";
                        }
                        if (!(this.currentIND4H.EmaShort > this.currentIND4H.EmaLong))
                        {
                            reasonCode += "LONG:EMA4H↓ ";
                            reasonDetail += $"LONG:EMA4H↓({this.currentIND4H.EmaShort:0.##} < {this.currentIND4H.EmaLong:0.##}) ";
                        }
                        if (!(this.currentIND4H.Adx > 25))
                        {
                            reasonCode += "LONG:ADX4H↓ ";
                            reasonDetail += $"LONG:ADX4H↓({this.currentIND4H.Adx:0.##}) ";
                        }
                        if (!(this.currentIND1H.EmaShort > this.currentIND1H.EmaLong))
                        {
                            reasonCode += "LONG:EMA1H↓ ";
                            reasonDetail += $"LONG:EMA1H↓({this.currentIND1H.EmaShort:0.##} < {this.currentIND1H.EmaLong:0.##}) ";
                        }
                        if (!(this.currentIND1H.Adx > 25))
                        {
                            reasonCode += "LONG:ADX1H↓ ";
                            reasonDetail += $"LONG:ADX1H↓({this.currentIND1H.Adx:0.##}) ";
                        }
                    }

                    if (!shortTrend || !shortTiming || !trend4hShort || !trend1hShort)
                    {
                        //숏 진입 실패 이유 분석
                        if (!(emaShort_1h < emaLong_1h))
                        {
                            reasonCode += "SHORT:EMA↑ ";
                            reasonDetail += $"SHORT:EMA↑({emaShort_1h:0.##} > {emaLong_1h:0.##}) ";
                        }
                        if (!(rsi_1h < 48))
                        {
                            reasonCode += "SHORT:RSI1H↑ ";
                            reasonDetail += $"SHORT:RSI1H↑({rsi_1h:0.##}) ";
                        }
                        if (!(macdHist_1h <= 1))
                        {
                            reasonCode += "SHORT:MACD↑ ";
                            reasonDetail += $"SHORT:MACD↑({macdHist_1h:0.##}) ";
                        }
                        if (!(rsi_15m > 62))
                        {
                            reasonCode += "SHORT:RSI15M↓ ";
                            reasonDetail += $"SHORT:RSI15M↓({rsi_15m:0.##}) ";
                        }
                        if (!(price_15m >= bbUpper_15m * 1))
                        {
                            reasonCode += "SHORT:BB↓ ";
                            reasonDetail += $"SHORT:BB↓({price_15m:0.####} < {bbUpper_15m * 0.99:0.####}) ";
                        }
                        if (!(atr_15m >= avgAtr_15m * 1))
                        {
                            reasonCode += "SHORT:ATR↓ ";
                            reasonDetail += $"SHORT:ATR↓({atr_15m:0.####} < {avgAtr_15m * 0.95:0.####}) ";
                        }
                        if (!(adx >= 23))
                        {
                            reasonCode += "SHORT:ADX↓ ";
                            reasonDetail += $"SHORT:ADX↓({adx:0.####}) ";
                        }
                        if (!(price_15m <= bbMiddle_15m))
                        {
                            reasonCode += "SHORT:BBMIDDLE↑ ";
                            reasonDetail += $"SHORT:BBMIDDLE↑({price_15m:0.####} > {bbMiddle_15m:0.####}) ";
                        }
                        if (!(this.currentIND4H.EmaShort < this.currentIND4H.EmaLong))
                        {
                            reasonCode += "SHORT:EMA4H↑ ";
                            reasonDetail += $"SHORT:EMA4H↑({this.currentIND4H.EmaShort:0.##} > {this.currentIND4H.EmaLong:0.##}) ";
                        }
                        if (!(this.currentIND4H.Adx > 25))
                        {
                            reasonCode += "SHORT:ADX4H↓ ";
                            reasonDetail += $"SHORT:ADX4H↓({this.currentIND4H.Adx:0.##}) ";
                        }
                        if (!(this.currentIND1H.EmaShort < this.currentIND1H.EmaLong))
                        {
                            reasonCode += "SHORT:EMA1H↑ ";
                            reasonDetail += $"SHORT:EMA1H↑({this.currentIND1H.EmaShort:0.##} > {this.currentIND1H.EmaLong:0.##}) ";
                        }
                        if (!(this.currentIND1H.Adx > 25))
                        {
                            reasonCode += "SHORT:ADX1H↓ ";
                            reasonDetail += $"SHORT:ADX1H↓({this.currentIND1H.Adx:0.##}) ";
                        }
                    }
                }
                else
                {
                    if (!longTrend || !longTiming || !trend4hLong || !trend1hLong)
                    {
                        //롱 진입 실패 이유 분석
                        if (!(emaShort_1h > emaLong_1h))
                        {
                            reasonCode += "LONG:EMA↓ ";
                            reasonDetail += $"LONG:EMA↓({emaShort_1h:0.##} < {emaLong_1h:0.##}) ";
                        }
                        if (!(rsi_1h > 50))
                        {
                            reasonCode += "LONG:RSI1H↓ ";
                            reasonDetail += $"LONG:RSI1H↓({rsi_1h:0.##}) ";
                        }
                        if (!(macdHist_1h >= -3))
                        {
                            reasonCode += "LONG:MACD↓ ";
                            reasonDetail += $"LONG:MACD↓({macdHist_1h:0.##}) ";
                        }
                        if (!(rsi_15m < 40))
                        {
                            reasonCode += "LONG:RSI15M↑ ";
                            reasonDetail += $"LONG:RSI15M↑({rsi_15m:0.##}) ";
                        }
                        if (!(price_15m <= bbLower_15m * 1.01))
                        {
                            reasonCode += "LONG:BB↑ ";
                            reasonDetail += $"LONG:BB↑({price_15m:0.####} > {bbLower_15m * 1.01:0.####}) ";
                        }
                        if (!(atr_15m >= avgAtr_15m * 0.95))
                        {
                            reasonCode += "LONG:ATR↓ ";
                            reasonDetail += $"LONG:ATR↓({atr_15m:0.####} < {avgAtr_15m * 0.95:0.####}) ";
                        }
                        if (!(adx >= 20))
                        {
                            reasonCode += "LONG:ADX↓ ";
                            reasonDetail += $"LONG:ADX↓({adx:0.####}) ";
                        }
                        if (!(price_15m >= bbMiddle_15m))
                        {
                            reasonCode += "LONG:BBMIDDLE↓ ";
                            reasonDetail += $"LONG:BBMIDDLE↓({price_15m:0.####} < {bbMiddle_15m:0.####}) ";
                        }
                        if (!(this.currentIND4H.EmaShort > this.currentIND4H.EmaLong))
                        {
                            reasonCode += "LONG:EMA4H↓ ";
                            reasonDetail += $"LONG:EMA4H↓({this.currentIND4H.EmaShort:0.##} < {this.currentIND4H.EmaLong:0.##}) ";
                        }
                        if (!(this.currentIND4H.Adx > 20))
                        {
                            reasonCode += "LONG:ADX4H↓ ";
                            reasonDetail += $"LONG:ADX4H↓({this.currentIND4H.Adx:0.##}) ";
                        }
                        if (!(this.currentIND1H.EmaShort > this.currentIND1H.EmaLong))
                        {
                            reasonCode += "LONG:EMA1H↓ ";
                            reasonDetail += $"LONG:EMA1H↓({this.currentIND1H.EmaShort:0.##} < {this.currentIND1H.EmaLong:0.##}) ";
                        }
                        if (!(this.currentIND1H.Adx > 20))
                        {
                            reasonCode += "LONG:ADX1H↓ ";
                            reasonDetail += $"LONG:ADX1H↓({this.currentIND1H.Adx:0.##}) ";
                        }
                    }

                    if (!shortTrend || !shortTiming || !trend4hShort || !trend1hShort)
                    {
                        //숏 진입 실패 이유 분석
                        if (!(emaShort_1h < emaLong_1h))
                        {
                            reasonCode += "SHORT:EMA↑ ";
                            reasonDetail += $"SHORT:EMA↑({emaShort_1h:0.##} > {emaLong_1h:0.##}) ";
                        }
                        if (!(rsi_1h < 50))
                        {
                            reasonCode += "SHORT:RSI1H↑ ";
                            reasonDetail += $"SHORT:RSI1H↑({rsi_1h:0.##}) ";
                        }
                        if (!(macdHist_1h <= 3))
                        {
                            reasonCode += "SHORT:MACD↑ ";
                            reasonDetail += $"SHORT:MACD↑({macdHist_1h:0.##}) ";
                        }
                        if (!(rsi_15m > 60))
                        {
                            reasonCode += "SHORT:RSI15M↓ ";
                            reasonDetail += $"SHORT:RSI15M↓({rsi_15m:0.##}) ";
                        }
                        if (!(price_15m >= bbUpper_15m * 0.99))
                        {
                            reasonCode += "SHORT:BB↓ ";
                            reasonDetail += $"SHORT:BB↓({price_15m:0.####} < {bbUpper_15m * 0.99:0.####}) ";
                        }
                        if (!(atr_15m >= avgAtr_15m * 0.95))
                        {
                            reasonCode += "SHORT:ATR↓ ";
                            reasonDetail += $"SHORT:ATR↓({atr_15m:0.####} < {avgAtr_15m * 0.95:0.####}) ";
                        }
                        if (!(adx >= 20))
                        {
                            reasonCode += "SHORT:ADX↓ ";
                            reasonDetail += $"SHORT:ADX↓({adx:0.####}) ";
                        }
                        if (!(price_15m <= bbMiddle_15m))
                        {
                            reasonCode += "SHORT:BBMIDDLE↑ ";
                            reasonDetail += $"SHORT:BBMIDDLE↑({price_15m:0.####} > {bbMiddle_15m:0.####}) ";
                        }
                        if (!(this.currentIND4H.EmaShort < this.currentIND4H.EmaLong))
                        {
                            reasonCode += "SHORT:EMA4H↑ ";
                            reasonDetail += $"SHORT:EMA4H↑({this.currentIND4H.EmaShort:0.##} > {this.currentIND4H.EmaLong:0.##}) ";
                        }
                        if (!(this.currentIND4H.Adx > 20))
                        {
                            reasonCode += "SHORT:ADX4H↓ ";
                            reasonDetail += $"SHORT:ADX4H↓({this.currentIND4H.Adx:0.##}) ";
                        }
                        if (!(this.currentIND1H.EmaShort < this.currentIND1H.EmaLong))
                        {
                            reasonCode += "SHORT:EMA1H↑ ";
                            reasonDetail += $"SHORT:EMA1H↑({this.currentIND1H.EmaShort:0.##} > {this.currentIND1H.EmaLong:0.##}) ";
                        }
                        if (!(this.currentIND1H.Adx > 20))
                        {
                            reasonCode += "SHORT:ADX1H↓ ";
                            reasonDetail += $"SHORT:ADX1H↓({this.currentIND1H.Adx:0.##}) ";
                        }
                    }
                }

                // 출력 조건
                if (lastReason != reasonCode)
                {
                    string CoinText = $"[{this.Coin}]".PadRight(11);
                    Ob.ui.SetText($"{CoinText}[{"HOLD"}]<{ADDText.ToString()}> → {reasonDetail}ATR_THRESHOLD={this.currentATR_THRESHOLD:0.####}");
                    lastReason = reasonCode;
                }

                return (TradeSignal.HOLD, "");
            }

        }
        public bool IsBollingerBreakout(List<double> closePrices, double bbUpper, double bbLower, double currentPrice)
        {
            // 최근 밴드 폭 계산
            double bbWidth = bbUpper - bbLower;

            // 최근 20개 캔들 기준 평균 밴드폭 계산
            var widths = new List<double>();
            for (int i = 0; i < closePrices.Count - 20; i++)
            {
                var slice = closePrices.Skip(i).Take(20).ToList();
                var (upper, middle, lower) = CalculateBollingerBands(slice);
                widths.Add(upper - lower);
            }

            double avgWidth = widths.Average();

            // 수렴 기준 (밴드폭이 평균의 60% 이하)
            bool isSqueeze = bbWidth < avgWidth * 0.6;

            // 상단 또는 하단 돌파
            bool upperBreakout = currentPrice > bbUpper;
            bool lowerBreakout = currentPrice < bbLower;

            return isSqueeze && (upperBreakout || lowerBreakout);
        }
        public readonly Queue<double> _volWindow = new Queue<double>();
        private readonly object _volLock = new object();
        static double QuantileLinear(double[] sorted, double p)
        {
            int n = sorted.Length;
            if (n == 0) return double.NaN;
            if (p <= 0) return sorted[0];
            if (p >= 1) return sorted[n - 1];

            double pos = p * (n - 1);
            int i = (int)pos;         // 아래 인덱스
            double frac = pos - i;          // 보간 가중치

            // i+1이 범위 밖이면 i만 반환
            return (i + 1 < n)
                ? sorted[i] + frac * (sorted[i + 1] - sorted[i])
                : sorted[i];
        }
        public async Task<(int, string)> GETSIGNAL(
                // “현재” MTF 지표
                (double atr_15m,
                double avgAtr_15m,
                double atr_threshold,
                double rsi_15m,
                double bbLower_15m,
                double bbMiddle_15m,
                double bbUpper_15m,
                double price_15m,
                double macdHist_15m,
                double ema7_15m,
                double ema21_15m,
                double ema7_1h,
                double ema21_1h,
                double rsi_1h,
                double macdHist_1h,
                double ema20_2h,
                double ema50_2h,
                double adx_2h,
                double adx_plus_2h,
                double adx_minus_2h,
                double adx_15m,
                double adx_plus_15m,
                double adx_minus_15m,
                List<double> closes15m,
                List<double> volumes15m,
                FourHourIndicators ind1h,
                FourHourIndicators ind4h,
                List<double> closes1h,
                DateTime openTime
                ) curr,

                // “직전” MTF 지표
                (double atr_15m,
                double avgAtr_15m,
                double atr_threshold,
                double rsi_15m,
                double bbLower_15m,
                double bbMiddle_15m,
                double bbUpper_15m,
                double price_15m,
                double macdHist_15m,
                double ema7_15m,
                double ema21_15m,
                double ema7_1h,
                double ema21_1h,
                double rsi_1h,
                double macdHist_1h,
                double ema20_2h,
                double ema50_2h,
                double adx_2h,
                double adx_plus_2h,
                double adx_minus_2h,
                double adx_15m,
                double adx_plus_15m,
                double adx_minus_15m,
                List<double> closes15m,
                List<double> volumes15m,
                FourHourIndicators ind1h,
                FourHourIndicators ind4h,
                List<double> closes1h,
                DateTime openTime
                ) prev)
        {
            try
            {
                bool passVolFilter = true;

                // ── 거래량 필터링: 절대 임계값 + 슬라이딩 윈도우 IQR ──
                double prevVolFirst = prev.volumes15m.Last();
                double currVolFirst = curr.volumes15m.Last();
                double volChange = (currVolFirst - prevVolFirst) / prevVolFirst;
                if (Math.Abs(volChange) > Ob.AbsThreshold)
                {
                    passVolFilter = false;
                }

                this._volWindow.Enqueue(volChange);
                if (this._volWindow.Count > Ob.WindowSize) this._volWindow.Dequeue();
                var sortedVol = this._volWindow.OrderBy(x => x).ToList();

                double[] snapshot;
                lock (_volLock)
                {
                    this._volWindow.Enqueue(volChange);
                    if (this._volWindow.Count > Ob.WindowSize)
                        this._volWindow.Dequeue();

                    snapshot = this._volWindow.ToArray();
                }
                
                System.Array.Sort(snapshot);

                int vc = snapshot.Length;
                double q1 = QuantileLinear(snapshot, 0.25);
                double q3 = QuantileLinear(snapshot, 0.75);
                double iqr = q3 - q1;
                double lower = q1 - 1.5 * iqr;
                double upper = q3 + 1.5 * iqr;
                if (volChange < lower || volChange > upper)
                {
                    passVolFilter = false;
                }
                // ── 1) 추세 강도 가중치: ADX 20→40 구간 매핑 ──
                double strength = Math.Clamp((curr.adx_2h - 20) / 20.0, 0, 1);
                //Ob.strengthList.Add(strength);

                // ────────────────────────────────────────────────
                // A. “횡보 중 약한 상승” (weakUpSideways) – 중간 완화 버전
                // ────────────────────────────────────────────────

                // 1) 15M BB 폭 계산 (curr vs prev)
                double prevBBWidth = prev.bbUpper_15m - prev.bbLower_15m;
                double currBBWidth = curr.bbUpper_15m - curr.bbLower_15m;

                // ── 2) ATR 밴드 필터 (완화) ──
                bool inAtrBand = Math.Abs(curr.price_15m - prev.price_15m) <= curr.atr_15m * 1.5;

                // ── 3) 다중 타임프레임 확인 ──
                bool up1h = curr.ind1h != null && curr.ind1h.EmaShort > curr.ind1h.EmaLong && curr.ind1h.AdxPlus > curr.ind1h.AdxMinus;
                bool up4h = curr.ind4h != null && curr.ind4h.EmaShort > curr.ind4h.EmaLong && curr.ind4h.AdxPlus > curr.ind4h.AdxMinus;
                bool dn1h = curr.ind1h != null && curr.ind1h.EmaShort < curr.ind1h.EmaLong && curr.ind1h.AdxMinus > curr.ind1h.AdxPlus;
                bool dn4h = curr.ind4h != null && curr.ind4h.EmaShort < curr.ind4h.EmaLong && curr.ind4h.AdxMinus > curr.ind4h.AdxPlus;

                // 원래 0% → 지금 80% → 중간으로 50% 정도(0.5)로 완화
                bool isBBNarrow = currBBWidth <= prevBBWidth * 1.05;

                // 2) 15M 볼린저 중앙선 기울기 비교
                double prevBBMiddle = prev.bbMiddle_15m;
                double currBBMiddle = curr.bbMiddle_15m;
                bool isMiddleUp = currBBMiddle > prevBBMiddle;

                // 3) 15M ADX (2H ADX 사용) – 원래 <15, 지금 <20 → 중간으로 <17
                bool isADXLow = strength > 0.20;

                // 4) DI+ vs DI− 비교 (2H)
                bool isDIPlusAbove = (curr.adx_plus_2h - curr.adx_minus_2h) >= 1.0;

                // 5) 1시간봉 “Higher Low” 근사 계산 (15m 4봉 중 최저값 비교)
                bool isHigherLow = true;

                // 6) 15M 거래량 비교
                bool isVolumeBelowAvg = false;
                if (prev.volumes15m.Count >= 20)
                {
                    double avgVol20 = prev.volumes15m.Skip(prev.volumes15m.Count - 20).Average();
                    double currVol = curr.volumes15m[^1];
                    isVolumeBelowAvg = currVol < avgVol20 * 0.85;
                }

                double score = 0.0;

                if (isBBNarrow) score += 0.15;
                if (isMiddleUp) score += 0.10;
                if (isADXLow) score += 0.20;
                if (isDIPlusAbove) score += 0.10;
                if (inAtrBand) score += 0.10;

                double threshold = 0.65;
                bool weakUpSideways = score >= threshold;
                // ────────────────────────────────────────────────
                // B. “돌파 직전” (imminentBreakout) – 중간 완화 버전
                // ────────────────────────────────────────────────

                // 1) BB 폭 급격 확대 (직전 대비 1.4배) – 원래 1.5 → 중간 1.4
                bool isBBWide = currBBWidth >= prevBBWidth * 1.4;

                // 2) 15M 거래량 급증 (직전 대비 1.4배) – 원래 1.5 → 중간 1.4
                bool isVolumeSpike = false;
                if (curr.volumes15m.Count >= 1 && prev.volumes15m.Count >= 2)
                {
                    double currVol = curr.volumes15m[^1];
                    double prevVol = prev.volumes15m[^2];
                    isVolumeSpike = currVol >= prevVol * 1.4;
                }

                // 3) 15M RSI ≥ 57.5 (원래 60 → 중간 57.5)
                bool isRSIHigh = curr.rsi_15m >= 57.5;

                // 4) 15M MACD 히스토그램 모멘텀 상승 (원래 1.2배 → 중간 1.15)
                double prevMACDHist = prev.macdHist_15m;
                double currMACDHist = curr.macdHist_15m;
                bool isMACDHistUp = (currMACDHist > 0) &&
                                    (currMACDHist >= prevMACDHist * 1.15);

                // 5) 15M 종가가 볼린저 상단 돌파 (0.5% 이탈) – 원래 1% → 중간 0.5%
                bool isCloseAboveBBUpper = curr.price_15m >= curr.bbUpper_15m * 1.005;

                // 6) 15M ADX 급등: (prev < 22.5 && curr ≥ 27.5) – 원래 <20 & ≥25 → 중간 <22.5 & ≥27.5
                double prevADX = prev.adx_2h;
                double currADX = curr.adx_2h;
                bool isADXSpike = (prevADX < 22.5) && (currADX >= 27.5);

                bool imminentBreakout =
                       (isBBWide && isVolumeSpike && (isRSIHigh || isMACDHistUp) && isADXSpike)
                    || isCloseAboveBBUpper;


                // ────────────────────────────────────────────────
                // C. “횡보 중 약한 하락” (weakDownSideways) – 중간 완화 버전
                // ────────────────────────────────────────────────

                // 1) BB 폭 축소(15M)
                bool isBBNarrowShort = isBBNarrow;
                // 2) 15M 볼린저 중앙선 하락
                bool isMiddleDown = currBBMiddle < prevBBMiddle;

                // 3) 15M ADX < 17 (원래 <15 → 중간 <17)
                bool isADXLowShort = isADXLow;

                // 4) 2H DI− > DI+
                bool isDIminusAbove = (curr.adx_minus_2h - curr.adx_plus_2h) >= 1.0;

                // 5) 1H봉 “Lower High” 근사 계산 (15m 4봉 중 최고값 비교)
                bool isLowerHigh = true;
                

                // 6) 15M 거래량 비교 (횡보 중 약한 하락)
                bool isVolumeBelowAvgShort = false;
                //bool isVolumeRisingShort = false;
                if (prev.volumes15m.Count >= 20 && curr.volumes15m.Count >= 2)
                {
                    double avgVol20 = prev.volumes15m
                        .Skip(Math.Max(0, prev.volumes15m.Count - 20))
                        .Average();
                    double currVol = curr.volumes15m[^1];
                    double prevVol = prev.volumes15m[^2];

                    // “현재 거래량 < 과거 20봉 평균의 85%” (원래 80% → 중간 85%)
                    isVolumeBelowAvgShort = currVol < avgVol20 * 0.85;
                    // “현재 거래량 > 직전 ×1.035” (원래 1.05 → 중간 1.035)
                    //isVolumeRisingShort = currVol > prevVol * 1.035;
                }

                double shortScore = 0.0;

                if (isBBNarrowShort) shortScore += 0.15;
                if (isMiddleDown) shortScore += 0.10;
                if (isADXLowShort) shortScore += 0.20;
                if (isDIminusAbove) shortScore += 0.10;
                if (inAtrBand) shortScore += 0.10;

                double shortThreshold = 0.65;
                bool weakDownSideways = shortScore >= shortThreshold;


                // ────────────────────────────────────────────────
                // D. “급락 직전” (imminentCrashDown) – 중간 완화 버전
                // ────────────────────────────────────────────────

                // 1) 15M BB 폭 급격 확대 (직전 대비 1.4배) – 원래 1.5 → 중간 1.4
                bool isBBWideCrash = currBBWidth >= prevBBWidth * 1.4;

                // 2) 15M 거래량 급증 (직전 대비 1.4배) – 원래 1.5 → 중간 1.4
                bool isVolumeSpikeCrash = false;
                if (curr.volumes15m.Count >= 1 && prev.volumes15m.Count >= 2)
                {
                    double currVol = curr.volumes15m[^1];
                    double prevVol = prev.volumes15m[^2];
                    isVolumeSpikeCrash = currVol >= prevVol * 1.4;
                }

                // 3) 15M RSI 급격 하락 or 과매도 (변경 없음)
                bool isRSIFallSharpDown = (prev.rsi_15m - curr.rsi_15m) >= 10;
                bool isRSIOversoldDown = curr.rsi_15m <= 30;

                // 4) 15M MACD 히스토그램 음영 확대 (원래 1.2배 → 중간 1.15배)
                bool isMACDHistCrashDown = (currMACDHist < 0) &&
                                           (currMACDHist <= prevMACDHist * 1.15);

                // 5) 15M ADX 급등: (prev < 22.5 && curr ≥ 27.5) – 원래 <20 & ≥25 → 중간 <22.5 & ≥27.5
                bool isADXSharpUpDown = (prevADX < 22.5) && (currADX >= 27.5);

                // 6) 상위 타임프레임(1H, 4H) 모두 하락 추세
                bool trend1hShortDown = (curr.ind1h.Ema20 < curr.ind1h.Ema50) &&
                                        (curr.ind1h.AdxMinus > curr.ind1h.AdxPlus);
                bool trend4hShortDown = (curr.ind4h.Ema20 < curr.ind4h.Ema50) &&
                                        (curr.ind4h.AdxMinus > curr.ind4h.AdxPlus);

                // 7) 15M 종가가 볼린저 밴드 하단선 아래 (0.5% 이탈) – 원래 1% → 중간 0.5%
                bool isCloseBelowBBLower = curr.price_15m <= curr.bbLower_15m * 0.995;

                // 8) 15M EMA 단기 < EMA 장기 (유지)
                bool isEMAdown15mDown = curr.ema7_15m < curr.ema21_15m;

                bool imminentCrashDown =
                    (
                        isBBWideCrash
                     && isVolumeSpikeCrash
                     && (isRSIFallSharpDown || isRSIOversoldDown)
                     && isMACDHistCrashDown
                     && isADXSharpUpDown
                     && trend1hShortDown && trend4hShortDown
                    )
                    ||
                    (
                        isCloseBelowBBLower
                     && isEMAdown15mDown
                     && isVolumeSpikeCrash
                     && isADXSharpUpDown
                    );

                bool isVolumeRising = false;
                bool isVolumeRisingShort = false;

                // ────────────────────────────────────────────────
                // E. 최종 시그널 반환
                // ────────────────────────────────────────────────

                // 디버깅/로깅용으로 모든 변수들을 JSON 직렬화
                var allVars = new
                {
                    currOpenTime = curr.openTime,
                    prevOpenTime = prev.openTime,
                    // A: weakUpSideways 관련
                    weakUpSideways,
                    isBBNarrow,
                    currBBWidth,
                    prevBBWidth,
                    isMiddleUp,
                    currBBMiddle,
                    prevBBMiddle,
                    isADXLow,
                    isDIPlusAbove,
                    isHigherLow,
                    isVolumeBelowAvg,
                    isVolumeRising,

                    // B: imminentBreakout 관련
                    imminentBreakout,
                    isBBWide,
                    isVolumeSpike,
                    isRSIHigh,
                    isMACDHistUp,
                    isCloseAboveBBUpper,
                    isADXSpike,

                    // C: weakDownSideways 관련
                    weakDownSideways,
                    isBBNarrowShort,
                    isMiddleDown,
                    isADXLowShort,
                    isDIminusAbove,
                    isLowerHigh,
                    isVolumeBelowAvgShort,
                    isVolumeRisingShort,

                    // D: imminentCrashDown 관련
                    imminentCrashDown,
                    isBBWideCrash,
                    isVolumeSpikeCrash,
                    isRSIFallSharpDown,
                    isRSIOversoldDown,
                    isMACDHistCrashDown,
                    isADXSharpUpDown,
                    trend1hShortDown,
                    trend4hShortDown,
                    isCloseBelowBBLower,
                    isEMAdown15mDown,

                    // E: 인자로 전달된 MTF 지표 전체
                    curr,
                    prev
                };

                string json = JsonConvert.SerializeObject(
                    allVars,
                    Formatting.None
                );

                if (weakUpSideways) return (1, json);  // 횡보 중 약한 상승
                if (imminentBreakout) return (2, json);  // 돌파 직전
                if (weakDownSideways) return (3, json);  // 횡보 중 약한 하락
                if (imminentCrashDown) return (4, json);  // 급락 직전

                return (0, json);  // 그 외: 신호 없음
            }
            catch(Exception ex)
            {
                Ob.app._ERROR("GETSIGNAL-<" + this.Coin + ">", ex);
                return (0, "");
            }
        }

        public double MovingAverage(IEnumerable<double> src, int period)
        {
            return src.Skip(src.Count() - period).Average();
        }
        public async Task CalcMargin(BinancePositionDetailsUsdt position)
        {
            try
            {
                if(position.PositionSide == PositionSide.Long)
                {
                    double IsolatedMargin = 0;
                    this.bPosition_Long_Cnt = 0;
                    this.LossSum = (double)position.UnrealizedPnl;
                    this.nowPNL = (double)position.UnrealizedPnl;
                    decimal liquidationPrice = position.LiquidationPrice;
                    decimal markPrice = position.MarkPrice;
                    IsolatedMargin = this.MarginLong;
                    if (position.LiquidationPrice != 0)
                    {
                        decimal distanceFromLiquidation = Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100);

                        if (distanceFromLiquidation <= 2)
                        {
                            decimal marginToAdd = position.IsolatedMargin * 0.40m;
                            Ob.ui.SetText($"<{position.Symbol}:{position.PositionSide.ToString().ToUpper()}> 증거금 추가 : {marginToAdd:F4} USDT");
                            await PositionMarginLongShortAsync(position.Symbol, marginToAdd, position.PositionSide);
                        }
                    }
                    decimal notional = Math.Abs(position.Notional);
                    decimal leverage = position.Leverage;
                    decimal initialMargin = notional / leverage;
                    decimal isolatedMargin = position.IsolatedMargin;

                    decimal removableMargin = isolatedMargin - initialMargin;

                    decimal distancePercent = (liquidationPrice > 0) ? Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100) : 999;

                    if (removableMargin > 0 && distancePercent > 2.0m)
                    {
                        decimal buffer = Math.Max(removableMargin * 0.03m, 1.0m);
                        decimal safeRemovable = removableMargin - buffer;

                        if (safeRemovable > 10)
                        {
                            Ob.ui.SetText($"<{position.Symbol}:{position.PositionSide.ToString().ToUpper()}> 증거금 회수 : {safeRemovable:F4} USDT");
                            await PositionMarginLongShortAsync(position.Symbol, -safeRemovable, position.PositionSide);
                        }
                    }
                    string iQuery = $"update abuy2way set CurrentMoney={position.MarkPrice}, LongQty={Math.Abs(position.Quantity)}, LongPnl={position.UnrealizedPnl}, LongBaseMoney={position.BreakEvenPrice}, LongInvestMoney={position.BreakEvenPrice * Math.Abs(position.Quantity)}, LongMargin={IsolatedMargin} where bCoin = '{position.Symbol}' and status = 0";
                    await Ob.db.ExecuteQueryAsync(iQuery, false);
                }
                else if(position.PositionSide == PositionSide.Short)
                {
                    double IsolatedMargin = 0;
                    this.bPosition_Short_Cnt = 0;
                    this.LossSum = (double)position.UnrealizedPnl;
                    this.nowPNL = (double)position.UnrealizedPnl;
                    decimal liquidationPrice = position.LiquidationPrice;
                    decimal markPrice = position.MarkPrice;
                    IsolatedMargin = this.MarginShort;
                    if (position.LiquidationPrice != 0)
                    {
                        decimal distanceFromLiquidation = Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100);

                        if (distanceFromLiquidation <= 2)
                        {
                            decimal marginToAdd = position.IsolatedMargin * 0.40m;
                            Ob.ui.SetText($"<{position.Symbol}:{position.PositionSide.ToString().ToUpper()}> 증거금 추가 : {marginToAdd:F4} USDT");
                            await PositionMarginLongShortAsync(position.Symbol, marginToAdd, position.PositionSide);
                        }
                    }

                    decimal notional = Math.Abs(position.Notional);
                    decimal leverage = position.Leverage;
                    decimal initialMargin = notional / leverage;
                    decimal isolatedMargin = position.IsolatedMargin;

                    decimal removableMargin = isolatedMargin - initialMargin;

                    decimal distancePercent = (liquidationPrice > 0) ? Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100) : 999;

                    if (removableMargin > 0 && distancePercent > 2.0m)
                    {
                        decimal buffer = Math.Max(removableMargin * 0.03m, 1.0m);
                        decimal safeRemovable = removableMargin - buffer;

                        if (safeRemovable > 10)
                        {
                            Ob.ui.SetText($"<{position.Symbol}:{position.PositionSide.ToString().ToUpper()}> 증거금 회수 : {safeRemovable:F4} USDT");
                            await PositionMarginLongShortAsync(position.Symbol, -safeRemovable, position.PositionSide);
                        }
                    }

                    string iQuery = $"update abuy2way set CurrentMoney={position.MarkPrice}, ShortQty={Math.Abs(position.Quantity)}, ShortPnl={position.UnrealizedPnl}, ShortBaseMoney={position.BreakEvenPrice}, ShortInvestMoney={position.BreakEvenPrice * Math.Abs(position.Quantity)}, ShortMargin={IsolatedMargin} where bCoin = '{position.Symbol}' and status = 0";
                    await Ob.db.ExecuteQueryAsync(iQuery, false);
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("CalcMargin-<" + this.Coin+ ">[" + position.PositionSide.ToString().ToUpper() + "]", ex);
            }
        }
        private DateTime lastLogTime = DateTime.MinValue;
        public async Task CheckCoin3(DateTime date, double price)
        {
            try
            {
                // CheckCoin3 함수 시작 부분 수정
                if (this.bException) return;
                // --- 강력한 Null 및 데이터 개수 체크 추가 ---
                bool isDataMissing = (this.CurrIndicator.closes15m == null || this.CurrIndicator.closes15m.Count < 2 ||
                               this.PrevIndicator.closes15m == null || this.PrevIndicator.closes15m.Count < 2 ||
                               this.CurrIndicator.ind1h == null || this.CurrIndicator.ind4h == null ||
                               this.PrevIndicator.ind1h == null || this.PrevIndicator.ind4h == null);

                if (isDataMissing)
                {
                    // 마지막 로그 기록 후 10분이 지났는지 확인
                    if ((Ob.app.NowTime() - this.lastLogTime).TotalMinutes >= 10)
                    {
                        this.lastLogTime = Ob.app.NowTime();
                        string missingDetail = "";
                        if (this.CurrIndicator.closes15m == null) missingDetail += "Curr-Closes ";
                        if (this.PrevIndicator.closes15m == null) missingDetail += "Prev-Closes ";
                        if (this.CurrIndicator.ind1h == null) missingDetail += "Curr-Ind1h ";
                        if (this.CurrIndicator.ind4h == null) missingDetail += "Curr-Ind4h ";

                        Ob.ui.SetText($"<{this.Coin}> 지표 데이터 부족으로 대기 중... ({missingDetail})");
                    }
                    return;
                }
                // 세부 지표 필터
                bool isMacdImproving = false;
                bool isBBLowerHit = false;

                // 1. 현재 진행 중인 포지션 DB 조회
                string Query = "select * from abuy2way where bCoin = '" + this.Coin + "' and status = 0 ORDER BY bDate DESC, bTime DESC limit 0, 1";
                DataTable dt = await Ob.db.SelectQueryAsync(Query);
                if (dt.Rows.Count == 0) return;

                DataRow dr = dt.Rows[0];
                var buy = await this.ConvBuy(dr);
                if (buy == null) return;

                // 2. 현재 및 직전 지표 구조화 (GETSIGNAL 인자 활용)
                var curr = this.CurrIndicator;
                var prev = this.PrevIndicator;
                (int signal, string json) = await this.GETSIGNAL(curr, prev);

                // 기본 변수 계산
                double currentPrice = price;
                double Ratio = ((buy.LongBaseMoney - currentPrice) / buy.LongBaseMoney) * 100; // 평단 대비 하락률
                double pnlLongPct = buy.LongPnl / buy.LongInvestMoney * 100; // 현재 수익률
                TimeSpan tsLastScal = Ob.app.NowTime() - buy.LongScalDt; // 마지막 추매 후 경과 시간
                bool bAvailable = true;

                // 세부 지표 필터
                isMacdImproving = curr.macdHist_15m > prev.macdHist_15m; // 하락 에너지 감소 확인
                isBBLowerHit = curr.price_15m <= curr.bbLower_15m; // 볼밴 하단 터치 여부

                // ---------------------------------------------------------
                // A. 익절 및 탈출 로직 (Exit Strategy)
                // ---------------------------------------------------------
                // 물타기 진행 시 목표 수익률을 낮게 잡아(0.7%) 탈출을 최우선으로 함
                double targetProfit = (buy.LongScalingin > 0) ? 0.7 : Ob.Profit_Ratio;

                if (buy.LongStatus == "0" && pnlLongPct >= targetProfit)
                {
                    Ob.ui.SetText($"<{this.Coin}> [탈출성공] 수익률:{pnlLongPct:F2}%, 추매회수:{buy.LongScalingin}");
                    var (ret1, ret2) = await ClosePisition(buy, currentPrice, 1, 0, json, buy.LongPnl, 0, true, date);
                    if (ret1 != null)
                    {
                        await this.InUpData(buy, false);
                        return;
                    }
                }
                // ---------------------------------------------------------
                // B. 5단계 분할 추매 (3% -> 4% -> 5% -> 6% -> 7%)
                // ---------------------------------------------------------
                if (buy.LongStatus == "0" && bAvailable && Ratio > 5.0)
                {
                    double additionalQty = 0;
                    string stepLabel = "";

                    if (buy.LongScalingin == 0 && tsLastScal.TotalHours >= 24 && Ratio > 5.0 && (signal == 1 || signal == 2))
                    {
                        additionalQty = buy.LongQty * 0.03;
                        stepLabel = "1차(24h/3%/signal)";
                    }
                    else if (buy.LongScalingin == 1 && tsLastScal.TotalHours >= 48 && Ratio > 10.0 && isMacdImproving)
                    {
                        additionalQty = buy.LongQty * 0.04;
                        stepLabel = "2차(48h/4%/MACD)";
                    }
                    else if (buy.LongScalingin == 2 && tsLastScal.TotalHours >= 72 && Ratio > 15.0 && curr.rsi_15m < 40)
                    {
                        additionalQty = buy.LongQty * 0.05;
                        stepLabel = "3차(72h/5%/RSI)";
                    }
                    else if (buy.LongScalingin == 3 && tsLastScal.TotalHours >= 96 && Ratio > 20.0 && isBBLowerHit)
                    {
                        additionalQty = buy.LongQty * 0.06;
                        stepLabel = "4차(96h/6%/BB하단)";
                    }
                    else if (buy.LongScalingin == 4 && tsLastScal.TotalHours >= 120 && Ratio > 25.0 && curr.rsi_15m < 35 && isBBLowerHit)
                    {
                        additionalQty = buy.LongQty * 0.07;
                        stepLabel = "5차(120h/7%/복합)";
                    }

                    if (additionalQty > 0)
                    {
                        if (additionalQty * currentPrice < 10.1)
                            additionalQty = 10.1 / currentPrice;

                        var filteredQty = await this.CalculateQuantityAsync(this.Coin, (decimal)additionalQty);
                        if (filteredQty <= 0) return;

                        Ob.ui.SetText($"<{this.Coin}> [{stepLabel}] 실행 >> 하락률:{Ratio:F1}%, 수량:{filteredQty}개");

                        var result = await this.PlaceFuturesOrder_LONG_HEDGE(this.Coin, filteredQty ?? 0, 0, false);
                        if (result != null)
                        {
                            await Task.Delay(1000);
                            var positions = await this.GetPositions();
                            var longUnit = positions.FirstOrDefault(p => p.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase) && p.Quantity > 0);

                            if (longUnit != null)
                            {
                                buy.LongInvestMoney = (double)Math.Abs(longUnit.Quantity) * (double)longUnit.BreakEvenPrice;
                                buy.LongQty = (double)Math.Abs(longUnit.Quantity);
                                buy.LongScalingin += 1;
                                buy.LongScalDt = Ob.app.NowTime();
                                buy.ScalDt = Ob.app.NowTime();

                                await this.InUpData(buy, false);
                            }
                        }
                    }
                }

                // 시그널 상태 업데이트 (UI 반영용)
                if (this.ThisSignal != signal)
                {
                    this.ThisSignal = signal;
                    string uQuery = $"update acoin set NSignal = {this.ThisSignal} where coin = '{this.Coin}'";
                    await Ob.db.ExecuteQueryAsync(uQuery, false);
                }
                if (this.ThisSignal != signal || (this.ThisisisBBLowerHit != isBBLowerHit) || (this.ThisisMacdImproving != isMacdImproving) || (this.rsi_15m != curr.rsi_15m))
                {
                    this.ThisSignal = signal;
                    this.rsi_15m = curr.rsi_15m;
                    this.ThisisMacdImproving = isMacdImproving;
                    this.ThisisisBBLowerHit = isBBLowerHit;
                    string uQuery = $"update acoin set NSignal = {this.ThisSignal} where coin = '{this.Coin}'";
                    await Ob.db.ExecuteQueryAsync(uQuery, false);

                    try
                    {
                        if (Ob.MQ_INIT == 1)
                        {
                            //// 1. 현재 시간 확인
                            //DateTime now = DateTime.Now;

                            //// 2. 해당 코인의 마지막 전송 기록이 있는지 확인
                            //// 기록이 있고, 1분(60초)이 지나지 않았다면 전송하지 않고 스킵(return)
                            //if (_lastMqSendTime.TryGetValue(this.Coin, out DateTime lastTime))
                            //{
                            //    if ((now - lastTime).TotalSeconds < 300) // 1분 미만이면
                            //    {
                            //        return; // 전송 안 함
                            //    }
                            //}

                            //// 3. 전송 조건 충족 시: 마지막 전송 시간 갱신 (덮어쓰기)
                            //_lastMqSendTime[this.Coin] = now;

                            //// --- 기존 전송 로직 ---
                            //Dictionary<string, object> dictData = new Dictionary<string, object>();
                            //dictData.Add("symbol", this.Coin);
                            //dictData.Add("signal", signal);
                            //dictData.Add("bbLower_15m", this.CurrIndicator.bbLower_15m);
                            //dictData.Add("bbMiddle_15m", this.CurrIndicator.bbMiddle_15m);
                            //dictData.Add("bbUpper_15m", this.CurrIndicator.bbUpper_15m);
                            //dictData.Add("close", this.CurrIndicator.closes1h);
                            //dictData.Add("event_dt", Ob.app.GetNowTime("DT"));
                            //dictData.Add("isMacdImproving", isMacdImproving);
                            //dictData.Add("isBBLowerHit", isBBLowerHit);
                            //dictData.Add("rsi_15m", rsi_15m);

                            //string sendData = Ob.app.AES_Encrypt(JsonConvert.SerializeObject(dictData, Formatting.Indented));
                            //byte[] body = Encoding.UTF8.GetBytes(sendData);

                            //// 비동기 전송 (CancellationToken이 없다면 default 사용 추천)
                            //Ob.channel.BasicPublishAsync(
                            //    exchange: Ob.MY_ACCOUNT.QueueName,
                            //    routingKey: "",
                            //    body: body,
                            //    mandatory: false,
                            //    cancellationToken: default
                            //);
                        }
                    }
                    catch (Exception ex)
                    {
                        Ob.MQ_INIT = 0;
                        Ob.app._ERROR("CheckCoin2-MQ", ex);
                    }
                }

                buy.Dispose();
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("CheckCoin3-LongRecovery", ex);
            }
        }
        //public async Task CheckCoin3(DateTime date, double price)
        //{
        //    try
        //    {
        //        if (this.bException) return;
        //        if (this.PrevIndicator.closes15m == null) return;

        //        //if(this.nPosition_Long != null)
        //        //{
        //        //    if(this.nPosition_Long_1 == null)
        //        //    {
        //        //        this.nPosition_Long_1 = this.nPosition_Long;
        //        //        BinancePositionDetailsUsdt snapshot;
        //        //        lock (this.nPosition_Long_Lock)
        //        //        {
        //        //            snapshot = CloneHelper.DeepClone(this.nPosition_Long);
        //        //        }
        //        //        await this.CalcMargin(snapshot);
        //        //    }
        //        //    else
        //        //    {
        //        //        //Ob.ui.SetText("<" + this.Coin + "> N0L : " + this.nPosition_Long.UpdateTime + ", P : " + this.nPosition_Long_1.UpdateTime);

        //        //        //if (this.nPosition_Long.UpdateTime != this.nPosition_Long_1.UpdateTime)
        //        //        //{
        //        //            this.nPosition_Long_1 = this.nPosition_Long;
        //        //            BinancePositionDetailsUsdt snapshot;
        //        //            lock (this.nPosition_Long_Lock)
        //        //            {
        //        //                snapshot = CloneHelper.DeepClone(this.nPosition_Long);
        //        //            }
        //        //            await this.CalcMargin(snapshot);
        //        //        //}
        //        //    }
        //        //}
        //        //if (this.nPosition_Short != null)
        //        //{
        //        //    if (this.nPosition_Short_1 == null)
        //        //    {
        //        //        this.nPosition_Short_1 = this.nPosition_Short;
        //        //        BinancePositionDetailsUsdt snapshot;
        //        //        lock (this.nPosition_Short_Lock)
        //        //        {
        //        //            snapshot = CloneHelper.DeepClone(this.nPosition_Short);
        //        //        }
        //        //        await this.CalcMargin(snapshot);
        //        //    }
        //        //    else
        //        //    {
        //        //        //Ob.ui.SetText("<" + this.Coin + "> N0S : " + this.nPosition_Short.UpdateTime + ", P : " + this.nPosition_Short_1.UpdateTime);

        //        //        //if (this.nPosition_Short.UpdateTime != this.nPosition_Short_1.UpdateTime)
        //        //        //{
        //        //            this.nPosition_Short_1 = this.nPosition_Short;
        //        //            BinancePositionDetailsUsdt snapshot;
        //        //            lock (this.nPosition_Short_Lock)
        //        //            {
        //        //                snapshot = CloneHelper.DeepClone(this.nPosition_Short);
        //        //            }
        //        //            await this.CalcMargin(snapshot);
        //        //        //}
        //        //    }
        //        //}
        //        string Query = "select * from abuy2way where bCoin = '" + this.Coin + "' and status = 0 ORDER BY bDate DESC, bTime DESC limit 0, 1";
        //        DataTable dt = await Ob.db.SelectQueryAsync(Query);
        //        (int signal, string json) = await this.GETSIGNAL(this.CurrIndicator, this.PrevIndicator);
        //        //Logger logger = LogManager.GetLogger("signal");
        //        //logger.Info($"<{this.Coin}>[{signal}] Current = {price.ToString("#,0.######")} > {json}");

        //        bool bAvailable = Ob.AvailableBalance > (decimal)Ob.MY_ACCOUNT.SafeMoney ? true : false;

        //        if (dt.Rows.Count == 0)
        //        {
        //            if(bAvailable)
        //            {
        //                if (signal == 0 || signal == 1 || signal == 3)
        //                {
        //                    if(false)
        //                    {
        //                        Abuy2Way buy = new Abuy2Way();

        //                        buy.BCoin = this.Coin;
        //                        buy.BDate = date.ToString("yyyyMMdd");
        //                        buy.BTime = date.ToString("HHmmss");
        //                        buy.BuyId = "4" + "-" + this.Coin + "_" + date.ToString("yyyyMMddHHmmssfff");

        //                        buy.StartDt = date;
        //                        buy.LongStartDt = date;
        //                        buy.ShortStartDt = date;

        //                        var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, this.InvestedMoney);
        //                        if (qty == 0) return;

        //                        Ob.ui.SetText("<" + this.Coin + "> [구매-LONG|SHORT] >> 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                        var result1 = await this.PlaceFuturesOrder_LONG_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                        if (result1 == null) return;

        //                        //var result2 = await this.PlaceFuturesOrder_SHORT_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                        //if (result2 == null) return;

        //                        await Task.Delay(1000);

        //                        var positions = await this.GetPositions();
        //                        var units = positions
        //                        .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                        .ToList();

        //                        foreach (var unit in units)
        //                        {
        //                            Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                        }

        //                        var longUnit = units.FirstOrDefault(p => p.Quantity > 0);
        //                        //var shortUnit = units.FirstOrDefault(p => p.Quantity < 0);

        //                        buy.LongStartInvestMoney = this.InvestedMoney;
        //                        buy.ShortStartInvestMoney = this.InvestedMoney;
        //                        buy.LongInvestMoney = (double)Math.Abs(longUnit.Quantity) * (double)longUnit.BreakEvenPrice;
        //                        buy.ShortInvestMoney = 0;
        //                        buy.LongQty = (double)Math.Abs(longUnit.Quantity);
        //                        buy.ShortQty = 0;
        //                        buy.LongScalingin = 0;
        //                        buy.LongScalQty = 0;
        //                        buy.LongScalMoney = 0;
        //                        buy.StartMoney = (double)longUnit.BreakEvenPrice;
        //                        buy.LongStartMoney = (double)longUnit.BreakEvenPrice;
        //                        buy.ShortStartMoney = 0;
        //                        buy.LongExecMoney = (double)longUnit.BreakEvenPrice;
        //                        buy.ShortExecMoney = 0;
        //                        buy.LongStatus = "0";
        //                        buy.ShortStatus = "1";
        //                        buy.ShortScalingin = 0;
        //                        buy.ShortScalQty = 0;
        //                        buy.ShortScalMoney = 0;
        //                        buy.Status = "0";
        //                        buy.LongPnl = 0;
        //                        buy.ShortPnl = 0;
        //                        buy.LongCloseMoney = 0;
        //                        buy.ShortCloseMoney = 0;
        //                        buy.LongCloseDt = DateTime.MinValue;
        //                        buy.ShortCloseDt = DateTime.MinValue;
        //                        buy.LongCloseParam = null;
        //                        buy.ShortCloseParam = null;
        //                        buy.CloseDt = DateTime.MinValue;

        //                        var ret = await this.InUpData(buy, true);
        //                        if (!ret) bException = true;

        //                        buy.Dispose();
        //                        buy = null;
        //                    }                            
        //                }
        //            }
        //        }
        //        else
        //        {
        //            DataRow dr = dt.Rows[0];
        //            var buy = await this.ConvBuy(dr);
        //            if (buy == null) return;

        //            bool bAvailable_Long = buy.LongInvestMoney > this.MaxMoney ? false : true;
        //            bool bAvailable_Short = buy.ShortInvestMoney > this.MaxMoney ? false : true;

        //            //if (this.Coin == "WLDUSDT" || this.Coin == "KNCUSDT" || this.Coin == "AAVEUSDT")
        //            //{
        //            //    bAvailable_Long = buy.LongInvestMoney > 2000 ? false : true;
        //            //}

        //            double LongMargin = dr["LongMargin"] != DBNull.Value ? Convert.ToInt32(dr["LongMargin"]) : 0;
        //            double ShortMargin = dr["ShortMargin"] != DBNull.Value ? Convert.ToInt32(dr["ShortMargin"]) : 0;

        //            if (this.MarginLong == 0) this.MarginLong = LongMargin;
        //            if (this.MarginShort == 0) this.MarginShort = ShortMargin;

        //            switch (signal)
        //            {
        //                case 0: //신호X
        //                case 1: //횡보 중 약한 상승 구간 (LONG)
        //                case 3: //횡보 중 약한 하락 구간 (SHORT)
        //                    {
        //                        if (buy.ShortStatus == "0" && buy.LongStatus == "0")
        //                        {
        //                            double currentPrice = price;

        //                            double pnlLongPct = buy.LongPnl / buy.LongInvestMoney * 100;
        //                            double pnlShorPct = buy.ShortPnl / buy.ShortInvestMoney * 100;


        //                            //double pnlLongPct = (currentPrice - buy.LongBaseMoney) / buy.LongBaseMoney * 100;
        //                            //double pnlShorPct = (buy.ShortBaseMoney - currentPrice) / buy.ShortBaseMoney * 100;

        //                            BinanceFuturesOrder ret1 = null;
        //                            BinanceFuturesOrder ret2 = null;
        //                            if (pnlLongPct >= Ob.Profit_Ratio)
        //                            {
        //                                Ob.ui.SetText("<" + this.Coin + "> [판매-LONG<0>] >> 가격 : " + price.ToString("#,0.######") + ", PNL : " + buy.LongPnl.ToString("#,0.######"));
        //                                (ret1, ret2) = await ClosePisition(buy, currentPrice, 1, 0, json, buy.LongPnl, buy.ShortPnl, true, date);
        //                                if (ret1 == null) return;
        //                                var ret = await this.InUpData(buy, false);
        //                                if (!ret) bException = true;
        //                            }
        //                            if (pnlShorPct >= Ob.Profit_Ratio)
        //                            {
        //                                Ob.ui.SetText("<" + this.Coin + "> [판매-SHORT<0>] >> 가격 : " + price.ToString("#,0.######") + ", PNL : " + buy.ShortPnl.ToString("#,0.######"));
        //                                (ret1, ret2) = await ClosePisition(buy, currentPrice, 2, 0, json, buy.LongPnl, buy.ShortPnl, true, date);
        //                                if (ret2 == null) return;
        //                                var ret = await this.InUpData(buy, false);
        //                                if (!ret) bException = true;
        //                            }

        //                            //비율이 6 이상이고, 롱의 currentPrice 값이 평단가보다(50%) 밑으로(숏은 위로) 올라가고 추세가 롱일때 (숏은 추세가 숏일때) 현재 구입 금액의 50%를 추매해서 평단가를 맞춘다.
        //                            //롱, 숏 동시에 처리하며 마지막 추매 시간이 12시간 이상일 때만 추매
        //                            double Gap = (buy.LongBaseMoney - buy.ShortBaseMoney);
        //                            double ratio = 0.50;
        //                            double longRebuyPrice = (buy.LongBaseMoney * (1.0 - ratio)) + (buy.ShortBaseMoney * ratio);
        //                            double shortRebuyPrice = (buy.ShortBaseMoney * (1.0 - ratio)) + (buy.LongBaseMoney * ratio);

        //                            double Ratio = Gap / currentPrice * 100;
        //                            TimeSpan ts1 = Ob.app.NowTime() - buy.LongScalDt;
        //                            TimeSpan ts2 = Ob.app.NowTime() - buy.ShortScalDt;

        //                            double longPercent = ((currentPrice - buy.LongBaseMoney) / buy.LongBaseMoney) * 100.0;
        //                            double shortPercent = ((buy.ShortBaseMoney - currentPrice) / buy.ShortBaseMoney) * 100.0;


        //                            if (Ratio > 2)
        //                            {
        //                                double ReBuyPrice = 0;
        //                                string Val = "";
        //                                string Hours = Math.Round(ts1.TotalHours, 2).ToString();
        //                                if (signal == 1 || signal == 3)
        //                                {
        //                                    if (signal == 1)
        //                                    {
        //                                        ReBuyPrice = longRebuyPrice;
        //                                        if (currentPrice < longRebuyPrice)
        //                                        {
        //                                            Val = "True";
        //                                        }
        //                                        ReBuyPrice = currentPrice - 10000;

        //                                    }
        //                                    else
        //                                    {
        //                                        ReBuyPrice = shortRebuyPrice;
        //                                        Hours = Math.Round(ts2.TotalHours,2).ToString();
        //                                        if (currentPrice > shortRebuyPrice)
        //                                        {
        //                                            Val = "True";
        //                                        }
        //                                        ReBuyPrice = currentPrice + 10000;
        //                                    }

        //                                }
        //                                //bool bExecMoney = buy.LongExecMoney < currentPrice ? true : false;
        //                                bool bExecMoney = true;
        //                                if(buy.LongScalingin < 1)
        //                                {
        //                                    bExecMoney = true;
        //                                }else
        //                                {
        //                                    bExecMoney = Math.Abs(currentPrice - buy.LongExecMoney) >= this.CurrIndicator.atr_15m * 0.5;
        //                                }
        //                                bExecMoney = true;
        //                                if (signal == 1 && ts1.TotalHours >= 8 && longPercent < -2 && bAvailable && bAvailable_Long && bExecMoney)
        //                                {
        //                                    double additionalUsd = buy.LongInvestMoney * 0.4;
        //                                    if (additionalUsd > 100)
        //                                    {
        //                                        if (buy.LongInvestMoney > 500)
        //                                        {
        //                                            additionalUsd = 50;
        //                                        }
        //                                        else
        //                                        {
        //                                            additionalUsd = 100;
        //                                        }
        //                                    }
        //                                    if (additionalUsd < 25) additionalUsd = 25;
        //                                    var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, additionalUsd);
        //                                    if (qty == 0) return;

        //                                    Ob.ui.SetText("<" + this.Coin + "> [추매-평단가조정-LONG<5>] >> 추매값 : " + additionalUsd.ToString("#,0.######") + ", 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                                    var result1 = await this.PlaceFuturesOrder_LONG_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                                    if (result1 == null) return;
        //                                    await Task.Delay(1000);

        //                                    var positions = await this.GetPositions();
        //                                    var units = positions
        //                                    .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                                    .ToList();
        //                                    foreach (var unit in units)
        //                                    {
        //                                        Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                                    }
        //                                    var longUnit = units.FirstOrDefault(p => p.Quantity > 0);
        //                                    buy.LongInvestMoney = (double)Math.Abs(longUnit.Quantity) * (double)longUnit.BreakEvenPrice;
        //                                    buy.LongQty = (double)Math.Abs(longUnit.Quantity);
        //                                    buy.LongScalingin += 1;
        //                                    buy.LongScalQty += (double)qty;
        //                                    buy.LongScalMoney += additionalUsd;
        //                                    buy.ScalDt = Ob.app.NowTime();
        //                                    buy.LongScalDt = Ob.app.NowTime();
        //                                    buy.LongExecMoney = currentPrice;
        //                                    var ret = await this.InUpData(buy, false);
        //                                    if (!ret) bException = true;
        //                                }
        //                                else
        //                                {
        //                                    if (!string.IsNullOrEmpty(Val))
        //                                    {
        //                                        //Ob.ui.SetText("<" + this.Coin + "> [SIGNAL:" + signal.ToString() + "][" + Math.Round(Ratio, 2).ToString() + "][" + Hours + "] >> Value : " + Val + ", currentPrice : " + currentPrice.ToString("#,0.######") + ", ReBuyPrice : " + ReBuyPrice.ToString("#,0.######") + "");
        //                                    }
        //                                }
        //                                //bExecMoney = buy.ShortExecMoney > currentPrice ? true : false;
        //                                if (buy.ShortScalingin < 1)
        //                                {
        //                                    bExecMoney = true;
        //                                }
        //                                else
        //                                {
        //                                    bExecMoney = Math.Abs(currentPrice - buy.ShortExecMoney) >= this.CurrIndicator.atr_15m * 0.5;
        //                                }
        //                                bExecMoney = true;
        //                                if (signal == 3 && ts2.TotalHours >= 8 && shortPercent < -2 && bAvailable && bAvailable_Short && bExecMoney)
        //                                {
        //                                    double additionalUsd = buy.ShortInvestMoney * 0.4;
        //                                    if (additionalUsd > 100)
        //                                    {
        //                                        if(buy.ShortInvestMoney > 500)
        //                                        {
        //                                            additionalUsd = 50;
        //                                        }
        //                                        else
        //                                        {
        //                                            additionalUsd = 100;
        //                                        }
        //                                    }
        //                                    if (additionalUsd < 25) additionalUsd = 25;
        //                                    var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, additionalUsd); 
        //                                    if (qty == 0) return;

        //                                    Ob.ui.SetText("<" + this.Coin + "> [추매-평단가조정-SHORT<5>] >> 추매값 : " + additionalUsd.ToString("#,0.######") + ", 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                                    var result2 = await this.PlaceFuturesOrder_SHORT_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                                    if (result2 == null) return;

        //                                    await Task.Delay(1000);

        //                                    var positions = await this.GetPositions();
        //                                    var units = positions
        //                                    .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                                    .ToList();

        //                                    foreach (var unit in units)
        //                                    {
        //                                        Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                                    }

        //                                    var shortUnit = units.FirstOrDefault(p => p.Quantity < 0);
        //                                    buy.ShortInvestMoney = (double)Math.Abs(shortUnit.Quantity) * (double)shortUnit.BreakEvenPrice;
        //                                    buy.ShortQty += (double)Math.Abs(shortUnit.Quantity);
        //                                    buy.ShortScalingin += 1;
        //                                    buy.ShortScalQty += (double)qty;
        //                                    buy.ShortScalMoney += additionalUsd;
        //                                    buy.ScalDt = Ob.app.NowTime();
        //                                    buy.ShortScalDt = Ob.app.NowTime();
        //                                    buy.ShortExecMoney = currentPrice;
        //                                    var ret = await this.InUpData(buy, false);
        //                                    if (!ret) bException = true;
        //                                }else
        //                                {
        //                                    if (!string.IsNullOrEmpty(Val))
        //                                    {
        //                                        //Ob.ui.SetText("<" + this.Coin + "> [SIGNAL:" + signal.ToString() + "][" + Math.Round(Ratio, 2).ToString() + "][" + Hours + "] >> Value : " + Val + ", currentPrice : " + currentPrice.ToString("#,0.######") + ", ReBuyPrice : " + ReBuyPrice.ToString("#,0.######") + "");
        //                                    }
        //                                }
        //                            }
        //                        }
        //                        else
        //                        {
        //                            if (buy.LongStatus == "0")
        //                            {
        //                                double currentPrice = price;
        //                                double pnlLongPct = buy.LongPnl / buy.LongInvestMoney * 100;

        //                                if (pnlLongPct >= Ob.Profit_Ratio)
        //                                {
        //                                    Ob.ui.SetText("<" + this.Coin + "> [판매-LONG<1>] >> 가격 : " + price.ToString("#,0.######") + ", PNL : " + buy.LongPnl.ToString("#,0.######"));
        //                                    var (ret1, ret2) = await ClosePisition(buy, currentPrice, 1, 0, json, buy.LongPnl, 0, true, date);
        //                                    if (ret1 == null) return;
        //                                    var ret = await this.InUpData(buy, false);
        //                                    if (!ret) bException = true;
        //                                }
        //                                else
        //                                {
        //                                    double priceBasedTrigger = buy.ShortCloseMoney * 1.02;
        //                                    double reEntryTriggerPriceShort = this.CurrIndicator.bbLower_15m + (this.CurrIndicator.bbMiddle_15m - this.CurrIndicator.bbLower_15m) * 0.3;
        //                                    double triggerPrice = signal == 3 ? priceBasedTrigger : reEntryTriggerPriceShort;

        //                                    //if (currentPrice >= triggerPrice && bAvailable)
        //                                    if (bAvailable)
        //                                    {
        //                                        if(false)
        //                                        {
        //                                            Abuy2Way newBuy = (Abuy2Way)buy.Clone();
        //                                            //double buyMoney = buy.LongInvestMoney;
        //                                            double buyMoney = this.InvestedMoney;
        //                                            var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, buyMoney);
        //                                            if (qty == 0) return;

        //                                            Ob.ui.SetText("<" + this.Coin + "> [구매-SHORT<1>] >> 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                                            var result2 = await this.PlaceFuturesOrder_SHORT_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                                            if (result2 == null) return;

        //                                            var (ret1, ret2) = await ClosePisition(buy, currentPrice, 5, 1, json, 0, 0, true, date);
        //                                            var ret = await this.InUpData(buy, false);
        //                                            if (!ret) bException = true;

        //                                            await Task.Delay(1000);

        //                                            var positions = await this.GetPositions();
        //                                            var units = positions
        //                                            .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                                            .ToList();

        //                                            foreach (var unit in units)
        //                                            {
        //                                                Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                                            }

        //                                            var shortUnit = units.FirstOrDefault(p => p.Quantity < 0);
        //                                            newBuy.BDate = date.ToString("yyyyMMdd");
        //                                            newBuy.BTime = date.ToString("HHmmss");
        //                                            newBuy.BuyId = "4" + "-" + this.Coin + "_" + date.ToString("yyyyMMddHHmmssfff");
        //                                            newBuy.ShortStartDt = date;
        //                                            newBuy.ShortStartInvestMoney = buyMoney;
        //                                            newBuy.ShortInvestMoney = (double)Math.Abs(shortUnit.Quantity) * (double)shortUnit.BreakEvenPrice;
        //                                            newBuy.ShortQty = (double)Math.Abs(shortUnit.Quantity);
        //                                            newBuy.ShortScalingin = 0;
        //                                            newBuy.ShortScalQty = 0;
        //                                            newBuy.ShortScalMoney = 0;
        //                                            newBuy.ShortStartMoney = (double)shortUnit.BreakEvenPrice;
        //                                            newBuy.ShortExecMoney = (double)shortUnit.BreakEvenPrice;
        //                                            newBuy.ShortStatus = "0";
        //                                            newBuy.Status = "0";
        //                                            newBuy.ShortParam = "";
        //                                            newBuy.ShortPnl = 0;
        //                                            newBuy.ShortCloseMoney = 0;

        //                                            ret = await this.InUpData(newBuy, true);
        //                                            if (!ret) bException = true;
        //                                        }
        //                                    }
        //                                }
        //                            }

        //                            if (buy.ShortStatus == "0")
        //                            {
        //                                double currentPrice = price;
        //                                double pnlShorPct = buy.ShortPnl / buy.ShortInvestMoney * 100;

        //                                if (pnlShorPct >= Ob.Profit_Ratio)
        //                                {
        //                                    Ob.ui.SetText("<" + this.Coin + "> [판매-SHORT<1>] >> 가격 : " + price.ToString("#,0.######") + ", PNL : " + buy.ShortPnl.ToString("#,0.######"));
        //                                    var (ret1, ret2) = await ClosePisition(buy, currentPrice, 2, 0, json, 0, buy.ShortPnl, true, date);
        //                                    if (ret2 == null) return;
        //                                    var ret = await this.InUpData(buy, false);
        //                                    if (!ret) bException = true;
        //                                }
        //                                else
        //                                {
        //                                    double priceBasedTrigger = buy.LongCloseMoney * 0.98;
        //                                    double reEntryTriggerPriceLong = this.CurrIndicator.bbUpper_15m - (this.CurrIndicator.bbUpper_15m - this.CurrIndicator.bbMiddle_15m) * 0.3;
        //                                    double triggerPrice = signal == 1 ? priceBasedTrigger : reEntryTriggerPriceLong;

        //                                    //if (currentPrice <= triggerPrice && bAvailable)
        //                                    if (bAvailable)
        //                                    {
        //                                        if(false)
        //                                        {
        //                                            Abuy2Way newBuy = (Abuy2Way)buy.Clone();
        //                                            //double buyMoney = buy.ShortInvestMoney;
        //                                            double buyMoney = this.InvestedMoney;
        //                                            var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, buyMoney);
        //                                            if (qty == 0) return;

        //                                            Ob.ui.SetText("<" + this.Coin + "> [구매-LONG<1>] >> 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                                            var result1 = await this.PlaceFuturesOrder_LONG_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                                            if (result1 == null) return;

        //                                            var (ret1, ret2) = await ClosePisition(buy, currentPrice, 4, 1, json, 0, 0, true, date);
        //                                            var ret = await this.InUpData(buy, false);
        //                                            if (!ret) bException = true;

        //                                            await Task.Delay(1000);

        //                                            var positions = await this.GetPositions();
        //                                            var units = positions
        //                                            .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                                            .ToList();

        //                                            foreach (var unit in units)
        //                                            {
        //                                                Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                                            }

        //                                            var longUnit = units.FirstOrDefault(p => p.Quantity > 0);

        //                                            newBuy.BDate = date.ToString("yyyyMMdd");
        //                                            newBuy.BTime = date.ToString("HHmmss");
        //                                            newBuy.BuyId = "4" + "-" + this.Coin + "_" + date.ToString("yyyyMMddHHmmssfff");
        //                                            newBuy.LongStartDt = date;
        //                                            newBuy.LongStartInvestMoney = buyMoney;
        //                                            newBuy.LongInvestMoney = (double)Math.Abs(longUnit.Quantity) * (double)longUnit.BreakEvenPrice;
        //                                            newBuy.LongQty = (double)Math.Abs(longUnit.Quantity);
        //                                            newBuy.LongScalingin = 0;
        //                                            newBuy.LongScalQty = 0;
        //                                            newBuy.LongScalMoney = 0;
        //                                            newBuy.LongStartMoney = (double)longUnit.BreakEvenPrice;
        //                                            newBuy.LongExecMoney = (double)longUnit.BreakEvenPrice;
        //                                            newBuy.LongStatus = "0";
        //                                            newBuy.Status = "0";
        //                                            newBuy.LongParam = "";
        //                                            newBuy.LongPnl = 0;
        //                                            newBuy.LongCloseMoney = 0;
        //                                            ret = await this.InUpData(newBuy, true);
        //                                            if (!ret) bException = true;
        //                                        }                                                
        //                                    }
        //                                }
        //                            }
        //                        }
        //                        break;
        //                    }
        //                case 2: //돌파 직전 구간 (LONG)
        //                    {
        //                        double currentPrice = price;
        //                        if (buy.ShortStatus == "0")
        //                        {
        //                            double pnlShortUsd = buy.ShortPnl;
        //                            double combinedPnlPct = pnlShortUsd / InvestedMoney * 100;
        //                            if (pnlShortUsd > 0.5)
        //                            {
        //                                Ob.ui.SetText("<" + this.Coin + "> [판매-SHORT<2>] >> 가격 : " + price.ToString("#,0.######") + ", PNL : " + pnlShortUsd.ToString("#,0.######"));
        //                                var (ret1, ret2) = await ClosePisition(buy, currentPrice, 2, 0, json, 0, pnlShortUsd, true, date);
        //                                if (ret2 == null) return;
        //                                var ret = await this.InUpData(buy, false);
        //                                if (!ret) bException = true;
        //                            }
        //                        }
        //                        if (buy.LongStatus == "0")
        //                        {
        //                            double LongPercent = (currentPrice - buy.LongExecMoney) / buy.LongExecMoney * 100;
        //                            if (LongPercent >= 10 && bAvailable && bAvailable_Long)
        //                            {
        //                                double additionalLongUsd = InvestedMoney * 0.30;
        //                                var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, additionalLongUsd);
        //                                if (qty == 0) return;

        //                                Ob.ui.SetText("<" + this.Coin + "> [추매-LONG<2>] >> 추매값 : " + additionalLongUsd.ToString("#,0.######") + ", 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                                var result1 = await this.PlaceFuturesOrder_LONG_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                                if (result1 == null) return;
        //                                await Task.Delay(1000);

        //                                var positions = await this.GetPositions();
        //                                var units = positions
        //                                .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                                .ToList();
        //                                foreach (var unit in units)
        //                                {
        //                                    Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                                    if(unit.PositionSide == PositionSide.Short)
        //                                    {
        //                                        if (unit.LiquidationPrice != 0)
        //                                        {
        //                                            decimal liquidationPrice = unit.LiquidationPrice;
        //                                            decimal markPrice = unit.MarkPrice;
        //                                            decimal distanceFromLiquidation = Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100);
        //                                            if (distanceFromLiquidation <= 3)
        //                                            {
        //                                                decimal marginToAdd = unit.IsolatedMargin * 0.30m;
        //                                                Ob.ui.SetText($"<{unit.Symbol}:{unit.PositionSide.ToString().ToUpper()}> 증거금 추가(1) : {marginToAdd:F4} USDT");
        //                                                await PositionMarginLongShortAsync(this.Coin, marginToAdd, unit.PositionSide);
        //                                            }
        //                                        }
        //                                    }
        //                                }
        //                                var longUnit = units.FirstOrDefault(p => p.Quantity > 0);
        //                                buy.LongInvestMoney = (double)Math.Abs(longUnit.Quantity) * (double)longUnit.BreakEvenPrice;
        //                                buy.LongQty = (double)Math.Abs(longUnit.Quantity);
        //                                buy.LongScalingin += 1;
        //                                buy.LongScalQty += (double)qty;
        //                                buy.LongScalMoney += additionalLongUsd;
        //                                buy.LongExecMoney = currentPrice;
        //                                buy.ScalDt = Ob.app.NowTime();
        //                                buy.LongScalDt = Ob.app.NowTime();
        //                                var ret = await this.InUpData(buy, false);
        //                                if (!ret) bException = true;
        //                            }
        //                        }else
        //                        {
        //                            if(bAvailable)
        //                            {
        //                                if(false)
        //                                {
        //                                    Abuy2Way newBuy = (Abuy2Way)buy.Clone();

        //                                    double buyMoney = this.InvestedMoney;
        //                                    var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, buyMoney);
        //                                    if (qty == 0) return;

        //                                    Ob.ui.SetText("<" + this.Coin + "> [구매-LONG<2>] >> 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                                    var result1 = await this.PlaceFuturesOrder_LONG_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                                    if (result1 == null) return;

        //                                    var (ret1, ret2) = await ClosePisition(buy, currentPrice, 4, 1, json, 0, 0, true, date);
        //                                    var ret = await this.InUpData(buy, false);
        //                                    if (!ret) bException = true;

        //                                    await Task.Delay(1000);

        //                                    var positions = await this.GetPositions();
        //                                    var units = positions
        //                                    .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                                    .ToList();

        //                                    foreach (var unit in units)
        //                                    {
        //                                        Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                                        if (unit.PositionSide == PositionSide.Short)
        //                                        {
        //                                            if (unit.LiquidationPrice != 0)
        //                                            {
        //                                                decimal liquidationPrice = unit.LiquidationPrice;
        //                                                decimal markPrice = unit.MarkPrice;
        //                                                decimal distanceFromLiquidation = Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100);
        //                                                if (distanceFromLiquidation <= 3)
        //                                                {
        //                                                    decimal marginToAdd = unit.IsolatedMargin * 0.30m;
        //                                                    Ob.ui.SetText($"<{unit.Symbol}:{unit.PositionSide.ToString().ToUpper()}> 증거금 추가(1) : {marginToAdd:F4} USDT");
        //                                                    await PositionMarginLongShortAsync(this.Coin, marginToAdd, unit.PositionSide);
        //                                                }
        //                                            }
        //                                        }
        //                                    }

        //                                    var longUnit = units.FirstOrDefault(p => p.Quantity > 0);

        //                                    newBuy.BDate = date.ToString("yyyyMMdd");
        //                                    newBuy.BTime = date.ToString("HHmmss");
        //                                    newBuy.BuyId = "4" + "-" + this.Coin + "_" + date.ToString("yyyyMMddHHmmssfff");
        //                                    newBuy.LongStartDt = date;
        //                                    newBuy.LongStartInvestMoney = buyMoney;
        //                                    newBuy.LongInvestMoney = (double)Math.Abs(longUnit.Quantity) * (double)longUnit.BreakEvenPrice;
        //                                    newBuy.LongQty = (double)Math.Abs(longUnit.Quantity);
        //                                    newBuy.LongScalingin = 0;
        //                                    newBuy.LongScalQty = 0;
        //                                    newBuy.LongScalMoney = 0;
        //                                    newBuy.LongStartMoney = (double)longUnit.BreakEvenPrice;
        //                                    newBuy.LongExecMoney = (double)longUnit.BreakEvenPrice;
        //                                    newBuy.LongStatus = "0";
        //                                    newBuy.Status = "0";
        //                                    newBuy.LongParam = "";
        //                                    newBuy.LongPnl = 0;
        //                                    newBuy.LongCloseMoney = 0;
        //                                    ret = await this.InUpData(newBuy, true);
        //                                    if (!ret) bException = true;

        //                                }
        //                            }
        //                        }
        //                        break;
        //                    }
        //                case 4: //급락 직전 구간 (SHORT)
        //                    {
        //                        double currentPrice = price;
        //                        if (buy.LongStatus == "0")
        //                        {
        //                            double pnlLongUsd = buy.LongPnl;
        //                            double combinedPnlPct = pnlLongUsd / InvestedMoney * 100;
        //                            if (pnlLongUsd > 0.5)
        //                            {
        //                                Ob.ui.SetText("<" + this.Coin + "> [판매-LONG<4>] >> 가격 : " + price.ToString("#,0.######") + ", PNL : " + pnlLongUsd.ToString("#,0.######"));
        //                                var (ret1, ret2) = await ClosePisition(buy, currentPrice, 1, 0, json, pnlLongUsd, 0, true, date);
        //                                if (ret1 == null) return;
        //                                var ret = await this.InUpData(buy, false);
        //                                if (!ret) bException = true;
        //                            }

        //                        }
        //                        if (buy.ShortStatus == "0")
        //                        {
        //                            double ShortPercent = (buy.ShortExecMoney - currentPrice) / buy.ShortExecMoney * 100;
        //                            if (ShortPercent >= 10 && bAvailable && bAvailable_Short)
        //                            {
        //                                double additionalLongUsd = InvestedMoney * 0.30;
        //                                var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, additionalLongUsd);
        //                                if (qty == 0) return;

        //                                Ob.ui.SetText("<" + this.Coin + "> [추매-SHORT<4>] >> 추매값 : " + additionalLongUsd.ToString("#,0.######") + ", 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                                var result2 = await this.PlaceFuturesOrder_SHORT_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                                if (result2 == null) return;

        //                                await Task.Delay(1000);

        //                                var positions = await this.GetPositions();
        //                                var units = positions
        //                                .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                                .ToList();

        //                                foreach (var unit in units)
        //                                {
        //                                    Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                                    if (unit.PositionSide == PositionSide.Long)
        //                                    {
        //                                        if (unit.LiquidationPrice != 0)
        //                                        {
        //                                            decimal liquidationPrice = unit.LiquidationPrice;
        //                                            decimal markPrice = unit.MarkPrice;
        //                                            decimal distanceFromLiquidation = Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100);
        //                                            if (distanceFromLiquidation <= 3)
        //                                            {
        //                                                decimal marginToAdd = unit.IsolatedMargin * 0.30m;
        //                                                Ob.ui.SetText($"<{unit.Symbol}:{unit.PositionSide.ToString().ToUpper()}> 증거금 추가(1) : {marginToAdd:F4} USDT");
        //                                                await PositionMarginLongShortAsync(this.Coin, marginToAdd, unit.PositionSide);
        //                                            }
        //                                        }
        //                                    }
        //                                }

        //                                var shortUnit = units.FirstOrDefault(p => p.Quantity < 0);
        //                                buy.ShortInvestMoney = (double)Math.Abs(shortUnit.Quantity) * (double)shortUnit.BreakEvenPrice;
        //                                buy.ShortQty += (double)Math.Abs(shortUnit.Quantity);
        //                                buy.ShortScalingin += 1;
        //                                buy.ShortScalQty += (double)qty;
        //                                buy.ShortScalMoney += additionalLongUsd;
        //                                buy.ShortExecMoney = currentPrice;
        //                                buy.ScalDt = Ob.app.NowTime();
        //                                buy.ShortScalDt = Ob.app.NowTime();
        //                                var ret = await this.InUpData(buy, false);
        //                                if (!ret) bException = true;
        //                            }
        //                        }else
        //                        {
        //                            if(bAvailable)
        //                            {
        //                                if(false)
        //                                {
        //                                    Abuy2Way newBuy = (Abuy2Way)buy.Clone();
        //                                    double buyMoney = this.InvestedMoney;
        //                                    var qty = await this.CalculateBuyQuantityAsync(this.Coin, price, buyMoney);
        //                                    if (qty == 0) return;

        //                                    Ob.ui.SetText("<" + this.Coin + "> [구매-SHORT<4>] >> 가격 : " + price.ToString("#,0.######") + ", 수량 : " + qty.ToString());

        //                                    var result2 = await this.PlaceFuturesOrder_SHORT_HEDGE(this.Coin, (decimal)qty, 0, false);
        //                                    if (result2 == null) return;

        //                                    var (ret1, ret2) = await ClosePisition(buy, currentPrice, 5, 1, json, 0, 0, true, date);
        //                                    var ret = await this.InUpData(buy, false);
        //                                    if (!ret) bException = true;

        //                                    await Task.Delay(1000);

        //                                    var positions = await this.GetPositions();
        //                                    var units = positions
        //                                    .Where(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase))
        //                                    .ToList();

        //                                    foreach (var unit in units)
        //                                    {
        //                                        Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());
        //                                        if (unit.PositionSide == PositionSide.Long)
        //                                        {
        //                                            if (unit.LiquidationPrice != 0)
        //                                            {
        //                                                decimal liquidationPrice = unit.LiquidationPrice;
        //                                                decimal markPrice = unit.MarkPrice;
        //                                                decimal distanceFromLiquidation = Math.Abs((markPrice - liquidationPrice) / liquidationPrice * 100);
        //                                                if (distanceFromLiquidation <= 3)
        //                                                {
        //                                                    decimal marginToAdd = unit.IsolatedMargin * 0.30m;
        //                                                    Ob.ui.SetText($"<{unit.Symbol}:{unit.PositionSide.ToString().ToUpper()}> 증거금 추가(1) : {marginToAdd:F4} USDT");
        //                                                    await PositionMarginLongShortAsync(this.Coin, marginToAdd, unit.PositionSide);
        //                                                }
        //                                            }
        //                                        }
        //                                    }

        //                                    var shortUnit = units.FirstOrDefault(p => p.Quantity < 0);
        //                                    newBuy.BDate = date.ToString("yyyyMMdd");
        //                                    newBuy.BTime = date.ToString("HHmmss");
        //                                    newBuy.BuyId = "4" + "-" + this.Coin + "_" + date.ToString("yyyyMMddHHmmssfff");
        //                                    newBuy.ShortStartDt = date;
        //                                    newBuy.ShortStartInvestMoney = buyMoney;
        //                                    newBuy.ShortInvestMoney = (double)Math.Abs(shortUnit.Quantity) * (double)shortUnit.BreakEvenPrice;
        //                                    newBuy.ShortQty = (double)Math.Abs(shortUnit.Quantity);
        //                                    newBuy.ShortScalingin = 0;
        //                                    newBuy.ShortScalQty = 0;
        //                                    newBuy.ShortScalMoney = 0;
        //                                    newBuy.ShortStartMoney = (double)shortUnit.BreakEvenPrice;
        //                                    newBuy.ShortExecMoney = (double)shortUnit.BreakEvenPrice;
        //                                    newBuy.ShortStatus = "0";
        //                                    newBuy.Status = "0";
        //                                    newBuy.ShortParam = "";
        //                                    newBuy.ShortPnl = 0;
        //                                    newBuy.ShortCloseMoney = 0;

        //                                    ret = await this.InUpData(newBuy, true);
        //                                    if (!ret) bException = true;
        //                                }
        //                            }
        //                        }
        //                        break;
        //                    }
        //                default:
        //                    break;
        //            }
        //            buy.Dispose();
        //            buy = null;
        //        }

        //        if (this.ThisSignal != signal)
        //        {
        //            this.ThisSignal = signal;
        //            string uQuery = $"update acoin set NSignal = {this.ThisSignal} where coin = '{this.Coin}'";
        //            await Ob.db.ExecuteQueryAsync(uQuery, false);
        //        }

        //        try
        //        {
        //            if (Ob.MQ_INIT == 1)
        //            {
        //                // 1. 현재 시간 확인
        //                DateTime now = DateTime.Now;

        //                // 2. 해당 코인의 마지막 전송 기록이 있는지 확인
        //                // 기록이 있고, 1분(60초)이 지나지 않았다면 전송하지 않고 스킵(return)
        //                if (_lastMqSendTime.TryGetValue(this.Coin, out DateTime lastTime))
        //                {
        //                    if ((now - lastTime).TotalSeconds < 300) // 1분 미만이면
        //                    {
        //                        return; // 전송 안 함
        //                    }
        //                }

        //                // 3. 전송 조건 충족 시: 마지막 전송 시간 갱신 (덮어쓰기)
        //                _lastMqSendTime[this.Coin] = now;

        //                // --- 기존 전송 로직 ---
        //                Dictionary<string, object> dictData = new Dictionary<string, object>();
        //                dictData.Add("symbol", this.Coin);
        //                dictData.Add("signal", signal);
        //                dictData.Add("bbLower_15m", this.CurrIndicator.bbLower_15m);
        //                dictData.Add("bbMiddle_15m", this.CurrIndicator.bbMiddle_15m);
        //                dictData.Add("bbUpper_15m", this.CurrIndicator.bbUpper_15m);
        //                dictData.Add("close", this.CurrIndicator.closes1h);
        //                dictData.Add("event_dt", Ob.app.GetNowTime("DT"));

        //                string sendData = Ob.app.AES_Encrypt(JsonConvert.SerializeObject(dictData, Formatting.Indented));
        //                byte[] body = Encoding.UTF8.GetBytes(sendData);

        //                // 비동기 전송 (CancellationToken이 없다면 default 사용 추천)
        //                Ob.channel.BasicPublishAsync(
        //                    exchange: Ob.MY_ACCOUNT.QueueName,
        //                    routingKey: "",
        //                    body: body,
        //                    mandatory: false,
        //                    cancellationToken: default
        //                );
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            Ob.MQ_INIT = 0;
        //            Ob.app._ERROR("CheckCoin2-MQ", ex);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Ob.app._ERROR("CheckCoin2", ex);
        //    }

        //}
        public async Task PositionMarginLongShortAsync(string symbol, decimal amount, PositionSide side)
        {
            FuturesMarginChangeDirectionType type = amount >= 0
            ? FuturesMarginChangeDirectionType.Add
            : FuturesMarginChangeDirectionType.Reduce;

            decimal absAmount = Math.Abs(amount);

            var result = await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, absAmount, type, side);
            if (result.Success)
            {
                Ob.ui.SetText("<" + symbol + ">[" + symbol + " - " + side.ToString().ToUpper() + " [" + type.ToString().ToUpper() + "] <ModifyPositionMarginAsync-성공>(1)] (" + amount.ToString() + ") " + result.Data.Message.ToString());
                return;
            }
            else
            {
                Ob.ui.SetText("<" + symbol + ">[" + symbol + " - " + side.ToString().ToUpper() + " [" + type.ToString().ToUpper() + "] < ModifyPositionMarginAsync-오류>(1)] " + result.Error.Message);
                return;
            }
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
                        $"LongParam, ShortParam, LongScalingin, ShortScalingin, LongScalQty, LongScalMoney, ShortScalQty, ShortScalMoney, LongCount, ShortCount, adx15m, adx15mp, adx15mm, adx1h, adx1hp, adx1hm, adx2h, adx2hp, adx2hm, adx4h, adx4hp, adx4hm" +
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
                        $"{buy.ShortCount}," +
                        $"{this.CurrIndicator.adx_15m}," +
                        $"{this.CurrIndicator.adx_plus_15m}," +
                        $"{this.CurrIndicator.adx_minus_15m}," +
                        $"{this.CurrIndicator.ind1h.Adx}," +
                        $"{this.CurrIndicator.ind1h.AdxPlus}," +
                        $"{this.CurrIndicator.ind1h.AdxMinus}," +
                        $"{this.CurrIndicator.adx_2h}," +
                        $"{this.CurrIndicator.adx_plus_2h}," +
                        $"{this.CurrIndicator.adx_minus_2h}," +
                        $"{this.CurrIndicator.ind4h.Adx}," +
                        $"{this.CurrIndicator.ind4h.AdxPlus}," +
                        $"{this.CurrIndicator.ind4h.AdxMinus}" +
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

                var ret = await Ob.db.ExecuteQueryAsync(query, true);
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
                //BinanceFuturesOrder ret1 = new BinanceFuturesOrder();
                //BinanceFuturesOrder ret2 = new BinanceFuturesOrder();
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
                    ret1 = await this.PlaceFuturesOrder_CLOSE_HEDGE(this.Coin, (decimal)buy.LongQty, false);
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
                    ret2 = await this.PlaceFuturesOrder_CLOSE_HEDGE(this.Coin, (decimal)buy.ShortQty, true);
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
                    ret1 = await this.PlaceFuturesOrder_CLOSE_HEDGE(this.Coin, (decimal)buy.LongQty, false);
                    ret2 = await this.PlaceFuturesOrder_CLOSE_HEDGE(this.Coin, (decimal)buy.ShortQty, true);
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
                //return (new BinanceFuturesOrder(), new BinanceFuturesOrder());
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
        public async Task<BinanceFuturesPositionMarginResult> ModifyPositionMarginAsync(string symbol, decimal amount)
        {
            try
            {
                var positions = await this.GetPositions();
                var unit = positions.FirstOrDefault(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase));
                Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());

                var result = await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, amount, FuturesMarginChangeDirectionType.Add);

                if (result.Success)
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<ModifyPositionMarginAsync-성공>] (" + amount.ToString() + ") " + result.Data.Message.ToString());

                    positions = await this.GetPositions();
                    unit = positions.FirstOrDefault(s => s.Symbol.Equals(this.Coin, StringComparison.OrdinalIgnoreCase));
                    Ob.ui.SetText("<" + this.Coin + ">[POSITION] " + unit.ToString());

                    return result.Data;
                }
                else
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<ModifyPositionMarginAsync-오류>] " + result.Error.Message);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("ModifyPositionMarginAsync<" + this.Coin + ">", ex);
                return null;
            }

            //ystem.Threading.Thread.Sleep(100);
        }
        public async Task<BinanceFuturesOrder> PlaceFuturesOrder_LONG_HEDGE(string symbol, decimal quantity, int leverage, bool reduce)
        {
            try
            {
                //await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, 50m, FuturesMarginChangeDirectionType.Add);await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, 50m, FuturesMarginChangeDirectionType.Add);

                Ob.ui.SetText("<" + this.Coin + ">[" + symbol + " <LONG_HEDGE>] " + quantity.ToString());
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
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + " <LONG_HEDGE-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + " <LONG_HEDGE-오류>] " + result.Error.Message);
                    return null; // 실패한 경우 null 반환
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("PlaceFuturesOrder_SHORT_HEDGE<" + this.Coin + ">", ex);
                return null;
            }

            //ystem.Threading.Thread.Sleep(100);
        }
        public async Task<BinanceFuturesOrder> PlaceFuturesOrder_SHORT_HEDGE(string symbol, decimal quantity, int leverage, bool reduce)
        {
            try
            {
                Ob.ui.SetText("<" + this.Coin + ">[" + symbol + " <SHORT_HEDGE>] " + quantity.ToString());

                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol,                // 거래 심볼 (예: BTCUSDT)
                    OrderSide.Sell,         // "Buy" 또는 "Sell" (롱/숏 포지션)
                    FuturesOrderType.Market,       // 주문 유형 (LIMIT)
                    quantity,              // 주문 수량
                    null,                 // 주문 가격
                    positionSide: Binance.Net.Enums.PositionSide.Short
                );

                if (result.Success)
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + " <SHORT_HEDGE-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + " <SHORT_HEDGE-오류>] " + result.Error.Message);
                    return null; // 실패한 경우 null 반환
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("PlaceFuturesOrder_SHORT_HEDGE<" + this.Coin + ">", ex);
                return null;
            }

            //ystem.Threading.Thread.Sleep(100);
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
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<LONG_CLOSE-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<LONG_CLOSE-오류>] " + result.Error.Message);
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
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<SHORT_CLOSE-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<SHORT_CLOSE-오류>] " + result.Error.Message);
                    return null; // 실패한 경우 null 반환
                }
            }
        }

        public async Task<BinanceFuturesOrder> PlaceFuturesOrder_LONG(string symbol, decimal quantity, int leverage, bool reduce)
        {
            try
            {
                //await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, 50m, FuturesMarginChangeDirectionType.Add);await Ob.client.UsdFuturesApi.Account.ModifyPositionMarginAsync(symbol, 50m, FuturesMarginChangeDirectionType.Add);

                Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<LONG>] " + quantity.ToString());
                // 주문 요청 보내기 (LIMIT 주문)
                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol,                // 거래 심볼 (예: BTCUSDT)
                    OrderSide.Buy,         // "Buy" 또는 "Sell" (롱/숏 포지션)
                    FuturesOrderType.Market,       // 주문 유형 (LIMIT)
                    quantity,              // 주문 수량
                    null,                 // 주문 가격
                    positionSide: Binance.Net.Enums.PositionSide.Both,
                    reduceOnly: reduce
                );

                if (result.Success)
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<LONG-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<LONG-오류>] " + result.Error.Message);
                    return null; // 실패한 경우 null 반환
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("PlaceFuturesOrder_LONG<" + this.Coin + ">", ex);
                return null;
            }

            //ystem.Threading.Thread.Sleep(100);
        }
        public async Task<decimal> TruncatePriceToTickAsync(string symbol, decimal price)
        {
            if (Ob.exInfo == null) return 0;

            var symInfo = Ob.exInfo.Symbols
                .FirstOrDefault(s => string.Equals(s.Name, symbol, StringComparison.OrdinalIgnoreCase));

            if (symInfo == null) return 0;


            // 2) Tick Size 가져오기
            var tickSize = symInfo.PriceFilter.TickSize;

            // 3) 가격을 tickSize 단위로 버림(truncate)
            //    (price / tickSize)의 소수 부분을 잘라낸 뒤 다시 곱해준다
            var quotient = price / tickSize;
            var truncatedQuotient = Math.Truncate(quotient);
            var truncatedPrice = truncatedQuotient * tickSize;

            return truncatedPrice;
        }
        public async Task<BinanceFuturesOrder> PlaceFuturesOrder_TP(string symbol, decimal quantity, decimal takeProfitPrice)
        {
            try
            {
                var tpPrice = await this.TruncatePriceToTickAsync(symbol, takeProfitPrice);
                if (tpPrice == 0) return null;
                Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<TP>] " + quantity.ToString() + "(" + tpPrice.ToString() + ")");
                // 주문 요청 보내기 (LIMIT 주문)
                var side = quantity > 0 ? OrderSide.Sell : OrderSide.Buy;

                var quantity1 = Math.Abs(quantity);

                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: side,                       // LONG→Sell, SHORT→Buy
                    type: FuturesOrderType.TakeProfit,
                    quantity: quantity1,
                    price: tpPrice,                    // 체결 시도할 지정가
                    stopPrice: tpPrice,                    // 가격이 이 지점에 닿으면 주문 발동
                    timeInForce: TimeInForce.GoodTillCanceled, // GTC
                    reduceOnly: true                        // 포지션 청산 전용
                );

                if (result.Success)
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<TP-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<TP-오류>] " + result.Error.Message);
                    var cancelAll = await Ob.client.UsdFuturesApi.Trading.CancelAllOrdersAsync(symbol);
                    return null; // 실패한 경우 null 반환
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("PlaceFuturesOrder_TP<" + this.Coin + ">", ex);
                return null;
            }

            //ystem.Threading.Thread.Sleep(100);
        }
        public async Task<BinanceFuturesOrder> PlaceFuturesOrder_SHORT(string symbol, decimal quantity, int leverage, bool reduce)
        {
            try
            {
                Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<SHORT>] " + quantity.ToString());

                var result = await Ob.client.UsdFuturesApi.Trading.PlaceOrderAsync(
                    symbol,                // 거래 심볼 (예: BTCUSDT)
                    OrderSide.Sell,         // "Buy" 또는 "Sell" (롱/숏 포지션)
                    FuturesOrderType.Market,       // 주문 유형 (LIMIT)
                    quantity,              // 주문 수량
                    null,                 // 주문 가격
                    positionSide: Binance.Net.Enums.PositionSide.Both,
                    reduceOnly: reduce
                );

                if (result.Success)
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<SHORT-RESULT>] " + result.Data.ToString());
                    return result.Data; // 주문이 성공한 경우 주문 정보 반환
                }
                else
                {
                    Ob.ui.SetText("<" + this.Coin + ">[" + symbol + "<SHORT-오류>] " + result.Error.Message);
                    return null; // 실패한 경우 null 반환
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("PlaceFuturesOrder_LONG<" + this.Coin + ">", ex);
                return null;
            }

            //ystem.Threading.Thread.Sleep(100);
        }
        // 포지션 정보 조회 메서드
        public async Task<IEnumerable<BinancePositionDetailsUsdt>> GetPositions()
        {
            var result = await Ob.client.UsdFuturesApi.Account.GetPositionInformationAsync(this.Coin);
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
    }
    public enum Side { Buy, Sell }
    public class HedgeGridPositionRow
    {
        public long id { get; set; }
        public string symbol { get; set; } = string.Empty;
        public string status { get; set; } = "OPEN";
        public decimal initial_budget { get; set; }
        public DateTime entry_time_utc { get; set; }
        public DateTime entry_time { get; set; }  // ✅ 추가

        // LONG 포지션
        public decimal long_total_qty { get; set; }
        public decimal? long_avg_price { get; set; }
        public decimal long_total_cost { get; set; }
        public int long_position_count { get; set; }

        // SHORT 포지션
        public decimal short_total_qty { get; set; }
        public decimal? short_avg_price { get; set; }
        public decimal short_total_cost { get; set; }
        public int short_position_count { get; set; }

        // 현재 상태
        public decimal? current_price { get; set; }
        public decimal? total_pnl_usdt { get; set; }
        public decimal? total_pnl_pct { get; set; }

        public decimal? recovery_base_price { get; set; }
        public string recovery_side { get; set; } // [추가] "LONG", "SHORT", "NONE"

        // 청산 정보
        public DateTime? exit_time_utc { get; set; }
        public DateTime? exit_time { get; set; }  // ✅ 추가
        public string? exit_reason { get; set; }
        public decimal? max_budget_used { get; set; }
        public int total_trades { get; set; }

        public decimal? long_pnl_usdt { get; set; }
        public decimal? short_pnl_usdt { get; set; }
        public decimal? max_recorded_pnl { get; set; }

        public int CfgMaxTrade { get; set; }

        public decimal? long_pnl_percent { get; set; }
        public decimal? short_pnl_percent { get; set; }
        public decimal? pnl_gap { get; set; }
        public decimal pending_recovery_usd { get; set; }
        public string pending_target_side { get; set; }
        public decimal? last_tp_price { get; set; } // 🚩 추가
        public int? version { get; set; } // 🚩 추가

        public decimal? max_notional_cap { get; set; } // 🚩 추가

    }
    public class HedgeGridTradeRow
    {
        public long id { get; set; }
        public long position_id { get; set; }
        public DateTime trade_time { get; set; }
        public DateTime trade_time_utc { get; set; }
        public string side { get; set; }
        public string trade_type { get; set; }
        public decimal qty { get; set; }
        public decimal price { get; set; }
        public decimal cost { get; set; }
        public string reason { get; set; }
    }
}
