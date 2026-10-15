<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import type Bom from "@/types/Bom";
import { useRoute, useRouter } from "vue-router";
import {
  addBom,
  deleteBomPredecessor,
  editBom,
  getBomDetail,
  getBomList,
  setBomPredecessor,
} from "@/api/BomAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsTextArea from "@/components/common/KsTextArea.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import KsInput from "@/components/common/KsInput.vue";
import BallPileIcon from "@/components/icons/BallPileIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();
const route = useRoute();

const bom: Ref<Bom> = ref({
  id: null,
  title: "",
  assemblyId: route.query.assemblyId.toString(),
  positionNumber: null,
  count: null,
  unit: "",
  material: "",
  standard: "",
  diagonal: null,
  diagonalUnit: null,
  length: null,
  lengthUnit: null,
  width: null,
  widthUnit: null,
  height: null,
  heightUnit: null,
  notWetWeight: null,
  notWetWeightUnit: null,
  wetWeight: null,
  wetWeightUnit: null,
  description: "",
  sort: 0,
});

const bomPredecessor = ref({
  bomId: null,
  predecessorId: null,
});

const bomList: Ref<Bom[]> = ref([]);

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  bomListLoading: true,
});

const lengthAppendArray = [
  { id: 1, text: "میلیمتر" },
  { id: 2, text: "سانتیمتر" },
  { id: 3, text: "متر" },
];

const weightAppendArray = [
  { id: 1, text: "گرم" },
  { id: 2, text: "کیلوگرم" },
  { id: 3, text: "تن" },
];

async function removePredecessorRequest() {
  if (bomPredecessor.value.bomId) {
    loadings.value.bomListLoading = true;
    const [data, error] = await deleteBomPredecessor(bom.value.id);
    const isSuccess = !error && data?.success;
    if (!isSuccess) showErrorNotification(data?.message || error.message);
    loadings.value.bomListLoading = false;
  }
}

async function setPredecessorRequest() {
  const [data, error] = await setBomPredecessor(
    bom.value.id,
    bomPredecessor.value.predecessorId
  );
  const isSuccess = !error && data?.success;
  if (!isSuccess) showErrorNotification(data?.message || error.message);
}

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = props.id
    ? await editBom(bom.value)
    : await addBom(bom.value);
  const isSuccess = !error && data?.success;

  if (isSuccess) {
    bom.value.id = data.data.id;
    if (!bomPredecessor.value.bomId && bomPredecessor.value.predecessorId)
      await setPredecessorRequest();
    showSuccessNotification(
      "بوم با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
    );
    router.back();
  } else showErrorNotification(data.message || error.message);

  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (
    bom.value.title === "" ||
    bom.value.title === null ||
    bom.value.positionNumber === null ||
    bom.value.count === null ||
    bom.value.unit === "" ||
    bom.value.unit === null ||
    bom.value.material === "" ||
    bom.value.material === null ||
    bom.value.standard === "" ||
    bom.value.standard === null ||
    bom.value.diagonal === null ||
    bom.value.diagonalUnit === null ||
    bom.value.length === null ||
    bom.value.lengthUnit === null ||
    bom.value.width === null ||
    bom.value.height === null ||
    bom.value.notWetWeight === null ||
    bom.value.wetWeight === null ||
    bom.value.description === "" ||
    bom.value.description === null
  )
    isValid = false;

  if (isValid) return true;
  else {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
};

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;
  const [data, error] = await getBomDetail(
    id,
    route.query.assemblyId.toString()
  );
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else {
    bom.value = data.data;
    if (data.data.predecessorId) {
      bomPredecessor.value.predecessorId = data.data.predecessorId;
      bomPredecessor.value.bomId = data.data.id;
    }
  }
  loadings.value.inputLoading = false;
}

async function fetchBomList() {
  loadings.value.bomListLoading = true;
  const [data, error] = await getBomList(route.query.assemblyId.toString());
  if (!error && data.success) bomList.value = data.data.data;
  loadings.value.bomListLoading = false;
}

onMounted(async () => {
  if (props.id) await fetchDetail(props.id);
  await fetchBomList();
});
</script>

<template>
  <KsUpdateForm
    :btn-loading="loadings.btnLoading"
    :is-edit="!!props.id"
    form-title="Bom"
    @formSubmit="sendRequest">
    <template #formIcon>
      <BallPileIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
    <template #form>
      <div key="1" class="col-12 col-md-10 mb-5">
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3">
          <KsInput
            v-model="bom.title"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="عنوان" />
          <KsInput
            v-model="bom.positionNumber"
            :loading="loadings.inputLoading"
            class="flex-fill"
            input-type="number"
            placeholder="شماره موقعیت" />
        </div>
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3 mt-5">
          <KsInput
            v-model="bom.count"
            :loading="loadings.inputLoading"
            class="flex-fill"
            input-type="number"
            placeholder="تعداد" />
          <KsInput
            v-model="bom.unit"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="واحد" />
        </div>
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3 mt-5">
          <KsInput
            v-model="bom.material"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="متریال" />
          <KsInput
            v-model="bom.standard"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="استاندارد" />
        </div>
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3 mt-5">
          <KsInput
            v-model="bom.diagonal"
            append-text="میلیمتر"
            class="flex-fill"
            placeholder="قطر"
            :appendTextIsDropdown="true"
            :appendTextOptions="lengthAppendArray"
            :dropdownValue="bom.diagonalUnit"
            @update:appendTextSelectedId="bom.diagonalUnit = $event" />
          <KsInput
            v-model="bom.length"
            append-text="میلیمتر"
            class="flex-fill"
            placeholder="طول"
            :appendTextIsDropdown="true"
            :appendTextOptions="lengthAppendArray"
            :dropdownValue="bom.lengthUnit"
            @update:appendTextSelectedId="bom.lengthUnit = $event" />

          <KsInput
            v-model="bom.width"
            append-text="میلیمتر"
            class="flex-fill"
            placeholder="عرض"
            :appendTextIsDropdown="true"
            :appendTextOptions="lengthAppendArray"
            :dropdownValue="bom.widthUnit"
            @update:appendTextSelectedId="bom.widthUnit = $event" />
          <KsInput
            v-model="bom.height"
            append-text="میلیمتر"
            class="flex-fill"
            placeholder="ارتفاع"
            :appendTextIsDropdown="true"
            :appendTextOptions="lengthAppendArray"
            :dropdownValue="bom.heightUnit"
            @update:appendTextSelectedId="bom.heightUnit = $event" />
        </div>
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3 mt-5">
          <KsInput
            v-model="bom.notWetWeight"
            append-text="گرم"
            class="flex-fill"
            input-type="number"
            placeholder="وزن ناخالص"
            :appendTextIsDropdown="true"
            :appendTextOptions="weightAppendArray"
            :dropdownValue="bom.notWetWeightUnit"
            @update:appendTextSelectedId="bom.notWetWeightUnit = $event" />
          <KsInput
            v-model="bom.wetWeight"
            append-text="گرم"
            class="flex-fill"
            input-type="number"
            placeholder="وزن خالص"
            :appendTextIsDropdown="true"
            :appendTextOptions="weightAppendArray"
            :dropdownValue="bom.wetWeightUnit"
            @update:appendTextSelectedId="bom.wetWeightUnit = $event" />
        </div>
        <KsTextArea
          v-model="bom.description"
          class="mt-5"
          placeholder="توضیحات" />
        <div
          class="w-100 d-flex flex-row-reverse align-items-center gap-4 mt-5">
          <span class="text-small fw-bold" dir="rtl">
            انتخاب BOM پیش نیاز (اختیاری):
          </span>
          <KsSelect
            v-model="bomPredecessor.predecessorId"
            :loading="loadings.bomListLoading"
            :options="bomList"
            class="flex-1"
            label-prop-name="title"
            mode="filter"
            placeholder="Bom پیش نیاز مورد نظر را انتخاب کنید"
            remove-btn
            simple-placeholder
            value-prop-name="id"
            @removeBtnClick="removePredecessorRequest" />
        </div>
      </div>
    </template>
  </KsUpdateForm>
</template>

<style scoped></style>
