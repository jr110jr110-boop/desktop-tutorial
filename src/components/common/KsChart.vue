<script setup lang="ts">
import type { PropType } from "vue";
import { onMounted, ref } from "vue";
import { Chart, type ChartData, type ChartType, registerables } from "chart.js";

const props = defineProps({
  chartType: {
    type: String as PropType<ChartType>,
    required: true,
  },
  chartData: {
    type: Object as PropType<ChartData>,
    required: true,
  },
});

const myChartRef = ref<HTMLCanvasElement>(null);

function initChart() {
  const ctx = myChartRef.value.getContext("2d");

  Chart.register(...registerables);
  const myChart = new Chart(ctx, {
    type: props.chartType,
    data: props.chartData,
    options: {
      responsive: true,
      maintainAspectRatio: true,
      animations: {
        tension: {
          duration: 1000,
          easing: "linear",
          from: 0.3,
          to: 0.4,
        },
      },
      plugins: {
        legend: {
          display: true,
          fullSize: true,
          position: "top",
          labels: {
            font: {
              family: "IRANYekan",
            },
          },
          textDirection: "rtl",
        },
      },
    },
  });
}

onMounted(() => {
  initChart();
});
</script>

<template>
  <canvas ref="myChartRef" width="100" style="max-height: 320px" />
</template>
