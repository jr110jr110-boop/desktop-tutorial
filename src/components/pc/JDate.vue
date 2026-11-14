<script setup lang="ts">
// A date field: typed Jalali entry (FnDate) with the same contract as before: v-model is yyyy-mm-dd or null; `max` is the latest date allowed.
import { ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { jalali } from "@/composables/inventory";

const props = defineProps({ modelValue: { type: String, default: null }, disabled: Boolean, /** latest date allowed (yyyy-mm-dd) */ max: { type: String, default: undefined } });
const emit = defineEmits(["update:modelValue"]);
const tooLate = ref("");
const remount = ref(0);

function onValue(v: string | null) {
  if (v && props.max && v > props.max.slice(0, 10)) {
    // not accepted: the field is rebuilt from the model so what is shown is what will be saved
    tooLate.value = `تاریخ نباید بعد از ${jalali(props.max)} باشد؛ مقدار قبلی برگردانده شد.`;
    remount.value++;
    return;
  }
  tooLate.value = "";
  emit("update:modelValue", v);
}
</script>

<template>
  <div>
    <FnDate :key="remount" :disabled="disabled" :model-value="modelValue ? modelValue.slice(0, 10) : null" @update:model-value="onValue" />
    <div v-if="tooLate" class="text-danger" style="font-size: 0.7rem" role="alert">{{ tooLate }}</div>
  </div>
</template>
