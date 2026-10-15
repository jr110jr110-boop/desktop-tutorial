<script lang="ts" setup>
import type { PropType } from "vue";
import { computed, ref } from "vue";
import KsInput from "@/components/common/KsInput.vue";
import DatePicker from "vue3-persian-datetime-picker";
import moment from "moment-jalaali";

import { Colors } from "@/assets/colors";

const props = defineProps({
  modelValue: {
    type: [String, Number] as PropType<string | number>,
  },
  placeholder: {
    type: String,
    required: true,
  },
  format: {
    type: String,
    default: "YYYY-MM-DD",
  },
  displayFormat: {
    type: String,
    default: "dddd jDD jMMMM jYYYY",
  },
  type: {
    type: String,
    default: "date",
  },
  elementTag: {
    type: String,
    required: true,
  },
  color: {
    type: String,
    default: Colors.danger,
  },
  loading: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  simplePlaceholder: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
});

const emit = defineEmits(["update:modelValue"]);

const inputValue = computed(() => {
  let date = "";
  if (props.modelValue)
    date = moment(props.modelValue, props.format).format(props.displayFormat);
  return date;
});

const datePickerValue = computed(() => {
  return props.modelValue;
});

const datePicker = ref(null);

function datePickerChange() {
  emit("update:modelValue", datePicker.value.outputValue[0]);
}
</script>
<template>
  <div>
    <KsInput
      v-model="inputValue"
      :input-id="elementTag"
      :loading="loading"
      :placeholder="placeholder"
      :simple-placeholder="simplePlaceholder"
      class="w-100"
      readonly />
    <DatePicker
      ref="datePicker"
      v-model="datePickerValue"
      :color="color"
      :display-format="displayFormat"
      :element="elementTag"
      :format="format"
      :type="type"
      @change="datePickerChange" />
  </div>
</template>
