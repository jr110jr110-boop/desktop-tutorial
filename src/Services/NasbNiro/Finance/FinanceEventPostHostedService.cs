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
    /// Turns the pending accounting events into ledger vouchers by itself and drafts sales invoices of due contract milestones
    /// (offline: the server clock is the only input). Both jobs are idempotent. It can be switched off with Finance:AutoPost=false.
    /// </summary>
    public class FinanceEventPostHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FinanceEventPostHostedService> _logger;

        public FinanceEventPostHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<FinanceEventPostHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
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
                            await scope.ServiceProvider.GetRequiredService<IEventPostingService>().PostPendingAsync(stoppingToken);
                        }
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            await scope.ServiceProvider.GetRequiredService<IReceivablesService>().DraftFromMilestonesAsync(stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Finance auto-post job failed; it will retry."); }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
