<script setup lang="ts">
// Small dependency-free line chart over dates (SVG): S-curve (percent) and cash flow (rial).
import { computed } from "vue";
import moment from "moment-jalaali";
import { dayDiff } from "@/composables/projectControl";

interface Point { date: string; y: number | null }
interface Series { name: string; color: string; dashed?: boolean; points: Point[] }
const props = defineProps({
  series: { type: Array as () => Series[], required: true },
  height: { type: Number, default: 280 },
  /** percent chart: fixed 0-100 axis */
  percent: Boolean,
  unitText: { type: String, default: "" },
});

const W = 760;
const L = 64, R = 14, T = 12, B = 34;

const dates = computed(() => props.series.flatMap((s) => s.points.filter((p) => p.y !== null).map((p) => p.date.slice(0, 10))).sort());
const d0 = computed(() => dates.value[0]);
const d1 = computed(() => dates.value[dates.value.length - 1]);
const span = computed(() => (d0.value ? Math.max(1, dayDiff(d0.value, d1.value)) : 1));
const yMax = computed(() => {
  if (props.percent) return 100;
  const m = Math.max(0, ...props.series.flatMap((s) => s.points.map((p) => p.y ?? 0)));
  return m <= 0 ? 1 : m;
});
const x = (d: string) => L + (dayDiff(d0.value, d.slice(0, 10)) / span.value) * (W - L - R);
const y = (v: number) => T + (1 - v / yMax.value) * (props.height - T - B);

const paths = computed(() =>
  props.series.map((s) => {
    const pts = s.points.filter((p) => p.y !== null).sort((a, b) => a.date.localeCompare(b.date));
    return { s, pts, d: pts.map((p, i) => `${i ? "L" : "M"}${x(p.date).toFixed(1)},${y(p.y as number).toFixed(1)}`).join(" ") };
  })
);
const yTicks = computed(() => [0, 0.25, 0.5, 0.75, 1].map((f) => ({ v: yMax.value * f, y: y(yMax.value * f) })));
const xTicks = computed(() => {
  if (!d0.value) return [];
  const n = 5;
  return Array.from({ length: n + 1 }, (_, i) => {
    const days = Math.round((span.value * i) / n);
    const iso = new Date(new Date(d0.value + "T00:00:00Z").getTime() + days * 86400000).toISOString().slice(0, 10);
    return { x: x(iso), label: moment(iso, "YYYY-MM-DD").format("jYYYY/jMM/jDD") };
  });
});
const fmt = (v: number) => (props.percent ? v.toFixed(0) + "٪" : v >= 1e9 ? (v / 1e9).toFixed(1) + " میلیارد" : v >= 1e6 ? (v / 1e6).toFixed(1) + " میلیون" : Math.round(v).toLocaleString("en-US"));
</script>

<template>
  <div>
    <div class="d-flex flex-wrap gap-3 small mb-1">
      <span v-for="s in series" :key="s.name"><i class="lc-key" :style="{ borderColor: s.color, borderStyle: s.dashed ? 'dashed' : 'solid' }"></i> {{ s.name }}</span>
      <span v-if="unitText" class="inv-muted">{{ unitText }}</span>
    </div>
    <div v-if="!d0" class="inv-muted">داده‌ای برای نمودار نیست.</div>
    <svg v-else :viewBox="`0 0 ${W} ${height}`" width="100%" :height="height" font-family="B Zar, sans-serif" font-size="11" direction="ltr" style="max-width: 100%">
      <g v-for="t in yTicks" :key="t.v">
        <line :x1="L" :x2="W - R" :y1="t.y" :y2="t.y" stroke="#e9ecef" />
        <text :x="L - 6" :y="t.y + 4" text-anchor="end" fill="#6c757d">{{ fmt(t.v) }}</text>
      </g>
      <g v-for="t in xTicks" :key="t.label">
        <line :x1="t.x" :x2="t.x" :y1="T" :y2="height - B" stroke="#f1f3f5" />
        <text :x="t.x" :y="height - B + 16" text-anchor="middle" fill="#6c757d">{{ t.label }}</text>
      </g>
      <g v-for="p in paths" :key="p.s.name">
        <path :d="p.d" fill="none" :stroke="p.s.color" stroke-width="2" :stroke-dasharray="p.s.dashed ? '6 4' : undefined" />
        <circle v-for="pt in p.pts" :key="pt.date" :cx="x(pt.date)" :cy="y(pt.y as number)" r="2.5" :fill="p.s.color"><title>{{ p.s.name }} — {{ moment(pt.date.slice(0, 10), "YYYY-MM-DD").format("jYYYY/jMM/jDD") }}: {{ fmt(pt.y as number) }}</title></circle>
      </g>
    </svg>
  </div>
</template>

<style scoped>
.lc-key { display: inline-block; width: 22px; border-top: 3px solid; vertical-align: middle; margin-left: 4px; }
</style>
