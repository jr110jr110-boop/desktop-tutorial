<script setup lang="ts">
// Picks a contract / project (optional). The list is loaded once and shared.
import { onMounted } from "vue";
import { getContracts, rowsOf } from "@/api/InventoryAPIs";
import { contractsCache } from "@/composables/projectControl";

defineProps({ modelValue: { type: Number, default: null }, disabled: Boolean, placeholder: { type: String, default: "همه / بدون پروژه" } });
const emit = defineEmits(["update:modelValue"]);

onMounted(async () => {
  if (contractsCache.value.length) return;
  const [data] = await getContracts();
  contractsCache.value = rowsOf(data);
});
const input = (v: string) => emit("update:modelValue", v === "" ? null : Number(v));
</script>

<template>
  <select :disabled="disabled" :value="modelValue ?? ''" class="form-select form-select-sm" @change="input(($event.target as HTMLSelectElement).value)">
    <option value="">{{ placeholder }}</option>
    <option v-for="c in contractsCache" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option>
  </select>
</template>
