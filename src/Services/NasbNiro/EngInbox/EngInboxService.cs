using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.EngInbox;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.EngInboxDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.EngInbox
{
    public interface IEngInboxService
    {
        /// <summary>Scan the source now: read, validate, keep and move every ready package (bounded per run; never throws on a bad package).</summary>
        Task<ResponseDto<EngInboxScanResultDto>> ScanAsync(CancellationToken ct);

        Task<ResponseDto<EngInboxStatusDto>> GetStatusAsync(CancellationToken ct);
        Task<ResponseDto<PagedList<EngInboxPackageDto>>> GetPackagesAsync(EngInboxQuery query, CancellationToken ct);
        Task<ResponseDto<EngInboxPackageDetailDto>> GetPackageAsync(int id, CancellationToken ct);
        Task<ResponseDto<EngInboxPackageDto>> DismissAsync(EngInboxDismissDto dto, CancellationToken ct);
    }

    public static class EngInboxText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string NotFound = "بسته پیدا نشد.";
        public const string AiNeverDecides = "بستهٔ صندوق ورودی را فقط انسان کنار می‌گذارد؛ ایجنت هوش مصنوعی تصمیم نمی‌گیرد.";
        public const string ReasonTooShort = "علت را دست‌کم ۱۵ نویسه بنویسید.";
        public const string Concurrent = "این بسته هم‌زمان تغییر کرد؛ صفحه را تازه کنید و دوباره تلاش کنید.";
        public const string DismissStatus = "فقط بستهٔ ردشده، متوقف‌شده یا منتظر ایجنت کنار گذاشته می‌شود.";
        public const string Duplicate = "تکراری";
        public const string OriginatorCannotCheck = "تهیه‌کنندهٔ بستهٔ مهندسی نمی‌تواند همان مدرک را بررسی یا تأیید کند.";
        public const string OriginatorUnknown = "تهیه‌کننده باید نام کاربری یک کاربر فعال باشد.";
        public const string ScanBusy = "بررسی دیگری در حال اجراست.";
        public const string RowVersionRequired = "نسخهٔ ردیف (RowVersion) بسته لازم است؛ صفحه را تازه کنید و دوباره تلاش کنید.";
        public const int TechAfter = 3;
        public const string VersionConflict = "نسخه تکراری با محتوای متفاوت؛ شماره نسخه را بالا ببرید.";
        public const string OlderVersion = "نسخهٔ بالاتری از همین بسته قبلاً دریافت شده است؛ نسخهٔ قدیمی‌تر پذیرفته نمی‌شود.";

        public static string KindText(EngInboxKind k) => k == EngInboxKind.Platform ? "پلتفرم" : "پروژه";

        /// <summary>«پلتفرم X / API 611 / نسخه 2» or «پروژه C / P / نسخه 2».</summary>
        public static string Name(EngInboxPackage p) =>
            p.Kind == EngInboxKind.Platform
                ? $"پلتفرم {p.PlatformCode} / {p.Standard} / نسخه {p.PackageVersion.ToString(CultureInfo.InvariantCulture)}"
                : $"پروژه {p.ContractCode} / {p.ProjectCode} / نسخه {p.PackageVersion.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The ingest side of the engineering inbox: scan the source, parse and validate, keep the structured copy (a rejected package is kept too, with its findings and no lines) and take the package out of the inbox. Idempotent
    /// (the content hash), bounded per run, and a package that cannot be processed is counted and left for the next scan instead of stopping the run.
    /// </summary>
    public class EngInboxService : IEngInboxService
    {
        public const int MaxPerRun = 10;

        /// <summary>One scan at a time in the whole process (the agent step, the hosted loop and the button share it).</summary>
        private static readonly SemaphoreSlim ScanGate = new SemaphoreSlim(1, 1);

        /// <summary>How often saving the same package failed (key: the folder and its signature); after <see cref="EngInboxText.TechAfter"/> failures the package is moved to _Rejected with a technical note.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Failures = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IEngineeringSource _source;

        public EngInboxService(IUnitOfWork uow, IInventoryUserContext user, IEngineeringSource source)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;
        private bool CanRead => _user.IsInRole(EngInboxRoles.Readers);
        private bool CanOperate => !IsAi && _user.IsInRole(EngInboxRoles.Operators);

        private static readonly EngInboxStatus[] Live = { EngInboxStatus.Ingested, EngInboxStatus.Applied, EngInboxStatus.ApplyBlocked, EngInboxStatus.Superseded };

        private void Stamp(Entities.Model.BaseEntity e, bool isNew)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (isNew) { e.CreateDate = now; e.CreatorUserId = _user.UserId; }
            e.ModifyDate = now;
            e.ModifierUserId = _user.UserId;
        }

        // ------------------------------------------------------------------------------------------------ scan

        public async Task<ResponseDto<EngInboxScanResultDto>> ScanAsync(CancellationToken ct)
        {
            if (!CanOperate && !AgentMaker.Allows(EngInboxCodes.Ingest)) return ResponseDtoHelper.BadRequest<EngInboxScanResultDto>(EngInboxText.NotAllowed);
            if (!await ScanGate.WaitAsync(0, ct)) return ResponseDtoHelper.Ok(new EngInboxScanResultDto { Note = EngInboxText.ScanBusy });
            try { return await ScanCoreAsync(ct); }
            finally { ScanGate.Release(); }
        }

        private async Task<ResponseDto<EngInboxScanResultDto>> ScanCoreAsync(CancellationToken ct)
        {
            var result = new EngInboxScanResultDto();
            try
            {
                if (!_source.IsConfigured)
                {
                    result.NotConfigured = true;
                    result.Note = _source.NotConfiguredReason;
                    return ResponseDtoHelper.Ok(result);
                }
                IReadOnlyList<EngPackageHandle> handles = await _source.ListReadyAsync(ct);
                result.Found = handles.Count;
                foreach (EngPackageHandle h in handles.Take(MaxPerRun))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        switch (await IngestOneAsync(h, ct))
                        {
                            case EngSourceOutcome.Ingested: result.Ingested++; break;
                            case EngSourceOutcome.Duplicate: result.Duplicates++; break;
                            default: result.Rejected++; break;
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException || ex is System.IO.IOException)
                    {
                        AgentDb.Clear(_uow);
                        result.Errors++;
                        try { if (await GiveUpAsync(h, ct)) result.Rejected++; }
                        catch (Exception ex2) when (ex2 is DbUpdateException || ex2 is InvalidOperationException || ex2 is System.IO.IOException) { AgentDb.Clear(_uow); }
                    }
                }
                if (handles.Count > MaxPerRun) result.Note = "بسته‌های بعدی در اسکن بعدی خوانده می‌شوند.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                AgentDb.Clear(_uow);
                result.Errors++;
            }
            try { await TouchStateAsync(result, ct); }
            catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException) { AgentDb.Clear(_uow); }
            return ResponseDtoHelper.Ok(result);
        }

        /// <summary>A package whose saving failed again and again: after the third time it is stored as Rejected (code TECH) and moved to _Rejected, so one broken folder never jams the inbox.</summary>
        private async Task<bool> GiveUpAsync(EngPackageHandle h, CancellationToken ct)
        {
            string sig = await SafeSignatureAsync(h, ct);
            string key = h.Id + "|" + sig;
            int n = Failures.AddOrUpdate(key, 1, (_, old) => old + 1);
            if (n < EngInboxText.TechAfter) return false;
            Failures.TryRemove(key, out _);
            string note = "این بسته " + EngInboxText.TechAfter.ToString(CultureInfo.InvariantCulture) + " بار ذخیره نشد (خطای فنی)؛ به پوشهٔ ردشده رفت. پوشه را بررسی و در صورت نیاز دوباره در صندوق ورودی بگذارید.";
            bool exists = await _uow.Set<EngInboxPackage>().AsNoTracking().AnyAsync(x => !x.Deleted && x.Status == EngInboxStatus.Rejected && x.OriginalPath == h.RelativePath && x.SourceSignature == sig, ct);
            if (!exists)
            {
                var pkg = new EngInboxPackage
                {
                    SourceName = _source.Name, Kind = h.LayoutKind == EngPackageKind.Project ? EngInboxKind.Project : EngInboxKind.Platform, PackageVersion = 0, Status = EngInboxStatus.Rejected, RejectCode = "TECH",
                    OriginalPath = Fit(h.RelativePath, 500), SourceSignature = Fit(sig, 100), FindingsJson = FindingsToJson(new[] { new EngFinding { Severity = EngFindingSeverity.Error, Message = note } }), ErrorCount = 1,
                    IngestedAt = DateTimeOffset.Now, StatusAt = DateTimeOffset.Now, StatusNote = Fit("رد شد: خطای فنی", 500),
                };
                Stamp(pkg, true);
                _uow.Set<EngInboxPackage>().Add(pkg);
                await _uow.SaveChangesAsync(ct);
                string stored = await TryMoveAsync(h, EngSourceOutcome.Rejected, note, pkg, ct);
                if (!string.IsNullOrEmpty(stored)) { pkg.StoredPath = Fit(stored, 500); Stamp(pkg, false); await _uow.SaveChangesAsync(ct); }
            }
            else await TryMoveAsync(h, EngSourceOutcome.Rejected, note, null, ct);
            return true;
        }

        private async Task<string> SafeSignatureAsync(EngPackageHandle h, CancellationToken ct)
        {
            try { return await _source.SignatureAsync(h, ct) ?? ""; }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { return ""; }
        }

        /// <summary>Moves the package out of the inbox; a failed move is reported in the status note of the package (when there is one) and the package is NOT treated as moved (empty result).</summary>
        private async Task<string> TryMoveAsync(EngPackageHandle h, EngSourceOutcome outcome, string note, EngInboxPackage pkg, CancellationToken ct)
        {
            try { return await _source.MarkAsync(h, outcome, note, ct) ?? ""; }
            catch (EngSourceMoveException ex)
            {
                if (pkg != null) pkg.StatusNote = Fit((pkg.StatusNote ?? "") + " — " + ex.Message, 500);
                return "";
            }
        }

        public static string Fit(string text, int max) => text == null ? null : (text.Length <= max ? text : text.Substring(0, max));

        private async Task TouchStateAsync(EngInboxScanResultDto r, CancellationToken ct)
        {
            EngInboxScanState s = await _uow.Set<EngInboxScanState>().Where(x => !x.Deleted).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            bool isNew = s == null;
            if (isNew) { s = new EngInboxScanState(); _uow.Set<EngInboxScanState>().Add(s); }
            s.LastScanAt = DateTimeOffset.Now;
            s.LastFound = r.Found; s.LastIngested = r.Ingested; s.LastRejected = r.Rejected; s.LastDuplicates = r.Duplicates;
            s.LastNote = r.NotConfigured ? EngText.Norm(r.Note).Length == 0 ? null : r.Note : (r.Errors > 0 ? "خطا در " + r.Errors.ToString(CultureInfo.InvariantCulture) + " بسته؛ در اسکن بعدی دوباره تلاش می‌شود." : r.Note);
            Stamp(s, isNew);
            await _uow.SaveChangesAsync(ct);
        }

        private async Task<EngSourceOutcome> IngestOneAsync(EngPackageHandle h, CancellationToken ct)
        {
            // a package the inbox already knows (same files, same sizes, same times) is recognised without reading or hashing its files (B3)
            string sig = await SafeSignatureAsync(h, ct);
            if (sig.Length > 0)
            {
                var known = await _uow.Set<EngInboxPackage>().AsNoTracking().Where(x => !x.Deleted && x.SourceSignature == sig).OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.OriginalPath }).ToListAsync(ct);
                var live = known.FirstOrDefault(x => Live.Contains(x.Status));
                if (live != null)
                {
                    await TryMoveAsync(h, EngSourceOutcome.Duplicate, EngInboxText.Duplicate + ": همین بسته قبلاً دریافت شده است (بستهٔ شمارهٔ " + live.Id.ToString(CultureInfo.InvariantCulture) + ").", null, ct);
                    return EngSourceOutcome.Duplicate;
                }
                // a rejected package seen again unchanged (its folder could not be moved): no second row, only the move is tried again (B2)
                var again = known.FirstOrDefault(x => x.Status == EngInboxStatus.Rejected && x.OriginalPath == h.RelativePath);
                if (again != null)
                {
                    await TryMoveAsync(h, EngSourceOutcome.Rejected, "این بسته قبلاً رد شده است (بستهٔ شمارهٔ " + again.Id.ToString(CultureInfo.InvariantCulture) + "؛ دلایل در صفحهٔ صندوق ورودی مهندسی).", null, ct);
                    return EngSourceOutcome.Rejected;
                }
            }

            EngPackageContent content = await _source.ReadAsync(h, ct);
            EngParsedPackage p;
            if (content.Problems.Count > 0 || content.Workbook == null)
            {
                p = new EngParsedPackage();
                foreach (string problem in content.Problems) p.Error(null, 0, problem);
                if (content.Problems.Count == 0) p.Error(null, 0, "فایل اکسل بسته خوانده نشد.");
            }
            else
            {
                p = EngPackageParser.Parse(content.Workbook, content.WorkbookName, content.Files);
                AddLayoutWarnings(h, p);
            }

            EngInboxKind kind = p.Info.Kind == EngPackageKind.Project ? EngInboxKind.Project : p.Info.Kind == EngPackageKind.Platform ? EngInboxKind.Platform : (h.LayoutKind == EngPackageKind.Project ? EngInboxKind.Project : EngInboxKind.Platform);
            string platform = p.Info.PlatformCode ?? "", standard = p.Info.Standard ?? "", contract = p.Info.ContractCode ?? "", project = p.Info.ProjectCode ?? "";
            int version = p.Info.PackageVersion;
            string rejectCode = null;

            // the preparer is a real, active user of the system (maker is not checker: he can never check or approve what the agent makes from his package)
            int? originatorId = null;
            if (!p.HasErrors)
            {
                string login = EngText.Norm(p.Info.PreparedBy);
                if (login.Length > 0)
                {
                    string upper = login.ToUpperInvariant();
                    originatorId = await _uow.Set<Entities.Identity.User>().AsNoTracking().Where(u => !u.IsDeleted && u.IsActive && (u.NormalizedUserName == upper || u.UserName == login)).OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
                }
                if (originatorId == null) p.Error("مشخصات", 0, EngInboxText.OriginatorUnknown);
            }

            if (!p.HasErrors)
            {
                IQueryable<EngInboxPackage> sameKey = _uow.Set<EngInboxPackage>().Where(x => !x.Deleted && x.Kind == kind && x.PlatformCode == platform && x.Standard == standard && x.ContractCode == contract && x.ProjectCode == project && Live.Contains(x.Status));
                EngInboxPackage same = await sameKey.Where(x => x.PackageVersion == version).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
                if (same != null && same.ContentHash == p.ContentHash)
                {
                    await TryMoveAsync(h, EngSourceOutcome.Duplicate, EngInboxText.Duplicate + ": همین بسته قبلاً دریافت شده است (بستهٔ شمارهٔ " + same.Id.ToString(CultureInfo.InvariantCulture) + ").", null, ct);
                    return EngSourceOutcome.Duplicate;
                }
                if (same != null) { rejectCode = "VersionConflict"; p.Error(null, 0, EngInboxText.VersionConflict); }
                else
                {
                    int higher = await sameKey.Where(x => x.PackageVersion > version).Select(x => x.PackageVersion).OrderByDescending(x => x).FirstOrDefaultAsync(ct);
                    if (higher > 0) { rejectCode = "OlderVersion"; p.Error(null, 0, EngInboxText.OlderVersion + " (نسخهٔ " + higher.ToString(CultureInfo.InvariantCulture) + ")"); }
                }
            }

            bool rejected = p.HasErrors;
            if (rejected)
            {
                // the same rejected folder, unchanged, is stored once (B2)
                bool stored0 = sig.Length > 0 && await _uow.Set<EngInboxPackage>().AsNoTracking().AnyAsync(x => !x.Deleted && x.Status == EngInboxStatus.Rejected && x.OriginalPath == h.RelativePath && x.SourceSignature == sig, ct);
                if (stored0)
                {
                    await TryMoveAsync(h, EngSourceOutcome.Rejected, "این بسته قبلاً رد شده است.", null, ct);
                    return EngSourceOutcome.Rejected;
                }
            }
            // a rejected package is stored with every text cut to its column (the findings carry the real values and lengths)
            var pkg = new EngInboxPackage
            {
                SourceName = _source.Name, Kind = kind, PlatformCode = Fit(platform, EngPackageParser.Max.PlatformCode), Standard = Fit(standard, EngPackageParser.Max.Standard), ContractCode = Fit(contract, EngPackageParser.Max.ContractCode),
                ProjectCode = Fit(project, EngPackageParser.Max.ProjectCode), PackageVersion = rejected && version < 1 ? 0 : version,
                Title = Fit(p.Info.Title, EngPackageParser.Max.Title), PreparedByText = Fit(p.Info.PreparedBy, EngPackageParser.Max.PreparedBy), OriginatorUserId = rejected ? null : originatorId, SchemaVersion = p.Info.SchemaVersion,
                ContentHash = p.ContentHash, OriginalPath = Fit(h.RelativePath, 500), SourceSignature = Fit(sig, 100),
                Status = rejected ? EngInboxStatus.Rejected : EngInboxStatus.Ingested, RejectCode = rejectCode, FindingsJson = FindingsToJson(p.Findings), ErrorCount = p.ErrorCount, WarningCount = p.WarningCount,
                BomCount = p.Bom.Count, OpcCount = p.Opc.Count, DocCount = p.Docs.Count, MdlCount = p.Mdl.Count, IngestedAt = DateTimeOffset.Now, StatusAt = DateTimeOffset.Now,
                StatusNote = rejected ? "رد شد: " + p.ErrorCount.ToString(CultureInfo.InvariantCulture) + " خطا" : "دریافت شد؛ منتظر ایجنت مهندسی",
            };
            Stamp(pkg, true);
            if (!rejected) AddLines(pkg, p);
            _uow.Set<EngInboxPackage>().Add(pkg);
            await _uow.SaveChangesAsync(ct);
            Failures.TryRemove(h.Id + "|" + sig, out _);

            if (!rejected)
            {
                // a higher version replaces the lower ones of the same key that are still on their way
                var lower = await _uow.Set<EngInboxPackage>().Where(x => !x.Deleted && x.Id != pkg.Id && x.Kind == kind && x.PlatformCode == pkg.PlatformCode && x.Standard == pkg.Standard && x.ContractCode == pkg.ContractCode && x.ProjectCode == pkg.ProjectCode
                    && x.PackageVersion < version && (x.Status == EngInboxStatus.Ingested || x.Status == EngInboxStatus.ApplyBlocked || x.Status == EngInboxStatus.Applied)).ToListAsync(ct);
                foreach (EngInboxPackage old in lower)
                {
                    old.Status = EngInboxStatus.Superseded; old.SupersededByPackageId = pkg.Id; old.StatusAt = DateTimeOffset.Now;
                    old.StatusNote = "جایگزین‌شده با نسخهٔ " + version.ToString(CultureInfo.InvariantCulture);
                    Stamp(old, false);
                }
                if (lower.Count > 0) await _uow.SaveChangesAsync(ct);
            }

            string note = rejected ? RejectionText(pkg, p.Findings) : "بستهٔ شمارهٔ " + pkg.Id.ToString(CultureInfo.InvariantCulture) + " دریافت شد: " + EngInboxText.Name(pkg) + ".";
            string stored = await TryMoveAsync(h, rejected ? EngSourceOutcome.Rejected : EngSourceOutcome.Ingested, note, pkg, ct);
            if (!string.IsNullOrEmpty(stored)) pkg.StoredPath = Fit(stored, 500);
            Stamp(pkg, false);
            await _uow.SaveChangesAsync(ct);
            return rejected ? EngSourceOutcome.Rejected : EngSourceOutcome.Ingested;
        }

        private static void AddLayoutWarnings(EngPackageHandle h, EngParsedPackage p)
        {
            if (h.LayoutKind.HasValue && p.Info.Kind.HasValue && h.LayoutKind != p.Info.Kind) p.Warn(null, 0, "پوشهٔ بسته زیر «" + (h.LayoutKind == EngPackageKind.Platform ? "Platforms" : "Projects") + "» است ولی نوع بسته در فایل «" + (p.Info.Kind == EngPackageKind.Platform ? "پلتفرم" : "پروژه") + "» است؛ محتوای فایل ملاک است.");
            if (h.LayoutKind == EngPackageKind.Platform && p.Info.Kind == EngPackageKind.Platform)
            {
                if (!string.Equals(h.LayoutPlatform, p.Info.PlatformCode, StringComparison.OrdinalIgnoreCase)) p.Warn(null, 0, "پوشهٔ پلتفرم «" + h.LayoutPlatform + "» با «کد پلتفرم» فایل («" + p.Info.PlatformCode + "») یکی نیست؛ محتوای فایل ملاک است.");
                if (!string.Equals(h.LayoutStandard, p.Info.Standard, StringComparison.OrdinalIgnoreCase)) p.Warn(null, 0, "پوشهٔ استاندارد «" + h.LayoutStandard + "» با «استاندارد» فایل («" + p.Info.Standard + "») یکی نیست؛ محتوای فایل ملاک است.");
            }
            if (h.LayoutKind == EngPackageKind.Project && p.Info.Kind == EngPackageKind.Project)
            {
                if (!string.Equals(h.LayoutContract, p.Info.ContractCode, StringComparison.OrdinalIgnoreCase)) p.Warn(null, 0, "پوشهٔ قرارداد «" + h.LayoutContract + "» با «کد قرارداد» فایل («" + p.Info.ContractCode + "») یکی نیست؛ محتوای فایل ملاک است.");
                if (!string.Equals(h.LayoutProject, p.Info.ProjectCode, StringComparison.OrdinalIgnoreCase)) p.Warn(null, 0, "پوشهٔ پروژه «" + h.LayoutProject + "» با «کد پروژه» فایل («" + p.Info.ProjectCode + "») یکی نیست؛ محتوای فایل ملاک است.");
            }
        }

        private void AddLines(EngInboxPackage pkg, EngParsedPackage p)
        {
            int n = 0;
            foreach (EngBomRow b in p.Bom)
            {
                var l = new EngInboxBomLine { SortOrder = ++n, SourceRow = b.SourceRow, Level = b.Level, ParentCode = b.ParentCode, PartCode = b.PartCode, Description = b.Description, Qty = b.Qty, Unit = b.Unit, MakeBuy = (int)b.MakeBuy, Material = b.Material, DrawingNumber = b.DrawingNumber, DrawingRevision = b.DrawingRevision, Note = b.Note };
                Stamp(l, true);
                pkg.BomLines.Add(l);
            }
            n = 0;
            foreach (EngOpcRow o in p.Opc)
            {
                var l = new EngInboxOpcLine { SortOrder = ++n, SourceRow = o.SourceRow, PartCode = o.PartCode, OperationNumber = o.OperationNumber, Description = o.Description, WorkCenterCode = o.WorkCenterCode, SetupMinutes = o.SetupMinutes, RunMinutes = o.RunMinutes, IsInspection = o.IsInspection, IsSpecialProcess = o.IsSpecialProcess, Predecessors = string.Join(",", o.Predecessors), StageCode = o.StageCode };
                Stamp(l, true);
                pkg.OpcLines.Add(l);
            }
            foreach (EngDocRow d in p.Docs)
            {
                var l = new EngInboxDocRef { SourceRow = d.SourceRow, Number = d.Number, Title = d.Title, Revision = d.Revision, DocType = d.DocType, IsRequired = d.Required, FileName = d.FileName, FileSize = d.FileSize, FileSha256 = d.FileSha256 };
                Stamp(l, true);
                pkg.Documents.Add(l);
            }
            foreach (EngMdlRow m in p.Mdl)
            {
                var l = new EngInboxMdlLine { SourceRow = m.SourceRow, Number = m.Number, Title = m.Title, DocType = m.DocType, CommitDate = m.CommitDate };
                Stamp(l, true);
                pkg.MdlLines.Add(l);
            }
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static string FindingsToJson(IEnumerable<EngFinding> findings) =>
            JsonSerializer.Serialize(findings.Select(f => new EngInboxFindingDto { Severity = f.IsError ? "error" : "warning", Sheet = f.Sheet, Row = f.Row, Message = f.Message }).ToList(), Json);

        public static List<EngInboxFindingDto> FindingsOf(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<EngInboxFindingDto>();
            try { return JsonSerializer.Deserialize<List<EngInboxFindingDto>>(json, Json) ?? new List<EngInboxFindingDto>(); }
            catch (JsonException) { return new List<EngInboxFindingDto>(); }
        }

        private static string RejectionText(EngInboxPackage p, IEnumerable<EngFinding> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("این بسته رد شد و وارد سیستم نشد. دلایل (خطاها باید اصلاح شوند؛ هشدارها فقط اطلاع هستند):");
            sb.AppendLine("بسته: " + (p.PackageVersion > 0 ? EngInboxText.Name(p) : p.OriginalPath));
            sb.AppendLine();
            foreach (EngFinding f in findings.OrderByDescending(f => f.IsError).ThenBy(f => f.Sheet).ThenBy(f => f.Row)) sb.AppendLine(f.Describe());
            sb.AppendLine();
            sb.AppendLine("پس از اصلاح، پوشهٔ بسته را دوباره در صندوق ورودی بگذارید (برای تغییر محتوای بستهٔ پذیرفته‌شده، شمارهٔ نسخه را بالا ببرید).");
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------ reading

        private static EngInboxPackageDto ToDto(EngInboxPackage p, EngInboxPackageDto d = null)
        {
            d ??= new EngInboxPackageDto();
            d.Id = p.Id; d.SourceName = p.SourceName; d.Kind = p.Kind; d.PlatformCode = p.PlatformCode; d.Standard = p.Standard; d.ContractCode = p.ContractCode; d.ProjectCode = p.ProjectCode; d.PackageVersion = p.PackageVersion;
            d.Title = p.Title; d.PreparedByText = p.PreparedByText; d.Status = p.Status; d.StatusNote = p.StatusNote; d.RejectCode = p.RejectCode; d.ErrorCount = p.ErrorCount; d.WarningCount = p.WarningCount;
            d.BomCount = p.BomCount; d.OpcCount = p.OpcCount; d.DocCount = p.DocCount; d.MdlCount = p.MdlCount; d.IngestedAt = p.IngestedAt; d.StatusAt = p.StatusAt; d.ApplyBlockReason = p.ApplyBlockReason;
            d.OriginatorUserId = p.OriginatorUserId; d.ProductId = p.ProductId; d.BomHeaderId = p.BomHeaderId; d.RoutingId = p.RoutingId; d.LinkedContractId = p.LinkedContractId; d.LinkedWorkOrderId = p.LinkedWorkOrderId; d.DismissReason = p.DismissReason;
            d.RowVersion = p.RowVersion == null ? null : Convert.ToBase64String(p.RowVersion);
            return d;
        }

        public async Task<ResponseDto<EngInboxStatusDto>> GetStatusAsync(CancellationToken ct)
        {
            if (!CanRead) return ResponseDtoHelper.BadRequest<EngInboxStatusDto>(EngInboxText.NotAllowed);
            EngInboxScanState s = await _uow.Set<EngInboxScanState>().AsNoTracking().Where(x => !x.Deleted).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            var counts = await _uow.Set<EngInboxPackage>().AsNoTracking().Where(x => !x.Deleted).GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
            var dto = new EngInboxStatusDto
            {
                SourceName = _source.Name, IsConfigured = _source.IsConfigured, Location = _source.Location, NotConfiguredReason = _source.IsConfigured ? null : _source.NotConfiguredReason,
                LastScanAt = s?.LastScanAt, LastScanNote = s?.LastNote, LastFound = s?.LastFound ?? 0, LastIngested = s?.LastIngested ?? 0, LastRejected = s?.LastRejected ?? 0, LastDuplicates = s?.LastDuplicates ?? 0,
                CanDismiss = CanOperate,
            };
            foreach (EngInboxStatus st in Enum.GetValues(typeof(EngInboxStatus)).Cast<EngInboxStatus>().OrderBy(x => (int)x))
                dto.Counts.Add(new EngInboxStatusCountDto { Status = st, Count = counts.Where(c => c.Status == st).Select(c => c.Count).FirstOrDefault() });
            if (await global::Services.NasbNiro.Agents.Steps.EngInboxApplyStep.IsPausedAsync(_uow, ct)) dto.ApplyPausedReason = "ایجنت مهندسی (یا گام ساخت BOM و OPC) خاموش است؛ بسته‌ها همان‌طور که هستند می‌مانند و با روشن شدن ایجنت پیش می‌روند.";
            dto.CanScan = CanOperate && dto.IsConfigured;
            dto.ScanDisabledReason = !CanOperate ? "اسکن با واحد مهندسی است." : !dto.IsConfigured ? dto.NotConfiguredReason : null;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<PagedList<EngInboxPackageDto>>> GetPackagesAsync(EngInboxQuery query, CancellationToken ct)
        {
            if (!CanRead) return ResponseDtoHelper.BadRequest<PagedList<EngInboxPackageDto>>(EngInboxText.NotAllowed);
            query ??= new EngInboxQuery();
            IQueryable<EngInboxPackage> q = _uow.Set<EngInboxPackage>().AsNoTracking().Where(x => !x.Deleted);
            if (query.Id.HasValue) q = q.Where(x => x.Id == query.Id.Value);
            if (query.Status.HasValue) q = q.Where(x => x.Status == query.Status.Value);
            if (query.Kind.HasValue) q = q.Where(x => x.Kind == query.Kind.Value);
            string search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
            if (search != null) q = q.Where(x => x.PlatformCode.Contains(search) || x.Standard.Contains(search) || x.ContractCode.Contains(search) || x.ProjectCode.Contains(search) || x.Title.Contains(search));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 200), total, out int number, out int size);
            List<EngInboxPackage> rows = await q.OrderByDescending(x => x.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(r => ToDto(r)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<EngInboxPackageDetailDto>> GetPackageAsync(int id, CancellationToken ct)
        {
            if (!CanRead) return ResponseDtoHelper.BadRequest<EngInboxPackageDetailDto>(EngInboxText.NotAllowed);
            EngInboxPackage p = await _uow.Set<EngInboxPackage>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (p == null) return ResponseDtoHelper.NotFound<EngInboxPackageDetailDto>(EngInboxText.NotFound);
            var d = (EngInboxPackageDetailDto)ToDto(p, new EngInboxPackageDetailDto());
            d.SchemaVersion = p.SchemaVersion; d.ContentHash = p.ContentHash; d.OriginalPath = p.OriginalPath; d.StoredPath = p.StoredPath; d.ApplyAttempts = p.ApplyAttempts; d.LastApplyAt = p.LastApplyAt; d.AppliedAt = p.AppliedAt;
            d.SupersededByPackageId = p.SupersededByPackageId;
            d.CanDismiss = CanOperate && (p.Status == EngInboxStatus.Rejected || p.Status == EngInboxStatus.ApplyBlocked || p.Status == EngInboxStatus.Ingested);
            d.Findings = FindingsOf(p.FindingsJson);
            d.BomLines = (await _uow.Set<EngInboxBomLine>().AsNoTracking().Where(l => l.PackageId == id && !l.Deleted).OrderBy(l => l.SortOrder).ToListAsync(ct))
                .Select(l => new EngInboxBomLineDto { Level = l.Level, ParentCode = l.ParentCode, PartCode = l.PartCode, Description = l.Description, Qty = l.Qty, Unit = l.Unit, MakeBuy = l.MakeBuy, Material = l.Material, DrawingNumber = l.DrawingNumber, DrawingRevision = l.DrawingRevision, Note = l.Note }).ToList();
            d.OpcLines = (await _uow.Set<EngInboxOpcLine>().AsNoTracking().Where(l => l.PackageId == id && !l.Deleted).OrderBy(l => l.SortOrder).ToListAsync(ct))
                .Select(l => new EngInboxOpcLineDto { PartCode = l.PartCode, OperationNumber = l.OperationNumber, Description = l.Description, WorkCenterCode = l.WorkCenterCode, SetupMinutes = l.SetupMinutes, RunMinutes = l.RunMinutes, IsInspection = l.IsInspection, IsSpecialProcess = l.IsSpecialProcess, Predecessors = l.Predecessors, StageCode = l.StageCode }).ToList();
            d.Documents = (await _uow.Set<EngInboxDocRef>().AsNoTracking().Where(l => l.PackageId == id && !l.Deleted).OrderBy(l => l.SourceRow).ToListAsync(ct))
                .Select(l => new EngInboxDocDto { Number = l.Number, Title = l.Title, Revision = l.Revision, DocType = l.DocType, IsRequired = l.IsRequired, FileName = l.FileName, FileSize = l.FileSize, FileSha256 = l.FileSha256 }).ToList();
            d.MdlLines = (await _uow.Set<EngInboxMdlLine>().AsNoTracking().Where(l => l.PackageId == id && !l.Deleted).OrderBy(l => l.SourceRow).ToListAsync(ct))
                .Select(l => new EngInboxMdlDto { Number = l.Number, Title = l.Title, DocType = l.DocType, CommitDate = l.CommitDate }).ToList();
            if (p.ProductId.HasValue) d.ProductCode = await _uow.Set<PbProduct>().AsNoTracking().Where(x => x.Id == p.ProductId.Value).Select(x => x.Code).FirstOrDefaultAsync(ct);
            if (p.BomHeaderId.HasValue)
            {
                var b = await _uow.Set<PbBomHeader>().AsNoTracking().Where(x => x.Id == p.BomHeaderId.Value).Select(x => new { x.Code, x.Revision, x.Status }).FirstOrDefaultAsync(ct);
                if (b != null) { d.BomCode = b.Code; d.BomRevision = b.Revision; d.BomStatus = b.Status.ToString(); d.BomStatusCode = (int)b.Status; }
            }
            if (p.RoutingId.HasValue)
            {
                var r = await _uow.Set<PbOpcRouting>().AsNoTracking().Where(x => x.Id == p.RoutingId.Value).Select(x => new { x.PartNumber, x.Revision, x.Status }).FirstOrDefaultAsync(ct);
                if (r != null) { d.RoutingPartNumber = r.PartNumber; d.RoutingRevision = r.Revision; d.RoutingStatus = r.Status.ToString(); d.RoutingStatusCode = (int)r.Status; }
            }
            return ResponseDtoHelper.Ok(d);
        }

        // ------------------------------------------------------------------------------------------------ dismiss

        public async Task<ResponseDto<EngInboxPackageDto>> DismissAsync(EngInboxDismissDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<EngInboxPackageDto>(EngInboxText.AiNeverDecides);
            if (!CanOperate) return ResponseDtoHelper.BadRequest<EngInboxPackageDto>(EngInboxText.NotAllowed);
            if (dto == null) return ResponseDtoHelper.BadRequest<EngInboxPackageDto>(AgentText.InvalidData);
            string reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            if (reason == null || reason.Length < 15) return ResponseDtoHelper.BadRequest<EngInboxPackageDto>(EngInboxText.ReasonTooShort);
            if (reason.Length > 1000) reason = reason.Substring(0, 1000);
            if (string.IsNullOrWhiteSpace(dto.RowVersion)) return ResponseDtoHelper.BadRequest<EngInboxPackageDto>(EngInboxText.RowVersionRequired);
            EngInboxPackage p = await _uow.Set<EngInboxPackage>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (p == null) return ResponseDtoHelper.NotFound<EngInboxPackageDto>(EngInboxText.NotFound);
            if (p.Status != EngInboxStatus.Rejected && p.Status != EngInboxStatus.ApplyBlocked && p.Status != EngInboxStatus.Ingested) return ResponseDtoHelper.BadRequest<EngInboxPackageDto>(EngInboxText.DismissStatus);
            try { _uow.Entry(p).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
            catch (FormatException) { return ResponseDtoHelper.BadRequest<EngInboxPackageDto>(AgentText.InvalidData); }
            p.Status = EngInboxStatus.Dismissed; p.DismissedByUserId = _user.UserId; p.DismissReason = reason; p.StatusAt = DateTimeOffset.Now;
            p.StatusNote = "کنار گذاشته شد: " + AgentText.Cut(reason, 200);
            Stamp(p, false);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { AgentDb.Clear(_uow); return ResponseDtoHelper.BadRequest<EngInboxPackageDto>(EngInboxText.Concurrent); }
            return ResponseDtoHelper.Ok(ToDto(p));
        }
    }
}
