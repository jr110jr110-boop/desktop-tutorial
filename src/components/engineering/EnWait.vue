<script setup lang="ts">
// A request that waits for a person never expires: this banner says what waits and for how long (amber from 48 office hours, red from 72: the allowed limit).
import { computed } from "vue";
import { waitText } from "@/composables/engineering";

const props = defineProps<{ hours: number | null | undefined; text: string }>();
const known = computed(() => props.hours !== null && props.hours !== undefined);
const red = computed(() => Number(props.hours) >= 72);
const amber = computed(() => Number(props.hours) >= 48);
const cls = computed(() => (red.value ? "red" : amber.value ? "amber" : "blue"));
</script>

<template>
  <div :class="'en-banner ' + cls" role="alert" data-test="en-wait" title="حد مجاز ۷۲ ساعت اداری">
    <strong>{{ text }}</strong>
    <span v-if="known"> — در انتظار: {{ waitText(hours) }}.</span>
    <span class="small"> (حد مجاز ۷۲ ساعت اداری؛ ساعت اداری: ۸ تا ۱۹، شنبه تا پنجشنبه.)</span>
    <span> این درخواست منقضی نمی‌شود و تا تصمیم گرفته شود هشدار دائمی می‌ماند.</span>
  </div>
</template>
