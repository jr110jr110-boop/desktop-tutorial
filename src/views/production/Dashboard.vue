<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import { getProductionDashboard } from "@/api/ProductionAPIs";
import { fail } from "@/composables/inventory";
import { machineStatusKeys, percentText, persianOf, qtyText } from "@/composables/production";

// Production dashboard (MfDashboard): tiles with the counts and the lists behind them; every row links to its page.
const router = useRouter();
const asOf = ref<string | null>(null);
const d = ref<any>(null);
const loading = ref(false);

async function load() {
  loading.value = true;
  const [data, error] = await getProductionDashboard(asOf.value);
  loading.value = false;
  if (error) return fail(error);
  d.value = data;
}
const go = (path: string) => router.push(path);
const arr = (v: any): any[] => (Array.isArray(v) ? v : []);

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">داشبورد تولید</h1>
          <span v-if="d" class="inv-muted">وضعیت تا {{ persianOf(d.asOfText, d.asOf) }}<span v-if="d.lastScheduleRunText"> - آخرین زمان‌بندی: {{ d.lastScheduleRunText }}</span></span>
        </div>
        <div class="d-flex gap-2 align-items-end">
          <div><label class="form-label small mb-0">تاریخ مبنا (خالی = امروز)</label><FnDate v-model="asOf" /></div>
          <button :disabled="loading" class="btn btn-sm btn-outline-secondary" @click="load">بازخوانی</button>
        </div>
      </div>

      <template v-if="d">
        <div class="mf-tiles mb-3">
          <button :class="{ warn: d.lateOrders > 0 }" class="mf-tile" @click="go('/production/orders')"><span class="mf-value">{{ d.lateOrders }}</span><span class="mf-title">سفارش تولید با تاخیر</span></button>
          <button :class="{ warn: d.qcHoldOverTwoDays > 0 }" class="mf-tile" @click="go('/production/orders')"><span class="mf-value">{{ d.qcHoldOverTwoDays }}</span><span class="mf-title">بازرسی معطل بیش از ۲ روز</span></button>
          <button :class="{ warn: d.overloadWeeks > 0 }" class="mf-tile" @click="go('/production/schedule')"><span class="mf-value">{{ d.overloadWeeks }}</span><span class="mf-title">هفته پرکار (بار بیش از ظرفیت)</span></button>
          <button :class="{ warn: d.kitShortages > 0 }" class="mf-tile" @click="go('/production/orders')"><span class="mf-value">{{ d.kitShortages }}</span><span class="mf-title">کمبود کیت مواد</span></button>
          <button :class="{ warn: d.machinesDown > 0 }" class="mf-tile" @click="go('/production/work-centers')"><span class="mf-value">{{ d.machinesDown }}</span><span class="mf-title">ماشین از کار افتاده</span></button>
          <button :class="{ warn: d.maintenanceOverdue > 0 }" class="mf-tile" @click="go('/production/maintenance')"><span class="mf-value">{{ d.maintenanceOverdue }}</span><span class="mf-title">نگهداری عقب‌افتاده</span></button>
          <button class="mf-tile" @click="go('/production/orders')">
            <span class="mf-value">{{ percentText(d.scrapRatePercent) }}</span><span class="mf-title">نرخ ضایعات این ماه</span>
            <span class="mf-sub">سالم {{ qtyText(d.monthGoodQty) }} / ضایعات {{ qtyText(d.monthScrapQty) }}</span>
          </button>
          <button class="mf-tile" @click="go('/production/oee')"><span class="mf-value">{{ percentText(d.oeePercent) }}</span><span class="mf-title">OEE</span></button>
        </div>

        <div class="row g-3">
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">سفارش‌های با تاخیر</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>سفارش</th><th>کالا</th><th>روز تاخیر</th><th>تاریخ نیاز</th><th>پایان برنامه</th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.lateOrderList).length"><td colspan="5" class="text-center inv-muted">موردی نیست.</td></tr>
                <tr v-for="o in arr(d.lateOrderList)" :key="o.orderId" class="mf-link" @click="go('/production/orders/' + o.orderId)">
                  <td class="inv-ltr">{{ o.number }}</td><td class="inv-ltr">{{ o.itemCode }}</td><td>{{ o.daysLate }}</td><td>{{ o.needDateText }}</td><td>{{ o.plannedFinishText }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">بازرسی معطل بیش از ۲ روز</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>کارت کار</th><th>مرحله</th><th>روز انتظار</th><th>زمان درخواست</th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.qcHoldList).length"><td colspan="4" class="text-center inv-muted">موردی نیست.</td></tr>
                <tr v-for="q in arr(d.qcHoldList)" :key="q.operationId" class="mf-link" @click="go('/production/operations/' + q.operationId)">
                  <td class="inv-ltr">{{ q.jobCard }}</td><td>{{ q.title }}</td><td>{{ q.daysOnHold }}</td><td>{{ q.requestedAtText }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">هفته‌های پرکار</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>مرکز کار</th><th>هفته (شروع)</th><th>ظرفیت</th><th>بار</th><th>درصد بار</th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.overloadList).length"><td colspan="5" class="text-center inv-muted">موردی نیست.</td></tr>
                <tr v-for="(c, i) in arr(d.overloadList)" :key="i" class="mf-link" @click="go('/production/schedule')">
                  <td class="inv-ltr">{{ c.workCenterCode }}</td><td>{{ persianOf(c.weekStartText, c.weekStart) }}</td><td>{{ qtyText(c.capacityHours) }}</td><td>{{ qtyText(c.loadHours) }}</td>
                  <td><span class="inv-badge bad">{{ percentText(c.loadPercent) }}</span></td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">کمبود کیت مواد</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>سفارش</th><th>کالا</th><th>تاریخ نیاز</th><th>پایان برنامه</th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.kitShortageList).length"><td colspan="4" class="text-center inv-muted">موردی نیست.</td></tr>
                <tr v-for="o in arr(d.kitShortageList)" :key="o.orderId" class="mf-link" @click="go('/production/orders/' + o.orderId)">
                  <td class="inv-ltr">{{ o.number }}</td><td class="inv-ltr">{{ o.itemCode }}</td><td>{{ o.needDateText }}</td><td>{{ o.plannedFinishText }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">ماشین‌های از کار افتاده</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>ماشین</th><th>مرکز کار</th><th>وضعیت</th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.machinesDownList).length"><td colspan="3" class="text-center inv-muted">موردی نیست.</td></tr>
                <tr v-for="m in arr(d.machinesDownList)" :key="m.machineId" class="mf-link" @click="go('/production/maintenance')">
                  <td>{{ m.title }}</td><td class="inv-ltr">{{ m.workCenterCode }}</td><td><span class="inv-badge bad">{{ machineStatusKeys[m.status] || m.status }}</span></td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">نگهداری عقب‌افتاده</h2>
            <p class="mb-1">{{ d.maintenanceOverdue }} دستور نگهداری از سررسید گذشته است.</p>
            <button class="btn btn-sm btn-outline-primary" @click="go('/production/maintenance')">رفتن به نگهداری و تعمیرات</button>
          </div>
          <div class="col-12">
            <h2 class="inv-title mb-2">OEE مراکز کار</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>مرکز کار</th><th>دسترس‌پذیری</th><th>عملکرد</th><th>کیفیت</th><th>OEE</th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.oee).length"><td colspan="5" class="text-center inv-muted">داده‌ای نیست.</td></tr>
                <tr v-for="r in arr(d.oee)" :key="r.workCenterId" class="mf-link" @click="go('/production/oee')">
                  <td><span class="inv-ltr">{{ r.workCenterCode }}</span> {{ r.workCenterTitle }}</td><td>{{ percentText(r.availabilityPercent) }}</td><td>{{ percentText(r.performancePercent) }}</td><td>{{ percentText(r.qualityPercent) }}</td><td>{{ percentText(r.oeePercent) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>
      <p v-else-if="loading" class="inv-muted">در حال بارگذاری...</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
<style scoped>
.mf-tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 0.6rem; }
.mf-tile { display: flex; flex-direction: column; gap: 0.15rem; text-align: right; padding: 0.7rem 0.8rem; border: 1px solid #dfe3e8; border-radius: 8px; background: #fff; }
.mf-tile.warn { border-color: #e0a800; background: #fff9e6; }
.mf-value { font-size: 1.5rem; font-weight: 700; }
.mf-title { font-size: 0.85rem; color: #555; }
.mf-sub { font-size: 0.75rem; color: #777; }
.mf-link { cursor: pointer; }
</style>
