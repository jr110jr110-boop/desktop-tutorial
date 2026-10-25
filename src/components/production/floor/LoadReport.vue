<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { getLoad } from "@/api/ProductionAPIs";
import { camelize, parseAmount } from "@/composables/fn";
import { fail } from "@/composables/inventory";
import { hoursText, percentText, persianOf } from "@/composables/production";
import { hasRole, ProductionRoles } from "@/composables/roles";
import { loadWorkCenters } from "./floorCommon";

// Load report: work centre x week. capacity / demand load (unlimited capacity) / scheduled hours (finite-capacity plan). Weeks start on Saturday.
const canRead = hasRole(ProductionRoles.readers);
const from = ref<string | null>(null);
const weeks = ref(8);
const threshold = ref("100");
const wcId = ref<number | null>(null);
const workCenters = ref<any[]>([]);
const report = ref<any>(null);
const busy = ref(false);

const heads = computed(() => (report.value?.rows?.[0]?.cells || []).map((c: any) => persianOf(c.weekStartText, c.weekStart)));
const limit = computed(() => report.value?.threshold ?? (parseAmount(threshold.value) || 100));
const barWidth = (c: any) => (c.loadPercent === null || c.loadPercent === undefined ? 0 : Math.min(100, Number(c.loadPercent)));

async function load() {
  busy.value = true;
  const [d, error] = await getLoad({ From: from.value, Weeks: weeks.value, WorkCenterId: wcId.value, Threshold: parseAmount(threshold.value) || 100 });
  busy.value = false;
  if (error) return fail(error);
  report.value = camelize(d);
}

onMounted(async () => {
  workCenters.value = await loadWorkCenters();
  if (canRead) load();
});
</script>

<template>
  <div>
    <p v-if="!canRead" class="inv-muted">گزارش بار فقط برای نقش‌های مدیریتی، برنامه‌ریزی و کنترل است.</p>
    <template v-else>
      <div class="inv-filters mb-3">
        <FnDate v-model="from" placeholder="از تاریخ (خالی = امروز)" />
        <select v-model.number="weeks" class="form-select form-select-sm" style="max-width: 9rem"><option :value="4">۴ هفته</option><option :value="8">۸ هفته</option><option :value="12">۱۲ هفته</option></select>
        <input v-model="threshold" class="form-control form-control-sm fn-num" placeholder="آستانه (درصد)" style="max-width: 9rem" title="آستانه اضافه‌بار به درصد" />
        <select v-model="wcId" class="form-select form-select-sm"><option :value="null">همه مراکز کار</option><option v-for="w in workCenters" :key="w.id" :value="w.id">{{ w.title }}</option></select>
        <button :disabled="busy" class="btn btn-sm btn-secondary" @click="load">نمایش</button>
      </div>

      <template v-if="report">
        <div class="table-responsive">
          <table class="table table-sm table-bordered text-center align-middle">
            <thead>
              <tr><th class="text-start">مرکز کار</th><th v-for="(h, i) in heads" :key="i" class="inv-ltr">{{ h }}</th></tr>
            </thead>
            <tbody>
              <tr v-if="!(report.rows || []).length"><td :colspan="heads.length + 1" class="inv-muted">مرکز کاری نیست.</td></tr>
              <tr v-for="row in report.rows" :key="row.workCenterId">
                <th class="text-start text-nowrap">{{ row.title || row.code }}</th>
                <td v-for="c in row.cells" :key="c.weekStart" :class="{ 'table-danger': c.overloaded }" class="small lr-cell">
                  <div class="lr-bar"><div :class="{ over: c.overloaded }" :style="{ width: barWidth(c) + '%' }"></div></div>
                  <div class="fn-num"><b>{{ percentText(c.loadPercent, 0) }}</b></div>
                  <div class="fn-num inv-muted">ظرفیت {{ hoursText(c.capacityHours) }}</div>
                  <div class="fn-num">بار {{ hoursText(c.loadHours) }}</div>
                  <div class="fn-num">برنامه {{ hoursText(c.scheduledHours) }}</div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="inv-muted small">سلول قرمز: بار بیش از {{ limit }} درصد ظرفیت. «بار» نیاز بدون محدودیت ظرفیت و «برنامه» ساعت جای‌گذاری‌شده با ظرفیت محدود است؛ بار بدون ظرفیت با خط تیره نشان داده می‌شود.</p>

        <h2 class="h6 mt-3">فهرست اضافه‌بارها</h2>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>مرکز کار</th><th>هفته (شروع)</th><th>ظرفیت</th><th>بار</th><th>برنامه</th><th>درصد</th></tr></thead>
            <tbody>
              <tr v-if="!(report.overloads || []).length"><td colspan="6" class="text-center inv-muted">اضافه‌باری نیست.</td></tr>
              <tr v-for="(c, i) in report.overloads || []" :key="i">
                <td>{{ c.workCenterCode }}</td><td class="inv-ltr">{{ persianOf(c.weekStartText, c.weekStart) }}</td>
                <td class="fn-num">{{ hoursText(c.capacityHours) }}</td><td class="fn-num">{{ hoursText(c.loadHours) }}</td><td class="fn-num">{{ hoursText(c.scheduledHours) }}</td>
                <td><span class="inv-badge bad">{{ percentText(c.loadPercent, 0) }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </template>
  </div>
</template>

<style scoped>
.lr-cell { min-width: 7.5rem; line-height: 1.35; }
.lr-bar { height: 6px; background: #e9ecef; border-radius: 3px; overflow: hidden; margin-bottom: 3px; }
.lr-bar > div { height: 100%; background: #198754; }
.lr-bar > div.over { background: #dc3545; }
</style>
