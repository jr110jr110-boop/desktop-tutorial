<script setup lang="ts">
import { computed, ref } from "vue";
import DeliveryDialog from "./DeliveryDialog.vue";
import OrderEditor from "./OrderEditor.vue";
import OrderPrint from "./OrderPrint.vue";
import { creditApproveSlOrder, draftSlInvoice, slOrderAction } from "@/api/SalesAPIs";
import { camelize, printSheet } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { InventoryRoles, SalesRoles, hasRole } from "@/composables/roles";
import { badgeOfSalesOrder, orderStatuses, persianOf, qtyText, rialText, salesActionTitles } from "@/composables/sales";

// Sales order detail: lines with ordered / reserved / delivered / invoiced quantities, actions the API accepts, credit gate, deliveries and drafted invoices, print.
const props = defineProps<{ order: any }>();
const emit = defineEmits<{ (e: "update", order: any): void; (e: "reload"): void }>();

const canWrite = hasRole(SalesRoles.writers);
/** The warehouse builds the delivery document (InventoryRoles.documents); QC approves the loading on the document page. */
const canBuildDocument = hasRole(InventoryRoles.documents);
const canCredit = hasRole(SalesRoles.creditApprovers);
const busy = ref(false);
const editing = ref(false);
const delivering = ref(false);
const reasonFor = ref<"cancel" | "close" | null>(null);
const reason = ref("");
const creditNote = ref<string | null>(null);

const o = computed(() => props.order);
const status = computed(() => o.value.status);
const creditApproved = computed(() => !!o.value.creditApprovedAt && Number(o.value.creditApprovedNetAmount) === Number(o.value.netAmount));
const waitingCredit = computed(() => status.value === 1 && o.value.creditApprovalRequired && !creditApproved.value);
const canDeliver = computed(() => status.value === 2 || status.value === 3);
const missingInvoice = computed(() => (o.value.deliveries || []).some((d: any) => !d.reversed && !d.invoiceId));
const liveDeliveries = computed(() => (o.value.deliveries || []).filter((d: any) => !d.reversed));

async function run(fn: () => Promise<any>, done: string | null) {
  busy.value = true;
  const [data, error] = await fn();
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  emit("update", camelize(data));
  if (done) ok(done);
  return true;
}

async function confirmOrder() {
  busy.value = true;
  const [data, error] = await slOrderAction({ id: o.value.id, action: "confirm", rowVersion: o.value.rowVersion });
  busy.value = false;
  if (error) {
    // over the credit limit: the server has already flagged the order and notified the finance manager, so show its reason and reload
    fail(error);
    emit("reload");
    return;
  }
  emit("update", camelize(data));
  ok("سفارش تایید شد و موجودی برای مشتری رزرو شد.");
}

function askReason(kind: "cancel" | "close") {
  reasonFor.value = kind;
  reason.value = "";
}
async function sendReason() {
  const kind = reasonFor.value!;
  if (!reason.value.trim() && (kind === "cancel" || status.value === 3)) return fail(kind === "cancel" ? "دلیل لغو سفارش را بنویسید." : "برای بستن سفارش با تحویل ناقص، دلیل را بنویسید.");
  if (await run(() => slOrderAction({ id: o.value.id, action: kind, reason: reason.value.trim() || null, rowVersion: o.value.rowVersion }), kind === "cancel" ? "سفارش لغو شد." : "سفارش بسته شد.")) reasonFor.value = null;
}

async function approveCredit() {
  await run(() => creditApproveSlOrder({ id: o.value.id, note: creditNote.value, rowVersion: o.value.rowVersion }), "عبور از سقف اعتبار تایید شد؛ اکنون فروش می‌تواند سفارش را تایید کند.");
}

async function draftInvoices() {
  await run(() => draftSlInvoice({ orderId: o.value.id }), "پیش‌نویس فاکتورهای جاافتاده ساخته شد.");
}

function onSaved(order: any) {
  editing.value = false;
  emit("update", order);
}
</script>

<template>
  <OrderEditor v-if="editing" :order="o" @saved="onSaved" @close="editing = false" />

  <div v-else class="inv-card screen-only">
    <div class="inv-toolbar">
      <h1 class="inv-title">سفارش فروش <span class="inv-ltr">{{ o.number }}</span></h1>
      <span :class="badgeOfSalesOrder(status)" class="inv-badge">{{ orderStatuses[status] }}</span>
      <span v-if="o.creditApprovalRequired" :class="creditApproved ? 'ok' : 'bad'" class="inv-badge">{{ creditApproved ? "سقف اعتبار تایید شده" : "منتظر تایید سقف اعتبار" }}</span>
      <div class="ms-auto d-flex gap-2">
        <button class="btn btn-sm btn-outline-secondary" @click="printSheet">چاپ سفارش</button>
        <router-link class="btn btn-sm btn-outline-secondary" :to="{ name: 'SlOrders' }">بازگشت به فهرست</router-link>
      </div>
    </div>

    <div class="row g-2 mb-3 small">
      <div class="col-md-3"><b>مشتری:</b> {{ o.customerName }}</div>
      <div class="col-md-3"><b>تاریخ سفارش:</b> {{ persianOf(o.orderDateText, o.orderDate) }}</div>
      <div class="col-md-3"><b>شماره سفارش مشتری:</b> <span class="inv-ltr">{{ o.customerOrderNumber || "-" }}</span></div>
      <div class="col-md-3"><b>تحویل درخواستی:</b> {{ persianOf(o.requestedDeliveryDateText, o.requestedDeliveryDate) || "-" }}</div>
      <div class="col-md-3"><b>مبلغ خالص (ریال):</b> {{ rialText(o.netAmount) }}</div>
      <div v-if="o.confirmedAt" class="col-md-3"><b>تایید سفارش:</b> {{ persianOf(o.confirmedAtText, o.confirmedAt, true) }}</div>
      <div v-if="o.quotationId" class="col-md-3"><b>پیش‌فاکتور:</b> <router-link :to="{ name: 'SlQuotations' }">مشاهده پیش‌فاکتورها</router-link></div>
      <div v-if="o.cancelReason" class="col-12"><b>دلیل لغو یا بستن:</b> {{ o.cancelReason }}</div>
    </div>

    <div v-if="o.creditApprovalRequired" :class="creditApproved ? 'alert-success' : 'alert-warning'" class="alert small py-2">
      <div v-if="creditApproved">عبور از سقف اعتبار تایید شد ({{ persianOf(o.creditApprovedAtText, o.creditApprovedAt, true) }}).<span v-if="o.creditApprovalNote"> توضیح مدیر مالی: {{ o.creditApprovalNote }}</span></div>
      <template v-else>
        <div>{{ o.creditReason }}</div>
        <div v-if="o.creditExcess != null">مبلغ عبور از سقف (ریال): <b>{{ rialText(o.creditExcess) }}</b></div>
        <div>موضوع به مدیر مالی اعلام شده است. پس از تایید او، سفارش را دوباره تایید کنید.</div>
        <div v-if="canCredit && status === 1" class="d-flex flex-wrap gap-2 mt-2 align-items-center">
          <input v-model="creditNote" class="form-control form-control-sm" style="max-width: 320px" placeholder="توضیح (اختیاری)" />
          <button v-if="!o.touchedByMe" :disabled="busy" class="btn btn-sm btn-success" @click="approveCredit">تایید سقف اعتبار</button>
          <span v-else class="inv-muted">شما این مورد را ثبت یا ویرایش کرده‌اید؛ تأیید با کاربر دیگر است</span>
        </div>
      </template>
    </div>

    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>#</th><th>کالا</th><th>تعداد سفارش</th><th>رزرو</th><th>تحویل‌شده</th><th>فاکتورشده</th><th>قیمت واحد (ریال)</th><th>تخفیف (٪)</th><th>مبلغ (ریال)</th></tr></thead>
        <tbody>
          <tr v-for="l in o.lines" :key="l.id">
            <td>{{ l.rowNumber }}</td>
            <td><span class="inv-ltr">{{ l.itemCode }}</span> {{ l.itemName }}<div v-if="l.description" class="small inv-muted">{{ l.description }}</div></td>
            <td>{{ qtyText(l.quantity) }} {{ l.unit }}</td>
            <td>{{ qtyText(l.reservedQuantity) }}</td>
            <td>{{ qtyText(l.deliveredQuantity) }}</td>
            <td>{{ qtyText(l.invoicedQuantity) }}</td>
            <td>{{ rialText(l.unitPrice) }}</td>
            <td>{{ qtyText(l.discountPercent) }}</td>
            <td>{{ rialText(l.amount) }}</td>
          </tr>
        </tbody>
        <tfoot><tr><th colspan="8" class="text-end">جمع خالص (ریال)</th><th>{{ rialText(o.netAmount) }}</th></tr></tfoot>
      </table>
    </div>

    <div v-if="canWrite" class="d-flex flex-wrap gap-2 mb-3">
      <button v-if="status === 1" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="editing = true">ویرایش</button>
      <button v-if="status === 1" :disabled="busy" class="btn btn-sm btn-success" @click="confirmOrder">{{ salesActionTitles.confirm }}</button>
      <button v-if="status === 1 || status === 2" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="askReason('cancel')">{{ salesActionTitles.cancel }}</button>
      <button v-if="status === 3 || status === 4 || status === 5" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="askReason('close')">{{ salesActionTitles.close }}</button>
    </div>

    <div v-if="reasonFor" class="alert alert-secondary py-2 d-flex flex-wrap gap-2 align-items-center">
      <label class="small mb-0">{{ reasonFor === "cancel" ? "دلیل لغو" : status === 3 ? "دلیل بستن با تحویل ناقص" : "توضیح بستن (اختیاری)" }}</label>
      <input v-model="reason" class="form-control form-control-sm" style="max-width: 380px" />
      <button :disabled="busy" class="btn btn-sm btn-primary" @click="sendReason">ثبت</button>
      <button class="btn btn-sm btn-outline-secondary" @click="reasonFor = null">انصراف</button>
    </div>

    <h2 class="h6 mt-3">تحویل‌ها و پیش‌نویس فاکتورها</h2>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>سند تحویل</th><th>وضعیت</th><th>پیش‌نویس فاکتور</th><th>توضیح</th></tr></thead>
        <tbody>
          <tr v-if="!o.deliveries?.length"><td colspan="4" class="text-center inv-muted">هنوز تحویلی تایید نشده است.</td></tr>
          <tr v-for="d in o.deliveries" :key="d.id">
            <td><router-link :to="{ name: 'InventoryDocumentEdit', params: { id: d.documentId } }" class="inv-ltr">{{ d.documentNumber }}</router-link></td>
            <td><span :class="d.reversed ? 'bad' : 'ok'" class="inv-badge">{{ d.reversed ? "باطل‌شده" : "تایید‌شده" }}</span></td>
            <td>
              <router-link v-if="d.invoiceId" :to="{ name: 'FnInvoices' }" class="inv-ltr">{{ d.invoiceNumber || "فاکتور" }}</router-link>
              <span v-else-if="d.reversed" class="inv-muted">-</span>
              <span v-else class="inv-badge warn">ساخته نشده</span>
            </td>
            <td class="small">{{ d.draftNote }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <p class="small inv-muted">تایید فاکتور پیش‌نویس با مالی است و در صفحهٔ فاکتورهای فروش انجام می‌شود.</p>
    <div class="d-flex flex-wrap gap-2">
      <button v-if="canDeliver && canBuildDocument" class="btn btn-sm btn-primary" @click="delivering = true">ساخت سند تحویل به مشتری</button>
      <span v-else-if="canDeliver && canWrite" class="small inv-muted">سند تحویل را انبار در همین صفحه می‌سازد؛ تأیید بارگیری با کنترل کیفیت است.</span>
      <button v-if="canWrite && missingInvoice" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="draftInvoices">پیش‌نویس فاکتورهای جاافتاده</button>
    </div>
    <p v-if="liveDeliveries.length && status === 3" class="small inv-muted mt-2">تحویل جزئی انجام شده است؛ برای مانده می‌توان سند تحویل دیگری ساخت.</p>

    <DeliveryDialog v-if="delivering" :order="o" @close="delivering = false" @changed="emit('reload')" />
  </div>

  <OrderPrint :order="o" />
</template>
