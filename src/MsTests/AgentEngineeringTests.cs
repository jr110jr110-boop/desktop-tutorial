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
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.ProductBankDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, station 5 (engineering and the product bank): the engineering agent instantiates a work order of a repeat product (released BOM in the bank); the BOM of a new product and its routing chart are Input tasks
    /// for the engineers (design content); people sign the four BOM signatures, release the BOM, check and approve the routing and publish it. A seat never signs, releases, checks, approves or publishes.
    /// </summary>
    [TestClass]
    public class AgentEngineeringTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(1702, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Eng1 = AppHost.Principal(1711, "engineer.one", "ENGINEERING");
        private static readonly ClaimsPrincipal Eng2 = AppHost.Principal(1712, "engineer.two", "ENGINEERING");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(1713, "qc.one", "QC");
        private static readonly ClaimsPrincipal Shop = AppHost.Principal(1714, "shop.one", "CONSTRUCTION");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            AppHost host = new AppHost("AgentEng_" + name + "_" + Guid.NewGuid().ToString("N")).WithoutReadinessGate();
            return new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-ENG"); }
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

        private static void AssertRoles(string expected, AgentTask task) => CollectionAssert.AreEquivalent(expected.Split(','), task.GateRoles.Split(','), task.StepCode + " roles " + task.GateRoles);

        private static bool IsSeat(Env e, int? id) => id.HasValue && e.Seats.Values.Any(s => s.UserId == id.Value);

        private static Task<T> As<T>(Env e, ClaimsPrincipal who, Func<IServiceProvider, Task<T>> call) => e.Host.As(who, call);

        private static SavePbBomLineDto Line(int no, string pn, string name, decimal qty, PbMakeOrBuy mob = PbMakeOrBuy.Make) =>
            new SavePbBomLineDto { ItemNumber = no, PartNumber = pn, PartName = name, Qty = qty, Unit = "عدد", Material = "WCB", MakeOrBuy = mob, NetWeight = 10m, GrossWeight = 12m };

        private const string TopPart = "99.11.00.000.2";

        /// <summary>A live accepted contract with one work order of equipment 7; <paramref name="withProduct"/> registers the product of that equipment in the bank.</summary>
        private static (int Contract, int WorkOrder, int Product) Seed(Env e, bool withProduct)
        {
            int contract = 0, wo = 0, product = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-ENG", Title = "ساخت توربین", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-20), Price = 1_000_000_000m });
                u.SaveChanges();
                WorkOrder w = AppHost.Add(u, new WorkOrder { Code = "WO-ENG", Title = "توربین یک", Count = 2, ContractId = c.Id, EquipmentId = 7 });
                AppHost.Add(u, new PcContractReview { ContractId = c.Id, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept, Kind = PcReviewKind.StandardProduct });
                if (withProduct)
                {
                    PbProduct p = AppHost.Add(u, new PbProduct { Code = TopPart, Title = "توربین نمونه", EquipmentId = 7 });
                    u.SaveChanges();
                    product = p.Id;
                }
                u.SaveChanges();
                contract = c.Id; wo = w.Id;
            }, Seeder);
            return (contract, wo, product);
        }

        private static SavePbBomDto Bom() => new SavePbBomDto
        {
            AssemblyPartNumber = TopPart, Title = "BOM توربین",
            Lines = new List<SavePbBomLineDto> { Line(1, "99.11.01.001.2", "بدنه", 1), Line(2, "99.11.01.002.2", "درپوش", 2), Line(3, "70.04.06.052.1", "پیچ", 8, PbMakeOrBuy.Purchase) },
        };

        private static PbProjectBaseline Baseline(Env e, int wo) => e.Host.Read(u => u.Set<PbProjectBaseline>().AsNoTracking().SingleOrDefault(b => b.WorkOrderId == wo && !b.Deleted));

        // ------------------------------------------------------------------------------------------------ a repeat product, end to end

        [TestMethod]
        public async Task A_New_Product_Is_Written_And_Signed_By_People_Then_The_Agent_Instantiates_And_People_Check_Approve_And_Publish_The_Routing()
        {
            Env e = await NewEnv("full");
            var (contract, wo, product) = Seed(e, true);
            int engSeat = e.Seats["AG-ENG"].UserId;

            // nothing is released: the BOM is an Input task that says what is missing; the agent has nothing to instantiate
            await Settle(e);
            AgentTask bomInput = OpenOne(e, "ENG.BOM.INPUT");
            Assert.AreEqual(AgentStepKind.Input, bomInput.Kind);
            Assert.AreEqual(AgentTaskState.WaitingInput, bomInput.State);
            Assert.AreEqual(wo, bomInput.TargetId);
            StringAssert.Contains(bomInput.Summary, "BOM ثبت نشده");
            AssertRoles("PMO,ENGINEERING,ENGINEER", bomInput);
            Assert.AreEqual(0, Open(e, "ENG.INSTANTIATE").Count);
            Assert.AreEqual(0, Open(e, "ENG.OPC.INPUT").Count, "the routing is asked for only once the BOM is released");

            // the engineer writes the draft: the task now asks for the submission
            var saved = await As(e, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(Bom(), Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(product, saved.Data.ProductId, "the BOM is of the product of the equipment");
            await Settle(e);
            StringAssert.Contains(OpenOne(e, "ENG.BOM.INPUT").Summary, "برای بررسی فرستاده نشده");
            Assert.IsTrue((await As(e, Eng1, p => p.GetRequiredService<IPbBomService>().SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct))).Success);

            // one gate task for the four signatures; the roles are those of the signatures still open
            await Settle(e);
            Assert.AreEqual(0, Open(e, "ENG.BOM.INPUT").Count, Dump(e));
            AgentTask sign = OpenOne(e, "ENG.BOM.SIGN");
            Assert.AreEqual(AgentStepKind.HumanGate, sign.Kind);
            AssertRoles("ENGINEERING,ENGINEER,QC,QA,CONSTRUCTION,PRODUCTIONMANAGER,SHOPSUPERVISOR,PMO", sign);   // the union of the four signature lists, each role once
            Assert.AreEqual("/product-bank/boms/" + saved.Data.Id, sign.Link);
            foreach (var (who, step) in new[] { (Eng2, PbBomSignature.Checked), (Qc, PbBomSignature.QcChecked), (Shop, PbBomSignature.ManufacturingChecked) })
                Assert.IsTrue((await As(e, who, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = saved.Data.Id, Step = step }, Ct))).Success, step.ToString());
            await Settle(e);
            AssertRoles(PbRoles.ApproveRoles, OpenOne(e, "ENG.BOM.SIGN"));
            Assert.IsTrue((await As(e, Seeder, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = saved.Data.Id, Step = PbBomSignature.Approved }, Ct))).Success);

            // approved: the release gate; no instantiation yet
            await DesignTestData.VerifyBomAsync(e.Host, saved.Data.Id);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "ENG.BOM.SIGN").Count);
            AssertRoles(PbRoles.ReleaseRoles, OpenOne(e, "ENG.BOM.RELEASE"));
            Assert.IsNull(Baseline(e, wo));
            var released = await As(e, Pmo2, p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsTrue(released.Success, released.Message);

            // the agent instantiates the work order; the BOM input is gone; the routing is now the missing content
            await Settle(e);
            AgentTask inst = Tasks(e).Last(t => t.StepCode == "ENG.INSTANTIATE");
            Assert.AreEqual(AgentTaskState.Done, inst.State, Dump(e));
            Assert.AreEqual("AG-ENG", inst.AgentCode);
            PbProjectBaseline baseline = Baseline(e, wo);
            Assert.IsNotNull(baseline, Dump(e));
            Assert.AreEqual(engSeat, baseline.CreatorUserId, "the agent made the baseline as itself");
            Assert.AreEqual(2, baseline.Count);
            Assert.AreEqual(0, Open(e, "ENG.BOM.INPUT").Count);
            Assert.AreEqual(0, Open(e, "ENG.BOM.RELEASE").Count);
            AgentTask opcInput = OpenOne(e, "ENG.OPC.INPUT");
            Assert.AreEqual(AgentTaskState.WaitingInput, opcInput.State);
            Assert.AreEqual(product, opcInput.TargetId);
            StringAssert.Contains(opcInput.Summary, "مراحل ساخت هر قطعه");
            AssertRoles("PMO,ENGINEERING,ENGINEER", opcInput);
            int instCount = Tasks(e).Count(t => t.StepCode == "ENG.INSTANTIATE");
            await Settle(e);
            Assert.AreEqual(instCount, Tasks(e).Count(t => t.StepCode == "ENG.INSTANTIATE"), "no second instantiation");

            // the routing: draft (still the input task), check, approve, publish
            var dto = new SavePbOpcDto
            {
                ProductId = product, PartNumber = "99.11.01.001.2", Title = "بدنه",
                Steps = new List<SavePbOpcStepDto> { new SavePbOpcStepDto { Seq = 1, StageCode2 = "CA", PartName = "بدنه", StandardHours = 1.5m }, new SavePbOpcStepDto { Seq = 2, StageCode2 = "RG", ResourceClass = "CNC_MILLING", StandardHours = 2.5m } },
            };
            var opc = await As(e, Eng1, p => p.GetRequiredService<IPbRoutingService>().SaveAsync(dto, Ct));
            Assert.IsTrue(opc.Success, opc.Message);
            await Settle(e);
            StringAssert.Contains(OpenOne(e, "ENG.OPC.INPUT").Summary, "پیش‌نویس چارت");
            Assert.IsTrue((await As(e, Eng1, p => p.GetRequiredService<IPbRoutingService>().SubmitAsync(new PbOpcActionDto { Id = opc.Data.Id }, Ct))).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "ENG.OPC.INPUT").Count);
            AssertRoles(PbRoles.CheckRoles, OpenOne(e, "ENG.OPC.CHECK"));
            Assert.IsTrue((await As(e, Eng2, p => p.GetRequiredService<IPbRoutingService>().CheckAsync(new PbOpcActionDto { Id = opc.Data.Id }, Ct))).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "ENG.OPC.CHECK").Count);
            AssertRoles(PbRoles.ApproveRoles, OpenOne(e, "ENG.OPC.APPROVE"));
            Assert.IsTrue((await As(e, Seeder, p => p.GetRequiredService<IPbRoutingService>().ApproveAsync(new PbOpcActionDto { Id = opc.Data.Id }, Ct))).Success);
            await Settle(e);
            AssertRoles(PbRoutingService.PublishRoles, OpenOne(e, "ENG.OPC.PUBLISH"));
            var published = await As(e, Seeder, p => p.GetRequiredService<IPbRoutingService>().PublishToMesAsync(new PbOpcActionDto { Id = opc.Data.Id }, Ct));
            Assert.IsTrue(published.Success, published.Message);
            await Settle(e);
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station == 5 && t.State < AgentTaskState.Done), Dump(e));

            // no signature, release, check, approval or publication is a seat's
            PbBomHeader bom = e.Host.Read(u => u.Set<PbBomHeader>().AsNoTracking().Single());
            foreach (int? id in new int?[] { bom.CheckedByUserId, bom.QcCheckedByUserId, bom.ManufacturingCheckedByUserId, bom.ApprovedByUserId, bom.ReleasedByUserId })
                Assert.IsFalse(IsSeat(e, id), "a seat signed the BOM");
            PbOpcRouting routing = e.Host.Read(u => u.Set<PbOpcRouting>().AsNoTracking().Single());
            foreach (int? id in new int?[] { routing.CheckedByUserId, routing.ApprovedByUserId })
                Assert.IsFalse(IsSeat(e, id), "a seat decided on the routing");
            Assert.IsFalse(IsSeat(e, bom.PreparedByUserId));

            // the board: the baseline exists and nothing of the flow is open, so station 5 is done
            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 5).Status);
        }

        // ------------------------------------------------------------------------------------------------ a product that is not in the bank yet

        [TestMethod]
        public async Task A_Work_Order_Of_A_Product_That_Is_Not_In_The_Bank_Is_An_Input_Task_That_Says_What_Is_Missing_And_Is_Never_Instantiated()
        {
            Env e = await NewEnv("noproduct");
            var (contract, wo, _) = Seed(e, false);
            await Settle(e);
            AgentTask input = OpenOne(e, "ENG.BOM.INPUT");
            StringAssert.Contains(input.Summary, "محصولی برای تجهیز این دستور کار در بانک محصول ثبت نشده است");
            Assert.AreEqual(contract, input.ContractId);
            Assert.AreEqual(0, Open(e, "ENG.INSTANTIATE").Count);
            Assert.IsNull(Baseline(e, wo));
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode == "ENG.INSTANTIATE"));
            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreNotEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 5).Status);
        }

        // ------------------------------------------------------------------------------------------------ the catalogue

        [TestMethod]
        public void The_Station_Has_The_Steps_Of_The_Spec_And_No_Agent_Revises_A_Bom_And_Every_Gate_Uses_The_Roles_Of_The_Service()
        {
            AppHost host = new AppHost("AgentEngCat_" + Guid.NewGuid().ToString("N"));
            List<IAgentStep> steps = host.AsSystem(p => Task.FromResult(p.GetServices<IAgentStep>().Where(s => s.Station == 5).ToList())).GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[] { "ENG.INSTANTIATE", "ENG.BOM.INPUT", "ENG.OPC.INPUT", "ENG.BOM.SIGN", "ENG.BOM.RELEASE", "ENG.OPC.CHECK", "ENG.OPC.APPROVE", "ENG.OPC.PUBLISH", "ENG.INBOX.INGEST", "ENG.INBOX.FIX", "ENG.INBOX.APPLY" }, steps.Select(s => s.Code).ToList());
            Assert.IsTrue(steps.All(s => s.AgentCode == "AG-ENG"));
            Assert.AreEqual(3, steps.Count(s => s.Kind == AgentStepKind.Agent), "the instantiation and the two inbox steps (ingest, apply) are the agent's");
            Assert.AreEqual(3, steps.Count(s => s.Kind == AgentStepKind.Input));
            Assert.AreEqual(5, steps.Count(s => s.Kind == AgentStepKind.HumanGate));
            Assert.AreEqual(PbRoles.ReleaseRoles, steps.Single(s => s.Code == "ENG.BOM.RELEASE").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
            Assert.AreEqual(PbRoles.CheckRoles, steps.Single(s => s.Code == "ENG.OPC.CHECK").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
            Assert.AreEqual(PbRoles.ApproveRoles, steps.Single(s => s.Code == "ENG.OPC.APPROVE").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
            Assert.AreEqual(PbRoutingService.PublishRoles, steps.Single(s => s.Code == "ENG.OPC.PUBLISH").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
            // the publication needs a role of both lists; the lists are the same, so the gate roles satisfy both
            CollectionAssert.AreEquivalent(PbRoutingService.PublishRoles.Split(','), Services.NasbNiro.Production.ProductionRoles.RoutingWriters.Split(','));
        }
    }
}
