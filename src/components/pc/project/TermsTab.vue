<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { generateProjectCode, getCalendars, getCoding, getTerms, getWorkOrderInfos, saveTerms, saveWorkOrderInfo } from "@/api/ProjectControlAPIs";
import { getWorkOrders, rowsOf } from "@/api/InventoryAPIs";
import JDate from "@/components/pc/JDate.vue";
import { fail, ok, rial } from "@/composables/inventory";
import { accrualModes, categories, dateOnly, weekdays } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Contract terms (penalty, advance, retention, reporting rules), the project code (generated once, never typed) and the equipment codes of the work orders (WI-PL-005).
const props = defineProps({ contractId: { type: Number, required: true } });
const emit = defineEmits(["changed"]);

const t = ref<any>(null);
const calendars = ref<any[]>([]);
const busy = ref(false);
const category = ref(1);
const canEdit = hasRole(ProjectControlRoles.terms);
const canCode = hasRole(ProjectControlRoles.planning);

const infos = ref<any[]>([]);
const workOrders = ref<any[]>([]);
const typeRows = ref<any[]>([]);
const info = ref<any>({ workOrderId: null, typeCode: "", sizeCode: "", optionCode: "", plannedDeliveryDate: null });

async function load() {
  t.value = null;
  const [data, error] = await getTerms(props.contractId);
  if (error) return fail(error);
  t.value = { ...data, scheduleStartDate: dateOnly(data.scheduleStartDate), contractDeliveryDate: dateOnly(data.contractDeliveryDate) };
  const [i] = await getWorkOrderInfos(props.contractId);
  infos.value = Array.isArray(i) ? i : [];
}

async function save() {
  busy.value = true;
  const [data, error] = await saveTerms({ ...t.value, calendarId: t.value.calendarId || null });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  t.value = { ...data, scheduleStartDate: dateOnly(data.scheduleStartDate), contractDeliveryDate: dateOnly(data.contractDeliveryDate) };
  emit("changed");
}

async function generate() {
  busy.value = true;
  const [data, error] = await generateProjectCode(props.contractId, category.value);
  busy.value = false;
  if (error) return fail(error);
  ok("کد پروژه صادر شد: " + data.projectCode);
  await load();
  emit("changed");
}

async function saveInfo() {
  busy.value = true;
  const [, error] = await saveWorkOrderInfo({ ...info.value, contractId: props.contractId, productType: 0 });
  busy.value = false;
  if (error) return fail(error);
  ok("کد تجهیز ثبت شد.");
  info.value = { workOrderId: null, typeCode: "", sizeCode: "", optionCode: "", plannedDeliveryDate: null };
  const [i] = await getWorkOrderInfos(props.contractId);
  infos.value = Array.isArray(i) ? i : [];
}

onMounted(async () => {
  const [[c], [w], [ty]] = await Promise.all([getCalendars(), getWorkOrders(), getCoding("TYPE")]);
  calendars.value = rowsOf(c);
  workOrders.value = rowsOf(w).filter((x: any) => x.contractId === props.contractId);
  typeRows.value = Array.isArray(ty) ? ty : [];
  load();
});
watch(() => props.contractId, load);
</script>

<template>
  <div v-if="t">
    <div class="row g-2 mb-3">
      <div class="col-md-3"><label class="form-label small mb-0">کد قرارداد</label><div class="inv-ltr">{{ t.contractCode }}</div></div>
      <div class="col-md-5"><label class="form-label small mb-0">عنوان</label><div>{{ t.contractTitle }}</div></div>
      <div class="col-md-4">
        <label class="form-label small mb-0">کد پروژه</label>
        <div v-if="t.projectCode"><b class="inv-ltr">{{ t.projectCode }}</b> <span class="inv-muted">({{ categories[t.category] }}؛ ثابت و بدون تغییر)</span></div>
        <div v-else-if="canCode" class="d-flex gap-2">
          <select v-model.number="category" class="form-select form-select-sm"><option v-for="(n, k) in categories" :key="k" :value="Number(k)">{{ n }}</option></select>
          <button :disabled="busy" class="btn btn-sm btn-primary text-nowrap" @click="generate">صدور کد</button>
        </div>
        <div v-else class="inv-muted">هنوز صادر نشده؛ صدور با برنامه‌ریزی است.</div>
      </div>
    </div>

    <div class="row g-2">
      <div class="col-md-2"><label class="form-label small mb-0">ارز</label><input v-model="t.currencyCode" :disabled="!canEdit" class="form-control form-control-sm inv-ltr" maxlength="5" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">مبلغ قرارداد بدون ارزش افزوده</label><input v-model.number="t.valueExVat" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">ارزش افزوده (٪)</label><input v-model.number="t.vatPercent" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" max="100" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">پیش‌پرداخت (٪)</label><input v-model.number="t.advancePercent" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" max="100" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">حسن انجام کار / سپرده (٪)</label><input v-model.number="t.retentionPercent" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" max="100" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">جریمه تاخیر در هفته (٪)</label><input v-model.number="t.latePenaltyPercentPerWeek" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" max="100" step="0.01" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">سقف جریمه (٪)</label><input v-model.number="t.penaltyCapPercent" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" max="100" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">مهلت مجاز تاخیر (روز)</label><input v-model.number="t.graceDays" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" /></div>
      <div class="col-md-4"><label class="form-label small mb-0">مبلغ فعلی (با دستور تغییرهای تاییدشده)</label><div>{{ rial(t.currentValue) }} <span class="inv-muted">(+{{ rial(t.approvedVariationPrice) }} و {{ t.approvedVariationDays }} روز)</span></div></div>

      <div class="col-md-3"><label class="form-label small mb-0">تاریخ شروع برنامه</label><JDate v-model="t.scheduleStartDate" :disabled="!canEdit" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">تاریخ تحویل قراردادی</label><JDate v-model="t.contractDeliveryDate" :disabled="!canEdit" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">تقویم کاری پروژه</label>
        <select v-model="t.calendarId" :disabled="!canEdit" class="form-select form-select-sm"><option :value="null">تقویم شرکت</option><option v-for="c in calendars" :key="c.id" :value="c.id">{{ c.name }}</option></select>
      </div>
      <div class="col-md-3"><label class="form-label small mb-0">روز هفته تاریخ داده (برداشت هفتگی)</label>
        <select v-model.number="t.dataDateWeekday" :disabled="!canEdit" class="form-select form-select-sm"><option v-for="(n, k) in weekdays" :key="k" :value="Number(k)">{{ n }}</option></select>
      </div>
      <div class="col-md-3"><label class="form-label small mb-0">روش تخصیص ارزش برنامه (PV)</label>
        <select v-model.number="t.accrualMode" :disabled="!canEdit" class="form-select form-select-sm"><option v-for="(n, k) in accrualModes" :key="k" :value="Number(k)">{{ n }}</option></select>
      </div>
      <div class="col-md-2"><label class="form-label small mb-0">روز گزارش ماهانه</label><input v-model.number="t.clientReportDayOfMonth" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="1" max="31" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">مهلت بررسی کارفرما (روز)</label><input v-model.number="t.clientReviewDays" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="1" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">ذخیره مدیریتی</label><input v-model.number="t.managementReserve" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="0" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">مدیر پروژه</label><div>{{ t.projectManagerName || "—" }}</div></div>
      <div class="col-12 d-flex flex-wrap gap-4">
        <label class="form-check"><input v-model="t.changeProcedureAgreed" :disabled="!canEdit" class="form-check-input" type="checkbox" /> رویه کنترل تغییرات با کارفرما امضا شده است</label>
        <label class="form-check"><input v-model="t.clientReportShowsIndices" :disabled="!canEdit" class="form-check-input" type="checkbox" /> گزارش کارفرما شامل SPI و CPI باشد</label>
        <label class="form-check"><input v-model="t.autoSnapshot" :disabled="!canEdit" class="form-check-input" type="checkbox" /> برداشت خودکار عکس هفتگی</label>
      </div>
    </div>
    <div v-if="canEdit" class="mt-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره شرایط قرارداد</button></div>

    <h2 class="inv-title mt-4 mb-2">کد تجهیز دستورهای ساخت (WI-PL-005)</h2>
    <table class="table table-sm">
      <thead><tr><th>دستور ساخت</th><th>نوع</th><th>اندازه</th><th>گزینه</th><th>تکرار</th><th>کد پروژه تجهیز</th><th>تحویل برنامه‌ای</th></tr></thead>
      <tbody>
        <tr v-if="!infos.length"><td colspan="7" class="text-center inv-muted">کدی ثبت نشده است.</td></tr>
        <tr v-for="i in infos" :key="i.id"><td class="inv-ltr">{{ i.workOrderCode }}</td><td class="inv-ltr">{{ i.typeCode }}</td><td class="inv-ltr">{{ i.sizeCode }}</td><td class="inv-ltr">{{ i.optionCode }}</td><td>{{ i.repeatCounter }}</td><td class="inv-ltr fw-bold">{{ i.equipmentProjectCode }}</td><td>{{ i.plannedDeliveryDate ? i.plannedDeliveryDate.slice(0, 10) : "" }}</td></tr>
      </tbody>
    </table>
    <div v-if="canCode" class="row g-2 align-items-end">
      <div class="col-md-3"><label class="form-label small mb-0">دستور ساخت</label><select v-model="info.workOrderId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }}</option></select></div>
      <div class="col-md-2"><label class="form-label small mb-0">نوع (TYPE)</label><select v-model="info.typeCode" class="form-select form-select-sm inv-ltr"><option value="">-</option><option v-for="r in typeRows" :key="r.id" :value="r.code">{{ r.code }} {{ r.family }}</option></select></div>
      <div class="col-md-2"><label class="form-label small mb-0">اندازه (۳ نویسه)</label><input v-model="info.sizeCode" class="form-control form-control-sm inv-ltr" maxlength="3" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">گزینه (۲ نویسه)</label><input v-model="info.optionCode" class="form-control form-control-sm inv-ltr" maxlength="2" /></div>
      <div class="col-md-3"><button :disabled="busy || !info.workOrderId || !info.typeCode" class="btn btn-sm btn-outline-primary" @click="saveInfo">ثبت کد تجهیز</button></div>
    </div>
    <p v-if="canCode && !workOrders.length" class="inv-muted mt-1">دستور ساختی برای این قرارداد پیدا نشد.</p>
  </div>
</template>
