using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Quality;
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

        public ItpService(IUnitOfWork uow, IInventoryUserContext user)
        {
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
        private static List<string> Evaluate(Itp itp, ItpActivity a, List<ReportInfo> reports, DateTime today, List<ReleaseParty> waiveParties)
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
                    DateTime? deadline = a.NotificationSentAt?.Date.AddDays(itp.NoticeDays);
                    if (deadline.HasValue && deadline.Value <= today) waiveParties.Add(party);
                    else if (!a.NotificationSentAt.HasValue) blockers.Add($"نقطه شاهد (W): اعلام زمان آزمون به {PartyName(party)} ثبت نشده است.");
                    else blockers.Add($"نقطه شاهد (W): مهلت اعلام {PartyName(party)} تا {Services.NasbNiro.Finance.FiscalCalendars.Text(deadline.Value)} است.");
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

        private async Task<ItpDto> BuildAsync(Itp itp, CancellationToken cancellationToken)
        {
            List<ReportInfo> reports = await ReportsAsync(itp.Activities.Select(a => a.Id), cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, itp.Revisions.Select(r => (int?)r.ChangedByUserId), cancellationToken);
            DateTime today = DateTime.Today;
            var dto = new ItpDto
            {
                Id = itp.Id, Number = itp.Number, WorkOrderId = itp.WorkOrderId, WorkOrderCode = itp.WorkOrder?.Code, ContractCode = itp.Contract?.Code,
                Revision = itp.Revision, Status = itp.Status, AutoDrafted = itp.AutoDrafted, ContractId = itp.ContractId, TemplateId = itp.TemplateId,
                TemplateCode = itp.Template?.Code, ClientApprovalReference = itp.ClientApprovalReference, ClientApprovalDate = itp.ClientApprovalDate,
                NoticeDays = itp.NoticeDays, Note = itp.Note, ActivityCount = itp.Activities.Count,
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
                    WitnessDeadline = a.NotificationSentAt?.Date.AddDays(itp.NoticeDays), DoneAt = a.DoneAt, StatusNote = a.StatusNote,
                    HoldPending = !terminal && HasPoint(a, ItpPoint.H),
                    Blockers = terminal ? new List<string>() : Evaluate(itp, a, reports, today, waive),
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
                NoticeDays = (int)settings[abroad ? QualitySettingKeys.NoticeDaysAbroad : QualitySettingKeys.NoticeDaysIran],
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
            if (dto.NoticeDays < 0 || dto.NoticeDays > 365) return Bad<ItpDto>("مهلت اعلام نقطه شاهد باید بین ۰ تا ۳۶۵ روز باشد.");
            itp.NoticeDays = dto.NoticeDays;
            itp.Note = Clean(dto.Note);
            return await SaveAsync(itp, cancellationToken);
        }

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
                    break;
                case "client-approve":
                    if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (itp.Status != ItpStatus.Submitted) return Bad<ItpDto>("ابتدا برنامه را برای کارفرما ارسال کنید.");
                    if (Clean(dto.Reference) == null) return Bad<ItpDto>("شماره نامه یا مرجع تایید کارفرما را وارد کنید.");
                    itp.Status = ItpStatus.ClientApproved; itp.ClientApprovalReference = Clean(dto.Reference);
                    itp.ClientApprovalDate = (dto.Date ?? DateTime.Today).Date;
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
                    activity.NotificationSentAt = date;
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
                    ResponseDto<ItpDto> refused = await MarkDoneAsync(itp, activity, cancellationToken);
                    if (refused != null) return refused;
                    activity.StatusNote = Clean(dto.Note) ?? activity.StatusNote;
                    break;
                case "not-applicable":
                    if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<ItpDto>(QualityText.NotAllowed);
                    if (IsTerminal(activity.Status)) return Bad<ItpDto>("فعالیت پایان یافته است.");
                    if (Clean(dto.Note) == null) return Bad<ItpDto>("دلیل غیرقابل‌اجرا بودن فعالیت را بنویسید.");
                    activity.Status = ItpActivityStatus.NotApplicable; activity.StatusNote = Clean(dto.Note);
                    activity.DoneAt = DateTimeOffset.Now; activity.DoneByUserId = Me;
                    break;
                default:
                    return Bad<ItpDto>("عملیات نامعتبر است.");
            }
            return await SaveAsync(itp, cancellationToken);
        }

        /// <summary>Applies Done to the activity (adds the waived-by-notice rows). Returns an error response, or null when done.</summary>
        private async Task<ResponseDto<ItpDto>> MarkDoneAsync(Itp itp, ItpActivity activity, CancellationToken cancellationToken)
        {
            if (IsTerminal(activity.Status)) return Bad<ItpDto>("فعالیت قبلاً پایان یافته است.");
            if ((itp.Status == ItpStatus.Draft || itp.Status == ItpStatus.Submitted) && (HasPoint(activity, ItpPoint.H) || HasPoint(activity, ItpPoint.W)))
                    return Bad<ItpDto>("برنامه هنوز توسط کارفرما تایید نشده است؛ فعالیت دارای نقطه کنترل پیش از تایید برنامه انجام‌شده ثبت نمی‌شود.");
            List<ReportInfo> reports = await ReportsAsync(new[] { activity.Id }, cancellationToken);
            var waive = new List<ReleaseParty>();
            List<string> blockers = Evaluate(itp, activity, reports, DateTime.Today, waive);
            if (blockers.Count > 0) return Bad<ItpDto>(string.Join(" ", blockers));
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (ReleaseParty party in waive)
                activity.Releases.Add(new ItpPointRelease
                {
                    Party = party, Action = ReleaseAction.Waived, PersonName = null, Organisation = PartyName(party), ReleaseDate = DateTime.Today,
                    Reference = "witness waived by notice", WaivedByNotice = true, CreatedAt = now, CreatedByUserId = Me,
                });
            activity.Status = ItpActivityStatus.Done; activity.DoneAt = now; activity.DoneByUserId = Me;
            if (itp.Status == ItpStatus.ClientApproved) itp.Status = ItpStatus.InUse;
            return null;
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
            ResponseDto<ItpDto> refused = await MarkDoneAsync(itp, activity, cancellationToken);
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
                WitnessDeadline = a.NotificationSentAt?.Date.AddDays(a.NoticeDays),
            }).ToList();
            return ResponseDtoHelper.Ok(await PagedList<PendingPointDto>.CreateAsync(list, query.PageNumber, query.PageSize, cancellationToken));
        }
    }
}
