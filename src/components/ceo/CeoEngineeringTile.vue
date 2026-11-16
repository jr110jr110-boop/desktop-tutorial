<script setup lang="ts">
// The engineering tiles of the CEO page (ENG-E1), wired like the over-allocated-resources tile: the counts are those of the CEO dashboard service (every en* field), each tile links to its
// list. A tile is in its alarm state when the server says so (enPlatformsAlarm) or when something waits for the CEO or is overdue; a request that waits for the CEO never expires.
import { computed, onMounted, ref } from "vue";
import { getCeoDashboard } from "@/api/FinanceAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { faNum } from "@/composables/ceo";
import { enPaths, waitText } from "@/composables/engineering";

const d = ref<any>(null);
const failed = ref(false);
async function load() {
  const [data, error] = await getCeoDashboard();
  if (error || !data) {
    failed.value = true;
    return;
  }
  d.value = data;
}
onMounted(load);
const n = (v: any) => (d.value ? faNum(Number(v ?? 0), 0) : failed.value ? "—" : "…");
const tiles = computed(() => {
  const x = d.value || {};
  const w = Number(x.enWaiversAwaitingCeo ?? 0);
  return [
    { key: "platforms", title: "پلتفرم‌های ناقص", value: n(x.enPlatformsIncomplete), unit: "از " + n(x.enPlatformsTotal), to: enPaths.platforms, alarm: !!x.enPlatformsAlarm, warn: Number(x.enPlatformsIncomplete) > 0,
      note: Number(x.enAwardedCasesAtDocumentRisk) > 0 ? `${n(x.enAwardedCasesAtDocumentRisk)} پرونده برنده‌شده در معرض خطر مدارک است` : "مدارک این پلتفرم‌ها کامل و تایید نشده است" },
    { key: "debts", title: "بدهی مدارک مهندسی", value: n(x.enOpenDebts), unit: "باز", to: enPaths.debts, alarm: Number(x.enOverdueDebts) > 0, warn: Number(x.enOpenDebts) > 0,
      note: Number(x.enOverdueDebts) > 0 ? `${n(x.enOverdueDebts)} بدهی از موعد گذشته است` : "بدهی‌ای از موعد نگذشته" },
    { key: "waivers", title: "معافیت اضطراری منتظر شما", value: n(x.enWaiversAwaitingCeo), unit: "درخواست", to: enPaths.waivers, alarm: w > 0, warn: false,
      note: w > 0 ? `قدیمی‌ترین ${waitText(x.enOldestWaiverWaitingHours)} منتظر است؛ منقضی نمی‌شود` : "درخواستی منتظر نیست" },
    { key: "risk", title: "پرونده در معرض خطر مدارک", value: n(x.enCasesAtDocumentRisk), unit: "پرونده", to: enPaths.cases, alarm: Number(x.enAwardedCasesAtDocumentRisk) > 0, warn: Number(x.enCasesAtDocumentRisk) > 0,
      note: `${n(x.enAwardedCasesAtDocumentRisk)} مورد برنده‌شده است` },
    { key: "deviations", title: "انحراف از استاندارد", value: n(x.enDeviationsAwaiting), unit: "منتظر تصمیم", to: enPaths.deviations, alarm: false, warn: Number(x.enDeviationsAwaiting) > 0,
      note: `${n(x.enStandardsDeviationsOpen)} انحراف و معافیت باز` },
    { key: "gaps", title: "شکاف استاندارد باز", value: n(x.enStandardGapsOpen), unit: "گزارش", to: enPaths.gaps, alarm: false, warn: Number(x.enStandardGapsOpen) > 0, note: "منتظر تایید مدیر مهندسی" },
    { key: "regs", title: "ثبت پلتفرم جدید", value: n(x.enPlatformRegistrationsAwaiting), unit: "منتظر شما", to: enPaths.registrations, alarm: Number(x.enPlatformRegistrationsAwaiting) > 0, warn: false, note: "طراحی‌های جدیدی که برای ثبت به‌عنوان پلتفرم پیشنهاد شده" },
  ];
});
</script>

<template>
  <section aria-label="مهندسی" class="ceo-en-tile">
    <router-link v-for="t in tiles" :key="t.key" :to="t.to" :class="['en-cell', t.alarm ? 'st-alarm' : t.warn ? 'st-warn' : 'st-ok']" :data-test="'ceo-en-' + t.key">
      <span class="en-title">{{ t.title }}</span>
      <span class="en-value">
        <CeoIcon v-if="t.alarm" :size="18" name="warn" />
        <span class="en-num">{{ t.value }}</span><span class="en-unit">{{ t.unit }}</span>
      </span>
      <span class="en-note">{{ failed ? "تعداد خوانده نشد؛ برای دیدن فهرست بزنید." : t.note }}</span>
    </router-link>
  </section>
</template>

<style scoped>
.ceo-en-tile { margin-top: 12px; display: grid; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); gap: 12px; }
.en-cell { display: flex; flex-direction: column; gap: 4px; padding: 14px 20px; background: var(--ceo-card); border: 1px solid var(--ceo-line); border-radius: 16px; box-shadow: var(--ceo-shadow); color: var(--ceo-ink); text-decoration: none; }
.en-cell:hover { background: var(--ceo-card-hover); }
.en-cell:focus-visible { outline: 2px solid var(--ceo-focus); outline-offset: 2px; }
.en-title { font-size: 13px; font-weight: 500; color: var(--ceo-muted); }
.en-value { display: inline-flex; align-items: baseline; gap: 6px; }
.en-num { font-size: 28px; font-weight: 300; }
.en-unit { font-size: 13px; color: var(--ceo-muted); }
.en-note { font-size: 12px; color: var(--ceo-muted); }
.st-warn .en-num { color: var(--ceo-warn, #b26a00); }
.st-alarm { border-color: var(--ceo-bad, #b3261e); }
.st-alarm .en-num { color: var(--ceo-bad, #b3261e); }
</style>
