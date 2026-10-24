using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface IPayrollRunService
    {
        Task<ResponseDto<PagedList<PayrollRunDto>>> GetRunsAsync(PayrollRunQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PayrollRunDto>> GetRunAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PayrollRunDto>> CreateRunAsync(CreatePayrollRunDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PayrollCalcResultDto>> CalculateAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PayrollRunDto>> ApproveAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PayrollRunDto>> PostAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PayrollRunDto>> DeleteRunAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PayslipDto>>> GetPayslipsAsync(PayslipQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PayslipDto>> GetPayslipAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<InsuranceListDto>> GetInsuranceListAsync(int year, int month, int? workshopId, CancellationToken cancellationToken);
        Task<ResponseDto<TaxListDto>> GetTaxListAsync(int year, int month, CancellationToken cancellationToken);
        Task<ResponseDto<EmployeeYearSummaryDto>> GetEmployeeYearSummaryAsync(int employeeId, int year, CancellationToken cancellationToken);
        Task<ResponseDto<List<BankTransferRowDto>>> GetBankTransferAsync(int runId, CancellationToken cancellationToken);
        Task<ResponseDto<byte[]>> GetBankTransferFileAsync(int runId, CancellationToken cancellationToken);
    }

    /// <summary>The payroll reports shared by the screens and by the Sepidar outbox. Only approved, posted or paid runs count.</summary>
    internal static class PayrollReports
    {
        public static async Task<InsuranceListDto> InsuranceListAsync(IUnitOfWork uow, int year, int month, int? workshopId, CancellationToken ct)
        {
            IQueryable<FnPayslip> q = uow.Set<FnPayslip>().AsNoTracking().Where(p => p.Run.Year == year && p.Run.Month == month && p.Run.Status >= FnPayrollStatus.Approved);
            if (workshopId.HasValue) q = q.Where(p => p.WorkshopId == workshopId.Value);
            List<FnPayslip> slips = await q.Include(p => p.Employee).Include(p => p.Lines).AsSplitQuery().OrderBy(p => p.Employee.PersonnelCode).ToListAsync(ct);
            var dto = new InsuranceListDto { Year = year, Month = month, WorkshopId = workshopId };
            if (workshopId.HasValue)
            {
                FnInsuranceWorkshop w = await uow.Set<FnInsuranceWorkshop>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workshopId.Value, ct);
                dto.WorkshopCode = w?.Code; dto.WorkshopName = w?.Name;
            }
            foreach (FnPayslip p in slips)
            {
                FnPayslipLine baseWage = p.Lines.FirstOrDefault(l => l.ItemCode == "BASE_WAGE");
                FnPayslipLine employee = p.Lines.FirstOrDefault(l => l.Kind == FnPayrollItemKind.Deduction && l.AccountRole == AccountRoles.InsurancePayable);
                decimal Sum(string code) => p.Lines.Where(l => l.ItemCode == code).Sum(l => l.Amount);
                var row = new InsuranceListRowDto
                {
                    PersonnelCode = p.Employee.PersonnelCode, InsuranceNumber = p.Employee.InsuranceNumber, NationalCode = p.Employee.NationalCode, FirstName = p.Employee.FirstName,
                    LastName = p.Employee.LastName, FatherName = p.Employee.FatherName, BirthDate = p.Employee.BirthDate, JobCode = p.Employee.InsuranceJobCode, DaysWorked = p.DaysPaid,
                    DailyWage = baseWage != null ? R(baseWage.Base) : p.Employee.DailyWage, MonthlyWage = baseWage?.Amount ?? 0, InsurableTotal = p.InsurableTotal,
                    Benefits = p.InsurableTotal - (baseWage?.Amount ?? 0), InsurableCapped = employee != null ? R(employee.Base) : 0, EmployeeShare = p.EmployeeInsurance,
                    EmployerShare = Sum("SS_EMPLOYER"), Unemployment = Sum("UNEMPLOYMENT"), HardAndHarmful = Sum("HARD_HARMFUL"),
                };
                dto.Rows.Add(row);
            }
            dto.Employees = dto.Rows.Count;
            dto.TotalInsurable = dto.Rows.Sum(r => r.InsurableTotal);
            dto.TotalEmployeeShare = dto.Rows.Sum(r => r.EmployeeShare);
            dto.TotalEmployerShare = dto.Rows.Sum(r => r.EmployerShare);
            dto.TotalUnemployment = dto.Rows.Sum(r => r.Unemployment);
            return dto;
        }

        public static async Task<TaxListDto> TaxListAsync(IUnitOfWork uow, int year, int month, CancellationToken ct)
        {
            List<FnPayslip> slips = await uow.Set<FnPayslip>().AsNoTracking().Where(p => p.Run.Year == year && p.Run.Month == month && p.Run.Status >= FnPayrollStatus.Approved)
                .Include(p => p.Employee).OrderBy(p => p.Employee.PersonnelCode).ToListAsync(ct);
            var dto = new TaxListDto { Year = year, Month = month };
            foreach (FnPayslip p in slips)
                dto.Rows.Add(new TaxListRowDto
                {
                    PersonnelCode = p.Employee.PersonnelCode, NationalCode = p.Employee.NationalCode, FirstName = p.Employee.FirstName, LastName = p.Employee.LastName,
                    TaxableTotal = p.TaxableTotal, EmployeeInsurance = p.EmployeeInsurance, Tax = p.Tax,
                });
            dto.Employees = dto.Rows.Count;
            dto.TotalTaxable = dto.Rows.Sum(r => r.TaxableTotal);
            dto.TotalTax = dto.Rows.Sum(r => r.Tax);
            return dto;
        }
    }

    /// <summary>
    /// Payroll runs: create, calculate, approve (a different person than the preparer), post to the ledger (totals per cost centre or work order, never per person)
    /// and the confidential payslip, insurance-list and tax-list reports. Salary values are never written to the log.
    /// </summary>
    public class PayrollRunService : IPayrollRunService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;

        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public PayrollRunService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _notify = notify;
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز دسترسی به اطلاعات حقوق و دستمزد را ندارید";
        private const string SameUser = "تایید باید توسط شخص دیگری غیر از محاسبه‌کننده حقوق انجام شود.";
        private const string Conflict = "همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید.";
        private bool CanRead => _user.IsInRole(FinanceRoles.PayrollReaders);
        private bool CanWrite => _user.IsInRole(FinanceRoles.PayrollUsers);

        private async Task EnsureAsync(CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            await F2Seed.EnsureAsync(_uow, ct);
        }

        private static bool ValidYear(int y) => y >= 1300 && y <= 1600;

        // ================================================================================== runs

        private async Task<List<PayrollRunDto>> ToDtosAsync(List<FnPayrollRun> runs, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, runs.SelectMany(r => new int?[] { r.CreatedByUserId, r.PreparedByUserId, r.ApprovedByUserId }), ct);
            return runs.Select(r => new PayrollRunDto
            {
                Id = r.Id, Number = r.Number, Year = r.Year, Month = r.Month, WorkshopId = r.WorkshopId, Status = r.Status, EmployeeCount = r.EmployeeCount, TotalGross = r.TotalGross,
                TotalInsurable = r.TotalInsurable, TotalTaxable = r.TotalTaxable, TotalEmployeeInsurance = r.TotalEmployeeInsurance, TotalEmployerInsurance = r.TotalEmployerInsurance,
                TotalTax = r.TotalTax, TotalOtherDeductions = r.TotalOtherDeductions, TotalNet = r.TotalNet, TotalProvisions = r.TotalProvisions, CreatorName = names.Name(r.CreatedByUserId),
                PreparedByName = r.CalculatedAt.HasValue ? names.Name(r.PreparedByUserId) : null, ApprovedByName = names.Name(r.ApprovedByUserId), CreatedAt = r.CreatedAt,
                CalculatedAt = r.CalculatedAt, ApprovedAt = r.ApprovedAt, PostedAt = r.PostedAt, PaidAt = r.PaidAt, AccountingEventId = r.AccountingEventId, Note = r.Note,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PayrollRunDto>>> GetRunsAsync(PayrollRunQuery query, CancellationToken ct)
        {
            query ??= new PayrollRunQuery();
            if (!CanRead) return Bad<PagedList<PayrollRunDto>>(NotAllowed);
            IQueryable<FnPayrollRun> q = _uow.Set<FnPayrollRun>().AsNoTracking();
            if (query.Year.HasValue) q = q.Where(r => r.Year == query.Year.Value);
            if (query.Month.HasValue) q = q.Where(r => r.Month == query.Month.Value);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnPayrollRun> rows = await q.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenByDescending(r => r.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await ToDtosAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<PayrollRunDto>> GetRunAsync(int id, CancellationToken ct)
        {
            if (!CanRead) return Bad<PayrollRunDto>(NotAllowed);
            return await RunDtoAsync(id, ct);
        }

        private async Task<ResponseDto<PayrollRunDto>> RunDtoAsync(int id, CancellationToken ct)
        {
            FnPayrollRun r = await _uow.Set<FnPayrollRun>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r == null) return ResponseDtoHelper.NotFound<PayrollRunDto>("اجرای حقوق پیدا نشد.");
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<FnPayrollRun> { r }, ct))[0]);
        }

        public async Task<ResponseDto<PayrollRunDto>> CreateRunAsync(CreatePayrollRunDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<PayrollRunDto>(NotAllowed);
            if (dto == null || !ValidYear(dto.Year) || dto.Month < 1 || dto.Month > 12) return Bad<PayrollRunDto>("سال یا ماه نامعتبر است.");
            await EnsureAsync(ct);
            if (dto.WorkshopId.HasValue && !await _uow.Set<FnInsuranceWorkshop>().AnyAsync(w => w.Id == dto.WorkshopId.Value, ct)) return Bad<PayrollRunDto>("کارگاه بیمه پیدا نشد.");
            if (await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == dto.Year && r.Month == dto.Month && (dto.WorkshopId == null || r.WorkshopId == null || r.WorkshopId == dto.WorkshopId), ct))
                return Bad<PayrollRunDto>("برای این ماه (و کارگاه) قبلاً اجرای حقوق ثبت شده یا با اجرای دیگر همپوشانی دارد.");
            var run = new FnPayrollRun
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.PayrollRun, "PAY", PersianCal.End(dto.Year, dto.Month), ct), Year = dto.Year, Month = dto.Month,
                WorkshopId = dto.WorkshopId, Status = FnPayrollStatus.Draft, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Note = Clean(dto.Note),
            };
            _uow.Set<FnPayrollRun>().Add(run);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<PayrollRunDto>("برای این ماه (و کارگاه) قبلاً اجرای حقوق ثبت شده است."); }
            return await RunDtoAsync(run.Id, ct);
        }

        public async Task<ResponseDto<PayrollRunDto>> DeleteRunAsync(int id, CancellationToken ct)
        {
            if (!CanWrite) return Bad<PayrollRunDto>(NotAllowed);
            FnPayrollRun run = await _uow.Set<FnPayrollRun>().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return ResponseDtoHelper.NotFound<PayrollRunDto>("اجرای حقوق پیدا نشد.");
            if (run.Status > FnPayrollStatus.Calculated) return Bad<PayrollRunDto>("اجرای تاییدشده حذف نمی‌شود.");
            _uow.Set<FnPayslip>().RemoveRange(await _uow.Set<FnPayslip>().Where(p => p.RunId == id).ToListAsync(ct));
            _uow.Set<FnPayrollRun>().Remove(run);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new PayrollRunDto { Id = id, Number = run.Number });
        }

        // ================================================================================== calculation

        private static string MissingMessage(int year, IEnumerable<string> keys) =>
            $"مقادیر قانونی زیر برای سال {year} در جدول نرخ‌ها وارد نشده است و محاسبه ممکن نیست: " + string.Join("، ", keys) +
            ". هیچ مقداری به‌صورت پیش‌فرض فرض نمی‌شود؛ مقدار و منبع آن را در جدول نرخ‌ها (یا جدول مالیات) ثبت کنید.";

        public async Task<ResponseDto<PayrollCalcResultDto>> CalculateAsync(int id, CancellationToken ct)
        {
            if (!CanWrite) return Bad<PayrollCalcResultDto>(NotAllowed);
            await EnsureAsync(ct);
            FnPayrollRun run = await _uow.Set<FnPayrollRun>().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return ResponseDtoHelper.NotFound<PayrollCalcResultDto>("اجرای حقوق پیدا نشد.");
            if (run.Status > FnPayrollStatus.Calculated) return Bad<PayrollCalcResultDto>("اجرای تاییدشده تغییر نمی‌کند.");
            DateTime start = PersianCal.Start(run.Year, run.Month), end = PersianCal.End(run.Year, run.Month);

            List<FnEmployee> employees = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.IsActive && e.HireDate <= end && (e.EndDate == null || e.EndDate >= start)
                && (run.WorkshopId == null || e.WorkshopId == run.WorkshopId)).OrderBy(e => e.PersonnelCode).ToListAsync(ct);
            if (employees.Count == 0) return Bad<PayrollCalcResultDto>("کارمند فعالی برای این ماه (و کارگاه) وجود ندارد.");
            List<int> employeeIds = employees.Select(e => e.Id).ToList();
            List<FnAttendanceSheet> sheets = await _uow.Set<FnAttendanceSheet>().AsNoTracking().Include(s => s.WorkOrders)
                .Where(s => s.Year == run.Year && s.Month == run.Month && employeeIds.Contains(s.EmployeeId)).AsSplitQuery().ToListAsync(ct);
            Dictionary<int, FnAttendanceSheet> sheetOf = sheets.ToDictionary(s => s.EmployeeId);
            List<FnPayrollItem> items = await _uow.Set<FnPayrollItem>().AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.Order).ThenBy(i => i.Id).ToListAsync(ct);
            var rates = new PayrollRates(await _uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => r.Year == 0 || r.Year == run.Year).ToListAsync(ct), run.Year);
            List<FnTaxBracket> brackets = await _uow.Set<FnTaxBracket>().AsNoTracking().Where(b => b.Year == run.Year).OrderBy(b => b.FromAmount).ToListAsync(ct);
            List<FnEmployeeLoan> loans = await _uow.Set<FnEmployeeLoan>().AsNoTracking().Where(l => l.IsActive && l.Balance > 0 && employeeIds.Contains(l.EmployeeId)).ToListAsync(ct);

            var warnings = new List<string>();
            var slips = new List<FnPayslip>();
            foreach (FnEmployee e in employees)
            {
                if (!sheetOf.TryGetValue(e.Id, out FnAttendanceSheet sheet))
                {
                    warnings.Add($"برای کارمند با کد پرسنلی {e.PersonnelCode} کارکرد این ماه ثبت نشده و از محاسبه کنار گذاشته شد.");
                    continue;
                }
                var input = new PayrollInput { Employee = e, Sheet = sheet, Loans = loans.Where(l => l.EmployeeId == e.Id).ToList() };
                if (!string.IsNullOrWhiteSpace(sheet.InputsJson))
                {
                    try { input.Inputs = JsonSerializer.Deserialize<Dictionary<string, decimal>>(sheet.InputsJson) ?? input.Inputs; }
                    catch (JsonException) { return Bad<PayrollCalcResultDto>($"ورودی‌های ماهانه کارمند با کد پرسنلی {e.PersonnelCode} خوانده نشد."); }
                    input.Inputs = input.Inputs.ToDictionary(kv => kv.Key.ToUpperInvariant(), kv => kv.Value);
                }
                PayrollResult r;
                try { r = PayrollEngine.Calculate(items, rates, brackets, run.Year, run.Month, input); }
                catch (FormulaEvaluator.FormulaException ex) { return Bad<PayrollCalcResultDto>("خطا در فرمول اقلام حقوق: " + ex.Message); }
                if (r.DaysPaid <= 0 && r.Gross == 0)
                {
                    warnings.Add($"کارکرد کارمند با کد پرسنلی {e.PersonnelCode} صفر است و فیش صادر نشد.");
                    continue;
                }
                if (r.Net < 0) return Bad<PayrollCalcResultDto>($"حقوق خالص کارمند با کد پرسنلی {e.PersonnelCode} منفی می‌شود؛ کسورات (وام، مساعده یا سایر کسورات) را بررسی کنید.");

                var slip = new FnPayslip
                {
                    EmployeeId = e.Id, WorkshopId = e.WorkshopId, CostCenterId = e.CostCenterId, DaysPaid = r.DaysPaid, GrossEarnings = r.Gross, InsurableTotal = r.Insurable,
                    TaxableTotal = r.Taxable, EmployeeInsurance = r.EmployeeInsurance, EmployerInsurance = r.EmployerInsurance, Tax = r.Tax, OtherDeductions = r.OtherDeductions,
                    Provisions = r.Provisions, Net = r.Net, Lines = new List<FnPayslipLine>(), Allocations = new List<FnPayslipAllocation>(),
                };
                int row = 1;
                foreach (PayrollLine l in r.Lines)
                    slip.Lines.Add(new FnPayslipLine
                    {
                        RowNumber = row++, ItemCode = l.Code, Title = l.Title, Kind = l.Kind, Base = l.Base, Amount = l.Amount, Insurable = l.Insurable, Taxable = l.Taxable, AccountRole = l.Role,
                    });
                // work-order allocation: hours on the attendance sheet, otherwise the employee's default work order
                var hours = sheet.WorkOrders.Where(w => w.Hours > 0).ToList();
                if (hours.Count > 0)
                {
                    decimal totalHours = hours.Sum(w => w.Hours);
                    decimal given = 0;
                    for (int i = 0; i < hours.Count; i++)
                    {
                        decimal share = i == hours.Count - 1 ? 1m - given : Math.Round(hours[i].Hours / totalHours, 6);
                        given += share;
                        slip.Allocations.Add(new FnPayslipAllocation { WorkOrderId = hours[i].WorkOrderId, Share = share });
                    }
                }
                else if (e.DefaultWorkOrderId.HasValue) slip.Allocations.Add(new FnPayslipAllocation { WorkOrderId = e.DefaultWorkOrderId.Value, Share = 1m });
                slips.Add(slip);
            }
            if (rates.Missing.Count > 0) return Bad<PayrollCalcResultDto>(MissingMessage(run.Year, rates.Missing));
            if (slips.Count == 0) return Bad<PayrollCalcResultDto>("هیچ کارمندی با کارکرد ثبت‌شده برای محاسبه وجود ندارد.");

            // two saves: the old payslips go first (the unique index RunId + EmployeeId), then the new ones and the totals
            List<FnPayslip> old = await _uow.Set<FnPayslip>().Where(p => p.RunId == run.Id).ToListAsync(ct);
            if (old.Count > 0)
            {
                _uow.Set<FnPayslip>().RemoveRange(old);
                run.Status = FnPayrollStatus.Draft;
                await _uow.SaveChangesAsync(ct);
            }
            foreach (FnPayslip s in slips) s.RunId = run.Id;
            _uow.Set<FnPayslip>().AddRange(slips);
            run.EmployeeCount = slips.Count;
            run.TotalGross = slips.Sum(s => s.GrossEarnings);
            run.TotalInsurable = slips.Sum(s => s.InsurableTotal);
            run.TotalTaxable = slips.Sum(s => s.TaxableTotal);
            run.TotalEmployeeInsurance = slips.Sum(s => s.EmployeeInsurance);
            run.TotalEmployerInsurance = slips.Sum(s => s.EmployerInsurance);
            run.TotalTax = slips.Sum(s => s.Tax);
            run.TotalOtherDeductions = slips.Sum(s => s.OtherDeductions);
            run.TotalNet = slips.Sum(s => s.Net);
            run.TotalProvisions = slips.Sum(s => s.Provisions);
            run.Status = FnPayrollStatus.Calculated;
            run.EditorUserIds = WithEditor(run.EditorUserIds, Me);
            run.PreparedByUserId = Me;
            run.CalculatedAt = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PayrollCalcResultDto>(Conflict); }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "payroll:" + run.Id, run.Status == FnPayrollStatus.Calculated, "finance", "payroll-run", run.Id,
                $"تایید حقوق {run.Year}/{run.Month:00}" + (string.IsNullOrEmpty(run.Number) ? "" : " (" + run.Number + ")"), "/finance/payroll/runs/" + run.Id, FinanceRoles.Managers, ct);
            return ResponseDtoHelper.Ok(new PayrollCalcResultDto { Run = (await RunDtoAsync(run.Id, ct)).Data, Warnings = warnings });
        }

        // ================================================================================== approval

        private static List<LoanPortion> Portions(IEnumerable<FnEmployeeLoan> loans, FnLoanKind kind, int monthIndex)
        {
            var list = new List<LoanPortion>();
            foreach (FnEmployeeLoan l in loans.Where(x => x.Kind == kind && x.IsActive && x.Balance > 0 && PersianCal.Index(x.FirstYear, x.FirstMonth) <= monthIndex).OrderBy(x => x.Id))
            {
                decimal portion = Math.Min(l.InstallmentAmount, l.Balance);
                if (portion > 0) list.Add(new LoanPortion { LoanId = l.Id, Kind = kind, Amount = portion });
            }
            return list;
        }

        public async Task<ResponseDto<PayrollRunDto>> ApproveAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<PayrollRunDto>(NotAllowed + " (فقط مدیر مالی)");
            await EnsureAsync(ct);
            FnPayrollRun run = await _uow.Set<FnPayrollRun>().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return ResponseDtoHelper.NotFound<PayrollRunDto>("اجرای حقوق پیدا نشد.");
            if (run.Status != FnPayrollStatus.Calculated) return Bad<PayrollRunDto>("فقط اجرای محاسبه‌شده تایید می‌شود.");
            if (Touched(run.PreparedByUserId, run.EditorUserIds, Me) || run.CreatedByUserId == Me) return Bad<PayrollRunDto>(SameUser);
            DateTimeOffset calculatedAt = run.CalculatedAt ?? DateTimeOffset.MinValue;

            List<FnPayslip> slips = await _uow.Set<FnPayslip>().Include(p => p.Lines).Where(p => p.RunId == run.Id).AsSplitQuery().ToListAsync(ct);
            List<int> employeeIds = slips.Select(s => s.EmployeeId).ToList();
            // the yearly bonus is paid either by a bonus run or by an ANNUAL_BONUS line, not both (review 3, H8)
            List<int> bonusEmployees = slips.Where(s => s.Lines.Any(l => l.ItemCode == "ANNUAL_BONUS" && l.Amount > 0)).Select(s => s.EmployeeId).ToList();
            if (bonusEmployees.Count > 0 && await _uow.Set<FnBonusLine>().AnyAsync(b => bonusEmployees.Contains(b.EmployeeId) && b.Run.Year == run.Year && b.Run.Status == FnSettlementStatus.Posted, ct))
                return Bad<PayrollRunDto>("عیدی سال این اجرا برای یکی از کارکنان با «محاسبه عیدی» ثبت شده است؛ قلم ANNUAL_BONUS را حذف کنید تا دوبار پرداخت نشود.");
            if (await _uow.Set<FnAttendanceSheet>().AnyAsync(s => s.Year == run.Year && s.Month == run.Month && employeeIds.Contains(s.EmployeeId) && s.UpdatedAt > calculatedAt, ct))
                return Bad<PayrollRunDto>("کارکرد یا ورودی‌های ماه پس از محاسبه تغییر کرده است؛ حقوق را دوباره محاسبه کنید.");
            if (await _uow.Set<FnPayrollRateTable>().AnyAsync(r => (r.Year == 0 || r.Year == run.Year) && r.CreatedAt > calculatedAt, ct))
                return Bad<PayrollRunDto>("جدول نرخ‌ها پس از محاسبه تغییر کرده است؛ حقوق را دوباره محاسبه کنید.");

            // loans and advances: the instalments on the payslips must still be what the loan tables say
            int monthIndex = PersianCal.Index(run.Year, run.Month);
            List<FnPayrollItem> loanItems = await _uow.Set<FnPayrollItem>().AsNoTracking().Where(i => i.Calculation == FnPayrollCalc.LoanInstallment).ToListAsync(ct);
            List<FnEmployeeLoan> loans = await _uow.Set<FnEmployeeLoan>().Where(l => employeeIds.Contains(l.EmployeeId)).ToListAsync(ct);
            var balanceChanges = new List<(FnEmployeeLoan Loan, decimal Amount)>();
            foreach (FnPayslip s in slips)
            {
                List<FnEmployeeLoan> mine = loans.Where(l => l.EmployeeId == s.EmployeeId).ToList();
                foreach (FnPayrollItem item in loanItems)
                {
                    decimal onSlip = s.Lines.Where(l => l.ItemCode == item.Code).Sum(l => l.Amount);
                    FnLoanKind kind = string.Equals(item.Variant, "Advance", StringComparison.OrdinalIgnoreCase) ? FnLoanKind.Advance : FnLoanKind.Loan;
                    List<LoanPortion> portions = Portions(mine, kind, monthIndex);
                    if (portions.Sum(p => p.Amount) != onSlip)
                        return Bad<PayrollRunDto>("اقساط وام یا مساعده پس از محاسبه تغییر کرده است؛ حقوق را دوباره محاسبه کنید.");
                    foreach (LoanPortion p in portions) balanceChanges.Add((mine.First(l => l.Id == p.LoanId), p.Amount));
                }
            }
            foreach (var change in balanceChanges) change.Loan.Balance -= change.Amount;

            // leave balances of the year
            decimal entitlement = (await _uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => (r.Year == 0 || r.Year == run.Year) && r.Key == "LeaveEntitlementMonthly")
                .OrderByDescending(r => r.Year).Select(r => (decimal?)r.Value).FirstOrDefaultAsync(ct)) ?? 0m;
            decimal carryMax = (await _uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => (r.Year == 0 || r.Year == run.Year) && r.Key == "LeaveCarryForwardMax")
                .OrderByDescending(r => r.Year).Select(r => (decimal?)r.Value).FirstOrDefaultAsync(ct)) ?? 0m;
            Dictionary<int, FnLeaveBalance> balances = await _uow.Set<FnLeaveBalance>().Where(b => b.Year == run.Year && employeeIds.Contains(b.EmployeeId)).ToDictionaryAsync(b => b.EmployeeId, ct);
            Dictionary<int, FnLeaveBalance> previous = await _uow.Set<FnLeaveBalance>().AsNoTracking().Where(b => b.Year == run.Year - 1 && employeeIds.Contains(b.EmployeeId)).ToDictionaryAsync(b => b.EmployeeId, ct);
            Dictionary<int, decimal> paidLeave = await _uow.Set<FnAttendanceSheet>().AsNoTracking().Where(s => s.Year == run.Year && s.Month == run.Month && employeeIds.Contains(s.EmployeeId))
                .ToDictionaryAsync(s => s.EmployeeId, s => s.PaidLeaveDays, ct);
            foreach (int employeeId in employeeIds)
            {
                if (!balances.TryGetValue(employeeId, out FnLeaveBalance b))
                {
                    decimal carried = previous.TryGetValue(employeeId, out FnLeaveBalance prev) ? Math.Max(0m, Math.Min(prev.CarriedForward + prev.Entitled - prev.Used, carryMax)) : 0m;
                    b = new FnLeaveBalance { EmployeeId = employeeId, Year = run.Year, CarriedForward = carried };
                    _uow.Set<FnLeaveBalance>().Add(b);
                    balances[employeeId] = b;
                }
                if (b.LastMonth < run.Month)
                {
                    b.Entitled += entitlement * (run.Month - b.LastMonth);
                    b.Used += paidLeave.TryGetValue(employeeId, out decimal used) ? used : 0m;
                    b.LastMonth = run.Month;
                }
            }

            run.Status = FnPayrollStatus.Approved;
            run.ApprovedByUserId = Me;
            run.ApprovedAt = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PayrollRunDto>(Conflict); }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "payroll:" + run.Id, false, "finance", "payroll-run", run.Id, "تایید حقوق", "/finance/payroll/runs/" + run.Id, FinanceRoles.Managers, ct);
            return await RunDtoAsync(run.Id, ct);
        }

        // ================================================================================== posting

        public async Task<ResponseDto<PayrollRunDto>> PostAsync(int id, CancellationToken ct)
        {
            if (!CanWrite) return Bad<PayrollRunDto>(NotAllowed);
            await EnsureAsync(ct);
            FnPayrollRun run = await _uow.Set<FnPayrollRun>().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return ResponseDtoHelper.NotFound<PayrollRunDto>("اجرای حقوق پیدا نشد.");
            if (run.Status != FnPayrollStatus.Approved) return Bad<PayrollRunDto>("فقط اجرای تاییدشده در دفتر ثبت می‌شود.");
            List<FnPayslip> slips = await _uow.Set<FnPayslip>().AsNoTracking().Include(p => p.Lines).Include(p => p.Allocations).Where(p => p.RunId == run.Id).AsSplitQuery().ToListAsync(ct);
            List<int> ccIds = slips.Where(s => s.CostCenterId.HasValue).Select(s => s.CostCenterId.Value).Distinct().ToList();
            Dictionary<int, CostCenter> centers = await _uow.Set<CostCenter>().AsNoTracking().Where(c => ccIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            List<int> workOrderIds = slips.SelectMany(s => s.Allocations).Select(a => a.WorkOrderId).Distinct().ToList();
            Dictionary<int, int> contractOf = workOrderIds.Count == 0 ? new Dictionary<int, int>()
                : await _uow.Set<WorkOrder>().AsNoTracking().Where(w => workOrderIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.ContractId, ct);
            DateTime date = PersianCal.End(run.Year, run.Month);
            string period = $"{run.Year}/{run.Month:00}";

            // totals per account role, work order and cost centre: the voucher never carries a person
            var debits = new List<(string Role, int? WorkOrder, string Center, decimal Amount)>();
            var credits = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var order = new List<string>();
            void Credit(string role, decimal amount)
            {
                if (amount == 0) return;
                if (!credits.ContainsKey(role)) { credits[role] = 0; order.Add(role); }
                credits[role] += amount;
            }
            void Debit(string role, int? workOrder, string center, decimal amount)
            {
                if (amount == 0) return;
                int i = debits.FindIndex(d => d.Role == role && d.WorkOrder == workOrder && d.Center == center);
                if (i < 0) debits.Add((role, workOrder, center, amount));
                else debits[i] = (role, workOrder, center, debits[i].Amount + amount);
            }
            var laborCosts = new Dictionary<int, decimal>();
            var employeeRecoveries = new List<(int EmployeeId, decimal Amount)>();
            foreach (FnPayslip s in slips)
            {
                CostCenter cc = s.CostCenterId.HasValue && centers.TryGetValue(s.CostCenterId.Value, out CostCenter c) ? c : null;
                bool production = cc != null && cc.IsProduction;
                string center = cc?.Code;
                string indirect = production ? AccountRoles.PayrollExpenseIndirect : AccountRoles.PayrollExpenseAdmin;
                string employer = production ? AccountRoles.EmployerInsuranceProduction : AccountRoles.EmployerInsuranceAdmin;
                // earnings paid from a provision (bonus from 30402 ...) reduce the provision, they are not expensed again
                decimal fromProvision = 0;
                foreach (IGrouping<string, FnPayslipLine> g in s.Lines.Where(l => l.Kind == FnPayrollItemKind.Earning && l.AccountRole != null).GroupBy(l => l.AccountRole))
                {
                    decimal amount = g.Sum(l => l.Amount);
                    fromProvision += amount;
                    Debit(g.Key, null, center, amount);
                }
                decimal expensed = s.GrossEarnings - fromProvision;
                if (s.Allocations.Count > 0)
                {
                    decimal given = 0;
                    List<FnPayslipAllocation> allocations = s.Allocations.OrderBy(a => a.Id).ToList();
                    for (int i = 0; i < allocations.Count; i++)
                    {
                        decimal part = i == allocations.Count - 1 ? expensed - given : R(expensed * allocations[i].Share);
                        given += part;
                        Debit(AccountRoles.PayrollExpenseDirect, allocations[i].WorkOrderId, null, part);
                        laborCosts[allocations[i].WorkOrderId] = (laborCosts.TryGetValue(allocations[i].WorkOrderId, out decimal cur) ? cur : 0m) + part;
                    }
                }
                else Debit(indirect, null, center, expensed);
                foreach (FnPayslipLine l in s.Lines)
                {
                    switch (l.Kind)
                    {
                        case FnPayrollItemKind.Deduction:
                            // loan/advance recovery clears the receivable of that employee (sub-ledger by employee detail)
                            if (l.AccountRole == AccountRoles.EmployeeAdvance && l.Amount != 0)
                                employeeRecoveries.Add((s.EmployeeId, l.Amount));
                            else Credit(l.AccountRole ?? AccountRoles.OtherPayable, l.Amount);
                            break;
                        case FnPayrollItemKind.EmployerContribution:
                            Debit(employer, null, center, l.Amount);
                            Credit(l.AccountRole ?? AccountRoles.InsurancePayable, l.Amount);
                            break;
                        case FnPayrollItemKind.Provision:
                            Debit(l.ItemCode == "SEVERANCE" ? AccountRoles.SeveranceExpense : indirect, null, center, l.Amount);
                            Credit(l.AccountRole ?? AccountRoles.OtherPayable, l.Amount);
                            break;
                    }
                }
                Credit(AccountRoles.PayrollPayable, s.Net);
            }
            var lines = new List<AccountingEventLine>();
            foreach (var d in debits)
                lines.Add(new AccountingEventLine { AccountRole = d.Role, Debit = d.Amount, WorkOrderId = d.WorkOrder, CostCenter = d.Center, Description = $"حقوق و دستمزد {period}" });
            foreach (string role in order)
                lines.Add(new AccountingEventLine { AccountRole = role, Credit = credits[role], Description = $"حقوق و دستمزد {period}" });
            foreach (var g in employeeRecoveries.GroupBy(x => x.EmployeeId))
                lines.Add(new AccountingEventLine { AccountRole = AccountRoles.EmployeeAdvance, Credit = g.Sum(x => x.Amount), PartyType = "Employee", PartyId = g.Key, Description = $"کسر وام/مساعده {period}" });
            if (lines.Sum(l => l.Debit) != lines.Sum(l => l.Credit)) return Bad<PayrollRunDto>("سند حقوق متوازن نیست؛ اجرای حقوق را دوباره محاسبه کنید.");

            AccountingEvent e = _costing.AddEvent(AccountingEventType.PayrollRun, date, "FnPayrollRun", run.Id, run.Number, $"حقوق و دستمزد {period}", lines);
            // a work order whose labour production already absorbed in that payroll month (MfCostPosting) gets no second Labour CostEntry: the absorbed cost is the project cost,
            // finance reconciles absorbed against actual in the ledger. The payroll accounting entries above are not touched.
            List<int> costedIds = laborCosts.Where(x => x.Value != 0).Select(x => x.Key).ToList();
            DateTime monthFirst = PersianCal.Start(run.Year, run.Month), monthNext = PersianCal.End(run.Year, run.Month).Date.AddDays(1);
            HashSet<int> absorbedInProduction = costedIds.Count == 0 ? new HashSet<int>()
                : (await _uow.Set<MfCostPosting>().AsNoTracking().Where(p => costedIds.Contains(p.WorkOrderId) && p.PostingDate >= monthFirst && p.PostingDate < monthNext).Select(p => p.WorkOrderId).Distinct().ToListAsync(ct)).ToHashSet();
            foreach (var kv in LabourEntriesToPost(laborCosts, absorbedInProduction))
                _uow.Set<CostEntry>().Add(new CostEntry
                {
                    EntryDate = date, Kind = CostKind.Labour, WorkOrderId = kv.Key, ContractId = contractOf.TryGetValue(kv.Key, out int contract) && contract > 0 ? contract : (int?)null,
                    Quantity = 0, Amount = kv.Value, SourceType = "PayrollRun", SourceId = run.Id, SourceNumber = run.Number, Description = $"دستمزد مستقیم {period}",
                    CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me,
                });
            run.Status = FnPayrollStatus.Posted;
            run.PostedByUserId = Me;
            run.PostedAt = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PayrollRunDto>(Conflict); }
            run.AccountingEventId = e.Id;
            try { await _uow.SaveChangesAsync(ct); } catch (DbUpdateException) { /* the link is informative only */ }
            return await RunDtoAsync(run.Id, ct);
        }

        /// <summary>The payroll Labour cost entries to write: the work orders with a non-zero cost that production did not already absorb in the month.</summary>
        internal static List<KeyValuePair<int, decimal>> LabourEntriesToPost(IDictionary<int, decimal> laborCosts, ISet<int> absorbedInProduction)
            => laborCosts.Where(x => x.Value != 0 && !absorbedInProduction.Contains(x.Key)).OrderBy(x => x.Key).ToList();

        // ================================================================================== payslips

        private static PayslipDto ToDto(FnPayslip p, FnPayrollRun run) => new PayslipDto
        {
            Id = p.Id, RunId = p.RunId, Year = run.Year, Month = run.Month, RunStatus = run.Status, EmployeeId = p.EmployeeId, PersonnelCode = p.Employee?.PersonnelCode,
            EmployeeName = p.Employee == null ? null : (p.Employee.FirstName + " " + p.Employee.LastName).Trim(), JobTitle = p.Employee?.JobTitle, Unit = p.Employee?.Unit,
            NationalCode = p.Employee?.NationalCode, InsuranceNumber = p.Employee?.InsuranceNumber, DaysPaid = p.DaysPaid, GrossEarnings = p.GrossEarnings, InsurableTotal = p.InsurableTotal,
            TaxableTotal = p.TaxableTotal, EmployeeInsurance = p.EmployeeInsurance, EmployerInsurance = p.EmployerInsurance, Tax = p.Tax, OtherDeductions = p.OtherDeductions,
            Provisions = p.Provisions, Net = p.Net,
            Lines = (p.Lines ?? new List<FnPayslipLine>()).OrderBy(l => l.RowNumber).Select(l => new PayslipLineDto
            {
                RowNumber = l.RowNumber, ItemCode = l.ItemCode, Title = l.Title, Kind = l.Kind, Base = l.Base, Amount = l.Amount, Insurable = l.Insurable, Taxable = l.Taxable,
            }).ToList(),
        };

        public async Task<ResponseDto<PagedList<PayslipDto>>> GetPayslipsAsync(PayslipQuery query, CancellationToken ct)
        {
            query ??= new PayslipQuery();
            if (!CanRead) return Bad<PagedList<PayslipDto>>(NotAllowed);
            IQueryable<FnPayslip> q = _uow.Set<FnPayslip>().AsNoTracking();
            if (query.RunId.HasValue) q = q.Where(p => p.RunId == query.RunId.Value);
            if (query.EmployeeId.HasValue) q = q.Where(p => p.EmployeeId == query.EmployeeId.Value);
            if (query.Year.HasValue) q = q.Where(p => p.Run.Year == query.Year.Value);
            if (query.Month.HasValue) q = q.Where(p => p.Run.Month == query.Month.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnPayslip> rows = await q.Include(p => p.Employee).Include(p => p.Run).Include(p => p.Lines).AsSplitQuery()
                .OrderBy(p => p.Run.Year).ThenBy(p => p.Run.Month).ThenBy(p => p.Employee.PersonnelCode).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(p => ToDto(p, p.Run)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<PayslipDto>> GetPayslipAsync(int id, CancellationToken ct)
        {
            if (!CanRead) return Bad<PayslipDto>(NotAllowed);
            FnPayslip p = await _uow.Set<FnPayslip>().AsNoTracking().Include(x => x.Employee).Include(x => x.Run).Include(x => x.Lines).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            return p == null ? ResponseDtoHelper.NotFound<PayslipDto>("فیش حقوق پیدا نشد.") : ResponseDtoHelper.Ok(ToDto(p, p.Run));
        }

        // ================================================================================== reports

        public async Task<ResponseDto<InsuranceListDto>> GetInsuranceListAsync(int year, int month, int? workshopId, CancellationToken ct)
        {
            if (!CanRead) return Bad<InsuranceListDto>(NotAllowed);
            if (!ValidYear(year) || month < 1 || month > 12) return Bad<InsuranceListDto>("سال یا ماه نامعتبر است.");
            return ResponseDtoHelper.Ok(await PayrollReports.InsuranceListAsync(_uow, year, month, workshopId, ct));
        }

        public async Task<ResponseDto<TaxListDto>> GetTaxListAsync(int year, int month, CancellationToken ct)
        {
            if (!CanRead) return Bad<TaxListDto>(NotAllowed);
            if (!ValidYear(year) || month < 1 || month > 12) return Bad<TaxListDto>("سال یا ماه نامعتبر است.");
            return ResponseDtoHelper.Ok(await PayrollReports.TaxListAsync(_uow, year, month, ct));
        }

        public async Task<ResponseDto<EmployeeYearSummaryDto>> GetEmployeeYearSummaryAsync(int employeeId, int year, CancellationToken ct)
        {
            if (!CanRead) return Bad<EmployeeYearSummaryDto>(NotAllowed);
            if (!ValidYear(year)) return Bad<EmployeeYearSummaryDto>("سال نامعتبر است.");
            FnEmployee e = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, ct);
            if (e == null) return ResponseDtoHelper.NotFound<EmployeeYearSummaryDto>("کارمند پیدا نشد.");
            List<FnPayslip> slips = await _uow.Set<FnPayslip>().AsNoTracking().Where(p => p.EmployeeId == employeeId && p.Run.Year == year && p.Run.Status >= FnPayrollStatus.Approved).ToListAsync(ct);
            return ResponseDtoHelper.Ok(new EmployeeYearSummaryDto
            {
                EmployeeId = e.Id, PersonnelCode = e.PersonnelCode, EmployeeName = (e.FirstName + " " + e.LastName).Trim(), Year = year, Months = slips.Count,
                Gross = slips.Sum(s => s.GrossEarnings), EmployeeInsurance = slips.Sum(s => s.EmployeeInsurance), Tax = slips.Sum(s => s.Tax),
                OtherDeductions = slips.Sum(s => s.OtherDeductions), Net = slips.Sum(s => s.Net),
            });
        }

        public async Task<ResponseDto<List<BankTransferRowDto>>> GetBankTransferAsync(int runId, CancellationToken ct)
        {
            if (!CanRead) return Bad<List<BankTransferRowDto>>(NotAllowed);
            FnPayrollRun run = await _uow.Set<FnPayrollRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run == null) return ResponseDtoHelper.NotFound<List<BankTransferRowDto>>("اجرای حقوق پیدا نشد.");
            if (run.Status < FnPayrollStatus.Posted) return Bad<List<BankTransferRowDto>>("فایل واریز فقط پس از ثبت در دفتر صادر می‌شود.");
            List<FnPayslip> slips = await _uow.Set<FnPayslip>().AsNoTracking().Include(p => p.Employee).Where(p => p.RunId == runId && p.Net > 0).OrderBy(p => p.Employee.PersonnelCode).ToListAsync(ct);
            List<string> noIban = slips.Where(p => string.IsNullOrWhiteSpace(p.Employee.BankIban)).Select(p => p.Employee.PersonnelCode).ToList();
            if (noIban.Count > 0) return Bad<List<BankTransferRowDto>>("شماره شبا برای کارمندان با این کدهای پرسنلی ثبت نشده است: " + string.Join("، ", noIban));
            return ResponseDtoHelper.Ok(slips.Select(p => new BankTransferRowDto
            {
                Iban = p.Employee.BankIban, Amount = p.Net, Name = (p.Employee.FirstName + " " + p.Employee.LastName).Trim(), PersonnelCode = p.Employee.PersonnelCode,
            }).ToList());
        }

        public async Task<ResponseDto<byte[]>> GetBankTransferFileAsync(int runId, CancellationToken ct)
        {
            ResponseDto<List<BankTransferRowDto>> rows = await GetBankTransferAsync(runId, ct);
            if (!rows.Success) return Bad<byte[]>(rows.Message);
            var sheet = new XlsxSheet { Name = "Transfer", Header = new List<string> { "شبا", "مبلغ (ریال)", "نام و نام خانوادگی", "کد پرسنلی" } };
            foreach (BankTransferRowDto r in rows.Data) sheet.Rows.Add(new object[] { r.Iban, r.Amount, r.Name, r.PersonnelCode });
            return ResponseDtoHelper.Ok(SimpleXlsx.Build(new List<XlsxSheet> { sheet }));
        }
    }
}
