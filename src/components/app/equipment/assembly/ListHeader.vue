<script lang="ts" setup>
import { useEquipmentAssembly } from "@/store";
import type { PropType } from "vue";
import {
  ENTRIES_QNT_OPTIONS,
  EQUIPMENT_ASSEMBLY_MODEL,
  EQUIPMENT_ASSEMBLY_UPDATE_MODAL_MODEL,
} from "@/models";
import showWarnNotification from "@/composables/showWarnNotification";

import KsInput from "@/components/common/KsInput.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import KsButton from "@/components/common/KsButton.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import CpuIcon from "@/components/icons/CpuIcon.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import SearchIcon from "@/components/icons/SearchIcon.vue";
import { Colors } from "@/assets/colors";

const {
  config,
  updateAssemblyModal,
  clonedAssembly,
  pasteLoading,
  updateEquipmentAssembly,
  fetchEquipmentAssemblyList,
} = useEquipmentAssembly.value;
const entriesQntOptions = ENTRIES_QNT_OPTIONS;

const openUpdateAssemblyModal = () => {
  updateAssemblyModal.value = {
    ...EQUIPMENT_ASSEMBLY_UPDATE_MODAL_MODEL,
    visible: true,
    assembly: { ...EQUIPMENT_ASSEMBLY_MODEL },
  };
};

const onEntriesQntSubmit = (val) => {
  if (val) {
    let qnt;
    if (val === "همه") qnt = 0;
    else qnt = parseInt(val);

    if (qnt || qnt === 0) {
      config.value.entriesQnt = qnt;
      fetchEquipmentAssemblyList(true);
    } else
      showWarnNotification("مقدار وارد شده برای تعداد نمایش باید عدد باشد");
  }
};

const onPasteEquipmentAssembly = async () => {
  pasteLoading.value = true;
  await updateEquipmentAssembly(clonedAssembly.value, false, null);
  pasteLoading.value = false;
};
</script>

<template>
  <div>
    <KsButton
      class="floating-btn d-sm-none"
      fixed-width-height="44"
      radius="circle"
      @click="openUpdateAssemblyModal">
      <template #content>
        <PlusIcon fill="#ffffff" width="18" />
      </template>
    </KsButton>
    <div
      class="d-flex flex-row-reverse justify-content-between align-items-center">
      <div class="list-title">
        <span class="fw-bold text-large text-color">زیر مجموعه ها</span>
        <CpuIcon :fill="Colors.black" class="ms-3" width="24" />
      </div>
      <div class="d-flex gap-3">
        <KsButton
          color="dark"
          fixed-width-height="42"
          theme="flat"
          @click="$router.back()">
          <template #content>
            <AngleLeftIcon :fill="Colors.primary" width="6" />
          </template>
        </KsButton>
        <KsButton
          class="d-none d-sm-block"
          color="primary"
          title="افزودن زیر مجموعه"
          @click="openUpdateAssemblyModal" />
        <KsButton
          v-if="!!clonedAssembly"
          class="d-none d-sm-block"
          theme="flat"
          color="primary"
          title="چسباندن زیر مجموعه"
          @click="onPasteEquipmentAssembly" />
      </div>
    </div>
    <div
      class="d-flex flex-column flex-lg-row gap-3 gap-lg-5 justify-content-between my-4 align-items-lg-center"
      dir="rtl">
      <div class="rtl flex-1 d-sm-flex align-items-center gap-1" dir="rtl">
        <KsInput
          v-model="config.searchQuery"
          :placeholder="'جستجو بر اساس '"
          class="flex-fill"
          icon="right"
          input-type="text"
          simple-placeholder
          style="max-width: 520px"
          @blur="fetchEquipmentAssemblyList(true)">
          <template #icon>
            <SearchIcon :fill="Colors.primary" width="16" />
          </template>
        </KsInput>
      </div>
      <div
        class="flex-shrink-1 d-flex align-items-center justify-content-end flex-row-reverse gap-2 mt-3 mt-sm-0 text-small">
        <KsSelect
          v-model="config.tempEntriesQnt"
          :options="entriesQntOptions"
          class="flex-fill"
          label-prop-name="title"
          mode="search"
          placeholder="تعداد نمایش"
          simple-placeholder
          submit-btn
          value-prop-name="val"
          @onSubmitClick="onEntriesQntSubmit" />
        <span class="fw-light" style="width: 80px">تعداد نمایش:</span>
      </div>
    </div>
    <hr class="opacity-30" />
  </div>
</template>
