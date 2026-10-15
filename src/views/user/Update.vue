<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import type User from "@/types/User";
import type UserRole from "@/types/UserRole";
import type AttachFile from "@/types/AttachFile";
import { useRouter } from "vue-router";
import {
  addUser,
  changeUserPassword,
  editUser,
  editUserProfilePhoto,
  getUserDetail,
  getUserRoleList,
} from "@/api/UserAPIs";
import { getDocument } from "@/api/DocumentAPIs";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showErrorNotification from "@/composables/showErrorNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import KsInput from "@/components/common/KsInput.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import LockIcon from "@/components/icons/LockIcon.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsAvatar from "@/components/common/KsAvatar.vue";
import UserIcon from "@/components/icons/UserIcon.vue";
import { Colors } from "@/assets/colors";
import mime from "mime-types";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();

const user: Ref<User> = ref({
  id: props.id,
  userName: "",
  name: "",
  lastName: "",
  email: "",
  roleIds: null,
  password: "",
  confirmPassword: "",
  phoneNumber: "",
  birthDate: "",
  nationalCode: "",
});

const userProfilePhoto: Ref<AttachFile> = ref({
  id: "",
  file: null,
  src: "",
});

const password = ref({
  UserId: props.id,
  NewPassword: "",
});

const userRoleList: Ref<UserRole[]> = ref([]);

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  selectLoading: true,
  photoLoading: false,
});

const onUserProfilePhotoPickerChange = (files: FileList) => {
  if (files[0]) {
    userProfilePhoto.value.file = files[0];
    userProfilePhoto.value.mimeType = "." + mime.extension(files[0].type);
    userProfilePhoto.value.src = URL.createObjectURL(files[0]);
    userProfilePhoto.value.id = null;
  }
};

async function sendChangePasswordRequest() {
  loadings.value.btnLoading = true;
  const [data, error] = await changeUserPassword(password.value);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    password.value.NewPassword = "";
    showSuccessNotification(
      "رمز عبور کاربر با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
    );
  }

  loadings.value.btnLoading = false;
}

async function updateUserProfilePhoto() {
  if (!userProfilePhoto.value.id && userProfilePhoto.value.file) {
    loadings.value.photoLoading = true;
    const formData = new FormData();
    formData.append("file", userProfilePhoto.value.file);
    const [data, error] = await editUserProfilePhoto(user.value.id, formData);
    const isSuccess = !error && data?.success;
    if (!isSuccess) showErrorNotification(data?.message || error.message);
    loadings.value.photoLoading = false;
  }
}

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const role = user.value.roleIds;
  user.value.roleIds = [role];
  delete user.value.roles;
  const [data, error] = props.id
    ? await editUser(user.value)
    : await addUser(user.value);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    if (!props.id) user.value.id = data.data.id;
    await updateUserProfilePhoto();
    showSuccessNotification(
      "کاربر با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
    );
    router.back()
  }

  user.value.roleIds = role;
  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (
    user.value.userName === "" ||
    user.value.userName === null ||
    user.value.name === "" ||
    user.value.name === null ||
    user.value.lastName === "" ||
    user.value.lastName === null ||
    user.value.email === "" ||
    user.value.email === null ||
    user.value.roleIds === "" ||
    user.value.roleIds === null ||
    user.value.password === "" ||
    user.value.password === null ||
    user.value.confirmPassword === "" ||
    user.value.confirmPassword === null ||
    user.value.phoneNumber === "" ||
    user.value.phoneNumber === null ||
    user.value.birthDate === "" ||
    user.value.birthDate === null ||
    user.value.nationalCode === "" ||
    user.value.nationalCode === null
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
  loadings.value.photoLoading = true;
  const [data, error] = await getUserDetail(id);
  loadings.value.inputLoading = false;
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else {
    user.value = data.data;
    user.value.roleIds = data.data.roles[0].id;
    if (data.data.profilePhotoId) {
      userProfilePhoto.value.id = data.data.profilePhotoId;
      await fetchUserProfilePhoto();
    }
  }
  loadings.value.photoLoading = false;
}

async function fetchUserRoleList() {
  loadings.value.selectLoading = true;

  const [data, error] = await getUserRoleList();
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else userRoleList.value = data.data;

  loadings.value.selectLoading = false;
}

async function fetchUserProfilePhoto() {
  const [data, error] = await getDocument(userProfilePhoto.value.id);
  const isSuccess = !error;
  if (isSuccess) userProfilePhoto.value.src = URL.createObjectURL(data);
}

onMounted(async () => {
  if (props.id) await fetchDetail(props.id);
  await fetchUserRoleList();
});
</script>

<template>
  <div>
    <KsUpdateForm
      :btn-loading="loadings.btnLoading"
      :is-edit="!!props.id"
      form-title="کاربر"
      @formSubmit="sendRequest">
      <template #formIcon>
        <UserIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #form>
        <div key="1" class="col-12 col-md-10 mb-4">
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3">
            <KsInput
              v-model="user.name"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="نام" />
            <KsInput
              v-model="user.lastName"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="نام خانوادگی" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="user.userName"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="نام کاربری" />
            <KsDatePicker
              v-model="user.birthDate"
              :loading="loadings.inputLoading"
              class="flex-fill"
              element-tag="birth-date"
              placeholder="تاریخ تولد" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="user.nationalCode"
              :loading="loadings.inputLoading"
              class="flex-fill"
              input-type="number"
              placeholder="کد ملی" />
            <KsSelect
              v-model="user.roleIds"
              :loading="loadings.selectLoading"
              :options="userRoleList"
              class="flex-fill"
              label-prop-name="description"
              placeholder="سمت"
              value-prop-name="id" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="user.email"
              :loading="loadings.inputLoading"
              class="flex-fill"
              placeholder="ایمیل" />
            <KsInput
              v-model="user.phoneNumber"
              :loading="loadings.inputLoading"
              class="flex-fill"
              input-type="number"
              placeholder="موبایل" />
          </div>
          <div
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-if="!props.id"
              v-model="user.password"
              :loading="loadings.inputLoading"
              class="flex-fill"
              input-type="password"
              placeholder="رمز عبور" />
            <KsInput
              v-if="!props.id"
              v-model="user.confirmPassword"
              :loading="loadings.inputLoading"
              class="flex-fill"
              input-type="password"
              placeholder="تکرار رمز عبور" />
          </div>
          <div class="d-flex flex-column gap-3 py-4">
            <span class="text-xSmall fw-bold"> :تصویر پروفایل (اختیاری) </span>
            <KsAvatar
              :loading="loadings.photoLoading"
              :media-src="userProfilePhoto.src"
              :size="120"
              accept-media="image/*"
              has-action-buttons
              has-media-picker
              object-fit="contain"
              @mediaChange="onUserProfilePhotoPickerChange" />
          </div>
        </div>
      </template>
    </KsUpdateForm>
    <KsUpdateForm
      v-if="props.id"
      :btn-loading="loadings.btnLoading"
      :has-back-btn="false"
      :has-submit-btn="false"
      :is-edit="!!props.id"
      class="mt-3"
      form-title="رمز عبور">
      <template #formIcon>
        <LockIcon :fill="Colors.black" class="ms-3" width="20" />
      </template>
      <template #form>
        <div key="1" class="col-12 col-md-10 mb-5">
          <div class="d-flex flex-row-reverse align-items-center gap-3">
            <KsInput
              v-model="password.NewPassword"
              class="flex-fill"
              placeholder="رمز عبور جدید" />
            <KsButton
              :loading="loadings.btnLoading"
              color="warn"
              theme="flat"
              title="تغییر رمز عبور"
              @click="sendChangePasswordRequest" />
          </div>
        </div>
      </template>
    </KsUpdateForm>
  </div>
</template>
