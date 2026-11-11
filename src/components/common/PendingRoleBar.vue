<script setup lang="ts">
import { computed, watch } from "vue";
import { useRoute } from "vue-router";
import { clearMyPendingRoles, loadMyPendingRoles, myPendingRoles, waitingText } from "@/composables/pendingRoles";

// FX2: a bar at the top of every page when a sensitive role of the signed-in user waits for the second person.
// One call after sign-in / app load (the route leaves the Login page); the list is cleared on the Login page.
const route = useRoute();
let loaded = false;

watch(
  () => route.name,
  async (name) => {
    if (name === "Login") {
      loaded = false;
      clearMyPendingRoles();
      return;
    }
    if (loaded || !localStorage.getItem("token")) return;
    loaded = true;
    await loadMyPendingRoles();
  },
  { immediate: true }
);

const text = computed(() => waitingText(myPendingRoles.value.map((p) => p.role)));
</script>

<template>
  <div v-if="text" class="pending-role-bar d-print-none" dir="rtl" role="alert">{{ text }}</div>
</template>

<style scoped>
.pending-role-bar { background: #fff1c2; color: #5c3d00; border: 1px solid #e6c250; border-radius: 8px; margin-bottom: 10px; text-align: center; font-size: 0.9rem; font-weight: 600; padding: 8px 14px; font-family: IRANYekan, Tahoma, sans-serif; }
:global([data-theme="darkMode"]) .pending-role-bar { background: #40330f; color: #f6dc8a; border-color: #6b5518; }
</style>
