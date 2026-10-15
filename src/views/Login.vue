<script lang="ts" setup>
import type Login from "@/types/Login";
import type { Ref } from "vue";
import { ref } from "vue";
import { loginUser } from "@/api/LoginAPIs";
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

const submitButtonLoading: Ref<boolean> = ref(false);

const parseJwtToken = (token) => {
  const base64Url = token.split(".")[1];
  const base64 = base64Url.replace(/-/g, "+").replace(/_/g, "/");
  const jsonPayload = decodeURIComponent(
    atob(base64)
      .split("")
      .map((c) => {
        return "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2);
      })
      .join("")
  );

  const finalPayload = JSON.parse(jsonPayload);

  return {
    tokenExpireDate: finalPayload["exp"],
    userRole:
      finalPayload[
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
      ],
  };
};

const onLoginSuccess = (data) => {
  const parsedJwtToken: { tokenExpireDate: string; userRole: string } =
    parseJwtToken(data.accessToken);
  window.localStorage.setItem("token", data.accessToken);
  window.localStorage.setItem("refreshToken", data.refreshToken);
  window.localStorage.setItem(
    "tokenExpireDate",
    parsedJwtToken.tokenExpireDate
  );
  window.localStorage.setItem("userRole", parsedJwtToken.userRole);
  window.localStorage.setItem("accessList", data.accessList);
};

async function sendLoginRequest() {
  if (!validateForm()) return;
  submitButtonLoading.value = true;
  const [data, error] = await loginUser(loginInfo.value);
  const isSuccess = !error && data?.success;
  if (!isSuccess)
    showErrorNotification((data && data.message) || error.message);
  else if (data.data.accessToken) {
    await onLoginSuccess(data.data);
    showSuccessNotification("شما با موفقیت وارد حساب کاربری خود شدید!");
    await window.history.go(0);
  }
  submitButtonLoading.value = false;
}

const validateForm = () => {
  if (loginInfo.value.username && loginInfo.value.password) return true;
  else {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
};
</script>

<template>
  <div
    class="vh-100 vw-100 login-wrapper bg-dark d-flex align-items-center justify-content-center"
    @keyup.enter="sendLoginRequest">
    <div
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
}
</style>
