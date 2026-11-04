<script setup lang="ts">
// A small modal that replaces the browser prompt(): a title, optional choices (buttons), an optional text box (a reason), confirm and cancel.
import { ref, watch } from "vue";

const props = defineProps<{
  modelValue: boolean;
  title: string;
  options?: { value: string; label: string }[];
  textLabel?: string;
  textRequired?: boolean;
}>();
const emit = defineEmits<{ (e: "update:modelValue", v: boolean): void; (e: "confirm", v: { choice: string; text: string }): void }>();

const choice = ref("");
const text = ref("");
watch(
  () => props.modelValue,
  (v) => {
    if (v) {
      choice.value = props.options?.[0]?.value || "";
      text.value = "";
    }
  },
);
const close = () => emit("update:modelValue", false);
function confirm() {
  if (props.textLabel && props.textRequired && !text.value.trim()) return;
  emit("confirm", { choice: choice.value, text: text.value.trim() });
  close();
}
</script>

<template>
  <div v-if="modelValue" class="pb6-dlg-backdrop" @click.self="close">
    <div class="pb6-dlg" role="dialog" aria-modal="true">
      <h2 class="h6">{{ title }}</h2>
      <div v-if="options?.length" class="d-flex flex-wrap gap-2 mb-2">
        <button v-for="o in options" :key="o.value" type="button" class="btn btn-sm" :class="choice === o.value ? 'btn-primary' : 'btn-outline-primary'" @click="choice = o.value">{{ o.label }}</button>
      </div>
      <textarea v-if="textLabel" v-model="text" class="form-control form-control-sm mb-2" rows="3" :placeholder="textLabel"></textarea>
      <div class="d-flex gap-2 justify-content-end">
        <button type="button" class="btn btn-sm btn-outline-secondary" @click="close">انصراف</button>
        <button type="button" class="btn btn-sm btn-primary" :disabled="!!textLabel && !!textRequired && !text.trim()" @click="confirm">تایید</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.pb6-dlg-backdrop { position: fixed; inset: 0; background: rgba(0, 0, 0, 0.4); display: flex; align-items: center; justify-content: center; z-index: 2000; padding: 16px; }
.pb6-dlg { background: var(--bs-body-bg, #fff); color: var(--bs-body-color, #212529); border-radius: 8px; padding: 16px; width: min(440px, 100%); }
</style>
