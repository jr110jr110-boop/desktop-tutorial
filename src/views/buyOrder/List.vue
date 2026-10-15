<script lang="ts" setup>
import type PreviewTextRow from "@/types/PreviewTextRow";
import type AttachFile from "@/types/AttachFile";
import type WorkOrder from "@/types/WorkOrder";
import type BuyOrder from "@/types/BuyOrder";
import type Contract from "@/types/Contract";
import type _Process from "@/types/Process";
import type { Ref } from "vue";
import { onMounted, reactive, ref } from "vue";
import { useRoute } from "vue-router";
import { getBuyOrderList, revertBuyOrderStatus } from "@/api/BuyOrderAPIs";
import { getWorkOrderList } from "@/api/WorkOrderAPIs";
import { getContractList } from "@/api/ContractAPIs";
import {
  PREVIEW_MODAL_BUY_ORDER_MODEL,
  PREVIEW_MODAL_PROCESS_MODEL,
  REVERT_STATUSES,
} from "@/models";
import { downloadFile } from "@/composables/downloadFile";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import moment from "moment-jalaali";

import NoDataFound from "@/layouts/NoDataFound.vue";
import PreviewModalTextRow from "@/components/app/_common/previewModal/PreviewModalTextRow.vue";
import KsAccordion from "@/components/common/KsAccordion.vue";
import KsButton from "@/components/common/KsButton.vue";
import EditIcon from "@/components/icons/EditIcon.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import ListLoader from "@/layouts/ListLoader.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import EyeIcon from "@/components/icons/EyeIcon.vue";
import KsModal from "@/components/common/KsModal.vue";
import ShoppingCartIcon from "@/components/icons/ShoppingCartIcon.vue";
import UndoIcon from "@/components/icons/UndoIcon.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import MenuIcon from "@/components/icons/MenuIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  workOrderId: {
    type: [String, Number],
    default: null,
  },
});

moment.loadPersian({ dialect: "persian-modern" });
const route = useRoute();

const revertStatusList = REVERT_STATUSES;

const state = reactive({
  workOrderId: props.workOrderId,
  contractId: null as string | number,
  isLoading: true,
  revertBuyOrderStatusBtnLoading: null,
  downloadBtnLoading: null,
  selectedRevertStatus: null,
});

const buyOrderList: Ref<BuyOrder[]> = ref([]);
const contractList: Ref<Contract[]> = ref([]);
const workOrderList: Ref<WorkOrder[]> = ref([]);

const previewModal = reactive({
  visible: false,
  buyOrderTitle: "",
  processTitle: "",
  buyOrder: [] as PreviewTextRow[],
  process: [] as PreviewTextRow[],
  processFiles: [] as AttachFile[],
  mode: null as "buyOrder" | "process",
  activeIndex: null,
});

const revertBuyOrderStatusModal = reactive({
  visible: false,
  activeId: null,
  activeIndex: null,
});

const getActiveWorkOrderTitle = () => {
  for (let i = 0; i < workOrderList.value.length; i++) {
    if (workOrderList.value[i].id == state.workOrderId)
      return workOrderList.value[i].title;
  }
  return "";
};

const getAvailableRevertStatusList = () => {
  let availableRevertStatusList = [];
  for (let i = 0; i < revertStatusList.length; i++)
    if (
      revertStatusList[i].status <
      buyOrderList.value[revertBuyOrderStatusModal.activeIndex].status
    )
      availableRevertStatusList.push(revertStatusList[i]);

  return availableRevertStatusList;
};

const showRevertProcessStatusModal = (id: string | number, index: number) => {
  state.selectedRevertStatus = null;
  revertBuyOrderStatusModal.activeId = id;
  revertBuyOrderStatusModal.activeIndex = index;
  revertBuyOrderStatusModal.visible = true;
};

const onPreviewModalRequest = (
  mode: "buyOrder" | "process",
  index?: number,
  buyOrder?: BuyOrder,
  process?: _Process
) => {
  previewModal.buyOrderTitle = buyOrder?.title ?? null;
  previewModal.processTitle = process?.title ?? null;
  previewModal.activeIndex = index;
  previewModal.mode = mode;
  previewModal[mode] = (
    mode === "buyOrder"
      ? [...PREVIEW_MODAL_BUY_ORDER_MODEL]
      : [...PREVIEW_MODAL_PROCESS_MODEL]
  ).map((item) => {
    let value = mode === "buyOrder" ? buyOrder[item.key] : process[item.key];
    if (item.format === "price") value = value.toLocaleString("ar-SA");
    if (item.format === "percent") value = parseFloat(value).toFixed(2);

    return {
      title: item.title,
      value,
      prefix: item.prefix ?? null,
    };
  });
  if (mode === "process") previewModal.processFiles = process.files;
  previewModal.visible = true;
};

async function downloadProcessAttachFile(file, index: number) {
  state.downloadBtnLoading = index;
  await downloadFile(file, file.id);
  state.downloadBtnLoading = null;
}

async function requestRevertBuyOrderStatus() {
  state.revertBuyOrderStatusBtnLoading = revertBuyOrderStatusModal.activeIndex;

  const [data, error] = await revertBuyOrderStatus(
    revertBuyOrderStatusModal.activeId,
    state.selectedRevertStatus
  );

  if (!data?.success)
    showErrorNotification(
      error.message + " | \nلطفا در صورت نیاز با پشتیبانی تماس بگیرید"
    );
  else showSuccessNotification("فرآیند با موفقیت بازگردانده شد");

  revertBuyOrderStatusModal.visible = false;
  state.revertBuyOrderStatusBtnLoading = null;

  if (!error) await fetchBuyOrderList();
}

// MOUNTED API CALLS
async function fetchBuyOrderList() {
  state.isLoading = true;
  const [data, error] = await getBuyOrderList(state.workOrderId);
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else buyOrderList.value = data.data;
  state.isLoading = false;
}

async function fetchWorkOrderList(checkInitWorkId: boolean) {
  state.isLoading = true;
  const [data, error] = await getWorkOrderList(
    true,
    state.contractId,
    "",
    0,
    1
  );
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    workOrderList.value = data.data.data;
    if (!props.workOrderId) {
      state.workOrderId = null;
      if (workOrderList.value.length)
        state.workOrderId = workOrderList.value[0].id;
    } else if (workOrderList.value.length && checkInitWorkId)
      for (let i = 0; i < workOrderList.value.length; i++)
        if (workOrderList.value[i].id === props.workOrderId) {
          state.contractId = workOrderList.value[i].contractId;
          break;
        }
  }
  buyOrderList.value = [];
  if (state.workOrderId) await fetchBuyOrderList();
  state.isLoading = false;
}

async function fetchContractList() {
  state.isLoading = true;
  const [data, error] = await getContractList();
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    contractList.value = data.data.data;
    if (!props.workOrderId) {
      state.contractId = null;
      if (contractList.value.length)
        state.contractId = contractList.value[0].id;
    } else
      state.contractId = route.query.contractId
        ? route.query.contractId.toString()
        : null;
  }
  state.isLoading = false;
}

onMounted(async () => {
  await fetchContractList();
  if (state.contractId) await fetchWorkOrderList(true);
});
</script>

<template>
  <div class="background-color p-3 shadow-sm rounded-3 text-end">
    <!--   Header   -->
    <KsButton
      :disabled="!state.workOrderId"
      class="floating-btn d-sm-none"
      fixed-width-height="44"
      radius="circle"
      @click="
        $router.push({
          name: 'BuyOrderUpdate',
          params: { id: null },
          query: {
            workOrderId: state.workOrderId,
            workOrderTitle: getActiveWorkOrderTitle(),
          },
        })
      ">
      <template #content>
        <PlusIcon fill="#ffffff" width="18" />
      </template>
    </KsButton>
    <div
      class="d-flex flex-row-reverse justify-content-between align-items-center">
      <div class="list-title">
        <span class="fw-bold text-large text-color"
          >فرآیندهای تامین، مونتاژ و ساخت</span
        >
        <ShoppingCartIcon :fill="Colors.black" class="ms-3" width="24" />
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
          :disabled="!state.workOrderId"
          :tooltip="!state.workOrderId ? 'ابتدا یک دستور ساخت انتخاب کنید' : ''"
          class="d-none d-sm-block"
          color="primary"
          title="افزودن فرآیندهای تامین، مونتاژ و ساخت"
          @click="
            $router.push({
              name: 'BuyOrderUpdate',
              params: { id: null },
              query: {
                workOrderId: state.workOrderId,
                workOrderTitle: getActiveWorkOrderTitle(),
              },
            })
          " />
      </div>
    </div>
    <div
      class="d-flex flex-column flex-lg-row gap-3 gap-lg-5 justify-content-between my-4 pt-3 align-items-lg-center"
      dir="rtl">
      <div
        class="rtl flex-1 d-flex flex-column flex-md-row align-items-md-center gap-3"
        dir="rtl">
        <KsSelect
          v-model="state.contractId"
          :loading="state.isLoading"
          :options="contractList"
          class="flex-1"
          label-prop-name="employer"
          placeholder="قراراداد"
          value-prop-name="id"
          @change="fetchWorkOrderList(false)" />
        <KsSelect
          v-model="state.workOrderId"
          :disabled="!state.contractId"
          :loading="state.isLoading"
          :options="workOrderList"
          class="flex-1"
          label-prop-name="title"
          placeholder="دستور ساخت"
          value-prop-name="id"
          @change="fetchBuyOrderList()" />
      </div>
    </div>
    <hr class="opacity-30" />

    <!--   List   -->
    <transition-group mode="out-in" name="fade">
      <div
        v-if="!state.isLoading && buyOrderList.length"
        key="list"
        class="d-flex flex-column gap-2">
        <KsAccordion
          v-for="(buyOrder, indexBuyOrder) in buyOrderList"
          :key="indexBuyOrder"
          btn-classes="px-2 py-1"
          class="accordion-btn">
          <template #header>
            <div
              class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
              dir="rtl">
              <div
                class="d-flex gap-3 align-items-center justify-content-between flex-1">
                <div class="d-flex gap-2">
                  <span class="text-small">
                    {{ buyOrder.title ?? "فرآیندهای تامین، مونتاژ و ساخت" }}
                  </span>
                  -
                  <span class="text-small text-secondary">
                    {{ buyOrder.statusDescription }}
                  </span>
                </div>
                <div
                  class="d-flex gap-1 align-items-center mt-3 mt-sm-0"
                  @click.stop>
                  <KsButton
                    :loading="
                      state.revertBuyOrderStatusBtnLoading === indexBuyOrder
                    "
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
                          @click="
                            onPreviewModalRequest(
                              'buyOrder',
                              indexBuyOrder,
                              buyOrder
                            )
                          ">
                          <EyeIcon :fill="Colors.black" width="16" />
                          <span class="text-small">مشاهده اطلاعات</span>
                        </li>
                        <li
                          class="d-flex flex-row-reverse align-items-center gap-3"
                          @click="
                            $router.push({
                              name: 'BuyOrderUpdate',
                              params: { id: buyOrder.id },
                              query: {
                                workOrderId: state.workOrderId,
                                workOrderTitle: getActiveWorkOrderTitle(),
                              },
                            })
                          ">
                          <EditIcon :fill="Colors.black" width="16" />
                          <span class="text-small">ویرایش</span>
                        </li>
                        <li
                          :class="{
                            'no-pointer-event opacity-25':
                              buyOrder.status === 0,
                          }"
                          class="d-flex flex-row-reverse align-items-center gap-3"
                          @click="
                            showRevertProcessStatusModal(
                              buyOrder.id,
                              indexBuyOrder
                            )
                          ">
                          <UndoIcon :fill="Colors.black" width="16" />
                          <span class="text-small">بازگرداندن</span>
                        </li>
                      </ul>
                    </template>
                  </KsButton>
                </div>
              </div>
            </div>
          </template>
          <template #content>
            <div class="d-flex flex-column gap-3 px-1 py-2">
              <hr class="my-1" />
              <span
                v-if="!buyOrder.processes || !buyOrder.processes.length"
                class="text-xSmall text-center fw-bold"
                dir="rtl">
                لیست فرآیندهای تامین خالی است
              </span>
              <span
                v-if="buyOrder.processes && buyOrder.processes.length"
                class="text-small fw-bold"
                dir="rtl">
                لیست فرآیندهای تامین:
              </span>
              <div
                v-if="buyOrder.processes && buyOrder.processes.length"
                class="d-flex flex-column gap-2">
                <div
                  v-for="(process, indexProcess) in buyOrder.processes"
                  :key="indexProcess"
                  class="accordion-btn px-2 py-1">
                  <div
                    class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-sm-center"
                    dir="rtl">
                    <div class="d-flex gap-3 align-items-center">
                      <span class="text-medium fw-bold accordion-btn-title">
                        {{ indexProcess + 1 }}.
                      </span>
                      <span
                        class="text-medium fw-bold accordion-btn-title pe-0"
                        >{{ process.title }}</span
                      >
                    </div>
                    <div class="d-flex align-items-center gap-1">
                      <KsButton
                        color="secondary"
                        fixed-width-height="32"
                        tooltip="مشاهده اطلاعات"
                        @click="
                          onPreviewModalRequest(
                            'process',
                            indexProcess,
                            null,
                            process
                          )
                        ">
                        <template #content>
                          <EyeIcon :fill="Colors.white" width="14" />
                        </template>
                      </KsButton>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </template>
        </KsAccordion>
      </div>

      <div key="KsSkeletonLoader">
        <ListLoader :active="state.isLoading" />
      </div>

      <!--    NO DATA FOUND   -->
      <div key="NoDataComponent">
        <NoDataFound v-if="!state.isLoading && !buyOrderList.length" />
      </div>
    </transition-group>

    <!--   PREVIEW MODAL   -->
    <KsModal v-model="previewModal.visible" blur>
      <div
        class="p-1 position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
        <div class="ps-3" style="overflow-y: auto; height: 65vh">
          <div
            v-if="previewModal.mode === 'buyOrder'"
            class="d-flex flex-column gap-2"
            dir="rtl">
            <span class="text-medium fw-bold">
              فرآیندهای تامین، مونتاژ و ساخت
              {{ previewModal.buyOrderTitle }}
            </span>
            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-4" dir="ltr">
              <div class="d-flex flex-column align-items-end gap-4">
                <PreviewModalTextRow
                  v-for="(buyOrder, index) in previewModal.buyOrder"
                  :key="index"
                  :prefix="buyOrder.prefix"
                  :text="buyOrder.value ?? '-'"
                  :title="buyOrder.title" />
              </div>
            </div>
          </div>
          <div
            v-if="previewModal.mode === 'process'"
            class="d-flex flex-column gap-2"
            dir="rtl">
            <span class="text-medium fw-bold">
              {{ previewModal.processTitle }}
            </span>

            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-4" dir="ltr">
              <div
                v-for="(process, index) in previewModal.process"
                :key="index">
                <PreviewModalTextRow
                  v-if="index !== 0"
                  :prefix="process.prefix"
                  :text="process.value ?? '-'"
                  :title="process.title" />
              </div>
            </div>

            <!--  ًٍEquipment Process Attach Files -->
            <transition-group
              key="equipmentProcess.files"
              class="d-flex flex-column gap-2"
              dir="ltr"
              mode="out-in"
              name="fade"
              tag="div">
              <hr key="hr" />
              <span key="title" class="text-medium fw-bold text-end">
                :اسناد کنترل کیفی
              </span>

              <div
                v-for="(file, indexProcessFile) in previewModal.processFiles"
                :key="indexProcessFile">
                <div class="d-flex flex-column gap-3 mt-3">
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title"
                      >:کد سند کنترل کیفی</span
                    >
                    <span class="text-small">{{ file.code }}</span>
                  </div>
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title"
                      >:عنوان سند کنترل کیفی</span
                    >
                    <span class="text-small">{{ file.name }}</span>
                  </div>
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title"
                      >:توضیحات سند کنترل کیفی</span
                    >
                    <span class="text-small">{{ file.description }}</span>
                  </div>
                  <KsButton
                    key="download-btn"
                    :disabled="!file.id"
                    :loading="state.downloadBtnLoading === indexProcessFile"
                    class="w-100"
                    color="primary"
                    fixed-width-height="42"
                    theme="flat"
                    tooltip="دانلود"
                    @click="downloadProcessAttachFile(file, indexProcessFile)">
                    <template #content>
                      <DownloadIcon :fill="Colors.primary" width="24" />
                    </template>
                  </KsButton>
                </div>
              </div>
            </transition-group>
          </div>
        </div>

        <div class="d-flex justify-content-end gap-2 mt-4">
          <KsButton
            color="dark"
            theme="flat"
            title="بستن"
            @click="previewModal.visible = false" />
        </div>
      </div>
    </KsModal>

    <!--    REVERT PROCESS STATUS MODAL   -->
    <KsModal v-model="revertBuyOrderStatusModal.visible" :order="3" blur>
      <div class="text-center pt-2">
        <span class="color-danger fw-bold text-large">
          بازگردانی فرآیندهای تامین، مونتاژ و ساخت
        </span>
      </div>

      <div class="text-center my-3" dir="rtl">
        <span class="text-small">
          برای بازگرداندن فرآیندهای تامین، مونتاژ و ساخت یکی از مراحل زیر را
          انتخاب کنید
        </span>

        <KsSelect
          v-model="state.selectedRevertStatus"
          :options="getAvailableRevertStatusList()"
          class="my-4"
          label-prop-name="title"
          placeholder="انتخاب مرحله"
          simple-placeholder
          value-prop-name="status" />
      </div>

      <div class="d-flex justify-content-end gap-2 mt-3">
        <KsButton
          :disabled="state.selectedRevertStatus === null"
          :loading="state.revertBuyOrderStatusBtnLoading !== null"
          color="danger"
          theme="flat"
          title="تایید"
          @click="requestRevertBuyOrderStatus" />
        <KsButton
          color="transparent"
          title="لغو"
          @click="revertBuyOrderStatusModal.visible = false" />
      </div>
    </KsModal>
  </div>
</template>
