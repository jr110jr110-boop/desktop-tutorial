<script lang="ts" setup>
import type { PropType } from "vue";
import type EquipmentBom from "@/types/EquipmentBom";
import { useEquipmentAssembly } from "@/store";

import KsButton from "@/components/common/KsButton.vue";
import MenuIcon from "@/components/icons/MenuIcon.vue";
import CodeBranchIcon from "@/components/icons/CodeBranchIcon.vue";
import EyeIcon from "@/components/icons/EyeIcon.vue";
import TrashIcon from "@/components/icons/TrashIcon.vue";
import EditIcon from "@/components/icons/EditIcon.vue";
import CopyIcon from "@/components/icons/CopyIcon.vue";
import { Colors } from "@/assets/colors";

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
    required: true,
    default: () => [],
  },
});

const emits = defineEmits(["onDelete"]);

const {
  updateBomModal,
  updateProcessModal,
  clonedProcess,
  pasteLoading,
  onCloneBom,
  updateEquipmentProcess,
  onPreviewModalRequest,
} = useEquipmentAssembly.value;

const onEditBom = () => {
  updateBomModal.value = {
    visible: true,
    isEdit: true,
    ...props,
  };
};

const onAddProcess = () => {
  updateProcessModal.value = {
    visible: true,
    process: null,
    isEdit: false,
    bomId: props.bom.id,
  };
};

const onPasteClonedProcess = async () => {
  pasteLoading.value = true;
  clonedProcess.value.equipmentBomId = props.bom.id;
  await updateEquipmentProcess(clonedProcess.value, false, true);
  pasteLoading.value = false;
};
</script>

<template>
  <div
    class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
    dir="rtl">
    <div class="flex-1 d-flex gap-3">
      <span class="text-small">{{ bom.title }}</span>
    </div>
    <div class="flex-1 d-flex gap-2 align-items-center">
      <span class="text-small color-primary-light"> جمع زمان برآورد: </span>
      <span class="text-small color-primary-light">
        {{ bom.totalDuration }} روز
      </span>
    </div>
    <div class="flex-1 d-flex gap-2 align-items-center">
      <span class="text-small color-primary-light"> جمع درصد وزنی: </span>
      <span class="text-small color-primary-light">
        {{ parseFloat(bom.totalWeightPercent).toFixed(2) }}
      </span>
    </div>

    <div @click.stop>
      <KsButton
        color="dark"
        custom-padding="0"
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
              @click="onAddProcess">
              <CodeBranchIcon :fill="Colors.black" width="14" />
              <span class="text-small">افزودن فرآیند</span>
            </li>
            <li
              v-if="!!clonedProcess"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="onPasteClonedProcess">
              <CopyIcon :fill="Colors.black" width="14" />
              <span class="text-small">چسباندن فرآیند</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                onPreviewModalRequest({
                  mode: 'bom',
                  bom,
                })
              ">
              <EyeIcon :fill="Colors.black" width="16" />
              <span class="text-small">اطلاعات BOM</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="onCloneBom(bom)">
              <CopyIcon :fill="Colors.black" width="14" />
              <span class="text-small">کپی Bom</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="onEditBom">
              <EditIcon :fill="Colors.black" width="16" />
              <span class="text-small">ویرایش</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="emits('onDelete')">
              <TrashIcon :fill="Colors.black" width="16" />
              <span class="text-small">حذف</span>
            </li>
          </ul>
        </template>
      </KsButton>
    </div>
  </div>
</template>
