<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import FnDate from "@/components/fn/FnDate.vue";
import { lastDays, todayIso } from "@/components/production/mnt/mntCommon";
import { getCostPostings, getCostRuns, runCost } from "@/api/ProductionAPIs";
import { money, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { dateText, hoursText, persianOf } from "@/composables/production";
import { hasRole, ProductionRoles } from "@/composables/roles";

// Daily production cost posting (MfCost). The server posts the closed bookings with the rates it holds and writes the accounting event; the page never computes a rate.
// Labour amounts are hidden by the server (labourAmount = null with labourHidden) where a row would reveal one person's pay.
const canRun = hasRole(ProductionRoles.costRunners);
const tab = ref<"runs" | "postings">("runs");
const range = lastDays(60);
const filters = ref<any>({ From: range.from, To: range.to, RunId: null as number | null, WorkOrderId: null as number | null });
const runs = usePagedList(getCostRuns, filters);
const postings = usePagedList(getCostPostings, filters);
const runDate = ref<string | null>(null);
const busy = ref(false);
const result = ref<any>(null);
const runLabel = ref("");

const hiddenText = "محرمانه (حقوق)";
const labourOf = (row: any) => (row.labourHidden || row.labourAmount === null || row.labourAmount === undefined ? hiddenText : money(row.labourAmount));
const resultRuns = computed(() => result.value?.runs || []);

async function run() {
  if (runDate.value && runDate.value >= todayIso()) return fail("روز امروز و روزهای آینده قابل ثبت هزینه نیست؛ یک روز گذشته را انتخاب کنید.");
  if (!confirm(runDate.value ? `هزینه روز ${dateText(runDate.value)} ثبت شود؟` : "هزینه همه رزروهای ثبت‌نشده روزهای گذشته ثبت شود؟")) return;
  busy.value = true;
  const [data, error] = await runCost(runDate.value);
  busy.value = false;
  if (error) return fail(error);
  result.value = data;
  ok(data?.message || "ثبت هزینه انجام شد.");
  reload();
}

function reload() {
  return tab.value === "runs" ? runs.search() : postings.search();
}
function setTab(t: typeof tab.value) {
  tab.value = t;
  reload();
}
function postingsOf(r: any) {
  filters.value.RunId = r.id;
  runLabel.value = r.number;
  setTab("postings");
}
function clearRun() {
  filters.value.RunId = null;
  runLabel.value = "";
  postings.search();
}

onMounted(runs.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">ثبت هزینه تولید</h1></div>

      <div v-if="canRun" class="d-flex gap-2 align-items-end flex-wrap mb-3">
        <div><label class="form-label small mb-0">روز مورد نظر (خالی = همه رزروهای ثبت‌نشده)</label><FnDate v-model="runDate" /></div>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="run">اجرای ثبت هزینه</button>
        <span class="inv-muted small">فقط روزهای گذشته؛ امروز و آینده را سرویس رد می‌کند. نرخ‌ها از مرکز کار و حقوق توسط سرور اعمال می‌شود.</span>
      </div>

      <div v-if="result" class="alert alert-light border mb-3">
        <div class="fw-bold mb-1">نتیجه آخرین اجرا</div>
        <div v-if="result.message" class="mb-1">{{ result.message }}</div>
        <div class="row g-2 small">
          <div class="col-6 col-md-3">ثبت‌شده: <b>{{ result.bookingsPosted }}</b></div>
          <div class="col-6 col-md-3">ردشده (نرخ ندارد): <b>{{ result.bookingsSkipped }}</b></div>
          <div class="col-6 col-md-3">رزرو بدون ساعت: <b>{{ result.zeroHourBookings }}</b></div>
          <div class="col-6 col-md-3">نرخ دستمزد از حقوق / از مرکز کار: <b>{{ result.labourRateFromPayroll }} / {{ result.labourRateFromWorkCenter }}</b></div>
          <div class="col-6 col-md-3">دستمزد (ریال): <b>{{ labourOf(result) }}</b></div>
          <div class="col-6 col-md-3">سربار (ریال): <b class="fn-num">{{ money(result.overheadAmount) }}</b></div>
        </div>
        <div v-if="result.errors?.length" class="mt-2">
          <div class="text-danger small">خطاها (نرخ یا داده ناقص):</div>
          <ul class="small mb-0"><li v-for="(e, i) in result.errors" :key="i">{{ e }}</li></ul>
        </div>
        <div v-if="resultRuns.length" class="mt-2 small">اجراهای ساخته‌شده: <span v-for="r in resultRuns" :key="r.id" class="inv-badge info me-1"><span class="inv-ltr">{{ r.number }}</span> - {{ persianOf(r.postingDateText, r.postingDate) }}</span></div>
      </div>

      <ul class="nav nav-tabs mb-3">
        <li class="nav-item"><button :class="{ active: tab === 'runs' }" class="nav-link" @click="setTab('runs')">اجراها</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'postings' }" class="nav-link" @click="setTab('postings')">ردیف‌های هزینه</button></li>
      </ul>
      <div class="inv-filters mb-3 align-items-end">
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
        <button class="btn btn-sm btn-secondary" @click="reload">جستجو</button>
        <span v-if="tab === 'postings' && filters.RunId" class="inv-badge info">اجرای <span class="inv-ltr">{{ runLabel }}</span> <a href="#" @click.prevent="clearRun">همه</a></span>
      </div>

      <template v-if="tab === 'runs'">
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>روز هزینه</th><th>زمان اجرا</th><th>تعداد رزرو</th><th>ساعت دستمزد</th><th>ساعت ماشین</th><th>دستمزد (ریال)</th><th>سربار (ریال)</th><th>رویداد حسابداری</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!runs.rows.value.length"><td colspan="10" class="text-center inv-muted">اجرایی نیست.</td></tr>
              <tr v-for="r in runs.rows.value" :key="r.id">
                <td class="inv-ltr">{{ r.number }}</td><td>{{ persianOf(r.postingDateText, r.postingDate) }}</td><td>{{ r.runAtText }}</td><td>{{ r.bookingCount }}</td>
                <td>{{ hoursText(r.labourHours) }}</td><td>{{ hoursText(r.machineHours) }}</td>
                <td class="fn-num">{{ labourOf(r) }}</td><td class="fn-num">{{ money(r.overheadAmount) }}</td>
                <td><router-link v-if="r.accountingEventId" to="/finance/events">رویداد {{ r.accountingEventId }}</router-link><span v-else class="inv-muted">-</span></td>
                <td><button class="btn btn-sm btn-outline-primary" @click="postingsOf(r)">ردیف‌ها</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="runs.page.value" :total="runs.total.value" @go="runs.go" />
      </template>

      <template v-else>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>اجرا</th><th>روز</th><th>دستور کار</th><th>گره WBS</th><th>مرکز کار</th><th>تعداد رزرو</th><th>ساعت دستمزد</th><th>ساعت ماشین</th><th>دستمزد (ریال)</th><th>سربار (ریال)</th></tr></thead>
            <tbody>
              <tr v-if="!postings.rows.value.length"><td colspan="10" class="text-center inv-muted">ردیفی نیست.</td></tr>
              <tr v-for="p in postings.rows.value" :key="p.id">
                <td class="inv-ltr">{{ p.runNumber }}</td><td>{{ persianOf(p.postingDateText, p.postingDate) }}</td>
                <td class="inv-ltr">{{ p.workOrderCode }}</td><td class="inv-ltr">{{ p.wbsCode || "-" }}</td><td class="inv-ltr">{{ p.workCenterCode || "-" }}</td>
                <td>{{ p.bookingCount }}</td><td>{{ hoursText(p.labourHours) }}</td><td>{{ hoursText(p.machineHours) }}</td>
                <td class="fn-num">{{ labourOf(p) }}</td><td class="fn-num">{{ money(p.overheadAmount) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="postings.page.value" :total="postings.total.value" @go="postings.go" />
        <p class="inv-muted small mb-0">برای ردیف‌هایی که کمتر از سه نفر در آن کار کرده‌اند، دستمزد برای نقش‌هایی که مجاز به دیدن حقوق نیستند «محرمانه (حقوق)» نمایش داده می‌شود.</p>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
