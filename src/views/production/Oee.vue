<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { lastDays } from "@/components/production/mnt/mntCommon";
import { getOee, getWorkCenters } from "@/api/ProductionAPIs";
import { camelize } from "@/composables/fn";
import { fail } from "@/composables/inventory";
import { dateText, hoursText, percentText, qtyText } from "@/composables/production";

// OEE per work centre for a Persian date range: availability x performance x quality. The server computes every figure; the page only shows them.
const range = lastDays(30);
const filters = ref<{ From: string | null; To: string | null; WorkCenterId: number | null }>({ From: range.from, To: range.to, WorkCenterId: null });
const rows = ref<any[]>([]);
const workCenters = ref<any[]>([]);
const loading = ref(false);
const shownRange = ref({ from: range.from, to: range.to });

// OEE of a bar: colour by level (the server's OEE can be null when nothing was booked)
const bars = computed(() => rows.value.filter((r) => r.oeePercent !== null && r.oeePercent !== undefined));
const badgeOfOee = (v: number | null) => (v === null || v === undefined ? "" : v >= 85 ? "ok" : v >= 60 ? "warn" : "bad");

async function load() {
  if (filters.value.From && filters.value.To && filters.value.From > filters.value.To) return fail("تاریخ شروع بعد از تاریخ پایان است.");
  loading.value = true;
  const [data, error] = await getOee({ From: filters.value.From, To: filters.value.To, WorkCenterId: filters.value.WorkCenterId });
  loading.value = false;
  if (error) return fail(error);
  const d = camelize(data);
  rows.value = Array.isArray(d) ? d : d?.data || [];
  shownRange.value = { from: filters.value.From || "", to: filters.value.To || "" };
}

onMounted(async () => {
  const [data] = await getWorkCenters({ PageNumber: 1, PageSize: 500, IsActive: true });
  const d = camelize(data);
  workCenters.value = Array.isArray(d) ? d : d?.data || [];
  load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">اثربخشی کلی تجهیزات (OEE)</h1></div>
      <div class="inv-filters mb-3 align-items-end">
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
        <select v-model="filters.WorkCenterId" class="form-select form-select-sm">
          <option :value="null">همه مراکز کار</option>
          <option v-for="w in workCenters" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
        </select>
        <button :disabled="loading" class="btn btn-sm btn-secondary" @click="load">نمایش</button>
      </div>
      <p class="inv-muted small">دوره: {{ dateText(shownRange.from) || "-" }} تا {{ dateText(shownRange.to) || "-" }}</p>

      <div v-if="bars.length" class="oee-bars mb-3">
        <div v-for="r in bars" :key="r.workCenterId" class="oee-row">
          <span class="oee-name"><span class="inv-ltr">{{ r.workCenterCode }}</span> {{ r.workCenterTitle }}</span>
          <span class="oee-track"><span :class="badgeOfOee(r.oeePercent)" :style="{ width: Math.min(100, Math.max(0, r.oeePercent)) + '%' }" class="oee-fill"></span></span>
          <span class="oee-val fn-num">{{ percentText(r.oeePercent) }}</span>
        </div>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr><th>مرکز کار</th><th>ساعت برنامه‌ای</th><th>ساعت توقف</th><th>ساعت تولید ثبت‌شده</th><th>ساعت استاندارد</th><th>سالم</th><th>ضایعات</th><th>دسترس‌پذیری</th><th>عملکرد</th><th>کیفیت</th><th>OEE</th></tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td colspan="11" class="text-center inv-muted">در حال بارگذاری...</td></tr>
            <tr v-if="!loading && !rows.length"><td colspan="11" class="text-center inv-muted">داده‌ای نیست.</td></tr>
            <tr v-for="r in rows" :key="r.workCenterId">
              <td><span class="inv-ltr">{{ r.workCenterCode }}</span> {{ r.workCenterTitle }}</td>
              <td>{{ hoursText(r.plannedHours) }}</td><td>{{ hoursText(r.downtimeHours) }}</td><td>{{ hoursText(r.bookedRunHours) }}</td><td>{{ hoursText(r.standardHours) }}</td>
              <td>{{ qtyText(r.goodQty) }}</td><td>{{ qtyText(r.scrapQty) }}</td>
              <td>{{ percentText(r.availabilityPercent) }}</td><td>{{ percentText(r.performancePercent) }}</td><td>{{ percentText(r.qualityPercent) }}</td>
              <td><span v-if="r.oeePercent !== null && r.oeePercent !== undefined" :class="badgeOfOee(r.oeePercent)" class="inv-badge">{{ percentText(r.oeePercent) }}</span><span v-else>-</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="inv-muted small mb-0">OEE = دسترس‌پذیری × عملکرد × کیفیت؛ بدون زمان تولید یا بدون قطعه، عملکرد، کیفیت و OEE خالی (-) است. ساعت‌ها به صورت ساعت:دقیقه نمایش داده می‌شوند.</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
<style scoped>
.oee-row { display: flex; align-items: center; gap: 0.6rem; margin-bottom: 0.3rem; }
.oee-name { width: 14rem; flex: none; font-size: 0.85rem; }
.oee-track { flex: 1; height: 0.9rem; background: #eef0f3; border-radius: 4px; overflow: hidden; }
.oee-fill { display: block; height: 100%; background: #6c757d; }
.oee-fill.ok { background: #198754; }
.oee-fill.warn { background: #e0a800; }
.oee-fill.bad { background: #dc3545; }
.oee-val { width: 4rem; flex: none; text-align: left; }
</style>
