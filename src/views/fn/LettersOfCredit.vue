<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import { getBankAccounts, getLetterOfCredit, getLettersOfCredit, letterOfCreditAction, saveLetterOfCredit } from "@/api/FnAPIs";
import { getSuppliers } from "@/api/ProcurementAPIs";
import { fail, jalali, num, ok, today } from "@/composables/inventory";
import { camelize, money, parseAmount } from "@/composables/fn";
import { lcPostingKinds, lcStatuses } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Letters of credit (اعتبار اسنادی): open, pay the margin and fees, receive the documents, settle at the rate of the day, expire or cancel.
const list = ref<any[]>([]);
const detail = ref<any>(null);
const form = ref<any>(null);
const dialog = ref<null | { action: string; title: string; fields: any[] }>(null);
const banks = ref<any[]>([]);
const suppliers = ref<any[]>([]);
const busy = ref(false);
const canWrite = hasRole(FnRoles.treasuryAndAccountants);

const supplierName = (id: number) => suppliers.value.find((s) => s.providerId === id)?.providerName || String(id);
const bankTitle = (id: number) => banks.value.find((b) => b.id === id)?.title || String(id);
const badge = (s: number) => (s === 3 ? "ok" : s === 4 || s === 5 ? "bad" : "info");

async function load() {
  const [d, error] = await getLettersOfCredit();
  if (error) return fail(error);
  list.value = d || [];
}
async function open(id: number) {
  const [d, error] = await getLetterOfCredit(id);
  if (error) return fail(error);
  detail.value = d;
}

function edit(l?: any) {
  form.value = l
    ? { id: l.id, lcNumber: l.lcNumber, providerId: l.providerId, bankAccountId: l.bankAccountId, currencyCode: l.currencyCode, amount: String(l.amount), openDate: String(l.openDate).slice(0, 10), expiryDate: String(l.expiryDate).slice(0, 10), marginPercent: String(l.marginPercent), purchaseOrderId: l.purchaseOrderId, note: l.note || "" }
    : { id: null, lcNumber: "", providerId: null, bankAccountId: null, currencyCode: "", amount: "", openDate: today(), expiryDate: null, marginPercent: "0", purchaseOrderId: null, note: "" };
  detail.value = null;
}
async function save() {
  const f = form.value;
  const amount = parseAmount(f.amount);
  if (!String(f.lcNumber).trim() || !f.providerId || !f.bankAccountId || !String(f.currencyCode).trim() || !amount || !f.openDate || !f.expiryDate) return fail("شماره، تامین‌کننده، بانک، ارز، مبلغ و تاریخ‌ها را وارد کنید.");
  busy.value = true;
  const [d, error] = await saveLetterOfCredit({ id: f.id, lcNumber: String(f.lcNumber).trim(), providerId: Number(f.providerId), bankAccountId: Number(f.bankAccountId), currencyCode: String(f.currencyCode).trim().toUpperCase(), amount, openDate: f.openDate, expiryDate: f.expiryDate, marginPercent: parseAmount(f.marginPercent) || 0, purchaseOrderId: f.purchaseOrderId || null, note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("اعتبار اسنادی ذخیره شد.");
  form.value = null;
  detail.value = d;
  load();
}

function ask(action: string) {
  const dateField = { key: "date", label: "تاریخ", type: "date", value: today() };
  const noteField = { key: "note", label: "توضیح", type: "text" };
  const map: Record<string, any> = {
    margin: { title: "پرداخت سپرده (مارجین)", fields: [dateField, { key: "amount", label: "مبلغ (ریال) *", type: "number", required: true }, noteField] },
    fee: { title: "پرداخت کارمزد", fields: [dateField, { key: "amount", label: "مبلغ (ریال) *", type: "number", required: true }, noteField] },
    documents: { title: "دریافت اسناد حمل", fields: [{ key: "foreignInvoiceRef", label: "شماره فاکتور خارجی *", type: "text", required: true }] },
    pay: { title: "پرداخت و تسویه", fields: [dateField, { key: "rate", label: "نرخ تسویه *", type: "number", required: true }, noteField] },
    expire: { title: "منقضی‌کردن اعتبار", fields: [dateField] },
    cancel: { title: "لغو اعتبار و استرداد سپرده", fields: [dateField, noteField] },
  };
  dialog.value = { action, ...map[action] };
}
async function act(v: any) {
  const d = dialog.value!;
  dialog.value = null;
  busy.value = true;
  const [data, error] = await letterOfCreditAction({ id: detail.value.id, action: d.action, date: v.date || null, amount: parseAmount(v.amount) || 0, rate: parseAmount(v.rate), foreignInvoiceRef: v.foreignInvoiceRef || null, note: v.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  detail.value = data;
  load();
}

onMounted(async () => {
  const [b] = await getBankAccounts({ PageSize: 200 });
  banks.value = (b?.data || []).filter((x: any) => x.isActive);
  const [s] = await getSuppliers();
  suppliers.value = camelize(s) || [];
  load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">اعتبارات اسنادی (LC)</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="edit()">اعتبار جدید</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>شماره LC</th><th>تامین‌کننده</th><th>ارز</th><th>مبلغ ارزی</th><th>گشایش</th><th>انقضا</th><th>سپرده پرداخت‌شده (ریال)</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!list.length"><td colspan="9" class="text-center inv-muted">اعتباری ثبت نشده است.</td></tr>
          <tr v-for="l in list" :key="l.id" style="cursor: pointer" @click="open(l.id)">
            <td class="inv-ltr">{{ l.number }}</td><td class="inv-ltr">{{ l.lcNumber }}</td><td>{{ supplierName(l.providerId) }}</td><td class="inv-ltr">{{ l.currencyCode }}</td><td class="fn-num">{{ num(l.amount) }}</td>
            <td>{{ jalali(l.openDate) }}</td><td>{{ jalali(l.expiryDate) }}</td><td class="fn-num">{{ money(l.marginPaid) }}</td><td><span :class="badge(l.status)" class="inv-badge">{{ lcStatuses[l.status] }}</span></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">اعتبار <span class="inv-ltr">{{ detail.number }}</span> <span :class="badge(detail.status)" class="inv-badge">{{ lcStatuses[detail.status] }}</span></h2>
        <div class="row g-2 mb-2">
          <div class="col-md-4"><b>شماره LC:</b> <span class="inv-ltr">{{ detail.lcNumber }}</span></div>
          <div class="col-md-4"><b>تامین‌کننده:</b> {{ supplierName(detail.providerId) }}</div>
          <div class="col-md-4"><b>بانک:</b> {{ bankTitle(detail.bankAccountId) }}</div>
          <div class="col-md-4"><b>مبلغ:</b> <span class="inv-ltr">{{ num(detail.amount) }} {{ detail.currencyCode }}</span></div>
          <div class="col-md-4"><b>درصد سپرده:</b> {{ detail.marginPercent }}</div>
          <div class="col-md-4"><b>سپرده مانده / کارمزد:</b> {{ money(detail.marginPaid) }} / {{ money(detail.feesPaid) }}</div>
          <div class="col-md-4"><b>گشایش:</b> {{ jalali(detail.openDate) }}</div>
          <div class="col-md-4"><b>انقضا:</b> {{ jalali(detail.expiryDate) }}</div>
          <div v-if="detail.foreignInvoiceRef" class="col-md-4"><b>فاکتور خارجی:</b> <span class="inv-ltr">{{ detail.foreignInvoiceRef }}</span></div>
          <div v-if="detail.settlementRate" class="col-md-4"><b>نرخ تسویه:</b> {{ num(detail.settlementRate) }}</div>
          <div v-if="detail.settledBase" class="col-md-4"><b>مبلغ تسویه (ریال):</b> {{ money(detail.settledBase) }}</div>
          <div v-if="detail.paidDate" class="col-md-4"><b>تاریخ پرداخت:</b> {{ jalali(detail.paidDate) }}</div>
          <div v-if="detail.note" class="col-12"><b>توضیح:</b> {{ detail.note }}</div>
        </div>
        <table class="table table-sm">
          <thead><tr><th>نوع</th><th>تاریخ</th><th>مبلغ (ریال)</th><th>سند</th><th>توضیح</th></tr></thead>
          <tbody>
            <tr v-if="!detail.postings.length"><td colspan="5" class="text-center inv-muted">ثبتی انجام نشده است.</td></tr>
            <tr v-for="(p, i) in detail.postings" :key="i"><td>{{ lcPostingKinds[p.kind] }}</td><td>{{ jalali(p.date) }}</td><td class="fn-num">{{ money(p.amount) }}</td><td>{{ p.voucherId }}</td><td>{{ p.note }}</td></tr>
          </tbody>
        </table>
        <div class="d-flex gap-2 justify-content-end flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
          <template v-if="canWrite && (detail.status === 1 || detail.status === 2)">
            <button v-if="detail.status === 1" class="btn btn-sm btn-outline-primary" @click="edit(detail)">ویرایش</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="ask('margin')">سپرده</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="ask('fee')">کارمزد</button>
            <button v-if="detail.status === 1" :disabled="busy" class="btn btn-sm btn-outline-success" @click="ask('documents')">دریافت اسناد</button>
            <button v-if="detail.status === 2" :disabled="busy" class="btn btn-sm btn-success" @click="ask('pay')">پرداخت و تسویه</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="ask('expire')">منقضی</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="ask('cancel')">لغو</button>
          </template>
        </div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش اعتبار اسنادی" : "اعتبار اسنادی جدید" }}</h2>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">شماره LC *</label><input v-model="form.lcNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">ارز *</label><input v-model="form.currencyCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label class="form-label small mb-0">تامین‌کننده *</label><select v-model="form.providerId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="s in suppliers" :key="s.providerId" :value="s.providerId">{{ s.providerName }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">حساب بانکی *</label><select v-model="form.bankAccountId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">مبلغ ارزی *</label><input v-model="form.amount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-6"><label class="form-label small mb-0">درصد سپرده</label><input v-model="form.marginPercent" class="form-control form-control-sm fn-num" /></div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ گشایش *</label><FnDate v-model="form.openDate" /></div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ انقضا *</label><FnDate v-model="form.expiryDate" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>

    <PromptModal v-if="dialog" :fields="dialog.fields" :title="dialog.title" @cancel="dialog = null" @confirm="act" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
