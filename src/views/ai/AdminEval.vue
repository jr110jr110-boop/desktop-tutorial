<script setup lang="ts">
// «ارزیابی»: runs the fixed set of Persian questions against the configured model and reports the pass rate, the numeric failures and the latency (p50 / p95). Starting an evaluation returns
// a running run: it is read again every 2 seconds until it has finished. Read: PMO and CEO. Start: PMO. The answers of the cases are written with the data of the CEO, so the server
// sends them to the CEO only (the PMO reads the counts).
import { onBeforeUnmount, onMounted, ref } from "vue";
import AiAdminTabs from "@/components/ai/AiAdminTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import { getAiEvalCases, getAiEvalRun, getAiEvalRuns, runAiEval } from "@/api/AiAPIs";
import { POLL_LOST_TEXT, startPolling } from "@/composables/ai";
import { usePagedList } from "@/composables/fn";
import { fail, jalali, ok } from "@/composables/inventory";
import { AiRoles, hasRole } from "@/composables/roles";

const canStart = hasRole(AiRoles.admins);
const canSeeAnswers = hasRole(AiRoles.evalAnswers);
const list = usePagedList(getAiEvalRuns, ref({}), 25);
const current = ref<any>(null);
const detail = ref<any>(null);
const detailBusy = ref(false);
const starting = ref(false);
const lost = ref(false);
const cases = ref<any[] | null>(null);
const openCase = ref<string | null>(null);
let stop: (() => void) | null = null;

async function start() {
  if (starting.value) return;
  starting.value = true;
  const [run, error] = await runAiEval();
  starting.value = false;
  if (error) return fail(error);
  current.value = run;
  ok("ارزیابی آغاز شد.");
  watch(run.id);
}
function watch(id: number) {
  stop?.();
  lost.value = false;
  stop = startPolling(
    async () => {
      const [run, error] = await getAiEvalRun(id);
      if (error) throw error; // startPolling tries again, and gives up after 3 failures in a row
      current.value = run;
      if (run.running) return false;
      list.load();
      return true;
    },
    2000,
    60 * 60 * 1000,
    () => {
      lost.value = true;
      if (current.value) current.value = { ...current.value, running: false };
    }
  );
}
async function show(id: number) {
  detailBusy.value = true;
  const [run, error] = await getAiEvalRun(id);
  detailBusy.value = false;
  if (error) return fail(error);
  openCase.value = null;
  detail.value = run;
}
async function loadCases(e: Event) {
  if (!(e.target as HTMLDetailsElement).open || cases.value) return;
  const [data, error] = await getAiEvalCases();
  if (error) return fail(error);
  cases.value = data || [];
}

// the acceptance of the design (docs/ai-backend-brief.md §8): at least 90 percent passed, no unverified number shown, p95 up to 8 seconds for text
const passClass = (p: number) => (p >= 90 ? "ok" : "bad");
const failClass = (n: number) => (n === 0 ? "ok" : "bad");
const p95Class = (ms: number) => (ms <= 8000 ? "ok" : "warn");
const secs = (ms: number) => (ms / 1000).toFixed(1);

onMounted(async () => {
  await list.load();
  // an evaluation that is still running (started before this page was opened) is followed too
  const r = list.rows.value.find((x) => x.running);
  if (r && !current.value) {
    current.value = r;
    watch(r.id);
  }
});
onBeforeUnmount(() => stop?.());
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مدیریت هوش مصنوعی</h1></div>
      <AiAdminTabs />
      <div class="inv-toolbar">
        <h2 class="inv-title">ارزیابی</h2>
        <button v-if="canStart" :disabled="starting || current?.running" class="btn btn-sm btn-primary" @click="start">
          <span v-if="starting || current?.running" class="spinner-border spinner-border-sm me-1"></span>شروع ارزیابی
        </button>
      </div>

      <div v-if="current" class="border rounded p-2 mb-3">
        <div v-if="lost" class="text-danger mb-1">{{ POLL_LOST_TEXT }}</div>
        <div v-if="current.running" class="mb-0"><span class="spinner-border spinner-border-sm"></span> ارزیابی در حال انجام است؛ هر ۲ ثانیه بازخوانی می‌شود... (شروع: {{ current.startedAtText }})</div>
        <div v-else-if="!lost" class="d-flex flex-wrap gap-3 align-items-center">
          <b>ارزیابی {{ current.id }} پایان یافت</b>
          <span>قبول: {{ current.passed }} از {{ current.total }}</span>
          <span :class="passClass(current.passPercent)" class="inv-badge">{{ current.passPercent }}٪</span>
          <button class="btn btn-sm btn-outline-primary" @click="show(current.id)">مشاهده جزئیات</button>
        </div>
      </div>

      <details class="mb-3" @toggle="loadCases">
        <summary>موارد ارزیابی</summary>
        <p v-if="!cases" class="inv-muted mb-0">در حال بارگذاری...</p>
        <ol v-else class="mb-0 mt-2">
          <li v-for="c in cases" :key="c.id"><span class="inv-ltr">{{ c.code }}</span> — {{ c.question }}<span v-if="!c.enabled" class="inv-badge warn ms-1">غیرفعال</span></li>
        </ol>
      </details>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>زمان</th><th>کل</th><th>قبول</th><th>درصد قبولی</th><th>شکست عددی</th><th>p50 (ثانیه)</th><th>p95 (ثانیه)</th><th>مدل</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="10" class="text-center inv-muted">{{ list.loading.value ? "در حال بارگذاری..." : "ارزیابی‌ای انجام نشده است." }}</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td>{{ r.id }}</td>
              <td>{{ r.startedAtText }}</td>
              <td>{{ r.total }}</td>
              <td>{{ r.passed }}</td>
              <td><span v-if="r.running" class="inv-badge info">در حال انجام</span><span v-else :class="passClass(r.passPercent)" class="inv-badge">{{ r.passPercent }}٪</span></td>
              <td><span :class="failClass(r.numericFailures)" class="inv-badge">{{ r.numericFailures }}</span></td>
              <td>{{ secs(r.p50Ms) }}</td>
              <td><span :class="p95Class(r.p95Ms)" class="inv-badge">{{ secs(r.p95Ms) }}</span></td>
              <td class="inv-ltr">{{ r.model || "-" }}</td>
              <td><button :disabled="detailBusy" class="btn btn-sm btn-outline-secondary" @click="show(r.id)">جزئیات</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="list.pageSize" :total="list.total.value" @go="list.go" />
      <p class="inv-muted mb-0">معیار پذیرش طراحی: دست‌کم ۹۰٪ قبولی، صفر شکست عددی و p95 تا ۸ ثانیه برای پرسش متنی.</p>
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <div class="d-flex justify-content-between align-items-center mb-2">
          <h2 class="inv-title">ارزیابی {{ detail.id }} — {{ detail.startedAtText }}</h2>
          <button class="btn btn-sm btn-secondary" @click="detail = null">بستن</button>
        </div>
        <div class="row g-2 mb-2">
          <div class="col-6 col-md-3">قبول: <b>{{ detail.passed }}</b> از {{ detail.total }}</div>
          <div class="col-6 col-md-3">درصد قبولی: <b>{{ detail.passPercent }}٪</b></div>
          <div class="col-6 col-md-3">شکست عددی: <b>{{ detail.numericFailures }}</b></div>
          <div class="col-6 col-md-3">p50 / p95: <b>{{ secs(detail.p50Ms) }} / {{ secs(detail.p95Ms) }}</b> ثانیه</div>
          <div class="col-12 inv-ltr">{{ detail.model }}</div>
        </div>
        <p v-if="!canSeeAnswers" class="inv-muted">پاسخ‌ها با داده‌های مدیرعامل نوشته شده‌اند و فقط برای مدیرعامل نمایش داده می‌شوند؛ شما شمار نتیجه هر مورد را می‌بینید.</p>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>پرسش</th><th>نتیجه</th><th>ابزارها</th><th>واقعیت‌ها</th><th>راستی‌آزمایی</th><th>زمان (ثانیه)</th><th></th></tr></thead>
            <tbody>
              <template v-for="c in detail.cases || []" :key="c.code">
                <tr>
                  <td class="inv-ltr">{{ c.code }}</td>
                  <td>{{ c.question }}</td>
                  <td><span :class="c.passed ? 'ok' : 'bad'" class="inv-badge">{{ c.passed ? "قبول" : "رد" }}</span></td>
                  <td><span :class="c.toolsOk ? 'ok' : 'bad'" class="inv-badge">{{ c.toolsOk ? "درست" : "نادرست" }}</span></td>
                  <td><span :class="c.factsOk ? 'ok' : 'bad'" class="inv-badge">{{ c.factsOk ? "درست" : "نادرست" }}</span></td>
                  <td><span :class="c.verified ? 'ok' : 'warn'" class="inv-badge">{{ c.verified ? "بله" : "خیر" }}</span><span v-if="c.degraded" class="inv-badge warn ms-1">کاهش‌یافته</span></td>
                  <td>{{ secs(c.ms) }}</td>
                  <td><button class="btn btn-sm btn-outline-secondary" @click="openCase = openCase === c.code ? null : c.code">جزئیات</button></td>
                </tr>
                <tr v-if="openCase === c.code">
                  <td colspan="8" class="bg-light">
                    <div class="small mb-1"><b>ابزارهای مورد انتظار:</b> <span class="inv-ltr">{{ c.expectedTools || "-" }}</span></div>
                    <div class="small mb-1"><b>ابزارهای فراخوانی‌شده:</b> <span class="inv-ltr">{{ (c.calledTools || []).join("، ") || "-" }}</span></div>
                    <ul class="small mb-1">
                      <li v-for="(f, i) in c.facts || []" :key="i">
                        <span :class="f.ok ? 'ok' : 'bad'" class="inv-badge me-1">{{ f.ok ? "درست" : "نادرست" }}</span>{{ f.fact }}<span v-if="f.reason" class="inv-muted"> — {{ f.reason }}</span>
                      </li>
                    </ul>
                    <div v-if="c.answer" class="case-answer"><b>پاسخ مدل:</b> {{ c.answer }}</div>
                  </td>
                </tr>
              </template>
            </tbody>
          </table>
        </div>
        <p class="inv-muted mb-0">ثبت‌شده در {{ jalali(detail.startedAt, true) }}</p>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style scoped>
.case-answer {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.8;
}
</style>
