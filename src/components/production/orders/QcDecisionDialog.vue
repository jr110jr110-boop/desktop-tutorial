<script setup lang="ts">
import AttachmentPanel from "@/components/common/AttachmentPanel.vue";
import { AttachmentOwner } from "@/composables/attachments";
import { computed, onMounted, ref } from "vue";
import { getEquipmentList, getItpByWorkOrder, getPartCharacteristics } from "@/api/QualityAPIs";
import { qcDecision } from "@/api/ProductionAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, ok } from "@/composables/inventory";
import { testTypes } from "@/composables/quality";
import { qcResults, qtyText } from "@/composables/production";

// Decision of the QC inspector on an operation that waits for inspection. The quantity inspected is taken by the service (good quantity of the operation, or the order quantity): the API has no quantity field.
const props = defineProps<{ operation: any; quantity: number; workOrderId?: number | null; itemId?: number | null }>();
const emit = defineEmits(["close", "done"]);
const result = ref<1 | 2 | 3 | 4>(1);
const note = ref("");
const busy = ref(false);
// ITP point operation: the activities of the work order ITP with the same test type; the chosen one is sent as itpActivityId (empty = the server picks by test type)
const activities = ref<any[]>([]);
const activityId = ref<number | null>(props.operation.itpActivityId ?? null);

// R1p D16: the measured values behind the decision; every value needs its calibrated instrument (the service refuses a value without one)
const instruments = ref<any[]>([]);
const measurements = ref<any[]>([]);
// Package B: the characteristics come from the released list of the part's drawing; the inspector picks one and types only the measured value (nominal, tolerance and pass/fail are the server's)
const part = ref<any>(null);
const lines = computed<any[]>(() => part.value?.lines || []);
const lineOf = (m: any) => lines.value.find((l: any) => l.id === m.characteristicId);
const instrumentsFor = (m: any) => {
  const l = lineOf(m);
  return !l || l.instrumentClass === 99 ? instruments.value : instruments.value.filter((e: any) => e.type === l.instrumentClass);
};
const addMeasurement = () => measurements.value.push({ characteristicId: null, measured: null, instrumentId: null });
const addRequired = () => {
  for (const l of lines.value.filter((x: any) => x.required)) if (!measurements.value.some((m) => m.characteristicId === l.id)) measurements.value.push({ characteristicId: l.id, measured: null, instrumentId: null });
};

onMounted(async () => {
  const [eq] = await getEquipmentList({ PageSize: 300 });
  instruments.value = rowsOf(eq);
  if (props.itemId) {
    const [pc] = await getPartCharacteristics(props.itemId);
    part.value = pc;
  }
  if (props.operation.inspectionRequirement !== 3 || !props.workOrderId) return;
  const [data] = await getItpByWorkOrder(props.workOrderId);
  const type = props.operation.itpTestType;
  activities.value = (data?.activities || []).filter((a: any) => type == null || a.testType === type);
  if (!activityId.value && activities.value.length === 1) activityId.value = activities.value[0].id;
});

async function save() {
  if (result.value !== 1 && !note.value.trim()) return fail("برای نتیجه غیر از «قبول»، توضیح بنویسید.");
  for (const m of measurements.value) {
    if (!m.characteristicId || m.measured === null || m.measured === "") return fail("برای هر اندازه‌گیری، مشخصه از فهرست نقشه و مقدار اندازه‌گیری‌شده را وارد کنید.");
    if (!m.instrumentId) return fail("برای هر مقدار اندازه‌گیری‌شده، ابزار اندازه‌گیری کالیبره الزامی است.");
  }
  busy.value = true;
  const [, error] = await qcDecision({
    operationId: props.operation.id, result: result.value, note: note.value.trim() || null, itpActivityId: activityId.value,
    measurements: measurements.value.map((m) => ({ characteristicId: m.characteristicId, measured: Number(m.measured), instrumentId: m.instrumentId })),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("نتیجه بازرسی ثبت شد.");
  emit("done");
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 520px">
      <h2 class="inv-title mb-2">تصمیم بازرسی - مرحله {{ operation.sequence }} {{ operation.title }}</h2>
      <AttachmentPanel :owner-type="AttachmentOwner.ProductionOperation" :owner-id="operation.id" title="عکس، برگه اندازه‌گیری و گزارش بازرسی" />
      <p class="inv-muted">مقدار بازرسی‌شده: {{ qtyText(operation.goodQty > 0 ? operation.goodQty : quantity) }}</p>
      <div v-if="operation.inspectionRequirement === 3" class="mb-2">
        <label class="form-label small mb-0">نقطه ITP<span v-if="operation.itpTestType"> ({{ testTypes[operation.itpTestType] }})</span></label>
        <select v-model="activityId" class="form-select form-select-sm">
          <option :value="null">انتخاب خودکار بر اساس نوع آزمون</option>
          <option v-for="a in activities" :key="a.id" :value="a.id">{{ a.itemNo }} - {{ a.activity }}</option>
        </select>
      </div>
      <div class="d-flex flex-wrap gap-3 mb-2">
        <label v-for="(t, k) in qcResults" :key="k" class="form-check"><input v-model="result" :value="Number(k)" class="form-check-input" type="radio" /> <span class="form-check-label">{{ t }}</span></label>
      </div>
      <div class="mb-2">
        <div class="d-flex justify-content-between"><label class="form-label small mb-0">اندازه‌گیری‌ها (از فهرست مشخصه‌های نقشه)</label><span><button v-if="lines.length" class="btn btn-sm btn-outline-secondary me-1" @click="addRequired">مشخصه‌های الزامی</button><button v-if="lines.length" class="btn btn-sm btn-outline-secondary" @click="addMeasurement">افزودن مقدار</button></span></div>
        <p v-if="part && !lines.length" class="small inv-muted mb-0">{{ part.message || "برای این قطعه فهرست مشخصه منتشرشده‌ای نیست؛ مقدار اندازه‌گیری‌شده ثبت نمی‌شود." }}</p>
        <div v-for="(m, i) in measurements" :key="i" class="row g-1 mt-1">
          <div class="col-6"><select v-model="m.characteristicId" class="form-select form-select-sm" @change="m.instrumentId = null"><option :value="null">— مشخصه —</option><option v-for="l in lines" :key="l.id" :value="l.id">{{ l.no }} — {{ l.name }}</option></select></div>
          <div class="col-3 small inv-ltr align-self-center">{{ lineOf(m) ? lineOf(m).nominal + " [" + lineOf(m).lower + " … " + lineOf(m).upper + "] " + (lineOf(m).unit || "") : "" }}</div>
          <div class="col-3"><input v-model="m.measured" class="form-control form-control-sm inv-ltr" type="number" step="any" placeholder="اندازه‌گیری" /></div>
          <div class="col-10"><select v-model="m.instrumentId" class="form-select form-select-sm"><option :value="null">ابزار اندازه‌گیری (الزامی)</option><option v-for="e in instrumentsFor(m)" :key="e.id" :value="e.id">{{ e.code }} - {{ e.title || e.name }}</option></select></div>
          <div class="col-2"><button class="btn btn-sm btn-outline-danger w-100" @click="measurements.splice(i, 1)">حذف</button></div>
        </div>
      </div>
      <label class="form-label small mb-0">توضیح {{ result === 1 ? "(اختیاری)" : "(الزامی)" }}</label>
      <textarea v-model="note" class="form-control form-control-sm" rows="3"></textarea>
      <p v-if="result === 2 || result === 3" class="inv-muted mt-2 mb-0">برای رد یا دوباره‌کاری، گزارش عدم انطباق به صورت خودکار ساخته می‌شود. بازرس نمی‌تواند روی کاری که خودش ثبت زمان کرده تصمیم بگیرد.</p>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت نتیجه</button></div>
    </div>
  </div>
</template>
