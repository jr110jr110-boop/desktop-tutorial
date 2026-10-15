<template>
  <svg
    :height="width"
    :stroke="fill"
    :width="width"
    class="feather feather-edit-3"
    fill="none"
    stroke-linecap="round"
    stroke-linejoin="round"
    stroke-width="2.5"
    viewBox="0 0 24 24"
    xmlns="http://www.w3.org/2000/svg">
    <line
      :class="{ 'to-minus': toMinus }"
      :stroke="fill"
      x1="12"
      x2="12"
      y1="5"
      y2="19"></line>
    <line :stroke="fill" x1="5" x2="19" y1="12" y2="12"></line>
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
  toMinus: {
    type: Boolean,
    default: false,
  },
});
</script>

<style lang="scss" scoped>
.to-minus {
  transform: rotate(90deg) translate(0, -100%);
}
</style>
