<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import type TableValue from "@/types/TableValue";
import type FilterQuery from "@/types/FilterQuery";
import type FilterQueryValue from "@/types/FilterQueryValue";
import { getContractList } from "@/api/ContractAPIs";

import KsList from "@/components/common/KsList.vue";
import CpuIcon from "@/components/icons/CpuIcon.vue";
import ShoppingBoxIcon from "@/components/icons/ShoppingBoxIcon.vue";
import FilesIcon from "@/components/icons/FilesIcon.vue";
import { Colors } from "@/assets/colors";

const contractList: Ref<FilterQueryValue[]> = ref([]);

const tableHeads: string[] = ["عنوان", "کد"];

const tableValues: TableValue[] = [
  {
    propName: "title",
    type: "text",
  },
  {
    propName: "code",
    type: "text",
  },
];

const filterQueries: FilterQuery[] = [
  {
    propName: "ContractId",
    title: "قرارداد",
    type: "select",
    values: contractList.value,
    selected: "",
  },
];

async function fetchContractList() {
  const [data, error] = await getContractList();
  if (!error)
    for (let i = 0; i < data.data.data.length; i++) {
      contractList.value.push({
        title: data.data.data[i].employer,
        value: data.data.data[i].id,
      });
    }
}

function setIdToLocalStorage(rowId) {
  window.sessionStorage.setItem("equipmentId", rowId);
}

onMounted(() => {
  fetchContractList();
});
</script>

<template>
  <KsList
    :filter-queries="filterQueries"
    :table-heads="tableHeads"
    :table-values="tableValues"
    api-delete-url="api/v1/Equipments/delete-equipment-collection"
    api-fetch-url="api/v1/Equipments"
    list-title="محصولات"
    search-terms="عنوان"
    update-route-name="EquipmentUpdate">
    <template #listIcon>
      <ShoppingBoxIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
    <template #custom-action-btn="{ rowData }">
      <li
        class="d-flex flex-row-reverse align-items-center gap-3"
        @click="
          $router.push({
            name: 'EquipmentAssemblyList',
            params: { equipmentId: rowData.id },
          }),
            setIdToLocalStorage(rowData.id)
        ">
        <CpuIcon :fill="Colors.black" width="16" />
        <span class="text-small">زیر مجموعه</span>
      </li>
      <li
        class="d-flex flex-row-reverse align-items-center gap-3"
        @click="
          $router.push({
            name: 'EquipmentFileList',
            params: { equipmentId: rowData.id },
            query: { equipmentTitle: rowData.title },
          })
        ">
        <FilesIcon :fill="Colors.black" width="13" />
        <span class="text-small">MDL</span>
      </li>
    </template>
  </KsList>
</template>
