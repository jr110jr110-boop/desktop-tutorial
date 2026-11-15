<script setup lang="ts">
// The histogram of one resource: a bar for the load of every day or week against the capacity line; the part over the capacity is in the danger colour and the part that comes from a draft plan is hatched.
// Dependency-free SVG (the same approach as LineChart.vue). The chart scrolls sideways inside its own box at phone width; every bar is a button (keyboard and screen reader) that selects the bucket.
import { computed } from "vue";
import type { LoadBucket } from "@/api/ResourceAPIs";
import { faDate, loadNum, DRAFT_LAYER } from "@/composables/resources";

const props = defineProps({
  buckets: { type: Array as () => LoadBucket[], required: true },
  unitText: { type: String, default: "" },
  weekly: Boolean,
  selected: { type: String, default: null },
  height: { type: Number, default: 260 },
});
const emit = defineEmits<{ (e: "select", start: string): void }>();

const BAR = 26; // width of one slot
const L = 52, R = 10, T = 14, B = 46;
const width = computed(() => L + R + Math.max(1, props.buckets.length) * BAR);
const yMax = computed(() => {
  const m = Math.max(0, ...props.buckets.map((b) => Math.max(b.load, b.capacity)));
  return m <= 0 ? 1 : m * 1.08;
});
const y = (v: number) => T + (1 - v / yMax.value) * (props.height - T - B);
const baseY = computed(() => props.height - B);

interface Bar { b: LoadBucket; x: number; over: number; draft: number; normal: number; label: string; aria: string; capY: number }
const bars = computed<Bar[]>(() =>
  props.buckets.map((b, i) => {
    const over = Math.min(Math.max(b.over, 0), b.load);
    const draft = Math.min(Math.max(b.draftLoad, 0), Math.max(b.load - over, 0));
    const normal = Math.max(b.load - over - draft, 0);
    const label = faDate(b.start);
    const range = props.weekly ? `${faDate(b.start)} تا ${faDate(b.end)}` : faDate(b.start);
    return {
      b, x: L + i * BAR, over, draft, normal, label, capY: y(b.capacity),
      aria: `${range}: بار ${loadNum(b.load)}${props.unitText ? " " + props.unitText : ""}، ظرفیت ${loadNum(b.capacity)}${b.over > 0 ? "، بیش‌تخصیص " + loadNum(b.over) : ""}${b.draftLoad > 0 ? "، " + loadNum(b.draftLoad) + " از آن " + DRAFT_LAYER : ""}`,
    };
  })
);
// one label in every few slots, so the dates do not run into each other
const every = computed(() => (props.weekly ? 2 : 3));
const yTicks = computed(() => [0, 0.25, 0.5, 0.75, 1].map((f) => ({ v: (yMax.value / 1.08) * f, y: y((yMax.value / 1.08) * f) })));
const capacityPath = computed(() => bars.value.map((r, i) => `${i ? "L" : "M"}${r.x},${r.capY.toFixed(1)} L${r.x + BAR},${r.capY.toFixed(1)}`).join(" "));
const summary = computed(() => {
  const over = props.buckets.filter((b) => b.over > 0).length;
  return `نمودار بار منبع: ${loadNum(props.buckets.length)} ${props.weekly ? "هفته" : "روز"}، ${over ? loadNum(over) + " مورد بیش‌تخصیص" : "بدون بیش‌تخصیص"}`;
});
const pick = (r: Bar) => emit("select", r.b.start);
</script>

<template>
  <div>
    <div class="d-flex flex-wrap gap-3 small mb-1" aria-hidden="true">
      <span><i class="lc-sw lc-fill-normal"></i> بار تاییدشده</span>
      <span><i class="lc-sw lc-hatch"></i> {{ DRAFT_LAYER }}</span>
      <span><i class="lc-sw lc-fill-over"></i> بیش از ظرفیت{{ weekly ? " (در نمای هفتگی: جمع بیش‌تخصیص روزهای هفته)" : "" }}</span>
      <span><i class="lc-line"></i> ظرفیت</span>
      <span v-if="unitText" class="inv-muted">{{ unitText }}</span>
    </div>
    <div v-if="!buckets.length" class="inv-muted">برای این بازه باری ثبت نشده است.</div>
    <div v-else class="lc-scroll" dir="ltr" tabindex="0" role="group" :aria-label="summary">
      <svg :viewBox="`0 0 ${width} ${height}`" :width="width" :height="height" font-size="10" focusable="false">
        <defs>
          <pattern id="lcHatch" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
            <rect width="6" height="6" class="lc-hatch-bg" />
            <line x1="0" y1="0" x2="0" y2="6" class="lc-hatch-line" stroke-width="2" />
          </pattern>
        </defs>
        <g v-for="t in yTicks" :key="t.v">
          <line :x1="L" :x2="width - R" :y1="t.y" :y2="t.y" class="lc-grid" />
          <text :x="L - 6" :y="t.y + 3" text-anchor="end" class="lc-text">{{ loadNum(t.v) }}</text>
        </g>
        <g v-for="(r, i) in bars" :key="r.b.start" role="button" tabindex="0" :aria-label="r.aria" :aria-pressed="selected === r.b.start" class="lc-bar" @click="pick(r)" @keydown.enter.prevent="pick(r)" @keydown.space.prevent="pick(r)">
          <title>{{ r.aria }}</title>
          <rect :x="r.x" :y="T" :width="BAR" :height="baseY - T" fill="transparent" />
          <rect v-if="selected === r.b.start" :x="r.x + 1" :y="T" :width="BAR - 2" :height="baseY - T" class="lc-selected" />
          <rect :x="r.x + 4" :y="y(r.normal)" :width="BAR - 8" :height="Math.max(0, baseY - y(r.normal))" class="lc-fill-normal" />
          <rect :x="r.x + 4" :y="y(r.normal + r.draft)" :width="BAR - 8" :height="Math.max(0, y(r.normal) - y(r.normal + r.draft))" class="lc-fill-draft" />
          <rect :x="r.x + 4" :y="y(r.normal + r.draft + r.over)" :width="BAR - 8" :height="Math.max(0, y(r.normal + r.draft) - y(r.normal + r.draft + r.over))" class="lc-fill-over" />
          <text v-if="i % every === 0" :x="r.x + BAR / 2" :y="baseY + 14" text-anchor="end" class="lc-text" :transform="`rotate(-40 ${r.x + BAR / 2} ${baseY + 14})`">{{ r.label }}</text>
        </g>
        <path :d="capacityPath" fill="none" class="lc-cap" stroke-width="2" stroke-dasharray="5 3" />
      </svg>
      <!-- the text alternative of the picture: the same numbers as a table for a screen reader -->
      <table class="visually-hidden">
        <caption>{{ summary }}</caption>
        <thead>
          <tr><th scope="col">{{ weekly ? "هفته" : "روز" }}</th><th scope="col">بار</th><th scope="col">ظرفیت</th><th scope="col">بیش‌تخصیص</th><th scope="col">{{ DRAFT_LAYER }}</th></tr>
        </thead>
        <tbody>
          <tr v-for="r in bars" :key="r.b.start">
            <th scope="row">{{ weekly ? faDate(r.b.start) + " تا " + faDate(r.b.end) : faDate(r.b.start) }}</th>
            <td>{{ loadNum(r.b.load) }}</td><td>{{ loadNum(r.b.capacity) }}</td><td>{{ loadNum(r.b.over) }}</td><td>{{ loadNum(r.b.draftLoad) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
/* colours come from the app's variables (rgb triples), so the dark theme follows */
.lc-scroll { overflow-x: auto; max-width: 100%; border: 1px solid rgb(var(--gray-4)); border-radius: 6px; padding: 4px; }
.lc-sw { display: inline-block; width: 14px; height: 14px; margin-left: 4px; vertical-align: middle; border-radius: 2px; }
.lc-fill-normal { fill: rgb(var(--primary)); background: rgb(var(--primary)); }
.lc-fill-over { fill: rgb(var(--danger)); background: rgb(var(--danger)); }
.lc-fill-draft { fill: url(#lcHatch); }
.lc-hatch { background: repeating-linear-gradient(45deg, rgba(var(--primary), 0.25), rgba(var(--primary), 0.25) 3px, rgb(var(--primary)) 3px, rgb(var(--primary)) 5px); }
.lc-hatch-bg { fill: rgba(var(--primary), 0.25); }
.lc-hatch-line { stroke: rgb(var(--primary)); }
.lc-grid { stroke: rgb(var(--gray-4)); }
.lc-text { fill: rgba(var(--text-color), 0.65); }
.lc-selected { fill: rgba(var(--warn), 0.25); }
.lc-cap { stroke: rgb(var(--text-color)); }
.lc-line { display: inline-block; width: 22px; border-top: 2px dashed rgb(var(--text-color)); vertical-align: middle; margin-left: 4px; }
.lc-bar { cursor: pointer; outline: none; }
.lc-bar:focus-visible rect:first-of-type { stroke: rgb(var(--primary)); stroke-width: 2; }
</style>
