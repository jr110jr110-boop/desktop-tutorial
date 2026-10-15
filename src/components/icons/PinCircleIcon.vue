<template>
  <svg
    :stroke="fill"
    class="collapse-toggle-icon feather feather-disc"
    fill="none"
    height="20px"
    stroke-linecap="round"
    stroke-linejoin="round"
    stroke-width="2"
    viewBox="0 0 24 24"
    width="20px"
    xmlns="http://www.w3.org/2000/svg">
    <circle cx="12" cy="12" r="10"></circle>
    <circle v-if="active" cx="12" cy="12" r="3"></circle>
  </svg>
</template>
<script setup>
defineProps({
  fill: {
    type: String,
    required: true,
  },
  active: {
    type: Boolean,
    required: true,
  },
});
</script>
