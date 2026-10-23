<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ContractPick from "@/components/fn/ContractPick.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { approveDeferralEntry, cancelDeferral, getDeferral, getDeferrals, getProvisions, provisionMovement, runDeferralAmortisation, runReversingAccruals, saveDeferral, saveProvision, trueUpProvision } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { money, parseAmount } from "@/composables/fn";
import { deferralKinds, deferralStatuses, draftApproved, provisionKinds, usePeriods } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Deferrals (prepaid expense / deferred income) with monthly amortisation entries, provisions with movements and payroll true-up, and the period reversal of accruals.
const tab = ref("deferrals");
const tabs = [{ key: "deferrals", title: "هزینه و درآمد انتقالی" }, { key: "provisions", title: "ذخایر" }, { key: "reversing", title: "برگشت خودکار اسناد تعهدی" }];
const { periods, load: loadPeriods, label } = usePeriods();
const canWrite = hasRole(FnRoles.accountants);
const canManage = hasRole(FnRoles.managers);
const busy = ref(false);
const periodId = ref<number | null>(null);

const list = ref<any[]>([]);
const detail = ref<any>(null);
const form = ref<any>(null);
async function load() {
  const [d, error] = await getDeferrals();
  if (error) return fail(error);
  list.value = d || [];
}
async function open(id: number) {
  const [d, error] = await getDeferral(id);
  if (error) return fail(error);
  detail.value = d;
}
async function save() {
  const f = form.value;
  if (!String(f.description).trim() || !(parseAmount(f.totalAmount) || 0) || !f.startDate || !f.endDate || !String(f.balanceAccountCode).trim() || !String(f.resultAccountCode).trim()) return fail("شرح، مبلغ، تاریخ‌ها و دو حساب را وارد کنید.");
  if (f.endDate < f.startDate) return fail("تاریخ پایان باید پس از تاریخ شروع باشد.");
  busy.value = true;
  const [, error] = await saveDeferral({ kind: Number(f.kind), description: f.description, totalAmount: parseAmount(f.totalAmount), startDate: f.startDate, endDate: f.endDate, balanceAccountCode: String(f.balanceAccountCode).trim(), resultAccountCode: String(f.resultAccountCode).trim(), costCenterCode: String(f.costCenterCode || "").trim() || null, contractId: f.contractId || null });
  busy.value = false;
  if (error) return fail(error);
  ok("برنامه ذخیره شد.");
  form.value = null;
  load();
}
const runResult = ref<any[] | null>(null);
async function amortise() {
  if (!periodId.value) return fail("دوره را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await runDeferralAmortisation(periodId.value);
  busy.value = false;
  if (error) return fail(error);
  runResult.value = d || [];
  ok(`${(d || []).length} ردیف استهلاک پیش‌نویس ساخته شد؛ پس از تایید سند صادر می‌شود.`);
  load();
}
async function approveEntry(e: any) {
  busy.value = true;
  const [, error] = await approveDeferralEntry(e.id);
  busy.value = false;
  if (error) return fail(error);
  ok("ردیف تایید و سند صادر شد.");
  open(e.deferralId);
  load();
}
async function cancel() {
  busy.value = true;
  const [, error] = await cancelDeferral(detail.value.id);
  busy.value = false;
  if (error) return fail(error);
  ok("برنامه لغو شد.");
  detail.value = null;
  load();
}

// provisions
const provisions = ref<any[]>([]);
const asOf = ref<string | null>(null);
const pForm = ref<any>(null);
const mForm = ref<any>(null);
const tForm = ref<any>(null);
const tResult = ref<any>(null);
async function loadProvisions() {
  const [d, error] = await getProvisions(asOf.value);
  if (error) return fail(error);
  provisions.value = d || [];
}
async function saveP() {
  const f = pForm.value;
  if (!String(f.description).trim() || !String(f.expenseAccountCode).trim() || !String(f.liabilityAccountCode).trim()) return fail("شرح و حساب‌های هزینه و بدهی را وارد کنید.");
  busy.value = true;
  const [, error] = await saveProvision({ id: f.id || null, kind: Number(f.kind), description: f.description, reviewDate: f.reviewDate || null, expenseAccountCode: String(f.expenseAccountCode).trim(), liabilityAccountCode: String(f.liabilityAccountCode).trim(), isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره ثبت شد.");
  pForm.value = null;
  loadProvisions();
}
async function saveM() {
  const f = mForm.value;
  const amount = parseAmount(f.amount);
  if (!amount || !f.date || !String(f.reason).trim()) return fail("مبلغ (مثبت برای افزایش، منفی برای مصرف)، تاریخ و دلیل را وارد کنید.");
  busy.value = true;
  const [, error] = await provisionMovement({ provisionId: f.provisionId, date: f.date, amount: f.direction === "use" ? -Math.abs(amount) : Math.abs(amount), reason: f.reason, counterAccountCode: String(f.counterAccountCode || "").trim() || null });
  busy.value = false;
  if (error) return fail(error);
  ok("حرکت ذخیره ثبت و سند صادر شد.");
  mForm.value = null;
  loadProvisions();
}
async function trueUp() {
  const f = tForm.value;
  if (!f.date || parseAmount(f.requiredBalance) === null || !String(f.reason).trim()) return fail("تاریخ، مانده لازم و توضیح محاسبه را وارد کنید.");
  busy.value = true;
  const [d, error] = await trueUpProvision({ kind: f.kind, date: f.date, requiredBalance: parseAmount(f.requiredBalance), reason: f.reason });
  busy.value = false;
  if (error) return fail(error);
  tResult.value = d;
  ok("تعدیل ذخیره حقوق ثبت شد.");
  tForm.value = null;
}

// reversing accruals
const revResult = ref<any>(null);
async function reverse() {
  if (!periodId.value) return fail("دوره را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await runReversingAccruals(periodId.value);
  busy.value = false;
  if (error) return fail(error);
  revResult.value = d;
  ok(`${d.createdVoucherIds.length} سند برگشت ساخته شد.`);
}

onMounted(() => {
  loadPeriods();
  load();
  loadProvisions();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">انتقالی‌ها، تعهدی‌ها و ذخایر</h1>
        <button v-if="canWrite && tab === 'deferrals'" class="btn btn-sm btn-primary" @click="form = { kind: 1, description: '', totalAmount: '', startDate: today(), endDate: null, balanceAccountCode: '', resultAccountCode: '', costCenterCode: '', contractId: null }">برنامه جدید</button>
        <button v-if="canWrite && tab === 'provisions'" class="btn btn-sm btn-primary" @click="pForm = { kind: 1, description: '', reviewDate: null, expenseAccountCode: '', liabilityAccountCode: '', isActive: true }">ذخیره جدید</button>
        <button v-if="canWrite && tab === 'provisions'" class="btn btn-sm btn-outline-primary" @click="tForm = { kind: 'bonus', date: today(), requiredBalance: '', reason: '' }">تعدیل ذخایر حقوق</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'deferrals'">
        <div class="inv-filters mb-3">
          <select v-model="periodId" class="form-select form-select-sm"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="amortise">ساخت ردیف‌های استهلاک دوره</button>
        </div>
        <p v-if="runResult" class="inv-muted">{{ runResult.length }} ردیف پیش‌نویس ساخته شد.</p>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>نوع</th><th>شرح</th><th>مبلغ کل</th><th>از تاریخ</th><th>تا تاریخ</th><th>ماه</th><th>مستهلک‌شده</th><th>باقی‌مانده</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!list.length"><td colspan="10" class="text-center inv-muted">برنامه‌ای نیست.</td></tr>
            <tr v-for="d in list" :key="d.id" style="cursor: pointer" @click="open(d.id)"><td class="inv-ltr">{{ d.number }}</td><td>{{ deferralKinds[d.kind] }}</td><td>{{ d.description }}</td><td class="fn-num">{{ money(d.totalAmount) }}</td><td>{{ jalali(d.startDate) }}</td><td>{{ jalali(d.endDate) }}</td><td>{{ d.months }}</td><td class="fn-num">{{ money(d.amortised) }}</td><td class="fn-num">{{ money(d.remaining) }}</td><td>{{ deferralStatuses[d.status] }}</td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'provisions'">
        <div class="inv-filters mb-3"><div><label class="form-label small mb-0">مانده تا تاریخ</label><FnDate v-model="asOf" /></div><button class="btn btn-sm btn-secondary" @click="loadProvisions">نمایش</button></div>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>نوع</th><th>شرح</th><th>مانده (ریال)</th><th>تاریخ بازبینی</th><th>فعال</th><th>حساب هزینه</th><th>حساب بدهی</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!provisions.length"><td colspan="9" class="text-center inv-muted">ذخیره‌ای نیست.</td></tr>
            <template v-for="p in provisions" :key="p.id">
              <tr>
                <td class="inv-ltr">{{ p.number }}</td><td>{{ provisionKinds[p.kind] }}</td><td>{{ p.description }}</td><td class="fn-num">{{ money(p.balance) }}</td>
                <td>{{ jalali(p.reviewDate) }} <span v-if="p.reviewDue" class="inv-badge warn">سررسید بازبینی</span></td><td>{{ p.isActive ? "بله" : "خیر" }}</td><td class="inv-ltr">{{ p.expenseAccountCode }}</td><td class="inv-ltr">{{ p.liabilityAccountCode }}</td>
                <td v-if="canWrite" class="text-nowrap"><button class="btn btn-sm btn-outline-secondary me-1" @click="pForm = { ...p, reviewDate: p.reviewDate || null }">ویرایش</button><button class="btn btn-sm btn-outline-primary" @click="mForm = { provisionId: p.id, date: today(), direction: 'add', amount: '', reason: '', counterAccountCode: '' }">حرکت</button></td>
              </tr>
              <tr v-for="(m, i) in p.movements" :key="p.id + '-' + i" class="inv-muted"><td></td><td colspan="2">{{ m.reason }}</td><td class="fn-num">{{ money(m.amount) }}</td><td>{{ jalali(m.date) }}</td><td colspan="2">سند {{ m.voucherId }}</td><td colspan="2" class="inv-ltr">{{ m.counterAccountCode }}</td></tr>
            </template>
          </tbody>
        </table>
        <p v-if="tResult">تعدیل ذخیره حقوق ({{ tResult.kind }}): مانده قبلی {{ money(tResult.currentBalance) }} - اختلاف {{ money(tResult.difference) }} - سند {{ tResult.voucherId }}</p>
      </template>

      <template v-else>
        <p class="inv-muted">اسناد تعهدی که برای برگشت خودکار در دوره بعد علامت خورده‌اند، در دوره انتخابی به‌صورت پیش‌نویس برگشت می‌خورند.</p>
        <div class="inv-filters mb-3">
          <select v-model="periodId" class="form-select form-select-sm"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="reverse">اجرای برگشت</button>
        </div>
        <template v-if="revResult">
          <p>اسناد ساخته‌شده: <span v-for="id in revResult.createdVoucherIds" :key="id" class="inv-badge me-1">{{ id }}</span><span v-if="!revResult.createdVoucherIds.length">-</span></p>
          <ul v-if="revResult.skipped.length"><li v-for="(s, i) in revResult.skipped" :key="i">{{ s }}</li></ul>
        </template>
      </template>
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2"><span class="inv-ltr">{{ detail.number }}</span> - {{ detail.description }} <span class="inv-badge">{{ deferralStatuses[detail.status] }}</span></h2>
        <p>مبلغ کل {{ money(detail.totalAmount) }} - مستهلک‌شده {{ money(detail.amortised) }} - باقی‌مانده {{ money(detail.remaining) }} - حساب مانده <span class="inv-ltr">{{ detail.balanceAccountCode }}</span> - حساب نتیجه <span class="inv-ltr">{{ detail.resultAccountCode }}</span></p>
        <table class="table table-sm">
          <thead><tr><th>ردیف</th><th>دوره</th><th>مبلغ</th><th>وضعیت</th><th>سند</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-for="e in detail.entries" :key="e.id"><td>{{ e.sequence }}</td><td>{{ periods.find((p) => p.id === e.periodId)?.title || e.periodId }}</td><td class="fn-num">{{ money(e.amount) }}</td><td>{{ draftApproved[e.status] }}</td><td>{{ e.voucherId }}</td><td v-if="canWrite"><button v-if="e.status === 0" :disabled="busy" class="btn btn-sm btn-success" @click="approveEntry(e)">تایید و صدور سند</button></td></tr>
          </tbody>
        </table>
        <div class="d-flex gap-2 justify-content-end"><button v-if="canManage && detail.status === 1" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="cancel">لغو برنامه</button><button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button></div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">برنامه هزینه / درآمد انتقالی</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in deferralKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">مبلغ کل (ریال) *</label><input v-model="form.totalAmount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرح *</label><input v-model="form.description" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="form.startDate" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تا تاریخ *</label><FnDate v-model="form.endDate" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">حساب مانده (پیش‌پرداخت / درآمد انتقالی) *</label><input v-model="form.balanceAccountCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">حساب نتیجه (هزینه / درآمد) *</label><input v-model="form.resultAccountCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مرکز هزینه</label><input v-model="form.costCenterCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">پروژه</label><ContractPick v-model="form.contractId" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>

    <div v-if="pForm" class="inv-modal-backdrop screen-only" @click.self="pForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ذخیره</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">نوع</label><select v-model="pForm.kind" class="form-select form-select-sm"><option v-for="(t, k) in provisionKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ بازبینی</label><FnDate v-model="pForm.reviewDate" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرح *</label><input v-model="pForm.description" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">حساب هزینه *</label><input v-model="pForm.expenseAccountCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">حساب بدهی *</label><input v-model="pForm.liabilityAccountCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label><input v-model="pForm.isActive" type="checkbox" /> فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="pForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveP">ذخیره</button></div>
      </div>
    </div>

    <div v-if="mForm" class="inv-modal-backdrop screen-only" @click.self="mForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">حرکت ذخیره</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">نوع حرکت</label><select v-model="mForm.direction" class="form-select form-select-sm"><option value="add">افزایش ذخیره (بدهکار هزینه)</option><option value="use">مصرف یا برگشت ذخیره</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ *</label><FnDate v-model="mForm.date" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مبلغ (ریال) *</label><input v-model="mForm.amount" class="form-control form-control-sm fn-num" /></div>
          <div v-if="mForm.direction === 'use'" class="col-md-6"><label class="form-label small mb-0">حساب مقابل (پیش‌فرض: حساب هزینه)</label><input v-model="mForm.counterAccountCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label class="form-label small mb-0">دلیل *</label><input v-model="mForm.reason" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="mForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveM">ثبت و صدور سند</button></div>
      </div>
    </div>

    <div v-if="tForm" class="inv-modal-backdrop screen-only" @click.self="tForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">تعدیل ذخایر حقوق</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">نوع</label><select v-model="tForm.kind" class="form-select form-select-sm"><option value="bonus">پاداش</option><option value="leave">مرخصی</option><option value="severance">سنوات</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ *</label><FnDate v-model="tForm.date" /></div>
          <div class="col-12"><label class="form-label small mb-0">مانده لازم ذخیره (ریال، جمع کل) *</label><input v-model="tForm.requiredBalance" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح محاسبه *</label><input v-model="tForm.reason" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="tForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="trueUp">ثبت تعدیل</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
