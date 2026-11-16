<script setup lang="ts">
// The permanent engineering alarm of the CEO page (ENG-E1): a red band straight after the pulse bar, visible without scrolling, whenever anything of engineering waits for the CEO
// or the server flags an engineering alarm. Every item is a link to its list. Nothing waiting = nothing shown. What waits for the CEO never expires; the band stays until it is decided.
import { computed, onMounted, ref } from "vue";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { faNum } from "@/composables/ceo";
import { loadCeoDashboardShared } from "@/composables/ceoDashboard";
import { enPaths, waitText } from "@/composables/engineering";

const d = ref<any>(null);
onMounted(async () => {
  const [data, error] = await loadCeoDashboardShared();
  if (!error && data) d.value = data;
});

const num = (v: any) => Number(v ?? 0) || 0;
const items = computed(() => {
  const x = d.value;
  if (!x) return [];
  const rows: { key: string; text: string; to: any }[] = [];
  if (num(x.enWaiversAwaitingCeo) > 0)
    rows.push({ key: "waivers", text: `${faNum(num(x.enWaiversAwaitingCeo), 0)} مجوز اضطرار منتظر تصمیم شماست` + (num(x.enOldestWaiverWaitingHours) > 0 ? ` (قدیمی‌ترین ${waitText(x.enOldestWaiverWaitingHours)})` : ""), to: enPaths.waivers });
  if (num(x.enDeviationsAwaiting) > 0) rows.push({ key: "deviations", text: `${faNum(num(x.enDeviationsAwaiting), 0)} انحراف از استاندارد منتظر تصمیم است`, to: enPaths.deviations });
  if (num(x.enPlatformRegistrationsAwaiting) > 0) rows.push({ key: "regs", text: `${faNum(num(x.enPlatformRegistrationsAwaiting), 0)} ثبت پلتفرم جدید منتظر تصمیم شماست`, to: enPaths.registrations });
  if (num(x.enAwardedCasesAtDocumentRisk) > 0) rows.push({ key: "risk", text: `${faNum(num(x.enAwardedCasesAtDocumentRisk), 0)} پرونده برنده‌شده در معرض خطر مدارک است`, to: enPaths.cases });
  if (num(x.enOverdueDebts) > 0) rows.push({ key: "debts", text: `${faNum(num(x.enOverdueDebts), 0)} بدهی مدارک از موعد گذشته است`, to: { path: enPaths.debts, query: { overdue: 1 } } });
  if (num(x.enStandardGapsOpen) > 0) rows.push({ key: "gaps", text: `${faNum(num(x.enStandardGapsOpen), 0)} گزارش تفاوت دو استاندارد باز است`, to: enPaths.gaps });
  if (!rows.length && x.enPlatformsAlarm) rows.push({ key: "platforms", text: "پرونده مدارک پلتفرم‌ها در هشدار است", to: enPaths.platforms });
  return rows;
});
</script>

<template>
  <section v-if="items.length" aria-label="هشدار مهندسی" class="ceo-en-alarm" data-test="ceo-en-alarm" role="alert">
    <span class="alarm-title"><CeoIcon :size="18" name="warn" /> هشدار مهندسی</span>
    <router-link v-for="i in items" :key="i.key" :to="i.to" :data-test="'ceo-en-alarm-' + i.key" class="alarm-item">{{ i.text }}</router-link>
  </section>
</template>

<style scoped>
.ceo-en-alarm { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 22px; margin-top: 12px; padding: 12px 20px; border-radius: 16px; background: #fdecee; border: 2px solid var(--ceo-bad, #b3261e); color: #7a1a14; }
.alarm-title { display: inline-flex; align-items: center; gap: 6px; font-weight: 700; }
.alarm-item { color: #7a1a14; font-weight: 600; text-decoration: underline; }
.alarm-item:focus-visible { outline: 2px solid var(--ceo-focus); outline-offset: 2px; }
</style>
