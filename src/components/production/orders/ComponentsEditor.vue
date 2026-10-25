<script setup lang="ts">
import { ref, watch } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { saveComponents } from "@/api/ProductionAPIs";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { qtyText } from "@/composables/production";
import { itemLabel, searchItems } from "../master/masterKit";

// Consumed components of the order (the kit). Editable only while the order is a draft, by the planners; the list is replaced as a whole.
const props = defineProps<{ order: any; canEdit: boolean }>();
const emit = defineEmits<{ (e: "changed", order: any): void }>();
const editing = ref(false);
const lines = ref<{ item: any; quantity: number }[]>([]);
const serials = ref("");
const busy = ref(false);

function start() {
  lines.value = (props.order.components || []).map((c: any) => ({ item: { id: c.itemId, code: c.itemCode, title: c.itemTitle }, quantity: c.quantity }));
  serials.value = props.order.serialsPlanned || "";
  editing.value = true;
}
async function save() {
  if (lines.value.some((l) => !l.item)) return fail("برای هر ردیف کالا را انتخاب کنید.");
  if (lines.value.some((l) => !(Number(l.quantity) > 0))) return fail("مقدار هر قلم باید بیشتر از صفر باشد.");
  busy.value = true;
  const [d, error] = await saveComponents({ orderId: props.order.id, components: lines.value.map((l) => ({ itemId: l.item.id, quantity: Number(l.quantity) })), serialsPlanned: serials.value.trim() || null, rowVersion: props.order.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok("اقلام مصرفی ذخیره شد.");
  editing.value = false;
  emit("changed", camelize(d));
}
watch(() => props.order.id, () => (editing.value = false));
</script>

<template>
  <div class="inv-card">
    <div class="inv-toolbar">
      <h2 class="inv-title">اقلام مصرفی (کیت)</h2>
      <button v-if="canEdit && !editing" class="btn btn-sm btn-outline-primary" @click="start">ویرایش اقلام</button>
    </div>
    <template v-if="!editing">
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>کالا</th><th>مقدار</th></tr></thead>
          <tbody>
            <tr v-if="!order.components?.length"><td colspan="2" class="text-center inv-muted">قلم مصرفی ثبت نشده است.</td></tr>
            <tr v-for="c in order.components" :key="c.itemId"><td><span class="inv-ltr">{{ c.itemCode }}</span> {{ c.itemTitle }}</td><td>{{ qtyText(c.quantity) }} {{ c.unit }}</td></tr>
          </tbody>
        </table>
      </div>
      <p v-if="order.serialsPlanned" class="small mb-0"><b>سریال‌های برنامه‌ریزی‌شده:</b> <span class="inv-ltr">{{ order.serialsPlanned }}</span></p>
    </template>
    <template v-else>
      <div v-for="(l, i) in lines" :key="i" class="row g-2 mb-1 align-items-center">
        <div class="col-md-7"><LookupBox v-model="l.item" :fetch="searchItems" :label="itemLabel" placeholder="کد یا نام کالا" /></div>
        <div class="col-8 col-md-3"><input v-model.number="l.quantity" class="form-control form-control-sm" type="number" min="0" step="any" /></div>
        <div class="col-4 col-md-2"><button class="btn btn-sm btn-outline-danger" @click="lines.splice(i, 1)">حذف</button></div>
      </div>
      <button class="btn btn-sm btn-outline-secondary mb-2" @click="lines.push({ item: null, quantity: 1 })">افزودن قلم</button>
      <div class="mb-2"><label class="form-label small mb-0">سریال‌های برنامه‌ریزی‌شده</label><input v-model="serials" class="form-control form-control-sm inv-ltr" /></div>
      <div class="d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره اقلام</button><button class="btn btn-sm btn-outline-secondary" @click="editing = false">انصراف</button></div>
    </template>
  </div>
</template>
