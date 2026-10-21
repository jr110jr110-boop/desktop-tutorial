<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import ReceiptPrint from "@/components/fn/print/ReceiptPrint.vue";
import QPager from "@/components/quality/QPager.vue";
import { getBankAccounts, getCashBoxes, getCustomers, getReceipt, getReceipts, getSalesInvoices, receiptAction, saveReceipt } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { badgeOfDoc, docStatuses, instruments, money, parseAmount, printSheet, usePagedList } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Receipts from customers: cash, transfer, card or cheque (cheques enter the received-cheque register); settles the oldest open invoices (FIFO) or the ones chosen.
const filters = ref({ Status: null as number | null, From: null as string | null, To: null as string | null, Contains: "" });
const list = usePagedList(getReceipts, filters);
const banks = ref<any[]>([]);
const boxes = ref<any[]>([]);
const openInvoices = ref<any[]>([]);
const form = ref<any>(null);
const detail = ref<any>(null);
const busy = ref(false);
const printing = ref(false);
const canWrite = hasRole(FnRoles.treasuryAndAccountants);

const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchCustomers = async (t: string) => rowsOf((await getCustomers({ Contains: t, PageSize: 15 }))[0]);
const blankLine = (): any => ({ instrument: 2, amount: "", bankAccountId: null, cashBoxId: null, reference: "", chequeSayadId: "", chequeSerial: "", chequeBank: "", chequeBranch: "", chequeHolder: "", chequeDueDate: null });
const linesTotal = () => (form.value ? form.value.lines.reduce((s: number, l: any) => s + (parseAmount(l.amount) || 0), 0) : 0);
const allocTotal = () => (form.value ? form.value.allocations.reduce((s: number, a: any) => s + (parseAmount(a.amount) || 0), 0) : 0);

async function loadRefs() {
  const [b] = await getBankAccounts({ PageSize: 200 });
  banks.value = (b?.data || []).filter((x: any) => x.isActive);
  const [c] = await getCashBoxes({ PageSize: 200 });
  boxes.value = (c?.data || []).filter((x: any) => x.isActive);
}

function newReceipt() {
  form.value = { id: null, date: today(), customer: null, payerName: "", description: "", lines: [blankLine()], fifo: true, allocations: [] };
  openInvoices.value = [];
}

async function pickCustomer() {
  openInvoices.value = [];
  if (!form.value.customer) return;
  const [data] = await getSalesInvoices({ CustomerId: form.value.customer.id, OpenOnly: true, PageSize: 100 });
  openInvoices.value = rowsOf(data).filter((i: any) => i.status === 2 && i.openAmount > 0);
}

function addAllocation(inv: any) {
  if (form.value.allocations.some((a: any) => a.invoiceId === inv.id)) return;
  form.value.allocations.push({ invoiceId: inv.id, invoiceNumber: inv.number, amount: String(Math.min(inv.openAmount, Math.max(linesTotal() - allocTotal(), 0)) || inv.openAmount) });
}

async function open(id: number) {
  const [data, error] = await getReceipt(id);
  if (error) return fail(error);
  detail.value = data;
}

async function edit() {
  const d = detail.value;
  form.value = {
    id: d.id, date: String(d.date).slice(0, 10), customer: d.customerId ? { id: d.customerId, name: d.customerName } : null, payerName: d.payerName || "", description: d.description || "",
    fifo: !(d.allocations && d.allocations.length),
    lines: d.lines.map((l: any) => ({
      instrument: l.instrument, amount: String(l.amount), bankAccountId: l.bankAccountId, cashBoxId: l.cashBoxId, reference: l.reference || "", chequeSayadId: l.chequeSayadId || "", chequeSerial: l.chequeSerial || "",
      chequeBank: l.chequeBank || "", chequeBranch: l.chequeBranch || "", chequeHolder: l.chequeHolder || "", chequeDueDate: l.chequeDueDate ? String(l.chequeDueDate).slice(0, 10) : null,
    })),
    allocations: (d.allocations || []).map((a: any) => ({ invoiceId: a.invoiceId, invoiceNumber: a.invoiceNumber, amount: String(a.amount) })),
  };
  detail.value = null;
  await pickCustomer();
}

async function save() {
  const f = form.value;
  if (!f.customer && !String(f.payerName).trim()) return fail("مشتری یا نام پرداخت‌کننده را وارد کنید.");
  const lines = f.lines.map((l: any) => ({
    instrument: Number(l.instrument), amount: parseAmount(l.amount), bankAccountId: l.instrument === 1 ? null : l.bankAccountId, cashBoxId: l.instrument === 1 ? l.cashBoxId : null, reference: l.reference || null,
    chequeSayadId: l.instrument === 3 ? l.chequeSayadId || null : null, chequeSerial: l.instrument === 3 ? l.chequeSerial || null : null, chequeBank: l.instrument === 3 ? l.chequeBank || null : null,
    chequeBranch: l.instrument === 3 ? l.chequeBranch || null : null, chequeHolder: l.instrument === 3 ? l.chequeHolder || null : null, chequeDueDate: l.instrument === 3 ? l.chequeDueDate : null,
  }));
  if (lines.some((l: any) => !l.amount)) return fail("مبلغ همه ردیف‌ها را وارد کنید.");
  const manual = !f.fifo && f.allocations.length > 0;
  busy.value = true;
  const [data, error] = await saveReceipt({
    id: f.id, date: f.date, customerId: f.customer?.id || null, payerName: f.payerName || null, description: f.description || null, lines,
    allocateFifo: !manual, allocations: manual ? f.allocations.map((a: any) => ({ invoiceId: a.invoiceId, amount: parseAmount(a.amount) })) : [],
  });
  busy.value = false;
  if (error) return fail(error);
  ok("سند دریافت ذخیره شد.");
  form.value = null;
  detail.value = data;
  list.load();
}

async function act(action: string) {
  busy.value = true;
  const [data, error] = await receiptAction({ id: detail.value.id, action });
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "سند تایید شد." : action === "post" ? "دریافت ثبت نهایی شد." : "سند باطل شد.");
  detail.value = data;
  list.load();
}

async function print() {
  printing.value = true;
  await nextTick();
  printSheet();
}
const bankTitle = (id: number) => banks.value.find((b) => b.id === id)?.title || "";
const boxTitle = (id: number) => boxes.value.find((b) => b.id === id)?.title || "";

onMounted(() => {
  loadRefs();
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <ReceiptPrint v-if="printing && detail" :banks="banks" :boxes="boxes" :r="detail" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">دریافت‌ها از مشتریان</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="newReceipt">سند دریافت جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in docStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا نام مشتری" @keydown.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تاریخ</th><th>مشتری / پرداخت‌کننده</th><th>مبلغ (ریال)</th><th>تخصیص‌یافته</th><th>وضعیت</th><th>تهیه‌کننده</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">سند دریافتی نیست.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id" style="cursor: pointer" @click="open(r.id)">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ jalali(r.date) }}</td><td>{{ r.customerName || r.payerName }}</td><td class="fn-num">{{ money(r.totalAmount) }}</td><td class="fn-num">{{ money(r.allocatedAmount) }}</td>
              <td><span :class="badgeOfDoc(r.status)" class="inv-badge">{{ docStatuses[r.status] }}</span></td><td>{{ r.creatorName }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">سند دریافت <span class="inv-ltr">{{ detail.number }}</span> <span :class="badgeOfDoc(detail.status)" class="inv-badge">{{ docStatuses[detail.status] }}</span></h2>
        <div class="row g-2 mb-2">
          <div class="col-md-3"><b>تاریخ:</b> {{ jalali(detail.date) }}</div>
          <div class="col-md-5"><b>از:</b> {{ detail.customerName || detail.payerName }}</div>
          <div class="col-md-2"><b>جمع:</b> {{ money(detail.totalAmount) }}</div>
          <div class="col-md-2"><b>تخصیص:</b> {{ money(detail.allocatedAmount) }}</div>
          <div v-if="detail.description" class="col-12"><b>بابت:</b> {{ detail.description }}</div>
        </div>
        <table class="table table-sm">
          <thead><tr><th>روش</th><th>بانک / صندوق</th><th>چک</th><th>سررسید</th><th>مبلغ</th></tr></thead>
          <tbody>
            <tr v-for="(l, i) in detail.lines" :key="i">
              <td>{{ instruments[l.instrument] }}</td><td>{{ bankTitle(l.bankAccountId) || boxTitle(l.cashBoxId) }}</td>
              <td><span v-if="l.instrument === 3" class="inv-ltr">{{ l.chequeSerial }} / {{ l.chequeSayadId }}</span> {{ l.chequeBank }}</td>
              <td>{{ l.chequeDueDate ? jalali(l.chequeDueDate) : "" }}</td><td class="fn-num">{{ money(l.amount) }}</td>
            </tr>
          </tbody>
        </table>
        <table v-if="detail.allocations && detail.allocations.length" class="table table-sm">
          <thead><tr><th>فاکتور فروش</th><th>مبلغ تسویه‌شده</th></tr></thead>
          <tbody><tr v-for="(a, i) in detail.allocations" :key="i"><td class="inv-ltr">{{ a.invoiceNumber }}</td><td class="fn-num">{{ money(a.amount) }}</td></tr></tbody>
        </table>
        <div class="d-flex gap-2 justify-content-end flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="print">چاپ سند دریافت</button>
          <button v-if="canWrite && detail.status === 0" class="btn btn-sm btn-outline-primary" @click="edit">ویرایش</button>
          <button v-if="canWrite && detail.status === 0" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید (غیر از تهیه‌کننده)</button>
          <button v-if="canWrite && detail.status === 1" :disabled="busy" class="btn btn-sm btn-success" @click="act('post')">ثبت نهایی</button>
          <button v-if="canWrite && (detail.status === 0 || detail.status === 1)" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">ابطال</button>
        </div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش سند دریافت" : "سند دریافت جدید" }}</h2>
        <div class="row g-2 mb-2">
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="form.date" /></div>
          <div class="col-md-5"><label class="form-label small mb-0">مشتری</label><LookupBox v-model="form.customer" :fetch="searchCustomers" :label="(c: any) => c.name" placeholder="نام مشتری" @pick="pickCustomer" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نام پرداخت‌کننده (اگر مشتری ثبت‌شده نیست)</label><input v-model="form.payerName" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">بابت</label><input v-model="form.description" class="form-control form-control-sm" /></div>
        </div>

        <div class="fn-section">ردیف‌های دریافت</div>
        <div class="table-responsive">
          <table class="table table-sm fn-lines">
            <thead><tr><th>روش</th><th>بانک / صندوق</th><th>مبلغ</th><th>مرجع / مشخصات چک</th><th></th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in form.lines" :key="i">
                <td><select v-model="l.instrument" class="form-select form-select-sm"><option :value="1">نقد</option><option :value="2">حواله بانکی</option><option :value="3">چک</option><option :value="4">کارت</option></select></td>
                <td>
                  <select v-if="l.instrument === 1" v-model="l.cashBoxId" class="form-select form-select-sm"><option :value="null">صندوق...</option><option v-for="b in boxes" :key="b.id" :value="b.id">{{ b.title }}</option></select>
                  <select v-else-if="l.instrument !== 3" v-model="l.bankAccountId" class="form-select form-select-sm"><option :value="null">حساب بانکی...</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select>
                  <span v-else class="inv-muted">چک وارد صندوق چک‌های دریافتی می‌شود</span>
                </td>
                <td><input v-model="l.amount" class="form-control form-control-sm fn-num" /></td>
                <td>
                  <input v-if="l.instrument !== 3" v-model="l.reference" class="form-control form-control-sm" placeholder="شماره پیگیری / مرجع" />
                  <div v-else class="d-flex gap-1 flex-wrap">
                    <input v-model="l.chequeSerial" class="form-control form-control-sm inv-ltr" placeholder="سریال" style="width: 110px" />
                    <input v-model="l.chequeSayadId" class="form-control form-control-sm inv-ltr" placeholder="صیادی ۱۶ رقم" style="width: 150px" />
                    <input v-model="l.chequeBank" class="form-control form-control-sm" placeholder="بانک" style="width: 110px" />
                    <input v-model="l.chequeBranch" class="form-control form-control-sm" placeholder="شعبه" style="width: 100px" />
                    <input v-model="l.chequeHolder" class="form-control form-control-sm" placeholder="صاحب حساب" style="width: 130px" />
                    <FnDate v-model="l.chequeDueDate" />
                  </div>
                </td>
                <td><button v-if="form.lines.length > 1" class="btn btn-sm btn-link text-danger p-0" @click="form.lines.splice(i, 1)">حذف</button></td>
              </tr>
              <tr class="fn-total"><td colspan="2">جمع دریافتی</td><td class="fn-num">{{ money(linesTotal()) }}</td><td colspan="2"></td></tr>
            </tbody>
          </table>
        </div>
        <button class="btn btn-sm btn-outline-secondary mb-3" @click="form.lines.push(blankLine())">+ ردیف</button>

        <div v-if="form.customer" class="mb-2">
          <div class="fn-section">تسویه فاکتورها</div>
          <label class="form-check small"><input v-model="form.fifo" class="form-check-input" type="checkbox" /> تسویه خودکار از قدیمی‌ترین فاکتور باز مشتری (FIFO)</label>
          <template v-if="!form.fifo">
            <div class="mb-1"><button v-for="inv in openInvoices" :key="inv.id" class="btn btn-sm btn-outline-secondary me-1 mb-1" @click="addAllocation(inv)">{{ inv.number }} - مانده {{ money(inv.openAmount) }}</button></div>
            <table v-if="form.allocations.length" class="table table-sm">
              <thead><tr><th>فاکتور</th><th>مبلغ</th><th></th></tr></thead>
              <tbody>
                <tr v-for="(a, i) in form.allocations" :key="a.invoiceId"><td class="inv-ltr">{{ a.invoiceNumber }}</td><td><input v-model="a.amount" class="form-control form-control-sm fn-num" /></td><td><button class="btn btn-sm btn-link text-danger p-0" @click="form.allocations.splice(i, 1)">حذف</button></td></tr>
                <tr class="fn-total"><td>جمع تخصیص</td><td class="fn-num">{{ money(allocTotal()) }}</td><td></td></tr>
              </tbody>
            </table>
          </template>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
