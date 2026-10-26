<script setup lang="ts">
// One chart of a section (ChartDto) drawn with Chart.js. Right to left: the time and category axes run from the right, the value axis sits at the right, horizontal bars grow to the left.
// Marks follow the dataviz rules of the page: thin bars (≤ 22px, 4px rounded data end, square at the baseline), 2px lines with a soft gradient area under the main one,
// a 2px gap of the card colour between touching marks, hairline horizontal gridlines only (none along a horizontal bar: the value sits at its tip), a 60% donut with its total,
// the legend as small chips under the plot (two or more series, and every donut), one tooltip card that lists every series (value first, name second), and a table view.
// Colours come from the series role (composables/ceo.ts chartTokens); no colour is the only carrier of meaning (legend, line dash of the neutral series, table).
// hidden=true (masked labour amounts) shows «محرمانه (حقوق)» instead of the chart. No animation: the page has a single load reveal, nothing else moves by itself.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { Chart, registerables } from "chart.js";
import type { CeoChart } from "@/api/CeoAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { chartTokens, faNum, PROTECTED_TEXT, rialFull, unitValue, type CeoTheme } from "@/composables/ceo";

Chart.register(...registerables);

const props = defineProps({
  chart: { type: Object as () => CeoChart, required: true },
  theme: { type: String as () => CeoTheme, default: "light" },
  height: { type: Number, default: 196 },
});
const emit = defineEmits<{ (e: "drill", key: string): void }>();

const canvas = ref<HTMLCanvasElement | null>(null);
const tip = ref<HTMLDivElement | null>(null);
const asTable = ref(false);
let instance: Chart | null = null;

const tokens = computed(() => chartTokens[props.theme] || chartTokens.light);
const unit = computed(() => props.chart.unit);
const isEmpty = computed(() => !props.chart.labels?.length || !props.chart.series?.length || props.chart.series.every((s) => !s.values?.some((v) => Number(v))));

// ---------------------------------------------------------------- scale of a money axis
const maxAbs = computed(() => Math.max(0, ...props.chart.series.flatMap((s) => (s.values || []).map((v) => Math.abs(Number(v) || 0)))));
const scale = computed(() => {
  if (unit.value !== "rial") return { div: 1, caption: unit.value === "percent" ? "درصد" : unit.value === "days" ? "روز" : unit.value === "hours" ? "ساعت" : "" };
  if (maxAbs.value >= 1e9) return { div: 1e9, caption: "میلیارد ریال" };
  if (maxAbs.value >= 1e6) return { div: 1e6, caption: "میلیون ریال" };
  return { div: 1, caption: "ریال" };
});
const tick = (v: number) => {
  if (unit.value === "percent") return faNum(v, 0) + "٪";
  const x = v / scale.value.div;
  return faNum(x, Math.abs(x) < 10 && x % 1 !== 0 ? 1 : 0);
};
const valueText = (v: number) => unitValue(unit.value, v, true);
/** A direct label (bar tip, last point): the axis unit, one decimal below a thousand. */
const labelText = (v: number) => {
  if (unit.value === "percent") return faNum(v, 1) + "٪";
  const x = v / scale.value.div;
  return faNum(x, Math.abs(x) < 1000 ? 1 : 0);
};

// ---------------------------------------------------------------- donut: largest first, at most four coloured segments, the rest folded into «سایر»
const donut = computed(() => {
  const s = props.chart.series[0];
  const items = props.chart.labels.map((label, i) => ({ label, value: Number(s?.values?.[i]) || 0 })).filter((x) => x.value > 0);
  items.sort((a, b) => b.value - a.value);
  if (items.length <= 5) return items.map((x, i) => ({ ...x, folded: [] as string[], other: i === 4 }));
  const head = items.slice(0, 4).map((x) => ({ ...x, folded: [] as string[], other: false }));
  const rest = items.slice(4);
  return [...head, { label: "سایر", value: rest.reduce((a, b) => a + b.value, 0), folded: rest.map((r) => r.label), other: true }];
});
const donutTotal = computed(() => donut.value.reduce((a, b) => a + b.value, 0));

// ---------------------------------------------------------------- colours by role
const colors = computed(() => {
  const t = tokens.value;
  const roles = props.chart.series.map((s) => s.role || "primary");
  if (props.chart.kind === "stackedBar" && roles.length > 1 && roles.every((r) => r === roles[0])) {
    // the same role repeated in a stack: ordered steps of one measure (age buckets, stages) -> the one-hue ramp, light to dark
    const ramp = t.sequential;
    return roles.map((_, i) => ramp[Math.round((i * (ramp.length - 1)) / Math.max(1, roles.length - 1))]);
  }
  // by role; a colour already taken in this chart moves to the next free categorical slot (so two series never share a colour)
  const used = new Set<string>();
  return roles.map((r) => {
    let c = t.roles[r] || t.categorical[0];
    if (used.has(c) && r !== "neutral") c = t.categorical.find((x) => !used.has(x)) || c;
    used.add(c);
    return c;
  });
});
const donutColors = computed(() => donut.value.map((d, i) => (d.other ? tokens.value.other : tokens.value.categorical[i])));

const legend = computed(() => {
  if (props.chart.kind === "donut") return donut.value.map((d, i) => ({ name: d.label, color: donutColors.value[i], shape: "dot", value: valueText(d.value), share: donutTotal.value ? faNum((d.value / donutTotal.value) * 100, 0) + "٪" : "" }));
  if (props.chart.series.length < 2) return [];
  return props.chart.series.map((s, i) => ({ name: s.name, color: colors.value[i], shape: props.chart.kind === "line" ? (s.role === "neutral" ? "dash" : "line") : "dot", value: "", share: "" }));
});

// ---------------------------------------------------------------- the HTML tooltip (labels are server data: textContent only)
function renderTip(context: any) {
  const el = tip.value;
  if (!el) return;
  const t = context.tooltip;
  if (!t || t.opacity === 0) {
    el.style.opacity = "0";
    return;
  }
  el.replaceChildren();
  const points = t.dataPoints || [];
  if (!points.length) return;
  const title = document.createElement("div");
  title.className = "tip-title";
  const isDonut = props.chart.kind === "donut";
  title.textContent = isDonut ? props.chart.title : points[0].label;
  el.appendChild(title);
  for (const p of points) {
    const row = document.createElement("div");
    row.className = "tip-row";
    const key = document.createElement("span");
    key.className = "tip-key" + (props.chart.kind === "line" ? " line" : "");
    const color = isDonut ? donutColors.value[p.dataIndex] : colors.value[p.datasetIndex];
    key.style.background = color;
    const value = document.createElement("strong");
    const raw = isDonut ? donut.value[p.dataIndex]?.value : Number(p.raw);
    value.textContent = valueText(raw);
    if (unit.value === "rial") value.title = rialFull(raw);
    const name = document.createElement("span");
    name.className = "tip-name";
    if (isDonut) {
      const d = donut.value[p.dataIndex];
      name.textContent = d.label + (donutTotal.value ? ` (${faNum((d.value / donutTotal.value) * 100, 0)}٪)` : "") + (d.folded.length ? ` شامل ${d.folded.join("، ")}` : "");
    } else name.textContent = props.chart.series[p.datasetIndex]?.name || "";
    row.append(key, value, name);
    el.appendChild(row);
  }
  el.style.opacity = "1";
  // keep the box inside the figure: it goes to the side of the pointer that has room
  const box = el.parentElement!.getBoundingClientRect();
  const w = el.offsetWidth;
  const h = el.offsetHeight;
  let x = t.caretX + 14;
  if (x + w > box.width) x = t.caretX - w - 14;
  x = Math.max(0, Math.min(x, box.width - w));
  let y = t.caretY - h / 2;
  y = Math.max(0, Math.min(y, props.height - h));
  el.style.transform = `translate(${Math.round(x)}px, ${Math.round(y)}px)`;
}

// ---------------------------------------------------------------- small plugins: crosshair (lines), value at the bar tip (horizontal bars), last value (single line), total (donut)
const crosshair = {
  id: "ceoCrosshair",
  afterDatasetsDraw(c: any) {
    if (props.chart.kind !== "line") return;
    const active = c.tooltip?.getActiveElements?.() || [];
    if (!active.length) return;
    const x = active[0].element.x;
    const { top, bottom } = c.chartArea;
    const ctx = c.ctx;
    ctx.save();
    ctx.strokeStyle = tokens.value.axis;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(Math.round(x) + 0.5, top);
    ctx.lineTo(Math.round(x) + 0.5, bottom);
    ctx.stroke();
    ctx.restore();
  },
};
const directLabels = {
  id: "ceoDirectLabels",
  afterDatasetsDraw(c: any) {
    const ctx = c.ctx;
    const kind = props.chart.kind;
    ctx.save();
    ctx.font = "500 12px IRANYekan, Tahoma, sans-serif";
    ctx.fillStyle = tokens.value.ink;
    ctx.textBaseline = "middle";
    if (kind === "hbar" && props.chart.series.length === 1) {
      // the value at the tip of each bar, in the unit of the axis caption (no «ریال» on every bar)
      const meta = c.getDatasetMeta(0);
      const data = c.data.datasets[0].data as number[];
      meta.data.forEach((bar: any, i: number) => {
        ctx.textAlign = "right";
        ctx.fillText(labelText(Number(data[i])), bar.x - 6, bar.y);
      });
    }
    if (kind === "line" && props.chart.series.length === 1) {
      // the latest value beside its point (the newest point is at the left edge, so the label sits on its right, inside the plot)
      const meta = c.getDatasetMeta(0);
      const lastPoint = meta.data[meta.data.length - 1];
      const data = c.data.datasets[0].data as number[];
      if (lastPoint) {
        ctx.textAlign = "left";
        ctx.fillText(labelText(Number(data[data.length - 1])), lastPoint.x + 8, lastPoint.y - 12);
      }
    }
    if (kind === "donut") {
      const meta = c.getDatasetMeta(0);
      const arc = meta.data[0];
      if (arc) {
        ctx.textAlign = "center";
        ctx.font = "500 22px IRANYekan, Tahoma, sans-serif";
        ctx.fillText(unit.value === "rial" ? valueText(donutTotal.value).replace(" ریال", "") : faNum(donutTotal.value, 0), arc.x, arc.y - 7);
        ctx.font = "400 12px IRANYekan, Tahoma, sans-serif";
        ctx.fillStyle = tokens.value.muted;
        ctx.fillText("مجموع", arc.x, arc.y + 16);
      }
    }
    ctx.restore();
  },
};

// ---------------------------------------------------------------- the Chart.js configuration of a kind
function config(): any {
  const t = tokens.value;
  const kind = props.chart.kind;
  const font = { family: "IRANYekan, Tahoma, sans-serif", size: 12 };
  const tooltip = { enabled: false, external: renderTip };
  const base: any = {
    animation: false,
    // drawn at twice the pixels at least: sharp on ordinary screens and on paper
    devicePixelRatio: Math.max(2, window.devicePixelRatio || 1),
    responsive: true,
    maintainAspectRatio: false,
    locale: "fa-IR",
    plugins: { legend: { display: false }, tooltip },
    font,
  };
  if (kind === "donut") {
    return {
      type: "doughnut",
      data: { labels: donut.value.map((d) => d.label), datasets: [{ data: donut.value.map((d) => d.value), backgroundColor: donutColors.value, borderColor: t.card, borderWidth: 2, borderRadius: 2, hoverOffset: 4, hoverBorderColor: t.card }] },
      options: { ...base, cutout: "60%", layout: { padding: 6 }, interaction: { mode: "nearest", intersect: true } },
      plugins: [directLabels],
    };
  }
  const horizontal = kind === "hbar";
  const stacked = kind === "stackedBar";
  const isLine = kind === "line";
  const n = props.chart.series.length;
  // the main line (the first that is not a plan/reference) carries a soft area: its colour at 18% at the top, nothing at the baseline
  const filled = props.chart.series.findIndex((s) => s.role !== "neutral");
  const datasets = props.chart.series.map((s, i) => {
    const c = colors.value[i];
    if (isLine) {
      const last = (s.values || []).length - 1;
      return {
        label: s.name,
        data: s.values,
        borderColor: c,
        backgroundColor: i === filled ? areaFill(c) : c,
        fill: i === filled ? "origin" : false,
        borderWidth: 2,
        borderDash: s.role === "neutral" ? [5, 4] : [],
        cubicInterpolationMode: "monotone",
        pointRadius: (ctx: any) => (ctx.dataIndex === last && s.role !== "neutral" ? 4 : 0),
        pointHoverRadius: 4,
        pointBackgroundColor: c,
        pointBorderColor: t.card,
        pointBorderWidth: 2,
        pointHitRadius: 12,
      };
    }
    const top = !stacked || i === n - 1;
    return {
      label: s.name,
      data: s.values,
      backgroundColor: c,
      hoverBackgroundColor: c,
      borderColor: t.card,
      // the 2px surface gap between the segments of a stack; the rounded data end only on the outer segment
      borderWidth: stacked ? (horizontal ? { right: i === 0 ? 0 : 2 } : { bottom: i === 0 ? 0 : 2 }) : 0,
      borderSkipped: stacked ? false : "start",
      borderRadius: top ? (stacked ? { topLeft: 4, topRight: 4 } : 4) : 0,
      maxBarThickness: 22,
      categoryPercentage: n > 1 && !stacked ? 0.72 : 0.62,
      barPercentage: n > 1 && !stacked ? 0.9 : 1,
    };
  });
  const valueAxis: any = {
    position: horizontal ? "bottom" : "right",
    reverse: horizontal, // horizontal bars grow from the right
    stacked,
    beginAtZero: !isLine,
    grace: isLine ? "8%" : 0,
    // horizontal gridlines only: along a horizontal bar the value is written at its tip, so that axis has no grid
    grid: { display: !horizontal, color: t.grid, drawBorder: false, tickLength: 0, lineWidth: 1 },
    border: { display: false },
    // a single horizontal series has its value at each bar's tip: no second scale under it
    ticks: { display: !(horizontal && n === 1), color: t.muted, font, padding: 8, maxTicksLimit: horizontal ? 4 : 5, precision: unit.value === "count" ? 0 : undefined, callback: (v: number) => tick(Number(v)) },
  };
  const labels = props.chart.labels.map(String);
  const labelWidth = measure(labels, 11);
  const categoryAxis: any = {
    position: horizontal ? "right" : "bottom",
    reverse: !horizontal, // time runs from the right
    stacked,
    grid: { display: false, drawBorder: true, borderColor: t.axis },
    ticks: {
      color: t.muted,
      font,
      padding: 6,
      autoSkip: false,
      maxRotation: 0,
      // points sit on the plot edges: the first and last labels align inwards (Chart.js 3 pads the wrong edge of a reversed axis otherwise)
      align: isLine ? "inner" : "center",
      // a horizontal axis shows as many labels as fit, counted from the newest (left) one so the current period is always named
      callback(this: any, _value: any, index: number) {
        if (horizontal) return labels[index];
        const width = this.maxWidth || this.width || this.chart?.width || 300;
        const fit = Math.max(1, Math.floor(width / (labelWidth + 6)));
        const step = Math.max(1, Math.ceil(labels.length / fit));
        return (labels.length - 1 - index) % step === 0 ? labels[index] : "";
      },
    },
  };
  return {
    type: isLine ? "line" : "bar",
    data: { labels: props.chart.labels, datasets },
    options: {
      ...base,
      indexAxis: horizontal ? "y" : "x",
      layout: { padding: { left: horizontal ? 64 : isLine && n === 1 ? 4 : 0, top: isLine && n === 1 ? 14 : 4 } },
      interaction: { mode: "index", intersect: false, axis: horizontal ? "y" : "x" },
      scales: horizontal ? { x: valueAxis, y: categoryAxis } : { x: categoryAxis, y: valueAxis },
    },
    plugins: [crosshair, directLabels],
  };
}

/** The widest label in px with the page font (the fonts are loaded before the first draw). */
function measure(labels: string[], size: number): number {
  try {
    const ctx = document.createElement("canvas").getContext("2d");
    if (!ctx) return 60;
    ctx.font = `${size}px IRANYekan, Tahoma, sans-serif`;
    return Math.max(24, ...labels.map((l) => ctx.measureText(l).width));
  } catch {
    return 60;
  }
}

/** The soft area under the main line: a vertical gradient over the plot, recomputed when the plot size changes. */
function areaFill(color: string) {
  return (ctx: any) => {
    const area = ctx.chart?.chartArea;
    if (!area) return hexAlpha(color, 0.12);
    const g = ctx.chart.ctx.createLinearGradient(0, area.top, 0, area.bottom);
    g.addColorStop(0, hexAlpha(color, 0.18));
    g.addColorStop(1, hexAlpha(color, 0));
    return g;
  };
}

function hexAlpha(hex: string, a: number) {
  const h = hex.replace("#", "");
  const n = parseInt(h, 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${a})`;
}

let fontsReady: Promise<unknown> | null = null;
function waitFonts() {
  if (!fontsReady) {
    try {
      fontsReady = Promise.race([Promise.all([(document as any).fonts.load('400 11px "IRANYekan"', "۱۲۳ آ"), (document as any).fonts.load('500 18px "IRANYekan"', "۱۲۳ آ")]), new Promise((r) => setTimeout(r, 1500))]);
    } catch {
      fontsReady = Promise.resolve();
    }
  }
  return fontsReady;
}

let drawSeq = 0;
async function draw() {
  const mine = ++drawSeq;
  instance?.destroy();
  instance = null;
  if (props.chart.hidden || isEmpty.value || asTable.value) return;
  await waitFonts();
  await nextTick();
  // a newer draw started while this one waited (new data and a theme switch together): only the newest one draws
  if (mine !== drawSeq || !canvas.value) return;
  Chart.getChart(canvas.value)?.destroy();
  instance = new Chart(canvas.value, config());
}

watch(() => [props.chart, props.theme], draw, { deep: false });
watch(asTable, draw);
// paper has other widths and heights than the screen: redraw when printing starts and ends
let printMedia: MediaQueryList | null = null;
const onPrintChange = () => instance?.resize();
onMounted(() => {
  draw();
  try {
    printMedia = window.matchMedia("print");
    printMedia.addEventListener?.("change", onPrintChange);
  } catch {
    printMedia = null;
  }
});
onBeforeUnmount(() => {
  printMedia?.removeEventListener?.("change", onPrintChange);
  instance?.destroy();
  instance = null;
});

/** Redraw at the print size (called by the page before printing). */
function resize() {
  instance?.resize();
}
defineExpose({ resize });

// ---------------------------------------------------------------- table view
const tableRows = computed(() => {
  if (props.chart.kind === "donut") return donut.value.map((d) => ({ label: d.label + (d.folded.length ? ` (${d.folded.join("، ")})` : ""), cells: [d.value] }));
  return props.chart.labels.map((label, i) => ({ label, cells: props.chart.series.map((s) => Number(s.values?.[i])) }));
});
const tableHeads = computed(() => (props.chart.kind === "donut" ? [props.chart.series[0]?.name || "مقدار"] : props.chart.series.map((s) => s.name)));
</script>

<template>
  <figure class="ceo-chart">
    <figcaption class="chart-head">
      <div class="chart-titles">
        <h4 class="chart-title">{{ chart.title }}</h4>
        <span v-if="scale.caption && !chart.hidden" class="chart-unit">{{ scale.caption }}</span>
      </div>
      <div v-if="!chart.hidden && !isEmpty" class="chart-tools">
        <button :aria-label="asTable ? 'نمایش نمودار' : 'نمایش جدول اعداد'" :aria-pressed="asTable" :data-tip="asTable ? 'نمایش نمودار' : 'جدول اعداد'" class="ceo-icon-btn small" type="button" @click="asTable = !asTable">
          <CeoIcon :name="asTable ? 'chart' : 'table'" :size="16" />
        </button>
        <button v-if="chart.drillKey" aria-label="جدول جزئیات این نمودار" class="ceo-icon-btn small" data-tip="ریز رکوردها" type="button" @click="emit('drill', chart.drillKey)">
          <CeoIcon :size="16" name="list" />
        </button>
      </div>
    </figcaption>

    <div v-if="chart.hidden" :style="{ minHeight: height + 'px' }" class="chart-masked">
      <CeoIcon :size="22" name="lock" />
      <strong>{{ PROTECTED_TEXT }}</strong>
      <span>این نمودار مبالغ دستمزد دارد و برای نقش شما نمایش داده نمی‌شود.</span>
    </div>
    <div v-else-if="isEmpty" :style="{ minHeight: height + 'px' }" class="chart-empty">
      <span>برای این نمودار هنوز داده‌ای ثبت نشده است.</span>
      <span v-if="chart.drillKey" class="chart-empty-hint">جدول جزئیات را باز کنید تا ببینید کدام رکوردها شمرده می‌شوند.</span>
    </div>
    <template v-else>
      <div v-if="asTable" class="chart-table-wrap">
        <table class="chart-table">
          <thead>
            <tr>
              <th scope="col">{{ chart.kind === "donut" ? "دسته" : "دوره" }}</th>
              <th v-for="h in tableHeads" :key="h" scope="col">{{ h }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in tableRows" :key="r.label">
              <th scope="row">{{ r.label }}</th>
              <td v-for="(c, j) in r.cells" :key="j" :title="unit === 'rial' ? rialFull(c) : undefined" class="num">{{ valueText(c) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else :class="{ 'is-donut': chart.kind === 'donut' }" :style="{ height: height + 'px' }" class="chart-canvas">
        <canvas ref="canvas" :aria-label="chart.title + ' — برای دیدن اعداد، نمای جدول را باز کنید'" role="img" />
        <div ref="tip" class="chart-tip" dir="rtl" />
      </div>
      <ul v-if="legend.length && !asTable" :class="{ donut: chart.kind === 'donut' }" class="chart-legend">
        <li v-for="(l, i) in legend" :key="i">
          <span :class="'key-' + l.shape" :style="'--key: ' + l.color" class="legend-key" />
          <span class="legend-name">{{ l.name }}</span>
          <span v-if="l.share" class="legend-share">{{ l.share }}</span>
        </li>
      </ul>
    </template>
    <p v-if="chart.note && !chart.hidden" class="chart-note">{{ chart.note }}</p>
  </figure>
</template>

<style scoped>
.ceo-chart {
  margin: 0;
  min-width: 0;
}
.chart-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 12px;
}
.chart-titles {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0 8px;
  min-width: 0;
  padding-top: 4px;
}
.chart-title {
  margin: 0;
  font-size: 14px;
  line-height: 20px;
  font-weight: 500;
  color: var(--ceo-ink);
}
.chart-unit {
  font-size: 12px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.chart-tools {
  display: flex;
  gap: 2px;
  flex: none;
  margin-top: -2px;
}
/* the legend: small chips under the plot */
.chart-legend {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  list-style: none;
  margin: 12px 0 0;
  padding: 0;
  font-size: 12px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.chart-legend li {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 1px 10px;
  border: 1px solid var(--ceo-line-soft);
  border-radius: 999px;
  background: var(--ceo-cell);
}
.chart-legend.donut {
  justify-content: center;
}
.legend-name {
  color: var(--ceo-ink);
}
.legend-share {
  color: var(--ceo-muted);
  font-variant-numeric: tabular-nums;
}
.legend-key {
  display: inline-block;
  flex: none;
}
.key-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--key);
}
.key-line {
  width: 14px;
  height: 0;
  border-top: 2px solid var(--key);
  border-radius: 1px;
}
.key-dash {
  width: 14px;
  height: 0;
  border-top: 2px dashed var(--key);
}
.chart-canvas {
  position: relative;
  width: 100%;
}
.chart-canvas.is-donut {
  max-width: 248px;
  margin-inline: auto;
}
.chart-canvas canvas {
  display: block;
}
/* the tooltip card: the popover surface, a hairline, 10px corners and the floating shadow */
.chart-tip {
  position: absolute;
  top: 0;
  left: 0;
  z-index: 3;
  min-width: 140px;
  max-width: 264px;
  padding: 10px 12px;
  border: 1px solid var(--ceo-line);
  border-radius: 10px;
  background: var(--ceo-pop);
  color: var(--ceo-ink);
  box-shadow: var(--ceo-shadow-float);
  pointer-events: none;
  opacity: 0;
  font-size: 12px;
  line-height: 20px;
  text-align: right;
}
.chart-tip :deep(.tip-title) {
  color: var(--ceo-muted);
  margin-bottom: 4px;
}
.chart-tip :deep(.tip-row) {
  display: flex;
  align-items: center;
  gap: 8px;
  white-space: nowrap;
}
.chart-tip :deep(.tip-row strong) {
  font-weight: 500;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}
.chart-tip :deep(.tip-name) {
  color: var(--ceo-muted);
  white-space: normal;
}
.chart-tip :deep(.tip-key) {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex: none;
}
.chart-tip :deep(.tip-key.line) {
  height: 2px;
  width: 14px;
  border-radius: 1px;
}
.chart-masked,
.chart-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  text-align: center;
  padding: 16px;
  border: 1px dashed var(--ceo-line-strong);
  border-radius: 10px;
  background: var(--ceo-cell);
  color: var(--ceo-muted);
  font-size: 13px;
  line-height: 20px;
}
.chart-masked strong {
  color: var(--ceo-ink);
  font-weight: 500;
}
.chart-empty-hint {
  font-size: 12px;
}
.chart-note {
  margin: 8px 0 0;
  font-size: 12px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.chart-table-wrap {
  max-height: 264px;
  overflow: auto;
  border: 1px solid var(--ceo-line);
  border-radius: 10px;
}
.chart-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
  line-height: 20px;
}
.chart-table th,
.chart-table td {
  padding: 6px 12px;
  border-bottom: 1px solid var(--ceo-line-soft);
  text-align: right;
  color: var(--ceo-ink);
  font-weight: 400;
}
.chart-table thead th {
  position: sticky;
  top: 0;
  background: var(--ceo-cell-alt);
  color: var(--ceo-muted);
  font-weight: 500;
  font-size: 12px;
}
.chart-table tbody tr:nth-child(even) {
  background: var(--ceo-cell);
}
.chart-table .num {
  text-align: left;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.chart-table tr:last-child th,
.chart-table tr:last-child td {
  border-bottom: 0;
}
</style>
