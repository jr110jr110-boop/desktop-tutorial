using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// Takes the weekly progress snapshot by itself (offline: the server clock is the only input) and marks guarantees whose expiry date has passed as expired.
    /// It checks every thirty minutes; a snapshot per contract and data date is unique and the expiry is idempotent, so repeated or parallel runs are harmless.
    /// </summary>
    public class PcSnapshotHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<PcSnapshotHostedService> _logger;

        public PcSnapshotHostedService(IServiceScopeFactory scopes, ILogger<PcSnapshotHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (IServiceScope scope = _scopes.CreateScope())
                    {
                        IPcProgressService progress = scope.ServiceProvider.GetRequiredService<IPcProgressService>();
                        await progress.RunDueSnapshotsAsync(new PcSnapshotRunDto(), stoppingToken);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _logger?.LogWarning(ex, "Project control snapshot job failed; it will retry."); }

                // R1a: a guarantee whose expiry date has passed becomes Expired by itself (a date, not a decision); a failure of one job must not stop the other
                try
                {
                    using (IServiceScope scope = _scopes.CreateScope())
                    {
                        IPcCommercialService commercial = scope.ServiceProvider.GetRequiredService<IPcCommercialService>();
                        int expired = await commercial.ExpireGuaranteesAsync(stoppingToken);
                        if (expired > 0) _logger?.LogInformation("Project control: {Count} guarantee(s) expired.", expired);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _logger?.LogWarning(ex, "Project control guarantee expiry job failed; it will retry."); }
                try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
