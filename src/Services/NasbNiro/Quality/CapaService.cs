using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface ICapaService
    {
        Task<ResponseDto<PagedList<CapaListDto>>> GetCapasAsync(CapaQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<CapaListDto>>> GetOverdueAsync(CapaQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<CapaDto>> GetCapaAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<CapaDto>> SaveCapaAsync(SaveCapaDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<CapaDto>> CapaActionAsync(CapaActionRequestDto dto, CancellationToken cancellationToken);

        /// <summary>Stages a CAPA draft in the context (no save): the caller saves it together with its own change.</summary>
        Task<Capa> DraftAsync(CapaSource source, int? sourceId, string problem, bool auto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<ComplaintListDto>>> GetComplaintsAsync(ComplaintQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ComplaintDto>> GetComplaintAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<ComplaintDto>> SaveComplaintAsync(SaveComplaintDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ComplaintDto>> ComplaintActionAsync(ComplaintActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Corrective and preventive action (ISO 9001 10.2, FO-QA-017) and customer complaints (ISO 9.1.2).</summary>
    public class CapaService : ICapaService
    {
        private const string CapaStaff = "QA,QC,PMO";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public CapaService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        private static bool IsOverdue(Capa c, DateTime today) =>
            ((c.Status == CapaStatus.Open || c.Status == CapaStatus.ActionsDefined) && c.DueDate.HasValue && c.DueDate.Value.Date < today) ||
            (c.Status == CapaStatus.Implemented && c.EffectivenessDueDate.HasValue && c.EffectivenessDueDate.Value.Date < today);

        public async Task<Capa> DraftAsync(CapaSource source, int? sourceId, string problem, bool auto, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            var capa = new Capa
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Capa, "CAPA", DateTime.Today, cancellationToken),
                Source = source, SourceId = sourceId, Problem = Clean(problem), Status = CapaStatus.Open, AutoDrafted = auto,
                DueDate = auto ? DateTime.Today.AddDays((int)settings[QualitySettingKeys.NcrMajorDays]) : (DateTime?)null,
                Actions = new List<CapaAction>(), CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<Capa>().Add(capa);
            return capa;
        }

        // -------------------------------------------------------------------------------- reading

        private static CapaListDto ToList(Capa c, Dictionary<int, string> names, DateTime today) => new CapaListDto
        {
            Id = c.Id, Number = c.Number, Source = c.Source, SourceId = c.SourceId, Problem = c.Problem, Status = c.Status, OwnerUserId = c.OwnerUserId,
            OwnerName = names.Name(c.OwnerUserId), DueDate = c.DueDate, EffectivenessDueDate = c.EffectivenessDueDate, Overdue = IsOverdue(c, today), AutoDrafted = c.AutoDrafted,
        };

        private async Task<ResponseDto<PagedList<CapaListDto>>> PageAsync(IQueryable<Capa> rows, CapaQuery query, CancellationToken cancellationToken)
        {
            int total = await rows.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize;
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<Capa> items = await rows.OrderByDescending(c => c.Id).Skip((number - 1) * size).Take(size).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, items.Select(c => c.OwnerUserId), cancellationToken);
            DateTime today = DateTime.Today;
            PagedList<CapaListDto> page = await PagedList<CapaListDto>.CreateAsync(items.Select(c => ToList(c, names, today)).ToList(), 1, 0, cancellationToken);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(page);
        }

        private IQueryable<Capa> Filter(CapaQuery query)
        {
            IQueryable<Capa> rows = _uow.Set<Capa>().Where(c => !c.Deleted);
            if (query.Status.HasValue) rows = rows.Where(c => c.Status == query.Status.Value);
            if (query.Source.HasValue) rows = rows.Where(c => c.Source == query.Source.Value);
            if (query.OwnerUserId.HasValue) rows = rows.Where(c => c.OwnerUserId == query.OwnerUserId.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(c => c.Number.Contains(text) || c.Problem.Contains(text));
            return rows;
        }

        public Task<ResponseDto<PagedList<CapaListDto>>> GetCapasAsync(CapaQuery query, CancellationToken cancellationToken) =>
            PageAsync(Filter(query ??= new CapaQuery()), query, cancellationToken);

        public Task<ResponseDto<PagedList<CapaListDto>>> GetOverdueAsync(CapaQuery query, CancellationToken cancellationToken)
        {
            query ??= new CapaQuery();
            DateTime today = DateTime.Today;
            IQueryable<Capa> rows = Filter(query).Where(c =>
                ((c.Status == CapaStatus.Open || c.Status == CapaStatus.ActionsDefined) && c.DueDate < today) ||
                (c.Status == CapaStatus.Implemented && c.EffectivenessDueDate < today));
            return PageAsync(rows, query, cancellationToken);
        }

        public async Task<ResponseDto<CapaDto>> GetCapaAsync(int id, CancellationToken cancellationToken)
        {
            Capa c = await _uow.Set<Capa>().Include(x => x.Actions).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (c == null) return QualityText.NotFound<CapaDto>("اقدام اصلاحی و پیشگیرانه پیدا نشد.");
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow,
                new int?[] { c.OwnerUserId, c.ClosedByUserId }.Concat(c.Actions.Select(a => a.OwnerUserId)), cancellationToken);
            DateTime today = DateTime.Today;
            CapaListDto l = ToList(c, names, today);
            string sourceNumber = null;
            if (c.Source == CapaSource.Ncr && c.SourceId.HasValue)
                sourceNumber = await _uow.Set<NonConformance>().Where(n => n.Id == c.SourceId.Value).Select(n => n.Number).FirstOrDefaultAsync(cancellationToken);
            else if (c.Source == CapaSource.CustomerComplaint && c.SourceId.HasValue)
                sourceNumber = await _uow.Set<CustomerComplaint>().Where(n => n.Id == c.SourceId.Value).Select(n => n.Number).FirstOrDefaultAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new CapaDto
            {
                Id = l.Id, Number = l.Number, Source = l.Source, SourceId = l.SourceId, Problem = l.Problem, Status = l.Status, OwnerUserId = l.OwnerUserId,
                OwnerName = l.OwnerName, DueDate = l.DueDate, EffectivenessDueDate = l.EffectivenessDueDate, Overdue = l.Overdue, AutoDrafted = l.AutoDrafted,
                SourceNumber = sourceNumber, Containment = c.Containment, RootCauseMethod = c.RootCauseMethod, RootCauseAnalysis = c.RootCauseAnalysis,
                EffectivenessCriteria = c.EffectivenessCriteria, EffectivenessResult = c.EffectivenessResult, ImplementedAt = c.ImplementedAt, ClosedAt = c.ClosedAt,
                ClosedByName = names.Name(c.ClosedByUserId), CloseNote = c.CloseNote, RowVersion = c.RowVersion == null ? null : Convert.ToBase64String(c.RowVersion),
                Actions = c.Actions.OrderBy(a => a.Id).Select(a => new CapaActionDto
                {
                    Id = a.Id, Description = a.Description, OwnerUserId = a.OwnerUserId, OwnerName = names.Name(a.OwnerUserId), DueDate = a.DueDate, DoneAt = a.DoneAt,
                    EvidenceDocumentId = a.EvidenceDocumentId, EvidenceNote = a.EvidenceNote, Overdue = !a.DoneAt.HasValue && a.DueDate.HasValue && a.DueDate.Value.Date < today,
                }).ToList(),
            });
        }

        // -------------------------------------------------------------------------------- saving

        private async Task<bool> UserExistsAsync(int? userId, CancellationToken cancellationToken) =>
            !userId.HasValue || await _uow.Set<User>().AnyAsync(u => u.Id == userId.Value, cancellationToken);

        public async Task<ResponseDto<CapaDto>> SaveCapaAsync(SaveCapaDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Problem) == null) return Bad<CapaDto>("شرح مساله (عدم انطباق) را بنویسید.");
            if (!_user.IsInRole(CapaStaff)) return Bad<CapaDto>(QualityText.NotAllowed);
            if (!Enum.IsDefined(typeof(CapaSource), dto.Source) || !Enum.IsDefined(typeof(RootCauseMethod), dto.RootCauseMethod)) return Bad<CapaDto>(QualityText.InvalidData);
            if (!await UserExistsAsync(dto.OwnerUserId, cancellationToken)) return Bad<CapaDto>("مسئول انتخاب‌شده پیدا نشد.");
            foreach (SaveCapaActionDto a in dto.Actions ?? new List<SaveCapaActionDto>())
            {
                if (Clean(a.Description) == null) return Bad<CapaDto>("شرح هر اقدام را بنویسید.");
                if (!await UserExistsAsync(a.OwnerUserId, cancellationToken)) return Bad<CapaDto>("مسئول اقدام پیدا نشد.");
            }
            NonConformance ncr = null;
            if (dto.Source == CapaSource.Ncr && dto.SourceId.HasValue)
            {
                ncr = await _uow.Set<NonConformance>().FirstOrDefaultAsync(n => n.Id == dto.SourceId.Value && !n.Deleted, cancellationToken);
                if (ncr == null) return Bad<CapaDto>("گزارش عدم انطباق پیدا نشد.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            Capa capa;
            if (dto.Id.HasValue)
            {
                capa = await _uow.Set<Capa>().Include(c => c.Actions).FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, cancellationToken);
                if (capa == null) return QualityText.NotFound<CapaDto>("اقدام اصلاحی و پیشگیرانه پیدا نشد.");
                if (capa.Status != CapaStatus.Open && capa.Status != CapaStatus.ActionsDefined) return Bad<CapaDto>("در این مرحله اقدام اصلاحی ویرایش نمی‌شود.");
                if (Clean(dto.RowVersion) != null)
                {
                    try { _uow.Entry(capa).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                    catch (FormatException) { return Bad<CapaDto>(QualityText.InvalidData); }
                }
            }
            else
            {
                capa = await DraftAsync(dto.Source, dto.SourceId, dto.Problem, false, cancellationToken);
                if (ncr != null) ncr.Capa = capa;
            }
            // actions: keep the finished ones, replace the rest
            List<SaveCapaActionDto> input = dto.Actions ?? new List<SaveCapaActionDto>();
            foreach (CapaAction old in capa.Actions.ToList())
            {
                SaveCapaActionDto match = input.FirstOrDefault(a => a.Id == old.Id);
                if (match == null)
                {
                    if (old.DoneAt.HasValue) return Bad<CapaDto>("اقدام انجام‌شده حذف نمی‌شود.");
                    capa.Actions.Remove(old);
                }
                else if (!old.DoneAt.HasValue)
                {
                    old.Description = Clean(match.Description); old.OwnerUserId = match.OwnerUserId; old.DueDate = match.DueDate?.Date;
                }
            }
            foreach (SaveCapaActionDto a in input.Where(a => !a.Id.HasValue || capa.Actions.All(x => x.Id != a.Id.Value)))
                capa.Actions.Add(new CapaAction { Description = Clean(a.Description), OwnerUserId = a.OwnerUserId, DueDate = a.DueDate?.Date });
            capa.Source = dto.Source; capa.SourceId = dto.SourceId; capa.Problem = Clean(dto.Problem); capa.Containment = Clean(dto.Containment);
            capa.RootCauseMethod = dto.RootCauseMethod; capa.RootCauseAnalysis = Clean(dto.RootCauseAnalysis); capa.OwnerUserId = dto.OwnerUserId;
            if (dto.DueDate.HasValue) capa.DueDate = dto.DueDate.Value.Date;
            capa.EffectivenessCriteria = Clean(dto.EffectivenessCriteria);
            bool complete = capa.OwnerUserId.HasValue && capa.DueDate.HasValue && capa.RootCauseMethod != RootCauseMethod.None && capa.RootCauseAnalysis != null &&
                            capa.EffectivenessCriteria != null && capa.Actions.Count > 0;
            capa.Status = complete ? CapaStatus.ActionsDefined : CapaStatus.Open;
            capa.ModifyDate = now; capa.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<CapaDto>(QualityText.Concurrency); }
            return await GetCapaAsync(capa.Id, cancellationToken);
        }

        public async Task<ResponseDto<CapaDto>> CapaActionAsync(CapaActionRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<CapaDto>(QualityText.InvalidData);
            Capa capa = await _uow.Set<Capa>().Include(c => c.Actions).FirstOrDefaultAsync(c => c.Id == dto.Id && !c.Deleted, cancellationToken);
            if (capa == null) return QualityText.NotFound<CapaDto>("اقدام اصلاحی و پیشگیرانه پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            bool staff = _user.IsInRole(CapaStaff);
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "complete-action":
                    CapaAction action = capa.Actions.FirstOrDefault(a => a.Id == dto.ActionId);
                    if (action == null) return QualityText.NotFound<CapaDto>("اقدام پیدا نشد.");
                    if (capa.Status != CapaStatus.ActionsDefined) return Bad<CapaDto>("اقدام‌ها هنوز تعریف نهایی نشده یا اجرا شده‌اند.");
                    if (!staff && action.OwnerUserId != Me && capa.OwnerUserId != Me) return Bad<CapaDto>(QualityText.NotAllowed);
                    if (action.DoneAt.HasValue) return Bad<CapaDto>("این اقدام قبلاً انجام شده است.");
                    if (Clean(dto.Note) == null && !dto.EvidenceDocumentId.HasValue) return Bad<CapaDto>("مدرک یا شرح انجام اقدام را وارد کنید.");
                    action.DoneAt = DateTime.Today; action.EvidenceNote = Clean(dto.Note); action.EvidenceDocumentId = dto.EvidenceDocumentId;
                    break;
                case "implemented":
                    if (capa.Status != CapaStatus.ActionsDefined) return Bad<CapaDto>("فقط CAPA با اقدام‌های تعریف‌شده اجرا می‌شود.");
                    if (!staff && capa.OwnerUserId != Me) return Bad<CapaDto>(QualityText.NotAllowed);
                    if (capa.Actions.Any(a => !a.DoneAt.HasValue)) return Bad<CapaDto>("همه اقدام‌ها باید انجام شده باشند.");
                    capa.Status = CapaStatus.Implemented; capa.ImplementedAt = DateTime.Today;
                    Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
                    capa.EffectivenessDueDate = DateTime.Today.AddDays((int)settings[QualitySettingKeys.CapaEffectivenessDays]);
                    break;
                case "effectiveness":
                    if (capa.Status != CapaStatus.Implemented) return Bad<CapaDto>("بررسی اثربخشی فقط پس از اجرای اقدام‌ها ثبت می‌شود.");
                    if (!_user.IsInRole(QualityRoles.CapaClosers)) return Bad<CapaDto>(QualityText.NotAllowed);
                    if (capa.OwnerUserId == Me) return Bad<CapaDto>(QualityText.SameUser + " (اثربخشی را مسئول اقدام ارزیابی نمی‌کند)");
                    if (Clean(dto.Note) == null) return Bad<CapaDto>("نتیجه بررسی اثربخشی را (بر اساس معیار تعریف‌شده) بنویسید.");
                    capa.EffectivenessResult = Clean(dto.Note);
                    capa.Status = dto.Passed ? CapaStatus.EffectivenessCheck : CapaStatus.ActionsDefined;
                    if (!dto.Passed) capa.ImplementedAt = null;
                    break;
                case "close":
                    if (capa.Status != CapaStatus.EffectivenessCheck) return Bad<CapaDto>("بستن CAPA فقط پس از تایید اثربخشی ممکن است.");
                    if (!_user.IsInRole(QualityRoles.CapaClosers)) return Bad<CapaDto>(QualityText.NotAllowed + " (CAPA را تضمین کیفیت یا مدیر QC می‌بندد)");
                    if (capa.OwnerUserId == Me) return Bad<CapaDto>(QualityText.SameUser + " (CAPA را مسئول آن نمی‌بندد)");
                    capa.Status = CapaStatus.Closed; capa.ClosedAt = now; capa.ClosedByUserId = Me; capa.CloseNote = Clean(dto.Note);
                    break;
                case "cancel":
                    if (!_user.IsInRole(CapaStaff)) return Bad<CapaDto>(QualityText.NotAllowed);
                    if (capa.Status == CapaStatus.Closed || capa.Status == CapaStatus.Cancelled) return Bad<CapaDto>("CAPA بسته یا لغو شده است.");
                    if (Clean(dto.Note) == null) return Bad<CapaDto>("دلیل لغو را بنویسید.");
                    capa.Status = CapaStatus.Cancelled; capa.CloseNote = Clean(dto.Note); capa.ClosedAt = now; capa.ClosedByUserId = Me;
                    break;
                default:
                    return Bad<CapaDto>("عملیات نامعتبر است.");
            }
            capa.ModifyDate = now; capa.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<CapaDto>(QualityText.Concurrency); }
            return await GetCapaAsync(capa.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------------- complaints

        private async Task<ComplaintDto> ComplaintDtoAsync(CustomerComplaint c, CancellationToken cancellationToken)
        {
            string wo = c.WorkOrderId.HasValue ? await _uow.Set<WorkOrder>().Where(w => w.Id == c.WorkOrderId.Value).Select(w => w.Code).FirstOrDefaultAsync(cancellationToken) : null;
            string ncr = c.NcrId.HasValue ? await _uow.Set<NonConformance>().Where(n => n.Id == c.NcrId.Value).Select(n => n.Number).FirstOrDefaultAsync(cancellationToken) : null;
            string capa = c.CapaId.HasValue ? await _uow.Set<Capa>().Where(n => n.Id == c.CapaId.Value).Select(n => n.Number).FirstOrDefaultAsync(cancellationToken) : null;
            return new ComplaintDto
            {
                Id = c.Id, Number = c.Number, ContractId = c.ContractId, WorkOrderId = c.WorkOrderId, WorkOrderCode = wo, ReceivedDate = c.ReceivedDate, Severity = c.Severity,
                Status = c.Status, ResponseDue = c.ResponseDue, Overdue = c.Status != ComplaintStatus.Responded && c.Status != ComplaintStatus.Closed && c.ResponseDue.Date < DateTime.Today,
                NcrId = c.NcrId, CapaId = c.CapaId, Channel = c.Channel, Description = c.Description, Response = c.Response, RespondedAt = c.RespondedAt, ClosedAt = c.ClosedAt,
                NcrNumber = ncr, CapaNumber = capa,
            };
        }

        public async Task<ResponseDto<PagedList<ComplaintListDto>>> GetComplaintsAsync(ComplaintQuery query, CancellationToken cancellationToken)
        {
            query ??= new ComplaintQuery();
            DateTime today = DateTime.Today;
            IQueryable<CustomerComplaint> rows = _uow.Set<CustomerComplaint>().Where(c => !c.Deleted);
            if (query.Status.HasValue) rows = rows.Where(c => c.Status == query.Status.Value);
            if (query.Overdue == true) rows = rows.Where(c => c.Status != ComplaintStatus.Responded && c.Status != ComplaintStatus.Closed && c.ResponseDue < today);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(c => c.Number.Contains(text) || c.Description.Contains(text));
            IQueryable<ComplaintListDto> projection = rows.OrderByDescending(c => c.Id).Select(c => new ComplaintListDto
            {
                Id = c.Id, Number = c.Number, ContractId = c.ContractId, WorkOrderId = c.WorkOrderId, WorkOrderCode = c.WorkOrder.Code, ReceivedDate = c.ReceivedDate,
                Severity = c.Severity, Status = c.Status, ResponseDue = c.ResponseDue,
                Overdue = c.Status != ComplaintStatus.Responded && c.Status != ComplaintStatus.Closed && c.ResponseDue < today, NcrId = c.NcrId, CapaId = c.CapaId,
            });
            return ResponseDtoHelper.Ok(await PagedList<ComplaintListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<ComplaintDto>> GetComplaintAsync(int id, CancellationToken cancellationToken)
        {
            CustomerComplaint c = await _uow.Set<CustomerComplaint>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return c == null ? QualityText.NotFound<ComplaintDto>("شکایت پیدا نشد.") : ResponseDtoHelper.Ok(await ComplaintDtoAsync(c, cancellationToken));
        }

        public async Task<ResponseDto<ComplaintDto>> SaveComplaintAsync(SaveComplaintDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Description) == null) return Bad<ComplaintDto>("شرح شکایت را بنویسید.");
            if (!_user.IsInRole(QualityRoles.Complaints)) return Bad<ComplaintDto>(QualityText.NotAllowed);
            if (!Enum.IsDefined(typeof(NcrSeverity), dto.Severity)) return Bad<ComplaintDto>(QualityText.InvalidData);
            DateTime received = (dto.ReceivedDate ?? DateTime.Today).Date;
            if (received > DateTime.Today) return Bad<ComplaintDto>("تاریخ دریافت نمی‌تواند در آینده باشد.");
            int? contractId = dto.ContractId;
            if (dto.WorkOrderId.HasValue)
            {
                int? woContract = await _uow.Set<WorkOrder>().Where(w => w.Id == dto.WorkOrderId.Value && !w.Deleted).Select(w => (int?)w.ContractId).FirstOrDefaultAsync(cancellationToken);
                if (!woContract.HasValue) return Bad<ComplaintDto>("دستور کار پیدا نشد.");
                contractId = woContract;
            }
            if (contractId.HasValue && !await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == contractId.Value, cancellationToken)) return Bad<ComplaintDto>("قرارداد پیدا نشد.");
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            CustomerComplaint c;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<CustomerComplaint>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (c == null) return QualityText.NotFound<ComplaintDto>("شکایت پیدا نشد.");
                if (c.Status != ComplaintStatus.Received && c.Status != ComplaintStatus.UnderReview) return Bad<ComplaintDto>("شکایت پاسخ‌داده‌شده ویرایش نمی‌شود.");
            }
            else
            {
                c = new CustomerComplaint
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Complaint, "CC", received, cancellationToken),
                    Status = ComplaintStatus.Received, CreateDate = now, CreatorUserId = Me,
                };
                _uow.Set<CustomerComplaint>().Add(c);
            }
            c.ContractId = contractId; c.WorkOrderId = dto.WorkOrderId; c.ReceivedDate = received; c.Channel = Clean(dto.Channel); c.Description = Clean(dto.Description);
            c.Severity = dto.Severity; c.ResponseDue = received.AddDays((int)settings[QualitySettingKeys.ComplaintResponseDays]);
            c.ModifyDate = now; c.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ComplaintDtoAsync(c, cancellationToken));
        }

        public async Task<ResponseDto<ComplaintDto>> ComplaintActionAsync(ComplaintActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<ComplaintDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Complaints)) return Bad<ComplaintDto>(QualityText.NotAllowed);
            CustomerComplaint c = await _uow.Set<CustomerComplaint>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (c == null) return QualityText.NotFound<ComplaintDto>("شکایت پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "review":
                    if (c.Status != ComplaintStatus.Received) return Bad<ComplaintDto>("فقط شکایت دریافت‌شده بررسی می‌شود.");
                    if (dto.Severity != NcrSeverity.NotClassified) c.Severity = dto.Severity;
                    if (c.Severity == NcrSeverity.NotClassified) return Bad<ComplaintDto>("شدت شکایت (جزئی / عمده) را مشخص کنید.");
                    c.Status = ComplaintStatus.UnderReview;
                    break;
                case "link-ncr":
                    if (c.Status == ComplaintStatus.Closed) return Bad<ComplaintDto>("شکایت بسته شده است.");
                    if (!dto.NcrId.HasValue || !await _uow.Set<NonConformance>().AnyAsync(n => n.Id == dto.NcrId.Value && !n.Deleted, cancellationToken)) return Bad<ComplaintDto>("گزارش عدم انطباق پیدا نشد.");
                    c.NcrId = dto.NcrId;
                    break;
                case "create-capa":
                    if (!_user.IsInRole(CapaStaff)) return Bad<ComplaintDto>(QualityText.NotAllowed);
                    if (c.Status == ComplaintStatus.Closed) return Bad<ComplaintDto>("شکایت بسته شده است.");
                    if (c.CapaId.HasValue) return Bad<ComplaintDto>("برای این شکایت قبلاً CAPA ساخته شده است.");
                    Capa capa = await DraftAsync(CapaSource.CustomerComplaint, c.Id, $"شکایت مشتری {c.Number}: {c.Description}", false, cancellationToken);
                    c.Capa = capa;
                    break;
                case "respond":
                    if (c.Status != ComplaintStatus.UnderReview) return Bad<ComplaintDto>("ابتدا شکایت را بررسی کنید.");
                    if (Clean(dto.Response) == null) return Bad<ComplaintDto>("متن پاسخ به مشتری را بنویسید.");
                    c.Response = Clean(dto.Response); c.RespondedAt = DateTime.Today; c.Status = ComplaintStatus.Responded;
                    break;
                case "close":
                    if (!_user.IsInRole(CapaStaff)) return Bad<ComplaintDto>(QualityText.NotAllowed);
                    if (c.Status != ComplaintStatus.Responded) return Bad<ComplaintDto>("فقط شکایتِ پاسخ‌داده‌شده بسته می‌شود.");
                    if (c.Severity == NcrSeverity.Major && !c.CapaId.HasValue && !c.NcrId.HasValue) return Bad<ComplaintDto>("شکایت عمده باید به گزارش عدم انطباق یا CAPA مرتبط شود.");
                    c.Status = ComplaintStatus.Closed; c.ClosedAt = now; c.ClosedByUserId = Me;
                    break;
                default:
                    return Bad<ComplaintDto>("عملیات نامعتبر است.");
            }
            c.ModifyDate = now; c.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ComplaintDtoAsync(c, cancellationToken));
        }
    }
}
