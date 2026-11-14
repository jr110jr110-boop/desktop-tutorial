using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>Agent layer, stations 2 (contract review) and 3 (initiation): the agents draft the terms, the review, the project code, the charter and the plan; people decide at the gates.</summary>
    [TestClass]
    public class AgentContractInitTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Buyer = AppHost.Principal(1026, "buyer", "COMMERCIAL");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(1031, "ceo", "CEO");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(1081, "project.manager", "PROJECTMANAGER");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentCon_" + name + "_" + Guid.NewGuid().ToString("N"));
            Dictionary<string, AiActingUser> seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct));
            host.Write(u => { foreach (int id in new[] { 1011, 1026, 1031, 1051, 1081 }) AppHost.Add(u, new User { Id = id, UserName = "con.user" + id, Email = "con.user" + id + "@example.com", IsActive = true }); u.SaveChanges(); }, Seeder);
            return new Env { Host = host, Seats = seats };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        /// <summary>The master finds the work, then each process agent does what is due (twice, so that a step opened by the previous one is done too).</summary>
        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 2; i++)
            {
                await Run(e, "AG-MASTER");
                await Run(e, "AG-CONTRACT");
                await Run(e, "AG-INIT");
            }
            await Run(e, "AG-MASTER");
        }

        private static List<AgentTask> Tasks(Env e) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().OrderBy(t => t.Id).ToList());
        private static List<AgentTask> Open(Env e, string step) => Tasks(e).Where(t => t.StepCode == step && t.State < AgentTaskState.Done).ToList();
        private static AgentTask OpenOne(Env e, string step)
        {
            List<AgentTask> list = Open(e, step);
            Assert.AreEqual(1, list.Count, step + " open count: " + Dump(e));
            return list[0];
        }
        private static AgentTask Closed(Env e, string step)
        {
            AgentTask t = Tasks(e).SingleOrDefault(x => x.StepCode == step && x.State == AgentTaskState.Done);
            Assert.IsNotNull(t, step + " not done: " + Dump(e));
            return t;
        }
        private static string Dump(Env e) => string.Join(" | ", Tasks(e).Select(t => t.StepCode + "#" + t.TargetId + ":" + t.State + ":" + t.LastError));

        private static T Svc<T>(Env e, ClaimsPrincipal who, Func<IPcProjectService, Task<T>> body) => e.Host.As(who, s => body(s.GetRequiredService<IPcProjectService>())).GetAwaiter().GetResult();

        private sealed class World
        {
            public int Contract, Equipment;
        }

        private static World SeedContract(Env e, string code, int? equipmentId = null, bool guarantee = false, bool fullTerms = false)
        {
            var w = new World();
            e.Host.Write(u =>
            {
                Equipment eq = equipmentId.HasValue ? u.Set<Equipment>().Single(x => x.Id == equipmentId.Value) : AppHost.Add(u, new Equipment { Code = "EQ-1", Title = "پمپ گریز از مرکز" });
                ContractEntity c = AppHost.Add(u, new ContractEntity
                {
                    Code = code, Title = "ساخت پمپ " + code, Employer = "شرکت کارفرما", Site = "اهواز", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-3), StartDate = DateTimeOffset.Now.Date,
                    DeliverDate = DateTimeOffset.Now.Date.AddDays(200), Price = 1_000_000_000m, Type = "ساخت",
                });
                u.SaveChanges();
                AppHost.Add(u, new ContractEquipment { ContractId = c.Id, EquipmentId = eq.Id, Count = 2 });
                if (guarantee) AppHost.Add(u, new ContractGuaranty { Title = "حسن انجام کار", ContractEntityId = c.Id });
                if (fullTerms) AppHost.Add(u, new PcContractTerms { ContractId = c.Id, CurrencyCode = "IRR", ValueExVat = 1_000_000_000m, ContractDeliveryDate = DateTime.Today.AddDays(200), ScheduleStartDate = DateTime.Today, AdvancePercent = 10m, RetentionPercent = 5m, LatePenaltyPercentPerWeek = 0.5m, PenaltyCapPercent = 10m });
                u.SaveChanges();
                w.Contract = c.Id; w.Equipment = eq.Id;
            }, Seeder);
            return w;
        }

        private static PcContractReview ReviewOf(Env e, int contractId) => e.Host.Read(u => u.Set<PcContractReview>().AsNoTracking().Single(r => r.ContractId == contractId));

        private static void AssertNoSeatInDecisionColumns(Env e)
        {
            int[] seatIds = e.Seats.Values.Select(s => s.UserId).ToArray();
            e.Host.Read(u =>
            {
                foreach (PcContractReview r in u.Set<PcContractReview>().AsNoTracking())
                {
                    Assert.IsFalse(r.PmoAcceptedByUserId.HasValue && seatIds.Contains(r.PmoAcceptedByUserId.Value), "PMO signature");
                    Assert.IsFalse(r.CommercialAcceptedByUserId.HasValue && seatIds.Contains(r.CommercialAcceptedByUserId.Value), "commercial signature");
                    Assert.IsFalse(r.CapabilityCheckedByUserId.HasValue && seatIds.Contains(r.CapabilityCheckedByUserId.Value), "capability");
                    Assert.IsFalse(r.DecidedByUserId.HasValue && seatIds.Contains(r.DecidedByUserId.Value), "decision");
                }
                foreach (PcProjectCharter c in u.Set<PcProjectCharter>().AsNoTracking())
                    Assert.IsFalse(c.ApprovedByUserId.HasValue && seatIds.Contains(c.ApprovedByUserId.Value), "charter approval");
                foreach (PcProjectPlan p in u.Set<PcProjectPlan>().AsNoTracking())
                    Assert.IsFalse(p.ApprovedByUserId.HasValue && seatIds.Contains(p.ApprovedByUserId.Value), "plan approval");
                return 0;
            });
        }

        [TestMethod]
        public async Task The_Contract_Chain_From_The_Record_To_The_Kick_Off_Is_Drafted_By_The_Agents_And_Decided_By_People()
        {
            Env e = await NewEnv("chain");
            World w = SeedContract(e, "C-100");
            int contractUser = e.Seats["AG-CONTRACT"].UserId, initUser = e.Seats["AG-INIT"].UserId;

            // station 2: the agent writes the terms the record holds; the percentages exist only on the signed paper contract: the one Input
            await Settle(e);
            AgentTask terms = Closed(e, "CON.TERMS.DRAFT");
            Assert.AreEqual("AG-CONTRACT", terms.AgentCode);
            Assert.AreEqual(2, terms.Station);
            Assert.AreEqual(AgentStepKind.Agent, terms.Kind);
            PcContractTerms t = e.Host.Read(u => u.Set<PcContractTerms>().AsNoTracking().Single(x => x.ContractId == w.Contract));
            Assert.AreEqual(1_000_000_000m, t.ValueExVat);
            Assert.AreEqual("IRR", t.CurrencyCode);
            Assert.AreEqual(DateTime.Today.AddDays(200), t.ContractDeliveryDate);
            Assert.AreEqual(DateTime.Today, t.ScheduleStartDate);
            Assert.AreEqual(contractUser, t.CreatorUserId);
            AgentTask input = OpenOne(e, "CON.TERMS.INPUT");
            Assert.AreEqual(AgentTaskState.WaitingInput, input.State);
            Assert.AreEqual("PMO,COMMERCIAL", input.GateRoles);
            StringAssert.Contains(input.Summary, "درصد پیش‌پرداخت");
            StringAssert.Contains(input.Summary, "سقف جریمه");
            Assert.IsFalse(input.Summary.Contains("مبلغ قرارداد"), "the amount comes from the record");
            Assert.AreEqual(0, Open(e, "CON.REVIEW.DRAFT").Count, "no review before the terms are complete");

            // the commercial person types the paper contract percentages
            PcContractTermsDto dto = Svc(e, Buyer, s => s.GetTermsAsync(w.Contract, Ct)).Data;
            dto.AdvancePercent = 10m; dto.RetentionPercent = 5m; dto.LatePenaltyPercentPerWeek = 0.5m; dto.PenaltyCapPercent = 10m;
            Assert.IsTrue(Svc(e, Buyer, s => s.SaveTermsAsync(dto, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "CON.TERMS.INPUT").Count);

            // the review is drafted with every answer the contract gives; a special order (the equipment has no accepted contract before)
            Closed(e, "CON.REVIEW.DRAFT");
            PcContractReview r = ReviewOf(e, w.Contract);
            Assert.AreEqual(contractUser, r.CreatorUserId);
            Assert.AreEqual(PcReviewKind.SpecialOrder, r.Kind);
            Assert.IsTrue(r.RequirementsDefined && r.DifferencesResolved && r.DeliveryFeasible && r.PenaltiesReviewed && r.PaymentTermsReviewed && r.CurrencyReviewed);
            Assert.IsFalse(r.StandardsItpAgreed, "no earlier accepted contract used this equipment");
            Assert.IsFalse(r.GuaranteesReviewed, "no guarantee is recorded for the contract");
            AgentTask complete = OpenOne(e, "CON.REVIEW.COMPLETE");
            Assert.AreEqual(AgentTaskState.WaitingInput, complete.State);
            StringAssert.Contains(complete.Summary, "استانداردها و ITP");
            StringAssert.Contains(complete.Summary, "ضمانت‌نامه‌ها");
            Assert.IsFalse(complete.Summary.Contains("جرایم"), "the penalties come from the terms");
            AgentTask capability = OpenOne(e, "CON.REVIEW.CAPABILITY");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, capability.State);
            Assert.AreEqual("PLANNING", capability.GateRoles);
            Assert.AreEqual(0, Open(e, "CON.REVIEW.ACCEPT").Count, "no signature on an incomplete checklist");

            // planning ticks the two judgement items and confirms the capability (an editor of the review does not sign it)
            PcReviewDto rd = new PcReviewDto { Id = r.Id, ContractId = w.Contract, Kind = PcReviewKind.SpecialOrder, RequirementsDefined = true, DifferencesResolved = true, DeliveryFeasible = true, StandardsItpAgreed = true, GuaranteesReviewed = true, PenaltiesReviewed = true, PaymentTermsReviewed = true, CurrencyReviewed = true, Notes = r.Notes };
            Assert.IsTrue(Svc(e, Planner, s => s.SaveReviewAsync(rd, Ct)).Success);
            Assert.IsTrue(Svc(e, Planner, s => s.ReviewActionAsync(new PcReviewActionDto { Id = r.Id, Action = "capability-check" }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "CON.REVIEW.COMPLETE").Count);
            Assert.AreEqual(0, Open(e, "CON.REVIEW.CAPABILITY").Count);
            AgentTask accept = OpenOne(e, "CON.REVIEW.ACCEPT");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, accept.State);
            Assert.AreEqual("PMO,COMMERCIAL", accept.GateRoles);

            // the planner who edited cannot sign; the PMO and the commercial person sign (the seat that made the draft never does)
            Assert.IsFalse(Svc(e, AppHost.Principal(1051, "planner", "PLANNING", "PMO"), s => s.ReviewActionAsync(new PcReviewActionDto { Id = r.Id, Action = "accept", Slot = "pmo" }, Ct)).Success);
            Assert.IsTrue(Svc(e, Pmo, s => s.ReviewActionAsync(new PcReviewActionDto { Id = r.Id, Action = "accept" }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual("COMMERCIAL", OpenOne(e, "CON.REVIEW.ACCEPT").GateRoles, "only the commercial signature is left");
            Assert.IsTrue(Svc(e, Buyer, s => s.ReviewActionAsync(new PcReviewActionDto { Id = r.Id, Action = "accept" }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual(PcReviewDecision.Accept, ReviewOf(e, w.Contract).Decision);
            Assert.AreEqual(0, Open(e, "CON.REVIEW.ACCEPT").Count);

            // station 3: initiation, the code and the notice of the units by the agent; the CEO selects the manager
            await Settle(e);
            Closed(e, "INIT.START"); Closed(e, "INIT.CODE"); Closed(e, "INIT.UNITS");
            Assert.AreEqual(3, Closed(e, "INIT.CODE").Station);
            Assert.AreEqual("AG-INIT", Closed(e, "INIT.CODE").AgentCode);
            PcContractTerms coded = e.Host.Read(u => u.Set<PcContractTerms>().AsNoTracking().Single(x => x.ContractId == w.Contract));
            Assert.AreEqual(PcCategory.Manufacturing, coded.Category);
            Assert.AreEqual(9, coded.ProjectCode.Length);
            AgentTask manager = OpenOne(e, "INIT.MANAGER");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, manager.State);
            Assert.AreEqual("CEO", manager.GateRoles);
            Assert.AreEqual(0, Open(e, "INIT.CODE.INPUT").Count);

            // the charter and the plan are drafted from the contract (not empty skeletons)
            await Settle(e);
            Closed(e, "INIT.CHARTER.DRAFT");
            PcProjectCharter charter = e.Host.Read(u => u.Set<PcProjectCharter>().AsNoTracking().Single(x => x.ContractId == w.Contract));
            Assert.AreEqual(initUser, charter.CreatorUserId);
            StringAssert.Contains(charter.Overview, coded.ProjectCode);
            StringAssert.Contains(charter.ScopeDescription, "پمپ گریز از مرکز × 2");
            StringAssert.Contains(charter.Constraints, "0.5");
            StringAssert.Contains(charter.TimeBaselineEstimate, "200 روز");
            Assert.IsFalse(string.IsNullOrWhiteSpace(charter.Risks) || string.IsNullOrWhiteSpace(charter.ManagerAuthority) || string.IsNullOrWhiteSpace(charter.Deliverables));
            Assert.AreEqual(0, Open(e, "INIT.CHARTER.ACCEPT").Count, "no manager yet");

            ResponseDto<PcInitiationDto> picked = Svc(e, Ceo, s => s.InitiationActionAsync(new PcInitiationActionDto { ContractId = w.Contract, Step = PcInitiationStep.ManagerSelected, ProjectManagerUserId = 1081 }, Ct));
            Assert.IsTrue(picked.Success, picked.Message + " | " + Dump(e));
            await Settle(e);
            PcProjectPlan plan = e.Host.Read(u => u.Set<PcProjectPlan>().AsNoTracking().Single(x => x.ContractId == w.Contract));
            StringAssert.Contains(plan.ScopeStatement, "پمپ گریز از مرکز × 2");
            StringAssert.Contains(plan.Milestones, "تحویل قراردادی");
            Assert.IsFalse(string.IsNullOrWhiteSpace(plan.QualityPlan) || string.IsNullOrWhiteSpace(plan.CommunicationPlan));
            AgentTask accepting = OpenOne(e, "INIT.CHARTER.ACCEPT");
            Assert.AreEqual("PROJECTMANAGER", accepting.GateRoles);
            Assert.IsTrue(Svc(e, ProjectManager, s => s.CharterActionAsync("accept", new PcApproveDto { ContractId = w.Contract }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual("CEO", OpenOne(e, "INIT.CHARTER.APPROVE").GateRoles);
            Assert.IsTrue(Svc(e, Ceo, s => s.CharterActionAsync("approve", new PcApproveDto { ContractId = w.Contract }, Ct)).Success);
            await Settle(e);
            Assert.AreEqual("PROJECTMANAGER,CEO", OpenOne(e, "INIT.PEP.APPROVE").GateRoles);
            Assert.IsTrue(Svc(e, ProjectManager, s => s.ApprovePlanAsync(new PcApproveDto { ContractId = w.Contract }, Ct)).Success);
            await Settle(e);

            // the kick-off meeting is physical; the request of the BOM follows by the agent
            AgentTask kom = OpenOne(e, "INIT.KOM");
            Assert.AreEqual(AgentStepKind.Physical, kom.Kind);
            Assert.AreEqual(AgentTaskState.WaitingPhysical, kom.State);
            Assert.AreEqual("PROJECTMANAGER", kom.GateRoles);
            Assert.AreEqual(0, Open(e, "INIT.BOM").Count);
            Assert.IsTrue(Svc(e, ProjectManager, s => s.InitiationActionAsync(new PcInitiationActionDto { ContractId = w.Contract, Step = PcInitiationStep.KomHeld }, Ct)).Success);
            await Settle(e);
            Closed(e, "INIT.BOM");

            // the initiation is complete up to the BOM request and the board says so for stations 2 and 3
            ResponseDto<AgentBoardDto> boardResponse = await e.Host.As(Pmo, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(w.Contract, Ct));
            AgentBoardDto board = boardResponse.Data;
            Assert.AreEqual(AgentCellStatus.Done, board.Rows.Single(x => x.ContractId == w.Contract).Cells.Single(c => c.Station == 2).Status);
            Assert.AreEqual(AgentCellStatus.Done, board.Rows.Single(x => x.ContractId == w.Contract).Cells.Single(c => c.Station == 3).Status);

            AssertNoSeatInDecisionColumns(e);

            // every human Input of the chain was justified: CON.TERMS.INPUT (percentages only on the paper contract), CON.REVIEW.COMPLETE (judgement: standards/ITP and guarantees of a special order)
            string[] inputs = Tasks(e).Where(x => x.Kind == AgentStepKind.Input).Select(x => x.StepCode).Distinct().OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { "CON.REVIEW.COMPLETE", "CON.TERMS.INPUT" }, inputs, Dump(e));
        }

        [TestMethod]
        public async Task A_Standard_Product_With_Complete_Terms_Needs_Only_The_Two_Signatures()
        {
            Env e = await NewEnv("standard");
            World first = SeedContract(e, "C-200", guarantee: true, fullTerms: true);
            // the first contract is accepted by hand (history): the second one with the same equipment is a standard product
            e.Host.Write(u => { AppHost.Add(u, new PcContractReview { ContractId = first.Contract, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept, Kind = PcReviewKind.SpecialOrder, DecidedAt = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            World second = SeedContract(e, "C-201", first.Equipment, guarantee: true, fullTerms: true);

            await Settle(e);
            PcContractReview r = ReviewOf(e, second.Contract);
            Assert.AreEqual(PcReviewKind.StandardProduct, r.Kind, r.Notes);
            Assert.IsTrue(r.RequirementsDefined && r.DifferencesResolved && r.DeliveryFeasible && r.StandardsItpAgreed && r.GuaranteesReviewed && r.PenaltiesReviewed && r.PaymentTermsReviewed && r.CurrencyReviewed);
            Assert.AreEqual(0, Tasks(e).Count(x => x.Kind == AgentStepKind.Input), "nothing is left for a person to type: " + Dump(e));
            Assert.AreEqual(0, Open(e, "CON.REVIEW.CAPABILITY").Count);
            AgentTask accept = OpenOne(e, "CON.REVIEW.ACCEPT");
            Assert.AreEqual(second.Contract, accept.ContractId);
            Assert.AreEqual("PMO,COMMERCIAL", accept.GateRoles);
            Assert.AreEqual(0, Tasks(e).Count(x => x.StepCode == "CON.REVIEW.DRAFT" && x.ContractId == first.Contract), "a contract that already has a review gets no draft");
            AssertNoSeatInDecisionColumns(e);
        }
    }
}
