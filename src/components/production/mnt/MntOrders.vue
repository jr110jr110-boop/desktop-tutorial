<script setup lang="ts">
// Maintenance orders: filterable list and the "create by hand" dialog (also used to make the order of a breakdown report).
import { computed, onMounted, ref, watch } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import FnDate from "@/components/fn/FnDate.vue";
import DateTimeField from "./DateTimeField.vue";
import { createMaintenanceOrder, getMaintenanceOrders } from "@/api/ProductionAPIs";
import { parseAmount, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { badgeOfMaintenance, maintenanceKinds, maintenanceOrderStatuses, persianOf } from "@/composables/production";
import { hasRole, ProductionRoles } from "@/composables/roles";

const props = defineProps({ machines: { type: Array as () => any[], default: () => [] }, fromRequest: { type: Object, default: null } });
const emit = defineEmits(["done"]);
const router = useRouter();
const filters = ref({ MachineId: null as number | null, Kind: null as number | null, Status: null as number | null, OpenOnly: true as boolean | null });
const list = usePagedList(getMaintenanceOrders, filters);
const canWrite = hasRole(ProductionRoles.maintenanceWriters);
const form = ref<any>(null);
const busy = ref(false);
const rows = computed(() => list.rows.value);

function blank() {
  return { machineId: null, kind: 1, task: "", dueDate: null, estimatedHours: "", checklist: "", costCenter: "", requestId: null, downtimeStart: null };
}
function fromReport(r: any) {
  form.value = { ...blank(), machineId: r.machineId, kind: 2, task: r.description || "", requestId: r.id, downtimeStart: r.reportedAt ? String(r.reportedAt).slice(0, 16) + ":00" : null };
}
watch(() => props.fromRequest, (r) => r && fromReport(r), { immediate: true });

async function create() {
  const f = form.value;
  if (!f.machineId) return fail("ماشین را انتخاب کنید.");
  if (!String(f.task).trim()) return fail("شرح کار نگهداری را بنویسید.");
  busy.value = true;
  const [data, error] = await createMaintenanceOrder({
    machineId: f.machineId, kind: f.kind, task: f.task, dueDate: f.dueDate || null, estimatedHours: parseAmount(f.estimatedHours) || 0, checklist: f.checklist,
    costCenter: f.costCenter, requestId: f.requestId, downtimeStart: f.kind === 2 ? f.downtimeStart : null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("دستور نگهداری ساخته شد.");
  form.value = null;
  emit("done");
  if (data?.id) router.push("/production/maintenance-orders/" + data.id);
  else list.load();
}

onMounted(list.load);
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <select v-model="filters.MachineId" class="form-select form-select-sm">
        <option :value="null">همه ماشین‌ها</option>
        <option v-for="m in props.machines" :key="m.machineId" :value="m.machineId">{{ m.title }}</option>
      </select>
      <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in maintenanceKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
      <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in maintenanceOrderStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
      <label class="form-check mb-0"><input v-model="filters.OpenOnly" class="form-check-input" type="checkbox" :true-value="true" :false-value="null" /> <span class="form-check-label">فقط باز</span></label>
      <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = blank()">دستور جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>ماشین</th><th>نوع</th><th>شرح</th><th>سررسید</th><th>شروع توقف</th><th>پایان توقف</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">دستوری نیست.</td></tr>
          <tr v-for="o in rows" :key="o.id">
            <td class="inv-ltr">{{ o.number }}</td><td>{{ o.machineTitle }}</td><td>{{ maintenanceKinds[o.kind] }}</td><td>{{ o.task }}</td>
            <td>{{ persianOf(o.dueDateText, o.dueDate) || "-" }} <span v-if="o.overdue" class="inv-badge bad">عقب‌افتاده</span></td>
            <td>{{ persianOf(o.downtimeStartText, o.downtimeStart, true) || "-" }}</td><td>{{ persianOf(o.downtimeEndText, o.downtimeEnd, true) || "-" }}</td>
            <td><span :class="badgeOfMaintenance(o.status)" class="inv-badge">{{ maintenanceOrderStatuses[o.status] }}</span></td>
            <td><button class="btn btn-sm btn-outline-primary" @click="router.push('/production/maintenance-orders/' + o.id)">باز کردن</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.requestId ? "دستور تعمیر از گزارش خرابی" : "دستور نگهداری جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">ماشین</label>
            <select v-model="form.machineId" :disabled="!!form.requestId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="m in props.machines" :key="m.machineId" :value="m.machineId">{{ m.title }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">نوع</label>
            <select v-model="form.kind" :disabled="!!form.requestId" class="form-select form-select-sm"><option v-for="(t, k) in maintenanceKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">شرح کار</label><input v-model="form.task" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">سررسید</label><FnDate v-model="form.dueDate" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">ساعت برآوردی</label><input v-model="form.estimatedHours" class="form-control form-control-sm fn-num" inputmode="decimal" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مرکز هزینه</label><input v-model="form.costCenter" class="form-control form-control-sm inv-ltr" /></div>
          <div v-if="form.kind === 2" class="col-12"><label class="form-label small mb-0">زمان توقف ماشین (خالی = زمان گزارش یا اکنون)</label><DateTimeField v-model="form.downtimeStart" /></div>
          <div class="col-12"><label class="form-label small mb-0">فهرست بازبینی</label><textarea v-model="form.checklist" class="form-control form-control-sm" rows="3"></textarea></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ایجاد</button></div>
      </div>
    </div>
  </div>
</template>
