<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getItpByWorkOrder } from "@/api/QualityAPIs";
import { qcDecision } from "@/api/ProductionAPIs";
import { fail, ok } from "@/composables/inventory";
import { testTypes } from "@/composables/quality";
import { qcResults, qtyText } from "@/composables/production";

// Decision of the QC inspector on an operation that waits for inspection. The quantity inspected is taken by the service (good quantity of the operation, or the order quantity): the API has no quantity field.
const props = defineProps<{ operation: any; quantity: number; workOrderId?: number | null }>();
const emit = defineEmits(["close", "done"]);
const result = ref<1 | 2 | 3 | 4>(1);
const note = ref("");
const busy = ref(false);
// ITP point operation: the activities of the work order ITP with the same test type; the chosen one is sent as itpActivityId (empty = the server picks by test type)
const activities = ref<any[]>([]);
const activityId = ref<number | null>(props.operation.itpActivityId ?? null);

onMounted(async () => {
  if (props.operation.inspectionRequirement !== 3 || !props.workOrderId) return;
  const [data] = await getItpByWorkOrder(props.workOrderId);
  const type = props.operation.itpTestType;
  activities.value = (data?.activities || []).filter((a: any) => type == null || a.testType === type);
  if (!activityId.value && activities.value.length === 1) activityId.value = activities.value[0].id;
});

async function save() {
  if (result.value !== 1 && !note.value.trim()) return fail("برای نتیجه غیر از «قبول»، توضیح بنویسید.");
  busy.value = true;
  const [, error] = await qcDecision({ operationId: props.operation.id, result: result.value, note: note.value.trim() || null, itpActivityId: activityId.value });
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
      <label class="form-label small mb-0">توضیح {{ result === 1 ? "(اختیاری)" : "(الزامی)" }}</label>
      <textarea v-model="note" class="form-control form-control-sm" rows="3"></textarea>
      <p v-if="result === 2 || result === 3" class="inv-muted mt-2 mb-0">برای رد یا دوباره‌کاری، گزارش عدم انطباق به صورت خودکار ساخته می‌شود. بازرس نمی‌تواند روی کاری که خودش ثبت زمان کرده تصمیم بگیرد.</p>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت نتیجه</button></div>
    </div>
  </div>
</template>
