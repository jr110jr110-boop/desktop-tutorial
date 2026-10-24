<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import LoanSchedulePrint from "@/components/fn/print/LoanSchedulePrint.vue";
import { approveLoanAccrual, facilityAction, getBankAccounts, getFacilities, getFacility, getFacilityUtilisation, payInstallment, runLoanAccrual, saveFacility, saveFacilityLimit } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { money, parseAmount, printSheet } from "@/composables/fn";
import { draftApproved, facilityTypes, installmentStatuses, loanStatuses, scheduleKinds, usePeriods } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Bank facilities: loans / murabaha / overdraft / leasing with the repayment schedule, disbursement, instalment payment, monthly interest accrual and the bank limits with utilisation.
const tab = ref("loans");
const tabs = [{ key: "loans", title: "تسهیلات" }, { key: "accrual", title: "سود تعلق‌گرفته" }, { key: "limits", title: "سقف بانکی و استفاده" }];
const { periods, load: loadPeriods, label } = usePeriods();
const canWrite = hasRole(FnRoles.treasuryAndAccountants);
const canAccrue = hasRole(FnRoles.accountants);
const canManage = hasRole(FnRoles.managers);
const busy = ref(false);
const printing = ref(false);
const banks = ref<any[]>([]);

const list = ref<any[]>([]);
const loan = ref<any>(null);
const form = ref<any>(null);
const act = ref<any>(null);
const pay = ref<any>(null);
const limits = ref<any>(null);
const limitForm = ref<any>(null);
const periodId = ref<number | null>(null);
const accruals = ref<any[]>([]);

async function load() {
  const [d, error] = await getFacilities();
  if (error) return fail(error);
  list.value = d || [];
}
async function open(id: number) {
  const [d, error] = await getFacility(id);
  if (error) return fail(error);
  loan.value = d;
}
const blankRow = () => ({ dueDate: today(), principal: "", interest: "" });
async function save() {
  const f = form.value;
  if (!String(f.bankName).trim() || !(parseAmount(f.amount) || 0) || !f.startDate) return fail("بانک، مبلغ و تاریخ شروع را وارد کنید.");
  const custom = Number(f.scheduleKind) === 3;
  if (!custom && !(Number(f.installmentCount) > 0)) return fail("تعداد اقساط را وارد کنید.");
  if (custom && !f.customRows.length) return fail("برای جدول دلخواه حداقل یک قسط وارد کنید.");
  busy.value = true;
  const [d, error] = await saveFacility({
    id: f.id || null, bankName: f.bankName, facilityType: Number(f.facilityType), contractNo: f.contractNo || null, amount: parseAmount(f.amount), currencyCode: f.currencyCode || null, startDate: f.startDate,
    ratePercent: parseAmount(f.ratePercent) || 0, scheduleKind: Number(f.scheduleKind), installmentCount: custom ? f.customRows.length : Number(f.installmentCount), feeAmount: parseAmount(f.feeAmount) || 0, collateral: f.collateral || null,
    facilityLimitId: null, bankAccountId: f.bankAccountId || null,
    customRows: custom ? f.customRows.map((r: any) => ({ dueDate: r.dueDate, principal: parseAmount(r.principal) || 0, interest: parseAmount(r.interest) || 0 })) : [],
  });
  busy.value = false;
  if (error) return fail(error);
  ok("تسهیلات ذخیره شد.");
  form.value = null;
  loan.value = d;
  load();
}
async function doAct() {
  const a = act.value;
  if (a.action === "disburse" && !a.bankAccountId) return fail("حساب بانکی واریز را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await facilityAction({ id: loan.value.id, action: a.action, date: a.date || today(), bankAccountId: a.bankAccountId || null });
  busy.value = false;
  if (error) return fail(error);
  ok(a.action === "disburse" ? "تسهیلات پرداخت و سند صادر شد." : "تسهیلات لغو شد.");
  act.value = null;
  loan.value = d;
  load();
}
async function doPay() {
  const p = pay.value;
  if (!p.bankAccountId) return fail("حساب بانکی پرداخت را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await payInstallment({ loanId: loan.value.id, sequence: p.sequence, date: p.date || today(), bankAccountId: p.bankAccountId, penalty: parseAmount(p.penalty) || 0 });
  busy.value = false;
  if (error) return fail(error);
  ok("قسط پرداخت و سند صادر شد.");
  pay.value = null;
  loan.value = d;
  load();
}
async function printLoan() {
  printing.value = true;
  await nextTick();
  printSheet();
}

async function accrue() {
  if (!periodId.value) return fail("دوره را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await runLoanAccrual(periodId.value);
  busy.value = false;
  if (error) return fail(error);
  accruals.value = d || [];
  ok(`${accruals.value.length} ردیف سود تعلق‌گرفته (پیش‌نویس) ساخته شد.`);
}
async function approveAccrual(a: any) {
  busy.value = true;
  const [d, error] = await approveLoanAccrual(a.id);
  busy.value = false;
  if (error) return fail(error);
  ok("ردیف تایید و سند صادر شد.");
  accruals.value = accruals.value.map((x) => (x.id === a.id ? d : x));
}

async function loadLimits() {
  const [d, error] = await getFacilityUtilisation();
  if (error) return fail(error);
  limits.value = d;
}
async function saveLimit() {
  const f = limitForm.value;
  if (!String(f.bankName).trim() || !(parseAmount(f.limitAmount) || 0) || !f.validFrom) return fail("بانک، مبلغ سقف و تاریخ شروع را وارد کنید.");
  busy.value = true;
  const [, error] = await saveFacilityLimit({ id: f.id || null, bankName: f.bankName, limitAmount: parseAmount(f.limitAmount), validFrom: f.validFrom, validTo: f.validTo || null, note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("سقف ذخیره شد.");
  limitForm.value = null;
  loadLimits();
}

onMounted(async () => {
  const [b] = await getBankAccounts({ PageSize: 200 });
  banks.value = (b?.data || []).filter((x: any) => x.isActive);
  loadPeriods();
  load();
  loadLimits();
});
</script>

<template>
  <div class="inv-page">
    <LoanSchedulePrint v-if="printing && loan" :loan="loan" />
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">تسهیلات بانکی</h1>
        <button v-if="canWrite && tab === 'loans'" class="btn btn-sm btn-primary" @click="form = { bankName: '', facilityType: 1, contractNo: '', amount: '', currencyCode: '', startDate: today(), ratePercent: '', scheduleKind: 2, installmentCount: 12, feeAmount: '', collateral: '', bankAccountId: null, customRows: [] }">تسهیلات جدید</button>
        <button v-if="canManage && tab === 'limits'" class="btn btn-sm btn-primary" @click="limitForm = { bankName: '', limitAmount: '', validFrom: today(), validTo: null, note: '' }">سقف جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'loans'">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>بانک</th><th>نوع</th><th>مبلغ</th><th>نرخ</th><th>اقساط</th><th>اصل باقی‌مانده</th><th>سود تعلق‌گرفته</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!list.length"><td colspan="9" class="text-center inv-muted">تسهیلاتی ثبت نشده است.</td></tr>
            <tr v-for="l in list" :key="l.id" style="cursor: pointer" @click="open(l.id)"><td class="inv-ltr">{{ l.number }}</td><td>{{ l.bankName }}</td><td>{{ facilityTypes[l.facilityType] }}</td><td class="fn-num">{{ money(l.amount) }}</td><td>{{ l.ratePercent }}%</td><td>{{ l.installmentCount }}</td><td class="fn-num">{{ money(l.outstandingPrincipal) }}</td><td class="fn-num">{{ money(l.accruedInterestPayable) }}</td><td>{{ loanStatuses[l.status] }}</td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'accrual'">
        <div class="inv-filters mb-3">
          <select v-model="periodId" class="form-select form-select-sm"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
          <button v-if="canAccrue" :disabled="busy" class="btn btn-sm btn-primary" @click="accrue">محاسبه سود تعلق‌گرفته دوره</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>تسهیلات</th><th>مبلغ سود</th><th>وضعیت</th><th>سند</th><th v-if="canAccrue"></th></tr></thead>
          <tbody>
            <tr v-if="!accruals.length"><td colspan="5" class="text-center inv-muted">پس از محاسبه، ردیف‌ها اینجا نمایش داده می‌شوند. تاییدکننده باید غیر از تهیه‌کننده باشد.</td></tr>
            <tr v-for="a in accruals" :key="a.id"><td class="inv-ltr">{{ a.loanNumber }}</td><td class="fn-num">{{ money(a.amount) }}</td><td>{{ draftApproved[a.status] }}</td><td>{{ a.voucherId }}</td><td v-if="canAccrue"><button v-if="a.status === 0" :disabled="busy" class="btn btn-sm btn-success" @click="approveAccrual(a)">تایید و صدور سند</button></td></tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <template v-if="limits">
          <p>جمع سقف: <b>{{ money(limits.totalLimit) }}</b> - استفاده‌شده: <b>{{ money(limits.totalUtilised) }}</b> - قابل استفاده: <b>{{ money(limits.totalAvailable) }}</b> ریال</p>
          <table class="table table-sm">
            <thead><tr><th>بانک</th><th>سقف</th><th>استفاده‌شده</th><th>قابل استفاده</th><th>از تاریخ</th><th>تا تاریخ</th><th>توضیح</th><th v-if="canManage"></th></tr></thead>
            <tbody>
              <tr v-if="!(limits.facilities || []).length"><td colspan="8" class="text-center inv-muted">سقفی ثبت نشده است.</td></tr>
              <tr v-for="f in limits.facilities || []" :key="f.id"><td>{{ f.bankName }}</td><td class="fn-num">{{ money(f.limitAmount) }}</td><td class="fn-num">{{ money(f.utilised) }}</td><td class="fn-num">{{ money(f.available) }}</td><td>{{ jalali(f.validFrom) }}</td><td>{{ jalali(f.validTo) }}</td><td>{{ f.note }}</td><td v-if="canManage"><button class="btn btn-sm btn-outline-secondary" @click="limitForm = { ...f, limitAmount: String(f.limitAmount), validTo: f.validTo || null }">ویرایش</button></td></tr>
            </tbody>
          </table>
        </template>
      </template>
    </div>

    <div v-if="loan" class="inv-modal-backdrop screen-only" @click.self="loan = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2"><span class="inv-ltr">{{ loan.number }}</span> - {{ loan.bankName }} <span class="inv-badge">{{ loanStatuses[loan.status] }}</span></h2>
        <p>
          {{ facilityTypes[loan.facilityType] }} - قرارداد <span class="inv-ltr">{{ loan.contractNo }}</span> - مبلغ {{ money(loan.amount) }} ریال - نرخ {{ loan.ratePercent }}% - {{ scheduleKinds[loan.scheduleKind] }} - از {{ jalali(loan.startDate) }} تا {{ jalali(loan.endDate) }}<br />
          جمع سود {{ money(loan.totalInterest) }} - اصل باقی‌مانده {{ money(loan.outstandingPrincipal) }} - سود تعلق‌گرفته پرداخت‌نشده {{ money(loan.accruedInterestPayable) }} - کارمزد {{ money(loan.feeAmount) }} - وثیقه: {{ loan.collateral }}
          <span v-if="loan.disbursementVoucherId"> - سند پرداخت {{ loan.disbursementVoucherId }}</span>
        </p>
        <div class="table-responsive" style="max-height: 320px; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>قسط</th><th>سررسید</th><th>اصل</th><th>سود</th><th>جمع</th><th>وضعیت</th><th>تاریخ پرداخت</th><th>جریمه</th><th>سند</th><th v-if="canWrite"></th></tr></thead>
            <tbody>
              <tr v-for="i in loan.installments" :key="i.id"><td>{{ i.sequence }}</td><td>{{ jalali(i.dueDate) }}</td><td class="fn-num">{{ money(i.principal) }}</td><td class="fn-num">{{ money(i.interest) }}</td><td class="fn-num">{{ money(i.total) }}</td><td>{{ installmentStatuses[i.status] }}</td><td>{{ jalali(i.paidDate) }}</td><td class="fn-num">{{ money(i.penaltyPaid) }}</td><td>{{ i.voucherId }}</td><td v-if="canWrite"><button v-if="i.status === 0 && loan.status === 1" class="btn btn-sm btn-outline-primary" @click="pay = { sequence: i.sequence, date: today(), bankAccountId: loan.bankAccountId, penalty: '' }">پرداخت</button></td></tr>
            </tbody>
          </table>
        </div>
        <div class="d-flex gap-2 justify-content-end flex-wrap mt-2">
          <button class="btn btn-sm btn-outline-secondary" @click="loan = null">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printLoan">چاپ جدول</button>
          <button v-if="canWrite && loan.status === 0" class="btn btn-sm btn-outline-primary" @click="form = { ...loan, amount: String(loan.amount), ratePercent: String(loan.ratePercent), feeAmount: String(loan.feeAmount), customRows: [] }; loan = null">ویرایش</button>
          <button v-if="canWrite && loan.status === 0" class="btn btn-sm btn-success" @click="act = { action: 'disburse', date: today(), bankAccountId: loan.bankAccountId }">پرداخت وام به حساب</button>
          <button v-if="canWrite && loan.status === 0" class="btn btn-sm btn-outline-danger" @click="act = { action: 'cancel', date: today(), bankAccountId: null }">لغو</button>
        </div>
      </div>
    </div>

    <div v-if="act" class="inv-modal-backdrop screen-only" @click.self="act = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ act.action === "disburse" ? "پرداخت تسهیلات" : "لغو تسهیلات" }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="act.date" /></div>
          <div v-if="act.action === 'disburse'" class="col-md-6"><label class="form-label small mb-0">حساب بانکی واریز *</label><select v-model="act.bankAccountId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="act = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="doAct">تایید</button></div>
      </div>
    </div>

    <div v-if="pay" class="inv-modal-backdrop screen-only" @click.self="pay = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">پرداخت قسط {{ pay.sequence }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ پرداخت</label><FnDate v-model="pay.date" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">جریمه دیرکرد (ریال)</label><input v-model="pay.penalty" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">حساب بانکی پرداخت *</label><select v-model="pay.bankAccountId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="pay = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="doPay">پرداخت و صدور سند</button></div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش تسهیلات" : "تسهیلات جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">بانک *</label><input v-model="form.bankName" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع</label><select v-model="form.facilityType" class="form-select form-select-sm"><option v-for="(t, k) in facilityTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">شماره قرارداد</label><input v-model="form.contractNo" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مبلغ (ریال) *</label><input v-model="form.amount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نرخ سود سالانه (درصد)</label><input v-model="form.ratePercent" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">کارمزد (ریال)</label><input v-model="form.feeAmount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ شروع *</label><FnDate v-model="form.startDate" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">روش بازپرداخت</label><select v-model="form.scheduleKind" class="form-select form-select-sm"><option v-for="(t, k) in scheduleKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div v-if="Number(form.scheduleKind) !== 3" class="col-md-4"><label class="form-label small mb-0">تعداد اقساط (ماهانه)</label><input v-model="form.installmentCount" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">حساب بانکی واریز</label><select v-model="form.bankAccountId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">وثیقه</label><input v-model="form.collateral" class="form-control form-control-sm" /></div>
        </div>
        <div v-if="Number(form.scheduleKind) === 3" class="mt-2">
          <table class="table table-sm">
            <thead><tr><th>سررسید</th><th>اصل</th><th>سود</th><th></th></tr></thead>
            <tbody><tr v-for="(r, i) in form.customRows" :key="i"><td><FnDate v-model="r.dueDate" /></td><td><input v-model="r.principal" class="form-control form-control-sm fn-num" /></td><td><input v-model="r.interest" class="form-control form-control-sm fn-num" /></td><td><button class="btn btn-sm btn-link text-danger p-0" @click="form.customRows.splice(i, 1)">حذف</button></td></tr></tbody>
          </table>
          <button class="btn btn-sm btn-outline-secondary" @click="form.customRows.push(blankRow())">+ قسط</button>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>

    <div v-if="limitForm" class="inv-modal-backdrop screen-only" @click.self="limitForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">سقف تسهیلات بانک</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">بانک *</label><input v-model="limitForm.bankName" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مبلغ سقف (ریال) *</label><input v-model="limitForm.limitAmount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="limitForm.validFrom" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="limitForm.validTo" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="limitForm.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="limitForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveLimit">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
