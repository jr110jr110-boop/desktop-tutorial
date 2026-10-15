<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import type { AxiosRequestConfig } from "axios";
import type Tender from "@/types/Tender";
import { useRouter } from "vue-router";
import {
  addGuarantyTender,
  editGuarantyTender,
  addTender,
  attachFileTender,
  detachFileTender,
  deleteGuarantyTender,
  editFileTender,
  editTender,
  getTenderDetail,
} from "@/api/TenderAPIs";
import { downloadFile } from "@/composables/downloadFile";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import DeleteModal from "@/components/app/DeleteModal.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import KsTextArea from "@/components/common/KsTextArea.vue";
import KsUploadProgress from "@/components/common/KsUploadProgress.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsFilePicker from "@/components/common/KsFilePicker.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import DownloadIcon from "@/components/icons/DownloadIcon.vue";
import TagsIcon from "@/components/icons/TagsIcon.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import { Colors } from "@/assets/colors";
import mime from "mime-types";
import KsSelect from "@/components/common/KsSelect.vue";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();

const tender: Ref<Tender> = ref({
  id: props.id,
  code: "",
  pmrNo: "",
  description: "",
  count: "",
  company: "",
  subCompany: "",
  reciveDate: "",
  tenderDate: "",
  techSendDate: "",
  financeSendDate: "",
  callResult: "",
  customerResult: "",
  result: "",
  files: [],
  guaranties: [],
  customerName: "",
  customerRole: "",
  customerTel: "",
  customerCode: "",
  customerMobile: "",
});

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  deleteBtnLoading: null,
  downloadBtnLoading: null,
  submitButtonLoading: false,
  uploadingProgress: {
    activeIndex: null,
    progress: 0,
  },
});

const selectCustomerResult = [
  {
    title: "تعیین نشده",
    value: "تعیین نشده",
  },
  {
    title: "در حال مذاکره",
    value: "در حال مذاکره",
  },
  {
    title: "تایید شده",
    value: "تایید شده",
  },
  {
    title: "رد شده",
    value: "رد شده",
  },
];

const selectFileRoll = [
  {
    title: "مدارک مناقصه",
    value: "مدارک مناقصه",
  },
  {
    title: "پیشنهاد فنی",
    value: "پیشنهاد فنی",
  },
  {
    title: "پیشنهاد ملی",
    value: "پیشنهاد ملی",
  },
  {
    title: "ضمانت نامه شرکت مناقصه",
    value: "ضمانت نامه شرکت مناقصه",
  },
  {
    title: "صورت جلسات قراردادی",
    value: "صورت جلسات قراردادی",
  },
];

const deleteFileModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
});

const deleteGuarantyModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null,
});

const openDeleteFileModal = (id, index) => {
  deleteFileModal.value.activeId = id;
  deleteFileModal.value.activeIndex = index;
  if (id) deleteFileModal.value.visible = true;
  else {
    removeAttachedFileForm();
  }
};

const openDeleteGuarantyModal = (id, index) => {
  deleteGuarantyModal.value.activeId = id;
  deleteGuarantyModal.value.activeIndex = index;
  if (id) deleteGuarantyModal.value.visible = true;
  else {
    console.log(deleteGuaranty());
    deleteGuaranty();
  }
};

const addAttachFileForm = () => {
  tender.value.files.push({
    id: null,
    description: "",
    name: ".",
    code: ".",
    size: "",
    file: null,
    mimeType: "",
  });
};

const addGuaranty = () => {
  tender.value.guaranties.push({
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
    tenderId: props.id,
  });
};

const removeAttachedFileForm = async () => {
  if (deleteFileModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteFileModal.value.activeIndex;
    const [data, error] = await detachFileTender(
      tender.value.id,
      deleteFileModal.value.activeId
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      tender.value.files.splice(deleteFileModal.value.activeIndex, 1);
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else tender.value.files.splice(deleteFileModal.value.activeIndex, 1);
  deleteFileModal.value.visible = false;
};

const deleteGuaranty = async () => {
  if (deleteGuarantyModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteGuarantyModal.value.activeIndex;
    const [data, error] = await deleteGuarantyTender(
      tender.value.guaranties[deleteGuarantyModal.value.activeIndex].id
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      tender.value.guaranties.splice(deleteGuarantyModal.value.activeIndex, 1);
      showSuccessNotification("فایل پیوست با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else
    tender.value.guaranties.splice(deleteGuarantyModal.value.activeIndex, 1);
  deleteGuarantyModal.value.visible = false;
};

const onAttachedFileChanged = (file: FileList, index) => {
  if (file[0]) {
    tender.value.files[index].id = null;
    tender.value.files[index].file = file[0];
    tender.value.files[index].mimeType = "." + mime.extension(file[0].type);
    if (!tender.value.files[index].description)
      tender.value.files[index].description = file[0].name;
    tender.value.files[index].size = file[0].size.toString();
  }
};

async function downloadAttachFile(file, index: number) {
  loadings.value.downloadBtnLoading = index;
  await downloadFile(file, file.id);
  loadings.value.downloadBtnLoading = null;
}

async function uploadAttachFile() {
  let isSuccess = true;
  for (let i = 0; i < tender.value.files.length; i++) {
    if (!tender.value.files[i].id) {
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
      formData.append("file", tender.value.files[i].file);

      const [data, error] = await attachFileTender(
        tender.value.id,
        tender.value.files[i].description,
        tender.value.files[i].code,
        tender.value.files[i].name,
        formData,
        config
      );
      loadings.value.uploadingProgress.progress = 0;
      if (!data?.success) {
        showErrorNotification(data?.message || error.message);
        isSuccess = false;
        break;
      } else tender.value.files[i].id = data.data;
    }
  }

  loadings.value.uploadingProgress.activeIndex = null;
  if (isSuccess) {
    showSuccessNotification(
      "مناقصه با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
    );
  }
}

async function sendGuaranty() {
  let isSuccess = true;
  for (let i = 0; i < tender.value.guaranties.length; i++) {
    if (!tender.value.guaranties[i].id) {
      addGuarantyTender(tender.value.guaranties[i]);
    } else {
      editGuarantyTender(tender.value.guaranties[i]);
    }
  }

  loadings.value.uploadingProgress.activeIndex = null;
  if (isSuccess) {
    showSuccessNotification(
      "مناقصه با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
    );
  }
}

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = props.id
    ? await editTender(tender.value)
    : await addTender(tender.value);
  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    tender.value.id = data.data.id;
    await uploadAttachFile();
    await sendGuaranty();
    router.back();
  }
  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (
    tender.value.company === "" ||
    tender.value.company === null ||
    tender.value.code === "" ||
    tender.value.code === null ||
    tender.value.count === "" ||
    tender.value.count === null ||
    tender.value.reciveDate === "" ||
    tender.value.reciveDate === null ||
    tender.value.tenderDate === "" ||
    tender.value.tenderDate === null
  )
    isValid = false;

  if (isValid) return true;
  else {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
};

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;

  const [data, error] = await getTenderDetail(id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else tender.value = data.data;

  loadings.value.inputLoading = false;
}

onMounted(() => {
  if (props.id) fetchDetail(props.id);
});
</script>

<template>
  <div>
    <KsUpdateForm
      :btn-loading="loadings.btnLoading"
      :is-edit="!!props.id"
      form-title="مناقصه"
      @formSubmit="sendRequest">
      <template #formIcon>
        <TagsIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #form>
        <div
          v-if="!loadings.inputLoading"
          key="8"
          class="col-12 col-md-10 mb-4">
          <KsTextArea
            v-model="tender.description"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="موضوع مناقصه" />
        </div>
        <div
          v-if="!loadings.inputLoading"
          key="1"
          class="col-12 col-md-10 mb-5">
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
            <KsInput
              v-model="tender.company"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کارفرمای اصلی *" />

            <KsInput
              v-model="tender.subCompany"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کارفرمای فرعی (استعلام قیمت)" />
          </div>
        </div>

        <div
          v-if="!loadings.inputLoading"
          key="2"
          class="col-12 col-md-10 mb-5">
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
            <KsInput
              v-model="tender.code"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="کد مناقصه *" />

            <KsInput
              v-model="tender.count"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="تعداد سفارش *" />

            <KsInput
              v-model="tender.pmrNo"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="PMR NO" />
          </div>
        </div>

        <div
          v-if="!loadings.inputLoading"
          key="3"
          class="col-12 col-md-10 mb-5">
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
            <KsDatePicker
              v-model="tender.reciveDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              element-tag="start-date"
              placeholder="تاریخ دریافت مناقصه *" />

            <KsDatePicker
              v-model="tender.tenderDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              element-tag="end-date"
              placeholder="مهلت پاسخ به کارفرما *" />

            <KsDatePicker
              v-model="tender.techSendDate"
              element-tag="start-response"
              placeholder="تاریخ ارسال پیشنهاد فنی به کارفرما"
              :loading="loadings.inputLoading"
              class="flex-fill" />

            <KsDatePicker
              v-model="tender.financeSendDate"
              element-tag="end-response"
              placeholder="تاریخ ارسال پیشنهاد مالی به کارفرما"
              :loading="loadings.inputLoading"
              class="flex-fill" />
          </div>
        </div>

        <div
          v-if="!loadings.inputLoading"
          key="6"
          class="col-12 col-md-10 mb-5">
          <KsSelect
            v-model="tender.customerResult"
            class="flex-fill"
            custom-height="52"
            placeholder="نتیجه مناقصه کارفرما"
            simple-placeholder
            :options="selectCustomerResult"
            :loading="loadings.inputLoading" />
        </div>

        <div
          v-if="!loadings.inputLoading"
          key="5"
          class="col-12 col-md-10 mb-5">
          <KsTextArea
            v-model="tender.callResult"
            placeholder="شرح پیگیری های انجام شده"
            :loading="loadings.inputLoading"
            class="flex-fill" />
        </div>

        <div
          v-if="!loadings.inputLoading"
          key="7"
          class="col-12 col-md-10 mb-5">
          <KsTextArea
            v-model="tender.result"
            placeholder="لیست شرکت در مناقصه"
            :loading="loadings.inputLoading"
            class="flex-fill" />
        </div>

        <transition-group
          key="tender.personal"
          class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
          mode="out-in"
          name="fade"
          tag="div">
          <hr key="hr" />
          <span key="title" class="text-medium fw-bold">
            :کارشناس مسئول کارفرما
          </span>

          <KsUploadProgress>
            <div key="1" class="col-12 col-lg-12 col-md-10 mb-5">
              <div
                class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                <KsInput
                  v-model="tender.customerName"
                  placeholder="نام و نام خانوادگی"
                  :loading="loadings.inputLoading"
                  class="mt-5 col-md-6" />
                <KsInput
                  v-model="tender.customerRole"
                  placeholder="سمت"
                  :loading="loadings.inputLoading"
                  class="mt-5 col-md-6" />
              </div>
            </div>

            <div key="1" class="col-12 col-lg-12 col-md-10 mb-5">
              <div
                class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                <KsInput
                  v-model="tender.customerTel"
                  placeholder="تلفن تماس"
                  :loading="loadings.inputLoading"
                  class="mt-5 col-md-6" />
                <KsInput
                  v-model="tender.customerCode"
                  placeholder="شماره داخلی"
                  :loading="loadings.inputLoading"
                  class="mt-5 col-md-6" />
              </div>
            </div>

            <div key="1" class="col-12 col-lg-12 col-md-10 mb-5">
              <div
                class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
                <KsInput
                  v-model="tender.customerMobile"
                  placeholder="تلفن همراه"
                  :loading="loadings.inputLoading"
                  class="mt-5 col-md-6" />
              </div>
            </div>
          </KsUploadProgress>
        </transition-group>

        <transition-group
          key="tender.files"
          class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
          mode="out-in"
          name="fade"
          tag="div">
          <hr key="hr" />
          <span key="title" class="text-medium fw-bold">
            :فایل های پیوست مناقصه
          </span>

          <KsUploadProgress
            v-for="(file, index) in tender.files"
            :key="index"
            :is-uploading="loadings.uploadingProgress.activeIndex === index"
            :progress="loadings.uploadingProgress.progress">
            <div
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-3 mt-3">
              <div class="d-flex flex-row-reverse align-items-center gap-3">
                <KsButton
                  :loading="loadings.deleteBtnLoading === index"
                  color="warn"
                  fixed-width-height="52"
                  theme="flat"
                  tooltip="حذف"
                  @click="openDeleteFileModal(file.id, index)">
                  <template #content>
                    <TimesIcon :fill="Colors.warn" width="18" />
                  </template>
                </KsButton>
                <KsFilePicker
                  :file-size="file.size"
                  :has-file="!!file.id || !!file.file"
                  :height="52"
                  class="flex-fill"
                  icon-size="20"
                  @fileChange="(val) => onAttachedFileChanged(val, index)" />
              </div>
              <div
                class="d-flex flex-fill flex-row-reverse align-items-center gap-3">
                <KsSelect
                  v-model="file.description"
                  class="flex-fill"
                  custom-height="52"
                  placeholder="عنوان فایل پیوست"
                  simple-placeholder
                  :options="selectFileRoll" />
                <KsButton
                  key="download-btn"
                  :disabled="!file.id"
                  :loading="loadings.downloadBtnLoading === index"
                  color="primary"
                  fixed-width-height="52"
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
          key="tender.guaranties"
          class="col-12 col-md-10 mb-4 d-flex flex-column gap-3"
          mode="out-in"
          name="fade"
          tag="div">
          <hr key="hr" />
          <span key="title" class="text-medium fw-bold"> :ضمانت نامه </span>

          <KsUploadProgress
            v-for="(guaranty, index) in tender.guaranties"
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
                    element-tag="Tenderstart-issuing" />
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
                    element-tag="startTender-sent" />
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
                    element-tag="cancelTender-date" />
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
                    element-tag="endTender-list" />
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
              key="add-guaranty-list"
              color="primary"
              fixed-width-height="52"
              tooltip="گارانتی جدید"
              @click="addGuaranty()">
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
      @submit="removeAttachedFileForm" />
    <DeleteModal
      v-model="deleteGuarantyModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteGuarantyModal.visible = false"
      @submit="deleteGuaranty" />
  </div>
</template>
