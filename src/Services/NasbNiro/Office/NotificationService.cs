using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.Contracts.Identity;

namespace Services.NasbNiro.Office
{
    /// <summary>What a gate of another module asks the inbox to show. Recipients: roles (every holder sees the item), users or positions.</summary>
    public class NotifyRequest
    {
        /// <summary>Identifies the gate, for example "pr:15" or "voucher:301". The same key never creates a second open item for the same recipient, and closes together.</summary>
        public string SourceKey { get; set; }

        public string Module { get; set; }
        public string SourceType { get; set; }
        public int? SourceId { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
        public OfInboxKind Kind { get; set; } = OfInboxKind.Approval;
        public OfUrgency Priority { get; set; }
        public DateTime? DueDate { get; set; }

        /// <summary>Role names separated by commas.</summary>
        public string Roles { get; set; }

        public List<int> UserIds { get; set; } = new List<int>();
        public List<int> PositionIds { get; set; } = new List<int>();

        /// <summary>Close the open items of this key whose recipient is not in the lists above (a gate with several steps: the step that was passed leaves the inbox).</summary>
        public bool ReplaceRecipients { get; set; }
    }

    /// <summary>
    /// The unified inbox for the other modules. None of the methods throws and none of them blocks a gate: a problem while writing the inbox item
    /// (or sending the optional e-mail) is ignored, so procurement, finance and quality flows always go on. Call after the gate's own save.
    /// </summary>
    public interface INotificationService
    {
        Task NotifyAsync(NotifyRequest request, CancellationToken cancellationToken);

        /// <summary>Closes every open item of the gate (the gate was passed, rejected or cancelled).</summary>
        Task CloseAsync(string sourceKey, string reason, CancellationToken cancellationToken);

        /// <summary>Open the gate's items while it waits, close them when it does not.</summary>
        Task GateAsync(string sourceKey, bool waiting, NotifyRequest request, CancellationToken cancellationToken);
    }

    public class NotificationService : INotificationService
    {
        private readonly IUnitOfWork _uow;
        private readonly IConfiguration _configuration;
        private readonly IEmailSender _email;

        public NotificationService(IUnitOfWork uow, IConfiguration configuration, IEmailSender email)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _configuration = configuration;
            _email = email;
        }

        private static string Cut(string s, int n) => s == null ? null : (s.Length <= n ? s : s.Substring(0, n));

        public async Task NotifyAsync(NotifyRequest request, CancellationToken cancellationToken)
        {
            var added = new List<OfInboxItem>();
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.SourceKey) || string.IsNullOrWhiteSpace(request.Title)) return;
                string key = Cut(request.SourceKey.Trim(), 200);
                List<string> roles = (request.Roles ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
                List<int> users = (request.UserIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
                List<int> positions = (request.PositionIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
                if (roles.Count + users.Count + positions.Count == 0)
                {
                    if (request.ReplaceRecipients) await CloseAsync(key, null, cancellationToken);
                    return;
                }
                List<OfInboxItem> open = await _uow.Set<OfInboxItem>().Where(i => i.SourceKey == key && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)).ToListAsync(cancellationToken);
                DateTimeOffset now = DateTimeOffset.Now;
                OfInboxItem Make() => new OfInboxItem
                {
                    Kind = request.Kind, Title = Cut(request.Title.Trim(), 300), SourceModule = Cut(request.Module, 50), SourceType = Cut(request.SourceType, 50), SourceId = request.SourceId,
                    SourceKey = key, Link = Cut(request.Link, 1000), DueDate = request.DueDate?.Date, Priority = request.Priority, Status = OfInboxStatus.Unread, CreatedAt = now,
                };
                foreach (string role in roles)
                    if (!open.Any(i => i.RecipientRole == role)) { OfInboxItem item = Make(); item.RecipientRole = role; added.Add(item); }
                foreach (int user in users)
                    if (!open.Any(i => i.RecipientUserId == user)) { OfInboxItem item = Make(); item.RecipientUserId = user; added.Add(item); }
                foreach (int position in positions)
                    if (!open.Any(i => i.RecipientPositionId == position)) { OfInboxItem item = Make(); item.RecipientPositionId = position; added.Add(item); }
                bool closedAny = false;
                if (request.ReplaceRecipients)
                {
                    foreach (OfInboxItem i in open)
                    {
                        bool keep = (i.RecipientRole != null && roles.Contains(i.RecipientRole)) || (i.RecipientUserId.HasValue && users.Contains(i.RecipientUserId.Value))
                            || (i.RecipientPositionId.HasValue && positions.Contains(i.RecipientPositionId.Value));
                        if (keep) continue;
                        i.Status = OfInboxStatus.Done; i.ClosedAt = now; i.ClosedReason = "مرحله تایید گذشت"; closedAny = true;
                    }
                }
                if (added.Count == 0 && !closedAny) return;
                _uow.Set<OfInboxItem>().AddRange(added);
                await _uow.SaveChangesAsync(cancellationToken);
                if (added.Count > 0) await QueueEmailsAsync(users, positions, request, cancellationToken);
            }
            catch (Exception)
            {
                foreach (OfInboxItem item in added)
                {
                    try { _uow.Entry(item).State = EntityState.Detached; }
                    catch (Exception) { /* the inbox must never break the caller */ }
                }
            }
        }

        public async Task CloseAsync(string sourceKey, string reason, CancellationToken cancellationToken)
        {
            List<OfInboxItem> items = null;
            try
            {
                if (string.IsNullOrWhiteSpace(sourceKey)) return;
                string key = Cut(sourceKey.Trim(), 200);
                items = await _uow.Set<OfInboxItem>().Where(i => i.SourceKey == key && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)).ToListAsync(cancellationToken);
                if (items.Count == 0) return;
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (OfInboxItem item in items)
                {
                    item.Status = OfInboxStatus.Done;
                    item.ClosedAt = now;
                    item.ClosedReason = Cut(reason, 1000) ?? "مرحله تایید گذشت";
                }
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (Exception)
            {
                if (items != null)
                    foreach (OfInboxItem item in items)
                    {
                        try { _uow.Entry(item).State = EntityState.Detached; }
                        catch (Exception) { /* ignored */ }
                    }
            }
        }

        public Task GateAsync(string sourceKey, bool waiting, NotifyRequest request, CancellationToken cancellationToken)
        {
            if (waiting && request != null)
            {
                request.SourceKey = sourceKey;
                return NotifyAsync(request, cancellationToken);
            }
            return CloseAsync(sourceKey, null, cancellationToken);
        }

        /// <summary>Optional internal SMTP e-mail (off unless Office:EmailNotifications is true). It is sent in the background and its failure is ignored.</summary>
        private async Task QueueEmailsAsync(List<int> users, List<int> positions, NotifyRequest request, CancellationToken cancellationToken)
        {
            if (_email == null || !string.Equals(_configuration?["Office:EmailNotifications"], "true", StringComparison.OrdinalIgnoreCase)) return;
            var ids = new List<int>(users);
            if (positions.Count > 0)
                ids.AddRange(await _uow.Set<OfPosition>().AsNoTracking().Where(p => positions.Contains(p.Id) && p.UserId != null).Select(p => p.UserId.Value).ToListAsync(cancellationToken));
            List<string> emails = await _uow.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id) && u.Email != null).Select(u => u.Email).Distinct().ToListAsync(cancellationToken);
            if (emails.Count == 0) return;
            IEmailSender sender = _email;
            string subject = Cut(request.Title, 200);
            string body = "<div dir=\"rtl\">" + System.Net.WebUtility.HtmlEncode(request.Title) + "</div>";
            _ = Task.Run(async () =>
            {
                foreach (string address in emails)
                {
                    try { await sender.SendEmailAsync(address, subject, body); }
                    catch (Exception) { /* offline SMTP must not matter */ }
                }
            });
        }
    }
}
