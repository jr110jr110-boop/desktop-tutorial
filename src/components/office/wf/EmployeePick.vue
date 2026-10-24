<script setup lang="ts">
// Type-ahead picker of an employee (directory); the model is the employee id.
import { ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { employeeLabel, searchEmployees } from "./lookups";

const props = defineProps({ modelValue: { type: Number, default: null }, fallbackText: { type: String, default: "" }, disabled: Boolean });
const emit = defineEmits(["update:modelValue"]);
const picked = ref<any>(null);
function onPick(e: any) {
  picked.value = e;
  emit("update:modelValue", e ? e.id : null);
}
</script>

<template>
  <LookupBox :model-value="picked" :fetch="searchEmployees" :label="employeeLabel" placeholder="جستجوی کارمند..." :disabled="disabled" :fallback-text="modelValue ? fallbackText || 'کارمند #' + modelValue : ''" @pick="onPick" />
</template>
