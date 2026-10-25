<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import ProposalResultDialog from "./ProposalResultDialog.vue";
import { getSlEstimate, getSlEstimates, getSlProposal, getSlSettings, proposalAction, saveSlProposal } from "@/api/SalesAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { badgeOfProposal, persianOf, proposalStatuses, rialText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";
import { blankToNull, groupDigits, inquiryLabel, inquirySearch, numOrNull, openDoc, uploadDoc } from "./pipelineKit";

// Proposal editor / detail. id = null creates a proposal from a FINAL estimate of an inquiry the CEO decided to bid on.
// Buttons follow the API state and the role: approve (SALES within the threshold, CEO otherwise; below cost needs a note), submit, revise, cancel, result.
const props = defineProps({ id: { type: Number, default: null } });
const emit = defineEmits(["changed"]);
const router = useRouter();

const d = ref<any>(null);
const f = ref<any>(blank());
const deviations = ref<string[]>([]);
const inquiry = ref<any>(null);
const estimates = ref<any[]>([]);
const estimate = ref<any>(null);
const threshold = ref<number | null | undefined>(undefined);
const note = ref("");
const busy = ref(false);
const showResult = ref(false);
const searchBidding = inquirySearch(4);
const canWrite = hasRole(SalesRoles.writers);
const isCeo = hasRole(SalesRoles.ceo);
const canApproveRole = hasRole(SalesRoles.proposalApprovers);
const isNew = computed(() => !d.value);
const editable = computed(() => canWrite && (!d.value || d.value.status === 1));
const canApprove = computed(() => d.value?.status === 1 && canApproveRole && (isCeo || !d.value.needsCeo));
const day = (v: any) => (v ? String(v).slice(0, 10) : null);

function blank() {
  return { estimateId: null as number | null, technicalDocumentId: null as string | null, commercialDocumentId: null as string | null, validityDays: "", price: "", deliveryWeeks: "", paymentTermsText: "", guaranteesText: "",
    vatPercent: "", advancePercent: "", retentionPercent: "", latePenaltyPercentPerWeek: "", penaltyCapPercent: "", clientReviewDays: "", customerReference: "" };
}
const txt = (v: any) => (v === null || v === undefined ? "" : String(v));
function fill(x: any) {
  d.value = x;
  f.value = {
    estimateId: x.estimateId, technicalDocumentId: x.technicalDocumentId || null, commercialDocumentId: x.commercialDocumentId || null, validityDays: txt(x.validityDays), price: x.price !== null && x.price !== undefined ? groupDigits(x.price) : "",
    deliveryWeeks: txt(x.deliveryWeeks), paymentTermsText: x.paymentTermsText || "", guaranteesText: x.guaranteesText || "", vatPercent: txt(x.vatPercent), advancePercent: txt(x.advancePercent),
    retentionPercent: txt(x.retentionPercent), latePenaltyPercentPerWeek: txt(x.latePenaltyPercentPerWeek), penaltyCapPercent: txt(x.penaltyCapPercent), clientReviewDays: txt(x.clientReviewDays), customerReference: x.customerReference || "",
  };
  deviations.value = String(x.deviationsText || "").split("\n").map((s) => s.trim()).filter(Boolean);
}
async function loadEstimate(estimateId: number | null) {
  estimate.value = null;
  if (!estimateId) return;
  const [e] = await getSlEstimate(estimateId);
  estimate.value = e || null;
}
async function load() {
  const [s] = await getSlSettings();
  const t = Array.isArray(s) ? s.find((x: any) => x.key === "ProposalApprovalThresholdRial") : null;
  threshold.value = t ? t.value : null;
  if (!props.id) return;
  const [x, error] = await getSlProposal(props.id);
  if (error) return fail(error);
  fill(x);
  await loadEstimate(x.estimateId);
}
async function pickInquiry(i: any) {
  inquiry.value = i;
  estimates.value = [];
  f.value.estimateId = null;
  estimate.value = null;
  if (!i) return;
  const [e] = await getSlEstimates({ InquiryId: i.id, Status: 2, PageSize: 20 });
  estimates.value = rowsOf(camelize(e));
  if (estimates.value.length === 1) await pickEstimate(estimates.value[0].id);
}
async function pickEstimate(estimateId: number | null) {
  f.value.estimateId = estimateId;
  await loadEstimate(estimateId);
}
async function upload(e: Event, field: "technicalDocumentId" | "commercialDocumentId") {
  const input = e.target as HTMLInputElement;
  const file = input.files?.[0];
  if (file) {
    const idv = await uploadDoc(file);
    if (idv) f.value[field] = idv;
  }
  input.value = "";
}
function payload() {
  const v = f.value;
  return {
    id: d.value?.id ?? null, inquiryId: d.value?.inquiryId ?? inquiry.value?.id, estimateId: v.estimateId, technicalDocumentId: v.technicalDocumentId, commercialDocumentId: v.commercialDocumentId,
    validityDays: numOrNull(v.validityDays), price: numOrNull(v.price), deliveryWeeks: numOrNull(v.deliveryWeeks), paymentTermsText: blankToNull(v.paymentTermsText), guaranteesText: blankToNull(v.guaranteesText),
    deviationsText: deviations.value.map((s) => s.trim()).filter(Boolean).join("\n") || null, vatPercent: numOrNull(v.vatPercent), advancePercent: numOrNull(v.advancePercent), retentionPercent: numOrNull(v.retentionPercent),
    latePenaltyPercentPerWeek: numOrNull(v.latePenaltyPercentPerWeek), penaltyCapPercent: numOrNull(v.penaltyCapPercent), clientReviewDays: numOrNull(v.clientReviewDays), customerReference: blankToNull(v.customerReference),
    rowVersion: d.value?.rowVersion ?? null,
  };
}
async function save() {
  if (!payload().inquiryId) return fail({ message: "استعلام را انتخاب کنید." });
  if (!f.value.estimateId) return fail({ message: "برآورد نهایی‌شده را انتخاب کنید." });
  busy.value = true;
  const [x, error] = await saveSlProposal(payload());
  busy.value = false;
  if (error) return fail(error);
  ok("پیشنهاد ذخیره شد.");
  const created = !d.value;
  fill(x);
  await loadEstimate(x.estimateId);
  emit("changed", created ? x : null);
  return true;
}
async function act(action: "approve" | "submit" | "revise" | "cancel") {
  if ((action === "cancel" || (action === "approve" && d.value.belowCost)) && !note.value.trim()) return fail({ message: action === "cancel" ? "دلیل لغو را بنویسید." : "برای تایید پیشنهاد زیر بهای تمام‌شده، دلیل را بنویسید." });
  busy.value = true;
  const [x, error] = await proposalAction({ id: d.value.id, action, note: note.value, rowVersion: d.value.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok({ approve: "پیشنهاد تایید شد.", submit: "پیشنهاد برای مشتری ارسال شد.", revise: "بازنگری جدید ساخته شد.", cancel: "پیشنهاد لغو شد." }[action]);
  note.value = "";
  if (action === "revise") {
    emit("changed", x);
    return router.push({ name: "SlProposal", params: { id: x.id } });
  }
  fill(x);
  emit("changed", null);
}
async function resultDone(x: any) {
  showResult.value = false;
  fill(x);
  emit("changed", null);
}
const priceVsCost = computed(() => {
  const price = numOrNull(f.value.price) ?? estimate.value?.price ?? null;
  const cost = estimate.value?.totalCost ?? d.value?.estimateTotalCost ?? null;
  return price !== null && cost !== null && price < cost;
});
const belowCost = computed(() => d.value?.belowCost || priceVsCost.value);
onMounted(load);
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <div>
        <h1 class="inv-title">{{ d ? "پیشنهاد " + d.number : "پیشنهاد قیمت جدید" }}<span v-if="d" class="small text-muted"> - بازنگری {{ d.revision }}</span></h1>
        <template v-if="d">
          <span :class="badgeOfProposal(d.status)" class="inv-badge">{{ d.statusText || proposalStatuses[d.status] }}</span>
          <span class="ms-2">{{ d.customerName }} -</span>
          <router-link class="ms-1 inv-ltr" :to="{ name: 'SlInquiry', params: { id: d.inquiryId } }">{{ d.inquiryNumber }}</router-link>
        </template>
      </div>
      <div v-if="canWrite" class="d-flex gap-2">
        <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        <button v-if="d && d.status === 2" :disabled="busy" class="btn btn-sm btn-success" @click="act('submit')">ارسال برای مشتری</button>
        <button v-if="d && (d.status === 2 || d.status === 3)" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="act('revise')">بازنگری جدید</button>
      </div>
    </div>

    <div v-if="belowCost" class="alert alert-danger py-2">قیمت پیشنهاد کمتر از بهای تمام‌شده برآورد است؛ تایید فقط با مدیرعامل و با ثبت دلیل در توضیح ممکن است.</div>
    <div v-else-if="d && d.status === 1 && d.needsCeo" class="alert alert-warning py-2">
      این پیشنهاد را فقط مدیرعامل تایید می‌کند<span v-if="threshold === null || threshold === undefined"> (سقف تایید فروش هنوز تعیین نشده است)</span><span v-else> (قیمت از سقف تایید فروش، {{ rialText(threshold) }} ریال، بیشتر است)</span>.
    </div>

    <div class="row g-2">
      <template v-if="isNew">
        <div class="col-md-6"><label class="form-label small mb-0">استعلام (در حال تهیه پیشنهاد)</label><LookupBox v-model="inquiry" :fetch="searchBidding" :label="inquiryLabel" @pick="pickInquiry" /></div>
      </template>
      <div class="col-md-6">
        <label class="form-label small mb-0">برآورد نهایی‌شده</label>
        <select v-if="isNew" :value="f.estimateId" class="form-select form-select-sm" @change="pickEstimate(Number(($event.target as HTMLSelectElement).value) || null)">
          <option :value="null">-</option><option v-for="e in estimates" :key="e.id" :value="e.id">{{ e.number }} - قیمت {{ rialText(e.price) }}</option>
        </select>
        <div v-else class="form-control form-control-sm bg-light">
          <router-link v-if="estimate" class="inv-ltr" :to="{ name: 'SlEstimate', params: { id: estimate.id } }">{{ estimate.number }}</router-link>
          <span v-if="estimate"> - بازنگری {{ estimate.revision }} - بهای تمام‌شده {{ rialText(estimate.totalCost) }} - قیمت برآورد {{ rialText(estimate.price) }}</span>
        </div>
      </div>
      <div v-if="isNew && estimate" class="col-12 small text-muted">بهای تمام‌شده برآورد: {{ rialText(estimate.totalCost) }} - قیمت برآورد: {{ rialText(estimate.price) }} (قیمت خالی = قیمت برآورد)</div>

      <div class="col-md-4"><label class="form-label small mb-0">قیمت (ریال، پیش از ارزش افزوده)</label><input v-model="f.price" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="numeric" :placeholder="estimate?.price ? groupDigits(estimate.price) : ''" @blur="f.price = groupDigits(f.price)" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">اعتبار (روز)</label><input v-model="f.validityDays" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="numeric" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">زمان تحویل (هفته)</label><input v-model="f.deliveryWeeks" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="numeric" /></div>
      <div class="col-md-4"><label class="form-label small mb-0">شماره ارجاع مشتری</label><input v-model="f.customerReference" :disabled="!editable" class="form-control form-control-sm inv-ltr" /></div>

      <div class="col-md-6">
        <label class="form-label small mb-0">پیشنهاد فنی</label>
        <div class="d-flex gap-2 align-items-center"><input v-if="editable" class="form-control form-control-sm" type="file" @change="upload($event, 'technicalDocumentId')" /><a v-if="f.technicalDocumentId" href="#" class="text-nowrap" @click.prevent="openDoc(f.technicalDocumentId, 'technical-proposal')">دریافت فایل</a><span v-else-if="!editable" class="text-muted">-</span></div>
      </div>
      <div class="col-md-6">
        <label class="form-label small mb-0">پیشنهاد بازرگانی</label>
        <div class="d-flex gap-2 align-items-center"><input v-if="editable" class="form-control form-control-sm" type="file" @change="upload($event, 'commercialDocumentId')" /><a v-if="f.commercialDocumentId" href="#" class="text-nowrap" @click.prevent="openDoc(f.commercialDocumentId, 'commercial-proposal')">دریافت فایل</a><span v-else-if="!editable" class="text-muted">-</span></div>
      </div>

      <div class="col-md-6"><label class="form-label small mb-0">شرایط پرداخت</label><textarea v-model="f.paymentTermsText" :disabled="!editable" class="form-control form-control-sm" rows="3" /></div>
      <div class="col-md-6"><label class="form-label small mb-0">تضمین‌ها</label><textarea v-model="f.guaranteesText" :disabled="!editable" class="form-control form-control-sm" rows="3" /></div>

      <div class="col-md-2"><label class="form-label small mb-0">ارزش افزوده (درصد)</label><input v-model="f.vatPercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">پیش‌پرداخت (درصد)</label><input v-model="f.advancePercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">حسن انجام کار (درصد)</label><input v-model="f.retentionPercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">جریمه تاخیر هفتگی (درصد)</label><input v-model="f.latePenaltyPercentPerWeek" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">سقف جریمه (درصد)</label><input v-model="f.penaltyCapPercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">مهلت بررسی کارفرما (روز)</label><input v-model="f.clientReviewDays" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="numeric" /></div>

      <div class="col-12">
        <div class="d-flex align-items-center justify-content-between"><label class="form-label small mb-0">انحراف‌ها از درخواست مشتری</label><button v-if="editable" class="btn btn-sm btn-outline-secondary" @click="deviations.push('')">افزودن انحراف</button></div>
        <div v-if="!deviations.length" class="small text-muted">انحرافی ثبت نشده است.</div>
        <div v-for="(_, i) in deviations" :key="i" class="d-flex gap-2 mt-1"><input v-model="deviations[i]" :disabled="!editable" class="form-control form-control-sm" /><button v-if="editable" class="btn btn-sm btn-outline-danger" @click="deviations.splice(i, 1)">حذف</button></div>
      </div>
    </div>

    <div v-if="d" class="border rounded p-2 mt-3">
      <div class="row g-2 small">
        <div v-if="d.approvedAtText" class="col-md-4">تایید: {{ d.approvedByName || "" }} - {{ persianOf(d.approvedAtText, null) }}<div v-if="d.approvalNote" class="text-muted">{{ d.approvalNote }}</div></div>
        <div v-if="d.submittedAtText" class="col-md-4">ارسال به مشتری: {{ persianOf(d.submittedAtText, null) }}</div>
        <div v-if="d.resultAtText" class="col-md-4">
          نتیجه: <b>{{ proposalStatuses[d.status] }}</b> - {{ persianOf(d.resultAtText, null) }}
          <div v-if="d.resultReason">دلیل: {{ d.resultReason }}</div>
          <div v-if="d.competitor">رقیب: {{ d.competitor }}<span v-if="d.competitorPrice !== null && d.competitorPrice !== undefined"> - قیمت رقیب: {{ rialText(d.competitorPrice) }} ریال</span></div>
          <div v-if="d.lessonsLearned">درس‌آموخته: {{ d.lessonsLearned }}</div>
        </div>
      </div>

      <div v-if="d.contractId" class="alert alert-success py-2 mt-2 mb-0">
        قرارداد پیش‌نویس ساخته شد:
        <router-link :to="{ name: 'ContractUpdate', params: { id: d.contractId } }">مشاهده قرارداد</router-link>.
        بازبینی قرارداد و آغاز پروژه به‌صورت پیش‌نویس در کنترل پروژه ایجاد شده است:
        <router-link :to="{ name: 'ProjectControlPortfolio' }">پرتفوی کنترل پروژه</router-link>
      </div>

      <div v-if="(d.status === 1 && (canApprove || canWrite)) || ((d.status === 1 || d.status === 2 || d.status === 3) && canWrite) || (d.status === 3 && canApproveRole)" class="mt-3">
        <label class="form-label small mb-0">توضیح (برای تایید زیر بهای تمام‌شده و برای لغو الزامی است)</label>
        <textarea v-model="note" class="form-control form-control-sm mb-2" rows="2" />
        <div class="d-flex gap-2 flex-wrap">
          <button v-if="canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید پیشنهاد</button>
          <button v-if="d.status === 3 && canApproveRole" :disabled="busy" class="btn btn-sm btn-primary" @click="showResult = true">ثبت نتیجه (برنده / بازنده)</button>
          <button v-if="canWrite && [1, 2, 3].includes(d.status)" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">لغو پیشنهاد</button>
        </div>
      </div>
    </div>
    <ProposalResultDialog v-if="showResult && d" :proposal="d" @close="showResult = false" @done="resultDone" />
  </div>
</template>
