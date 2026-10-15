<script setup lang="ts">
import type { ProgressReport } from "@/types/ProgressReport";
import type { ChartData } from "chart.js";
import type { PropType } from "vue";
import KsChart from "@/components/common/KsChart.vue";
import { computed } from "vue";
import { convertToJalaliDate } from "@/composables/ConvertToJalaliDate";

const props = defineProps({
  reportProgress: {
    type: Object as PropType<ProgressReport>,
    required: true,
  },
});

const chartDataLine = computed<ChartData>(() => ({
  labels: props.reportProgress.weightPercents.map((weightPercent) =>
    convertToJalaliDate(weightPercent.date)
  ),
  datasets: [
    {
      label: "واقعی",
      data: props.reportProgress.weightPercents.map(
        (weightPercent) => weightPercent.actual
      ),
      borderColor: "#1A5CFF",
      backgroundColor: "#1A5CFF",
    },
    {
      label: "برنامه",
      data: props.reportProgress.weightPercents.map(
        (weightPercent) => weightPercent.planned
      ),
      borderColor: "rgb(255,136,0)",
      backgroundColor: "rgb(255,136,0)",
    },
  ],
}));

const chartDataBar = computed<ChartData>(() => ({
  labels: props.reportProgress.prices.map((price) =>
    convertToJalaliDate(price.date)
  ),
  datasets: [
    {
      label: "واقعی",
      data: props.reportProgress.prices.map((price) => price.actual),
      borderColor: "#1A5CFF",
      backgroundColor: "#1A5CFF",
    },
    {
      label: "برنامه",
      data: props.reportProgress.prices.map((price) => price.planned),
      borderColor: "rgb(255,136,0)",
      backgroundColor: "rgb(255,136,0)",
    },
  ],
}));
</script>

<template>
  <div class="d-flex flex-column gap-4">
    <hr />
    <div class="d-flex flex-column gap-3" dir="rtl">
      <span class="fw-bold"> درصد پیشرفت ها: </span>
      <KsChart chart-type="line" :chart-data="chartDataLine" />
    </div>
    <hr />
    <div class="d-flex flex-column gap-3" dir="rtl">
      <span class="fw-bold"> قیمت ها: </span>
      <KsChart chart-type="bar" :chart-data="chartDataBar" />
    </div>
  </div>
</template>
