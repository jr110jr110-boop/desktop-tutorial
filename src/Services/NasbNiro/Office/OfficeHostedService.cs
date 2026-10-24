using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Office
{
    /// <summary>
    /// The daily jobs of the office module (offline: the server clock is the only input; both are idempotent). Shortly after 01:00 the attendance of the last finished days is computed,
    /// and the chairs of overdue meeting decisions are alerted. Switch: Office:Jobs=false.
    /// </summary>
    public class OfficeHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OfficeHostedService> _logger;

        public OfficeHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OfficeHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        internal static DateTime NextRun(DateTime now)
        {
            DateTime today = now.Date.AddHours(1);
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
                bool enabled = !string.Equals(_configuration?["Office:Jobs"], "false", StringComparison.OrdinalIgnoreCase);
                if (enabled)
                {
                    try
                    {
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            var result = await scope.ServiceProvider.GetRequiredService<IAttendanceService>().RunDailyAsync(stoppingToken);
                            _logger?.LogInformation("Attendance job: {Days} days for {Employees} employees", result.Days, result.Employees);
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Attendance job failed; it will run again tomorrow."); }
                    try
                    {
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            await scope.ServiceProvider.GetRequiredService<IMeetingService>().CheckOverdueAsync(stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Overdue decisions job failed; it will run again tomorrow."); }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
