namespace Services.NasbNiro.ContractsSystem
{
    /// <summary>PB6: roles of the contracts system, the progress statements and the petty-cash flow. One place, so the controllers, the services and the attachment rules agree.</summary>
    public static class CtRoles
    {
        public const string Readers = "PMO,CEO,PLANNING,COMMERCIAL,SALES,PROJECTMANAGER,FINANCIAL,FINANCEMANAGER,ENGINEERING,AUDITOR";
        /// <summary>Who may ask for a deal (the managers of the units; commercial prepares the paper work).</summary>
        public const string Requesters = "PMO,PLANNING,COMMERCIAL,SALES,PROJECTMANAGER,ENGINEERING,FINANCIAL,FINANCEMANAGER,PRODUCTIONMANAGER,SERVICE";
        public const string Commercial = "COMMERCIAL,PMO";
        public const string Management = "CEO";
        public const string Planning = "PLANNING,PMO";
        public const string FinanceManager = "FINANCEMANAGER,PMO";
        public const string Legal = "COMMERCIAL";
    }

    public static class PsRoles
    {
        public const string Readers = "PMO,CEO,PLANNING,COMMERCIAL,PROJECTMANAGER,FINANCIAL,FINANCEMANAGER,TREASURY,QC,ENGINEERING,AUDITOR";
        /// <summary>The commercial unit prepares the statement.</summary>
        public const string Preparers = "COMMERCIAL";
        public const string TechnicalOffice = "ENGINEERING,PLANNING";
        public const string Supervision = "QC,CONSTRUCTION";
        public const string PaymentRecorders = "FINANCIAL,FINANCEMANAGER,TREASURY";
        public const string RateEditors = "FINANCIAL,FINANCEMANAGER";
        public const string RateVerifiers = "FINANCEMANAGER,PMO,CEO";
    }

    public static class PettyFlowRoles
    {
        public const string Readers = "PMO,CEO,FINANCIAL,FINANCEMANAGER,TREASURY,AUDITOR";
        /// <summary>Accounting control.</summary>
        public const string Accounting = "FINANCIAL";
        /// <summary>Finance manager approval.</summary>
        public const string FinanceManager = "FINANCEMANAGER";
        public const string Ceo = "CEO";
        public const string Treasury = "TREASURY,FINANCEMANAGER";
    }
}

namespace Services.NasbNiro.ContractsSystem
{
    /// <summary>PB6 document number kinds (the band 741..760; 741 and 742 are in <c>ProcurementNumbers</c>).</summary>
    public static class Pb6Numbers
    {
        public const int DealRequest = 743;
        public const int TenderRun = 744;
        public const int ContractDraft = 745;
        public const int Statement = 746;
        public const int PettyRequest = 747;
        public const int PettySummary = 748;
    }
}
