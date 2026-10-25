<script setup lang="ts">
import { nextTick, ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import ScheduleRun from "@/components/production/floor/ScheduleRun.vue";
import LoadReport from "@/components/production/floor/LoadReport.vue";
import JobCards from "@/components/production/floor/JobCards.vue";
import DispatchList from "@/components/production/floor/DispatchList.vue";

// Planning of the shop floor: scheduler run, load report and the dispatch list with job-card printing.
const tab = ref("dispatch");
const cards = ref<InstanceType<typeof JobCards> | null>(null);
const cardRows = ref<any[]>([]);
const cardCentre = ref("");
async function printCards(rows: any[], centre: string) {
  cardRows.value = rows;
  cardCentre.value = centre;
  await nextTick();
  cards.value?.print();
}
const tabs = [
  { key: "dispatch", title: "فهرست توزیع کار" },
  { key: "load", title: "گزارش بار" },
  { key: "run", title: "اجرای برنامه‌ریزی" },
];
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar screen-only"><h1 class="inv-title">برنامه‌ریزی و بار کارگاه</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <DispatchList v-if="tab === 'dispatch'" @print="printCards" />
      <LoadReport v-else-if="tab === 'load'" />
      <ScheduleRun v-else-if="tab === 'run'" />
    </div>
    <!-- direct child of the page: the print mechanism keeps only .print-form children of .inv-page -->
    <JobCards ref="cards" :rows="cardRows" :work-center-title="cardCentre" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
