<script lang="ts" setup>
import type { Ref } from "vue";
import type { AxiosRequestConfig } from "axios";
import type ContractProvisionalStatement from "@/types/ContractProvisionalStatement";
import type ContractEquipment from "@/types/ContractEquipment";
import type Equipment from "@/types/Equipment";
import type Contract from "@/types/Contract";
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import {
  addContract,
  addContractEquipment,
  addContractProvisionalStatement,
  addGuarantyContract,
  attachFileContract,
  deleteContractEquipment,
  deleteContractProvisionalStatement,
  deleteGuarantyContract,
  detachFileContract,
  editContract,
  editContractEquipment,
  editContractProvisionalStatement,
  editGuarantyContract,
  getContractDetail,
  getContractEquipmentList,
  getContractProvisionalStatementList,
} from "@/api/ContractAPIs";
import { getEquipmentList } from "@/api/EquipmentAPIs";
import { downloadFile } from "@/composables/downloadFile";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import showWarnNotification from "@/composables/showWarnNotification";
import DeleteModal from "@/components/app/DeleteModal.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import KsFilePicker from "@/components/common/KsFilePicker.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsUploadProgress from "@/components/common/KsUploadProgress.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import CopyIcon from "@/components/icons/CopyIcon.vue";
import { Colors } from "@/assets/colors";
import mime from "mime-types";
import KsInputSeperate from "@/components/common/KsInputSeperate.vue";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();

const contractStatusList = [
  {
    title: "جاری",
    value: 1,
  },
  {
    title: "خاتمه یافته",
    value: 2,
  },
  {
    title: "فسخ شده",
    value: 3,
  },
  {
    title: "متوقف",
    value: 4,
  },
];

const typeOperation = [
  {
    title: "ساخت",
    value: "ساخت",
  },
  {
    title: "خدمات",
    value: "خدمات",
  },
];

const deleteFileModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
  mode: null as
    | "attachFile"
    | "contractEquipment"
    | "contractProvisionalStatement",
});

const deleteGuarantyModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
});

const openDeleteGuarantyModal = (id, index) => {
  deleteGuarantyModal.value.activeId = id;
  deleteGuarantyModal.value.activeIndex = index;
  if (id) deleteGuarantyModal.value.visible = true;
  else {
    deleteGuaranty();
  }
};

const openDeleteModal = (
  id,
  index,
  mode: "attachFile" | "contractEquipment" | "contractProvisionalStatement"
) => {
  deleteFileModal.value.activeId = id;
  deleteFileModal.value.activeIndex = index;
  deleteFileModal.value.mode = mode;
  deleteFileModal.value.visible = true;
};

const contract: Ref<Contract> = ref({
  id: props.id,
  code: "",
  subCode: "",
  tenderCode: "",
  employerOrderNo: "",
  employerContractNo: "",
  employerProjectNo: "",
  internalContractNo: "",
  internalProjectNo: "",
  contractDate: "",
  announceDate: "",
  startDate: "",
  payDate: "",
  price: 0,
  duration: 0,
  actualStartDate: "",
  TPA: "",
  site: "",
  type: "",
  title: "",
  tempDate: "",
  deliverDate: "",
  gurantyDate: "",
  endDate: "",
  employer: "",
  subEmployer: "",
  master: "",
  status: 1,
  forContract: "",
  services: "",
  files: [],
  guaranties: [],
});

const contractEquipmentList: Ref<ContractEquipment[]> = ref([]);

const contractProvisionalStatementList: Ref<ContractProvisionalStatement[]> =
  ref([]);

const equipmentList: Ref<Equipment[]> = ref([]);

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  selectLoading: true,
  contractEquipmentList: false,
  contractProvisionalStatementList: false,
  deleteBtnLoading: null,
  downloadBtnLoading: null,
  uploadingProgress: {
    activeIndex: null,
    progress: 0,
  },
});

const openDeleteFileModal = (id, index, mode) => {
  deleteFileModal.value.activeId = id;
  deleteFileModal.value.activeIndex = index;
  deleteFileModal.value.activeIndex = mode;
  if (id) deleteFileModal.value.visible = true;
  else {
    removeAttachedFileForm();
  }
};

const addAttachFileForm = () => {
  contract.value.files.push({
    id: null,
    description: "",
    name: ".",
    code: ".",
    size: "",
    file: null,
    mimeType: "",
  });
};

const onDeleteModalSubmit = async () => {
  console.log(deleteFileModal.value.mode);
  if (deleteFileModal.value.mode === "attachFile")
    await removeAttachedFileForm();
  else if (deleteFileModal.value.mode === "contractEquipment")
    await removeContractEquipmentForm();
  else if (deleteFileModal.value.mode === "contractProvisionalStatement")
    await removeContractProvisionalStatementForm();
};

const removeAttachedFileForm = async () => {
  if (deleteFileModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteFileModal.value.activeIndex;
    const [data, error] = await detachFileContract(
      contract.value.id,
      deleteFileModal.value.activeId
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      contract.value.files.splice(deleteFileModal.value.activeIndex, 1);
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else contract.value.files.splice(deleteFileModal.value.activeIndex, 1);
  deleteFileModal.value.visible = false;
};

const onAttachedFileChanged = (file: FileList, index) => {
  if (file[0]) {
    contract.value.files[index].id = null;
    contract.value.files[index].file = file[0];
    contract.value.files[index].mimeType = "." + mime.extension(file[0].type);
    if (!contract.value.files[index].description)
      contract.value.files[index].description = file[0].name;
    contract.value.files[index].size = file[0].size.toString();
  }
};

async function downloadAttachFile(file, index: number) {
  loadings.value.downloadBtnLoading = index;
  await downloadFile(file, file.id);
  loadings.value.downloadBtnLoading = null;
}

const addContractEquipmentForm = () => {
  contractEquipmentList.value.push({
    contractId: null,
    equipmentId: null,
    count: null,
  });
};

const addContractProvisionalStatementForm = () => {
  contractProvisionalStatementList.value.push({
    id: null,
    date: "",
    number: null,
    price: null,
    type: "",
    status: "",
    acceptedPrice: 0,
  });
};

const removeContractProvisionalStatementForm = async () => {
  if (deleteFileModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteFileModal.value.activeIndex;
    const [data, error] = await deleteContractProvisionalStatement(
      contractProvisionalStatementList.value[deleteFileModal.value.activeIndex]
        .contractId,
      contractProvisionalStatementList.value[deleteFileModal.value.activeIndex]
        .id
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      contractProvisionalStatementList.value.splice(
        deleteFileModal.value.activeIndex,
        1
      );
      showSuccessNotification("وضعیت قرارداد با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else
    contractProvisionalStatementList.value.splice(
      deleteFileModal.value.activeIndex,
      1
    );
  deleteFileModal.value.visible = false;
};

const removeContractEquipmentForm = async () => {
  if (deleteFileModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteFileModal.value.activeIndex;
    const [data, error] = await deleteContractEquipment(
      contractEquipmentList.value[deleteFileModal.value.activeIndex].contractId,
      contractEquipmentList.value[deleteFileModal.value.activeIndex].equipmentId
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      contractEquipmentList.value.splice(deleteFileModal.value.activeIndex, 1);
      showSuccessNotification("محصول قرارداد با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else
    contractEquipmentList.value.splice(deleteFileModal.value.activeIndex, 1);
  deleteFileModal.value.visible = false;
};

async function updateContactEquipmentList() {
  let isSuccess = true;
  for (let i = 0; i < contractEquipmentList.value.length; i++) {
    const [data, error] = contractEquipmentList.value[i].contractId
      ? await editContractEquipment(contractEquipmentList.value[i])
      : await addContractEquipment({
          count: contractEquipmentList.value[i].count,
          contractId: contract.value.id,
          equipmentId: contractEquipmentList.value[i].equipmentId,
        });
    if (!data?.success) {
      isSuccess = false;
      showErrorNotification(data?.message || error.message);
      break;
    } else contractEquipmentList.value[i].contractId = contract.value.id;
  }

  if (isSuccess) {
    await updateContactProvisionalStatementList();
    await uploadAttachFile();
  }
}

async function uploadAttachFile() {
  let isSuccess = true;
  for (let i = 0; i < contract.value.files.length; i++) {
    if (!contract.value.files[i].id) {
      loadings.value.uploadingProgress.activeIndex = i;
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
      formData.append("file", contract.value.files[i].file);

      const [data, error] = await attachFileContract(
        contract.value.id,
        contract.value.files[i].description,
        contract.value.files[i].code,
        contract.value.files[i].name,
        formData,
        config
      );
      loadings.value.uploadingProgress.progress = 0;
      if (!data?.success) {
        showErrorNotification(data?.message || error.message);
        isSuccess = false;
        break;
      } else contract.value.files[i].id = data.data;
    }
  }

  loadings.value.uploadingProgress.activeIndex = null;
}

async function updateContactProvisionalStatementList() {
  for (let i = 0; i < contractProvisionalStatementList.value.length; i++) {
    const [data, error] = contractProvisionalStatementList.value[i].id
      ? await editContractProvisionalStatement({
          contractId: contract.value.id,
          ...contractProvisionalStatementList.value[i],
        })
      : await addContractProvisionalStatement({
          contractId: contract.value.id,
          ...contractProvisionalStatementList.value[i],
        });
    if (!data?.success) {
      showErrorNotification(data?.message || error.message);
      break;
    } else
      contractProvisionalStatementList.value[i].contractId = Number(
        contract.value.id
      );
  }
}

const addGuaranty = () => {
  contract.value.guaranties.push({
    id: null,
    title: "",
    bank: "",
    no: "",
    regDate: "",
    price: "",
    deposite: "",
    chequeNo: "",
    sentDate: "",
    cancelDate: "",
    extention: "",
    dueDate: "",
    description: "",
    contractEntityId: props.id,
  });
};

const deleteGuaranty = async () => {
  if (deleteGuarantyModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteGuarantyModal.value.activeIndex;
    const [data, error] = await deleteGuarantyContract(
      contract.value.guaranties[deleteGuarantyModal.value.activeIndex].id
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      contract.value.guaranties.splice(
        deleteGuarantyModal.value.activeIndex,
        1
      );
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else
    contract.value.guaranties.splice(deleteGuarantyModal.value.activeIndex, 1);
  deleteGuarantyModal.value.visible = false;
};

async function sendGuaranty() {
  let isSuccess = true;
  for (let i = 0; i < contract.value.guaranties.length; i++) {
    if (!contract.value.guaranties[i].id) {
      await addGuarantyContract(contract.value.guaranties[i]);
    } else {
      await editGuarantyContract(contract.value.guaranties[i]);
    }
  }

  loadings.value.uploadingProgress.activeIndex = null;
  if (isSuccess) {
    router.back();
  }
}

async function sendRequest() {
  if (contract.value.title == null || contract.value.title == "") {
    showWarnNotification("فیلد عنوان را تکمیل کنید!");
    return;
  } else if (contract.value.code == null || contract.value.code == "") {
    showWarnNotification("فیلد کد کارفرمای اصلی را تکمیل کنید!");
    return;
  } else if (contract.value.employer == null || contract.value.employer == "") {
    showWarnNotification("فیلد کارفرما اصلی را تکمیل کنید!");
    return;
  } else if (
    contract.value.tenderCode == null ||
    contract.value.tenderCode == ""
  ) {
    showWarnNotification("فیلد کد مناقصه را تکمیل کنید!");
    return;
  } else if (
    contract.value.employerOrderNo == null ||
    contract.value.employerOrderNo == ""
  ) {
    showWarnNotification("فیلد شماره سفارش کارفرما را تکمیل کنید!");
    return;
  } else if (
    contract.value.contractDate == null ||
    contract.value.contractDate == ""
  ) {
    showWarnNotification("فیلد تاریخ عقد قرارداد را تکمیل کنید!");
    return;
  } else if (
    contract.value.announceDate == null ||
    contract.value.announceDate == ""
  ) {
    showWarnNotification("فیلد تاریخ ابلاغ قرارداد را تکمیل کنید!");
    return;
  } else if (contract.value.type == null || contract.value.type == "") {
    showWarnNotification("فیلد نوع عملیات را تکمیل کنید!");
    return;
  }
  loadings.value.btnLoading = true;
  contract.value.startDate = new Date().toJSON().slice(0, 10);
  contract.value.actualStartDate = new Date().toJSON().slice(0, 10);
  contract.value.payDate = new Date().toJSON().slice(0, 10);
  contract.value.endDate = new Date().toJSON().slice(0, 10);
  contract.value.gurantyDate = new Date().toJSON().slice(0, 10);
  contract.value.tempDate = new Date().toJSON().slice(0, 10);
  contract.value.deliverDate = new Date().toJSON().slice(0, 10);
  const [data, error] = props.id
    ? await editContract(contract.value)
    : await addContract(contract.value);
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    contract.value.id = data.data.id;
    await updateContactEquipmentList();
    await uploadAttachFile();
    await sendGuaranty();
  }
  loadings.value.btnLoading = false;
}

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;

  const [data, error] = await getContractDetail(id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else contract.value = data.data;

  loadings.value.inputLoading = false;
}

async function fetchContractEquipmentList(id: number | string) {
  loadings.value.contractEquipmentList = true;

  const [data, error] = await getContractEquipmentList(id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else contractEquipmentList.value = data.data.data;

  loadings.value.contractEquipmentList = false;
}

async function fetchContractProvisionalStatementList(id: number | string) {
  loadings.value.contractProvisionalStatementList = true;

  const [data, error] = await getContractProvisionalStatementList(id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else contractProvisionalStatementList.value = data.data.data;

  loadings.value.contractProvisionalStatementList = false;
}

async function fetchEquipmentList() {
  loadings.value.selectLoading = true;

  const [data, error] = await getEquipmentList();
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else equipmentList.value = data.data.data;

  loadings.value.selectLoading = false;
}

onMounted(async () => {
  if (props.id) {
    await fetchDetail(props.id);
    await fetchContractEquipmentList(props.id);
    await fetchContractProvisionalStatementList(props.id);
  }
  await fetchEquipmentList();
});
</script>

<template>
  <div>
    <KsUpdateForm
      :btn-loading="loadings.btnLoading"
      :is-edit="!!props.id"
      form-title="قرارداد"
      @formSubmit="sendRequest">
      <template #formIcon>
        <CopyIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #form>
        <div key="1" class="col-12 col-md-10 mb-5">
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
            <KsInput
              v-model="contract.title"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="عنوان پروژه *" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="contract.code"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کد کارفرما اصلی *" />
            <KsInput
              v-model="contract.employer"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کارفرما اصلی *" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="contract.subCode"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کد کارفرما فرعی" />
            <KsInput
              v-model="contract.subEmployer"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کارفرما فرعی" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="contract.tenderCode"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کد مناقصه" />
            <KsInput
              v-model="contract.employerOrderNo"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="شماره سفارش کارفرما" />
            <KsInput
              v-model="contract.employerContractNo"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="شماره قرارداد کارفرما" />
            <KsInput
              v-model="contract.employerProjectNo"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="شماره پروژه کارفرما" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="contract.internalContractNo"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کد قرارداد داخلی" />
            <KsInput
              v-model="contract.internalProjectNo"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کد پروژه داخلی" />
            <KsInput
              v-model="contract.site"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="سایت" />
            <KsInput
              v-model="contract.TPA"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="شرکت بازرسی TPA" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsDatePicker
              v-model="contract.contractDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ عقد قرارداد *"
              element-tag="contractDate" />
            <KsDatePicker
              v-model="contract.announceDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ ابلاغ قرارداد *"
              element-tag="announceDate" />
            <KsDatePicker
              v-model="contract.startDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ شروع قرارداد *"
              element-tag="startDate" />
            <KsDatePicker
              v-model="contract.actualStartDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ واقعی شروع *"
              element-tag="actualStartDate" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsDatePicker
              v-model="contract.payDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ واریز پیش پرداخت *"
              element-tag="payDate" />
            <KsSelect
              v-model="contract.type"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="نوع عملیات"
              :options="typeOperation" />
            <KsInputSeperate
              v-model="contract.price"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="مبلغ قرارداد (ریال)" />
            <KsInput
              v-model="contract.duration"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="مدت قرارداد (روز)" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsDatePicker
              v-model="contract.endDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ پایان پروژه *"
              element-tag="endDate" />
            <KsDatePicker
              v-model="contract.gurantyDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ پایان گارانتی *"
              element-tag="gurantyDate" />
            <KsDatePicker
              v-model="contract.tempDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ تحویل موقت *"
              element-tag="tempDate" />
            <KsDatePicker
              v-model="contract.deliverDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تاریخ تحویل دائم *"
              element-tag="deliverDate" />
          </div>
        </div>

        <transition-group
          key="contract.files"
          class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
          mode="out-in"
          name="fade"
          tag="div">
          <hr key="hr" />
          <span key="title" class="text-medium fw-bold">
            :فایل های پیوست قرارداد
          </span>

          <KsUploadProgress
            v-for="(file, index) in contract.files"
            :key="index"
            :is-uploading="loadings.uploadingProgress.activeIndex === index"
            :progress="loadings.uploadingProgress.progress">
            <div
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-3 mt-3">
              <div class="d-flex flex-row-reverse align-items-center gap-3">
                <KsButton
                  :loading="loadings.deleteBtnLoading === index"
                  color="warn"
                  fixed-width-height="42"
                  theme="flat"
                  tooltip="حذف"
                  @click="openDeleteModal(id, index, 'attachFile')">
                  <template #content>
                    <TimesIcon :fill="Colors.warn" width="18" />
                  </template>
                </KsButton>
                <KsFilePicker
                  :file-size="file.size"
                  :has-file="!!file.id || !!file.file"
                  :height="42"
                  class="flex-fill"
                  icon-size="20"
                  @fileChange="(val) => onAttachedFileChanged(val, index)" />
              </div>
              <div
                class="d-flex flex-fill flex-row-reverse align-items-center gap-3">
                <KsSelect
                  v-model="file.description"
                  :options="[
                    {
                      value: 'قرارداد',
                      title: 'قرارداد',
                    },
                    {
                      value: 'اسناد فنی ضمیمه قرارداد',
                      title: 'اسناد فنی ضمیمه قرارداد',
                    },
                    {
                      value: 'متمم قرارداد',
                      title: 'متمم قرارداد',
                    },
                    {
                      value: 'صورت جلسات',
                      title: 'صورت جلسات',
                    },
                    {
                      value: 'متن',
                      title: 'متن',
                    },
                    {
                      value: 'منشور',
                      title: 'منشور',
                    },
                    {
                      value: 'PEP',
                      title: 'PEP',
                    },
                    {
                      value: 'فاکتور',
                      title: 'فاکتور',
                    },
                    {
                      value: 'صورت وضعیت ها',
                      title: 'صورت وضعیت ها',
                    },
                    {
                      value: 'رسیدهای واریزی',
                      title: 'رسیدهای واریزی',
                    },
                  ]"
                  class="flex-fill"
                  placeholder="عنوان فایل پیوست"
                  simple-placeholder />
                <KsButton
                  key="add-btn"
                  :disabled="!file.id"
                  :loading="loadings.downloadBtnLoading === index"
                  color="primary"
                  fixed-width-height="42"
                  theme="flat"
                  tooltip="دانلود"
                  @click="downloadAttachFile(file, index)">
                  <template #content>
                    <DownloadIcon :fill="Colors.primary" width="24" />
                  </template>
                </KsButton>
              </div>
            </div>
          </KsUploadProgress>

          <KsButton
            key="add-btn"
            class="mt-3"
            color="primary"
            fixed-width-height="52"
            tooltip="فایل پیوست جدید"
            @click="addAttachFileForm">
            <template #content>
              <PlusIcon :fill="Colors.white" width="18" />
            </template>
          </KsButton>
        </transition-group>

        <transition-group
          v-if="!loadings.contractEquipmentList"
          key="contract.equipments"
          class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
          mode="out-in"
          name="fade"
          tag="div">
          <hr key="hr" />
          <span key="title" class="text-medium fw-bold">
            :محصولات قرارداد
          </span>

          <div
            v-for="(contractEquipment, index) in contractEquipmentList"
            :key="index"
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-3 mt-3">
            <div
              class="d-flex flex-row-reverse align-items-center gap-3 flex-1">
              <KsButton
                :loading="loadings.deleteBtnLoading === index"
                color="warn"
                fixed-width-height="42"
                theme="flat"
                tooltip="حذف"
                @click="
                  openDeleteModal(
                    contractEquipment.contractId,
                    index,
                    'contractEquipment'
                  )
                ">
                <template #content>
                  <TimesIcon :fill="Colors.warn" width="18" />
                </template>
              </KsButton>
              <KsSelect
                v-model="contractEquipment.equipmentId"
                :loading="loadings.selectLoading"
                :options="equipmentList"
                class="flex-1"
                label-prop-name="title"
                placeholder="محصول مورد نظر را انتخاب کنید"
                simple-placeholder
                value-prop-name="id" />
            </div>
            <KsInput
              v-model="contractEquipment.count"
              class="flex-1"
              input-type="number"
              placeholder="تعداد"
              simple-placeholder />
          </div>

          <KsButton
            key="add-btn"
            class="mt-3"
            color="primary"
            fixed-width-height="52"
            tooltip="محصول قرارداد جدید"
            @click="addContractEquipmentForm">
            <template #content>
              <PlusIcon :fill="Colors.white" width="18" />
            </template>
          </KsButton>
        </transition-group>

        <transition-group
          v-if="!loadings.contractProvisionalStatementList"
          key="contract.equipments"
          class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
          mode="out-in"
          name="fade"
          tag="div">
          <hr key="hr" />
          <span key="title" class="text-medium fw-bold">
            :صورت وضعیت قرارداد
          </span>

          <div
            v-for="(statement, index) in contractProvisionalStatementList"
            :key="index"
            class="d-flex flex-column gap-5 mb-5">
            <div
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-3 mt-3">
              <div
                class="d-flex flex-row-reverse align-items-center gap-3 flex-1">
                <KsButton
                  :loading="loadings.deleteBtnLoading === index"
                  color="warn"
                  fixed-width-height="42"
                  theme="flat"
                  tooltip="حذف"
                  @click="
                    openDeleteModal(
                      statement.id,
                      index,
                      'contractProvisionalStatement'
                    )
                  ">
                  <template #content>
                    <TimesIcon :fill="Colors.warn" width="18" />
                  </template>
                </KsButton>
                <KsInput
                  v-model="statement.number"
                  class="flex-1"
                  input-type="number"
                  placeholder="شماره صورت وضعیت"
                  :loading="loadings.contractProvisionalStatementList" />
                <KsDatePicker
                  v-model="statement.date"
                  class="flex-1"
                  element-tag="contractProvisionalStatementDate"
                  placeholder="تاریخ"
                  :loading="loadings.contractProvisionalStatementList" />
              </div>
            </div>
            <div
              class="d-flex flex-row-reverse align-items-center gap-3 flex-1">
              <KsInput
                v-model="statement.price"
                class="flex-1"
                input-type="number"
                placeholder="مبلغ صورت وضعیت"
                append-text="تومان"
                :loading="loadings.contractProvisionalStatementList" />
              <KsInput
                v-model="statement.acceptedPrice"
                class="flex-1"
                input-type="number"
                append-text="تومان"
                placeholder="مبلغ تایید شده"
                :loading="loadings.contractProvisionalStatementList" />
            </div>
            <div
              class="d-flex flex-row-reverse align-items-center gap-3 flex-1">
              <KsSelect
                v-model="statement.type"
                class="flex-1"
                input-type="number"
                :options="[
                  {
                    title: 'فاکتور',
                  },
                  {
                    title: 'صورت وضعیت',
                  },
                ]"
                placeholder="نوع"
                value-prop-name="title"
                :loading="loadings.contractProvisionalStatementList" />
              <KsSelect
                v-model="statement.status"
                class="flex-1"
                input-type="number"
                :options="[
                  {
                    title: 'دوره ای',
                  },
                  {
                    title: 'تحویل موقت',
                  },
                  {
                    title: 'تحویل دائم',
                  },
                ]"
                placeholder="وضعیت"
                value-prop-name="title"
                :loading="loadings.contractProvisionalStatementList" />
            </div>
          </div>

          <KsButton
            key="add-btn"
            class="mt-3"
            color="primary"
            fixed-width-height="52"
            tooltip="محصول قرارداد جدید"
            @click="addContractProvisionalStatementForm">
            <template #content>
              <PlusIcon :fill="Colors.white" width="18" />
            </template>
          </KsButton>
        </transition-group>

        <transition-group
          key="contract.guaranties"
          class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
          mode="out-in"
          name="fade"
          tag="div">
          <hr key="hr" />
          <span key="title" class="text-medium fw-bold"> :گارانتی </span>

          <KsUploadProgress
            v-for="(guaranty, index) in contract.guaranties"
            :key="index">
            <div
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-3 mt-3">
              <div key="1" class="col-12 col-lg-12 col-md-12 mb-5">
                <div
                  class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                  <KsButton
                    :loading="loadings.deleteBtnLoading === index"
                    color="warn"
                    fixed-width-height="42"
                    theme="flat"
                    tooltip="حذف"
                    class="mt-5"
                    @click="openDeleteGuarantyModal(guaranty.id, index)">
                    <template #content>
                      <TimesIcon :fill="Colors.warn" width="18" />
                    </template>
                  </KsButton>

                  <KsInput
                    v-model="guaranty.title"
                    placeholder="موضوع ضمانتنامه"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42" />
                  <KsInput
                    v-model="guaranty.bank"
                    placeholder="بانک / صندوق"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42" />
                </div>
                <div
                  class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                  <KsInput
                    v-model="guaranty.no"
                    placeholder="شماره ضمانتنامه"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42" />
                  <KsDatePicker
                    v-model="guaranty.regDate"
                    placeholder="تاریخ صدور"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42"
                    element-tag="startContract-issuing1" />
                </div>
                <div
                  class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                  <KsInput
                    v-model="guaranty.price"
                    placeholder="مبلغ ضمانتنامه"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42" />
                  <KsInput
                    v-model="guaranty.deposite"
                    placeholder="مبلغ ودیعه"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42" />
                </div>
                <div
                  class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                  <KsInput
                    v-model="guaranty.chequeNo"
                    placeholder="شماره چک"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42" />
                  <KsDatePicker
                    v-model="guaranty.sentDate"
                    placeholder="تاریخ ارسال به کارفرما"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42"
                    element-tag="startContract-sent" />
                </div>
                <div
                  class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                  <KsDatePicker
                    v-model="guaranty.cancelDate"
                    placeholder="تاریخ ابطال"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42"
                    element-tag="cancelContract-date" />
                  <KsInput
                    v-model="guaranty.extention"
                    placeholder="تمدید / روز"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42" />
                </div>
                <div
                  class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                  <KsDatePicker
                    v-model="guaranty.dueDate"
                    placeholder="اخرین سررسید"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42"
                    element-tag="endContract-list" />
                  <KsInput
                    v-model="guaranty.description"
                    placeholder="توضیحات"
                    :loading="loadings.inputLoading"
                    class="flex-fill mt-5"
                    icon-size="20"
                    height="42" />
                </div>
              </div>
            </div>
          </KsUploadProgress>

          <div class="col-12 col-md-10 mb-4 d-flex flex-column gap-3">
            <KsButton
              key="add-guaranty-contract-list"
              color="primary"
              fixed-width-height="52"
              tooltip="گارانتی جدید"
              @click="addGuaranty">
              <template #content>
                <PlusIcon :fill="Colors.white" width="18" />
              </template>
            </KsButton>
          </div>
        </transition-group>
      </template>
    </KsUpdateForm>

    <!--    DELETE MODAL   -->
    <DeleteModal
      v-model="deleteFileModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteFileModal.visible = false"
      @submit="onDeleteModalSubmit" />

    <DeleteModal
      v-model="deleteGuarantyModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteGuarantyModal.visible = false"
      @submit="deleteGuaranty" />
  </div>
</template>
