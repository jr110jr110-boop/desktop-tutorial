using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Ceo
{
    /// <summary>
    /// The nightly job of the CEO dashboard, at 00:30 (offline: the server clock is the only input): it stores the snapshot of the day (see <see cref="ICeoSnapshotService"/>).
    /// It is idempotent per day, so a restart around the hour does no harm; a failure is logged and the job runs again the next night.
    /// Switch: Ceo:Jobs=false (the smoke tests run with it off and take the snapshot through the endpoint instead).
    /// </summary>
    public class CeoHostedService : BackgroundService
    {
        /// <summary>The time of the day the job runs.</summary>
        internal static readonly TimeSpan RunAt = new TimeSpan(0, 30, 0);

        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CeoHostedService> _logger;

        public CeoHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CeoHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>The next 00:30 after <paramref name="now"/> (today's when it is still ahead, else tomorrow's).</summary>
        internal static DateTime NextRun(DateTime now)
        {
            DateTime today = now.Date + RunAt;
            return now < today ? today : today.AddDays(1);
        }

        /// <summary>False when the switch Ceo:Jobs is "false".</summary>
        internal static bool Enabled(IConfiguration configuration) => !string.Equals(configuration?["Ceo:Jobs"], "false", StringComparison.OrdinalIgnoreCase);

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
                    try
                    {
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            int keys = await scope.ServiceProvider.GetRequiredService<ICeoSnapshotService>().RunAsync(DateTime.Today, stoppingToken);
                            _logger?.LogInformation("CEO snapshot job: {Keys} figures stored.", keys);
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "The CEO snapshot job failed; it will run again tomorrow night."); }
                }
                // the clock is past the hour now; wait a little so the same night is not run twice
                try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
