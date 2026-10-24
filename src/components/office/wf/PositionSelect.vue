<script setup lang="ts">
// Select of a position ("title - unit"). Loads the positions once (cached in lookups.ts).
import { computed, onMounted, ref } from "vue";
import { loadPositions } from "./lookups";

const props = defineProps({
  modelValue: { type: Number, default: null },
  placeholder: { type: String, default: "انتخاب سمت" },
  /** hide inactive positions (the saved one stays visible) */
  activeOnly: { type: Boolean, default: true },
  signersOnly: Boolean,
  /** only these ids (for example my positions) */
  only: { type: Array as () => number[] | null, default: null },
  disabled: Boolean,
});
const emit = defineEmits(["update:modelValue"]);
const all = ref<any[]>([]);
const options = computed(() =>
  all.value.filter((p) => p.id === props.modelValue || ((!props.activeOnly || p.isActive) && (!props.signersOnly || p.canSign) && (!props.only || props.only.includes(p.id))))
);
onMounted(async () => (all.value = await loadPositions()));
</script>

<template>
  <select :disabled="disabled" :value="modelValue ?? ''" class="form-select form-select-sm" @change="emit('update:modelValue', ($event.target as HTMLSelectElement).value ? Number(($event.target as HTMLSelectElement).value) : null)">
    <option value="">{{ placeholder }}</option>
    <option v-for="p in options" :key="p.id" :value="p.id">{{ p.title }}{{ p.unitTitle ? " - " + p.unitTitle : "" }}</option>
  </select>
</template>
