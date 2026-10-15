<template>
  <transition-group
    v-show="notifications.leftBottom.length"
    class="notifications-parent left-bottom"
    name="left-bottom"
    tag="div">
    <KsNotificationChild
      v-for="n in notifications.leftBottom"
      :key="n.id"
      :notification="n" />
  </transition-group>
  <transition-group
    v-show="notifications.leftTop.length"
    class="notifications-parent left-top"
    name="left-top"
    tag="div">
    <KsNotificationChild
      v-for="n in notifications.leftTop"
      :key="n.id"
      :notification="n" />
  </transition-group>
  <transition-group
    v-show="notifications.rightBottom.length"
    class="notifications-parent right-bottom"
    name="right-bottom"
    tag="div">
    <KsNotificationChild
      v-for="n in notifications.rightBottom"
      :key="n.id"
      :notification="n" />
  </transition-group>
  <transition-group
    v-show="notifications.rightTop.length"
    class="notifications-parent right-top"
    name="right-top"
    tag="div">
    <KsNotificationChild
      v-for="n in notifications.rightTop"
      :key="n.id"
      :notification="n" />
  </transition-group>
  <transition-group
    v-show="notifications.top.length"
    class="notifications-parent top"
    name="top"
    tag="div">
    <KsNotificationChild
      v-for="n in notifications.top"
      :key="n.id"
      :notification="n" />
  </transition-group>
  <transition-group
    v-show="notifications.bottom.length"
    class="notifications-parent bottom"
    name="bottom"
    tag="div">
    <KsNotificationChild
      v-for="n in notifications.bottom"
      :key="n.id"
      :notification="n" />
  </transition-group>
</template>

<script lang="ts" setup>
import type Notification from "@/components/common/KsNotification/type";
import { computed } from "vue";

import KsNotificationChild from "./KsNotificationChild.vue";
import useNotification from "@/components/common/KsNotification/useNotification";

const notifications = computed(() => {
  let leftBottom: Notification[] = [];
  let leftTop: Notification[] = [];
  let rightBottom: Notification[] = [];
  let rightTop: Notification[] = [];
  let top: Notification[] = [];
  let bottom: Notification[] = [];
  for (let i = 0; i < useNotification.notifications.length; i++) {
    switch (useNotification.notifications[i].position) {
      case "left-bottom":
        leftBottom.push(useNotification.notifications[i]);
        break;
      case "left-top":
        leftTop.push(useNotification.notifications[i]);
        break;
      case "right-bottom":
        rightBottom.push(useNotification.notifications[i]);
        break;
      case "right-top":
        rightTop.push(useNotification.notifications[i]);
        break;
      case "top":
        top.push(useNotification.notifications[i]);
        break;
      case "bottom":
        bottom.push(useNotification.notifications[i]);
        break;
    }
  }
  return {
    leftBottom,
    leftTop,
    rightBottom,
    rightTop,
    top,
    bottom,
  };
});
</script>

<style lang="scss" scoped>
.notifications-parent {
  position: fixed;
  z-index: 9999999;

  &.left-bottom,
  &.right-bottom,
  &.bottom {
    bottom: 16px;
  }

  &.left-top,
  &.right-top,
  &.top {
    top: 16px;
  }

  &.left-bottom,
  &.left-top {
    left: 16px;
  }

  &.right-bottom,
  &.right-top {
    right: 16px;
  }

  &.bottom,
  &.top {
    left: 50%;
    transform: translateX(-50%);
  }
}

.left-bottom-enter-active,
.left-top-enter-active,
.right-bottom-enter-active,
.right-top-enter-active,
.top-enter-active,
.bottom-enter-active {
  position: relative;
  transition: all ease 0.3s;
}

.left-bottom-enter-from,
.left-bottom-leave-to,
.left-top-enter-from,
.left-top-leave-to,
.right-bottom-enter-from,
.right-bottom-leave-to,
.right-top-enter-from,
.right-top-leave-to,
.top-enter-from,
.top-leave-to,
.bottom-enter-from,
.bottom-leave-to {
  opacity: 0;
}

.left-bottom-enter-to,
.left-top-enter-to,
.right-bottom-enter-to,
.right-top-enter-to,
.top-enter-to,
.bottom-enter-to {
  opacity: 1;
}

.left-bottom-enter-from,
.left-top-enter-from {
  left: -500px;
}

.left-bottom-enter-to,
.left-top-enter-to {
  opacity: 1;
  left: 0;
}

.right-bottom-enter-from,
.right-top-enter-from {
  right: -500%;
}

.right-bottom-enter-to,
.right-top-enter-to {
  opacity: 1;
  right: 0;
}

.bottom-enter-from {
  bottom: -500px;
}

.bottom-enter-to {
  opacity: 1;
  bottom: 0;
}

.top-enter-from {
  top: -500px;
}

.top-enter-to {
  opacity: 1;
  top: 0;
}
</style>
