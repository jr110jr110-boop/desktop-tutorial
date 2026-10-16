<script setup lang="ts">
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import { Colors } from "@/assets/colors";
import ClipBoard from "@/components/icons/BallPileIcon.vue";
import type TableValue from "@/types/TableValue";

import KsTable from "@/components/common/KsTable.vue";
import KsButton from "@/components/common/KsButton.vue";
import type WorkflowGroup from "@/types/WorkFlowGroup";
import { getWorkflowGroupList } from "@/api/WorkFlowGroupAPIs";
import FlowChartIcon from "@/components/icons/FlowChartIcon.vue";

import showErrorNotification from "@/composables/showErrorNotification";

const workflowGroups: Ref<WorkflowGroup[]> = ref([]);

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

async function fetchWorkflowGroup() {
  loadings.value.selectLoading = true;
  const [data, error] = await getWorkflowGroupList();
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      workflowGroups.value = data.data;
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  loadings.value.selectLoading = false;
}

onMounted(async () => {
  await fetchWorkflowGroup();
});
</script>

<template>
  <div class="background-color p-3 shadow-sm rounded-3">
    <div
      class="d-flex flex-row-reverse justify-content-between align-items-center">
      <div class="list-title">
        <span class="fw-bold text-large text-color">دستگردان مدارک</span>
        <ClipBoard :fill="Colors.black" width="24" class="ms-3" />
      </div>
      <div class="d-flex gap-3">
        <KsButton
          color="primary"
          title="افزودن جدید"
          class="d-block"
          @click="$router.push({ name: 'WorkflowGroupUpdate' })" />
      </div>
    </div>

    <div>
      <transition-group name="fade" mode="out-in">
        <div key="GRTable" class="mt-4" dir="rtl">
          <KsTable
            update-route-name="WorkflowGroupUpdate"
            :t-heads="tableHeads"
            :t-values="tableValues"
            :t-data="workflowGroups"
            :has-delete-btn="false">
            <template #custom-action-btn="{ rowIndex }">
              <li
                class="d-flex flex-row-reverse align-items-center gap-3"
                @click="
                  $router.push({
                    name: 'WorkflowChart',
                    params: { id: workflowGroups[rowIndex].id },
                  })
                ">
                <FlowChartIcon :fill="Colors.black" width="16" />
                <span class="text-small">فلوچارت</span>
              </li>
            </template>
          </KsTable>
        </div>
      </transition-group>
    </div>
  </div>
</template>
