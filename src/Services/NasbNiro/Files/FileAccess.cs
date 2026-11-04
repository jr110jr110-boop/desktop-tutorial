using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.Identity.AccountServices;
using Services.NasbNiro.Inventory;
using Services.Services;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.Files
{
    public enum FileVerdictKind
    {
        /// <summary>The request may go on (for a change: the document service decides what is left, including the lock of the file).</summary>
        Allowed = 0,
        NotFound = 1,
        Denied = 2,
    }

    /// <summary>The answer of the per-file access check. The text of a refusal never names the owner of the file or what is in it.</summary>
    public sealed class FileVerdict
    {
        public FileVerdictKind Kind { get; private set; }

        public string Message { get; private set; }

        /// <summary>A record of some module holds the file (a letter, a voucher, a quality record, a leave request ...).</summary>
        public bool Owned { get; private set; }

        /// <summary>The file belongs to a letter: the letter's own read rule decided.</summary>
        public bool LetterFile { get; private set; }

        public bool IsAllowed => Kind == FileVerdictKind.Allowed;

        internal static FileVerdict Allow(bool owned = false, bool letter = false) =>
            new FileVerdict { Kind = FileVerdictKind.Allowed, Owned = owned, LetterFile = letter };

        internal static FileVerdict Deny(string message, bool owned = false, bool letter = false) =>
            new FileVerdict { Kind = FileVerdictKind.Denied, Message = message, Owned = owned, LetterFile = letter };

        internal static FileVerdict Missing() => new FileVerdict { Kind = FileVerdictKind.NotFound, Message = FileText.NotFound };
    }

    internal static class FileText
    {
        public const string NotFound = "فایلی یافت نشد";

        /// <summary>Contains «مجوز» like the other refusals, so one reader of the body recognises it.</summary>
        public const string NoReadAccess = "شما مجوز دسترسی به این فایل را ندارید.";


    }

    /// <summary>
    /// R1t D8: who may read and who may change a stored file. The file is resolved to the records that hold it and the rule of the owning module decides;
    /// a file that no record holds is the uploader's (and the PMO's) until it is attached. The guard in front of the Documents endpoints calls this before the action runs.
    /// </summary>
    public interface IFileAccessService
    {
        /// <summary>Download and file information.</summary>
        Task<FileVerdict> ReadAsync(Guid documentId, CancellationToken cancellationToken = default);

        /// <summary>Replacing the bytes or editing the name, description and code of a file.</summary>
        Task<FileVerdict> ChangeAsync(Guid documentId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The read rules of the office module for the files it owns. Kept behind a small interface so the decision can be tested without building letters, positions and meetings;
    /// the implementation just asks the three services whose own rules decide who sees a letter, a leave request or a meeting.
    /// </summary>
    public interface IFileOfficeRules
    {
        /// <summary>The letter's own rule (<see cref="ILetterService.CanReadFileAsync"/>): the parties, the CEO and, for numbered Normal and Confidential letters, the secretariat; a draft only its writer.</summary>
        Task<bool> CanReadLetterFileAsync(int letterId, Guid documentId, CancellationToken cancellationToken);

        Task<bool> CanReadLeaveRequestAsync(int leaveRequestId, CancellationToken cancellationToken);

        Task<bool> CanReadMeetingAsync(int meetingId, CancellationToken cancellationToken);
    }

    public sealed class FileOfficeRules : IFileOfficeRules
    {
        private readonly ILetterService _letters;
        private readonly ILeaveService _leaves;
        private readonly IMeetingService _meetings;

        public FileOfficeRules(ILetterService letters, ILeaveService leaves, IMeetingService meetings)
        {
            _letters = letters ?? throw new ArgumentNullException(nameof(letters));
            _leaves = leaves ?? throw new ArgumentNullException(nameof(leaves));
            _meetings = meetings ?? throw new ArgumentNullException(nameof(meetings));
        }

        public Task<bool> CanReadLetterFileAsync(int letterId, Guid documentId, CancellationToken ct) => _letters.CanReadFileAsync(letterId, documentId, ct);

        // the leave service answers with the request only to somebody who may see it (the employee, the approver and his deputy, HR, the CEO)
        public async Task<bool> CanReadLeaveRequestAsync(int leaveRequestId, CancellationToken ct) => (await _leaves.GetLeaveAsync(leaveRequestId, ct))?.Success == true;

        public async Task<bool> CanReadMeetingAsync(int meetingId, CancellationToken ct)
        {
            IQueryable<int> visible = await _meetings.VisibleMeetingIdsAsync(ct);
            return await visible.AnyAsync(i => i == meetingId, ct);
        }
    }

    /// <summary>P-B7a: the read rule of the task module for the files attached to a task (the task's own visibility decides).</summary>
    public interface IFileTaskRules
    {
        Task<bool> CanReadTaskAsync(int taskId, CancellationToken cancellationToken);
    }

    /// <summary>The signed-in user, as the decision needs to know him.</summary>
    internal sealed class FileCaller
    {
        public FileCaller(int userId, bool isPmo)
        {
            UserId = userId;
            IsPmo = isPmo;
        }

        public int UserId { get; }

        public bool IsPmo { get; }
    }

    internal enum FileOwnerRule
    {
        /// <summary>A letter: its own rule decides alone; the uploader and the PMO have no extra right to a letter's file.</summary>
        Letter = 0,

        /// <summary>Any other record: the read set of its module (or the record's own rule) decided <see cref="FileOwner.CallerMayRead"/>.</summary>
        Record = 1,
    }

    /// <summary>One record that holds the file, and whether the caller may read the file through it (decided by the module's own rule before the policy runs).</summary>
    internal sealed class FileOwner
    {
        public FileOwner(string kind, FileOwnerRule rule, bool callerMayRead)
        {
            Kind = kind;
            Rule = rule;
            CallerMayRead = callerMayRead;
        }

        public string Kind { get; }

        public FileOwnerRule Rule { get; }

        public bool CallerMayRead { get; }
    }

    internal sealed class FileFacts
    {
        public bool Exists { get; set; }

        public int? UploadedByUserId { get; set; }

        public List<FileOwner> Owners { get; } = new List<FileOwner>();
    }

    /// <summary>R1t D8: the decision itself, free of the database, so every case can be tested with a few lines.</summary>
    internal static class FileAccessPolicy
    {
        /// <summary>
        /// Read (download, file information):
        /// a letter's file is decided by the letter's rule alone (a stranger gets a refusal, and so do the uploader and the PMO when they are not parties);
        /// any other file is readable through any record whose module allows the caller; failing that, by the user who uploaded it;
        /// a file that no record holds is readable by its uploader and by the PMO only.
        /// </summary>
        public static FileVerdict Read(FileCaller caller, FileFacts facts)
        {
            if (!facts.Exists) return FileVerdict.Missing();
            bool owned = facts.Owners.Count > 0;
            if (facts.Owners.Any(o => o.Rule == FileOwnerRule.Letter))
                return facts.Owners.Any(o => o.Rule == FileOwnerRule.Letter && o.CallerMayRead) ? FileVerdict.Allow(true, true) : FileVerdict.Deny(FileText.NoReadAccess, true, true);
            if (facts.Owners.Any(o => o.CallerMayRead)) return FileVerdict.Allow(true);
            if (Uploader(caller, facts)) return FileVerdict.Allow(owned);
            if (!owned && caller.IsPmo) return FileVerdict.Allow();
            return FileVerdict.Deny(FileText.NoReadAccess, owned);
        }

        /// <summary>
        /// Change (replace the bytes, edit the name, description or code): the access check comes first, so somebody who may not even read the file learns nothing about it
        /// (not even what kind of record holds it); an owned file never changes through the generic endpoints (R1a2 referrer lock, so a read role never opens a change),
        /// and a loose file changes only by its uploader or the PMO. The document service says which of these applies to the caller who passed.
        /// </summary>
        public static FileVerdict Change(FileCaller caller, FileFacts facts)
        {
            FileVerdict read = Read(caller, facts);
            return read.IsAllowed || read.Kind == FileVerdictKind.NotFound ? read : FileVerdict.Deny(FileText.NoReadAccess, read.Owned, read.LetterFile);
        }

        private static bool Uploader(FileCaller caller, FileFacts facts) => caller.UserId > 0 && facts.UploadedByUserId == caller.UserId;
    }

    public sealed class FileAccessService : IFileAccessService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IFileOfficeRules _office;
        private readonly IFileTaskRules _tasks;

        public FileAccessService(IUnitOfWork uow, IInventoryUserContext user, IFileOfficeRules office, IFileTaskRules tasks = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _office = office ?? throw new ArgumentNullException(nameof(office));
            _tasks = tasks;
        }

        private static readonly HashSet<string> Special = new HashSet<string>(FileOwners.Special, StringComparer.Ordinal);

        private static readonly MethodInfo HoldsMethod = typeof(FileAccessService).GetMethod(nameof(HoldsAsync), BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>The columns of the model that hold this file (one cheap existence query per column of <see cref="DocumentLinks"/>).</summary>
        private async Task<List<DocumentLink>> HoldersAsync(Guid id, CancellationToken ct)
        {
            var found = new List<DocumentLink>();
            if (!(_uow is DbContext context)) return found;
            foreach (DocumentLink link in DocumentLinks.Of(context.Model))
            {
                if (Special.Contains(link.EntityType.Name)) continue;
                if (await (Task<bool>)HoldsMethod.MakeGenericMethod(link.EntityType).Invoke(this, new object[] { link, id, ct })) found.Add(link);
            }
            return found;
        }

        private async Task<bool> HoldsAsync<T>(DocumentLink link, Guid id, CancellationToken ct) where T : class
        {
            ParameterExpression row = Expression.Parameter(typeof(T), "r");
            Expression body;
            if (link.IsText)
            {
                string text = id.ToString();
                Expression list = Expression.Call(typeof(EF), nameof(EF.Property), new[] { typeof(string) }, row, Expression.Constant(link.Property));
                Expression<Func<string>> wanted = () => text;
                body = Expression.AndAlso(Expression.NotEqual(list, Expression.Constant(null, typeof(string))),
                    Expression.Call(list, typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) }), wanted.Body));
            }
            else
            {
                Expression<Func<Guid>> value = () => id;
                Type valueType = link.IsNullable ? typeof(Guid?) : typeof(Guid);
                Expression column = Expression.Call(typeof(EF), nameof(EF.Property), new[] { valueType }, row, Expression.Constant(link.Property));
                body = Expression.Equal(column, link.IsNullable ? Expression.Convert(value.Body, typeof(Guid?)) : value.Body);
            }
            return await _uow.Set<T>().AsNoTracking().IgnoreQueryFilters().AnyAsync(Expression.Lambda<Func<T, bool>>(body, row), ct);
        }

        private FileCaller Caller() => new FileCaller(_user.UserId, _user.IsInRole(IdentityRules.Pmo));

        public async Task<FileVerdict> ReadAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            FileFacts facts = await GatherAsync(documentId, stopAtUploader: true, cancellationToken);
            return FileAccessPolicy.Read(Caller(), facts);
        }

        public async Task<FileVerdict> ChangeAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            FileFacts facts = await GatherAsync(documentId, stopAtUploader: false, cancellationToken);
            return FileAccessPolicy.Change(Caller(), facts);
        }

        /// <summary>
        /// Finds the records that hold the file and asks each module's rule whether the caller may read through it. It stops at the first record that lets the caller in
        /// (the verdict cannot change after that), and at a letter (a letter's file is decided by the letters alone).
        /// With <paramref name="stopAtUploader"/> a reader who uploaded the file is let in without looking for owners.
        /// </summary>
        private async Task<FileFacts> GatherAsync(Guid id, bool stopAtUploader, CancellationToken ct)
        {
            var facts = new FileFacts();
            var row = await _uow.Set<Document>().AsNoTracking().Where(d => d.Id == id).Select(d => new { d.UploadedByUserId }).FirstOrDefaultAsync(ct);
            if (row == null) return facts;
            int? uploader = row.UploadedByUserId;
            facts.Exists = true;
            facts.UploadedByUserId = uploader;

            // a letter's file: the letter's own rule decides alone
            foreach (int letterId in await FileOwners.LetterIdsAsync(_uow, id, ct))
            {
                bool may = await _office.CanReadLetterFileAsync(letterId, id, ct);
                facts.Owners.Add(new FileOwner(nameof(OfLetter), FileOwnerRule.Letter, may));
                if (may) break;
            }
            if (facts.Owners.Count > 0) return facts;

            int me = _user.UserId;
            if (stopAtUploader && me > 0 && uploader == me) return facts;

            // the stored signature picture of a user is read by that user
            int? signer = await _uow.Set<OfUserSignature>().AsNoTracking().Where(s => s.ImageDocumentId == id).Select(s => (int?)s.UserId).FirstOrDefaultAsync(ct);
            if (signer.HasValue)
            {
                bool may = me > 0 && signer.Value == me;
                facts.Owners.Add(new FileOwner(nameof(OfUserSignature), FileOwnerRule.Record, may));
                if (may) return facts;
            }

            // the evidence of a leave request is read by whoever may see the request
            foreach (int leaveId in await _uow.Set<OfLeaveRequest>().AsNoTracking().IgnoreQueryFilters().Where(r => r.AttachmentDocumentId == id).Select(r => r.Id).ToListAsync(ct))
            {
                bool may = await _office.CanReadLeaveRequestAsync(leaveId, ct);
                facts.Owners.Add(new FileOwner(nameof(OfLeaveRequest), FileOwnerRule.Record, may));
                if (may) return facts;
            }

            // the evidence of a meeting decision is read by whoever may see the meeting
            foreach (int meetingId in await _uow.Set<OfDecision>().AsNoTracking().IgnoreQueryFilters().Where(d => d.EvidenceDocumentId == id).Select(d => d.MeetingId).Distinct().ToListAsync(ct))
            {
                bool may = await _office.CanReadMeetingAsync(meetingId, ct);
                facts.Owners.Add(new FileOwner(nameof(OfDecision), FileOwnerRule.Record, may));
                if (may) return facts;
            }

            // a file attached to a task is read by whoever may see the task (a confidential task is seen by its parties only); without the task rules the file fails closed
            foreach (int taskId in await _uow.Set<Entities.Model.NasbNiro.Tasks.TmTaskAttachment>().AsNoTracking().Where(a => a.DocumentId == id).Select(a => a.TaskId).Distinct().ToListAsync(ct))
            {
                bool may = _tasks != null && await _tasks.CanReadTaskAsync(taskId, ct);
                facts.Owners.Add(new FileOwner("TmTaskAttachment", FileOwnerRule.Record, may));
                if (may) return facts;
            }

            // the file of a package for the tax or social-security office: payroll packages only to the payroll readers
            foreach (FnOutboxKind kind in await _uow.Set<FnOutboxPackage>().AsNoTracking().Where(p => p.FileDocumentId == id).Select(p => p.Kind).Distinct().ToListAsync(ct))
            {
                bool may = _user.IsInRole(FileOwners.OutboxRoles(kind));
                facts.Owners.Add(new FileOwner(nameof(FnOutboxPackage), FileOwnerRule.Record, may));
                if (may) return facts;
            }

            // every other record that holds the file is found from the model (R1a2's DocumentLinks); the read roles come from the map, a type that is not in it fails closed
            foreach (DocumentLink link in await HoldersAsync(id, ct))
            {
                if (Special.Contains(link.EntityType.Name)) continue;
                bool known = FileOwners.TryRoles(link.EntityType.Name, link.Property, out string roles);
                bool may = known && (roles == null || _user.IsInRole(roles));
                facts.Owners.Add(new FileOwner(link.EntityType.Name, FileOwnerRule.Record, may));
                if (may) return facts;
            }
            return facts;
        }
    }
}
