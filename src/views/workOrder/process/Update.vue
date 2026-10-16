<script lang="ts" setup>
import type _Process from "@/types/Process";
import type { AxiosRequestConfig } from "axios";
import type WorkflowGroup from "@/types/WorkFlowGroup";
import { onMounted, ref, type Ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  addProcess,
  attachFileProcess,
  detachFileProcess,
  editProcess,
  finalizeFileProcess,
  getProcessDetail,
} from "@/api/ProcessAPIs";
import { changeDocumentFile, changeDocumentInfo } from "@/api/DocumentAPIs";
import { getMachineList } from "@/api/MachineAPIs";
import { getMockFile } from "@/composables/getMockFile";
import { downloadFile } from "@/composables/downloadFile";
import { getWorkflowGroupList } from "@/api/WorkFlowGroupAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsInputSeperate from "@/components/common/KsInputSeperate.vue";
import KsUploadProgress from "@/components/common/KsUploadProgress.vue";
import KsButton from "@/components/common/KsButton.vue";
import DeleteModal from "@/components/app/DeleteModal.vue";
import KsFilePicker from "@/components/common/KsFilePicker.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import CodeBranchIcon from "@/components/icons/CodeBranchIcon.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import { Colors } from "@/assets/colors";
import mime from "mime-types";
import type AttachFileToProccess from "@/types/AttachFileToProccess";
import type Machine from "@/types/Machine";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();
const route = useRoute();
const workflowGroups: Ref<WorkflowGroup[]> = ref([]);
const machinesList: Ref<Machine[]> = ref([]);
const attachFileToProcess: Ref<AttachFileToProccess> = ref({
  documentId: "",
  processId: 0,
  description: "",
  major: 0,
  minor: 0,
  uploadCondition: 1,
});

const process: Ref<_Process> = ref({
  id: null,
  title: "",
  exitCommodityCode: "",
  enterCommodityCode: "",
  mapDuration: 0,
  masterDuration: 0,
  processDuration: 0,
  weightPercent: "0.00",
  machineId: 0,
  type: "",
  programPrice: 0,
  workflow: 0,
  bomId: route.query.bomId.toString(),
  files: [],
});

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  deleteBtnLoading: null,
  downloadBtnLoading: null,
  uploadBtnLoading: false,
  uploadingProgress: {
    activeIndex: null,
    progress: 0,
  },
});

const deleteModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
});

const processTypeList = [
  { title: "خرید کالا", value: "1" },
  { title: "خرید خدمت", value: "2" },
  { title: "مونتاژ", value: "3" },
  { title: "مونتاژ نهایی", value: "4" },
  { title: "تولید", value: "5" },
];

const changeMachineId = async (option: { value: number }) => {
  process.value.machineId = option.value;
};

const parseProcessWeightPercentToDecimal = () => {
  if (process.value.weightPercent)
    process.value.weightPercent = parseFloat(
      process.value.weightPercent
    ).toFixed(2);
};

// const openDeleteModal = (id, index) => {
//   deleteModal.value.activeId = id;
//   deleteModal.value.activeIndex = index;
//   if (id) deleteModal.value.visible = true;
//   else removeProcessAttachedFileForm();
// };

const addProcessAttachFileForm = () => {
  // process.value.files.push({
  //   id: null,
  //   description: "",
  //   name: "",
  //   code: "",
  //   size: "",
  //   file: null,
  //   mimeType: "",
  // });
  attachFileToProcess.value.description = "";
  attachFileToProcess.value.major = 0;
  attachFileToProcess.value.minor = 0;
  attachFileToProcess.value.uploadCondition = 1;
};

// const onProcessAttachedFileChanged = (file: FileList, index) => {
//   if (file[0]) {
//     process.value.files[index].file = file[0];
//     process.value.files[index].mimeType = "." + mime.extension(file[0].type);
//     if (!process.value.files[index].description)
//       process.value.files[index].description = file[0].name;
//     process.value.files[index].size = file[0].size.toString();
//   }
// };

async function downloadProcessAttachFile(file, index: number) {
  loadings.value.downloadBtnLoading = index;
  await downloadFile(file, file.id);
  loadings.value.downloadBtnLoading = null;
}

// const removeProcessAttachedFileForm = async () => {
//   if (deleteModal.value.activeId) {
//     loadings.value.deleteBtnLoading = deleteModal.value.activeIndex;
//     const [data, error] = await detachFileProcess(
//       process.value.id,
//       deleteModal.value.activeId
//     );

//     if (!data?.success) showErrorNotification(data?.message || error.message);
//     else {
//       process.value.files.splice(deleteModal.value.activeIndex, 1);
//       showSuccessNotification("فایل پیوست با موفقیت حذف شد");
//     }

//     loadings.value.deleteBtnLoading = null;
//   } else process.value.files.splice(deleteModal.value.activeIndex, 1);
//   deleteModal.value.visible = false;
// };

// async function uploadProcessAttachFile() {
//   let isSuccess = true;
//   loadings.value.uploadBtnLoading = true;
//   for (let i = 0; i < process.value.files.length; i++) {
//     loadings.value.uploadingProgress.activeIndex = i;
//     const config: AxiosRequestConfig = {
//       headers: {
//         "Content-Type": "multipart/form-data",
//         Authorization: "Bearer " + window.localStorage.getItem("token"),
//       },
//       onUploadProgress: (progressEvent) => {
//         const { loaded, total } = progressEvent;
//         loadings.value.uploadingProgress.progress = Math.round(
//           (loaded * 100) / total
//         );
//       },
//     };

//     const formData = new FormData();
//     formData.append("file", process.value.files[i].file ?? getMockFile());

//     const [data, error] = process.value.files[i].id
//       ? await changeDocumentInfo(process.value.files[i])
//       : await attachFileProcess(attachFileToProcess.value);
//     if (!data?.success) {
//       showErrorNotification(data?.message || error.message);
//       isSuccess = false;
//       break;
//     } else if (!process.value.files[i].id)
//       process.value.files[i].id = data.data;

//     if (process.value.files[i].file) {
//       const [data, error] = await changeDocumentFile(
//         process.value.files[i].id,
//         formData
//       );
//       if (data.success) {
//         const [data2, error2] = await finalizeFileProcess(
//           process.value.id,
//           process.value.files[i].id
//         );
//         if (data2.success) process.value.files[i].isFinal = true;
//       }
//     }

//     loadings.value.uploadingProgress.progress = 0;
//   }

//   loadings.value.uploadingProgress.activeIndex = null;
//   loadings.value.uploadBtnLoading = false;
//   if (isSuccess) {
//     showSuccessNotification(
//       "فرآیند با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
//     );
//     router.back();
//   }
// }

async function fetchWorkflowGroup() {
  loadings.value.uploadBtnLoading = true;

  const [data, error] = await getWorkflowGroupList();
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      workflowGroups.value = data.data;
    }
  }
  loadings.value.uploadBtnLoading = false;
}

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = process.value.id
    ? await editProcess(process.value)
    : await addProcess(process.value);
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    process.value.id = data.data.id;
    router.back();
  }
  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (
    process.value.title === "" ||
    process.value.title === null ||
    process.value.enterCommodityCode === "" ||
    process.value.enterCommodityCode === null ||
    process.value.weightPercent === "" ||
    process.value.weightPercent === null ||
    process.value.mapDuration === null ||
    process.value.masterDuration === null ||
    process.value.processDuration === null ||
    process.value.type === "" ||
    process.value.type === null ||
    process.value.programPrice === null
  )
    isValid = false;

  if (isValid) return true;
  else {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
};

async function fetchMachine() {
  loadings.value.inputLoading = true;
  const [data, error] = await getMachineList();
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      machinesList.value = data.data;
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  loadings.value.inputLoading = false;
}

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;
  const [data, error] = await getProcessDetail(
    id,
    route.query.bomId.toString()
  );
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else process.value = data.data;
  loadings.value.inputLoading = false;
}

onMounted(() => {
  if (props.id) fetchDetail(props.id);
  fetchMachine();
  fetchWorkflowGroup();
});
</script>

<template>
  <div>
    <KsUpdateForm
      :btn-loading="loadings.btnLoading"
      :is-edit="!!props.id"
      form-title="فرآیند تامین"
      @formSubmit="sendRequest">
      <template #formIcon>
        <CodeBranchIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #form>
        <div key="1" class="col-12 col-md-10 mb-5">
          <div
            class="d-flex flex-column flex-sm-row-reverse gap-5 gap-sm-3 align-items-sm-center">
            <KsInput
              v-model="process.title"
              :loading="loadings.inputLoading"
              class="flex-1"
              placeholder="عنوان" />
            <KsSelect
              v-model="process.type"
              :loading="loadings.inputLoading"
              :options="processTypeList"
              class="flex-1"
              placeholder="انتخاب فرآیند" />
            <KsSelect
              v-if="process.type == '5'"
              v-model="process.machineId"
              :options="machinesList"
              value-prop-name="id"
              label-prop-name="title"
              class="flex-1"
              placeholder="دستگاه"
              @onOptionClick="changeMachineId"
              custom-handle-option-click />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse gap-5 gap-sm-3 align-items-sm-center mt-5">
            <KsInput
              v-model="process.enterCommodityCode"
              :loading="loadings.inputLoading"
              class="flex-1"
              placeholder="کد کالای ورودی" />
            <KsInput
              v-model="process.exitCommodityCode"
              :is-disabled="process.type === '1'"
              :loading="loadings.inputLoading"
              class="flex-1"
              placeholder="کد کالای خروجی" />
            <KsInput
              v-model="process.weightPercent"
              :loading="loadings.inputLoading"
              class="flex-1"
              input-type="number"
              placeholder="درصد وزنی"
              step="0.10"
              @blur="parseProcessWeightPercentToDecimal" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse gap-5 gap-sm-3 align-items-sm-center mt-5">
            <KsInput
              v-model="process.mapDuration"
              :loading="loadings.inputLoading"
              class="flex-1"
              input-type="number"
              placeholder="مدت پیش بینی زمان تولید مدارک" />
            <KsInput
              v-model="process.masterDuration"
              :loading="loadings.inputLoading"
              class="flex-1"
              input-type="number"
              placeholder="مدت پیش بینی زمان تایید کارفرما" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse gap-5 gap-sm-3 align-items-sm-center mt-5">
            <KsInput
              v-model="process.processDuration"
              :loading="loadings.inputLoading"
              class="flex-1"
              input-type="number"
              placeholder="مدت پیش بینی زمان تامین" />
            <KsInputSeperate
              v-model="process.programPrice"
              :loading="loadings.inputLoading"
              class="flex-1"
              placeholder="هزینه" />
            <KsSelect
              :loading="loadings.uploadBtnLoading"
              :options="workflowGroups"
              v-model="process.workflow"
              class="flex-1"
              placeholder="دستگردان مدارک"
              label-prop-name="title"
              value-prop-name="id" />
          </div>

          <!--  ًٍEquipment Process Attach Files -->
          <transition-group
            key="equipmentProcess.files"
            class="d-flex flex-column gap-4"
            dir="ltr"
            mode="out-in"
            name="fade"
            tag="div">
            <hr key="hr" />
            <span key="title" class="text-medium fw-bold">
              :اسناد کنترل کیفی
            </span>
            <KsUploadProgress :progress="loadings.uploadingProgress.progress">
              <div
                class="d-flex flex-column flex-md-row-reverse align-items-md-center gap-3 mt-3">
                <div class="d-flex flex-row-reverse align-items-center gap-3">
                  <KsButton
                    color="warn"
                    fixed-width-height="42"
                    theme="flat"
                    tooltip="حذف">
                    <template #content>
                      <TimesIcon :fill="Colors.warn" width="18" />
                    </template>
                  </KsButton>
                  <KsFilePicker :height="42" class="flex-fill" icon-size="20" />
                </div>
                <div
                  class="d-flex flex-1 flex-row-reverse align-items-center gap-3">
                  <KsInput
                    v-model="attachFileToProcess.major"
                    class="flex-fill"
                    custom-height="42"
                    placeholder="کد اولیه"
                    simple-placeholder />
                </div>
                <div
                  class="d-flex flex-1 flex-row-reverse align-items-center gap-3">
                  <KsInput
                    v-model="attachFileToProcess.minor"
                    class="flex-fill"
                    custom-height="42"
                    placeholder="کد ثانویه"
                    simple-placeholder />
                </div>
              </div>
              <KsInput
                v-model="attachFileToProcess.description"
                class="flex-fill mt-3"
                custom-height="42"
                placeholder="توضیحات سند کنترل کیفی"
                simple-placeholder />
            </KsUploadProgress>
            <div
              key="action-buttons"
              class="d-flex flex-column flex-sm-row gap-2 mt-3">
              <KsButton
                color="primary"
                fixed-width-height="42"
                tooltip="افزودن سند کنترل کیفی"
                @click="addProcessAttachFileForm">
                <template #content>
                  <PlusIcon :fill="Colors.white" width="1غشق 6" />
                </template>
              </KsButton>
            </div>
          </transition-group>
        </div>
      </template>
    </KsUpdateForm>

    <!--    DELETE MODAL   -->
    <!-- <DeleteModal
      v-model="deleteModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteModal.visible = false"
      @submit="removeProcessAttachedFileForm" /> -->
  </div>
</template>
