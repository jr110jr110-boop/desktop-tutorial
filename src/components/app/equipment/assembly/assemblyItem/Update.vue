<script lang="ts" setup>
import { computed, onMounted, ref } from "vue";
import { useEquipmentAssembly } from "@/store";
import { EQUIPMENT_ASSEMBLY_MODEL } from "@/models";

import KsInput from "@/components/common/KsInput.vue";
import UpdateModal from "../_common/UpdateModal.vue";

const { config, updateAssemblyModal, updateEquipmentAssembly } =
  useEquipmentAssembly.value;
const updatingAssembly = ref({
  ...EQUIPMENT_ASSEMBLY_MODEL,
  equipmentId: config.value.equipmentId,
});
const submitButtonLoading = ref(false);

const onUpdateEquipmentAssembly = async () => {
  submitButtonLoading.value = true;
  await updateEquipmentAssembly(
    updatingAssembly.value,
    updateAssemblyModal.value.isEdit,
    updateAssemblyModal.value.parentId
  );
  submitButtonLoading.value = false;
};

const getModalTitle = computed(() => {
  const isEdit = updateAssemblyModal.value.isEdit
    ? "ویرایش " + updateAssemblyModal.value.assembly.title
    : "افزودن  زیر مجموعه";
  const isChild = updateAssemblyModal.value.parentId
    ? " در " + updateAssemblyModal.value.parentTitle
    : "";
  return isEdit + isChild;
});

onMounted(() => {
  if (updateAssemblyModal.value.isEdit)
    updatingAssembly.value = { ...updateAssemblyModal.value.assembly };
});
</script>

<template>
  <UpdateModal
    :is-edit="updateAssemblyModal.isEdit"
    :model-value="updateAssemblyModal.visible"
    :submit-loading="submitButtonLoading"
    @cancel="updateAssemblyModal.visible = false"
    @submit="onUpdateEquipmentAssembly">
    <div class="ps-3 d-flex flex-column gap-5">
      <span dir="rtl">{{ getModalTitle }}</span>
      <KsInput
        v-model="updatingAssembly.title"
        placeholder="عنوان زیر مجموعه" />
      <KsInput v-model="updatingAssembly.code" placeholder="کد زیر مجموعه" />
      <KsInput
        v-model="updatingAssembly.assemblerDuration"
        append-text="روز"
        input-type="number"
        placeholder="زمان اجرا"
        simple-placeholder />
    </div>
  </UpdateModal>
</template>
