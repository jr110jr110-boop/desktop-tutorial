<script lang="ts" setup>
import type { AxiosRequestConfig } from "axios";
import type { Ref } from "vue";
import { ref } from "vue";
import { downloadFile } from "@/composables/downloadFile";
import { useEquipmentAssembly } from "@/store";
import { EQUIPMENT_PROCESS_FILE_MODEL } from "@/models";
import {
  attachFileEquipmentProcess,
  detachFileEquipmentProcess,
  finalizeFileEquipmentProcess,
} from "@/api/EquipmentAPIs";
import { changeDocumentFile, changeDocumentInfo } from "@/api/DocumentAPIs";
import { getMockFile } from "@/composables/getMockFile";
import mime from "mime-types";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import KsInput from "@/components/common/KsInput.vue";
import KsFilePicker from "@/components/common/KsFilePicker.vue";
import KsUploadProgress from "@/components/common/KsUploadProgress.vue";
import KsButton from "@/components/common/KsButton.vue";
import DeleteModal from "@/components/app/DeleteModal.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import CloudUploadIcon from "@/components/icons/CloudUploadIcon.vue";
import { Colors } from "@/assets/colors";

const { previewModal, fetchEquipmentAssemblyList } = useEquipmentAssembly.value;
const hiddenBtn: Ref<Boolean> = ref(true);
const loadings = ref({
  downloadBtn: null,
  deleteBtn: null,
  uploadBtn: false,
  uploadingProgress: {
    activeIndex: null,
    progress: 0,
  },
});

const deleteModal = ref({
  visible: false,
  id: null,
  index: null,
});

async function downloadEquipmentProcessAttachFile(file, index: number) {
  loadings.value.downloadBtn = index;
  await downloadFile(file, file.id);
  loadings.value.downloadBtn = null;
}

const openDeleteModal = (id, index) => {
  deleteModal.value = {
    visible: true,
    id,
    index,
  };
};

const removeEquipmentProcessAttachedFileForm = async () => {
  const { id, index } = deleteModal.value;
  if (id) {
    loadings.value.deleteBtn = index;
    const [data, error] = await detachFileEquipmentProcess(
      previewModal.process.id,
      id
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      previewModal.process.files.splice(index, 1);
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }
    loadings.value.deleteBtn = null;
  } else previewModal.process.files.splice(index, 1);
  deleteModal.value.visible = false;
  await fetchEquipmentAssemblyList(false, false);
};

const addEquipmentProcessAttachFileForm = () => {
  previewModal.process.files.push({ ...EQUIPMENT_PROCESS_FILE_MODEL });
  hiddenBtn.value = false;
};

const onEquipmentProcessAttachedFileChanged = (args) => {
  const { file, index } = args;
  if (file[0]) {
    previewModal.process.files[index].file = file[0];
    previewModal.process.files[index].mimeType =
      "." + mime.extension(file[0].type);
    if (!previewModal.process.files[index].description)
      previewModal.process.files[index].description = file[0].name;
    previewModal.process.files[index].size = file[0].size.toString();
  }
};

const uploadEquipmentProcessAttachFile = async () => {
  let isSuccess = true;
  loadings.value.uploadBtn = true;

  const files = previewModal.process.files;

  for (let i = 0; i < files.length; i++) {
    loadings.value.uploadingProgress.activeIndex = i;
    const axiosConfig: AxiosRequestConfig = {
      headers: {
        "Content-Type": "multipart/form-data",
        Authorization: "Bearer " + window.localStorage.getItem("token"),
      },
      onUploadProgress: (progressEvent) => {
        const { loaded, total } = progressEvent;
        loadings.value.uploadingProgress.progress = Math.round(
          (loaded * 100) / total
        );
      },
    };

    const formData = new FormData();
    formData.append("file", files[i].file ?? getMockFile());

    const [data, error] = files[i].id
      ? await changeDocumentInfo(files[i])
      : await attachFileEquipmentProcess(
          previewModal.process.id,
          files[i].description,
          files[i].name,
          files[i].code,
          formData,
          axiosConfig
        );
    if (!data?.success) {
      showErrorNotification(data?.message || error.message);
      isSuccess = false;
      break;
    } else if (!files[i].id) previewModal.process.files[i].id = data.data;

    if (files[i].file) {
      const [data, error] = await changeDocumentFile(
        previewModal.process.files[i].id,
        formData
      );
      if (data?.success) {
        const [data2, error2] = await finalizeFileEquipmentProcess(
          previewModal.process.id,
          previewModal.process.files[i].id
        );
        if (data2?.success) previewModal.process.files[i].isFinal = true;
      }
    }

    loadings.value.uploadingProgress.progress = 0;
  }

  if (isSuccess) {
    await fetchEquipmentAssemblyList(false, false);
    showSuccessNotification("فایل های پیوست با موفقیت بروزرسانی شدند");
  }
  loadings.value.uploadingProgress.activeIndex = null;
  loadings.value.uploadBtn = false;
};
</script>

<template>
  <div>
    <div class="d-flex flex-column gap-2" dir="rtl">
      <span class="text-medium fw-bold">
        {{ previewModal.process.title }}
      </span>

      <hr class="d-none d-md-block" />
      <div class="d-flex flex-column gap-4" dir="ltr">
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:کد ورود کالا</span>
          <span class="text-small">
            {{ previewModal.process.enterCommodityCode }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:کد خروج کالا</span>
          <span class="text-small">
            {{ previewModal.process.exitCommodityCode }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:درصد وزنی</span>
          <span class="text-small">
            {{ parseFloat(previewModal.process.weightPercent).toFixed(2) }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">
            :پیش بینی زمان تولید مدارک
          </span>
          <span class="text-small" dir="rtl">
            {{ previewModal.process.mapDuration }}
            <span>روز</span>
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">
            :پیش بینی زمان تایید کارفرما
          </span>
          <span class="text-small" dir="rtl">
            {{ previewModal.process.masterDuration }}
            <span>روز</span>
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:پیش بینی زمان تامین تامین</span>
          <span class="text-small" dir="rtl">
            {{ previewModal.process.processDuration }}
            <span>روز</span>
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:نوع فرآیند تامین</span>
          <span class="text-small">
            {{ previewModal.process.typeDescription }}
          </span>
        </div>
        <div class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
          <span class="text-small item-title">:هزینه فرآیند تامین</span>
          <span class="text-small fw-bold" dir="rtl">
            {{ previewModal.process.programPrice.toLocaleString("ar-SA") }}
            تومان
          </span>
        </div>
      </div>

      <!--  ًٍEquipment Process Attach Files -->
      <transition-group
        key="previewModal.process.files"
        class="d-flex flex-column gap-4"
        dir="ltr"
        mode="out-in"
        name="fade"
        tag="div">
        <hr key="hr" />
        <span key="title" class="text-medium fw-bold">:اسناد کنترل کیفی</span>

        <KsUploadProgress
          v-for="(file, indexProcessFile) in previewModal.process.files"
          :key="indexProcessFile"
          :is-uploading="
            loadings.uploadingProgress.activeIndex === indexProcessFile
          "
          :progress="loadings.uploadingProgress.progress">
          <div
            class="d-flex flex-column flex-md-row-reverse align-items-md-center gap-3 mt-3">
            <div class="d-flex flex-row-reverse align-items-center gap-3">
              <KsButton
                :loading="loadings.deleteBtn === indexProcessFile"
                color="warn"
                fixed-width-height="42"
                theme="flat"
                tooltip="حذف"
                @click="openDeleteModal(file.id, indexProcessFile)">
                <template #content>
                  <TimesIcon :fill="Colors.warn" width="18" />
                </template>
              </KsButton>
              <KsFilePicker
                :file-size="file.size"
                :has-file="(file.isFinal && !!file.id) || !!file.file"
                :height="42"
                class="flex-fill"
                icon-size="20"
                @fileChange="
                  (val) =>
                    onEquipmentProcessAttachedFileChanged({
                      file: val,
                      index: indexProcessFile,
                    })
                " />
            </div>
            <div
              class="d-flex flex-1 flex-row-reverse align-items-center gap-3">
              <KsInput
                v-model="file.code"
                class="flex-fill"
                custom-height="42"
                placeholder="کد سند کنترل کیفی"
                simple-placeholder />
            </div>
            <div
              class="d-flex flex-1 flex-row-reverse align-items-center gap-3">
              <KsInput
                v-model="file.name"
                class="flex-fill"
                custom-height="42"
                placeholder="عنوان سند کنترل کیفی"
                simple-placeholder />
            </div>

            <KsButton
              key="download-btn"
              :loading="loadings.downloadBtn === indexProcessFile"
              :disabled="!file.isFinal"
              color="primary"
              fixed-width-height="42"
              theme="flat"
              tooltip="دانلود"
              @click="
                downloadEquipmentProcessAttachFile(file, indexProcessFile)
              ">
              <template #content>
                <DownloadIcon :fill="Colors.primary" width="24" />
              </template>
            </KsButton>
          </div>
          <KsInput
            v-model="file.description"
            class="flex-fill mt-3"
            custom-height="42"
            placeholder="توضیحات سند کنترل کیفی"
            simple-placeholder />
        </KsUploadProgress>

        <div
          key="action-buttons"
          class="d-flex flex-column flex-sm-row gap-2 mt-3">
          <KsButton
            :disabled="!previewModal.process.files.length"
            :loading="loadings.uploadBtn"
            class="order-1 order-sm-0"
            color="primary"
            @click="uploadEquipmentProcessAttachFile">
            <template #content>
              <div class="d-flex gap-2 align-items-center">
                <span class="text-xSmall text-white">ویرایش اسناد</span>
                <CloudUploadIcon :fill="Colors.white" width="16" />
              </div>
            </template>
          </KsButton>
          <KsButton
            v-if="hiddenBtn && previewModal.process.files.length == 0"
            key="add-btn"
            color="primary"
            fixed-width-height="42"
            theme="flat"
            tooltip="سند کنترل کیفی جدید"
            @click="addEquipmentProcessAttachFileForm">
            <template #content>
              <PlusIcon :fill="Colors.primary" width="18" />
            </template>
          </KsButton>
        </div>
      </transition-group>
    </div>

    <DeleteModal
      :button-loading="loadings.deleteBtn !== null"
      :visible="deleteModal.visible"
      @cancel="deleteModal.visible = false"
      @submit="removeEquipmentProcessAttachedFileForm" />
  </div>
</template>
