<script setup lang="ts">
// A request that waits for a person never expires: this banner says what waits and for how long (amber under a day, red from a day on).
import { computed } from "vue";
import { waitText } from "@/composables/engineering";

const props = defineProps<{ hours: number | null | undefined; text: string }>();
const red = computed(() => Number(props.hours) >= 24);
</script>

<template>
  <div :class="'en-banner ' + (red ? 'red' : 'amber')" role="alert" data-test="en-wait">
    <strong>{{ text }}</strong>
    <span> — در انتظار: {{ waitText(hours) }}. این درخواست منقضی نمی‌شود و تا تصمیم گرفته شود هشدار دائمی می‌ماند.</span>
  </div>
</template>
