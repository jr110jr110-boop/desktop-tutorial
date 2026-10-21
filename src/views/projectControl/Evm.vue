<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import JDate from "@/components/pc/JDate.vue";
import LineChart from "@/components/pc/LineChart.vue";
import { addCostEntry, getCashFlow, getCostEntries, getEvm } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, printPage, rial, today } from "@/composables/inventory";
import { costKinds, fx, idx, pcContractId, pct, statusBadge, statusText } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Earned value management: values at the data date, indices with the colour from the API thresholds, forecasts, cash-flow curves and the cost entries.
const asOf = ref<string | null>(today());
const evm = ref<any>(null);
const cash = ref<any[]>([]);
const costs = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const canCost = hasRole(ProjectControlRoles.costs);

async function load() {
  if (!pcContractId.value) return;
  const [[e, e1], [c], [k]] = await Promise.all([getEvm(pcContractId.value, asOf.value), getCashFlow(pcContractId.value), getCostEntries({ ContractId: pcContractId.value })]);
  if (e1) fail(e1);
  else evm.value = e;
  cash.value = Array.isArray(c) ? c : [];
  costs.value = rowsOf(k);
}
const series = computed(() => [
  { name: "هزینه برنامه‌ای تجمعی", color: "#0d6efd", dashed: true, points: cash.value.map((p) => ({ date: p.date, y: Number(p.plannedCumulative) })) },
  { name: "هزینه واقعی تجمعی", color: "#dc3545", points: cash.value.map((p) => ({ date: p.date, y: Number(p.actualCostCumulative) })) },
  { name: "ارزش کسب‌شده", color: "#198754", points: cash.value.map((p) => ({ date: p.date, y: p.earnedValue === null ? null : Number(p.earnedValue) })) },
]);
const money = computed(() => {
  const e = evm.value;
  if (!e) return [];
  return [
    ["BAC بودجه تا تکمیل", e.bac], ["ذخیره مدیریتی", e.managementReserve], ["PV ارزش برنامه‌ای", e.pv], ["EV ارزش کسب‌شده", e.ev], ["AC هزینه واقعی", e.ac],
    ["CV انحراف هزینه", e.cv], ["SV انحراف زمانی", e.sv], ["EAC (روند فعلی)", e.eacTypical], ["EAC (انحراف یک‌باره)", e.eacAtypical], ["EAC (CPI×SPI)", e.eacCpiSpi],
    ["ETC باقیمانده", e.etcTypical], ["VAC انحراف در پایان", e.vacTypical],
  ];
});
async function save() {
  busy.value = true;
  const [, error] = await addCostEntry({ ...form.value, contractId: pcContractId.value });
  busy.value = false;
  if (error) return fail(error);
  ok("هزینه ثبت شد.");
  form.value = null;
  load();
}
onMounted(load);
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="ارزش کسب‌شده (EVM)" subtitle="مبالغ به ریال" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">ارزش کسب‌شده و جریان نقدی</h1>
        <div class="d-flex flex-wrap gap-2 align-items-end d-print-none">
          <ContractSelect @change="load" />
          <div style="width: 160px"><JDate v-model="asOf" /></div>
          <button class="btn btn-sm btn-secondary" @click="load">محاسبه</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else-if="evm">
        <div class="row g-2 mb-3">
          <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="inv-muted">SPI</div><span :class="statusBadge(evm.spiStatus)" class="inv-badge fs-6">{{ idx(evm.spi) }}</span> <small>{{ statusText(evm.spiStatus) }}</small></div></div>
          <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="inv-muted">CPI</div><span :class="statusBadge(evm.cpiStatus)" class="inv-badge fs-6">{{ idx(evm.cpi) }}</span> <small>{{ statusText(evm.cpiStatus) }}</small></div></div>
          <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="inv-muted">SPI(t) زمان کسب‌شده</div><b>{{ idx(evm.spiTime) }}</b><div class="small">ES = {{ fx(evm.earnedScheduleDays) }} روز، زمان واقعی = {{ evm.actualTimeDays }} روز، انحراف = {{ fx(evm.scheduleVarianceDays) }}</div></div></div>
          <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="inv-muted">TCPI</div><b>{{ idx(evm.tcpi) }}</b> <small>(نسبت به EAC: {{ idx(evm.tcpiEac) }})</small></div></div>
        </div>
        <div v-if="evm.correctiveActionRequired" class="alert alert-danger py-2">شاخص هزینه یا زمان در محدوده قرمز است؛ اقدام اصلاحی لازم است.</div>
        <div v-if="evm.rescheduleRequired" class="alert alert-warning py-2">زمان‌بندی مجدد لازم است.</div>
        <div v-if="evm.note" class="alert alert-info py-2">{{ evm.note }}</div>
        <div class="row">
          <div class="col-lg-6">
            <table class="table table-sm">
              <tbody>
                <tr v-for="m in money" :key="m[0]"><th>{{ m[0] }}</th><td class="text-start">{{ m[1] === null ? "—" : rial(m[1]) }}</td></tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <table class="table table-sm">
              <tbody>
                <tr><th>تاریخ داده</th><td>{{ jalali(evm.asOf) }}</td></tr>
                <tr><th>پیشرفت واقعی / برنامه‌ای</th><td>{{ pct(evm.physicalPercent) }} / {{ pct(evm.plannedPercent) }}</td></tr>
                <tr><th>پایان پیش‌بینی</th><td>{{ jalali(evm.forecastFinish) }}</td></tr>
                <tr><th>پایان خط مبنا</th><td>{{ jalali(evm.baselineFinish) }}</td></tr>
                <tr><th>مبنای محاسبه</th><td>{{ evm.fromBaseline ? "خط مبنای جاری" : "بدون خط مبنا (برآورد)" }}</td></tr>
              </tbody>
            </table>
          </div>
        </div>
        <h2 class="inv-title mb-2">جریان نقدی</h2>
        <LineChart v-if="cash.length > 1" :series="series" unit-text="" />
        <div v-else class="inv-muted">داده‌ای برای نمودار نیست (خط مبنا و هزینه لازم است).</div>

        <div class="d-flex justify-content-between align-items-center mt-4 mb-2">
          <h2 class="inv-title mb-0">هزینه‌های ثبت‌شده</h2>
          <button v-if="canCost" class="btn btn-sm btn-primary d-print-none" @click="form = { kind: 1, entryDate: today(), amount: 0, description: '' }">ثبت هزینه</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>نوع</th><th>مبلغ</th><th>منبع</th><th>شرح</th></tr></thead>
          <tbody>
            <tr v-if="!costs.length"><td colspan="5" class="text-center inv-muted">هزینه‌ای ثبت نشده است.</td></tr>
            <tr v-for="c in costs" :key="c.id"><td>{{ jalali(c.entryDate) }}</td><td>{{ costKinds[c.kind] }}</td><td>{{ rial(c.amount) }}</td><td>{{ c.sourceType }} {{ c.sourceNumber }}</td><td>{{ c.description }}</td></tr>
          </tbody>
        </table>
      </template>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h3 class="h6">ثبت هزینه</h3>
        <label class="form-label small mb-0">نوع</label>
        <select v-model.number="form.kind" class="form-select form-select-sm mb-2"><option v-for="(n, k) in costKinds" :key="k" :value="Number(k)">{{ n }}</option></select>
        <label class="form-label small mb-0">تاریخ</label><JDate v-model="form.entryDate" class="mb-2" />
        <label class="form-label small mb-0">مبلغ (ریال)</label><input v-model.number="form.amount" class="form-control form-control-sm mb-2" type="number" min="0" />
        <label class="form-label small mb-0">شرح</label><input v-model="form.description" class="form-control form-control-sm mb-3" />
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy || !form.amount" class="btn btn-sm btn-primary" @click="save">ثبت</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
