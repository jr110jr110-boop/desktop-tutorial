using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Sales
{
    /// <summary>The inputs of an estimate calculation (all percentages are inputs; nothing is assumed).</summary>
    internal class SlEstimateInputs
    {
        public decimal Material, Labour, Engineering, Subcontract, PackingTransport;
        public decimal? OverheadPercent, ContingencyPercent, TargetMarginPercent;
        public decimal? GuaranteeAmount, GuaranteeMonths, GuaranteeRatePercentPerMonth;
        public decimal? FinancingAmount, FinancingMonths, FinancingRatePercentPerMonth;
        public List<decimal> RiskExpectedCosts = new List<decimal>();
        public int MissingCostLines;
    }

    internal class SlEstimateTotals
    {
        public decimal DirectCost, GuaranteeCost, FinancingCost, RiskCost, ContingencyCost, CostBeforeOverhead, OverheadCost, TotalCost;
        public decimal? Price;
        public List<string> Missing = new List<string>();
    }

    /// <summary>
    /// Pure arithmetic of the estimate, in rials rounded to two decimals at each step so that the parts add up to the total exactly.
    /// Direct = material + labour + engineering + subcontract + packing/transport.
    /// Guarantee = amount x rate%/month x months; financing the same way; risk = sum of impact x probability%.
    /// Contingency = direct x contingency%. Overhead = (direct + guarantee + financing + risk + contingency) x overhead%.
    /// Total cost = all of the above. Price = total cost / (1 - margin%): the margin is a share of the selling price.
    /// </summary>
    internal static class SlEstimateMath
    {
        public static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        public static SlEstimateTotals Compute(SlEstimateInputs i)
        {
            var t = new SlEstimateTotals();
            t.DirectCost = R2(i.Material + i.Labour + i.Engineering + i.Subcontract + i.PackingTransport);
            t.GuaranteeCost = Carry("ضمانت", i.GuaranteeAmount, i.GuaranteeMonths, i.GuaranteeRatePercentPerMonth, t.Missing);
            t.FinancingCost = Carry("تأمین مالی", i.FinancingAmount, i.FinancingMonths, i.FinancingRatePercentPerMonth, t.Missing);
            t.RiskCost = R2(i.RiskExpectedCosts.Sum());
            if (i.ContingencyPercent.HasValue) t.ContingencyCost = R2(t.DirectCost * i.ContingencyPercent.Value / 100m);
            else t.Missing.Add("درصد پیش‌بینی نشده (contingency)");
            t.CostBeforeOverhead = R2(t.DirectCost + t.GuaranteeCost + t.FinancingCost + t.RiskCost + t.ContingencyCost);
            if (i.OverheadPercent.HasValue) t.OverheadCost = R2(t.CostBeforeOverhead * i.OverheadPercent.Value / 100m);
            else t.Missing.Add("درصد سربار");
            t.TotalCost = R2(t.CostBeforeOverhead + t.OverheadCost);
            if (!i.TargetMarginPercent.HasValue) t.Missing.Add("حاشیه سود هدف");
            if (i.MissingCostLines > 0) t.Missing.Add($"هزینه واحد {i.MissingCostLines} ردیف مشخص نیست");
            if (t.Missing.Count == 0 && i.TargetMarginPercent.Value < 100m) t.Price = R2(t.TotalCost / (1m - i.TargetMarginPercent.Value / 100m));
            return t;
        }

        private static decimal Carry(string name, decimal? amount, decimal? months, decimal? ratePerMonth, List<string> missing)
        {
            if (!amount.HasValue || amount.Value == 0m) return 0m;
            bool ok = true;
            if (!months.HasValue) { missing.Add($"مدت {name}"); ok = false; }
            if (!ratePerMonth.HasValue) { missing.Add($"نرخ ماهانه {name} (درصد)"); ok = false; }
            return ok ? R2(amount.Value * ratePerMonth.Value / 100m * months.Value) : 0m;
        }
    }

    /// <summary>Unit cost of items from the books: average cost, last purchase price, last purchase order. Used by estimates and cost-plus price suggestions.</summary>
    internal static class SlCosts
    {
        public static async Task<Dictionary<int, (decimal Cost, SlCostSource Source)>> ItemCostsAsync(IUnitOfWork uow, IEnumerable<int> itemIds, bool allowAverage, CancellationToken ct)
        {
            List<int> ids = itemIds.Distinct().ToList();
            var result = new Dictionary<int, (decimal, SlCostSource)>();
            if (ids.Count == 0) return result;
            List<ItemCost> costs = await uow.Set<ItemCost>().AsNoTracking().Where(c => ids.Contains(c.ItemId)).ToListAsync(ct);
            foreach (ItemCost c in costs)
            {
                if (allowAverage && c.Quantity > 0 && c.AverageCost > 0) result[c.ItemId] = (c.AverageCost, SlCostSource.AverageCost);
                else if (c.LastPurchasePrice.HasValue && c.LastPurchasePrice.Value > 0) result[c.ItemId] = (c.LastPurchasePrice.Value, SlCostSource.LastPurchase);
            }
            List<int> rest = ids.Where(id => !result.ContainsKey(id)).ToList();
            if (rest.Count > 0)
            {
                var lines = await uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.ItemId != null && rest.Contains(l.ItemId.Value) && l.UnitPrice > 0 && !l.PurchaseOrder.Deleted)
                    .Select(l => new { ItemId = l.ItemId.Value, l.UnitPrice, l.PurchaseOrder.OrderDate, l.Id }).ToListAsync(ct);
                foreach (var g in lines.GroupBy(l => l.ItemId))
                {
                    var last = g.OrderByDescending(l => l.OrderDate).ThenByDescending(l => l.Id).First();
                    result[g.Key] = (last.UnitPrice, SlCostSource.LastPurchaseOrder);
                }
            }
            return result;
        }

        /// <summary>Hourly cost of a work centre: labour rate plus machine rate (whichever finance entered). Null when finance entered none.</summary>
        public static decimal? HourlyRate(MfWorkCenter wc)
        {
            if (wc == null || (!wc.LabourRateRial.HasValue && !wc.MachineRateRial.HasValue)) return null;
            return (wc.LabourRateRial ?? 0m) + (wc.MachineRateRial ?? 0m);
        }
    }

    public interface ISlEstimateService
    {
        Task<ResponseDto<PagedList<SlEstimateDto>>> GetEstimatesAsync(SlEstimateQuery query, CancellationToken ct);
        Task<ResponseDto<SlEstimateDto>> GetEstimateAsync(int id, CancellationToken ct);
        Task<ResponseDto<SlEstimateDto>> SaveEstimateAsync(SlSaveEstimateDto dto, CancellationToken ct);
        Task<ResponseDto<SlEstimateDto>> FromEquipmentAsync(SlEstimateFromEquipmentDto dto, CancellationToken ct);
        Task<ResponseDto<SlEstimateDto>> EstimateActionAsync(SlEstimateActionDto dto, CancellationToken ct);
    }

    /// <summary>Cost estimates of inquiries: revisions, lines drafted from equipment templates, labour from routing standards and work-centre rates, risks, margin to price.</summary>
    public class SlEstimateService : ISlEstimateService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public SlEstimateService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        // ================================================================================== read

        private async Task<List<SlEstimateDto>> DtosAsync(List<SlEstimate> rows, CancellationToken ct)
        {
            List<int> inquiryIds = rows.Select(r => r.InquiryId).Distinct().ToList();
            Dictionary<int, string> inquiries = await _uow.Set<SlInquiry>().AsNoTracking().Where(i => inquiryIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
            List<int> equipmentIds = rows.SelectMany(r => r.Equipments.Select(e => e.EquipmentId)).Distinct().ToList();
            Dictionary<int, string> equipments = equipmentIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<Equipment>().AsNoTracking().Where(e => equipmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Title, ct);
            return rows.Select(r => new SlEstimateDto
            {
                Id = r.Id, Number = r.Number, InquiryId = r.InquiryId, InquiryNumber = inquiries.TryGetValue(r.InquiryId, out string n) ? n : null, Revision = r.Revision, Status = r.Status, Note = r.Note,
                OverheadPercent = r.OverheadPercent, ContingencyPercent = r.ContingencyPercent, TargetMarginPercent = r.TargetMarginPercent, GuaranteeAmount = r.GuaranteeAmount,
                GuaranteeMonths = r.GuaranteeMonths, GuaranteeRatePercentPerMonth = r.GuaranteeRatePercentPerMonth, FinancingAmount = r.FinancingAmount, FinancingMonths = r.FinancingMonths,
                FinancingRatePercentPerMonth = r.FinancingRatePercentPerMonth, MaterialCost = r.MaterialCost, LabourCost = r.LabourCost, EngineeringCost = r.EngineeringCost,
                SubcontractCost = r.SubcontractCost, PackingTransportCost = r.PackingTransportCost, DirectCost = r.DirectCost, GuaranteeCost = r.GuaranteeCost, FinancingCost = r.FinancingCost,
                RiskCost = r.RiskCost, ContingencyCost = r.ContingencyCost, CostBeforeOverhead = r.CostBeforeOverhead, OverheadCost = r.OverheadCost, TotalCost = r.TotalCost, Price = r.Price,
                MissingCostLines = r.MissingCostLines, MissingInputs = r.MissingInputs, FinalizedAt = r.FinalizedAt, FinalizedAtText = SlText.Stamp(r.FinalizedAt),
                Equipments = r.Equipments.OrderBy(e => e.Id).Select(e => new SlEstimateEquipmentDto { EquipmentId = e.EquipmentId, EquipmentName = equipments.TryGetValue(e.EquipmentId, out string en) ? en : null, Count = e.Count }).ToList(),
                Lines = r.Lines.OrderBy(l => l.RowNumber).Select(l => new SlEstimateLineDto
                {
                    Id = l.Id, RowNumber = l.RowNumber, Kind = l.Kind, Description = l.Description, EquipmentId = l.EquipmentId, EquipmentBomId = l.EquipmentBomId, ItemId = l.ItemId,
                    WorkCenterId = l.WorkCenterId, Unit = l.Unit, Quantity = l.Quantity, UnitCost = l.UnitCost, CostSource = l.CostSource, Amount = l.Amount, Note = l.Note,
                }).ToList(),
                Risks = r.Risks.OrderBy(x => x.Id).Select(x => new SlEstimateRiskDto { Id = x.Id, Title = x.Title, ProbabilityPercent = x.ProbabilityPercent, ImpactAmount = x.ImpactAmount, ExpectedCost = x.ExpectedCost }).ToList(),
                RowVersion = SlConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        private IQueryable<SlEstimate> Full() => _uow.Set<SlEstimate>().Include(e => e.Equipments).Include(e => e.Lines).Include(e => e.Risks).AsSplitQuery();

        public async Task<ResponseDto<PagedList<SlEstimateDto>>> GetEstimatesAsync(SlEstimateQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.CommercialReaders)) return Bad<PagedList<SlEstimateDto>>(SlText.NotAllowed);
            query ??= new SlEstimateQuery();
            IQueryable<SlEstimate> q = _uow.Set<SlEstimate>().AsNoTracking().Where(e => !e.Deleted);
            if (query.InquiryId.HasValue) q = q.Where(e => e.InquiryId == query.InquiryId.Value);
            if (query.Status.HasValue) q = q.Where(e => e.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(e => e.Number.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(e => e.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<SlEstimate> full = await Full().AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
                return await DtosAsync(ids.Select(id => full.First(f => f.Id == id)).ToList(), ct);
            }, ct));
        }

        public async Task<ResponseDto<SlEstimateDto>> GetEstimateAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.CommercialReaders)) return Bad<SlEstimateDto>(SlText.NotAllowed);
            SlEstimate row = await Full().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && !e.Deleted, ct);
            if (row == null) return SlText.NotFound<SlEstimateDto>("برآورد پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<SlEstimate> { row }, ct))[0]);
        }

        // ================================================================================== calculation

        private static string Validate(decimal? overhead, decimal? contingency, decimal? margin, decimal? gAmt, decimal? gMonths, decimal? gRate, decimal? fAmt, decimal? fMonths, decimal? fRate)
        {
            if ((overhead ?? 0) < 0 || (overhead ?? 0) > 1000) return "درصد سربار نامعتبر است.";
            if ((contingency ?? 0) < 0 || (contingency ?? 0) > 1000) return "درصد پیش‌بینی نشده نامعتبر است.";
            if (margin.HasValue && (margin.Value < 0 || margin.Value >= 100)) return "حاشیه سود هدف باید از صفر تا کمتر از ۱۰۰ درصد باشد.";
            if ((gAmt ?? 0) < 0 || (gMonths ?? 0) < 0 || (gRate ?? 0) < 0 || (gRate ?? 0) > 100) return "اطلاعات هزینه ضمانت نامعتبر است.";
            if ((fAmt ?? 0) < 0 || (fMonths ?? 0) < 0 || (fRate ?? 0) < 0 || (fRate ?? 0) > 100) return "اطلاعات هزینه تأمین مالی نامعتبر است.";
            return null;
        }

        /// <summary>Recomputes the stored results of an estimate from its lines, risks and inputs.</summary>
        internal static void Recalculate(SlEstimate e)
        {
            foreach (SlEstimateLine l in e.Lines) l.Amount = l.UnitCost.HasValue ? SlEstimateMath.R2(l.Quantity * l.UnitCost.Value) : 0m;
            foreach (SlEstimateRisk r in e.Risks) r.ExpectedCost = SlEstimateMath.R2(r.ImpactAmount * r.ProbabilityPercent / 100m);
            decimal Sum(SlCostKind k) => e.Lines.Where(l => l.Kind == k).Sum(l => l.Amount);
            var input = new SlEstimateInputs
            {
                Material = Sum(SlCostKind.Material), Labour = Sum(SlCostKind.Labour), Engineering = Sum(SlCostKind.Engineering), Subcontract = Sum(SlCostKind.Subcontract),
                PackingTransport = Sum(SlCostKind.PackingTransport), OverheadPercent = e.OverheadPercent, ContingencyPercent = e.ContingencyPercent, TargetMarginPercent = e.TargetMarginPercent,
                GuaranteeAmount = e.GuaranteeAmount, GuaranteeMonths = e.GuaranteeMonths, GuaranteeRatePercentPerMonth = e.GuaranteeRatePercentPerMonth,
                FinancingAmount = e.FinancingAmount, FinancingMonths = e.FinancingMonths, FinancingRatePercentPerMonth = e.FinancingRatePercentPerMonth,
                RiskExpectedCosts = e.Risks.Select(r => r.ExpectedCost).ToList(), MissingCostLines = e.Lines.Count(l => !l.UnitCost.HasValue),
            };
            SlEstimateTotals t = SlEstimateMath.Compute(input);
            e.MaterialCost = SlEstimateMath.R2(input.Material); e.LabourCost = SlEstimateMath.R2(input.Labour); e.EngineeringCost = SlEstimateMath.R2(input.Engineering);
            e.SubcontractCost = SlEstimateMath.R2(input.Subcontract); e.PackingTransportCost = SlEstimateMath.R2(input.PackingTransport);
            e.DirectCost = t.DirectCost; e.GuaranteeCost = t.GuaranteeCost; e.FinancingCost = t.FinancingCost; e.RiskCost = t.RiskCost; e.ContingencyCost = t.ContingencyCost;
            e.CostBeforeOverhead = t.CostBeforeOverhead; e.OverheadCost = t.OverheadCost; e.TotalCost = t.TotalCost; e.Price = t.Price;
            e.MissingCostLines = input.MissingCostLines;
            e.MissingInputs = t.Missing.Count == 0 ? null : string.Join("؛ ", t.Missing);
        }

        // ================================================================================== save

        private async Task<string> CheckInquiryAsync(int inquiryId, CancellationToken ct)
        {
            SlInquiry inquiry = await _uow.Set<SlInquiry>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == inquiryId && !i.Deleted, ct);
            if (inquiry == null) return "استعلام پیدا نشد.";
            if (inquiry.Status != SlInquiryStatus.Bidding && inquiry.Status != SlInquiryStatus.Submitted)
                return $"برآورد فقط برای استعلامی ثبت می‌شود که مدیرعامل شرکت در آن را تأیید کرده است؛ وضعیت فعلی: {SlText.InquiryStatus(inquiry.Status)}.";
            return null;
        }

        public async Task<ResponseDto<SlEstimateDto>> SaveEstimateAsync(SlSaveEstimateDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlEstimateDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlEstimateDto>(SlText.InvalidData);
            string problem = Validate(dto.OverheadPercent, dto.ContingencyPercent, dto.TargetMarginPercent, dto.GuaranteeAmount, dto.GuaranteeMonths, dto.GuaranteeRatePercentPerMonth,
                dto.FinancingAmount, dto.FinancingMonths, dto.FinancingRatePercentPerMonth);
            if (problem != null) return Bad<SlEstimateDto>(problem);
            dto.Lines ??= new List<SlSaveEstimateLineDto>();
            dto.Risks ??= new List<SlSaveEstimateRiskDto>();
            foreach (SlSaveEstimateLineDto l in dto.Lines)
            {
                if (!Enum.IsDefined(typeof(SlCostKind), l.Kind) || Clean(l.Description) == null) return Bad<SlEstimateDto>("برای هر ردیف برآورد، نوع و شرح را وارد کنید.");
                if (l.Quantity < 0 || (l.UnitCost ?? 0) < 0) return Bad<SlEstimateDto>("مقدار و هزینه واحد نمی‌تواند منفی باشد.");
            }
            foreach (SlSaveEstimateRiskDto r in dto.Risks)
            {
                if (Clean(r.Title) == null) return Bad<SlEstimateDto>("عنوان هر ریسک را وارد کنید.");
                if (r.ProbabilityPercent < 0 || r.ProbabilityPercent > 100 || r.ImpactAmount < 0) return Bad<SlEstimateDto>("احتمال ریسک بین ۰ تا ۱۰۰ درصد و اثر آن غیرمنفی است.");
            }
            string inquiryProblem = await CheckInquiryAsync(dto.InquiryId, ct);
            if (inquiryProblem != null) return Bad<SlEstimateDto>(inquiryProblem);

            DateTimeOffset now = DateTimeOffset.Now;
            SlEstimate row;
            if (dto.Id.HasValue)
            {
                row = await Full().FirstOrDefaultAsync(e => e.Id == dto.Id.Value && !e.Deleted, ct);
                if (row == null) return SlText.NotFound<SlEstimateDto>("برآورد پیدا نشد.");
                if (row.InquiryId != dto.InquiryId) return Bad<SlEstimateDto>("استعلام برآورد تغییر نمی‌کند.");
                if (row.Status != SlEstimateStatus.Draft) return Bad<SlEstimateDto>("برآورد نهایی‌شده ویرایش نمی‌شود؛ بازنگری جدید بسازید.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlEstimateDto>(SlText.InvalidData);
            }
            else
            {
                if (await _uow.Set<SlEstimate>().AnyAsync(e => e.InquiryId == dto.InquiryId && !e.Deleted && e.Status != SlEstimateStatus.Superseded, ct))
                    return Bad<SlEstimateDto>("برای این استعلام برآورد فعال وجود دارد؛ آن را ویرایش یا از آن بازنگری بسازید.");
                row = await NewEstimateAsync(dto.InquiryId, 1, now, ct);
            }
            row.Note = Clean(dto.Note); row.OverheadPercent = dto.OverheadPercent; row.ContingencyPercent = dto.ContingencyPercent; row.TargetMarginPercent = dto.TargetMarginPercent;
            row.GuaranteeAmount = dto.GuaranteeAmount; row.GuaranteeMonths = dto.GuaranteeMonths; row.GuaranteeRatePercentPerMonth = dto.GuaranteeRatePercentPerMonth;
            row.FinancingAmount = dto.FinancingAmount; row.FinancingMonths = dto.FinancingMonths; row.FinancingRatePercentPerMonth = dto.FinancingRatePercentPerMonth;
            _uow.RemoveRange(row.Lines.ToList());
            _uow.RemoveRange(row.Risks.ToList());
            row.Lines.Clear();
            row.Risks.Clear();
            await AddLinesAsync(row, dto.Lines, ct);
            foreach (SlSaveEstimateRiskDto r in dto.Risks) row.Risks.Add(new SlEstimateRisk { Title = r.Title.Trim(), ProbabilityPercent = r.ProbabilityPercent, ImpactAmount = r.ImpactAmount });
            Recalculate(row);
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlEstimateDto>(SlText.Concurrency);
            }
            return await GetEstimateAsync(row.Id, ct);
        }

        private async Task<SlEstimate> NewEstimateAsync(int inquiryId, int revision, DateTimeOffset now, CancellationToken ct)
        {
            string number = await _uow.Set<SlInquiry>().AsNoTracking().Where(i => i.Id == inquiryId).Select(i => i.Number).FirstAsync(ct);
            var row = new SlEstimate { InquiryId = inquiryId, Revision = revision, Number = $"{number}/E{revision}", Status = SlEstimateStatus.Draft, CreateDate = now, CreatorUserId = Me };
            _uow.Set<SlEstimate>().Add(row);
            return row;
        }

        /// <summary>Adds the lines; a line without a unit cost gets it from the books (item cost, work-centre rate) when it can, else it stays missing.</summary>
        private async Task AddLinesAsync(SlEstimate row, List<SlSaveEstimateLineDto> lines, CancellationToken ct)
        {
            List<int> itemIds = lines.Where(l => !l.UnitCost.HasValue && l.ItemId.HasValue && l.Kind != SlCostKind.Labour).Select(l => l.ItemId.Value).ToList();
            Dictionary<int, (decimal Cost, SlCostSource Source)> itemCosts = await SlCosts.ItemCostsAsync(_uow, itemIds, true, ct);
            List<int> centerIds = lines.Where(l => !l.UnitCost.HasValue && l.WorkCenterId.HasValue).Select(l => l.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, MfWorkCenter> centers = centerIds.Count == 0 ? new Dictionary<int, MfWorkCenter>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            int n = 0;
            foreach (SlSaveEstimateLineDto l in lines)
            {
                decimal? cost = l.UnitCost;
                SlCostSource source = SlCostSource.Manual;
                if (!cost.HasValue)
                {
                    if (l.WorkCenterId.HasValue && centers.TryGetValue(l.WorkCenterId.Value, out MfWorkCenter wc) && SlCosts.HourlyRate(wc) is decimal rate) { cost = rate; source = SlCostSource.WorkCenterRate; }
                    else if (l.ItemId.HasValue && itemCosts.TryGetValue(l.ItemId.Value, out var ic)) { cost = ic.Cost; source = ic.Source; }
                }
                row.Lines.Add(new SlEstimateLine
                {
                    RowNumber = ++n, Kind = l.Kind, Description = l.Description.Trim(), EquipmentId = l.EquipmentId, EquipmentBomId = l.EquipmentBomId, ItemId = l.ItemId, WorkCenterId = l.WorkCenterId,
                    Unit = Clean(l.Unit), Quantity = l.Quantity, UnitCost = cost, CostSource = source, Note = Clean(l.Note),
                });
            }
        }

        // ================================================================================== from equipment templates

        public async Task<ResponseDto<SlEstimateDto>> FromEquipmentAsync(SlEstimateFromEquipmentDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlEstimateDto>(SlText.NotAllowed);
            if (dto == null || dto.Equipments == null || dto.Equipments.Count == 0) return Bad<SlEstimateDto>("حداقل یک تجهیز (قالب) انتخاب کنید.");
            if (dto.Equipments.Any(e => e.Count <= 0)) return Bad<SlEstimateDto>("تعداد هر تجهیز باید بیشتر از صفر باشد.");
            if (dto.Equipments.GroupBy(e => e.EquipmentId).Any(g => g.Count() > 1)) return Bad<SlEstimateDto>("هر تجهیز فقط یک بار انتخاب می‌شود.");
            string problem = Validate(dto.OverheadPercent, dto.ContingencyPercent, dto.TargetMarginPercent, null, null, null, null, null, null);
            if (problem != null) return Bad<SlEstimateDto>(problem);
            string inquiryProblem = await CheckInquiryAsync(dto.InquiryId, ct);
            if (inquiryProblem != null) return Bad<SlEstimateDto>(inquiryProblem);
            if (await _uow.Set<SlEstimate>().AnyAsync(e => e.InquiryId == dto.InquiryId && !e.Deleted && e.Status != SlEstimateStatus.Superseded, ct))
                return Bad<SlEstimateDto>("برای این استعلام برآورد فعال وجود دارد؛ آن را ویرایش یا از آن بازنگری بسازید.");

            List<int> equipmentIds = dto.Equipments.Select(e => e.EquipmentId).ToList();
            List<Equipment> equipments = await _uow.Set<Equipment>().AsNoTracking().Where(e => equipmentIds.Contains(e.Id) && !e.Deleted).ToListAsync(ct);
            if (equipments.Count != equipmentIds.Count) return Bad<SlEstimateDto>("تجهیز انتخاب‌شده پیدا نشد.");
            List<EquipmentAssembly> assemblies = await _uow.Set<EquipmentAssembly>().AsNoTracking().Where(a => equipmentIds.Contains(a.EquipmentId) && !a.Deleted).OrderBy(a => a.Id).ToListAsync(ct);
            List<int> assemblyIds = assemblies.Select(a => a.Id).ToList();
            List<EquipmentBom> boms = await _uow.Set<EquipmentBom>().AsNoTracking().Where(b => assemblyIds.Contains(b.EquipmentAssemblyId) && !b.Deleted).OrderBy(b => b.EquipmentAssemblyId).ThenBy(b => b.Sort).ThenBy(b => b.Id).ToListAsync(ct);
            List<int> bomIds = boms.Select(b => b.Id).ToList();
            List<EquipmentProcess> processes = await _uow.Set<EquipmentProcess>().AsNoTracking().Where(p => bomIds.Contains(p.EquipmentBomId) && !p.Deleted).OrderBy(p => p.Sort).ThenBy(p => p.Id).ToListAsync(ct);

            // the stock item of a BOM line: the item whose code is the exit commodity code of one of its processes
            List<string> codes = processes.Select(p => p.ExitCommodityCode).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
            Dictionary<string, InventoryItem> items = codes.Count == 0 ? new Dictionary<string, InventoryItem>()
                : (await _uow.Set<InventoryItem>().AsNoTracking().Where(i => codes.Contains(i.Code) && !i.Deleted).ToListAsync(ct)).GroupBy(i => i.Code).ToDictionary(g => g.Key, g => g.OrderBy(i => i.Id).First());

            var resolved = new Dictionary<int, InventoryItem>();
            foreach (EquipmentBom b in boms)
            {
                string code = processes.Where(p => p.EquipmentBomId == b.Id).Select(p => p.ExitCommodityCode?.Trim()).LastOrDefault(c => !string.IsNullOrEmpty(c) && items.ContainsKey(c));
                if (code != null) resolved[b.Id] = items[code];
            }

            // routing of the in-house items
            var routings = new Dictionary<int, (List<OperationTemplate> Steps, Dictionary<int, MfOperationStandard> Standards)>();
            foreach (InventoryItem item in resolved.Values.GroupBy(i => i.Id).Select(g => g.First()))
            {
                MfResolvedRouting r = await MfRouting.KeyForItemAsync(_uow, item, ct);
                if (r.Steps.Count > 0) routings[item.Id] = (r.Steps.OrderBy(s => s.Sequence).ThenBy(s => s.Id).ToList(), await MfRouting.StandardsAsync(_uow, r.Steps.Select(s => s.Id), ct));
            }
            Dictionary<int, (decimal Cost, SlCostSource Source)> bought = await SlCosts.ItemCostsAsync(_uow, resolved.Values.Where(i => !routings.ContainsKey(i.Id)).Select(i => i.Id), true, ct);
            // an in-house item is costed by its routing, so its stock (production) cost is not used again as a material price
            Dictionary<int, (decimal Cost, SlCostSource Source)> purchasedOnly = await SlCosts.ItemCostsAsync(_uow, resolved.Values.Where(i => routings.ContainsKey(i.Id)).Select(i => i.Id), false, ct);
            List<int> centerIds = routings.Values.SelectMany(r => r.Standards.Values).Where(s => s.WorkCenterId.HasValue).Select(s => s.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, MfWorkCenter> centers = centerIds.Count == 0 ? new Dictionary<int, MfWorkCenter>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

            DateTimeOffset now = DateTimeOffset.Now;
            SlEstimate row = await NewEstimateAsync(dto.InquiryId, 1, now, ct);
            row.OverheadPercent = dto.OverheadPercent; row.ContingencyPercent = dto.ContingencyPercent; row.TargetMarginPercent = dto.TargetMarginPercent;
            int n = 0;
            foreach (SlEstimateEquipmentInputDto input in dto.Equipments)
            {
                row.Equipments.Add(new SlEstimateEquipment { EquipmentId = input.EquipmentId, Count = input.Count });
                foreach (EquipmentAssembly a in assemblies.Where(x => x.EquipmentId == input.EquipmentId))
                {
                    foreach (EquipmentBom b in boms.Where(x => x.EquipmentAssemblyId == a.Id))
                    {
                        decimal quantity = b.Count * (decimal)input.Count;
                        resolved.TryGetValue(b.Id, out InventoryItem item);
                        decimal? cost = null;
                        SlCostSource source = SlCostSource.Manual;
                        if (item != null)
                        {
                            var map = routings.ContainsKey(item.Id) ? purchasedOnly : bought;
                            if (map.TryGetValue(item.Id, out var found)) { cost = found.Cost; source = found.Source; }
                        }
                        row.Lines.Add(new SlEstimateLine
                        {
                            RowNumber = ++n, Kind = SlCostKind.Material, Description = item != null ? $"{item.Code} {b.Title}".Trim() : b.Title, EquipmentId = input.EquipmentId, EquipmentBomId = b.Id,
                            ItemId = item?.Id, Unit = Clean(b.Unit) ?? item?.Unit, Quantity = quantity, UnitCost = cost, CostSource = source,
                            Note = item == null ? "کالایی با کد خروجی فرایند این ردیف در انبار پیدا نشد؛ هزینه واحد را وارد کنید." : (routings.ContainsKey(item.Id) && cost == null ? "کالای ساخت داخل است؛ هزینه ماده اولیه را وارد کنید." : null),
                        });
                        if (item == null || !routings.TryGetValue(item.Id, out var routing)) continue;
                        foreach (OperationTemplate step in routing.Steps)
                        {
                            routing.Standards.TryGetValue(step.Id, out MfOperationStandard std);
                            string title = $"{item.Code} - {step.Title}";
                            if (std != null && std.IsOutside)
                            {
                                row.Lines.Add(new SlEstimateLine
                                {
                                    RowNumber = ++n, Kind = SlCostKind.Subcontract, Description = title + " (برون‌سپاری)", EquipmentId = input.EquipmentId, EquipmentBomId = b.Id, ItemId = item.Id,
                                    Unit = Clean(b.Unit) ?? item.Unit, Quantity = quantity, UnitCost = null, CostSource = SlCostSource.Manual, Note = "هزینه پیمانکار را وارد کنید.",
                                });
                                continue;
                            }
                            MfWorkCenter wc = std?.WorkCenterId != null && centers.TryGetValue(std.WorkCenterId.Value, out MfWorkCenter c) ? c : null;
                            decimal hours = std == null ? 0m : std.SetupHours + std.RunHoursPerUnit * quantity;
                            decimal? rate = SlCosts.HourlyRate(wc);
                            string note = std == null ? "استاندارد زمان این مرحله تعریف نشده است." : wc == null ? "مرکز کار این مرحله تعریف نشده است." : rate == null ? "نرخ ساعتی مرکز کار را مالی تعیین نکرده است." : null;
                            row.Lines.Add(new SlEstimateLine
                            {
                                RowNumber = ++n, Kind = SlCostKind.Labour, Description = wc != null ? $"{title} ({wc.Code})" : title, EquipmentId = input.EquipmentId, EquipmentBomId = b.Id, ItemId = item.Id,
                                WorkCenterId = wc?.Id, Unit = "ساعت", Quantity = hours, UnitCost = std == null ? null : rate, CostSource = SlCostSource.WorkCenterRate, Note = note,
                            });
                        }
                    }
                }
            }
            Recalculate(row);
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return await GetEstimateAsync(row.Id, ct);
        }

        // ================================================================================== finalize, revise

        public async Task<ResponseDto<SlEstimateDto>> EstimateActionAsync(SlEstimateActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlEstimateDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlEstimateDto>(SlText.InvalidData);
            SlEstimate row = await Full().FirstOrDefaultAsync(e => e.Id == dto.Id && !e.Deleted, ct);
            if (row == null) return SlText.NotFound<SlEstimateDto>("برآورد پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlEstimateDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            SlEstimate result = row;
            if (action == "finalize")
            {
                if (row.Status != SlEstimateStatus.Draft) return Bad<SlEstimateDto>("فقط برآورد پیش‌نویس نهایی می‌شود.");
                Recalculate(row);
                if (row.MissingInputs != null || !row.Price.HasValue) return Bad<SlEstimateDto>("برآورد کامل نیست: " + (row.MissingInputs ?? "قیمت محاسبه نشد") + ".");
                if (row.Lines.Count == 0) return Bad<SlEstimateDto>("برآورد بدون ردیف هزینه نهایی نمی‌شود.");
                row.Status = SlEstimateStatus.Final;
                row.FinalizedByUserId = Me;
                row.FinalizedAt = now;
            }
            else if (action == "revise")
            {
                if (row.Status != SlEstimateStatus.Final) return Bad<SlEstimateDto>("فقط از برآورد نهایی‌شده بازنگری ساخته می‌شود.");
                string problem = await CheckInquiryAsync(row.InquiryId, ct);
                if (problem != null) return Bad<SlEstimateDto>(problem);
                int revision = await _uow.Set<SlEstimate>().Where(e => e.InquiryId == row.InquiryId).MaxAsync(e => e.Revision, ct) + 1;
                SlEstimate copy = await NewEstimateAsync(row.InquiryId, revision, now, ct);
                copy.Note = row.Note; copy.OverheadPercent = row.OverheadPercent; copy.ContingencyPercent = row.ContingencyPercent; copy.TargetMarginPercent = row.TargetMarginPercent;
                copy.GuaranteeAmount = row.GuaranteeAmount; copy.GuaranteeMonths = row.GuaranteeMonths; copy.GuaranteeRatePercentPerMonth = row.GuaranteeRatePercentPerMonth;
                copy.FinancingAmount = row.FinancingAmount; copy.FinancingMonths = row.FinancingMonths; copy.FinancingRatePercentPerMonth = row.FinancingRatePercentPerMonth;
                foreach (SlEstimateEquipment e in row.Equipments) copy.Equipments.Add(new SlEstimateEquipment { EquipmentId = e.EquipmentId, Count = e.Count });
                foreach (SlEstimateLine l in row.Lines.OrderBy(l => l.RowNumber))
                    copy.Lines.Add(new SlEstimateLine
                    {
                        RowNumber = l.RowNumber, Kind = l.Kind, Description = l.Description, EquipmentId = l.EquipmentId, EquipmentBomId = l.EquipmentBomId, ItemId = l.ItemId, WorkCenterId = l.WorkCenterId,
                        Unit = l.Unit, Quantity = l.Quantity, UnitCost = l.UnitCost, CostSource = l.CostSource, Note = l.Note,
                    });
                foreach (SlEstimateRisk r in row.Risks) copy.Risks.Add(new SlEstimateRisk { Title = r.Title, ProbabilityPercent = r.ProbabilityPercent, ImpactAmount = r.ImpactAmount });
                Recalculate(copy);
                copy.EditorUserIds = LedgerMath.WithEditor(null, Me);
                copy.ModifyDate = now; copy.ModifierUserId = Me;
                row.Status = SlEstimateStatus.Superseded;
                result = copy;
            }
            else return Bad<SlEstimateDto>("عمل نامعتبر است (finalize یا revise).");
            row.ModifyDate = now;
            row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlEstimateDto>(SlText.Concurrency);
            }
            return await GetEstimateAsync(result.Id, ct);
        }
    }
}
