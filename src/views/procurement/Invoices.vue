<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import PrintForm from "@/components/print/PrintForm.vue";
import { uploadFile } from "@/api/InventoryAPIs";
import { getPurchaseOrder, getPurchaseOrders, getSupplierInvoice, getSupplierInvoices, invoiceAction, saveSupplierInvoice } from "@/api/ProcurementAPIs";
import { fail, jalali, num, ok, printPage, rial, today } from "@/composables/inventory";
import { invoiceStatuses } from "@/composables/procurement";
import { hasRole, ProcurementRoles } from "@/composables/roles";

// Supplier invoices with the three-way match (order, QC-accepted receipt, invoice).
const route = useRoute();
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const filters = ref({ Status: null as number | null, Contains: "" });
const orders = ref<any[]>([]);
const form = ref<any>(null);
const detail = ref<any>(null);
const cancelNote = ref<string | null>(null);
const busy = ref(false);
const canSave = computed(() => hasRole(ProcurementRoles.finance) || hasRole(ProcurementRoles.commercial));
const canApproveMatched = hasRole(ProcurementRoles.finance);
const canApproveVariance = hasRole(ProcurementRoles.orderApproval);
const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

async function load() {
  const [data, error] = await getSupplierInvoices({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

function search() {
  page.value = 1;
  load();
}

async function newInvoice() {
  const [data] = await getPurchaseOrders({ PageSize: 200 });
  orders.value = (data?.data || []).filter((o: any) => o.status >= 2 && o.status <= 5);
  form.value = { id: null, purchaseOrderId: null, invoiceNumber: "", invoiceDate: today(), isOfficial: true, vatAmount: null, documentId: null, lines: [], order: null };
}

async function pickOrder() {
  const f = form.value;
  f.lines = [];
  if (!f.purchaseOrderId) return;
  const [order, error] = await getPurchaseOrder(f.purchaseOrderId);
  if (error) return fail(error);
  f.order = order;
  // what QC accepted and was not invoiced yet, at the order price
  f.lines = order.lines
    .map((l: any) => ({ orderLineId: l.id, label: (l.itemCode || "") + " " + (l.itemTitle || l.description || ""), accepted: l.acceptedQuantity, invoiced: l.invoicedQuantity, quantity: Math.max(0, l.acceptedQuantity - l.invoicedQuantity), unitPrice: l.unitPrice, orderPrice: l.unitPrice }))
    .filter((l: any) => l.quantity > 0 || l.accepted > 0);
  if (order.vatPercent) f.vatAmount = Math.round(f.lines.reduce((s: number, l: any) => s + l.quantity * l.unitPrice, 0) * (order.vatPercent / 100));
}

async function edit(inv: any) {
  const [order] = await getPurchaseOrder(inv.purchaseOrderId);
  form.value = {
    id: inv.id, purchaseOrderId: inv.purchaseOrderId, invoiceNumber: inv.invoiceNumber, invoiceDate: (inv.invoiceDate || "").slice(0, 10), isOfficial: inv.isOfficial,
    vatAmount: inv.vatAmount, documentId: inv.documentId, order,
    lines: inv.lines.map((l: any) => {
      const ol = order.lines.find((x: any) => x.id === l.orderLineId);
      return { orderLineId: l.orderLineId, label: (l.itemCode || "") + " " + (l.description || ""), accepted: ol?.acceptedQuantity, invoiced: ol?.invoicedQuantity, quantity: l.quantity, unitPrice: l.unitPrice, orderPrice: l.orderUnitPrice };
    }),
  };
  detail.value = null;
}

async function save() {
  const f = form.value;
  busy.value = true;
  const [data, error] = await saveSupplierInvoice({
    id: f.id, purchaseOrderId: f.purchaseOrderId, invoiceNumber: f.invoiceNumber, invoiceDate: f.invoiceDate, isOfficial: f.isOfficial, vatAmount: f.vatAmount, documentId: f.documentId,
    lines: f.lines.filter((l: any) => Number(l.quantity) > 0).map((l: any) => ({ orderLineId: l.orderLineId, quantity: Number(l.quantity), unitPrice: Number(l.unitPrice) })),
  });
  busy.value = false;
  if (error) return fail(error);
  form.value = null;
  detail.value = data;
  ok(data.status === 1 ? "فاکتور با سفارش و رسید تطبیق دارد." : "فاکتور ثبت شد؛ مغایرت قیمت دارد و نیازمند تایید است.");
  load();
}

async function attach(event: any) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  const [id, error] = await uploadFile(file, "فاکتور تامین‌کننده " + form.value.invoiceNumber);
  if (error) return fail(error);
  form.value.documentId = id;
  ok("تصویر فاکتور بارگذاری شد.");
}

async function act(action: string, note?: string) {
  busy.value = true;
  const [data, error] = await invoiceAction(detail.value.id, action, note);
  busy.value = false;
  if (error) return fail(error);
  detail.value = data;
  cancelNote.value = null;
  ok("ثبت شد.");
  load();
}

const canApprove = (inv: any) => (inv.status === 1 && canApproveMatched) || (inv.status === 2 && canApproveVariance);

async function open(inv: any) {
  const [data, error] = await getSupplierInvoice(inv.id);
  if (error) return fail(error);
  detail.value = data;
}

onMounted(async () => {
  await load();
  if (route.query.id) open({ id: Number(route.query.id) });
});
</script>

<template>
  <div class="inv-page">
    <PrintForm
      v-if="detail"
      title="برگه تطبیق سه‌طرفه فاکتور تامین‌کننده"
      :fields="[
        { label: 'شماره ثبت', value: detail.number, ltr: true },
        { label: 'شماره فاکتور تامین‌کننده', value: detail.invoiceNumber, ltr: true },
        { label: 'تاریخ فاکتور', value: jalali(detail.invoiceDate) },
        { label: 'تامین‌کننده', value: detail.providerName },
        { label: 'سفارش خرید', value: detail.purchaseOrderNumber, ltr: true },
        { label: 'نوع فاکتور', value: detail.isOfficial ? 'رسمی' : 'غیررسمی' },
        { label: 'وضعیت', value: invoiceStatuses[detail.status] },
        { label: 'اختلاف قیمت (ریال)', value: rial(detail.priceVariance) },
      ]"
      :notes="detail.matchNote ? [{ label: 'نتیجه تطبیق', value: detail.matchNote }] : []"
      :signatures="[
        { title: 'ثبت‌کننده', name: detail.creatorName, date: jalali(detail.createDate) },
        { title: detail.status === 2 || detail.priceVariance ? 'تاییدکننده (مالی / معاونت PMO)' : 'تایید مالی', name: detail.approvedByName, date: jalali(detail.approvedAt) },
      ]"
      distribution="توزیع نسخ: ۱- مالی ۲- بازرگانی ۳- انبار">
      <table>
        <thead><tr><th>ردیف</th><th>کالا / شرح</th><th>مقدار فاکتور</th><th>فی فاکتور</th><th>فی سفارش</th><th>مبلغ</th></tr></thead>
        <tbody>
          <tr v-for="(l, i) in detail.lines" :key="l.id">
            <td>{{ i + 1 }}</td>
            <td><span class="inv-ltr">{{ l.itemCode }}</span> {{ l.description }}</td>
            <td class="num">{{ num(l.quantity) }}</td>
            <td class="num">{{ rial(l.unitPrice) }}</td>
            <td class="num">{{ rial(l.orderUnitPrice) }}</td>
            <td class="num">{{ rial(l.amount) }}</td>
          </tr>
        </tbody>
        <tfoot>
          <tr><td colspan="5">جمع خالص</td><td class="num">{{ rial(detail.netAmount) }}</td></tr>
          <tr><td colspan="5">مالیات</td><td class="num">{{ rial(detail.vatAmount) }}</td></tr>
          <tr><td colspan="5">جمع کل</td><td class="num">{{ rial(detail.totalAmount) }}</td></tr>
        </tfoot>
      </table>
    </PrintForm>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">فاکتورهای تامین‌کنندگان</h1>
        <button v-if="canSave" class="btn btn-sm btn-primary" @click="newInvoice">ثبت فاکتور</button>
      </div>
      <p class="inv-muted">
        فاکتور فقط برای مقدارِ تایید‌شده کیفی و فاکتورنشده پذیرفته می‌شود. اگر قیمت فاکتور با سفارش فرق داشته باشد، فاکتور «نیازمند تایید» می‌شود و معاونت PMO باید تایید کند.
      </p>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره فاکتور یا تامین‌کننده" @keydown.enter="search" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in invoiceStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>فاکتور</th><th>تاریخ</th><th>تامین‌کننده</th><th>سفارش</th><th>جمع کل (ریال)</th><th>اختلاف قیمت</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="8" class="text-center inv-muted">فاکتوری نیست.</td></tr>
          <tr v-for="r in rows" :key="r.id" role="button" @click="open(r)">
            <td class="inv-ltr">{{ r.number }}</td>
            <td class="inv-ltr">{{ r.invoiceNumber }}</td>
            <td>{{ jalali(r.invoiceDate) }}</td>
            <td>{{ r.providerName }}</td>
            <td class="inv-ltr">{{ r.purchaseOrderNumber }}</td>
            <td class="num">{{ rial(r.totalAmount) }}</td>
            <td class="num" :class="{ 'text-danger': r.priceVariance }">{{ rial(r.priceVariance) }}</td>
            <td><span class="inv-badge" :class="{ ok: r.status === 1 || r.status === 3, warn: r.status === 2 || r.status === 0, bad: r.status === 9 }">{{ invoiceStatuses[r.status] }}</span></td>
          </tr>
        </tbody>
      </table>
      <div class="d-flex gap-2 align-items-center">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--, load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }}</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++, load()">بعدی</button>
      </div>
    </div>

    <!-- detail with the match result -->
    <div v-if="detail && !form" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">فاکتور {{ detail.invoiceNumber }} — <span class="inv-ltr">{{ detail.number }}</span></h2>
        <div class="d-flex flex-wrap gap-2 mb-2">
          <span class="inv-badge" :class="{ ok: detail.status === 1 || detail.status === 3, warn: detail.status === 2 || detail.status === 0, bad: detail.status === 9 }">{{ invoiceStatuses[detail.status] }}</span>
          <span class="inv-badge">{{ detail.providerName }}</span>
          <span class="inv-badge">سفارش {{ detail.purchaseOrderNumber }}</span>
          <span class="inv-badge" :class="{ bad: detail.priceVariance }">اختلاف قیمت: {{ rial(detail.priceVariance) }}</span>
          <span v-if="detail.approvedByName" class="inv-badge ok">تایید: {{ detail.approvedByName }}</span>
        </div>
        <div v-if="detail.matchNote" class="alert alert-warning small py-2">{{ detail.matchNote }}</div>
        <div v-if="detail.cancelReason" class="alert alert-danger small py-2">ابطال: {{ detail.cancelReason }}</div>
        <table class="table table-sm table-bordered">
          <thead><tr><th>#</th><th>کالا / شرح</th><th>مقدار</th><th>فی فاکتور</th><th>فی سفارش</th><th>مبلغ</th></tr></thead>
          <tbody>
            <tr v-for="(l, i) in detail.lines" :key="l.id" :class="{ 'table-warning': l.unitPrice !== l.orderUnitPrice }">
              <td>{{ i + 1 }}</td><td><span class="inv-ltr">{{ l.itemCode }}</span> {{ l.description }}</td><td class="num">{{ num(l.quantity) }}</td>
              <td class="num">{{ rial(l.unitPrice) }}</td><td class="num">{{ rial(l.orderUnitPrice) }}</td><td class="num">{{ rial(l.amount) }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr><td colspan="5">جمع خالص / مالیات / کل</td><td class="num">{{ rial(detail.netAmount) }} / {{ rial(detail.vatAmount) }} / {{ rial(detail.totalAmount) }}</td></tr>
          </tfoot>
        </table>
        <div v-if="cancelNote !== null" class="d-flex gap-2 mb-2">
          <input v-model="cancelNote" class="form-control form-control-sm" placeholder="علت ابطال *" />
          <button :disabled="busy" class="btn btn-sm btn-danger" @click="act('cancel', cancelNote || '')">ابطال</button>
        </div>
        <div class="d-flex flex-wrap gap-2 justify-content-end">
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="nextTick(printPage)">چاپ برگه تطبیق</button>
          <button v-if="detail.status < 3 && canSave" class="btn btn-sm btn-outline-primary" @click="edit(detail)">ویرایش</button>
          <button v-if="detail.status < 3 && canSave && cancelNote === null" class="btn btn-sm btn-outline-danger" @click="cancelNote = ''">ابطال</button>
          <button v-if="canApprove(detail)" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">
            {{ detail.status === 2 ? "تایید با اختلاف قیمت (معاونت PMO)" : "تایید مالی" }}
          </button>
        </div>
      </div>
    </div>

    <!-- new / edit -->
    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش فاکتور" : "ثبت فاکتور تامین‌کننده" }}</h2>
        <div class="row g-2">
          <div class="col-md-5">
            <label class="form-label small mb-0">سفارش خرید *</label>
            <select v-model="form.purchaseOrderId" :disabled="!!form.id" class="form-select form-select-sm" @change="pickOrder">
              <option :value="null">-</option>
              <option v-for="o in orders" :key="o.id" :value="o.id">{{ o.number }} — {{ o.providerName }}</option>
            </select>
          </div>
          <div class="col-md-3"><label class="form-label small mb-0">شماره فاکتور تامین‌کننده *</label><input v-model="form.invoiceNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ فاکتور *</label><input v-model="form.invoiceDate" class="form-control form-control-sm" type="date" /></div>
          <div class="col-md-2 d-flex align-items-end"><label class="form-check small"><input v-model="form.isOfficial" class="form-check-input" type="checkbox" /> رسمی</label></div>
          <div class="col-md-3"><label class="form-label small mb-0">مالیات ارزش افزوده (ریال)</label><input v-model.number="form.vatAmount" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">تصویر فاکتور</label><input class="form-control form-control-sm" type="file" @change="attach" /><span v-if="form.documentId" class="inv-badge ok">پیوست است</span></div>
        </div>
        <table class="table table-sm table-bordered mt-2">
          <thead><tr><th>ردیف سفارش</th><th>تایید کیفی / فاکتور شده</th><th style="width: 8rem">مقدار فاکتور</th><th style="width: 9rem">فی فاکتور</th><th>فی سفارش</th></tr></thead>
          <tbody>
            <tr v-if="!form.lines.length"><td colspan="5" class="text-center inv-muted">سفارش را انتخاب کنید؛ فقط مقدار تایید‌شده کیفی قابل فاکتور است.</td></tr>
            <tr v-for="l in form.lines" :key="l.orderLineId" :class="{ 'table-warning': l.unitPrice !== l.orderPrice }">
              <td>{{ l.label }}</td>
              <td class="num">{{ num(l.accepted) }} / {{ num(l.invoiced) }}</td>
              <td><input v-model.number="l.quantity" class="form-control form-control-sm" step="any" type="number" /></td>
              <td><input v-model.number="l.unitPrice" class="form-control form-control-sm" type="number" /></td>
              <td class="num">{{ rial(l.orderPrice) }}</td>
            </tr>
          </tbody>
        </table>
        <div class="d-flex gap-2 justify-content-end">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت و تطبیق</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
