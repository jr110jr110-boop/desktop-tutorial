<script setup lang="ts">
import FnDate from "@/components/fn/FnDate.vue";
import ContractPick from "@/components/fn/ContractPick.vue";
import { getPurchaseOrders } from "@/api/ProcurementAPIs";
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import Pb6Dialog from "@/components/common/Pb6Dialog.vue";
import Pb6Table from "@/components/common/Pb6Table.vue";
import { getCashFlow, getCbs, getLegalRates, getStatement, getStatementAnalytics, getStatementPrint, getStatements, legalRateAction, recordStatementPayment, saveCashPlan, saveCbs, saveLegalRate, saveStatement, statementAction } from "@/api/Pb6APIs";
import { fail, jalali, ok, printPage, rial, today } from "@/composables/inventory";
import { hasRole, ProcurementRoles } from "@/composables/roles";

// Progress statements (پیوست ۷ and the company form): preparation by commercial, technical office, supervision, authorisation by the project manager, payment.
const route = useRoute();
const tab = ref<"list" | "rates" | "cbs" | "cash" | "analytics">("list");
const rows = ref<any[]>([]);
const current = ref<any>(null);
const printing = ref<any>(null);
const rates = ref<any[]>([]);
const analytics = ref<any>(null);
const cash = ref<any>(null);
const cbs = ref<any[]>([]);
const scope = ref({ purchaseOrderId: null as number | null, contractId: null as number | null });
const note = ref("");
const amount = ref<number | null>(null);
const busy = ref(false);

const statuses: Record<number, string> = { 0: "پیش‌نویس", 1: "آماده شد", 2: "تایید دفتر فنی", 3: "تایید نظارت", 4: "مجوز پرداخت (مدیر پروژه)", 5: "درخواست پرداخت", 6: "پرداخت شد", 8: "برگشت خورد", 9: "رد شد" };
const rateStatuses: Record<number, string> = { 0: "پیش‌نویس (تایید نشده)", 1: "تاییدشده", 2: "کنار گذاشته" };

const form = ref<any>(blank());
function blank() {
  return { side: 1, purchaseOrderId: null, contractId: null, periodFrom: today(), periodTo: today(), subject: "", supportingDocuments: "", lines: [line()], deductions: [], manualPrevious: [] };
}
function line() {
  return { title: "", quantity: 1, unitPrice: 0, advanceAmount: 0, afterDeliveryQcAmount: 0, goodPerformanceAmount: 0, afterFinalTestsAmount: 0 };
}
const gross = computed(() => form.value.lines.reduce((s: number, l: any) => s + (Number(l.advanceAmount) || 0) + (Number(l.afterDeliveryQcAmount) || 0) + (Number(l.goodPerformanceAmount) || 0) + (Number(l.afterFinalTestsAmount) || 0), 0));
const verifiedRates = computed(() => rates.value.filter((r) => r.status === 1));

async function load() {
  current.value = null;
  printing.value = null;
  if (tab.value === "list") {
    const [d, e] = await getStatements();
    if (e) return fail(e);
    rows.value = (d || []).map((r: any) => ({ ...r, statusText: statuses[r.status], sideText: r.side === 1 ? "تامین‌کننده/پیمانکار" : "کارفرما" }));
  } else if (tab.value === "analytics") {
    const [d, e] = await getStatementAnalytics();
    if (e) return fail(e);
    analytics.value = d;
  }
  const [r] = await getLegalRates();
  rates.value = r || [];
}

async function open(r: any) {
  const [d, e] = await getStatement(r.id);
  if (e) return fail(e);
  current.value = d;
  amount.value = null;
  note.value = "";
}

async function create() {
  busy.value = true;
  const [d, e] = await saveStatement(form.value);
  busy.value = false;
  if (e) return fail(e);
  ok("صورت‌وضعیت ذخیره شد.");
  await load();
  current.value = d;
}

async function doAction(action: string, extra: any = {}) {
  busy.value = true;
  const [d, e] = await statementAction(current.value.id, action, { note: note.value || null, amount: amount.value, ...extra });
  busy.value = false;
  if (e) return fail(e);
  ok("انجام شد.");
  current.value = d;
  load().then(() => (current.value = d));
}

const pay = ref<any>({ payMethod: 1, chequeNumber: "", receivedAmount: 0, paidDate: today() });
async function recordPay() {
  const [d, e] = await recordStatementPayment({ id: current.value.id, ...pay.value });
  if (e) return fail(e);
  ok("پرداخت ثبت شد.");
  current.value = d;
}

async function showPrint() {
  const [d, e] = await getStatementPrint(current.value.id);
  if (e) return fail(e);
  printing.value = d;
}

function addLegal() {
  form.value.deductions.push({ kind: 2, legalRateId: verifiedRates.value[0]?.id ?? null });
}
function addNcr() {
  form.value.deductions.push({ kind: 1, title: "", itemTitle: "", quantity: 1, unitPrice: 0, ncrId: null });
}

// rates
const rate = ref<any>({ code: "", title: "", percent: 0, deductionKind: 1, sourceReference: "", basisNote: "" });
async function createRate() {
  const [, e] = await saveLegalRate(rate.value);
  if (e) return fail(e);
  ok("نرخ ثبت شد؛ نفر دوم باید آن را تایید کند.");
  load();
}
const retireDlg = ref<{ open: boolean; row: any }>({ open: false, row: null });
async function sendRateAction(r: any, a: string, reason?: string) {
  const [, e] = await legalRateAction(r.id, a, reason);
  if (e) return fail(e);
  ok("انجام شد.");
  load();
}
function rateAct(r: any, a: string) {
  if (a === "retire") retireDlg.value = { open: true, row: r };
  else sendRateAction(r, a);
}

// cbs and cash flow
async function loadScope() {
  const [c, e] = await getCbs(scope.value);
  if (e) return fail(e);
  cbs.value = c || [];
  const [f, e2] = await getCashFlow(scope.value);
  if (e2) return fail(e2);
  cash.value = f;
}
async function saveCbsItems() {
  const [c, e] = await saveCbs({ ...scope.value, items: cbs.value.map((i) => ({ code: i.code, title: i.title, amount: i.amount, wbsNodeId: i.wbsNodeId })) });
  if (e) return fail(e);
  cbs.value = c;
  ok("CBS ذخیره شد.");
}
const planRows = ref<any[]>([]);
async function savePlan() {
  const [f, e] = await saveCashPlan({ ...scope.value, items: planRows.value.filter((p) => p.amount > 0).map((p) => ({ direction: p.direction, dueDate: p.dueDate, amount: p.amount, note: p.note || null })) });
  if (e) return fail(e);
  cash.value = f;
  ok("برنامه نقدی ذخیره شد (برنامه قبلی این قرارداد جایگزین شد).");
}

// Purchase orders for the order select (the list the procurement pages use).
const orders = ref<any[]>([]);
// the order list is closed to roles outside the procurement readers (403): it is not asked for, and the select is replaced by a note
const canOrders = hasRole(ProcurementRoles.orderReaders);
async function loadOrders() {
  if (!canOrders) return;
  const [data] = await getPurchaseOrders({ PageSize: 200 });
  orders.value = data?.data || [];
}

onMounted(() => {
  loadOrders();
  if (route.query.id) getStatement(Number(route.query.id)).then(([d]) => (current.value = d));
  load();
});
function switchTab(t: any) {
  tab.value = t;
  load();
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">صورت‌وضعیت‌ها (پیوست ۷)</h1></div>
      <div class="btn-group mb-3 d-print-none">
        <button v-for="t in [['list','صورت‌وضعیت‌ها'],['rates','نرخ کسورات قانونی'],['cbs','CBS و WBS'],['cash','جریان نقدی'],['analytics','تحلیل']]" :key="t[0]" class="btn btn-sm" :class="tab === t[0] ? 'btn-primary' : 'btn-outline-primary'" @click="switchTab(t[0])">{{ t[1] }}</button>
      </div>

      <template v-if="tab === 'list'">
        <Pb6Table :columns="[{ key: 'number', title: 'شماره' }, { key: 'sideText', title: 'سمت' }, { key: 'providerName', title: 'طرف قرارداد' }, { key: 'serial', title: 'سریال' }, { key: 'grossThisPeriod', title: 'ناخالص دوره', kind: 'money' }, { key: 'payableThisPeriod', title: 'قابل پرداخت', kind: 'money' }, { key: 'statusText', title: 'وضعیت' }]" :rows="rows" @select="open" />

        <div v-if="!current" class="inv-card mt-3 d-print-none">
          <h2 class="h6">صورت‌وضعیت جدید (بازرگانی)</h2>
          <div class="inv-filters">
            <select v-model.number="form.side" class="form-select form-select-sm"><option :value="1">تامین‌کننده/پیمانکار</option><option :value="2">کارفرما</option></select>
            <select v-if="canOrders" v-model="form.purchaseOrderId" class="form-select form-select-sm"><option :value="null">سفارش (قرارداد) — انتخاب کنید</option><option v-for="o in orders" :key="o.id" :value="o.id">{{ o.number }} - {{ o.providerName }}</option></select>
            <span v-else class="small inv-muted align-self-center">فهرست سفارش‌ها برای نقش شما در دسترس نیست.</span>
            <ContractPick v-model="form.contractId" placeholder="قرارداد پروژه — انتخاب کنید" />
            <FnDate v-model="form.periodFrom" />
            <FnDate v-model="form.periodTo" />
            <input v-model="form.subject" class="form-control form-control-sm" placeholder="موضوع" />
          </div>
          <table class="table table-sm mt-2"><thead><tr><th>شرح</th><th>تعداد</th><th>قیمت واحد</th><th>پیش‌پرداخت</th><th>پس از تحویل و QC</th><th>عملکرد خوب</th><th>پس از آزمون نهایی</th><th>CBS</th></tr></thead><tbody>
            <tr v-for="(l, i) in form.lines" :key="i">
              <td><input v-model="l.title" class="form-control form-control-sm" /></td>
              <td><input v-model.number="l.quantity" type="number" class="form-control form-control-sm inv-ltr" /></td>
              <td><input v-model.number="l.unitPrice" type="number" class="form-control form-control-sm inv-ltr" /></td>
              <td><input v-model.number="l.advanceAmount" type="number" class="form-control form-control-sm inv-ltr" /></td>
              <td><input v-model.number="l.afterDeliveryQcAmount" type="number" class="form-control form-control-sm inv-ltr" /></td>
              <td><input v-model.number="l.goodPerformanceAmount" type="number" class="form-control form-control-sm inv-ltr" /></td>
              <td><input v-model.number="l.afterFinalTestsAmount" type="number" class="form-control form-control-sm inv-ltr" /></td>
              <td><input v-model.number="l.cbsItemId" type="number" class="form-control form-control-sm inv-ltr" /></td>
            </tr>
          </tbody></table>
          <button class="btn btn-sm btn-outline-secondary" @click="form.lines.push(line())">ردیف دیگر</button>
          <p class="mt-2">ناخالص این دوره: <strong>{{ rial(gross) }}</strong> ریال</p>
          <h3 class="h6">کسورات</h3>
          <div v-for="(d, i) in form.deductions" :key="i" class="inv-filters mb-1">
            <template v-if="d.kind === 1">
              <input v-model="d.itemTitle" class="form-control form-control-sm" placeholder="کالای معیوب" />
              <input v-model.number="d.quantity" type="number" class="form-control form-control-sm inv-ltr" />
              <input v-model.number="d.unitPrice" type="number" class="form-control form-control-sm inv-ltr" placeholder="قیمت واحد" />
              <input v-model.number="d.ncrId" type="number" class="form-control form-control-sm inv-ltr" placeholder="شناسه گزارش عدم انطباق" />
              <input v-model="d.letterNumber" class="form-control form-control-sm" placeholder="شماره نامه" />
            </template>
            <template v-else>
              <select v-model.number="d.legalRateId" class="form-select form-select-sm"><option v-for="r in verifiedRates" :key="r.id" :value="r.id">{{ r.title }} ({{ r.percent }}٪)</option></select>
            </template>
          </div>
          <button class="btn btn-sm btn-outline-secondary" @click="addNcr">کسر عدم انطباق</button>
          <button class="btn btn-sm btn-outline-secondary ms-2" :disabled="!verifiedRates.length" :title="verifiedRates.length ? '' : 'نرخ تاییدشده‌ای در جدول نیست'" @click="addLegal">کسر قانونی</button>
          <div class="mt-2"><button class="btn btn-sm btn-primary" :disabled="busy" @click="create">ذخیره</button></div>
        </div>

        <div v-if="current" class="inv-card mt-3">
          <div class="d-flex justify-content-between d-print-none"><h2 class="h6">{{ current.number }} — {{ statuses[current.status] }}</h2><button class="btn btn-sm btn-outline-secondary" @click="current = null">بستن</button></div>
          <p>دوره {{ jalali(current.periodFrom) }} تا {{ jalali(current.periodTo) }} — مبلغ قرارداد: {{ rial(current.contractAmount) }} — پرداخت‌های قبلی: {{ rial(current.previousPayments) }}</p>
          <p>ناخالص: {{ rial(current.grossThisPeriod) }} — کسر عدم انطباق: {{ rial(current.nonconformityDeductions) }} — کسر قانونی: {{ rial(current.legalDeductions) }} — <strong>قابل پرداخت: {{ rial(current.payableThisPeriod) }}</strong></p>
          <ul v-if="current.warnings?.length" class="text-warning small"><li v-for="w in current.warnings" :key="w">{{ w }}</li></ul>
          <p v-if="current.returnReason" class="text-danger">علت برگشت/رد: {{ current.returnReason }}</p>
          <table class="table table-sm"><thead><tr><th>#</th><th>شرح</th><th>جمع این دوره</th><th>پیشرفت WBS</th></tr></thead><tbody>
            <tr v-for="l in current.lines" :key="l.id"><td>{{ l.rowNumber }}</td><td>{{ l.title }} <span class="inv-ltr inv-muted">{{ l.cbsCode }}</span></td><td class="num">{{ rial(l.thisPeriodAmount) }}</td><td>{{ l.progressPercent ?? "" }}</td></tr>
          </tbody></table>
          <table v-if="current.deductions?.length" class="table table-sm"><thead><tr><th>کسر</th><th>شرح</th><th>مبلغ</th></tr></thead><tbody>
            <tr v-for="d in current.deductions" :key="d.id"><td>{{ d.kind === 1 ? "عدم انطباق " + (d.ncrNumber || "") : "قانونی" }}</td><td>{{ d.title || d.itemTitle || d.rateTitle }}</td><td class="num">{{ rial(d.amount) }}</td></tr>
          </tbody></table>
          <p class="small">تهیه: {{ current.preparedByName }} | دفتر فنی: {{ current.technicalApprovedByName }} {{ rial(current.technicalApprovedAmount) }} | نظارت: {{ current.supervisionApprovedByName }} {{ rial(current.supervisionApprovedAmount) }} | مجوز مدیر پروژه: {{ current.paymentAuthorisedByName }}</p>
          <div class="d-print-none d-flex gap-2 flex-wrap">
            <input v-model="note" class="form-control form-control-sm w-auto" placeholder="توضیح / علت" />
            <input v-model.number="amount" type="number" class="form-control form-control-sm w-auto inv-ltr" placeholder="مبلغ تاییدی (اختیاری)" />
            <button v-if="current.status === 0 || current.status === 8" class="btn btn-sm btn-primary" @click="doAction('prepare')">آماده‌سازی</button>
            <button v-if="current.status === 1" class="btn btn-sm btn-success" @click="doAction('technical-approve')">تایید دفتر فنی</button>
            <button v-if="current.status === 2" class="btn btn-sm btn-success" @click="doAction('supervision-approve')">تایید نظارت</button>
            <button v-if="current.status === 3" class="btn btn-sm btn-success" @click="doAction('authorise-payment')">مجوز پرداخت (مدیر پروژه)</button>
            <button v-if="current.status === 4 && current.side === 1" class="btn btn-sm btn-primary" @click="doAction('request-payment')">درخواست پرداخت</button>
            <button v-if="current.status >= 1 && current.status <= 4" class="btn btn-sm btn-warning" @click="doAction('return')">برگشت</button>
            <button v-if="current.status < 5" class="btn btn-sm btn-danger" @click="doAction('reject')">رد</button>
            <button class="btn btn-sm btn-outline-secondary" @click="showPrint">برگه چاپ</button>
          </div>
          <div v-if="current.status === 5 || (current.status === 4 && current.side === 2)" class="inv-filters mt-2 d-print-none">
            <select v-model.number="pay.payMethod" class="form-select form-select-sm"><option :value="1">چک</option><option :value="2">انتقال بانکی</option><option :value="3">نقد</option><option :value="9">سایر</option></select>
            <input v-model="pay.chequeNumber" class="form-control form-control-sm inv-ltr" placeholder="شماره چک" />
            <input v-model.number="pay.receivedAmount" type="number" class="form-control form-control-sm inv-ltr" placeholder="مبلغ" />
            <FnDate v-model="pay.paidDate" />
            <button class="btn btn-sm btn-primary" @click="recordPay">ثبت پرداخت/دریافت</button>
          </div>
          <p v-if="current.status === 6">پرداخت: {{ jalali(current.paidDate) }} — مورد انتظار {{ rial(current.expectedAmount) }}، دریافتی {{ rial(current.receivedAmount) }}، اختلاف {{ rial(current.paymentVariance) }}</p>
        </div>

        <div v-if="printing" class="inv-card mt-3 pb6-sheet" dir="rtl">
          <h2 class="h5 text-center">صورت‌وضعیت شماره {{ printing.statement.serial }} ({{ printing.statement.number }})</h2>
          <p>طرف قرارداد: {{ printing.statement.providerName }} — دوره: {{ jalali(printing.statement.periodFrom) }} تا {{ jalali(printing.statement.periodTo) }}</p>
          <table class="table table-sm table-bordered"><thead><tr><th>#</th><th>شرح</th><th>تعداد</th><th>قیمت واحد</th><th>مبلغ این دوره</th></tr></thead><tbody>
            <tr v-for="l in printing.statement.lines" :key="l.id"><td>{{ l.rowNumber }}</td><td>{{ l.title }}</td><td>{{ l.quantity }}</td><td class="num">{{ rial(l.unitPrice) }}</td><td class="num">{{ rial(l.thisPeriodAmount) }}</td></tr>
          </tbody></table>
          <p>جمع ناخالص: {{ rial(printing.statement.grossThisPeriod) }} | کسورات: {{ rial(printing.statement.nonconformityDeductions + printing.statement.legalDeductions) }} | قابل پرداخت: {{ rial(printing.statement.payableThisPeriod) }} ریال ({{ printing.payableInWords }})</p>
          <button class="btn btn-sm btn-outline-secondary d-print-none" @click="printPage">چاپ</button>
        </div>
      </template>

      <template v-else-if="tab === 'rates'">
        <p class="inv-muted">سامانه هیچ نرخی از پیش ندارد. هر نرخ با مرجع آن ثبت می‌شود و پس از تایید نفر دوم (مدیر مالی) قابل استفاده است.</p>
        <table class="table table-sm"><thead><tr><th>کد</th><th>عنوان</th><th>درصد</th><th>مرجع</th><th>وضعیت</th><th></th></tr></thead><tbody>
          <tr v-if="!rates.length"><td colspan="6" class="text-center inv-muted">نرخی ثبت نشده است.</td></tr>
          <tr v-for="r in rates" :key="r.id"><td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td>{{ r.percent }}</td><td>{{ r.sourceReference }}</td><td>{{ rateStatuses[r.status] }}</td>
            <td><button v-if="r.status === 0" class="btn btn-sm btn-outline-success" @click="rateAct(r, 'verify')">تایید</button><button v-if="r.status !== 2" class="btn btn-sm btn-outline-danger ms-1" @click="rateAct(r, 'retire')">کنار گذاشتن</button></td></tr>
        </tbody></table>
        <div class="inv-filters">
          <input v-model="rate.code" class="form-control form-control-sm inv-ltr" placeholder="کد" />
          <input v-model="rate.title" class="form-control form-control-sm" placeholder="عنوان" />
          <input v-model.number="rate.percent" type="number" class="form-control form-control-sm inv-ltr" placeholder="درصد" />
          <select v-model.number="rate.deductionKind" class="form-select form-select-sm" title="نوع کسر"><option :value="1">مالیات</option><option :value="2">بیمه</option><option :value="3">حسن انجام کار</option><option :value="9">سایر</option></select>
          <input v-model="rate.sourceReference" class="form-control form-control-sm" placeholder="مرجع (قانون/نامه)" />
          <button class="btn btn-sm btn-primary" @click="createRate">ثبت نرخ</button>
        </div>
      </template>

      <template v-else-if="tab === 'cbs' || tab === 'cash'">
        <div class="inv-filters mb-2">
          <select v-if="canOrders" v-model="scope.purchaseOrderId" class="form-select form-select-sm"><option :value="null">سفارش — انتخاب کنید</option><option v-for="o in orders" :key="o.id" :value="o.id">{{ o.number }} - {{ o.providerName }}</option></select>
          <span v-else class="small inv-muted align-self-center">فهرست سفارش‌ها برای نقش شما در دسترس نیست؛ قرارداد پروژه را انتخاب کنید.</span>
          <ContractPick v-model="scope.contractId" placeholder="قرارداد پروژه — انتخاب کنید" />
          <button class="btn btn-sm btn-secondary" @click="loadScope">نمایش</button>
        </div>
        <template v-if="tab === 'cbs'">
          <table class="table table-sm"><thead><tr><th>کد</th><th>عنوان</th><th>مبلغ</th><th>گره WBS</th><th>پیشرفت</th><th>ارزش کار انجام‌شده</th><th>مطالبه‌شده</th></tr></thead><tbody>
            <tr v-for="(i, k) in cbs" :key="k"><td><input v-model="i.code" class="form-control form-control-sm inv-ltr" /></td><td><input v-model="i.title" class="form-control form-control-sm" /></td><td><input v-model.number="i.amount" type="number" class="form-control form-control-sm inv-ltr" /></td><td><input v-model.number="i.wbsNodeId" type="number" class="form-control form-control-sm inv-ltr" /></td><td>{{ i.progressPercent ?? "" }}</td><td class="num">{{ rial(i.earnedAmount) }}</td><td class="num">{{ rial(i.claimedAmount) }}</td></tr>
          </tbody></table>
          <button class="btn btn-sm btn-outline-secondary" @click="cbs.push({ code: '', title: '', amount: 0, wbsNodeId: null })">بند دیگر</button>
          <button class="btn btn-sm btn-primary ms-2" @click="saveCbsItems">ذخیره CBS</button>
        </template>
        <template v-else-if="cash">
          <table class="table table-sm"><thead><tr><th>ماه</th><th>برنامه پرداخت</th><th>پرداخت واقعی</th><th>برنامه دریافت</th><th>دریافت واقعی</th><th>تجمعی برنامه</th><th>تجمعی واقعی</th></tr></thead><tbody>
            <tr v-for="m in cash.months" :key="m.month"><td>{{ m.month }}</td><td class="num">{{ rial(m.plannedPayment) }}</td><td class="num">{{ rial(m.actualPayment) }}</td><td class="num">{{ rial(m.plannedReceipt) }}</td><td class="num">{{ rial(m.actualReceipt) }}</td><td class="num">{{ rial(m.cumulativePlanned) }}</td><td class="num">{{ rial(m.cumulativeActual) }}</td></tr>
          </tbody></table>
          <h3 class="h6">برنامه نقدی دستی (جایگزین برنامه ثبت‌شده)</h3>
          <div v-for="(p, i) in planRows" :key="i" class="inv-filters mb-1">
            <select v-model.number="p.direction" class="form-select form-select-sm"><option :value="2">پرداخت</option><option :value="1">دریافت</option></select>
            <FnDate v-model="p.dueDate" />
            <input v-model.number="p.amount" type="number" class="form-control form-control-sm inv-ltr" placeholder="مبلغ (ریال)" />
            <input v-model="p.note" class="form-control form-control-sm" placeholder="توضیح" />
          </div>
          <button class="btn btn-sm btn-outline-secondary" @click="planRows.push({ direction: 2, dueDate: today(), amount: 0, note: '' })">ردیف برنامه</button>
          <button class="btn btn-sm btn-primary ms-2" @click="savePlan">ذخیره برنامه</button>
        </template>
      </template>

      <template v-else-if="tab === 'analytics' && analytics">
        <p>تعداد {{ analytics.count }} — ناخالص {{ rial(analytics.totalGross) }} — کسورات {{ rial(analytics.totalDeductions) }} — قابل پرداخت {{ rial(analytics.totalPayable) }} — میانگین زمان تا مجوز: {{ analytics.averageApprovalDays }} روز</p>
        <h3 class="h6">به تفکیک قرارداد</h3>
        <Pb6Table :columns="[{ key: 'title', title: 'قرارداد' }, { key: 'count', title: 'تعداد' }, { key: 'amount', title: 'مبلغ', kind: 'money' }]" :rows="analytics.byContract" />
        <h3 class="h6">به تفکیک دوره</h3>
        <Pb6Table :columns="[{ key: 'title', title: 'ماه' }, { key: 'count', title: 'تعداد' }, { key: 'amount', title: 'مبلغ', kind: 'money' }]" :rows="analytics.byPeriod" />
        <h3 class="h6">به تفکیک بند CBS</h3>
        <Pb6Table :columns="[{ key: 'title', title: 'بند' }, { key: 'count', title: 'تعداد' }, { key: 'amount', title: 'مبلغ', kind: 'money' }]" :rows="analytics.byCbs" />
      </template>
    </div>
    <Pb6Dialog v-model="retireDlg.open" title="کنار گذاشتن نرخ" text-label="علت کنار گذاشتن" :text-required="true" @confirm="(v: any) => sendRateAction(retireDlg.row, 'retire', v.text)" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style>
.pb6-sheet { font-family: "B Zar", "BZar", Tahoma, sans-serif; }
@media print { @page { size: A4; margin: 12mm; } }
</style>
