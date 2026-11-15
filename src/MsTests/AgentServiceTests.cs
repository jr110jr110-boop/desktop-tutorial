using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.AfterSales;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using ViewModels.NasbNiro.AgentDtos;
using ViewModels.NasbNiro.ServiceDtos;

namespace MsTests
{
    /// <summary>
    /// Agent layer, station 12 (after-sales): the agent drafts the order of an open request, people start it, do the visit and record it, a second service person decides the warranty, the CEO decides goodwill,
    /// a person closes the order and the agent retries the draft invoice of a closed billable order. A seat never confirms, decides or closes.
    /// </summary>
    [TestClass]
    public class AgentServiceTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Service1 = AppHost.Principal(1901, "service.one", "SERVICE");
        private static readonly ClaimsPrincipal Service2 = AppHost.Principal(1902, "service.two", "SERVICE");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(1903, "ceo.one", "CEO");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
        }

        private static async Task<Env> NewEnv(string name)
        {
            AppHost host = new AppHost("AgentSv_" + name + "_" + Guid.NewGuid().ToString("N"));
            return new Env { Host = host, Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)) };
        }

        private static Task<AiSeatJobResult> RunSeat(Env e, string code) =>
            AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[code], code,
                sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == code).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));

        private static async Task Run(Env e, string code) { AiSeatJobResult r = await RunSeat(e, code); Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error); }

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 4; i++) { await Run(e, "AG-MASTER"); await Run(e, "AG-SERVICE"); }
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

        private sealed class Ids { public int Contract, Customer, Unit, Request, Employee; public Guid Doc; }

        /// <summary>A customer, a site, a contract, an installed unit in warranty (not yet confirmed), an employee, a signed report document and a breakdown request of the unit.</summary>
        private static Ids Seed(Env e, bool withRequest, bool confirmed = false)
        {
            var ids = new Ids();
            e.Host.Write(u =>
            {
                FnCustomer c = AppHost.Add(u, new FnCustomer { Name = "شرکت برق", IsActive = true });
                ContractEntity k = AppHost.Add(u, new ContractEntity { Code = "C-SV" + Guid.NewGuid().ToString("N").Substring(0, 4), Title = "توربین", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-200), Price = 1_000_000_000m });
                u.SaveChanges();
                SlSite site = AppHost.Add(u, new SlSite { CustomerId = c.Id, Title = "نیروگاه" });
                FnEmployee emp = new FnEmployee { PersonnelCode = "E" + Guid.NewGuid().ToString("N").Substring(0, 5), FirstName = "مهندس", LastName = "خدمات", NationalCode = "0499370899", HireDate = new DateTime(2020, 1, 1), IsActive = true, CreatedAt = DateTimeOffset.Now };
                u.Set<FnEmployee>().Add(emp);
                Document d = new Document("UploadedDocuments/" + Guid.NewGuid().ToString("N") + ".pdf", ".pdf").SetUploader(1001).SetName("report").SetCode("T").SetDescription("d");
                AppHost.Add(u, d);
                u.SaveChanges();
                SvInstalledUnit unit = AppHost.Add(u, new SvInstalledUnit
                {
                    Tag = "TG-1", Title = "توربین ۱", CustomerId = c.Id, SiteId = site.Id, ContractId = k.Id, HandoverDate = DateTime.Today.AddDays(-100), WarrantyStart = DateTime.Today.AddDays(-100), WarrantyEnd = DateTime.Today.AddDays(260),
                    WarrantySource = SvWarrantySource.Manual, IsConfirmed = confirmed, CreateDate = DateTimeOffset.Now,
                });
                u.SaveChanges();
                ids.Contract = k.Id; ids.Customer = c.Id; ids.Unit = unit.Id; ids.Employee = emp.Id; ids.Doc = d.Id;
                if (withRequest)
                {
                    SvServiceRequest r = AppHost.Add(u, new SvServiceRequest
                    {
                        Number = "SVR-1", CustomerId = c.Id, UnitId = unit.Id, Channel = "phone", ReceivedAt = DateTime.Today, Description = "لرزش بیش از حد", Type = SvRequestType.Breakdown, Priority = SvPriority.High, Status = SvRequestStatus.Open, CreateDate = DateTimeOffset.Now,
                    });
                    u.SaveChanges();
                    ids.Request = r.Id;
                }
            }, Seeder);
            return ids;
        }

        // ------------------------------------------------------------------------------------------------ the visit

        [TestMethod]
        public async Task The_Agent_Drafts_The_Order_People_Start_And_Record_It_A_Second_Person_Decides_The_Warranty_And_The_CEO_Decides_Goodwill()
        {
            Env e = await NewEnv("visit");
            Ids ids = Seed(e, true);
            int seat = e.Seats["AG-SERVICE"].UserId;

            // the unit drafted from the delivered shipment waits for a second service person; the agent drafts the order of the open request
            await Settle(e);
            AgentTask confirm = OpenOne(e, "SV.UNIT.CONFIRM");
            Assert.AreEqual(AgentStepKind.HumanGate, confirm.Kind);
            AssertRoles("SERVICE", confirm);
            Assert.AreEqual("/service/units/" + ids.Unit, confirm.Link);
            Done(e, "SV.ORDER.DRAFT");
            Assert.AreEqual(0, Open(e, "SV.ORDER.DRAFT").Count, Dump(e));
            SvServiceOrder order = e.Host.Read(u => u.Set<SvServiceOrder>().AsNoTracking().Single(o => o.RequestId == ids.Request));
            Assert.AreEqual(seat, order.CreatorUserId);
            Assert.AreEqual(SvOrderStatus.Planned, order.Status);
            Assert.AreEqual(ids.Unit, order.UnitId);
            Assert.AreEqual(ids.Contract, order.ContractId);
            Assert.AreEqual(SvWarrantyDecision.None, order.Decision);

            // the engineers and the start are physical
            AgentTask start = OpenOne(e, "SV.ORDER.START");
            Assert.AreEqual(AgentStepKind.Physical, start.Kind);
            AssertRoles("SERVICE", start);
            Assert.AreEqual("/service/orders/" + order.Id, start.Link);
            StringAssert.Contains(start.Summary, "مهندس ندارد");
            var saved = await As(e, Service1, p => p.GetRequiredService<ISvOrderService>().SaveOrderAsync(new SvSaveOrderDto
            {
                Id = order.Id, RequestId = ids.Request, UnitId = ids.Unit, Kind = order.Kind, Title = order.Title, PlannedStart = order.PlannedStart, EngineerEmployeeIds = new List<int> { ids.Employee },
            }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            var started = await As(e, Service1, p => p.GetRequiredService<ISvOrderService>().OrderActionAsync(new SvOrderActionDto { Id = order.Id, Action = "start" }, Ct));
            Assert.IsTrue(started.Success, started.Message);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SV.ORDER.START").Count, Dump(e));
            AgentTask field = OpenOne(e, "SV.ORDER.FIELDWORK");
            Assert.AreEqual(AgentStepKind.Physical, field.Kind);
            StringAssert.Contains(field.Summary, "ساعت");
            Assert.AreEqual(0, Open(e, "SV.WARRANTY.DECIDE").Count, "no hours, no decision yet");

            // the visit: hours, a measurement and the signed report
            Assert.IsTrue((await As(e, Service1, p => p.GetRequiredService<ISvOrderService>().SaveTimeEntryAsync(new SvSaveTimeEntryDto { OrderId = order.Id, EmployeeId = ids.Employee, Hours = 6, Kind = SvTimeKind.Work }, Ct))).Success);
            Assert.IsTrue((await As(e, Service1, p => p.GetRequiredService<ISvOrderService>().SaveMeasurementAsync(new SvSaveMeasurementDto { OrderId = order.Id, Name = "لرزش", Value = 7.1m, Unit = "mm/s" }, Ct))).Success);
            var report = await As(e, Service1, p => p.GetRequiredService<ISvOrderService>().SaveOrderAsync(new SvSaveOrderDto
            {
                Id = order.Id, RequestId = ids.Request, UnitId = ids.Unit, Kind = order.Kind, Title = order.Title, PlannedStart = order.PlannedStart, ReportDocumentId = ids.Doc,
            }, Ct));
            Assert.IsTrue(report.Success, report.Message);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SV.ORDER.FIELDWORK").Count, Dump(e));

            // the warranty decision is a second person's: neither the seat that made the order nor the person who edited it can take it
            AgentTask warranty = OpenOne(e, "SV.WARRANTY.DECIDE");
            Assert.AreEqual(AgentStepKind.HumanGate, warranty.Kind);
            AssertRoles("SERVICE", warranty);
            StringAssert.Contains(warranty.Summary, "در بازهٔ گارانتی");
            StringAssert.Contains(warranty.Summary, "هنوز تایید نشده");
            Assert.AreEqual(0, Open(e, "SV.ORDER.CLOSE").Count);
            var same = await As(e, Service1, p => p.GetRequiredService<ISvOrderService>().DecideAsync(new SvDecisionDto { OrderId = order.Id, Decision = SvWarrantyDecision.Billable }, Ct));
            Assert.IsFalse(same.Success, "the editor decided");

            // the second person confirms the unit (nobody edited it), and the warranty can be granted; this time service asks goodwill instead
            Assert.IsTrue((await As(e, Service2, p => p.GetRequiredService<ISvUnitService>().UnitActionAsync(new SvUnitActionDto { Id = ids.Unit, Action = "confirm" }, Ct))).Success);
            Assert.IsTrue((await As(e, Service1, p => p.GetRequiredService<ISvOrderService>().RequestGoodwillAsync(new SvGoodwillRequestDto { OrderId = order.Id, Reason = "مشتری قدیمی؛ خرابی بعد از دوره" }, Ct))).Success);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SV.UNIT.CONFIRM").Count, Dump(e));
            Assert.AreEqual(0, Open(e, "SV.WARRANTY.DECIDE").Count, Dump(e));
            AgentTask goodwill = OpenOne(e, "SV.GOODWILL.DECIDE");
            Assert.AreEqual(AgentStepKind.HumanGate, goodwill.Kind);
            AssertRoles("CEO", goodwill);
            StringAssert.Contains(goodwill.Summary, "بعد از دوره");
            Assert.AreEqual(0, Open(e, "SV.ORDER.CLOSE").Count);
            var decided = await As(e, Ceo, p => p.GetRequiredService<ISvOrderService>().DecideAsync(new SvDecisionDto { OrderId = order.Id, Decision = SvWarrantyDecision.Goodwill, Note = "تایید" }, Ct));
            Assert.IsTrue(decided.Success, decided.Message);
            await Settle(e);
            Assert.AreEqual(0, Open(e, "SV.GOODWILL.DECIDE").Count, Dump(e));

            // a person closes it
            AgentTask close = OpenOne(e, "SV.ORDER.CLOSE");
            Assert.AreEqual(AgentStepKind.HumanGate, close.Kind);
            AssertRoles("SERVICE", close);
            Assert.AreEqual("/service/orders/" + order.Id, close.Link);

            SvServiceOrder fin = e.Host.Read(u => u.Set<SvServiceOrder>().AsNoTracking().Single(o => o.Id == order.Id));
            Assert.AreEqual(1903, fin.DecidedByUserId);
            Assert.IsFalse(IsSeat(e, fin.DecidedByUserId) || IsSeat(e, fin.ClosedByUserId) || IsSeat(e, fin.GoodwillRequestedByUserId), "a seat decided, closed or asked goodwill");
            Assert.IsFalse(IsSeat(e, e.Host.Read(u => u.Set<SvInstalledUnit>().AsNoTracking().Single(x => x.Id == ids.Unit).ConfirmedByUserId)), "a seat confirmed the unit");
            Assert.AreEqual(1902, e.Host.Read(u => u.Set<SvInstalledUnit>().AsNoTracking().Single(x => x.Id == ids.Unit).ConfirmedByUserId));
        }

        // ------------------------------------------------------------------------------------------------ the invoice, and the board

        [TestMethod]
        public async Task The_Agent_Drafts_The_Invoice_Of_A_Closed_Billable_Order_And_The_Board_Shows_The_Station_Done()
        {
            Env e = await NewEnv("invoice");
            Ids ids = Seed(e, false, confirmed: true);
            int orderId = 0;
            e.Host.Write(u =>
            {
                u.Set<FnTaxRate>().Add(new FnTaxRate { Kind = FnTaxKind.Vat, Rate = 10m, EffectiveFrom = new DateTime(2000, 1, 1), Source = "test fixture", Verify = true });
                SvServiceOrder o = AppHost.Add(u, new SvServiceOrder
                {
                    Number = "SVO-1", UnitId = ids.Unit, CustomerId = ids.Customer, ContractId = ids.Contract, Title = "رفع عیب", Status = SvOrderStatus.Closed, Decision = SvWarrantyDecision.Billable, DecidedByUserId = 1902,
                    ClosedAt = DateTimeOffset.Now, ClosedByUserId = 1901, BillableLabourRateRial = 2_000_000m, CreateDate = DateTimeOffset.Now, CreatorUserId = 1901,
                });
                u.SaveChanges();
                u.Set<SvTimeEntry>().Add(new SvTimeEntry { OrderId = o.Id, EmployeeId = ids.Employee, WorkDate = DateTime.Today, Hours = 5, Kind = SvTimeKind.Work, CreatedByUserId = 1901 });
                u.SaveChanges();
                orderId = o.Id;
            }, Seeder);

            await Settle(e);
            Done(e, "SV.INVOICE.DRAFT");
            Assert.AreEqual(0, Open(e, "SV.INVOICE.DRAFT").Count, Dump(e));
            SvServiceOrder row = e.Host.Read(u => u.Set<SvServiceOrder>().AsNoTracking().Single(o => o.Id == orderId));
            Assert.IsNotNull(row.InvoiceId, Dump(e));
            FnSalesInvoice inv = e.Host.Read(u => u.Set<FnSalesInvoice>().AsNoTracking().Include(i => i.Lines).Single(i => i.Id == row.InvoiceId.Value));
            Assert.AreEqual(FnDocStatus.Draft, inv.Status);
            Assert.AreEqual(e.Seats["AG-SERVICE"].UserId, inv.CreatedByUserId);
            Assert.AreEqual(10_000_000m, inv.NetAmount);
            Assert.IsFalse(IsSeat(e, row.DecidedByUserId) || IsSeat(e, row.ClosedByUserId));

            ResponseDto<AgentBoardDto> board = await e.Host.As(Seeder, s => s.GetRequiredService<IAgentBoardService>().GetBoardAsync(ids.Contract, Ct));
            Assert.AreEqual(AgentCellStatus.Done, board.Data.Rows.Single(r => r.ContractId == ids.Contract).Cells.Single(c => c.Station == 12).Status, "a closed order and nothing open");
        }

        // ------------------------------------------------------------------------------------------------ the catalogue

        [TestMethod]
        public void The_Station_Has_The_Steps_Of_The_Spec_And_Gates_Use_The_Roles_Of_The_Services()
        {
            AppHost host = new AppHost("AgentSvCat_" + Guid.NewGuid().ToString("N"));
            List<IAgentStep> steps = host.AsSystem(p => Task.FromResult(p.GetServices<IAgentStep>().Where(s => s.Station == 12).ToList())).GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[]
            {
                "SV.UNIT.CONFIRM", "SV.ORDER.DRAFT", "SV.ORDER.START", "SV.ORDER.FIELDWORK", "SV.GOODWILL.DECIDE", "SV.WARRANTY.DECIDE", "SV.ORDER.CLOSE", "SV.INVOICE.DRAFT",
            }, steps.Select(s => s.Code).ToList());
            Assert.IsTrue(steps.All(s => s.AgentCode == "AG-SERVICE"));
            CollectionAssert.AreEquivalent(new[] { "SV.ORDER.DRAFT", "SV.INVOICE.DRAFT" }, steps.Where(s => s.Kind == AgentStepKind.Agent).Select(s => s.Code).ToList());
            CollectionAssert.AreEquivalent(new[] { "SV.ORDER.START", "SV.ORDER.FIELDWORK" }, steps.Where(s => s.Kind == AgentStepKind.Physical).Select(s => s.Code).ToList());
            Assert.AreEqual(SvRoles.GoodwillDeciders, steps.Single(s => s.Code == "SV.GOODWILL.DECIDE").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
            Assert.AreEqual(SvRoles.Writers, steps.Single(s => s.Code == "SV.ORDER.CLOSE").GateInfo(new AgentWorkItem { TargetId = 1 }).Roles);
        }
    }
}
