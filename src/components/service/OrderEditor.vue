<script setup lang="ts">
// Creates a service order (field / workshop): unit, kind, title, planned dates, travel note, engineers and tasks. Emits "saved" with the new order.
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import UnitPick from "./UnitPick.vue";
import { useEmployees } from "./svKit";
import { getSvRequest, getSvUnit, saveSvOrder } from "@/api/ServiceAPIs";
import { fail, ok } from "@/composables/inventory";
import { orderKinds } from "@/composables/service";

const props = defineProps({ requestId: { type: Number, default: null }, unitId: { type: Number, default: null } });
const emit = defineEmits(["saved", "cancel"]);
const { employees, loadEmployees } = useEmployees();
const busy = ref(false);
const unit = ref<any>(null);
const requestNumber = ref("");
const f = ref<any>({ kind: 1, title: "", plannedStart: null, plannedEnd: null, travelNote: "", engineers: [] as number[], tasks: [] as { description: string; isDone: boolean }[] });
const pickEngineer = ref<number | null>(null);

const nameOf = (id: number) => employees.value.find((e) => e.id === id)?.name || "#" + id;
function addEngineer() {
  if (pickEngineer.value && !f.value.engineers.includes(pickEngineer.value)) f.value.engineers.push(pickEngineer.value);
  pickEngineer.value = null;
}
async function save() {
  if (!unit.value?.id) return fail("واحد نصب‌شده را انتخاب کنید.");
  if (!String(f.value.title).trim()) return fail("عنوان دستور را بنویسید.");
  busy.value = true;
  const [data, error] = await saveSvOrder({
    requestId: props.requestId, unitId: unit.value.id, kind: f.value.kind, title: f.value.title, plannedStart: f.value.plannedStart, plannedEnd: f.value.plannedEnd, travelNote: f.value.travelNote,
    engineerEmployeeIds: f.value.engineers, tasks: f.value.tasks.filter((t: any) => String(t.description).trim()),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("دستور خدمات ثبت شد.");
  emit("saved", data);
}
onMounted(async () => {
  loadEmployees();
  let unitId = props.unitId;
  if (props.requestId) {
    const [r] = await getSvRequest(props.requestId);
    if (r) {
      requestNumber.value = r.number;
      unitId = unitId || r.unitId;
      if (!f.value.title) f.value.title = r.description ? String(r.description).slice(0, 120) : "";
    }
  }
  if (unitId) {
    const [u] = await getSvUnit(unitId);
    if (u) unit.value = u;
  }
});
</script>

<template>
  <div class="inv-modal-backdrop screen-only" @click.self="emit('cancel')">
    <div class="inv-modal wide">
      <h2 class="inv-title mb-2">دستور خدمات جدید <span v-if="requestNumber" class="inv-muted">برای درخواست <span class="inv-ltr">{{ requestNumber }}</span></span></h2>
      <div class="row g-2">
        <div class="col-md-6"><label class="form-label small mb-0">واحد نصب‌شده *</label><UnitPick v-model="unit" :disabled="!!requestId && !!unit" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">نوع</label>
          <select v-model="f.kind" class="form-select form-select-sm"><option v-for="(t, k) in orderKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-12"><label class="form-label small mb-0">عنوان *</label><input v-model="f.title" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">شروع برنامه</label><FnDate v-model="f.plannedStart" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">پایان برنامه</label><FnDate v-model="f.plannedEnd" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">یادداشت سفر</label><input v-model="f.travelNote" class="form-control form-control-sm" /></div>
        <div class="col-12">
          <label class="form-label small mb-0">مهندسان</label>
          <div class="d-flex gap-2 flex-wrap align-items-center">
            <select v-model="pickEngineer" class="form-select form-select-sm" style="max-width: 18rem"><option :value="null">انتخاب...</option><option v-for="e in employees" :key="e.id" :value="e.id">{{ e.name }}<template v-if="e.jobTitle"> - {{ e.jobTitle }}</template></option></select>
            <button class="btn btn-sm btn-outline-secondary" @click="addEngineer">افزودن</button>
            <span v-for="id in f.engineers" :key="id" class="inv-badge info">{{ nameOf(id) }} <a href="#" @click.prevent="f.engineers.splice(f.engineers.indexOf(id), 1)">×</a></span>
          </div>
        </div>
        <div class="col-12">
          <label class="form-label small mb-0">وظایف</label>
          <div v-for="(t, i) in f.tasks" :key="i" class="d-flex gap-2 mb-1"><input v-model="t.description" class="form-control form-control-sm" /><button class="btn btn-sm btn-outline-danger" @click="f.tasks.splice(i, 1)">حذف</button></div>
          <button class="btn btn-sm btn-outline-secondary" @click="f.tasks.push({ description: '', isDone: false })">افزودن وظیفه</button>
        </div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('cancel')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button></div>
    </div>
  </div>
</template>
