<script setup lang="ts">
// Picks the accepted re-inspection that proves a decision: an incoming re-inspection (QcInspection, made by the system when the reworked goods arrive in the quarantine
// warehouse), an inspection record (QcRecord) or an approved test report (TestReport). The value is { kind, recordId } as the API takes it (NCR execute / close,
// out-of-tolerance re-inspection). The list shows the latest accepted candidates; the id can also be typed. The API checks the match and answers in Persian.
import { computed, ref, watch } from "vue";
import { getInspections, rowsOf } from "@/api/InventoryAPIs";
import { getQcRecords, getTestReports } from "@/api/QualityAPIs";
import { jalali } from "@/composables/inventory";
import { reinspectionKinds, type ReinspectionValue } from "@/composables/quality";

const props = defineProps({
  modelValue: { type: Object as () => ReinspectionValue, required: true },
  /** the kinds the caller accepts, in the order shown */
  kinds: { type: Array as () => string[], default: () => ["QcInspection", "QcRecord", "TestReport"] },
  /** narrows records and reports to one work order */
  workOrderId: { type: Number, default: null },
  /** narrows incoming re-inspections to the ones the system made for this NCR */
  ncrId: { type: Number, default: null },
  disabled: Boolean,
});
const emit = defineEmits<{ (e: "update:modelValue", value: ReinspectionValue): void }>();

const candidates = ref<{ id: number; label: string }[]>([]);
const loading = ref(false);
let token = 0;

const kind = computed(() => props.modelValue.kind);
const recordId = computed(() => props.modelValue.recordId);

function set(next: Partial<ReinspectionValue>) {
  emit("update:modelValue", { kind: props.modelValue.kind, recordId: props.modelValue.recordId, ...next });
}

function onKind(event: Event) {
  const value = (event.target as HTMLSelectElement).value || null;
  set({ kind: value, recordId: null });
}

function onId(event: Event) {
  const raw = (event.target as HTMLInputElement | HTMLSelectElement).value;
  const id = Number(raw);
  set({ recordId: raw !== "" && Number.isFinite(id) && id > 0 ? Math.trunc(id) : null });
}

async function load() {
  const mine = ++token;
  candidates.value = [];
  if (!kind.value) return;
  loading.value = true;
  let rows: { id: number; label: string }[] = [];
  if (kind.value === "QcInspection") {
    const [data] = await getInspections({ Status: 1, PageSize: 200 });
    rows = rowsOf(data)
      .filter((r: any) => (props.ncrId ? r.reinspectionOfNcrId === props.ncrId : true) && Number(r.acceptedQuantity) > 0)
      .map((r: any) => ({ id: r.id, label: `${r.number} — ${r.itemCode}${r.lotNumber ? " — بچ " + r.lotNumber : ""} — ${jalali(r.inspectedAt)}` }));
  } else if (kind.value === "QcRecord") {
    const [data] = await getQcRecords({ WorkOrderId: props.workOrderId, Result: 1, PageSize: 100 });
    rows = rowsOf(data).map((r: any) => ({ id: r.id, label: `${r.number} — ${r.itemCode || "-"}${r.serialNumber ? " — سریال " + r.serialNumber : ""} — ${jalali(r.recordDate)}` }));
  } else if (kind.value === "TestReport") {
    const [data] = await getTestReports({ WorkOrderId: props.workOrderId, Status: 2, PageSize: 100 });
    rows = rowsOf(data)
      .filter((r: any) => r.result === 1)
      .map((r: any) => ({ id: r.id, label: `${r.number} — ${jalali(r.reportDate)}` }));
  }
  if (mine === token) {
    candidates.value = rows;
    loading.value = false;
  }
}

watch(() => [props.modelValue.kind, props.workOrderId, props.ncrId], load, { immediate: true });
</script>

<template>
  <div class="row g-2">
    <div class="col-md-4">
      <label class="form-label small mb-0">نوع بازرسی مجدد</label>
      <select :value="kind || ''" :disabled="disabled" class="form-select form-select-sm" @change="onKind">
        <option value="">-</option>
        <option v-for="k in kinds" :key="k" :value="k">{{ reinspectionKinds[k] || k }}</option>
      </select>
    </div>
    <div class="col-md-5">
      <label class="form-label small mb-0">سند قبول‌شده</label>
      <select :value="recordId ?? ''" :disabled="disabled || !kind" class="form-select form-select-sm" @change="onId">
        <option value="">{{ loading ? "در حال بارگذاری…" : candidates.length ? "انتخاب کنید" : "موردی پیدا نشد؛ شناسه را بنویسید" }}</option>
        <option v-for="c in candidates" :key="c.id" :value="c.id">{{ c.label }}</option>
      </select>
    </div>
    <div class="col-md-3">
      <label class="form-label small mb-0">یا شناسه سند</label>
      <input :value="recordId ?? ''" :disabled="disabled || !kind" class="form-control form-control-sm inv-ltr" min="1" type="number" @input="onId" />
    </div>
  </div>
</template>
