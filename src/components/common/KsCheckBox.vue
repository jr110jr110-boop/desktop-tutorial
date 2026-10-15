<template>
  <div
    :dir="ltr ? 'ltr' : 'rtl'"
    class="ks-checkbox d-flex align-items-center gap-3 unselectable-text"
    @click="onClick">
    <div
      :class="[color, { active: modelValue === onValue }]"
      class="checkbox d-flex align-items-center justify-content-center">
      <transition name="scale-up">
        <CheckIcon v-if="modelValue === onValue" fill="#ffffff" width="14" />
      </transition>
    </div>
    <span class="text-xSmall">{{ title }}</span>
  </div>
</template>

<script lang="ts" setup>
import type { PropType } from "vue";
import CheckIcon from "@/components/icons/CheckIcon.vue";

const props = defineProps({
  title: {
    type: String,
    default: "",
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
  modelValue: {
    type: [Boolean, Number, String] as PropType<string | number | boolean>,
  },
  onValue: {
    type: [String, Number, Boolean] as PropType<string | number | boolean>,
    default: true,
  },
  offValue: {
    type: [String, Number, Boolean] as PropType<string | number | boolean>,
    default: false,
  },
  ltr: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  customHandleChange: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
});

const emit = defineEmits(["update:modelValue", "change"]);

function onClick() {
  if (!props.customHandleChange)
    emit(
      "update:modelValue",
      props.modelValue === props.onValue ? props.offValue : props.onValue
    );
  emit(
    "change",
    props.modelValue === props.onValue ? props.offValue : props.onValue
  );
}
</script>

<style lang="scss" scoped>
.ks-checkbox {
  &:hover {
    cursor: pointer;

    .checkbox {
      background: rgba(var(--input-background-hover), 1);
    }
  }

  .checkbox {
    width: 24px;
    height: 24px;
    border: 2px solid rgba(var(--checkbox-border), 1);
    background: rgba(var(--input-background), 1);
    border-radius: 8px;

    &.active {
      position: relative;
      border: none;

      &.secondary {
        background: rgba(var(--secondary), 1);
        box-shadow: 0 3px 12px 0 rgba(var(--secondary), 0.3);
      }

      &.primary {
        background: rgba(var(--primary), 1);
        box-shadow: 0 3px 12px 0 rgba(var(--primary), 0.3);
      }

      &.warn {
        background: rgba(var(--warn), 1);
        box-shadow: 0 3px 12px 0 rgba(var(--warn), 0.3);
      }

      &.danger {
        background: rgba(var(--danger), 1);
        box-shadow: 0 3px 12px 0 rgba(var(--danger), 0.3);
      }

      &.dark {
        background: rgba(var(--black), 1);
        box-shadow: 0 3px 12px 0 rgba(var(--black), 0.3);
      }

      &.success {
        background: rgba(var(--success), 1);
        box-shadow: 0 3px 12px 0 rgba(var(--success), 0.3);
      }
    }
  }

  .text-small {
    padding-top: 2px;
  }
}
</style>
