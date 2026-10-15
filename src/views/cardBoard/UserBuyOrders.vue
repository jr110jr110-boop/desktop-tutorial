<script lang="ts" setup>
import type { AxiosRequestConfig } from "axios";
import type PreviewTextRow from "@/types/PreviewTextRow";
import type _Process from "@/types/Process";
import type TableValue from "@/types/TableValue";
import type BuyOrder from "@/types/BuyOrder";
import type Provider from "@/types/Provider";
import type Inquiry from "@/types/Inquiry";
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import { getProviderVendorList } from "@/api/ProviderAPIs";
import { getCardBoardUserBuyOrders } from "@/api/CardBoardAPIs";
import {
  addInquiry,
  deleteInquiry,
  editInquiry,
  getInquiryList,
  setInquiryScore,
} from "@/api/InquiryAPIs";
import { addDocument, getDocument, getDocumentFileInfo } from "@/api/Document";

import {
  attachFileBuyOrder,
  buyOrderAskTempReceipt,
  buyOrderAssemblerAssembled,
  buyOrderConstructorConstructed,
  buyOrderPMOAcceptReject,
  buyOrderAcceptReject,
  buyOrderEngineeringRespondTempReceipt,
  buyOrderInquiriesAdded,
  buyOrderInquiriesScoreAdded,
  buyOrderPMOAcceptGivenToWareHouse,
  buyOrderQcRespondTempReceipt,
  buyOrderSetFinancialDocuments,
  buyOrderSetTempReceipt,
  buyOrderSetWinnerInquiry,
  buyOrderWarehouseFinalRespond,
  detachFileBuyOrder,
  getBuyOrderDetail,
} from "@/api/BuyOrderAPIs";
import { downloadFile } from "@/composables/downloadFile";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import { PREVIEW_MODAL_BUY_ORDER_MODEL } from "@/models";

import NoDataFound from "@/layouts/NoDataFound.vue";
import DeleteModal from "@/components/app/DeleteModal.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsModal from "@/components/common/KsModal.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsInputSeperate from "@/components/common/KsInputSeperate.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import ListLoader from "@/layouts/ListLoader.vue";
import KsTable from "@/components/common/KsTable.vue";
import PreviewModalTextRow from "@/components/app/_common/previewModal/PreviewModalTextRow.vue";
import KsPagination from "@/components/common/KsPagination.vue";
import BriefcaseIcon from "@/components/icons/BriefcaseIcon.vue";
import TasksIcon from "@/components/icons/TasksIcon.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import EyeIcon from "@/components/icons/EyeIcon.vue";
import KsAlert from "@/components/common/KsAlert.vue";
import KsAccordion from "@/components/common/KsAccordion.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import { Colors } from "@/assets/colors";
import KsUploadProgress from "@/components/common/KsUploadProgress.vue";
import KsFilePicker from "@/components/common/KsFilePicker.vue";
import { getMockFile } from "@/composables/getMockFile";
import { finalizeFileEquipmentProcess } from "@/api/EquipmentAPIs";
import { changeDocumentFile, changeDocumentInfo } from "@/api/DocumentAPIs";
import showWarnNotification from "@/composables/showWarnNotification";
import mime from "mime-types";
import KsTextArea from "@/components/common/KsTextArea.vue";
import type Assembler from "@/types/Assembler";
import type Constructor from "@/types/Constructor";
import { getMachineActive } from "@/api/MachineAPIs";
import type Machine from "@/types/Machine";
import type ProccessFile from "@/types/_File";
import type AcceptReject from "@/types/AcceptReject";
import EditIcon from "@/components/icons/EditIcon.vue";

type TaskModalMode =
  | "RegisterInquiry"
  | "SetInquiryScore"
  | "SelectWinner"
  | "SetFinancialDocuments"
  | "AskTempReceipt"
  | "SetTempReceipt"
  | "QCRespondTempReceipt"
  | "QCAttachFiles"
  | "EngineeringRespondTempReceipt"
  | "WarehouseFinalRespond"
  | "AssemblerAssembled"
  | "PMOAcceptGivenToWarehouse"
  | "ConstructionSubmit"
  | "PMOAcceptReject"
  | "AcceptReject";

const tableHeads: string[] = [
  "دستور کار",
  "فرآیندهای تامین، مونتاژ و ساخت",
  "کد ورود کالا",
  "کد خروج کالا",
  "وضعیت",
];

const tableValues: TableValue[] = [
  {
    propName: "workOrderTitle",
    type: "text",
    clickable: true,
  },
  {
    propName: "title",
    type: "text",
  },
  {
    propName: "enterCommodityCode",
    type: "text",
  },
  {
    propName: "exitCommodityCode",
    type: "text",
  },
  {
    propName: "statusDescription",
    type: "text",
  },
];

const userMode = localStorage.getItem("userRole").toLowerCase();
const addedFiles: Ref<FileList> = ref();
const addedLogSheetFiles: Ref<FileList> = ref();
const isFileAdded: Ref<boolean> = ref(false);
const isFileLogSheetAdded: Ref<boolean> = ref(false);
const logSheet: Ref<string> = ref("");
const machineList: Ref<Machine[]> = ref([]);

const financeTypeList = [
  {
    title: "چکی",
    value: "چکی",
  },
  {
    title: "نقدی",
    value: "نقدی",
  },
  {
    title: "صورت وضعیتی",
    value: "صورت وضعیتی",
  },
  {
    title: "چکی و نقدی",
    value: "چکی و نقدی",
  },
];

const pagination = ref({
  activePage: 1,
  totalPage: 1,
});

const loadings = ref({
  listLoading: true,
  modalSubmitLoading: false,
  modalUpdateBtnLoading: null,
  providerListLoading: true,
  inquiryListLoading: false,
  buyOrderFilesLoading: false,
  deleteBtnLoading: null,
  downloadBtnLoading: null,
  uploadBtnLoading: null,
  uploadingProgress: {
    activeIndex: null,
    progress: 0,
  },
});

const alerts = ref({
  hasUnRegisteredInquiries: true,
});

const docData: Ref<ProccessFile> = ref({
  description: "",
  documentId: "",
  id: 0,
  major: 0,
  minor: 0,
  processId: 0,
  regDate: "",
  regDateString: "",
  userDispalyName: "",
  userId: 0,
  version: "",
});

const userBuyOrders: Ref<BuyOrder[]> = ref([]);

const providerList: Ref<Provider[]> = ref([]);
const providerVendorList = ref([]);
const financeTypeValue = ref("");

const realBuyCount = ref(null);

const currBuyOrder: Ref<BuyOrder> = ref(null);

const assemblerInfo: Ref<Assembler> = ref({
  id: 0,
  timeSheet: "",
});

const constructorInfo: Ref<Constructor> = ref({
  id: 0,
  machineId: 0,
  constractorDescription: "",
  timeSheet: "",
});

const acceptRejectInfo: Ref<AcceptReject> = ref({
  id: 0,
});

const previewModal = ref({
  visible: false,
  data: null as PreviewTextRow[],
  processes: [] as _Process[],
  buyOrder: null as BuyOrder,
  activeIndex: null,
});

const taskModal = ref({
  visible: false,
  mode: null as TaskModalMode,
  modalTitle: null,
  activeIndex: null,
});

const deleteModal = ref({
  visible: false,
  activeId: null,
  mode: "",
  activeIndex: null,
});

const openDeleteModal = (activeId, mode, activeIndex) => {
  deleteModal.value = {
    activeId,
    mode,
    activeIndex,
    visible: !!activeId,
  };

  if (!activeId) {
    if (mode === "inquiry") removeInquiryForm();
    else removeBuyOrderAttachedFileForm();
  }
};

const onDeleteModalSubmit = () => {
  if (deleteModal.value.mode === "inquiry") removeInquiryForm();
  else removeBuyOrderAttachedFileForm();
};

const addNewInquiryForm = () => {
  userBuyOrders.value[taskModal.value.activeIndex].inquiries.push({
    id: null,
    count: null,
    price: null,
    technicalScore: null,
    buyOrderId: null,
    providerId: null,
    technicalDoc: null,
    financialDoc: null,
  });
};

const onPreviewModalRequest = (index: number) => {
  previewModal.value.activeIndex = index;
  previewModal.value.visible = true;
};

const showPreviewModal = async (buyOrder: BuyOrder, activeIndex) => {
  previewModal.value.data = [...PREVIEW_MODAL_BUY_ORDER_MODEL].map((item) => {
    let value = buyOrder[item.key];
    if (item.format === "price") value = value.toLocaleString("ar-SA");
    if (item.format === "percent") value = parseFloat(value).toFixed(2);

    return {
      title: item.title,
      value,
      prefix: item.prefix ?? null,
    };
  });
  previewModal.value.processes = buyOrder.processes;
  previewModal.value.activeIndex = activeIndex;
  previewModal.value.visible = true;

  const [data, error] = await fetchInquiryList(
    userBuyOrders.value[activeIndex].id
  );
  if (!error) addFetchedInquiriesToBuyOrder(data.data.data, activeIndex);

  previewModal.value.buyOrder = await getCurrentBuyOrder(
    buyOrder.workOrderId,
    buyOrder.id
  );
};

const addBuyOrderFile = () => {
  currBuyOrder.value.files.push({
    file: null,
    name: "",
    code: "",
    id: null,
    description: "",
    isFinal: false,
  });
};

async function downloadBuyOrderAttachFile(file, index: number) {
  loadings.value.downloadBtnLoading = index;
  await downloadFile(file, file.id);
  loadings.value.downloadBtnLoading = null;
}

const onBuyOrderFileChanged = (args) => {
  const { file, index } = args;
  if (file[0]) {
    currBuyOrder.value.files[index].file = file[0];
    currBuyOrder.value.files[index].mimeType =
      "." + mime.extension(file[0].type);
    if (!currBuyOrder.value.files[index].description)
      currBuyOrder.value.files[index].description = file[0].name;
    currBuyOrder.value.files[index].size = file[0].size.toString();
  }
};

const onFileTechnicalDocChanged = (file: FileList, inquiryIndex) => {
  uploadTechnicalDoc(inquiryIndex, file[0]);
};

async function uploadTechnicalDoc(inquiryIndex, file) {
  const formData = new FormData();
  formData.append("file", file);

  const [data, error] = await addDocument(
    formData,
    "workflow",
    "Technical",
    "workflow"
  );

  if (error) {
    showErrorNotification(error?.message);
  } else {
    const order = userBuyOrders.value[taskModal.value.activeIndex];
    if (order && order.inquiries && order.inquiries[inquiryIndex]) {
      order.inquiries[inquiryIndex].technicalDoc = data;
    } else {
      console.warn("Invalid order or inquiry index");
    }
  }
}

const onFileFinancialDocChanged = (file: FileList, inquiryIndex) => {
  uploadFinancialDoc(inquiryIndex, file[0]);
};

async function uploadFinancialDoc(inquiryIndex, file) {
  const formData = new FormData();
  formData.append("file", file);

  const [data, error] = await addDocument(
    formData,
    "workflow",
    "Financial",
    "workflow"
  );

  if (error) {
    showErrorNotification(error?.message);
  } else {
    const order = userBuyOrders.value[taskModal.value.activeIndex];
    if (order && order.inquiries && order.inquiries[inquiryIndex]) {
      order.inquiries[inquiryIndex].financialDoc = data;
    } else {
      console.warn("Invalid order or inquiry index");
    }
  }
}

const attachConstructorConstructedFile = (file: FileList) => {
  isFileAdded.value = true;
  addedFiles.value = file;
};

async function uploadAssemlerAssembledFile() {
  if (isFileAdded.value) {
    const formData = new FormData();
    formData.append("file", addedFiles.value[0]);

    const [data, error] = await addDocument(
      formData,
      "Assembler File",
      "file " + Number(userBuyOrders.value.length + 1),
      "0"
    );
    if (error) {
      showErrorNotification(error?.message);
    } else {
      assemblerInfo.value.timeSheet = data;
    }
  }
}

const attachLogSheetFile = (file: FileList) => {
  isFileLogSheetAdded.value = true;
  addedLogSheetFiles.value = file;
  uploadLogSheetFile();
};

async function uploadLogSheetFile() {
  if (isFileLogSheetAdded.value) {
    const formData = new FormData();
    formData.append("file", addedLogSheetFiles.value[0]);

    const [data, error] = await addDocument(
      formData,
      "logSheet",
      "file " + Number(userBuyOrders.value.length + 1),
      "0"
    );
    if (error) {
      showErrorNotification(error?.message);
    } else {
      logSheet.value = data;
    }
  }
}

async function uploadconstructorconstructedFile() {
  if (isFileAdded.value) {
    const formData = new FormData();
    formData.append("file", addedFiles.value[0]);

    const [data, error] = await addDocument(
      formData,
      "Constructor File",
      "file " + Number(userBuyOrders.value.length + 1),
      "0"
    );
    if (error) {
      showErrorNotification(error?.message);
    } else {
      constructorInfo.value.timeSheet = data;
    }
  }
}

const removeBuyOrderAttachedFileForm = async () => {
  const { activeId, activeIndex } = deleteModal.value;
  if (activeId) {
    loadings.value.deleteBtnLoading = activeIndex;
    const [data, error] = await detachFileBuyOrder(
      currBuyOrder.value.id,
      activeId
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      currBuyOrder.value.files.splice(activeIndex, 1);
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }
    loadings.value.deleteBtnLoading = null;
  } else currBuyOrder.value.files.splice(activeIndex, 1);
  deleteModal.value.visible = false;
};

const showTaskModal = async (activeIndex, modalTitle, mode: TaskModalMode) => {
  taskModal.value.activeIndex = activeIndex;
  taskModal.value.modalTitle = modalTitle;
  taskModal.value.mode = mode;
  taskModal.value.visible = true;

  if (
    [
      "RegisterInquiry",
      "SelectWinner",
      "SetInquiryScore",
      "SetFinancialDocuments",
    ].includes(mode)
  ) {
    loadings.value.inquiryListLoading = true;
    const [data, error] = await fetchInquiryList(
      userBuyOrders.value[activeIndex].id
    );
    if (data?.success)
      addFetchedInquiriesToBuyOrder(data.data.data, activeIndex);
    loadings.value.inquiryListLoading = false;
  }

  if (mode === "QCAttachFiles") {
    currBuyOrder.value = await getCurrentBuyOrder(
      userBuyOrders.value[activeIndex].workOrderId,
      userBuyOrders.value[activeIndex].id
    );
  }
};

const getCurrentBuyOrder = async (workOrderId, buyOrderId) => {
  loadings.value.buyOrderFilesLoading = true;
  const [data, error] = await getBuyOrderDetail(workOrderId, buyOrderId);
  loadings.value.buyOrderFilesLoading = false;
  if (data?.success) return data.data;
  return null;
};

const addFetchedInquiriesToBuyOrder = (
  fetchedInquiryList: Inquiry[],
  buyOrderIndex
) => {
  for (let i = 0; i < fetchedInquiryList.length; i++) {
    let exists = userBuyOrders.value[buyOrderIndex].inquiries.some(
      (inquiry) => inquiry.id === fetchedInquiryList[i].id
    );
    if (!exists)
      userBuyOrders.value[buyOrderIndex].inquiries.push(fetchedInquiryList[i]);
  }
};

const removeInquiryForm = async () => {
  if (deleteModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteModal.value.activeIndex;
    const [data, error] = await deleteInquiry(deleteModal.value.activeId);
    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      showSuccessNotification("استعلام با موفقیت حذف شد");
      userBuyOrders.value[taskModal.value.activeIndex].inquiries.splice(
        deleteModal.value.activeIndex,
        1
      );
    }
    loadings.value.deleteBtnLoading = null;
  } else
    userBuyOrders.value[taskModal.value.activeIndex].inquiries.splice(
      deleteModal.value.activeIndex,
      1
    );
  deleteModal.value.visible = false;
};

// async function downloadProcessAttachFile(file, index: number) {
//   loadings.value.downloadBtnLoading = index;
//   await downloadFile(file, file.id);
//   loadings.value.downloadBtnLoading = null;
// }

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
    showSuccessNotification("سند مهندسی با موفقیت دانلود شد");
  } else {
    showErrorNotification("سند مهندسی با خطا مواجه شد!");
  }
}

async function fetchDocumentFileInfo(fileId) {
  loadings.value.modalUpdateBtnLoading = true;
  const [data, error] = await getDocumentFileInfo(fileId);
  if (!error) {
    docData.value = data.data;
    fetchDocumentById(fileId);
    if (data.success == false) {
      showErrorNotification(data.message);
    }
  } else {
    showErrorNotification("دانلود با خطا مواجه شد!");
  }
  loadings.value.modalUpdateBtnLoading = false;
}

async function updateInquiryRequest(inquiryIndex, mode: "add" | "edit") {
  const currInquiry =
    userBuyOrders.value[taskModal.value.activeIndex].inquiries[inquiryIndex];
  if (!currInquiry.price || !currInquiry.count || !currInquiry.providerId) {
    showWarnNotification("لطفا تمامی فیلدها را پر کنید");
    return;
  } else if (
    userBuyOrders.value[taskModal.value.activeIndex].inquiries[inquiryIndex]
      .financialDoc == null ||
    userBuyOrders.value[taskModal.value.activeIndex].inquiries[inquiryIndex]
      .financialDoc == "00000000-0000-0000-0000-000000000000" ||
    userBuyOrders.value[taskModal.value.activeIndex].inquiries[inquiryIndex]
      .technicalDoc == null ||
    userBuyOrders.value[taskModal.value.activeIndex].inquiries[inquiryIndex]
      .technicalDoc == "00000000-0000-0000-0000-000000000000"
  ) {
    showWarnNotification("لطفا فایل سند مالی و فنی را انتخاب کنید");
    return;
  }

  loadings.value.modalUpdateBtnLoading = inquiryIndex;
  userBuyOrders.value[taskModal.value.activeIndex].inquiries[
    inquiryIndex
  ].buyOrderId = userBuyOrders.value[taskModal.value.activeIndex].id;
  const [data, error] =
    mode === "add"
      ? await addInquiry(
          userBuyOrders.value[taskModal.value.activeIndex].inquiries[
            inquiryIndex
          ]
        )
      : await editInquiry(
          userBuyOrders.value[taskModal.value.activeIndex].inquiries[
            inquiryIndex
          ]
        );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    showSuccessNotification(
      "استعلام با موفقیت " + (mode === "edit" ? "ویرایش شد" : "ثبت شد")
    );
    if (mode === "add")
      userBuyOrders.value[taskModal.value.activeIndex].inquiries[
        inquiryIndex
      ].id = data.data.id;
  }
  loadings.value.modalUpdateBtnLoading = null;
}

async function setInquiryScoreRequest(inquiryIndex) {
  loadings.value.modalUpdateBtnLoading = inquiryIndex;
  userBuyOrders.value[taskModal.value.activeIndex].inquiries[
    inquiryIndex
  ].buyOrderId = userBuyOrders.value[taskModal.value.activeIndex].id;
  const [data, error] = await setInquiryScore(
    userBuyOrders.value[taskModal.value.activeIndex].inquiries[inquiryIndex].id,
    userBuyOrders.value[taskModal.value.activeIndex].inquiries[inquiryIndex]
      .technicalScore
  );
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else showSuccessNotification("امتیاز با موفقیت به استعلام افزوده شد");
  loadings.value.modalUpdateBtnLoading = null;
}

async function requestBuyOrderInquiriesAdded() {
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderInquiriesAdded(
    userBuyOrders.value[taskModal.value.activeIndex].id,
    userBuyOrders.value[taskModal.value.activeIndex].financeType
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("استعلام ها به واخد کنترل کیفی ارسال شدند");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderInquiriesScoreAdded() {
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderInquiriesScoreAdded(
    userBuyOrders.value[taskModal.value.activeIndex].id
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("استعلام ها به واخد PMO ارسال شدند");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderSetWinnerInquiry(inquiryIndex) {
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderSetWinnerInquiry(
    userBuyOrders.value[taskModal.value.activeIndex].id,
    inquiryIndex
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("استعلام برنده با موفقیت انتخاب شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderAskTempReceipt() {
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderAskTempReceipt(
    userBuyOrders.value[taskModal.value.activeIndex].id,
    realBuyCount.value
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderSetTempReceipt() {
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderSetTempReceipt(
    userBuyOrders.value[taskModal.value.activeIndex].id,
    logSheet.value
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function uploadBuyOrderAttachFile(index) {
  loadings.value.uploadBtnLoading = true;
  loadings.value.uploadingProgress.activeIndex = index;
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
  formData.append(
    "file",
    currBuyOrder.value.files[index].file ?? getMockFile()
  );

  const [data, error] = currBuyOrder.value.files[index].id
    ? await changeDocumentInfo(currBuyOrder.value.files[index])
    : await attachFileBuyOrder(
        currBuyOrder.value.id,
        currBuyOrder.value.files[index].description,
        currBuyOrder.value.files[index].name,
        currBuyOrder.value.files[index].code,
        formData,
        axiosConfig
      );
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else if (!currBuyOrder.value.files[index].id) {
    currBuyOrder.value.files[index].id = data.data;
    showSuccessNotification("سند با موفقیت ثبت شد");
  }

  if (currBuyOrder.value.files[index].file) {
    const [data, error] = await changeDocumentFile(
      currBuyOrder.value.files[index].id,
      formData
    );
    if (data?.success) {
      const [data2, error2] = await finalizeFileEquipmentProcess(
        currBuyOrder.value.id,
        currBuyOrder.value.files[index].id
      );
      if (data2?.success) currBuyOrder.value.files[index].isFinal = true;
      showSuccessNotification("فایل پیوست با موفقیت بروزرسانی شد");
    }
  }

  loadings.value.uploadingProgress.progress = 0;
  loadings.value.uploadingProgress.activeIndex = null;
  loadings.value.uploadBtnLoading = null;
}

const validateForm = () => {
  let isValid = true;

  if (userBuyOrders.value[taskModal.value.activeIndex].files === null)
    isValid = false;

  if (isValid) return true;
  else {
    showWarnNotification("آپلود کردن فایل الزامی است");
    return false;
  }
};

async function requestBuyOrderQcRespondTempReceipt(isAccepted: boolean) {
  if (!validateForm()) return;

  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderQcRespondTempReceipt(
    userBuyOrders.value[taskModal.value.activeIndex].id,
    isAccepted
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderEngineeringRespondTempReceipt(
  status: "reject-give-to-salvage" | "accepted" | "says-needs-fixes"
) {
  if (!validateForm()) return;
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderEngineeringRespondTempReceipt(
    userBuyOrders.value[taskModal.value.activeIndex].id,
    status
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderWarehouseFinalRespond() {
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderWarehouseFinalRespond(
    userBuyOrders.value[taskModal.value.activeIndex].id
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderAssemblerAssembled() {
  loadings.value.modalSubmitLoading = true;
  await uploadAssemlerAssembledFile();

  assemblerInfo.value.id = userBuyOrders.value[taskModal.value.activeIndex].id;
  const [data, error] = await buyOrderAssemblerAssembled(assemblerInfo.value);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderConstructorConstructed() {
  loadings.value.modalSubmitLoading = true;
  await uploadconstructorconstructedFile();

  constructorInfo.value.id =
    userBuyOrders.value[taskModal.value.activeIndex].id;
  constructorInfo.value.machineId =
    machineList.value[taskModal.value.activeIndex].id;
  const [data, error] = await buyOrderConstructorConstructed(
    constructorInfo.value
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderPMOAcceptReject() {
  loadings.value.modalSubmitLoading = true;
  acceptRejectInfo.value.id =
    userBuyOrders.value[taskModal.value.activeIndex].id;

  const [data, error] = await buyOrderPMOAcceptReject(acceptRejectInfo.value);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderAcceptReject() {
  loadings.value.modalSubmitLoading = true;
  acceptRejectInfo.value.id =
    userBuyOrders.value[taskModal.value.activeIndex].id;

  const [data, error] = await buyOrderAcceptReject(acceptRejectInfo.value);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyorderPMOAcceptGivenToWareHouse() {
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderPMOAcceptGivenToWareHouse(
    userBuyOrders.value[taskModal.value.activeIndex].id
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function requestBuyOrderSetFinancialDocuments() {
  loadings.value.modalSubmitLoading = true;
  const [data, error] = await buyOrderSetFinancialDocuments(
    userBuyOrders.value[taskModal.value.activeIndex].id,
    userBuyOrders.value[taskModal.value.activeIndex].optionalFinanceDocDate,
    userBuyOrders.value[taskModal.value.activeIndex].financeType,
    userBuyOrders.value[taskModal.value.activeIndex].financeNumber,
    userBuyOrders.value[taskModal.value.activeIndex].financePrice
  );

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    userBuyOrders.value.splice(taskModal.value.activeIndex, 1);
    showSuccessNotification("عملیات با موفقیت انجام شد");
  }

  taskModal.value.visible = false;
  loadings.value.modalSubmitLoading = false;
}

async function fetchInquiryList(buyOrderId) {
  loadings.value.inquiryListLoading = true;
  const [data, error] = await getInquiryList(buyOrderId);
  if (!data?.success) showErrorNotification(data?.message || error.message);
  loadings.value.inquiryListLoading = false;
  return [data, error];
}

// OnMounted Functions
const onFetchedUserBuyOrders = (fetchedData: BuyOrder[]) => {
  userBuyOrders.value = fetchedData;
  userBuyOrders.value.forEach((userBuyOrder) => {
    userBuyOrder.inquiries = [];
  });
};

async function fetchUserBuyOrders() {
  loadings.value.listLoading = true;
  const [data, error] = await getCardBoardUserBuyOrders(
    25,
    pagination.value.activePage
  );
  if (!error) await onFetchedUserBuyOrders(data.data.data);
  else showErrorNotification(data?.message || error.message);
  loadings.value.listLoading = false;
}

async function fetchActiveMachine() {
  loadings.value.listLoading = true;
  const [data, error] = await getMachineActive();
  if (!error) machineList.value = data.data;
  else showErrorNotification(data?.message || error.message);
  loadings.value.listLoading = false;
}

async function fetchProviderVendorList() {
  loadings.value.providerListLoading = true;
  const [data, error] = await getProviderVendorList();
  if (!error) providerVendorList.value = data.data;
  else showErrorNotification(data?.message || error.message);
  loadings.value.providerListLoading = false;
}

async function fetchProviderList() {
  loadings.value.providerListLoading = true;
  const [data, error] = await getProviderVendorList();
  if (!error) providerList.value = data.data;
  else showErrorNotification(data?.message || error.message);
  loadings.value.providerListLoading = false;
}

onMounted(async () => {
  await fetchUserBuyOrders();
  await fetchActiveMachine();
  await fetchProviderVendorList();
  await fetchProviderList();
  await fetchProviderVendorList();
});
</script>

<template>
  <div class="background-color min-vh-100 p-3 shadow-sm rounded-3 text-end">
    <div
      class="d-flex flex-row-reverse justify-content-between align-items-center">
      <div class="list-title">
        <span class="fw-bold text-large text-color"
          >فرآیندهای تامین، مونتاژ و ساخت شما</span
        >
        <BriefcaseIcon :fill="Colors.black" class="ms-3" width="24" />
      </div>
    </div>
    <hr class="opacity-30 my-4" />

    <transition-group mode="out-in" name="fade">
      <div
        v-if="!loadings.listLoading && userBuyOrders.length"
        key="list"
        dir="rtl">
        <KsTable
          :has-delete-btn="false"
          :has-edit-btn="false"
          :t-data="userBuyOrders"
          :t-heads="tableHeads"
          :t-values="tableValues"
          has-custom-action-btn>
          <template #custom-action-btn="{ rowData, rowIndex }">
            <li
              v-if="
                rowData.status === 3 ||
                rowData.status === 7 ||
                rowData.status === 9
              "
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                $router.push({
                  name: 'BuyOrderUpdate',
                  params: { id: userBuyOrders[rowIndex].id },
                  query: {
                    workOrderId: userBuyOrders[rowIndex].workOrderId,
                    workOrderTitle: userBuyOrders[rowIndex].workOrderTitle,
                  },
                })
              ">
              <EditIcon :fill="Colors.black" width="16" />
              <span class="text-small">ویرایش</span>
            </li>
            <li
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="showPreviewModal(rowData, rowIndex)">
              <EyeIcon :fill="Colors.black" width="16" />
              <span class="text-small">مشاهده اطلاعات</span>
            </li>
            <li
              v-if="rowData.status === 0"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'RegisterInquiry'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">ثبت استعلام</span>
            </li>
            <li
              v-else-if="rowData.status === 1 && userMode === 'engineering'"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'SetInquiryScore'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">ثبت امتیاز</span>
            </li>
            <li
              v-else-if="rowData.status === 3"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(rowIndex, rowData.workOrderTitle, 'SelectWinner')
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">انتخاب برنده</span>
            </li>
            <li
              v-else-if="rowData.status === 4"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'SetFinancialDocuments'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">ثبت اسناد مالی</span>
            </li>
            <li
              v-else-if="rowData.status === 5 || rowData.status === 20"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'AskTempReceipt'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">درخواست رسید</span>
            </li>

            <li
              v-else-if="
                rowData.status === 10 ||
                rowData.status === 12 ||
                rowData.status === 15
              "
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'SetTempReceipt'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">ثبت رسید موقت</span>
            </li>
            <template v-else-if="rowData.status === 6">
              <li
                class="d-flex flex-row-reverse align-items-center gap-3"
                @click="
                  showTaskModal(
                    rowIndex,
                    rowData.workOrderTitle,
                    'QCAttachFiles'
                  )
                ">
                <TasksIcon :fill="Colors.black" width="16" />
                <span class="text-small">ثبت اسناد کنترل کیفی</span>
              </li>
              <li
                class="d-flex flex-row-reverse align-items-center gap-3"
                @click="
                  showTaskModal(
                    rowIndex,
                    rowData.workOrderTitle,
                    'QCRespondTempReceipt'
                  )
                ">
                <TasksIcon :fill="Colors.black" width="16" />
                <span class="text-small">بررسی رسید موقت</span>
              </li>
            </template>
            <li
              v-else-if="rowData.status === 99 && userMode === 'warehouse'"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'WarehouseFinalRespond'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">تایید نهایی</span>
            </li>
            <li
              v-else-if="
                (rowData.status === 7 || rowData.status === 9) &&
                userMode === 'pmo'
              "
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'PMOAcceptGivenToWarehouse'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">تایید ارسال به انبار</span>
            </li>
            <li
              v-else-if="rowData.status === 8"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'EngineeringRespondTempReceipt'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">بررسی اختلاف</span>
            </li>
            <li
              v-else-if="rowData.status === 11 || rowData.status === 13"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'AssemblerAssembled'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">تایید مونتاژ</span>
            </li>
            <li
              v-else-if="rowData.status === 14 || rowData.status === 16"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'ConstructionSubmit'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">ثبت تولید</span>
            </li>
            <li
              v-else-if="rowData.status === 90"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(
                  rowIndex,
                  rowData.workOrderTitle,
                  'PMOAcceptReject'
                )
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">تایید ارسال به انبار ضایعات</span>
            </li>
            <li
              v-else-if="rowData.status === 91"
              class="d-flex flex-row-reverse align-items-center gap-3"
              @click="
                showTaskModal(rowIndex, rowData.workOrderTitle, 'AcceptReject')
              ">
              <TasksIcon :fill="Colors.black" width="16" />
              <span class="text-small">ارسال به انبار ضایعات</span>
            </li>
            <li v-else class="text-center text-xSmall opacity-75">
              عملیاتی وجود ندارد
            </li>
          </template>
        </KsTable>
      </div>

      <!-- ListLoader -->
      <div key="KsSkeletonLoader">
        <ListLoader :active="loadings.listLoading" />
      </div>

      <!--    PAGINATION   -->
      <KsPagination
        v-if="userBuyOrders.length"
        key="KsPagination"
        v-model="pagination.activePage"
        :length="pagination.totalPage"
        class="mt-3"
        @onChange="fetchUserBuyOrders" />

      <!--    NO DATA FOUND   -->
      <div key="NoDataComponent">
        <NoDataFound v-if="!loadings.listLoading && !userBuyOrders.length" />
      </div>
    </transition-group>

    <!--   TaskModal   -->
    <KsModal v-model="taskModal.visible" :order="0" blur>
      <div
        v-if="taskModal.mode === 'RegisterInquiry'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div
          class="p-3"
          style="height: 82vh; overflow-y: auto; overflow-x: hidden">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              استعلام های
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="d-flex flex-column gap-3 my-3 flex-1">
            <transition-group
              class="d-flex flex-column"
              dir="ltr"
              mode="out-in"
              name="fade"
              tag="div">
              <hr key="hr" />
              <div
                v-if="
                  !loadings.inquiryListLoading &&
                  !userBuyOrders[taskModal.activeIndex].inquiries.length
                "
                key="noData"
                class="mt-3 text-center">
                <span class="text-small"
                  >استعلامی برای این فرآیندهای تامین، مونتاژ و ساخت ثبت نشده
                  است</span
                >
              </div>
              <div
                v-if="
                  !loadings.inquiryListLoading &&
                  userBuyOrders[taskModal.activeIndex].inquiries.some(
                    (inQ) => !inQ.id
                  )
                "
                key="alert"
                class="mb-4">
                <KsAlert
                  v-model="alerts.hasUnRegisteredInquiries"
                  :closeable="false"
                  color="danger"
                  theme="flat">
                  <template #title>
                    <span class="fw-bold text-small color-danger">
                      استعلام ثبت نشده وجود دارد
                    </span>
                  </template>
                </KsAlert>
              </div>

              <div
                v-if="loadings.inquiryListLoading"
                key="loader"
                class="py-5 w-100 position-relative">
                <div class="custom-loader center" />
              </div>
              <div v-if="!loadings.inquiryListLoading">
                <div
                  v-for="(inquiry, index) in userBuyOrders[
                    taskModal.activeIndex
                  ].inquiries"
                  :key="index">
                  <div class="d-flex flex-row-reverse align-items-center gap-2">
                    <KsButton
                      :loading="loadings.deleteBtnLoading === index"
                      color="warn"
                      fixed-width-height="42"
                      theme="flat"
                      tooltip="حذف"
                      @click="openDeleteModal(inquiry.id, 'inquiry', index)">
                      <template #content>
                        <TimesIcon :fill="Colors.warn" width="18" />
                      </template>
                    </KsButton>
                    <KsSelect
                      v-model="inquiry.providerId"
                      :options="providerVendorList"
                      class="flex-1"
                      label-prop-name="name"
                      value-prop-name="id"
                      placeholder="تامین کننده را وارد کنید"
                      simple-placeholder />
                  </div>
                  <div
                    class="d-flex flex-row-reverse align-items-center gap-2 mt-3">
                    <KsInputSeperate
                      v-model="inquiry.price"
                      append-text="تومان"
                      placeholder="قیمت کل"
                      simple-placeholder />
                    <KsInput
                      v-model="inquiry.count"
                      append-text="عدد"
                      class="flex-1"
                      input-type="number"
                      placeholder="تعداد خرید"
                      simple-placeholder />
                  </div>
                  <div
                    class="d-flex flex-row-reverse align-items-center gap-2 mt-3">
                    <KsFilePicker
                      :has-file="!!inquiry.technicalDoc"
                      :height="42"
                      class="flex-fill"
                      icon-size="20"
                      @fileChange="
                        (val) => onFileTechnicalDocChanged(val, index)
                      " />
                    <KsFilePicker
                      :has-file="!!inquiry.financialDoc"
                      :height="42"
                      class="flex-fill"
                      icon-size="20"
                      @fileChange="
                        (val) => onFileFinancialDocChanged(val, index)
                      " />
                  </div>
                  <div class="mt-3">
                    <KsButton
                      v-if="inquiry.id"
                      :loading="loadings.modalUpdateBtnLoading === index"
                      color="dark"
                      theme="flat"
                      title="ویرایش استعلام"
                      @click="updateInquiryRequest(index, 'edit')" />
                    <KsButton
                      v-else
                      :loading="loadings.modalUpdateBtnLoading === index"
                      color="primary"
                      title="ثبت استعلام"
                      @click="updateInquiryRequest(index, 'add')" />
                  </div>
                  <hr
                    v-if="
                      index !==
                      userBuyOrders[taskModal.activeIndex].inquiries.length - 1
                    "
                    class="w-100 my-4" />
                </div>
              </div>
              <div key="addButton" class="mt-5">
                <KsButton
                  color="primary"
                  theme="flat"
                  tooltip="افزودن فرم استعلام"
                  @click="addNewInquiryForm">
                  <template #content>
                    <div class="py-1">
                      <PlusIcon :fill="Colors.primary" width="16" />
                    </div>
                  </template>
                </KsButton>
              </div>
              <div class="d-flex align-items-center gap-2">
                <KsSelect
                  v-model="userBuyOrders[taskModal.activeIndex].financeType"
                  :options="financeTypeList"
                  placeholder="نوع مالی"
                  class="w-100 mt-5" />
              </div>
            </transition-group>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2 mt-4">
          <KsButton
            :disabled="!userBuyOrders[taskModal.activeIndex].inquiries.length"
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="ثبت نهایی استعلام ها"
            @click="requestBuyOrderInquiriesAdded" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="
          taskModal.mode === 'SetInquiryScore' ||
          taskModal.mode === 'SelectWinner'
        "
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div
          class="p-3"
          style="height: 82vh; overflow-y: auto; overflow-x: hidden">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              {{
                taskModal.mode === "SetInquiryScore"
                  ? "ثبت امتیاز استعلام های "
                  : "انتخاب استعلام برنده "
              }}
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="d-flex flex-column gap-3 my-3 flex-1">
            <transition-group
              class="d-flex flex-column"
              dir="ltr"
              mode="out-in"
              name="fade"
              tag="div">
              <hr key="hr" />
              <div
                v-if="
                  !loadings.inquiryListLoading &&
                  !userBuyOrders[taskModal.activeIndex].inquiries.length
                "
                key="noData"
                class="mt-3 text-center">
                <span class="text-small"
                  >استعلامی برای این فرآیندهای تامین، مونتاژ و ساخت ثبت نشده
                  است</span
                >
              </div>
              <div
                v-if="
                  !loadings.inquiryListLoading &&
                  userBuyOrders[taskModal.activeIndex].inquiries.some(
                    (inQ) => !inQ.id
                  )
                "
                key="alert"
                class="mb-4">
                <KsAlert
                  v-model="alerts.hasUnRegisteredInquiries"
                  :closeable="false"
                  color="danger"
                  theme="flat">
                  <template #title>
                    <span class="fw-bold text-small color-danger">
                      استعلام بدون امتیاز وجود دارد
                    </span>
                  </template>
                </KsAlert>
              </div>

              <div
                v-if="loadings.inquiryListLoading"
                key="loader"
                class="py-5 w-100 position-relative">
                <div class="custom-loader center" />
              </div>
              <div v-if="!loadings.inquiryListLoading">
                <div
                  v-for="(inquiry, index) in userBuyOrders[
                    taskModal.activeIndex
                  ].inquiries"
                  :key="index"
                  class="d-flex flex-column gap-3 w-100">
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title">:قیمت</span>
                    <span class="text-small text-end" dir="rtl">
                      <strong class="ms-1">
                        {{ inquiry.price.toLocaleString("ar-SA") }}
                      </strong>
                      <small>تومان</small>
                    </span>
                  </div>
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title">:تعداد</span>
                    <span class="text-small text-end" dir="rtl">
                      {{ inquiry.count }}
                      <small>عدد</small>
                    </span>
                  </div>
                  <div
                    v-if="taskModal.mode === 'SelectWinner'"
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title">:امتیاز</span>
                    <KsButton color="primary" custom-padding="0">
                      <template #content>
                        <div style="padding: 4px 8px 1px 8px">
                          <span class="text-small text-white fw-bold">{{
                            inquiry.technicalScore
                          }}</span>
                        </div>
                      </template>
                    </KsButton>
                  </div>
                  <div
                    class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                    <span class="text-small item-title">:تامین کننده</span>
                    <span class="text-small text-end" dir="rtl">
                      {{ inquiry.provider.name }}
                    </span>
                  </div>
                  <KsAccordion btn-classes="p-2" class="accordion-btn">
                    <template #header>
                      <span class="text-small">اطلاعات تماس</span>
                    </template>
                    <template #content>
                      <div class="d-flex flex-column gap-1 p-2">
                        <div
                          v-for="(providerContact, index2) in inquiry.provider
                            .providerContacts"
                          :key="index2"
                          class="d-flex flex-column align-items-end gap-2">
                          <div
                            class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                            <span class="text-small item-title"
                              >:{{ providerContact.typeDescription }}</span
                            >
                            <span class="text-small">
                              {{ providerContact.data }}
                            </span>
                          </div>
                          <div
                            class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                            <span class="text-small item-title">:نام</span>
                            <span class="text-small" dir="rtl">
                              {{ providerContact.name }}
                            </span>
                          </div>
                          <span class="flex-1 text-small">:توضیحات</span>
                          <span class="text-small" dir="rtl">
                            {{ providerContact.description }}
                          </span>
                          <hr
                            v-if="
                              index !==
                              inquiry.provider.providerContacts.length - 1
                            "
                            class="w-100" />
                        </div>
                      </div>
                    </template>
                  </KsAccordion>
                  <KsInput
                    v-if="taskModal.mode === 'SetInquiryScore'"
                    v-model="inquiry.technicalScore"
                    input-type="number"
                    placeholder="امتیاز استعلام"
                    simple-placeholder />
                  <KsButton
                    v-if="taskModal.mode === 'SetInquiryScore'"
                    :loading="loadings.modalUpdateBtnLoading === index"
                    color="primary"
                    title="بروزرسانی امتیاز"
                    @click="setInquiryScoreRequest(index)" />
                  <KsButton
                    v-if="taskModal.mode === 'SelectWinner'"
                    :loading="loadings.modalSubmitLoading"
                    color="secondary"
                    title="انتخاب به عنوان برنده"
                    @click="requestBuyOrderSetWinnerInquiry(inquiry.id)" />
                  <hr
                    v-if="
                      index !==
                      userBuyOrders[taskModal.activeIndex].inquiries.length - 1
                    "
                    class="w-100" />
                </div>
              </div>
            </transition-group>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2 mt-4">
          <KsButton
            v-if="taskModal.mode === 'SetInquiryScore'"
            :disabled="!userBuyOrders[taskModal.activeIndex].inquiries.length"
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="ثبت و تایید نهایی"
            @click="requestBuyOrderInquiriesScoreAdded" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'SetFinancialDocuments'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div
          class="p-3"
          style="height: 82vh; overflow-y: auto; overflow-x: hidden">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              ثبت اطلاعات مالی برای
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="d-flex flex-column gap-3 my-3 flex-1">
            <transition-group
              class="d-flex flex-column"
              dir="ltr"
              mode="out-in"
              name="fade"
              tag="div">
              <hr key="hr" />
              <div
                v-if="loadings.inquiryListLoading"
                key="loader"
                class="py-5 w-100 position-relative">
                <div class="custom-loader center" />
              </div>
              <div v-if="!loadings.inquiryListLoading">
                <div
                  v-for="(inquiry, index) in userBuyOrders[
                    taskModal.activeIndex
                  ].inquiries"
                  :key="index"
                  class="w-100">
                  <div
                    v-if="inquiry.isAccept"
                    class="d-flex flex-column gap-3 w-100">
                    <div
                      class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                      <span class="text-small item-title">:قیمت</span>
                      <span class="text-small text-end" dir="rtl">
                        <strong class="ms-1">
                          {{ inquiry.price.toLocaleString("ar-SA") }}
                        </strong>
                        <small>تومان</small>
                      </span>
                    </div>
                    <div
                      class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                      <span class="text-small item-title">:تعداد</span>
                      <span class="text-small text-end" dir="rtl">
                        {{ inquiry.count }}
                        <small>عدد</small>
                      </span>
                    </div>
                    <div
                      class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                      <span class="text-small item-title">:امتیاز</span>
                      <KsButton color="primary" custom-padding="0">
                        <template #content>
                          <div style="padding: 4px 8px 1px 8px">
                            <span class="text-small text-white fw-bold">{{
                              inquiry.technicalScore
                            }}</span>
                          </div>
                        </template>
                      </KsButton>
                    </div>
                    <div
                      class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                      <span class="text-small item-title">:تامین کننده</span>
                      <span class="text-small text-end" dir="rtl">
                        {{ inquiry.provider.name }}
                        ({{ inquiry.provider.listName }})
                      </span>
                    </div>
                    <KsAccordion btn-classes="p-2" class="accordion-btn">
                      <template #header>
                        <span class="text-small">اطلاعات تماس</span>
                      </template>
                      <template #content>
                        <div class="d-flex flex-column gap-1 p-2">
                          <div
                            v-for="(providerContact, index2) in inquiry.provider
                              .providerContacts"
                            :key="index2"
                            class="d-flex flex-column align-items-end gap-2">
                            <div
                              class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                              <span class="text-small item-title"
                                >:{{ providerContact.typeDescription }}</span
                              >
                              <span class="text-small">
                                {{ providerContact.data }}
                              </span>
                            </div>
                            <div
                              class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                              <span class="text-small item-title">:نام</span>
                              <span class="text-small" dir="rtl">
                                {{ providerContact.name }}
                              </span>
                            </div>
                            <span class="flex-1 text-small">:توضیحات</span>
                            <span class="text-small" dir="rtl">
                              {{ providerContact.description }}
                            </span>
                            <hr
                              v-if="
                                index !==
                                inquiry.provider.providerContacts.length - 1
                              "
                              class="w-100" />
                          </div>
                        </div>
                      </template>
                    </KsAccordion>
                  </div>
                </div>
              </div>
            </transition-group>

            <span class="text-small fw-bold mt-3" dir="ltr">
              :اطلاعات مالی این فرآیندهای تامین، مونتاژ و ساخت را وارد
              کنید</span
            >
            <KsDatePicker
              v-model="
                userBuyOrders[taskModal.activeIndex].optionalFinanceDocDate
              "
              element-tag="optionalFinanceDocDate"
              placeholder="تاریخ سند مالی اختیاری"
              simple-placeholder />
            <KsSelect
              v-model="userBuyOrders[taskModal.activeIndex].financeType"
              :options="financeTypeList"
              :disabled="true"
              placeholder="نوع مالی"
              simple-placeholder />
            <KsInput
              v-model="userBuyOrders[taskModal.activeIndex].financeNumber"
              input-type="number"
              placeholder="شماره مالی"
              simple-placeholder />
            <KsInputSeperate
              v-model="userBuyOrders[taskModal.activeIndex].financePrice"
              append-text="تومان"
              placeholder="هزینه مالی"
              simple-placeholder />
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2 mt-4">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="ثبت و تایید نهایی"
            @click="requestBuyOrderSetFinancialDocuments" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'AskTempReceipt'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              درخواست رسید موقت برای
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="d-flex flex-column gap-3 py-4">
            <span class="text-small">
              لطفا تعداد خرید برای این فرآیندهای تامین، مونتاژ و ساخت را وارد
              کنید:
            </span>
            <KsInput
              v-model="realBuyCount"
              input-type="number"
              placeholder="تعداد خرید"
              simple-placeholder />
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :disabled="realBuyCount === null"
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="درخواست رسید موقت"
            @click="requestBuyOrderAskTempReceipt" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'SetTempReceipt'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              ثبت رسید موقت برای
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              از ثبت رسید موقت برای این فرآیندهای تامین، مونتاژ و ساخت مطمعن
              هستید؟
            </span>
          </div>
          <KsFilePicker
            :height="42"
            class="flex-fill"
            icon-size="20"
            @fileChange="(val) => attachLogSheetFile(val)" />
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید ثبت رسید موقت"
            @click="requestBuyOrderSetTempReceipt" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'QCAttachFiles'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div
          class="p-3"
          style="height: 82vh; overflow-y: auto; overflow-x: hidden">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              ثبت اسناد کنترل کیفی برای
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="d-flex flex-column gap-3 my-3 flex-1">
            <transition-group
              class="d-flex flex-column"
              dir="ltr"
              mode="out-in"
              name="fade"
              tag="div">
              <hr key="hr" />
              <div
                v-if="loadings.buyOrderFilesLoading"
                key="loader"
                class="py-5 w-100 position-relative">
                <div class="custom-loader center" />
              </div>
              <div
                v-if="!loadings.buyOrderFilesLoading"
                class="d-flex flex-column gap-4 mt-3">
                <KsUploadProgress
                  v-for="(file, index) in currBuyOrder.files"
                  :key="index"
                  :is-uploading="
                    loadings.uploadingProgress.activeIndex === index
                  "
                  :progress="loadings.uploadingProgress.progress">
                  <div class="d-flex flex-column gap-2">
                    <div
                      class="d-flex flex-row-reverse align-items-center gap-2">
                      <KsButton
                        :loading="loadings.deleteBtnLoading === index"
                        color="warn"
                        fixed-width-height="42"
                        theme="flat"
                        tooltip="حذف"
                        @click="
                          openDeleteModal(file.id, 'buyOrderFile', index)
                        ">
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
                            onBuyOrderFileChanged({
                              file: val,
                              index: index,
                            })
                        " />
                      <KsButton
                        key="download-btn"
                        :loading="loadings.downloadBtnLoading === index"
                        :disabled="!file.id"
                        color="primary"
                        fixed-width-height="42"
                        theme="flat"
                        tooltip="دانلود"
                        @click="downloadBuyOrderAttachFile(file, index)">
                        <template #content>
                          <DownloadIcon :fill="Colors.primary" width="24" />
                        </template>
                      </KsButton>
                    </div>
                    <KsInput
                      v-model="file.code"
                      class="flex-fill"
                      custom-height="42"
                      placeholder="کد سند کنترل کیفی"
                      simple-placeholder />
                    <KsInput
                      v-model="file.name"
                      class="flex-fill"
                      custom-height="42"
                      placeholder="عنوان سند کنترل کیفی"
                      simple-placeholder />
                    <KsInput
                      v-model="file.description"
                      class="flex-fill"
                      custom-height="42"
                      placeholder="توضیحات سند کنترل کیفی"
                      simple-placeholder />
                    <KsButton
                      :loading="loadings.uploadBtnLoading === index"
                      :theme="!file.id ? 'modern' : 'flat'"
                      :title="file.id ? 'بروزرسانی سند' : 'ثبت سند'"
                      @click="uploadBuyOrderAttachFile(index)" />
                  </div>
                </KsUploadProgress>
              </div>
              <span
                v-if="
                  !loadings.buyOrderFilesLoading && !currBuyOrder.files.length
                "
                class="text-center text-small">
                لیست اسناد کنترل کیفی خالی است
              </span>
              <KsButton
                v-if="!loadings.buyOrderFilesLoading"
                key="add-button"
                class="mt-5"
                custom-padding="14px 0"
                color="primary"
                theme="flat"
                @click="addBuyOrderFile">
                <template #content>
                  <PlusIcon :fill="Colors.primary" />
                </template>
              </KsButton>
            </transition-group>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2 mt-2">
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'QCRespondTempReceipt'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              بررسی رسید موقت برای
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              ابتدا اسناد کنترل کیفی را بررسی کرده و سپس رسید موقت را تایید یا
              رد کنید
            </span>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید"
            @click="requestBuyOrderQcRespondTempReceipt(true)" />
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="danger"
            title="رد"
            @click="requestBuyOrderQcRespondTempReceipt(false)" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'EngineeringRespondTempReceipt'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              بررسی اختلاف برای
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              ابتدا اسناد را بررسی کرده و سپس اختلاف را تایید یا رد کنید
            </span>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید"
            @click="requestBuyOrderEngineeringRespondTempReceipt('accepted')" />
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="warn"
            title="نیاز به اصلاح"
            @click="
              requestBuyOrderEngineeringRespondTempReceipt('says-needs-fixes')
            " />
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="danger"
            title="رد"
            @click="
              requestBuyOrderEngineeringRespondTempReceipt(
                'reject-give-to-salvage'
              )
            " />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'WarehouseFinalRespond'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              تایید نهایی برای
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              این فرآیندهای تامین، مونتاژ و ساخت را تایید می کنید؟
            </span>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید"
            @click="requestBuyOrderWarehouseFinalRespond" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'AssemblerAssembled'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              تایید مونتاژ برای
              {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              مونتاژ این فرآیندهای تامین، مونتاژ و ساخت را تایید می کنید؟
            </span>
          </div>
          <KsFilePicker
            :has-file="!!constructorInfo.timeSheet"
            :height="42"
            class="flex-fill"
            icon-size="20"
            @fileChange="(val) => attachConstructorConstructedFile(val)" />
        </div>

        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید"
            @click="requestBuyOrderAssemblerAssembled" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'PMOAcceptGivenToWarehouse'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              تایید ارسال
              {{ taskModal.modalTitle }}
              به انبار
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              ارسال این فرآیندهای تامین، مونتاژ و ساخت را به انبار تایید می
              کنید؟
            </span>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید"
            @click="requestBuyorderPMOAcceptGivenToWareHouse" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'ConstructionSubmit'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              ثبت تولید {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              ارسال این فرآیندهای تامین، مونتاژ و ساخت را به انبار تایید می
              کنید؟
            </span>
          </div>
          <KsSelect
            v-model="constructorInfo.machineId"
            :options="machineList"
            :disabled="true"
            placeholder="دستگاه"
            simple-placeholder />
          <KsTextArea
            v-model="constructorInfo.constractorDescription"
            placeholder="شرح"
            simple-placeholder
            class="mt-3" />
          <KsFilePicker
            :has-file="!!constructorInfo.timeSheet"
            :height="42"
            class="flex-fill"
            icon-size="20"
            @fileChange="(val) => attachConstructorConstructedFile(val)" />
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید"
            @click="requestBuyOrderConstructorConstructed" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'PMOAcceptReject'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              تایید ارسال به انبار ضایعات {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              ارسال این فرآیندهای تامین، مونتاژ و ساخت را به انبار ضایعات تایید
              می کنید؟
            </span>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید"
            @click="requestBuyOrderPMOAcceptReject" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>

      <div
        v-if="taskModal.mode === 'AcceptReject'"
        class="d-flex flex-column justify-content-between"
        style="max-width: 420px">
        <div class="p-3">
          <div class="text-center pt-2">
            <span class="fw-bold text-large">
              ارسال به انبار ضایعات {{ taskModal.modalTitle }}
            </span>
          </div>
          <div class="text-center py-4">
            <span class="fw-bold text-small">
              ارسال این فرآیندهای تامین، مونتاژ و ساخت را به انبار ضایعات تایید
              می کنید؟
            </span>
          </div>
        </div>
        <div class="d-flex justify-content-end gap-2">
          <KsButton
            :loading="loadings.modalSubmitLoading"
            color="success"
            title="تایید"
            @click="requestBuyOrderAcceptReject" />
          <KsButton
            color="transparent"
            title="بستن"
            @click="taskModal.visible = false" />
        </div>
      </div>
    </KsModal>

    <!--   PreviewModal   -->
    <KsModal v-model="previewModal.visible" blur>
      <div
        class="position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
        <div
          class="pe-3"
          style="overflow-y: auto; max-width: 380px; height: 65vh">
          <div class="d-flex flex-column gap-2" dir="rtl">
            <span class="text-medium fw-bold">
              فرآیندهای تامین، مونتاژ و ساخت
              {{ previewModal.buyOrder.title }}
            </span>
            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-4" dir="ltr">
              <div class="d-flex flex-column align-items-end gap-3">
                <PreviewModalTextRow
                  v-for="(buyOrder, index) in previewModal.data"
                  :key="index"
                  :prefix="buyOrder.prefix"
                  :text="buyOrder.value ?? '-'"
                  :title="buyOrder.title" />

                <KsButton
                  key="download-btn"
                  class="w-100"
                  color="primary"
                  fixed-width-height="42"
                  theme="flat"
                  tooltip="دانلود سند"
                  @click="
                    fetchDocumentFileInfo(previewModal.buyOrder.logShitId)
                  ">
                  <template #content>
                    <DownloadIcon :fill="Colors.primary" width="24" />
                  </template>
                </KsButton>

                <hr class="w-100 my-3" />
                <div v-if="previewModal.buyOrder.status !== 0">
                  <span class="text-medium fw-bold mb-3">
                    :استعلام های ثبت شده
                  </span>
                  <div
                    v-if="
                      !loadings.inquiryListLoading &&
                      !userBuyOrders[previewModal.activeIndex].inquiries.length
                    "
                    key="noData"
                    class="text-center">
                    <span class="text-small"
                      >استعلامی برای این فرآیندهای تامین، مونتاژ و ساخت ثبت نشده
                      است</span
                    >
                  </div>

                  <div
                    v-if="loadings.inquiryListLoading"
                    key="loader"
                    class="py-5 w-100 position-relative">
                    <div class="custom-loader center" />
                  </div>
                  <div
                    v-for="(inquiry, index) in userBuyOrders[
                      previewModal.activeIndex
                    ].inquiries"
                    :key="index"
                    :class="{ 'p-3': inquiry.isAccept }"
                    :style="
                      inquiry.isAccept &&
                      'border: 3px solid rgba(var(--success), 0.2); background: rgba(var(--success), 0.02)'
                    "
                    class="d-flex flex-column radius-12 gap-2 w-100">
                    <KsButton
                      v-if="inquiry.isAccept"
                      class="flex-1"
                      color="success"
                      theme="flat"
                      title="استعلام برنده" />
                    <div
                      class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                      <span class="text-small item-title">:قیمت</span>
                      <span class="text-small text-end" dir="rtl">
                        <strong class="ms-1">
                          {{ inquiry.price.toLocaleString("ar-SA") }}
                        </strong>
                        <small>تومان</small>
                      </span>
                    </div>
                    <div
                      class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                      <span class="text-small item-title">:تعداد</span>
                      <span class="text-small text-end" dir="rtl">
                        {{ inquiry.count }}
                        <small>عدد</small>
                      </span>
                    </div>
                    <div
                      class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                      <span class="text-small item-title">:امتیاز</span>
                      <KsButton color="primary" custom-padding="0">
                        <template #content>
                          <div style="padding: 4px 8px 1px 8px">
                            <span class="text-small text-white fw-bold">{{
                              inquiry.technicalScore
                            }}</span>
                          </div>
                        </template>
                      </KsButton>
                    </div>
                    <div
                      class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                      <span class="text-small item-title">:تامین کننده</span>
                      <span class="text-small text-end" dir="rtl">
                        {{ inquiry.provider.name }}
                      </span>
                    </div>
                    <KsAccordion btn-classes="p-2" class="accordion-btn">
                      <template #header>
                        <span class="text-small">اطلاعات تماس</span>
                      </template>
                      <template #content>
                        <div class="d-flex flex-column gap-1 p-2">
                          <div
                            v-for="(providerContact, index2) in inquiry.provider
                              .providerContacts"
                            :key="index2"
                            class="d-flex flex-column align-items-end gap-2">
                            <div
                              class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                              <span class="text-small item-title"
                                >:{{ providerContact.typeDescription }}</span
                              >
                              <span class="text-small">
                                {{ providerContact.data }}
                              </span>
                            </div>
                            <div
                              class="flex-1 d-flex flex-row-reverse justify-content-between gap-4 align-items-center">
                              <span class="text-small item-title">:نام</span>
                              <span class="text-small" dir="rtl">
                                {{ providerContact.name }}
                              </span>
                            </div>
                            <span class="flex-1 text-small">:توضیحات</span>
                            <span class="text-small" dir="rtl">
                              {{ providerContact.description }}
                            </span>
                            <hr
                              v-if="
                                index !==
                                inquiry.provider.providerContacts.length - 1
                              "
                              class="w-100" />
                          </div>
                        </div>
                      </template>
                    </KsAccordion>

                    <KsButton
                      v-if="previewModal.buyOrder.status == 1"
                      key="download-btn"
                      :loading="loadings.downloadBtnLoading === index"
                      class="w-100"
                      color="primary"
                      fixed-width-height="42"
                      theme="flat"
                      tooltip="دانلود سند فنی"
                      @click="fetchDocumentFileInfo(inquiry.technicalDoc)">
                      <template #content>
                        <DownloadIcon :fill="Colors.primary" width="24" />
                      </template>
                    </KsButton>
                    <KsButton
                      v-if="previewModal.buyOrder.status == 4"
                      key="download-btn"
                      :loading="loadings.downloadBtnLoading === index"
                      class="w-100"
                      color="primary"
                      fixed-width-height="42"
                      theme="flat"
                      tooltip="دانلود سند مالی"
                      @click="fetchDocumentFileInfo(inquiry.financialDoc)">
                      <template #content>
                        <DownloadIcon :fill="Colors.primary" width="24" />
                      </template>
                    </KsButton>
                    <hr
                      v-if="
                        index !==
                          userBuyOrders[previewModal.activeIndex].inquiries
                            .length -
                            1 && !inquiry.isAccept
                      "
                      class="w-100" />
                  </div>
                </div>
              </div>
              <!--  ًٍEquipment Process Attach Files -->
              <transition-group
                v-if="
                  previewModal.buyOrder.timeSheet !== '' &&
                  previewModal.buyOrder.timeSheet !== null &&
                  previewModal.buyOrder.timeSheet !==
                    '00000000-0000-0000-0000-000000000000'
                "
                key="equipmentProcess.files"
                class="d-flex flex-column gap-2 py-2"
                dir="ltr"
                mode="out-in"
                name="fade"
                tag="div">
                <hr class="w-100 my-3" key="hr" />

                <span key="title" class="text-medium fw-bold text-end">
                  :سند تایم شیت
                </span>

                <div class="d-flex flex-column gap-3 px-3 mt-3">
                  <KsButton
                    key="download-btn"
                    :loading="loadings.downloadBtnLoading"
                    class="w-100"
                    color="primary"
                    fixed-width-height="42"
                    theme="flat"
                    tooltip="دانلود"
                    @click="
                      fetchDocumentFileInfo(previewModal.buyOrder.timeSheet)
                    ">
                    <template #content>
                      <DownloadIcon :fill="Colors.primary" width="24" />
                    </template>
                  </KsButton>
                </div>
              </transition-group>
              <hr class="w-100 my-3" />

              <div v-if="previewModal.buyOrder.status !== 0">
                <span class="text-medium fw-bold"> :اسناد کنترل کیفی </span>
                <span
                  v-if="!previewModal.buyOrder?.files.length"
                  class="text-small">
                  <br />
                  اسناد کنترل کیفی برای این فرآیندهای تامین، مونتاژ و ساخت ثبت
                  نشده است
                </span>
                <div
                  v-if="loadings.buyOrderFilesLoading"
                  class="py-5 w-100 position-relative">
                  <div class="custom-loader center" />
                </div>
                <div
                  v-else-if="previewModal.buyOrder?.files.length"
                  class="d-flex flex-column gap-2"
                  dir="ltr">
                  <div
                    v-for="(file, indexBuyOrderFile) in previewModal.buyOrder
                      .files"
                    :key="indexBuyOrderFile">
                    <div class="d-flex flex-column gap-3 mt-3">
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title"> :کد سند </span>
                        <span class="text-small">{{ file.code }}</span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title"> :عنوان سند </span>
                        <span class="text-small">{{ file.name }}</span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title">
                          :توضیحات سند کنترل کیفی
                        </span>
                        <span class="text-small">{{ file.description }}</span>
                      </div>
                      <KsButton
                        key="download-btn"
                        :disabled="!file.id"
                        :loading="
                          loadings.downloadBtnLoading === indexBuyOrderFile
                        "
                        class="w-100"
                        color="primary"
                        fixed-width-height="42"
                        theme="flat"
                        tooltip="دانلود"
                        @click="
                          downloadBuyOrderAttachFile(file, indexBuyOrderFile)
                        ">
                        <template #content>
                          <DownloadIcon :fill="Colors.primary" width="24" />
                        </template>
                      </KsButton>
                    </div>
                  </div>
                </div>
                <hr class="w-100 my-3" />
              </div>
              <span class="text-medium fw-bold">
                :فرآیندهای تامین، مونتاژ و ساخت
              </span>
              <span v-if="!previewModal.processes.length" class="text-small">
                فرآیندی برای این فرآیندهای تامین، مونتاژ و ساخت ثبت نشده است
              </span>
              <div
                v-if="previewModal.processes.length"
                class="d-flex flex-column gap-2">
                <KsAccordion
                  v-for="(process, index) in previewModal.processes"
                  :key="index"
                  btn-classes="p-2"
                  class="accordion-btn">
                  <template #header>
                    <span class="text-small">
                      {{ process.title }}
                    </span>
                  </template>
                  <template #content>
                    <div class="d-flex flex-column gap-3 py-2 px-3" dir="ltr">
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title">:کد ورود کالا</span>
                        <span class="text-small">
                          {{ process.enterCommodityCode }}
                        </span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title">:کد خروج کالا</span>
                        <span class="text-small">
                          {{ process.exitCommodityCode }}
                        </span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title">:درصد وزنی</span>
                        <span class="text-small">
                          {{ parseFloat(process.weightPercent).toFixed(2) }}
                        </span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title"
                          >:پیش بینی زمان تولید مدارک</span
                        >
                        <span class="text-small" dir="rtl">
                          {{ process.mapDuration }}
                          <span>روز</span>
                        </span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title"
                          >:پیش بینی زمان تایید کارفرما</span
                        >
                        <span class="text-small" dir="rtl">
                          {{ process.masterDuration }}
                          <span>روز</span>
                        </span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title"
                          >:پیش بینی زمان تامین تامین</span
                        >
                        <span class="text-small" dir="rtl">
                          {{ process.processDuration }}
                          <span>روز</span>
                        </span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title"
                          >:نوع فرآیند تامین</span
                        >
                        <span class="text-small">{{
                          process.typeDescription
                        }}</span>
                      </div>
                      <div
                        class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                        <span class="text-small item-title"
                          >:هزینه فرآیند تامین</span
                        >
                        <span class="text-small fw-bold" dir="rtl">
                          {{ process.programPrice.toLocaleString("ar-SA") }}
                          تومان
                        </span>
                      </div>
                    </div>

                    <!--  ًٍEquipment Process Attach Files -->
                    <transition-group
                      key="equipmentProcess.files"
                      class="d-flex flex-column gap-2 py-2 px-3"
                      dir="ltr"
                      mode="out-in"
                      name="fade"
                      tag="div">
                      <hr key="hr" />
                      <span key="title" class="text-medium fw-bold text-end">
                        :اسناد مهندسی
                      </span>

                      <div
                        v-for="(file, indexProcessFile) in previewModal.buyOrder
                          .status == 0
                          ? [process.files[process.files.length - 1]]
                          : process.files"
                        :key="indexProcessFile">
                        <div
                          class="d-flex flex-column gap-3 mt-3"
                          v-if="
                            previewModal.buyOrder.status == 0
                              ? process.files.length - 1
                              : process.files
                          ">
                          <div
                            class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                            <span class="text-small item-title"
                              >:کد سند مهندسی</span
                            >
                            <span class="text-small">{{ file.version }}</span>
                          </div>
                          <div
                            class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                            <span class="text-small item-title">:شرح</span>
                            <span class="text-small">{{
                              file.description
                            }}</span>
                          </div>
                          <KsButton
                            key="download-btn"
                            :disabled="!file.id"
                            :loading="
                              loadings.downloadBtnLoading === indexProcessFile
                            "
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
                      </div>
                    </transition-group>
                  </template>
                </KsAccordion>
              </div>
            </div>
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

    <!--    DELETE MODAL   -->
    <DeleteModal
      v-model="deleteModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteModal.visible = false"
      @submit="onDeleteModalSubmit" />
  </div>
</template>

<style scoped>
.item-title {
  width: 180px;
  font-weight: bold;
  text-align: end;
}
</style>
