using System;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.Engineering
{
    /// <summary>Records the instant the engineering control started, once, at the first start of the program after the update (work created before it is legacy). It does nothing else.</summary>
    public class EnHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<EnHostedService> _logger;

        public EnHostedService(IServiceScopeFactory scopes, ILogger<EnHostedService> logger)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            for (int attempt = 0; attempt < 10 && !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    using IServiceScope scope = _scopes.CreateScope();
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    await EnControl.EnsureRecordedAsync(uow, null, stoppingToken);
                    return;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _logger?.LogWarning(ex, "Engineering activation record failed; it will be tried again."); }
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
