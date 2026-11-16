using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.NasbNiro.EngInbox
{
    /// <summary>
    /// At start the program creates the inbox folder and its sub-folders (Platforms, Projects, _Processed, _Rejected, _Templates with the empty template and the sample package) when they are missing. When it cannot (no disk,
    /// no rights) it writes a clear warning and stays healthy: the source reports "not configured", the ingest step does nothing and is no error. The scan itself runs as the agent step ENG.INBOX.INGEST.
    /// </summary>
    public class EngInboxHostedService : BackgroundService
    {
        private readonly IEngineeringSource _source;
        private readonly ILogger<EngInboxHostedService> _logger;

        public EngInboxHostedService(IEngineeringSource source, ILogger<EngInboxHostedService> logger)
        {
            _source = source;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                bool ok = _source.IsConfigured;
                if (ok) _logger?.LogInformation("Engineering inbox ready ({Source}): {Location}", _source.Name, _source.Location);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Engineering inbox could not be prepared. صندوق ورودی مهندسی آماده نشد.");
            }
            return Task.CompletedTask;
        }
    }
}
