using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// R1c (H3): the contract as it stands today, the one source of the value, the delivery date and the price/time change of a contract:
    /// the terms as signed plus the approved variation orders plus the CEO-approved amendments that no approved variation order carries.
    /// </summary>
    public sealed class PcEffectiveContract
    {
        public int ContractId { get; set; }

        /// <summary>The value in the terms (before the changes); falls back to the price of the legacy contract when the terms carry none.</summary>
        public decimal BaseValue { get; set; }

        /// <summary>Base value plus <see cref="PriceDelta"/>.</summary>
        public decimal Value { get; set; }

        /// <summary>Approved variation orders plus CEO-approved amendments (an amendment named by an approved variation order is counted once, through the order).</summary>
        public decimal PriceDelta { get; set; }
        public int DaysDelta { get; set; }
        public decimal VariationPrice { get; set; }
        public int VariationDays { get; set; }
        public decimal AmendmentPrice { get; set; }
        public int AmendmentDays { get; set; }

        /// <summary>The delivery date in the terms; null when none is recorded.</summary>
        public DateTime? BaseDeliveryDate { get; set; }

        /// <summary>The delivery date moved by <see cref="DaysDelta"/>; null when the terms carry no delivery date.</summary>
        public DateTime? DeliveryDate { get; set; }
    }

    /// <summary>The effective numbers of contracts (see <see cref="PcEffectiveContract"/>).</summary>
    public static class PcEffective
    {
        public static async Task<PcEffectiveContract> EffectiveContractAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken)
        {
            PcContractTerms terms = await uow.Set<PcContractTerms>().AsNoTracking().FirstOrDefaultAsync(t => t.ContractId == contractId && !t.Deleted, cancellationToken);
            decimal value = terms?.ValueExVat ?? 0m;
            if (value <= 0m)
            {
                decimal? price = await uow.Set<ContractEntity>().AsNoTracking().Where(c => c.Id == contractId).Select(c => c.Price).FirstOrDefaultAsync(cancellationToken);
                value = price ?? 0m;
            }
            var orders = await uow.Set<PcVariationOrder>().AsNoTracking().Where(v => v.ContractId == contractId && !v.Deleted && v.Status == PcVariationStatus.Approved)
                .Select(v => new { v.AmendmentId, v.PriceDelta, v.DaysDelta }).ToListAsync(cancellationToken);
            var amendments = await uow.Set<PcContractAmendment>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted && a.CeoApprovedAt != null)
                .Select(a => new { a.Id, a.PriceDelta, a.DaysDelta }).ToListAsync(cancellationToken);
            return Build(contractId, value, terms?.ContractDeliveryDate,
                orders.Select(o => (o.AmendmentId, o.PriceDelta, o.DaysDelta)).ToList(), amendments.Select(a => (a.Id, a.PriceDelta ?? 0m, a.DaysDelta ?? 0)).ToList());
        }

        /// <summary>The price change of several contracts at once (the CEO backlog); contracts without a change are not in the result.</summary>
        public static async Task<Dictionary<int, decimal>> PriceDeltasAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, decimal>();
            if (contractIds == null || contractIds.Count == 0) return result;
            var orders = await uow.Set<PcVariationOrder>().AsNoTracking().Where(v => contractIds.Contains(v.ContractId) && !v.Deleted && v.Status == PcVariationStatus.Approved)
                .Select(v => new { v.ContractId, v.AmendmentId, v.PriceDelta, v.DaysDelta }).ToListAsync(cancellationToken);
            var amendments = await uow.Set<PcContractAmendment>().AsNoTracking().Where(a => contractIds.Contains(a.ContractId) && !a.Deleted && a.CeoApprovedAt != null)
                .Select(a => new { a.ContractId, a.Id, a.PriceDelta, a.DaysDelta }).ToListAsync(cancellationToken);
            foreach (int id in orders.Select(o => o.ContractId).Union(amendments.Select(a => a.ContractId)).Distinct())
            {
                PcEffectiveContract e = Build(id, 0m, null,
                    orders.Where(o => o.ContractId == id).Select(o => (o.AmendmentId, o.PriceDelta, o.DaysDelta)).ToList(),
                    amendments.Where(a => a.ContractId == id).Select(a => (a.Id, a.PriceDelta ?? 0m, a.DaysDelta ?? 0)).ToList());
                if (e.PriceDelta != 0m) result[id] = e.PriceDelta;
            }
            return result;
        }

        /// <summary>
        /// Amounts given as a share of the value follow the effective value: the milestones not yet on a statement (planned or due) are recomputed (not saved).
        /// Returns how many amounts changed.
        /// </summary>
        public static async Task<int> RefreshMilestoneAmountsAsync(IUnitOfWork uow, int contractId, int userId, CancellationToken cancellationToken)
        {
            PcEffectiveContract effective = await EffectiveContractAsync(uow, contractId, cancellationToken);
            if (effective.Value <= 0m) return 0;
            List<PcPaymentMilestone> list = await uow.Set<PcPaymentMilestone>().Where(m => m.ContractId == contractId && !m.Deleted && m.PercentOfValue != null
                && (m.Status == PcMilestoneStatus.Planned || m.Status == PcMilestoneStatus.Due)).ToListAsync(cancellationToken);
            int changed = 0;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PcPaymentMilestone m in list)
            {
                decimal amount = PcText.Round2(effective.Value * m.PercentOfValue.Value / 100m);
                if (amount == m.Amount) continue;
                m.Amount = amount; m.ModifyDate = now; m.ModifierUserId = userId;
                changed++;
            }
            return changed;
        }

        /// <summary>The arithmetic, apart from the database: the dedupe rule is that an approved order which names an amendment carries that amendment's change.</summary>
        internal static PcEffectiveContract Build(int contractId, decimal baseValue, DateTime? baseDelivery, IList<(int? AmendmentId, decimal Price, int Days)> orders, IList<(int Id, decimal Price, int Days)> amendments)
        {
            var carried = new HashSet<int>(orders.Where(o => o.AmendmentId.HasValue).Select(o => o.AmendmentId.Value));
            var e = new PcEffectiveContract { ContractId = contractId, BaseValue = baseValue, BaseDeliveryDate = baseDelivery?.Date };
            e.VariationPrice = orders.Sum(o => o.Price);
            e.VariationDays = orders.Sum(o => o.Days);
            e.AmendmentPrice = amendments.Where(a => !carried.Contains(a.Id)).Sum(a => a.Price);
            e.AmendmentDays = amendments.Where(a => !carried.Contains(a.Id)).Sum(a => a.Days);
            e.PriceDelta = e.VariationPrice + e.AmendmentPrice;
            e.DaysDelta = e.VariationDays + e.AmendmentDays;
            e.Value = baseValue + e.PriceDelta;
            e.DeliveryDate = e.BaseDeliveryDate?.AddDays(e.DaysDelta);
            return e;
        }
    }

    /// <summary>
    /// R1c (H8): the gates of the contract review (ISO 9001 8.2.3) and of the re-review that an amendment or a variation order starts.
    /// A contract is reviewed when a review of the stage Contract was accepted by two different people (the PMO and the commercial unit).
    /// </summary>
    internal static class PcGates
    {
        public const string ReviewMissing = "بازنگری قرارداد (ISO 9001 بند 8.2.3) هنوز پذیرفته نشده است؛ ابتدا PMO و بازرگانی (دو نفر متفاوت) آن را بپذیرند.";
        public const string ChangeReviewOpen = "پس از الحاقیه یا دستور تغییر، بازنگری مجدد قرارداد باید دوباره پذیرفته شود؛ تا آن زمان مبنای جدید ساخته یا تایید نمی‌شود.";
        public const string TenderReviewMissing = "بازنگری مناقصه (پیش از ارسال پیشنهاد) هنوز توسط PMO و بازرگانی (دو نفر متفاوت) پذیرفته نشده است.";

        public static Task<bool> ContractReviewAcceptedAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken) =>
            uow.Set<PcContractReview>().AsNoTracking().AnyAsync(r => r.ContractId == contractId && !r.Deleted && r.Stage == PcReviewStage.Contract && r.Decision == PcReviewDecision.Accept, cancellationToken);

        public static Task<bool> ChangeReviewOpenAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken) =>
            uow.Set<PcContractReview>().AsNoTracking().AnyAsync(r => r.ContractId == contractId && !r.Deleted && r.Stage == PcReviewStage.Change && r.Decision == PcReviewDecision.ReviewRequired, cancellationToken);

        /// <summary>Null when the contract review is accepted (project code, WBS, purchase requests); else the Persian refusal.</summary>
        public static async Task<string> RequireReviewAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken) =>
            await ContractReviewAcceptedAsync(uow, contractId, cancellationToken) ? null : ReviewMissing;

        /// <summary>Null when a new baseline may be built or approved: the review is accepted and no re-review is waiting.</summary>
        public static async Task<string> RequireBaselineReviewAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken)
        {
            string error = await RequireReviewAsync(uow, contractId, cancellationToken);
            if (error != null) return error;
            return await ChangeReviewOpenAsync(uow, contractId, cancellationToken) ? ChangeReviewOpen : null;
        }

        /// <summary>Null when the tender (by its code) has an accepted tender-stage review.</summary>
        public static async Task<string> RequireTenderReviewAsync(IUnitOfWork uow, string tenderCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tenderCode)) return null;
            string code = tenderCode.Trim();
            bool ok = await uow.Set<PcContractReview>().AsNoTracking().AnyAsync(r => !r.Deleted && r.Stage == PcReviewStage.Tender && r.TenderCode == code && r.Decision == PcReviewDecision.Accept, cancellationToken);
            return ok ? null : TenderReviewMissing;
        }

        /// <summary>
        /// An approved amendment or variation order re-opens the review of the contract: adds (without saving) a review of the stage Change in the state ReviewRequired.
        /// Nothing is added when the contract was never reviewed (the first review still has to be accepted) or when a re-review is already waiting.
        /// </summary>
        public static async Task OpenChangeReviewAsync(IUnitOfWork uow, int contractId, int? amendmentId, int? variationOrderId, int userId, string note, CancellationToken cancellationToken)
        {
            if (!await ContractReviewAcceptedAsync(uow, contractId, cancellationToken)) return;
            // one waiting re-review covers every change that happened before it was accepted
            bool exists = await uow.Set<PcContractReview>().AnyAsync(r => r.ContractId == contractId && !r.Deleted && r.Stage == PcReviewStage.Change && r.Decision == PcReviewDecision.ReviewRequired, cancellationToken);
            if (exists) return;
            DateTimeOffset now = DateTimeOffset.Now;
            PcContractReview last = await uow.Set<PcContractReview>().AsNoTracking().Where(r => r.ContractId == contractId && !r.Deleted && r.Stage == PcReviewStage.Contract).OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            uow.Set<PcContractReview>().Add(new PcContractReview
            {
                ContractId = contractId, TenderCode = last?.TenderCode, Kind = last?.Kind ?? PcReviewKind.StandardProduct, Stage = PcReviewStage.Change, Decision = PcReviewDecision.ReviewRequired,
                AmendmentId = amendmentId, VariationOrderId = variationOrderId, Notes = note, CreateDate = now, CreatorUserId = userId, ModifyDate = now, ModifierUserId = userId,
            });
        }

        /// <summary>The items that must be ticked before a review of the given stage can be accepted (Persian names).</summary>
        public static List<string> MissingItems(PcContractReview r)
        {
            var missing = new List<string>();
            if (r.Stage == PcReviewStage.Change)
            {
                if (!r.RequirementsDefined) missing.Add("الزامات تغییرکرده");
                if (!r.DeliveryFeasible) missing.Add("امکان تحویل در موعد جدید");
                if (!r.PenaltiesReviewed) missing.Add("جرایم");
                if (!r.PaymentTermsReviewed) missing.Add("شرایط پرداخت");
                return missing;
            }
            if (!r.RequirementsDefined) missing.Add("الزامات تعریف‌شده");
            if (!r.DifferencesResolved) missing.Add("رفع اختلافات");
            if (!r.DeliveryFeasible) missing.Add("امکان تحویل در موعد");
            if (!r.StandardsItpAgreed) missing.Add("استانداردها و ITP");
            if (!r.GuaranteesReviewed) missing.Add("ضمانت‌نامه‌ها");
            if (!r.PenaltiesReviewed) missing.Add("جرایم");
            if (!r.PaymentTermsReviewed) missing.Add("شرایط پرداخت");
            if (!r.CurrencyReviewed) missing.Add("ارز و نرخ تبدیل");
            if (r.Kind == PcReviewKind.SpecialOrder && !r.CapabilityChecked) missing.Add("تایید توانایی اجرا توسط برنامه‌ریزی");
            return missing;
        }
    }

    /// <summary>R1c (M1): the editor list of a draft (",5,7,"); whoever created or edited a document is not its checker.</summary>
    internal static class PcMaker
    {
        public static string WithEditor(string ids, int user) => user == 0 || (ids != null && ids.Contains("," + user + ",")) ? ids : (ids ?? ",") + user + ",";
        public static bool Touched(int creator, string ids, int user) => (creator != 0 && creator == user) || (ids != null && ids.Contains("," + user + ","));
    }

    /// <summary>R1c (M16): the warranty of a delivered unit follows the contract terms, not the expiry of the bank guarantee.</summary>
    public static class PcWarranty
    {
        /// <summary>
        /// The warranty ends at the earlier of two dates: commissioning plus the months after commissioning, and delivery plus the months after delivery
        /// (★ 12 and 18 by default, to be checked in each contract). A date that is not known yet is left out; null when neither is known.
        /// </summary>
        public static DateTime? End(DateTime? commissioning, DateTime? delivery, int monthsAfterCommissioning, int monthsAfterDelivery)
        {
            DateTime? a = commissioning.HasValue && monthsAfterCommissioning > 0 ? commissioning.Value.Date.AddMonths(monthsAfterCommissioning) : (DateTime?)null;
            DateTime? b = delivery.HasValue && monthsAfterDelivery > 0 ? delivery.Value.Date.AddMonths(monthsAfterDelivery) : (DateTime?)null;
            if (a.HasValue && b.HasValue) return a.Value <= b.Value ? a : b;
            return a ?? b;
        }
    }

    /// <summary>R1c (H5, M8): when a work order counts as delivered, and how a delay penalty is counted.</summary>
    internal static class PcDelivery
    {
        /// <summary>
        /// The day a work order is fully delivered: the delivery day of the shipment on which the delivered quantities (the shipment lines) first add up to the ordered quantity.
        /// A shipment without lines (data from before the lines existed) delivers the whole order. Null while the quantity is not complete.
        /// </summary>
        public static DateTime? FullDeliveryDate(decimal orderedQuantity, IEnumerable<(DateTime DeliveredAt, int ShipmentId, decimal? LineQuantity)> deliveredShipments)
        {
            decimal ordered = orderedQuantity > 0m ? orderedQuantity : 1m;
            decimal sum = 0m;
            foreach (var s in deliveredShipments.OrderBy(x => x.DeliveredAt).ThenBy(x => x.ShipmentId))
            {
                sum += s.LineQuantity ?? ordered;
                if (sum >= ordered) return s.DeliveredAt.Date;
            }
            return null;
        }

        /// <summary>
        /// Amounts of the delay penalty of the items of one contract under one overall cap: each item is base x weekly rate x (late days / 7), and the sum of all
        /// items (with what was already decided) never exceeds cap percent of the contract value. Items are served in the order given.
        /// </summary>
        public static decimal[] PenaltyAmounts(decimal contractValue, decimal ratePercentPerWeek, decimal? capPercent, IList<(decimal Base, int LateDays)> items, decimal alreadyCounted, int decimals = 0)
        {
            var result = new decimal[items.Count];
            decimal remaining = capPercent.HasValue ? Math.Max(0m, contractValue * capPercent.Value / 100m - alreadyCounted) : decimal.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                decimal amount = PcCommercialService.PenaltyAmount(items[i].Base, ratePercentPerWeek, items[i].LateDays, capPercent, decimals);
                if (amount > remaining) amount = PcText.RoundMoney(remaining, decimals);
                result[i] = amount;
                if (capPercent.HasValue) remaining -= amount;
            }
            return result;
        }
    }

    /// <summary>R1c (R-CL-1): claims that are still open.</summary>
    internal static class PcClaimRules
    {
        /// <summary>The number of claims of the contract that are neither settled nor withdrawn.</summary>
        public static Task<int> OpenCountAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken) =>
            uow.Set<PcClaim>().AsNoTracking().CountAsync(c => c.ContractId == contractId && !c.Deleted && c.Status != PcClaimStatus.Settled && c.Status != PcClaimStatus.Withdrawn, cancellationToken);
    }
}
