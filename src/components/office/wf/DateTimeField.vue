<script setup lang="ts">
// Persian date (FnDate) + time field. The model is the local date-time text "yyyy-MM-ddTHH:mm:00" the API takes; the date is shown in Jalali.
import { computed } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { joinDateTime } from "./lookups";

const props = defineProps({ modelValue: { type: String, default: null }, disabled: Boolean });
const emit = defineEmits(["update:modelValue"]);
const date = computed(() => (props.modelValue ? String(props.modelValue).slice(0, 10) : null));
const time = computed(() => (props.modelValue && /T(\d{2}:\d{2})/.test(props.modelValue) ? /T(\d{2}:\d{2})/.exec(props.modelValue)![1] : ""));
const setDate = (d: string | null) => emit("update:modelValue", joinDateTime(d, time.value || "09:00"));
const setTime = (t: string) => emit("update:modelValue", date.value ? joinDateTime(date.value, t) : null);
</script>

<template>
  <div class="d-flex gap-1">
    <FnDate :model-value="date" :disabled="disabled" @update:model-value="setDate" />
    <input :value="time" :disabled="disabled || !date" type="time" class="form-control form-control-sm inv-ltr" style="max-width: 7rem" @input="setTime(($event.target as HTMLInputElement).value)" />
  </div>
</template>
