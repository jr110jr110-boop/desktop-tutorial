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
        public const string Requesters = "PMO,PLANNING,WAREHOUSE,WHSUPERVISOR,ENGINEERING,ENGINEER,CONSTRUCTION,QC,ASSEMBLER,COMMERCIAL";
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
    }

    public static class SupplierGrades
    {
        /// <summary>WI-PO-003: A 85-100, B 65-84, C 45-64, D 0-44.</summary>
        public static SupplierGrade FromScore(decimal score) =>
            score >= 85 ? SupplierGrade.A : score >= 65 ? SupplierGrade.B : score >= 45 ? SupplierGrade.C : SupplierGrade.D;
    }
}
