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
    /// The nightly job of the CEO dashboard, at 00:30 (offline: the server clock is the only input): it stores the snapshot of the day that has just ended, i.e. yesterday
    /// (see <see cref="ICeoSnapshotService"/>), so the last snapshot of a Persian month is its true month end. It is idempotent per day, so a restart around the hour does no harm.
    /// When the server starts and no row exists for yesterday (it was down at 00:30), the job stores yesterday once with the figures of that moment (a one-day catch-up).
    /// A failure is logged as an error and the job runs again the next night.
    /// Switch: Ceo:Jobs=false (the smoke tests run with it off and take the snapshot through the endpoint instead).
    /// </summary>
    public class CeoHostedService : BackgroundService
    {
        /// <summary>The time of the day the job runs.</summary>
        internal static readonly TimeSpan RunAt = new TimeSpan(0, 30, 0);

        /// <summary>The catch-up waits this long after start-up, so the application (migrations, seeding) has settled.</summary>
        internal static readonly TimeSpan CatchUpDelay = TimeSpan.FromSeconds(30);

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

        /// <summary>The day the job stores: the day that ended, i.e. the day before <paramref name="now"/>.</summary>
        internal static DateTime SnapshotDay(DateTime now) => now.Date.AddDays(-1);

        /// <summary>False when the switch Ceo:Jobs is "false".</summary>
        internal static bool Enabled(IConfiguration configuration) => !string.Equals(configuration?[CeoConfigKeys.Jobs], "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Stores the snapshot of the day that ended (<paramref name="catchUp"/> false: the night job, it replaces the day's values) or only when that day has none (<paramref name="catchUp"/> true: the start-up catch-up).
        /// A failure is logged as an error and returned as false; it never stops the service. Returns the number of keys stored (0 for a catch-up that had nothing to do or a failure).
        /// </summary>
        internal async Task<int> StoreAsync(DateTime now, bool catchUp, CancellationToken ct)
        {
            DateTime day = SnapshotDay(now);
            try
            {
                using (IServiceScope scope = _scopes.CreateScope())
                {
                    var snapshots = scope.ServiceProvider.GetRequiredService<ICeoSnapshotService>();
                    int keys = catchUp ? await snapshots.CatchUpAsync(day, ct) : await snapshots.RunAsync(day, ct);
                    if (keys > 0) _logger?.LogInformation("CEO snapshot {Kind}: {Keys} figures stored for {Day}.", catchUp ? "catch-up" : "job", keys, CeoText.Date(day));
                    return keys;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "The CEO snapshot {Kind} for {Day} failed; the nightly job will try again at the next run.", catchUp ? "catch-up" : "job", CeoText.Date(day));
                return 0;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                if (Enabled(_configuration))
                {
                    await Task.Delay(CatchUpDelay, stoppingToken);
                    await StoreAsync(DateTime.Now, catchUp: true, stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    DateTime next = NextRun(DateTime.Now);
                    while (!stoppingToken.IsCancellationRequested && DateTime.Now < next)
                    {
                        TimeSpan wait = next - DateTime.Now;
                        if (wait > TimeSpan.FromMinutes(30)) wait = TimeSpan.FromMinutes(30);
                        if (wait < TimeSpan.FromSeconds(1)) break;
                        await Task.Delay(wait, stoppingToken);
                    }
                    if (stoppingToken.IsCancellationRequested) return;
                    if (Enabled(_configuration)) await StoreAsync(DateTime.Now, catchUp: false, stoppingToken);
                    // the clock is past the hour now; wait a little so the same night is not run twice
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                }
            }
            catch (OperationCanceledException) { /* the host is stopping */ }
        }
    }
}
