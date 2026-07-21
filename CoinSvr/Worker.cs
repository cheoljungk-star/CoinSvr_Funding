using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Nancy.Bootstrapper;

namespace CoinSvr
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private IServiceScopeFactory _serviceScopeFactory;

        public Worker(ILogger<Worker> logger, IServiceScopeFactory serviceScopeFactory, IHostApplicationLifetime lifetime)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;

            lifetime.ApplicationStopping.Register(() => {
                // 호스트 종료 직전에 호출
                Ob.OrderBook_All.CleanupAsync().GetAwaiter().GetResult();
            });
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            var configuration = _serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<IConfiguration>();
            //_folderPaths = File.ReadAllLines(configuration["App.Configurations:ConfigurationFilePath"]).Select(x => x.Trim()).ToList();

            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                FrmMain main = new FrmMain();
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("ExecuteAsync", ex);
            }
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await Ob.OrderBook_All.CleanupAsync();
            await base.StopAsync(cancellationToken);
        }
    }
}
