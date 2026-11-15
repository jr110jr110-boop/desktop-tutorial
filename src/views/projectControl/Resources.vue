<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import ResourcePool from "@/components/pc/resources/ResourcePool.vue";
import ResourceRates from "@/components/pc/resources/ResourceRates.vue";
import ResourcePlan from "@/components/pc/resources/ResourcePlan.vue";
import { printPage } from "@/composables/inventory";
import { pcContractId, setPcContract } from "@/composables/projectControl";

// Resources of project control (priority 4): the pool, the standard rates and the resource plan of a project.
// The planning agent drafts the plan; people review, edit and approve it here. The links of the gate inbox open this page:
//   /project-control/resources?contract=<id>              the plan of that project
//   /project-control/resources?tab=rates[&resource=<id>]  the standard rates (of one resource)
const props = defineProps({ tab: { type: String, default: "pool" } });
const route = useRoute();
const current = ref(props.tab);
const resourceId = ref<number | null>(null);
const tabs = [
  { key: "pool", title: "منابع" },
  { key: "rates", title: "نرخ استاندارد منابع" },
  { key: "plan", title: "برنامه منابع پروژه" },
];

function applyQuery() {
  const contract = Number(route.query.contract ?? route.query.contractId);
  if (contract > 0) setPcContract(contract);
  const wanted = String(route.query.tab || "");
  if (tabs.some((t) => t.key === wanted)) current.value = wanted;
  else if (contract > 0) current.value = "plan";
  else current.value = props.tab;
  const resource = Number(route.query.resource);
  resourceId.value = resource > 0 ? resource : null;
}
onMounted(applyQuery);
watch(() => route.query, applyQuery);
watch(() => props.tab, applyQuery);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="منابع پروژه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">منابع، نرخ استاندارد و برنامه منابع</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <ContractSelect v-if="current === 'plan'" />
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <PcTabs v-model="current" :tabs="tabs" />
      <ResourcePool v-if="current === 'pool'" />
      <ResourceRates v-else-if="current === 'rates'" :resource-id="resourceId" />
      <template v-else>
        <NeedContract v-if="!pcContractId" />
        <ResourcePlan v-else :contract-id="pcContractId" />
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
