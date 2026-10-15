<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, reactive, ref } from "vue";
import { getWorkOrderProgressStatistic } from "@/api/WorkOrderAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import moment from "moment-jalaali";

import KsButton from "@/components/common/KsButton.vue";
import ListLoader from "@/layouts/ListLoader.vue";
import NoDataFound from "@/layouts/NoDataFound.vue";
import KsAccordion from "@/components/common/KsAccordion.vue";
import KsModal from "@/components/common/KsModal.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import EyeIcon from "@/components/icons/EyeIcon.vue";
import ChartLineIcon from "@/components/icons/ChartLineIcon.vue";
import { Colors } from "@/assets/colors";

interface WorkOrderProgressStat {
  assemblyId: number | string;
  title: string;
  progressLevel: number;
  bomProgressLevels: {
    bomId: number | string;
    title: string;
    progressLevel: number;
    processProgressLevels: {
      processId: number;
      title: string;
      progressLevel: number;
    }[];
  }[];
}

const props = defineProps({
  workOrderId: {
    type: String,
    required: true,
  },
});

const state = reactive({
  isLoading: true,
});

const workOrderProgressStatistic: Ref<WorkOrderProgressStat[]> = ref([]);

const previewModal = reactive({
  visible: false,
  process: null,
});

const showPreviewModal = (data) => {
  previewModal.process = data;
  previewModal.visible = true;
};

async function fetchWorkOrderProgressStatistic() {
  state.isLoading = true;
  const [data, error] = await getWorkOrderProgressStatistic(props.workOrderId);
  if (!data?.success)
    showErrorNotification(data?.message || error.message);
  else workOrderProgressStatistic.value = data.data;
  state.isLoading = false;
}

const getWorkOrderProgressColor = (progress: number) => {
  return progress === 0 ? "danger" : progress === 50 ? "warn" : "success";
};

const convertToJalaliDate = (gregorianDate) => {
  return gregorianDate
    ? moment(gregorianDate, "YYYY-MM-DD").format("dddd DD jMMMM jYYYY")
    : "-";
};

onMounted(() => {
  fetchWorkOrderProgressStatistic();
});
</script>

<template>
  <div class="background-color p-3 shadow-sm rounded-3 text-end">
    <!--   Header   -->
    <div
      class="d-flex flex-row-reverse justify-content-between align-items-center">
      <div class="list-title">
        <span class="fw-bold text-large text-color">جزییات درصد پیشرفت</span>
        <ChartLineIcon :fill="Colors.black" class="ms-3" width="24" />
      </div>
      <div class="d-flex gap-3">
        <KsButton
          color="dark"
          fixed-width-height="42"
          theme="flat"
          @click="$router.back()">
          <template #content>
            <AngleLeftIcon :fill="Colors.primary" width="6" />
          </template>
        </KsButton>
      </div>
    </div>
    <hr class="opacity-30" />

    <!--   List   -->
    <transition-group mode="out-in" name="fade">
      <div
        v-if="!state.isLoading && workOrderProgressStatistic.length"
        key="list"
        class="d-flex flex-column gap-1 mt-3">
        <KsAccordion
          v-for="(assembly, indexAssembly) in workOrderProgressStatistic"
          :key="indexAssembly"
          :btn-classes="
            'accordion-' +
            getWorkOrderProgressColor(assembly.progressLevel) +
            ' accordion-btn px-2 py-1'
          "
          :plus-icon-color="getWorkOrderProgressColor(assembly.progressLevel)">
          <template #header>
            <div
              class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
              dir="rtl">
              <div class="d-flex gap-3">
                <span class="text-medium fw-bold accordion-btn-title">{{
                    assembly.title
                  }}</span>
              </div>
              <KsButton
                :color="getWorkOrderProgressColor(assembly.progressLevel)"
                :title="assembly.progressLevel + ' %'"
                bold
                dir="ltr" />
            </div>
          </template>
          <template #content>
            <div class="d-flex flex-column gap-2 pe-3 ps-0 py-1">
              <span
                v-if="
                  !assembly.bomProgressLevels ||
                  !assembly.bomProgressLevels.length
                "
                class="text-xSmall text-center fw-bold text-dark"
                dir="rtl"
              >لیست BOM خالی است</span
              >
              <div
                v-if="
                  assembly.bomProgressLevels &&
                  assembly.bomProgressLevels.length
                "
                class="d-flex flex-column gap-2">
                <KsAccordion
                  v-for="(bom, indexBom) in assembly.bomProgressLevels"
                  :key="indexBom"
                  :btn-classes="
                    'accordion-' +
                    getWorkOrderProgressColor(bom.progressLevel) +
                    ' accordion-btn px-2 py-1'
                  "
                  :plus-icon-color="
                    getWorkOrderProgressColor(bom.progressLevel)
                  ">
                  <template #header>
                    <div
                      class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-center"
                      dir="rtl">
                      <div class="d-flex gap-3">
                        <span class="text-medium fw-bold accordion-btn-title">{{
                            bom.title
                          }}</span>
                      </div>

                      <KsButton
                        :color="getWorkOrderProgressColor(bom.progressLevel)"
                        :title="bom.progressLevel + ' %'"
                        bold
                        dir="ltr" />
                    </div>
                  </template>
                  <template #content>
                    <div class="d-flex flex-column gap-3 ps-0 pe-3 py-1">
                      <span
                        v-if="
                          !bom.processProgressLevels ||
                          !bom.processProgressLevels.length
                        "
                        class="text-xSmall text-center fw-bold"
                      >لیست فرآیند تامین خالی است</span
                      >
                      <transition-group
                        v-if="
                          bom.processProgressLevels &&
                          bom.processProgressLevels.length
                        "
                        class="d-flex flex-column gap-2"
                        name="list"
                        tag="div">
                        <div
                          v-for="(
                            process, indexProcess
                          ) in bom.processProgressLevels"
                          :key="indexProcess"
                          :class="
                            'accordion-' +
                            getWorkOrderProgressColor(process.progressLevel) +
                            ' accordion-btn px-2 py-1'
                          ">
                          <div
                            class="d-flex flex-column flex-sm-row gap-2 justify-content-between align-items-sm-center"
                            dir="rtl">
                            <div class="d-flex gap-3 align-items-center">
                              <span
                                class="text-medium fw-bold accordion-btn-title"
                              >{{ indexProcess + 1 }}.</span
                              >
                              <span
                                class="text-medium fw-bold accordion-btn-title pe-0"
                              >{{ process.title }}</span
                              >
                            </div>
                            <div class="d-flex gap-2 align-items-center">
                              <KsButton
                                :color="
                                  getWorkOrderProgressColor(
                                    process.progressLevel
                                  )
                                "
                                :title="
                                  process.progressLevel === 0
                                    ? 'آغاز نشده'
                                    : process.progressLevel === 50
                                    ? 'در حال انجام'
                                    : 'تکمیل شده'
                                "
                                bold />
                              <KsButton
                                color="dark"
                                fixed-width-height="42"
                                tooltip="مشاهده اطلاعات"
                                @click="showPreviewModal(process)">
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
            </div>
          </template>
        </KsAccordion>
      </div>

      <div key="list-loader">
        <ListLoader :active="state.isLoading" />
      </div>

      <!--    NO DATA FOUND   -->
      <div key="NoDataComponent">
        <NoDataFound
          v-if="!state.isLoading && !workOrderProgressStatistic.length" />
      </div>
    </transition-group>

    <!--   PREVIEW MODAL   -->
    <KsModal v-model="previewModal.visible" blur>
      <div
        class="p-1 position-relative d-flex flex-column justify-content-between h-100 w-100 overflow-hidden">
        <div class="pe-3" style="overflow-y: auto; height: 65vh">
          <div class="d-flex flex-column gap-2" dir="rtl">
            <span class="text-medium fw-bold">
              {{ previewModal.process.title }}
            </span>

            <hr class="d-none d-md-block" />
            <div class="d-flex flex-column gap-4" dir="ltr">
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:تاریخ شروع برنامه</span>
                <span class="text-small" dir="rtl">
                  {{
                    convertToJalaliDate(previewModal.process.programStartDate)
                  }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:تاریخ اتمام برنامه</span>
                <span class="text-small" dir="rtl">
                  {{ convertToJalaliDate(previewModal.process.programEndDate) }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:تاریخ اجرا</span>
                <span class="text-small" dir="rtl">
                  {{
                    convertToJalaliDate(previewModal.process.actualStartDate)
                  }}
                </span>
              </div>
              <div
                class="flex-1 d-flex flex-row-reverse gap-4 align-items-center">
                <span class="text-small item-title">:تاریخ اتمام</span>
                <span class="text-small" dir="rtl">
                  {{ convertToJalaliDate(previewModal.process.actualEndDate) }}
                </span>
              </div>
            </div>
          </div>
        </div>

        <div class="d-flex justify-content-end gap-2 mt-4">
          <KsButton
            color="dark"
            theme="flat"
            title="بستن"
            @click="previewModal.visible = false" />
        </div>
      </div>
    </KsModal>
  </div>
</template>
<style scoped>
.item-title {
  width: 120px;
}
</style>
