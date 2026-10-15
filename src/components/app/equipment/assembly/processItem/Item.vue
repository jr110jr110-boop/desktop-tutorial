<script lang="ts" setup>
import type EquipmentProcess from "@/types/EquipmentProcess";
import type { PropType } from "vue";
import { computed, ref } from "vue";
import { useEquipmentAssembly } from "@/store";
import { deleteEquipmentProcess } from "@/api/EquipmentAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import Update from "./Update.vue";
import DeleteModal from "@/components/app/DeleteModal.vue";
import KsButton from "@/components/common/KsButton.vue";
import MenuIcon from "@/components/icons/MenuIcon.vue";
import EyeIcon from "@/components/icons/EyeIcon.vue";
import TrashIcon from "@/components/icons/TrashIcon.vue";
import EditIcon from "@/components/icons/EditIcon.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import { Colors } from "@/assets/colors";
import CopyIcon from "@/components/icons/CopyIcon.vue";

const props = defineProps({
  process: {
    type: Object as PropType<EquipmentProcess>,
    required: true
  },
  isLastItem: {
    type: Boolean,
    default: false
  },
  index: {
    type: Number,
    required: true
  },
  bomId: {
    type: [Number, String],
    required: true
  }
});

const {
  updateProcessModal,
  onGoingProcessSwipe,
  onPreviewModalRequest,
  onCloneProcess,
  fetchEquipmentAssemblyList
} = useEquipmentAssembly.value;

const deleteModal = ref({
  visible: false,
  submitLoading: false
});

const requestDeleteEquipmentProcess = async () => {
  deleteModal.value.submitLoading = true;
  const [data, error] = await deleteEquipmentProcess(props.process.id);

  if (!data?.success)
    showErrorNotification(data?.message || error.message);
  else {
    await fetchEquipmentAssemblyList(false, false);
    showSuccessNotification("فرآیند تامین با موفقیت حذف شد");
  }

  deleteModal.value = {
    visible: false,
    submitLoading: false
  };
};

const openUpdateProcessModal = () => {
  updateProcessModal.value = {
    visible: true,
    process: props.process,
    bomId: props.bomId,
    isEdit: true
  };
};

const isCurrentProcessSwiping = computed(
  () =>
    onGoingProcessSwipe.value.bomId === props.bomId &&
    onGoingProcessSwipe.value.processIndex === props.index
);

const onSortProcess = (isUp) => {
  onGoingProcessSwipe.value = {
    processIndex: props.index,
    bomId: props.bomId,
    isUp
  };
};
</script>

<template>
  <div
    :class="{
      'opacity-25 no-pointer-event': isCurrentProcessSwiping,
    }"
    class="accordion-btn">
    <div
      class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-sm-center"
      dir="rtl">
      <div class="flex-1 d-flex gap-3 align-items-center">
        <div class="d-flex flex-column pe-sm-2">
          <div
            :class="{
              'no-pointer-event opacity-25': index === 0,
            }"
            class="sort-btn"
            @click="onSortProcess(true)">
            <AngleLeftIcon style="transform: rotate(90deg)" width="6" />
          </div>
          <div
            :class="{
              'no-pointer-event opacity-25': isLastItem,
            }"
            class="sort-btn"
            @click="onSortProcess(false)">
            <AngleLeftIcon style="transform: rotate(-90deg)" width="6" />
          </div>
        </div>

        <span class="text-small">
          {{ index + 1 }}.
          <span class="pe-2">{{ process.title }}</span>
        </span>
      </div>
      <div class="flex-1 d-flex gap-2">
        <span class="text-small">زمان برآورد:</span>
        <span class="text-small">{{ process.processDuration }} روز</span>
      </div>
      <div class="flex-1 d-flex gap-2">
        <span class="text-small color-primary-light">درصد وزنی:</span>
        <span class="text-small color-primary-light">{{
            parseFloat(process.weightPercent).toFixed(2)
          }}</span>
      </div>
      <div class="p-2 align-self-end">
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
                @click="
                  onPreviewModalRequest({
                    mode: 'process',
                    process,
                  })
                ">
                <EyeIcon :fill="Colors.black" width="14" />
                <span class="text-small">اطلاعات فرآیند</span>
              </li>
              <li
                class="d-flex flex-row-reverse align-items-center gap-3"
                @click="
                  onCloneProcess(process)
                ">
                <CopyIcon :fill="Colors.black" width="14" />
                <span class="text-small">کپی فرآیند</span>
              </li>
              <li
                class="d-flex flex-row-reverse align-items-center gap-3"
                @click="openUpdateProcessModal">
                <EditIcon :fill="Colors.black" width="14" />
                <span class="text-small">ویرایش</span>
              </li>
              <li
                class="d-flex flex-row-reverse align-items-center gap-3"
                @click="deleteModal.visible = true">
                <TrashIcon :fill="Colors.black" width="14" />
                <span class="text-small">حذف</span>
              </li>
            </ul>
          </template>
        </KsButton>
      </div>
    </div>

    <Update
      v-if="
        updateProcessModal.visible &&
        updateProcessModal.isEdit &&
        updateProcessModal.process.id === process.id
      " />

    <DeleteModal
      :button-loading="deleteModal.submitLoading"
      :visible="deleteModal.visible"
      @cancel="deleteModal.visible = false"
      @submit="requestDeleteEquipmentProcess" />
  </div>
</template>
