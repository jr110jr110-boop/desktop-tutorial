<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import type BuyOrder from "@/types/BuyOrder";
import { useRoute, useRouter } from "vue-router";
import {
  addBuyOrder,
  addBuyOrderProcess,
  buyOrderSyncCounts,
  deleteBuyOrderProcess,
  editBuyOrder,
  editBuyOrderProcess,
  getBuyOrderDetail,
  getBuyOrderReadyProcessList,
  getBuyOrderProcessList,
} from "@/api/BuyOrderAPIs";
import { getProcessDetail } from "@/api/ProcessAPIs";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showErrorNotification from "@/composables/showErrorNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import DeleteModal from "@/components/app/DeleteModal.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsAlert from "@/components/common/KsAlert.vue";
// import KsDatePicker from "@/components/KsDatePicker.vue";
import ShoppingCartIcon from "@/components/icons/ShoppingCartIcon.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import { Colors } from "@/assets/colors";
import type DeleteBuyOrderProcess from "@/types/DeleteBuyOrderProcess";

interface BuyOrderReadyProcess {
  id: number | string;
  bomId: number | string;
  title: string;
}

interface BuyOrderProcess {
  buyOrderId: number | string;
  processId: number | string;
  attribute: string;
}

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const route = useRoute();
const router = useRouter();

const buyOrderProcessInfo: Ref<DeleteBuyOrderProcess> = ref({
  buyOrderId: 0,
  processId: 0,
});

const buyOrder: Ref<BuyOrder> = ref({
  id: props.id,
  title: "",
  exitCommodityCode: "",
  enterCommodityCode: "",
  count: 0,
  reserveCount: 0,
  buyCount: null,
  workOrderId: route.query.workOrderId.toString(),
});

const buyOrderReadyProcessList: Ref<BuyOrderReadyProcess[]> = ref([]);
const buyOrderProcesses: Ref<BuyOrderProcess[]> = ref([]);

const deleteModal = ref({
  visible: false,
  activeIndex: null,
  activeId: null,
});

const alerts = ref({
  buyOrder: true,
  readyProcesses: !buyOrder.value.id,
});

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  processListLoading: false,
  selectLoading: true,
  deleteBtnLoading: null,
});

const openDeleteModal = (index, id) => {
  deleteModal.value.activeIndex = index;
  deleteModal.value.activeId = id;
  deleteModal.value.visible = true;
  buyOrderProcessInfo.value.buyOrderId =
    buyOrderProcesses.value[deleteModal.value.activeIndex].buyOrderId;
  buyOrderProcessInfo.value.processId =
    buyOrderProcesses.value[deleteModal.value.activeIndex].processId;
};

const addBuyOrderProcessForm = (processId = null, attribute = "") => {
  buyOrderProcesses.value.push({
    buyOrderId: null,
    processId,
    attribute,
  });
};

const removeBuyOrderProcessForm = async () => {
  if (buyOrderProcessInfo.value.buyOrderId == null) {
    buyOrderProcesses.value.splice(deleteModal.value.activeIndex, 1);
    deleteModal.value.visible = false;
    return false;
  }
  if (deleteModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteModal.value.activeIndex;
    const [data, error] = await deleteBuyOrderProcess(
      buyOrderProcessInfo.value
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      showSuccessNotification("محصول قرارداد با موفقیت حذف شد");
      buyOrderProcesses.value.splice(deleteModal.value.activeIndex, 1);
    }

    loadings.value.deleteBtnLoading = null;
  } else buyOrderProcesses.value.splice(deleteModal.value.activeIndex, 1);
  deleteModal.value.visible = false;
};

async function updateBuyOrderProcesses() {
  let isSuccess = true;
  loadings.value.btnLoading = true;
  for (let i = 0; i < buyOrderProcesses.value.length; i++) {
    const [data, error] = buyOrderProcesses.value[i].buyOrderId
      ? await editBuyOrderProcess(buyOrderProcesses.value[i])
      : await addBuyOrderProcess({
          buyOrderId: buyOrder.value.id,
          processId: buyOrderProcesses.value[i].processId,
          attribute: buyOrderProcesses.value[i].attribute,
        });

    if (!data?.success) {
      showErrorNotification(error.message || error.response.data.message);
      isSuccess = false;
      break;
    }
  }

  if (isSuccess) {
    showSuccessNotification("فرآیندها با موفقیت بروزرسانی شدند");
    await requestBuyerOrderSyncCounts();
  }
  loadings.value.btnLoading = false;
}

const requestBuyerOrderSyncCounts = async () => {
  const keys = ["buyCount", "reserveCount", "realBuyCount"];
  const [data] = await buyOrderSyncCounts(buyOrder.value.id);

  if (data?.success)
    keys.forEach((key) => {
      buyOrder.value[key] = data.data[key];
    });
};

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = props.id
    ? await editBuyOrder(buyOrder.value)
    : await addBuyOrder(buyOrder.value);
  if (!data?.success) showErrorNotification(data.message || error.message);
  else {
    showSuccessNotification(
      "فرآیندهای تامین، مونتاژ و ساخت با موفقیت " +
        (props.id ? "ویرایش شد" : "افزوده شد")
    );
    if (data.success && !props.id) {
      buyOrder.value.id = data.data.id;
      alerts.value.readyProcesses = false;
      await router.replace({
        name: route.name,
        params: {
          id: buyOrder.value.id,
        },
        query: route.query,
      });
    }
    if (route.query.processId) await updateBuyOrderProcesses();
  }

  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (
    buyOrder.value.exitCommodityCode === "" ||
    buyOrder.value.exitCommodityCode === null ||
    buyOrder.value.enterCommodityCode === "" ||
    buyOrder.value.enterCommodityCode === null ||
    buyOrder.value.count === null ||
    buyOrder.value.reserveCount === null ||
    buyOrder.value.buyCount === null
  )
    isValid = false;

  if (!isValid) showWarnNotification("فیلدهای الزامی را وارد کنید");
  return isValid;
};

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;

  const [data, error] = await getBuyOrderDetail(
    route.query.workOrderId.toString(),
    id
  );
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else if (data.success) buyOrder.value = data.data;
  else showErrorNotification(data.message);

  loadings.value.inputLoading = false;
}

async function fetchBuyOrderReadyProcessList() {
  loadings.value.selectLoading = true;
  if (props.id) {
    const [data, error] = await getBuyOrderProcessList(props.id);
    if (!data?.success) {
      showErrorNotification(data?.message || error.message);
      router.back();
    } else if (data.success) {
      buyOrderReadyProcessList.value = data.data;
    } else showErrorNotification(data.message);
  } else {
    const [data, error] = await getBuyOrderReadyProcessList(
      route.query.workOrderId.toString(),
      buyOrder.value.enterCommodityCode
    );
    if (!data?.success) {
      showErrorNotification(data?.message || error.message);
      router.back();
    } else if (data.success) {
      buyOrderReadyProcessList.value = data.data.data;
    } else showErrorNotification(data.message);
  }

  loadings.value.selectLoading = false;
}

async function fetchProcessDetail(processId, bomId) {
  loadings.value.inputLoading = true;
  loadings.value.selectLoading = true;
  loadings.value.processListLoading = true;

  const [data, error] = await getProcessDetail(processId, bomId);
  if (!error && data.success) {
    buyOrder.value.enterCommodityCode = data.data.enterCommodityCode;
    buyOrder.value.exitCommodityCode = data.data.exitCommodityCode;
    buyOrder.value.count = data.data.bomCount;
    buyOrder.value.buyCount = data.data.bomCount;
    await fetchBuyOrderReadyProcessList();
    addBuyOrderProcessForm(data.data.id, "اضافه شد");
  }
  loadings.value.inputLoading = false;
  loadings.value.selectLoading = false;
  loadings.value.processListLoading = false;
}

onMounted(async () => {
  if (props.id) {
    await fetchDetail(props.id);
    await fetchBuyOrderReadyProcessList();
  }
  if (route.query.processId && route.query.bomId)
    await fetchProcessDetail(route.query.processId, route.query.bomId);
});
</script>

<template>
  <div>
    <KsUpdateForm
      :btn-loading="loadings.btnLoading"
      :has-transition-group="false"
      :is-edit="!!props.id"
      form-title="فرآیندهای تامین، مونتاژ و ساخت"
      @formSubmit="sendRequest">
      <template #formIcon>
        <ShoppingCartIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #form>
        <div class="col-12 col-md-10 mb-5">
          <KsAlert
            v-model="alerts.buyOrder"
            :closeable="false"
            color="primary"
            theme="flat">
            <template #title>
              <span class="text-small text-primary"
                >این فرآیندهای تامین، مونتاژ و ساخت برای دستور ساخت
                <strong class="fw-bold">
                  "{{ $route.query.workOrderTitle }}"
                </strong>
                می باشد
              </span>
            </template>
          </KsAlert>
          <KsInput
            v-model="buyOrder.title"
            :loading="loadings.inputLoading"
            class="mt-5"
            placeholder="عنوان فرآیندهای تامین، مونتاژ و ساخت" />
          <div
            class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="buyOrder.enterCommodityCode"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کد ورود کالا" />
            <KsInput
              v-model="buyOrder.exitCommodityCode"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کد خروج کالا" />
          </div>
          <div
            class="d-flex flex-column flex-md-row-reverse align-md-items-center gap-5 gap-md-3 mt-5">
            <KsInput
              v-model="buyOrder.count"
              :loading="loadings.inputLoading"
              class="flex-fill no-pointer-event"
              input-type="number"
              placeholder="تعداد کل"
              readonly />
            <KsInput
              v-model="buyOrder.buyCount"
              :loading="loadings.inputLoading"
              class="flex-fill"
              input-type="number"
              placeholder="تعداد خرید" />
            <KsInput
              v-model="buyOrder.reserveCount"
              :loading="loadings.inputLoading"
              class="flex-fill"
              input-type="number"
              placeholder="تعداد رزرو" />
          </div>
        </div>
      </template>
    </KsUpdateForm>

    <KsUpdateForm
      :has-back-btn="false"
      :has-submit-btn="false"
      :has-transition-group="false"
      :is-edit="!!props.id"
      class="mt-3"
      form-title="فرآیندهای تامین، مونتاژ و ساخت">
      <template #form>
        <div class="col-12 col-md-10 mb-5">
          <KsAlert
            v-if="!route.query.processId"
            v-model="alerts.readyProcesses"
            :closeable="false"
            class="mt-3"
            color="primary"
            theme="flat">
            <template #title>
              <span class="text-small text-primary">
                ابتدا فرآیندهای تامین، مونتاژ و ساخت مورد نظر را اضافه کنید...
              </span>
            </template>
          </KsAlert>
          <div v-if="loadings.processListLoading" class="position-relative p-4">
            <div class="custom-loader center" />
          </div>
          <div
            v-if="
              (buyOrder.id || route.query.processId) &&
              !loadings.processListLoading
            ">
            <div
              v-for="(buyOrderProcess, index) in buyOrderProcesses"
              :key="index"
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-3 mt-3">
              <div
                class="d-flex flex-row-reverse align-items-center gap-3 flex-1"
                v-if="!props.id">
                <KsButton
                  :disabled="index == 1 ? false : true"
                  :loading="loadings.deleteBtnLoading === index"
                  color="warn"
                  fixed-width-height="42"
                  theme="flat"
                  tooltip="حذف"
                  @click="openDeleteModal(index, buyOrderProcess.processId)">
                  <template #content>
                    <TimesIcon :fill="Colors.warn" width="18" />
                  </template>
                </KsButton>
                <KsSelect
                  v-model="buyOrderProcess.processId"
                  :loading="loadings.selectLoading"
                  :options="buyOrderReadyProcessList"
                  class="flex-1"
                  label-prop-name="title"
                  placeholder="فرآیند مورد نظر را انتخاب کنید"
                  simple-placeholder
                  value-prop-name="id" />
              </div>
              <KsInput
                v-if="!props.id"
                v-model="buyOrderProcess.attribute"
                class="flex-1"
                placeholder="ویژگی"
                simple-placeholder />
            </div>
            <div
              class="d-flex gap-2 align-items-center mt-5"
              v-if="props.id"
              v-for="(item, index) in buyOrderReadyProcessList"
              :key="index">
              <KsInput
                v-model="item.title"
                class="flex-1"
                :class="buyOrderReadyProcessList[index + 1] ? 'mt-2' : ''"
                placeholder="عنوان"
                simple-placeholder
                :is-disabled="true" />
            </div>
            <div class="d-flex gap-2 align-items-center mt-5" v-if="!props.id">
              <KsButton
                key="add-btn"
                color="primary"
                fixed-width-height="42"
                tooltip="فرآیند جدید"
                @click="addBuyOrderProcessForm">
                <template #content>
                  <PlusIcon :fill="Colors.white" width="18" />
                </template>
              </KsButton>

              <KsButton
                :disabled="!buyOrderProcesses.length || !buyOrder.id"
                :loading="loadings.btnLoading"
                :tooltip="
                  !buyOrder.id
                    ? 'نیاز به افزودن فرآیندهای تامین، مونتاژ و ساخت'
                    : ''
                "
                color="primary"
                title="بروزرسانی فرآیندها"
                @click="updateBuyOrderProcesses" />
            </div>
          </div>
        </div>
      </template>
    </KsUpdateForm>

    <!--    DELETE MODAL   -->
    <DeleteModal
      v-model="deleteModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteModal.visible = false"
      @submit="removeBuyOrderProcessForm" />
  </div>
</template>
