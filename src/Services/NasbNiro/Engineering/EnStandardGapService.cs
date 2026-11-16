using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    /// <summary>The standard-gap report: for a platform ordered under another standard, the engineering agent compares the Approved list of the standard the product is assessed against with the Approved list of the standard of the order.</summary>
    internal static class EnGaps
    {
        public const string KeyPrefix = "EN-GAP-";
        public const int NoteMin = 30;

        public static bool PreApproval(EnCaseStatus s) => s == EnCaseStatus.AwaitingEngConfirm || s == EnCaseStatus.AwaitingCeoApproval;

        public sealed class Plan
        {
            public string BaseCode;
            public int BaseRevision, TargetRevision;
            public bool TargetAnyList, Needed;
            public int? ProductId;
        }

        public static string GapHash(EnStandardGap g, IEnumerable<EnStandardGapLine> lines, string note) =>
            EnHash.Of(string.Join("|", "GAP", EnText.Num(g.CaseId), EnHash.Norm(g.BaseCode), EnHash.Norm(g.TargetCode), EnText.Num(g.BaseRevision), EnText.Num(g.TargetRevision),
                string.Join(";", lines.OrderBy(l => l.Seq).Select(l => EnText.Num((int)l.Kind) + "/" + EnDossierBuilder.TypeTitle(l.DocumentType, l.Title) + "/" + (l.BaseRequired ? "1" : "0") + (l.TargetRequired ? "1" : "0"))), EnHash.Norm(note)));

        /// <summary>What the case needs now: a gap is needed when the platform has dossiers and none of them is under the standard of the order.</summary>
        public static async Task<Plan> PlanOfAsync(IUnitOfWork uow, EnCase c, CancellationToken ct)
        {
            Plan p = new Plan();
            string target = c.ReferenceCode;
            if (c.ProposedClassification != EnClassification.Platform || !c.ProposedProductId.HasValue || string.IsNullOrWhiteSpace(target) || !PreApproval(c.Status)) return p;
            int pid = c.ProposedProductId.Value; p.ProductId = pid;
            List<string> codes = await uow.Set<EnPlatformDossier>().AsNoTracking().Where(d => !d.Deleted && d.ProductId == pid).Select(d => d.ReferenceCode).ToListAsync(ct);
            if (codes.Count == 0 || codes.Any(x => x == target)) return p;
            p.Needed = true;
            var counts = await uow.Set<EnPlatformDocument>().AsNoTracking().Where(x => !x.Dossier.Deleted && x.Dossier.ProductId == pid && x.State == EnLineState.Available).GroupBy(x => x.Dossier.ReferenceCode).Select(g => new { Code = g.Key, N = g.Count() }).ToListAsync(ct);
            var best = counts.OrderByDescending(x => x.N).ThenBy(x => x.Code, StringComparer.Ordinal).FirstOrDefault();
            p.BaseCode = best?.Code;
            if (p.BaseCode != null) p.BaseRevision = await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == p.BaseCode).Select(l => l.Revision).FirstOrDefaultAsync(ct);
            p.TargetRevision = await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == target).Select(l => l.Revision).FirstOrDefaultAsync(ct);
            p.TargetAnyList = await uow.Set<EnReferenceList>().AsNoTracking().AnyAsync(l => !l.Deleted && l.Code == target, ct);
            return p;
        }

        public static bool Same(EnStandardGap g, Plan p, string target) => g.BaseCode == p.BaseCode && g.TargetCode == target && g.BaseRevision == p.BaseRevision && g.TargetRevision == p.TargetRevision;

        public static async Task<EnStandardGap> LiveAsync(IUnitOfWork uow, int caseId, CancellationToken ct) =>
            await uow.Set<EnStandardGap>().AsNoTracking().Where(g => !g.Deleted && g.CaseId == caseId && g.Status != EnGapStatus.Void).OrderByDescending(g => g.Id).FirstOrDefaultAsync(ct);

        /// <summary>The chokepoint merged into the refusal of the CEO approval of the case: null when it may pass.</summary>
        public static async Task<string> ApproveRefusalAsync(IUnitOfWork uow, EnCase c, CancellationToken ct)
        {
            Plan p = await PlanOfAsync(uow, c, ct);
            EnStandardGap live = await LiveAsync(uow, c.Id, ct);
            if (live != null && live.Status == EnGapStatus.Draft) return "گزارش تفاوت استاندارد " + live.Number + " هنوز توسط مدیر مهندسی تایید نشده است.";
            if (!p.Needed) return null;
            if (live == null || !Same(live, p, c.ReferenceCode)) return "گزارش تفاوت استاندارد (" + c.ReferenceCode + " در برابر استاندارد فعلی پلتفرم) هنوز تهیه نشده یا کهنه شده است؛ ایجنت مهندسی آن را می‌سازد و مدیر مهندسی تایید می‌کند.";
            return live.Status == EnGapStatus.Confirmed ? null : "گزارش تفاوت استاندارد " + live.Number + " هنوز توسط مدیر مهندسی تایید نشده است.";
        }

        public static async Task SyncAsync(INotificationService notify, EnStandardGap g, CancellationToken ct)
        {
            await OfficeGates.SyncAsync(notify, KeyPrefix + EnText.Num(g.Id), g.Status == EnGapStatus.Draft, "Engineering", nameof(EnStandardGap), g.Id,
                "گزارش تفاوت استاندارد " + g.Number + " منتظر تایید مدیر مهندسی است: " + EnText.Cut(g.Summary, 150), "/engineering/standard-gaps/" + EnText.Num(g.Id), EnRoles.Manager + "," + EnRoles.Ceo, ct, null, Entities.Model.NasbNiro.Office.OfInboxKind.Approval);
        }

        /// <summary>Idempotent: voids the live gap that no longer fits the case and builds the one that does.</summary>
        public static async Task<string> BuildAsync(IUnitOfWork uow, EnCase c, IEnReferenceService refs, INotificationService notify, int me, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            Plan p = await PlanOfAsync(uow, c, ct);
            List<EnStandardGap> lives = await uow.Set<EnStandardGap>().Include(g => g.Lines).Where(g => !g.Deleted && g.CaseId == c.Id && g.Status != EnGapStatus.Void).ToListAsync(ct);
            string message = null;
            foreach (EnStandardGap old in lives.Where(g => !p.Needed || !Same(g, p, c.ReferenceCode)).ToList())
            {
                if (!PreApproval(c.Status)) continue;
                old.Status = EnGapStatus.Void; old.ModifyDate = now; old.ModifierUserId = me; lives.Remove(old);
                message = "گزارش " + old.Number + " باطل شد (استاندارد یا فهرست مرجع تغییر کرده است).";
            }
            await uow.SaveChangesAsync(ct);
            foreach (EnStandardGap v in await uow.Set<EnStandardGap>().AsNoTracking().Where(g => !g.Deleted && g.CaseId == c.Id && g.Status == EnGapStatus.Void).ToListAsync(ct)) await SyncAsync(notify, v, ct);
            if (!p.Needed || lives.Count > 0) return message ?? "گزارش تفاوت استاندارد از پیش وجود دارد.";

            string target = c.ReferenceCode;
            EnReferenceList baseList = p.BaseCode == null ? null : await uow.Set<EnReferenceList>().AsNoTracking().Include(l => l.Lines).Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == p.BaseCode).FirstOrDefaultAsync(ct);
            EnReferenceList targetList = p.TargetRevision == 0 ? null : await uow.Set<EnReferenceList>().AsNoTracking().Include(l => l.Lines).Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == target).FirstOrDefaultAsync(ct);
            PbProduct product = await uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.ProductId.Value, ct);
            EnStandardGap gap = new EnStandardGap { CaseId = c.Id, ProductId = p.ProductId, BaseCode = p.BaseCode, TargetCode = target, BaseRevision = p.BaseRevision, TargetRevision = p.TargetRevision, Status = EnGapStatus.Draft, CreateDate = now, CreatorUserId = me, ModifyDate = now, ModifierUserId = me };
            gap.Number = await EnNumbers.NextAsync(uow, EnNumbers.StandardGap, "ESG", now.DateTime, ct);
            string productText = product?.Code ?? EnText.Num(p.ProductId.Value);
            if (baseList != null && targetList != null)
            {
                var reusable = (await uow.Set<EnPlatformDocument>().AsNoTracking().Where(x => !x.Dossier.Deleted && x.Dossier.ProductId == p.ProductId.Value && x.State == EnLineState.Available && (x.DocumentId != null || x.SystemProven)).OrderBy(x => x.Id).Select(x => new { x.Id, x.DocumentType, x.Title }).ToListAsync(ct))
                    .GroupBy(x => EnDossierBuilder.TypeTitle(x.DocumentType, x.Title)).ToDictionary(g => g.Key, g => g.First().Id);
                int seq = 0;
                foreach (EnReferenceLine t in targetList.Lines.OrderBy(l => l.Seq))
                {
                    string k = EnDossierBuilder.TypeTitle(t.DocumentType, t.Title);
                    EnReferenceLine b = baseList.Lines.FirstOrDefault(x => EnDossierBuilder.TypeTitle(x.DocumentType, x.Title) == k);
                    EnGapKind kind = b == null ? EnGapKind.Added : b.Required != t.Required ? EnGapKind.RequiredChanged : EnGapKind.Same;
                    gap.Lines.Add(new EnStandardGapLine { Seq = ++seq, Kind = kind, DocumentType = t.DocumentType, Discipline = t.Discipline, Title = t.Title, BaseRequired = b?.Required ?? false, TargetRequired = t.Required, ReusableFromDossierLineId = reusable.TryGetValue(k, out int rid) ? rid : (int?)null });
                }
                foreach (EnReferenceLine b in baseList.Lines.OrderBy(l => l.Seq).Where(b => !targetList.Lines.Any(t => EnDossierBuilder.TypeTitle(t.DocumentType, t.Title) == EnDossierBuilder.TypeTitle(b.DocumentType, b.Title))))
                    gap.Lines.Add(new EnStandardGapLine { Seq = ++seq, Kind = EnGapKind.Removed, DocumentType = b.DocumentType, Discipline = b.Discipline, Title = b.Title, BaseRequired = b.Required, TargetRequired = false });
                gap.AddedCount = gap.Lines.Count(l => l.Kind == EnGapKind.Added); gap.RemovedCount = gap.Lines.Count(l => l.Kind == EnGapKind.Removed); gap.RequiredChangedCount = gap.Lines.Count(l => l.Kind == EnGapKind.RequiredChanged);
                gap.ReusableCount = gap.Lines.Count(l => l.Kind != EnGapKind.Removed && l.ReusableFromDossierLineId.HasValue);
                gap.Summary = "پلتفرم " + productText + " تاکنون با استاندارد " + p.BaseCode + " ارزیابی شده و سفارش تحت استاندارد " + target + " است: " + EnText.Num(gap.AddedCount) + " مدرک افزوده، " + EnText.Num(gap.RemovedCount) + " حذف‌شده، " + EnText.Num(gap.RequiredChangedCount) + " با الزام تغییرکرده و " + EnText.Num(gap.Lines.Count(l => l.Kind == EnGapKind.Same)) + " یکسان؛ فایل " + EnText.Num(gap.ReusableCount) + " مدرک قابل استفادهٔ مجدد است.";
            }
            else
            {
                string head = "پلتفرم " + productText + " تاکنون " + (p.BaseCode == null ? "مدرک موجودی با هیچ استانداردی ندارد" : "با استاندارد " + p.BaseCode + " ارزیابی شده") + " و سفارش تحت استاندارد " + target + " است؛ ";
                if (targetList == null)
                {
                    gap.Summary = head + "فهرست مدارک تاییدشدهٔ استاندارد " + target + " هنوز تعریف نشده است و مقایسه ممکن نیست.";
                    if (!p.TargetAnyList && baseList != null && refs != null)
                    {
                        EnSaveReferenceDto dto = new EnSaveReferenceDto
                        {
                            Code = EnText.Cut(target, 60), Title = EnText.Cut("فهرست مدارک مرجع — " + target, 300), StandardSource = EnText.Cut(baseList.StandardSource, 200),
                            Note = EnText.Cut("نسخهٔ بررسی‌نشده از " + p.BaseCode + " که باید با متن استاندارد " + target + " تطبیق داده شود.", 1000),
                            Lines = baseList.Lines.OrderBy(l => l.Seq).Select(l => new EnReferenceLineDto { DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, Required = l.Required, TenderStage = l.TenderStage, ClientSubmittal = l.ClientSubmittal, StandardClause = l.StandardClause, Note = l.Note, Units = l.Units }).ToList(),
                        };
                        ResponseDto<EnReferenceListDto> r = await refs.SaveListAsync(dto, ct);
                        gap.Summary += r.Success ? " پیش‌نویس فهرست استاندارد " + target + " (کپی بررسی‌نشدهٔ " + p.BaseCode + ") ساخته شد تا مهندسی آن را با متن استاندارد تطبیق دهد." : " ساخت پیش‌نویس فهرست خودکار نشد.";
                    }
                }
                else gap.Summary = head + "فهرست مدارک تاییدشده‌ای برای استاندارد پایه وجود ندارد و مقایسه ممکن نیست.";
            }
            gap.Summary = EnText.Cut(gap.Summary, 2000);
            gap.ContentHash = GapHash(gap, gap.Lines, null);
            uow.Set<EnStandardGap>().Add(gap);
            EnEvents.Add(uow, c, "standard-gap-built", me, gap.Number);
            await uow.SaveChangesAsync(ct);
            await SyncAsync(notify, gap, ct);
            return "گزارش تفاوت استاندارد " + gap.Number + " ساخته شد.";
        }
    }

    public interface IEnStandardGapService
    {
        Task<ResponseDto<PagedList<EnGapDto>>> GetGapsAsync(EnGapQuery query, CancellationToken ct);
        Task<ResponseDto<EnGapDto>> GetGapAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnGapDto>> ConfirmAsync(EnGapConfirmDto dto, CancellationToken ct);
    }

    /// <summary>The standard-gap reports: read, and the confirmation of the head of engineering (the effect on materials and design).</summary>
    public class EnStandardGapService : IEnStandardGapService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public EnStandardGapService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;

        private static string ConfirmReason(EnStandardGap g, bool manager, bool seat) =>
            !manager ? EnText.NotAllowed : seat ? EnText.AiNever : g.Status != EnGapStatus.Draft ? "فقط گزارش تفاوت در انتظار تایید تایید می‌شود." : null;

        private async Task<List<EnGapDto>> DtosAsync(List<EnStandardGap> rows, CancellationToken ct)
        {
            bool manager = _user.IsInRole(EnRoles.Manager);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.Select(r => r.ConfirmedByUserId), ct);
            List<int> caseIds = rows.Select(r => r.CaseId).Distinct().ToList(), productIds = rows.Where(r => r.ProductId.HasValue).Select(r => r.ProductId.Value).Distinct().ToList();
            Dictionary<int, string> cases = await _uow.Set<EnCase>().AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
            Dictionary<int, string> products = await _uow.Set<PbProduct>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            List<EnGapDto> result = new List<EnGapDto>();
            foreach (EnStandardGap g in rows)
            {
                string why = ConfirmReason(g, manager, seat);
                result.Add(new EnGapDto
                {
                    Id = g.Id, Number = g.Number, CaseId = g.CaseId, CaseNumber = cases.TryGetValue(g.CaseId, out string cn) ? cn : null, ProductId = g.ProductId, ProductCode = g.ProductId.HasValue && products.TryGetValue(g.ProductId.Value, out string pc) ? pc : null,
                    BaseCode = g.BaseCode, TargetCode = g.TargetCode, BaseRevision = g.BaseRevision, TargetRevision = g.TargetRevision, Status = (int)g.Status, StatusText = StatusText(g.Status),
                    AddedCount = g.AddedCount, RemovedCount = g.RemovedCount, RequiredChangedCount = g.RequiredChangedCount, ReusableCount = g.ReusableCount, Summary = g.Summary, EngineeringImpactNote = g.EngineeringImpactNote,
                    ConfirmedByName = names.Name(g.ConfirmedByUserId), ConfirmedAtText = EnText.Stamp(g.ConfirmedAt), RowVersion = EnConcurrency.Text(g.RowVersion), CanConfirm = why == null, ConfirmBlockedReason = why,
                    Lines = g.Lines.OrderBy(l => l.Seq).Select(l => new EnGapLineDto { Seq = l.Seq, Kind = (int)l.Kind, KindText = KindText(l.Kind), DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, BaseRequired = l.BaseRequired, TargetRequired = l.TargetRequired, ReusableFromDossierLineId = l.ReusableFromDossierLineId }).ToList(),
                });
            }
            return result;
        }

        internal static string StatusText(EnGapStatus s) => s == EnGapStatus.Draft ? "منتظر تایید مدیر مهندسی" : s == EnGapStatus.Confirmed ? "تاییدشده" : "باطل";
        internal static string KindText(EnGapKind k) => k == EnGapKind.Added ? "افزوده با استاندارد سفارش" : k == EnGapKind.Removed ? "حذف‌شده در استاندارد سفارش" : k == EnGapKind.RequiredChanged ? "الزام تغییر کرده" : "یکسان";

        public async Task<ResponseDto<PagedList<EnGapDto>>> GetGapsAsync(EnGapQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnGapDto>>(EnText.NotAllowed);
            query ??= new EnGapQuery();
            IQueryable<EnStandardGap> q = _uow.Set<EnStandardGap>().AsNoTracking().Include(g => g.Lines).Where(g => !g.Deleted);
            if (query.Status.HasValue) { EnGapStatus s = (EnGapStatus)query.Status.Value; q = q.Where(g => g.Status == s); }
            if (query.CaseId.HasValue) q = q.Where(g => g.CaseId == query.CaseId.Value);
            if (query.OnlyOpen) q = q.Where(g => g.Status == EnGapStatus.Draft);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(g => g.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<EnGapDto>> GetGapAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnGapDto>(EnText.NotAllowed);
            EnStandardGap g = await _uow.Set<EnStandardGap>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (g == null) return EnText.NotFound<EnGapDto>("گزارش تفاوت استاندارد پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnStandardGap> { g }, ct))[0]);
        }

        public async Task<ResponseDto<EnGapDto>> ConfirmAsync(EnGapConfirmDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnGapDto>(EnText.InvalidData);
            EnStandardGap g = await _uow.Set<EnStandardGap>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (g == null) return EnText.NotFound<EnGapDto>("گزارش تفاوت استاندارد پیدا نشد.");
            if (!EnConcurrency.Apply(_uow, g, dto.RowVersion)) return EnText.Bad<EnGapDto>(EnText.InvalidData);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            string note = EnText.Clean(dto.EngineeringImpactNote);
            string why = ConfirmReason(g, _user.IsInRole(EnRoles.Manager), seat) ?? EnText.ReasonShort(note, EnGaps.NoteMin, "توضیح اثر بر مواد و طراحی") ?? EnText.Over(note, 2000, "توضیح اثر بر مواد و طراحی");
            if (why != null) return EnText.Bad<EnGapDto>(why);
            EnCase c = await _uow.Set<EnCase>().FirstOrDefaultAsync(x => x.Id == g.CaseId, ct);
            EnGaps.Plan plan = c == null ? new EnGaps.Plan() : await EnGaps.PlanOfAsync(_uow, c, ct);
            if (c == null || !plan.Needed || !EnGaps.Same(g, plan, c.ReferenceCode)) return EnText.Bad<EnGapDto>("گزارش کهنه شده است (استاندارد یا فهرست مرجع تغییر کرده)؛ ایجنت مهندسی گزارش تازه می‌سازد.");
            DateTimeOffset now = DateTimeOffset.Now;
            g.EngineeringImpactNote = note; g.Status = EnGapStatus.Confirmed; g.ConfirmedByUserId = Me; g.ConfirmedAt = now; g.ContentHash = EnGaps.GapHash(g, g.Lines, note); g.ModifyDate = now; g.ModifierUserId = Me;
            EnEvents.Add(_uow, c, "standard-gap-confirmed", Me, g.Number);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnGapDto>(EnText.Concurrency); }
            await EnGaps.SyncAsync(_notify, g, ct);
            return await GetGapAsync(g.Id, ct);
        }
    }
}
