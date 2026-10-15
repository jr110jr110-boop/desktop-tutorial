<script lang="ts" setup>
import type EquipmentAssembly from "@/types/EquipmentAssembly";
import type { PropType } from "vue";
import { ref } from "vue";
import { deleteEquipmentAssembly } from "@/api/EquipmentAPIs";
import { useEquipmentAssembly } from "@/store";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import Header from "./Header.vue";
import BomItem from "../bomItem";
import ProcessItem from "../processItem";
import AddBomModal from "../bomItem/Update.vue";
import Update from "./Update.vue";
import KsAccordion from "@/components/common/KsAccordion.vue";
import DeleteModal from "@/components/app/DeleteModal.vue";

const props = defineProps({
  assembly: {
    type: Object as PropType<EquipmentAssembly>,
    required: true,
  },
});

const { fetchEquipmentAssemblyList, updateAssemblyModal, updateBomModal } =
  useEquipmentAssembly.value;

const deleteModal = ref({
  visible: false,
  submitLoading: false,
});

const onDeleteEquipmentAssembly = async (id: string | number = null) => {
  deleteModal.value.submitLoading = true;
  const [data, error] = await deleteEquipmentAssembly(id ?? props.assembly.id);
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    await fetchEquipmentAssemblyList(false, false);
    showSuccessNotification("زیر مجموعه با موفقیت حذف شد");
  }
  deleteModal.value.visible = false;
  deleteModal.value.submitLoading = false;
};
</script>

<template>
  <div>
    <KsAccordion btn-classes="px-2 py-1" class="accordion-btn">
      <template #header>
        <Header :assembly="assembly" @delete="deleteModal.visible = true" />
      </template>
      <template #content>
        <div class="d-flex flex-column gap-3 pe-3 ps-1 py-1">
          <div v-if="assembly.children.length" class="d-flex flex-column gap-3">
            <hr class="m-0" />
            <Item
              v-for="(assemblyChild, indexAssembly) in assembly.children"
              :key="indexAssembly"
              :assembly="assemblyChild"
              :index="indexAssembly">
              <BomItem
                v-for="(bom, indexBom) in assemblyChild.equipmentBoms"
                :key="indexBom"
                :assembly-id="assemblyChild.id"
                :bom="bom"
                :other-boms="assemblyChild.equipmentBoms">
                <transition-group
                  v-if="bom.equipmentProcesses && bom.equipmentProcesses.length"
                  class="d-flex flex-column gap-2"
                  name="list"
                  tag="div">
                  <ProcessItem
                    v-for="(process, indexProcess) in bom.equipmentProcesses"
                    :key="indexProcess"
                    :bom-id="bom.id"
                    :index="indexProcess"
                    :is-last-item="
                      indexProcess === bom.equipmentProcesses.length - 1
                    "
                    :process="process" />
                </transition-group>
              </BomItem>
            </Item>
          </div>
          <hr class="m-0" />
          <span
            v-if="!assembly.equipmentBoms || !assembly.equipmentBoms.length"
            class="text-xSmall text-center fw-bold"
            dir="rtl">
            لیست BOM خالی است
          </span>
          <div
            v-if="assembly.equipmentBoms && assembly.equipmentBoms.length"
            class="d-flex flex-column gap-2">
            <slot />
          </div>
        </div>
      </template>
    </KsAccordion>

    <Update
      v-if="
        updateAssemblyModal.visible &&
        (updateAssemblyModal.assembly.id === assembly.id ||
          updateAssemblyModal.parentId === assembly.id)
      " />

    <DeleteModal
      v-model="deleteModal.visible"
      :button-loading="deleteModal.submitLoading"
      @cancel="deleteModal.visible = false"
      @submit="onDeleteEquipmentAssembly" />

    <AddBomModal
      v-if="
        updateBomModal.visible &&
        !updateBomModal.isEdit &&
        updateBomModal.assemblyId === assembly.id
      " />
  </div>
</template>
