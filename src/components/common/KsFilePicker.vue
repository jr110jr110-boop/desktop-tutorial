<script lang="ts" setup>
import type { PropType, Ref } from "vue";
import { ref } from "vue";

import { Colors } from "@/assets/colors";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import PaperClipIcon from "@/components/icons/PaperClipIcon.vue";

const props = defineProps({
  hasFile: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  fileName: {
    type: String as PropType<string | null>,
    default: null,
  },
  fileSize: {
    type: [String, Number] as PropType<string | number | null>,
    default: null,
  },
  height: {
    type: Number as PropType<number>,
    default: 92,
  },
  iconSize: {
    type: String,
    default: "28",
  },
  loading: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  acceptMedia: {
    type: String,
    default: "",
  },
  multipleMedia: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
});

const emit = defineEmits(["fileChange"]);

const filePicker: Ref<HTMLInputElement | null> = ref(null);

function onFilePickerClick() {
  filePicker.value.click();
}

function onMediaPickerChange(event: Event) {
  emit("fileChange", (event.target as HTMLInputElement).files);
}

function getFileSize(bytes, dp = 1) {
  const thresh = 1000;

  if (Math.abs(bytes) < thresh) {
    return bytes + " B";
  }

  const units = ["KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];
  let u = -1;
  const r = 10 ** dp;

  do {
    bytes /= thresh;
    ++u;
  } while (
    Math.round(Math.abs(bytes) * r) / r >= thresh &&
    u < units.length - 1
  );

  return bytes.toFixed(dp) + " " + units[u];
}
</script>

<template>
  <div class="position-relative">
    <button
      :class="{ active: hasFile }"
      :style="{ height: height + 'px', borderRadius: height / 5 + 'px' }"
      class="ks-file-picker w-100"
      @click="onFilePickerClick">
      <input
        ref="filePicker"
        :accept="acceptMedia"
        :multiple="multipleMedia"
        hidden
        type="file"
        @change="onMediaPickerChange" />
      <div
        v-if="!hasFile"
        class="d-flex flex-row-reverse gap-3 px-4 align-items-center">
        <PlusIcon :fill="Colors.primary" :width="iconSize" />
        <span class="text-small"> افزودن فایل </span>
      </div>
      <div
        v-if="hasFile"
        class="d-flex flex-row-reverse gap-3 px-4 align-items-center">
        <PaperClipIcon :fill="Colors.primary" width="16" />
        <span v-if="!fileSize" class="color-primary text-small">
          ویرایش فایل
        </span>
        <span v-if="fileSize" class="color-primary fw-bold text-small">
          {{ getFileSize(fileSize) }}
        </span>
      </div>
    </button>
  </div>
</template>

<style lang="scss" scoped>
.ks-file-picker {
  background: rgba(var(--input-background), 1);
  border: none;
  overflow: hidden;
  padding: 0;

  &.active {
    background: rgba(var(--primary), 0.15);
  }

  &:hover {
    background: rgba(var(--input-background-focus), 1);
  }

  svg {
    stroke: rgba(var(--text-color), 0.7);
  }
}
</style>
