<script setup lang="ts">
import type TableValue from "@/types/TableValue";
import KsList from "@/components/common/KsList.vue";

const tableHeads: string[] = [
  "نام توربین",
  "پیش بینی زمان اجرا",
  "پیش بینی هزینه",
];

const tableValues: TableValue[] = [
  {
    propName: "title",
    type: "text",
  },
  {
    propName: "timeEstimation",
    type: "text",
  },
  {
    propName: "priceEstimation",
    type: "price",
  },
];
</script>

<template>
  <KsList
    :has-add-btn="false"
    :has-edit-btn="false"
    :has-delete-btn="false"
    :has-pagination="false"
    :has-search="false"
    :has-entry-qnt="false"
    :table-heads="tableHeads"
    :table-values="tableValues"
    api-fetch-url="api/v1/Reports/equipmentEstimationReports"
    list-title="برآورد اولیه هزینه و زمان" />
</template>
