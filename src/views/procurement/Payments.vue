<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import PrintForm from "@/components/print/PrintForm.vue";
import { getBankAccounts } from "@/api/FnAPIs";
import { deductionKinds, parseAmount } from "@/composables/fn";
import { getPaymentRequests, getPurchaseOrders, getSupplierInvoices, paymentAction, savePaymentRequest } from "@/api/ProcurementAPIs";
import { fail, jalali, ok, printPage, rial, today } from "@/composables/inventory";
import { paymentStatuses, paymentTypes } from "@/composables/procurement";
import { hasRole, ProcurementRoles } from "@/composables/roles";

// Payment requests: made by commerce, approved by the PMO deputy, paid by finance with the payment reference.
const route = useRoute();
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const filters = ref({ Status: null as number | null, Contains: "" });
const orders = ref<any[]>([]);
const invoices = ref<any[]>([]);
const form = ref<any>(null);
const detail = ref<any>(null);
const dialog = ref<null | { action: string; title: string; label: string; value: string }>(null);
const busy = ref(false);
const canCreate = hasRole(ProcurementRoles.commercial);
const canApprove = hasRole(ProcurementRoles.orderApproval);
const canPay = hasRole(ProcurementRoles.finance);
const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));
const banks = ref<any[]>([]);
const bankAccountId = ref<number | null>(null);
// typed deductions (withholding, retentions, late penalty, advance recovery); without lines the single total below is used
const lineTotal = computed(() => (form.value ? form.value.deductionLines.reduce((s: number, l: any) => s + (parseAmount(l.amount) || 0), 0) : 0));
const totalDeductions = computed(() => (form.value?.deductionLines.length ? lineTotal.value : Number(form.value?.deductions) || 0));
const net = computed(() => (form.value ? (Number(form.value.amount) || 0) - totalDeductions.value : 0));
function setLineAmount(l: any) {
  const b = parseAmount(l.base), r = parseAmount(l.ratePercent);
  if (b && r) l.amount = String(Math.round((b * r) / 100));
}

async function load() {
  const [data, error] = await getPaymentRequests({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

function search() {
  page.value = 1;
  load();
}

async function create() {
  const [o] = await getPurchaseOrders({ PageSize: 200 });
  orders.value = (o?.data || []).filter((x: any) => x.status >= 2 && x.status <= 5);
  const [i] = await getSupplierInvoices({ Status: 3, PageSize: 200 });
  invoices.value = i?.data || [];
  form.value = { id: null, type: 3, requestDate: today(), purchaseOrderId: null, invoiceId: null, amount: null, deductions: 0, deductionNote: "", description: "", deductionLines: [] };
}

function pickInvoice() {
  const inv = invoices.value.find((i) => i.id === form.value.invoiceId);
  if (inv) {
    form.value.purchaseOrderId = inv.purchaseOrderId;
    form.value.amount = inv.totalAmount;
  }
}

async function save() {
  const f = form.value;
  const deductionLines = f.deductionLines.map((l: any) => ({ kind: Number(l.kind), base: parseAmount(l.base) || 0, ratePercent: parseAmount(l.ratePercent) || 0, amount: parseAmount(l.amount) || 0, note: l.note || null }));
  if (deductionLines.some((l: any) => l.amount <= 0)) return fail("مبلغ هر ردیف کسورات را وارد کنید.");
  busy.value = true;
  const [data, error] = await savePaymentRequest({ ...f, deductions: deductionLines.length ? 0 : f.deductions, deductionLines });
  busy.value = false;
  if (error) return fail(error);
  form.value = null;
  detail.value = data;
  ok("درخواست پرداخت ثبت شد.");
  load();
}

async function ask(action: string) {
  if (action === "paid") {
    const [b] = await getBankAccounts({ PageSize: 200 });
    banks.value = (b?.data || []).filter((x: any) => x.isActive);
    bankAccountId.value = null;
  }
  if (action === "paid") dialog.value = { action, title: "ثبت پرداخت", label: "شماره سند پرداخت در سپیدار یا شماره پیگیری بانک *", value: "" };
  else if (action === "reject") dialog.value = { action, title: "رد درخواست پرداخت", label: "علت رد *", value: "" };
  else act(action);
}

async function act(action: string, value?: string) {
  busy.value = true;
  const [data, error] = await paymentAction(detail.value.id, action, action === "reject" ? value : undefined, action === "paid" ? value : undefined, action === "paid" ? bankAccountId.value : null);
  busy.value = false;
  if (error) return fail(error);
  detail.value = data;
  dialog.value = null;
  ok("ثبت شد.");
  load();
}

async function confirmDialog() {
  const d = dialog.value!;
  if (!d.value.trim()) return fail(d.label.replace(" *", "") + " را بنویسید.");
  if (d.action === "paid" && !bankAccountId.value) return fail("حساب بانکی پرداخت را انتخاب کنید.");
  await act(d.action, d.value);
}

onMounted(async () => {
  await load();
  if (route.query.id) detail.value = rows.value.find((r) => r.id === Number(route.query.id)) || null;
});
</script>

<template>
  <div class="inv-page">
    <PrintForm
      v-if="detail"
      title="درخواست پرداخت"
      :fields="[
        { label: 'شماره', value: detail.number, ltr: true },
        { label: 'تاریخ', value: jalali(detail.requestDate) },
        { label: 'نوع', value: paymentTypes[detail.type] },
        { label: 'تامین‌کننده', value: detail.providerName },
        { label: 'سفارش خرید', value: detail.purchaseOrderNumber || '-', ltr: true },
        { label: 'فاکتور', value: detail.invoiceNumber || '-', ltr: true },
        { label: 'مبلغ (ریال)', value: rial(detail.amount) },
        { label: 'کسورات (ریال)', value: rial(detail.deductions) },
        { label: 'خالص قابل پرداخت (ریال)', value: rial(detail.netAmount) },
        { label: 'تایید کیفی', value: detail.qcApproved ? 'دارد' : 'ندارد' },
        { label: 'شماره سند / پیگیری پرداخت', value: detail.paymentReference || '-', ltr: true },
        { label: 'وضعیت', value: paymentStatuses[detail.status] },
      ]"
      :notes="[
        ...(detail.deductionNote ? [{ label: 'شرح کسورات', value: detail.deductionNote }] : []),
        ...(detail.description ? [{ label: 'توضیح', value: detail.description }] : []),
      ]"
      :signatures="[
        { title: 'درخواست‌کننده (بازرگانی)', name: detail.creatorName, date: jalali(detail.createDate) },
        { title: 'تاییدکننده (معاونت PMO)', name: detail.approvedByName, date: jalali(detail.approvedAt) },
        { title: 'پرداخت‌کننده (مالی)', name: detail.paidByName, date: jalali(detail.paidAt) },
      ]"
      distribution="توزیع نسخ: ۱- مالی ۲- بازرگانی" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">درخواست‌های پرداخت</h1>
        <button v-if="canCreate" class="btn btn-sm btn-primary" @click="create">درخواست پرداخت جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا تامین‌کننده" @keydown.enter="search" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in paymentStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>تاریخ</th><th>نوع</th><th>تامین‌کننده</th><th>سفارش / فاکتور</th><th>خالص (ریال)</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="7" class="text-center inv-muted">درخواستی نیست.</td></tr>
          <tr v-for="r in rows" :key="r.id" role="button" @click="detail = r">
            <td class="inv-ltr">{{ r.number }}</td>
            <td>{{ jalali(r.requestDate) }}</td>
            <td>{{ paymentTypes[r.type] }}</td>
            <td>{{ r.providerName }}</td>
            <td class="inv-ltr">{{ r.purchaseOrderNumber }} {{ r.invoiceNumber }}</td>
            <td class="num">{{ rial(r.netAmount) }}</td>
            <td><span class="inv-badge" :class="{ ok: r.status === 1 || r.status === 2, warn: r.status === 0, bad: r.status === 9 }">{{ paymentStatuses[r.status] }}</span></td>
          </tr>
        </tbody>
      </table>
      <div class="d-flex gap-2 align-items-center">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--, load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }}</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++, load()">بعدی</button>
      </div>
    </div>

    <div v-if="detail && !form" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">درخواست پرداخت <span class="inv-ltr">{{ detail.number }}</span></h2>
        <div class="d-flex flex-wrap gap-2 mb-2">
          <span class="inv-badge" :class="{ ok: detail.status === 1 || detail.status === 2, warn: detail.status === 0, bad: detail.status === 9 }">{{ paymentStatuses[detail.status] }}</span>
          <span class="inv-badge">{{ paymentTypes[detail.type] }}</span>
          <span class="inv-badge">{{ detail.providerName }}</span>
          <span class="inv-badge" :class="{ ok: detail.qcApproved }">{{ detail.qcApproved ? "تایید کیفی دارد" : "بدون تایید کیفی" }}</span>
        </div>
        <table class="table table-sm table-bordered">
          <tbody>
            <tr><td>مبلغ</td><td class="num">{{ rial(detail.amount) }}</td></tr>
            <tr><td>کسورات</td><td class="num">{{ rial(detail.deductions) }} <span class="inv-muted">{{ detail.deductionNote }}</span><div v-for="(l, i) in detail.deductionLines || []" :key="i" class="small">{{ deductionKinds[l.kind] }}: {{ rial(l.amount) }} <span v-if="l.ratePercent" class="inv-muted">({{ l.ratePercent }}% از {{ rial(l.base) }})</span> {{ l.note }}</div></td></tr>
            <tr><td>خالص قابل پرداخت</td><td class="num fw-bold">{{ rial(detail.netAmount) }}</td></tr>
            <tr><td>سفارش / فاکتور</td><td class="inv-ltr">{{ detail.purchaseOrderNumber }} {{ detail.invoiceNumber }}</td></tr>
            <tr v-if="detail.approvedByName"><td>تایید</td><td>{{ detail.approvedByName }} {{ jalali(detail.approvedAt, true) }}</td></tr>
            <tr v-if="detail.paidByName"><td>پرداخت</td><td>{{ detail.paidByName }} {{ jalali(detail.paidAt, true) }} — <span class="inv-ltr">{{ detail.paymentReference }}</span></td></tr>
            <tr v-if="detail.rejectReason"><td>علت رد</td><td>{{ detail.rejectReason }}</td></tr>
          </tbody>
        </table>
        <p class="inv-muted small">تاییدکننده باید غیر از ثبت‌کننده باشد؛ پرداخت‌کننده غیر از ثبت‌کننده و تاییدکننده.</p>
        <div class="d-flex flex-wrap gap-2 justify-content-end">
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="nextTick(printPage)">چاپ</button>
          <button v-if="detail.status === 0 && canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="ask('approve')">تایید (معاونت PMO)</button>
          <button v-if="detail.status === 1 && canPay" :disabled="busy" class="btn btn-sm btn-primary" @click="ask('paid')">ثبت پرداخت</button>
          <button v-if="detail.status < 2 && (canApprove || canPay)" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="ask('reject')">رد</button>
        </div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">درخواست پرداخت جدید</h2>
        <div class="row g-2">
          <div class="col-6">
            <label class="form-label small mb-0">نوع</label>
            <select v-model="form.type" class="form-select form-select-sm">
              <option v-for="(t, k) in paymentTypes" :key="k" :value="Number(k)">{{ t }}</option>
            </select>
          </div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ</label><input v-model="form.requestDate" class="form-control form-control-sm" type="date" /></div>
          <div v-if="form.type === 3" class="col-12">
            <label class="form-label small mb-0">فاکتور تاییدشده *</label>
            <select v-model="form.invoiceId" class="form-select form-select-sm" @change="pickInvoice">
              <option :value="null">-</option>
              <option v-for="i in invoices" :key="i.id" :value="i.id">{{ i.invoiceNumber }} — {{ i.providerName }} ({{ rial(i.totalAmount) }})</option>
            </select>
          </div>
          <div v-else class="col-12">
            <label class="form-label small mb-0">سفارش خرید *</label>
            <select v-model="form.purchaseOrderId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="o in orders" :key="o.id" :value="o.id">{{ o.number }} — {{ o.providerName }} ({{ rial(o.totalAmount) }})</option>
            </select>
            <p class="inv-muted small mb-0">پیش‌پرداخت تا درصد مجاز سفارش و با ضمانت‌نامه؛ صورت‌وضعیت تا سقف کار تاییدشده کیفی.</p>
          </div>
          <div class="col-6"><label class="form-label small mb-0">مبلغ (ریال) *</label><input v-model.number="form.amount" class="form-control form-control-sm" type="number" /></div>
          <div v-if="!form.deductionLines.length" class="col-6"><label class="form-label small mb-0">کسورات (ریال)</label><input v-model.number="form.deductions" class="form-control form-control-sm" type="number" /></div>
          <div v-if="!form.deductionLines.length" class="col-12"><label class="form-label small mb-0">شرح کسورات (جریمه، حسن انجام کار...)</label><input v-model="form.deductionNote" class="form-control form-control-sm" /></div>
          <div class="col-12">
            <table v-if="form.deductionLines.length" class="table table-sm">
              <thead><tr><th>نوع کسر</th><th>مبنا (ریال)</th><th>درصد</th><th>مبلغ (ریال)</th><th>توضیح</th><th></th></tr></thead>
              <tbody>
                <tr v-for="(l, i) in form.deductionLines" :key="i">
                  <td><select v-model="l.kind" class="form-select form-select-sm"><option v-for="(t, k) in deductionKinds" :key="k" :value="Number(k)">{{ t }}</option></select></td>
                  <td><input v-model="l.base" class="form-control form-control-sm" @input="setLineAmount(l)" /></td>
                  <td><input v-model="l.ratePercent" class="form-control form-control-sm" style="width: 5rem" @input="setLineAmount(l)" /></td>
                  <td><input v-model="l.amount" class="form-control form-control-sm" /></td>
                  <td><input v-model="l.note" class="form-control form-control-sm" /></td>
                  <td><button class="btn btn-sm btn-link text-danger p-0" @click="form.deductionLines.splice(i, 1)">حذف</button></td>
                </tr>
              </tbody>
            </table>
            <button class="btn btn-sm btn-outline-secondary" @click="form.deductionLines.push({ kind: 1, base: '', ratePercent: '', amount: '', note: '' })">+ ردیف کسورات</button>
            <span class="inv-muted small ms-2">کسر «بازیابی پیش‌پرداخت» فقط تا مانده پیش‌پرداخت باز همین سفارش مجاز است.</span>
          </div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.description" class="form-control form-control-sm" /></div>
          <div class="col-12 small">خالص قابل پرداخت: <b>{{ rial(net) }}</b> ریال</div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button>
        </div>
      </div>
    </div>

    <div v-if="dialog" class="inv-modal-backdrop screen-only" @click.self="dialog = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ dialog.title }}</h2>
        <label class="form-label small mb-0">{{ dialog.label }}</label>
        <input v-model="dialog.value" class="form-control form-control-sm" />
        <template v-if="dialog.action === 'paid'">
          <label class="form-label small mb-0 mt-2">حساب بانکی پرداخت *</label>
          <select v-model="bankAccountId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select>
        </template>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="dialog = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="confirmDialog">ثبت</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
