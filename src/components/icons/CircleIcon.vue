<template>
  <svg
    :height="width"
    :stroke="fill"
    :width="width"
    class="feather feather-circle"
    fill="none"
    stroke-linecap="round"
    stroke-linejoin="round"
    stroke-width="4"
    viewBox="0 0 24 24"
    xmlns="http://www.w3.org/2000/svg">
    <circle cx="12" cy="12" r="10"></circle>
  </svg>
</template>
<script setup>
defineProps({
  fill: {
    type: String,
    default: "#000000",
  },
  width: {
    type: String,
    default: "16",
  },
});
</script>
