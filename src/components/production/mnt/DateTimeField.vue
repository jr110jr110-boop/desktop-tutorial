<script setup lang="ts">
// Persian date + time field. The value is an ISO date-time (yyyy-mm-ddTHH:mm:00) or null; people read and type the Persian date only.
import { ref, watch } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { joinDateTime, splitDateTime } from "./mntCommon";

const props = defineProps({ modelValue: { type: String, default: null }, disabled: Boolean });
const emit = defineEmits(["update:modelValue"]);
const date = ref<string | null>(splitDateTime(props.modelValue).date);
const time = ref(splitDateTime(props.modelValue).time);

watch(
  () => props.modelValue,
  (v) => {
    const s = splitDateTime(v);
    if (s.date !== date.value) date.value = s.date;
    if (s.time !== time.value && (s.date || !date.value)) time.value = s.time;
  }
);
function push() {
  emit("update:modelValue", joinDateTime(date.value, time.value));
}
watch(date, push);
watch(time, push);
</script>

<template>
  <div class="d-flex gap-1">
    <FnDate v-model="date" :disabled="disabled" />
    <input v-model="time" :disabled="disabled" class="form-control form-control-sm inv-ltr" style="max-width: 6rem" type="time" />
  </div>
</template>
