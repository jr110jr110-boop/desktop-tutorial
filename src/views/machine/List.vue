<script setup lang="ts">
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import { Colors } from "@/assets/colors";
import ClipBoard from "@/components/icons/BallPileIcon.vue";
import type TableValue from "@/types/TableValue";

import KsTable from "@/components/common/KsTable.vue";
import KsButton from "@/components/common/KsButton.vue";
import type Machine from "@/types/Machine";
import { getMachineList } from "@/api/MachineAPIs";
import FlowChartIcon from "@/components/icons/FlowChartIcon.vue";

import showErrorNotification from "@/composables/showErrorNotification";

const machines: Ref<Machine[]> = ref([]);

const loadings = ref({
  btnLoading: false,
  isLoading: false,
  inputLoading: false,
  selectLoading: true,
  deleteBtnLoading: null,
  departmentFactoryList: false,
  factoryMillList: false,
  millModelLoading: null,
  downloadBtnLoading: null,
});

const tableHeads: string[] = ["عنوان"];

const tableValues: TableValue[] = [
  {
    propName: "title",
    type: "text",
  },
];

async function fetchMachine() {
  loadings.value.selectLoading = true;
  const [data, error] = await getMachineList();
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      machines.value = data.data;
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  loadings.value.selectLoading = false;
}

onMounted(async () => {
  await fetchMachine();
});
</script>

<template>
  <div class="background-color p-3 shadow-sm rounded-3">
    <div
      class="d-flex flex-row-reverse justify-content-between align-items-center">
      <div class="list-title">
        <span class="fw-bold text-large text-color">دستگاه</span>
        <ClipBoard :fill="Colors.black" width="24" class="ms-3" />
      </div>
      <div class="d-flex gap-3">
        <KsButton
          color="primary"
          title="افزودن جدید"
          class="d-block"
          @click="$router.push({ name: 'MachineUpdate' })" />
      </div>
    </div>

    <div>
      <transition-group name="fade" mode="out-in">
        <div key="GRTable" class="mt-4" dir="rtl">
          <KsTable
            update-route-name="MachineUpdate"
            :t-heads="tableHeads"
            :t-values="tableValues"
            :t-data="machines"
            :has-delete-btn="false" />
        </div>
      </transition-group>
    </div>
  </div>
</template>
