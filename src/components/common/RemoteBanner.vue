<script setup lang="ts">
import { ref, watch } from "vue";
import { useRoute } from "vue-router";
import { getRemoteStatus } from "@/api/DispatchAPIs";

// Production release P2 part D: a thin line under the navigation bar when the user works from outside the company (VPN). The server already refuses writes there; the line says why
// a save button answers "only for viewing" before anyone tries.
const route = useRoute();
const status = ref<any>(null);
let asked = false;

watch(
  () => route.name,
  async (name) => {
    if (asked || name === "Login" || !localStorage.getItem("token")) return;
    asked = true;
    const [data] = await getRemoteStatus();
    status.value = data ?? null;
  },
  { immediate: true }
);
</script>

<template>
  <div v-if="status?.remote" class="remote-line d-print-none" dir="rtl" role="status">
    {{ status.text }}<span v-if="!status.allowed">؛ نقش شما برای کار از بیرون شرکت مجاز نیست.</span>
  </div>
</template>

<style scoped>
.remote-line { background: #fff4d6; color: #6b4b00; border: 1px solid #ecd28a; border-radius: 8px; margin-bottom: 10px; text-align: center; font-size: 0.82rem; padding: 4px 12px; font-family: IRANYekan, Tahoma, sans-serif; }
:global([data-theme="darkMode"]) .remote-line { background: #3a3220; color: #f2d58e; border-color: #5a4a22; }
</style>
