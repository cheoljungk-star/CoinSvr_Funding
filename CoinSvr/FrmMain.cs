using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Objects;
using Microsoft.Extensions.Logging;
using MySqlX.XDevAPI;
using MySqlX.XDevAPI.Common;
using MySqlX.XDevAPI.Relational;
using Nancy;
using Nancy.Hosting.Self;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Org.BouncyCastle.Ocsp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoinSvr
{
    public class FrmMain
    {
        private Timer _timer;
        private Timer _healthTimer;
        private FundingHedgeManager _FundingManager;
        private SpikeScalpManager _SpikeManager;
        public FrmMain()
        {
            try
            {
                this.SetText($"CoinSvr Service Running <{Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version}>");
                Ob.ui = this;
                //Ob.DB_CONNECTION = "Server=127.0.0.1; Port=3306;Database=coinplay;Uid=root;Pwd=dkssud79!!;pooling=true;Allow Zero Datetime=False;Min Pool Size=0;Max Pool Size=200;CharSet=utf8";
                //Ob.DB_CONNECTION = "Server=cheoljung.iptime.org; Port=6060;Database=coinplay;Uid=root;Pwd=dkssud79!!;pooling=true;Allow Zero Datetime=False;Min Pool Size=0;Max Pool Size=200;CharSet=utf8";
                Ob.DB_CONNECTION = "Server=cheoljung.iptime.org;Port=9312;Database=coinplay;User ID=root;Password=dkssud79!!;Pooling=true;Minimum Pool Size=0;Maximum Pool Size=200;Default Command Timeout=30;Connection Idle Timeout=180;Keepalive=60;Interactive Session=true;Character Set=utf8;SslMode=None;Allow Zero DateTime=False";
                this.StartUp();
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("FrmMain", ex);
            }
        }
        private async void StartUp()
        {
            try
            {
                while (!NetworkInterface.GetIsNetworkAvailable())
                {
                    Thread.Sleep(5000);
                }

                Ob.db = new DB_SVC(Ob.DB_CONNECTION);
                if (!string.IsNullOrEmpty(Ob.DB_CONNECTION))
                {
                    await Ob.db.ConnectAsync();
                    if (Ob.db.IsConnected())
                    {
                        this.SetText("[Success] DB connetion successful");
                    }
                    else
                    {
                        this.SetText("[Fail] DB connetion fail");
                    }
                }
                else
                {
                    this.SetText("[Fail] DB connetion fail-Connection String is Null Or Empty");
                }

                string Query = $"select * from access where MY_ID='127.0.0.3' AND Use_YN = '1'";
                DataTable dt = await Ob.db.SelectQueryAsync(Query);
                this.SetText("select Access Count : " + dt.Rows.Count.ToString());

                DataRow row = dt.Rows[0];
                var dict = new Dictionary<string, object>();
                foreach (DataColumn col in row.Table.Columns)
                {
                    var val = row[col];
                    dict[col.ColumnName] = val == DBNull.Value ? "" : val;
                }

                Ob.MY_ACCOUNT = JsonConvert.DeserializeObject<Access>(JsonConvert.SerializeObject(dict, Formatting.Indented));

                this.SetText($"최대 투자금 : ${Ob.MY_ACCOUNT.MaxInvest.ToString("#,0.##")}");
                this.SetText($"안전 자금 : ${Ob.MY_ACCOUNT.SafeMoney.ToString("#,0.##")}");

                ThreadPool.SetMinThreads(200, 200);

                //await Ob.app.StartMQ();

                Ob.apiKey = Ob.MY_ACCOUNT.ApiKey;
                Ob.apiSecretKey = Ob.MY_ACCOUNT.ApiSecretKey;

                Ob.IsAccount = true;

                BinanceRestClient.SetDefaultOptions(options =>
                {
                    options.ApiCredentials = new BinanceCredentials(Ob.apiKey, Ob.apiSecretKey);
                    options.RequestTimeout = TimeSpan.FromSeconds(30);
                    options.AutoTimestamp = true;
                    options.ReceiveWindow = TimeSpan.FromMilliseconds(60000);
                });
                BinanceSocketClient.SetDefaultOptions(options =>
                {
                    options.ApiCredentials = new BinanceCredentials(Ob.apiKey, Ob.apiSecretKey);
                });

                Ob.client = new BinanceRestClient();

                //Ob.socketClient_postion = new BinanceSocketClient();

                

                var ret1 = await Ob.client.UsdFuturesApi.Account.ModifyPositionModeAsync(true);
                var ret2 = await Ob.client.UsdFuturesApi.Account.SetMultiAssetsModeAsync(false);


                // 초기화
                //FundingHedger.DebugDryRun = true; // 실제 주문 차단
                //FundingHedgeManager.DebugForceFundingTime = DateTime.UtcNow.AddSeconds(40);
                // 1) 거래 심볼 정보 조회
                var infoResult = await Ob.client.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync();
                this.SetText("GetExchangeInfo >> " + infoResult.Success.ToString());
                if (infoResult.Success)
                {
                    Ob.exInfo = infoResult.Data;
                }

                this._FundingManager = new FundingHedgeManager();

                // 시작
                this._FundingManager.Start();
                this._SpikeManager = new SpikeScalpManager(SpikeScalpConfig.LoadOrDefault());
                CancellationToken cts = new CancellationToken();
                _ = this._SpikeManager.RunLoopAsync(cts);

              

                //var RestSvcThread = new Thread(new ThreadStart(RunRestService));
                //RestSvcThread.IsBackground = true;
                //RestSvcThread.Start();

                //Ob.thread_ExecDB = new SvcExecDB();


                
            }
            catch(Exception ex) {
                Ob.app._ERROR("StartUp", ex);
            }
        }
        private async Task Run()
        {
            try
            {
                Ob.IsInit = true;
             
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("RUN", ex);
            }

        }
        private static CancellationTokenSource _timerCts;
        private bool IsValidSymbol(string symbol)
        {
            // 1. ASCII만 허용 (한자/이모지 차단)
            if (symbol.Any(c => c > 127))
                return false;

            // 2. 정규식: 대문자+숫자만 + USDT
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                symbol, @"^[A-Z0-9]+USDT$"))
                return false;

            // 3. 길이 제한 (너무 긴 심볼 차단)
            if (symbol.Length > 15)
                return false;

            return true;
        }
        private async void RunRestService()
        {
            try
            {
                var hostConfigs = new HostConfiguration
                {
                    UrlReservations = new UrlReservations() { CreateAutomatically = true }
                };

                var Uris = new Uri[1];
                Uris[0] = new Uri("http://localhost:6070");
                //netsh http add urlacl url = "http://+:11010/" user = "Everyone"
                using (var host = new NancyHost(Uris))
                {
                    host.Start();

                    while (true)
                    {
                        await Task.Delay(5000);
                    }
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("RunRestService", ex);
            }
        }
        public void SetText(string text)
        {
            try
            {
                Logger logger = LogManager.GetLogger("ui");
                logger.Info(text);
            }
            catch
            {
            }
        }
        public void SetEnter(string text)
        {
            try
            {
                Logger logger = LogManager.GetLogger("enter");
                logger.Info(text);
            }
            catch
            {
            }
        }
    }
}
