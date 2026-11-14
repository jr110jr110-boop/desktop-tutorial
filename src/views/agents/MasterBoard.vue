<script setup lang="ts">
// «نمای ایجنت اصلی»: the whole process of the company on one line of thirteen stations, one row per contract (and one for the company itself). Read by PMO, CEO and the auditor
// (AgentRoles.board). The step catalogue is read by PMO and CEO; only the PMO retries a failed task and switches a step. Refreshes every 60 s while the browser tab is visible.
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { enableAgentStep, getAgentBoard, getAgentSteps, getAgentTask, getAgentTasks, retryAgentTask } from "@/api/AgentAPIs";
import { rowsOf } from "@/api/ApiClient";
import {
  agentTitleOf, ageShort, ageText, AgentCellStatus, badgeOfState, badgeOfStuck, cellStatuses, cellStatusOrder, fa, jalali, rolesText, startAutoRefresh, stationTitles, stepKinds, stuckReasons,
  taskLink, taskStates,
} from "@/composables/agents";
import { fail, ok } from "@/composables/inventory";
import { AgentRoles, hasRole } from "@/composables/roles";

const router = useRouter();
const canAdmin = hasRole(AgentRoles.admin);
const canSteps = hasRole(AgentRoles.steps);

const board = ref<any | null>(null);
const steps = ref<any[]>([]);
const loading = ref(false);
const busyRetry = ref<number | null>(null);
const busyStep = ref<string | null>(null);

// ---- icons of the nodes (24 x 24, stroked)
const icons: Record<number, string> = {
  [AgentCellStatus.NotStarted]: "",
  [AgentCellStatus.Idle]: "M5 12.5l4.5 4.5L19 7.5",
  [AgentCellStatus.Agent]: "M13 3L5 14h6l-1 7 8-11h-6z",
  [AgentCellStatus.WaitingInput]: "M4 20l4-1 11-11-3-3L5 16z",
  [AgentCellStatus.WaitingPhysical]: "M4 8l8-4 8 4v8l-8 4-8-4zM4 8l8 4 8-4M12 12v8",
  [AgentCellStatus.WaitingGate]: "M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6zM9 12l2 2 4-4",
  [AgentCellStatus.Failed]: "M6 6l12 12M18 6L6 18",
};

const stations = computed<{ number: number; title: string }[]>(() => board.value?.stations?.length ? board.value.stations : stationTitles.map((t, i) => ({ number: i + 1, title: t })));
const rowsOfBoard = computed<any[]>(() => board.value?.rows || []);
const stuck = computed<any[]>(() => board.value?.stuck || []);
const openTotal = computed(() => rowsOfBoard.value.reduce((n, r) => n + r.cells.reduce((m: number, c: any) => m + (c.openCount || 0), 0), 0));
const gateTotal = computed(() => rowsOfBoard.value.reduce((n, r) => n + r.cells.filter((c: any) => c.status === AgentCellStatus.WaitingGate).length, 0));
const failedTotal = computed(() => stuck.value.filter((s) => s.reason === 1).length);
const stationOfStep = (s: any) => s.station;
const stepGroups = computed(() => {
  const groups: { station: number; title: string; steps: any[] }[] = [];
  for (const s of steps.value) {
    let g = groups.find((x) => x.station === stationOfStep(s));
    if (!g) groups.push((g = { station: s.station, title: s.stationTitle, steps: [] }));
    g.steps.push(s);
  }
  return groups.sort((a, b) => a.station - b.station);
});

let seq = 0;
async function loadAll() {
  const mine = ++seq;
  loading.value = true;
  const [[b, be], [st, se]] = await Promise.all([getAgentBoard(), canSteps ? getAgentSteps() : Promise.resolve([[], null] as [any, null])]);
  if (mine !== seq) return;
  loading.value = false;
  if (be) fail(be);
  else board.value = b;
  if (se) fail(se);
  else if (canSteps) steps.value = rowsOf(st);
  if (panel.value) await loadPanel(false);
}

// ---- the side panel of one cell
const panel = ref<{ station: number; stationTitle: string; contractId: number | null; label: string } | null>(null);
const panelTasks = ref<any[]>([]);
const panelLoading = ref(false);
const selected = ref<any | null>(null);
const detailLoading = ref(false);

function openCell(row: any, cell: any) {
  if (cell.status === AgentCellStatus.NotStarted) return;
  const title = stations.value.find((s) => s.number === cell.station)?.title || String(cell.station);
  panel.value = { station: cell.station, stationTitle: title, contractId: row.contractId ?? null, label: row.label };
  selected.value = null;
  loadPanel(true);
}
async function loadPanel(first: boolean) {
  const p = panel.value;
  if (!p) return;
  if (first) panelLoading.value = true;
  const params: any = { Station: p.station, OpenOnly: true, PageNumber: 1, PageSize: 200 };
  if (p.contractId !== null) params.ContractId = p.contractId;
  const [data, error] = await getAgentTasks(params);
  panelLoading.value = false;
  if (error) return fail(error);
  // the company row has no contract: the API cannot filter "no contract", so the tasks of contracts are dropped here
  panelTasks.value = rowsOf(data).filter((t) => (p.contractId === null ? !t.contractId : true));
  if (selected.value) await loadDetail(selected.value.id, false);
}
async function loadDetail(id: number, first: boolean) {
  if (first) detailLoading.value = true;
  const [data, error] = await getAgentTask(id);
  detailLoading.value = false;
  if (error) return fail(error);
  selected.value = data;
}
function closePanel() {
  panel.value = null;
  selected.value = null;
  panelTasks.value = [];
}

// ---- actions
function go(link: string | null | undefined) {
  const to = taskLink(router, link);
  if (to) router.push(to);
}
async function retry(id: number) {
  if (busyRetry.value) return;
  busyRetry.value = id;
  const [, error] = await retryAgentTask(id);
  busyRetry.value = null;
  if (error) return fail(error);
  ok("کار ناموفق دوباره در صف ایجنت قرار گرفت.");
  await loadAll();
}
async function toggle(s: any, enabled: boolean) {
  if (!canAdmin || busyStep.value) return;
  busyStep.value = s.code;
  const [data, error] = await enableAgentStep(s.code, enabled);
  busyStep.value = null;
  if (error) return fail(error);
  const i = steps.value.findIndex((x) => x.code === s.code);
  if (i >= 0) steps.value.splice(i, 1, data);
  ok(enabled ? "گام فعال شد؛ ایجنت آن را انجام می‌دهد." : "گام غیرفعال شد؛ این گام دستی (با انسان) انجام می‌شود.");
}
function onSwitch(s: any, ev: Event) {
  const input = ev.target as HTMLInputElement;
  const want = input.checked;
  // the switch follows the server's answer, not the click
  input.checked = s.enabled;
  toggle(s, want);
}

function cellKey(c: any) {
  return cellStatuses[c.status]?.key || "none";
}
function cellTitle(row: any, c: any) {
  const st = cellStatuses[c.status];
  const parts = [st?.title];
  if (c.title) parts.push(c.title);
  if (c.waitingOn) parts.push("منتظر: " + rolesText(c.waitingOn));
  if (c.openCount) parts.push("کارهای باز: " + fa(c.openCount));
  return parts.filter(Boolean).join(" — ");
}

let stop: (() => void) | null = null;
onMounted(() => {
  loadAll();
  stop = startAutoRefresh(loadAll);
});
onBeforeUnmount(() => stop && stop());
</script>

<template>
  <div class="inv-page ag">
    <div class="ag-card ag-hero">
      <div style="flex: 1 1 360px; min-width: 0">
        <h1>نمای ایجنت اصلی</h1>
        <p>فرایند شرکت روی یک خط سیزده‌ایستگاهی؛ ایجنت‌ها کار می‌کنند و هر جا انسان لازم باشد همان‌جا دیده می‌شود.</p>
        <p v-if="board" class="inv-muted">آخرین به‌روزرسانی: {{ jalali(board.generatedAt, true) }}</p>
      </div>
      <div class="ag-tiles">
        <div class="ag-tile"><b>{{ fa(openTotal) }}</b><span>کار باز</span></div>
        <div class="ag-tile gate"><b>{{ fa(gateTotal) }}</b><span>گیت منتظر انسان</span></div>
        <div :class="{ bad: stuck.length > 0 }" class="ag-tile"><b>{{ fa(stuck.length) }}</b><span>گیرکرده</span></div>
        <div :class="{ bad: failedTotal > 0 }" class="ag-tile"><b>{{ fa(failedTotal) }}</b><span>ناموفق</span></div>
      </div>
    </div>

    <div class="ag-card">
      <div class="ag-legend" aria-label="راهنما">
        <span v-for="k in cellStatusOrder" :key="k">
          <i :class="cellStatuses[k].key" class="ag-node" style="width: 20px; height: 20px; margin: 0; display: inline-flex">
            <svg v-if="icons[k]" style="width: 11px; height: 11px" viewBox="0 0 24 24"><path :d="icons[k]" /></svg>
          </i>
          {{ cellStatuses[k].title }}
        </span>
      </div>

      <div v-if="!board" class="ag-empty">{{ loading ? "در حال بارگذاری..." : "اطلاعاتی برای نمایش نیست." }}</div>
      <div v-else class="ag-scroll">
        <div class="ag-line">
          <div></div>
          <div v-for="s in stations" :key="s.number" class="ag-line-head"><b>{{ fa(s.number) }}</b>{{ s.title }}</div>
          <template v-for="row in rowsOfBoard" :key="row.contractId ?? 'general'">
            <div class="ag-line-label">{{ row.label }}</div>
            <button
              v-for="c in row.cells"
              :key="c.station"
              :disabled="c.status === AgentCellStatus.NotStarted"
              :title="cellTitle(row, c)"
              class="ag-cell"
              @click="openCell(row, c)">
              <span :class="cellKey(c)" class="ag-node">
                <svg v-if="icons[c.status]" viewBox="0 0 24 24"><path :d="icons[c.status]" /></svg>
                <span v-if="c.openCount > 1" class="ag-count">{{ fa(c.openCount) }}</span>
              </span>
              <span :class="cellKey(c)" class="ag-cell-text">
                <b>{{ cellStatuses[c.status]?.short }}</b>
                <template v-if="c.status === AgentCellStatus.WaitingGate">
                  <span class="d-block">{{ rolesText(c.waitingOn) }}</span>
                  <span class="d-block">قدیمی‌ترین: {{ ageShort(c.oldestHours) }}</span>
                </template>
                <template v-else-if="c.status === AgentCellStatus.Failed">
                  <span class="d-block">{{ ageShort(c.oldestHours) }}</span>
                </template>
                <template v-else-if="c.status >= AgentCellStatus.Agent">
                  <span class="d-block">{{ ageShort(c.oldestHours) }}</span>
                </template>
              </span>
            </button>
          </template>
        </div>
      </div>
    </div>

    <div class="ag-card">
      <h2 class="inv-title mb-2">موارد گیرکرده</h2>
      <div v-if="!stuck.length" class="ag-empty">مورد گیرکرده‌ای نیست؛ همه‌چیز در جریان است.</div>
      <div v-for="s in stuck" :key="s.taskId" class="ag-stuck">
        <div class="ag-stuck-main">
          <div class="d-flex flex-wrap gap-2 align-items-center">
            <span :class="badgeOfStuck(s.reason)" class="ag-chip">{{ stuckReasons[s.reason] }}</span>
            <span class="ag-chip">{{ s.stationTitle }}</span>
            <b>{{ s.title }}</b>
          </div>
          <div class="ag-row-meta mt-1">
            <span v-if="s.contractLabel">{{ s.contractLabel }}</span>
            <span v-if="s.waitingOn">· منتظر: {{ rolesText(s.waitingOn) }}</span>
            <span>· {{ ageText(s.ageHours) }}</span>
            <span class="inv-ltr">{{ s.stepCode }}</span>
          </div>
          <div v-if="s.error" class="ag-note bad">{{ s.error }}</div>
        </div>
        <div class="d-flex gap-2">
          <button :disabled="!taskLink(router, s.link)" class="btn btn-sm btn-outline-primary" @click="go(s.link)">باز کردن سند</button>
          <button v-if="canAdmin && s.reason === 1" :disabled="busyRetry === s.taskId" class="btn btn-sm btn-outline-danger" @click="retry(s.taskId)">تلاش دوباره</button>
        </div>
      </div>
    </div>

    <div v-if="canSteps" class="ag-card">
      <h2 class="inv-title mb-1">گام‌ها و کلیدها</h2>
      <p class="inv-muted">هر گام یا توسط ایجنت انجام می‌شود یا (وقتی خاموش است) دستی و با انسان. {{ canAdmin ? "کلید گام را فقط PMO تغییر می‌دهد." : "کلیدها را فقط PMO تغییر می‌دهد." }}</p>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>کد</th><th>عنوان</th><th>نوع</th><th>ایجنت</th><th>وضعیت گام</th><th>صندلی ایجنت</th><th>کار باز</th></tr></thead>
          <tbody>
            <tr v-if="!steps.length"><td colspan="7" class="text-center inv-muted">{{ loading ? "در حال بارگذاری..." : "گامی ثبت نشده است." }}</td></tr>
            <template v-for="g in stepGroups" :key="g.station">
              <tr><td class="ag-station-head" colspan="7">{{ fa(g.station) }}. {{ g.title }}</td></tr>
              <tr v-for="s in g.steps" :key="s.code">
                <td class="inv-ltr">{{ s.code }}</td>
                <td>{{ s.title }}</td>
                <td>{{ stepKinds[s.kind] }}</td>
                <td>{{ agentTitleOf(s.agentCode) }}</td>
                <td>
                  <div class="d-flex align-items-center gap-2">
                    <div class="form-check form-switch m-0">
                      <input :id="'sw-' + s.code" :checked="s.enabled" :disabled="!canAdmin || busyStep === s.code" :aria-label="'فعال بودن گام ' + s.title" class="form-check-input" role="switch" type="checkbox" @change="onSwitch(s, $event)" />
                    </div>
                    <span v-if="!s.enabled" class="ag-chip warn">دستی (با انسان)</span>
                    <span v-else class="ag-chip ok">ایجنت</span>
                  </div>
                </td>
                <td><span :class="s.seatEnabled ? 'ok' : 'bad'" class="ag-chip">{{ s.seatEnabled ? "روشن" : "خاموش" }}</span></td>
                <td>{{ fa(s.openTasks) }}</td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
    </div>

    <template v-if="panel">
      <div class="ag-panel-backdrop" @click="closePanel"></div>
      <aside aria-label="کارهای این ایستگاه" class="ag-panel">
        <div class="d-flex justify-content-between align-items-start gap-2 mb-2">
          <div>
            <h2 class="inv-title">{{ panel.stationTitle }}</h2>
            <div class="inv-muted">{{ panel.label }}</div>
          </div>
          <button class="btn btn-sm btn-outline-secondary" @click="closePanel">بستن</button>
        </div>
        <div v-if="panelLoading" class="ag-empty">در حال بارگذاری...</div>
        <div v-else-if="!panelTasks.length" class="ag-empty">کار بازی در این ایستگاه نیست.</div>
        <div v-for="t in panelTasks" :key="t.id" :class="{ on: selected && selected.id === t.id }" class="ag-panel-task" @click="loadDetail(t.id, true)">
          <div class="d-flex flex-wrap gap-2 align-items-center">
            <b>{{ t.title }}</b>
            <span :class="badgeOfState(t.state)" class="ag-chip">{{ taskStates[t.state] }}</span>
            <span v-if="t.overdue" class="ag-chip bad">با تأخیر</span>
            <span v-if="t.isFallback" class="ag-chip warn">دستی (ایجنت خاموش)</span>
          </div>
          <div class="ag-row-meta mt-1">
            <span>ایجنت: {{ agentTitleOf(t.agentCode) }}</span>
            <span>· {{ ageText(t.ageHours) }}</span>
            <span v-if="t.gateRoles">· {{ rolesText(t.gateRoles) }}</span>
            <span v-if="t.targetNumber" class="inv-ltr">{{ t.targetNumber }}</span>
          </div>
          <div v-if="t.lastError" class="ag-note bad">{{ t.lastError }}</div>
          <div class="ag-actions" @click.stop>
            <button :disabled="!taskLink(router, t.link)" class="btn btn-sm btn-outline-primary" @click="go(t.link)">باز کردن سند</button>
            <button v-if="canAdmin && t.state === 7" :disabled="busyRetry === t.id" class="btn btn-sm btn-outline-danger" @click="retry(t.id)">تلاش دوباره</button>
          </div>
        </div>

        <div v-if="selected" class="mt-3">
          <h3 class="inv-title mb-2">رویدادهای «{{ selected.title }}»</h3>
          <div v-if="detailLoading" class="inv-muted">در حال بارگذاری...</div>
          <div v-if="selected.summary" class="ag-prepared">{{ selected.summary }}</div>
          <div v-if="!(selected.events || []).length" class="inv-muted">رویدادی ثبت نشده است.</div>
          <div class="inv-timeline">
            <div v-for="e in selected.events || []" :key="e.id" class="step" :class="{ rejected: e.toState === 7, done: e.toState === 10, active: e.toState !== 7 && e.toState !== 10 }">
              <div>
                <b>{{ taskStates[e.toState] || e.toState }}</b>
                <span v-if="e.fromState" class="inv-muted"> (از «{{ taskStates[e.fromState] || e.fromState }}»)</span>
              </div>
              <div class="inv-muted">{{ e.atText || jalali(e.at, true) }}<span v-if="e.seatCode"> · {{ agentTitleOf(e.seatCode) }}</span></div>
              <div v-if="e.note" class="small" style="overflow-wrap: anywhere">{{ e.note }}</div>
            </div>
          </div>
        </div>
      </aside>
    </template>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="./agents.scss"></style>
