<script setup lang="ts">
// «دستیار هوشمند»: the assistant as its own page, for every signed-in user. The same panel sits on the CEO dashboard.
import AssistantPanel from "@/components/ai/AssistantPanel.vue";
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <AssistantPanel />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
