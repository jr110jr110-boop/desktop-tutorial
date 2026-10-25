<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getDocuments } from "@/api/InventoryAPIs";
import { operationAction } from "@/api/ProductionAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { documentStatuses, fail, jalali, ok } from "@/composables/inventory";

// Completes an outside operation with the confirmed "return from contractor" document (InventoryDocumentType 10) of the same work order.
// The service checks contractor, warehouse, the job number in the reference, confirmation of both documents and the incoming inspection.
const SUBCONTRACT_RETURN = 10;
const props = defineProps<{ operation: any; workOrderId: number }>();
const emit = defineEmits(["close", "done"]);
const docs = ref<any[]>([]);
const documentId = ref<number | null>(null);
const busy = ref(false);
const loaded = ref(false);

async function save() {
  if (!documentId.value) return fail("سند برگشت از پیمانکار را انتخاب کنید.");
  busy.value = true;
  const [, error] = await operationAction({ id: props.operation.id, action: "complete-outside", documentId: documentId.value });
  busy.value = false;
  if (error) return fail(error);
  ok("عملیات بیرونی پایان یافت.");
  emit("done");
}
onMounted(async () => {
  const [d] = await getDocuments({ Type: SUBCONTRACT_RETURN, WorkOrderId: props.workOrderId, PageSize: 100 });
  docs.value = rowsOf(camelize(d));
  loaded.value = true;
});
const label = (d: any) => `${d.number || "پیش‌نویس (بدون شماره)"} - ${jalali(d.documentDate)} - ${documentStatuses[d.status] ?? ""}${d.providerName ? " - " + d.providerName : ""}${d.referenceNumber ? " - مرجع: " + d.referenceNumber : ""}`;
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 620px">
      <h2 class="inv-title mb-2">پایان کار بیرونی - مرحله {{ operation.sequence }} {{ operation.title }}</h2>
      <p class="inv-muted">مرجع سند برگشت باید شماره برگه کار (<span class="inv-ltr">{{ operation.jobCard }}</span>) یا شماره حواله ارسال به پیمانکار باشد.</p>
      <label class="form-label small mb-0">سند برگشت از پیمانکار</label>
      <select v-model="documentId" class="form-select form-select-sm">
        <option :value="null">{{ loaded && !docs.length ? "سند برگشتی برای این دستور کار نیست" : "-" }}</option>
        <option v-for="d in docs" :key="d.id" :value="d.id">{{ label(d) }}</option>
      </select>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">پایان کار بیرونی</button></div>
    </div>
  </div>
</template>
