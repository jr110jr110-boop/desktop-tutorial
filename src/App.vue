<script lang="ts">
import { defineComponent } from "vue";
import { refreshSession, sessionNeedsRefresh } from "@/api/ApiClient";
import { getCurrentUserData } from "@/api/UserAPIs";
import { getDocument } from "@/api/DocumentAPIs";

export default defineComponent({
  data() {
    return {
      isReady: false,
      userProfilePhoto: {
        loading: false,
        src: null
      }
    };
  },
  created() {
    if (localStorage.getItem("token")) this.checkLoginValidation();
    else this.onAppReady();
  },
  methods: {
    async getCurrentUserProfilePhoto(photoId) {
      this.userProfilePhoto.loading = true;
      const [data, error] = await getDocument(photoId);
      const isSuccess = !error && data?.success;
      if (isSuccess) this.userProfilePhoto.src = URL.createObjectURL(data);
      this.userProfilePhoto.loading = false;
    },

    async fetchCurrentUserData() {
      const [data, error] = await getCurrentUserData();
      const isSuccess = !error && data?.success;
      if (isSuccess && data.data.profilePhotoId)
        await this.getCurrentUserProfilePhoto(data.data.profilePhotoId);
    },

    // Start-up check: an access token that has ended (or ends within five minutes) is renewed with the refresh token before the pages ask for data.
    // When the API refuses the refresh token, refreshSession() ends the session (storage cleared, login page, the "انقضای مهلت دسترسی" notice).
    // The app is shown in every case; while it runs the tokens are renewed silently by the API client (see src/api/ApiClient.ts).
    async checkLoginValidation() {
      if (sessionNeedsRefresh()) await refreshSession();
      this.onAppReady();
      if (localStorage.getItem("token")) this.fetchCurrentUserData();
    },

    onAppReady() {
      setTimeout(() => {
        this.isReady = true;
      }, 500);
    }
  }
});
</script>

<script lang="ts" setup>
import { computed, onMounted, reactive, watch } from "vue";
import { useRoute } from "vue-router";

import KsNavbar from "./components/common/KsNavbar.vue";
import KsSidebar from "./components/common/KsSidebar/KsSidebar.vue";
import KsNotification
  from "./components/common/KsNotification/KsNotification.vue";
import AppLoader from "@/layouts/AppLoader.vue";

const state = reactive({
  sidebar: {
    isSidebarExpanded: true,
    isSidebarPinned: true
  },
  screenWidth: screen.width as number,
  userProfilePhotoSrc: null
});

const _route = useRoute();

watch(
  () => _route.name,
  () => {
    if (!state.sidebar.isSidebarPinned) state.sidebar.isSidebarExpanded = false;
  }
);

const hiddenSidebarWidth = computed(() => {
  let width = "0px";
  if (state.screenWidth >= 1200)
    if (state.sidebar.isSidebarExpanded) width = "220px";
    else width = "75px";
  else if (state.screenWidth < 1200 && state.screenWidth >= 991) width = "75px";

  return width;
});

const sidebarWidth = computed(() => {
  let width = "0px";
  if (state.screenWidth < 991 && state.sidebar.isSidebarExpanded)
    width = "100%";
  else if (
    state.screenWidth < 1200 &&
    state.screenWidth >= 991 &&
    state.sidebar.isSidebarExpanded
  )
    width = "380px";
  else if (state.screenWidth >= 991)
    state.sidebar.isSidebarExpanded ? (width = "220px") : (width = "75px");

  return width;
});

const changeSidebarExpandState = (ignorePin: boolean, val: boolean) => {
  if (ignorePin) {
    state.sidebar.isSidebarPinned = false;
    state.sidebar.isSidebarExpanded = val;
  } else
    !state.sidebar.isSidebarPinned && (state.sidebar.isSidebarExpanded = val);
};

const changeSidebarPinState = (val: boolean) => {
  state.sidebar.isSidebarPinned = val;
  state.sidebar.isSidebarExpanded = val;
};

onMounted(() => {
  window.onresize = () => {
    state.screenWidth = screen.width;
    if (screen.width < 1200) changeSidebarPinState(false);
  };
  if (screen.width < 1200) changeSidebarPinState(false);

  if (localStorage.getItem("theme")) {
    document.documentElement.setAttribute(
      "data-theme",
      localStorage.getItem("theme")
    );
  } else {
    localStorage.setItem("theme", "lightMode");
    document.documentElement.setAttribute("data-theme", "");
  }
});
</script>

<template>
  <div>
    <KsNavbar
      v-if="$route.name !== 'Login' && isReady"
      :is-sidebar-expanded="state.sidebar.isSidebarExpanded"
      :screen-width="state.screenWidth"
      @toggleSidebar="
        changeSidebarExpandState(true, !state.sidebar.isSidebarExpanded)
      " />
    <div
      v-if="isReady"
      :class="{ 'd-flex justify-content-between ': state.screenWidth >= 991 }"
      class="w-100 min-w-0">
      <div
        :class="{ 'px-0 px-sm-3 py-3 flex-1': $route.name !== 'Login' }"
        class="min-w-0">
        <router-view v-slot="{ Component, route }">
          <transition mode="out-in" name="fade">
            <component :is="Component" :key="route.path" />
          </transition>
        </router-view>
      </div>

      <transition name="fade">
        <div
          v-if="
            state.screenWidth < 1200 &&
            state.sidebar.isSidebarExpanded &&
            $route.name !== 'Login' &&
            isReady
          "
          class="blur-overlay right-0 top-0 position-fixed"
          @click="state.sidebar.isSidebarExpanded = false" />
      </transition>

      <div
        v-if="$route.name !== 'Login' && isReady"
        :style="'width: ' + hiddenSidebarWidth"
        class="min-w-0 d-print-none" />
      <div
        v-if="$route.name !== 'Login' && isReady"
        :style="'width: ' + sidebarWidth"
        class="shadow-sm sidebar position-fixed right-0 d-print-none"
        style="top: 52px"
        @mouseout="
          state.screenWidth >= 1200 && changeSidebarExpandState(false, false)
        "
        @mouseover="
          state.screenWidth >= 1200 && changeSidebarExpandState(false, true)
        ">
        <KsSidebar
          :avatar="userProfilePhoto.src"
          :avatar-loading="userProfilePhoto.loading"
          :sidebar-state="state.sidebar"
          :window-screen="state.screenWidth"
          @pinSidebar="
            state.sidebar.isSidebarPinned = !state.sidebar.isSidebarPinned
          " />
      </div>
    </div>

    <!--   LOADER   -->
    <AppLoader v-if="!isReady" />
    <KsNotification />
  </div>
</template>
