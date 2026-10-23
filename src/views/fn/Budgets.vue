<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { approveBudget, checkCommitment, downloadBudgetCsv, getBudgetReport, getBudget, getBudgets, getFiscalYears, importBudgetCsv, reviseBudget, saveBudget } from "@/api/FnAPIs";
import { fail, ok, today } from "@/composables/inventory";
import { money, monthNames, parseAmount, printSheet, yesNo } from "@/composables/fn";
import BudgetReportPrint from "@/components/fn/print/BudgetReportPrint.vue";
import FnDate from "@/components/fn/FnDate.vue";
import { budgetGroupBys, budgetKinds, budgetStatuses, commitModes } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Budgets: versions per fiscal year (annual, revision), lines by account, cost centre, project and month, CSV import / export, and the two signatures (finance manager and CEO).
const years = ref<any[]>([]);
const fiscalYearId = ref<number | null>(null);
const list = ref<any[]>([]);
const detail = ref<any>(null);
const form = ref<any>(null);
const csv = ref<null | { text: string }>(null);
const busy = ref(false);
const tab = ref<"list" | "report" | "commit">("list");
const rep = ref<any>({ budgetId: null, fromPeriod: "1", toPeriod: "12", groupBy: "account", forecast: "Remaining" });
const report = ref<any>(null);
const printing = ref(false);
const chk = ref<any>({ accountCode: "", costCenterCode: "", contractId: "", amount: "", date: "", overrideReason: "" });
const chkResult = ref<any>(null);
const canWrite = hasRole(FnRoles.accountants);
const canApprove = hasRole(FnRoles.budgetApprovers);

const formTotal = computed(() => (form.value ? form.value.lines.reduce((s: number, l: any) => s + (parseAmount(l.amount) || 0), 0) : 0));

async function load() {
  const [d, error] = await getBudgets(fiscalYearId.value);
  if (error) return fail(error);
  list.value = d || [];
}
async function open(id: number) {
  const [d, error] = await getBudget(id);
  if (error) return fail(error);
  detail.value = d;
}

const yearTitle = (id: number) => years.value.find((y) => y.id === id)?.title || String(id);
const blankLine = () => ({ accountCode: "", costCenterCode: "", contractId: "", period: "1", amount: "", isCapital: false, note: "" });

function edit(b?: any) {
  form.value = b
    ? {
        id: b.id, fiscalYearId: b.fiscalYearId, title: b.title || "", kind: b.kind, commitMode: b.commitMode,
        lines: b.lines.map((l: any) => ({ accountCode: l.accountCode || "", costCenterCode: l.costCenterCode || "", contractId: l.contractId ? String(l.contractId) : "", period: String(l.period), amount: String(l.amount), isCapital: l.isCapital, note: l.note || "" })),
      }
    : { id: null, fiscalYearId: fiscalYearId.value || years.value[0]?.id || null, title: "", kind: 1, commitMode: 1, lines: [blankLine()] };
  detail.value = null;
}

async function save() {
  const f = form.value;
  if (!f.fiscalYearId || !String(f.title).trim()) return fail("سال مالی و عنوان بودجه را وارد کنید.");
  const lines = f.lines.filter((l: any) => l.accountCode || parseAmount(l.amount)).map((l: any) => ({
    accountCode: String(l.accountCode).trim(), costCenterCode: String(l.costCenterCode || "").trim() || null, contractId: parseAmount(l.contractId) || null, period: Number(l.period), amount: parseAmount(l.amount) || 0, isCapital: l.isCapital, note: l.note || null,
  }));
  if (!lines.length) return fail("بودجه حداقل یک ردیف لازم دارد.");
  if (lines.some((l: any) => !l.accountCode || l.period < 1 || l.period > 12)) return fail("کد حساب و ماه (۱ تا ۱۲) هر ردیف را درست وارد کنید.");
  busy.value = true;
  const [d, error] = await saveBudget({ id: f.id, fiscalYearId: Number(f.fiscalYearId), title: f.title, kind: Number(f.kind), commitMode: Number(f.commitMode), lines });
  busy.value = false;
  if (error) return fail(error);
  ok("بودجه ذخیره شد.");
  form.value = null;
  detail.value = d;
  load();
}

async function onCsvFile(event: any) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  csv.value = { text: await file.text() };
}
async function doImport() {
  if (!csv.value?.text.trim()) return fail("متن فایل خالی است.");
  busy.value = true;
  const [d, error] = await importBudgetCsv(detail.value.id, csv.value.text);
  busy.value = false;
  if (error) return fail(error);
  ok("ردیف‌های بودجه از فایل وارد شد.");
  csv.value = null;
  detail.value = d;
  load();
}
async function exportCsv() {
  const message = await downloadBudgetCsv(detail.value.id);
  if (message) fail(message);
  else ok("فایل CSV ذخیره شد.");
}

async function approve() {
  busy.value = true;
  const [d, error] = await approveBudget(detail.value.id);
  busy.value = false;
  if (error) return fail(error);
  ok(d.status === 1 ? "بودجه با دو امضا تایید شد." : "امضای شما ثبت شد؛ امضای دوم لازم است.");
  detail.value = d;
  load();
}
async function revise() {
  busy.value = true;
  const [d, error] = await reviseBudget(detail.value.id);
  busy.value = false;
  if (error) return fail(error);
  ok("پیش‌نویس بازنگری ساخته شد.");
  detail.value = d;
  load();
}

const approvedBudgets = computed(() => list.value.filter((b) => b.status === 1));
const reportTitle = computed(() => {
  const b = list.value.find((x) => x.id === report.value?.budgetId);
  return b ? `${b.number} - ${b.title}` : "";
});

async function runReport() {
  if (!rep.value.budgetId) return fail("بودجه را انتخاب کنید.");
  const from = Number(rep.value.fromPeriod), to = Number(rep.value.toPeriod);
  if (from > to) return fail("ماه شروع باید پیش از ماه پایان باشد.");
  busy.value = true;
  const [d, error] = await getBudgetReport({ budgetId: rep.value.budgetId, fromPeriod: from, toPeriod: to, groupBy: rep.value.groupBy, forecast: rep.value.forecast });
  busy.value = false;
  if (error) return fail(error);
  report.value = d;
}
async function printReport() {
  printing.value = true;
  await nextTick();
  printSheet();
}
async function runCheck() {
  const c = chk.value;
  if (!c.accountCode.trim() || !parseAmount(c.amount)) return fail("کد حساب و مبلغ را وارد کنید.");
  busy.value = true;
  const [d, error] = await checkCommitment({ accountCode: c.accountCode.trim(), costCenterCode: c.costCenterCode.trim() || null, contractId: parseAmount(c.contractId) || null, amount: parseAmount(c.amount), date: c.date || today(), overrideReason: c.overrideReason.trim() || null });
  busy.value = false;
  if (error) return fail(error);
  chkResult.value = d;
}

onMounted(async () => {
  const [y] = await getFiscalYears();
  years.value = y || [];
  load();
});
</script>

<template>
  <div class="inv-page">
    <BudgetReportPrint v-if="printing && report" :data="report" :title="reportTitle" />
    <div class="inv-card screen-only">
      <ul class="nav nav-tabs mb-3">
        <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'list' }" href="#" @click.prevent="tab = 'list'">نسخه‌های بودجه</a></li>
        <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'report' }" href="#" @click.prevent="tab = 'report'">بودجه در برابر عملکرد</a></li>
        <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'commit' }" href="#" @click.prevent="tab = 'commit'">کنترل تعهد</a></li>
      </ul>

      <div v-if="tab === 'report'">
        <div class="row g-2 mb-2">
          <div class="col-md-3"><label class="form-label small mb-0">بودجه تاییدشده</label><select v-model="rep.budgetId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="b in approvedBudgets" :key="b.id" :value="b.id">{{ b.number }} - {{ b.title }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">از ماه</label><select v-model="rep.fromPeriod" class="form-select form-select-sm"><option v-for="(m, k) in monthNames" :key="k" :value="String(k + 1)">{{ m }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">تا ماه</label><select v-model="rep.toPeriod" class="form-select form-select-sm"><option v-for="(m, k) in monthNames" :key="k" :value="String(k + 1)">{{ m }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">گروه‌بندی</label><select v-model="rep.groupBy" class="form-select form-select-sm"><option v-for="(t, k) in budgetGroupBys" :key="k" :value="k">{{ t }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">پیش‌بینی پایان سال</label><select v-model="rep.forecast" class="form-select form-select-sm"><option value="Remaining">بودجه باقی‌مانده</option><option value="RunRate">روند جاری</option></select></div>
          <div class="col-md-1 d-flex align-items-end"><button :disabled="busy" class="btn btn-sm btn-primary w-100" @click="runReport">نمایش</button></div>
        </div>
        <template v-if="report">
          <div class="d-flex justify-content-between mb-2">
            <span>جمع بودجه: <b>{{ money(report.totalBudget) }}</b> - جمع عملکرد: <b>{{ money(report.totalActual) }}</b> - انحراف: <b>{{ money(report.totalVariance) }}</b> - عملکرد خارج از بودجه: <b>{{ money(report.unbudgetedActual) }}</b> ریال</span>
            <button class="btn btn-sm btn-outline-secondary" @click="printReport">چاپ</button>
          </div>
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>عنوان</th><th>بودجه</th><th>عملکرد</th><th>انحراف</th><th>درصد</th><th>بودجه کل سال</th><th>پیش‌بینی</th></tr></thead>
            <tbody>
              <tr v-if="!report.rows.length"><td colspan="8" class="text-center inv-muted">داده‌ای نیست.</td></tr>
              <tr v-for="r in report.rows" :key="r.key"><td class="inv-ltr">{{ r.key }}</td><td>{{ r.title }}</td><td class="fn-num">{{ money(r.budget) }}</td><td class="fn-num">{{ money(r.actual) }}</td><td class="fn-num" :class="r.variance < 0 ? 'text-danger' : ''">{{ money(r.variance) }}</td><td class="fn-num">{{ Number(r.variancePercent).toFixed(1) }}%</td><td class="fn-num">{{ money(r.fullYearBudget) }}</td><td class="fn-num">{{ money(r.forecast) }}</td></tr>
            </tbody>
          </table>
        </template>
      </div>

      <div v-else-if="tab === 'commit'">
        <p class="inv-muted">بررسی اینکه مبلغ پیشنهادی با بودجه تاییدشده سازگار است (نتیجه بر اساس حالت کنترل تعهد بودجه: هشدار یا مسدودکننده).</p>
        <div class="row g-2 mb-2">
          <div class="col-md-2"><label class="form-label small mb-0">کد حساب *</label><input v-model="chk.accountCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">مرکز هزینه</label><input v-model="chk.costCenterCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">شناسه پروژه</label><input v-model="chk.contractId" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">مبلغ (ریال) *</label><input v-model="chk.amount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="chk.date" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">دلیل عبور از محدودیت</label><input v-model="chk.overrideReason" class="form-control form-control-sm" /></div>
        </div>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="runCheck">بررسی</button>
        <div v-if="chkResult" class="mt-3 p-2 border rounded">
          <div><span :class="chkResult.allowed ? 'ok' : 'warn'" class="inv-badge">{{ chkResult.allowed ? "مجاز" : "غیرمجاز" }}</span> <span v-if="chkResult.warning" class="inv-badge warn">هشدار</span> <span v-if="chkResult.overridden" class="inv-badge">عبور با دلیل</span> - حالت کنترل: {{ commitModes[chkResult.mode] }} - بودجه دارد: {{ yesNo(chkResult.hasBudget) }}</div>
          <div class="mt-1">بودجه تا تاریخ: {{ money(chkResult.budgetToDate) }} - هزینه تا تاریخ: {{ money(chkResult.actualToDate) }} - باقی‌مانده: {{ money(chkResult.remaining) }} - مبلغ درخواستی: {{ money(chkResult.amount) }}</div>
          <div v-if="chkResult.message" class="mt-1">{{ chkResult.message }}</div>
        </div>
      </div>

      <template v-else>
      <div class="inv-toolbar">
        <h1 class="inv-title">بودجه</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="edit()">بودجه جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="fiscalYearId" class="form-select form-select-sm" @change="load"><option :value="null">همه سال‌ها</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>عنوان</th><th>سال مالی</th><th>نسخه</th><th>نوع</th><th>کنترل تعهد</th><th>جمع (ریال)</th><th>وضعیت</th><th>امضاها</th></tr></thead>
        <tbody>
          <tr v-if="!list.length"><td colspan="9" class="text-center inv-muted">بودجه‌ای نیست.</td></tr>
          <tr v-for="b in list" :key="b.id" style="cursor: pointer" @click="open(b.id)">
            <td class="inv-ltr">{{ b.number }}</td><td>{{ b.title }}</td><td>{{ yearTitle(b.fiscalYearId) }}</td><td>{{ b.version }}</td><td>{{ budgetKinds[b.kind] }}</td><td>{{ commitModes[b.commitMode] }}</td><td class="fn-num">{{ money(b.total) }}</td>
            <td><span :class="b.status === 1 ? 'ok' : b.status === 2 ? '' : 'warn'" class="inv-badge">{{ budgetStatuses[b.status] }}</span></td>
            <td>مدیر مالی: {{ yesNo(!!b.managerApprovedByUserId) }} - مدیرعامل: {{ yesNo(!!b.ceoApprovedByUserId) }}</td>
          </tr>
        </tbody>
      </table>
      </template>
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">بودجه <span class="inv-ltr">{{ detail.number }}</span> - {{ detail.title }} <span :class="detail.status === 1 ? 'ok' : 'warn'" class="inv-badge">{{ budgetStatuses[detail.status] }}</span></h2>
        <p>
          سال مالی: {{ yearTitle(detail.fiscalYearId) }} - نسخه {{ detail.version }} ({{ budgetKinds[detail.kind] }}) - کنترل تعهد: {{ commitModes[detail.commitMode] }} - جمع: <b>{{ money(detail.total) }}</b> ریال -
          امضای مدیر مالی: {{ yesNo(!!detail.managerApprovedByUserId) }} - امضای مدیرعامل: {{ yesNo(!!detail.ceoApprovedByUserId) }}
        </p>
        <div class="table-responsive" style="max-height: 340px; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>حساب</th><th>مرکز هزینه</th><th>پروژه</th><th>ماه</th><th>مبلغ (ریال)</th><th>سرمایه‌ای</th><th>توضیح</th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in detail.lines" :key="i"><td class="inv-ltr">{{ l.accountCode }}</td><td class="inv-ltr">{{ l.costCenterCode }}</td><td>{{ l.contractId }}</td><td>{{ monthNames[l.period - 1] }}</td><td class="fn-num">{{ money(l.amount) }}</td><td>{{ yesNo(l.isCapital) }}</td><td>{{ l.note }}</td></tr>
            </tbody>
          </table>
        </div>
        <div v-if="csv" class="border rounded p-2 mt-2">
          <p class="inv-muted mb-1">ستون‌ها: accountCode,costCenterCode,contractId,period,amount,isCapital,note (ماه ۱ تا ۱۲). ردیف‌های فایل جایگزین ردیف‌های فعلی می‌شود.</p>
          <textarea v-model="csv.text" class="form-control form-control-sm inv-ltr" rows="5"></textarea>
          <div class="d-flex gap-2 justify-content-end mt-2"><button class="btn btn-sm btn-outline-secondary" @click="csv = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="doImport">وارد کردن ردیف‌ها</button></div>
        </div>
        <div class="d-flex gap-2 justify-content-end flex-wrap mt-2">
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
          <button class="btn btn-sm btn-outline-secondary" @click="exportCsv">خروجی CSV</button>
          <template v-if="detail.status === 0">
            <label v-if="canWrite" class="btn btn-sm btn-outline-secondary mb-0">ورود از فایل CSV<input accept=".csv,.txt" hidden type="file" @change="onCsvFile" /></label>
            <button v-if="canWrite" class="btn btn-sm btn-outline-primary" @click="edit(detail)">ویرایش</button>
            <button v-if="canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="approve">تایید (امضای من)</button>
          </template>
          <button v-if="canWrite && detail.status === 1" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="revise">ساخت بازنگری</button>
        </div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش بودجه" : "بودجه جدید" }}</h2>
        <div class="row g-2 mb-2">
          <div class="col-md-3"><label class="form-label small mb-0">سال مالی *</label><select v-model="form.fiscalYearId" :disabled="!!form.id" class="form-select form-select-sm"><option :value="null">-</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in budgetKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">کنترل تعهد</label><select v-model="form.commitMode" class="form-select form-select-sm"><option v-for="(t, k) in commitModes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        </div>
        <div class="table-responsive" style="max-height: 320px; overflow: auto">
          <table class="table table-sm fn-lines">
            <thead><tr><th>حساب *</th><th>مرکز هزینه</th><th>شناسه پروژه</th><th>ماه *</th><th>مبلغ (ریال)</th><th>سرمایه‌ای</th><th>توضیح</th><th></th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in form.lines" :key="i">
                <td><input v-model="l.accountCode" class="form-control form-control-sm inv-ltr" /></td>
                <td><input v-model="l.costCenterCode" class="form-control form-control-sm inv-ltr" /></td>
                <td><input v-model="l.contractId" class="form-control form-control-sm" /></td>
                <td><select v-model="l.period" class="form-select form-select-sm"><option v-for="(m, k) in monthNames" :key="k" :value="String(k + 1)">{{ m }}</option></select></td>
                <td><input v-model="l.amount" class="form-control form-control-sm fn-num" /></td>
                <td><input v-model="l.isCapital" type="checkbox" /></td>
                <td><input v-model="l.note" class="form-control form-control-sm" /></td>
                <td><button class="btn btn-sm btn-link text-danger p-0" @click="form.lines.splice(i, 1)">حذف</button></td>
              </tr>
            </tbody>
            <tfoot><tr class="fn-total"><td colspan="4">جمع</td><td class="fn-num">{{ money(formTotal) }}</td><td colspan="3"></td></tr></tfoot>
          </table>
        </div>
        <div class="d-flex justify-content-between mt-2">
          <button class="btn btn-sm btn-outline-secondary" @click="form.lines.push(blankLine())">+ ردیف</button>
          <div class="d-flex gap-2"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره پیش‌نویس</button></div>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
