<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import { itemLabel, loadWorkCenters, searchItems } from "@/components/production/master/masterKit";
import { groupDigits, numOrNull } from "@/components/sales/pipeline/pipelineKit";
import { estimateAction, getSlEstimate, getSlEstimates, getSlProposals, saveSlEstimate } from "@/api/SalesAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { badgeOfEstimate, costKinds, costSources, estimateStatuses, persianOf, rialText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";

// One estimate (SlEstimate): lines by cost kind, guarantee / financing carrying cost, overhead / contingency / margin percentages (all inputs, empty until entered), risks,
// the totals exactly as the API computed them on the last save, revisions and the below-cost warning from the proposals of the inquiry.
const route = useRoute();
const router = useRouter();
const id = computed(() => Number(route.params.id));
const d = ref<any>(null);
const lines = ref<any[]>([]);
const risks = ref<any[]>([]);
const f = ref<any>({});
const revisions = ref<any[]>([]);
const proposals = ref<any[]>([]);
const workCenters = ref<any[]>([]);
const busy = ref(false);
const dirty = ref(false);
const canWrite = hasRole(SalesRoles.writers);
const editable = computed(() => canWrite && d.value?.status === 1);
const belowCost = computed(() => proposals.value.filter((p) => p.belowCost && ![6, 7].includes(p.status)));
const kindOrder = [1, 2, 3, 4, 5];

const txt = (v: any) => (v === null || v === undefined ? "" : String(v));
function fill(x: any) {
  d.value = x;
  f.value = {
    note: x.note || "", overheadPercent: txt(x.overheadPercent), contingencyPercent: txt(x.contingencyPercent), targetMarginPercent: txt(x.targetMarginPercent),
    guaranteeAmount: x.guaranteeAmount !== null && x.guaranteeAmount !== undefined ? groupDigits(x.guaranteeAmount) : "", guaranteeMonths: txt(x.guaranteeMonths), guaranteeRatePercentPerMonth: txt(x.guaranteeRatePercentPerMonth),
    financingAmount: x.financingAmount !== null && x.financingAmount !== undefined ? groupDigits(x.financingAmount) : "", financingMonths: txt(x.financingMonths), financingRatePercentPerMonth: txt(x.financingRatePercentPerMonth),
  };
  // edited: the user typed a unit cost on this line; an unedited line keeps its system source (average cost, last purchase, work-centre rate) because it is sent back as null
  lines.value = (x.lines || []).map((l: any) => ({ ...l, edited: false, unitCost: l.unitCost === null || l.unitCost === undefined ? "" : groupDigits(l.unitCost), quantity: txt(l.quantity) }));
  risks.value = (x.risks || []).map((r: any) => ({ ...r, impactAmount: groupDigits(r.impactAmount), probabilityPercent: txt(r.probabilityPercent) }));
  dirty.value = false;
}
async function load() {
  const [x, error] = await getSlEstimate(id.value);
  if (error) return fail(error);
  fill(x);
  const [r] = await getSlEstimates({ InquiryId: x.inquiryId, PageSize: 50 });
  revisions.value = rowsOf(camelize(r));
  const [p] = await getSlProposals({ InquiryId: x.inquiryId, PageSize: 50 });
  proposals.value = rowsOf(camelize(p)).filter((q: any) => q.estimateId === id.value);
  if (!workCenters.value.length) workCenters.value = await loadWorkCenters();
}
watch(id, load);

function addLine(kind: number) {
  lines.value.push({ id: null, kind, description: "", equipmentId: null, equipmentBomId: null, itemId: null, workCenterId: null, unit: kind === 3 ? "ساعت" : kind === 2 ? "ساعت" : "", quantity: "1", unitCost: "", costSource: 0, edited: true, amount: null, note: "" });
  dirty.value = true;
}
function removeLine(l: any) {
  lines.value.splice(lines.value.indexOf(l), 1);
  dirty.value = true;
}
function pickItem(l: any, item: any) {
  l.itemId = item ? item.id : null;
  if (item) {
    l.itemCode = item.code || "";
    l.itemName = item.title || "";
    if (!l.description) l.description = item.title || "";
    if (!l.unit) l.unit = item.unit || item.unitTitle || "";
  }
  dirty.value = true;
}
const linesOf = (kind: number) => lines.value.filter((l) => l.kind === kind);
function addRisk() {
  risks.value.push({ id: null, title: "", probabilityPercent: "", impactAmount: "", expectedCost: null });
  dirty.value = true;
}

function payload() {
  const v = f.value;
  return {
    id: d.value.id, inquiryId: d.value.inquiryId, note: String(v.note || "").trim() || null,
    overheadPercent: numOrNull(v.overheadPercent), contingencyPercent: numOrNull(v.contingencyPercent), targetMarginPercent: numOrNull(v.targetMarginPercent),
    guaranteeAmount: numOrNull(v.guaranteeAmount), guaranteeMonths: numOrNull(v.guaranteeMonths), guaranteeRatePercentPerMonth: numOrNull(v.guaranteeRatePercentPerMonth),
    financingAmount: numOrNull(v.financingAmount), financingMonths: numOrNull(v.financingMonths), financingRatePercentPerMonth: numOrNull(v.financingRatePercentPerMonth),
    lines: lines.value.map((l) => ({ kind: l.kind, description: String(l.description || "").trim(), equipmentId: l.equipmentId, equipmentBomId: l.equipmentBomId, itemId: l.itemId, workCenterId: l.workCenterId, unit: String(l.unit || "").trim(), quantity: numOrNull(l.quantity) ?? 0, unitCost: l.id && !l.edited && l.costSource !== 0 ? null : numOrNull(l.unitCost), note: String(l.note || "").trim() || null })),
    risks: risks.value.map((r) => ({ title: String(r.title || "").trim(), probabilityPercent: numOrNull(r.probabilityPercent) ?? 0, impactAmount: numOrNull(r.impactAmount) ?? 0 })),
    rowVersion: d.value.rowVersion,
  };
}
async function save() {
  busy.value = true;
  const [x, error] = await saveSlEstimate(payload());
  busy.value = false;
  if (error) return fail(error);
  ok("برآورد ذخیره و دوباره محاسبه شد.");
  fill(x);
  return true;
}
async function act(action: "finalize" | "revise") {
  if (dirty.value && action === "finalize" && !(await save())) return;
  busy.value = true;
  const [x, error] = await estimateAction({ id: d.value.id, action, rowVersion: d.value.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  if (action === "revise") {
    ok("بازنگری جدید ساخته شد.");
    return router.push({ name: "SlEstimate", params: { id: x.id } });
  }
  ok("برآورد نهایی شد.");
  fill(x);
  load();
}
const totals = computed(() => {
  const x = d.value || {};
  return [
    ["مواد و قطعات", x.materialCost], ["دستمزد ساخت", x.labourCost], ["مهندسی", x.engineeringCost], ["پیمانکار فرعی", x.subcontractCost], ["بسته‌بندی و حمل", x.packingTransportCost],
    ["جمع هزینه مستقیم", x.directCost, true], ["هزینه ضمانت", x.guaranteeCost], ["هزینه تامین مالی", x.financingCost], ["هزینه ریسک", x.riskCost], ["هزینه پیش‌بینی‌نشده", x.contingencyCost],
    ["جمع پیش از سربار", x.costBeforeOverhead, true], ["سربار", x.overheadCost], ["بهای تمام‌شده", x.totalCost, true],
  ] as [string, number, boolean?][];
});
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="mb-2"><router-link :to="{ name: 'SlEstimates' }" class="btn btn-sm btn-outline-secondary">بازگشت به فهرست</router-link></div>
      <template v-if="d">
        <div class="inv-toolbar">
          <div>
            <h1 class="inv-title"><span class="inv-ltr">{{ d.number }}</span> - بازنگری {{ d.revision }}</h1>
            <span :class="badgeOfEstimate(d.status)" class="inv-badge">{{ estimateStatuses[d.status] }}</span>
            <router-link class="ms-2 inv-ltr" :to="{ name: 'SlInquiry', params: { id: d.inquiryId } }">{{ d.inquiryNumber }}</router-link>
            <span v-if="d.finalizedAtText" class="small text-muted ms-2">نهایی‌شده: {{ persianOf(d.finalizedAtText, null) }}</span>
          </div>
          <div v-if="canWrite" class="d-flex gap-2">
            <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره و محاسبه</button>
            <button v-if="editable" :disabled="busy" class="btn btn-sm btn-success" @click="act('finalize')">نهایی کردن</button>
            <button v-if="d.status === 2" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="act('revise')">بازنگری جدید</button>
          </div>
        </div>

        <div v-for="p in belowCost" :key="p.id" class="alert alert-danger py-2">
          قیمت پیشنهاد {{ p.number }} ({{ rialText(p.price) }} ریال) کمتر از بهای تمام‌شده این برآورد است؛ تایید آن فقط با مدیرعامل و با ثبت توضیح ممکن است.
        </div>
        <div v-if="d.missingInputs" class="alert alert-warning py-2">قیمت هنوز محاسبه نشده است: {{ d.missingInputs }}</div>
        <div v-if="dirty" class="alert alert-info py-2">تغییرات ذخیره نشده است؛ جمع‌ها پس از ذخیره دوباره محاسبه می‌شوند.</div>

        <div v-if="d.equipments && d.equipments.length" class="mb-3">
          <h2 class="inv-title mb-1">قالب‌های تجهیزات</h2>
          <span v-for="e in d.equipments" :key="e.equipmentId" class="inv-badge info me-2">{{ e.equipmentName || e.equipmentId }} × {{ e.count }}</span>
        </div>

        <div v-for="kind in kindOrder" :key="kind" class="mb-3">
          <div class="d-flex align-items-center justify-content-between mb-1">
            <h2 class="inv-title mb-0">{{ costKinds[kind] }}<span v-if="kind === 3" class="small text-muted"> (ساعت × نرخ ساعتی)</span><span v-if="kind === 2" class="small text-muted"> (استانداردهای مسیر ساخت × نرخ مرکز کار)</span></h2>
            <button v-if="editable" class="btn btn-sm btn-outline-secondary" @click="addLine(kind)">افزودن ردیف</button>
          </div>
          <div class="table-responsive">
            <table class="table table-sm">
              <thead><tr><th style="min-width: 220px">شرح</th><th v-if="kind === 1" style="min-width: 180px">کالا</th><th v-if="kind === 2" style="min-width: 160px">مرکز کار</th><th>واحد</th><th>{{ kind === 3 || kind === 2 ? "ساعت" : "مقدار" }}</th><th>{{ kind === 3 || kind === 2 ? "نرخ ساعتی (ریال)" : "هزینه واحد (ریال)" }}</th><th>منبع هزینه</th><th>مبلغ (ریال)</th><th></th></tr></thead>
              <tbody>
                <tr v-if="!linesOf(kind).length"><td :colspan="9" class="text-center inv-muted">ردیفی نیست.</td></tr>
                <tr v-for="(l, i) in linesOf(kind)" :key="l.id ?? 'n' + i">
                  <td><input v-model="l.description" :disabled="!editable" class="form-control form-control-sm" @input="dirty = true" /></td>
                  <td v-if="kind === 1"><LookupBox :model-value="null" :fetch="searchItems" :label="itemLabel" :disabled="!editable" :fallback-text="l.itemId ? (l.itemCode ? l.itemCode + ' – ' + (l.itemName || '') : l.itemName || '#' + l.itemId) : ''" placeholder="کالا" @pick="(it: any) => pickItem(l, it)" /></td>
                  <td v-if="kind === 2"><select v-model="l.workCenterId" :disabled="!editable" class="form-select form-select-sm" @change="dirty = true"><option :value="null">-</option><option v-if="l.workCenterId && !workCenters.find((w) => w.id === l.workCenterId)" :value="l.workCenterId">{{ l.workCenterName || "#" + l.workCenterId }}</option><option v-for="w in workCenters" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option></select></td>
                  <td><input v-model="l.unit" :disabled="!editable" class="form-control form-control-sm" style="width: 80px" @input="dirty = true" /></td>
                  <td><input v-model="l.quantity" :disabled="!editable" class="form-control form-control-sm inv-ltr" style="width: 90px" inputmode="decimal" @input="dirty = true" /></td>
                  <td><input v-model="l.unitCost" :disabled="!editable" class="form-control form-control-sm inv-ltr" style="width: 140px" inputmode="numeric" placeholder="خالی = از منابع" @input="dirty = true; l.edited = true" @blur="l.unitCost = groupDigits(l.unitCost)" /></td>
                  <td><span v-if="l.id && !l.edited" :class="l.unitCost === '' ? 'warn' : 'info'" class="inv-badge">{{ l.unitCost === "" ? "نامشخص" : costSources[l.costSource] }}</span><span v-else-if="l.id" class="inv-badge">{{ l.unitCost === "" ? "از منابع" : "دستی" }}</span><span v-else class="inv-badge">{{ l.unitCost === "" ? "از منابع" : "دستی" }}</span></td>
                  <td class="inv-ltr">{{ l.id ? rialText(l.amount) : "-" }}</td>
                  <td><button v-if="editable" class="btn btn-sm btn-outline-danger" @click="removeLine(l)">حذف</button></td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="row g-3">
          <div class="col-lg-8">
            <div class="border rounded p-2 mb-3">
              <h2 class="inv-title mb-2">ضمانت و تامین مالی</h2>
              <div class="row g-2">
                <div class="col-md-4"><label class="form-label small mb-0">مبلغ ضمانت (ریال)</label><input v-model="f.guaranteeAmount" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="numeric" @input="dirty = true" @blur="f.guaranteeAmount = groupDigits(f.guaranteeAmount)" /></div>
                <div class="col-md-4"><label class="form-label small mb-0">مدت ضمانت (ماه)</label><input v-model="f.guaranteeMonths" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" @input="dirty = true" /></div>
                <div class="col-md-4"><label class="form-label small mb-0">نرخ ماهانه ضمانت (درصد)</label><input v-model="f.guaranteeRatePercentPerMonth" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" @input="dirty = true" /></div>
                <div class="col-md-4"><label class="form-label small mb-0">مبلغ تامین مالی (ریال)</label><input v-model="f.financingAmount" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="numeric" @input="dirty = true" @blur="f.financingAmount = groupDigits(f.financingAmount)" /></div>
                <div class="col-md-4"><label class="form-label small mb-0">مدت تامین مالی (ماه)</label><input v-model="f.financingMonths" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" @input="dirty = true" /></div>
                <div class="col-md-4"><label class="form-label small mb-0">نرخ ماهانه تامین مالی (درصد)</label><input v-model="f.financingRatePercentPerMonth" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" @input="dirty = true" /></div>
                <div class="col-md-4"><label class="form-label small mb-0">سربار (درصد)</label><input v-model="f.overheadPercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" @input="dirty = true" /></div>
                <div class="col-md-4"><label class="form-label small mb-0">پیش‌بینی‌نشده (درصد)</label><input v-model="f.contingencyPercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" @input="dirty = true" /></div>
                <div class="col-md-4"><label class="form-label small mb-0">حاشیه سود هدف (درصد از قیمت)</label><input v-model="f.targetMarginPercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" @input="dirty = true" /></div>
                <div class="col-12"><label class="form-label small mb-0">یادداشت</label><textarea v-model="f.note" :disabled="!editable" class="form-control form-control-sm" rows="2" @input="dirty = true" /></div>
              </div>
            </div>

            <div class="border rounded p-2">
              <div class="d-flex align-items-center justify-content-between mb-1">
                <h2 class="inv-title mb-0">ریسک‌ها</h2>
                <button v-if="editable" class="btn btn-sm btn-outline-secondary" @click="addRisk">افزودن ریسک</button>
              </div>
              <table class="table table-sm mb-0">
                <thead><tr><th>عنوان</th><th>احتمال (درصد)</th><th>اثر (ریال)</th><th>هزینه مورد انتظار (ریال)</th><th></th></tr></thead>
                <tbody>
                  <tr v-if="!risks.length"><td colspan="5" class="text-center inv-muted">ریسکی ثبت نشده است.</td></tr>
                  <tr v-for="(r, i) in risks" :key="r.id ?? 'n' + i">
                    <td><input v-model="r.title" :disabled="!editable" class="form-control form-control-sm" @input="dirty = true" /></td>
                    <td><input v-model="r.probabilityPercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" style="width: 100px" inputmode="decimal" @input="dirty = true" /></td>
                    <td><input v-model="r.impactAmount" :disabled="!editable" class="form-control form-control-sm inv-ltr" style="width: 160px" inputmode="numeric" @input="dirty = true" @blur="r.impactAmount = groupDigits(r.impactAmount)" /></td>
                    <td class="inv-ltr">{{ r.id ? rialText(r.expectedCost) : "-" }}</td>
                    <td><button v-if="editable" class="btn btn-sm btn-outline-danger" @click="risks.splice(i, 1); dirty = true">حذف</button></td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <div class="col-lg-4">
            <div class="border rounded p-2 mb-3">
              <h2 class="inv-title mb-2">جمع‌ها (ریال)</h2>
              <table class="table table-sm mb-0">
                <tbody>
                  <tr v-for="[title, value, strong] in totals" :key="title" :class="{ 'fw-bold': strong }"><td>{{ title }}</td><td class="inv-ltr text-end">{{ rialText(value) }}</td></tr>
                  <tr class="fw-bold table-light"><td>قیمت فروش</td><td class="inv-ltr text-end">{{ d.price === null || d.price === undefined ? "محاسبه نشده" : rialText(d.price) }}</td></tr>
                </tbody>
              </table>
              <div v-if="d.missingCostLines" class="small text-warning mt-1">{{ d.missingCostLines }} ردیف بدون هزینه واحد است.</div>
            </div>
            <div class="border rounded p-2">
              <h2 class="inv-title mb-2">بازنگری‌ها</h2>
              <table class="table table-sm mb-0">
                <tbody>
                  <tr v-for="r in revisions" :key="r.id" :class="{ 'table-active': r.id === d.id }">
                    <td class="inv-ltr"><router-link :to="{ name: 'SlEstimate', params: { id: r.id } }">{{ r.number }}</router-link></td>
                    <td>{{ rialText(r.price) }}</td>
                    <td><span :class="badgeOfEstimate(r.status)" class="inv-badge">{{ estimateStatuses[r.status] }}</span></td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </template>
      <p v-else class="inv-muted">در حال بارگذاری...</p>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
