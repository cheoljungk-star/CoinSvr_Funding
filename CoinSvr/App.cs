using CoinSvr;
using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Sockets;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Time;
using Org.BouncyCastle.Asn1.Crmf;
using Org.BouncyCastle.Ocsp;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CoinSvr
{
    public class App
    {
        public async Task<string> appKey(string Ip, string Body)
        {
            try
            {
                string Query = $"select * from access where MY_ID='{Ip}' AND Use_YN = '1'";
                
                if (Body != "")
                {
                    string Alias = Body;
                    Query = $"select * from access where Alias='{Alias}' AND Use_YN = '1'";
                }
                DataTable dt = await Ob.db.SelectQueryAsync(Query);
                if (dt.Rows.Count == 0) return "";
                var dict = new Dictionary<string, object>();
                dict.Add("Key", Ob.ENC_KEY);
                return JsonConvert.SerializeObject(dict, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "].appLogin(" + Ip.ToString() + ")", ex);
                return "";
            }
        }
        public async Task<string> appLogin(string Ip, string Body)
        {
            try
            {
                string Query = $"select * from access where MY_ID='{Ip}' AND Use_YN = '1'";
                string Alias = "";
                if (Body != "")
                {
                    Alias = Body;
                    Query = $"select * from access where Alias='{Alias}' AND Use_YN = '1'";
                }
                DataTable dt = await Ob.db.SelectQueryAsync(Query);
                if (dt.Rows.Count == 0)
                {
                    Ob.ui.SetText("appLogin [Fail] > " + Ip + " > " + Body);
                    return "";
                }

                DataRow row = dt.Rows[0];
                var dict = new Dictionary<string, object>();
                foreach (DataColumn col in row.Table.Columns)
                {
                    var val = row[col];
                    dict[col.ColumnName] = val == DBNull.Value ? "" : val;
                }
                Query = $"update access set Conn_dt = now() where MY_ID='{Ip}' AND Use_YN = '1'";
                if(Alias != "") Query = $"update access set Conn_dt = now() where Alias='{Alias}' AND Use_YN = '1'";
                await Ob.db.ExecuteQueryAsync(Query, true);
                return JsonConvert.SerializeObject(dict, Formatting.Indented);
            }
            catch(Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "].appLogin(" + Ip.ToString() + ")", ex);
                return "";
            }
        }
        public async Task<string> appAlive(string Ip, string Body)
        {
            try
            {
                string Query = $"select * from access where MY_ID='{Ip}' AND Use_YN = '1'";
                
                JObject Info = JObject.Parse(Body);
                JObject Data = JObject.Parse(Info["Data"].ToString());

                string Alias = Info["ID"].ToString();
                
                Query = $"select * from access where Alias='{Alias}' AND Use_YN = '1'";
                
                DataTable dt = await Ob.db.SelectQueryAsync(Query);
                if (dt.Rows.Count == 0)
                {
                    Ob.ui.SetText("appAlive [Fail] > " + Ip + " > " + Body);
                    return "";
                }

                DataRow row = dt.Rows[0];
                var dict = new Dictionary<string, object>();
                foreach (DataColumn col in row.Table.Columns)
                {
                    var val = row[col];
                    dict[col.ColumnName] = val == DBNull.Value ? "" : val;
                }
                Ob.ui.SetText("appAlive > " + Ip + " > " + Body);

                Query = $"update access set Conn_dt = now() where MY_ID='{Ip}' AND Use_YN = '1'";
                await Ob.db.ExecuteQueryAsync(Query, true);

                string nDate = Ob.app.NowTime().ToString("yyyyMMdd");
                
                double SwapBNB = Info["SwapBNB"] != null ? double.Parse(Info["SwapBNB"].ToString()) : 0;
                double Profit = double.Parse(Info["Profit"].ToString());
                double Bnb = double.Parse(Info["Bnb"].ToString());
                double Money = double.Parse(Info["Money"].ToString());
                double TranserFee = double.Parse(Info["TranserFee"].ToString());
                double Deposit = double.Parse(Info["Deposit"].ToString());
                string iQuery = "INSERT INTO AccountInfo (nDate, Alias, Today, TotalInitialMargin, TotalMaintenanceMargin, TotalWalletBalance, TotalUnrealizedProfit, TotalMarginBalance, TotalPositionInitialMargin, TotalOpenOrderInitialMargin, TotalCrossWalletBalance, TotalCrossUnrealizedPnl, AvailableBalance, MaxWithdrawQuantity, Swap_Bnb, Profit, Bnb, TranserFee, Deposit, update_dt)";
                iQuery += " VALUES(";
                iQuery += "'" + nDate + "' ";
                iQuery += ", '" + Alias + "' ";
                iQuery += ", " + Money + " ";
                iQuery += ", " + Data["TotalInitialMargin"].ToString() + "";
                iQuery += ", " + Data["TotalMaintenanceMargin"].ToString() + "";
                iQuery += ", " + Data["TotalWalletBalance"].ToString() + "";
                iQuery += ", " + Data["TotalUnrealizedProfit"].ToString() + "";
                iQuery += ", " + Data["TotalMarginBalance"].ToString() + "";
                iQuery += ", " + Data["TotalPositionInitialMargin"].ToString() + "";
                iQuery += ", " + Data["TotalOpenOrderInitialMargin"].ToString() + "";
                iQuery += ", " + Data["TotalCrossWalletBalance"].ToString() + "";
                iQuery += ", " + Data["TotalCrossUnrealizedPnl"].ToString() + "";
                iQuery += ", " + Data["AvailableBalance"].ToString() + "";
                iQuery += ", " + Data["MaxWithdrawQuantity"].ToString() + "";
                iQuery += ", " + SwapBNB + "";
                iQuery += ", " + Profit + "";
                iQuery += ", " + Bnb + "";
                iQuery += ", " + TranserFee + "";
                iQuery += ", " + Deposit + "";
                iQuery += "," + " now()";
                iQuery += " )";
                iQuery += " ON DUPLICATE KEY UPDATE ";
                iQuery += "Today=" + Money + ", TotalInitialMargin=" + Data["TotalInitialMargin"].ToString() + ", TotalMaintenanceMargin=" + Data["TotalMaintenanceMargin"].ToString() + ", TotalWalletBalance=" + Data["TotalWalletBalance"].ToString() + ", TotalUnrealizedProfit=" + Data["TotalUnrealizedProfit"].ToString() + ", TotalMarginBalance=" + Data["TotalMarginBalance"].ToString() + ", TotalPositionInitialMargin=" + Data["TotalPositionInitialMargin"].ToString() + ", TotalOpenOrderInitialMargin=" + Data["TotalOpenOrderInitialMargin"].ToString() + ", TotalCrossWalletBalance=" + Data["TotalCrossWalletBalance"].ToString() + ", TotalCrossUnrealizedPnl=" + Data["TotalCrossUnrealizedPnl"].ToString() + ", AvailableBalance=" + Data["AvailableBalance"].ToString() + ", Swap_Bnb=" + SwapBNB + ", MaxWithdrawQuantity=" + Data["MaxWithdrawQuantity"].ToString() + ", Profit=" + Profit + ", Bnb=" + Bnb + ", TranserFee=" + TranserFee + ", Deposit=" + Deposit + ", update_dt=now()";
                await Ob.db.ExecuteQueryAsync(iQuery);

                return JsonConvert.SerializeObject(dict, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "].appAlive(" + Ip.ToString() + ")", ex);
                return "";
            }
        }
        public async Task<double> DayPayment()
        {
            try
            {
                string startDate = DateTime.MinValue.ToString("yyyyMMdd");
                string Query = $"select * from accountinfo where Payment_Yn = '9' order by nDate desc limit 0, 1";
                DataTable dt = await Ob.db.SelectQueryAsync(Query);
                startDate = DateTime.MinValue.ToString("yyyyMMdd");
                if (dt.Rows.Count > 0)
                {
                    startDate = dt.Rows[0]["nDate"].ToString();
                }

                string endDate = Ob.app.NowTime().ToString("yyyyMMdd");

                Query = $"select * from accountinfo where nDate between '{startDate}' and '{endDate}' AND Alias = '{Ob.MY_ACCOUNT.Alias}' AND Payment_Yn in (0, 1)";
                dt = await Ob.db.SelectQueryAsync(Query);
                double Profit = 0;
                double Payment_Ratio = Ob.MY_ACCOUNT.PaymentRatio ?? 0;
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    try
                    {
                        string FeeDate = dt.Rows[i]["nDate"].ToString();
                        string Payment_Yn = dt.Rows[i]["Payment_Yn"].ToString();

                        if (Payment_Yn == "0" || endDate == FeeDate)
                        {
                            double NowTotalWalletBalance = (double)dt.Rows[i]["TotalWalletBalance"] + (double)dt.Rows[i]["Swap_Bnb"] + (double)dt.Rows[i]["TranserFee"] - (double)dt.Rows[i]["Deposit"];
                            string pDate = DateTime.ParseExact(FeeDate, "yyyyMMdd", CultureInfo.InvariantCulture).AddDays(-1).ToString("yyyyMMdd");
                            Query = $"select * from accountinfo where nDate = '{pDate}' AND Alias = '{Ob.MY_ACCOUNT.Alias}'";
                            DataTable dt2 = await Ob.db.SelectQueryAsync(Query);
                            if (dt2.Rows.Count > 0)
                            {
                                double TotalWalletBalance = (double)dt2.Rows[0]["TotalWalletBalance"];
                                double income = NowTotalWalletBalance - TotalWalletBalance;
                                if(endDate == FeeDate)
                                {
                                    Profit = (double)dt2.Rows[0]["Profit"] + income;
                                    string uuQuery = $"update accountinfo set Payment_Yn=1, Profit={Profit}, Payment_Amount=0 where nDate='{FeeDate}' AND Alias = '{Ob.MY_ACCOUNT.Alias}'";
                                    await Ob.db.ExecuteQueryAsync(uuQuery, true);
                                }
                                else
                                {
                                    Profit += income;
                                    string uuQuery = $"update accountinfo set Payment_Yn=1, Profit={Profit}, Payment_Amount=0 where nDate='{FeeDate}' AND Alias = '{Ob.MY_ACCOUNT.Alias}'";
                                    await Ob.db.ExecuteQueryAsync(uuQuery, true);
                                }
                                    
                            }
                        }
                        else
                        {
                            double income = (double)dt.Rows[i]["Profit"];
                            Profit += income;
                        }
                    }
                    catch (Exception ex)
                    {
                        Ob.app._ERROR("DayPayment > Select", ex);
                    }
                }
                return Profit;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("DayPayment", ex);
                return -1;
            }
        }
        public async Task<string> appPayment(string Ip, string Body)
        {
            try
            {
                Ob.ui.SetText("appPayment > " + Ip + " >> " + Body);

                JObject Info = JObject.Parse(Body);

                string Alias = Info["Alias"].ToString();
                string Payment_Yn = Info["Payment_Yn"].ToString();
                string Payment_Text = Info["Payment_Text"].ToString();

                string Query = $"update access set RunPayment={Payment_Yn}, Payment_Text='{Payment_Text}', Payment_dt=now() where Alias='{Alias}'";
                await Ob.db.ExecuteQueryAsync(Query);

                var dict = new Dictionary<string, object>();
                dict["result"] = "1";
                return JsonConvert.SerializeObject(dict, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "].appPayment(" + Ip.ToString() + ")", ex);
                return "";
            }
        }
        public void DeleteOldSignalFilesByFolderName(string parentFolder, int daysOld = 7)
        {
            try
            {
                string[] subFolders = Directory.GetDirectories(parentFolder);

                DateTime today = DateTime.Today;

                foreach (var folder in subFolders)
                {
                    string folderName = Path.GetFileName(folder);

                    if (DateTime.TryParseExact(folderName, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime folderDate))
                    {
                        double diffDays = (today - folderDate).TotalDays;

                        if (diffDays >= daysOld)
                        {
                            //Ob.ui.SetText($"[{folderName}] - {diffDays} 삭제");
                            
                            string[] signalFiles = Directory.GetFiles(folder, "*.signal", SearchOption.TopDirectoryOnly);

                            foreach (var file in signalFiles)
                            {
                                try
                                {
                                    File.Delete(file);
                                    Ob.ui.SetText($"삭제: {file}");
                                }
                                catch (Exception ex)
                                {
                                    Ob.ui.SetText($"삭제 실패: {file} => {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        
                    }
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "].DeleteOldSignalFilesByFolderName", ex);
            }
            if (!Directory.Exists(parentFolder))
            {
                Ob.ui.SetText($"폴더가 존재하지 않습니다: {parentFolder}");
                return;
            }

           
        }
        public DateTime NowTime()
        {
            try
            {
                //DateTime currentTime = serviceStartTime.Add(stopwatch.Elapsed);
                return TimeSource.Current.Time;
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "].NowTime", ex);
                return NowTime();
            }
        }
        public string AES_Encrypt(string value)
        {
            string password = Ob.ENC_KEY;
            return Encrypt(value, password);
        }
        public static string Encrypt(string plainText, string password)
        {
            try
            {
                RijndaelManaged aes = new RijndaelManaged();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var keybytes = Encoding.UTF8.GetBytes(password);
                byte[] iv = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

                aes.Key = keybytes;
                aes.IV = iv;

                var encrypt = aes.CreateEncryptor(aes.Key, aes.IV);
                byte[] xBuff = null;
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encrypt, CryptoStreamMode.Write))
                    {
                        byte[] xXml = Encoding.UTF8.GetBytes(plainText);
                        cs.Write(xXml, 0, xXml.Length);
                    }

                    xBuff = ms.ToArray();
                }

                System.String Output = Convert.ToBase64String(xBuff);
                return Output;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public string AES_Decrypt(string value)
        {
            string password = Ob.ENC_KEY;
            return Decrypt(value, password);
        }
        public static string Decrypt(string combinedString, string password)
        {
            try
            {
                RijndaelManaged aes = new RijndaelManaged();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var keybytes = Encoding.UTF8.GetBytes(password);
                byte[] iv = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

                aes.Key = keybytes;
                aes.IV = iv;
                var decrypt = aes.CreateDecryptor();
                byte[] xBuff = null;
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, decrypt, CryptoStreamMode.Write))
                    {
                        byte[] xXml = Convert.FromBase64String(combinedString);
                        cs.Write(xXml, 0, xXml.Length);
                    }

                    xBuff = ms.ToArray();
                }

                string Output = Encoding.UTF8.GetString(xBuff);
                return Output;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        private string BytesToString(byte[] Input)
        {
            try
            {
                System.Text.StringBuilder Result = new System.Text.StringBuilder(Input.Length * 2);
                string Part = null;
                foreach (byte b in Input)
                {
                    Part = Convert.ToString(b, 16).ToUpper();
                    if (Part.Length == 1)
                    {
                        Part = "0" + Part;
                    }
                    Result.Append(Part);
                }
                return Result.ToString();
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "]BytesToString", ex);
                return null;
            }

        }
        public async Task GeneListenKey()
        {
            // 1) listenKey 발급
            var listenKeyResult = await Ob.client.UsdFuturesApi.Account.StartUserStreamAsync();
            if (!listenKeyResult.Success)
            {
                Ob.ui.SetText("listenKey ERROR : " + listenKeyResult.Error);
                Ob._listenKey = "";
                return;
            }
            Ob._listenKey = listenKeyResult.Data;
            Ob.ui.SetText("listenKey SUCCESS : " + Ob._listenKey);
        }
        public async Task<string> RestExec(string param, string dBody)
        {
            try
            {
                var client = new RestClient();
                var request = new RestRequest("http://" + Ob.REMOTE_IP + ":6071/" + param, RestSharp.Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(dBody);

                var response = await client.ExecuteAsync(request, new CancellationTokenSource().Token);

                if (response.ErrorException != null)
                {
                    Ob.app._ERROR("[" + this.ToString() + "] RestExec ", response.ErrorException);
                }
                string message = response.Content;
                return message;
            }
            catch (Exception ex)
            {
                return "";
            }
        }
        public async Task StartMQ()
        {
            try
            {
                Ob.factory = new ConnectionFactory()
                {
                    UserName = "admin",
                    Password = "dkssud79!!",
                    VirtualHost = "/",
                    AutomaticRecoveryEnabled = true,
                    TopologyRecoveryEnabled = true
                };

                Ob.connection = await Ob.factory.CreateConnectionAsync("127.0.0.1");
                Ob.channel = await Ob.connection.CreateChannelAsync();

                var queueDeclareOk = await Ob.channel.QueueDeclareAsync();
                string QueueName = queueDeclareOk.QueueName;
                await Ob.channel.QueueBindAsync(queue: QueueName, exchange: Ob.MY_ACCOUNT.QueueName, routingKey: "");

                var consumer = new AsyncEventingBasicConsumer(Ob.channel);
                consumer.ReceivedAsync += Consumer_ReceivedAsync;

                Ob.channel.BasicConsumeAsync(queue: QueueName, autoAck: true, consumer: consumer);

                await Ob.channel.ExchangeDeclareAsync(exchange: Ob.MY_ACCOUNT.QueueName, type: "topic", durable: false);

                Ob.connection.CallbackExceptionAsync += Connection_CallbackExceptionAsync;
                Ob.connection.ConnectionBlockedAsync += Connection_ConnectionBlockedAsync;
                Ob.connection.ConnectionShutdownAsync += Connection_ConnectionShutdownAsync;
                Ob.connection.ConnectionUnblockedAsync += Connection_ConnectionUnblockedAsync;

                Ob.channel.BasicAcksAsync += Channel_BasicAcksAsync;
                Ob.channel.BasicNacksAsync += Channel_BasicNacksAsync;
                Ob.channel.BasicReturnAsync += Channel_BasicReturnAsync;
                Ob.channel.CallbackExceptionAsync += Channel_CallbackExceptionAsync;
                Ob.channel.FlowControlAsync += Channel_FlowControlAsync;

                Ob.MQ_INIT = 1;
            }
            catch (Exception ex)
            {
                Ob.MQ_INIT = 0;
                Ob.app._ERROR("[" + this.ToString() + "] StartMQ", ex);
            }
        }
        public async Task<string> AppAlive(string body)
        {
            try
            {
                string ret = await Ob.app.RestExec("/Alive", body);
                string RetVal = this.AES_Decrypt(ret);
                return ret;
            }
            catch
            {
                return "";
            }
        }
        private Task Consumer_ReceivedAsync(object sender, BasicDeliverEventArgs @event)
        {
            try
            {
                var body = @event.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                message = Ob.app.AES_Decrypt(message);
                JObject a = JObject.Parse(message);

                if (Ob.bot == null) return Task.CompletedTask;

                if (a["version"] != null)
                {
                    string symbol = a["symbol"].ToString();
                    bool ShouldTrade = (bool)a["ShouldTrade"];
                    decimal Confidence = (decimal)a["Confidence"];
                    decimal _maxConfidence24h = (decimal)a["_maxConfidence24h"];
                    string Direction = a["Direction"].ToString();
                    decimal Adx = (decimal)a["Adx"];
                    decimal Open1m = (decimal)a["Open1m"];
                    decimal RecentHigh5m = (decimal)a["RecentHigh5m"];
                    decimal RecentLow5m = (decimal)a["RecentLow5m"];
                    var st = Ob.bot.GetOrCreateState(symbol);
                    if (st != null)
                    {
                        st.ShouldTrade = ShouldTrade;
                        st.Confidence = Confidence;
                        st._maxConfidence24h = _maxConfidence24h;
                        st.Direction = Direction;
                        st.Adx = Adx;
                        st.Open1m = Open1m;
                        st.RecentHigh5m = RecentHigh5m;
                        st.RecentLow5m = RecentLow5m;
                    }
                }
                else
                {
                    string symbol = a["symbol"].ToString();
                    string signal = a["signal"].ToString();
                    string bbLower_15m = a["bbLower_15m"].ToString();
                    string bbMiddle_15m = a["bbMiddle_15m"].ToString();
                    string bbUpper_15m = a["bbUpper_15m"].ToString();
                    List<double> close = a["close"]!.ToObject<List<double>>();

                    bool isMacdImproving = (bool)a["isMacdImproving"];
                    bool isBBLowerHit = (bool)a["isBBLowerHit"];

                    string rsi_15m = a["rsi_15m"].ToString();


                    if (Ob.CoinHT.ContainsKey(symbol))
                    {
                        var o = (COIN_OBJECT_)Ob.CoinHT[symbol];
                        o.signal = int.Parse(signal);
                        o.close = close;
                        o.bbLower_15m = double.Parse(bbLower_15m);
                        o.bbMiddle_15m = double.Parse(bbMiddle_15m);
                        o.bbUpper_15m = double.Parse(bbUpper_15m);
                        o.isMacdImproving = isMacdImproving;
                        o.isBBLowerHit = isBBLowerHit;
                        o.rsi_15m = double.Parse(rsi_15m);
                    }
                }
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "] Consumer_ReceivedAsync", ex);
            }
            return Task.CompletedTask;

        }
        private Task Channel_FlowControlAsync(object sender, FlowControlEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }

        private Task Channel_CallbackExceptionAsync(object sender, CallbackExceptionEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }

        private Task Channel_BasicReturnAsync(object sender, BasicReturnEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }

        private Task Channel_BasicNacksAsync(object sender, BasicNackEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }

        private Task Channel_BasicAcksAsync(object sender, BasicAckEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }

        private Task Connection_ConnectionUnblockedAsync(object sender, AsyncEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }

        private Task Connection_ConnectionShutdownAsync(object sender, ShutdownEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }

        private Task Connection_ConnectionBlockedAsync(object sender, ConnectionBlockedEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }

        private Task Connection_CallbackExceptionAsync(object sender, CallbackExceptionEventArgs @event)
        {
            Ob.MQ_INIT = 0;
            Ob.ui.SetText("MQ >> ERROR >> " + @event.ToString());
            return Task.CompletedTask;
        }
        public string GetNowTime(string Type)
        {
            string returnTime = "";
            try
            {
                if (Type == "D")
                {
                    returnTime = Ob.app.NowTime().ToString("yyyy.MM.dd");
                }
                if (Type == "D2")
                {
                    returnTime = Ob.app.NowTime().ToString("yyyyMMdd");
                }
                if (Type == "D-")
                {
                    returnTime = Ob.app.NowTime().AddDays(-1).ToString("yyyyMMdd");
                }
                else if (Type == "T")
                {
                    returnTime = Ob.app.NowTime().ToString("HH:mm:ss");
                }
                else if (Type == "T2")
                {
                    returnTime = Ob.app.NowTime().ToString("HHmmss");
                }
                else if (Type == "S")
                {
                    returnTime = Ob.app.NowTime().ToString("mm:ss");
                }
                else if (Type == "S2")
                {
                    returnTime = Ob.app.NowTime().ToString("mmss");
                }
                else if (Type == "S3")
                {
                    returnTime = Ob.app.NowTime().ToString("ss");
                }
                else if (Type == "A")
                {
                    returnTime = Ob.app.NowTime().ToString("yyyy.MM.dd HH:mm:ss");
                }
                else if (Type == "A2")
                {
                    returnTime = Ob.app.NowTime().ToString("yyyyMMddHHmmss");
                }
                else if (Type == "K")
                {
                    returnTime = Ob.app.NowTime().ToString("yyMMddHHmmss");
                }
                else if (Type == "K2")
                {
                    returnTime = Ob.app.NowTime().ToString("yyMMddHHmmss") + Ob.app.NowTime().Millisecond.ToString().PadLeft(3, Convert.ToChar("0"));
                }
                else if (Type == "K3")
                {
                    returnTime = Ob.app.NowTime().ToString("yyyyMMddHHmmss") + Ob.app.NowTime().Millisecond.ToString().PadLeft(3, Convert.ToChar("0"));
                }
                else if (Type == "H")
                {
                    returnTime = Ob.app.NowTime().ToString("HHmm");
                }
            }
            catch { }
            return returnTime;
        }
        public void _ERROR(string func, Exception ex, string fatal = "0")
        {
            try
            {
                if (fatal == "0")
                {
                    Logger _logger = LogManager.GetLogger("error");
                    _logger.Error($"{func} >> {ex.Message}");
                    Console.WriteLine(ex);
                }
                else
                {
                    Logger _logger = LogManager.GetLogger("error");
                    _logger.Fatal($"{func} >> {ex.Message}");
                    Console.WriteLine(ex);
                }

            }
            catch { }
        }
    }
    public static class CloneHelper
    {
        public static T DeepClone<T>(T src)
        {
            // 직렬화 → 역직렬화
            var json = System.Text.Json.JsonSerializer.Serialize(src);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
        }
    }
}
