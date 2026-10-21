using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// The monthly jobs of release F2 (offline: the server clock is the only input; both are idempotent): the depreciation of the last finished Persian month is drafted
    /// (a person still approves it) and the Sepidar packages of that month are drafted. It follows the same switch as the other finance jobs (Finance:AutoPost=false).
    /// </summary>
    public class FinanceF2HostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FinanceF2HostedService> _logger;

        public FinanceF2HostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<FinanceF2HostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                bool enabled = !string.Equals(_configuration?["Finance:AutoPost"], "false", StringComparison.OrdinalIgnoreCase);
                if (enabled)
                {
                    try
                    {
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            string result = await scope.ServiceProvider.GetRequiredService<IAssetService>().DraftLastMonthAsync(stoppingToken);
                            _logger?.LogInformation("Depreciation job: {Result}", result);
                        }
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            string result = await scope.ServiceProvider.GetRequiredService<ISepidarOutboxService>().GenerateMonthlyAsync(stoppingToken);
                            _logger?.LogInformation("Sepidar outbox job: {Result}", result);
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Finance monthly job failed; it will retry."); }
                }
                try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
