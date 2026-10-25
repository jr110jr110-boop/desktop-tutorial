<script setup lang="ts">
// After-sales dashboard (SvDashboard): open requests by priority, SLA breaches, installed base, warranty orders and cost (protected), open orders, goodwill waiting, surveys, and the KPIs.
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import { getSvDashboard, getSvKpis } from "@/api/ServiceAPIs";
import { fail } from "@/composables/inventory";
import { badgeOfPriority, hoursText, labourText, qtyText } from "@/composables/service";

const router = useRouter();
const dash = ref<any>(null);
const kpi = ref<any>(null);
const from = ref<string | null>(null);
const to = ref<string | null>(null);

async function load() {
  const [d, e1] = await getSvDashboard();
  if (e1) fail(e1);
  else dash.value = d;
  await loadKpis();
}
async function loadKpis() {
  const [k, e2] = await getSvKpis({ From: from.value, To: to.value });
  if (e2) fail(e2);
  else kpi.value = k;
}
const hoursOrDash = (h: number | null | undefined) => (h === null || h === undefined ? "-" : hoursText(h));
const go = (path: string, query?: Record<string, string>) => router.push({ path, query });
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">داشبورد خدمات پس از فروش</h1>
        <span v-if="dash" class="inv-muted">تا تاریخ {{ dash.asOfText }}</span>
      </div>

      <div v-if="dash" class="sv-tiles mb-3">
        <div class="sv-tile" @click="go('/service/requests')"><div class="v">{{ dash.openRequests }}</div><div class="l">درخواست باز</div></div>
        <div :class="{ bad: dash.slaBreaches > 0 }" class="sv-tile" @click="go('/service/requests')"><div class="v">{{ dash.slaBreaches }}</div><div class="l">تخطی از SLA</div></div>
        <div class="sv-tile" @click="go('/service/units')"><div class="v">{{ dash.unitsInWarranty }}</div><div class="l">واحد در گارانتی</div></div>
        <div class="sv-tile" @click="go('/service/units')"><div class="v">{{ dash.unitsUnderLtsa }}</div><div class="l">واحد تحت LTSA</div></div>
        <div class="sv-tile" @click="go('/service/units')"><div class="v">{{ dash.unitsOutOfWarranty }}</div><div class="l">واحد خارج از گارانتی</div></div>
        <div :class="{ warn: dash.unitsToConfirm > 0 }" class="sv-tile" @click="go('/service/units')"><div class="v">{{ dash.unitsToConfirm }}</div><div class="l">واحد منتظر تایید</div></div>
        <div class="sv-tile" @click="go('/service/orders')"><div class="v">{{ dash.warrantyOrdersYtd }}</div><div class="l">دستور گارانتی از ابتدای سال</div></div>
        <div class="sv-tile"><div class="v sv-money">{{ labourText(dash.warrantyCostYtd, dash.warrantyCostHidden) }}</div><div class="l">هزینه گارانتی از ابتدای سال (ریال)</div></div>
        <div class="sv-tile" @click="go('/service/orders')"><div class="v">{{ dash.openOrders }}</div><div class="l">دستور خدمات باز</div></div>
        <div class="sv-tile" @click="go('/service/orders')"><div class="v">{{ dash.ordersWaitingClose }}</div><div class="l">دستور منتظر بستن</div></div>
        <div :class="{ warn: dash.goodwillWaiting > 0 }" class="sv-tile" @click="go('/service/orders')"><div class="v">{{ dash.goodwillWaiting }}</div><div class="l">حسن نیت منتظر مدیرعامل</div></div>
        <div class="sv-tile" @click="go('/service/surveys')"><div class="v">{{ dash.surveysDrafted }}</div><div class="l">نظرسنجی پیش‌نویس</div></div>
        <div class="sv-tile" @click="go('/service/surveys')">
          <div class="v">{{ dash.surveyAverageScore === null || dash.surveyAverageScore === undefined ? "-" : Number(dash.surveyAverageScore).toFixed(2) }}</div><div class="l">میانگین رضایت (از ۵)</div>
        </div>
      </div>

      <h3 v-if="dash" class="inv-title mb-2">درخواست‌های باز بر حسب اولویت</h3>
      <div v-if="dash" class="d-flex gap-2 flex-wrap mb-4">
        <span v-for="p in dash.openByPriority" :key="p.priority" :class="badgeOfPriority(p.priority)" class="inv-badge">{{ p.priorityText }}: {{ p.count }}</span>
        <span v-if="!(dash.openByPriority || []).length" class="inv-muted">درخواست بازی نیست.</span>
      </div>

      <h3 class="inv-title mb-2">شاخص‌ها (KPI)</h3>
      <div class="inv-filters mb-3">
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="from" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="to" /></div>
        <button class="btn btn-sm btn-secondary" @click="loadKpis">محاسبه</button>
      </div>
      <template v-if="kpi">
        <div class="row g-2 small mb-3">
          <div class="col-md-3"><b>درخواست‌های بررسی‌شده:</b> {{ kpi.requestsCounted }}</div>
          <div class="col-md-3"><b>میانگین زمان اولین پاسخ:</b> {{ hoursOrDash(kpi.firstResponseHours) }} ساعت</div>
          <div class="col-md-3"><b>میانگین زمان رفع:</b> {{ hoursOrDash(kpi.resolutionHours) }} ساعت</div>
          <div class="col-md-3"><b>MTBF کل:</b> {{ kpi.mtbfHours === null || kpi.mtbfHours === undefined ? "-" : qtyText(kpi.mtbfHours) }} ساعت</div>
          <div class="col-md-3"><b>جمع خرابی‌ها:</b> {{ kpi.totalFailures }}</div>
          <div class="col-md-3"><b>جمع کارکرد (ساعت):</b> {{ qtyText(kpi.totalRunningHours) }}</div>
        </div>

        <h4 class="small fw-bold">هزینه گارانتی و MTBF به تفکیک نوع محصول</h4>
        <div class="table-responsive mb-3">
          <table class="table table-sm">
            <thead><tr><th>نوع محصول</th><th>واحدها</th><th>کارکرد (ساعت)</th><th>خرابی</th><th>MTBF (ساعت)</th><th>دستور گارانتی</th><th>هزینه گارانتی (ریال)</th></tr></thead>
            <tbody>
              <tr v-if="!(kpi.byProduct || []).length"><td colspan="7" class="text-center inv-muted">داده‌ای نیست.</td></tr>
              <tr v-for="p in kpi.byProduct" :key="p.productType">
                <td>{{ p.productTypeText }}</td><td>{{ p.units }}</td><td>{{ qtyText(p.runningHours) }}</td><td>{{ p.failures }}</td>
                <td>{{ p.mtbfHours === null || p.mtbfHours === undefined ? "-" : qtyText(p.mtbfHours) }}</td><td>{{ p.warrantyOrders }}</td><td>{{ labourText(p.warrantyCost, p.warrantyCostHidden) }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <h4 class="small fw-bold">خرابی‌های تکراری</h4>
        <table class="table table-sm">
          <thead><tr><th>واحد (tag)</th><th>خرابی</th><th>تکرار</th></tr></thead>
          <tbody>
            <tr v-if="!(kpi.repeatFailures || []).length"><td colspan="3" class="text-center inv-muted">خرابی تکراری نیست.</td></tr>
            <tr v-for="r in kpi.repeatFailures" :key="r.unitId"><td><a href="#" @click.prevent="go('/service/units/' + r.unitId)"><span class="inv-ltr">{{ r.unitTag }}</span></a></td><td>{{ r.failures }}</td><td>{{ r.repeats }}</td></tr>
          </tbody>
        </table>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
<style scoped>
.sv-tiles {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 0.6rem;
}
.sv-tile {
  border: 1px solid #dee2e6;
  border-radius: 0.5rem;
  padding: 0.6rem 0.8rem;
  background: #fff;
  cursor: pointer;
}
.sv-tile.bad { background: #f8d7da; }
.sv-tile.warn { background: #fff3cd; }
.sv-tile .v { font-size: 1.5rem; font-weight: 700; }
.sv-tile .v.sv-money { font-size: 1.05rem; }
.sv-tile .l { font-size: 0.8rem; color: #495057; }
</style>
