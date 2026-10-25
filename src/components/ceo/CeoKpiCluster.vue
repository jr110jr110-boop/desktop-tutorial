<script setup lang="ts">
// The 8 headline KPIs as one instrument cluster: a single frame divided by hairlines (4 × 2 when there is room, 2 × 4 with the class «narrow»), not eight floating cards.
// Each cell: title, status (shape + colour + text for screen readers), the value (short rial; the full value on hover), the change against last month (arrow, colour by deltaGood,
// and words), an optional note and the 12-month trend. A cell opens the detail table of its drillKey (or its page when it has only a link).
import { computed } from "vue";
import type { CeoKpi } from "@/api/CeoAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import CeoSparkline from "@/components/ceo/CeoSparkline.vue";
import { deltaOf, kpiFullValue, kpiValue, splitValue, statusTitles } from "@/composables/ceo";

const props = defineProps({
  kpis: { type: Array as () => CeoKpi[], required: true },
});
const emit = defineEmits<{ (e: "open", kpi: CeoKpi): void }>();

const cells = computed(() =>
  props.kpis.map((k) => {
    const delta = deltaOf(k);
    const opens = !!(k.drillKey || k.link);
    const label = [k.title, kpiFullValue(k), "وضعیت " + statusTitles[k.status], delta ? delta.label : "", k.drillKey ? "باز کردن جدول جزئیات" : ""].filter(Boolean).join("، ");
    return { k, delta, opens, label, value: splitValue(kpiValue(k)) };
  })
);
</script>

<template>
  <section aria-label="شاخص‌های اصلی" class="kpi-cluster">
    <component
      :is="c.opens ? 'button' : 'div'"
      v-for="c in cells"
      :key="c.k.key"
      :aria-label="c.opens ? c.label : undefined"
      :class="['st-' + c.k.status, { masked: c.k.hidden, clickable: c.opens }]"
      :type="c.opens ? 'button' : undefined"
      class="kpi-cell"
      @click="c.opens && emit('open', c.k)">
      <span class="kpi-top">
        <span class="kpi-title">{{ c.k.title }}</span>
        <span v-if="c.k.status !== 'neutral'" :title="statusTitles[c.k.status]" class="kpi-lamp">
          <CeoIcon :name="c.k.status" :size="17" />
          <span class="visually-hidden">وضعیت: {{ statusTitles[c.k.status] }}</span>
        </span>
      </span>
      <span :title="kpiFullValue(c.k)" class="kpi-value">
        <CeoIcon v-if="c.k.hidden" :size="18" name="lock" />
        <span v-if="c.value.num" class="kpi-num">{{ c.value.num }}</span>
        <span v-if="c.value.unit" class="kpi-unit">{{ c.value.unit }}</span>
      </span>
      <span v-if="c.delta" :class="'tone-' + c.delta.tone" class="kpi-delta">
        <span aria-hidden="true" class="kpi-arrow">{{ c.delta.arrow }}</span>
        <span class="kpi-delta-text">{{ c.delta.text }}</span>
        <span class="kpi-delta-period">نسبت به ماه قبل</span>
      </span>
      <span v-else-if="!c.k.hidden" class="kpi-delta tone-flat"><span class="kpi-delta-period">بدون مقایسه با ماه قبل</span></span>
      <span v-if="c.k.note" class="kpi-note">{{ c.k.note }}</span>
      <span class="kpi-spark">
        <CeoSparkline v-if="c.k.trend && c.k.trend.length > 1 && !c.k.hidden" :labels="c.k.trendLabels || []" :unit="c.k.unit" :values="c.k.trend" />
      </span>
    </component>
  </section>
</template>

<style scoped>
.kpi-cluster {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  background: var(--ceo-line);
  gap: 1px;
  border: 1px solid var(--ceo-line);
  border-radius: 10px;
  overflow: hidden;
}
.kpi-cell {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 2px;
  min-width: 0;
  padding: 14px 16px 10px;
  background: var(--ceo-card);
  border: 0;
  text-align: right;
  color: var(--ceo-ink);
  font: inherit;
  position: relative;
}
.kpi-cell.clickable {
  cursor: pointer;
}
.kpi-cell.clickable:hover {
  background: var(--ceo-card-hover);
}
.kpi-cell:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: -3px;
  z-index: 1;
}
.kpi-top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
}
.kpi-title {
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.kpi-lamp {
  display: inline-flex;
  flex: none;
}
.st-ok .kpi-lamp {
  color: var(--ceo-ok);
}
.st-warn .kpi-lamp {
  color: var(--ceo-warn);
}
.st-danger .kpi-lamp {
  color: var(--ceo-danger);
}
.kpi-value {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  column-gap: 7px;
  margin-top: 2px;
  min-height: 40px;
  color: var(--ceo-ink);
}
.kpi-num {
  font-size: 30px;
  line-height: 40px;
  font-weight: 500;
  letter-spacing: -0.2px;
}
.kpi-unit {
  font-size: 14px;
  line-height: 22px;
  color: var(--ceo-muted);
}
.masked .kpi-value {
  align-items: center;
  gap: 6px;
}
.masked .kpi-unit {
  font-size: 15px;
}
.kpi-delta {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0 6px;
  font-size: 12px;
  line-height: 18px;
}
.kpi-arrow {
  font-size: 10px;
}
.kpi-delta-text {
  font-weight: 500;
}
.tone-good .kpi-arrow,
.tone-good .kpi-delta-text {
  color: var(--ceo-ok);
}
.tone-bad .kpi-arrow,
.tone-bad .kpi-delta-text {
  color: var(--ceo-danger);
}
.tone-flat .kpi-arrow,
.tone-flat .kpi-delta-text {
  color: var(--ceo-ink);
}
.kpi-delta-period {
  color: var(--ceo-muted);
}
.kpi-note {
  font-size: 12px;
  line-height: 18px;
  color: var(--ceo-muted);
}
.kpi-spark {
  margin-top: auto;
  padding-top: 8px;
  min-height: 48px;
  --ceo-spark: var(--ceo-spark-line);
  --ceo-primary-mark: var(--ceo-primary);
}
.kpi-cluster.narrow {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.narrow .kpi-cell {
  padding: 12px 14px 8px;
}
@media (max-width: 480px) {
  .kpi-num {
    font-size: 22px;
    line-height: 32px;
  }
  .kpi-unit {
    font-size: 12px;
    line-height: 18px;
  }
  .kpi-delta-period {
    display: none;
  }
}
</style>
