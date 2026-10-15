<template>
  <transition name="modal">
    <div
      v-if="modelValue"
      :class="{ 'blur-overlay': blur }"
      :style="{ zIndex: 999990 + order }"
      class="ks-modal-wrapper p-3"
      @click="preventClose ? shakeModal() : closeModal()">
      <div
        ref="KsModal"
        :class="[noPadding ? 'p-0' : 'p-3', { 'full-screen': fullScreen }]"
        class="ks-modal background-color"
        @click.stop>
        <KsButton
          v-if="!preventClose"
          class="closeBtn background-color"
          color="transparent"
          fixed-width-height="34"
          @click="closeModal">
          <template #content>
            <TimesIcon fill="#000000" width="16" />
          </template>
        </KsButton>
        <div class="ks-modal-content" dir="rtl">
          <slot />
        </div>
      </div>
    </div>
  </transition>
</template>

<script lang="ts" setup>
import type { PropType, Ref } from "vue";
import { ref } from "vue";
import KsButton from "@/components/common/KsButton.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";

defineProps({
  modelValue: {
    type: Boolean as PropType<boolean>
  },
  blur: {
    type: Boolean as PropType<boolean>,
    default: false
  },
  preventClose: {
    type: Boolean as PropType<boolean>,
    default: true
  },
  noPadding: {
    type: Boolean as PropType<boolean>,
    default: false
  },
  fullScreen: {
    type: Boolean as PropType<boolean>,
    default: false
  },
  order: {
    type: Number as PropType<number>,
    default: 0
  }
});

const emit = defineEmits(["onClose", "update:modelValue"]);

const KsModal: Ref<HTMLDivElement> = ref(null);

function shakeModal() {
  KsModal.value.classList.add("shake");
  setTimeout(() => {
    KsModal.value.classList.remove("shake");
  }, 500);
}

function closeModal() {
  emit("update:modelValue", false);
  emit("onClose");
}
</script>

<style lang="scss" scoped>
.ks-modal-wrapper {
  width: 100%;
  height: 100vh;
  background: rgba(var(--black), 0.2);
  position: fixed;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  display: flex;
  justify-content: center;
  align-items: center;

  @media screen and (max-width: 450px) {
    .ks-modal {
      min-width: 300px !important;
    }
  }

  .ks-modal {
    border-radius: 20px;
    box-shadow: 0 5px 30px 0 rgba(0, 0, 0, 0.1);
    position: relative;
    min-width: 420px;
    max-width: 95vw;
    max-height: 95vh;

    &.full-screen {
      width: 100%;
      height: 100%;
    }

    &.shake {
      animation: shake 0.3s;
    }

    .closeBtn {
      position: absolute;
      right: -8px;
      top: -8px;
      border-radius: 6px;
      box-shadow: 0 5px 30px 0 rgba(0, 0, 0, 0.09);

      svg {
        fill: rgb(var(--black));
      }
    }
  }
}

[data-theme="darkMode"] {
  .closeBtn {
    svg {
      fill: rgb(var(--white)) !important;
    }
  }
}

.modal-enter-active {
  transition: all 200ms ease;
}

.modal-enter-from,
.modal-leave-to {
  opacity: 0;

  .ks-modal {
    transform: scale(0.5);
  }
}

.modal-enter-to {
  opacity: 1;

  .ks-modal {
    transform: scale(1);
  }
}

@keyframes shake {
  0% {
    transform: translate(1px, 1px) rotate(0deg);
  }
  10% {
    transform: translate(-1px, -2px) rotate(-1deg);
  }
  20% {
    transform: translate(-3px, 0px) rotate(1deg);
  }
  30% {
    transform: translate(3px, 2px) rotate(0deg);
  }
  40% {
    transform: translate(1px, -1px) rotate(1deg);
  }
  50% {
    transform: translate(-1px, 2px) rotate(-1deg);
  }
  60% {
    transform: translate(-3px, 1px) rotate(0deg);
  }
  70% {
    transform: translate(3px, 1px) rotate(-1deg);
  }
  80% {
    transform: translate(-1px, -1px) rotate(1deg);
  }
  90% {
    transform: translate(1px, 2px) rotate(0deg);
  }
  100% {
    transform: translate(1px, -2px) rotate(-1deg);
  }
}
</style>
