<script setup lang="ts">
// Work orders of the chosen contract (existing WorkOrders endpoint, filtered by contract).
import { ref, watch } from "vue";
import { getWorkOrders, rowsOf } from "@/api/InventoryAPIs";

const props = defineProps({ modelValue: { type: Number, default: null }, contractId: { type: Number, default: null }, disabled: Boolean, placeholder: { type: String, default: "همه دستورهای ساخت" } });
const emit = defineEmits(["update:modelValue", "change"]);
const list = ref<any[]>([]);
let all: any[] | null = null;

async function load() {
  if (!all) {
    const [data] = await getWorkOrders();
    all = rowsOf(data);
  }
  list.value = all.filter((w) => !props.contractId || w.contractId === props.contractId);
}
function pick(v: string) {
  const value = v === "" ? null : Number(v);
  emit("update:modelValue", value);
  emit("change", value);
}
watch(() => props.contractId, load, { immediate: true });
</script>

<template>
  <select :disabled="disabled" :value="modelValue" class="form-select form-select-sm" @change="pick(($event.target as HTMLSelectElement).value)">
    <option :value="null">{{ placeholder }}</option>
    <option v-for="w in list" :key="w.id" :value="w.id">{{ w.code }}</option>
  </select>
</template>
