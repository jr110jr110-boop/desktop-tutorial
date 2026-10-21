using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface IAssetService
    {
        Task<ResponseDto<List<AssetClassDto>>> GetClassesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<AssetClassDto>> SaveClassAsync(SaveAssetClassDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<AssetDto>>> GetAssetsAsync(AssetQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<AssetDto>> GetAssetAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<AssetDto>> SaveAssetAsync(SaveAssetDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<AssetDto>> AssetActionAsync(AssetActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<DepreciationRunDto>>> GetDepreciationRunsAsync(DepreciationRunQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<DepreciationRunDto>> GetDepreciationRunAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<DepreciationRunDto>> PrepareDepreciationAsync(int periodId, CancellationToken cancellationToken);
        Task<ResponseDto<DepreciationRunDto>> DepreciationActionAsync(DepreciationActionDto dto, CancellationToken cancellationToken);
        /// <summary>The background job: drafts the run of the last finished Persian month when there is none (idempotent).</summary>
        Task<string> DraftLastMonthAsync(CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<DisposalDto>>> GetDisposalsAsync(DisposalQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<DisposalDto>> SaveDisposalAsync(SaveDisposalDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<DisposalDto>> DisposalActionAsync(DocActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<DepreciationScheduleDto>> GetDepreciationScheduleAsync(int year, CancellationToken cancellationToken);
        Task<ResponseDto<AssetMovementDto>> GetMovementAsync(int year, CancellationToken cancellationToken);
        Task<ResponseDto<AssetHandoverDto>> GetHandoverAsync(int? employeeId, string custodian, CancellationToken cancellationToken);
        Task<ResponseDto<SetupModuleDto>> SetupCheckAsync(CancellationToken cancellationToken);
    }

    /// <summary>Depreciation arithmetic: pure functions over an asset, its class and its idle periods.</summary>
    internal static class AssetMath
    {
        public sealed class IdleInterval
        {
            public DateTime From { get; set; }
            public DateTime? To { get; set; }
        }

        public sealed class Result
        {
            public decimal Amount { get; set; }
            public int Months { get; set; }
            public int IdleMonths { get; set; }
            public decimal AccumulatedAfter { get; set; }
            public int YearStartYear { get; set; }
            public decimal YearStartAccumulated { get; set; }
        }

        public static List<IdleInterval> Intervals(IEnumerable<FnAssetHistory> history)
        {
            var list = new List<IdleInterval>();
            IdleInterval open = null;
            foreach (FnAssetHistory h in history.Where(x => x.Kind == FnAssetHistoryKind.IdleFrom || x.Kind == FnAssetHistoryKind.IdleTo).OrderBy(x => x.Date).ThenBy(x => x.Id))
            {
                if (h.Kind == FnAssetHistoryKind.IdleFrom)
                {
                    if (open == null) { open = new IdleInterval { From = h.Date.Date }; list.Add(open); }
                }
                else if (open != null)
                {
                    open.To = h.Date.Date;
                    open = null;
                }
            }
            return list;
        }

        /// <summary>The land class has no accumulated account and is not depreciated.</summary>
        public static bool Depreciable(FnAssetClass c) => c != null && c.IsActive && !string.IsNullOrEmpty(c.AccumulatedAccountCode);

        public static bool HasRate(FnAssetClass c)
        {
            if (c.Method == FnDepreciationMethod.DecliningBalance) return c.RatePercent.HasValue && c.RatePercent.Value > 0;
            return (c.RatePercent.HasValue && c.RatePercent.Value > 0) || (c.UsefulLifeYears.HasValue && c.UsefulLifeYears.Value > 0);
        }

        /// <summary>The first Persian month (index) that is depreciated next.</summary>
        public static int FirstIndex(FnAsset a, FnAssetClass c)
        {
            if (a.DepreciatedThrough.HasValue) return PersianCal.Index(a.DepreciatedThrough.Value) + 1;
            int inUse = PersianCal.Index(a.InUseDate.Value);
            return c.StartRule == FnDepreciationStart.MonthOfUse ? inUse : inUse + 1;
        }

        /// <summary>Idle assets are depreciated at the reduced percent once they have been idle for the given number of whole months.</summary>
        public static bool IsReduced(List<IdleInterval> idle, int monthIndex, int afterMonths)
        {
            DateTime monthStart = PersianCal.StartOfIndex(monthIndex);
            foreach (IdleInterval i in idle)
            {
                if (i.From.Date > monthStart) continue;
                if (i.To.HasValue && i.To.Value.Date <= monthStart) continue;
                int fromIndex = PersianCal.Index(i.From);
                int firstStart = i.From.Date == PersianCal.StartOfIndex(fromIndex) ? fromIndex : fromIndex + 1;
                int completed = Math.Max(0, monthIndex - firstStart);
                if (completed >= Math.Max(0, afterMonths)) return true;
            }
            return false;
        }

        /// <summary>
        /// Depreciation of the months from the asset's next month up to <paramref name="targetIndex"/> (inclusive). Each month is rounded to a whole rial
        /// and never takes the book value below the salvage value. Declining balance uses the book value at the start of the Persian year.
        /// Returns null when there is nothing to depreciate.
        /// </summary>
        public static Result Compute(FnAsset a, FnAssetClass c, List<IdleInterval> idle, int targetIndex)
        {
            if (a.InUseDate == null || c == null) return null;
            int first = FirstIndex(a, c);
            if (first > targetIndex) return null;
            decimal acc = a.AccumulatedDepreciation;
            int ysYear = a.YearStartYear ?? 0;
            decimal ysAcc = a.YearStartAccumulated;
            decimal depreciable = a.Cost - a.SalvageValue;
            decimal total = 0;
            int months = 0, idleMonths = 0;
            for (int m = first; m <= targetIndex; m++)
            {
                int year = PersianCal.YearOfIndex(m);
                if (ysYear != year) { ysYear = year; ysAcc = acc; }
                decimal monthly;
                if (c.Method == FnDepreciationMethod.DecliningBalance)
                    monthly = Math.Max(0m, a.Cost - ysAcc) * (c.RatePercent ?? 0m) / 100m / 12m;
                else if (c.RatePercent.HasValue && c.RatePercent.Value > 0)
                    monthly = depreciable * c.RatePercent.Value / 100m / 12m;
                else
                    monthly = depreciable / Math.Max(c.UsefulLifeYears ?? 1m, 0.0001m) / 12m;
                if (IsReduced(idle, m, c.IdleAfterMonths))
                {
                    monthly = monthly * c.IdleRatePercent / 100m;
                    idleMonths++;
                }
                monthly = R(monthly);
                decimal room = Math.Max(0m, a.Cost - a.SalvageValue - acc);
                if (monthly > room) monthly = room;
                acc += monthly;
                total += monthly;
                months++;
            }
            return new Result { Amount = total, Months = months, IdleMonths = idleMonths, AccumulatedAfter = acc, YearStartYear = ysYear, YearStartAccumulated = ysAcc };
        }
    }

    /// <summary>A supplier invoice line flagged as capital drafts a fixed asset for finance to complete (class, cost centre, in-use date).</summary>
    internal static class AssetDrafts
    {
        public static async Task AddFromInvoiceLineAsync(IUnitOfWork uow, SupplierInvoice invoice, SupplierInvoiceLine line, int userId, CancellationToken ct)
        {
            string title = Clean(line.OrderLine.Description);
            if (title == null && line.OrderLine.ItemId.HasValue)
                title = await uow.Set<InventoryItem>().Where(i => i.Id == line.OrderLine.ItemId.Value).Select(i => i.Title).FirstOrDefaultAsync(ct);
            title = title ?? ("دارایی از فاکتور " + invoice.InvoiceNumber);
            int number = await InventoryNumbers.NextAsync(uow, FinanceNumbers.AssetDraft, 0, ct);
            uow.Set<FnAsset>().Add(new FnAsset
            {
                Code = "TMP-" + number.ToString("00000"), Title = title.Length > 300 ? title.Substring(0, 300) : title, Status = FnAssetStatus.Draft,
                AcquisitionDate = invoice.InvoiceDate.Date, Cost = R(line.Quantity * line.UnitPrice), Source = FnAssetSource.SupplierInvoiceLine, SourceId = invoice.Id,
                SourceNumber = invoice.Number, CreatedByUserId = userId, CreatedAt = DateTimeOffset.Now,
            });
        }
    }

    public partial class AssetService : IAssetService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;

        public AssetService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "این کار باید توسط شخص دیگری غیر از ثبت‌کننده یا تهیه‌کننده انجام شود.";
        private const string Conflict = "همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید.";

        private async Task EnsureAsync(CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            await F2Seed.EnsureAsync(_uow, ct);
        }

        // ================================================================================== set-up check

        internal static async Task<SetupModuleDto> CheckSetupAsync(IUnitOfWork uow, CancellationToken ct)
        {
            await F2Seed.EnsureAsync(uow, ct);
            List<FnAssetClass> classes = await uow.Set<FnAssetClass>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync(ct);
            var dto = new SetupModuleDto { Module = "Assets" };
            dto.Missing = classes.Where(c => AssetMath.Depreciable(c) && !AssetMath.HasRate(c)).Select(c => c.Code).ToList();
            dto.Ok = dto.Missing.Count == 0;
            dto.Message = dto.Ok ? null : "نرخ یا عمر مفید استهلاک رده‌های دارایی زیر تعیین نشده است: " + string.Join("، ", dto.Missing);
            return dto;
        }

        public async Task<ResponseDto<SetupModuleDto>> SetupCheckAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<SetupModuleDto>(NotAllowed);
            return ResponseDtoHelper.Ok(await CheckSetupAsync(_uow, ct));
        }

        // ================================================================================== asset classes

        private static AssetClassDto ToDto(FnAssetClass c) => new AssetClassDto
        {
            Id = c.Id, Code = c.Code, Title = c.Title, AssetAccountCode = c.AssetAccountCode, AccumulatedAccountCode = c.AccumulatedAccountCode, Method = c.Method,
            RatePercent = c.RatePercent, UsefulLifeYears = c.UsefulLifeYears, StartRule = c.StartRule, IdleRatePercent = c.IdleRatePercent, IdleAfterMonths = c.IdleAfterMonths,
            SalvagePercent = c.SalvagePercent, Source = c.Source, Verify = c.Verify, IsIntangible = c.IsIntangible, IsActive = c.IsActive,
            HasRate = !AssetMath.Depreciable(c) || AssetMath.HasRate(c),
        };

        public async Task<ResponseDto<List<AssetClassDto>>> GetClassesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<AssetClassDto>>(NotAllowed);
            await EnsureAsync(ct);
            List<FnAssetClass> rows = await _uow.Set<FnAssetClass>().AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(ToDto).ToList());
        }

        public async Task<ResponseDto<AssetClassDto>> SaveClassAsync(SaveAssetClassDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<AssetClassDto>(NotAllowed);
            if (dto == null) return Bad<AssetClassDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            string code = Clean(dto.Code)?.ToUpperInvariant();
            if (code == null || !Regex.IsMatch(code, "^[A-Z0-9]{1,20}$")) return Bad<AssetClassDto>("کد رده باید حرف انگلیسی و عدد (حداکثر ۲۰ نویسه) باشد.");
            if (Clean(dto.Title) == null) return Bad<AssetClassDto>("عنوان رده را وارد کنید.");
            if (!Enum.IsDefined(typeof(FnDepreciationMethod), dto.Method)) return Bad<AssetClassDto>("روش استهلاک نامعتبر است.");
            if (dto.RatePercent.HasValue && (dto.RatePercent.Value <= 0 || dto.RatePercent.Value > 100)) return Bad<AssetClassDto>("نرخ استهلاک باید بین صفر و ۱۰۰ درصد باشد.");
            if (dto.UsefulLifeYears.HasValue && dto.UsefulLifeYears.Value <= 0) return Bad<AssetClassDto>("عمر مفید باید بیشتر از صفر باشد.");
            if (dto.IdleRatePercent.HasValue && (dto.IdleRatePercent.Value < 0 || dto.IdleRatePercent.Value > 100)) return Bad<AssetClassDto>("درصد استهلاک دارایی بلااستفاده نامعتبر است.");
            if (dto.IdleAfterMonths.HasValue && (dto.IdleAfterMonths.Value < 0 || dto.IdleAfterMonths.Value > 120)) return Bad<AssetClassDto>("ماه‌های بلااستفاده نامعتبر است.");
            if (dto.SalvagePercent.HasValue && (dto.SalvagePercent.Value < 0 || dto.SalvagePercent.Value >= 100)) return Bad<AssetClassDto>("درصد ارزش اسقاط نامعتبر است.");
            if (dto.StartRule.HasValue && !Enum.IsDefined(typeof(FnDepreciationStart), dto.StartRule.Value)) return Bad<AssetClassDto>("قاعده شروع استهلاک نامعتبر است.");

            string assetCode = Clean(dto.AssetAccountCode), accCode = Clean(dto.AccumulatedAccountCode);
            if (assetCode == null) return Bad<AssetClassDto>("حساب دارایی را مشخص کنید.");
            var wanted = new List<string> { assetCode };
            if (accCode != null) wanted.Add(accCode);
            Dictionary<string, FnAccount> accounts = await _uow.Set<FnAccount>().AsNoTracking().Where(a => wanted.Contains(a.Code)).ToDictionaryAsync(a => a.Code, ct);
            if (!accounts.TryGetValue(assetCode, out FnAccount assetAccount) || assetAccount.Level != FnAccountLevel.Subsidiary || !assetAccount.IsActive ||
                !(assetCode.StartsWith("201") || assetCode.StartsWith("203")))
                return Bad<AssetClassDto>("حساب دارایی باید حساب معین فعال گروه ۲۰۱ یا ۲۰۳ باشد.");
            if (accCode != null && (!accounts.TryGetValue(accCode, out FnAccount accAccount) || accAccount.Level != FnAccountLevel.Subsidiary || !accAccount.IsActive ||
                                    !(accCode.StartsWith("202") || accCode.StartsWith("204"))))
                return Bad<AssetClassDto>("حساب استهلاک انباشته باید حساب معین فعال گروه ۲۰۲ یا ۲۰۴ باشد.");

            FnAssetClass c;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<FnAssetClass>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (c == null) return ResponseDtoHelper.NotFound<AssetClassDto>("رده دارایی پیدا نشد.");
            }
            else
            {
                c = new FnAssetClass { Jurisdiction = FinanceSeed.Iran, IdleRatePercent = 30, IdleAfterMonths = 6, StartRule = FnDepreciationStart.NextMonth };
                _uow.Set<FnAssetClass>().Add(c);
            }
            if (await _uow.Set<FnAssetClass>().AnyAsync(x => x.Code == code && x.Id != c.Id, ct)) return Bad<AssetClassDto>("این کد رده قبلاً ثبت شده است.");
            c.Code = code; c.Title = Clean(dto.Title); c.AssetAccountCode = assetCode; c.AccumulatedAccountCode = accCode; c.Method = dto.Method;
            c.RatePercent = dto.RatePercent; c.UsefulLifeYears = dto.UsefulLifeYears;
            if (dto.StartRule.HasValue) c.StartRule = dto.StartRule.Value;
            if (dto.IdleRatePercent.HasValue) c.IdleRatePercent = dto.IdleRatePercent.Value;
            if (dto.IdleAfterMonths.HasValue) c.IdleAfterMonths = dto.IdleAfterMonths.Value;
            if (dto.SalvagePercent.HasValue) c.SalvagePercent = dto.SalvagePercent.Value;
            c.Source = Clean(dto.Source) ?? F2Seed.DepreciationSource;
            c.Verify = dto.Verify; c.IsIntangible = dto.IsIntangible; c.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(c));
        }

        // ================================================================================== assets

        private async Task<List<AssetDto>> BuildDtosAsync(List<FnAsset> rows, bool withHistory, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(a => new int?[] { a.CreatedByUserId, a.ApprovedByUserId }), ct);
            List<int> employeeIds = rows.Where(a => a.CustodianEmployeeId.HasValue).Select(a => a.CustodianEmployeeId.Value).Distinct().ToList();
            Dictionary<int, string> custodians = employeeIds.Count == 0 ? new Dictionary<int, string>()
                : (await _uow.Set<FnEmployee>().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).Select(e => new { e.Id, e.FirstName, e.LastName, e.JobTitle }).ToListAsync(ct))
                    .ToDictionary(e => e.Id, e => (e.FirstName + " " + e.LastName).Trim() + (string.IsNullOrWhiteSpace(e.JobTitle) ? "" : " - " + e.JobTitle));
            List<int> costCenterIds = rows.Where(a => a.CostCenterId.HasValue).Select(a => a.CostCenterId.Value).Distinct().ToList();
            Dictionary<int, string> costCenters = costCenterIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<CostCenter>().AsNoTracking().Where(c => costCenterIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            Dictionary<int, List<FnAssetHistory>> history = new Dictionary<int, List<FnAssetHistory>>();
            if (withHistory)
            {
                List<int> ids = rows.Select(a => a.Id).ToList();
                history = (await _uow.Set<FnAssetHistory>().AsNoTracking().Where(h => ids.Contains(h.AssetId)).OrderBy(h => h.Date).ThenBy(h => h.Id).ToListAsync(ct))
                    .GroupBy(h => h.AssetId).ToDictionary(g => g.Key, g => g.ToList());
            }
            Dictionary<int, string> historyUsers = withHistory
                ? await InventoryUsers.GetNamesAsync(_uow, history.Values.SelectMany(x => x).Select(h => (int?)h.UserId), ct) : new Dictionary<int, string>();
            return rows.Select(a => new AssetDto
            {
                Id = a.Id, Code = a.Code, Title = a.Title, ClassId = a.ClassId, ClassCode = a.Class?.Code, ClassTitle = a.Class?.Title, Serial = a.Serial, Maker = a.Maker,
                Location = a.Location, CustodianEmployeeId = a.CustodianEmployeeId,
                CustodianTitle = a.CustodianEmployeeId.HasValue && custodians.TryGetValue(a.CustodianEmployeeId.Value, out string cn) ? cn : a.CustodianTitle,
                CostCenterId = a.CostCenterId, CostCenterCode = a.CostCenterId.HasValue && costCenters.TryGetValue(a.CostCenterId.Value, out string cc) ? cc : null,
                MachineId = a.MachineId, AcquisitionDate = a.AcquisitionDate, InUseDate = a.InUseDate, Cost = a.Cost, SalvageValue = a.SalvageValue, Source = a.Source,
                SourceId = a.SourceId, SourceNumber = a.SourceNumber, OffsetAccountCode = a.OffsetAccountCode, OpeningAccumulated = a.OpeningAccumulated,
                AccumulatedDepreciation = a.AccumulatedDepreciation, BookValue = a.Cost - a.AccumulatedDepreciation, DepreciatedThrough = a.DepreciatedThrough, Status = a.Status,
                DisposedAt = a.DisposedAt, CreatorName = names.Name(a.CreatedByUserId), ApprovedByName = names.Name(a.ApprovedByUserId), ApprovedAt = a.ApprovedAt,
                History = history.TryGetValue(a.Id, out List<FnAssetHistory> hs)
                    ? hs.Select(h => new AssetHistoryDto { Date = h.Date, Kind = h.Kind, Amount = h.Amount, Note = h.Note, UserName = historyUsers.Name(h.UserId) }).ToList()
                    : new List<AssetHistoryDto>(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<AssetDto>>> GetAssetsAsync(AssetQuery query, CancellationToken ct)
        {
            query ??= new AssetQuery();
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<AssetDto>>(NotAllowed);
            await EnsureAsync(ct);
            IQueryable<FnAsset> q = _uow.Set<FnAsset>().AsNoTracking();
            if (query.Status.HasValue) q = q.Where(a => a.Status == query.Status.Value);
            if (query.ClassId.HasValue) q = q.Where(a => a.ClassId == query.ClassId.Value);
            if (query.CostCenterId.HasValue) q = q.Where(a => a.CostCenterId == query.CostCenterId.Value);
            if (query.CustodianEmployeeId.HasValue) q = q.Where(a => a.CustodianEmployeeId == query.CustodianEmployeeId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(a => a.Code.Contains(text) || a.Title.Contains(text) || (a.Serial != null && a.Serial.Contains(text)));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnAsset> rows = await q.Include(a => a.Class).OrderBy(a => a.Code).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await BuildDtosAsync(rows, false, ct), total, number, size));
        }

        public async Task<ResponseDto<AssetDto>> GetAssetAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<AssetDto>(NotAllowed);
            FnAsset a = await _uow.Set<FnAsset>().AsNoTracking().Include(x => x.Class).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a == null) return ResponseDtoHelper.NotFound<AssetDto>("دارایی پیدا نشد.");
            return ResponseDtoHelper.Ok((await BuildDtosAsync(new List<FnAsset> { a }, true, ct))[0]);
        }

        public async Task<ResponseDto<AssetDto>> SaveAssetAsync(SaveAssetDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<AssetDto>(NotAllowed);
            if (dto == null) return Bad<AssetDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            if (Clean(dto.Title) == null) return Bad<AssetDto>("عنوان دارایی را وارد کنید.");
            if (dto.Cost <= 0) return Bad<AssetDto>("بهای تمام‌شده باید بیشتر از صفر باشد.");
            if (!Enum.IsDefined(typeof(FnAssetSource), dto.Source)) return Bad<AssetDto>("منبع دارایی نامعتبر است.");

            FnAsset a = null;
            if (dto.Id.HasValue)
            {
                a = await _uow.Set<FnAsset>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (a == null) return ResponseDtoHelper.NotFound<AssetDto>("دارایی پیدا نشد.");
                if (a.Status != FnAssetStatus.Draft) return Bad<AssetDto>("فقط پیش‌نویس دارایی ویرایش می‌شود؛ تغییرات بعدی با عملیات «انتقال»، «بهبود» و ... ثبت می‌شود.");
            }
            else if (dto.Source == FnAssetSource.SupplierInvoiceLine) return Bad<AssetDto>("دارایی از ردیف فاکتور خرید را سیستم پیش‌نویس می‌کند.");

            FnAssetClass cls = null;
            if (dto.ClassId.HasValue)
            {
                cls = await _uow.Set<FnAssetClass>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.ClassId.Value, ct);
                if (cls == null || !cls.IsActive) return Bad<AssetDto>("رده دارایی پیدا نشد یا غیرفعال است.");
            }
            if (dto.CostCenterId.HasValue && !await _uow.Set<CostCenter>().AnyAsync(c => c.Id == dto.CostCenterId.Value && c.IsActive, ct)) return Bad<AssetDto>("مرکز هزینه پیدا نشد.");
            if (dto.CustodianEmployeeId.HasValue && !await _uow.Set<FnEmployee>().AnyAsync(e => e.Id == dto.CustodianEmployeeId.Value, ct)) return Bad<AssetDto>("امین دارایی (کارمند) پیدا نشد.");
            decimal salvage = R(dto.SalvageValue ?? (cls == null ? 0 : dto.Cost * cls.SalvagePercent / 100m));
            if (salvage < 0 || salvage >= dto.Cost) return Bad<AssetDto>("ارزش اسقاط باید کمتر از بهای تمام‌شده باشد.");
            if (dto.InUseDate.HasValue && dto.AcquisitionDate.HasValue && dto.InUseDate.Value.Date < dto.AcquisitionDate.Value.Date) return Bad<AssetDto>("تاریخ بهره‌برداری پیش از تاریخ تحصیل است.");

            decimal openingAcc = 0;
            DateTime? throughMonth = null;
            if (dto.Source == FnAssetSource.Opening)
            {
                if (cls == null) return Bad<AssetDto>("برای دارایی افتتاحیه رده دارایی لازم است.");
                if (!dto.InUseDate.HasValue) return Bad<AssetDto>("برای دارایی افتتاحیه تاریخ بهره‌برداری لازم است.");
                openingAcc = R(dto.OpeningAccumulated ?? 0);
                if (openingAcc < 0 || openingAcc > dto.Cost - salvage) return Bad<AssetDto>("استهلاک انباشته افتتاحیه نمی‌تواند از بهای تمام‌شده منهای ارزش اسقاط بیشتر باشد.");
                if (openingAcc > 0 && !dto.OpeningDepreciatedThrough.HasValue) return Bad<AssetDto>("آخرین ماهی را که استهلاک آن در دفاتر قبلی ثبت شده مشخص کنید.");
                if (dto.OpeningDepreciatedThrough.HasValue)
                {
                    DateTime d = dto.OpeningDepreciatedThrough.Value.Date;
                    throughMonth = PersianCal.StartOfIndex(PersianCal.Index(d));
                }
            }
            else if (dto.OpeningAccumulated.GetValueOrDefault() != 0) return Bad<AssetDto>("استهلاک انباشته افتتاحیه فقط برای دارایی افتتاحیه است.");

            if (a == null)
            {
                int n = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.AssetDraft, 0, ct);
                a = new FnAsset { Code = "TMP-" + n.ToString("00000"), Status = FnAssetStatus.Draft, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now };
                _uow.Set<FnAsset>().Add(a);
            }
            a.Title = Clean(dto.Title); a.ClassId = dto.ClassId; a.Serial = Clean(dto.Serial); a.Maker = Clean(dto.Maker); a.Location = Clean(dto.Location);
            a.CustodianEmployeeId = dto.CustodianEmployeeId; a.CustodianTitle = Clean(dto.CustodianTitle); a.CostCenterId = dto.CostCenterId; a.MachineId = dto.MachineId;
            a.AcquisitionDate = dto.AcquisitionDate?.Date; a.InUseDate = dto.InUseDate?.Date; a.Cost = R(dto.Cost); a.SalvageValue = salvage;
            if (a.Source != FnAssetSource.SupplierInvoiceLine) a.Source = dto.Source;
            if (a.Source != FnAssetSource.SupplierInvoiceLine) a.SourceNumber = Clean(dto.SourceNumber);
            a.OffsetAccountCode = Clean(dto.OffsetAccountCode);
            a.OpeningAccumulated = openingAcc; a.AccumulatedDepreciation = openingAcc; a.DepreciatedThrough = throughMonth;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<AssetDto>(Conflict); }
            return await GetAssetAsync(a.Id, ct);
        }

        private async Task<FnAccount> SubsidiaryAsync(string code, CancellationToken ct)
        {
            if (code == null) return null;
            FnAccount account = await _uow.Set<FnAccount>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
            return account != null && account.IsActive && account.Level == FnAccountLevel.Subsidiary ? account : null;
        }

        private static AccountingEventLine AssetLine(string role, FnAsset a, string costCenterCode, decimal debit, decimal credit, string text) => new AccountingEventLine
        {
            AccountRole = role, Debit = debit, Credit = credit, PartyType = "Asset", PartyId = a.Id, CostCenter = costCenterCode, Description = text,
        };

        private static string AccountRole(string code) => EventPostingService.DirectRolePrefix + code;

        private void History(FnAsset a, FnAssetHistoryKind kind, DateTime date, decimal amount, string note)
        {
            _uow.Set<FnAssetHistory>().Add(new FnAssetHistory { Asset = a, Kind = kind, Date = date.Date, Amount = amount, Note = note, UserId = Me, At = DateTimeOffset.Now });
        }

        public async Task<ResponseDto<AssetDto>> AssetActionAsync(AssetActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<AssetDto>(NotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<AssetDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            FnAsset a = await _uow.Set<FnAsset>().Include(x => x.Class).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (a == null) return ResponseDtoHelper.NotFound<AssetDto>("دارایی پیدا نشد.");
            DateTime date = dto.Date?.Date ?? DateTime.Today;
            string costCenterCode = a.CostCenterId.HasValue ? await _uow.Set<CostCenter>().Where(c => c.Id == a.CostCenterId.Value).Select(c => c.Code).FirstOrDefaultAsync(ct) : null;

            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "approve":
                {
                    if (a.Status != FnAssetStatus.Draft) return Bad<AssetDto>("فقط پیش‌نویس تایید می‌شود.");
                    if (a.CreatedByUserId > 0 && a.CreatedByUserId == Me) return Bad<AssetDto>(SameUser);
                    FnAssetClass cls = a.Class;
                    if (cls == null || !cls.IsActive) return Bad<AssetDto>("رده دارایی را مشخص کنید.");
                    if (a.Cost <= 0) return Bad<AssetDto>("بهای تمام‌شده باید بیشتر از صفر باشد.");
                    if (a.SalvageValue == 0 && cls.SalvagePercent > 0) a.SalvageValue = R(a.Cost * cls.SalvagePercent / 100m);
                    bool opening = a.Source == FnAssetSource.Opening;
                    if (opening)
                    {
                        if (!a.InUseDate.HasValue) return Bad<AssetDto>("برای دارایی افتتاحیه تاریخ بهره‌برداری لازم است.");
                    }
                    else
                    {
                        if (await SubsidiaryAsync(Clean(a.OffsetAccountCode), ct) == null)
                            return Bad<AssetDto>("حساب طرف بستانکار (جایی که بهای دارایی اکنون در دفاتر نشسته، مثلاً موجودی کالا یا هزینه خدمات خریداری‌شده) را مشخص کنید.");
                        if (!a.AcquisitionDate.HasValue) a.AcquisitionDate = date;
                    }
                    int sequence = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.AssetCode, cls.Id, ct);
                    a.Code = $"{cls.Code}-{sequence:0000}";
                    a.Status = !a.InUseDate.HasValue ? FnAssetStatus.UnderConstruction : FnAssetStatus.InUse;
                    a.ApprovedByUserId = Me; a.ApprovedAt = DateTimeOffset.Now;
                    History(a, FnAssetHistoryKind.Acquired, a.AcquisitionDate ?? a.InUseDate ?? date, a.Cost, opening ? "دارایی افتتاحیه" : Clean(dto.Note));
                    if (!opening)
                    {
                        string debitRole = a.Status == FnAssetStatus.UnderConstruction ? AccountRoles.AssetUnderConstruction : AccountRole(cls.AssetAccountCode);
                        // a is saved already (draft), so its id is known for the detail account
                        _costing.AddEvent(AccountingEventType.AssetAcquisition, a.AcquisitionDate.Value, "FnAsset", a.Id, a.Code, $"تحصیل دارایی {a.Code}", new[]
                        {
                            AssetLine(debitRole, a, costCenterCode, a.Cost, 0, $"تحصیل {a.Code}"),
                            new AccountingEventLine { AccountRole = AccountRole(a.OffsetAccountCode), Credit = a.Cost, Description = $"انتقال بها به دارایی {a.Code}" },
                        });
                    }
                    break;
                }
                case "commission":
                {
                    if (a.Status != FnAssetStatus.UnderConstruction) return Bad<AssetDto>("فقط دارایی در جریان تکمیل به بهره‌برداری می‌رسد.");
                    _costing.AddEvent(AccountingEventType.AssetCommissioned, date, "FnAsset", a.Id, a.Code, $"بهره‌برداری دارایی {a.Code}", new[]
                    {
                        AssetLine(AccountRole(a.Class.AssetAccountCode), a, costCenterCode, a.Cost, 0, $"بهره‌برداری {a.Code}"),
                        AssetLine(AccountRoles.AssetUnderConstruction, a, costCenterCode, 0, a.Cost, $"خروج از دارایی در جریان تکمیل {a.Code}"),
                    });
                    a.InUseDate = date; a.Status = FnAssetStatus.InUse;
                    History(a, FnAssetHistoryKind.Commissioned, date, a.Cost, Clean(dto.Note));
                    break;
                }
                case "idle-from":
                    if (a.Status != FnAssetStatus.InUse) return Bad<AssetDto>("فقط دارایی در حال بهره‌برداری بلااستفاده می‌شود.");
                    a.Status = FnAssetStatus.Idle;
                    History(a, FnAssetHistoryKind.IdleFrom, date, 0, Clean(dto.Note));
                    break;
                case "idle-to":
                    if (a.Status != FnAssetStatus.Idle) return Bad<AssetDto>("فقط دارایی بلااستفاده دوباره به کار می‌افتد.");
                    a.Status = FnAssetStatus.InUse;
                    History(a, FnAssetHistoryKind.IdleTo, date, 0, Clean(dto.Note));
                    break;
                case "transfer":
                {
                    if (a.Status == FnAssetStatus.Draft || a.Status == FnAssetStatus.Disposed) return Bad<AssetDto>("این دارایی منتقل نمی‌شود.");
                    if (dto.CostCenterId.HasValue)
                    {
                        if (!await _uow.Set<CostCenter>().AnyAsync(c => c.Id == dto.CostCenterId.Value && c.IsActive, ct)) return Bad<AssetDto>("مرکز هزینه پیدا نشد.");
                        a.CostCenterId = dto.CostCenterId;
                    }
                    if (dto.CustodianEmployeeId.HasValue)
                    {
                        if (!await _uow.Set<FnEmployee>().AnyAsync(e => e.Id == dto.CustodianEmployeeId.Value, ct)) return Bad<AssetDto>("امین دارایی (کارمند) پیدا نشد.");
                        a.CustodianEmployeeId = dto.CustodianEmployeeId;
                    }
                    if (Clean(dto.CustodianTitle) != null) a.CustodianTitle = Clean(dto.CustodianTitle);
                    if (Clean(dto.Location) != null) a.Location = Clean(dto.Location);
                    if (!dto.CostCenterId.HasValue && !dto.CustodianEmployeeId.HasValue && Clean(dto.CustodianTitle) == null && Clean(dto.Location) == null)
                        return Bad<AssetDto>("مقصد انتقال (مرکز هزینه، امین یا محل) را مشخص کنید.");
                    History(a, FnAssetHistoryKind.Transfer, date, 0, Clean(dto.Note));
                    break;
                }
                case "improve":
                {
                    if (a.Status != FnAssetStatus.InUse && a.Status != FnAssetStatus.Idle && a.Status != FnAssetStatus.UnderConstruction) return Bad<AssetDto>("برای این دارایی بهبود ثبت نمی‌شود.");
                    decimal amount = R(dto.Amount ?? 0);
                    if (amount <= 0) return Bad<AssetDto>("مبلغ بهبود را وارد کنید.");
                    string offset = Clean(dto.OffsetAccountCode);
                    if (await SubsidiaryAsync(offset, ct) == null) return Bad<AssetDto>("حساب طرف بستانکار را مشخص کنید.");
                    string role = a.Status == FnAssetStatus.UnderConstruction ? AccountRoles.AssetUnderConstruction : AccountRole(a.Class.AssetAccountCode);
                    _costing.AddEvent(AccountingEventType.AssetImprovement, date, "FnAsset", a.Id, a.Code, $"بهبود و اضافات دارایی {a.Code}", new[]
                    {
                        AssetLine(role, a, costCenterCode, amount, 0, $"اضافات {a.Code}"),
                        new AccountingEventLine { AccountRole = AccountRole(offset), Credit = amount, Description = $"انتقال بها به دارایی {a.Code}" },
                    });
                    a.Cost += amount;
                    History(a, FnAssetHistoryKind.Improvement, date, amount, Clean(dto.Note));
                    break;
                }
                default:
                    return Bad<AssetDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<AssetDto>(Conflict); }
            return await GetAssetAsync(a.Id, ct);
        }
    }
}
