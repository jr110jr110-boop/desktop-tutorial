import type { RouteRecordRaw } from "vue-router";
import { createRouter, createWebHistory } from "vue-router";
import showWarnNotification from "@/composables/showWarnNotification";

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
    component: () => import("@/views/workflowGroup/List.vue"),
    meta: {
      access: "workflow",
    },
  },
  {
    path: "/workflow/Group/update/:id?",
    name: "WorkflowGroupUpdate",
    props: true,
    component: () => import("@/views/workflowGroup/Update.vue"),
    meta: {
      access: "workflow",
    },
  },
  {
    path: "/workflow/FlowChart/:id?",
    name: "WorkflowChart",
    props: true,
    component: () => import("@/views/workflowGroup/FlowChart.vue"),
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
        component: () => import("@/views/report/pages/EquipmentEstimation.vue"),
      },
      {
        path: "contract-progress",
        name: "ReportContractProgress",
        component: () => import("@/views/report/pages/ContractProgress.vue"),
      },
      {
        path: "work-order-progress",
        name: "ReportWorkOrderProgress",
        component: () => import("@/views/report/pages/WorkOrderProgress.vue"),
      },
      {
        path: "material-balance",
        name: "ReportMaterialBalance",
        component: () => import("@/views/report/pages/MaterialBalance.vue"),
      },
    ],
  },

  // Not Found
  {
    path: "/:pathMatch(.*)*",
    component: () => import("@/views/NotFound.vue"),
  },
];

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
});

const accessList = localStorage.getItem("accessList") || [];

const handleHasNoAccess = (next) => {
  showWarnNotification("شما به این صفحه دسترسی ندارید");
  next({ name: "Dashboard" });
};

router.beforeEach((to, from, next) => {
  if (to.name === "Login") {
    if (!window.localStorage.getItem("token")) next();
    else next({ name: "Dashboard" });
  } else {
    if (!window.localStorage.getItem("token")) next({ name: "Login" });
    else {
      if (to.meta.access === "user")
        if (accessList.includes("Account")) next();
        else handleHasNoAccess(next);
      else if (to.meta.access === "contract")
        if (accessList.includes("Contract")) next();
        else handleHasNoAccess(next);
      else if (to.meta.access === "equipment")
        if (accessList.includes("Equipment")) next();
        else handleHasNoAccess(next);
      else if (to.meta.access === "workflow")
        if (accessList.includes("Workflow")) next();
        else handleHasNoAccess(next);
      else if (to.meta.access === "machine")
        if (accessList.includes("Machine")) next();
        else handleHasNoAccess(next);
      else if (to.meta.access === "tender")
        if (accessList.includes("Tender")) next();
        else handleHasNoAccess(next);
      else next();
    }
  }
});

export default router;
