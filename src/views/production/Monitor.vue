<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import CameraTiles from "@/components/production/monitor/CameraTiles.vue";
import MonitorSettings from "@/components/production/monitor/MonitorSettings.vue";
import { getLiveBoard, getStateEvents, getUtilisation, pollMonitors } from "@/api/ShopAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hoursText } from "@/composables/production";
import { DncRoles } from "@/composables/dnc";
import { ProductionRoles, hasRole } from "@/composables/roles";

// Live monitoring of the machines (MfMonitor): the live board (refreshed every 10 seconds), the measured utilisation against the booked hours, the state history, the shop cameras and the settings.
const tab = ref<"live" | "util" | "events" | "cameras" | "settings">("live");
const board = ref<any>(null);
const states: Record<number, string> = { 0: "خاموش", 1: "آماده / بیکار", 2: "در حال کار", 3: "آلارم", 4: "بدون ارتباط" };
const stateClass: Record<number, string> = { 0: "st-off", 1: "st-idle", 2: "st-run", 3: "st-alarm", 4: "st-offline" };
const canPoll = hasRole(ProductionRoles.supervisors);
const canSettings = hasRole(DncRoles.linkEditors) || hasRole(DncRoles.linkTesters);
const canCameras = hasRole(ProductionRoles.readers);
let timer: any = null;

async function loadBoard() {
  const [d, error] = await getLiveBoard();
  if (error) return fail(error);
  board.value = d;
}
async function pollNow() {
  const [d, error] = await pollMonitors();
  if (error) return fail(error);
  ok(`پایش شد: ${d.polled} ماشین، ${d.changed} تغییر وضعیت، ${d.alarms} آلارم.`);
  loadBoard();
}

// utilisation: preset windows only (no Gregorian date is shown)
const isoDay = (d: Date) => d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0");
const windowDays = ref(1);
const util = ref<any[]>([]);
async function loadUtil() {
  const to = new Date();
  to.setDate(to.getDate() + 1);
  const from = new Date();
  from.setDate(from.getDate() - (windowDays.value - 1));
  const [d, error] = await getUtilisation(isoDay(from) + "T00:00:00", isoDay(to) + "T00:00:00");
  if (error) return fail(error);
  util.value = d;
}

const eventFilters = ref({ State: null as number | null });
const events = usePagedList(getStateEvents, eventFilters);
const minutesText = (m: number) => hoursText((m || 0) / 60);
const gapClass = (g: number | null) => (g === null ? "" : Math.abs(g) > 20 ? "bad" : Math.abs(g) > 10 ? "warn" : "ok");
const summary = computed(() => board.value ? [
  { k: 2, n: board.value.running }, { k: 1, n: board.value.idle }, { k: 3, n: board.value.alarm }, { k: 0, n: board.value.off }, { k: 4, n: board.value.offline },
] : []);

function open(t: typeof tab.value) {
  tab.value = t;
  if (t === "util") loadUtil();
  if (t === "events") events.load();
}
onMounted(() => {
  loadBoard();
  timer = setInterval(() => tab.value === "live" && loadBoard(), 10000);
});
onBeforeUnmount(() => clearInterval(timer));
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">پایش زنده ماشین‌ها</h1>
        <button v-if="canPoll" class="btn btn-sm btn-outline-primary" @click="pollNow">پایش هم‌اکنون</button>
      </div>
      <ul class="nav nav-tabs mb-3">
        <li class="nav-item"><a :class="{ active: tab === 'live' }" class="nav-link" href="#" @click.prevent="open('live')">وضعیت زنده</a></li>
        <li class="nav-item"><a :class="{ active: tab === 'util' }" class="nav-link" href="#" @click.prevent="open('util')">بهره‌برداری واقعی</a></li>
        <li class="nav-item"><a :class="{ active: tab === 'events' }" class="nav-link" href="#" @click.prevent="open('events')">تاریخچه وضعیت</a></li>
        <li v-if="canCameras" class="nav-item"><a :class="{ active: tab === 'cameras' }" class="nav-link" href="#" @click.prevent="open('cameras')">دوربین‌ها</a></li>
        <li v-if="canSettings" class="nav-item"><a :class="{ active: tab === 'settings' }" class="nav-link" href="#" @click.prevent="open('settings')">تنظیمات پایش</a></li>
      </ul>

      <template v-if="tab === 'live'">
        <p v-if="!board" class="inv-muted">در حال بارگذاری...</p>
        <template v-else>
          <div class="d-flex flex-wrap gap-2 mb-3 small">
            <span v-for="s in summary" :key="s.k" :class="stateClass[s.k]" class="mon-chip">{{ states[s.k] }}: {{ s.n }}</span>
            <span v-if="board.mismatches" class="mon-chip st-alarm">ناهمخوانی با ثبت زمان: {{ board.mismatches }}</span>
            <span class="inv-muted ms-auto">به‌روز: {{ board.asOfText }}</span>
          </div>
          <p v-if="!board.machines.length" class="inv-muted">هنوز پایشی برای ماشین‌ها تنظیم نشده است (زبانه «تنظیمات پایش»).</p>
          <div class="mon-grid">
            <div v-for="m in board.machines" :key="m.machineId" :class="m.state === null || m.state === undefined ? 'st-offline' : stateClass[m.state]" class="mon-card">
              <div class="d-flex justify-content-between"><b>{{ m.machineTitle }}</b><span class="inv-ltr small">{{ m.workCenterCode }}</span></div>
              <div class="mon-state">{{ m.state === null || m.state === undefined ? "هنوز دیده نشده" : states[m.state] }}</div>
              <div class="small">از {{ m.sinceText || "-" }} ({{ minutesText(m.minutesInState) }})</div>
              <div v-if="m.alarmText || m.alarmCode" class="small text-danger">آلارم {{ m.alarmCode }} {{ m.alarmText }}</div>
              <div v-if="m.programName || m.lastRunProgram" class="small">برنامه: <span class="inv-ltr">{{ m.programName || m.lastRunProgram }}</span><span v-if="m.lastRunRevision"> (نسخه {{ m.lastRunRevision }})</span></div>
              <div v-if="m.spindleRpm || m.partCount" class="small inv-ltr">S {{ m.spindleRpm ?? "-" }} · {{ m.partCount ?? "-" }} pcs</div>
              <div v-if="m.operationName" class="small">کار: {{ m.operationName }}<span v-if="m.operatorName"> — {{ m.operatorName }}</span></div>
              <div class="small mt-1">امروز: کارکرد {{ minutesText(m.today.running) }}، بیکار {{ minutesText(m.today.idle) }}، آلارم {{ minutesText(m.today.alarm) }}<span v-if="m.todayRunPercent !== null"> — {{ m.todayRunPercent }}٪</span></div>
              <div v-if="m.mismatchText" class="mon-warn small mt-1">{{ m.mismatchText }}</div>
              <div class="inv-muted small">آخرین ارتباط: {{ m.lastSeenAtText || "-" }}</div>
            </div>
          </div>
        </template>
      </template>

      <template v-else-if="tab === 'util'">
        <div class="inv-toolbar">
          <div class="inv-filters">
            <select v-model.number="windowDays" class="form-select form-select-sm" @change="loadUtil"><option :value="1">امروز</option><option :value="7">۷ روز اخیر</option><option :value="30">۳۰ روز اخیر</option><option :value="90">۹۰ روز اخیر</option></select>
          </div>
        </div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>ماشین</th><th>کارکرد</th><th>بیکار</th><th>خاموش</th><th>آلارم</th><th>بدون ارتباط</th><th>سهم کارکرد</th><th>تعداد آلارم</th><th>ساعت ثبت‌شده اپراتور</th><th>اختلاف ثبت با واقعی</th></tr></thead>
            <tbody>
              <tr v-if="!util.length"><td colspan="10" class="text-center inv-muted">داده‌ای نیست.</td></tr>
              <tr v-for="u in util" :key="u.machineId">
                <td>{{ u.machineTitle }}</td><td>{{ minutesText(u.minutes.running) }}</td><td>{{ minutesText(u.minutes.idle) }}</td><td>{{ minutesText(u.minutes.off) }}</td><td>{{ minutesText(u.minutes.alarm) }}</td>
                <td>{{ minutesText(u.minutes.offline) }}</td><td>{{ u.runPercent ?? "-" }}{{ u.runPercent !== null ? "٪" : "" }}</td><td>{{ u.alarms }}</td><td>{{ hoursText(u.bookedHours) }}</td>
                <td><span :class="gapClass(u.bookingGapPercent)" class="inv-badge">{{ u.bookingGapPercent === null ? "-" : u.bookingGapPercent + "٪" }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="inv-muted small">سهم کارکرد = زمان کار واقعی ماشین به کل زمانِ دیده‌شده (زمان بدون ارتباط حساب نمی‌شود). اختلاف مثبت یعنی اپراتورها بیش از کار واقعی ماشین ثبت زمان کرده‌اند.</p>
      </template>

      <template v-else-if="tab === 'events'">
        <div class="inv-toolbar"><div class="inv-filters"><select v-model="eventFilters.State" class="form-select form-select-sm" @change="events.search"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in states" :key="k" :value="Number(k)">{{ t }}</option></select></div></div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>ماشین</th><th>وضعیت</th><th>از</th><th>تا</th><th>مدت</th><th>آلارم</th></tr></thead>
            <tbody>
              <tr v-if="!events.rows.value.length"><td colspan="6" class="text-center inv-muted">رویدادی نیست.</td></tr>
              <tr v-for="e in events.rows.value" :key="e.id"><td>{{ e.machineTitle }}</td><td><span :class="stateClass[e.state]" class="mon-chip">{{ states[e.state] }}</span></td><td>{{ e.startAtText }}</td><td>{{ e.endAtText || "ادامه دارد" }}</td><td>{{ minutesText(e.minutes) }}</td><td class="small">{{ e.alarmCode }} {{ e.alarmText }}<span v-if="e.maintenanceRequestId" class="inv-badge info ms-1">درخواست تعمیر</span></td></tr>
            </tbody>
          </table>
        </div>
        <QPager :page="events.page.value" :total="events.total.value" @go="events.go" />
      </template>

      <CameraTiles v-else-if="tab === 'cameras'" />
      <MonitorSettings v-else />
    </div>
  </div>
</template>

<style scoped>
.mon-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }
.mon-card { border-radius: 10px; padding: 12px; border: 2px solid #ccc; background: var(--bs-body-bg, #fff); }
.mon-state { font-size: 1.3rem; font-weight: 700; margin: 4px 0; }
.mon-chip { border-radius: 12px; padding: 2px 10px; border: 1px solid #ccc; }
.mon-warn { background: #fff3e0; color: #b45309; border-radius: 6px; padding: 2px 6px; }
.st-run { border-color: #2e7d32; } .st-run .mon-state, .mon-chip.st-run { color: #2e7d32; }
.st-idle { border-color: #f9a825; } .st-idle .mon-state, .mon-chip.st-idle { color: #b7791f; }
.st-alarm { border-color: #c62828; } .st-alarm .mon-state, .mon-chip.st-alarm { color: #c62828; }
.st-off { border-color: #757575; } .st-off .mon-state, .mon-chip.st-off { color: #616161; }
.st-offline { border-color: #9e9e9e; border-style: dashed; } .st-offline .mon-state, .mon-chip.st-offline { color: #9e9e9e; }
</style>

<style lang="scss" src="../fn/fn.scss"></style>
