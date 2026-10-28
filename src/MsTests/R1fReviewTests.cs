using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using IocConfig;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.Identity.Settings;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.OfficeDtos;
using IdentityRole = Entities.Identity.Role;
using IdentityUser = Entities.Identity.User;
using IdentityUserRole = Entities.Identity.UserRole;

namespace MsTests
{
    /// <summary>A signed-in user for a service under test: the user id and the roles (a requested role list is a comma separated "any of").</summary>
    internal sealed class TestUser : IInventoryUserContext
    {
        private readonly HashSet<string> _roles;

        public TestUser(int userId, params string[] roles)
        {
            UserId = userId;
            _roles = new HashSet<string>(roles ?? new string[0], StringComparer.OrdinalIgnoreCase);
        }

        public int UserId { get; }

        public bool IsInRole(string role) => !string.IsNullOrWhiteSpace(role) && role.Split(',').Any(r => _roles.Contains(r.Trim()));
    }

    /// <summary>The in-memory database of the service tests: the real context with its SaveChanges hooks (audit trail included), one database per test instance.</summary>
    internal static class R1fDb
    {
        public static ServiceProvider Build()
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddScoped<IWebHostEnvironment, TestWebHostEnvironment>();
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", reloadOnChange: false, optional: false)
                .AddInMemoryCollection(new[] { new KeyValuePair<string, string>("UseInMemoryDatabase", "true") })
                .Build();
            services.AddSingleton<IConfigurationRoot>(provider => configuration);
            services.Configure<SiteSettings>(options => configuration.Bind(options));
            services.AddScoped<IUnitOfWork, ApplicationDbContext>();
            services.AddCustomIdentityServices();
            var siteSettings = services.GetSiteSettings();
            siteSettings.ActiveDatabase = ActiveDatabase.InMemoryDatabase;
            siteSettings.ConnectionStrings.LocalDb ??= new Localdb { InitialCatalog = "IRNNCO_R1fTests" };
            services.AddRequiredEfInternalServices(siteSettings);
            services.AddDbContextPool<ApplicationDbContext>((serviceProvider, optionsBuilder) =>
            {
                optionsBuilder.SetDbContextOptions(siteSettings);
                optionsBuilder.UseInternalServiceProvider(serviceProvider);
            });
            return services.BuildServiceProvider();
        }

        public static async Task RunAsync(IServiceProvider provider, Func<IUnitOfWork, Task> body)
        {
            using IServiceScope scope = provider.CreateScope();
            await body(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        }

        public static FnEmployee Employee(string code, int? userId = null, int? workshopId = null) => new FnEmployee
        {
            PersonnelCode = code, FirstName = "f" + code, LastName = "l" + code, NationalCode = "0499370899", HireDate = new DateTime(2020, 1, 1), DailyWage = 1m, IsActive = true, UserId = userId, WorkshopId = workshopId,
        };
    }

    /// <summary>
    /// Release R1f, the review fixes: the pure rules behind them (the rule of three per account role and cost centre, the typed-sheet refusal, the device address list, the gap-day span, the merged ledger lines,
    /// the policy hash) and the small database rules (the second person of a request, the cached wage, the one-time upgrade).
    /// </summary>
    [TestClass]
    public class R1fPayrollHelperTests
    {
        private readonly ServiceProvider _provider = R1fDb.Build();

        private const string Direct = "PayrollExpense.Direct";
        private const string Insurance = "EmployerInsurance";

        private static PayrollPooling.DebitLine Find(List<PayrollPooling.DebitLine> lines, string role, int? workOrder, string centre) =>
            lines.Single(l => l.Role == role && l.WorkOrderId == workOrder && l.CostCenter == centre);

        // ---------------------------------------------------------------------------------------- M4: the rule of three per role and cost centre

        [TestMethod]
        public void A_Cost_Centre_With_Fewer_Than_Three_Employees_In_A_Role_Moves_To_The_Company_Line_Of_That_Role()
        {
            var lines = new List<PayrollPooling.DebitLine>();
            PayrollPooling.Add(lines, 1, Direct, 7, "CC-A", 1000m);
            PayrollPooling.Add(lines, 2, Direct, 7, "CC-A", 2000m);
            PayrollPooling.Add(lines, 3, Direct, 7, "CC-B", 400m);
            PayrollPooling.Add(lines, 4, Direct, 7, "CC-B", 500m);
            PayrollPooling.Add(lines, 5, Direct, 7, "CC-B", 600m);
            decimal before = lines.Sum(l => l.Amount);

            var pooled = PayrollPooling.PoolSmallCentres(lines);

            Assert.AreEqual(1, pooled.Count);
            Assert.AreEqual(Direct, pooled[0].Role);
            Assert.AreEqual("CC-A", pooled[0].CostCenter);
            Assert.AreEqual(2, lines.Count, "centre A is gone, the company line is new, centre B stays");
            PayrollPooling.DebitLine company = Find(lines, Direct, 7, null);
            Assert.AreEqual(3000m, company.Amount);
            Assert.IsTrue(company.Pooled);
            Assert.AreEqual(2, company.Employees.Count);
            PayrollPooling.DebitLine b = Find(lines, Direct, 7, "CC-B");
            Assert.AreEqual(1500m, b.Amount);
            Assert.IsFalse(b.Pooled);
            Assert.AreEqual(before, lines.Sum(l => l.Amount), "the totals per account do not change, only the cost-centre dimension of the small group");
        }

        [TestMethod]
        public void One_Employee_In_A_Centre_Never_Shows_His_Pay_On_A_Cost_Centre_Line()
        {
            var lines = new List<PayrollPooling.DebitLine>();
            PayrollPooling.Add(lines, 9, Direct, null, "CC-ALONE", 7777m);
            PayrollPooling.Add(lines, 9, Insurance, null, "CC-ALONE", 777m);
            PayrollPooling.PoolSmallCentres(lines);
            Assert.IsTrue(lines.All(l => l.CostCenter == null), "no cost-centre line is left for a centre with one employee");
            Assert.AreEqual(2, lines.Count, "one company line per role");
            Assert.AreEqual(7777m, Find(lines, Direct, null, null).Amount);
            Assert.AreEqual(777m, Find(lines, Insurance, null, null).Amount);
        }

        [TestMethod]
        public void The_Rule_Counts_Distinct_Employees_Across_The_Work_Orders_Of_One_Role_And_Centre()
        {
            var lines = new List<PayrollPooling.DebitLine>();
            // one employee on two work orders and a second one without a work order are still two people in the centre
            PayrollPooling.Add(lines, 1, Direct, 7, "CC-A", 1000m);
            PayrollPooling.Add(lines, 1, Direct, 8, "CC-A", 2000m);
            PayrollPooling.Add(lines, 2, Direct, null, "CC-A", 300m);
            var pooled = PayrollPooling.PoolSmallCentres(lines);
            Assert.AreEqual(1, pooled.Count);
            Assert.IsTrue(lines.All(l => l.CostCenter == null));
            Assert.AreEqual(3, lines.Count, "the work orders stay apart on the company level");
            Assert.AreEqual(1000m, Find(lines, Direct, 7, null).Amount);
            Assert.AreEqual(2000m, Find(lines, Direct, 8, null).Amount);
            Assert.AreEqual(300m, Find(lines, Direct, null, null).Amount);

            // the same three people: nothing is pooled
            var enough = new List<PayrollPooling.DebitLine>();
            PayrollPooling.Add(enough, 1, Direct, 7, "CC-A", 1000m);
            PayrollPooling.Add(enough, 2, Direct, 8, "CC-A", 2000m);
            PayrollPooling.Add(enough, 3, Direct, null, "CC-A", 300m);
            Assert.AreEqual(0, PayrollPooling.PoolSmallCentres(enough).Count);
            Assert.AreEqual(3, enough.Count);
            Assert.IsTrue(enough.All(l => l.CostCenter == "CC-A" && !l.Pooled));
        }

        [TestMethod]
        public void The_Rule_Is_Applied_Per_Account_Role_So_A_Role_With_Two_People_Is_Pooled_While_Another_Role_Of_The_Same_Centre_Stays()
        {
            var lines = new List<PayrollPooling.DebitLine>();
            foreach (int employee in new[] { 1, 2, 3 }) PayrollPooling.Add(lines, employee, Direct, null, "CC-A", 100m * employee);
            foreach (int employee in new[] { 1, 2 }) PayrollPooling.Add(lines, employee, Insurance, null, "CC-A", 10m * employee);
            var pooled = PayrollPooling.PoolSmallCentres(lines);
            Assert.AreEqual(1, pooled.Count);
            Assert.AreEqual(Insurance, pooled[0].Role);
            Assert.AreEqual(600m, Find(lines, Direct, null, "CC-A").Amount, "three people in the direct pay: the centre line stays");
            Assert.AreEqual(30m, Find(lines, Insurance, null, null).Amount, "two people in the insurance: only the company line shows it");
            Assert.AreEqual(630m, lines.Sum(l => l.Amount));
        }

        [TestMethod]
        public void A_Pooled_Amount_Joins_The_Company_Line_That_Already_Exists_And_A_Line_Without_A_Centre_Is_Never_Pooled()
        {
            var lines = new List<PayrollPooling.DebitLine>();
            PayrollPooling.Add(lines, 1, Direct, 7, null, 400m);       // the company level already holds an amount of this role and work order
            PayrollPooling.Add(lines, 2, Direct, 7, "CC-A", 50m);
            PayrollPooling.Add(lines, 3, Direct, 7, "CC-B", 60m);
            PayrollPooling.Add(lines, 4, Direct, 7, "CC-B", 0m);       // a zero amount is not a line
            PayrollPooling.PoolSmallCentres(lines);
            Assert.AreEqual(1, lines.Count, "both small centres end in the one company line");
            PayrollPooling.DebitLine company = Find(lines, Direct, 7, null);
            Assert.AreEqual(510m, company.Amount);
            Assert.IsTrue(company.Pooled);
            Assert.IsTrue(company.Employees.SetEquals(new[] { 1, 2, 3 }));

            // nothing to pool: no change
            var plain = new List<PayrollPooling.DebitLine>();
            PayrollPooling.Add(plain, 1, Direct, null, null, 5m);
            Assert.AreEqual(0, PayrollPooling.PoolSmallCentres(plain).Count);
            Assert.IsFalse(plain[0].Pooled);
        }

        // ---------------------------------------------------------------------------------------- M2: typed or imported sheets and the run warning

        [TestMethod]
        public void A_Typed_Or_Imported_Sheet_Is_Refused_For_A_Prepared_Or_Confirmed_Month_And_Allowed_While_The_Month_Is_Open()
        {
            Assert.IsNull(HrClose.SheetEntryRefusal(1405, 8, null, false), "the month was never closed");
            Assert.IsNull(HrClose.SheetEntryRefusal(1405, 8, new OfAttendanceMonth { Status = OfMonthStatus.Rejected }, true), "a rejected close opens the month again");

            string prepared = HrClose.SheetEntryRefusal(1405, 8, new OfAttendanceMonth { Status = OfMonthStatus.Prepared }, true);
            StringAssert.Contains(prepared, "1405/08");
            StringAssert.Contains(prepared, "آماده شده");
            Assert.AreEqual(prepared, HrClose.SheetEntryRefusal(1405, 8, new OfAttendanceMonth { Status = OfMonthStatus.Prepared }, false), "a prepared month takes no typed sheet, new or not");

            string confirmedExisting = HrClose.SheetEntryRefusal(1405, 8, new OfAttendanceMonth { Status = OfMonthStatus.Confirmed }, true);
            StringAssert.Contains(confirmedExisting, "درخواست اصلاح کارکرد", "the way that remains is named");
            string confirmedNew = HrClose.SheetEntryRefusal(1405, 8, new OfAttendanceMonth { Status = OfMonthStatus.Confirmed }, false);
            StringAssert.Contains(confirmedNew, "برگه جدید");
            Assert.AreNotEqual(confirmedExisting, confirmedNew);
        }

        [TestMethod]
        public void The_Run_Warning_Lists_The_Count_And_The_Sorted_Distinct_Codes_Of_The_Sheets_Outside_The_Close()
        {
            Assert.IsNull(HrClose.OutsideCloseWarning(null));
            Assert.IsNull(HrClose.OutsideCloseWarning(new string[0]));
            Assert.IsNull(HrClose.OutsideCloseWarning(new[] { "", " ", null }));
            string text = HrClose.OutsideCloseWarning(new[] { "B2", "A1", "A1", "", "C10", "B2" });
            StringAssert.StartsWith(text, "3 ");
            StringAssert.EndsWith(text, ": A1، B2، C10");
            StringAssert.Contains(text, "snapshot");
        }

        // ---------------------------------------------------------------------------------------- L6: the policy is part of the hash

        [TestMethod]
        public void The_Month_Hash_Covers_The_Snapshot_And_The_Policy_And_A_Legacy_Month_Hashes_The_Snapshot_Alone()
        {
            const string snapshot = "[{\"e\":1,\"d\":26.00}]";
            Assert.AreEqual(PayrollHash.Hex(snapshot), PayrollHash.OfMonth(snapshot, null), "a month closed before the policy was frozen");
            string withPolicy = PayrollHash.OfMonth(snapshot, "{\"OvertimeRequiresApproval\":true}");
            Assert.AreNotEqual(PayrollHash.OfMonth(snapshot, null), withPolicy);
            Assert.AreNotEqual(withPolicy, PayrollHash.OfMonth(snapshot, "{\"OvertimeRequiresApproval\":false}"), "switching a control changes the hash");
            Assert.AreEqual(withPolicy, PayrollHash.OfMonth(snapshot, "{\"OvertimeRequiresApproval\":true}"));
            Assert.AreNotEqual(withPolicy, PayrollHash.OfMonth(snapshot.Replace("26.00", "25.00"), "{\"OvertimeRequiresApproval\":true}"));
        }

        // ---------------------------------------------------------------------------------------- H1: the LAN addresses of a device

        [TestMethod]
        public void The_Address_List_Of_A_Device_Is_Normalised_Deduplicated_And_Checked()
        {
            Assert.IsTrue(DeviceAddress.TryNormalize(null, out string none, out string noError));
            Assert.IsNull(none);
            Assert.IsNull(noError);
            Assert.IsTrue(DeviceAddress.TryNormalize("  ", out none, out _));
            Assert.IsNull(none);

            Assert.IsTrue(DeviceAddress.TryNormalize("192.168.1.20, 10.0.5.77/24 ; 192.168.1.20", out string list, out _));
            Assert.AreEqual("192.168.1.20,10.0.5.0/24", list, "a range is written with its network address, a duplicate is dropped");
            Assert.IsTrue(DeviceAddress.TryNormalize("۱۹۲.۱۶۸.۱.۲۰", out list, out _));
            Assert.AreEqual("192.168.1.20", list, "Persian digits are read");

            foreach (string bad in new[] { "300.1.1.1", "10.1", "167772161", "10.0.0.0/33", "abc", "10.0.0.1/", "::1", "10.0.0.1/-1" })
            {
                Assert.IsFalse(DeviceAddress.TryNormalize("192.168.1.20," + bad, out string rejected, out string error), bad);
                Assert.IsNull(rejected);
                StringAssert.Contains(error, bad.Split('/')[0].Length > 0 ? bad : "نامعتبر");
            }
            string eleven = string.Join(",", Enumerable.Range(1, 11).Select(i => "10.0.0." + i));
            Assert.IsFalse(DeviceAddress.TryNormalize(eleven, out _, out string tooMany));
            StringAssert.Contains(tooMany, "10");
        }

        [TestMethod]
        public void A_Device_Is_Allowed_Only_From_An_Address_Inside_Its_List()
        {
            Assert.IsTrue(DeviceAddress.Allows(null, "8.8.8.8"), "no list = any address");
            Assert.IsTrue(DeviceAddress.Allows("", null));
            const string list = "192.168.1.20,10.0.5.0/24";
            Assert.IsTrue(DeviceAddress.Allows(list, "192.168.1.20"));
            Assert.IsFalse(DeviceAddress.Allows(list, "192.168.1.21"));
            Assert.IsTrue(DeviceAddress.Allows(list, "10.0.5.1"));
            Assert.IsTrue(DeviceAddress.Allows(list, "10.0.5.254"));
            Assert.IsFalse(DeviceAddress.Allows(list, "10.0.6.1"));
            Assert.IsTrue(DeviceAddress.Allows(list, "::ffff:192.168.1.20"), "an IPv4-mapped IPv6 address is the IPv4 address");
            Assert.IsFalse(DeviceAddress.Allows(list, "2001:db8::1"), "any other IPv6 address has no IPv4 form");
            Assert.IsFalse(DeviceAddress.Allows(list, null), "no address known = refused when a list exists");
            Assert.IsFalse(DeviceAddress.Allows(list, "not-an-address"));
            Assert.IsTrue(DeviceAddress.Allows("127.0.0.1", "::1"), "the IPv6 loopback is 127.0.0.1");
            Assert.IsTrue(DeviceAddress.Allows("0.0.0.0/0", "203.0.113.9"), "a /0 range is every IPv4 address");
        }

        // ---------------------------------------------------------------------------------------- M8: the gap-day span

        [TestMethod]
        public void A_Day_Counts_As_A_Gap_Day_Only_Between_The_First_Assignment_And_The_End_Of_The_Last_One()
        {
            var spans = new Dictionary<int, AttendanceService.AssignmentSpan>
            {
                [1] = new AttendanceService.AssignmentSpan { First = new DateTime(2026, 8, 5), End = new DateTime(2026, 8, 25) },
                [2] = new AttendanceService.AssignmentSpan { First = new DateTime(2026, 8, 5), End = null },
            };
            Assert.IsFalse(AttendanceService.InAssignmentSpan(spans, 1, new DateTime(2026, 8, 4)), "before the first assignment the day is neutral");
            Assert.IsTrue(AttendanceService.InAssignmentSpan(spans, 1, new DateTime(2026, 8, 5)), "the first day belongs to the span");
            Assert.IsTrue(AttendanceService.InAssignmentSpan(spans, 1, new DateTime(2026, 8, 15)));
            Assert.IsTrue(AttendanceService.InAssignmentSpan(spans, 1, new DateTime(2026, 8, 25)), "the last day belongs to the span");
            Assert.IsFalse(AttendanceService.InAssignmentSpan(spans, 1, new DateTime(2026, 8, 26)), "after a closed last assignment the day is neutral");
            Assert.IsFalse(AttendanceService.InAssignmentSpan(spans, 2, new DateTime(2026, 8, 4)));
            Assert.IsTrue(AttendanceService.InAssignmentSpan(spans, 2, new DateTime(2027, 3, 1)), "an open-ended last assignment never closes the span");
            Assert.IsFalse(AttendanceService.InAssignmentSpan(spans, 3, new DateTime(2026, 8, 15)), "an employee without any shift assignment has no span");
        }

        // ---------------------------------------------------------------------------------------- M4: the event lines for a reader who may not see persons

        private static AccountingEventLineDto Dto(int row, string role, decimal debit, decimal credit, string party = null, int? workOrder = null) => new AccountingEventLineDto
        {
            RowNumber = row, AccountRole = role, Debit = debit, Credit = credit, PartyType = party, Description = "d" + row, WorkOrderId = workOrder, CostCenter = party == null ? "CC" : null,
        };

        [TestMethod]
        public void The_Employee_Lines_Of_An_Event_Are_Merged_Per_Role_And_Side_And_The_Totals_Stay()
        {
            var lines = new List<AccountingEventLineDto>
            {
                Dto(1, "PayrollExpense", 5000m, 0m, null, 9),
                Dto(2, "EmployeeLoan", 0m, 2000m, PersonalLedger.EmployeeParty),
                Dto(3, "EmployeeLoan", 0m, 1500m, PersonalLedger.EmployeeParty),
                Dto(4, "EmployeeLoan", 500m, 0m, PersonalLedger.EmployeeParty),
                Dto(5, "EmployeeAdvance", 0m, 300m, PersonalLedger.EmployeeParty),
                Dto(6, "Customer", 0m, 700m, "Customer"),
                Dto(7, "PayrollPayable", 0m, 800m),
            };
            List<AccountingEventLineDto> merged = PersonalLedger.MergeEventLines(lines);
            Assert.AreEqual(6, merged.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, merged.Select(l => l.RowNumber).ToArray(), "the rows are numbered again");
            Assert.AreEqual("PayrollExpense", merged[0].AccountRole);
            Assert.AreEqual(9, merged[0].WorkOrderId, "a line that is not personal is left as it is");
            Assert.AreEqual("EmployeeLoan", merged[1].AccountRole);
            Assert.AreEqual(3500m, merged[1].Credit, "the two recoveries of the loan are one line, at the place of the first");
            Assert.AreEqual(0m, merged[1].Debit);
            Assert.AreEqual(PersonalLedger.MaskedNote, merged[1].Description);
            Assert.AreEqual(500m, merged[2].Debit, "the debit side of the same role is a line of its own");
            Assert.AreEqual("EmployeeAdvance", merged[3].AccountRole, "another role is merged separately");
            Assert.AreEqual(300m, merged[3].Credit);
            Assert.AreEqual("Customer", merged[4].PartyType, "a customer line is not an employee line");
            Assert.AreEqual(lines.Sum(l => l.Debit), merged.Sum(l => l.Debit));
            Assert.AreEqual(lines.Sum(l => l.Credit), merged.Sum(l => l.Credit));
        }

        [TestMethod]
        public void The_Stored_Event_Lines_Are_Merged_Without_Touching_The_Tracked_Originals()
        {
            AccountingEventLine L(int row, string role, decimal debit, decimal credit, string party) => new AccountingEventLine { RowNumber = row, AccountRole = role, Debit = debit, Credit = credit, PartyType = party, Description = "d" + row, PartyId = row };
            var lines = new List<AccountingEventLine>
            {
                L(1, "PayrollExpense", 5000m, 0m, null),
                L(2, "EmployeeLoan", 0m, 2000m, PersonalLedger.EmployeeParty),
                L(3, "EmployeeLoan", 0m, 1500m, PersonalLedger.EmployeeParty),
                L(4, "PayrollPayable", 0m, 1500m, null),
            };

            // a reader who may not see persons: one merged line, new numbers next to the lines, the originals unchanged
            List<(int RowNumber, AccountingEventLine Line)> hidden = PersonalLedger.MergeEventLines(lines, false);
            Assert.AreEqual(3, hidden.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, hidden.Select(h => h.RowNumber).ToArray());
            Assert.AreEqual(3500m, hidden[1].Line.Credit);
            Assert.AreEqual(PersonalLedger.MaskedNote, hidden[1].Line.Description);
            Assert.IsNull(hidden[1].Line.PartyId, "the merged line names no person");
            Assert.IsFalse(lines.Contains(hidden[1].Line), "the merged line is a new object");
            Assert.AreEqual(2000m, lines[1].Credit);
            Assert.AreEqual(1500m, lines[2].Credit);
            Assert.AreEqual(2, lines[1].RowNumber);
            Assert.AreEqual(3, lines[2].RowNumber);
            Assert.AreEqual("d2", lines[1].Description);
            Assert.AreEqual(4, lines[3].RowNumber, "no tracked line was renumbered");

            // a reader who sees persons gets every line as it is
            List<(int RowNumber, AccountingEventLine Line)> shown = PersonalLedger.MergeEventLines(lines, true);
            Assert.AreEqual(4, shown.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, shown.Select(h => h.RowNumber).ToArray());
            Assert.IsTrue(shown.Select(h => h.Line).SequenceEqual(lines), "the same objects in the same order");
        }

        // ---------------------------------------------------------------------------------------- H2: the second person of a request

        private async Task<(FnEmployee Self, FnEmployee Manager, FnEmployee Other)> SeedUsersAsync(IUnitOfWork uow)
        {
            var manager = new IdentityRole(FinanceRoles.Managers);
            uow.Set<IdentityRole>().Add(manager);
            await uow.SaveChangesAsync();
            uow.Set<IdentityUserRole>().Add(new IdentityUserRole { UserId = 501, RoleId = manager.Id });
            FnEmployee self = R1fDb.Employee("8001", 700), finance = R1fDb.Employee("8002", 501), other = R1fDb.Employee("8003", 702);
            uow.Set<FnEmployee>().AddRange(self, finance, other);
            await uow.SaveChangesAsync();
            return (self, finance, other);
        }

        [TestMethod]
        public async Task Nobody_Decides_A_Request_About_Himself_And_The_Record_Of_A_Finance_Manager_Goes_To_The_Ceo()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                var (self, finance, other) = await SeedUsersAsync(uow);
                var payroll = new TestUser(700, "PAYROLL");
                var manager = new TestUser(501, FinanceRoles.Managers);
                var ceo = new TestUser(900, FinanceRoles.Ceo);
                const string notAllowed = "no role";

                // the person concerned: refused whatever his role (also the CEO)
                Assert.AreEqual(SelfApproval.OwnRecord, await SelfApproval.RefusalAsync(uow, payroll, self, true, notAllowed, true, CancellationToken.None));
                Assert.AreEqual(SelfApproval.OwnRecord, await SelfApproval.RefusalAsync(uow, new TestUser(700, FinanceRoles.Ceo), self, true, notAllowed, false, CancellationToken.None));
                Assert.AreEqual(SelfApproval.OwnRecord, await SelfApproval.RefusalAsync(uow, manager, finance, true, notAllowed, true, CancellationToken.None), "the finance manager's own record");

                // another employee: the usual role decides, a caller without it is told so
                Assert.IsNull(await SelfApproval.RefusalAsync(uow, payroll, other, true, notAllowed, true, CancellationToken.None));
                Assert.AreEqual(notAllowed, await SelfApproval.RefusalAsync(uow, payroll, other, false, notAllowed, true, CancellationToken.None));
                Assert.IsNull(await SelfApproval.RefusalAsync(uow, payroll, null, true, notAllowed, true, CancellationToken.None), "no subject: the usual rule");

                // the record of a finance manager: loans, openings and employee changes go to the CEO alone
                Assert.AreEqual(SelfApproval.FinanceManagerRecord, await SelfApproval.RefusalAsync(uow, payroll, finance, true, notAllowed, true, CancellationToken.None));
                Assert.AreEqual(SelfApproval.FinanceManagerRecord, await SelfApproval.RefusalAsync(uow, new TestUser(502, FinanceRoles.Managers), finance, true, notAllowed, true, CancellationToken.None), "another finance manager does not decide it either");
                Assert.IsNull(await SelfApproval.RefusalAsync(uow, ceo, finance, false, notAllowed, true, CancellationToken.None), "the CEO decides it");
                // corrections are decided by HR: the usual role or the CEO
                Assert.IsNull(await SelfApproval.RefusalAsync(uow, new TestUser(800, "HR"), finance, true, notAllowed, false, CancellationToken.None));
                Assert.AreEqual(notAllowed, await SelfApproval.RefusalAsync(uow, new TestUser(801, "PAYROLL"), finance, false, notAllowed, false, CancellationToken.None));
                Assert.IsNull(await SelfApproval.RefusalAsync(uow, ceo, finance, false, notAllowed, false, CancellationToken.None));

                // the inbox item of such a request
                Assert.AreEqual("PAYROLL,FINANCEMANAGER", await SelfApproval.GateRolesAsync(uow, other, "PAYROLL,FINANCEMANAGER", true, CancellationToken.None));
                Assert.AreEqual(FinanceRoles.Ceo, await SelfApproval.GateRolesAsync(uow, finance, "PAYROLL,FINANCEMANAGER", true, CancellationToken.None));
                Assert.AreEqual("HR," + FinanceRoles.Ceo, await SelfApproval.GateRolesAsync(uow, finance, "HR", false, CancellationToken.None));
                Assert.AreEqual("HR", await SelfApproval.GateRolesAsync(uow, null, "HR", false, CancellationToken.None));

                HashSet<int> managers = await SelfApproval.FinanceManagerEmployeesAsync(uow, new[] { self, finance, other }, CancellationToken.None);
                Assert.IsTrue(managers.SetEquals(new[] { finance.Id }));
                Assert.IsTrue(await SelfApproval.HoldsFinanceManagerRoleAsync(uow, 501, CancellationToken.None));
                Assert.IsFalse(await SelfApproval.HoldsFinanceManagerRoleAsync(uow, 700, CancellationToken.None));
                Assert.IsFalse(await SelfApproval.HoldsFinanceManagerRoleAsync(uow, null, CancellationToken.None));
            });
        }

        // ---------------------------------------------------------------------------------------- L2: the cached wage follows the history

        [TestMethod]
        public async Task The_Nightly_Refresh_Sets_The_Cached_Wage_To_The_Wage_In_Force_This_Month()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                DateTime today = new DateTime(2026, 10, 28);   // 1405/08/06
                DateTime monthStart = WageHistory.MonthStart(today);
                FnEmployee raised = R1fDb.Employee("8101"), later = R1fDb.Employee("8102"), plain = R1fDb.Employee("8103");
                raised.DailyWage = 100m; later.DailyWage = 200m; plain.DailyWage = 300m;
                uow.Set<FnEmployee>().AddRange(raised, later, plain);
                await uow.SaveChangesAsync();
                uow.Set<FnEmployeeWage>().AddRange(
                    new FnEmployeeWage { EmployeeId = raised.Id, EffectiveFrom = new DateTime(2026, 3, 21), DailyWage = 100m, IsBaseline = true },
                    new FnEmployeeWage { EmployeeId = raised.Id, EffectiveFrom = monthStart, DailyWage = 150m },                 // approved earlier, future-dated, in force since this month began
                    new FnEmployeeWage { EmployeeId = later.Id, EffectiveFrom = new DateTime(2026, 3, 21), DailyWage = 200m, IsBaseline = true },
                    new FnEmployeeWage { EmployeeId = later.Id, EffectiveFrom = WageHistory.MonthStart(today.AddMonths(1)), DailyWage = 999m });   // not yet in force
                await uow.SaveChangesAsync();

                Assert.AreEqual(1, await WageHistory.RefreshCachedWagesAsync(uow, today, CancellationToken.None));
                Assert.AreEqual(150m, (await uow.Set<FnEmployee>().AsNoTracking().FirstAsync(e => e.Id == raised.Id)).DailyWage);
                Assert.AreEqual(200m, (await uow.Set<FnEmployee>().AsNoTracking().FirstAsync(e => e.Id == later.Id)).DailyWage, "a wage of next month is not in force yet");
                Assert.AreEqual(300m, (await uow.Set<FnEmployee>().AsNoTracking().FirstAsync(e => e.Id == plain.Id)).DailyWage, "an employee without a history keeps his cached wage");
                Assert.AreEqual(0, await WageHistory.RefreshCachedWagesAsync(uow, today, CancellationToken.None), "nothing left to change");
                Assert.AreEqual(1, await WageHistory.RefreshCachedWagesAsync(uow, today.AddMonths(1), CancellationToken.None), "next month the other wage takes effect");
                Assert.AreEqual(999m, (await uow.Set<FnEmployee>().AsNoTracking().FirstAsync(e => e.Id == later.Id)).DailyWage);
            });
        }

        // ---------------------------------------------------------------------------------------- M1 and L6: the one-time upgrade

        [TestMethod]
        public async Task The_Upgrade_Turns_The_Overtime_Approval_On_Once_And_Locks_The_Sheets_Of_The_Months_Closed_Before_The_Snapshot()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                uow.Set<OfAttendancePolicy>().Add(new OfAttendancePolicy { OvertimeRequiresApproval = false, GraceMinutes = 10, CompanyRulesApplied = true });
                uow.Set<OfAttendanceMonth>().AddRange(
                    new OfAttendanceMonth { Year = 1404, Month = 5, Status = OfMonthStatus.Confirmed },                                                    // closed before the snapshot existed
                    new OfAttendanceMonth { Year = 1404, Month = 6, Status = OfMonthStatus.Prepared, SnapshotJson = "[]", SnapshotHash = "x" },           // prepared by the two-step close
                    new OfAttendanceMonth { Year = 1404, Month = 7, Status = OfMonthStatus.Confirmed, SnapshotJson = "[]", SnapshotHash = "y" });          // confirmed by the two-step close
                uow.Set<FnAttendanceSheet>().AddRange(
                    new FnAttendanceSheet { EmployeeId = 1, Year = 1404, Month = 5, DaysWorked = 26m, Source = FnSheetSource.Manual },
                    new FnAttendanceSheet { EmployeeId = 2, Year = 1404, Month = 5, DaysWorked = 25m, Source = FnSheetSource.HrClose, Locked = false },
                    new FnAttendanceSheet { EmployeeId = 1, Year = 1404, Month = 6, DaysWorked = 26m, Source = FnSheetSource.Manual },
                    new FnAttendanceSheet { EmployeeId = 1, Year = 1404, Month = 7, DaysWorked = 26m, Source = FnSheetSource.Manual });
                await uow.SaveChangesAsync();

                Assert.IsFalse(await R1fUpgrades.OvertimeApprovalDoneAsync(uow, CancellationToken.None));
                await R1fUpgrades.EnsureAsync(uow, 77, CancellationToken.None);

                Assert.IsTrue(await R1fUpgrades.OvertimeApprovalDoneAsync(uow, CancellationToken.None));
                OfAttendancePolicy policy = await uow.Set<OfAttendancePolicy>().AsNoTracking().SingleAsync();
                Assert.IsTrue(policy.OvertimeRequiresApproval, "the switch is on for the existing row");
                Assert.AreEqual(10, policy.GraceMinutes, "nothing else was touched");
                Assert.AreEqual(77, policy.UpdatedByUserId);
                List<FnAttendanceSheet> sheets = await uow.Set<FnAttendanceSheet>().AsNoTracking().ToListAsync();
                Assert.IsTrue(sheets.Where(s => s.Month == 5).All(s => s.Source == FnSheetSource.HrClose && s.Locked), "the sheets of the old closed month are HrClose rows and locked");
                Assert.IsTrue(sheets.Where(s => s.Month != 5).All(s => s.Source == FnSheetSource.Manual && !s.Locked), "months with a snapshot are not touched");
                Assert.AreEqual(2, await uow.Set<FnSetting>().AsNoTracking().CountAsync(s => s.Key == R1fUpgrades.OvertimeApprovalMarker || s.Key == R1fUpgrades.ClosedSheetsMarker));

                // the CEO decides to switch it off later: the upgrade does not turn it on again
                OfAttendancePolicy tracked = await uow.Set<OfAttendancePolicy>().SingleAsync();
                tracked.OvertimeRequiresApproval = false;
                FnAttendanceSheet extra = await uow.Set<FnAttendanceSheet>().FirstAsync(s => s.Month == 5 && s.EmployeeId == 1);
                extra.Source = FnSheetSource.Manual; extra.Locked = false;
                await uow.SaveChangesAsync();
                await R1fUpgrades.EnsureAsync(uow, 78, CancellationToken.None);
                Assert.IsFalse((await uow.Set<OfAttendancePolicy>().AsNoTracking().SingleAsync()).OvertimeRequiresApproval, "one time only");
                Assert.AreEqual(FnSheetSource.Manual, (await uow.Set<FnAttendanceSheet>().AsNoTracking().FirstAsync(s => s.Id == extra.Id)).Source, "one time only");
                Assert.AreEqual(2, await uow.Set<FnSetting>().AsNoTracking().CountAsync(s => s.Key == R1fUpgrades.OvertimeApprovalMarker || s.Key == R1fUpgrades.ClosedSheetsMarker), "no marker twice");
            });
        }

        // ---------------------------------------------------------------------------------------- M5: a leaver loses his refresh tokens

        public class TokenSpy : DispatchProxy
        {
            public static readonly List<int> Invalidated = new List<int>();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == "InvalidateUserTokensAsync") lock (Invalidated) Invalidated.Add((int)args[0]);
                return targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null;
            }
        }

        public class NullStub : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args) => null;
        }

        [TestMethod]
        public async Task A_Leaver_Is_Locked_His_Refresh_Tokens_Are_Revoked_And_His_Open_Shift_Assignments_End()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                var user = new IdentityUser { UserName = "leaver-r1f", Name = "n", LastName = "l", Email = "leaver-r1f@example.test", SecurityStamp = "stamp-before", IsActive = true };
                uow.Set<IdentityUser>().Add(user);
                await uow.SaveChangesAsync();
                FnEmployee employee = R1fDb.Employee("8201", user.Id);
                uow.Set<FnEmployee>().Add(employee);
                await uow.SaveChangesAsync();
                uow.Set<OfShiftAssignment>().AddRange(
                    new OfShiftAssignment { EmployeeId = employee.Id, ShiftId = 1, FromDate = new DateTime(2026, 1, 1), ToDate = null, CreatedByUserId = 1 },          // open: ends on the last day
                    new OfShiftAssignment { EmployeeId = employee.Id, ShiftId = 1, FromDate = new DateTime(2026, 12, 1), ToDate = null, CreatedByUserId = 1 });         // starts after the last day: removed
                await uow.SaveChangesAsync();

                TokenSpy.Invalidated.Clear();
                var service = new GlService(uow, new TestUser(900, FinanceRoles.Managers), DispatchProxy.Create<IEventPostingService, NullStub>(), DispatchProxy.Create<ILedgerReportService, NullStub>(),
                    DispatchProxy.Create<IReceivablesService, NullStub>(), DispatchProxy.Create<INotificationService, NullStub>(), DispatchProxy.Create<Services.Token.ITokenStoreService, TokenSpy>());
                MethodInfo apply = typeof(GlService).GetMethod("ApplyLeaverAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(apply);
                var notes = await (Task<List<string>>)apply.Invoke(service, new object[] { employee, new DateTime(2026, 10, 20), "ST-R1F", CancellationToken.None });
                await uow.SaveChangesAsync();

                CollectionAssert.AreEqual(new[] { user.Id }, TokenSpy.Invalidated.ToArray(), "the refresh tokens of the linked user were revoked");
                Assert.IsTrue(notes.Any(n => n.Contains("refresh")), "the approval result says so");
                IdentityUser locked = await uow.Set<IdentityUser>().AsNoTracking().SingleAsync(u => u.Id == user.Id);
                Assert.IsFalse(locked.IsActive);
                Assert.IsTrue(locked.LockoutEnabled);
                Assert.IsTrue(locked.LockoutEnd.HasValue && locked.LockoutEnd.Value > DateTimeOffset.Now.AddYears(50));
                Assert.AreNotEqual("stamp-before", locked.SecurityStamp, "a new security stamp invalidates the access tokens at their next request");
                List<OfShiftAssignment> assignments = await uow.Set<OfShiftAssignment>().AsNoTracking().Where(a => a.EmployeeId == employee.Id).ToListAsync();
                Assert.AreEqual(1, assignments.Count);
                Assert.AreEqual(new DateTime(2026, 10, 20), assignments[0].ToDate);
                FnAuditLog audit = await uow.Set<FnAuditLog>().AsNoTracking().OrderByDescending(a => a.Id).FirstAsync(a => a.Action == "Leaver");
                StringAssert.Contains(audit.AfterJson, "refreshTokensRevoked");

                // a second call finds the account locked already: the tokens are still revoked, the account is not locked twice
                TokenSpy.Invalidated.Clear();
                var again = await (Task<List<string>>)apply.Invoke(service, new object[] { employee, new DateTime(2026, 10, 20), "ST-R1F", CancellationToken.None });
                CollectionAssert.AreEqual(new[] { user.Id }, TokenSpy.Invalidated.ToArray());
                Assert.IsFalse(again.Any(n => n.Contains("غیرفعال")), "the account was locked already");
            });
        }
    }

    /// <summary>
    /// Release R1f review H1 and M6: the service rules of the clock import (a pending batch decided by a second HR user), of the device push (address list, key holder), of the policy switches and of the push log retention,
    /// against the in-memory database.
    /// </summary>
    [TestClass]
    public class R1fAttendanceServiceTests
    {
        private readonly ServiceProvider _provider = R1fDb.Build();
        private static readonly CancellationToken Ct = CancellationToken.None;

        private static AttendanceService Service(IUnitOfWork uow, int userId, params string[] roles) => new AttendanceService(uow, new TestUser(userId, roles));

        private static ClockRowDto Row(string code, string time, string date = "1404/05/10") => new ClockRowDto { Code = code, Date = date, Time = time, Direction = "in" };

        // ---------------------------------------------------------------------------------------- H1: the import is a pending batch

        [TestMethod]
        public async Task An_Import_Waits_As_A_Pending_Batch_And_Only_A_Second_Hr_User_Without_A_Punch_In_It_Decides_It()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                FnEmployee e1 = R1fDb.Employee("9101", 101), e2 = R1fDb.Employee("9102", 102), importerOwn = R1fDb.Employee("9201", 201);
                uow.Set<FnEmployee>().AddRange(e1, e2, importerOwn);
                await uow.SaveChangesAsync();

                var importer = Service(uow, 201, "HR");
                ResponseDto<ClockImportResultDto> imported = await importer.ImportEventsAsync(new ImportClockDto
                {
                    Reason = "paper register of the gate",
                    Rows = new List<ClockRowDto> { Row("9101", "08:00"), Row("9102", "08:05"), Row("9201", "08:10"), Row("9101", "08:00"), Row("9999", "08:20") },
                }, Ct);
                Assert.IsTrue(imported.Success, imported.Message);
                ClockImportResultDto result = imported.Data;
                Assert.AreEqual(2, result.Imported);
                Assert.AreEqual(1, result.Duplicates);
                Assert.AreEqual(1, result.Unknown);
                Assert.AreEqual(1, result.Rejected, "the importer's own punch is refused");
                Assert.IsTrue(result.Errors.Any(m => m.Contains("خودِ واردکننده")), string.Join(" | ", result.Errors));
                Assert.IsTrue(result.PendingApproval);
                Assert.IsNotNull(result.BatchId);

                List<OfClockEvent> events = await uow.Set<OfClockEvent>().AsNoTracking().ToListAsync();
                Assert.AreEqual(2, events.Count, "the importer's row and the duplicate were not stored");
                Assert.IsTrue(events.All(e => e.Approval == OfClockApproval.Pending && e.Source == OfClockSource.Import && e.ImportBatchId == result.BatchId), "nothing counts before the second person decides");
                OfClockImportBatch batch = await uow.Set<OfClockImportBatch>().AsNoTracking().SingleAsync();
                Assert.AreEqual(OfClockBatchStatus.Pending, batch.Status);
                Assert.AreEqual(201, batch.ImportedByUserId);
                Assert.AreEqual(2, batch.Rows);

                // the importer, a non-HR user and the HR user whose own punch is inside the batch cannot decide it
                var action = new ClockBatchActionDto { Id = batch.Id, Action = "approve" };
                ResponseDto<ClockImportBatchDto> own = await importer.ImportBatchActionAsync(action, Ct);
                Assert.IsFalse(own.Success);
                StringAssert.Contains(own.Message, "همان کسی که وارد کرده");
                Assert.IsFalse((await Service(uow, 900, "CEO").ImportBatchActionAsync(action, Ct)).Success, "a role that is not HR");
                ResponseDto<ClockImportBatchDto> withPunch = await Service(uow, 101, "HR").ImportBatchActionAsync(action, Ct);
                Assert.IsFalse(withPunch.Success);
                StringAssert.Contains(withPunch.Message, "تردد دارد");

                // the list of the batches waiting for me: the second HR user sees it with the two actions, the importer does not
                var second = Service(uow, 202, "HR");
                ResponseDto<ViewModels.NasbNiro.Pagination.PagedList<ClockImportBatchDto>> waiting = await second.GetImportBatchesAsync(new ClockBatchQuery { WaitingForMe = true }, Ct);
                Assert.IsTrue(waiting.Success, waiting.Message);
                Assert.AreEqual(1, waiting.Data.Count);
                CollectionAssert.AreEquivalent(new[] { "approve", "reject" }, waiting.Data[0].AllowedActions.ToArray());
                Assert.AreEqual(0, (await importer.GetImportBatchesAsync(new ClockBatchQuery { WaitingForMe = true }, Ct)).Data.Count);

                // a rejection needs a reason; with one the rows are rejected and stay rejected
                ResponseDto<ClockImportBatchDto> noNote = await second.ImportBatchActionAsync(new ClockBatchActionDto { Id = batch.Id, Action = "reject" }, Ct);
                Assert.IsFalse(noNote.Success);
                ResponseDto<ClockImportBatchDto> rejected = await second.ImportBatchActionAsync(new ClockBatchActionDto { Id = batch.Id, Action = "reject", Note = "the file is not the register" }, Ct);
                Assert.IsTrue(rejected.Success, rejected.Message);
                Assert.AreEqual("Rejected", rejected.Data.Status);
                Assert.IsTrue((await uow.Set<OfClockEvent>().AsNoTracking().ToListAsync()).All(e => e.Approval == OfClockApproval.Rejected && e.ApprovedByUserId == 202));
                ResponseDto<ClockImportBatchDto> twice = await second.ImportBatchActionAsync(action, Ct);
                Assert.IsFalse(twice.Success, "decided already");
                StringAssert.Contains(twice.Message, "قبلاً تصمیم گرفته شده");
            });
        }

        [TestMethod]
        public async Task The_Second_Hr_User_Approves_A_Batch_And_Its_Punches_Count()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                FnEmployee e1 = R1fDb.Employee("9111", 111), e2 = R1fDb.Employee("9112", 112);
                uow.Set<FnEmployee>().AddRange(e1, e2);
                await uow.SaveChangesAsync();
                // an earlier device punch of the same day: it is approved and stays as it is
                uow.Set<OfClockEvent>().Add(new OfClockEvent { EmployeeId = e1.Id, Timestamp = new DateTime(2025, 8, 1, 8, 0, 0), Source = OfClockSource.Device, DeviceCode = "GATE", Approval = OfClockApproval.Approved });
                await uow.SaveChangesAsync();

                var importer = Service(uow, 211, "HR");
                ResponseDto<ClockImportResultDto> imported = await importer.ImportEventsAsync(new ImportClockDto { Rows = new List<ClockRowDto> { Row("9111", "08:30"), Row("9112", "08:35") } }, Ct);
                Assert.IsTrue(imported.Success, imported.Message);
                Assert.AreEqual(2, imported.Data.Imported);

                var second = Service(uow, 212, "HR");
                ResponseDto<ClockImportBatchDto> approved = await second.ImportBatchActionAsync(new ClockBatchActionDto { Id = imported.Data.BatchId.Value, Action = "approve", Note = "checked against the register" }, Ct);
                Assert.IsTrue(approved.Success, approved.Message);
                Assert.AreEqual("Approved", approved.Data.Status);
                List<OfClockEvent> imports = await uow.Set<OfClockEvent>().AsNoTracking().Where(e => e.Source == OfClockSource.Import).ToListAsync();
                Assert.AreEqual(2, imports.Count);
                Assert.IsTrue(imports.All(e => e.Approval == OfClockApproval.Approved && e.ApprovedByUserId == 212 && e.ApprovedAt.HasValue));
                OfClockImportBatch batch = await uow.Set<OfClockImportBatch>().AsNoTracking().SingleAsync();
                Assert.AreEqual(OfClockBatchStatus.Approved, batch.Status);
                Assert.AreEqual(212, batch.DecidedByUserId);
                Assert.AreEqual("checked against the register", batch.DecisionNote);

                // the audit trail: the imported rows one by one, the batch and its decision
                Assert.AreEqual(2, await uow.Set<FnAuditLog>().AsNoTracking().CountAsync(a => a.EntityType == "OfClockEvent" && a.Action == "Added"), "two imported rows; the approved device punch has no row");
                Assert.IsTrue(await uow.Set<FnAuditLog>().AsNoTracking().AnyAsync(a => a.EntityType == "OfClockImportBatch" && a.Action == "Added"));
                Assert.IsTrue(await uow.Set<FnAuditLog>().AsNoTracking().AnyAsync(a => a.EntityType == "OfClockImportBatch" && a.Action == "Modified" && a.AfterJson.Contains("\"DecidedByUserId\":212")));
                Assert.AreEqual(2, await uow.Set<FnAuditLog>().AsNoTracking().CountAsync(a => a.EntityType == "OfClockEvent" && a.Action == "Modified"), "the approval of each imported row");
            });
        }

        // ---------------------------------------------------------------------------------------- H1: the device push

        [TestMethod]
        public async Task A_Device_Is_Refused_From_Another_Address_And_The_Punch_Of_The_Key_Holder_Is_Held_For_A_Second_Person()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                FnEmployee holder = R1fDb.Employee("9301", 301), other = R1fDb.Employee("9302", 302), rotator = R1fDb.Employee("9303", 303);
                uow.Set<FnEmployee>().AddRange(holder, other, rotator);
                await uow.SaveChangesAsync();

                // HR user 301 creates the device bound to two LAN entries and sees the key once
                ResponseDto<DeviceKeyDto> created = await Service(uow, 301, "HR").SaveDeviceAsync(new SaveDeviceDto { Code = "gate-r1f", Title = "gate", AllowedAddresses = "10.0.5.0/24, 192.168.1.20" }, Ct);
                Assert.IsTrue(created.Success, created.Message);
                string key = created.Data.ApiKey;
                Assert.IsFalse(string.IsNullOrEmpty(key));
                Assert.AreEqual("10.0.5.0/24,192.168.1.20", created.Data.Device.AllowedAddresses);
                int deviceId = created.Data.Device.Id;

                var device = Service(uow, 0);
                DateTime day = new DateTime(2025, 8, 1);
                DevicePushDto Push(params (string Code, int Hour)[] punches) => new DevicePushDto { Events = punches.Select(p => new DevicePushEventDto { EmployeeCode = p.Code, Timestamp = day.AddHours(p.Hour), Direction = OfClockDirection.In }).ToList() };

                // the right key from a wrong address is refused and logged
                ResponseDto<DevicePushResultDto> wrongAddress = await device.PushAsync("GATE-R1F", key, Push(("9302", 8)), Ct, "172.16.0.9");
                Assert.IsFalse(wrongAddress.Success);
                Assert.AreEqual(HttpStatusCode.Unauthorized, wrongAddress.HttpStatusCode);
                Assert.AreEqual(0, await uow.Set<OfClockEvent>().CountAsync(), "nothing was stored");
                Assert.IsTrue(await uow.Set<OfDevicePushLog>().AsNoTracking().AnyAsync(l => l.Outcome == "RejectedAddress" && l.DeviceId == deviceId));
                Assert.IsFalse((await device.PushAsync("GATE-R1F", key, Push(("9302", 8)), Ct, null)).Success, "no address known while a list exists");

                // from the LAN: the key creator's punch is held, the other punch is approved at once
                ResponseDto<DevicePushResultDto> accepted = await device.PushAsync("GATE-R1F", key, Push(("9301", 8), ("9302", 8)), Ct, "10.0.5.77");
                Assert.IsTrue(accepted.Success, accepted.Message);
                Assert.AreEqual(2, accepted.Data.Accepted);
                Assert.AreEqual(1, accepted.Data.Held);
                OfClockEvent heldEvent = await uow.Set<OfClockEvent>().AsNoTracking().SingleAsync(e => e.EmployeeId == holder.Id);
                Assert.AreEqual(OfClockApproval.Pending, heldEvent.Approval);
                Assert.AreEqual(OfClockApproval.Approved, (await uow.Set<OfClockEvent>().AsNoTracking().SingleAsync(e => e.EmployeeId == other.Id)).Approval);
                Assert.IsTrue((await device.PushAsync("GATE-R1F", key, Push(("9302", 9)), Ct, "::ffff:192.168.1.20")).Success, "an IPv4-mapped IPv6 address is the listed IPv4 address");

                // the key creator cannot decide his own held punch; another HR user can; the held punch is in the audit trail, the approved ones are not
                var request = new ClockRequestActionDto { Id = heldEvent.Id, Action = "approve" };
                ResponseDto<ClockEventDto> byHolder = await Service(uow, 301, "HR").ClockRequestActionAsync(request, Ct);
                Assert.IsFalse(byHolder.Success);
                StringAssert.Contains(byHolder.Message, "کلید دستگاه");
                Assert.IsFalse((await Service(uow, 303, "HR").ClockRequestActionAsync(new ClockRequestActionDto { Id = heldEvent.Id, Action = "reject" }, Ct)).Success, "a rejection needs a reason");
                ResponseDto<ClockEventDto> byOther = await Service(uow, 304, "HR").ClockRequestActionAsync(request, Ct);
                Assert.IsTrue(byOther.Success, byOther.Message);
                Assert.AreEqual(OfClockApproval.Approved, (await uow.Set<OfClockEvent>().AsNoTracking().SingleAsync(e => e.Id == heldEvent.Id)).Approval);
                Assert.AreEqual(304, (await uow.Set<OfClockEvent>().AsNoTracking().SingleAsync(e => e.Id == heldEvent.Id)).ApprovedByUserId);
                Assert.IsTrue(await uow.Set<FnAuditLog>().AsNoTracking().AnyAsync(a => a.EntityType == "OfClockEvent" && a.Action == "Added" && a.AfterJson.Contains("\"Approval\":\"Pending\"")), "the held punch is audited");
                Assert.AreEqual(1, await uow.Set<FnAuditLog>().AsNoTracking().CountAsync(a => a.EntityType == "OfClockEvent" && a.Action == "Added"), "the approved device punches are not");

                // a rotation by HR user 303: the old key is dead, and 303's own punches are held from then on
                ResponseDto<DeviceKeyDto> rotated = await Service(uow, 303, "HR").RotateDeviceKeyAsync(deviceId, Ct);
                Assert.IsTrue(rotated.Success, rotated.Message);
                Assert.IsFalse((await device.PushAsync("GATE-R1F", key, Push(("9303", 10)), Ct, "10.0.5.77")).Success, "the old key");
                ResponseDto<DevicePushResultDto> afterRotation = await device.PushAsync("GATE-R1F", rotated.Data.ApiKey, Push(("9303", 10), ("9302", 10)), Ct, "10.0.5.77");
                Assert.IsTrue(afterRotation.Success, afterRotation.Message);
                Assert.AreEqual(1, afterRotation.Data.Held);
                Assert.AreEqual(OfClockApproval.Pending, (await uow.Set<OfClockEvent>().AsNoTracking().SingleAsync(e => e.EmployeeId == rotator.Id)).Approval);
                Assert.AreEqual(303, (await uow.Set<OfDevice>().AsNoTracking().SingleAsync(d => d.Id == deviceId)).KeyRotatedByUserId);

                // editing the device without an address list keeps the stored list; an empty text clears it
                ResponseDto<DeviceKeyDto> edited = await Service(uow, 301, "HR").SaveDeviceAsync(new SaveDeviceDto { Id = deviceId, Code = "GATE-R1F", Title = "gate 2" }, Ct);
                Assert.IsTrue(edited.Success, edited.Message);
                Assert.IsNull(edited.Data.ApiKey, "an edit never issues a key");
                Assert.AreEqual("10.0.5.0/24,192.168.1.20", edited.Data.Device.AllowedAddresses);
                ResponseDto<DeviceKeyDto> cleared = await Service(uow, 301, "HR").SaveDeviceAsync(new SaveDeviceDto { Id = deviceId, Code = "GATE-R1F", AllowedAddresses = "" }, Ct);
                Assert.IsTrue(cleared.Success, cleared.Message);
                Assert.IsTrue(string.IsNullOrEmpty(cleared.Data.Device.AllowedAddresses));
                Assert.IsFalse((await Service(uow, 301, "CEO").SaveDeviceAsync(new SaveDeviceDto { Code = "OTHER" }, Ct)).Success, "the CEO does not create devices");
                Assert.IsFalse((await Service(uow, 301, "HR").SaveDeviceAsync(new SaveDeviceDto { Code = "BAD-ADDRESS", AllowedAddresses = "10.0.0.300" }, Ct)).Success);
            });
        }

        // ---------------------------------------------------------------------------------------- M6: the controls are switched off by the CEO only

        [TestMethod]
        public async Task The_Overtime_Approval_And_The_Early_Leave_Deduction_Are_Switched_Off_By_The_Ceo_Only()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                var hr = Service(uow, 401, "HR");
                var ceo = Service(uow, 900, "CEO");

                // a policy row from before the release has the switch off: the first write turns it on (the upgrade), then HR cannot switch it off again
                uow.Set<OfAttendancePolicy>().Add(new OfAttendancePolicy { OvertimeRequiresApproval = false, CompanyRulesApplied = true, GraceMinutes = 15 });
                await uow.SaveChangesAsync();
                ResponseDto<AttendancePolicyDto> shown = await hr.GetPolicyAsync(Ct);
                Assert.IsTrue(shown.Data.OvertimeRequiresApproval == true, "the policy shows the value the first write will store");

                ResponseDto<AttendancePolicyDto> off = await hr.SavePolicyAsync(new AttendancePolicyDto { GraceMinutes = 15, OvertimeRequiresApproval = false }, Ct);
                Assert.IsFalse(off.Success);
                StringAssert.Contains(off.Message, "فقط با مدیرعامل");
                Assert.IsTrue((await uow.Set<OfAttendancePolicy>().AsNoTracking().SingleAsync()).OvertimeRequiresApproval, "the upgrade made it ON and HR could not turn it off");
                ResponseDto<AttendancePolicyDto> earlyOff = await hr.SavePolicyAsync(new AttendancePolicyDto { GraceMinutes = 15, EarlyLeaveDeduction = false }, Ct);
                Assert.IsFalse(earlyOff.Success);
                StringAssert.Contains(earlyOff.Message, "فقط با مدیرعامل");

                // HR may change the other numbers and may keep the switches on
                Assert.IsTrue((await hr.SavePolicyAsync(new AttendancePolicyDto { GraceMinutes = 20, OvertimeRequiresApproval = true, EarlyLeaveDeduction = true }, Ct)).Success);
                Assert.AreEqual(20, (await uow.Set<OfAttendancePolicy>().AsNoTracking().SingleAsync()).GraceMinutes);

                // the CEO switches them off; the audit trail records who
                ResponseDto<AttendancePolicyDto> ceoOff = await ceo.SavePolicyAsync(new AttendancePolicyDto { GraceMinutes = 20, OvertimeRequiresApproval = false, EarlyLeaveDeduction = false }, Ct);
                Assert.IsTrue(ceoOff.Success, ceoOff.Message);
                OfAttendancePolicy stored = await uow.Set<OfAttendancePolicy>().AsNoTracking().SingleAsync();
                Assert.IsFalse(stored.OvertimeRequiresApproval);
                Assert.IsTrue(stored.EarlyLeaveDeductionDisabled);
                Assert.AreEqual(900, stored.UpdatedByUserId);
                Assert.IsTrue(await uow.Set<FnAuditLog>().AsNoTracking().AnyAsync(a => a.EntityType == "OfAttendancePolicy" && a.Action == "Modified" && a.AfterJson.Contains("\"OvertimeRequiresApproval\":false")));

                // turning them back on is HR's; the other number stays untouched while the switch is not sent
                Assert.IsTrue((await hr.SavePolicyAsync(new AttendancePolicyDto { GraceMinutes = 20, OvertimeRequiresApproval = true, EarlyLeaveDeduction = true }, Ct)).Success);
                stored = await uow.Set<OfAttendancePolicy>().AsNoTracking().SingleAsync();
                Assert.IsTrue(stored.OvertimeRequiresApproval);
                Assert.IsFalse(stored.EarlyLeaveDeductionDisabled);
                // a user outside HR, PMO and the CEO may not save the policy at all
                Assert.IsFalse((await Service(uow, 402, "SECRETARIAT").SavePolicyAsync(new AttendancePolicyDto { GraceMinutes = 5 }, Ct)).Success);
            });
        }

        // ---------------------------------------------------------------------------------------- L9: the retention of the push log

        [TestMethod]
        public async Task The_Push_Log_Is_Kept_For_The_Number_Of_Days_Of_The_Setting_And_A_Wrong_Value_Means_Ninety()
        {
            await R1fDb.RunAsync(_provider, async uow =>
            {
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (int age in new[] { 400, 100, 40, 10 })
                    uow.Set<OfDevicePushLog>().Add(new OfDevicePushLog { DeviceCode = "G" + age, ReceivedAt = now.AddDays(-age), EventCount = 1, Accepted = 1, Outcome = "Push" });
                await uow.SaveChangesAsync();
                var service = Service(uow, 0);

                Assert.AreEqual(2, await service.PurgePushLogsAsync(Ct), "the default is 90 days: the rows of 400 and 100 days ago go");
                Assert.AreEqual(2, await uow.Set<OfDevicePushLog>().CountAsync());

                uow.Set<FnSetting>().Add(new FnSetting { Key = FinanceSeed.SettingPushLogDays, Value = "abc", Title = "t" });
                await uow.SaveChangesAsync();
                Assert.AreEqual(0, await service.PurgePushLogsAsync(Ct), "a text that is not a number means 90");

                FnSetting setting = await uow.Set<FnSetting>().SingleAsync(s => s.Key == FinanceSeed.SettingPushLogDays);
                setting.Value = "10";
                await uow.SaveChangesAsync();
                Assert.AreEqual(0, await service.PurgePushLogsAsync(Ct), "less than 30 days is not accepted: 90 again");

                setting.Value = "30";
                await uow.SaveChangesAsync();
                Assert.AreEqual(1, await service.PurgePushLogsAsync(Ct), "30 days: the row of 40 days ago goes");
                Assert.AreEqual("G10", (await uow.Set<OfDevicePushLog>().AsNoTracking().SingleAsync()).DeviceCode);
            });
        }
    }
}
