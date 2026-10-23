<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ExcelRows, { type ImportField } from "@/components/fn/ExcelRows.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { createImportedYear, getFiscalYears, importHistoricalVouchers, importSepidarAccounts, importSepidarDetails, importSepidarOpening } from "@/api/FnAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { jalaliYearOf, money, parseAmount, yesNo } from "@/composables/fn";
import { importRowStatuses } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Migration wizard from Sepidar (finance manager): chart of accounts, detail accounts, opening balances (always a dry run first), imported years, voucher history.
// Each import step first runs a dry run (nothing is saved) and shows the report; only a clean report can be committed.
const tab = ref("accounts");
const tabs = [{ key: "accounts", title: "۱. سرفصل حساب‌ها" }, { key: "details", title: "۲. تفصیلی‌ها" }, { key: "opening", title: "۳. مانده افتتاحیه" }, { key: "years", title: "۴. سال‌های واردشده" }, { key: "vouchers", title: "۵. اسناد سال‌های قبل" }];
const canRun = hasRole(FnRoles.managers);
const busy = ref(false);
const years = ref<any[]>([]);

const accountFields: ImportField[] = [
  { key: "code", label: "کد حساب سپیدار *", words: ["کد", "کد حساب", "code"], required: true },
  { key: "title", label: "عنوان *", words: ["عنوان", "نام", "title"], required: true },
  { key: "level", label: "سطح (۱ تا ۳)", words: ["سطح", "level"], type: "number" },
  { key: "nature", label: "ماهیت", words: ["ماهیت", "nature"] },
  { key: "pmisCode", label: "کد حساب PMIS (نگاشت)", words: ["کد pmis", "pmis", "pmiscode"] },
];
const detailFields: ImportField[] = [
  { key: "code", label: "کد تفصیلی *", words: ["کد", "کد تفصیلی", "code"], required: true },
  { key: "title", label: "عنوان *", words: ["عنوان", "نام", "title"], required: true },
  { key: "type", label: "نوع", words: ["نوع", "type"] },
];
const openingFields: ImportField[] = [
  { key: "accountCode", label: "کد حساب *", words: ["کد حساب", "حساب", "account", "accountcode"], required: true },
  { key: "detailCode", label: "کد تفصیلی", words: ["کد تفصیلی", "تفصیلی", "detail", "detailcode"] },
  { key: "debit", label: "بدهکار", words: ["بدهکار", "debit"], type: "number" },
  { key: "credit", label: "بستانکار", words: ["بستانکار", "credit"], type: "number" },
];
const voucherFields: ImportField[] = [
  { key: "date", label: "تاریخ *", words: ["تاریخ", "date"], required: true, type: "date" },
  { key: "number", label: "شماره سند *", words: ["شماره سند", "شماره", "number"], required: true, type: "number" },
  { key: "description", label: "شرح", words: ["شرح", "description"] },
  { key: "accountCode", label: "کد حساب *", words: ["کد حساب", "حساب", "account", "accountcode"], required: true },
  { key: "detailCode", label: "کد تفصیلی", words: ["کد تفصیلی", "تفصیلی", "detail", "detailcode"] },
  { key: "debit", label: "بدهکار", words: ["بدهکار", "debit"], type: "number" },
  { key: "credit", label: "بستانکار", words: ["بستانکار", "credit"], type: "number" },
];

const data = ref<Record<string, any[]>>({ accounts: [], details: [], opening: [], vouchers: [] });
const reports = ref<Record<string, any>>({ accounts: null, details: null, opening: null, vouchers: null });
const opening = ref<{ fiscalYearId: number | null; description: string }>({ fiscalYearId: null, description: "سند افتتاحیه (ورود از سپیدار)" });
const historical = ref<{ fiscalYearId: number | null }>({ fiscalYearId: null });
const yearForm = ref<{ year: string; title: string; startDate: string | null; endDate: string | null }>({ year: "", title: "", startDate: null, endDate: null });
const createdYear = ref<any>(null);

const setRows = (key: string, rows: any[]) => {
  data.value[key] = rows;
  reports.value[key] = null;
};

async function loadYears() {
  const [d] = await getFiscalYears();
  years.value = d || [];
}

async function runAccounts(commit: boolean) {
  if (!data.value.accounts.length) return fail("ابتدا فایل را انتخاب کنید.");
  const rows = data.value.accounts.map((r) => ({ code: r.code, title: r.title, level: r.level || 3, nature: r.nature, pmisCode: r.pmisCode }));
  busy.value = true;
  const [d, error] = await importSepidarAccounts(rows, commit);
  busy.value = false;
  if (error) return fail(error);
  reports.value.accounts = d;
  ok(commit ? "سرفصل‌ها وارد شد." : "بررسی انجام شد.");
}

async function runDetails(commit: boolean) {
  if (!data.value.details.length) return fail("ابتدا فایل را انتخاب کنید.");
  const rows = data.value.details.map((r) => ({ code: r.code, title: r.title, type: r.type }));
  busy.value = true;
  const [d, error] = await importSepidarDetails(rows, commit);
  busy.value = false;
  if (error) return fail(error);
  reports.value.details = d;
  ok(commit ? "تفصیلی‌ها وارد شد." : "بررسی انجام شد.");
}

async function runOpening(commit: boolean) {
  if (!data.value.opening.length) return fail("ابتدا فایل را انتخاب کنید.");
  const rows = data.value.opening.map((r) => ({ accountCode: r.accountCode, detailCode: r.detailCode, debit: r.debit || 0, credit: r.credit || 0 }));
  busy.value = true;
  const [d, error] = await importSepidarOpening({ fiscalYearId: opening.value.fiscalYearId, description: opening.value.description, rows }, commit);
  busy.value = false;
  if (error) return fail(error);
  reports.value.opening = d;
  ok(commit ? (d.committed ? "سند افتتاحیه ثبت شد." : "ثبت انجام نشد؛ خطاها را ببینید.") : "بررسی انجام شد.");
}

async function createYear() {
  const f = yearForm.value;
  const year = parseAmount(f.year);
  if (!year || !f.startDate || !f.endDate) return fail("سال، تاریخ شروع و پایان را وارد کنید.");
  busy.value = true;
  const [d, error] = await createImportedYear({ year, title: f.title || "سال مالی " + year, startDate: f.startDate, endDate: f.endDate });
  busy.value = false;
  if (error) return fail(error);
  createdYear.value = d;
  ok("سال واردشده ساخته شد.");
  loadYears();
}

async function runVouchers(dryRun: boolean) {
  if (!historical.value.fiscalYearId) return fail("سال مالی واردشده را انتخاب کنید.");
  if (!data.value.vouchers.length) return fail("ابتدا فایل را انتخاب کنید.");
  if (data.value.vouchers.some((r) => !r.date)) return fail("تاریخ برخی ردیف‌ها خوانده نشد؛ تاریخ شمسی بنویسید.");
  const rows = data.value.vouchers.map((r) => ({ date: r.date, number: r.number || 0, description: r.description, accountCode: r.accountCode, detailCode: r.detailCode, debit: r.debit || 0, credit: r.credit || 0 }));
  busy.value = true;
  const [d, error] = await importHistoricalVouchers({ fiscalYearId: historical.value.fiscalYearId, dryRun, rows });
  busy.value = false;
  if (error) return fail(error);
  reports.value.vouchers = d;
  ok(dryRun ? "بررسی انجام شد." : "اسناد وارد شد.");
}

const canCommitList = (r: any) => !!r && r.dryRun && !r.committed && !(r.errors > 0);
const canCommitOpening = (r: any) => !!r && r.dryRun && r.balanced && !r.errors.length && !r.unknownAccounts.length && !r.unknownDetails.length;

onMounted(loadYears);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">انتقال از سپیدار: ورود داده‌های اولیه</h1></div>
      <p v-if="!canRun" class="alert alert-warning py-2">ورود اطلاعات فقط با مدیر مالی است. این صفحه برای شما فقط نمایشی است.</p>
      <PcTabs v-model="tab" :tabs="tabs" />

      <!-- accounts and details: the same report -->
      <template v-for="key in ['accounts', 'details']" :key="key">
        <template v-if="tab === key">
          <ExcelRows :disabled="!canRun" :fields="key === 'accounts' ? accountFields : detailFields" :hint="key === 'accounts' ? 'اگر کد PMIS داده شود حساب سپیدار به آن نگاشت می‌شود؛ وگرنه حساب جدید زیر والدش ساخته می‌شود.' : 'نوع خالی از روی عنوان حدس زده می‌شود و در صورت ابهام «نیازمند بررسی» می‌شود.'" @rows="(r: any[]) => setRows(key, r)" />
          <div class="d-flex gap-2 mb-3">
            <button :disabled="!canRun || busy" class="btn btn-sm btn-outline-primary" @click="key === 'accounts' ? runAccounts(false) : runDetails(false)">بررسی (بدون ثبت)</button>
            <button :disabled="!canRun || busy || !canCommitList(reports[key])" class="btn btn-sm btn-primary" @click="key === 'accounts' ? runAccounts(true) : runDetails(true)">ثبت نهایی</button>
          </div>
          <div v-if="reports[key]">
            <p>
              <span :class="reports[key].errors ? 'bad' : 'ok'" class="inv-badge">{{ reports[key].committed ? "ثبت شد" : "نتیجه بررسی" }}</span>
              کل: <b>{{ reports[key].total }}</b> - موجود: {{ reports[key].existing }} - نگاشت‌شده: {{ reports[key].mapped }} - جدید: {{ reports[key].new }} - نیازمند بررسی: {{ reports[key].review }} - خطا: <b>{{ reports[key].errors }}</b>
            </p>
            <div class="table-responsive" style="max-height: 360px; overflow: auto">
              <table class="table table-sm">
                <thead><tr><th>ردیف</th><th>کد</th><th>عنوان</th><th>وضعیت</th><th>کد PMIS</th><th>نوع حدس‌زده</th><th>پیام</th></tr></thead>
                <tbody>
                  <tr v-for="r in reports[key].rows" :key="r.row" :class="{ 'table-danger': r.status === 'Error', 'table-warning': r.status === 'Review' }">
                    <td>{{ r.row }}</td><td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td>{{ importRowStatuses[r.status] || r.status }}</td><td class="inv-ltr">{{ r.pmisCode }}</td><td>{{ r.guessedType }}</td><td>{{ r.message }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </template>
      </template>

      <!-- opening balances -->
      <template v-if="tab === 'opening'">
        <div class="row g-2 mb-2">
          <div class="col-md-4"><label class="form-label small mb-0">سال مالی</label><select v-model="opening.fiscalYearId" class="form-select form-select-sm"><option :value="null">سال مالی جاری</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select></div>
          <div class="col-md-8"><label class="form-label small mb-0">شرح سند افتتاحیه</label><input v-model="opening.description" class="form-control form-control-sm" /></div>
        </div>
        <ExcelRows :disabled="!canRun" :fields="openingFields" hint="مانده‌ها باید متوازن باشند و هر کد حساب و تفصیلی باید در کدینگ PMIS (یا نگاشت) باشد." @rows="(r: any[]) => setRows('opening', r)" />
        <div class="d-flex gap-2 mb-3">
          <button :disabled="!canRun || busy" class="btn btn-sm btn-outline-primary" @click="runOpening(false)">گزارش بررسی (بدون ثبت)</button>
          <button :disabled="!canRun || busy || !canCommitOpening(reports.opening)" class="btn btn-sm btn-primary" @click="runOpening(true)">ثبت سند افتتاحیه</button>
        </div>
        <div v-if="reports.opening">
          <p>
            <span :class="reports.opening.balanced ? 'ok' : 'bad'" class="inv-badge">{{ reports.opening.committed ? "ثبت شد" : reports.opening.balanced ? "متوازن" : "نامتوازن" }}</span>
            ردیف: {{ reports.opening.rows }} - جمع بدهکار: <b>{{ money(reports.opening.totalDebit) }}</b> - جمع بستانکار: <b>{{ money(reports.opening.totalCredit) }}</b> - اختلاف: <b>{{ money(reports.opening.difference) }}</b>
            - سند افتتاحیه قبلی: {{ yesNo(reports.opening.alreadyHasOpening) }}
          </p>
          <div v-if="reports.opening.unknownAccounts.length" class="alert alert-danger py-2">حساب‌های ناشناخته: <span class="inv-ltr">{{ reports.opening.unknownAccounts.join("، ") }}</span></div>
          <div v-if="reports.opening.unknownDetails.length" class="alert alert-danger py-2">تفصیلی‌های ناشناخته: <span class="inv-ltr">{{ reports.opening.unknownDetails.join("، ") }}</span></div>
          <ul v-if="reports.opening.errors.length" class="text-danger"><li v-for="(e, i) in reports.opening.errors" :key="i">{{ e }}</li></ul>
          <p v-if="reports.opening.voucherId">سند افتتاحیه شماره {{ reports.opening.voucherId }} ساخته شد.</p>
        </div>
      </template>

      <!-- imported years -->
      <template v-if="tab === 'years'">
        <p class="inv-muted">برای ورود اسناد سال‌های قبل، یک سال مالی «واردشده» بسازید. این سال فقط خواندنی است و در بستن سال و ترازها حساب می‌شود.</p>
        <div class="row g-2 mb-2">
          <div class="col-md-2"><label class="form-label small mb-0">سال (مثل ۱۴۰۳) *</label><input v-model="yearForm.year" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">عنوان</label><input v-model="yearForm.title" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">شروع *</label><FnDate v-model="yearForm.startDate" :disabled="!canRun" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">پایان *</label><FnDate v-model="yearForm.endDate" :disabled="!canRun" /></div>
          <div class="col-md-1 d-flex align-items-end"><button :disabled="!canRun || busy" class="btn btn-sm btn-primary" @click="createYear">ایجاد</button></div>
        </div>
        <p v-if="createdYear" class="alert alert-success py-2">سال «{{ createdYear.title }}» ایجاد شد.</p>
        <table class="table table-sm">
          <thead><tr><th>سال مالی</th><th>از</th><th>تا</th><th>واردشده</th></tr></thead>
          <tbody>
            <tr v-for="y in years" :key="y.id"><td>{{ y.title }} <span class="inv-muted">({{ jalaliYearOf(y.startDate) }})</span></td><td>{{ jalali(y.startDate) }}</td><td>{{ jalali(y.endDate) }}</td><td>{{ yesNo(y.isImported) }}</td></tr>
          </tbody>
        </table>
      </template>

      <!-- historical vouchers -->
      <template v-if="tab === 'vouchers'">
        <div class="row g-2 mb-2">
          <div class="col-md-5"><label class="form-label small mb-0">سال مالی واردشده *</label><select v-model="historical.fiscalYearId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="y in years.filter((x) => x.isImported)" :key="y.id" :value="y.id">{{ y.title }}</option></select></div>
        </div>
        <ExcelRows :disabled="!canRun" :fields="voucherFields" hint="ردیف‌های هم‌شماره یک سند هستند و هر سند باید متوازن باشد." @rows="(r: any[]) => setRows('vouchers', r)" />
        <div class="d-flex gap-2 mb-3">
          <button :disabled="!canRun || busy" class="btn btn-sm btn-outline-primary" @click="runVouchers(true)">بررسی (بدون ثبت)</button>
          <button :disabled="!canRun || busy || !reports.vouchers || !reports.vouchers.dryRun || reports.vouchers.errors.length > 0" class="btn btn-sm btn-primary" @click="runVouchers(false)">ثبت اسناد</button>
        </div>
        <div v-if="reports.vouchers">
          <p>
            <span :class="reports.vouchers.errors.length ? 'bad' : 'ok'" class="inv-badge">{{ reports.vouchers.committed ? "ثبت شد" : "نتیجه بررسی" }}</span>
            اسناد: <b>{{ reports.vouchers.vouchers }}</b> - ردیف: {{ reports.vouchers.lines }} - جمع بدهکار: {{ money(reports.vouchers.totalDebit) }} - جمع بستانکار: {{ money(reports.vouchers.totalCredit) }}
          </p>
          <ul v-if="reports.vouchers.errors.length" class="text-danger"><li v-for="(e, i) in reports.vouchers.errors" :key="i">{{ e }}</li></ul>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
