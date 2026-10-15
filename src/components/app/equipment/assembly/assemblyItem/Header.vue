<script lang="ts" setup>
import type EquipmentAssembly from "@/types/EquipmentAssembly";
import type { PropType } from "vue";
import { ref } from "vue";
import { useEquipmentAssembly } from "@/store";
import { EQUIPMENT_ASSEMBLY_MODEL } from "@/models";

import KsButton from "@/components/common/KsButton.vue";
import MenuIcon from "@/components/icons/MenuIcon.vue";
import ClockIcon from "@/components/icons/ClockIcon.vue";
import CpuIcon from "@/components/icons/CpuIcon.vue";
import BallPileIcon from "@/components/icons/BallPileIcon.vue";
import TrashIcon from "@/components/icons/TrashIcon.vue";
import EditIcon from "@/components/icons/EditIcon.vue";
import { Colors } from "@/assets/colors";
import CopyIcon from "@/components/icons/CopyIcon.vue";

const props = defineProps({
  assembly: {
    type: Object as PropType<EquipmentAssembly>,
    required: true,
  },
});

const emits = defineEmits(["delete"]);

const {
  updateAssemblyModal,
  updateBomModal,
  clonedBom,
  clonedAssembly,
  pasteLoading,
  onCloneAssembly,
  updateEquipmentAssembly,
  updateEquipmentBom,
} = useEquipmentAssembly.value;

const openUpdateAssemblyModal = (isEdit: boolean) => {
  updateAssemblyModal.value = {
    visible: true,
    assembly: isEdit ? props.assembly : { ...EQUIPMENT_ASSEMBLY_MODEL },
    isEdit,
    parentId: !isEdit ? props.assembly.id : null,
    parentTitle: !isEdit ? props.assembly.title : "",
  };
};

const openUpdateBomModal = () => {
  updateBomModal.value = {
    visible: true,
    isEdit: false,
    bom: null,
    assemblyId: props.assembly.id,
    otherBoms: props.assembly.equipmentBoms,
  };
};

const onPasteClonedBom = async () => {
  pasteLoading.value = true;
  clonedBom.value.equipmentAssemblyId = props.assembly.id;
  await updateEquipmentBom(
    clonedBom.value,
    false,
    clonedBom.value.predecessorId ?? null
  );
  pasteLoading.value = false;
};

const onPasteClonedAssembly = async () => {
  pasteLoading.value = true;
  clonedAssembly.value.equipmentId = props.assembly.equipmentId;
  await updateEquipmentAssembly(clonedAssembly.value, false, props.assembly.id);
  pasteLoading.value = false;
};
</script>

<template>
  <div
    class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
    dir="rtl">
    <div
      class="flex-1 d-flex flex-column flex-sm-row align-items-center gap-2 gap-sm-3">
      <span class="flex-1 text-small">{{ assembly.title }}</span>
      <span class="flex-1 text-small color-primary fw-bold" dir="ltr">
        #{{ assembly.code }}
      </span>
      <div class="flex-1 d-flex gap-2 align-items-center">
        <ClockIcon :fill="Colors.primaryLight" width="14" />
        <span class="text-small color-primary-light">{{
          assembly.assemblerDuration
        }}</span>
      </div>
      <div class="flex-1 d-flex gap-2 align-items-center">
        <span class="text-small color-primary-light">جمع درصد وزنی:</span>
        <span class="text-small color-primary-light">
          {{ parseFloat(assembly.totalWeightPercent).toFixed(2) }}
        </span>
      </div>
    </div>
    <div @click.stop>
      <KsButton
        color="dark"
        fixed-width-height="32"
        has-drop-down-menu
        menu-direction="left"
        theme="flat">
        <template #content>
          <MenuIcon width="14" />
        </template>
        <template #dropdown-menu>
          <ul @click.stop>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="openUpdateAssemblyModal(false)">
              <CpuIcon :fill="Colors.black" width="16" />
              <span class="text-small">افزودن مجموعه</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="onCloneAssembly(assembly)">
              <CopyIcon :fill="Colors.black" width="16" />
              <span class="text-small">کپی مجموعه</span>
            </li>
            <li
              v-if="!!clonedAssembly"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="onPasteClonedAssembly">
              <CopyIcon :fill="Colors.black" width="16" />
              <span class="text-small">چسباندن مجموعه</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="openUpdateBomModal">
              <BallPileIcon :fill="Colors.black" width="16" />
              <span class="text-small">افزودن BOM</span>
            </li>
            <li
              v-if="!!clonedBom"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="onPasteClonedBom">
              <CopyIcon :fill="Colors.black" width="16" />
              <span class="text-small">چسباندن BOM</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="openUpdateAssemblyModal(true)">
              <EditIcon :fill="Colors.black" width="16" />
              <span class="text-small">ویرایش</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="emits('delete')">
              <TrashIcon :fill="Colors.black" width="16" />
              <span class="text-small">حذف</span>
            </li>
          </ul>
        </template>
      </KsButton>
    </div>
  </div>
</template>
