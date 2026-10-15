<script lang="ts" setup>
defineProps({
  title: {
    type: String,
    required: true,
  },
  text: {
    type: [String, Number],
    default: "",
    required: true,
  },
  prefix: {
    type: String,
    default: null,
  },
});
</script>

<template>
  <div
    :class="{ 'justify-content-between': !!prefix }"
    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
    <span class="text-small item-title">:{{ title }}</span>
    <span class="text-small" dir="rtl">{{ text }}</span>
    <span v-if="!!prefix" class="text-small"> {{ prefix }} </span>
  </div>
</template>

<style lang="scss" scoped>
.item-title {
  width: 180px;
  font-weight: bold;
}
</style>
