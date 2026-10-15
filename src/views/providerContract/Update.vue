<script lang="ts" setup>
import type {Ref} from "vue";
import type {AxiosRequestConfig} from "axios";
import type ContractProvisionalStatement from "@/types/ContractProvisionalStatement";
import type Contract from "@/types/Contract";
import {onMounted, ref} from "vue";
import {useRouter} from "vue-router";
import {
  addContractProvider,
  attachFileContractProvider,
  detachFileContractProvider,
  editContractProvider,
  getContractProviderDetail,
} from "@/api/ProviderContractAPIs";
import {
  addContractProvisionalStatement, addGuarantyContract, attachFileContract,
  deleteContractProvisionalStatement, deleteGuarantyContract,
  editContractProvisionalStatement, editGuarantyContract,
  getContractProvisionalStatementList,
} from "@/api/ContractAPIs";
import {downloadFile} from "@/composables/downloadFile";
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
import {Colors} from "@/assets/colors";
import mime from "mime-types";
import {getProviderVendorList} from "@/api/ProviderAPIs";
import {addGuarantyTender, editGuarantyTender} from "@/api/TenderAPIs";

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
  announceDate: "2022-11-01T15:05:53.541Z",
  startDate: "",
  payDate: "2022-11-01T15:05:53.541Z",
  price: 0,
  duration: 0,
  actualStartDate: "2022-11-01T15:05:53.541Z",
  TPA: "",
  site: "",
  type: "",
  title: "",
  tempDate: "2022-11-01T15:05:53.541Z",
  deliverDate: "",
  gurantyDate: "2022-11-01T15:05:53.541Z",
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

const contractProvisionalStatementList: Ref<ContractProvisionalStatement[]> =
    ref([]);
const contractVendorList = ref([]);

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  selectLoading: true,
  deleteBtnLoading: null,
  vendorListLoading: true,
  contractProvisionalStatementList: false,
  downloadBtnLoading: null,
  uploadingProgress: {
    activeIndex: null,
    progress: 0,
  },
});

type DeleteModalMode = "attachFile" | "contractProvisionalStatement";

const deleteModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
  mode: null as DeleteModalMode,
});

const deleteGuarantyModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
});

const openDeleteModal = (id, index, mode: DeleteModalMode) => {
  deleteModal.value.activeId = id;
  deleteModal.value.activeIndex = index;
  deleteModal.value.mode = mode;
  deleteModal.value.visible = true;
};

const openDeleteGuarantyModal = (id, index) => {
  deleteGuarantyModal.value.activeId = id;
  deleteGuarantyModal.value.activeIndex = index;
  if (id) deleteGuarantyModal.value.visible = true;
  else {
    deleteGuaranty()
  }
};

const addAttachFileForm = () => {
  (contract.value.files as any).push({
    id: null,
    description: "",
    name: ".",
    code: ".",
    size: "",
    file: null,
    mimeType: "",
  });
};

async function fetchVendorList() {
  loadings.value.vendorListLoading = true;
  const [data, error] = await getProviderVendorList();
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
  } else contractVendorList.value = data.data;
  loadings.value.vendorListLoading = false;
}

const onDeleteModalSubmit = () => {
  if (deleteModal.value.mode === "attachFile") removeAttachedFileForm();
  else if (deleteModal.value.mode === "contractProvisionalStatement")
    removeContractProvisionalStatementForm();
};

const removeAttachedFileForm = async () => {
  if (deleteModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteModal.value.activeIndex;
    const [data, error] = await detachFileContractProvider(
        contract.value.id,
        deleteModal.value.activeId
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      contract.value.files.splice(deleteModal.value.activeIndex, 1);
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else contract.value.files.splice(deleteModal.value.activeIndex, 1);
  deleteModal.value.visible = false;
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
          const {loaded, total} = progressEvent;
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
  if (isSuccess) {
    showSuccessNotification(
        "قرارداد با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
    );
  }
}

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
  if (deleteModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteModal.value.activeIndex;
    const [data, error] = await deleteContractProvisionalStatement(
        contractProvisionalStatementList.value[deleteModal.value.activeIndex]
            .contractId,
        contractProvisionalStatementList.value[deleteModal.value.activeIndex].id
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      contractProvisionalStatementList.value.splice(
          deleteModal.value.activeIndex,
          1
      );
      showSuccessNotification("وضعیت قرارداد تامین کنندگان با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else
    contractProvisionalStatementList.value.splice(
        deleteModal.value.activeIndex,
        1
    );
  deleteModal.value.visible = false;
};

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
      contract.value.guaranties.splice(deleteGuarantyModal.value.activeIndex, 1);
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else contract.value.guaranties.splice(deleteGuarantyModal.value.activeIndex, 1);
  deleteGuarantyModal.value.visible = false;
};

async function sendGuaranty() {
  let isSuccess = true;
  for (let i = 0; i < contract.value.guaranties.length; i++) {
    if (!contract.value.guaranties[i].id) {
      await addGuarantyContract(contract.value.guaranties[i])
    }
    else{
      await editGuarantyContract(contract.value.guaranties[i])
    }
  }

  loadings.value.uploadingProgress.activeIndex = null;
  if (isSuccess) {
    showSuccessNotification(
        "مناقصه با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
    );
    router.back();
  }
}

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = props.id
      ? await editContractProvider(contract.value)
      : await addContractProvider(contract.value);
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    contract.value.id = data.data.id;
    await updateContactProvisionalStatementList();
    await uploadAttachFile();
    await sendGuaranty()
  }
  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (
      contract.value.code === "" ||
      contract.value.code === null ||
      contract.value.title === "" ||
      contract.value.title === null ||
      contract.value.employer === "" ||
      contract.value.employer === null ||
      contract.value.startDate === "" ||
      contract.value.startDate === null ||
      contract.value.endDate === "" ||
      contract.value.endDate === null ||
      contract.value.deliverDate === "" ||
      contract.value.deliverDate === null ||
      contract.value.contractDate === "" ||
      contract.value.contractDate === null
  )
    isValid = false;

  if (isValid) return true;
  else {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
};

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

async function fetchContractProvisionalStatementList(id: number | string) {
  loadings.value.contractProvisionalStatementList = true;

  const [data, error] = await getContractProvisionalStatementList(id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else contractProvisionalStatementList.value = data.data.data;

  loadings.value.contractProvisionalStatementList = false;
}

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;

  const [data, error] = await getContractProviderDetail(id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else contract.value = data.data;

  loadings.value.inputLoading = false;
}

onMounted(async () => {
  if (props.id) {
    await fetchDetail(props.id);
    await fetchContractProvisionalStatementList(props.id);
  }
  await fetchVendorList();
});
</script>

<template>
  <div>
    <KsUpdateForm
        :btn-loading="loadings.btnLoading"
        :is-edit="!!props.id"
        form-title="قرارداد تامین کنندگان"
        @formSubmit="sendRequest">
      <template #formIcon>
        <CopyIcon :fill="Colors.black" class="ms-3" width="24"/>
      </template>
      <template #form>
        <div key="1" class="col-12 col-md-10 mb-5">
          <div
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
            <KsInput
                v-model="contract.title"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="موضوع قرارداد *"
            />
            <KsSelect
                v-model="contract.employer"
                :loading="loadings.vendorListLoading"
                :options="contractVendorList"
                label-prop-name="name"
                value-prop-name="name"
                mode="filter"
                class="flex-fill"
                placeholder="پیمانکار *"/>

            <KsInput
                v-model="contract.price"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="مبلغ قرارداد"
            />
          </div>
          <div
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">

            <KsInput
                v-model="contract.code"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="کد قرارداد *"
            />
            <KsInput
                v-model="contract.internalContractNo"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="شماره قرارداد"
            />
            <KsInput
                v-model="contract.internalProjectNo"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="کد پروژه"
            />
          </div>
          <div
              v-if="!loadings.inputLoading"
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsDatePicker
                v-model="contract.contractDate"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="تاریخ عقد قرارداد *"
                element-tag="contract-provider-Date"
            />
            <KsDatePicker
                v-model="contract.startDate"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="تاریخ شروع قرارداد *"
                element-tag="start-provider-Date"
            />
            <KsDatePicker
                v-model="contract.deliverDate"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="تاریخ تحویل قرارداد *"
                element-tag="deliver-provider-Date"
            />
            <KsDatePicker
                v-model="contract.endDate"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="تاریخ اتمام حسن انجام کار *"
                element-tag="end-provider-Date"
            />
          </div>
          <div
              v-if="!loadings.inputLoading"
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">

            <KsInput
                v-model="contract.employerOrderNo"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="شماره الحاقیه"
            />
            <KsInput
                v-model="contract.site"
                :loading="loadings.inputLoading"
                class="flex-fill"
                placeholder="مبلغ کل (قرارداد + الحاقیه)"
            />
            <KsInput
              v-model="contract.services"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="آخرین صورت وضعیت پرداخت شده"
          />
          </div>
        </div>

        <transition-group
            key="contract.files"
            class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
            mode="out-in"
            name="fade"
            tag="div">
          <hr key="hr"/>
          <span key="title" class="text-medium fw-bold">
            :فایل های پیوست قرارداد تامین کنندگان
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
                    @click="openDeleteModal(file.id, index, 'attachFile')">
                  <template #content>
                    <TimesIcon :fill="Colors.warn" width="18"/>
                  </template>
                </KsButton>
                <KsFilePicker
                    :file-size="file.size"
                    :has-file="!!file.id || !!file.file"
                    :height="42"
                    class="flex-fill"
                    icon-size="20"
                    @fileChange="(val) => onAttachedFileChanged(val, index)"/>
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
                      value: 'الحاقیه',
                      title: 'الحاقیه',
                    },
                    {
                      value: 'متن',
                      title: 'متن',
                    },
                    {
                      value: 'صورت جلسات',
                      title: 'صورت جلسات',
                    },
                    {
                      value: 'صورت وضعیت ها',
                      title: 'صورت وضعیت ها',
                    },
                    {
                      value: 'سند های پرداختی',
                      title: 'سند های پرداختی',
                    },
                    {
                      value: 'فاکتور',
                      title: 'فاکتور',
                    },
                    {
                      value: 'چک تسویه',
                      title: 'چک تسویه',
                    },
                  ]"
                    class="flex-fill"
                    placeholder="عنوان فایل پیوست"
                    simple-placeholder/>
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
                    <DownloadIcon :fill="Colors.primary" width="24"/>
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
              <PlusIcon :fill="Colors.white" width="18"/>
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
          <hr key="hr"/>
          <span key="title" class="text-medium fw-bold">
            :صورت وضعیت قرارداد تامین کنندگان
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
                    <TimesIcon :fill="Colors.warn" width="18"/>
                  </template>
                </KsButton>
                <KsInput
                    v-model="statement.number"
                    class="flex-1"
                    input-type="number"
                    placeholder="شماره صورت وضعیت"
                    :loading="loadings.contractProvisionalStatementList"/>
                <KsDatePicker
                    v-model="statement.date"
                    class="flex-1"
                    :element-tag="`contractProvisionalStatementDate_${index}`"
                    placeholder="تاریخ"
                    :loading="loadings.contractProvisionalStatementList"/>
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
                  :loading="loadings.contractProvisionalStatementList"/>
              <KsInput
                  v-model="statement.acceptedPrice"
                  class="flex-1"
                  input-type="number"
                  append-text="تومان"
                  placeholder="مبلغ تایید شده"
                  :loading="loadings.contractProvisionalStatementList"/>
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
                  :loading="loadings.contractProvisionalStatementList"/>
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
                  :loading="loadings.contractProvisionalStatementList"/>
            </div>
          </div>

          <KsButton
              key="add-btn"
              class="mt-3"
              color="primary"
              fixed-width-height="52"
              tooltip="محصول قرارداد تامین کنندگان جدید"
              @click="addContractProvisionalStatementForm">
            <template #content>
              <PlusIcon :fill="Colors.white" width="18"/>
            </template>
          </KsButton>
        </transition-group>

        <transition-group
            key="contract.guaranties"
            class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
            mode="out-in"
            name="fade"
            tag="div">
          <hr key="hr"/>
          <span key="title" class="text-medium fw-bold">
            :گارانتی
          </span>

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
                      <TimesIcon :fill="Colors.warn" width="18"/>
                    </template>
                  </KsButton>

                  <KsInput
                      v-model="guaranty.title"
                      placeholder="موضوع ضمانتنامه"
                      :loading="loadings.inputLoading"
                      class="flex-fill mt-5"
                      icon-size="20"
                      height="42"
                  />

                  <KsSelect
                      v-model="guaranty.bank"
                      class="flex-fill mt-5"
                      input-type="number"
                      :options="[
                        {
                          title: 'چک',
                        },
                        {
                          title: 'سفته',
                        },
                      ]"
                      placeholder="نوع"
                      value-prop-name="title"
                      :loading="loadings.inputLoading"
                      icon-size="20"
                      height="42"/>
                </div>
                <div
                    class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">

                  <KsInput
                      v-model="guaranty.price"
                      placeholder="مبلغ ضمانتنامه"
                      :loading="loadings.inputLoading"
                      class="flex-fill mt-5"
                      icon-size="20"
                      height="42"
                  />
                  <KsInput
                      v-model="guaranty.chequeNo"
                      placeholder="شماره چک / سفته"
                      :loading="loadings.inputLoading"
                      class="flex-fill mt-5"
                      icon-size="20"
                      height="42"
                  />
                </div>
                <div
                    class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">

                  <KsDatePicker
                      v-model="guaranty.sentDate"
                      placeholder="تاریخ دریافت"
                      :loading="loadings.inputLoading"
                      class="flex-fill mt-5"
                      icon-size="20"
                      height="42"
                      element-tag="get-date"
                  />
                  <KsDatePicker
                      v-model="guaranty.cancelDate"
                      placeholder="تاریخ عودت"
                      :loading="loadings.inputLoading"
                      class="flex-fill mt-5"
                      icon-size="20"
                      height="42"
                      element-tag="return-date"
                  />
                </div>
              </div>

            </div>
          </KsUploadProgress>

          <div class="col-12 col-md-10 mb-4 d-flex flex-column gap-3">
            <KsButton
                key="add-guaranty-provider-list"
                color="primary"
                fixed-width-height="52"
                tooltip="گارانتی جدید"
                @click="addGuaranty">
              <template #content>
                <PlusIcon :fill="Colors.white" width="18"/>
              </template>
            </KsButton>
          </div>


        </transition-group>

      </template>
    </KsUpdateForm>

    <!--    DELETE MODAL   -->
    <DeleteModal
        v-model="deleteModal.visible"
        :button-loading="loadings.deleteBtnLoading !== null"
        @cancel="deleteModal.visible = false"
        @submit="onDeleteModalSubmit"/>

    <DeleteModal
        v-model="deleteGuarantyModal.visible"
        :button-loading="loadings.deleteBtnLoading !== null"
        @cancel="deleteGuarantyModal.visible = false"
        @submit="deleteGuaranty"/>
  </div>
</template>
