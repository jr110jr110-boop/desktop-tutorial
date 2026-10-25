<script setup lang="ts">
import { ref } from "vue";
import { useRoute } from "vue-router";
import { hasRole, ProductionRoles } from "@/composables/roles";
import MachinesTab from "@/components/production/master/MachinesTab.vue";
import ReasonCodesTab from "@/components/production/master/ReasonCodesTab.vue";
import WorkCentersTab from "@/components/production/master/WorkCentersTab.vue";

// Work centres, machines and reason codes of production (MfWorkCenters). /production/machines (alias) opens the machines tab.
const route = useRoute();
// FINANCIAL may read the work centres only (machines and reason codes need masterReaders)
const canMaster = hasRole(ProductionRoles.masterReaders);
const tabs: [string, string][] = canMaster ? [["centers", "مراکز کار"], ["machines", "ماشین‌ها"], ["reasons", "دلایل ضایعات و توقف"]] : [["centers", "مراکز کار"]];
const tab = ref<string>(canMaster && route.path.endsWith("/machines") ? "machines" : "centers");
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مراکز کار و ماشین‌ها</h1></div>
      <ul class="nav nav-tabs mb-3">
        <li v-for="t in tabs" :key="t[0]" class="nav-item"><a :class="{ active: tab === t[0] }" class="nav-link" href="#" @click.prevent="tab = t[0]">{{ t[1] }}</a></li>
      </ul>
      <WorkCentersTab v-if="tab === 'centers'" />
      <MachinesTab v-else-if="tab === 'machines'" />
      <ReasonCodesTab v-else />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
