<script setup lang="ts">
// The trend of a headline KPI: up to 12 monthly points, oldest at the right and newest at the left (the page reads right to left, so time does too).
// The line is in the de-emphasis steel; the current month is the dot in the primary steel with a ring of the card colour. A text summary is given to screen readers.
import { computed } from "vue";
import { faNum, rialShort } from "@/composables/ceo";

const props = defineProps({
  values: { type: Array as () => number[], required: true },
  labels: { type: Array as () => string[], default: () => [] },
  unit: { type: String, default: "count" },
});

const W = 200;
const H = 40;
const PAD = 5;
const points = computed(() => {
  const v = props.values.filter((x) => Number.isFinite(Number(x))).map(Number);
  if (v.length < 2) return [];
  const min = Math.min(...v);
  const max = Math.max(...v);
  const span = max - min || 1;
  // mirrored: index 0 (oldest) at the right edge
  return v.map((y, i) => ({ x: W - PAD - (i * (W - 2 * PAD)) / (v.length - 1), y: H - PAD - ((y - min) / span) * (H - 2 * PAD) }));
});
const line = computed(() => points.value.map((p, i) => `${i ? "L" : "M"}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(" "));
const area = computed(() => (points.value.length ? `${line.value} L${points.value[points.value.length - 1].x.toFixed(1)},${H} L${points.value[0].x.toFixed(1)},${H} Z` : ""));
const last = computed(() => points.value[points.value.length - 1]);
const fmt = (v: number) => (props.unit === "rial" ? rialShort(v) : props.unit === "percent" ? faNum(v, 1) + "٪" : faNum(v, 1));
const summary = computed(() => {
  const v = props.values;
  if (v.length < 2) return "";
  const first = props.labels[0] || "";
  const end = props.labels[props.labels.length - 1] || "";
  return `روند ${faNum(v.length, 0)} ماه: از ${fmt(v[0])}${first ? " در " + first : ""} به ${fmt(v[v.length - 1])}${end ? " در " + end : ""}؛ کمینه ${fmt(Math.min(...v))}، بیشینه ${fmt(Math.max(...v))}`;
});
</script>

<template>
  <div v-if="points.length" class="spark">
    <svg :viewBox="`0 0 ${W} ${H}`" aria-hidden="true" preserveAspectRatio="none">
      <path :d="area" class="spark-area" />
      <path :d="line" class="spark-line" vector-effect="non-scaling-stroke" />
    </svg>
    <span :style="{ left: (last.x / W) * 100 + '%', top: (last.y / H) * 100 + '%' }" class="spark-dot" />
    <span class="visually-hidden">{{ summary }}</span>
  </div>
</template>

<style scoped>
.spark {
  position: relative;
  height: 40px;
  width: 100%;
}
.spark svg {
  display: block;
  width: 100%;
  height: 100%;
  overflow: visible;
}
.spark-line {
  fill: none;
  stroke: var(--ceo-spark, #8fa9c2);
  stroke-width: 2;
  stroke-linejoin: round;
  stroke-linecap: round;
}
.spark-area {
  fill: var(--ceo-spark, #8fa9c2);
  opacity: 0.12;
  stroke: none;
}
/* the dot is HTML so it stays round when the SVG is stretched */
.spark-dot {
  position: absolute;
  width: 9px;
  height: 9px;
  margin: -4.5px 0 0 -4.5px;
  border-radius: 50%;
  background: var(--ceo-primary-mark, #215d96);
  box-shadow: 0 0 0 2px var(--ceo-card, #fff);
}
</style>
