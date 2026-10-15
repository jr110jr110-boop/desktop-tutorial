<script setup lang="ts">
import KsSkeletonLoading from "@/components/common/KsSkeletonLoading.vue";
defineProps({
  active: {
    type: Boolean,
    default: false,
  },
});
</script>

<template>
  <div
    v-if="active"
    :class="{ 'w-100 vh-100': active }"
    class="position-relative">
    <KsSkeletonLoading class="list-loader" />
  </div>
</template>
