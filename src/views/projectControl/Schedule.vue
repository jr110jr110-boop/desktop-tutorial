<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import JDate from "@/components/pc/JDate.vue";
import WorkOrderOfContract from "@/components/pc/WorkOrderOfContract.vue";
import { applyNeedBy, downloadMsProject, generateItpActivities, getExecutivePlan, getNeedBy, getNotices, runCpm } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, printPage, today } from "@/composables/inventory";
import { activityStatuses, addDays, pcContractId, pct, phases, unitOptions, unitText } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Schedule control: critical path method (plan run and forecast run), executive plan for a window of days, need-by dates of purchases,
// ITP activities and inspection notice drafts, and the MS Project XML export.
const tab = ref("cpm");
const tabs = [
  { key: "cpm", title: "محاسبه مسیر بحرانی (CPM)" },
  { key: "exec", title: "برنامه اجرایی (بازه هفتگی)" },
  { key: "needby", title: "تاریخ نیاز خرید" },
  { key: "notices", title: "اعلام نقاط بازرسی" },
];
const canRun = hasRole(ProjectControlRoles.planningOrPm);
const canPlanning = hasRole(ProjectControlRoles.planning);
const busy = ref(false);

// ---- CPM
const forecast = ref(false);
const mirror = ref(true);
const dataDate = ref<string | null>(today());
const run = ref<any>(null);
async function doRun() {
  busy.value = true;
  const [data, error] = await runCpm(pcContractId.value!, forecast.value, mirror.value, forecast.value ? dataDate.value : null);
  busy.value = false;
  if (error) return fail(error);
  run.value = data;
  if (data.hasCycle) fail(new Error(data.message + " (" + data.cycleCodes.join("، ") + ")"));
  else ok(forecast.value ? "پیش‌بینی با پیشرفت واقعی محاسبه شد." : "برنامه محاسبه شد.");
}
async function exportXml() {
  busy.value = true;
  const err = await downloadMsProject(pcContractId.value!, "project-" + pcContractId.value + ".xml");
  busy.value = false;
  if (err) fail(new Error(err));
}
const criticalItems = computed(() => (run.value?.items || []).filter((i: any) => i.critical));

// ---- executive plan
const ex = ref<any>({ from: today(), days: 14, unit: "" });
const exRows = ref<any[]>([]);
async function loadExec() {
  const [data, error] = await getExecutivePlan({ ContractId: pcContractId.value, From: ex.value.from, Days: ex.value.days, ResponsibleUnit: ex.value.unit });
  if (error) return fail(error);
  exRows.value = rowsOf(data);
}
function shiftWeek(delta: number) {
  ex.value.from = addDays(ex.value.from, delta * 7);
  loadExec();
}

// ---- need-by
const needRows = ref<any[]>([]);
async function loadNeed() {
  const [data, error] = await getNeedBy({ ContractId: pcContractId.value });
  if (error) return fail(error);
  needRows.value = rowsOf(data);
}
async function applyNeed() {
  if (!confirm("تاریخ نیاز به ردیف‌های درخواست خرید اعمال شود؟")) return;
  busy.value = true;
  const [data, error] = await applyNeedBy(pcContractId.value!);
  busy.value = false;
  if (error) return fail(error);
  ok(`${Array.isArray(data) ? data.length : 0} ردیف اعمال شد.`);
  loadNeed();
}

// ---- ITP activities and notices
const itpOrder = ref<number | null>(null);
const notices = ref<any[]>([]);
const lookAhead = ref<number | null>(null);
async function genItp() {
  busy.value = true;
  const [data, error] = await generateItpActivities(pcContractId.value!, itpOrder.value!);
  busy.value = false;
  if (error) return fail(error);
  ok(`${data.activitiesCreated} فعالیت ITP ساخته شد.`);
  loadNotices();
}
async function loadNotices() {
  const [data, error] = await getNotices({ ContractId: pcContractId.value, LookAheadDays: lookAhead.value });
  if (error) return fail(error);
  notices.value = rowsOf(data);
}

function loadTab() {
  if (!pcContractId.value) return;
  if (tab.value === "exec") loadExec();
  else if (tab.value === "needby") loadNeed();
  else if (tab.value === "notices") loadNotices();
}
onMounted(loadTab);
watch([pcContractId, tab], () => { run.value = null; loadTab(); });
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="کنترل زمان‌بندی پروژه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">کنترل زمان‌بندی</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <ContractSelect />
          <button v-if="pcContractId" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="exportXml">خروجی MS Project (XML)</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <PcTabs v-model="tab" :tabs="tabs" />

        <div v-if="tab === 'cpm'">
          <div v-if="canRun" class="d-flex flex-wrap gap-3 align-items-end mb-3 d-print-none">
            <label class="form-check"><input v-model="forecast" class="form-check-input" type="checkbox" /> پیش‌بینی با پیشرفت واقعی (به‌جای برنامه اولیه)</label>
            <label v-if="!forecast" class="form-check"><input v-model="mirror" class="form-check-input" type="checkbox" /> تاریخ‌ها به فرآیندها، مجموعه‌ها و دستورهای ساخت قدیمی هم نوشته شود</label>
            <div v-if="forecast" style="width: 170px"><label class="form-label small mb-0">تاریخ داده</label><JDate v-model="dataDate" /></div>
            <button :disabled="busy" class="btn btn-sm btn-primary" @click="doRun">اجرای محاسبه</button>
          </div>
          <p v-else class="inv-muted">اجرای محاسبه با برنامه‌ریزی و مدیر پروژه است.</p>
          <div v-if="run">
            <div v-if="run.hasCycle" class="alert alert-danger py-2">{{ run.message }} فعالیت‌های حلقه: <span class="inv-ltr">{{ run.cycleCodes.join(" ← ") }}</span></div>
            <template v-else>
              <div class="row g-2 mb-3">
                <div class="col-md-3"><div class="border rounded p-2"><div class="inv-muted">شروع پروژه</div><b>{{ jalali(run.projectStart) }}</b></div></div>
                <div class="col-md-3"><div class="border rounded p-2"><div class="inv-muted">پایان پروژه</div><b>{{ jalali(run.projectFinish) }}</b></div></div>
                <div class="col-md-3"><div class="border rounded p-2"><div class="inv-muted">مدت (روز کاری)</div><b>{{ run.projectFinishOffset }}</b></div></div>
                <div class="col-md-3"><div class="border rounded p-2"><div class="inv-muted">فعالیت‌های بحرانی</div><b>{{ criticalItems.length }}</b> از {{ run.items.length }}<span v-if="run.mirrored" class="inv-muted"> ({{ run.mirrored }} مورد قدیمی به‌روز شد)</span></div></div>
              </div>
              <p v-if="run.message" class="inv-muted">{{ run.message }}</p>
              <h2 class="inv-title mb-2">مسیر بحرانی</h2>
              <p class="inv-ltr text-start">{{ run.criticalPath.join(" ← ") || "—" }}</p>
              <div class="table-responsive">
                <table class="table table-sm">
                  <thead><tr><th>کد</th><th>عنوان</th><th>شروع زودترین</th><th>پایان زودترین</th><th>شروع دیرترین</th><th>پایان دیرترین</th><th>شناوری کل</th><th>شناوری آزاد</th></tr></thead>
                  <tbody>
                    <tr v-for="i in run.items" :key="i.activityId" :class="{ 'table-danger': i.critical }">
                      <td class="inv-ltr">{{ i.code }}</td><td>{{ i.title }}</td><td>{{ jalali(i.earlyStart) }}</td><td>{{ jalali(i.earlyFinish) }}</td><td>{{ jalali(i.lateStart) }}</td><td>{{ jalali(i.lateFinish) }}</td><td>{{ i.totalFloat }}</td><td>{{ i.freeFloat }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </template>
          </div>
          <p class="inv-muted mt-2 mb-0">«برنامه اولیه» پیشرفت را نادیده می‌گیرد و تاریخ‌های برنامه و شناوری را می‌نویسد؛ «پیش‌بینی» با تاریخ‌های واقعی، مدت باقی‌مانده و تاریخ تحویل سفارش‌های خرید، تاریخ پایان پیش‌بینی را می‌سازد. نمودار گانت پس از این محاسبه دیده می‌شود.</p>
        </div>

        <div v-else-if="tab === 'exec'">
          <div class="d-flex flex-wrap gap-2 align-items-end mb-3 d-print-none">
            <div style="width: 170px"><label class="form-label small mb-0">از تاریخ</label><JDate v-model="ex.from" /></div>
            <div style="width: 110px"><label class="form-label small mb-0">تعداد روز</label><input v-model.number="ex.days" class="form-control form-control-sm" type="number" min="1" max="120" /></div>
            <div style="width: 170px"><label class="form-label small mb-0">واحد</label><select v-model="ex.unit" class="form-select form-select-sm"><option value="">همه</option><option v-for="u in unitOptions" :key="u[0]" :value="u[0]">{{ u[1] }}</option></select></div>
            <button class="btn btn-sm btn-secondary" @click="loadExec">نمایش</button>
            <button class="btn btn-sm btn-outline-secondary" @click="shiftWeek(-1)">هفته قبل</button>
            <button class="btn btn-sm btn-outline-secondary" @click="shiftWeek(1)">هفته بعد</button>
          </div>
          <p class="mb-2">بازه: {{ jalali(ex.from) }} تا {{ jalali(addDays(ex.from, ex.days - 1)) }}</p>
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>عنوان</th><th>واحد</th><th>فاز</th><th>شروع</th><th>پایان</th><th>پیشرفت</th><th>شناوری</th><th>وضعیت</th></tr></thead>
            <tbody>
              <tr v-if="!exRows.length"><td colspan="9" class="text-center inv-muted">فعالیتی در این بازه نیست.</td></tr>
              <tr v-for="r in exRows" :key="r.activityId" :class="{ 'table-danger': r.isCritical && r.status !== 2 }">
                <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }} <span v-if="r.isLate" class="inv-badge warn">تاخیر</span></td><td>{{ unitText(r.responsibleUnit) }}</td><td>{{ phases[r.phase] }}</td>
                <td>{{ jalali(r.start) }}</td><td>{{ jalali(r.finish) }}</td><td>{{ pct(r.percent) }}</td><td>{{ r.totalFloat }}</td><td>{{ activityStatuses[r.status] }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <div v-else-if="tab === 'needby'">
          <div class="d-flex gap-2 mb-3 d-print-none"><button v-if="canPlanning" :disabled="busy" class="btn btn-sm btn-primary" @click="applyNeed">اعمال تاریخ نیاز به درخواست‌های خرید</button><button class="btn btn-sm btn-outline-secondary" @click="loadNeed">بازخوانی</button></div>
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>فعالیت</th><th>شروع دیرترین</th><th>زمان تامین (روز)</th><th>تاریخ نیاز</th><th>درخواست خرید</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!needRows.length"><td colspan="7" class="text-center inv-muted">ردیفی نیست؛ ابتدا محاسبه مسیر بحرانی را اجرا کنید.</td></tr>
              <tr v-for="r in needRows" :key="r.activityId">
                <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td>{{ jalali(r.lateStart) }}</td><td>{{ r.leadTimeDays }}</td><td>{{ jalali(r.needByDate) }}</td>
                <td><span v-if="r.purchaseRequestLineId" :class="r.appliedToRequest ? 'ok' : 'warn'" class="inv-badge">{{ r.appliedToRequest ? "اعمال شد" : "اعمال نشده" }}</span></td>
                <td><span v-if="r.longLead" class="inv-badge bad">زمان‌بر</span></td>
              </tr>
            </tbody>
          </table>
        </div>

        <div v-else>
          <div class="d-flex flex-wrap gap-2 align-items-end mb-3 d-print-none">
            <div style="min-width: 200px"><label class="form-label small mb-0">ساخت فعالیت‌های ITP برای دستور ساخت</label><WorkOrderOfContract v-model="itpOrder" :contract-id="pcContractId" placeholder="دستور ساخت" /></div>
            <button v-if="canPlanning" :disabled="busy || !itpOrder" class="btn btn-sm btn-primary" @click="genItp">ساخت فعالیت‌ها</button>
            <div style="width: 130px"><label class="form-label small mb-0">روز آینده</label><input v-model.number="lookAhead" class="form-control form-control-sm" type="number" min="1" placeholder="پیش‌فرض تنظیمات" /></div>
            <button class="btn btn-sm btn-secondary" @click="loadNotices">نمایش پیش‌نویس‌ها</button>
          </div>
          <table class="table table-sm">
            <thead><tr><th>ITP</th><th>مرحله</th><th>فعالیت</th><th>طرف بازرسی</th><th>تاریخ برنامه</th><th>مهلت اعلام</th><th>متن پیش‌نویس</th></tr></thead>
            <tbody>
              <tr v-if="!notices.length"><td colspan="7" class="text-center inv-muted">نقطه بازرسی نزدیکی نیست.</td></tr>
              <tr v-for="n in notices" :key="n.itpActivityId">
                <td class="inv-ltr">{{ n.itpNumber }}</td><td>{{ n.stage }}</td><td>{{ n.activity }}</td><td>{{ n.party }}</td><td>{{ jalali(n.plannedDate) }}</td>
                <td>{{ jalali(n.noticeDeadline) }} <span v-if="n.noticeLate" class="inv-badge bad">دیر</span></td><td class="small">{{ n.text }}</td>
              </tr>
            </tbody>
          </table>
          <p class="inv-muted mb-0">اعلام نقطه بازرسی را QC ارسال می‌کند؛ این فهرست فقط پیش‌نویس سیستم است.</p>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
