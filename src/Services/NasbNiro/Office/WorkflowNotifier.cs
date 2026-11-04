using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ProjectControl;

namespace Services.NasbNiro.Office
{
    public enum NotificationChannel { Sms = 1, Email = 2 }

    /// <summary>One message for the optional SMS / e-mail gateway. It carries the user id only; the gateway looks the number or the address up itself, so no personal contact data passes through the application code.</summary>
    public class GatewayMessage
    {
        public NotificationChannel Channel { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
        public OfUrgency Priority { get; set; }
        public string SourceKey { get; set; }
    }

    public class GatewayResult
    {
        public bool Sent { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Decision D9: SMS and e-mail go through a pluggable OFFLINE gateway (a GSM modem, the internal mail server). The default implementation is <see cref="DisabledNotificationGateway"/> and makes no
    /// call at all; a site that owns a modem or a mail server registers its own implementation and switches the channels on in the project control settings.
    /// </summary>
    public interface INotificationGateway
    {
        string Name { get; }
        Task<GatewayResult> SendAsync(GatewayMessage message, CancellationToken cancellationToken);
    }

    public sealed class DisabledNotificationGateway : INotificationGateway
    {
        public string Name => "disabled";

        public Task<GatewayResult> SendAsync(GatewayMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(new GatewayResult { Sent = false, Reason = "gateway is not configured" });
    }

    /// <summary>
    /// The notifier of the task, progress-approval and register workflows: the in-app inbox item always (through <see cref="INotificationService"/>), and, when the company switched a channel on and the item is
    /// urgent enough, a message for the gateway. Nothing here throws and nothing blocks the caller.
    /// </summary>
    public interface IWorkflowNotifier
    {
        Task NotifyAsync(NotifyRequest request, CancellationToken cancellationToken);
        Task CloseAsync(string sourceKey, string reason, CancellationToken cancellationToken);
    }

    public class WorkflowNotifier : IWorkflowNotifier
    {
        private readonly IUnitOfWork _uow;
        private readonly INotificationService _inbox;
        private readonly INotificationGateway _gateway;

        public WorkflowNotifier(IUnitOfWork uow, INotificationService inbox, INotificationGateway gateway)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
            _gateway = gateway ?? new DisabledNotificationGateway();
        }

        public async Task NotifyAsync(NotifyRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return;
            await _inbox.NotifyAsync(request, cancellationToken);
            try
            {
                Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
                bool sms = settings[PcSettingKeys.NotifySmsEnabled] == 1m, email = settings[PcSettingKeys.NotifyEmailEnabled] == 1m;
                if ((!sms && !email) || (int)request.Priority < (int)settings[PcSettingKeys.NotifyMinPriority]) return;
                var users = new List<int>((request.UserIds ?? new List<int>()).Where(u => u > 0));
                List<int> positions = (request.PositionIds ?? new List<int>()).Where(p => p > 0).ToList();
                if (positions.Count > 0)
                    users.AddRange(await _uow.Set<OfPosition>().AsNoTracking().Where(p => positions.Contains(p.Id) && p.UserId != null).Select(p => p.UserId.Value).ToListAsync(cancellationToken));
                foreach (int user in users.Distinct())
                    foreach (NotificationChannel channel in new[] { NotificationChannel.Sms, NotificationChannel.Email })
                    {
                        if ((channel == NotificationChannel.Sms && !sms) || (channel == NotificationChannel.Email && !email)) continue;
                        await _gateway.SendAsync(new GatewayMessage { Channel = channel, UserId = user, Title = request.Title, Link = request.Link, Priority = request.Priority, SourceKey = request.SourceKey }, cancellationToken);
                    }
            }
            catch (Exception)
            {
                // the gateway must never block a workflow
            }
        }

        public Task CloseAsync(string sourceKey, string reason, CancellationToken cancellationToken) => _inbox.CloseAsync(sourceKey, reason, cancellationToken);
    }
}
