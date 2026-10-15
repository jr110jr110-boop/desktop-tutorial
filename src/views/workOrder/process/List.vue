<script lang="ts" setup>
import type TableValue from "@/types/TableValue";
import type ListCustomGetParam from "@/types/ListCustomGetParam";
import type _Process from "@/types/Process";
import { ref } from "vue";
import { revertProcessStatus } from "@/api/ProcessAPIs";
import { downloadFile } from "@/composables/downloadFile";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import KsList from "@/components/common/KsList.vue";
import KsModal from "@/components/common/KsModal.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import KsButton from "@/components/common/KsButton.vue";
import UndoIcon from "@/components/icons/UndoIcon.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import BallPileIcon from "@/components/icons/BallPileIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  bomId: {
    type: String,
    required: true,
  },
});

const tableHeads: string[] = ["عنوان", "درصد وزنی", "نوع", "وضعیت"];

const tableValues: TableValue[] = [
  {
    propName: "title",
    type: "text",
    clickable: true,
  },
  {
    propName: "weightPercent",
    type: "text",
  },
  {
    propName: "typeDescription",
    type: "text",
  },
  {
    propName: "processStatusDescription",
    type: "price",
  },
];

const getParams: ListCustomGetParam[] = [
  {
    propName: "BomId",
    value: props.bomId,
  },
];

const revertStatusList: { title: string; status: number }[] = [
  {
    title: "فرآیند جدید",
    status: 0,
  },
  {
    title: "تایید واحد PMO",
    status: 1,
  },
  {
    title: "تخصیص کنترل کیفین",
    status: 2,
  },
  {
    title: "افزوده شدن اسناد",
    status: 3,
  },
  {
    title: "تایید اسناد توسط واحد کنترل کیفی",
    status: 4,
  },
  {
    title: "تایید کارفرما",
    status: 5,
  },
  {
    title: "آماده بودن فرآیند",
    status: 6,
  },
  {
    title: "افزوده شدن فرآیندهای تامین، مونتاژ و ساخت",
    status: 7,
  },
];

const selectedRevertStatus = ref(null);

const reFetchListData = ref(false);

const previewModal = ref({
  visible: false,
  data: null as _Process,
});

const revertProcessStatusModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
  currentStatus: null,
});

const loadings = ref({
  revertProcessStatusBtn: null,
  downloadBtn: null,
});

const getAvailableRevertStatusList = () => {
  let availableRevertStatusList = [];
  for (let i = 0; i < revertStatusList.length; i++)
    if (
      revertStatusList[i].status < revertProcessStatusModal.value.currentStatus
    )
      availableRevertStatusList.push(revertStatusList[i]);

  return availableRevertStatusList;
};

const showPreviewModal = (data: _Process) => {
  previewModal.value.data = data;
  previewModal.value.visible = true;
};

const showRevertProcessStatusModal = (
  id: string | number,
  index: number,
  currentStatus
) => {
  selectedRevertStatus.value = null;
  revertProcessStatusModal.value.activeId = id;
  revertProcessStatusModal.value.activeIndex = index;
  revertProcessStatusModal.value.currentStatus = currentStatus;
  revertProcessStatusModal.value.visible = true;
};

async function downloadProcessAttachFile(file, index: number) {
  loadings.value.downloadBtn = index;
  await downloadFile(file, file.id);
  loadings.value.downloadBtn = null;
}

async function requestRevertProcessStatus() {
  revertProcessStatusModal.value.visible = false;
  loadings.value.revertProcessStatusBtn =
    revertProcessStatusModal.value.activeIndex;

  const [data, error] = await revertProcessStatus(
    revertProcessStatusModal.value.activeId,
    selectedRevertStatus.value
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    reFetchListData.value = true;
    setTimeout(() => {
      reFetchListData.value = false;
    }, 500);
    showSuccessNotification("فرآیند با موفقیت بازگردانده شد");
  }

  loadings.value.revertProcessStatusBtn = null;
}
</script>

<template>
  <div>
    <KsList
      :custom-get-params="getParams"
      :re-fetch-data="reFetchListData"
      :table-heads="tableHeads"
      :table-values="tableValues"
      :update-route-queries="{ bomId }"
      api-delete-url="api/v1/Processes/delete-process-collection"
      api-fetch-url="api/v1/Processes"
      has-back-btn
      list-title="فرآیندهای تامین"
      search-terms="عنوان"
      update-route-name="WorkOrderProcessUpdate"
      @onValueClick="(val) => showPreviewModal(val.data)">
      <template #listIcon>
        <BallPileIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #custom-action-btn="{ rowData, rowIndex }">
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            showRevertProcessStatusModal(
              rowData.id,
              rowIndex,
              rowData.processStatus
            )
          ">
          <UndoIcon :fill="Colors.black" width="16" />
          <span class="text-small">بازگرداندن فرآیند</span>
        </li>
      </template>
    </KsList>

    <!--   PREVIEW MODAL   -->
    <KsModal v-model="previewModal.visible" blur>
      <div
        class="p-1 position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
        <div
          class="ps-3"
          style="overflow-y: auto; max-width: 380px; height: 65vh">
          <div class="d-flex flex-column gap-2" dir="rtl">
            <span class="text-medium fw-bold">
              {{ previewModal.data.title }}
            </span>

            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-4" dir="ltr">
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:کد ورود کالا</span>
                <span class="text-small">
                  {{ previewModal.data.enterCommodityCode }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:کد خروج کالا</span>
                <span class="text-small">
                  {{ previewModal.data.exitCommodityCode }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:درصد وزنی</span>
                <span class="text-small">
                  {{ previewModal.data.weightPercent }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title"
                  >:پیش بینی زمان تولید مدارک</span
                >
                <span class="text-small" dir="rtl">
                  {{ previewModal.data.mapDuration }}
                  <span>روز</span>
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title"
                  >:پیش بینی زمان تایید کارفرما</span
                >
                <span class="text-small" dir="rtl">
                  {{ previewModal.data.masterDuration }}
                  <span>روز</span>
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title"
                  >:پیش بینی زمان تامین تامین</span
                >
                <span class="text-small" dir="rtl">
                  {{ previewModal.data.processDuration }}
                  <span>روز</span>
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:نوع فرآیند تامین</span>
                <span class="text-small">{{
                  previewModal.data.typeDescription
                }}</span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:هزینه فرآیند تامین</span>
                <span class="text-small fw-bold" dir="rtl">
                  {{ previewModal.data.programPrice.toLocaleString("ar-SA") }}
                  تومان
                </span>
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
                v-for="(file, indexProcessFile) in previewModal.data.files"
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
                    :disabled="!file.isFinal"
                    :loading="loadings.downloadBtn === indexProcessFile"
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

        <div class="d-flex gap-2 mt-4">
          <KsButton
            color="dark"
            theme="flat"
            title="بستن"
            @click="previewModal.visible = false" />
        </div>
      </div>
    </KsModal>

    <!--    REVERT PROCESS STATUS MODAL   -->
    <KsModal v-model="revertProcessStatusModal.visible" blur>
      <div class="text-center pt-2">
        <span class="color-danger fw-bold text-large"> بازگردانی فرآیند </span>
      </div>

      <div class="text-center my-3" dir="rtl">
        <span class="text-small">
          برای بازگرداندن این فرآیند یکی از مراحل زیر را انتخاب کنید
        </span>

        <KsSelect
          v-model="selectedRevertStatus"
          :options="getAvailableRevertStatusList()"
          class="my-4"
          label-prop-name="title"
          placeholder="انتخاب مرحله"
          simple-placeholder
          value-prop-name="status" />
      </div>

      <div class="d-flex justify-content-end gap-2 mt-3">
        <KsButton
          :disabled="selectedRevertStatus === null"
          :loading="loadings.revertProcessStatusBtn"
          color="danger"
          theme="flat"
          title="تایید"
          @click="requestRevertProcessStatus" />
        <KsButton
          color="transparent"
          title="لغو"
          @click="revertProcessStatusModal.visible = false" />
      </div>
    </KsModal>
  </div>
</template>

<style lang="scss" scoped>
.item-title {
  width: 180px;
  font-weight: bold;
  text-align: right;
}
</style>
