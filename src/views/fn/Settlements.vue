<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { bonusRunAction, getBonusRuns, getPayrollEmployees, getPayrollTaxReconciliation, getTerminations, saveBonusRun, saveTermination, terminationAction } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { jalaliNow, money, parseAmount } from "@/composables/fn";
import { settlementStatuses } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Payroll settlements (personal data: PAYROLL, FINANCEMANAGER, CEO): terminations (severance, leave, notice, tax, insurance, net), the annual bonus (عیدی) runs
// and the annual payroll tax reconciliation. Drafts are approved by another person. Amounts are never written to the console.
const tab = ref("terminations");
const tabs = [{ key: "terminations", title: "تسویه پایان خدمت" }, { key: "bonus", title: "عیدی" }, { key: "tax", title: "تطبیق مالیات حقوق سالانه" }];
const canWrite = hasRole(FnRoles.payrollUsers);
const busy = ref(false);
const employees = ref<any[]>([]);
const empName = (e: any) => `${e.personnelCode} - ${e.firstName || ""} ${e.lastName || ""}`.trim();

// terminations
const terms = ref<any[]>([]);
const tDetail = ref<any>(null);
const tForm = ref<any>(null);
async function loadTerms() {
  const [d, error] = await getTerminations();
  if (error) return fail(error);
  terms.value = d || [];
}
async function saveTerm() {
  const f = tForm.value;
  if (!f.employeeId || !f.terminationDate) return fail("کارمند و تاریخ پایان خدمت را وارد کنید.");
  busy.value = true;
  const [d, error] = await saveTermination({ id: f.id || null, employeeId: f.employeeId, terminationDate: f.terminationDate, unusedLeaveDays: String(f.unusedLeaveDays).trim() === "" ? null : parseAmount(f.unusedLeaveDays), noticeDays: parseAmount(f.noticeDays) || 0, monthlyBase: String(f.monthlyBase).trim() === "" ? null : parseAmount(f.monthlyBase), note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("پیش‌نویس تسویه ثبت شد.");
  tForm.value = null;
  tDetail.value = d;
  loadTerms();
}
async function termAct(action: string) {
  busy.value = true;
  const [d, error] = await terminationAction(tDetail.value.id, action);
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "تسویه تایید و سند صادر شد." : "تسویه باطل شد.");
  tDetail.value = d;
  loadTerms();
}

// bonus runs
const runs = ref<any[]>([]);
const bDetail = ref<any>(null);
const bForm = ref<any>(null);
async function loadRuns() {
  const [d, error] = await getBonusRuns();
  if (error) return fail(error);
  runs.value = d || [];
}
async function saveRun() {
  const f = bForm.value;
  if (!parseAmount(f.year) || !f.payDate) return fail("سال و تاریخ پرداخت را وارد کنید.");
  busy.value = true;
  const [d, error] = await saveBonusRun({ id: f.id || null, year: parseAmount(f.year), payDate: f.payDate, employeeIds: f.employeeIds.length ? f.employeeIds : null, note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("پیش‌نویس عیدی ثبت شد.");
  bForm.value = null;
  bDetail.value = d;
  loadRuns();
}
async function runAct(action: string) {
  busy.value = true;
  const [d, error] = await bonusRunAction(bDetail.value.id, action);
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "عیدی تایید و سند صادر شد." : "اجرای عیدی باطل شد.");
  bDetail.value = d;
  loadRuns();
}

// tax reconciliation
const year = ref(String(jalaliNow().year));
const recon = ref<any>(null);
async function loadRecon() {
  const y = parseAmount(year.value);
  if (!y) return fail("سال را وارد کنید.");
  const [d, error] = await getPayrollTaxReconciliation(y);
  if (error) return fail(error);
  recon.value = d;
}

onMounted(async () => {
  loadTerms();
  loadRuns();
  const [e] = await getPayrollEmployees({ PageSize: 300 });
  employees.value = e?.data || [];
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">تسویه‌های حقوقی: پایان خدمت، عیدی، مالیات سالانه</h1>
        <button v-if="canWrite && tab === 'terminations'" class="btn btn-sm btn-primary" @click="tForm = { employeeId: null, terminationDate: today(), unusedLeaveDays: '', noticeDays: '0', monthlyBase: '', note: '' }">تسویه جدید</button>
        <button v-if="canWrite && tab === 'bonus'" class="btn btn-sm btn-primary" @click="bForm = { year: String(jalaliNow().year), payDate: today(), employeeIds: [], note: '' }">عیدی جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'terminations'">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>کارمند</th><th>پایان خدمت</th><th>سنوات (سال)</th><th>جمع</th><th>مالیات</th><th>بیمه</th><th>خالص قابل پرداخت</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!terms.length"><td colspan="9" class="text-center inv-muted">تسویه‌ای نیست.</td></tr>
            <tr v-for="t in terms" :key="t.id" style="cursor: pointer" @click="tDetail = t"><td class="inv-ltr">{{ t.number }}</td><td>{{ t.personnelCode }} {{ t.employeeName }}</td><td>{{ jalali(t.terminationDate) }}</td><td class="fn-num">{{ t.yearsOfService }}</td><td class="fn-num">{{ money(t.total) }}</td><td class="fn-num">{{ money(t.taxAmount) }}</td><td class="fn-num">{{ money(t.insuranceAmount) }}</td><td class="fn-num">{{ money(t.netPayable) }}</td><td>{{ settlementStatuses[t.status] }}</td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'bonus'">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>سال</th><th>تاریخ پرداخت</th><th>ناخالص (سقف‌دار)</th><th>مالیات</th><th>خالص</th><th>از ذخیره</th><th>هزینه همین دوره</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!runs.length"><td colspan="9" class="text-center inv-muted">اجرای عیدی ثبت نشده است.</td></tr>
            <tr v-for="r in runs" :key="r.id" style="cursor: pointer" @click="bDetail = r"><td class="inv-ltr">{{ r.number }}</td><td>{{ r.year }}</td><td>{{ jalali(r.payDate) }}</td><td class="fn-num">{{ money(r.totalGross) }}</td><td class="fn-num">{{ money(r.totalTax) }}</td><td class="fn-num">{{ money(r.totalNet) }}</td><td class="fn-num">{{ money(r.fromProvision) }}</td><td class="fn-num">{{ money(r.expensedNow) }}</td><td>{{ settlementStatuses[r.status] }}</td></tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <div class="inv-filters mb-3"><div><label class="form-label small mb-0">سال (شمسی)</label><input v-model="year" class="form-control form-control-sm" style="max-width: 110px" /></div><button class="btn btn-sm btn-primary" @click="loadRecon">نمایش</button></div>
        <template v-if="recon">
          <p>
            جمع مشمول: <b>{{ money(recon.totalTaxable) }}</b> - کسرشده: <b>{{ money(recon.totalWithheld) }}</b> - محاسبه مجدد: <b>{{ money(recon.totalRecomputed) }}</b> - اختلاف: <b>{{ money(recon.totalDifference) }}</b><br />
            مالیات کسرشده در دفتر کل: {{ money(recon.ledgerWithheld) }} <span :class="recon.tiesToLedger ? 'ok' : 'bad'" class="inv-badge">{{ recon.tiesToLedger ? "با دفتر کل همخوان است" : "با دفتر کل همخوان نیست" }}</span>
          </p>
          <p v-if="recon.note" class="inv-muted">{{ recon.note }}</p>
          <table class="table table-sm">
            <thead><tr><th>پرسنلی</th><th>کارمند</th><th>فیش</th><th>مشمول مالیات</th><th>کسرشده</th><th>محاسبه مجدد</th><th>اختلاف</th></tr></thead>
            <tbody>
              <tr v-if="!recon.rows.length"><td colspan="7" class="text-center inv-muted">داده‌ای نیست.</td></tr>
              <tr v-for="r in recon.rows" :key="r.employeeId" :class="r.difference ? 'table-warning' : ''"><td>{{ r.personnelCode }}</td><td>{{ r.employeeName }}</td><td>{{ r.payslips }}</td><td class="fn-num">{{ money(r.taxable) }}</td><td class="fn-num">{{ money(r.taxWithheld) }}</td><td class="fn-num">{{ money(r.taxRecomputed) }}</td><td class="fn-num">{{ money(r.difference) }}</td></tr>
            </tbody>
          </table>
        </template>
      </template>
    </div>

    <div v-if="tDetail" class="inv-modal-backdrop screen-only" @click.self="tDetail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2"><span class="inv-ltr">{{ tDetail.number }}</span> - {{ tDetail.personnelCode }} {{ tDetail.employeeName }} <span class="inv-badge">{{ settlementStatuses[tDetail.status] }}</span></h2>
        <table class="table table-sm" style="max-width: 640px">
          <tbody>
            <tr><td>تاریخ استخدام / پایان خدمت</td><td>{{ jalali(tDetail.hireDate) }} / {{ jalali(tDetail.terminationDate) }} ({{ tDetail.yearsOfService }} سال)</td></tr>
            <tr><td>مزد ماهانه مبنا / مزد روزانه</td><td class="fn-num">{{ money(tDetail.monthlyBase) }} / {{ money(tDetail.dailyWage) }}</td></tr>
            <tr><td>سنوات ({{ tDetail.severanceDaysPerYear }} روز برای هر سال)</td><td class="fn-num">{{ money(tDetail.severancePay) }}</td></tr>
            <tr><td>مرخصی استفاده‌نشده ({{ tDetail.unusedLeaveDays }} روز)</td><td class="fn-num">{{ money(tDetail.leavePay) }}</td></tr>
            <tr><td>بدل اخطار ({{ tDetail.noticeDays }} روز)</td><td class="fn-num">{{ money(tDetail.noticePay) }}</td></tr>
            <tr class="fn-total"><td>جمع</td><td class="fn-num">{{ money(tDetail.total) }}</td></tr>
            <tr><td>مالیات</td><td class="fn-num">{{ money(tDetail.taxAmount) }}</td></tr>
            <tr><td>بیمه</td><td class="fn-num">{{ money(tDetail.insuranceAmount) }}</td></tr>
            <tr class="fn-total"><td>خالص قابل پرداخت</td><td class="fn-num">{{ money(tDetail.netPayable) }}</td></tr>
            <tr><td>از ذخیره سنوات / مرخصی</td><td class="fn-num">{{ money(tDetail.fromSeveranceProvision) }} / {{ money(tDetail.fromLeaveProvision) }}</td></tr>
            <tr v-if="tDetail.voucherId"><td>سند حسابداری</td><td>{{ tDetail.voucherId }}</td></tr>
          </tbody>
        </table>
        <ul v-if="tDetail.sources?.length" class="small inv-muted"><li v-for="(s, i) in tDetail.sources" :key="i">{{ s }}</li></ul>
        <div class="d-flex gap-2 justify-content-end">
          <button class="btn btn-sm btn-outline-secondary" @click="tDetail = null">بستن</button>
          <template v-if="canWrite && tDetail.status === 0">
            <button class="btn btn-sm btn-outline-primary" @click="tForm = { id: tDetail.id, employeeId: tDetail.employeeId, terminationDate: tDetail.terminationDate, unusedLeaveDays: String(tDetail.unusedLeaveDays), noticeDays: String(tDetail.noticeDays), monthlyBase: String(tDetail.monthlyBase), note: tDetail.note || '' }; tDetail = null">ویرایش</button>
            <button :disabled="busy" class="btn btn-sm btn-success" @click="termAct('approve')">تایید و صدور سند</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="termAct('cancel')">ابطال</button>
          </template>
        </div>
      </div>
    </div>

    <div v-if="bDetail" class="inv-modal-backdrop screen-only" @click.self="bDetail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2"><span class="inv-ltr">{{ bDetail.number }}</span> - عیدی سال {{ bDetail.year }} <span class="inv-badge">{{ settlementStatuses[bDetail.status] }}</span></h2>
        <p>تاریخ پرداخت {{ jalali(bDetail.payDate) }} - {{ bDetail.bonusDays }} روز (سقف {{ bDetail.capDays }} روز، حداقل مزد روزانه {{ money(bDetail.minimumDailyWage) }}) - ناخالص {{ money(bDetail.totalGross) }} - مالیات {{ money(bDetail.totalTax) }} - خالص <b>{{ money(bDetail.totalNet) }}</b><span v-if="bDetail.voucherId"> - سند {{ bDetail.voucherId }}</span></p>
        <div class="table-responsive" style="max-height: 320px; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>کارمند</th><th>مزد روزانه</th><th>روز کارکرد</th><th>روز سال</th><th>ناخالص</th><th>مشمول ماهانه</th><th>مالیات</th><th>خالص</th></tr></thead>
            <tbody><tr v-for="l in bDetail.lines" :key="l.employeeId"><td>{{ l.employeeName }}</td><td class="fn-num">{{ money(l.dailyWage) }}</td><td>{{ l.employedDays }}</td><td>{{ l.yearDays }}</td><td class="fn-num">{{ money(l.gross) }}</td><td class="fn-num">{{ money(l.monthlyTaxable) }}</td><td class="fn-num">{{ money(l.tax) }}</td><td class="fn-num">{{ money(l.net) }}</td></tr></tbody>
          </table>
        </div>
        <ul v-if="bDetail.sources?.length" class="small inv-muted"><li v-for="(s, i) in bDetail.sources" :key="i">{{ s }}</li></ul>
        <div class="d-flex gap-2 justify-content-end">
          <button class="btn btn-sm btn-outline-secondary" @click="bDetail = null">بستن</button>
          <template v-if="canWrite && bDetail.status === 0">
            <button class="btn btn-sm btn-outline-primary" @click="bForm = { id: bDetail.id, year: String(bDetail.year), payDate: bDetail.payDate, employeeIds: bDetail.lines.map((l: any) => l.employeeId), note: bDetail.note || '' }; bDetail = null">ویرایش</button>
            <button :disabled="busy" class="btn btn-sm btn-success" @click="runAct('approve')">تایید و صدور سند</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="runAct('cancel')">ابطال</button>
          </template>
        </div>
      </div>
    </div>

    <div v-if="tForm" class="inv-modal-backdrop screen-only" @click.self="tForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">تسویه پایان خدمت</h2>
        <div class="row g-2">
          <div class="col-md-8"><label class="form-label small mb-0">کارمند *</label><select v-model="tForm.employeeId" :disabled="!!tForm.id" class="form-select form-select-sm"><option :value="null">-</option><option v-for="e in employees" :key="e.id" :value="e.id">{{ empName(e) }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ پایان خدمت *</label><FnDate v-model="tForm.terminationDate" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مرخصی استفاده‌نشده (روز؛ خالی = از مانده مرخصی)</label><input v-model="tForm.unusedLeaveDays" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">روز اخطار</label><input v-model="tForm.noticeDays" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مزد ماهانه مبنا (خالی = از فیش‌ها)</label><input v-model="tForm.monthlyBase" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="tForm.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="tForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveTerm">محاسبه و ذخیره پیش‌نویس</button></div>
      </div>
    </div>

    <div v-if="bForm" class="inv-modal-backdrop screen-only" @click.self="bForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">محاسبه عیدی</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">سال (شمسی) *</label><input v-model="bForm.year" class="form-control form-control-sm" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">تاریخ پرداخت *</label><FnDate v-model="bForm.payDate" /></div>
          <div class="col-12">
            <label class="form-label small mb-0">کارمندان (بدون انتخاب = همه دارای مزد روزانه)</label>
            <select v-model="bForm.employeeIds" multiple class="form-select form-select-sm" style="height: 9rem"><option v-for="e in employees" :key="e.id" :value="e.id">{{ empName(e) }}</option></select>
          </div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="bForm.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="bForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveRun">محاسبه و ذخیره پیش‌نویس</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
