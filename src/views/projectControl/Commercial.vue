<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import Milestones from "@/components/pc/commercial/Milestones.vue";
import Guarantees from "@/components/pc/commercial/Guarantees.vue";
import Penalties from "@/components/pc/commercial/Penalties.vue";
import Variations from "@/components/pc/commercial/Variations.vue";
import PaymentStatementPrint from "@/components/pc/print/PaymentStatementPrint.vue";
import GuaranteeListPrint from "@/components/pc/print/GuaranteeListPrint.vue";
import { printPage } from "@/composables/inventory";
import { pcContractId, usePcPrint } from "@/composables/projectControl";

// One page for the commercial side; the tab is chosen by the route (milestones, guarantees, penalties, variations) so the alert links of the CEO dashboard open the right tab.
const props = defineProps({ tab: { type: String, default: "milestones" } });
const router = useRouter();
const { job, run } = usePcPrint();
const tabs = [
  { key: "milestones", title: "مراحل پرداخت" },
  { key: "guarantees", title: "ضمانت‌نامه‌ها" },
  { key: "penalties", title: "جریمه تاخیر" },
  { key: "variations", title: "دستور تغییر" },
];
const current = computed({ get: () => props.tab, set: (k: string) => router.push({ name: "ProjectControl" + k[0].toUpperCase() + k.slice(1) }) });
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="امور تجاری پروژه" subtitle="مبالغ به ریال" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">امور تجاری پروژه</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none"><ContractSelect /><button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button></div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <PcTabs v-model="current" :tabs="tabs" />
        <Milestones v-if="tab === 'milestones'" :contract-id="pcContractId" @payment-request="(m: any) => run('payment', m)" />
        <Guarantees v-else-if="tab === 'guarantees'" :contract-id="pcContractId" @print="(rows: any[]) => run('guarantees', rows)" />
        <Penalties v-else-if="tab === 'penalties'" :contract-id="pcContractId" />
        <Variations v-else :contract-id="pcContractId" />
      </template>
    </div>
    <PaymentStatementPrint v-if="job?.kind === 'payment'" :m="job.data" />
    <GuaranteeListPrint v-else-if="job?.kind === 'guarantees'" :rows="job.data" :contract-id="pcContractId" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
