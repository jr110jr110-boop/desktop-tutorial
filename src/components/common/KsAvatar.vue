<script lang="ts" setup>
import type { PropType, Ref } from "vue";
import { ref } from "vue";

import KsButton from "@/components/common/KsButton.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import EditIcon from "@/components/icons/EditIcon.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  mediaSrc: {
    type: String,
    default: null,
  },
  size: {
    type: Number,
    default: 92,
  },
  iconSize: {
    type: String,
    default: "28",
  },
  loading: {
    type: Boolean,
    default: false,
  },
  hasActionButtons: {
    type: Boolean,
    default: false,
  },
  hasMediaPicker: {
    type: Boolean,
    default: false,
  },
  acceptMedia: {
    type: String,
    default: "image/*,video/*",
  },
  multipleMedia: {
    type: Boolean,
    default: false,
  },
  mediaType: {
    type: String as PropType<"image" | "video">,
    default: "image",
    validator: (prop: string) => ["image", "video"].includes(prop),
  },
  active: {
    type: Boolean,
    default: false,
  },
  objectFit: {
    type: String as PropType<"cover" | "contain">,
    default: "cover",
  },
  radius: {
    type: String as PropType<"auto" | "circle" | "square">,
    default: "auto",
  },
});

const emit = defineEmits(["click", "mediaChange", "removeMedia"]);

const mediaPicker: Ref<HTMLInputElement | null> = ref(null);

function onAvatarClick() {
  if (props.hasMediaPicker) mediaPicker.value.click();
  else emit("click");
}

function onMediaPickerChange(event: Event) {
  emit("mediaChange", (event.target as HTMLInputElement).files);
}

function onRemoveButtonClick() {
  emit("removeMedia");
  mediaPicker.value.value = null;
}
</script>

<template>
  <div class="position-relative">
    <button
      :class="{ active: active }"
      :style="{
        width: size + 'px',
        height: size + 'px',
        borderRadius:
          radius === 'auto'
            ? size / 4 + 'px'
            : radius === 'circle'
            ? '9999px'
            : '0px',
      }"
      class="ks-avatar"
      @click="onAvatarClick">
      <input
        v-if="hasMediaPicker"
        ref="mediaPicker"
        :accept="acceptMedia"
        :multiple="multipleMedia"
        hidden
        type="file"
        @change="onMediaPickerChange" />
      <div v-if="loading" class="position-relative w-100 h-100">
        <div class="custom-loader center p-4" />
      </div>
      <img
        v-if="mediaSrc && mediaType === 'image' && !loading"
        :class="'object-fit-' + objectFit"
        :src="mediaSrc"
        alt="" />
      <video
        v-if="mediaSrc && mediaType === 'video' && !loading"
        :class="'object-fit-' + objectFit"
        :src="mediaSrc"
        autoplay
        loop
        muted />
      <slot name="default-icon">
        <PlusIcon
          v-if="!mediaSrc && !loading"
          :fill="Colors.primary"
          :width="iconSize" />
      </slot>
    </button>
    <transition name="fade">
      <div v-if="hasActionButtons && mediaSrc" class="action-buttons">
        <div class="d-flex flex-column gap-1">
          <KsButton
            color="dark"
            fixed-width-height="24"
            radius="circle"
            @click="onAvatarClick">
            <template #content>
              <EditIcon :fill="Colors.white" />
            </template>
          </KsButton>
          <KsButton
            color="danger"
            fixed-width-height="24"
            radius="circle"
            @click="onRemoveButtonClick">
            <template #content>
              <TimesIcon :fill="Colors.white" />
            </template>
          </KsButton>
        </div>
      </div>
    </transition>
  </div>
</template>

<style lang="scss" scoped>
.ks-avatar {
  background: rgba(var(--input-background), 1);
  border: 5px solid rgba(var(--input-background), 1);
  overflow: hidden;
  padding: 0;

  &.active {
    border: 5px solid rgba(var(--success), 0.4);
  }

  &:hover {
    background: rgba(var(--input-background-focus), 1);

    img,
    video {
      transform: scale(1.1);
    }
  }

  svg {
    stroke: rgba(var(--text-color), 0.7);
  }

  img,
  video {
    width: 100%;
    height: 100%;

    &.object-fit-contain {
      padding: 4px;
    }
  }
}

.action-buttons {
  position: absolute;
  bottom: 8px;
  right: -4px;
}
</style>
