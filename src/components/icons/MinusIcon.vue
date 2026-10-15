<template>
  <svg :width="width" viewBox="0 0 384 512" xmlns="http://www.w3.org/2000/svg">
    <path
      :fill="fill"
      d="M368 224H16c-8.84 0-16 7.16-16 16v32c0 8.84 7.16 16 16 16h352c8.84 0 16-7.16 16-16v-32c0-8.84-7.16-16-16-16z" />
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
