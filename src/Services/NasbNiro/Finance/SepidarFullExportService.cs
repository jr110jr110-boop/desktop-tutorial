using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface ISepidarFullExportService
    {
        Task<ResponseDto<bool>> StatusAsync(CancellationToken cancellationToken);
        Task<ResponseDto<List<ExportLayoutColumnDto>>> GetLayoutAsync(CancellationToken cancellationToken);
        Task<ResponseDto<List<ExportLayoutColumnDto>>> SaveLayoutAsync(SaveExportLayoutDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<ExportMappingRowDto>>> GetAccountMappingAsync(bool onlyUnmapped, CancellationToken cancellationToken);
        Task<ResponseDto<ExportMappingRowDto>> SaveAccountMappingAsync(SaveExportMappingDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<ExportMappingRowDto>>> GetDetailMappingAsync(bool onlyUnmapped, CancellationToken cancellationToken);
        Task<ResponseDto<ExportMappingRowDto>> SaveDetailMappingAsync(SaveExportMappingDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ExportPreviewDto>> PreviewAsync(int periodId, CancellationToken cancellationToken);
        Task<ResponseDto<VoucherExportDto>> ExportAsync(ExportPeriodDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<VoucherExportDto>>> GetExportsAsync(int? periodId, CancellationToken cancellationToken);
        /// <summary>The file of an export, exactly as it was created (UTF-8 with BOM).</summary>
        Task<ResponseDto<byte[]>> DownloadAsync(int id, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Fallback path for the final vouchers of a period as a CSV file (opens in Excel) in a configurable column layout. Switched by the setting
    /// Finance:SepidarFullExport (off by default; the F2 outbox stays the default path). Dates are Persian. The file of an export is kept and
    /// never changed; a second export of the same period supersedes the first with a reason.
    /// </summary>
    public class SepidarFullExportService : ISepidarFullExportService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public SepidarFullExportService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string Disabled = "خروجی کامل اسناد برای سپیدار غیرفعال است (تنظیم Finance:SepidarFullExport).";
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        private static readonly (string Key, string Title)[] DefaultColumns =
        {
            ("VoucherNo", "شماره سند"), ("Date", "تاریخ"), ("Description", "شرح سند"), ("AccountCode", "کد حساب"), ("DetailCode", "کد تفصیلی"),
            ("Debit", "بدهکار"), ("Credit", "بستانکار"), ("CostCenter", "مرکز هزینه"), ("Project", "پروژه"),
        };
        private static readonly string[] KnownKeys = { "VoucherNo", "Date", "Description", "LineDescription", "AccountCode", "DetailCode", "Debit", "Credit", "CostCenter", "Project" };

        private async Task<bool> EnabledAsync(CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            string v = await _uow.Set<FnSetting>().AsNoTracking().Where(s => s.Key == FinanceSeed.SettingSepidarExport).Select(s => s.Value).FirstOrDefaultAsync(ct);
            return v == "1";
        }

        public async Task<ResponseDto<bool>> StatusAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<bool>(NotAllowed);
            return ResponseDtoHelper.Ok(await EnabledAsync(ct));
        }

        // ------------------------------------------------------------------------------------------ layout

        private async Task<List<FnExportLayoutColumn>> LayoutAsync(bool track, CancellationToken ct)
        {
            IQueryable<FnExportLayoutColumn> q = _uow.Set<FnExportLayoutColumn>();
            if (!track) q = q.AsNoTracking();
            List<FnExportLayoutColumn> cols = await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
            if (cols.Count > 0) return cols;
            int order = 1;
            foreach (var d in DefaultColumns) _uow.Set<FnExportLayoutColumn>().Add(new FnExportLayoutColumn { Key = d.Key, Title = d.Title, SortOrder = order++, IsActive = true });
            _uow.Set<FnExportLayoutColumn>().Add(new FnExportLayoutColumn { Key = "LineDescription", Title = "شرح ردیف", SortOrder = order, IsActive = false });
            await _uow.SaveChangesAsync(ct);
            IQueryable<FnExportLayoutColumn> again = _uow.Set<FnExportLayoutColumn>();
            if (!track) again = again.AsNoTracking();
            return await again.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        }

        private static ExportLayoutColumnDto ToDto(FnExportLayoutColumn c) => new ExportLayoutColumnDto { Key = c.Key, Title = c.Title, SortOrder = c.SortOrder, IsActive = c.IsActive };

        public async Task<ResponseDto<List<ExportLayoutColumnDto>>> GetLayoutAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<ExportLayoutColumnDto>>(NotAllowed);
            return ResponseDtoHelper.Ok((await LayoutAsync(false, ct)).Select(ToDto).ToList());
        }

        public async Task<ResponseDto<List<ExportLayoutColumnDto>>> SaveLayoutAsync(SaveExportLayoutDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<List<ExportLayoutColumnDto>>(NotAllowed + " (مدیر مالی)");
            List<ExportLayoutColumnDto> input = (dto?.Columns ?? new List<ExportLayoutColumnDto>()).Where(c => c != null).ToList();
            if (input.Count == 0) return Bad<List<ExportLayoutColumnDto>>("حداقل یک ستون لازم است.");
            if (input.Any(c => !KnownKeys.Contains(c.Key ?? "")) ) return Bad<List<ExportLayoutColumnDto>>("کلید ستون نامعتبر است.");
            if (input.Select(c => c.Key).Distinct().Count() != input.Count) return Bad<List<ExportLayoutColumnDto>>("کلید ستون تکراری است.");
            if (input.Any(c => Clean(c.Title) == null)) return Bad<List<ExportLayoutColumnDto>>("عنوان ستون را وارد کنید.");
            if (!input.Any(c => c.IsActive)) return Bad<List<ExportLayoutColumnDto>>("حداقل یک ستون باید فعال باشد.");
            List<FnExportLayoutColumn> cols = await LayoutAsync(true, ct);
            foreach (ExportLayoutColumnDto c in input)
            {
                FnExportLayoutColumn row = cols.FirstOrDefault(x => x.Key == c.Key);
                if (row == null) { row = new FnExportLayoutColumn { Key = c.Key }; _uow.Set<FnExportLayoutColumn>().Add(row); cols.Add(row); }
                row.Title = Clean(c.Title); row.SortOrder = c.SortOrder; row.IsActive = c.IsActive;
            }
            // the saved layout is the whole layout: columns that are not listed are switched off (never deleted)
            foreach (FnExportLayoutColumn old in cols.Where(c => input.All(i => i.Key != c.Key))) old.IsActive = false;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(cols.OrderByDescending(c => c.IsActive).ThenBy(c => c.SortOrder).ThenBy(c => c.Id).Select(ToDto).ToList());
        }

        // ------------------------------------------------------------------------------------------ mapping

        public async Task<ResponseDto<List<ExportMappingRowDto>>> GetAccountMappingAsync(bool onlyUnmapped, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<ExportMappingRowDto>>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            IQueryable<FnAccount> q = _uow.Set<FnAccount>().AsNoTracking().Where(a => a.Level == FnAccountLevel.Subsidiary && a.IsActive);
            if (onlyUnmapped) q = q.Where(a => a.SepidarCode == null || a.SepidarCode == "");
            var list = await q.OrderBy(a => a.Code).Select(a => new ExportMappingRowDto { Code = a.Code, Title = a.Title, SepidarCode = a.SepidarCode, Mapped = a.SepidarCode != null && a.SepidarCode != "" }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<ExportMappingRowDto>> SaveAccountMappingAsync(SaveExportMappingDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<ExportMappingRowDto>(NotAllowed + " (مدیر مالی)");
            string code = Digits(Clean(dto?.Code) ?? "");
            if (code.Length == 0) return Bad<ExportMappingRowDto>("کد حساب را وارد کنید.");
            FnAccount a = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (a == null) return ResponseDtoHelper.NotFound<ExportMappingRowDto>("حساب پیدا نشد.");
            string sep = Clean(dto.SepidarCode);
            if (sep != null && sep.Length > 50) return Bad<ExportMappingRowDto>("کد سپیدار بیش از حد طولانی است.");
            a.SepidarCode = sep;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new ExportMappingRowDto { Code = a.Code, Title = a.Title, SepidarCode = a.SepidarCode, Mapped = sep != null });
        }

        public async Task<ResponseDto<List<ExportMappingRowDto>>> GetDetailMappingAsync(bool onlyUnmapped, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<ExportMappingRowDto>>(NotAllowed);
            var list = await _uow.Set<FnDetailAccount>().AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Code)
                .Select(d => new { d.Id, d.Code, d.Title }).Take(5000).ToListAsync(ct);
            Dictionary<int, string> maps = await _uow.Set<FnDetailSepidarMap>().AsNoTracking().ToDictionaryAsync(m => m.DetailAccountId, m => m.SepidarCode, ct);
            var rows = list.Select(d => new ExportMappingRowDto { Code = d.Code, Title = d.Title, SepidarCode = maps.TryGetValue(d.Id, out string s) ? s : null, Mapped = maps.ContainsKey(d.Id) }).ToList();
            if (onlyUnmapped) rows = rows.Where(r => !r.Mapped).ToList();
            return ResponseDtoHelper.Ok(rows);
        }

        public async Task<ResponseDto<ExportMappingRowDto>> SaveDetailMappingAsync(SaveExportMappingDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<ExportMappingRowDto>(NotAllowed + " (مدیر مالی)");
            string code = Clean(dto?.Code);
            if (code == null) return Bad<ExportMappingRowDto>("کد تفصیلی را وارد کنید.");
            FnDetailAccount d = await _uow.Set<FnDetailAccount>().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (d == null) return ResponseDtoHelper.NotFound<ExportMappingRowDto>("تفصیلی پیدا نشد.");
            string sep = Clean(dto.SepidarCode);
            if (sep != null && sep.Length > 50) return Bad<ExportMappingRowDto>("کد سپیدار بیش از حد طولانی است.");
            FnDetailSepidarMap map = await _uow.Set<FnDetailSepidarMap>().FirstOrDefaultAsync(m => m.DetailAccountId == d.Id, ct);
            if (sep == null) { if (map != null) _uow.Set<FnDetailSepidarMap>().Remove(map); }
            else if (map == null) _uow.Set<FnDetailSepidarMap>().Add(new FnDetailSepidarMap { DetailAccountId = d.Id, SepidarCode = sep });
            else map.SepidarCode = sep;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new ExportMappingRowDto { Code = d.Code, Title = d.Title, SepidarCode = sep, Mapped = sep != null });
        }

        // ------------------------------------------------------------------------------------------ export

        private sealed class Built
        {
            public ExportPreviewDto Preview = new ExportPreviewDto();
            public string Content;
            public FnPeriod Period;
        }

        private async Task<Built> BuildAsync(int periodId, bool withContent, CancellationToken ct)
        {
            var built = new Built();
            ExportPreviewDto pv = built.Preview;
            pv.PeriodId = periodId;
            pv.Enabled = await EnabledAsync(ct);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == periodId, ct);
            if (period == null) { pv.Errors.Add("دوره پیدا نشد."); return built; }
            built.Period = period;
            if (period.Status != FnPeriodStatus.Closed) pv.Errors.Add("دوره بسته نشده است؛ خروجی فقط برای دوره بسته‌شده گرفته می‌شود (در دوره باز یا قفل‌شده هنوز ثبت سند ممکن است و خروجی کهنه می‌شود).");
            pv.AlreadyExported = await _uow.Set<FnVoucherExport>().AnyAsync(e => e.PeriodId == periodId && e.Status == FnExportStatus.Active, ct);

            int nonFinal = await _uow.Set<FnVoucher>().CountAsync(v => v.FiscalYearId == period.FiscalYearId && v.Date >= period.StartDate && v.Date <= period.EndDate && v.Status != FnVoucherStatus.Final, ct);
            if (nonFinal > 0) pv.Errors.Add($"{nonFinal} سند قطعی‌نشده در این دوره هست.");

            var lines = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Voucher.FiscalYearId == period.FiscalYearId && l.Voucher.Date >= period.StartDate && l.Voucher.Date <= period.EndDate && l.Voucher.Status == FnVoucherStatus.Final)
                .OrderBy(l => l.Voucher.Number).ThenBy(l => l.RowNumber)
                .Select(l => new
                {
                    VoucherId = l.VoucherId, l.Voucher.Number, l.Voucher.Date, VoucherDescription = l.Voucher.Description, l.Description, l.RowNumber,
                    AccountCode = l.Account.Code, AccountSepidar = l.Account.SepidarCode, l.Detail1Id, l.Detail2Id, l.Debit, l.Credit, l.CostCenterId, l.WorkOrderId, l.ContractId,
                }).ToListAsync(ct);
            pv.Vouchers = lines.Select(l => l.VoucherId).Distinct().Count();
            pv.Rows = lines.Count;
            pv.TotalDebit = lines.Sum(l => l.Debit);
            pv.TotalCredit = lines.Sum(l => l.Credit);

            List<int> detailIds = lines.SelectMany(l => new int?[] { l.Detail1Id, l.Detail2Id }).Where(x => x.HasValue).Select(x => x.Value).Distinct().ToList();
            var details = await _uow.Set<FnDetailAccount>().AsNoTracking().Where(d => detailIds.Contains(d.Id)).Select(d => new { d.Id, d.Code, d.Type }).ToDictionaryAsync(d => d.Id, ct);
            Dictionary<int, string> detailMap = await _uow.Set<FnDetailSepidarMap>().AsNoTracking().Where(m => detailIds.Contains(m.DetailAccountId)).ToDictionaryAsync(m => m.DetailAccountId, m => m.SepidarCode, ct);

            // dimension columns come from the line's own fields (cost centre, work order, contract) as well as from Detail2: each resolves to the matching detail and its Sepidar code
            var dimIds = new Dictionary<(FnDetailType, int), FnDetailAccount>();
            foreach (var l in lines)
            {
                if (l.CostCenterId.HasValue) dimIds[(FnDetailType.CostCenter, l.CostCenterId.Value)] = null;
                if (l.WorkOrderId.HasValue) dimIds[(FnDetailType.WorkOrder, l.WorkOrderId.Value)] = null;
                if (l.ContractId.HasValue) dimIds[(FnDetailType.Project, l.ContractId.Value)] = null;
            }
            var dimTypes = dimIds.Keys.Select(k => k.Item1).Distinct().ToList();
            var dimLinked = dimIds.Keys.Select(k => (int?)k.Item2).Distinct().ToList();
            var dimDetails = await _uow.Set<FnDetailAccount>().AsNoTracking().Where(d => dimTypes.Contains(d.Type) && d.LinkedEntityId != null && dimLinked.Contains(d.LinkedEntityId)).ToListAsync(ct);
            foreach (FnDetailAccount d in dimDetails) dimIds[(d.Type, d.LinkedEntityId.Value)] = d;
            List<int> dimDetailIds = dimDetails.Select(x => x.Id).ToList();
            Dictionary<int, string> dimMap = await _uow.Set<FnDetailSepidarMap>().AsNoTracking().Where(m => dimDetailIds.Contains(m.DetailAccountId)).ToDictionaryAsync(m => m.DetailAccountId, m => m.SepidarCode, ct);
            string DimCode(FnDetailType t, int? id) => id.HasValue && dimIds.TryGetValue((t, id.Value), out FnDetailAccount d) && d != null && dimMap.TryGetValue(d.Id, out string c) ? c : "";
            foreach (var kv in dimIds.Where(k => k.Value == null || !dimMap.ContainsKey(k.Value.Id)).OrderBy(k => k.Key.Item1).ThenBy(k => k.Key.Item2))
                pv.Errors.Add($"بعد {kv.Key.Item1} شماره {kv.Key.Item2} تفصیلی با کد سپیدار ندارد.");

            foreach (string code in lines.Where(l => Clean(l.AccountSepidar) == null).Select(l => l.AccountCode).Distinct().OrderBy(c => c, StringComparer.Ordinal)) pv.UnmappedAccounts.Add(code);
            foreach (int id in detailIds.Where(i => !detailMap.ContainsKey(i)).OrderBy(i => i)) pv.UnmappedDetails.Add(details[id].Code);
            pv.UnmappedDetails = pv.UnmappedDetails.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
            if (pv.UnmappedAccounts.Count > 0) pv.Errors.Add($"{pv.UnmappedAccounts.Count} حساب کد سپیدار ندارد: {string.Join("، ", pv.UnmappedAccounts)}");
            if (pv.UnmappedDetails.Count > 0) pv.Errors.Add($"{pv.UnmappedDetails.Count} تفصیلی کد سپیدار ندارد: {string.Join("، ", pv.UnmappedDetails)}");
            if (pv.Rows == 0) pv.Errors.Add("سند قطعی در این دوره نیست.");
            if (pv.TotalDebit != pv.TotalCredit) pv.Errors.Add("جمع بدهکار و بستانکار برابر نیست.");

            if (withContent && pv.Errors.Count == 0)
            {
                List<FnExportLayoutColumn> cols = (await LayoutAsync(false, ct)).Where(c => c.IsActive).ToList();
                var sb = new StringBuilder();
                sb.Append(string.Join(",", cols.Select(c => Csv(c.Title)))).Append("\r\n");
                var cal = new PersianCalendar();
                foreach (var l in lines)
                {
                    string DetailOf(int? id) => id.HasValue ? detailMap[id.Value] : "";
                    string CostCentre() { string c = FindDetail(l.Detail2Id, FnDetailType.CostCenter); return c != "" ? c : DimCode(FnDetailType.CostCenter, l.CostCenterId); }
                    string Project() { string c = FindDetail(l.Detail2Id, FnDetailType.Project, FnDetailType.WorkOrder); if (c != "") return c; c = DimCode(FnDetailType.WorkOrder, l.WorkOrderId); return c != "" ? c : DimCode(FnDetailType.Project, l.ContractId); }
                    string FindDetail(int? id, params FnDetailType[] types) => id.HasValue && types.Contains(details[id.Value].Type) ? detailMap[id.Value] : "";
                    var cells = new List<string>();
                    foreach (FnExportLayoutColumn c in cols)
                    {
                        switch (c.Key)
                        {
                            case "VoucherNo": cells.Add(l.Number?.ToString(CultureInfo.InvariantCulture) ?? ""); break;
                            case "Date": cells.Add(string.Format(CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}", cal.GetYear(l.Date), cal.GetMonth(l.Date), cal.GetDayOfMonth(l.Date))); break;
                            case "Description": cells.Add(Csv(l.VoucherDescription)); break;
                            case "LineDescription": cells.Add(Csv(l.Description)); break;
                            case "AccountCode": cells.Add(Csv(l.AccountSepidar)); break;
                            case "DetailCode": cells.Add(Csv(DetailOf(l.Detail1Id))); break;
                            case "Debit": cells.Add(l.Debit.ToString("0", CultureInfo.InvariantCulture)); break;
                            case "Credit": cells.Add(l.Credit.ToString("0", CultureInfo.InvariantCulture)); break;
                            case "CostCenter": cells.Add(Csv(CostCentre())); break;
                            case "Project": cells.Add(Csv(Project())); break;
                            default: cells.Add(""); break;
                        }
                    }
                    sb.Append(string.Join(",", cells)).Append("\r\n");
                }
                built.Content = sb.ToString();
            }
            return built;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("\r", " ").Replace("\n", " ");
            // spreadsheet formula injection guard
            if (value[0] == '=' || value[0] == '+' || value[0] == '@' || value[0] == '-') value = "'" + value;
            return value.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public async Task<ResponseDto<ExportPreviewDto>> PreviewAsync(int periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<ExportPreviewDto>(NotAllowed);
            Built b = await BuildAsync(periodId, false, ct);
            return ResponseDtoHelper.Ok(b.Preview);
        }

        public async Task<ResponseDto<VoucherExportDto>> ExportAsync(ExportPeriodDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<VoucherExportDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null) return Bad<VoucherExportDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!await EnabledAsync(ct)) return Bad<VoucherExportDto>(Disabled);
            Built b = await BuildAsync(dto.PeriodId, true, ct);
            if (b.Period == null) return ResponseDtoHelper.NotFound<VoucherExportDto>("دوره پیدا نشد.");
            if (b.Preview.Errors.Count > 0) return Bad<VoucherExportDto>(string.Join(" | ", b.Preview.Errors));

            FnVoucherExport previous = await _uow.Set<FnVoucherExport>().FirstOrDefaultAsync(e => e.PeriodId == dto.PeriodId && e.Status == FnExportStatus.Active, ct);
            if (previous != null && Clean(dto.SupersedeReason) == null)
                return Bad<VoucherExportDto>($"این دوره قبلاً صادر شده است ({previous.Number}). برای صدور دوباره، علت جایگزینی را بنویسید.");

            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(b.Content))).ToLowerInvariant();
            int year = b.Period.FiscalYear.Year;
            var export = new FnVoucherExport
            {
                Number = "EXP-" + year.ToString(CultureInfo.InvariantCulture) + "-" + (await InventoryNumbers.NextAsync(_uow, FinanceNumbers.VoucherExport, year, ct)).ToString("D4", CultureInfo.InvariantCulture),
                LegalEntityId = b.Period.FiscalYear.LegalEntityId, PeriodId = dto.PeriodId, Status = FnExportStatus.Active,
                Vouchers = b.Preview.Vouchers, Rows = b.Preview.Rows, TotalDebit = b.Preview.TotalDebit, TotalCredit = b.Preview.TotalCredit,
                Sha256 = hash, Content = b.Content, ExportedByUserId = _user.UserId, ExportedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnVoucherExport>().Add(export);
            await _uow.SaveChangesAsync(ct);
            if (previous != null)
            {
                previous.Status = FnExportStatus.Superseded;
                previous.SupersededByExportId = export.Id;
                previous.SupersedeReason = Clean(dto.SupersedeReason);
                await _uow.SaveChangesAsync(ct);
            }
            return ResponseDtoHelper.Ok(await ToDtoAsync(export, b.Period.Title, ct));
        }

        private async Task<VoucherExportDto> ToDtoAsync(FnVoucherExport e, string periodTitle, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { e.ExportedByUserId }, ct);
            return new VoucherExportDto
            {
                Id = e.Id, Number = e.Number, LegalEntityId = e.LegalEntityId, PeriodId = e.PeriodId, PeriodTitle = periodTitle, Status = e.Status, Vouchers = e.Vouchers, Rows = e.Rows,
                TotalDebit = e.TotalDebit, TotalCredit = e.TotalCredit, Sha256 = e.Sha256, ExportedByUserId = e.ExportedByUserId, ExportedByName = names.Name(e.ExportedByUserId),
                ExportedAt = e.ExportedAt, SupersededByExportId = e.SupersededByExportId, SupersedeReason = e.SupersedeReason,
            };
        }

        public async Task<ResponseDto<List<VoucherExportDto>>> GetExportsAsync(int? periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<VoucherExportDto>>(NotAllowed);
            IQueryable<FnVoucherExport> q = _uow.Set<FnVoucherExport>().AsNoTracking();
            if (periodId.HasValue) q = q.Where(e => e.PeriodId == periodId.Value);
            var rows = await q.OrderByDescending(e => e.Id).Take(200).Select(e => new
            {
                e.Id, e.Number, e.LegalEntityId, e.PeriodId, PeriodTitle = e.Period.Title, e.Status, e.Vouchers, e.Rows, e.TotalDebit, e.TotalCredit, e.Sha256,
                e.ExportedByUserId, e.ExportedAt, e.SupersededByExportId, e.SupersedeReason,
            }).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => (int?)r.ExportedByUserId), ct);
            return ResponseDtoHelper.Ok(rows.Select(e => new VoucherExportDto
            {
                Id = e.Id, Number = e.Number, LegalEntityId = e.LegalEntityId, PeriodId = e.PeriodId, PeriodTitle = e.PeriodTitle, Status = e.Status, Vouchers = e.Vouchers, Rows = e.Rows,
                TotalDebit = e.TotalDebit, TotalCredit = e.TotalCredit, Sha256 = e.Sha256, ExportedByUserId = e.ExportedByUserId, ExportedByName = names.Name(e.ExportedByUserId),
                ExportedAt = e.ExportedAt, SupersededByExportId = e.SupersededByExportId, SupersedeReason = e.SupersedeReason,
            }).ToList());
        }

        public async Task<ResponseDto<byte[]>> DownloadAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<byte[]>(NotAllowed);
            string content = await _uow.Set<FnVoucherExport>().AsNoTracking().Where(e => e.Id == id).Select(e => e.Content).FirstOrDefaultAsync(ct);
            if (content == null) return ResponseDtoHelper.NotFound<byte[]>("خروجی پیدا نشد.");
            byte[] body = Encoding.UTF8.GetBytes(content);
            return ResponseDtoHelper.Ok(Encoding.UTF8.GetPreamble().Concat(body).ToArray());
        }
    }
}
