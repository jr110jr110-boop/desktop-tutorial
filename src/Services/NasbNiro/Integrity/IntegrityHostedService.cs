using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Integrity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Integrity
{
    /// <summary>
    /// Package D: the verification of the audit chain every night at 02:30 (★; incremental from the checkpoint, a full pass once a week), and the reading of the result files of the monthly restore test
    /// (deploy/restore-test.ps1) as soon as they appear. Offline: the server clock is the only input. Idempotent and catch-up safe: it asks the database whether a scheduled run already happened today,
    /// so a server that was off at 02:30 verifies when it starts. Switch: Integrity:Jobs=false (the CI runs it by hand through the on-demand endpoint so the counts of the smoke stay exact).
    /// </summary>
    public class IntegrityHostedService : BackgroundService
    {
        /// <summary>★ The time of the nightly verification.</summary>
        public static readonly TimeSpan RunAt = new TimeSpan(2, 30, 0);

        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IntegrityHostedService> _logger;

        public IntegrityHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<IntegrityHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        public static bool Enabled(IConfiguration configuration) => !string.Equals(configuration?["Integrity:Jobs"], "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the scheduled run of <paramref name="now"/>'s day is due: it is past 02:30 and no scheduled run was made today.</summary>
        internal static bool Due(DateTime now, DateTimeOffset? lastScheduledRun, bool lastWasError = false, int scheduledRunsToday = 0)
        {
            if (now.TimeOfDay < RunAt) return false;
            if (!(lastScheduledRun.HasValue && lastScheduledRun.Value.LocalDateTime.Date == now.Date)) return true;
            // a run that could not be made (a database error) is tried again every hour, at most four times a day (the alert for it was sent at the first failure)
            return lastWasError && scheduledRunsToday < MaxRunsPerDay && now - lastScheduledRun.Value.LocalDateTime >= RetryAfter;
        }

        /// <summary>★ The wait before a run that failed is tried again, and the most scheduled runs of one day.</summary>
        public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);
        public const int MaxRunsPerDay = 4;

        /// <summary>One pass of the job; also called by the tests. Each step has its own scope, so one failing step does not stop the other.</summary>
        internal async Task RunOnceAsync(DateTime now, CancellationToken ct)
        {
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IRestoreTestService>().ImportAsync(ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger?.LogWarning(ex, "Integrity job (restore test results) failed; it will run again."); }
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISafetySettingsService>().SweepAsync(ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger?.LogWarning(ex, "Integrity job (safety requests) failed; it will run again."); }
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var lastRun = await uow.Set<AuditChainRun>().AsNoTracking().Where(r => !r.Manual).OrderByDescending(r => r.StartedAt).Select(r => new { r.StartedAt, r.Status }).FirstOrDefaultAsync(ct);
                DateTimeOffset? last = lastRun?.StartedAt;
                int today = 0;
                if (last.HasValue && last.Value.LocalDateTime.Date == now.Date)
                {
                    DateTimeOffset dayStart = new DateTimeOffset(now.Date, TimeZoneInfo.Local.GetUtcOffset(now.Date));
                    today = await uow.Set<AuditChainRun>().AsNoTracking().CountAsync(r => !r.Manual && r.StartedAt >= dayStart, ct);
                }
                if (!Due(now, last, lastRun?.Status == IntegrityRunStatus.Error, today)) return;
                var run = await scope.ServiceProvider.GetRequiredService<IAuditIntegrityService>().RunScheduledAsync(now, ct);
                if (run != null) _logger?.LogInformation("Integrity job: {Kind} verification of the audit chain, status {Status}, {Checked} entries checked.", run.Kind, run.Status, run.EntriesChecked);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger?.LogWarning(ex, "Integrity job (audit chain verification) failed; it will run again."); }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                if (Enabled(_configuration))
                {
                    try { await RunOnceAsync(DateTime.Now, stoppingToken); }
                    catch (OperationCanceledException) { return; }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
