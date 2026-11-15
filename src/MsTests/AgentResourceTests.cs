using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, station 4, priority 4: the planning agent drafts the resource plan from the pool (RES.PLAN.DRAFT); people supply the missing resources, review and submit it, and the project manager of the contract
    /// approves it; the finance manager proposes the standard rates and a second person approves them. The baseline waits for the plan and freezes its assignments.
    /// </summary>
    [TestClass]
    public class AgentResourceTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal Manager = AppHost.Principal(1081, "project.manager", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal FinMaker = AppHost.Principal(1201, "fin.one", "FINANCEMANAGER");
        private static readonly ClaimsPrincipal FinSecond = AppHost.Principal(1202, "fin.two", "FINANCEMANAGER");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentRes_" + name + "_" + Guid.NewGuid().ToString("N"));
            return new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-PLAN"); }
            await Run(e, "AG-MASTER");
        }

        private static List<AgentTask> Tasks(Env e) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().OrderBy(t => t.Id).ToList());
        private static List<AgentTask> Open(Env e, string step) => Tasks(e).Where(t => t.StepCode == step && t.State < AgentTaskState.Done).ToList();
        private static string Dump(Env e) => string.Join(" | ", Tasks(e).Select(t => t.StepCode + "#" + t.TargetId + ":" + t.State + ":" + t.LastError));

        private static AgentTask OpenOne(Env e, string step)
        {
            List<AgentTask> list = Open(e, step);
            Assert.AreEqual(1, list.Count, step + " open count: " + Dump(e));
            return list[0];
        }

        private static T Svc<T, S>(Env e, ClaimsPrincipal who, Func<S, Task<T>> body) => e.Host.As(who, s => body(s.GetRequiredService<S>())).GetAwaiter().GetResult();

        private static int SeedContract(Env e)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-500", Title = "ساخت پمپ", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-30), StartDate = DateTimeOffset.Now.Date, Price = 1_000_000_000m });
                u.SaveChanges();
                AppHost.Add(u, new WorkOrder { Code = "WO-1", Title = "پمپ شماره یک", ContractId = c.Id, Count = 1, EquipmentId = 1 });
                AppHost.Add(u, new PcContractReview { ContractId = c.Id, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept, Kind = PcReviewKind.StandardProduct });
                AppHost.Add(u, new PcContractTerms { ContractId = c.Id, CurrencyCode = "IRR", ValueExVat = 1_000_000_000m, ScheduleStartDate = DateTime.Today, ContractDeliveryDate = DateTime.Today.AddDays(200), ProjectCode = "026" + "01" + "MA" + "01", ProjectManagerUserId = 1081 });
                PcProjectInitiation init = AppHost.Add(u, new PcProjectInitiation { ContractId = c.Id, CurrentStep = PcInitiationStep.WbsDrafted, Steps = new List<PcInitiationStepRecord>() });
                foreach (PcInitiationStep s in Enum.GetValues(typeof(PcInitiationStep)))
                    init.Steps.Add(new PcInitiationStepRecord { Step = s, DoneAt = s <= PcInitiationStep.BomRequested ? DateTimeOffset.Now : (DateTimeOffset?)null });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        private static void SeedPool(Env e, params (string Code, PcResourceKind Kind, PcResourceUnit Unit, string Unit2)[] resources)
        {
            foreach (var r in resources)
            {
                ResponseDto<PcResourceDto> saved = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(new PcResourceDto { Code = r.Code, Title = "منبع " + r.Code, Kind = r.Kind, Unit = r.Unit, Capacity = 3m, ResponsibleUnit = r.Unit2 }, Ct));
                Assert.IsTrue(saved.Success, saved.Message);
            }
        }

        private static List<PcActivity> Activities(Env e, int contractId) => e.Host.Read(u => u.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted).OrderBy(a => a.Id).ToList());
        private static List<PcResourcePlan> Plans(Env e) => e.Host.Read(u => u.Set<PcResourcePlan>().AsNoTracking().OrderBy(p => p.Id).ToList());

        [TestMethod]
        public async Task The_Agent_Drafts_The_Plan_Once_From_The_Pool_And_The_Unmatched_Activities_Wait_For_The_Planner()
        {
            Env e = await NewEnv("draft");
            int contract = SeedContract(e);
            int planSeat = e.Seats["AG-PLAN"].UserId;

            // no resource in the pool: the agent has nothing to match and the planning chain is as it was
            await Settle(e);
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode.StartsWith("RES.", StringComparison.Ordinal)), Dump(e));
            List<PcActivity> acts = Activities(e, contract);
            Assert.AreEqual(4, acts.Count);
            // the generated activities are all of one unit; an activity of a unit that the pool will not serve is added
            string[] units = acts.Where(a => a.DurationDays > 0 && a.ResponsibleUnit != null).Select(a => a.ResponsibleUnit.Trim().ToUpperInvariant()).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.AreEqual(1, units.Length, "units of the activities that have work: " + string.Join(",", units));
            e.Host.Write(u => { AppHost.Add(u, new PcActivity { ContractId = contract, Code = "X-1", Title = "نصب در محل", DurationDays = 3, ResponsibleUnit = "ENGINEERING" }); u.SaveChanges(); }, Seeder);
            acts = Activities(e, contract);

            // the pool gets a resource for the unit of the generated activities only
            SeedPool(e, ("POOL-" + units[0], PcResourceKind.Labour, PcResourceUnit.Hour, units[0]));
            await Settle(e);
            AgentTask draft = Tasks(e).Last(t => t.StepCode == "RES.PLAN.DRAFT" && t.State == AgentTaskState.Done);
            Assert.AreEqual(4, draft.Station);
            Assert.AreEqual("AG-PLAN", draft.AgentCode);
            Assert.AreEqual(AgentStepKind.Agent, draft.Kind);
            Assert.AreEqual("PcResourcePlan", draft.OutputType);

            List<PcResourcePlan> plans = Plans(e);
            Assert.AreEqual(1, plans.Count, "one plan, however often the agent runs");
            PcResourcePlan plan = plans[0];
            Assert.IsTrue(plan.PreparedByAgent);
            Assert.AreEqual(planSeat, plan.MakerUserId);
            Assert.AreEqual(PcResourcePlanStatus.Draft, plan.Status);

            // the rows are the rule's rows: the same as the pure rule gives for the activities as they are now
            var expected = PcResourceRules.Draft(
                acts.Where(a => a.DurationDays > 0).Select(a => new PcResourceRules.DraftActivity { Id = a.Id, DurationDays = a.DurationDays, ResponsibleUnit = a.ResponsibleUnit, DayHours = PcResourceRules.DayHours(null, null) }),
                e.Host.Read(u => u.Set<PcResource>().AsNoTracking().OrderBy(r => r.Code).ToList()).Select(r => new PcResourceRules.DraftResource { Id = r.Id, Code = r.Code, Kind = r.Kind, Unit = r.Unit, ResponsibleUnit = r.ResponsibleUnit }));
            List<PcActivityResource> rows = e.Host.Read(u => u.Set<PcActivityResource>().AsNoTracking().OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).ToList());
            CollectionAssert.AreEqual(expected.Select(x => (x.ActivityId, x.ResourceId, x.Quantity)).ToArray(), rows.Select(x => (x.ActivityId, x.ResourceId, x.Quantity)).ToArray());
            Assert.IsTrue(rows.All(x => x.Source == PcAssignmentSource.Agent && x.Distribution == PcResourceDistribution.Uniform && x.EditedByUserId == planSeat));
            int unmatched = acts.Count(a => a.DurationDays > 0 && !rows.Any(r => r.ActivityId == a.Id));
            Assert.IsTrue(unmatched > 0, "an activity of a unit without a resource: " + string.Join(",", units));

            // no baseline is made while the plan is open
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode == "PLAN.BASELINE.CREATE"), Dump(e));

            // the planner is asked for the activities without a resource; nobody reviews the draft yet
            AgentTask input = OpenOne(e, "RES.POOL.INPUT");
            Assert.AreEqual(AgentTaskState.WaitingInput, input.State);
            Assert.AreEqual("PLANNING,PMO", input.GateRoles ?? "PLANNING,PMO");
            Assert.IsTrue(input.Summary.Contains(unmatched.ToString()), input.Summary);
            Assert.AreEqual(0, Open(e, "RES.PLAN.REVIEW").Count);

            // the agent run is idempotent
            int before = Tasks(e).Count;
            await Settle(e);
            Assert.AreEqual(1, Plans(e).Count);
            Assert.AreEqual(rows.Count, e.Host.Read(u => u.Set<PcActivityResource>().Count()));
            Assert.AreEqual(1, Tasks(e).Count(t => t.StepCode == "RES.PLAN.DRAFT"), Dump(e));

            // the planner marks the rest «بدون منبع»: the input closes and the review gate opens
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.MarkUnassignedAsync(new PcMarkUnassignedDto { PlanId = plan.Id, AllRemaining = true }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "RES.POOL.INPUT").Count, Dump(e));
            AgentTask review = OpenOne(e, "RES.PLAN.REVIEW");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, review.State);
            Assert.AreEqual("PLANNING,PMO", review.GateRoles);

            // a human edits: the human becomes the maker, the plan keeps the history of the agent
            int firstRowActivity = rows[0].ActivityId, firstRowResource = rows[0].ResourceId;
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto
            {
                PlanId = plan.Id, ActivityId = firstRowActivity, Lines = new List<PcAssignmentLineDto> { new PcAssignmentLineDto { ResourceId = firstRowResource, Quantity = 99m } },
            }, Ct)).Success);
            PcResourcePlan edited = Plans(e).Single();
            Assert.AreEqual(1051, edited.MakerUserId);
            Assert.IsTrue(edited.PreparedByAgent);

            // the submit is the planner's; then the gate names the project manager of the contract
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "submit" }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "RES.PLAN.REVIEW").Count);
            AgentTask approve = OpenOne(e, "RES.PLAN.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, approve.State);
            Assert.AreEqual(1081, approve.GateUserId);

            // a seat never approves
            string seat = await AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats["AG-PLAN"], "AG-PLAN",
                async sp => (await sp.GetRequiredService<IPcResourceService>().PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve" }, Ct)).Message);
            Assert.AreEqual(PcResourceText.AiDecision, seat);

            // return and fix
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "return", Note = "کمبود منبع" }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "RES.PLAN.APPROVE").Count);
            AgentTask fix = OpenOne(e, "RES.PLAN.FIX");
            Assert.AreEqual(AgentTaskState.WaitingInput, fix.State);
            Assert.IsTrue(fix.Summary.Contains("کمبود منبع"), fix.Summary);
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "submit" }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "RES.PLAN.FIX").Count);
            Assert.AreEqual(1081, OpenOne(e, "RES.PLAN.APPROVE").GateUserId);
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve" }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "RES.PLAN.APPROVE").Count);
            Assert.AreEqual(PcResourcePlanStatus.Approved, Plans(e).Single().Status);
            Assert.IsFalse(e.Seats.Values.Any(s => s.UserId == Plans(e).Single().ApprovedByUserId), "no seat id in the decision column");
            Assert.AreEqual(1, Plans(e).Count, "the approved plan is not drafted again");
        }

        [TestMethod]
        public async Task The_Baseline_Waits_For_The_Plan_Refuses_While_It_Is_Open_And_Freezes_The_Approved_Assignments()
        {
            Env e = await NewEnv("baseline");
            int contract = SeedContract(e);
            SeedPool(e, ("QC-INSPECTOR", PcResourceKind.Labour, PcResourceUnit.Hour, "QC"), ("WH-CREW", PcResourceKind.Labour, PcResourceUnit.Day, "WAREHOUSE"), ("COM-DESK", PcResourceKind.Labour, PcResourceUnit.Hour, "COMMERCIAL"));
            await Settle(e);
            List<PcActivityDto> dtos = Svc<ResponseDto<PagedList<PcActivityDto>>, IPcScheduleService>(e, Planner, s => s.GetActivitiesAsync(new PcActivityQuery { ContractId = contract, PageSize = 100 }, Ct)).Data.ToList();
            foreach (PcActivityDto d in dtos) { d.BudgetAtCompletion = 250_000_000m; Assert.IsTrue(Svc<ResponseDto<PcActivityDto>, IPcScheduleService>(e, Planner, s => s.SaveActivityAsync(d, Ct)).Success); }
            await Settle(e);

            // the plan is open: the baseline agent holds back and a direct request is refused with a clear message
            PcResourcePlan plan = Plans(e).Single();
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode == "PLAN.BASELINE.CREATE"), Dump(e));
            ResponseDto<PcBaselineDto> refused = Svc<ResponseDto<PcBaselineDto>, IPcBaselineService>(e, Planner, s => s.CreateBaselineAsync(new PcBaselineCreateDto { ContractId = contract }, Ct));
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.Contains("طرح منابع"), refused.Message);

            // submit (the planner marks anything unassigned), approve
            Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.MarkUnassignedAsync(new PcMarkUnassignedDto { PlanId = plan.Id, AllRemaining = true }, Ct));
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "submit" }, Ct)).Success);
            Assert.IsFalse(Svc<ResponseDto<PcBaselineDto>, IPcBaselineService>(e, Planner, s => s.CreateBaselineAsync(new PcBaselineCreateDto { ContractId = contract }, Ct)).Success, "a submitted plan holds the baseline too");
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve" }, Ct)).Success);
            int approvedRows = e.Host.Read(u => u.Set<PcActivityResource>().Count(x => x.PlanId == plan.Id));
            Assert.IsTrue(approvedRows > 0);

            await Settle(e);
            PcBaseline baseline = e.Host.Read(u => u.Set<PcBaseline>().AsNoTracking().Single(b => b.ContractId == contract));
            Assert.AreEqual(PcBaselineStatus.Draft, baseline.Status);
            Assert.AreEqual(approvedRows, e.Host.Read(u => u.Set<PcBaselineResource>().Count(x => x.BaselineId == baseline.Id)), "one frozen row per approved assignment");
            Assert.IsTrue(e.Host.Read(u => u.Set<PcBaselineResource>().AsNoTracking().Where(x => x.BaselineId == baseline.Id).ToList()).All(x => x.RateAtBaseline == null), "no approved rate: nothing is frozen as a rate");

            // after the plan is changed by a new approved version the draft baseline is stale for the resources
            PcResourcePlanDto next = Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = contract }, Ct)).Data;
            Assert.AreEqual(2, next.Version);
            ResponseDto<List<PcBaselineResourceDto>> view = Svc<ResponseDto<List<PcBaselineResourceDto>>, IPcResourceService>(e, Planner, s => s.GetBaselineResourcesAsync(baseline.Id, Ct));
            Assert.AreEqual(approvedRows, view.Data.Count);

            // the stale draft: version 2 is approved with a different assignment while the draft baseline of version 1 still waits
            PcActivity target = Activities(e, contract).First(a => a.DurationDays > 0 && a.ResponsibleUnit != null && e.Host.Read(u => u.Set<PcResource>().Any(r => r.ResponsibleUnit == a.ResponsibleUnit)));
            int resourceId = e.Host.Read(u => u.Set<PcResource>().First(r => r.ResponsibleUnit == target.ResponsibleUnit).Id);
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = next.Id, ActivityId = target.Id, Lines = new List<PcAssignmentLineDto> { new PcAssignmentLineDto { ResourceId = resourceId, Quantity = 77m } } }, Ct)).Success);
            Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.MarkUnassignedAsync(new PcMarkUnassignedDto { PlanId = next.Id, AllRemaining = true }, Ct));
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = next.Id, Action = "submit" }, Ct)).Success);
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = next.Id, Action = "approve" }, Ct)).Success);
            // the old draft (all managers signed) cannot be approved any more
            e.Host.Write(u => { u.Set<PcBaseline>().Single(b => b.Id == baseline.Id).Status = PcBaselineStatus.ManagersSigned; u.SaveChanges(); }, Seeder);
            ResponseDto<PcBaselineDto> stale = Svc<ResponseDto<PcBaselineDto>, IPcBaselineService>(e, Pmo, s => s.BaselineActionAsync(new PcBaselineActionDto { Id = baseline.Id, Action = "approve" }, Ct));
            Assert.IsFalse(stale.Success);
            Assert.IsTrue(stale.Message.Contains("طرح منابع تاییدشده پس از ساخت"), stale.Message);
            // a new baseline can be created: the stale draft is withdrawn and the new rows are those of version 2
            ResponseDto<PcBaselineDto> fresh = Svc<ResponseDto<PcBaselineDto>, IPcBaselineService>(e, Planner, s => s.CreateBaselineAsync(new PcBaselineCreateDto { ContractId = contract }, Ct));
            Assert.IsTrue(fresh.Success, fresh.Message);
            Assert.AreNotEqual(baseline.Id, fresh.Data.Id);
            Assert.AreEqual(0, e.Host.Read(u => u.Set<PcBaseline>().Count(b => b.Id == baseline.Id && !b.Deleted && b.Status != PcBaselineStatus.Superseded)), "the stale draft is withdrawn");
            List<PcBaselineResource> frozen = e.Host.Read(u => u.Set<PcBaselineResource>().AsNoTracking().Where(x => x.BaselineId == fresh.Data.Id).ToList());
            Assert.IsTrue(frozen.Any(x => x.ActivityId == target.Id && x.ResourceId == resourceId && x.Quantity == 77m), "the rows are those of version 2");
        }

        [TestMethod]
        public async Task A_Project_Without_A_Resource_Plan_Is_Baselined_As_Before()
        {
            Env e = await NewEnv("noplan");
            int contract = SeedContract(e);
            await Settle(e);
            List<PcActivityDto> dtos = Svc<ResponseDto<PagedList<PcActivityDto>>, IPcScheduleService>(e, Planner, s => s.GetActivitiesAsync(new PcActivityQuery { ContractId = contract, PageSize = 100 }, Ct)).Data.ToList();
            foreach (PcActivityDto d in dtos) { d.BudgetAtCompletion = 250_000_000m; Svc<ResponseDto<PcActivityDto>, IPcScheduleService>(e, Planner, s => s.SaveActivityAsync(d, Ct)); }
            await Settle(e);
            PcBaseline baseline = e.Host.Read(u => u.Set<PcBaseline>().AsNoTracking().Single(b => b.ContractId == contract));
            Assert.AreEqual(PcBaselineStatus.Draft, baseline.Status);
            Assert.AreEqual(0, e.Host.Read(u => u.Set<PcBaselineResource>().Count()));
            Assert.AreEqual(0, Plans(e).Count);
        }

        [TestMethod]
        public async Task The_Rate_Steps_Ask_The_Finance_Manager_For_Used_Resources_And_A_Second_Person_To_Approve()
        {
            Env e = await NewEnv("rates");
            int contract = SeedContract(e);
            SeedPool(e, ("QC-INSPECTOR", PcResourceKind.Labour, PcResourceUnit.Hour, "QC"), ("WH-CREW", PcResourceKind.Labour, PcResourceUnit.Day, "WAREHOUSE"));
            await Settle(e);
            int[] usedIds = e.Host.Read(u => u.Set<PcActivityResource>().Select(x => x.ResourceId).Distinct().OrderBy(x => x).ToArray());
            Assert.IsTrue(usedIds.Length > 0, Dump(e));
            List<AgentTask> inputs = Open(e, "RES.RATE.INPUT");
            Assert.AreEqual(usedIds.Length, inputs.Count, "one task for each used resource without an approved rate: " + Dump(e));
            Assert.IsTrue(inputs.All(t => t.State == AgentTaskState.WaitingInput && t.GateRoles == "FINANCEMANAGER"));

            int target = int.Parse(inputs[0].TargetId.ToString());
            PcResourceRateDto rate = Svc<ResponseDto<PcResourceRateDto>, IPcResourceService>(e, FinMaker, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = target, Rate = 250_000m, EffectiveFrom = DateTime.Today.AddDays(-1) }, Ct)).Data;
            Svc<ResponseDto<PcResourceRateDto>, IPcResourceService>(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = rate.Id, Action = "submit" }, Ct));
            await Settle(e);
            Assert.AreEqual(usedIds.Length - 1, Open(e, "RES.RATE.INPUT").Count, "the proposed rate closes its input");
            AgentTask gate = OpenOne(e, "RES.RATE.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            Assert.AreEqual("FINANCEMANAGER,CEO", gate.GateRoles);
            Assert.IsFalse(gate.Summary.Contains("250"), "the amount is not in the task text");
            Assert.IsTrue(Svc<ResponseDto<PcResourceRateDto>, IPcResourceService>(e, FinSecond, s => s.RateActionAsync(new PcResourceRateActionDto { Id = rate.Id, Action = "approve" }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "RES.RATE.APPROVE").Count);
        }

        [TestMethod]
        public async Task The_Plan_Of_A_Project_Whose_Baseline_Exists_Is_Drafted_Again_Only_For_An_Approved_Change_Request()
        {
            Env e = await NewEnv("change");
            int contract = SeedContract(e);
            SeedPool(e, ("QC-INSPECTOR", PcResourceKind.Labour, PcResourceUnit.Hour, "QC"), ("WH-CREW", PcResourceKind.Labour, PcResourceUnit.Day, "WAREHOUSE"), ("COM-DESK", PcResourceKind.Labour, PcResourceUnit.Hour, "COMMERCIAL"));
            await Settle(e);
            // the first plan is approved by hand
            PcResourcePlan first = Plans(e).Single();
            Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.MarkUnassignedAsync(new PcMarkUnassignedDto { PlanId = first.Id, AllRemaining = true }, Ct));
            Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = first.Id, Action = "submit" }, Ct));
            Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = first.Id, Action = "approve" }, Ct));
            // a baseline exists
            e.Host.Write(u =>
            {
                PcBaseline b = AppHost.Add(u, new PcBaseline { ContractId = contract, Number = 0, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Approved, ProjectStart = DateTime.Today, ProjectFinish = DateTime.Today.AddDays(5), ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1011 });
                u.SaveChanges();
                u.Set<PcContractTerms>().Single(t => t.ContractId == contract).CurrentBaselineId = b.Id;
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            Assert.AreEqual(1, Plans(e).Count, "locked: the agent does not draft a version without a change request");
            e.Host.Write(u =>
            {
                PcChangeRequest cr = AppHost.Add(u, new PcChangeRequest { ContractId = contract, Number = "CR-9", Decision = PcChangeDecision.Approve, Status = PcChangeStatus.Implementing });
                u.SaveChanges();
                // the request is approved but built no baseline yet: the agent does not draft
                // the repeat guard (30 minutes) of the agent steps: the first draft was made long ago
                foreach (AgentTask t in u.Set<AgentTask>().Where(x => x.StepCode == "RES.PLAN.DRAFT")) t.ClosedAt = DateTimeOffset.Now.AddHours(-1);
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            Assert.AreEqual(1, Plans(e).Count, "an approved request that built no baseline opens no plan: " + Dump(e));
            // the request builds its baseline (now the current one): the agent drafts the version
            e.Host.Write(u =>
            {
                int crId = u.Set<PcChangeRequest>().Single(c => c.Number == "CR-9").Id;
                PcBaseline b = AppHost.Add(u, new PcBaseline { ContractId = contract, Number = 1, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Approved, ChangeRequestId = crId, ProjectStart = DateTime.Today, ProjectFinish = DateTime.Today.AddDays(5), ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1011 });
                u.SaveChanges();
                u.Set<PcContractTerms>().Single(t => t.ContractId == contract).CurrentBaselineId = b.Id;
                foreach (AgentTask t in u.Set<AgentTask>().Where(x => x.StepCode == "RES.PLAN.DRAFT")) t.ClosedAt = DateTimeOffset.Now.AddHours(-1);
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            List<PcResourcePlan> plans = Plans(e);
            Assert.AreEqual(2, plans.Count, Dump(e));
            Assert.AreEqual(2, plans[1].Version);
            Assert.IsNotNull(plans[1].ChangeRequestId);
            Assert.IsTrue(plans[1].PreparedByAgent);
            await Settle(e);
            Assert.AreEqual(2, Plans(e).Count, "once per change request");
        }
    }
}
