<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { activityAction, deleteActivity, getActivities, getCalendars, getWbs, saveActivity } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import JDate from "@/components/pc/JDate.vue";
import WorkOrderOfContract from "@/components/pc/WorkOrderOfContract.vue";
import { fail, jalali, ok } from "@/composables/inventory";
import { activityStatuses, constraints, dateOnly, milestoneEvents, pct, phases, progressMethods, unitOptions, unitText } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Activities of the project: planned dates (CPM), float, forecast, progress and status. Planning and the project manager edit; unit managers start or hold their activities.
const props = defineProps({ contractId: { type: Number, required: true } });
const filters = ref<any>({ Status: null, Phase: null, ResponsibleUnit: "", WorkOrderId: null, OnlyCritical: false, OnlyLate: false, Contains: "" });
const rows = ref<any[]>([]);
const nodes = ref<any[]>([]);
const calendars = ref<any[]>([]);
const loading = ref(false);
const busy = ref(false);
const a = ref<any>(null);
const reason = ref("");
const canEdit = hasRole(ProjectControlRoles.planningOrPm);
const canAct = hasRole(ProjectControlRoles.unitManagers);

async function load() {
  loading.value = true;
  const f = { ...filters.value, ContractId: props.contractId };
  if (!f.OnlyCritical) delete f.OnlyCritical;
  if (!f.OnlyLate) delete f.OnlyLate;
  const [data, error] = await getActivities(f);
  loading.value = false;
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
async function loadLists() {
  const [[n], [c]] = await Promise.all([getWbs({ ContractId: props.contractId }), getCalendars()]);
  nodes.value = rowsOf(n);
  calendars.value = rowsOf(c);
}

const blank = () => ({
  id: null, contractId: props.contractId, wbsNodeId: null, code: "", title: "", sourceType: 7, workOrderId: null, durationDays: 1, calendarId: null, phase: null, responsibleUnit: "", constraintType: 0, constraintDate: null,
  progressMethod: 0, weight: 0, budgetAtCompletion: 0, purchaseOrderLineId: null, purchaseRequestLineId: null, itpActivityId: null, milestoneEvent: 0, plannedQuantity: null, leadTimeDays: 0, isPackaging: false,
});
function edit(r: any) {
  a.value = { ...r, constraintDate: dateOnly(r.constraintDate) };
  reason.value = "";
}
async function save() {
  busy.value = true;
  const x = a.value;
  const [, error] = await saveActivity({ ...x, phase: x.phase || null, workOrderId: x.workOrderId || null, wbsNodeId: x.wbsNodeId || null, calendarId: x.calendarId || null, constraintDate: x.constraintType ? x.constraintDate : null });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  a.value = null;
  load();
}
async function remove() {
  if (!confirm("فعالیت حذف شود؟")) return;
  const [, error] = await deleteActivity(a.value.id);
  if (error) return fail(error);
  ok("حذف شد.");
  a.value = null;
  load();
}
async function act(action: string) {
  busy.value = true;
  const [data, error] = await activityAction(a.value.id, action, null, reason.value);
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  a.value = { ...a.value, ...data, constraintDate: dateOnly(data.constraintDate) };
  load();
}

onMounted(() => { loadLists(); load(); });
watch(() => props.contractId, () => { a.value = null; loadLists(); load(); });
</script>

<template>
  <div>
    <div class="inv-filters mb-3 d-print-none">
      <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" @keydown.enter="load" />
      <select v-model="filters.Status" class="form-select form-select-sm" @change="load"><option :value="null">همه وضعیت‌ها</option><option v-for="(n, k) in activityStatuses" :key="k" :value="Number(k)">{{ n }}</option></select>
      <select v-model="filters.Phase" class="form-select form-select-sm" @change="load"><option :value="null">همه فازها</option><option v-for="(n, k) in phases" :key="k" :value="Number(k)">{{ n }}</option></select>
      <select v-model="filters.ResponsibleUnit" class="form-select form-select-sm" @change="load"><option value="">همه واحدها</option><option v-for="u in unitOptions" :key="u[0]" :value="u[0]">{{ u[1] }}</option></select>
      <WorkOrderOfContract v-model="filters.WorkOrderId" :contract-id="contractId" @change="load" />
      <label class="form-check mb-1"><input v-model="filters.OnlyCritical" class="form-check-input" type="checkbox" @change="load" /> فقط بحرانی</label>
      <label class="form-check mb-1"><input v-model="filters.OnlyLate" class="form-check-input" type="checkbox" @change="load" /> فقط تاخیردار</label>
      <button class="btn btn-sm btn-secondary" @click="load">جستجو</button>
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="edit(blank())">فعالیت جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>کد</th><th>عنوان</th><th>فاز</th><th>واحد</th><th>مدت</th><th>شروع برنامه</th><th>پایان برنامه</th><th>شناوری</th><th>پایان پیش‌بینی</th><th>پیشرفت</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="11" class="text-center inv-muted">{{ loading ? "در حال بارگذاری..." : "فعالیتی نیست." }}</td></tr>
          <tr v-for="r in rows" :key="r.id" role="button" :class="{ 'table-danger': r.isCritical && r.status !== 2 }" @click="edit(r)">
            <td class="inv-ltr">{{ r.code }}</td>
            <td>{{ r.title }} <span v-if="r.isCritical" class="inv-badge bad">بحرانی</span> <span v-if="r.isLate" class="inv-badge warn">تاخیر</span></td>
            <td>{{ phases[r.phase] }}</td><td>{{ unitText(r.responsibleUnit) }}</td><td>{{ r.durationDays }}</td>
            <td>{{ jalali(r.earlyStart) }}</td><td>{{ jalali(r.earlyFinish) }}</td><td>{{ r.totalFloat }}</td><td>{{ jalali(r.forecastFinish) }}</td>
            <td>{{ pct(r.percentCache) }}</td>
            <td><span :class="r.status === 2 ? 'ok' : r.status === 3 ? 'bad' : r.status === 1 ? 'info' : ''" class="inv-badge">{{ activityStatuses[r.status] }}</span></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="a" class="inv-modal-backdrop d-print-none" @click.self="a = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-3">{{ a.id ? "فعالیت " + a.code : "فعالیت جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">کد (خالی: خودکار)</label><input v-model="a.code" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-9"><label class="form-label small mb-0">عنوان *</label><input v-model="a.title" :disabled="!canEdit" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">گره WBS</label><select v-model="a.wbsNodeId" :disabled="!canEdit" class="form-select form-select-sm"><option :value="null">-</option><option v-for="n in nodes" :key="n.id" :value="n.id">{{ n.code }} {{ n.title }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">دستور ساخت</label><WorkOrderOfContract v-model="a.workOrderId" :contract-id="contractId" :disabled="!canEdit" placeholder="-" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">فاز</label><select v-model="a.phase" :disabled="!canEdit" class="form-select form-select-sm"><option :value="null">-</option><option v-for="(n, k) in phases" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">واحد مسئول</label><select v-model="a.responsibleUnit" :disabled="!canEdit" class="form-select form-select-sm"><option value="">-</option><option v-for="u in unitOptions" :key="u[0]" :value="u[0]">{{ u[1] }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">مدت (روز کاری)</label><input v-model.number="a.durationDays" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">تقویم</label><select v-model="a.calendarId" :disabled="!canEdit" class="form-select form-select-sm"><option :value="null">تقویم پروژه</option><option v-for="c in calendars" :key="c.id" :value="c.id">{{ c.name }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">قید زمانی</label><select v-model.number="a.constraintType" :disabled="!canEdit" class="form-select form-select-sm"><option v-for="(n, k) in constraints" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div v-if="a.constraintType" class="col-md-3"><label class="form-label small mb-0">تاریخ قید</label><JDate v-model="a.constraintDate" :disabled="!canEdit" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">روش اندازه‌گیری پیشرفت</label>
            <select v-model.number="a.progressMethod" :disabled="!canEdit" class="form-select form-select-sm"><option v-for="(n, k) in progressMethods" :key="k" :value="Number(k)" :disabled="Number(k) === 5">{{ n }}{{ Number(k) === 5 ? " (هنوز فعال نیست)" : "" }}</option></select>
          </div>
          <div v-if="[2, 3].includes(a.progressMethod)" class="col-md-3"><label class="form-label small mb-0">شناسه ردیف سفارش خرید *</label><input v-model.number="a.purchaseOrderLineId" :disabled="!canEdit" class="form-control form-control-sm" type="number" /></div>
          <div v-if="a.progressMethod === 6" class="col-md-3"><label class="form-label small mb-0">شناسه ردیف ITP *</label><input v-model.number="a.itpActivityId" :disabled="!canEdit" class="form-control form-control-sm" type="number" /></div>
          <div v-if="a.progressMethod === 8" class="col-md-3"><label class="form-label small mb-0">رویداد تکمیل *</label><select v-model.number="a.milestoneEvent" :disabled="!canEdit" class="form-select form-select-sm"><option v-for="(n, k) in milestoneEvents" :key="k" :value="Number(k)">{{ n }}</option></select></div>
          <div v-if="a.progressMethod === 7" class="col-md-3"><label class="form-label small mb-0">مقدار برنامه‌ای *</label><input v-model.number="a.plannedQuantity" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">وزن (٪)</label><input v-model.number="a.weight" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" max="100" step="0.01" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">بودجه تکمیل (BAC)</label><input v-model.number="a.budgetAtCompletion" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">زمان تامین (روز)</label><input v-model.number="a.leadTimeDays" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-md-3 pt-4"><label class="form-check"><input v-model="a.isPackaging" :disabled="!canEdit" class="form-check-input" type="checkbox" /> فعالیت بسته‌بندی (نیازمند برگه ترخیص)</label></div>
        </div>
        <div v-if="a.id" class="border rounded p-2 mt-3 small">
          <div class="row g-2">
            <div class="col-md-3">شروع زودترین: {{ jalali(a.earlyStart) }}</div><div class="col-md-3">پایان زودترین: {{ jalali(a.earlyFinish) }}</div>
            <div class="col-md-3">شروع دیرترین: {{ jalali(a.lateStart) }}</div><div class="col-md-3">پایان دیرترین: {{ jalali(a.lateFinish) }}</div>
            <div class="col-md-3">شناوری کل: {{ a.totalFloat }} / آزاد: {{ a.freeFloat }}</div><div class="col-md-3">شروع پیش‌بینی: {{ jalali(a.forecastStart) }}</div>
            <div class="col-md-3">شروع واقعی: {{ jalali(a.actualStart) }}</div><div class="col-md-3">پایان واقعی: {{ jalali(a.actualFinish) }}</div>
            <div class="col-md-3">پیشرفت: {{ pct(a.percentCache) }}</div><div class="col-md-3">وضعیت: {{ activityStatuses[a.status] }}</div>
            <div v-if="a.needByDate" class="col-md-3">نیاز تا تاریخ: {{ jalali(a.needByDate) }}</div><div v-if="a.onHoldReason" class="col-md-6">دلیل توقف: {{ a.onHoldReason }}</div>
          </div>
        </div>
        <div class="d-flex flex-wrap gap-2 align-items-end mt-3">
          <button v-if="canEdit" :disabled="busy || !a.title" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
          <template v-if="a.id && canAct">
            <button v-if="!a.actualStart" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('start')">ثبت شروع واقعی (امروز)</button>
            <input v-if="a.status !== 3" v-model="reason" class="form-control form-control-sm" placeholder="دلیل توقف" style="max-width: 220px" />
            <button v-if="a.status !== 3 && a.status !== 2" :disabled="busy || !reason" class="btn btn-sm btn-outline-warning" @click="act('hold')">توقف</button>
            <button v-if="a.status === 3" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('resume')">ادامه</button>
          </template>
          <button v-if="a.id && canEdit" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="remove">حذف</button>
          <button class="btn btn-sm btn-outline-secondary" @click="a = null">بستن</button>
        </div>
        <p class="inv-muted mt-2 mb-0">پایان فعالیت دستی ثبت نمی‌شود؛ با پیشنهاد پیشرفت ۱۰۰ درصد و تایید برنامه‌ریزی (یا رویدادهای سیستم) انجام می‌شود. فعالیت شروع‌شده یا موجود در خط مبنا حذف نمی‌شود.</p>
      </div>
    </div>
  </div>
</template>
