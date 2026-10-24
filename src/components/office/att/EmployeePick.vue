<script setup lang="ts">
// Employee picker over the employee directory (name, job title). v-model is the picked record {id, name, jobTitle} or null.
import LookupBox from "@/components/fn/LookupBox.vue";
import { employeeLabel, searchEmployees } from "./attCommon";

defineProps({ modelValue: { type: Object, default: null }, placeholder: { type: String, default: "نام یا عنوان شغلی" }, disabled: Boolean });
const emit = defineEmits(["update:modelValue"]);
</script>

<template>
  <LookupBox :model-value="modelValue" :disabled="disabled" :fetch="searchEmployees" :label="employeeLabel" :placeholder="placeholder" @update:model-value="emit('update:modelValue', $event)" />
</template>
