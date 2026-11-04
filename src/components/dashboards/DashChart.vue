<script setup lang="ts">
// One chart of a dashboard widget (offline chart.js, the library the front already uses). Colours are fixed per series so the same series has the same colour in every chart; the donut uses the same list.
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { Chart, registerables } from "chart.js";

const props = defineProps<{ chart: any }>();
const canvas = ref<HTMLCanvasElement | null>(null);
let instance: Chart | null = null;
const palette = ["#2563eb", "#d97706", "#059669", "#7c3aed", "#dc2626", "#0891b2", "#65a30d", "#db2777"];
const empty = computed(() => !props.chart.labels?.length || props.chart.series?.every((s: any) => s.values.every((v: any) => v === null || v === 0)));

function draw() {
  instance?.destroy();
  instance = null;
  if (!canvas.value || props.chart.masked || empty.value) return;
  Chart.register(...registerables);
  const donut = props.chart.kind === "donut";
  const labels = props.chart.labels;
  const datasets = props.chart.series.map((s: any, i: number) => ({
    label: s.name,
    data: s.values.map((v: any) => (v === null ? 0 : v)),
    backgroundColor: donut ? labels.map((_: any, j: number) => palette[j % palette.length]) : palette[i % palette.length],
    borderColor: palette[i % palette.length],
    tension: 0.25,
  }));
  instance = new Chart(canvas.value.getContext("2d")!, {
    type: donut ? "doughnut" : props.chart.kind === "line" ? "line" : "bar",
    data: { labels, datasets },
    options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: donut || datasets.length > 1, position: "bottom", labels: { font: { family: "IRANYekan" } } } } },
  });
}
onMounted(draw);
watch(() => props.chart, draw, { deep: true });
onBeforeUnmount(() => instance?.destroy());
</script>

<template>
  <div class="dash-chart">
    <div v-if="chart.masked" class="inv-muted small text-center py-4">مبالغ برای نقش شما نمایش داده نمی‌شود.</div>
    <div v-else-if="empty" class="inv-muted small text-center py-4">داده‌ای برای نمایش نیست.</div>
    <div v-else style="height: 240px"><canvas ref="canvas" /></div>
  </div>
</template>
