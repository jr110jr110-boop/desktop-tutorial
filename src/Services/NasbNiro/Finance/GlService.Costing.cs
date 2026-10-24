using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface ICostingCompletenessService
    {
        Task<ResponseDto<List<FnOverheadRateDto>>> GetOverheadRatesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnOverheadRateDto>> SaveOverheadRateAsync(FnOverheadRateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnAllocationKeyDto>>> GetAllocationKeysAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnAllocationKeyDto>> SaveAllocationKeyAsync(FnAllocationKeyDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnWorkOrderBaseDto>> SaveWorkOrderBaseAsync(FnWorkOrderBaseDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnWorkOrderBaseDto>>> GetWorkOrderBasesAsync(int periodId, CancellationToken cancellationToken);
        Task<ResponseDto<FnAllocationResultDto>> AllocateAsync(int periodId, FnAllocateRequest request, CancellationToken cancellationToken);
        Task<ResponseDto<FnWorkOrderCostDto>> CostSheetAsync(int workOrderId, CancellationToken cancellationToken);
        Task<ResponseDto<FnWorkOrderCloseDto>> CloseWorkOrderAsync(int workOrderId, FnWorkOrderCloseRequest request, CancellationToken cancellationToken);
        Task<ResponseDto<FnWorkOrderCloseDto>> ReopenWorkOrderAsync(int workOrderId, FnWorkOrderCloseRequest request, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnStandardCostDto>>> GetStandardCostsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnStandardCostDto>> SaveStandardCostAsync(FnStandardCostDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnContractRevenueDto>> SaveContractRevenueAsync(FnContractRevenueDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnContractRevenueDto>>> GetContractRevenueAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnRevenueRunDto>> RevenueRecognitionAsync(int periodId, FnRevenueRunRequest request, CancellationToken cancellationToken);
    }

    // Costing completeness (F3 section 5): overhead step-down allocation and absorption, work-order close-out, standard costs, percentage-of-completion revenue.
    public partial class GlService : ICostingCompletenessService
    {
        private const string OverheadPoolCode = "70206", AbsorbedCode = "70110", OverheadVarianceCode = "70111", CostVarianceCode = "70109";

        private async Task<Dictionary<string, CostCenter>> CenterByCodeAsync(CancellationToken ct) => await _uow.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Code, ct);

        // ------------------------------------------------------------------------------------------ rates, keys, bases (inputs)

        public async Task<ResponseDto<List<FnOverheadRateDto>>> GetOverheadRatesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnOverheadRateDto>>(NotAllowed);
            var centers = await _uow.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            var rows = await _uow.Set<FnOverheadRate>().AsNoTracking().OrderBy(r => r.CostCenterId).ThenBy(r => r.Base).ThenBy(r => r.ValidFrom).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(r => new FnOverheadRateDto { Id = r.Id, CostCenterCode = centers.GetValueOrDefault(r.CostCenterId), Base = r.Base, Rate = r.Rate, ValidFrom = r.ValidFrom, ValidTo = r.ValidTo, Source = r.Source, Verify = r.Verify }).ToList());
        }

        public async Task<ResponseDto<FnOverheadRateDto>> SaveOverheadRateAsync(FnOverheadRateDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnOverheadRateDto>(NotAllowed);
            if (dto == null || dto.Rate <= 0) return Bad<FnOverheadRateDto>("نرخ سربار باید بیشتر از صفر باشد.");
            if (!Enum.IsDefined(typeof(FnOverheadBase), dto.Base)) return Bad<FnOverheadRateDto>("مبنای نرخ نامعتبر است.");
            if (dto.ValidFrom == default || (dto.ValidTo.HasValue && dto.ValidTo.Value.Date < dto.ValidFrom.Date)) return Bad<FnOverheadRateDto>("بازه اعتبار نامعتبر است.");
            CostCenter cc = await _uow.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Code == (dto.CostCenterCode ?? "").Trim(), ct);
            if (cc == null) return Bad<FnOverheadRateDto>("مرکز هزینه پیدا نشد.");
            FnOverheadRate r = dto.Id.HasValue ? await _uow.Set<FnOverheadRate>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct) : null;
            if (dto.Id.HasValue && r == null) return ResponseDtoHelper.NotFound<FnOverheadRateDto>("نرخ پیدا نشد.");
            if (r == null) { r = new FnOverheadRate(); _uow.Set<FnOverheadRate>().Add(r); }
            r.CostCenterId = cc.Id; r.Base = dto.Base; r.Rate = dto.Rate; r.ValidFrom = dto.ValidFrom.Date; r.ValidTo = dto.ValidTo?.Date; r.Source = Clean(dto.Source); r.Verify = dto.Verify;
            await _uow.SaveChangesAsync(ct);
            dto.Id = r.Id;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<List<FnAllocationKeyDto>>> GetAllocationKeysAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnAllocationKeyDto>>(NotAllowed);
            var centers = await _uow.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            var rows = await _uow.Set<FnAllocationKey>().AsNoTracking().OrderBy(r => r.Sequence).ThenBy(r => r.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(r => new FnAllocationKeyDto { Id = r.Id, FromCostCenterCode = centers.GetValueOrDefault(r.FromCostCenterId), ToCostCenterCode = centers.GetValueOrDefault(r.ToCostCenterId), Basis = r.Basis, Value = r.Value, ValidFrom = r.ValidFrom, ValidTo = r.ValidTo, Sequence = r.Sequence, Source = r.Source }).ToList());
        }

        public async Task<ResponseDto<FnAllocationKeyDto>> SaveAllocationKeyAsync(FnAllocationKeyDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnAllocationKeyDto>(NotAllowed);
            if (dto == null || dto.Value <= 0) return Bad<FnAllocationKeyDto>("مقدار کلید تسهیم باید بیشتر از صفر باشد.");
            string basis = Clean(dto.Basis) ?? "Percent";
            if (!new[] { "Percent", "Area", "Headcount", "KWh", "Other" }.Contains(basis)) return Bad<FnAllocationKeyDto>("مبنا باید Percent، Area، Headcount، KWh یا Other باشد.");
            if (dto.ValidFrom == default) return Bad<FnAllocationKeyDto>("تاریخ شروع را وارد کنید.");
            var centers = await CenterByCodeAsync(ct);
            if (!centers.TryGetValue((dto.FromCostCenterCode ?? "").Trim(), out CostCenter from) || !centers.TryGetValue((dto.ToCostCenterCode ?? "").Trim(), out CostCenter to)) return Bad<FnAllocationKeyDto>("مرکز هزینه مبدأ یا مقصد پیدا نشد.");
            if (from.Id == to.Id) return Bad<FnAllocationKeyDto>("مبدأ و مقصد یکسان است.");
            FnAllocationKey k = dto.Id.HasValue ? await _uow.Set<FnAllocationKey>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct) : null;
            if (dto.Id.HasValue && k == null) return ResponseDtoHelper.NotFound<FnAllocationKeyDto>("کلید پیدا نشد.");
            if (k == null) { k = new FnAllocationKey(); _uow.Set<FnAllocationKey>().Add(k); }
            k.FromCostCenterId = from.Id; k.ToCostCenterId = to.Id; k.Basis = basis; k.Value = dto.Value; k.ValidFrom = dto.ValidFrom.Date; k.ValidTo = dto.ValidTo?.Date; k.Sequence = dto.Sequence; k.Source = Clean(dto.Source);
            await _uow.SaveChangesAsync(ct);
            dto.Id = k.Id; dto.Basis = basis;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FnWorkOrderBaseDto>> SaveWorkOrderBaseAsync(FnWorkOrderBaseDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnWorkOrderBaseDto>(NotAllowed);
            if (dto == null || dto.Quantity < 0) return Bad<FnWorkOrderBaseDto>("مقدار مبنا نامعتبر است.");
            if (!await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId, ct)) return Bad<FnWorkOrderBaseDto>("دستور کار پیدا نشد.");
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PeriodId, ct);
            if (period == null) return Bad<FnWorkOrderBaseDto>("دوره پیدا نشد.");
            if (await _uow.Set<FnOverheadAllocation>().AnyAsync(a => a.PeriodId == dto.PeriodId, ct)) return Bad<FnWorkOrderBaseDto>("تسهیم سربار این دوره انجام شده است.");
            var centers = await CenterByCodeAsync(ct);
            if (!centers.TryGetValue((dto.CostCenterCode ?? "").Trim(), out CostCenter cc)) return Bad<FnWorkOrderBaseDto>("مرکز هزینه پیدا نشد.");
            FnWorkOrderBase b = await _uow.Set<FnWorkOrderBase>().FirstOrDefaultAsync(x => x.WorkOrderId == dto.WorkOrderId && x.CostCenterId == cc.Id && x.PeriodId == dto.PeriodId && x.Base == dto.Base, ct);
            if (b == null) { b = new FnWorkOrderBase { WorkOrderId = dto.WorkOrderId, CostCenterId = cc.Id, PeriodId = dto.PeriodId, Base = dto.Base }; _uow.Set<FnWorkOrderBase>().Add(b); }
            b.Quantity = dto.Quantity; b.CreatedByUserId = Me;
            await _uow.SaveChangesAsync(ct);
            dto.Id = b.Id;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<List<FnWorkOrderBaseDto>>> GetWorkOrderBasesAsync(int periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnWorkOrderBaseDto>>(NotAllowed);
            var centers = await _uow.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            var rows = await _uow.Set<FnWorkOrderBase>().AsNoTracking().Where(b => b.PeriodId == periodId).OrderBy(b => b.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(b => new FnWorkOrderBaseDto { Id = b.Id, WorkOrderId = b.WorkOrderId, CostCenterCode = centers.GetValueOrDefault(b.CostCenterId), PeriodId = b.PeriodId, Base = b.Base, Quantity = b.Quantity }).ToList());
        }

        // ------------------------------------------------------------------------------------------ allocation, absorption and variance

        public async Task<ResponseDto<FnAllocationResultDto>> AllocateAsync(int periodId, FnAllocateRequest request, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnAllocationResultDto>(NotAllowed);
            bool dry = request?.DryRun ?? false;
            await FinanceSeed.EnsureAsync(_uow, ct);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == periodId, ct);
            if (period == null) return ResponseDtoHelper.NotFound<FnAllocationResultDto>("دوره پیدا نشد.");
            int entityId = period.FiscalYear.LegalEntityId;
            if (await _uow.Set<FnOverheadAllocation>().AnyAsync(a => a.PeriodId == periodId, ct)) return Bad<FnAllocationResultDto>("تسهیم و جذب سربار این دوره قبلاً انجام شده است.");
            DateTime start = period.StartDate.Date, end = period.EndDate.Date;
            var centers = await _uow.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

            // actual cost of every cost centre from the 702xx accounts
            var actualRows = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.CostCenterId != null && l.Account.Code.StartsWith("702") && l.Voucher.Date >= start && l.Voucher.Date <= end && l.Voucher.Status != FnVoucherStatus.Draft && l.Voucher.FiscalYear.LegalEntityId == entityId)
                .GroupBy(l => l.CostCenterId).Select(g => new { Id = g.Key.Value, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);
            var own = actualRows.ToDictionary(r => r.Id, r => r.Net);
            var pool = new Dictionary<int, decimal>(own);
            var received = new Dictionary<int, decimal>();
            var allocated = new Dictionary<int, decimal>();

            List<FnAllocationKey> keys = await _uow.Set<FnAllocationKey>().AsNoTracking().Where(k => k.ValidFrom <= end && (k.ValidTo == null || k.ValidTo >= end)).ToListAsync(ct);
            var result = new FnAllocationResultDto { PeriodId = periodId, DryRun = dry };
            var done = new HashSet<int>();
            var order = keys.GroupBy(k => k.FromCostCenterId).Select(g => new { From = g.Key, Seq = g.Min(x => x.Sequence), Keys = g.OrderBy(x => x.Id).ToList() }).OrderBy(g => g.Seq).ThenBy(g => g.From).ToList();
            var stepLines = new List<(int From, int To, decimal Amount)>();
            foreach (var src in order)
            {
                decimal amount = pool.GetValueOrDefault(src.From);
                done.Add(src.From);
                if (src.Keys.Any(k => done.Contains(k.ToCostCenterId))) return Bad<FnAllocationResultDto>($"ترتیب گام‌به‌گام نامعتبر است: مرکز {centers[src.From].Code} به مرکزی تسهیم می‌شود که پیش‌تر تسهیم شده است.");
                decimal total = src.Keys.Sum(k => k.Value), left = amount;
                for (int i = 0; i < src.Keys.Count; i++)
                {
                    FnAllocationKey k = src.Keys[i];
                    decimal share = i == src.Keys.Count - 1 ? left : R(amount * k.Value / total);
                    left -= share;
                    if (share == 0) continue;
                    pool[k.ToCostCenterId] = pool.GetValueOrDefault(k.ToCostCenterId) + share;
                    received[k.ToCostCenterId] = received.GetValueOrDefault(k.ToCostCenterId) + share;
                    stepLines.Add((src.From, k.ToCostCenterId, share));
                    result.Steps.Add(new FnAllocationStepDto { From = centers[src.From].Code, To = centers[k.ToCostCenterId].Code, Amount = share });
                }
                if (amount != 0) { allocated[src.From] = amount; pool[src.From] = 0; }
            }
            result.TotalAllocated = stepLines.Sum(s => s.Amount);

            // absorption into work orders
            var bases = await _uow.Set<FnWorkOrderBase>().AsNoTracking().Where(b => b.PeriodId == periodId).ToListAsync(ct);
            var rates = await _uow.Set<FnOverheadRate>().AsNoTracking().Where(r => r.ValidFrom <= end && (r.ValidTo == null || r.ValidTo >= end)).ToListAsync(ct);
            var absorbed = new Dictionary<int, decimal>();
            var absorptions = new List<FnAbsorptionDto>();
            var missing = new List<string>();
            foreach (FnWorkOrderBase b in bases)
            {
                FnOverheadRate rate = rates.Where(r => r.CostCenterId == b.CostCenterId && r.Base == b.Base).OrderByDescending(r => r.ValidFrom).FirstOrDefault();
                if (rate == null) { missing.Add($"{centers[b.CostCenterId].Code}/{b.Base}"); continue; }
                decimal amount = R(b.Quantity * rate.Rate);
                absorbed[b.CostCenterId] = absorbed.GetValueOrDefault(b.CostCenterId) + amount;
                absorptions.Add(new FnAbsorptionDto { WorkOrderId = b.WorkOrderId, CostCenterCode = centers[b.CostCenterId].Code, Base = b.Base, Quantity = b.Quantity, Rate = rate.Rate, Amount = amount });
            }
            if (missing.Count > 0) return Bad<FnAllocationResultDto>("نرخ جذب سربار ثبت نشده است: " + string.Join("، ", missing.Distinct()));
            result.Absorptions = absorptions;
            result.TotalAbsorbed = absorptions.Sum(a => a.Amount);

            // variance of the producing cost centres
            decimal variance = 0;
            var varianceRows = new List<(int Cc, decimal Actual, decimal Absorbed)>();
            foreach (int id in pool.Keys.Union(absorbed.Keys).Distinct().OrderBy(x => centers[x].Code, StringComparer.Ordinal))
            {
                bool producing = centers[id].IsProduction;
                decimal actual = producing ? pool.GetValueOrDefault(id) : 0, abs = absorbed.GetValueOrDefault(id);
                result.Centers.Add(new FnCenterResultDto
                {
                    CostCenterCode = centers[id].Code, IsProduction = producing, OwnCost = own.GetValueOrDefault(id), Received = received.GetValueOrDefault(id), Allocated = allocated.GetValueOrDefault(id),
                    Actual = actual, Absorbed = abs, Variance = producing ? actual - abs : 0,
                });
                if (!producing && abs == 0) continue;
                if (!producing) return Bad<FnAllocationResultDto>($"مرکز {centers[id].Code} تولیدی نیست ولی سربار جذب می‌کند.");
                variance += actual - abs;
                varianceRows.Add((id, actual, abs));
            }
            result.Variance = variance;
            if (dry) return ResponseDtoHelper.Ok(result);
            if (stepLines.Count == 0 && absorptions.Count == 0 && varianceRows.Count == 0) return Bad<FnAllocationResultDto>("برای این دوره هزینه‌ای برای تسهیم یا جذب پیدا نشد.");

            FnAccount pooled = await AccountByCodeAsync(OverheadPoolCode, ct), absorbedAcc = await AccountByCodeAsync(AbsorbedCode, ct), varianceAcc = await AccountByCodeAsync(OverheadVarianceCode, ct), wip = await RoleAccountAsync(AccountRoles.Wip, "10520", ct);
            if (pooled == null || absorbedAcc == null || varianceAcc == null || wip == null) return Bad<FnAllocationResultDto>("حساب‌های سربار (70206، 70110، 70111، 10520) در کدینگ نیست.");
            string number = await NextNumberAsync("OVH", FinanceNumbers.OverheadAllocation, end, ct);
            // the run record is saved FIRST: the unique index (one run per entity and period) stops a concurrent second run before any voucher exists
            var rec = new FnOverheadAllocation
            {
                Number = number, LegalEntityId = entityId, PeriodId = periodId, TotalAllocated = result.TotalAllocated, TotalAbsorbed = result.TotalAbsorbed, Variance = variance,
                CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnOverheadAllocation>().Add(rec);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<FnAllocationResultDto>("تسهیم و جذب سربار این دوره همزمان توسط کاربر دیگری انجام شد."); }
            async Task<ResponseDto<FnAllocationResultDto>> FailAsync(string message)
            {
                await _uow.Set<FnOverheadAllocation>().Where(x => x.Id == rec.Id).ExecuteDeleteAsync(ct);
                return Bad<FnAllocationResultDto>(message);
            }
            FnVoucher v1 = null, v2 = null, v3 = null;
            if (stepLines.Count > 0)
            {
                var lines = new List<AutoLine>();
                foreach (var s in stepLines)
                {
                    lines.Add(new AutoLine { Account = pooled, CostCenterId = s.To, Debit = s.Amount, Description = $"تسهیم سربار {centers[s.From].Code} به {centers[s.To].Code}" });
                    lines.Add(new AutoLine { Account = pooled, CostCenterId = s.From, Credit = s.Amount, Description = $"تسهیم سربار {centers[s.From].Code} به {centers[s.To].Code}" });
                }
                var r1 = await AddAutoVoucherAsync(entityId, end, $"تسهیم گام‌به‌گام سربار {period.Title} ({number})", "OverheadAllocation", number, lines, FnVoucherType.Auto, null, ct);
                if (r1.Error != null) return await FailAsync(r1.Error);
                v1 = r1.Voucher;
            }
            if (absorptions.Count > 0)
            {
                var lines = new List<AutoLine>();
                foreach (FnWorkOrderBase b in bases)
                {
                    FnAbsorptionDto a = absorptions.First(x => x.WorkOrderId == b.WorkOrderId && x.CostCenterCode == centers[b.CostCenterId].Code && x.Base == b.Base);
                    lines.Add(new AutoLine { Account = wip, WorkOrderId = b.WorkOrderId, CostCenterId = b.CostCenterId, Debit = a.Amount, Description = $"جذب سربار دستور کار {b.WorkOrderId}" });
                    lines.Add(new AutoLine { Account = absorbedAcc, CostCenterId = b.CostCenterId, WorkOrderId = b.WorkOrderId, Credit = a.Amount, Description = $"جذب سربار دستور کار {b.WorkOrderId}" });
                }
                var r2 = await AddAutoVoucherAsync(entityId, end, $"جذب سربار در دستورکارها {period.Title} ({number})", "OverheadAbsorption", number, lines, FnVoucherType.Auto, null, ct);
                if (r2.Error != null) return await FailAsync(r2.Error);
                v2 = r2.Voucher;
            }
            if (varianceRows.Count > 0)
            {
                var lines = new List<AutoLine>();
                foreach (var row in varianceRows)
                {
                    decimal vr = row.Actual - row.Absorbed;
                    lines.Add(new AutoLine { Account = absorbedAcc, CostCenterId = row.Cc, Debit = row.Absorbed, Description = "بستن سربار جذب‌شده" });
                    lines.Add(new AutoLine { Account = pooled, CostCenterId = row.Cc, Credit = row.Actual, Description = "بستن سربار واقعی" });
                    if (vr > 0) lines.Add(new AutoLine { Account = varianceAcc, CostCenterId = row.Cc, Debit = vr, Description = "سربار جذب‌نشده" });
                    else if (vr < 0) lines.Add(new AutoLine { Account = varianceAcc, CostCenterId = row.Cc, Credit = -vr, Description = "سربار مازاد جذب‌شده" });
                }
                var r3 = await AddAutoVoucherAsync(entityId, end, $"انحراف جذب سربار {period.Title} ({number})", "OverheadVariance", number, lines, FnVoucherType.Auto, null, ct);
                if (r3.Error != null) return await FailAsync(r3.Error);
                v3 = r3.Voucher;
            }
            foreach (FnWorkOrderBase b in bases)
            {
                FnAbsorptionDto a = absorptions.First(x => x.WorkOrderId == b.WorkOrderId && x.CostCenterCode == centers[b.CostCenterId].Code && x.Base == b.Base);
                _uow.Set<CostEntry>().Add(new CostEntry
                {
                    EntryDate = end, Kind = CostKind.Overhead, WorkOrderId = b.WorkOrderId, Quantity = b.Quantity, Amount = a.Amount, SourceType = "OverheadAbsorption", SourceId = b.Id, SourceNumber = number,
                    Description = "سربار جذب‌شده " + period.Title, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me,
                });
            }
            await _uow.SaveChangesAsync(ct);
            rec.AllocationVoucherId = v1?.Id; rec.AbsorptionVoucherId = v2?.Id; rec.VarianceVoucherId = v3?.Id;
            await _uow.SaveChangesAsync(ct);
            result.Id = rec.Id; result.Number = number; result.AllocationVoucherId = rec.AllocationVoucherId; result.AbsorptionVoucherId = rec.AbsorptionVoucherId; result.VarianceVoucherId = rec.VarianceVoucherId;
            return ResponseDtoHelper.Ok(result);
        }

        // ------------------------------------------------------------------------------------------ work-order cost sheet and close-out

        private async Task<FnWorkOrderCostDto> BuildCostAsync(WorkOrder wo, CancellationToken ct)
        {
            var kinds = await _uow.Set<CostEntry>().AsNoTracking().Where(c => c.WorkOrderId == wo.Id).GroupBy(c => c.Kind).Select(g => new { Kind = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
            decimal K(params CostKind[] k) => kinds.Where(x => k.Contains(x.Kind)).Sum(x => x.Amount);
            var wipLines = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.WorkOrderId == wo.Id && l.Account.Code == "10520" && l.Voucher.Status != FnVoucherStatus.Draft)
                .GroupBy(l => 1).Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).FirstOrDefaultAsync(ct);
            // what has left WIP as finished goods: production receipts and earlier close-outs (net of their reopenings); returns of material are NOT a transfer, the sheet material is already net of them
            List<int> receiptEvents = await _uow.Set<AccountingEvent>().AsNoTracking().Where(e => e.Type == AccountingEventType.ProductionReceipt).Select(e => e.Id).ToListAsync(ct);
            // a cancelled receipt is a reversal event of a receipt event: it takes the amount back out of "transferred"
            List<int> receiptReversals = await _uow.Set<AccountingEvent>().AsNoTracking().Where(e => e.ReversesEventId != null && receiptEvents.Contains(e.ReversesEventId.Value)).Select(e => e.Id).ToListAsync(ct);
            receiptEvents.AddRange(receiptReversals);
            decimal transferred = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.WorkOrderId == wo.Id && l.Account.Code == "10520" && l.Voucher.Status != FnVoucherStatus.Draft
                    && ((l.Voucher.AccountingEventId != null && receiptEvents.Contains(l.Voucher.AccountingEventId.Value)) || l.Voucher.SourceType == "WorkOrderClose"))
                .SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
            decimal labourLedger = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.WorkOrderId == wo.Id && l.Account.Code == "70102" && l.Voucher.Status != FnVoucherStatus.Draft).SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            bool closed = await _uow.Set<FnWorkOrderClose>().AnyAsync(c => c.WorkOrderId == wo.Id && c.IsOpen == false, ct);
            var dto = new FnWorkOrderCostDto
            {
                WorkOrderId = wo.Id, Code = wo.Code, Quantity = wo.Count, Material = K(CostKind.Material), Labour = K(CostKind.Labour), Overhead = K(CostKind.Overhead), Subcontract = K(CostKind.Subcontract, CostKind.PurchasedService),
                WipBalance = (wipLines?.Debit ?? 0) - (wipLines?.Credit ?? 0), TransferredOut = transferred, LabourInLedger = labourLedger, IsClosed = closed,
            };
            dto.TotalCost = dto.Material + dto.Labour + dto.Overhead + dto.Subcontract;
            dto.UnitCost = dto.Quantity > 0 ? R(dto.TotalCost / dto.Quantity) : dto.TotalCost;
            return dto;
        }

        public async Task<ResponseDto<FnWorkOrderCostDto>> CostSheetAsync(int workOrderId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnWorkOrderCostDto>(NotAllowed);
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, ct);
            return wo == null ? ResponseDtoHelper.NotFound<FnWorkOrderCostDto>("دستور کار پیدا نشد.") : ResponseDtoHelper.Ok(await BuildCostAsync(wo, ct));
        }

        private static FnWorkOrderCloseDto ToDto(FnWorkOrderClose c) => new FnWorkOrderCloseDto
        {
            Id = c.Id, Number = c.Number, WorkOrderId = c.WorkOrderId, Material = c.Material, Labour = c.Labour, Overhead = c.Overhead, Subcontract = c.Subcontract, TotalCost = c.TotalCost,
            Variance = c.Variance, Quantity = c.Quantity, UnitCost = c.UnitCost, IsOpen = c.IsOpen, VoucherId = c.VoucherId, ReversalVoucherId = c.ReversalVoucherId, ToFinishedGoods = c.TotalCost - c.Variance,
        };

        public async Task<ResponseDto<FnWorkOrderCloseDto>> CloseWorkOrderAsync(int workOrderId, FnWorkOrderCloseRequest request, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnWorkOrderCloseDto>(NotAllowed);
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, ct);
            if (wo == null) return ResponseDtoHelper.NotFound<FnWorkOrderCloseDto>("دستور کار پیدا نشد.");
            if (wo.Condition != WorkOrderCondition.Done) return Bad<FnWorkOrderCloseDto>("فقط دستور کار انجام‌شده بسته می‌شود.");
            if (await _uow.Set<FnContractRevenue>().AnyAsync(r => r.ContractId == wo.ContractId && r.Method == FnRevenueMethod.PercentOfCompletion, ct))
                return Bad<FnWorkOrderCloseDto>("قرارداد این دستور کار به روش درصد پیشرفت شناسایی می‌شود؛ بهای آن با اجرای شناسایی درآمد به بهای تمام‌شده منتقل می‌شود و دستور کار به محصول ساخته‌شده بسته نمی‌شود."); 
            if (await _uow.Set<FnWorkOrderClose>().AnyAsync(c => c.WorkOrderId == workOrderId && !c.IsOpen, ct)) return Bad<FnWorkOrderCloseDto>("این دستور کار قبلاً بسته شده است؛ برای تغییر باید توسط مدیر مالی بازگشایی شود.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            FnWorkOrderCostDto cost = await BuildCostAsync(wo, ct);
            int quantity = request?.Quantity ?? wo.Count;
            if (quantity <= 0) return Bad<FnWorkOrderCloseDto>("تعداد تحویلی باید بیشتر از صفر باشد.");
            DateTime date = (request?.Date ?? DateTime.Today).Date;
            FnAccount wip = await RoleAccountAsync(AccountRoles.Wip, "10520", ct), fg = await RoleAccountAsync(AccountRoles.Inventory + ".03", "10503", ct);
            FnAccount labour = await AccountByCodeAsync("70102", ct), variance = await AccountByCodeAsync(CostVarianceCode, ct);
            if (wip == null || fg == null || labour == null || variance == null) return Bad<FnWorkOrderCloseDto>("حساب‌های کالای در جریان، محصول ساخته‌شده، دستمزد یا انحراف در کدینگ نیست.");
            // finished goods are debited with what is still to be transferred according to the cost sheet; the ledger side (WIP balance and direct labour booked to the order) is credited;
            // the difference is the cost variance.
            decimal remainingSheet = cost.TotalCost - cost.TransferredOut;
            decimal ledger = cost.WipBalance + cost.LabourInLedger;
            decimal diff = ledger - remainingSheet; // positive: the ledger holds more than the sheet
            if (ledger == 0 && remainingSheet == 0) return Bad<FnWorkOrderCloseDto>("برای این دستور کار هزینه‌ای ثبت نشده است.");
            var lines = new List<AutoLine>();
            string text = $"بستن دستور کار {wo.Code}";
            if (remainingSheet != 0) lines.Add(new AutoLine { Account = fg, WorkOrderId = wo.Id, ContractId = wo.ContractId, Debit = remainingSheet > 0 ? remainingSheet : 0, Credit = remainingSheet < 0 ? -remainingSheet : 0, Description = text });
            if (cost.WipBalance != 0) lines.Add(new AutoLine { Account = wip, WorkOrderId = wo.Id, ContractId = wo.ContractId, Debit = cost.WipBalance < 0 ? -cost.WipBalance : 0, Credit = cost.WipBalance > 0 ? cost.WipBalance : 0, Description = text });
            if (cost.LabourInLedger != 0) lines.Add(new AutoLine { Account = labour, WorkOrderId = wo.Id, ContractId = wo.ContractId, Debit = cost.LabourInLedger < 0 ? -cost.LabourInLedger : 0, Credit = cost.LabourInLedger > 0 ? cost.LabourInLedger : 0, Description = text });
            if (diff != 0) lines.Add(new AutoLine { Account = variance, WorkOrderId = wo.Id, ContractId = wo.ContractId, Debit = diff > 0 ? diff : 0, Credit = diff < 0 ? -diff : 0, Description = "انحراف بهای تمام‌شده " + text });
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            string number = await NextNumberAsync("WOC", FinanceNumbers.WorkOrderClose, date, ct);
            // the close record is saved FIRST: the unique index (one active close per work order) stops a concurrent second close before any voucher exists
            var rec = new FnWorkOrderClose
            {
                Number = number, WorkOrderId = wo.Id, Material = cost.Material, Labour = cost.Labour, Overhead = cost.Overhead, Subcontract = cost.Subcontract, TotalCost = cost.TotalCost, Variance = diff,
                Quantity = quantity, UnitCost = R(cost.TotalCost / quantity), FgAmount = remainingSheet, IsOpen = false, VoucherId = 0, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnWorkOrderClose>().Add(rec);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<FnWorkOrderCloseDto>("این دستور کار همزمان توسط کاربر دیگری بسته شد."); }
            var v = await AddAutoVoucherAsync(entityId, date, $"{text} ({number})", "WorkOrderClose", number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) { await _uow.Set<FnWorkOrderClose>().Where(x => x.Id == rec.Id).ExecuteDeleteAsync(ct); return Bad<FnWorkOrderCloseDto>(v.Error); }
            await _uow.SaveChangesAsync(ct);
            rec.VoucherId = v.Voucher.Id;
            await _uow.SaveChangesAsync(ct);
            FnWorkOrderCloseDto dto = ToDto(rec);
            dto.ToFinishedGoods = remainingSheet;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FnWorkOrderCloseDto>> ReopenWorkOrderAsync(int workOrderId, FnWorkOrderCloseRequest request, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FnWorkOrderCloseDto>(NotAllowed + " (فقط مدیر مالی)");
            if (Clean(request?.Reason) == null) return Bad<FnWorkOrderCloseDto>("دلیل بازگشایی را وارد کنید.");
            FnWorkOrderClose c = await _uow.Set<FnWorkOrderClose>().FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId && !x.IsOpen, ct);
            if (c == null) return ResponseDtoHelper.NotFound<FnWorkOrderCloseDto>("بستن فعالی برای این دستور کار نیست.");
            if (c.DeliveredQuantity > 0) return Bad<FnWorkOrderCloseDto>("از این دستور کار کالا تحویل مشتری شده و بهای آن به بهای تمام‌شده منتقل شده است؛ بازگشایی ممکن نیست.");
            FnVoucher original = await _uow.Set<FnVoucher>().Include(v => v.Lines).ThenInclude(l => l.Account).Include(v => v.FiscalYear).FirstOrDefaultAsync(v => v.Id == c.VoucherId, ct);
            if (original == null) return Bad<FnWorkOrderCloseDto>("سند بستن پیدا نشد.");
            DateTime date = (request.Date ?? DateTime.Today).Date;
            var lines = original.Lines.OrderBy(l => l.RowNumber).Select(l => new AutoLine
            {
                Account = l.Account, Detail1Id = l.Detail1Id, Detail2Id = l.Detail2Id, Debit = l.Credit, Credit = l.Debit, Description = "برگشت: " + l.Description, WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, CostCenterId = l.CostCenterId,
            }).ToList();
            var v = await AddAutoVoucherAsync(original.FiscalYear.LegalEntityId, date, $"بازگشایی دستور کار ({c.Number}): {request.Reason.Trim()}", "WorkOrderClose", c.Number, lines, FnVoucherType.Reversal, original, ct);
            if (v.Error != null) return Bad<FnWorkOrderCloseDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            c.IsOpen = true; c.ReversalVoucherId = v.Voucher.Id; c.ReopenReason = request.Reason.Trim();
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(c));
        }

        // ------------------------------------------------------------------------------------------ standard cost

        public async Task<ResponseDto<List<FnStandardCostDto>>> GetStandardCostsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnStandardCostDto>>(NotAllowed);
            var rows = await _uow.Set<FnStandardCost>().AsNoTracking().OrderBy(r => r.ItemId).ThenByDescending(r => r.ValidFrom).Take(500).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(r => new FnStandardCostDto { Id = r.Id, ItemId = r.ItemId, Cost = r.Cost, ValidFrom = r.ValidFrom, Source = r.Source }).ToList());
        }

        public async Task<ResponseDto<FnStandardCostDto>> SaveStandardCostAsync(FnStandardCostDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnStandardCostDto>(NotAllowed);
            if (dto == null || dto.Cost < 0 || dto.ValidFrom == default) return Bad<FnStandardCostDto>("بهای استاندارد یا تاریخ نامعتبر است.");
            if (!await _uow.Set<Entities.Model.NasbNiro.Inventory.InventoryItem>().AnyAsync(i => i.Id == dto.ItemId, ct)) return Bad<FnStandardCostDto>("کالا پیدا نشد.");
            FnStandardCost s = await _uow.Set<FnStandardCost>().FirstOrDefaultAsync(x => x.ItemId == dto.ItemId && x.ValidFrom == dto.ValidFrom.Date, ct);
            if (s == null) { s = new FnStandardCost { ItemId = dto.ItemId, ValidFrom = dto.ValidFrom.Date }; _uow.Set<FnStandardCost>().Add(s); }
            s.Cost = R(dto.Cost); s.Source = Clean(dto.Source);
            await _uow.SaveChangesAsync(ct);
            dto.Id = s.Id; dto.Cost = s.Cost;
            return ResponseDtoHelper.Ok(dto);
        }

        // ------------------------------------------------------------------------------------------ percentage-of-completion revenue

        public async Task<ResponseDto<List<FnContractRevenueDto>>> GetContractRevenueAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnContractRevenueDto>>(NotAllowed);
            var rows = await _uow.Set<FnContractRevenue>().AsNoTracking().ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(r => new FnContractRevenueDto { ContractId = r.ContractId, Method = r.Method, ContractPrice = r.ContractPrice, ProgressSource = r.ProgressSource, ProgressMeasure = r.ProgressMeasure }).ToList());
        }

        public async Task<ResponseDto<FnContractRevenueDto>> SaveContractRevenueAsync(FnContractRevenueDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FnContractRevenueDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null) return Bad<FnContractRevenueDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId, ct)) return Bad<FnContractRevenueDto>("قرارداد پیدا نشد.");
            if (dto.ProgressSource != "ProjectControl" && dto.ProgressSource != "Manual") return Bad<FnContractRevenueDto>("منبع پیشرفت باید ProjectControl یا Manual باشد.");
            if (dto.ProgressMeasure != "Physical" && dto.ProgressMeasure != "CostToCost") return Bad<FnContractRevenueDto>("روش پیشرفت باید Physical یا CostToCost باشد.");
            if (dto.ContractPrice.HasValue && dto.ContractPrice.Value <= 0) return Bad<FnContractRevenueDto>("مبلغ قرارداد باید بیشتر از صفر باشد.");
            FnContractRevenue r = await _uow.Set<FnContractRevenue>().FirstOrDefaultAsync(x => x.ContractId == dto.ContractId, ct);
            if (r != null && r.Method == FnRevenueMethod.PercentOfCompletion && dto.Method == FnRevenueMethod.OnInvoice && await _uow.Set<FnRevenueRecognition>().AnyAsync(x => x.ContractId == dto.ContractId, ct))
                return Bad<FnContractRevenueDto>("برای قراردادی که درآمد آن به روش درصد پیشرفت شناسایی شده، روش قابل تغییر نیست.");
            if (r == null) { r = new FnContractRevenue { ContractId = dto.ContractId }; _uow.Set<FnContractRevenue>().Add(r); }
            r.Method = dto.Method; r.ContractPrice = dto.ContractPrice; r.ProgressSource = dto.ProgressSource; r.ProgressMeasure = dto.ProgressMeasure;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FnRevenueRunDto>> RevenueRecognitionAsync(int periodId, FnRevenueRunRequest request, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnRevenueRunDto>(NotAllowed);
            bool dry = request?.DryRun ?? false;
            await FinanceSeed.EnsureAsync(_uow, ct);
            FnPeriod period = await _uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.Id == periodId, ct);
            if (period == null) return ResponseDtoHelper.NotFound<FnRevenueRunDto>("دوره پیدا نشد.");
            int entityId = period.FiscalYear.LegalEntityId;
            DateTime end = period.EndDate.Date;
            var settings = await _uow.Set<FnContractRevenue>().AsNoTracking().Where(r => r.Method == FnRevenueMethod.PercentOfCompletion).ToListAsync(ct);
            FnAccount revenue = await RoleAccountAsync(AccountRoles.RevenueContracts, "60101", ct), asset = await AccountByCodeAsync("10308", ct), liability = await AccountByCodeAsync("30303", ct);
            FnAccount lossExpense = await AccountByCodeAsync("70112", ct), lossProvision = await AccountByCodeAsync("30404", ct);
            FnAccount wipAccount = await RoleAccountAsync(AccountRoles.Wip, "10520", ct), cogsAccount = await RoleAccountAsync(AccountRoles.Cogs, "70101", ct);
            if (wipAccount == null || cogsAccount == null) return Bad<FnRevenueRunDto>("حساب کالای در جریان یا بهای تمام‌شده فروش در کدینگ نیست.");
            if (revenue == null || asset == null || liability == null || lossExpense == null || lossProvision == null) return Bad<FnRevenueRunDto>("حساب‌های درآمد قرارداد، مطالبات تکمیل‌نشده (10308)، صورت‌حساب مازاد (30303) یا ذخیره زیان در کدینگ نیست.");
            var run = new FnRevenueRunDto { PeriodId = periodId, DryRun = dry };
            var lines = new List<AutoLine>();
            var recs = new List<FnRevenueRecognition>();
            string number = dry ? null : await NextNumberAsync("REV", FinanceNumbers.RevenueRecognition, end, ct);
            foreach (FnContractRevenue s in settings.OrderBy(x => x.ContractId))
            {
                FnRevenueInputDto input = request?.Inputs?.FirstOrDefault(i => i.ContractId == s.ContractId);
                decimal price = s.ContractPrice ?? await _uow.Set<Entities.Model.NasbNiro.ProjectControl.PcContractTerms>().AsNoTracking().Where(t => t.ContractId == s.ContractId).Select(t => t.ValueExVat ?? 0).FirstOrDefaultAsync(ct);
                if (price <= 0) return Bad<FnRevenueRunDto>($"مبلغ قرارداد {s.ContractId} مشخص نیست.");
                decimal progress, eac;
                var snap = await _uow.Set<Entities.Model.NasbNiro.ProjectControl.PcProgressSnapshot>().AsNoTracking().Where(x => x.ContractId == s.ContractId && x.DataDate <= end).OrderByDescending(x => x.DataDate).FirstOrDefaultAsync(ct);
                decimal snapEac = snap == null ? 0 : (snap.Cpi.HasValue && snap.Cpi.Value > 0 ? R(snap.Bac / snap.Cpi.Value) : snap.Bac);
                eac = input?.EstimatedCost ?? snapEac;
                if (s.ProgressSource == "Manual" || input?.ProgressPercent != null)
                {
                    if (input?.ProgressPercent == null) return Bad<FnRevenueRunDto>($"درصد پیشرفت قرارداد {s.ContractId} باید به‌صورت دستی وارد شود.");
                    progress = input.ProgressPercent.Value;
                }
                else
                {
                    if (snap == null) return Bad<FnRevenueRunDto>($"برای قرارداد {s.ContractId} تصویر پیشرفت کنترل پروژه تا پایان دوره نیست.");
                    progress = s.ProgressMeasure == "CostToCost" ? (eac > 0 ? Math.Round(snap.Ac * 100m / eac, 4, MidpointRounding.AwayFromZero) : 0) : snap.PhysicalPercent;
                }
                if (progress < 0 || progress > 100) return Bad<FnRevenueRunDto>($"درصد پیشرفت قرارداد {s.ContractId} باید بین ۰ و ۱۰۰ باشد.");
                decimal earned = R(price * progress / 100m);
                var ledger = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.ContractId == s.ContractId && l.Voucher.Date <= end && l.Voucher.Status != FnVoucherStatus.Draft && l.Voucher.FiscalYear.LegalEntityId == entityId
                        && (l.Account.Code == revenue.Code || l.Account.Code == "10308" || l.Account.Code == "30303" || l.Account.Code == "30404" || l.Account.Code == wipAccount.Code))
                    .GroupBy(l => l.Account.Code).Select(g => new { Code = g.Key, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);
                decimal Net(string code) => ledger.Where(x => x.Code == code).Sum(x => x.Net);
                // the cost incurred on the contract (still in work in progress) is expensed with the revenue, so the margin follows the progress
                List<int> contractWos = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.ContractId == s.ContractId).Select(w => w.Id).ToListAsync(ct);
                var wipByWo = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.Account.Code == wipAccount.Code && l.Voucher.Date <= end && l.Voucher.Status != FnVoucherStatus.Draft && l.Voucher.FiscalYear.LegalEntityId == entityId
                        && (l.ContractId == s.ContractId || (l.WorkOrderId != null && contractWos.Contains(l.WorkOrderId.Value))))
                    .GroupBy(l => l.WorkOrderId).Select(g => new { Wo = g.Key, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);
                decimal costThisRun = wipByWo.Sum(x => x.Net);
                decimal revenueLedger = -Net(revenue.Code), oldAsset = Net("10308"), oldLiability = -Net("30303"), oldProvision = -Net("30404");
                decimal adjust = earned - revenueLedger;
                decimal position = (oldAsset - oldLiability) + adjust;
                decimal newAsset = position > 0 ? position : 0, newLiability = position < 0 ? -position : 0;
                decimal expectedLoss = eac > price ? eac - price : 0;
                decimal provision = R(expectedLoss * (100m - progress) / 100m);
                decimal provisionChange = provision - oldProvision;
                var row = new FnRevenueRowDto
                {
                    ContractId = s.ContractId, ProgressPercent = progress, ContractPrice = price, EstimatedCost = eac, EarnedToDate = earned, RevenueInLedger = revenueLedger, RevenueThisRun = adjust,
                    ContractAsset = newAsset, ContractLiability = newLiability, LossProvision = provision, LossProvisionChange = provisionChange, CostOfSalesThisRun = costThisRun,
                };
                run.Rows.Add(row);
                run.TotalRevenue += adjust; run.TotalCostOfSales += costThisRun;
                string text = $"شناسایی درآمد قرارداد {s.ContractId} به روش درصد پیشرفت ({progress:0.##}٪)";
                if (adjust != 0) lines.Add(new AutoLine { Account = revenue, ContractId = s.ContractId, Debit = adjust < 0 ? -adjust : 0, Credit = adjust > 0 ? adjust : 0, Description = text });
                decimal dAsset = newAsset - oldAsset, dLiab = newLiability - oldLiability;
                if (dAsset != 0) lines.Add(new AutoLine { Account = asset, ContractId = s.ContractId, Debit = dAsset > 0 ? dAsset : 0, Credit = dAsset < 0 ? -dAsset : 0, Description = text });
                if (dLiab != 0) lines.Add(new AutoLine { Account = liability, ContractId = s.ContractId, Credit = dLiab > 0 ? dLiab : 0, Debit = dLiab < 0 ? -dLiab : 0, Description = text });
                if (costThisRun != 0)
                {
                    lines.Add(new AutoLine { Account = cogsAccount, ContractId = s.ContractId, Debit = costThisRun > 0 ? costThisRun : 0, Credit = costThisRun < 0 ? -costThisRun : 0, Description = $"بهای تمام‌شده شناسایی‌شده قرارداد {s.ContractId} ({progress:0.##}٪)" });
                    // the work in progress leaves per work order, so the balance of every work order of the contract is zero afterwards (nothing is left to be closed out and expensed a second time)
                    foreach (var w in wipByWo.Where(x => x.Net != 0))
                        lines.Add(new AutoLine { Account = wipAccount, WorkOrderId = w.Wo, ContractId = s.ContractId, Credit = w.Net > 0 ? w.Net : 0, Debit = w.Net < 0 ? -w.Net : 0, Description = $"انتقال هزینه انجام‌شده قرارداد {s.ContractId} از کالای در جریان" });
                }
                if (provisionChange != 0)
                {
                    lines.Add(new AutoLine { Account = lossExpense, ContractId = s.ContractId, Debit = provisionChange > 0 ? provisionChange : 0, Credit = provisionChange < 0 ? -provisionChange : 0, Description = "ذخیره زیان مورد انتظار قرارداد " + s.ContractId });
                    lines.Add(new AutoLine { Account = lossProvision, ContractId = s.ContractId, Credit = provisionChange > 0 ? provisionChange : 0, Debit = provisionChange < 0 ? -provisionChange : 0, Description = "ذخیره زیان مورد انتظار قرارداد " + s.ContractId });
                }
                recs.Add(new FnRevenueRecognition
                {
                    Number = number, LegalEntityId = entityId, PeriodId = periodId, ContractId = s.ContractId, ProgressPercent = progress, ContractPrice = price, EstimatedCost = eac, EarnedToDate = earned,
                    RecognisedBefore = revenueLedger, RevenueThisRun = adjust, BilledToDate = revenueLedger - 0, ContractAsset = newAsset, ContractLiability = newLiability, LossProvision = provision, CostOfSales = costThisRun,
                    CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
                });
            }
            if (dry) return ResponseDtoHelper.Ok(run);
            if (lines.Count == 0) { run.Number = null; return ResponseDtoHelper.Ok(run); }
            var v = await AddAutoVoucherAsync(entityId, end, $"شناسایی درآمد قراردادها به روش درصد پیشرفت {period.Title} ({number})", "RevenueRecognition", number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) return Bad<FnRevenueRunDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            foreach (FnRevenueRecognition r in recs) { r.VoucherId = v.Voucher.Id; _uow.Set<FnRevenueRecognition>().Add(r); }
            await _uow.SaveChangesAsync(ct);
            run.Number = number; run.VoucherId = v.Voucher.Id;
            return ResponseDtoHelper.Ok(run);
        }
    }
}
