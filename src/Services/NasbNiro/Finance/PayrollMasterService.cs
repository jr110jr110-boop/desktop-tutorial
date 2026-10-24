using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface IPayrollMasterService
    {
        Task<ResponseDto<PayrollSetupDto>> GetSetupAsync(int year, CancellationToken cancellationToken);

        Task<ResponseDto<List<WorkshopDto>>> GetWorkshopsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<WorkshopDto>> SaveWorkshopAsync(SaveWorkshopDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<EmployeeDto>>> GetEmployeesAsync(EmployeeQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<EmployeeDto>> GetEmployeeAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<EmployeeDto>> SaveEmployeeAsync(SaveEmployeeDto dto, CancellationToken cancellationToken);
        /// <summary>Name, job title and unit only: for every signed-in user (never wages, national codes or bank data).</summary>
        Task<ResponseDto<List<EmployeeDirectoryDto>>> GetDirectoryAsync(string contains, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<LoanDto>>> GetLoansAsync(LoanQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LoanDto>> SaveLoanAsync(SaveLoanDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<PayrollItemDto>>> GetItemsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<PayrollItemDto>> SaveItemAsync(SavePayrollItemDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<PayrollRateDto>>> GetRatesAsync(RateQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PayrollRateDto>> SaveRateAsync(SavePayrollRateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<TaxBracketDto>>> GetBracketsAsync(int year, CancellationToken cancellationToken);
        Task<ResponseDto<List<TaxBracketDto>>> SaveBracketsAsync(SaveTaxBracketsDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<AttendanceDto>>> GetAttendanceAsync(AttendanceQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<AttendanceDto>> SaveAttendanceAsync(SaveAttendanceDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ImportAttendanceResultDto>> ImportAttendanceAsync(ImportAttendanceDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<LeaveBalanceDto>>> GetLeaveBalancesAsync(int year, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Payroll master data: insurance workshops, employees, loans, payroll items, the dated rate table, tax brackets, attendance and leave.
    /// Confidential: only PAYROLL, FINANCEMANAGER and CEO read it; only PAYROLL and FINANCEMANAGER change it. Wages are never written to the log.
    /// </summary>
    public class PayrollMasterService : IPayrollMasterService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public PayrollMasterService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز دسترسی به اطلاعات حقوق و دستمزد را ندارید";
        private const string Conflict = "همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید.";
        private bool CanRead => _user.IsInRole(FinanceRoles.PayrollReaders);
        private bool CanWrite => _user.IsInRole(FinanceRoles.PayrollUsers);

        private async Task EnsureAsync(CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            await F2Seed.EnsureAsync(_uow, ct);
        }

        private static bool ValidYear(int y) => y >= 1300 && y <= 1600;

        /// <summary>A change of items, rates or brackets invalidates calculated runs that were not approved yet: they must be calculated again.</summary>
        private async Task ReopenCalculatedRunsAsync(int? year, CancellationToken ct)
        {
            IQueryable<FnPayrollRun> q = _uow.Set<FnPayrollRun>().Where(r => r.Status == FnPayrollStatus.Calculated);
            if (year.HasValue) q = q.Where(r => r.Year == year.Value);
            List<FnPayrollRun> runs = await q.ToListAsync(ct);
            foreach (FnPayrollRun r in runs) r.Status = FnPayrollStatus.Draft;
            // the approval item of a run that must be calculated again leaves the inbox in the same save (approving it would only answer "only a calculated run")
            if (runs.Count > 0)
            {
                List<string> keys = runs.Select(r => "payroll:" + r.Id).ToList();
                List<global::Entities.Model.NasbNiro.Office.OfInboxItem> items = await _uow.Set<global::Entities.Model.NasbNiro.Office.OfInboxItem>()
                    .Where(i => i.SourceKey != null && keys.Contains(i.SourceKey) && (i.Status == global::Entities.Model.NasbNiro.Office.OfInboxStatus.Unread || i.Status == global::Entities.Model.NasbNiro.Office.OfInboxStatus.Read)).ToListAsync(ct);
                foreach (var i in items) { i.Status = global::Entities.Model.NasbNiro.Office.OfInboxStatus.Done; i.ClosedAt = DateTimeOffset.Now; i.ClosedReason = "شرط‌های محاسبه تغییر کرد؛ حقوق دوباره محاسبه شود"; }
            }
        }

        // ================================================================================== set-up

        internal static async Task<SetupModuleDto> CheckSetupAsync(IUnitOfWork uow, int year, CancellationToken ct)
        {
            await F2Seed.EnsureAsync(uow, ct);
            List<string> have = await uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => r.Year == 0 || r.Year == year).Select(r => r.Key).ToListAsync(ct);
            var haveSet = new HashSet<string>(have, StringComparer.OrdinalIgnoreCase);
            var dto = new SetupModuleDto { Module = "Payroll" };
            dto.Missing = F2Seed.StandardRateKeys.Where(k => !haveSet.Contains(k)).ToList();
            if (!await uow.Set<FnTaxBracket>().AnyAsync(b => b.Year == year, ct)) dto.Missing.Add("TaxBrackets" + year);
            dto.Ok = dto.Missing.Count == 0;
            dto.Message = dto.Ok ? null : $"مقادیر قانونی حقوق و دستمزد سال {year} هنوز وارد نشده است: " + string.Join("، ", dto.Missing);
            return dto;
        }

        public async Task<ResponseDto<PayrollSetupDto>> GetSetupAsync(int year, CancellationToken ct)
        {
            if (!CanRead) return Bad<PayrollSetupDto>(NotAllowed);
            if (year == 0) year = PersianCal.Year(DateTime.Today);
            if (!ValidYear(year)) return Bad<PayrollSetupDto>("سال نامعتبر است.");
            await EnsureAsync(ct);
            SetupModuleDto check = await CheckSetupAsync(_uow, year, ct);
            var dto = new PayrollSetupDto { Year = year, Ok = check.Ok };
            dto.MissingRateKeys = check.Missing.Where(k => !k.StartsWith("TaxBrackets", StringComparison.Ordinal)).ToList();
            dto.TaxBracketsMissing = check.Missing.Any(k => k.StartsWith("TaxBrackets", StringComparison.Ordinal));
            dto.Items = await _uow.Set<FnPayrollItem>().AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.Order).Select(i => i.Code).ToListAsync(ct);
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== workshops

        private static WorkshopDto ToDto(FnInsuranceWorkshop w) => new WorkshopDto
        {
            Id = w.Id, Code = w.Code, Name = w.Name, EmployerName = w.EmployerName, Address = w.Address, Branch = w.Branch, IsContractor = w.IsContractor,
            ContractRow = w.ContractRow, RateSet = w.RateSet, IsActive = w.IsActive,
        };

        public async Task<ResponseDto<List<WorkshopDto>>> GetWorkshopsAsync(CancellationToken ct)
        {
            if (!CanRead) return Bad<List<WorkshopDto>>(NotAllowed);
            List<FnInsuranceWorkshop> rows = await _uow.Set<FnInsuranceWorkshop>().AsNoTracking().OrderBy(w => w.Code).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(ToDto).ToList());
        }

        public async Task<ResponseDto<WorkshopDto>> SaveWorkshopAsync(SaveWorkshopDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<WorkshopDto>(NotAllowed);
            if (dto == null) return Bad<WorkshopDto>("اطلاعات ارسال‌شده کامل نیست.");
            string code = Clean(dto.Code);
            if (code == null || code.Length > 20) return Bad<WorkshopDto>("کد کارگاه را وارد کنید (حداکثر ۲۰ نویسه).");
            if (Clean(dto.Name) == null) return Bad<WorkshopDto>("نام کارگاه را وارد کنید.");
            FnInsuranceWorkshop w;
            if (dto.Id.HasValue)
            {
                w = await _uow.Set<FnInsuranceWorkshop>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (w == null) return ResponseDtoHelper.NotFound<WorkshopDto>("کارگاه پیدا نشد.");
            }
            else { w = new FnInsuranceWorkshop(); _uow.Set<FnInsuranceWorkshop>().Add(w); }
            if (await _uow.Set<FnInsuranceWorkshop>().AnyAsync(x => x.Code == code && x.Id != w.Id, ct)) return Bad<WorkshopDto>("این کد کارگاه قبلاً ثبت شده است.");
            w.Code = code; w.Name = Clean(dto.Name); w.EmployerName = Clean(dto.EmployerName); w.Address = Clean(dto.Address); w.Branch = Clean(dto.Branch);
            w.IsContractor = dto.IsContractor; w.ContractRow = Clean(dto.ContractRow); w.RateSet = Clean(dto.RateSet); w.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(w));
        }

        // ================================================================================== employees

        private static EmployeeDto ToDto(FnEmployee e, string workshop) => new EmployeeDto
        {
            Id = e.Id, PersonnelCode = e.PersonnelCode, FirstName = e.FirstName, LastName = e.LastName, FatherName = e.FatherName, NationalCode = e.NationalCode,
            BirthDate = e.BirthDate, BirthPlace = e.BirthPlace, Gender = e.Gender, MaritalStatus = e.MaritalStatus, ChildrenCount = e.ChildrenCount,
            InsuranceNumber = e.InsuranceNumber, InsuranceJobCode = e.InsuranceJobCode, WorkshopId = e.WorkshopId, WorkshopName = workshop, HireDate = e.HireDate,
            EndDate = e.EndDate, EmploymentType = e.EmploymentType, JobTitle = e.JobTitle, Unit = e.Unit, CostCenterId = e.CostCenterId, DefaultWorkOrderId = e.DefaultWorkOrderId,
            BankIban = e.BankIban, DailyWage = e.DailyWage, ExemptionFlags = e.ExemptionFlags, HardAndHarmful = e.HardAndHarmful, UserId = e.UserId,
            QualifiedPersonId = e.QualifiedPersonId, IsActive = e.IsActive,
        };

        public async Task<ResponseDto<PagedList<EmployeeDto>>> GetEmployeesAsync(EmployeeQuery query, CancellationToken ct)
        {
            query ??= new EmployeeQuery();
            if (!CanRead) return Bad<PagedList<EmployeeDto>>(NotAllowed);
            IQueryable<FnEmployee> q = _uow.Set<FnEmployee>().AsNoTracking();
            if (query.WorkshopId.HasValue) q = q.Where(e => e.WorkshopId == query.WorkshopId.Value);
            if (query.CostCenterId.HasValue) q = q.Where(e => e.CostCenterId == query.CostCenterId.Value);
            if (query.IsActive.HasValue) q = q.Where(e => e.IsActive == query.IsActive.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(e => e.PersonnelCode.Contains(text) || e.FirstName.Contains(text) || e.LastName.Contains(text) || e.NationalCode.Contains(text));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnEmployee> rows = await q.Include(e => e.Workshop).OrderBy(e => e.PersonnelCode).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(e => ToDto(e, e.Workshop?.Name)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<EmployeeDto>> GetEmployeeAsync(int id, CancellationToken ct)
        {
            if (!CanRead) return Bad<EmployeeDto>(NotAllowed);
            FnEmployee e = await _uow.Set<FnEmployee>().AsNoTracking().Include(x => x.Workshop).FirstOrDefaultAsync(x => x.Id == id, ct);
            return e == null ? ResponseDtoHelper.NotFound<EmployeeDto>("کارمند پیدا نشد.") : ResponseDtoHelper.Ok(ToDto(e, e.Workshop?.Name));
        }

        public async Task<ResponseDto<EmployeeDto>> SaveEmployeeAsync(SaveEmployeeDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<EmployeeDto>(NotAllowed);
            if (dto == null) return Bad<EmployeeDto>("اطلاعات ارسال‌شده کامل نیست.");
            string code = Clean(Digits(dto.PersonnelCode ?? ""));
            if (code == null || code.Length > 20) return Bad<EmployeeDto>("کد پرسنلی را وارد کنید (حداکثر ۲۰ نویسه).");
            if (Clean(dto.FirstName) == null || Clean(dto.LastName) == null) return Bad<EmployeeDto>("نام و نام خانوادگی را وارد کنید.");
            string national = Clean(Digits(dto.NationalCode ?? ""));
            if (!IranianNationalCode.Valid(national)) return Bad<EmployeeDto>("کد ملی نامعتبر است (۱۰ رقم با رقم کنترل).");
            string iban = Clean(dto.BankIban)?.Replace(" ", "").ToUpperInvariant();
            if (iban != null && !IbanValid(iban)) return Bad<EmployeeDto>("شماره شبا نامعتبر است (IR و ۲۴ رقم با کنترل mod-97).");
            decimal wage = R(dto.DailyWage);
            if (wage <= 0) return Bad<EmployeeDto>("مزد روزانه باید بیشتر از صفر باشد.");
            if (dto.Gender != 1 && dto.Gender != 2) return Bad<EmployeeDto>("جنسیت نامعتبر است (۱ مرد، ۲ زن).");
            if (dto.MaritalStatus != 1 && dto.MaritalStatus != 2) return Bad<EmployeeDto>("وضعیت تاهل نامعتبر است (۱ مجرد، ۲ متاهل).");
            if (dto.ChildrenCount < 0 || dto.ChildrenCount > 20) return Bad<EmployeeDto>("تعداد فرزند نامعتبر است.");
            if (!Enum.IsDefined(typeof(FnEmploymentType), dto.EmploymentType)) return Bad<EmployeeDto>("نوع استخدام نامعتبر است.");
            if (dto.HireDate == default(DateTime)) return Bad<EmployeeDto>("تاریخ استخدام را وارد کنید.");
            if (dto.EndDate.HasValue && dto.EndDate.Value.Date < dto.HireDate.Date) return Bad<EmployeeDto>("تاریخ پایان کار پیش از تاریخ استخدام است.");
            string flags = Clean(dto.ExemptionFlags);
            if (flags != null)
            {
                var parts = flags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                if (parts.Any(p => !Regex.IsMatch(p, "^[A-Za-z][A-Za-z0-9]{0,29}$"))) return Bad<EmployeeDto>("پرچم‌های معافیت باید حرف و عدد انگلیسی و با کاما جدا شوند.");
                flags = string.Join(",", parts);
            }
            if (dto.WorkshopId.HasValue && !await _uow.Set<FnInsuranceWorkshop>().AnyAsync(w => w.Id == dto.WorkshopId.Value && w.IsActive, ct)) return Bad<EmployeeDto>("کارگاه بیمه پیدا نشد یا غیرفعال است.");
            if (dto.CostCenterId.HasValue && !await _uow.Set<CostCenter>().AnyAsync(c => c.Id == dto.CostCenterId.Value && c.IsActive, ct)) return Bad<EmployeeDto>("مرکز هزینه پیدا نشد.");
            if (dto.DefaultWorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.DefaultWorkOrderId.Value, ct)) return Bad<EmployeeDto>("دستور کار پیدا نشد.");

            FnEmployee e;
            if (dto.Id.HasValue)
            {
                e = await _uow.Set<FnEmployee>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (e == null) return ResponseDtoHelper.NotFound<EmployeeDto>("کارمند پیدا نشد.");
            }
            else
            {
                e = new FnEmployee { CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me };
                _uow.Set<FnEmployee>().Add(e);
            }
            if (await _uow.Set<FnEmployee>().AnyAsync(x => x.PersonnelCode == code && x.Id != e.Id, ct)) return Bad<EmployeeDto>("این کد پرسنلی قبلاً ثبت شده است.");
            if (await _uow.Set<FnEmployee>().AnyAsync(x => x.NationalCode == national && x.Id != e.Id, ct)) return Bad<EmployeeDto>("این کد ملی قبلاً ثبت شده است.");
            e.PersonnelCode = code; e.FirstName = Clean(dto.FirstName); e.LastName = Clean(dto.LastName); e.FatherName = Clean(dto.FatherName); e.NationalCode = national;
            e.BirthDate = dto.BirthDate?.Date; e.BirthPlace = Clean(dto.BirthPlace); e.Gender = dto.Gender; e.MaritalStatus = dto.MaritalStatus; e.ChildrenCount = dto.ChildrenCount;
            e.InsuranceNumber = Clean(dto.InsuranceNumber); e.InsuranceJobCode = Clean(dto.InsuranceJobCode); e.WorkshopId = dto.WorkshopId; e.HireDate = dto.HireDate.Date;
            e.EndDate = dto.EndDate?.Date; e.EmploymentType = dto.EmploymentType; e.JobTitle = Clean(dto.JobTitle); e.Unit = Clean(dto.Unit); e.CostCenterId = dto.CostCenterId;
            e.DefaultWorkOrderId = dto.DefaultWorkOrderId; e.BankIban = iban; e.DailyWage = wage; e.ExemptionFlags = flags; e.HardAndHarmful = dto.HardAndHarmful;
            e.UserId = dto.UserId; e.QualifiedPersonId = dto.QualifiedPersonId; e.IsActive = dto.IsActive;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<EmployeeDto>(Conflict); }
            return await GetEmployeeAsync(e.Id, ct);
        }

        public async Task<ResponseDto<List<EmployeeDirectoryDto>>> GetDirectoryAsync(string contains, CancellationToken ct)
        {
            IQueryable<FnEmployee> q = _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.IsActive);
            string text = Clean(contains);
            if (text != null) q = q.Where(e => e.FirstName.Contains(text) || e.LastName.Contains(text) || (e.JobTitle != null && e.JobTitle.Contains(text)) || (e.Unit != null && e.Unit.Contains(text)));
            var rows = await q.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).Take(500).Select(e => new { e.Id, e.FirstName, e.LastName, e.JobTitle, e.Unit }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(e => new EmployeeDirectoryDto { Id = e.Id, Name = (e.FirstName + " " + e.LastName).Trim(), JobTitle = e.JobTitle, Unit = e.Unit }).ToList());
        }

        // ================================================================================== loans and advances

        private static LoanDto ToDto(FnEmployeeLoan l, string personnel) => new LoanDto
        {
            Id = l.Id, EmployeeId = l.EmployeeId, PersonnelCode = personnel, Kind = l.Kind, Title = l.Title, Principal = l.Principal, InstallmentAmount = l.InstallmentAmount,
            Balance = l.Balance, FirstYear = l.FirstYear, FirstMonth = l.FirstMonth, IsActive = l.IsActive,
        };

        public async Task<ResponseDto<PagedList<LoanDto>>> GetLoansAsync(LoanQuery query, CancellationToken ct)
        {
            query ??= new LoanQuery();
            if (!CanRead) return Bad<PagedList<LoanDto>>(NotAllowed);
            IQueryable<FnEmployeeLoan> q = _uow.Set<FnEmployeeLoan>().AsNoTracking();
            if (query.EmployeeId.HasValue) q = q.Where(l => l.EmployeeId == query.EmployeeId.Value);
            if (query.IsActive.HasValue) q = q.Where(l => l.IsActive == query.IsActive.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnEmployeeLoan> rows = await q.Include(l => l.Employee).OrderByDescending(l => l.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(l => ToDto(l, l.Employee?.PersonnelCode)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<LoanDto>> SaveLoanAsync(SaveLoanDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<LoanDto>(NotAllowed);
            if (dto == null) return Bad<LoanDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnEmployee emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (emp == null) return Bad<LoanDto>("کارمند پیدا نشد.");
            if (!Enum.IsDefined(typeof(FnLoanKind), dto.Kind)) return Bad<LoanDto>("نوع وام نامعتبر است.");
            decimal principal = R(dto.Principal), installment = R(dto.InstallmentAmount);
            if (principal <= 0) return Bad<LoanDto>("مبلغ وام یا مساعده باید بیشتر از صفر باشد.");
            if (installment <= 0 || installment > principal) return Bad<LoanDto>("مبلغ قسط باید بیشتر از صفر و حداکثر برابر مبلغ وام باشد.");
            if (!ValidYear(dto.FirstYear) || dto.FirstMonth < 1 || dto.FirstMonth > 12) return Bad<LoanDto>("ماه شروع کسر نامعتبر است.");
            FnEmployeeLoan l;
            if (dto.Id.HasValue)
            {
                l = await _uow.Set<FnEmployeeLoan>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (l == null) return ResponseDtoHelper.NotFound<LoanDto>("وام پیدا نشد.");
                if (l.Balance != l.Principal) return Bad<LoanDto>("از این وام قسطی کسر شده است؛ فقط فعال یا غیرفعال کردن آن ممکن است.");
            }
            else
            {
                l = new FnEmployeeLoan { EmployeeId = emp.Id, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me };
                _uow.Set<FnEmployeeLoan>().Add(l);
            }
            l.Kind = dto.Kind; l.Title = Clean(dto.Title); l.Principal = principal; l.InstallmentAmount = installment; l.Balance = principal;
            l.FirstYear = dto.FirstYear; l.FirstMonth = dto.FirstMonth; l.IsActive = dto.IsActive;
            await ReopenCalculatedRunsAsync(null, ct);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<LoanDto>(Conflict); }
            return ResponseDtoHelper.Ok(ToDto(l, emp.PersonnelCode));
        }

        // ================================================================================== payroll items

        private static readonly string[] ProvisionRoles = { AccountRoles.BonusProvision, AccountRoles.LeaveProvision, AccountRoles.SeveranceProvision };

        private static PayrollItemDto ToDto(FnPayrollItem i) => new PayrollItemDto
        {
            Id = i.Id, Code = i.Code, Title = i.Title, Kind = i.Kind, Insurable = i.Insurable, Taxable = i.Taxable, Calculation = i.Calculation, RateKey = i.RateKey,
            Variant = i.Variant, ItemCodes = i.ItemCodes, Formula = i.Formula, Factor = i.Factor, Factor2 = i.Factor2, ProRata = i.ProRata, Condition = i.Condition,
            ReducesTaxable = i.ReducesTaxable, SeveranceBase = i.SeveranceBase, AccountRole = i.AccountRole, Order = i.Order, IsActive = i.IsActive, Source = i.Source, Verify = i.Verify,
        };

        public async Task<ResponseDto<List<PayrollItemDto>>> GetItemsAsync(CancellationToken ct)
        {
            if (!CanRead) return Bad<List<PayrollItemDto>>(NotAllowed);
            await EnsureAsync(ct);
            List<FnPayrollItem> rows = await _uow.Set<FnPayrollItem>().AsNoTracking().OrderBy(i => i.Order).ThenBy(i => i.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(ToDto).ToList());
        }

        public async Task<ResponseDto<PayrollItemDto>> SaveItemAsync(SavePayrollItemDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<PayrollItemDto>(NotAllowed);
            if (dto == null) return Bad<PayrollItemDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            string code = Clean(dto.Code)?.ToUpperInvariant();
            if (code == null || !Regex.IsMatch(code, "^[A-Z][A-Z0-9_]{0,29}$")) return Bad<PayrollItemDto>("کد قلم باید حرف انگلیسی، عدد و _ باشد (حداکثر ۳۰ نویسه).");
            if (BuiltInHas(code) || code.StartsWith("RATE_", StringComparison.Ordinal)) return Bad<PayrollItemDto>("این کد با متغیرهای داخلی فرمول‌ها تداخل دارد.");
            if (Clean(dto.Title) == null) return Bad<PayrollItemDto>("عنوان قلم را وارد کنید.");
            if (!Enum.IsDefined(typeof(FnPayrollItemKind), dto.Kind) || !Enum.IsDefined(typeof(FnPayrollCalc), dto.Calculation)) return Bad<PayrollItemDto>("نوع یا روش محاسبه نامعتبر است.");
            if (Clean(dto.Source) == null) return Bad<PayrollItemDto>("منبع (ماده قانون یا مصوبه) را وارد کنید.");
            if (dto.Factor < 0 || dto.Factor > 1000) return Bad<PayrollItemDto>("ضریب نامعتبر است.");
            string condition = Clean(dto.Condition);
            if (condition != null && !PayrollEngine.Conditions.Any(c => string.Equals(c, condition, StringComparison.OrdinalIgnoreCase))) return Bad<PayrollItemDto>("شرط نامعتبر است (Married, HasChildren, HardAndHarmful).");
            if (dto.Kind != FnPayrollItemKind.Earning && dto.Kind != FnPayrollItemKind.Info)
            {
                string role = Clean(dto.AccountRole);
                if (role == null || !AccountRoles.Standard().Any(r => r.Role == role)) return Bad<PayrollItemDto>("نقش حساب بستانکار قلم نامعتبر است.");
            }
            if (dto.Kind == FnPayrollItemKind.Earning && Clean(dto.AccountRole) != null && !ProvisionRoles.Contains(Clean(dto.AccountRole)))
                return Bad<PayrollItemDto>("برای قلم حقوقی، نقش حساب فقط ذخیره عیدی/مرخصی/پایان خدمت است (پرداخت از محل ذخیره).");
            string variant = Clean(dto.Variant);
            switch (dto.Calculation)
            {
                case FnPayrollCalc.FixedMonthly:
                    if (Clean(dto.RateKey) == null) return Bad<PayrollItemDto>("کلید جدول نرخ‌ها را وارد کنید.");
                    break;
                case FnPayrollCalc.PerHour:
                    if (variant == null || !new[] { "OVERTIME", "NIGHT", "HOLIDAY" }.Contains(variant.ToUpperInvariant())) return Bad<PayrollItemDto>("نوع ساعت باید OVERTIME، NIGHT یا HOLIDAY باشد.");
                    break;
                case FnPayrollCalc.LoanInstallment:
                    if (variant == null || !new[] { "LOAN", "ADVANCE" }.Contains(variant.ToUpperInvariant())) return Bad<PayrollItemDto>("نوع قسط باید Loan یا Advance باشد.");
                    break;
                case FnPayrollCalc.PercentOfItems:
                    if (Clean(dto.ItemCodes) == null) return Bad<PayrollItemDto>("کدهای اقلام مبنا (یا @INSURABLE) را وارد کنید.");
                    break;
                case FnPayrollCalc.Formula:
                {
                    if (Clean(dto.Formula) == null) return Bad<PayrollItemDto>("فرمول را وارد کنید.");
                    HashSet<string> names;
                    try { names = FormulaEvaluator.Names(dto.Formula); }
                    catch (FormulaEvaluator.FormulaException ex) { return Bad<PayrollItemDto>(ex.Message); }
                    List<string> earlier = await _uow.Set<FnPayrollItem>().AsNoTracking().Where(i => i.Order < dto.Order && i.Code != code).Select(i => i.Code).ToListAsync(ct);
                    var known = new HashSet<string>(earlier.Select(c => c.ToUpperInvariant()));
                    foreach (string n in names)
                        if (!BuiltInHas(n) && !n.StartsWith("RATE_", StringComparison.Ordinal) && !known.Contains(n))
                            return Bad<PayrollItemDto>($"نام «{n}» در فرمول شناخته‌شده نیست (فقط متغیرهای داخلی، RATE_Key و اقلامِ قبل از این قلم).");
                    break;
                }
            }

            FnPayrollItem item;
            if (dto.Id.HasValue)
            {
                item = await _uow.Set<FnPayrollItem>().FirstOrDefaultAsync(i => i.Id == dto.Id.Value, ct);
                if (item == null) return ResponseDtoHelper.NotFound<PayrollItemDto>("قلم پیدا نشد.");
                if (!string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)) return Bad<PayrollItemDto>("کد قلم تغییر نمی‌کند.");
            }
            else
            {
                if (await _uow.Set<FnPayrollItem>().AnyAsync(i => i.Code == code, ct)) return Bad<PayrollItemDto>("این کد قلم قبلاً ثبت شده است.");
                item = new FnPayrollItem { Code = code, Jurisdiction = FinanceSeed.Iran };
                _uow.Set<FnPayrollItem>().Add(item);
            }
            item.Title = Clean(dto.Title); item.Kind = dto.Kind; item.Insurable = dto.Insurable; item.Taxable = dto.Taxable; item.Calculation = dto.Calculation;
            item.RateKey = Clean(dto.RateKey); item.Variant = variant; item.ItemCodes = Clean(dto.ItemCodes); item.Formula = Clean(dto.Formula); item.Factor = dto.Factor;
            item.Factor2 = dto.Factor2; item.ProRata = dto.ProRata; item.Condition = condition; item.ReducesTaxable = dto.ReducesTaxable; item.SeveranceBase = dto.SeveranceBase; item.AccountRole = Clean(dto.AccountRole);
            item.Order = dto.Order; item.IsActive = dto.IsActive; item.Source = Clean(dto.Source); item.Verify = dto.Verify;
            await ReopenCalculatedRunsAsync(null, ct);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(item));
        }

        private static bool BuiltInHas(string name) => PayrollEngine.BuiltInNames.Contains(name, StringComparer.Ordinal);

        // ================================================================================== rates and tax brackets

        public async Task<ResponseDto<List<PayrollRateDto>>> GetRatesAsync(RateQuery query, CancellationToken ct)
        {
            query ??= new RateQuery();
            if (!CanRead) return Bad<List<PayrollRateDto>>(NotAllowed);
            await EnsureAsync(ct);
            IQueryable<FnPayrollRateTable> q = _uow.Set<FnPayrollRateTable>().AsNoTracking();
            if (query.Year.HasValue) q = q.Where(r => r.Year == query.Year.Value || r.Year == 0);
            string key = Clean(query.Key);
            if (key != null) q = q.Where(r => r.Key.Contains(key));
            List<FnPayrollRateTable> rows = await q.OrderBy(r => r.Key).ThenBy(r => r.Year).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(r => new PayrollRateDto { Id = r.Id, Year = r.Year, Key = r.Key, Value = r.Value, Source = r.Source, Verify = r.Verify, Note = r.Note }).ToList());
        }

        public async Task<ResponseDto<PayrollRateDto>> SaveRateAsync(SavePayrollRateDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<PayrollRateDto>(NotAllowed);
            if (dto == null) return Bad<PayrollRateDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (dto.Year != 0 && !ValidYear(dto.Year)) return Bad<PayrollRateDto>("سال نامعتبر است (۰ برای نرخ‌های بلندمدت).");
            string key = Clean(dto.Key);
            if (key == null || !Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9]{0,59}$")) return Bad<PayrollRateDto>("کلید باید حرف و عدد انگلیسی باشد.");
            if (Clean(dto.Source) == null) return Bad<PayrollRateDto>("منبع قانونی مقدار را وارد کنید (ماده قانون، بخشنامه یا مصوبه).");
            if (dto.Value < 0) return Bad<PayrollRateDto>("مقدار نمی‌تواند منفی باشد.");
            FnPayrollRateTable r = await _uow.Set<FnPayrollRateTable>().FirstOrDefaultAsync(x => x.Year == dto.Year && x.Key == key, ct);
            if (r == null)
            {
                r = new FnPayrollRateTable { Year = dto.Year, Key = key };
                _uow.Set<FnPayrollRateTable>().Add(r);
            }
            r.Value = dto.Value; r.Source = Clean(dto.Source); r.Verify = dto.Verify; r.Note = Clean(dto.Note);
            // the time stamp is what blocks the approval of a run that was calculated with the earlier value
            r.CreatedAt = DateTimeOffset.Now; r.CreatedByUserId = Me;
            await ReopenCalculatedRunsAsync(dto.Year == 0 ? (int?)null : dto.Year, ct);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new PayrollRateDto { Id = r.Id, Year = r.Year, Key = r.Key, Value = r.Value, Source = r.Source, Verify = r.Verify, Note = r.Note });
        }

        public async Task<ResponseDto<List<TaxBracketDto>>> GetBracketsAsync(int year, CancellationToken ct)
        {
            if (!CanRead) return Bad<List<TaxBracketDto>>(NotAllowed);
            List<FnTaxBracket> rows = await _uow.Set<FnTaxBracket>().AsNoTracking().Where(b => b.Year == year).OrderBy(b => b.FromAmount).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(b => new TaxBracketDto { FromAmount = b.FromAmount, ToAmount = b.ToAmount, RatePercent = b.RatePercent }).ToList());
        }

        public async Task<ResponseDto<List<TaxBracketDto>>> SaveBracketsAsync(SaveTaxBracketsDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<List<TaxBracketDto>>(NotAllowed);
            if (dto == null || !ValidYear(dto.Year)) return Bad<List<TaxBracketDto>>("سال نامعتبر است.");
            if (dto.Brackets == null || dto.Brackets.Count == 0) return Bad<List<TaxBracketDto>>("حداقل یک پله مالیاتی لازم است.");
            List<TaxBracketDto> sorted = dto.Brackets.OrderBy(b => b.FromAmount).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                TaxBracketDto b = sorted[i];
                if (b.FromAmount < 0 || b.RatePercent < 0 || b.RatePercent > 100) return Bad<List<TaxBracketDto>>("مبلغ یا نرخ پله نامعتبر است.");
                bool last = i == sorted.Count - 1;
                if (last && b.ToAmount.HasValue) return Bad<List<TaxBracketDto>>("پله آخر باید سقف نداشته باشد.");
                if (!last && (!b.ToAmount.HasValue || b.ToAmount.Value <= b.FromAmount)) return Bad<List<TaxBracketDto>>("سقف هر پله باید از کف آن بیشتر باشد.");
                if (!last && sorted[i + 1].FromAmount != b.ToAmount.Value) return Bad<List<TaxBracketDto>>("پله‌ها باید پیوسته باشند (کف پله بعد برابر سقف پله قبل).");
            }
            if (await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == dto.Year && r.Status >= FnPayrollStatus.Approved, ct))
                return Bad<List<TaxBracketDto>>("برای این سال فیش حقوق تاییدشده وجود دارد؛ جدول مالیات تغییر نمی‌کند.");
            List<FnTaxBracket> old = await _uow.Set<FnTaxBracket>().Where(b => b.Year == dto.Year).ToListAsync(ct);
            _uow.Set<FnTaxBracket>().RemoveRange(old);
            await _uow.SaveChangesAsync(ct); // the unique index (Year, FromAmount): the old rows go first
            foreach (TaxBracketDto b in sorted)
                _uow.Set<FnTaxBracket>().Add(new FnTaxBracket { Year = dto.Year, FromAmount = R(b.FromAmount), ToAmount = b.ToAmount.HasValue ? R(b.ToAmount.Value) : (decimal?)null, RatePercent = b.RatePercent });
            await ReopenCalculatedRunsAsync(dto.Year, ct);
            await _uow.SaveChangesAsync(ct);
            return await GetBracketsAsync(dto.Year, ct);
        }

        // ================================================================================== attendance

        private async Task<bool> LockedAsync(int year, int month, int? workshopId, CancellationToken ct) =>
            await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == year && r.Month == month && r.Status >= FnPayrollStatus.Approved && (r.WorkshopId == null || r.WorkshopId == workshopId), ct);

        private static AttendanceDto ToDto(FnAttendanceSheet s, FnEmployee e)
        {
            Dictionary<string, decimal> inputs = new Dictionary<string, decimal>();
            if (!string.IsNullOrWhiteSpace(s.InputsJson))
            {
                try { inputs = JsonSerializer.Deserialize<Dictionary<string, decimal>>(s.InputsJson) ?? inputs; }
                catch (JsonException) { /* an unreadable cell is shown empty */ }
            }
            return new AttendanceDto
            {
                Id = s.Id, EmployeeId = s.EmployeeId, PersonnelCode = e?.PersonnelCode, EmployeeName = e == null ? null : (e.FirstName + " " + e.LastName).Trim(), Year = s.Year, Month = s.Month,
                DaysWorked = s.DaysWorked, OvertimeHours = s.OvertimeHours, NightHours = s.NightHours, HolidayHours = s.HolidayHours, ShiftType = s.ShiftType,
                PaidLeaveDays = s.PaidLeaveDays, SickLeaveDays = s.SickLeaveDays, UnpaidLeaveDays = s.UnpaidLeaveDays, AbsenceDays = s.AbsenceDays, MissionDays = s.MissionDays, Inputs = inputs,
                WorkOrders = (s.WorkOrders ?? new List<FnAttendanceWorkOrder>()).Select(w => new AttendanceWorkOrderDto { WorkOrderId = w.WorkOrderId, Hours = w.Hours }).ToList(),
            };
        }

        public async Task<ResponseDto<PagedList<AttendanceDto>>> GetAttendanceAsync(AttendanceQuery query, CancellationToken ct)
        {
            query ??= new AttendanceQuery();
            if (!CanRead) return Bad<PagedList<AttendanceDto>>(NotAllowed);
            IQueryable<FnAttendanceSheet> q = _uow.Set<FnAttendanceSheet>().AsNoTracking().Where(s => s.Year == query.Year && s.Month == query.Month);
            if (query.EmployeeId.HasValue) q = q.Where(s => s.EmployeeId == query.EmployeeId.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnAttendanceSheet> rows = await q.Include(s => s.Employee).Include(s => s.WorkOrders).OrderBy(s => s.Employee.PersonnelCode).Skip((number - 1) * size).Take(size).AsSplitQuery().ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(s => ToDto(s, s.Employee)).ToList(), total, number, size));
        }

        private async Task<(string Error, FnAttendanceSheet Sheet, FnEmployee Employee)> SaveSheetAsync(SaveAttendanceDto dto, CancellationToken ct)
        {
            if (!ValidYear(dto.Year) || dto.Month < 1 || dto.Month > 12) return ("سال یا ماه نامعتبر است.", null, null);
            FnEmployee emp = null;
            if (dto.EmployeeId.HasValue) emp = await _uow.Set<FnEmployee>().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId.Value, ct);
            else if (Clean(dto.PersonnelCode) != null)
            {
                string pc = Clean(Digits(dto.PersonnelCode));
                emp = await _uow.Set<FnEmployee>().FirstOrDefaultAsync(e => e.PersonnelCode == pc, ct);
            }
            if (emp == null) return ("کارمند پیدا نشد.", null, null);
            if (!emp.IsActive) return ("کارمند غیرفعال است.", null, null);
            if (await LockedAsync(dto.Year, dto.Month, emp.WorkshopId, ct)) return ("فیش حقوق این ماه تایید شده و کارکرد قابل تغییر نیست.", null, null);
            if (dto.DaysWorked < 0 || dto.DaysWorked > 31 || dto.PaidLeaveDays < 0 || dto.MissionDays < 0 || dto.UnpaidLeaveDays < 0 || dto.AbsenceDays < 0
                || dto.DaysWorked + dto.PaidLeaveDays + dto.MissionDays > 31)
                return ("روزهای کارکرد نامعتبر است (حداکثر ۳۱ روز).", null, null);
            int monthDays = PersianCal.DaysIn(dto.Year, dto.Month);
            if (dto.DaysWorked + dto.UnpaidLeaveDays + dto.AbsenceDays > monthDays)
                return ($"روزهای کارکرد و غیبت از تعداد روزهای این ماه ({monthDays} روز) بیشتر است.", null, null);
            if (dto.OvertimeHours < 0 || dto.NightHours < 0 || dto.HolidayHours < 0 || dto.OvertimeHours > 400 || dto.NightHours > 400 || dto.HolidayHours > 400) return ("ساعات کارکرد نامعتبر است.", null, null);
            if (dto.ShiftType < 0 || dto.ShiftType > 3) return ("نوع نوبت‌کاری نامعتبر است.", null, null);

            var inputs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (dto.Inputs != null && dto.Inputs.Count > 0)
            {
                HashSet<string> allowed = (await _uow.Set<FnPayrollItem>().AsNoTracking().Where(i => i.IsActive && i.Calculation == FnPayrollCalc.Input).Select(i => i.Code).ToListAsync(ct))
                    .Select(c => c.ToUpperInvariant()).ToHashSet();
                foreach (KeyValuePair<string, decimal> kv in dto.Inputs)
                {
                    string code = (kv.Key ?? "").Trim().ToUpperInvariant();
                    if (!allowed.Contains(code)) return ($"قلم ورودی «{kv.Key}» تعریف نشده است.", null, null);
                    if (kv.Value < 0) return ("مبلغ قلم ورودی نمی‌تواند منفی باشد.", null, null);
                    inputs[code] = R(kv.Value);
                }
            }
            var workOrders = new List<AttendanceWorkOrderDto>();
            if (dto.WorkOrders != null)
            {
                if (dto.WorkOrders.GroupBy(w => w.WorkOrderId).Any(g => g.Count() > 1)) return ("دستور کار تکراری است.", null, null);
                foreach (AttendanceWorkOrderDto w in dto.WorkOrders)
                {
                    if (w.Hours <= 0 || w.Hours > 744) return ("ساعت دستور کار نامعتبر است.", null, null);
                    if (!await _uow.Set<WorkOrder>().AnyAsync(x => x.Id == w.WorkOrderId, ct)) return ("دستور کار پیدا نشد.", null, null);
                    workOrders.Add(w);
                }
            }

            FnAttendanceSheet sheet = await _uow.Set<FnAttendanceSheet>().Include(s => s.WorkOrders).FirstOrDefaultAsync(s => s.EmployeeId == emp.Id && s.Year == dto.Year && s.Month == dto.Month, ct);
            if (sheet == null)
            {
                sheet = new FnAttendanceSheet { EmployeeId = emp.Id, Year = dto.Year, Month = dto.Month, WorkOrders = new List<FnAttendanceWorkOrder>() };
                _uow.Set<FnAttendanceSheet>().Add(sheet);
            }
            sheet.DaysWorked = dto.DaysWorked; sheet.OvertimeHours = dto.OvertimeHours; sheet.NightHours = dto.NightHours; sheet.HolidayHours = dto.HolidayHours;
            sheet.ShiftType = dto.ShiftType; sheet.PaidLeaveDays = dto.PaidLeaveDays; sheet.UnpaidLeaveDays = dto.UnpaidLeaveDays; sheet.AbsenceDays = dto.AbsenceDays;
            sheet.MissionDays = dto.MissionDays;
            sheet.InputsJson = inputs.Count == 0 ? null : JsonSerializer.Serialize(inputs);
            sheet.UpdatedAt = DateTimeOffset.Now; sheet.UpdatedByUserId = Me;
            foreach (FnAttendanceWorkOrder gone in sheet.WorkOrders.Where(x => workOrders.All(w => w.WorkOrderId != x.WorkOrderId)).ToList())
            {
                sheet.WorkOrders.Remove(gone);
                _uow.Set<FnAttendanceWorkOrder>().Remove(gone);
            }
            foreach (AttendanceWorkOrderDto w in workOrders)
            {
                FnAttendanceWorkOrder existing = sheet.WorkOrders.FirstOrDefault(x => x.WorkOrderId == w.WorkOrderId);
                if (existing != null) existing.Hours = w.Hours;
                else sheet.WorkOrders.Add(new FnAttendanceWorkOrder { WorkOrderId = w.WorkOrderId, Hours = w.Hours });
            }
            return (null, sheet, emp);
        }

        public async Task<ResponseDto<AttendanceDto>> SaveAttendanceAsync(SaveAttendanceDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<AttendanceDto>(NotAllowed);
            if (dto == null) return Bad<AttendanceDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            var result = await SaveSheetAsync(dto, ct);
            if (result.Error != null) return Bad<AttendanceDto>(result.Error);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<AttendanceDto>(Conflict); }
            return ResponseDtoHelper.Ok(ToDto(result.Sheet, result.Employee));
        }

        public async Task<ResponseDto<ImportAttendanceResultDto>> ImportAttendanceAsync(ImportAttendanceDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<ImportAttendanceResultDto>(NotAllowed);
            if (dto == null || dto.Rows == null || dto.Rows.Count == 0) return Bad<ImportAttendanceResultDto>("ردیفی برای ورود ارسال نشده است.");
            if (dto.Rows.Count > 2000) return Bad<ImportAttendanceResultDto>("حداکثر ۲۰۰۰ ردیف در هر بار.");
            await EnsureAsync(ct);
            var outcome = new ImportAttendanceResultDto();
            int row = 0;
            foreach (SaveAttendanceDto r in dto.Rows)
            {
                row++;
                r.Year = dto.Year; r.Month = dto.Month;
                var result = await SaveSheetAsync(r, ct);
                if (result.Error != null) { outcome.Errors.Add($"ردیف {row}: {result.Error}"); continue; }
                try { await _uow.SaveChangesAsync(ct); outcome.Imported++; }
                catch (DbUpdateException)
                {
                    outcome.Errors.Add($"ردیف {row}: ذخیره نشد؛ ورود متوقف شد (ردیف‌های قبلی ثبت شده‌اند).");
                    break;
                }
            }
            return ResponseDtoHelper.Ok(outcome);
        }

        public async Task<ResponseDto<List<LeaveBalanceDto>>> GetLeaveBalancesAsync(int year, CancellationToken ct)
        {
            if (!CanRead) return Bad<List<LeaveBalanceDto>>(NotAllowed);
            if (!ValidYear(year)) return Bad<List<LeaveBalanceDto>>("سال نامعتبر است.");
            var rows = await _uow.Set<FnLeaveBalance>().AsNoTracking().Where(b => b.Year == year).ToListAsync(ct);
            List<int> ids = rows.Select(r => r.EmployeeId).ToList();
            Dictionary<int, string> codes = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.PersonnelCode, ct);
            return ResponseDtoHelper.Ok(rows.OrderBy(r => codes.TryGetValue(r.EmployeeId, out string c) ? c : "").Select(r => new LeaveBalanceDto
            {
                EmployeeId = r.EmployeeId, PersonnelCode = codes.TryGetValue(r.EmployeeId, out string pc) ? pc : null, Year = r.Year, CarriedForward = r.CarriedForward,
                Entitled = r.Entitled, Used = r.Used, Balance = r.CarriedForward + r.Entitled - r.Used,
            }).ToList());
        }
    }
}
