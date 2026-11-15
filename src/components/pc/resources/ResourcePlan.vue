<script setup lang="ts">
// The resource plan of a project. The planning agent drafts it («تهیه‌شده توسط ایجنت»); a person reviews and edits it here, the planner submits it and the project manager approves or returns it.
// Nothing here is data entry first: the table shows what the agent prepared (source of every line), what is still without a resource and what is deliberately «بدون منبع».
import { computed, onMounted, ref, watch } from "vue";
import JDate from "@/components/pc/JDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import MspImport from "@/components/pc/resources/MspImport.vue";
import ResourcePlanPrint from "@/components/pc/print/ResourcePlanPrint.vue";
import { deleteAssignment, discardPlan, getBudget, getOpenableChanges, getPlan, getPlanActivities, getPlanVersions, getResources, isNoPlan, markUnassigned, openPlan, planAction, saveAssignments } from "@/api/ResourceAPIs";
import type { Budget, OpenableChange, PlanActivity, PlanAssignment, Resource, ResourcePlan } from "@/api/ResourceAPIs";
import { fail, ok } from "@/composables/inventory";
import { resourceClasses, unitTitle, usePcPrint } from "@/composables/projectControl";
import {
  assignmentSources, CONFIDENTIAL, distributions, faDateTime, money, NO_APPROVED_RATE, planDecideGate, planDiscardGate, planEditable, planStatusBadge, planStatuses, planSubmitGate, PREPARED_BY_AGENT, qty,
  resourceKinds, resourceLabel, ResourceTexts, unitText,
} from "@/composables/resources";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

const props = defineProps({ contractId: { type: Number, required: true } });

const plan = ref<ResourcePlan | null>(null);
const versions = ref<ResourcePlan[]>([]);
const versionSel = ref<number | null>(null);
const noPlan = ref(false);
const loading = ref(false);
const busy = ref(false);
const activities = ref<PlanActivity[]>([]);
const resources = ref<Resource[]>([]);
const budget = ref<Budget | null>(null);
const asOf = ref<string | null>(null);
const filter = ref<"all" | "need" | "done">("all");
const returning = ref(false);
const discarding = ref<{ reason: string } | null>(null);
const opening = ref<{ changeRequestId: number | null; changes: OpenableChange[] } | null>(null);
const canWrite = hasRole(ProjectControlRoles.resourceWriters);
const importing = ref(false);
const { job, run } = usePcPrint();
function imported(updated: ResourcePlan | null) {
  if (updated) plan.value = updated;
  loadPlan();
}
const canReadActivities = hasRole(ProjectControlRoles.readers);

const latestVersion = computed(() => Math.max(plan.value?.version || 0, ...versions.value.map((v) => v.version)));
const isLatest = computed(() => !plan.value || plan.value.version === latestVersion.value);
const editable = computed(() => planEditable(plan.value, isLatest.value));
const submitGate = computed(() => (plan.value ? planSubmitGate(plan.value, isLatest.value) : { show: false, reason: null }));
const discardGate = computed(() => (plan.value ? planDiscardGate(plan.value, isLatest.value) : { show: false, reason: null }));
const decideGate = computed(() => (plan.value ? planDecideGate(plan.value, isLatest.value) : { show: false, reason: null }));

async function loadPlan() {
  loading.value = true;
  noPlan.value = false;
  const [data, error] = await getPlan(props.contractId, versionSel.value);
  loading.value = false;
  if (error) {
    plan.value = null;
    budget.value = null;
    if (isNoPlan(error)) {
      noPlan.value = true;
      versions.value = [];
      return;
    }
    return fail(error);
  }
  plan.value = data;
  loadBudget();
}
async function loadVersions() {
  const [data, error] = await getPlanVersions(props.contractId);
  if (!error) versions.value = data;
}
async function loadActivities() {
  if (!canReadActivities) {
    activities.value = [];
    return;
  }
  const [data, error] = await getPlanActivities(props.contractId);
  if (error) return fail(error);
  activities.value = data;
}
async function loadResources() {
  if (!canWrite) return;
  const [data, error] = await getResources({ isActive: true });
  if (!error) resources.value = data;
}
async function loadBudget() {
  if (!plan.value) return;
  const [data, error] = await getBudget(plan.value.id, asOf.value);
  if (error) return fail(error);
  budget.value = data;
}
async function reloadAll() {
  await Promise.all([loadPlan(), loadVersions(), loadActivities()]);
  loadResources();
}
function take(result: [ResourcePlan | null, any], success?: string) {
  const [data, error] = result;
  if (error) {
    fail(error);
    return false;
  }
  plan.value = data;
  if (success) ok(success);
  loadBudget();
  return true;
}

// opening a plan: before any baseline it is an empty draft; after a baseline it needs the approved change request that built its own baseline (the server decides which qualify)
async function startOpen() {
  opening.value = { changeRequestId: null, changes: [] };
  const [data] = await getOpenableChanges(props.contractId);
  if (opening.value) opening.value.changes = data || [];
}
async function open() {
  busy.value = true;
  const [data, error] = await openPlan(props.contractId, opening.value?.changeRequestId || null);
  busy.value = false;
  if (error) return fail(error);
  ok("طرح منابع (پیش‌نویس خالی) باز شد.");
  opening.value = null;
  plan.value = data;
  noPlan.value = false;
  versionSel.value = null;
  loadVersions();
  loadBudget();
}
async function discard() {
  if (!plan.value || !discarding.value) return;
  busy.value = true;
  const [, error] = await discardPlan(plan.value.id, discarding.value.reason.trim() || null);
  busy.value = false;
  if (error) return fail(error);
  ok("طرح باز دور انداخته شد.");
  discarding.value = null;
  versionSel.value = null;
  reloadAll();
}

// ---- the activity table
interface Row {
  id: number;
  code: string;
  title: string;
  unit: string | null;
  lines: PlanAssignment[];
  marked: boolean;
}
const rows = computed<Row[]>(() => {
  const p = plan.value;
  if (!p) return [];
  const byId = new Map<number, Row>();
  const marked = new Set(p.markedUnassignedActivityIds);
  for (const a of activities.value) if (a.durationDays > 0 || marked.has(a.id)) byId.set(a.id, { id: a.id, code: a.code, title: a.title, unit: a.responsibleUnit, lines: [], marked: marked.has(a.id) });
  for (const l of p.assignments) {
    let r = byId.get(l.activityId);
    if (!r) byId.set(l.activityId, (r = { id: l.activityId, code: l.activityCode, title: l.activityTitle, unit: null, lines: [], marked: marked.has(l.activityId) }));
    r.lines.push(l);
  }
  for (const id of marked) if (!byId.has(id)) byId.set(id, { id, code: "#" + id, title: "", unit: null, lines: [], marked: true });
  return [...byId.values()].sort((a, b) => a.code.localeCompare(b.code, "en", { numeric: true }));
});
const needs = (r: Row) => !r.lines.length && !r.marked;
const shown = computed(() => rows.value.filter((r) => (filter.value === "need" ? needs(r) : filter.value === "done" ? !needs(r) : true)));
const agentLines = computed(() => (plan.value?.assignments || []).filter((l) => l.source === 1).length);

// ---- inline add / edit of the lines of an activity
const adding = ref<{ activityId: number; resourceId: number | null; quantity: number; distribution: number } | null>(null);
const editing = ref<{ id: number; quantity: number; distribution: number } | null>(null);
function startAdd(r: Row) {
  editing.value = null;
  adding.value = { activityId: r.id, resourceId: null, quantity: 1, distribution: 1 };
}
const optionsFor = (r: Row) => resources.value.filter((x) => !r.lines.some((l) => l.resourceId === x.id));
const addUnit = computed(() => unitText(resources.value.find((x) => x.id === adding.value?.resourceId)?.unit || 0));
async function saveAdd(r: Row) {
  const a = adding.value;
  if (!a || !a.resourceId) return fail("منبع را انتخاب کنید.");
  if (!(a.quantity > 0)) return fail("مقدار منبع باید بیشتر از صفر باشد.");
  busy.value = true;
  const result = await saveAssignments(plan.value!.id, a.activityId, [{ resourceId: a.resourceId, quantity: a.quantity, distribution: a.distribution, responsibleRole: r.unit }]);
  busy.value = false;
  if (take(result, "منبع به فعالیت اضافه شد.")) adding.value = null;
}
function startEdit(l: PlanAssignment) {
  adding.value = null;
  editing.value = { id: l.id, quantity: l.quantity, distribution: l.distribution };
}
async function saveEdit(l: PlanAssignment) {
  const e = editing.value;
  if (!e) return;
  if (!(e.quantity > 0)) return fail("مقدار منبع باید بیشتر از صفر باشد.");
  busy.value = true;
  const result = await saveAssignments(plan.value!.id, l.activityId, [{ resourceId: l.resourceId, quantity: e.quantity, distribution: e.distribution, responsibleRole: l.responsibleRole }]);
  busy.value = false;
  if (take(result, "اصلاح ذخیره شد.")) editing.value = null;
}
async function removeLine(l: PlanAssignment) {
  if (!confirm(`منبع «${l.resourceCode}» از فعالیت «${l.activityCode}» برداشته شود؟`)) return;
  busy.value = true;
  const result = await deleteAssignment(l.id);
  busy.value = false;
  take(result, "منبع برداشته شد.");
}
async function mark(ids: number[] | null, unmark = false) {
  busy.value = true;
  const result = await markUnassigned(plan.value!.id, ids ? { activityIds: ids, unmark } : { allRemaining: true });
  busy.value = false;
  take(result, unmark ? "علامت «بدون منبع» برداشته شد." : "فعالیت «بدون منبع» علامت خورد.");
}
async function markAll() {
  if (!confirm(`همه ${plan.value!.unassignedCount} فعالیتِ بدون منبع، «بدون منبع» علامت بخورند؟`)) return;
  mark(null);
}

// ---- actions of the plan
/** A refused action (a stale version, a state that changed) is shown by the server's own words; the plan is read again so the page shows what the server holds. */
function reloadAfterRefusal() {
  loadPlan();
  loadVersions();
}
async function submit() {
  busy.value = true;
  const result = await planAction(plan.value!, "submit");
  busy.value = false;
  if (take(result, "طرح برای تایید مدیر پروژه ارسال شد.")) loadVersions();
  else reloadAfterRefusal();
}
async function approve() {
  if (!confirm("طرح منابع تایید شود؟")) return;
  busy.value = true;
  const result = await planAction(plan.value!, "approve");
  busy.value = false;
  if (take(result, "طرح منابع تایید شد.")) loadVersions();
  else reloadAfterRefusal();
}
async function giveBack(v: Record<string, any>) {
  busy.value = true;
  const result = await planAction(plan.value!, "return", String(v.note || "").trim());
  busy.value = false;
  if (take(result, "طرح با علت برگشت به برنامه‌ریز برگشت.")) {
    returning.value = false;
    loadVersions();
  } else {
    returning.value = false;
    reloadAfterRefusal();
  }
}

const classTitle = (c: number) => resourceClasses[c] || "";
const sourceBadge = (s: number) => (s === 1 ? "info" : s === 2 ? "ok" : "");

onMounted(reloadAll);
watch(() => props.contractId, () => { versionSel.value = null; adding.value = null; editing.value = null; reloadAll(); });
watch(versionSel, () => { adding.value = null; editing.value = null; loadPlan(); });
watch(asOf, loadBudget);
</script>

<template>
  <div>
    <div v-if="loading && !plan" class="inv-muted">در حال خواندن...</div>

    <div v-else-if="noPlan" class="border rounded p-3">
      <p class="mb-2">{{ ResourceTexts.noPlan }}</p>
      <p class="inv-muted small">ایجنت برنامه‌ریزی پیش‌نویس طرح را از روی فعالیت‌ها و منابع تعریف‌شده می‌سازد و برای بازبینی شما می‌گذارد. اگر می‌خواهید دستی شروع کنید، یک پیش‌نویس خالی باز کنید.</p>
      <button v-if="canWrite && !opening" :disabled="busy" class="btn btn-sm btn-primary" @click="startOpen">باز کردن طرح منابع (پیش‌نویس خالی)</button>
      <div v-if="opening" class="border rounded p-2 mt-2">
        <label class="form-label small mb-0">درخواست تغییر (فقط پس از تایید خط مبنا لازم است)</label>
        <select v-model="opening.changeRequestId" class="form-select form-select-sm mb-1" style="max-width: 420px">
          <option :value="null">بدون درخواست تغییر / اولین درخواست مجاز</option>
          <option v-for="c in opening.changes" :key="c.id" :value="c.id">{{ c.number }} {{ c.description.slice(0, 60) }}</option>
        </select>
        <p class="small inv-muted mb-2">پس از خط مبنا، طرح فقط با درخواست تغییرِ «تاییدشده و در حال اجرا» که خط مبنای خودش را ساخته باز می‌شود؛ اگر درخواست مناسب نباشد، سرور علت را می‌گوید.</p>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="open">باز کردن</button>
        <button class="btn btn-sm btn-outline-secondary ms-1" @click="opening = null">انصراف</button>
      </div>
    </div>

    <template v-else-if="plan">
      <!-- header: version, status, who prepared and who decides -->
      <div class="border rounded p-3 mb-3">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
          <b>طرح منابع {{ plan.contractCode }}</b>
          <select v-model.number="versionSel" class="form-select form-select-sm" style="max-width: 230px" title="نسخه‌های طرح">
            <option :value="null">نسخه جاری ({{ qty(plan.version) }})</option>
            <option v-for="v in versions" :key="v.id" :value="v.version">نسخه {{ qty(v.version) }} - {{ planStatuses[v.status] }}</option>
          </select>
          <span class="inv-badge" :class="planStatusBadge(plan.status)">{{ planStatuses[plan.status] }}</span>
          <span v-if="plan.preparedByAgent" class="inv-badge info">{{ PREPARED_BY_AGENT }}</span>
          <router-link v-if="plan.changeRequestId" :to="{ path: '/project-control/changes', query: { id: String(plan.changeRequestId) } }" class="small">درخواست تغییر #{{ qty(plan.changeRequestId) }}</router-link>
        </div>
        <div class="row g-2 small">
          <div class="col-md-4"><span class="inv-muted">آخرین ویرایش / سازنده:</span> {{ plan.makerName || "—" }}<span v-if="plan.preparedByAgent" class="inv-muted"> (پیش‌نویس اولیه از ایجنت)</span></div>
          <div class="col-md-4"><span class="inv-muted">تاییدکننده:</span> <template v-if="plan.approvedByName">{{ plan.approvedByName }} <span class="inv-muted">{{ faDateTime(plan.approvedAt) }}</span></template><template v-else>—</template></div>
          <div class="col-md-4"><span class="inv-muted">تصمیم‌گیرنده:</span> {{ plan.deciderName || (plan.deciderUserId === null ? "معاون PMO" : "—") }}</div>
          <div class="col-md-4"><span class="inv-muted">ارسال:</span> {{ faDateTime(plan.submittedAt) }}</div>
        </div>
        <div v-if="plan.status === 3 && plan.returnReason" class="alert alert-warning py-2 mt-2 mb-0"><b>علت برگشت</b> ({{ faDateTime(plan.returnedAt) }}): {{ plan.returnReason }}</div>
        <div v-if="!isLatest" class="alert alert-secondary py-2 mt-2 mb-0">{{ ResourceTexts.oldVersion }}</div>
        <div v-else-if="plan.preparedByAgent && plan.status === 0" class="alert alert-info py-2 mt-2 mb-0">
          این پیش‌نویس را ایجنت برنامه‌ریزی ساخته است ({{ qty(agentLines) }} ردیف از ایجنت). ایجنت طرح را ارسال یا تایید نمی‌کند: بازبینی، اصلاح و ارسال با برنامه‌ریز و تایید با مدیر پروژه است.
        </div>
      </div>

      <!-- counter and toolbar -->
      <div class="d-flex flex-wrap gap-2 align-items-center mb-2 d-print-none">
        <span class="inv-badge" :class="plan.unassignedCount ? 'warn' : 'ok'">فعالیت بدون منبع: {{ qty(plan.unassignedCount) }} از {{ qty(plan.activitiesNeedingResource) }}</span>
        <select v-if="canReadActivities" v-model="filter" class="form-select form-select-sm" style="max-width: 190px">
          <option value="all">همه فعالیت‌ها</option>
          <option value="need">فقط بدون منبع</option>
          <option value="done">دارای منبع یا «بدون منبع»</option>
        </select>
        <button v-if="editable && canWrite" class="btn btn-sm btn-outline-primary" @click="importing = !importing">ورود از MS Project</button>
        <button class="btn btn-sm btn-outline-secondary" @click="run('plan', { plan, budget })">چاپ برنامه منابع پروژه</button>
        <button v-if="editable && plan.unassignedCount" :disabled="busy" class="btn btn-sm btn-outline-secondary ms-auto" @click="markAll">همه بدون‌منبع‌ها را «بدون منبع» علامت بزن</button>
      </div>

      <MspImport v-if="importing && editable" :contract-id="contractId" @done="imported" @close="importing = false" />

      <div class="table-responsive">
        <table class="table table-sm align-middle">
          <thead><tr><th>فعالیت</th><th>منابع</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!shown.length"><td colspan="4" class="text-center inv-muted">فعالیتی برای نمایش نیست.</td></tr>
            <template v-for="r in shown" :key="r.id">
              <tr :class="{ 'table-warning': needs(r) }">
                <td style="min-width: 200px">
                  <span class="inv-ltr">{{ r.code }}</span> {{ r.title }}
                  <div v-if="r.unit" class="small inv-muted">{{ unitTitle(r.unit) }}</div>
                </td>
                <td style="min-width: 320px">
                  <div v-if="!r.lines.length" class="inv-muted small">—</div>
                  <div v-for="l in r.lines" :key="l.id" class="d-flex flex-wrap gap-2 align-items-center py-1 border-bottom">
                    <template v-if="editing && editing.id === l.id">
                      <span><span class="inv-ltr">{{ l.resourceCode }}</span> {{ l.resourceTitle }}</span>
                      <input v-model.number="editing.quantity" type="number" min="0" step="any" class="form-control form-control-sm" style="width: 100px" />
                      <span class="small">{{ unitText(l.resourceUnit) }}</span>
                      <select v-model.number="editing.distribution" class="form-select form-select-sm" style="width: 140px"><option v-for="(n, k) in distributions" :key="k" :value="Number(k)">{{ n }}</option></select>
                      <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveEdit(l)">ذخیره</button>
                      <button class="btn btn-sm btn-outline-secondary" @click="editing = null">انصراف</button>
                    </template>
                    <template v-else>
                      <span><span class="inv-ltr">{{ l.resourceCode }}</span> {{ l.resourceTitle }} <span class="small inv-muted">({{ resourceKinds[l.resourceKind] }})</span></span>
                      <b>{{ qty(l.quantity) }}</b><span class="small">{{ unitText(l.resourceUnit) }}</span>
                      <span class="small inv-muted">{{ distributions[l.distribution] }}</span>
                      <span class="inv-badge" :class="sourceBadge(l.source)" :title="l.source === 1 ? PREPARED_BY_AGENT : ''">{{ assignmentSources[l.source] }}</span>
                      <template v-if="editable">
                        <button class="btn btn-sm btn-outline-secondary py-0" @click="startEdit(l)">ویرایش</button>
                        <button :disabled="busy" class="btn btn-sm btn-outline-danger py-0" @click="removeLine(l)">حذف</button>
                      </template>
                    </template>
                  </div>
                </td>
                <td>
                  <span v-if="r.marked" class="inv-badge">بدون منبع</span>
                  <span v-else-if="r.lines.length" class="inv-badge ok">دارای منبع</span>
                  <span v-else class="inv-badge warn">منبع ندارد</span>
                </td>
                <td class="d-print-none text-nowrap">
                  <template v-if="editable">
                    <button v-if="!r.marked" class="btn btn-sm btn-outline-primary" @click="startAdd(r)">افزودن منبع</button>
                    <button v-if="needs(r)" :disabled="busy" class="btn btn-sm btn-outline-secondary ms-1" @click="mark([r.id])">بدون منبع</button>
                    <button v-if="r.marked" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="mark([r.id], true)">برداشتن علامت</button>
                  </template>
                </td>
              </tr>
              <tr v-if="adding && adding.activityId === r.id" class="table-light">
                <td colspan="4">
                  <div class="d-flex flex-wrap gap-2 align-items-end">
                    <div><label class="form-label small mb-0">منبع</label>
                      <select v-model="adding.resourceId" class="form-select form-select-sm" style="min-width: 240px"><option :value="null">انتخاب کنید</option><option v-for="x in optionsFor(r)" :key="x.id" :value="x.id">{{ resourceLabel(x) }}</option></select></div>
                    <div><label class="form-label small mb-0">مقدار ({{ addUnit || "واحد منبع" }})</label><input v-model.number="adding.quantity" type="number" min="0" step="any" class="form-control form-control-sm" style="width: 120px" /></div>
                    <div><label class="form-label small mb-0">توزیع</label><select v-model.number="adding.distribution" class="form-select form-select-sm"><option v-for="(n, k) in distributions" :key="k" :value="Number(k)">{{ n }}</option></select></div>
                    <button :disabled="busy" class="btn btn-sm btn-primary" @click="saveAdd(r)">افزودن</button>
                    <button class="btn btn-sm btn-outline-secondary" @click="adding = null">انصراف</button>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <p v-if="!canReadActivities" class="inv-muted small">فهرست کامل فعالیت‌ها برای نقش شما نمایش داده نمی‌شود؛ فقط فعالیت‌هایی که منبع دارند دیده می‌شوند.</p>

      <!-- actions -->
      <div class="d-flex flex-wrap gap-2 align-items-center my-3 d-print-none">
        <template v-if="submitGate.show">
          <button :disabled="busy || !!submitGate.reason" :title="submitGate.reason || ''" class="btn btn-sm btn-primary" @click="submit">ارسال برای تایید مدیر پروژه</button>
          <span v-if="submitGate.reason" class="small text-danger">{{ submitGate.reason }}</span>
        </template>
        <template v-if="decideGate.show">
          <button :disabled="busy || !!decideGate.reason" :title="decideGate.reason || ''" class="btn btn-sm btn-success" @click="approve">تایید طرح</button>
          <button :disabled="busy || !!decideGate.reason" :title="decideGate.reason || ''" class="btn btn-sm btn-outline-danger" @click="returning = true">برگشت با علت</button>
          <span v-if="decideGate.reason" class="small text-danger">{{ decideGate.reason }}</span>
        </template>
        <template v-if="discardGate.show && !discarding">
          <button :disabled="busy || !!discardGate.reason" :title="discardGate.reason || ''" class="btn btn-sm btn-outline-danger ms-auto" @click="discarding = { reason: '' }">حذف طرح باز</button>
          <span v-if="discardGate.reason" class="small text-danger">{{ discardGate.reason }}</span>
        </template>
      </div>
      <div v-if="discarding" class="border border-danger rounded p-3 mb-3 d-print-none">
        <b>حذف طرح باز</b>
        <p class="small mb-2">این نسخه از طرح (با همه منابع ثبت‌شده در آن) دور انداخته می‌شود. فقط پیش از ساخت خط مبنا و برای طرحی که با درخواست تغییر باز نشده ممکن است.</p>
        <input v-model="discarding.reason" maxlength="900" class="form-control form-control-sm mb-2" style="max-width: 420px" placeholder="علت (اختیاری)" />
        <button :disabled="busy" class="btn btn-sm btn-danger" @click="discard">دور انداختن طرح</button>
        <button class="btn btn-sm btn-outline-secondary ms-1" @click="discarding = null">انصراف</button>
      </div>

      <!-- budget -->
      <div class="border rounded p-3">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
          <b>بودجه منابع طرح</b>
          <span class="small inv-muted">مقدار × نرخ مصوب در تاریخ</span>
          <div style="max-width: 200px"><JDate v-model="asOf" /></div>
          <button v-if="asOf" class="btn btn-sm btn-outline-secondary" @click="asOf = null">امروز</button>
        </div>
        <template v-if="budget">
          <div class="row g-2 mb-2">
            <div class="col-md-4"><div class="border rounded p-2"><div class="inv-muted small">جمع (ریال)</div><b>{{ money(budget.total) }}</b></div></div>
            <div class="col-md-4"><div class="border rounded p-2"><div class="inv-muted small">ردیف بدون نرخ مصوب (در جمع نیست)</div><b :class="budget.noRateCount ? 'text-danger' : ''">{{ qty(budget.noRateCount) }}</b></div></div>
            <div class="col-md-4"><div class="border rounded p-2"><div class="inv-muted small">ردیف {{ CONFIDENTIAL }} (در جمع نیست)</div><b>{{ qty(budget.hiddenLineCount) }}</b></div></div>
          </div>
          <p v-if="budget.rateHidden || budget.hiddenLineCount" class="small inv-muted">نرخ و مبلغ ردیف‌هایی که هزینه یک نفر را نشان می‌دهد برای نقش شما {{ CONFIDENTIAL }} است و در جمع نمی‌آید.</p>
          <div class="row g-3">
            <div class="col-lg-5">
              <b class="small">به تفکیک گروه منبع</b>
              <div class="table-responsive"><table class="table table-sm">
                <thead><tr><th>گروه</th><th>ردیف</th><th>مبلغ (ریال)</th></tr></thead>
                <tbody>
                  <tr v-if="!budget.byClass.length"><td colspan="3" class="text-center inv-muted">—</td></tr>
                  <tr v-for="g in budget.byClass" :key="g.resourceClass"><td>{{ classTitle(g.resourceClass) }}</td><td>{{ qty(g.lineCount) }}</td><td>{{ money(g.amount) }}</td></tr>
                </tbody>
              </table></div>
            </div>
            <div class="col-lg-7">
              <b class="small">به تفکیک منبع</b>
              <div class="table-responsive"><table class="table table-sm">
                <thead><tr><th>منبع</th><th>مقدار</th><th>ردیف</th><th>مبلغ (ریال)</th></tr></thead>
                <tbody>
                  <tr v-if="!budget.byResource.length"><td colspan="4" class="text-center inv-muted">—</td></tr>
                  <tr v-for="g in budget.byResource" :key="g.resourceId || g.resourceCode || ''"><td><span class="inv-ltr">{{ g.resourceCode }}</span> {{ g.resourceTitle }}</td><td>{{ qty(g.quantity) }}</td><td>{{ qty(g.lineCount) }}</td><td>{{ money(g.amount) }}</td></tr>
                </tbody>
              </table></div>
            </div>
          </div>
          <details class="mt-2">
            <summary class="small">ردیف‌های بودجه ({{ qty(budget.lines.length) }})</summary>
            <div class="table-responsive"><table class="table table-sm">
              <thead><tr><th>فعالیت</th><th>منبع</th><th>مقدار</th><th>نرخ</th><th>مبلغ</th></tr></thead>
              <tbody>
                <tr v-for="l in budget.lines" :key="l.assignmentId">
                  <td class="inv-ltr">{{ l.activityCode }}</td><td><span class="inv-ltr">{{ l.resourceCode }}</span> {{ l.resourceTitle }}</td><td>{{ qty(l.quantity) }}</td>
                  <template v-if="l.rateHidden"><td class="inv-muted">{{ CONFIDENTIAL }}</td><td class="inv-muted">{{ CONFIDENTIAL }}</td></template>
                  <template v-else-if="l.noApprovedRate"><td colspan="2" class="text-danger">{{ NO_APPROVED_RATE }}</td></template>
                  <template v-else><td>{{ money(l.rate) }}</td><td>{{ money(l.amount) }}</td></template>
                </tr>
              </tbody>
            </table></div>
          </details>
        </template>
        <div v-else class="inv-muted small">بودجه خوانده نشد.</div>
      </div>

      <ResourcePlanPrint v-if="job?.kind === 'plan'" :plan="job.data.plan" :budget="job.data.budget" />
      <PromptModal v-if="returning" title="برگشت طرح منابع" :fields="[{ key: 'note', label: 'علت برگشت طرح', type: 'textarea', required: true, maxlength: 1000 }]" confirm-text="برگشت" @confirm="giveBack" @cancel="returning = false" />
    </template>
  </div>
</template>
