<template>
  <div :class="'gap-' + rowGap" class="ks-skeleton d-flex flex-column">
    <div
      v-for="row in rowNumber"
      :style="[{ height: rowHeight + 'px' }, 'background: ' + backgroundColor]"
      class="item" />
  </div>
</template>

<script lang="ts" setup>
import type { PropType } from "vue";

defineProps({
  rowNumber: {
    type: Number as PropType<number>,
    default: 3,
  },
  rowGap: {
    type: Number as PropType<1 | 2 | 3 | 4 | 5>,
    default: 3,
    validator: (prop: number) => [1, 2, 3, 4, 5].includes(prop),
  },
  rowHeight: {
    type: Number as PropType<number>,
    default: 52,
  },
  backgroundColor: {
    type: String,
    default: "#e5e5e5",
  },
});
</script>

<style lang="scss" scoped>
.ks-skeleton {
  cursor: progress;

  .item {
    position: relative;
    overflow: hidden;
    border-radius: 4px;

    &::after {
      position: absolute;
      content: "";
      left: 0;
      top: 0;
      width: 100%;
      height: 100%;
      transform: translateX(-100%);
      background-image: linear-gradient(
        to left,
        transparent,
        rgba(var(--background-color), 0.5),
        transparent
      );
      animation: skeleton 800ms infinite;
    }
  }
}

@-webkit-keyframes skeleton {
  100% {
    transform: translateX(100%);
  }
}

@keyframes skeleton {
  100% {
    transform: translateX(100%);
  }
}

[data-theme="darkMode"] {
  .ks-skeleton {
    .item {
      background: rgb(var(--background-color-gray));
    }
  }
}
</style>
