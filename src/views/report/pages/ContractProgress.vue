<script setup lang="ts">
import type Contract from "@/types/Contract";
import type {ProgressReport} from "@/types/ProgressReport";
import {computed, onMounted, ref} from "vue";
import {Colors} from "@/assets/colors";
import {useRoute, useRouter} from "vue-router";
import {getContractList} from "@/api/ContractAPIs";
import {getReportContractProgress} from "@/api/ReportAPIs";
import ProgressReportContainer from "@/components/app/report/progress/ProgressReportContainer.vue";
import KsSelect from "@/components/common/KsSelect.vue";
import KsButton from "@/components/common/KsButton.vue";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";

const route = useRoute();
const router = useRouter();

const contractList = ref<Contract[]>([]);
const contractListLoading = ref(false);
const contractId = ref(null);

const progressReport = ref<ProgressReport>(null);
const progressReportLoading = ref(true);

const isLoading = computed(
    () => contractListLoading.value || progressReportLoading.value
);

const receiveContractProgressReport = async () => {
  progressReportLoading.value = true;
  const [data, error] = await getReportContractProgress(contractId.value);
  if (!error) {
    progressReport.value = data?.data;
    await router.replace({
      name: "ReportContractProgress",
      query: {
        contract: contractId.value,
      },
    });
  }

  progressReportLoading.value = false;
};

const receiveContractList = async () => {
  contractListLoading.value = true;
  const [data, error] = await getContractList();
  contractListLoading.value = false;
  if (!error) {
    contractList.value = data.data.data;
    if (contractList.value.length) {
      contractId.value =
          route.query.contract?.toString() ??
          (contractList.value[0]?.id as string) ??
          null;
      await receiveContractProgressReport();
    }
  }
};

onMounted(() => {
  receiveContractList();
});
</script>

<template>
  <div class="d-flex flex-column gap-4">
    <div class="d-flex gap-4 justify-content-between" dir="rtl">

      <div class="d-flex gap-2 align-items-center p-1">

        <span class="text-xSmall"> انتخاب قرارداد: </span>
        <KsSelect
            v-model="contractId"
            :loading="contractListLoading"
            :options="contractList"
            mode="filter"
            value-prop-name="id"
            label-prop-name="code"
            placeholder=""
            @update:modelValue="receiveContractProgressReport"/>
      </div>
      <div class="d-flex flex-row-reverse justify-content-xl-between align-items-center">
        <KsButton
            class="p-1"
            color="dark"
            fixed-width-height="42"
            theme="flat"
            @click="$router.back()">
          <template #content>
            <AngleLeftIcon :fill="Colors.primary" width="6"/>
          </template>
        </KsButton>
      </div>
    </div>

    <div class="background-color p-3 shadow-sm rounded-3">
      <div v-if="isLoading" class="position-relative p-5">
        <div class="custom-loader center"/>
      </div>
      <div v-else class="d-flex flex-column gap-3">
        <div
            v-if="!progressReport"
            class="d-flex align-items-center justify-content-center p-5">
          <span class="fw-bold text-small"> داده ای یافت نشد </span>
        </div>
        <ProgressReportContainer v-else :report-progress="progressReport"/>
      </div>
    </div>
  </div>
</template>
