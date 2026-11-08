<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import DesignMatrixPrint from "@/components/productBank/print/DesignMatrixPrint.vue";
import DesignReviewPrint from "@/components/productBank/print/DesignReviewPrint.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { designFindingAction, designReviewAction, getDesignMatrix, getDesignPlans, getDesignReview, saveDesignFinding, saveDesignReview, saveDesignTraces, saveDesignValidation } from "@/api/DesignControlAPIs";
import { getBoms, getRoutings } from "@/api/ProductBankAPIs";
import { getDesignInputs } from "@/api/DesignControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { printForm } from "@/composables/quality";
import { findingClass, findingStatuses, inputKinds, outcomeClass, outputKinds, reviewOutcomes, reviewStatusClass, reviewStatuses, reviewTypes, revisionText, traceClass, traceResults, validationMethods } from "@/composables/designControl";

// Package A1: ONE design review (ISO 9001 8.3.4). A verification needs every input covered by a passed criterion judged by an independent participant (not an author of the output); findings are
// done by their owner and closed by another person; the review is closed by someone who made none of the outputs. Only a closed, accepted verification opens the BOM and drawing release gates.
const route = useRoute();
const router = useRouter();
const review = ref<any>(null);
const matrix = ref<any>(null);
const form = ref<any>(null);
const plans = ref<any[]>([]);
const boms = ref<any[]>([]);
const routings = ref<any[]>([]);
const planInputs = ref<any[]>([]);
const busy = ref(false);
const printing = ref<"minutes" | "matrix">("minutes");
const dialog = ref<null | { kind: "review" | "finding"; id: number; action: string; title: string }>(null);
const holdDialog = ref<null | { minutes: string; heldDate: string | null }>(null);
const closeDialog = ref<null | { outcome: number; note: string }>(null);
const finding = ref<null | { description: string; ownerUserId: number | null; dueDate: string | null }>(null);
const validation = ref<any>(null);
const judge = ref<Record<string, { result: number; evidence: string; evidenceRef: string }>>({});
const isNew = computed(() => route.params.id === "new");
const planOf = computed(() => plans.value.find((p) => p.id === form.value?.planId));
const key = (i: number, o: number) => i + ":" + o;
const canJudge = computed(() => !!review.value?.canJudge && review.value.type !== 1 && review.value.status === 1);

function blank() {
  return { id: null, planId: Number(route.query.planId) || null, type: 2, title: "", scheduledDate: null, inputIds: [] as number[], outputs: [] as any[], participants: [{ userId: null, roleText: "" }], previousReviewId: null, rowVersion: null };
}
function toForm(r: any) {
  return {
    id: r.id, planId: r.planId, type: r.type, title: r.title, scheduledDate: r.scheduledDate ? String(r.scheduledDate).substring(0, 10) : null,
    inputIds: r.type === 1 ? r.inputs.map((i: any) => i.inputId) : [],
    outputs: r.outputs.map((o: any) => ({ kind: o.kind, revisionId: o.revisionId, bomHeaderId: o.bomHeaderId, opcRoutingId: o.opcRoutingId, label: o.label })),
    participants: r.participants.map((p: any) => ({ userId: p.userId, roleText: p.roleText })), previousReviewId: r.previousReviewId, rowVersion: r.rowVersion,
  };
}
function setReview(r: any) {
  review.value = r;
  form.value = r.canEdit ? toForm(r) : null;
  validation.value = { method: r.validationMethod || 1, evidence: r.validationEvidence || "", customerAcceptanceRef: r.customerAcceptanceRef || "", customerAcceptanceNotApplicable: !!r.customerAcceptanceNotApplicable, customerAcceptanceNote: r.customerAcceptanceNote || "" };
}
async function loadMatrix() {
  if (!review.value || review.value.type === 1) return (matrix.value = null);
  const [data, error] = await getDesignMatrix(review.value.id);
  if (error) return fail(error);
  matrix.value = data;
  const j: Record<string, { result: number; evidence: string; evidenceRef: string }> = {};
  for (const r of data.rows) for (const o of data.outputs) {
    const c = (r.cells || []).find((x: any) => x.outputId === o.id);
    j[key(r.input.inputId, o.id)] = { result: c?.result ?? 0, evidence: c?.evidence ?? "", evidenceRef: c?.evidenceRef ?? "" };
  }
  judge.value = j;
}
async function load() {
  if (isNew.value) {
    review.value = null;
    form.value = blank();
    return;
  }
  const [data, error] = await getDesignReview(Number(route.params.id));
  if (error) return fail(error);
  setReview(data);
  await loadMatrix();
}
async function pickPlan() {
  const p = planOf.value;
  form.value.outputs = [];
  boms.value = [];
  routings.value = [];
  planInputs.value = [];
  if (!p) return;
  const [b] = await getBoms({ ProductId: p.productId, PageSize: 100 });
  boms.value = rowsOf(b).filter((x: any) => x.productId === p.productId && x.revision === p.productRevision);
  const [r] = await getRoutings({ ProductId: p.productId, PageSize: 100 });
  routings.value = rowsOf(r);
  const [i] = await getDesignInputs({ ProductId: p.productId, ProductRevision: p.productRevision });
  planInputs.value = rowsOf(i).filter((x: any) => x.status === 0);
}
const addOutput = (kind: number) => form.value.outputs.push({ kind, revisionId: null, bomHeaderId: null, opcRoutingId: null, label: "" });
const toggleInput = (id: number) => (form.value.inputIds = form.value.inputIds.includes(id) ? form.value.inputIds.filter((x: number) => x !== id) : [...form.value.inputIds, id]);
async function save() {
  const dto = { ...form.value, outputs: form.value.outputs.map((o: any) => ({ kind: o.kind, revisionId: o.revisionId || null, bomHeaderId: o.bomHeaderId || null, opcRoutingId: o.opcRoutingId || null })), participants: form.value.participants.filter((p: any) => p.userId) };
  busy.value = true;
  const [data, error] = await saveDesignReview(dto);
  busy.value = false;
  if (error) return fail(error);
  ok("بازبینی ذخیره شد.");
  if (isNew.value) return router.replace({ name: "DesignReview", params: { id: data.id } });
  setReview(data);
  await loadMatrix();
}
async function reviewAction(action: string, extra: any = {}) {
  busy.value = true;
  const [data, error] = await designReviewAction({ id: review.value.id, action, ...extra });
  busy.value = false;
  if (error) return fail(error);
  dialog.value = null;
  holdDialog.value = null;
  closeDialog.value = null;
  ok("انجام شد.");
  if (action === "repeat") return router.replace({ name: "DesignReview", params: { id: data.id } });
  setReview(data);
  await loadMatrix();
}
async function dialogConfirm(note: string) {
  const d = dialog.value!;
  if (d.kind === "review") return reviewAction(d.action, { note });
  busy.value = true;
  const [data, error] = await designFindingAction({ id: d.id, action: d.action, note });
  busy.value = false;
  if (error) return fail(error);
  dialog.value = null;
  setReview(data);
  await loadMatrix();
}
async function addFinding() {
  busy.value = true;
  const [data, error] = await saveDesignFinding({ reviewId: review.value.id, ...finding.value });
  busy.value = false;
  if (error) return fail(error);
  finding.value = null;
  ok("یافته ثبت شد و مالک آن مطلع شد.");
  setReview(data);
  await loadMatrix();
}
async function saveTraces() {
  const rows = Object.entries(judge.value).filter(([, v]) => v.result !== 0 || v.evidence.trim()).map(([k, v]) => {
    const [inputId, outputId] = k.split(":").map(Number);
    return { inputId, outputId, result: v.result, evidence: v.evidence, evidenceRef: v.evidenceRef || null };
  });
  if (!rows.length) return fail("دست‌کم یک معیار را قضاوت کنید.");
  busy.value = true;
  const [data, error] = await saveDesignTraces({ reviewId: review.value.id, rows });
  busy.value = false;
  if (error) return fail(error);
  ok("قضاوت معیارها ثبت شد.");
  setReview(data);
  await loadMatrix();
}
async function saveValidation() {
  busy.value = true;
  const [data, error] = await saveDesignValidation({ reviewId: review.value.id, method: validation.value.method, evidence: validation.value.evidence, customerAcceptanceRef: validation.value.customerAcceptanceRef,
    customerAcceptanceNotApplicable: validation.value.customerAcceptanceNotApplicable, customerAcceptanceNote: validation.value.customerAcceptanceNote });
  busy.value = false;
  if (error) return fail(error);
  ok("مدرک صحه‌گذاری ثبت شد.");
  setReview(data);
}
async function doPrint(kind: "minutes" | "matrix") {
  printing.value = kind;
  await nextTick();
  printForm();
}
const myFinding = (f: any) => f.status === 0 || f.status === 1;
onMounted(async () => {
  const [p] = await getDesignPlans({ Status: 2, PageSize: 300 });
  plans.value = rowsOf(p);
  if (isNew.value && form.value === null) form.value = blank();
  await load();
  if (isNew.value && form.value?.planId) await pickPlan();
});
</script>

<template>
  <div class="inv-page ks-report">
    <DesignReviewPrint v-if="review && printing === 'minutes'" :review="review" />
    <DesignMatrixPrint v-if="review && matrix && printing === 'matrix'" :matrix="matrix" :review="review" />
    <KsReportHeader :title="review ? reviewTypes[review.type] + ' ' + review.number : 'بازبینی طراحی جدید'" :subtitle="review ? review.productCode : ''" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">{{ review ? reviewTypes[review.type] + " " + review.number : "بازبینی طراحی جدید" }}</h1>
        <div v-if="review" class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="doPrint('minutes')">چاپ صورتجلسه</button>
          <button v-if="matrix" class="btn btn-sm btn-outline-secondary" @click="doPrint('matrix')">چاپ ماتریس ردیابی</button>
          <router-link :to="{ name: 'DesignDossier', params: { productId: review.productId } }" class="btn btn-sm btn-outline-secondary">پرونده طراحی محصول</router-link>
        </div>
      </div>

      <div v-if="review" class="mb-3">
        <p class="small mb-1">
          <span class="inv-badge" :class="reviewStatusClass[review.status]">{{ reviewStatuses[review.status] }}</span>
          <span v-if="review.status === 2" class="inv-badge ms-1" :class="outcomeClass[review.outcome]">{{ reviewOutcomes[review.outcome] }}</span>
          محصول <b class="inv-ltr">{{ review.productCode }}</b> · بازنگری <span class="inv-ltr">{{ revisionText(review.productRevision) }}</span> · برنامه <span class="inv-ltr">{{ review.planNumber }}</span>
          · سازنده: {{ review.createdByName }} · شرکت‌کننده مستقل: {{ review.independentCount }}
        </p>
        <p v-if="review.minutes" class="small mb-1">صورتجلسه ({{ jalali(review.heldDate) }}، {{ review.heldByName }}): {{ review.minutes }}</p>
        <p v-if="review.outcomeNote" class="small mb-1">شرح نتیجه ({{ review.closedByName }}): {{ review.outcomeNote }}</p>
        <p v-if="review.voidReason" class="alert alert-secondary py-1 small">باطل شد: {{ review.voidReason }}</p>
        <div v-if="review.blockers.length && review.status < 2" class="alert alert-warning py-1 small">
          <b>تا برطرف نشدن این موارد با نتیجه «پذیرفته» بسته نمی‌شود:</b>
          <ul class="mb-0"><li v-for="(b, i) in review.blockers" :key="i">{{ b }}</li></ul>
        </div>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <button v-if="review.canHold" :disabled="busy" class="btn btn-sm btn-primary" @click="holdDialog = { minutes: '', heldDate: today() }">ثبت برگزاری (صورتجلسه)</button>
          <button v-if="review.canClose" :disabled="busy" class="btn btn-sm btn-success" @click="closeDialog = { outcome: 1, note: '' }">بستن بازبینی</button>
          <button v-if="review.status === 2 && review.outcome !== 1 && !form" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="reviewAction('repeat')">بازبینی تازه با همان دامنه</button>
          <button v-if="review.canVoid" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = { kind: 'review', id: review.id, action: 'void', title: 'ابطال بازبینی' }">ابطال</button>
        </div>
      </div>

      <!-- plan and edit form (a planned review is edited by its creator only) -->
      <div v-if="form" class="border rounded p-3 mb-3 d-print-none">
        <div class="row g-2 mb-2">
          <div class="col-md-5">
            <label class="form-label small mb-0">برنامه طراحی تاییدشده *</label>
            <select v-model="form.planId" class="form-select form-select-sm" :disabled="!!form.id" @change="pickPlan">
              <option :value="null">انتخاب کنید</option>
              <option v-for="p in plans" :key="p.id" :value="p.id">{{ p.number }} — {{ p.productCode }} (بازنگری {{ p.productRevisionText }})</option>
            </select>
          </div>
          <div class="col-md-3">
            <label class="form-label small mb-0">نوع *</label>
            <select v-model.number="form.type" class="form-select form-select-sm" :disabled="!!form.id"><option v-for="(t, k) in reviewTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          </div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ برنامه‌ریزی‌شده</label><KsDatePicker v-model="form.scheduledDate" element-tag="dr-sched" placeholder="تاریخ" simple-placeholder /></div>
          <div class="col-12"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" maxlength="300" /></div>
        </div>
        <h2 class="h6">خروجی‌های مورد بازبینی</h2>
        <div v-for="(o, idx) in form.outputs" :key="idx" class="d-flex gap-2 align-items-center mb-1">
          <span class="small" style="min-width: 130px">{{ outputKinds[o.kind] }}</span>
          <select v-if="o.kind === 2" v-model="o.bomHeaderId" class="form-select form-select-sm">
            <option :value="null">BOM را انتخاب کنید</option>
            <option v-for="b in boms" :key="b.id" :value="b.id">{{ b.code }} — {{ b.title }}</option>
          </select>
          <select v-else-if="o.kind === 3" v-model="o.opcRoutingId" class="form-select form-select-sm">
            <option :value="null">مسیر ساخت را انتخاب کنید</option>
            <option v-for="r in routings" :key="r.id" :value="r.id">{{ r.partNumber }} — {{ r.title }}</option>
          </select>
          <input v-else v-model.number="o.revisionId" type="number" class="form-control form-control-sm" placeholder="شناسه بازنگری نقشه در EDMS" />
          <button class="btn btn-sm btn-outline-danger py-0" @click="form.outputs.splice(idx, 1)">حذف</button>
        </div>
        <div class="d-flex gap-2 mb-3">
          <button class="btn btn-sm btn-outline-secondary" @click="addOutput(2)">+ BOM</button>
          <button class="btn btn-sm btn-outline-secondary" @click="addOutput(1)">+ نقشه</button>
          <button class="btn btn-sm btn-outline-secondary" @click="addOutput(3)">+ مسیر ساخت</button>
        </div>
        <div v-if="form.type === 1 && planInputs.length" class="mb-3">
          <h2 class="h6">ورودی‌های تحت پوشش (بازبینی عمومی)</h2>
          <label v-for="i in planInputs" :key="i.id" class="d-block small"><input type="checkbox" :checked="form.inputIds.includes(i.id)" @change="toggleInput(i.id)" /> {{ i.code }} — {{ inputKinds[i.kind] }}: {{ i.statement }}</label>
        </div>
        <h2 class="h6">شرکت‌کنندگان (دست‌کم یک نفر مستقل از تهیه‌کنندگان خروجی‌ها)</h2>
        <div v-for="(p, idx) in form.participants" :key="idx" class="d-flex gap-2 align-items-center mb-1">
          <div style="min-width: 260px"><UserSelect v-model="p.userId" placeholder="شرکت‌کننده" /></div>
          <input v-model="p.roleText" class="form-control form-control-sm" placeholder="نقش در جلسه" maxlength="100" />
          <button class="btn btn-sm btn-outline-danger py-0" @click="form.participants.splice(idx, 1)">حذف</button>
        </div>
        <div class="d-flex gap-2 mt-2">
          <button class="btn btn-sm btn-outline-secondary" @click="form.participants.push({ userId: null, roleText: '' })">+ شرکت‌کننده</button>
          <button :disabled="busy || !form.planId" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>

      <div v-if="review">
        <h2 class="h6">شرکت‌کنندگان</h2>
        <table class="table table-sm">
          <thead><tr><th>نام</th><th>نقش</th><th>مستقل از تهیه‌کنندگان</th></tr></thead>
          <tbody><tr v-for="p in review.participants" :key="p.id"><td>{{ p.name }}</td><td>{{ p.roleText }}</td><td><span class="inv-badge" :class="p.independent ? 'ok' : 'warn'">{{ p.independent ? "مستقل" : "تهیه‌کننده" }}</span></td></tr></tbody>
        </table>

        <h2 class="h6">خروجی‌های بازبینی‌شده</h2>
        <table class="table table-sm">
          <thead><tr><th>نوع</th><th>خروجی</th><th>بازنگری</th><th>تهیه‌کنندگان</th><th>تغییر پس از انتخاب</th></tr></thead>
          <tbody>
            <tr v-for="o in review.outputs" :key="o.id">
              <td>{{ outputKinds[o.kind] }}</td><td>{{ o.label }}</td><td class="inv-ltr">{{ o.revisionText }}</td><td>{{ o.authorNames.join("، ") }}</td>
              <td><span class="inv-badge" :class="o.unchanged ? 'ok' : 'bad'">{{ o.unchanged ? "بدون تغییر" : "تغییر کرده" }}</span></td>
            </tr>
          </tbody>
        </table>

        <!-- the traceability matrix: input x output with the judged result; an independent participant judges while the review is held -->
        <div v-if="matrix">
          <h2 class="h6">ماتریس ردیابی (ورودی ← خروجی ← مدرک)</h2>
          <div class="table-responsive">
            <table class="table table-sm table-bordered">
              <thead><tr><th>ورودی</th><th>الزام / معیار پذیرش</th><th v-for="o in matrix.outputs" :key="o.id">{{ o.label }}</th></tr></thead>
              <tbody>
                <tr v-for="r in matrix.rows" :key="r.input.inputId" :class="{ 'table-danger': !r.input.covered }">
                  <td class="inv-ltr">{{ r.input.code }}<div class="small inv-muted">{{ inputKinds[r.input.kind] }}</div></td>
                  <td class="small">{{ r.input.statement }}<div class="inv-muted">{{ r.input.acceptanceCriterion }}</div></td>
                  <td v-for="o in matrix.outputs" :key="o.id" style="min-width: 190px">
                    <template v-if="canJudge && judge[key(r.input.inputId, o.id)]">
                      <select v-model.number="judge[key(r.input.inputId, o.id)].result" class="form-select form-select-sm mb-1"><option v-for="(t, k) in traceResults" :key="k" :value="Number(k)">{{ t }}</option></select>
                      <input v-model="judge[key(r.input.inputId, o.id)].evidence" class="form-control form-control-sm mb-1" placeholder="مدرک: اندازه‌گیری، آزمون یا بررسی" maxlength="1000" />
                      <input v-model="judge[key(r.input.inputId, o.id)].evidenceRef" class="form-control form-control-sm inv-ltr" placeholder="مرجع گزارش" maxlength="500" />
                    </template>
                    <template v-else>
                      <span class="inv-badge" :class="traceClass[(r.cells.find((c: any) => c.outputId === o.id) || {}).result || 0]">{{ traceResults[(r.cells.find((c: any) => c.outputId === o.id) || {}).result || 0] }}</span>
                      <div class="small">{{ (r.cells.find((c: any) => c.outputId === o.id) || {}).evidence }}</div>
                      <div class="small inv-muted">{{ (r.cells.find((c: any) => c.outputId === o.id) || {}).judgedByName }}</div>
                    </template>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-if="matrix.uncovered.length" class="alert alert-warning py-1 small">
            <b>ورودی‌های بدون پوشش:</b>
            <ul class="mb-0"><li v-for="u in matrix.uncovered" :key="u.inputId"><span class="inv-ltr">{{ u.code }}</span>: {{ u.reason }}</li></ul>
          </div>
          <button v-if="canJudge" :disabled="busy" class="btn btn-sm btn-primary mb-3 d-print-none" @click="saveTraces">ذخیره قضاوت معیارها</button>
        </div>
        <div v-else>
          <h2 class="h6">ورودی‌های تحت پوشش</h2>
          <table class="table table-sm"><tbody><tr v-for="i in review.inputs" :key="i.inputId"><td class="inv-ltr">{{ i.code }}</td><td>{{ inputKinds[i.kind] }}</td><td>{{ i.statement }}</td><td>{{ i.acceptanceCriterion }}</td></tr></tbody></table>
        </div>

        <!-- validation record -->
        <div v-if="review.type === 3 && validation" class="border rounded p-3 mb-3">
          <h2 class="h6">مدرک صحه‌گذاری (آزمون، آزمایش میدانی یا شبیه‌سازی) و پذیرش مشتری</h2>
          <div class="row g-2">
            <div class="col-md-3"><label class="form-label small mb-0">روش *</label><select v-model.number="validation.method" class="form-select form-select-sm" :disabled="review.status > 1"><option v-for="(t, k) in validationMethods" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-9"><label class="form-label small mb-0">مدرک * (مرجع گزارش آزمون یا شبیه‌سازی)</label><input v-model="validation.evidence" class="form-control form-control-sm" :disabled="review.status > 1" maxlength="2000" /></div>
            <div class="col-md-8"><label class="form-label small mb-0">مدرک پذیرش مشتری</label><input v-model="validation.customerAcceptanceRef" class="form-control form-control-sm" :disabled="review.status > 1 || validation.customerAcceptanceNotApplicable" maxlength="500" /></div>
            <div class="col-md-4 d-flex align-items-end"><label class="small"><input v-model="validation.customerAcceptanceNotApplicable" type="checkbox" :disabled="review.status > 1" /> مشتری ندارد</label></div>
            <div v-if="validation.customerAcceptanceNotApplicable" class="col-12"><label class="form-label small mb-0">توضیح «مشتری ندارد» *</label><input v-model="validation.customerAcceptanceNote" class="form-control form-control-sm" :disabled="review.status > 1" maxlength="1000" /></div>
          </div>
          <button v-if="review.status < 2" :disabled="busy" class="btn btn-sm btn-primary mt-2 d-print-none" @click="saveValidation">ثبت مدرک صحه‌گذاری</button>
        </div>

        <!-- findings: done by the owner, closed by another person, voided by a third person -->
        <h2 class="h6">یافته‌ها و اقدام‌ها</h2>
        <table class="table table-sm">
          <thead><tr><th>#</th><th>شرح</th><th>مالک</th><th>مهلت</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!review.findings.length"><td colspan="6" class="text-center inv-muted">یافته‌ای ثبت نشده است.</td></tr>
            <tr v-for="f in review.findings" :key="f.id" :class="{ 'table-warning': f.late }">
              <td>{{ f.seq }}</td><td>{{ f.description }}<div v-if="f.doneNote" class="small inv-muted">اقدام: {{ f.doneNote }}</div><div v-if="f.closeNote" class="small inv-muted">بستن ({{ f.closedByName }}): {{ f.closeNote }}</div></td>
              <td>{{ f.ownerName }}</td><td>{{ jalali(f.dueDate) }}</td>
              <td><span class="inv-badge" :class="findingClass[f.status]">{{ findingStatuses[f.status] }}</span><span v-if="f.late" class="inv-badge bad ms-1">دیرکرد</span></td>
              <td class="d-print-none text-nowrap">
                <template v-if="review.status < 2 && myFinding(f)">
                  <button v-if="f.status === 0" class="btn btn-sm btn-outline-primary py-0" @click="dialog = { kind: 'finding', id: f.id, action: 'done', title: 'اعلام انجام اقدام (مالک)' }">انجام شد</button>
                  <button v-if="f.status === 1" class="btn btn-sm btn-outline-success py-0" @click="dialog = { kind: 'finding', id: f.id, action: 'close', title: 'بستن یافته (نفر دیگر)' }">بستن</button>
                  <button class="btn btn-sm btn-outline-danger py-0 ms-1" @click="dialog = { kind: 'finding', id: f.id, action: 'void', title: 'ابطال یافته (نفر سوم)' }">ابطال</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
        <div v-if="review.status < 2 && !finding" class="d-print-none"><button class="btn btn-sm btn-outline-secondary" @click="finding = { description: '', ownerUserId: null, dueDate: null }">+ یافته جدید</button></div>
        <div v-if="finding" class="border rounded p-3 d-print-none">
          <div class="row g-2">
            <div class="col-12"><label class="form-label small mb-0">شرح یافته *</label><textarea v-model="finding.description" class="form-control form-control-sm" rows="2" maxlength="1000"></textarea></div>
            <div class="col-md-5"><label class="form-label small mb-0">مالک اقدام *</label><UserSelect v-model="finding.ownerUserId" placeholder="مالک" /></div>
            <div class="col-md-4"><label class="form-label small mb-0">مهلت *</label><KsDatePicker v-model="finding.dueDate" element-tag="df-due" placeholder="تاریخ" simple-placeholder /></div>
          </div>
          <div class="d-flex gap-2 mt-2">
            <button :disabled="busy || !finding.description.trim() || !finding.ownerUserId || !finding.dueDate" class="btn btn-sm btn-primary" @click="addFinding">ثبت یافته</button>
            <button class="btn btn-sm btn-outline-secondary" @click="finding = null">انصراف</button>
          </div>
        </div>
      </div>
    </div>

    <ReasonDialog v-if="dialog" :title="dialog.title" :busy="busy" @confirm="dialogConfirm" @close="dialog = null" />

    <div v-if="holdDialog" class="inv-modal-backdrop d-print-none" @click.self="holdDialog = null">
      <div class="inv-modal" style="max-width: 600px" role="dialog" aria-modal="true">
        <h2 class="inv-title mb-2">ثبت برگزاری جلسه</h2>
        <label class="form-label small mb-0">تاریخ برگزاری</label>
        <KsDatePicker v-model="holdDialog.heldDate" element-tag="dr-held" placeholder="تاریخ" simple-placeholder />
        <label class="form-label small mb-0 mt-2">صورتجلسه *</label>
        <textarea v-model="holdDialog.minutes" class="form-control form-control-sm" rows="4" maxlength="4000"></textarea>
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy || !holdDialog.minutes.trim()" class="btn btn-sm btn-primary" @click="reviewAction('hold', { minutes: holdDialog!.minutes, heldDate: holdDialog!.heldDate })">ثبت</button>
          <button class="btn btn-sm btn-outline-secondary" @click="holdDialog = null">انصراف</button>
        </div>
      </div>
    </div>

    <div v-if="closeDialog" class="inv-modal-backdrop d-print-none" @click.self="closeDialog = null">
      <div class="inv-modal" style="max-width: 600px" role="dialog" aria-modal="true">
        <h2 class="inv-title mb-2">بستن بازبینی</h2>
        <p class="inv-muted small">فقط نتیجه «پذیرفته» دروازه انتشار را باز می‌کند. «پذیرفته با شرط» و «رد» شرح می‌خواهند؛ شرط‌ها باید به‌صورت یافته (با مالک و مهلت) ثبت شده باشند.</p>
        <label class="form-label small mb-0">نتیجه *</label>
        <select v-model.number="closeDialog.outcome" class="form-select form-select-sm"><option v-for="k in [1, 2, 3]" :key="k" :value="k">{{ reviewOutcomes[k] }}</option></select>
        <label class="form-label small mb-0 mt-2">شرح</label>
        <textarea v-model="closeDialog.note" class="form-control form-control-sm" rows="3" maxlength="2000"></textarea>
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy" class="btn btn-sm btn-success" @click="reviewAction('close', { outcome: closeDialog!.outcome, note: closeDialog!.note })">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="closeDialog = null">انصراف</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../../inventory/inventory.scss"></style>
