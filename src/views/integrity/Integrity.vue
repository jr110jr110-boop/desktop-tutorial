<script setup lang="ts">
// «سلامت سوابق» (Package D): the two lights (the hash chain of the audit trail and the monthly restore test), the history of the chain checks, the button «بررسی همین حالا»
// and the results of the restore tests. The nightly check (02:30) and the monthly restore test run by themselves; here a person reads the result or asks for a check.
// Read: PMO, CEO, QA, AUDITOR, FINANCEMANAGER. The API checks every call again.
import { onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { getChainRuns, getIntegritySummary, getRestoreTests, importRestoreTests, runChainCheck, type ChainRun, type IntegritySummary } from "@/api/IntegrityAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";

const summary = ref<IntegritySummary | null>(null);
const busy = ref(false);
const open = ref<number | null>(null);
const none = ref<any>({});
const runs = usePagedList(getChainRuns, none, 25);
const tests = usePagedList(getRestoreTests, none, 12);

const chainBadge = (s: string | undefined) => (s === "Ok" ? "ok" : s === "Broken" ? "bad" : "warn");
const restoreBadge = (s: string | undefined) => (s === "Ok" ? "ok" : s === "Never" || s === "Stale" || s === "Failed" ? "bad" : "warn");

async function load() {
  const [data, error] = await getIntegritySummary();
  if (error) return fail(error);
  summary.value = data;
  runs.load();
  tests.load();
}

async function check(mode: "incremental" | "full") {
  busy.value = true;
  const [run, error] = await runChainCheck(mode);
  busy.value = false;
  if (error) return fail(error);
  if ((run as ChainRun).status === "Ok") ok("زنجیره سوابق سالم است.");
  else fail(new Error((run as ChainRun).message || "خطا در زنجیره سوابق؛ هشدار برای مسئول کیفیت، PMO و مدیرعامل ارسال شد."));
  load();
}

async function importResults() {
  busy.value = true;
  const [r, error] = await importRestoreTests();
  busy.value = false;
  if (error) return fail(error);
  ok(r?.message || `${r?.imported ?? 0} نتیجه جدید خوانده شد.`);
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page" dir="rtl">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">سلامت سوابق</h1>
        <RouterLink :to="{ name: 'SafetyProfile' }" class="btn btn-sm btn-outline-secondary">پروفایل سخت‌گیرانه تولید</RouterLink>
      </div>
      <div v-if="summary" class="row g-3">
        <div class="col-md-6">
          <div class="border rounded p-3 h-100">
            <div class="d-flex justify-content-between align-items-center">
              <b>زنجیره سوابق (امضای HMAC)</b>
              <span :class="chainBadge(summary.chainStatus)" class="inv-badge">{{ summary.chainStatusText }}</span>
            </div>
            <div v-if="summary.lastChainRun" class="inv-muted small mt-2">
              آخرین بررسی: {{ summary.lastChainRun.startedAtText }} ({{ summary.lastChainRun.kindText }}) — {{ summary.lastChainRun.entriesChecked }} ردیف بررسی شد
            </div>
            <div v-if="summary.lastFullRun" class="inv-muted small">آخرین بررسی کامل: {{ summary.lastFullRun.startedAtText }}</div>
            <div v-if="summary.lastChainRun && summary.lastChainRun.status !== 'Ok'" class="text-danger small mt-2">{{ summary.lastChainRun.message }}</div>
            <div class="mt-3 d-flex gap-2">
              <button :disabled="busy" class="btn btn-sm btn-primary" @click="check('incremental')">بررسی همین حالا</button>
              <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="check('full')">بررسی کامل زنجیره</button>
            </div>
          </div>
        </div>
        <div class="col-md-6">
          <div class="border rounded p-3 h-100">
            <div class="d-flex justify-content-between align-items-center">
              <b>آزمون بازیابی پشتیبان</b>
              <span :class="restoreBadge(summary.restoreStatus)" class="inv-badge">{{ summary.restoreStatusText }}</span>
            </div>
            <div class="inv-muted small mt-2">
              <template v-if="summary.lastRestoreSuccessAtText">آخرین آزمون موفق: {{ summary.lastRestoreSuccessAtText }} ({{ summary.daysSinceRestoreSuccess }} روز پیش)</template>
              <template v-else>هنوز آزمون موفقی ثبت نشده است.</template>
            </div>
            <div v-if="summary.red" class="text-danger small mt-1">بیش از {{ summary.restoreWarnDays }} روز از آخرین آزمون موفق گذشته یا آزمونی انجام نشده است.</div>
            <div class="mt-3"><button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="importResults">خواندن نتیجه آزمون</button></div>
          </div>
        </div>
      </div>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar"><h2 class="inv-title">تاریخچه بررسی زنجیره</h2></div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>زمان</th><th>نوع</th><th>درخواست از</th><th>وضعیت</th><th>ردیف‌های بررسی‌شده</th><th>آخرین ردیف</th></tr></thead>
          <tbody>
            <tr v-if="!runs.rows.value.length"><td colspan="7" class="text-center inv-muted">{{ runs.loading.value ? "در حال بارگذاری..." : "بررسی‌ای ثبت نشده است." }}</td></tr>
            <template v-for="r in runs.rows.value" :key="r.id">
              <tr role="button" @click="open = open === r.id ? null : r.id">
                <td>{{ r.id }}</td>
                <td>{{ r.startedAtText }}</td>
                <td>{{ r.kindText }}</td>
                <td>{{ r.manual ? r.requestedByName || "دستی" : "خودکار (۰۲:۳۰)" }}</td>
                <td><span :class="chainBadge(r.status)" class="inv-badge">{{ r.statusText }}</span></td>
                <td>{{ r.entriesChecked }}</td>
                <td>{{ r.headSeq ?? "-" }}</td>
              </tr>
              <tr v-if="open === r.id">
                <td colspan="7" class="bg-light">
                  <div>{{ r.message }}</div>
                  <div v-if="r.status === 'Broken'" class="text-danger mt-1">
                    اولین ردیف خراب: شماره {{ r.breakSeq }}، جدول «{{ r.breakEntityTypeText || r.breakEntityType }}»، شناسه ردیف {{ r.breakRowId ?? "-" }}، زمان {{ r.breakAtText || "-" }}
                  </div>
                  <div v-if="!r.keyConfigured" class="text-warning mt-1">کلید امضای سوابق تنظیم نشده بود.</div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <QPager :page="runs.page.value" :total="runs.total.value" :page-size="runs.pageSize" @go="runs.go" />
    </div>

    <div class="inv-card">
      <div class="inv-toolbar"><h2 class="inv-title">آزمون‌های بازیابی</h2></div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>زمان</th><th>فایل پشتیبان</th><th>تاریخ پشتیبان</th><th>تاریخچه تغییرات پایگاه داده</th><th>زنجیره سوابق</th><th>نتیجه</th></tr></thead>
          <tbody>
            <tr v-if="!tests.rows.value.length"><td colspan="6" class="text-center inv-muted">{{ tests.loading.value ? "در حال بارگذاری..." : "آزمونی ثبت نشده است؛ اولین آزمون روز اول ماه ساعت ۰۳:۰۰ اجرا می‌شود." }}</td></tr>
            <template v-for="t in tests.rows.value" :key="t.id">
              <tr role="button" @click="open = open === -t.id ? null : -t.id">
                <td>{{ t.startedAtText }}</td>
                <td class="inv-ltr">{{ t.backupFile }}</td>
                <td>{{ t.backupTakenAtText }}</td>
                <td>{{ t.migrationsMatch ? "مطابق" : "ناهمخوان" }}</td>
                <td>{{ t.chainOk ? "سالم (" + t.chainEntriesChecked + " ردیف)" : "ناسالم" }}</td>
                <td><span :class="t.success ? 'ok' : 'bad'" class="inv-badge">{{ t.success ? "موفق" : "ناموفق" }}</span></td>
              </tr>
              <tr v-if="open === -t.id">
                <td colspan="6" class="bg-light">
                  <div>{{ t.message }}</div>
                  <ul class="mb-0 small">
                    <li v-for="c in t.checks" :key="c.name"><span :class="c.passed ? 'text-success' : 'text-danger'">{{ c.passed ? "قبول" : "رد" }}</span> — {{ c.name }}: {{ c.detail }}</li>
                  </ul>
                  <div class="small inv-muted mt-1">پایگاه آزمایشی پس از آزمون {{ t.scratchDropped ? "حذف شد" : "حذف نشد" }}.</div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <QPager :page="tests.page.value" :total="tests.total.value" :page-size="tests.pageSize" @go="tests.go" />
    </div>
  </div>
</template>
