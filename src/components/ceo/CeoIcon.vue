<script setup lang="ts">
// Line icons of the CEO dashboard (20×20, stroke = currentColor, so they follow the theme). The status shapes differ by outline as well as colour:
// ok = circle with a check, warn = triangle, danger = octagon, neutral = circle with a dash, info = circle with an i.
defineProps({
  name: { type: String, required: true },
  size: { type: [Number, String], default: 18 },
  /** a label for screen readers; empty = decorative (aria-hidden) */
  label: { type: String, default: "" },
});
</script>

<template>
  <svg
    :aria-hidden="label ? undefined : 'true'"
    :aria-label="label || undefined"
    :height="size"
    :role="label ? 'img' : undefined"
    :width="size"
    class="ceo-icon"
    fill="none"
    stroke="currentColor"
    stroke-linecap="round"
    stroke-linejoin="round"
    stroke-width="1.6"
    viewBox="0 0 20 20">
    <template v-if="name === 'ok'"><circle cx="10" cy="10" r="7.2" /><path d="M6.8 10.2l2.2 2.2 4.2-4.6" /></template>
    <template v-else-if="name === 'warn'"><path d="M10 2.8l7.6 13.4H2.4z" /><path d="M10 8v3.6" /><circle cx="10" cy="14" fill="currentColor" r=".55" stroke="none" /></template>
    <template v-else-if="name === 'danger'"><path d="M7 2.6h6l4.4 4.4v6L13 17.4H7L2.6 13V7z" /><path d="M10 6.4v4.4" /><circle cx="10" cy="13.4" fill="currentColor" r=".6" stroke="none" /></template>
    <template v-else-if="name === 'neutral'"><circle cx="10" cy="10" r="7.2" /><path d="M7 10h6" /></template>
    <template v-else-if="name === 'info'"><circle cx="10" cy="10" r="7.2" /><path d="M10 9v4.6" /><circle cx="10" cy="6.4" fill="currentColor" r=".6" stroke="none" /></template>
    <template v-else-if="name === 'refresh'"><path d="M16.2 7.4A6.6 6.6 0 0 0 4.4 6" /><path d="M3.8 12.6A6.6 6.6 0 0 0 15.6 14" /><path d="M16.4 3.4v4.2h-4.2" /><path d="M3.6 16.6v-4.2h4.2" /></template>
    <template v-else-if="name === 'print'"><path d="M5.6 7.4V3h8.8v4.4" /><rect height="6.4" rx="1.4" width="14.4" x="2.8" y="7.4" /><path d="M5.6 11.6h8.8V17H5.6z" /></template>
    <template v-else-if="name === 'sun'"><circle cx="10" cy="10" r="3.4" /><path d="M10 1.8v2M10 16.2v2M1.8 10h2M16.2 10h2M4.2 4.2l1.4 1.4M14.4 14.4l1.4 1.4M4.2 15.8l1.4-1.4M14.4 5.6l1.4-1.4" /></template>
    <template v-else-if="name === 'moon'"><path d="M16.4 12.6A7 7 0 0 1 7.4 3.6a7 7 0 1 0 9 9z" /></template>
    <template v-else-if="name === 'close'"><path d="M5 5l10 10M15 5L5 15" /></template>
    <template v-else-if="name === 'chevron'"><path d="M7.4 5l5.2 5-5.2 5" /></template>
    <template v-else-if="name === 'chevron-down'"><path d="M5 7.6l5 5 5-5" /></template>
    <template v-else-if="name === 'assistant'"><path d="M3.4 4.6h13.2v8.6H9.2L5.6 16.4v-3.2H3.4z" /><path d="M7 8.9h.01M10 8.9h.01M13 8.9h.01" stroke-width="2.2" /></template>
    <template v-else-if="name === 'table'"><rect height="12.8" rx="1.4" width="14.4" x="2.8" y="3.6" /><path d="M2.8 7.8h14.4M2.8 12h14.4M8 7.8v8.6" /></template>
    <template v-else-if="name === 'chart'"><path d="M3 16.4h14" /><path d="M5.6 13.6V9.4M9 13.6V5.4M12.4 13.6v-6M15.6 13.6V10.8" /></template>
    <template v-else-if="name === 'lock'"><rect height="7.4" rx="1.4" width="11.2" x="4.4" y="9" /><path d="M7 9V6.6a3 3 0 0 1 6 0V9" /></template>
    <template v-else-if="name === 'open'"><path d="M11.4 3.6h5v5" /><path d="M16.4 3.6L9.6 10.4" /><path d="M14.4 11.6v4.2a.8.8 0 0 1-.8.8H4.2a.8.8 0 0 1-.8-.8V6.4a.8.8 0 0 1 .8-.8h4.2" /></template>
    <template v-else-if="name === 'sort'"><path d="M6.6 4.4v11.2M3.8 7.2l2.8-2.8 2.8 2.8M13.4 15.6V4.4M10.6 12.8l2.8 2.8 2.8-2.8" /></template>
    <template v-else-if="name === 'sort-asc'"><path d="M10 15.6V4.4M6.4 8l3.6-3.6L13.6 8" /></template>
    <template v-else-if="name === 'sort-desc'"><path d="M10 4.4v11.2M6.4 12l3.6 3.6 3.6-3.6" /></template>
    <template v-else-if="name === 'list'"><path d="M7.4 5.4h9.2M7.4 10h9.2M7.4 14.6h9.2" /><path d="M3.6 5.4h.01M3.6 10h.01M3.6 14.6h.01" stroke-width="2.2" /></template>
    <template v-else-if="name === 'analysis'"><path d="M10 2.8l1.6 4.2 4.2 1.6-4.2 1.6L10 14.4l-1.6-4.2-4.2-1.6 4.2-1.6z" /><path d="M15.4 13.2l.7 1.8 1.8.7-1.8.7-.7 1.8-.7-1.8-1.8-.7 1.8-.7z" /></template>
    <template v-else-if="name === 'note'"><path d="M4.6 3h7.8l3 3v11H4.6z" /><path d="M12.2 3v3.2h3.2M7.4 10h5.2M7.4 13h3.4" /></template>
    <template v-else-if="name === 'camera'"><path d="M3 6.6h3l1.4-2h5.2l1.4 2h3v9.4H3z" /><circle cx="10" cy="11" r="2.8" /></template>
  </svg>
</template>

<style scoped>
.ceo-icon {
  flex: none;
  display: inline-block;
  vertical-align: middle;
}
</style>
