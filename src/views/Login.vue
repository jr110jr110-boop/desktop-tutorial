<script lang="ts" setup>
import type Login from "@/types/Login";
import type { Ref } from "vue";
import { ref } from "vue";
import { changeTemporaryPassword, loginUser } from "@/api/LoginAPIs";
import {
  MUST_CHANGE_PASSWORD,
  passwordProblem,
  passwordRulesText,
  storeSession,
} from "@/composables/session";
import { failureText } from "@/composables/apiMessage";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import KsInput from "@/components/common/KsInput.vue";
import KsCheckBox from "@/components/common/KsCheckBox.vue";
import KsButton from "@/components/common/KsButton.vue";

const loginInfo: Ref<Login> = ref({
  username: "",
  password: "",
  rememberMe: true,
});

// "login": user name + password. "mustChange": the password is a temporary one set by an administrator, so the user chooses a new one first.
const mode: Ref<"login" | "mustChange"> = ref("login");

const newPasswordInfo = ref({
  temporaryPassword: "",
  newPassword: "",
  repeatPassword: "",
});

const submitButtonLoading: Ref<boolean> = ref(false);

/** The sign-in answer for a temporary password: success=false, no tokens, data.code = MUST_CHANGE_PASSWORD (HTTP 200; a real 428 is read the same way). */
const needsNewPassword = (body: any): boolean =>
  body?.data?.code === MUST_CHANGE_PASSWORD || body?.data?.mustChangePassword === true;

/** Keeps the tokens of a sign-in (normal, or after the new password) and opens the application. */
const finishSignIn = (tokens: any) => {
  storeSession({
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    accessList: tokens.accessList,
  });
  showSuccessNotification("شما با موفقیت وارد حساب کاربری خود شدید!");
  window.history.go(0);
};

async function sendLoginRequest() {
  if (submitButtonLoading.value) return;
  if (!validateForm()) return;
  submitButtonLoading.value = true;
  try {
    const [data, error] = await loginUser(loginInfo.value);
    if (needsNewPassword(data ?? error?.response?.data)) {
      startPasswordChange();
      return;
    }
    if (error || !data?.success) {
      showErrorNotification(failureText(data, error, "ورود انجام نشد."));
      return;
    }
    if (data.data?.accessToken) finishSignIn(data.data);
    else showErrorNotification("ورود انجام نشد؛ پاسخ سرور کامل نبود.");
  } finally {
    submitButtonLoading.value = false;
  }
}

const validateForm = () => {
  if (loginInfo.value.username && loginInfo.value.password) return true;
  else {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
};

// ---------------------------------------------------------------- temporary password: choose a new one

/** The password just typed is the temporary one; it is offered again in the form (and can be corrected) next to the new password. */
function startPasswordChange() {
  newPasswordInfo.value = {
    temporaryPassword: loginInfo.value.password,
    newPassword: "",
    repeatPassword: "",
  };
  mode.value = "mustChange";
}

function backToLogin() {
  newPasswordInfo.value = { temporaryPassword: "", newPassword: "", repeatPassword: "" };
  loginInfo.value.password = "";
  mode.value = "login";
}

/** Persian text of the first problem of the form, or null when it can be sent (the API checks the same rules again). */
function newPasswordProblem(): string | null {
  const form = newPasswordInfo.value;
  if (!form.temporaryPassword) return "رمز موقت را وارد کنید.";
  const rule = passwordProblem(form.newPassword);
  if (rule) return rule;
  if (form.newPassword !== form.repeatPassword) return "رمز جدید و تکرار آن یکسان نیستند.";
  if (form.newPassword === form.temporaryPassword) return "رمز جدید نباید با رمز موقت یکی باشد.";
  return null;
}

async function sendNewPassword() {
  if (submitButtonLoading.value) return;
  const problem = newPasswordProblem();
  if (problem) {
    showWarnNotification(problem);
    return;
  }
  submitButtonLoading.value = true;
  try {
    const [data, error] = await changeTemporaryPassword({
      userName: loginInfo.value.username,
      temporaryPassword: newPasswordInfo.value.temporaryPassword,
      newPassword: newPasswordInfo.value.newPassword,
    });
    if (error || !data?.success) {
      showErrorNotification(failureText(data, error, "تغییر رمز انجام نشد."));
      return;
    }
    // the answer is a sign-in: the user is inside with the new password
    if (data.data?.accessToken) finishSignIn(data.data);
    else showErrorNotification("رمز تغییر کرد، اما ورود انجام نشد؛ دوباره وارد شوید.");
  } finally {
    submitButtonLoading.value = false;
  }
}

const onEnter = () => (mode.value === "login" ? sendLoginRequest() : sendNewPassword());
</script>

<template>
  <div
    class="vh-100 vw-100 login-wrapper bg-dark d-flex align-items-center justify-content-center"
    @keyup.enter="onEnter">
    <div
      v-if="mode === 'login'"
      class="login-box background-color d-flex flex-column gap-3 p-4 shadow-lg">
      <KsInput
        v-model="loginInfo.username"
        placeholder="نام کاربری"
        simple-placeholder />
      <KsInput
        v-model="loginInfo.password"
        input-type="password"
        placeholder="رمز عبور"
        simple-placeholder />
      <KsCheckBox
        v-model="loginInfo.rememberMe"
        class="py-2"
        color="primary"
        title="مرا به خاطر بسپار" />
      <KsButton
        :loading="submitButtonLoading"
        color="primary"
        title="ورود"
        @click="sendLoginRequest" />
    </div>

    <div
      v-else
      class="login-box change-box background-color d-flex flex-column gap-3 p-4 shadow-lg">
      <div class="fw-bold fs-6 text-center">تعیین رمز عبور جدید</div>
      <p class="mb-0 small">
        رمز عبور حساب «{{ loginInfo.username }}» را مدیر سامانه به‌صورت موقت تعیین کرده است.
        برای ادامه، یک رمز عبور جدید برای خودتان انتخاب کنید.
      </p>
      <KsInput
        v-model="newPasswordInfo.temporaryPassword"
        input-type="password"
        placeholder="رمز موقت"
        simple-placeholder />
      <KsInput
        v-model="newPasswordInfo.newPassword"
        input-type="password"
        placeholder="رمز عبور جدید"
        simple-placeholder />
      <KsInput
        v-model="newPasswordInfo.repeatPassword"
        input-type="password"
        placeholder="تکرار رمز عبور جدید"
        simple-placeholder />
      <p class="mb-0 small rules">{{ passwordRulesText }}</p>
      <KsButton
        :loading="submitButtonLoading"
        color="primary"
        title="ذخیره و ورود"
        @click="sendNewPassword" />
      <KsButton
        :disabled="submitButtonLoading"
        color="secondary"
        theme="flat"
        title="بازگشت"
        @click="backToLogin" />
    </div>
  </div>
</template>

<style lang="scss" scoped>
.login-wrapper {
  background: url("../assets/img/login-bg.jpg") no-repeat center right;
  background-size: cover;

  .login-box {
    border-radius: 12px;
    min-width: 320px;
  }

  .change-box {
    max-width: 380px;
  }

  .rules {
    opacity: 0.8;
  }
}
</style>
