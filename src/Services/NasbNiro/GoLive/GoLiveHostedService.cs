using System;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.GoLive
{
    /// <summary>
    /// Package G. (1) At the first start after the update it records the cut-over instant once (the day the new controls started; no migration data step is needed). It retries until the database answers (the server may start before the
    /// database). (2) Every few hours it sweeps the purchase order lines whose cited drawing revision was superseded (alerts to the buyer and engineering). The sweep is OFF when GoLive:Jobs is "false" (CI); the activation record is
    /// always written.
    /// </summary>
    public class GoLiveHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoLiveHostedService> _logger;

        public GoLiveHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<GoLiveHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            bool recorded = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope scope = _scopes.CreateScope();
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    if (!recorded) { await CutOver.EnsureRecordedAsync(uow, null, stoppingToken); recorded = true; }
                    if (!string.Equals(_configuration?["GoLive:Jobs"], "false", StringComparison.OrdinalIgnoreCase))
                        await PoDrawingCitation.SyncAlertsAsync(uow, scope.ServiceProvider.GetService<INotificationService>(), null, stoppingToken);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _logger?.LogWarning(ex, "Go-live job failed; it will run again."); }
                try { await Task.Delay(recorded ? TimeSpan.FromHours(3) : TimeSpan.FromSeconds(20), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
