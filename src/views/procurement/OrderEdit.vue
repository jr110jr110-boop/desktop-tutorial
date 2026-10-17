<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import PrintForm from "@/components/print/PrintForm.vue";
import { getContracts, getProviders, getWorkOrders, rowsOf, uploadFile } from "@/api/InventoryAPIs";
import {
  acceptService, getPurchaseOrder, getPurchaseRequest, getPurchaseRequests, purchaseOrderAction, savePurchaseOrder,
} from "@/api/ProcurementAPIs";
import { fail, jalali, num, ok, printPage, rial, today } from "@/composables/inventory";
import { orderStatuses, purchaseKinds } from "@/composables/procurement";
import { hasRole, ProcurementRoles } from "@/composables/roles";

// Purchase order: made by commerce from an approved request / inquiry, approved by the PMO deputy (another person), released to the supplier.
const route = useRoute();
const router = useRouter();
const order = ref<any>(null);
const providers = ref<any[]>([]);
const workOrders = ref<any[]>([]);
const contracts = ref<any[]>([]);
const requests = ref<any[]>([]);
const busy = ref(false);
const cancelNote = ref<string | null>(null);
const acceptance = ref<any>(null);
const printMode = ref<"order" | "service">("order");

const isNew = computed(() => !order.value?.id);
const isDraft = computed(() => !order.value?.id || order.value.status === 0);
const canCommercial = hasRole(ProcurementRoles.commercial);
const canEdit = computed(() => isDraft.value && canCommercial);
const canApprove = hasRole(ProcurementRoles.orderApproval);
const canAccept = hasRole(ProcurementRoles.serviceAcceptance);
const rate = computed(() => (order.value?.currency ? Number(order.value.exchangeRate) || 1 : 1));
const totalRial = computed(() => (order.value?.lines || []).reduce((s: number, l: any) => s + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0), 0) * rate.value);
const orderable = computed(() => requests.value);

function blank() {
  return {
    id: null, status: 0, kind: 1, orderDate: today(), providerId: null, inquiryId: null, workOrderId: null, contractId: null, currency: "", exchangeRate: null,
    incoterms: "", paymentTerms: "", vatPercent: 10, advancePercent: 0, advanceGuarantee: "", advanceGuaranteeExpiry: "", performanceGuarantee: "",
    retentionPercent: null, contractNumber: "", contractSignedAt: "", requirements: "", deliveryDate: "", deliveryPlace: "", latePenaltyPercentPerWeek: null,
    description: "", lines: [] as any[],
  };
}

function load(dto: any) {
  // the API keeps prices in rial; the form shows them in the currency of the order
  const r = dto.currency && dto.exchangeRate ? Number(dto.exchangeRate) : 1;
  order.value = {
    ...dto,
    orderDate: (dto.orderDate || "").slice(0, 10),
    currency: dto.currency || "",
    deliveryDate: dto.deliveryDate ? dto.deliveryDate.slice(0, 10) : "",
    advanceGuaranteeExpiry: dto.advanceGuaranteeExpiry ? dto.advanceGuaranteeExpiry.slice(0, 10) : "",
    contractSignedAt: dto.contractSignedAt ? dto.contractSignedAt.slice(0, 10) : "",
    lines: dto.lines.map((l: any) => ({ ...l, unitPrice: Math.round((l.unitPrice / r) * 10000) / 10000, deliveryDate: l.deliveryDate ? l.deliveryDate.slice(0, 10) : "" })),
  };
}

async function addRequest(requestId: number) {
  const [r, error] = await getPurchaseRequest(requestId);
  if (error) return fail(error);
  if (r.status !== 2 && r.status !== 3) return fail("درخواست خرید تایید نشده است.");
  order.value.kind = r.kind;
  order.value.workOrderId = order.value.workOrderId || r.workOrderId;
  order.value.contractId = order.value.contractId || r.contractId;
  r.lines
    .filter((l: any) => l.quantity - l.orderedQuantity > 0)
    .forEach((l: any) =>
      order.value.lines.push({
        requestLineId: l.id, requestNumber: r.number, itemId: l.itemId, itemCode: l.itemCode, itemTitle: l.itemTitle, description: l.description, unit: l.unit,
        quantity: l.quantity - l.orderedQuantity, unitPrice: l.estimatedUnitPrice, deliveryDate: l.neededBy ? l.neededBy.slice(0, 10) : "", workOrderId: null, contractId: null,
      })
    );
  if (!order.value.lines.length) fail("همه ردیف‌های این درخواست سفارش داده شده است.");
}

async function save(silent = false): Promise<boolean> {
  const o = order.value;
  if (!o.providerId) {
    fail("تامین‌کننده را انتخاب کنید.");
    return false;
  }
  busy.value = true;
  const [data, error] = await savePurchaseOrder({
    id: o.id, kind: o.kind, orderDate: o.orderDate, providerId: o.providerId, inquiryId: o.inquiryId, workOrderId: o.workOrderId, contractId: o.contractId,
    currency: o.currency || null, exchangeRate: o.currency ? o.exchangeRate : null, incoterms: o.incoterms, paymentTerms: o.paymentTerms, vatPercent: o.vatPercent,
    advancePercent: o.advancePercent, advanceGuarantee: o.advanceGuarantee, advanceGuaranteeExpiry: o.advanceGuaranteeExpiry || null,
    performanceGuarantee: o.performanceGuarantee, retentionPercent: o.retentionPercent, contractNumber: o.contractNumber, contractSignedAt: o.contractSignedAt || null,
    requirements: o.requirements, deliveryDate: o.deliveryDate || null, deliveryPlace: o.deliveryPlace, latePenaltyPercentPerWeek: o.latePenaltyPercentPerWeek,
    description: o.description,
    lines: o.lines.map((l: any) => ({
      requestLineId: l.requestLineId, itemId: l.itemId, description: l.description, unit: l.unit, quantity: Number(l.quantity) || 0,
      unitPrice: Number(l.unitPrice) || 0, deliveryDate: l.deliveryDate || null, workOrderId: l.workOrderId, contractId: l.contractId,
    })),
  });
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  load(data);
  if (!silent) ok("ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "ProcurementOrderEdit", params: { id: data.id } });
  return true;
}

async function act(action: string, note?: string) {
  if (action === "approve" && isDraft.value && canCommercial && !(await save(true))) return;
  busy.value = true;
  const [data, error] = await purchaseOrderAction(order.value.id, action, note);
  busy.value = false;
  if (error) return fail(error);
  load(data);
  cancelNote.value = null;
  ok("ثبت شد.");
}

function startAcceptance(line: any) {
  acceptance.value = { line, purchaseOrderLineId: line.id, acceptanceDate: today(), quantity: line.openQuantity, notes: "", documentId: null };
}

async function attach(event: any) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  const [id, error] = await uploadFile(file, "تایید انجام خدمت " + order.value.number);
  if (error) return fail(error);
  acceptance.value.documentId = id;
  ok("فایل بارگذاری شد.");
}

async function submitAcceptance() {
  const a = acceptance.value;
  busy.value = true;
  const [data, error] = await acceptService({ purchaseOrderLineId: a.purchaseOrderLineId, acceptanceDate: a.acceptanceDate, quantity: Number(a.quantity), notes: a.notes, documentId: a.documentId });
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok("تایید انجام خدمت ثبت شد.");
  a.saved = true;
}

async function printService() {
  printMode.value = "service";
  await nextTick();
  printPage();
}

async function printOrder() {
  printMode.value = "order";
  await nextTick();
  printPage();
}

onMounted(async () => {
  getProviders().then(([l]) => (providers.value = rowsOf(l)));
  getWorkOrders().then(([l]) => (workOrders.value = rowsOf(l)));
  getContracts().then(([l]) => (contracts.value = rowsOf(l)));
  if (route.params.id === "new") {
    order.value = blank();
    const [a] = await getPurchaseRequests({ Status: 2, PageSize: 100 });
    const [b] = await getPurchaseRequests({ Status: 3, PageSize: 100 });
    requests.value = [...rowsOf(a), ...rowsOf(b)];
    if (route.query.request) await addRequest(Number(route.query.request));
  } else {
    const [data, error] = await getPurchaseOrder(Number(route.params.id));
    if (error) return fail(error);
    load(data);
  }
});
</script>

<template>
  <div v-if="order" class="inv-page">
    <!-- purchase order: prepared by commerce, approved by the PMO deputy (no company form code yet) -->
    <PrintForm
      v-if="printMode === 'order' && !isNew"
      title="سفارش خرید"
      :fields="[
        { label: 'شماره', value: order.number, ltr: true },
        { label: 'تاریخ', value: jalali(order.orderDate) },
        { label: 'تامین‌کننده', value: order.providerName },
        { label: 'نوع', value: purchaseKinds[order.kind] },
        { label: 'استعلام', value: order.inquiryNumber || '-', ltr: true },
        { label: 'دستور ساخت', value: order.workOrderCode || '-' },
        { label: 'پروژه / قرارداد', value: order.contractCode || '-' },
        { label: 'ارز', value: order.currency ? order.currency + ' (نرخ ' + order.exchangeRate + ')' : 'ریال' },
        { label: 'شرایط پرداخت', value: order.paymentTerms || '-' },
        { label: 'شرایط تحویل (Incoterms)', value: order.incoterms || '-' },
        { label: 'تاریخ تحویل', value: jalali(order.deliveryDate) || '-' },
        { label: 'محل تحویل', value: order.deliveryPlace || '-' },
        { label: 'پیش‌پرداخت ٪', value: order.advancePercent ?? 0 },
        { label: 'ضمانت‌نامه پیش‌پرداخت', value: order.advanceGuarantee || '-' },
        { label: 'ضمانت حسن انجام کار', value: order.performanceGuarantee || '-' },
        { label: 'حسن انجام کار (ضمانت نگهداری) ٪', value: order.retentionPercent ?? '-' },
        { label: 'جریمه تاخیر هفتگی ٪', value: order.latePenaltyPercentPerWeek ?? '-' },
        { label: 'شماره قرارداد', value: order.contractNumber || '-' },
      ]"
      :notes="[
        ...(order.requirements ? [{ label: 'الزامات فنی و کیفی', value: order.requirements }] : []),
        ...(order.description ? [{ label: 'توضیح', value: order.description }] : []),
      ]"
      :signatures="[
        { title: 'تهیه‌کننده (بازرگانی)', name: order.creatorName, date: jalali(order.createDate) },
        { title: 'تاییدکننده (معاونت PMO)', name: order.approvedByName, date: jalali(order.approvedAt) },
      ]"
      distribution="توزیع نسخ: ۱- بازرگانی ۲- انبار ۳- مالی ۴- برنامه‌ریزی و کنترل پروژه">
      <table>
        <thead>
          <tr><th>ردیف</th><th>کد کالا</th><th>شرح</th><th>واحد</th><th>مقدار</th><th>فی (ریال)</th><th>مبلغ (ریال)</th><th>تحویل</th></tr>
        </thead>
        <tbody>
          <tr v-for="l in order.lines" :key="l.id">
            <td>{{ l.rowNumber }}</td>
            <td class="inv-ltr">{{ l.itemCode }}</td>
            <td>{{ l.itemTitle || l.description }}</td>
            <td>{{ l.unit }}</td>
            <td class="num">{{ num(l.quantity) }}</td>
            <td class="num">{{ rial(l.unitPrice) }}</td>
            <td class="num">{{ rial(l.amount) }}</td>
            <td>{{ jalali(l.deliveryDate) }}</td>
          </tr>
        </tbody>
        <tfoot>
          <tr><td colspan="6">جمع</td><td class="num">{{ rial(order.totalAmount) }}</td><td></td></tr>
          <tr v-if="order.vatAmount"><td colspan="6">مالیات بر ارزش افزوده</td><td class="num">{{ rial(order.vatAmount) }}</td><td></td></tr>
        </tfoot>
      </table>
    </PrintForm>

    <!-- service acceptance sheet -->
    <PrintForm
      v-if="printMode === 'service' && acceptance"
      title="برگه تایید انجام خدمت / کار پیمانکاری"
      :fields="[
        { label: 'سفارش خرید', value: order.number, ltr: true },
        { label: 'تامین‌کننده / پیمانکار', value: order.providerName },
        { label: 'تاریخ', value: jalali(acceptance.acceptanceDate) },
        { label: 'ردیف سفارش', value: acceptance.line.rowNumber },
        { label: 'شرح', value: acceptance.line.description || acceptance.line.itemTitle },
        { label: 'مقدار تاییدشده', value: num(acceptance.quantity) + ' ' + (acceptance.line.unit || '') },
        { label: 'مقدار سفارش', value: num(acceptance.line.quantity) },
        { label: 'پروژه / دستور ساخت', value: (order.contractCode || '') + ' ' + (order.workOrderCode || '') },
      ]"
      :notes="acceptance.notes ? [{ label: 'توضیح', value: acceptance.notes }] : []"
      :signatures="[{ title: 'تایید کننده (کنترل کیفیت / واحد درخواست‌کننده)' }, { title: 'پیمانکار / تامین‌کننده' }, { title: 'بازرگانی' }]"
      distribution="توزیع نسخ: ۱- بازرگانی ۲- مالی ۳- برنامه‌ریزی و کنترل پروژه" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">سفارش خرید <span v-if="order.number" class="inv-ltr ms-2">{{ order.number }}</span></h1>
          <span class="inv-badge" :class="{ ok: order.status >= 1 && order.status <= 5, warn: order.status === 0, bad: order.status === 9 }">{{ orderStatuses[order.status] }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'ProcurementOrders' }" class="btn btn-sm btn-outline-secondary">فهرست</router-link>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="printOrder">چاپ سفارش</button>
          <button v-if="canEdit" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save()">ذخیره پیش‌نویس</button>
          <button v-if="order.status === 0 && !isNew && canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید (معاونت PMO)</button>
          <button v-if="order.status === 1 && canCommercial" :disabled="busy" class="btn btn-sm btn-success" @click="act('release')">ابلاغ به تامین‌کننده</button>
          <button v-if="[2, 3, 4].includes(order.status) && canCommercial" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act('close')">بستن سفارش</button>
          <button v-if="[0, 1, 2].includes(order.status) && !isNew && (canCommercial || canApprove)" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="cancelNote = ''">ابطال</button>
          <router-link v-if="[2, 3, 4].includes(order.status) && order.kind === 1" :to="{ name: 'InventoryDocumentEdit', params: { id: 'new' }, query: { type: 1, order: order.id } }" class="btn btn-sm btn-outline-primary">رسید انبار</router-link>
        </div>
      </div>
      <div v-if="order.status === 9" class="alert alert-danger small py-2">ابطال شد: {{ order.cancelReason }}</div>
      <div v-if="!isNew" class="d-flex flex-wrap gap-2 mb-2">
        <span class="inv-badge">جمع سفارش: {{ rial(order.totalAmount) }}</span>
        <span class="inv-badge">مالیات: {{ rial(order.vatAmount) }}</span>
        <span class="inv-badge">دریافت‌شده: {{ rial(order.receivedAmount) }}</span>
        <span class="inv-badge">فاکتور شده: {{ rial(order.invoicedAmount) }}</span>
        <span class="inv-badge">پرداخت شده: {{ rial(order.paidAmount) }}</span>
        <span v-if="order.approvedByName" class="inv-badge ok">تایید: {{ order.approvedByName }}</span>
        <router-link v-if="order.inquiryId" :to="{ name: 'ProcurementInquiryEdit', params: { id: order.inquiryId } }" class="small">استعلام {{ order.inquiryNumber }}</router-link>
      </div>

      <div v-if="isNew" class="row g-2 mb-2">
        <div class="col-md-6">
          <label class="form-label small mb-0">افزودن ردیف از درخواست خرید تاییدشده</label>
          <select class="form-select form-select-sm" @change="addRequest(Number(($event.target as HTMLSelectElement).value)), (($event.target as HTMLSelectElement).value = '')">
            <option value="">-</option>
            <option v-for="r in orderable" :key="r.id" :value="r.id">{{ r.number }} — {{ rial(r.estimatedAmount) }}</option>
          </select>
          <p class="inv-muted small mb-0">سفارش بالاتر از سقف استعلام باید از صفحه استعلام تاییدشده ساخته شود.</p>
        </div>
      </div>

      <div class="row g-2">
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="order.orderDate" :disabled="!canEdit" class="form-control form-control-sm" type="date" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">نوع</label>
          <select v-model="order.kind" :disabled="!canEdit" class="form-select form-select-sm">
            <option v-for="(t, k) in purchaseKinds" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">تامین‌کننده *</label>
          <select v-model="order.providerId" :disabled="!canEdit || !!order.inquiryId" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">دستور ساخت</label>
          <select v-model="order.workOrderId" :disabled="!canEdit" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">قرارداد / پروژه</label>
          <select v-model="order.contractId" :disabled="!canEdit" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="c in contracts" :key="c.id" :value="c.id">{{ c.code }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">ارز (خالی = ریال)</label>
          <input v-model="order.currency" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" placeholder="USD / EUR" />
        </div>
        <div v-if="order.currency" class="col-md-2">
          <label class="form-label small mb-0">نرخ تبدیل به ریال *</label>
          <input v-model.number="order.exchangeRate" :disabled="!canEdit" class="form-control form-control-sm" type="number" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">مالیات ٪</label>
          <input v-model.number="order.vatPercent" :disabled="!canEdit" class="form-control form-control-sm" type="number" />
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">شرایط پرداخت</label>
          <input v-model="order.paymentTerms" :disabled="!canEdit" class="form-control form-control-sm" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">Incoterms</label>
          <input v-model="order.incoterms" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ تحویل</label>
          <input v-model="order.deliveryDate" :disabled="!canEdit" class="form-control form-control-sm" type="date" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">محل تحویل</label>
          <input v-model="order.deliveryPlace" :disabled="!canEdit" class="form-control form-control-sm" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">پیش‌پرداخت ٪</label>
          <input v-model.number="order.advancePercent" :disabled="!canEdit" class="form-control form-control-sm" type="number" />
        </div>
        <template v-if="order.advancePercent > 0">
          <div class="col-md-4">
            <label class="form-label small mb-0">ضمانت‌نامه پیش‌پرداخت *</label>
            <input v-model="order.advanceGuarantee" :disabled="!canEdit" class="form-control form-control-sm" />
          </div>
          <div class="col-md-2">
            <label class="form-label small mb-0">اعتبار ضمانت‌نامه</label>
            <input v-model="order.advanceGuaranteeExpiry" :disabled="!canEdit" class="form-control form-control-sm" type="date" />
          </div>
        </template>
        <div class="col-md-3">
          <label class="form-label small mb-0">ضمانت حسن انجام کار</label>
          <input v-model="order.performanceGuarantee" :disabled="!canEdit" class="form-control form-control-sm" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">حسن انجام کار (نگهداشت) ٪</label>
          <input v-model.number="order.retentionPercent" :disabled="!canEdit" class="form-control form-control-sm" type="number" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">جریمه تاخیر هفتگی ٪</label>
          <input v-model.number="order.latePenaltyPercentPerWeek" :disabled="!canEdit" class="form-control form-control-sm" type="number" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">شماره قرارداد (از سقف قرارداد کتبی)</label>
          <input v-model="order.contractNumber" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ امضای قرارداد</label>
          <input v-model="order.contractSignedAt" :disabled="!canEdit" class="form-control form-control-sm" type="date" />
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">الزامات فنی و کیفی (مشخصات، گواهی مواد، بازرسی) {{ order.kind !== 2 ? "*" : "" }}</label>
          <textarea v-model="order.requirements" :disabled="!canEdit" class="form-control form-control-sm" rows="2"></textarea>
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">توضیح</label>
          <textarea v-model="order.description" :disabled="!canEdit" class="form-control form-control-sm" rows="2"></textarea>
        </div>
      </div>
    </div>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">ردیف‌ها <span class="inv-muted">جمع: {{ rial(totalRial) }} ریال</span></h2>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-bordered">
          <thead>
            <tr>
              <th>#</th><th>درخواست</th><th>کالا / شرح</th><th>واحد</th><th style="width: 7rem">مقدار</th>
              <th style="width: 9rem">فی{{ order.currency ? " (" + order.currency + ")" : " (ریال)" }}</th><th style="width: 9rem">تحویل</th>
              <th v-if="order.id">دریافت / تایید</th><th v-if="order.id">باز</th><th v-if="canEdit"></th><th v-if="order.id"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="!order.lines.length"><td colspan="11" class="text-center inv-muted">ردیفی نیست.</td></tr>
            <tr v-for="(l, i) in order.lines" :key="l.id || i">
              <td>{{ i + 1 }}</td>
              <td class="inv-ltr">{{ l.requestNumber }}</td>
              <td><span class="inv-ltr">{{ l.itemCode }}</span> {{ l.itemTitle || l.description }}</td>
              <td>{{ l.unit }}</td>
              <td><input v-model.number="l.quantity" :disabled="!canEdit" class="form-control form-control-sm" step="any" type="number" /></td>
              <td><input v-model.number="l.unitPrice" :disabled="!canEdit" class="form-control form-control-sm" type="number" /></td>
              <td><input v-model="l.deliveryDate" :disabled="!canEdit" class="form-control form-control-sm" type="date" /></td>
              <td v-if="order.id" class="num">{{ num(l.receivedQuantity) }} / {{ num(l.acceptedQuantity) }}<span v-if="l.late" class="inv-badge bad ms-1">دیرکرد</span></td>
              <td v-if="order.id" class="num">{{ num(l.openQuantity) }}</td>
              <td v-if="canEdit"><button class="btn btn-sm btn-link text-danger p-0" @click="order.lines.splice(i, 1)">×</button></td>
              <td v-if="order.id">
                <button
                  v-if="!l.itemId && [2, 3].includes(order.status) && canAccept && l.openQuantity > 0"
                  class="btn btn-sm btn-outline-primary text-nowrap"
                  @click="startAcceptance(l)">
                  تایید انجام خدمت
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="order.id && order.status <= 1" class="inv-muted small mb-0">تاییدکننده سفارش باید غیر از تهیه‌کننده باشد. سفارش ارزی و پیش‌پرداخت بیش از حد مجاز تایید مدیرعامل می‌خواهد.</p>
    </div>

    <div v-if="cancelNote !== null" class="inv-modal-backdrop" @click.self="cancelNote = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ابطال سفارش {{ order.number }}</h2>
        <textarea v-model="cancelNote" class="form-control form-control-sm" placeholder="علت ابطال *" rows="3"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="cancelNote = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-danger" @click="act('cancel', cancelNote || '')">ابطال سفارش</button>
        </div>
      </div>
    </div>

    <div v-if="acceptance" class="inv-modal-backdrop" @click.self="acceptance = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">تایید انجام خدمت — ردیف {{ acceptance.line.rowNumber }}</h2>
        <p class="small">{{ acceptance.line.description }} — مانده {{ num(acceptance.line.openQuantity) }} {{ acceptance.line.unit }}. تایید کننده باید غیر از خریدار و ابلاغ‌کننده سفارش باشد.</p>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">تاریخ</label><input v-model="acceptance.acceptanceDate" class="form-control form-control-sm" type="date" /></div>
          <div class="col-6"><label class="form-label small mb-0">مقدار تاییدشده</label><input v-model.number="acceptance.quantity" class="form-control form-control-sm" step="any" type="number" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="acceptance.notes" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">فایل گزارش / صورت‌وضعیت</label><input class="form-control form-control-sm" type="file" @change="attach" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="acceptance = null">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printService">چاپ برگه</button>
          <button :disabled="busy || acceptance.saved" class="btn btn-sm btn-primary" @click="submitAcceptance">ثبت تایید</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
