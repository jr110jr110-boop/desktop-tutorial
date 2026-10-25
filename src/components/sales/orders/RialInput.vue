<script setup lang="ts">
// Rial amount field: shows thousands separators while the model stays a plain number (null when empty). Persian/Arabic digits are accepted.
import { ref, watch } from "vue";
import { rial } from "@/composables/sales";

const props = defineProps({ modelValue: { type: Number, default: null }, disabled: Boolean, placeholder: { type: String, default: "ریال" } });
const emit = defineEmits(["update:modelValue"]);
const text = ref(props.modelValue === null || props.modelValue === undefined ? "" : rial(props.modelValue));

watch(
  () => props.modelValue,
  (v) => {
    const shown = v === null || v === undefined ? "" : rial(v);
    if (shown !== text.value && (parse(text.value) ?? null) !== (v ?? null)) text.value = shown;
  }
);

function parse(s: string): number | null {
  const latin = s
    .replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d)))
    .replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d)))
    .replace(/[^0-9]/g, "");
  return latin === "" ? null : Number(latin);
}

function onInput(e: Event) {
  const v = parse((e.target as HTMLInputElement).value);
  text.value = v === null ? "" : rial(v);
  emit("update:modelValue", v);
}
</script>

<template>
  <input :value="text" :disabled="disabled" :placeholder="placeholder" class="form-control form-control-sm inv-ltr" inputmode="numeric" type="text" @input="onInput" />
</template>
