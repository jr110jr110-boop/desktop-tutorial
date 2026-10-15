<script lang="ts" setup>
import type EquipmentBom from "@/types/EquipmentBom";
import type { PropType } from "vue";
import { ref, watch } from "vue";
import { useEquipmentAssembly } from "@/store";
import {
  deleteEquipmentBom,
  editEquipmentProcessSort,
} from "@/api/EquipmentAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import KsAccordion from "@/components/common/KsAccordion.vue";
import DeleteModal from "@/components/app/DeleteModal.vue";
import Header from "./Header.vue";
import Update from "./Update.vue";
import AddProcessModal from "../processItem/Update.vue";

const props = defineProps({
  bom: {
    type: Object as PropType<EquipmentBom>,
    required: true,
  },
  assemblyId: {
    type: [String, Number],
    required: true,
  },
  otherBoms: {
    type: Array as PropType<EquipmentBom[]>,
    default: () => [],
    required: true,
  },
});

const {
  updateBomModal,
  updateProcessModal,
  onGoingProcessSwipe,
  fetchEquipmentAssemblyList,
} = useEquipmentAssembly.value;

const deleteModal = ref({
  visible: false,
  submitLoading: false,
});

const requestDeleteEquipmentBom = async () => {
  deleteModal.value.submitLoading = true;
  const [data, error] = await deleteEquipmentBom(props.bom.id);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    await fetchEquipmentAssemblyList(false, false);
    showSuccessNotification("BOM با موفقیت حذف شد");
  }
  deleteModal.value.visible = false;
  deleteModal.value.submitLoading = false;
};

watch(
  () => onGoingProcessSwipe.value.bomId,
  async (curr, prev) => {
    if (curr === props.bom.id) await updateEquipmentProcessSort();
  }
);

const updateEquipmentProcessSort = async () => {
  const { isUp, processIndex: firstIndex } = onGoingProcessSwipe.value;
  const secondIndex = isUp ? firstIndex - 1 : firstIndex + 1;
  const [data, error] = await editEquipmentProcessSort({
    id1: props.bom.equipmentProcesses[secondIndex].id,
    id2: props.bom.equipmentProcesses[firstIndex].id,
  });

  if (!data?.success)
    showErrorNotification(data?.message || error.message || "خطایی رخ داد");
  else await fetchEquipmentAssemblyList(false, false);

  onGoingProcessSwipe.value = {
    processIndex: null,
    bomId: null,
    isUp: null,
  };
};
</script>

<template>
  <div>
    <KsAccordion btn-classes="px-2 py-1" class="accordion-btn">
      <template #header>
        <Header v-bind="props" @onDelete="deleteModal.visible = true" />
      </template>
      <template #content>
        <div class="d-flex flex-column gap-3 ps-1 pe-3 py-1">
          <hr class="m-0" />
          <span
            v-if="!bom.equipmentProcesses || !bom.equipmentProcesses.length"
            class="text-xSmall text-center fw-bold">
            لیست فرآیند تامین خالی است
          </span>
          <slot />
        </div>
      </template>
    </KsAccordion>

    <Update
      v-if="
        updateBomModal.visible &&
        updateBomModal.isEdit &&
        updateBomModal.bom.id === bom.id
      " />

    <DeleteModal
      :button-loading="deleteModal.submitLoading"
      :visible="deleteModal.visible"
      @cancel="deleteModal.visible = false"
      @submit="requestDeleteEquipmentBom" />

    <AddProcessModal
      v-if="
        updateProcessModal.visible &&
        !updateProcessModal.isEdit &&
        updateProcessModal.bomId === bom.id
      " />
  </div>
</template>
