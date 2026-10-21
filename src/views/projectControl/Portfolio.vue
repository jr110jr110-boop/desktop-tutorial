<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getGuaranteeAlerts, getOverdueInitiation, getPcDashboard, getPortfolio } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, num, printPage, rial } from "@/composables/inventory";
import { contractLabel, idx, initiationSteps, pct, setPcContract, statusBadge, statusText } from "@/composables/projectControl";

// Portfolio (PMO) dashboard: every project with SPI / CPI colour (amber and red thresholds are set in the API), late activities,
// open change requests, guarantee alerts, overdue payment milestones and penalty exposure.
const router = useRouter();
const dash = ref<any>(null);
const rows = ref<any[]>([]);
const alerts = ref<any[]>([]);
const overdue = ref<any[]>([]);
const loading = ref(false);

async function load() {
  loading.value = true;
  const [[d, e1], [p, e2], [a], [o]] = await Promise.all([getPcDashboard(), getPortfolio(), getGuaranteeAlerts(), getOverdueInitiation()]);
  loading.value = false;
  if (e1) fail(e1);
  else dash.value = d;
  if (e2) fail(e2);
  else rows.value = rowsOf(p);
  alerts.value = Array.isArray(a) ? a : [];
  overdue.value = Array.isArray(o) ? o : [];
}

const tiles = computed(() => {
  const d = dash.value;
  if (!d) return [];
  return [
    { title: "پروژه‌های فعال", value: num(d.activeProjects) },
    { title: "پروژه قرمز (SPI یا CPI)", value: num(d.projectsRed), warn: d.projectsRed > 0, bad: d.projectsRed > 0 },
    { title: "پروژه زرد (SPI یا CPI)", value: num(d.projectsAmber), warn: d.projectsAmber > 0 },
    { title: "فعالیت دارای تاخیر", value: num(d.lateActivities), warn: d.lateActivities > 0 },
    { title: "درخواست تغییر باز", value: num(d.openChangeRequests), sub: d.overdueChangeRequests + " از مهلت گذشته", warn: d.overdueChangeRequests > 0 },
    { title: "هشدار ضمانت‌نامه", value: num(d.guaranteeAlerts), warn: d.guaranteeAlerts > 0 },
    { title: "مرحله پرداخت سررسیدشده", value: num(d.overdueMilestones), warn: d.overdueMilestones > 0 },
    { title: "گام شروع پروژه از مهلت گذشته", value: num(d.overdueInitiationSteps), warn: d.overdueInitiationSteps > 0 },
    { title: "پاسخ کارفرما از مهلت گذشته", value: num(d.overdueClientResponses), warn: d.overdueClientResponses > 0 },
    { title: "جریمه محاسبه‌شده (ریال)", value: rial(d.penaltyExposure), warn: d.penaltyExposure > 0 },
  ];
});

const sorted = computed(() => [...rows.value].sort((a, b) => (a.spi ?? 9) - (b.spi ?? 9)));
const levelBadge = (l: number) => (l === 0 ? "bad" : l <= 7 ? "bad" : l <= 14 ? "warn" : "info");

function open(contractId: number, name = "PcProject") {
  setPcContract(contractId);
  router.push({ name });
}

onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="داشبورد سبد پروژه‌ها (PMO)" subtitle="مبالغ به ریال" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title screen-only">داشبورد سبد پروژه‌ها (PMO)</h1>
          <span class="inv-muted d-print-none">آستانه‌های زرد و قرمز SPI و CPI در تنظیمات API تعیین شده است (پیش‌فرض: زرد کمتر از ۰٫۹۵ و قرمز کمتر از ۰٫۹۰).</span>
        </div>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" :disabled="loading" @click="load">بازخوانی</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>

      <div v-if="tiles.length" class="pc-tiles mb-3">
        <div v-for="t in tiles" :key="t.title" class="pc-tile" :class="{ warn: t.warn, bad: t.bad }">
          <span class="pc-value">{{ t.value }}</span>
          <span class="pc-title">{{ t.title }}</span>
          <span v-if="t.sub" class="pc-sub">{{ t.sub }}</span>
        </div>
      </div>

      <h2 class="inv-title mb-2">پروژه‌ها</h2>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th>پروژه</th><th>پیشرفت واقعی</th><th>پیشرفت برنامه</th><th>SPI</th><th>CPI</th><th>پایان پیش‌بینی</th>
              <th>فعالیت تاخیردار</th><th>تغییر باز</th><th>ضمانت‌نامه</th><th>مرحله پرداخت</th><th>گام شروع</th><th>پاسخ کارفرما</th><th>جریمه</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="!sorted.length"><td colspan="13" class="text-center inv-muted">پروژه‌ای با شرایط قرارداد ثبت نشده است.</td></tr>
            <tr v-for="r in sorted" :key="r.contractId" role="button" @click="open(r.contractId)">
              <td><span class="inv-ltr">{{ r.projectCode }}</span> {{ r.contractTitle }}</td>
              <td>
                <div class="pc-bar"><div :style="{ width: Math.min(100, r.physicalPercent) + '%' }"></div></div>
                {{ pct(r.physicalPercent) }}
              </td>
              <td>{{ pct(r.plannedPercent) }}</td>
              <td><span :class="statusBadge(r.spiStatus)" class="inv-badge" :title="statusText(r.spiStatus)">{{ idx(r.spi) }}</span></td>
              <td><span :class="statusBadge(r.cpiStatus)" class="inv-badge" :title="statusText(r.cpiStatus)">{{ idx(r.cpi) }}</span></td>
              <td>{{ jalali(r.forecastFinish) }}</td>
              <td><span v-if="r.lateActivities" class="inv-badge warn">{{ r.lateActivities }}</span><span v-else>0</span></td>
              <td>{{ r.openChanges }}</td>
              <td><span v-if="r.guaranteeAlerts" class="inv-badge warn">{{ r.guaranteeAlerts }}</span><span v-else>0</span></td>
              <td><span v-if="r.overdueMilestones" class="inv-badge bad">{{ r.overdueMilestones }}</span><span v-else>0</span></td>
              <td><span v-if="r.overdueInitiationSteps" class="inv-badge bad">{{ r.overdueInitiationSteps }}</span><span v-else>0</span></td>
              <td><span v-if="r.overdueClientResponses" class="inv-badge warn">{{ r.overdueClientResponses }}</span><span v-else>0</span></td>
              <td class="num">{{ rial(r.penaltyExposure) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="row g-3 mt-1">
        <div class="col-lg-6">
          <h2 class="inv-title mb-2">هشدار انقضای ضمانت‌نامه‌ها</h2>
          <div v-if="!alerts.length" class="inv-muted">هشداری نیست.</div>
          <div v-for="a in alerts" :key="a.guaranteeId" class="alert py-1 mb-1 d-flex justify-content-between" :class="levelBadge(a.daysToExpiry) === 'bad' ? 'alert-danger' : 'alert-warning'">
            <span>{{ a.title }} <span class="inv-ltr">{{ a.no }}</span> — {{ contractLabel(a.contractId) }}</span>
            <span>{{ a.daysToExpiry < 0 ? "منقضی شده" : a.daysToExpiry + " روز مانده" }} ({{ jalali(a.expiryDate) }})</span>
          </div>
        </div>
        <div class="col-lg-6">
          <h2 class="inv-title mb-2">گام‌های شروع پروژه از مهلت گذشته</h2>
          <div v-if="!overdue.length" class="inv-muted">مورد عقب‌افتاده‌ای نیست.</div>
          <div v-for="(o, i) in overdue" :key="i" class="alert alert-warning py-1 mb-1 d-flex justify-content-between">
            <span>{{ o.title || initiationSteps[o.step] }} — {{ contractLabel(Number(o.note)) }}</span>
            <span>مهلت: {{ jalali(o.dueDate) }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style scoped>
.pc-tiles {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(190px, 1fr));
  gap: 0.6rem;
}
.pc-tile {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  padding: 0.7rem 0.8rem;
  border: 1px solid #dfe3e8;
  border-radius: 8px;
  background: #fff;
}
.pc-tile.warn { border-color: #e0a800; background: #fff9e6; }
.pc-tile.bad { border-color: #dc3545; background: #fdecee; }
.pc-value { font-size: 1.25rem; font-weight: 700; }
.pc-title { font-size: 0.8rem; color: #495057; }
.pc-sub { font-size: 0.72rem; color: #6c757d; }
.pc-bar { height: 5px; background: #e9ecef; border-radius: 3px; overflow: hidden; margin-bottom: 2px; min-width: 70px; }
.pc-bar > div { height: 100%; background: #198754; }
.num { white-space: nowrap; }
</style>
