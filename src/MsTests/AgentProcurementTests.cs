using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProjectBalance;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.ProcurementDtos;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, station 6 (procurement): the real steps on the in-memory database. The agents run as their own service users (the real jobs of the seats), people act at the gates with the roles
    /// the task names, and the existing services refuse what they refused before. No seat ever signs a decision.
    /// </summary>
    [TestClass]
    public class AgentProcurementTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        // the ids of the people are far above the ids of the service users, so that an id found in a decision column is never a coincidence
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(1031, "ceo", "CEO");
        private static readonly ClaimsPrincipal Auditor = AppHost.Principal(1032, "auditor", "AUDITOR");
        private static readonly ClaimsPrincipal Engineering = AppHost.Principal(1021, "engineering.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal Buyer = AppHost.Principal(1026, "buyer", "COMMERCIAL");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal Warehouse = AppHost.Principal(1061, "storekeeper", "WAREHOUSE");

        private const string NotAllowed = "شما مجوز این کار را ندارید";

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            var host = new AppHost("AgentProc_" + name + "_" + Guid.NewGuid().ToString("N"));
            Dictionary<string, AiActingUser> seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct));
            return new Env { Host = host, Seats = seats };
        }

        // ------------------------------------------------------------------------------------------------ running things

        /// <summary>Runs the real job of a seat as its service user (the way the scheduler does).</summary>
        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task<AiSeatJobResult> Master(Env e)
        {
            AiSeatJobResult r = await RunSeat(e, "AG-MASTER");
            Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error);
            return r;
        }

        private static async Task<AiSeatJobResult> Agent(Env e, string code)
        {
            AiSeatJobResult r = await RunSeat(e, code);
            Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error);
            return r;
        }

        private static List<AgentTask> Tasks(Env e) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().OrderBy(t => t.Id).ToList());
        private static List<AgentTask> Open(Env e, string step) => Tasks(e).Where(t => t.StepCode == step && t.State < AgentTaskState.Done).ToList();
        private static AgentTask OpenOne(Env e, string step) => Open(e, step).Single();
        private static AgentTask Closed(Env e, string step) => Tasks(e).Single(t => t.StepCode == step && t.State == AgentTaskState.Done);
        private static string Dump(Env e) => string.Join(" | ", Tasks(e).Select(t => t.StepCode + "#" + t.TargetId + ":" + t.State));

        private static List<PurchaseRequest> Requests(Env e) => e.Host.Read(u => u.Set<PurchaseRequest>().AsNoTracking().Include(r => r.Lines).OrderBy(r => r.Id).ToList());
        private static PurchaseRequest RequestOf(Env e, int id) => Requests(e).Single(r => r.Id == id);
        private static List<PurchaseInquiry> Inquiries(Env e) => e.Host.Read(u => u.Set<PurchaseInquiry>().AsNoTracking().Include(i => i.Quotes).OrderBy(i => i.Id).ToList());

        private static int UserOf(Env e, string seat) => e.Seats[seat].UserId;

        // ------------------------------------------------------------------------------------------------ the seeds

        private sealed class Seed
        {
            public int ProviderA, ProviderB, Item;
        }

        /// <summary>An item under its reorder point (safety stock 10, no stock, reorder quantity 10), two suppliers, and the last purchase price of the item when <paramref name="price"/> is given.</summary>
        private static Seed SeedWorld(Env e, decimal? price)
        {
            var seed = new Seed();
            e.Host.Write(u =>
            {
                Provider a = AppHost.Add(u, new Provider { Name = "supplier A" });
                Provider b = AppHost.Add(u, new Provider { Name = "supplier B" });
                InventoryItem item = AppHost.Add(u, new InventoryItem { Code = "I-100", Title = "plate", Unit = "عدد", MinStock = 10m, ReorderQuantity = 10m });
                u.SaveChanges();
                if (price.HasValue) AppHost.Add(u, new ItemCost { ItemId = item.Id, LastPurchasePrice = price, AverageCost = price.Value });
                u.SaveChanges();
                seed.ProviderA = a.Id; seed.ProviderB = b.Id; seed.Item = item.Id;
            }, Seeder);
            return seed;
        }

        /// <summary>An approved request (planning approved) of 10 plates at 500,000 for the inquiry steps; optionally of a contract.</summary>
        private static int SeedApprovedRequest(Env e, Seed seed, int? contractId = null)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                var request = AppHost.Add(u, new PurchaseRequest
                {
                    Number = "PR-" + Guid.NewGuid().ToString("N").Substring(0, 6), Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Approved,
                    RequestDate = DateTime.Today, EstimatedAmount = 5_000_000m, PlanningApprovedAt = DateTimeOffset.Now, PlanningApprovedByUserId = 1012, ContractId = contractId, CreatorUserId = 1012,
                });
                u.SaveChanges();
                AppHost.Add(u, new PurchaseRequestLine { PurchaseRequestId = request.Id, RowNumber = 1, ItemId = seed.Item, Description = "plate", Unit = "عدد", Quantity = 10m, EstimatedUnitPrice = 500_000m });
                u.SaveChanges();
                id = request.Id;
            }, Seeder);
            return id;
        }

        private static Task<ResponseDto<InquiryDto>> SaveQuotes(Env e, ClaimsPrincipal who, int inquiryId, int requestId, params (int Provider, decimal UnitPrice, int Days)[] offers)
        {
            int line = RequestOf(e, requestId).Lines.Single().Id;
            var dto = new SaveInquiryDto
            {
                Id = inquiryId, PurchaseRequestId = requestId, InquiryDate = DateTime.Today,
                Quotes = offers.Select(o => new SaveQuoteDto
                {
                    ProviderId = o.Provider, QuoteNumber = "Q-" + o.Provider, QuoteDate = DateTime.Today, DeliveryDays = o.Days, PaymentTerms = "net 30", VatPercent = 10m,
                    Lines = new List<SaveQuoteLineDto> { new SaveQuoteLineDto { RequestLineId = line, Quantity = 10m, UnitPrice = o.UnitPrice } },
                }).ToList(),
            };
            return e.Host.As(who, p => p.GetRequiredService<IProcurementService>().SaveInquiryAsync(dto, Ct));
        }

        private static Task<ResponseDto<InquiryDto>> Judge(Env e, ClaimsPrincipal who, int inquiryId, int quoteId, bool accepted) =>
            e.Host.As(who, p => p.GetRequiredService<IProcurementService>().TechnicalReviewAsync(new TechnicalReviewDto { InquiryId = inquiryId, QuoteId = quoteId, Accepted = accepted, Score = accepted ? 90m : (decimal?)null, Note = accepted ? "ok" : "no certificate" }, Ct));

        private static Task<ResponseDto<PurchaseRequestDto>> RequestAction(Env e, ClaimsPrincipal who, int id, string action, string note = null) =>
            e.Host.As(who, p => p.GetRequiredService<IProcurementService>().RequestActionAsync(new ProcurementActionDto { Id = id, Action = action, Note = note }, Ct));

        private static Task<ResponseDto<InquiryDto>> InquiryAction(Env e, ClaimsPrincipal who, int id, string action) =>
            e.Host.As(who, p => p.GetRequiredService<IProcurementService>().InquiryActionAsync(new ProcurementActionDto { Id = id, Action = action }, Ct));

        /// <summary>Every column of the documents that the data layer watches as a decision (approval, review, selection, signature): the id of a seat is in none of them.</summary>
        private static void AssertNoSeatInAnyDecisionColumn(Env e)
        {
            HashSet<int> seatIds = e.Seats.Values.Select(s => s.UserId).ToHashSet();
            var seen = new List<string>();
            foreach (Type type in new[] { typeof(PurchaseRequest), typeof(PurchaseInquiry), typeof(SupplierQuote), typeof(PurchaseOrder) })
            {
                var props = type.GetProperties().Where(p => SeatApprovalGuard.MatchesName(p.Name) && (p.PropertyType == typeof(int) || p.PropertyType == typeof(int?))).ToList();
                Assert.IsTrue(props.Count > 0, type.Name + " has decision columns");
                IEnumerable<object> rows = (IEnumerable<object>)typeof(AgentProcurementTests).GetMethod(nameof(RowsOf), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(type).Invoke(null, new object[] { e });
                foreach (object row in rows)
                    foreach (PropertyInfo p in props)
                    {
                        object value = p.GetValue(row);
                        if (value is int id && seatIds.Contains(id)) seen.Add(type.Name + "." + p.Name + "=" + id);
                    }
            }
            Assert.AreEqual(0, seen.Count, string.Join(", ", seen));
        }

        private static List<object> RowsOf<T>(Env e) where T : class => e.Host.Read(u => u.Set<T>().AsNoTracking().Cast<object>().ToList());

        // ------------------------------------------------------------------------------------------------ the catalogue

        [TestMethod]
        public async Task The_Catalogue_Has_No_Duplicate_Codes_And_Every_Step_Belongs_To_A_Registered_Agent_Of_Station_Six()
        {
            Env e = await NewEnv("catalog");
            using IServiceScope scope = e.Host.Provider.CreateScope();
            List<IAgentStep> steps = scope.ServiceProvider.GetServices<IAgentStep>().ToList();
            var procurement = steps.Where(s => s.Station == 6 && s.Code != null && !s.Code.StartsWith("TEST.")).ToList();
            Assert.AreEqual(199, steps.Count, "the registrations of the application");
            Assert.AreEqual(199, steps.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "no duplicate code");
            Assert.AreEqual(34, procurement.Count, "the station 6 steps");
            IAgentStepCatalog catalog = scope.ServiceProvider.GetRequiredService<IAgentStepCatalog>();
            Assert.AreEqual(199, catalog.All.Count);
            foreach (IAgentStep s in steps)
            {
                AiSeatDefinition seat = AiSeatCatalog.Find(s.AgentCode);
                Assert.IsNotNull(seat, s.Code + " names an agent that is not a seat: " + s.AgentCode);
                Assert.IsTrue(AgentCodes.Process.Contains(s.AgentCode), s.Code);
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.Title), s.Code);
                Assert.IsFalse(s.Title.Contains("عامل") && !s.Title.Contains("مدیرعامل"), s.Code + " says عامل");
                var info = s.GateInfo(new AgentWorkItem { TargetType = "X", TargetId = 7, TargetNumber = "N-7" });
                if (s.Kind != AgentStepKind.Agent)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(info.Link), s.Code + " has no link");
                    Assert.IsTrue(info.Link.StartsWith("/"), s.Code + " link " + info.Link);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(info.Roles), s.Code + " names no roles");
                }
            }
            // the agents that make documents: the ones whose method the step calls (the codes of the registrations of an agent)
            string[] agentSteps = steps.Where(s => s.Kind == AgentStepKind.Agent).Select(s => s.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "CON.REVIEW.DRAFT", "CON.TERMS.DRAFT", "ENG.CASE.AWARD", "ENG.CASE.CLOSE", "ENG.CASE.OPEN", "ENG.CASE.PROPOSE", "ENG.DEBT.SETTLE", "ENG.DIST.NOTIFY", "ENG.HANDOVER.DRAFT", "ENG.INSTANTIATE", "ENG.PLATFORM.ASSESS", "ENG.REF.DRAFT", "ENG.RISK.ESCALATE", "ENG.TENDER.DRAFT", "ENG.TENDER.REVALIDATE", "FIN.SEPIDAR.GENERATE", "FIN.STATEMENT.DRAFT", "FIN.STATEMENT.PAYREQUEST", "FIN.STATEMENT.PREPARE", "INIT.BOM", "INIT.CHARTER.DRAFT", "INIT.CODE", "INIT.PEP.DRAFT", "INIT.START", "INIT.UNITS", "MF.COMPONENTS", "MF.ORDERS.DRAFT", "MF.RELEASE.CHECK", "PLAN.BALANCE.RUN", "PLAN.BASELINE.CREATE", "PLAN.CPM", "PLAN.ITP.ACTIVITIES", "PLAN.PR.BOM", "PLAN.WBS.DICTIONARY", "PLAN.WBS.GENERATE", "PLAN.WEIGHTS", "PROC.INQ.OPEN", "PROC.INQ.SELECT", "PROC.PAY.DRAFT", "PROC.PO.DRAFT", "PROC.PO.REQUIREMENTS", "PROC.PR.PRICE.AUTO.PLAN", "PROC.PR.PRICE.AUTO.WH", "PROC.PR.REVISE.PLAN", "PROC.PR.REVISE.WH", "PROC.PR.SUBMIT.PLAN", "PROC.PR.SUBMIT.WH", "QC.CAPA.IMPLEMENT", "QC.ITP.ENSURE", "QC.ITP.SUBMIT", "QC.ITP.USE", "QC.RELEASE.DRAFT", "RES.COST.PROPOSE", "RES.LEVEL.PROPOSE", "RES.LOAD.CHECK", "RES.PLAN.DRAFT", "SHIP.DRAFT", "SL.BID.SUBMIT", "SL.ESTIMATE.FROM-EQUIPMENT", "SL.PROPOSAL.DRAFT", "SP.OUTBOX.PREPARE", "SP.OUTBOX.SEND", "SV.INVOICE.DRAFT", "SV.ORDER.DRAFT", "WH.ISSUE.DRAFT", "WH.KIT.DRAFT", "WH.MR.SUBMIT", "WH.PR.REORDER"
            }, agentSteps);
            // the two registrations of one class have distinct codes and distinct agents
            Assert.AreEqual("AG-WH", steps.Single(s => s.Code == "PROC.PR.SUBMIT.WH").AgentCode);
            Assert.AreEqual("AG-PLAN", steps.Single(s => s.Code == "PROC.PR.SUBMIT.PLAN").AgentCode);
            Assert.AreEqual("AG-WH", steps.Single(s => s.Code == "PROC.PR.REVISE.WH").AgentCode);
            Assert.AreEqual("AG-PLAN", steps.Single(s => s.Code == "PROC.PR.REVISE.PLAN").AgentCode);
        }

        [TestMethod]
        public async Task The_Roles_Of_Every_Agent_Satisfy_The_Role_Check_Of_The_Service_Method_Its_Step_Calls()
        {
            Env e = await NewEnv("roles");
            Func<string, Func<IServiceProvider, Task<string>>, Task<string>> asSeat = (code, body) =>
                AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code, body);

            // the role check is the first thing the method does; what the invalid input then answers is not a refusal of the role
            Func<string, string> notRefused = message => message ?? string.Empty;

            // AG-WH: SaveRequestAsync (reorder) and RequestActionAsync (submit, revise)
            string wh = await asSeat("AG-WH", async sp => (await sp.GetRequiredService<IProcurementService>().SaveRequestAsync(new SavePurchaseRequestDto { Source = PurchaseRequestSource.ReorderPoint, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today }, Ct)).Message);
            Assert.IsFalse(notRefused(wh).Contains(NotAllowed), "AG-WH SaveRequestAsync: " + wh);
            string whSubmit = await asSeat("AG-WH", async sp => (await sp.GetRequiredService<IProcurementService>().RequestActionAsync(new ProcurementActionDto { Id = 0, Action = "submit" }, Ct)).Message);
            Assert.IsFalse(notRefused(whSubmit).Contains(NotAllowed), whSubmit);
            // AG-PLAN: the same two methods, and the balance run of the BOM step
            string plan = await asSeat("AG-PLAN", async sp => (await sp.GetRequiredService<IProcurementService>().SaveRequestAsync(new SavePurchaseRequestDto { Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today }, Ct)).Message);
            Assert.IsFalse(notRefused(plan).Contains(NotAllowed), "AG-PLAN SaveRequestAsync: " + plan);
            string balance = await asSeat("AG-PLAN", async sp => (await sp.GetRequiredService<IProjectBalanceService>().RunAsync(new BalanceRunRequestDto { CreateDrafts = true }, Ct)).Message);
            Assert.AreEqual(BalanceMessages.InvalidData, balance, "the role of AG-PLAN passes the first check of the balance run; the empty request is what is refused");
            // the check discriminates: the warehouse agent has no planning role
            string wrong = await asSeat("AG-WH", async sp => (await sp.GetRequiredService<IProjectBalanceService>().RunAsync(new BalanceRunRequestDto { CreateDrafts = true }, Ct)).Message);
            Assert.AreEqual(BalanceMessages.NotAllowed, wrong);
            // AG-PROC: the inquiry, the selection, the order and the payment request (COMMERCIAL)
            string inquiry = await asSeat("AG-PROC", async sp => (await sp.GetRequiredService<IProcurementService>().SaveInquiryAsync(new SaveInquiryDto { PurchaseRequestId = 0, InquiryDate = DateTime.Today }, Ct)).Message);
            Assert.IsFalse(notRefused(inquiry).Contains(NotAllowed), "AG-PROC SaveInquiryAsync: " + inquiry);
            string select = await asSeat("AG-PROC", async sp => (await sp.GetRequiredService<IProcurementService>().SelectQuoteAsync(new SelectQuoteDto { InquiryId = 0, QuoteId = 0 }, Ct)).Message);
            Assert.IsFalse(notRefused(select).Contains(NotAllowed), "AG-PROC SelectQuoteAsync: " + select);
            string order = await asSeat("AG-PROC", async sp => (await sp.GetRequiredService<IProcurementService>().CreateOrderFromInquiryAsync(0, Ct)).Message);
            Assert.IsFalse(notRefused(order).Contains(NotAllowed), "AG-PROC CreateOrderFromInquiryAsync: " + order);
            string payment = await asSeat("AG-PROC", async sp => (await sp.GetRequiredService<IPayablesService>().SavePaymentAsync(new SavePaymentRequestDto { Amount = 0m }, Ct)).Message);
            Assert.AreEqual("مبلغ یا کسورات نامعتبر است.", payment, "the commercial role passes; the amount is what is refused");
            string wrongPay = await asSeat("AG-WH", async sp => (await sp.GetRequiredService<IPayablesService>().SavePaymentAsync(new SavePaymentRequestDto { Amount = 0m }, Ct)).Message);
            StringAssert.Contains(wrongPay, NotAllowed);
        }

        // ------------------------------------------------------------------------------------------------ the chain

        [TestMethod]
        public async Task The_Reorder_Step_Does_Not_Recompute_The_List_While_Its_Task_Waits_For_The_Agent_Or_Was_Done_Less_Than_Half_An_Hour_Ago()
        {
            Env e = await NewEnv("reorder-throttle");
            Func<Task<int>> discover = async () => (await e.Host.As(Seeder, sp => sp.GetServices<IAgentStep>().Single(s => s.Code == "WH.PR.REORDER").DiscoverAsync(sp.GetRequiredService<IUnitOfWork>(), DateTime.Today, Ct))).Count;
            Action<AgentTaskState, DateTimeOffset?> setTask = (state, closed) => e.Host.Write(u =>
            {
                u.Set<AgentTask>().RemoveRange(u.Set<AgentTask>().Where(t => t.StepCode == "WH.PR.REORDER").ToList());
                AppHost.Add(u, new AgentTask { StepCode = "WH.PR.REORDER", Station = 7, AgentCode = "AG-WH", Kind = AgentStepKind.Agent, State = state, TargetType = "ReorderRun", TargetId = 1, ClosedAt = closed, CreateDate = DateTimeOffset.Now.AddHours(-2) });
                u.SaveChanges();
            }, Seeder);

            // nothing is under its point: no task is needed
            Assert.AreEqual(0, await discover());
            // a queued task of the agent is kept without reading the list (no item is short here: only the open task explains the answer)
            setTask(AgentTaskState.Queued, null);
            Assert.AreEqual(1, await discover());

            // a shortage now exists
            SeedWorld(e, 500_000m);
            // done a moment ago: the next window picks the shortage up (the list is not read again before)
            setTask(AgentTaskState.Done, DateTimeOffset.Now.AddMinutes(-5));
            Assert.AreEqual(0, await discover());
            // done long ago: the list is read and the shortage is found
            setTask(AgentTaskState.Done, DateTimeOffset.Now.AddHours(-2));
            Assert.AreEqual(1, await discover());
            // a task that waits for a person (fallback) is not shortcut: the real list decides
            setTask(AgentTaskState.WaitingHumanGate, null);
            Assert.AreEqual(1, await discover());
        }

        [TestMethod]
        public async Task Smoke_contract_reorder_draft_without_price_source()
        {
            Env e = await NewEnv("smoke-noprice");
            Seed seed = SeedWorld(e, 500_000m);
            int ghost = 0;
            e.Host.Write(u =>
            {
                // never bought, never quoted, no stock, no cost: nothing in the system can price it
                InventoryItem g = AppHost.Add(u, new InventoryItem { Code = "I-900", Title = "ghost part", Unit = "عدد", MinStock = 5m, ReorderQuantity = 5m });
                u.SaveChanges();
                ghost = g.Id;
            }, Seeder);

            await Master(e);
            await Agent(e, "AG-WH");

            // (a) exactly one WH.PR.REORDER task, Done, output = the draft
            List<AgentTask> reorders = Tasks(e).Where(t => t.StepCode == "WH.PR.REORDER").ToList();
            Assert.AreEqual(1, reorders.Count, Dump(e));
            Assert.AreEqual(AgentTaskState.Done, reorders[0].State);
            Assert.AreEqual(10, (int)AgentTaskState.Done);
            Assert.AreEqual("PurchaseRequest", reorders[0].OutputType);
            PurchaseRequest draft = Requests(e).Single();
            Assert.AreEqual(draft.Id, reorders[0].OutputId);
            Assert.AreEqual(PurchaseRequestStatus.Draft, draft.Status);
            Assert.AreEqual(2, draft.Lines.Count, "one priceable line and one unpriced-forever line");
            Assert.IsTrue(draft.Lines.Single(l => l.ItemId == seed.Item).EstimatedUnitPrice > 0);
            Assert.IsTrue((draft.Lines.Single(l => l.ItemId == ghost).EstimatedUnitPrice ?? 0) <= 0);

            // (b) one open Input for planning on that request
            AgentTask price = OpenOne(e, "PROC.PR.PRICE.WH");
            Assert.AreEqual(AgentTaskState.WaitingInput, price.State);
            Assert.AreEqual("PLANNING", price.GateRoles);
            Assert.AreEqual(draft.Id, price.TargetId);
            Assert.AreEqual("/procurement/requests/" + draft.Id, price.Link);
            StringAssert.Contains(price.Summary, "ghost part");

            // (c) one open submit task, Queued, not counted as an attempt, with the reason
            AgentTask submit = OpenOne(e, "PROC.PR.SUBMIT.WH");
            Assert.AreEqual(AgentTaskState.Queued, submit.State);
            Assert.AreEqual(draft.Id, submit.TargetId);
            Assert.AreEqual(0, submit.AttemptCount);
            StringAssert.Contains(submit.LastError, "قیمت برآوردی ردیف‌ها کامل نیست");

            // (d) a second run makes no second reorder task and no second draft
            await Agent(e, "AG-WH");
            Assert.AreEqual(1, Tasks(e).Count(t => t.StepCode == "WH.PR.REORDER"), Dump(e));
            Assert.AreEqual(1, Requests(e).Count);
            Assert.AreEqual(1, Open(e, "PROC.PR.PRICE.WH").Count);
            Assert.AreEqual(1, Open(e, "PROC.PR.SUBMIT.WH").Count);
        }

        [TestMethod]
        public async Task The_Chain_From_The_Reorder_List_To_The_Order_Draft_Is_Made_By_The_Agents_And_Decided_By_People()
        {
            Env e = await NewEnv("chain");
            Seed seed = SeedWorld(e, 500_000m);
            int whId = UserOf(e, "AG-WH"), procId = UserOf(e, "AG-PROC");

            // the older seat stands down while the step of the agent is on: no draft, so the agent's draft is the only one
            AiSeatJobResult old = await RunSeat(e, "WH-REORDER");
            Assert.AreEqual(AiSeatRunStatus.Succeeded, old.Status, old.Error);
            StringAssert.Contains(old.Summary, "superseded by AG-WH");
            Assert.AreEqual(0, Requests(e).Count);

            // the master finds the reorder run and makes the task of the warehouse agent
            AiSeatJobResult m = await Master(e);
            AgentTask reorder = OpenOne(e, "WH.PR.REORDER");
            Assert.AreEqual("AG-WH", reorder.AgentCode);
            Assert.AreEqual(AgentStepKind.Agent, reorder.Kind);
            Assert.AreEqual(6, reorder.Station);
            Assert.AreEqual(AgentTaskState.Queued, reorder.State);

            // the warehouse agent drafts the request from the list and submits it in the same run (the round reconciles in between)
            await Agent(e, "AG-WH");
            PurchaseRequest request = Requests(e).Single();
            Assert.AreEqual(PurchaseRequestStatus.Submitted, request.Status, Dump(e));
            Assert.AreEqual(PurchaseRequestSource.ReorderPoint, request.Source);
            Assert.AreEqual(whId, request.CreatorUserId, "the agent is the maker, never the approver");
            Assert.AreEqual(5_000_000m, request.EstimatedAmount);
            Assert.AreEqual(1, request.Lines.Count);
            Assert.AreEqual(seed.Item, request.Lines.Single().ItemId);
            Assert.AreEqual(10m, request.Lines.Single().Quantity);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "WH.PR.REORDER").State);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.PR.SUBMIT.WH").State);
            Assert.AreEqual("PurchaseRequest", Closed(e, "PROC.PR.SUBMIT.WH").OutputType, "the submit step has no output of its own: the request is its target");

            // the old seat still stands down, and the agent does not make a second draft for the item that is now on an open request
            StringAssert.Contains((await RunSeat(e, "WH-REORDER")).Summary, "superseded by AG-WH");
            await Agent(e, "AG-WH");
            Assert.AreEqual(1, Requests(e).Count, "no doubled drafts");

            // the gate of planning: the roles of the task are the ones the service demands
            await Master(e);
            AgentTask gate = OpenOne(e, "PROC.PR.APPROVE");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, gate.State);
            Assert.AreEqual("PLANNING,PMO", gate.GateRoles);
            Assert.AreEqual("/procurement/requests/" + request.Id, gate.Link);
            Assert.IsFalse((await RequestAction(e, Buyer, request.Id, "approve-planning")).Success, "the buyer does not approve");
            Assert.IsFalse((await RequestAction(e, Warehouse, request.Id, "approve-planning")).Success);
            ResponseDto<PurchaseRequestDto> approved = await RequestAction(e, Planner, request.Id, "approve-planning");
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(PurchaseRequestStatus.Approved, RequestOf(e, request.Id).Status);
            Assert.AreEqual(1051, RequestOf(e, request.Id).PlanningApprovedByUserId);

            // the procurement agent opens the inquiry; the buyer is asked for the quotes
            await Master(e);
            await Agent(e, "AG-PROC");
            PurchaseInquiry inquiry = Inquiries(e).Single();
            Assert.AreEqual(request.Id, inquiry.PurchaseRequestId);
            Assert.AreEqual(InquiryStatus.Open, inquiry.Status);
            Assert.AreEqual(0, inquiry.Quotes.Count);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.INQ.OPEN").State);
            AgentTask quotesTask = OpenOne(e, "PROC.INQ.QUOTES");
            Assert.AreEqual(AgentStepKind.Input, quotesTask.Kind);
            Assert.AreEqual(AgentTaskState.WaitingInput, quotesTask.State);
            Assert.AreEqual("COMMERCIAL", quotesTask.GateRoles);
            Assert.AreEqual("/procurement/inquiries/" + inquiry.Id, quotesTask.Link);
            Assert.AreEqual(0, Open(e, "PROC.INQ.TECH").Count, "nothing to judge yet");

            // the buyer enters two quotes; the engineer judges them
            ResponseDto<InquiryDto> quoted = await SaveQuotes(e, Buyer, inquiry.Id, request.Id, (seed.ProviderA, 480_000m, 30), (seed.ProviderB, 520_000m, 20));
            Assert.IsTrue(quoted.Success, quoted.Message);
            await Master(e);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.INQ.QUOTES").State);
            AgentTask tech = OpenOne(e, "PROC.INQ.TECH");
            Assert.AreEqual(AgentTaskState.WaitingHumanGate, tech.State);
            Assert.AreEqual("ENGINEERING,QC", tech.GateRoles);
            Assert.IsFalse((await Judge(e, Buyer, inquiry.Id, quoted.Data.Quotes[0].Id, true)).Success, "the buyer does not judge");
            foreach (QuoteDto q in quoted.Data.Quotes)
            {
                ResponseDto<InquiryDto> judged = await Judge(e, Engineering, inquiry.Id, q.Id, true);
                Assert.IsTrue(judged.Success, judged.Message);
            }

            // the gate of planning on the inquiry (budget and delivery time), then the agent selects the lowest quote
            await Master(e);
            Assert.AreEqual(0, Open(e, "PROC.INQ.TECH").Count);
            AgentTask planning = OpenOne(e, "PROC.INQ.PLAN");
            Assert.AreEqual("PLANNING,PMO", planning.GateRoles);
            Assert.AreEqual(0, Open(e, "PROC.INQ.SELECT").Count, "the agent waits for the planning approval");
            Assert.IsFalse((await InquiryAction(e, Buyer, inquiry.Id, "approve-planning")).Success);
            Assert.IsTrue((await InquiryAction(e, Planner, inquiry.Id, "approve-planning")).Success);

            await Master(e);
            AgentTask select = OpenOne(e, "PROC.INQ.SELECT");
            Assert.AreEqual(AgentTaskState.Queued, select.State);
            Assert.AreEqual(0, Open(e, "PROC.INQ.SELECT-MANUAL").Count);
            await Agent(e, "AG-PROC");
            PurchaseInquiry selected = Inquiries(e).Single();
            Assert.AreEqual(selected.Quotes.Single(q => q.ProviderId == seed.ProviderA).Id, selected.SelectedQuoteId, "the lowest quote");
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.INQ.SELECT").State);

            // the draft of the order is made from the winner by the agent; the approval and the signing stay with people
            PurchaseOrder order = e.Host.Read(u => u.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines).Single());
            Assert.AreEqual(PurchaseOrderStatus.Draft, order.Status);
            Assert.AreEqual(seed.ProviderA, order.ProviderId);
            Assert.AreEqual(inquiry.Id, order.InquiryId);
            Assert.AreEqual(procId, order.CreatorUserId);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.PO.DRAFT").State);
            // the requirements are written by the agent from the request and the quote: nobody types them, and no contract is needed below the limit
            StringAssert.StartsWith(order.Requirements, "مطابق مشخصات فنی درخواست خرید " + request.Number);
            StringAssert.Contains(order.Requirements, inquiry.Number);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.PO.REQUIREMENTS").State);
            Assert.AreEqual(0, Open(e, "PROC.PO.COMPLETE").Count, "nothing is left for the buyer to type");
            Assert.AreEqual(0, Tasks(e).Count(t => t.Kind == AgentStepKind.Input && t.State < AgentTaskState.Done && t.StepCode != "PROC.INQ.QUOTES"), "the only input of the whole chain was the supplier quotes");

            // the whole chain left no seat in a decision column
            AssertNoSeatInAnyDecisionColumn(e);
            Assert.IsNull(order.ApprovedByUserId);
            Assert.IsNull(order.ReleasedByUserId);
            // the audit: every run of an agent step wrote its own event row
            Assert.IsTrue(e.Host.Read(u => u.Set<AiAuditEvent>().Count(a => a.Kind == AiAuditKinds.Agent)) > 0);
        }

        // ------------------------------------------------------------------------------------------------ the refusals and the people

        [TestMethod]
        public async Task A_Draft_Without_Prices_Is_Not_Submitted_And_A_Person_Is_Asked_To_Price_It()
        {
            Env e = await NewEnv("unpriced");
            SeedWorld(e, null);
            await Master(e);
            await Agent(e, "AG-WH");
            PurchaseRequest draft = Requests(e).Single();
            Assert.AreEqual(PurchaseRequestStatus.Draft, draft.Status, "an unpriced request has an estimate of zero and would skip the management approval");
            Assert.AreEqual(0m, draft.EstimatedAmount);
            Assert.AreEqual(UserOf(e, "AG-WH"), draft.CreatorUserId);

            // the submit step waits (it is not a failure) and the price step is asked of a person
            AgentTask submit = OpenOne(e, "PROC.PR.SUBMIT.WH");
            Assert.AreEqual(AgentTaskState.Queued, submit.State, Dump(e));
            Assert.AreEqual(0, submit.AttemptCount, "waiting is not an attempt");
            Assert.IsNotNull(submit.NextAttemptAt, "it is tried again later");
            StringAssert.Contains(submit.LastError ?? string.Empty, "قیمت برآوردی ردیف‌ها کامل نیست");
            AgentTask price = OpenOne(e, "PROC.PR.PRICE.WH");
            Assert.AreEqual(AgentStepKind.Input, price.Kind);
            Assert.AreEqual(AgentTaskState.WaitingInput, price.State);
            Assert.AreEqual("PLANNING", price.GateRoles);
            Assert.AreEqual("/procurement/requests/" + draft.Id, price.Link);
            StringAssert.Contains(price.Summary, "ایجنت");
            StringAssert.Contains(price.Summary, "قیمت");
            Assert.AreEqual(0, Open(e, "PROC.PR.APPROVE").Count);

            // the agent runs again and still does not submit
            await Agent(e, "AG-WH");
            Assert.AreEqual(PurchaseRequestStatus.Draft, RequestOf(e, draft.Id).Status);

            // the planner prices the line and submits: the planner becomes the maker, so another person approves
            int line = RequestOf(e, draft.Id).Lines.Single().Id;
            ResponseDto<PurchaseRequestDto> saved = await e.Host.As(Planner, p => p.GetRequiredService<IProcurementService>().SaveRequestAsync(new SavePurchaseRequestDto
            {
                Id = draft.Id, Source = PurchaseRequestSource.ReorderPoint, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today, Submit = false,
                Lines = new List<SavePurchaseRequestLineDto> { new SavePurchaseRequestLineDto { ItemId = draft.Lines.Single().ItemId, Unit = "عدد", Quantity = 10m, EstimatedUnitPrice = 400_000m } },
            }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await RequestAction(e, Planner, draft.Id, "submit")).Success);
            PurchaseRequest submitted = RequestOf(e, draft.Id);
            Assert.AreEqual(PurchaseRequestStatus.Submitted, submitted.Status);
            Assert.AreEqual(4_000_000m, submitted.EstimatedAmount);
            Assert.AreEqual(1051, submitted.CreatorUserId);
            await Master(e);
            Assert.AreEqual(0, Open(e, "PROC.PR.PRICE.WH").Count);
            Assert.AreEqual(0, Open(e, "PROC.PR.SUBMIT.WH").Count);
            Assert.AreEqual("PLANNING,PMO", OpenOne(e, "PROC.PR.APPROVE").GateRoles);
            // the person who priced and submitted it cannot approve it
            Assert.IsFalse((await RequestAction(e, Planner, draft.Id, "approve-planning")).Success);
            Assert.IsTrue((await RequestAction(e, AppHost.Principal(1052, "planner.two", "PLANNING"), draft.Id, "approve-planning")).Success);
        }

        [TestMethod]
        public async Task The_Lowest_Quote_Of_A_Supplier_In_Grade_D_Is_Not_Selected_By_The_Agent_And_The_Buyer_Is_Asked_With_The_Reason()
        {
            Env e = await NewEnv("graded");
            Seed seed = SeedWorld(e, 500_000m);
            e.Host.Write(u => { AppHost.Add(u, new SupplierProfile { ProviderId = seed.ProviderA, Grade = SupplierGrade.D }); AppHost.Add(u, new SupplierProfile { ProviderId = seed.ProviderB, Grade = SupplierGrade.A }); u.SaveChanges(); }, Seeder);
            int requestId = SeedApprovedRequest(e, seed);
            await Master(e);
            await Agent(e, "AG-PROC");
            PurchaseInquiry inquiry = Inquiries(e).Single();
            ResponseDto<InquiryDto> quoted = await SaveQuotes(e, Buyer, inquiry.Id, requestId, (seed.ProviderA, 400_000m, 30), (seed.ProviderB, 520_000m, 30));
            Assert.IsTrue(quoted.Success, quoted.Message);
            foreach (QuoteDto q in quoted.Data.Quotes) Assert.IsTrue((await Judge(e, Engineering, inquiry.Id, q.Id, true)).Success);
            Assert.IsTrue((await InquiryAction(e, Planner, inquiry.Id, "approve-planning")).Success);

            await Master(e);
            await Agent(e, "AG-PROC");
            PurchaseInquiry after = Inquiries(e).Single();
            Assert.AreEqual(InquiryStatus.Open, after.Status, "the agent did not select the grade D supplier");
            Assert.IsNull(after.SelectedQuoteId);
            Assert.AreEqual(0, Open(e, "PROC.INQ.SELECT").Count);
            AgentTask manual = OpenOne(e, "PROC.INQ.SELECT-MANUAL");
            Assert.AreEqual(AgentStepKind.Input, manual.Kind);
            Assert.AreEqual(AgentTaskState.WaitingInput, manual.State);
            Assert.AreEqual("COMMERCIAL", manual.GateRoles);
            Assert.AreEqual("/procurement/inquiries/" + inquiry.Id, manual.Link);
            StringAssert.Contains(manual.Summary, "ایجنت انتخاب نکرد");
            StringAssert.Contains(manual.Summary, "supplier A");
            StringAssert.Contains(manual.Summary, "گرید D");

            // the buyer selects the other supplier with a reason: the service accepts it and the task closes
            ResponseDto<InquiryDto> chosen = await e.Host.As(Buyer, p => p.GetRequiredService<IProcurementService>().SelectQuoteAsync(new SelectQuoteDto
            {
                InquiryId = inquiry.Id, QuoteId = quoted.Data.Quotes.Single(q => q.ProviderId == seed.ProviderB).Id, Reason = "the cheaper supplier is not allowed",
            }, Ct));
            Assert.IsTrue(chosen.Success, chosen.Message);
            await Master(e);
            Assert.AreEqual(0, Open(e, "PROC.INQ.SELECT-MANUAL").Count);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.INQ.SELECT-MANUAL").State);
            AssertNoSeatInAnyDecisionColumn(e);
        }

        [TestMethod]
        public async Task A_Returned_Request_Gets_A_Revision_Draft_From_The_Agent_And_A_Person_Is_Asked_To_Correct_It()
        {
            Env e = await NewEnv("returned");
            SeedWorld(e, 500_000m);
            await Master(e);
            await Agent(e, "AG-WH");
            PurchaseRequest first = Requests(e).Single();
            Assert.AreEqual(PurchaseRequestStatus.Submitted, first.Status);

            Assert.IsFalse((await RequestAction(e, Planner, first.Id, "return")).Success, "a return needs its reason");
            ResponseDto<PurchaseRequestDto> returned = await RequestAction(e, Planner, first.Id, "return", "the quantity is more than the shop needs");
            Assert.IsTrue(returned.Success, returned.Message);
            Assert.AreEqual(PurchaseRequestStatus.Returned, RequestOf(e, first.Id).Status);

            await Master(e);
            Assert.AreEqual(0, Open(e, "PROC.PR.APPROVE").Count, "the request left the gate");
            AgentTask revise = OpenOne(e, "PROC.PR.REVISE.WH");
            Assert.AreEqual("AG-WH", revise.AgentCode);
            Assert.AreEqual(AgentTaskState.Queued, revise.State);
            StringAssert.Contains(revise.Summary, "the quantity is more than the shop needs");

            await Agent(e, "AG-WH");
            List<PurchaseRequest> all = Requests(e);
            Assert.AreEqual(2, all.Count);
            PurchaseRequest revision = all.Single(r => r.Id != first.Id);
            Assert.AreEqual(PurchaseRequestStatus.Draft, revision.Status);
            Assert.AreEqual(first.Id, revision.PreviousRequestId);
            Assert.AreEqual(UserOf(e, "AG-WH"), revision.CreatorUserId);
            Assert.AreEqual(revision.Id, RequestOf(e, first.Id).SupersededByRequestId);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.PR.REVISE.WH").State);

            // the agent does not submit a revision: a person corrects it (PROC.PR.FIX)
            Assert.AreEqual(PurchaseRequestStatus.Draft, RequestOf(e, revision.Id).Status);
            AgentTask fix = OpenOne(e, "PROC.PR.FIX.WH");
            Assert.AreEqual(AgentStepKind.Input, fix.Kind);
            Assert.AreEqual(AgentTaskState.WaitingInput, fix.State);
            Assert.AreEqual(revision.Id, fix.TargetId);
            Assert.AreEqual("/procurement/requests/" + revision.Id, fix.Link);
            StringAssert.Contains(fix.Summary, "the quantity is more than the shop needs");
            CollectionAssert.AreEquivalent(ProcurementRoles.Requesters.Split(','), fix.GateRoles.Split(','));
            foreach (string role in ProcurementRoles.Requesters.Split(',')) StringAssert.Contains(fix.GateRoles, role);
            Assert.AreEqual(0, Open(e, "PROC.PR.SUBMIT.WH").Count, "a revision is for a person");

            // the storekeeper fixes and submits; the revision now has another maker
            Assert.IsTrue((await RequestAction(e, Warehouse, revision.Id, "submit")).Success);
            Assert.AreEqual(1061, RequestOf(e, revision.Id).CreatorUserId);
            await Master(e);
            Assert.AreEqual(0, Open(e, "PROC.PR.FIX.WH").Count);
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.PR.FIX.WH").State);
            Assert.AreEqual(revision.Id, OpenOne(e, "PROC.PR.APPROVE").TargetId);
        }

        [TestMethod]
        public async Task With_The_Procurement_Agent_Off_The_Buyer_Opens_The_Inquiry_By_Hand_And_The_Fallback_Task_Closes()
        {
            Env e = await NewEnv("off");
            Seed seed = SeedWorld(e, 500_000m);
            int requestId = SeedApprovedRequest(e, seed);
            // the PMO switches the agent off (the same row as the admin page)
            e.Host.Write(u =>
            {
                AiSeat row = u.Set<AiSeat>().Single(s => s.Code == "AG-PROC");
                row.Enabled = false;
                u.SaveChanges();
            }, Pmo);

            await Master(e);
            AgentTask fallback = OpenOne(e, "PROC.INQ.OPEN");
            Assert.IsTrue(fallback.IsFallback);
            Assert.AreEqual(AgentTaskState.WaitingInput, fallback.State);
            Assert.AreEqual("COMMERCIAL", fallback.GateRoles);
            Assert.AreEqual("/procurement/requests/" + requestId, fallback.Link);
            Assert.AreEqual(0, Inquiries(e).Count, "the agent did nothing");
            var inbox = await e.Host.As(Buyer, p => p.GetRequiredService<IAgentInboxService>().GetInboxAsync(new AgentInboxQuery(), Ct));
            Assert.AreEqual(1, inbox.Data.Count);
            Assert.AreEqual("PROC.INQ.OPEN", inbox.Data[0].StepCode);

            // the buyer opens the inquiry with the existing screen
            ResponseDto<InquiryDto> opened = await e.Host.As(Buyer, p => p.GetRequiredService<IProcurementService>().SaveInquiryAsync(new SaveInquiryDto { PurchaseRequestId = requestId, InquiryDate = DateTime.Today }, Ct));
            Assert.IsTrue(opened.Success, opened.Message);
            await Master(e);
            Assert.AreEqual(0, Open(e, "PROC.INQ.OPEN").Count);
            AgentTask done = Closed(e, "PROC.INQ.OPEN");
            Assert.AreEqual(AgentText.DocumentChanged, done.ClosedReason);
            Assert.AreEqual(0, e.Host.Read(u => u.Set<AiSeatRun>().Count(r => r.Seat.Code == "AG-PROC")), "the switched-off agent did not run");
            Assert.AreEqual(1, Inquiries(e).Count);
            Assert.AreEqual(1, Open(e, "PROC.INQ.QUOTES").Count);
        }

        [TestMethod]
        public async Task A_Medium_Request_Needs_Planning_And_Then_Management_And_The_Gate_Asks_For_Exactly_The_Roles_Still_Missing()
        {
            Env e = await NewEnv("medium");
            SeedWorld(e, 12_000_000m);
            await Master(e);
            await Agent(e, "AG-WH");
            PurchaseRequest request = Requests(e).Single();
            Assert.AreEqual(PurchaseRequestStatus.Submitted, request.Status);
            Assert.AreEqual(120_000_000m, request.EstimatedAmount);
            await Master(e);
            Assert.AreEqual("PLANNING,PMO,CEO", OpenOne(e, "PROC.PR.APPROVE").GateRoles, "planning first, then management");

            // the warehouse holds none of the roles of the gate
            Assert.IsFalse((await RequestAction(e, Warehouse, request.Id, "approve-planning")).Success);
            Assert.IsTrue((await RequestAction(e, Planner, request.Id, "approve-planning")).Success);
            Assert.AreEqual(PurchaseRequestStatus.Submitted, RequestOf(e, request.Id).Status, "the medium purchase waits for management");
            await Master(e);
            Assert.AreEqual("CEO,PMO", OpenOne(e, "PROC.PR.APPROVE").GateRoles, "planning is done: only management is asked");
            Assert.IsFalse((await RequestAction(e, Planner, request.Id, "approve-management")).Success);
            Assert.IsTrue((await RequestAction(e, Ceo, request.Id, "approve-management")).Success);
            Assert.AreEqual(PurchaseRequestStatus.Approved, RequestOf(e, request.Id).Status);
            await Master(e);
            Assert.AreEqual(0, Open(e, "PROC.PR.APPROVE").Count);
            AssertNoSeatInAnyDecisionColumn(e);
        }

        [TestMethod]
        public async Task When_The_Step_Of_The_Agent_Is_Switched_Off_The_Older_Seat_Makes_The_Reorder_Draft_Again_And_Never_Both()
        {
            Env e = await NewEnv("supersede");
            SeedWorld(e, 500_000m);
            // the PMO switches the step off: the seat is no longer superseded
            var off = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().EnableStepAsync(new AgentStepEnableDto { Code = "WH.PR.REORDER", Enabled = false }, Ct));
            Assert.IsTrue(off.Success, off.Message);
            AiSeatJobResult seat = await RunSeat(e, "WH-REORDER");
            Assert.AreEqual(AiSeatRunStatus.Succeeded, seat.Status, seat.Error);
            StringAssert.Contains(seat.Summary, "drafts=1; lines=1");
            Assert.AreEqual(1, Requests(e).Count);
            // and the agent, whose step is off, makes none: its task is a task for people
            await Master(e);
            await Agent(e, "AG-WH");
            Assert.AreEqual(1, Requests(e).Count, "one maker at a time");
            Assert.AreEqual(0, Open(e, "WH.PR.REORDER").Count, "the item is on an open request now: no task for anybody");
        }

        [TestMethod]
        public async Task The_Agent_Prices_Its_Own_Draft_From_The_Latest_Order_Then_The_Accepted_Quote_And_A_Person_Gets_Only_What_Has_No_Price_At_All()
        {
            Env e = await NewEnv("autoprice");
            Seed seed = SeedWorld(e, null);   // no cost of the item: only orders and quotes can price it
            int item2 = 0, item3 = 0;
            e.Host.Write(u =>
            {
                InventoryItem i2 = AppHost.Add(u, new InventoryItem { Code = "I-200", Title = "bolt", Unit = "عدد", MinStock = 20m, ReorderQuantity = 20m });
                InventoryItem i3 = AppHost.Add(u, new InventoryItem { Code = "I-300", Title = "ghost part", Unit = "عدد", MinStock = 5m, ReorderQuantity = 5m });
                u.SaveChanges();
                item2 = i2.Id; item3 = i3.Id;
                // the plate was bought: a released order of 430,000 and an older one of 410,000 (the latest wins)
                var old = AppHost.Add(u, new PurchaseOrder { Number = "PO-OLD", Status = PurchaseOrderStatus.Closed, Kind = PurchaseKind.Goods, ProviderId = seed.ProviderA, OrderDate = DateTime.Today.AddDays(-90), TotalAmount = 1m });
                var recent = AppHost.Add(u, new PurchaseOrder { Number = "PO-NEW", Status = PurchaseOrderStatus.Released, Kind = PurchaseKind.Goods, ProviderId = seed.ProviderA, OrderDate = DateTime.Today.AddDays(-5), TotalAmount = 1m });
                // a draft order and a foreign-currency order do not count
                var draft = AppHost.Add(u, new PurchaseOrder { Number = "PO-DRAFT", Status = PurchaseOrderStatus.Draft, Kind = PurchaseKind.Goods, ProviderId = seed.ProviderA, OrderDate = DateTime.Today, TotalAmount = 1m });
                var foreign = AppHost.Add(u, new PurchaseOrder { Number = "PO-USD", Status = PurchaseOrderStatus.Released, Kind = PurchaseKind.Goods, ProviderId = seed.ProviderA, OrderDate = DateTime.Today, TotalAmount = 1m, Currency = "USD" });
                u.SaveChanges();
                AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = old.Id, RowNumber = 1, ItemId = seed.Item, Description = "plate", Unit = "عدد", Quantity = 1m, UnitPrice = 410_000m });
                AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = recent.Id, RowNumber = 1, ItemId = seed.Item, Description = "plate", Unit = "عدد", Quantity = 1m, UnitPrice = 430_000m });
                AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = draft.Id, RowNumber = 1, ItemId = item2, Description = "bolt", Unit = "عدد", Quantity = 1m, UnitPrice = 1m });
                AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = foreign.Id, RowNumber = 1, ItemId = item2, Description = "bolt", Unit = "عدد", Quantity = 1m, UnitPrice = 2m });
                // the bolt was only quoted: an accepted quote line of 12,000 and a rejected one of 9,000
                var request = AppHost.Add(u, new PurchaseRequest { Number = "PR-OLDQ", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Closed, RequestDate = DateTime.Today.AddDays(-30) });
                u.SaveChanges();
                var rl = AppHost.Add(u, new PurchaseRequestLine { PurchaseRequestId = request.Id, RowNumber = 1, ItemId = item2, Description = "bolt", Unit = "عدد", Quantity = 1m });
                var inq = AppHost.Add(u, new PurchaseInquiry { Number = "INQ-OLDQ", PurchaseRequestId = request.Id, Status = InquiryStatus.Approved, InquiryDate = DateTime.Today.AddDays(-20) });
                u.SaveChanges();
                var good = AppHost.Add(u, new SupplierQuote { InquiryId = inq.Id, ProviderId = seed.ProviderA, QuoteNumber = "Q1", QuoteDate = DateTime.Today.AddDays(-20), TechnicallyAccepted = true, TechnicalReviewedAt = DateTimeOffset.Now });
                var bad = AppHost.Add(u, new SupplierQuote { InquiryId = inq.Id, ProviderId = seed.ProviderB, QuoteNumber = "Q2", QuoteDate = DateTime.Today.AddDays(-10), TechnicallyAccepted = false, TechnicalReviewedAt = DateTimeOffset.Now });
                u.SaveChanges();
                AppHost.Add(u, new SupplierQuoteLine { QuoteId = good.Id, RequestLineId = rl.Id, Quantity = 1m, UnitPrice = 12_000m });
                AppHost.Add(u, new SupplierQuoteLine { QuoteId = bad.Id, RequestLineId = rl.Id, Quantity = 1m, UnitPrice = 9_000m });
                u.SaveChanges();
            }, Seeder);

            await Master(e);
            await Agent(e, "AG-WH");
            PurchaseRequest draftRequest = Requests(e).Single(r => r.Source == PurchaseRequestSource.ReorderPoint);
            Dictionary<int, decimal?> priced = draftRequest.Lines.ToDictionary(l => l.ItemId.Value, l => l.EstimatedUnitPrice);
            Assert.AreEqual(430_000m, priced[seed.Item], "the latest live order wins over the older one");
            Assert.AreEqual(12_000m, priced[item2], "no order of the bolt counts (draft, foreign currency): the accepted quote does");
            Assert.IsNull(priced[item3], "the ghost part has no price anywhere");
            Assert.AreEqual(PurchaseRequestStatus.Draft, draftRequest.Status, "one line is still without a price");
            // the person is asked for the ghost part only
            await Master(e);
            AgentTask input = OpenOne(e, "PROC.PR.PRICE.WH");
            Assert.AreEqual(AgentTaskState.WaitingInput, input.State);
            StringAssert.Contains(input.Summary, "ghost part");
            Assert.IsFalse(input.Summary.Contains("plate") || input.Summary.Contains("bolt"), input.Summary);
            Assert.AreEqual(0, Open(e, "PROC.PR.PRICE.AUTO.WH").Count);

            // the planner prices the ghost part by hand and submits; the earlier prices stay
            ResponseDto<PurchaseRequestDto> saved = await e.Host.As(Planner, p => p.GetRequiredService<IProcurementService>().SaveRequestAsync(new SavePurchaseRequestDto
            {
                Id = draftRequest.Id, Source = PurchaseRequestSource.ReorderPoint, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today,
                Lines = draftRequest.Lines.Select(l => new SavePurchaseRequestLineDto { Id = l.Id, ItemId = l.ItemId, Unit = l.Unit, Quantity = l.Quantity, EstimatedUnitPrice = l.EstimatedUnitPrice ?? 77_000m }).ToList(),
            }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(draftRequest.Lines.Single(l => l.ItemId == seed.Item).Quantity * 430_000m + draftRequest.Lines.Single(l => l.ItemId == item2).Quantity * 12_000m + draftRequest.Lines.Single(l => l.ItemId == item3).Quantity * 77_000m, RequestOf(e, draftRequest.Id).EstimatedAmount);
        }

        [TestMethod]
        public async Task A_Draft_Made_By_Another_Hand_Is_Priced_By_The_Agent_That_Owns_It_And_Then_Submitted_In_The_Same_Run()
        {
            Env e = await NewEnv("autoprice2");
            Seed seed = SeedWorld(e, null);
            // the draft of AG-PLAN (a BOM draft) with an unpriced line of the plate; the plate has a released order
            int planId = UserOf(e, "AG-PLAN");
            int requestId = 0;
            e.Host.Write(u =>
            {
                var order = AppHost.Add(u, new PurchaseOrder { Number = "PO-1", Status = PurchaseOrderStatus.Released, Kind = PurchaseKind.Goods, ProviderId = seed.ProviderA, OrderDate = DateTime.Today.AddDays(-3), TotalAmount = 1m });
                u.SaveChanges();
                AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = order.Id, RowNumber = 1, ItemId = seed.Item, Description = "plate", Unit = "عدد", Quantity = 1m, UnitPrice = 600_000m });
                var r = AppHost.Add(u, new PurchaseRequest { Number = "PR-BOM", Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Draft, RequestDate = DateTime.Today, CreatorUserId = planId });
                u.SaveChanges();
                AppHost.Add(u, new PurchaseRequestLine { PurchaseRequestId = r.Id, RowNumber = 1, ItemId = seed.Item, Description = "plate", Unit = "عدد", Quantity = 5m });
                u.SaveChanges();
                requestId = r.Id;
            }, Seeder);
            await Master(e);
            Assert.AreEqual(1, Open(e, "PROC.PR.PRICE.AUTO.PLAN").Count);
            Assert.AreEqual(0, Open(e, "PROC.PR.SUBMIT.PLAN").Count, "not submitted before it is priced");
            Assert.AreEqual(0, Open(e, "PROC.PR.PRICE.PLAN").Count, "and no person is asked");
            await Agent(e, "AG-PLAN");
            PurchaseRequest done = RequestOf(e, requestId);
            Assert.AreEqual(3_000_000m, done.EstimatedAmount, string.Join(" | ", Tasks(e).Select(t => t.StepCode + ":" + t.State + ":" + t.LastError)));
            Assert.AreEqual(600_000m, done.Lines.Single().EstimatedUnitPrice);
            Assert.AreEqual(PurchaseRequestStatus.Submitted, done.Status);
            Assert.AreEqual(planId, done.CreatorUserId, "the maker is still the agent");
            Assert.AreEqual(AgentTaskState.Done, Closed(e, "PROC.PR.PRICE.AUTO.PLAN").State);
            StringAssert.Contains(Closed(e, "PROC.PR.PRICE.AUTO.PLAN").Summary ?? string.Empty, "PO-1");
        }

        [TestMethod]
        public async Task A_Large_Order_Gets_Its_Requirements_From_The_Agent_And_Only_The_Signed_Contract_Is_Left_For_The_Buyer()
        {
            Env e = await NewEnv("contract");
            Seed seed = SeedWorld(e, 500_000m);
            int orderId = 0;
            e.Host.Write(u =>
            {
                var order = AppHost.Add(u, new PurchaseOrder { Number = "PO-BIG", Status = PurchaseOrderStatus.Draft, Kind = PurchaseKind.Goods, ProviderId = seed.ProviderA, OrderDate = DateTime.Today, TotalAmount = 200_000_000m, CreatorUserId = UserOf(e, "AG-PROC") });
                u.SaveChanges();
                orderId = order.Id;
            }, Seeder);
            await Master(e);
            Assert.AreEqual(1, Open(e, "PROC.PO.REQUIREMENTS").Count);
            AgentTask contract = OpenOne(e, "PROC.PO.COMPLETE");
            Assert.AreEqual(AgentStepKind.Input, contract.Kind);
            Assert.AreEqual("COMMERCIAL", contract.GateRoles);
            StringAssert.Contains(contract.Summary, "قرارداد");
            Assert.IsFalse(contract.Summary.Contains("الزامات فنی"), "the requirements are not asked of a person");
        }

        // ------------------------------------------------------------------------------------------------ the board and the drills

        [TestMethod]
        public async Task The_Board_Row_Of_The_Contract_Shows_Station_Six_Waiting_At_A_Gate_With_The_Role_And_The_Age()
        {
            Env e = await NewEnv("board");
            Seed seed = SeedWorld(e, 500_000m);
            e.Host.Write(u => { AppHost.Add(u, new ContractEntity { Code = "C-9", Title = "قرارداد آزمایشی", InternalContractNo = "INT-9" }); u.SaveChanges(); }, Seeder);
            int contractId = e.Host.Read(u => u.Set<ContractEntity>().Single().Id);
            int requestId = SeedApprovedRequest(e, seed, contractId);
            await Master(e);
            await Agent(e, "AG-PROC");
            int inquiryId = Inquiries(e).Single().Id;
            Assert.IsTrue((await SaveQuotes(e, Buyer, inquiryId, requestId, (seed.ProviderA, 480_000m, 30))).Success);
            await Master(e);
            AgentTask tech = OpenOne(e, "PROC.INQ.TECH");
            Assert.AreEqual(contractId, tech.ContractId, "the task carries the contract of the request");
            e.Host.Write(u => { u.Set<AgentTask>().Single(t => t.Id == tech.Id).CreateDate = DateTimeOffset.Now.AddHours(-30); u.SaveChanges(); }, Pmo);

            var board = await e.Host.As(Pmo, p => p.GetRequiredService<IAgentBoardService>().GetBoardAsync(contractId, Ct));
            Assert.IsTrue(board.Success, board.Message);
            AgentBoardRowDto row = board.Data.Rows.Single();
            Assert.AreEqual(contractId, row.ContractId);
            StringAssert.Contains(row.Label, "INT-9");
            AgentBoardCellDto six = row.Cells[5];
            Assert.AreEqual(6, six.Station);
            Assert.AreEqual(AgentCellStatus.WaitingGate, six.Status);
            Assert.AreEqual("ENGINEERING,QC", six.WaitingOn);
            Assert.AreEqual(1, six.OpenCount);
            Assert.AreEqual(30, six.OldestHours);
            Assert.AreEqual(AgentCellStatus.NotStarted, row.Cells[0].Status);
            Assert.AreEqual(AgentCellStatus.NotStarted, row.Cells[6].Status);

            // the list of the tasks of the contract, and the row that holds the tasks of no contract
            var list = await e.Host.As(Ceo, p => p.GetRequiredService<IAgentBoardService>().GetTasksAsync(new AgentTaskQuery { ContractId = contractId, Station = 6, PageSize = 20 }, Ct));
            Assert.IsTrue(list.Data.Any(t => t.StepCode == "PROC.INQ.TECH"));
            var company = await e.Host.As(Ceo, p => p.GetRequiredService<IAgentBoardService>().GetTasksAsync(new AgentTaskQuery { NoContract = true, PageSize = 20 }, Ct));
            Assert.IsTrue(company.Data.All(t => t.ContractId == null));
        }

        [TestMethod]
        public void A_Step_Of_The_Procurement_Station_Never_Calls_An_Approval()
        {
            string dir = null;
            for (string d = AppContext.BaseDirectory; d != null && dir == null; d = System.IO.Directory.GetParent(d)?.FullName)
            {
                string candidate = System.IO.Path.Combine(d, "Services", "NasbNiro", "Agents", "Steps");
                if (System.IO.Directory.Exists(candidate)) dir = candidate;
            }
            Assert.IsNotNull(dir);
            foreach (string file in System.IO.Directory.GetFiles(dir, "*.cs"))
            {
                string text = System.IO.File.ReadAllText(file);
                foreach (string banned in new[] { "\"approve", "SignAsync", "ApproveAsync", "DecideAsync", "SaveChanges", ".Remove(" })
                    Assert.IsFalse(text.Contains(banned), System.IO.Path.GetFileName(file) + " contains " + banned);
            }
        }
    }
}
