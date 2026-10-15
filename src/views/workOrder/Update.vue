<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import type WorkOrder from "@/types/WorkOrder";
import type ContractEquipment from "@/types/ContractEquipment";
import { useRoute, useRouter } from "vue-router";
import { getContractEquipmentRemainingList } from "@/api/ContractAPIs";
import {
  addWorkOrder,
  editWorkOrder,
  getWorkOrderDetail,
} from "@/api/WorkOrderAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import KsInput from "@/components/common/KsInput.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import KsTextArea from "@/components/common/KsTextArea.vue";
import HammerIcon from "@/components/icons/HammerIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const route = useRoute();
const router = useRouter();

const workOrder: Ref<WorkOrder> = ref({
  id: props.id,
  contractId: route.query.contractId.toString(),
  equipmentId: null,
  code: "",
  title: "",
  condition: "0",
  description: "",
  count: null,
  weightPercent: 0,
});

const contractEquipmentList: Ref<ContractEquipment[]> = ref([]);

const workOrderConditions = [
  {
    title: "فعال",
    value: "0",
  },
  {
    title: "متوقف شده",
    value: "1",
  },
  {
    title: "لغو شده",
    value: "3",
  },
];

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  selectLoading: true,
});

async function changeCondition(option: { label: string; value: string }) {
  workOrder.value.conditionDescription = option.label;
  workOrder.value.condition = option.value;
  
}

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = props.id
    ? await editWorkOrder(workOrder.value)
    : await addWorkOrder(workOrder.value);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    showSuccessNotification(
      "دستور ساخت با موفقیت" + (props.id ? "ویرایش شد" : "افزوده شد")
    );
    router.back();
  }

  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (
    workOrder.value.equipmentId === "" ||
    workOrder.value.equipmentId === null ||
    workOrder.value.code === "" ||
    workOrder.value.code === null ||
    workOrder.value.title === "" ||
    workOrder.value.title === null ||
    workOrder.value.description === "" ||
    workOrder.value.description === null ||
    workOrder.value.count === null
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

  const [data, error] = await getWorkOrderDetail(
    route.query.contractId.toString(),
    id
  );
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else {
    workOrder.value = data.data;
    workOrder.value.condition = data.data.condition.toString();
  }

  loadings.value.inputLoading = false;
}

async function fetchContractEquipmentList() {
  loadings.value.selectLoading = true;

  const [data, error] = await getContractEquipmentRemainingList(
    route.query.contractId.toString()
  );
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else contractEquipmentList.value = data.data;

  loadings.value.selectLoading = false;
}

onMounted(async () => {
  if (props.id) await fetchDetail(props.id);
  await fetchContractEquipmentList();
});
</script>

<template>
  <KsUpdateForm
    :btn-loading="loadings.btnLoading"
    :has-transition-group="false"
    :is-edit="!!props.id"
    form-title="دستور ساخت"
    @formSubmit="sendRequest">
    <template #formIcon>
      <HammerIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
    <template #form>
      <div class="col-12 col-md-10 mb-5">
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3">
          <KsInput
            v-model="workOrder.title"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="عنوان" />
          <KsInput
            v-model="workOrder.code"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="کد" />
          <KsSelect
            v-if="props.id"
            v-model="workOrder.condition"
            :loading="loadings.inputLoading"
            :options="workOrderConditions"
            @onOptionClick="changeCondition"
            custom-handle-option-click
            class="flex-fill"
            placeholder="وضعیت دستور ساخت" />
        </div>
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3 mt-5">
          <KsSelect
            v-model="workOrder.equipmentId"
            :loading="loadings.selectLoading"
            :options="contractEquipmentList"
            class="flex-fill"
            label-prop-name="equipmentTitle"
            placeholder="محصول مورد نظر را انتخاب کنید"
            value-prop-name="equipmentId" />
          <KsInput
            v-model="workOrder.count"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="تعداد" />
          <KsInput
            v-model="workOrder.weightPercent"
            :loading="loadings.inputLoading"
            class="flex-fill"
            append-text="%"
            input-type="number"
            placeholder="درصد وزنی" />
        </div>
        <KsTextArea
          v-model="workOrder.description"
          :loading="loadings.inputLoading"
          class="mt-5"
          placeholder="توضیحات" />
      </div>
    </template>
  </KsUpdateForm>
</template>
