<script setup lang="ts">
// The tabs of the AI admin pages (/ai/admin/...). The pages are read by the PMO and the CEO; only the PMO writes (the buttons are hidden for the CEO, the API refuses anyway).
import { useRoute } from "vue-router";

const route = useRoute();
const tabs = [
  { name: "AiAdminEndpoints", title: "سرورهای هوش مصنوعی" },
  { name: "AiAdminPrompts", title: "نسخه‌های دستور سیستم" },
  { name: "AiAdminSeats", title: "دستیارهای زمان‌بندی‌شده" },
  { name: "AiAdminEval", title: "ارزیابی" },
  { name: "AiAdminAudit", title: "ممیزی" },
];
</script>

<template>
  <ul class="nav nav-tabs mb-3 screen-only">
    <li v-for="t in tabs" :key="t.name" class="nav-item">
      <router-link :class="{ active: route.name === t.name }" :to="{ name: t.name }" class="nav-link">{{ t.title }}</router-link>
    </li>
  </ul>
</template>
