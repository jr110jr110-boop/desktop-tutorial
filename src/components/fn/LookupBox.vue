<script setup lang="ts">
// Type-ahead picker of a record (account, detail account, customer, supplier, employee ...).
// modelValue is the picked record (or null); fetch(text) answers the matching records; label(record) is the text shown.
import { onBeforeUnmount, ref, watch } from "vue";

const props = defineProps({
  modelValue: { type: Object, default: null },
  fetch: { type: Function, required: true },
  label: { type: Function, required: true },
  placeholder: { type: String, default: "جستجو..." },
  disabled: Boolean,
  /** The text shown for a value that was loaded without the full record (for example only a code). */
  fallbackText: { type: String, default: "" },
});
const emit = defineEmits(["update:modelValue", "pick"]);
const text = ref("");
const items = ref<any[]>([]);
const open = ref(false);
let timer: any = null;

function shown(v: any) {
  return v ? (props.label as any)(v) : props.fallbackText || "";
}
watch(
  () => props.modelValue,
  (v) => {
    text.value = shown(v);
  },
  { immediate: true }
);

async function search(value: string) {
  const list = await (props.fetch as any)(value);
  items.value = Array.isArray(list) ? list : [];
  open.value = true;
}

function onInput(value: string) {
  text.value = value;
  if (timer) clearTimeout(timer);
  if (value.trim() === "") {
    emit("update:modelValue", null);
    emit("pick", null);
    items.value = [];
    open.value = false;
    return;
  }
  timer = setTimeout(() => search(value), 250);
}

function pick(item: any) {
  emit("update:modelValue", item);
  emit("pick", item);
  text.value = (props.label as any)(item);
  open.value = false;
}

function onFocus() {
  if (!props.disabled && !items.value.length) search(text.value === shown(props.modelValue) ? "" : text.value);
  else open.value = items.value.length > 0;
}

function onBlur() {
  setTimeout(() => {
    open.value = false;
    text.value = shown(props.modelValue);
  }, 180);
}

onBeforeUnmount(() => timer && clearTimeout(timer));
</script>

<template>
  <div class="fn-lookup">
    <input :disabled="disabled" :placeholder="placeholder" :value="text" class="form-control form-control-sm" @blur="onBlur" @focus="onFocus" @input="onInput(($event.target as HTMLInputElement).value)" />
    <ul v-if="open" class="fn-lookup-list">
      <li v-if="!items.length" class="inv-muted px-2">موردی پیدا نشد.</li>
      <li v-for="(item, i) in items" :key="item.id ?? i" @mousedown.prevent="pick(item)">{{ (label as any)(item) }}</li>
    </ul>
  </div>
</template>

<style scoped>
.fn-lookup {
  position: relative;
  min-width: 150px;
}
.fn-lookup-list {
  position: absolute;
  z-index: 30;
  right: 0;
  left: 0;
  margin: 0;
  padding: 0.2rem 0;
  list-style: none;
  max-height: 220px;
  overflow: auto;
  background: #fff;
  border: 1px solid #ced4da;
  border-radius: 0.3rem;
  box-shadow: 0 4px 10px rgba(0, 0, 0, 0.12);
  font-size: 0.8rem;
}
.fn-lookup-list li {
  padding: 0.25rem 0.6rem;
  cursor: pointer;
}
.fn-lookup-list li:hover {
  background: #eef3fb;
}
</style>
