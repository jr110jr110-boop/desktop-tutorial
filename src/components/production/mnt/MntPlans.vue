<script setup lang="ts">
// Preventive maintenance plans: list, editor, and drafting of the orders that are due.
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import FnDate from "@/components/fn/FnDate.vue";
import { draftDueMaintenance, getMaintenancePlans, saveMaintenancePlan } from "@/api/ProductionAPIs";
import { parseAmount, usePagedList, yesNo } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { maintenanceIntervalKinds, persianOf, qtyText } from "@/composables/production";
import { hasRole, ProductionRoles } from "@/composables/roles";

const props = defineProps({ machines: { type: Array as () => any[], default: () => [] } });
const router = useRouter();
const filters = ref({ MachineId: null as number | null, IsActive: true as boolean | null });
const list = usePagedList(getMaintenancePlans, filters);
const canWrite = hasRole(ProductionRoles.maintenanceWriters);
const form = ref<any>(null);
const busy = ref(false);
const draft = ref<{ asOf: string | null; result: any }>({ asOf: null, result: null });
const rows = computed(() => list.rows.value);

function blank() {
  return { id: null, machineId: null, task: "", intervalKind: 1, intervalValue: "", startDate: null, checklist: "", estimatedHours: "", isActive: true, rowVersion: null };
}
function edit(p: any) {
  form.value = { id: p.id, machineId: p.machineId, task: p.task, intervalKind: p.intervalKind, intervalValue: String(p.intervalValue ?? ""), startDate: p.startDate ? String(p.startDate).slice(0, 10) : null, checklist: p.checklist || "", estimatedHours: String(p.estimatedHours ?? ""), isActive: p.isActive, rowVersion: p.rowVersion };
}

async function save() {
  const f = form.value;
  if (!f.machineId) return fail("ماشین را انتخاب کنید.");
  if (!String(f.task).trim()) return fail("شرح کار را بنویسید.");
  const value = parseAmount(f.intervalValue);
  if (!value || value <= 0) return fail("مقدار دوره باید بیشتر از صفر باشد.");
  busy.value = true;
  const [, error] = await saveMaintenancePlan({
    id: f.id, machineId: f.machineId, task: f.task, intervalKind: f.intervalKind, intervalValue: value, startDate: f.startDate || null, checklist: f.checklist,
    estimatedHours: parseAmount(f.estimatedHours) || 0, isActive: f.isActive, rowVersion: f.rowVersion,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("برنامه نگهداری ذخیره شد.");
  form.value = null;
  list.load();
}

async function runDraft() {
  busy.value = true;
  const [data, error] = await draftDueMaintenance(draft.value.asOf);
  busy.value = false;
  if (error) return fail(error);
  draft.value.result = data;
  ok(`${data?.drafted ?? 0} دستور پیش‌نویس شد.`);
  list.load();
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
      <select v-model="filters.IsActive" class="form-select form-select-sm">
        <option :value="true">فعال</option><option :value="false">غیرفعال</option><option :value="null">همه</option>
      </select>
      <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = blank()">برنامه جدید</button>
    </div>

    <div v-if="canWrite" class="d-flex gap-2 align-items-end flex-wrap mb-3">
      <div><label class="form-label small mb-0">تا تاریخ (سررسید)</label><FnDate v-model="draft.asOf" /></div>
      <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="runDraft">پیش‌نویس دستورهای سررسید</button>
      <span class="inv-muted small">دستورهایی که تا ۷ روز آینده سررسید دارند پیش‌نویس می‌شوند؛ تاریخ خالی = امروز.</span>
    </div>
    <div v-if="draft.result" class="alert alert-info py-2 small">
      <div>تاریخ مبنا {{ persianOf(draft.result.asOfText, draft.result.asOf) }}: {{ draft.result.plansChecked }} برنامه بررسی شد و {{ draft.result.drafted }} دستور پیش‌نویس شد.</div>
      <ul class="mb-0"><li v-for="o in draft.result.orders" :key="o.id"><a href="#" @click.prevent="router.push('/production/maintenance-orders/' + o.id)"><span class="inv-ltr">{{ o.number }}</span></a> - {{ o.machineTitle }} - {{ o.task }}</li></ul>
    </div>

    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>ماشین</th><th>شرح کار</th><th>دوره</th><th>شروع</th><th>آخرین انجام</th><th>سررسید بعدی</th><th>ساعت کارکرد از آخرین انجام</th><th>ساعت برآوردی</th><th>فعال</th><th>دستور باز</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="11" class="text-center inv-muted">برنامه‌ای نیست.</td></tr>
          <tr v-for="p in rows" :key="p.id">
            <td>{{ p.machineTitle }}</td><td>{{ p.task }}</td>
            <td>{{ qtyText(p.intervalValue) }} {{ maintenanceIntervalKinds[p.intervalKind] }}</td>
            <td>{{ persianOf(p.startDateText, p.startDate) }}</td><td>{{ persianOf(p.lastDoneAtText, p.lastDoneAt) || "-" }}</td><td>{{ persianOf(p.nextDueDateText, p.nextDueDate) || "-" }}</td>
            <td>{{ p.intervalKind === 2 ? qtyText(p.runningHoursSince) : "-" }}</td><td>{{ qtyText(p.estimatedHours) }}</td><td>{{ yesNo(p.isActive) }}</td>
            <td><a v-if="p.openOrderId" href="#" @click.prevent="router.push('/production/maintenance-orders/' + p.openOrderId)">باز کردن</a><span v-else>-</span></td>
            <td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="edit(p)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش برنامه نگهداری" : "برنامه نگهداری جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">ماشین</label>
            <select v-model="form.machineId" :disabled="!!form.id" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="m in props.machines" :key="m.machineId" :value="m.machineId">{{ m.title }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">شرح کار</label><input v-model="form.task" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع دوره</label>
            <select v-model="form.intervalKind" class="form-select form-select-sm"><option v-for="(t, k) in maintenanceIntervalKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">مقدار دوره ({{ maintenanceIntervalKinds[form.intervalKind] }})</label><input v-model="form.intervalValue" class="form-control form-control-sm fn-num" inputmode="decimal" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ شروع</label><FnDate v-model="form.startDate" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">ساعت برآوردی</label><input v-model="form.estimatedHours" class="form-control form-control-sm fn-num" inputmode="decimal" /></div>
          <div class="col-md-4 d-flex align-items-end"><label class="form-check"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> <span class="form-check-label">فعال</span></label></div>
          <div class="col-12"><label class="form-label small mb-0">فهرست بازبینی (هر مورد در یک خط)</label><textarea v-model="form.checklist" class="form-control form-control-sm" rows="4"></textarea></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
