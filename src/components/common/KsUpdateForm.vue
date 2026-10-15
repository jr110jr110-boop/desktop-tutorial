<script lang="ts" setup>
import type { PropType } from "vue";

import KsButton from "@/components/common/KsButton.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import { Colors } from "@/assets/colors";

defineProps({
  formTitle: {
    type: String,
    required: true,
  },
  btnLoading: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  isEdit: {
    type: Boolean as PropType<boolean>,
    required: true,
  },
  hasSubmitBtn: {
    type: Boolean as PropType<boolean>,
    default: true,
  },
  hasTransitionGroup: {
    type: Boolean as PropType<boolean>,
    default: true,
  },
  hasBackBtn: {
    type: Boolean as PropType<boolean>,
    default: true,
  },
});

defineEmits(["formSubmit"]);
</script>

<template>
  <div class="background-color p-3 shadow-sm rounded-3 text-end">
    <div
      class="d-flex flex-row-reverse justify-content-between align-items-center">
      <div class="list-title">
        <span class="fw-bold text-large text-color" dir="rtl">
          {{ isEdit ? "ویرایش" : "افزودن" }}
          {{ formTitle }}
        </span>
        <slot name="formIcon" />
      </div>
      <KsButton
        v-if="hasBackBtn"
        class="p-1"
        color="dark"
        fixed-width-height="42"
        theme="flat"
        @click="$router.back()">
        <template #content>
          <AngleLeftIcon :fill="Colors.primary" width="6" />
        </template>
      </KsButton>
    </div>
    <hr class="opacity-30" />

    <!--       FORM       -->
    <transition-group
      v-if="hasTransitionGroup"
      class="mt-5 row gap-3 justify-content-center align-items-center"
      name="fade"
      tag="div">
      <slot name="form" />
    </transition-group>
    <div
      v-else
      class="mt-5 row gap-3 justify-content-center align-items-center">
      <slot name="form" />
    </div>

    <div
      v-if="hasSubmitBtn"
      class="d-grid d-sm-flex justify-content-sm-center mt-3">
      <KsButton
        :loading="btnLoading"
        :title="(isEdit ? 'ویرایش' : 'افزودن') + ' ' + formTitle"
        class="py-1 px-2 fw-bold text-small"
        dir="rtl"
        primary
        @click="$emit('formSubmit')" />
    </div>
  </div>
</template>

<style scoped></style>
