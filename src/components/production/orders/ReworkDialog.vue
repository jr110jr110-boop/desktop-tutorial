<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getNcrs } from "@/api/InventoryAPIs";
import { reworkOperation } from "@/api/ProductionAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, jalali, ok } from "@/composables/inventory";

// Only NCRs whose disposition is rework (NcrDisposition 2) are listed.
// Makes a rework operation for an operation rejected in inspection. The NCR must belong to the same work order and have the "rework" disposition (the service checks both).
const props = defineProps<{ operation: any; workOrderCode: string; itemId: number | null }>();
const emit = defineEmits(["close", "done"]);
const ncrs = ref<any[]>([]);
const ncrId = ref<number | null>(props.operation.ncrId ?? null);
const note = ref("");
const busy = ref(false);

async function save() {
  if (!ncrId.value) return fail("گزارش عدم انطباق را انتخاب کنید.");
  busy.value = true;
  const [, error] = await reworkOperation({ operationId: props.operation.id, ncrId: ncrId.value, note: note.value.trim() || null });
  busy.value = false;
  if (error) return fail(error);
  ok("عملیات دوباره‌کاری ساخته شد.");
  emit("done");
}
onMounted(async () => {
  const [d] = await getNcrs({ ItemId: props.itemId, PageSize: 100 });
  ncrs.value = rowsOf(camelize(d)).filter((n: any) => n.disposition === 2 && (n.workOrderCode === props.workOrderCode || n.id === props.operation.ncrId));
});
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 560px">
      <h2 class="inv-title mb-2">دوباره‌کاری - مرحله {{ operation.sequence }} {{ operation.title }}</h2>
      <label class="form-label small mb-0">گزارش عدم انطباق</label>
      <select v-model="ncrId" class="form-select form-select-sm mb-2">
        <option :value="null">-</option>
        <option v-for="n in ncrs" :key="n.id" :value="n.id">{{ n.number }} - {{ jalali(n.reportDate) }} - {{ n.itemTitle }}</option>
      </select>
      <label class="form-label small mb-0">توضیح (اختیاری)</label>
      <textarea v-model="note" class="form-control form-control-sm" rows="2"></textarea>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ساخت دوباره‌کاری</button></div>
    </div>
  </div>
</template>
