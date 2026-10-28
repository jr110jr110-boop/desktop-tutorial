<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import ReviewPrint from "@/components/quality/print/ReviewPrint.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { getReview, reviewAction, reviewDecisionAction, saveReviewDecision, saveReviewInput } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { dateInput, printForm, reviewDecisionStatuses, reviewInputCategories, reviewStatuses } from "@/composables/quality";
import { currentUserId, hasRole, QualityRoles } from "@/composables/roles";

// Management review (ISO 9001 9.3): inputs per category, decisions (optionally drafting a CAPA), hold and close with minutes.
// After the closing the decisions are followed up (R1q D23, review M17): the responsible person reports a decision done, QA / PMO / CEO (not he himself) confirms it,
// sends the report back, cancels the decision or opens it again - always with a written reason. The responsible person of a decision reaches the closed review from
// his inbox without being a reader of the module and sees his own decisions only.
const route = useRoute();
const review = ref<any>(null);
const input = ref<any>(null);
const decision = ref<any>(null);
const minutes = ref<string | null>(null);
const busy = ref(false);
async function doPrint() {
  await nextTick();
  printForm();
}
const canWrite = hasRole(QualityRoles.review);
/** the readers of the module see the whole review; anybody else only the decisions he is responsible for */
const seesAll = hasRole(QualityRoles.readers);
const me = currentUserId();
const closed = computed(() => review.value?.status === 2);

async function load() {
  const [data, error] = await getReview(Number(route.params.id));
  if (error) return fail(error);
  review.value = data;
  minutes.value = data.minutesDocumentId || null;
}

async function saveInput() {
  busy.value = true;
  const [, error] = await saveReviewInput({ reviewId: review.value.id, category: input.value.category, text: input.value.text, attendees: input.value.attendees || null });
  busy.value = false;
  if (error) return fail(error);
  input.value = null;
  ok("ورودی ثبت شد.");
  load();
}

async function saveDecision() {
  busy.value = true;
  const d = decision.value;
  const [, error] = await saveReviewDecision({ reviewId: review.value.id, id: d.id, text: d.text, responsibleUserId: d.responsibleUserId, dueDate: d.dueDate || null, createCapa: !!d.createCapa });
  busy.value = false;
  if (error) return fail(error);
  decision.value = null;
  ok("تصمیم ثبت شد.");
  load();
}

async function act(action: string) {
  busy.value = true;
  const [data, error] = await reviewAction(review.value.id, action, minutes.value);
  busy.value = false;
  if (error) return fail(error);
  review.value = data;
  ok("انجام شد.");
}

// ---------------------------------------------------------------------------------------------- follow-up of the decisions
type FollowUp = "done" | "return" | "cancel" | "reopen";
const followUp = ref<{ decision: any; action: FollowUp } | null>(null);

const isOwner = (d: any) => me !== null && d.responsibleUserId === me;
/** the responsible person reports the work done (the decision stays open until a reviewer confirms) */
const canReport = (d: any) => closed.value && d.status === 0 && !d.doneRequested && isOwner(d);
/** QA / PMO / CEO who is not the responsible person: completes the decision (or confirms the report of the owner) */
const canConfirm = (d: any) => closed.value && d.status === 0 && canWrite && !isOwner(d);
const canSendBack = (d: any) => closed.value && d.status === 0 && d.doneRequested && canWrite && !isOwner(d);
const canCancelDecision = (d: any) => closed.value && d.status === 0 && canWrite && !isOwner(d);
const canReopen = (d: any) => closed.value && d.status !== 0 && canWrite;
const statusClass = (d: any) => (d.status === 1 ? "ok" : d.status === 2 ? "" : d.overdue ? "bad" : "warn");

const openCount = computed(() => (review.value ? review.value.decisions.filter((d: any) => d.status === 0).length : 0));
const overdueCount = computed(() => (review.value ? review.value.decisions.filter((d: any) => d.overdue).length : 0));

const followUpDialog = computed(() => {
  const f = followUp.value;
  if (!f) return null;
  const owner = isOwner(f.decision);
  switch (f.action) {
    case "done":
      return owner
        ? { title: "گزارش انجام تصمیم", label: "شرح اقدام انجام‌شده (یا مرجع مدرک)", confirmText: "گزارش انجام", confirmClass: "btn-success", initialText: "", hint: "پس از گزارش، یک نفر از QA، PMO یا مدیرعامل (غیر از شما) آن را تایید می‌کند تا تصمیم «انجام شد» ثبت شود؛ اگر CAPA مرتبط دارد، باید بسته شده باشد." }
        : { title: f.decision.doneRequested ? "تایید انجام تصمیم" : "ثبت انجام تصمیم", label: "شرح اقدام انجام‌شده (یا مرجع مدرک)", confirmText: "انجام شد", confirmClass: "btn-success", initialText: f.decision.doneRequestNote || "", hint: "اگر CAPA مرتبط دارد، باید بسته شده باشد؛ شرح در پرونده می‌ماند." };
    case "return":
      return { title: "برگرداندن گزارش انجام", label: "دلیل برگرداندن", confirmText: "برگردان", confirmClass: "btn-warning", initialText: "", hint: "تصمیم باز می‌ماند و دوباره در صندوق مسئول آن قرار می‌گیرد." };
    case "cancel":
      return { title: "لغو تصمیم", label: "دلیل لغو", confirmText: "لغو تصمیم", confirmClass: "btn-danger", initialText: "", hint: "تصمیم لغو‌شده دیگر پیگیری و دیرکرد حساب نمی‌شود؛ مسئول تصمیم نمی‌تواند خودش آن را لغو کند." };
    default:
      return { title: "بازگشایی تصمیم", label: "دلیل بازگشایی", confirmText: "بازگشایی", confirmClass: "btn-primary", initialText: "", hint: "تصمیم دوباره باز و پیگیری می‌شود." };
  }
});

const followUpDone: Record<FollowUp, string> = {
  done: "ثبت شد.",
  return: "گزارش انجام به مسئول برگردانده شد.",
  cancel: "تصمیم لغو شد.",
  reopen: "تصمیم دوباره باز شد.",
};

async function decide(text: string) {
  const f = followUp.value;
  if (!f) return;
  busy.value = true;
  const [data, error] = await reviewDecisionAction(f.decision.id, f.action, text);
  busy.value = false;
  if (error) return fail(error);
  review.value = data;
  followUp.value = null;
  ok(followUpDone[f.action]);
}

onMounted(load);
</script>

<template>
  <div v-if="review" class="inv-page">
    <ReviewPrint v-if="seesAll" :review="review" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">بازنگری مدیریت — {{ jalali(review.reviewDate) }}</h1>
          <span :class="{ ok: review.status === 2, info: review.status === 1, warn: review.status === 0 }" class="inv-badge">{{ reviewStatuses[review.status] }}</span>
          <span v-if="closed && openCount" class="inv-badge warn ms-1">{{ openCount }} تصمیم باز</span>
          <span v-if="overdueCount" class="inv-badge bad ms-1">{{ overdueCount }} تصمیم از موعد گذشته</span>
          <span class="inv-muted ms-2">حاضرین: {{ review.attendees || "—" }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2 align-items-end">
          <router-link :to="seesAll ? { name: 'QualityReviews' } : { name: 'OfInbox' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="seesAll" class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ صورتجلسه (FO-QA-022)</button>
          <template v-if="canWrite">
            <button v-if="review.status === 0" :disabled="busy" class="btn btn-sm btn-primary" @click="act('hold')">جلسه برگزار شد</button>
            <button v-if="review.status === 1" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act('redraft')">بازگشت به پیش‌نویس</button>
            <button v-if="review.status === 1" :disabled="busy" class="btn btn-sm btn-success" @click="act('close')">بستن (با صورتجلسه)</button>
          </template>
        </div>
      </div>
      <div class="col-md-6"><label class="form-label small mb-0">صورتجلسه (فایل)</label><DocAttach v-model="minutes" title="صورتجلسه بازنگری مدیریت" :disabled="!canWrite || review.status === 2" /></div>
      <p v-if="!seesAll" class="inv-muted small mt-2 mb-0">فقط تصمیم‌هایی که شما مسئول آن‌ها هستید نمایش داده می‌شود.</p>
    </div>

    <div v-if="seesAll" class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">ورودی‌ها (۹.۳.۲)</h2>
        <button v-if="canWrite && review.status !== 2" class="btn btn-sm btn-primary" @click="input = { category: 1, text: '', attendees: review.attendees }">ورودی جدید</button>
      </div>
      <div v-if="input" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">دسته</label><select v-model="input.category" class="form-select form-select-sm"><option v-for="(t, k) in reviewInputCategories" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-8"><label class="form-label small mb-0">حاضرین</label><input v-model="input.attendees" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">متن *</label><textarea v-model="input.text" class="form-control form-control-sm" rows="3"></textarea></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveInput">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="input = null">انصراف</button></div>
        </div>
      </div>
      <table class="table table-sm">
        <thead><tr><th style="width: 25%">دسته</th><th>متن</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!review.inputs.length"><td colspan="3" class="text-center inv-muted">ورودی‌ای نیست.</td></tr>
          <tr v-for="i in review.inputs" :key="i.id">
            <td>{{ i.categoryName || reviewInputCategories[i.category] }} <span v-if="i.autoDrafted" class="inv-badge info">پیش‌نویس خودکار</span></td>
            <td class="small" style="white-space: pre-wrap">{{ i.text }}</td>
            <td><button v-if="canWrite && review.status !== 2" class="btn btn-sm btn-outline-secondary" @click="input = { category: i.category, text: i.text, attendees: review.attendees }">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">تصمیمات و اقدامات (۹.۳.۳)</h2>
        <button v-if="canWrite && review.status !== 2" class="btn btn-sm btn-primary" @click="decision = { id: null, text: '', responsibleUserId: null, dueDate: '', createCapa: false }">تصمیم جدید</button>
      </div>
      <div v-if="decision" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">متن تصمیم *</label><textarea v-model="decision.text" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-md-4"><label class="form-label small mb-0">مسئول</label><UserSelect v-model="decision.responsibleUserId" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مهلت</label><input v-model="decision.dueDate" class="form-control form-control-sm" type="date" /></div>
          <div v-if="!decision.id" class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="decision.createCapa" class="form-check-input" type="checkbox" /> ایجاد پیش‌نویس CAPA</label></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveDecision">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="decision = null">انصراف</button></div>
        </div>
      </div>
      <table class="table table-sm mb-0">
        <thead><tr><th>تصمیم</th><th>مسئول</th><th>مهلت</th><th v-if="closed">وضعیت</th><th>CAPA</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!review.decisions.length"><td :colspan="closed ? 6 : 5" class="text-center inv-muted">تصمیمی ثبت نشده.</td></tr>
          <tr v-for="d in review.decisions" :key="d.id" :class="{ 'table-warning': d.overdue }">
            <td class="small" style="white-space: pre-wrap">
              {{ d.text }}
              <div v-if="d.doneRequested" class="mt-1">
                <span class="inv-badge info">گزارش انجام داده شده؛ منتظر تایید QA یا PMO</span>
                <span class="inv-muted"> {{ d.doneRequestedByName }} — {{ jalali(d.doneRequestedAt, true) }}: {{ d.doneRequestNote }}</span>
              </div>
              <div v-else-if="d.status === 0 && d.doneNote" class="mt-1 text-danger">گزارش انجام برگردانده شد: {{ d.doneNote }}</div>
              <div v-if="d.status !== 0" class="mt-1 inv-muted">{{ reviewDecisionStatuses[d.status] }} توسط {{ d.doneByName || "—" }} — {{ jalali(d.doneAt, true) }}: {{ d.doneNote }}</div>
            </td>
            <td>{{ d.responsibleName }}</td>
            <td :class="{ 'text-danger': d.overdue }">{{ jalali(d.dueDate) }}</td>
            <td v-if="closed">
              <span :class="statusClass(d)" class="inv-badge">{{ reviewDecisionStatuses[d.status] }}</span>
              <span v-if="d.overdue" class="inv-badge bad ms-1">از موعد گذشته</span>
            </td>
            <td><router-link v-if="d.capaId" :to="{ name: 'QualityCapaEdit', params: { id: d.capaId } }" class="inv-ltr">{{ d.capaNumber }}</router-link></td>
            <td>
              <div class="d-flex flex-wrap gap-1">
                <button v-if="canWrite && review.status !== 2" class="btn btn-sm btn-outline-secondary" @click="decision = { ...d, dueDate: dateInput(d.dueDate), createCapa: false }">ویرایش</button>
                <button v-if="canReport(d)" :disabled="busy" class="btn btn-sm btn-outline-success" @click="followUp = { decision: d, action: 'done' }">گزارش انجام</button>
                <button v-if="canConfirm(d)" :disabled="busy" class="btn btn-sm btn-outline-success" @click="followUp = { decision: d, action: 'done' }">{{ d.doneRequested ? "تایید انجام" : "ثبت انجام" }}</button>
                <button v-if="canSendBack(d)" :disabled="busy" class="btn btn-sm btn-outline-warning" @click="followUp = { decision: d, action: 'return' }">برگرداندن</button>
                <button v-if="canCancelDecision(d)" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="followUp = { decision: d, action: 'cancel' }">لغو</button>
                <button v-if="canReopen(d)" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="followUp = { decision: d, action: 'reopen' }">بازگشایی</button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-if="closed" class="inv-muted small mt-2 mb-0">
        پیگیری: مسئول تصمیم انجام آن را گزارش می‌کند و یک نفر از QA، PMO یا مدیرعامل (غیر از خود او) تایید می‌کند؛ تصمیمی که CAPA دارد پس از بسته‌شدن CAPA انجام‌شده ثبت می‌شود. لغو و بازگشایی با دلیل مکتوب است و مسئول تصمیم، آن را خودش لغو نمی‌کند.
      </p>
    </div>

    <ReasonDialog
      v-if="followUp && followUpDialog"
      :key="followUp.decision.id + followUp.action"
      :busy="busy"
      :confirm-class="followUpDialog.confirmClass"
      :confirm-text="followUpDialog.confirmText"
      :hint="followUpDialog.hint"
      :initial-text="followUpDialog.initialText"
      :label="followUpDialog.label"
      :title="followUpDialog.title"
      @close="followUp = null"
      @confirm="decide"
    />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
