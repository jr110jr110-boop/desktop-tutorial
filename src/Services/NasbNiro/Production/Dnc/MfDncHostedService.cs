using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Production.Dnc
{
    /// <summary>
    /// Delivers the queued transfers of the server-driven links (network folder, FTP, serial converter) every few seconds and fetches the programs asked back from the machines; then polls the live monitoring (P2 part B). Offline: only machines on the LAN.
    /// Switch: the same Production:Jobs=false as the nightly production jobs (the smoke test runs the queue itself with POST dnc/transfers/run-pending). Loop interval: Dnc:PollSeconds (default 2; each monitor keeps its own poll interval, 10 s unless set).
    /// A failure is logged and the loop goes on; it never stops the host.
    /// </summary>
    public class MfDncHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MfDncHostedService> _logger;

        public MfDncHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<MfDncHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>The machines a worker is busy with right now.</summary>
        private readonly ConcurrentDictionary<int, byte> _busy = new ConcurrentDictionary<int, byte>();

        private async Task WorkMachineAsync(int machine, CancellationToken stoppingToken)
        {
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                var r = await scope.ServiceProvider.GetRequiredService<IMfDncService>().ProcessPendingAsync(machine, stoppingToken);
                if (r.Processed > 0) _logger?.LogInformation("DNC machine {Machine}: {Processed} transfers, {Delivered} delivered, {Received} received, {Failed} failed", machine, r.Processed, r.Delivered, r.Received, r.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { _logger?.LogWarning(ex, "DNC transfers of machine {Machine} failed; they run again shortly.", machine); }
            finally { _busy.TryRemove(machine, out _); }
        }

        private bool Enabled => !string.Equals(_configuration?["Production:Jobs"], "false", StringComparison.OrdinalIgnoreCase);

        private TimeSpan Interval => int.TryParse(_configuration?["Dnc:PollSeconds"], out int s) && s >= 2 && s <= 600 ? TimeSpan.FromSeconds(s) : TimeSpan.FromSeconds(2);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled) return;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // review finding 6: every machine on its own worker (own scope), one at a time per machine; a long serial transfer to one machine never holds the others
                    List<int> machines;
                    using (IServiceScope scope = _scopes.CreateScope())
                        machines = await scope.ServiceProvider.GetRequiredService<IMfDncService>().PendingMachinesAsync(stoppingToken);
                    foreach (int machine in machines)
                        if (_busy.TryAdd(machine, 0)) _ = Task.Run(() => WorkMachineAsync(machine, stoppingToken), CancellationToken.None);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { _logger?.LogWarning(ex, "DNC transfer job failed; it runs again shortly."); }
                try
                {
                    // P2 part B: the live monitoring (each monitor is polled at its own interval; silent gateways go offline)
                    using IServiceScope scope = _scopes.CreateScope();
                    var m = await scope.ServiceProvider.GetRequiredService<IMfMonitorService>().PollAsync(stoppingToken);
                    if (m.Changed > 0) _logger?.LogInformation("Monitor: {Changed} state changes, {Alarms} alarms, {Offline} offline", m.Changed, m.Alarms, m.Offline);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { _logger?.LogWarning(ex, "Machine monitoring poll failed; it runs again shortly."); }
                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
