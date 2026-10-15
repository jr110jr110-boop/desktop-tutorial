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
