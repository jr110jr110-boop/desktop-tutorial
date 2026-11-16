<script setup lang="ts">
// The tile «منابع بیش‌تخصیص‌یافته» of the CEO page (priority 4e): the number of resources whose load exceeds their capacity (open over-allocations found by the planning agent),
// with the ones the PMO accepted on purpose counted apart, linked to the over-allocations of the resources page. The counts are those of the CEO dashboard service (pcOverloadedResources, pcOverloadsAccepted).
// It follows the style of the headline cells; when the counts cannot be read the tile says so and still links to the page.
import { computed, onMounted, ref } from "vue";
import { loadCeoDashboardShared } from "@/composables/ceoDashboard";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { faNum } from "@/composables/ceo";
import { overloadsPath } from "@/composables/resources";

const open = ref<number | null>(null);
const accepted = ref<number | null>(null);
const failed = ref(false);

async function load() {
  const [data, error] = await loadCeoDashboardShared();
  if (error || !data) { failed.value = true; return; }
  open.value = Number(data.pcOverloadedResources ?? 0);
  accepted.value = Number(data.pcOverloadsAccepted ?? 0);
}
onMounted(load);

const status = computed(() => (open.value === null ? "neutral" : open.value > 0 ? "warn" : "ok"));
const note = computed(() => (accepted.value ? `${faNum(accepted.value, 0)} مورد را PMO عمدا پذیرفته است (جدا شمرده می‌شود)` : "بار طرح‌های تاییدشده و تولید بیش از ظرفیت منبع"));
</script>

<template>
  <section aria-label="منابع بیش‌تخصیص‌یافته" class="ceo-res-tile">
    <router-link :to="overloadsPath()" :class="['st-' + status]" class="res-cell" data-test="ceo-overload-tile">
      <span class="res-title">منابع بیش‌تخصیص‌یافته</span>
      <span class="res-value">
        <CeoIcon v-if="status === 'warn'" :size="18" name="warn" />
        <span v-if="open !== null" class="res-num">{{ faNum(open, 0) }}</span>
        <span v-else class="res-num">{{ failed ? "—" : "…" }}</span>
        <span class="res-unit">منبع</span>
      </span>
      <span class="res-note">{{ failed ? "تعداد خوانده نشد؛ برای دیدن بیش‌تخصیص‌ها بزنید." : note }}</span>
      <span class="res-link">باز کردن بیش‌تخصیص‌ها</span>
    </router-link>
  </section>
</template>

<style scoped>
.ceo-res-tile { margin-top: 12px; }
.res-cell {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 16px;
  padding: 14px 24px;
  background: var(--ceo-card);
  border: 1px solid var(--ceo-line);
  border-radius: 16px;
  box-shadow: var(--ceo-shadow);
  color: var(--ceo-ink);
  text-decoration: none;
}
.res-cell:hover { background: var(--ceo-card-hover); }
.res-cell:focus-visible { outline: 2px solid var(--ceo-focus); outline-offset: 2px; }
.res-title { font-size: 13px; font-weight: 500; color: var(--ceo-muted); }
.res-value { display: inline-flex; align-items: baseline; gap: 6px; }
.res-num { font-size: 28px; font-weight: 300; }
.res-unit { font-size: 13px; color: var(--ceo-muted); }
.res-note { font-size: 12px; color: var(--ceo-muted); flex: 1 1 220px; }
.res-link { font-size: 12px; text-decoration: underline; }
.st-warn .res-num { color: var(--ceo-warn, #b26a00); }
</style>
