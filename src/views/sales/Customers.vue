<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import PcTabs from "@/components/pc/PcTabs.vue";
import ActivitiesTab from "@/components/sales/pipeline/ActivitiesTab.vue";
import ContactsTab from "@/components/sales/pipeline/ContactsTab.vue";
import CustomersTab from "@/components/sales/pipeline/CustomersTab.vue";
import LegacyLinkTab from "@/components/sales/pipeline/LegacyLinkTab.vue";
import SitesTab from "@/components/sales/pipeline/SitesTab.vue";

// Customers (SlCustomers / SlCustomer): profile, contacts, sites, activities and the legacy contract / tender linker. /sales/customers/:id opens that customer.
const route = useRoute();
const router = useRouter();
const openId = computed(() => (route.params.id ? Number(route.params.id) : null));
const tab = ref("customers");
const tabs = [
  { key: "customers", title: "مشتریان" },
  { key: "contacts", title: "مخاطبان" },
  { key: "sites", title: "سایت‌ها" },
  { key: "activities", title: "فعالیت‌ها" },
  { key: "legacy", title: "پیوند قراردادهای قدیمی" },
];
watch(openId, (id) => id && (tab.value = "customers"), { immediate: true });
function closed() {
  if (route.params.id) router.replace({ name: "SlCustomers" });
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مشتریان و ارتباط با مشتری</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <CustomersTab v-if="tab === 'customers'" :open-id="openId" @open="closed" />
      <ContactsTab v-else-if="tab === 'contacts'" />
      <SitesTab v-else-if="tab === 'sites'" />
      <ActivitiesTab v-else-if="tab === 'activities'" />
      <LegacyLinkTab v-else />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
