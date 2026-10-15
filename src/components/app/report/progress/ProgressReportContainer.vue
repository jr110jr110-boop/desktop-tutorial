<script setup lang="ts">
import type { ProgressReport } from "@/types/ProgressReport";
import type { PropType } from "vue";
import ProgressChart from "./components/ProgressChart.vue";
import ProgressTable from "./components/ProgressTable.vue";

defineProps({
  reportProgress: {
    type: Object as PropType<ProgressReport>,
    required: true,
  },
});
</script>

<template>
  <div class="d-flex flex-column gap-3">
    <div class="d-flex flex-column mb-1 text-xSmall" dir="rtl">
      <div class="d-flex align-items-center">
        <span style="width: 92px" class="opacity-75"> دستور ساخت: </span>
        <span class="fw-bold">
          {{ reportProgress.workOrderTitle }}
        </span>
      </div>
      <div class="d-flex align-items-center">
        <span style="width: 92px" class="opacity-75"> فرآیند تامین: </span>
        <span class="fw-bold"> {{ reportProgress.processTitle }} </span>
      </div>
    </div>
    <ProgressTable :report-progress="reportProgress" />
    <ProgressChart v-if="reportProgress" :report-progress="reportProgress" />
    <slot />
  </div>
</template>
