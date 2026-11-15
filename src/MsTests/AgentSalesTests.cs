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
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.SalesDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, station 1 (sales): the bid checklist is an Input, the agent sends a complete one, the CEO decides; the agent estimates a repeat product from the equipment templates of the latest won inquiry,
    /// sales finalises a complete estimate and the agent drafts the proposal; the CEO (above the threshold or below cost) or sales approves; the tender is reviewed by the PMO deputy and the commercial unit; people send it and record the answer. A seat never decides, approves or records a result.
    /// </summary>
    [TestClass]
    public class AgentSalesTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Sales1 = AppHost.Principal(1801, "sales.one", "SALES");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(1802, "ceo.one", "CEO");
        private static readonly ClaimsPrincipal Commercial1 = AppHost.Principal(1803, "commercial.one", "COMMERCIAL");
        private static readonly ClaimsPrincipal PmoDeputy = AppHost.Principal(1804, "pmo.deputy", "PMO");
        private static readonly ClaimsPrincipal Commercial2 = AppHost.Principal(1805, "commercial.two", "COMMERCIAL");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            AppHost host = new AppHost("AgentSl_" + name + "_" + Guid.NewGuid().ToString("N"));
            return new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 4; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-SALES"); }
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

        private static AgentTask Done(Env e, string step)
        {
            AgentTask t = Tasks(e).LastOrDefault(x => x.StepCode == step && x.State == AgentTaskState.Done);
            Assert.IsNotNull(t, step + " not done: " + Dump(e));
            return t;
        }

        private static void AssertRoles(string expected, AgentTask task) => CollectionAssert.AreEquivalent(expected.Split(','), task.GateRoles.Split(','), task.StepCode + " roles " + task.GateRoles);

        private static bool IsSeat(Env e, int? id) => id.HasValue && e.Seats.Values.Any(s => s.UserId == id.Value);

        private static Task<T> As<T>(Env e, ClaimsPrincipal who, Func<IServiceProvider, Task<T>> call) => e.Host.As(who, call);

        /// <summary>A customer with a won, estimated and proposed inquiry (200 days old: the history), a new inquiry of the same product type and quantity, and the equipment template the history used.</summary>
        private static (int Customer, int Equipment, int Old, int New) Seed(Env e, bool newIsRepeat, int? contract = null)
        {
            int customer = 0, equipment = 0, old = 0, fresh = 0;
            e.Host.Write(u =>
            {
                FnCustomer c = AppHost.Add(u, new FnCustomer { Name = "شرکت برق", IsActive = true });
                Equipment eq = AppHost.Add(u, new Equipment { Code = "EQ-T", Title = "توربین بخار" });
                u.SaveChanges();
                EquipmentAssembly asm = AppHost.Add(u, new EquipmentAssembly { Code = "A1", Title = "روتور", EquipmentId = eq.Id });
                u.SaveChanges();
                AppHost.Add(u, new EquipmentBom { EquipmentAssemblyId = asm.Id, PositionNumber = 1, Title = "شفت", Count = 1, Unit = "عدد" });
                SlInquiry o = AppHost.Add(u, new SlInquiry { Number = "INQ-OLD", ReceivedDate = DateTime.Today.AddDays(-200), CustomerId = c.Id, Title = "توربین قبلی", ProductType = SlProductType.SteamTurbine, Quantity = 2, Status = SlInquiryStatus.Won, CreatorUserId = 1001 });
                u.SaveChanges();
                SlEstimate oe = AppHost.Add(u, new SlEstimate { Number = "INQ-OLD/E1", InquiryId = o.Id, Revision = 1, Status = SlEstimateStatus.Final, Price = 5_000_000_000m, TotalCost = 4_000_000_000m, OverheadPercent = 10m, ContingencyPercent = 5m, TargetMarginPercent = 20m });
                u.SaveChanges();
                u.Set<SlEstimateEquipment>().Add(new SlEstimateEquipment { EstimateId = oe.Id, EquipmentId = eq.Id, Count = 2 });
                AppHost.Add(u, new SlProposal { Number = "PRP-OLD", Revision = 1, InquiryId = o.Id, EstimateId = oe.Id, Price = 5_000_000_000m, Status = SlProposalStatus.Won, ValidityDays = 30, PaymentTermsText = "۳۰ درصد پیش‌پرداخت", GuaranteesText = "ضمانت ۱۲ ماه", AdvancePercent = 30m, ContractId = contract });
                SlInquiry n = AppHost.Add(u, new SlInquiry
                {
                    Number = "INQ-NEW", ReceivedDate = DateTime.Today, CustomerId = c.Id, Title = "توربین جدید", ProductType = newIsRepeat ? SlProductType.SteamTurbine : SlProductType.Condenser, Quantity = 2,
                    RequiredDeliveryDate = DateTime.Today.AddDays(70), CustomerReference = "RFQ-77", Status = SlInquiryStatus.Received,
                });
                u.SaveChanges();
                customer = c.Id; equipment = eq.Id; old = o.Id; fresh = n.Id;
            }, Seeder);
            return (customer, equipment, old, fresh);
        }

        // ------------------------------------------------------------------------------------------------ the whole line

        [TestMethod]
        public async Task A_Repeat_Product_Goes_From_The_Checklist_To_A_Recorded_Result_With_The_Agent_Estimating_And_Drafting_And_People_Deciding()
        {
            Env e = await NewEnv("full");
            var (_, equipment, old, fresh) = Seed(e, true);
            int seat = e.Seats["AG-SALES"].UserId;

            // the inquiry is registered by a person (not a task); its checklist is an Input; the won history makes no task
            await Settle(e);
            AgentTask draft = OpenOne(e, "SL.BID.DRAFT");
            Assert.AreEqual(AgentStepKind.Input, draft.Kind);
            Assert.AreEqual(AgentTaskState.WaitingInput, draft.State);
            Assert.AreEqual(fresh, draft.TargetId);
            StringAssert.Contains(draft.Summary, "هنوز ثبت نشده است");
            AssertRoles("SALES", draft);
            Assert.AreEqual("/sales/bid-decisions/" + fresh, draft.Link);
            Assert.IsFalse(Tasks(e).Any(t => t.TargetId == old && t.TargetType == "SlInquiry"), "the won history has no task");
            Assert.IsFalse(Tasks(e).Any(t => t.StepCode.StartsWith("SL.INQUIRY")), "the inquiry is not a task");

            // sales writes the checklist; the agent sends the complete one; the CEO decides
            var saved = await As(e, Sales1, p => p.GetRequiredService<ISlPipelineService>().SaveBidDecisionAsync(new SlSaveBidDecisionDto
            {
                InquiryId = fresh, TechnicalCapabilityOk = true, CommercialRiskReviewed = true, PaymentTermsReviewed = true, GuaranteesReviewed = true, SanctionCheck = SlSanctionCheck.Clear, ExpectedMarginPercent = 18m,
            }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SL.BID.DRAFT").Count, Dump(e));
            Assert.AreEqual(AgentTaskState.Done, Done(e, "SL.BID.SUBMIT").State);
            SlBidDecision bid = e.Host.Read(u => u.Set<SlBidDecision>().AsNoTracking().Single(d => d.InquiryId == fresh));
            Assert.AreEqual(SlBidStatus.WaitingDecision, bid.Status);
            AgentTask decide = OpenOne(e, "SL.BID.DECIDE");
            Assert.AreEqual(AgentStepKind.HumanGate, decide.Kind);
            AssertRoles("CEO", decide);
            Assert.IsTrue((await As(e, Ceo, p => p.GetRequiredService<ISlPipelineService>().DecideBidAsync(new SlBidActionDto { InquiryId = fresh, Decision = "bid" }, Ct))).Success);

            // the agent estimates from the equipment of the won history (the same count: the same quantity); the costs it cannot know are an Input
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SL.BID.DECIDE").Count);
            Done(e, "SL.ESTIMATE.FROM-EQUIPMENT");
            SlEstimate est = e.Host.Read(u => u.Set<SlEstimate>().AsNoTracking().Include(x => x.Equipments).Single(x => x.InquiryId == fresh));
            Assert.AreEqual(seat, est.CreatorUserId);
            Assert.AreEqual(SlEstimateStatus.Draft, est.Status);
            Assert.AreEqual(equipment, est.Equipments.Single().EquipmentId);
            Assert.AreEqual(2, est.Equipments.Single().Count);
            Assert.AreEqual(10m, est.OverheadPercent);
            Assert.AreEqual(20m, est.TargetMarginPercent);
            AgentTask input = OpenOne(e, "SL.ESTIMATE.INPUT");
            Assert.AreEqual(AgentStepKind.Input, input.Kind);
            StringAssert.Contains(input.Summary, "کامل نیست");
            Assert.AreEqual("/sales/estimates/" + est.Id, input.Link);
            Assert.AreEqual(0, Open(e, "SL.ESTIMATE.FINALIZE").Count);

            // sales enters the cost the agent could not find; the agent finalises and drafts the proposal with the terms of the customer's last proposal
            var fixedEstimate = await As(e, Sales1, p => p.GetRequiredService<ISlEstimateService>().SaveEstimateAsync(new SlSaveEstimateDto
            {
                Id = est.Id, InquiryId = fresh, OverheadPercent = 10m, ContingencyPercent = 5m, TargetMarginPercent = 20m,
                Lines = new List<SlSaveEstimateLineDto> { new SlSaveEstimateLineDto { Kind = SlCostKind.Material, Description = "شفت", Unit = "عدد", Quantity = 2, UnitCost = 1_000_000_000m } },
            }, Ct));
            Assert.IsTrue(fixedEstimate.Success, fixedEstimate.Message);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SL.ESTIMATE.INPUT").Count, Dump(e));
            AgentTask finalize = OpenOne(e, "SL.ESTIMATE.FINALIZE");
            Assert.AreEqual(AgentStepKind.HumanGate, finalize.Kind, "the finalisation stamps a decision column: a person's");
            AssertRoles("SALES", finalize);
            Assert.AreEqual(0, Open(e, "SL.PROPOSAL.DRAFT").Count);
            Assert.IsTrue((await As(e, Sales1, p => p.GetRequiredService<ISlEstimateService>().EstimateActionAsync(new SlEstimateActionDto { Id = est.Id, Action = "finalize" }, Ct))).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SL.ESTIMATE.FINALIZE").Count, Dump(e));
            SlEstimate fin = e.Host.Read(u => u.Set<SlEstimate>().AsNoTracking().Single(x => x.InquiryId == fresh));
            Assert.AreEqual(SlEstimateStatus.Final, fin.Status, Dump(e) + " missing=" + fin.MissingInputs + " price=" + fin.Price);
            Assert.AreEqual(1801, fin.FinalizedByUserId);
            Assert.IsFalse(IsSeat(e, fin.FinalizedByUserId), "a seat finalised");
            Done(e, "SL.PROPOSAL.DRAFT");
            SlProposal prop = e.Host.Read(u => u.Set<SlProposal>().AsNoTracking().Single(x => x.InquiryId == fresh));
            Assert.AreEqual(SlProposalStatus.Draft, prop.Status);
            Assert.AreEqual(seat, prop.CreatorUserId);
            Assert.AreEqual(fin.Price, prop.Price);
            Assert.AreEqual(10, prop.DeliveryWeeks, "70 days to the required delivery date");
            Assert.AreEqual(30, prop.ValidityDays, "the terms of the customer's last proposal");
            Assert.AreEqual("۳۰ درصد پیش‌پرداخت", prop.PaymentTermsText);
            Assert.AreEqual("RFQ-77", prop.CustomerReference);
            Assert.IsFalse(prop.BelowCost);

            // approval: the CEO alone while the CEO has set no threshold; sales or the CEO once the price is under it
            AgentTask approve = OpenOne(e, "SL.PROPOSAL.APPROVE");
            Assert.AreEqual(AgentStepKind.HumanGate, approve.Kind);
            AssertRoles("CEO", approve);
            e.Host.Write(u => { AppHost.Add(u, new SlSetting { Key = SlSettingKeys.ProposalApprovalThreshold, Value = 100_000_000_000m, Title = "سقف" }); u.SaveChanges(); }, Seeder);
            await Settle(e);
            AssertRoles("SALES,CEO", OpenOne(e, "SL.PROPOSAL.APPROVE"));
            Assert.IsFalse((await As(e, Sales1, p => p.GetRequiredService<ISlProposalService>().ProposalActionAsync(new SlProposalActionDto { Id = prop.Id, Action = "approve" }, Ct))).Success, "the one who prepared the estimate does not approve");
            Assert.IsTrue((await As(e, Ceo, p => p.GetRequiredService<ISlProposalService>().ProposalActionAsync(new SlProposalActionDto { Id = prop.Id, Action = "approve" }, Ct))).Success);

            // a person sends it (physical), a person records the answer (input)
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SL.PROPOSAL.APPROVE").Count);
            // the tender must be reviewed before the offer leaves the company: a gate for the PMO deputy and the commercial unit, and the sending waits for it
            AgentTask review = OpenOne(e, "SL.TENDER.REVIEW");
            Assert.AreEqual(AgentStepKind.HumanGate, review.Kind);
            AssertRoles("PMO,COMMERCIAL", review);
            StringAssert.Contains(review.Summary, "ثبت نشده است");
            Assert.AreEqual(0, Open(e, "SL.PROPOSAL.SEND").Count, "not sent before the tender review");
            string tenderCode = e.Host.Read(u => u.Set<Entities.Model.NasbNiro.Tenders.Tender>().AsNoTracking().Single().Code);
            var saveReview = await As(e, Commercial1, p => p.GetRequiredService<Services.NasbNiro.ProjectControl.IPcProjectService>().SaveReviewAsync(new ViewModels.NasbNiro.ProjectControlDtos.PcReviewDto
            {
                TenderCode = tenderCode, Kind = Entities.Model.NasbNiro.ProjectControl.PcReviewKind.StandardProduct, RequirementsDefined = true, DifferencesResolved = true, DeliveryFeasible = true,
                StandardsItpAgreed = true, GuaranteesReviewed = true, PenaltiesReviewed = true, PaymentTermsReviewed = true, CurrencyReviewed = true,
            }, Ct));
            Assert.IsTrue(saveReview.Success, saveReview.Message);
            await Settle(e);
            StringAssert.Contains(OpenOne(e, "SL.TENDER.REVIEW").Summary, "نوشته شده");
            foreach (ClaimsPrincipal who in new[] { PmoDeputy, Commercial2 })
            {
                var accepted = await As(e, who, p => p.GetRequiredService<Services.NasbNiro.ProjectControl.IPcProjectService>().ReviewActionAsync(new ViewModels.NasbNiro.ProjectControlDtos.PcReviewActionDto { Id = saveReview.Data.Id.Value, Action = "accept" }, Ct));
                Assert.IsTrue(accepted.Success, accepted.Message);
            }
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SL.TENDER.REVIEW").Count, Dump(e));
            AgentTask send = OpenOne(e, "SL.PROPOSAL.SEND");
            Assert.AreEqual(AgentStepKind.Physical, send.Kind);
            AssertRoles("SALES", send);
            var sent = await As(e, Sales1, p => p.GetRequiredService<ISlProposalService>().ProposalActionAsync(new SlProposalActionDto { Id = prop.Id, Action = "submit" }, Ct));
            Assert.IsTrue(sent.Success, sent.Message);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SL.PROPOSAL.SEND").Count);
            AgentTask result = OpenOne(e, "SL.PROPOSAL.RESULT");
            Assert.AreEqual(AgentStepKind.Input, result.Kind);
            AssertRoles("SALES,CEO", result);
            int reason = 0;
            e.Host.Write(u => { SlReasonCode r = AppHost.Add(u, new SlReasonCode { Code = "PRICE", Title = "قیمت", Kind = SlReasonKind.Lost, IsActive = true }); u.SaveChanges(); reason = r.Id; }, Seeder);
            var lost = await As(e, Ceo, p => p.GetRequiredService<ISlProposalService>().ResultAsync(new SlProposalResultDto { Id = prop.Id, Result = "lost", ReasonId = reason, Competitor = "رقیب", CompetitorPrice = 4_000_000_000m }, Ct));
            Assert.IsTrue(lost.Success, lost.Message);
            await Settle(e);
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station == 1 && t.State < AgentTaskState.Done), Dump(e));

            // no decision, approval, sending or result is a seat's
            SlBidDecision bidEnd = e.Host.Read(u => u.Set<SlBidDecision>().AsNoTracking().Single(d => d.InquiryId == fresh));
            SlProposal end = e.Host.Read(u => u.Set<SlProposal>().AsNoTracking().Single(x => x.InquiryId == fresh));
            Assert.AreEqual(1802, bidEnd.DecidedByUserId);
            Assert.AreEqual(1802, end.ApprovedByUserId);
            Assert.AreEqual(1801, end.SubmittedByUserId);
            foreach (int? id in new int?[] { bidEnd.DecidedByUserId, end.ApprovedByUserId, end.SubmittedByUserId })
                Assert.IsFalse(IsSeat(e, id), "a seat decided");
        }

        // ------------------------------------------------------------------------------------------------ a new product, and the board

        [TestMethod]
        public async Task A_Product_Without_A_Won_Precedent_Is_An_Input_Task_And_The_Board_Shows_The_Station_Done_For_A_Won_Contract()
        {
            Env e = await NewEnv("new");
            int contract = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-SL", Title = "ساخت توربین", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-20), Price = 1_000_000_000m });
                u.SaveChanges();
                contract = c.Id;
            }, Seeder);
            var (_, _, _, fresh) = Seed(e, false, contract);
            // the CEO has decided to bid for the new inquiry (a condenser: the won history was a turbine)
            e.Host.Write(u =>
            {
                SlInquiry i = u.Set<SlInquiry>().Single(x => x.Id == fresh);
                i.Status = SlInquiryStatus.Bidding;
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            AgentTask input = OpenOne(e, "SL.ESTIMATE.INPUT");
            Assert.AreEqual(fresh, input.TargetId);
            Assert.AreEqual("SlInquiry", input.TargetType);
            StringAssert.Contains(input.Summary, "محصول تکراری برنده ندارد");
            AssertRoles("SALES", input);
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode == "SL.ESTIMATE.FROM-EQUIPMENT"), "nothing derivable, nothing run");
            Assert.AreEqual(0, e.Host.Read(u => u.Set<SlEstimate>().Count(x => x.InquiryId == fresh)));

            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 1).Status, "a won proposal made the contract and nothing of its inquiry is open");
        }

        // ------------------------------------------------------------------------------------------------ the catalogue

        [TestMethod]
        public void The_Station_Has_The_Steps_Of_The_Spec_And_Gates_Use_The_Roles_Of_The_Services()
        {
            AppHost host = new AppHost("AgentSlCat_" + Guid.NewGuid().ToString("N"));
            List<IAgentStep> steps = host.AsSystem(p => Task.FromResult(p.GetServices<IAgentStep>().Where(s => s.Station == 1).ToList())).GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[]
            {
                "SL.BID.DRAFT", "SL.BID.SUBMIT", "SL.BID.DECIDE", "SL.ESTIMATE.FROM-EQUIPMENT", "SL.ESTIMATE.INPUT", "SL.ESTIMATE.FINALIZE", "SL.PROPOSAL.DRAFT", "SL.PROPOSAL.APPROVE", "SL.TENDER.REVIEW", "SL.PROPOSAL.SEND", "SL.PROPOSAL.RESULT",
            }, steps.Select(s => s.Code).ToList());
            Assert.IsTrue(steps.All(s => s.AgentCode == "AG-SALES"));
            CollectionAssert.AreEquivalent(new[] { "SL.BID.SUBMIT", "SL.ESTIMATE.FROM-EQUIPMENT", "SL.PROPOSAL.DRAFT" }, steps.Where(s => s.Kind == AgentStepKind.Agent).Select(s => s.Code).ToList());
            Assert.AreEqual(SalesRoles.Ceo, steps.Single(s => s.Code == "SL.BID.DECIDE").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
            Assert.AreEqual(SalesRoles.ProposalApprovers, steps.Single(s => s.Code == "SL.PROPOSAL.RESULT").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
            Assert.AreEqual("PMO,COMMERCIAL", steps.Single(s => s.Code == "SL.TENDER.REVIEW").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
            Assert.AreEqual(SalesRoles.Writers, steps.Single(s => s.Code == "SL.PROPOSAL.SEND").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
        }
    }
}
