import type { RouteRecordRaw } from "vue-router";
import { createRouter, createWebHistory } from "vue-router";
import showWarnNotification from "@/composables/showWarnNotification";
import { Features } from "@/utils/features";
import { currentRole, FinanceRoles, FnRoles, hasRole, InventoryRoles, ProcurementRoles, ProjectControlRoles, QualityRoles, Roles } from "@/composables/roles";

import Dashboard from "@/views/Dashboard.vue";

const routes: Array<RouteRecordRaw> = [
  {
    path: "/",
    name: "Dashboard",
    component: Dashboard,
  },
  {
    path: "/login",
    name: "Login",
    component: () => import("@/views/Login.vue"),
  },

  // Cardboard
  {
    path: "/cardBoard/userProcesses",
    name: "CardBoardUserProcesses",
    component: () => import("@/views/cardBoard/UserProcesses.vue"),
  },
  {
    path: "/cardBoard/userBuyOrders",
    name: "CardBoardUserBuyOrders",
    component: () => import("@/views/cardBoard/UserBuyOrders.vue"),
  },

  // Contract
  {
    path: "/contract/list",
    name: "ContractList",
    component: () => import("@/views/contract/List.vue"),
    meta: {
      access: "contract",
    },
  },
  {
    path: "/contract/update/:id?",
    props: true,
    name: "ContractUpdate",
    component: () => import("@/views/contract/Update.vue"),
    meta: {
      access: "contract",
    },
  },

  // Equipment
  {
    path: "/equipment/list",
    name: "EquipmentList",
    component: () => import("@/views/equipment/List.vue"),
    meta: {
      access: "equipment",
    },
  },
  {
    path: "/equipment/update/:id?",
    props: true,
    name: "EquipmentUpdate",
    component: () => import("@/views/equipment/Update.vue"),
    meta: {
      access: "equipment",
    },
  },
  {
    path: "/equipment/file/list/:equipmentId",
    props: true,
    name: "EquipmentFileList",
    component: () => import("@/views/equipment/file/List.vue"),
    meta: {
      access: "equipment",
    },
  },
  {
    path: "/equipment/file/update/:id",
    props: true,
    name: "EquipmentFileUpdate",
    component: () => import("@/views/equipment/file/Update.vue"),
    meta: {
      access: "equipment",
    },
  },
  // Equipment Assembly
  {
    path: "/equipmentAssembly/list/:equipmentId",
    name: "EquipmentAssemblyList",
    props: true,
    component: () => import("@/views/equipment/assembly/index.vue"),
    meta: {
      access: "equipment",
    },
  },

  // Tender
  {
    path: "/tender/list",
    name: "TenderList",
    component: () => import("@/views/tender/List.vue"),
    meta: {
      access: "tender",
    },
  },
  {
    path: "/tender/update/:id?",
    props: true,
    name: "TenderUpdate",
    component: () => import("@/views/tender/Update.vue"),
    meta: {
      access: "tender",
    },
  },

  // User
  {
    path: "/user/list",
    name: "UserList",
    component: () => import("@/views/user/List.vue"),
    meta: {
      access: "user",
    },
  },
  {
    path: "/user/update/:id?",
    props: true,
    name: "UserUpdate",
    component: () => import("@/views/user/Update.vue"),
    meta: {
      access: "user",
    },
  },

  // Provider
  {
    path: "/provider/list",
    name: "ProviderList",
    component: () => import("@/views/provider/List.vue"),
    meta: {
      access: "provider",
    },
  },
  {
    path: "/provider/update/:id?",
    props: true,
    name: "ProviderUpdate",
    component: () => import("@/views/provider/Update.vue"),
    meta: {
      access: "provider",
    },
  },
  {
    path: "/providerContract/list",
    name: "ProviderContractList",
    component: () => import("@/views/providerContract/List.vue"),
    meta: {
      access: "providerContract",
    },
  },
  {
    path: "/providerContract/update/:id?",
    props: true,
    name: "ProviderContractUpdate",
    component: () => import("@/views/providerContract/Update.vue"),
    meta: {
      access: "providerContract",
    },
  },
  {
    path: "/providerCategory/list",
    name: "ProviderCategoryList",
    component: () => import("@/views/provider/category/List.vue"),
    meta: {
      access: "providerCategory",
    },
  },
  {
    path: "/providerCategory/update/:id?",
    props: true,
    name: "providerCategoryUpdate",
    component: () => import("@/views/provider/category/Update.vue"),
    meta: {
      access: "providerCategory",
    },
  },

  // Work Order
  {
    path: "/workOrder/list/:contractId",
    props: true,
    name: "WorkOrderList",
    component: () => import("@/views/workOrder/List.vue"),
    meta: {
      access: "workOrder",
    },
  },
  {
    path: "/workOrder/update/:id?",
    props: true,
    name: "WorkOrderUpdate",
    component: () => import("@/views/workOrder/Update.vue"),
    meta: {
      access: "workOrder",
    },
  },

  // Work Order Assembly
  {
    path: "/workOrder/assemblyList/:workOrderId",
    props: true,
    name: "WorkOrderSubAssemblyList",
    component: () => import("@/views/workOrder/assembly/List.vue"),
    meta: {
      access: "workOrder",
    },
  },
  {
    path: "/workOrder/assembly/update/:id?",
    props: true,
    name: "WorkOrderSubAssemblyUpdate",
    component: () => import("@/views/workOrder/assembly/Update.vue"),
    meta: {
      access: "workOrder",
    },
  },

  // Work Order Bom
  {
    path: "/workOrder/bomList/:assemblyId",
    props: true,
    name: "WorkOrderBomList",
    component: () => import("@/views/workOrder/bom/List.vue"),
    meta: {
      access: "workOrder",
    },
  },
  {
    path: "/workOrder/bom/update/:id?",
    props: true,
    name: "WorkOrderBomUpdate",
    component: () => import("@/views/workOrder/bom/Update.vue"),
    meta: {
      access: "workOrder",
    },
  },

  // Work Order Process
  {
    path: "/workOrder/processList/:bomId",
    props: true,
    name: "WorkOrderProcessList",
    component: () => import("@/views/workOrder/process/List.vue"),
    meta: {
      access: "workOrder",
    },
  },
  {
    path: "/workOrder/process/update/:id?",
    props: true,
    name: "WorkOrderProcessUpdate",
    component: () => import("@/views/workOrder/process/Update.vue"),
    meta: {
      access: "workOrder",
    },
  },

  {
    path: "/workOrder/progressStatistic/:workOrderId",
    props: true,
    name: "WorkOrderProgressStatistic",
    component: () => import("@/views/workOrder/ProgressStatistic.vue"),
    meta: {
      access: "workOrder",
    },
  },

  // Work Flow Group
  {
    path: "/workflow/Group/list",
    name: "WorkflowGroupList",
    component: () => import("@/views/workFlowGroup/List.vue"),
    meta: {
      access: "workflow",
    },
  },
  {
    path: "/workflow/Group/update/:id?",
    name: "WorkflowGroupUpdate",
    props: true,
    component: () => import("@/views/workFlowGroup/Update.vue"),
    meta: {
      access: "workflow",
    },
  },
  {
    path: "/workflow/FlowChart/:id?",
    name: "WorkflowChart",
    props: true,
    component: () => import("@/views/workFlowGroup/FlowChart.vue"),
    meta: {
      access: "workflow",
    },
  },

  // Machine
  {
    path: "/machine/list",
    name: "MachineList",
    props: true,
    component: () => import("@/views/machine/List.vue"),
    meta: {
      access: "machine",
    },
  },
  {
    path: "/machine/update/:id?",
    name: "MachineUpdate",
    props: true,
    component: () => import("@/views/machine/Update.vue"),
    meta: {
      access: "machine",
    },
  },

  // Buy Order
  {
    path: "/buyOrder/list/:workOrderId?",
    props: true,
    name: "BuyOrderList",
    component: () => import("@/views/buyOrder/List.vue"),
    meta: {
      access: "buyOrder",
    },
  },
  {
    path: "/buyOrder/update/:id?",
    props: true,
    name: "BuyOrderUpdate",
    component: () => import("@/views/buyOrder/Update.vue"),
    meta: {
      access: "buyOrder",
    },
  },

  // Holiday
  {
    path: "/holiday/days",
    name: "HolidayDays",
    component: () => import("@/views/holiday/Days.vue"),
    meta: {
      access: "holiday",
    },
  },
  {
    path: "/holiday/dates",
    name: "HolidayDates",
    component: () => import("@/views/holiday/Dates.vue"),
    meta: {
      access: "holiday",
    },
  },

  // Reports
  {
    path: "/reports",
    redirect: "equipment-estimation",
    component: () => import("@/views/report/ReportView.vue"),
    children: [
      {
        path: "equipment-estimation",
        name: "ReportEquipmentEstimation",
        meta: { title: "برآورد اولیه هزینه و زمان" },
        component: () => import("@/views/report/pages/EquipmentEstimation.vue"),
      },
      {
        path: "contract-progress",
        name: "ReportContractProgress",
        meta: { title: "گزارش پیشرفت قرارداد" },
        component: () => import("@/views/report/pages/ContractProgress.vue"),
      },
      {
        path: "work-order-progress",
        name: "ReportWorkOrderProgress",
        meta: { title: "گزارش پیشرفت دستور ساخت" },
        component: () => import("@/views/report/pages/WorkOrderProgress.vue"),
      },
      {
        path: "material-balance",
        name: "ReportMaterialBalance",
        meta: { title: "بالانس مواد و احتیاجات" },
        component: () => import("@/views/report/pages/MaterialBalance.vue"),
      },
    ],
  },

  // Not Found

  // Warehouse, part traceability and Sepidar link
  { path: "/inventory/items", name: "InventoryItems", component: () => import("@/views/inventory/Items.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/groups", name: "InventoryGroups", component: () => import("@/views/inventory/Groups.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/warehouses", name: "InventoryWarehouses", component: () => import("@/views/inventory/Warehouses.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/documents", name: "InventoryDocuments", component: () => import("@/views/inventory/Documents.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/documents/:id", name: "InventoryDocumentEdit", component: () => import("@/views/inventory/DocumentEdit.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/stock", name: "InventoryStock", component: () => import("@/views/inventory/Stock.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/kardex", name: "InventoryKardex", component: () => import("@/views/inventory/Kardex.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/trace", name: "InventoryTrace", component: () => import("@/views/inventory/Trace.vue"), meta: { access: "traceability" } },
  { path: "/inventory/routing", name: "InventoryRouting", component: () => import("@/views/inventory/Routing.vue"), meta: { access: "traceability" } },
  { path: "/inventory/operations", name: "InventoryOperations", component: () => import("@/views/inventory/Operations.vue"), meta: { access: "traceability" } },
  { path: "/inventory/sepidar", name: "InventorySepidar", component: () => import("@/views/inventory/Sepidar.vue"), meta: { access: "sepidar" } },
  { path: "/inventory/coding", name: "InventoryCoding", component: () => import("@/views/inventory/Coding.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/item-requests", name: "InventoryItemRequests", component: () => import("@/views/inventory/ItemRequests.vue"), meta: { access: "warehouse", roles: InventoryRoles.itemRequest } },
  { path: "/inventory/material-requests", name: "InventoryMaterialRequests", component: () => import("@/views/inventory/MaterialRequests.vue"), meta: { access: "warehouse", roles: InventoryRoles.materialRequest } },
  { path: "/inventory/material-requests/:id", name: "InventoryMaterialRequestEdit", component: () => import("@/views/inventory/MaterialRequestEdit.vue"), meta: { access: "warehouse", roles: InventoryRoles.materialRequest } },
  { path: "/inventory/inspections", name: "InventoryInspections", component: () => import("@/views/inventory/Inspections.vue"), meta: { access: "warehouse", roles: InventoryRoles.qc } },
  { path: "/inventory/ncr", name: "InventoryNcrs", component: () => import("@/views/inventory/Ncrs.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/ncr/:id", name: "InventoryNcrEdit", component: () => import("@/views/inventory/NcrEdit.vue"), meta: { access: "warehouse" } },
  { path: "/inventory/counts", name: "InventoryStockCounts", component: () => import("@/views/inventory/StockCounts.vue"), meta: { access: "warehouse", roles: InventoryRoles.counts } },
  { path: "/inventory/counts/:id", name: "InventoryStockCountEdit", component: () => import("@/views/inventory/StockCountEdit.vue"), meta: { access: "warehouse", roles: InventoryRoles.counts } },
  { path: "/inventory/opening-import", name: "InventoryOpeningImport", component: () => import("@/views/inventory/OpeningImport.vue"), meta: { access: "warehouse", roles: InventoryRoles.documents } },
  // Finance and cost accounting
  { path: "/procurement/settings", name: "ProcurementSettings", component: () => import("@/views/procurement/Settings.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/requests", name: "ProcurementRequests", component: () => import("@/views/procurement/Requests.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/requests/:id", name: "ProcurementRequestEdit", component: () => import("@/views/procurement/RequestEdit.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/inquiries", name: "ProcurementInquiries", component: () => import("@/views/procurement/Inquiries.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/inquiries/:id", name: "ProcurementInquiryEdit", component: () => import("@/views/procurement/InquiryEdit.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/orders", name: "ProcurementOrders", component: () => import("@/views/procurement/Orders.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/orders/:id", name: "ProcurementOrderEdit", component: () => import("@/views/procurement/OrderEdit.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/invoices", name: "ProcurementInvoices", component: () => import("@/views/procurement/Invoices.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/payments", name: "ProcurementPayments", component: () => import("@/views/procurement/Payments.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/procurement/suppliers", name: "ProcurementSuppliers", component: () => import("@/views/procurement/Suppliers.vue"), meta: { roles: ProcurementRoles.readers } },
  { path: "/finance/ceo-dashboard", name: "FinanceCeoDashboard", component: () => import("@/views/finance/CeoDashboard.vue"), meta: { roles: FinanceRoles.ceo } },
  { path: "/finance/events", name: "FinanceEvents", component: () => import("@/views/finance/Events.vue"), meta: { roles: FinanceRoles.readers } },
  { path: "/finance/accounts", name: "FinanceAccounts", component: () => import("@/views/finance/Accounts.vue"), meta: { roles: FinanceRoles.readers } },
  { path: "/finance/costs", name: "FinanceCosts", component: () => import("@/views/finance/Costs.vue"), meta: { roles: FinanceRoles.readers } },
  { path: "/finance/valuation", name: "FinanceValuation", component: () => import("@/views/finance/Valuation.vue"), meta: { roles: FinanceRoles.readers } },
  { path: "/finance/readiness", name: "FinanceReadiness", component: () => import("@/views/finance/Readiness.vue"), meta: { roles: FinanceRoles.readers } },
  // Accounting (F1 + F2): ledger, treasury, receivables, tax, fixed assets, payroll, Sepidar outbox
  { path: "/fn/dashboard", name: "FnDashboard", component: () => import("@/views/fn/Dashboard.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/setup-check", name: "FnSetupCheck", component: () => import("@/views/fn/SetupCheck.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/years", name: "FnYears", component: () => import("@/views/fn/Years.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/accounts", name: "FnAccounts", component: () => import("@/views/fn/Accounts.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/vouchers", name: "FnVouchers", component: () => import("@/views/fn/Vouchers.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/vouchers/:id", name: "FnVoucherEdit", component: () => import("@/views/fn/VoucherEdit.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/reports/journal", name: "FnReportJournal", component: () => import("@/views/fn/LedgerReports.vue"), props: { kind: "journal" }, meta: { roles: FnRoles.readers } },
  { path: "/fn/reports/ledger", name: "FnReportLedger", component: () => import("@/views/fn/LedgerReports.vue"), props: { kind: "general-ledger" }, meta: { roles: FnRoles.readers } },
  { path: "/fn/reports/trial-balance", name: "FnReportTrial", component: () => import("@/views/fn/LedgerReports.vue"), props: { kind: "trial-balance" }, meta: { roles: FnRoles.readers } },
  { path: "/fn/reports/account-review", name: "FnReportReview", component: () => import("@/views/fn/LedgerReports.vue"), props: { kind: "account-review" }, meta: { roles: FnRoles.readers } },
  { path: "/fn/reports/balance-sheet", name: "FnReportBalance", component: () => import("@/views/fn/LedgerReports.vue"), props: { kind: "balance-sheet" }, meta: { roles: FnRoles.readers } },
  { path: "/fn/reports/income-statement", name: "FnReportIncome", component: () => import("@/views/fn/LedgerReports.vue"), props: { kind: "income-statement" }, meta: { roles: FnRoles.readers } },
  { path: "/fn/reports/project-profitability", name: "FnReportProfit", component: () => import("@/views/fn/LedgerReports.vue"), props: { kind: "project-profitability" }, meta: { roles: FnRoles.readers } },
  { path: "/fn/reports/reconciliation", name: "FnReportRecon", component: () => import("@/views/fn/LedgerReports.vue"), props: { kind: "reconciliation" }, meta: { roles: FnRoles.readers } },
  { path: "/fn/treasury/accounts", name: "FnTreasuryAccounts", component: () => import("@/views/fn/TreasuryAccounts.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/treasury/petty-cash", name: "FnPettyCash", component: () => import("@/views/fn/PettyCash.vue"), meta: {} },
  { path: "/fn/treasury/payments", name: "FnPayments", component: () => import("@/views/fn/Payments.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/treasury/receipts", name: "FnReceipts", component: () => import("@/views/fn/Receipts.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/treasury/cheques", name: "FnCheques", component: () => import("@/views/fn/Cheques.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/treasury/guarantees", name: "FnGuarantees", component: () => import("@/views/fn/Guarantees.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/treasury/statements", name: "FnStatements", component: () => import("@/views/fn/Statements.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/treasury/reconciliations", name: "FnReconciliations", component: () => import("@/views/fn/Reconciliations.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/treasury/cash-position", name: "FnCashPosition", component: () => import("@/views/fn/CashPosition.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/receivables/customers", name: "FnCustomers", component: () => import("@/views/fn/Customers.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/receivables/customers/:id/statement", name: "FnCustomerStatement", component: () => import("@/views/fn/CustomerStatement.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/receivables/invoices", name: "FnInvoices", component: () => import("@/views/fn/Invoices.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/receivables/ageing", name: "FnAgeing", component: () => import("@/views/fn/Ageing.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/tax", name: "FnTax", component: () => import("@/views/fn/Tax.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/assets/classes", name: "FnAssetClasses", component: () => import("@/views/fn/AssetClasses.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/assets/register", name: "FnAssets", component: () => import("@/views/fn/Assets.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/assets/depreciation", name: "FnDepreciation", component: () => import("@/views/fn/Depreciation.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/assets/disposals", name: "FnDisposals", component: () => import("@/views/fn/Disposals.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/assets/reports", name: "FnAssetReports", component: () => import("@/views/fn/AssetReports.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/payroll/setup", name: "FnPayrollMasters", component: () => import("@/views/fn/PayrollMasters.vue"), meta: { roles: FnRoles.payrollReaders } },
  { path: "/fn/payroll/employees", name: "FnPayrollEmployees", component: () => import("@/views/fn/PayrollEmployees.vue"), meta: { roles: FnRoles.payrollReaders } },
  { path: "/fn/payroll/attendance", name: "FnPayrollAttendance", component: () => import("@/views/fn/PayrollAttendance.vue"), meta: { roles: FnRoles.payrollReaders } },
  { path: "/fn/payroll/runs", name: "FnPayrollRuns", component: () => import("@/views/fn/PayrollRuns.vue"), meta: { roles: FnRoles.payrollReaders } },
  { path: "/fn/payroll/payslips", name: "FnPayslips", component: () => import("@/views/fn/Payslips.vue"), meta: { roles: FnRoles.payrollReaders } },
  { path: "/fn/payroll/reports", name: "FnPayrollReports", component: () => import("@/views/fn/PayrollReports.vue"), meta: { roles: FnRoles.payrollReaders } },
  { path: "/fn/sepidar", name: "FnSepidar", component: () => import("@/views/fn/Sepidar.vue"), meta: { roles: FnRoles.outbox } },
  { path: "/fn/audit", name: "FnAudit", component: () => import("@/views/fn/AuditTrail.vue"), meta: { roles: FnRoles.auditReaders } },
  { path: "/fn/close-checklist", name: "FnCloseChecklist", component: () => import("@/views/fn/CloseChecklist.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/finance-settings", name: "FnFinanceSettings", component: () => import("@/views/fn/FinanceSettings.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/voucher-templates", name: "FnVoucherTemplates", component: () => import("@/views/fn/VoucherTemplates.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/mass-import", name: "FnMassImport", component: () => import("@/views/fn/MassImport.vue"), meta: { roles: FnRoles.accountants } },
  { path: "/fn/migration", name: "FnMigration", component: () => import("@/views/fn/Migration.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/parallel-run", name: "FnParallelRun", component: () => import("@/views/fn/ParallelRun.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/sepidar-export", name: "FnSepidarExport", component: () => import("@/views/fn/SepidarExport.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/entities", name: "FnEntities", component: () => import("@/views/fn/LegalEntities.vue"), meta: { feature: "multiEntity", roles: FnRoles.readers } },
  { path: "/fn/consolidation", name: "FnConsolidation", component: () => import("@/views/fn/Consolidation.vue"), meta: { feature: "multiEntity", roles: FnRoles.readers } },
  { path: "/fn/fx", name: "FnFx", component: () => import("@/views/fn/Fx.vue"), meta: { feature: "foreignCurrency", roles: FnRoles.readers } },
  { path: "/fn/letters-of-credit", name: "FnLettersOfCredit", component: () => import("@/views/fn/LettersOfCredit.vue"), meta: { feature: "foreignCurrency", roles: FnRoles.readers } },
  { path: "/fn/budgets", name: "FnBudgets", component: () => import("@/views/fn/Budgets.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/costing", name: "FnCosting", component: () => import("@/views/fn/Costing.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/deferrals", name: "FnDeferrals", component: () => import("@/views/fn/Deferrals.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/loans", name: "FnLoans", component: () => import("@/views/fn/Loans.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/financial-statements", name: "FnFinancialStatements", component: () => import("@/views/fn/FinStatements.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/tax-books", name: "FnTaxBooks", component: () => import("@/views/fn/TaxBooks.vue"), meta: { roles: FnRoles.readers } },
  { path: "/fn/collections", name: "FnCollections", component: () => import("@/views/fn/Collections.vue"), meta: { roles: FnRoles.readers } },
  // Quality (QC + QA): the paths of the alert links of the API (/quality/capa, /quality/calibration, /quality/itp, ...) are kept
  { path: "/quality/dashboard", name: "QualityDashboard", component: () => import("@/views/quality/Dashboard.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/itp", name: "QualityItps", component: () => import("@/views/quality/ItpList.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/itp/:id", name: "QualityItpEdit", component: () => import("@/views/quality/ItpEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/records", name: "QualityRecords", component: () => import("@/views/quality/Records.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/records/:id", name: "QualityRecordEdit", component: () => import("@/views/quality/RecordEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/reports", name: "QualityReports", component: () => import("@/views/quality/Reports.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/reports/:id", name: "QualityReportEdit", component: () => import("@/views/quality/ReportEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/calibration", name: "QualityCalibration", component: () => import("@/views/quality/Calibration.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/calibration/:id", name: "QualityEquipmentEdit", component: () => import("@/views/quality/EquipmentEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/calibration-labels", name: "QualityCalibrationLabels", component: () => import("@/views/quality/CalibrationLabels.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/out-of-tolerance/:id", name: "QualityOotCase", component: () => import("@/views/quality/OotCase.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/personnel", name: "QualityPersonnel", component: () => import("@/views/quality/Personnel.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/wps", name: "QualityWps", component: () => import("@/views/quality/Wps.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/weld-logs", name: "QualityWeldLogs", component: () => import("@/views/quality/WeldLogs.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/certificates", name: "QualityCertificates", component: () => import("@/views/quality/Certificates.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/traceability", name: "QualityTraceability", component: () => import("@/views/quality/Traceability.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/capa", name: "QualityCapas", component: () => import("@/views/quality/Capas.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/capa/:id", name: "QualityCapaEdit", component: () => import("@/views/quality/CapaEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/release-notes", name: "QualityReleaseNotes", component: () => import("@/views/quality/ReleaseNotes.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/release-notes/:id", name: "QualityReleaseNoteEdit", component: () => import("@/views/quality/ReleaseNoteEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/data-book/:workOrderId", name: "QualityDataBook", component: () => import("@/views/quality/DataBook.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/complaints", name: "QualityComplaints", component: () => import("@/views/quality/Complaints.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/documents", name: "QualityDocuments", component: () => import("@/views/quality/Documents.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/documents/:id", name: "QualityDocumentEdit", component: () => import("@/views/quality/DocumentEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/audits", name: "QualityAudits", component: () => import("@/views/quality/Audits.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/audits/:id", name: "QualityAuditEdit", component: () => import("@/views/quality/AuditEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/management-reviews", name: "QualityReviews", component: () => import("@/views/quality/Reviews.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/management-reviews/:id", name: "QualityReviewEdit", component: () => import("@/views/quality/ReviewEdit.vue"), meta: { roles: QualityRoles.readers } },
  { path: "/quality/settings", name: "QualitySettings", component: () => import("@/views/quality/Settings.vue"), meta: { roles: QualityRoles.readers } },
  // Project control (PMO): the paths of the alert links of the API (/project-control/portfolio, guarantees, changes, milestones, transmittals, penalties) are kept
  { path: "/project-control/portfolio", name: "ProjectControlPortfolio", component: () => import("@/views/projectControl/Portfolio.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/project", name: "ProjectControlProject", component: () => import("@/views/projectControl/Project.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/wbs", name: "ProjectControlWbs", component: () => import("@/views/projectControl/Wbs.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/activities", name: "ProjectControlActivities", component: () => import("@/views/projectControl/Activities.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/schedule", name: "ProjectControlSchedule", component: () => import("@/views/projectControl/Schedule.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/gantt", name: "ProjectControlGantt", component: () => import("@/views/projectControl/Gantt.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/baselines", name: "ProjectControlBaselines", component: () => import("@/views/projectControl/Baselines.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/progress", name: "ProjectControlProgress", component: () => import("@/views/projectControl/Progress.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/evm", name: "ProjectControlEvm", component: () => import("@/views/projectControl/Evm.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/milestones", name: "ProjectControlMilestones", component: () => import("@/views/projectControl/Commercial.vue"), props: { tab: "milestones" }, meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/guarantees", name: "ProjectControlGuarantees", component: () => import("@/views/projectControl/Commercial.vue"), props: { tab: "guarantees" }, meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/penalties", name: "ProjectControlPenalties", component: () => import("@/views/projectControl/Commercial.vue"), props: { tab: "penalties" }, meta: { roles: ProjectControlRoles.penalties } },
  { path: "/project-control/variations", name: "ProjectControlVariations", component: () => import("@/views/projectControl/Commercial.vue"), props: { tab: "variations" }, meta: { roles: ProjectControlRoles.variations } },
  { path: "/project-control/mdr", name: "ProjectControlMdr", component: () => import("@/views/projectControl/Mdr.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/mdl", name: "ProjectControlMdl", component: () => import("@/views/projectControl/Mdr.vue"), props: { mdl: true }, meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/transmittals", name: "ProjectControlTransmittals", component: () => import("@/views/projectControl/Transmittals.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/mdr-templates", name: "ProjectControlMdrTemplates", component: () => import("@/views/projectControl/DocSetup.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/final-books", name: "ProjectControlFinalBooks", component: () => import("@/views/projectControl/FinalBooks.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/changes", name: "ProjectControlChanges", component: () => import("@/views/projectControl/Changes.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/claims", name: "ProjectControlClaims", component: () => import("@/views/projectControl/Claims.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/shipments", name: "ProjectControlShipments", component: () => import("@/views/projectControl/Shipments.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/project-control/reports", name: "ProjectControlReports", component: () => import("@/views/projectControl/Reports.vue"), meta: { roles: ProjectControlRoles.readers } },
  { path: "/inventory/labels", name: "InventoryLabels", component: () => import("@/views/inventory/Labels.vue") },
  {
    path: "/:pathMatch(.*)*",
    component: () => import("@/views/NotFound.vue"),
  },
];

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
});

const handleHasNoAccess = (next) => {
  showWarnNotification("شما به این صفحه دسترسی ندارید");
  next({ name: "Dashboard" });
};

// meta.access = the claim (accessList) the page needs; meta.roles = the roles the API allows (JWT role, see composables/roles.ts)
const claims: Record<string, string> = {
  user: "Account",
  contract: "Contract",
  equipment: "Equipment",
  workflow: "Workflow",
  machine: "Machine",
  warehouse: "Warehouse",
  traceability: "Traceability",
  sepidar: "SepidarLink",
  tender: "Tender",
};

router.beforeEach((to, from, next) => {
  // read on every navigation: the list changes when another user logs in without reloading the page
  const accessList = localStorage.getItem("accessList") || "";
  const loggedIn = !!window.localStorage.getItem("token");
  if (to.name === "Login") {
    if (!loggedIn) next();
    else next({ name: "Dashboard" });
    return;
  }
  if (!loggedIn) return next({ name: "Login" });
  // the CEO starts on the CEO dashboard
  if (to.name === "Dashboard" && currentRole() === Roles.CEO) return next({ name: "FinanceCeoDashboard" });
  const claim = typeof to.meta.access === "string" ? claims[to.meta.access] : undefined;
  if (claim && !accessList.includes(claim)) return handleHasNoAccess(next);
  // features switched off by company decision (one company, rial only)
  const feature = to.meta.feature as keyof typeof Features | undefined;
  if (feature && !Features[feature]) return handleHasNoAccess(next);
  const roles = to.meta.roles as string[] | undefined;
  if (roles && !hasRole(roles)) return handleHasNoAccess(next);
  next();
});

export default router;
