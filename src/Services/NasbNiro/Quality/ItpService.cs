using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Quality;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface IItpService
    {
        Task<ResponseDto<PagedList<ItpTemplateListDto>>> GetTemplatesAsync(ItpTemplateQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ItpTemplateDto>> GetTemplateAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ItpListDto>>> GetItpsAsync(ItpQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ItpDto>> GetItpAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<ItpDto>> GetByWorkOrderAsync(int workOrderId, CancellationToken cancellationToken);

        /// <summary>Drafts the ITP of a work order from the matching template (returns the existing one when there is one).</summary>
        Task<ResponseDto<ItpDto>> EnsureItpAsync(EnsureItpDto dto, CancellationToken cancellationToken);

        /// <summary>Called by the record and report services: the system drafts the ITP, a person approves it.</summary>
        Task<int?> EnsureItpForWorkOrderAsync(int workOrderId, CancellationToken cancellationToken);

        Task<ResponseDto<ItpDto>> SaveItpAsync(SaveItpDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ItpDto>> ItpActionAsync(ItpActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ItpDto>> SaveActivityAsync(SaveItpActivityDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ItpDto>> ActivityActionAsync(ItpActivityActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ItpDto>> AddReleaseAsync(SaveItpReleaseDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ItpDto>> SetLimitsAsync(SetItpLimitsDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PendingPointDto>>> GetPendingPointsAsync(PendingPointQuery query, CancellationToken cancellationToken);

        /// <summary>After a report is approved or a release is added: marks the activity Done when every condition is met.</summary>
        Task<bool> TryCompleteActivityAsync(int activityId, CancellationToken cancellationToken);
    }

    public class ItpService : IItpService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public ItpService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _notify = notify;
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private DbSet<ItpTemplate> Templates => _uow.Set<ItpTemplate>();
        private DbSet<Itp> Itps => _uow.Set<Itp>();
        private DbSet<ItpActivity> Activities => _uow.Set<ItpActivity>();
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        // -------------------------------------------------------------------------------- templates

        public async Task EnsureSeedAsync(CancellationToken cancellationToken)
        {
            HashSet<string> codes = (await Templates.Where(t => !t.Deleted).Select(t => t.Code).ToListAsync(cancellationToken)).ToHashSet();
            List<ItpTemplate> missing = ItpSeedData.Build().Where(t => !codes.Contains(t.Code)).ToList();
            if (missing.Count == 0) return;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (ItpTemplate template in missing)
            {
                template.CreateDate = now; template.ModifyDate = now; template.CreatorUserId = Me; template.ModifierUserId = Me;
                Templates.Add(template);
            }
            await _uow.SaveChangesAsync(cancellationToken);
        }

        public async Task<ResponseDto<PagedList<ItpTemplateListDto>>> GetTemplatesAsync(ItpTemplateQuery query, CancellationToken cancellationToken)
        {
            query ??= new ItpTemplateQuery();
            await EnsureSeedAsync(cancellationToken);
            IQueryable<ItpTemplate> rows = Templates.Where(t => !t.Deleted);
            if (query.ProductType.HasValue) rows = rows.Where(t => t.ProductType == query.ProductType.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(t => t.Code.Contains(text) || t.Title.Contains(text));
            IQueryable<ItpTemplateListDto> projection = rows.OrderBy(t => t.Id).Select(t => new ItpTemplateListDto
            {
                Id = t.Id, Code = t.Code, Title = t.Title, ProductType = t.ProductType, ApplicableCodes = t.ApplicableCodes,
                Revision = t.Revision, IsActive = t.IsActive, ActivityCount = t.Activities.Count,
            });
            return ResponseDtoHelper.Ok(await PagedList<ItpTemplateListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<ItpTemplateDto>> GetTemplateAsync(int id, CancellationToken cancellationToken)
        {
            ItpTemplate t = await Templates.Include(x => x.Activities).ThenInclude(a => a.Parameters).AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (t == null) return QualityText.NotFound<ItpTemplateDto>("قالب برنامه بازرسی و آزمون پیدا نشد.");
            return ResponseDtoHelper.Ok(new ItpTemplateDto
            {
                Id = t.Id, Code = t.Code, Title = t.Title, ProductType = t.ProductType, ApplicableCodes = t.ApplicableCodes, Revision = t.Revision,
                IsActive = t.IsActive, ActivityCount = t.Activities.Count,
                Activities = t.Activities.OrderBy(a => a.Seq).Select(a => new ItpTemplateActivityDto
                {
                    Id = a.Id, Seq = a.Seq, ItemNo = a.ItemNo, Stage = a.Stage, Activity = a.Activity, TestType = a.TestType,
                    AcceptanceReference = a.AcceptanceReference, PointVendor = a.PointVendor, PointTpi = a.PointTpi, PointClient = a.PointClient,
                    DocumentProduced = a.DocumentProduced, RequiredReportType = a.RequiredReportType, Remark = a.Remark, GatesRelease = a.GatesRelease,
                    Parameters = a.Parameters.OrderBy(p => p.Id).Select(p => ToDto(p.Id, p.Name, p.Unit, p.Min, p.Max, p.Source, p.RequiresLimits)).ToList(),
                }).ToList(),
            });
        }

        private static ItpParameterDto ToDto(int id, string name, string unit, decimal? min, decimal? max, string source, bool requires) =>
            new ItpParameterDto { Id = id, Name = name, Unit = unit, Min = min, Max = max, Source = source, RequiresLimits = requires };

        // -------------------------------------------------------------------------------- reading ITPs

        private IQueryable<Itp> Full() =>
            Itps.Include(i => i.WorkOrder).Include(i => i.Contract).Include(i => i.Template)
                .Include(i => i.Activities).ThenInclude(a => a.Parameters)
                .Include(i => i.Activities).ThenInclude(a => a.Releases)
                .Include(i => i.Revisions).AsSplitQuery();

        public static bool IsTerminal(ItpActivityStatus status) =>
            status == ItpActivityStatus.Done || status == ItpActivityStatus.Waived || status == ItpActivityStatus.NotApplicable;

        public static bool HasPoint(ItpActivity a, ItpPoint point) =>
            a.PointVendor == point || a.PointTpi == point || a.PointClient == point;

        private static ItpPoint PointOf(ItpActivity a, ReleaseParty party) =>
            party == ReleaseParty.Vendor ? a.PointVendor : party == ReleaseParty.Tpi ? a.PointTpi : a.PointClient;

        private static bool IsPresenceAction(ReleaseAction action) => action != ReleaseAction.Waived;

        private static string PartyName(ReleaseParty party) =>
            party == ReleaseParty.Vendor ? "سازنده" : party == ReleaseParty.Tpi ? "بازرس شخص ثالث" : "کارفرما";

        private sealed class ReportInfo
        {
            public int ActivityId { get; set; }
            public TestReportType Type { get; set; }
            public TestReportStatus Status { get; set; }
            public ReportResult Result { get; set; }
        }

        /// <summary>Why the activity cannot be Done yet, and which parties are waived by the notice period.</summary>
        private static List<string> Evaluate(int noticeDays, ItpActivity a, List<ReportInfo> reports, DateTime today, List<ReleaseParty> waiveParties)
        {
            var blockers = new List<string>();
            if (a.RequiredReportType.HasValue)
            {
                List<ReportInfo> mine = reports.Where(r => r.ActivityId == a.Id && r.Status != TestReportStatus.Rejected).ToList();
                if (!mine.Any(r => r.Status == TestReportStatus.Approved && r.Result == ReportResult.Pass)) blockers.Add("گزارش آزمون تاییدشده و قبول برای این فعالیت وجود ندارد.");
                else if (mine.Any(r => r.Status != TestReportStatus.Approved)) blockers.Add("گزارش آزمون تاییدنشده برای این فعالیت هنوز باز است.");
            }
            foreach (ReleaseParty party in new[] { ReleaseParty.Vendor, ReleaseParty.Tpi, ReleaseParty.Client })
            {
                ItpPoint point = PointOf(a, party);
                List<ItpPointRelease> rows = (a.Releases ?? new List<ItpPointRelease>()).Where(r => r.Party == party).ToList();
                if (point == ItpPoint.H)
                {
                    if (!rows.Any(r => IsPresenceAction(r.Action))) blockers.Add($"نقطه توقف (H): ثبت رهاسازی {PartyName(party)} لازم است.");
                }
                else if (point == ItpPoint.W)
                {
                    if (rows.Count > 0) continue;
                    DateTime? deadline = ItpNoticeRules.Deadline(a.NotificationSentAt, noticeDays);
                    if (ItpNoticeRules.WaivedByNotice(a.NotificationSentAt, noticeDays, today)) waiveParties.Add(party);
                    else if (!a.NotificationSentAt.HasValue) blockers.Add($"نقطه شاهد (W): اعلام زمان آزمون به {PartyName(party)} ثبت نشده است.");
                    else blockers.Add($"نقطه شاهد (W): مهلت اعلام {PartyName(party)} تا {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(deadline.Value))} است.");
                }
            }
            return blockers;
        }

        private async Task<List<ReportInfo>> ReportsAsync(IEnumerable<int> activityIds, CancellationToken cancellationToken)
        {
            List<int> ids = activityIds.ToList();
            return await _uow.Set<TestReport>().Where(r => !r.Deleted && r.ItpActivityId.HasValue && ids.Contains(r.ItpActivityId.Value))
                .Select(r => new ReportInfo { ActivityId = r.ItpActivityId.Value, Type = r.Type, Status = r.Status, Result = r.Result }).ToListAsync(cancellationToken);
        }

        private async Task<int> MinNoticeDaysAsync(CancellationToken cancellationToken) =>
            (int)(await QualitySettingKeys.LoadAsync(_uow, cancellationToken))[QualitySettingKeys.MinNoticeDays];

        private async Task<ItpDto> BuildAsync(Itp itp, CancellationToken cancellationToken)
        {
            List<ReportInfo> reports = await ReportsAsync(itp.Activities.Select(a => a.Id), cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, itp.Revisions.Select(r => (int?)r.ChangedByUserId), cancellationToken);
            List<int> letterIds = itp.Activities.Where(a => a.NotificationLetterId.HasValue).Select(a => a.NotificationLetterId.Value)
                .Concat(itp.Activities.Where(a => a.NotApplicableLetterId.HasValue).Select(a => a.NotApplicableLetterId.Value)).Distinct().ToList();
            Dictionary<int, string> letterNumbers = letterIds.Count == 0
                ? new Dictionary<int, string>()
                : await _uow.Set<OfLetter>().Where(l => letterIds.Contains(l.Id)).Select(l => new { l.Id, l.Number }).ToDictionaryAsync(l => l.Id, l => l.Number, cancellationToken);
            DateTime today = DateTime.Today;
            // R1q H6: a plan saved before the minimum existed may hold fewer days; the minimum applies to every deadline
            int noticeDays = ItpNoticeRules.EffectiveDays(itp.NoticeDays, await MinNoticeDaysAsync(cancellationToken));
            var dto = new ItpDto
            {
                Id = itp.Id, Number = itp.Number, WorkOrderId = itp.WorkOrderId, WorkOrderCode = itp.WorkOrder?.Code, ContractCode = itp.Contract?.Code,
                Revision = itp.Revision, Status = itp.Status, AutoDrafted = itp.AutoDrafted, ContractId = itp.ContractId, TemplateId = itp.TemplateId,
                TemplateCode = itp.Template?.Code, ClientApprovalReference = itp.ClientApprovalReference, ClientApprovalDate = itp.ClientApprovalDate,
                NoticeDays = noticeDays, Note = itp.Note, ActivityCount = itp.Activities.Count,
                DoneCount = itp.Activities.Count(a => IsTerminal(a.Status)),
            };
            foreach (ItpActivity a in itp.Activities.OrderBy(x => x.Seq).ThenBy(x => x.Id))
            {
                bool terminal = IsTerminal(a.Status);
                var waive = new List<ReleaseParty>();
                dto.Activities.Add(new ItpActivityDto
                {
                    Id = a.Id, ItpId = a.ItpId, Seq = a.Seq, ItemNo = a.ItemNo, Stage = a.Stage, Activity = a.Activity, TestType = a.TestType,
                    AcceptanceReference = a.AcceptanceReference, PointVendor = a.PointVendor, PointTpi = a.PointTpi, PointClient = a.PointClient,
                    DocumentProduced = a.DocumentProduced, RequiredReportType = a.RequiredReportType, Remark = a.Remark, GatesRelease = a.GatesRelease,
                    Status = a.Status, PlannedDate = a.PlannedDate, NotificationSentAt = a.NotificationSentAt,
                    WitnessDeadline = ItpNoticeRules.Deadline(a.NotificationSentAt, noticeDays), DoneAt = a.DoneAt, StatusNote = a.StatusNote,
                    NotificationLetterId = a.NotificationLetterId,
                    NotificationLetterNumber = a.NotificationLetterId.HasValue && letterNumbers.TryGetValue(a.NotificationLetterId.Value, out string letterNumber) ? letterNumber : null,
                    NotApplicableReference = a.NotApplicableReference, NotApplicablePending = a.NotApplicablePending, NotApplicableLetterId = a.NotApplicableLetterId,
                    NotApplicableLetterNumber = a.NotApplicableLetterId.HasValue && letterNumbers.TryGetValue(a.NotApplicableLetterId.Value, out string clientLetterNumber) ? clientLetterNumber : null,
                    EvidenceDocumentId = a.EvidenceDocumentId, EvidenceNote = a.EvidenceNote,
                    HoldPending = !terminal && HasPoint(a, ItpPoint.H),
                    Blockers = terminal ? new List<string>() : Evaluate(noticeDays, a, reports, today, waive),
                    Parameters = a.Parameters.OrderBy(p => p.Id).Select(p => ToDto(p.Id, p.Name, p.Unit, p.Min, p.Max, p.Source, p.RequiresLimits)).ToList(),
                    Releases = a.Releases.OrderBy(r => r.Id).Select(r => new ItpReleaseDto
                    {
                        Id = r.Id, Party = r.Party, Action = r.Action, PersonName = r.PersonName, Organisation = r.Organisation,
                        ReleaseDate = r.ReleaseDate, Reference = r.Reference, WaivedByNotice = r.WaivedByNotice,
                    }).ToList(),
                });
            }
            dto.Revisions = itp.Revisions.OrderBy(r => r.Revision).Select(r => new ItpRevisionDto
            {
                Id = r.Id, Revision = r.Revision, Reason = r.Reason, ClientApprovalReference = r.ClientApprovalReference,
                ClientApprovalDate = r.ClientApprovalDate, ChangedAt = r.ChangedAt, ChangedByName = names.Name(r.ChangedByUserId),
            }).ToList();
            return dto;
        }

        public async Task<ResponseDto<PagedList<ItpListDto>>> GetItpsAsync(ItpQuery query, CancellationToken cancellationToken)
        {
            query ??= new ItpQuery();
            IQueryable<Itp> rows = Itps.Where(i => !i.Deleted);
            if (query.Status.HasValue) rows = rows.Where(i => i.Status == query.Status.Value);
            if (query.WorkOrderId.HasValue) rows = rows.Where(i => i.WorkOrderId == query.WorkOrderId.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(i => i.Number.Contains(text) || i.WorkOrder.Code.Contains(text) || i.WorkOrder.Title.Contains(text));
            IQueryable<ItpListDto> projection = rows.OrderByDescending(i => i.Id).Select(i => new ItpListDto
            {
                Id = i.Id, Number = i.Number, WorkOrderId = i.WorkOrderId, WorkOrderCode = i.WorkOrder.Code, ContractCode = i.Contract.Code,
                Revision = i.Revision, Status = i.Status, AutoDrafted = i.AutoDrafted, ActivityCount = i.Activities.Count,
                DoneCount = i.Activities.Count(a => a.Status == ItpActivityStatus.Done || a.Status == ItpActivityStatus.Waived || a.Status == ItpActivityStatus.NotApplicable),
            });
            return ResponseDtoHelper.Ok(await PagedList<ItpListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<ItpDto>> GetItpAsync(int id, CancellationToken cancellationToken)
        {
            Itp itp = await Full().FirstOrDefaultAsync(i => i.Id == id && !i.Deleted, cancellationToken);
            return itp == null ? QualityText.NotFound<ItpDto>("برنامه بازرسی و آزمون پیدا نشد.") : ResponseDtoHelper.Ok(await BuildAsync(itp, cancellationToken));
        }

        public async Task<ResponseDto<ItpDto>> GetByWorkOrderAsync(int workOrderId, CancellationToken cancellationToken)
        {
            Itp itp = await Full().FirstOrDefaultAsync(i => i.WorkOrderId == workOrderId && !i.Deleted, cancellationToken);
            return itp == null ? QualityText.NotFound<ItpDto>("برای این دستور کار هنوز برنامه بازرسی و آزمون ساخته نشده است.") : ResponseDtoHelper.Ok(await BuildAsync(itp, cancellationToken));
        }

        // -------------------------------------------------------------------------------- drafting from a template

        /// <summary>Guesses the product type from the equipment title of the work order.</summary>
        public static ItpProductType DetectProductType(string title)
        {
            string t = (title ?? string.Empty).ToLowerInvariant();
            if (t.Contains("orc")) return ItpProductType.OrcUnit;
            if (t.Contains("condenser") || t.Contains("کندانسور") || t.Contains("چگالنده")) return ItpProductType.Condenser;
            if (t.Contains("lube") || t.Contains("روغن") || t.Contains("روانکاری")) return ItpProductType.LubeOilSystem;
            if (t.Contains("pump") || t.Contains("پمپ")) return ItpProductType.Pump;
            if (t.Contains("valve") || t.Contains("شیر")) return ItpProductType.Valve;
            return ItpProductType.SteamTurbine;
        }

        private async Task<ItpTemplate> PickTemplateAsync(int? templateId, ItpProductType type, CancellationToken cancellationToken)
        {
            IQueryable<ItpTemplate> q = Templates.Include(t => t.Activities).ThenInclude(a => a.Parameters).AsSplitQuery().Where(t => !t.Deleted && t.IsActive);
            if (templateId.HasValue) return await q.FirstOrDefaultAsync(t => t.Id == templateId.Value, cancellationToken);
            ItpTemplate template = await q.Where(t => t.ProductType == type).OrderBy(t => t.Id).FirstOrDefaultAsync(cancellationToken);
            // product types without their own template use the steam turbine plan (★ architect decision)
            return template ?? await q.Where(t => t.ProductType == ItpProductType.SteamTurbine).OrderBy(t => t.Id).FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<ResponseDto<Itp>> CreateItpAsync(int workOrderId, int? templateId, ItpProductType? productType, bool abroad, bool auto, CancellationToken cancellationToken)
        {
            await EnsureSeedAsync(cancellationToken);
            WorkOrder wo = await _uow.Set<WorkOrder>().FirstOrDefaultAsync(w => w.Id == workOrderId && !w.Deleted, cancellationToken);
            if (wo == null) return QualityText.NotFound<Itp>("دستور کار پیدا نشد.");
            ItpProductType type = productType ?? DetectProductType(
                await _uow.Set<Equipment>().Where(e => e.Id == wo.EquipmentId).Select(e => e.Title).FirstOrDefaultAsync(cancellationToken));
            ItpTemplate template = await PickTemplateAsync(templateId, type, cancellationToken);
            if (template == null) return Bad<Itp>("قالب برنامه بازرسی و آزمون پیدا نشد.");
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            var itp = new Itp
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Itp, "ITP", DateTime.Today, cancellationToken),
                WorkOrderId = wo.Id, ContractId = wo.ContractId, TemplateId = template.Id, Revision = 0, Status = ItpStatus.Draft,
                NoticeDays = ItpNoticeRules.EffectiveDays((int)settings[abroad ? QualitySettingKeys.NoticeDaysAbroad : QualitySettingKeys.NoticeDaysIran], (int)settings[QualitySettingKeys.MinNoticeDays]),
                AutoDrafted = auto, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
                Activities = new List<ItpActivity>(),
                Revisions = new List<ItpRevision>(),
            };
            foreach (ItpTemplateActivity ta in template.Activities.OrderBy(a => a.Seq))
            {
                itp.Activities.Add(new ItpActivity
                {
                    Seq = ta.Seq, ItemNo = ta.ItemNo, Stage = ta.Stage, Activity = ta.Activity, TestType = ta.TestType,
                    AcceptanceReference = ta.AcceptanceReference, PointVendor = ta.PointVendor, PointTpi = ta.PointTpi, PointClient = ta.PointClient,
                    DocumentProduced = ta.DocumentProduced, RequiredReportType = ta.RequiredReportType, Remark = ta.Remark, GatesRelease = ta.GatesRelease,
                    Status = ItpActivityStatus.Planned,
                    Parameters = ta.Parameters.OrderBy(p => p.Id).Select(p => new ItpActivityParameter
                    {
                        Name = p.Name, Unit = p.Unit, Min = p.Min, Max = p.Max, Source = p.Source, RequiresLimits = p.RequiresLimits,
                    }).ToList(),
                    Releases = new List<ItpPointRelease>(),
                });
            }
            Itps.Add(itp);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(itp);
        }

        public async Task<ResponseDto<ItpDto>> EnsureItpAsync(EnsureItpDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || dto.WorkOrderId <= 0) return Bad<ItpDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Planners)) return Bad<ItpDto>(QualityText.NotAllowed);
            Itp existing = await Itps.AsNoTracking().FirstOrDefaultAsync(i => i.WorkOrderId == dto.WorkOrderId && !i.Deleted, cancellationToken);
            if (existing != null) return await GetItpAsync(existing.Id, cancellationToken);
            ResponseDto<Itp> created = await CreateItpAsync(dto.WorkOrderId, dto.TemplateId, dto.ProductType, dto.Abroad, false, cancellationToken);
            return created.Success ? await GetItpAsync(created.Data.Id, cancellationToken) : Bad<ItpDto>(created.Message);
        }

        public async Task<int?> EnsureItpForWorkOrderAsync(int workOrderId, CancellationToken cancellationToken)
        {
            Itp existing = await Itps.AsNoTracking().FirstOrDefaultAsync(i => i.WorkOrderId == workOrderId && !i.Deleted, cancellationToken);
            if (existing != null) return existing.Id;
            ResponseDto<Itp> created = await CreateItpAsync(workOrderId, null, null, false, true, cancellationToken);
            return created.Success ? created.Data.Id : (int?)null;
        }

        // -------------------------------------------------------------------------------- editing

        private async Task<Itp> LoadForEditAsync(int id, CancellationToken cancellationToken) =>
            await Itps.Include(i => i.Activities).ThenInclude(a => a.Parameters)
                .Include(i => i.Activities).ThenInclude(a => a.Releases)
                .Include(i => i.Revisions).AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id && !i.Deleted, cancellationToken);

        private async Task<ResponseDto<ItpDto>> SaveAsync(Itp itp, CancellationToken cancellationToken)
        {
            itp.ModifyDate = DateTimeOffset.Now; itp.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<ItpDto>(QualityText.Concurrency);
            }
            return await GetItpAsync(itp.Id, cancellationToken);
        }

        public async Task<ResponseDto<ItpDto>> SaveItpAsync(SaveItpDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<ItpDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Planners)) return Bad<ItpDto>(QualityText.NotAllowed);
            Itp itp = await LoadForEditAsync(dto.Id, cancellationToken);
            if (itp == null) return QualityText.NotFound<ItpDto>("برنامه بازرسی و آزمون پیدا نشد.");
            if (itp.Status == ItpStatus.Closed) return Bad<ItpDto>("برنامه بسته‌شده قابل ویرایش نیست.");
            string noticeProblem = ItpNoticeRules.CheckNoticeDays(dto.NoticeDays, await MinNoticeDaysAsync(cancellationToken));
            if (noticeProblem != null) return Bad<ItpDto>(noticeProblem);
            // R1q D9: the notice period is part of what the client approved; shortening it afterwards would let the vendor waive the client's witness sooner.
            // Changing it needs a revision of the plan (revise), which the client approves again.
            if ((itp.Status == ItpStatus.ClientApproved || itp.Status == ItpStatus.InUse) && dto.NoticeDays != itp.NoticeDays)
                return Bad<ItpDto>("مهلت اعلام نقطه شاهد پس از تایید کارفرما قفل است؛ برای تغییر آن برنامه را با دلیل بازنگری کنید تا کارفرما دوباره تایید کند.");
            itp.NoticeDays = dto.NoticeDays;
            itp.Note = Clean(dto.Note);
            MarkEditor(itp);
            return await SaveAsync(itp, cancellationToken);
        }

        /// <summary>R1q H6: the user changed or submitted the plan, so he cannot be the one who records the client's approval of it.</summary>
        private void MarkEditor(Itp itp) => itp.EditorUserIds = global::Services.NasbNiro.Finance.LedgerMath.WithEditor(itp.EditorUserIds, Me);

        private string Snapshot(Itp itp) =>
            JsonSerializer.Serialize(itp.Activities.OrderBy(a => a.Seq).Select(a => new
            {
                a.Seq, a.ItemNo, a.Stage, a.Activity, TestType = (int)a.TestType, a.AcceptanceReference,
                PointVendor = (int)a.PointVendor, PointTpi = (int)a.PointTpi, PointClient = (int)a.PointClient,
                a.DocumentProduced, RequiredReportType = (int?)a.RequiredReportType, a.GatesRelease, Status = (int)a.Status,
                Parameters = a.Parameters.Select(p => new { p.Name, p.Unit, p.Min, p.Max, p.Source }).ToList(),
            }).ToList());

        /// <summary>Edits of an approved plan keep the old state as a revision snapshot; the plan must be approved again.</summary>
        private ResponseDto<ItpDto> OpenRevision(Itp itp, string reason)
        {
            if (itp.Status != ItpStatus.ClientApproved && itp.Status != ItpStatus.InUse) return null;
            reason = Clean(reason);
            if (reason == null) return Bad<ItpDto>("برنامه تاییدشده با ثبت دلیل تغییر و ایجاد بازنگری جدید ویرایش می‌شود؛ دلیل را بنویسید.");
            itp.Revisions.Add(new ItpRevision
            {
                Revision = itp.Revision, Reason = reason, ClientApprovalReference = itp.ClientApprovalReference,
                ClientApprovalDate = itp.ClientApprovalDate, Snapshot = Snapshot(itp), ChangedAt = DateTimeOffset.Now, ChangedByUserId = Me,
            });
            itp.Revision++;
            itp.Status = ItpStatus.Draft;
            itp.ClientApprovalReference = null;
            itp.ClientApprovalDate = null;
            MarkEditor(itp);
            return null;
        }

        public async Task<ResponseDto<ItpDto>> ItpActionAsync(ItpActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<ItpDto>(QualityText.InvalidData);
            Itp itp = await LoadForEditAsync(dto.Id, cancellationToken);
            if (itp == null) return QualityText.NotFound<ItpDto>("برنامه بازرسی و آزمون پیدا نشد.");
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (!_user.IsInRole(QualityRoles.Planners)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (itp.Status != ItpStatus.Draft) return Bad<ItpDto>("فقط برنامه پیش‌نویس برای تایید کارفرما ارسال می‌شود.");
                    if (itp.Activities.Count == 0) return Bad<ItpDto>("برنامه بدون فعالیت ارسال نمی‌شود.");
                    itp.Status = ItpStatus.Submitted; itp.SubmittedAt = DateTimeOffset.Now; itp.SubmittedByUserId = Me;
                    MarkEditor(itp);
                    break;
                case "client-approve":
                    if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (itp.Status != ItpStatus.Submitted) return Bad<ItpDto>("ابتدا برنامه را برای کارفرما ارسال کنید.");
                    if (Clean(dto.Reference) == null) return Bad<ItpDto>("شماره نامه یا مرجع تایید کارفرما را وارد کنید.");
                    if (Clean(dto.Reference).Length > ItpNoticeRules.MaxReferenceLength) return Bad<ItpDto>($"مرجع تایید کارفرما حداکثر {ItpNoticeRules.MaxReferenceLength} نویسه است.");
                    // R1q H6: the plan is approved by somebody who did not prepare, change, revise or submit it (a vendor cannot approve for the client alone)
                    if (itp.SubmittedByUserId == Me || global::Services.NasbNiro.Finance.LedgerMath.Touched(itp.AutoDrafted ? 0 : itp.CreatorUserId, itp.EditorUserIds, Me))
                        return Bad<ItpDto>("تایید کارفرما را باید شخصی ثبت کند که برنامه را تهیه، ویرایش، بازنگری یا ارسال نکرده است (تفکیک وظایف).");
                    DateTime approvalDate = (dto.Date ?? DateTime.Today).Date;
                    string approvalProblem = ItpNoticeRules.CheckApprovalDate(approvalDate, DateTime.Today, (int)(await QualitySettingKeys.LoadAsync(_uow, cancellationToken))[QualitySettingKeys.MaxBackdateDays]);
                    if (approvalProblem != null) return Bad<ItpDto>(approvalProblem);
                    itp.Status = ItpStatus.ClientApproved; itp.ClientApprovalReference = Clean(dto.Reference);
                    itp.ClientApprovalDate = approvalDate;
                    // R1q H6: the client approved the revised plan, so the control points it agreed to drop are not applicable from now on
                    DateTimeOffset approvedAt = DateTimeOffset.Now;
                    foreach (ItpActivity pending in itp.Activities.Where(a => a.NotApplicablePending && !IsTerminal(a.Status)))
                    {
                        pending.NotApplicablePending = false; pending.Status = ItpActivityStatus.NotApplicable;
                        pending.DoneAt = approvedAt; pending.DoneByUserId = Me;
                    }
                    itp.EditorUserIds = null; // the next revision starts with its own makers
                    break;
                case "use":
                    if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (itp.Status != ItpStatus.ClientApproved) return Bad<ItpDto>("فقط برنامه تاییدشده در حال اجرا قرار می‌گیرد.");
                    itp.Status = ItpStatus.InUse;
                    break;
                case "close":
                    if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (itp.Status != ItpStatus.ClientApproved && itp.Status != ItpStatus.InUse) return Bad<ItpDto>("فقط برنامه تاییدشده بسته می‌شود.");
                    if (itp.Activities.Any(a => !IsTerminal(a.Status))) return Bad<ItpDto>("همه فعالیت‌ها باید انجام‌شده، معاف یا غیرقابل‌اجرا باشند.");
                    itp.Status = ItpStatus.Closed;
                    break;
                case "revise":
                    if (!_user.IsInRole(QualityRoles.Planners)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (itp.Status != ItpStatus.ClientApproved && itp.Status != ItpStatus.InUse) return Bad<ItpDto>("بازنگری فقط برای برنامه تاییدشده ثبت می‌شود.");
                    ResponseDto<ItpDto> refused = OpenRevision(itp, dto.Reason);
                    if (refused != null) return refused;
                    break;
                default:
                    return Bad<ItpDto>("عملیات نامعتبر است.");
            }
            return await SaveAsync(itp, cancellationToken);
        }

        public async Task<ResponseDto<ItpDto>> SaveActivityAsync(SaveItpActivityDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Activity) == null) return Bad<ItpDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Planners)) return Bad<ItpDto>(QualityText.NotAllowed);
            Itp itp = await LoadForEditAsync(dto.ItpId, cancellationToken);
            if (itp == null) return QualityText.NotFound<ItpDto>("برنامه بازرسی و آزمون پیدا نشد.");
            if (itp.Status == ItpStatus.Closed) return Bad<ItpDto>("برنامه بسته‌شده قابل ویرایش نیست.");
            ItpActivity activity = dto.Id.HasValue ? itp.Activities.FirstOrDefault(a => a.Id == dto.Id.Value) : null;
            if (dto.Id.HasValue && activity == null) return QualityText.NotFound<ItpDto>("فعالیت پیدا نشد.");
            if (activity != null && IsTerminal(activity.Status)) return Bad<ItpDto>("فعالیت انجام‌شده یا بسته‌شده قابل ویرایش نیست.");
            foreach (SaveItpParameterDto p in dto.Parameters ?? new List<SaveItpParameterDto>())
            {
                if (Clean(p.Name) == null) return Bad<ItpDto>("نام پارامتر را وارد کنید.");
                if (p.Min.HasValue && p.Max.HasValue && p.Min.Value > p.Max.Value) return Bad<ItpDto>($"حد پایین پارامتر «{p.Name}» از حد بالا بیشتر است.");
            }
            ResponseDto<ItpDto> refused = OpenRevision(itp, dto.Reason);
            if (refused != null) return refused;
            if (activity == null)
            {
                activity = new ItpActivity { Status = ItpActivityStatus.Planned, Parameters = new List<ItpActivityParameter>(), Releases = new List<ItpPointRelease>() };
                itp.Activities.Add(activity);
            }
            activity.Seq = dto.Seq > 0 ? dto.Seq : itp.Activities.Count;
            activity.ItemNo = Clean(dto.ItemNo);
            activity.Stage = Clean(dto.Stage);
            activity.Activity = Clean(dto.Activity);
            activity.TestType = dto.TestType;
            activity.AcceptanceReference = Clean(dto.AcceptanceReference);
            activity.PointVendor = dto.PointVendor;
            activity.PointTpi = dto.PointTpi;
            activity.PointClient = dto.PointClient;
            activity.DocumentProduced = Clean(dto.DocumentProduced);
            activity.RequiredReportType = dto.RequiredReportType;
            activity.Remark = Clean(dto.Remark);
            activity.GatesRelease = dto.GatesRelease;
            activity.PlannedDate = dto.PlannedDate?.Date;
            activity.Parameters.Clear();
            foreach (SaveItpParameterDto p in dto.Parameters ?? new List<SaveItpParameterDto>())
                activity.Parameters.Add(new ItpActivityParameter
                {
                    Name = Clean(p.Name), Unit = Clean(p.Unit), Min = p.Min, Max = p.Max, Source = Clean(p.Source),
                    RequiresLimits = p.RequiresLimits && !(p.Min.HasValue || p.Max.HasValue),
                });
            MarkEditor(itp);
            return await SaveAsync(itp, cancellationToken);
        }

        public async Task<ResponseDto<ItpDto>> SetLimitsAsync(SetItpLimitsDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<ItpDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Engineering + ",PMO")) return Bad<ItpDto>(QualityText.NotAllowed + " (حدود پذیرش را مهندسی تعیین می‌کند)");
            if (!dto.Min.HasValue && !dto.Max.HasValue) return Bad<ItpDto>("حداقل یکی از حد پایین یا حد بالا را وارد کنید.");
            if (dto.Min.HasValue && dto.Max.HasValue && dto.Min.Value > dto.Max.Value) return Bad<ItpDto>("حد پایین از حد بالا بیشتر است.");
            ItpActivityParameter p = await _uow.Set<ItpActivityParameter>().Include(x => x.Activity).FirstOrDefaultAsync(x => x.Id == dto.ParameterId, cancellationToken);
            if (p == null) return QualityText.NotFound<ItpDto>("پارامتر پیدا نشد.");
            if (IsTerminal(p.Activity.Status)) return Bad<ItpDto>("فعالیت انجام‌شده قابل ویرایش نیست.");
            // reports that were drafted before the limits existed keep their own copy: the limits apply to new reports
            p.Min = dto.Min; p.Max = dto.Max; p.RequiresLimits = false;
            p.Source = Clean(dto.Source) ?? p.Source;
            Itp itp = await LoadForEditAsync(p.Activity.ItpId, cancellationToken);
            MarkEditor(itp);
            return await SaveAsync(itp, cancellationToken);
        }

        public async Task<ResponseDto<ItpDto>> ActivityActionAsync(ItpActivityActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<ItpDto>(QualityText.InvalidData);
            ItpActivity activity = await Activities.Include(a => a.Releases).FirstOrDefaultAsync(a => a.Id == dto.Id, cancellationToken);
            if (activity == null) return QualityText.NotFound<ItpDto>("فعالیت پیدا نشد.");
            Itp itp = await LoadForEditAsync(activity.ItpId, cancellationToken);
            activity = itp.Activities.First(a => a.Id == dto.Id);
            if (itp.Status == ItpStatus.Closed) return Bad<ItpDto>("برنامه بسته شده است.");
            DateTime date = (dto.Date ?? DateTime.Today).Date;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "notify":
                    if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (IsTerminal(activity.Status)) return Bad<ItpDto>("فعالیت پایان یافته است.");
                    // R1q D9, review H5: the notice date is the server day, or the day the outgoing letter that carried the notice was SENT to the client; never a typed past date
                    string dateProblem = ItpNoticeRules.CheckTypedDate(dto.Date, DateTime.Today);
                    if (dateProblem != null) return Bad<ItpDto>(dateProblem);
                    (DateTime? noticeDate, string letterProblem) = await NotificationDateAsync(itp, dto.LetterId, cancellationToken);
                    if (letterProblem != null) return Bad<ItpDto>(letterProblem);
                    activity.NotificationSentAt = noticeDate;
                    activity.NotificationLetterId = dto.LetterId;
                    activity.Status = ItpActivityStatus.Notified;
                    activity.StatusNote = Clean(dto.Note) ?? activity.StatusNote;
                    break;
                case "plan":
                    if (!_user.IsInRole(QualityRoles.Planners)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (IsTerminal(activity.Status)) return Bad<ItpDto>("فعالیت پایان یافته است.");
                    activity.PlannedDate = date;
                    break;
                case "done":
                    if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<ItpDto>(QualityText.NotAllowed + " (فقط بازرس QC فعالیت را انجام‌شده ثبت می‌کند)");
                    // R1q D17 ★: as for an inspection record, the QC inspector who signs an activity off holds a valid inspector qualification once the register is in use
                    string competence = await QualityChecks.InspectorAsync(_uow, Me, DateTime.Today, "ثبت انجام‌شدن فعالیت برنامه بازرسی بدون گواهی صلاحیت معتبر مجاز نیست.", cancellationToken);
                    if (competence != null) return Bad<ItpDto>(competence);
                    ResponseDto<ItpDto> refused = await MarkDoneAsync(itp, activity, dto.EvidenceDocumentId, Clean(dto.Note), cancellationToken);
                    if (refused != null) return refused;
                    activity.StatusNote = Clean(dto.Note) ?? activity.StatusNote;
                    break;
                case "not-applicable":
                    if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (IsTerminal(activity.Status)) return Bad<ItpDto>("فعالیت پایان یافته است.");
                    if (Clean(dto.Note) == null) return Bad<ItpDto>("دلیل غیرقابل‌اجرا بودن فعالیت را بنویسید.");
                    if (Clean(dto.Reference) != null && Clean(dto.Reference).Length > ItpNoticeRules.MaxReferenceLength)
                        return Bad<ItpDto>($"مرجع نامه کارفرما حداکثر {ItpNoticeRules.MaxReferenceLength} نویسه است.");
                    if (ItpNoticeRules.ThirdPartyControlled(activity.PointTpi, activity.PointClient))
                    {
                        // R1q D9, review H6: a hold or witness point of the client or the third party is removed only when the client's own letter (an incoming letter of the
                        // same contract, registered in the Office) agrees, and the activity stays open (pending) until the client approves the revised plan; the vendor cannot drop it alone
                        if (activity.NotApplicablePending) return Bad<ItpDto>("برای این فعالیت پیش‌تر درخواست غیرقابل‌اجرا شدن ثبت شده و منتظر تایید برنامه بازنگری‌شده توسط کارفرماست.");
                        if (!dto.LetterId.HasValue)
                            return Bad<ItpDto>("نقطه توقف یا شاهد کارفرما/بازرس شخص ثالث را فقط با نامه موافقت کارفرما می‌توان غیرقابل‌اجرا کرد؛ نامه وارده کارفرما را از دبیرخانه انتخاب کنید (شناسه نامه).");
                        (OfLetter clientLetter, string clientLetterProblem) = await ClientLetterAsync(itp, dto.LetterId.Value, cancellationToken);
                        if (clientLetterProblem != null) return Bad<ItpDto>(clientLetterProblem);
                        string letterReference = Clean(dto.Reference) ?? Clean(clientLetter.IncomingNumber) ?? clientLetter.Number;
                        ResponseDto<ItpDto> revision = OpenRevision(itp, $"غیرقابل‌اجرا شدن فعالیت {activity.ItemNo ?? activity.Seq.ToString()}: {Clean(dto.Note)} (نامه کارفرما: {letterReference})");
                        if (revision != null) return revision;
                        if (itp.Status == ItpStatus.Submitted) itp.Status = ItpStatus.Draft; // the client must see the plan with this change
                        activity.NotApplicablePending = true; activity.NotApplicableLetterId = clientLetter.Id;
                        activity.NotApplicableReference = letterReference.Length > ItpNoticeRules.MaxReferenceLength ? letterReference.Substring(0, ItpNoticeRules.MaxReferenceLength) : letterReference;
                        activity.StatusNote = Clean(dto.Note);
                        MarkEditor(itp);
                        break;
                    }
                    activity.Status = ItpActivityStatus.NotApplicable; activity.StatusNote = Clean(dto.Note);
                    activity.DoneAt = DateTimeOffset.Now; activity.DoneByUserId = Me;
                    break;
                default:
                    return Bad<ItpDto>("عملیات نامعتبر است.");
            }
            ResponseDto<ItpDto> savedActivity = await SaveAsync(itp, cancellationToken);
            if (savedActivity.Success) await SyncHoldPointAsync(itp, activity, cancellationToken);
            return savedActivity;
        }

        /// <summary>Office inbox: an activity with a hold point (H) that was notified waits for the QC inspector until it is done, waived or not applicable.</summary>
        private Task SyncHoldPointAsync(Itp itp, ItpActivity activity, CancellationToken cancellationToken) =>
            global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "itp-h:" + activity.Id, activity.Status == ItpActivityStatus.Notified && HasPoint(activity, ItpPoint.H), "quality", "itp-hold-point", activity.Id,
                "نقطه توقف (H) آماده بازرسی: " + (activity.Activity ?? activity.ItemNo), "/quality/itp/" + itp.Id, QualityRoles.Inspectors, cancellationToken, activity.PlannedDate);

        /// <summary>
        /// The date a witness notice counts from (R1q D9, review H5): the server day, or the day the quoted outgoing letter was SENT. The letter must be an outgoing letter of this system
        /// (not an archive import) of the contract of the plan, addressed to the client of that contract, in status Sent with its sent date; that date may not be in the future, before
        /// the client approved the plan, or further back than the back-date window.
        /// </summary>
        private async Task<(DateTime? Date, string Problem)> NotificationDateAsync(Itp itp, int? letterId, CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            if (!letterId.HasValue) return (today, null);
            OfLetter letter = await _uow.Set<OfLetter>().AsNoTracking().Include(l => l.Recipients).FirstOrDefaultAsync(l => l.Id == letterId.Value && !l.Deleted, cancellationToken);
            if (letter == null) return (null, "نامه انتخاب‌شده پیدا نشد.");
            if (letter.Kind != OfLetterKind.Outgoing || letter.Imported)
                return (null, "فقط نامه صادره ثبت‌شده در این سامانه به‌عنوان مرجع اعلام پذیرفته می‌شود (نه نامه وارده، داخلی یا بایگانی وارداتی).");
            if (letter.Status != OfLetterStatus.Sent || !letter.SentDate.HasValue)
                return (null, "نامه انتخاب‌شده هنوز ارسال نشده است؛ فقط نامه صادره‌ای که دبیرخانه آن را «ارسال‌شده» ثبت کرده مرجع اعلام است (نه امضاشده یا ثبت‌شده).");
            if (letter.ContractId != itp.ContractId)
                return (null, "نامه انتخاب‌شده به همین پروژه/قرارداد وصل نیست؛ نامه اعلام باید در دبیرخانه به قرارداد این برنامه متصل شده باشد.");
            if (!await HasClientPartyAsync(itp.ContractId, letter.Recipients.Where(r => r.PartyId.HasValue).Select(r => r.PartyId.Value), cancellationToken))
                return (null, "گیرنده نامه انتخاب‌شده کارفرمای این قرارداد نیست؛ اعلام آزمون باید برای کارفرما (طرف حساب بیرونی قرارداد) ارسال شده باشد.");
            int window = (int)(await QualitySettingKeys.LoadAsync(_uow, cancellationToken))[QualitySettingKeys.MaxBackdateDays];
            string dateProblem = ItpNoticeRules.CheckLetterDate(letter.SentDate.Value, today, LastApprovalDate(itp), window);
            return dateProblem != null ? (null, dateProblem) : (letter.SentDate.Value.Date, null);
        }

        /// <summary>The date the client last approved the plan (the current approval, or the one a revision in progress replaced).</summary>
        private static DateTime? LastApprovalDate(Itp itp) =>
            itp.ClientApprovalDate ?? itp.Revisions?.OrderByDescending(r => r.Revision).Select(r => r.ClientApprovalDate).FirstOrDefault(d => d.HasValue);

        /// <summary>True when one of the Office parties is the client of the contract (ClientPartyRules).</summary>
        private async Task<bool> HasClientPartyAsync(int contractId, IEnumerable<int> partyIds, CancellationToken cancellationToken)
        {
            List<int> ids = partyIds.Distinct().ToList();
            if (ids.Count == 0) return false;
            List<int> linked = await _uow.Set<SlCustomerLink>().Where(l => !l.Deleted && l.ContractId == contractId).Select(l => l.CustomerId).ToListAsync(cancellationToken);
            var parties = await _uow.Set<OfExternalParty>().Where(p => !p.Deleted && ids.Contains(p.Id)).Select(p => new { p.Kind, p.CustomerId }).ToListAsync(cancellationToken);
            return parties.Any(p => ClientPartyRules.IsClient(p.Kind == OfPartyKind.Supplier, p.Kind == OfPartyKind.Customer, p.CustomerId, linked));
        }

        /// <summary>
        /// The client's letter that agrees a control point is not applicable (R1q review H6): a registered incoming letter of this system, of the contract of the plan,
        /// written by the client, dated after the plan was last approved and not in the future.
        /// </summary>
        private async Task<(OfLetter Letter, string Problem)> ClientLetterAsync(Itp itp, int letterId, CancellationToken cancellationToken)
        {
            OfLetter letter = await _uow.Set<OfLetter>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == letterId && !l.Deleted, cancellationToken);
            if (letter == null) return (null, "نامه انتخاب‌شده پیدا نشد.");
            if (letter.Kind != OfLetterKind.Incoming || (letter.Status != OfLetterStatus.Received && letter.Status != OfLetterStatus.Archived))
                return (null, "موافقت کارفرما را فقط نامه وارده ثبت‌شده در دبیرخانه اثبات می‌کند (نه نامه صادره، داخلی، پیش‌نویس یا باطل‌شده).");
            if (letter.ContractId != itp.ContractId)
                return (null, "نامه انتخاب‌شده به همین پروژه/قرارداد وصل نیست؛ نامه کارفرما باید در دبیرخانه به قرارداد این برنامه متصل شده باشد.");
            if (!letter.SenderPartyId.HasValue || !await HasClientPartyAsync(itp.ContractId, new[] { letter.SenderPartyId.Value }, cancellationToken))
                return (null, "فرستنده نامه انتخاب‌شده کارفرمای این قرارداد نیست.");
            string dateProblem = ItpNoticeRules.CheckClientLetterDate((letter.IncomingDate ?? letter.Date).Date, DateTime.Today, LastApprovalDate(itp));
            return dateProblem != null ? (null, dateProblem) : (letter, null);
        }

        /// <summary>Applies Done to the activity (adds the waived-by-notice rows). Returns an error response, or null when done.</summary>
        private async Task<ResponseDto<ItpDto>> MarkDoneAsync(Itp itp, ItpActivity activity, Guid? evidenceDocumentId, string evidenceNote, CancellationToken cancellationToken)
        {
            if (IsTerminal(activity.Status)) return Bad<ItpDto>("فعالیت قبلاً پایان یافته است.");
            if ((itp.Status == ItpStatus.Draft || itp.Status == ItpStatus.Submitted) && (HasPoint(activity, ItpPoint.H) || HasPoint(activity, ItpPoint.W)))
                    return Bad<ItpDto>("برنامه هنوز توسط کارفرما تایید نشده است؛ فعالیت دارای نقطه کنترل پیش از تایید برنامه انجام‌شده ثبت نمی‌شود.");
            List<ReportInfo> reports = await ReportsAsync(new[] { activity.Id }, cancellationToken);
            var waive = new List<ReleaseParty>();
            List<string> blockers = Evaluate(ItpNoticeRules.EffectiveDays(itp.NoticeDays, await MinNoticeDaysAsync(cancellationToken)), activity, reports, DateTime.Today, waive);
            if (blockers.Count > 0) return Bad<ItpDto>(string.Join(" ", blockers));
            // R1q D18 ★: an activity that no test report completes needs proof it was done: an accepted inspection record on it, or a document
            if (!activity.RequiredReportType.HasValue)
            {
                // a document counts only when the file really is in the document store (a made-up id is not proof), was uploaded lately by a quality or engineering user
                // and proves no other activity (review M16)
                if (evidenceDocumentId.HasValue)
                {
                    string evidenceProblem = await EvidenceProblemAsync(activity.Id, evidenceDocumentId.Value, cancellationToken);
                    if (evidenceProblem != null) return Bad<ItpDto>(evidenceProblem);
                }
                bool accepted = await _uow.Set<QcRecord>().AnyAsync(r => !r.Deleted && r.ItpActivityId == activity.Id && r.Result == QcResult.Accept, cancellationToken);
                if (!ItpNoticeRules.HasEvidence(accepted, evidenceDocumentId))
                    return Bad<ItpDto>("برای انجام‌شده ثبت‌کردن این فعالیت مدرک لازم است: یک بازرسی قبول‌شده روی همین فعالیت ثبت کنید یا مدرک انجام کار (گواهی، صورت‌جلسه یا سند بازبینی) را ضمیمه کنید.");
                activity.EvidenceDocumentId = evidenceDocumentId;
                activity.EvidenceNote = evidenceNote;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (ReleaseParty party in waive)
                activity.Releases.Add(new ItpPointRelease
                {
                    Party = party, Action = ReleaseAction.Waived, PersonName = null, Organisation = PartyName(party), ReleaseDate = DateTime.Today,
                    Reference = "witness waived by notice", WaivedByNotice = true, CreatedAt = now, CreatedByUserId = Me,
                });
            activity.Status = ItpActivityStatus.Done; activity.DoneAt = now; activity.DoneByUserId = Me;
            activity.NotApplicablePending = false; // done in the meantime: the request to drop the point is void
            if (itp.Status == ItpStatus.ClientApproved) itp.Status = ItpStatus.InUse;
            return null;
        }

        /// <summary>Why the file cannot prove an activity was done (null when it can): see <see cref="EvidenceDocumentRules"/>.</summary>
        private async Task<string> EvidenceProblemAsync(int activityId, Guid documentId, CancellationToken cancellationToken)
        {
            var file = await _uow.Set<Entities.Model.Document>().AsNoTracking().Where(d => d.Id == documentId).Select(d => new { d.RegisterDate, d.UploadedByUserId }).FirstOrDefaultAsync(cancellationToken);
            if (file == null) return "فایل مدرک در مخزن اسناد پیدا نشد؛ ابتدا فایل را بارگذاری کنید.";
            bool qualityUploader = false;
            if (file.UploadedByUserId.HasValue)
            {
                List<string> roles = await _uow.Set<UserRole>().Where(ur => ur.UserId == file.UploadedByUserId.Value).Select(ur => ur.Role.Name).ToListAsync(cancellationToken);
                string[] allowed = QualityRoles.EvidenceUploaders.Split(',');
                qualityUploader = roles.Any(r => allowed.Contains(r, StringComparer.OrdinalIgnoreCase));
            }
            bool usedElsewhere = await Activities.AnyAsync(a => a.EvidenceDocumentId == documentId && a.Id != activityId, cancellationToken);
            int window = (int)(await QualitySettingKeys.LoadAsync(_uow, cancellationToken))[QualitySettingKeys.MaxBackdateDays];
            return EvidenceDocumentRules.Check(file.RegisterDate, qualityUploader, usedElsewhere, DateTime.Today, window);
        }

        public async Task<ResponseDto<ItpDto>> AddReleaseAsync(SaveItpReleaseDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<ItpDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<ItpDto>(QualityText.NotAllowed + " (رهاسازی نقطه کنترل را بازرس QC ثبت می‌کند)");
            if (!Enum.IsDefined(typeof(ReleaseParty), dto.Party) || !Enum.IsDefined(typeof(ReleaseAction), dto.Action)) return Bad<ItpDto>(QualityText.InvalidData);
            ItpActivity activity = await Activities.FirstOrDefaultAsync(a => a.Id == dto.ActivityId, cancellationToken);
            if (activity == null) return QualityText.NotFound<ItpDto>("فعالیت پیدا نشد.");
            Itp itp = await LoadForEditAsync(activity.ItpId, cancellationToken);
            activity = itp.Activities.First(a => a.Id == dto.ActivityId);
            if (IsTerminal(activity.Status)) return Bad<ItpDto>("فعالیت پایان یافته است.");
            if (PointOf(activity, dto.Party) == ItpPoint.None) return Bad<ItpDto>($"برای {PartyName(dto.Party)} نقطه کنترلی روی این فعالیت تعریف نشده است.");
            if (Clean(dto.PersonName) == null) return Bad<ItpDto>("نام شخص رهاکننده را وارد کنید.");
            if (dto.Action == ReleaseAction.Waived)
            {
                if (PointOf(activity, dto.Party) == ItpPoint.H) return Bad<ItpDto>("نقطه توقف (H) معاف‌شدنی نیست؛ رهاسازی واقعی لازم است.");
                if (Clean(dto.Reference) == null) return Bad<ItpDto>("مرجع (نامه) معافیت را وارد کنید.");
            }
            activity.Releases.Add(new ItpPointRelease
            {
                Party = dto.Party, Action = dto.Action, PersonName = Clean(dto.PersonName), Organisation = Clean(dto.Organisation),
                ReleaseDate = (dto.ReleaseDate ?? DateTime.Today).Date, Reference = Clean(dto.Reference), WaivedByNotice = false,
                CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me,
            });
            ResponseDto<ItpDto> saved = await SaveAsync(itp, cancellationToken);
            if (saved.Success && activity.RequiredReportType.HasValue) await TryCompleteActivityAsync(activity.Id, cancellationToken);
            if (saved.Success) await SyncHoldPointAsync(itp, activity, cancellationToken);
            return saved.Success ? await GetItpAsync(itp.Id, cancellationToken) : saved;
        }

        public async Task<bool> TryCompleteActivityAsync(int activityId, CancellationToken cancellationToken)
        {
            ItpActivity probe = await Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == activityId, cancellationToken);
            if (probe == null) return false;
            Itp itp = await LoadForEditAsync(probe.ItpId, cancellationToken);
            ItpActivity activity = itp.Activities.First(a => a.Id == activityId);
            if (IsTerminal(activity.Status) || !activity.RequiredReportType.HasValue) return false;
            if (itp.Status != ItpStatus.ClientApproved && itp.Status != ItpStatus.InUse) return false;
            ResponseDto<ItpDto> refused = await MarkDoneAsync(itp, activity, null, null, cancellationToken);
            if (refused != null) return false;
            itp.ModifyDate = DateTimeOffset.Now; itp.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
            return true;
        }

        // -------------------------------------------------------------------------------- pending points

        public async Task<ResponseDto<PagedList<PendingPointDto>>> GetPendingPointsAsync(PendingPointQuery query, CancellationToken cancellationToken)
        {
            query ??= new PendingPointQuery();
            IQueryable<ItpActivity> rows = Activities.Where(a => !a.Itp.Deleted && a.Itp.Status != ItpStatus.Closed &&
                (a.Status == ItpActivityStatus.Planned || a.Status == ItpActivityStatus.Notified) &&
                (a.PointVendor == ItpPoint.H || a.PointTpi == ItpPoint.H || a.PointClient == ItpPoint.H ||
                 a.PointVendor == ItpPoint.W || a.PointTpi == ItpPoint.W || a.PointClient == ItpPoint.W));
            if (query.WorkOrderId.HasValue) rows = rows.Where(a => a.Itp.WorkOrderId == query.WorkOrderId.Value);
            int minNotice = await MinNoticeDaysAsync(cancellationToken);
            var raw = await rows.OrderBy(a => a.ItpId).ThenBy(a => a.Seq).Select(a => new
            {
                a.ItpId, ItpNumber = a.Itp.Number, a.Itp.WorkOrderId, WorkOrderCode = a.Itp.WorkOrder.Code, a.Itp.NoticeDays,
                a.Id, a.ItemNo, a.Activity, a.Status, a.PlannedDate, a.NotificationSentAt, a.PointVendor, a.PointTpi, a.PointClient,
            }).ToListAsync(cancellationToken);
            List<PendingPointDto> list = raw.Select(a => new PendingPointDto
            {
                ItpId = a.ItpId, ItpNumber = a.ItpNumber, WorkOrderId = a.WorkOrderId, WorkOrderCode = a.WorkOrderCode, ActivityId = a.Id,
                ItemNo = a.ItemNo, Activity = a.Activity, Status = a.Status, PlannedDate = a.PlannedDate,
                Points = $"V:{a.PointVendor} T:{a.PointTpi} C:{a.PointClient}",
                WitnessDeadline = ItpNoticeRules.Deadline(a.NotificationSentAt, ItpNoticeRules.EffectiveDays(a.NoticeDays, minNotice)),
            }).ToList();
            return ResponseDtoHelper.Ok(await PagedList<PendingPointDto>.CreateAsync(list, query.PageNumber, query.PageSize, cancellationToken));
        }
    }
}
