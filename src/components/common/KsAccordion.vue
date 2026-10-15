<template>
  <div class="cursor-pointer d-flex flex-column">
    <div
      :class="btnClasses"
      class="d-flex flex-row-reverse gap-3 align-items-center"
      @click="show = !show">
      <PlusIcon
        :fill="Colors[plusIconColor]"
        :to-minus="show"
        class="default-icon-color"
        width="16" />
      <div class="flex-fill">
        <slot name="header" />
      </div>
    </div>
    <div :style="show ? 'max-height:' + maxHeight + 'px' : 'max-height: 0'">
      <transition name="fade">
        <div v-show="show">
          <slot name="content" />
        </div>
      </transition>
    </div>
  </div>
</template>

<script lang="ts" setup>
import type { PropType, Ref } from "vue";
import { ref } from "vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  maxHeight: {
    type: String,
    default: "9999999999999999999999",
  },
  btnClasses: {
    type: String,
    default: "",
  },
  plusIconColor: {
    type: String as PropType<
      "danger" | "black" | "warn" | "success" | "primary" | "secondary"
    >,
    default: "black",
  },
  openOnInit: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
});

const show: Ref<boolean> = ref(props.openOnInit);
</script>
