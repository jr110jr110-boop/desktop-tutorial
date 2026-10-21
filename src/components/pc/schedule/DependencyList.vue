<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { deleteDependency, getActivities, getDependencies, saveDependency } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, ok } from "@/composables/inventory";
import { dependencyOrigins, dependencyTypes } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Dependencies between activities: FS, SS, FF, SF with lag (negative lag = lead). The API refuses a link that makes a cycle and names the activities.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const acts = ref<any[]>([]);
const d = ref<any>(null);
const busy = ref(false);
const text = ref("");
const canEdit = hasRole(ProjectControlRoles.planningOrPm);

async function load() {
  const [[x, e1], [a]] = await Promise.all([getDependencies({ ContractId: props.contractId }), getActivities({ ContractId: props.contractId })]);
  if (e1) return fail(e1);
  rows.value = rowsOf(x);
  acts.value = rowsOf(a);
}
const shown = computed(() => {
  const t = text.value.trim().toLowerCase();
  return t ? rows.value.filter((r) => String(r.predecessorCode).toLowerCase().includes(t) || String(r.successorCode).toLowerCase().includes(t)) : rows.value;
});
const blank = () => ({ id: null, contractId: props.contractId, predecessorId: null, successorId: null, type: 1, lagDays: 0 });
async function save() {
  busy.value = true;
  const [, error] = await saveDependency({ ...d.value });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  d.value = null;
  load();
}
async function remove(r: any) {
  if (!confirm("وابستگی حذف شود؟")) return;
  const [, error] = await deleteDependency(r.id);
  if (error) return fail(error);
  ok("حذف شد.");
  load();
}
onMounted(load);
watch(() => props.contractId, () => { d.value = null; load(); });
</script>

<template>
  <div>
    <div class="inv-filters mb-3 d-print-none">
      <input v-model="text" class="form-control form-control-sm" placeholder="کد فعالیت" />
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="d = blank()">وابستگی جدید</button>
    </div>
    <div v-if="d" class="border rounded p-3 mb-3 d-print-none">
      <div class="row g-2 align-items-end">
        <div class="col-md-4"><label class="form-label small mb-0">پیش‌نیاز</label><select v-model="d.predecessorId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} {{ a.title }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">نوع</label><select v-model.number="d.type" class="form-select form-select-sm"><option v-for="(n, k) in dependencyTypes" :key="k" :value="Number(k)">{{ n }}</option></select></div>
        <div class="col-md-4"><label class="form-label small mb-0">پیرو</label><select v-model="d.successorId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in acts" :key="a.id" :value="a.id">{{ a.code }} {{ a.title }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">تاخیر / تقدم (روز)</label><input v-model.number="d.lagDays" class="form-control form-control-sm" type="number" /></div>
      </div>
      <div class="mt-2 d-flex gap-2"><button :disabled="busy || !d.predecessorId || !d.successorId" class="btn btn-sm btn-primary" @click="save">ذخیره</button><button class="btn btn-sm btn-outline-secondary" @click="d = null">انصراف</button></div>
    </div>
    <table class="table table-sm table-hover">
      <thead><tr><th>پیش‌نیاز</th><th>نوع</th><th>پیرو</th><th>تاخیر (روز)</th><th>منشأ</th><th class="d-print-none"></th></tr></thead>
      <tbody>
        <tr v-if="!shown.length"><td colspan="6" class="text-center inv-muted">وابستگی‌ای نیست.</td></tr>
        <tr v-for="r in shown" :key="r.id">
          <td class="inv-ltr">{{ r.predecessorCode }}</td><td><span class="inv-badge info">{{ dependencyTypes[r.type] }}</span></td><td class="inv-ltr">{{ r.successorCode }}</td><td>{{ r.lagDays }}</td><td>{{ dependencyOrigins[r.origin] }}</td>
          <td class="d-print-none text-nowrap">
            <template v-if="canEdit"><button class="btn btn-sm btn-outline-secondary" @click="d = { ...r }">ویرایش</button> <button class="btn btn-sm btn-outline-danger" @click="remove(r)">حذف</button></template>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
