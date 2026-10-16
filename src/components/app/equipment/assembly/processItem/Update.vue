<script lang="ts" setup>
import type EquipmentProcess from "@/types/EquipmentProcess";
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import { useEquipmentAssembly } from "@/store";
import { EQUIPMENT_PROCESS_MODEL } from "@/models";
import { getWorkflowGroupList } from "@/api/WorkFlowGroupAPIs";
import { getMachineList } from "@/api/MachineAPIs";
import type WorkflowGroup from "@/types/WorkFlowGroup";
import type Machine from "@/types/Machine";

import KsInput from "@/components/common/KsInput.vue";
import KsInputSeperate from "@/components/common/KsInputSeperate.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import UpdateModal from "../_common/UpdateModal.vue";
import showErrorNotification from "@/composables/showErrorNotification";

const workflowGroups: Ref<WorkflowGroup[]> = ref([]);
const machinesList: Ref<Machine[]> = ref([]);
const { updateProcessModal, updateEquipmentProcess } =
  useEquipmentAssembly.value;

const updatingProcess: Ref<EquipmentProcess> = ref({
  ...EQUIPMENT_PROCESS_MODEL,
});
const submitButtonLoading = ref(false);
const processTypeList = [
  { title: "خرید کالا", value: "1" },
  { title: "خرید خدمت", value: "2" },
  { title: "مونتاژ", value: "3" },
  { title: "مونتاژ نهایی", value: "4" },
  { title: "تولید", value: "5" },
];

const changeMachineId = async (option: { value: number }) => {
  updatingProcess.value.machineId = option.value;
};

const onUpdateEquipmentProcess = async () => {
  submitButtonLoading.value = true;
  await updateEquipmentProcess(
    updatingProcess.value,
    updateProcessModal.value.isEdit
  );

  submitButtonLoading.value = false;
};

const parseEquipmentProcessWeightPercentToDecimal = () => {
  if (updatingProcess.value.weightPercent)
    updatingProcess.value.weightPercent = parseFloat(
      updatingProcess.value.weightPercent
    ).toFixed(2);
};

async function fetchMachine() {
  submitButtonLoading.value = true;
  const [data, error] = await getMachineList();
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      machinesList.value = data.data;
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  submitButtonLoading.value = false;
}

async function fetchWorkflowGroup() {
  submitButtonLoading.value = true;

  const [data, error] = await getWorkflowGroupList();
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      workflowGroups.value = data.data;
    }
  }
  submitButtonLoading.value = false;
}

onMounted(async () => {
  await fetchMachine();
  if (updateProcessModal.value.isEdit) {
    updatingProcess.value = { ...updateProcessModal.value.process };
  }
  await fetchWorkflowGroup();

  updatingProcess.value.equipmentBomId = updateProcessModal.value.bomId;
});
</script>

<template>
  <UpdateModal
    :is-edit="updateProcessModal.isEdit"
    :model-value="updateProcessModal.visible"
    :submit-loading="submitButtonLoading"
    @cancel="updateProcessModal.visible = false"
    @submit="onUpdateEquipmentProcess">
    <div class="ps-3" style="overflow-y: auto; height: 67vh">
      <div class="d-flex flex-column gap-5" dir="rtl">
        <span>
          {{
            updateProcessModal.isEdit
              ? "ویرایش " + updateProcessModal.process.title
              : "افزودن فرآیند تامین"
          }}
        </span>
        <KsInput
          v-model="updatingProcess.title"
          class="flex-1"
          placeholder="عنوان" />
        <KsSelect
          v-model="updatingProcess.type"
          :options="processTypeList"
          class="flex-1"
          placeholder="انتخاب فرآیند" />
        <KsSelect
          v-if="updatingProcess.type == '5'"
          v-model="updatingProcess.machineId"
          :options="machinesList"
          value-prop-name="id"
          label-prop-name="title"
          class="flex-1"
          placeholder="دستگاه"
          @onOptionClick="changeMachineId"
          custom-handle-option-click />
        <KsInput
          v-model="updatingProcess.enterCommodityCode"
          class="flex-1"
          placeholder="کد کالای ورودی" />
        <KsInput
          v-model="updatingProcess.exitCommodityCode"
          :is-disabled="updatingProcess.type === '1'"
          class="flex-1"
          placeholder="کد کالای خروجی" />
        <KsInput
          v-model="updatingProcess.weightPercent"
          class="flex-1"
          input-type="number"
          placeholder="درصد وزنی"
          step="0.10"
          @blur="parseEquipmentProcessWeightPercentToDecimal" />
        <KsInput
          v-model="updatingProcess.mapDuration"
          append-text="روز"
          class="flex-1"
          input-type="number"
          placeholder="مدت پیش بینی زمان تولید مدارک" />
        <KsInput
          v-model="updatingProcess.masterDuration"
          append-text="روز"
          class="flex-1"
          input-type="number"
          placeholder="مدت پیش بینی زمان تایید کارفرما" />
        <KsInput
          v-model="updatingProcess.processDuration"
          append-text="روز"
          class="flex-1"
          input-type="number"
          placeholder="مدت پیش بینی زمان فرآیند تامین" />
        <KsInputSeperate
          v-model="updatingProcess.programPrice"
          append-text="تومان"
          class="flex-1"
          placeholder="هزینه" />
        <KsSelect
          :loading="submitButtonLoading"
          :options="workflowGroups"
          v-model="updatingProcess.workflowId"
          class="flex-1"
          placeholder="دستگردان مدارک"
          label-prop-name="title"
          value-prop-name="id" />
      </div>
    </div>
  </UpdateModal>
</template>
