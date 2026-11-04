<script setup lang="ts">
import moment from "moment-jalaali";
import { onMounted, reactive, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { getContracts, rowsOf } from "@/api/InventoryAPIs";
import { getTaskAgenda, getTaskBoard, getTaskCounts, getTasks } from "@/api/TasksAPIs";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import JDate from "@/components/pc/JDate.vue";
import TaskDrawer from "@/components/tasks/TaskDrawer.vue";
import { fail, jalali, printPage, today } from "@/composables/inventory";
import { contractsCache } from "@/composables/projectControl";
import { cartables, priorities, taskKinds, taskStatusClass, taskStatuses, weekDays } from "@/composables/tasks";

// Task management: cartables (list), board by status and calendar (day / week / month agenda). Every signed-in user has a cartable.
const route = useRoute();
const router = useRouter();
const view = ref<"list" | "board" | "calendar">("list");
const cartable = ref("mine");
const q = reactive<any>({ Contains: "", Status: "", Kind: "", Priority: "", ContractId: "" });
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const board = ref<any[]>([]);
const agenda = ref<any[]>([]);
const mode = ref("week");
const agendaDate = ref<string | null>(today());
const counts = ref<any>({});
const open = ref<{ id: number | null; preset?: any } | null>(null);

const query = () => ({ ...q, Cartable: cartable.value, PageNumber: page.value, PageSize: 25 });
async function load() {
  if (view.value === "list") {
    const [d, e] = await getTasks(query());
    if (e) return fail(e);
    rows.value = rowsOf(d);
    total.value = d?.totalItems ?? rows.value.length;
  } else if (view.value === "board") {
    const [d, e] = await getTaskBoard({ ...q, Cartable: cartable.value });
    if (e) return fail(e);
    board.value = Array.isArray(d) ? d : [];
  } else {
    const [d, e] = await getTaskAgenda({ Mode: mode.value, Date: agendaDate.value, MineOnly: cartable.value === "mine" });
    if (e) return fail(e);
    agenda.value = Array.isArray(d) ? d : [];
  }
  const [c] = await getTaskCounts();
  if (c) counts.value = c;
}
function pick(c: string) { cartable.value = c; page.value = 1; load(); }
function shift(dir: number) {
  // the month is a Persian month: the step goes through the Jalali calendar (the day is kept, or cut to the end of a shorter month), never a fixed number of days
  const m = moment(agendaDate.value || today(), "YYYY-MM-DD");
  if (mode.value === "month") m.add(dir, "jMonth"); else m.add(dir * (mode.value === "week" ? 7 : 1), "days");
  agendaDate.value = m.format("YYYY-MM-DD");
  load();
}
const badge = (c: string) => ({ "assigned-to-me": counts.value.assignedToMe, "assigned-by-me": counts.value.assignedByMe, overdue: counts.value.overdue, waiting: counts.value.waitingForMe, unit: counts.value.unit } as any)[c];
function openTask(id: number | null, preset?: any) { open.value = { id, preset }; }
function closed() { open.value = null; if (route.query.open) router.replace({ query: {} }); load(); }
onMounted(async () => {
  if (!contractsCache.value.length) { const [d] = await getContracts(); contractsCache.value = rowsOf(d); }
  if (route.query.open) openTask(Number(route.query.open));
  load();
});
watch(view, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="وظایف و کارتابل" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">وظایف و کارتابل</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <div class="btn-group btn-group-sm"><button :class="view === 'list' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="view = 'list'">فهرست</button><button :class="view === 'board' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="view = 'board'">بورد</button><button :class="view === 'calendar' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="view = 'calendar'">تقویم</button></div>
          <button class="btn btn-sm btn-success" @click="openTask(null)">+ وظیفه جدید</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>

      <ul class="nav nav-pills mb-3 d-print-none flex-wrap">
        <li v-for="c in cartables" :key="c[0]" class="nav-item"><a :class="{ active: cartable === c[0] }" class="nav-link py-1" href="#" @click.prevent="pick(c[0])">{{ c[1] }}<span v-if="badge(c[0])" class="badge bg-danger ms-1">{{ badge(c[0]) }}</span></a></li>
      </ul>

      <div v-if="view !== 'calendar'" class="row g-2 mb-3 d-print-none">
        <div class="col-md-3"><input v-model="q.Contains" class="form-control form-control-sm" placeholder="جستجو در شماره و عنوان" @keyup.enter="load" /></div>
        <div class="col-md-2"><select v-model="q.Status" class="form-select form-select-sm"><option value="">همه وضعیت‌ها</option><option v-for="(t, k) in taskStatuses" :key="k" :value="k">{{ t }}</option></select></div>
        <div class="col-md-2"><select v-model="q.Kind" class="form-select form-select-sm"><option value="">همه انواع</option><option v-for="(t, k) in taskKinds" :key="k" :value="k">{{ t }}</option></select></div>
        <div class="col-md-2"><select v-model="q.Priority" class="form-select form-select-sm"><option value="">همه اولویت‌ها</option><option v-for="(t, k) in priorities" :key="k" :value="k">{{ t }}</option></select></div>
        <div class="col-md-2"><select v-model="q.ContractId" class="form-select form-select-sm"><option value="">همه پروژه‌ها</option><option v-for="c in contractsCache" :key="c.id" :value="c.id">{{ c.code }}</option></select></div>
        <div class="col-md-1"><button class="btn btn-sm btn-secondary w-100" @click="page = 1; load()">نمایش</button></div>
      </div>

      <!-- list -->
      <div v-if="view === 'list'" class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>عنوان</th><th>نوع</th><th>اولویت</th><th>وضعیت</th><th>سررسید</th><th style="width: 120px">پیشرفت</th><th>مسئول</th><th>سازنده</th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">وظیفه‌ای در این کارتابل نیست.</td></tr>
            <tr v-for="t in rows" :key="t.id" :class="{ 'table-danger': t.overdue }" style="cursor: pointer" @click="openTask(t.id)">
              <td class="inv-ltr">{{ t.number }}</td>
              <td>{{ t.title }} <span v-if="t.checklistTotal" class="small inv-muted">({{ t.checklistDone }}/{{ t.checklistTotal }})</span><span v-if="t.commentCount" class="small inv-muted"> • {{ t.commentCount }} یادداشت</span></td>
              <td>{{ taskKinds[t.kind] }}</td><td>{{ priorities[t.priority] }}</td>
              <td><span :class="taskStatusClass[t.status]" class="inv-badge">{{ taskStatuses[t.status] }}</span></td>
              <td>{{ t.dueDateText }}</td>
              <td><div class="progress" style="height: 12px"><div class="progress-bar" :style="{ width: t.percentComplete + '%' }"></div></div></td>
              <td>{{ (t.assigneeNames || []).join("، ") }}</td><td>{{ t.creatorName }}</td>
            </tr>
          </tbody>
        </table>
        <div class="inv-pager d-print-none"><button :disabled="page <= 1" class="btn btn-sm btn-light" @click="page--; load()">قبلی</button><span>صفحه {{ page }} - {{ total }} وظیفه</span><button :disabled="page * 25 >= total" class="btn btn-sm btn-light" @click="page++; load()">بعدی</button></div>
      </div>

      <!-- board -->
      <div v-else-if="view === 'board'" class="d-flex gap-2" style="overflow-x: auto">
        <div v-for="col in board" :key="col.status" class="border rounded p-2 bg-light" style="min-width: 230px; flex: 1">
          <div class="fw-bold mb-2">{{ taskStatuses[col.status] }} <span class="badge bg-secondary">{{ col.count }}</span></div>
          <div v-for="t in col.items" :key="t.id" :class="{ 'border-danger': t.overdue }" class="card card-body p-2 mb-2" style="cursor: pointer" @click="openTask(t.id)">
            <div class="small inv-muted inv-ltr">{{ t.number }}</div>
            <div>{{ t.title }}</div>
            <div class="small inv-muted">{{ (t.assigneeNames || []).join("، ") }} <span v-if="t.dueDateText">• {{ t.dueDateText }}</span></div>
            <span class="inv-badge" style="width: fit-content">{{ priorities[t.priority] }}</span>
          </div>
          <p v-if="col.count > col.items.length" class="small inv-muted">و {{ col.count - col.items.length }} مورد دیگر</p>
        </div>
      </div>

      <!-- calendar -->
      <div v-else>
        <div class="d-flex flex-wrap gap-2 align-items-end mb-3 d-print-none">
          <div class="btn-group btn-group-sm"><button v-for="m in [['day', 'روز'], ['week', 'هفته'], ['month', 'ماه']]" :key="m[0]" :class="mode === m[0] ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="mode = m[0]; load()">{{ m[1] }}</button></div>
          <button class="btn btn-sm btn-light" @click="shift(-1)">قبلی</button>
          <div style="width: 170px"><JDate v-model="agendaDate" /></div>
          <button class="btn btn-sm btn-light" @click="shift(1)">بعدی</button>
          <button class="btn btn-sm btn-secondary" @click="load">نمایش</button>
          <span class="inv-muted small">فقط وظایف دارای سررسید؛ «کارتابل من» = برنامه شخصی</span>
        </div>
        <div class="row g-2">
          <div v-for="d in agenda" :key="d.date" :class="mode === 'month' ? 'col-md-3 col-lg-2' : mode === 'week' ? 'col-md-3' : 'col-12'">
            <div class="border rounded p-2 h-100" :class="{ 'bg-light': !d.items.length }">
              <div class="fw-bold small">{{ weekDays[d.weekDay] }} {{ jalali(d.date) }}</div>
              <div v-for="i in d.items" :key="i.taskId" :class="i.overdue ? 'text-danger' : ''" class="small mt-1" style="cursor: pointer" @click="openTask(i.taskId)">
                <span :class="taskStatusClass[i.status]" class="inv-badge">{{ taskStatuses[i.status] }}</span> {{ i.title }}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
    <TaskDrawer v-if="open" :key="open.id ?? 'new'" :task-id="open.id" :preset="open.preset || {}" @close="closed" @changed="load" />
  </div>
</template>
<style lang="scss" src="../inventory/inventory.scss"></style>
