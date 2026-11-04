<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { getActivities } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { assignTask, getTask, getTaskReferrals, saveTask, taskAction, taskChecklist, taskComment, taskProgress, transferTask, watchTask, withdrawTask } from "@/api/TasksAPIs";
import JDate from "@/components/pc/JDate.vue";
import PositionSelect from "@/components/common/PositionSelect.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import ReferralNode from "@/components/tasks/ReferralNode";
import TaskAttachments from "@/components/tasks/TaskAttachments.vue";
import { fail, ok } from "@/composables/inventory";
import { contractsCache } from "@/composables/projectControl";
import { actionTexts, assigneeRoles, assigneeStates, classifications, historyKinds, importances, priorities, recurrences, taskKinds, taskStatusClass, taskStatuses } from "@/composables/tasks";

// The task drawer: info, checklist, comments, attachments, links, history (with the referral tree) and reminders.
// Every rule (who may do what) is decided by the API; the buttons follow task.allowedActions.
const props = defineProps({ taskId: { type: Number, default: null }, preset: { type: Object, default: () => ({}) } });
const emit = defineEmits(["close", "changed"]);
const task = ref<any>(null);
const tab = ref("info");
const busy = ref(false);
const edit = ref(false);
const form = reactive<any>({});
const referrals = ref<any[]>([]);
const acts = ref<any[]>([]);
const note = ref("");
const newCheck = ref("");
const newComment = ref("");
const replyTo = ref<number | null>(null);
const progress = reactive<any>({ percent: 0, actualHours: null, actualCost: null, note: "" });
const assign = reactive<any>({ userId: null, positionId: null, role: 1, reason: "" });
const move = reactive<any>({ assigneeId: null, userId: null, positionId: null, reason: "" });

const tabs = [["info", "اطلاعات"], ["checklist", "چک‌لیست"], ["comments", "یادداشت‌ها"], ["attach", "پیوست‌ها"], ["links", "پیوندها"], ["history", "تاریخچه و ارجاع"], ["remind", "یادآوری‌ها"]];
const can = (a: string) => !!task.value?.allowedActions?.includes(a);
const isNew = computed(() => !props.taskId);

function fillForm(t: any) {
  Object.assign(form, {
    id: t?.id ?? null, title: t?.title ?? "", description: t?.description ?? "", kind: t?.kind ?? 1, category: t?.category ?? "", priority: t?.priority ?? 2, importance: t?.importance ?? 2,
    confidentiality: t?.confidentiality ?? 0, startDate: t?.startDate?.slice(0, 10) ?? null, dueDate: t?.dueDate?.slice(0, 10) ?? null, estimatedHours: t?.estimatedHours ?? 0, estimatedCost: t?.estimatedCost ?? 0,
    recurrence: t?.recurrence ?? 0, recurrenceInterval: t?.recurrenceInterval ?? 1, recurrenceEnd: t?.recurrenceEnd?.slice(0, 10) ?? null, reminderDays: t?.reminderDays ?? "",
    contractId: t?.contractId ?? null, activityId: t?.activityId ?? null, wbsNodeId: t?.wbsNodeId ?? null, parentTaskId: t?.parentTaskId ?? null, decisionId: t?.decisionId ?? null,
    letterId: t?.letterId ?? null, requestId: t?.requestId ?? null, riskId: t?.riskId ?? null, issueId: t?.issueId ?? null,
    requestType: t?.requestType ?? "", requester: t?.requester ?? "", referringOrganisation: t?.referringOrganisation ?? "",
    assignees: [], checklist: [],
  });
}
async function loadActivities() {
  acts.value = [];
  if (!form.contractId) return;
  const [d] = await getActivities({ ContractId: form.contractId });
  acts.value = rowsOf(d);
}
async function load() {
  if (!props.taskId) {
    fillForm(props.preset);
    Object.assign(form, props.preset);
    edit.value = true;
    await loadActivities();
    return;
  }
  const [d, e] = await getTask(props.taskId);
  if (e) { fail(e); return emit("close"); }
  task.value = d;
  progress.percent = d.percentComplete;
  fillForm(d);
  edit.value = false;
  const [r] = await getTaskReferrals(props.taskId);
  referrals.value = Array.isArray(r) ? r : [];
  await loadActivities();
}
async function run<T>(fn: () => Promise<[T, any]>, done?: string) {
  busy.value = true;
  const [data, error] = await fn();
  busy.value = false;
  if (error) { fail(error); return null; }
  if (done) ok(done);
  emit("changed");
  await load();
  return data;
}
async function save() {
  if (!form.title.trim()) return fail("عنوان وظیفه را بنویسید.");
  busy.value = true;
  const dto = { ...form, assignees: isNew.value ? form.assignees : [], checklist: isNew.value ? form.checklist : [] };
  const [d, e] = await saveTask(dto);
  busy.value = false;
  if (e) return fail(e);
  ok("وظیفه ذخیره شد.");
  emit("changed");
  if (isNew.value) { emit("close"); return; }
  task.value = d;
  await load();
}
const act = (a: string) => {
  if (["return", "reject", "suspend"].includes(a) && !note.value.trim()) return fail("برای این اقدام دلیل را بنویسید.");
  return run(() => taskAction({ id: task.value.id, action: a, note: note.value }), "انجام شد.").then(() => (note.value = ""));
};
const savePercent = () => run(() => taskProgress({ taskId: task.value.id, percent: progress.percent, actualHours: progress.actualHours, actualCost: progress.actualCost, note: progress.note }), "پیشرفت ثبت شد.");
const addCheck = () => { if (!newCheck.value.trim()) return; run(() => taskChecklist({ taskId: task.value.id, action: "add", text: newCheck.value })).then(() => (newCheck.value = "")); };
const toggleCheck = (i: any) => run(() => taskChecklist({ taskId: task.value.id, action: "toggle", itemId: i.id }));
const removeCheck = (i: any) => run(() => taskChecklist({ taskId: task.value.id, action: "remove", itemId: i.id }));
const addComment = () => { if (!newComment.value.trim()) return; run(() => taskComment({ taskId: task.value.id, parentId: replyTo.value, text: newComment.value })).then(() => { newComment.value = ""; replyTo.value = null; }); };
const doAssign = () => {
  if (!assign.userId && !assign.positionId) return fail("شخص یا سمت را انتخاب کنید.");
  run(() => assignTask({ taskId: task.value.id, targets: [{ userId: assign.userId, positionId: assign.positionId, role: assign.role, reason: assign.reason }] }), "ارجاع شد.").then(() => Object.assign(assign, { userId: null, positionId: null, reason: "" }));
};
const doTransfer = () => {
  if (!move.assigneeId || (!move.userId && !move.positionId) || !move.reason.trim()) return fail("ارجاع، مقصد و دلیل انتقال را مشخص کنید.");
  run(() => transferTask({ taskId: task.value.id, assigneeId: move.assigneeId, toUserId: move.userId, toPositionId: move.positionId, reason: move.reason }), "منتقل شد.").then(() => Object.assign(move, { assigneeId: null, userId: null, positionId: null, reason: "" }));
};
const doWithdraw = (a: any) => {
  const reason = window.prompt("دلیل پس‌گرفتن ارجاع:");
  if (reason) run(() => withdrawTask({ taskId: task.value.id, assigneeId: a.id, reason }), "ارجاع پس گرفته شد.");
};
const toggleWatch = () => run(() => watchTask(task.value.id, !task.value.watching));
const activeAssignees = computed(() => (task.value?.assignees || []).filter((a: any) => a.state === 0));
const reminderList = computed(() => (task.value?.history || []).filter((h: any) => h.kind === 11));
onMounted(load);
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal wide">
      <div class="d-flex justify-content-between align-items-start mb-2">
        <div>
          <h5 class="mb-1">{{ isNew ? "وظیفه جدید" : task ? task.number + " - " + task.title : "..." }}</h5>
          <span v-if="task" :class="taskStatusClass[task.status]" class="inv-badge">{{ taskStatuses[task.status] }}</span>
          <span v-if="task?.overdue" class="inv-badge bad ms-1">معوق</span>
        </div>
        <div class="d-flex gap-2"><button v-if="task" class="btn btn-sm btn-outline-secondary" @click="toggleWatch">{{ task.watching ? "لغو پیگیری" : "پیگیری" }}</button><button class="btn btn-sm btn-light" @click="emit('close')">بستن</button></div>
      </div>

      <ul v-if="!isNew" class="nav nav-tabs mb-3"><li v-for="t in tabs" :key="t[0]" class="nav-item"><a :class="{ active: tab === t[0] }" class="nav-link" href="#" @click.prevent="tab = t[0]">{{ t[1] }}</a></li></ul>

      <!-- info -->
      <div v-if="tab === 'info'">
        <div v-if="!edit && task">
          <table class="table table-sm">
            <tbody>
              <tr><th style="width: 160px">نوع</th><td>{{ taskKinds[task.kind] }} <span v-if="task.category">/ {{ task.category }}</span></td><th style="width: 140px">اولویت</th><td>{{ priorities[task.priority] }} (اهمیت {{ importances[task.importance] }})</td></tr>
              <tr><th>سازنده</th><td>{{ task.creatorName }} - {{ task.createdAtText }}</td><th>محرمانگی</th><td>{{ classifications[task.confidentiality] }}</td></tr>
              <tr><th>شروع</th><td>{{ task.startDateText }}</td><th>سررسید</th><td>{{ task.dueDateText }}</td></tr>
              <tr><th>ساعت برآوردی / واقعی</th><td>{{ task.estimatedHours }} / {{ task.actualHours }}</td><th>هزینه برآوردی / واقعی</th><td>{{ task.estimatedCost }} / {{ task.actualCost }}</td></tr>
              <tr><th>پیشرفت</th><td colspan="3"><div class="progress" style="height: 14px"><div class="progress-bar" :style="{ width: task.percentComplete + '%' }">{{ task.percentComplete }}٪</div></div></td></tr>
              <tr v-if="task.description"><th>شرح</th><td colspan="3" style="white-space: pre-wrap">{{ task.description }}</td></tr>
              <tr v-if="task.statusReason"><th>دلیل وضعیت</th><td colspan="3">{{ task.statusReason }}</td></tr>
            </tbody>
          </table>
          <h6>ارجاع‌های فعال</h6>
          <table class="table table-sm">
            <thead><tr><th>گیرنده</th><th>نقش</th><th>ارجاع‌دهنده</th><th>تاریخ</th><th>علت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!activeAssignees.length"><td colspan="6" class="text-center inv-muted">ارجاع فعالی نیست.</td></tr>
              <tr v-for="a in activeAssignees" :key="a.id"><td>{{ a.userName || a.positionTitle }}</td><td>{{ assigneeRoles[a.role] }}</td><td>{{ a.assignedByName }}</td><td>{{ a.assignedAtText }}</td><td>{{ a.reason }}</td><td><button v-if="can('assign')" class="btn btn-sm btn-outline-danger" @click="doWithdraw(a)">پس‌گرفتن</button></td></tr>
            </tbody>
          </table>

          <div v-if="can('progress')" class="border rounded p-2 mb-2">
            <div class="row g-2 align-items-end">
              <div class="col-md-2"><label class="form-label small mb-0">درصد پیشرفت</label><input v-model.number="progress.percent" :disabled="!!task.activityId" class="form-control form-control-sm" type="number" min="0" max="100" /></div>
              <div class="col-md-2"><label class="form-label small mb-0">ساعت واقعی</label><input v-model.number="progress.actualHours" class="form-control form-control-sm" type="number" min="0" /></div>
              <div class="col-md-2"><label class="form-label small mb-0">هزینه واقعی</label><input v-model.number="progress.actualCost" class="form-control form-control-sm" type="number" min="0" /></div>
              <div class="col-md-4"><label class="form-label small mb-0">توضیح</label><input v-model="progress.note" class="form-control form-control-sm" /></div>
              <div class="col-md-2"><button :disabled="busy" class="btn btn-sm btn-primary w-100" @click="savePercent">ثبت پیشرفت</button></div>
            </div>
            <p v-if="task.activityId" class="inv-muted small mb-0 mt-1">درصد وظیفه‌ای که به فعالیت وصل است از «کارتابل تایید پیشرفت» ثبت و تایید می‌شود؛ اینجا فقط ساعت و هزینه ثبت می‌شود.</p>
          </div>

          <div v-if="can('assign')" class="border rounded p-2 mb-2">
            <h6>ارجاع به شخص یا سمت</h6>
            <div class="row g-2 align-items-end">
              <div class="col-md-3"><label class="form-label small mb-0">شخص</label><UserSelect v-model="assign.userId" /></div>
              <div class="col-md-3"><label class="form-label small mb-0">یا سمت</label><PositionSelect v-model="assign.positionId" /></div>
              <div class="col-md-2"><label class="form-label small mb-0">نقش</label><select v-model.number="assign.role" class="form-select form-select-sm"><option v-for="(t, k) in assigneeRoles" :key="k" :value="Number(k)">{{ t }}</option></select></div>
              <div class="col-md-3"><label class="form-label small mb-0">علت ارجاع</label><input v-model="assign.reason" class="form-control form-control-sm" /></div>
              <div class="col-md-1"><button :disabled="busy" class="btn btn-sm btn-primary" @click="doAssign">ارجاع</button></div>
            </div>
          </div>
          <div v-if="can('transfer') && activeAssignees.length" class="border rounded p-2 mb-2">
            <h6>انتقال به کارتابل دیگر (با دلیل)</h6>
            <div class="row g-2 align-items-end">
              <div class="col-md-3"><label class="form-label small mb-0">ارجاع فعلی</label><select v-model.number="move.assigneeId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in activeAssignees" :key="a.id" :value="a.id">{{ a.userName || a.positionTitle }}</option></select></div>
              <div class="col-md-3"><label class="form-label small mb-0">به شخص</label><UserSelect v-model="move.userId" /></div>
              <div class="col-md-3"><label class="form-label small mb-0">یا سمت</label><PositionSelect v-model="move.positionId" /></div>
              <div class="col-md-2"><label class="form-label small mb-0">دلیل</label><input v-model="move.reason" class="form-control form-control-sm" /></div>
              <div class="col-md-1"><button :disabled="busy" class="btn btn-sm btn-warning" @click="doTransfer">انتقال</button></div>
            </div>
          </div>

          <div class="d-flex flex-wrap gap-2 align-items-center d-print-none">
            <input v-model="note" class="form-control form-control-sm" style="max-width: 320px" placeholder="توضیح یا دلیل اقدام" />
            <button v-for="a in ['start', 'complete', 'approve', 'return', 'reject', 'suspend', 'resume', 'reopen']" v-show="can(a)" :key="a" :disabled="busy" :class="a === 'approve' ? 'btn-success' : a === 'reject' || a === 'return' ? 'btn-outline-danger' : 'btn-outline-primary'" class="btn btn-sm" @click="act(a)">{{ actionTexts[a] }}</button>
            <button v-if="can('edit')" class="btn btn-sm btn-secondary" @click="edit = true">ویرایش</button>
          </div>
        </div>

        <div v-else>
          <div class="row g-2">
            <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" maxlength="300" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">نوع</label><select v-model.number="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in taskKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-2"><label class="form-label small mb-0">دسته</label><input v-model="form.category" class="form-control form-control-sm" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">اولویت</label><select v-model.number="form.priority" class="form-select form-select-sm"><option v-for="(t, k) in priorities" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-2"><label class="form-label small mb-0">اهمیت</label><select v-model.number="form.importance" class="form-select form-select-sm"><option v-for="(t, k) in importances" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-2"><label class="form-label small mb-0">محرمانگی</label><select v-model.number="form.confidentiality" class="form-select form-select-sm"><option v-for="(t, k) in classifications" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-3"><label class="form-label small mb-0">تاریخ شروع</label><JDate v-model="form.startDate" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">سررسید</label><JDate v-model="form.dueDate" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">ساعت برآوردی</label><input v-model.number="form.estimatedHours" class="form-control form-control-sm" type="number" min="0" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">هزینه برآوردی (ریال)</label><input v-model.number="form.estimatedCost" class="form-control form-control-sm" type="number" min="0" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">تکرار</label><select v-model.number="form.recurrence" class="form-select form-select-sm"><option v-for="(t, k) in recurrences" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div v-if="form.recurrence" class="col-md-2"><label class="form-label small mb-0">هر چند دوره</label><input v-model.number="form.recurrenceInterval" class="form-control form-control-sm" type="number" min="1" /></div>
            <div v-if="form.recurrence" class="col-md-3"><label class="form-label small mb-0">پایان تکرار</label><JDate v-model="form.recurrenceEnd" /></div>
            <div class="col-md-4"><label class="form-label small mb-0">یادآوری (چند روز مانده، مثلا 3,1,0)</label><input v-model="form.reminderDays" class="form-control form-control-sm inv-ltr" placeholder="3,1,0" /></div>
            <div class="col-md-4"><label class="form-label small mb-0">پروژه / قرارداد</label><select v-model.number="form.contractId" class="form-select form-select-sm" @change="form.activityId = null; loadActivities()"><option :value="null">-</option><option v-for="c in contractsCache" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select></div>
            <div class="col-md-4"><label class="form-label small mb-0">فعالیت (WBS)</label><select v-model.number="form.activityId" :disabled="!form.contractId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} - {{ a.title }}</option></select></div>
            <div class="col-12"><label class="form-label small mb-0">شرح</label><textarea v-model="form.description" class="form-control form-control-sm" rows="3"></textarea></div>
          </div>
          <div v-if="isNew" class="border rounded p-2 mt-2">
            <h6>ارجاع اولیه</h6>
            <div v-for="(a, i) in form.assignees" :key="i" class="row g-2 mb-1 align-items-end">
              <div class="col-md-3"><UserSelect v-model="a.userId" /></div><div class="col-md-3"><PositionSelect v-model="a.positionId" placeholder="یا سمت" /></div>
              <div class="col-md-2"><select v-model.number="a.role" class="form-select form-select-sm"><option v-for="(t, k) in assigneeRoles" :key="k" :value="Number(k)">{{ t }}</option></select></div>
              <div class="col-md-3"><input v-model="a.reason" class="form-control form-control-sm" placeholder="علت ارجاع" /></div>
              <div class="col-md-1"><button class="btn btn-sm btn-outline-danger" @click="form.assignees.splice(i, 1)">×</button></div>
            </div>
            <button class="btn btn-sm btn-outline-primary" @click="form.assignees.push({ userId: null, positionId: null, role: 1, reason: '' })">+ گیرنده</button>
            <h6 class="mt-2">چک‌لیست</h6>
            <div v-for="(c, i) in form.checklist" :key="i" class="d-flex gap-2 mb-1"><input v-model="form.checklist[i]" class="form-control form-control-sm" /><button class="btn btn-sm btn-outline-danger" @click="form.checklist.splice(i, 1)">×</button></div>
            <button class="btn btn-sm btn-outline-primary" @click="form.checklist.push('')">+ مورد</button>
          </div>
          <div class="d-flex gap-2 mt-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button><button v-if="!isNew" class="btn btn-sm btn-light" @click="load">انصراف</button></div>
        </div>
      </div>
      <div v-else-if="isNew"></div>

      <!-- checklist -->
      <div v-else-if="tab === 'checklist' && task">
        <p v-if="task.checklistTotal" class="small inv-muted">{{ task.checklistDone }} از {{ task.checklistTotal }} انجام شده</p>
        <div v-for="i in task.checklist" :key="i.id" class="d-flex align-items-center gap-2 mb-1">
          <input :checked="i.done" :disabled="!can('progress') && !can('edit')" class="form-check-input" type="checkbox" @change="toggleCheck(i)" />
          <span :class="{ 'text-decoration-line-through text-muted': i.done }">{{ i.text }}</span><span v-if="i.done" class="small inv-muted">({{ i.doneByName }} {{ i.doneAtText }})</span>
          <button v-if="can('edit')" class="btn btn-sm btn-link text-danger p-0" @click="removeCheck(i)">حذف</button>
        </div>
        <p v-if="!task.checklist.length" class="inv-muted">موردی نیست.</p>
        <div v-if="can('edit') || can('progress')" class="d-flex gap-2 mt-2"><input v-model="newCheck" class="form-control form-control-sm" placeholder="مورد جدید" @keyup.enter="addCheck" /><button class="btn btn-sm btn-primary" @click="addCheck">افزودن</button></div>
      </div>

      <!-- comments -->
      <div v-else-if="tab === 'comments' && task">
        <div v-for="c in task.comments" :key="c.id" :class="c.parentId ? 'me-4' : ''" class="border rounded p-2 mb-2">
          <div class="small inv-muted">{{ c.userName }} - {{ c.atText }}</div>
          <div style="white-space: pre-wrap">{{ c.text }}</div>
          <button class="btn btn-sm btn-link p-0" @click="replyTo = c.id">پاسخ</button>
        </div>
        <p v-if="!task.comments.length" class="inv-muted">یادداشتی نیست.</p>
        <div v-if="can('comment')" class="mt-2">
          <div v-if="replyTo" class="small">پاسخ به یادداشت {{ replyTo }} <a href="#" @click.prevent="replyTo = null">لغو</a></div>
          <textarea v-model="newComment" class="form-control form-control-sm" rows="2" placeholder="یادداشت"></textarea>
          <button :disabled="busy" class="btn btn-sm btn-primary mt-1" @click="addComment">ثبت یادداشت</button>
        </div>
      </div>

      <TaskAttachments v-else-if="tab === 'attach' && task" :task="task" :can-write="can('comment')" @changed="load(); emit('changed')" />

      <!-- links -->
      <div v-else-if="tab === 'links' && task">
        <table class="table table-sm">
          <tbody>
            <tr><th style="width: 160px">قرارداد / پروژه</th><td>{{ task.contractTitle || "-" }}</td></tr>
            <tr><th>فعالیت</th><td>{{ task.activityCode ? task.activityCode + " - " + task.activityTitle : "-" }}</td></tr>
            <tr><th>گره WBS</th><td>{{ task.wbsNodeCode || "-" }}</td></tr>
            <tr><th>وظیفه والد</th><td>{{ task.parentTaskTitle || "-" }}</td></tr>
            <tr v-if="task.decisionId"><th>مصوبه جلسه</th><td>{{ task.decisionId }}</td></tr>
            <tr v-if="task.letterId"><th>نامه</th><td><router-link :to="'/office/letters'">شناسه {{ task.letterId }}</router-link></td></tr>
            <tr v-if="task.requestId"><th>درخواست</th><td>{{ task.requestId }}</td></tr>
            <tr v-if="task.riskId"><th>ریسک</th><td>{{ task.riskId }}</td></tr>
            <tr v-if="task.issueId"><th>مساله</th><td>{{ task.issueId }}</td></tr>
          </tbody>
        </table>
        <h6>وظایف فرعی</h6>
        <p v-if="!task.subTasks.length" class="inv-muted">وظیفه فرعی نیست.</p>
        <ul><li v-for="s in task.subTasks" :key="s.id">{{ s.number }} - {{ s.title }} <span :class="taskStatusClass[s.status]" class="inv-badge">{{ taskStatuses[s.status] }}</span></li></ul>
      </div>

      <!-- history -->
      <div v-else-if="tab === 'history' && task">
        <h6>درخت ارجاع</h6>
        <p v-if="!referrals.length" class="inv-muted">ارجاعی ثبت نشده.</p>
        <component :is="ReferralNode" v-for="r in referrals" :key="r.id" :node="r" />
        <h6 class="mt-3">تاریخچه (غیرقابل ویرایش)</h6>
        <div class="inv-timeline">
          <div v-for="h in task.history" :key="h.id" class="step">
            <div class="small inv-muted">{{ h.atText }} - {{ h.userName }} - <span class="inv-badge">{{ historyKinds[h.kind] }}</span></div>
            <div>{{ h.text }} <span v-if="h.fromValue || h.toValue" class="inv-muted">({{ h.fromValue }} ← {{ h.toValue }})</span></div>
          </div>
        </div>
      </div>

      <!-- reminders -->
      <div v-else-if="tab === 'remind' && task">
        <p>یادآوری‌ها بر اساس سررسید: <b class="inv-ltr">{{ task.reminderDays || "پیش‌فرض تنظیمات" }}</b> روز مانده؛ سررسید {{ task.dueDateText || "-" }}. پیام‌ها در صندوق یکپارچه می‌آید؛ پیامک و ایمیل فقط وقتی در تنظیمات فعال شود.</p>
        <p v-if="!reminderList.length" class="inv-muted">هنوز یادآوری ارسال نشده.</p>
        <ul><li v-for="h in reminderList" :key="h.id">{{ h.atText }} - {{ h.text }}</li></ul>
        <div v-if="can('edit')" class="d-flex gap-2 align-items-center"><input v-model="form.reminderDays" class="form-control form-control-sm inv-ltr" style="max-width: 160px" placeholder="3,1,0" /><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره یادآوری‌ها</button></div>
      </div>
    </div>
  </div>
</template>
