<script setup lang="ts">
// The side column of the dashboard: the alerts of the whole company grouped by severity (each opens its page), the count of new AI notes, the catalogue of every detail table,
// the module pages, and the "snapshot now" action of the KPI trends. layout: «column» beside the sections (stays in view while they scroll), «band» above them in two columns
// (tablet), «stack» one column (phone). On a phone the warning and information groups start folded so the sections are reached quickly; the critical ones are always open.
import { computed } from "vue";
import { useRouter } from "vue-router";
import type { CeoAlert } from "@/api/CeoAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { faNum, openableLink, severityTitles } from "@/composables/ceo";

const props = defineProps({
  alerts: { type: Array as () => CeoAlert[], default: () => [] },
  notesCount: { type: Number as () => number | null, default: null },
  quickLinks: { type: Array as () => { title: string; to: string }[], default: () => [] },
  snapshotBusy: { type: Boolean, default: false },
  layout: { type: String as () => "column" | "band" | "stack", default: "column" },
});
const emit = defineEmits<{ (e: "notes"): void; (e: "catalogue"): void; (e: "snapshot"): void }>();

const router = useRouter();
const order: CeoAlert["severity"][] = ["danger", "warning", "info"];
const icon: Record<string, string> = { danger: "danger", warning: "warn", info: "info" };
const groups = computed(() =>
  order
    .map((sev) => ({ sev, title: severityTitles[sev], items: props.alerts.filter((a) => a.severity === sev).map((a) => ({ ...a, to: openableLink(router, a.link) })) }))
    .filter((g) => g.items.length)
);
</script>

<template>
  <aside :class="layout" aria-labelledby="ceo-alerts-title" class="side">
    <section class="side-block">
      <h2 id="ceo-alerts-title" class="side-title">
        هشدارها
        <span v-if="alerts.length" class="side-count">{{ faNum(alerts.length, 0) }}</span>
      </h2>
      <p v-if="!alerts.length" class="side-empty">هشداری نیست؛ همه‌ی ماژول‌ها در وضعیت عادی‌اند.</p>
      <details v-for="g in groups" :key="g.sev + layout" :class="'sev-' + g.sev" :open="g.sev === 'danger' || layout !== 'stack'" class="alert-group">
        <summary class="alert-group-title">
          <CeoIcon :name="icon[g.sev]" :size="16" />
          {{ g.title }}
          <span class="alert-group-count">{{ faNum(g.items.length, 0) }}</span>
          <CeoIcon :size="14" class="alert-fold" name="chevron-down" />
        </summary>
        <ul class="alert-list">
          <li v-for="(a, i) in g.items" :key="i">
            <a v-if="a.to" :href="router.resolve(a.to).href" class="alert-item" @click.prevent="router.push(a.to)">
              <span class="alert-text">{{ a.text }}</span>
              <CeoIcon :size="14" class="alert-go" name="chevron" />
            </a>
            <span v-else class="alert-item static"><span class="alert-text">{{ a.text }}</span></span>
          </li>
        </ul>
      </details>
    </section>

    <section class="side-block">
      <button class="side-row" type="button" @click="emit('notes')">
        <CeoIcon :size="18" name="note" />
        <span class="side-row-text">یادداشت‌های تازه‌ی هوش مصنوعی</span>
        <span v-if="notesCount === null" class="side-row-value muted">—</span>
        <span v-else :class="{ fresh: notesCount > 0 }" class="side-row-value">{{ faNum(notesCount, 0) }}</span>
      </button>
      <button class="side-row" type="button" @click="emit('catalogue')">
        <CeoIcon :size="18" name="list" />
        <span class="side-row-text">همه‌ی جدول‌های جزئیات</span>
        <CeoIcon :size="14" name="chevron" />
      </button>
    </section>

    <section v-if="quickLinks.length" aria-labelledby="ceo-links-title" class="side-block">
      <h2 id="ceo-links-title" class="side-title small">صفحه‌های ماژول‌ها</h2>
      <ul class="link-list">
        <li v-for="l in quickLinks" :key="l.to">
          <a :href="router.resolve(l.to).href" @click.prevent="router.push(l.to)">{{ l.title }}</a>
        </li>
      </ul>
    </section>

    <section class="side-block quiet">
      <button :disabled="snapshotBusy" class="ceo-btn ghost small" title="نقطه‌ی امروز روندها را همین حالا ثبت می‌کند (کار شبانه هم هر شب ساعت ۰۰:۳۰ همین کار را می‌کند)" type="button" @click="emit('snapshot')">
        <CeoIcon :size="15" name="camera" />
        {{ snapshotBusy ? "در حال ثبت…" : "ثبت نقطه‌ی امروز روندها" }}
      </button>
    </section>
  </aside>
</template>

<style scoped>
.side {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.side.band {
  display: grid;
  grid-template-columns: minmax(0, 3fr) minmax(0, 2fr);
  grid-template-rows: auto auto 1fr;
  align-items: start;
}
.side.band > :first-child {
  grid-row: span 3;
}
.side-block {
  background: var(--ceo-card);
  border: 1px solid var(--ceo-line);
  border-radius: 8px;
  padding: 14px 16px;
}
.side-block.quiet {
  background: transparent;
  border: 0;
  padding: 0 4px;
}
.side-title {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0 0 10px;
  font-size: 16px;
  line-height: 24px;
  font-weight: 600;
  color: var(--ceo-ink);
}
.side-title.small {
  font-size: 14px;
  line-height: 22px;
  margin-bottom: 6px;
}
.side-count {
  font-size: 12px;
  line-height: 18px;
  font-weight: 500;
  color: var(--ceo-muted);
}
.side-empty {
  margin: 0;
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-muted);
}
.alert-group + .alert-group {
  margin-top: 12px;
}
.alert-group-title {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 0 0 4px;
  font-size: 13px;
  line-height: 20px;
  font-weight: 500;
  color: var(--ceo-ink);
  list-style: none;
  cursor: pointer;
}
.alert-group-title::-webkit-details-marker {
  display: none;
}
.alert-group-title .alert-fold {
  margin-inline-start: auto;
  color: var(--ceo-muted) !important;
}
details:not([open]) .alert-fold {
  transform: rotate(90deg);
}
.alert-group-title:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: 2px;
  border-radius: 2px;
}
.sev-danger .alert-group-title :deep(.ceo-icon) {
  color: var(--ceo-danger);
}
.sev-warning .alert-group-title :deep(.ceo-icon) {
  color: var(--ceo-warn);
}
.sev-info .alert-group-title :deep(.ceo-icon) {
  color: var(--ceo-info);
}
.alert-group-count {
  color: var(--ceo-muted);
  font-weight: 400;
}
.alert-list {
  list-style: none;
  margin: 0;
  padding: 0;
}
.alert-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 7px 0 7px 2px;
  border-top: 1px solid var(--ceo-line-soft);
  color: var(--ceo-ink);
  text-decoration: none;
  font-size: 13px;
  line-height: 21px;
}
li:first-child .alert-item {
  border-top: 0;
}
.sev-danger .alert-item {
  padding-inline-start: 10px;
  box-shadow: inset -2px 0 0 var(--ceo-danger);
}
.alert-text {
  flex: 1;
  min-width: 0;
}
.alert-go {
  margin-top: 4px;
  color: var(--ceo-muted);
  transform: scaleX(-1);
}
a.alert-item:hover .alert-text {
  text-decoration: underline;
  text-underline-offset: 3px;
}
a.alert-item:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: 2px;
  border-radius: 2px;
}
.side-row {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 8px 0;
  border: 0;
  background: none;
  color: var(--ceo-ink);
  font: inherit;
  font-size: 13px;
  line-height: 20px;
  text-align: right;
  cursor: pointer;
}
.side-row + .side-row {
  border-top: 1px solid var(--ceo-line-soft);
}
.side-row :deep(.ceo-icon) {
  color: var(--ceo-muted);
}
.side-row:last-child :deep(.ceo-icon:last-child) {
  transform: scaleX(-1);
}
.side-row-text {
  flex: 1;
}
.side-row-value {
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}
.side-row-value.fresh {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.side-row-value.fresh::before {
  content: "";
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--ceo-brass);
}
.side-row-value.muted {
  color: var(--ceo-muted);
}
.side-row:hover .side-row-text {
  text-decoration: underline;
  text-underline-offset: 3px;
}
.side-row:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: 2px;
}
.link-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 2px 12px;
}
.link-list a {
  display: block;
  padding: 4px 0;
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-primary);
  text-decoration: none;
}
.link-list a:hover {
  text-decoration: underline;
  text-underline-offset: 3px;
}
</style>
