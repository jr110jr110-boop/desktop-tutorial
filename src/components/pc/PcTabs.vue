<script setup lang="ts">
// Tabs of a project control page: [{ key, title, hidden? }].
defineProps({ tabs: { type: Array as () => { key: string; title: string; hidden?: boolean }[], required: true }, modelValue: { type: String, required: true } });
const emit = defineEmits(["update:modelValue"]);
</script>

<template>
  <ul class="nav nav-tabs mb-3 d-print-none">
    <template v-for="t in tabs" :key="t.key">
      <li v-if="!t.hidden" class="nav-item">
        <a :class="{ active: modelValue === t.key }" class="nav-link" href="#" @click.prevent="emit('update:modelValue', t.key)">{{ t.title }}</a>
      </li>
    </template>
  </ul>
</template>
