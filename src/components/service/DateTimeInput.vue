<script setup lang="ts">
// Persian date + time field. The value is an ISO date-time (yyyy-mm-ddTHH:mm:00) or null.
import { ref, watch } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { joinDateTime } from "./svKit";

const props = defineProps({ modelValue: { type: String, default: null }, disabled: Boolean });
const emit = defineEmits(["update:modelValue"]);
const split = (v: string | null) => {
  const m = /^(\d{4}-\d{2}-\d{2})(?:T(\d{2}:\d{2}))?/.exec(String(v || ""));
  return { date: m ? m[1] : null, time: m ? m[2] || "" : "" };
};
const date = ref<string | null>(split(props.modelValue).date);
const time = ref(split(props.modelValue).time);

watch(
  () => props.modelValue,
  (v) => {
    const s = split(v);
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
