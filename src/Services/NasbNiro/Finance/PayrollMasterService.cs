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

        // release R1f: maker-checker controls on the payroll inputs
        Task<ResponseDto<PagedList<EmployeeChangeDto>>> GetEmployeeChangesAsync(EmployeeChangeQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<EmployeeChangeDto>> EmployeeChangeActionAsync(EmployeeChangeActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<EmployeeWageDto>>> GetEmployeeWagesAsync(int employeeId, CancellationToken cancellationToken);
        Task<ResponseDto<LoanDto>> LoanActionAsync(LoanActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ViewModels.NasbNiro.OfficeDtos.AttendanceCorrectionDto>> ProposeCorrectionAsync(ViewModels.NasbNiro.OfficeDtos.ProposeCorrectionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<LeaveOpeningDto>>> GetLeaveOpeningsAsync(LeaveOpeningQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LeaveOpeningDto>> SaveLeaveOpeningAsync(SaveLeaveOpeningDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ImportLeaveOpeningResultDto>> ImportLeaveOpeningsAsync(ImportLeaveOpeningDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LeaveOpeningDto>> LeaveOpeningActionAsync(LeaveOpeningActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Payroll master data: insurance workshops, employees, loans, payroll items, the dated rate table, tax brackets, attendance and leave.
    /// Confidential: only PAYROLL, FINANCEMANAGER and CEO read it; only PAYROLL and FINANCEMANAGER change it. Wages are never written to the log.
    /// </summary>
    public partial class PayrollMasterService : IPayrollMasterService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public PayrollMasterService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
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
            await R1fUpgrades.EnsureAsync(_uow, Me, ct);
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

        private static EmployeeDto ToDto(FnEmployee e, string workshop, int? pendingChangeId = null) => new EmployeeDto
        {
            Id = e.Id, PersonnelCode = e.PersonnelCode, FirstName = e.FirstName, LastName = e.LastName, FatherName = e.FatherName, NationalCode = e.NationalCode,
            BirthDate = e.BirthDate, BirthPlace = e.BirthPlace, IdentityCardNumber = e.IdentityCardNumber, Nationality = e.Nationality, Gender = e.Gender, MaritalStatus = e.MaritalStatus, ChildrenCount = e.ChildrenCount,
            InsuranceNumber = e.InsuranceNumber, InsuranceJobCode = e.InsuranceJobCode, WorkshopId = e.WorkshopId, WorkshopName = workshop, HireDate = e.HireDate,
            EndDate = e.EndDate, EmploymentType = e.EmploymentType, JobTitle = e.JobTitle, Unit = e.Unit, CostCenterId = e.CostCenterId, DefaultWorkOrderId = e.DefaultWorkOrderId,
            BankIban = e.BankIban, DailyWage = e.DailyWage, ExemptionFlags = e.ExemptionFlags, HardAndHarmful = e.HardAndHarmful, UserId = e.UserId,
            QualifiedPersonId = e.QualifiedPersonId, IsActive = e.IsActive, PendingApproval = e.PendingApproval, PendingChangeId = pendingChangeId,
        };

        /// <summary>The open change request of each employee (at most one is allowed at a time).</summary>
        private async Task<Dictionary<int, int>> PendingChangeIdsAsync(IEnumerable<int> employeeIds, CancellationToken ct)
        {
            List<int> ids = employeeIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, int>();
            var rows = await _uow.Set<FnEmployeeChange>().AsNoTracking().Where(c => c.Status == FnChangeStatus.Pending && ids.Contains(c.EmployeeId)).Select(c => new { c.EmployeeId, c.Id }).ToListAsync(ct);
            return rows.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.Max(r => r.Id));
        }

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
            Dictionary<int, int> pending = await PendingChangeIdsAsync(rows.Select(e => e.Id), ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(e => ToDto(e, e.Workshop?.Name, pending.TryGetValue(e.Id, out int pc) ? pc : (int?)null)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<EmployeeDto>> GetEmployeeAsync(int id, CancellationToken ct)
        {
            if (!CanRead) return Bad<EmployeeDto>(NotAllowed);
            FnEmployee e = await _uow.Set<FnEmployee>().AsNoTracking().Include(x => x.Workshop).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e == null) return ResponseDtoHelper.NotFound<EmployeeDto>("کارمند پیدا نشد.");
            Dictionary<int, int> pending = await PendingChangeIdsAsync(new[] { e.Id }, ct);
            return ResponseDtoHelper.Ok(ToDto(e, e.Workshop?.Name, pending.TryGetValue(e.Id, out int pc) ? pc : (int?)null));
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
            if ((Clean(dto.IdentityCardNumber)?.Length ?? 0) > 20) return Bad<EmployeeDto>("شماره شناسنامه حداکثر ۲۰ نویسه است.");
            if ((Clean(dto.Nationality)?.Length ?? 0) > 50) return Bad<EmployeeDto>("تابعیت حداکثر ۵۰ نویسه است.");
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

            // release R1f: the month a changed daily wage applies from (default: the current Persian month)
            if (dto.WageEffectiveYear.HasValue != dto.WageEffectiveMonth.HasValue) return Bad<EmployeeDto>("سال و ماه اعمال مزد را با هم وارد کنید.");
            DateTime? wageFrom = null;
            if (dto.WageEffectiveYear.HasValue)
            {
                if (!ValidYear(dto.WageEffectiveYear.Value) || dto.WageEffectiveMonth.Value < 1 || dto.WageEffectiveMonth.Value > 12) return Bad<EmployeeDto>("ماه اعمال مزد نامعتبر است.");
                wageFrom = PersianCal.Start(dto.WageEffectiveYear.Value, dto.WageEffectiveMonth.Value);
            }

            bool created = !dto.Id.HasValue;
            FnEmployee e;
            if (!created)
            {
                e = await _uow.Set<FnEmployee>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (e == null) return ResponseDtoHelper.NotFound<EmployeeDto>("کارمند پیدا نشد.");
            }
            else
            {
                // a new employee is paid by a run only after a second PAYROLL / FINANCEMANAGER user approved it
                e = new FnEmployee { CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me, PendingApproval = true };
                _uow.Set<FnEmployee>().Add(e);
            }
            if (await _uow.Set<FnEmployee>().AnyAsync(x => x.PersonnelCode == code && x.Id != e.Id, ct)) return Bad<EmployeeDto>("این کد پرسنلی قبلاً ثبت شده است.");
            if (await _uow.Set<FnEmployee>().AnyAsync(x => x.NationalCode == national && x.Id != e.Id, ct)) return Bad<EmployeeDto>("این کد ملی قبلاً ثبت شده است.");

            // release R1f: the guarded fields (daily wage, exemption flags, children, hard-and-harmful, IBAN) of an approved employee change only through a request that a second user approves;
            // until then the row keeps the approved values and a payroll run uses them. A new employee holds them on the row but is skipped by the runs until he is approved.
            var proposed = new GuardedFieldsDto { DailyWage = wage, ExemptionFlags = flags, ChildrenCount = dto.ChildrenCount, HardAndHarmful = dto.HardAndHarmful, BankIban = iban };
            DateTimeOffset now = DateTimeOffset.Now;
            bool guardedOnRow = created || e.PendingApproval;
            FnEmployeeChange change = null;
            if (!guardedOnRow && !SameGuarded(GuardedOf(e), proposed))
            {
                string reason = Clean(dto.ChangeReason);
                if (reason == null) return Bad<EmployeeDto>("برای تغییر مزد روزانه، پرچم‌های معافیت، تعداد فرزند، سخت و زیان‌آور یا شماره شبا، دلیل تغییر را بنویسید؛ تغییر پس از تایید نفر دوم اعمال می‌شود.");
                if (await _uow.Set<FnEmployeeChange>().AnyAsync(c => c.EmployeeId == e.Id && c.Status == FnChangeStatus.Pending, ct)) return Bad<EmployeeDto>("تغییر دیگری برای این کارمند در انتظار تایید است؛ ابتدا آن را تایید یا رد کنید.");
                DateTime? effective = null;
                if (wage != e.DailyWage)
                {
                    effective = wageFrom ?? WageHistory.MonthStart(DateTime.Today);
                    string guard = await WageMonthGuardAsync(e.Id, effective.Value, ct);
                    if (guard != null) return Bad<EmployeeDto>(guard);
                }
                change = new FnEmployeeChange
                {
                    Employee = e, Kind = FnEmployeeChangeKind.Update, Status = FnChangeStatus.Pending, ProposedJson = GuardedJson(proposed), WageEffectiveFrom = effective,
                    Reason = reason.Length > 500 ? reason.Substring(0, 500) : reason, ProposedByUserId = Me, ProposedAt = now,
                };
                _uow.Set<FnEmployeeChange>().Add(change);
            }

            e.PersonnelCode = code; e.FirstName = Clean(dto.FirstName); e.LastName = Clean(dto.LastName); e.FatherName = Clean(dto.FatherName); e.NationalCode = national;
            e.BirthDate = dto.BirthDate?.Date; e.BirthPlace = Clean(dto.BirthPlace); e.IdentityCardNumber = Clean(dto.IdentityCardNumber); e.Nationality = Clean(dto.Nationality); e.Gender = dto.Gender; e.MaritalStatus = dto.MaritalStatus;
            e.InsuranceNumber = Clean(dto.InsuranceNumber); e.InsuranceJobCode = Clean(dto.InsuranceJobCode); e.WorkshopId = dto.WorkshopId; e.HireDate = dto.HireDate.Date;
            e.EndDate = dto.EndDate?.Date; e.EmploymentType = dto.EmploymentType; e.JobTitle = Clean(dto.JobTitle); e.Unit = Clean(dto.Unit); e.CostCenterId = dto.CostCenterId;
            e.DefaultWorkOrderId = dto.DefaultWorkOrderId;
            e.UserId = dto.UserId; e.QualifiedPersonId = dto.QualifiedPersonId; e.IsActive = dto.IsActive;
            if (guardedOnRow) { e.BankIban = iban; e.DailyWage = wage; e.ChildrenCount = dto.ChildrenCount; e.ExemptionFlags = flags; e.HardAndHarmful = dto.HardAndHarmful; }

            if (created || e.PendingApproval)
            {
                // the request of the first approval follows what is on the row (a rejected one is submitted again by saving the employee again)
                FnEmployeeChange open = created ? null : await _uow.Set<FnEmployeeChange>().FirstOrDefaultAsync(c => c.EmployeeId == e.Id && c.Status == FnChangeStatus.Pending && c.Kind == FnEmployeeChangeKind.Create, ct);
                if (open != null)
                {
                    open.ProposedJson = GuardedJson(proposed);
                    if (wageFrom.HasValue) open.WageEffectiveFrom = wageFrom;
                    change = open;
                }
                else
                {
                    string reason = Clean(dto.ChangeReason) ?? "ثبت کارمند جدید";
                    change = new FnEmployeeChange
                    {
                        Employee = e, Kind = FnEmployeeChangeKind.Create, Status = FnChangeStatus.Pending, ProposedJson = GuardedJson(proposed), WageEffectiveFrom = wageFrom ?? WageHistory.MonthStart(e.HireDate),
                        Reason = reason.Length > 500 ? reason.Substring(0, 500) : reason, ProposedByUserId = Me, ProposedAt = now,
                    };
                    _uow.Set<FnEmployeeChange>().Add(change);
                }
            }

            // who touched the master row since the last payroll run it was part of (they may not approve the next one)
            if (created || change != null || _uow.Entry(e).State == EntityState.Modified)
            {
                e.EditorUserIds = WithEditor(e.EditorUserIds, Me); e.LastEditedByUserId = Me; e.LastEditedAt = now;
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<EmployeeDto>(Conflict); }
            if (change != null)
                await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "empchg:" + change.Id, true, "finance", "employee-change", change.Id,
                    $"تایید {(change.Kind == FnEmployeeChangeKind.Create ? "کارمند جدید" : "تغییر مشخصات حقوقی کارمند")} با کد پرسنلی {e.PersonnelCode}", "/finance/payroll/employee-changes",
                    await SelfApproval.GateRolesAsync(_uow, e, FinanceRoles.PayrollUsers, true, ct), ct);
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

        private static LoanDto ToDto(FnEmployeeLoan l, string personnel, Dictionary<int, string> names = null) => new LoanDto
        {
            Id = l.Id, EmployeeId = l.EmployeeId, PersonnelCode = personnel, Kind = l.Kind, Title = l.Title, Principal = l.Principal, InstallmentAmount = l.InstallmentAmount,
            Balance = l.Balance, FirstYear = l.FirstYear, FirstMonth = l.FirstMonth, IsActive = l.IsActive,
            ApprovalStatus = l.ApprovalStatus.ToString(), CreatedByName = names?.Name(l.CreatedByUserId), ApprovedByName = names?.Name(l.ApprovedByUserId), ApprovedAt = l.ApprovedAt,
            RejectReason = l.RejectReason,
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
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(l => (int?)l.CreatedByUserId).Concat(rows.Select(l => l.ApprovedByUserId)), ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(l => ToDto(l, l.Employee?.PersonnelCode, names)).ToList(), total, number, size));
        }

        /// <summary>
        /// A loan or advance is a request (release R1f): it is recovered from the pay and paid out only after the finance manager - never the person who created or changed it - approved it.
        /// Changing a loan that is not yet recovered sends it back to the approval; a loan that has instalments behind it can only be switched off (never on again without a new request).
        /// </summary>
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
            DateTimeOffset now = DateTimeOffset.Now;
            FnEmployeeLoan l;
            if (dto.Id.HasValue)
            {
                l = await _uow.Set<FnEmployeeLoan>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (l == null) return ResponseDtoHelper.NotFound<LoanDto>("وام پیدا نشد.");
                if (l.EmployeeId != emp.Id) return Bad<LoanDto>("کارمند وام تغییر نمی‌کند؛ وام جدید ثبت کنید.");
                if (l.Balance != l.Principal)
                {
                    bool sameTerms = l.EmployeeId == emp.Id && l.Kind == dto.Kind && l.Principal == principal && l.InstallmentAmount == installment && l.FirstYear == dto.FirstYear && l.FirstMonth == dto.FirstMonth;
                    if (!sameTerms || (dto.IsActive && !l.IsActive)) return Bad<LoanDto>("از این وام قسطی کسر شده است؛ فقط غیرفعال کردن آن ممکن است.");
                    if (dto.IsActive == l.IsActive) return ResponseDtoHelper.Ok(ToDto(l, emp.PersonnelCode, await InventoryUsers.GetNamesAsync(_uow, new int?[] { l.CreatedByUserId, l.ApprovedByUserId }, ct)));
                    l.IsActive = false;
                    l.EditorUserIds = WithEditor(l.EditorUserIds, Me); l.LastEditedByUserId = Me; l.LastEditedAt = now;
                    await ReopenCalculatedRunsAsync(null, ct);
                    try { await _uow.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException) { return Bad<LoanDto>(Conflict); }
                    return ResponseDtoHelper.Ok(ToDto(l, emp.PersonnelCode, await InventoryUsers.GetNamesAsync(_uow, new int?[] { l.CreatedByUserId, l.ApprovedByUserId }, ct)));
                }
                // saving a loan without a change does not send it back to the approval
                if (l.Kind == dto.Kind && l.Principal == principal && l.InstallmentAmount == installment && l.FirstYear == dto.FirstYear && l.FirstMonth == dto.FirstMonth && l.IsActive == dto.IsActive && Clean(l.Title) == Clean(dto.Title))
                    return ResponseDtoHelper.Ok(ToDto(l, emp.PersonnelCode, await InventoryUsers.GetNamesAsync(_uow, new int?[] { l.CreatedByUserId, l.ApprovedByUserId }, ct)));
            }
            else
            {
                l = new FnEmployeeLoan { EmployeeId = emp.Id, CreatedAt = now, CreatedByUserId = Me };
                _uow.Set<FnEmployeeLoan>().Add(l);
            }
            l.Kind = dto.Kind; l.Title = Clean(dto.Title); l.Principal = principal; l.InstallmentAmount = installment; l.Balance = principal;
            l.FirstYear = dto.FirstYear; l.FirstMonth = dto.FirstMonth; l.IsActive = dto.IsActive;
            // a new or changed loan is a request again
            l.ApprovalStatus = FnLoanApproval.Pending; l.ApprovedByUserId = null; l.ApprovedAt = null; l.RejectReason = null;
            l.EditorUserIds = WithEditor(l.EditorUserIds, Me); l.LastEditedByUserId = Me; l.LastEditedAt = now;
            await ReopenCalculatedRunsAsync(null, ct);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<LoanDto>(Conflict); }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "loan:" + l.Id, true, "finance", "payroll-loan", l.Id,
                $"تایید {(l.Kind == FnLoanKind.Advance ? "مساعده" : "وام")} کارمند با کد پرسنلی {emp.PersonnelCode}", "/finance/payroll/loans",
                await SelfApproval.GateRolesAsync(_uow, emp, FinanceRoles.Managers, true, ct), ct);
            return ResponseDtoHelper.Ok(ToDto(l, emp.PersonnelCode, await InventoryUsers.GetNamesAsync(_uow, new int?[] { l.CreatedByUserId, l.ApprovedByUserId }, ct)));
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
            // release R1f: the keys that replaced hard-coded numbers have a range (the month length, the night window, the sick-pay basis ...)
            string range = PayrollKeys.Validate(key, dto.Value);
            if (range != null) return Bad<PayrollRateDto>(range);
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
                Source = s.Source.ToString(), Locked = s.Locked,
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

        /// <summary>The monthly overtime cap (hours) of the attendance policy: the overtime and the holiday work of a sheet share it.</summary>
        private async Task<decimal> OvertimeCapHoursAsync(CancellationToken ct)
        {
            global::Entities.Model.NasbNiro.Office.OfAttendancePolicy policy = await _uow.Set<global::Entities.Model.NasbNiro.Office.OfAttendancePolicy>().AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct)
                ?? new global::Entities.Model.NasbNiro.Office.OfAttendancePolicy();
            return policy.MonthlyOvertimeCapMinutes / 60m;
        }

        private async Task<(string Error, FnAttendanceSheet Sheet, FnEmployee Employee)> SaveSheetAsync(SaveAttendanceDto dto, FnSheetSource source, decimal capHours, CancellationToken ct)
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
            // release R1f: the same checks as the HR close and a correction (the bound is the policy cap of the month, not a fixed number of hours)
            var numbers = new ViewModels.NasbNiro.OfficeDtos.SheetNumbersDto
            {
                DaysWorked = dto.DaysWorked, OvertimeHours = dto.OvertimeHours, NightHours = dto.NightHours, HolidayHours = dto.HolidayHours, ShiftType = dto.ShiftType, PaidLeaveDays = dto.PaidLeaveDays,
                SickLeaveDays = dto.SickLeaveDays, UnpaidLeaveDays = dto.UnpaidLeaveDays, AbsenceDays = dto.AbsenceDays, MissionDays = dto.MissionDays,
            };
            string numberError = SheetNumbers.Validate(numbers, dto.Year, dto.Month, capHours);
            if (numberError != null) return (numberError, null, null);

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
            // release R1f review M2: for a month HR has prepared or confirmed, payroll neither types nor imports a new sheet nor changes the numbers of one (inputs and work-order hours stay payroll's own)
            if (sheet == null || !SheetNumbers.Same(SheetNumbers.Of(sheet), numbers))
            {
                string closedRefusal = HrClose.SheetEntryRefusal(dto.Year, dto.Month, await HrClose.GetAsync(_uow, dto.Year, dto.Month, ct), sheet != null);
                if (closedRefusal != null) return (closedRefusal, null, null);
            }
            if (sheet != null && sheet.Locked && !SheetNumbers.Same(SheetNumbers.Of(sheet), numbers))
                return ("ارقام کارکرد این برگه با بستن ماه توسط منابع انسانی قفل شده است؛ برای تغییر آن «درخواست اصلاح کارکرد» ثبت کنید تا منابع انسانی (نفر دیگری) آن را تایید کند.", null, null);
            bool isNew = sheet == null;
            string beforeNumbers = isNew ? null : SheetNumbers.ToJson(SheetNumbers.Of(sheet)), beforeInputs = sheet?.InputsJson, beforeOrders = isNew ? null : OrdersKey(sheet.WorkOrders);
            if (isNew)
            {
                sheet = new FnAttendanceSheet { EmployeeId = emp.Id, Year = dto.Year, Month = dto.Month, WorkOrders = new List<FnAttendanceWorkOrder>(), Source = source };
                _uow.Set<FnAttendanceSheet>().Add(sheet);
            }
            else if (!sheet.Locked) sheet.Source = source;
            if (!sheet.Locked) SheetNumbers.Apply(numbers, sheet);
            sheet.InputsJson = inputs.Count == 0 ? null : JsonSerializer.Serialize(inputs);
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
            // the row is touched only when something changed: the editor may not approve the payroll run that uses the row
            bool changed = isNew || beforeNumbers != SheetNumbers.ToJson(SheetNumbers.Of(sheet)) || beforeInputs != sheet.InputsJson || beforeOrders != OrdersKey(sheet.WorkOrders);
            if (changed)
            {
                sheet.UpdatedAt = DateTimeOffset.Now; sheet.UpdatedByUserId = Me;
                sheet.EditorUserIds = WithEditor(sheet.EditorUserIds, Me);
            }
            return (null, sheet, emp);
        }

        private static string OrdersKey(IEnumerable<FnAttendanceWorkOrder> orders) =>
            string.Join(";", (orders ?? new List<FnAttendanceWorkOrder>()).OrderBy(w => w.WorkOrderId).Select(w => w.WorkOrderId + ":" + w.Hours.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)));

        public async Task<ResponseDto<AttendanceDto>> SaveAttendanceAsync(SaveAttendanceDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<AttendanceDto>(NotAllowed);
            if (dto == null) return Bad<AttendanceDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            var result = await SaveSheetAsync(dto, FnSheetSource.Manual, await OvertimeCapHoursAsync(ct), ct);
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
            decimal capHours = await OvertimeCapHoursAsync(ct);
            var outcome = new ImportAttendanceResultDto();
            int row = 0;
            foreach (SaveAttendanceDto r in dto.Rows)
            {
                row++;
                r.Year = dto.Year; r.Month = dto.Month;
                var result = await SaveSheetAsync(r, FnSheetSource.Import, capHours, ct);
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
