using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;

namespace Services.NasbNiro.Procurement
{
    /// <summary>
    /// R1c (H6): what a value needs in purchasing authority. A purchase request is approved on its ESTIMATE; what the company commits is the selected quote and then the order,
    /// so the number of quotes and the level of approval are judged again on those values, at the selection, at the creation of the order and at its approval.
    /// </summary>
    internal static class ProcurementAuthority
    {
        public const string InquiryNotApproved = "استعلام این سفارش در وضعیت «تاییدشده» نیست؛ سفارش فقط از استعلامی صادر می‌شود که برنده آن انتخاب و توسط کمیته تایید شده باشد.";
        public const string InquiryRequired = "برای این مبلغ سفارش باید از استعلام تاییدشده صادر شود.";

        public static bool NeedsManagement(Dictionary<string, decimal> s, decimal amount) => amount >= s[ProcurementSettingKeys.MediumFrom];
        public static bool NeedsBoard(Dictionary<string, decimal> s, decimal amount) => amount >= s[ProcurementSettingKeys.MajorFrom];

        /// <summary>The number of technically accepted quotes a value needs: what the estimate needed (stored on the inquiry), or what the value itself needs, whichever is more.</summary>
        public static int RequiredQuotes(Dictionary<string, decimal> s, int onInquiry, decimal amount) => Math.Max(onInquiry, ProcurementSettingKeys.RequiredQuotes(s, amount));

        /// <summary>True when the value is above the managing-director limit or the board limit and the purchase request does not carry that approval (it was approved on a lower estimate).</summary>
        public static bool RequestLevelGap(Dictionary<string, decimal> s, PurchaseRequest request, decimal amount) =>
            (NeedsManagement(s, amount) && !request.ManagementApprovedAt.HasValue) || (NeedsBoard(s, amount) && string.IsNullOrWhiteSpace(request.BoardResolution));

        /// <summary>True when the selection committee of an inquiry needs the managing director for the value of the selected quote.</summary>
        public static bool InquiryNeedsManagement(Dictionary<string, decimal> s, PurchaseInquiry inquiry, PurchaseRequest request, decimal amount)
        {
            int accepted = inquiry.Quotes?.Count(q => q.TechnicallyAccepted) ?? 0;
            bool fewer = !string.IsNullOrWhiteSpace(inquiry.FewerQuotesReason) && accepted < RequiredQuotes(s, inquiry.RequiredQuotes, amount);
            return amount >= s[ProcurementSettingKeys.CeoCommitteeFrom] || fewer || (request != null && RequestLevelGap(s, request, amount));
        }

        /// <summary>
        /// The refusal for an order of this value (rial) built from the inquiry, or null when the authority covers it: the inquiry is approved, enough technically accepted quotes
        /// were taken for THIS value (or the managing director accepted fewer), and the managing director / the board approved a value of this size.
        /// </summary>
        public static string OrderProblem(Dictionary<string, decimal> s, decimal amount, PurchaseInquiry inquiry, PurchaseRequest request)
        {
            if (inquiry.Status != InquiryStatus.Approved) return InquiryNotApproved;
            int required = RequiredQuotes(s, inquiry.RequiredQuotes, amount);
            int accepted = inquiry.Quotes.Count(q => q.TechnicallyAccepted);
            if (accepted < required)
            {
                if (string.IsNullOrWhiteSpace(inquiry.FewerQuotesReason))
                    return $"برای مبلغ این سفارش {required} پیشنهاد قابل قبول فنی لازم است و {accepted} پیشنهاد هست.";
                if (!inquiry.ManagementApprovedAt.HasValue)
                    return $"برای مبلغ این سفارش {required} پیشنهاد قابل قبول فنی لازم است؛ پذیرش پیشنهاد کمتر تایید مدیرعامل لازم دارد.";
            }
            if (request != null && RequestLevelGap(s, request, amount) && !inquiry.ManagementApprovedAt.HasValue)
                return "مبلغ این سفارش از سقف تایید مدیرعامل یا مصوبه هیئت‌مدیره درخواست خرید گذشته است؛ تایید مدیرعامل در استعلام لازم است.";
            if (request != null && NeedsBoard(s, amount) && string.IsNullOrWhiteSpace(request.BoardResolution))
                return "خرید عمده (BL-006 ماده 5) مصوبه هیئت‌مدیره لازم دارد؛ شماره مصوبه در تایید مدیرعامل ثبت شود.";
            return null;
        }

        /// <summary>The refusal for an order without an inquiry (small purchases only), or null. Every purchase request behind it must carry the approval its share of the order needs.</summary>
        public static string OrderProblemWithoutInquiry(Dictionary<string, decimal> s, decimal amount, IEnumerable<(PurchaseRequest Request, decimal Share)> requests)
        {
            if (amount >= s[ProcurementSettingKeys.Quote1From]) return InquiryRequired;
            foreach (var r in requests)
            {
                decimal basis = Math.Max(r.Request.EstimatedAmount, r.Share);
                if (RequestLevelGap(s, r.Request, basis))
                    return $"مبلغ سفارش از سقف تایید مدیرعامل یا مصوبه هیئت‌مدیره درخواست خرید {r.Request.Number} گذشته است؛ درخواست خرید باید با سطح تایید این مبلغ تایید شده باشد.";
            }
            return null;
        }
    }

    /// <summary>R1c (M13): the metrics of a supplier that the scored evaluation reads (all of them are computed by the vendor list already).</summary>
    internal sealed class SupplierMetrics
    {
        public int Orders { get; set; }
        public int ReceiptLines { get; set; }
        public int LateReceiptLines { get; set; }
        public int Ncrs { get; set; }
        public int Returns { get; set; }

        /// <summary>Written warnings after returns; the third one cuts the cooperation (WI-PO-003 note 3).</summary>
        public int Warnings { get; set; }

        /// <summary>One value per inquiry the supplier took part in with a technically accepted quote: the lowest accepted total of that inquiry divided by the supplier's total (at most 1).</summary>
        public List<decimal> PriceRatios { get; set; } = new List<decimal>();
    }

    internal sealed class CriterionScore
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal Weight { get; set; }

        /// <summary>0..100; null when the supplier has no data for the criterion yet (it is left out of the weighted mean).</summary>
        public decimal? Score { get; set; }
        public string Basis { get; set; }
    }

    /// <summary>R1c (M13): the four criteria of the supplier evaluation computed from the metrics, with the weights of the settings.</summary>
    internal static class SupplierScoring
    {
        public const string Quality = "quality";
        public const string Delivery = "delivery";
        public const string Price = "price";
        public const string Service = "service";

        public static List<CriterionScore> Criteria(SupplierMetrics m, Dictionary<string, decimal> s)
        {
            var list = new List<CriterionScore>();

            // quality: the share of receipt lines without a nonconformance report or a return to the supplier
            int defects = m.Ncrs + m.Returns;
            decimal? quality = m.ReceiptLines > 0 || defects > 0 ? Math.Round(100m * (1m - Math.Min(1m, defects / (decimal)Math.Max(m.ReceiptLines, 1))), 1) : (decimal?)null;
            list.Add(new CriterionScore
            {
                Key = Quality, Title = "کیفیت", Weight = s[ProcurementSettingKeys.EvalWeightQuality], Score = quality,
                Basis = quality == null ? "هنوز رسیدی ثبت نشده است" : $"{m.Ncrs} گزارش عدم انطباق و {m.Returns} مرجوعی در {m.ReceiptLines} ردیف رسید",
            });

            // delivery: the share of receipt lines that arrived on or before the due date
            decimal? delivery = m.ReceiptLines > 0 ? Math.Round(100m * (m.ReceiptLines - m.LateReceiptLines) / m.ReceiptLines, 1) : (decimal?)null;
            list.Add(new CriterionScore
            {
                Key = Delivery, Title = "تحویل به‌موقع", Weight = s[ProcurementSettingKeys.EvalWeightDelivery], Score = delivery,
                Basis = delivery == null ? "هنوز رسیدی ثبت نشده است" : $"{m.ReceiptLines - m.LateReceiptLines} از {m.ReceiptLines} ردیف رسید در موعد",
            });

            // price: how close the supplier's technically accepted quotes were to the lowest accepted quote of the same inquiry
            decimal? price = m.PriceRatios.Count > 0 ? Math.Round(100m * m.PriceRatios.Average(), 1) : (decimal?)null;
            list.Add(new CriterionScore
            {
                Key = Price, Title = "قیمت", Weight = s[ProcurementSettingKeys.EvalWeightPrice], Score = price,
                Basis = price == null ? "پیشنهاد قابل قبول فنی در استعلام نداشته است" : $"میانگین نسبت کمترین قیمت قابل قبول به قیمت تامین‌کننده در {m.PriceRatios.Count} استعلام",
            });

            // service: written warnings after returns (three cut the cooperation)
            decimal? service = m.Orders > 0 ? Math.Round(100m * (1m - Math.Min(1m, m.Warnings / 3m)), 1) : (decimal?)null;
            list.Add(new CriterionScore
            {
                Key = Service, Title = "خدمات پس از فروش و پاسخگویی", Weight = s[ProcurementSettingKeys.EvalWeightService], Score = service,
                Basis = service == null ? "سفارشی ثبت نشده است" : $"{m.Warnings} اخطار کتبی از سه اخطار قطع همکاری",
            });
            return list;
        }

        /// <summary>The weighted mean of the criteria that have data (null when none has), to one decimal like the manual evaluation.</summary>
        public static decimal? Total(IEnumerable<CriterionScore> criteria)
        {
            List<CriterionScore> applicable = criteria.Where(c => c.Score.HasValue && c.Weight > 0).ToList();
            if (applicable.Count == 0) return null;
            return Math.Round(applicable.Sum(c => c.Score.Value * c.Weight) / applicable.Sum(c => c.Weight), 1);
        }
    }

    /// <summary>R1c (M13): deductions that the system proposes on the payment request of an invoice from the terms of the purchase order.</summary>
    internal static class PaymentProposals
    {
        public const string Marker = "پیشنهاد سیستم از شرایط سفارش؛ تایید مالی لازم است";

        internal sealed class Proposal
        {
            public FnDeductionKind Kind { get; set; }
            public decimal Base { get; set; }
            public decimal RatePercent { get; set; }
            public decimal Amount { get; set; }
            public string Note { get; set; }
        }

        /// <summary>
        /// The late penalty: for each invoice line, the line amount x the weekly rate x (days late / 7), where the days late are counted from the due date to the day the line was last
        /// received (or its service accepted). What other payment requests of the same invoice already deduct is subtracted, so the penalty is not taken twice.
        /// </summary>
        public static Proposal LatePenalty(decimal? ratePercentPerWeek, IEnumerable<(decimal LineAmount, DateTime? Due, DateTime? Received)> lines, decimal alreadyDeducted, decimal room)
        {
            if (!ratePercentPerWeek.HasValue || ratePercentPerWeek.Value <= 0m) return null;
            decimal total = 0m, basis = 0m;
            int maxLate = 0;
            foreach (var l in lines)
            {
                if (!l.Due.HasValue || !l.Received.HasValue) continue;
                int late = (int)(l.Received.Value.Date - l.Due.Value.Date).TotalDays;
                if (late <= 0) continue;
                total += l.LineAmount * ratePercentPerWeek.Value / 100m * late / 7m;
                basis += l.LineAmount;
                maxLate = Math.Max(maxLate, late);
            }
            decimal amount = Math.Min(Math.Round(total, 0, MidpointRounding.AwayFromZero) - alreadyDeducted, room);
            if (amount <= 0m) return null;
            return new Proposal
            {
                Kind = FnDeductionKind.LatePenalty, Base = Math.Round(basis, 0), RatePercent = ratePercentPerWeek.Value, Amount = amount,
                Note = $"جریمه تاخیر تحویل تا {maxLate} روز با نرخ {ratePercentPerWeek.Value.ToString("0.####", CultureInfo.InvariantCulture)}٪ در هفته؛ {Marker}",
            };
        }

        /// <summary>The retention (performance guarantee deposit) of the order terms: a share of the amount of the payment request.</summary>
        public static Proposal Retention(decimal? retentionPercent, decimal paymentAmount, decimal room)
        {
            if (!retentionPercent.HasValue || retentionPercent.Value <= 0m || paymentAmount <= 0m) return null;
            decimal amount = Math.Min(Math.Round(paymentAmount * retentionPercent.Value / 100m, 0, MidpointRounding.AwayFromZero), room);
            if (amount <= 0m) return null;
            return new Proposal
            {
                Kind = FnDeductionKind.PerformanceRetention, Base = Math.Round(paymentAmount, 0), RatePercent = retentionPercent.Value, Amount = amount,
                Note = $"تضمین حسن انجام کار {retentionPercent.Value.ToString("0.####", CultureInfo.InvariantCulture)}٪ طبق سفارش؛ {Marker}",
            };
        }
    }

    /// <summary>R1c (M13): the frozen snapshot of a purchase order revision.</summary>
    internal static class OrderSnapshot
    {
        public static string Json(PurchaseOrder o)
        {
            var snapshot = new
            {
                o.Number, o.Revision, o.Status, o.Kind, OrderDate = o.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), o.ProviderId, o.InquiryId, o.QuoteId, o.WorkOrderId, o.ContractId,
                o.Currency, o.ExchangeRate, o.Incoterms, o.PaymentTerms, o.VatPercent, o.AdvancePercent, o.AdvanceGuarantee, o.PerformanceGuarantee, o.RetentionPercent,
                o.ContractNumber, o.Requirements, DeliveryDate = o.DeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), o.DeliveryPlace, o.LatePenaltyPercentPerWeek,
                o.Description, o.TotalAmount, o.ApprovedAt, o.ApprovedByUserId, o.ReleasedAt, o.ReleasedByUserId, o.RevisionReason,
                Lines = o.Lines.OrderBy(l => l.RowNumber).Select(l => new
                {
                    l.RowNumber, l.RequestLineId, l.ItemId, l.Description, l.Unit, l.Quantity, l.UnitPrice,
                    DeliveryDate = l.DeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), l.WorkOrderId, l.ContractId, l.IsCapital,
                }).ToList(),
            };
            return JsonSerializer.Serialize(snapshot);
        }
    }
}
