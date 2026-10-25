<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import JDate from "@/components/pc/JDate.vue";
import { generateReport, getReport, getReports, releaseReport } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, rial } from "@/composables/inventory";
import ClientReportPrint from "@/components/pc/print/ClientReportPrint.vue";
import WeeklyReportPrint from "@/components/pc/print/WeeklyReportPrint.vue";
import CeoReportPrint from "@/components/pc/print/CeoReportPrint.vue";
import { contractLabel, idx, milestoneLine, parseContent, pcContractId, pct, phases, reportKinds, usePcPrint } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import AiNotesBox from "@/components/ai/AiNotesBox.vue";

// Periodic reports. The content is frozen when the report is generated (hash); the project manager releases it. The client report shows SPI and CPI only when the contract terms allow it.
const rows = ref<any[]>([]);
const kind = ref<number | null>(null);
const gen = ref<{ kind: number; date: string | null }>({ kind: 1, date: null });
const d = ref<any>(null);
const busy = ref(false);
const canGenerate = hasRole(ProjectControlRoles.reportGenerate);
const canRelease = hasRole(ProjectControlRoles.projectManagers);
const canAiNotes = hasRole(ProjectControlRoles.planning);
const kinds = [1, 2, 3];
const { job, run } = usePcPrint();

async function load() {
  if (!pcContractId.value) return;
  const [r, e] = await getReports({ ContractId: pcContractId.value, Kind: kind.value });
  if (e) return fail(e);
  rows.value = rowsOf(r);
}
async function generate() {
  busy.value = true;
  const [r, e] = await generateReport(pcContractId.value!, gen.value.kind, gen.value.date);
  busy.value = false;
  if (e) return fail(e);
  ok("گزارش " + r.number + " ساخته شد.");
  load();
  show(r.id);
}
async function show(id: number) {
  const [r, e] = await getReport(id);
  if (e) return fail(e);
  d.value = { ...r, c: parseContent(r.content) };
}
async function release() {
  busy.value = true;
  const [r, e] = await releaseReport(d.value.id);
  busy.value = false;
  if (e) return fail(e);
  ok("گزارش منتشر شد.");
  d.value = { ...r, c: parseContent(r.content) };
  load();
}
const evm = computed(() => d.value?.c?.evm);
onMounted(load);
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="گزارش‌های پروژه" :subtitle="contractLabel(pcContractId)" />
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title screen-only">گزارش‌های پروژه</h1><div class="d-print-none"><ContractSelect /></div></div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <div v-if="canGenerate" class="border rounded p-3 mb-3 d-print-none">
          <div class="row g-2 align-items-end">
            <div class="col-md-4"><label class="form-label small mb-0">نوع گزارش</label><select v-model.number="gen.kind" class="form-select form-select-sm"><option v-for="k in kinds" :key="k" :value="k">{{ reportKinds[k] }}</option></select></div>
            <div class="col-md-3"><label class="form-label small mb-0">تاریخ داده (خالی = امروز)</label><JDate v-model="gen.date" /></div>
            <div class="col-md-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="generate">ساخت گزارش</button></div>
          </div>
        </div>
        <div class="inv-filters mb-2 d-print-none"><select v-model="kind" class="form-select form-select-sm" @change="load"><option :value="null">همه گزارش‌ها</option><option v-for="k in kinds" :key="k" :value="k">{{ reportKinds[k] }}</option></select></div>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>نوع</th><th>تاریخ داده</th><th>تاریخ ساخت</th><th>انتشار</th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="5" class="text-center inv-muted">گزارشی ساخته نشده است.</td></tr>
            <tr v-for="r in rows" :key="r.id" role="button" @click="show(r.id)"><td class="inv-ltr">{{ r.number }}</td><td>{{ reportKinds[r.kind] }}</td><td>{{ jalali(r.dataDate) }}</td><td>{{ jalali(r.generatedAt) }}</td><td><span :class="r.releasedAt ? 'ok' : 'warn'" class="inv-badge">{{ r.releasedAt ? "منتشر شد " + jalali(r.releasedAt) : "منتشرنشده" }}</span></td></tr>
          </tbody>
        </table>
      </template>
    </div>

    <ClientReportPrint v-if="job?.kind === 'report' && job.data.kind === 1" :report="job.data" />
    <WeeklyReportPrint v-else-if="job?.kind === 'report' && job.data.kind === 2" :report="job.data" />
    <CeoReportPrint v-else-if="job?.kind === 'report' && job.data.kind === 3" :report="job.data" />

    <div v-if="d" class="inv-modal-backdrop" @click.self="d = null">
      <div class="inv-modal" style="max-width: 900px; width: 96%; max-height: 92vh; overflow: auto">
        <div class="d-flex justify-content-between mb-2">
          <h3 class="h6">{{ reportKinds[d.kind] }} — <span class="inv-ltr">{{ d.number }}</span> — {{ jalali(d.dataDate) }}</h3>
          <div class="d-flex gap-2"><button v-if="canRelease && !d.releasedAt" :disabled="busy" class="btn btn-sm btn-success" @click="release">انتشار</button><button class="btn btn-sm btn-outline-secondary" @click="run('report', d)">چاپ</button><button class="btn btn-sm btn-secondary" @click="d = null">بستن</button></div>
        </div>
        <p v-if="!d.releasedAt" class="alert alert-warning py-1 small">این گزارش هنوز منتشر نشده است (برای ارسال به کارفرما باید مدیر پروژه منتشر کند).</p>
        <div v-if="!d.c" class="alert alert-danger">محتوای گزارش خوانده نشد.</div>

        <template v-else-if="d.kind === 1">
          <div class="row g-2 mb-2">
            <div class="col-md-3">پیشرفت واقعی: <b>{{ pct(d.c.physicalPercent) }}</b></div><div class="col-md-3">پیشرفت برنامه: <b>{{ pct(d.c.plannedPercent) }}</b></div>
            <div class="col-md-3">پایان خط مبنا: {{ jalali(d.c.baselineFinish) }}</div><div class="col-md-3">پایان پیش‌بینی: {{ jalali(d.c.forecastFinish) }}</div>
            <template v-if="d.c.spi != null || d.c.cpi != null"><div class="col-md-3">SPI: {{ idx(d.c.spi) }}</div><div class="col-md-3">CPI: {{ idx(d.c.cpi) }}</div></template>
          </div>
          <table class="table table-sm"><thead><tr><th>فاز</th><th>واقعی</th><th>برنامه</th><th>فعالیت</th></tr></thead><tbody><tr v-for="(p, i) in d.c.phases" :key="i"><td>{{ phases[p.phase] || "—" }}</td><td>{{ pct(p.percent) }}</td><td>{{ pct(p.plannedPercent) }}</td><td>{{ p.activities }}</td></tr></tbody></table>
          <p>مدارک ارسال‌شده: {{ d.c.documentsSubmitted }} — تاییدشده: {{ d.c.documentsApproved }} — پاسخ‌های از مهلت گذشته: {{ d.c.documentsAwaitingResponse }}</p>
          <ul><li v-for="(m, i) in d.c.milestones" :key="i">{{ milestoneLine(m) }}</li></ul>
        </template>

        <template v-else-if="d.kind === 2">
          <div class="row g-2 mb-2">
            <div class="col-md-3">پیشرفت واقعی: <b>{{ pct(d.c.progress?.physicalPercent) }}</b></div><div class="col-md-3">برنامه: <b>{{ pct(d.c.progress?.plannedPercent) }}</b></div>
            <div class="col-md-3">SPI: <b>{{ idx(evm?.spi) }}</b></div><div class="col-md-3">CPI: <b>{{ idx(evm?.cpi) }}</b></div>
            <div class="col-md-3">پایان پیش‌بینی: {{ jalali(evm?.forecastFinish) }}</div><div class="col-md-3">تغییرات باز: {{ d.c.openChanges }}</div><div class="col-md-3">هشدار ضمانت‌نامه: {{ d.c.guaranteeAlerts }}</div>
          </div>
          <table class="table table-sm"><thead><tr><th>فاز</th><th>واقعی</th><th>برنامه</th></tr></thead><tbody><tr v-for="(p, i) in d.c.progress?.byPhase" :key="i"><td>{{ phases[p.phase] || "—" }}</td><td>{{ pct(p.percent) }}</td><td>{{ pct(p.plannedPercent) }}</td></tr></tbody></table>
          <b class="small">فعالیت‌های دارای تاخیر ({{ d.c.lateActivities?.length || 0 }})</b>
          <div class="inv-ltr small">{{ (d.c.lateActivities || []).join("، ") || "—" }}</div>
        </template>

        <template v-else>
          <div class="row g-2 mb-2" v-if="d.c.row">
            <div class="col-md-3">پروژه: {{ d.c.row.projectCode }}</div><div class="col-md-3">پیشرفت واقعی: <b>{{ pct(d.c.row.physicalPercent) }}</b></div><div class="col-md-3">برنامه: {{ pct(d.c.row.plannedPercent) }}</div><div class="col-md-3">پایان پیش‌بینی: {{ jalali(d.c.row.forecastFinish) }}</div>
            <div class="col-md-3">SPI: <b>{{ idx(d.c.row.spi) }}</b></div><div class="col-md-3">CPI: <b>{{ idx(d.c.row.cpi) }}</b></div><div class="col-md-3">فعالیت تاخیردار: {{ d.c.row.lateActivities }}</div><div class="col-md-3">تغییر باز: {{ d.c.row.openChanges }}</div>
            <div class="col-md-3">هشدار ضمانت‌نامه: {{ d.c.row.guaranteeAlerts }}</div><div class="col-md-3">مرحله پرداخت سررسیدشده: {{ d.c.row.overdueMilestones }}</div><div class="col-md-3">پاسخ کارفرما از مهلت گذشته: {{ d.c.row.overdueClientResponses }}</div><div class="col-md-3">جریمه محاسبه‌شده: {{ rial(d.c.row.penaltyExposure) }}</div>
          </div>
        </template>

        <table v-if="d.c && evm && d.kind !== 1" class="table table-sm">
          <thead><tr><th>BAC</th><th>PV</th><th>EV</th><th>AC</th><th>EAC</th><th>ETC</th><th>VAC</th></tr></thead>
          <tbody><tr><td>{{ rial(evm.bac) }}</td><td>{{ rial(evm.pv) }}</td><td>{{ rial(evm.ev) }}</td><td>{{ rial(evm.ac) }}</td><td>{{ evm.eacTypical != null ? rial(evm.eacTypical) : "—" }}</td><td>{{ evm.etcTypical != null ? rial(evm.etcTypical) : "—" }}</td><td>{{ evm.vacTypical != null ? rial(evm.vacTypical) : "—" }}</td></tr></tbody>
        </table>
        <!-- narrative of the AI seat PMO-WEEKLY for the weekly report (kind 2); only PMO and planning read these notes -->
        <AiNotesBox v-if="d.kind === 2 && canAiNotes" :target-id="d.id" target-type="PcWeeklyReport" />
        <div class="small inv-muted inv-ltr">hash: {{ d.hash }}</div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
