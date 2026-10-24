using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.FinanceDtos
{
    /// <summary>One screen for the CEO: money, stock, procurement, quality and the open approvals of the whole company (rial).</summary>
    public class CeoDashboardDto
    {
        public DateTimeOffset GeneratedAt { get; set; }

        // stock
        public decimal StockValue { get; set; }
        public int ItemsBelowMinimum { get; set; }
        public int QuarantineLines { get; set; }

        // procurement and payables
        public int OpenPurchaseRequests { get; set; }
        public int PurchaseRequestsWaitingApproval { get; set; }
        public int OpenPurchaseOrders { get; set; }
        public decimal OpenPurchaseOrdersAmount { get; set; }
        public int LateOrderLines { get; set; }
        /// <summary>Goods and services received but not invoiced (GRNI balance).</summary>
        public decimal ReceivedNotInvoiced { get; set; }
        public int InvoicesWaitingApproval { get; set; }
        /// <summary>Approved supplier invoices not yet paid.</summary>
        public decimal PayablesOpen { get; set; }
        public int PaymentsWaitingApproval { get; set; }
        public decimal PaymentsWaitingAmount { get; set; }
        public decimal PaidThisMonth { get; set; }
        public int BlockedSuppliers { get; set; }
        public int SupplierEvaluationsDue { get; set; }

        // quality and production support
        public int PendingInspections { get; set; }
        public int OpenNcrs { get; set; }
        public int OpenMaterialRequests { get; set; }
        public int DocumentsWaitingApproval { get; set; }
        public int OverdueCapas { get; set; }
        public int CalibrationOverdue { get; set; }
        public int CalibrationDue30 { get; set; }
        public int PendingHPoints { get; set; }
        public int QualificationsLapsing { get; set; }
        public int ComplaintsOverdue { get; set; }
        public int WitnessNoticesDue { get; set; }

        // project control (PMO)
        public int PcActiveProjects { get; set; }
        public int PcProjectsRed { get; set; }
        public int PcProjectsAmber { get; set; }
        public int PcLateActivities { get; set; }
        public int PcOpenChangeRequests { get; set; }
        public int PcOverdueChangeRequests { get; set; }
        public int PcGuaranteeAlerts { get; set; }
        public int PcOverdueMilestones { get; set; }
        public int PcOverdueInitiationSteps { get; set; }
        public int PcOverdueClientResponses { get; set; }
        public decimal PcPenaltyExposure { get; set; }

        // office automation: decisions of approved meeting minutes that are open past their due date ("مصوبات معوق")
        public int OverdueDecisions { get; set; }

        // Sepidar link
        public int EventsPending { get; set; }
        public int EventsFailed { get; set; }
        public DateTimeOffset? LastEventSent { get; set; }

        /// <summary>General ledger, treasury and receivables figures (finance module).</summary>
        public CeoFinanceDto Finance { get; set; } = new CeoFinanceDto();

        public List<ProjectCostDto> Projects { get; set; } = new List<ProjectCostDto>();
        public List<DashboardAlertDto> Alerts { get; set; } = new List<DashboardAlertDto>();
    }

    /// <summary>Contract (project): price against actual cost and the open purchase commitments.</summary>
    public class ProjectCostDto
    {
        public int ContractId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal? ContractPrice { get; set; }
        public decimal MaterialCost { get; set; }
        public decimal SubcontractCost { get; set; }
        public decimal OtherCost { get; set; }
        public decimal ActualCost { get; set; }
        /// <summary>Released purchase orders of the project not yet received.</summary>
        public decimal Committed { get; set; }
        /// <summary>(actual + committed) / price, percent.</summary>
        public decimal? CostToPricePercent { get; set; }
    }

    public class DashboardAlertDto
    {
        /// <summary>danger, warning or info.</summary>
        public string Level { get; set; }
        public string Text { get; set; }
        /// <summary>Front-end route to open.</summary>
        public string Link { get; set; }
    }
}
