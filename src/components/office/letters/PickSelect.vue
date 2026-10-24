<script setup lang="ts">
// A select with a filter box above it (positions and parties lists are long). v-model is the selected value or null.
import { computed, ref } from "vue";

const props = defineProps({
  modelValue: { type: [Number, String, null] as any, default: null },
  options: { type: Array as () => { value: any; text: string }[], default: () => [] },
  placeholder: { type: String, default: "انتخاب..." },
  disabled: Boolean,
});
const emit = defineEmits(["update:modelValue"]);
const filter = ref("");
const shown = computed(() => {
  const q = filter.value.trim();
  if (!q) return props.options;
  return props.options.filter((o) => o.value === props.modelValue || o.text.includes(q));
});

function onChange(event: Event) {
  const raw = (event.target as HTMLSelectElement).value;
  const found = props.options.find((o) => String(o.value) === raw);
  emit("update:modelValue", found ? found.value : null);
}
</script>

<template>
  <div class="pick-select">
    <input v-if="options.length > 8 && !disabled" v-model="filter" class="form-control form-control-sm mb-1" placeholder="فیلتر..." />
    <select :disabled="disabled" :value="modelValue" class="form-select form-select-sm" @change="onChange">
      <option value="">{{ placeholder }}</option>
      <option v-for="o in shown" :key="o.value" :value="o.value">{{ o.text }}</option>
    </select>
  </div>
</template>
