<script setup lang="ts">
// One module of the company (SectionDto) as a card: the module's icon in a tinted circle, the title, the status as a pill (shape + colour + word) and the alert count;
// the KPIs as a soft grid of cells; 1-3 charts. Three icon actions with tooltips: «بخوان» (the spoken summary, GET ceo/speech, played by the page; a stop mark while
// it is read), «جزئیات» (the detail table of the section's main drill key; the other tables of the section are one click away in the panel) and «تحلیل هوشمند»
// (POST ceo/analyze for exactly this section, shown at the bottom of the card). The module's own page is reached from the side column and from the drill panel.
import { computed } from "vue";
import { useRouter } from "vue-router";
import type { CeoKpi, CeoSection, CeoStatus } from "@/api/CeoAPIs";
import CeoAnalysisBox from "@/components/ceo/CeoAnalysis.vue";
import CeoChart from "@/components/ceo/CeoChart.vue";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { deltaOf, faNum, kpiFullValue, kpiValue, openableLink, splitValue, statusTitles, type AnalysisState, type CeoTheme } from "@/composables/ceo";

/** the line icon of each module (CeoIcon names); an unknown section key falls back to the chart icon */
const moduleIcons: Record<string, string> = {
  finance: "mod-finance",
  sales: "mod-sales",
  projects: "mod-projects",
  production: "mod-production",
  quality: "mod-quality",
  inventory: "mod-inventory",
  procurement: "mod-procurement",
  service: "mod-service",
  office: "mod-office",
};

const props = defineProps({
  section: { type: Object as () => CeoSection, required: true },
  status: { type: String as () => CeoStatus, default: "neutral" },
  theme: { type: String as () => CeoTheme, default: "light" },
  analysis: { type: Object as () => AnalysisState | null, default: null },
  /** its spoken summary is being read */
  reading: { type: Boolean, default: false },
});
const emit = defineEmits<{
  (e: "drill", key: string): void;
  (e: "analyze"): void;
  (e: "close-analysis"): void;
  (e: "read"): void;
}>();

const router = useRouter();
/** SectionDto has no main drill key: the first chart's table, else the first KPI's. */
const mainDrill = computed(() => props.section.charts.find((c) => c.drillKey)?.drillKey || props.section.kpis.find((k) => k.drillKey)?.drillKey || null);
const kpiCells = computed(() => props.section.kpis.map((k) => ({ k, delta: deltaOf(k), value: splitValue(kpiValue(k)) })));
function openKpi(k: CeoKpi) {
  if (k.drillKey) emit("drill", k.drillKey);
  else {
    const to = openableLink(router, k.link);
    if (to) router.push(to);
  }
}
const analysisLabel = computed(() => {
  const a = props.analysis;
  if (a?.busy) return "در حال تحلیل…";
  if (a?.result && !a.open) return "نمایش تحلیل هوشمند";
  if (a?.result && a.open) return "تحلیل دوباره";
  return "تحلیل هوشمند این بخش";
});
const icon = computed(() => moduleIcons[props.section.key] || "chart");
</script>

<template>
  <section :id="'ceo-sec-' + section.key" :aria-labelledby="'ceo-sec-title-' + section.key" :class="'st-' + status" class="ceo-section">
    <header class="sec-head">
      <div class="sec-heading">
        <span aria-hidden="true" class="sec-icon"><CeoIcon :name="icon" :size="18" /></span>
        <h2 :id="'ceo-sec-title-' + section.key" class="sec-title" tabindex="-1">{{ section.title }}</h2>
        <span class="sec-sub">
          <span class="sec-status"><CeoIcon :name="status" :size="13" />{{ statusTitles[status] }}</span>
          <span v-if="section.alertCount > 0" class="sec-alerts">{{ faNum(section.alertCount, 0) }} هشدار</span>
        </span>
      </div>
      <div class="sec-actions">
        <button
          :aria-label="reading ? 'توقف خواندن' : 'خلاصه‌ی صوتی بخش ' + section.title"
          :aria-pressed="reading"
          :data-tip="reading ? 'توقف خواندن' : 'بخوان: خلاصه‌ی صوتی'"
          class="ceo-icon-btn ceo-read"
          type="button"
          @click="emit('read')">
          <CeoIcon :name="reading ? 'stop' : 'speaker'" :size="17" />
        </button>
        <button :aria-label="'جزئیات ' + section.title" :disabled="!mainDrill" class="ceo-icon-btn" data-tip="جزئیات" type="button" @click="mainDrill && emit('drill', mainDrill)">
          <CeoIcon :size="17" name="list" />
        </button>
        <button
          :aria-busy="!!analysis && analysis.busy"
          :aria-expanded="!!analysis && analysis.open"
          :aria-label="analysisLabel"
          :class="{ on: !!analysis && analysis.open && !!analysis.result }"
          :data-tip="analysisLabel"
          :disabled="!!analysis && analysis.busy"
          class="ceo-icon-btn sec-analyze"
          type="button"
          @click="emit('analyze')">
          <CeoIcon :class="{ spinning: !!analysis && analysis.busy }" :name="analysis && analysis.busy ? 'refresh' : 'analysis'" :size="17" />
        </button>
      </div>
    </header>

    <div v-if="section.kpis.length" class="sec-kpis">
      <component
        :is="c.k.drillKey || c.k.link ? 'button' : 'div'"
        v-for="c in kpiCells"
        :key="c.k.key"
        :class="['st-' + c.k.status, { clickable: c.k.drillKey || c.k.link, masked: c.k.hidden }]"
        :title="kpiFullValue(c.k) + (c.k.note ? ' — ' + c.k.note : '')"
        :type="c.k.drillKey || c.k.link ? 'button' : undefined"
        class="sec-kpi"
        @click="openKpi(c.k)">
        <span class="sec-kpi-title">{{ c.k.title }}</span>
        <span class="sec-kpi-value">
          <CeoIcon v-if="c.k.hidden" :size="15" name="lock" />
          <CeoIcon v-else-if="c.k.status === 'warn' || c.k.status === 'danger'" :name="c.k.status" :size="14" class="sec-kpi-lamp" />
          <span v-if="c.value.num" class="sec-kpi-num">{{ c.value.num }}</span>
          <span v-if="c.value.unit" class="sec-kpi-unit">{{ c.value.unit }}</span>
        </span>
        <span v-if="c.delta" :class="'tone-' + c.delta.tone" class="sec-kpi-delta"><span aria-hidden="true">{{ c.delta.arrow }}</span> {{ c.delta.text }}</span>
        <span class="visually-hidden">وضعیت {{ statusTitles[c.k.status] }}{{ c.delta ? "، " + c.delta.label : "" }}</span>
      </component>
    </div>

    <div class="sec-charts">
      <CeoChart v-for="ch in section.charts" :key="ch.key" :chart="ch" :theme="theme" @drill="emit('drill', $event)" />
    </div>

    <CeoAnalysisBox v-if="analysis && analysis.open" :busy="analysis.busy" :error="analysis.error" :result="analysis.result" :started-at="analysis.startedAt" @close="emit('close-analysis')" @retry="emit('analyze')" />
  </section>
</template>

<style scoped>
.ceo-section {
  display: flex;
  flex-direction: column;
  gap: 24px;
  min-width: 0;
  padding: 24px;
  background: var(--ceo-card);
  border: 1px solid var(--ceo-line);
  border-radius: 16px;
  box-shadow: var(--ceo-shadow);
  scroll-margin-top: 76px;
}
.sec-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
/* the heading in two lines whatever the title's length: the icon beside both, the title, the status pill under it */
.sec-heading {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  align-items: center;
  column-gap: 12px;
  row-gap: 2px;
  min-width: 0;
  flex: 1;
}
/* the module's icon in a tinted circle */
.sec-icon {
  grid-row: 1 / span 2;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border-radius: 999px;
  background: var(--ceo-primary-soft);
  color: var(--ceo-primary);
}
.sec-title {
  grid-column: 2;
  margin: 0;
  font-size: 18px;
  line-height: 26px;
  font-weight: 700;
  color: var(--ceo-ink);
  outline: none;
}
/* under the title: the status as a pill (shape, colour and word), then the alert count */
.sec-sub {
  grid-column: 2;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 10px;
}
.sec-alerts {
  font-size: 12px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.sec-status {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 1px 10px 1px 8px;
  border: 1px solid var(--ceo-line);
  border-radius: 999px;
  background: var(--ceo-neutral-soft);
  color: var(--ceo-muted);
  font-size: 12px;
  line-height: 20px;
  font-weight: 500;
  white-space: nowrap;
}
.st-ok .sec-status {
  background: var(--ceo-ok-soft);
  border-color: var(--ceo-ok-line);
  color: var(--ceo-ok);
}
.st-warn .sec-status {
  background: var(--ceo-warn-soft);
  border-color: var(--ceo-warn-line);
  color: var(--ceo-warn-ink);
}
.st-danger .sec-status {
  background: var(--ceo-danger-soft);
  border-color: var(--ceo-danger-line);
  color: var(--ceo-danger-ink);
}
.sec-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex: none;
}
/* the KPIs: a soft grid of cells, no borders, fills alternating a shade; the cells of a short last row grow to fill it (no empty slot) */
.sec-kpis {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.sec-kpi {
  flex: 1 1 148px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  padding: 12px 16px;
  background: var(--ceo-cell);
  border: 0;
  border-radius: 10px;
  font: inherit;
  text-align: right;
  color: var(--ceo-ink);
  transition: background-color 120ms ease;
}
.sec-kpi:nth-child(even) {
  background: var(--ceo-cell-alt);
}
.sec-kpi.clickable {
  cursor: pointer;
}
.sec-kpi.clickable:hover {
  background: var(--ceo-primary-soft);
}
.sec-kpi:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: 2px;
}
.sec-kpi-title {
  font-size: 12px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.sec-kpi-value {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  column-gap: 6px;
}
.sec-kpi-num {
  font-size: 20px;
  line-height: 28px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}
.sec-kpi-unit {
  font-size: 12px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.sec-kpi-lamp,
.sec-kpi-value > .ceo-icon {
  align-self: center;
}
.masked .sec-kpi-value {
  align-items: center;
}
.masked .sec-kpi-unit {
  font-size: 13px;
  line-height: 28px;
}
.sec-kpi.st-warn .sec-kpi-lamp {
  color: var(--ceo-warn);
}
.sec-kpi.st-danger .sec-kpi-lamp {
  color: var(--ceo-danger);
}
.sec-kpi-delta {
  font-size: 12px;
  line-height: 20px;
  font-variant-numeric: tabular-nums;
}
.tone-good {
  color: var(--ceo-ok);
}
.tone-bad {
  color: var(--ceo-danger-ink);
}
.tone-flat {
  color: var(--ceo-muted);
}
.sec-charts {
  display: flex;
  flex-direction: column;
  gap: 32px;
}
@media (prefers-reduced-motion: reduce) {
  .sec-kpi {
    transition: none;
  }
}
</style>
