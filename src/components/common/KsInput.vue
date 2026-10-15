<template>
  <div
    :class="[ltr ? 'ltr' : 'rtl', icon !== null && 'icon-' + icon]"
    :dir="ltr ? 'ltr' : 'rtl'"
    class="ks-input IRANYekan">
    <div
      :class="[
        appendText && 'input-group',
        ltr ? 'ltr-input' : 'rtl-input',
        { 'opacity-75': isDisabled },
      ]">
      <!-- Input for updatingBom.length -->
      <input
        :id="inputId"
        ref="ksInput"
        :dir="ltr ? 'ltr' : 'rtl'"
        :disabled="loading || isDisabled"
        :placeholder="simplePlaceholder ? placeholder : ''"
        :readonly="loading || readonly || isDisabled"
        :step="step"
        :style="customHeight && { height: customHeight + 'px' }"
        :type="inputType"
        :value="modelValue"
        class="color-primary form-control"
        @change="emit('blur')"
        @input="onInputChange" />
      <label
        v-if="!simplePlaceholder"
        :class="modelValue !== null && modelValue !== '' && 'has-value'">
        {{ placeholder }}
      </label>

      <!-- Dropdown for updatingBom.lengthUnit -->
      <div v-if="appendTextIsDropdown" class="input-group-append" dir="rtl">
        <span class="input-group-text fw-bold">
          <select
            class="input-group-text"
            v-model="selectedDropdown"
            @change="onSelectChange">
            <option
              v-for="option in appendTextOptions"
              :key="option.id"
              :value="option.id">
              {{ option.text }}
            </option>
          </select>
        </span>
      </div>

      <!-- Static appendText -->
      <div
        class="input-group-append"
        v-if="appendText && !appendTextIsDropdown">
        <span class="input-group-text fw-bold" :dir="ltr && 'ltr'">
          {{ appendText }}
        </span>
      </div>

      <div
        v-if="loading"
        :class="ltr ? 'right' : 'left'"
        class="custom-loader" />
      <button v-if="icon" class="ks-input-icon">
        <slot name="icon" />
      </button>
    </div>
  </div>
</template>
<script lang="ts" setup>
import { ref, watch } from "vue";
import type { PropType } from "vue";

// Emit custom events
const emit = defineEmits([
  "change",
  "update:modelValue",
  "blur",
  "update:appendTextSelectedId",
]);

// Define props
const props = defineProps({
  inputId: { type: String },
  placeholder: { type: String, default: "" },
  simplePlaceholder: { type: Boolean as PropType<boolean>, default: false },
  modelValue: { type: [String, Number] as PropType<string | number> }, // For input value
  inputType: {
    type: String as PropType<"text" | "number" | "password">,
    default: "text",
  },
  loading: { type: Boolean as PropType<boolean>, default: false },
  readonly: { type: Boolean as PropType<boolean>, default: false },
  appendText: { type: String },
  appendTextIsDropdown: { type: Boolean as PropType<boolean>, default: false },
  appendTextOptions: {
    type: Array as PropType<{ id: any; text: string }[]>,
    default: () => [],
  },
  dropdownValue: { type: [String, Number], default: null }, // For dropdown
  ltr: { type: Boolean as PropType<boolean>, default: false },
  icon: {
    type: String as PropType<"right" | "left" | null>,
    validator: (prop: string | null) =>
      prop === null || ["right", "left"].includes(prop),
    default: null,
  },
  isDisabled: { type: Boolean as PropType<boolean>, default: false },
  customHeight: { type: String, default: null },
  step: { type: String, default: "0" },
});

// Reactive property for dropdown value
const selectedDropdown = ref(props.dropdownValue);

// Watch for props changes to sync selectedDropdown
watch(
  () => props.dropdownValue,
  (newValue) => {
    selectedDropdown.value = newValue;
  },
  { immediate: true }
);

// Emit changes for dropdown value
function onSelectChange(event: Event) {
  selectedDropdown.value = (event.target as HTMLSelectElement).value;
  emit("update:appendTextSelectedId", selectedDropdown.value);
}

// Emit changes for input value
function onInputChange(event: Event) {
  emit("change");
  emit("update:modelValue", (event.target as HTMLInputElement).value);
}
</script>
