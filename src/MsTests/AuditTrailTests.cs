using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using IocConfig;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ViewModels.Identity.Settings;

namespace MsTests
{
    /// <summary>
    /// Release R1f: what the finance audit trail writes for the attendance and payroll records, against an in-memory database through the real SaveChanges hook.
    /// The wage, the exemption flags and the loan amounts are written as one-way hashes, the Input items of a sheet as a hash plus the names of the changed items, the device key never,
    /// the punches of devices and imported files not per punch. The last test measures what the extra records cost.
    /// </summary>
    [TestClass]
    public class AuditTrailTests
    {
        public TestContext TestContext { get; set; }

        private readonly IServiceProvider _serviceProvider;

        public AuditTrailTests()
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddScoped<IWebHostEnvironment, TestWebHostEnvironment>();
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", reloadOnChange: true, optional: false)
                .AddInMemoryCollection(new[] { new KeyValuePair<string, string>("UseInMemoryDatabase", "true") })
                .Build();
            services.AddSingleton<IConfigurationRoot>(provider => configuration);
            services.Configure<SiteSettings>(options => configuration.Bind(options));
            services.AddScoped<IUnitOfWork, ApplicationDbContext>();
            services.AddCustomIdentityServices();
            var siteSettings = services.GetSiteSettings();
            siteSettings.ActiveDatabase = ActiveDatabase.InMemoryDatabase;
            siteSettings.ConnectionStrings.LocalDb ??= new Localdb { InitialCatalog = "IRNNCO_AuditTests" };
            services.AddRequiredEfInternalServices(siteSettings);
            services.AddDbContextPool<ApplicationDbContext>((serviceProvider, optionsBuilder) =>
            {
                optionsBuilder.SetDbContextOptions(siteSettings);
                optionsBuilder.UseInternalServiceProvider(serviceProvider);
            });
            _serviceProvider = services.BuildServiceProvider();
        }

        private void InScope(Action<IUnitOfWork> body)
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            body(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        }

        private static List<FnAuditLog> Trail(IUnitOfWork uow, string entityType, string summaryContains = null) =>
            uow.Set<FnAuditLog>().Where(a => a.EntityType == entityType).ToList().Where(a => summaryContains == null || (a.Summary ?? "").Contains(summaryContains)).OrderBy(a => a.Id).ToList();

        private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 8);

        // ------------------------------------------------------------------------------------------ clock events

        [TestMethod]
        public void A_Manual_Clock_Request_Is_Audited_Its_Decision_Too_But_Device_And_Import_Punches_Are_Not()
        {
            InScope(uow =>
            {
                int before = Trail(uow, "OfClockEvent").Count;
                DateTime day = new DateTime(2026, 8, 12, 8, 0, 0);
                uow.Set<OfClockEvent>().Add(new OfClockEvent { EmployeeId = 1, Timestamp = day, Source = OfClockSource.Device, DeviceCode = "D1", Approval = OfClockApproval.Approved });
                uow.Set<OfClockEvent>().Add(new OfClockEvent { EmployeeId = 1, Timestamp = day.AddHours(1), Source = OfClockSource.Import, DeviceCode = "IMPORT", Approval = OfClockApproval.Approved });
                var manual = new OfClockEvent { EmployeeId = 1, Timestamp = day.AddHours(2), Source = OfClockSource.Manual, Reason = "forgot", CreatedByUserId = 5, Approval = OfClockApproval.Pending };
                uow.Set<OfClockEvent>().Add(manual);
                uow.SaveChanges();

                List<FnAuditLog> added = Trail(uow, "OfClockEvent").Skip(before).ToList();
                Assert.AreEqual(1, added.Count, "only the manual request has a row; the device and import punches are logged per push");
                Assert.AreEqual("Added", added[0].Action);
                StringAssert.Contains(added[0].AfterJson, "\"Source\":\"Manual\"");
                StringAssert.Contains(added[0].AfterJson, "\"Approval\":\"Pending\"");
                StringAssert.Contains(added[0].AfterJson, "\"CreatedByUserId\":5");

                // the second person approves it
                manual.Approval = OfClockApproval.Approved;
                manual.ApprovedByUserId = 9;
                manual.ApprovedAt = DateTimeOffset.Now;
                uow.SaveChanges();
                List<FnAuditLog> decided = Trail(uow, "OfClockEvent").Skip(before + 1).ToList();
                Assert.AreEqual(1, decided.Count);
                Assert.AreEqual("Modified", decided[0].Action);
                StringAssert.Contains(decided[0].BeforeJson, "\"Approval\":\"Pending\"");
                StringAssert.Contains(decided[0].AfterJson, "\"Approval\":\"Approved\"");
                StringAssert.Contains(decided[0].AfterJson, "\"ApprovedByUserId\":9");
                Assert.IsTrue(decided[0].EntityId > 0, "the long key of the event is recorded");
            });
        }

        // ------------------------------------------------------------------------------------------ the employee master

        [TestMethod]
        public void The_Wage_And_The_Exemption_Flags_Of_An_Employee_Are_Written_As_Hashes_Only()
        {
            InScope(uow =>
            {
                string code = Unique("E");
                var employee = new FnEmployee { PersonnelCode = code, FirstName = "x", LastName = "y", NationalCode = "0499370899", DailyWage = 1234567m, ExemptionFlags = "Disabled", ChildrenCount = 2, HardAndHarmful = true, BankIban = "IR120170000000123456789012", HireDate = new DateTime(2020, 1, 1), PendingApproval = true };
                uow.Set<FnEmployee>().Add(employee);
                uow.SaveChanges();

                FnAuditLog row = Trail(uow, "FnEmployee").Last(a => a.Action == "Added" && a.AfterJson.Contains(code));
                Assert.IsFalse(row.AfterJson.Contains("1234567"), "the wage is never in the trail");
                Assert.IsFalse(row.AfterJson.Contains("Disabled"), "nor the exemption condition");
                Assert.IsFalse(row.AfterJson.Contains("012345678"), "the account number is masked");
                StringAssert.Contains(row.AfterJson, "\"DailyWageHash\"");
                StringAssert.Contains(row.AfterJson, "\"ExemptionFlagsHash\"");
                StringAssert.Contains(row.AfterJson, "\"BankIban\":\"****9012\"");
                StringAssert.Contains(row.AfterJson, "\"BankIbanHash\"");
                StringAssert.Contains(row.AfterJson, "\"ChildrenCount\":2");
                StringAssert.Contains(row.AfterJson, "\"HardAndHarmful\":true");
                StringAssert.Contains(row.AfterJson, "\"PendingApproval\":true");
                Assert.IsFalse(row.AfterJson.Contains("\"FirstName\""), "no name or other personal data");

                // the approval changes the wage and clears the pending flag: both are in the trail, the wage as two different hashes
                employee.DailyWage = 1500000m;
                employee.PendingApproval = false;
                uow.SaveChanges();
                FnAuditLog changed = Trail(uow, "FnEmployee").Last(a => a.Action == "Modified");
                Assert.IsFalse(changed.BeforeJson.Contains("1234567") || changed.AfterJson.Contains("1500000"));
                var beforeJson = JsonDocument.Parse(changed.BeforeJson).RootElement;
                var afterJson = JsonDocument.Parse(changed.AfterJson).RootElement;
                Assert.AreNotEqual(beforeJson.GetProperty("DailyWageHash").GetString(), afterJson.GetProperty("DailyWageHash").GetString());
                Assert.IsTrue(beforeJson.GetProperty("PendingApproval").GetBoolean());
                Assert.IsFalse(afterJson.GetProperty("PendingApproval").GetBoolean());
                Assert.IsFalse(afterJson.TryGetProperty("ChildrenCount", out _), "an unchanged field is not repeated");
                Assert.AreEqual(employee.Id, changed.EntityId);
            });
        }

        [TestMethod]
        public void The_Same_Wage_Gives_The_Same_Hash_So_A_Rewrite_Without_A_Change_Is_Not_A_Change()
        {
            InScope(uow =>
            {
                var employee = new FnEmployee { PersonnelCode = Unique("E"), FirstName = "x", LastName = "y", NationalCode = "0499370899", DailyWage = 800000m, HireDate = new DateTime(2020, 1, 1) };
                uow.Set<FnEmployee>().Add(employee);
                uow.SaveChanges();
                int rows = Trail(uow, "FnEmployee").Count;
                employee.DailyWage = 800000.00m;   // the same value
                employee.UserId = 3;               // another audited field changes
                uow.SaveChanges();
                List<FnAuditLog> after = Trail(uow, "FnEmployee").Skip(rows).ToList();
                Assert.AreEqual(1, after.Count);
                Assert.IsFalse(after[0].AfterJson.Contains("DailyWageHash"));
                StringAssert.Contains(after[0].AfterJson, "\"UserId\":3");
            });
        }

        [TestMethod]
        public void Loan_Amounts_And_Wage_History_Are_Hashes_And_The_Approval_Is_Plain()
        {
            InScope(uow =>
            {
                string title = Unique("loan");
                var loan = new FnEmployeeLoan { EmployeeId = 1, Kind = FnLoanKind.Loan, Title = title, Principal = 5000000m, InstallmentAmount = 500000m, Balance = 5000000m, FirstYear = 1405, FirstMonth = 9, CreatedByUserId = 4, ApprovalStatus = FnLoanApproval.Pending };
                uow.Set<FnEmployeeLoan>().Add(loan);
                var wage = new FnEmployeeWage { EmployeeId = 1, EffectiveFrom = new DateTime(2026, 8, 23), DailyWage = 999888m, CreatedByUserId = 4 };
                uow.Set<FnEmployeeWage>().Add(wage);
                uow.SaveChanges();

                FnAuditLog loanRow = Trail(uow, "FnEmployeeLoan").Last(a => a.Action == "Added" && a.AfterJson.Contains(title));
                Assert.IsFalse(loanRow.AfterJson.Contains("5000000") || loanRow.AfterJson.Contains("500000"));
                StringAssert.Contains(loanRow.AfterJson, "\"PrincipalHash\"");
                StringAssert.Contains(loanRow.AfterJson, "\"InstallmentAmountHash\"");
                StringAssert.Contains(loanRow.AfterJson, "\"ApprovalStatus\":\"Pending\"");
                FnAuditLog wageRow = Trail(uow, "FnEmployeeWage").Last();
                Assert.IsFalse(wageRow.AfterJson.Contains("999888"));
                StringAssert.Contains(wageRow.AfterJson, "\"DailyWageHash\"");

                loan.ApprovalStatus = FnLoanApproval.Approved;
                loan.ApprovedByUserId = 8;
                loan.ApprovedAt = DateTimeOffset.Now;
                uow.SaveChanges();
                FnAuditLog decided = Trail(uow, "FnEmployeeLoan").Last(a => a.Action == "Modified");
                StringAssert.Contains(decided.BeforeJson, "\"ApprovalStatus\":\"Pending\"");
                StringAssert.Contains(decided.AfterJson, "\"ApprovalStatus\":\"Approved\"");
                StringAssert.Contains(decided.AfterJson, "\"ApprovedByUserId\":8");
            });
        }

        // ------------------------------------------------------------------------------------------ the monthly sheet and the requests

        [TestMethod]
        public void Sheet_Inputs_Are_A_Hash_And_The_Names_Of_The_Items_That_Changed()
        {
            InScope(uow =>
            {
                var sheet = new FnAttendanceSheet { EmployeeId = 1, Year = 1405, Month = 700 + new Random().Next(1, 99), DaysWorked = 26m, Source = FnSheetSource.Manual, InputsJson = "{\"BONUS\":100,\"FOOD\":5}" };
                uow.Set<FnAttendanceSheet>().Add(sheet);
                uow.SaveChanges();
                FnAuditLog added = Trail(uow, "FnAttendanceSheet").Last(a => a.Action == "Added");
                StringAssert.Contains(added.AfterJson, "\"InputsJsonHash\"");
                StringAssert.Contains(added.AfterJson, "\"InputsJsonKeys\":\"BONUS,FOOD\"");
                StringAssert.Contains(added.AfterJson, "\"Source\":\"Manual\"");
                StringAssert.Contains(added.AfterJson, "\"Locked\":false");
                Assert.IsFalse(added.AfterJson.Contains("100"), "no amount of an Input item");

                // FOOD changes, MISSION is added, BONUS is the same (written with other spacing and a decimal point)
                sheet.InputsJson = "{ \"BONUS\": 100.00, \"FOOD\": 7, \"MISSION\": 3 }";
                uow.SaveChanges();
                FnAuditLog changed = Trail(uow, "FnAttendanceSheet").Last(a => a.Action == "Modified");
                StringAssert.Contains(changed.AfterJson, "\"InputsJsonKeys\":\"FOOD,MISSION\"");
                var before = JsonDocument.Parse(changed.BeforeJson).RootElement;
                var after = JsonDocument.Parse(changed.AfterJson).RootElement;
                Assert.AreNotEqual(before.GetProperty("InputsJsonHash").GetString(), after.GetProperty("InputsJsonHash").GetString());
                Assert.IsFalse(changed.AfterJson.Contains("BONUS"));

                // the same members in another layout are no change at all
                int rows = Trail(uow, "FnAttendanceSheet").Count;
                sheet.InputsJson = "{\"MISSION\":3,\"FOOD\":7,\"BONUS\":100}";
                uow.SaveChanges();
                Assert.AreEqual(rows, Trail(uow, "FnAttendanceSheet").Count);

                // locking the row is itself an audited change
                sheet.Locked = true;
                sheet.Source = FnSheetSource.HrClose;
                uow.SaveChanges();
                FnAuditLog locked = Trail(uow, "FnAttendanceSheet").Last(a => a.Action == "Modified");
                StringAssert.Contains(locked.AfterJson, "\"Locked\":true");
                StringAssert.Contains(locked.AfterJson, "\"Source\":\"HrClose\"");
            });
        }

        [TestMethod]
        public void A_Correction_Request_Shows_The_Names_Of_The_Numbers_It_Changes_Not_Their_Values()
        {
            InScope(uow =>
            {
                string reason = Unique("fix");
                var request = new FnAttendanceCorrection
                {
                    SheetId = 1, EmployeeId = 1, Year = 1405, Month = 8, Reason = reason, Status = FnChangeStatus.Pending, ProposedByUserId = 4,
                    CurrentJson = "{\"DaysWorked\":26,\"OvertimeHours\":4,\"NightHours\":0}", ProposedJson = "{\"DaysWorked\":26,\"OvertimeHours\":6,\"NightHours\":2}",
                };
                uow.Set<FnAttendanceCorrection>().Add(request);
                uow.SaveChanges();
                FnAuditLog row = Trail(uow, "FnAttendanceCorrection").Last(a => a.Action == "Added" && a.AfterJson.Contains(reason));
                StringAssert.Contains(row.AfterJson, "\"ProposedJsonKeys\":\"NightHours,OvertimeHours\"");
                StringAssert.Contains(row.AfterJson, "\"ProposedJsonHash\"");
                StringAssert.Contains(row.AfterJson, "\"Status\":\"Pending\"");

                request.Status = FnChangeStatus.Approved;
                request.DecidedByUserId = 9;
                request.DecidedAt = DateTimeOffset.Now;
                uow.SaveChanges();
                FnAuditLog decided = Trail(uow, "FnAttendanceCorrection").Last(a => a.Action == "Modified");
                StringAssert.Contains(decided.AfterJson, "\"Status\":\"Approved\"");
                StringAssert.Contains(decided.AfterJson, "\"DecidedByUserId\":9");
            });
        }

        [TestMethod]
        public void The_Employee_Change_Request_Keeps_Its_Proposed_Values_Out_Of_The_Trail()
        {
            InScope(uow =>
            {
                string reason = Unique("raise");
                var employee = new FnEmployee { PersonnelCode = Unique("E"), FirstName = "x", LastName = "y", NationalCode = "0499370899", DailyWage = 800000m, HireDate = new DateTime(2020, 1, 1) };
                uow.Set<FnEmployee>().Add(employee);
                uow.Set<FnEmployeeChange>().Add(new FnEmployeeChange
                {
                    Employee = employee, Kind = FnEmployeeChangeKind.Update, Status = FnChangeStatus.Pending, Reason = reason, ProposedByUserId = 4, ProposedAt = DateTimeOffset.Now,
                    ProposedJson = "{\"DailyWage\":9876543,\"ExemptionFlags\":\"Veteran\",\"ChildrenCount\":1,\"HardAndHarmful\":false,\"BankIban\":\"IR000\"}", WageEffectiveFrom = new DateTime(2026, 8, 23),
                });
                uow.SaveChanges();
                FnAuditLog row = Trail(uow, "FnEmployeeChange").Last(a => a.AfterJson.Contains(reason));
                Assert.IsFalse(row.AfterJson.Contains("9876543") || row.AfterJson.Contains("Veteran") || row.AfterJson.Contains("IR000"));
                StringAssert.Contains(row.AfterJson, "\"ProposedJsonHash\"");
                StringAssert.Contains(row.AfterJson, "\"ProposedJsonKeys\":\"BankIban,ChildrenCount,DailyWage,ExemptionFlags,HardAndHarmful\"");
            });
        }

        // ------------------------------------------------------------------------------------------ devices, shifts, leave, policy, month close

        [TestMethod]
        public void The_Device_Key_Is_Never_In_The_Trail_And_A_Push_Time_Is_Not_A_Change()
        {
            InScope(uow =>
            {
                string code = Unique("D");
                var device = new OfDevice { Code = code, Title = "gate", Location = "main", ApiKeyHash = "SECRETKEYHASH", IsActive = true };
                uow.Set<OfDevice>().Add(device);
                uow.SaveChanges();
                FnAuditLog added = Trail(uow, "OfDevice").Last(a => a.Action == "Added" && a.AfterJson.Contains(code));
                Assert.IsFalse(added.AfterJson.Contains("SECRETKEYHASH") || added.AfterJson.Contains("ApiKeyHash"));
                StringAssert.Contains(added.AfterJson, "\"IsActive\":true");

                int rows = Trail(uow, "OfDevice").Count;
                device.LastPushAt = DateTimeOffset.Now;   // moves with every push: the push log is its trail
                uow.SaveChanges();
                Assert.AreEqual(rows, Trail(uow, "OfDevice").Count);

                device.ApiKeyHash = "NEWSECRET";
                device.KeyRotatedAt = DateTimeOffset.Now;
                device.KeyRotatedByUserId = 7;
                uow.SaveChanges();
                FnAuditLog rotated = Trail(uow, "OfDevice").Last(a => a.Action == "Modified");
                Assert.IsFalse(rotated.AfterJson.Contains("NEWSECRET") || rotated.BeforeJson.Contains("SECRETKEYHASH"));
                StringAssert.Contains(rotated.AfterJson, "\"KeyRotatedByUserId\":7");
            });
        }

        [TestMethod]
        public void Shifts_Assignments_Leave_Requests_The_Policy_And_The_Month_Close_Are_Audited()
        {
            InScope(uow =>
            {
                string title = Unique("shift");
                uow.Set<OfShift>().Add(new OfShift { Title = title, StartMinute = 480, EndMinute = 960, BreakMinutes = 30 });
                uow.Set<OfShiftAssignment>().Add(new OfShiftAssignment { EmployeeId = 1, ShiftId = 1, FromDate = new DateTime(2026, 8, 1), CreatedByUserId = 4, Reason = "late correction" });
                string number = Unique("LV");
                uow.Set<OfLeaveRequest>().Add(new OfLeaveRequest { Number = number, EmployeeId = 1, Kind = OfLeaveKind.DailyLeave, FromDate = new DateTime(2026, 8, 10), ToDate = new DateTime(2026, 8, 11), Days = 2m, Reason = "private medical reason" });
                uow.Set<OfAttendancePolicy>().Add(new OfAttendancePolicy { GraceMinutes = 15, OvertimeRequiresApproval = true, EarlyLeaveDeductionDisabled = true });
                uow.Set<OfAttendanceMonth>().Add(new OfAttendanceMonth { Year = 1405 + new Random().Next(1, 50), Month = 8, Status = OfMonthStatus.Prepared, ClosedByUserId = 4, SnapshotJson = "[{\"e\":1,\"d\":26.00}]", SnapshotHash = "abc123" });
                uow.SaveChanges();

                Assert.IsTrue(Trail(uow, "OfShift").Any(a => a.AfterJson.Contains(title)));
                FnAuditLog assignment = Trail(uow, "OfShiftAssignment").Last();
                StringAssert.Contains(assignment.AfterJson, "late correction");
                FnAuditLog leave = Trail(uow, "OfLeaveRequest").Last(a => a.AfterJson.Contains(number));
                Assert.IsFalse(leave.AfterJson.Contains("private medical reason"), "the reason of a leave request may be medical and is not copied");
                FnAuditLog policy = Trail(uow, "OfAttendancePolicy").Last();
                StringAssert.Contains(policy.AfterJson, "\"EarlyLeaveDeductionDisabled\":true");
                StringAssert.Contains(policy.AfterJson, "\"OvertimeRequiresApproval\":true");
                FnAuditLog month = Trail(uow, "OfAttendanceMonth").Last();
                Assert.IsFalse(month.AfterJson.Contains("SnapshotJson") || month.AfterJson.Contains("26.00"), "the snapshot lists every employee's attendance and is not copied");
                StringAssert.Contains(month.AfterJson, "\"SnapshotHash\":\"abc123\"");
                StringAssert.Contains(month.AfterJson, "\"Status\":\"Prepared\"");
                StringAssert.Contains(month.Summary, "1405".Substring(0, 2));
            });
        }

        // ------------------------------------------------------------------------------------------ cost

        [TestMethod]
        public void Audit_Cost_Of_The_R1f_Records_Is_One_Small_Row_Per_Change_And_None_For_Punches()
        {
            InScope(uow =>
            {
                const int Punches = 2000;
                DateTime start = new DateTime(2026, 8, 1, 8, 0, 0);

                // 2,000 device punches: the push log is the trail, so the audit trail does not grow
                int auditBefore = uow.Set<FnAuditLog>().Count();
                for (int i = 0; i < Punches; i++) uow.Set<OfClockEvent>().Add(new OfClockEvent { EmployeeId = 1 + i % 50, Timestamp = start.AddMinutes(i), Source = OfClockSource.Device, DeviceCode = "D1", Approval = OfClockApproval.Approved });
                var deviceTimer = Stopwatch.StartNew();
                uow.SaveChanges();
                deviceTimer.Stop();
                Assert.AreEqual(auditBefore, uow.Set<FnAuditLog>().Count(), "device punches add no audit row");

                // 2,000 manual requests: one row each
                auditBefore = uow.Set<FnAuditLog>().Count();
                for (int i = 0; i < Punches; i++) uow.Set<OfClockEvent>().Add(new OfClockEvent { EmployeeId = 1 + i % 50, Timestamp = start.AddDays(40).AddMinutes(i), Source = OfClockSource.Manual, Reason = "r", CreatedByUserId = 5, Approval = OfClockApproval.Pending });
                var manualTimer = Stopwatch.StartNew();
                uow.SaveChanges();
                manualTimer.Stop();
                List<FnAuditLog> rows = uow.Set<FnAuditLog>().Where(a => a.EntityType == "OfClockEvent").ToList().Skip(auditBefore).ToList();
                int added = uow.Set<FnAuditLog>().Count() - auditBefore;
                Assert.AreEqual(Punches, added);
                double averageBytes = uow.Set<FnAuditLog>().OrderByDescending(a => a.Id).Take(Punches).ToList().Average(a => (a.AfterJson?.Length ?? 0) + (a.BeforeJson?.Length ?? 0) + (a.Summary?.Length ?? 0));

                // a month close of 200 employees: 200 sheets, each with one row
                auditBefore = uow.Set<FnAuditLog>().Count();
                for (int i = 0; i < 200; i++)
                    uow.Set<FnAttendanceSheet>().Add(new FnAttendanceSheet { EmployeeId = 1000 + i, Year = 1500, Month = 1, DaysWorked = 26m, OvertimeHours = 4m, Source = FnSheetSource.HrClose, Locked = true, InputsJson = "{\"BONUS\":1}" });
                var sheetTimer = Stopwatch.StartNew();
                uow.SaveChanges();
                sheetTimer.Stop();
                Assert.AreEqual(200, uow.Set<FnAuditLog>().Count() - auditBefore);

                // the same number of unaudited rows for the base line
                var plainTimer = Stopwatch.StartNew();
                for (int i = 0; i < Punches; i++) uow.Set<OfAttendanceDay>().Add(new OfAttendanceDay { EmployeeId = 1 + i % 50, Date = start.AddDays(100 + i / 50), Status = OfDayStatus.Present });
                uow.SaveChanges();
                plainTimer.Stop();

                TestContext.WriteLine($"R1f audit cost (in-memory database, {Punches} rows per run): device punches {deviceTimer.ElapsedMilliseconds} ms and 0 audit rows; manual requests {manualTimer.ElapsedMilliseconds} ms and {added} audit rows of about {averageBytes:0} characters; " +
                    $"200 locked sheets {sheetTimer.ElapsedMilliseconds} ms and 200 audit rows; {Punches} unaudited day rows {plainTimer.ElapsedMilliseconds} ms.");
                Console.WriteLine($"R1f audit cost: device {deviceTimer.ElapsedMilliseconds} ms / 0 rows; manual {manualTimer.ElapsedMilliseconds} ms / {added} rows / ~{averageBytes:0} chars each; sheets {sheetTimer.ElapsedMilliseconds} ms / 200 rows; plain {plainTimer.ElapsedMilliseconds} ms");
                Assert.IsTrue(averageBytes < 600, "a manual request row stays small: " + averageBytes);
            });
        }
    }
}
