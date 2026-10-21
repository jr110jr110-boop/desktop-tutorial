<script setup lang="ts">
// A Persian (Shamsi) date field. The value is yyyy-mm-dd (what the API takes); the text shown and typed is jYYYY/jMM/jDD.
// Only Persian dates are shown: no Gregorian date appears anywhere. Digits of any script are accepted.
import { ref, watch } from "vue";
import { jalali } from "@/composables/inventory";
import { jalaliToIso } from "@/composables/fn";

const props = defineProps({ modelValue: { type: String, default: null }, disabled: Boolean, placeholder: { type: String, default: "مثال ۱۴۰۵/۰۷/۱۵" } });
const emit = defineEmits(["update:modelValue"]);
const text = ref(props.modelValue ? jalali(props.modelValue) : "");
const bad = ref(false);

watch(
  () => props.modelValue,
  (v) => {
    const shown = v ? jalali(v) : "";
    if (!bad.value || v) text.value = shown;
  }
);

function onInput(value: string) {
  text.value = value;
  if (value.trim() === "") {
    bad.value = false;
    emit("update:modelValue", null);
    return;
  }
  const iso = jalaliToIso(value);
  bad.value = iso === null;
  if (iso !== null) emit("update:modelValue", iso);
}

function onBlur() {
  if (!bad.value) text.value = props.modelValue ? jalali(props.modelValue) : "";
}
</script>

<template>
  <input
    :class="{ 'is-invalid': bad }"
    :disabled="disabled"
    :placeholder="placeholder"
    :value="text"
    class="form-control form-control-sm inv-ltr fn-date"
    inputmode="numeric"
    maxlength="10"
    @blur="onBlur"
    @input="onInput(($event.target as HTMLInputElement).value)" />
</template>

<style scoped>
.fn-date {
  min-width: 110px;
  text-align: center;
}
</style>
