<script setup lang="ts">
// A time of day field (HH:mm, 24 hours). Digits of any script are accepted; the value is "HH:mm" or "" while the text is not a time.
import { ref, watch } from "vue";
import { normalizeTime } from "./attCommon";

const props = defineProps({ modelValue: { type: String, default: "" }, disabled: Boolean, placeholder: { type: String, default: "08:00" } });
const emit = defineEmits(["update:modelValue"]);
const text = ref(props.modelValue || "");
const bad = ref(false);
watch(() => props.modelValue, (v) => { if (!bad.value || v) text.value = v || ""; });

function onInput(value: string) {
  text.value = value;
  if (value.trim() === "") { bad.value = false; emit("update:modelValue", ""); return; }
  const t = normalizeTime(value);
  bad.value = t === null;
  emit("update:modelValue", t || "");
}
function onBlur() {
  if (!bad.value) text.value = props.modelValue || "";
}
</script>

<template>
  <input :class="{ 'is-invalid': bad }" :disabled="disabled" :placeholder="placeholder" :value="text" class="form-control form-control-sm inv-ltr text-center" inputmode="numeric" maxlength="8" style="min-width: 80px" @blur="onBlur" @input="onInput(($event.target as HTMLInputElement).value)" />
</template>
