<script lang="ts" setup>
import type { Ref } from "vue";
import { ref } from "vue";
import { useRouter } from "vue-router";
import type { AxiosRequestConfig } from "axios";
import type TableValue from "@/types/TableValue";
import type FilterQuery from "@/types/FilterQuery";
import type ProjectCharter from "@/types/ProjectCharter";
import type Contract from "@/types/Contract";
import {
  updateContractProjectCharterFile,
  updateContractProjectCharterMaster,
} from "@/api/ProjectCharterAPIs";
import { downloadFile } from "@/composables/downloadFile";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import KsList from "@/components/common/KsList.vue";
import KsUploadProgress from "@/components/common/KsUploadProgress.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsModal from "@/components/common/KsModal.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsFilePicker from "@/components/common/KsFilePicker.vue";
import CopyIcon from "@/components/icons/CopyIcon.vue";
import TriangleMusicIcon from "@/components/icons/TriangleMusicIcon.vue";
import HammerIcon from "@/components/icons/HammerIcon.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import ChartLineIcon from "@/components/icons/ChartLineIcon.vue";
import { Colors } from "@/assets/colors";
import mime from "mime-types";

const tableHeads: string[] = [
  "کارفرما",
  "کد",
  "تاریخ ثبت",
  "تاریخ اتمام",
  "وضعیت",
];

const tableValues: TableValue[] = [
  {
    propName: "employer",
    type: "text",
  },
  {
    propName: "code",
    type: "text",
    maxWidth: 102,
  },
  {
    propName: "startDate",
    type: "date",
    maxWidth: 200,
  },
  {
    propName: "endDate",
    type: "date",
  },
  {
    propName: "status",
    type: "status",
    status: [
      {
        title: "جاری",
        value: 1,
        color: "primary",
      },
      {
        title: "خاتمه یافته",
        value: 2,
        color: "dark",
      },
      {
        title: "فسخ شده",
        value: 3,
        color: "warn",
      },
    ],
  },
];

const router = useRouter();

const filterQueries: FilterQuery[] = [
  {
    propName: "StartedFrom",
    title: "تاریخ شروع",
    type: "date",
    selected: "",
  },
  {
    propName: "EndedAt",
    title: "تاریخ اتمام",
    type: "date",
    selected: "",
  },
];

const projectCharterModalVisible: Ref<boolean> = ref(false);

const projectCharter: Ref<ProjectCharter> = ref({
  contractId: null,
  master: "",
  projectCharterFile: {
    id: null,
    code: ".",
    name: ".",
    description: "",
    size: "",
    file: null,
    mimeType: "",
  },
});

const loadings = ref({
  btnLoading: false,
  downloadBtnLoading: false,
  uploadingProgress: {
    isActive: false,
    progress: 0,
  },
});

const reFetchContractList = ref(false);

const showProjectCharterModal = async (data: Contract) => {
  await emptyProjectCharterData();
  projectCharterModalVisible.value = true;
  projectCharter.value.contractId = data.id;
  projectCharter.value.master = data.master;
  if (data.projectCharterFile)
    for (let key of Object.keys(data.projectCharterFile))
      projectCharter.value.projectCharterFile[key] =
        data.projectCharterFile[key];
};

const emptyProjectCharterData = () => {
  projectCharter.value.contractId = null;
  projectCharter.value.master = "";
  projectCharter.value.projectCharterFile = {
    id: null,
    code: ".",
    name: ".",
    description: "",
    size: "",
    file: null,
    mimeType: "",
  };
};

const onAttachedFileChanged = (file: FileList) => {
  if (file[0]) {
    projectCharter.value.projectCharterFile.id = null;
    projectCharter.value.projectCharterFile.file = file[0];
    projectCharter.value.projectCharterFile.mimeType =
      "." + mime.extension(file[0].type);
    if (!projectCharter.value.projectCharterFile.description)
      projectCharter.value.projectCharterFile.description = file[0].name;
    projectCharter.value.projectCharterFile.size = file[0].size.toString();
  }
};

async function downloadAttachFile(fileId) {
  loadings.value.downloadBtnLoading = true;
  await downloadFile(projectCharter.value.projectCharterFile, fileId);
  loadings.value.downloadBtnLoading = false;
}

async function requestUpdateProjectCharter() {
  if (
    projectCharter.value.projectCharterFile.id ||
    projectCharter.value.projectCharterFile.file
  ) {
    loadings.value.btnLoading = true;
    loadings.value.uploadingProgress.isActive = true;
    let isSuccess = true;
    const [data, error] = await updateContractProjectCharterMaster({
      contractId: projectCharter.value.contractId,
      master: projectCharter.value.master,
    });

    if (!data?.success) {
      showErrorNotification(data?.message || error.message);
      isSuccess = false;
    } else {
      if (projectCharter.value.projectCharterFile.file) {
        const config: AxiosRequestConfig = {
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
        formData.append("file", projectCharter.value.projectCharterFile.file);

        const [data, error] = await updateContractProjectCharterFile(
          projectCharter.value.contractId,
          projectCharter.value.projectCharterFile.description,
          projectCharter.value.projectCharterFile.code,
          projectCharter.value.projectCharterFile.name,
          formData,
          config
        );

        loadings.value.uploadingProgress.progress = 0;
        if (!data?.success) {
          showErrorNotification(data?.message || error.message);
          isSuccess = false;
        } else {
          projectCharter.value.projectCharterFile.id = data.data;
          projectCharterModalVisible.value = false;
        }
      }
    }
    if (isSuccess) {
      showSuccessNotification("منشور پروژه قرارداد با موفقیت بروز شد");
      projectCharterModalVisible.value = false;
      await requestReFetchData();
    }
    loadings.value.uploadingProgress.isActive = false;
    loadings.value.btnLoading = false;
  } else showErrorNotification("فایل پیوست منشور پروژه الزامیست");
}

const requestReFetchData = () => {
  reFetchContractList.value = true;
  setTimeout(() => {
    reFetchContractList.value = false;
  }, 200);
};

const onReportProgressButtonClick = (contractId) => {
  router.push({
    name: "ReportContractProgress",
    query: {
      contract: contractId,
    },
  });
};
</script>

<template>
  <div>
    <KsList
      :filter-queries="filterQueries"
      :re-fetch-data="reFetchContractList"
      :table-heads="tableHeads"
      :table-values="tableValues"
      api-delete-url="api/v1/Contracts/delete-contract-collection"
      api-fetch-url="api/v1/Contracts"
      list-title="قرارداد"
      search-terms="کارفرما"
      update-route-name="ContractUpdate">
      <template #listIcon>
        <CopyIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #custom-action-btn="{ rowData }">
        <!--        <li-->
        <!--          :class="{-->
        <!--            'no-pointer-event opacity-50': !rowData.projectCharterFile,-->
        <!--          }"-->
        <!--          class="d-flex flex-row-reverse align-items-center gap-3"-->
        <!--          @click="-->
        <!--            $router.push({-->
        <!--              name: 'WorkOrderList',-->
        <!--              params: { contractId: rowData.id },-->
        <!--            })-->
        <!--          ">-->
        <!--          <HammerIcon :fill="Colors.black" width="16" />-->
        <!--          <span class="text-small">دستورات ساخت</span>-->
        <!--        </li>-->
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="
            $router.push({
              name: 'WorkOrderList',
              params: { contractId: rowData.id },
            })
          ">
          <HammerIcon :fill="Colors.black" width="16" />
          <span class="text-small">دستورات ساخت</span>
        </li>
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="showProjectCharterModal(rowData)">
          <TriangleMusicIcon :fill="Colors.black" width="16" />
          <span class="text-small">منشور پروژه</span>
        </li>
        <li
          class="d-flex flex-row-reverse align-items-center gap-3"
          @click="onReportProgressButtonClick(rowData.id)">
          <ChartLineIcon :fill="Colors.black" width="16" />
          <span class="text-small">گزارشات قرارداد</span>
        </li>
      </template>
    </KsList>

    <KsModal v-model="projectCharterModalVisible" blur>
      <div class="h-100 overflow-hidden d-flex flex-column gap-4 pt-5 pb-1">
        <KsInput v-model="projectCharter.master" placeholder="مدیر پروژه" />
        <div class="mt-1 text-end">
          <span class="text-small"> :فایل پیوست منشور پروژه </span>
        </div>
        <KsUploadProgress
          :is-uploading="loadings.uploadingProgress.isActive"
          :progress="loadings.uploadingProgress.progress">
          <KsFilePicker
            :file-size="projectCharter.projectCharterFile.size"
            :has-file="
              !!projectCharter.projectCharterFile.id ||
              !!projectCharter.projectCharterFile.file
            "
            :height="42"
            class="flex-fill"
            icon-size="20"
            @fileChange="(val) => onAttachedFileChanged(val)" />
          <KsInput
            v-model="projectCharter.projectCharterFile.description"
            class="flex-fill mt-3"
            placeholder="عنوان فایل پیوست"
            simple-placeholder />
          <KsButton
            v-if="projectCharter.projectCharterFile.id"
            key="download-btn"
            :loading="loadings.downloadBtnLoading"
            class="mt-3 w-100"
            color="primary"
            fixed-width-height="42"
            theme="flat"
            @click="downloadAttachFile(projectCharter.projectCharterFile.id)">
            <template #content>
              <div class="d-flex flex-row-reverse gap-2 align-items-center">
                <DownloadIcon :fill="Colors.primary" width="24" />
                <span class="color-primary">دانلود فایل</span>
              </div>
            </template>
          </KsButton>
        </KsUploadProgress>

        <div class="d-flex gap-3">
          <KsButton
            :loading="loadings.btnLoading"
            color="primary"
            class="mt-3 flex-1"
            title="ثبت"
            @click="requestUpdateProjectCharter" />
          <KsButton
            color="dark"
            class="mt-3 flex-1"
            theme="flat"
            title="بستن"
            @click="projectCharterModalVisible = false" />
        </div>
      </div>
    </KsModal>
  </div>
</template>
<style lang="scss" scoped>
.btn-success-icon,
.btn-danger-icon {
  position: absolute;
  top: -4px;
  right: -4px;
  width: 18px;
  height: 18px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 16px;
}

.btn-success-icon {
  background: rgba(var(--success), 1);
}

.btn-danger-icon {
  background: rgba(var(--danger), 1);
  z-index: 99;
}
</style>
