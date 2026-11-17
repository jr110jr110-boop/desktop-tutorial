using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    public interface IEnInfoRequestService
    {
        Task<ResponseDto<PagedList<EnInfoRequestDto>>> GetRequestsAsync(EnInfoQuery query, CancellationToken ct);
        Task<ResponseDto<EnInfoRequestDto>> GetRequestAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnInfoRequestDto>> AskAsync(EnInfoAskDto dto, CancellationToken ct);
        Task<ResponseDto<EnInfoRequestDto>> ActionAsync(EnInfoActionDto dto, CancellationToken ct);
    }

    /// <summary>A unit that needs more than its notice asks the PMO (engineering asks too, before the release, about a case with a contract or a handover that is not void); the PMO answers (never with a price: engineering and the units do not see prices); the asking unit closes. Only the asking unit, the PMO, the CEO and the auditor read a question.</summary>
    public class EnInfoRequestService : IEnInfoRequestService
    {
        public const string KeyPrefix = "EN-INFO-";
        public const string Hint = "پاسخ نباید شامل قیمت، مبلغ یا هزینه باشد؛ مهندسی و واحدها قیمت نمی‌بینند (بررسی خودکار ممکن نیست و مسئولیت با پاسخ‌دهنده است).";
        private const string PrivilegedReaders = "PMO,CEO,AUDITOR";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public EnInfoRequestService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private bool OfUnit(EnUnit u) => _user.IsInRole(EnUnits.RolesOf(u));
        private bool CanSee(EnInfoRequest r) => _user.IsInRole(PrivilegedReaders) || OfUnit(r.Unit);

        internal static string StatusText(EnInfoStatus s) => s == EnInfoStatus.Open ? "منتظر پاسخ PMO" : s == EnInfoStatus.Answered ? "پاسخ‌داده‌شده" : s == EnInfoStatus.Closed ? "بسته‌شده" : "ابطال‌شده";

        private string AnswerReason(EnInfoRequest r, bool seat) =>
            !_user.IsInRole(EnRoles.Pmo) ? EnText.NotAllowed + " (پاسخ با PMO است)" : seat ? EnText.AiNever : r.Status != EnInfoStatus.Open ? "فقط پرسش باز پاسخ داده می‌شود." : null;

        private string CloseReason(EnInfoRequest r, bool seat) =>
            !OfUnit(r.Unit) ? EnText.NotAllowed + " (بستن با واحد پرسنده است)" : seat ? EnText.AiNever : r.Status != EnInfoStatus.Answered ? "فقط پرسش پاسخ‌داده‌شده بسته می‌شود." : null;

        private string VoidReason(EnInfoRequest r, bool seat) =>
            r.AskedByUserId != Me ? EnText.NotAllowed + " (فقط پرسنده پرسش خود را پس می‌گیرد)" : seat ? EnText.AiNever : r.Status != EnInfoStatus.Open ? "فقط پرسش باز پس گرفته می‌شود." : null;

        private async Task<List<EnInfoRequestDto>> DtosAsync(List<EnInfoRequest> rows, CancellationToken ct)
        {
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.SelectMany(r => new int?[] { r.AskedByUserId, r.AnsweredByUserId, r.ClosedByUserId }), ct);
            List<int> caseIds = rows.Select(r => r.CaseId).Distinct().ToList(), handoverIds = rows.Where(r => r.HandoverId.HasValue).Select(r => r.HandoverId.Value).Distinct().ToList(), contractIds = rows.Select(r => r.ContractId).Distinct().ToList();
            Dictionary<int, string> cases = await _uow.Set<EnCase>().AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
            Dictionary<int, string> handovers = await _uow.Set<EnHandover>().AsNoTracking().Where(h => handoverIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Number, ct);
            Dictionary<int, string> contracts = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            return rows.Select(r =>
            {
                string answer = AnswerReason(r, seat), close = CloseReason(r, seat), voidR = VoidReason(r, seat);
                return new EnInfoRequestDto
                {
                    Id = r.Id, Number = r.Number, HandoverId = r.HandoverId, HandoverNumber = r.HandoverId.HasValue && handovers.TryGetValue(r.HandoverId.Value, out string hn) ? hn : null, CaseId = r.CaseId, CaseNumber = cases.TryGetValue(r.CaseId, out string cn) ? cn : null,
                    ContractId = r.ContractId, ContractCode = contracts.TryGetValue(r.ContractId, out string kc) ? kc : null, Unit = (int)r.Unit, UnitText = EnText.Unit(r.Unit), Question = r.Question, AskedByName = names.Name(r.AskedByUserId), AskedAtText = EnText.Stamp(r.AskedAt),
                    Status = (int)r.Status, StatusText = StatusText(r.Status), Answer = r.Answer, AnsweredByName = names.Name(r.AnsweredByUserId), AnsweredAtText = EnText.Stamp(r.AnsweredAt), ClosedByName = names.Name(r.ClosedByUserId), ClosedAtText = EnText.Stamp(r.ClosedAt),
                    AnswerHint = Hint, RowVersion = EnConcurrency.Text(r.RowVersion), CanAnswer = answer == null, AnswerBlockedReason = answer, CanClose = close == null, CloseBlockedReason = close, CanVoid = voidR == null, VoidBlockedReason = voidR,
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<EnInfoRequestDto>>> GetRequestsAsync(EnInfoQuery query, CancellationToken ct)
        {
            List<EnUnit> mine = EnUnits.Askers.Where(OfUnit).ToList();
            bool all = _user.IsInRole(PrivilegedReaders);
            if (!all && mine.Count == 0) return EnText.Bad<PagedList<EnInfoRequestDto>>(EnText.NotAllowed);
            query ??= new EnInfoQuery();
            IQueryable<EnInfoRequest> q = _uow.Set<EnInfoRequest>().AsNoTracking().Where(x => !x.Deleted);
            if (!all) q = q.Where(x => mine.Contains(x.Unit));
            if (query.CaseId.HasValue) q = q.Where(x => x.CaseId == query.CaseId.Value);
            if (query.HandoverId.HasValue) q = q.Where(x => x.HandoverId == query.HandoverId.Value);
            if (query.Status.HasValue) { EnInfoStatus s = (EnInfoStatus)query.Status.Value; q = q.Where(x => x.Status == s); }
            if (query.OnlyOpen) q = q.Where(x => x.Status == EnInfoStatus.Open || x.Status == EnInfoStatus.Answered);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(x => x.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<EnInfoRequestDto>> GetRequestAsync(int id, CancellationToken ct)
        {
            EnInfoRequest r = await _uow.Set<EnInfoRequest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return EnText.NotFound<EnInfoRequestDto>("پرسش پیدا نشد.");
            if (!CanSee(r)) return EnText.Bad<EnInfoRequestDto>(EnText.NotAllowed);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnInfoRequest> { r }, ct))[0]);
        }

        private async Task SyncAsync(EnInfoRequest r, CancellationToken ct) =>
            await OfficeGates.SyncAsync(_notify, KeyPrefix + EnText.Num(r.Id), r.Status == EnInfoStatus.Open, "Engineering", nameof(EnInfoRequest), r.Id,
                "پرسش واحد " + EnText.Unit(r.Unit) + " دربارهٔ مدارک مهندسی (" + r.Number + "): " + EnText.Cut(r.Question, 150), "/engineering/info-requests/" + EnText.Num(r.Id), EnRoles.Pmo, ct, null, OfInboxKind.Task);

        public async Task<ResponseDto<EnInfoRequestDto>> AskAsync(EnInfoAskDto dto, CancellationToken ct)
        {
            if (dto == null || !EnUnits.Askers.Contains((EnUnit)dto.Unit)) return EnText.Bad<EnInfoRequestDto>(EnText.InvalidData);
            EnUnit unit = (EnUnit)dto.Unit;
            if (!OfUnit(unit)) return EnText.Bad<EnInfoRequestDto>(EnText.NotAllowed + " (پرسش را کارکنان همان واحد ثبت می‌کنند)");
            if (await EnActors.IsSeatAsync(_uow, Me, ct)) return EnText.Bad<EnInfoRequestDto>(EnText.AiNever);
            string question = EnText.Clean(dto.Question);
            string why = EnText.ReasonShort(question, 15, "پرسش") ?? EnText.Over(question, 2000, "پرسش");
            if (why != null) return EnText.Bad<EnInfoRequestDto>(why);
            int caseId; int contractId; int? handoverId = null;
            if (dto.HandoverId.HasValue)
            {
                EnHandover h = await _uow.Set<EnHandover>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.HandoverId.Value && !x.Deleted, ct);
                if (h == null) return EnText.NotFound<EnInfoRequestDto>(EnText.NotFoundHandover);
                if (unit == EnUnit.Engineering ? h.Status == EnHandoverStatus.Void : h.Status != EnHandoverStatus.Released) return EnText.Bad<EnInfoRequestDto>(unit == EnUnit.Engineering ? "پرسش دربارهٔ تحویل ابطال‌شده ثبت نمی‌شود." : "پرسش دربارهٔ تحویلی ثبت می‌شود که به واحدها ابلاغ شده است.");
                caseId = h.CaseId; contractId = h.ContractId; handoverId = h.Id;
            }
            else
            {
                if (!dto.CaseId.HasValue) return EnText.Bad<EnInfoRequestDto>("تحویل یا پروندهٔ مهندسی را مشخص کنید.");
                EnCase c = await _uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.CaseId.Value && !x.Deleted, ct);
                if (c == null) return EnText.NotFound<EnInfoRequestDto>(EnText.NotFoundCase);
                if (!c.ContractId.HasValue) return EnText.Bad<EnInfoRequestDto>("این پرونده هنوز قرارداد ندارد.");
                caseId = c.Id; contractId = c.ContractId.Value;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            EnInfoRequest r = new EnInfoRequest { HandoverId = handoverId, CaseId = caseId, ContractId = contractId, Unit = unit, Question = question, AskedByUserId = Me, AskedAt = now, Status = EnInfoStatus.Open, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
            r.Number = await EnNumbers.NextAsync(_uow, EnNumbers.InfoRequest, "EIR", now.DateTime, ct);
            _uow.Set<EnInfoRequest>().Add(r);
            await _uow.SaveChangesAsync(ct);
            await SyncAsync(r, ct);
            return await GetRequestAsync(r.Id, ct);
        }

        public async Task<ResponseDto<EnInfoRequestDto>> ActionAsync(EnInfoActionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnInfoRequestDto>(EnText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "answer" && action != "close" && action != "void") return EnText.Bad<EnInfoRequestDto>("عمل نامعتبر است (answer، close، void).");
            EnInfoRequest r = await _uow.Set<EnInfoRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return EnText.NotFound<EnInfoRequestDto>("پرسش پیدا نشد.");
            if (!CanSee(r)) return EnText.Bad<EnInfoRequestDto>(EnText.NotAllowed);
            if (!EnConcurrency.Apply(_uow, r, dto.RowVersion)) return EnText.Bad<EnInfoRequestDto>(EnText.InvalidData);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string text = EnText.Clean(dto.Answer), why;
            if (action == "answer")
            {
                why = AnswerReason(r, seat) ?? EnText.ReasonShort(text, 15, "پاسخ") ?? EnText.Over(text, 2000, "پاسخ");
                if (why != null) return EnText.Bad<EnInfoRequestDto>(why);
                r.Answer = text; r.AnsweredByUserId = Me; r.AnsweredAt = now; r.Status = EnInfoStatus.Answered;
            }
            else if (action == "close")
            {
                why = CloseReason(r, seat);
                if (why != null) return EnText.Bad<EnInfoRequestDto>(why);
                r.ClosedByUserId = Me; r.ClosedAt = now; r.Status = EnInfoStatus.Closed;
            }
            else
            {
                why = VoidReason(r, seat) ?? EnText.ReasonShort(text, EnRules.ReasonMin, "دلیل پس‌گرفتن");
                if (why != null) return EnText.Bad<EnInfoRequestDto>(why);
                r.Status = EnInfoStatus.Void;
                EnCase c = await _uow.Set<EnCase>().FirstOrDefaultAsync(x => x.Id == r.CaseId, ct);
                if (c != null) EnEvents.Add(_uow, c, "info-request-void", Me, r.Number);   // the reason stays on the request, not in the case events (all readers see the events)
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnInfoRequestDto>(EnText.Concurrency); }
            await SyncAsync(r, ct);
            return await GetRequestAsync(r.Id, ct);
        }
    }
}
