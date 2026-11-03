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

        /// <summary>A CAPA without an owner (for example one the system drafted from a major NCR or an audit finding) is a task for QA until the owner and the actions are set. Call after the save.</summary>
        Task SyncAssignGateAsync(Capa capa, CancellationToken cancellationToken);

        /// <summary>
        /// The nightly check of late CAPAs (R1q D13): QA is told on the first late day and the CEO after the setting CapaCeoEscalationDays; every level is told once.
        /// Returns how many CAPAs moved up a level.
        /// </summary>
        Task<CapaEscalationResultDto> EscalateAsync(CancellationToken cancellationToken);

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

        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public CapaService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _notify = notify;
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        /// <summary>The roles that read every CAPA; anybody else sees the CAPAs he owns or has an action in (R1q D13).</summary>
        private bool ReadsAll => _user.IsInRole(QualityRoles.Readers);

        /// <summary>
        /// Review M11: the person who created or edited the CAPA, owns it or owns one of its actions, or finished an action for somebody else cannot be the one who judges its
        /// effectiveness, closes it or cancels it. A CAPA the system drafted has no human creator.
        /// </summary>
        private bool IsMaker(Capa c) =>
            global::Services.NasbNiro.Finance.LedgerMath.Touched(c.AutoDrafted ? 0 : c.CreatorUserId, c.EditorUserIds, Me) || c.OwnerUserId == Me || (c.Actions ?? new List<CapaAction>()).Any(a => a.OwnerUserId == Me);

        private static bool IsOverdue(Capa c, DateTime today) =>
            ((c.Status == CapaStatus.Open || c.Status == CapaStatus.ActionsDefined) && c.DueDate.HasValue && c.DueDate.Value.Date < today) ||
            (c.Status == CapaStatus.Implemented && c.EffectivenessDueDate.HasValue && c.EffectivenessDueDate.Value.Date < today);

        public async Task SyncAssignGateAsync(Capa capa, CancellationToken cancellationToken)
        {
            if (capa == null || capa.Id == 0) return;
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "capa-assign:" + capa.Id, capa.Status == CapaStatus.Open && !capa.OwnerUserId.HasValue && !capa.Deleted, "quality", "capa", capa.Id,
                "تعیین مسئول و اقدام‌های CAPA " + capa.Number, "/quality/capa/" + capa.Id, "QA", cancellationToken, capa.DueDate, global::Entities.Model.NasbNiro.Office.OfInboxKind.Task);
        }

        public async Task<Capa> DraftAsync(CapaSource source, int? sourceId, string problem, bool auto, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            var capa = new Capa
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Capa, "CAPA", DateTime.Today, cancellationToken),
                Source = source, SourceId = sourceId, Problem = Clean(problem), Status = CapaStatus.Open, AutoDrafted = auto, Mandatory = CapaRules.IsMandatory(auto, source),
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
            Mandatory = c.Mandatory || CapaRules.IsMandatory(c.AutoDrafted, c.Source), Iteration = c.Iteration, EscalationLevel = c.EscalationLevel, CancelReason = c.CancelReason,
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
            if (!ReadsAll)
            {
                int me = Me;
                rows = rows.Where(c => c.OwnerUserId == me || c.Actions.Any(a => a.OwnerUserId == me));
            }
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
            if (!ReadsAll && c.OwnerUserId != Me && !c.Actions.Any(a => a.OwnerUserId == Me)) return Bad<CapaDto>(QualityText.NotAllowed);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow,
                new int?[] { c.OwnerUserId, c.ClosedByUserId }.Concat(c.Actions.Select(a => a.OwnerUserId)), cancellationToken);
            DateTime today = DateTime.Today;
            Dictionary<string, decimal> settingsForRead = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
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
                Mandatory = c.Mandatory || CapaRules.IsMandatory(c.AutoDrafted, c.Source), SourceNumber = sourceNumber, Containment = c.Containment, RootCauseMethod = c.RootCauseMethod, RootCauseAnalysis = c.RootCauseAnalysis,
                EffectivenessCriteria = c.EffectivenessCriteria, EffectivenessResult = c.EffectivenessResult, ImplementedAt = c.ImplementedAt, ClosedAt = c.ClosedAt,
                ClosedByName = names.Name(c.ClosedByUserId), CloseNote = c.CloseNote, RowVersion = c.RowVersion == null ? null : Convert.ToBase64String(c.RowVersion),
                Iteration = c.Iteration, EscalationLevel = c.EscalationLevel, CancelReason = c.CancelReason, CancelledAt = c.CancelledAt,
                EffectivenessEarliest = c.ImplementedAt.HasValue ? CapaRules.EffectivenessEarliest(c.ImplementedAt.Value, (int)settingsForRead[QualitySettingKeys.EffectivenessWaitDays]) : (DateTime?)null,
                Actions = c.Actions.OrderBy(a => a.Id).Select(a => new CapaActionDto
                {
                    Id = a.Id, Description = a.Description, OwnerUserId = a.OwnerUserId, OwnerName = names.Name(a.OwnerUserId), DueDate = a.DueDate, DoneAt = a.DoneAt,
                    EvidenceDocumentId = a.EvidenceDocumentId, EvidenceNote = a.EvidenceNote, Overdue = !a.DoneAt.HasValue && a.DueDate.HasValue && a.DueDate.Value.Date < today,
                    Iteration = a.Iteration,
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
                // review M11: the source decides whether the CAPA is mandatory; it is fixed when the CAPA is created
                if (dto.Source != capa.Source || dto.SourceId != capa.SourceId) return Bad<CapaDto>("منبع CAPA پس از ثبت تغییر نمی‌کند؛ اگر منبع را اشتباه ثبت کرده‌اید، آن را لغو و CAPA تازه‌ای ثبت کنید.");
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
            var removedActionIds = new List<int>();
            foreach (CapaAction old in capa.Actions.ToList())
            {
                SaveCapaActionDto match = input.FirstOrDefault(a => a.Id == old.Id);
                if (match == null)
                {
                    if (old.DoneAt.HasValue) return Bad<CapaDto>("اقدام انجام‌شده حذف نمی‌شود.");
                    removedActionIds.Add(old.Id);
                    capa.Actions.Remove(old);
                }
                else if (!old.DoneAt.HasValue)
                {
                    old.Description = Clean(match.Description); old.OwnerUserId = match.OwnerUserId; old.DueDate = match.DueDate?.Date;
                }
            }
            // the actions of a new round (after a failed effectiveness check) belong to the new iteration
            foreach (SaveCapaActionDto a in input.Where(a => !a.Id.HasValue || capa.Actions.All(x => x.Id != a.Id.Value)))
                capa.Actions.Add(new CapaAction { Description = Clean(a.Description), OwnerUserId = a.OwnerUserId, DueDate = a.DueDate?.Date, Iteration = capa.Iteration });
            capa.Problem = Clean(dto.Problem); capa.Containment = Clean(dto.Containment);
            capa.EditorUserIds = global::Services.NasbNiro.Finance.LedgerMath.WithEditor(capa.EditorUserIds, Me);
            capa.RootCauseMethod = dto.RootCauseMethod; capa.RootCauseAnalysis = Clean(dto.RootCauseAnalysis); capa.OwnerUserId = dto.OwnerUserId;
            if (dto.DueDate.HasValue) capa.DueDate = dto.DueDate.Value.Date;
            capa.EffectivenessCriteria = Clean(dto.EffectivenessCriteria);
            bool complete = capa.OwnerUserId.HasValue && capa.DueDate.HasValue && capa.RootCauseMethod != RootCauseMethod.None && capa.RootCauseAnalysis != null &&
                            capa.EffectivenessCriteria != null && capa.Actions.Any(a => a.Iteration == capa.Iteration);
            capa.Status = complete ? CapaStatus.ActionsDefined : CapaStatus.Open;
            capa.ModifyDate = now; capa.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<CapaDto>(QualityText.Concurrency); }
            await SyncAssignGateAsync(capa, cancellationToken);
            foreach (int removed in removedActionIds) await _notify.CloseAsync("capa-action:" + removed, "اقدام از CAPA حذف شد.", cancellationToken);
            await SyncActionTasksAsync(capa, cancellationToken);
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
                    // the day the action was really done may be entered late, within the back-date window of the company (never in the future); the waiting period of the
                    // effectiveness check runs from the last such day, so a late entry does not start the waiting period again
                    DateTime doneOn = (dto.Date ?? DateTime.Today).Date;
                    string doneDateProblem = await QualityChecks.RecordDateAsync(_uow, doneOn, "انجام اقدام CAPA", cancellationToken, allowTomorrow: false);
                    if (doneDateProblem != null) return Bad<CapaDto>(doneDateProblem);
                    action.DoneAt = doneOn; action.EvidenceNote = Clean(dto.Note); action.EvidenceDocumentId = dto.EvidenceDocumentId;
                    if (action.OwnerUserId != Me) capa.EditorUserIds = global::Services.NasbNiro.Finance.LedgerMath.WithEditor(capa.EditorUserIds, Me); // done on behalf of the owner
                    break;
                case "implemented":
                    if (capa.Status != CapaStatus.ActionsDefined) return Bad<CapaDto>("فقط CAPA با اقدام‌های تعریف‌شده اجرا می‌شود.");
                    if (!staff && capa.OwnerUserId != Me) return Bad<CapaDto>(QualityText.NotAllowed);
                    if (!capa.Actions.Any(a => a.Iteration == capa.Iteration)) return Bad<CapaDto>("برای این دور CAPA هنوز اقدامی تعریف نشده است.");
                    if (capa.Actions.Any(a => !a.DoneAt.HasValue)) return Bad<CapaDto>("همه اقدام‌ها باید انجام شده باشند.");
                    capa.Status = CapaStatus.Implemented;
                    capa.ImplementedAt = CapaRules.ImplementedOn(capa.Actions.Where(a => a.Iteration == capa.Iteration).Select(a => a.DoneAt), DateTime.Today);
                    if (capa.OwnerUserId != Me) capa.EditorUserIds = global::Services.NasbNiro.Finance.LedgerMath.WithEditor(capa.EditorUserIds, Me);
                    Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
                    capa.EffectivenessDueDate = DateTime.Today.AddDays((int)settings[QualitySettingKeys.CapaEffectivenessDays]);
                    break;
                case "effectiveness":
                    if (capa.Status != CapaStatus.Implemented) return Bad<CapaDto>("بررسی اثربخشی فقط پس از اجرای اقدام‌ها ثبت می‌شود.");
                    if (!_user.IsInRole(QualityRoles.CapaClosers)) return Bad<CapaDto>(QualityText.NotAllowed);
                    if (IsMaker(capa)) return Bad<CapaDto>(QualityText.SameUser + " (اثربخشی را کسی که CAPA را تعریف یا ویرایش کرده یا مسئول CAPA یا یکی از اقدام‌های آن است ارزیابی نمی‌کند)");
                    if (Clean(dto.Note) == null) return Bad<CapaDto>("نتیجه بررسی اثربخشی را (بر اساس معیار تعریف‌شده) بنویسید.");
                    // R1q D20: the effect of an action shows over time; the check is made no earlier than the waiting period after the implementation
                    Dictionary<string, decimal> waitSettings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
                    DateTime earliest = CapaRules.EffectivenessEarliest(capa.ImplementedAt ?? DateTime.Today, (int)waitSettings[QualitySettingKeys.EffectivenessWaitDays]);
                    if (DateTime.Today < earliest)
                        return Bad<CapaDto>($"بررسی اثربخشی زودتر از {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(earliest))} (حداقل {(int)waitSettings[QualitySettingKeys.EffectivenessWaitDays]} روز پس از اجرای اقدام‌ها) ثبت نمی‌شود.");
                    capa.EffectivenessResult = Clean(dto.Note);
                    if (dto.Passed)
                    {
                        capa.Status = CapaStatus.EffectivenessCheck;
                    }
                    else
                    {
                        // R1q D20: a failed check re-opens the CAPA for a new round: new actions, a new implementation and a new check (the old actions stay as the record of round n)
                        // the old due date is cleared: the new round needs a new one before its actions can be defined (review M11)
                        capa.Status = CapaStatus.Open; capa.ImplementedAt = null; capa.EffectivenessDueDate = null; capa.DueDate = null;
                        capa.Iteration++;
                        capa.CloseNote = null;
                    }
                    break;
                case "close":
                    if (capa.Status != CapaStatus.EffectivenessCheck) return Bad<CapaDto>("بستن CAPA فقط پس از تایید اثربخشی ممکن است.");
                    if (!_user.IsInRole(QualityRoles.CapaClosers)) return Bad<CapaDto>(QualityText.NotAllowed + " (CAPA را تضمین کیفیت یا مدیر QC می‌بندد)");
                    if (IsMaker(capa)) return Bad<CapaDto>(QualityText.SameUser + " (CAPA را کسی که آن را تعریف یا ویرایش کرده یا مسئول CAPA یا یکی از اقدام‌های آن است نمی‌بندد)");
                    capa.Status = CapaStatus.Closed; capa.ClosedAt = now; capa.ClosedByUserId = Me; capa.CloseNote = Clean(dto.Note);
                    break;
                case "cancel":
                    if (!_user.IsInRole(CapaStaff)) return Bad<CapaDto>(QualityText.NotAllowed);
                    if (capa.Status == CapaStatus.Closed || capa.Status == CapaStatus.Cancelled) return Bad<CapaDto>("CAPA بسته یا لغو شده است.");
                    if (Clean(dto.Note) == null) return Bad<CapaDto>("دلیل لغو را بنویسید.");
                    // R1q D21: a CAPA the system or a rule required (major NCR, audit finding, auto-drafted) is cancelled only by the QA manager, with a reason that stays on the record
                    if ((capa.Mandatory || CapaRules.IsMandatory(capa.AutoDrafted, capa.Source)) && !_user.IsInRole(QualityRoles.QaManager))
                        return Bad<CapaDto>(QualityText.NotAllowed + " (CAPA الزامی را فقط مدیر QA با ثبت دلیل لغو می‌کند)");
                    // review M11: nobody cancels the CAPA he created, edited or owns (a second person decides it is not needed)
                    if (IsMaker(capa)) return Bad<CapaDto>(QualityText.SameUser + " (CAPA را کسی که آن را تعریف یا ویرایش کرده یا مسئول CAPA یا یکی از اقدام‌های آن است لغو نمی‌کند)");
                    capa.Status = CapaStatus.Cancelled; capa.CloseNote = Clean(dto.Note); capa.ClosedAt = now; capa.ClosedByUserId = Me;
                    capa.CancelReason = Clean(dto.Note); capa.CancelledAt = now; capa.CancelledByUserId = Me;
                    break;
                default:
                    return Bad<CapaDto>("عملیات نامعتبر است.");
            }
            capa.ModifyDate = now; capa.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<CapaDto>(QualityText.Concurrency); }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "capa:" + capa.Id, capa.Status == CapaStatus.Implemented || capa.Status == CapaStatus.EffectivenessCheck, "quality", "capa", capa.Id,
                (capa.Status == CapaStatus.Implemented ? "بررسی اثربخشی CAPA " : "بستن CAPA ") + capa.Number, "/quality/capa/" + capa.Id, QualityRoles.CapaClosers, cancellationToken,
                capa.Status == CapaStatus.Implemented ? capa.EffectivenessDueDate : (DateTime?)null);
            await SyncAssignGateAsync(capa, cancellationToken);
            await SyncActionTasksAsync(capa, cancellationToken);
            if (capa.Status == CapaStatus.Closed || capa.Status == CapaStatus.Cancelled) await CloseLateAlertsAsync(capa.Id, capa.Status == CapaStatus.Closed ? "CAPA بسته شد." : "CAPA لغو شد.", cancellationToken);
            return await GetCapaAsync(capa.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------------- inbox and escalation (R1q D13)

        /// <summary>The "late CAPA" alerts of QA and the CEO stop being true when the CAPA is closed, cancelled or on time again (review M11).</summary>
        private async Task CloseLateAlertsAsync(int capaId, string reason, CancellationToken cancellationToken)
        {
            await _notify.CloseAsync("capa-late:qa:" + capaId, reason, cancellationToken);
            await _notify.CloseAsync("capa-late:ceo:" + capaId, reason, cancellationToken);
        }

        private async Task NotifyLateAsync(Capa c, int level, CancellationToken cancellationToken)
        {
            string title = (level >= 2 ? "CAPA با دیرکرد طولانی (اطلاع مدیرعامل): " : "CAPA دیرکرددار: ") + c.Number;
            await _notify.NotifyAsync(new global::Services.NasbNiro.Office.NotifyRequest
            {
                SourceKey = "capa-late:qa:" + c.Id, Module = "quality", SourceType = "capa-late", SourceId = c.Id, Title = title, Link = "/quality/capa/" + c.Id,
                Kind = global::Entities.Model.NasbNiro.Office.OfInboxKind.Alert, Roles = "QA", DueDate = c.DueDate,
            }, cancellationToken);
            if (level >= 2)
                await _notify.NotifyAsync(new global::Services.NasbNiro.Office.NotifyRequest
                {
                    SourceKey = "capa-late:ceo:" + c.Id, Module = "quality", SourceType = "capa-late", SourceId = c.Id, Title = title, Link = "/quality/capa/" + c.Id,
                    Kind = global::Entities.Model.NasbNiro.Office.OfInboxKind.Alert, Roles = "CEO", DueDate = c.DueDate,
                }, cancellationToken);
        }

        /// <summary>Each open action of the current round with an owner is a task in the inbox of that owner until it is done (or the CAPA stops).</summary>
        private async Task SyncActionTasksAsync(Capa capa, CancellationToken cancellationToken)
        {
            if (capa == null || capa.Actions == null) return;
            foreach (CapaAction a in capa.Actions)
            {
                bool waiting = capa.Status == CapaStatus.ActionsDefined && !a.DoneAt.HasValue && a.OwnerUserId.HasValue && a.Iteration == capa.Iteration && !capa.Deleted;
                await _notify.GateAsync("capa-action:" + a.Id, waiting, new global::Services.NasbNiro.Office.NotifyRequest
                {
                    SourceKey = "capa-action:" + a.Id, Module = "quality", SourceType = "capa-action", SourceId = capa.Id,
                    Title = $"اقدام CAPA {capa.Number}: {Cut(a.Description, 120)}", Link = "/quality/capa/" + capa.Id, Kind = global::Entities.Model.NasbNiro.Office.OfInboxKind.Task,
                    DueDate = a.DueDate, UserIds = a.OwnerUserId.HasValue ? new List<int> { a.OwnerUserId.Value } : new List<int>(),
                }, cancellationToken);
            }
        }

        private static string Cut(string s, int n) => s == null ? string.Empty : (s.Length <= n ? s : s.Substring(0, n) + "…");

        public async Task<CapaEscalationResultDto> EscalateAsync(CancellationToken cancellationToken)
        {
            var result = new CapaEscalationResultDto();
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            int ceoAfter = (int)settings[QualitySettingKeys.CapaCeoEscalationDays];
            DateTime today = DateTime.Today;
            bool bootstrapped = await _uow.Set<QualitySetting>().AnyAsync(x => x.Key == QualitySettingKeys.CapaEscalationBootstrapped, cancellationToken);
            List<Capa> open = await _uow.Set<Capa>().Where(c => !c.Deleted && c.Status != CapaStatus.Closed && c.Status != CapaStatus.Cancelled).ToListAsync(cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;

            if (!bootstrapped)
            {
                // review M11: the first run after the upgrade only records the level of the CAPAs that are already late; telling QA and the CEO about the whole backlog in one
                // night would bury the alerts that matter. The marker is saved in the same transaction, so this happens once.
                result.FirstRun = true;
                foreach (Capa c in open)
                {
                    DateTime? due = (c.Status == CapaStatus.Implemented || c.Status == CapaStatus.EffectivenessCheck) ? c.EffectivenessDueDate : c.DueDate;
                    int level = CapaRules.EscalationLevel(due, today, ceoAfter);
                    if (level == c.EscalationLevel) continue;
                    c.EscalationLevel = level; c.EscalatedAt = level > 0 ? now : (DateTimeOffset?)null;
                    if (level > 0) result.Recorded++;
                }
                _uow.Set<QualitySetting>().Add(new QualitySetting
                {
                    Key = QualitySettingKeys.CapaEscalationBootstrapped, Value = 1, Title = "اجرای اول بررسی شبانه CAPA انجام شد", Source = "سامانه",
                });
                try { await _uow.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateConcurrencyException) { result.Skipped = open.Count; }
                return result;
            }

            foreach (Capa c in open)
            {
                DateTime? due = (c.Status == CapaStatus.Implemented || c.Status == CapaStatus.EffectivenessCheck) ? c.EffectivenessDueDate : c.DueDate;
                int level = CapaRules.EscalationLevel(due, today, ceoAfter);
                if (level == c.EscalationLevel) continue;
                int previous = c.EscalationLevel;
                try
                {
                    // the people are told first and the level is saved afterwards: when the notice fails the level stays, and the next run tells them again
                    if (level > c.EscalationLevel) await NotifyLateAsync(c, level, cancellationToken);
                    else if (level == 0) await CloseLateAlertsAsync(c.Id, "CAPA دیگر دیرکرد ندارد.", cancellationToken); // recovered (a new due date or a new round): it can be escalated again
                    c.EscalationLevel = level;
                    if (level > 0) c.EscalatedAt = now;
                    await _uow.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // somebody changed the CAPA at the same moment (a manual run and the nightly job overlap): leave it for the next run
                    _uow.Entry(c).State = EntityState.Detached;
                    result.Skipped++;
                    continue;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    _uow.Entry(c).State = EntityState.Detached;
                    result.Skipped++;
                    continue;
                }
                if (level > previous)
                {
                    result.Escalated++;
                    if (level >= 2) result.CeoNotified++;
                }
            }
            return result;
        }

        // -------------------------------------------------------------------------------- complaints

        private async Task<ComplaintDto> ComplaintDtoAsync(CustomerComplaint c, CancellationToken cancellationToken)
        {
            string wo = c.WorkOrderId.HasValue ? await _uow.Set<WorkOrder>().Where(w => w.Id == c.WorkOrderId.Value).Select(w => w.Code).FirstOrDefaultAsync(cancellationToken) : null;
            string ncr = c.NcrId.HasValue ? await _uow.Set<NonConformance>().Where(n => n.Id == c.NcrId.Value).Select(n => n.Number).FirstOrDefaultAsync(cancellationToken) : null;
            string capa = c.CapaId.HasValue ? await _uow.Set<Capa>().Where(n => n.Id == c.CapaId.Value).Select(n => n.Number).FirstOrDefaultAsync(cancellationToken) : null;
            string serviceRequest = c.ServiceRequestId.HasValue ? await _uow.Set<Entities.Model.NasbNiro.AfterSales.SvServiceRequest>().Where(r => r.Id == c.ServiceRequestId.Value).Select(r => r.Number).FirstOrDefaultAsync(cancellationToken) : null;
            return new ComplaintDto
            {
                ServiceRequestId = c.ServiceRequestId, ServiceRequestNumber = serviceRequest,
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
            await SyncAssignGateAsync(c.Capa, cancellationToken);
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
