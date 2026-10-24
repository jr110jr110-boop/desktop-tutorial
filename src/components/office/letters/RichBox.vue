<script setup lang="ts">
// A small editor for the letter body: paragraphs, bold, lists. The value is sanitised HTML (v-model).
// Pasted text is inserted as plain text, so no foreign markup enters the letter.
import { onMounted, ref, watch } from "vue";
import { sanitizeHtml } from "./letterKit";

const props = defineProps({ modelValue: { type: String, default: "" }, disabled: Boolean, minHeight: { type: String, default: "14rem" } });
const emit = defineEmits(["update:modelValue"]);
const box = ref<HTMLElement | null>(null);
let last = "";

function render(value: string) {
  if (!box.value) return;
  last = sanitizeHtml(value);
  box.value.innerHTML = last;
}
function onInput() {
  if (!box.value) return;
  last = sanitizeHtml(box.value.innerHTML);
  emit("update:modelValue", last);
}
function onPaste(event: ClipboardEvent) {
  event.preventDefault();
  const text = event.clipboardData?.getData("text/plain") || "";
  document.execCommand("insertText", false, text);
}
function format(command: string) {
  box.value?.focus();
  document.execCommand(command, false);
  onInput();
}
onMounted(() => render(props.modelValue));
watch(() => props.modelValue, (v) => { if (sanitizeHtml(v) !== last) render(v); });
</script>

<template>
  <div class="rich-box">
    <div v-if="!disabled" class="rich-tools d-print-none">
      <button class="btn btn-sm btn-outline-secondary" title="پررنگ" type="button" @mousedown.prevent="format('bold')"><b>ب</b></button>
      <button class="btn btn-sm btn-outline-secondary" title="فهرست نقطه‌ای" type="button" @mousedown.prevent="format('insertUnorderedList')">• فهرست</button>
      <button class="btn btn-sm btn-outline-secondary" title="فهرست شماره‌دار" type="button" @mousedown.prevent="format('insertOrderedList')">۱. فهرست</button>
      <button class="btn btn-sm btn-outline-secondary" title="پاک‌کردن قالب" type="button" @mousedown.prevent="format('removeFormat')">پاک‌کردن قالب</button>
    </div>
    <div ref="box" :contenteditable="!disabled" :style="{ minHeight }" class="rich-area" dir="rtl" @input="onInput" @paste="onPaste"></div>
  </div>
</template>

<style scoped>
.rich-tools { display: flex; gap: 0.35rem; margin-bottom: 0.35rem; flex-wrap: wrap; }
.rich-area { border: 1px solid #ced4da; border-radius: 0.4rem; padding: 0.6rem 0.8rem; background: #fff; line-height: 2; outline: none; overflow-wrap: anywhere; }
.rich-area:focus { border-color: #86b7fe; box-shadow: 0 0 0 0.15rem rgba(13, 110, 253, 0.2); }
.rich-area[contenteditable="false"] { background: #f8f9fa; }
</style>
