<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import ExcelRows, { type ImportField } from "@/components/fn/ExcelRows.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import ParallelRunPrint from "@/components/fn/print/ParallelRunPrint.vue";
import { compareParallel, explainParallelDifference, getParallelRun, getParallelRuns, getParallelStatus, signOffParallelRun } from "@/api/FnAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { money, printSheet, yesNo } from "@/composables/fn";
import { diffKinds, usePeriods } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Parallel run with Sepidar: the trial balance of Sepidar for a period is compared with PMIS. Every difference gets an explanation; the finance manager signs off a clean period.
// Go-live is ready after the required number of consecutive clean, signed-off periods.
const { periods, load: loadPeriods, label } = usePeriods();
const status = ref<any>(null);
const runs = ref<any[]>([]);
const run = ref<any>(null);
const periodId = ref<number | null>(null);
const includeDraft = ref(false);
const rows = ref<any[]>([]);
const dialog = ref<null | { kind: "explain" | "sign"; diff?: any }>(null);
const printing = ref(false);
const busy = ref(false);
const canCompare = hasRole(FnRoles.accountants);
const canSign = hasRole(FnRoles.managers);

const fields: ImportField[] = [
  { key: "accountCode", label: "کد حساب *", words: ["کد حساب", "حساب", "account", "accountcode"], required: true },
  { key: "detailCode", label: "کد تفصیلی", words: ["کد تفصیلی", "تفصیلی", "detail", "detailcode"] },
  { key: "debit", label: "بدهکار", words: ["بدهکار", "debit"], type: "number" },
  { key: "credit", label: "بستانکار", words: ["بستانکار", "credit"], type: "number" },
];

async function loadAll() {
  const [s] = await getParallelStatus();
  status.value = s;
  const [r] = await getParallelRuns(null);
  runs.value = r || [];
}

async function compare() {
  if (!periodId.value) return fail("دوره را انتخاب کنید.");
  if (!rows.value.length) return fail("فایل تراز سپیدار را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await compareParallel({ periodId: periodId.value, includeDraft: includeDraft.value, rows: rows.value.map((r) => ({ accountCode: r.accountCode, detailCode: r.detailCode, debit: r.debit || 0, credit: r.credit || 0 })) });
  busy.value = false;
  if (error) return fail(error);
  ok("مقایسه انجام شد.");
  run.value = d;
  loadAll();
}

async function open(id: number) {
  const [d, error] = await getParallelRun(id);
  if (error) return fail(error);
  run.value = d;
}

async function onConfirm(v: any) {
  const d = dialog.value!;
  dialog.value = null;
  busy.value = true;
  const [data, error] = d.kind === "explain" ? await explainParallelDifference(run.value.id, d.diff.id, v.text) : await signOffParallelRun(run.value.id, v.text);
  busy.value = false;
  if (error) return fail(error);
  ok(d.kind === "explain" ? "توضیح ثبت شد." : "مقایسه تایید شد.");
  run.value = data;
  loadAll();
}

async function print() {
  printing.value = true;
  await nextTick();
  printSheet();
}

onMounted(() => {
  loadPeriods();
  loadAll();
});
</script>

<template>
  <div class="inv-page">
    <ParallelRunPrint v-if="printing && run" :run="run" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">دوره موازی با سپیدار: مقایسه ترازها</h1></div>

      <div v-if="status" class="mb-3">
        <p>
          <span :class="status.goLiveReady ? 'ok' : 'warn'" class="inv-badge">{{ status.goLiveReady ? "آماده خروج از موازی" : "هنوز آماده نیست" }}</span>
          دوره‌های متوالی پاک و تاییدشده: <b>{{ status.consecutiveCleanPeriods }}</b> از <b>{{ status.requiredMonths }}</b>
        </p>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>دوره</th><th>مقایسه</th><th>توضیح‌داده‌نشده</th><th>تاییدشده</th><th>وضعیت</th></tr></thead>
            <tbody>
              <tr v-if="!status.periods.length"><td colspan="5" class="text-center inv-muted">هنوز مقایسه‌ای نشده است.</td></tr>
              <tr v-for="p in status.periods" :key="p.periodId">
                <td>{{ p.periodTitle }}</td><td><a v-if="p.runId" href="#" @click.prevent="open(p.runId)">نمایش</a></td><td>{{ p.unexplained }}</td><td>{{ yesNo(p.signedOff) }}</td>
                <td><span :class="p.clean ? 'ok' : 'warn'" class="inv-badge">{{ p.clean ? "پاک" : "دارای اختلاف" }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div v-if="canCompare" class="border rounded p-2 mb-3">
        <div class="fn-section mt-0">مقایسه جدید</div>
        <div class="inv-filters mb-2">
          <select v-model="periodId" class="form-select form-select-sm"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
          <label class="form-check small"><input v-model="includeDraft" class="form-check-input" type="checkbox" /> با اسناد غیرقطعی</label>
        </div>
        <ExcelRows :fields="fields" hint="تراز آزمایشی همان دوره از سپیدار (جمع گردش بدهکار و بستانکار هر حساب / تفصیلی)." @rows="(r: any[]) => (rows = r)" />
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="compare">مقایسه با PMIS</button>
      </div>

      <div v-if="runs.length" class="mb-3">
        <div class="fn-section">مقایسه‌های انجام‌شده</div>
        <table class="table table-sm table-hover">
          <thead><tr><th>دوره</th><th>بارگذاری</th><th>ردیف</th><th>مطابق</th><th>اختلاف</th><th>توضیح‌داده‌نشده</th><th>تایید</th></tr></thead>
          <tbody>
            <tr v-for="r in runs" :key="r.id" style="cursor: pointer" @click="open(r.id)">
              <td>{{ r.periodTitle }}</td><td>{{ jalali(r.uploadedAt, true) }}</td><td>{{ r.rows }}</td><td>{{ r.matched }}</td><td>{{ r.pmisOnly + r.sepidarOnly + r.differs }}</td><td>{{ r.unexplained }}</td><td>{{ yesNo(r.signedOff) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="run" class="border rounded p-2">
        <div class="d-flex justify-content-between align-items-center flex-wrap gap-2">
          <h2 class="inv-title mb-0">نتیجه مقایسه: {{ run.periodTitle }}</h2>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-secondary" @click="print">چاپ گزارش مقایسه</button>
            <button v-if="canSign && !run.signedOff" :disabled="busy" class="btn btn-sm btn-success" @click="dialog = { kind: 'sign' }">تایید و امضای مقایسه</button>
          </div>
        </div>
        <p class="mt-2">
          ردیف‌ها: {{ run.rows }} - مطابق: <b>{{ run.matched }}</b> - فقط PMIS: {{ run.pmisOnly }} - فقط سپیدار: {{ run.sepidarOnly }} - مبلغ متفاوت: {{ run.differs }} - توضیح‌داده‌نشده: <b>{{ run.unexplained }}</b>
          <span v-if="run.signedOff" class="inv-badge ok me-2">تاییدشده: {{ jalali(run.signedOffAt, true) }} - {{ run.signOffNote }}</span>
        </p>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>کد حساب</th><th>عنوان</th><th>تفصیلی</th><th>نوع</th><th>بدهکار PMIS</th><th>بستانکار PMIS</th><th>بدهکار سپیدار</th><th>بستانکار سپیدار</th><th>توضیح</th><th v-if="canCompare && !run.signedOff"></th></tr></thead>
            <tbody>
              <tr v-if="!run.differences.length"><td colspan="10" class="text-center inv-muted">اختلافی نیست.</td></tr>
              <tr v-for="d in run.differences" :key="d.id" :class="{ 'table-warning': !d.explanation }">
                <td class="inv-ltr">{{ d.accountCode }}</td><td>{{ d.accountTitle }}</td><td class="inv-ltr">{{ d.detailCode }}</td><td>{{ diffKinds[d.kind] }}</td>
                <td class="fn-num">{{ money(d.pmisDebit) }}</td><td class="fn-num">{{ money(d.pmisCredit) }}</td><td class="fn-num">{{ money(d.sepidarDebit) }}</td><td class="fn-num">{{ money(d.sepidarCredit) }}</td><td>{{ d.explanation }}</td>
                <td v-if="canCompare && !run.signedOff"><button class="btn btn-sm btn-outline-secondary" @click="dialog = { kind: 'explain', diff: d }">{{ d.explanation ? "ویرایش توضیح" : "ثبت توضیح" }}</button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <PromptModal
      v-if="dialog"
      :fields="[{ key: 'text', label: dialog.kind === 'explain' ? 'توضیح علت اختلاف' : 'یادداشت تایید', type: 'textarea', required: true, value: dialog.diff?.explanation || '' }]"
      :confirm-text="dialog.kind === 'explain' ? 'ثبت توضیح' : 'تایید مقایسه'"
      :title="dialog.kind === 'explain' ? 'توضیح اختلاف ' + (dialog.diff?.accountCode || '') : 'تایید مدیر مالی'"
      @cancel="dialog = null"
      @confirm="onConfirm" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
