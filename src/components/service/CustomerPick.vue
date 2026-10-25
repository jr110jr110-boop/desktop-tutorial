<script setup lang="ts">
// Customer type-ahead. modelValue is the customer record { id, name } or null.
import LookupBox from "@/components/fn/LookupBox.vue";
import { customerLabel, searchCustomers } from "./svKit";

defineProps({ modelValue: { type: Object, default: null }, disabled: Boolean, fallbackText: { type: String, default: "" } });
const emit = defineEmits(["update:modelValue", "pick"]);
</script>

<template>
  <LookupBox :model-value="modelValue" :fetch="searchCustomers" :label="customerLabel" :disabled="disabled" :fallback-text="fallbackText" placeholder="نام مشتری"
    @update:model-value="emit('update:modelValue', $event)" @pick="emit('pick', $event)" />
</template>
