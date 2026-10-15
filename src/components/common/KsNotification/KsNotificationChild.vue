<template>
  <div
    :class="[
      notification.color,
      notification.position,
      notification.ltr ? 'ltr' : 'rtl',
    ]"
    :dir="notification.ltr ? 'ltr' : 'rtl'"
    class="ks-notification unselectable-text"
    @click="
      notification.closeOnClick &&
        useNotification.removeNotification(notification.id)
    ">
    <button
      v-if="notification.closeable"
      class="close-btn"
      @click="useNotification.removeNotification(notification.id)">
      <TimesIcon
        :fill="notification.color === 'white' ? '#000000' : '#ffffff'"
        width="14" />
    </button>
    <div
      :class="{ 'p-3': !notification.content }"
      class="d-flex align-items-center">
      <!--      Content      -->
      <div v-if="notification.content" v-html="notification.content" />

      <!--      Icon      -->
      <div
        v-if="!notification.content && notification.icon"
        :class="notification.ltr ? 'me-3' : 'ms-3'"
        v-html="notification.icon" />

      <div
        v-if="!notification.content"
        :class="notification.ltr ? 'me-3' : 'ms-3'"
        class="d-flex flex-column gap-2">
        <span class="fw-bold text-small title">{{ notification.title }}</span>
        <span class="text-xSmall text">{{ notification.text }}</span>
      </div>
    </div>
    <div class="progress">
      <div
        ref="progressBar"
        aria-valuemax="100"
        aria-valuemin="0"
        aria-valuenow="0"
        class="progress-bar progress-bar-striped"
        role="progressbar"
        style="width: 0"></div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import type Notification from "@/components/common/KsNotification/type";
import type { Ref } from "vue";
import { onMounted, ref } from "vue";

import useNotification from "@/components/common/KsNotification/useNotification";
import TimesIcon from "@/components/icons/TimesIcon.vue";

type Props = {
  notification: Notification;
};

const props: Props = defineProps({
  notification: {
    type: Object,
    required: true,
  },
});

const duration: Ref<number> = ref(0);
const progressBar: Ref<HTMLElement> = ref(null);

let interval: any = 0;

const progressBarHandler = () => {
  if (duration.value <= props.notification.duration) {
    duration.value += 100;
    if (progressBar.value)
      progressBar.value.style.width =
        (duration.value / props.notification.duration) * 100 + "%";
  } else {
    useNotification.removeNotification(props.notification.id);
    clearInterval(interval);
  }
};

onMounted(() => {
  if (props.notification.duration !== null)
    interval = setInterval(progressBarHandler, 100);
});
</script>

<style lang="scss" scoped>
.ks-notification {
  color: rgba(var(--white), 1);
  border-radius: 14px;
  margin-top: 8px;
  position: relative;
  min-width: 220px;
  overflow-x: hidden !important;

  .title,
  .text {
    color: rgb(var(--white));
  }

  &.rtl {
    .close-btn {
      left: 4px;
    }
  }

  &.ltr {
    .close-btn {
      right: 4px;
    }
  }

  .close-btn {
    position: absolute;
    top: 0;
    padding: 6px;
    border: none;
    outline: none;
    background: transparent;
  }

  &.secondary {
    background: rgba(var(--secondary), 1);
    box-shadow: 0 3px 12px 0 rgba(var(--secondary), 0.3);
  }

  &.primary {
    background: rgba(var(--primary), 1);
    box-shadow: 0 3px 12px 0 rgba(var(--primary), 0.3);
  }

  &.dark {
    background: rgba(var(--black), 1);
    box-shadow: 0 3px 12px 0 rgba(var(--black), 0.3);
  }

  &.danger {
    background: rgba(var(--danger), 1);
    box-shadow: 0 3px 12px 0 rgba(var(--danger), 0.3);
  }

  &.warn {
    background: rgba(var(--warn), 1);
    box-shadow: 0 3px 12px 0 rgba(var(--warn), 0.3);
  }

  &.success {
    background: rgba(var(--success), 1);
    box-shadow: 0 3px 12px 0 rgba(var(--success), 0.3);
  }

  &.white {
    background: rgba(var(--white), 1);
    box-shadow: 0 3px 12px 0 rgba(var(--black), 0.1);

    .title,
    .text {
      color: rgba(var(--black), 1);
    }
  }

  .text-xSmall {
    max-width: 220px;
  }

  .progress {
    direction: ltr;
    position: absolute;
    bottom: 0;
    left: 0;
    width: 100%;
    height: 4px;
    background: none;

    .progress-bar {
      background: rgba(var(--white), 0.4);
      box-shadow: none;
    }
  }
}
</style>
