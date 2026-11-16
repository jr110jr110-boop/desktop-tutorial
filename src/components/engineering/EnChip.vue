<script setup lang="ts">
// A status chip: the text is the server's Persian text, the colour comes from composables/engineering.ts chipOf().
import { chipOf } from "@/composables/engineering";

defineProps<{ group: string; status: number | null | undefined; text?: string | null }>();
</script>

<template>
  <span :class="'inv-badge ' + chipOf(group, status)">{{ text || "—" }}</span>
</template>
