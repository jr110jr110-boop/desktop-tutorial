using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Quality
{
    /// <summary>
    /// The nightly job of the quality system, shortly after 04:00 (offline: the server clock is the only input; every step is idempotent, so a missed night is caught up the next one):
    /// late CAPAs are escalated to QA and, after the company-set number of days, to the CEO (R1q D13), and the document revisions approved with a future effective date become
    /// effective on their day (R1q D41). Switch: Quality:Jobs=false (the smoke tests switch it off so the fixed test data stays untouched).
    /// </summary>
    public class QualityHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<QualityHostedService> _logger;

        public QualityHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<QualityHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>The next 04:00 after <paramref name="now"/>.</summary>
        internal static DateTime NextRun(DateTime now)
        {
            DateTime today = now.Date.AddHours(4);
            return now < today ? today : today.AddDays(1);
        }

        /// <summary>True unless the configuration switches the job off (Quality:Jobs=false).</summary>
        internal static bool Enabled(IConfiguration configuration) =>
            !string.Equals(configuration?["Quality:Jobs"], "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>One run of the job, also called by the tests and by a manual trigger. Each step has its own scope, so one failing step does not stop the other.</summary>
        internal async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<ICapaService>().EscalateAsync(cancellationToken);
                if (result.FirstRun) _logger?.LogInformation("Quality job: first run, the level of {Recorded} late CAPA(s) was recorded without telling anybody", result.Recorded);
                else _logger?.LogInformation("Quality job: {Escalated} CAPA escalated to QA, {Ceo} to the CEO, {Skipped} left for the next run", result.Escalated, result.CeoNotified, result.Skipped);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger?.LogWarning(ex, "Quality job (CAPA escalation) failed; it will run again tomorrow."); }
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                int promoted = await scope.ServiceProvider.GetRequiredService<IDocumentControlService>().PromoteScheduledAsync(cancellationToken);
                if (promoted > 0) _logger?.LogInformation("Quality job: {Promoted} scheduled document revision(s) became effective", promoted);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger?.LogWarning(ex, "Quality job (document revisions) failed; it will run again tomorrow."); }
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
                if (Enabled(_configuration))
                {
                    try { await RunOnceAsync(stoppingToken); }
                    catch (OperationCanceledException) { return; }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
