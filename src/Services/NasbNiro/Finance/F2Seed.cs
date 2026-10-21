using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// Release F2 seed data: asset classes (without rates), payroll items (without amounts) and the few long-standing ratios.
    /// Statutory amounts (minimum wage, allowances, insurance ceiling, tax brackets, depreciation rates) are NOT seeded.
    /// </summary>
    public static class F2Seed
    {
        public const string DepreciationSource = "جدول استهلاکات موضوع ماده ۱۴۹ قانون مالیات‌های مستقیم — توسط مالی تعیین شود";
        private const string LabourLaw = "قانون کار — توسط مالی تایید شود";

        /// <summary>Rate table keys that finance must enter for the year (names only; the values are never invented).</summary>
        public static readonly string[] StandardRateKeys =
        {
            "MinimumDailyWage", "HousingAllowance", "GroceryAllowance", "MaritalAllowance", "SeniorityDaily", "InsuranceCeilingDaily", "DailyWorkHours",
        };

        public static IEnumerable<FnAssetClass> AssetClassSeeds()
        {
            FnAssetClass C(string code, string title, string asset, string accumulated, bool intangible = false) => new FnAssetClass
            {
                Code = code, Title = title, AssetAccountCode = asset, AccumulatedAccountCode = accumulated, Method = FnDepreciationMethod.StraightLine,
                StartRule = FnDepreciationStart.NextMonth, IdleRatePercent = 30, IdleAfterMonths = 6, SalvagePercent = 0, Source = DepreciationSource, Verify = true,
                IsIntangible = intangible, IsActive = true, Jurisdiction = FinanceSeed.Iran,
            };
            yield return C("LND", "زمین", "20101", null);
            yield return C("BLD", "ساختمان", "20102", "20202");
            yield return C("MCH", "ماشین‌آلات و تجهیزات", "20103", "20203");
            yield return C("INS", "تاسیسات", "20104", "20204");
            yield return C("VEH", "وسایط نقلیه", "20105", "20205");
            yield return C("FUR", "اثاثه و تجهیزات اداری", "20106", "20206");
            yield return C("COM", "رایانه و تجهیزات فناوری اطلاعات", "20106", "20206");
            yield return C("TLS", "ابزارآلات (سرمایه‌ای)", "20107", "20207");
            yield return C("SWR", "نرم‌افزار", "20301", "20401", true);
            yield return C("PAT", "حق اختراع", "20302", "20401", true);
        }

        public static IEnumerable<FnPayrollRateTable> RateSeeds()
        {
            FnPayrollRateTable R(string key, decimal value, string source) => new FnPayrollRateTable { Year = 0, Key = key, Value = value, Source = source, Verify = true };
            yield return R("ShiftPercent1", 10m, "قانون کار ماده ۵۵ — نوبت‌کاری دو نوبت ★ — توسط مالی تایید شود");
            yield return R("ShiftPercent2", 15m, "قانون کار ماده ۵۵ — نوبت‌کاری سه نوبت ★ — توسط مالی تایید شود");
            yield return R("ShiftPercent3", 22.5m, "قانون کار ماده ۵۵ — شب‌کاری مداوم ★ — توسط مالی تایید شود");
            yield return R("LeaveEntitlementMonthly", 2.5m, "قانون کار ماده ۶۴ — ۳۰ روز در سال ★ — توسط مالی تایید شود");
            yield return R("LeaveCarryForwardMax", 9m, "قانون کار ماده ۶۵ — حداکثر انتقال ★ — توسط مالی تایید شود");
        }

        public static IEnumerable<FnPayrollItem> PayrollItemSeeds()
        {
            FnPayrollItem I(int order, string code, string title, FnPayrollItemKind kind, FnPayrollCalc calc, bool insurable, bool taxable, string source,
                string rateKey = null, string variant = null, string itemCodes = null, string formula = null, decimal factor = 1, bool proRata = false,
                string condition = null, bool reduces = false, string role = null) => new FnPayrollItem
            {
                Order = order, Code = code, Title = title, Kind = kind, Calculation = calc, Insurable = insurable, Taxable = taxable, RateKey = rateKey, Variant = variant,
                ItemCodes = itemCodes, Formula = formula, Factor = factor, ProRata = proRata, Condition = condition, ReducesTaxable = reduces, AccountRole = role,
                IsActive = true, Jurisdiction = FinanceSeed.Iran, Source = source, Verify = true,
            };
            const string insurance = "قانون تامین اجتماعی ماده ۲۸ — توسط مالی تایید شود";
            const string notSeeded = "مبلغ از جدول نرخ‌ها خوانده می‌شود";
            yield return I(10, "BASE_WAGE", "مزد مبنا", FnPayrollItemKind.Earning, FnPayrollCalc.PerDayWorked, true, true, "مزد روزانه قرارداد کارمند × روزهای کارکرد");
            yield return I(20, "SENIORITY", "پایه سنوات", FnPayrollItemKind.Earning, FnPayrollCalc.PerDayWorked, true, true, notSeeded, rateKey: "SeniorityDaily");
            yield return I(30, "HOUSING", "حق مسکن", FnPayrollItemKind.Earning, FnPayrollCalc.FixedMonthly, true, true, notSeeded, rateKey: "HousingAllowance", proRata: true);
            yield return I(40, "GROCERY", "بن خواربار", FnPayrollItemKind.Earning, FnPayrollCalc.FixedMonthly, true, true, notSeeded, rateKey: "GroceryAllowance", proRata: true);
            yield return I(50, "MARITAL", "حق تاهل", FnPayrollItemKind.Earning, FnPayrollCalc.FixedMonthly, true, true, notSeeded, rateKey: "MaritalAllowance", proRata: true, condition: "Married");
            yield return I(60, "CHILD", "حق اولاد", FnPayrollItemKind.Earning, FnPayrollCalc.Formula, true, true, "تعداد فرزند × ۳ × حداقل مزد روزانه ★ — توسط مالی تایید شود",
                formula: "CHILDREN * 3 * RATE_MinimumDailyWage", condition: "HasChildren");
            yield return I(70, "OVERTIME", "اضافه‌کاری", FnPayrollItemKind.Earning, FnPayrollCalc.PerHour, true, true, "قانون کار ماده ۵۹ — ۱٫۴ برابر ★ — توسط مالی تایید شود", variant: "OVERTIME", factor: 1.4m);
            yield return I(80, "NIGHT", "شب‌کاری", FnPayrollItemKind.Earning, FnPayrollCalc.PerHour, true, true, "قانون کار ماده ۵۸ — ۳۵٪ ★ — توسط مالی تایید شود", variant: "NIGHT", factor: 0.35m);
            yield return I(90, "HOLIDAY", "تعطیل‌کاری", FnPayrollItemKind.Earning, FnPayrollCalc.PerHour, true, true, "قانون کار ماده ۶۲ — ۴۰٪ ★ — توسط مالی تایید شود", variant: "HOLIDAY", factor: 0.4m);
            yield return I(100, "SHIFT", "حق نوبت‌کاری", FnPayrollItemKind.Earning, FnPayrollCalc.Formula, true, true, "قانون کار ماده ۵۵ — ۱۰ / ۱۵ / ۲۲٫۵٪ بر حسب نوع نوبت ★ — توسط مالی تایید شود",
                formula: "BASE_WAGE * SHIFT_PERCENT / 100");
            yield return I(110, "MISSION", "ماموریت", FnPayrollItemKind.Earning, FnPayrollCalc.Input, false, true, "مبلغ هر ماه وارد می‌شود — مشمول بیمه و مالیات توسط مالی تایید شود");
            yield return I(120, "BONUS", "پاداش", FnPayrollItemKind.Earning, FnPayrollCalc.Input, true, true, "مبلغ هر ماه وارد می‌شود");

            yield return I(200, "SS_EMPLOYEE", "بیمه سهم کارگر", FnPayrollItemKind.Deduction, FnPayrollCalc.PercentOfItems, false, false, insurance + " — ۷٪ تا سقف ★",
                itemCodes: "@INSURABLE", factor: 7, reduces: true, role: AccountRoles.InsurancePayable);
            yield return I(210, "INCOME_TAX", "مالیات بر درآمد حقوق", FnPayrollItemKind.Deduction, FnPayrollCalc.IncomeTax, false, false, "جدول نرخ مالیات سال از جدول نرخ‌ها خوانده می‌شود",
                role: AccountRoles.PayrollTaxPayable);
            yield return I(220, "LOAN", "قسط وام", FnPayrollItemKind.Deduction, FnPayrollCalc.LoanInstallment, false, false, "از جدول وام کارکنان", variant: "Loan", role: AccountRoles.EmployeeAdvance);
            yield return I(230, "ADVANCE", "مساعده", FnPayrollItemKind.Deduction, FnPayrollCalc.LoanInstallment, false, false, "از جدول مساعده کارکنان", variant: "Advance", role: AccountRoles.EmployeeAdvance);
            yield return I(240, "OTHER_DED", "سایر کسورات", FnPayrollItemKind.Deduction, FnPayrollCalc.Input, false, false, "مبلغ هر ماه وارد می‌شود", role: AccountRoles.OtherPayable);

            yield return I(300, "SS_EMPLOYER", "بیمه سهم کارفرما", FnPayrollItemKind.EmployerContribution, FnPayrollCalc.PercentOfItems, false, false, insurance + " — ۲۰٪ تا سقف ★",
                itemCodes: "@INSURABLE", factor: 20, role: AccountRoles.InsurancePayable);
            yield return I(310, "UNEMPLOYMENT", "بیمه بیکاری", FnPayrollItemKind.EmployerContribution, FnPayrollCalc.PercentOfItems, false, false, "قانون بیمه بیکاری — ۳٪ تا سقف ★ — توسط مالی تایید شود",
                itemCodes: "@INSURABLE", factor: 3, role: AccountRoles.InsurancePayable);
            yield return I(320, "HARD_HARMFUL", "بیمه مشاغل سخت و زیان‌آور", FnPayrollItemKind.EmployerContribution, FnPayrollCalc.PercentOfItems, false, false,
                "قانون تامین اجتماعی — ۴٪ تا سقف ★ — توسط مالی تایید شود", itemCodes: "@INSURABLE", factor: 4, condition: "HardAndHarmful", role: AccountRoles.InsurancePayable);

            yield return I(400, "SEVERANCE", "ذخیره سنوات (پایان خدمت)", FnPayrollItemKind.Provision, FnPayrollCalc.Formula, false, false,
                "قانون کار ماده ۲۴ — یک ماه آخرین حقوق به ازای هر سال، ماهانه یک‌دوازدهم ★ — توسط مالی تایید شود", formula: "INSURABLE / 12", role: AccountRoles.SeveranceProvision);
            yield return I(410, "BONUS_PROV", "ذخیره عیدی و پاداش", FnPayrollItemKind.Provision, FnPayrollCalc.Formula, false, false,
                "عیدی ۶۰ روز مزد با سقف ۹۰ روز حداقل مزد در سال، ماهانه یک‌دوازدهم ★ — توسط مالی تایید شود",
                formula: "min(60 * DAILY_WAGE, 90 * RATE_MinimumDailyWage) / 12", role: AccountRoles.BonusProvision);
            yield return I(420, "LEAVE_PROV", "ذخیره مرخصی استفاده‌نشده", FnPayrollItemKind.Provision, FnPayrollCalc.Formula, false, false,
                LabourLaw, formula: "max(LEAVE_ENTITLEMENT - PAID_LEAVE_DAYS, 0) * DAILY_WAGE", role: AccountRoles.LeaveProvision);
        }

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static volatile bool _done;

        /// <summary>Idempotent: adds the missing asset classes, payroll items and long-standing ratios; never changes what finance has edited.</summary>
        public static async Task EnsureAsync(IUnitOfWork uow, CancellationToken ct)
        {
            if (_done) return;
            await Gate.WaitAsync(ct);
            try
            {
                if (_done) return;
                HashSet<string> classes = (await uow.Set<FnAssetClass>().Select(c => c.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> items = (await uow.Set<FnPayrollItem>().Select(c => c.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> rates = (await uow.Set<FnPayrollRateTable>().Where(r => r.Year == 0).Select(r => r.Key).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool changed = false;
                foreach (FnAssetClass c in AssetClassSeeds())
                    if (classes.Add(c.Code)) { uow.Set<FnAssetClass>().Add(c); changed = true; }
                foreach (FnPayrollItem i in PayrollItemSeeds())
                    if (items.Add(i.Code)) { uow.Set<FnPayrollItem>().Add(i); changed = true; }
                foreach (FnPayrollRateTable r in RateSeeds())
                    if (rates.Add(r.Key)) { r.CreatedAt = DateTimeOffset.Now; uow.Set<FnPayrollRateTable>().Add(r); changed = true; }
                if (changed) await uow.SaveChangesAsync(ct);
                _done = true;
            }
            finally { Gate.Release(); }
        }
    }
}
