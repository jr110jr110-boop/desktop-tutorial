<script lang="ts" setup>
import type TableValue from "@/types/TableValue";

import KsList from "@/components/common/KsList.vue";
import EditIcon from "@/components/icons/EditIcon.vue";
import FilesIcon from "@/components/icons/FilesIcon.vue";

defineProps({
  equipmentId: {
    type: [String, Number],
    required: true,
  },
});

const tableHeads: string[] = ["مجموعه", "بوم", "فرآیند", "وضعیت فایل"];

const tableValues: TableValue[] = [
  {
    propName: "assemblyTitle",
    type: "text",
  },
  {
    propName: "bomTitle",
    type: "text",
  },
  {
    propName: "processTitle",
    type: "text",
  },
  {
    propName: "isFinalized",
    type: "status",
    status: [
      {
        title: "کامل",
        value: true,
        color: "success",
      },
      {
        title: "بدون فایل",
        value: false,
        color: "warn",
      },
    ],
  },
];
</script>

<template>
  <KsList
    :api-fetch-url="`api/v1/EquipmentProcess/getFilesStatusByEquipmentId/${equipmentId}`"
    :has-add-btn="false"
    :has-delete-btn="false"
    :has-edit-btn="false"
    :has-entry-qnt="false"
    :has-pagination="false"
    :has-search="false"
    :list-title="'فایل های محصول: ' + $route.query.equipmentTitle"
    :table-heads="tableHeads"
    :table-values="tableValues"
    has-back-btn
    has-custom-action-btn>
    <template #listIcon>
      <FilesIcon class="ms-3" width="22" />
    </template>
    <template #custom-action-btn="{ rowData }">
      <li
        class="d-flex flex-row-reverse align-items-center gap-3"
        @click="
          $router.push({
            name: 'EquipmentFileUpdate',
            params: { id: rowData.id },
            query: { ...rowData },
          })
        ">
        <EditIcon width="16" />
        <span class="text-small">ویرایش</span>
      </li>
    </template>
  </KsList>
</template>
