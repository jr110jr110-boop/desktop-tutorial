using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public partial class AssetService
    {
        // ================================================================================== depreciation runs

        private async Task<Dictionary<int, List<AssetMath.IdleInterval>>> IdleAsync(IEnumerable<int> assetIds, CancellationToken ct)
        {
            List<int> ids = assetIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, List<AssetMath.IdleInterval>>();
            List<FnAssetHistory> rows = await _uow.Set<FnAssetHistory>().AsNoTracking()
                .Where(h => ids.Contains(h.AssetId) && (h.Kind == FnAssetHistoryKind.IdleFrom || h.Kind == FnAssetHistoryKind.IdleTo)).ToListAsync(ct);
            return rows.GroupBy(h => h.AssetId).ToDictionary(g => g.Key, g => AssetMath.Intervals(g));
        }

        private async Task<DepreciationRunDto> RunDtoAsync(int id, CancellationToken ct)
        {
            FnDepreciationRun run = await _uow.Set<FnDepreciationRun>().AsNoTracking().Include(r => r.Period).Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return null;
            List<int> assetIds = run.Lines.Select(l => l.AssetId).ToList();
            Dictionary<int, FnAsset> assets = await _uow.Set<FnAsset>().AsNoTracking().Include(a => a.Class).Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { run.PreparedByUserId, run.ApprovedByUserId }, ct);
            return new DepreciationRunDto
            {
                Id = run.Id, Number = run.Number, PeriodId = run.PeriodId, PeriodTitle = run.Period.Title, PeriodStart = run.Period.StartDate, PeriodEnd = run.Period.EndDate,
                Status = run.Status, TotalAmount = run.TotalAmount, AssetCount = run.AssetCount,
                PreparedByName = run.PreparedByUserId == 0 ? "سیستم (کار زمان‌بندی‌شده)" : names.Name(run.PreparedByUserId),
                ApprovedByName = names.Name(run.ApprovedByUserId), PreparedAt = run.PreparedAt, ApprovedAt = run.ApprovedAt,
                Lines = run.Lines.OrderBy(l => l.Id).Select(l =>
                {
                    assets.TryGetValue(l.AssetId, out FnAsset a);
                    return new DepreciationLineDto
                    {
                        AssetId = l.AssetId, AssetCode = a?.Code, AssetTitle = a?.Title, ClassCode = a?.Class?.Code, CostCenterId = l.CostCenterId, Months = l.Months,
                        IdleMonths = l.IdleMonths, Amount = l.Amount, BookValueBefore = l.BookValueBefore, AccumulatedAfter = l.AccumulatedAfter,
                    };
                }).ToList(),
            };
        }

        public async Task<ResponseDto<PagedList<DepreciationRunDto>>> GetDepreciationRunsAsync(DepreciationRunQuery query, CancellationToken ct)
        {
            query ??= new DepreciationRunQuery();
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<DepreciationRunDto>>(NotAllowed);
            IQueryable<FnDepreciationRun> q = _uow.Set<FnDepreciationRun>().AsNoTracking();
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            if (query.Year.HasValue) q = q.Where(r => r.Period.FiscalYear.Year == query.Year.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<int> ids = await q.OrderByDescending(r => r.Period.EndDate).Skip((number - 1) * size).Take(size).Select(r => r.Id).ToListAsync(ct);
            var rows = new List<DepreciationRunDto>();
            foreach (int id in ids)
            {
                DepreciationRunDto dto = await RunDtoAsync(id, ct);
                dto.Lines = new List<DepreciationLineDto>(); // the list carries totals only; the lines are read per run
                rows.Add(dto);
            }
            return ResponseDtoHelper.Ok(F2Page.Make(rows, total, number, size));
        }

        public async Task<ResponseDto<DepreciationRunDto>> GetDepreciationRunAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<DepreciationRunDto>(NotAllowed);
            DepreciationRunDto dto = await RunDtoAsync(id, ct);
            return dto == null ? ResponseDtoHelper.NotFound<DepreciationRunDto>("اجرای استهلاک پیدا نشد.") : ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<DepreciationRunDto>> PrepareDepreciationAsync(int periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<DepreciationRunDto>(NotAllowed);
            return await PrepareCoreAsync(periodId, ct);
        }

        private async Task<ResponseDto<DepreciationRunDto>> PrepareCoreAsync(int periodId, CancellationToken ct)
        {
            await EnsureAsync(ct);
            FnPeriod period = await _uow.Set<FnPeriod>().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == periodId, ct);
            if (period == null) return ResponseDtoHelper.NotFound<DepreciationRunDto>("دوره مالی پیدا نشد.");
            int target = PersianCal.Index(period.EndDate);
            if (PersianCal.Index(period.StartDate) != target) return Bad<DepreciationRunDto>("دوره مالی باید دقیقاً یک ماه شمسی باشد.");
            if (period.Status != FnPeriodStatus.Open) return Bad<DepreciationRunDto>("دوره مالی باز نیست.");

            FnDepreciationRun run = await _uow.Set<FnDepreciationRun>().Include(r => r.Lines).FirstOrDefaultAsync(r => r.PeriodId == periodId, ct);
            if (run != null && run.Status == FnRunStatus.Approved) return Bad<DepreciationRunDto>("استهلاک این دوره قبلاً تایید شده است.");
            if (await _uow.Set<FnDepreciationRun>().AnyAsync(r => r.Status == FnRunStatus.Draft && r.PeriodId != periodId && r.Period.EndDate < period.EndDate, ct))
                return Bad<DepreciationRunDto>("استهلاک یک دوره قبل هنوز تایید نشده است؛ ابتدا آن را تایید یا حذف کنید.");
            if (await _uow.Set<FnDepreciationRun>().AnyAsync(r => r.Status == FnRunStatus.Approved && r.Period.EndDate > period.EndDate, ct))
                return Bad<DepreciationRunDto>("استهلاک دوره‌های بعد تایید شده است.");

            List<FnAsset> assets = await _uow.Set<FnAsset>().AsNoTracking().Include(a => a.Class)
                .Where(a => (a.Status == FnAssetStatus.InUse || a.Status == FnAssetStatus.Idle) && a.ClassId != null && a.InUseDate != null).OrderBy(a => a.Code).ToListAsync(ct);
            Dictionary<int, List<AssetMath.IdleInterval>> idle = await IdleAsync(assets.Select(a => a.Id), ct);

            var missing = new SortedSet<string>(StringComparer.Ordinal);
            var computed = new List<(FnAsset Asset, AssetMath.Result Result)>();
            foreach (FnAsset a in assets)
            {
                FnAssetClass c = a.Class;
                if (!AssetMath.Depreciable(c)) continue;
                if (AssetMath.FirstIndex(a, c) > target) continue;
                if (!AssetMath.HasRate(c)) { missing.Add(c.Code); continue; }
                AssetMath.Result r = AssetMath.Compute(a, c, idle.TryGetValue(a.Id, out List<AssetMath.IdleInterval> iv) ? iv : new List<AssetMath.IdleInterval>(), target);
                if (r != null && r.Amount > 0) computed.Add((a, r));
            }
            if (missing.Count > 0)
                return Bad<DepreciationRunDto>("نرخ یا عمر مفید استهلاک رده‌های دارایی زیر تعیین نشده است: " + string.Join("، ", missing) + ". ابتدا نرخ را در رده‌های دارایی (با ذکر منبع) وارد کنید.");
            if (computed.Count == 0) return Bad<DepreciationRunDto>("دارایی‌ای برای استهلاک در این دوره وجود ندارد.");

            DateTime through = PersianCal.StartOfIndex(target);
            if (run == null)
            {
                run = new FnDepreciationRun
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.DepreciationRun, "DEP", period.EndDate, ct), PeriodId = period.Id, FiscalYearId = period.FiscalYearId,
                    Status = FnRunStatus.Draft,
                };
                _uow.Set<FnDepreciationRun>().Add(run);
            }
            else _uow.Set<FnDepreciationLine>().RemoveRange(run.Lines.ToList());
            run.Lines = computed.Select(x => new FnDepreciationLine
            {
                AssetId = x.Asset.Id, ClassId = x.Asset.ClassId.Value, CostCenterId = x.Asset.CostCenterId, Months = x.Result.Months, IdleMonths = x.Result.IdleMonths, Amount = x.Result.Amount,
                BookValueBefore = x.Asset.Cost - x.Asset.AccumulatedDepreciation, AccumulatedAfter = x.Result.AccumulatedAfter, ThroughMonth = through,
                YearStartYearAfter = x.Result.YearStartYear, YearStartAccumulatedAfter = x.Result.YearStartAccumulated,
            }).ToList();
            run.TotalAmount = run.Lines.Sum(l => l.Amount);
            run.AssetCount = run.Lines.Count;
            run.EditorUserIds = WithEditor(run.EditorUserIds, Me);
            run.PreparedByUserId = Me;
            run.PreparedAt = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<DepreciationRunDto>(Conflict); }
            return ResponseDtoHelper.Ok(await RunDtoAsync(run.Id, ct));
        }

        public async Task<ResponseDto<DepreciationRunDto>> DepreciationActionAsync(DepreciationActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<DepreciationRunDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            FnDepreciationRun run = await _uow.Set<FnDepreciationRun>().Include(r => r.Lines).Include(r => r.Period).FirstOrDefaultAsync(r => r.Id == dto.Id, ct);
            if (run == null) return ResponseDtoHelper.NotFound<DepreciationRunDto>("اجرای استهلاک پیدا نشد.");
            if (run.Status != FnRunStatus.Draft) return Bad<DepreciationRunDto>("اجرای تاییدشده تغییر نمی‌کند.");
            string action = dto.Action.Trim().ToLowerInvariant();

            if (action == "delete")
            {
                if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<DepreciationRunDto>(NotAllowed);
                _uow.Set<FnDepreciationLine>().RemoveRange(run.Lines.ToList());
                _uow.Set<FnDepreciationRun>().Remove(run);
                await _uow.SaveChangesAsync(ct);
                return ResponseDtoHelper.Ok(new DepreciationRunDto { Id = dto.Id, Number = run.Number });
            }
            if (action != "approve") return Bad<DepreciationRunDto>("عمل نامعتبر است.");
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<DepreciationRunDto>(NotAllowed + " (فقط مدیر مالی)");
            if (Touched(run.PreparedByUserId, run.EditorUserIds, Me)) return Bad<DepreciationRunDto>(SameUser);

            List<int> assetIds = run.Lines.Select(l => l.AssetId).ToList();
            Dictionary<int, FnAsset> assets = await _uow.Set<FnAsset>().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
            foreach (FnDepreciationLine l in run.Lines)
            {
                if (!assets.TryGetValue(l.AssetId, out FnAsset a) || (a.Status != FnAssetStatus.InUse && a.Status != FnAssetStatus.Idle) || a.AccumulatedDepreciation + l.Amount != l.AccumulatedAfter)
                    return Bad<DepreciationRunDto>("اطلاعات یک دارایی پس از آماده‌سازی تغییر کرده است؛ استهلاک را دوباره آماده کنید.");
            }
            Dictionary<int, FnAssetClass> classes = await _uow.Set<FnAssetClass>().AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
            List<int> ccIds = run.Lines.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId.Value).Distinct().ToList();
            Dictionary<int, CostCenter> centers = await _uow.Set<CostCenter>().AsNoTracking().Where(c => ccIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

            var lines = new List<AccountingEventLine>();
            foreach (var group in run.Lines.GroupBy(l => l.CostCenterId ?? 0).OrderBy(g => g.Key))
            {
                centers.TryGetValue(group.Key, out CostCenter cc);
                lines.Add(new AccountingEventLine
                {
                    AccountRole = cc != null && cc.IsProduction ? AccountRoles.DepreciationProduction : AccountRoles.DepreciationAdmin, Debit = group.Sum(l => l.Amount),
                    CostCenter = cc?.Code, Description = $"استهلاک {run.Period.Title}",
                });
            }
            foreach (FnDepreciationLine l in run.Lines.OrderBy(l => l.AssetId))
            {
                FnAsset a = assets[l.AssetId];
                lines.Add(new AccountingEventLine
                {
                    AccountRole = AccountRole(classes[l.ClassId].AccumulatedAccountCode), Credit = l.Amount, PartyType = "Asset", PartyId = a.Id, Description = $"استهلاک {a.Code}",
                });
                a.AccumulatedDepreciation = l.AccumulatedAfter;
                a.DepreciatedThrough = l.ThroughMonth;
                a.YearStartYear = l.YearStartYearAfter;
                a.YearStartAccumulated = l.YearStartAccumulatedAfter;
            }
            AccountingEvent e = _costing.AddEvent(AccountingEventType.DepreciationRun, run.Period.EndDate, "FnDepreciationRun", run.Id, run.Number, $"استهلاک دارایی‌های ثابت {run.Period.Title}", lines);
            run.Status = FnRunStatus.Approved;
            run.ApprovedByUserId = Me;
            run.ApprovedAt = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<DepreciationRunDto>(Conflict); }
            run.AccountingEventId = e.Id;
            try { await _uow.SaveChangesAsync(ct); } catch (DbUpdateException) { /* the link is informative only */ }
            return ResponseDtoHelper.Ok(await RunDtoAsync(run.Id, ct));
        }

        public async Task<string> DraftLastMonthAsync(CancellationToken ct)
        {
            await EnsureAsync(ct);
            int last = PersianCal.Index(DateTime.Today) - 1;
            DateTime end = PersianCal.EndOfIndex(last);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().FirstOrDefaultAsync(p => p.StartDate <= end && p.EndDate >= end && p.FiscalYear.LegalEntityId == FinanceSeed.MainEntityId, ct);
            if (period == null) return "no period";
            if (await _uow.Set<FnDepreciationRun>().AnyAsync(r => r.PeriodId == period.Id, ct)) return "exists";
            ResponseDto<DepreciationRunDto> result = await PrepareCoreAsync(period.Id, ct);
            return result.Success ? "drafted" : result.Message;
        }

        // ================================================================================== disposals

        private async Task<List<DisposalDto>> DisposalDtosAsync(List<FnAssetDisposal> rows, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(d => new int?[] { d.PreparedByUserId, d.ApprovedByUserId }), ct);
            return rows.Select(d => new DisposalDto
            {
                Id = d.Id, Number = d.Number, AssetId = d.AssetId, AssetCode = d.Asset?.Code, AssetTitle = d.Asset?.Title, Date = d.Date, Kind = d.Kind, Status = d.Status,
                Cost = d.Asset?.Cost ?? 0, ProceedsAmount = d.ProceedsAmount, ProceedsMethod = d.ProceedsMethod, CatchUpDepreciation = d.CatchUpDepreciation,
                AccumulatedAtDisposal = d.AccumulatedAtDisposal, BookValue = d.BookValue, GainLoss = d.GainLoss, VatPercent = d.VatPercent, VatAmount = d.VatAmount, Note = d.Note,
                PreparedByName = names.Name(d.PreparedByUserId), ApprovedByName = names.Name(d.ApprovedByUserId),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<DisposalDto>>> GetDisposalsAsync(DisposalQuery query, CancellationToken ct)
        {
            query ??= new DisposalQuery();
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<DisposalDto>>(NotAllowed);
            IQueryable<FnAssetDisposal> q = _uow.Set<FnAssetDisposal>().AsNoTracking();
            if (query.Status.HasValue) q = q.Where(d => d.Status == query.Status.Value);
            if (query.AssetId.HasValue) q = q.Where(d => d.AssetId == query.AssetId.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnAssetDisposal> rows = await q.Include(d => d.Asset).OrderByDescending(d => d.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await DisposalDtosAsync(rows, ct), total, number, size));
        }

        private async Task<(string Error, decimal CatchUp, decimal Accumulated, decimal Book)> PreviewAsync(FnAsset a, DateTime date, CancellationToken ct)
        {
            decimal catchUp = 0;
            FnAssetClass c = a.Class;
            if ((a.Status == FnAssetStatus.InUse || a.Status == FnAssetStatus.Idle) && a.InUseDate.HasValue && AssetMath.Depreciable(c))
            {
                int target = PersianCal.Index(date);
                if (a.DepreciatedThrough.HasValue && PersianCal.Index(a.DepreciatedThrough.Value) > target)
                    return ("تاریخ واگذاری پیش از آخرین ماهی است که استهلاک آن ثبت شده است.", 0, 0, 0);
                if (AssetMath.FirstIndex(a, c) <= target)
                {
                    if (!AssetMath.HasRate(c)) return ("نرخ یا عمر مفید استهلاک رده «" + c.Code + "» تعیین نشده است.", 0, 0, 0);
                    Dictionary<int, List<AssetMath.IdleInterval>> idle = await IdleAsync(new[] { a.Id }, ct);
                    AssetMath.Result r = AssetMath.Compute(a, c, idle.TryGetValue(a.Id, out List<AssetMath.IdleInterval> iv) ? iv : new List<AssetMath.IdleInterval>(), target);
                    catchUp = r?.Amount ?? 0;
                }
            }
            decimal accumulated = a.AccumulatedDepreciation + catchUp;
            return (null, catchUp, accumulated, a.Cost - accumulated);
        }

        public async Task<ResponseDto<DisposalDto>> SaveDisposalAsync(SaveDisposalDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<DisposalDto>(NotAllowed);
            if (dto == null) return Bad<DisposalDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            FnAsset a = await _uow.Set<FnAsset>().Include(x => x.Class).FirstOrDefaultAsync(x => x.Id == dto.AssetId, ct);
            if (a == null) return ResponseDtoHelper.NotFound<DisposalDto>("دارایی پیدا نشد.");
            if (a.Status != FnAssetStatus.InUse && a.Status != FnAssetStatus.Idle && a.Status != FnAssetStatus.UnderConstruction) return Bad<DisposalDto>("این دارایی قابل واگذاری نیست.");
            if (!Enum.IsDefined(typeof(FnDisposalKind), dto.Kind)) return Bad<DisposalDto>("نوع واگذاری نامعتبر است.");
            DateTime date = dto.Date.Date;
            DateTime since = a.InUseDate ?? a.AcquisitionDate ?? DateTime.MinValue;
            if (date < since.Date) return Bad<DisposalDto>("تاریخ واگذاری پیش از تاریخ تحصیل یا بهره‌برداری است.");
            decimal proceeds = R(dto.ProceedsAmount);
            if (proceeds < 0) return Bad<DisposalDto>("مبلغ دریافتی نمی‌تواند منفی باشد.");
            decimal vatPercent = 0;
            if (dto.Kind == FnDisposalKind.Sale && proceeds > 0)
            {
                vatPercent = dto.VatPercent ?? (dto.ApplyStandardVat ? await TaxRates.RateAtAsync(_uow, FnTaxKind.Vat, date, ct) ?? 0 : 0);
                if (vatPercent < 0 || vatPercent > 100) return Bad<DisposalDto>("درصد مالیات بر ارزش افزوده باید بین ۰ و ۱۰۰ باشد.");
            }
            FnProceedsMethod method = proceeds == 0 ? FnProceedsMethod.None : dto.ProceedsMethod;
            if (proceeds > 0 && method == FnProceedsMethod.None) return Bad<DisposalDto>("روش دریافت وجه را مشخص کنید.");
            if (method == FnProceedsMethod.Cash && !(dto.CashBoxId.HasValue && await _uow.Set<FnCashBox>().AnyAsync(x => x.Id == dto.CashBoxId.Value && x.IsActive, ct))) return Bad<DisposalDto>("صندوق پیدا نشد.");
            if (method == FnProceedsMethod.Bank && !(dto.BankAccountId.HasValue && await _uow.Set<FnBankAccount>().AnyAsync(x => x.Id == dto.BankAccountId.Value && x.IsActive, ct))) return Bad<DisposalDto>("حساب بانکی پیدا نشد.");
            if (method == FnProceedsMethod.Receivable && !(dto.CustomerId.HasValue && await _uow.Set<FnCustomer>().AnyAsync(x => x.Id == dto.CustomerId.Value, ct))) return Bad<DisposalDto>("مشتری پیدا نشد.");

            FnAssetDisposal d;
            if (dto.Id.HasValue)
            {
                d = await _uow.Set<FnAssetDisposal>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (d == null) return ResponseDtoHelper.NotFound<DisposalDto>("سند واگذاری پیدا نشد.");
                if (d.Status != FnDocStatus.Draft) return Bad<DisposalDto>("فقط پیش‌نویس ویرایش می‌شود.");
                if (d.AssetId != a.Id) return Bad<DisposalDto>("دارایی سند قابل تغییر نیست.");
            }
            else
            {
                if (await _uow.Set<FnAssetDisposal>().AnyAsync(x => x.AssetId == a.Id && x.Status == FnDocStatus.Draft, ct)) return Bad<DisposalDto>("برای این دارایی سند واگذاری در جریان وجود دارد.");
                d = new FnAssetDisposal
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.AssetDisposal, "DSP", date, ct), AssetId = a.Id, Status = FnDocStatus.Draft,
                    PreparedByUserId = Me, PreparedAt = DateTimeOffset.Now,
                };
                _uow.Set<FnAssetDisposal>().Add(d);
            }
            var preview = await PreviewAsync(a, date, ct);
            if (preview.Error != null) return Bad<DisposalDto>(preview.Error);
            d.Date = date; d.Kind = dto.Kind; d.ProceedsAmount = proceeds; d.ProceedsMethod = method; d.VatPercent = vatPercent; d.VatAmount = R(proceeds * vatPercent / 100m);
            d.CashBoxId = method == FnProceedsMethod.Cash ? dto.CashBoxId : null;
            d.BankAccountId = method == FnProceedsMethod.Bank ? dto.BankAccountId : null;
            d.CustomerId = method == FnProceedsMethod.Receivable ? dto.CustomerId : null;
            d.CatchUpDepreciation = preview.CatchUp; d.AccumulatedAtDisposal = preview.Accumulated; d.BookValue = preview.Book; d.GainLoss = proceeds - preview.Book;
            d.Note = Clean(dto.Note);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<DisposalDto>(Conflict); }
            d.Asset = a;
            return ResponseDtoHelper.Ok((await DisposalDtosAsync(new List<FnAssetDisposal> { d }, ct))[0]);
        }

        public async Task<ResponseDto<DisposalDto>> DisposalActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<DisposalDto>("اطلاعات ارسال‌شده کامل نیست.");
            await EnsureAsync(ct);
            FnAssetDisposal d = await _uow.Set<FnAssetDisposal>().Include(x => x.Asset).ThenInclude(x => x.Class).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (d == null) return ResponseDtoHelper.NotFound<DisposalDto>("سند واگذاری پیدا نشد.");
            if (d.Status != FnDocStatus.Draft) return Bad<DisposalDto>("فقط پیش‌نویس قابل تصمیم‌گیری است.");
            string action = dto.Action.Trim().ToLowerInvariant();
            if (action == "cancel")
            {
                if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<DisposalDto>(NotAllowed);
                d.Status = FnDocStatus.Cancelled;
                await _uow.SaveChangesAsync(ct);
                return ResponseDtoHelper.Ok((await DisposalDtosAsync(new List<FnAssetDisposal> { d }, ct))[0]);
            }
            if (action != "approve") return Bad<DisposalDto>("عمل نامعتبر است.");
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<DisposalDto>(NotAllowed + " (فقط مدیر مالی)");
            if (d.PreparedByUserId != 0 && d.PreparedByUserId == Me) return Bad<DisposalDto>(SameUser);
            FnAsset a = d.Asset;
            if (a.Status != FnAssetStatus.InUse && a.Status != FnAssetStatus.Idle && a.Status != FnAssetStatus.UnderConstruction) return Bad<DisposalDto>("این دارایی قابل واگذاری نیست.");

            // everything is recomputed now: depreciation may have been approved since the draft was saved
            var p = await PreviewAsync(a, d.Date, ct);
            if (p.Error != null) return Bad<DisposalDto>(p.Error);
            d.CatchUpDepreciation = p.CatchUp; d.AccumulatedAtDisposal = p.Accumulated; d.BookValue = p.Book; d.GainLoss = d.ProceedsAmount - p.Book;

            CostCenter cc = a.CostCenterId.HasValue ? await _uow.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == a.CostCenterId.Value, ct) : null;
            var lines = new List<AccountingEventLine>();
            if (p.CatchUp > 0)
                lines.Add(new AccountingEventLine
                {
                    AccountRole = cc != null && cc.IsProduction ? AccountRoles.DepreciationProduction : AccountRoles.DepreciationAdmin, Debit = p.CatchUp, CostCenter = cc?.Code,
                    Description = $"استهلاک تا تاریخ واگذاری {a.Code}",
                });
            if (a.AccumulatedDepreciation > 0 && !string.IsNullOrEmpty(a.Class?.AccumulatedAccountCode))
                lines.Add(AssetLine(AccountRole(a.Class.AccumulatedAccountCode), a, null, a.AccumulatedDepreciation, 0, $"برگشت استهلاک انباشته {a.Code}"));
            if (d.ProceedsAmount > 0)
            {
                switch (d.ProceedsMethod)
                {
                    case FnProceedsMethod.Cash:
                        lines.Add(new AccountingEventLine { AccountRole = AccountRoles.Cash, Debit = d.ProceedsAmount + d.VatAmount, PartyType = "CashBox", PartyId = d.CashBoxId, Description = $"دریافت بهای واگذاری {a.Code}" });
                        break;
                    case FnProceedsMethod.Bank:
                    {
                        FnBankAccount bank = await _uow.Set<FnBankAccount>().AsNoTracking().Include(b => b.Account).FirstAsync(b => b.Id == d.BankAccountId.Value, ct);
                        lines.Add(new AccountingEventLine { AccountRole = AccountRole(bank.Account.Code), Debit = d.ProceedsAmount + d.VatAmount, PartyType = "BankAccount", PartyId = bank.Id, Description = $"دریافت بهای واگذاری {a.Code}" });
                        break;
                    }
                    default:
                        lines.Add(new AccountingEventLine { AccountRole = AccountRoles.AccountsReceivable, Debit = d.ProceedsAmount + d.VatAmount, PartyType = "Customer", PartyId = d.CustomerId, Description = $"بهای واگذاری {a.Code}" });
                        break;
                }
                if (d.VatAmount > 0) lines.Add(new AccountingEventLine { AccountRole = AccountRoles.VatSales, Credit = d.VatAmount, Description = $"مالیات ارزش افزوده فروش دارایی {a.Code}" });
            }
            if (d.GainLoss < 0) lines.Add(new AccountingEventLine { AccountRole = AccountRoles.AssetLossOnDisposal, Debit = -d.GainLoss, Description = $"زیان واگذاری {a.Code}" });
            if (d.GainLoss > 0) lines.Add(new AccountingEventLine { AccountRole = AccountRoles.AssetGainOnDisposal, Credit = d.GainLoss, Description = $"سود واگذاری {a.Code}" });
            string assetRole = a.Status == FnAssetStatus.UnderConstruction ? AccountRoles.AssetUnderConstruction : AccountRole(a.Class.AssetAccountCode);
            lines.Add(AssetLine(assetRole, a, cc?.Code, 0, a.Cost, $"خروج دارایی {a.Code}"));
            AccountingEvent e = _costing.AddEvent(AccountingEventType.AssetDisposal, d.Date, "FnAssetDisposal", d.Id, d.Number, $"واگذاری دارایی {a.Code}", lines);

            a.AccumulatedDepreciation = p.Accumulated;
            if (p.CatchUp > 0) a.DepreciatedThrough = PersianCal.StartOfIndex(PersianCal.Index(d.Date));
            a.Status = FnAssetStatus.Disposed;
            a.DisposedAt = d.Date;
            History(a, FnAssetHistoryKind.Disposal, d.Date, d.ProceedsAmount, d.Number);
            d.Status = FnDocStatus.Posted;
            d.ApprovedByUserId = Me;
            d.ApprovedAt = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<DisposalDto>(Conflict); }
            _ = e;
            return ResponseDtoHelper.Ok((await DisposalDtosAsync(new List<FnAssetDisposal> { d }, ct))[0]);
        }

        // ================================================================================== reports

        public async Task<ResponseDto<DepreciationScheduleDto>> GetDepreciationScheduleAsync(int year, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<DepreciationScheduleDto>(NotAllowed);
            if (year < 1300 || year > 1600) return Bad<DepreciationScheduleDto>("سال نامعتبر است.");
            var lines = await _uow.Set<FnDepreciationLine>().AsNoTracking()
                .Where(l => l.Run.Status == FnRunStatus.Approved && l.ThroughMonth >= PersianCal.Start(year, 1) && l.ThroughMonth <= PersianCal.End(year, 12))
                .Select(l => new { l.AssetId, l.ClassId, l.Amount, l.ThroughMonth }).ToListAsync(ct);
            List<int> assetIds = lines.Select(l => l.AssetId).Distinct().ToList();
            Dictionary<int, FnAsset> assets = await _uow.Set<FnAsset>().AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
            Dictionary<int, FnAssetClass> classes = await _uow.Set<FnAssetClass>().AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
            var result = new DepreciationScheduleDto { Year = year };
            foreach (var g in lines.GroupBy(l => l.AssetId).OrderBy(g => assets[g.Key].Code))
            {
                var row = new DepreciationScheduleRowDto { AssetId = g.Key, AssetCode = assets[g.Key].Code, AssetTitle = assets[g.Key].Title, ClassCode = classes[g.First().ClassId].Code };
                for (int m = 1; m <= 12; m++) row.Months.Add(g.Where(x => PersianCal.Month(x.ThroughMonth) == m).Sum(x => x.Amount));
                row.Total = row.Months.Sum();
                result.Assets.Add(row);
            }
            foreach (var g in result.Assets.GroupBy(r => r.ClassCode).OrderBy(g => g.Key))
            {
                var row = new DepreciationScheduleRowDto { ClassCode = g.Key, AssetCode = g.Key, AssetTitle = classes.Values.First(c => c.Code == g.Key).Title };
                for (int m = 0; m < 12; m++) row.Months.Add(g.Sum(r => r.Months[m]));
                row.Total = row.Months.Sum();
                result.Classes.Add(row);
            }
            result.Total = result.Classes.Sum(r => r.Total);
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<AssetMovementDto>> GetMovementAsync(int year, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<AssetMovementDto>(NotAllowed);
            if (year < 1300 || year > 1600) return Bad<AssetMovementDto>("سال نامعتبر است.");
            DateTime from = PersianCal.Start(year, 1), to = PersianCal.End(year, 12);
            List<FnAsset> assets = await _uow.Set<FnAsset>().AsNoTracking().Where(a => a.Status != FnAssetStatus.Draft && a.ClassId != null).ToListAsync(ct);
            List<int> ids = assets.Select(a => a.Id).ToList();
            List<FnAssetHistory> history = await _uow.Set<FnAssetHistory>().AsNoTracking()
                .Where(h => ids.Contains(h.AssetId) && (h.Kind == FnAssetHistoryKind.Acquired || h.Kind == FnAssetHistoryKind.Improvement)).ToListAsync(ct);
            var lineRows = await _uow.Set<FnDepreciationLine>().AsNoTracking().Where(l => l.Run.Status == FnRunStatus.Approved && ids.Contains(l.AssetId))
                .Select(l => new { l.AssetId, l.Amount, l.ThroughMonth }).ToListAsync(ct);
            Dictionary<int, FnAssetClass> classes = await _uow.Set<FnAssetClass>().AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
            var result = new AssetMovementDto { Year = year };
            var rows = new Dictionary<int, AssetMovementRowDto>();
            foreach (FnAsset a in assets)
            {
                if (a.DisposedAt.HasValue && a.DisposedAt.Value.Date < from) continue;
                if (!rows.TryGetValue(a.ClassId.Value, out AssetMovementRowDto row))
                {
                    FnAssetClass c = classes[a.ClassId.Value];
                    row = new AssetMovementRowDto { ClassCode = c.Code, ClassTitle = c.Title };
                    rows[c.Id] = row;
                }
                DateTime? approvedOn = a.ApprovedAt?.Date;
                foreach (FnAssetHistory h in history.Where(x => x.AssetId == a.Id))
                {
                    DateTime effective = h.Date.Date;
                    if (a.Source == FnAssetSource.Opening && h.Kind == FnAssetHistoryKind.Acquired)
                        effective = PersianCal.Start(PersianCal.Year(approvedOn ?? h.Date), 1).AddDays(-1); // opening assets belong to the opening balance of the year of approval
                    if (effective > to) continue;
                    if (effective < from) row.OpeningCost += h.Amount; else row.Additions += h.Amount;
                }
                if (a.Source == FnAssetSource.Opening && a.OpeningAccumulated > 0)
                {
                    int approvedYear = PersianCal.Year(approvedOn ?? a.CreatedAt.Date);
                    if (approvedYear < year) row.OpeningAccumulated += a.OpeningAccumulated;
                    else if (approvedYear == year) row.OpeningAccumulated += a.OpeningAccumulated;
                }
                foreach (var l in lineRows.Where(x => x.AssetId == a.Id))
                {
                    if (l.ThroughMonth < from) row.OpeningAccumulated += l.Amount;
                    else if (l.ThroughMonth <= to) row.Depreciation += l.Amount;
                }
                if (a.Status == FnAssetStatus.Disposed && a.DisposedAt.HasValue && a.DisposedAt.Value.Date <= to)
                {
                    FnAssetDisposal disposal = await _uow.Set<FnAssetDisposal>().AsNoTracking().FirstOrDefaultAsync(x => x.AssetId == a.Id && x.Status == FnDocStatus.Posted, ct);
                    row.Disposals += a.Cost;
                    row.DisposalAccumulated += disposal?.AccumulatedAtDisposal ?? a.AccumulatedDepreciation;
                    row.Depreciation += disposal?.CatchUpDepreciation ?? 0;
                }
            }
            foreach (AssetMovementRowDto r in rows.Values.OrderBy(x => x.ClassCode))
            {
                r.ClosingCost = r.OpeningCost + r.Additions - r.Disposals;
                r.ClosingAccumulated = r.OpeningAccumulated + r.Depreciation - r.DisposalAccumulated;
                r.NetBookValue = r.ClosingCost - r.ClosingAccumulated;
                result.Rows.Add(r);
            }
            result.Total = new AssetMovementRowDto
            {
                ClassCode = "TOTAL", ClassTitle = "جمع", OpeningCost = result.Rows.Sum(r => r.OpeningCost), Additions = result.Rows.Sum(r => r.Additions), Disposals = result.Rows.Sum(r => r.Disposals),
                ClosingCost = result.Rows.Sum(r => r.ClosingCost), OpeningAccumulated = result.Rows.Sum(r => r.OpeningAccumulated), Depreciation = result.Rows.Sum(r => r.Depreciation),
                DisposalAccumulated = result.Rows.Sum(r => r.DisposalAccumulated), ClosingAccumulated = result.Rows.Sum(r => r.ClosingAccumulated), NetBookValue = result.Rows.Sum(r => r.NetBookValue),
            };
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<AssetHandoverDto>> GetHandoverAsync(int? employeeId, string custodian, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<AssetHandoverDto>(NotAllowed);
            string text = Clean(custodian);
            if (!employeeId.HasValue && text == null) return Bad<AssetHandoverDto>("امین دارایی (کارمند یا عنوان) را مشخص کنید.");
            IQueryable<FnAsset> q = _uow.Set<FnAsset>().AsNoTracking().Include(a => a.Class).Where(a => a.Status != FnAssetStatus.Draft && a.Status != FnAssetStatus.Disposed);
            if (employeeId.HasValue) q = q.Where(a => a.CustodianEmployeeId == employeeId.Value);
            else q = q.Where(a => a.CustodianTitle != null && a.CustodianTitle.Contains(text));
            List<FnAsset> rows = await q.OrderBy(a => a.Code).ToListAsync(ct);
            var dto = new AssetHandoverDto { EmployeeId = employeeId, PrintedOn = DateTime.Today, CustodianTitle = text };
            if (employeeId.HasValue)
            {
                var emp = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.Id == employeeId.Value).Select(e => new { e.FirstName, e.LastName, e.JobTitle }).FirstOrDefaultAsync(ct);
                if (emp == null) return ResponseDtoHelper.NotFound<AssetHandoverDto>("کارمند پیدا نشد.");
                dto.CustodianName = (emp.FirstName + " " + emp.LastName).Trim();
                dto.CustodianTitle = emp.JobTitle;
            }
            dto.Assets = await BuildDtosAsync(rows, false, ct);
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
