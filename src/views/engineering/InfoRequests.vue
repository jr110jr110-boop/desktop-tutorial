<script setup lang="ts">
import { computed } from "vue";
import { useRoute } from "vue-router";
import EnInfoList from "@/components/engineering/EnInfoList.vue";

// The questions of the units to the PMO (a unit user sees its own unit's questions; the PMO, the CEO and the auditor see all).
const route = useRoute();
const focus = computed(() => Number(route.params.id) || null);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">پرسش واحدها از PMO</h1></div>
      <EnInfoList :highlight-id="focus" />
    </div>
  </div>
</template>
