<script setup lang="ts">
import { ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import DeliverablesRegister from "@/components/pc/registers/DeliverablesRegister.vue";
import EvaluationsRegister from "@/components/pc/registers/EvaluationsRegister.vue";
import IssuesRegister from "@/components/pc/registers/IssuesRegister.vue";
import LessonsRegister from "@/components/pc/registers/LessonsRegister.vue";
import PhasesRegister from "@/components/pc/registers/PhasesRegister.vue";
import PhotosRegister from "@/components/pc/registers/PhotosRegister.vue";
import RisksRegister from "@/components/pc/registers/RisksRegister.vue";
import StakeholdersRegister from "@/components/pc/registers/StakeholdersRegister.vue";
import WeeklyRegister from "@/components/pc/registers/WeeklyRegister.vue";
import { printPage } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";

// PMBOK registers of the project: risks, issues, stakeholders, lessons learned, deliverables, photos, evaluations, weekly report, phase evaluation.
const route = useRoute();
const tab = ref(typeof route.query.tab === "string" && route.query.tab ? route.query.tab : "risks");
const tabs = [
  { key: "risks", title: "ریسک‌ها" }, { key: "issues", title: "مسائل" }, { key: "stakeholders", title: "ذی‌نفعان" }, { key: "lessons", title: "درس‌های آموخته" },
  { key: "deliverables", title: "تحویلی‌ها" }, { key: "photos", title: "عکس‌های پروژه" }, { key: "evaluations", title: "ارزیابی پیمانکار و مشاور" },
  { key: "weekly", title: "گزارش هفتگی" }, { key: "phases", title: "ارزیابی فاز" },
];
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="ثبت‌های مدیریت پروژه" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">ثبت‌های مدیریت پروژه (PMBOK)</h1>
        <div class="d-flex flex-wrap gap-2 d-print-none"><ContractSelect /><button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button></div>
      </div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <PcTabs v-model="tab" :tabs="tabs" />
        <RisksRegister v-if="tab === 'risks'" :contract-id="pcContractId" />
        <IssuesRegister v-else-if="tab === 'issues'" :contract-id="pcContractId" />
        <StakeholdersRegister v-else-if="tab === 'stakeholders'" :contract-id="pcContractId" />
        <LessonsRegister v-else-if="tab === 'lessons'" :contract-id="pcContractId" />
        <DeliverablesRegister v-else-if="tab === 'deliverables'" :contract-id="pcContractId" />
        <PhotosRegister v-else-if="tab === 'photos'" :contract-id="pcContractId" />
        <EvaluationsRegister v-else-if="tab === 'evaluations'" :contract-id="pcContractId" />
        <WeeklyRegister v-else-if="tab === 'weekly'" :contract-id="pcContractId" />
        <PhasesRegister v-else :contract-id="pcContractId" />
      </template>
    </div>
  </div>
</template>
<style lang="scss" src="../inventory/inventory.scss"></style>
