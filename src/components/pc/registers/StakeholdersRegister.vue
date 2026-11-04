<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { getStakeholderMatrix, getStakeholders, saveStakeholder } from "@/api/ProjectRegistersAPIs";
import PositionSelect from "@/components/common/PositionSelect.vue";
import { fail, ok } from "@/composables/inventory";
import { engagements } from "@/composables/tasks";

// Stakeholder register and the power / interest matrix. Only role and organisation are stored, never a personal phone number.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const matrix = ref<any>(null);
const form = ref<any>(null);

async function load() {
  const [[d, e], [m]] = await Promise.all([getStakeholders(props.contractId), getStakeholderMatrix(props.contractId)]);
  if (e) return fail(e);
  rows.value = rowsOf(d);
  matrix.value = m;
}
const blank = () => ({ id: null, contractId: props.contractId, name: "", organization: "", roleTitle: "", category: "", influence: 3, interest: 3, engagementCurrent: 3, engagementDesired: 4, expectations: "", communicationNeed: "", communicationFrequency: "", ownerPositionId: null, isActive: true });
async function save() {
  const [, e] = await saveStakeholder(form.value);
  if (e) return fail(e);
  ok("ذی‌نفع ذخیره شد.");
  form.value = null;
  load();
}
const quads = [["manageClosely", "مدیریت نزدیک (قدرت و علاقه زیاد)", "border-danger"], ["keepSatisfied", "راضی نگه‌داشتن (قدرت زیاد)", "border-warning"], ["keepInformed", "مطلع نگه‌داشتن (علاقه زیاد)", "border-info"], ["monitor", "پایش", "border-secondary"]];
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div v-if="matrix" class="row g-2 mb-3">
      <div v-for="q in quads" :key="q[0]" class="col-md-6"><div :class="q[2]" class="border border-2 rounded p-2 h-100"><div class="fw-bold small mb-1">{{ q[1] }}</div><span v-for="s in matrix[q[0]]" :key="s.id" class="inv-badge me-1">{{ s.name }}</span><span v-if="!matrix[q[0]].length" class="inv-muted small">-</span></div></div>
    </div>
    <button class="btn btn-sm btn-success mb-2 d-print-none" @click="form = blank()">+ ذی‌نفع جدید</button>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>نام / سازمان</th><th>نقش</th><th>دسته</th><th>نفوذ</th><th>علاقه</th><th>مشارکت فعلی</th><th>مشارکت مطلوب</th><th>شکاف</th><th>ربع ماتریس</th><th>نیاز ارتباطی</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="11" class="text-center inv-muted">ذی‌نفعی ثبت نشده.</td></tr>
          <tr v-for="s in rows" :key="s.id" :class="{ 'text-muted': !s.isActive }">
            <td>{{ s.name }}<div class="small inv-muted">{{ s.organization }}</div></td><td>{{ s.roleTitle }}</td><td>{{ s.category }}</td><td>{{ s.influence }}</td><td>{{ s.interest }}</td>
            <td>{{ engagements[s.engagementCurrent] }}</td><td>{{ engagements[s.engagementDesired] }}</td><td>{{ s.engagementGap }}</td><td>{{ s.quadrantText }}</td><td>{{ s.communicationNeed }} <span class="inv-muted">{{ s.communicationFrequency }}</span></td>
            <td class="d-print-none"><button class="btn btn-sm btn-outline-primary" @click="form = { ...s }">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal wide">
        <h5>{{ form.id ? "ویرایش ذی‌نفع" : "ذی‌نفع جدید" }}</h5>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">نام (شخص یا سازمان)</label><input v-model="form.name" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">سازمان</label><input v-model="form.organization" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نقش</label><input v-model="form.roleTitle" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">دسته (کارفرما، مشاور...)</label><input v-model="form.category" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نفوذ (۱ تا ۵)</label><input v-model.number="form.influence" class="form-control form-control-sm" type="number" min="1" max="5" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">علاقه (۱ تا ۵)</label><input v-model.number="form.interest" class="form-control form-control-sm" type="number" min="1" max="5" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">مشارکت فعلی</label><select v-model.number="form.engagementCurrent" class="form-select form-select-sm"><option v-for="(t, k) in engagements" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">مشارکت مطلوب</label><select v-model.number="form.engagementDesired" class="form-select form-select-sm"><option v-for="(t, k) in engagements" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">انتظارات</label><input v-model="form.expectations" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نیاز ارتباطی</label><input v-model="form.communicationNeed" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تناوب</label><input v-model="form.communicationFrequency" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">سمت مسئول ارتباط</label><PositionSelect v-model="form.ownerPositionId" /></div>
          <div class="col-md-6 pt-4"><label><input v-model="form.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-primary" @click="save">ذخیره</button><button class="btn btn-sm btn-light" @click="form = null">انصراف</button></div>
      </div>
    </div>
  </div>
</template>
