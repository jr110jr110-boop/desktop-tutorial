<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { decidePhaseEvaluation, getPhaseEvaluations, savePhaseEvaluation } from "@/api/ProjectRegistersAPIs";
import JDate from "@/components/pc/JDate.vue";
import { fail, ok, today } from "@/composables/inventory";
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import { approvalStateClass, approvalStates, phaseDecisions, phases } from "@/composables/tasks";

// Phase evaluation at the end of a phase (gate review): four scores 1..5, findings and a go / hold decision by the PMO.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const form = ref<any>(null);
const dec = ref<any>(null);
const note = ref("");
const isPmo = hasRole(ProjectControlRoles.pmo);

async function load() {
  const [d, e] = await getPhaseEvaluations({ ContractId: props.contractId, PageSize: 100 });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
const blank = () => ({ id: null, contractId: props.contractId, phase: 1, evaluationDate: today(), scheduleScore: 3, costScore: 3, qualityScore: 3, scopeScore: 3, achievements: "", problems: "", recommendation: "", decision: 1, submit: false });
async function save(submit: boolean) {
  const [, e] = await savePhaseEvaluation({ ...form.value, submit });
  if (e) return fail(e);
  ok(submit ? "ارسال شد." : "ذخیره شد.");
  form.value = null;
  load();
}
async function decide(approve: boolean) {
  if (!approve && !note.value.trim()) return fail("دلیل را بنویسید.");
  const [, e] = await decidePhaseEvaluation({ id: dec.value.id, approve, note: note.value });
  if (e) return fail(e);
  dec.value = null; note.value = "";
  ok("ثبت شد.");
  load();
}
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <button class="btn btn-sm btn-success mb-2 d-print-none" @click="form = blank()">+ ارزیابی فاز</button>
    <table class="table table-sm">
      <thead><tr><th>فاز</th><th>تاریخ</th><th>زمان</th><th>هزینه</th><th>کیفیت</th><th>دامنه</th><th>امتیاز کل</th><th>تصمیم</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="10" class="text-center inv-muted">ارزیابی فازی ثبت نشده.</td></tr>
        <tr v-for="r in rows" :key="r.id">
          <td>{{ phases[r.phase] }}</td><td>{{ r.evaluationDateText }}</td><td>{{ r.scheduleScore }}</td><td>{{ r.costScore }}</td><td>{{ r.qualityScore }}</td><td>{{ r.scopeScore }}</td><td>{{ r.overallScore }}</td><td>{{ phaseDecisions[r.decision] }}</td>
          <td><span :class="approvalStateClass[r.state]" class="inv-badge">{{ approvalStates[r.state] }}</span></td>
          <td class="d-print-none text-nowrap"><button v-if="r.state === 0 || r.state === 3" class="btn btn-sm btn-outline-primary" @click="form = { ...r, evaluationDate: r.evaluationDate?.slice(0, 10) }">ویرایش</button><button v-if="isPmo && r.state === 1" class="btn btn-sm btn-outline-success" @click="dec = r">تصمیم PMO</button></td>
        </tr>
      </tbody>
    </table>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h5>ارزیابی فاز</h5>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">فاز</label><select v-model.number="form.phase" class="form-select form-select-sm"><option v-for="(t, k) in phases" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ</label><JDate v-model="form.evaluationDate" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">پیشنهاد</label><select v-model.number="form.decision" class="form-select form-select-sm"><option v-for="(t, k) in phaseDecisions" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div v-for="f in [['scheduleScore', 'زمان'], ['costScore', 'هزینه'], ['qualityScore', 'کیفیت'], ['scopeScore', 'دامنه']]" :key="f[0]" class="col-md-3"><label class="form-label small mb-0">{{ f[1] }} (۱ تا ۵)</label><input v-model.number="form[f[0]]" class="form-control form-control-sm" type="number" min="1" max="5" /></div>
          <div class="col-12"><label class="form-label small mb-0">دستاوردها</label><textarea v-model="form.achievements" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-12"><label class="form-label small mb-0">مشکلات</label><textarea v-model="form.problems" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-12"><label class="form-label small mb-0">توصیه</label><textarea v-model="form.recommendation" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>
        <div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-outline-secondary" @click="save(false)">ذخیره پیش‌نویس</button><button class="btn btn-sm btn-primary" @click="save(true)">ارسال برای تصمیم PMO</button><button class="btn btn-sm btn-light" @click="form = null">انصراف</button></div>
      </div>
    </div>
    <div v-if="dec" class="inv-modal-backdrop" @click.self="dec = null"><div class="inv-modal"><h5>{{ phases[dec.phase] }} - {{ dec.overallScore }}</h5><input v-model="note" class="form-control form-control-sm" placeholder="توضیح (برای رد الزامی)" /><div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-success" @click="decide(true)">تایید</button><button class="btn btn-sm btn-outline-danger" @click="decide(false)">رد</button><button class="btn btn-sm btn-light" @click="dec = null">انصراف</button></div></div></div>
  </div>
</template>
