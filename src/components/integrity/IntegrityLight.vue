<script setup lang="ts">
// The CEO dashboard light of Package D: the audit chain and the last successful restore test (red when it is older than 35 days or never made). Opens «سلامت سوابق».
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { getIntegritySummary, type IntegritySummary } from "@/api/IntegrityAPIs";

const router = useRouter();
const summary = ref<IntegritySummary | null>(null);
onMounted(async () => {
  const [data, error] = await getIntegritySummary();
  if (!error) summary.value = data;
});
</script>

<template>
  <div v-if="summary" class="integrity-light" role="button" title="سلامت سوابق" @click="router.push({ name: 'Integrity' })">
    <span :class="summary.chainStatus === 'Ok' ? 'good' : 'red'" class="dot"></span>
    <span>زنجیره سوابق: {{ summary.chainStatusText }}</span>
    <span class="sep">|</span>
    <span :class="summary.red ? 'red' : 'good'" class="dot"></span>
    <span>
      آزمون بازیابی پشتیبان:
      <template v-if="summary.lastRestoreSuccessAtText">آخرین موفق {{ summary.lastRestoreSuccessAtText }}</template>
      <template v-else>انجام نشده</template>
      <template v-if="summary.red"> (قرمز: بیش از {{ summary.restoreWarnDays }} روز)</template>
    </span>
  </div>
</template>

<style scoped>
.integrity-light { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; cursor: pointer; padding: 6px 12px; border: 1px solid rgba(128, 128, 128, 0.35); border-radius: 8px; font-size: 13px; margin: 8px 0; }
.dot { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
.dot.good { background: #2e9e5b; }
.dot.red { background: #d93025; }
.sep { opacity: 0.4; }
</style>
