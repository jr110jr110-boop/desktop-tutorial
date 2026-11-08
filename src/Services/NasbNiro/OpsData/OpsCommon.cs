using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entities.Model.NasbNiro.OpsData;

namespace Services.NasbNiro.OpsData
{
    public static class OpsDocTypes
    {
        public const string InventoryDocument = "InventoryDocument";
        public const string StockCount = "StockCount";
        public const string Operation = "MfOperation";
        public const string Inspection = "QcInspection";
        public const string Invoice = "SupplierInvoice";

        public static readonly string[] All = { InventoryDocument, StockCount, Operation, Inspection, Invoice };

        public static string Link(string type, int id)
        {
            string i = id.ToString(CultureInfo.InvariantCulture);
            switch (type)
            {
                case InventoryDocument: return "/inventory/documents/" + i;
                case StockCount: return "/inventory/counts/" + i;
                case Operation: return "/production/operations/" + i;
                case Inspection: return "/inventory/inspections?id=" + i;
                case Invoice: return "/procurement/invoices?id=" + i;
                default: return null;
            }
        }

        public static string Title(string type)
        {
            switch (type)
            {
                case InventoryDocument: return "سند انبار";
                case StockCount: return "شمارش انبار";
                case Operation: return "عملیات تولید";
                case Inspection: return "بازرسی ورودی";
                case Invoice: return "فاکتور تامین‌کننده";
                default: return type;
            }
        }
    }

    public static class OpsRoles
    {
        // who may ask for an exception: the people who enter the data of the unit
        public const string WarehouseMakers = "PMO,WAREHOUSE,WHSUPERVISOR,PLANNING";
        public const string ProductionMakers = "PMO,SHOPSUPERVISOR,PRODUCTIONMANAGER,OPERATOR,ASSEMBLER,ENGINEER,ENGINEERING,QC,WAREHOUSE";
        public const string QualityMakers = "PMO,QC,QA";
        public const string FinanceMakers = "PMO,FINANCIAL,FINANCEMANAGER,COMMERCIAL";

        // the supervisor of the unit decides (PMO is the fallback of every unit); never the person who entered the data, never an AI seat
        public const string WarehouseSupervisors = "WHSUPERVISOR,PMO";
        public const string ProductionSupervisors = "SHOPSUPERVISOR,PRODUCTIONMANAGER,PMO";
        public const string QualitySupervisors = "QA,PMO";
        public const string FinanceSupervisors = "FINANCEMANAGER,PMO";

        /// <summary>Everybody who reads the data-quality page: the makers and supervisors of the four units, planning and the management.</summary>
        public const string Readers = "PMO,CEO,PLANNING,WAREHOUSE,WHSUPERVISOR,SHOPSUPERVISOR,PRODUCTIONMANAGER,QC,QA,FINANCIAL,FINANCEMANAGER,AUDITOR,COMMERCIAL";

        public static string Makers(OpsUnit unit)
        {
            switch (unit)
            {
                case OpsUnit.Production: return ProductionMakers;
                case OpsUnit.Quality: return QualityMakers;
                case OpsUnit.Finance: return FinanceMakers;
                default: return WarehouseMakers;
            }
        }

        public static string Supervisors(OpsUnit unit)
        {
            switch (unit)
            {
                case OpsUnit.Production: return ProductionSupervisors;
                case OpsUnit.Quality: return QualitySupervisors;
                case OpsUnit.Finance: return FinanceSupervisors;
                default: return WarehouseSupervisors;
            }
        }

        public static string UnitTitle(OpsUnit unit)
        {
            switch (unit)
            {
                case OpsUnit.Production: return "تولید";
                case OpsUnit.Quality: return "کنترل کیفیت";
                case OpsUnit.Finance: return "مالی";
                default: return "انبار";
            }
        }

        /// <summary>The roles that see the findings of a unit (the unit, its supervisors, planning and the management).</summary>
        public static string UnitReaders(OpsUnit unit) => Makers(unit) + "," + Supervisors(unit) + ",CEO,PLANNING,AUDITOR";

        public static string StatusTitle(OpsFindingStatus s)
        {
            switch (s)
            {
                case OpsFindingStatus.Resolved: return "رفع شد (سند اصلاحی)";
                case OpsFindingStatus.Verified: return "درستی داده تأیید شد";
                case OpsFindingStatus.Superseded: return "منتفی شد";
                default: return "باز";
            }
        }

        public static string ExceptionStatusTitle(OpsExceptionStatus s)
        {
            switch (s)
            {
                case OpsExceptionStatus.Approved: return "تأییدشده";
                case OpsExceptionStatus.Rejected: return "ردشده";
                case OpsExceptionStatus.Voided: return "باطل‌شده (داده پس از تأیید تغییر کرد)";
                default: return "در انتظار تصمیم سرپرست";
            }
        }
    }

    /// <summary>The identity of a document a rule ran on, with the people who made it (the exception and the verification are never theirs).</summary>
    public sealed class OpsDocRef
    {
        public string Type { get; set; }
        public int Id { get; set; }
        public string Number { get; set; }
        public OpsUnit Unit { get; set; }

        /// <summary>Everybody who created, edited or confirmed the document.</summary>
        public HashSet<int> Makers { get; set; } = new HashSet<int>();

        /// <summary>False for documents the system makes (production receipts, count adjustments, QC releases): their findings are warnings only, so no automatic flow stalls.</summary>
        public bool Blockable { get; set; } = true;

        public bool Confirmed { get; set; }
    }

    public sealed class OpsEvaluation
    {
        public OpsDocRef Doc { get; set; }
        public List<RuleFinding> Findings { get; set; } = new List<RuleFinding>();

        /// <summary>The rule codes this evaluation looks at (null = all): findings of other rules on the same document are not superseded by it.</summary>
        public HashSet<string> Scope { get; set; }
    }
}
