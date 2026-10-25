using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.AfterSales
{
    /// <summary>
    /// The nightly job of after-sales service, shortly after 03:00 (offline: the server clock is the only input; idempotent): the periodic visits of the active service agreements that are due
    /// become draft service orders, delivery surveys are drafted for old handovers, ended agreements expire and the SLA-breach alerts of the open requests are refreshed.
    /// Switch: Service:Jobs=false. Lead days: the settings VisitLeadDays and SurveyLeadDays (default 30 each).
    /// </summary>
    public class SvHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SvHostedService> _logger;

        public SvHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<SvHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        internal static DateTime NextRun(DateTime now)
        {
            DateTime today = now.Date.AddHours(3);
            return now < today ? today : today.AddDays(1);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime next = NextRun(DateTime.Now);
                while (!stoppingToken.IsCancellationRequested && DateTime.Now < next)
                {
                    TimeSpan wait = next - DateTime.Now;
                    if (wait > TimeSpan.FromMinutes(30)) wait = TimeSpan.FromMinutes(30);
                    if (wait < TimeSpan.FromSeconds(1)) break;
                    try { await Task.Delay(wait, stoppingToken); }
                    catch (OperationCanceledException) { return; }
                }
                if (stoppingToken.IsCancellationRequested) return;
                if (!string.Equals(_configuration?["Service:Jobs"], "false", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using IServiceScope scope = _scopes.CreateScope();
                        var r = await scope.ServiceProvider.GetRequiredService<ISvContractService>().RunNightlyAsync(stoppingToken);
                        _logger?.LogInformation("Service job: {Visits} visits drafted, {Surveys} surveys drafted, {Expired} agreements expired, {Breaches} SLA alerts", r.VisitsDrafted, r.SurveysDrafted, r.ContractsExpired, r.BreachAlerts);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Service job failed; it will run again tomorrow."); }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
