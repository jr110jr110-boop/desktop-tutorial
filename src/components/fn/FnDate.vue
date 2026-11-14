<script lang="ts">
export default { inheritAttrs: false };
</script>

<script setup lang="ts">
// A Persian (Shamsi) date field. The value is yyyy-mm-dd (what the API takes); the text shown and typed is jYYYY/jMM/jDD.
// Only Persian dates are shown: no Gregorian date appears anywhere. Digits of any script are accepted.
import { onBeforeUnmount, ref, watch } from "vue";
import { jalali } from "@/composables/inventory";
import { jalaliToIso } from "@/composables/fn";

const props = defineProps({ modelValue: { type: String, default: null }, disabled: Boolean, placeholder: { type: String, default: "مثال ۱۴۰۵/۰۷/۱۵" } });
const emit = defineEmits(["update:modelValue"]);
const text = ref(props.modelValue ? jalali(props.modelValue) : "");
const bad = ref(false);
// Short notice shown when an invalid text was given back to the last valid value (what is shown is what will be saved).
const notice = ref("");
let noticeTimer: ReturnType<typeof setTimeout> | undefined;
onBeforeUnmount(() => clearTimeout(noticeTimer));

watch(
  () => props.modelValue,
  (v) => {
    const shown = v ? jalali(v) : "";
    if (!bad.value || v) text.value = shown;
  }
);

function onInput(value: string) {
  text.value = value;
  notice.value = "";
  if (value.trim() === "") {
    bad.value = false;
    emit("update:modelValue", null);
    return;
  }
  const iso = jalaliToIso(value);
  bad.value = iso === null;
  if (iso !== null) emit("update:modelValue", iso);
}

// Blur or Enter: an invalid text is replaced by the last valid value (empty when there was none) and the user is told.
function onBlur() {
  if (bad.value) {
    bad.value = false;
    notice.value = "تاریخ نامعتبر است؛ مقدار قبلی برگردانده شد.";
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(() => (notice.value = ""), 6000);
  }
  text.value = props.modelValue ? jalali(props.modelValue) : "";
}
</script>

<template>
  <div class="fn-date-wrap" v-bind="$attrs">
    <input
      :class="{ 'is-invalid': bad }"
      :disabled="disabled"
      :placeholder="placeholder"
      :value="text"
      class="form-control form-control-sm inv-ltr fn-date"
      inputmode="numeric"
      maxlength="10"
      @blur="onBlur"
      @keydown.enter="onBlur"
      @input="onInput(($event.target as HTMLInputElement).value)" />
    <div v-if="notice" class="fn-date-msg" role="alert">{{ notice }}</div>
  </div>
</template>

<style scoped>
.fn-date-wrap {
  min-width: 110px;
}
.fn-date-msg {
  color: var(--bs-danger, #dc3545);
  font-size: 0.7rem;
  line-height: 1.15;
  margin-top: 1px;
}
.fn-date {
  min-width: 110px;
  text-align: center;
}
</style>
