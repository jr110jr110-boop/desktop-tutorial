<script lang="ts" setup>
import type EquipmentBom from "@/types/EquipmentBom";
import type { Ref, PropType } from "vue";
import { onMounted, ref } from "vue";
import { EQUIPMENT_BOM_MODEL, EQUIPMENT_BOM_PREDECESSOR_MODEL } from "@/models";
import { useEquipmentAssembly } from "@/store";
import { deleteEquipmentBomPredecessor } from "@/api/EquipmentAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import type EquipmentBomListAll from "@/types/EquipmentBomAll";

import UpdateModal from "../_common/UpdateModal.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsTextArea from "@/components/common/KsTextArea.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import { getEquipmentBomListAll } from "@/api/EquipmentAPIs";

const { updateBomModal, updateEquipmentBom } = useEquipmentAssembly.value;

const updatingBom: Ref<EquipmentBom> = ref({ ...EQUIPMENT_BOM_MODEL });
const equipmentBomPredecessor = ref({ ...EQUIPMENT_BOM_PREDECESSOR_MODEL });
const equipmentBomListLoading = ref(false);
const submitButtonLoading = ref(false);
const equipmentBomListAllInfo: Ref<EquipmentBomListAll[]> = ref([
  { id: 0, title: "" },
]);
const equipmentId = ref();
const removeEquipmentBomPredecessorRequest = async () => {
  equipmentBomPredecessor.value.equipmentBomId = null;
  if (
    equipmentBomPredecessor.value.equipmentBomId == null ||
    equipmentBomPredecessor.value.equipmentBomId == ""
  ) {
    equipmentBomPredecessor.value.equipmentBomId = null;
  }
  if (equipmentBomPredecessor.value.equipmentBomId) {
    equipmentBomListLoading.value = true;
    const [data, error] = await deleteEquipmentBomPredecessor(
      equipmentBomPredecessor.value.equipmentBomId
    );
    const isSuccess = !error && data?.success;
    if (!isSuccess) showErrorNotification(data?.message || error.message);
    equipmentBomListLoading.value = false;
  }
};

const onUpdateEquipmentBom = async () => {
  submitButtonLoading.value = true;
  if (updatingBom.value.material == null) {
    updatingBom.value.material = "0";
  }
  if (updatingBom.value.standard == null) {
    updatingBom.value.standard = "0";
  }

  await updateEquipmentBom(
    updatingBom.value,
    updateBomModal.value.isEdit,
    equipmentBomPredecessor.value.equipmentBomId
  );
  submitButtonLoading.value = false;
};

async function fetchEquipmentBomListAll() {
  equipmentBomListLoading.value = true;
  const [data, error] = await getEquipmentBomListAll(equipmentId.value);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
  } else {
    equipmentBomListAllInfo.value = data.data.data;
  }
  equipmentBomListLoading.value = false;
}

const lengthAppendArray = [
  { id: "1", text: "میلیمتر" },
  { id: "2", text: "سانتیمتر" },
  { id: "3", text: "متر" },
];

const weightAppendArray = [
  { id: "1", text: "گرم" },
  { id: "2", text: "کیلوگرم" },
  { id: "3", text: "تن" },
];

onMounted(() => {
  equipmentId.value = window.sessionStorage.getItem("equipmentId");
  if (updateBomModal.value.isEdit) {
    updatingBom.value = { ...updateBomModal.value.bom };

    if (updateBomModal.value.bom.predecessorId) {
      equipmentBomPredecessor.value.predecessorId =
        updateBomModal.value.bom.predecessorId;
      equipmentBomPredecessor.value.equipmentBomId =
        updateBomModal.value.bom.id;
    }
  }

  updatingBom.value.equipmentAssemblyId = updateBomModal.value.assemblyId;
  equipmentBomPredecessor.value.equipmentBomList =
    updateBomModal.value.otherBoms;
  fetchEquipmentBomListAll();
});
</script>

<template>
  <UpdateModal
    :is-edit="updateBomModal.isEdit"
    :model-value="updateBomModal.visible"
    :submit-loading="submitButtonLoading"
    @cancel="updateBomModal.visible = false"
    @submit="onUpdateEquipmentBom">
    <div class="ps-3" style="overflow-y: auto; height: 67vh">
      <div class="d-flex flex-column gap-5" dir="rtl">
        <span>{{
          updateBomModal.isEdit
            ? "ویرایش " + updateBomModal.bom.title
            : "افزودن BOM"
        }}</span>
        <KsInput
          v-model="updatingBom.title"
          class="flex-1"
          placeholder="نام قطعه" />
        <KsInput
          v-model="updatingBom.positionNumber"
          class="flex-1"
          input-type="number"
          placeholder="شماره موقعیت" />
        <KsInput
          v-model="updatingBom.count"
          class="flex-1"
          input-type="number"
          placeholder="تعداد در نقشه" />
        <KsInput v-model="updatingBom.unit" class="flex-1" placeholder="واحد" />
        <KsInput
          v-model="updatingBom.material"
          class="flex-1"
          placeholder="متریال" />
        <KsInput
          v-model="updatingBom.standard"
          class="flex-1"
          placeholder="استاندارد" />
        <KsInput
          v-model="updatingBom.diagonal"
          append-text="میلیمتر"
          class="flex-1"
          placeholder="قطر"
          :appendTextIsDropdown="true"
          :appendTextOptions="lengthAppendArray"
          :dropdownValue="updatingBom.diagonalUnit || ''"
          @update:appendTextSelectedId="updatingBom.diagonalUnit = $event" />
        <KsInput
          v-model="updatingBom.length"
          append-text="میلیمتر"
          class="flex-1"
          placeholder="طول"
          :appendTextIsDropdown="true"
          :appendTextOptions="lengthAppendArray"
          :dropdownValue="updatingBom.lengthUnit || ''"
          @update:appendTextSelectedId="updatingBom.lengthUnit = $event" />

        <KsInput
          v-model="updatingBom.width"
          append-text="میلیمتر"
          class="flex-1"
          placeholder="عرض"
          :appendTextIsDropdown="true"
          :appendTextOptions="lengthAppendArray"
          :dropdownValue="updatingBom.widthUnit || ''"
          @update:appendTextSelectedId="updatingBom.widthUnit = $event" />
        <KsInput
          v-model="updatingBom.height"
          append-text="میلیمتر"
          class="flex-1"
          placeholder="ارتفاع"
          :appendTextIsDropdown="true"
          :appendTextOptions="lengthAppendArray"
          :dropdownValue="updatingBom.heightUnit || ''"
          @update:appendTextSelectedId="updatingBom.heightUnit = $event" />
        <KsInput
          v-model="updatingBom.notWetWeight"
          append-text="گرم"
          class="flex-1"
          input-type="number"
          placeholder="وزن ناخالص"
          :appendTextIsDropdown="true"
          :appendTextOptions="weightAppendArray"
          :dropdownValue="updatingBom.notWetWeightUnit || ''"
          @update:appendTextSelectedId="
            updatingBom.notWetWeightUnit = $event
          " />
        <KsInput
          v-model="updatingBom.wetWeight"
          append-text="گرم"
          class="flex-1"
          input-type="number"
          placeholder="وزن خالص"
          :appendTextIsDropdown="true"
          :appendTextOptions="weightAppendArray"
          :dropdownValue="updatingBom.wetWeightUnit || ''"
          @update:appendTextSelectedId="updatingBom.wetWeightUnit = $event" />
        <KsTextArea
          v-model="updatingBom.description"
          class="flex-1"
          placeholder="توضیحات" />
        <div class="flex-1 d-flex flex-column gap-3">
          <span class="text-small fw-bold" dir="rtl">
            انتخاب BOM پیش نیاز (اختیاری):
          </span>

          <KsSelect
            v-model="updatingBom.predecessorId"
            :loading="equipmentBomListLoading"
            :options="equipmentBomListAllInfo"
            placeholder="Bom پیش نیاز مورد نظر را انتخاب کنید"
            label-prop-name="title"
            value-prop-name="id"
            class="flex-1"
            simple-placeholder
            remove-btn
            @removeBtnClick="removeEquipmentBomPredecessorRequest" />
        </div>
      </div>
    </div>
  </UpdateModal>
</template>
