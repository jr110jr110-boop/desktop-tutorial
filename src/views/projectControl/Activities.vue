<script setup lang="ts">
import { ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import ActivityList from "@/components/pc/schedule/ActivityList.vue";
import DependencyList from "@/components/pc/schedule/DependencyList.vue";
import CalendarList from "@/components/pc/schedule/CalendarList.vue";
import { printPage } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";

// Activities, dependencies and working calendars of a project.
const tab = ref("activities");
const tabs = [
  { key: "activities", title: "فعالیت‌ها" },
  { key: "dependencies", title: "وابستگی‌ها" },
  { key: "calendars", title: "تقویم‌های کاری" },
];
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فعالیت‌های پروژه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">فعالیت‌ها، وابستگی‌ها و تقویم‌ها</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <ContractSelect />
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <CalendarList v-if="tab === 'calendars'" />
      <NeedContract v-else-if="!pcContractId" />
      <ActivityList v-else-if="tab === 'activities'" :contract-id="pcContractId" />
      <DependencyList v-else :contract-id="pcContractId" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
