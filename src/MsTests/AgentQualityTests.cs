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
using Entities.Model.NasbNiro.Inventory;
using Entities.Identity;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.QualityDtos;
using Services.NasbNiro.Quality;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.InventoryDtos;

namespace MsTests
{
    /// <summary>Agent layer, station 9 (quality): the inspection plan from the template, its submission and use by the quality agent; the client approval, the inspections and the release note signature are done by people.</summary>
    [TestClass]
    public class AgentQualityTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(1081, "project.manager", "PROJECTMANAGER");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentQual_" + name + "_" + Guid.NewGuid().ToString("N"));
            return new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-QC"); }
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

        private static AgentTask Closed(Env e, string step)
        {
            AgentTask t = Tasks(e).LastOrDefault(x => x.StepCode == step && x.State == AgentTaskState.Done);
            Assert.IsNotNull(t, step + " not done: " + Dump(e));
            return t;
        }

        private static T Svc<T, S>(Env e, ClaimsPrincipal who, Func<S, Task<T>> body) => e.Host.As(who, s => body(s.GetRequiredService<S>())).GetAwaiter().GetResult();

        private static int SeedContract(Env e, out int workOrder)
        {
            int id = 0, wo = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-900", Title = "ساخت پمپ", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-30) });
                u.SaveChanges();
                WorkOrder w = AppHost.Add(u, new WorkOrder { Code = "WO-9", Title = "پمپ", ContractId = c.Id, Count = 1, EquipmentId = 1 });
                AppHost.Add(u, new PcContractReview { ContractId = c.Id, Stage = PcReviewStage.Contract, Decision = PcReviewDecision.Accept, Kind = PcReviewKind.StandardProduct });
                u.SaveChanges();
                id = c.Id; wo = w.Id;
            }, Seeder);
            workOrder = wo;
            return id;
        }

        [TestMethod]
        public async Task A_Plan_Drafted_By_Another_Agent_Seat_Is_Submitted_By_The_Quality_Agent_But_A_Plan_Drafted_By_A_Person_Is_Not()
        {
            Env e = await NewEnv("itp-other");
            int contract = SeedContract(e, out int wo);
            int planSeat = e.Seats["AG-PLAN"].UserId, qcSeat = e.Seats["AG-QC"].UserId;
            int wo2 = 0, byAgent = 0, byPerson = 0;
            e.Host.Write(u =>
            {
                WorkOrder w2 = AppHost.Add(u, new WorkOrder { Code = "WO-9B", Title = "پمپ ۲", ContractId = contract, Count = 1, EquipmentId = 1 });
                u.SaveChanges();
                wo2 = w2.Id;
                Func<string, int, int, int, Itp> make = (number, workOrder, creator, seq) => AppHost.Add(u, new Itp
                {
                    Number = number, WorkOrderId = workOrder, ContractId = contract, Revision = 1, Status = ItpStatus.Draft, CreatorUserId = creator,
                    Activities = new List<ItpActivity> { new ItpActivity { Seq = seq, ItemNo = "1", Stage = "ساخت", Activity = "بازرسی ابعادی", Status = ItpActivityStatus.Planned } },
                });
                // the plan of the first work order was drafted by the planning agent (from the WBS), the other by a person
                Itp a = make("ITP-PLAN", wo, planSeat, 1);
                Itp b = make("ITP-HUMAN", wo2, 1001, 1);
                u.SaveChanges();
                byAgent = a.Id; byPerson = b.Id;
            }, Seeder);
            await Settle(e);

            Itp submitted = e.Host.Read(u => u.Set<Itp>().AsNoTracking().Single(i => i.Id == byAgent));
            Assert.AreEqual(ItpStatus.Submitted, submitted.Status, Dump(e));
            Assert.AreEqual(planSeat, submitted.CreatorUserId);
            Assert.AreEqual(qcSeat, submitted.SubmittedByUserId);
            Assert.AreEqual(ItpStatus.Draft, e.Host.Read(u => u.Set<Itp>().AsNoTracking().Single(i => i.Id == byPerson).Status), "a plan that a person is still drafting is not taken over by the agent");
            AgentTask gate = OpenOne(e, "QC.ITP.APPROVE");
            Assert.AreEqual(byAgent, gate.TargetId);
        }

        [TestMethod]
        public async Task The_Inspection_Plan_Is_Made_Submitted_And_Used_By_The_Agent_And_Approved_And_Inspected_By_People()
        {
            Env e = await NewEnv("itp");
            int contract = SeedContract(e, out int wo);
            int qcSeat = e.Seats["AG-QC"].UserId;
            await Settle(e);

            AgentTask ensure = Closed(e, "QC.ITP.ENSURE");
            Assert.AreEqual(9, ensure.Station);
            Assert.AreEqual("AG-QC", ensure.AgentCode);
            Assert.AreEqual(AgentStepKind.Agent, ensure.Kind);
            Itp itp = e.Host.Read(u => u.Set<Itp>().Include(i => i.Activities).AsNoTracking().Single(i => i.WorkOrderId == wo));
            Assert.AreEqual(qcSeat, itp.CreatorUserId);
            Assert.IsTrue(itp.Activities.Count > 0, "from the template");
            Closed(e, "QC.ITP.SUBMIT");
            Assert.AreEqual(ItpStatus.Submitted, e.Host.Read(u => u.Set<Itp>().AsNoTracking().Single(i => i.Id == itp.Id).Status));
            AgentTask gate = OpenOne(e, "QC.ITP.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            CollectionAssert.AreEquivalent(new[] { "QC", "QA", "PMO" }, gate.GateRoles.Split(','));

            // the QC manager records the approval of the client (with the letter reference); the agent puts the plan in use
            var qcManager = AppHost.Principal(1201, "qc.manager", "QC");
            e.Host.Write(u => { AppHost.Add(u, new User { Id = 1201, UserName = "qc.manager", Email = "qc.manager@example.com", IsActive = true }); u.SaveChanges(); }, Seeder);
            Assert.IsTrue(Svc<ResponseDto<ItpDto>, IItpService>(e, qcManager, s => s.ItpActionAsync(new ItpActionDto { Id = itp.Id, Action = "client-approve", Reference = "نامه ۱۲۳", Date = DateTime.Today }, Ct)).Success);
            await Settle(e);
            Closed(e, "QC.ITP.USE");
            Assert.AreEqual(ItpStatus.InUse, e.Host.Read(u => u.Set<Itp>().AsNoTracking().Single(i => i.Id == itp.Id).Status));
            Assert.AreEqual(0, Open(e, "QC.ITP.APPROVE").Count);
            Assert.AreEqual(0, Open(e, "QC.ITP.INSPECT").Count, "no activity is due yet");

            // an activity that is due today becomes one physical task for the inspector
            int activity = itp.Activities.OrderBy(a => a.Id).First().Id;
            e.Host.Write(u => { u.Set<ItpActivity>().Single(a => a.Id == activity).PlannedDate = DateTime.Today; u.SaveChanges(); }, Seeder);
            await Settle(e);
            AgentTask inspect = OpenOne(e, "QC.ITP.INSPECT");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, inspect.State);
            Assert.AreEqual("QC", inspect.GateRoles);
            Assert.AreEqual("ItpActivity", inspect.TargetType);

            // the release note: the work order has no confirmed production receipt, so the agent does not draft
            Assert.AreEqual(0, Open(e, "QC.RELEASE.DRAFT").Count);
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode == "QC.RELEASE.DRAFT"));

            // a draft release note with a line waits for the signature of the QC manager
            e.Host.Write(u =>
            {
                InventoryItem item = AppHost.Add(u, new InventoryItem { Code = "P-1", Title = "پمپ" });
                u.SaveChanges();
                var note = AppHost.Add(u, new ReleaseNote { Number = "RN-1", WorkOrderId = wo, ContractId = contract, Status = ReleaseNoteStatus.Draft, Lines = new List<ReleaseNoteLine>() });
                note.Lines.Add(new ReleaseNoteLine { RowNumber = 1, ItemId = item.Id, Quantity = 1 });
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            AgentTask sign = OpenOne(e, "QC.RELEASE.SIGN");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, sign.State);
            Assert.AreEqual("QC", sign.GateRoles);

            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreNotEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 9).Status);

            // the only human tasks are gates and the physical inspection: no Input
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station == 9 && t.Kind == AgentStepKind.Input), Dump(e));
        }
    
        private static void AssertRoles(string expected, AgentTask task) => CollectionAssert.AreEquivalent(expected.Split(','), task.GateRoles.Split(','), task.StepCode + " roles " + task.GateRoles);

        private static void Users(Env e, params (int Id, string Name)[] users) =>
            e.Host.Write(u => { foreach (var x in users) AppHost.Add(u, new User { Id = x.Id, UserName = x.Name, Email = x.Name + "@example.com", IsActive = true }); u.SaveChanges(); }, Seeder);

        // ------------------------------------------------------------------------------------------------ the release note, positive path

        [TestMethod]
        public async Task The_Release_Note_Is_Drafted_With_Lines_When_Every_Check_Passes_Then_Signed_By_A_QC_Person_And_The_Station_Is_Complete()
        {
            Env e = await NewEnv("release");
            int contract = SeedContract(e, out int wo);
            int qcSeat = e.Seats["AG-QC"].UserId;
            Users(e, (1401, "qc.signer"));
            int item = 0;
            e.Host.Write(u =>
            {
                // every check of the service passes: an approved ITP without open points, no NCR, no unfinished production order, an accepted final inspection, a complete data book, no configuration baseline to audit
                InventoryItem pump = AppHost.Add(u, new InventoryItem { Code = "P-RL", Title = "پمپ", Unit = "دستگاه", TrackingMode = ItemTrackingMode.Serial });
                u.SaveChanges();
                AppHost.Add(u, new Itp { Number = "ITP-RL", WorkOrderId = wo, ContractId = contract, Revision = 1, Status = ItpStatus.InUse });
                AppHost.Add(u, new QcRecord { Number = "QR-RL", Type = QcRecordType.Final, WorkOrderId = wo, ItemId = pump.Id, Quantity = 1, InspectorUserId = 1201, RecordDate = DateTime.Today, Result = QcResult.Accept });
                AppHost.Add(u, new SerialUnit { ItemId = pump.Id, SerialNumber = "SN-RL-1", Status = SerialUnitStatus.InStock, WorkOrderId = wo });
                var receipt = AppHost.Add(u, new InventoryDocument { Type = InventoryDocumentType.ProductionReceipt, Number = "PR-RL", Status = InventoryDocumentStatus.Confirmed, WarehouseId = 1, DocumentDate = DateTime.Today, WorkOrderId = wo, ContractId = contract, Lines = new List<InventoryDocumentLine>() });
                receipt.Lines.Add(new InventoryDocumentLine { RowNumber = 1, ItemId = pump.Id, Quantity = 1 });
                u.SaveChanges();
                item = pump.Id;
            }, Seeder);
            ResponseDto<ReleaseChecksDto> checks = await e.Host.As(Pmo, s => s.GetRequiredService<IReleaseService>().GetChecksAsync(wo, Ct));
            Assert.IsTrue(checks.Success, checks.Message);
            Assert.IsTrue(checks.Data.CanRelease, "the seed passes every check of the service: " + string.Join(" | ", checks.Data.Checks.Where(c => !c.Passed).Select(c => c.Code + ":" + c.Detail)));

            await Settle(e);
            AgentTask draft = Closed(e, "QC.RELEASE.DRAFT");
            Assert.AreEqual(9, draft.Station);
            Assert.AreEqual(AgentStepKind.Agent, draft.Kind);
            ReleaseNote note = e.Host.Read(u => u.Set<ReleaseNote>().AsNoTracking().Include(n => n.Lines).Single(n => n.WorkOrderId == wo));
            Assert.AreEqual(ReleaseNoteStatus.Draft, note.Status);
            Assert.AreEqual(qcSeat, note.CreatorUserId);
            Assert.AreEqual(1, note.Lines.Count);
            Assert.AreEqual(item, note.Lines.Single().ItemId);
            Assert.AreEqual("SN-RL-1", note.Lines.Single().SerialNumbers);
            AgentTask sign = OpenOne(e, "QC.RELEASE.SIGN");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, sign.State);
            AssertRoles("QC", sign);
            Assert.AreEqual(0, Open(e, "QC.RELEASE.DRAFT").Count);

            // the agent that drafted the note cannot release it (the service refuses the maker; the data layer refuses a seat), a QC person signs
            var signer = AppHost.Principal(1401, "qc.signer", "QC");
            ResponseDto<ReleaseNoteDto> released = await e.Host.As(signer, s => s.GetRequiredService<IReleaseService>().NoteActionAsync(new ReleaseNoteActionDto { Id = note.Id, Action = "release" }, Ct));
            Assert.IsTrue(released.Success, released.Message);
            await Settle(e);
            Closed(e, "QC.RELEASE.SIGN");
            ReleaseNote after = e.Host.Read(u => u.Set<ReleaseNote>().AsNoTracking().Single(n => n.Id == note.Id));
            Assert.AreEqual(ReleaseNoteStatus.Released, after.Status);
            Assert.AreEqual(1401, after.ReleasedByUserId);
            Assert.IsFalse(e.Seats.Values.Any(x => x.UserId == after.ReleasedByUserId), "no seat in a decision column");
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode.StartsWith("QC.RELEASE") && t.State < AgentTaskState.Done), Dump(e));
            // no further gate after the QC signature: ReleaseService goes straight to Released (the shipping agent takes over)
            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 9).Status, "the station probe says complete: " + Dump(e));
        }

        // ------------------------------------------------------------------------------------------------ the test report gate

        [TestMethod]
        public async Task A_Submitted_Test_Report_Waits_For_A_QC_Approver_Who_Is_Not_The_Examiner_And_The_Gate_Closes_On_Approval()
        {
            Env e = await NewEnv("report");
            int contract = SeedContract(e, out int wo);
            Users(e, (1411, "examiner"), (1412, "approver"));
            int report = 0;
            e.Host.Write(u =>
            {
                TestReport r = AppHost.Add(u, new TestReport { Number = "TR-1", Type = TestReportType.PT, WorkOrderId = wo, ExaminerUserId = 1411, ReportDate = DateTime.Today, Status = TestReportStatus.Submitted, Result = ReportResult.Pass, SubmittedAt = DateTimeOffset.Now, CreatorUserId = 1411 });
                // a draft report is not a gate
                AppHost.Add(u, new TestReport { Number = "TR-0", Type = TestReportType.PT, WorkOrderId = wo, ExaminerUserId = 1411, ReportDate = DateTime.Today, Status = TestReportStatus.Draft, CreatorUserId = 1411 });
                u.SaveChanges();
                report = r.Id;
            }, Seeder);
            await Settle(e);
            AgentTask gate = OpenOne(e, "QC.REPORT.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            Assert.AreEqual(report, gate.TargetId);
            Assert.AreEqual("QC", gate.GateRoles);
            Assert.AreEqual(contract, gate.ContractId);

            var examiner = AppHost.Principal(1411, "examiner", "QC");
            ResponseDto<TestReportDto> own = await e.Host.As(examiner, s => s.GetRequiredService<IInspectionService>().ReportActionAsync(new TestReportActionDto { Id = report, Action = "approve" }, Ct));
            Assert.IsFalse(own.Success, "the examiner does not approve his own report");
            Assert.AreEqual(1, Open(e, "QC.REPORT.APPROVE").Count);
            var approver = AppHost.Principal(1412, "approver", "QC");
            ResponseDto<TestReportDto> done = await e.Host.As(approver, s => s.GetRequiredService<IInspectionService>().ReportActionAsync(new TestReportActionDto { Id = report, Action = "approve" }, Ct));
            Assert.IsTrue(done.Success, done.Message);
            await Settle(e);
            Closed(e, "QC.REPORT.APPROVE");
            TestReport after = e.Host.Read(u => u.Set<TestReport>().AsNoTracking().Single(r => r.Id == report));
            Assert.AreEqual(1412, after.ApprovedByUserId);
            Assert.IsFalse(e.Seats.Values.Any(x => x.UserId == after.ApprovedByUserId));
        }

        // ------------------------------------------------------------------------------------------------ the NCR chain

        [TestMethod]
        public async Task The_Ncr_Waits_For_Classification_Then_The_Three_Signatures_Then_The_Shop_Floor_Then_The_Close_And_Each_Gate_Has_The_Roles_Of_The_Service()
        {
            Env e = await NewEnv("ncr");
            int contract = SeedContract(e, out int wo);
            Users(e, (1421, "ncr.qc"), (1422, "ncr.eng"), (1423, "ncr.mfg"), (1424, "ncr.closer"));
            int ncr = 0;
            e.Host.Write(u =>
            {
                InventoryItem part = AppHost.Add(u, new InventoryItem { Code = "P-NC", Title = "شافت", Unit = "عدد" });
                u.SaveChanges();
                NonConformance n = AppHost.Add(u, new NonConformance { Number = "NCR-1", ReportDate = DateTime.Today, Source = NcrSource.InProcess, Status = NcrStatus.Open, ItemId = part.Id, Quantity = 1, WorkOrderId = wo, ContractId = contract, Description = "ابعاد خارج از تلرانس", CreatorUserId = 1001 });
                u.SaveChanges();
                ncr = n.Id;
            }, Seeder);

            // not classified: QC classifies and decides (the service's NCR save is for PMO and QC)
            await Settle(e);
            AgentTask classify = OpenOne(e, "QC.NCR.CLASSIFY");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, classify.State);
            AssertRoles("PMO,QC", classify);
            Assert.AreEqual(0, Open(e, "QC.NCR.APPROVE").Count);

            // classified as minor with the decision "scrap" (no committee, no permit)
            e.Host.Write(u => { NonConformance n = u.Set<NonConformance>().Single(x => x.Id == ncr); n.Severity = NcrSeverity.Minor; n.Disposition = NcrDisposition.Scrap; u.SaveChanges(); }, Seeder);
            await Settle(e);
            Closed(e, "QC.NCR.CLASSIFY");
            AgentTask approve = OpenOne(e, "QC.NCR.APPROVE");
            AssertRoles("QC,QA,ENGINEERING,CONSTRUCTION,PRODUCTIONMANAGER", approve);

            // the quality signature leaves the other two roles
            Func<int, string, string[], ApprovalKind, Task<ResponseDto<NcrDto>>> sign = (id, name, roles, kind) =>
                e.Host.As(AppHost.Principal(id, name, roles), s => s.GetRequiredService<IInventoryQualityService>().ApproveNcrAsync(new ApproveNcrDto { Id = ncr, Kind = kind }, Ct));
            Assert.IsTrue((await sign(1421, "ncr.qc", new[] { "QC" }, ApprovalKind.Quality)).Success);
            await Settle(e);
            AssertRoles("ENGINEERING,CONSTRUCTION,PRODUCTIONMANAGER", OpenOne(e, "QC.NCR.APPROVE"));
            Assert.IsTrue((await sign(1422, "ncr.eng", new[] { "ENGINEERING" }, ApprovalKind.Engineering)).Success);
            await Settle(e);
            AssertRoles("CONSTRUCTION,PRODUCTIONMANAGER", OpenOne(e, "QC.NCR.APPROVE"));
            Assert.IsTrue((await sign(1423, "ncr.mfg", new[] { "PRODUCTIONMANAGER" }, ApprovalKind.Manufacturing)).Success);
            await Settle(e);
            Closed(e, "QC.NCR.APPROVE");
            Assert.AreEqual(NcrStatus.Approved, e.Host.Read(u => u.Set<NonConformance>().AsNoTracking().Single(x => x.Id == ncr).Status));

            // approved, the product is on the shop floor (no warehouse): the decision is carried out by people (physical)
            AgentTask execute = OpenOne(e, "QC.NCR.EXECUTE");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, execute.State);
            Assert.AreEqual(AgentStepKind.Physical, execute.Kind);
            AssertRoles("PMO,QC,WAREHOUSE", execute);

            // executed (the evidence of the shop floor is attached by the person who executes; the state is set here), then closed by a QC person who did not raise or execute it
            e.Host.Write(u =>
            {
                NonConformance n = u.Set<NonConformance>().Single(x => x.Id == ncr);
                n.Status = NcrStatus.Executed; n.ExecutedAt = DateTimeOffset.Now; n.ExecutedByUserId = 1423;
                n.CauseCategory = NcrCauseCategory.Method; n.RootCause = "دستورالعمل ناقص"; n.CorrectiveAction = "اصلاح دستورالعمل";
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            Closed(e, "QC.NCR.EXECUTE");
            AgentTask close = OpenOne(e, "QC.NCR.CLOSE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, close.State);
            AssertRoles("QC,QA", close);
            ResponseDto<NcrDto> closed = await e.Host.As(AppHost.Principal(1424, "ncr.closer", "QC"), s => s.GetRequiredService<IInventoryQualityService>().CloseNcrAsync(new CloseNcrDto { Id = ncr, EffectivenessCheck = "تکرار نشد" }, Ct));
            Assert.IsTrue(closed.Success, closed.Message);
            await Settle(e);
            Closed(e, "QC.NCR.CLOSE");
            NonConformance last = e.Host.Read(u => u.Set<NonConformance>().AsNoTracking().Single(x => x.Id == ncr));
            Assert.AreEqual(NcrStatus.Closed, last.Status);
            Assert.IsFalse(e.Seats.Values.Any(x => new int?[] { last.QualityApprovedByUserId, last.EngineeringApprovedByUserId, last.ManufacturingApprovedByUserId, last.ExecutedByUserId, last.ClosedByUserId }.Contains(x.UserId)), "no seat in a decision column");
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode.StartsWith("QC.NCR") && t.State < AgentTaskState.Done), Dump(e));
        }

        // ------------------------------------------------------------------------------------------------ the CAPA chain

        [TestMethod]
        public async Task The_Capa_Waits_For_Its_Definition_Then_Its_Actions_Then_The_Agent_Marks_It_Implemented_And_The_Effectiveness_Gate_Appears_Only_After_The_Waiting_Period()
        {
            Env e = await NewEnv("capa");
            Users(e, (1431, "capa.owner"), (1432, "capa.qa"), (1433, "capa.closer"));
            int qcSeat = e.Seats["AG-QC"].UserId;
            int capa = 0;
            e.Host.Write(u =>
            {
                // the service drafts a CAPA for a major NCR without a human creator: a draft that waits for QA
                Capa c = AppHost.Add(u, new Capa { Number = "CAPA-1", Source = CapaSource.Ncr, SourceId = 1, Problem = "عدم انطباق عمده", Status = CapaStatus.Open, AutoDrafted = true, Mandatory = true, Iteration = 1, DueDate = DateTime.Today.AddDays(10), CreatorUserId = 1001 });
                u.SaveChanges();
                capa = c.Id;
            }, Seeder);
            await Settle(e);
            AgentTask assign = OpenOne(e, "QC.CAPA.ASSIGN");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, assign.State);
            AssertRoles("QA,QC,PMO", assign);
            Assert.AreEqual(0, Open(e, "QC.CAPA.ACTION").Count);

            // the actions are defined (QA), then done by the owner (physical)
            e.Host.Write(u =>
            {
                Capa c = u.Set<Capa>().Include(x => x.Actions).Single(x => x.Id == capa);
                c.Status = CapaStatus.ActionsDefined; c.OwnerUserId = 1431;
                c.Actions.Add(new CapaAction { Description = "اصلاح دستورالعمل", OwnerUserId = 1431, DueDate = DateTime.Today.AddDays(5), Iteration = 1 });
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            Closed(e, "QC.CAPA.ASSIGN");
            AgentTask action = OpenOne(e, "QC.CAPA.ACTION");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, action.State);
            AssertRoles("QA,QC,PMO", action);
            Assert.AreEqual(0, Open(e, "QC.CAPA.IMPLEMENT").Count + Tasks(e).Count(t => t.StepCode == "QC.CAPA.IMPLEMENT"), "the agent waits for the actions");
            int actionId = e.Host.Read(u => u.Set<CapaAction>().AsNoTracking().Single(a => a.CapaId == capa).Id);
            ResponseDto<CapaDto> done = await e.Host.As(AppHost.Principal(1431, "capa.owner", "QC"), s => s.GetRequiredService<ICapaService>().CapaActionAsync(new CapaActionRequestDto { Id = capa, Action = "complete-action", ActionId = actionId, Note = "دستورالعمل اصلاح شد" }, Ct));
            Assert.IsTrue(done.Success, done.Message);

            // the agent records the CAPA as implemented; the waiting period of the effectiveness check starts
            await Settle(e);
            AgentTask impl = Closed(e, "QC.CAPA.IMPLEMENT");
            Assert.AreEqual(AgentStepKind.Agent, impl.Kind);
            Closed(e, "QC.CAPA.ACTION");
            Capa implemented = e.Host.Read(u => u.Set<Capa>().AsNoTracking().Single(x => x.Id == capa));
            Assert.AreEqual(CapaStatus.Implemented, implemented.Status);
            Assert.IsNotNull(implemented.ImplementedAt);
            Assert.AreEqual(0, Open(e, "QC.CAPA.EFFECTIVENESS").Count, "the service refuses an earlier check, so there is no gate yet");
            ResponseDto<CapaDto> early = await e.Host.As(AppHost.Principal(1432, "capa.qa", "QA"), s => s.GetRequiredService<ICapaService>().CapaActionAsync(new CapaActionRequestDto { Id = capa, Action = "effectiveness", Note = "اثربخش", Passed = true }, Ct));
            Assert.IsFalse(early.Success, "the service agrees: too early");

            // the waiting period passes
            e.Host.Write(u => { u.Set<Capa>().Single(x => x.Id == capa).ImplementedAt = DateTime.Today.AddDays(-31); u.SaveChanges(); }, Seeder);
            await Settle(e);
            AgentTask eff = OpenOne(e, "QC.CAPA.EFFECTIVENESS");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, eff.State);
            AssertRoles("QA,QC", eff);
            ResponseDto<CapaDto> judged = await e.Host.As(AppHost.Principal(1432, "capa.qa", "QA"), s => s.GetRequiredService<ICapaService>().CapaActionAsync(new CapaActionRequestDto { Id = capa, Action = "effectiveness", Note = "پس از یک ماه تکرار نشد", Passed = true }, Ct));
            Assert.IsTrue(judged.Success, judged.Message);
            await Settle(e);
            Closed(e, "QC.CAPA.EFFECTIVENESS");
            AgentTask close = OpenOne(e, "QC.CAPA.CLOSE");
            AssertRoles("QA,QC", close);
            ResponseDto<CapaDto> closed = await e.Host.As(AppHost.Principal(1433, "capa.closer", "QC"), s => s.GetRequiredService<ICapaService>().CapaActionAsync(new CapaActionRequestDto { Id = capa, Action = "close", Note = "بسته شد" }, Ct));
            Assert.IsTrue(closed.Success, closed.Message);
            await Settle(e);
            Closed(e, "QC.CAPA.CLOSE");
            Capa last = e.Host.Read(u => u.Set<Capa>().AsNoTracking().Single(x => x.Id == capa));
            Assert.AreEqual(CapaStatus.Closed, last.Status);
            Assert.AreEqual(1433, last.ClosedByUserId);
            Assert.IsFalse(e.Seats.Values.Any(x => x.UserId == last.ClosedByUserId));
            Assert.AreNotEqual(0, qcSeat);
            Assert.AreEqual(0, Tasks(e).Count(t => t.StepCode.StartsWith("QC.CAPA") && t.State < AgentTaskState.Done), Dump(e));
        }
}
}
