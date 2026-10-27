<script lang="ts" setup>
import type { Ref } from "vue";
import { computed, onMounted, ref } from "vue";
import type User from "@/types/User";
import type UserRole from "@/types/UserRole";
import type AttachFile from "@/types/AttachFile";
import { useRouter } from "vue-router";
import {
  addUser,
  adminResetPassword,
  editUser,
  editUserProfilePhoto,
  getRoleGrants,
  getUserDetail,
  getUserRoleList,
} from "@/api/UserAPIs";
import { getDocument } from "@/api/DocumentAPIs";
import { failureText } from "@/composables/apiMessage";
import { currentUserId, roleTitles, SensitiveRoles } from "@/composables/roles";
import { passwordProblem, passwordRulesText } from "@/composables/session";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showErrorNotification from "@/composables/showErrorNotification";
import showWarnNotification from "@/composables/showWarnNotification";
import useNotification from "@/components/common/KsNotification/useNotification";

import OwnPasswordForm from "@/components/account/OwnPasswordForm.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import KsModal from "@/components/common/KsModal.vue";
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

const userRoleList: Ref<UserRole[]> = ref([]);

/** The ids of the roles the user has now (edit page). The form shows and changes the first one; the others stay as they are. */
const currentRoleIds: Ref<number[]> = ref([]);

/** Sensitive roles of this user that wait for a second PMO user (edit page). */
const pendingGrants: Ref<any[]> = ref([]);

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
  selectLoading: true,
  photoLoading: false,
});

/** A PMO user edits the own record: the API refuses to change the own roles, and the password is changed with the current one. */
const isOwnRecord = computed(() => !!props.id && Number(props.id) === currentUserId());

const isEmpty = (value: unknown) => value === "" || value === null || value === undefined;

const titleOfRole = (name: string) => roleTitles[String(name || "").toUpperCase()] || name;

// ---------------------------------------------------------------- roles

const selectedRole = computed(() =>
  userRoleList.value.find((role) => Number(role.id) === Number(user.value.roleIds))
);

/** The chosen role is a sensitive one that the user does not have yet: the API only records a request for a second PMO user. */
const needsSecondApproval = computed(
  () =>
    !!selectedRole.value &&
    SensitiveRoles.includes(String(selectedRole.value.name || "").toUpperCase()) &&
    !currentRoleIds.value.includes(Number(selectedRole.value.id))
);

/** The role ids the API gets: the chosen role, and (edit page) the roles the form does not show, unchanged. */
function roleIdsToSend(): number[] {
  const chosen = isEmpty(user.value.roleIds) ? null : Number(user.value.roleIds);
  if (!props.id) return chosen === null ? [] : [chosen];
  // the own roles cannot be changed (the API answers 403 when the list differs), so the list is sent as it is
  if (isOwnRecord.value || chosen === null) return [...currentRoleIds.value];
  // another user: the chosen role takes the place of the first one, the further roles stay
  return [chosen, ...currentRoleIds.value.slice(1).filter((id) => id !== chosen)];
}

/** The sentence the API starts with when a sensitive role is only requested (AccountServiceMessages.SensitiveRolePending). */
const PENDING_TEXT = "نقش‌های حساس پس از تأیید";

/** The answer of a save says that a sensitive role waits for a second PMO user: tell it, with the Persian role titles. */
function announcePendingRoles(message: unknown) {
  if (typeof message !== "string" || !message.includes(PENDING_TEXT)) return;
  const names = message
    .slice(message.lastIndexOf(":") + 1)
    .split("،")
    .map((name) => name.trim())
    .filter(Boolean);
  const titles = names.map(titleOfRole).join("، ");
  useNotification.addNotification({
    title: "نقش در انتظار تأیید",
    text:
      `نقش حساس «${titles || "؟"}» هنوز به کاربر داده نشده است؛ یک مدیر (PMO) دیگر باید آن را تأیید کند ` +
      "(فهرست کاربران، بخش «نقش‌های حساس در انتظار تأیید»).",
    color: "warn",
    duration: 15000,
  });
}

async function loadPendingGrants() {
  const [data, error] = await getRoleGrants();
  // only a hint on this page: the panel of the user list shows a failure
  if (error || !data?.success) return;
  pendingGrants.value = (Array.isArray(data.data) ? data.data : []).filter(
    (grant) => Number(grant.userId) === Number(props.id)
  );
}

// ---------------------------------------------------------------- temporary password (PMO sets it for another user)

const resetPassword = ref({ temporaryPassword: "" });

const resetDialog = ref({
  visible: false,
  step: "confirm" as "confirm" | "result",
  loading: false,
  /** The password the API made. It is shown once and removed when the dialog closes. */
  temporaryPassword: "",
  copied: false,
});

function askResetPassword() {
  const typed = resetPassword.value.temporaryPassword.trim();
  if (typed) {
    const problem = passwordProblem(typed);
    if (problem) {
      showWarnNotification(problem);
      return;
    }
  }
  resetDialog.value = { visible: true, step: "confirm", loading: false, temporaryPassword: "", copied: false };
}

function closeResetDialog() {
  resetDialog.value = { visible: false, step: "confirm", loading: false, temporaryPassword: "", copied: false };
}

async function sendResetPassword() {
  if (resetDialog.value.loading) return;
  resetDialog.value.loading = true;
  const typed = resetPassword.value.temporaryPassword.trim();
  const [data, error] = await adminResetPassword(props.id, typed || null);
  resetDialog.value.loading = false;
  if (error || !data?.success) {
    closeResetDialog();
    showErrorNotification(failureText(data, error, "بازنشانی رمز عبور انجام نشد."));
    return;
  }
  // the API returns the password only when it made it; the one the admin typed is not sent back
  resetDialog.value.temporaryPassword = data.data?.temporaryPassword || "";
  resetDialog.value.step = "result";
  resetPassword.value.temporaryPassword = "";
}

/** Copies a text with the clipboard API, or (page opened over plain http on the company network) with the old way. */
async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    /* the old way below */
  }
  const area = document.createElement("textarea");
  area.value = text;
  area.setAttribute("readonly", "");
  area.style.position = "fixed";
  area.style.top = "-1000px";
  document.body.appendChild(area);
  area.select();
  let copied = false;
  try {
    copied = document.execCommand("copy");
  } catch {
    copied = false;
  }
  area.remove();
  return copied;
}

async function copyTemporaryPassword() {
  if (await copyText(resetDialog.value.temporaryPassword)) {
    resetDialog.value.copied = true;
    showSuccessNotification("رمز در حافظه کپی شد.");
  } else {
    showWarnNotification("کپی انجام نشد؛ رمز را با ماوس انتخاب و کپی کنید.");
  }
}

// ---------------------------------------------------------------- the form

const onUserProfilePhotoPickerChange = (files: FileList) => {
  if (files[0]) {
    userProfilePhoto.value.file = files[0];
    userProfilePhoto.value.mimeType = "." + mime.extension(files[0].type);
    userProfilePhoto.value.src = URL.createObjectURL(files[0]);
    userProfilePhoto.value.id = null;
  }
};

async function updateUserProfilePhoto() {
  if (!userProfilePhoto.value.id && userProfilePhoto.value.file) {
    loadings.value.photoLoading = true;
    const formData = new FormData();
    formData.append("file", userProfilePhoto.value.file);
    const [data, error] = await editUserProfilePhoto(user.value.id, formData);
    const isSuccess = !error && data?.success;
    if (!isSuccess) showErrorNotification(failureText(data, error, "تصویر پروفایل ذخیره نشد."));
    loadings.value.photoLoading = false;
  }
}

async function sendRequest() {
  if (loadings.value.btnLoading) return;
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  try {
    const payload: any = { ...user.value, roleIds: roleIdsToSend() };
    delete payload.roles;
    // the password of a user is never sent with the other details; an existing user gets a temporary password below
    if (props.id) {
      delete payload.password;
      delete payload.confirmPassword;
    }
    const [data, error] = props.id ? await editUser(payload) : await addUser(payload);

    if (error || !data?.success) {
      showErrorNotification(failureText(data, error, "ذخیره کاربر انجام نشد."));
      return;
    }
    if (!props.id) user.value.id = data.data.id;
    await updateUserProfilePhoto();
    showSuccessNotification(
      "کاربر با موفقیت " + (props.id ? "ویرایش شد" : "افزوده شد")
    );
    // a sensitive role is not given at once: the answer says that it waits for a second PMO user
    announcePendingRoles(data.message);
    router.back();
  } finally {
    loadings.value.btnLoading = false;
  }
}

const validateForm = () => {
  const u = user.value;
  const adding = !props.id;
  const missing =
    [u.userName, u.name, u.lastName, u.email, u.phoneNumber, u.birthDate, u.nationalCode].some(isEmpty) ||
    // a user whose sensitive role still waits for approval has no role: such a user can be edited without choosing one
    (adding && (isEmpty(u.roleIds) || isEmpty(u.password) || isEmpty(u.confirmPassword)));

  if (missing) {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
  if (adding) {
    const problem = passwordProblem(u.password);
    if (problem) {
      showWarnNotification(problem);
      return false;
    }
    if (u.password !== u.confirmPassword) {
      showWarnNotification("رمز عبور و تکرار آن یکسان نیستند.");
      return false;
    }
  }
  return true;
};

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;
  loadings.value.photoLoading = true;
  const [data, error] = await getUserDetail(id);
  loadings.value.inputLoading = false;
  if (error || !data?.success) {
    showErrorNotification(failureText(data, error, "اطلاعات کاربر خوانده نشد."));
    router.back();
  } else {
    user.value = data.data;
    // a user can have no role (a sensitive role that waits for approval is not given yet)
    currentRoleIds.value = (data.data.roles || []).map((role: any) => Number(role.id));
    user.value.roleIds = data.data.roles?.[0]?.id ?? null;
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
  if (error || !data?.success) {
    showErrorNotification(failureText(data, error, "فهرست نقش‌ها خوانده نشد."));
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
  if (props.id) {
    await fetchDetail(props.id);
    loadPendingGrants();
  }
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
              :disabled="isOwnRecord"
              :loading="loadings.selectLoading"
              :options="userRoleList"
              class="flex-fill"
              label-prop-name="description"
              placeholder="سمت"
              value-prop-name="id" />
          </div>
          <!-- roles: what the API does with the choice -->
          <p v-if="isOwnRecord" class="text-xSmall mt-3 mb-0">
            تغییر سمت خودتان مجاز نیست؛ مدیر (PMO) دیگری باید این کار را انجام دهد.
          </p>
          <p v-if="needsSecondApproval" class="text-xSmall color-warn mt-3 mb-0">
            «{{ titleOfRole(selectedRole.name) }}» نقش حساس است. بعد از ذخیره، این نقش تا زمانی که یک مدیر (PMO) دیگر آن را
            تأیید نکند به کاربر داده نمی‌شود<template v-if="props.id">
              و سمت فعلی کاربر همین حالا برداشته می‌شود</template>.
          </p>
          <div v-if="pendingGrants.length" class="text-xSmall color-warn mt-3 mb-0">
            برای این کاربر نقش حساس در انتظار تأیید یک مدیر (PMO) دیگر است:
            <span v-for="(grant, index) in pendingGrants" :key="grant.id" class="fw-bold">
              {{ index ? "، " : "" }}{{ titleOfRole(grant.role) }}
            </span>
            (در فهرست کاربران، بخش «نقش‌های حساس در انتظار تأیید»).
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
            v-if="!props.id"
            class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="user.password"
              :loading="loadings.inputLoading"
              class="flex-fill"
              input-type="password"
              placeholder="رمز عبور" />
            <KsInput
              v-model="user.confirmPassword"
              :loading="loadings.inputLoading"
              class="flex-fill"
              input-type="password"
              placeholder="تکرار رمز عبور" />
          </div>
          <p v-if="!props.id" class="text-xSmall mt-3 mb-0">
            {{ passwordRulesText }}
          </p>
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

    <!-- another user: the PMO sets a temporary password, the user must change it at the next sign-in -->
    <KsUpdateForm
      v-if="props.id && !isOwnRecord"
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
          <p class="text-small">
            رمز عبور کاربر را نمی‌توان مستقیم عوض کرد. با «بازنشانی رمز عبور» یک رمز موقت تعیین می‌شود، ورود فعلی کاربر
            بسته می‌شود و کاربر باید در ورود بعدی خودش رمز جدید انتخاب کند.
          </p>
          <div class="d-flex flex-column flex-sm-row-reverse align-items-sm-center gap-5 gap-sm-3 mt-5">
            <KsInput
              v-model="resetPassword.temporaryPassword"
              class="flex-fill"
              ltr
              placeholder="رمز موقت (اختیاری)" />
            <KsButton
              color="warn"
              theme="flat"
              title="بازنشانی رمز عبور"
              @click="askResetPassword" />
          </div>
          <p class="text-xSmall mt-3 mb-0">
            اگر رمز موقت را وارد نکنید، سامانه یکی می‌سازد و فقط یک بار نشان می‌دهد. {{ passwordRulesText }}
          </p>
        </div>
      </template>
    </KsUpdateForm>

    <!-- the own record: the password is changed with the current one -->
    <KsUpdateForm
      v-if="props.id && isOwnRecord"
      :has-back-btn="false"
      :has-submit-btn="false"
      :is-edit="true"
      class="mt-3"
      form-title="رمز عبور من">
      <template #formIcon>
        <LockIcon :fill="Colors.black" class="ms-3" width="20" />
      </template>
      <template #form>
        <div key="1" class="col-12 col-md-10 mb-5">
          <OwnPasswordForm />
        </div>
      </template>
    </KsUpdateForm>

    <KsModal v-model="resetDialog.visible" blur>
      <div class="d-flex flex-column gap-3 p-2" style="max-width: 460px">
        <template v-if="resetDialog.step === 'confirm'">
          <span class="fw-bold text-medium">بازنشانی رمز عبور</span>
          <span class="text-small">
            رمز عبور «{{ user.name }} {{ user.lastName }}» باطل می‌شود و ورود فعلی او در همه‌ی دستگاه‌ها بسته می‌شود.
            یک رمز موقت تعیین می‌شود که کاربر باید در ورود بعدی آن را با رمز جدید خودش عوض کند.
          </span>
          <div class="d-flex gap-2 justify-content-end">
            <KsButton
              :disabled="resetDialog.loading"
              color="dark"
              theme="flat"
              title="انصراف"
              @click="closeResetDialog" />
            <KsButton
              :loading="resetDialog.loading"
              color="warn"
              theme="flat"
              title="بازنشانی"
              @click="sendResetPassword" />
          </div>
        </template>
        <template v-else>
          <span class="fw-bold text-medium">رمز موقت تعیین شد</span>
          <template v-if="resetDialog.temporaryPassword">
            <span class="text-small">
              رمز موقت «{{ user.userName }}» این است. این رمز فقط همین یک بار نشان داده می‌شود؛ آن را به کاربر بدهید.
            </span>
            <div class="temporary-password" dir="ltr">{{ resetDialog.temporaryPassword }}</div>
            <KsButton
              :color="resetDialog.copied ? 'success' : 'primary'"
              :title="resetDialog.copied ? 'کپی شد' : 'کپی رمز'"
              theme="flat"
              @click="copyTemporaryPassword" />
          </template>
          <span v-else class="text-small">
            رمز موقتی که وارد کردید برای کاربر ثبت شد؛ آن را به کاربر بدهید.
          </span>
          <span class="text-small fw-bold">
            کاربر باید در ورود بعدی این رمز موقت را با رمز جدید خودش عوض کند.
          </span>
          <div class="d-flex justify-content-end">
            <KsButton color="dark" theme="flat" title="بستن" @click="closeResetDialog" />
          </div>
        </template>
      </div>
    </KsModal>
  </div>
</template>

<style lang="scss" scoped>
.temporary-password {
  font-family: monospace;
  font-size: 1.15rem;
  letter-spacing: 0.05em;
  padding: 0.6rem 0.8rem;
  border-radius: 8px;
  border: 1px dashed rgba(var(--primary), 0.6);
  text-align: center;
  user-select: all;
  word-break: break-all;
}
</style>
