using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Tasks;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.TaskDtos;

namespace MsTests
{
    /// <summary>
    /// P-B7a: the task status machine, the immutable referral history, the roll-up of tasks into a WBS activity, the progress-approval chain (configuration, lock, approver is not the submitter, application through the
    /// manual path), the risk score, the daily site report approval and the disabled notification gateway.
    /// </summary>
    [TestClass]
    public class TasksP7aTests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(11, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(22, "project.manager", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal Planning = AppHost.Principal(24, "planning.manager", "PLANNING");
        private static readonly ClaimsPrincipal Warehouse = AppHost.Principal(25, "warehouse", "WAREHOUSE");

        private static AppHost NewHost(string name)
        {
            var host = new AppHost("P7a_" + name + "_" + Guid.NewGuid().ToString("N"));
            host.Write(u =>
            {
                foreach (int id in new[] { 11, 22, 24, 25 }) AppHost.Add(u, new User { Id = id, UserName = "p7.user" + id, Email = "p7.user" + id + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
            return host;
        }

        private static int NewContract(AppHost host)
        {
            int id = 0;
            host.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        private static int NewActivity(AppHost host, int contractId, PcProgressMethod method)
        {
            int id = 0;
            host.Write(u =>
            {
                var a = AppHost.Add(u, new PcActivity { ContractId = contractId, Code = "A-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "activity", DurationDays = 4, ProgressMethod = method, SourceType = PcSourceType.Manual });
                u.SaveChanges();
                id = a.Id;
            }, Seeder);
            return id;
        }

        private static Task<ResponseDto<T>> Tasks<T>(AppHost host, ClaimsPrincipal who, Func<ITaskService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<ITaskService>()));

        // ------------------------------------------------------------------------------------------------ the status machine

        [TestMethod]
        public void TheStatusMachineAllowsTheDocumentedTransitionsOnly()
        {
            TmTaskStatus to;
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.Open, "start", null, out to)); Assert.AreEqual(TmTaskStatus.InProgress, to);
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.Assigned, "start", null, out to)); Assert.AreEqual(TmTaskStatus.InProgress, to);
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.InProgress, "complete", null, out to)); Assert.AreEqual(TmTaskStatus.Pending, to);
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.Pending, "approve", null, out to)); Assert.AreEqual(TmTaskStatus.Closed, to);
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.Pending, "return", null, out to)); Assert.AreEqual(TmTaskStatus.InProgress, to);
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.Open, "reject", null, out to)); Assert.AreEqual(TmTaskStatus.Rejected, to);
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.InProgress, "suspend", null, out to)); Assert.AreEqual(TmTaskStatus.Suspended, to);
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.Suspended, "resume", TmTaskStatus.InProgress, out to)); Assert.AreEqual(TmTaskStatus.InProgress, to, "resume goes back to where it was");
            Assert.IsTrue(TaskRules.TryNext(TmTaskStatus.Closed, "reopen", null, out to));
            Assert.IsFalse(TaskRules.TryNext(TmTaskStatus.Open, "approve", null, out _), "nothing to approve before the work is reported");
            Assert.IsFalse(TaskRules.TryNext(TmTaskStatus.Closed, "start", null, out _), "a closed task is not started");
            Assert.IsFalse(TaskRules.TryNext(TmTaskStatus.Pending, "start", null, out _));
            Assert.IsFalse(TaskRules.TryNext(TmTaskStatus.Rejected, "complete", null, out _));
            Assert.IsFalse(TaskRules.TryNext(TmTaskStatus.Open, "no-such-action", null, out _));
        }

        [TestMethod]
        public void TheRollUpWeightsByEstimatedHoursAndLeavesRejectedTasksOut()
        {
            decimal? p = TaskRules.RollUpPercent(new[] { (10m, 0m, TmTaskStatus.Closed), (30m, 0m, TmTaskStatus.Open) });
            Assert.AreEqual(25m, p, "a closed task counts as 100, weighted by hours: 10 of 40");
            Assert.AreEqual(50m, TaskRules.RollUpPercent(new[] { (10m, 100m, TmTaskStatus.InProgress), (10m, 0m, TmTaskStatus.InProgress) }));
            Assert.AreEqual(100m, TaskRules.RollUpPercent(new[] { (10m, 100m, TmTaskStatus.Closed), (500m, 0m, TmTaskStatus.Rejected) }), "a rejected task is left out");
            Assert.IsNull(TaskRules.RollUpPercent(new[] { (10m, 0m, TmTaskStatus.Rejected) }), "no counting task: no value");
            Assert.AreEqual(50m, TaskRules.RollUpPercent(new[] { (0m, 100m, TmTaskStatus.InProgress), (0m, 0m, TmTaskStatus.InProgress) }), "a task without an estimate counts as one hour");
        }

        // ------------------------------------------------------------------------------------------------ referral history

        [TestMethod]
        public async Task ATransferKeepsTheFirstReferralAsItWasAndAddsAChainedOne()
        {
            AppHost host = NewHost("Referral");
            int contractId = NewContract(host);
            ResponseDto<TmTaskDto> created = await Tasks(host, ProjectManager, s => s.SaveTaskAsync(new TmTaskSaveDto
            {
                Title = "task", Kind = TmTaskKind.Project, ContractId = contractId,
                Assignees = new List<TmAssignTargetDto> { new TmAssignTargetDto { UserId = 24, Role = TmAssigneeRole.Responsible, Reason = "for action" } },
            }, CancellationToken.None));
            Assert.IsTrue(created.Success, created.Message);
            Assert.AreEqual(TmTaskStatus.Assigned, created.Data.Status);
            int taskId = created.Data.Id;
            TmAssigneeDto first = created.Data.Assignees.Single();
            DateTimeOffset firstAt = first.AssignedAt;

            ResponseDto<TmTaskDto> noReason = await Tasks(host, Planning, s => s.TransferAsync(new TmTaskTransferDto { TaskId = taskId, AssigneeId = first.Id, ToUserId = 11, Reason = " " }, CancellationToken.None));
            Assert.IsFalse(noReason.Success, "a transfer needs a reason");
            ResponseDto<TmTaskDto> stranger = await Tasks(host, Warehouse, s => s.TransferAsync(new TmTaskTransferDto { TaskId = taskId, AssigneeId = first.Id, ToUserId = 11, Reason = "x" }, CancellationToken.None));
            Assert.IsFalse(stranger.Success, "somebody who does not hold the cartable cannot move the task");

            ResponseDto<TmTaskDto> moved = await Tasks(host, Planning, s => s.TransferAsync(new TmTaskTransferDto { TaskId = taskId, AssigneeId = first.Id, ToUserId = 11, Reason = "needs the PMO" }, CancellationToken.None));
            Assert.IsTrue(moved.Success, moved.Message);
            Assert.AreEqual(2, moved.Data.Assignees.Count);
            TmAssigneeDto old = moved.Data.Assignees.Single(a => a.Id == first.Id);
            Assert.AreEqual(TmAssigneeState.Transferred, old.State);
            Assert.AreEqual("for action", old.Reason, "why it was referred never changes");
            Assert.AreEqual(firstAt, old.AssignedAt, "when it was referred never changes");
            Assert.AreEqual("needs the PMO", old.EndReason);
            TmAssigneeDto added = moved.Data.Assignees.Single(a => a.Id != first.Id);
            Assert.AreEqual(first.Id, added.ParentId, "the new referral points at the old one");
            Assert.AreEqual(TmAssigneeState.Active, added.State);

            // the old holder (planning) no longer holds it; a transferred referral cannot be moved again
            ResponseDto<TmTaskDto> again = await Tasks(host, Planning, s => s.TransferAsync(new TmTaskTransferDto { TaskId = taskId, AssigneeId = first.Id, ToUserId = 22, Reason = "again" }, CancellationToken.None));
            Assert.IsFalse(again.Success);

            ResponseDto<List<TmReferralNodeDto>> tree = await Tasks(host, ProjectManager, s => s.GetReferralTreeAsync(taskId, CancellationToken.None));
            Assert.AreEqual(1, tree.Data.Count);
            Assert.AreEqual(1, tree.Data[0].Children.Count);
            Assert.IsTrue(moved.Data.History.Any(h => h.Kind == TmHistoryKind.Transfer), "the transfer is in the history");
        }

        [TestMethod]
        public async Task TheOneWhoReportedTheTaskDoneCannotApproveItAndAPersonalTaskNeedsNoApproval()
        {
            AppHost host = NewHost("Approve");
            int contractId = NewContract(host);
            TmTaskDto t = (await Tasks(host, ProjectManager, s => s.SaveTaskAsync(new TmTaskSaveDto
            {
                Title = "task", Kind = TmTaskKind.Project, ContractId = contractId,
                Assignees = new List<TmAssignTargetDto> { new TmAssignTargetDto { UserId = 24, Role = TmAssigneeRole.Responsible } },
            }, CancellationToken.None))).Data;
            Assert.IsFalse((await Tasks(host, ProjectManager, s => s.ActionAsync(new TmTaskActionDto { Id = t.Id, Action = "start" }, CancellationToken.None))).Success, "the creator is not the assignee");
            Assert.IsTrue((await Tasks(host, Planning, s => s.ActionAsync(new TmTaskActionDto { Id = t.Id, Action = "start" }, CancellationToken.None))).Success);
            ResponseDto<TmTaskDto> done = await Tasks(host, Planning, s => s.ActionAsync(new TmTaskActionDto { Id = t.Id, Action = "complete" }, CancellationToken.None));
            Assert.AreEqual(TmTaskStatus.Pending, done.Data.Status);
            Assert.IsFalse((await Tasks(host, Planning, s => s.ActionAsync(new TmTaskActionDto { Id = t.Id, Action = "approve" }, CancellationToken.None))).Success, "the reporter does not approve");
            Assert.IsFalse((await Tasks(host, ProjectManager, s => s.ActionAsync(new TmTaskActionDto { Id = t.Id, Action = "return" }, CancellationToken.None))).Success, "returning needs a reason");
            ResponseDto<TmTaskDto> closed = await Tasks(host, ProjectManager, s => s.ActionAsync(new TmTaskActionDto { Id = t.Id, Action = "approve" }, CancellationToken.None));
            Assert.AreEqual(TmTaskStatus.Closed, closed.Data.Status);
            Assert.AreEqual(100m, closed.Data.PercentComplete);

            TmTaskDto personal = (await Tasks(host, ProjectManager, s => s.SaveTaskAsync(new TmTaskSaveDto { Title = "mine", Kind = TmTaskKind.Personal }, CancellationToken.None))).Data;
            Assert.IsFalse((await Tasks(host, Planning, s => s.GetTaskAsync(personal.Id, CancellationToken.None))).Success, "nobody else sees a personal task");
            Assert.IsFalse((await Tasks(host, Pmo, s => s.GetTaskAsync(personal.Id, CancellationToken.None))).Success, "not even the PMO");
            await Tasks(host, ProjectManager, s => s.ActionAsync(new TmTaskActionDto { Id = personal.Id, Action = "start" }, CancellationToken.None));
            ResponseDto<TmTaskDto> pdone = await Tasks(host, ProjectManager, s => s.ActionAsync(new TmTaskActionDto { Id = personal.Id, Action = "complete" }, CancellationToken.None));
            Assert.AreEqual(TmTaskStatus.Closed, pdone.Data.Status, "a personal task closes by itself");
        }

        // ------------------------------------------------------------------------------------------------ roll-up into the activity

        [TestMethod]
        public async Task ClosingTasksMovesThePercentOfTheActivityWithTheTasksMethod()
        {
            AppHost host = NewHost("Rollup");
            int contractId = NewContract(host);
            int activityId = NewActivity(host, contractId, PcProgressMethod.Tasks);
            TmTaskDto a = (await Tasks(host, ProjectManager, s => s.SaveTaskAsync(new TmTaskSaveDto { Title = "a", Kind = TmTaskKind.Project, ActivityId = activityId, EstimatedHours = 10 }, CancellationToken.None))).Data;
            TmTaskDto b = (await Tasks(host, ProjectManager, s => s.SaveTaskAsync(new TmTaskSaveDto { Title = "b", Kind = TmTaskKind.Project, ActivityId = activityId, EstimatedHours = 30 }, CancellationToken.None))).Data;
            Assert.AreEqual(contractId, a.ContractId, "the task takes its project from the activity");
            Assert.AreEqual(0m, host.Read(u => u.Set<PcActivity>().Single(x => x.Id == activityId).PercentCache));
            await Tasks(host, ProjectManager, s => s.ActionAsync(new TmTaskActionDto { Id = a.Id, Action = "complete" }, CancellationToken.None));
            await Tasks(host, Pmo, s => s.ActionAsync(new TmTaskActionDto { Id = a.Id, Action = "approve" }, CancellationToken.None));
            Assert.AreEqual(25m, host.Read(u => u.Set<PcActivity>().Single(x => x.Id == activityId).PercentCache), "10 of 40 hours closed");
            ResponseDto<List<TmTaskListDto>> listed = await Tasks(host, Planning, s => s.GetByActivityAsync(activityId, CancellationToken.None));
            Assert.AreEqual(2, listed.Data.Count, "the activity lists its tasks");
            // the percent of a task linked to an activity is not typed by the assignee: it goes through the approval cartable
            ResponseDto<TmTaskDto> typed = await Tasks(host, ProjectManager, s => s.UpdateProgressAsync(new TmTaskProgressDto { TaskId = b.Id, Percent = 50 }, CancellationToken.None));
            Assert.IsFalse(typed.Success);
        }

        // ------------------------------------------------------------------------------------------------ the approval chain

        [TestMethod]
        public void AChainNeedsALastLevelOfPmoOrPlanningAndAKnownRole()
        {
            Assert.IsNotNull(PcProgressChainRules.Problem(new List<(string, int?)>()));
            Assert.IsNull(PcProgressChainRules.Problem(new List<(string, int?)> { ("PROJECTMANAGER", null), ("PMO", null) }));
            Assert.IsNull(PcProgressChainRules.Problem(new List<(string, int?)> { (null, 5), ("PLANNING", null) }), "a position and then planning");
            Assert.IsNotNull(PcProgressChainRules.Problem(new List<(string, int?)> { ("PROJECTMANAGER", null) }), "the last level must write to the plan");
            Assert.IsNotNull(PcProgressChainRules.Problem(new List<(string, int?)> { ("NOSUCHROLE", null), ("PMO", null) }));
            Assert.IsNotNull(PcProgressChainRules.Problem(new List<(string, int?)> { ("PMO", 5) }), "a level is a role or a position, not both");
            Assert.IsNotNull(PcProgressChainRules.Problem(Enumerable.Repeat(("PLANNING", (int?)null), 7).ToList()), "at most six levels");
        }

        [TestMethod]
        public async Task TheChainApprovesLevelByLevelLocksTheReportAndWritesThroughTheManualPath()
        {
            AppHost host = NewHost("Chain");
            int contractId = NewContract(host);
            int activityId = NewActivity(host, contractId, PcProgressMethod.Manual);
            IPcProgressApprovalService Svc(IServiceProvider p) => p.GetRequiredService<IPcProgressApprovalService>();

            ResponseDto<PcChainDto> bad = await host.As(ProjectManager, p => Svc(p).SaveChainAsync(new PcChainSaveDto { ContractId = contractId, Levels = new List<PcChainLevelDto> { new PcChainLevelDto { Role = "PROJECTMANAGER" } } }, CancellationToken.None));
            Assert.IsFalse(bad.Success, "the last level must be the PMO or planning");
            ResponseDto<PcChainDto> noRole = await host.As(Warehouse, p => Svc(p).SaveChainAsync(new PcChainSaveDto { ContractId = contractId, Levels = new List<PcChainLevelDto> { new PcChainLevelDto { Role = "PMO" } } }, CancellationToken.None));
            Assert.IsFalse(noRole.Success);
            Assert.IsTrue((await host.As(ProjectManager, p => Svc(p).GetChainAsync(contractId, CancellationToken.None))).Data.IsDefault);
            ResponseDto<PcChainDto> saved = await host.As(ProjectManager, p => Svc(p).SaveChainAsync(new PcChainSaveDto
            {
                ContractId = contractId, Levels = new List<PcChainLevelDto> { new PcChainLevelDto { Title = "Planning", Role = "PLANNING" }, new PcChainLevelDto { Title = "PMO", Role = "PMO" } },
            }, CancellationToken.None));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(2, saved.Data.Levels.Count);

            var save = new PcSubmissionSaveDto { ContractId = contractId, Target = PcSubmissionTarget.Activity, ActivityId = activityId, Percent = 40, Comment = "week", ActualCost = 1000, CostKind = (int)CostKind.Labour, CostNote = "wages", ActualStart = DateTime.Today.AddDays(-2) };
            ResponseDto<PcSubmissionDto> draft = await host.As(ProjectManager, p => Svc(p).SaveSubmissionAsync(save, CancellationToken.None));
            Assert.IsTrue(draft.Success, draft.Message);
            Assert.AreEqual(PcSubmissionState.Draft, draft.Data.State);
            Assert.IsTrue(draft.Data.CanEdit);
            save.Id = draft.Data.Id; save.Submit = true;
            ResponseDto<PcSubmissionDto> sent = await host.As(ProjectManager, p => Svc(p).SaveSubmissionAsync(save, CancellationToken.None));
            Assert.IsTrue(sent.Success, sent.Message);
            Assert.AreEqual(PcSubmissionState.InApproval, sent.Data.State);
            Assert.AreEqual(1, sent.Data.CurrentLevel);
            Assert.IsTrue(sent.Data.Locked);
            int id = sent.Data.Id;

            // locked for the submitter, and the chain cannot change under the people who are deciding
            save.Percent = 90; save.Submit = false;
            Assert.IsFalse((await host.As(ProjectManager, p => Svc(p).SaveSubmissionAsync(save, CancellationToken.None))).Success, "the submitter cannot change a report in approval");
            Assert.IsFalse((await host.As(Pmo, p => Svc(p).SaveChainAsync(new PcChainSaveDto { ContractId = contractId, Levels = new List<PcChainLevelDto> { new PcChainLevelDto { Role = "PMO" } } }, CancellationToken.None))).Success, "the chain is locked while a report is in approval");

            Assert.IsFalse((await host.As(ProjectManager, p => Svc(p).DecideAsync(new PcSubmissionDecisionDto { Id = id, Approve = true }, CancellationToken.None))).Success, "the submitter does not approve his own report");
            Assert.IsFalse((await host.As(Pmo, p => Svc(p).DecideAsync(new PcSubmissionDecisionDto { Id = id, Approve = true }, CancellationToken.None))).Success, "level 1 belongs to planning");
            Assert.IsFalse((await host.As(Planning, p => Svc(p).DecideAsync(new PcSubmissionDecisionDto { Id = id, Approve = false }, CancellationToken.None))).Success, "a rejection needs a reason");

            ResponseDto<PcSubmissionDto> returned = await host.As(Planning, p => Svc(p).DecideAsync(new PcSubmissionDecisionDto { Id = id, Approve = false, Note = "does not match the daily report" }, CancellationToken.None));
            Assert.AreEqual(PcSubmissionState.Returned, returned.Data.State);
            Assert.AreEqual("does not match the daily report", returned.Data.ResultNote);
            Assert.IsFalse(returned.Data.Locked, "a returned report is editable again by its submitter");

            save.Percent = 35; save.Submit = true;
            ResponseDto<PcSubmissionDto> second = await host.As(ProjectManager, p => Svc(p).SaveSubmissionAsync(save, CancellationToken.None));
            Assert.AreEqual(2, second.Data.Round);
            Assert.AreEqual(1, second.Data.CurrentLevel);

            ResponseDto<PcSubmissionDto> edited = await host.As(Planning, p => Svc(p).DecideAsync(new PcSubmissionDecisionDto { Id = id, Approve = true, Percent = 30 }, CancellationToken.None));
            Assert.IsTrue(edited.Success, edited.Message);
            Assert.AreEqual(30m, edited.Data.Percent, "the approver edited the percent");
            Assert.AreEqual(2, edited.Data.CurrentLevel);
            Assert.IsFalse((await host.As(Planning, p => Svc(p).DecideAsync(new PcSubmissionDecisionDto { Id = id, Approve = true }, CancellationToken.None))).Success, "level 2 is the PMO");

            ResponseDto<PcSubmissionDto> final = await host.As(Pmo, p => Svc(p).DecideAsync(new PcSubmissionDecisionDto { Id = id, Approve = true }, CancellationToken.None));
            Assert.IsTrue(final.Success, final.Message);
            Assert.AreEqual(PcSubmissionState.Approved, final.Data.State);
            Assert.IsTrue(final.Data.ManualProgressId > 0);
            Assert.IsTrue(final.Data.CostEntryId > 0);
            Assert.IsTrue(final.Data.History.Any(h => h.Action == "edited" && h.ChangesJson.Contains("percent")));
            Assert.IsTrue(final.Data.History.Any(h => h.Action == "returned"));
            Assert.IsTrue(final.Data.History.Any(h => h.Action == "applied"));

            PcManualProgress row = host.Read(u => u.Set<PcManualProgress>().Single(m => m.ActivityId == activityId));
            Assert.AreEqual(1, row.State, "the manual path: an approved row");
            Assert.AreEqual(30m, row.Percent);
            Assert.AreEqual(22, row.ProposedByUserId, "proposed by the submitter");
            Assert.AreEqual(11, row.DecidedByUserId, "decided by the PMO");
            Assert.AreEqual(30m, host.Read(u => u.Set<PcActivity>().Single(a => a.Id == activityId).PercentCache));
            CostEntry cost = host.Read(u => u.Set<CostEntry>().Single(c => c.Id == final.Data.CostEntryId.Value));
            Assert.AreEqual(22, cost.CreatedByUserId, "the cost is the submitter's entry, so a second finance person checks it");
            Assert.IsFalse(host.Read(u => u.Set<PcCostCheck>().Any(c => c.CostEntryId == cost.Id)), "and it is not yet checked");
            Assert.IsTrue((await host.As(Pmo, p => Svc(p).SaveChainAsync(new PcChainSaveDto { ContractId = contractId, Levels = new List<PcChainLevelDto> { new PcChainLevelDto { Role = "PMO" } } }, CancellationToken.None))).Success, "with nothing in approval the chain can change");
        }

        [TestMethod]
        public async Task AnActivityMeasuredFromTasksCannotBeReportedByHand()
        {
            AppHost host = NewHost("NoHand");
            int contractId = NewContract(host);
            int activityId = NewActivity(host, contractId, PcProgressMethod.Tasks);
            ResponseDto<PcSubmissionDto> r = await host.As(ProjectManager, p => p.GetRequiredService<IPcProgressApprovalService>().SaveSubmissionAsync(new PcSubmissionSaveDto { ContractId = contractId, Target = PcSubmissionTarget.Activity, ActivityId = activityId, Percent = 10 }, CancellationToken.None));
            Assert.IsFalse(r.Success);
        }

        // ------------------------------------------------------------------------------------------------ risk score

        [TestMethod]
        public void TheRiskScoreIsProbabilityTimesImpactAndTheLevelFollowsTheLimits()
        {
            Assert.AreEqual(20, PcRiskRules.Score(4, 5));
            Assert.AreEqual(1, PcRiskRules.Score(1, 1));
            Assert.IsTrue(PcRiskRules.ValidScale(1) && PcRiskRules.ValidScale(5));
            Assert.IsFalse(PcRiskRules.ValidScale(0) || PcRiskRules.ValidScale(6));
            Assert.AreEqual("critical", PcRiskRules.Level(20, 12, 20));
            Assert.AreEqual("high", PcRiskRules.Level(12, 12, 20));
            Assert.AreEqual("medium", PcRiskRules.Level(6, 12, 20));
            Assert.AreEqual("low", PcRiskRules.Level(2, 12, 20));
            Assert.AreEqual("manage-closely", PcStakeholderRules.Quadrant(5, 4));
            Assert.AreEqual("keep-satisfied", PcStakeholderRules.Quadrant(5, 1));
            Assert.AreEqual("keep-informed", PcStakeholderRules.Quadrant(1, 5));
            Assert.AreEqual("monitor", PcStakeholderRules.Quadrant(2, 2));
            Assert.AreEqual(80m, PcEvaluationRules.Total(new[] { (50m, 5m), (50m, 3m) }));
            Assert.AreEqual("A", PcEvaluationRules.Grade(91m, 90m, 75m, 60m));
            Assert.AreEqual("D", PcEvaluationRules.Grade(10m, 90m, 75m, 60m));
        }

        [TestMethod]
        public async Task ARiskIsScoredByTheServiceItsOpenActionsBlockClosingAndTheHeatMapCountsIt()
        {
            AppHost host = NewHost("Risk");
            int contractId = NewContract(host);
            IPcRegistersService Svc(IServiceProvider p) => p.GetRequiredService<IPcRegistersService>();
            Assert.IsFalse((await host.As(Planning, p => Svc(p).SaveRiskAsync(new PcRiskSaveDto { ContractId = contractId, Title = "r", Probability = 6, Impact = 2 }, CancellationToken.None))).Success, "scale 1..5");
            Assert.IsFalse((await host.As(AppHost.Principal(26, "accountant", "FINANCIAL"), p => Svc(p).SaveRiskAsync(new PcRiskSaveDto { ContractId = contractId, Title = "r", Probability = 1, Impact = 1 }, CancellationToken.None))).Success, "role");
            ResponseDto<PcRiskDto> risk = await host.As(Planning, p => Svc(p).SaveRiskAsync(new PcRiskSaveDto { ContractId = contractId, Title = "late plates", Probability = 4, Impact = 5 }, CancellationToken.None));
            Assert.IsTrue(risk.Success, risk.Message);
            Assert.AreEqual(20, risk.Data.Score);
            Assert.IsTrue(risk.Data.Number.StartsWith("RK"));
            ResponseDto<PcRiskHeatMapDto> map = await host.As(Planning, p => Svc(p).GetRiskHeatMapAsync(contractId, CancellationToken.None));
            Assert.AreEqual(25, map.Data.Cells.Count);
            Assert.AreEqual(1, map.Data.Cells.Single(c => c.Probability == 4 && c.Impact == 5).Count);
            ResponseDto<TmTaskDto> action = await Tasks(host, Planning, s => s.SaveTaskAsync(new TmTaskSaveDto { Title = "mitigate", Kind = TmTaskKind.Project, ContractId = contractId, RiskId = risk.Data.Id }, CancellationToken.None));
            Assert.IsTrue(action.Success, action.Message);
            Assert.IsFalse((await host.As(Planning, p => Svc(p).SetRiskStatusAsync(new PcRiskStatusDto { Id = risk.Data.Id, Status = PcRiskStatus.Closed, Note = "done" }, CancellationToken.None))).Success, "an open action blocks closing");
            // an issue is closed by somebody other than the one who resolved it
            ResponseDto<PcIssueDto> issue = await host.As(Planning, p => Svc(p).SaveIssueAsync(new PcIssueSaveDto { ContractId = contractId, Title = "crane" }, CancellationToken.None));
            await host.As(Planning, p => Svc(p).IssueActionAsync(new PcIssueActionDto { Id = issue.Data.Id, Action = "resolve", Note = "hired" }, CancellationToken.None));
            Assert.IsFalse((await host.As(Planning, p => Svc(p).IssueActionAsync(new PcIssueActionDto { Id = issue.Data.Id, Action = "close" }, CancellationToken.None))).Success);
            Assert.IsTrue((await host.As(Pmo, p => Svc(p).IssueActionAsync(new PcIssueActionDto { Id = issue.Data.Id, Action = "close" }, CancellationToken.None))).Success);
        }

        // ------------------------------------------------------------------------------------------------ daily report approval

        [TestMethod]
        public void TheConfirmerOfADailyReportIsNotItsAuthorOrAnEditor()
        {
            Assert.IsFalse(PcDailyRules.MayConfirm(5, ",5,", 5), "the author");
            Assert.IsFalse(PcDailyRules.MayConfirm(5, ",5,7,", 7), "an editor");
            Assert.IsTrue(PcDailyRules.MayConfirm(5, ",5,7,", 9));
            Assert.AreEqual(",5,7,", PcDailyRules.AddEditor(",5,", 7));
            Assert.AreEqual(",5,", PcDailyRules.AddEditor(",5,", 5), "no duplicate");
        }

        [TestMethod]
        public async Task TheExtendedDailyReportKeepsItsRowsAndIsConfirmedByAThirdPerson()
        {
            AppHost host = NewHost("Daily");
            int contractId = NewContract(host);
            IPcProgressService Svc(IServiceProvider p) => p.GetRequiredService<IPcProgressService>();
            var dto = new PcDailyReportDto
            {
                ContractId = contractId, Unit = "ALL", ReportDate = DateTime.Today, Weather = "sunny", Notes = "normal day",
                Manpower = new List<PcDailyManpowerDto> { new PcDailyManpowerDto { Kind = PcManpowerKind.Internal, Title = "welder", Count = 5, Present = 4, Absent = 1 }, new PcDailyManpowerDto { Kind = PcManpowerKind.Contractor, Title = "company", ActivityType = "scaffold", PersonDays = 6 } },
                Materials = new List<PcDailyMaterialDto> { new PcDailyMaterialDto { Description = "plate", Unit = "kg", Quantity = 120 } },
                Machinery = new List<PcDailyMachineryDto> { new PcDailyMachineryDto { Name = "crane", Ownership = PcMachineOwnership.Hired, Present = 1, Active = 1, Hours = 6 } },
                Obstacles = new List<PcDailyObstacleDto> { new PcDailyObstacleDto { Category = "weather", Description = "wind", ImpactHours = 2 } },
                HseEvents = new List<PcDailyHseDto> { new PcDailyHseDto { Kind = PcHseKind.NearMiss, Description = "a tool fell, nobody hurt" } },
            };
            ResponseDto<PcDailyReportDto> saved = await host.As(Planning, p => Svc(p).SaveDailyAsync(dto, CancellationToken.None));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(2, saved.Data.Manpower.Count);
            Assert.AreEqual(1, saved.Data.Materials.Count);
            Assert.AreEqual(1, saved.Data.Machinery.Count);
            Assert.AreEqual(1, saved.Data.Obstacles.Count);
            Assert.AreEqual(1, saved.Data.HseEvents.Count);
            Assert.AreEqual("sunny", saved.Data.Weather);
            int id = saved.Data.Id.Value;

            dto.Machinery[0].Active = 3;
            Assert.IsFalse((await host.As(Planning, p => Svc(p).SaveDailyAsync(dto, CancellationToken.None))).Success, "more active machines than present");
            dto.Machinery[0].Active = 1;

            Assert.IsFalse((await host.As(Planning, p => Svc(p).ConfirmDailyAsync(id, CancellationToken.None))).Success, "the author cannot confirm");
            Assert.IsTrue((await host.As(Pmo, p => Svc(p).SaveDailyAsync(dto, CancellationToken.None))).Success, "the PMO edits it");
            Assert.IsFalse((await host.As(Pmo, p => Svc(p).ConfirmDailyAsync(id, CancellationToken.None))).Success, "an editor cannot confirm either");
            ResponseDto<PcDailyReportDto> confirmed = await host.As(ProjectManager, p => Svc(p).ConfirmDailyAsync(id, CancellationToken.None));
            Assert.IsTrue(confirmed.Success, confirmed.Message);
            Assert.AreEqual(1, confirmed.Data.State);
            Assert.IsFalse((await host.As(Planning, p => Svc(p).SaveDailyAsync(dto, CancellationToken.None))).Success, "a confirmed report is read only");
            ResponseDto<PagedList<PcDailyListDto>> list = await host.As(Planning, p => Svc(p).GetDailyListAsync(new PcDailyListQuery { ContractId = contractId, PageSize = 0 }, CancellationToken.None));
            Assert.AreEqual(1, list.Data.Count);
            ResponseDto<PcDailyReportDto> one = await host.As(Planning, p => Svc(p).GetDailyByIdAsync(id, CancellationToken.None));
            Assert.AreEqual(2, one.Data.Manpower.Count);
        }

        // ------------------------------------------------------------------------------------------------ notifications

        [TestMethod]
        public async Task TheGatewayIsDisabledByDefaultAndAnAssignmentLeavesAnInboxItem()
        {
            var gateway = new DisabledNotificationGateway();
            GatewayResult r = await gateway.SendAsync(new GatewayMessage { Channel = NotificationChannel.Sms, UserId = 1, Title = "t" }, CancellationToken.None);
            Assert.IsFalse(r.Sent, "no external call");
            AppHost host = NewHost("Inbox");
            Assert.IsInstanceOfType(host.Provider.CreateScope().ServiceProvider.GetRequiredService<INotificationGateway>(), typeof(DisabledNotificationGateway));
            int contractId = NewContract(host);
            await Tasks(host, ProjectManager, s => s.SaveTaskAsync(new TmTaskSaveDto
            {
                Title = "assigned", Kind = TmTaskKind.Project, ContractId = contractId, Priority = TmPriority.High,
                Assignees = new List<TmAssignTargetDto> { new TmAssignTargetDto { UserId = 24, Role = TmAssigneeRole.Responsible } },
            }, CancellationToken.None));
            int items = host.Read(u => u.Set<Entities.Model.NasbNiro.Office.OfInboxItem>().Count(i => i.SourceModule == "tasks"));
            Assert.IsTrue(items >= 1, "the assignee has an inbox item");
        }

        [TestMethod]
        public void TheReminderOffsetsAreParsedAndValidated()
        {
            CollectionAssert.AreEqual(new List<int> { 7, 3, 1 }, TaskRules.ParseOffsets("1, 3,7"), "largest first");
            Assert.IsTrue(TaskRules.OffsetsValid("1,2,30"));
            Assert.IsFalse(TaskRules.OffsetsValid("1,x"));
            Assert.IsFalse(TaskRules.OffsetsValid("400"));
            Assert.AreEqual(new DateTime(2026, 1, 8), TaskRules.NextOccurrence(new DateTime(2026, 1, 1), TmRecurrence.Weekly, 1, null));
            Assert.IsTrue(TaskRules.NextOccurrence(new DateTime(2026, 1, 1), TmRecurrence.Monthly, 1, null) > new DateTime(2026, 1, 25), "a Persian month later");
            Assert.IsNull(TaskRules.NextOccurrence(new DateTime(2026, 1, 1), TmRecurrence.Monthly, 1, new DateTime(2026, 1, 15)), "past the end of the recurrence");
        }
    }
}
