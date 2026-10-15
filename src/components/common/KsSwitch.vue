<template>
  <div
    :class="[color, { active: modelValue === onValue }]"
    class="ks-switch"
    @click="
      $emit('update:modelValue', modelValue === onValue ? offValue : onValue)
    ">
    <div class="switch-slide unselectable-text IRANYekan">
      <div v-if="modelValue === onValue" class="on-value">
        <slot name="on" />
      </div>
      <div v-else class="off-value">
        <slot name="off" />
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import type { PropType } from "vue";

defineProps({
  modelValue: {
    type: [String, Number, Boolean] as PropType<string | number | boolean>,
  },
  onValue: {
    type: [String, Number, Boolean] as PropType<string | number | boolean>,
    default: true,
  },
  offValue: {
    type: [String, Number, Boolean] as PropType<string | number | boolean>,
    default: false,
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
});
</script>

<style lang="scss" scoped>
.ks-switch {
  &.active {
    .switch-slide {
      &:after {
        left: 25px;
      }
    }

    &.primary {
      .switch-slide {
        background-color: rgba(var(--primary), 1);
        box-shadow: 0 0 20px 0 rgba(var(--primary), 0.2);
      }
    }

    &.warn {
      .switch-slide {
        background-color: rgba(var(--warn), 1);
        box-shadow: 0 0 20px 0 rgba(var(--warn), 0.2);
      }
    }

    &.danger {
      .switch-slide {
        background-color: rgba(var(--danger), 1);
        box-shadow: 0 0 20px 0 rgba(var(--danger), 0.2);
      }
    }

    &.success {
      .switch-slide {
        background-color: rgba(var(--success), 1);
        box-shadow: 0 0 20px 0 rgba(var(--success), 0.2);
      }
    }

    &.dark {
      .switch-slide {
        background-color: rgba(var(--black), 1);
        box-shadow: 0 0 20px 0 rgba(var(--black), 0.2);
      }
    }

    &.secondary {
      .switch-slide {
        background-color: rgba(var(--secondary), 1);
        box-shadow: 0 0 20px 0 rgba(var(--secondary), 0.2);
      }
    }
  }

  &:hover {
    .switch-slide {
      background-color: rgba(var(--input-background-focus, 1));
    }
  }

  .switch-slide {
    display: flex;
    align-items: center;
    justify-content: center;
    min-width: 102px;
    height: 30px;
    background-color: rgba(var(--switch-background, 1));
    border-radius: 32px;
    position: relative;
    cursor: pointer;
    transition: 0.3s;
    padding-right: 16px;
    padding-left: 16px;

    &::after {
      content: "";
      width: 22px;
      height: 22px;
      background-color: rgba(var(--background-color));
      position: absolute;
      border-radius: 70px;
      top: 4px;
      left: calc(100% - 4px);
      transform: translateX(-100%);
      transition: 0.5s;
    }

    .on-value,
    .off-value {
      font-size: 13px;
      padding-top: 3px;
    }

    .off-value {
      color: rgba(var(--switch-color-off), 1);
      margin-right: 16px;
    }

    .on-value {
      color: rgba(var(--white), 1);
      margin-left: 16px;
    }
  }
}
</style>
