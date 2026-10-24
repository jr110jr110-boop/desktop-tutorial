using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface IAuditReviewService
    {
        Task<ResponseDto<List<AuditProgramDto>>> GetProgramsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<AuditProgramDto>> SaveProgramAsync(SaveAuditProgramDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<AuditListDto>>> GetAuditsAsync(AuditQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<AuditDto>> GetAuditAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<AuditDto>> SaveAuditAsync(SaveAuditDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<AuditDto>> AuditActionAsync(AuditActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<AuditDto>> SaveFindingAsync(SaveAuditFindingDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<ReviewListDto>>> GetReviewsAsync(ReviewQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ReviewDto>> GetReviewAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<ReviewDto>> CreateReviewAsync(CreateReviewDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ReviewDto>> SaveInputAsync(SaveReviewInputDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ReviewDto>> SaveDecisionAsync(SaveReviewDecisionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ReviewDto>> ReviewActionAsync(ReviewActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Internal audits (ISO 9001 9.2) and management review (9.3). The system drafts the review inputs from the records.</summary>
    public class AuditReviewService : IAuditReviewService
    {
        private const string ReviewRoles = "QA,PMO,CEO";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICapaService _capa;

        public AuditReviewService(IUnitOfWork uow, IInventoryUserContext user, ICapaService capa)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _capa = capa ?? throw new ArgumentNullException(nameof(capa));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        // -------------------------------------------------------------------------------- programs

        public async Task<ResponseDto<List<AuditProgramDto>>> GetProgramsAsync(CancellationToken cancellationToken)
        {
            List<AuditProgram> programs = await _uow.Set<AuditProgram>().Where(p => !p.Deleted).OrderByDescending(p => p.Year).ToListAsync(cancellationToken);
            var counts = await _uow.Set<Audit>().Where(a => !a.Deleted && a.ProgramId.HasValue).GroupBy(a => a.ProgramId.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(programs.Select(p => new AuditProgramDto
            {
                Id = p.Id, Year = p.Year, Scope = p.Scope, Note = p.Note, AuditCount = counts.FirstOrDefault(c => c.Id == p.Id)?.Count ?? 0,
            }).ToList());
        }

        public async Task<ResponseDto<AuditProgramDto>> SaveProgramAsync(SaveAuditProgramDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || dto.Year < 1300 || dto.Year > 2200 || Clean(dto.Scope) == null) return Bad<AuditProgramDto>("سال و دامنه برنامه ممیزی را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.Audit)) return Bad<AuditProgramDto>(QualityText.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            AuditProgram p;
            if (dto.Id.HasValue)
            {
                p = await _uow.Set<AuditProgram>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (p == null) return QualityText.NotFound<AuditProgramDto>("برنامه ممیزی پیدا نشد.");
            }
            else
            {
                p = new AuditProgram { CreateDate = now, CreatorUserId = Me };
                _uow.Set<AuditProgram>().Add(p);
            }
            p.Year = dto.Year; p.Scope = Clean(dto.Scope); p.Note = Clean(dto.Note); p.ModifyDate = now; p.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new AuditProgramDto
            {
                Id = p.Id, Year = p.Year, Scope = p.Scope, Note = p.Note, AuditCount = await _uow.Set<Audit>().CountAsync(a => a.ProgramId == p.Id && !a.Deleted, cancellationToken),
            });
        }

        // -------------------------------------------------------------------------------- audits

        private static List<int> Team(Audit a) => QualityText.SplitList(a.TeamUserIds).Select(s => int.TryParse(s, out int id) ? id : 0).Where(id => id > 0).ToList();

        public async Task<ResponseDto<PagedList<AuditListDto>>> GetAuditsAsync(AuditQuery query, CancellationToken cancellationToken)
        {
            query ??= new AuditQuery();
            DateTime today = DateTime.Today;
            IQueryable<Audit> rows = _uow.Set<Audit>().Where(a => !a.Deleted);
            if (query.Status.HasValue) rows = rows.Where(a => a.Status == query.Status.Value);
            if (query.ProgramId.HasValue) rows = rows.Where(a => a.ProgramId == query.ProgramId.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(a => a.Number.Contains(text) || a.AuditedUnit.Contains(text));
            int total = await rows.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize;
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            var items = await rows.OrderByDescending(a => a.Id).Skip((number - 1) * size).Take(size)
                .Select(a => new { a.Id, a.Number, a.ProgramId, a.AuditedUnit, a.Criteria, a.PlannedDate, a.LeadAuditorUserId, a.Status, Findings = a.Findings.Count(f => !f.Deleted) }).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, items.Select(i => (int?)i.LeadAuditorUserId), cancellationToken);
            PagedList<AuditListDto> page = await PagedList<AuditListDto>.CreateAsync(items.Select(i => new AuditListDto
            {
                Id = i.Id, Number = i.Number, ProgramId = i.ProgramId, AuditedUnit = i.AuditedUnit, Criteria = i.Criteria, PlannedDate = i.PlannedDate,
                LeadAuditorName = names.Name(i.LeadAuditorUserId), Status = i.Status, FindingCount = i.Findings, Overdue = i.Status == AuditStatus.Planned && i.PlannedDate.Date < today,
            }).ToList(), 1, 0, cancellationToken);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<AuditDto>> GetAuditAsync(int id, CancellationToken cancellationToken)
        {
            Audit a = await _uow.Set<Audit>().Include(x => x.Findings).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (a == null) return QualityText.NotFound<AuditDto>("ممیزی پیدا نشد.");
            List<int> team = Team(a);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, team.Select(t => (int?)t).Concat(new int?[] { a.LeadAuditorUserId }), cancellationToken);
            List<int> capaIds = a.Findings.Where(f => f.CapaId.HasValue).Select(f => f.CapaId.Value).ToList();
            var capas = await _uow.Set<Capa>().Where(c => capaIds.Contains(c.Id)).Select(c => new { c.Id, c.Number, c.Status }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new AuditDto
            {
                Id = a.Id, Number = a.Number, ProgramId = a.ProgramId, AuditedUnit = a.AuditedUnit, Criteria = a.Criteria, PlannedDate = a.PlannedDate,
                LeadAuditorName = names.Name(a.LeadAuditorUserId), Status = a.Status, FindingCount = a.Findings.Count(f => !f.Deleted),
                Overdue = a.Status == AuditStatus.Planned && a.PlannedDate.Date < DateTime.Today, LeadAuditorUserId = a.LeadAuditorUserId, TeamUserIds = team,
                TeamNames = team.Select(t => names.Name(t)).ToList(), ReportDocumentId = a.ReportDocumentId, DoneAt = a.DoneAt, ClosedAt = a.ClosedAt,
                Findings = a.Findings.Where(f => !f.Deleted).OrderBy(f => f.Id).Select(f => new AuditFindingDto
                {
                    Id = f.Id, AuditId = f.AuditId, Clause = f.Clause, Type = f.Type, Evidence = f.Evidence, CapaId = f.CapaId,
                    CapaNumber = capas.FirstOrDefault(c => c.Id == f.CapaId)?.Number, CapaStatus = capas.FirstOrDefault(c => c.Id == f.CapaId)?.Status, DueDate = f.DueDate,
                }).ToList(),
            });
        }

        public async Task<ResponseDto<AuditDto>> SaveAuditAsync(SaveAuditDto dto, CancellationToken cancellationToken)
        {
            string unit = dto == null ? null : QualityText.Upper(dto.AuditedUnit);
            if (dto == null || unit == null || Clean(dto.Criteria) == null || dto.PlannedDate == default) return Bad<AuditDto>("واحد ممیزی‌شونده، معیارها و تاریخ برنامه‌ریزی را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.Audit)) return Bad<AuditDto>(QualityText.NotAllowed);
            List<int> team = (dto.TeamUserIds ?? new List<int>()).Where(t => t != dto.LeadAuditorUserId).Distinct().ToList();
            List<int> auditors = team.Concat(new[] { dto.LeadAuditorUserId }).ToList();
            if (await _uow.Set<User>().CountAsync(u => auditors.Contains(u.Id), cancellationToken) != auditors.Count) return Bad<AuditDto>("ممیز انتخاب‌شده پیدا نشد.");
            // auditors do not audit their own unit: the unit is compared with the role names of every auditor
            List<string> roles = await _uow.Set<UserRole>().Where(ur => auditors.Contains(ur.UserId)).Select(ur => ur.Role.Name).ToListAsync(cancellationToken);
            if (roles.Any(r => string.Equals(r, unit, StringComparison.OrdinalIgnoreCase)))
                return Bad<AuditDto>($"ممیز نمی‌تواند واحد خود ({unit}) را ممیزی کند (استقلال ممیز، ISO 9001 بند 9.2).");
            if (dto.ProgramId.HasValue && !await _uow.Set<AuditProgram>().AnyAsync(p => p.Id == dto.ProgramId.Value && !p.Deleted, cancellationToken)) return Bad<AuditDto>("برنامه ممیزی پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            Audit a;
            if (dto.Id.HasValue)
            {
                a = await _uow.Set<Audit>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (a == null) return QualityText.NotFound<AuditDto>("ممیزی پیدا نشد.");
                if (a.Status != AuditStatus.Planned) return Bad<AuditDto>("ممیزی انجام‌شده ویرایش نمی‌شود.");
            }
            else
            {
                a = new Audit
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Audit, "AUD", dto.PlannedDate, cancellationToken),
                    Status = AuditStatus.Planned, Findings = new List<AuditFinding>(), CreateDate = now, CreatorUserId = Me,
                };
                _uow.Set<Audit>().Add(a);
            }
            a.ProgramId = dto.ProgramId; a.AuditedUnit = unit; a.Criteria = Clean(dto.Criteria); a.PlannedDate = dto.PlannedDate.Date;
            a.LeadAuditorUserId = dto.LeadAuditorUserId; a.TeamUserIds = string.Join(",", team); a.ModifyDate = now; a.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetAuditAsync(a.Id, cancellationToken);
        }

        private static bool IsNc(AuditFindingType type) => type == AuditFindingType.MajorNC || type == AuditFindingType.MinorNC;

        public async Task<ResponseDto<AuditDto>> SaveFindingAsync(SaveAuditFindingDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Clause) == null || Clean(dto.Evidence) == null || !Enum.IsDefined(typeof(AuditFindingType), dto.Type)) return Bad<AuditDto>("بند، نوع و شواهد یافته را وارد کنید.");
            if (!_user.IsInRole(QualityRoles.Audit)) return Bad<AuditDto>(QualityText.NotAllowed);
            Audit audit = await _uow.Set<Audit>().Include(a => a.Findings).FirstOrDefaultAsync(a => a.Id == dto.AuditId && !a.Deleted, cancellationToken);
            if (audit == null) return QualityText.NotFound<AuditDto>("ممیزی پیدا نشد.");
            if (audit.Status == AuditStatus.Closed) return Bad<AuditDto>("ممیزی بسته شده است.");
            AuditFinding f = dto.Id.HasValue ? audit.Findings.FirstOrDefault(x => x.Id == dto.Id.Value && !x.Deleted) : null;
            if (dto.Id.HasValue && f == null) return QualityText.NotFound<AuditDto>("یافته پیدا نشد.");
            Capa existing = null;
            if (dto.CapaId.HasValue)
            {
                existing = await _uow.Set<Capa>().FirstOrDefaultAsync(c => c.Id == dto.CapaId.Value && !c.Deleted, cancellationToken);
                if (existing == null) return Bad<AuditDto>("CAPA پیدا نشد.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            if (f == null)
            {
                f = new AuditFinding { CreateDate = now, CreatorUserId = Me };
                audit.Findings.Add(f);
            }
            f.Clause = Clean(dto.Clause); f.Type = dto.Type; f.Evidence = Clean(dto.Evidence); f.DueDate = dto.DueDate?.Date;
            // a nonconformance always has a CAPA: the system drafts it when none is named, QA assigns the owner
            if (IsNc(f.Type))
            {
                if (existing != null) f.Capa = existing;
                else if (!f.CapaId.HasValue && f.Capa == null)
                {
                    Capa capa = await _capa.DraftAsync(CapaSource.AuditFinding, audit.Id, $"یافته ممیزی {audit.Number} بند {f.Clause}: {f.Evidence}", true, cancellationToken);
                    f.Capa = capa;
                }
            }
            else if (existing != null) f.Capa = existing;
            f.ModifyDate = now; f.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            await _capa.SyncAssignGateAsync(f.Capa, cancellationToken);
            return await GetAuditAsync(audit.Id, cancellationToken);
        }

        public async Task<ResponseDto<AuditDto>> AuditActionAsync(AuditActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<AuditDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Audit)) return Bad<AuditDto>(QualityText.NotAllowed);
            Audit a = await _uow.Set<Audit>().Include(x => x.Findings).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (a == null) return QualityText.NotFound<AuditDto>("ممیزی پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "done":
                    if (a.Status != AuditStatus.Planned) return Bad<AuditDto>("فقط ممیزی برنامه‌ریزی‌شده انجام‌شده ثبت می‌شود.");
                    if (!dto.ReportDocumentId.HasValue) return Bad<AuditDto>("گزارش ممیزی را پیوست کنید.");
                    if (a.Findings.Any(f => !f.Deleted && IsNc(f.Type) && !f.CapaId.HasValue && f.Capa == null)) return Bad<AuditDto>("برای هر عدم انطباق ممیزی باید CAPA ثبت شود.");
                    a.Status = AuditStatus.Done; a.DoneAt = now; a.ReportDocumentId = dto.ReportDocumentId;
                    break;
                case "close":
                    if (a.Status != AuditStatus.Done) return Bad<AuditDto>("ابتدا ممیزی باید انجام‌شده ثبت شود.");
                    List<int> capaIds = a.Findings.Where(f => !f.Deleted && f.CapaId.HasValue).Select(f => f.CapaId.Value).ToList();
                    List<string> open = await _uow.Set<Capa>().Where(c => capaIds.Contains(c.Id) && c.Status != CapaStatus.Closed && c.Status != CapaStatus.Cancelled).Select(c => c.Number).ToListAsync(cancellationToken);
                    if (open.Count > 0) return Bad<AuditDto>("ممیزی تا بسته‌شدن CAPA همه عدم انطباق‌ها بسته نمی‌شود: " + string.Join("، ", open));
                    a.Status = AuditStatus.Closed; a.ClosedAt = now; a.ClosedByUserId = Me;
                    break;
                default:
                    return Bad<AuditDto>("عملیات نامعتبر است.");
            }
            a.ModifyDate = now; a.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetAuditAsync(a.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------------- management review

        private static readonly Dictionary<ReviewInputCategory, string> CategoryNames = new Dictionary<ReviewInputCategory, string>
        {
            { ReviewInputCategory.PreviousActions, "وضعیت اقدام‌های جلسه‌های قبلی" },
            { ReviewInputCategory.ChangesInIssues, "تغییر در موضوعات داخلی و خارجی مرتبط با سیستم مدیریت کیفیت" },
            { ReviewInputCategory.CustomerSatisfaction, "رضایت مشتری و بازخورد طرف‌های ذی‌نفع" },
            { ReviewInputCategory.ObjectivesAchievement, "میزان تحقق اهداف کیفیت" },
            { ReviewInputCategory.ProcessPerformance, "عملکرد فرایندها و انطباق محصول و خدمات" },
            { ReviewInputCategory.NonconformitiesAndCorrectiveActions, "عدم انطباق‌ها و اقدام‌های اصلاحی" },
            { ReviewInputCategory.MonitoringAndMeasurement, "نتایج پایش و اندازه‌گیری" },
            { ReviewInputCategory.AuditResults, "نتایج ممیزی‌ها" },
            { ReviewInputCategory.ExternalProviderPerformance, "عملکرد تامین‌کنندگان خارجی" },
            { ReviewInputCategory.ResourceAdequacy, "کفایت منابع" },
            { ReviewInputCategory.RiskActionEffectiveness, "اثربخشی اقدام‌های مقابله با ریسک‌ها و فرصت‌ها" },
            { ReviewInputCategory.ImprovementOpportunities, "فرصت‌های بهبود" },
        };

        /// <summary>Texts the system can compute from the records; the other categories are written by people.</summary>
        private async Task<Dictionary<ReviewInputCategory, string>> DraftInputsAsync(CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            DateTime from = today.AddYears(-1);
            var drafts = new Dictionary<ReviewInputCategory, string>();

            int overdueDecisions = await _uow.Set<ManagementReviewDecision>().CountAsync(d => d.Review.Status != ReviewStatus.Draft && d.DueDate < today &&
                (d.CapaId == null || (d.Capa.Status != CapaStatus.Closed && d.Capa.Status != CapaStatus.Cancelled)), cancellationToken);
            drafts[ReviewInputCategory.PreviousActions] = $"تصمیم‌های جلسه‌های قبل با موعد گذشته و بسته‌نشده: {overdueDecisions}";

            int complaints = await _uow.Set<CustomerComplaint>().CountAsync(c => !c.Deleted && c.ReceivedDate >= from, cancellationToken);
            int complaintsOpen = await _uow.Set<CustomerComplaint>().CountAsync(c => !c.Deleted && c.Status != ComplaintStatus.Closed, cancellationToken);
            int complaintsLate = await _uow.Set<CustomerComplaint>().CountAsync(c => !c.Deleted && c.Status != ComplaintStatus.Responded && c.Status != ComplaintStatus.Closed && c.ResponseDue < today, cancellationToken);
            drafts[ReviewInputCategory.CustomerSatisfaction] = $"شکایت مشتری در ۱۲ ماه گذشته: {complaints}؛ باز: {complaintsOpen}؛ پاسخ دیرکرد: {complaintsLate}";

            int pass = await _uow.Set<TestReport>().CountAsync(r => !r.Deleted && r.Status == TestReportStatus.Approved && r.Result == ReportResult.Pass && r.ReportDate >= from, cancellationToken);
            int fail = await _uow.Set<TestReport>().CountAsync(r => !r.Deleted && r.Result == ReportResult.Fail && r.ReportDate >= from, cancellationToken);
            int rejected = await _uow.Set<QcRecord>().CountAsync(r => !r.Deleted && r.Result == QcResult.Reject && r.RecordDate >= from, cancellationToken);
            drafts[ReviewInputCategory.ProcessPerformance] = $"گزارش آزمون قبول: {pass}؛ مردود: {fail}؛ بازرسی‌های رد شده فرایندی: {rejected}";

            int ncrAll = await _uow.Set<NonConformance>().CountAsync(n => !n.Deleted && n.ReportDate >= from, cancellationToken);
            int ncrOpen = await _uow.Set<NonConformance>().CountAsync(n => !n.Deleted && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled, cancellationToken);
            int capaOpen = await _uow.Set<Capa>().CountAsync(c => !c.Deleted && c.Status != CapaStatus.Closed && c.Status != CapaStatus.Cancelled, cancellationToken);
            int capaLate = await _uow.Set<Capa>().CountAsync(c => !c.Deleted && (((c.Status == CapaStatus.Open || c.Status == CapaStatus.ActionsDefined) && c.DueDate < today) ||
                                                                              (c.Status == CapaStatus.Implemented && c.EffectivenessDueDate < today)), cancellationToken);
            drafts[ReviewInputCategory.NonconformitiesAndCorrectiveActions] = $"عدم انطباق ثبت‌شده در ۱۲ ماه: {ncrAll}؛ باز: {ncrOpen}؛ CAPA باز: {capaOpen}؛ CAPA دیرکرد: {capaLate}";

            int eqLate = await _uow.Set<MeasuringEquipment>().CountAsync(e => !e.Deleted && e.RequiresCalibration && e.Status != EquipmentStatus.OutOfService && (e.NextDue == null || e.NextDue < today), cancellationToken);
            int ootOpen = await _uow.Set<OutOfToleranceCase>().CountAsync(c => !c.Deleted && c.Status == OotStatus.Open, cancellationToken);
            drafts[ReviewInputCategory.MonitoringAndMeasurement] = $"تجهیز اندازه‌گیری با کالیبراسیون سررسیدگذشته: {eqLate}؛ پرونده خارج از تلرانس باز: {ootOpen}";

            int audits = await _uow.Set<Audit>().CountAsync(a => !a.Deleted && a.Status != AuditStatus.Planned && a.PlannedDate >= from, cancellationToken);
            int major = await _uow.Set<AuditFinding>().CountAsync(f => !f.Deleted && f.Type == AuditFindingType.MajorNC && f.Audit.PlannedDate >= from, cancellationToken);
            int minor = await _uow.Set<AuditFinding>().CountAsync(f => !f.Deleted && f.Type == AuditFindingType.MinorNC && f.Audit.PlannedDate >= from, cancellationToken);
            drafts[ReviewInputCategory.AuditResults] = $"ممیزی انجام‌شده در ۱۲ ماه: {audits}؛ عدم انطباق عمده: {major}؛ جزئی: {minor}";
            return drafts;
        }

        private async Task<ReviewDto> BuildReviewAsync(ManagementReview r, CancellationToken cancellationToken)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, r.Decisions.Select(d => d.ResponsibleUserId), cancellationToken);
            List<int> capaIds = r.Decisions.Where(d => d.CapaId.HasValue).Select(d => d.CapaId.Value).ToList();
            Dictionary<int, string> capas = await _uow.Set<Capa>().Where(c => capaIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, cancellationToken);
            return new ReviewDto
            {
                Id = r.Id, ReviewDate = r.ReviewDate, Status = r.Status, DecisionCount = r.Decisions.Count, MinutesDocumentId = r.MinutesDocumentId, OfMeetingId = r.OfMeetingId, Attendees = r.Attendees,
                Inputs = r.Inputs.OrderBy(i => i.Category).Select(i => new ReviewInputDto { Id = i.Id, Category = i.Category, CategoryName = CategoryNames[i.Category], Text = i.Text, AutoDrafted = i.AutoDrafted }).ToList(),
                Decisions = r.Decisions.OrderBy(d => d.Id).Select(d => new ReviewDecisionDto
                {
                    Id = d.Id, Text = d.Text, ResponsibleUserId = d.ResponsibleUserId, ResponsibleName = names.Name(d.ResponsibleUserId), DueDate = d.DueDate, CapaId = d.CapaId,
                    CapaNumber = d.CapaId.HasValue && capas.TryGetValue(d.CapaId.Value, out string n) ? n : null,
                }).ToList(),
            };
        }

        private IQueryable<ManagementReview> FullReview() =>
            _uow.Set<ManagementReview>().Include(r => r.Inputs).Include(r => r.Decisions).AsSplitQuery();

        public async Task<ResponseDto<PagedList<ReviewListDto>>> GetReviewsAsync(ReviewQuery query, CancellationToken cancellationToken)
        {
            query ??= new ReviewQuery();
            IQueryable<ManagementReview> rows = _uow.Set<ManagementReview>().Where(r => !r.Deleted);
            if (query.Status.HasValue) rows = rows.Where(r => r.Status == query.Status.Value);
            IQueryable<ReviewListDto> projection = rows.OrderByDescending(r => r.ReviewDate).ThenByDescending(r => r.Id).Select(r => new ReviewListDto
            {
                Id = r.Id, ReviewDate = r.ReviewDate, Status = r.Status, DecisionCount = r.Decisions.Count, MinutesDocumentId = r.MinutesDocumentId, OfMeetingId = r.OfMeetingId,
            });
            return ResponseDtoHelper.Ok(await PagedList<ReviewListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<ReviewDto>> GetReviewAsync(int id, CancellationToken cancellationToken)
        {
            ManagementReview r = await FullReview().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return r == null ? QualityText.NotFound<ReviewDto>("جلسه بازنگری مدیریت پیدا نشد.") : ResponseDtoHelper.Ok(await BuildReviewAsync(r, cancellationToken));
        }

        public async Task<ResponseDto<ReviewDto>> CreateReviewAsync(CreateReviewDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ReviewRoles)) return Bad<ReviewDto>(QualityText.NotAllowed);
            if (dto?.OfMeetingId != null && !await _uow.Set<Entities.Model.NasbNiro.Office.OfMeeting>().AnyAsync(m => m.Id == dto.OfMeetingId.Value && !m.Deleted, cancellationToken))
                return Bad<ReviewDto>("جلسه مرتبط در دفتر اتوماسیون اداری پیدا نشد.");
            Dictionary<ReviewInputCategory, string> drafts = await DraftInputsAsync(cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            var review = new ManagementReview
            {
                ReviewDate = (dto?.ReviewDate ?? DateTime.Today).Date, Attendees = Clean(dto?.Attendees), Status = ReviewStatus.Draft, OfMeetingId = dto?.OfMeetingId,
                Inputs = new List<ManagementReviewInput>(), Decisions = new List<ManagementReviewDecision>(), CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            foreach (ReviewInputCategory category in Enum.GetValues(typeof(ReviewInputCategory)))
                review.Inputs.Add(new ManagementReviewInput { Category = category, Text = drafts.TryGetValue(category, out string text) ? text : null, AutoDrafted = drafts.ContainsKey(category) });
            _uow.Set<ManagementReview>().Add(review);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetReviewAsync(review.Id, cancellationToken);
        }

        public async Task<ResponseDto<ReviewDto>> SaveInputAsync(SaveReviewInputDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(ReviewInputCategory), dto.Category)) return Bad<ReviewDto>(QualityText.InvalidData);
            if (!_user.IsInRole(ReviewRoles)) return Bad<ReviewDto>(QualityText.NotAllowed);
            ManagementReview r = await FullReview().FirstOrDefaultAsync(x => x.Id == dto.ReviewId && !x.Deleted, cancellationToken);
            if (r == null) return QualityText.NotFound<ReviewDto>("جلسه بازنگری مدیریت پیدا نشد.");
            if (r.Status == ReviewStatus.Closed) return Bad<ReviewDto>("جلسه بسته شده است.");
            ManagementReviewInput input = r.Inputs.First(i => i.Category == dto.Category);
            input.Text = Clean(dto.Text); input.AutoDrafted = false;
            if (dto.Attendees != null) r.Attendees = Clean(dto.Attendees);
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await BuildReviewAsync(r, cancellationToken));
        }

        public async Task<ResponseDto<ReviewDto>> SaveDecisionAsync(SaveReviewDecisionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Text) == null) return Bad<ReviewDto>("متن تصمیم را بنویسید.");
            if (!_user.IsInRole(ReviewRoles)) return Bad<ReviewDto>(QualityText.NotAllowed);
            ManagementReview r = await FullReview().FirstOrDefaultAsync(x => x.Id == dto.ReviewId && !x.Deleted, cancellationToken);
            if (r == null) return QualityText.NotFound<ReviewDto>("جلسه بازنگری مدیریت پیدا نشد.");
            if (r.Status == ReviewStatus.Closed) return Bad<ReviewDto>("جلسه بسته شده است.");
            if (dto.ResponsibleUserId.HasValue && !await _uow.Set<User>().AnyAsync(u => u.Id == dto.ResponsibleUserId.Value, cancellationToken)) return Bad<ReviewDto>("مسئول پیدا نشد.");
            ManagementReviewDecision d = dto.Id.HasValue ? r.Decisions.FirstOrDefault(x => x.Id == dto.Id.Value) : null;
            if (dto.Id.HasValue && d == null) return QualityText.NotFound<ReviewDto>("تصمیم پیدا نشد.");
            if (d == null) { d = new ManagementReviewDecision(); r.Decisions.Add(d); }
            d.Text = Clean(dto.Text); d.ResponsibleUserId = dto.ResponsibleUserId; d.DueDate = dto.DueDate?.Date;
            if (dto.CreateCapa && !d.CapaId.HasValue && d.Capa == null)
                d.Capa = await _capa.DraftAsync(CapaSource.ManagementReview, r.Id, $"تصمیم بازنگری مدیریت {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(r.ReviewDate))}: {d.Text}", true, cancellationToken);
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            await _capa.SyncAssignGateAsync(d.Capa, cancellationToken);
            return ResponseDtoHelper.Ok(await BuildReviewAsync(r, cancellationToken));
        }

        public async Task<ResponseDto<ReviewDto>> ReviewActionAsync(ReviewActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<ReviewDto>(QualityText.InvalidData);
            if (!_user.IsInRole(ReviewRoles)) return Bad<ReviewDto>(QualityText.NotAllowed);
            ManagementReview r = await FullReview().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return QualityText.NotFound<ReviewDto>("جلسه بازنگری مدیریت پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "redraft":
                    if (r.Status != ReviewStatus.Draft) return Bad<ReviewDto>("فقط جلسه پیش‌نویس دوباره محاسبه می‌شود.");
                    Dictionary<ReviewInputCategory, string> drafts = await DraftInputsAsync(cancellationToken);
                    foreach (ManagementReviewInput i in r.Inputs.Where(i => (i.AutoDrafted || Clean(i.Text) == null) && drafts.ContainsKey(i.Category))) { i.Text = drafts[i.Category]; i.AutoDrafted = true; }
                    break;
                case "hold":
                    if (r.Status != ReviewStatus.Draft) return Bad<ReviewDto>("فقط جلسه پیش‌نویس برگزار می‌شود.");
                    if (Clean(r.Attendees) == null) return Bad<ReviewDto>("حاضران جلسه را ثبت کنید.");
                    List<string> empty = r.Inputs.Where(i => Clean(i.Text) == null).Select(i => CategoryNames[i.Category]).ToList();
                    if (empty.Count > 0) return Bad<ReviewDto>("ورودی‌های بند ۹.۳.۲ تکمیل نشده است: " + string.Join("، ", empty));
                    r.Status = ReviewStatus.Held;
                    break;
                case "close":
                    if (r.Status != ReviewStatus.Held) return Bad<ReviewDto>("ابتدا جلسه باید برگزار شود.");
                    if (!dto.MinutesDocumentId.HasValue) return Bad<ReviewDto>("صورت‌جلسه (FO-QA-022) را پیوست کنید.");
                    if (r.Decisions.Any(d => !d.ResponsibleUserId.HasValue || !d.DueDate.HasValue)) return Bad<ReviewDto>("برای هر تصمیم مسئول و موعد را مشخص کنید.");
                    r.Status = ReviewStatus.Closed; r.MinutesDocumentId = dto.MinutesDocumentId;
                    break;
                default:
                    return Bad<ReviewDto>("عملیات نامعتبر است.");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await BuildReviewAsync(r, cancellationToken));
        }
    }
}
