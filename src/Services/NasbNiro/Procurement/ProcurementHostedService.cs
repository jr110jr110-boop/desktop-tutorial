using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Procurement
{
    /// <summary>
    /// PB6 (M6): the daily guarantee alert (marks the guarantees that passed their expiry and alerts the ones that are near it). It is OFF unless Procurement:GuaranteeJob is "true",
    /// so tests and CI never run it. The run is idempotent; the server clock is the only input (offline).
    /// </summary>
    public class ProcurementHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProcurementHostedService> _logger;

        public ProcurementHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ProcurementHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool enabled = string.Equals(_configuration?["Procurement:GuaranteeJob"], "true", StringComparison.OrdinalIgnoreCase);
                if (enabled)
                {
                    try
                    {
                        using IServiceScope scope = _scopes.CreateScope();
                        var result = await scope.ServiceProvider.GetRequiredService<ISupplierContractService>().RunGuaranteeAlertsJobAsync(stoppingToken);
                        _logger?.LogInformation("Guarantee alert job: {Alerted} alerted, {Expired} expired", result.Data?.Alerted, result.Data?.MarkedExpired);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Guarantee alert job failed; it will run again."); }
                }
                try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
