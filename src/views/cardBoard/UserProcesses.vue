<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, reactive, ref } from "vue";
import type CardBoardUserProcess from "@/types/CardBoardUserProcess";
import type User from "@/types/User";
import { getCardBoardUserProcesses } from "@/api/CardBoardAPIs";
import {
  attachFileProcess,
  detachFileProcess,
  processAcceptDocuments,
  processNotAcceptDocuments,
  processChangeSelectedUser,
  processMasterApproved,
  processPMOAccept,
} from "@/api/ProcessAPIs";
import { getUserLookup } from "@/api/UserAPIs";
import { useRouter } from "vue-router";
import { downloadFile } from "@/composables/downloadFile";
import { addDocument, getDocument, getDocumentFileInfo } from "@/api/Document";

import showSuccessNotification from "@/composables/showSuccessNotification";
import showErrorNotification from "@/composables/showErrorNotification";

import DeleteModal from "@/components/app/DeleteModal.vue";
import KsAccordion from "@/components/common/KsAccordion.vue";
import KsButton from "@/components/common/KsButton.vue";
import BriefcaseIcon from "@/components/icons/BriefcaseIcon.vue";
import TasksIcon from "@/components/icons/TasksIcon.vue";
import KsModal from "@/components/common/KsModal.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import EyeIcon from "@/components/icons/EyeIcon.vue";
import KsCheckBox from "@/components/common/KsCheckBox.vue";
import NoDataFound from "@/layouts/NoDataFound.vue";
import KsInput from "@/components/common/KsInput.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import KsFilePicker from "@/components/common/KsFilePicker.vue";
import CloudUploadIcon from "@/components/icons/CloudUploadIcon.vue";
import ShoppingCartIcon from "@/components/icons/ShoppingCartIcon.vue";
import MenuIcon from "@/components/icons/MenuIcon.vue";
import { Colors } from "@/assets/colors";
import type AttachFileToProccess from "@/types/AttachFileToProccess";
import type _File from "@/types/_File";
import EditIcon from "@/components/icons/EditIcon.vue";

const docData: Ref<_File> = ref({
  id: "",
  name: "",
  description: "",
  mimeType: "",
  code: "",
});
const addedFiles: Ref<FileList> = ref();
const isFileAdded: Ref<boolean> = ref(false);
const addFieldAttachFile: Ref<boolean> = ref(false);
const disableBtnUploadCondition: Ref<boolean> = ref(false);
const disableFieldUploadCondition: Ref<boolean> = ref(false);

const attachFileToProcessInfo: Ref<AttachFileToProccess> = ref({
  documentId: "00000000-0000-0000-0000-000000000000",
  processId: 0,
  description: "",
  major: 0,
  minor: 0,
  uploadCondition: 1,
});

type PreviewModalMode =
  | "PmoAccept"
  | "PmoAcceptMultiple"
  | "ChooseEngineer"
  | "UpdateDocuments"
  | "AcceptDocuments"
  | "FailureToSend"
  | "ClientApproved";

const router = useRouter();

const state = reactive({
  isLoading: true,
  uploadBtnLoading: false,
  downloadBtnLoading: null,
  modalSubmitLoading: false,
  engineerListLoading: false,
  deleteBtnLoading: null,
  uploadingProgress: {
    activeIndex: null,
    progress: 0,
  },
});

const userMode = localStorage.getItem("userRole");

const previewModal = reactive({
  visible: false,
  madeIndex: null,
  assemblyIndex: null,
  bomIndex: null,
  activeIndex: null,
});

const taskModal = reactive({
  visible: false,
  modalTitle: "",
  activeIndex: null,
  madeIndex: 0,
  bomIndex: null,
  assemblyIndex: null,
  mode: null as PreviewModalMode,
});

const deleteModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
});

const userProcessesFlat = ref([]);
const userProccesses: Ref<CardBoardUserProcess[]> = ref([]);
const engineerList: Ref<User[]> = ref([]);
const selectedproccessesCheckBoxVisible = ref(false);

const onPreviewModalRequest = (
  madeIndex: number,
  assemblyIndex: number,
  bomIndex: number,
  index: number
) => {
  previewModal.madeIndex = madeIndex;
  previewModal.assemblyIndex = assemblyIndex;
  previewModal.bomIndex = bomIndex;
  previewModal.activeIndex = index;
  previewModal.visible = true;
};

const openDeleteModal = (id, activeIndex?) => {
  deleteModal.value.activeId = id;
  deleteModal.value.activeIndex = activeIndex;
  if (id) deleteModal.value.visible = true;
  else removeProcessAttachedFileForm();
};

function addProcessAttachFileForm() {
  disableBtnUploadCondition.value = true;
  disableFieldUploadCondition.value = true;
  addFieldAttachFile.value = true;
  attachFileToProcessInfo.value.uploadCondition = 2;
}

function undoProcessAttachFileForm() {
  disableBtnUploadCondition.value = true;
  disableFieldUploadCondition.value = true;
  addFieldAttachFile.value = true;
  attachFileToProcessInfo.value.uploadCondition = 3;
}

const onProcessAttachedFileChanged = (file: FileList) => {
  isFileAdded.value = true;
  addedFiles.value = file;
};

async function uploadImage() {
  if (isFileAdded.value) {
    const formData = new FormData();
    formData.append("file", addedFiles.value[0]);

    const [data, error] = await addDocument(
      formData,
      "workflow",
      "file " +
        Number(
          userProccesses.value[taskModal.madeIndex].assemblies[
            taskModal.assemblyIndex
          ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].files
            .length + 1
        ),
      "workflow"
    );
    if (error) {
      showErrorNotification(error?.message);
    } else {
      attachFileToProcessInfo.value.documentId = data;
    }
  }
}

const onTaskModalRequest = (
  modalTitle: string,
  madeIndex?: number,
  assemblyIndex?: number,
  bomIndex?: number,
  index?: number,
  mode?: PreviewModalMode
) => {
  taskModal.modalTitle = modalTitle;
  taskModal.madeIndex = madeIndex;
  taskModal.assemblyIndex = assemblyIndex;
  taskModal.bomIndex = bomIndex;
  taskModal.activeIndex = index;
  taskModal.mode = mode;
  taskModal.visible = true;
};

const removeProcessAttachedFileForm = async () => {
  if (deleteModal.value.activeId) {
    state.deleteBtnLoading = deleteModal.value.activeIndex;
    const [data, error] = await detachFileProcess(
      userProccesses.value[taskModal.madeIndex].assemblies[
        taskModal.assemblyIndex
      ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].id,
      deleteModal.value.activeId
    );
    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      userProccesses.value[taskModal.madeIndex].assemblies[
        taskModal.assemblyIndex
      ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].files.splice(
        deleteModal.value.activeIndex,
        1
      );
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }
    state.deleteBtnLoading = null;
  } else
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].files.splice(
      deleteModal.value.activeIndex,
      1
    );
  deleteModal.value.visible = false;
};

async function uploadProcessAttachFile() {
  if (
    isFileAdded.value == false &&
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].files.length ==
      0
  ) {
    showErrorNotification("فایلی برای آپلود انتخاب نشده است");
    return;
  }
  state.uploadBtnLoading = true;
  await uploadImage();

  attachFileToProcessInfo.value.processId =
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].id;

  const [data, error] = await attachFileProcess(attachFileToProcessInfo.value);

  if (error) showErrorNotification(data?.message);
  else {
    if (data.success) {
      showSuccessNotification("اسناد کنترل کیفی با موفقیت بروزرسانی شدند");
      taskModal.visible = false;
      location.reload();
    } else {
      showErrorNotification(data?.message);
    }
  }
  state.uploadBtnLoading = false;
}

async function fetchDocumentById(fileId: string) {
  const [data] = await getDocument(fileId);
  if (data.success == undefined) {
    const url = window.URL.createObjectURL(
      new Blob([data], { type: "application/jpg" })
    );
    const link = document.createElement("a");
    link.href = url;
    link.setAttribute("download", docData.value.name + docData.value.mimeType);
    document.body.appendChild(link);
    link.click();
    showSuccessNotification("سند کنترل کیفی با موفقیت دانلود شد");
  } else {
    showErrorNotification("سند کنترل کیفی با خطا مواجه شد!");
  }
}

async function fetchDocumentFileInfo(fileId) {
  state.modalSubmitLoading = true;
  const [data, error] = await getDocumentFileInfo(fileId);
  if (!error) {
    docData.value = data.data;
    fetchDocumentById(fileId);
  } else {
    showErrorNotification("سند کنترل کیفی با خطا مواجه شد!");
  }
  state.modalSubmitLoading = false;
}

async function sendEngineerAcceptDocuments() {
  state.modalSubmitLoading = true;
  const [data, error] = await processAcceptDocuments(
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].id,
    userMode
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses.splice(taskModal.activeIndex, 1);
    showSuccessNotification("فرآیند با موفقیت به مرحله بعد ارسال شد");
  }

  state.modalSubmitLoading = false;
  taskModal.visible = false;
}

async function sendEngineerFailureToSend() {
  state.modalSubmitLoading = true;
  const [data, error] = await processNotAcceptDocuments(
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].id
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses.splice(taskModal.activeIndex, 1);
    showSuccessNotification("فرآیند تایید نشد");
    location.reload();
  }

  state.modalSubmitLoading = false;
  taskModal.visible = false;
}

async function sendMultiplePMOAcceptRequest(
  index?,
  assemblyIndex?,
  bomIndex?,
  activeIndex?
) {
  state.modalSubmitLoading = true;
  let isSuccess = true;
  if (assemblyIndex !== null)
    isSuccess = await sendPMOAcceptRequest(
      index,
      assemblyIndex,
      bomIndex,
      activeIndex
    );
  else
    for (let i = 0; i < userProccesses.value.length; i++)
      for (let j = 0; j < userProccesses.value[i].assemblies.length; j++)
        for (
          let k = 0;
          k < userProccesses.value[i].assemblies[j].boms.length;
          k++
        )
          for (
            let l = 0;
            l < userProccesses.value[i].assemblies[j].boms[k].proccesses.length;
            l++
          )
            if (
              userProccesses.value[i].assemblies[j].boms[k].proccesses[l]
                .isSelected &&
              userProccesses.value[i].assemblies[j].boms[k].proccesses[l]
                .processStatus === 0
            ) {
              let isMultiSuccess = await sendPMOAcceptRequest(i, j, k, l);
              if (!isMultiSuccess) {
                isSuccess = false;
                break;
              }
            }

  taskModal.visible = false;
  state.modalSubmitLoading = false;

  if (isSuccess)
    showSuccessNotification(
      "فرآیند" + (assemblyIndex !== null ? "" : "ها") + " با موفقیت تایید شد "
    );
}

async function sendPMOAcceptRequest(
  index,
  assemblyIndex,
  bomIndex,
  activeIndex,
  retryCount = 0
) {
  const MAX_RETRIES = 3;

  const [data, error] = await processPMOAccept(
    userProccesses.value[index].assemblies[assemblyIndex].boms[bomIndex]
      .proccesses[activeIndex].id
  );

  const errorMessage = data?.message || error?.message;

  if (
    errorMessage?.includes("The instance of entity type") &&
    retryCount < MAX_RETRIES
  ) {
    setTimeout(async () => {
      return await sendPMOAcceptRequest(
        index,
        assemblyIndex,
        bomIndex,
        activeIndex,
        retryCount + 1
      );
    }, 1000);
  }

  if (!data?.success) {
    if (!errorMessage?.includes("The instance of entity type")) {
      showErrorNotification(errorMessage);
    }
    return false;
  } else {
    userProccesses.value[index].assemblies[assemblyIndex].boms[
      bomIndex
    ].proccesses.splice(activeIndex, 1);
    return true;
  }
}

async function sendMasterApprovedRequest() {
  state.modalSubmitLoading = true;
  const [data, error] = await processMasterApproved(
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].id
  );
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[
      taskModal.activeIndex
    ].processStatus = 6;
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[
      taskModal.activeIndex
    ].processStatusDescription = "فرآیند نهایی شده است";
  }
  state.modalSubmitLoading = false;
  taskModal.visible = false;
}

async function sendSelectedEngineerRequest() {
  state.modalSubmitLoading = true;
  const [data, error] = await processChangeSelectedUser(
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].id
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userProccesses.value[taskModal.madeIndex].assemblies[
      taskModal.assemblyIndex
    ].boms[taskModal.bomIndex].proccesses.splice(taskModal.activeIndex, 1);
    showSuccessNotification(
      "فرآیند با موفقیت به مهندس انتخاب شده تخصیص داده شد"
    );
    location.reload();
  }
  taskModal.visible = false;
  state.modalSubmitLoading = false;
}

const onPMOCreateBuyOrder = async (
  processId,
  bomId,
  workOrderId,
  workOrderTitle
) => {
  await router.push({
    name: "BuyOrderUpdate",
    query: { workOrderId, workOrderTitle, processId, bomId },
  });
};

const convertFlatToTree = (flatArr) => {
  let sortedFlatArr = flatArr.slice().sort();

  for (let i = 0; i < sortedFlatArr.length; i++) {
    // let isAssemblyExists = userProccesses.value[
    //   taskModal.madeIndex
    // ].assemblies.some((item) => item.id === sortedFlatArr[i].assemblyId);
    // if (!isAssemblyExists) {
    //   userProccesses.value[taskModal.madeIndex].assemblies.push({
    //     id: sortedFlatArr[i].assemblyId,
    //     title: sortedFlatArr[i].assemblyTitle,
    //     workOrderId: sortedFlatArr[i].workOrderId,
    //     workOrderTitle: sortedFlatArr[i].workOrderTitle,
    //     boms: [],
    //   });
    // }
    // let isBomExists = userProccesses.value[taskModal.madeIndex].assemblies[
    //   userProccesses.value.length - 1
    // ].boms.some((item) => item.id === sortedFlatArr[i].bomId);
    // if (!isBomExists) {
    //   userProccesses.value[taskModal.madeIndex].assemblies[
    //     userProccesses.value.length - 1
    //   ].boms.push({
    //     id: sortedFlatArr[i].bomId,
    //     title: sortedFlatArr[i].bomTitle,
    //     proccesses: [],
    //   });
    // }
    // userProccesses.value[taskModal.madeIndex].assemblies[
    //   userProccesses.value.length - 1
    // ].boms[
    //   userProccesses.value[taskModal.madeIndex].assemblies[
    //     userProccesses.value.length - 1
    //   ].boms.length - 1
    // ].proccesses.push({
    //   id: sortedFlatArr[i].id,
    //   title: sortedFlatArr[i].title,
    //   enterCommodityCode: sortedFlatArr[i].enterCommodityCode,
    //   exitCommodityCode: sortedFlatArr[i].exitCommodityCode,
    //   mapDuration: sortedFlatArr[i].mapDuration,
    //   masterDuration: sortedFlatArr[i].masterDuration,
    //   processDuration: sortedFlatArr[i].processDuration,
    //   sort: sortedFlatArr[i].sort,
    //   programPrice: sortedFlatArr[i].programPrice,
    //   needsMasterAcceptance: sortedFlatArr[i].needsMasterAcceptance,
    //   files: sortedFlatArr[i].files,
    //   userName: sortedFlatArr[i].userName,
    //   userId: sortedFlatArr[i].userId,
    //   typeDescription: sortedFlatArr[i].typeDescription,
    //   weightPercent: sortedFlatArr[i].weightPercent,
    //   type: sortedFlatArr[i].type,
    //   processStatusDescription: sortedFlatArr[i].processStatusDescription,
    //   processStatus: sortedFlatArr[i].processStatus,
    //   isSelected: true,
    // });
  }
};

const fetchCardBoardUserProcesses = async () => {
  state.isLoading = true;
  const [data, error] = await getCardBoardUserProcesses();
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userProccesses.value = data.data.data;
    convertFlatToTree(data.data.data);
  }
  state.isLoading = false;
};

// const getDetail = async (id) => {
//   loadings.value.inputLoading = true;
//   const [data, error] = await getDocumentInfo(id);
//   if (data?.success) file.value = data.data;
//   else ShowErrorNotification(data?.message || error.message);
//   loadings.value.inputLoading = false;
// };

async function downloadProcessAttachFile(file, index: number) {
  try {
    state.downloadBtnLoading = index;
    const [data, error] = await getDocumentFileInfo(index);
    if (data?.success) file.value = data.data;
    else showErrorNotification(data?.message || error.message);
    await downloadFile(file, file.documentId);
  } catch (error) {
    console.error("خطا در دانلود فایل:", error);
  } finally {
    state.downloadBtnLoading = null;
  }
}

// async function fetchDocumentById() {
//   state.isLoading = true;
//   const [data, error] = await getDocument();
//   if (!data?.success) showErrorNotification(data?.message || error.message);
//   else {
//     convertFlatToTree(data.data.data);
//     userProccessesFlat.value = data.data.data;
//   }
//   state.isLoading = false;
// }

const fetchEngineerList = async () => {
  state.engineerListLoading = true;
  const [data, error] = await getUserLookup(null, null, null, "ENGINEER");
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else engineerList.value = data.data.data;
  state.engineerListLoading = false;
};

function showMultiSelectBtn() {
  if (userMode !== "PMO") return false;
  for (let i = 0; i < userProcessesFlat.value.length; i++)
    if (userProcessesFlat.value[i].processStatus === 0) return true;
  return false;
}

onMounted(async () => {
  await fetchCardBoardUserProcesses();
  if (userMode === "ENGINEERING") await fetchEngineerList();
});
</script>

<template>
  <div class="background-color p-3 shadow-sm rounded-3 text-end">
    <!--   Header   -->
    <div
      class="d-flex flex-row-reverse justify-content-between align-items-center">
      <div class="list-title">
        <span class="fw-bold text-large text-color">فرآیندهای شما</span>
        <BriefcaseIcon :fill="Colors.black" class="ms-3" width="24" />
      </div>
    </div>
    <hr class="opacity-30" />

    <!--   List   -->
    <transition-group mode="out-in" name="fade">
      <div
        v-if="!state.isLoading && userProccesses.length"
        key="list"
        class="d-flex flex-column gap-3 mt-3">
        <div v-if="showMultiSelectBtn()" class="d-flex gap-2">
          <KsButton
            :active="selectedproccessesCheckBoxVisible"
            color="dark"
            theme="flat"
            title="انتخاب چندگانه"
            @click="
              selectedproccessesCheckBoxVisible =
                !selectedproccessesCheckBoxVisible
            " />
          <KsButton
            v-if="selectedproccessesCheckBoxVisible"
            color="primary"
            title="تایید فرآیندهای انتخاب شده"
            @click="
              onTaskModalRequest(
                '',
                null,
                null,
                null,
                null,
                'PmoAcceptMultiple'
              )
            " />
        </div>
        <KsAccordion
          v-for="(item, indexMade) in userProccesses"
          :key="indexMade"
          btn-classes="p-2"
          class="accordion-btn"
          open-on-init>
          <template #header>
            <div
              class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
              dir="rtl">
              <div class="d-flex gap-3">
                <span class="text-medium fw-bold accordion-btn-title">
                  {{ item.title }}
                </span>
              </div>
            </div>
          </template>
          <template #content>
            <div class="d-flex flex-column gap-3 pe-3 ps-1 py-2">
              <span
                v-if="!userProccesses || !userProccesses.length"
                class="text-xSmall text-center fw-bold text-dark"
                dir="rtl">
                لیست دستور ساخت خالی است
              </span>
              <div
                v-if="userProccesses && userProccesses.length"
                class="d-flex flex-column gap-2">
                <KsAccordion
                  v-for="(assembly, indexAssembly) in item.assemblies"
                  :key="indexAssembly"
                  btn-classes="p-2"
                  class="accordion-btn"
                  open-on-init>
                  <template #header>
                    <div
                      class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
                      dir="rtl">
                      <div class="d-flex gap-3">
                        <span class="text-medium fw-bold accordion-btn-title">
                          {{ assembly.title }}
                        </span>
                      </div>
                    </div>
                  </template>
                  <template #content>
                    <div class="d-flex flex-column gap-3 pe-3 ps-1 py-2">
                      <span
                        v-if="!item.assemblies || !item.assemblies.length"
                        class="text-xSmall text-center fw-bold text-dark"
                        dir="rtl">
                        لیست اسمبلی خالی است
                      </span>
                      <div
                        v-if="item.assemblies && item.assemblies.length"
                        class="d-flex flex-column gap-2">
                        <KsAccordion
                          v-for="(bom, indexBom) in assembly.boms"
                          :key="indexBom"
                          btn-classes="p-2"
                          class="accordion-btn"
                          open-on-init>
                          <template #header>
                            <div
                              class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
                              dir="rtl">
                              <span
                                class="text-medium fw-bold accordion-btn-title">
                                {{ bom.title }}
                              </span>
                            </div>
                          </template>
                          <template #content>
                            <div
                              class="d-flex flex-column gap-2 pe-3 ps-1 pb-1">
                              <span
                                v-if="!assembly.boms || !assembly.boms.length"
                                class="text-xSmall text-center fw-bold">
                                لیست BOM خالی است
                              </span>
                              <transition-group
                                v-if="assembly.boms && assembly.boms.length"
                                class="d-flex flex-column gap-2 mt-1"
                                name="list"
                                tag="div">
                                <div
                                  v-for="(
                                    process, indexProcess
                                  ) in bom.proccesses"
                                  :key="indexProcess"
                                  class="d-flex flex-row-reverse align-items-center gap-2">
                                  <KsCheckBox
                                    v-if="
                                      selectedproccessesCheckBoxVisible &&
                                      process.processStatus === 0
                                    "
                                    v-model="process.isSelected"
                                    color="primary" />
                                  <div class="accordion-btn px-2 py-1 flex-1">
                                    <div
                                      class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-sm-center"
                                      dir="rtl">
                                      <div
                                        class="d-flex gap-3 align-items-center">
                                        <span
                                          class="text-medium fw-bold accordion-btn-title">
                                          {{ indexProcess + 1 }}.
                                        </span>
                                        <span
                                          class="text-medium fw-bold accordion-btn-title pe-0">
                                          {{ process.title }}
                                        </span>
                                        -
                                        <span class="text-small">
                                          {{ process.processStatusDescription }}
                                        </span>
                                      </div>
                                      <KsButton
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
                                                  indexMade,
                                                  indexAssembly,
                                                  indexBom,
                                                  indexProcess
                                                )
                                              ">
                                              <EyeIcon
                                                :fill="Colors.black"
                                                width="16" />
                                              <span class="text-small"
                                                >مشاهده اطلاعات</span
                                              >
                                            </li>
                                            <li
                                              v-if="process.processStatus === 0"
                                              class="d-flex flex-row-reverse align-items-center gap-3"
                                              @click="
                                                onTaskModalRequest(
                                                  process.title,
                                                  indexMade,
                                                  indexAssembly,
                                                  indexBom,
                                                  indexProcess,
                                                  'PmoAccept'
                                                )
                                              ">
                                              <TasksIcon
                                                :fill="Colors.black"
                                                width="16" />
                                              <span class="text-small"
                                                >تایید فرآیند</span
                                              >
                                            </li>
                                            <li
                                              v-if="process.processStatus === 1"
                                              class="d-flex flex-row-reverse align-items-center gap-3"
                                              @click="
                                                onTaskModalRequest(
                                                  process.title,
                                                  indexMade,
                                                  indexAssembly,
                                                  indexBom,
                                                  indexProcess,
                                                  'ChooseEngineer'
                                                )
                                              ">
                                              <TasksIcon
                                                :fill="Colors.black"
                                                width="16" />
                                              <span class="text-small"
                                                >شروع دستگردان مدارک</span
                                              >
                                            </li>
                                            <li
                                              v-if="
                                                process.processStatus === 2 ||
                                                process.processStatus === 3
                                              "
                                              class="d-flex flex-row-reverse align-items-center gap-3"
                                              @click="
                                                onTaskModalRequest(
                                                  process.title,
                                                  indexMade,
                                                  indexAssembly,
                                                  indexBom,
                                                  indexProcess,
                                                  'UpdateDocuments'
                                                )
                                              ">
                                              <CloudUploadIcon
                                                :fill="Colors.black"
                                                width="16" />
                                              <span class="text-small"
                                                >بروزرسانی اسناد</span
                                              >
                                            </li>
                                            <li
                                              v-if="
                                                process.processStatus === 3 ||
                                                process.processStatus === 4
                                              "
                                              class="d-flex flex-row-reverse align-items-center gap-3"
                                              @click="
                                                onTaskModalRequest(
                                                  process.title,
                                                  indexMade,
                                                  indexAssembly,
                                                  indexBom,
                                                  indexProcess,
                                                  'AcceptDocuments'
                                                )
                                              ">
                                              <TasksIcon
                                                :fill="Colors.black"
                                                width="16" />
                                              <span class="text-small"
                                                >تایید و ارسال</span
                                              >
                                            </li>
                                            <li
                                              v-if="process.processStatus === 4"
                                              class="d-flex flex-row-reverse align-items-center gap-3"
                                              @click="
                                                onTaskModalRequest(
                                                  process.title,
                                                  indexMade,
                                                  indexAssembly,
                                                  indexBom,
                                                  indexProcess,
                                                  'FailureToSend'
                                                )
                                              ">
                                              <TasksIcon
                                                :fill="Colors.black"
                                                width="16" />
                                              <span class="text-small"
                                                >عدم تایید</span
                                              >
                                            </li>
                                            <li
                                              v-if="process.processStatus === 5"
                                              class="d-flex flex-row-reverse align-items-center gap-3"
                                              @click="
                                                onTaskModalRequest(
                                                  process.title,
                                                  indexMade,
                                                  indexAssembly,
                                                  indexBom,
                                                  indexProcess,
                                                  'ClientApproved'
                                                )
                                              ">
                                              <TasksIcon
                                                :fill="Colors.black"
                                                width="16" />
                                              <span class="text-small"
                                                >تایید توسط مشتری</span
                                              >
                                            </li>
                                            <li
                                              v-if="
                                                process.processStatus === 6 ||
                                                process.processStatus === 5
                                              "
                                              :class="{
                                                'no-pointer-event opacity-50':
                                                  process.processStatus === 5,
                                              }"
                                              class="d-flex flex-row-reverse align-items-center gap-3"
                                              @click="
                                                onPMOCreateBuyOrder(
                                                  process.id,
                                                  bom.id,
                                                  item.id,
                                                  item.title
                                                )
                                              ">
                                              <ShoppingCartIcon
                                                :fill="Colors.black"
                                                width="16" />
                                              <span class="text-small">
                                                ساخت فرآیندهای تامین، مونتاژ و
                                                ساخت</span
                                              >
                                            </li>
                                          </ul>
                                        </template>
                                      </KsButton>
                                    </div>
                                  </div>
                                </div>
                              </transition-group>
                            </div>
                          </template>
                        </KsAccordion>
                      </div>
                    </div>
                  </template>
                </KsAccordion>
              </div>
            </div>
          </template>
        </KsAccordion>
      </div>

      <div key="list-loader">
        <ListLoader :active="state.isLoading" />
      </div>

      <!--    NO DATA FOUND   -->
      <div key="NoDataComponent">
        <NoDataFound v-if="!state.isLoading && !userProccesses.length" />
      </div>
    </transition-group>

    <!--   PREVIEW MODAL   -->
    <KsModal v-model="previewModal.visible" blur>
      <div
        class="p-1 position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
        <div
          class="ps-3"
          style="overflow-y: auto; max-width: 380px; height: 65vh">
          <div class="d-flex flex-column gap-2" dir="rtl">
            <span class="text-medium fw-bold">
              {{
                userProccesses[taskModal.madeIndex].assemblies[
                  previewModal.assemblyIndex
                ].boms[previewModal.bomIndex].proccesses[
                  previewModal.activeIndex
                ].title
              }}
            </span>

            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-4" dir="ltr">
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:کد ورود کالا</span>
                <span class="text-small">
                  {{
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].enterCommodityCode
                  }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:کاربر</span>
                <span class="text-small">
                  {{
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].userName
                  }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:کد خروج کالا</span>
                <span class="text-small">
                  {{
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].exitCommodityCode
                  }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:درصد وزنی</span>
                <span class="text-small">
                  {{
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].weightPercent
                  }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title"
                  >:پیش بینی زمان تولید مدارک</span
                >
                <span class="text-small" dir="rtl">
                  {{
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].mapDuration
                  }}
                  <span>روز</span>
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title"
                  >:پیش بینی زمان تایید کارفرما</span
                >
                <span class="text-small" dir="rtl">
                  {{
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].masterDuration
                  }}
                  <span>روز</span>
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title"
                  >:پیش بینی زمان تامین تامین</span
                >
                <span class="text-small" dir="rtl">
                  {{
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].processDuration
                  }}
                  <span>روز</span>
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:نوع فرآیند تامین</span>
                <span class="text-small">{{
                  userProccesses[taskModal.madeIndex].assemblies[
                    previewModal.assemblyIndex
                  ].boms[previewModal.bomIndex].proccesses[
                    previewModal.activeIndex
                  ].typeDescription
                }}</span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:هزینه فرآیند تامین</span>
                <span class="text-small fw-bold" dir="rtl">
                  {{
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].programPrice.toLocaleString("ar-SA")
                  }}
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
              <template
                v-for="(file, indexProcessFile) in userProccesses[
                  taskModal.madeIndex
                ].assemblies[previewModal.assemblyIndex].boms[
                  previewModal.bomIndex
                ].proccesses[previewModal.activeIndex].files"
                :key="indexProcessFile">
                <div
                  v-if="
                    indexProcessFile ===
                    userProccesses[taskModal.madeIndex].assemblies[
                      previewModal.assemblyIndex
                    ].boms[previewModal.bomIndex].proccesses[
                      previewModal.activeIndex
                    ].files.length -
                      1
                  "
                  class="d-flex flex-column gap-3 mt-3">
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title"
                      >:عنوان سند کنترل کیفی</span
                    >
                    <span class="text-small">{{ file.userDispalyName }}</span>
                  </div>
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title"
                      >:نسخه سند کنترل کیفی</span
                    >
                    <span class="text-small">{{ file.version }}</span>
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
                    :disabled="!file.documentId"
                    :loading="state.downloadBtnLoading === indexProcessFile"
                    class="w-100"
                    color="primary"
                    fixed-width-height="42"
                    theme="flat"
                    tooltip="دانلود"
                    @click="fetchDocumentFileInfo(file.documentId)">
                    <template #content>
                      <DownloadIcon :fill="Colors.primary" width="24" />
                    </template>
                  </KsButton>
                </div>
              </template>
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

    <!--   TASK MODAL   -->
    <KsModal
      v-model="taskModal.visible"
      :full-screen="taskModal.mode === 'UpdateDocuments'"
      :order="0"
      blur>
      <div
        v-if="
          taskModal.mode === 'PmoAccept' ||
          taskModal.mode === 'PmoAcceptMultiple'
        ">
        <div class="text-center pt-2">
          <span class="color-primary fw-bold text-large">
            تایید فرآیند
            {{
              taskModal.mode === "PmoAcceptMultiple"
                ? "ها"
                : taskModal.modalTitle
            }}
          </span>
        </div>

        <div class="text-center my-3" dir="rtl">
          <span class="text-small">
            از تایید این فرآیند
            {{ taskModal.mode === "PmoAcceptMultiple" ? "ها" : "" }}
            مطمعن هستید؟
          </span>
        </div>

        <div class="d-flex justify-content-end gap-2 mt-3">
          <KsButton
            :loading="state.modalSubmitLoading"
            color="primary"
            title="تایید"
            @click="
              taskModal.mode === 'PmoAccept'
                ? sendMultiplePMOAcceptRequest(
                    taskModal.madeIndex,
                    taskModal.assemblyIndex,
                    taskModal.bomIndex,
                    taskModal.activeIndex
                  )
                : sendMultiplePMOAcceptRequest(null)
            " />
          <KsButton
            color="transparent"
            title="لغو"
            @click="taskModal.visible = false" />
        </div>
      </div>
      <div v-if="taskModal.mode === 'ChooseEngineer'">
        <div class="text-center pt-2">
          <span class="color-primary fw-bold text-large">
            تخصیص مهندس برای فرآیند
            {{ taskModal.modalTitle }}
          </span>
        </div>

        <div class="d-flex flex-column gap-3 text-center my-4" dir="rtl">
          <span class="text-small">
            برای فرآیند انتخاب شده یکی از کنترل کیفین زیر را انتخاب کنید
          </span>
        </div>

        <div class="d-flex justify-content-end gap-2 mt-3">
          <KsButton
            :loading="state.modalSubmitLoading"
            color="primary"
            title="تایید"
            @click="sendSelectedEngineerRequest" />
          <KsButton
            color="transparent"
            title="لغو"
            @click="taskModal.visible = false" />
        </div>
      </div>
      <div
        v-if="taskModal.mode === 'UpdateDocuments'"
        class="d-flex flex-column">
        <div
          class="p-3"
          style="height: 82vh; overflow-y: auto; overflow-x: hidden">
          <div class="text-center pt-2">
            <span class="color-primary fw-bold text-large">
              ویرایش و تایید اسناد کنترل کیفی فرآیند {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="d-flex flex-column gap-3 my-4" dir="rtl">
            <span class="text-small">
              <strong>
                اسناد کنترل کیفی این فرآیند را میتوانید ویرایش کنید و پس از
                بررسی نهایی اسناد را میتوانید تایید کنید
              </strong>
            </span>

            <!--  Process Attach Files -->
            <transition-group
              key="equipmentProcess.files"
              class="d-flex flex-column gap-4"
              dir="ltr"
              mode="out-in"
              name="fade"
              tag="div">
              <hr key="hr" />
              <span key="title" class="text-medium fw-bold">
                :دستگردان سند کنترل کیفی
              </span>

              <div class="d-flex flex-column align-items-md-center gap-3">
                <table class="table" dir="rtl">
                  <thead>
                    <tr>
                      <th scope="col" style="width: 0">ردیف</th>
                      <th scope="col">نام</th>
                      <th scope="col">نسخه</th>
                      <th scope="col">تاریخ</th>
                      <th scope="col">توضیح</th>
                      <th scope="col">فایل</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr
                      v-for="(file, indexProcessFile) in userProccesses[
                        taskModal.madeIndex
                      ].assemblies[taskModal.assemblyIndex].boms[
                        taskModal.bomIndex
                      ].proccesses[taskModal.activeIndex].files">
                      <th scope="row" class="text-center">
                        {{ indexProcessFile + 1 }}
                      </th>
                      <td>{{ file.userDispalyName }}</td>
                      <td>{{ file.version }}</td>
                      <td>{{ file.regDateString }}</td>
                      <td>{{ file.description }}</td>
                      <td>
                        <KsButton
                          v-show="
                            file.documentId !=
                            '00000000-0000-0000-0000-000000000000'
                          "
                          key="download-btn"
                          :loading="state.downloadBtnLoading"
                          color="primary"
                          fixed-width-height="42"
                          theme="flat"
                          tooltip="دانلود"
                          @click="fetchDocumentById(file.documentId)">
                          <template #content>
                            <DownloadIcon :fill="Colors.primary" width="24" />
                          </template>
                        </KsButton>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>

              <div
                class="d-flex flex-column flex-md-row-reverse align-items-md-center gap-3 w-100"
                v-if="addFieldAttachFile">
                <div class="d-flex flex-row-reverse align-items-center gap-3">
                  <KsButton
                    :loading="state.deleteBtnLoading"
                    color="warn"
                    fixed-width-height="42"
                    theme="flat"
                    tooltip="حذف"
                    @click="
                      (addFieldAttachFile = false),
                        (disableBtnUploadCondition = false)
                    ">
                    <template #content>
                      <TimesIcon :fill="Colors.warn" width="18" />
                    </template>
                  </KsButton>

                  <KsFilePicker
                    v-if="disableFieldUploadCondition"
                    :has-file="
                      !!attachFileToProcessInfo.processId ||
                      !!attachFileToProcessInfo.documentId
                    "
                    :height="42"
                    class="flex-fill"
                    icon-size="20"
                    @fileChange="(val) => onProcessAttachedFileChanged(val)" />
                </div>
                <div
                  class="d-flex flex-1 flex-row-reverse align-items-center gap-3">
                  <KsInput
                    v-model="attachFileToProcessInfo.description"
                    class="flex-fill"
                    custom-height="42"
                    placeholder="توضیحات سند کنترل کیفی"
                    simple-placeholder />
                </div>
                <div>
                  <KsButton
                    :loading="state.uploadBtnLoading"
                    class="order-1 order-sm-0"
                    color="primary"
                    :disabled="
                      attachFileToProcessInfo.description == null ||
                      attachFileToProcessInfo.description == ''
                    "
                    @click="uploadProcessAttachFile">
                    <template #content>
                      <div class="d-flex gap-2 align-items-center">
                        <span class="text-xSmall text-white"
                          >ویرایش و تایید اسناد</span
                        >
                        <CloudUploadIcon :fill="Colors.white" width="16" />
                      </div>
                    </template>
                  </KsButton>
                </div>
              </div>
            </transition-group>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2 mt-3">
          <KsButton
            :disabled="disableBtnUploadCondition"
            color="primary"
            theme="flat"
            title="افزودن سند"
            @click="addProcessAttachFileForm">
          </KsButton>
          <KsButton
            :disabled="
              disableBtnUploadCondition &&
              userProccesses[taskModal.madeIndex].assemblies[
                taskModal.assemblyIndex
              ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex].files
                .length == 0
            "
            color="warn"
            theme="flat"
            title="برگشت سند"
            @click="undoProcessAttachFileForm">
          </KsButton>
          <KsButton
            color="transparent"
            title="لغو"
            @click="taskModal.visible = false" />
        </div>
      </div>
      <div v-if="taskModal.mode === 'AcceptDocuments'">
        <div style="max-height: 82vh; overflow-y: auto">
          <div class="text-center pt-2">
            <span class="color-primary fw-bold text-large">
              تایید اسناد کنترل کیفی فرآیند
              {{ taskModal.modalTitle }}
            </span>
          </div>

          <div class="my-3" dir="rtl">
            <div class="text-center">
              <span class="text-small">
                از تایید اسناد کنترل کیفی این فرآیند مطمعن هستید؟
              </span>
            </div>

            <!--  ًٍEquipment Process Attach Files -->
            <!-- <transition-group
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
                v-for="(file, indexProcessFile) in userProccesses[
                  taskModal.assemblyIndex
                ].boms[taskModal.bomIndex].proccesses[taskModal.activeIndex]
                  .files"
                :key="indexProcessFile">
                <div class="d-flex flex-column gap-3 mt-3">
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title">:کد سند کنترل کیفی</span>
                    <span class="text-small">{{ file.code }}</span>
                  </div>
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title">:عنوان سند کنترل کیفی</span>
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
            </transition-group> -->
          </div>
        </div>

        <div class="d-flex justify-content-end gap-2 mt-3">
          <KsButton
            :loading="state.modalSubmitLoading"
            color="primary"
            title="تایید"
            @click="sendEngineerAcceptDocuments" />
          <KsButton
            color="transparent"
            title="لغو"
            @click="taskModal.visible = false" />
        </div>
      </div>
      <div v-if="taskModal.mode === 'FailureToSend'">
        <div style="max-height: 82vh; overflow-y: auto">
          <div class="text-center pt-2">
            <span class="color-primary fw-bold text-large">
              عدم تایید اسناد کنترل کیفی فرآیند
              {{ taskModal.modalTitle }}
            </span>
          </div>

          <div class="my-3" dir="rtl">
            <div class="text-center">
              <span class="text-small">
                از عدم تایید اسناد کنترل کیفی این فرآیند مطمعن هستید؟
              </span>
            </div>
          </div>
        </div>

        <div class="d-flex justify-content-end gap-2 mt-3">
          <KsButton
            :loading="state.modalSubmitLoading"
            color="primary"
            title="تایید"
            @click="sendEngineerFailureToSend" />
          <KsButton
            color="transparent"
            title="لغو"
            @click="taskModal.visible = false" />
        </div>
      </div>
      <div v-if="taskModal.mode === 'ClientApproved'">
        <div class="text-center pt-2">
          <span class="color-primary fw-bold text-large">
            تایید فرآیند
            {{ taskModal.modalTitle }}
            توسط مشتری
          </span>
        </div>

        <div class="text-center my-3" dir="rtl">
          <span class="text-small"> مشتری این فرآیند را تایید کرده است؟ </span>
        </div>

        <div class="d-flex justify-content-end gap-2 mt-3">
          <KsButton
            :loading="state.modalSubmitLoading"
            color="primary"
            title="تایید"
            @click="sendMasterApprovedRequest" />
          <KsButton
            color="transparent"
            title="لغو"
            @click="taskModal.visible = false" />
        </div>
      </div>
    </KsModal>

    <!--    DELETE MODAL   -->
    <DeleteModal
      v-model="deleteModal.visible"
      :button-loading="state.deleteBtnLoading !== null"
      @cancel="deleteModal.visible = false"
      @submit="removeProcessAttachedFileForm" />
  </div>
</template>

<style lang="scss" scoped>
.item-title {
  width: 180px;
  font-weight: bold;
}
td,
th {
  align-content: center;
}
</style>
