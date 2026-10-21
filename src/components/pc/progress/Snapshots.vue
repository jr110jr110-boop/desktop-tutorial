<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { getSnapshot, getSnapshots, takeSnapshot } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import LineChart from "@/components/pc/LineChart.vue";
import JDate from "@/components/pc/JDate.vue";
import { fail, jalali, ok, rial } from "@/composables/inventory";
import { idx, pct } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Weekly snapshots (frozen progress and EVM at a data date) and the S-curve drawn from them: planned percent against physical percent.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const detail = ref<any>(null);
const dataDate = ref<string | null>(null);
const busy = ref(false);
const canTake = hasRole(ProjectControlRoles.planning);

async function load() {
  const [d, e] = await getSnapshots({ ContractId: props.contractId });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
const asc = computed(() => [...rows.value].sort((a, b) => String(a.dataDate).localeCompare(String(b.dataDate))));
const series = computed(() => [
  { name: "پیشرفت برنامه‌ای", color: "#0d6efd", dashed: true, points: asc.value.map((r) => ({ date: r.dataDate, y: Number(r.plannedPercent) })) },
  { name: "پیشرفت واقعی", color: "#198754", points: asc.value.map((r) => ({ date: r.dataDate, y: Number(r.physicalPercent) })) },
]);
async function take() {
  busy.value = true;
  const [, error] = await takeSnapshot(props.contractId, dataDate.value);
  busy.value = false;
  if (error) return fail(error);
  ok("تصویر لحظه‌ای ثبت شد.");
  load();
}
async function show(id: number) {
  const [d, e] = await getSnapshot(id);
  if (e) return fail(e);
  detail.value = d;
}
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <h2 class="inv-title mb-2">منحنی S</h2>
    <LineChart v-if="asc.length > 1" :series="series" percent unit-text="٪" />
    <div v-else class="inv-muted mb-3">برای رسم منحنی دست‌کم دو تصویر لحظه‌ای لازم است.</div>
    <div v-if="canTake" class="d-flex gap-2 align-items-end my-3 d-print-none">
      <div style="width: 170px"><label class="form-label small mb-0">تاریخ داده (اختیاری)</label><JDate v-model="dataDate" /></div>
      <button :disabled="busy" class="btn btn-sm btn-primary" @click="take">ثبت تصویر لحظه‌ای</button>
    </div>
    <table class="table table-sm table-hover">
      <thead><tr><th>تاریخ داده</th><th>واقعی</th><th>برنامه</th><th>BAC</th><th>PV</th><th>EV</th><th>AC</th><th>SPI</th><th>CPI</th><th>پایان پیش‌بینی</th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="10" class="text-center inv-muted">تصویری ثبت نشده است.</td></tr>
        <tr v-for="r in rows" :key="r.id" role="button" @click="show(r.id)">
          <td>{{ jalali(r.dataDate) }}</td><td>{{ pct(r.physicalPercent) }}</td><td>{{ pct(r.plannedPercent) }}</td><td>{{ rial(r.bac) }}</td><td>{{ rial(r.pv) }}</td><td>{{ rial(r.ev) }}</td><td>{{ rial(r.ac) }}</td>
          <td>{{ idx(r.spi) }}</td><td>{{ idx(r.cpi) }}</td><td>{{ jalali(r.forecastFinish) }}</td>
        </tr>
      </tbody>
    </table>
    <div v-if="detail" class="inv-modal-backdrop" @click.self="detail = null">
      <div class="inv-modal" style="max-width: 900px">
        <h3 class="h6">تصویر لحظه‌ای {{ jalali(detail.dataDate) }} — {{ detail.lines.length }} فعالیت</h3>
        <div style="max-height: 60vh; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>فعالیت</th><th>واقعی</th><th>برنامه</th><th>بودجه</th><th>PV</th><th>EV</th><th>پایان پیش‌بینی</th></tr></thead>
            <tbody><tr v-for="l in detail.lines" :key="l.activityId"><td class="inv-ltr">{{ l.activityCode }}</td><td>{{ pct(l.percent) }}</td><td>{{ pct(l.plannedPercent) }}</td><td>{{ rial(l.budget) }}</td><td>{{ rial(l.pv) }}</td><td>{{ rial(l.ev) }}</td><td>{{ jalali(l.forecastFinish) }}</td></tr></tbody>
          </table>
        </div>
        <div class="small inv-muted inv-ltr mt-1">hash: {{ detail.hash }}</div>
        <div class="text-start mt-2"><button class="btn btn-sm btn-secondary" @click="detail = null">بستن</button></div>
      </div>
    </div>
  </div>
</template>
