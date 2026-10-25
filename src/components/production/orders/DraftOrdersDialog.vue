<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { draftProductionOrders } from "@/api/ProductionAPIs";
import { getWorkOrders } from "@/api/InventoryAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { itemLabel, searchItems } from "../master/masterKit";

// Draft production orders by hand for a work order (the system normally drafts them itself). There is no work-order-to-process API in the front, so the free process id field was removed (all processes of the work order are drafted). The item is needed only when the process output code matches no item.
const emit = defineEmits(["close", "done"]);
const workOrders = ref<any[]>([]);
const f = ref({ workOrderId: null as number | null, item: null as any });
const result = ref<any>(null);
const busy = ref(false);

async function run() {
  if (!f.value.workOrderId) return fail("دستور کار را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await draftProductionOrders({ workOrderId: f.value.workOrderId, processId: null, itemId: f.value.item ? f.value.item.id : null });
  busy.value = false;
  if (error) return fail(error);
  result.value = camelize(d);
  ok(`${result.value?.drafted ?? 0} دستور تولید پیش‌نویس شد.`);
}
onMounted(async () => {
  const [w] = await getWorkOrders();
  workOrders.value = rowsOf(camelize(w));
});
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 560px">
      <h2 class="inv-title mb-2">پیش‌نویس دستور تولید از دستور کار</h2>
      <template v-if="!result">
        <div class="mb-2"><label class="form-label small mb-0">دستور کار</label><select v-model="f.workOrderId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option></select></div>
        <div class="mb-2"><label class="form-label small mb-0">کالای خروجی (فقط اگر کد خروجی فرایند با هیچ کالایی نمی‌خواند)</label><LookupBox v-model="f.item" :fetch="searchItems" :label="itemLabel" placeholder="کد یا نام کالا" /></div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="run">ساخت پیش‌نویس</button></div>
      </template>
      <template v-else>
        <p class="mb-1">ساخته‌شده: <b>{{ result.drafted }}</b> - ردشده: <b>{{ result.skipped }}</b></p>
        <ul v-if="result.notes?.length" class="small"><li v-for="(n, i) in result.notes" :key="i">{{ n }}</li></ul>
        <div class="text-end mt-3"><button class="btn btn-sm btn-primary" @click="emit('done')">بستن</button></div>
      </template>
    </div>
  </div>
</template>
