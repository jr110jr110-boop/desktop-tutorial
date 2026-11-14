<script setup lang="ts">
// Lines of a shipment: a part (item), one serial-numbered unit of the work order, or a free description. Rules of the server (PcReportService.BuildLinesAsync):
// quantity > 0; a serial line is one unit (quantity 1) and a serial is on one shipment only; an item line must be an item of this work order.
import { ref, watch } from "vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import { getItem, getSerials, rowsOf } from "@/api/InventoryAPIs";

const props = defineProps({ modelValue: { type: Array as () => any[], required: true }, workOrderId: { type: Number as () => number | null, default: null } });
const emit = defineEmits(["update:modelValue"]);
const serials = ref<any[]>([]);

async function loadSerials() {
  serials.value = [];
  if (!props.workOrderId) return;
  const [d] = await getSerials({ WorkOrderId: props.workOrderId, PageNumber: 1, PageSize: 500 });
  serials.value = rowsOf(d);
}
/** Names of the items already on the lines (the saved lines carry only the ids). */
async function loadLabels() {
  for (const l of props.modelValue) {
    if (l.itemId && !l.item) {
      const [it] = await getItem(l.itemId);
      if (it) l.item = { code: it.code, title: it.title };
    }
  }
}
watch(() => props.workOrderId, loadSerials, { immediate: true });
watch(() => props.modelValue.length, loadLabels, { immediate: true });

const kindOf = (l: any) => (l.serialUnitId ? "serial" : l.itemId ? "item" : "text");
function add(kind: string) {
  emit("update:modelValue", [...props.modelValue, { itemId: null, serialUnitId: null, quantity: 1, description: "", kind }]);
}
function drop(i: number) {
  emit("update:modelValue", props.modelValue.filter((_, k) => k !== i));
}
function pickItem(l: any, item: any) {
  l.itemId = item?.id ?? null;
  l.item = item ? { code: item.code, title: item.title } : null;
}
function pickSerial(l: any, id: string) {
  l.serialUnitId = id ? Number(id) : null;
  if (l.serialUnitId) {
    l.quantity = 1;
    const s = serials.value.find((x) => x.id === l.serialUnitId);
    if (s) {
      l.itemId = s.itemId;
      l.item = { code: s.itemCode, title: s.itemTitle };
    }
  }
}
const usedSerial = (id: number, self: any) => props.modelValue.some((l) => l !== self && l.serialUnitId === id);
</script>

<template>
  <div class="mb-2">
    <div class="d-flex gap-1 mb-1 align-items-center">
      <label class="form-label small mb-0 me-auto">اقلام محموله</label>
      <button type="button" class="btn btn-sm btn-outline-primary" @click="add('item')">+ کالا</button>
      <button type="button" class="btn btn-sm btn-outline-primary" @click="add('serial')">+ شماره سریال</button>
      <button type="button" class="btn btn-sm btn-outline-secondary" @click="add('text')">+ شرح</button>
    </div>
    <div v-if="!modelValue.length" class="small inv-muted">هنوز ردیفی نیست؛ بسته‌بندی بدون اقلام ممکن نیست.</div>
    <div v-for="(l, i) in modelValue" :key="i" class="row g-1 mb-1 align-items-start">
      <div class="col-md-5">
        <select v-if="l.kind === 'serial' || (!l.kind && kindOf(l) === 'serial')" :value="l.serialUnitId" class="form-select form-select-sm" @change="pickSerial(l, ($event.target as HTMLSelectElement).value)">
          <option value="">شماره سریال دستور ساخت…</option>
          <option v-for="s in serials" :key="s.id" :value="s.id" :disabled="usedSerial(s.id, l)">{{ s.serialNumber }} — {{ s.itemCode }}</option>
        </select>
        <template v-else-if="l.kind === 'item' || (!l.kind && kindOf(l) === 'item')">
          <ItemPicker :model-value="l.item || null" @update:model-value="pickItem(l, $event)" />
        </template>
        <span v-else class="small inv-muted">ردیف شرح آزاد</span>
      </div>
      <div class="col-md-2"><input v-model.number="l.quantity" :disabled="!!l.serialUnitId" type="number" min="0" step="any" class="form-control form-control-sm inv-ltr" placeholder="تعداد" /></div>
      <div class="col-md-4"><input v-model="l.description" class="form-control form-control-sm" placeholder="شرح" /></div>
      <div class="col-md-1 text-end"><button type="button" class="btn btn-sm btn-outline-danger" title="حذف ردیف" @click="drop(i)">×</button></div>
    </div>
    <div class="small inv-muted">کالای هر ردیف باید از اقلام همین دستور ساخت باشد؛ هر شماره سریال فقط در یک محموله می‌آید و تعداد آن یک است.</div>
  </div>
</template>
