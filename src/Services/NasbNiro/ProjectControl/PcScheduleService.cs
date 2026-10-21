using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcScheduleService
    {
        Task<ResponseDto<PagedList<PcCalendarDto>>> GetCalendarsAsync(PaginationParams query, CancellationToken cancellationToken);
        Task<ResponseDto<PcCalendarDto>> SaveCalendarAsync(PcCalendarDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcWbsNodeDto>>> GetWbsAsync(PcWbsQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcWbsNodeDto>> SaveWbsNodeAsync(PcWbsNodeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteWbsNodeAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcWbsNodeDto>> ApproveWbsNodeAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcWbsGenerateResultDto>> GenerateWbsAsync(PcWbsGenerateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<int>> AutoWeightsAsync(int contractId, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcActivityDto>>> GetActivitiesAsync(PcActivityQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcActivityDto>> GetActivityAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcActivityDto>> SaveActivityAsync(PcActivityDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteActivityAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcActivityDto>> ActivityActionAsync(PcActivityActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcDependencyDto>>> GetDependenciesAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcDependencyDto>> SaveDependencyAsync(PcDependencyDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteDependencyAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PcCpmRunDto>> RunCpmAsync(PcCpmRunRequestDto dto, CancellationToken cancellationToken);

        /// <summary>Read only plan run with some durations replaced (change request scenario). Null when the contract has no activities or the network has a cycle.</summary>
        Task<CpmResult> PreviewPlanAsync(int contractId, Dictionary<int, int> durationOverride, CancellationToken cancellationToken);

        /// <summary>Plan run on tracked activities (the caller already changed them) that writes the dates and mirrors them to the legacy fields. The caller saves.</summary>
        Task<CpmResult> ApplyPlanNoSaveAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcExecutivePlanRowDto>>> GetExecutivePlanAsync(PcExecutivePlanQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<string>> ExportMsProjectXmlAsync(int contractId, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcNeedByRowDto>>> GetNeedByAsync(PcContractQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcNeedByRowDto>>> ApplyNeedByAsync(PcNeedByApplyDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PcWbsGenerateResultDto>> GenerateItpActivitiesAsync(PcItpGenerateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcNoticeDraftDto>>> GetNoticeDraftsAsync(PcNoticeQuery query, CancellationToken cancellationToken);

        /// <summary>Null when adding the link would keep the network acyclic, otherwise the Persian error with the activity codes of the cycle.</summary>
        Task<string> CheckNoCycleAsync(int contractId, int predecessorId, int successorId, CancellationToken cancellationToken);
    }

    /// <summary>Calendars, WBS, activities, dependencies and the critical path method (WI-PL-009 sections 12-18).</summary>
    public class PcScheduleService : IPcScheduleService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcProgressService _progress;
        private readonly IPcProjectService _project;
        private readonly IPcMdrService _mdr;

        public PcScheduleService(IUnitOfWork uow, IInventoryUserContext user, IPcProgressService progress, IPcProjectService project, IPcMdrService mdr)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _mdr = mdr ?? throw new ArgumentNullException(nameof(mdr));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);
        private static DateTimeOffset? Dto(DateTime? d) => d.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Utc)) : (DateTimeOffset?)null;

        // ============================================================================ calendars

        public async Task<ResponseDto<PagedList<PcCalendarDto>>> GetCalendarsAsync(PaginationParams query, CancellationToken cancellationToken)
        {
            query ??= new PaginationParams();
            IQueryable<PcCalendar> q = _uow.Set<PcCalendar>().AsNoTracking().Where(c => !c.Deleted).OrderBy(c => c.Id);
            PagedList<PcCalendar> page = await PagedList<PcCalendar>.CreateAsync(q.Include(c => c.Exceptions), query.PageNumber, query.PageSize, cancellationToken);
            List<PcCalendarDto> dtos = page.Select(CalendarDto).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcCalendarDto>)new PcPage<PcCalendarDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        private static PcCalendarDto CalendarDto(PcCalendar c) => new PcCalendarDto
        {
            Id = c.Id, Name = c.Name, WorkingWeekdays = c.WorkingWeekdays, HoursPerDay = c.HoursPerDay, UseCompanyHolidays = c.UseCompanyHolidays,
            Exceptions = (c.Exceptions ?? new List<PcCalendarException>()).OrderBy(e => e.Date).Select(e => new PcCalendarExceptionDto { Date = e.Date, IsWorking = e.IsWorking, Note = e.Note }).ToList(),
        };

        public async Task<ResponseDto<PcCalendarDto>> SaveCalendarAsync(PcCalendarDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Name) == null) return Bad<PcCalendarDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcCalendarDto>(PcText.NotAllowed);
            if (dto.WorkingWeekdays <= 0 || dto.WorkingWeekdays > 127) return Bad<PcCalendarDto>("حداقل یک روز کاری در هفته لازم است (۱ تا ۱۲۷).");
            if (dto.HoursPerDay <= 0 || dto.HoursPerDay > 24) return Bad<PcCalendarDto>("ساعت کار روزانه نامعتبر است.");
            if (dto.Exceptions != null && dto.Exceptions.Select(e => e.Date.Date).Distinct().Count() != dto.Exceptions.Count) return Bad<PcCalendarDto>("تاریخ استثنا تکراری است.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcCalendar c;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<PcCalendar>().Include(x => x.Exceptions).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (c == null) return PcText.NotFound<PcCalendarDto>("تقویم پیدا نشد.");
                _uow.Set<PcCalendarException>().RemoveRange(c.Exceptions);
                c.Exceptions = new List<PcCalendarException>();
            }
            else
            {
                c = new PcCalendar { CreateDate = now, CreatorUserId = Me, Exceptions = new List<PcCalendarException>() };
                _uow.Set<PcCalendar>().Add(c);
            }
            c.Name = Clean(dto.Name); c.WorkingWeekdays = dto.WorkingWeekdays; c.HoursPerDay = dto.HoursPerDay; c.UseCompanyHolidays = dto.UseCompanyHolidays;
            foreach (PcCalendarExceptionDto e in dto.Exceptions ?? new List<PcCalendarExceptionDto>())
                c.Exceptions.Add(new PcCalendarException { Date = e.Date.Date, IsWorking = e.IsWorking, Note = Clean(e.Note) });
            c.ModifyDate = now; c.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(CalendarDto(c));
        }

        // ============================================================================ WBS

        private static PcWbsNodeDto ToDto(PcWbsNode n) => new PcWbsNodeDto
        {
            Id = n.Id, ContractId = n.ContractId, ParentId = n.ParentId, Code = n.Code, Title = n.Title, NodeType = n.NodeType, Phase = n.Phase, WorkOrderId = n.WorkOrderId, AssemblyId = n.AssemblyId,
            BomId = n.BomId, ProcessId = n.ProcessId, Weight = n.Weight, BudgetAmount = n.BudgetAmount, Dictionary = n.Dictionary, ResponsibleUnit = n.ResponsibleUnit, Sort = n.Sort,
            IsGenerated = n.IsGenerated, ApprovedAt = n.ApprovedAt, RowVersion = n.RowVersion == null ? null : Convert.ToBase64String(n.RowVersion),
        };

        public async Task<ResponseDto<PagedList<PcWbsNodeDto>>> GetWbsAsync(PcWbsQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcWbsQuery();
            IQueryable<PcWbsNode> q = _uow.Set<PcWbsNode>().AsNoTracking().Where(n => !n.Deleted);
            if (query.ContractId.HasValue) q = q.Where(n => n.ContractId == query.ContractId.Value);
            if (query.WorkOrderId.HasValue) q = q.Where(n => n.WorkOrderId == query.WorkOrderId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(n => n.Code.Contains(text) || n.Title.Contains(text));
            IQueryable<PcWbsNodeDto> projection = q.OrderBy(n => n.ContractId).ThenBy(n => n.Code).Select(n => new PcWbsNodeDto
            {
                Id = n.Id, ContractId = n.ContractId, ParentId = n.ParentId, Code = n.Code, Title = n.Title, NodeType = n.NodeType, Phase = n.Phase, WorkOrderId = n.WorkOrderId, AssemblyId = n.AssemblyId,
                BomId = n.BomId, ProcessId = n.ProcessId, Weight = n.Weight, BudgetAmount = n.BudgetAmount, Dictionary = n.Dictionary, ResponsibleUnit = n.ResponsibleUnit, Sort = n.Sort,
                IsGenerated = n.IsGenerated, ApprovedAt = n.ApprovedAt,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcWbsNodeDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcWbsNodeDto>> SaveWbsNodeAsync(PcWbsNodeDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Code) == null || Clean(dto.Title) == null) return Bad<PcWbsNodeDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<PcWbsNodeDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcWbsNodeType), dto.NodeType)) return Bad<PcWbsNodeDto>("نوع گره WBS را مشخص کنید.");
            if (dto.Weight < 0 || dto.Weight > 100) return Bad<PcWbsNodeDto>("وزن باید بین ۰ و ۱۰۰ باشد.");
            if (dto.BudgetAmount < 0) return Bad<PcWbsNodeDto>("بودجه منفی نیست.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcWbsNodeDto>(PcText.ContractNotFound);
            if (dto.ParentId.HasValue)
            {
                PcWbsNode parent = await _uow.Set<PcWbsNode>().AsNoTracking().FirstOrDefaultAsync(n => n.Id == dto.ParentId.Value && !n.Deleted, cancellationToken);
                if (parent == null || parent.ContractId != dto.ContractId) return Bad<PcWbsNodeDto>("گره والد متعلق به این قرارداد نیست.");
                if (dto.Id.HasValue && await IsDescendantAsync(dto.ParentId.Value, dto.Id.Value, cancellationToken)) return Bad<PcWbsNodeDto>("گره نمی‌تواند زیرمجموعه خودش باشد.");
            }
            string code = Clean(dto.Code);
            if (await _uow.Set<PcWbsNode>().AnyAsync(n => n.ContractId == dto.ContractId && n.Code == code && !n.Deleted && n.Id != (dto.Id ?? 0), cancellationToken)) return Bad<PcWbsNodeDto>("این کد WBS قبلاً در قرارداد استفاده شده است.");
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && w.ContractId == dto.ContractId && !w.Deleted, cancellationToken)) return Bad<PcWbsNodeDto>("دستور کار متعلق به این قرارداد نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcWbsNode n;
            if (dto.Id.HasValue)
            {
                n = await _uow.Set<PcWbsNode>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (n == null) return PcText.NotFound<PcWbsNodeDto>("گره WBS پیدا نشد.");
                if (n.ContractId != dto.ContractId) return Bad<PcWbsNodeDto>("قرارداد گره قابل تغییر نیست.");
            }
            else
            {
                n = new PcWbsNode { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcWbsNode>().Add(n);
            }
            n.ParentId = dto.ParentId; n.Code = code; n.Title = Clean(dto.Title); n.NodeType = dto.NodeType; n.Phase = dto.Phase; n.WorkOrderId = dto.WorkOrderId;
            n.AssemblyId = dto.AssemblyId; n.BomId = dto.BomId; n.ProcessId = dto.ProcessId; n.Weight = dto.Weight; n.BudgetAmount = dto.BudgetAmount; n.Dictionary = Clean(dto.Dictionary);
            n.ResponsibleUnit = Clean(dto.ResponsibleUnit)?.ToUpperInvariant(); n.Sort = dto.Sort;
            n.ModifyDate = now; n.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcWbsNodeDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(n));
        }

        private async Task<bool> IsDescendantAsync(int nodeId, int ancestorId, CancellationToken cancellationToken)
        {
            int? cursor = nodeId;
            for (int i = 0; i < 200 && cursor.HasValue; i++)
            {
                if (cursor.Value == ancestorId) return true;
                cursor = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.Id == cursor.Value).Select(n => n.ParentId).FirstOrDefaultAsync(cancellationToken);
            }
            return false;
        }

        public async Task<ResponseDto<bool>> DeleteWbsNodeAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<bool>(PcText.NotAllowed);
            PcWbsNode n = await _uow.Set<PcWbsNode>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (n == null) return PcText.NotFound<bool>("گره WBS پیدا نشد.");
            if (await _uow.Set<PcWbsNode>().AnyAsync(x => x.ParentId == id && !x.Deleted, cancellationToken)) return Bad<bool>("گره زیرمجموعه دارد و حذف نمی‌شود.");
            if (await _uow.Set<PcActivity>().AnyAsync(a => a.WbsNodeId == id && !a.Deleted, cancellationToken)) return Bad<bool>("گره فعالیت دارد و حذف نمی‌شود.");
            if (await _uow.Set<PcBaselineActivity>().AnyAsync(a => a.WbsNodeId == id, cancellationToken)) return Bad<bool>("گره در مبنای زمانی ثبت شده و حذف نمی‌شود.");
            n.Delete(); n.ModifyDate = DateTimeOffset.Now; n.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<PcWbsNodeDto>> ApproveWbsNodeAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.ProjectManagers)) return Bad<PcWbsNodeDto>(PcText.NotAllowed);
            PcWbsNode n = await _uow.Set<PcWbsNode>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (n == null) return PcText.NotFound<PcWbsNodeDto>("گره WBS پیدا نشد.");
            if (n.ApprovedAt.HasValue) return Bad<PcWbsNodeDto>("قبلاً تایید شده است.");
            if (Clean(n.Dictionary) == null) return Bad<PcWbsNodeDto>("فرهنگ لغت WBS گره را بنویسید.");
            n.ApprovedAt = DateTimeOffset.Now; n.ApprovedByUserId = Me; n.ModifyDate = n.ApprovedAt.Value; n.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(n));
        }

        // ---- weights

        /// <summary>Weights of siblings (nodes and activities under the same parent) sum to 100: by budget, else by duration, else equal.</summary>
        internal static void ApplyWeights(IList<PcWbsNode> nodes, IList<PcActivity> activities)
        {
            var inSet = new HashSet<PcWbsNode>(nodes);
            var nodeById = nodes.Where(n => n.Id != 0).ToDictionary(n => n.Id);
            PcWbsNode ParentOf(PcWbsNode n)
            {
                PcWbsNode p = n.Parent ?? (n.ParentId.HasValue && nodeById.TryGetValue(n.ParentId.Value, out PcWbsNode found) ? found : null);
                return p != null && inSet.Contains(p) ? p : null;
            }
            PcWbsNode NodeOf(PcActivity a)
            {
                PcWbsNode p = a.WbsNode ?? (a.WbsNodeId.HasValue && nodeById.TryGetValue(a.WbsNodeId.Value, out PcWbsNode found) ? found : null);
                return p != null && inSet.Contains(p) ? p : null;
            }

            var childNodes = new Dictionary<PcWbsNode, List<PcWbsNode>>();
            var childActs = new Dictionary<PcWbsNode, List<PcActivity>>();
            var rootNodes = new List<PcWbsNode>();
            foreach (PcWbsNode n in nodes)
            {
                PcWbsNode p = ParentOf(n);
                if (p == null) { rootNodes.Add(n); continue; }
                if (!childNodes.ContainsKey(p)) childNodes[p] = new List<PcWbsNode>();
                childNodes[p].Add(n);
            }
            foreach (PcActivity a in activities)
            {
                PcWbsNode p = NodeOf(a);
                if (p == null) continue;
                if (!childActs.ContainsKey(p)) childActs[p] = new List<PcActivity>();
                childActs[p].Add(a);
            }

            // budget and duration roll-ups (post-order)
            var budget = new Dictionary<PcWbsNode, decimal>();
            var duration = new Dictionary<PcWbsNode, decimal>();
            void Roll(PcWbsNode n)
            {
                decimal b = 0m, d = 0m;
                if (childNodes.TryGetValue(n, out List<PcWbsNode> cn)) foreach (PcWbsNode c in cn) { Roll(c); b += budget[c]; d += duration[c]; }
                if (childActs.TryGetValue(n, out List<PcActivity> ca)) foreach (PcActivity a in ca) { b += a.BudgetAtCompletion; d += Math.Max(1, a.DurationDays); }
                budget[n] = b; duration[n] = d; n.BudgetAmount = b;
            }
            foreach (PcWbsNode r in rootNodes) Roll(r);

            void Split(List<PcWbsNode> ns, List<PcActivity> acts)
            {
                var items = new List<(object Item, decimal Budget, decimal Duration)>();
                foreach (PcWbsNode n in ns) items.Add((n, budget[n], duration[n]));
                foreach (PcActivity a in acts) items.Add((a, a.BudgetAtCompletion, Math.Max(1, a.DurationDays)));
                if (items.Count == 0) return;
                decimal totalBudget = items.Sum(i => i.Budget);
                decimal totalDuration = items.Sum(i => i.Duration);
                var shares = new List<decimal>();
                foreach (var i in items)
                    shares.Add(totalBudget > 0 ? i.Budget / totalBudget * 100m : totalDuration > 0 ? i.Duration / totalDuration * 100m : 100m / items.Count);
                decimal sum = 0m;
                for (int k = 0; k < items.Count; k++)
                {
                    decimal w = k == items.Count - 1 ? Math.Round(100m - sum, 4) : Math.Round(shares[k], 4);
                    sum += w;
                    if (items[k].Item is PcWbsNode nn) nn.Weight = w; else ((PcActivity)items[k].Item).Weight = w;
                }
            }
            Split(rootNodes, new List<PcActivity>());
            foreach (PcWbsNode n in nodes)
            {
                childNodes.TryGetValue(n, out List<PcWbsNode> cn);
                childActs.TryGetValue(n, out List<PcActivity> ca);
                Split(cn ?? new List<PcWbsNode>(), ca ?? new List<PcActivity>());
            }
        }

        public async Task<ResponseDto<int>> AutoWeightsAsync(int contractId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<int>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == contractId && !c.Deleted, cancellationToken)) return PcText.NotFound<int>(PcText.ContractNotFound);
            List<PcWbsNode> nodes = await _uow.Set<PcWbsNode>().Where(n => n.ContractId == contractId && !n.Deleted).ToListAsync(cancellationToken);
            List<PcActivity> acts = await _uow.Set<PcActivity>().Where(a => a.ContractId == contractId && !a.Deleted).ToListAsync(cancellationToken);
            ApplyWeights(nodes, acts);
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PcWbsNode n in nodes) { n.ModifyDate = now; n.ModifierUserId = Me; }
            foreach (PcActivity a in acts) { a.ModifyDate = now; a.ModifierUserId = Me; }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(nodes.Count + acts.Count);
        }

        // ---- generation from the legacy structure

        private sealed class GenState
        {
            public PcWbsNode Root;
            public List<PcWbsNode> Nodes = new List<PcWbsNode>();
            public List<PcActivity> Activities = new List<PcActivity>();
            public List<PcDependency> Dependencies = new List<PcDependency>();
            public int Counter;
            public DateTimeOffset Now;
        }

        private PcActivity NewActivity(GenState g, int contractId, PcWbsNode node, string code, string title, PcSourceType source, int? sourceId, int? workOrderId, int duration, PcPhase phase, string unit, PcProgressMethod method)
        {
            var a = new PcActivity
            {
                ContractId = contractId, WbsNode = node, Code = code, Title = title, SourceType = source, SourceId = sourceId, WorkOrderId = workOrderId, DurationDays = duration, Phase = phase,
                ResponsibleUnit = unit, ProgressMethod = method, Status = PcActivityStatus.NotStarted, Sort = ++g.Counter, CreateDate = g.Now, CreatorUserId = Me, ModifyDate = g.Now, ModifierUserId = Me,
            };
            g.Activities.Add(a);
            return a;
        }

        private PcWbsNode NewNode(GenState g, int contractId, PcWbsNode parent, string code, string title, PcWbsNodeType type, PcPhase? phase, int? workOrderId)
        {
            var n = new PcWbsNode
            {
                ContractId = contractId, Parent = parent, Code = code, Title = title, NodeType = type, Phase = phase, WorkOrderId = workOrderId, IsGenerated = true, Sort = ++g.Counter,
                CreateDate = g.Now, CreatorUserId = Me, ModifyDate = g.Now, ModifierUserId = Me,
            };
            g.Nodes.Add(n);
            return n;
        }

        private PcDependency Link(GenState g, int contractId, PcActivity pred, PcActivity succ, PcDependencyOrigin origin)
        {
            if (pred == null || succ == null || ReferenceEquals(pred, succ)) return null;
            if (g.Dependencies.Any(d => ReferenceEquals(d.Predecessor, pred) && ReferenceEquals(d.Successor, succ))) return null;
            var d = new PcDependency { ContractId = contractId, Predecessor = pred, Successor = succ, Type = PcDependencyType.FS, LagDays = 0, Origin = origin };
            g.Dependencies.Add(d);
            return d;
        }

        private static string UnitFor(ProcessType type)
        {
            switch (type)
            {
                case ProcessType.Buy: return "COMMERCIAL";
                case ProcessType.Service: return "CONSTRUCTION";
                case ProcessType.Construction: return "CONSTRUCTION";
                default: return "ASSEMBLER";
            }
        }

        public async Task<ResponseDto<PcWbsGenerateResultDto>> GenerateWbsAsync(PcWbsGenerateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcWbsGenerateResultDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcWbsGenerateResultDto>(PcText.NotAllowed);
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken);
            if (contract == null) return PcText.NotFound<PcWbsGenerateResultDto>(PcText.ContractNotFound);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, dto.ContractId, false, cancellationToken);

            IQueryable<WorkOrder> woQuery = _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.ContractId == dto.ContractId && !w.Deleted);
            if (dto.WorkOrderIds != null && dto.WorkOrderIds.Count > 0) woQuery = woQuery.Where(w => dto.WorkOrderIds.Contains(w.Id));
            List<WorkOrder> orders = await woQuery.OrderBy(w => w.Id).ToListAsync(cancellationToken);
            if (orders.Count == 0) return Bad<PcWbsGenerateResultDto>("دستور کاری برای تولید WBS پیدا نشد.");

            var result = new PcWbsGenerateResultDto { ContractId = dto.ContractId };
            var g = new GenState { Now = DateTimeOffset.Now };
            string projectCode = terms?.ProjectCode ?? contract.Code ?? ("C" + contract.Id);

            PcWbsNode root = await _uow.Set<PcWbsNode>().FirstOrDefaultAsync(n => n.ContractId == dto.ContractId && n.ParentId == null && n.NodeType == PcWbsNodeType.Project && !n.Deleted, cancellationToken);
            if (root == null) root = NewNode(g, dto.ContractId, null, projectCode, string.IsNullOrWhiteSpace(contract.Title) ? projectCode : contract.Title, PcWbsNodeType.Project, null, null);
            g.Root = root;
            g.Counter = await _uow.Set<PcActivity>().Where(a => a.ContractId == dto.ContractId).MaxAsync(a => (int?)a.Sort, cancellationToken) ?? 0;

            HashSet<int> existingWo = (await _uow.Set<PcWbsNode>().Where(n => n.ContractId == dto.ContractId && !n.Deleted && n.NodeType == PcWbsNodeType.Product && n.WorkOrderId != null)
                .Select(n => n.WorkOrderId.Value).ToListAsync(cancellationToken)).ToHashSet();
            int productIndex = existingWo.Count;

            foreach (WorkOrder wo in orders)
            {
                if (existingWo.Contains(wo.Id)) { result.WorkOrdersSkipped++; result.Notes.Add("WBS دستور کار " + wo.Code + " قبلاً تولید شده است."); continue; }
                productIndex++;
                PcWorkOrderInfo info = await _uow.Set<PcWorkOrderInfo>().AsNoTracking().FirstOrDefaultAsync(i => i.WorkOrderId == wo.Id && !i.Deleted, cancellationToken);
                string pcode = projectCode + "." + productIndex.ToString("00", CultureInfo.InvariantCulture);
                PcWbsNode product = NewNode(g, dto.ContractId, root, pcode, wo.Title ?? wo.Code, PcWbsNodeType.Product, null, wo.Id);
                var heads = new List<PcActivity>();
                var tails = new List<PcActivity>();
                int seqNo = 0;
                string NextCode() => pcode + "-" + (++seqNo).ToString("000", CultureInfo.InvariantCulture);

                // engineering from the MDR template of the product type
                PcActivity mdr = null, mdl = null;
                int drafted = await _mdr.DraftMdrNoSaveAsync(dto.ContractId, wo.Id, info?.ProductType ?? ItpProductType.Other, cancellationToken);
                if (drafted > 0)
                {
                    result.MdrLinesDrafted += drafted;
                    PcWbsNode eng = NewNode(g, dto.ContractId, product, pcode + ".E", "مهندسی و مدارک", PcWbsNodeType.Phase, PcPhase.Engineering, wo.Id);
                    mdr = NewActivity(g, dto.ContractId, eng, NextCode(), "تهیه و صدور مدارک داخلی (MDR)", PcSourceType.EngineeringDeliverable, 0, wo.Id, 10, PcPhase.Engineering, "ENGINEERING", PcProgressMethod.EngineeringIssue);
                    mdl = NewActivity(g, dto.ContractId, eng, NextCode(), "ارسال و تایید مدارک کارفرما (MDL)", PcSourceType.EngineeringDeliverable, 1, wo.Id, 10, PcPhase.Engineering, "ENGINEERING", PcProgressMethod.EngineeringIssue);
                    Link(g, dto.ContractId, mdr, mdl, PcDependencyOrigin.Manual);
                }

                List<AssemblyEntity> assemblies = await _uow.Set<AssemblyEntity>().AsNoTracking().Where(a => a.WorkOrderId == wo.Id && !a.Deleted).OrderBy(a => a.Id).ToListAsync(cancellationToken);
                var assemblyTail = new Dictionary<int, PcActivity>();
                var assemblyHead = new Dictionary<int, PcActivity>();
                int asmIndex = 0;
                foreach (AssemblyEntity asm in assemblies)
                {
                    asmIndex++;
                    PcWbsNode asmNode = NewNode(g, dto.ContractId, product, pcode + "." + asmIndex.ToString("00", CultureInfo.InvariantCulture), asm.Title ?? asm.Code, PcWbsNodeType.Assembly, null, wo.Id);
                    asmNode.AssemblyId = asm.Id;
                    List<Bom> boms = await _uow.Set<Bom>().AsNoTracking().Where(b => b.AssemblyId == asm.Id && !b.Deleted).OrderBy(b => b.Sort).ThenBy(b => b.Id).ToListAsync(cancellationToken);
                    var bomHead = new Dictionary<int, PcActivity>();
                    var bomTail = new Dictionary<int, PcActivity>();
                    PcActivity asmHead = null, asmTail = null;
                    foreach (Bom bom in boms)
                    {
                        PcWbsNode bomNode = NewNode(g, dto.ContractId, asmNode, asmNode.Code + "." + bom.PositionNumber.ToString("00", CultureInfo.InvariantCulture), bom.Title ?? ("BOM " + bom.PositionNumber), PcWbsNodeType.Part, null, wo.Id);
                        bomNode.AssemblyId = asm.Id; bomNode.BomId = bom.Id;
                        List<ProcessEntity> processes = await _uow.Set<ProcessEntity>().AsNoTracking().Where(p => p.BomId == bom.Id && !p.Deleted).OrderBy(p => p.Sort).ThenBy(p => p.Id).ToListAsync(cancellationToken);
                        PcActivity prev = null;
                        foreach (ProcessEntity proc in processes)
                        {
                            PcProgressMethod method = proc.Type == ProcessType.Service ? PcProgressMethod.ServiceDocs : PcProgressMethod.Manual;
                            PcPhase phase = proc.Type == ProcessType.Buy ? PcPhase.Procurement : proc.Type == ProcessType.FinalAssembly ? PcPhase.Testing : PcPhase.Construction;
                            PcActivity act = NewActivity(g, dto.ContractId, bomNode, NextCode(), proc.Title ?? ("فرآیند " + proc.Id), PcSourceType.Process, proc.Id, wo.Id, Math.Max(1, proc.GetTotalDuration()), phase, UnitFor(proc.Type), method);
                            act.BudgetAtCompletion = proc.ProgramPrice;
                            act.Weight = proc.WeightPercent;
                            act.ActualStart = PcText.RealDate(proc.ActualStartDate ?? default);
                            act.ActualFinish = proc.ProcessStatus == ProcessStatus.ProcessIsCompleted ? PcText.RealDate(proc.ActualEndDate ?? default) : null;
                            if (proc.Type == ProcessType.Buy)
                            {
                                var link = await FindPurchaseLinkAsync(proc.Id, cancellationToken);
                                if (link.OrderLineId.HasValue) { act.PurchaseOrderLineId = link.OrderLineId; act.PurchaseRequestLineId = link.RequestLineId; act.ProgressMethod = PcProgressMethod.SupplyReceipt; }
                                else if (link.RequestLineId.HasValue) act.PurchaseRequestLineId = link.RequestLineId;
                            }
                            if (prev == null) { bomHead[bom.Id] = act; }
                            else Link(g, dto.ContractId, prev, act, PcDependencyOrigin.ProcessSort);
                            prev = act;
                        }
                        if (prev != null) bomTail[bom.Id] = prev;
                        if (asmHead == null && bomHead.ContainsKey(bom.Id)) asmHead = bomHead[bom.Id];
                        if (bomTail.ContainsKey(bom.Id)) asmTail = bomTail[bom.Id];
                    }
                    foreach (Bom bom in boms.Where(b => b.PredecessorId.HasValue))
                        if (bomHead.TryGetValue(bom.Id, out PcActivity head) && bomTail.TryGetValue(bom.PredecessorId.Value, out PcActivity ptail))
                            Link(g, dto.ContractId, ptail, head, PcDependencyOrigin.BomPredecessor);
                    if (asmHead != null) assemblyHead[asm.Id] = asmHead;
                    if (asmTail != null) assemblyTail[asm.Id] = asmTail;
                    // activities that have no predecessor inside the assembly start it
                    foreach (var kv in bomHead)
                        if (!g.Dependencies.Any(d => ReferenceEquals(d.Successor, kv.Value))) heads.Add(kv.Value);
                }
                foreach (AssemblyEntity asm in assemblies.Where(a => a.PredecessorId.HasValue))
                    if (assemblyHead.TryGetValue(asm.Id, out PcActivity head) && assemblyTail.TryGetValue(asm.PredecessorId.Value, out PcActivity ptail))
                        Link(g, dto.ContractId, ptail, head, PcDependencyOrigin.AssemblyPredecessor);

                // manufacturing waits for the internal documents
                if (mdr != null)
                    foreach (PcActivity h in g.Activities.Where(a => a.WorkOrderId == wo.Id && a.SourceType == PcSourceType.Process && !g.Dependencies.Any(d => ReferenceEquals(d.Successor, a))).ToList())
                        Link(g, dto.ContractId, mdr, h, PcDependencyOrigin.Manual);

                // inspection plan, if there is one
                List<PcActivity> itp = await AddItpActivitiesAsync(g, dto.ContractId, product, wo.Id, pcode, cancellationToken);

                // close-out: release note, packing, shipment, delivery
                PcWbsNode closeout = NewNode(g, dto.ContractId, product, pcode + ".Z", "ترخیص، بسته‌بندی و تحویل", PcWbsNodeType.Phase, PcPhase.Delivery, wo.Id);
                PcActivity release = NewActivity(g, dto.ContractId, closeout, NextCode(), "صدور برگه ترخیص کیفی", PcSourceType.Milestone, null, wo.Id, 0, PcPhase.Delivery, "QC", PcProgressMethod.Milestone);
                release.MilestoneEvent = PcMilestoneEvent.ReleaseNoteReleased;
                PcActivity packing = NewActivity(g, dto.ContractId, closeout, NextCode(), "بسته‌بندی", PcSourceType.Manual, null, wo.Id, 2, PcPhase.Delivery, "WAREHOUSE", PcProgressMethod.Manual);
                packing.IsPackaging = true;
                PcActivity ship = NewActivity(g, dto.ContractId, closeout, NextCode(), "ارسال", PcSourceType.Milestone, null, wo.Id, 0, PcPhase.Delivery, "WAREHOUSE", PcProgressMethod.Milestone);
                ship.MilestoneEvent = PcMilestoneEvent.ShipmentShipped;
                PcActivity delivered = NewActivity(g, dto.ContractId, closeout, NextCode(), "تحویل به کارفرما", PcSourceType.Milestone, null, wo.Id, 0, PcPhase.Delivery, "COMMERCIAL", PcProgressMethod.Milestone);
                delivered.MilestoneEvent = PcMilestoneEvent.ShipmentDelivered;
                Link(g, dto.ContractId, release, packing, PcDependencyOrigin.Manual);
                Link(g, dto.ContractId, packing, ship, PcDependencyOrigin.Manual);
                Link(g, dto.ContractId, ship, delivered, PcDependencyOrigin.Manual);
                if (mdl != null) Link(g, dto.ContractId, mdl, release, PcDependencyOrigin.Manual);
                // every activity of the work order without a successor feeds the release note
                var withSuccessor = new HashSet<PcActivity>(g.Dependencies.Select(d => d.Predecessor));
                foreach (PcActivity a in g.Activities.Where(a => a.WorkOrderId == wo.Id && !ReferenceEquals(a, release) && !ReferenceEquals(a, packing) && !ReferenceEquals(a, ship) && !ReferenceEquals(a, delivered) && !withSuccessor.Contains(a)).ToList())
                    Link(g, dto.ContractId, a, release, PcDependencyOrigin.Manual);
            }

            if (g.Activities.Count == 0 && g.Nodes.Count == 0) return Bad<PcWbsGenerateResultDto>("هیچ دستور کار جدیدی برای تولید WBS نبود.");

            var allNodes = (await _uow.Set<PcWbsNode>().Where(n => n.ContractId == dto.ContractId && !n.Deleted).ToListAsync(cancellationToken)).Concat(g.Nodes).Distinct().ToList();
            var allActivities = (await _uow.Set<PcActivity>().Where(a => a.ContractId == dto.ContractId && !a.Deleted).ToListAsync(cancellationToken)).Concat(g.Activities).Distinct().ToList();
            // weights: only the new generated structure is re-weighted (users' manual weights elsewhere stay)
            ApplyWeights(allNodes.Where(n => n.IsGenerated).ToList(), allActivities.Where(a => a.SourceType != PcSourceType.Manual || g.Activities.Contains(a)).ToList());

            _uow.Set<PcWbsNode>().AddRange(g.Nodes.Where(x => x.Id == 0));
            _uow.Set<PcActivity>().AddRange(g.Activities);
            _uow.Set<PcDependency>().AddRange(g.Dependencies);
            string stepError = await _project.MarkStepAsync(dto.ContractId, PcInitiationStep.WbsDrafted, Me, false, "WBS", cancellationToken);
            if (stepError != null) return Bad<PcWbsGenerateResultDto>(stepError);
            await _uow.SaveChangesAsync(cancellationToken);
            result.NodesCreated = g.Nodes.Count; result.ActivitiesCreated = g.Activities.Count; result.DependenciesCreated = g.Dependencies.Count;
            return ResponseDtoHelper.Ok(result);
        }

        /// <summary>The purchase request line and order line that buy for a legacy BOM process (through its buy order).</summary>
        private async Task<(int? RequestLineId, int? OrderLineId)> FindPurchaseLinkAsync(int processId, CancellationToken cancellationToken)
        {
            List<int> buyOrderIds = await _uow.Set<BuyOrderProcesses>().AsNoTracking().Where(b => b.ProcessId == processId).Select(b => b.BuyOrderId).ToListAsync(cancellationToken);
            if (buyOrderIds.Count == 0) return (null, null);
            int? requestLine = await _uow.Set<PurchaseRequestLine>().AsNoTracking().Where(l => l.BuyOrderId != null && buyOrderIds.Contains(l.BuyOrderId.Value)).OrderBy(l => l.Id).Select(l => (int?)l.Id).FirstOrDefaultAsync(cancellationToken);
            if (!requestLine.HasValue) return (null, null);
            int? orderLine = await _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.RequestLineId == requestLine.Value && !l.PurchaseOrder.Deleted).OrderBy(l => l.Id).Select(l => (int?)l.Id).FirstOrDefaultAsync(cancellationToken);
            return (requestLine, orderLine);
        }

        private async Task<List<PcActivity>> AddItpActivitiesAsync(GenState g, int contractId, PcWbsNode product, int workOrderId, string pcode, CancellationToken cancellationToken)
        {
            var added = new List<PcActivity>();
            Itp itp = await _uow.Set<Itp>().AsNoTracking().Include(i => i.Activities).Where(i => i.WorkOrderId == workOrderId && !i.Deleted && i.Status != ItpStatus.Closed)
                .OrderByDescending(i => i.Status).ThenByDescending(i => i.Id).AsSplitQuery().FirstOrDefaultAsync(cancellationToken);
            if (itp == null) return added;
            HashSet<int> already = (await _uow.Set<PcActivity>().Where(a => a.ContractId == contractId && a.SourceType == PcSourceType.ItpActivity && a.ItpActivityId != null && !a.Deleted).Select(a => a.ItpActivityId.Value).ToListAsync(cancellationToken)).ToHashSet();
            PcWbsNode node = NewNode(g, contractId, product, pcode + ".Q", "بازرسی و آزمون (ITP)", PcWbsNodeType.Phase, PcPhase.Testing, workOrderId);
            PcActivity prev = null;
            int seq = g.Activities.Count(a => a.WorkOrderId == workOrderId);
            foreach (ItpActivity ia in itp.Activities.Where(a => a.GatesRelease && a.Status != ItpActivityStatus.NotApplicable && !already.Contains(a.Id)).OrderBy(a => a.Seq))
            {
                PcActivity act = NewActivity(g, contractId, node, pcode + "-Q" + (++seq).ToString("000", CultureInfo.InvariantCulture), (ia.Stage != null ? ia.Stage + " - " : "") + ia.Activity, PcSourceType.ItpActivity, ia.Id, workOrderId, 1, PcPhase.Testing, "QC", PcProgressMethod.ItpPoints);
                act.ItpActivityId = ia.Id;
                if (prev != null) Link(g, contractId, prev, act, PcDependencyOrigin.Manual);
                prev = act;
                added.Add(act);
            }
            if (added.Count == 0) { g.Nodes.Remove(node); }
            else
            {
                // inspections follow the manufacturing activities of the work order
                foreach (PcActivity tail in g.Activities.Where(a => a.WorkOrderId == workOrderId && a.SourceType == PcSourceType.Process && !g.Dependencies.Any(d => ReferenceEquals(d.Predecessor, a))).ToList())
                    Link(g, contractId, tail, added[0], PcDependencyOrigin.Manual);
            }
            return added;
        }

        public async Task<ResponseDto<PcWbsGenerateResultDto>> GenerateItpActivitiesAsync(PcItpGenerateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcWbsGenerateResultDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcWbsGenerateResultDto>(PcText.NotAllowed);
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId && w.ContractId == dto.ContractId && !w.Deleted, cancellationToken);
            if (wo == null) return PcText.NotFound<PcWbsGenerateResultDto>(PcText.WorkOrderNotFound);
            PcWbsNode product = await _uow.Set<PcWbsNode>().FirstOrDefaultAsync(n => n.ContractId == dto.ContractId && n.WorkOrderId == wo.Id && n.NodeType == PcWbsNodeType.Product && !n.Deleted, cancellationToken);
            if (product == null) return Bad<PcWbsGenerateResultDto>("ابتدا WBS دستور کار را تولید کنید.");
            var g = new GenState { Now = DateTimeOffset.Now };
            g.Counter = await _uow.Set<PcActivity>().Where(a => a.ContractId == dto.ContractId).MaxAsync(a => (int?)a.Sort, cancellationToken) ?? 0;
            List<PcActivity> added = await AddItpActivitiesAsync(g, dto.ContractId, product, wo.Id, product.Code, cancellationToken);
            if (added.Count == 0) return Bad<PcWbsGenerateResultDto>("برنامه بازرسی و آزمون (ITP) با فعالیت جدیدی برای این دستور کار پیدا نشد.");
            // before the release note, if there is one
            PcActivity release = await _uow.Set<PcActivity>().FirstOrDefaultAsync(a => a.ContractId == dto.ContractId && a.WorkOrderId == wo.Id && a.MilestoneEvent == PcMilestoneEvent.ReleaseNoteReleased && !a.Deleted, cancellationToken);
            if (release != null) Link(g, dto.ContractId, added[added.Count - 1], release, PcDependencyOrigin.Manual);
            // existing manufacturing tails feed the first inspection
            List<PcActivity> existing = await _uow.Set<PcActivity>().Where(a => a.ContractId == dto.ContractId && a.WorkOrderId == wo.Id && a.SourceType == PcSourceType.Process && !a.Deleted).ToListAsync(cancellationToken);
            HashSet<int> hasSuccessor = (await _uow.Set<PcDependency>().Where(d => d.ContractId == dto.ContractId).Select(d => d.PredecessorId).ToListAsync(cancellationToken)).ToHashSet();
            foreach (PcActivity tail in existing.Where(a => !hasSuccessor.Contains(a.Id)))
                Link(g, dto.ContractId, tail, added[0], PcDependencyOrigin.Manual);
            // the release note no longer waits for those tails directly: remove the redundant tail -> release links
            if (release != null)
            {
                List<PcDependency> redundant = await _uow.Set<PcDependency>().Where(d => d.ContractId == dto.ContractId && d.SuccessorId == release.Id && existing.Select(e => e.Id).Contains(d.PredecessorId)).ToListAsync(cancellationToken);
                _uow.Set<PcDependency>().RemoveRange(redundant);
            }
            _uow.Set<PcWbsNode>().AddRange(g.Nodes);
            _uow.Set<PcActivity>().AddRange(g.Activities);
            _uow.Set<PcDependency>().AddRange(g.Dependencies);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new PcWbsGenerateResultDto { ContractId = dto.ContractId, NodesCreated = g.Nodes.Count, ActivitiesCreated = g.Activities.Count, DependenciesCreated = g.Dependencies.Count });
        }

        // ============================================================================ activities

        private static PcActivityDto ToDto(PcActivity a, string wbsCode = null) => new PcActivityDto
        {
            Id = a.Id, ContractId = a.ContractId, WbsNodeId = a.WbsNodeId, WbsCode = wbsCode, Code = a.Code, Title = a.Title, SourceType = a.SourceType, SourceId = a.SourceId, WorkOrderId = a.WorkOrderId,
            DurationDays = a.DurationDays, CalendarId = a.CalendarId, Phase = a.Phase, ResponsibleUnit = a.ResponsibleUnit, ConstraintType = a.ConstraintType, ConstraintDate = a.ConstraintDate,
            EarlyStart = a.EarlyStart, EarlyFinish = a.EarlyFinish, LateStart = a.LateStart, LateFinish = a.LateFinish, TotalFloat = a.TotalFloat, FreeFloat = a.FreeFloat, IsCritical = a.IsCritical,
            ForecastStart = a.ForecastStart, ForecastFinish = a.ForecastFinish, ActualStart = a.ActualStart, ActualFinish = a.ActualFinish, ProgressMethod = a.ProgressMethod, Weight = a.Weight,
            BudgetAtCompletion = a.BudgetAtCompletion, Status = a.Status, OnHoldReason = a.OnHoldReason, PurchaseOrderLineId = a.PurchaseOrderLineId, PurchaseRequestLineId = a.PurchaseRequestLineId,
            ItpActivityId = a.ItpActivityId, MilestoneEvent = a.MilestoneEvent, PlannedQuantity = a.PlannedQuantity, SerialItemId = a.SerialItemId, NeedByDate = a.NeedByDate, LeadTimeDays = a.LeadTimeDays,
            IsPackaging = a.IsPackaging, IsLate = a.IsLate, LateKind = a.LateKind, PercentCache = a.PercentCache, Sort = a.Sort, RowVersion = a.RowVersion == null ? null : Convert.ToBase64String(a.RowVersion),
        };

        public async Task<ResponseDto<PagedList<PcActivityDto>>> GetActivitiesAsync(PcActivityQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcActivityQuery();
            IQueryable<PcActivity> q = _uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted);
            if (query.ContractId.HasValue) q = q.Where(a => a.ContractId == query.ContractId.Value);
            if (query.WorkOrderId.HasValue) q = q.Where(a => a.WorkOrderId == query.WorkOrderId.Value);
            if (query.WbsNodeId.HasValue) q = q.Where(a => a.WbsNodeId == query.WbsNodeId.Value);
            if (query.Status.HasValue) q = q.Where(a => a.Status == query.Status.Value);
            if (query.Phase.HasValue) q = q.Where(a => a.Phase == query.Phase.Value);
            string unit = Clean(query.ResponsibleUnit)?.ToUpperInvariant();
            if (unit != null) q = q.Where(a => a.ResponsibleUnit == unit);
            if (query.OnlyCritical) q = q.Where(a => a.IsCritical);
            if (query.OnlyLate) q = q.Where(a => a.IsLate);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(a => a.Code.Contains(text) || a.Title.Contains(text));
            IQueryable<PcActivityDto> projection = q.OrderBy(a => a.ContractId).ThenBy(a => a.Sort).ThenBy(a => a.Id).Select(a => new PcActivityDto
            {
                Id = a.Id, ContractId = a.ContractId, WbsNodeId = a.WbsNodeId, WbsCode = a.WbsNode.Code, Code = a.Code, Title = a.Title, SourceType = a.SourceType, SourceId = a.SourceId, WorkOrderId = a.WorkOrderId,
                DurationDays = a.DurationDays, CalendarId = a.CalendarId, Phase = a.Phase, ResponsibleUnit = a.ResponsibleUnit, ConstraintType = a.ConstraintType, ConstraintDate = a.ConstraintDate,
                EarlyStart = a.EarlyStart, EarlyFinish = a.EarlyFinish, LateStart = a.LateStart, LateFinish = a.LateFinish, TotalFloat = a.TotalFloat, FreeFloat = a.FreeFloat, IsCritical = a.IsCritical,
                ForecastStart = a.ForecastStart, ForecastFinish = a.ForecastFinish, ActualStart = a.ActualStart, ActualFinish = a.ActualFinish, ProgressMethod = a.ProgressMethod, Weight = a.Weight,
                BudgetAtCompletion = a.BudgetAtCompletion, Status = a.Status, OnHoldReason = a.OnHoldReason, PurchaseOrderLineId = a.PurchaseOrderLineId, PurchaseRequestLineId = a.PurchaseRequestLineId,
                ItpActivityId = a.ItpActivityId, MilestoneEvent = a.MilestoneEvent, PlannedQuantity = a.PlannedQuantity, SerialItemId = a.SerialItemId, NeedByDate = a.NeedByDate, LeadTimeDays = a.LeadTimeDays,
                IsPackaging = a.IsPackaging, IsLate = a.IsLate, LateKind = a.LateKind, PercentCache = a.PercentCache, Sort = a.Sort,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcActivityDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcActivityDto>> GetActivityAsync(int id, CancellationToken cancellationToken)
        {
            PcActivity a = await _uow.Set<PcActivity>().AsNoTracking().Include(x => x.WbsNode).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (a == null) return PcText.NotFound<PcActivityDto>("فعالیت پیدا نشد.");
            return ResponseDtoHelper.Ok(ToDto(a, a.WbsNode?.Code));
        }

        public async Task<ResponseDto<PcActivityDto>> SaveActivityAsync(PcActivityDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Title) == null) return Bad<PcActivityDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<PcActivityDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken)) return PcText.NotFound<PcActivityDto>(PcText.ContractNotFound);
            if (dto.DurationDays < 0 || dto.DurationDays > 3650) return Bad<PcActivityDto>("مدت فعالیت باید بین ۰ و ۳۶۵۰ روز کاری باشد.");
            if (dto.Weight < 0 || dto.Weight > 100) return Bad<PcActivityDto>("وزن باید بین ۰ و ۱۰۰ باشد.");
            if (dto.BudgetAtCompletion < 0) return Bad<PcActivityDto>("بودجه منفی نیست.");
            if (dto.LeadTimeDays < 0 || dto.LeadTimeDays > 3650) return Bad<PcActivityDto>("زمان تامین نامعتبر است.");
            if (!Enum.IsDefined(typeof(PcProgressMethod), dto.ProgressMethod)) return Bad<PcActivityDto>("روش پیشرفت نامعتبر است.");
            if (dto.ProgressMethod == PcProgressMethod.AssemblyChecklist) return Bad<PcActivityDto>("روش «چک‌لیست مونتاژ» در نسخه بعد فعال می‌شود.");
            if (!Enum.IsDefined(typeof(PcConstraint), dto.ConstraintType)) return Bad<PcActivityDto>("نوع قید نامعتبر است.");
            if (dto.ConstraintType != PcConstraint.None && !dto.ConstraintDate.HasValue) return Bad<PcActivityDto>("تاریخ قید را وارد کنید.");
            if (dto.WbsNodeId.HasValue && !await _uow.Set<PcWbsNode>().AnyAsync(n => n.Id == dto.WbsNodeId.Value && n.ContractId == dto.ContractId && !n.Deleted, cancellationToken)) return Bad<PcActivityDto>("گره WBS متعلق به این قرارداد نیست.");
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && w.ContractId == dto.ContractId && !w.Deleted, cancellationToken)) return Bad<PcActivityDto>("دستور کار متعلق به این قرارداد نیست.");
            if (dto.CalendarId.HasValue && !await _uow.Set<PcCalendar>().AnyAsync(c => c.Id == dto.CalendarId.Value && !c.Deleted, cancellationToken)) return Bad<PcActivityDto>("تقویم پیدا نشد.");
            switch (dto.ProgressMethod)
            {
                case PcProgressMethod.SupplyReceipt:
                case PcProgressMethod.ServiceDocs:
                    if (!dto.PurchaseOrderLineId.HasValue) return Bad<PcActivityDto>("برای این روش، ردیف سفارش خرید را مشخص کنید.");
                    break;
                case PcProgressMethod.ItpPoints:
                    if (!dto.ItpActivityId.HasValue) return Bad<PcActivityDto>("برای این روش، ردیف ITP را مشخص کنید.");
                    break;
                case PcProgressMethod.EngineeringIssue:
                    if (!dto.WorkOrderId.HasValue) return Bad<PcActivityDto>("برای پیشرفت مهندسی، دستور کار را مشخص کنید.");
                    break;
                case PcProgressMethod.Milestone:
                    if (dto.MilestoneEvent == PcMilestoneEvent.None) return Bad<PcActivityDto>("رویداد تکمیل نقطه عطف را مشخص کنید.");
                    break;
                case PcProgressMethod.Quantity:
                    if (!dto.PlannedQuantity.HasValue || dto.PlannedQuantity.Value <= 0) return Bad<PcActivityDto>("مقدار برنامه‌ای را وارد کنید.");
                    break;
            }
            if (dto.PurchaseOrderLineId.HasValue && !await _uow.Set<PurchaseOrderLine>().AnyAsync(l => l.Id == dto.PurchaseOrderLineId.Value && !l.PurchaseOrder.Deleted, cancellationToken)) return Bad<PcActivityDto>("ردیف سفارش خرید پیدا نشد.");
            if (dto.PurchaseRequestLineId.HasValue && !await _uow.Set<PurchaseRequestLine>().AnyAsync(l => l.Id == dto.PurchaseRequestLineId.Value && !l.PurchaseRequest.Deleted, cancellationToken)) return Bad<PcActivityDto>("ردیف درخواست خرید پیدا نشد.");
            if (dto.ItpActivityId.HasValue && !await _uow.Set<ItpActivity>().AnyAsync(l => l.Id == dto.ItpActivityId.Value, cancellationToken)) return Bad<PcActivityDto>("ردیف ITP پیدا نشد.");

            string code = Clean(dto.Code);
            DateTimeOffset now = DateTimeOffset.Now;
            PcActivity a;
            if (dto.Id.HasValue)
            {
                a = await _uow.Set<PcActivity>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (a == null) return PcText.NotFound<PcActivityDto>("فعالیت پیدا نشد.");
                if (a.ContractId != dto.ContractId) return Bad<PcActivityDto>("قرارداد فعالیت قابل تغییر نیست.");
                if (code == null) code = a.Code;
            }
            else
            {
                if (code == null)
                {
                    int count = await _uow.Set<PcActivity>().CountAsync(x => x.ContractId == dto.ContractId, cancellationToken);
                    code = "A" + (count + 1).ToString("0000", CultureInfo.InvariantCulture);
                }
                int sort = (await _uow.Set<PcActivity>().Where(x => x.ContractId == dto.ContractId).MaxAsync(x => (int?)x.Sort, cancellationToken) ?? 0) + 1;
                a = new PcActivity { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me, Sort = sort, Status = PcActivityStatus.NotStarted };
                _uow.Set<PcActivity>().Add(a);
            }
            if (await _uow.Set<PcActivity>().AnyAsync(x => x.ContractId == dto.ContractId && x.Code == code && !x.Deleted && x.Id != (dto.Id ?? 0), cancellationToken)) return Bad<PcActivityDto>("این کد فعالیت در قرارداد تکراری است.");
            a.Code = code; a.Title = Clean(dto.Title); a.WbsNodeId = dto.WbsNodeId; a.SourceType = Enum.IsDefined(typeof(PcSourceType), dto.SourceType) ? dto.SourceType : PcSourceType.Manual;
            a.SourceId = dto.SourceId; a.WorkOrderId = dto.WorkOrderId; a.DurationDays = dto.DurationDays; a.CalendarId = dto.CalendarId; a.Phase = dto.Phase;
            a.ResponsibleUnit = Clean(dto.ResponsibleUnit)?.ToUpperInvariant(); a.ConstraintType = dto.ConstraintType; a.ConstraintDate = dto.ConstraintDate?.Date;
            a.ProgressMethod = dto.ProgressMethod; a.Weight = dto.Weight; a.BudgetAtCompletion = dto.BudgetAtCompletion; a.PurchaseOrderLineId = dto.PurchaseOrderLineId;
            a.PurchaseRequestLineId = dto.PurchaseRequestLineId; a.ItpActivityId = dto.ItpActivityId; a.MilestoneEvent = dto.MilestoneEvent; a.PlannedQuantity = dto.PlannedQuantity;
            a.SerialItemId = dto.SerialItemId; a.LeadTimeDays = dto.LeadTimeDays; a.IsPackaging = dto.IsPackaging;
            a.ModifyDate = now; a.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcActivityDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(a));
        }

        public async Task<ResponseDto<bool>> DeleteActivityAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<bool>(PcText.NotAllowed);
            PcActivity a = await _uow.Set<PcActivity>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (a == null) return PcText.NotFound<bool>("فعالیت پیدا نشد.");
            if (a.Status != PcActivityStatus.NotStarted || a.ActualStart.HasValue) return Bad<bool>("فعالیت شروع‌شده حذف نمی‌شود.");
            if (await _uow.Set<PcBaselineActivity>().AnyAsync(x => x.ActivityId == id, cancellationToken)) return Bad<bool>("فعالیت در مبنای زمانی ثبت شده و حذف نمی‌شود؛ از مسیر درخواست تغییر اقدام کنید.");
            if (await _uow.Set<PcProgressSnapshotLine>().AnyAsync(x => x.ActivityId == id, cancellationToken) || await _uow.Set<PcManualProgress>().AnyAsync(x => x.ActivityId == id, cancellationToken)) return Bad<bool>("برای فعالیت پیشرفت ثبت شده و حذف نمی‌شود.");
            if (await _uow.Set<PcPaymentMilestone>().AnyAsync(x => x.ActivityId == id && !x.Deleted, cancellationToken)) return Bad<bool>("فعالیت به مرحله پرداخت وصل است.");
            List<PcDependency> deps = await _uow.Set<PcDependency>().Where(d => d.PredecessorId == id || d.SuccessorId == id).ToListAsync(cancellationToken);
            _uow.Set<PcDependency>().RemoveRange(deps);
            a.Delete(); a.ModifyDate = DateTimeOffset.Now; a.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<PcActivityDto>> ActivityActionAsync(PcActivityActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcActivityDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.UnitManagers + ",PMO")) return Bad<PcActivityDto>(PcText.NotAllowed);
            PcActivity a = await _uow.Set<PcActivity>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (a == null) return PcText.NotFound<PcActivityDto>("فعالیت پیدا نشد.");
            DateTime date = (dto.Date ?? DateTime.Today).Date;
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "start":
                    if (a.ActualStart.HasValue) return Bad<PcActivityDto>("فعالیت قبلاً شروع شده است.");
                    if (a.IsPackaging && a.WorkOrderId.HasValue)
                    {
                        bool released = await _uow.Set<ReleaseNote>().AnyAsync(r => r.WorkOrderId == a.WorkOrderId.Value && !r.Deleted && r.Status == ReleaseNoteStatus.Released, cancellationToken);
                        if (!released) return Bad<PcActivityDto>("شروع بسته‌بندی بدون برگه ترخیص کیفی صادرشده ممکن نیست (کنترل ترخیص).");
                    }
                    a.ActualStart = date; a.Status = PcActivityStatus.InProgress;
                    break;
                case "finish":
                    return Bad<PcActivityDto>("پایان فعالیت با پیشنهاد پیشرفت ۱۰۰ درصد و تایید برنامه‌ریزی (یا از رویدادهای سیستم) ثبت می‌شود.");
                case "hold":
                    if (Clean(dto.Reason) == null) return Bad<PcActivityDto>("دلیل توقف را بنویسید.");
                    a.Status = PcActivityStatus.OnHold; a.OnHoldReason = Clean(dto.Reason);
                    break;
                case "resume":
                    if (a.Status != PcActivityStatus.OnHold) return Bad<PcActivityDto>("فعالیت متوقف نیست.");
                    a.Status = a.ActualStart.HasValue ? PcActivityStatus.InProgress : PcActivityStatus.NotStarted; a.OnHoldReason = null;
                    break;
                default:
                    return Bad<PcActivityDto>("عملیات نامعتبر است.");
            }
            a.ModifyDate = now; a.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcActivityDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(a));
        }

        // ============================================================================ dependencies

        public async Task<string> CheckNoCycleAsync(int contractId, int predecessorId, int successorId, CancellationToken cancellationToken)
        {
            if (predecessorId == successorId) return "فعالیت نمی‌تواند پیش‌نیاز خودش باشد.";
            List<PcDependency> deps = await _uow.Set<PcDependency>().AsNoTracking().Where(d => d.ContractId == contractId).ToListAsync(cancellationToken);
            var links = deps.Select(d => new CpmLink { PredecessorId = d.PredecessorId, SuccessorId = d.SuccessorId, Type = d.Type, Lag = d.LagDays }).ToList();
            links.Add(new CpmLink { PredecessorId = predecessorId, SuccessorId = successorId, Type = PcDependencyType.FS });
            List<int> ids = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted).Select(a => a.Id).ToListAsync(cancellationToken);
            List<int> cycle = PcCpm.FindCycle(ids, links);
            if (cycle.Count == 0) return null;
            Dictionary<int, string> codes = await _uow.Set<PcActivity>().AsNoTracking().Where(a => cycle.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, cancellationToken);
            return "این وابستگی حلقه ایجاد می‌کند: " + string.Join(" ← ", cycle.Select(i => codes.TryGetValue(i, out string c) ? c : i.ToString(CultureInfo.InvariantCulture)));
        }

        private async Task<PcDependencyDto> DependencyDtoAsync(PcDependency d, CancellationToken cancellationToken)
        {
            var codes = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.Id == d.PredecessorId || a.Id == d.SuccessorId).Select(a => new { a.Id, a.Code }).ToListAsync(cancellationToken);
            return new PcDependencyDto
            {
                Id = d.Id, ContractId = d.ContractId, PredecessorId = d.PredecessorId, PredecessorCode = codes.FirstOrDefault(c => c.Id == d.PredecessorId)?.Code, SuccessorId = d.SuccessorId,
                SuccessorCode = codes.FirstOrDefault(c => c.Id == d.SuccessorId)?.Code, Type = d.Type, LagDays = d.LagDays, Origin = d.Origin,
            };
        }

        public async Task<ResponseDto<PagedList<PcDependencyDto>>> GetDependenciesAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            IQueryable<PcDependency> q = _uow.Set<PcDependency>().AsNoTracking();
            if (query.ContractId.HasValue) q = q.Where(d => d.ContractId == query.ContractId.Value);
            IQueryable<PcDependencyDto> projection = q.OrderBy(d => d.Id).Select(d => new PcDependencyDto
            {
                Id = d.Id, ContractId = d.ContractId, PredecessorId = d.PredecessorId, PredecessorCode = d.Predecessor.Code, SuccessorId = d.SuccessorId, SuccessorCode = d.Successor.Code,
                Type = d.Type, LagDays = d.LagDays, Origin = d.Origin,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcDependencyDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcDependencyDto>> SaveDependencyAsync(PcDependencyDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcDependencyDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<PcDependencyDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(PcDependencyType), dto.Type)) return Bad<PcDependencyDto>("نوع وابستگی نامعتبر است.");
            if (dto.LagDays < -3650 || dto.LagDays > 3650) return Bad<PcDependencyDto>("تاخیر/تقدم نامعتبر است.");
            PcActivity pred = await _uow.Set<PcActivity>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.PredecessorId && !a.Deleted, cancellationToken);
            PcActivity succ = await _uow.Set<PcActivity>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.SuccessorId && !a.Deleted, cancellationToken);
            if (pred == null || succ == null) return PcText.NotFound<PcDependencyDto>("فعالیت پیدا نشد.");
            if (pred.ContractId != succ.ContractId) return Bad<PcDependencyDto>("دو فعالیت باید در یک قرارداد باشند.");
            PcDependency d = null;
            if (dto.Id.HasValue)
            {
                d = await _uow.Set<PcDependency>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, cancellationToken);
                if (d == null) return PcText.NotFound<PcDependencyDto>("وابستگی پیدا نشد.");
            }
            if (await _uow.Set<PcDependency>().AnyAsync(x => x.PredecessorId == dto.PredecessorId && x.SuccessorId == dto.SuccessorId && x.Id != (dto.Id ?? 0), cancellationToken)) return Bad<PcDependencyDto>("بین این دو فعالیت وابستگی وجود دارد.");
            // cycle check on every save (the edited link is replaced by the new one)
            if (dto.PredecessorId == dto.SuccessorId) return Bad<PcDependencyDto>("فعالیت نمی‌تواند پیش‌نیاز خودش باشد.");
            List<PcDependency> deps = await _uow.Set<PcDependency>().AsNoTracking().Where(x => x.ContractId == pred.ContractId && x.Id != (dto.Id ?? 0)).ToListAsync(cancellationToken);
            var links = deps.Select(x => new CpmLink { PredecessorId = x.PredecessorId, SuccessorId = x.SuccessorId, Type = x.Type, Lag = x.LagDays }).ToList();
            links.Add(new CpmLink { PredecessorId = dto.PredecessorId, SuccessorId = dto.SuccessorId, Type = dto.Type, Lag = dto.LagDays });
            List<int> ids = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == pred.ContractId && !a.Deleted).Select(a => a.Id).ToListAsync(cancellationToken);
            List<int> cycle = PcCpm.FindCycle(ids, links);
            if (cycle.Count > 0)
            {
                Dictionary<int, string> codes = await _uow.Set<PcActivity>().AsNoTracking().Where(a => cycle.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, cancellationToken);
                return Bad<PcDependencyDto>("این وابستگی حلقه ایجاد می‌کند: " + string.Join(" ← ", cycle.Select(i => codes.TryGetValue(i, out string c) ? c : i.ToString(CultureInfo.InvariantCulture))));
            }
            if (d == null)
            {
                d = new PcDependency { ContractId = pred.ContractId };
                _uow.Set<PcDependency>().Add(d);
            }
            d.PredecessorId = dto.PredecessorId; d.SuccessorId = dto.SuccessorId; d.Type = dto.Type; d.LagDays = dto.LagDays; d.Origin = PcDependencyOrigin.Manual;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await DependencyDtoAsync(d, cancellationToken));
        }

        public async Task<ResponseDto<bool>> DeleteDependencyAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<bool>(PcText.NotAllowed);
            PcDependency d = await _uow.Set<PcDependency>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (d == null) return PcText.NotFound<bool>("وابستگی پیدا نشد.");
            _uow.Set<PcDependency>().Remove(d);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        // ============================================================================ CPM

        public async Task<ResponseDto<PcCpmRunDto>> RunCpmAsync(PcCpmRunRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcCpmRunDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm)) return Bad<PcCpmRunDto>(PcText.NotAllowed);
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken);
            if (contract == null) return PcText.NotFound<PcCpmRunDto>(PcText.ContractNotFound);
            List<PcActivity> acts = await _uow.Set<PcActivity>().Where(a => a.ContractId == dto.ContractId && !a.Deleted).OrderBy(a => a.Id).ToListAsync(cancellationToken);
            var run = new PcCpmRunDto { ContractId = dto.ContractId, Forecast = dto.Forecast };
            if (acts.Count == 0) { run.Message = "فعالیتی برای محاسبه وجود ندارد."; return ResponseDtoHelper.Ok(run); }

            PcContractTerms terms = await PcContext.TermsAsync(_uow, dto.ContractId, false, cancellationToken);
            PcWorkCalendar calendar = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken);
            DateTime start = PcContext.ScheduleStart(terms, contract);
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            DateTime dataDate = (dto.DataDate ?? DateTime.Today).Date;

            if (dto.Forecast) await _progress.ApplyAsync(dto.ContractId, cancellationToken);

            Dictionary<int, DateTime?> delivery = new Dictionary<int, DateTime?>();
            if (dto.Forecast)
            {
                List<int> lineIds = acts.Where(a => a.PurchaseOrderLineId.HasValue && a.Status != PcActivityStatus.Done).Select(a => a.PurchaseOrderLineId.Value).Distinct().ToList();
                var lines = await _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, Date = l.DeliveryDate ?? l.PurchaseOrder.DeliveryDate }).ToListAsync(cancellationToken);
                foreach (var l in lines) delivery[l.Id] = l.Date;
            }

            var nodes = acts.Select(a => new CpmNode
            {
                Id = a.Id, Duration = a.DurationDays, Constraint = a.ConstraintType, ConstraintDate = a.ConstraintDate,
                ActualStart = dto.Forecast ? a.ActualStart : null, ActualFinish = dto.Forecast && a.Status == PcActivityStatus.Done ? (a.ActualFinish ?? a.ActualStart) : null,
                Percent = a.PercentCache, FinishNotBefore = dto.Forecast && a.PurchaseOrderLineId.HasValue && delivery.TryGetValue(a.PurchaseOrderLineId.Value, out DateTime? del) ? del : null,
            }).ToList();
            List<PcDependency> deps = await _uow.Set<PcDependency>().AsNoTracking().Where(d => d.ContractId == dto.ContractId).ToListAsync(cancellationToken);
            var links = deps.Select(d => new CpmLink { PredecessorId = d.PredecessorId, SuccessorId = d.SuccessorId, Type = d.Type, Lag = d.LagDays }).ToList();

            CpmResult result = PcCpm.Run(nodes, links, calendar, start, dto.Forecast, dto.Forecast ? dataDate : (DateTime?)null, (int)settings[PcSettingKeys.CriticalFloat]);
            if (result.HasCycle)
            {
                run.HasCycle = true;
                run.CycleCodes = result.CycleNodes.Select(i => acts.FirstOrDefault(a => a.Id == i)?.Code ?? i.ToString(CultureInfo.InvariantCulture)).ToList();
                run.Message = "شبکه فعالیت‌ها حلقه دارد؛ محاسبه انجام نشد.";
                return ResponseDtoHelper.Ok(run);
            }

            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PcActivity a in acts)
            {
                if (!result.Items.TryGetValue(a.Id, out CpmItem item)) continue;
                if (dto.Forecast)
                {
                    a.ForecastStart = item.EarlyStart; a.ForecastFinish = item.EarlyFinish;
                    bool late = a.Status != PcActivityStatus.Done && a.EarlyFinish.HasValue && item.EarlyFinish > a.EarlyFinish.Value;
                    a.IsLate = late; a.LateKind = late ? "FINISH" : null;
                }
                else
                {
                    a.EarlyStart = item.EarlyStart; a.EarlyFinish = item.EarlyFinish; a.LateStart = item.LateStart; a.LateFinish = item.LateFinish;
                    a.TotalFloat = item.TotalFloat; a.FreeFloat = item.FreeFloat; a.IsCritical = item.Critical;
                    if (a.ProgressMethod == PcProgressMethod.SupplyReceipt || a.PurchaseRequestLineId.HasValue) a.NeedByDate = item.LateStart.AddDays(-Math.Max(0, a.LeadTimeDays));
                }
                a.ModifyDate = now; a.ModifierUserId = Me;
                run.Items.Add(new PcCpmItemDto
                {
                    ActivityId = a.Id, Code = a.Code, Title = a.Title, EarlyStart = item.EarlyStart, EarlyFinish = item.EarlyFinish, LateStart = item.LateStart, LateFinish = item.LateFinish,
                    TotalFloat = item.TotalFloat, FreeFloat = item.FreeFloat, Critical = item.Critical,
                });
            }
            run.ProjectStart = result.ProjectStart; run.ProjectFinish = result.ProjectFinish; run.ProjectFinishOffset = result.ProjectFinishOffset;
            run.CriticalPath = result.CriticalPath.Select(i => acts.First(a => a.Id == i).Code).ToList();

            if (!dto.Forecast && dto.Mirror) run.Mirrored = await MirrorToLegacyAsync(acts, result, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(run);
        }

        private async Task<CpmResult> PlanRunAsync(int contractId, List<PcActivity> acts, Dictionary<int, int> durationOverride, CancellationToken cancellationToken)
        {
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId && !c.Deleted, cancellationToken);
            if (contract == null || acts.Count == 0) return null;
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            PcWorkCalendar calendar = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken);
            DateTime start = PcContext.ScheduleStart(terms, contract);
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            var nodes = acts.Select(a => new CpmNode
            {
                Id = a.Id, Duration = durationOverride != null && durationOverride.TryGetValue(a.Id, out int dur) ? Math.Max(0, dur) : a.DurationDays, Constraint = a.ConstraintType, ConstraintDate = a.ConstraintDate,
            }).ToList();
            List<PcDependency> deps = await _uow.Set<PcDependency>().AsNoTracking().Where(d => d.ContractId == contractId).ToListAsync(cancellationToken);
            var links = deps.Select(d => new CpmLink { PredecessorId = d.PredecessorId, SuccessorId = d.SuccessorId, Type = d.Type, Lag = d.LagDays }).ToList();
            CpmResult result = PcCpm.Run(nodes, links, calendar, start, false, null, (int)settings[PcSettingKeys.CriticalFloat]);
            return result.HasCycle ? null : result;
        }

        public async Task<CpmResult> PreviewPlanAsync(int contractId, Dictionary<int, int> durationOverride, CancellationToken cancellationToken)
        {
            List<PcActivity> acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted).OrderBy(a => a.Id).ToListAsync(cancellationToken);
            return await PlanRunAsync(contractId, acts, durationOverride, cancellationToken);
        }

        public async Task<CpmResult> ApplyPlanNoSaveAsync(int contractId, CancellationToken cancellationToken)
        {
            List<PcActivity> acts = await _uow.Set<PcActivity>().Where(a => a.ContractId == contractId && !a.Deleted).OrderBy(a => a.Id).ToListAsync(cancellationToken);
            CpmResult result = await PlanRunAsync(contractId, acts, null, cancellationToken);
            if (result == null) return null;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PcActivity a in acts)
            {
                if (!result.Items.TryGetValue(a.Id, out CpmItem item)) continue;
                a.EarlyStart = item.EarlyStart; a.EarlyFinish = item.EarlyFinish; a.LateStart = item.LateStart; a.LateFinish = item.LateFinish;
                a.TotalFloat = item.TotalFloat; a.FreeFloat = item.FreeFloat; a.IsCritical = item.Critical;
                if (a.ProgressMethod == PcProgressMethod.SupplyReceipt || a.PurchaseRequestLineId.HasValue) a.NeedByDate = item.LateStart.AddDays(-Math.Max(0, a.LeadTimeDays));
                a.ModifyDate = now; a.ModifierUserId = Me;
            }
            await MirrorToLegacyAsync(acts, result, cancellationToken);
            return result;
        }

        /// <summary>Writes the computed dates back to the old process / assembly / work order date fields so the legacy screens show them.</summary>
        private async Task<int> MirrorToLegacyAsync(List<PcActivity> acts, CpmResult result, CancellationToken cancellationToken)
        {
            int mirrored = 0;
            List<int> processIds = acts.Where(a => a.SourceType == PcSourceType.Process && a.SourceId.HasValue).Select(a => a.SourceId.Value).ToList();
            Dictionary<int, ProcessEntity> processes = (await _uow.Set<ProcessEntity>().Where(p => processIds.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id);
            foreach (PcActivity a in acts.Where(a => a.SourceType == PcSourceType.Process && a.SourceId.HasValue))
            {
                if (!processes.TryGetValue(a.SourceId.Value, out ProcessEntity p) || !result.Items.TryGetValue(a.Id, out CpmItem item)) continue;
                p.ProgramStartDate = Dto(item.EarlyStart); p.ProgramEndDate = Dto(item.EarlyFinish);
                mirrored++;
            }
            List<PcWbsNode> nodes = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == acts[0].ContractId && !n.Deleted).ToListAsync(cancellationToken);
            Dictionary<int, PcWbsNode> nodeById = nodes.ToDictionary(n => n.Id);
            var asmRange = new Dictionary<int, (DateTime Start, DateTime Finish)>();
            var woRange = new Dictionary<int, DateTime>();
            foreach (PcActivity a in acts)
            {
                if (!result.Items.TryGetValue(a.Id, out CpmItem item)) continue;
                if (a.WorkOrderId.HasValue && a.SourceType == PcSourceType.Process)
                    woRange[a.WorkOrderId.Value] = woRange.TryGetValue(a.WorkOrderId.Value, out DateTime w) ? (item.EarlyStart < w ? item.EarlyStart : w) : item.EarlyStart;
                int? cursor = a.WbsNodeId;
                for (int i = 0; i < 20 && cursor.HasValue && nodeById.TryGetValue(cursor.Value, out PcWbsNode node); i++)
                {
                    if (node.AssemblyId.HasValue && node.NodeType == PcWbsNodeType.Assembly)
                    {
                        if (asmRange.TryGetValue(node.AssemblyId.Value, out var r)) asmRange[node.AssemblyId.Value] = (item.EarlyStart < r.Start ? item.EarlyStart : r.Start, item.EarlyFinish > r.Finish ? item.EarlyFinish : r.Finish);
                        else asmRange[node.AssemblyId.Value] = (item.EarlyStart, item.EarlyFinish);
                    }
                    cursor = node.ParentId;
                }
            }
            List<int> asmIds = asmRange.Keys.ToList();
            foreach (AssemblyEntity asm in await _uow.Set<AssemblyEntity>().Where(x => asmIds.Contains(x.Id)).ToListAsync(cancellationToken))
            {
                asm.StartDate = Dto(asmRange[asm.Id].Start); asm.EndDate = Dto(asmRange[asm.Id].Finish);
                mirrored++;
            }
            List<int> woIds = woRange.Keys.ToList();
            foreach (WorkOrder wo in await _uow.Set<WorkOrder>().Where(x => woIds.Contains(x.Id)).ToListAsync(cancellationToken))
            {
                wo.SetStartDate(new DateTimeOffset(DateTime.SpecifyKind(woRange[wo.Id].Date, DateTimeKind.Utc)));
                mirrored++;
            }
            return mirrored;
        }

        // ============================================================================ weekly executive plan

        public async Task<ResponseDto<PagedList<PcExecutivePlanRowDto>>> GetExecutivePlanAsync(PcExecutivePlanQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcExecutivePlanQuery();
            if (!query.ContractId.HasValue) return Bad<PagedList<PcExecutivePlanRowDto>>("قرارداد را مشخص کنید.");
            DateTime from = (query.From ?? DateTime.Today).Date;
            DateTime to = from.AddDays(Math.Max(1, Math.Min(query.Days <= 0 ? 14 : query.Days, 366)));
            string unit = Clean(query.ResponsibleUnit)?.ToUpperInvariant();
            IQueryable<PcActivity> q = _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == query.ContractId.Value && !a.Deleted && a.Status != PcActivityStatus.Done && a.DurationDays > 0
                && (a.ForecastStart ?? a.EarlyStart) != null && (a.ForecastStart ?? a.EarlyStart) <= to && (a.ForecastFinish ?? a.EarlyFinish) >= from);
            if (unit != null) q = q.Where(a => a.ResponsibleUnit == unit);
            IQueryable<PcExecutivePlanRowDto> projection = q.OrderBy(a => a.ResponsibleUnit).ThenBy(a => a.ForecastStart ?? a.EarlyStart).ThenBy(a => a.Id).Select(a => new PcExecutivePlanRowDto
            {
                ActivityId = a.Id, Code = a.Code, Title = a.Title, ResponsibleUnit = a.ResponsibleUnit, Phase = a.Phase, Start = a.ForecastStart ?? a.EarlyStart, Finish = a.ForecastFinish ?? a.EarlyFinish,
                Percent = a.PercentCache, Status = a.Status, IsCritical = a.IsCritical, IsLate = a.IsLate, TotalFloat = a.TotalFloat,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcExecutivePlanRowDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        // ============================================================================ MS Project XML (export only)

        public async Task<ResponseDto<string>> ExportMsProjectXmlAsync(int contractId, CancellationToken cancellationToken)
        {
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId && !c.Deleted, cancellationToken);
            if (contract == null) return PcText.NotFound<string>(PcText.ContractNotFound);
            List<PcActivity> acts = await _uow.Set<PcActivity>().AsNoTracking().Include(a => a.WbsNode).Where(a => a.ContractId == contractId && !a.Deleted).OrderBy(a => a.Sort).ThenBy(a => a.Id).ToListAsync(cancellationToken);
            if (acts.Count == 0) return Bad<string>("فعالیتی برای خروجی وجود ندارد.");
            if (acts.Any(a => !a.EarlyStart.HasValue)) return Bad<string>("ابتدا محاسبه مسیر بحرانی را اجرا کنید.");
            List<PcDependency> deps = await _uow.Set<PcDependency>().AsNoTracking().Where(d => d.ContractId == contractId).ToListAsync(cancellationToken);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            PcWorkCalendar cal = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken);
            XNamespace ns = "http://schemas.microsoft.com/project";
            string F(DateTime d, string time) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T" + time;
            DateTime projStart = acts.Min(a => a.EarlyStart.Value), projFinish = acts.Max(a => a.EarlyFinish.Value);
            var uid = new Dictionary<int, int>();
            int k = 0;
            foreach (PcActivity a in acts) uid[a.Id] = ++k;

            var weekDays = new List<XElement>();
            for (int d = 0; d < 7; d++)
            {
                bool working = cal.IsWorking(NextDay(d));
                var wd = new XElement(ns + "WeekDay", new XElement(ns + "DayType", d + 1), new XElement(ns + "DayWorking", working ? 1 : 0));
                if (working) wd.Add(new XElement(ns + "WorkingTimes", new XElement(ns + "WorkingTime", new XElement(ns + "FromTime", "08:00:00"), new XElement(ns + "ToTime", "16:00:00"))));
                weekDays.Add(wd);
            }
            var tasks = new XElement(ns + "Tasks");
            tasks.Add(new XElement(ns + "Task", new XElement(ns + "UID", 0), new XElement(ns + "ID", 0), new XElement(ns + "Name", contract.Title ?? contract.Code ?? "Project"),
                new XElement(ns + "Start", F(projStart, "08:00:00")), new XElement(ns + "Finish", F(projFinish, "16:00:00")), new XElement(ns + "Summary", 1), new XElement(ns + "OutlineLevel", 0)));
            foreach (PcActivity a in acts)
            {
                var t = new XElement(ns + "Task",
                    new XElement(ns + "UID", uid[a.Id]), new XElement(ns + "ID", uid[a.Id]), new XElement(ns + "Name", a.Title),
                    new XElement(ns + "WBS", a.WbsNode?.Code ?? a.Code), new XElement(ns + "OutlineLevel", 1),
                    new XElement(ns + "Start", F(a.EarlyStart.Value, "08:00:00")), new XElement(ns + "Finish", F(a.EarlyFinish.Value, "16:00:00")),
                    new XElement(ns + "Duration", "PT" + (a.DurationDays * 8).ToString(CultureInfo.InvariantCulture) + "H0M0S"),
                    new XElement(ns + "Milestone", a.DurationDays == 0 ? 1 : 0), new XElement(ns + "Critical", a.IsCritical ? 1 : 0),
                    new XElement(ns + "PercentComplete", ((int)Math.Round(a.PercentCache, 0)).ToString(CultureInfo.InvariantCulture)));
                foreach (PcDependency d in deps.Where(x => x.SuccessorId == a.Id && uid.ContainsKey(x.PredecessorId)).OrderBy(x => x.Id))
                {
                    int msType = d.Type == PcDependencyType.FF ? 0 : d.Type == PcDependencyType.FS ? 1 : d.Type == PcDependencyType.SF ? 2 : 3;
                    t.Add(new XElement(ns + "PredecessorLink", new XElement(ns + "PredecessorUID", uid[d.PredecessorId]), new XElement(ns + "Type", msType),
                        new XElement(ns + "LinkLag", (d.LagDays * 8 * 60 * 10).ToString(CultureInfo.InvariantCulture)), new XElement(ns + "LagFormat", 7)));
                }
                tasks.Add(t);
            }
            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ns + "Project",
                    new XElement(ns + "Name", contract.Code ?? "Project"), new XElement(ns + "Title", contract.Title ?? contract.Code),
                    new XElement(ns + "StartDate", F(projStart, "08:00:00")), new XElement(ns + "FinishDate", F(projFinish, "16:00:00")),
                    new XElement(ns + "CalendarUID", 1), new XElement(ns + "MinutesPerDay", 480),
                    new XElement(ns + "Calendars", new XElement(ns + "Calendar", new XElement(ns + "UID", 1), new XElement(ns + "Name", "Standard"), new XElement(ns + "IsBaseCalendar", 1), new XElement(ns + "WeekDays", weekDays))),
                    tasks));
            return ResponseDtoHelper.Ok(doc.Declaration + Environment.NewLine + doc.Root);
        }

        /// <summary>A date whose DayOfWeek is the given number (2024-01-07 is a Sunday).</summary>
        private static DateTime NextDay(int dayOfWeek) => new DateTime(2024, 1, 7).AddDays(dayOfWeek);

        // ============================================================================ need-by dates and notices

        private async Task<List<PcNeedByRowDto>> NeedByRowsAsync(int contractId, bool apply, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            int longLead = (int)settings[PcSettingKeys.LongLeadDays];
            List<PcActivity> acts = await _uow.Set<PcActivity>().Where(a => a.ContractId == contractId && !a.Deleted && (a.ProgressMethod == PcProgressMethod.SupplyReceipt || a.PurchaseRequestLineId != null)).OrderBy(a => a.Sort).ToListAsync(cancellationToken);
            Dictionary<int, PurchaseRequestLine> lines = new Dictionary<int, PurchaseRequestLine>();
            if (apply)
            {
                List<int> ids = acts.Where(a => a.PurchaseRequestLineId.HasValue).Select(a => a.PurchaseRequestLineId.Value).ToList();
                lines = (await _uow.Set<PurchaseRequestLine>().Include(l => l.PurchaseRequest).Where(l => ids.Contains(l.Id)).ToListAsync(cancellationToken)).ToDictionary(l => l.Id);
            }
            var rows = new List<PcNeedByRowDto>();
            foreach (PcActivity a in acts)
            {
                bool applied = false;
                if (apply && a.NeedByDate.HasValue && a.PurchaseRequestLineId.HasValue && lines.TryGetValue(a.PurchaseRequestLineId.Value, out PurchaseRequestLine line) && line.PurchaseRequest.Status == PurchaseRequestStatus.Draft)
                {
                    line.NeededBy = a.NeedByDate.Value; applied = true;
                }
                rows.Add(new PcNeedByRowDto
                {
                    ActivityId = a.Id, Code = a.Code, Title = a.Title, LateStart = a.LateStart, LeadTimeDays = a.LeadTimeDays, NeedByDate = a.NeedByDate, PurchaseRequestLineId = a.PurchaseRequestLineId,
                    AppliedToRequest = applied, LongLead = a.LeadTimeDays >= longLead,
                });
            }
            return rows;
        }

        public async Task<ResponseDto<PagedList<PcNeedByRowDto>>> GetNeedByAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            if (!query.ContractId.HasValue) return Bad<PagedList<PcNeedByRowDto>>("قرارداد را مشخص کنید.");
            List<PcNeedByRowDto> rows = await NeedByRowsAsync(query.ContractId.Value, false, cancellationToken);
            return ResponseDtoHelper.Ok(await PagedList<PcNeedByRowDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<List<PcNeedByRowDto>>> ApplyNeedByAsync(PcNeedByApplyDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<List<PcNeedByRowDto>>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<List<PcNeedByRowDto>>(PcText.NotAllowed);
            List<PcNeedByRowDto> rows = await NeedByRowsAsync(dto.ContractId, true, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(rows);
        }

        public async Task<ResponseDto<PagedList<PcNoticeDraftDto>>> GetNoticeDraftsAsync(PcNoticeQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcNoticeQuery();
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            int look = query.LookAheadDays ?? (int)settings[PcSettingKeys.NoticeLookAheadDays];
            DateTime today = DateTime.Today;
            DateTime horizon = today.AddDays(Math.Max(1, look));
            IQueryable<PcActivity> aq = _uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.ItpActivityId != null && a.Status != PcActivityStatus.Done);
            if (query.ContractId.HasValue) aq = aq.Where(a => a.ContractId == query.ContractId.Value);
            if (query.WorkOrderId.HasValue) aq = aq.Where(a => a.WorkOrderId == query.WorkOrderId.Value);
            var acts = await aq.Select(a => new { a.Id, a.WorkOrderId, a.ItpActivityId, Start = a.ForecastStart ?? a.EarlyStart }).ToListAsync(cancellationToken);
            List<int> itpActIds = acts.Select(a => a.ItpActivityId.Value).ToList();
            var itpRows = await _uow.Set<ItpActivity>().AsNoTracking().Where(i => itpActIds.Contains(i.Id) && i.Status != ItpActivityStatus.Done && i.Status != ItpActivityStatus.Waived && i.Status != ItpActivityStatus.NotApplicable)
                .Select(i => new { i.Id, i.ItpId, i.Stage, i.Activity, i.PointClient, i.PointTpi, i.NotificationSentAt, Number = i.Itp.Number, i.Itp.NoticeDays }).ToListAsync(cancellationToken);
            var drafts = new List<PcNoticeDraftDto>();
            foreach (var a in acts)
            {
                var i = itpRows.FirstOrDefault(x => x.Id == a.ItpActivityId.Value);
                if (i == null || !a.Start.HasValue || a.Start.Value > horizon.AddDays(i.NoticeDays) || i.NotificationSentAt.HasValue) continue;
                string party = i.PointClient == ItpPoint.W || i.PointClient == ItpPoint.H ? "کارفرما" : i.PointTpi == ItpPoint.W || i.PointTpi == ItpPoint.H ? "بازرس شخص ثالث" : null;
                if (party == null) continue;
                DateTime deadline = a.Start.Value.AddDays(-i.NoticeDays);
                if (deadline > horizon) continue;
                drafts.Add(new PcNoticeDraftDto
                {
                    ItpId = i.ItpId, ItpNumber = i.Number, ItpActivityId = i.Id, ActivityId = a.Id, WorkOrderId = a.WorkOrderId ?? 0, Stage = i.Stage, Activity = i.Activity, Party = party, PlannedDate = a.Start,
                    NoticeDeadline = deadline, NoticeLate = deadline < today,
                    Text = $"به {party}: بازرسی «{i.Activity}» ({i.Stage}) طبق برنامه بازرسی و آزمون {i.Number} در تاریخ {a.Start.Value:yyyy-MM-dd} انجام می‌شود. لطفاً حداقل {i.NoticeDays} روز قبل برای حضور اعلام نظر کنید. (پیش‌نویس سیستم؛ ارسال با کنترل کیفی)",
                });
            }
            drafts = drafts.OrderBy(d => d.NoticeDeadline).ThenBy(d => d.ItpActivityId).ToList();
            return ResponseDtoHelper.Ok(await PagedList<PcNoticeDraftDto>.CreateAsync(drafts, query.PageNumber, query.PageSize, cancellationToken));
        }
    }
}
