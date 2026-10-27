using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Production
{
    /// <summary>
    /// The nightly jobs of the production module, shortly after 02:00 (offline: the server clock is the only input; every job is idempotent and a failure of one does not stop the others).
    /// 1. the maintenance plans that are due get a draft order, 2. the finite-capacity scheduler runs again, 3. the cost of the closed time bookings is posted, 4. the labour of protected groups
    /// (fewer than three payroll-rated employees) of every ended month is posted as the monthly aggregate.
    /// Switch: Production:Jobs=false. Lead days of the maintenance plans: Production:MaintenanceLeadDays (default 7).
    /// </summary>
    public class ProductionHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProductionHostedService> _logger;

        public ProductionHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ProductionHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        internal static DateTime NextRun(DateTime now)
        {
            DateTime today = now.Date.AddHours(2);
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
                if (!string.Equals(_configuration?["Production:Jobs"], "false", StringComparison.OrdinalIgnoreCase))
                {
                    if (!await RunAsync("maintenance plans", async sp => { var r = await sp.GetRequiredService<IMfMaintenanceService>().DraftDueNightlyAsync(stoppingToken); _logger?.LogInformation("Maintenance job: {Drafted} orders drafted from {Plans} plans", r.Drafted, r.PlansChecked); }, stoppingToken)) return;
                    if (!await RunAsync("scheduler", async sp => { var r = await sp.GetRequiredService<IMfScheduleService>().RunNightlyAsync(stoppingToken); _logger?.LogInformation("Scheduler job: {Orders} orders, {Late} late", r.Orders, r.LateOrders); }, stoppingToken)) return;
                    if (!await RunAsync("cost posting", async sp => { var r = await sp.GetRequiredService<IMfCostService>().RunNightlyAsync(stoppingToken); _logger?.LogInformation("Cost job: {Posted} bookings posted, {Skipped} skipped", r.BookingsPosted, r.BookingsSkipped); }, stoppingToken)) return;
                    // R1p D1: after the daily posting, the labour of protected groups of every ended month is posted as the monthly aggregate (the first nights of a month post the month before)
                    if (!await RunAsync("labour month", async sp => { int months = await sp.GetRequiredService<IMfLabourService>().RunNightlyAsync(stoppingToken); _logger?.LogInformation("Labour month job: {Months} months posted", months); }, stoppingToken)) return;
                }
                try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>False when the host is stopping.</summary>
        private async Task<bool> RunAsync(string name, Func<IServiceProvider, Task> job, CancellationToken stoppingToken)
        {
            try
            {
                using (IServiceScope scope = _scopes.CreateScope()) await job(scope.ServiceProvider);
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { _logger?.LogWarning(ex, "Production job '{Job}' failed; it will run again tomorrow.", name); }
            return true;
        }
    }
}
