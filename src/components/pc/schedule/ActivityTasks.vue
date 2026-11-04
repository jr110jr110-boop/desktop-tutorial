<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { getActivities } from "@/api/ProjectControlAPIs";
import { getTasksByActivity } from "@/api/TasksAPIs";
import TaskDrawer from "@/components/tasks/TaskDrawer.vue";
import { fail } from "@/composables/inventory";
import { progressMethods } from "@/composables/projectControl";
import { priorities, taskStatusClass, taskStatuses } from "@/composables/tasks";

// The tasks of a WBS activity (two-way link). When the progress method of the activity is «وظایف», its percent is the roll-up of these tasks weighted by estimated hours.
const props = defineProps({ contractId: { type: Number, required: true } });
const acts = ref<any[]>([]);
const activityId = ref<number | null>(null);
const tasks = ref<any[]>([]);
const open = ref<{ id: number | null; preset?: any } | null>(null);

async function loadActs() {
  const [d, e] = await getActivities({ ContractId: props.contractId });
  if (e) return fail(e);
  acts.value = rowsOf(d);
}
async function load() {
  tasks.value = [];
  if (!activityId.value) return;
  const [d, e] = await getTasksByActivity(activityId.value);
  if (e) return fail(e);
  tasks.value = rowsOf(d);
}
const current = () => acts.value.find((a) => a.id === activityId.value);
onMounted(loadActs);
watch(() => props.contractId, () => { activityId.value = null; tasks.value = []; loadActs(); });
</script>

<template>
  <div>
    <div class="d-flex flex-wrap gap-2 align-items-end mb-3 d-print-none">
      <div style="min-width: 360px"><label class="form-label small mb-0">فعالیت</label><select v-model.number="activityId" class="form-select form-select-sm" @change="load"><option :value="null">انتخاب کنید</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} - {{ a.title }}</option></select></div>
      <button :disabled="!activityId" class="btn btn-sm btn-success" @click="open = { id: null, preset: { contractId, activityId, kind: 2 } }">+ وظیفه برای این فعالیت</button>
    </div>
    <p v-if="current()" class="small">روش پیشرفت: <b>{{ progressMethods[current().progressMethod] }}</b> | پیشرفت فعلی {{ current().percentComplete ?? current().percent ?? 0 }}٪ <span v-if="current().progressMethod !== 9" class="inv-muted">(برای روش «وظایف»، درصد از میانگین وزنی ساعت برآوردی وظایف می‌آید)</span></p>
    <table class="table table-sm table-hover">
      <thead><tr><th>شماره</th><th>عنوان</th><th>وضعیت</th><th>اولویت</th><th>سررسید</th><th>پیشرفت</th><th>ساعت برآوردی</th><th>مسئول</th></tr></thead>
      <tbody>
        <tr v-if="!tasks.length"><td colspan="8" class="text-center inv-muted">{{ activityId ? "وظیفه‌ای به این فعالیت وصل نیست." : "یک فعالیت انتخاب کنید." }}</td></tr>
        <tr v-for="t in tasks" :key="t.id" style="cursor: pointer" @click="open = { id: t.id }">
          <td class="inv-ltr">{{ t.number }}</td><td>{{ t.title }}</td><td><span :class="taskStatusClass[t.status]" class="inv-badge">{{ taskStatuses[t.status] }}</span></td><td>{{ priorities[t.priority] }}</td><td>{{ t.dueDateText }}</td><td>{{ t.percentComplete }}٪</td><td>{{ t.estimatedHours }}</td><td>{{ (t.assigneeNames || []).join("، ") }}</td>
        </tr>
      </tbody>
    </table>
    <TaskDrawer v-if="open" :key="open.id ?? 'new'" :task-id="open.id" :preset="open.preset || {}" @close="open = null; load()" @changed="load" />
  </div>
</template>
