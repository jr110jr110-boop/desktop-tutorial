import type { RouteRecordRaw } from "vue-router";
import { createRouter, createWebHistory } from "vue-router";
import showWarnNotification from "@/composables/showWarnNotification";
import { currentRole, FinanceRoles, hasRole, InventoryRoles, ProcurementRoles, Roles } from "@/composables/roles";

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
  const roles = to.meta.roles as string[] | undefined;
  if (roles && !hasRole(roles)) return handleHasNoAccess(next);
  next();
});

export default router;
