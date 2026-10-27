<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { baselineAction, createBaseline, deleteCostLine, getActivities, getBaseline, getBaselines, getCostLines, getTerms, getWbs, saveCostLine } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import BaselinePrint from "@/components/pc/print/BaselinePrint.vue";
import { fail, jalali, ok, printPage, rial } from "@/composables/inventory";
import { baselineKinds, baselineStatuses, costClasses, pcContractId, resourceClasses, setPcContract, sumOf, unitTitle, usePcPrint } from "@/composables/projectControl";
import { currentRole, hasRole, ProjectControlRoles } from "@/composables/roles";

// Baselines (WI-PL-009 17): created from the calculated plan by planning, signed by the manager of every unit that owns activities,
// approved by the PMO deputy. Cost baseline lines (CBS) are kept per project and are compared with the BAC of the baseline.
const { job, run } = usePcPrint();
const tab = ref("baselines");
const tabs = [
  { key: "baselines", title: "خط مبنای زمانی" },
  { key: "cost", title: "خط مبنای هزینه (CBS)" },
];
const rows = ref<any[]>([]);
const sel = ref<any>(null);
const reason = ref("");
const note = ref("");
const busy = ref(false);
const terms = ref<any>(null);
const canCreate = hasRole(ProjectControlRoles.planning);
const role = currentRole();

async function load() {
  if (!pcContractId.value) return;
  const [[data, error], [t]] = await Promise.all([getBaselines({ ContractId: pcContractId.value, PageSize: 100 }), getTerms(pcContractId.value)]);
  if (error) return fail(error);
  terms.value = t;
  rows.value = rowsOf(data).sort((a, b) => b.number - a.number);
  if (sel.value) sel.value = rows.value.find((x) => x.id === sel.value.id) || null;
}

async function create() {
  busy.value = true;
  const [data, error] = await createBaseline(pcContractId.value!, reason.value);
  busy.value = false;
  if (error) return fail(error);
  ok("خط مبنا ساخته شد؛ مدیران واحدها باید امضا کنند.");
  reason.value = "";
  sel.value = data;
  load();
}
async function act(action: "sign" | "approve") {
  busy.value = true;
  const [data, error] = await baselineAction(sel.value.id, action, note.value);
  busy.value = false;
  if (error) return fail(error);
  ok(action === "sign" ? "امضا شد." : "خط مبنا تایید شد.");
  note.value = "";
  sel.value = data;
  load();
}
const mySignatures = computed(() => (sel.value?.signatures || []).filter((s: any) => !s.signedAt && s.unitRole === role));
const signedCount = computed(() => (sel.value?.signatures || []).filter((s: any) => s.signedAt).length);

// ---- cost lines
const lines = ref<any[]>([]);
const nodes = ref<any[]>([]);
const acts = ref<any[]>([]);
const line = ref<any>(null);
async function loadCost() {
  if (!pcContractId.value) return;
  const [[l, e1], [n], [a]] = await Promise.all([getCostLines({ ContractId: pcContractId.value }), getWbs({ ContractId: pcContractId.value }), getActivities({ ContractId: pcContractId.value })]);
  if (e1) return fail(e1);
  lines.value = rowsOf(l);
  nodes.value = rowsOf(n);
  acts.value = rowsOf(a);
}
const byClass = computed(() => Object.keys(costClasses).map((k) => ({ k: Number(k), sum: sumOf(lines.value.filter((l) => l.costClass === Number(k)), "amount") })));
const totalCost = computed(() => sumOf(lines.value, "amount"));
const current = computed(() => rows.value.find((x) => x.isCurrent));
const blankLine = () => ({ id: null, contractId: pcContractId.value, wbsNodeId: null, activityId: null, costClass: 1, resourceClass: null, amount: 0, description: "" });
async function saveLine() {
  busy.value = true;
  const [, error] = await saveCostLine({ ...line.value, wbsNodeId: line.value.wbsNodeId || null, activityId: line.value.activityId || null, resourceClass: line.value.resourceClass || null });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  line.value = null;
  loadCost();
}
async function removeLine(l: any) {
  if (!confirm("ردیف حذف شود؟")) return;
  const [, error] = await deleteCostLine(l.id);
  if (error) return fail(error);
  loadCost();
}
const nodeText = (id: number | null) => nodes.value.find((n) => n.id === id)?.code || "";
const actText = (id: number | null) => acts.value.find((n) => n.id === id)?.code || "";

// the link /project-control/baselines?id=N (an inbox item) opens that baseline; when another project is chosen, the project of the baseline becomes the chosen one
const route = useRoute();
async function openFromLink() {
  const id = Number(route.query.id);
  if (!Number.isInteger(id) || id <= 0) return;
  const [b, e] = await getBaseline(id);
  if (e) return fail(e);
  tab.value = "baselines";
  if (b.contractId && b.contractId !== pcContractId.value) {
    setPcContract(b.contractId);
    await nextTick(); // the watcher below clears the selection when the project changes
  }
  sel.value = b;
}
onMounted(async () => {
  await openFromLink();
  load();
  loadCost();
});
watch(() => route.query.id, openFromLink);
watch(pcContractId, () => { sel.value = null; load(); loadCost(); });
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="خط مبنای پروژه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">خط مبنای زمان و هزینه</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <ContractSelect />
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
        </div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <PcTabs v-model="tab" :tabs="tabs" />

        <div v-if="tab === 'baselines'">
          <div v-if="canCreate" class="d-flex flex-wrap gap-2 align-items-end mb-3 d-print-none">
            <div style="min-width: 300px"><label class="form-label small mb-0">دلیل / عنوان خط مبنای جدید</label><input v-model="reason" class="form-control form-control-sm" placeholder="خالی: خط مبنای اولیه یا بازنگری" /></div>
            <button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ساخت خط مبنا از برنامه جاری</button>
          </div>
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>نوع</th><th>وضعیت</th><th>شروع</th><th>پایان</th><th>BAC</th><th>ذخیره مدیریتی</th><th>فعالیت</th><th>امضاها</th><th>تایید</th></tr></thead>
            <tbody>
              <tr v-if="!rows.length"><td colspan="10" class="text-center inv-muted">خط مبنایی ثبت نشده است.</td></tr>
              <tr v-for="r in rows" :key="r.id" role="button" :class="{ 'table-active': sel && sel.id === r.id }" @click="sel = r">
                <td>R{{ r.number }} <span v-if="r.isCurrent" class="inv-badge ok">جاری</span></td><td>{{ baselineKinds[r.kind] }}</td>
                <td><span :class="r.status === 2 ? 'ok' : r.status === 3 ? '' : 'warn'" class="inv-badge">{{ baselineStatuses[r.status] }}</span></td>
                <td>{{ jalali(r.projectStart) }}</td><td>{{ jalali(r.projectFinish) }}</td><td>{{ rial(r.bac) }}</td><td>{{ rial(r.managementReserve) }}</td><td>{{ r.activityCount }}</td>
                <td>{{ r.signatures.filter((s: any) => s.signedAt).length }} از {{ r.signatures.length }}</td><td>{{ jalali(r.approvedAt) }}</td>
              </tr>
            </tbody>
          </table>

          <div v-if="sel" class="border rounded p-3 d-print-none">
            <div class="inv-toolbar">
              <b>خط مبنا R{{ sel.number }} — {{ sel.reason }}</b>
              <div class="d-flex gap-2"><button class="btn btn-sm btn-outline-secondary" @click="run('baseline', sel)">چاپ برگه تایید</button><button class="btn btn-sm btn-outline-secondary" @click="sel = null">بستن</button></div>
            </div>
            <table class="table table-sm">
              <thead><tr><th>امضاکننده (واحد)</th><th>نقش</th><th>امضا‌کننده</th><th>تاریخ امضا</th></tr></thead>
              <tbody>
                <tr v-if="!sel.signatures.length"><td colspan="4" class="inv-muted">واحدی با فعالیت دارای «واحد مسئول» نیست؛ امضایی لازم نیست.</td></tr>
                <tr v-for="s in sel.signatures" :key="s.unitRole"><td>{{ unitTitle(s.unitRole) }}</td><td class="inv-ltr">{{ s.unitRole }}</td><td>{{ s.userName }}</td><td><span v-if="s.signedAt" class="inv-badge ok">{{ jalali(s.signedAt, true) }}</span><span v-else class="inv-badge warn">امضا نشده</span></td></tr>
              </tbody>
            </table>
            <div class="d-flex flex-wrap gap-2 align-items-end">
              <button v-if="sel.status === 0 && mySignatures.length" :disabled="busy" class="btn btn-sm btn-success" @click="act('sign')">امضای برنامه (مدیر {{ unitTitle(role) }})</button>
              <template v-if="sel.status === 1 && role === 'PMO'">
                <input v-model="note" class="form-control form-control-sm" placeholder="یادداشت تایید" style="max-width: 260px" />
                <button :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید خط مبنا (معاون PMO)</button>
              </template>
            </div>
            <p class="inv-muted mt-2 mb-0">امضا شده: {{ signedCount }} از {{ sel.signatures.length }}. پس از امضای همه مدیران، معاون PMO تایید می‌کند؛ تایید، خط مبنای قبلی را جایگزین‌شده می‌کند و گام «تایید خط مبنا» از شروع پروژه ثبت می‌شود. تا پایان امضا و تایید، خط مبنای جدید دیگری ساخته نمی‌شود.</p>
          </div>
        </div>

        <div v-else>
          <div class="row g-2 mb-3">
            <div v-for="c in byClass" :key="c.k" class="col-md-3"><div class="border rounded p-2"><div class="inv-muted">{{ costClasses[c.k] }}</div><b>{{ rial(c.sum) }}</b></div></div>
            <div class="col-md-6"><div class="border rounded p-2"><div class="inv-muted">مجموع خط مبنای هزینه</div><b>{{ rial(totalCost) }}</b></div></div>
            <div class="col-md-6"><div class="border rounded p-2"><div class="inv-muted">BAC خط مبنای زمانی جاری</div><b>{{ current ? rial(current.bac) : "—" }}</b> <span v-if="current && totalCost" :class="Math.abs(totalCost - current.bac) < 1 ? 'ok' : 'warn'" class="inv-badge">اختلاف {{ rial(totalCost - current.bac) }}</span></div></div>
          </div>
          <div v-if="canCreate" class="mb-2 d-print-none"><button class="btn btn-sm btn-primary" @click="line = blankLine()">ردیف هزینه جدید</button></div>
          <div v-if="line" class="border rounded p-3 mb-3 d-print-none">
            <div class="row g-2">
              <div class="col-md-3"><label class="form-label small mb-0">طبقه هزینه</label><select v-model.number="line.costClass" class="form-select form-select-sm"><option v-for="(n, k) in costClasses" :key="k" :value="Number(k)">{{ n }}</option></select></div>
              <div class="col-md-3"><label class="form-label small mb-0">نوع منبع</label><select v-model="line.resourceClass" class="form-select form-select-sm"><option :value="null">-</option><option v-for="(n, k) in resourceClasses" :key="k" :value="Number(k)">{{ n }}</option></select></div>
              <div class="col-md-3"><label class="form-label small mb-0">مبلغ (ریال)</label><input v-model.number="line.amount" class="form-control form-control-sm" type="number" min="0" /></div>
              <div class="col-md-3"><label class="form-label small mb-0">گره WBS</label><select v-model="line.wbsNodeId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="n in nodes" :key="n.id" :value="n.id">{{ n.code }} {{ n.title }}</option></select></div>
              <div class="col-md-4"><label class="form-label small mb-0">فعالیت</label><select v-model="line.activityId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} {{ a.title }}</option></select></div>
              <div class="col-md-8"><label class="form-label small mb-0">شرح</label><input v-model="line.description" class="form-control form-control-sm" /></div>
            </div>
            <div class="mt-2 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveLine">ذخیره</button><button class="btn btn-sm btn-outline-secondary" @click="line = null">انصراف</button></div>
          </div>
          <table class="table table-sm table-hover">
            <thead><tr><th>طبقه</th><th>منبع</th><th>گره WBS</th><th>فعالیت</th><th>شرح</th><th>مبلغ</th><th class="d-print-none"></th></tr></thead>
            <tbody>
              <tr v-if="!lines.length"><td colspan="7" class="text-center inv-muted">ردیفی نیست.</td></tr>
              <tr v-for="l in lines" :key="l.id">
                <td>{{ costClasses[l.costClass] }}</td><td>{{ resourceClasses[l.resourceClass] }}</td><td class="inv-ltr">{{ nodeText(l.wbsNodeId) }}</td><td class="inv-ltr">{{ actText(l.activityId) }}</td><td>{{ l.description }}</td><td>{{ rial(l.amount) }}</td>
                <td class="d-print-none text-nowrap"><template v-if="canCreate"><button class="btn btn-sm btn-outline-secondary" @click="line = { ...l }">ویرایش</button> <button class="btn btn-sm btn-outline-danger" @click="removeLine(l)">حذف</button></template></td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>
    <BaselinePrint v-if="job?.kind === 'baseline'" :b="job.data" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
