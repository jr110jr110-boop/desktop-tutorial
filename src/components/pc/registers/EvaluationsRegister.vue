<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { getProviders } from "@/api/InventoryAPIs";
import { decideEvaluation, getEvaluations, getEvaluationTemplate, saveEvaluation } from "@/api/ProjectRegistersAPIs";
import JDate from "@/components/pc/JDate.vue";
import { fail, ok, today } from "@/composables/inventory";
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import { approvalStateClass, approvalStates, evaluationParties } from "@/composables/tasks";

// Contractor / consultant evaluation questionnaire at project close: weighted criteria scored 1..5, grade from the settings.
// The evaluator is not the buyer of the contract with that party; the PMO decides.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const providers = ref<any[]>([]);
const form = ref<any>(null);
const dec = ref<any>(null);
const note = ref("");
const isPmo = hasRole(ProjectControlRoles.pmo);

async function load() {
  const [d, e] = await getEvaluations({ ContractId: props.contractId, PageSize: 100 });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function start(kind: number) {
  const [t, e] = await getEvaluationTemplate(kind);
  if (e) return fail(e);
  const [p] = await getProviders();
  providers.value = rowsOf(p);
  form.value = { id: null, contractId: props.contractId, partyKind: kind, providerId: null, partyName: "", evaluationDate: today(), comment: "", scores: (t?.criteria || []).map((c: any, i: number) => ({ seq: i + 1, criterion: c.criterion, weight: c.weight, score: 3, comment: "" })) };
}
async function edit(r: any) {
  const [p] = await getProviders();
  providers.value = rowsOf(p);
  form.value = { ...r, evaluationDate: r.evaluationDate?.slice(0, 10) };
}
const total = () => {
  const s = form.value.scores, w = s.reduce((a: number, x: any) => a + Number(x.weight), 0);
  return w ? Math.round((s.reduce((a: number, x: any) => a + (Number(x.weight) * Number(x.score)) / 5, 0) / w) * 100) : 0;
};
async function save(submit: boolean) {
  const [, e] = await saveEvaluation({ ...form.value, submit });
  if (e) return fail(e);
  ok(submit ? "برای تصمیم PMO ارسال شد." : "ذخیره شد.");
  form.value = null;
  load();
}
async function decide(approve: boolean) {
  if (!approve && !note.value.trim()) return fail("دلیل را بنویسید.");
  const [, e] = await decideEvaluation({ id: dec.value.id, approve, note: note.value });
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
    <div class="d-flex gap-2 mb-2 d-print-none"><button class="btn btn-sm btn-success" @click="start(1)">+ ارزیابی پیمانکار</button><button class="btn btn-sm btn-success" @click="start(2)">+ ارزیابی مشاور</button></div>
    <table class="table table-sm">
      <thead><tr><th>شماره</th><th>طرف</th><th>تاریخ</th><th>امتیاز</th><th>درجه</th><th>ارزیاب</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="8" class="text-center inv-muted">ارزیابی ثبت نشده.</td></tr>
        <tr v-for="r in rows" :key="r.id">
          <td class="inv-ltr">{{ r.number }}</td><td>{{ evaluationParties[r.partyKind] }}: {{ r.partyName }}</td><td>{{ r.evaluationDateText }}</td><td>{{ r.totalScore }}</td><td>{{ r.grade }}</td><td>{{ r.evaluatorName }}</td>
          <td><span :class="approvalStateClass[r.state]" class="inv-badge">{{ approvalStates[r.state] }}</span></td>
          <td class="d-print-none text-nowrap"><button v-if="r.state === 0 || r.state === 3" class="btn btn-sm btn-outline-primary" @click="edit(r)">ویرایش</button><button v-if="isPmo && r.state === 1" class="btn btn-sm btn-outline-success" @click="dec = r">تصمیم PMO</button></td>
        </tr>
      </tbody>
    </table>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal wide">
        <h5>پرسشنامه ارزیابی {{ evaluationParties[form.partyKind] }}</h5>
        <div class="row g-2 mb-2">
          <div class="col-md-5"><label class="form-label small mb-0">طرف قرارداد</label><select v-model.number="form.providerId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name || p.title }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">یا نام</label><input v-model="form.partyName" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ</label><JDate v-model="form.evaluationDate" /></div>
        </div>
        <table class="table table-sm"><thead><tr><th>معیار</th><th style="width: 80px">وزن</th><th style="width: 120px">امتیاز (۱ تا ۵)</th><th>توضیح</th></tr></thead>
          <tbody><tr v-for="s in form.scores" :key="s.seq"><td>{{ s.criterion }}</td><td>{{ s.weight }}</td><td><input v-model.number="s.score" class="form-control form-control-sm" type="number" min="1" max="5" /></td><td><input v-model="s.comment" class="form-control form-control-sm" /></td></tr></tbody></table>
        <p>امتیاز تقریبی: <b>{{ total() }}</b> از ۱۰۰ (درجه نهایی را سیستم از تنظیمات می‌دهد)</p>
        <textarea v-model="form.comment" class="form-control form-control-sm" rows="2" placeholder="نظر کلی"></textarea>
        <div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-outline-secondary" @click="save(false)">ذخیره پیش‌نویس</button><button class="btn btn-sm btn-primary" @click="save(true)">ارسال برای تصمیم PMO</button><button class="btn btn-sm btn-light" @click="form = null">انصراف</button></div>
      </div>
    </div>
    <div v-if="dec" class="inv-modal-backdrop" @click.self="dec = null"><div class="inv-modal"><h5>{{ dec.number }} - درجه {{ dec.grade }} ({{ dec.totalScore }})</h5><input v-model="note" class="form-control form-control-sm" placeholder="توضیح (برای رد الزامی)" /><div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-success" @click="decide(true)">تایید</button><button class="btn btn-sm btn-outline-danger" @click="decide(false)">رد</button><button class="btn btn-sm btn-light" @click="dec = null">انصراف</button></div></div></div>
  </div>
</template>
