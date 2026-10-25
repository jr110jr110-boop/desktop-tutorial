<script setup lang="ts">
// Installed unit type-ahead. modelValue is the unit record { id, tag, title } or null.
import LookupBox from "@/components/fn/LookupBox.vue";
import { searchUnits, unitLabel } from "./svKit";

defineProps({ modelValue: { type: Object, default: null }, disabled: Boolean, fallbackText: { type: String, default: "" } });
const emit = defineEmits(["update:modelValue", "pick"]);
</script>

<template>
  <LookupBox :model-value="modelValue" :fetch="searchUnits" :label="unitLabel" :disabled="disabled" :fallback-text="fallbackText" placeholder="شماره tag یا عنوان واحد"
    @update:model-value="emit('update:modelValue', $event)" @pick="emit('pick', $event)" />
</template>
