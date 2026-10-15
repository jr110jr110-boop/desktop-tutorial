<script lang="ts" setup>
import type TableValue from "@/types/TableValue";
import type ListCustomGetParam from "@/types/ListCustomGetParam";

import KsList from "@/components/common/KsList.vue";
import CpuIcon from "@/components/icons/CpuIcon.vue";
import BallPileIcon from "@/components/icons/BallPileIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  workOrderId: {
    type: String,
    required: true,
  },
});

const tableHeads: string[] = [
  "عنوان",
  "کد",
  "زمان اجرا (روز)",
  "جمع درصد وزنی",
];

const tableValues: TableValue[] = [
  {
    propName: "title",
    type: "text",
  },
  {
    propName: "code",
    type: "text",
  },
  {
    propName: "assemblerDuration",
    type: "text",
  },
  {
    propName: "totalWeightPercent",
    type: "text",
  },
];

const getParams: ListCustomGetParam[] = [
  {
    propName: "WorkOrderId",
    value: props.workOrderId,
  },
];
</script>

<template>
  <div>
    <KsList
      :custom-get-params="getParams"
      :table-heads="tableHeads"
      :table-values="tableValues"
      :update-route-queries="{ workOrderId, parentId: null, parentTitle: null }"
      api-delete-url="api/v1/Assemblies/delete-assembly-collection"
      api-fetch-url="api/v1/Assemblies"
      has-back-btn
      has-node
      node-prop-name="children"
      list-title="زیر مجموعه"
      search-terms="عنوان"
      update-route-name="WorkOrderSubAssemblyUpdate">
      <template #listIcon>
        <CpuIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #custom-action-btn="{ rowData }">
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            $router.push({
              name: 'WorkOrderSubAssemblyUpdate',
              query: {
                workOrderId,
                parentId: rowData.id,
                parentTitle: rowData.title,
              },
            })
          ">
          <CpuIcon :fill="Colors.black" width="16" />
          <span class="text-small">افزودن مجموعه</span>
        </li>
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            $router.push({
              name: 'WorkOrderBomList',
              params: { assemblyId: rowData.id },
            })
          ">
          <BallPileIcon :fill="Colors.black" width="16" />
          <span class="text-small">لیست Bom</span>
        </li>
      </template>
    </KsList>
  </div>
</template>
