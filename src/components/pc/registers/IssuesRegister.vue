<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { getIssues, issueAction, saveIssue } from "@/api/ProjectRegistersAPIs";
import { getActivities } from "@/api/ProjectControlAPIs";
import JDate from "@/components/pc/JDate.vue";
import PositionSelect from "@/components/common/PositionSelect.vue";
import { fail, ok, today } from "@/composables/inventory";
import { issueStatuses, priorities } from "@/composables/tasks";

// Issue log: category, priority, owner position, due date, resolution (resolver and closer differ), link to an activity and its delay reason code.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const acts = ref<any[]>([]);
const form = ref<any>(null);
const act = ref<any>(null);
const note = ref("");

async function load() {
  const [d, e] = await getIssues({ ContractId: props.contractId, PageSize: 200 });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function open(r?: any) {
  const [a] = await getActivities({ ContractId: props.contractId });
  acts.value = rowsOf(a);
  form.value = r ? { ...r, raisedDate: r.raisedDate?.slice(0, 10), dueDate: r.dueDate?.slice(0, 10) ?? null } : { id: null, contractId: props.contractId, title: "", description: "", category: "", priority: 2, ownerPositionId: null, raisedDate: today(), dueDate: null, activityId: null, delayReasonCode: "" };
}
async function save() {
  const [, e] = await saveIssue(form.value);
  if (e) return fail(e);
  ok("مساله ذخیره شد.");
  form.value = null;
  load();
}
async function doAction() {
  if (["resolve", "close", "reopen"].includes(act.value.a) && !note.value.trim()) return fail("شرح را بنویسید.");
  const [, e] = await issueAction({ id: act.value.row.id, action: act.value.a, note: note.value });
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
    <button class="btn btn-sm btn-success mb-2 d-print-none" @click="open()">+ مساله جدید</button>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>عنوان</th><th>دسته</th><th>اولویت</th><th>مالک</th><th>ثبت</th><th>سررسید</th><th>وضعیت</th><th>فعالیت / کد تاخیر</th><th>راه‌حل</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="11" class="text-center inv-muted">مساله‌ای ثبت نشده.</td></tr>
          <tr v-for="r in rows" :key="r.id" :class="{ 'table-danger': r.overdue }">
            <td class="inv-ltr">{{ r.number }}</td><td>{{ r.title }}</td><td>{{ r.category }}</td><td>{{ priorities[r.priority] }}</td><td>{{ r.ownerPositionTitle }}</td><td>{{ r.raisedDateText }}</td><td>{{ r.dueDateText }}</td>
            <td>{{ issueStatuses[r.status] }}</td><td><span class="inv-ltr">{{ r.activityCode }}</span> {{ r.delayReasonCode }}</td>
            <td>{{ r.resolution }}<div v-if="r.resolvedByName" class="small inv-muted">حل: {{ r.resolvedByName }} {{ r.resolvedAtText }}</div><div v-if="r.closedByName" class="small inv-muted">بستن: {{ r.closedByName }} {{ r.closedAtText }}</div></td>
            <td class="d-print-none text-nowrap">
              <button v-if="r.status < 2" class="btn btn-sm btn-outline-primary me-1" @click="open(r)">ویرایش</button>
              <button v-if="r.status === 0" class="btn btn-sm btn-outline-secondary me-1" @click="act = { a: 'start', row: r }">شروع</button>
              <button v-if="r.status < 2" class="btn btn-sm btn-outline-success me-1" @click="act = { a: 'resolve', row: r }">حل شد</button>
              <button v-if="r.status === 2" class="btn btn-sm btn-success me-1" @click="act = { a: 'close', row: r }">بستن</button>
              <button v-if="r.status >= 2" class="btn btn-sm btn-outline-danger" @click="act = { a: 'reopen', row: r }">بازگشایی</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h5>{{ form.id ? "ویرایش مساله" : "مساله جدید" }}</h5>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">دسته</label><input v-model="form.category" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">اولویت</label><select v-model.number="form.priority" class="form-select form-select-sm"><option v-for="(t, k) in priorities" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">تاریخ ثبت</label><JDate v-model="form.raisedDate" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">سررسید</label><JDate v-model="form.dueDate" /></div>
          <div class="col-12"><label class="form-label small mb-0">سمت مالک</label><PositionSelect v-model="form.ownerPositionId" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">فعالیت مرتبط</label><select v-model.number="form.activityId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} - {{ a.title }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">کد دلیل تاخیر</label><input v-model="form.delayReasonCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرح</label><textarea v-model="form.description" class="form-control form-control-sm" rows="3"></textarea></div>
        </div>
        <div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-primary" @click="save">ذخیره</button><button class="btn btn-sm btn-light" @click="form = null">انصراف</button></div>
      </div>
    </div>
    <div v-if="act" class="inv-modal-backdrop" @click.self="act = null">
      <div class="inv-modal"><h5>{{ act.row.number }}</h5><input v-model="note" class="form-control form-control-sm" placeholder="شرح راه‌حل یا دلیل" /><p class="small inv-muted mt-1">بستن مساله را باید شخصی غیر از حل‌کننده انجام دهد.</p><div class="d-flex gap-2"><button class="btn btn-sm btn-primary" @click="doAction">ثبت</button><button class="btn btn-sm btn-light" @click="act = null">انصراف</button></div></div>
    </div>
  </div>
</template>
