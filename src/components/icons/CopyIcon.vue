<template>
  <svg
    :height="width"
    :stroke="fill"
    :width="width"
    class="feather feather-copy"
    fill="none"
    stroke-linecap="round"
    stroke-linejoin="round"
    stroke-width="2"
    viewBox="0 0 24 24"
    xmlns="http://www.w3.org/2000/svg">
    <rect height="13" rx="2" ry="2" width="13" x="9" y="9"></rect>
    <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
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
