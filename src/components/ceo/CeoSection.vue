<script setup lang="ts">
// One module of the company (SectionDto) as a quiet panel: the title with the module's status (shape + colour + words) and its alert count, the KPIs in a hairline strip,
// 1-3 charts, and two actions: «جزئیات» opens the detail table of the section's main drill key (the other tables of the section are one click away in the panel)
// and «تحلیل هوشمند» asks the AI for an analysis of exactly this section (POST ceo/analyze), shown at the bottom of the panel. The module's own page is reached from the
// side column (module pages) and from the drill panel («باز کردن صفحه‌ی ماژول»), so the header keeps only these two actions — and the quiet «بخوان» (voice mode):
// the section's spoken summary (GET ceo/speech, played by the page; «توقف» while it is read).
import { computed } from "vue";
import { useRouter } from "vue-router";
import type { CeoKpi, CeoSection, CeoStatus } from "@/api/CeoAPIs";
import CeoAnalysisBox from "@/components/ceo/CeoAnalysis.vue";
import CeoChart from "@/components/ceo/CeoChart.vue";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { deltaOf, faNum, kpiFullValue, kpiValue, openableLink, splitValue, statusTitles, type AnalysisState, type CeoTheme } from "@/composables/ceo";

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
  if (a?.result && !a.open) return "نمایش تحلیل";
  if (a?.result && a.open) return "تحلیل دوباره";
  return "تحلیل هوشمند";
});
</script>

<template>
  <section :id="'ceo-sec-' + section.key" :aria-labelledby="'ceo-sec-title-' + section.key" :class="'st-' + status" class="ceo-section">
    <header class="sec-head">
      <div class="sec-heading">
        <span :title="statusTitles[status]" class="sec-lamp"><CeoIcon :name="status" :size="18" /></span>
        <h2 :id="'ceo-sec-title-' + section.key" class="sec-title" tabindex="-1">{{ section.title }}</h2>
        <span class="visually-hidden">وضعیت: {{ statusTitles[status] }}</span>
        <span v-if="section.alertCount > 0" class="sec-alerts">{{ faNum(section.alertCount, 0) }} هشدار</span>
      </div>
      <div class="sec-actions">
        <button
          :aria-label="reading ? 'توقف خواندن' : 'خلاصه‌ی صوتی بخش ' + section.title"
          :aria-pressed="reading"
          :title="reading ? 'توقف خواندن' : 'خلاصه‌ی صوتی این بخش'"
          class="ceo-tool ceo-read"
          type="button"
          @click="emit('read')">
          <CeoIcon :name="reading ? 'stop' : 'speaker'" :size="15" />
          {{ reading ? "توقف" : "بخوان" }}
        </button>
        <button :disabled="!mainDrill" class="ceo-btn ghost" type="button" @click="mainDrill && emit('drill', mainDrill)">
          <CeoIcon :size="16" name="list" />
          جزئیات
        </button>
        <button :aria-expanded="!!analysis && analysis.open" :disabled="!!analysis && analysis.busy" class="ceo-btn soft" type="button" @click="emit('analyze')">
          <CeoIcon :size="16" name="analysis" />
          {{ analysisLabel }}
        </button>
      </div>
    </header>

    <div v-if="section.kpis.length" class="sec-kpis">
      <div class="sec-kpis-row">
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
            <CeoIcon v-else-if="c.k.status === 'warn' || c.k.status === 'danger'" :name="c.k.status" :size="15" class="sec-kpi-lamp" />
            <span v-if="c.value.num" class="sec-kpi-num">{{ c.value.num }}</span>
            <span v-if="c.value.unit" class="sec-kpi-unit">{{ c.value.unit }}</span>
          </span>
          <span v-if="c.delta" :class="'tone-' + c.delta.tone" class="sec-kpi-delta"><span aria-hidden="true">{{ c.delta.arrow }}</span> {{ c.delta.text }}</span>
          <span class="visually-hidden">وضعیت {{ statusTitles[c.k.status] }}{{ c.delta ? "، " + c.delta.label : "" }}</span>
        </component>
      </div>
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
  gap: 16px;
  min-width: 0;
  padding: 18px 20px 20px;
  background: var(--ceo-card);
  border: 1px solid var(--ceo-line);
  border-radius: 8px;
  scroll-margin-top: 72px;
}
.sec-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px 12px;
}
.sec-heading {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.sec-lamp {
  display: inline-flex;
  color: var(--ceo-muted);
}
.ceo-section.st-ok .sec-lamp {
  color: var(--ceo-ok);
}
.ceo-section.st-warn .sec-lamp {
  color: var(--ceo-warn);
}
.ceo-section.st-danger .sec-lamp {
  color: var(--ceo-danger);
}
.sec-title {
  margin: 0;
  font-size: 18px;
  line-height: 26px;
  font-weight: 600;
  color: var(--ceo-ink);
  outline: none;
}
.sec-alerts {
  font-size: 12px;
  line-height: 18px;
  color: var(--ceo-muted);
  padding-inline-start: 8px;
  border-inline-start: 1px solid var(--ceo-line);
}
.ceo-section.st-danger .sec-alerts {
  color: var(--ceo-danger-ink);
}
.ceo-section.st-warn .sec-alerts {
  color: var(--ceo-warn-ink);
}
.sec-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}
.sec-kpis {
  border: 1px solid var(--ceo-line);
  border-radius: 6px;
  overflow: hidden;
}
/* a flex strip: the cells of a short last row grow to fill it (no empty cells); each cell draws its end and bottom hairline, the outer ones are clipped */
.sec-kpis-row {
  display: flex;
  flex-wrap: wrap;
  margin-inline-end: -1px;
  margin-bottom: -1px;
}
.sec-kpi {
  flex: 1 1 128px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: 10px 12px;
  background: var(--ceo-card);
  border: 0;
  border-inline-end: 1px solid var(--ceo-line);
  border-bottom: 1px solid var(--ceo-line);
  font: inherit;
  text-align: right;
  color: var(--ceo-ink);
}
.sec-kpi.clickable {
  cursor: pointer;
}
.sec-kpi.clickable:hover {
  background: var(--ceo-card-hover);
}
.sec-kpi:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: -3px;
}
.sec-kpi-title {
  font-size: 12px;
  line-height: 18px;
  color: var(--ceo-muted);
}
.sec-kpi-value {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  column-gap: 5px;
}
.sec-kpi-num {
  font-size: 19px;
  line-height: 28px;
  font-weight: 500;
}
.sec-kpi-unit {
  font-size: 11px;
  line-height: 16px;
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
  font-size: 11px;
  line-height: 16px;
}
.tone-good {
  color: var(--ceo-ok);
}
.tone-bad {
  color: var(--ceo-danger);
}
.tone-flat {
  color: var(--ceo-muted);
}
.ceo-section :deep(.ceo-btn.soft) {
  min-width: 112px;
  justify-content: center;
}
.sec-charts {
  display: flex;
  flex-direction: column;
  gap: 22px;
}
</style>
