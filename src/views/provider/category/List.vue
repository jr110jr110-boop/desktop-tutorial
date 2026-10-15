<script setup lang="ts">
import type TableValue from "@/types/TableValue";
import KsList from "@/components/common/KsList.vue";

const tableHeads: string[] = ["دسته بندی"];

const tableValues: TableValue[] = [
  {
    propName: "category",
    type: "text",
  },
];
</script>

<template>
  <KsList
    :table-heads="tableHeads"
    :table-values="tableValues"
    :has-search="false"
    :has-entry-qnt="false"
    :has-pagination="false"
    :send-form-params="false"
    api-delete-url="api/v1/ProviderCategories/delete"
    api-fetch-url="api/v1/ProviderCategories"
    list-title="دسته بندی تامین کنندگان"
    update-route-name="providerCategoryUpdate" />
</template>
