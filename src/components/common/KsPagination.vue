<template>
  <div class="d-flex gap-1 justify-content-center">
    <KsButton
      :disabled="modelValue === 1"
      color="dark"
      fixed-width-height="32"
      theme="flat"
      @click="changeActivePage(modelValue - 1)">
      <template #content>
        <AngleLeftIcon :fill="Colors.black" width="5" />
      </template>
    </KsButton>
    <KsButton
      v-for="btn in pagePills"
      :key="btn"
      :active="modelValue === btn"
      :title="typeof btn === 'number' ? btn.toString() : '...'"
      bold
      color="dark"
      fixed-width-height="32"
      size="mini"
      theme="flat"
      @click="
        changeActivePage(
          btn === '+' ? modelValue + 3 : btn === '-' ? modelValue - 3 : btn
        )
      " />
    <KsButton
      :disabled="modelValue === length"
      color="dark"
      fixed-width-height="32"
      theme="flat"
      @click="changeActivePage(modelValue + 1)">
      <template #content>
        <AngleLeftIcon
          :fill="Colors.black"
          style="transform: rotate(180deg)"
          width="5" />
      </template>
    </KsButton>
  </div>
</template>

<script lang="ts" setup>
import { computed } from "vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import KsButton from "@/components/common/KsButton.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  length: {
    type: Number,
    required: true,
  },
  modelValue: {
    type: Number,
  },
});

const emits = defineEmits(["update:modelValue", "onChange"]);

const pagePills = computed(() => {
  // CASE 1: IS LESS THAN 10, SO NO NEED OF DOTS
  if (props.length <= 10) return props.length;

  const siblingCount = 5;
  const rightSiblingIndex = Math.max(props.modelValue, 1);
  const leftSiblingIndex = Math.min(props.modelValue, props.length);
  const showLeftDots = leftSiblingIndex > siblingCount;
  const showRightDots = rightSiblingIndex < props.length - 2;
  const firstPageIndex = 1;
  const lastPageIndex = props.length;

  // CASE 2: Need to show right dots
  if (!showLeftDots && showRightDots) {
    let leftRange = range(1, siblingCount);
    return [...leftRange, "+", props.length];
  }

  // CASE 3: Need to show left dots
  if (showLeftDots && !showRightDots) {
    let rightRange = range(props.length - siblingCount + 1, props.length);
    return [firstPageIndex, "-", ...rightRange];
  }

  // Case 4: Need to show both left and right dots
  if (showLeftDots && showRightDots) {
    let middleRange = range(leftSiblingIndex - 2, rightSiblingIndex + 2);
    return [firstPageIndex, "-", ...middleRange, "+", lastPageIndex];
  }

  return 1;
});

const range = (start, end) => {
  let length = end - start + 1;
  return Array.from({ length }, (_, idx) => idx + start);
};

const changeActivePage = (page: number) => {
  if (props.modelValue !== page) {
    emits("update:modelValue", page);
    emits("onChange");
  }
};
</script>
