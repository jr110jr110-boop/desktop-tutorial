<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import RecordPrint from "@/components/quality/print/RecordPrint.vue";
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import DocAttach from "@/components/quality/DocAttach.vue";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import { getEquipmentList, getItpByWorkOrder, getQcRecord, saveQcRecord } from "@/api/QualityAPIs";
import { getLots } from "@/api/InventoryAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, num, ok, today } from "@/composables/inventory";
import { activityStatuses, dateInput, qcResults, recordTypes, useBackdateWindow } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One inspection record with its measurements. A record cannot be edited after it is saved (the API has no update).
const route = useRoute();
const router = useRouter();
const isNew = route.params.id === "new";
const rec = ref<any>(null);
const item = ref<any>(null);
const lots = ref<any[]>([]);
const activities = ref<any[]>([]);
const equipment = ref<any[]>([]);
const busy = ref(false);
const pmode = ref<"dimensional" | "order" | "inspection">("inspection");
async function doPrint(mode: any = null) {
  if (mode !== null) pmode.value = mode;
  await nextTick();
  printForm();
}
const canWrite = hasRole(QualityRoles.recordWriters);
const isQc = hasRole(QualityRoles.inspectors);
// the date of a record is limited to the back-date window of the company (R1q D15, setting MaxBackdateDays): from that many days back to tomorrow; the API refuses a date
// outside it with its own Persian text, which is shown
const { min: dateMin, max: dateMax, hint: dateHint, warning: dateWarning, load: loadWindow } = useBackdateWindow(true);

function blank() {
  return {
    type: 1, workOrderId: null, itpActivityId: null, itemId: null, lotId: null, serialNumber: "", quantity: 1, operation: "", drawingNumber: "", drawingRevision: "",
    recordDate: today(), result: 1, attachmentDocumentId: null, note: "",
    measurements: [] as any[],
  };
}

const needsMeasurements = computed(() => rec.value && [3, 4].includes(rec.value.type));
const pass = (m: any) => {
  const measured = Number(m.measured), nominal = Number(m.nominal);
  if (m.measured === "" || m.measured === null || isNaN(measured)) return null;
  return measured >= nominal - Number(m.tolMinus || 0) && measured <= nominal + Number(m.tolPlus || 0);
};
const failedCount = computed(() => (rec.value?.measurements || []).filter((m: any) => (isNew ? pass(m) === false : m.pass === false)).length);

async function loadActivities() {
  activities.value = [];
  if (!rec.value.workOrderId) return;
  const [data] = await getItpByWorkOrder(rec.value.workOrderId);
  activities.value = (data?.activities || []).filter((a: any) => ![2, 3, 4].includes(a.status));
}

async function loadLots() {
  lots.value = [];
  rec.value.lotId = null;
  if (!item.value) return;
  const [data] = await getLots({ ItemId: item.value.id, PageSize: 200 });
  lots.value = rowsOf(data);
}

function addMeasurement() {
  rec.value.measurements.push({ characteristic: "", nominal: 0, tolPlus: 0, tolMinus: 0, measured: null, unit: "mm", equipmentId: null });
}

async function save() {
  const r = rec.value;
  busy.value = true;
  const [data, error] = await saveQcRecord({
    ...r,
    itemId: item.value?.id || null,
    quantity: Number(r.quantity) || 0,
    itpActivityId: r.itpActivityId || null,
    lotId: r.lotId || null,
    measurements: r.measurements.map((m: any) => ({
      ...m,
      nominal: Number(m.nominal) || 0,
      tolPlus: Number(m.tolPlus) || 0,
      tolMinus: Number(m.tolMinus) || 0,
      measured: Number(m.measured) || 0,
      equipmentId: m.equipmentId || null,
    })),
  });
  busy.value = false;
  if (error) return fail(error);
  ok(data.ncrId ? "ثبت شد؛ گزارش عدم انطباق به صورت پیش‌نویس ساخته شد." : "ثبت شد.");
  router.replace({ name: "QualityRecordEdit", params: { id: data.id } });
  rec.value = { ...data, recordDate: dateInput(data.recordDate) };
}

onMounted(async () => {
  loadWindow();
  const [eq] = await getEquipmentList({ PageSize: 300 });
  equipment.value = rowsOf(eq);
  if (isNew) {
    rec.value = blank();
    return;
  }
  const [data, error] = await getQcRecord(Number(route.params.id));
  if (error) return fail(error);
  rec.value = { ...data, recordDate: dateInput(data.recordDate) };
});
</script>

<template>
  <div v-if="rec" class="inv-page">
    <RecordPrint v-if="!isNew" :rec="rec" :kind="pmode" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">سابقه بازرسی <span v-if="rec.number" class="inv-ltr ms-2">{{ rec.number }}</span></h1>
          <span v-if="!isNew" :class="{ ok: rec.result === 1, bad: rec.result === 2, warn: rec.result > 2 }" class="inv-badge">{{ qcResults[rec.result] }}</span>
          <span v-if="rec.selfInspection" class="inv-badge info ms-1">خودبازرسی اپراتور</span>
        </div>
        <div class="d-flex gap-2">
          <router-link :to="{ name: 'QualityRecords' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="doPrint('inspection')">چاپ سابقه بازرسی</button>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="doPrint('dimensional')">چاپ DCF (FO-QC-14)</button>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="doPrint('order')">چاپ حکم بازرسی (FO-QC-018)</button>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="router.push({ name: 'QualityRecordEdit', params: { id: 'new' } })">بازرسی جدید</button>
          <button v-if="isNew && canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button>
        </div>
      </div>
      <p v-if="isNew && !isQc" class="inv-muted">فقط بازرس QC می‌تواند فعالیت‌های برنامه بازرسی (ITP) را ثبت کند؛ شما فقط خودبازرسی فعالیت‌های خارج از ITP را می‌توانید ثبت کنید.</p>

      <div class="row g-2">
        <div class="col-md-2">
          <label class="form-label small mb-0">نوع</label>
          <select v-model="rec.type" :disabled="!isNew" class="form-select form-select-sm"><option v-for="(t, k) in recordTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="rec.recordDate" :disabled="!isNew" :max="dateMax" :min="dateMin" class="form-control form-control-sm" type="date" />
          <span v-if="isNew && dateWarning(rec.recordDate)" class="small text-danger">{{ dateWarning(rec.recordDate) }}</span>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">دستور ساخت</label>
          <WorkOrderSelect v-if="isNew" v-model="rec.workOrderId" @change="loadActivities" />
          <div v-else class="small pt-1 inv-ltr">{{ rec.workOrderCode }}</div>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">فعالیت ITP</label>
          <select v-if="isNew" v-model="rec.itpActivityId" class="form-select form-select-sm">
            <option :value="null">— خارج از برنامه بازرسی —</option>
            <option v-for="a in activities" :key="a.id" :value="a.id">{{ a.itemNo || a.seq }} - {{ a.activity }} ({{ activityStatuses[a.status] }})</option>
          </select>
          <div v-else class="small pt-1">{{ rec.itpActivityId || "—" }}</div>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">نتیجه</label>
          <select v-model="rec.result" :disabled="!isNew" class="form-select form-select-sm"><option v-for="(t, k) in qcResults" :key="k" :value="Number(k)">{{ t }}</option></select>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">کالا / قطعه</label>
          <ItemPicker v-if="isNew" v-model="item" @select="loadLots" />
          <div v-else class="small pt-1"><span class="inv-ltr">{{ rec.itemCode }}</span> {{ rec.itemTitle }}</div>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">بچ</label>
          <select v-if="isNew" v-model="rec.lotId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="l in lots" :key="l.id" :value="l.id">{{ l.lotNumber }}</option></select>
          <div v-else class="small pt-1">{{ rec.lotId || "—" }}</div>
        </div>
        <div class="col-md-3"><label class="form-label small mb-0">سریال</label><input v-model="rec.serialNumber" :disabled="!isNew" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-1"><label class="form-label small mb-0">تعداد</label><input v-model.number="rec.quantity" :disabled="!isNew" class="form-control form-control-sm" type="number" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">عملیات / مرحله</label><input v-model="rec.operation" :disabled="!isNew" class="form-control form-control-sm" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">شماره نقشه</label><input v-model="rec.drawingNumber" :disabled="!isNew" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">بازنگری نقشه</label><input v-model="rec.drawingRevision" :disabled="!isNew" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">پیوست</label><DocAttach v-model="rec.attachmentDocumentId" :disabled="!isNew" title="پیوست بازرسی" /></div>
        <div class="col-md-12"><label class="form-label small mb-0">توضیح</label><input v-model="rec.note" :disabled="!isNew" class="form-control form-control-sm" /></div>
      </div>
      <p v-if="rec.ncrId" class="small mt-2 mb-0">گزارش عدم انطباق: <router-link :to="{ name: 'InventoryNcrEdit', params: { id: rec.ncrId } }" class="inv-ltr">{{ rec.ncrNumber }}</router-link></p>
      <p v-if="isNew" class="inv-muted mt-2 mb-0">اگر نتیجه «رد» ثبت شود، سیستم گزارش عدم انطباق را پیش‌نویس می‌کند. تجهیز اندازه‌گیری باید در تاریخ بازرسی کالیبره باشد.</p>
      <p v-if="isNew" class="inv-muted small mt-1 mb-0">{{ dateHint }}</p>
    </div>

    <div v-if="needsMeasurements || rec.measurements.length" class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">اندازه‌گیری‌ها <span v-if="failedCount" class="inv-badge bad ms-2">{{ failedCount }} مورد خارج از تلرانس</span></h2>
        <button v-if="isNew" class="btn btn-sm btn-outline-primary" @click="addMeasurement">ردیف جدید</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>مشخصه</th><th>اسمی</th><th>+ تلرانس</th><th>− تلرانس</th><th>اندازه‌گیری‌شده</th><th>واحد</th><th>تجهیز</th><th>نتیجه</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rec.measurements.length"><td colspan="9" class="text-center inv-muted">ردیفی نیست.</td></tr>
            <tr v-for="(m, i) in rec.measurements" :key="i">
              <template v-if="isNew">
                <td><input v-model="m.characteristic" class="form-control form-control-sm" /></td>
                <td><input v-model="m.nominal" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
                <td><input v-model="m.tolPlus" class="form-control form-control-sm inv-ltr" type="number" step="any" min="0" /></td>
                <td><input v-model="m.tolMinus" class="form-control form-control-sm inv-ltr" type="number" step="any" min="0" /></td>
                <td><input v-model="m.measured" class="form-control form-control-sm inv-ltr" type="number" step="any" /></td>
                <td><input v-model="m.unit" class="form-control form-control-sm inv-ltr" style="width: 70px" /></td>
                <td>
                  <select v-model="m.equipmentId" class="form-select form-select-sm">
                    <option :value="null">-</option>
                    <option v-for="e in equipment" :key="e.id" :value="e.id">{{ e.code }} - {{ e.name }}</option>
                  </select>
                </td>
                <td><span v-if="pass(m) !== null" :class="pass(m) ? 'ok' : 'bad'" class="inv-badge">{{ pass(m) ? "قبول" : "رد" }}</span></td>
                <td><button class="btn btn-sm btn-outline-danger" @click="rec.measurements.splice(i, 1)">حذف</button></td>
              </template>
              <template v-else>
                <td>{{ m.characteristic }}</td>
                <td class="inv-ltr">{{ num(m.nominal) }}</td>
                <td class="inv-ltr">+{{ num(m.tolPlus) }}</td>
                <td class="inv-ltr">-{{ num(m.tolMinus) }}</td>
                <td class="inv-ltr">{{ num(m.measured) }}</td>
                <td class="inv-ltr">{{ m.unit }}</td>
                <td class="inv-ltr">{{ m.equipmentCode }}</td>
                <td><span :class="m.pass ? 'ok' : 'bad'" class="inv-badge">{{ m.pass ? "قبول" : "رد" }}</span></td>
                <td></td>
              </template>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="isNew" class="inv-muted mb-0">نتیجه هر ردیف را سرور هنگام ثبت محاسبه می‌کند؛ نمایش بالا فقط پیش‌نمایش است. بازرسی ابعادی و نمونه اول حداقل یک ردیف لازم دارد.</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
