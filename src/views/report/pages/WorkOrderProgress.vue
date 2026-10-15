<script setup lang="ts">
import type Contract from "@/types/Contract";
import type WorkOrder from "@/types/WorkOrder";
import type { ProgressReport } from "@/types/ProgressReport";
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { getReportWorkOrderProgress } from "@/api/ReportAPIs";
import { getWorkOrderList } from "@/api/WorkOrderAPIs";
import { getContractList } from "@/api/ContractAPIs";
import ProgressReportContainer from "@/components/app/report/progress/ProgressReportContainer.vue";
import KsSelect from "@/components/common/KsSelect.vue";

const route = useRoute();
const router = useRouter();

const contractList = ref<Contract[]>([]);
const contractListLoading = ref(false);
const contractId = ref<string>(null);

const workOrderList = ref<WorkOrder[]>([]);
const workOrderListLoading = ref(false);
const workOrderId = ref<string>(null);

const progressReport = ref<ProgressReport>(null);
const progressReportLoading = ref(true);

const isLoading = computed(
  () =>
    contractListLoading.value ||
    workOrderListLoading.value ||
    progressReportLoading.value
);

const emptyAllData = () => {
  workOrderList.value = [];
  workOrderId.value = null;
  progressReport.value = null;
};

const receiveContractList = async () => {
  await emptyAllData();

  contractListLoading.value = true;
  const [data, error] = await getContractList();
  if (!error) {
    contractList.value = data.data.data;

    if (contractList.value.length)
      contractId.value =
        route.query.contract?.toString() ??
        (contractList.value[0]?.id as string) ??
        null;
  }
  contractListLoading.value = false;
};

const receiveWorkOrderList = async (receiveProgress = false) => {
  await emptyAllData();

  workOrderListLoading.value = true;
  const [data, error] = await getWorkOrderList(
    false,
    contractId.value,
    null,
    -1,
    null
  );
  if (!error) {
    workOrderList.value = data.data.data;
    if (workOrderList.value.length)
      workOrderId.value =
        route.query.work_order?.toString() ??
        (workOrderList.value[0]?.id as string) ??
        null;
  }
  workOrderListLoading.value = false;

  if (!error && receiveProgress) await receiveWorkOrderProgressReport();
};

const receiveWorkOrderProgressReport = async () => {
  progressReportLoading.value = true;
  const [data, error] = await getReportWorkOrderProgress(workOrderId.value);
  if (!error) {
    progressReport.value = data?.data;

    await router.replace({
      name: "ReportWorkOrderProgress",
      query: {
        contract: contractId.value,
        work_order: workOrderId.value,
      },
    });
  }
  progressReportLoading.value = false;
};

onMounted(async () => {
  await receiveContractList();
  await receiveWorkOrderList();
  await receiveWorkOrderProgressReport();
});
</script>

<template>
  <div class="d-flex flex-column gap-4">
    <div class="d-flex gap-4 justify-content-center" dir="rtl">
      <div class="d-flex gap-2 align-items-center">
        <span class="text-xSmall"> انتخاب قرارداد: </span>
        <KsSelect
          v-model="contractId"
          :loading="contractListLoading"
          :options="contractList"
          mode="filter"
          value-prop-name="id"
          label-prop-name="code"
          placeholder=""
          @update:modelValue="receiveWorkOrderList(true)" />
      </div>
      <div class="d-flex gap-2 align-items-center">
        <span class="text-xSmall"> انتخاب دستورکار: </span>
        <KsSelect
          v-model="workOrderId"
          :loading="workOrderListLoading"
          :options="workOrderList"
          mode="filter"
          value-prop-name="id"
          label-prop-name="title"
          placeholder=""
          @update:modelValue="receiveWorkOrderProgressReport" />
      </div>
    </div>

    <div class="background-color p-3 shadow-sm rounded-3">
      <div v-if="isLoading" class="position-relative p-5">
        <div class="custom-loader center" />
      </div>
      <div v-else class="d-flex flex-column gap-3">
        <div
          v-if="!progressReport"
          class="d-flex align-items-center justify-content-center p-5">
          <span class="fw-bold text-small"> داده ای یافت نشد </span>
        </div>
        <ProgressReportContainer v-else :report-progress="progressReport" />
      </div>
    </div>
  </div>
</template>
