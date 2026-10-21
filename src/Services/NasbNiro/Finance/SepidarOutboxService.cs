using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public class SepidarSendResult
    {
        public bool Success { get; set; }
        public string Reference { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// The one door to the commercial accounting software approved by the tax and insurance offices. Only tax and insurance data goes out; nothing else is exchanged.
    /// The default implementation drops the file in a folder when one is configured (Finance:SepidarOutboxFolder); a direct connector can replace it later.
    /// </summary>
    public interface ISepidarClient
    {
        Task<SepidarSendResult> SendAsync(string packageNumber, byte[] workbook, CancellationToken cancellationToken);
    }

    public class FileSepidarClient : ISepidarClient
    {
        private readonly IConfiguration _configuration;

        public FileSepidarClient(IConfiguration configuration) { _configuration = configuration; }

        public async Task<SepidarSendResult> SendAsync(string packageNumber, byte[] workbook, CancellationToken ct)
        {
            string folder = _configuration?["Finance:SepidarOutboxFolder"];
            if (string.IsNullOrWhiteSpace(folder))
                return new SepidarSendResult { Success = false, Message = "اتصال مستقیم به نرم‌افزار مالی پیکربندی نشده است؛ فایل اکسل را دریافت و در نرم‌افزار مالی وارد کنید، سپس «ارسال‌شده» را ثبت کنید." };
            try
            {
                Directory.CreateDirectory(folder);
                string name = new string(packageNumber.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray()) + ".xlsx";
                await File.WriteAllBytesAsync(Path.Combine(folder, name), workbook, ct);
                return new SepidarSendResult { Success = true, Reference = name };
            }
            catch (IOException ex) { return new SepidarSendResult { Success = false, Message = "نوشتن فایل در پوشه تبادل ممکن نشد: " + ex.Message }; }
            catch (UnauthorizedAccessException) { return new SepidarSendResult { Success = false, Message = "دسترسی نوشتن در پوشه تبادل وجود ندارد." }; }
        }
    }

    public interface ISepidarOutboxService
    {
        Task<ResponseDto<PagedList<OutboxPackageDto>>> GetPackagesAsync(OutboxQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<OutboxPackageDto>> GetPackageAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<OutboxPackageDto>> GenerateAsync(GenerateOutboxDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<OutboxPackageDto>> ActionAsync(OutboxActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<byte[]>> ExportAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<OutboxSummaryDto>> GetSummaryAsync(CancellationToken cancellationToken);
        /// <summary>The background job: drafts the packages of the last Persian month that do not exist yet. Returns a short log line.</summary>
        Task<string> GenerateMonthlyAsync(CancellationToken cancellationToken);
    }

    internal sealed class OutboxTable
    {
        public string Name { get; set; }
        public List<string> Columns { get; set; } = new List<string>();
        public List<List<object>> Rows { get; set; } = new List<List<object>>();
    }

    internal sealed class OutboxTableJson
    {
        public string Name { get; set; }
        public List<string> Columns { get; set; } = new List<string>();
        public List<List<JsonElement>> Rows { get; set; } = new List<List<JsonElement>>();
    }

    public class SepidarOutboxService : ISepidarOutboxService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ISepidarClient _client;

        public SepidarOutboxService(IUnitOfWork uow, IInventoryUserContext user, ISepidarClient client)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string Conflict = "همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید.";
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false };

        private static bool IsPayroll(FnOutboxKind kind) => kind == FnOutboxKind.PayrollInsuranceList || kind == FnOutboxKind.PayrollTaxList;
        private bool CanSee(FnOutboxKind kind) => IsPayroll(kind) ? _user.IsInRole(FinanceRoles.PayrollReaders) : _user.IsInRole(FinanceRoles.OutboxReaders);
        private bool CanPrepare(FnOutboxKind kind) => IsPayroll(kind) ? _user.IsInRole(FinanceRoles.PayrollUsers) : _user.IsInRole(FinanceRoles.Accountants);
        private bool CanApprove(FnOutboxKind kind) => IsPayroll(kind) ? _user.IsInRole(FinanceRoles.PayrollUsers) : _user.IsInRole(FinanceRoles.Managers);

        private static bool ValidYear(int y) => y >= 1300 && y <= 1600;

        // ================================================================================== content of the packages

        private static string D(DateTime date) => PersianDate(date);

        private static OutboxTable Table(string name, params string[] columns) => new OutboxTable { Name = name, Columns = columns.ToList() };

        private static void Add(OutboxTable table, params object[] values) => table.Rows.Add(values.ToList());

        private async Task<List<OutboxTable>> BuildAsync(FnOutboxKind kind, int year, int? month, int? season, int? workshopId, DateTime from, DateTime to, CancellationToken ct)
        {
            var tables = new List<OutboxTable>();
            switch (kind)
            {
                case FnOutboxKind.SalesInvoicesTaxpayer:
                {
                    OutboxTable t = Table("Invoices", "Number", "Date", "Type", "CustomerName", "EconomicCode", "NationalId", "PostalCode", "NetAmount", "VatAmount", "GrossAmount", "TaxId");
                    var rows = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Posted && i.Date >= from && i.Date <= to)
                        .OrderBy(i => i.Date).ThenBy(i => i.Id)
                        .Select(i => new { i.Number, i.Date, i.Type, i.Customer.Name, i.Customer.EconomicCode, i.Customer.NationalId, i.Customer.PostalCode, i.NetAmount, i.VatAmount, i.GrossAmount, i.TaxId }).ToListAsync(ct);
                    foreach (var r in rows) Add(t, r.Number, D(r.Date), r.Type.ToString(), r.Name, r.EconomicCode, r.NationalId, r.PostalCode, r.NetAmount, r.VatAmount, r.GrossAmount, r.TaxId);
                    tables.Add(t);
                    break;
                }
                case FnOutboxKind.VatSeason:
                {
                    ResponseDto<VatSeasonDto> vat = await TaxService.BuildVatSeasonAsync(_uow, year, season ?? 0, ct);
                    VatSeasonDto v = vat.Data;
                    OutboxTable rows = Table("Parties", "Side", "PartyName", "EconomicCode", "NetAmount", "VatAmount", "Documents");
                    foreach (VatSeasonRowDto r in v.Rows) Add(rows, r.Side, r.PartyName, r.EconomicCode, r.NetAmount, r.VatAmount, r.Documents);
                    OutboxTable summary = Table("Summary", "Year", "Season", "SalesNet", "SalesVat", "PurchasesNet", "PurchasesVat", "VatPayable");
                    Add(summary, v.Year, v.Season, v.SalesNet, v.SalesVat, v.PurchasesNet, v.PurchasesVat, v.VatPayable);
                    tables.Add(summary);
                    tables.Add(rows);
                    break;
                }
                case FnOutboxKind.PayrollInsuranceList:
                {
                    InsuranceListDto list = await PayrollReports.InsuranceListAsync(_uow, year, month.Value, workshopId, ct);
                    OutboxTable header = Table("Workshop", "WorkshopCode", "WorkshopName", "Year", "Month", "Employees", "TotalInsurable", "TotalEmployeeShare", "TotalEmployerShare", "TotalUnemployment");
                    Add(header, list.WorkshopCode, list.WorkshopName, list.Year, list.Month, list.Employees, list.TotalInsurable, list.TotalEmployeeShare, list.TotalEmployerShare, list.TotalUnemployment);
                    OutboxTable workers = Table("Workers", "PersonnelCode", "InsuranceNumber", "NationalCode", "FirstName", "LastName", "FatherName", "BirthDate", "JobCode", "Days", "DailyWage",
                        "MonthlyWage", "Benefits", "InsurableTotal", "InsurableCapped", "EmployeeShare", "EmployerShare", "Unemployment", "HardAndHarmful");
                    foreach (InsuranceListRowDto r in list.Rows)
                        Add(workers, r.PersonnelCode, r.InsuranceNumber, r.NationalCode, r.FirstName, r.LastName, r.FatherName, r.BirthDate.HasValue ? D(r.BirthDate.Value) : null, r.JobCode,
                            r.DaysWorked, r.DailyWage, r.MonthlyWage, r.Benefits, r.InsurableTotal, r.InsurableCapped, r.EmployeeShare, r.EmployerShare, r.Unemployment, r.HardAndHarmful);
                    tables.Add(header);
                    tables.Add(workers);
                    break;
                }
                case FnOutboxKind.PayrollTaxList:
                {
                    TaxListDto list = await PayrollReports.TaxListAsync(_uow, year, month.Value, ct);
                    OutboxTable header = Table("Summary", "Year", "Month", "Employees", "TotalTaxable", "TotalTax");
                    Add(header, list.Year, list.Month, list.Employees, list.TotalTaxable, list.TotalTax);
                    OutboxTable rows = Table("Employees", "PersonnelCode", "NationalCode", "FirstName", "LastName", "TaxableTotal", "EmployeeInsurance", "Tax");
                    foreach (TaxListRowDto r in list.Rows) Add(rows, r.PersonnelCode, r.NationalCode, r.FirstName, r.LastName, r.TaxableTotal, r.EmployeeInsurance, r.Tax);
                    tables.Add(header);
                    tables.Add(rows);
                    break;
                }
            }
            return tables;
        }

        private static string Hash(string json)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(json)).Select(b => b.ToString("x2")));
        }

        // ================================================================================== generation

        private async Task<ResponseDto<FnOutboxPackage>> CreatePackageAsync(GenerateOutboxDto dto, int userId, CancellationToken ct)
        {
            if (!Enum.IsDefined(typeof(FnOutboxKind), dto.Kind)) return Bad<FnOutboxPackage>("نوع بسته نامعتبر است.");
            if (!ValidYear(dto.Year)) return Bad<FnOutboxPackage>("سال نامعتبر است.");
            DateTime from, to;
            int? month = null, season = null;
            if (dto.Kind == FnOutboxKind.VatSeason)
            {
                if (!dto.Season.HasValue || dto.Season < 1 || dto.Season > 4) return Bad<FnOutboxPackage>("فصل را مشخص کنید (۱ تا ۴).");
                season = dto.Season;
                from = PersianCal.Start(dto.Year, (season.Value - 1) * 3 + 1);
                to = PersianCal.End(dto.Year, season.Value * 3);
            }
            else
            {
                if (!dto.Month.HasValue || dto.Month < 1 || dto.Month > 12) return Bad<FnOutboxPackage>("ماه را مشخص کنید (۱ تا ۱۲).");
                month = dto.Month;
                from = PersianCal.Start(dto.Year, month.Value);
                to = PersianCal.End(dto.Year, month.Value);
            }
            int? workshopId = dto.Kind == FnOutboxKind.PayrollInsuranceList ? dto.WorkshopId : null;
            if (workshopId.HasValue && !await _uow.Set<FnInsuranceWorkshop>().AnyAsync(w => w.Id == workshopId.Value, ct)) return Bad<FnOutboxPackage>("کارگاه بیمه پیدا نشد.");

            List<OutboxTable> tables = await BuildAsync(dto.Kind, dto.Year, month, season, workshopId, from, to, ct);
            int rowCount = tables.Where(t => t.Name != "Summary" && t.Name != "Workshop").Sum(t => t.Rows.Count);
            if (rowCount == 0 && dto.Kind != FnOutboxKind.VatSeason) return Bad<FnOutboxPackage>("برای این دوره داده‌ای برای ارسال وجود ندارد (برای حقوق، فقط اجرای تاییدشده).");
            string json = JsonSerializer.Serialize(tables, Json);

            List<FnOutboxPackage> previous = await _uow.Set<FnOutboxPackage>().Where(p => p.Kind == dto.Kind && p.Year == dto.Year && p.Month == month && p.Season == season
                && p.WorkshopId == workshopId).ToListAsync(ct);
            var package = new FnOutboxPackage
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.OutboxPackage, "SEP", to, ct), Kind = dto.Kind, Year = dto.Year, Month = month, Season = season,
                WorkshopId = workshopId, Status = FnOutboxStatus.Draft, Version = previous.Count == 0 ? 1 : previous.Max(p => p.Version) + 1, FromDate = from, ToDate = to,
                ContentHash = Hash(json), PayloadJson = json, RowCount = rowCount, Note = Clean(dto.Note), CreatedByUserId = userId, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnOutboxPackage>().Add(package);
            await _uow.SaveChangesAsync(ct);
            // the new package supersedes the old ones (second save: the new id is known)
            bool changed = false;
            foreach (FnOutboxPackage old in previous.Where(p => p.Status != FnOutboxStatus.Superseded))
            {
                old.Status = FnOutboxStatus.Superseded;
                old.SupersededByPackageId = package.Id;
                changed = true;
            }
            if (changed)
            {
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { /* the old package was changed at the same time; it stays as it is */ }
            }
            return ResponseDtoHelper.Ok(package);
        }

        public async Task<ResponseDto<OutboxPackageDto>> GenerateAsync(GenerateOutboxDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<OutboxPackageDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!CanPrepare(dto.Kind)) return Bad<OutboxPackageDto>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            ResponseDto<FnOutboxPackage> created = await CreatePackageAsync(dto, Me, ct);
            if (!created.Success) return Bad<OutboxPackageDto>(created.Message);
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<FnOutboxPackage> { created.Data }, true, ct))[0]);
        }

        public async Task<string> GenerateMonthlyAsync(CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            int last = PersianCal.Index(DateTime.Today) - 1;
            int year = PersianCal.YearOfIndex(last), month = PersianCal.MonthOfIndex(last);
            DateTime from = PersianCal.Start(year, month), to = PersianCal.End(year, month);
            int made = 0;

            async Task<bool> Exists(FnOutboxKind kind, int? m, int? s, int? w) =>
                await _uow.Set<FnOutboxPackage>().AnyAsync(p => p.Kind == kind && p.Year == year && p.Month == m && p.Season == s && p.WorkshopId == w && p.Status != FnOutboxStatus.Superseded, ct);
            async Task Make(GenerateOutboxDto dto)
            {
                ResponseDto<FnOutboxPackage> r = await CreatePackageAsync(dto, 0, ct);
                if (r.Success) made++;
            }

            if (!await Exists(FnOutboxKind.SalesInvoicesTaxpayer, month, null, null) && await _uow.Set<FnSalesInvoice>().AnyAsync(i => i.Status == FnDocStatus.Posted && i.Date >= from && i.Date <= to, ct))
                await Make(new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = year, Month = month });
            if (month % 3 == 0)
            {
                int season = month / 3;
                if (!await Exists(FnOutboxKind.VatSeason, null, season, null)) await Make(new GenerateOutboxDto { Kind = FnOutboxKind.VatSeason, Year = year, Season = season });
            }
            List<int?> workshops = await _uow.Set<FnPayslip>().AsNoTracking().Where(p => p.Run.Year == year && p.Run.Month == month && p.Run.Status >= FnPayrollStatus.Approved && p.WorkshopId != null)
                .Select(p => p.WorkshopId).Distinct().ToListAsync(ct);
            foreach (int? w in workshops)
                if (!await Exists(FnOutboxKind.PayrollInsuranceList, month, null, w)) await Make(new GenerateOutboxDto { Kind = FnOutboxKind.PayrollInsuranceList, Year = year, Month = month, WorkshopId = w });
            if (await _uow.Set<FnPayslip>().AnyAsync(p => p.Run.Year == year && p.Run.Month == month && p.Run.Status >= FnPayrollStatus.Approved, ct) && !await Exists(FnOutboxKind.PayrollTaxList, month, null, null))
                await Make(new GenerateOutboxDto { Kind = FnOutboxKind.PayrollTaxList, Year = year, Month = month });
            return $"{year}/{month:00}: {made} package(s) drafted";
        }

        // ================================================================================== reading

        private async Task<List<OutboxPackageDto>> ToDtosAsync(List<FnOutboxPackage> rows, bool withTables, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(p => new int?[] { p.CreatedByUserId, p.ApprovedByUserId }), ct);
            var list = new List<OutboxPackageDto>();
            foreach (FnOutboxPackage p in rows)
            {
                var dto = new OutboxPackageDto
                {
                    Id = p.Id, Number = p.Number, Kind = p.Kind, Year = p.Year, Month = p.Month, Season = p.Season, WorkshopId = p.WorkshopId, Status = p.Status, Version = p.Version,
                    FromDate = p.FromDate, ToDate = p.ToDate, ContentHash = p.ContentHash, RowCount = p.RowCount, SupersededByPackageId = p.SupersededByPackageId,
                    ExternalReference = p.ExternalReference, Error = p.Error, Note = p.Note, CreatorName = p.CreatedByUserId == 0 ? "سیستم (کار زمان‌بندی‌شده)" : names.Name(p.CreatedByUserId),
                    CreatedAt = p.CreatedAt, ApprovedByName = names.Name(p.ApprovedByUserId), ApprovedAt = p.ApprovedAt, ExportedAt = p.ExportedAt, SentAt = p.SentAt,
                };
                if (withTables && !string.IsNullOrEmpty(p.PayloadJson))
                {
                    List<OutboxTableJson> tables = JsonSerializer.Deserialize<List<OutboxTableJson>>(p.PayloadJson, Json) ?? new List<OutboxTableJson>();
                    dto.Tables = tables.Select(t => new OutboxTableInfoDto { Name = t.Name, Rows = t.Rows.Count }).ToList();
                }
                list.Add(dto);
            }
            return list;
        }

        public async Task<ResponseDto<PagedList<OutboxPackageDto>>> GetPackagesAsync(OutboxQuery query, CancellationToken ct)
        {
            query ??= new OutboxQuery();
            if (!_user.IsInRole(FinanceRoles.OutboxReaders) && !_user.IsInRole(FinanceRoles.PayrollReaders)) return Bad<PagedList<OutboxPackageDto>>(NotAllowed);
            IQueryable<FnOutboxPackage> q = _uow.Set<FnOutboxPackage>().AsNoTracking();
            if (!_user.IsInRole(FinanceRoles.PayrollReaders)) q = q.Where(p => p.Kind != FnOutboxKind.PayrollInsuranceList && p.Kind != FnOutboxKind.PayrollTaxList);
            if (!_user.IsInRole(FinanceRoles.OutboxReaders)) q = q.Where(p => p.Kind == FnOutboxKind.PayrollInsuranceList || p.Kind == FnOutboxKind.PayrollTaxList);
            if (query.Kind.HasValue) q = q.Where(p => p.Kind == query.Kind.Value);
            if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
            if (query.Year.HasValue) q = q.Where(p => p.Year == query.Year.Value);
            if (query.Month.HasValue) q = q.Where(p => p.Month == query.Month.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnOutboxPackage> rows = await q.OrderByDescending(p => p.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await ToDtosAsync(rows, false, ct), total, number, size));
        }

        public async Task<ResponseDto<OutboxPackageDto>> GetPackageAsync(int id, CancellationToken ct)
        {
            FnOutboxPackage p = await _uow.Set<FnOutboxPackage>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<OutboxPackageDto>("بسته پیدا نشد.");
            if (!CanSee(p.Kind)) return Bad<OutboxPackageDto>(NotAllowed);
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<FnOutboxPackage> { p }, true, ct))[0]);
        }

        // ================================================================================== workflow

        private static byte[] Workbook(FnOutboxPackage p)
        {
            List<OutboxTableJson> tables = JsonSerializer.Deserialize<List<OutboxTableJson>>(p.PayloadJson, Json) ?? new List<OutboxTableJson>();
            var sheets = new List<XlsxSheet>();
            foreach (OutboxTableJson t in tables)
            {
                var sheet = new XlsxSheet { Name = t.Name, Header = t.Columns };
                foreach (List<JsonElement> row in t.Rows)
                    sheet.Rows.Add(row.Select(Cell).ToArray());
                sheets.Add(sheet);
            }
            return SimpleXlsx.Build(sheets, true);
        }

        private static object Cell(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number: return e.TryGetDecimal(out decimal d) ? d : (object)e.GetDouble();
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }

        public async Task<ResponseDto<byte[]>> ExportAsync(int id, CancellationToken ct)
        {
            FnOutboxPackage p = await _uow.Set<FnOutboxPackage>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<byte[]>("بسته پیدا نشد.");
            if (!CanSee(p.Kind)) return Bad<byte[]>(NotAllowed);
            if (p.Status == FnOutboxStatus.Draft) return Bad<byte[]>("بسته هنوز تایید نشده است.");
            if (p.Status == FnOutboxStatus.Superseded) return Bad<byte[]>("این بسته با نسخه جدیدتر جایگزین شده است.");
            byte[] file = Workbook(p);
            if (p.Status == FnOutboxStatus.Ready)
            {
                p.Status = FnOutboxStatus.Exported;
                p.ExportedAt = DateTimeOffset.Now;
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { /* exported by someone else at the same time: the file is identical */ }
            }
            return ResponseDtoHelper.Ok(file);
        }

        public async Task<ResponseDto<OutboxPackageDto>> ActionAsync(OutboxActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<OutboxPackageDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnOutboxPackage p = await _uow.Set<FnOutboxPackage>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<OutboxPackageDto>("بسته پیدا نشد.");
            if (!CanSee(p.Kind)) return Bad<OutboxPackageDto>(NotAllowed);
            string action = dto.Action.Trim().ToLowerInvariant();
            switch (action)
            {
                case "approve":
                    if (!CanApprove(p.Kind)) return Bad<OutboxPackageDto>(NotAllowed);
                    if (p.Status != FnOutboxStatus.Draft) return Bad<OutboxPackageDto>("فقط بسته پیش‌نویس تایید می‌شود.");
                    if (p.CreatedByUserId != 0 && p.CreatedByUserId == Me) return Bad<OutboxPackageDto>("تایید باید توسط شخص دیگری غیر از تهیه‌کننده بسته انجام شود.");
                    p.Status = FnOutboxStatus.Ready; p.ApprovedByUserId = Me; p.ApprovedAt = DateTimeOffset.Now;
                    break;
                case "send":
                {
                    if (!CanPrepare(p.Kind)) return Bad<OutboxPackageDto>(NotAllowed);
                    if (p.Status != FnOutboxStatus.Ready && p.Status != FnOutboxStatus.Exported) return Bad<OutboxPackageDto>("فقط بسته تاییدشده ارسال می‌شود.");
                    SepidarSendResult result = await _client.SendAsync(p.Number, Workbook(p), ct);
                    if (!result.Success)
                    {
                        p.Error = result.Message;
                        await _uow.SaveChangesAsync(ct);
                        return Bad<OutboxPackageDto>(result.Message);
                    }
                    p.Status = FnOutboxStatus.Sent; p.SentAt = DateTimeOffset.Now; p.ExternalReference = result.Reference; p.Error = null;
                    break;
                }
                case "mark-sent":
                    if (!CanPrepare(p.Kind)) return Bad<OutboxPackageDto>(NotAllowed);
                    if (p.Status != FnOutboxStatus.Ready && p.Status != FnOutboxStatus.Exported) return Bad<OutboxPackageDto>("فقط بسته تاییدشده یا دریافت‌شده «ارسال‌شده» می‌شود.");
                    if (Clean(dto.Reference) == null) return Bad<OutboxPackageDto>("شماره پیگیری یا مرجع ارسال را وارد کنید.");
                    p.Status = FnOutboxStatus.Sent; p.SentAt = DateTimeOffset.Now; p.ExternalReference = Clean(dto.Reference); p.Error = null;
                    break;
                case "mark-accepted":
                    if (!CanPrepare(p.Kind)) return Bad<OutboxPackageDto>(NotAllowed);
                    if (p.Status != FnOutboxStatus.Sent) return Bad<OutboxPackageDto>("فقط بسته ارسال‌شده پذیرفته می‌شود.");
                    p.Status = FnOutboxStatus.Accepted; p.Error = null;
                    if (Clean(dto.Reference) != null) p.ExternalReference = Clean(dto.Reference);
                    break;
                case "mark-rejected":
                    if (!CanPrepare(p.Kind)) return Bad<OutboxPackageDto>(NotAllowed);
                    if (p.Status != FnOutboxStatus.Sent) return Bad<OutboxPackageDto>("فقط بسته ارسال‌شده رد می‌شود.");
                    if (Clean(dto.Note) == null) return Bad<OutboxPackageDto>("دلیل رد را بنویسید.");
                    p.Status = FnOutboxStatus.Rejected; p.Error = Clean(dto.Note);
                    break;
                default:
                    return Bad<OutboxPackageDto>("عمل نامعتبر است.");
            }
            if (Clean(dto.Note) != null && action != "mark-rejected") p.Note = Clean(dto.Note);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<OutboxPackageDto>(Conflict); }
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<FnOutboxPackage> { p }, true, ct))[0]);
        }

        // ================================================================================== counters

        internal static async Task<OutboxSummaryDto> CountersAsync(IUnitOfWork uow, CancellationToken ct)
        {
            List<FnOutboxPackage> rows = await uow.Set<FnOutboxPackage>().AsNoTracking().Where(p => p.Status != FnOutboxStatus.Superseded && p.Status != FnOutboxStatus.Accepted)
                .Select(p => new FnOutboxPackage { Status = p.Status, Error = p.Error, SentAt = p.SentAt }).ToListAsync(ct);
            DateTimeOffset? lastSent = await uow.Set<FnOutboxPackage>().AsNoTracking().Where(p => p.SentAt != null).OrderByDescending(p => p.SentAt).Select(p => p.SentAt).FirstOrDefaultAsync(ct);
            return new OutboxSummaryDto
            {
                Pending = rows.Count(p => p.Status == FnOutboxStatus.Draft),
                ReadyToSend = rows.Count(p => p.Status == FnOutboxStatus.Ready || p.Status == FnOutboxStatus.Exported),
                Failed = rows.Count(p => p.Status == FnOutboxStatus.Rejected || (p.Error != null && (p.Status == FnOutboxStatus.Ready || p.Status == FnOutboxStatus.Exported))),
                LastSentAt = lastSent,
            };
        }

        public async Task<ResponseDto<OutboxSummaryDto>> GetSummaryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.OutboxReaders)) return Bad<OutboxSummaryDto>(NotAllowed);
            return ResponseDtoHelper.Ok(await CountersAsync(_uow, ct));
        }
    }
}
