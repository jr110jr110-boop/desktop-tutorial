<script setup lang="ts">
import { ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import ProgressOverview from "@/components/pc/progress/ProgressOverview.vue";
import ManualProgress from "@/components/pc/progress/ManualProgress.vue";
import DailyReport from "@/components/pc/progress/DailyReport.vue";
import Snapshots from "@/components/pc/progress/Snapshots.vue";
import { printPage } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";

const tab = ref("overview");
const tabs = [
  { key: "overview", title: "پیشرفت پروژه" },
  { key: "manual", title: "درخواست‌های پیشرفت" },
  { key: "daily", title: "گزارش روزانه" },
  { key: "snap", title: "تصاویر لحظه‌ای و منحنی S" },
];
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="پیشرفت پروژه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">پیشرفت پروژه</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none"><ContractSelect /><button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button></div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <PcTabs v-model="tab" :tabs="tabs" />
        <ProgressOverview v-if="tab === 'overview'" :contract-id="pcContractId" />
        <ManualProgress v-else-if="tab === 'manual'" :contract-id="pcContractId" />
        <DailyReport v-else-if="tab === 'daily'" :contract-id="pcContractId" />
        <Snapshots v-else :contract-id="pcContractId" />
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
