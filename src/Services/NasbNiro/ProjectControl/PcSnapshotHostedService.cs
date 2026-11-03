using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// Takes the weekly progress snapshot by itself (offline: the server clock is the only input) and marks guarantees whose expiry date has passed as expired.
    /// It checks every thirty minutes; a snapshot per contract and data date is unique and the expiry is idempotent, so repeated or parallel runs are harmless.
    /// R1c (M6): once a day (after 01:00) it also evaluates the payment milestones of every contract (due dates and the finance status); R1c (H4, M4): once a day (after 02:00) it runs the progress engine for the contracts with a current baseline (the dated progress events) and freezes the planned value of a baseline that has no frozen rows. The whole job is switched off with the setting
    /// <c>Pc:Jobs=false</c> (environment variable <c>Pc__Jobs</c>), which the smoke run of the CI uses so that its figures do not depend on the clock of the job.
    /// </summary>
    public class PcSnapshotHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<PcSnapshotHostedService> _logger;
        private readonly IConfiguration _configuration;
        private DateTime _milestonesDoneOn = DateTime.MinValue;
        private DateTime _progressDoneOn = DateTime.MinValue;

        public PcSnapshotHostedService(IServiceScopeFactory scopes, ILogger<PcSnapshotHostedService> logger, IConfiguration configuration = null)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>The jobs run unless <c>Pc:Jobs</c> is the text "false" (same switch as <c>Ceo:Jobs</c>).</summary>
        internal static bool Enabled(IConfiguration configuration) => !string.Equals(configuration?["Pc:Jobs"], "false", StringComparison.OrdinalIgnoreCase);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // R1c (H1, H5): the one-time upgrade of existing data (a data change, not a job: it also runs when Pc:Jobs is false)
            try
            {
                using (IServiceScope scope = _scopes.CreateScope())
                    await PcR1Upgrade.EnsureAsync(scope.ServiceProvider.GetRequiredService<DataLayer.Context.IUnitOfWork>(), stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _logger?.LogWarning(ex, "Project control R1c upgrade failed; it will retry at the next start."); }
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!Enabled(_configuration))
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }
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

                // R1c (M6): the payment milestones (a date or a progress that was reached, an invoice that was posted or paid) are evaluated once a day, in the night
                DateTime now = DateTime.Now;
                if (now.Hour >= 1 && _milestonesDoneOn < now.Date)
                {
                    try
                    {
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            IPcCommercialService commercial = scope.ServiceProvider.GetRequiredService<IPcCommercialService>();
                            int due = await commercial.EvaluateAllMilestonesAsync(stoppingToken);
                            if (due > 0) _logger?.LogInformation("Project control: {Count} payment milestone(s) became due.", due);
                        }
                        _milestonesDoneOn = now.Date;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Project control milestone job failed; it will retry."); }
                }

                // R1c (H4, M4): once a day, in the night, the engine writes the progress events of the day and freezes the planned value of a baseline that has no frozen rows
                if (now.Hour >= 2 && _progressDoneOn < now.Date)
                {
                    try
                    {
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            IPcProgressService progress = scope.ServiceProvider.GetRequiredService<IPcProgressService>();
                            int contracts = await progress.NightlyAsync(stoppingToken);
                            _logger?.LogInformation("Project control: progress events written for {Count} contract(s).", contracts);
                        }
                        _progressDoneOn = now.Date;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Project control nightly progress job failed; it will retry."); }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
