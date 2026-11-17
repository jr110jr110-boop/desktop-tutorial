using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    internal static class EnDeviations
    {
        public const string LineType = nameof(EnPlatformDocument);
        public const string ListType = nameof(EnReferenceList);
        public const int InstructionMin = 30;

        public static bool Open(EnDeviation d) => d.Status == EnDeviationStatus.Requested || d.Status == EnDeviationStatus.ManagerApproved;

        public static string LineHash(EnPlatformDocument line, string code, string instruction) =>
            EnHash.Of("NA|" + EnText.Num(line.Id) + "|" + EnHash.Norm(code) + "|" + EnHash.Norm(line.DocumentType) + "|" + EnHash.Norm(line.Title) + "|" + (line.Required ? "1" : "0") + "|" + EnHash.Norm(instruction));

        public static string ListHash(EnReferenceList l) => EnHash.Of("LS|" + EnText.Num(l.Id) + "|" + l.ContentHash);

        /// <summary>What a new revision removes, un-marks Required or un-marks ClientSubmittal compared with the previous APPROVED revision of the same Code (empty for a first revision or one that only adds or tightens).</summary>
        public static List<string> Loosened(EnReferenceList previous, EnReferenceList next)
        {
            List<string> result = new List<string>();
            if (previous == null) return result;
            foreach (EnReferenceLine p in previous.Lines.Where(x => x.Required || x.ClientSubmittal).OrderBy(x => x.Seq))
            {
                EnReferenceLine n = next.Lines.FirstOrDefault(x => EnDossierBuilder.TypeTitle(x.DocumentType, x.Title) == EnDossierBuilder.TypeTitle(p.DocumentType, p.Title));
                if (n == null) result.Add("حذف «" + p.Title + "»");
                else
                {
                    if (p.Required && !n.Required) result.Add("غیرالزامی شدن «" + p.Title + "»");
                    if (p.ClientSubmittal && !n.ClientSubmittal) result.Add("حذف ارائه به کارفرما برای «" + p.Title + "»");
                }
            }
            return result;
        }

        public static async Task SyncAsync(INotificationService notify, EnDeviation d, CancellationToken ct)
        {
            string key = "EN-DEV-" + EnText.Num(d.Id);
            bool open = Open(d);
            string roles = d.Status == EnDeviationStatus.ManagerApproved ? EnRoles.Ceo : EnRoles.Manager;
            await OfficeGates.SyncAsync(notify, key, open, "Engineering", nameof(EnDeviation), d.Id, "انحراف از استاندارد " + d.StandardCode + " (" + d.Number + "): " + EnText.Cut(d.Requirement, 120) + (d.Status == EnDeviationStatus.ManagerApproved ? " — منتظر مدیرعامل" : " — منتظر مدیر مهندسی"), "/engineering/deviations/" + d.Id, roles, ct, null, OfInboxKind.Approval, OfUrgency.Urgent);
        }

        public static async Task VoidOpenAsync(IUnitOfWork uow, INotificationService notify, string type, int targetId, string reason, int me, CancellationToken ct)
        {
            List<EnDeviation> open = await uow.Set<EnDeviation>().Where(d => !d.Deleted && d.TargetType == type && d.TargetId == targetId && (d.Status == EnDeviationStatus.Requested || d.Status == EnDeviationStatus.ManagerApproved)).ToListAsync(ct);
            foreach (EnDeviation d in open) { d.Status = EnDeviationStatus.Void; d.DecisionNote = EnText.Cut(reason, 1000); d.ModifyDate = DateTimeOffset.Now; d.ModifierUserId = me; }
            if (open.Count > 0) { await uow.SaveChangesAsync(ct); foreach (EnDeviation d in open) await SyncAsync(notify, d, ct); }
        }
    }

    public interface IEnDeviationService
    {
        Task<ResponseDto<PagedList<EnDeviationDto>>> GetDeviationsAsync(EnDeviationQuery query, CancellationToken ct);
        Task<ResponseDto<EnDeviationDto>> GetDeviationAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnDeviationDto>> DecideAsync(EnDeviationDecisionDto dto, CancellationToken ct);
    }

    /// <summary>Standards deviations: the head of engineering and then the CEO (two distinct persons, neither the requester, never a seat) decide; only at the CEO approval is the line set not-applicable (or the revision approved), in the same save.</summary>
    public class EnDeviationService : IEnDeviationService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public EnDeviationService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;

        private static string DecideReason(EnDeviation d, bool manager, bool ceo, bool seat, int me, bool listToucher)
        {
            if (seat) return EnText.AiNever;
            if (!EnDeviations.Open(d)) return "فقط انحراف در انتظار تصمیم تصمیم‌گیری می‌شود.";
            if (d.RequestedByUserId == me || listToucher) return EnText.SameUser;
            if (d.Status == EnDeviationStatus.Requested)
                return manager ? null : ceo ? "مدیرعامل پس از مدیر مهندسی تصمیم می‌گیرد؛ هنوز مدیر مهندسی تصمیم نگرفته است." : EnText.NotAllowed + " (تصمیم نخست با مدیر مهندسی است)";
            return !ceo ? EnText.NotAllowed + " (تصمیم نهایی با مدیرعامل است)" : d.ManagerApprovedByUserId == me ? EnText.SameUser + " (مدیر مهندسی و مدیرعامل باید دو نفر متفاوت باشند)" : null;
        }

        private async Task<List<EnDeviationDto>> DtosAsync(List<EnDeviation> rows, CancellationToken ct)
        {
            bool manager = _user.IsInRole(EnRoles.Manager), ceo = _user.IsInRole(EnRoles.Ceo);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.SelectMany(r => new int?[] { r.RequestedByUserId, r.ManagerApprovedByUserId, r.CeoApprovedByUserId, r.RejectedByUserId }), ct);
            List<int> caseIds = rows.Where(r => r.CaseId.HasValue).Select(r => r.CaseId.Value).Distinct().ToList(), productIds = rows.Where(r => r.ProductId.HasValue).Select(r => r.ProductId.Value).Distinct().ToList();
            Dictionary<int, string> cases = await _uow.Set<EnCase>().AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
            Dictionary<int, string> products = await _uow.Set<PbProduct>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            List<EnDeviationDto> result = new List<EnDeviationDto>();
            foreach (EnDeviation d in rows)
            {
                bool toucher = false;
                if (d.Kind == EnDeviationKind.ReferenceLoosening)
                {
                    EnReferenceList l = await _uow.Set<EnReferenceList>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.TargetId, ct);
                    toucher = l != null && (EnEditors.Touched(l.PreparedByUserId, l.EditorUserIds, Me) || l.CreatorUserId == Me);
                }
                string why = DecideReason(d, manager, ceo, seat, Me, toucher);
                result.Add(new EnDeviationDto
                {
                    Id = d.Id, Number = d.Number, Kind = (int)d.Kind, KindText = d.Kind == EnDeviationKind.NotApplicable ? "غیرقابل‌اجرا اعلام‌کردن بند الزامی" : d.Kind == EnDeviationKind.ReferenceLoosening ? "حذف یا سبک‌کردن بند الزامی در بازنگری فهرست مرجع" : "مجوز اضطرار",
                    TargetType = d.TargetType, TargetId = d.TargetId, CaseId = d.CaseId, CaseNumber = d.CaseId.HasValue && cases.TryGetValue(d.CaseId.Value, out string cn) ? cn : null, ProductId = d.ProductId, ProductCode = d.ProductId.HasValue && products.TryGetValue(d.ProductId.Value, out string pc) ? pc : null,
                    StandardCode = d.StandardCode, StandardClause = d.StandardClause, Requirement = d.Requirement, Instruction = d.Instruction, RequestedByName = names.Name(d.RequestedByUserId), RequestedAtText = EnText.Stamp(d.RequestedAt),
                    ManagerApprovedByName = names.Name(d.ManagerApprovedByUserId), ManagerApprovedAtText = EnText.Stamp(d.ManagerApprovedAt), CeoApprovedByName = names.Name(d.CeoApprovedByUserId), CeoApprovedAtText = EnText.Stamp(d.CeoApprovedAt),
                    RejectedByName = names.Name(d.RejectedByUserId), Status = (int)d.Status, StatusText = d.Status switch { EnDeviationStatus.Requested => "منتظر مدیر مهندسی", EnDeviationStatus.ManagerApproved => "منتظر مدیرعامل", EnDeviationStatus.Approved => "تاییدشده", EnDeviationStatus.Rejected => "ردشده", _ => "باطل" },
                    DecisionNote = d.DecisionNote, WaitingHours = EnDeviations.Open(d) ? Math.Max(0, (int)Math.Floor((now - d.RequestedAt).TotalHours)) : 0, RowVersion = EnConcurrency.Text(d.RowVersion), CanDecide = why == null, DecideBlockedReason = why,
                });
            }
            return result;
        }

        public async Task<ResponseDto<PagedList<EnDeviationDto>>> GetDeviationsAsync(EnDeviationQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnDeviationDto>>(EnText.NotAllowed);
            query ??= new EnDeviationQuery();
            IQueryable<EnDeviation> q = _uow.Set<EnDeviation>().AsNoTracking().Where(d => !d.Deleted);
            if (query.Status.HasValue) { EnDeviationStatus s = (EnDeviationStatus)query.Status.Value; q = q.Where(d => d.Status == s); }
            if (query.CaseId.HasValue) q = q.Where(d => d.CaseId == query.CaseId.Value);
            if (query.ProductId.HasValue) q = q.Where(d => d.ProductId == query.ProductId.Value);
            if (query.OnlyOpen) q = q.Where(d => d.Status == EnDeviationStatus.Requested || d.Status == EnDeviationStatus.ManagerApproved);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(d => d.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<EnDeviationDto>> GetDeviationAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnDeviationDto>(EnText.NotAllowed);
            EnDeviation d = await _uow.Set<EnDeviation>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (d == null) return EnText.NotFound<EnDeviationDto>("انحراف پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnDeviation> { d }, ct))[0]);
        }

        public async Task<ResponseDto<EnDeviationDto>> DecideAsync(EnDeviationDecisionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnDeviationDto>(EnText.InvalidData);
            string decision = (dto.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject") return EnText.Bad<EnDeviationDto>("تصمیم نامعتبر است (approve یا reject).");
            EnDeviation d = await _uow.Set<EnDeviation>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (d == null) return EnText.NotFound<EnDeviationDto>("انحراف پیدا نشد.");
            if (!EnConcurrency.Apply(_uow, d, dto.RowVersion)) return EnText.Bad<EnDeviationDto>(EnText.InvalidData);
            bool manager = _user.IsInRole(EnRoles.Manager), ceo = _user.IsInRole(EnRoles.Ceo);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string note = EnText.Clean(dto.Note);
            EnPlatformDocument line = null; EnReferenceList list = null; bool toucher = false;
            if (d.Kind == EnDeviationKind.NotApplicable)
                line = await _uow.Set<EnPlatformDocument>().Include(x => x.Dossier).ThenInclude(x => x.Documents).FirstOrDefaultAsync(x => x.Id == d.TargetId, ct);
            else if (d.Kind == EnDeviationKind.ReferenceLoosening)
            {
                list = await _uow.Set<EnReferenceList>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == d.TargetId, ct);
                toucher = list != null && (EnEditors.Touched(list.PreparedByUserId, list.EditorUserIds, Me) || list.CreatorUserId == Me);
            }
            string why = DecideReason(d, manager, ceo, seat, Me, toucher);
            if (why == null && decision == "reject") why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل رد");
            why ??= EnText.Over(note, 1000, "توضیح");
            if (why != null) return EnText.Bad<EnDeviationDto>(why);
            // the approvals bind to the exact content: a changed line (or revision) voids the request
            string current = d.Kind == EnDeviationKind.NotApplicable ? (line == null ? null : EnDeviations.LineHash(line, line.Dossier.ReferenceCode, d.Instruction)) : (list == null || list.Status != EnRefStatus.UnderReview ? null : EnDeviations.ListHash(list));
            if (current == null || current != d.ContentHash)
            {
                d.Status = EnDeviationStatus.Void; d.DecisionNote = "محتوای مورد درخواست پس از ثبت تغییر کرده است؛ درخواست باطل شد."; d.ModifyDate = now; d.ModifierUserId = Me;
                await _uow.SaveChangesAsync(ct);
                await EnDeviations.SyncAsync(_notify, d, ct);
                return EnText.Bad<EnDeviationDto>(d.DecisionNote);
            }
            if (decision == "reject")
            {
                d.Status = EnDeviationStatus.Rejected; d.RejectedByUserId = Me; d.RejectedAt = now; d.DecisionNote = note;   // the line stays Missing; a rejected revision goes back to draft
                if (list != null) { list.Status = EnRefStatus.Draft; list.ContentHash = null; list.Note = EnText.Cut("انحراف رد شد: " + note, 1000); list.ModifyDate = now; list.ModifierUserId = Me; }
            }
            else if (d.Status == EnDeviationStatus.Requested)
            {
                d.Status = EnDeviationStatus.ManagerApproved; d.ManagerApprovedByUserId = Me; d.ManagerApprovedAt = now; d.DecisionNote = note;
            }
            else
            {
                d.Status = EnDeviationStatus.Approved; d.CeoApprovedByUserId = Me; d.CeoApprovedAt = now; d.DecisionNote = note;
                if (line != null)
                {
                    line.State = EnLineState.NotApplicable; line.NotApplicableReason = EnText.Cut(d.Instruction, 500); line.DocumentId = null; line.ControlledDocumentId = null; line.Revision = null; line.DeviationId = d.Id; line.SuppliedByUserId = d.RequestedByUserId; line.SuppliedAt = now;
                    EnPlatformDossier dossier = line.Dossier;
                    if (dossier.Status == EnDossierStatus.Complete || dossier.Status == EnDossierStatus.Completing) { dossier.Status = EnDossierStatus.Incomplete; dossier.ConfirmedHash = null; dossier.ConfirmedAt = null; dossier.ConfirmedByUserId = null; }
                    dossier.ModifyDate = now; dossier.ModifierUserId = Me;
                }
                else if (list != null)
                {
                    // the earlier revision is superseded and the new one approved in one transaction (a failure never leaves the code without an Approved list)
                    int deviationId = d.Id, listId = list.Id; string version = dto.RowVersion; EnDeviation decided = null;
                    ResponseDto<EnDeviationDto> done = await EnTransaction.InAsync(_uow, async () =>
                    {
                        decided = await _uow.Set<EnDeviation>().FirstAsync(x => x.Id == deviationId, ct);
                        EnReferenceList target = await _uow.Set<EnReferenceList>().FirstAsync(x => x.Id == listId, ct);
                        if (!EnConcurrency.Apply(_uow, decided, version)) return EnText.Bad<EnDeviationDto>(EnText.InvalidData);
                        decided.Status = EnDeviationStatus.Approved; decided.CeoApprovedByUserId = Me; decided.CeoApprovedAt = now; decided.DecisionNote = note; decided.ModifyDate = now; decided.ModifierUserId = Me;
                        List<EnReferenceList> older = await _uow.Set<EnReferenceList>().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == target.Code && l.Id != target.Id).ToListAsync(ct);
                        try
                        {
                            if (older.Count > 0)
                            {
                                foreach (EnReferenceList o in older) { o.Status = EnRefStatus.Superseded; o.ModifyDate = now; o.ModifierUserId = Me; }
                                await _uow.SaveChangesAsync(ct);
                            }
                            target.Status = EnRefStatus.Approved; target.ApprovedAt = now; target.ApprovedByUserId = Me; target.ModifyDate = now; target.ModifierUserId = Me;
                            await _uow.SaveChangesAsync(ct);
                        }
                        catch (DbUpdateConcurrencyException) { return EnText.Bad<EnDeviationDto>(EnText.Concurrency); }
                        return ResponseDtoHelper.Ok<EnDeviationDto>(null);
                    }, r => r.Success, ct);
                    if (!done.Success) return done;
                    await EnDeviations.SyncAsync(_notify, decided, ct);
                    return await GetDeviationAsync(deviationId, ct);
                }
            }
            d.ModifyDate = now; d.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnDeviationDto>(EnText.Concurrency); }
            await EnDeviations.SyncAsync(_notify, d, ct);
            return await GetDeviationAsync(d.Id, ct);
        }
    }
}
