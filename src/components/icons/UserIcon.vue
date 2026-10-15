<template>
  <svg
    :height="width"
    :stroke="fill"
    :width="width"
    class="feather feather-user"
    fill="none"
    stroke-linecap="round"
    stroke-linejoin="round"
    stroke-width="2"
    viewBox="0 0 24 24"
    xmlns="http://www.w3.org/2000/svg">
    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path>
    <circle cx="12" cy="7" r="4"></circle>
  </svg>
</template>
<script setup>
defineProps({
  fill: {
    type: String,
    required: true,
  },
  width: {
    type: String,
    required: true,
  },
});
</script>
