using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Production.Dnc
{
    /// <summary>
    /// Delivers the queued transfers of the server-driven links (network folder, FTP, serial converter) every few seconds and fetches the programs asked back from the machines. Offline: only machines on the LAN.
    /// Switch: the same Production:Jobs=false as the nightly production jobs (the smoke test runs the queue itself with POST dnc/transfers/run-pending). Interval: Dnc:PollSeconds (default 10).
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

        private bool Enabled => !string.Equals(_configuration?["Production:Jobs"], "false", StringComparison.OrdinalIgnoreCase);

        private TimeSpan Interval => int.TryParse(_configuration?["Dnc:PollSeconds"], out int s) && s >= 2 && s <= 600 ? TimeSpan.FromSeconds(s) : TimeSpan.FromSeconds(10);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled) return;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope scope = _scopes.CreateScope();
                    var r = await scope.ServiceProvider.GetRequiredService<IMfDncService>().ProcessPendingAsync(stoppingToken);
                    if (r.Processed > 0) _logger?.LogInformation("DNC: {Processed} transfers, {Delivered} delivered, {Received} received, {Failed} failed", r.Processed, r.Delivered, r.Received, r.Failed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { _logger?.LogWarning(ex, "DNC transfer job failed; it runs again shortly."); }
                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
