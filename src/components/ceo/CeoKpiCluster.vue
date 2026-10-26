<script setup lang="ts">
// The 8 headline KPIs as one instrument strip: a single framed card divided by hairlines (4 × 2 when there is room, 2 × 4 with the class «narrow»), not eight floating cards.
// Each cell: title and status (a small tinted badge with the status shape; the words for screen readers), the number large and light with its unit on its own line
// (short rial; the full value on hover), the change against last month as a pill (arrow + value, tinted by deltaGood), an optional note and the 12-month trend.
// A cell opens the detail table of its drillKey (or its page when it has only a link).
import { computed } from "vue";
import type { CeoKpi } from "@/api/CeoAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import CeoSparkline from "@/components/ceo/CeoSparkline.vue";
import { deltaOf, kpiFullValue, kpiValue, PROTECTED_TEXT, splitValue, statusTitles } from "@/composables/ceo";

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
        <span v-if="c.k.status !== 'neutral'" :title="statusTitles[c.k.status]" class="kpi-badge">
          <CeoIcon :name="c.k.status" :size="14" />
          <span class="visually-hidden">وضعیت: {{ statusTitles[c.k.status] }}</span>
        </span>
      </span>
      <span :title="kpiFullValue(c.k)" class="kpi-value">
        <template v-if="c.k.hidden">
          <CeoIcon :size="18" name="lock" />
          <span class="kpi-masked">{{ PROTECTED_TEXT }}</span>
        </template>
        <span v-else class="kpi-num">{{ c.value.num }}</span>
      </span>
      <span class="kpi-unit">{{ c.k.hidden ? "" : c.value.unit }}</span>
      <span class="kpi-delta-row">
        <span v-if="c.delta" :class="'tone-' + c.delta.tone" class="kpi-delta">
          <span aria-hidden="true" class="kpi-arrow">{{ c.delta.arrow }}</span>
          <span class="kpi-delta-text">{{ c.delta.text }}</span>
        </span>
        <span v-if="c.delta" class="kpi-delta-period">نسبت به ماه قبل</span>
        <span v-else-if="!c.k.hidden" class="kpi-delta-period">بدون مقایسه با ماه قبل</span>
      </span>
      <span v-if="c.k.note" class="kpi-note">{{ c.k.note }}</span>
      <span class="kpi-spark">
        <CeoSparkline v-if="c.k.trend && c.k.trend.length > 1 && !c.k.hidden" :labels="c.k.trendLabels || []" :unit="c.k.unit" :values="c.k.trend" />
      </span>
    </component>
  </section>
</template>

<style scoped>
/* one framed card; the 1px gaps over the soft line colour are the hairline dividers between the cells */
.kpi-cluster {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 1px;
  background: var(--ceo-line-soft);
  border: 1px solid var(--ceo-line);
  border-radius: 16px;
  overflow: hidden;
  box-shadow: var(--ceo-shadow);
  /* the weight of the big numbers (light; 500 was tried, see CEO_FOUNDATION.md) */
  --kpi-num-weight: 300;
}
.kpi-cell {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  min-width: 0;
  padding: 20px 24px 16px;
  background: var(--ceo-card);
  border: 0;
  text-align: right;
  color: var(--ceo-ink);
  font: inherit;
  transition: background-color 120ms ease;
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
  min-height: 24px;
}
.kpi-title {
  font-size: 13px;
  line-height: 20px;
  font-weight: 500;
  color: var(--ceo-muted);
  padding-top: 2px;
}
/* the status as a small tinted badge: the shape in the state colour on its soft fill */
.kpi-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  width: 24px;
  height: 24px;
  border-radius: 999px;
  border: 1px solid transparent;
}
.st-ok .kpi-badge {
  background: var(--ceo-ok-soft);
  border-color: var(--ceo-ok-line);
  color: var(--ceo-ok);
}
.st-warn .kpi-badge {
  background: var(--ceo-warn-soft);
  border-color: var(--ceo-warn-line);
  color: var(--ceo-warn);
}
.st-danger .kpi-badge {
  background: var(--ceo-danger-soft);
  border-color: var(--ceo-danger-line);
  color: var(--ceo-danger);
}
.kpi-value {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 12px;
  min-height: 40px;
  color: var(--ceo-ink);
}
.kpi-num {
  font-size: 34px;
  line-height: 40px;
  font-weight: var(--kpi-num-weight);
  font-variant-numeric: tabular-nums;
}
.kpi-masked {
  font-size: 16px;
  line-height: 24px;
  color: var(--ceo-muted);
}
.kpi-unit {
  min-height: 20px;
  font-size: 12px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.kpi-delta-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 8px;
  margin-top: 12px;
  min-height: 24px;
  font-size: 12px;
  line-height: 20px;
}
/* the change as a pill: arrow and value on a soft tint with a tinted hairline */
.kpi-delta {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 1px 8px;
  border: 1px solid transparent;
  border-radius: 999px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.kpi-arrow {
  font-size: 9px;
  line-height: 1;
}
.tone-good {
  background: var(--ceo-ok-soft);
  border-color: var(--ceo-ok-line);
  color: var(--ceo-ok);
}
.tone-bad {
  background: var(--ceo-danger-soft);
  border-color: var(--ceo-danger-line);
  color: var(--ceo-danger-ink);
}
.tone-flat {
  background: var(--ceo-neutral-soft);
  border-color: var(--ceo-line);
  color: var(--ceo-ink);
}
.kpi-delta-period {
  color: var(--ceo-muted);
}
.kpi-note {
  margin-top: 8px;
  font-size: 12px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.kpi-spark {
  margin-top: auto;
  padding-top: 16px;
  min-height: 56px;
  --ceo-spark: var(--ceo-spark-line);
}
.kpi-cluster.narrow {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.narrow .kpi-cell {
  padding: 16px 20px 12px;
}
@media (max-width: 480px) {
  .kpi-cell {
    padding: 16px 16px 12px;
  }
  /* two lines for every title, so the numbers of a row stay level */
  .kpi-top {
    min-height: 40px;
  }
  .kpi-num {
    font-size: 26px;
    line-height: 32px;
  }
  .kpi-value {
    min-height: 32px;
    margin-top: 8px;
  }
  .kpi-delta-period {
    display: none;
  }
}
@media (prefers-reduced-motion: reduce) {
  .kpi-cell {
    transition: none;
  }
}
</style>
