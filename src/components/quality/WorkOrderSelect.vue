<script setup lang="ts">
// Work order drop-down (existing WorkOrders endpoint).
import { onMounted, ref } from "vue";
import { getWorkOrders, rowsOf } from "@/api/InventoryAPIs";

defineProps({ modelValue: { type: Number, default: null }, disabled: Boolean, placeholder: { type: String, default: "دستور ساخت" } });
const emit = defineEmits(["update:modelValue", "change"]);
const list = ref<any[]>([]);
onMounted(async () => {
  const [data] = await getWorkOrders();
  list.value = rowsOf(data);
});
function pick(v: string) {
  const value = v === "" ? null : Number(v);
  emit("update:modelValue", value);
  emit("change", value);
}
</script>

<template>
  <select :disabled="disabled" :value="modelValue" class="form-select form-select-sm" @change="pick(($event.target as HTMLSelectElement).value)">
    <option :value="null">{{ placeholder }}</option>
    <option v-for="w in list" :key="w.id" :value="w.id">{{ w.code }}</option>
  </select>
</template>
