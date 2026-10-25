<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRoute } from "vue-router";
import RevisionsTab from "@/components/production/master/RevisionsTab.vue";
import StandardsTab from "@/components/production/master/StandardsTab.vue";

// Operation standards per item and routing revisions (MfRoutings). /production/routings/:id opens that revision on the revisions tab.
const route = useRoute();
const openId = computed(() => (route.params.id ? Number(route.params.id) : null));
const tab = ref<"standards" | "revisions">(openId.value ? "revisions" : "standards");
watch(openId, (id) => id && (tab.value = "revisions"));
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مسیر ساخت و استانداردهای عملیات</h1></div>
      <ul class="nav nav-tabs mb-3">
        <li class="nav-item"><a :class="{ active: tab === 'standards' }" class="nav-link" href="#" @click.prevent="tab = 'standards'">استانداردهای عملیات</a></li>
        <li class="nav-item"><a :class="{ active: tab === 'revisions' }" class="nav-link" href="#" @click.prevent="tab = 'revisions'">نسخه‌های مسیر ساخت</a></li>
      </ul>
      <StandardsTab v-if="tab === 'standards'" />
      <RevisionsTab v-else :open-id="openId" />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
