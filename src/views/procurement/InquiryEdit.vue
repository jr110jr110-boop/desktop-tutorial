<script setup lang="ts">
import AttachmentPanel from "@/components/common/AttachmentPanel.vue";
import { AttachmentOwner } from "@/composables/attachments";
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import AiNotesBox from "@/components/ai/AiNotesBox.vue";
import PrintForm from "@/components/print/PrintForm.vue";
import { getProviders, rowsOf, uploadFile } from "@/api/InventoryAPIs";
import {
  createOrderFromInquiry, getInquiry, getPurchaseRequest, inquiryAction, saveInquiry, selectQuote, technicalReview,
} from "@/api/ProcurementAPIs";
import { inquiryPlanningAction } from "@/api/Pb6APIs";
import { fail, jalali, num, ok, printPage, rial, today } from "@/composables/inventory";
import { inquiryStatuses, supplierGrades } from "@/composables/procurement";
import { hasRole, ProcurementRoles } from "@/composables/roles";

// Inquiry: quotes of the suppliers for the lines of a purchase request, the choice with its reason, and the committee approvals.
const route = useRoute();
const router = useRouter();
const inq = ref<any>(null);
const request = ref<any>(null);
const providers = ref<any[]>([]);
const quotes = ref<any[]>([]);
const chosen = ref<number | null>(null);
const reason = ref("");
const busy = ref(false);
const minutes = ref<string | null>(null);
const pendingAction = ref("");

const isNew = computed(() => !inq.value?.id);
const open = computed(() => !inq.value?.id || inq.value.status === 0);
const canCommercial = hasRole(ProcurementRoles.commercial);
const canEdit = computed(() => open.value && canCommercial);
const canFinancial = hasRole(ProcurementRoles.committeeFinancial);
const canPlanningStep = hasRole(ProcurementRoles.planning);
const plan = ref<{ amount: number | null; days: number | null; note: string }>({ amount: null, days: null, note: "" });
const canTechnical = hasRole(ProcurementRoles.technical);
const canManagement = hasRole(ProcurementRoles.management);
const selected = computed(() => inq.value?.quotes?.find((q: any) => q.id === inq.value.selectedQuoteId));

function newQuote() {
  return {
    key: Math.random().toString(36).slice(2), providerId: null, quoteNumber: "", quoteDate: today(), validUntil: "", deliveryDays: null,
    paymentTerms: "", vatPercent: null, technicallyAccepted: true, technicalScore: null, technicalNote: "", documentId: null, notes: "",
    prices: Object.fromEntries((request.value?.lines || []).map((l: any) => [l.id, null])) as Record<number, number | null>,
  };
}

function loadInquiry(dto: any) {
  inq.value = dto;
  chosen.value = dto.selectedQuoteId;
  quotes.value = dto.quotes.map((q: any) => ({
    ...newQuote(), id: q.id, providerId: q.providerId, quoteNumber: q.quoteNumber || "", quoteDate: (q.quoteDate || "").slice(0, 10),
    validUntil: q.validUntil ? q.validUntil.slice(0, 10) : "", deliveryDays: q.deliveryDays, paymentTerms: q.paymentTerms || "",
    vatPercent: q.vatPercent, technicallyAccepted: q.technicallyAccepted, technicalScore: q.technicalScore, technicalNote: q.technicalNote || "",
    documentId: q.documentId, notes: q.notes || "", totalAmount: q.totalAmount, lowest: q.lowest, grade: q.grade, blocked: q.blocked,
    prices: Object.fromEntries(q.lines.map((l: any) => [l.requestLineId, l.unitPrice])),
  }));
}

async function save() {
  if (!quotes.value.length) return fail("حداقل یک پیشنهاد لازم است.");
  busy.value = true;
  const [data, error] = await saveInquiry({
    id: inq.value.id, purchaseRequestId: request.value.id, inquiryDate: inq.value.inquiryDate, description: inq.value.description,
    fewerQuotesReason: inq.value.fewerQuotesReason,
    quotes: quotes.value.map((q) => ({
      providerId: q.providerId, quoteNumber: q.quoteNumber, quoteDate: q.quoteDate, validUntil: q.validUntil || null, deliveryDays: q.deliveryDays,
      paymentTerms: q.paymentTerms, vatPercent: q.vatPercent, technicallyAccepted: q.technicallyAccepted, technicalScore: q.technicalScore,
      technicalNote: q.technicalNote, documentId: q.documentId, notes: q.notes,
      lines: request.value.lines
        .filter((l: any) => q.prices[l.id] !== null && q.prices[l.id] !== "" && q.prices[l.id] !== undefined)
        .map((l: any) => ({ requestLineId: l.id, quantity: l.quantity, unitPrice: Number(q.prices[l.id]) })),
    })),
  });
  busy.value = false;
  if (error) return fail(error);
  loadInquiry(data);
  ok("ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "ProcurementInquiryEdit", params: { id: data.id } });
}

async function choose() {
  const q = quotes.value.find((x) => x.id === chosen.value);
  if (!q) return fail("پیشنهاد برنده را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await selectQuote(inq.value.id, q.id, reason.value);
  busy.value = false;
  if (error) return fail(error);
  loadInquiry(data);
  ok("تامین‌کننده انتخاب شد.");
}

// R1c M12: engineering or quality judges each quote against the technical requirements (accept / reject + score); the buyer cannot
const review = ref<null | { quote: any; accepted: boolean; score: string; note: string }>(null);
async function sendReview() {
  const r = review.value!;
  const score = r.score.trim() === "" ? null : Number(r.score);
  if (score !== null && (!Number.isFinite(score) || score < 0 || score > 100)) return fail("امتیاز فنی باید بین ۰ و ۱۰۰ باشد.");
  if (!r.accepted && !r.note.trim()) return fail("برای رد فنی، علت را بنویسید.");
  busy.value = true;
  const [data, error] = await technicalReview({ inquiryId: inq.value.id, quoteId: r.quote.id, accepted: r.accepted, score, note: r.note.trim() || null });
  busy.value = false;
  if (error) return fail(error);
  review.value = null;
  ok("بررسی فنی ثبت شد.");
  if (data?.quotes) loadInquiry(data);
  else {
    const [d] = await getInquiry(inq.value.id);
    if (d) loadInquiry(d);
  }
}

async function planningStep(action: string) {
  busy.value = true;
  const [data, error] = await inquiryPlanningAction(inq.value.id, action, { note: plan.value.note, amount: plan.value.amount, days: plan.value.days });
  busy.value = false;
  if (error) return fail(error);
  if (data?.id && data.id !== inq.value.id) {
    router.replace({ name: "ProcurementInquiryEdit", params: { id: data.id } }).then(() => window.location.reload());
    return;
  }
  loadInquiry(data);
  ok("ثبت شد.");
}

async function act(action: string, reference?: string) {
  busy.value = true;
  const [data, error] = await inquiryAction(inq.value.id, action, undefined, reference);
  busy.value = false;
  if (error) return fail(error);
  loadInquiry(data);
  ok("ثبت شد.");
}

function approve(action: string) {
  if (action === "approve-management") {
    pendingAction.value = action;
    minutes.value = inq.value.committeeMinutes || "";
  } else act(action);
}

async function makeOrder() {
  busy.value = true;
  const [data, error] = await createOrderFromInquiry(inq.value.id);
  busy.value = false;
  if (error) return fail(error);
  ok("سفارش خرید ساخته شد.");
  router.push({ name: "ProcurementOrderEdit", params: { id: data.id } });
}

async function attach(q: any, event: any) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  const [id, error] = await uploadFile(file, "پیشنهاد قیمت " + (inq.value.number || ""));
  if (error) return fail(error);
  q.documentId = id;
  ok("فایل پیشنهاد بارگذاری شد؛ استعلام را ذخیره کنید.");
}

const providerName = (id: number) => providers.value.find((p) => p.id === id)?.name || "";
const totalOf = (q: any) => (request.value?.lines || []).reduce((s: number, l: any) => s + (Number(q.prices[l.id]) || 0) * l.quantity, 0);

onMounted(async () => {
  getProviders().then(([l]) => (providers.value = rowsOf(l)));
  if (route.params.id === "new") {
    const [r, error] = await getPurchaseRequest(Number(route.query.request));
    if (error) return fail(error);
    request.value = r;
    inq.value = { id: null, status: 0, inquiryDate: today(), description: "", fewerQuotesReason: "", requiredQuotes: r.requiredQuotes, quotes: [] };
    quotes.value = [newQuote()];
  } else {
    const [data, error] = await getInquiry(Number(route.params.id));
    if (error) return fail(error);
    const [r] = await getPurchaseRequest(data.purchaseRequestId);
    request.value = r;
    loadInquiry(data);
  }
});
</script>

<template>
  <div v-if="inq && request" class="inv-page">
    <PrintForm
      v-if="!isNew"
      title="جدول مقایسه استعلام قیمت"
      :fields="[
        { label: 'شماره استعلام', value: inq.number, ltr: true },
        { label: 'درخواست خرید', value: inq.purchaseRequestNumber, ltr: true },
        { label: 'تاریخ', value: jalali(inq.inquiryDate) },
        { label: 'تعداد پیشنهاد لازم', value: inq.requiredQuotes },
        { label: 'وضعیت', value: inquiryStatuses[inq.status] },
        { label: 'تامین‌کننده منتخب', value: selected?.providerName || '-' },
        { label: 'صورت‌جلسه کمیته', value: inq.committeeMinutes || '-' },
      ]"
      :notes="[
        ...(inq.selectionReason ? [{ label: 'دلیل انتخاب', value: inq.selectionReason }] : []),
        ...(inq.fewerQuotesReason ? [{ label: 'دلیل تعداد کمتر پیشنهاد', value: inq.fewerQuotesReason }] : []),
      ]"
      :signatures="[
        { title: 'بازرگانی', name: inq.commercialApprovedByName, date: jalali(inq.commercialApprovedAt) },
        { title: 'معاونت PMO (مالی)', name: inq.financialApprovedByName, date: jalali(inq.financialApprovedAt) },
        { title: 'فنی', name: inq.technicalApprovedByName, date: jalali(inq.technicalApprovedAt) },
        ...(inq.needsManagement ? [{ title: 'مدیرعامل', name: inq.managementApprovedByName, date: jalali(inq.managementApprovedAt) }] : []),
      ]"
      distribution="توزیع نسخ: ۱- بازرگانی ۲- برنامه‌ریزی و کنترل پروژه ۳- مالی">
      <table>
        <thead>
          <tr>
            <th>ردیف</th>
            <th>شرح</th>
            <th>مقدار</th>
            <th v-for="q in inq.quotes" :key="q.id">{{ q.providerName }} ({{ supplierGrades[q.grade] }})</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in request.lines" :key="l.id">
            <td>{{ i + 1 }}</td>
            <td>{{ l.description }}</td>
            <td class="num">{{ num(l.quantity) }} {{ l.unit }}</td>
            <td v-for="q in inq.quotes" :key="q.id" class="num">{{ rial(q.lines.find((x: any) => x.requestLineId === l.id)?.unitPrice) }}</td>
          </tr>
        </tbody>
        <tfoot>
          <tr><td colspan="3">جمع (ریال)</td><td v-for="q in inq.quotes" :key="q.id" class="num">{{ rial(q.totalAmount) }}</td></tr>
          <tr><td colspan="3">تایید فنی / امتیاز</td><td v-for="q in inq.quotes" :key="q.id">{{ q.technicallyAccepted ? "تایید" : "رد" }} {{ q.technicalScore ?? "" }}
              <button v-if="canTechnical && inq.status === 0 && inq.id" class="btn btn-sm btn-outline-primary ms-1 d-print-none" @click="review = { quote: q, accepted: q.technicallyAccepted !== false, score: q.technicalScore ?? '', note: '' }">بررسی فنی</button></td></tr>
          <tr><td colspan="3">تحویل (روز) / پرداخت</td><td v-for="q in inq.quotes" :key="q.id">{{ q.deliveryDays ?? "" }} / {{ q.paymentTerms }}</td></tr>
        </tfoot>
      </table>
    </PrintForm>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">استعلام <span v-if="inq.number" class="inv-ltr ms-2">{{ inq.number }}</span></h1>
          <span class="inv-badge" :class="{ ok: inq.status === 2, warn: inq.status <= 1, bad: inq.status === 9 }">{{ inquiryStatuses[inq.status] }}</span>
          <span class="inv-muted ms-2">درخواست خرید <router-link :to="{ name: 'ProcurementRequestEdit', params: { id: request.id } }" class="inv-ltr">{{ request.number }}</router-link></span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'ProcurementInquiries' }" class="btn btn-sm btn-outline-secondary">فهرست</router-link>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ جدول مقایسه</button>
          <button v-if="canEdit" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
          <button v-if="inq.status === 1 && canCommercial" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">ابطال استعلام</button>
          <button v-if="inq.status === 2 && !inq.purchaseOrderId && canCommercial" :disabled="busy" class="btn btn-sm btn-success" @click="makeOrder">ساخت سفارش خرید</button>
          <router-link v-if="inq.purchaseOrderId" :to="{ name: 'ProcurementOrderEdit', params: { id: inq.purchaseOrderId } }" class="btn btn-sm btn-outline-primary">سفارش {{ inq.purchaseOrderNumber }}</router-link>
        </div>
      </div>
      <div class="row g-2">
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="inq.inquiryDate" :disabled="!canEdit" class="form-control form-control-sm" type="date" />
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">توضیح</label>
          <input v-model="inq.description" :disabled="!canEdit" class="form-control form-control-sm" />
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">دلیل تعداد کمتر از {{ inq.requiredQuotes }} پیشنهاد (تایید مدیرعامل لازم می‌شود)</label>
          <input v-model="inq.fewerQuotesReason" :disabled="!canEdit" class="form-control form-control-sm" />
        </div>
      </div>
      <p class="inv-muted small mt-2 mb-0">
        برای برآورد {{ rial(request.estimatedAmount) }} ریال، {{ inq.requiredQuotes }} پیشنهاد قابل قبول فنی لازم است. تامین‌کننده گرید D یا مسدود قابل انتخاب نیست؛
        گرید C فقط وقتی A یا B نباشد؛ تامین‌کننده ارزیابی‌نشده فقط تا سقف خرید تنخواه.
      </p>
    </div>

    <!-- quotes -->
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">پیشنهادهای قیمت (فی هر ردیف به ریال)</h2>
        <button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="quotes.push(newQuote())">+ پیشنهاد</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-bordered align-middle">
          <thead>
            <tr>
              <th style="min-width: 220px">تامین‌کننده</th>
              <th>شماره / تاریخ پیشنهاد</th>
              <th>تحویل (روز) / پرداخت / مالیات٪</th>
              <th>تایید فنی</th>
              <th v-for="(l, i) in request.lines" :key="l.id" style="min-width: 130px">فی ردیف {{ i + 1 }}<div class="inv-muted small">{{ l.description }} × {{ num(l.quantity) }}</div></th>
              <th>جمع</th>
              <th v-if="canEdit"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(q, qi) in quotes" :key="q.key" :class="{ 'table-success': q.id && q.id === inq.selectedQuoteId }">
              <td>
                <select v-if="canEdit" v-model="q.providerId" class="form-select form-select-sm">
                  <option :value="null">-</option>
                  <option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option>
                </select>
                <span v-else>{{ providerName(q.providerId) }}</span>
                <div v-if="q.id" class="small">
                  <span class="inv-badge" :class="{ bad: q.blocked || q.grade === 4, ok: q.grade === 1 || q.grade === 2, warn: q.grade === 3 || q.grade === 0 }">گرید {{ supplierGrades[q.grade] }}{{ q.blocked ? " — مسدود" : "" }}</span>
                  <span v-if="q.lowest" class="inv-badge ok ms-1">کمترین</span>
                </div>
              </td>
              <td>
                <input v-model="q.quoteNumber" :disabled="!canEdit" class="form-control form-control-sm inv-ltr mb-1" placeholder="شماره" />
                <input v-model="q.quoteDate" :disabled="!canEdit" class="form-control form-control-sm mb-1" type="date" />
                <input v-model="q.validUntil" :disabled="!canEdit" class="form-control form-control-sm" title="اعتبار تا" type="date" />
              </td>
              <td>
                <input v-model.number="q.deliveryDays" :disabled="!canEdit" class="form-control form-control-sm mb-1" placeholder="روز" type="number" />
                <input v-model="q.paymentTerms" :disabled="!canEdit" class="form-control form-control-sm mb-1" placeholder="شرایط پرداخت" />
                <input v-model.number="q.vatPercent" :disabled="!canEdit" class="form-control form-control-sm" placeholder="مالیات ٪" type="number" />
              </td>
              <td>
                <label class="form-check small"><input v-model="q.technicallyAccepted" :disabled="!canEdit" class="form-check-input" type="checkbox" /> قابل قبول</label>
                <input v-model.number="q.technicalScore" :disabled="!canEdit" class="form-control form-control-sm mb-1" placeholder="امتیاز" type="number" />
                <input v-model="q.technicalNote" :disabled="!canEdit" class="form-control form-control-sm mb-1" placeholder="یادداشت فنی" />
                <input v-if="canEdit" class="form-control form-control-sm" type="file" @change="attach(q, $event)" />
                <span v-if="q.documentId" class="inv-badge ok">فایل پیوست است</span>
              </td>
              <td v-for="l in request.lines" :key="l.id"><input v-model.number="q.prices[l.id]" :disabled="!canEdit" class="form-control form-control-sm" type="number" /></td>
              <td class="num">{{ rial(totalOf(q)) }}</td>
              <td v-if="canEdit"><button class="btn btn-sm btn-link text-danger p-0" @click="quotes.splice(qi, 1)">×</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="inq.status === 0 && !isNew && canCommercial" class="row g-2 align-items-end mt-1">
        <div class="col-md-3">
          <label class="form-label small mb-0">پیشنهاد برنده</label>
          <select v-model="chosen" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="q in quotes.filter((x) => x.id)" :key="q.id" :value="q.id">{{ providerName(q.providerId) }} — {{ rial(q.totalAmount) }}</option>
          </select>
        </div>
        <div class="col-md-7">
          <label class="form-label small mb-0">دلیل انتخاب (اجباری اگر کمترین قیمت قابل قبول نیست)</label>
          <input v-model="reason" class="form-control form-control-sm" />
        </div>
        <div class="col-md-2"><button :disabled="busy" class="btn btn-sm btn-primary w-100" @click="choose">ثبت انتخاب</button></div>
      </div>
      <p v-if="inq.selectionReason" class="small mt-2 mb-0">دلیل انتخاب: {{ inq.selectionReason }}</p>
    </div>

    <!-- PB6 (D5): planning approves the budget and the delivery time before a winner is selected -->
    <div v-if="!isNew && inq.planningRequired && (inq.status === 0 || inq.status === 5)" class="inv-card screen-only">
      <h2 class="inv-title mb-2">تایید برنامه‌ریزی (بودجه و زمان تحویل)</h2>
      <p v-if="inq.planningApprovedAt" class="small mb-1">تایید شد توسط {{ inq.planningApprovedByName }} — بودجه {{ rial(inq.planningBudgetAmount) }} ریال، زمان تحویل {{ inq.planningDeliveryDays ?? "-" }} روز <span v-if="inq.planningNote">({{ inq.planningNote }})</span></p>
      <p v-else class="inv-muted small">پیش از انتخاب برنده، برنامه‌ریزی باید بودجه و زمان تحویل را تایید کند (دست‌کم یک پیشنهاد باید تایید فنی داشته باشد). تاییدکننده نباید ثبت‌کننده استعلام یا درخواست باشد.</p>
      <div v-if="canPlanningStep" class="d-flex flex-wrap gap-2 align-items-center">
        <input v-if="!inq.planningApprovedAt" v-model.number="plan.amount" class="form-control form-control-sm w-auto inv-ltr" type="number" placeholder="بودجه تاییدی (ریال، خالی = بیشترین پیشنهاد فنی)" />
        <input v-if="!inq.planningApprovedAt" v-model.number="plan.days" class="form-control form-control-sm w-auto inv-ltr" type="number" placeholder="زمان تحویل (روز)" />
        <input v-model="plan.note" class="form-control form-control-sm w-auto" placeholder="توضیح / علت برگشت" />
        <button v-if="!inq.planningApprovedAt" :disabled="busy" class="btn btn-sm btn-outline-success" @click="planningStep('approve-planning')">تایید برنامه‌ریزی</button>
        <button :disabled="busy" class="btn btn-sm btn-outline-warning" @click="planningStep('return')">برگشت به بازرگانی</button>
      </div>
      <div v-if="inq.status === 5" class="alert alert-warning small mt-2 mb-0">برگشت خورد<span v-if="inq.returnReason">: {{ inq.returnReason }}</span>. بازرگانی نسخه جدید می‌سازد.
        <button :disabled="busy" class="btn btn-sm btn-outline-primary ms-2" @click="planningStep('revise')">ساخت نسخه جدید</button></div>
    </div>

    <!-- committee -->
    <div v-if="inq.status === 1 || inq.status === 2" class="inv-card screen-only">
      <h2 class="inv-title mb-2">تایید کمیته انتخاب تامین‌کننده</h2>
      <p class="inv-muted small">
        {{ inq.needsCommittee ? "برای این مبلغ تایید مالی، بازرگانی و فنی لازم است" : "برای این مبلغ تایید کمیته لازم نیست" }}{{ inq.needsManagement ? " و تایید مدیرعامل نیز لازم است." : "." }}
        هر تاییدکننده باید شخص متفاوتی باشد.
      </p>
      <div class="d-flex flex-wrap gap-2 align-items-center">
        <span class="inv-badge" :class="{ ok: inq.financialApprovedAt }">مالی (معاونت PMO): {{ inq.financialApprovedByName || "—" }}</span>
        <span class="inv-badge" :class="{ ok: inq.commercialApprovedAt }">بازرگانی: {{ inq.commercialApprovedByName || "—" }}</span>
        <span class="inv-badge" :class="{ ok: inq.technicalApprovedAt }">فنی: {{ inq.technicalApprovedByName || "—" }}</span>
        <span v-if="inq.needsManagement" class="inv-badge" :class="{ ok: inq.managementApprovedAt }">مدیرعامل: {{ inq.managementApprovedByName || "—" }}</span>
        <template v-if="inq.status === 1">
          <button v-if="!inq.financialApprovedAt && canFinancial" :disabled="busy" class="btn btn-sm btn-outline-success" @click="approve('approve-financial')">تایید مالی</button>
          <button v-if="!inq.commercialApprovedAt && canCommercial" :disabled="busy" class="btn btn-sm btn-outline-success" @click="approve('approve-commercial')">تایید بازرگانی</button>
          <button v-if="!inq.technicalApprovedAt && canTechnical" :disabled="busy" class="btn btn-sm btn-outline-success" @click="approve('approve-technical')">تایید فنی</button>
          <button v-if="inq.needsManagement && !inq.managementApprovedAt && canManagement" :disabled="busy" class="btn btn-sm btn-outline-success" @click="approve('approve-management')">تایید مدیرعامل</button>
        </template>
      </div>
      <p v-if="inq.committeeMinutes" class="small mt-2 mb-0">صورت‌جلسه کمیته: {{ inq.committeeMinutes }}</p>
    </div>

    <!-- notes of the AI seat PROC-COMPARE on this quotation comparison (a suggestion; the committee approvals above stay with the people) -->
    <AiNotesBox v-if="!isNew" :target-id="inq.id" target-type="PurchaseRfq" />

    <div v-if="minutes !== null" class="inv-modal-backdrop" @click.self="minutes = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">تایید مدیرعامل در کمیته</h2>
        <label class="form-label small mb-0">شماره / شرح صورت‌جلسه کمیته (اختیاری)</label>
        <input v-model="minutes" class="form-control form-control-sm" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="minutes = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="act(pendingAction, minutes || undefined), (minutes = null)">ثبت تایید</button>
        </div>
      </div>
    </div>
  </div>
  <div v-if="review" class="inv-modal-backdrop" @click.self="review = null">
    <div class="inv-modal" style="max-width: 460px">
      <h2 class="inv-title mb-2">بررسی فنی پیشنهاد {{ review.quote.providerName }}</h2>
      <label class="form-check d-block"><input v-model="review.accepted" class="form-check-input" type="checkbox" /> <span class="form-check-label">از نظر فنی قابل قبول است</span></label>
      <label class="form-label small mb-0 mt-2">امتیاز فنی (۰ تا ۱۰۰، اختیاری)</label>
      <input v-model="review.score" class="form-control form-control-sm inv-ltr" inputmode="decimal" />
      <label class="form-label small mb-0 mt-2">توضیح {{ review.accepted ? "" : "(الزامی)" }}</label>
      <textarea v-model="review.note" class="form-control form-control-sm" rows="3"></textarea>
      <p class="inv-muted small mt-2 mb-0">کارشناس خرید نمی‌تواند بررسی فنی را ثبت کند؛ فقط مهندسی یا کیفیت.</p>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="review = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="sendReview">ثبت</button></div>
    </div>
    <AttachmentPanel v-if="inq?.id" :owner-type="AttachmentOwner.Inquiry" :owner-id="inq.id" title="مدارک فنی درخواست و پیوست‌های استعلام" />
    <template v-if="inq?.id">
      <AttachmentPanel v-for="q in quotes.filter((x: any) => x.id)" :key="'q' + q.id" :owner-type="AttachmentOwner.Quote" :owner-id="q.id" :title="'پیوست‌های پیشنهاد ' + (q.quoteNumber || q.id)" collapsed />
    </template>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
