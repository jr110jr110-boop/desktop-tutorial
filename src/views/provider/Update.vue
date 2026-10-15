<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import type Provider from "@/types/Provider";
import type ProviderContact from "@/types/ProviderContact";
import type ProviderAccount from "@/types/ProviderAccount";
import type ProviderContactType from "@/types/ProviderContactType";
import { useRouter } from "vue-router";
import {
  addProvider,
  addProviderContact,
  addProviderAccount,
  deleteProviderContact,
  deleteProviderAccount,
  editProvider,
  editProviderContact,
  editProviderAccount,
  getProviderCategoryList,
  getProviderDetail,
} from "@/api/ProviderAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import DeleteModal from "@/components/app/DeleteModal.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsTextArea from "@/components/common/KsTextArea.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import HandShakeIcon from "@/components/icons/HandShakeIcon.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();

const provider: Ref<Provider> = ref({
  id: props.id,
  name: "",
  description: "",
  categoryId: null,
  evaluationScore: 0,
});

const providerCategoryList = ref([]);

const providerContacts: Ref<ProviderContact[]> = ref([]);
const providerAccounts: Ref<ProviderAccount[]> = ref([]);

const providerContractTypes: ProviderContactType[] = [
  {
    value: "0",
    title: "تلفن دفتر",
  },
  {
    value: "1",
    title: "فکس دفتر",
  },
  {
    value: "2",
    title: "آدرس دفتر",
  },
  {
    value: "3",
    title: "تلفن گارخانه/کارگاه",
  },
  {
    value: "4",
    title: "فکس گارخانه/کارگاه",
  },
  {
    value: "5",
    title: "آدرس گارخانه/کارگاه",
  },
  {
    value: "6",
    title: "تلفن همراه",
  },
  {
    value: "7",
    title: "ایمیل",
  },
  {
    value: "8",
    title: "وب سایت",
  },
];

const deleteModal = ref({
  visible: false,
  activeIndex: null,
  activeId: null,
});

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  deleteBtnLoading: null,
});

const openDeleteModal = (index, id) => {
  deleteModal.value.activeIndex = index;
  deleteModal.value.activeId = id;
  deleteModal.value.visible = true;
};

const addProviderContactForm = () => {
  providerContacts.value.push({
    data: "",
    name: "",
    type: null,
    description: "",
    providerId: provider.value.id,
  });
};

const addProviderAccountForm = () => {
  providerAccounts.value.push({
    name: "",
    bank: "",
    no: "",
    shaba: "",
    card: "",
    providerId: provider.value.id,
  });
};

const removeProviderContractForm = async () => {
  if (deleteModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteModal.value.activeIndex;
    const [data, error] = await deleteProviderContact(
      providerContacts.value[deleteModal.value.activeIndex].id
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      providerContacts.value.splice(deleteModal.value.activeIndex, 1);
      showSuccessNotification("اطلاعات تماس تامین کننده با موفقیت حذف شد");
    }

    loadings.value.deleteBtnLoading = null;
  } else providerContacts.value.splice(deleteModal.value.activeIndex, 1);
  deleteModal.value.visible = false;
};

const removeProviderAccountForm = async () => {
  if (deleteModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteModal.value.activeIndex;
    const [data, error] = await deleteProviderAccount(
      providerAccounts.value[deleteModal.value.activeIndex].id
    );

    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      providerAccounts.value.splice(deleteModal.value.activeIndex, 1);
      showSuccessNotification(
        "اطلاعات حساب بانکی تامین کننده با موفقیت حذف شد"
      );
    }

    loadings.value.deleteBtnLoading = null;
  } else providerAccounts.value.splice(deleteModal.value.activeIndex, 1);
  deleteModal.value.visible = false;
};

async function updateProviderContacts() {
  let isSuccess = true;
  loadings.value.btnLoading = true;
  for (let i = 0; i < providerContacts.value.length; i++) {
    const [data, error] = providerContacts.value[i].id
      ? await editProviderContact(providerContacts.value[i])
      : await addProviderContact(providerContacts.value[i]);

    if (!data?.success) {
      showErrorNotification(error.message || error.response.data.message);
      isSuccess = false;
      break;
    }
  }

  if (isSuccess) {
    showSuccessNotification(
      "اطلاعات تماس تامین کننده با موفقیت بروزرسانی شدند"
    );
    router.back();
  }
  loadings.value.btnLoading = false;
}

async function updateProviderAccounts() {
  let isSuccess = true;
  loadings.value.btnLoading = true;
  for (let i = 0; i < providerAccounts.value.length; i++) {
    const [data, error] = providerAccounts.value[i].id
      ? await editProviderAccount(providerAccounts.value[i])
      : await addProviderAccount(providerAccounts.value[i]);

    if (!data?.success) {
      showErrorNotification(error.message || error.response.data.message);
      isSuccess = false;
      break;
    }
  }

  if (isSuccess) {
    showSuccessNotification(
      "اطلاعات حساب بانکی تامین کننده با موفقیت بروزرسانی شدند"
    );
    router.back();
  }
  loadings.value.btnLoading = false;
}

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = props.id
    ? await editProvider(provider.value)
    : await addProvider(provider.value);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    if (!props.id) provider.value.id = data.data.id;
    for (let i = 0; i < providerContacts.value.length; i++)
      providerContacts.value[i].providerId = data.data.id;

    for (let i = 0; i < providerAccounts.value.length; i++)
      providerAccounts.value[i].providerId = data.data.id;
  }
  showSuccessNotification(
    "اطلاعات تامین کننده با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
  );
  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (provider.value.name === "" || provider.value.name === null)
    isValid = false;

  if (isValid) return true;
  else {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
};

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;
  const [data, error] = await getProviderDetail(id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else {
    provider.value = data.data;
    let tempProviderContacts = data.data.providerContacts;
    providerAccounts.value = data.data.providerAccounts;
    for (let i = 0; i < data.data.providerContacts.length; i++)
      tempProviderContacts[i].type =
        data.data.providerContacts[i].type.toString();
    providerContacts.value = tempProviderContacts;
  }
  loadings.value.inputLoading = false;
}

const receiveProviderCategory = async () => {
  loadings.value.inputLoading = true;
  const [data, error] = await getProviderCategoryList();
  if (!error && data.success) providerCategoryList.value = data.data;
  loadings.value.inputLoading = false;
};

onMounted(async () => {
  if (props.id) await fetchDetail(props.id);
  await receiveProviderCategory();
});
</script>

<template>
  <div>
    <KsUpdateForm
      :btn-loading="loadings.btnLoading"
      :has-transition-group="false"
      :is-edit="!!props.id"
      form-title="تامین کننده"
      @formSubmit="sendRequest">
      <template #formIcon>
        <HandShakeIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #form>
        <div class="col-12 col-md-10 mb-5">
          <div
            class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3">
            <KsInput
              v-model="provider.name"
              :loading="loadings.inputLoading"
              class="w-100"
              placeholder="نام" />
            <KsInput
              v-model="provider.evaluationScore"
              :loading="loadings.inputLoading"
              class="w-100"
              input-type="number"
              placeholder="امتیاز" />
            <KsSelect
              v-model="provider.categoryId"
              :loading="loadings.inputLoading"
              :options="providerCategoryList"
              label-prop-name="category"
              value-prop-name="id"
              class="w-100"
              input-type="number"
              placeholder="گروه" />
          </div>
          <KsTextArea
            v-model="provider.description"
            :loading="loadings.inputLoading"
            class="w-100 mt-5"
            placeholder="توضیحات" />
        </div>
      </template>
    </KsUpdateForm>

    <KsUpdateForm
      :has-back-btn="false"
      :has-submit-btn="false"
      :has-transition-group="false"
      :is-edit="!!props.id"
      class="mt-3"
      form-title="اطلاعات تماس تامین کننده">
      <template #form>
        <div class="col-12 col-md-10 mb-5">
          <div
            v-for="(providerContact, index) in providerContacts"
            :key="index"
            class="d-flex flex-column gap-3 mb-4">
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
                  @click="openDeleteModal(index, providerContact.id)">
                  <template #content>
                    <TimesIcon :fill="Colors.warn" width="18" />
                  </template>
                </KsButton>
                <KsSelect
                  v-if="!loadings.inputLoading"
                  v-model="providerContact.type"
                  :loading="loadings.inputLoading"
                  :options="providerContractTypes"
                  class="flex-1"
                  placeholder="نوع اطلاعات تماس را انتخاب کنید"
                  simple-placeholder />
              </div>
              <KsInput
                v-model="providerContact.name"
                class="flex-1"
                placeholder="نام مخاطب"
                simple-placeholder />
            </div>
            <KsInput
              v-model="providerContact.data"
              class="flex-1"
              placeholder="اطلاعات تماس"
              simple-placeholder />
            <KsTextArea
              v-model="providerContact.description"
              class="flex-1"
              placeholder="توضیحات (اختیاری)"
              simple-placeholder />
          </div>
          <div class="d-flex gap-2 align-items-center mt-5">
            <KsButton
              key="add-btn"
              color="primary"
              fixed-width-height="42"
              tooltip="اطلاعات تماس جدید"
              @click="addProviderContactForm">
              <template #content>
                <PlusIcon :fill="Colors.white" width="18" />
              </template>
            </KsButton>

            <KsButton
              :disabled="!providerContacts.length || !provider.id"
              :loading="loadings.btnLoading"
              :tooltip="
                !providerContacts.length
                  ? 'نیاز به حداقل یک فرم تماس'
                  : !provider.id
                  ? 'تامین کننده افزوده نشده است'
                  : ''
              "
              color="primary"
              title="بروزرسانی اطلاعات تماس"
              @click="updateProviderContacts" />
          </div>
        </div>
      </template>
    </KsUpdateForm>

    <KsUpdateForm
      :has-back-btn="false"
      :has-submit-btn="false"
      :has-transition-group="false"
      :is-edit="!!props.id"
      class="mt-3"
      form-title="اطلاعات حساب بانکی تامین کننده">
      <template #form>
        <div class="col-12 col-md-10 mb-5">
          <div
            v-for="(providerAccount, index) in providerAccounts"
            :key="index"
            class="d-flex flex-column gap-3 mb-4">
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
                  @click="openDeleteModal(index, providerAccount.id)">
                  <template #content>
                    <TimesIcon :fill="Colors.warn" width="18" />
                  </template>
                </KsButton>
                <KsInput
                  v-model="providerAccount.name"
                  class="flex-1"
                  placeholder="نام"
                  simple-placeholder />
              </div>
              <KsInput
                v-model="providerAccount.bank"
                class="flex-1"
                placeholder="نام بانک"
                simple-placeholder />
            </div>
            <div
              class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-3">
              <KsInput
                v-model="providerAccount.card"
                class="flex-1"
                placeholder="شماره کارت"
                simple-placeholder />
              <KsInput
                v-model="providerAccount.no"
                class="flex-1"
                placeholder="شماره حساب"
                simple-placeholder />
              <KsInput
                v-model="providerAccount.shaba"
                class="flex-1"
                placeholder="شماره شبا"
                append-text="IR"
                :ltr="false"
                simple-placeholder />
            </div>
          </div>
          <div class="d-flex gap-2 align-items-center mt-5">
            <KsButton
              key="add-btn"
              color="primary"
              fixed-width-height="42"
              tooltip="اطلاعات حساب بانکی جدید"
              @click="addProviderAccountForm">
              <template #content>
                <PlusIcon :fill="Colors.white" width="18" />
              </template>
            </KsButton>

            <KsButton
              :disabled="!providerAccounts.length || !provider.id"
              :loading="loadings.btnLoading"
              :tooltip="
                !providerAccounts.length
                  ? 'نیاز به حداقل یک فرم تماس'
                  : !provider.id
                  ? 'تامین کننده افزوده نشده است'
                  : ''
              "
              color="primary"
              title="بروزرسانی اطلاعات حساب بانکی"
              @click="updateProviderAccounts" />
          </div>
        </div>
      </template>
    </KsUpdateForm>

    <!--    DELETE MODAL   -->
    <DeleteModal
      v-model="deleteModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteModal.visible = false"
      @submit="removeProviderContractForm" />
    <!--    DELETE MODAL   -->
    <DeleteModal
      v-model="deleteModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteModal.visible = false"
      @submit="removeProviderAccountForm" />
  </div>
</template>
