using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Entities.Model.NasbNiro.Ai;
using Services.NasbNiro.Ai;

namespace MsTests
{
    /// <summary>Release A1 part B: schedules, due computation, the finance rules, the evaluation facts and the seat identity.</summary>
    [TestClass]
    public class AiSeatTests
    {
        // ------------------------------------------------------------------------------------------ schedule

        [TestMethod]
        public void Schedule_Parses_The_Three_Forms()
        {
            AiSchedule d = AiSchedule.Parse("daily 06:30");
            Assert.AreEqual(AiScheduleKind.Daily, d.Kind);
            Assert.AreEqual(new TimeSpan(6, 30, 0), d.Time);
            AiSchedule w = AiSchedule.Parse("weekly 5 07:00");
            Assert.AreEqual(AiScheduleKind.Weekly, w.Kind);
            Assert.AreEqual(5, w.PersianWeekday);
            Assert.AreEqual(new TimeSpan(7, 0, 0), w.Time);
            AiSchedule p = AiSchedule.Parse("poll 15");
            Assert.AreEqual(AiScheduleKind.Poll, p.Kind);
            Assert.AreEqual(15, p.PollMinutes);
            Assert.AreEqual("هر هفته پنجشنبه ساعت 07:00", w.Describe());
            Assert.AreEqual("هر 15 دقیقه", p.Describe());
            Assert.AreEqual("هر روز ساعت 06:30", d.Describe());
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("daily")]
        [DataRow("daily 24:00")]
        [DataRow("daily 7")]
        [DataRow("weekly 7 07:00")]
        [DataRow("weekly 5")]
        [DataRow("poll 0")]
        [DataRow("poll x")]
        [DataRow("monthly 1 07:00")]
        public void Schedule_Refuses_Bad_Text(string text)
        {
            Assert.IsNull(AiSchedule.Parse(text));
        }

        [TestMethod]
        public void Persian_Weekday_Index_Starts_On_Saturday()
        {
            Assert.AreEqual(0, AiSchedule.PersianIndex(DayOfWeek.Saturday));
            Assert.AreEqual(5, AiSchedule.PersianIndex(DayOfWeek.Thursday));
            Assert.AreEqual(6, AiSchedule.PersianIndex(DayOfWeek.Friday));
        }

        [TestMethod]
        public void Daily_Seat_Runs_Once_After_Its_Moment()
        {
            AiSchedule s = AiSchedule.Parse("daily 07:00");
            var day = new DateTime(2026, 10, 25);
            // before the moment of today the latest moment is yesterday's, which was made up
            Assert.AreEqual(day.AddDays(-1).AddHours(7), s.LatestSlot(day.AddHours(6).AddMinutes(59)));
            Assert.IsFalse(s.IsDue(day.AddHours(6).AddMinutes(59), day.AddDays(-1).AddHours(7).AddMinutes(1), day.AddDays(-1).AddHours(7).AddMinutes(1), 5));
            // the moment has come and nothing ran today
            Assert.IsTrue(s.IsDue(day.AddHours(7), day.AddDays(-1).AddHours(7).AddMinutes(1), day.AddDays(-1).AddHours(7).AddMinutes(1), 5));
            Assert.IsTrue(s.IsDue(day.AddHours(7), null, null, 5));
            // it ran at 07:01: not again
            Assert.IsFalse(s.IsDue(day.AddHours(9), day.AddHours(7).AddMinutes(1), day.AddHours(7).AddMinutes(1), 5));
            // the server was down at 07:00: the run is made up at the first tick
            Assert.IsTrue(s.IsDue(day.AddHours(13), day.AddDays(-1).AddHours(7), day.AddDays(-1).AddHours(7), 5));
        }

        [TestMethod]
        public void A_Daily_Seat_That_Did_Not_Succeed_Is_Tried_Again_After_The_Retry_Time()
        {
            AiSchedule s = AiSchedule.Parse("daily 07:00");
            var day = new DateTime(2026, 10, 25);
            DateTime failed = day.AddHours(7).AddMinutes(1);
            DateTime? yesterdayOk = day.AddDays(-1).AddHours(7).AddMinutes(1);
            Assert.IsFalse(s.IsDue(failed.AddMinutes(4), failed, yesterdayOk, 5));
            Assert.IsTrue(s.IsDue(failed.AddMinutes(5), failed, yesterdayOk, 5));
            // after a success the retry stops
            Assert.IsFalse(s.IsDue(failed.AddMinutes(30), failed.AddMinutes(6), failed.AddMinutes(6), 5));
        }

        [TestMethod]
        public void Weekly_Seat_Is_Due_After_Its_Day_Only()
        {
            AiSchedule s = AiSchedule.Parse("weekly 5 07:00"); // Thursday 07:00
            var thursday = new DateTime(2026, 10, 22);
            Assert.AreEqual(DayOfWeek.Thursday, thursday.DayOfWeek);
            DateTime lastOk = new DateTime(2026, 10, 15, 7, 2, 0);
            Assert.IsFalse(s.IsDue(thursday.AddHours(6), lastOk, lastOk, 5));
            Assert.IsTrue(s.IsDue(thursday.AddHours(7), lastOk, lastOk, 5));
            Assert.IsTrue(s.IsDue(thursday.AddDays(1).AddHours(10), lastOk, lastOk, 5));
            DateTime thisWeek = thursday.AddHours(7).AddMinutes(3);
            Assert.IsFalse(s.IsDue(thursday.AddDays(3), thisWeek, thisWeek, 5));
            Assert.IsFalse(s.IsDue(thursday.AddDays(6).AddHours(6), thisWeek, thisWeek, 5));
            Assert.IsTrue(s.IsDue(thursday.AddDays(7).AddHours(7), thisWeek, thisWeek, 5));
        }

        [TestMethod]
        public void Poll_Seat_Runs_Every_N_Minutes_From_The_Last_Attempt()
        {
            AiSchedule s = AiSchedule.Parse("poll 15");
            var now = new DateTime(2026, 10, 25, 10, 0, 0);
            Assert.IsTrue(s.IsDue(now, null, null, 5));
            Assert.IsFalse(s.IsDue(now, now.AddMinutes(-14), null, 5));
            Assert.IsTrue(s.IsDue(now, now.AddMinutes(-15), null, 5));
            // a skipped or failed attempt counts as an attempt: the poll interval is kept
            Assert.IsFalse(s.IsDue(now, now.AddMinutes(-3), now.AddHours(-2), 5));
        }

        // ------------------------------------------------------------------------------------------ catalogue and identity

        [TestMethod]
        public void Catalogue_Has_The_Seven_Seats_With_Valid_Schedules()
        {
            CollectionAssert.AreEqual(
                new[] { "CEO-BRIEF", "PMO-WEEKLY", "WH-REORDER", "PROC-COMPARE", "QC-NCR-ASSIST", "FIN-ANOMALY", "OFFICE-DRAFT" },
                AiSeatCatalog.All.Select(s => s.Code).ToArray());
            foreach (AiSeatDefinition s in AiSeatCatalog.All)
            {
                Assert.IsNotNull(AiSchedule.Parse(s.Schedule), s.Code);
                Assert.IsTrue(s.Roles.Length > 0 && s.Roles.All(r => r == r.ToUpperInvariant()), s.Code);
                Assert.IsTrue(s.UserName.StartsWith("seat.") && s.UserName == s.UserName.ToLowerInvariant(), s.Code);
            }
            Assert.AreEqual("seat.wh-reorder", AiSeatCatalog.Find("WH-REORDER").UserName);
            CollectionAssert.AreEqual(new[] { "CEO" }, AiSeatCatalog.EvalUser.Roles);
            Assert.AreEqual("seat.eval", AiSeatCatalog.EvalUser.UserName);
            // the seats that only draft or note never carry a signing or paying role
            CollectionAssert.DoesNotContain(AiSeatCatalog.Find("WH-REORDER").Roles, "CEO");
            CollectionAssert.AreEqual(new[] { "SECRETARIAT" }, AiSeatCatalog.Find("OFFICE-DRAFT").Roles);
            // the least roles: the reorder seat is a warehouse writer only, the finance seat reads the books as FINANCIAL only (not as the finance manager who decides its notes)
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, AiSeatCatalog.Find("WH-REORDER").Roles);
            CollectionAssert.AreEqual(new[] { "FINANCIAL" }, AiSeatCatalog.Find("FIN-ANOMALY").Roles);
        }

        [TestMethod]
        public void Seat_Names_Are_Recognised_In_Any_Case()
        {
            Assert.IsTrue(AiSeatNames.IsSeatUser("seat.eval"));
            Assert.IsTrue(AiSeatNames.IsSeatUser("  SEAT.Ceo-Brief "));
            Assert.IsFalse(AiSeatNames.IsSeatUser("seatbelt"));
            Assert.IsFalse(AiSeatNames.IsSeatUser("admin"));
            Assert.IsFalse(AiSeatNames.IsSeatUser(null));
        }

        [TestMethod]
        public async Task Acting_Run_Has_A_Context_Of_Its_Own_And_Leaves_The_Callers_Untouched()
        {
            var services = new ServiceCollection();
            services.AddHttpContextAccessor();
            using ServiceProvider provider = services.BuildServiceProvider();
            var accessor = provider.GetRequiredService<IHttpContextAccessor>();
            var callerUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "PMO") }, "Test"));
            accessor.HttpContext = new DefaultHttpContext { User = callerUser };

            var seat = new AiActingUser { UserId = 99, UserName = "seat.wh-reorder", Roles = new[] { "WAREHOUSE", "PLANNING" } };
            var seen = await AiActing.RunAsync(provider.GetRequiredService<IServiceScopeFactory>(), seat, "WH-REORDER", sp =>
            {
                var inner = sp.GetRequiredService<IHttpContextAccessor>().HttpContext.User;
                return Task.FromResult((Id: inner.FindFirst(ClaimTypes.NameIdentifier).Value, Name: inner.Identity.Name, Warehouse: inner.IsInRole("WAREHOUSE"), Pmo: inner.IsInRole("PMO"),
                    Code: AiSeatContext.Code, Acting: AiActing.IsActing));
            });

            Assert.AreEqual("99", seen.Id);
            Assert.AreEqual("seat.wh-reorder", seen.Name);
            Assert.IsTrue(seen.Warehouse);
            Assert.IsFalse(seen.Pmo, "the seat has only its own roles");
            Assert.AreEqual("WH-REORDER", seen.Code);
            Assert.IsTrue(seen.Acting);

            // the caller's request is as it was
            Assert.AreSame(callerUser, accessor.HttpContext.User);
            Assert.IsTrue(accessor.HttpContext.User.IsInRole("PMO"));
            Assert.IsNull(AiSeatContext.Code);
            Assert.IsFalse(AiActing.IsActing);
        }

        // ------------------------------------------------------------------------------------------ notes

        [TestMethod]
        public void Note_Roles_Follow_The_Module_Of_The_Target()
        {
            Assert.AreEqual("PMO,QC", AiNoteTypes.Roles(AiNoteTypes.QualityNcr));
            foreach (string t in new[] { AiNoteTypes.FinanceJournal, AiNoteTypes.BankLine, AiNoteTypes.SupplierIbanChange })
                Assert.AreEqual("FINANCEMANAGER,FINANCIAL", AiNoteTypes.Roles(t));
            Assert.AreEqual("PMO,PLANNING", AiNoteTypes.Roles(AiNoteTypes.PcWeeklyReport));
            Assert.AreEqual("CEO", AiNoteTypes.Roles(AiNoteTypes.CeoBrief)); // written as the CEO: only the CEO reads it
            Assert.IsTrue(AiNoteTypes.Roles(AiNoteTypes.PurchaseRfq).Split(',').Contains("COMMERCIAL"));
            // the office types are decided by the office service
            Assert.IsNull(AiNoteTypes.Roles(AiNoteTypes.OfficeLetter));
            Assert.IsNull(AiNoteTypes.Roles(AiNoteTypes.OfficeMeeting));
            Assert.IsTrue(AiNoteTypes.IsOffice(AiNoteTypes.OfficeLetter) && AiNoteTypes.IsOffice(AiNoteTypes.OfficeMeeting) && !AiNoteTypes.IsOffice(AiNoteTypes.QualityNcr));
        }

        [TestMethod]
        public void Brief_Date_Is_Read_From_The_Target_Id()
        {
            Assert.AreEqual(new DateTime(2026, 10, 15), AiNoteService.BriefDate(20261015));
            Assert.IsNull(AiNoteService.BriefDate(20261315));
            Assert.IsNull(AiNoteService.BriefDate(null));
            Assert.IsNull(AiNoteService.BriefDate(12));
        }

        [TestMethod]
        public void Reorder_Quantity_Is_Up_To_The_Minimum()
        {
            Assert.AreEqual(7m, WhReorderSeatJob.ReorderQuantity(10, 3));
            Assert.AreEqual(0m, WhReorderSeatJob.ReorderQuantity(10, 10));
            Assert.AreEqual(0m, WhReorderSeatJob.ReorderQuantity(10, 12));
        }

        [TestMethod]
        public void Reorder_Quantity_Leaves_Out_What_Is_Already_On_Order()
        {
            Assert.AreEqual(4m, WhReorderSeatJob.ReorderQuantity(10, 3, 3));
            Assert.AreEqual(0m, WhReorderSeatJob.ReorderQuantity(10, 3, 7));
            Assert.AreEqual(0m, WhReorderSeatJob.ReorderQuantity(10, 3, 20));
            Assert.AreEqual(7m, WhReorderSeatJob.ReorderQuantity(10, 3, -5)); // a negative figure is never added
        }

        [TestMethod]
        public void Labour_Cost_Is_Masked_Inside_A_Seat_Run_Even_For_The_Ceo()
        {
            Assert.IsTrue(Services.NasbNiro.AfterSales.SvRoles.SeesPayroll(true));
            Assert.IsFalse(Services.NasbNiro.AfterSales.SvRoles.SeesPayroll(false));
            var scopes = new ServiceCollection().AddHttpContextAccessor().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            // the evaluation user acts as the CEO, but what it writes is read by other people
            var ceo = new AiActingUser { UserId = 5, UserName = "seat.eval", Roles = new[] { "CEO" } };
            bool inside = AiActing.RunAsync(scopes, ceo, "EVAL", sp => Task.FromResult(Services.NasbNiro.AfterSales.SvRoles.SeesPayroll(true))).GetAwaiter().GetResult();
            Assert.IsFalse(inside);
            Assert.IsTrue(Services.NasbNiro.AfterSales.SvRoles.SeesPayroll(true)); // and outside the run nothing changed
        }

        [TestMethod]
        public void Skipped_Run_Backs_Off_Only_When_The_Model_Was_Down()
        {
            DateTime now = new DateTime(2026, 10, 25, 10, 0, 0);
            string down = AiSeatTexts.LlmDown + ": هیچ نقطه‌ی پایانی";
            Assert.IsTrue(AiSeatRunner.IsBackedOff(AiSeatRunStatus.Skipped, down, now.AddMinutes(-10), now, 30));
            Assert.IsFalse(AiSeatRunner.IsBackedOff(AiSeatRunStatus.Skipped, down, now.AddMinutes(-31), now, 30));
            Assert.IsFalse(AiSeatRunner.IsBackedOff(AiSeatRunStatus.Skipped, AiSeatTexts.Cancelled, now.AddMinutes(-1), now, 30));
            Assert.IsFalse(AiSeatRunner.IsBackedOff(AiSeatRunStatus.Failed, down, now.AddMinutes(-1), now, 30));
            Assert.IsFalse(AiSeatRunner.IsBackedOff(null, null, null, now, 30));
        }

        [TestMethod]
        public void Weekly_Digest_Reads_The_Report_Figures()
        {
            string json = "{\"Progress\":{},\"Evm\":{\"PhysicalPercent\":41.26,\"PlannedPercent\":50,\"Spi\":0.82,\"Cpi\":1.05},\"LateActivities\":[\"A1\",\"A2\"],\"OpenChanges\":3,\"GuaranteeAlerts\":0}";
            string digest = PmoWeeklySeatJob.Digest(json);
            StringAssert.Contains(digest, "پیشرفت واقعی 41.3 درصد");
            StringAssert.Contains(digest, "پیشرفت برنامه‌ای 50 درصد");
            StringAssert.Contains(digest, "SPI 0.82");
            StringAssert.Contains(digest, "فعالیت‌های عقب‌افتاده 2");
            StringAssert.Contains(digest, "تغییرهای باز 3");
            StringAssert.Contains(digest, "هشدار ضمانت‌نامه 0");
            Assert.AreEqual("محتوای گزارش خوانده نشد.", PmoWeeklySeatJob.Digest("{bad"));
        }

        [TestMethod]
        public void Similarity_Is_The_Share_Of_Common_Words()
        {
            var a = AiTextSimilarity.Words("ترک سطحی روی قطعه ریختگی");
            var b = AiTextSimilarity.Words("ترک سطحی در قطعه ریختگی دیگر");
            var c = AiTextSimilarity.Words("رنگ پوسته شده");
            Assert.IsTrue(AiTextSimilarity.Jaccard(a, b) > AiTextSimilarity.Jaccard(a, c));
            Assert.AreEqual(0.0, AiTextSimilarity.Jaccard(a, c));
            Assert.AreEqual(1.0, AiTextSimilarity.Jaccard(a, a));
            Assert.AreEqual(0.0, AiTextSimilarity.Jaccard(a, new HashSet<string>()));
        }

        // ------------------------------------------------------------------------------------------ finance rules

        private static readonly DateTime Today = new DateTime(2026, 10, 25); // a Sunday

        private static FinVoucherInput V(int id, DateTime date, decimal total, params FinLineInput[] lines) =>
            new FinVoucherInput { Id = id, Number = id * 10, Date = date, TotalAmount = total, Lines = lines.ToList() };

        private static FinLineInput L(int counterparty, decimal amount, bool credit = true) =>
            new FinLineInput { CounterpartyId = counterparty, CounterpartyTitle = "تامین‌کننده " + counterparty, Amount = amount, Credit = credit };

        private static bool Friday(DateTime d) => d.DayOfWeek == DayOfWeek.Friday;

        private static List<FinFinding> Analyze(IReadOnlyList<FinVoucherInput> vouchers, IReadOnlyList<FinBankLineInput> bank = null, IReadOnlyList<FinIbanChangeInput> iban = null) =>
            FinAnomalyRules.Analyze(Today, vouchers, bank ?? new List<FinBankLineInput>(), iban ?? new List<FinIbanChangeInput>(), Friday, 1_000_000_000m, 100_000_000m);

        [TestMethod]
        public void Duplicate_Amount_To_The_Same_Counterparty_Within_Seven_Days()
        {
            var vouchers = new List<FinVoucherInput>
            {
                V(1, Today.AddDays(-5), 250_000_000m, L(5, 250_000_000m)),
                V(2, Today.AddDays(-1), 250_000_000m, L(5, 250_000_000m)),
                V(3, Today.AddDays(-1), 250_000_000m, L(6, 250_000_000m)),            // another counterparty
                V(4, Today.AddDays(-2), 260_000_000m, L(5, 260_000_000m)),            // another amount
            };
            List<FinFinding> f = Analyze(vouchers).Where(x => x.Rule == "DUPLICATE").ToList();
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, f.Select(x => x.TargetId).Distinct().ToArray());
            Assert.IsTrue(f.All(x => x.TargetType == AiNoteTypes.FinanceJournal));
            StringAssert.Contains(f[0].Text, "250,000,000 ریال");
        }

        [TestMethod]
        public void Duplicate_Amount_Further_Apart_Than_Seven_Days_Is_Not_Reported()
        {
            var vouchers = new List<FinVoucherInput>
            {
                V(1, Today.AddDays(-20), 100m, L(5, 100m)),
                V(2, Today.AddDays(-12), 100m, L(5, 100m)),
            };
            Assert.AreEqual(0, Analyze(vouchers).Count(x => x.Rule == "DUPLICATE"));
            // a debit and a credit of the same amount are not a repeat
            var sides = new List<FinVoucherInput> { V(1, Today, 100m, L(5, 100m, true)), V(2, Today, 100m, L(5, 100m, false)) };
            Assert.AreEqual(0, Analyze(sides).Count(x => x.Rule == "DUPLICATE"));
        }

        [TestMethod]
        public void Round_Amount_Needs_The_Threshold_And_The_Unit()
        {
            var vouchers = new List<FinVoucherInput>
            {
                V(1, Today.AddDays(-1), 2_000_000_000m),   // round and large
                V(2, Today.AddDays(-1), 2_050_000_000m),   // large, not a multiple of 100,000,000
                V(3, Today.AddDays(-1), 900_000_000m),     // round, below the threshold
                V(4, Today.AddDays(-1), 1_000_000_000m),   // exactly the threshold
            };
            CollectionAssert.AreEqual(new[] { 1, 4 }, Analyze(vouchers).Where(x => x.Rule == "ROUND").Select(x => x.TargetId).OrderBy(x => x).ToArray());
        }

        [TestMethod]
        public void Posting_On_A_Friday_Or_A_Holiday()
        {
            DateTime friday = new DateTime(2026, 10, 23);
            Assert.AreEqual(DayOfWeek.Friday, friday.DayOfWeek);
            var vouchers = new List<FinVoucherInput> { V(1, friday, 10m), V(2, friday.AddDays(-1), 10m) };
            List<FinFinding> f = Analyze(vouchers).Where(x => x.Rule == "OFFDAY").ToList();
            Assert.AreEqual(1, f.Count);
            Assert.AreEqual(1, f[0].TargetId);
            StringAssert.Contains(f[0].Text, "جمعه");
            StringAssert.Contains(f[0].Text, "1405/08/01");
            // a holiday that is not a Friday
            var holiday = FinAnomalyRules.Analyze(Today, new List<FinVoucherInput> { V(9, Today.AddDays(-3), 10m) }, new List<FinBankLineInput>(), new List<FinIbanChangeInput>(), d => d == Today.AddDays(-3), 1e9m, 1e8m);
            Assert.AreEqual(1, holiday.Count(x => x.Rule == "OFFDAY"));
            StringAssert.Contains(holiday.First(x => x.Rule == "OFFDAY").Text, "تعطیل");
        }

        [TestMethod]
        public void Drafts_And_Reversal_Pairs_Are_Not_Duplicates()
        {
            var a = V(1, Today.AddDays(-1), 5_000m, L(3, 5_000m));
            var b = V(2, Today, 5_000m, L(3, 5_000m));
            Assert.AreEqual(2, Analyze(new List<FinVoucherInput> { a, b }).Count(x => x.Rule == "DUPLICATE"));
            b.ExcludeFromDuplicate = true; // a draft, a reversed voucher or its reversal
            Assert.AreEqual(0, Analyze(new List<FinVoucherInput> { a, b }).Count(x => x.Rule == "DUPLICATE"));
        }

        [TestMethod]
        public void Payroll_Sources_And_Employee_Details_Are_Recognised()
        {
            foreach (string s in new[] { "PayrollRun", "PayrollTermination", "BonusRun", "SeveranceX", "LeaveSettlement" }) Assert.IsTrue(FinPayroll.IsPayrollSource(s), s);
            foreach (string s in new[] { "SupplierInvoice", "FnAssetDisposal", null, "" }) Assert.IsFalse(FinPayroll.IsPayrollSource(s), s);
            var employee = new Entities.Model.NasbNiro.Finance.FnDetailAccount { Type = Entities.Model.NasbNiro.Finance.FnDetailType.Employee };
            var supplier = new Entities.Model.NasbNiro.Finance.FnDetailAccount { Type = Entities.Model.NasbNiro.Finance.FnDetailType.Supplier };
            Assert.IsTrue(FinPayroll.IsEmployee(employee));
            Assert.IsFalse(FinPayroll.IsEmployee(supplier) || FinPayroll.IsEmployee(null));
            CollectionAssert.Contains(FinPayroll.AccountRoles, "PayrollPayable");
            CollectionAssert.Contains(FinPayroll.AccountRoles, "BonusProvision");
        }

        [TestMethod]
        public void Supplier_Iban_Change_In_The_Last_Thirty_Days_Is_Reported_Without_The_Iban()
        {
            var changes = new List<FinIbanChangeInput>
            {
                new FinIbanChangeInput { AuditId = 101, ProviderId = 7, ProviderName = "فولاد نمونه", At = Today.AddDays(-30) },
                new FinIbanChangeInput { AuditId = 102, ProviderId = 7, ProviderName = "فولاد نمونه", At = Today.AddDays(-3) },
                new FinIbanChangeInput { AuditId = 103, ProviderId = 8, ProviderName = "قدیمی", At = Today.AddDays(-31) },
            };
            List<FinFinding> f = Analyze(new List<FinVoucherInput>(), null, changes).Where(x => x.Rule == "IBAN").ToList();
            // one note per change (two changes of one supplier are two notes), the target is the audit row of the change
            Assert.AreEqual(2, f.Count);
            Assert.IsTrue(f.All(x => x.TargetType == AiNoteTypes.SupplierIbanChange));
            CollectionAssert.AreEqual(new[] { 101, 102 }, f.Select(x => x.TargetId).ToArray());
            StringAssert.Contains(f[0].Text, "فولاد نمونه");
            StringAssert.Contains(f[0].Text, "1405/"); // the date is Persian
            Assert.IsFalse(f[0].Text.Contains("IR"));
            Assert.AreEqual(2, FinAnomalyRules.ToNotes(f).Count);
        }

        [TestMethod]
        public void Bank_Line_Unmatched_For_More_Than_Seven_Days()
        {
            var bank = new List<FinBankLineInput>
            {
                new FinBankLineInput { Id = 1, Date = Today.AddDays(-8), Amount = 5_000_000m, Withdrawal = true },
                new FinBankLineInput { Id = 2, Date = Today.AddDays(-7), Amount = 5_000_000m, Withdrawal = true },
                new FinBankLineInput { Id = 3, Date = Today.AddDays(-30), Amount = 1_500m, Withdrawal = false },
            };
            List<FinFinding> f = Analyze(new List<FinVoucherInput>(), bank).Where(x => x.Rule == "UNMATCHED").ToList();
            CollectionAssert.AreEqual(new[] { 1, 3 }, f.Select(x => x.TargetId).OrderBy(x => x).ToArray());
            Assert.IsTrue(f.All(x => x.TargetType == AiNoteTypes.BankLine));
            StringAssert.Contains(f.First(x => x.TargetId == 3).Text, "واریز");
        }

        [TestMethod]
        public void Findings_Of_One_Record_Become_One_Note_Per_Rule()
        {
            DateTime friday = new DateTime(2026, 10, 23);
            var v = V(1, friday, 2_000_000_000m);
            List<FinFinding> findings = Analyze(new List<FinVoucherInput> { v });
            Assert.AreEqual(2, findings.Count);
            List<FinNoteDraft> notes = FinAnomalyRules.ToNotes(findings, new Dictionary<int, FinVoucherInput> { { 1, v } });
            // one note per record AND rule: a later rule on the same voucher is not lost to the unique index
            Assert.AreEqual(2, notes.Count);
            Assert.IsTrue(notes.All(n => n.TargetType == AiNoteTypes.FinanceJournal && n.TargetId == 1));
            CollectionAssert.AreEqual(new[] { "OFFDAY", "ROUND" }, notes.Select(n => n.RuleCode).ToArray());
            StringAssert.Contains(notes[0].Title, "10");
            Assert.IsTrue(notes.All(n => n.Text.Split('•').Length - 1 == 1));
        }

        // ------------------------------------------------------------------------------------------ evaluation facts

        [TestMethod]
        public void Eval_Facts_Are_Split_On_Semicolons()
        {
            CollectionAssert.AreEqual(new[] { "tool:a.b", "text:x y" }, AiEvalFacts.Split(" tool:a.b ; text:x y ;; ").ToArray());
            Assert.AreEqual(0, AiEvalFacts.Split(null).Count);
            CollectionAssert.AreEqual(new[] { "a", "b" }, AiEvalFacts.SplitTools("a, b").ToArray());
        }

        [TestMethod]
        public void Eval_Tools_Rule()
        {
            Assert.IsTrue(AiEvalFacts.ToolsOk("ceo_dashboard", new[] { "ceo_dashboard" }));
            Assert.IsTrue(AiEvalFacts.ToolsOk("ceo_dashboard", new[] { "cash_position", "ceo_dashboard" }));
            Assert.IsFalse(AiEvalFacts.ToolsOk("ceo_dashboard", new[] { "cash_position" }));
            Assert.IsFalse(AiEvalFacts.ToolsOk("a,b", new[] { "a" }));
            Assert.IsTrue(AiEvalFacts.ToolsOk("none", new string[0]));
            Assert.IsFalse(AiEvalFacts.ToolsOk("none", new[] { "a" }));
        }

        [TestMethod]
        public void Eval_Path_Follows_Names_And_Indexes()
        {
            using JsonDocument doc = JsonDocument.Parse("{\"StockValue\":1500000,\"items\":[{\"code\":\"A1\",\"qty\":4.5},{\"code\":\"B2\"}],\"alerts\":{\"count\":2}}");
            JsonElement root = doc.RootElement;
            Assert.AreEqual(1500000, AiEvalFacts.ResolvePath(root, "stockValue").Value.GetInt32());
            Assert.AreEqual("B2", AiEvalFacts.ResolvePath(root, "items[1].code").Value.GetString());
            Assert.AreEqual(4.5m, AiEvalFacts.ResolvePath(root, "items[0].qty").Value.GetDecimal());
            Assert.AreEqual(2, AiEvalFacts.ResolvePath(root, "alerts.count").Value.GetInt32());
            Assert.IsNull(AiEvalFacts.ResolvePath(root, "items[2].code"));
            Assert.IsNull(AiEvalFacts.ResolvePath(root, "missing"));
            Assert.IsNull(AiEvalFacts.ResolvePath(root, "items[x]"));
            Assert.IsNull(AiEvalFacts.ResolvePath(root, "stockValue.deeper"));
        }

        [TestMethod]
        public void Eval_Tool_Fact_Syntax()
        {
            Assert.IsTrue(AiEvalFacts.TryParseToolFact("tool:ceo_dashboard.stockValue", out string tool, out string path));
            Assert.AreEqual("ceo_dashboard", tool);
            Assert.AreEqual("stockValue", path);
            Assert.IsTrue(AiEvalFacts.TryParseToolFact("tool:x.items[0].code", out tool, out path));
            Assert.AreEqual("items[0].code", path);
            Assert.IsFalse(AiEvalFacts.TryParseToolFact("tool:nopath", out _, out _));
            Assert.IsFalse(AiEvalFacts.TryParseToolFact("text:abc", out _, out _));
        }

        [TestMethod]
        public void Eval_Number_Is_Found_By_Its_Canonical_Form()
        {
            using JsonDocument doc = JsonDocument.Parse("{\"a\":1500000,\"b\":12.50,\"c\":\"فولاد\",\"d\":true}");
            JsonElement r = doc.RootElement;
            Assert.IsTrue(AiEvalFacts.InAnswer(r.GetProperty("a"), "ارزش ۱٬۵۰۰٬۰۰۰ ریال است").Ok);
            Assert.IsTrue(AiEvalFacts.InAnswer(r.GetProperty("a"), "1,500,000").Ok);
            Assert.IsTrue(AiEvalFacts.InAnswer(r.GetProperty("a"), "حدود 1.5 میلیون").Ok);
            Assert.IsFalse(AiEvalFacts.InAnswer(r.GetProperty("a"), "1,500,001").Ok);
            Assert.IsTrue(AiEvalFacts.InAnswer(r.GetProperty("b"), "مقدار 12.5").Ok);
            Assert.IsTrue(AiEvalFacts.InAnswer(r.GetProperty("c"), "تامین‌کننده فولاد").Ok);
            Assert.IsFalse(AiEvalFacts.InAnswer(r.GetProperty("c"), "مس").Ok);
            Assert.IsFalse(AiEvalFacts.InAnswer(r.GetProperty("d"), "درست").Ok);
            Assert.IsTrue(AiEvalFacts.TextInAnswer("text:پنج چرا", "روش پنج چرا").Ok);
            Assert.IsFalse(AiEvalFacts.TextInAnswer("text:پنج چرا", "روش دیگر").Ok);
        }

        [TestMethod]
        public void Eval_Percentiles_Use_The_Nearest_Rank()
        {
            int[] v = Enumerable.Range(1, 10).Select(x => x * 10).ToArray();
            Assert.AreEqual(50, AiEvalFacts.Percentile(v, 50));
            Assert.AreEqual(100, AiEvalFacts.Percentile(v, 95));
            Assert.AreEqual(7, AiEvalFacts.Percentile(new[] { 7 }, 95));
            Assert.AreEqual(0, AiEvalFacts.Percentile(new int[0], 50));
            Assert.AreEqual(10, AiEvalFacts.Percentile(v, 1));
        }

        // ------------------------------------------------------------------------------------------ the evaluation catalogue against the mock

        // The routing of .github/ci-tools/mock-llm/mock_llm.py, written again here so that the number of cases the mock passes is checked by a test.
        private static readonly Dictionary<string, string[]> MockFirstNumbers = new Dictionary<string, string[]>
        {
            { "ceo_dashboard", new[] { "stockValue", "itemsBelowMinimum", "quarantineLines" } },
            { "cash_position", new[] { "cash", "banks", "pettyCash" } },
            { "items_below_minimum", new[] { "totalCount" } },
            { "search_documents", new string[0] },
        };

        // the keywords of the seats, and (release D1) of the drills of the CEO dashboard and of the UI actions: no evaluation question may hold one, so the mock keeps routing them as before
        private static readonly string[] SeatWords =
        {
            "گزارش روزانه", "روایت هفتگی", "یادداشت مقایسه", "پیشنهاد دسته‌بندی", "پیش‌نویس پاسخ", "خلاصه صورت‌جلسه", "تکرار", "اجبار", "جعل",
            "فهرست جزئیات", "ریز", "تحلیل", "باز کن", "بخوان هشدارها", "بیرون",
        };

        private static string MockTool(string q)
        {
            if (q.Contains("داشبورد")) return "ceo_dashboard";
            if (q.Contains("نقدینگی")) return "cash_position";
            if (q.Contains("موجودی")) return "items_below_minimum";
            if (q.Contains("سند") || q.Contains("روش")) return "search_documents";
            return null;
        }

        private static bool MockPasses(AiEvalSeed c)
        {
            string tool = MockTool(c.Question);
            List<string> expected = AiEvalFacts.SplitTools(c.Tools);
            List<string> facts = AiEvalFacts.Split(c.Facts);
            if (tool == null) return expected.Count == 1 && expected[0] == "none" && facts.Count == 0;
            if (expected.Count != 1 || expected[0] != tool) return false;
            // the answer of the mock holds the first three numbers of the result and nothing else
            return facts.All(f => AiEvalFacts.TryParseToolFact(f, out string t, out string p) && t == tool && MockFirstNumbers[tool].Contains(p));
        }

        public const int ExpectedCases = 66;
        public const int ExpectedMockPasses = 27;

        [TestMethod]
        public void Eval_Catalogue_Is_Large_Enough_And_Well_Formed()
        {
            Assert.AreEqual(ExpectedCases, AiEvalCatalog.Cases.Count);
            Assert.IsTrue(AiEvalCatalog.Cases.Count >= 60);
            Assert.AreEqual(AiEvalCatalog.Cases.Count, AiEvalCatalog.Cases.Select(c => c.Code).Distinct().Count());
            Assert.AreEqual(AiEvalCatalog.Cases.Count, AiEvalCatalog.Cases.Select(c => c.Question).Distinct().Count());
            var known = new HashSet<string>(AiTools.Build().Select(t => t.Name));
            foreach (AiEvalSeed c in AiEvalCatalog.Cases)
            {
                Assert.IsTrue(c.Question.Length > 1 && c.Question.Length <= 200, c.Code);
                foreach (string t in AiEvalFacts.SplitTools(c.Tools)) Assert.IsTrue(t == "none" || known.Contains(t), c.Code + " " + t);
                foreach (string f in AiEvalFacts.Split(c.Facts))
                    Assert.IsTrue(f.StartsWith("text:") || (AiEvalFacts.TryParseToolFact(f, out string tool, out _) && known.Contains(tool)), c.Code + " " + f);
                // no number is typed into a case: facts are read from the tools
                foreach (string f in AiEvalFacts.Split(c.Facts)) Assert.IsFalse(f.StartsWith("text:") && f.Any(char.IsDigit), c.Code);
                foreach (string w in SeatWords) Assert.IsFalse(c.Question.Contains(w), c.Code + " uses the mock keyword " + w);
            }
        }

        [TestMethod]
        public void Eval_Mock_Passes_Exactly_The_Expected_Number_Of_Cases()
        {
            List<string> passed = AiEvalCatalog.Cases.Where(MockPasses).Select(c => c.Code).ToList();
            Assert.AreEqual(ExpectedMockPasses, passed.Count, string.Join(",", passed));
        }
    }
}
