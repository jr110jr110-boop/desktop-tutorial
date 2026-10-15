<script lang="ts" setup>
defineProps({
  isUploading: {
    type: Boolean,
    default: false,
  },
  progress: {
    type: Number,
  },
});
</script>

<template>
  <div class="ks-upload-progress position-relative">
    <div v-if="isUploading" class="progress ks-progress-bar">
      <div
        :aria-valuenow="progress"
        :style="{ width: progress + '%' }"
        aria-valuemax="100"
        aria-valuemin="0"
        class="progress-bar progress-bar-striped bg-custom-color progress-bar-animated"
        role="progressbar"></div>
    </div>
    <div :class="{ 'no-pointer-event': isUploading }" :disabled="isUploading">
      <slot />
    </div>
  </div>
</template>

<style lang="scss" scoped>
.ks-upload-progress {
  .ks-progress-bar {
    position: absolute;
    bottom: 4px;
    left: 50%;
    transform: translateX(-50%);
    width: 99.5%;
    height: 6px;
    background: transparent;

    .progress-bar {
      background-color: rgb(var(--success));
      border-radius: 99px;
      background-image: linear-gradient(
        45deg,
        rgba(255, 255, 255, 0.3) 25%,
        transparent 25%,
        transparent 50%,
        rgba(255, 255, 255, 0.3) 50%,
        rgba(255, 255, 255, 0.3) 75%,
        transparent 75%,
        transparent
      );
    }
  }

  .no-pointer-event {
    opacity: 0.3;
  }
}
</style>
