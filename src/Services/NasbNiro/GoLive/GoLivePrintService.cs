using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.Contracts;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.GoLiveDtos;

namespace Services.NasbNiro.GoLive
{
    /// <summary>The outcome of a stamp: the PDF, or a Persian refusal with the HTTP status the caller answers (422 = fall back to the stamped HTML print, 429 = one stamp at a time).</summary>
    public sealed class GlStampOutcome
    {
        public byte[] Pdf { get; set; }
        public string Error { get; set; }
        public int Status { get; set; } = 200;
        public string FileName { get; set; }
    }

    public interface IGlPrintService
    {
        Task<ResponseDto<GlPrintPreparedDto>> PrepareAsync(GlPrintPrepareDto dto, CancellationToken ct);
        Task<GlStampOutcome> StampAsync(int printId, PdfStampImage header, PdfStampImage footer, CancellationToken ct);
    }

    /// <summary>
    /// Package G (fix round 1, H3, ISO 9001 7.5.3): the controlled printed copy is made on the server only. The browser asks for a print of a REVISION (and one of its files); the server checks that the revision is in force, builds the
    /// stamp text, numbers the copy and records the print (who, when, which revision, copy number, SHA-256 of the source and of the output) in the print log, which is part of the audit chain. The file is loaded from the store by the server:
    /// a PDF uploaded by a user is never stamped and never comes back as «کنترل‌شده». A revision that is not in force (or a copy asked for as uncontrolled) is stamped «نسخه کنترل‌نشده». One stamp at a time per user; a file above the
    /// size limit (setting) is refused with the fallback to the HTML print. The Persian pixels of the bands are drawn by the browser with the programme's font; their size is checked, their text is the server's header (kept in the log).
    /// </summary>
    public class GlPrintService : PbServiceBase, IGlPrintService
    {
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> PerUser = new ConcurrentDictionary<int, SemaphoreSlim>();
        private readonly IDocumentService _documents;

        public GlPrintService(IUnitOfWork uow, IInventoryUserContext user, IDocumentService documents) : base(uow, user) { _documents = documents; }

        private static string AsBuiltText(PcDocStatus s) => s == PcDocStatus.AsBuilt ? "ساخت‌شده (As-Built)" : s == PcDocStatus.Released ? "منتشرشده" : "—";
        private static string StatusText(PcDocStatus s) => s == PcDocStatus.Draft ? "پیش‌نویس" : s == PcDocStatus.UnderReview ? "در بررسی" : s == PcDocStatus.Approved ? "تاییدشده (منتشر نشده)" : s == PcDocStatus.Superseded ? "جایگزین‌شده" : s == PcDocStatus.Obsolete ? "منسوخ" : AsBuiltText(s);

        public async Task<ResponseDto<GlPrintPreparedDto>> PrepareAsync(GlPrintPrepareDto dto, CancellationToken ct)
        {
            if (IsAi || await GlText.IsAiSeatUserAsync(Uow, User.UserId, ct)) return ResponseDtoHelper.BadRequest<GlPrintPreparedDto>(GlText.AiNever);
            if (!InRole(EdmsRoles.ShopFloor + "," + EdmsRoles.Readers)) return ResponseDtoHelper.BadRequest<GlPrintPreparedDto>(GlText.NotAllowed);
            if (dto == null || dto.RevisionId <= 0) return ResponseDtoHelper.BadRequest<GlPrintPreparedDto>(GlText.InvalidData);
            PcDocumentRevision r = await Uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.RevisionId && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<GlPrintPreparedDto>("بازنگری پیدا نشد.");
            var d = await Uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(x => x.Id == r.DeliverableId).Select(x => new { x.DocumentNumber, x.Title }).FirstAsync(ct);
            PcDocumentRevisionFile f = await Uow.Set<PcDocumentRevisionFile>().AsNoTracking().Where(x => x.RevisionId == r.Id && !x.Deleted && (dto.DocumentId == null || x.DocumentId == dto.DocumentId)).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (f == null) return ResponseDtoHelper.NotFound<GlPrintPreparedDto>("فایلی برای این بازنگری ثبت نشده است.");
            // a revision in force is printed as a controlled copy; a draft, a superseded or an obsolete one only as an uncontrolled one; a reader may also ask for the plain uncontrolled copy
            bool controlled = EdmsRules.InForce(r.Status) && !dto.Uncontrolled;
            DateTimeOffset now = DateTimeOffset.Now;
            string userName = User.UserId > 0 ? (await InventoryUsers.GetNamesAsync(Uow, new int?[] { User.UserId }, ct)).Name(User.UserId) ?? ("#" + User.UserId) : "—";
            string printedAt = FiscalCalendars.Current.TextTime(now.DateTime);
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(Uow, ct);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int copy = 0;
                if (controlled) copy = (await Uow.Set<GlPrintLog>().Where(x => x.RevisionId == r.Id && x.Controlled && !x.Deleted).MaxAsync(x => (int?)x.CopyNumber, ct) ?? 0) + 1;
                string released = r.ReleasedAt.HasValue ? FiscalCalendars.Current.Text(r.ReleasedAt.Value.LocalDateTime.Date) : "-";
                string header = controlled ? GlStampText.Header(d.DocumentNumber, r.Code, released, printedAt, userName, copy) : GlStampText.UncontrolledHeader(d.DocumentNumber, r.Code, StatusText(r.Status) + (EdmsRules.InForce(r.Status) ? " (در اجرا)" : string.Empty), printedAt, userName);
                var log = new GlPrintLog
                {
                    RevisionId = r.Id, DeliverableId = r.DeliverableId, SourceFile = f.DocumentId.ToString("D"), DocumentNumber = GlText.Cut(d.DocumentNumber, 200), RevisionCode = GlText.Cut(r.Code, 50), Controlled = controlled, CopyNumber = copy,
                    Header = GlText.Cut(header, 600), PrintedByUserId = User.UserId, PreparedAt = now,
                };
                Stamp(log, true);
                Uow.Set<GlPrintLog>().Add(log);
                try { await Uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { Uow.Set<GlPrintLog>().Local.Where(x => x.Id == 0).ToList().ForEach(x => Uow.Entry(x).State = EntityState.Detached); continue; }   // another print took the copy number
                return ResponseDtoHelper.Ok(new GlPrintPreparedDto
                {
                    PrintId = log.Id, Controlled = controlled, CopyNumber = copy, Header = header, Footer = controlled ? GlStampText.Footer : GlStampText.Uncontrolled, FileName = f.FileName, DocumentId = f.DocumentId,
                    RevisionCode = r.Code, DocumentNumber = d.DocumentNumber, MaxMegabytes = (int)st[GlSettingKeys.StampMaxMb],
                });
            }
            return ResponseDtoHelper.BadRequest<GlPrintPreparedDto>(GlText.Concurrent);
        }

        public async Task<GlStampOutcome> StampAsync(int printId, PdfStampImage header, PdfStampImage footer, CancellationToken ct)
        {
            if (IsAi || await GlText.IsAiSeatUserAsync(Uow, User.UserId, ct)) return new GlStampOutcome { Status = 400, Error = GlText.AiNever };
            if (!InRole(EdmsRoles.ShopFloor + "," + EdmsRoles.Readers)) return new GlStampOutcome { Status = 400, Error = GlText.NotAllowed };
            GlPrintLog log = await Uow.Set<GlPrintLog>().FirstOrDefaultAsync(x => x.Id == printId && !x.Deleted, ct);
            if (log == null) return new GlStampOutcome { Status = 400, Error = "چاپ پیدا نشد؛ دوباره از صفحهٔ سند چاپ را بزنید." };
            if (log.PrintedByUserId != User.UserId) return new GlStampOutcome { Status = 400, Error = GlText.NotAllowed };
            if (log.StampedAt.HasValue) return new GlStampOutcome { Status = 400, Error = "این چاپ پیش‌تر مهر شده است؛ برای نسخهٔ تازه دوباره چاپ را بزنید تا شمارهٔ نسخه جداگانه ثبت شود." };
            // the revision must still be what the log says: a controlled copy of a revision that was superseded meanwhile is refused
            if (log.Controlled)
            {
                PcDocStatus status = await Uow.Set<PcDocumentRevision>().AsNoTracking().Where(x => x.Id == log.RevisionId).Select(x => x.Status).FirstOrDefaultAsync(ct);
                if (!EdmsRules.InForce(status)) return new GlStampOutcome { Status = 400, Error = "این بازنگری دیگر در اجرا نیست؛ چاپ کنترل‌شده ممکن نیست. دوباره چاپ را بزنید (نسخهٔ کنترل‌نشده)." };
            }
            SemaphoreSlim gate = PerUser.GetOrAdd(User.UserId, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0, ct)) return new GlStampOutcome { Status = 429, Error = "چاپ دیگری از شما در حال مهر شدن است؛ چند لحظه صبر کنید." };
            try
            {
                int maxMb = (int)(await QualitySettingKeys.LoadAsync(Uow, ct))[GlSettingKeys.StampMaxMb];
                ResponseDocumentDtoHolder file = await LoadAsync(Guid.Parse(log.SourceFile), maxMb, ct);
                if (file.Error != null) return new GlStampOutcome { Status = 422, Error = file.Error };
                byte[] stamped;
                try { stamped = PdfStamper.Stamp(file.Bytes, header, footer); }
                catch (PdfStampException ex) { return new GlStampOutcome { Status = 422, Error = ex.Message }; }
                log.SourceSha256 = Hex(file.Bytes); log.OutputSha256 = Hex(stamped); log.StampedAt = DateTimeOffset.Now;
                Stamp(log, false);
                await Uow.SaveChangesAsync(ct);
                return new GlStampOutcome { Pdf = stamped, FileName = (log.Controlled ? "controlled-copy-" : "uncontrolled-copy-") + log.RevisionId + ".pdf" };
            }
            finally { gate.Release(); }
        }

        private sealed class ResponseDocumentDtoHolder { public byte[] Bytes; public string Error; }

        private async Task<ResponseDocumentDtoHolder> LoadAsync(Guid documentId, int maxMb, CancellationToken ct)
        {
            var res = new ResponseDocumentDtoHolder();
            var dl = await _documents.DownloadDocumentAsync(documentId);
            if (dl == null || dl.FileStream == null) { res.Error = "فایل سند پیدا نشد."; return res; }
            using (dl.FileStream)
            {
                long limit = (long)maxMb * 1024 * 1024;
                if (dl.FileStream.Length > limit) { res.Error = $"حجم فایل بیش از {maxMb.ToString(System.Globalization.CultureInfo.InvariantCulture)} مگابایت است؛ چاپ با برگهٔ مهرشدهٔ برنامه انجام می‌شود."; return res; }
                using var ms = new MemoryStream((int)dl.FileStream.Length);
                await dl.FileStream.CopyToAsync(ms, ct);
                res.Bytes = ms.ToArray();
            }
            if (!PdfStamper.LooksLikePdf(res.Bytes)) { res.Error = "فایل PDF نیست؛ چاپ با برگهٔ مهرشدهٔ برنامه انجام می‌شود."; res.Bytes = null; }
            return res;
        }

        private static string Hex(byte[] data) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(data)); }
    }
}
