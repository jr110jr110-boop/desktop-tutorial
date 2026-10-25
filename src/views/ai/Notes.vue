<script setup lang="ts">
// «یادداشت‌های هوش مصنوعی»: what the seats left for people, as far as the signed-in user may read it (the server filters by the roles of the module of each note).
import AiNotesList from "@/components/ai/AiNotesList.vue";
</script>

<template>
  <div class="inv-page">
    <AiNotesList />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
