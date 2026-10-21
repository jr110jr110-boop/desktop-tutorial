<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import PaymentPrint from "@/components/fn/print/PaymentPrint.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import {
  getBankAccounts, getCashBoxes, getIssuedCheques, getPayableRequests, getPayment, getPayments, getPayrollRuns, getPettyCashes, paymentAction, savePayment,
} from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { badgeOfDoc, docStatuses, instruments, money, parseAmount, paymentPurposes, printSheet, usePagedList } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Payments: from the approved supplier payment requests (grouped by supplier), petty cash top-ups and payroll.
// Draft (treasury) -> approved (finance manager, not the person who made it) -> posted (treasury, with the bank tracking number).
const tab = ref("payments");
const tabs = [{ key: "payments", title: "اسناد پرداخت" }, { key: "requests", title: "درخواست‌های پرداخت منتظر خزانه" }];
const filters = ref({ Status: null as number | null, From: null as string | null, To: null as string | null, Contains: "" });
const list = usePagedList(getPayments, filters);
const reqFilters = ref({ Contains: "" });
const requests = usePagedList(getPayableRequests, reqFilters);
const banks = ref<any[]>([]);
const boxes = ref<any[]>([]);
const petties = ref<any[]>([]);
const runs = ref<any[]>([]);
const form = ref<any>(null);
const detail = ref<any>(null);
const leaves = ref<Record<number, any[]>>({});
const dialog = ref<null | { action: string; title: string; fields: any[] }>(null);
const busy = ref(false);
const printing = ref(false);
const canWrite = hasRole(FnRoles.treasury);
const canApprove = hasRole(FnRoles.managers);

const selectedRequests = computed(() => (form.value ? requests.rows.value.filter((r) => form.value.requestIds.includes(r.id)) : []));
const formNet = computed(() => {
  const f = form.value;
  if (!f) return 0;
  if (f.purpose === 1) return selectedRequests.value.reduce((s, r) => s + (r.netAmount || 0), 0);
  if (f.purpose === 2) return parseAmount(f.topUpAmount) || 0;
  return f.runNet || 0;
});
const linesTotal = computed(() => (form.value ? form.value.lines.reduce((s: number, l: any) => s + (parseAmount(l.amount) || 0), 0) : 0));
const supplierId = computed(() => selectedRequests.value[0]?.providerId || null);

const blankLine = (): any => ({ instrument: 2, amount: "", bankAccountId: null, cashBoxId: null, reference: "", payeeIban: "", issuedChequeId: null, chequeDueDate: null, chequeSayadId: "" });

async function loadRefs() {
  const [b] = await getBankAccounts({ PageSize: 200 });
  banks.value = (b?.data || []).filter((x: any) => x.isActive);
  const [c] = await getCashBoxes({ PageSize: 200 });
  boxes.value = (c?.data || []).filter((x: any) => x.isActive);
  const [p] = await getPettyCashes({ PageSize: 200 });
  petties.value = (p?.data || []).filter((x: any) => x.isActive);
}

async function newPayment(requestIds: number[] = []) {
  await requests.load();
  form.value = { id: null, date: today(), purpose: 1, requestIds, pettyCashId: null, topUpAmount: "", payrollRunId: null, runNet: 0, description: "", lines: [blankLine()] };
  runs.value = [];
  tab.value = "payments";
}

async function loadRuns() {
  // only the payroll roles may read the runs; the others type the number of the run
  const [data] = await getPayrollRuns({ Status: 3, PageSize: 50 });
  runs.value = data?.data || [];
}

function onPurpose() {
  const f = form.value;
  f.lines = [blankLine()];
  if (f.purpose === 3) loadRuns();
}

function pickRun() {
  const r = runs.value.find((x) => x.id === form.value.payrollRunId);
  form.value.runNet = r ? r.totalNet : 0;
}

function toggleRequest(id: number) {
  const ids = form.value.requestIds;
  const i = ids.indexOf(id);
  if (i >= 0) ids.splice(i, 1);
  else ids.push(id);
}

function fillRest(l: any) {
  const rest = formNet.value - linesTotal.value + (parseAmount(l.amount) || 0);
  if (rest > 0) l.amount = String(rest);
}

async function onInstrument(l: any) {
  if (l.instrument === 2 && form.value.purpose === 1 && !l.payeeIban) l.payeeIban = selectedRequests.value[0]?.supplierIban || "";
}

async function loadLeaves(l: any) {
  l.issuedChequeId = null;
  if (!l.bankAccountId) return;
  if (!leaves.value[l.bankAccountId]) {
    const [data] = await getIssuedCheques({ BankAccountId: l.bankAccountId, Status: 0, PageSize: 100 });
    leaves.value[l.bankAccountId] = data?.data || [];
  }
}

async function open(id: number) {
  const [data, error] = await getPayment(id);
  if (error) return fail(error);
  detail.value = data;
}

async function edit() {
  const d = detail.value;
  await requests.load();
  form.value = {
    id: d.id, date: String(d.date).slice(0, 10), purpose: d.purpose, requestIds: (d.requests || []).map((r: any) => r.paymentRequestId), pettyCashId: d.pettyCashId, topUpAmount: d.purpose === 2 ? String(d.netAmount) : "",
    payrollRunId: d.payrollRunId, runNet: d.purpose === 3 ? d.netAmount : 0, description: d.description || "",
    lines: d.lines.map((l: any) => ({
      instrument: l.instrument, amount: String(l.amount), bankAccountId: l.bankAccountId, cashBoxId: l.cashBoxId, reference: l.reference || "", payeeIban: l.payeeIban || "",
      issuedChequeId: l.issuedChequeId, chequeDueDate: l.chequeDueDate ? String(l.chequeDueDate).slice(0, 10) : null, chequeSayadId: l.chequeSayadId || "",
    })),
  };
  // the requests of a draft are still "approved but unpaid" in the queue; the ones already in this payment are added from the document
  for (const r of d.requests || []) {
    if (!requests.rows.value.some((x) => x.id === r.paymentRequestId))
      requests.rows.value.push({ id: r.paymentRequestId, number: r.paymentRequestNumber, providerId: d.providerId, providerName: d.providerName, amount: r.amount, deductions: r.deductions, netAmount: r.amount - r.deductions, supplierIban: "" });
  }
  detail.value = null;
}

async function save() {
  const f = form.value;
  if (formNet.value <= 0) return fail("مبلغ خالص پرداخت صفر است.");
  if (linesTotal.value !== formNet.value) return fail(`جمع ردیف‌های پرداخت (${money(linesTotal.value)}) با مبلغ خالص (${money(formNet.value)}) برابر نیست.`);
  const lines = f.lines.map((l: any) => ({
    instrument: Number(l.instrument), amount: parseAmount(l.amount), bankAccountId: l.instrument === 1 ? null : l.bankAccountId, cashBoxId: l.instrument === 1 ? l.cashBoxId : null, reference: l.reference || null,
    payeeIban: l.instrument === 2 ? l.payeeIban || null : null, issuedChequeId: l.instrument === 3 ? l.issuedChequeId : null, chequeDueDate: l.instrument === 3 ? l.chequeDueDate : null, chequeSayadId: l.instrument === 3 ? l.chequeSayadId || null : null,
  }));
  busy.value = true;
  const [data, error] = await savePayment({
    id: f.id, date: f.date, purpose: Number(f.purpose), paymentRequestIds: f.purpose === 1 ? f.requestIds : [], pettyCashId: f.purpose === 2 ? f.pettyCashId : null,
    payrollRunId: f.purpose === 3 ? f.payrollRunId : null, topUpAmount: f.purpose === 2 ? parseAmount(f.topUpAmount) : null, description: f.description || null, lines,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("سند پرداخت ذخیره شد.");
  form.value = null;
  detail.value = data;
  list.load();
  requests.load();
}

function ask(action: string) {
  if (action === "approve") dialog.value = { action, title: "تایید سند پرداخت", fields: [{ key: "note", label: "علت پذیرش (فقط اگر شبای گیرنده با شبای ثبت‌شده فرق دارد)", type: "textarea" }] };
  else if (action === "post") dialog.value = { action, title: "ثبت نهایی پرداخت", fields: [{ key: "reference", label: "شماره پیگیری حواله بانکی (برای ردیف‌های حواله)", type: "text" }] };
  else act(action, {});
}

async function act(action: string, v: any) {
  dialog.value = null;
  busy.value = true;
  const [data, error] = await paymentAction({ id: detail.value.id, action, note: v.note, reference: v.reference });
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "سند تایید شد." : action === "post" ? "پرداخت ثبت نهایی شد." : "سند باطل شد.");
  detail.value = data;
  list.load();
  requests.load();
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
  requests.load();
});
</script>

<template>
  <div class="inv-page">
    <PaymentPrint v-if="printing && detail" :banks="banks" :boxes="boxes" :p="detail" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">پرداخت‌ها</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="newPayment()">سند پرداخت جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'payments'">
        <div class="inv-filters mb-3">
          <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in docStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
          <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
          <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا نام گیرنده" @keydown.enter="list.search" />
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>تاریخ</th><th>بابت</th><th>گیرنده</th><th>خالص (ریال)</th><th>وضعیت</th><th>تهیه‌کننده</th></tr></thead>
            <tbody>
              <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">سند پرداختی نیست.</td></tr>
              <tr v-for="p in list.rows.value" :key="p.id" style="cursor: pointer" @click="open(p.id)">
                <td class="inv-ltr">{{ p.number }}</td><td>{{ jalali(p.date) }}</td><td>{{ paymentPurposes[p.purpose] }}</td><td>{{ p.payeeName }}</td><td class="fn-num">{{ money(p.netAmount) }}</td>
                <td><span :class="badgeOfDoc(p.status)" class="inv-badge">{{ docStatuses[p.status] }}</span></td><td>{{ p.creatorName }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      </template>

      <template v-else>
        <p class="inv-muted">درخواست‌های پرداخت تاییدشده‌ای که هنوز سند پرداخت ندارند. درخواست‌های یک تامین‌کننده را انتخاب و سند پرداخت بسازید.</p>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تامین‌کننده</th><th>تاریخ</th><th>شماره فاکتور</th><th>مبلغ</th><th>کسورات</th><th>خالص (ریال)</th><th>شبا</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!requests.rows.value.length"><td colspan="9" class="text-center inv-muted">درخواستی در انتظار نیست.</td></tr>
            <tr v-for="r in requests.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ r.providerName }}</td><td>{{ jalali(r.requestDate) }}</td><td class="inv-ltr">{{ r.invoiceNumber }}</td>
              <td class="fn-num">{{ money(r.amount) }}</td><td class="fn-num">{{ money(r.deductions) }}</td><td class="fn-num">{{ money(r.netAmount) }}</td><td class="inv-ltr">{{ r.supplierIban }}</td>
              <td><button v-if="canWrite && !r.paymentId" class="btn btn-sm btn-outline-primary" @click="newPayment([r.id])">سند پرداخت</button></td>
            </tr>
          </tbody>
        </table>
        <QPager :page="requests.page.value" :total="requests.total.value" @go="requests.go" />
      </template>
    </div>

    <!-- detail -->
    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">سند پرداخت <span class="inv-ltr">{{ detail.number }}</span> <span :class="badgeOfDoc(detail.status)" class="inv-badge">{{ docStatuses[detail.status] }}</span></h2>
        <div class="row g-2 mb-2">
          <div class="col-md-3"><b>تاریخ:</b> {{ jalali(detail.date) }}</div>
          <div class="col-md-3"><b>بابت:</b> {{ paymentPurposes[detail.purpose] }}</div>
          <div class="col-md-6"><b>گیرنده:</b> {{ detail.payeeName }}</div>
          <div class="col-md-3"><b>ناخالص:</b> {{ money(detail.grossAmount) }}</div>
          <div class="col-md-3"><b>کسورات:</b> {{ money(detail.deductionAmount) }}</div>
          <div class="col-md-3"><b>خالص:</b> {{ money(detail.netAmount) }}</div>
          <div class="col-md-3"><b>تهیه‌کننده:</b> {{ detail.creatorName }}</div>
          <div v-if="detail.description" class="col-12"><b>توضیح:</b> {{ detail.description }}</div>
          <div v-if="detail.ibanApprovedByUserId" class="col-12"><span class="inv-badge warn">اختلاف شبا با نظر مدیر مالی پذیرفته شده است.</span></div>
        </div>
        <table v-if="detail.requests && detail.requests.length" class="table table-sm">
          <thead><tr><th>درخواست پرداخت</th><th>مبلغ</th><th>کسورات</th></tr></thead>
          <tbody><tr v-for="r in detail.requests" :key="r.paymentRequestId"><td class="inv-ltr">{{ r.paymentRequestNumber }}</td><td class="fn-num">{{ money(r.amount) }}</td><td class="fn-num">{{ money(r.deductions) }}</td></tr></tbody>
        </table>
        <table class="table table-sm">
          <thead><tr><th>روش</th><th>بانک / صندوق</th><th>شبا / سریال چک</th><th>سررسید</th><th>شماره پیگیری</th><th>مبلغ</th></tr></thead>
          <tbody>
            <tr v-for="(l, i) in detail.lines" :key="i">
              <td>{{ instruments[l.instrument] }}</td><td>{{ bankTitle(l.bankAccountId) || boxTitle(l.cashBoxId) }}</td><td class="inv-ltr">{{ l.payeeIban || l.chequeSerial }}</td>
              <td>{{ l.chequeDueDate ? jalali(l.chequeDueDate) : "" }}</td><td class="inv-ltr">{{ l.reference }}</td><td class="fn-num">{{ money(l.amount) }}</td>
            </tr>
          </tbody>
        </table>
        <p v-if="detail.postedByName" class="inv-muted">ثبت نهایی: {{ detail.postedByName }} - {{ jalali(detail.postedAt, true) }}</p>
        <div class="d-flex gap-2 justify-content-end flex-wrap">
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="print">چاپ سند پرداخت</button>
          <button v-if="canWrite && detail.status === 0" class="btn btn-sm btn-outline-primary" @click="edit">ویرایش</button>
          <button v-if="canApprove && detail.status === 0" :disabled="busy" class="btn btn-sm btn-success" @click="ask('approve')">تایید (مدیر مالی)</button>
          <button v-if="canWrite && detail.status === 1" :disabled="busy" class="btn btn-sm btn-success" @click="ask('post')">ثبت نهایی پرداخت</button>
          <button v-if="canWrite && (detail.status === 0 || detail.status === 1)" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="ask('cancel')">ابطال</button>
        </div>
      </div>
    </div>

    <!-- form -->
    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش سند پرداخت" : "سند پرداخت جدید" }}</h2>
        <div class="row g-2 mb-2">
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="form.date" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">بابت</label><select v-model="form.purpose" :disabled="!!form.id" class="form-select form-select-sm" @change="onPurpose"><option v-for="(t, k) in paymentPurposes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-5"><label class="form-label small mb-0">توضیح</label><input v-model="form.description" class="form-control form-control-sm" /></div>
        </div>

        <template v-if="form.purpose === 1">
          <div class="fn-section">درخواست‌های پرداخت تاییدشده (یک تامین‌کننده)</div>
          <table class="table table-sm">
            <thead><tr><th></th><th>شماره</th><th>تامین‌کننده</th><th>فاکتور</th><th>خالص (ریال)</th></tr></thead>
            <tbody>
              <tr v-if="!requests.rows.value.length"><td colspan="5" class="text-center inv-muted">درخواست تاییدشده‌ای نیست.</td></tr>
              <tr v-for="r in requests.rows.value" :key="r.id" :class="{ 'opacity-50': supplierId && r.providerId !== supplierId }">
                <td><input :checked="form.requestIds.includes(r.id)" :disabled="(supplierId && r.providerId !== supplierId) || (r.paymentId && !form.requestIds.includes(r.id))" type="checkbox" @change="toggleRequest(r.id)" /></td>
                <td class="inv-ltr">{{ r.number }}</td><td>{{ r.providerName }}</td><td class="inv-ltr">{{ r.invoiceNumber }}</td><td class="fn-num">{{ money(r.netAmount) }}</td>
              </tr>
            </tbody>
          </table>
        </template>
        <div v-else-if="form.purpose === 2" class="row g-2 mb-2">
          <div class="col-md-6"><label class="form-label small mb-0">تنخواه *</label><select v-model="form.pettyCashId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="p in petties" :key="p.id" :value="p.id">{{ p.holderName }} (مانده {{ money(p.balance) }} از سقف {{ money(p.ceiling) }})</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">مبلغ شارژ (ریال) *</label><input v-model="form.topUpAmount" class="form-control form-control-sm fn-num" /></div>
        </div>
        <div v-else class="row g-2 mb-2">
          <div class="col-md-6">
            <label class="form-label small mb-0">دوره حقوق ثبت‌شده در دفاتر *</label>
            <select v-if="runs.length" v-model="form.payrollRunId" class="form-select form-select-sm" @change="pickRun"><option :value="null">-</option><option v-for="r in runs" :key="r.id" :value="r.id">{{ r.number }} - {{ r.year }}/{{ r.month }}</option></select>
            <input v-else v-model.number="form.payrollRunId" class="form-control form-control-sm" placeholder="شناسه دوره حقوق" type="number" />
          </div>
          <div class="col-md-6 d-flex align-items-end"><span class="inv-muted">مبلغ پرداخت برابر خالص حقوق دوره است و بدون نمایش اطلاعات فردی پرداخت می‌شود.</span></div>
        </div>

        <div class="fn-section">ردیف‌های پرداخت (جمع باید برابر خالص باشد: {{ money(formNet) }})</div>
        <div class="table-responsive">
          <table class="table table-sm fn-lines">
            <thead><tr><th>روش</th><th>بانک / صندوق</th><th>مبلغ</th><th>جزئیات</th><th></th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in form.lines" :key="i">
                <td>
                  <select v-model="l.instrument" class="form-select form-select-sm" @change="onInstrument(l)">
                    <option :value="1">نقد</option><option :value="2">حواله بانکی</option><option :value="3" :disabled="form.purpose !== 1">چک</option><option :value="4">کارت</option>
                  </select>
                </td>
                <td>
                  <select v-if="l.instrument === 1" v-model="l.cashBoxId" class="form-select form-select-sm"><option :value="null">صندوق...</option><option v-for="b in boxes" :key="b.id" :value="b.id">{{ b.title }}</option></select>
                  <select v-else v-model="l.bankAccountId" class="form-select form-select-sm" @change="l.instrument === 3 && loadLeaves(l)"><option :value="null">حساب بانکی...</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select>
                </td>
                <td><input v-model="l.amount" class="form-control form-control-sm fn-num" @focus="!l.amount && fillRest(l)" /></td>
                <td>
                  <input v-if="l.instrument === 2 && form.purpose === 1" v-model="l.payeeIban" class="form-control form-control-sm inv-ltr" placeholder="شبای گیرنده (خالی = شبای ثبت‌شده)" />
                  <div v-if="l.instrument === 3" class="d-flex gap-1 flex-wrap">
                    <select v-model="l.issuedChequeId" class="form-select form-select-sm" style="width: 150px"><option :value="null">برگ چک...</option><option v-for="c in leaves[l.bankAccountId] || []" :key="c.id" :value="c.id">{{ c.series }} {{ c.serial }}</option></select>
                    <FnDate v-model="l.chequeDueDate" />
                    <input v-model="l.chequeSayadId" class="form-control form-control-sm inv-ltr" placeholder="شناسه صیادی ۱۶ رقم" style="width: 170px" />
                  </div>
                </td>
                <td><button v-if="form.lines.length > 1" class="btn btn-sm btn-link text-danger p-0" @click="form.lines.splice(i, 1)">حذف</button></td>
              </tr>
              <tr class="fn-total"><td colspan="2">جمع ردیف‌ها</td><td class="fn-num">{{ money(linesTotal) }}</td><td colspan="2"><span :class="linesTotal === formNet && formNet > 0 ? 'ok' : 'bad'" class="inv-badge">{{ linesTotal === formNet && formNet > 0 ? "برابر خالص" : "اختلاف " + money(formNet - linesTotal) }}</span></td></tr>
            </tbody>
          </table>
        </div>
        <button class="btn btn-sm btn-outline-secondary" @click="form.lines.push(blankLine())">+ ردیف</button>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button>
        </div>
      </div>
    </div>

    <PromptModal v-if="dialog" :fields="dialog.fields" :title="dialog.title" @cancel="dialog = null" @confirm="(v: any) => act(dialog!.action, v)" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
