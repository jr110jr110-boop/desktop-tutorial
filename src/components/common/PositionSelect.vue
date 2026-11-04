<script setup lang="ts">
// Picks a position (سمت) of the organisation chart; the list is read once for the whole app.
import { onMounted, ref } from "vue";
import { loadAllPositions, positionText } from "@/components/office/letters/letterKit";

defineProps({ modelValue: { type: Number, default: null }, disabled: Boolean, placeholder: { type: String, default: "-" } });
const emit = defineEmits(["update:modelValue"]);
const positions = ref<any[]>(positionsCache.value);
onMounted(async () => {
  if (!positionsCache.value.length) positionsCache.value = await loadAllPositions();
  positions.value = positionsCache.value;
});
const input = (v: string) => emit("update:modelValue", v === "" ? null : Number(v));
</script>
<script lang="ts">
import { ref as vueRef } from "vue";
const positionsCache = vueRef<any[]>([]);
</script>

<template>
  <select :disabled="disabled" :value="modelValue" class="form-select form-select-sm" @change="input(($event.target as HTMLSelectElement).value)">
    <option :value="null">{{ placeholder }}</option>
    <option v-for="p in positions" :key="p.id" :value="p.id">{{ positionText(p) }}</option>
  </select>
</template>
