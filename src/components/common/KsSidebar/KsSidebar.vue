<template>
  <div class="background-color vh-100 pt-3 overflow-scroll pb-5">
    <div
      class="d-flex flex-row-reverse align-items-center py-1 px-2 justify-content-center position-relative"
      style="height: 120px">
      <div class="d-flex flex-column gap-1 align-items-center">
        <KsAvatar
          :loading="avatarLoading"
          :media-src="avatar"
          :size="sidebarState.isSidebarExpanded ? 84 : 42"
          media-type="image"
          object-fit="contain"
          radius="circle">
          <template #default-icon>
            <UserIcon :fill="Colors.primary" width="24" />
          </template>
        </KsAvatar>
        <div
          v-if="sidebarState.isSidebarExpanded"
          class="d-flex align-items-center w-100 justify-content-center bottom-0 no-pointer-event">
          <span class="fw-bold text-xSmall">
            {{ userRole }}
          </span>
        </div>
      </div>
      <button
        v-if="windowScreen >= 1200"
        v-show="sidebarState.isSidebarExpanded"
        class="bg-transparent p-0 border-0 position-absolute"
        style="left: 16px; top: 0"
        @click="emits('pinSidebar', true)">
        <pin-circle-icon
          :active="sidebarState.isSidebarPinned"
          :fill="Colors.primaryLight"
          class="pin-icon" />
      </button>
    </div>
    <hr class="opacity-30" />
    <ul
      class="list-unstyled d-flex flex-column gap-3"
      style="padding-right: 5px">
      <KsSidebarLink
        :sidebar-state="sidebarState"
        route-name="Dashboard"
        title="داشبورد">
        <HomeIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="
            $route.name === 'Dashboard' ? Colors.primary : Colors.primaryLight
          "
          width="18" />
      </KsSidebarLink>
      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.cardBoards"
        has-sub
        route-name="Cardboard"
        tag="cardboard-collapse"
        title="کارتابل">
        <BriefcaseIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="Colors.primaryLight"
          width="18" />
      </KsSidebarLink>
      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.reports"
        has-sub
        route-name="Reports"
        tag="reports-collapse"
        title="گزارشات">
        <ChartLineIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="Colors.primaryLight"
          width="18" />
      </KsSidebarLink>

      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.data"
        has-sub
        route-name="Data"
        tag="data-collapse"
        title="داده ها">
        <CopyIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="Colors.primaryLight"
          width="18" />
      </KsSidebarLink>

      <KsSidebarLink
        :is-locked="false"
        :sidebar-state="sidebarState"
        route-name="BuyOrderList"
        title="فرآیندهای تامین، مونتاژ و ساخت">
        <ShoppingCartIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="
            $route.meta['access'] === 'buyOrder'
              ? Colors.primary
              : Colors.primaryLight
          "
          width="18" />
      </KsSidebarLink>

      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.inventory"
        has-sub
        route-name="Inventory"
        tag="inventory-collapse"
        title="انبار و ردیابی قطعات">
        <CopyIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="
            String($route.name || '').startsWith('Inventory')
              ? Colors.primary
              : Colors.primaryLight
          "
          width="18" />
      </KsSidebarLink>

      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.procurement"
        has-sub
        route-name="Procurement"
        tag="procurement-collapse"
        title="تدارکات">
        <ShoppingCartIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="String($route.name || '').startsWith('Procurement') ? Colors.primary : Colors.primaryLight"
          width="18" />
      </KsSidebarLink>

      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.quality"
        has-sub
        route-name="Quality"
        tag="quality-collapse"
        title="کیفیت (QC و QA)">
        <BriefcaseIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="String($route.name || '').startsWith('Quality') ? Colors.primary : Colors.primaryLight"
          width="18" />
      </KsSidebarLink>

      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.projectControl"
        has-sub
        route-name="ProjectControl"
        tag="project-control-collapse"
        title="کنترل پروژه">
        <ChartLineIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="String($route.name || '').startsWith('ProjectControl') ? Colors.primary : Colors.primaryLight"
          width="18" />
      </KsSidebarLink>

      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.finance"
        has-sub
        route-name="Finance"
        tag="finance-collapse"
        title="مالی و بهای تمام‌شده">
        <ChartLineIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="String($route.name || '').startsWith('Finance') ? Colors.primary : Colors.primaryLight"
          width="18" />
      </KsSidebarLink>

      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.providers"
        has-sub
        route-name="Providers"
        tag="providers-collapse"
        title="گروه تامین">
        <HandShakeIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="Colors.primaryLight"
          width="18" />
      </KsSidebarLink>
      <KsSidebarLink
        :sidebar-state="sidebarState"
        :subs="links.holidays"
        has-sub
        route-name="Holiday"
        tag="holiday-collapse"
        title="تعطیلات">
        <UmbrellaBeachIcon
          :class="sidebarState.isSidebarExpanded && 'ms-3'"
          :fill="Colors.primaryLight"
          width="18" />
      </KsSidebarLink>
    </ul>
  </div>
</template>

<script lang="ts" setup>
import { FinanceRoles, hasRole, InventoryRoles, ProcurementRoles, ProjectControlRoles, QualityRoles } from "@/composables/roles";
import type { PropType } from "vue";
import type Links from "@/components/common/KsSidebar/type";

import KsSidebarLink from "./KsSidebarLink.vue";
import KsAvatar from "@/components/common/KsAvatar.vue";
import HomeIcon from "@/components/icons/HomeIcon.vue";
import PinCircleIcon from "@/components/icons/PinCircleIcon.vue";
import CopyIcon from "@/components/icons/CopyIcon.vue";
import ShoppingCartIcon from "@/components/icons/ShoppingCartIcon.vue";
import BriefcaseIcon from "@/components/icons/BriefcaseIcon.vue";
import UserIcon from "@/components/icons/UserIcon.vue";
import HandShakeIcon from "@/components/icons/HandShakeIcon.vue";
import UmbrellaBeachIcon from "@/components/icons/UmbrellaBeachIcon.vue";
import ChartLineIcon from "@/components/icons/ChartLineIcon.vue";
import { Colors } from "@/assets/colors";

defineProps({
  sidebarState: Object,
  windowScreen: Number as PropType<number>,
  avatar: {
    type: String,
    default: null,
  },
  avatarLoading: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
});

const emits = defineEmits(["pinSidebar"]);

const accessList = localStorage.getItem("accessList") || [];
const links = {
  cardBoards: [
    {
      title: "فرآیندهای تولید مدارک",
      routeName: "CardBoardUserProcesses",
    },
    {
      title: "فرآیندهای تامین، مونتاژ و ساخت",
      routeName: "CardBoardUserBuyOrders",
    },
  ] as Links[],
  reports: [
    {
      title: "برآورد اولیه هزینه و زمان",
      routeName: "ReportEquipmentEstimation",
    },
    {
      title: "وضعیت قراردادها",
      routeName: "ReportContractProgress",
    },
    {
      title: "وضعیت دستور ساخت ها",
      routeName: "ReportWorkOrderProgress",
    },
    {
      title: "بالانس مواد و احتیاجات",
      routeName: "ReportMaterialBalance",
    },
  ] as Links[],
  providers: [
    {
      title: "تامین کنندگان",
      routeName: "ProviderList",
    },
    {
      title: "دسنه بندی تامین کنندگان",
      routeName: "ProviderCategoryList",
    },
    {
      title: "قراردادهای تامین کنندگان",
      routeName: "ProviderContractList",
    },
  ] as Links[],
  data: [
    {
      title: "قراردادها",
      routeName: "ContractList",
      disabled: !accessList.includes("Contract"),
    },
    {
      title: "محصولات",
      routeName: "EquipmentList",
      disabled: !accessList.includes("Equipment"),
    },
    {
      title: "مناقصه ها",
      routeName: "TenderList",
      disabled: !accessList.includes("Tender"),
    },
    {
      title: "کاربران",
      routeName: "UserList",
      disabled: !accessList.includes("Account"),
    },
    {
      title: "دستگردان مدارک",
      routeName: "WorkflowGroupList",
      disabled: !accessList.includes("Workflow"),
    },
    {
      title: "دستگاه",
      routeName: "MachineList",
      disabled: !accessList.includes("Machine"),
    },
  ] as Links[],
  inventory: [
    { title: "اسناد انبار (رسید، حواله، انتقال)", routeName: "InventoryDocuments", disabled: !accessList.includes("Warehouse") },
    { title: "موجودی", routeName: "InventoryStock", disabled: !accessList.includes("Warehouse") },
    { title: "کاردکس", routeName: "InventoryKardex", disabled: !accessList.includes("Warehouse") },
    { title: "ردیابی قطعات (اسکن)", routeName: "InventoryTrace", disabled: !accessList.includes("Traceability") },
    { title: "کارتابل مراحل ساخت و QC", routeName: "InventoryOperations", disabled: !accessList.includes("Traceability") },
    { title: "مسیر ساخت", routeName: "InventoryRouting", disabled: !accessList.includes("Traceability") },
    { title: "کالاها و کدینگ", routeName: "InventoryItems", disabled: !accessList.includes("Warehouse") },
    { title: "گروه‌بندی کالا", routeName: "InventoryGroups", disabled: !accessList.includes("Warehouse") },
    { title: "انبارها و محل‌ها", routeName: "InventoryWarehouses", disabled: !accessList.includes("Warehouse") },
    { title: "درخواست کالا", routeName: "InventoryMaterialRequests", disabled: !accessList.includes("Warehouse") || !hasRole(InventoryRoles.materialRequest) },
    { title: "قرنطینه و بازرسی ورودی", routeName: "InventoryInspections", disabled: !accessList.includes("Warehouse") || !hasRole(InventoryRoles.qc) },
    { title: "گزارش عدم انطباق (NCR)", routeName: "InventoryNcrs", disabled: !accessList.includes("Warehouse") },
    { title: "شمارش و انبارگردانی", routeName: "InventoryStockCounts", disabled: !accessList.includes("Warehouse") || !hasRole(InventoryRoles.counts) },
    { title: "درخواست کد کالا", routeName: "InventoryItemRequests", disabled: !accessList.includes("Warehouse") || !hasRole(InventoryRoles.itemRequest) },
    { title: "جدول‌های کدینگ (WI-WH-002)", routeName: "InventoryCoding", disabled: !accessList.includes("Warehouse") },
    { title: "ارتباط با سپیدار", routeName: "InventorySepidar", disabled: !accessList.includes("SepidarLink") },
  ] as Links[],
  procurement: [
    { title: "درخواست خرید", routeName: "ProcurementRequests", disabled: !hasRole(ProcurementRoles.readers) },
    { title: "استعلام و کمیته خرید", routeName: "ProcurementInquiries", disabled: !hasRole(ProcurementRoles.readers) },
    { title: "سفارش خرید", routeName: "ProcurementOrders", disabled: !hasRole(ProcurementRoles.readers) },
    { title: "فاکتور تامین‌کننده", routeName: "ProcurementInvoices", disabled: !hasRole(ProcurementRoles.readers) },
    { title: "درخواست پرداخت", routeName: "ProcurementPayments", disabled: !hasRole(ProcurementRoles.readers) },
    { title: "لیست تامین‌کنندگان و ارزیابی", routeName: "ProcurementSuppliers", disabled: !hasRole(ProcurementRoles.readers) },
    { title: "تنظیمات و آستانه‌های خرید", routeName: "ProcurementSettings", disabled: !hasRole(ProcurementRoles.readers) },
  ] as Links[],
  quality: [
    { title: "داشبورد کیفیت", routeName: "QualityDashboard", disabled: !hasRole(QualityRoles.readers) },
    { title: "برنامه بازرسی و آزمون (ITP)", routeName: "QualityItps", disabled: !hasRole(QualityRoles.readers) },
    { title: "سوابق بازرسی", routeName: "QualityRecords", disabled: !hasRole(QualityRoles.readers) },
    { title: "گزارش‌های آزمون", routeName: "QualityReports", disabled: !hasRole(QualityRoles.readers) },
    { title: "تجهیزات اندازه‌گیری و کالیبراسیون", routeName: "QualityCalibration", disabled: !hasRole(QualityRoles.readers) },
    { title: "پرسنل صلاحیت‌دار و گواهی‌ها", routeName: "QualityPersonnel", disabled: !hasRole(QualityRoles.readers) },
    { title: "WPS و PQR", routeName: "QualityWps", disabled: !hasRole(QualityRoles.readers) },
    { title: "گزارش جوشکاری", routeName: "QualityWeldLogs", disabled: !hasRole(QualityRoles.readers) },
    { title: "گواهی‌های مواد (EN 10204)", routeName: "QualityCertificates", disabled: !hasRole(QualityRoles.readers) },
    { title: "ردیابی سریال و بچ", routeName: "QualityTraceability", disabled: !hasRole(QualityRoles.readers) },
    { title: "اقدام اصلاحی و پیشگیرانه (CAPA)", routeName: "QualityCapas", disabled: !hasRole(QualityRoles.readers) },
    { title: "برگه‌های ترخیص", routeName: "QualityReleaseNotes", disabled: !hasRole(QualityRoles.readers) },
    { title: "شکایات مشتری", routeName: "QualityComplaints", disabled: !hasRole(QualityRoles.readers) },
    { title: "کنترل مدارک", routeName: "QualityDocuments", disabled: !hasRole(QualityRoles.readers) },
    { title: "ممیزی داخلی", routeName: "QualityAudits", disabled: !hasRole(QualityRoles.readers) },
    { title: "بازنگری مدیریت", routeName: "QualityReviews", disabled: !hasRole(QualityRoles.readers) },
    { title: "تنظیمات کیفیت", routeName: "QualitySettings", disabled: !hasRole(QualityRoles.readers) },
  ] as Links[],
  projectControl: [
    { title: "داشبورد سبد پروژه‌ها", routeName: "ProjectControlPortfolio", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "فضای کار پروژه", routeName: "ProjectControlProject", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "ساختار شکست کار (WBS)", routeName: "ProjectControlWbs", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "فعالیت‌ها، وابستگی‌ها و تقویم", routeName: "ProjectControlActivities", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "کنترل زمان‌بندی (CPM)", routeName: "ProjectControlSchedule", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "نمودار گانت", routeName: "ProjectControlGantt", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "خط مبنا و هزینه", routeName: "ProjectControlBaselines", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "پیشرفت و منحنی S", routeName: "ProjectControlProgress", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "ارزش کسب‌شده و جریان نقدی", routeName: "ProjectControlEvm", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "مراحل پرداخت", routeName: "ProjectControlMilestones", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "ضمانت‌نامه‌ها", routeName: "ProjectControlGuarantees", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "جریمه تاخیر", routeName: "ProjectControlPenalties", disabled: !hasRole(ProjectControlRoles.penalties) },
    { title: "دستور تغییر", routeName: "ProjectControlVariations", disabled: !hasRole(ProjectControlRoles.variations) },
    { title: "فهرست مدارک (MDR)", routeName: "ProjectControlMdr", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "مدارک کارفرما (MDL)", routeName: "ProjectControlMdl", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "ترنسمیتال", routeName: "ProjectControlTransmittals", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "قالب و کد پاسخ مدارک", routeName: "ProjectControlMdrTemplates", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "کتاب نهایی", routeName: "ProjectControlFinalBooks", disabled: !hasRole(ProjectControlRoles.finalBooks) },
    { title: "کنترل تغییرات", routeName: "ProjectControlChanges", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "ادعاها", routeName: "ProjectControlClaims", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "محموله‌ها و ارسال", routeName: "ProjectControlShipments", disabled: !hasRole(ProjectControlRoles.readers) },
    { title: "گزارش‌های پروژه", routeName: "ProjectControlReports", disabled: !hasRole(ProjectControlRoles.readers) },
  ] as Links[],
  finance: [
    { title: "داشبورد مدیرعامل", routeName: "FinanceCeoDashboard", disabled: !hasRole(FinanceRoles.ceo) },
    { title: "رویدادهای حسابداری و سند سپیدار", routeName: "FinanceEvents", disabled: !hasRole(FinanceRoles.readers) },
    { title: "بهای تمام‌شده پروژه و دستور ساخت", routeName: "FinanceCosts", disabled: !hasRole(FinanceRoles.readers) },
    { title: "ارزش‌گذاری موجودی", routeName: "FinanceValuation", disabled: !hasRole(FinanceRoles.readers) },
    { title: "نگاشت حساب‌ها و مراکز هزینه", routeName: "FinanceAccounts", disabled: !hasRole(FinanceRoles.readers) },
    { title: "آمادگی سپیدار", routeName: "FinanceReadiness", disabled: !hasRole(FinanceRoles.readers) },
  ] as Links[],
  holidays: [
    {
      title: "روزهای کاری",
      routeName: "HolidayDays",
    },
    {
      title: "تعطیلات رسمی",
      routeName: "HolidayDates",
    },
  ] as Links[],
};

const userRole = localStorage.getItem("userRole");
</script>

<style scoped>
* {
  transition: all 200ms ease;
}

.pin-icon {
  width: 14px;
}
</style>
