<script lang="ts" setup>
import type TableValue from "@/types/TableValue";
import type FilterQuery from "@/types/FilterQuery";

import KsList from "@/components/common/KsList.vue";
import TagsIcon from "@/components/icons/TagsIcon.vue";
import { Colors } from "@/assets/colors";

const tableHeads: string[] = [
  "شرکت",
  "کد",
  "تاریخ دریافت مناقصه",
  "مهلت پاسخ به کارفرما",
  "نتیجه",
  "شرح مناقصه",
];

const tableValues: TableValue[] = [
  {
    propName: "company",
    type: "text",
  },
  {
    propName: "code",
    type: "text",
  },
  {
    propName: "reciveDate",
    type: "date",
  },
  {
    propName: "tenderDate",
    type: "date",
  },
  {
    propName: "result",
    type: "text",
  },
  {
    propName: "description",
    type: "text",
  },
];

const filterQueries: FilterQuery[] = [
  {
    propName: "FromDate",
    title: "از تاریخ",
    type: "date",
    selected: "",
  },
  {
    propName: "ToDate",
    title: "تا تاریخ",
    type: "date",
    selected: "",
  },
];
</script>

<template>
  <KsList
    :filter-queries="filterQueries"
    :table-heads="tableHeads"
    :table-values="tableValues"
    api-delete-url="api/v1/Tenders/delete-tender-collection"
    api-fetch-url="api/v1/Tenders"
    list-title="مناقصه"
    search-terms="عنوان"
    update-route-name="TenderUpdate">
    <template #listIcon>
      <TagsIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
  </KsList>
</template>
