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
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.ContractsSystem;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Ai;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.ProcurementDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, station 13 (finance and Sepidar): the finance agent drafts and prepares the progress statements, requests the payment of an authorised one and drafts the Sepidar package of a closed month;
    /// the technical office, the supervision, the project manager of the contract, the accountants and the finance manager decide at the gates; people do the physical hand-over and record what exists only outside.
    /// </summary>
    [TestClass]
    public class AgentFinanceTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Technical = AppHost.Principal(1601, "tech.office", "ENGINEERING");
        private static readonly ClaimsPrincipal Supervisor = AppHost.Principal(1602, "supervisor", "QC");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(1603, "project.manager", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal Accountant = AppHost.Principal(1604, "accountant", "FINANCIAL");
        private static readonly ClaimsPrincipal FinanceManager = AppHost.Principal(1605, "finance.manager", "FINANCEMANAGER");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(1606, "commercial", "COMMERCIAL");
        private static readonly ClaimsPrincipal Treasurer = AppHost.Principal(1607, "treasurer", "TREASURY");
        private static readonly ClaimsPrincipal Accountant2 = AppHost.Principal(1608, "accountant.two", "FINANCIAL");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            AppHost host = FinanceTestSupport.NewHost("AgentFin_" + name);
            var e = new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
            host.Write(u =>
            {
                foreach ((int id, string n) in new[] { (1601, "tech.office"), (1602, "supervisor"), (1603, "project.manager"), (1604, "accountant"), (1605, "finance.manager"), (1606, "commercial"), (1607, "treasurer"), (1608, "accountant.two") })
                    AppHost.Add(u, new User { Id = id, UserName = n, Email = n + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
            return e;
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 4; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-FIN"); }
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

        private static void AssertRoles(string expected, AgentTask task) => CollectionAssert.AreEquivalent(expected.Split(','), task.GateRoles.Split(','), task.StepCode + " roles " + task.GateRoles);

        private static PsStatement Statement(Env e, Func<PsStatement, bool> where) => e.Host.Read(u => u.Set<PsStatement>().AsNoTracking().Include(s => s.Lines).ToList().Single(where));

        private static Task<ResponseDto<StatementDto>> Act(Env e, ClaimsPrincipal who, int id, string action, string note = null) =>
            e.Host.As(who, s => s.GetRequiredService<IProgressStatementService>().StatementActionAsync(new FlowActionDto { Id = id, Action = action, Note = note, Date = DateTime.Today }, Ct));

        private static bool IsSeat(Env e, int? id) => id.HasValue && e.Seats.Values.Any(s => s.UserId == id.Value);

        /// <summary>A running contract whose project manager is user 1603.</summary>
        private static int SeedContract(Env e, string code = "C-FIN")
        {
            int id = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = code, Title = "ساخت پمپ", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-30), Price = 100_000_000m });
                u.SaveChanges();
                AppHost.Add(u, new PcContractTerms { ContractId = c.Id, ProjectManagerUserId = 1603, CurrencyCode = "IRR" });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        // ------------------------------------------------------------------------------------------------ the supplier statement, end to end

        [TestMethod]
        public async Task A_Supplier_Statement_Is_Drafted_And_Prepared_By_The_Agent_Then_Approved_By_Three_Gates_And_The_Payment_Is_Requested_By_The_Agent_And_Recorded_By_A_Person()
        {
            Env e = await NewEnv("supplier");
            int contract = SeedContract(e);
            int finSeat = e.Seats["AG-FIN"].UserId;
            int order = 0, orderLine = 0;
            e.Host.Write(u =>
            {
                Provider provider = AppHost.Add(u, new Provider { Name = "پیمانکار جوش" });
                u.SaveChanges();
                PurchaseOrder o = AppHost.Add(u, new PurchaseOrder { Number = "PO-S1", Kind = PurchaseKind.Service, Status = PurchaseOrderStatus.Released, ProviderId = provider.Id, OrderDate = DateTime.Today.AddDays(-20), ContractId = contract, TotalAmount = 10_000_000m });
                u.SaveChanges();
                PurchaseOrderLine l = AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = o.Id, RowNumber = 1, Description = "جوشکاری اسکلت", Unit = "ساعت", Quantity = 10, UnitPrice = 1_000_000m, ReceivedQuantity = 4 });
                u.SaveChanges();
                // the unit accepted 4 of the 10 hours (the acceptance is what the payment request is capped by)
                AppHost.Add(u, new ServiceAcceptance { Number = "SA-1", PurchaseOrderLineId = l.Id, AcceptanceDate = DateTime.Today, Quantity = 4 });
                u.SaveChanges();
                order = o.Id; orderLine = l.Id;
            }, Seeder);

            // 1. the agent drafts the statement from the accepted work (4 of 10 hours) and prepares it; no Input task: the content is in the system
            await Settle(e);
            AgentTask draft = Closed(e, "FIN.STATEMENT.DRAFT");
            Assert.AreEqual(13, draft.Station);
            Assert.AreEqual("AG-FIN", draft.AgentCode);
            Assert.AreEqual(AgentStepKind.Agent, draft.Kind);
            Assert.AreEqual("PurchaseOrder", draft.TargetType);
            PsStatement st = Statement(e, s => s.PurchaseOrderId == order);
            Assert.AreEqual(finSeat, st.CreatorUserId);
            Assert.AreEqual(PsSide.Supplier, st.Side);
            Assert.AreEqual(1, st.Lines.Count);
            Assert.AreEqual(orderLine, st.Lines.Single().OrderLineId);
            Assert.AreEqual(4_000_000m, st.GrossThisPeriod);
            Assert.AreEqual(4_000_000m, st.PayableThisPeriod);
            Closed(e, "FIN.STATEMENT.PREPARE");
            Assert.AreEqual(PsStatus.Prepared, Statement(e, s => s.Id == st.Id).Status);
            Assert.AreEqual(finSeat, Statement(e, s => s.Id == st.Id).PreparedByUserId, "the agent is the maker and the preparer");
            Assert.AreEqual(1, Statement(e, s => true).Lines.Count, "one statement only: the claimed work is not claimed again");
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station == 13 && t.Kind == AgentStepKind.Input), "nothing for a person to type");

            // 2. the technical office (not the agent, not a person who touched it)
            AgentTask tech = OpenOne(e, "FIN.STATEMENT.TECH");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, tech.State);
            AssertRoles("ENGINEERING,PLANNING", tech);
            Assert.AreEqual(contract, tech.ContractId);
            Assert.IsFalse((await Act(e, Supervisor, st.Id, "technical-approve")).Success, "the supervision role is not the technical office");
            Assert.IsTrue((await Act(e, Technical, st.Id, "technical-approve")).Success);
            await Settle(e);
            Closed(e, "FIN.STATEMENT.TECH");

            // 3. the supervision
            AgentTask sup = OpenOne(e, "FIN.STATEMENT.SUPERVISION");
            AssertRoles("QC,CONSTRUCTION", sup);
            Assert.IsTrue((await Act(e, Supervisor, st.Id, "supervision-approve")).Success);
            await Settle(e);
            Closed(e, "FIN.STATEMENT.SUPERVISION");

            // 4. the project manager of THIS contract (a named person, not a role)
            AgentTask authorise = OpenOne(e, "FIN.STATEMENT.AUTHORISE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, authorise.State);
            Assert.AreEqual(1603, authorise.GateUserId);
            Assert.IsTrue(string.IsNullOrEmpty(authorise.GateRoles), "a named person, no role");
            ClaimsPrincipal otherManager = AppHost.Principal(1609, "other.manager", "PROJECTMANAGER");
            e.Host.Write(u => { AppHost.Add(u, new User { Id = 1609, UserName = "other.manager", Email = "o@example.com", IsActive = true }); u.SaveChanges(); }, Seeder);
            Assert.IsFalse((await Act(e, otherManager, st.Id, "authorise-payment")).Success, "the service agrees: only the project manager of the contract");
            Assert.IsTrue((await Act(e, ProjectManager, st.Id, "authorise-payment")).Success);
            await Settle(e);
            Closed(e, "FIN.STATEMENT.AUTHORISE");

            // 5. the agent requests the payment through the payables service
            Closed(e, "FIN.STATEMENT.PAYREQUEST");
            PsStatement requested = Statement(e, s => s.Id == st.Id);
            Assert.AreEqual(PsStatus.PaymentRequested, requested.Status);
            Assert.IsNotNull(requested.PaymentRequestId);
            PaymentRequest request = e.Host.Read(u => u.Set<PaymentRequest>().AsNoTracking().Single(p => p.Id == requested.PaymentRequestId.Value));
            Assert.AreEqual(PaymentRequestType.Progress, request.Type);
            Assert.AreEqual(4_000_000m, request.Amount);
            Assert.AreEqual(0, Open(e, "FIN.STATEMENT.PAID").Count, "the treasury has not paid yet");

            // 6. the treasury pays (station 6 gates, simulated by the state they leave); then the person records how it was paid
            e.Host.Write(u => { PaymentRequest p = u.Set<PaymentRequest>().Single(x => x.Id == request.Id); p.Status = PaymentRequestStatus.Paid; p.PaidAt = DateTimeOffset.Now; p.PaymentReference = "REF-1"; u.SaveChanges(); }, Seeder);
            await Settle(e);
            AgentTask paid = OpenOne(e, "FIN.STATEMENT.PAID");
            Assert.AreEqual(AgentTaskState.WaitingInput, paid.State);
            Assert.AreEqual(AgentStepKind.Input, paid.Kind);
            AssertRoles("FINANCIAL,FINANCEMANAGER,TREASURY", paid);
            ResponseDto<StatementDto> recorded = await e.Host.As(Treasurer, s => s.GetRequiredService<IProgressStatementService>().RecordPaymentAsync(new StatementPaymentDto { Id = st.Id, PaidDate = DateTime.Today, PayMethod = PsPayMethod.BankTransfer, ReceivedAmount = 4_000_000m }, Ct));
            Assert.IsTrue(recorded.Success, recorded.Message);
            await Settle(e);
            Closed(e, "FIN.STATEMENT.PAID");

            // no decision column holds a seat; the statement is complete and the board shows station 13 as done
            PsStatement end = Statement(e, s => s.Id == st.Id);
            Assert.AreEqual(PsStatus.Paid, end.Status);
            foreach (int? decision in new int?[] { end.TechnicalApprovedByUserId, end.SupervisionApprovedByUserId, end.PaymentAuthorisedByUserId, end.PaymentRecordedByUserId })
                Assert.IsFalse(IsSeat(e, decision), "a seat decided");
            Assert.AreEqual(1601, end.TechnicalApprovedByUserId);
            Assert.AreEqual(1603, end.PaymentAuthorisedByUserId);
            Assert.AreEqual(1, e.Host.Read(u => u.Set<PsStatement>().Count()), "no second statement for the same work");
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station == 13 && t.State < AgentTaskState.Done), Dump(e));
            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == contract).Cells.Single(c => c.Station == 13).Status);
        }

        // ------------------------------------------------------------------------------------------------ the client statement

        [TestMethod]
        public async Task A_Client_Statement_Follows_The_Progress_Of_The_Cbs_Items_And_Goes_To_The_Client_After_The_Gates()
        {
            Env e = await NewEnv("client");
            int contract = SeedContract(e);
            e.Host.Write(u =>
            {
                PcWbsNode node = AppHost.Add(u, new PcWbsNode { ContractId = contract, Code = "1", Title = "ساخت" });
                u.SaveChanges();
                AppHost.Add(u, new PcActivity { ContractId = contract, WbsNodeId = node.Id, Code = "A1", Title = "ساخت بدنه", Weight = 1, PercentCache = 50m });
                AppHost.Add(u, new CtCbsItem { ContractId = contract, Code = "C1", Title = "ساخت", Amount = 10_000_000m, WbsNodeId = node.Id });
                u.SaveChanges();
            }, Seeder);

            await Settle(e);
            Closed(e, "FIN.STATEMENT.DRAFT");
            PsStatement st = Statement(e, s => s.ContractId == contract);
            Assert.AreEqual(PsSide.Client, st.Side);
            Assert.AreEqual(5_000_000m, st.GrossThisPeriod, "50% of 10,000,000");
            Assert.AreEqual(50m, st.Lines.Single().ProgressPercent);
            Assert.AreEqual(0, e.Host.Read(u => u.Set<PsStatement>().Count(s => s.Id != st.Id)));
            OpenOne(e, "FIN.STATEMENT.TECH");
            Assert.IsTrue((await Act(e, Technical, st.Id, "technical-approve")).Success);
            await Settle(e);
            Assert.IsTrue((await Act(e, Supervisor, st.Id, "supervision-approve")).Success);
            await Settle(e);
            Assert.IsTrue((await Act(e, ProjectManager, st.Id, "authorise-payment")).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "FIN.STATEMENT.PAYREQUEST").Count, "a client statement has no payment request");

            // physical: the statement goes to the client
            AgentTask send = OpenOne(e, "FIN.STATEMENT.SEND");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, send.State);
            AssertRoles("COMMERCIAL", send);
            Assert.AreEqual(0, Open(e, "FIN.STATEMENT.PAID").Count, "the money is expected after the statement was sent");
            Assert.IsTrue((await Act(e, Commercial, st.Id, "send")).Success);
            await Settle(e);
            Closed(e, "FIN.STATEMENT.SEND");

            // input: the receipt of the money exists only at the bank; the claimed progress is not claimed again
            AgentTask paid = OpenOne(e, "FIN.STATEMENT.PAID");
            Assert.AreEqual(AgentStepKind.Input, paid.Kind);
            Assert.AreEqual(1, e.Host.Read(u => u.Set<PsStatement>().Count()));
            ResponseDto<StatementDto> recorded = await e.Host.As(Accountant, s => s.GetRequiredService<IProgressStatementService>().RecordPaymentAsync(new StatementPaymentDto { Id = st.Id, PaidDate = DateTime.Today, PayMethod = PsPayMethod.BankTransfer, ReceivedAmount = 5_000_000m }, Ct));
            Assert.IsTrue(recorded.Success, recorded.Message);

            // more progress: a second statement for the difference only
            e.Host.Write(u =>
            {
                u.Set<PcActivity>().Single(a => a.ContractId == contract).PercentCache = 80m;
                // the repeat guard of the reconciler keeps a task from coming back within half an hour of its closing: let the first one be old
                foreach (AgentTask t in u.Set<AgentTask>().Where(t => t.StepCode == "FIN.STATEMENT.DRAFT").ToList()) t.ClosedAt = DateTimeOffset.Now.AddHours(-2);
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            List<PsStatement> all = e.Host.Read(u => u.Set<PsStatement>().AsNoTracking().OrderBy(s => s.Id).ToList());
            Assert.AreEqual(2, all.Count, Dump(e));
            Assert.AreEqual(3_000_000m, all[1].GrossThisPeriod, "80% less the 50% that was claimed");
            Assert.IsFalse(IsSeat(e, all[0].PaymentRecordedByUserId));
        }

        // ------------------------------------------------------------------------------------------------ a returned statement is a person's

        [TestMethod]
        public async Task A_Returned_Statement_Is_An_Input_Task_With_The_Reason_And_The_Roles_The_Service_Lets_Edit()
        {
            Env e = await NewEnv("returned");
            int contract = SeedContract(e);
            e.Host.Write(u =>
            {
                Provider provider = AppHost.Add(u, new Provider { Name = "پیمانکار" });
                u.SaveChanges();
                PurchaseOrder o = AppHost.Add(u, new PurchaseOrder { Number = "PO-S2", Kind = PurchaseKind.Service, Status = PurchaseOrderStatus.PartlyReceived, ProviderId = provider.Id, OrderDate = DateTime.Today, ContractId = contract, TotalAmount = 5_000_000m });
                u.SaveChanges();
                PurchaseOrderLine l = AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = o.Id, RowNumber = 1, Description = "نقشه‌برداری", Quantity = 5, UnitPrice = 1_000_000m, ReceivedQuantity = 5 });
                u.SaveChanges();
                AppHost.Add(u, new ServiceAcceptance { Number = "SA-2", PurchaseOrderLineId = l.Id, AcceptanceDate = DateTime.Today, Quantity = 5 });
                u.SaveChanges();
            }, Seeder);
            await Settle(e);
            PsStatement st = Statement(e, s => true);
            Assert.IsTrue((await Act(e, Technical, st.Id, "return", "کسر بیمه فراموش شده است")).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "FIN.STATEMENT.TECH").Count);
            AgentTask fix = OpenOne(e, "FIN.STATEMENT.FIX");
            Assert.AreEqual(AgentTaskState.WaitingInput, fix.State);
            AssertRoles("COMMERCIAL,PMO", fix);
            StringAssert.Contains(fix.Summary, "کسر بیمه فراموش شده است");
            Assert.AreEqual(1, e.Host.Read(u => u.Set<PsStatement>().Count()), "the agent does not draft another statement for the work of a returned one");
        }

        // ------------------------------------------------------------------------------------------------ sales invoice from a milestone

        [TestMethod]
        public async Task A_Sales_Invoice_Draft_Of_The_Milestone_Job_Gets_An_Approval_Gate_For_The_Accountants()
        {
            Env e = await NewEnv("invoice");
            int contract = SeedContract(e);
            int invoice = 0;
            e.Host.Write(u =>
            {
                FnCustomer c = AppHost.Add(u, new FnCustomer { Name = "کارفرما", IsActive = true });
                u.SaveChanges();
                // made the way DraftFromMilestonesAsync makes it: by the system (user 0)
                FnSalesInvoice i = AppHost.Add(u, new FnSalesInvoice { Number = "SI-1", Date = DateTime.Today, CustomerId = c.Id, ContractId = contract, Type = FnInvoiceType.Progress, Status = FnDocStatus.Draft, CreatedByUserId = 0, NetAmount = 1_000_000m, VatAmount = 100_000m, GrossAmount = 1_100_000m, ReceivableAmount = 1_100_000m });
                u.SaveChanges();
                invoice = i.Id;
            }, Seeder);
            await Settle(e);
            AgentTask gate = OpenOne(e, "FIN.INVOICE.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            Assert.AreEqual(13, gate.Station);
            AssertRoles("FINANCIAL,FINANCEMANAGER", gate);
            Assert.AreEqual("/fn/receivables/invoices", gate.Link);
            ResponseDto<SalesInvoiceDto> approved = await e.Host.As(Accountant, s => s.GetRequiredService<IReceivablesService>().InvoiceActionAsync(new DocActionDto { Id = invoice, Action = "approve" }, Ct));
            Assert.IsTrue(approved.Success, approved.Message);
            await Settle(e);
            Closed(e, "FIN.INVOICE.APPROVE");
            Assert.IsFalse(IsSeat(e, e.Host.Read(u => u.Set<FnSalesInvoice>().AsNoTracking().Single(i => i.Id == invoice).ApprovedByUserId)));
        }

        // ------------------------------------------------------------------------------------------------ Sepidar

        [TestMethod]
        public async Task The_Sepidar_Package_Of_A_Closed_Month_Is_Drafted_By_The_Agent_And_Approved_Handed_Over_And_Answered_By_People()
        {
            Env e = await NewEnv("sepidar");
            int contract = SeedContract(e);
            int last = PersianCal.Index(DateTime.Today) - 1;
            int year = PersianCal.YearOfIndex(last), month = PersianCal.MonthOfIndex(last);
            e.Host.Write(u =>
            {
                FnCustomer c = AppHost.Add(u, new FnCustomer { Name = "کارفرما", IsActive = true, NationalId = "10101010101" });
                u.SaveChanges();
                AppHost.Add(u, new FnSalesInvoice
                {
                    Number = "SI-9", Date = PersianCal.Start(year, month).AddDays(4), CustomerId = c.Id, ContractId = contract, Type = FnInvoiceType.Progress, Status = FnDocStatus.Posted,
                    NetAmount = 1_000_000m, VatAmount = 100_000m, GrossAmount = 1_100_000m, ReceivableAmount = 1_100_000m,
                    Lines = new List<FnSalesInvoiceLine> { new FnSalesInvoiceLine { RowNumber = 1, Description = "مرحله", Quantity = 1, UnitPrice = 1_000_000m, Amount = 1_000_000m, VatRatePercent = 10, VatAmount = 100_000m, Total = 1_100_000m } },
                });
                u.SaveChanges();
            }, Seeder);

            await Settle(e);
            AgentTask gen = Closed(e, "FIN.SEPIDAR.GENERATE");
            Assert.AreEqual(13, gen.Station);
            Assert.AreEqual("AG-FIN", gen.AgentCode);
            Assert.AreEqual(AgentStepKind.Agent, gen.Kind);
            FnOutboxPackage package = e.Host.Read(u => u.Set<FnOutboxPackage>().AsNoTracking().Single());
            Assert.AreEqual(FnOutboxKind.SalesInvoicesTaxpayer, package.Kind);
            Assert.AreEqual(FnOutboxStatus.Draft, package.Status);
            Assert.AreEqual(e.Seats["AG-FIN"].UserId, package.CreatedByUserId);
            Assert.AreEqual(year, package.Year);
            Assert.AreEqual(month, package.Month);

            // gate: the finance manager or the CEO, not the maker
            AgentTask gate = OpenOne(e, "FIN.SEPIDAR.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            AssertRoles("FINANCEMANAGER,CEO", gate);
            Assert.AreEqual("/fn/sepidar", gate.Link);
            Assert.IsFalse((await e.Host.As(Accountant, s => s.GetRequiredService<ISepidarOutboxService>().ActionAsync(new OutboxActionDto { Id = package.Id, Action = "approve" }, Ct))).Success, "an accountant is not the finance manager");
            ResponseDto<OutboxPackageDto> approved = await e.Host.As(FinanceManager, s => s.GetRequiredService<ISepidarOutboxService>().ActionAsync(new OutboxActionDto { Id = package.Id, Action = "approve" }, Ct));
            Assert.IsTrue(approved.Success, approved.Message);
            await Settle(e);
            Closed(e, "FIN.SEPIDAR.APPROVE");

            // physical: the hand-over in Sepidar happens outside the system
            AgentTask handover = OpenOne(e, "FIN.SEPIDAR.HANDOVER");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, handover.State);
            Assert.AreEqual(AgentStepKind.Physical, handover.Kind);
            AssertRoles("FINANCIAL,FINANCEMANAGER", handover);
            Assert.AreEqual(0, Open(e, "FIN.SEPIDAR.GENERATE").Count, "an approved package is never regenerated by the agent");
            ResponseDto<OutboxPackageDto> sent = await e.Host.As(Accountant2, s => s.GetRequiredService<ISepidarOutboxService>().ActionAsync(new OutboxActionDto { Id = package.Id, Action = "mark-sent", Reference = "SEP-REF-1", Note = "فایل در سپیدار وارد شد" }, Ct));
            Assert.IsTrue(sent.Success, sent.Message);
            await Settle(e);
            Closed(e, "FIN.SEPIDAR.HANDOVER");
            AgentTask result = OpenOne(e, "FIN.SEPIDAR.RESULT");
            Assert.AreEqual(AgentTaskState.WaitingPhysical, result.State);
            Assert.IsTrue((await e.Host.As(Accountant2, s => s.GetRequiredService<ISepidarOutboxService>().ActionAsync(new OutboxActionDto { Id = package.Id, Action = "mark-accepted", Note = "پذیرفته شد" }, Ct))).Success);
            await Settle(e);
            Closed(e, "FIN.SEPIDAR.RESULT");
            Assert.AreEqual(1, e.Host.Read(u => u.Set<FnOutboxPackage>().Count()), "one package for the month");
            Assert.AreEqual(0, Tasks(e).Count(t => t.Station == 13 && t.State < AgentTaskState.Done), Dump(e));
            Assert.IsFalse(IsSeat(e, e.Host.Read(u => u.Set<FnOutboxPackage>().AsNoTracking().Single().ApprovedByUserId)));
        }

        // ------------------------------------------------------------------------------------------------ roles

        [TestMethod]
        public async Task The_Roles_Of_The_Finance_Agent_Satisfy_The_Role_Check_Of_The_Service_Method_Its_Steps_Call()
        {
            Env e = await NewEnv("roles");
            Func<string, Func<IServiceProvider, Task<string>>, Task<string>> asSeat = (code, body) =>
                AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code, body);
            const string NotAllowed = "شما مجوز این کار را ندارید";

            string save = await asSeat("AG-FIN", async sp => (await sp.GetRequiredService<IProgressStatementService>().SaveStatementAsync(new SaveStatementDto { Side = PsSide.Supplier, PeriodFrom = DateTime.Today, PeriodTo = DateTime.Today }, Ct)).Message);
            Assert.IsFalse(save.Contains(NotAllowed), "SaveStatementAsync: " + save);
            string action = await asSeat("AG-FIN", async sp => (await sp.GetRequiredService<IProgressStatementService>().StatementActionAsync(new FlowActionDto { Id = 0, Action = "prepare" }, Ct)).Message);
            Assert.IsFalse(action.Contains(NotAllowed), "StatementActionAsync: " + action);
            string package = await asSeat("AG-FIN", async sp => (await sp.GetRequiredService<ISepidarOutboxService>().GenerateAsync(new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = 1300, Month = 99 }, Ct)).Message);
            Assert.IsFalse(package.Contains(NotAllowed), "GenerateAsync: " + package);
            // the check discriminates: the warehouse agent holds neither role
            string wrong = await asSeat("AG-WH", async sp => (await sp.GetRequiredService<IProgressStatementService>().SaveStatementAsync(new SaveStatementDto { Side = PsSide.Supplier, PeriodFrom = DateTime.Today, PeriodTo = DateTime.Today }, Ct)).Message);
            StringAssert.Contains(wrong, NotAllowed);
            string wrongPackage = await asSeat("AG-WH", async sp => (await sp.GetRequiredService<ISepidarOutboxService>().GenerateAsync(new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = 1300, Month = 99 }, Ct)).Message);
            StringAssert.Contains(wrongPackage, NotAllowed);
            // the seat holds no approving role of the flow
            CollectionAssert.AreEquivalent(new[] { "FINANCIAL", "COMMERCIAL" }, AiSeatCatalog.Find("AG-FIN").Roles);
        }
    }
}
