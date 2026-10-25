<script setup lang="ts">
// «دستیارهای زمان‌بندی‌شده»: the AI seats (each acts as a service user with the roles of its job, on a schedule), the switch of each seat, «اجرای همین حالا» and the history of runs.
// A run by hand returns a running run: it is read again every 2 seconds until it has finished. A seat only writes notes and drafts; it never approves anything.
// Read: PMO and CEO. Write (switch, run): PMO.
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import AiAdminTabs from "@/components/ai/AiAdminTabs.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import QPager from "@/components/quality/QPager.vue";
import { enableAiSeat, getAiSeatRun, getAiSeatRuns, getAiSeats, runAiSeat } from "@/api/AiAPIs";
import { badgeOfRun, POLL_LOST_TEXT, runStatuses, seatTitleOf, seatTitles, startPolling } from "@/composables/ai";
import { usePagedList } from "@/composables/fn";
import { fail, jalali, ok } from "@/composables/inventory";
import { AiRoles, hasRole, roleTitles } from "@/composables/roles";

const canWrite = hasRole(AiRoles.admins);
const seats = ref<any[]>([]);
const loading = ref(false);
const busy = ref(false);
const runDialog = ref<any>(null);
/** the runs started here, by seat code: the last answer of the API for each */
const active = ref<Record<string, any>>({});
const stops: Record<string, () => void> = {};
/** the polling of this seat gave up: the run state is unknown */
const lost = ref<Record<string, boolean>>({});
const filters = ref<any>({ Seat: null, Status: null });
const history = usePagedList(getAiSeatRuns, filters, 25);
const open = ref<number | null>(null);
const seatCodes = computed(() => Object.keys(seatTitles).filter((c) => c !== "EVAL"));

const rolesText = (csv: string) => (csv || "").split(",").filter(Boolean).map((r) => roleTitles[r] || r).join("، ");

async function load() {
  loading.value = true;
  const [data, error] = await getAiSeats();
  loading.value = false;
  if (error) return fail(error);
  seats.value = data || [];
  resume();
}

async function toggle(s: any, e: Event) {
  const box = e.target as HTMLInputElement;
  busy.value = true;
  const [, error] = await enableAiSeat(s.code, !s.enabled);
  busy.value = false;
  if (error) {
    box.checked = s.enabled; // the switch goes back to what the server has
    return fail(error);
  }
  ok(s.enabled ? "دستیار غیرفعال شد." : "دستیار فعال شد.");
  load();
}

async function start(values: { asOf?: string | null }) {
  const seat = runDialog.value;
  runDialog.value = null;
  busy.value = true;
  const [run, error] = await runAiSeat(seat.code, values.asOf || null);
  busy.value = false;
  if (error) return fail(error);
  active.value = { ...active.value, [seat.code]: run };
  ok("اجرای دستیار آغاز شد.");
  watchRun(seat.code, run.id);
}

function watchRun(code: string, id: number) {
  stops[code]?.();
  lost.value = { ...lost.value, [code]: false };
  stops[code] = startPolling(
    async () => {
      const [run, error] = await getAiSeatRun(id);
      if (error) throw error; // startPolling tries again, and gives up after 3 failures in a row
      active.value = { ...active.value, [code]: run };
      if (run.status === 1) return false;
      load();
      history.load();
      return true;
    },
    2000,
    15 * 60 * 1000,
    () => (lost.value = { ...lost.value, [code]: true })
  );
}

/** a run that was started before this page was opened (by the scheduler or by another person) is followed too */
function resume() {
  for (const s of seats.value) {
    if (s.lastRun?.status === 1 && active.value[s.code]?.id !== s.lastRun.id) {
      active.value = { ...active.value, [s.code]: s.lastRun };
      watchRun(s.code, s.lastRun.id);
    }
  }
}

const running = (s: any) => !lost.value[s.code] && (active.value[s.code]?.status === 1 || s.lastRun?.status === 1);
onMounted(() => {
  load();
  history.load();
});
onBeforeUnmount(() => Object.values(stops).forEach((stop) => stop()));
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مدیریت هوش مصنوعی</h1></div>
      <AiAdminTabs />
      <div class="inv-toolbar">
        <h2 class="inv-title">دستیارهای زمان‌بندی‌شده</h2>
        <button class="btn btn-sm btn-outline-secondary" @click="load">بازخوانی</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>نقش‌ها</th><th>زمان‌بندی</th><th>فعال</th><th>آخرین اجرا</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!seats.length"><td colspan="7" class="text-center inv-muted">{{ loading ? "در حال بارگذاری..." : "دستیاری ثبت نشده است." }}</td></tr>
            <template v-for="s in seats" :key="s.code">
              <tr>
                <td class="inv-ltr">{{ s.code }}</td>
                <td>{{ seatTitleOf(s.code, s.title) }}</td>
                <td>{{ rolesText(s.roles) }}</td>
                <td>{{ s.scheduleText || s.schedule }}</td>
                <td>
                  <label v-if="canWrite" class="form-check form-switch mb-0">
                    <input :checked="s.enabled" :disabled="busy" class="form-check-input" role="switch" type="checkbox" @change="toggle(s, $event)" />
                  </label>
                  <span v-else :class="s.enabled ? 'ok' : 'warn'" class="inv-badge">{{ s.enabled ? "فعال" : "غیرفعال" }}</span>
                </td>
                <td>
                  <template v-if="s.lastRun">
                    <span :class="badgeOfRun(s.lastRun.status)" class="inv-badge">{{ runStatuses[s.lastRun.status] }}</span>
                    <span class="inv-muted ms-1">{{ s.lastRun.startedAtText }}</span>
                  </template>
                  <span v-else class="inv-muted">هنوز اجرا نشده</span>
                </td>
                <td>
                  <button v-if="canWrite" :disabled="busy || running(s) || !s.enabled" :title="s.enabled ? '' : 'ابتدا دستیار را فعال کنید'" class="btn btn-sm btn-outline-primary" @click="runDialog = s">
                    <span v-if="running(s)" class="spinner-border spinner-border-sm me-1"></span>اجرای همین حالا
                  </button>
                </td>
              </tr>
              <tr v-if="active[s.code]">
                <td colspan="7" class="bg-light">
                  <div class="d-flex flex-wrap gap-2 align-items-center">
                    <span :class="badgeOfRun(active[s.code].status)" class="inv-badge">{{ runStatuses[active[s.code].status] }}</span>
                    <span class="inv-muted">شروع: {{ active[s.code].startedAtText }}</span>
                    <span v-if="active[s.code].finishedAt" class="inv-muted">پایان: {{ jalali(active[s.code].finishedAt, true) }}</span>
                    <span v-if="active[s.code].asOf" class="inv-muted">روز داده: {{ jalali(active[s.code].asOf) }}</span>
                    <span v-if="lost[s.code]" class="text-danger">{{ POLL_LOST_TEXT }}</span>
                    <span v-else-if="active[s.code].status === 1" class="inv-muted"><span class="spinner-border spinner-border-sm"></span> در حال اجرا؛ هر ۲ ثانیه بازخوانی می‌شود...</span>
                  </div>
                  <div v-if="active[s.code].outputs" class="run-text mt-1"><b>خروجی:</b> {{ active[s.code].outputs }}</div>
                  <div v-if="active[s.code].error" class="run-text mt-1 text-danger"><b>خطا:</b> {{ active[s.code].error }}</div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar"><h2 class="inv-title">تاریخچه اجراها</h2></div>
      <div class="inv-filters mb-3">
        <div>
          <label class="form-label small mb-0">دستیار</label>
          <select v-model="filters.Seat" class="form-select form-select-sm">
            <option :value="null">همه</option>
            <option v-for="c in seatCodes" :key="c" :value="c">{{ seatTitles[c] }}</option>
          </select>
        </div>
        <div>
          <label class="form-label small mb-0">وضعیت</label>
          <select v-model="filters.Status" class="form-select form-select-sm">
            <option :value="null">همه</option>
            <option v-for="(t, k) in runStatuses" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <button class="btn btn-sm btn-secondary" @click="history.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>دستیار</th><th>شروع</th><th>پایان</th><th>روز داده</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!history.rows.value.length"><td colspan="6" class="text-center inv-muted">{{ history.loading.value ? "در حال بارگذاری..." : "اجرایی ثبت نشده است." }}</td></tr>
            <template v-for="r in history.rows.value" :key="r.id">
              <tr role="button" @click="open = open === r.id ? null : r.id">
                <td>{{ r.id }}</td>
                <td>{{ seatTitleOf(r.seatCode) }}</td>
                <td>{{ r.startedAtText }}</td>
                <td>{{ r.finishedAt ? jalali(r.finishedAt, true) : "-" }}</td>
                <td>{{ r.asOf ? jalali(r.asOf) : "-" }}</td>
                <td><span :class="badgeOfRun(r.status)" class="inv-badge">{{ runStatuses[r.status] }}</span></td>
              </tr>
              <tr v-if="open === r.id">
                <td colspan="6" class="bg-light">
                  <div v-if="r.outputs" class="run-text"><b>خروجی:</b> {{ r.outputs }}</div>
                  <div v-if="r.error" class="run-text text-danger"><b>خطا:</b> {{ r.error }}</div>
                  <div v-if="!r.outputs && !r.error" class="inv-muted">خروجی یا خطایی ثبت نشده است.</div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <QPager :page="history.page.value" :page-size="history.pageSize" :total="history.total.value" @go="history.go" />
    </div>

    <PromptModal
      v-if="runDialog"
      :fields="[{ key: 'asOf', label: 'روز داده (اختیاری؛ خالی = امروز)', type: 'date' }]"
      confirm-text="اجرا"
      hint="دستیار فقط یادداشت یا پیش‌نویس می‌سازد؛ هیچ چیزی را تایید نمی‌کند. روز آینده تنها با تنظیم سرور پذیرفته می‌شود."
      :title="'اجرای «' + seatTitleOf(runDialog.code, runDialog.title) + '»'"
      @cancel="runDialog = null"
      @confirm="start" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style scoped>
.run-text {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.7;
}
</style>
