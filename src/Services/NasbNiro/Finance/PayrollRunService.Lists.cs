using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t D6: the insurance list and the tax list as the two offices ask for them, built from the same payslips as <see cref="PayrollReports"/> (the screens keep their columns).
    /// Only the packages of the Sepidar outbox use these tables; they are kept in this file so the payroll run service itself is not touched.
    /// Both layouts are marked Verify: the column lists follow the lists of the social-security office and the tax office as the finance team described them and have to be checked against the offices' current
    /// specifications before the first official submission.
    /// </summary>
    internal static class PayrollListsR1
    {
        public const string InsuranceLayoutNote = "Verify: ستون‌ها و ترتیب لیست بیمه بر پایه‌ی فایل‌های کارگاه و کارکنان نرم‌افزار لیست بیمه تامین اجتماعی است و باید پیش از نخستین ارسال رسمی با مشخصات فعلی سازمان تطبیق داده شود. روزهای بیمه عدد صحیح بر مبنای ۳۰ روز است و کسر تاخیر، روز بیمه را کم نمی‌کند.";

        public const string TaxLayoutNote = "Verify: ستون‌های لیست حقوق مالیاتی (کد ملی، ماه، ناخالص، معافیت‌ها، مشمول مالیات، مالیات) باید با فرم رسمی سازمان امور مالیاتی تطبیق داده شود. معافیت‌ها = ناخالص منهای مشمول مالیات (اقلام غیرمشمول، سهم بیمه کارگر و معافیت‌های کارمند).";

        public static readonly string[] WorkshopColumns =
        {
            "WorkshopCode", "WorkshopName", "EmployerName", "Address", "Branch", "Year", "Month", "Employees", "TotalDays", "TotalDailyWage", "TotalMonthlyWage", "TotalBenefits",
            "TotalInsurable", "TotalEmployeeShare", "TotalEmployerShare", "TotalUnemployment", "TotalHardAndHarmful",
        };

        public static readonly string[] WorkerColumns =
        {
            "RowNumber", "PersonnelCode", "InsuranceNumber", "NationalCode", "IdentityCardNumber", "FirstName", "LastName", "FatherName", "BirthDate", "Gender", "Nationality", "JobCode",
            "ContractStart", "ContractEnd", "Days", "DailyWage", "MonthlyWage", "Benefits", "InsurableTotal", "InsurableCapped", "EmployeeShare", "EmployerShare", "Unemployment", "HardAndHarmful",
        };

        public static readonly string[] TaxSummaryColumns = { "Year", "Month", "Employees", "TotalGross", "TotalExemptions", "TotalTaxable", "TotalTax" };

        public static readonly string[] TaxRowColumns =
        {
            "RowNumber", "PersonnelCode", "NationalCode", "FirstName", "LastName", "Year", "Month", "Gross", "Exemptions", "EmployeeInsurance", "Taxable", "Tax",
        };

        /// <summary>
        /// The insured days of the social-security list: whole days on the 30-day basis. The payslip keeps the paid days as a fraction because the lateness deduction is taken in parts of a day;
        /// the fraction never lowers the insured days, so the paid days are rounded up (30 at the most, 0 at the least).
        /// </summary>
        public static int InsuredDays(decimal daysPaid) => (int)Math.Max(0m, Math.Min(30m, Math.Ceiling(daysPaid)));

        public static string GenderWord(int gender) => gender == 1 ? "مرد" : gender == 2 ? "زن" : null;

        /// <summary>The code of the gender word in the text layout (1 male, 2 female, blank when unknown).</summary>
        public static string GenderCode(string word) => word == "مرد" ? "1" : word == "زن" ? "2" : string.Empty;

        private static string Date(DateTime? date) => date.HasValue ? PersianDate(date.Value) : null;

        /// <summary>The "Workshop" header table, the "Workers" table and the "Layout" note of an insurance list package.</summary>
        public static async Task<List<OutboxTable>> InsuranceTablesAsync(IUnitOfWork uow, int year, int month, int? workshopId, CancellationToken ct)
        {
            InsuranceListDto list = await PayrollReports.InsuranceListAsync(uow, year, month, workshopId, ct);
            List<string> codes = list.Rows.Select(r => r.PersonnelCode).ToList();
            Dictionary<string, FnEmployee> employees = (await uow.Set<FnEmployee>().AsNoTracking().Where(e => codes.Contains(e.PersonnelCode)).ToListAsync(ct)).ToDictionary(e => e.PersonnelCode, StringComparer.Ordinal);
            FnInsuranceWorkshop workshop = workshopId.HasValue ? await uow.Set<FnInsuranceWorkshop>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == workshopId.Value, ct) : null;

            var workers = new OutboxTable { Name = "Workers", Columns = WorkerColumns.ToList() };
            int number = 0, totalDays = 0;
            foreach (InsuranceListRowDto r in list.Rows)
            {
                employees.TryGetValue(r.PersonnelCode, out FnEmployee e);
                int days = InsuredDays(r.DaysWorked);
                totalDays += days;
                workers.Rows.Add(new List<object>
                {
                    ++number, r.PersonnelCode, r.InsuranceNumber, r.NationalCode, e?.IdentityCardNumber, r.FirstName, r.LastName, r.FatherName, Date(r.BirthDate), e == null ? null : GenderWord(e.Gender), e?.Nationality, r.JobCode,
                    Date(e?.HireDate), Date(e?.EndDate), days, r.DailyWage, r.MonthlyWage, r.Benefits, r.InsurableTotal, r.InsurableCapped, r.EmployeeShare, r.EmployerShare, r.Unemployment, r.HardAndHarmful,
                });
            }

            var header = new OutboxTable { Name = "Workshop", Columns = WorkshopColumns.ToList() };
            header.Rows.Add(new List<object>
            {
                list.WorkshopCode, list.WorkshopName, workshop?.EmployerName, workshop?.Address, workshop?.Branch, list.Year, list.Month, list.Employees, totalDays, list.Rows.Sum(r => r.DailyWage),
                list.Rows.Sum(r => r.MonthlyWage), list.Rows.Sum(r => r.Benefits), list.TotalInsurable, list.TotalEmployeeShare, list.TotalEmployerShare, list.TotalUnemployment, list.Rows.Sum(r => r.HardAndHarmful),
            });
            return new List<OutboxTable> { header, workers, LayoutTable("InsuranceList", InsuranceLayoutNote) };
        }

        /// <summary>The "Summary" table, the "Employees" table (one row for each employee of the month) and the "Layout" note of a tax list package.</summary>
        public static async Task<List<OutboxTable>> TaxTablesAsync(IUnitOfWork uow, int year, int month, CancellationToken ct)
        {
            List<FnPayslip> slips = await uow.Set<FnPayslip>().AsNoTracking().Where(p => p.Run.Year == year && p.Run.Month == month && p.Run.Status >= FnPayrollStatus.Approved)
                .Include(p => p.Employee).ToListAsync(ct);
            var rows = new OutboxTable { Name = "Employees", Columns = TaxRowColumns.ToList() };
            decimal totalGross = 0, totalExempt = 0, totalTaxable = 0, totalTax = 0;
            int number = 0;
            foreach (IGrouping<int, FnPayslip> g in slips.GroupBy(p => p.EmployeeId).OrderBy(g => g.First().Employee.PersonnelCode, StringComparer.Ordinal))
            {
                FnEmployee e = g.First().Employee;
                decimal gross = g.Sum(p => p.GrossEarnings), taxable = g.Sum(p => p.TaxableTotal), tax = g.Sum(p => p.Tax), insurance = g.Sum(p => p.EmployeeInsurance);
                decimal exemptions = Math.Max(0m, gross - taxable);
                rows.Rows.Add(new List<object> { ++number, e.PersonnelCode, e.NationalCode, e.FirstName, e.LastName, year, month, gross, exemptions, insurance, taxable, tax });
                totalGross += gross; totalExempt += exemptions; totalTaxable += taxable; totalTax += tax;
            }
            var summary = new OutboxTable { Name = "Summary", Columns = TaxSummaryColumns.ToList() };
            summary.Rows.Add(new List<object> { year, month, rows.Rows.Count, totalGross, totalExempt, totalTaxable, totalTax });
            return new List<OutboxTable> { summary, rows, LayoutTable("TaxList", TaxLayoutNote) };
        }

        private static OutboxTable LayoutTable(string item, string note)
        {
            var table = new OutboxTable { Name = "Layout", Columns = new List<string> { "Item", "Status", "Note" } };
            table.Rows.Add(new List<object> { item, "Verify", note });
            return table;
        }
    }
}
