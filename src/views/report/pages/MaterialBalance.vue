<script setup lang="ts">
import type TableValue from "@/types/TableValue";
import type Equipment from "@/types/Equipment";
import { onMounted, ref } from "vue";
import { getEquipmentList } from "@/api/EquipmentAPIs";
import KsList from "@/components/common/KsList.vue";
import KsSelect from "@/components/common/KsSelect.vue";

const tableHeads: string[] = [
  "بوم",
  "تعداد مورد نیاز",
  "تعداد موجود",
  "کسری مواد",
];

const tableValues: TableValue[] = [
  {
    propName: "bomTitle",
    type: "text",
  },
  {
    propName: "neededCount",
    type: "text",
  },
  {
    propName: "existingCount",
    type: "text",
  },
  {
    propName: "",
    type: "text",
  },
];

const equipmentId = ref(null);
const equipmentList = ref<Equipment[]>([]);
const equipmentListLoading = ref(true);
const refetchData = ref(false);

const triggerRefetchData = () => {
  refetchData.value = true;
  setTimeout(() => {
    refetchData.value = false;
  }, 100);
};

const receiveEquipmentList = async () => {
  equipmentListLoading.value = true;
  const [data, error] = await getEquipmentList();
  if (!error) equipmentList.value = data.data.data;
  equipmentListLoading.value = false;
};

onMounted(async () => {
  await receiveEquipmentList();
  if (equipmentList.value.length) equipmentId.value = equipmentList.value[0].id;
});
</script>

<template>
  <div class="d-flex flex-column gap-3">
    <div class="d-flex align-items-center gap-3" dir="rtl">
      <span> انتخاب محصول: </span>
      <KsSelect
        v-model="equipmentId"
        :loading="equipmentListLoading"
        :options="equipmentList"
        style="min-width: 200px"
        label-prop-name="title"
        value-prop-name="id"
        placeholder=""
        @update:modelValue="triggerRefetchData" />
    </div>
    <KsList
      v-if="equipmentId"
      :re-fetch-data="refetchData"
      :has-add-btn="false"
      :has-edit-btn="false"
      :has-delete-btn="false"
      :has-pagination="false"
      :has-search="false"
      :has-entry-qnt="false"
      :table-heads="tableHeads"
      :table-values="tableValues"
      :api-fetch-url="`api/v1/Reports/materialBalance_equipmentId/${equipmentId}`"
      list-title="بالانس مواد و احتیاجات" />
  </div>
</template>
