<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import TestReportPrint from "@/components/quality/print/TestReportPrint.vue";
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import DocAttach from "@/components/quality/DocAttach.vue";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import { getEquipmentList, getItpByWorkOrder, getQualifications, getReportStandards, getTestReport, saveTestReport, testReportAction } from "@/api/QualityAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { dateInput, qualificationStates, releaseParties, reportResults, reportStatuses, reportTypes, resultBadge, testTypes, useBackdateWindow } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One test report: values with limits (standards / ITP), indications, equipment, witnesses, submit and approve.
const route = useRoute();
const router = useRouter();
const isNew = route.params.id === "new";
const rep = ref<any>(null);
const item = ref<any>(null);
const standards = ref<any[]>([]);
const activities = ref<any[]>([]);
const equipment = ref<any[]>([]);
const qualifications = ref<any[]>([]);
const rejectReason = ref("");
const busy = ref(false);
async function doPrint() {
  await nextTick();
  printForm();
}
const isQc = hasRole(QualityRoles.inspectors);

const editable = computed(() => rep.value && (isNew || rep.value.status === 0 || rep.value.status === 3));
// the date of a report is limited to the back-date window of the company (R1q D15): from MaxBackdateDays days back to tomorrow; the API refuses a date outside it with its own
// Persian text, which is shown. A draft or rejected report is judged by the date it was first entered with (review M15).
const { min: dateMin, max: dateMax, hint: dateHint, warning: dateWarning, load: loadWindow } = useBackdateWindow(true);
const enteredOn = ref<string | null>(null);
const staleDraft = computed(() => !isNew && !!rep.value && !!editable.value && !!enteredOn.value && dateWarning(enteredOn.value) !== "");
const ndtMethod = computed(() => ({ 1: "PT", 2: "MT", 3: "UT", 4: "RT" } as Record<number, string>)[rep.value?.type]);
const hasIndications = computed(() => [1, 2, 3, 4].includes(rep.value?.type));

function blank() {
  return {
    id: null, rowVersion: null, type: 1, workOrderId: null, itpActivityId: null, serialNumber: "", procedureRef: "", procedureRevision: "", codeEdition: "",
    examinerQualificationId: null, reportDate: today(), environment: "", documentId: null, note: "", status: 0, result: 0, problems: [],
    values: [] as any[], indications: [] as any[], equipmentIds: [] as number[], witnesses: [] as any[],
  };
}

function load(dto: any) {
  enteredOn.value = dateInput(dto.reportDate);
  rep.value = {
    ...dto,
    reportDate: dateInput(dto.reportDate),
    equipmentIds: (dto.equipment || []).map((e: any) => e.equipmentId),
    witnesses: (dto.witnesses || []).map((w: any) => ({ ...w, witnessDate: dateInput(w.witnessDate) })),
  };
  item.value = dto.itemId ? { id: dto.itemId, code: dto.itemCode, title: "" } : null;
}

async function loadStandards() {
  const [data] = await getReportStandards(rep.value.type);
  standards.value = data || [];
}

async function loadActivities() {
  activities.value = [];
  if (!rep.value.workOrderId) return;
  const [data] = await getItpByWorkOrder(rep.value.workOrderId);
  activities.value = (data?.activities || []).filter((a: any) => a.requiredReportType === rep.value.type && ![2, 3, 4].includes(a.status));
}

function addStandards() {
  standards.value.forEach((s) => {
    if (rep.value.values.some((v: any) => v.parameter?.trim().toLowerCase() === s.name.trim().toLowerCase())) return;
    rep.value.values.push({ parameter: s.name, unit: s.unit, min: s.min, max: s.max, measured: null, source: s.source, requiresLimits: s.requiresLimits, pass: null });
  });
}

const addValue = () => rep.value.values.push({ parameter: "", unit: "", min: null, max: null, measured: null });
const addIndication = () => rep.value.indications.push({ location: "", type: "", size: "", orientation: "", accepted: true });
const addWitness = () => rep.value.witnesses.push({ party: 3, name: "", witnessDate: today() });
const nullIfEmpty = (v: any) => (v === "" || v === undefined ? null : v);

async function save(silent = false) {
  const r = rep.value;
  busy.value = true;
  const [data, error] = await saveTestReport({
    id: r.id,
    rowVersion: r.rowVersion,
    type: r.type,
    workOrderId: r.workOrderId,
    itpActivityId: r.itpActivityId || null,
    itemId: item.value?.id || null,
    serialNumber: r.serialNumber,
    procedureRef: r.procedureRef,
    procedureRevision: r.procedureRevision,
    codeEdition: r.codeEdition,
    examinerQualificationId: r.examinerQualificationId || null,
    reportDate: r.reportDate,
    environment: r.environment,
    documentId: r.documentId,
    note: r.note,
    values: r.values.map((v: any) => ({ parameter: v.parameter, unit: v.unit, min: nullIfEmpty(v.min), max: nullIfEmpty(v.max), measured: nullIfEmpty(v.measured) })),
    indications: r.indications,
    equipmentIds: r.equipmentIds,
    witnesses: r.witnesses,
  });
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  load(data);
  if (!silent) ok("ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "QualityReportEdit", params: { id: data.id } });
  return true;
}

async function act(action: string, reason?: string) {
  if (editable.value && action === "submit" && !(await save(true))) return;
  busy.value = true;
  const [data, error] = await testReportAction(rep.value.id, action, reason);
  busy.value = false;
  if (error) return fail(error);
  load(data);
  ok(action === "submit" ? "ارسال شد." : action === "approve" ? "تایید شد." : "رد شد.");
}

watch(() => rep.value?.type, () => {
  if (!rep.value) return;
  loadStandards();
  loadActivities();
  rep.value.examinerQualificationId = null;
  loadQualifications();
});

async function loadQualifications() {
  qualifications.value = [];
  if (!ndtMethod.value) return;
  const [data] = await getQualifications({ Kind: 3, Method: ndtMethod.value, PageSize: 200 });
  qualifications.value = rowsOf(data).filter((q: any) => q.state === 0 || q.state === 1);
}

onMounted(async () => {
  loadWindow();
  const [eq] = await getEquipmentList({ PageSize: 300 });
  equipment.value = rowsOf(eq).filter((e: any) => e.status !== 3);
  if (isNew) {
    rep.value = blank();
  } else {
    const [data, error] = await getTestReport(Number(route.params.id));
    if (error) return fail(error);
    load(data);
    loadActivities();
  }
  loadStandards();
  loadQualifications();
});
</script>

<template>
  <div v-if="rep" class="inv-page">
    <TestReportPrint v-if="rep.id" :rep="rep" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">گزارش آزمون <span v-if="rep.number" class="inv-ltr ms-2">{{ rep.number }}</span></h1>
          <span :class="{ ok: rep.status === 2, warn: rep.status === 1, bad: rep.status === 3 }" class="inv-badge">{{ reportStatuses[rep.status] }}</span>
          <span :class="resultBadge(rep.result)" class="inv-badge ms-1">نتیجه: {{ reportResults[rep.result] }}</span>
          <router-link v-if="rep.ncrId" :to="{ name: 'InventoryNcrEdit', params: { id: rep.ncrId } }" class="ms-2 small inv-ltr">{{ rep.ncrNumber }}</router-link>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'QualityReports' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="rep.id" class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ گزارش</button>
          <button v-if="editable && isQc" :disabled="busy" class="btn btn-sm btn-primary" @click="save()">ذخیره</button>
          <button v-if="!isNew && editable && isQc" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('submit')">ارسال برای تایید</button>
          <button v-if="rep.status === 1 && isQc" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">تایید (غیر از آزمونگر)</button>
        </div>
      </div>

      <div v-if="rep.problems && rep.problems.length && editable" class="alert alert-warning py-2">
        <b>پیش از ارسال باید رفع شود:</b>
        <ul class="mb-0"><li v-for="(p, i) in rep.problems" :key="i">{{ p }}</li></ul>
      </div>
      <div v-if="rep.status === 3 && rep.rejectReason" class="alert alert-danger py-2">دلیل رد: {{ rep.rejectReason }}</div>
      <div v-if="staleDraft" class="alert alert-warning py-2">
        تاریخ ثبت‌شده این گزارش ({{ jalali(enteredOn) }}) از مهلت ثبت گذشته است؛ پیش‌نویس یا گزارش ردشده‌ای که از مهلت گذشته ویرایش نمی‌شود. گزارش تازه‌ای با تاریخ واقعی آزمون ثبت کنید.
      </div>

      <div class="row g-2">
        <div class="col-md-3">
          <label class="form-label small mb-0">نوع گزارش</label>
          <select v-model="rep.type" :disabled="!isNew" class="form-select form-select-sm"><option v-for="(t, k) in reportTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="rep.reportDate" :disabled="!editable" :max="dateMax" :min="dateMin" class="form-control form-control-sm" type="date" />
          <span v-if="editable && dateWarning(rep.reportDate)" class="small text-danger">{{ dateWarning(rep.reportDate) }}</span>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">دستور ساخت</label>
          <WorkOrderSelect v-if="editable" v-model="rep.workOrderId" @change="loadActivities" />
          <div v-else class="small pt-1 inv-ltr">{{ rep.workOrderCode }}</div>
        </div>
        <div class="col-md-5">
          <label class="form-label small mb-0">فعالیت ITP (گزارش لازم)</label>
          <select v-if="editable" v-model="rep.itpActivityId" class="form-select form-select-sm">
            <option :value="null">—</option>
            <option v-for="a in activities" :key="a.id" :value="a.id">{{ a.itemNo || a.seq }} - {{ a.activity }}</option>
            <option v-if="rep.itpActivityId && !activities.some((a) => a.id === rep.itpActivityId)" :value="rep.itpActivityId">فعالیت {{ rep.itpActivityId }}</option>
          </select>
          <div v-else class="small pt-1">{{ rep.itpActivityId || "—" }}</div>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">کالا / قطعه</label>
          <ItemPicker v-if="editable" v-model="item" />
          <div v-else class="small pt-1 inv-ltr">{{ rep.itemCode }}</div>
        </div>
        <div class="col-md-2"><label class="form-label small mb-0">سریال</label><input v-model="rep.serialNumber" :disabled="!editable" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">رویه / دستورالعمل</label><input v-model="rep.procedureRef" :disabled="!editable" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">بازنگری رویه</label><input v-model="rep.procedureRevision" :disabled="!editable" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">ویرایش کد / استاندارد</label><input v-model="rep.codeEdition" :disabled="!editable" class="form-control form-control-sm inv-ltr" /></div>
        <div v-if="ndtMethod" class="col-md-6">
          <label class="form-label small mb-0">گواهی صلاحیت آزمونگر (روش {{ ndtMethod }}، سطح II یا III) *</label>
          <select v-if="editable" v-model="rep.examinerQualificationId" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="q in qualifications" :key="q.id" :value="q.id">{{ q.personName }} - {{ q.certificateNumber }} - سطح {{ q.level }} ({{ qualificationStates[q.state] }})</option>
          </select>
          <div v-else class="small pt-1 inv-ltr">{{ rep.examinerQualificationNumber }}</div>
        </div>
        <div class="col-md-6"><label class="form-label small mb-0">شرایط محیطی</label><input v-model="rep.environment" :disabled="!editable" class="form-control form-control-sm" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">پیوست (عکس / نقشه نشانه‌ها)</label><DocAttach v-model="rep.documentId" :disabled="!editable" title="پیوست گزارش آزمون" /></div>
        <div class="col-md-8"><label class="form-label small mb-0">توضیح</label><input v-model="rep.note" :disabled="!editable" class="form-control form-control-sm" /></div>
      </div>
      <p v-if="editable" class="inv-muted small mt-2 mb-0">{{ dateHint }}</p>
      <p v-if="rep.examinerName || rep.approvedByName" class="small mt-2 mb-0">
        آزمونگر: {{ rep.examinerName }} <span v-if="rep.submittedAt">— ارسال: {{ jalali(rep.submittedAt, true) }}</span>
        <span v-if="rep.approvedByName">— تایید: {{ rep.approvedByName }} {{ jalali(rep.approvedAt, true) }}</span>
      </p>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">پارامترها و مقادیر</h2>
        <div v-if="editable" class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-secondary" @click="addStandards">افزودن پارامترهای استاندارد این نوع</button>
          <button class="btn btn-sm btn-outline-primary" @click="addValue">ردیف جدید</button>
        </div>
      </div>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>مقدار اندازه‌گیری‌شده</th><th>نتیجه</th><th>منبع حد</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rep.values.length"><td colspan="8" class="text-center inv-muted">ردیفی نیست.</td></tr>
            <tr v-for="(v, i) in rep.values" :key="i">
              <td><input v-model="v.parameter" :disabled="!editable" class="form-control form-control-sm" /></td>
              <td><input v-model="v.unit" :disabled="!editable" class="form-control form-control-sm inv-ltr" style="width: 80px" /></td>
              <td><input v-model="v.min" :disabled="!editable" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
              <td><input v-model="v.max" :disabled="!editable" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
              <td><input v-model="v.measured" :disabled="!editable" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
              <td>
                <span v-if="v.requiresLimits" class="inv-badge warn">حد را مهندسی تعیین کند</span>
                <span v-else-if="v.pass !== null && v.pass !== undefined" :class="v.pass ? 'ok' : 'bad'" class="inv-badge">{{ v.pass ? "قبول" : "رد" }}</span>
              </td>
              <td class="small">{{ v.source }}</td>
              <td><button v-if="editable" class="btn btn-sm btn-outline-danger" @click="rep.values.splice(i, 1)">حذف</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="standards.length" class="border rounded p-2 mt-2">
        <b class="small">حدود استاندارد نوع «{{ reportTypes[rep.type] }}» (فقط نمایش؛ سرور محاسبه می‌کند)</b>
        <table class="table table-sm mb-0">
          <thead><tr><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>منبع</th><th>الزامی</th></tr></thead>
          <tbody>
            <tr v-for="s in standards" :key="s.name">
              <td>{{ s.name }}</td><td class="inv-ltr">{{ s.unit }}</td><td class="inv-ltr">{{ s.min ?? "" }}</td><td class="inv-ltr">{{ s.max ?? "" }}</td>
              <td class="small">{{ s.source }} <span v-if="s.requiresLimits" class="inv-badge warn">حد را مهندسی تعیین کند</span></td><td>{{ s.mandatory ? "بله" : "" }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="hasIndications" class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">نشانه‌ها (تمام نشانه‌ها، پذیرفته و مردود)</h2>
        <button v-if="editable" class="btn btn-sm btn-outline-primary" @click="addIndication">نشانه جدید</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th>محل</th><th>نوع (خطی / گرد)</th><th>اندازه</th><th>جهت</th><th>پذیرفته</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!rep.indications.length"><td colspan="6" class="text-center inv-muted">نشانه‌ای ثبت نشده (بدون نشانه).</td></tr>
          <tr v-for="(n, i) in rep.indications" :key="i">
            <td><input v-model="n.location" :disabled="!editable" class="form-control form-control-sm" /></td>
            <td><input v-model="n.type" :disabled="!editable" class="form-control form-control-sm" /></td>
            <td><input v-model="n.size" :disabled="!editable" class="form-control form-control-sm" /></td>
            <td><input v-model="n.orientation" :disabled="!editable" class="form-control form-control-sm" /></td>
            <td class="text-center"><input v-model="n.accepted" :disabled="!editable" type="checkbox" /></td>
            <td><button v-if="editable" class="btn btn-sm btn-outline-danger" @click="rep.indications.splice(i, 1)">حذف</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="row g-3">
      <div class="col-lg-6">
        <div class="inv-card h-100">
          <h2 class="inv-title mb-2">تجهیزات اندازه‌گیری</h2>
          <div v-if="editable" class="d-flex flex-wrap gap-2">
            <label v-for="e in equipment" :key="e.id" class="form-check small border rounded px-2 py-1">
              <input v-model="rep.equipmentIds" :value="e.id" class="form-check-input ms-1" type="checkbox" />
              <span class="inv-ltr">{{ e.code }}</span> {{ e.name }}
              <span v-if="e.status === 2" class="inv-badge bad">سررسیدگذشته</span>
            </label>
            <span v-if="!equipment.length" class="inv-muted">تجهیزی ثبت نشده.</span>
          </div>
          <ul v-else class="mb-0">
            <li v-for="e in rep.equipment" :key="e.equipmentId"><span class="inv-ltr">{{ e.code }}</span> {{ e.name }} — اعتبار تا {{ jalali(e.nextDue) }}</li>
          </ul>
          <p class="inv-muted mt-2 mb-0">فقط تجهیزی که در تاریخ آزمون کالیبره است پذیرفته می‌شود.</p>
        </div>
      </div>
      <div class="col-lg-6">
        <div class="inv-card h-100">
          <div class="inv-toolbar">
            <h2 class="inv-title">شاهدها</h2>
            <button v-if="editable" class="btn btn-sm btn-outline-primary" @click="addWitness">شاهد جدید</button>
          </div>
          <table class="table table-sm mb-0">
            <thead><tr><th>طرف</th><th>نام</th><th>تاریخ</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!rep.witnesses.length"><td colspan="4" class="text-center inv-muted">شاهدی نیست.</td></tr>
              <tr v-for="(w, i) in rep.witnesses" :key="i">
                <td><select v-model="w.party" :disabled="!editable" class="form-select form-select-sm"><option v-for="(t, k) in releaseParties" :key="k" :value="Number(k)">{{ t }}</option></select></td>
                <td><input v-model="w.name" :disabled="!editable" class="form-control form-control-sm" /></td>
                <td><input v-model="w.witnessDate" :disabled="!editable" class="form-control form-control-sm" type="date" /></td>
                <td><button v-if="editable" class="btn btn-sm btn-outline-danger" @click="rep.witnesses.splice(i, 1)">حذف</button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div v-if="rep.status === 1 && isQc" class="inv-card">
      <div class="d-flex gap-2 align-items-end">
        <div class="flex-grow-1"><label class="form-label small mb-0">دلیل رد گزارش</label><input v-model="rejectReason" class="form-control form-control-sm" /></div>
        <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('reject', rejectReason)">رد گزارش</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
