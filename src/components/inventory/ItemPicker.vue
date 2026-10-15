<script setup lang="ts">
// Search a coded part by code, title or drawing number; Enter on an exact code (or a scanned label) selects it.
import { ref, watch } from "vue";
import { getItems, getItemByCode } from "@/api/InventoryAPIs";
import { parseScan, trackingModes } from "@/composables/inventory";

const props = defineProps({
  modelValue: { type: Object, default: null },
  placeholder: { type: String, default: "کد یا نام کالا" },
  activeOnly: { type: Boolean, default: true },
});
const emit = defineEmits(["update:modelValue", "select"]);

const text = ref(props.modelValue ? `${props.modelValue.code} - ${props.modelValue.title}` : "");
const results = ref<any[]>([]);
const open = ref(false);
let timer: any = null;

watch(
  () => props.modelValue,
  (v) => {
    text.value = v ? `${v.code} - ${v.title}` : "";
  }
);

function onInput() {
  open.value = true;
  clearTimeout(timer);
  timer = setTimeout(search, 300);
}

async function search() {
  const q = text.value.trim();
  if (q.length < 1) {
    results.value = [];
    return;
  }
  const [data] = await getItems({ Contains: q, PageSize: 15, IsActive: props.activeOnly ? true : null });
  results.value = data?.data || [];
}

function choose(item: any) {
  open.value = false;
  results.value = [];
  emit("update:modelValue", item);
  emit("select", item);
}

async function onEnter() {
  const scan = parseScan(text.value);
  const code = scan.kind === "item" || scan.kind === "serial" || scan.kind === "lot" ? scan.itemCode : scan.text;
  if (!code) return;
  const [item] = await getItemByCode(code);
  if (item) choose(item);
  else if (results.value.length === 1) choose(results.value[0]);
}

function onBlur() {
  setTimeout(() => (open.value = false), 200);
}

function clear() {
  text.value = "";
  emit("update:modelValue", null);
}
</script>

<template>
  <div class="position-relative">
    <div class="input-group input-group-sm">
      <input
        v-model="text"
        :placeholder="placeholder"
        class="form-control"
        type="text"
        @input="onInput"
        @keydown.enter.prevent="onEnter"
        @blur="onBlur"
        @focus="open = results.length > 0" />
      <button v-if="modelValue" class="btn btn-outline-secondary" type="button" @click="clear">×</button>
    </div>
    <ul
      v-if="open && results.length"
      class="list-group position-absolute w-100 shadow-sm"
      style="z-index: 1060; max-height: 260px; overflow-y: auto">
      <li
        v-for="item in results"
        :key="item.id"
        class="list-group-item list-group-item-action py-1 small"
        role="button"
        @mousedown.prevent="choose(item)">
        <span class="inv-ltr">{{ item.code }}</span>
        — {{ item.title }}
        <span class="inv-muted">({{ trackingModes[item.trackingMode] }}، موجودی {{ item.onHand }} {{ item.unit }})</span>
      </li>
    </ul>
  </div>
</template>
