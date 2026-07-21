using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nancy;
using Nancy.Json;
using Newtonsoft.Json;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Nancy.Hosting.Self;
using System.Threading;
using Nancy.Bootstrapper;
using System.Collections;
using NLog;
using CoinSvr;
using Nancy.Extensions;

namespace CoinSvr
{
    public class RestController : NancyModule
    {
        public RestController()
        {
            try
            {
                After.AddItemToEndOfPipeline((ctx) =>
                {
                    ctx.Response.WithHeader("Access-Control-Allow-Origin", "*")
                        .WithHeader("Access-Control-Allow-Methods", "POST,GET")
                        .WithHeader("Access-Control-Allow-Headers", "Accept, Origin, Content-type");
                });


                Get("/", x => {
                    var clientIp = this.Request.UserHostAddress;
                    return string.Concat("Rest Service");
                });

                Post("/", x => {
                    var clientIp = this.Request.UserHostAddress;
                    var bodyStream = this.Request.Body;
                    var jsonBody = new System.IO.StreamReader(bodyStream).ReadToEnd();

                    return string.Concat("Rest Service");
                });
                
                Post("/Key", async x => {
                    var clientIp = this.Request.UserHostAddress;
                    var bodyStream = this.Request.Body;
                    var jsonBody = new System.IO.StreamReader(bodyStream).ReadToEnd();
                    var ret = await Ob.app.appKey(clientIp, jsonBody);
                    return ret;
                });

                Post("/Login", async x => {
                    var clientIp = this.Request.UserHostAddress;
                    var bodyStream = this.Request.Body;
                    var jsonBody = new System.IO.StreamReader(bodyStream).ReadToEnd();
                    var ret = await Ob.app.appLogin(clientIp, jsonBody);
                    ret = Ob.app.AES_Encrypt(ret);
                    return ret;
                });

                Post("/Alive", async x => {
                    var clientIp = this.Request.UserHostAddress;
                    var bodyStream = this.Request.Body;
                    var jsonBody = new System.IO.StreamReader(bodyStream).ReadToEnd();
                    var ret = await Ob.app.appAlive(clientIp, jsonBody);
                    ret = Ob.app.AES_Encrypt(ret);
                    return ret;
                });

                Post("/Payment", async x => {
                    var clientIp = this.Request.UserHostAddress;
                    var bodyStream = this.Request.Body;
                    var jsonBody = new System.IO.StreamReader(bodyStream).ReadToEnd();
                    var ret = await Ob.app.appPayment(clientIp, jsonBody);
                    return ret;
                });
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("BotController", ex);
            }
        }
    }

    public class Bootstrapper : DefaultNancyBootstrapper
    {
        /// <summary>
        /// Register only NancyModules found in this assembly
        /// </summary>
        protected override IEnumerable<ModuleRegistration> Modules
        {
            get
            {
                return GetType().Assembly.GetTypes().Where(type => type.BaseType == typeof(NancyModule)).Select(type => new ModuleRegistration(type));
            }
        }
    }
}


