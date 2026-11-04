<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { decideLesson, getLessons, saveLesson } from "@/api/ProjectRegistersAPIs";
import { fail, ok } from "@/composables/inventory";
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import { approvalStateClass, approvalStates, phases } from "@/composables/tasks";

// Lessons learned: any team member writes and submits; the PMO approves or rejects (not the author).
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const form = ref<any>(null);
const dec = ref<any>(null);
const note = ref("");
const isPmo = hasRole(ProjectControlRoles.pmo);

async function load() {
  const [d, e] = await getLessons({ ContractId: props.contractId, PageSize: 200 });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
const blank = () => ({ id: null, contractId: props.contractId, phase: null, category: "", isPositive: false, whatHappened: "", cause: "", recommendation: "", submit: false });
async function save(submit: boolean) {
  const [, e] = await saveLesson({ ...form.value, submit });
  if (e) return fail(e);
  ok(submit ? "برای تایید PMO ارسال شد." : "ذخیره شد.");
  form.value = null;
  load();
}
async function decide(approve: boolean) {
  if (!approve && !note.value.trim()) return fail("دلیل رد را بنویسید.");
  const [, e] = await decideLesson({ id: dec.value.id, approve, note: note.value });
  if (e) return fail(e);
  ok("ثبت شد.");
  dec.value = null; note.value = "";
  load();
}
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <button class="btn btn-sm btn-success mb-2 d-print-none" @click="form = blank()">+ درس آموخته جدید</button>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>شماره</th><th>فاز</th><th>دسته</th><th>نوع</th><th>چه شد</th><th>علت</th><th>توصیه</th><th>وضعیت</th><th>نویسنده</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="10" class="text-center inv-muted">درسی ثبت نشده.</td></tr>
          <tr v-for="l in rows" :key="l.id">
            <td class="inv-ltr">{{ l.number }}</td><td>{{ l.phase ? phases[l.phase] : "" }}</td><td>{{ l.category }}</td><td>{{ l.isPositive ? "نکته مثبت" : "نکته منفی" }}</td><td>{{ l.whatHappened }}</td><td>{{ l.cause }}</td><td>{{ l.recommendation }}</td>
            <td><span :class="approvalStateClass[l.state]" class="inv-badge">{{ approvalStates[l.state] }}</span><div v-if="l.decidedByName" class="small inv-muted">{{ l.decidedByName }} {{ l.decidedAtText }} {{ l.decisionNote }}</div></td><td>{{ l.creatorName }}</td>
            <td class="d-print-none text-nowrap"><button v-if="l.state === 0 || l.state === 3" class="btn btn-sm btn-outline-primary" @click="form = { ...l }">ویرایش</button><button v-if="isPmo && l.state === 1" class="btn btn-sm btn-outline-success" @click="dec = l">تصمیم PMO</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h5>درس آموخته</h5>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">فاز</label><select v-model.number="form.phase" class="form-select form-select-sm"><option :value="null">-</option><option v-for="(t, k) in phases" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">دسته</label><input v-model="form.category" class="form-control form-control-sm" /></div>
          <div class="col-md-4 pt-4"><label><input v-model="form.isPositive" class="form-check-input" type="checkbox" /> نکته مثبت</label></div>
          <div class="col-12"><label class="form-label small mb-0">چه اتفاقی افتاد</label><textarea v-model="form.whatHappened" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-12"><label class="form-label small mb-0">علت</label><textarea v-model="form.cause" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-12"><label class="form-label small mb-0">توصیه برای پروژه‌های بعد</label><textarea v-model="form.recommendation" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>
        <div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-outline-secondary" @click="save(false)">ذخیره پیش‌نویس</button><button class="btn btn-sm btn-primary" @click="save(true)">ارسال برای تایید PMO</button><button class="btn btn-sm btn-light" @click="form = null">انصراف</button></div>
      </div>
    </div>
    <div v-if="dec" class="inv-modal-backdrop" @click.self="dec = null">
      <div class="inv-modal"><h5>{{ dec.number }}</h5><p>{{ dec.whatHappened }}</p><input v-model="note" class="form-control form-control-sm" placeholder="توضیح (برای رد الزامی)" /><div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-success" @click="decide(true)">تایید</button><button class="btn btn-sm btn-outline-danger" @click="decide(false)">رد</button><button class="btn btn-sm btn-light" @click="dec = null">انصراف</button></div></div>
    </div>
  </div>
</template>
