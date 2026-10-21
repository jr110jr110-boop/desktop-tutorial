<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import moment from "moment-jalaali";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import { getActivities, getBaselines, getDependencies, getTerms } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, printPage, today } from "@/composables/inventory";
import { addDays, dateOnly, dayDiff, pcContractId, pct, phases, unitOptions } from "@/composables/projectControl";

// Gantt chart drawn with plain SVG (no chart library): grey thin bar = plan dates (the plan run of the CPM, equal to the approved baseline until the
// plan is calculated again), thick bar = current (forecast, else actual, else plan), darker part = progress, red = critical path.
const acts = ref<any[]>([]);
const deps = ref<any[]>([]);
const baseline = ref<any>(null);
const terms = ref<any>(null);
const loading = ref(false);
const f = ref({ phase: null as number | null, unit: "", onlyCritical: false, showBaseline: true, showDeps: false, text: "", scale: 6 });

async function load() {
  if (!pcContractId.value) return;
  loading.value = true;
  const [[a, e1], [d], [b], [t]] = await Promise.all([
    getActivities({ ContractId: pcContractId.value }), getDependencies({ ContractId: pcContractId.value }), getBaselines({ ContractId: pcContractId.value, PageSize: 50 }), getTerms(pcContractId.value),
  ]);
  loading.value = false;
  if (e1) return fail(e1);
  acts.value = rowsOf(a);
  deps.value = rowsOf(d);
  baseline.value = rowsOf(b).find((x: any) => x.isCurrent) || null;
  terms.value = t;
}

const ROW = 28;
const HEAD = 46;
const planStart = (a: any) => dateOnly(a.earlyStart);
const planFinish = (a: any) => dateOnly(a.earlyFinish);
const curStart = (a: any) => dateOnly(a.actualStart) || dateOnly(a.forecastStart) || planStart(a);
const curFinish = (a: any) => (a.status === 2 ? dateOnly(a.actualFinish) : null) || dateOnly(a.forecastFinish) || planFinish(a);

const rows = computed(() => {
  const t = f.value.text.trim().toLowerCase();
  return acts.value
    .filter((a) => planStart(a) || curStart(a))
    .filter((a) => (f.value.phase ? a.phase === f.value.phase : true))
    .filter((a) => (f.value.unit ? a.responsibleUnit === f.value.unit : true))
    .filter((a) => (f.value.onlyCritical ? a.isCritical : true))
    .filter((a) => (t ? `${a.code} ${a.title}`.toLowerCase().includes(t) : true))
    .sort((x, y) => String(planStart(x) || curStart(x)).localeCompare(String(planStart(y) || curStart(y))) || x.sort - y.sort);
});
const missingDates = computed(() => acts.value.filter((a) => !planStart(a)).length);

const range = computed(() => {
  const all: string[] = [];
  rows.value.forEach((a) => {
    [planStart(a), planFinish(a), curStart(a), curFinish(a)].forEach((d) => d && all.push(d));
  });
  [dateOnly(baseline.value?.projectFinish), dateOnly(terms.value?.contractDeliveryDate), today()].forEach((d) => d && all.push(d));
  if (!all.length) return null;
  all.sort();
  return { start: addDays(all[0], -3), finish: addDays(all[all.length - 1], 4) };
});
const totalDays = computed(() => (range.value ? dayDiff(range.value.start, range.value.finish) + 1 : 0));
const width = computed(() => totalDays.value * f.value.scale + 10);
const height = computed(() => HEAD + rows.value.length * ROW + 6);
const xOf = (d: string) => dayDiff(range.value!.start, d) * f.value.scale;
const xEnd = (d: string) => (dayDiff(range.value!.start, d) + 1) * f.value.scale;

const months = computed(() => {
  if (!range.value) return [];
  const out: { x: number; label: string }[] = [];
  for (let i = 0; i < totalDays.value; i++) {
    const iso = addDays(range.value.start, i);
    const m = moment(iso, "YYYY-MM-DD");
    if (i === 0 || m.jDate() === 1) out.push({ x: i * f.value.scale, label: m.format("jMMMM jYYYY") });
  }
  return out;
});
const ticks = computed(() => {
  if (!range.value) return [];
  const out: { x: number; label: string }[] = [];
  const step = f.value.scale >= 10 ? 7 : f.value.scale >= 5 ? 14 : 28;
  for (let i = 0; i < totalDays.value; i += step) out.push({ x: i * f.value.scale, label: moment(addDays(range.value.start, i), "YYYY-MM-DD").format("jMM/jDD") });
  return out;
});

interface Bar { a: any; y: number; px?: number; pw?: number; cx: number; cw: number; milestone: boolean; pct: number }
const bars = computed<Bar[]>(() => {
  if (!range.value) return [];
  return rows.value.map((a, i) => {
    const cs = curStart(a) || planStart(a)!;
    const cf = curFinish(a) || cs;
    const ps = planStart(a);
    const pf = planFinish(a);
    const cx = xOf(cs);
    const cw = Math.max(xEnd(cf) - cx, 3);
    return {
      a, y: HEAD + i * ROW, cx, cw, milestone: a.durationDays === 0,
      px: ps ? xOf(ps) : undefined, pw: ps && pf ? Math.max(xEnd(pf) - xOf(ps), 3) : undefined, pct: Math.max(0, Math.min(100, Number(a.percentCache) || 0)),
    };
  });
});
const barById = computed(() => {
  const m = new Map<number, Bar>();
  bars.value.forEach((b) => m.set(b.a.id, b));
  return m;
});
const lines = computed(() => {
  if (!f.value.showDeps || bars.value.length > 400) return [];
  return deps.value
    .map((d) => {
      const p = barById.value.get(d.predecessorId);
      const s = barById.value.get(d.successorId);
      if (!p || !s) return null;
      const x1 = p.cx + p.cw;
      const y1 = p.y + 11;
      const x2 = s.cx;
      const y2 = s.y + 11;
      return { id: d.id, path: `M${x1},${y1} L${x1 + 5},${y1} L${x1 + 5},${y2} L${Math.max(x2, x1 + 5)},${y2}`, crit: p.a.isCritical && s.a.isCritical };
    })
    .filter(Boolean) as { id: number; path: string; crit: boolean }[];
});

const mark = (d: string | null | undefined) => (d && range.value ? xOf(dateOnly(d)!) : null);
const todayX = computed(() => (range.value ? xOf(today()) : null));
const baselineX = computed(() => mark(baseline.value?.projectFinish));
const deliveryX = computed(() => mark(terms.value?.contractDeliveryDate));
const forecastEnd = computed(() => {
  const l = acts.value.map((a) => curFinish(a)).filter(Boolean) as string[];
  return l.length ? l.sort()[l.length - 1] : null;
});
const colorOf = (b: Bar) => (b.a.status === 2 ? "#198754" : b.a.isCritical ? "#dc3545" : "#0d6efd");
const tip = (a: any) =>
  `${a.code} ${a.title}\nبرنامه: ${jalali(a.earlyStart)} تا ${jalali(a.earlyFinish)}\nجاری: ${jalali(curStart(a))} تا ${jalali(curFinish(a))}\nپیشرفت: ${pct(a.percentCache)}  شناوری: ${a.totalFloat ?? "—"}${a.isCritical ? "  (بحرانی)" : ""}${a.isLate ? "  (تاخیر)" : ""}`;

onMounted(load);
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="نمودار گانت پروژه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">نمودار گانت</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <ContractSelect />
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <div class="inv-filters mb-3 d-print-none">
          <input v-model="f.text" class="form-control form-control-sm" placeholder="کد یا عنوان" />
          <select v-model="f.phase" class="form-select form-select-sm"><option :value="null">همه فازها</option><option v-for="(n, k) in phases" :key="k" :value="Number(k)">{{ n }}</option></select>
          <select v-model="f.unit" class="form-select form-select-sm"><option value="">همه واحدها</option><option v-for="u in unitOptions" :key="u[0]" :value="u[0]">{{ u[1] }}</option></select>
          <select v-model.number="f.scale" class="form-select form-select-sm"><option :value="3">مقیاس فشرده</option><option :value="6">مقیاس معمولی</option><option :value="10">مقیاس بزرگ</option><option :value="16">مقیاس بسیار بزرگ</option></select>
          <label class="form-check mb-1"><input v-model="f.onlyCritical" class="form-check-input" type="checkbox" /> فقط بحرانی</label>
          <label class="form-check mb-1"><input v-model="f.showBaseline" class="form-check-input" type="checkbox" /> نوار برنامه (خط مبنا)</label>
          <label class="form-check mb-1"><input v-model="f.showDeps" class="form-check-input" type="checkbox" /> وابستگی‌ها</label>
        </div>

        <div class="d-flex flex-wrap gap-4 small mb-2">
          <span><i class="gl" style="background: #0d6efd"></i> عادی</span>
          <span><i class="gl" style="background: #dc3545"></i> مسیر بحرانی</span>
          <span><i class="gl" style="background: #198754"></i> انجام‌شده</span>
          <span><i class="gl thin" style="background: #adb5bd"></i> برنامه / خط مبنا</span>
          <span><i class="gl line" style="border-color: #fd7e14"></i> امروز</span>
          <span><i class="gl line" style="border-color: #198754"></i> پایان خط مبنا: {{ jalali(baseline?.projectFinish) || "—" }}</span>
          <span><i class="gl line" style="border-color: #6f42c1"></i> تحویل قراردادی: {{ jalali(terms?.contractDeliveryDate) || "—" }}</span>
          <span>پایان پیش‌بینی: <b>{{ jalali(forecastEnd) || "—" }}</b></span>
        </div>
        <div v-if="missingDates" class="alert alert-warning py-2 d-print-none">{{ missingDates }} فعالیت تاریخ برنامه ندارد؛ در صفحه «کنترل زمان‌بندی» محاسبه مسیر بحرانی را اجرا کنید.</div>

        <div v-if="!range || !bars.length" class="inv-muted">{{ loading ? "در حال بارگذاری..." : "فعالیتی با تاریخ برنامه نیست." }}</div>
        <div v-else class="gantt-wrap" dir="ltr">
          <div class="gantt-list" :style="{ paddingTop: HEAD + 'px' }">
            <div v-for="b in bars" :key="b.a.id" class="gantt-name" :class="{ crit: b.a.isCritical }" :style="{ height: ROW + 'px' }" dir="rtl" :title="tip(b.a)">
              <span class="inv-ltr">{{ b.a.code }}</span> {{ b.a.title }}
            </div>
          </div>
          <div class="gantt-chart">
            <svg :width="width" :height="height" font-family="B Zar, sans-serif" font-size="11">
              <rect x="0" y="0" :width="width" :height="HEAD" fill="#f4f6f8" />
              <g v-for="m in months" :key="'m' + m.x">
                <line :x1="m.x" :x2="m.x" y1="0" :y2="height" stroke="#ced4da" />
                <text :x="m.x + 4" y="14" fill="#212529" direction="rtl">{{ m.label }}</text>
              </g>
              <g v-for="t in ticks" :key="'t' + t.x">
                <line :x1="t.x" :x2="t.x" y1="22" :y2="height" stroke="#e9ecef" />
                <text :x="t.x + 3" y="38" fill="#6c757d">{{ t.label }}</text>
              </g>
              <g v-for="(b, i) in bars" :key="'r' + b.a.id">
                <rect v-if="i % 2" x="0" :y="b.y" :width="width" :height="ROW" fill="#f8f9fa" />
              </g>
              <line v-if="todayX !== null" :x1="todayX" :x2="todayX" y1="0" :y2="height" stroke="#fd7e14" stroke-width="1.5" stroke-dasharray="4 3" />
              <line v-if="baselineX !== null" :x1="baselineX" :x2="baselineX" y1="0" :y2="height" stroke="#198754" stroke-width="1.5" stroke-dasharray="6 3" />
              <line v-if="deliveryX !== null" :x1="deliveryX" :x2="deliveryX" y1="0" :y2="height" stroke="#6f42c1" stroke-width="1.5" stroke-dasharray="2 3" />
              <path v-for="l in lines" :key="'l' + l.id" :d="l.path" fill="none" :stroke="l.crit ? '#dc3545' : '#868e96'" stroke-width="1" />
              <g v-for="b in bars" :key="b.a.id">
                <title>{{ tip(b.a) }}</title>
                <rect v-if="f.showBaseline && b.px !== undefined && !b.milestone" :x="b.px" :y="b.y + 19" :width="b.pw" height="5" rx="2" fill="#adb5bd" />
                <template v-if="b.milestone">
                  <polygon :points="`${b.cx},${b.y + 4} ${b.cx + 7},${b.y + 11} ${b.cx},${b.y + 18} ${b.cx - 7},${b.y + 11}`" :fill="colorOf(b)" />
                </template>
                <template v-else>
                  <rect :x="b.cx" :y="b.y + 5" :width="b.cw" height="12" rx="2" :fill="colorOf(b)" fill-opacity="0.3" :stroke="b.a.isLate ? '#fd7e14' : colorOf(b)" :stroke-width="b.a.isLate ? 2 : 1" />
                  <rect :x="b.cx" :y="b.y + 5" :width="(b.cw * b.pct) / 100" height="12" rx="2" :fill="colorOf(b)" />
                </template>
              </g>
            </svg>
          </div>
        </div>
        <p class="inv-muted mt-2 mb-0">نوار باریک خاکستری تاریخ‌های برنامه (آخرین محاسبه برنامه اولیه) است؛ API تاریخ هر فعالیت در خط مبنا را جداگانه نمی‌دهد، فقط پایان کل پروژه در خط مبنا (خط سبز) را می‌دهد. با هر بار محاسبه مجدد برنامه، نوار خاکستری به‌روز می‌شود.</p>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style scoped>
.gantt-wrap { display: flex; border: 1px solid #dee2e6; border-radius: 6px; overflow: hidden; background: #fff; }
.gantt-list { flex: 0 0 280px; border-left: 1px solid #dee2e6; background: #fff; }
.gantt-name { padding: 0 8px; line-height: 28px; font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; border-bottom: 1px solid #f1f3f5; text-align: right; }
.gantt-name.crit { color: #b02a37; font-weight: 600; }
.gantt-chart { overflow-x: auto; flex: 1 1 auto; }
.gl { display: inline-block; width: 22px; height: 10px; border-radius: 2px; vertical-align: middle; margin-left: 4px; }
.gl.thin { height: 5px; }
.gl.line { height: 0; border-top: 2px dashed; background: transparent !important; }
@media print {
  .gantt-chart { overflow: visible; }
  .gantt-wrap { border: none; }
}
</style>
