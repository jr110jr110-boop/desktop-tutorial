<script lang="ts" setup>
import type TableValue from "@/types/TableValue";
import type FilterQuery from "@/types/FilterQuery";

import KsList from "@/components/common/KsList.vue";
import HandShakeIcon from "@/components/icons/HandShakeIcon.vue";
import { Colors } from "@/assets/colors";

const tableHeads: string[] = [
  "کارفرما",
  "کد",
  "تاریخ ثبت",
  "تاریخ اتمام",
  "وضعیت",
];

const tableValues: TableValue[] = [
  {
    propName: "employer",
    type: "text",
  },
  {
    propName: "code",
    type: "text",
    maxWidth: 102,
  },
  {
    propName: "startDate",
    type: "date",
    maxWidth: 200,
  },
  {
    propName: "endDate",
    type: "date",
  },
  {
    propName: "status",
    type: "status",
    status: [
      {
        title: "جاری",
        value: 1,
        color: "primary",
      },
      {
        title: "خاتمه یافته",
        value: 2,
        color: "dark",
      },
      {
        title: "فسخ شده",
        value: 3,
        color: "warn",
      },
    ],
  },
];

const filterQueries: FilterQuery[] = [
  {
    propName: "StartedFrom",
    title: "تاریخ شروع",
    type: "date",
    selected: "",
  },
  {
    propName: "EndedAt",
    title: "تاریخ اتمام",
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
    api-delete-url="api/v1/ContractsForProvider/delete-contract-collection"
    api-fetch-url="api/v1/ContractsForProvider"
    list-title="قراردادهای تامین گنندگان"
    search-terms="کارفرما"
    update-route-name="ProviderContractUpdate">
    <template #listIcon>
      <HandShakeIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
  </KsList>
</template>
