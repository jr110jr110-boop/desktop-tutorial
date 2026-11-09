<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { getDispatchPlan } from "@/api/DispatchAPIs";

// Production release P2 part D: the plan of the planning seat MF-DISPATCH. Every work-centre queue in the order the seat proposes, with the reason of every place, the load of the
// centre and the concrete proposals. It changes nothing; the people on the shop floor decide.
const route = useRoute();
const router = useRouter();
const plan = ref<any>(null);
const error = ref<string | null>(null);
const loading = ref(false);
const horizon = ref(10);
const wc = ref<number | null>(route.query.wc ? Number(route.query.wc) : null);
const showMethod = ref(false);

async function load() {
  loading.value = true;
  const [data, err] = await getDispatchPlan({ WorkCenterId: null, HorizonDays: horizon.value });
  loading.value = false;
  const body = data?.data ?? data;
  if (err || data?.success === false) {
    error.value = data?.message || err?.message || "برنامه دریافت نشد.";
    return;
  }
  error.value = null;
  plan.value = body;
}
onMounted(load);
watch(horizon, load);
watch(wc, (v) => {
  if (String(v ?? "") !== String(route.query.wc ?? "")) router.replace({ query: v ? { wc: String(v) } : {} });
});
// a link to another centre from this page (a proposal) changes the filter too
watch(() => route.query.wc, (q) => (wc.value = q ? Number(q) : null));

const centers = computed(() => (plan.value?.centers || []).filter((c: any) => (wc.value ? c.workCenterId === wc.value : c.queue.length > 0 || c.demandHours > 0)));
const suggestions = computed(() => (plan.value?.suggestions || []).filter((s: any) => !wc.value || s.workCenterId === wc.value));
const fa = (n: number | null | undefined, digits = 1) => (n === null || n === undefined ? "—" : Number(n).toLocaleString("fa-IR", { maximumFractionDigits: digits }));
const stageText: Record<string, string> = { running: "روی ماشین", ready: "آماده", upcoming: "منتظر مرحله قبل" };
const riskText: Record<string, string> = { "late-risk": "دیر می‌شود", "at-risk": "کم‌فرصت", "on-time": "به‌موقع", "no-date": "بی‌تاریخ" };
const levelText: Record<string, string> = { bottleneck: "گلوگاه", busy: "پربار", normal: "عادی", idle: "بی‌کار" };
const kindText: Record<string, string> = { bottleneck: "گلوگاه", "late-risk": "خطر تأخیر", "machine-down": "ماشین متوقف", "machine-idle": "ماشین بیکار", program: "برنامه CNC", kit: "مواد" };
</script>

<template>
  <div class="dp" dir="rtl">
    <header class="dp-head">
      <div>
        <h1>برنامه پیشنهادی تولید</h1>
        <p class="dp-sub">برنامه‌ریز هوشمند تولید نوبت کارهای هر مرکز را پیشنهاد می‌کند؛ تصمیم با مدیر تولید و ناظر کارگاه است.<span v-if="plan"> ساخته‌شده در {{ plan.generatedAtText }}.</span></p>
      </div>
      <div class="dp-controls">
        <label>افق بار
          <select v-model.number="horizon">
            <option :value="5">۵ روز</option>
            <option :value="10">۱۰ روز</option>
            <option :value="20">۲۰ روز</option>
            <option :value="30">۳۰ روز</option>
          </select>
        </label>
        <label v-if="plan">مرکز کار
          <select v-model="wc">
            <option :value="null">همه</option>
            <option v-for="c in plan.centers" :key="c.workCenterId" :value="c.workCenterId">{{ c.title }}</option>
          </select>
        </label>
        <button :disabled="loading" type="button" @click="load">{{ loading ? "در حال ساخت…" : "ساخت دوباره" }}</button>
      </div>
    </header>

    <p v-if="error" class="dp-error" role="alert">{{ error }}</p>

    <template v-if="plan">
      <section class="dp-summary" aria-label="خلاصه">
        <div><span>سفارش باز</span><b>{{ fa(plan.openOrders) }}</b></div>
        <div :class="{ bad: plan.lateOrders > 0 }"><span>سفارش دیرکرد</span><b>{{ fa(plan.lateOrders) }}</b></div>
        <div :class="{ bad: plan.lateRiskOperations > 0 }"><span>کار آماده در خطر تأخیر</span><b>{{ fa(plan.lateRiskOperations) }}</b></div>
        <p v-if="plan.bottleneckText" class="dp-bn">{{ plan.bottleneckText }}</p>
      </section>

      <section v-if="suggestions.length" class="dp-panel" aria-labelledby="dp-s">
        <h2 id="dp-s">پیشنهادها</h2>
        <ul class="dp-sugg">
          <li v-for="(s, i) in suggestions" :key="i" :class="'sev-' + s.severity">
            <span class="dp-kind">{{ kindText[s.kind] || "نکته" }}</span>
            <span class="dp-stext">{{ s.text }}</span>
            <router-link :to="s.link">بررسی</router-link>
          </li>
        </ul>
      </section>

      <section v-for="c in centers" :key="c.workCenterId" :aria-label="c.title" :class="'lv-' + c.level" class="dp-panel dp-center">
        <div class="dp-chead">
          <h2>{{ c.title }} <small>{{ c.code }}</small></h2>
          <span class="dp-level">{{ levelText[c.level] }}</span>
        </div>
        <div class="dp-cfacts">
          <span>بار {{ c.loadPercent === null ? "بدون ظرفیت" : fa(c.loadPercent) + "٪" }}</span>
          <span>{{ fa(c.demandHours) }} ساعت کار در برابر {{ fa(c.capacityHours) }} ساعت ظرفیت</span>
          <span>{{ fa(c.readyCount) }} کار آماده ({{ fa(c.readyHours) }} ساعت)</span>
          <span v-if="c.measuredRunPercent !== null">کارکرد واقعی ۷ روز: {{ fa(c.measuredRunPercent) }}٪</span>
          <span v-if="c.machines.length">ماشین‌ها: {{ c.machines.join("، ") }}</span>
        </div>
        <div class="dp-track" aria-hidden="true"><span :style="{ width: (c.loadPercent === null && c.demandHours > 0 ? 100 : Math.min(100, (c.loadPercent || 0) / 1.5)) + '%' }" /><i /></div>

        <p v-if="!c.queue.length" class="dp-muted">کاری در صف این مرکز نیست.</p>
        <div v-else class="dp-tablewrap">
          <table class="dp-table">
            <thead>
              <tr><th scope="col">نوبت</th><th scope="col">کار</th><th scope="col">وضع</th><th scope="col">ساعت مانده</th><th scope="col">تاریخ نیاز</th><th scope="col">نسبت بحرانی</th><th scope="col">برنامه CNC</th><th scope="col">چرا این نوبت</th></tr>
            </thead>
            <tbody>
              <tr v-for="q in c.queue" :key="q.operationId" :class="['st-' + q.stage, 'rk-' + q.risk]">
                <td class="dp-rank">{{ fa(q.rank, 0) }}</td>
                <td><router-link :to="'/production/orders/' + q.orderId">{{ q.name }}</router-link><div class="dp-muted">{{ q.title }}<span v-if="q.itemTitle">، {{ q.itemTitle }}</span></div></td>
                <td><span class="dp-stage">{{ stageText[q.stage] }}</span><div v-if="q.kitShortage" class="dp-kit">مواد ناقص</div></td>
                <td>{{ fa(q.hoursLeft) }}</td>
                <td>{{ q.needDateText || "—" }}</td>
                <td><span class="dp-cr">{{ q.criticalRatio === null ? "—" : fa(q.criticalRatio, 2) }}</span><div class="dp-muted">{{ riskText[q.risk] }}</div></td>
                <td><span v-if="q.programReady === true" class="ok" title="روی ماشین تأیید شده">آماده</span><span v-else-if="q.programReady === false" class="no" :title="q.programNote">تأیید نشده</span><span v-else class="dp-muted">—</span></td>
                <td class="dp-reason">{{ q.reason }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section class="dp-panel dp-method">
        <button :aria-expanded="showMethod" type="button" @click="showMethod = !showMethod">روش برنامه‌ریز {{ showMethod ? "(بستن)" : "(نمایش)" }}</button>
        <p v-if="showMethod">{{ plan.method }}</p>
      </section>
    </template>
  </div>
</template>

<style scoped>
.dp { --ink: #1f2a33; --muted: #5d6a74; --page: #e9ece8; --panel: #f8f9f7; --rule: #d5dad4; --navy: #235f94; --green: #2e9e57; --amber: #d99a1e; --red: #d24035;
  font-family: IRANYekan, Tahoma, sans-serif; color: var(--ink); background: var(--page); border-radius: 14px; padding: 20px 22px 26px; font-variant-numeric: tabular-nums; }
:global([data-theme="darkMode"]) .dp { --ink: #e4e8e5; --muted: #9aa6a0; --page: #1b2227; --panel: #232c32; --rule: #34404a; --navy: #7fb2e0; }
.dp-head { display: flex; justify-content: space-between; gap: 16px; flex-wrap: wrap; align-items: flex-end; margin-bottom: 16px; }
.dp-head h1 { font-size: 1.5rem; font-weight: 800; margin: 0; }
.dp-sub { color: var(--muted); font-size: 0.86rem; margin: 4px 0 0; max-width: 70ch; }
.dp-controls { display: flex; gap: 10px; align-items: flex-end; flex-wrap: wrap; }
.dp-controls label { display: flex; flex-direction: column; font-size: 0.78rem; color: var(--muted); gap: 2px; }
.dp-controls select, .dp-controls button { font: inherit; font-size: 0.86rem; padding: 5px 10px; border-radius: 8px; border: 1px solid var(--rule); background: var(--panel); color: var(--ink); }
.dp-controls button { border-color: var(--navy); color: var(--navy); cursor: pointer; }
.dp-controls button:hover:not(:disabled) { background: var(--navy); color: #fff; }
:is(.dp-controls select, .dp-controls button, .dp a, .dp-method button):focus-visible { outline: 3px solid var(--navy); outline-offset: 2px; }
.dp-error { background: #fde8e6; color: #8c1d14; padding: 12px 14px; border-radius: 10px; }
.dp a { color: var(--navy); text-decoration: none; }
.dp a:hover { text-decoration: underline; }
.dp-muted { color: var(--muted); font-size: 0.8rem; }

.dp-summary { display: flex; flex-wrap: wrap; gap: 0; background: var(--panel); border: 1px solid var(--rule); border-radius: 12px; margin-bottom: 16px; overflow: hidden; }
.dp-summary > div { padding: 12px 18px; border-inline-start: 1px solid var(--rule); display: flex; flex-direction: column; }
.dp-summary > div:first-child { border-inline-start: 0; }
.dp-summary span { font-size: 0.8rem; color: var(--muted); }
.dp-summary b { font-size: 1.6rem; font-weight: 800; }
.dp-summary .bad b { color: var(--red); }
.dp-bn { flex: 1 1 260px; margin: 0; padding: 12px 18px; align-self: center; border-inline-start: 3px solid var(--red); font-size: 0.9rem; }

.dp-panel { background: var(--panel); border: 1px solid var(--rule); border-radius: 12px; padding: 16px 18px; margin-bottom: 16px; }
.dp-panel h2 { font-size: 1.05rem; font-weight: 700; margin: 0 0 10px; }
.dp-panel h2 small { color: var(--muted); font-weight: 400; font-size: 0.8rem; }
.dp-sugg { list-style: none; margin: 0; padding: 0; }
.dp-sugg li { display: flex; gap: 10px; align-items: baseline; padding: 8px 0; border-top: 1px solid var(--rule); font-size: 0.88rem; line-height: 1.75; }
.dp-sugg li:first-child { border-top: 0; }
.dp-stext { flex: 1; }
.dp-kind { font-size: 0.75rem; font-weight: 700; padding: 1px 8px; border-radius: 999px; background: var(--rule); white-space: nowrap; }
.sev-1 .dp-kind { background: var(--red); color: #fff; }
.sev-2 .dp-kind { background: var(--amber); color: #2b1f00; }

.dp-chead { display: flex; justify-content: space-between; align-items: baseline; }
.dp-level { font-size: 0.8rem; font-weight: 700; color: var(--muted); }
.lv-bottleneck .dp-level { color: var(--red); }
.lv-busy .dp-level { color: #8a6100; }
.dp-cfacts { display: flex; flex-wrap: wrap; gap: 6px 18px; font-size: 0.83rem; color: var(--muted); }
.dp-track { position: relative; height: 8px; background: var(--page); border-radius: 4px; margin: 10px 0 12px; overflow: hidden; }
.dp-track span { position: absolute; inset-inline-start: 0; top: 0; bottom: 0; background: var(--green); border-radius: 4px; }
.lv-busy .dp-track span { background: var(--amber); }
.lv-bottleneck .dp-track span { background: var(--red); }
.dp-track i { position: absolute; top: -2px; bottom: -2px; inset-inline-start: 66.6%; width: 2px; background: var(--ink); opacity: 0.45; }

.dp-tablewrap { overflow-x: auto; }
.dp-table { width: 100%; border-collapse: collapse; font-size: 0.85rem; min-width: 820px; }
.dp-table th { text-align: right; font-weight: 600; color: var(--muted); font-size: 0.78rem; padding: 6px 8px; border-bottom: 1px solid var(--rule); }
.dp-table td { padding: 8px; border-bottom: 1px solid var(--rule); vertical-align: top; }
.dp-rank { font-weight: 800; font-size: 1.05rem; width: 44px; }
.st-running .dp-rank { color: var(--green); }
.st-upcoming td { opacity: 0.72; }
.dp-stage { font-size: 0.78rem; padding: 1px 8px; border-radius: 999px; background: var(--page); white-space: nowrap; }
.st-running .dp-stage { background: var(--green); color: #fff; }
.dp-kit { color: #8a6100; font-size: 0.75rem; margin-top: 3px; }
.dp-cr { font-weight: 700; }
.rk-late-risk .dp-cr { color: var(--red); }
.rk-at-risk .dp-cr { color: #8a6100; }
.ok { color: var(--green); font-weight: 600; }
.no { color: var(--red); font-weight: 600; }
.dp-reason { max-width: 340px; line-height: 1.7; }
.dp-method button { font: inherit; background: none; border: 0; color: var(--navy); cursor: pointer; padding: 0; }
.dp-method p { margin: 10px 0 0; line-height: 1.9; max-width: 80ch; font-size: 0.88rem; }

@media (max-width: 991px) {
  .dp { padding: 14px 12px 20px; border-radius: 0; }
  .dp-head h1 { font-size: 1.25rem; }
}
</style>
