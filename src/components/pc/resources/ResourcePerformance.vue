<script setup lang="ts">
// «بودجه و عملکرد منابع» (priority 4d-3): for one project, the baseline, the planned value to the data date (phased over the Persian months), the current plan, the aggregated actuals and the variance, per resource and per class.
// Actuals are aggregated: machine hours at the linked work centre x the approved rate, the orders sent to the subcontractors, the stock cost of the project's issues; labour is only a class total of hours (never a person, never an amount).
// A figure that could show one person's pay is «محرمانه». The list «چه چیز به داده گره خورده» says honestly what the data model does not tie.
// Below, the cost-baseline lines that the planning agent proposed from the approved plan: a person accepts (through the cost baseline's own path) or rejects with a reason; the agent never applies them.
import { computed, onMounted, ref, watch } from "vue";
import JDate from "@/components/pc/JDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import ResourcePerformancePrint from "@/components/pc/print/ResourcePerformancePrint.vue";
import { decideCostProposal, getCostProposals, getResourcePerformance } from "@/api/ResourceAPIs";
import type { CostProposal, PerfRow, ResourcePerformance } from "@/api/ResourceAPIs";
import { faNum } from "@/composables/ceo";
import { fail, ok } from "@/composables/inventory";
import { resourceClasses, usePcPrint } from "@/composables/projectControl";
import { CONFIDENTIAL, faDateTime, money, NO_APPROVED_RATE, qty } from "@/composables/resources";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

const props = defineProps({ contractId: { type: Number, required: true } });
const data = ref<ResourcePerformance | null>(null);
const proposals = ref<CostProposal[]>([]);
const asOf = ref<string | null>(null);
const loading = ref(false);
const busy = ref(false);
const rejecting = ref<CostProposal | null>(null);
const canDecide = hasRole(ProjectControlRoles.resourceWriters);
const { job, run } = usePcPrint();
const proposalStatuses: Record<number, string> = { 1: "پیشنهادشده", 2: "پذیرفته‌شده", 3: "ردشده", 4: "منسوخ" };

// requests are numbered: a slow answer never overwrites a newer one
let sequence = 0;
async function load() {
  const mine = ++sequence;
  loading.value = true;
  const [[perf, perfError], [list, listError]] = await Promise.all([getResourcePerformance(props.contractId, asOf.value), getCostProposals(props.contractId)]);
  if (mine !== sequence) return;
  loading.value = false;
  if (perfError) { data.value = null; fail(perfError); } else data.value = perf;
  if (!listError) proposals.value = list;
}

const classTitle = (c: number) => resourceClasses[c] || "";
/** the cell of an amount: «محرمانه» when held back, «بدون نرخ مصوب» when there is no rate, a dash when the figure does not exist */
const amountCell = (v: number | null, row: PerfRow) => (v !== null ? money(v) : row.rateHidden ? CONFIDENTIAL : row.noApprovedRate ? NO_APPROVED_RATE : "—");
const qtyCell = (v: number | null) => (v === null ? "—" : qty(v));
const varianceClass = (v: number | null) => (v === null || v === 0 ? "" : v > 0 ? "text-danger" : "text-success");

/** the periods as a table: one row per Persian month, one column per class */
const classes = computed(() => [...new Set((data.value?.periods || []).map((p) => p.resourceClass))].sort());
const months = computed(() => {
  const map = new Map<string, { key: string; text: string; cells: Record<number, number | null> }>();
  for (const p of data.value?.periods || []) {
    const m = map.get(p.key) ?? { key: p.key, text: p.text, cells: {} };
    m.cells[p.resourceClass] = p.amount;
    map.set(p.key, m);
  }
  return [...map.values()].sort((a, b) => a.key.localeCompare(b.key));
});

const open = computed(() => proposals.value.filter((p) => p.status === 1));
const past = computed(() => proposals.value.filter((p) => p.status !== 1 && p.status !== 4));
async function accept(p: CostProposal) {
  busy.value = true;
  const [, error] = await decideCostProposal(p.id, "accept", p.rowVersion);
  busy.value = false;
  if (error) return fail(error);
  ok("ردیف به خط مبنای هزینه افزوده شد.");
  load();
}
async function reject(values: Record<string, any>) {
  const p = rejecting.value;
  rejecting.value = null;
  if (!p) return;
  busy.value = true;
  const [, error] = await decideCostProposal(p.id, "reject", p.rowVersion, String(values.reason || ""));
  busy.value = false;
  if (error) return fail(error);
  ok("پیشنهاد رد شد.");
  load();
}

onMounted(load);
watch(() => props.contractId, load);
watch(asOf, load);
</script>

<template>
  <div>
    <div class="d-flex flex-wrap gap-2 align-items-end mb-2 d-print-none">
      <div style="width: 170px"><label class="form-label small mb-0">تاریخ داده</label><JDate v-model="asOf" /></div>
      <button v-if="asOf" class="btn btn-sm btn-outline-secondary" @click="asOf = null">امروز</button>
      <button class="btn btn-sm btn-outline-secondary ms-auto" :disabled="!data" @click="run('performance', data)">چاپ</button>
    </div>
    <div v-if="loading && !data" class="inv-muted">در حال خواندن...</div>

    <template v-if="data">
      <p v-if="!data.hasBaseline" class="inv-muted">برای این پروژه هنوز خط مبنای تاییدشده‌ای نیست؛ ستون خط مبنا و ارزش برنامه‌ای خالی است و فقط طرح جاری و واقعی دیده می‌شود.</p>
      <div class="row g-2 mb-3">
        <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="inv-muted small">خط مبنا{{ data.baselineNumber !== null ? " (شماره " + faNum(data.baselineNumber, 0) + ")" : "" }}</div><b>{{ money(data.baselineTotal) }}</b></div></div>
        <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="inv-muted small">ارزش برنامه‌ای تا تاریخ داده</div><b>{{ money(data.plannedToDateTotal) }}</b></div></div>
        <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="inv-muted small">طرح جاری{{ data.planVersion !== null ? " (نسخه " + faNum(data.planVersion, 0) + ")" : "" }}</div><b>{{ money(data.planTotal) }}</b></div></div>
        <div class="col-6 col-md-3"><div class="border rounded p-2"><div class="inv-muted small">هزینه واقعی (آنچه به داده گره خورده)</div><b>{{ money(data.actualTotal) }}</b></div></div>
      </div>
      <p v-if="data.rateHidden || data.hiddenLineCount" class="small inv-muted">ردیف‌هایی که هزینه یک نفر یا گروه کوچک را نشان می‌دهد برای نقش شما {{ CONFIDENTIAL }} است و در جمع‌ها نیست ({{ faNum(data.hiddenLineCount, 0) }} ردیف). ردیف بدون نرخ مصوب: {{ faNum(data.noRateCount, 0) }}.</p>

      <h2 class="h6">به تفکیک گروه منبع</h2>
      <div class="table-responsive">
        <table class="table table-sm align-middle">
          <thead><tr><th>گروه</th><th>خط مبنا</th><th>برنامه تا تاریخ</th><th>طرح جاری</th><th>واقعی (مقدار)</th><th>واقعی (ریال)</th><th>انحراف (ریال)</th></tr></thead>
          <tbody>
            <tr v-if="!data.byClass.length"><td colspan="7" class="text-center inv-muted">—</td></tr>
            <tr v-for="c in data.byClass" :key="c.resourceClass">
              <td>{{ classTitle(c.resourceClass) }}</td>
              <td>{{ amountCell(c.baselineAmount, c) }}</td><td>{{ amountCell(c.plannedToDateAmount, c) }}</td><td>{{ amountCell(c.planAmount, c) }}</td>
              <td>{{ qtyCell(c.actualQuantity) }}<div v-if="c.actualNote" class="small inv-muted">{{ c.actualNote }}</div></td>
              <td>{{ amountCell(c.actualAmount, c) }}<div v-if="c.actualBasis" class="small inv-muted">{{ c.actualBasis }}</div></td>
              <td :class="varianceClass(c.varianceAmount)">{{ amountCell(c.varianceAmount, c) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2 class="h6">به تفکیک منبع</h2>
      <div class="table-responsive">
        <table class="table table-sm align-middle">
          <thead><tr><th>منبع</th><th>خط مبنا (مقدار)</th><th>برنامه تا تاریخ (مقدار)</th><th>طرح جاری (مقدار)</th><th>واقعی (مقدار)</th><th>انحراف (مقدار)</th><th>خط مبنا (ریال)</th><th>برنامه تا تاریخ (ریال)</th><th>طرح جاری (ریال)</th><th>واقعی (ریال)</th></tr></thead>
          <tbody>
            <tr v-if="!data.rows.length"><td colspan="10" class="text-center inv-muted">—</td></tr>
            <tr v-for="r in data.rows" :key="r.resourceId || r.resourceCode || ''">
              <td><span class="inv-ltr">{{ r.resourceCode }}</span> {{ r.resourceTitle }}<div v-if="r.note" class="small inv-muted">{{ r.note }}</div></td>
              <td>{{ qty(r.baselineQuantity) }}</td><td>{{ qty(r.plannedToDateQuantity) }}</td><td>{{ qty(r.planQuantity) }}</td><td>{{ qtyCell(r.actualQuantity) }}</td>
              <td :class="varianceClass(r.varianceQuantity)">{{ qtyCell(r.varianceQuantity) }}</td>
              <td>{{ amountCell(r.baselineAmount, r) }}</td><td>{{ amountCell(r.plannedToDateAmount, r) }}</td><td>{{ amountCell(r.planAmount, r) }}</td><td>{{ amountCell(r.actualAmount, r) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2 class="h6">ارزش برنامه‌ای به تفکیک ماه (شمسی)</h2>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>ماه</th><th v-for="c in classes" :key="c">{{ classTitle(c) }}</th></tr></thead>
          <tbody>
            <tr v-if="!months.length"><td :colspan="classes.length + 1" class="text-center inv-muted">—</td></tr>
            <tr v-for="m in months" :key="m.key">
              <td>{{ m.text }}</td>
              <td v-for="c in classes" :key="c">{{ m.cells[c] === undefined ? "—" : m.cells[c] === null ? CONFIDENTIAL : money(m.cells[c]) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <details class="mb-3">
        <summary class="small">چه چیز به داده گره خورده و چه چیز نه</summary>
        <ul class="small mb-0 mt-1">
          <li v-for="(t, i) in data.ties" :key="i"><b>{{ t.source }}</b>: <span :class="t.tied ? 'text-success' : 'text-danger'">{{ t.tied ? "گره خورده" : "گره نخورده" }}</span> — {{ t.note }}</li>
        </ul>
      </details>
    </template>

    <h2 class="h6 mt-3">پیشنهاد ردیف‌های خط مبنای هزینه (ایجنت برنامه‌ریزی)</h2>
    <p class="small inv-muted">ایجنت از طرح منابع تاییدشده برای هر گروه یک پیشنهاد می‌سازد؛ تا شما نپذیرید در خط مبنای هزینه چیزی نوشته نمی‌شود. ردیف‌های بدون نرخ مصوب و ردیف‌های {{ CONFIDENTIAL }} در مبلغ نمی‌آیند و جدا شمرده می‌شوند.</p>
    <div class="table-responsive">
      <table class="table table-sm align-middle">
        <thead><tr><th>گروه</th><th>نسخه طرح</th><th>مبلغ (ریال)</th><th>ردیف</th><th>خارج از مبلغ</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!open.length && !past.length"><td colspan="7" class="text-center inv-muted">پیشنهادی نیست.</td></tr>
          <tr v-for="p in open" :key="p.id">
            <td>{{ classTitle(p.resourceClass) }}</td><td>{{ faNum(p.planVersion, 0) }}</td><td>{{ p.amount === null ? CONFIDENTIAL : money(p.amount) }}<div v-if="p.existingAmount > 0" class="small inv-muted">اختلاف نسبت به {{ money(p.existingAmount) }} که از پیش در خط مبنا آمده است</div></td><td>{{ faNum(p.lineCount, 0) }}</td>
            <td class="small">{{ faNum(p.excludedNoRate, 0) }} بدون نرخ، {{ faNum(p.excludedSensitive, 0) }} {{ CONFIDENTIAL }}</td>
            <td><span class="inv-badge warn">{{ proposalStatuses[p.status] }}</span><span v-if="p.preparedByAgent" class="small inv-muted"> تهیه‌شده توسط ایجنت</span></td>
            <td class="d-print-none text-nowrap">
              <template v-if="canDecide">
                <button :disabled="busy" class="btn btn-sm btn-success" @click="accept(p)">پذیرش و افزودن به خط مبنای هزینه</button>
                <button :disabled="busy" class="btn btn-sm btn-outline-danger ms-1" @click="rejecting = p">رد با علت</button>
              </template>
              <span v-else class="small inv-muted">تصمیم با برنامه‌ریز یا PMO است.</span>
            </td>
          </tr>
          <tr v-for="p in past" :key="p.id" class="inv-muted">
            <td>{{ classTitle(p.resourceClass) }}</td><td>{{ faNum(p.planVersion, 0) }}</td><td>{{ p.amount === null ? CONFIDENTIAL : money(p.amount) }}</td><td>{{ faNum(p.lineCount, 0) }}</td><td></td>
            <td>{{ proposalStatuses[p.status] }}<span v-if="p.rejectReason"> — {{ p.rejectReason }}</span></td><td class="d-print-none">{{ faDateTime(p.createDate) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <PromptModal v-if="rejecting" title="رد پیشنهاد هزینه" :fields="[{ key: 'reason', label: 'علت رد', type: 'textarea', required: true, maxlength: 1000 }]" confirm-text="رد" @confirm="reject" @cancel="rejecting = null" />
    <ResourcePerformancePrint v-if="job?.kind === 'performance'" :data="job.data" :contract-id="contractId" />
  </div>
</template>
