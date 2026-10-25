using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Finance
{
    /// <summary>Standard Iranian manufacturing chart of accounts (★ provisional; finance edits it) and the role-to-account mapping.</summary>
    public static class FinanceSeed
    {
        public const string Iran = "IR";

        private static readonly (string Code, string Title)[] Groups =
        {
            ("1", "دارایی‌های جاری"), ("2", "دارایی‌های غیرجاری"), ("3", "بدهی‌های جاری"), ("4", "بدهی‌های غیرجاری"),
            ("5", "حقوق مالکانه"), ("6", "درآمدها"), ("7", "بهای تمام‌شده"), ("8", "هزینه‌ها"), ("9", "حساب‌های انتظامی"),
        };

        private static readonly (string Code, string Title)[] Generals =
        {
            ("101", "موجودی نقد"), ("102", "سرمایه‌گذاری‌های کوتاه‌مدت"), ("103", "دریافتنی‌های تجاری"), ("104", "سایر دریافتنی‌ها"),
            ("105", "موجودی مواد و کالا"), ("106", "پیش‌پرداخت‌ها"), ("107", "قراردادهای در جریان"),
            ("201", "دارایی‌های ثابت مشهود"), ("202", "استهلاک انباشته"), ("203", "دارایی‌های نامشهود"), ("204", "استهلاک انباشته نامشهود"),
            ("205", "سرمایه‌گذاری بلندمدت و سپرده‌ها"),
            ("301", "پرداختنی‌های تجاری"), ("302", "سایر پرداختنی‌ها"), ("303", "پیش‌دریافت‌ها"), ("304", "ذخایر"), ("305", "تسهیلات مالی کوتاه‌مدت"),
            ("401", "تسهیلات بلندمدت"), ("402", "ذخیره مزایای پایان خدمت کارکنان"),
            ("501", "سرمایه"), ("502", "اندوخته قانونی"), ("503", "سود (زیان) انباشته"), ("504", "تعدیلات سنواتی"), ("509", "خلاصه سود و زیان (موقت)"),
            ("601", "درآمد قراردادها و فروش"), ("602", "سایر درآمدهای عملیاتی"), ("603", "درآمدهای غیرعملیاتی"),
            ("701", "بهای تمام‌شده درآمد"), ("702", "سربار ساخت (هزینه‌های تولید)"),
            ("801", "هزینه‌های اداری، عمومی و فروش"), ("802", "هزینه‌های مالی"), ("803", "سایر هزینه‌های غیرعملیاتی"), ("804", "مالیات بر درآمد"),
            ("901", "حساب‌های انتظامی"), ("902", "طرف حساب‌های انتظامی"),
        };

        private static readonly (string Code, string Title)[] Subsidiaries =
        {
            ("10101", "صندوق"), ("10102", "بانک‌ها (ریالی)"), ("10103", "بانک‌ها (ارزی)"), ("10104", "تنخواه‌گردان‌ها"), ("10105", "وجوه در راه"),
            ("10201", "سپرده‌های کوتاه‌مدت بانکی"),
            ("10301", "حساب‌های دریافتنی تجاری"), ("10302", "اسناد دریافتنی (چک‌های در جریان)"), ("10303", "چک‌های در جریان وصول"), ("10304", "چک‌های برگشتی"),
            ("10305", "کسور حسن انجام کار دریافتنی (سپرده نزد کارفرما)"), ("10308", "مطالبات تکمیل‌نشده (درآمد شناسایی‌شده فاقد صورت‌حساب)"), ("10309", "ذخیره مطالبات مشکوک‌الوصول (کاهنده)"), ("10306", "مالیات تکلیفی کسرشده توسط کارفرما"), ("10307", "سپرده بیمه کسرشده توسط کارفرما"),
            ("10401", "مساعده و وام کارکنان"), ("10402", "سپرده‌ها و ودایع"), ("10403", "مالیات بر ارزش افزوده خرید"), ("10404", "سایر"),
            ("10501", "موجودی متریال"), ("10502", "قطعات و ریخته‌گری"), ("10503", "محصول ساخته‌شده"), ("10504", "مدل"), ("10505", "ملزومات مصرفی"),
            ("10507", "ابزارآلات"), ("10508", "قطعات استاندارد"), ("10512", "ابزار دقیق"), ("10520", "کالای در جریان ساخت"), ("10530", "کالای در راه"),
            ("10601", "پیش‌پرداخت خرید"), ("10602", "پیش‌پرداخت مالیات"), ("10603", "پیش‌پرداخت بیمه"), ("10604", "سایر"),
            ("10701", "مخارج قراردادهای در جریان پیشرفت"),
            ("20101", "زمین"), ("20102", "ساختمان"), ("20103", "ماشین‌آلات و تجهیزات"), ("20104", "تاسیسات"), ("20105", "وسایط نقلیه"),
            ("20106", "اثاثه و منصوبات"), ("20107", "ابزارآلات (سرمایه‌ای)"), ("20108", "دارایی در جریان تکمیل"), ("20109", "پیش‌پرداخت سرمایه‌ای"),
            ("20202", "ساختمان"), ("20203", "ماشین‌آلات و تجهیزات"), ("20204", "تاسیسات"), ("20205", "وسایط نقلیه"), ("20206", "اثاثه و منصوبات"), ("20207", "ابزارآلات"),
            ("20301", "نرم‌افزار"), ("20302", "حق اختراع"), ("20303", "سرقفلی"),
            ("20401", "استهلاک انباشته نامشهود"),
            ("20501", "سپرده‌های بلندمدت"), ("20502", "سپرده ضمانت‌نامه‌ها"),
            ("30101", "حساب‌های پرداختنی تامین‌کنندگان"), ("30102", "اسناد پرداختنی (چک‌های صادره)"), ("30103", "کالای دریافتی فاکتورنشده"),
            ("30201", "حقوق و دستمزد پرداختنی"), ("30202", "حق بیمه پرداختنی"), ("30203", "مالیات حقوق پرداختنی"), ("30204", "مالیات تکلیفی پرداختنی"),
            ("30205", "مالیات بر ارزش افزوده فروش"), ("30206", "سپرده‌های دریافتی از پیمانکاران (حسن انجام کار)"), ("30207", "سپرده بیمه پیمانکاران"), ("30208", "سایر"),
            ("30301", "پیش‌دریافت از کارفرمایان"), ("30303", "صورت‌حساب مازاد بر پیشرفت (درآمد انتقالی قراردادها)"),
            ("30401", "ذخیره مالیات بر درآمد"), ("30404", "ذخیره زیان مورد انتظار قراردادها"), ("30405", "ذخایر عمومی (ضمانت، دعاوی، سایر)"), ("30406", "ذخیره تضمین محصولات"), ("30402", "ذخیره عیدی و پاداش"), ("30403", "ذخیره مرخصی"),
            ("30501", "تسهیلات مالی کوتاه‌مدت"),
            ("40101", "تسهیلات بلندمدت"), ("40201", "ذخیره مزایای پایان خدمت کارکنان"),
            ("50101", "سرمایه"), ("50201", "اندوخته قانونی"), ("50301", "سود (زیان) انباشته"), ("50401", "تعدیلات سنواتی"), ("50901", "خلاصه سود و زیان"),
            ("60101", "درآمد قراردادهای ساخت"), ("60102", "فروش قطعات و خدمات"), ("60103", "برگشت از فروش و تخفیفات"),
            ("60201", "فروش ضایعات"),
            ("60301", "سود سپرده"), ("60302", "سود فروش دارایی"), ("60303", "سود تسعیر ارز"), ("60304", "سایر"), ("60305", "جرائم دیرکرد کسرشده از تامین‌کنندگان"),
            ("70101", "مواد مستقیم"), ("70102", "دستمزد مستقیم"), ("70103", "سربار ساخت"), ("70104", "پیمانکاری و خدمات خریداری‌شده"),
            ("70105", "بهای تمام‌شده ضایعات فروخته‌شده"), ("70106", "اضافات و کسورات انبار"), ("70107", "تعدیل ارزش موجودی"), ("70108", "اختلاف قیمت خرید"), ("70109", "انحراف بهای تمام‌شده"), ("70110", "سربار جذب‌شده"), ("70111", "انحراف جذب سربار"), ("70112", "هزینه زیان مورد انتظار قراردادها"),
            ("70113", "دستمزد جذب‌شده خدمات پس از فروش"), // ★ new in S1 part B (assumption: the accountant confirms the code); 70102 stays the production direct labour
            ("70201", "حقوق و دستمزد غیرمستقیم"), ("70202", "مواد غیرمستقیم و مصرفی"), ("70203", "استهلاک"), ("70204", "تعمیر و نگهداری"), ("70205", "انرژی"), ("70206", "سایر"),
            ("80101", "حقوق و دستمزد"), ("80102", "بیمه سهم کارفرما"), ("80103", "مزایای پایان خدمت"), ("80104", "استهلاک"), ("80105", "اجاره"),
            ("80106", "حمل و نقل"), ("80107", "خدمات حرفه‌ای"), ("80108", "سایر"), ("80109", "هزینه گارانتی"),
            ("80201", "کارمزد بانکی"), ("80202", "سود تسهیلات"),
            ("80301", "زیان فروش دارایی"), ("80302", "زیان تسعیر ارز"), ("80303", "جرائم و خسارات"), ("80304", "هزینه مطالبات مشکوک‌الوصول"),
            ("80401", "مالیات بر درآمد"),
            ("90101", "ضمانت‌نامه‌های صادره به نفع دیگران"), ("90102", "چک‌های تضمینی دریافتی"),
            ("90201", "طرف حساب‌های انتظامی (ضمانت‌نامه‌ها)"), ("90202", "طرف حساب چک‌های تضمینی"),
        };

        private static readonly Dictionary<string, string> StatementOfGeneral = new Dictionary<string, string>
        {
            { "101", "BS.CASH" }, { "102", "BS.STI" }, { "103", "BS.TREC" }, { "104", "BS.OREC" }, { "105", "BS.STOCK" }, { "106", "BS.PREP" }, { "107", "BS.WIPC" },
            { "201", "BS.PPE" }, { "202", "BS.ACCDEP" }, { "203", "BS.INTG" }, { "204", "BS.INTGDEP" }, { "205", "BS.LTI" },
            { "301", "BS.TPAY" }, { "302", "BS.OPAY" }, { "303", "BS.ADV" }, { "304", "BS.PROV" }, { "305", "BS.STFIN" },
            { "401", "BS.LTFIN" }, { "402", "BS.EOSB" },
            { "501", "BS.CAP" }, { "502", "BS.RES" }, { "503", "BS.RE" }, { "504", "BS.ADJ" }, { "509", "BS.SUMMARY" },
            { "601", "IS.REV" }, { "602", "IS.OOI" }, { "603", "IS.NONOPI" },
            { "701", "IS.COGS" }, { "702", "IS.COGS" },
            { "801", "IS.ADM" }, { "802", "IS.FIN" }, { "803", "IS.NONOPE" }, { "804", "IS.TAX" },
        };

        /// <summary>Statement line codes with titles and the section they belong to (Iranian Accounting Standard 1 layout ★).</summary>
        public static readonly (string Code, string Title, string Section)[] StatementLines =
        {
            ("BS.CASH", "موجودی نقد", "دارایی‌های جاری"), ("BS.STI", "سرمایه‌گذاری‌های کوتاه‌مدت", "دارایی‌های جاری"),
            ("BS.TREC", "دریافتنی‌های تجاری", "دارایی‌های جاری"), ("BS.OREC", "سایر دریافتنی‌ها", "دارایی‌های جاری"),
            ("BS.STOCK", "موجودی مواد و کالا", "دارایی‌های جاری"), ("BS.PREP", "پیش‌پرداخت‌ها", "دارایی‌های جاری"), ("BS.WIPC", "قراردادهای در جریان", "دارایی‌های جاری"),
            ("BS.PPE", "دارایی‌های ثابت مشهود", "دارایی‌های غیرجاری"), ("BS.ACCDEP", "استهلاک انباشته", "دارایی‌های غیرجاری"),
            ("BS.INTG", "دارایی‌های نامشهود", "دارایی‌های غیرجاری"), ("BS.INTGDEP", "استهلاک انباشته نامشهود", "دارایی‌های غیرجاری"),
            ("BS.LTI", "سرمایه‌گذاری بلندمدت و سپرده‌ها", "دارایی‌های غیرجاری"),
            ("BS.TPAY", "پرداختنی‌های تجاری", "بدهی‌های جاری"), ("BS.OPAY", "سایر پرداختنی‌ها", "بدهی‌های جاری"), ("BS.ADV", "پیش‌دریافت‌ها", "بدهی‌های جاری"),
            ("BS.PROV", "ذخایر", "بدهی‌های جاری"), ("BS.STFIN", "تسهیلات مالی کوتاه‌مدت", "بدهی‌های جاری"),
            ("BS.LTFIN", "تسهیلات بلندمدت", "بدهی‌های غیرجاری"), ("BS.EOSB", "ذخیره مزایای پایان خدمت کارکنان", "بدهی‌های غیرجاری"),
            ("BS.CAP", "سرمایه", "حقوق مالکانه"), ("BS.RES", "اندوخته قانونی", "حقوق مالکانه"), ("BS.RE", "سود (زیان) انباشته", "حقوق مالکانه"),
            ("BS.ADJ", "تعدیلات سنواتی", "حقوق مالکانه"), ("BS.SUMMARY", "خلاصه سود و زیان", "حقوق مالکانه"),
            ("IS.REV", "درآمد عملیاتی", "صورت سود و زیان"), ("IS.COGS", "بهای تمام‌شده درآمد", "صورت سود و زیان"),
            ("IS.ADM", "هزینه‌های اداری، عمومی و فروش", "صورت سود و زیان"), ("IS.OOI", "سایر درآمدهای عملیاتی", "صورت سود و زیان"),
            ("IS.FIN", "هزینه‌های مالی", "صورت سود و زیان"), ("IS.NONOPI", "درآمدهای غیرعملیاتی", "صورت سود و زیان"),
            ("IS.NONOPE", "هزینه‌های غیرعملیاتی", "صورت سود و زیان"), ("IS.TAX", "مالیات بر درآمد", "صورت سود و زیان"),
        };

        private const string Dx = "Project,WorkOrder,CostCenter";

        private static (string D1, string D2, bool Req, bool Currency) DetailPolicy(string code)
        {
            switch (code)
            {
                case "10101": return ("CashBox", "", false, false);
                case "10102": return ("Bank", "", false, false);
                case "10103": return ("Bank", "", false, true);
                case "10104": return ("PettyCash", "", false, false);
                case "10105": return ("Bank,CashBox,Other", "", false, false);
                case "10301": return ("Customer", Dx, true, true);
                case "10302": case "10303": case "10304": return ("Customer,Supplier,Person", Dx, false, false);
                case "10305": case "10306": case "10307": return ("Customer", Dx, false, false);
                case "10401": return ("Employee", "", false, false);
                case "10402": case "10404": return ("Supplier,Customer,Person,Other", "", false, false);
                case "10403": return ("Supplier,Other", "", false, false);
                case "10601": case "10602": case "10603": case "10604": return ("Supplier,Person", Dx, false, false);
                case "10701": return ("Customer", "Project,WorkOrder", false, false);
                case "30101": return ("Supplier", Dx, true, true);
                case "30102": return ("Supplier,Person,Employee,Other", "", false, false);
                case "30103": return ("Supplier", Dx, false, false);
                case "30201": return ("Employee,Bank", "", false, false);
                case "30204": case "30206": case "30207": return ("Supplier,Person", "", false, false);
                case "30208": return ("Supplier,Person,Employee,Customer,Other", "", false, false);
                case "30301": return ("Customer", Dx, false, false);
            }
            string g = code.Substring(0, 3);
            if (g == "102" || g == "205" || g == "305" || g == "401") return ("Bank,Other", "", false, false);
            if (g == "105") return ("", Dx, false, false);
            if (g == "201" || g == "202" || g == "203" || g == "204") return ("Asset", "CostCenter", false, false);
            char c = code[0];
            if (c == '6') return ("Customer", Dx, false, false);
            if (c == '7' || c == '8') return ("Supplier,Person,Other", Dx, false, false);
            if (c == '9') return ("Customer,Supplier,Person,Other", "Project", false, false);
            return ("Other", "", false, false);
        }

        internal static FnAccountType TypeOf(string code)
        {
            switch (code[0])
            {
                case '1': case '2': return FnAccountType.Asset;
                case '3': case '4': return FnAccountType.Liability;
                case '5': return FnAccountType.Equity;
                case '6': return FnAccountType.Revenue;
                case '7': case '8': return FnAccountType.Expense;
                default: return FnAccountType.Memorandum;
            }
        }

        internal static FnAccountNature NatureOf(string code)
        {
            if (code.StartsWith("202") || code.StartsWith("204") || code == "10309") return FnAccountNature.Credit;
            if (code == "60103" || code == "10304") return FnAccountNature.Debit;
            if (code.StartsWith("503") || code.StartsWith("504") || code.StartsWith("509") || code == "70106" || code == "70107" || code == "70108") return FnAccountNature.Either;
            switch (code[0])
            {
                case '1': case '2': case '7': case '8': return FnAccountNature.Debit;
                case '3': case '4': case '5': case '6': return FnAccountNature.Credit;
                default: return FnAccountNature.Either;
            }
        }

        /// <summary>Role to ledger account (subsidiary code). Roles that are not listed fall back to their base role (Inventory.09 -> Inventory).</summary>
        public static readonly Dictionary<string, string> RoleAccounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { AccountRoles.Inventory, "10505" }, { AccountRoles.Inventory + ".01", "10501" }, { AccountRoles.Inventory + ".02", "10502" },
            { AccountRoles.Inventory + ".03", "10503" }, { AccountRoles.Inventory + ".04", "10504" }, { AccountRoles.Inventory + ".05", "10505" },
            { AccountRoles.Inventory + ".07", "10507" }, { AccountRoles.Inventory + ".08", "10508" }, { AccountRoles.Inventory + ".12", "10512" },
            { AccountRoles.Grni, "30103" }, { AccountRoles.Wip, "10520" }, { AccountRoles.MaterialExpense, "70202" }, { AccountRoles.Cogs, "70101" },
            { AccountRoles.InventoryGain, "70106" }, { AccountRoles.InventoryLoss, "70106" },
            { AccountRoles.OpeningBalance, "50301" }, { AccountRoles.ScrapRecovery, "10505" }, { AccountRoles.ScrapCost, "70105" }, { AccountRoles.ScrapRevenue, "60201" },
            { AccountRoles.Bank, "10102" }, { AccountRoles.Revaluation, "70107" }, { AccountRoles.AccountsPayable, "30101" },
            { AccountRoles.PurchasePriceVariance, "70108" }, { AccountRoles.Vat, "10403" }, { AccountRoles.SupplierAdvance, "10601" },
            { AccountRoles.PurchasedServices, "70104" }, { AccountRoles.SupplierPenalty, "60305" },
            // release F1
            { AccountRoles.Cash, "10101" }, { AccountRoles.BankForeign, "10103" }, { AccountRoles.PettyCash, "10104" }, { AccountRoles.FundsInTransit, "10105" },
            { AccountRoles.AccountsReceivable, "10301" }, { AccountRoles.ChequesReceivable, "10302" }, { AccountRoles.ChequesInCollection, "10303" },
            { AccountRoles.ChequesBounced, "10304" }, { AccountRoles.RetentionReceivable, "10305" }, { AccountRoles.WithholdingReceivable, "10306" },
            { AccountRoles.InsuranceRetentionReceivable, "10307" }, { AccountRoles.EmployeeAdvance, "10401" },
            { AccountRoles.ChequesPayable, "30102" }, { AccountRoles.WithholdingPayable, "30204" }, { AccountRoles.VatSales, "30205" },
            { AccountRoles.PerformanceRetentionPayable, "30206" }, { AccountRoles.InsuranceRetentionPayable, "30207" }, { AccountRoles.OtherPayable, "30208" },
            { AccountRoles.CustomerAdvance, "30301" },
            { AccountRoles.RevenueContracts, "60101" }, { AccountRoles.RevenueSales, "60102" }, { AccountRoles.SalesReturns, "60103" },
            { AccountRoles.InterestIncome, "60301" }, { AccountRoles.FxGain, "60303" }, { AccountRoles.BankFees, "80201" }, { AccountRoles.FxLoss, "80302" },
            { AccountRoles.GuaranteeIssued, "90101" }, { AccountRoles.GuaranteeChequesReceived, "90102" },
            { AccountRoles.GuaranteeIssuedContra, "90201" }, { AccountRoles.GuaranteeChequesContra, "90202" },
            // release F2 seams
            { AccountRoles.PayrollPayable, "30201" }, { AccountRoles.InsurancePayable, "30202" }, { AccountRoles.PayrollTaxPayable, "30203" },
            { AccountRoles.PayrollExpenseAdmin, "80101" }, { AccountRoles.PayrollExpenseIndirect, "70201" }, { AccountRoles.PayrollExpenseDirect, "70102" },
            { AccountRoles.EmployerInsuranceAdmin, "80102" }, { AccountRoles.EmployerInsuranceProduction, "70201" },
            { AccountRoles.SeveranceExpense, "80103" }, { AccountRoles.SeveranceProvision, "40201" }, { AccountRoles.BonusProvision, "30402" }, { AccountRoles.LeaveProvision, "30403" },
            { AccountRoles.DepreciationProduction, "70203" }, { AccountRoles.DepreciationAdmin, "80104" },
            { AccountRoles.AssetGainOnDisposal, "60302" }, { AccountRoles.AssetLossOnDisposal, "80301" },
            { AccountRoles.AssetUnderConstruction, "20108" }, { AccountRoles.CapitalAdvance, "20109" },
            // release P1 (production): absorbed direct labour (70102) and machine overhead (70103) against work in progress (10520); finance reconciles absorbed against actual each month
            { AccountRoles.LabourAbsorbed, "70102" }, { AccountRoles.OverheadAbsorbed, "70103" },
            // release S1 part B (after-sales) ★ assumptions for the accountant to confirm:
            //  - the warranty provision is 30406 «ذخیره تضمین محصولات»: the brief said 30404, but 30404 already holds the provision for expected contract losses (GlService.Costing). The alternative is 30405 «ذخایر عمومی (ضمانت، دعاوی، سایر)», which the provision register (GlService.F3b.Notes) already lists.
            //  - the warranty expense (goodwill) is 80109 «هزینه گارانتی»
            //  - the labour of an after-sales order is absorbed into the new 70113, not into the production labour 70102
            { AccountRoles.WarrantyProvision, "30406" }, { AccountRoles.WarrantyExpense, "80109" }, { AccountRoles.ServiceLabourAbsorbed, "70113" },
        };

        /// <summary>The few long-standing statutory ratios that are seeded (each with its source and Verify = true).</summary>
        public static IEnumerable<FnTaxRate> TaxRateSeeds()
        {
            var from = new DateTime(2000, 1, 1);
            yield return new FnTaxRate { Kind = FnTaxKind.WithholdingContract, Rate = 3m, EffectiveFrom = from, Jurisdiction = Iran, Verify = true, Source = "قانون مالیات‌های مستقیم ماده ۱۰۴ — توسط مالی تایید شود" };
            yield return new FnTaxRate { Kind = FnTaxKind.InsuranceRetention, Rate = 5m, EffectiveFrom = from, Jurisdiction = Iran, Verify = true, Source = "قانون تامین اجتماعی ماده ۳۸ — توسط مالی تایید شود" };
            yield return new FnTaxRate { Kind = FnTaxKind.PerformanceRetention, Rate = 10m, EffectiveFrom = from, Jurisdiction = Iran, Verify = true, Source = "پیش‌فرض قراردادها (قابل ویرایش برای هر قرارداد) — توسط مالی تایید شود" };
        }

        public const string SettingCloseEnforce = "CloseChecklist.Enforce";
        public const string SettingParallelMonths = "ParallelRun.RequiredMonths";
        public const string SettingSepidarExport = "Finance:SepidarFullExport";
        public const string SettingFxRateType = "Fx.RateType";
        public const string SettingFxAutoReverse = "Fx.AutoReverse";
        public const string SettingPayableTerms = "Payables.DefaultTermsDays";

        public static IEnumerable<FnSetting> SettingSeeds()
        {
            yield return new FnSetting { Key = SettingPayableTerms, Value = "30", Title = "مهلت پرداخت پیش‌فرض (روز) وقتی شرایط پرداخت سفارش عدد ندارد ★" };
            yield return new FnSetting { Key = "BaseCurrency", Value = "IRR", Title = "ارز پایه شرکت" };
            yield return new FnSetting { Key = "Jurisdiction", Value = Iran, Title = "حوزه قضایی (قوانین مالی و مالیاتی)" };
            yield return new FnSetting { Key = "MatchDays", Value = "3", Title = "بازه روز برای تطبیق خودکار صورت‌حساب بانکی (★)" };
            yield return new FnSetting { Key = SettingCloseEnforce, Value = "0", Title = "الزام فهرست کنترل بستن دوره برای قفل کردن دوره (۱ = فعال؛ پیش از راه‌اندازی خاموش است)" };
            yield return new FnSetting { Key = SettingSepidarExport, Value = "0", Title = "خروجی کامل اسناد برای سپیدار (جایگزین؛ پیش‌فرض خاموش است و مسیر صف خروجی F2 فعال می‌ماند)" };
            yield return new FnSetting { Key = SettingFxRateType, Value = "1", Title = "نوع نرخ ارز برای تسعیر پایان دوره (۱ رسمی، ۲ توافقی، ۳ آزاد) ★" };
            yield return new FnSetting { Key = SettingFxAutoReverse, Value = "1", Title = "برگشت خودکار سند تسعیر در روز اول دوره بعد (۱ = فعال) ★" };
            yield return new FnSetting { Key = SettingParallelMonths, Value = "3", Title = "تعداد ماه‌های متوالی بدون مغایرت توضیح‌نشده برای راه‌اندازی (★)" };
        }

        // ------------------------------------------------------------------------------------------ persistence

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static volatile bool _done;

        /// <summary>Idempotent: settings, chart, role mapping, statutory ratios and the fiscal year of today.</summary>
        public static async Task EnsureAsync(IUnitOfWork uow, CancellationToken ct)
        {
            if (_done) return;
            await Gate.WaitAsync(ct);
            try
            {
                if (_done) return;
                await EnsureLegalEntitiesAsync(uow, ct);
                await EnsureSettingsAsync(uow, ct);
                await EnsureChartAsync(uow, ct);
                await EnsureMappingAsync(uow, ct);
                await EnsureTaxRatesAsync(uow, ct);
                await EnsureFiscalYearAsync(uow, DateTime.Today, ct);
                _done = true;
            }
            finally { Gate.Release(); }
        }

        /// <summary>The main company (entity 1). Other entities are created by the finance manager.</summary>
        public const int MainEntityId = 1;

        public static async Task EnsureLegalEntitiesAsync(IUnitOfWork uow, CancellationToken ct)
        {
            if (await uow.Set<FnLegalEntity>().AnyAsync(ct)) return;
            uow.Set<FnLegalEntity>().Add(new FnLegalEntity
            {
                Code = "MAIN", Name = "شرکت اصلی", Jurisdiction = Iran, BaseCurrency = "IRR", IsActive = true, CreatedAt = DateTimeOffset.Now,
            });
            await uow.SaveChangesAsync(ct);
        }

        public static async Task EnsureSettingsAsync(IUnitOfWork uow, CancellationToken ct)
        {
            HashSet<string> keys = (await uow.Set<FnSetting>().Select(s => s.Key).ToListAsync(ct)).ToHashSet();
            bool added = false;
            foreach (FnSetting s in SettingSeeds())
                if (!keys.Contains(s.Key)) { uow.Set<FnSetting>().Add(s); added = true; }
            if (added) await uow.SaveChangesAsync(ct);
        }

        public static async Task EnsureChartAsync(IUnitOfWork uow, CancellationToken ct)
        {
            Dictionary<string, FnAccount> existing = await uow.Set<FnAccount>().ToDictionaryAsync(a => a.Code, ct);
            bool added = false;
            FnAccount Add(string code, string title, FnAccountLevel level, FnAccount parent)
            {
                if (existing.TryGetValue(code, out FnAccount found)) return found;
                var detail = level == FnAccountLevel.Subsidiary ? DetailPolicy(code) : ("", "", false, false);
                var a = new FnAccount
                {
                    Code = code, Title = title, Level = level, Parent = parent, Type = TypeOf(code), Nature = NatureOf(code),
                    Temporary = (code[0] == '6' || code[0] == '7' || code[0] == '8' || code == "50901"),
                    DetailTypes = detail.Item1, Detail2Types = detail.Item2, RequiresDetail = detail.Item3, CurrencyAllowed = detail.Item4, IsMonetaryForeign = detail.Item4,
                    StatementLine = StatementOfGeneral.TryGetValue(code.Length >= 3 ? code.Substring(0, 3) : code, out string line) ? line : null,
                    IsActive = true, Jurisdiction = Iran,
                };
                uow.Set<FnAccount>().Add(a);
                existing[code] = a;
                added = true;
                return a;
            }
            foreach (var g in Groups) Add(g.Code, g.Title, FnAccountLevel.Group, null);
            foreach (var g in Generals) Add(g.Code, g.Title, FnAccountLevel.General, existing[g.Code.Substring(0, 1)]);
            foreach (var s in Subsidiaries) Add(s.Code, s.Title, FnAccountLevel.Subsidiary, existing[s.Code.Substring(0, 3)]);
            if (added) await uow.SaveChangesAsync(ct);
        }

        public static async Task EnsureMappingAsync(IUnitOfWork uow, CancellationToken ct)
        {
            Dictionary<string, int> accounts = await uow.Set<FnAccount>().Where(a => a.Level == FnAccountLevel.Subsidiary).ToDictionaryAsync(a => a.Code, a => a.Id, ct);
            List<AccountMapping> mappings = await uow.Set<AccountMapping>().ToListAsync(ct);
            bool changed = false;
            foreach (var (role, title) in AccountRoles.Standard())
            {
                AccountMapping m = mappings.FirstOrDefault(x => string.Equals(x.Role, role, StringComparison.OrdinalIgnoreCase));
                if (m == null)
                {
                    m = new AccountMapping { Role = role, Title = title, IsActive = true };
                    uow.Set<AccountMapping>().Add(m);
                    mappings.Add(m);
                    changed = true;
                }
                if (m.AccountId == null && RoleAccounts.TryGetValue(role, out string code) && accounts.TryGetValue(code, out int accountId))
                {
                    m.AccountId = accountId;
                    changed = true;
                }
            }
            if (changed) await uow.SaveChangesAsync(ct);
        }

        public static async Task EnsureTaxRatesAsync(IUnitOfWork uow, CancellationToken ct)
        {
            List<FnTaxKind> kinds = await uow.Set<FnTaxRate>().Select(r => r.Kind).Distinct().ToListAsync(ct);
            bool added = false;
            foreach (FnTaxRate r in TaxRateSeeds())
            {
                if (kinds.Contains(r.Kind)) continue;
                r.CreatedAt = DateTimeOffset.Now;
                uow.Set<FnTaxRate>().Add(r);
                added = true;
            }
            if (added) await uow.SaveChangesAsync(ct);
        }

        // ------------------------------------------------------------------------------------------ Persian calendar

        private static readonly PersianCalendar Persian = new PersianCalendar();

        public static int PersianYearOf(DateTime date) => Persian.GetYear(date);

        public static DateTime PersianDate(int year, int month, int day) => Persian.ToDateTime(year, month, day, 0, 0, 0, 0);

        public static DateTime YearStart(int year) => PersianDate(year, 1, 1);

        public static DateTime YearEnd(int year) => PersianDate(year + 1, 1, 1).AddDays(-1);

        public static readonly string[] MonthNames =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
        };

        /// <summary>The number of the fiscal year containing the date (through the fiscal calendar).</summary>
        public static int YearNumberOf(FnLegalEntity entity, DateTime date) => FiscalCalendars.Current.YearOf(date);

        /// <summary>Creates the Persian fiscal year of the main company containing the date with its 12 periods (idempotent). Saves.</summary>
        public static Task<FnFiscalYear> EnsureFiscalYearAsync(IUnitOfWork uow, DateTime date, CancellationToken ct) =>
            EnsureEntityYearAsync(uow, null, date, ct);

        /// <summary>Creates the fiscal year of the legal entity containing the date with its 12 periods (idempotent). Saves. Null entity = the main company.</summary>
        public static async Task<FnFiscalYear> EnsureEntityYearAsync(IUnitOfWork uow, FnLegalEntity entity, DateTime date, CancellationToken ct)
        {
            int entityId = entity?.Id ?? MainEntityId;
            int year = YearNumberOf(entity, date);
            FnFiscalYear found = await uow.Set<FnFiscalYear>().Include(y => y.Periods).FirstOrDefaultAsync(y => y.LegalEntityId == entityId && y.Year == year, ct);
            if (found != null) return found;
            await YearGate.WaitAsync(ct);
            try
            {
                found = await uow.Set<FnFiscalYear>().Include(y => y.Periods).FirstOrDefaultAsync(y => y.LegalEntityId == entityId && y.Year == year, ct);
                if (found != null) return found;
                return await CreateYearAsync(uow, entity, entityId, year, ct);
            }
            finally { YearGate.Release(); }
        }

        private static readonly SemaphoreSlim YearGate = new SemaphoreSlim(1, 1);

        private static async Task<FnFiscalYear> CreateYearAsync(IUnitOfWork uow, FnLegalEntity entity, int entityId, int year, CancellationToken ct)
        {
            IFiscalCalendar cal = FiscalCalendars.Current;
            var fy = new FnFiscalYear
            {
                Year = year, Title = year.ToString(CultureInfo.InvariantCulture), StartDate = cal.YearStart(year), EndDate = cal.YearEnd(year),
                Status = FnYearStatus.Open, Jurisdiction = entity?.Jurisdiction ?? Iran, LegalEntityId = entityId, Periods = new List<FnPeriod>(),
            };
            for (int m = 1; m <= 12; m++)
                fy.Periods.Add(new FnPeriod { Number = m, Title = cal.PeriodTitle(year, m), StartDate = cal.PeriodStart(year, m), EndDate = cal.PeriodEnd(year, m), Status = FnPeriodStatus.Open });
            uow.Set<FnFiscalYear>().Add(fy);
            await uow.SaveChangesAsync(ct);
            return fy;
        }
    }
}
