using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Agents
{
    public interface IAgentBoardService
    {
        Task<ResponseDto<PagedList<AgentTaskDto>>> GetTasksAsync(AgentTaskQuery query, CancellationToken ct);
        Task<ResponseDto<AgentTaskDto>> GetTaskAsync(int id, CancellationToken ct);

        /// <summary>PMO: a failed task goes back to the queue with its attempts reset.</summary>
        Task<ResponseDto<AgentTaskDto>> RetryAsync(AgentRetryDto dto, CancellationToken ct);

        /// <summary>The master view: one row per contract that has agent work, plus a row for the whole company, thirteen cells each, and the stuck items.</summary>
        Task<ResponseDto<AgentBoardDto>> GetBoardAsync(int? contractId, CancellationToken ct);

        Task<ResponseDto<List<AgentStepDto>>> GetStepsAsync(CancellationToken ct);

        /// <summary>PMO only (audited): switches a step on or off. The agent that owns a step that is off hands its tasks to people.</summary>
        Task<ResponseDto<AgentStepDto>> EnableStepAsync(AgentStepEnableDto dto, CancellationToken ct);
    }

    /// <summary>The tasks, the master board and the step switches. PMO, CEO and the auditor read; the PMO retries and switches.</summary>
    public class AgentBoardService : IAgentBoardService
    {
        public const string CompanyLabel = "عمومی شرکت";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAgentStepCatalog _catalog;
        private readonly IAiAudit _audit;
        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _http;
        private readonly INotificationService _notify;

        private readonly List<IAgentStationProbe> _probes;

        private bool IsSeat() => AiActing.IsActing || AiSeatNames.IsSeatUser(_http?.HttpContext?.User?.Identity?.Name);

        public AgentBoardService(IUnitOfWork uow, IInventoryUserContext user, IAgentStepCatalog catalog, IAiAudit audit, Microsoft.AspNetCore.Http.IHttpContextAccessor http = null, INotificationService notify = null, IEnumerable<IAgentStationProbe> probes = null)
        {
            _probes = probes?.ToList() ?? new List<IAgentStationProbe>();
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _http = http;
            _notify = notify;
        }

        // ------------------------------------------------------------------------------------------ tasks

        public async Task<ResponseDto<PagedList<AgentTaskDto>>> GetTasksAsync(AgentTaskQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(AgentRoles.BoardReaders)) return AgentText.Bad<PagedList<AgentTaskDto>>(AgentText.NotAllowed);
            query ??= new AgentTaskQuery();
            IQueryable<AgentTask> q = _uow.Set<AgentTask>().AsNoTracking().Where(t => !t.Deleted);
            if (query.State.HasValue) q = q.Where(t => t.State == query.State.Value);
            string agent = AgentText.Clean(query.Agent)?.ToUpperInvariant();
            if (agent != null) q = q.Where(t => t.AgentCode == agent);
            if (query.Station.HasValue) q = q.Where(t => t.Station == query.Station.Value);
            if (query.NoContract == true) q = q.Where(t => t.ContractId == null);
            else if (query.ContractId.HasValue) q = q.Where(t => t.ContractId == query.ContractId.Value);
            string step = AgentText.Clean(query.StepCode)?.ToUpperInvariant();
            if (step != null) q = q.Where(t => t.StepCode == step);
            if (query.OpenOnly == true) q = q.Where(t => t.State < AgentTaskState.Done);
            IQueryable<AgentTaskDto> proj = q.OrderByDescending(t => t.Id).Select(t => new AgentTaskDto
            {
                Id = t.Id, StepCode = t.StepCode, Station = t.Station, AgentCode = t.AgentCode, Kind = t.Kind, State = t.State, Title = t.Title, TargetType = t.TargetType, TargetId = t.TargetId,
                TargetNumber = t.TargetNumber, ContractId = t.ContractId, WorkOrderId = t.WorkOrderId, Summary = t.Summary, OutputType = t.OutputType, OutputId = t.OutputId, OutputNumber = t.OutputNumber,
                GateRoles = t.GateRoles, GateUserId = t.GateUserId, Link = t.Link, CreatedAt = t.CreateDate, DueAt = t.DueAt, IsFallback = t.IsFallback, LastError = t.LastError, ReturnReason = t.ReturnReason,
                AttemptCount = t.AttemptCount, NextAttemptAt = t.NextAttemptAt, StartedAt = t.StartedAt, ClosedAt = t.ClosedAt, ClosedReason = t.ClosedReason, ReturnedAt = t.ReturnedAt,
                ReturnedByUserId = t.ReturnedByUserId, EscalatedAt = t.EscalatedAt,
            });
            PagedList<AgentTaskDto> page = await PagedList<AgentTaskDto>.CreateAsync(proj, query.PageNumber, query.PageSize <= 0 ? 0 : Math.Min(query.PageSize, 200), ct);
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (AgentTaskDto d in page) AgentTaskView.Fill(d, now);
            return ResponseDtoHelper.Ok(page);
        }

        private async Task<AgentTaskDto> DetailAsync(int id, CancellationToken ct)
        {
            AgentTask t = await _uow.Set<AgentTask>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (t == null) return null;
            var d = new AgentTaskDto
            {
                Id = t.Id, StepCode = t.StepCode, Station = t.Station, AgentCode = t.AgentCode, Kind = t.Kind, State = t.State, Title = t.Title, TargetType = t.TargetType, TargetId = t.TargetId,
                TargetNumber = t.TargetNumber, ContractId = t.ContractId, WorkOrderId = t.WorkOrderId, Summary = t.Summary, OutputType = t.OutputType, OutputId = t.OutputId, OutputNumber = t.OutputNumber,
                GateRoles = t.GateRoles, GateUserId = t.GateUserId, Link = t.Link, CreatedAt = t.CreateDate, DueAt = t.DueAt, IsFallback = t.IsFallback, LastError = t.LastError, ReturnReason = t.ReturnReason,
                AttemptCount = t.AttemptCount, NextAttemptAt = t.NextAttemptAt, StartedAt = t.StartedAt, ClosedAt = t.ClosedAt, ClosedReason = t.ClosedReason, ReturnedAt = t.ReturnedAt,
                ReturnedByUserId = t.ReturnedByUserId, EscalatedAt = t.EscalatedAt,
            };
            AgentTaskView.Fill(d, DateTimeOffset.Now);
            List<AgentTaskEvent> events = await _uow.Set<AgentTaskEvent>().AsNoTracking().Where(e => e.TaskId == id).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, events.Where(e => e.SeatCode == null && e.UserId > 0).Select(e => (int?)e.UserId), ct);
            d.Events = events.Select(e => new AgentTaskEventDto
            {
                Id = e.Id, At = e.At, AtText = AiText.Stamp(e.At), FromState = e.FromState, ToState = e.ToState, UserId = e.UserId, SeatCode = e.SeatCode, Note = e.Note,
                UserName = e.SeatCode != null ? (AiSeatCatalog.Find(e.SeatCode)?.Title ?? e.SeatCode) : (e.UserId > 0 ? names.Name(e.UserId) : null),
            }).ToList();
            return d;
        }

        public async Task<ResponseDto<AgentTaskDto>> GetTaskAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(AgentRoles.BoardReaders)) return AgentText.Bad<AgentTaskDto>(AgentText.NotAllowed);
            AgentTaskDto d = await DetailAsync(id, ct);
            return d == null ? AgentText.NotFound<AgentTaskDto>(AgentText.TaskNotFound) : ResponseDtoHelper.Ok(d);
        }

        public async Task<ResponseDto<AgentTaskDto>> RetryAsync(AgentRetryDto dto, CancellationToken ct)
        {
            if (IsSeat() || !_user.IsInRole(AgentRoles.Admins)) return AgentText.Bad<AgentTaskDto>(AgentText.NotAllowed);
            AgentTask t = dto == null ? null : await _uow.Set<AgentTask>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (t == null) return AgentText.NotFound<AgentTaskDto>(AgentText.TaskNotFound);
            if (t.State != AgentTaskState.Failed) return AgentText.Bad<AgentTaskDto>(AgentText.OnlyFailedCanRetry);
            DateTimeOffset now = DateTimeOffset.Now;
            bool alerted = t.EscalatedAt.HasValue;
            t.State = AgentTaskState.Queued; t.AttemptCount = 0; t.NextAttemptAt = null; t.LastError = null; t.EscalatedAt = null; t.StartedAt = null; t.ClosedAt = null; t.ClosedReason = null;
            _uow.Set<AgentTaskEvent>().Add(new AgentTaskEvent { Task = t, At = now, FromState = AgentTaskState.Failed, ToState = AgentTaskState.Queued, UserId = _user.UserId, Note = "اجرای دوباره توسط PMO" });
            _audit.Add(AiAuditKinds.Agent, $"task {t.Id.ToString(CultureInfo.InvariantCulture)} step {t.StepCode} retried by the PMO", userId: _user.UserId);
            await _uow.SaveChangesAsync(ct);
            if (alerted && _notify != null) await _notify.CloseAsync(AgentNotices.AlertKey(t.Id), "کار دوباره در صف قرار گرفت.", ct);
            return ResponseDtoHelper.Ok(await DetailAsync(t.Id, ct));
        }

        // ------------------------------------------------------------------------------------------ the board

        private static AgentCellStatus CellOf(AgentTask t)
        {
            switch (t.State)
            {
                case AgentTaskState.Failed: return AgentCellStatus.Failed;
                case AgentTaskState.WaitingHumanGate: return AgentCellStatus.WaitingGate;
                case AgentTaskState.WaitingPhysical: return AgentCellStatus.WaitingPhysical;
                case AgentTaskState.WaitingInput: return AgentCellStatus.WaitingInput;
                default: return AgentCellStatus.Agent;
            }
        }

        public async Task<ResponseDto<AgentBoardDto>> GetBoardAsync(int? contractId, CancellationToken ct)
        {
            if (!_user.IsInRole(AgentRoles.BoardReaders)) return AgentText.Bad<AgentBoardDto>(AgentText.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            IQueryable<AgentTask> all = _uow.Set<AgentTask>().AsNoTracking().Where(t => !t.Deleted);
            if (contractId.HasValue) all = all.Where(t => t.ContractId == contractId.Value);

            List<AgentTask> open = await all.Where(t => t.State < AgentTaskState.Done).ToListAsync(ct);
            var closedKeys = await all.Where(t => t.State >= AgentTaskState.Done).Select(t => new { t.ContractId, t.Station }).Distinct().ToListAsync(ct);

            var contractIds = new SortedSet<int>();
            bool general = false;
            foreach (AgentTask t in open) { if (t.ContractId.HasValue) contractIds.Add(t.ContractId.Value); else general = true; }
            foreach (var k in closedKeys) { if (k.ContractId.HasValue) contractIds.Add(k.ContractId.Value); else general = true; }
            if (contractId.HasValue) { contractIds.Add(contractId.Value); general = false; }

            Dictionary<int, string> labels = await LabelsAsync(contractIds, ct);
            // the stations that know when they are complete (from the documents): contract ids per station
            var complete = new Dictionary<int, HashSet<int>>();
            foreach (IAgentStationProbe probe in _probes)
            {
                try { complete[probe.Station] = await probe.CompleteContractsAsync(_uow, contractIds.ToList(), ct); }
                catch (Exception) when (!ct.IsCancellationRequested) { /* a probe that fails leaves its station as it was */ }
            }
            var board = new AgentBoardDto { GeneratedAt = now };
            for (int s = AgentStations.Min; s <= AgentStations.Max; s++) board.Stations.Add(new AgentStationDto { Number = s, Title = AgentStations.Title(s) });

            AgentBoardRowDto Row(int? id, string label)
            {
                var row = new AgentBoardRowDto { ContractId = id, Label = label };
                for (int s = AgentStations.Min; s <= AgentStations.Max; s++)
                {
                    List<AgentTask> cellTasks = open.Where(t => t.ContractId == id && t.Station == s).ToList();
                    var cell = new AgentBoardCellDto { Station = s };
                    if (cellTasks.Count == 0)
                    {
                        cell.Status = closedKeys.Any(k => k.ContractId == id && k.Station == s) ? AgentCellStatus.Idle : AgentCellStatus.NotStarted;
                        if (id.HasValue && complete.TryGetValue(s, out HashSet<int> done) && done.Contains(id.Value)) cell.Status = AgentCellStatus.Done;
                    }
                    else
                    {
                        // the worst status wins; inside it the oldest task
                        AgentTask worst = cellTasks.OrderByDescending(CellOf).ThenBy(t => t.CreateDate).ThenBy(t => t.Id).First();
                        cell.Status = CellOf(worst); cell.OpenCount = cellTasks.Count; cell.TaskId = worst.Id; cell.Title = worst.Title;
                        cell.WaitingOn = worst.State == AgentTaskState.Failed ? AgentRoles.Admins : worst.GateRoles;
                        cell.OldestHours = (int)Math.Max(0, (now - cellTasks.Min(t => t.CreateDate)).TotalHours);
                    }
                    row.Cells.Add(cell);
                }
                return row;
            }

            if (general) board.Rows.Add(Row(null, CompanyLabel));
            foreach (int id in contractIds) board.Rows.Add(Row(id, labels.TryGetValue(id, out string label) ? label : "قرارداد " + id.ToString(CultureInfo.InvariantCulture)));

            foreach (AgentTask t in open.OrderBy(t => t.CreateDate).ThenBy(t => t.Id))
            {
                AgentStuckReason? reason = null;
                if (t.State == AgentTaskState.Failed) reason = AgentStuckReason.Failed;
                else if (AgentTaskView.IsWaiting(t.State) && t.DueAt.HasValue && t.DueAt.Value < now) reason = AgentStuckReason.GateOverdue;
                else if (t.IsFallback) reason = AgentStuckReason.AgentOff;
                if (reason == null) continue;
                board.Stuck.Add(new AgentStuckDto
                {
                    TaskId = t.Id, Reason = reason.Value, Station = t.Station, StationTitle = AgentStations.Title(t.Station), StepCode = t.StepCode, Title = t.Title, ContractId = t.ContractId,
                    ContractLabel = t.ContractId.HasValue ? (labels.TryGetValue(t.ContractId.Value, out string l) ? l : null) : CompanyLabel, WaitingOn = t.State == AgentTaskState.Failed ? AgentRoles.Admins : t.GateRoles,
                    AgeHours = (int)Math.Max(0, (now - t.CreateDate).TotalHours), Error = t.LastError, Link = t.Link,
                });
            }
            board.Stuck = board.Stuck.OrderBy(s => s.Reason).ThenByDescending(s => s.AgeHours).ThenBy(s => s.TaskId).ToList();
            return ResponseDtoHelper.Ok(board);
        }

        private async Task<Dictionary<int, string>> LabelsAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            int[] list = ids.ToArray();
            if (list.Length == 0) return new Dictionary<int, string>();
            var rows = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => list.Contains(c.Id)).Select(c => new { c.Id, c.InternalContractNo, c.Code, c.Title }).ToListAsync(ct);
            return rows.ToDictionary(r => r.Id, r => ((AgentText.Clean(r.InternalContractNo) ?? AgentText.Clean(r.Code) ?? "قرارداد " + r.Id.ToString(CultureInfo.InvariantCulture)) + (string.IsNullOrWhiteSpace(r.Title) ? string.Empty : " " + r.Title.Trim())));
        }

        // ------------------------------------------------------------------------------------------ steps

        private static AgentStepDto StepDto(IAgentStep s, AgentStepSetting setting, bool seatOn, int open) =>
            new AgentStepDto
            {
                Code = s.Code, Station = s.Station, StationTitle = AgentStations.Title(s.Station), AgentCode = s.AgentCode.ToUpperInvariant(), Title = s.Title, Kind = s.Kind, FallbackRoles = s.FallbackRoles,
                Enabled = setting.Enabled, SeatEnabled = seatOn, MaxAttempts = setting.MaxAttempts, GateDueHours = setting.GateDueHours, OfficeDeadlineHours = s is IOfficeDeadlineStep rule ? Math.Max(0, rule.OfficeDeadlineHours) : 0, OpenTasks = open,
            };

        public async Task<ResponseDto<List<AgentStepDto>>> GetStepsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AgentRoles.StepReaders)) return AgentText.Bad<List<AgentStepDto>>(AgentText.NotAllowed);
            Dictionary<string, AgentStepSetting> settings = await AgentSettings.EnsureAsync(_uow, _catalog.All, ct);
            HashSet<string> seatsOff = await AgentSettings.DisabledSeatsAsync(_uow, ct);
            var openByStep = (await _uow.Set<AgentTask>().AsNoTracking().Where(t => !t.Deleted && t.State < AgentTaskState.Done).GroupBy(t => t.StepCode).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
                .ToDictionary(x => x.Key, x => x.N, StringComparer.Ordinal);
            var list = new List<AgentStepDto>();
            foreach (IAgentStep s in _catalog.All)
                list.Add(StepDto(s, settings[s.Code], !seatsOff.Contains(s.AgentCode), openByStep.TryGetValue(s.Code, out int n) ? n : 0));
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<AgentStepDto>> EnableStepAsync(AgentStepEnableDto dto, CancellationToken ct)
        {
            if (IsSeat() || !_user.IsInRole(AgentRoles.Admins)) return AgentText.Bad<AgentStepDto>(AgentText.NotAllowed);
            IAgentStep step = _catalog.Find(dto?.Code);
            if (step == null) return AgentText.NotFound<AgentStepDto>(AgentText.StepNotFound);
            Dictionary<string, AgentStepSetting> settings = await AgentSettings.EnsureAsync(_uow, _catalog.All, ct);
            AgentStepSetting setting = settings[step.Code];
            if (setting.Enabled != dto.Enabled)
            {
                setting.Enabled = dto.Enabled;
                _audit.Add(AiAuditKinds.Agent, $"step {step.Code} {(dto.Enabled ? "enabled" : "disabled")} by the PMO", userId: _user.UserId);
                await _uow.SaveChangesAsync(ct);
            }
            HashSet<string> seatsOff = await AgentSettings.DisabledSeatsAsync(_uow, ct);
            int open = await _uow.Set<AgentTask>().CountAsync(t => !t.Deleted && t.StepCode == step.Code && t.State < AgentTaskState.Done, ct);
            return ResponseDtoHelper.Ok(StepDto(step, setting, !seatsOff.Contains(step.AgentCode), open));
        }
    }
}
