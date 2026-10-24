<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import ReportTables from "@/components/fn/ReportTables.vue";
import { distributionFinance } from "@/composables/fn";

// A4 print of a ledger report: journal, general ledger, account review, trial balance, balance sheet, income statement,
// project profitability, reconciliation check. `fields` carry the period and the filters.
defineProps<{ kind: string; title: string; data: any; fields: { label: string; value: any }[]; columns?: number }>();
const signatures = [{ title: "تهیه‌کننده (حسابداری)" }, { title: "مدیر مالی" }];
const statementSignatures = [{ title: "تهیه‌کننده (حسابداری)" }, { title: "مدیر مالی" }, { title: "مدیرعامل" }];
</script>

<template>
  <PrintForm
    class="q-form"
    :distribution="distributionFinance"
    :fields="fields"
    :signatures="['balance-sheet', 'income-statement', 'cash-flow', 'equity', 'ratios', 'comparative'].includes(kind) ? statementSignatures : signatures"
    :title="title">
    <ReportTables :columns="columns" :data="data" :kind="kind" />
  </PrintForm>
</template>
