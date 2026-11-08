<script setup lang="ts">
// «سلامت سوابق» (Package D): the two lights (the hash chain of the audit trail and the monthly restore test), the history of the chain checks, the button «بررسی همین حالا»
// and the results of the restore tests. The nightly check (02:30) and the monthly restore test run by themselves; here a person reads the result or asks for a check.
// Read: PMO, CEO, QA, AUDITOR, FINANCEMANAGER. The API checks every call again.
import { onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { cancelAcceptance, getAcceptances, getChainRun, requestAcceptance, signAcceptance, getChainRuns, getIntegritySummary, getRestoreTests, importRestoreTests, runChainCheck, type BreakAcceptance, type ChainRun, type IntegritySummary } from "@/api/IntegrityAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole, IntegrityRoles } from "@/composables/roles";

const summary = ref<IntegritySummary | null>(null);
const busy = ref(false);
const open = ref<number | null>(null);
const none = ref<any>({});
const runs = usePagedList(getChainRuns, none, 25);
const tests = usePagedList(getRestoreTests, none, 12);
const accepts = usePagedList(getAcceptances, none, 10);
const canAccept = hasRole(IntegrityRoles.approvers.concat(["QA"]));
const reason = ref("");
const seats: { role: "QA" | "PMO" | "CEO"; title: string }[] = [{ role: "QA", title: "مسئول کیفیت" }, { role: "PMO", title: "PMO" }, { role: "CEO", title: "مدیرعامل" }];
const signer = (a: BreakAcceptance, r: string) => (r === "QA" ? a.qaName : r === "PMO" ? a.pmoName : a.ceoName);
const signedAt = (a: BreakAcceptance, r: string) => (r === "QA" ? a.qaAtText : r === "PMO" ? a.pmoAtText : a.ceoAtText);

const chainBadge = (s: string | undefined) => (s === "Ok" ? "ok" : s === "Broken" ? "bad" : "warn");
const breakWhere = (r: ChainRun) => (r.breakKind === "record-drift" ? `جدول «${r.breakEntityTypeText || r.breakEntityType}»، شناسه ردیف ${r.breakRowId ?? "-"}، ستون ${r.breakColumn ?? "-"} (رکورد با ردپای ثبت‌شده نمی‌خواند)` : `شماره ${r.breakSeq}، جدول «${r.breakEntityTypeText || r.breakEntityType}»، شناسه ردیف ${r.breakRowId ?? "-"}`);
const restoreBadge = (s: string | undefined) => (s === "Ok" ? "ok" : s === "Never" || s === "Stale" || s === "Failed" ? "bad" : "warn");

async function load() {
  const [data, error] = await getIntegritySummary();
  if (error) return fail(error);
  summary.value = data;
  runs.load();
  tests.load();
  accepts.load();
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

async function check(mode: "incremental" | "full") {
  busy.value = true;
  let [run, error] = await runChainCheck(mode);
  // a full pass is queued (status Running): poll the run until it ends (at most about 30 minutes)
  for (let i = 0; !error && run && (run as ChainRun).status === "Running" && i < 900; i++) {
    await sleep(2000);
    [run, error] = await getChainRun((run as ChainRun).id);
  }
  busy.value = false;
  if (error) return fail(error);
  const r = run as ChainRun;
  if (r.status === "Ok") ok("زنجیره سوابق سالم است.");
  else if (r.status === "Running") ok("بررسی کامل هنوز در حال اجراست؛ نتیجه در تاریخچه می‌آید.");
  else fail(new Error(r.message || "خطا در زنجیره سوابق؛ هشدار برای مسئول کیفیت، PMO و مدیرعامل ارسال شد."));
  load();
}

async function askAccept() {
  const id = summary.value?.lastChainRun?.id;
  if (!id) return;
  busy.value = true;
  const [, error] = await requestAcceptance(id, reason.value);
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست پذیرش ثبت شد؛ سه امضای جدا (کیفیت، PMO، مدیرعامل) لازم است.");
  reason.value = "";
  load();
}

async function sign(a: BreakAcceptance, role: "QA" | "PMO" | "CEO") {
  busy.value = true;
  const [, error] = await signAcceptance(a.id, role);
  busy.value = false;
  if (error) return fail(error);
  ok("امضا ثبت شد.");
  load();
}

async function cancelAccept(a: BreakAcceptance) {
  busy.value = true;
  const [, error] = await cancelAcceptance(a.id);
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست لغو شد.");
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
            <div v-if="summary.openBreak" class="text-danger small mt-2">شکست زنجیره هنوز باز است و تا یک بررسی کامل سالم یا پذیرش سه‌امضایی باقی می‌ماند.</div>
            <div v-if="summary.lastChainRun && summary.lastChainRun.status !== 'Ok'" class="text-danger small mt-1">{{ summary.lastChainRun.message }}</div>
            <div v-if="summary.openBreak && canAccept && !summary.pendingAcceptanceId" class="mt-2 d-flex gap-2">
              <input v-model="reason" class="form-control form-control-sm" maxlength="1000" placeholder="دلیل پذیرش شکست شناخته‌شده (الزامی)" />
              <button :disabled="busy" class="btn btn-sm btn-outline-danger text-nowrap" @click="askAccept">درخواست پذیرش</button>
            </div>
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
            <div v-if="summary.red" class="text-danger small mt-1">بیش از {{ summary.restoreWarnDays }} روز از آخرین آزمون موفق گذشته یا آخرین آزمون ناموفق بوده یا آزمونی انجام نشده است.</div>
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
                    اولین مورد خراب ({{ r.breakKind }}): {{ breakWhere(r) }}، زمان {{ r.breakAtText || "-" }}
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
      <div class="inv-toolbar"><h2 class="inv-title">پذیرش شکست شناخته‌شده</h2></div>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>زمان</th><th>شکست</th><th>دلیل</th><th>درخواست‌دهنده</th><th v-for="x in seats" :key="x.role">{{ x.title }}</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!accepts.rows.value.length"><td colspan="8" class="text-center inv-muted">درخواستی ثبت نشده است.</td></tr>
            <tr v-for="a in accepts.rows.value" :key="a.id">
              <td>{{ a.requestedAtText }}</td>
              <td>{{ a.breakKind }}<span v-if="a.breakSeq"> ({{ a.breakSeq }})</span><span v-if="a.breakEntityType"> {{ a.breakEntityTypeText || a.breakEntityType }}</span></td>
              <td>{{ a.reason }}</td>
              <td>{{ a.requestedByName }}</td>
              <td v-for="x in seats" :key="x.role">
                <template v-if="signer(a, x.role)">{{ signer(a, x.role) }}<div class="inv-muted small">{{ signedAt(a, x.role) }}</div></template>
                <button v-else-if="a.status === 'Pending' && canAccept" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="sign(a, x.role)">امضا</button>
                <span v-else>-</span>
              </td>
              <td>{{ a.statusText }}</td>
              <td><button v-if="a.status === 'Pending'" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="cancelAccept(a)">لغو</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="inv-muted small">سه نفر جدا (کیفیت، PMO، مدیرعامل؛ نه هوش مصنوعی) باید امضا کنند؛ امضای سوم، شکست را می‌پذیرد و بررسی از همان نقطه ادامه می‌یابد.</div>
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
