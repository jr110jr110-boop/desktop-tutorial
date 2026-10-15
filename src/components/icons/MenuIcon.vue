<template>
  <svg :width="width" viewBox="0 0 448 512" xmlns="http://www.w3.org/2000/svg">
    <path
      :fill="fill"
      d="M442 114H6a6 6 0 0 1-6-6V84a6 6 0 0 1 6-6h436a6 6 0 0 1 6 6v24a6 6 0 0 1-6 6zm0 160H6a6 6 0 0 1-6-6v-24a6 6 0 0 1 6-6h436a6 6 0 0 1 6 6v24a6 6 0 0 1-6 6zm0 160H6a6 6 0 0 1-6-6v-24a6 6 0 0 1 6-6h436a6 6 0 0 1 6 6v24a6 6 0 0 1-6 6z" />
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
