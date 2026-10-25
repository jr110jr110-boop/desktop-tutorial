<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import MntKpis from "@/components/production/mnt/MntKpis.vue";
import MntOrderDetail from "@/components/production/mnt/MntOrderDetail.vue";
import MntOrders from "@/components/production/mnt/MntOrders.vue";
import MntPlans from "@/components/production/mnt/MntPlans.vue";
import MntRequests from "@/components/production/mnt/MntRequests.vue";
import { useMachineOptions } from "@/components/production/mnt/mntCommon";
import { hasRole, ProductionRoles } from "@/composables/roles";

// Maintenance (MfMaintenance): breakdown reports, orders (+ detail), preventive plans and machine KPIs.
// Routes: /production/maintenance (tabs), /production/maintenance-orders/:id (that order), /production/maintenance-requests/:id (breakdown report).
const route = useRoute();
const router = useRouter();
const { machines, loadMachines } = useMachineOptions();
const tab = ref<"requests" | "orders" | "plans" | "kpis">("orders");
const fromRequest = ref<any>(null);
const requestsRef = ref<any>(null);
const canKpi = hasRole(ProductionRoles.readers);

const orderId = computed(() => (route.name === "MfMaintenanceOrder" ? Number(route.params.id) || null : null));
const requestId = computed(() => (route.name === "MfMaintenanceRequest" ? Number(route.params.id) || null : null));

function sync() {
  if (orderId.value) tab.value = "orders";
  else if (requestId.value) tab.value = "requests";
}
function setTab(t: typeof tab.value) {
  tab.value = t;
  if (orderId.value || requestId.value) router.push("/production/maintenance");
}
function createFrom(r: any) {
  fromRequest.value = r;
  tab.value = "orders";
}
function created() {
  fromRequest.value = null;
}

watch(() => route.fullPath, sync);
onMounted(() => {
  sync();
  loadMachines();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">نگهداری و تعمیرات</h1>
      </div>
      <ul class="nav nav-tabs mb-3">
        <li class="nav-item"><button :class="{ active: tab === 'requests' }" class="nav-link" @click="setTab('requests')">گزارش‌های خرابی</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'orders' }" class="nav-link" @click="setTab('orders')">دستورهای نگهداری</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'plans' }" class="nav-link" @click="setTab('plans')">برنامه‌های نگهداری</button></li>
        <li v-if="canKpi" class="nav-item"><button :class="{ active: tab === 'kpis' }" class="nav-link" @click="setTab('kpis')">شاخص‌ها</button></li>
      </ul>

      <MntRequests v-if="tab === 'requests'" ref="requestsRef" :highlight-id="requestId" :machines="machines" @create="createFrom" />
      <template v-else-if="tab === 'orders'">
        <MntOrderDetail v-if="orderId" :id="orderId" :key="orderId" />
        <MntOrders v-else :from-request="fromRequest" :machines="machines" @done="created" />
      </template>
      <MntPlans v-else-if="tab === 'plans'" :machines="machines" />
      <MntKpis v-else-if="tab === 'kpis' && canKpi" :machines="machines" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
