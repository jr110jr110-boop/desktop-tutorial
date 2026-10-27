<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import TermsTab from "@/components/pc/project/TermsTab.vue";
import InitiationTab from "@/components/pc/project/InitiationTab.vue";
import CharterPlanTab from "@/components/pc/project/CharterPlanTab.vue";
import ReviewsTab from "@/components/pc/project/ReviewsTab.vue";
import AmendmentsTab from "@/components/pc/project/AmendmentsTab.vue";
import { pcContractId, setPcContract } from "@/composables/projectControl";

// Project (contract) workspace: terms and project code, start-up steps, charter, execution plan (PEP), contract reviews and amendments.
const tab = ref("terms");
const tabs = [
  { key: "terms", title: "شرایط و کد پروژه" },
  { key: "initiation", title: "مراحل شروع پروژه" },
  { key: "charter", title: "منشور پروژه" },
  { key: "plan", title: "برنامه اجرایی (PEP)" },
  { key: "reviews", title: "بازنگری قرارداد" },
  { key: "amendments", title: "الحاقیه‌ها" },
];
const bump = ref(0);

// Deep link from sales (proposal page): /project-control/project?contract=<id>&tab=reviews|initiation&review=<id> (the AI item links write contractId)
const route = useRoute();
const openReviewId = ref<number | null>(null);
function applyQuery() {
  const c = Number(route.query.contract ?? route.query.contractId);
  if (c > 0) setPcContract(c);
  const t = String(route.query.tab || "");
  if (tabs.some((x) => x.key === t)) tab.value = t;
  const rv = Number(route.query.review);
  openReviewId.value = rv > 0 ? rv : null;
}
onMounted(applyQuery);
watch(() => route.query, applyQuery);
</script>

<template>
  <div class="inv-page ks-report">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">فضای کار پروژه</h1>
        <ContractSelect />
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <PcTabs v-model="tab" :tabs="tabs" />
        <TermsTab v-if="tab === 'terms'" :key="'t' + bump" :contract-id="pcContractId" @changed="bump++" />
        <InitiationTab v-else-if="tab === 'initiation'" :key="'i' + bump" :contract-id="pcContractId" @goto="tab = $event" />
        <CharterPlanTab v-else-if="tab === 'charter'" kind="charter" :contract-id="pcContractId" @changed="bump++" />
        <CharterPlanTab v-else-if="tab === 'plan'" kind="plan" :contract-id="pcContractId" @changed="bump++" />
        <ReviewsTab v-else-if="tab === 'reviews'" :contract-id="pcContractId" :open-id="openReviewId" />
        <AmendmentsTab v-else-if="tab === 'amendments'" :contract-id="pcContractId" />
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
