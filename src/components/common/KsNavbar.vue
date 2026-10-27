<template>
  <div
    class="ks-navbar d-flex flex-row-reverse align-items-center justify-content-between shadow-sm py-2 px-4 sticky-top background-color">
    <div class="d-flex align-items-center gap-1">
      <img :src="Logo" alt="" class="logo" />
      <button
        :class="isSidebarExpanded && 'toClose'"
        class="toggle-btn d-flex flex-column align-items-end ms-3"
        @click="emits('toggleSidebar')">
        <span class="toggle-bar1" />
        <span class="toggle-bar2" />
        <span class="toggle-bar3" />
      </button>
    </div>
    <div class="d-flex gap-0 gap-md-3 align-items-center">
      <KsButton
        color="transparent"
        tooltip="تغییر رمز عبور"
        @click="router.push({ name: 'AccountPassword' })">
        <template #content>
          <div class="d-flex align-items-center gap-2">
            <span class="color-primary-light text-small d-none d-md-inline">
              رمز عبور
            </span>
            <LockIcon :fill="Colors.primaryLight" width="16" />
          </div>
        </template>
      </KsButton>
      <KsButton color="transparent" @click="logout">
        <template #content>
          <div class="d-flex align-items-center gap-2">
            <span class="color-primary-light text-small"> خروج </span>
            <UserIcon :fill="Colors.primaryLight" width="18" />
          </div>
        </template>
      </KsButton>
      <KsButton
        class="me-3"
        color="transparent"
        fixed-width-height="32"
        @click="toggleTheme">
        <template #content>
          <MoonIcon
            v-if="themeIsLight"
            :fill="Colors.primaryLight"
            width="18" />
          <SunIcon v-else :fill="Colors.primaryLight" width="18" />
        </template>
      </KsButton>
      <KsButton color="transparent" fixed-width-height="32">
        <template #content>
          <BellIcon :fill="Colors.primaryLight" width="18" />
        </template>
      </KsButton>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from "vue";
import { useRouter } from "vue-router";

import { Colors } from "@/assets/colors";
import Logo from "@/assets/img/logo.png";
import BellIcon from "@/components/icons/BellIcon.vue";
import MoonIcon from "@/components/icons/MoonIcon.vue";
import SunIcon from "@/components/icons/SunIcon.vue";
import KsButton from "@/components/common/KsButton.vue";
import UserIcon from "@/components/icons/UserIcon.vue";
import LockIcon from "@/components/icons/LockIcon.vue";
import { logoutUser } from "@/api/LoginAPIs";
import { clearSession } from "@/composables/session";

defineProps({
  screenWidth: Number,
  isSidebarExpanded: Boolean,
});

const emits = defineEmits(["toggleSidebar"]);

const router = useRouter();

const themeIsLight = ref(localStorage.getItem("theme") !== "darkMode");

function toggleTheme() {
  themeIsLight.value = !themeIsLight.value;
  localStorage.setItem("theme", themeIsLight.value ? "lightMode" : "darkMode");
  document.documentElement.setAttribute(
    "data-theme",
    themeIsLight.value ? "" : "darkMode"
  );
}

function logout() {
  // The whole session goes (not only the access token), and the server forgets the refresh token so a copy of it cannot be used later.
  // The server call is best effort: the user does not wait for it and the browser session is over in every case.
  const refresh = window.localStorage.getItem("refreshToken");
  const access = window.localStorage.getItem("token");
  clearSession();
  void logoutUser(refresh, access);
  router.push({ name: "Login" });
}
</script>
<style lang="scss" scoped>
.logo {
  width: 82px;
}
</style>
