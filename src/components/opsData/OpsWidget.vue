<script setup lang="ts">
// Data-quality widget for the EPM dashboards and the CEO dashboard: open findings, exceptions this week, top rules, readiness. Read only.
import { onMounted, ref } from "vue";
import { getOpsSummary, getReadinessSummary } from "@/api/OpsDataAPIs";

const props = defineProps<{ unit?: number | null; readiness?: boolean }>();
const s = ref<any>(null);
const r = ref<any>(null);
const hidden = ref(false);

onMounted(async () => {
  const [d, e] = await getOpsSummary(props.unit ?? null);
  if (e) { hidden.value = true; return; }
  s.value = d;
  if (props.readiness) {
    const [rd, re] = await getReadinessSummary();
    if (!re) r.value = rd;
  }
});
</script>

<template>
  <div v-if="s && !hidden" class="card mb-3">
    <div class="card-header d-flex justify-content-between">
      <strong>کیفیت داده</strong>
      <router-link :to="{ name: 'OpsDataQuality' }">مشاهده</router-link>
    </div>
    <div class="card-body">
      <div class="row text-center">
        <div class="col"><div class="fs-4">{{ s.openFindings }}</div><small>یافته‌ی باز</small></div>
        <div class="col"><div class="fs-4">{{ s.openBlocks }}</div><small>مسدود</small></div>
        <div class="col"><div class="fs-4">{{ s.exceptionsThisWeek }}</div><small>استثنا در این هفته</small></div>
        <div class="col" v-if="r"><div class="fs-4">{{ r.averagePercent }}٪</div><small>آمادگی مهندسی پروژه‌ها</small></div>
        <div class="col" v-if="r"><div class="fs-4">{{ r.overdueObligations }}</div><small>تعهد معوق</small></div>
      </div>
      <ul v-if="s.topRules?.length" class="mt-2 mb-0 small">
        <li v-for="t in s.topRules" :key="t.key">{{ t.title }}: {{ t.count }}</li>
      </ul>
    </div>
  </div>
</template>
