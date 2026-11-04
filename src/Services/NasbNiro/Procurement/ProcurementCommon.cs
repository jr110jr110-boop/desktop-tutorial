using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Procurement
{
    /// <summary>Roles (JWT names). Requester, approver, buyer, receiver and payer are kept apart by the services.</summary>
    public static class ProcurementRoles
    {
        /// <summary>PB6: the unit managers who ask for a purchase but do not see the prices of the procurement chain: they read their own requests only.</summary>
        public const string ExtraRequesters = "PRODUCTIONMANAGER,SHOPSUPERVISOR,SALES,SERVICE,PROJECTMANAGER,SECRETARIAT,HR";
        /// <summary>PB6: every unit manager asks; COMMERCIAL is the buyer and does not (requester and buyer are kept apart, ISO 9001 8.4).</summary>
        public const string Requesters = "PMO,PLANNING,WAREHOUSE,WHSUPERVISOR,ENGINEERING,ENGINEER,CONSTRUCTION,QC,ASSEMBLER,FINANCIAL,FINANCEMANAGER," + ExtraRequesters;
        /// <summary>PB6: who may open the request screens: the procurement readers and the requesting unit managers (the latter see their own requests only).</summary>
        public const string RequestReaders = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,WAREHOUSE,WHSUPERVISOR,ENGINEERING,QC,CONSTRUCTION,ASSEMBLER,ENGINEER," + ExtraRequesters;
        /// <summary>PB6: archive and restore of procurement documents (with a reason).</summary>
        public const string Archivers = "COMMERCIAL,PLANNING,FINANCIAL,FINANCEMANAGER,PMO";
        /// <summary>PB6: the procurement reports (the unit that buys, plans and pays, and management).</summary>
        public const string ReportReaders = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,WAREHOUSE,WHSUPERVISOR,AUDITOR";
        public const string Planning = "PLANNING,PMO";
        /// <summary>مدیرعامل (or management): medium/major purchases, committee above the CEO threshold, advance above the limit.</summary>
        public const string Management = "CEO,PMO";
        /// <summary>The buyer (inquiries, quotes, selection, orders, payment requests, the vendor list). R1c (M12): the PMO deputy approves purchases and never buys, so it is not in this list.</summary>
        public const string Commercial = "COMMERCIAL";
        /// <summary>Technical members of the selection committee (approvers).</summary>
        public const string Technical = "ENGINEERING,QC,CONSTRUCTION,PMO";
        /// <summary>R1c (M12): only engineering and quality decide that a quote meets the technical requirements ("technically accepted"); neither the buyer nor the PMO deputy does.</summary>
        public const string TechnicalAcceptance = "ENGINEERING,QC";
        /// <summary>Approves and releases purchase orders and payment requests (PMO deputy).</summary>
        public const string OrderApproval = "PMO,CEO";
        public const string Finance = "FINANCIAL,FINANCEMANAGER,TREASURY,PMO";
        /// <summary>R1a2 (C1): cancels a supplier invoice that is not approved yet (never the person who registered it).</summary>
        public const string InvoiceCancel = "COMMERCIAL,FINANCIAL,FINANCEMANAGER";
        /// <summary>R1c (M12): the finance staff who register supplier invoices (the PMO deputy approves, never registers).</summary>
        public const string FinanceStaff = "FINANCIAL,FINANCEMANAGER,TREASURY";
        /// <summary>R1c (M13): confirms the deductions that the system proposed from the order terms (late penalty, retention).</summary>
        public const string DeductionConfirmation = "FINANCIAL,FINANCEMANAGER";
        /// <summary>Accepts services and subcontract work (not the buyer, not the approver of the order).</summary>
        public const string ServiceAcceptance = "QC,ENGINEERING,CONSTRUCTION,PLANNING";
        public const string Readers = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,WAREHOUSE,WHSUPERVISOR,ENGINEERING,QC,CONSTRUCTION";
    }

    /// <summary>Company thresholds (rial). Defaults come from the procedures; management can change them.</summary>
    public static class ProcurementSettingKeys
    {
        public const string MediumFrom = "MediumPurchaseFrom";
        public const string MajorFrom = "MajorPurchaseFrom";
        public const string Quote1From = "OneQuoteFrom";
        public const string Quote2From = "TwoQuotesFrom";
        public const string Quote3From = "ThreeQuotesFrom";
        public const string PettyCashLimit = "PettyCashLimit";
        public const string ContractFrom = "ContractRequiredFrom";
        public const string CommitteeFrom = "CommitteeRequiredFrom";
        public const string CeoCommitteeFrom = "CeoCommitteeFrom";
        public const string MaxAdvancePercent = "MaxAdvancePercent";
        public const string ReceiptTolerancePercent = "ReceiptTolerancePercent";
        public const string PriceTolerancePercent = "InvoicePriceTolerancePercent";
        /// <summary>1 = a purchase receipt needs a PMIS purchase order (or is a petty-cash purchase); 0 = off during go-live.</summary>
        public const string EnforcePurchaseOrder = "EnforcePurchaseOrder";

        // R1c (M13): the weights of the scored supplier evaluation (quality, delivery, price, service); they need not add up to 100, the score is the weighted mean of the criteria that have data
        public const string EvalWeightQuality = "SupplierEvalWeightQuality";
        public const string EvalWeightDelivery = "SupplierEvalWeightDelivery";
        public const string EvalWeightPrice = "SupplierEvalWeightPrice";
        public const string EvalWeightService = "SupplierEvalWeightService";

        // PB6
        /// <summary>1 = planning approves the budget and the delivery time of an inquiry before the winner is selected (decision D5); 0 = off.</summary>
        public const string InquiryPlanningRequired = "InquiryPlanningRequired";
        /// <summary>The volume of a contract line may change by this percent of the original quantity through addenda (the company template says 25).</summary>
        public const string AddendumVolumePercent = "AddendumVolumePercent";
        /// <summary>An addendum that changes the value by this many rial or more also needs the managing director.</summary>
        public const string AddendumCeoFrom = "AddendumCeoFrom";
        public const string GuaranteeAlertDays = "GuaranteeAlertDays";
        /// <summary>Days after the approval of a purchase request by which the first order should exist; beyond it the request is overdue.</summary>
        public const string RequestToOrderDays = "RequestToOrderDays";
        /// <summary>The cheque after the invoice is due this many days after the invoice date (the template says 60).</summary>
        public const string ChequeAfterInvoiceDays = "ChequeAfterInvoiceDays";

        public static IEnumerable<ProcurementSetting> Defaults()
        {
            yield return new ProcurementSetting { Key = MediumFrom, Value = 10_000_000m, Title = "خرید متوسط از (تایید مدیرعامل)", Source = "BL-006 ماده 2 و 4" };
            yield return new ProcurementSetting { Key = MajorFrom, Value = 200_000_000m, Title = "خرید عمده از (مصوبه هیئت‌مدیره)", Source = "BL-006 ماده 2 و 5" };
            yield return new ProcurementSetting { Key = Quote1From, Value = 15_000_000m, Title = "یک استعلام از", Source = "BL-007 بند 5-4" };
            yield return new ProcurementSetting { Key = Quote2From, Value = 30_000_000m, Title = "دو استعلام از", Source = "BL-007 بند 5-4" };
            yield return new ProcurementSetting { Key = Quote3From, Value = 50_000_000m, Title = "سه استعلام از", Source = "BL-007 بند 5-4؛ BL-006 ماده 4" };
            yield return new ProcurementSetting { Key = PettyCashLimit, Value = 50_000_000m, Title = "سقف خرید تنخواه (بدون سفارش خرید)", Source = "BL-007 بند 2 (5 میلیون تومان)" };
            yield return new ProcurementSetting { Key = ContractFrom, Value = 150_000_000m, Title = "قرارداد کتبی الزامی از", Source = "WI-PL-004 گام 7" };
            yield return new ProcurementSetting { Key = CommitteeFrom, Value = 150_000_000m, Title = "تایید کمیته انتخاب تامین‌کننده از", Source = "WI-PL-004 گام 6 (پیشنهاد PMIS)" };
            yield return new ProcurementSetting { Key = CeoCommitteeFrom, Value = 5_000_000_000m, Title = "تایید مدیرعامل در کمیته از", Source = "WI-PL-004 گام 6" };
            yield return new ProcurementSetting { Key = MaxAdvancePercent, Value = 25m, Title = "حداکثر پیش‌پرداخت (درصد)", Source = "BL-006 ماده 8-ه" };
            yield return new ProcurementSetting { Key = ReceiptTolerancePercent, Value = 2m, Title = "رواداری مقدار دریافت نسبت به سفارش (درصد)", Source = "پیشنهاد PMIS" };
            yield return new ProcurementSetting { Key = EnforcePurchaseOrder, Value = 1m, Title = "الزام سفارش خرید برای رسید خرید (۱ = بله، ۰ = خیر در دوره راه‌اندازی)", Source = "WI-WH-001 / BL-007" };
            yield return new ProcurementSetting { Key = PriceTolerancePercent, Value = 1m, Title = "رواداری قیمت فاکتور نسبت به سفارش (درصد)", Source = "پیشنهاد PMIS (تطبیق سه‌طرفه)" };
            yield return new ProcurementSetting { Key = EvalWeightQuality, Value = 40m, Title = "وزن کیفیت در ارزیابی امتیازی تامین‌کننده", Source = "★ تصمیم موقت؛ شرکت تایید کند (WI-PO-003)" };
            yield return new ProcurementSetting { Key = EvalWeightDelivery, Value = 30m, Title = "وزن تحویل به‌موقع در ارزیابی امتیازی تامین‌کننده", Source = "★ تصمیم موقت؛ شرکت تایید کند (WI-PO-003)" };
            yield return new ProcurementSetting { Key = EvalWeightPrice, Value = 20m, Title = "وزن قیمت در ارزیابی امتیازی تامین‌کننده", Source = "★ تصمیم موقت؛ شرکت تایید کند (WI-PO-003)" };
            yield return new ProcurementSetting { Key = EvalWeightService, Value = 10m, Title = "وزن خدمات در ارزیابی امتیازی تامین‌کننده", Source = "★ تصمیم موقت؛ شرکت تایید کند (WI-PO-003)" };
            yield return new ProcurementSetting { Key = InquiryPlanningRequired, Value = 1m, Title = "تایید بودجه و زمان تحویل استعلام توسط برنامه‌ریزی پیش از انتخاب برنده (۱ = بله)", Source = "تصمیم D5 برنامه" };
            yield return new ProcurementSetting { Key = AddendumVolumePercent, Value = 25m, Title = "حداکثر تغییر حجم قرارداد با الحاقیه (درصد مقدار اولیه)", Source = "الگوی قرارداد FO-IP-040-04 ★ شرکت تایید کند" };
            yield return new ProcurementSetting { Key = AddendumCeoFrom, Value = 10_000_000m, Title = "الحاقیه با تغییر مبلغ از این حد، تایید مدیرعامل لازم دارد (ریال)", Source = "★ پیشنهاد PMIS؛ شرکت تایید کند" };
            yield return new ProcurementSetting { Key = GuaranteeAlertDays, Value = 30m, Title = "هشدار پایان اعتبار ضمانت‌نامه از چند روز قبل", Source = "★ پیشنهاد PMIS؛ شرکت تایید کند" };
            yield return new ProcurementSetting { Key = RequestToOrderDays, Value = 14m, Title = "مهلت صدور سفارش پس از تایید درخواست خرید (روز)", Source = "★ پیشنهاد PMIS؛ شرکت تایید کند" };
            yield return new ProcurementSetting { Key = ChequeAfterInvoiceDays, Value = 60m, Title = "چک پس از فاکتور: روز پس از تاریخ فاکتور", Source = "الگوی قرارداد FO-IP-040-04" };
            yield return new ProcurementSetting { Key = global::Services.NasbNiro.Attachments.AttachmentSettingKeys.MaxMegabytes, Value = global::Services.NasbNiro.Attachments.AttachmentRules.DefaultMaxMegabytes, Title = "حداکثر حجم هر فایل پیوست (مگابایت)", Source = "★ PB3؛ شرکت تایید کند" };
        }

        public static async Task<Dictionary<string, decimal>> LoadAsync(IUnitOfWork uow, CancellationToken cancellationToken)
        {
            DbSet<ProcurementSetting> set = uow.Set<ProcurementSetting>();
            List<ProcurementSetting> rows = await set.ToListAsync(cancellationToken);
            bool added = false;
            foreach (ProcurementSetting d in Defaults())
            {
                if (rows.Any(r => r.Key == d.Key)) continue;
                set.Add(d);
                rows.Add(d);
                added = true;
            }
            if (added) await uow.SaveChangesAsync(cancellationToken);
            return rows.ToDictionary(r => r.Key, r => r.Value);
        }

        /// <summary>Number of quotes required for an amount (BL-007 s.5-4).</summary>
        public static int RequiredQuotes(Dictionary<string, decimal> s, decimal amount) =>
            amount >= s[Quote3From] ? 3 : amount >= s[Quote2From] ? 2 : amount >= s[Quote1From] ? 1 : 0;
    }

    internal static class ProcurementNumbers
    {
        public const int PurchaseRequest = 301;
        public const int Inquiry = 302;
        public const int PurchaseOrder = 303;
        public const int Invoice = 304;
        public const int Payment = 305;
        public const int ServiceAcceptance = 306;

        // PB6 (741..760 are the numbers of this package)
        public const int Addendum = 741;
        public const int Guarantee = 742;
    }

    public static class SupplierGrades
    {
        /// <summary>WI-PO-003: A 85-100, B 65-84, C 45-64, D 0-44.</summary>
        public static SupplierGrade FromScore(decimal score) =>
            score >= 85 ? SupplierGrade.A : score >= 65 ? SupplierGrade.B : score >= 45 ? SupplierGrade.C : SupplierGrade.D;
    }
}
