<template>
  <div
    class="ks-radio d-flex flex-row-reverse align-items-center gap-2 cursor-pointer unselectable-text"
    @click="emits('update:modelValue', val)">
    <div
      :class="[{ empty: modelValue !== val }, { 'order-1': ltr }]"
      class="radio d-flex align-items-center justify-content-center">
      <div :class="[color, modelValue === val ? 'active' : 'inactive']" />
    </div>
    <span :class="{ 'order-0': ltr }" class="text-small pt-1">
      {{ title }}
    </span>
  </div>
</template>

<script lang="ts" setup>
import type { PropType } from "vue";

defineProps({
  modelValue: {
    type: [String, Number, Boolean],
    required: true,
  },
  val: {
    type: [String, Number],
    required: true,
  },
  title: {
    type: String,
    required: true,
  },
  color: {
    type: String as PropType<
      "secondary" | "primary" | "warn" | "danger" | "success" | "dark"
    >,
    default: "secondary",
    validator: (prop: string) =>
      ["secondary", "primary", "warn", "danger", "success", "dark"].includes(
        prop
      ),
  },
  ltr: {
    type: Boolean,
    default: false,
  },
});

const emits = defineEmits(["update:modelValue"]);
</script>

<style lang="scss" scoped>
.ks-radio {
  .radio {
    width: 20px;
    height: 20px;
    border-radius: 12px;
    transition: border 0ms;

    &.empty {
      border: 2px solid rgba(var(--input-color), 0.4);
    }
  }

  .inactive {
    border-radius: 12px;
  }

  .active {
    width: 100%;
    height: 100%;
    border-radius: 12px;

    &.primary {
      border: 7px solid rgba(var(--primary), 1);
      box-shadow: 0 3px 12px 0 rgba(var(--primary), 0.3);
    }

    &.warn {
      border: 7px solid rgba(var(--warn), 1);
      box-shadow: 0 3px 12px 0 rgba(var(--warn), 0.3);
    }

    &.danger {
      border: 7px solid rgba(var(--danger), 1);
      box-shadow: 0 3px 12px 0 rgba(var(--danger), 0.3);
    }

    &.success {
      border: 7px solid rgba(var(--success), 1);
      box-shadow: 0 3px 12px 0 rgba(var(--success), 0.3);
    }

    &.dark {
      border: 7px solid rgba(var(--black), 1);
      box-shadow: 0 3px 12px 0 rgba(var(--black), 0.3);
    }

    &.secondary {
      border: 7px solid rgba(var(--secondary), 1);
      box-shadow: 0 3px 12px 0 rgba(var(--secondary), 0.3);
    }
  }
}
</style>
