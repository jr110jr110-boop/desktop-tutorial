using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Agents
{
    /// <summary>What is shown of a task (the same for the inbox, the task list and the board).</summary>
    internal static class AgentTaskView
    {
        public static void Fill(AgentInboxItemDto d, DateTimeOffset now)
        {
            d.StationTitle = AgentStations.Title(d.Station);
            d.AgentTitle = AiSeatCatalog.Find(d.AgentCode)?.Title;
            d.CreatedAtText = d.CreatedAt.Year > 1 ? AiText.Stamp(d.CreatedAt) : null;
            d.AgeHours = d.CreatedAt.Year > 1 ? (int)Math.Max(0, (now - d.CreatedAt).TotalHours) : 0;
            d.Overdue = d.DueAt.HasValue && d.DueAt.Value < now && IsWaiting(d.State);
        }

        public static bool IsWaiting(AgentTaskState s) => s == AgentTaskState.WaitingHumanGate || s == AgentTaskState.WaitingPhysical || s == AgentTaskState.WaitingInput;

    }

    public interface IAgentInboxService
    {
        /// <summary>What waits for the signed-in person: the tasks whose roles (or user) are his. A seat never sees it.</summary>
        Task<ResponseDto<PagedList<AgentInboxItemDto>>> GetInboxAsync(AgentInboxQuery query, CancellationToken ct);

        Task<ResponseDto<AgentInboxCountsDto>> GetCountsAsync(CancellationToken ct);

        /// <summary>A person sends a task back to the agent with a reason.</summary>
        Task<ResponseDto<AgentInboxItemDto>> ReturnAsync(AgentReturnDto dto, CancellationToken ct);
    }

    /// <summary>
    /// The gate inbox. It is a view over the tasks of the reconciler: a task is shown only to a person who holds one of its roles (or is its user), never to a seat. The decision itself is made in the
    /// existing page of the document (the link), with the existing action, so no parallel approval exists; the task closes when the document leaves the gate.
    /// </summary>
    public class AgentInboxService : IAgentInboxService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IHttpContextAccessor _http;
        private readonly IAgentStepCatalog _catalog;
        private readonly IAiAudit _audit;
        private readonly IServiceProvider _services;

        public AgentInboxService(IUnitOfWork uow, IInventoryUserContext user, IHttpContextAccessor http, IAgentStepCatalog catalog, IAiAudit audit, IServiceProvider services)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        private bool IsSeat() => AiActing.IsActing || AiSeatNames.IsSeatUser(_http.HttpContext?.User?.Identity?.Name);

        private bool Mine(AgentTask t) =>
            (t.GateUserId.HasValue && t.GateUserId.Value == _user.UserId && _user.UserId > 0) || (!string.IsNullOrWhiteSpace(t.GateRoles) && _user.IsInRole(t.GateRoles));

        private static AgentInboxItemDto ToDto(AgentTask t, DateTimeOffset now)
        {
            var d = new AgentInboxItemDto
            {
                Id = t.Id, StepCode = t.StepCode, Station = t.Station, AgentCode = t.AgentCode, Kind = t.Kind, State = t.State, Title = t.Title, TargetType = t.TargetType, TargetId = t.TargetId,
                TargetNumber = t.TargetNumber, ContractId = t.ContractId, Summary = t.Summary, OutputType = t.OutputType, OutputNumber = t.OutputNumber, GateRoles = t.GateRoles, Link = t.Link,
                CreatedAt = t.CreateDate, DueAt = t.DueAt, IsFallback = t.IsFallback, LastError = t.LastError, ReturnReason = t.ReturnReason,
            };
            AgentTaskView.Fill(d, now);
            return d;
        }

        private async Task<List<AgentTask>> WaitingForMeAsync(int? station, int? contractId, CancellationToken ct)
        {
            IQueryable<AgentTask> q = _uow.Set<AgentTask>().AsNoTracking().Where(t => !t.Deleted
                && (t.State == AgentTaskState.WaitingHumanGate || t.State == AgentTaskState.WaitingPhysical || t.State == AgentTaskState.WaitingInput));
            if (station.HasValue) q = q.Where(t => t.Station == station.Value);
            if (contractId.HasValue) q = q.Where(t => t.ContractId == contractId.Value);
            // the roles are a comma separated list: matched in memory (hundreds of open tasks at most)
            List<AgentTask> all = await q.OrderBy(t => t.Id).ToListAsync(ct);
            return all.Where(Mine).ToList();
        }

        public async Task<ResponseDto<PagedList<AgentInboxItemDto>>> GetInboxAsync(AgentInboxQuery query, CancellationToken ct)
        {
            if (IsSeat()) return AgentText.Bad<PagedList<AgentInboxItemDto>>(AgentText.SeatsHaveNoInbox);
            query ??= new AgentInboxQuery();
            DateTimeOffset now = DateTimeOffset.Now;
            List<AgentTask> mine = await WaitingForMeAsync(query.Station, query.ContractId, ct);
            if (query.Kind.HasValue)
            {
                mine = query.Kind.Value switch
                {
                    AgentStepKind.HumanGate => mine.Where(t => t.State == AgentTaskState.WaitingHumanGate).ToList(),
                    AgentStepKind.Physical => mine.Where(t => t.State == AgentTaskState.WaitingPhysical).ToList(),
                    AgentStepKind.Input => mine.Where(t => t.State == AgentTaskState.WaitingInput).ToList(),
                    _ => mine.Where(t => t.IsFallback).ToList(),
                };
            }
            // the late ones first, then the oldest
            List<AgentInboxItemDto> rows = mine.Select(t => ToDto(t, now)).OrderByDescending(d => d.Overdue).ThenBy(d => d.Id).ToList();
            int size = query.PageSize <= 0 ? 0 : Math.Min(query.PageSize, 200);
            return ResponseDtoHelper.Ok(await PagedList<AgentInboxItemDto>.CreateAsync(rows, query.PageNumber, size, ct));
        }

        public async Task<ResponseDto<AgentInboxCountsDto>> GetCountsAsync(CancellationToken ct)
        {
            if (IsSeat()) return AgentText.Bad<AgentInboxCountsDto>(AgentText.SeatsHaveNoInbox);
            DateTimeOffset now = DateTimeOffset.Now;
            List<AgentTask> mine = await WaitingForMeAsync(null, null, ct);
            return ResponseDtoHelper.Ok(new AgentInboxCountsDto
            {
                HumanGate = mine.Count(t => t.State == AgentTaskState.WaitingHumanGate),
                Physical = mine.Count(t => t.State == AgentTaskState.WaitingPhysical),
                Input = mine.Count(t => t.State == AgentTaskState.WaitingInput),
                Total = mine.Count,
                Overdue = mine.Count(t => AgentTaskView.IsWaiting(t.State) && t.DueAt.HasValue && t.DueAt.Value < now),
            });
        }

        public async Task<ResponseDto<AgentInboxItemDto>> ReturnAsync(AgentReturnDto dto, CancellationToken ct)
        {
            if (IsSeat()) return AgentText.Bad<AgentInboxItemDto>(AgentText.SeatsHaveNoInbox);
            string reason = AgentText.Clean(dto?.Reason);
            if (reason == null) return AgentText.Bad<AgentInboxItemDto>(AgentText.ReasonRequired);
            AgentTask task = await _uow.Set<AgentTask>().FirstOrDefaultAsync(t => t.Id == dto.Id && !t.Deleted, ct);
            if (task == null) return AgentText.NotFound<AgentInboxItemDto>(AgentText.TaskNotFound);
            if (!Mine(task)) return AgentText.Bad<AgentInboxItemDto>(AgentText.NotYourGate);
            if (!AgentTaskView.IsWaiting(task.State)) return AgentText.Bad<AgentInboxItemDto>(AgentText.NotAtGate);
            reason = AgentText.Cut(reason, 1000);
            DateTimeOffset now = DateTimeOffset.Now;

            IAgentStep step = _catalog.Find(task.StepCode);
            if (step is IAgentReturnable returnable)
            {
                // the existing return action of the document, as this person (it refuses the creator, asks for a reason, closes the gate in the office inbox)
                var item = new AgentWorkItem { TargetType = task.TargetType, TargetId = task.TargetId, TargetNumber = task.TargetNumber, ContractId = task.ContractId, WorkOrderId = task.WorkOrderId, Summary = task.Summary };
                ResponseDto<bool> returned = await returnable.ReturnAsync(item, reason, _services, ct);
                if (returned == null || !returned.Success) return AgentText.Bad<AgentInboxItemDto>(string.IsNullOrWhiteSpace(returned?.Message) ? AgentText.InvalidData : returned.Message);
                // the service saved its own changes; the task is read again and noted (it closes at the next reconcile, when the document has left the gate)
                task = await _uow.Set<AgentTask>().FirstAsync(t => t.Id == dto.Id, ct);
            }
            else
            {
                // no return action of its own: the agent that prepared the document does its step again
                AgentTask producer = await _uow.Set<AgentTask>().Where(t => !t.Deleted && t.Id != task.Id && t.TargetType == task.TargetType && t.TargetId == task.TargetId && t.Kind == AgentStepKind.Agent
                    && t.State == AgentTaskState.Done).OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
                if (producer == null) return AgentText.Bad<AgentInboxItemDto>(AgentText.NoProducer);
                bool open = await _uow.Set<AgentTask>().AnyAsync(t => !t.Deleted && t.StepCode == producer.StepCode && t.TargetType == producer.TargetType && t.TargetId == producer.TargetId && t.State < AgentTaskState.Done, ct);
                if (open) return AgentText.Bad<AgentInboxItemDto>(AgentText.ProducerBusy);
                producer.State = AgentTaskState.Returned; producer.ClosedAt = null; producer.ClosedReason = null; producer.NextAttemptAt = null; producer.AttemptCount = 0; producer.LastError = null;
                producer.EscalatedAt = null;
                producer.ReturnReason = reason; producer.ReturnedAt = now; producer.ReturnedByUserId = _user.UserId;
                AddEvent(producer, AgentTaskState.Done, AgentTaskState.Returned, "برگشت به ایجنت: " + reason, now);
            }
            task.ReturnReason = reason; task.ReturnedAt = now; task.ReturnedByUserId = _user.UserId;
            AddEvent(task, task.State, task.State, "برگشت به ایجنت: " + reason, now);
            _audit.Add(AiAuditKinds.Agent, $"task {task.Id.ToString(CultureInfo.InvariantCulture)} step {task.StepCode} returned to the agent; {AgentText.Cut(reason, 300)}", userId: _user.UserId);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(task, now));
        }

        private void AddEvent(AgentTask task, AgentTaskState from, AgentTaskState to, string note, DateTimeOffset now) =>
            _uow.Set<AgentTaskEvent>().Add(new AgentTaskEvent { Task = task, At = now, FromState = from, ToState = to, UserId = _user.UserId, SeatCode = null, Note = AgentText.Cut(note, 1000) });
    }
}
