using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Finance;
using static Services.NasbNiro.Finance.FormulaEvaluator;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    /// <summary>The payroll rate table of one year: a year row wins over the long-standing (year 0) row. A key that is read but absent is recorded, never invented.</summary>
    internal sealed class PayrollRates
    {
        private readonly Dictionary<string, decimal> _values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        public PayrollRates(IEnumerable<FnPayrollRateTable> rows, int year)
        {
            List<FnPayrollRateTable> list = rows.ToList();
            foreach (FnPayrollRateTable r in list.Where(x => x.Year == 0)) _values[r.Key] = r.Value;
            foreach (FnPayrollRateTable r in list.Where(x => x.Year == year)) _values[r.Key] = r.Value;
        }

        public SortedSet<string> Missing { get; } = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool Has(string key) => _values.ContainsKey(key);

        public decimal Get(string key)
        {
            if (_values.TryGetValue(key, out decimal v)) return v;
            Missing.Add(key);
            return 0;
        }
    }

    internal sealed class PayrollInput
    {
        public FnEmployee Employee { get; set; }
        public FnAttendanceSheet Sheet { get; set; }
        public Dictionary<string, decimal> Inputs { get; set; } = new Dictionary<string, decimal>();
        public List<FnEmployeeLoan> Loans { get; set; } = new List<FnEmployeeLoan>();
    }

    internal sealed class PayrollLine
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public FnPayrollItemKind Kind { get; set; }
        public FnPayrollCalc Calculation { get; set; }
        public decimal Base { get; set; }
        public decimal Amount { get; set; }
        public bool Insurable { get; set; }
        public bool Taxable { get; set; }
        public string Role { get; set; }
    }

    internal sealed class LoanPortion
    {
        public int LoanId { get; set; }
        public FnLoanKind Kind { get; set; }
        public decimal Amount { get; set; }
    }

    internal sealed class PayrollResult
    {
        public List<PayrollLine> Lines { get; } = new List<PayrollLine>();
        public List<LoanPortion> LoanPortions { get; } = new List<LoanPortion>();
        public decimal DaysPaid { get; set; }
        public decimal Gross { get; set; }
        public decimal Insurable { get; set; }
        public decimal Taxable { get; set; }
        public decimal EmployeeInsurance { get; set; }
        public decimal EmployerInsurance { get; set; }
        public decimal Tax { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal Provisions { get; set; }
        public decimal Net { get; set; }
    }

    /// <summary>
    /// The payroll arithmetic: items are evaluated in order; the amount of every item becomes a variable named by its code, so later items and formulas can use it.
    /// Pure and deterministic: the same inputs always give the same payslip. No amount is invented: every statutory value comes from the rate table.
    /// </summary>
    internal static class PayrollEngine
    {
        public static readonly string[] BuiltInNames =
        {
            "DAYS", "DAYS_WORKED", "OT_HOURS", "NIGHT_HOURS", "HOLIDAY_HOURS", "PAID_LEAVE_DAYS", "UNPAID_LEAVE_DAYS", "ABSENCE_DAYS", "MISSION_DAYS", "CHILDREN",
            "DAILY_WAGE", "HOURLY_WAGE", "SHIFT_PERCENT", "INSURABLE", "INSURABLE_CAPPED", "TAXABLE", "GROSS", "LEAVE_ENTITLEMENT", "SEVERANCE_BASE",
        };

        public static readonly string[] Conditions = { "Married", "HasChildren", "HardAndHarmful" };

        public static bool ConditionMet(string condition, FnEmployee e)
        {
            if (string.IsNullOrWhiteSpace(condition)) return true;
            switch (condition.Trim().ToLowerInvariant())
            {
                case "married": return e.MaritalStatus == 2;
                case "haschildren": return e.ChildrenCount > 0;
                case "hardandharmful": return e.HardAndHarmful;
                default: return true;
            }
        }

        public static decimal BracketTax(decimal annual, IReadOnlyList<FnTaxBracket> brackets)
        {
            decimal tax = 0;
            foreach (FnTaxBracket b in brackets)
            {
                if (annual <= b.FromAmount) continue;
                decimal upper = b.ToAmount ?? decimal.MaxValue;
                tax += (Math.Min(annual, upper) - b.FromAmount) * b.RatePercent / 100m;
            }
            return tax;
        }

        public static PayrollResult Calculate(IReadOnlyList<FnPayrollItem> items, PayrollRates rates, IReadOnlyList<FnTaxBracket> brackets, int year, int month, PayrollInput input)
        {
            FnEmployee e = input.Employee;
            FnAttendanceSheet s = input.Sheet;
            var result = new PayrollResult();
            // the payroll month is always 30 days (company decision): a full month pays 30 days in a 29-, 30- or 31-day month; only unpaid leave and absence reduce it
            // days of the Persian month before the hire date or after the end date are not employed days (same 30-day basis)
            int monthDays = PersianCal.DaysIn(year, month);
            DateTime monthStart = PersianCal.Start(year, month).Date, monthEnd = monthStart.AddDays(monthDays - 1);
            int notEmployed = 0;
            if (e.HireDate.Date > monthStart) notEmployed += (int)Math.Min(monthDays, (e.HireDate.Date - monthStart).TotalDays);
            if (e.EndDate.HasValue && e.EndDate.Value.Date < monthEnd) notEmployed += (int)Math.Min(monthDays, (monthEnd - e.EndDate.Value.Date).TotalDays);
            notEmployed = Math.Min(notEmployed, monthDays);
            decimal daysPaid = Math.Max(0m, Math.Min(30m, 30m - s.UnpaidLeaveDays - s.AbsenceDays - notEmployed));
            if (s.DaysWorked + s.PaidLeaveDays + s.SickLeaveDays + s.MissionDays <= 0) daysPaid = 0; // no attendance at all: nothing is paid for days
            result.DaysPaid = daysPaid;
            decimal dailyWage = e.DailyWage;

            var vars = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["DAYS"] = daysPaid, ["DAYS_WORKED"] = s.DaysWorked, ["OT_HOURS"] = s.OvertimeHours, ["NIGHT_HOURS"] = s.NightHours, ["HOLIDAY_HOURS"] = s.HolidayHours,
                ["PAID_LEAVE_DAYS"] = s.PaidLeaveDays, ["UNPAID_LEAVE_DAYS"] = s.UnpaidLeaveDays, ["ABSENCE_DAYS"] = s.AbsenceDays, ["MISSION_DAYS"] = s.MissionDays,
                ["CHILDREN"] = e.ChildrenCount, ["DAILY_WAGE"] = dailyWage, ["INSURABLE"] = 0m, ["INSURABLE_CAPPED"] = 0m, ["TAXABLE"] = 0m, ["GROSS"] = 0m,
            };
            vars["SHIFT_PERCENT"] = s.ShiftType >= 1 && s.ShiftType <= 3 ? rates.Get("ShiftPercent" + s.ShiftType) : 0m;
            vars["LEAVE_ENTITLEMENT"] = rates.Get("LeaveEntitlementMonthly");
            bool hourlyNeeded = items.Any(i => i.Calculation == FnPayrollCalc.PerHour && ConditionMet(i.Condition, e));
            vars["HOURLY_WAGE"] = hourlyNeeded ? dailyWage / Math.Max(rates.Get("DailyWorkHours"), 0.0001m) : 0m;

            decimal insurableEarnings = 0, taxableEarnings = 0, gross = 0, reducesTaxable = 0, severanceBase = 0;
            bool severanceFlagged = items.Any(i => i.Kind == FnPayrollItemKind.Earning && i.SeveranceBase);
            int monthIndex = PersianCal.Index(year, month);

            decimal Resolve(string name)
            {
                if (vars.TryGetValue(name, out decimal v)) return v;
                if (name.StartsWith("RATE_", StringComparison.Ordinal)) return rates.Get(name.Substring(5));
                throw new FormulaException($"متغیر «{name}» در فرمول شناخته‌شده نیست (یا بعد از این قلم محاسبه می‌شود).");
            }

            foreach (FnPayrollItem item in items)
            {
                if (!ConditionMet(item.Condition, e)) continue;
                decimal amount = 0, @base = 0;
                switch (item.Calculation)
                {
                    case FnPayrollCalc.FixedMonthly:
                    {
                        @base = rates.Get(item.RateKey);
                        amount = @base * (item.Factor == 0 ? 1 : item.Factor);
                        if (item.ProRata) amount = amount * Math.Min(daysPaid, 30m) / 30m;
                        break;
                    }
                    case FnPayrollCalc.PerDayWorked:
                    {
                        @base = string.IsNullOrWhiteSpace(item.RateKey) ? dailyWage : rates.Get(item.RateKey);
                        amount = @base * daysPaid * (item.Factor == 0 ? 1 : item.Factor);
                        break;
                    }
                    case FnPayrollCalc.PerHour:
                    {
                        decimal hours = 0;
                        switch ((item.Variant ?? "").Trim().ToUpperInvariant())
                        {
                            case "OVERTIME": hours = s.OvertimeHours; break;
                            case "NIGHT": hours = s.NightHours; break;
                            case "HOLIDAY": hours = s.HolidayHours; break;
                        }
                        @base = vars["HOURLY_WAGE"];
                        amount = @base * hours * (item.Factor == 0 ? 1 : item.Factor);
                        break;
                    }
                    case FnPayrollCalc.PercentOfItems:
                    {
                        if (string.Equals((item.ItemCodes ?? "").Trim(), "@INSURABLE", StringComparison.OrdinalIgnoreCase))
                        {
                            decimal ceiling = rates.Get("InsuranceCeilingDaily");
                            @base = Math.Min(insurableEarnings, ceiling * daysPaid);
                            vars["INSURABLE_CAPPED"] = @base;
                        }
                        else
                        {
                            foreach (string code in (item.ItemCodes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                @base += vars.TryGetValue(code.Trim().ToUpperInvariant(), out decimal part) ? part : 0m;
                        }
                        amount = @base * item.Factor / 100m;
                        break;
                    }
                    case FnPayrollCalc.Formula:
                    {
                        vars["GROSS"] = gross; vars["TAXABLE"] = taxableEarnings; vars["INSURABLE"] = insurableEarnings;
                        vars["SEVERANCE_BASE"] = severanceFlagged ? severanceBase : insurableEarnings;
                        amount = Evaluate(item.Formula, Resolve) * (item.Factor == 0 ? 1 : item.Factor);
                        break;
                    }
                    case FnPayrollCalc.Input:
                        input.Inputs.TryGetValue(item.Code.ToUpperInvariant(), out amount);
                        break;
                    case FnPayrollCalc.LoanInstallment:
                    {
                        FnLoanKind kind = string.Equals(item.Variant, "Advance", StringComparison.OrdinalIgnoreCase) ? FnLoanKind.Advance : FnLoanKind.Loan;
                        foreach (FnEmployeeLoan loan in input.Loans.Where(l => l.Kind == kind && l.IsActive && l.Balance > 0 && PersianCal.Index(l.FirstYear, l.FirstMonth) <= monthIndex).OrderBy(l => l.Id))
                        {
                            decimal portion = Math.Min(loan.InstallmentAmount, loan.Balance);
                            if (portion <= 0) continue;
                            result.LoanPortions.Add(new LoanPortion { LoanId = loan.Id, Kind = kind, Amount = portion });
                            amount += portion;
                            @base += loan.Balance;
                        }
                        break;
                    }
                    case FnPayrollCalc.IncomeTax:
                    {
                        decimal exemptions = 0;
                        foreach (string flag in (e.ExemptionFlags ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            exemptions += rates.Get("TaxExemption" + flag.Trim() + "Monthly");
                        decimal monthlyTaxable = Math.Max(0m, taxableEarnings - reducesTaxable - exemptions);
                        @base = monthlyTaxable;
                        if (monthlyTaxable > 0)
                        {
                            if (brackets.Count == 0) rates.Missing.Add("TaxBrackets" + year);
                            else amount = BracketTax(monthlyTaxable * 12m, brackets) / 12m;
                        }
                        break;
                    }
                }
                amount = R(amount);
                if (amount < 0) amount = 0;
                vars[item.Code.ToUpperInvariant()] = amount;
                if (amount == 0 && item.Calculation != FnPayrollCalc.IncomeTax) continue; // zero lines are not printed
                var line = new PayrollLine
                {
                    Code = item.Code, Title = item.Title, Kind = item.Kind, Calculation = item.Calculation, Base = Math.Round(@base, 4), Amount = amount,
                    Insurable = item.Kind == FnPayrollItemKind.Earning && item.Insurable, Taxable = item.Kind == FnPayrollItemKind.Earning && item.Taxable, Role = item.AccountRole,
                };
                result.Lines.Add(line);
                switch (item.Kind)
                {
                    case FnPayrollItemKind.Earning:
                        gross += amount;
                        if (item.Insurable) insurableEarnings += amount;
                        if (item.SeveranceBase) severanceBase += amount;
                        if (item.Taxable) taxableEarnings += amount;
                        break;
                    case FnPayrollItemKind.Deduction:
                        if (item.ReducesTaxable) reducesTaxable += amount;
                        break;
                }
                if (item.Calculation == FnPayrollCalc.IncomeTax) result.Taxable = @base;
            }

            result.Gross = gross;
            result.Insurable = insurableEarnings;
            if (!items.Any(i => i.Calculation == FnPayrollCalc.IncomeTax)) result.Taxable = Math.Max(0m, taxableEarnings - reducesTaxable);
            foreach (PayrollLine l in result.Lines)
            {
                switch (l.Kind)
                {
                    case FnPayrollItemKind.Deduction:
                        if (l.Calculation == FnPayrollCalc.IncomeTax) result.Tax += l.Amount;
                        else if (l.Role == AccountRoles.InsurancePayable) result.EmployeeInsurance += l.Amount;
                        else result.OtherDeductions += l.Amount;
                        break;
                    case FnPayrollItemKind.EmployerContribution: result.EmployerInsurance += l.Amount; break;
                    case FnPayrollItemKind.Provision: result.Provisions += l.Amount; break;
                }
            }
            result.Net = gross - result.Tax - result.EmployeeInsurance - result.OtherDeductions;
            return result;
        }
    }
}
