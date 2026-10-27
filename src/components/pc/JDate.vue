<script setup lang="ts">
// A date field: the browser date input (yyyy-mm-dd) with the Jalali date shown under it.
import { computed } from "vue";
import { jalali } from "@/composables/inventory";

const props = defineProps({ modelValue: { type: String, default: null }, disabled: Boolean, /** latest date the browser lets the user pick (yyyy-mm-dd) */ max: { type: String, default: undefined } });
const emit = defineEmits(["update:modelValue"]);
const text = computed(() => (props.modelValue ? jalali(props.modelValue) : ""));
</script>

<template>
  <div>
    <input :disabled="disabled" :max="max" :value="modelValue ? modelValue.slice(0, 10) : ''" class="form-control form-control-sm" type="date" @input="emit('update:modelValue', ($event.target as HTMLInputElement).value || null)" />
    <div class="inv-muted" style="min-height: 1.1rem">{{ text }}</div>
  </div>
</template>
