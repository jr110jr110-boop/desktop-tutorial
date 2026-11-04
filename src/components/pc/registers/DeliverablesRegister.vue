<script setup lang="ts">
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import { onMounted, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { getActivities } from "@/api/ProjectControlAPIs";
import { getProjDeliverables, projDeliverableAction, saveProjDeliverable } from "@/api/ProjectRegistersAPIs";
import JDate from "@/components/pc/JDate.vue";
import { fail, ok } from "@/composables/inventory";
import { deliverableStatuses } from "@/composables/tasks";

// Writes belong to the unit managers and the PMO (the service checks it again); the others only read
const canWrite = hasRole(ProjectControlRoles.unitManagers);

// Deliverables of the project: linked to an activity / WBS node / MDR deliverable. The one who delivers is not the one who accepts.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const acts = ref<any[]>([]);
const form = ref<any>(null);
const act = ref<any>(null);
const note = ref("");

async function load() {
  const [d, e] = await getProjDeliverables({ ContractId: props.contractId, PageSize: 200 });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function open(r?: any) {
  const [a] = await getActivities({ ContractId: props.contractId });
  acts.value = rowsOf(a);
  form.value = r ? { ...r, dueDate: r.dueDate?.slice(0, 10) ?? null } : { id: null, contractId: props.contractId, code: "", title: "", description: "", activityId: null, wbsNodeId: null, mdrDeliverableId: null, workOrderId: null, dueDate: null };
}
async function save() {
  const [, e] = await saveProjDeliverable(form.value);
  if (e) return fail(e);
  ok("ذخیره شد.");
  form.value = null;
  load();
}
async function doAct() {
  if (act.value.a === "reject" && !note.value.trim()) return fail("دلیل رد را بنویسید.");
  const [, e] = await projDeliverableAction({ id: act.value.row.id, action: act.value.a, note: note.value });
  if (e) return fail(e);
  ok("ثبت شد.");
  act.value = null; note.value = "";
  load();
}
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <button v-if="canWrite" class="btn btn-sm btn-success mb-2 d-print-none" @click="open()">+ تحویلی جدید</button>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>کد</th><th>عنوان</th><th>فعالیت</th><th>WBS</th><th>سررسید</th><th>وضعیت</th><th>تحویل‌دهنده</th><th>پذیرنده</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">تحویلی ثبت نشده.</td></tr>
          <tr v-for="r in rows" :key="r.id" :class="{ 'table-danger': r.overdue }">
            <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td class="inv-ltr">{{ r.activityCode }}</td><td class="inv-ltr">{{ r.wbsNodeCode }}</td><td>{{ r.dueDateText }}</td><td>{{ deliverableStatuses[r.status] }}</td>
            <td>{{ r.deliveredByName }} {{ r.deliveredAtText }}</td><td>{{ r.acceptedByName }} {{ r.acceptedAtText }}<div class="small inv-muted">{{ r.acceptanceNote }}</div></td>
            <td class="d-print-none text-nowrap">
              <button v-if="canWrite && r.status < 2" class="btn btn-sm btn-outline-primary me-1" @click="open(r)">ویرایش</button>
              <button v-if="canWrite && r.status === 0" class="btn btn-sm btn-outline-secondary me-1" @click="act = { a: 'start', row: r }">شروع</button>
              <button v-if="canWrite && r.status < 2" class="btn btn-sm btn-outline-success me-1" @click="act = { a: 'deliver', row: r }">تحویل</button>
              <button v-if="canWrite && r.status === 2" class="btn btn-sm btn-success me-1" @click="act = { a: 'accept', row: r }">پذیرش</button>
              <button v-if="canWrite && r.status === 2" class="btn btn-sm btn-outline-danger" @click="act = { a: 'reject', row: r }">رد</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h5>تحویلی پروژه</h5>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">کد</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">فعالیت</label><select v-model.number="form.activityId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} - {{ a.title }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">سررسید</label><JDate v-model="form.dueDate" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرح</label><textarea v-model="form.description" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>
        <div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-primary" @click="save">ذخیره</button><button class="btn btn-sm btn-light" @click="form = null">انصراف</button></div>
      </div>
    </div>
    <div v-if="act" class="inv-modal-backdrop" @click.self="act = null">
      <div class="inv-modal"><h5>{{ act.row.code }} - {{ act.row.title }}</h5><input v-model="note" class="form-control form-control-sm" placeholder="توضیح" /><div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-primary" @click="doAct">ثبت</button><button class="btn btn-sm btn-light" @click="act = null">انصراف</button></div></div>
    </div>
  </div>
</template>
