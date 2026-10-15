<template>
  <div
    :class="ltr ? 'ltr' : 'rtl'"
    :dir="ltr ? 'ltr' : 'rtl'"
    class="ks-input IRANYekan">
    <textarea
      ref="textarea"
      :placeholder="simplePlaceholder ? placeholder : ''"
      :readonly="loading"
      :value="modelValue"
      class="color-primary"
      rows="4"
      @input="onInputChange" />
    <label
      v-if="!simplePlaceholder"
      :class="modelValue && 'has-value'"
      @click="textarea.focus()">
      {{ placeholder }}
    </label>
    <span class="counter">{{ stringCounter }}</span>
    <div v-if="loading" class="custom-loader left" />
  </div>
</template>

<script lang="ts" setup>
import type { ComputedRef, PropType } from "vue";
import { computed, ref } from "vue";

const props = defineProps({
  placeholder: {
    type: String,
  },
  simplePlaceholder: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  modelValue: {
    type: [String, Number] as PropType<string | number>,
  },
  loading: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  ltr: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
});

const emit = defineEmits(["change", "update:modelValue"]);

const textarea: HTMLTextAreaElement = ref(null);

const stringCounter: ComputedRef<string> = computed(() => {
  let count = "0";
  if (props.modelValue) count = props.modelValue.toString().length.toString();
  return count;
});

function onInputChange(event: Event) {
  emit("change");
  emit("update:modelValue", (event.target as HTMLInputElement).value);
}
</script>
