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
        :value="formatNumber(modelValue)"
        :type="inputType"
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
            v-model="dropdownValue"
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
  modelValue: { type: [String, Number] as PropType<string | number> }, // For updatingBom.length
  inputType: {
    type: String as PropType<"text" | "number" | "password">,
    default: "text",
  },
  loading: { type: Boolean as PropType<boolean>, default: false },
  readonly: { type: Boolean as PropType<boolean>, default: false },
  appendText: { type: String },
  appendTextDirection: {
    type: String as PropType<"right" | "left">,
    default: "left",
  },
  appendTextIsDropdown: { type: Boolean as PropType<boolean>, default: false },
  appendTextOptions: {
    type: Array as PropType<{ id: any; text: string }[]>,
    default: () => [],
  },
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
  dropdownValue: { type: [String, Number] }, // For updatingBom.lengthUnit
});

// Watch to initialize the dropdown's value on mount
watch(
  () => props.dropdownValue,
  (newValue) => {
    if (newValue !== null && newValue !== undefined) {
      selectedDropdown.value = newValue;
    }
  },
  { immediate: true }
);

// Reactive property for dropdown value
const selectedDropdown = ref(props.dropdownValue);

// Utility function to format number with commas
function formatNumber(value: string | number): string {
  const stringValue = String(value);
  return stringValue.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

// Function to handle input change
function onInputChange(event: Event) {
  const inputValue = (event.target as HTMLInputElement).value.replace(/,/g, ""); // Remove commas
  const formattedValue = formatNumber(inputValue);

  emit("update:modelValue", inputValue);
  emit("change");

  // Set the formatted value back to the input element for display
  (event.target as HTMLInputElement).value = formattedValue;
}

function onSelectChange(event: Event) {
  emit("update:appendTextSelectedId", selectedDropdown.value); // For dropdown
}
</script>
