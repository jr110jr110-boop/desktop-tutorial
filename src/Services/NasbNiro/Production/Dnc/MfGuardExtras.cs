using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production.Dnc
{
    // Production release P2 part E: what a program revision was made with (approved post processor, simulation report, CAM tool list), and the measuring devices.

    public interface IMfCamEvidenceService
    {
        Task<ResponseDto<PagedList<PostProcessorDto>>> GetPostsAsync(PostProcessorQuery query, CancellationToken ct);
        Task<ResponseDto<PostProcessorDto>> CreatePostAsync(SavePostProcessorDto dto, CancellationToken ct);
        Task<ResponseDto<PostProcessorDto>> PostActionAsync(PostProcessorActionDto dto, CancellationToken ct);
        Task<ResponseDto<NcAttachmentFileDto>> GetPostFileAsync(int id, CancellationToken ct);
        Task<ResponseDto<RevisionCamDto>> GetRevisionAsync(int revisionId, CancellationToken ct);
        Task<ResponseDto<RevisionCamDto>> SetRevisionAsync(SetRevisionCamDto dto, CancellationToken ct);
        Task<ResponseDto<RevisionCamDto>> UploadAttachmentAsync(UploadNcAttachmentDto dto, CancellationToken ct);
        Task<ResponseDto<RevisionCamDto>> ImportToolsAsync(ImportCamToolsDto dto, CancellationToken ct);
        Task<ResponseDto<NcAttachmentFileDto>> GetAttachmentAsync(int id, CancellationToken ct);
    }

    /// <summary>The evidence a revision needs before it can be submitted (the DNC submit and approval ask it here).</summary>
    public static class MfCamEvidence
    {
        public static bool Required(IConfiguration configuration) => !string.Equals(configuration?[MfGuardService.RequireCamEvidenceKey], "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>What is missing (Persian); empty when the revision has an approved post processor of its family, a simulation report and a CAM tool list.</summary>
        public static async Task<List<string>> MissingAsync(IUnitOfWork uow, MfNcRevision r, MfNcControllerFamily family, CancellationToken ct)
        {
            var missing = new List<string>();
            MfPostProcessor post = r.PostProcessorId.HasValue ? await uow.Set<MfPostProcessor>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.PostProcessorId.Value && !p.Deleted, ct) : null;
            if (post == null) missing.Add("پست‌پروسسور تأییدشده‌ای که برنامه با آن ساخته شده انتخاب نشده است");
            else if (post.Status != MfPostStatus.Approved) missing.Add($"پست‌پروسسور {post.Code} نسخه {post.Version} تأییدشده و جاری نیست");
            else if (!DncText.Fits(post.ControllerFamily, family)) missing.Add($"پست‌پروسسور {post.Code} برای خانواده کنترلر این برنامه نیست");
            if (!r.SimulationAttachmentId.HasValue) missing.Add("گزارش شبیه‌سازی (CAM یا شبیه‌ساز G-code) پیوست نشده است");
            if (r.ToolCount <= 0) missing.Add("فهرست ابزار CAM وارد نشده است");
            return missing;
        }
    }

    public class MfCamEvidenceService : IMfCamEvidenceService
    {
        public const int MaxAttachmentBytes = 20 * 1024 * 1024;
        public const int MaxPostBytes = 4 * 1024 * 1024;

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public MfCamEvidenceService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => MfText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;

        public static string StatusText(MfPostStatus s) => s switch
        {
            MfPostStatus.Draft => "پیش‌نویس",
            MfPostStatus.Submitted => "منتظر تأیید",
            MfPostStatus.Approved => "تأییدشده (جاری)",
            MfPostStatus.Retired => "بازنشسته",
            MfPostStatus.Rejected => "ردشده",
            _ => s.ToString(),
        };

        private static (byte[] Bytes, string Error) Decode(string base64, int max)
        {
            if (string.IsNullOrWhiteSpace(base64)) return (null, "فایل را انتخاب کنید.");
            if (base64.Length > (max / 3 + 1) * 4 + 8) return (null, $"حجم فایل بیش از {max / 1024 / 1024} مگابایت است.");
            try
            {
                byte[] b = Convert.FromBase64String(base64);
                return b.Length == 0 ? (null, "فایل خالی است.") : (b, null);
            }
            catch (FormatException) { return (null, "فایل خوانده نشد."); }
        }

        private static string FileName(string name)
        {
            string n = Clean(name);
            if (n == null) return null;
            n = n.Replace('\\', '/').Split('/').Last();
            return n.Length == 0 || n.Length > 200 || n.Any(char.IsControl) ? null : n;
        }

        // ================================================================================== post processors

        private async Task<List<PostProcessorDto>> PostDtosAsync(List<MfPostProcessor> rows, CancellationToken ct)
        {
            var names = await ExtrasShared.UserNamesAsync(_uow, rows.SelectMany(r => new[] { (int?)r.CreatorUserId, r.ApprovedByUserId }), ct);
            List<int> machineIds = rows.Where(r => r.MachineId.HasValue).Select(r => r.MachineId.Value).Distinct().ToList();
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            bool writer = _user.IsInRole(DncRoles.Writers) && !IsAi, approver = _user.IsInRole(DncRoles.Approvers) && !IsAi;
            return rows.Select(r =>
            {
                var d = new PostProcessorDto
                {
                    Id = r.Id, Code = r.Code, Title = r.Title, CamSystem = r.CamSystem, ControllerFamily = r.ControllerFamily, ControllerFamilyText = DncText.Family(r.ControllerFamily), MachineId = r.MachineId,
                    MachineTitle = r.MachineId.HasValue ? titles.GetValueOrDefault(r.MachineId.Value) : null, Version = r.Version, FileName = r.FileName, Sha256 = r.Sha256, SizeBytes = r.SizeBytes, Status = r.Status,
                    StatusText = StatusText(r.Status), ChangeNote = r.ChangeNote, CreatedByName = ExtrasShared.NameOf(names, r.CreatorUserId), SubmittedAtText = MfText.Stamp(r.SubmittedAt),
                    ApprovedByName = ExtrasShared.NameOf(names, r.ApprovedByUserId), ApprovedAtText = MfText.Stamp(r.ApprovedAt), RejectReason = r.RejectReason, RowVersion = MfConcurrency.Text(r.RowVersion),
                };
                bool author = r.CreatorUserId == Me;
                if ((r.Status == MfPostStatus.Draft || r.Status == MfPostStatus.Rejected) && (author || writer)) { d.AllowedActions.Add("submit"); d.AllowedActions.Add("withdraw"); }
                if (r.Status == MfPostStatus.Submitted && approver && !author && r.SubmittedByUserId != Me) { d.AllowedActions.Add("approve"); d.AllowedActions.Add("reject"); }
                if (r.Status == MfPostStatus.Approved && approver) d.AllowedActions.Add("retire");
                return d;
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PostProcessorDto>>> GetPostsAsync(PostProcessorQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<PagedList<PostProcessorDto>>(MfText.NotAllowed);
            query ??= new PostProcessorQuery();
            IQueryable<MfPostProcessor> q = _uow.Set<MfPostProcessor>().AsNoTracking().Where(p => !p.Deleted);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Code.Contains(text) || p.Title.Contains(text) || p.CamSystem.Contains(text));
            if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfPostProcessor> rows = await q.OrderBy(p => p.Code).ThenByDescending(p => p.Version).Skip((number - 1) * size).Take(size)
                .Select(p => new MfPostProcessor
                {
                    Id = p.Id, Code = p.Code, Title = p.Title, CamSystem = p.CamSystem, ControllerFamily = p.ControllerFamily, MachineId = p.MachineId, Version = p.Version, FileName = p.FileName, Sha256 = p.Sha256,
                    SizeBytes = p.SizeBytes, Status = p.Status, ChangeNote = p.ChangeNote, CreatorUserId = p.CreatorUserId, SubmittedByUserId = p.SubmittedByUserId, SubmittedAt = p.SubmittedAt,
                    ApprovedByUserId = p.ApprovedByUserId, ApprovedAt = p.ApprovedAt, RejectReason = p.RejectReason, RowVersion = p.RowVersion,
                }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(ExtrasShared.Page(await PostDtosAsync(rows, ct), total, number, size));
        }

        private static readonly Regex PostCode = new Regex(@"^[A-Za-z0-9_\-\.]{2,50}$", RegexOptions.CultureInvariant);

        public async Task<ResponseDto<PostProcessorDto>> CreatePostAsync(SavePostProcessorDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Writers) || IsAi) return Bad<PostProcessorDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfNcControllerFamily), dto.ControllerFamily)) return Bad<PostProcessorDto>(MfText.InvalidData);
            string code = Clean(dto.Code), title = Clean(dto.Title), cam = Clean(dto.CamSystem), name = FileName(dto.FileName);
            if (code == null || !PostCode.IsMatch(code)) return Bad<PostProcessorDto>("کد پست‌پروسسور ۲ تا ۵۰ نویسه لاتین، رقم، نقطه، خط تیره یا زیرخط است.");
            if (title == null || title.Length > 300) return Bad<PostProcessorDto>("عنوان را وارد کنید.");
            if (cam == null || cam.Length > 50) return Bad<PostProcessorDto>("نرم‌افزار CAM را وارد کنید (مثلاً PowerMill).");
            if (name == null) return Bad<PostProcessorDto>("نام فایل نامعتبر است.");
            if (dto.MachineId.HasValue && !await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId.Value, ct)) return Bad<PostProcessorDto>("ماشین پیدا نشد.");
            var (bytes, error) = Decode(dto.ContentBase64, MaxPostBytes);
            if (error != null) return Bad<PostProcessorDto>(error);
            var same = await _uow.Set<MfPostProcessor>().AsNoTracking().Where(p => !p.Deleted && p.Code == code).Select(p => new { p.Version, p.Status, p.ControllerFamily }).ToListAsync(ct);
            if (same.Any(p => p.Status == MfPostStatus.Draft || p.Status == MfPostStatus.Submitted)) return Bad<PostProcessorDto>("برای این کد نسخه پیش‌نویس یا منتظر تأیید وجود دارد؛ ابتدا آن را تکلیف کنید.");
            if (same.Count > 0 && same.Any(p => p.ControllerFamily != dto.ControllerFamily)) return Bad<PostProcessorDto>("خانواده کنترلر یک کد پست‌پروسسور تغییر نمی‌کند؛ کد تازه‌ای بسازید.");
            DateTimeOffset now = DateTimeOffset.Now;
            var row = new MfPostProcessor
            {
                Code = code, Title = title, CamSystem = cam, ControllerFamily = dto.ControllerFamily, MachineId = dto.MachineId, Version = same.Count == 0 ? 1 : same.Max(p => p.Version) + 1, FileName = name,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), SizeBytes = bytes.Length, Content = bytes, Status = MfPostStatus.Draft, ChangeNote = MfText.Clean(dto.ChangeNote),
                CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            if (row.ChangeNote?.Length > 1000) row.ChangeNote = row.ChangeNote.Substring(0, 1000);
            _uow.Set<MfPostProcessor>().Add(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<PostProcessorDto>("نسخه دیگری از این کد هم‌زمان ثبت شد؛ دوباره تلاش کنید."); }
            return ResponseDtoHelper.Ok((await PostDtosAsync(new List<MfPostProcessor> { row }, ct))[0]);
        }

        public async Task<ResponseDto<PostProcessorDto>> PostActionAsync(PostProcessorActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<PostProcessorDto>(MfText.InvalidData);
            if (IsAi) return Bad<PostProcessorDto>(MfText.NotAllowed);
            MfPostProcessor r = await _uow.Set<MfPostProcessor>().FirstOrDefaultAsync(p => p.Id == dto.Id && !p.Deleted, ct);
            if (r == null) return MfText.NotFound<PostProcessorDto>("پست‌پروسسور پیدا نشد.");
            if (!MfConcurrency.Apply(_uow, r, dto.RowVersion)) return Bad<PostProcessorDto>(MfText.Concurrency);
            DateTimeOffset now = DateTimeOffset.Now;
            bool author = r.CreatorUserId == Me;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (!(author || _user.IsInRole(DncRoles.Writers))) return Bad<PostProcessorDto>(MfText.NotAllowed);
                    if (r.Status != MfPostStatus.Draft && r.Status != MfPostStatus.Rejected) return Bad<PostProcessorDto>("فقط پیش‌نویس یا ردشده برای تأیید ارسال می‌شود.");
                    r.Status = MfPostStatus.Submitted; r.SubmittedByUserId = Me; r.SubmittedAt = now; r.RejectReason = null;
                    break;
                case "withdraw":
                    if (!(author || _user.IsInRole(DncRoles.Writers))) return Bad<PostProcessorDto>(MfText.NotAllowed);
                    if (r.Status != MfPostStatus.Draft && r.Status != MfPostStatus.Rejected) return Bad<PostProcessorDto>("فقط پیش‌نویس یا ردشده پس گرفته می‌شود.");
                    // a draft was never usable (a revision takes only an approved one): it goes
                    _uow.Set<MfPostProcessor>().Remove(r);
                    await _uow.SaveChangesAsync(ct);
                    await OfficeGates.SyncAsync(_notify, "mf:post:" + r.Id, false, "production", "post-processor", r.Id, "", "/production/dnc/posts", DncRoles.Approvers, ct);
                    return ResponseDtoHelper.Ok<PostProcessorDto>(null);
                case "approve":
                case "reject":
                    if (!_user.IsInRole(DncRoles.Approvers)) return Bad<PostProcessorDto>(MfText.NotAllowed);
                    if (author || r.SubmittedByUserId == Me) return Bad<PostProcessorDto>(MfText.SameUser);
                    if (r.Status != MfPostStatus.Submitted) return Bad<PostProcessorDto>("این نسخه منتظر تأیید نیست.");
                    if (dto.Action.Trim().ToLowerInvariant() == "reject")
                    {
                        string reason = Clean(dto.Reason);
                        if (reason == null) return Bad<PostProcessorDto>("دلیل رد را بنویسید.");
                        r.Status = MfPostStatus.Rejected; r.RejectReason = reason.Length > 1000 ? reason.Substring(0, 1000) : reason;
                        break;
                    }
                    if (Convert.ToHexString(SHA256.HashData(r.Content ?? Array.Empty<byte>())) != r.Sha256) return Bad<PostProcessorDto>("محتوای ذخیره‌شده با اثر انگشت نمی‌خواند؛ تأیید نمی‌شود.");
                    // one current version per code: the earlier approved one retires (revisions made with it keep pointing at it)
                    foreach (MfPostProcessor old in await _uow.Set<MfPostProcessor>().Where(p => !p.Deleted && p.Code == r.Code && p.Id != r.Id && p.Status == MfPostStatus.Approved).ToListAsync(ct))
                    { old.Status = MfPostStatus.Retired; old.ModifyDate = now; old.ModifierUserId = Me; }
                    r.Status = MfPostStatus.Approved; r.ApprovedByUserId = Me; r.ApprovedAt = now;
                    break;
                case "retire":
                    if (!_user.IsInRole(DncRoles.Approvers)) return Bad<PostProcessorDto>(MfText.NotAllowed);
                    if (r.Status != MfPostStatus.Approved) return Bad<PostProcessorDto>("فقط نسخه جاری بازنشسته می‌شود.");
                    r.Status = MfPostStatus.Retired;
                    break;
                default:
                    return Bad<PostProcessorDto>(MfText.InvalidData);
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PostProcessorDto>(MfText.Concurrency); }
            await OfficeGates.SyncAsync(_notify, "mf:post:" + r.Id, r.Status == MfPostStatus.Submitted && !r.Deleted, "production", "post-processor", r.Id,
                $"پست‌پروسسور {r.Code} نسخه {r.Version} منتظر تأیید است", "/production/dnc/posts", DncRoles.Approvers, ct);
            return ResponseDtoHelper.Ok((await PostDtosAsync(new List<MfPostProcessor> { r }, ct))[0]);
        }

        public async Task<ResponseDto<NcAttachmentFileDto>> GetPostFileAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Writers + "," + DncRoles.Approvers)) return Bad<NcAttachmentFileDto>(MfText.NotAllowed);
            var p = await _uow.Set<MfPostProcessor>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.FileName, x.Content }).FirstOrDefaultAsync(ct);
            if (p == null) return MfText.NotFound<NcAttachmentFileDto>("پست‌پروسسور پیدا نشد.");
            return ResponseDtoHelper.Ok(new NcAttachmentFileDto { FileName = p.FileName, ContentBase64 = Convert.ToBase64String(p.Content) });
        }

        // ================================================================================== revision evidence

        private async Task<(MfNcRevision Revision, MfNcProgram Program, string Error)> LoadAsync(int revisionId, bool forEdit, CancellationToken ct)
        {
            MfNcRevision r = forEdit ? await _uow.Set<MfNcRevision>().FirstOrDefaultAsync(x => x.Id == revisionId && !x.Deleted, ct)
                : await _uow.Set<MfNcRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == revisionId && !x.Deleted, ct);
            if (r == null) return (null, null, "نسخه پیدا نشد.");
            MfNcProgram p = await _uow.Set<MfNcProgram>().AsNoTracking().FirstAsync(x => x.Id == r.ProgramId, ct);
            if (!forEdit) return (r, p, null);
            if (!(_user.IsInRole(DncRoles.Writers) || r.CreatorUserId == Me) || IsAi) return (null, null, MfText.NotAllowed);
            if (r.Status != MfNcRevisionStatus.Draft) return (null, null, "مدارک CAM فقط در پیش‌نویس تغییر می‌کند؛ نسخه ارسال‌شده یا تأییدشده ثابت است.");
            return (r, p, null);
        }

        public async Task<ResponseDto<RevisionCamDto>> GetRevisionAsync(int revisionId, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<RevisionCamDto>(MfText.NotAllowed);
            var (r, p, error) = await LoadAsync(revisionId, false, ct);
            if (error != null) return MfText.NotFound<RevisionCamDto>(error);
            var dto = new RevisionCamDto { RevisionId = r.Id, PostProcessorId = r.PostProcessorId, CamSystem = r.CamSystem, SimulationAttachmentId = r.SimulationAttachmentId };
            if (r.PostProcessorId.HasValue)
            {
                var post = await _uow.Set<MfPostProcessor>().AsNoTracking().Where(x => x.Id == r.PostProcessorId.Value).Select(x => new { x.Code, x.Version, x.Status }).FirstOrDefaultAsync(ct);
                if (post != null) dto.PostProcessorText = $"{post.Code} نسخه {post.Version} ({StatusText(post.Status)})";
            }
            var atts = await _uow.Set<MfNcAttachment>().AsNoTracking().Where(a => a.RevisionId == r.Id).OrderByDescending(a => a.Id)
                .Select(a => new { a.Id, a.Kind, a.FileName, a.Sha256, a.SizeBytes, a.UploadedByUserId, a.UploadedAt }).ToListAsync(ct);
            var names = await ExtrasShared.UserNamesAsync(_uow, atts.Select(a => (int?)a.UploadedByUserId), ct);
            dto.Attachments = atts.Select(a => new NcAttachmentDto
            {
                Id = a.Id, Kind = a.Kind, KindText = a.Kind == MfNcAttachmentKind.Simulation ? "گزارش شبیه‌سازی" : "برگه راه‌اندازی", FileName = a.FileName, Sha256 = a.Sha256, SizeBytes = a.SizeBytes,
                UploadedByName = ExtrasShared.NameOf(names, a.UploadedByUserId), UploadedAtText = MfText.Stamp(a.UploadedAt),
            }).ToList();
            dto.Tools = await _uow.Set<MfCamTool>().AsNoTracking().Where(t => t.RevisionId == r.Id).OrderBy(t => t.ToolNumber)
                .Select(t => new CamToolDto { ToolNumber = t.ToolNumber, Name = t.Name, Diameter = t.Diameter, Length = t.Length }).ToListAsync(ct);
            dto.Missing = await MfCamEvidence.MissingAsync(_uow, r, p.ControllerFamily, ct);
            dto.Editable = r.Status == MfNcRevisionStatus.Draft && (_user.IsInRole(DncRoles.Writers) || r.CreatorUserId == Me) && !IsAi;
            return ResponseDtoHelper.Ok(dto);
        }

        private async Task<ResponseDto<RevisionCamDto>> SaveAsync(MfNcRevision r, CancellationToken ct)
        {
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<RevisionCamDto>(MfText.Concurrency); }
            return await GetRevisionAsync(r.Id, ct);
        }

        public async Task<ResponseDto<RevisionCamDto>> SetRevisionAsync(SetRevisionCamDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<RevisionCamDto>(MfText.InvalidData);
            var (r, p, error) = await LoadAsync(dto.RevisionId, true, ct);
            if (error != null) return Bad<RevisionCamDto>(error);
            if (dto.PostProcessorId.HasValue)
            {
                MfPostProcessor post = await _uow.Set<MfPostProcessor>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.PostProcessorId.Value && !x.Deleted, ct);
                if (post == null || post.Status != MfPostStatus.Approved) return Bad<RevisionCamDto>("فقط پست‌پروسسور تأییدشده و جاری انتخاب می‌شود.");
                if (!DncText.Fits(post.ControllerFamily, p.ControllerFamily)) return Bad<RevisionCamDto>("این پست‌پروسسور برای خانواده کنترلر این برنامه نیست.");
                if (post.MachineId.HasValue && p.MachineId.HasValue && post.MachineId != p.MachineId) return Bad<RevisionCamDto>("این پست‌پروسسور برای ماشین دیگری است.");
            }
            string cam = Clean(dto.CamSystem);
            if (cam?.Length > 50) return Bad<RevisionCamDto>("نام نرم‌افزار CAM حداکثر ۵۰ نویسه است.");
            r.PostProcessorId = dto.PostProcessorId; r.CamSystem = cam;
            return await SaveAsync(r, ct);
        }

        public async Task<ResponseDto<RevisionCamDto>> UploadAttachmentAsync(UploadNcAttachmentDto dto, CancellationToken ct)
        {
            if (dto == null || !Enum.IsDefined(typeof(MfNcAttachmentKind), dto.Kind)) return Bad<RevisionCamDto>(MfText.InvalidData);
            var (r, _, error) = await LoadAsync(dto.RevisionId, true, ct);
            if (error != null) return Bad<RevisionCamDto>(error);
            string name = FileName(dto.FileName);
            if (name == null) return Bad<RevisionCamDto>("نام فایل نامعتبر است.");
            var (bytes, decodeError) = Decode(dto.ContentBase64, MaxAttachmentBytes);
            if (decodeError != null) return Bad<RevisionCamDto>(decodeError);
            if (await _uow.Set<MfNcAttachment>().CountAsync(a => a.RevisionId == r.Id, ct) >= 20) return Bad<RevisionCamDto>("هر نسخه حداکثر ۲۰ پیوست دارد.");
            var att = new MfNcAttachment
            {
                RevisionId = r.Id, Kind = dto.Kind, FileName = name, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), SizeBytes = bytes.Length, Content = bytes, UploadedByUserId = Me, UploadedAt = DateTimeOffset.Now,
            };
            _uow.Set<MfNcAttachment>().Add(att);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<RevisionCamDto>("پیوست ذخیره نشد."); }
            if (dto.Kind == MfNcAttachmentKind.Simulation) r.SimulationAttachmentId = att.Id;
            return await SaveAsync(r, ct);
        }

        public async Task<ResponseDto<RevisionCamDto>> ImportToolsAsync(ImportCamToolsDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<RevisionCamDto>(MfText.InvalidData);
            var (r, _, error) = await LoadAsync(dto.RevisionId, true, ct);
            if (error != null) return Bad<RevisionCamDto>(error);
            if ((dto.Csv?.Length ?? 0) > 512 * 1024) return Bad<RevisionCamDto>("فهرست ابزار بزرگ‌تر از ۵۱۲ کیلوبایت است.");
            var (tools, parseError) = MfGuardRules.ParseCamToolList(dto.Csv);
            if (parseError != null) return Bad<RevisionCamDto>(parseError);
            if (tools.Any(t => t.Diameter is <= 0 or > 2000 || t.Length is <= 0 or > 2000)) return Bad<RevisionCamDto>("قطر یا طول ابزار باید بزرگ‌تر از صفر و کوچک‌تر از ۲۰۰۰ میلی‌متر باشد.");
            _uow.Set<MfCamTool>().RemoveRange(await _uow.Set<MfCamTool>().Where(t => t.RevisionId == r.Id).ToListAsync(ct));
            foreach (MfCamTool t in tools) { t.RevisionId = r.Id; _uow.Set<MfCamTool>().Add(t); }
            r.ToolCount = tools.Count;
            return await SaveAsync(r, ct);
        }

        public async Task<ResponseDto<NcAttachmentFileDto>> GetAttachmentAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<NcAttachmentFileDto>(MfText.NotAllowed);
            var a = await _uow.Set<MfNcAttachment>().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.FileName, x.Content }).FirstOrDefaultAsync(ct);
            if (a == null) return MfText.NotFound<NcAttachmentFileDto>("پیوست پیدا نشد.");
            return ResponseDtoHelper.Ok(new NcAttachmentFileDto { FileName = a.FileName, ContentBase64 = Convert.ToBase64String(a.Content) });
        }
    }

    // ============================================================================================ measuring devices

    public interface IMfMeasureService
    {
        Task<ResponseDto<List<MeasureStationDto>>> GetStationsAsync(CancellationToken ct);
        Task<ResponseDto<MeasureStationDto>> SaveStationAsync(MeasureStationDto dto, CancellationToken ct);
        Task<ResponseDto<StationKeyDto>> IssueKeyAsync(int stationId, CancellationToken ct);
        Task<ResponseDto<MeasurementPushResultDto>> PushAsync(string station, string key, string remote, MeasurementPushDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<MeasurementDto>>> GetMeasurementsAsync(MeasurementQuery query, CancellationToken ct);
    }

    /// <summary>
    /// Digital gauges and probe results (P2 part E). A gauge interface (a box or a PC with the gauges' receiver) pushes with its own key from its own address; a checklist item that is "device only"
    /// takes its value only from here. A typed value never becomes a measurement.
    /// </summary>
    public class MfMeasureService : IMfMeasureService
    {
        public const string Editors = DncRoles.LinkEditors + ",QC";
        public const string Readers = DncRoles.Readers + ",QC,QA";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public MfMeasureService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => MfText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;

        public static readonly Regex Characteristic = new Regex(@"^[A-Za-z][A-Za-z0-9_\-]{0,49}$", RegexOptions.CultureInvariant);

        public static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        public async Task<ResponseDto<List<MeasureStationDto>>> GetStationsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(Readers)) return Bad<List<MeasureStationDto>>(MfText.NotAllowed);
            List<MfMeasureStation> rows = await _uow.Set<MfMeasureStation>().AsNoTracking().Where(s => !s.Deleted).OrderBy(s => s.Code).ToListAsync(ct);
            List<int> machineIds = rows.Where(r => r.MachineId.HasValue).Select(r => r.MachineId.Value).ToList();
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            return ResponseDtoHelper.Ok(rows.Select(s => new MeasureStationDto
            {
                Id = s.Id, Code = s.Code, Title = s.Title, AllowedAddresses = s.AllowedAddresses, MachineId = s.MachineId, MachineTitle = s.MachineId.HasValue ? titles.GetValueOrDefault(s.MachineId.Value) : null,
                IsActive = s.IsActive, HasKey = s.KeyHash != null, LastSeenAtText = MfText.Stamp(s.LastSeenAt), RowVersion = MfConcurrency.Text(s.RowVersion),
            }).ToList());
        }

        public async Task<ResponseDto<MeasureStationDto>> SaveStationAsync(MeasureStationDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(Editors) || IsAi) return Bad<MeasureStationDto>(MfText.NotAllowed);
            if (dto == null) return Bad<MeasureStationDto>(MfText.InvalidData);
            string code = Clean(dto.Code), title = Clean(dto.Title);
            if (code == null || !Regex.IsMatch(code, @"^[A-Za-z0-9_\-]{2,50}$", RegexOptions.CultureInvariant)) return Bad<MeasureStationDto>("کد ایستگاه ۲ تا ۵۰ نویسه لاتین، رقم، خط تیره یا زیرخط است.");
            if (title == null || title.Length > 300) return Bad<MeasureStationDto>("عنوان ایستگاه را وارد کنید.");
            if (!DeviceAddress.TryNormalize(dto.AllowedAddresses, out string allowed, out string addressError)) return Bad<MeasureStationDto>(addressError);
            if (allowed == null) return Bad<MeasureStationDto>("نشانی یا نشانی‌های مجاز ایستگاه را وارد کنید (ایستگاه بدون نشانی مجاز پذیرفته نمی‌شود).");
            if (dto.MachineId.HasValue && !await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId.Value, ct)) return Bad<MeasureStationDto>("ماشین پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            MfMeasureStation row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<MfMeasureStation>().FirstOrDefaultAsync(s => s.Id == dto.Id.Value && !s.Deleted, ct);
                if (row == null) return MfText.NotFound<MeasureStationDto>("ایستگاه پیدا نشد.");
                if (!MfConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<MeasureStationDto>(MfText.Concurrency);
            }
            else
            {
                row = new MfMeasureStation { CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfMeasureStation>().Add(row);
            }
            row.Code = code; row.Title = title; row.AllowedAddresses = allowed; row.MachineId = dto.MachineId; row.IsActive = dto.IsActive; row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MeasureStationDto>(MfText.Concurrency); }
            catch (DbUpdateException) { return Bad<MeasureStationDto>("کد ایستگاه تکراری است."); }
            return ResponseDtoHelper.Ok((await GetStationsAsync(ct)).Data.First(s => s.Id == row.Id));
        }

        public async Task<ResponseDto<StationKeyDto>> IssueKeyAsync(int stationId, CancellationToken ct)
        {
            if (!_user.IsInRole(Editors) || IsAi) return Bad<StationKeyDto>(MfText.NotAllowed);
            MfMeasureStation row = await _uow.Set<MfMeasureStation>().FirstOrDefaultAsync(s => s.Id == stationId && !s.Deleted, ct);
            if (row == null) return MfText.NotFound<StationKeyDto>("ایستگاه پیدا نشد.");
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            row.KeyHash = HashKey(key); row.ModifyDate = DateTimeOffset.Now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new StationKeyDto { StationId = row.Id, Key = key });
        }

        public async Task<ResponseDto<MeasurementPushResultDto>> PushAsync(string station, string key, string remote, MeasurementPushDto dto, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(station) || string.IsNullOrEmpty(key) || station.Length > 50) return Unauthorized();
            MfMeasureStation s = await _uow.Set<MfMeasureStation>().FirstOrDefaultAsync(x => x.Code == station && !x.Deleted, ct);
            if (s == null || !s.IsActive || s.KeyHash == null) return Unauthorized();
            byte[] given = Encoding.UTF8.GetBytes(HashKey(key)), stored = Encoding.UTF8.GetBytes(s.KeyHash);
            if (given.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(given, stored) || !DeviceAddress.Allows(s.AllowedAddresses, remote)) return Unauthorized();
            var items = dto?.Items ?? new List<MeasurementPushItemDto>();
            if (items.Count == 0 || items.Count > 200) return Bad<MeasurementPushResultDto>("بین ۱ و ۲۰۰ اندازه در هر ارسال پذیرفته می‌شود.");
            DateTime now = DateTime.Now;
            foreach (MeasurementPushItemDto i in items)
            {
                string c = Clean(i.Characteristic)?.ToUpperInvariant();
                if (c == null || !Characteristic.IsMatch(c)) return Bad<MeasurementPushResultDto>("کد مشخصه نامعتبر است (حرف لاتین در ابتدا، حداکثر ۵۰ نویسه).");
                if (Math.Abs(i.Value) >= 1_000_000m) return Bad<MeasurementPushResultDto>("مقدار اندازه خارج از محدوده است.");
                int? machine = i.MachineId ?? s.MachineId;
                if (i.MachineId.HasValue && s.MachineId.HasValue && i.MachineId != s.MachineId) return Bad<MeasurementPushResultDto>("این ایستگاه برای ماشین دیگری تعریف شده است.");
                int? op = i.OperationId;
                if (op.HasValue)
                {
                    MfOperation o = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == op.Value && !x.Deleted, ct);
                    if (o == null || o.Status == MfOperationStatus.Done || o.Status == MfOperationStatus.Skipped) return Bad<MeasurementPushResultDto>("عملیات پیدا نشد یا تمام شده است.");
                }
                else if (machine.HasValue)
                {
                    // the operation open on the machine now
                    op = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.EndAt == null && b.MachineId == machine.Value).OrderByDescending(b => b.StartAt).Select(b => (int?)b.OperationId).FirstOrDefaultAsync(ct);
                }
                string device = Clean(i.Device);
                _uow.Set<MfMeasurement>().Add(new MfMeasurement
                {
                    OperationId = op, MachineId = machine, StationId = s.Id, Characteristic = c, Value = i.Value, Source = MfMeasurementSource.Gauge, Device = device?.Length > 200 ? device[..200] : device, At = now,
                });
            }
            s.LastSeenAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new MeasurementPushResultDto { Accepted = items.Count });
        }

        private static ResponseDto<MeasurementPushResultDto> Unauthorized() => ResponseDtoHelper.Unauthorized<MeasurementPushResultDto>("کلید یا نشانی ایستگاه اندازه‌گیری پذیرفته نشد.");

        public async Task<ResponseDto<PagedList<MeasurementDto>>> GetMeasurementsAsync(MeasurementQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(Readers)) return Bad<PagedList<MeasurementDto>>(MfText.NotAllowed);
            query ??= new MeasurementQuery();
            if (!query.OperationId.HasValue && !query.MachineId.HasValue) return Bad<PagedList<MeasurementDto>>("عملیات یا ماشین را انتخاب کنید.");
            IQueryable<MfMeasurement> q = _uow.Set<MfMeasurement>().AsNoTracking();
            if (query.OperationId.HasValue) q = q.Where(m => m.OperationId == query.OperationId.Value);
            if (query.MachineId.HasValue) q = q.Where(m => m.MachineId == query.MachineId.Value);
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfMeasurement> rows = await q.OrderByDescending(m => m.At).ThenByDescending(m => m.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<int> machineIds = rows.Where(r => r.MachineId.HasValue).Select(r => r.MachineId.Value).Distinct().ToList();
            List<int> stationIds = rows.Where(r => r.StationId.HasValue).Select(r => r.StationId.Value).Distinct().ToList();
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            Dictionary<int, string> codes = await _uow.Set<MfMeasureStation>().AsNoTracking().Where(s => stationIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Code, ct);
            var list = rows.Select(m => new MeasurementDto
            {
                Id = m.Id, OperationId = m.OperationId, MachineId = m.MachineId, MachineTitle = m.MachineId.HasValue ? titles.GetValueOrDefault(m.MachineId.Value) : null,
                StationCode = m.StationId.HasValue ? codes.GetValueOrDefault(m.StationId.Value) : null, Characteristic = m.Characteristic, Value = m.Value, Source = m.Source,
                SourceText = m.Source == MfMeasurementSource.Probe ? "پراب ماشین" : "گیج دیجیتال", Device = m.Device, At = m.At, AtText = MfText.Stamp(m.At),
            }).ToList();
            return ResponseDtoHelper.Ok(ExtrasShared.Page(list, total, number, size));
        }
    }
}
