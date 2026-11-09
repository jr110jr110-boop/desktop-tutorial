<script setup lang="ts">
// CEO dashboard widget of the AI internal auditor: open findings by severity, overdue, and what waits for the CEO. Read only.
import { onMounted, ref } from "vue";
import { getStatus } from "@/api/AuditAPIs";

const s = ref<any>(null);
onMounted(async () => {
  const [d, e] = await getStatus();
  if (!e) s.value = d;
});
</script>

<template>
  <div v-if="s" class="card mb-3">
    <div class="card-header d-flex justify-content-between">
      <strong>ممیز داخلی هوشمند</strong>
      <router-link :to="{ name: 'AuditFindings' }">مشاهده</router-link>
    </div>
    <div class="card-body">
      <div class="row text-center">
        <div class="col"><div class="fs-4">{{ s.open }}</div><small>یافته‌ی باز</small></div>
        <div class="col"><div class="fs-4 text-danger">{{ s.openMajor }}</div><small>عمده</small></div>
        <div class="col"><div class="fs-4">{{ s.openMinor }}</div><small>جزئی</small></div>
        <div class="col"><div class="fs-4">{{ s.openObservation }}</div><small>مشاهده</small></div>
        <div class="col"><div class="fs-4 text-danger">{{ s.overdue }}</div><small>گذشته از مهلت</small></div>
        <div class="col"><div class="fs-4">{{ s.waitingCeo }}</div><small>منتظر تایید مدیرعامل</small></div>
      </div>
      <div class="small inv-muted mt-2">آخرین اجرا: {{ s.lastRunAtText || "-" }} ({{ s.lastRunStatus || "-" }})؛ قواعد فعال {{ s.rulesEnabled }} از {{ s.rulesTotal }}</div>
    </div>
  </div>
</template>
