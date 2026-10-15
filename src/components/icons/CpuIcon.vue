<template>
  <svg
    :height="width"
    :stroke="fill"
    :width="width"
    class="feather feather-cpu"
    fill="none"
    stroke-linecap="round"
    stroke-linejoin="round"
    stroke-width="2"
    viewBox="0 0 24 24"
    xmlns="http://www.w3.org/2000/svg">
    <rect height="16" rx="2" ry="2" width="16" x="4" y="4"></rect>
    <rect height="6" width="6" x="9" y="9"></rect>
    <line x1="9" x2="9" y1="1" y2="4"></line>
    <line x1="15" x2="15" y1="1" y2="4"></line>
    <line x1="9" x2="9" y1="20" y2="23"></line>
    <line x1="15" x2="15" y1="20" y2="23"></line>
    <line x1="20" x2="23" y1="9" y2="9"></line>
    <line x1="20" x2="23" y1="14" y2="14"></line>
    <line x1="1" x2="4" y1="9" y2="9"></line>
    <line x1="1" x2="4" y1="14" y2="14"></line>
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
