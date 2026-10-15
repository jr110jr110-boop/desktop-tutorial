<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import type _Process from "@/types/Process";
import type BuyOrder from "@/types/BuyOrder";
import { getDashboardData } from "@/api/DashboardAPIs";

import KsAccordion from "@/components/common/KsAccordion.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsSkeletonLoading from "@/components/common/KsSkeletonLoading.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import CpuIcon from "@/components/icons/CpuIcon.vue";
import EyeIcon from "@/components/icons/EyeIcon.vue";
import EditIcon from "@/components/icons/EditIcon.vue";
import UserIcon from "@/components/icons/UserIcon.vue";
import ShoppingCartIcon from "@/components/icons/ShoppingCartIcon.vue";
import CodeBranchIcon from "@/components/icons/CodeBranchIcon.vue";
import { Colors } from "@/assets/colors";
import showErrorNotification from "@/composables/showErrorNotification";

interface DashboardCard {
  title: string;
  value: string;
}

const loading = ref(true);

const dashboardCards: Ref<DashboardCard[]> = ref([]);
const userProcessList: Ref<_Process[]> = ref([]);
const userBuyOrderList: Ref<BuyOrder[]> = ref([]);

function fetchDashboardData() {
  loading.value = true;
  setTimeout(async () => {
    const [data, error] = await getDashboardData();
    if (!data?.success) showErrorNotification(data?.message || error.message);
    else {
      if (data.success) {
        dashboardCards.value = data.data.titleValues;
        userProcessList.value = data.data.userProcesses;
        userBuyOrderList.value = data.data.userBuyOrders;
      } else showErrorNotification(data.message);
    }

    loading.value = false;
  }, 1000);
}

onMounted(async () => {
  await fetchDashboardData();
});
</script>

<template>
  <div class="background-color p-3 shadow-sm">
    <transition-group name="fade" tag="div">
      <div v-if="!loading" key="cards" class="row gx-3" dir="rtl">
        <div
          v-for="(dashboardCard, index) in dashboardCards"
          :key="index"
          class="dashboard-card col-12 col-md-6 col-lg-3 mb-4">
          <div
            class="d-flex flex-column gap-4 text-white p-4 radius-12"
            dir="rtl">
            <span class="text-medium text-white fw-bold">
              {{ dashboardCard.title }}
            </span>
            <div class="d-flex justify-content-between align-items-center">
              <span
                :style="
                  dashboardCard.value.length > 4
                    ? 'font-size: 22px'
                    : 'font-size: 38px'
                "
                class="fw-bold text-white IRANYekanFaNum">
                {{ dashboardCard.value }}
              </span>
              <CpuIcon
                v-if="dashboardCard['type'] === 'assembly'"
                fill="#ffffff"
                width="38" />
              <UserIcon
                v-if="dashboardCard['type'] === 'userinfo'"
                fill="#ffffff"
                width="38" />
              <ShoppingCartIcon
                v-if="dashboardCard['type'] === 'buyorder'"
                fill="#ffffff"
                width="38" />
              <CodeBranchIcon
                v-if="dashboardCard['type'] === 'process'"
                fill="#ffffff"
                width="30" />
            </div>

            <div
              class="d-flex gap-2 cursor-pointer"
              @click="$router.push({ name: 'BuyOrderList' })">
              <span class="text-small text-white">مشاهده لیست</span>
              <AngleLeftIcon fill="#fff" width="6" />
            </div>
          </div>
        </div>
      </div>

      <!--    Loading Cards     -->
      <div v-if="loading" key="cardsLoading" class="row gx-3" dir="rtl">
        <div class="dashboard-card col-12 col-md-6 col-lg-3 mb-4">
          <div
            class="d-flex flex-column gap-4 text-white p-4 radius-12"
            dir="rtl">
            <div class="w-50">
              <KsSkeletonLoading
                :row-height="28"
                :row-number="1"
                background-color="#ffffff30" />
            </div>
            <div class="d-flex justify-content-between align-items-center">
              <div class="w-75">
                <KsSkeletonLoading
                  :row-height="42"
                  :row-number="1"
                  background-color="#ffffff30" />
              </div>
              <div style="width: 42px">
                <KsSkeletonLoading
                  :row-height="42"
                  :row-number="1"
                  background-color="#ffffff30" />
              </div>
            </div>

            <div style="width: 104px">
              <KsSkeletonLoading
                :row-height="24"
                :row-number="1"
                background-color="#ffffff30" />
            </div>
          </div>
        </div>
        <div class="dashboard-card col-12 col-md-6 col-lg-3 mb-4">
          <div
            class="d-flex flex-column gap-4 text-white p-4 radius-12"
            dir="rtl">
            <div class="w-50">
              <KsSkeletonLoading
                :row-height="28"
                :row-number="1"
                background-color="#ffffff30" />
            </div>
            <div class="d-flex justify-content-between align-items-center">
              <div class="w-75">
                <KsSkeletonLoading
                  :row-height="42"
                  :row-number="1"
                  background-color="#ffffff30" />
              </div>
              <div style="width: 42px">
                <KsSkeletonLoading
                  :row-height="42"
                  :row-number="1"
                  background-color="#ffffff30" />
              </div>
            </div>

            <div style="width: 104px">
              <KsSkeletonLoading
                :row-height="24"
                :row-number="1"
                background-color="#ffffff30" />
            </div>
          </div>
        </div>
        <div class="dashboard-card col-12 col-md-6 col-lg-3 mb-4">
          <div
            class="d-flex flex-column gap-4 text-white p-4 radius-12"
            dir="rtl">
            <div class="w-50">
              <KsSkeletonLoading
                :row-height="28"
                :row-number="1"
                background-color="#ffffff30" />
            </div>
            <div class="d-flex justify-content-between align-items-center">
              <div class="w-75">
                <KsSkeletonLoading
                  :row-height="42"
                  :row-number="1"
                  background-color="#ffffff30" />
              </div>
              <div style="width: 42px">
                <KsSkeletonLoading
                  :row-height="42"
                  :row-number="1"
                  background-color="#ffffff30" />
              </div>
            </div>

            <div style="width: 104px">
              <KsSkeletonLoading
                :row-height="24"
                :row-number="1"
                background-color="#ffffff30" />
            </div>
          </div>
        </div>
        <div class="dashboard-card col-12 col-md-6 col-lg-3 mb-4">
          <div
            class="d-flex flex-column gap-4 text-white p-4 radius-12"
            dir="rtl">
            <div class="w-50">
              <KsSkeletonLoading
                :row-height="28"
                :row-number="1"
                background-color="#ffffff30" />
            </div>
            <div class="d-flex justify-content-between align-items-center">
              <div class="w-75">
                <KsSkeletonLoading
                  :row-height="42"
                  :row-number="1"
                  background-color="#ffffff30" />
              </div>
              <div style="width: 42px">
                <KsSkeletonLoading
                  :row-height="42"
                  :row-number="1"
                  background-color="#ffffff30" />
              </div>
            </div>

            <div style="width: 104px">
              <KsSkeletonLoading
                :row-height="24"
                :row-number="1"
                background-color="#ffffff30" />
            </div>
          </div>
        </div>
      </div>
    </transition-group>

    <KsAccordion btn-classes="px-2 py-1" class="accordion-btn mt-3">
      <template #header>
        <div
          class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
          dir="rtl">
          <div class="d-flex gap-3">
            <span class="text-medium fw-bold"> فرآیندهای تامین </span>
          </div>
          <KsButton
            v-if="!loading"
            :color="userProcessList.length ? 'primary' : 'dark'"
            custom-padding="5px 12px">
            <template #content>
              <div class="d-flex align-items-center justify-content-center">
                <span class="text-white text-small IRANYekanFaNum">{{
                  userProcessList.length.toString()
                }}</span>
              </div>
            </template>
          </KsButton>
          <div v-if="loading" class="custom-loader position-relative" />
        </div>
      </template>
      <template #content>
        <div class="d-flex flex-column gap-3 p-3">
          <span
            v-if="!userProcessList || !userProcessList.length"
            class="text-xSmall text-center fw-bold">
            لیست فرآیند تامین خالی است
          </span>
          <transition-group
            v-if="userProcessList && userProcessList.length"
            class="d-flex flex-column gap-2"
            name="list"
            tag="div">
            <div
              v-for="(process, indexProcess) in userProcessList"
              :key="indexProcess"
              class="accordion-btn py-1">
              <div
                class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-sm-center"
                dir="rtl">
                <div class="d-flex gap-3 align-items-center pe-3">
                  <span class="text-small">
                    {{ indexProcess + 1 }}.
                    <span class="pe-2">{{ process.title }}</span>
                  </span>
                </div>
                <div
                  class="d-flex gap-1 align-items-center align-self-end ps-2">
                  <KsButton
                    color="secondary"
                    fixed-width-height="42"
                    tooltip="مشاهده اطلاعات"
                    @click="$router.push({ name: 'CardBoardUserProcesses' })">
                    <template #content>
                      <EyeIcon :fill="Colors.white" width="16" />
                    </template>
                  </KsButton>
                </div>
              </div>
            </div>
          </transition-group>
        </div>
      </template>
    </KsAccordion>

    <KsAccordion btn-classes="px-2 py-1" class="accordion-btn mt-3">
      <template #header>
        <div
          class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
          dir="rtl">
          <div class="d-flex gap-3">
            <span class="text-medium fw-bold">
              فرآیندهای تامین، مونتاژ و ساخت
            </span>
          </div>
          <KsButton
            v-if="!loading"
            :color="userBuyOrderList.length ? 'primary' : 'dark'"
            custom-padding="5px 12px">
            <template #content>
              <div class="d-flex align-items-center justify-content-center">
                <span class="text-white text-small">{{
                  userBuyOrderList.length.toString()
                }}</span>
              </div>
            </template>
          </KsButton>
          <div v-if="loading" class="custom-loader position-relative" />
        </div>
      </template>
      <template #content>
        <div class="d-flex flex-column gap-3 p-3">
          <span
            v-if="!userBuyOrderList || !userBuyOrderList.length"
            class="text-xSmall text-center fw-bold">
            لیست فرآیندهای تامین، مونتاژ و ساخت خالی است
          </span>
          <transition-group
            v-if="userBuyOrderList && userBuyOrderList.length"
            class="d-flex flex-column gap-2"
            name="list"
            tag="div">
            <div
              v-for="(buyOrder, indexBuyOrder) in userBuyOrderList"
              :key="indexBuyOrder"
              class="accordion-btn py-1">
              <div
                class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-sm-center"
                dir="rtl">
                <div class="d-flex gap-3 align-items-center pe-3">
                  <span class="text-small"> {{ indexBuyOrder + 1 }}. </span>
                  <span class="text-small color-primary-light">
                    <strong class="color-primary">
                      {{ buyOrder.title ?? "فرآیندهای تامین، مونتاژ و ساخت" }}
                    </strong>
                  </span>
                  <span class="text-small color-primary-light">
                    کد ورود کالا:
                    <strong class="color-primary">
                      {{ buyOrder.enterCommodityCode }}
                    </strong>
                  </span>
                </div>
                <div
                  class="d-flex gap-1 align-items-center align-self-end ps-2">
                  <KsButton
                    color="secondary"
                    fixed-width-height="42"
                    tooltip="مشاهده اطلاعات"
                    @click="$router.push({ name: 'CardBoardUserBuyOrders' })">
                    <template #content>
                      <EyeIcon :fill="Colors.white" width="16" />
                    </template>
                  </KsButton>
                </div>
              </div>
            </div>
          </transition-group>
        </div>
      </template>
    </KsAccordion>
  </div>
</template>

<style lang="scss" scoped>
.dashboard-card {
  &:nth-child(1) .d-flex {
    background: linear-gradient(
      45deg,
      rgb(89, 168, 255) 0%,
      rgb(63, 148, 245) 100%
    );
  }

  &:nth-child(2) .d-flex {
    background: linear-gradient(
      45deg,
      rgb(73, 20, 255) 0%,
      rgb(105, 60, 255) 100%
    );
  }

  &:nth-child(3) .d-flex {
    background: linear-gradient(
      45deg,
      rgb(250, 120, 0) 0%,
      rgb(255, 145, 0) 100%
    );
  }

  &:nth-child(4) .d-flex {
    background: linear-gradient(
      45deg,
      rgb(255, 0, 69) 0%,
      rgb(255, 54, 108) 100%
    );
  }
}

.item-title {
  width: 180px;
  font-weight: bold;
  text-align: end;
}
</style>
