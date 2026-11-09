<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, type StyleValue } from "vue";
import { getFloor } from "@/api/DispatchAPIs";

// Production release P2 part D: the shop-floor overview. One look answers "which machines run, what is wrong, what should happen next". Every machine is drawn as its own stack light
// (green running, amber idle, red alarm), the vernacular of the shop. Read-only; refreshes every 30 seconds while open.
const d = ref<any>(null);
const error = ref<string | null>(null);
const loading = ref(true);
let timer: any = null;
let busy = false;

async function load() {
  // one request at a time, and none while the tab is hidden (a wall screen must not pile requests up)
  if (busy || document.hidden) return;
  busy = true;
  const [data, err] = await getFloor().finally(() => (busy = false));
  loading.value = false;
  if (err) {
    error.value = typeof err === "string" ? err : err?.message || "اطلاعات کارگاه دریافت نشد.";
    return;
  }
  if (data && data.success === false) {
    error.value = data.message;
    return;
  }
  error.value = null;
  d.value = data?.data ?? data;
}
onMounted(() => {
  load();
  timer = setInterval(load, 30000);
  document.addEventListener("visibilitychange", onVisible);
});
function onVisible() {
  if (!document.hidden) load();
}
onBeforeUnmount(() => {
  clearInterval(timer);
  document.removeEventListener("visibilitychange", onVisible);
});

// machine state: 0 off, 1 idle, 2 running, 3 alarm, 4 offline, null never seen
const stateText: Record<string, string> = { "0": "خاموش", "1": "آماده، بیکار", "2": "در حال کار", "3": "آلارم", "4": "بدون ارتباط", null: "هنوز دیده نشده" };
const lamp = (s: number | null) => (s === 2 ? "green" : s === 1 ? "amber" : s === 3 ? "red" : "dark");
const machines = computed(() => (d.value?.live?.machines || []).filter((m: any) => m.source !== 0));
const fa = (n: number | null | undefined) => (n === null || n === undefined ? "—" : Number(n).toLocaleString("fa-IR", { maximumFractionDigits: 1 }));
// a CSS custom property is not in the StyleValue type; the ring percentage goes through this helper
const ring = (p: number | null | undefined): StyleValue => ({ "--p": (p || 0) + "%" } as unknown as StyleValue);
const since = (min: number) => (min < 60 ? fa(min) + " دقیقه" : fa(Math.floor(min / 60)) + " ساعت" + (min % 60 ? " و " + fa(min % 60) + " دقیقه" : ""));
const levelText: Record<string, string> = { bottleneck: "گلوگاه", busy: "پربار", normal: "عادی", idle: "بی‌کار" };
const kindText: Record<string, string> = { bottleneck: "گلوگاه", "late-risk": "خطر تأخیر", "machine-down": "ماشین متوقف", "machine-idle": "ماشین بیکار", program: "برنامه CNC", kit: "مواد" };
// the bar holds 150 %; the tick marks 100 %. A centre with work and no capacity at all is drawn full.
const loadWidth = (c: any) => (c.loadPercent === null && c.demandHours > 0 ? 100 : Math.min(100, Math.max(2, Number(c.loadPercent || 0) / 1.5))) + "%";
</script>

<template>
  <div class="fl" dir="rtl">
    <header class="fl-head">
      <div>
        <h1 class="fl-title">{{ d?.greetingText || "کارگاه" }}؛ وضعیت کارگاه</h1>
        <p class="fl-sub">به‌روزرسانی خودکار هر ۳۰ ثانیه<span v-if="d?.asOfText">، آخرین بار {{ d.asOfText }}</span></p>
      </div>
      <div class="fl-head-side">
        <button class="fl-btn" type="button" @click="load">تازه‌سازی</button>
      </div>
    </header>

    <p v-if="error" class="fl-error" role="alert">{{ error }}</p>
    <div v-else-if="loading" class="fl-loading">در حال خواندن وضعیت ماشین‌ها…</div>

    <template v-if="d">
      <section aria-label="خلاصه" class="fl-strip">
        <router-link v-for="t in d.tiles" :key="t.key" :class="'tone-' + t.tone" :to="t.link" class="fl-stat">
          <span class="fl-stat-bar" />
          <span class="fl-stat-title">{{ t.title }}</span>
          <span class="fl-stat-value">{{ t.valueText }}</span>
          <span class="fl-stat-hint">{{ t.hint }}</span>
        </router-link>
      </section>

      <div class="fl-grid">
        <section aria-labelledby="fl-m" class="fl-panel fl-machines">
          <div class="fl-panel-head">
            <h2 id="fl-m">ماشین‌ها</h2>
            <router-link class="fl-link" to="/production/monitor">پایش و دوربین‌ها</router-link>
          </div>
          <div v-if="!machines.length" class="fl-empty">
            <p>پایش زنده ماشین‌ها هنوز راه‌اندازی نشده است.</p>
            <p class="fl-muted">با نصب ماژول ورودی دیجیتال روی چراغ برج هر ماشین، وضعیت آن این‌جا دیده می‌شود.</p>
            <router-link class="fl-btn" to="/production/monitor">تنظیم پایش ماشین‌ها</router-link>
          </div>
          <ul v-else class="fl-mlist">
            <li v-for="m in machines" :key="m.machineId" :class="'st-' + lamp(m.state)" class="fl-machine">
              <div :aria-label="stateText[String(m.state)]" class="tower" role="img">
                <span :class="{ on: m.state === 3 }" class="lamp red" />
                <span :class="{ on: m.state === 1 }" class="lamp amber" />
                <span :class="{ on: m.state === 2 }" class="lamp green" />
                <span class="pole" />
              </div>
              <div class="fl-mbody">
                <div class="fl-mname">{{ m.machineTitle }}</div>
                <div class="fl-mstate">{{ stateText[String(m.state)] }}<span v-if="m.minutesInState">، {{ since(m.minutesInState) }}</span></div>
                <div v-if="m.state === 3 && (m.alarmText || m.alarmCode)" class="fl-malarm">{{ m.alarmCode }} {{ m.alarmText }}</div>
                <div v-if="m.operationName" class="fl-mjob">{{ m.operationName }}<span v-if="m.operatorName">، {{ m.operatorName }}</span></div>
                <div v-if="m.lastRunProgram" class="fl-mprog">برنامه {{ m.lastRunProgram }}<span v-if="m.lastRunRevision"> نسخه {{ fa(m.lastRunRevision) }}</span></div>
                <div v-if="m.mismatchText" class="fl-mwarn">{{ m.mismatchText }}</div>
              </div>
              <div :style="ring(m.todayRunPercent)" class="fl-ring" title="سهم کار واقعی از امروز">
                <span>{{ m.todayRunPercent === null || m.todayRunPercent === undefined ? "—" : fa(m.todayRunPercent) + "٪" }}</span>
              </div>
            </li>
          </ul>
        </section>

        <section aria-labelledby="fl-s" class="fl-panel fl-advice">
          <div class="fl-panel-head">
            <h2 id="fl-s">پیشنهاد برنامه‌ریز هوشمند</h2>
            <router-link class="fl-link" to="/production/dispatch">برنامه کامل</router-link>
          </div>
          <p v-if="d.bottleneckText" class="fl-bottleneck">{{ d.bottleneckText }}</p>
          <p v-if="!d.suggestions.length" class="fl-muted">پیشنهادی نیست؛ صف‌ها با برنامه هم‌خوان است.</p>
          <ol class="fl-sugg">
            <li v-for="(s, i) in d.suggestions" :key="i" :class="'sev-' + s.severity">
              <span class="fl-kind">{{ kindText[s.kind] || "نکته" }}</span>
              <p>{{ s.text }}</p>
              <router-link :to="s.link" class="fl-link">بررسی</router-link>
            </li>
          </ol>
          <p class="fl-fine">این‌ها فقط پیشنهاد است؛ تصمیم با مدیر تولید و ناظر کارگاه است.</p>
        </section>

        <section aria-labelledby="fl-c" class="fl-panel fl-centers">
          <div class="fl-panel-head">
            <h2 id="fl-c">بار مراکز کار در ۱۰ روز آینده</h2>
            <router-link class="fl-link" to="/production/load">گزارش بار</router-link>
          </div>
          <p v-if="!d.centers.length" class="fl-muted">سفارش باز تولیدی روی مراکز کار نیست.</p>
          <div v-for="c in d.centers" :key="c.workCenterId" :class="'lv-' + c.level" class="fl-center">
            <div class="fl-cline">
              <router-link :to="'/production/dispatch?wc=' + c.workCenterId" class="fl-cname">{{ c.title }}</router-link>
              <span class="fl-clevel">{{ levelText[c.level] }}</span>
              <span class="fl-cpct">{{ c.loadPercent === null ? "بدون ظرفیت" : fa(c.loadPercent) + "٪" }}</span>
            </div>
            <div class="fl-track"><span :style="{ width: loadWidth(c) }" class="fl-fill" /><span class="fl-100" title="۱۰۰٪ ظرفیت" /></div>
            <div v-if="c.queue.length" class="fl-next">نوبت: {{ c.queue.map((q: any) => q.name).join("، ") }}</div>
          </div>
        </section>

        <section aria-labelledby="fl-w" class="fl-panel fl-waiting">
          <div class="fl-panel-head"><h2 id="fl-w">منتظر شما</h2></div>
          <p v-if="!d.waiting.length" class="fl-muted">کاری منتظر تأیید یا بازبینی نیست.</p>
          <router-link v-for="w in d.waiting" :key="w.key" :class="'tone-' + w.tone" :to="w.link" class="fl-wait">
            <span class="fl-wcount">{{ fa(w.count) }}</span>
            <span>{{ w.title }}</span>
          </router-link>
        </section>
      </div>
    </template>
  </div>
</template>

<style scoped>
.fl {
  --ink: #1f2a33;
  --muted: #5d6a74;
  --page: #e9ece8;
  --panel: #f8f9f7;
  --rule: #d5dad4;
  --navy: #235f94;
  --green: #2e9e57;
  --amber: #d99a1e;
  --red: #d24035;
  --dark-lamp: #c3c9c4;
  font-family: IRANYekan, Tahoma, sans-serif;
  color: var(--ink);
  background: var(--page);
  border-radius: 14px;
  padding: 20px 22px 26px;
  font-variant-numeric: tabular-nums;
}
:global([data-theme="darkMode"]) .fl {
  --ink: #e4e8e5;
  --muted: #9aa6a0;
  --page: #1b2227;
  --panel: #232c32;
  --rule: #34404a;
  --navy: #7fb2e0;
  --dark-lamp: #3a454d;
}
.fl-head { display: flex; justify-content: space-between; align-items: flex-end; gap: 16px; flex-wrap: wrap; margin-bottom: 18px; }
.fl-title { font-size: 1.55rem; font-weight: 800; margin: 0; letter-spacing: -0.01em; }
.fl-sub { margin: 4px 0 0; color: var(--muted); font-size: 0.85rem; }
.fl-head-side { display: flex; gap: 10px; align-items: center; }
.fl-btn { display: inline-block; border: 1px solid var(--navy); color: var(--navy); background: transparent; padding: 6px 14px; border-radius: 8px; font-size: 0.85rem; text-decoration: none; cursor: pointer; }
.fl-btn:hover { background: var(--navy); color: #fff; }
.fl-btn:focus-visible, .fl-link:focus-visible, .fl-stat:focus-visible, .fl-wait:focus-visible, .fl-cname:focus-visible { outline: 3px solid var(--navy); outline-offset: 2px; }
.fl-error { background: #fde8e6; color: #8c1d14; padding: 12px 14px; border-radius: 10px; }
.fl-loading { color: var(--muted); padding: 30px 0; }

/* the status strip: one row of four readings divided by rules, the colour of the reading is a short bar like a lamp */
.fl-strip { display: grid; grid-template-columns: repeat(4, 1fr); background: var(--panel); border: 1px solid var(--rule); border-radius: 12px; margin-bottom: 18px; overflow: hidden; }
.fl-stat { position: relative; display: flex; flex-direction: column; gap: 2px; padding: 14px 18px 14px 14px; color: inherit; text-decoration: none; border-inline-start: 1px solid var(--rule); }
.fl-stat:first-child { border-inline-start: 0; }
.fl-stat:hover { background: rgba(35, 95, 148, 0.05); }
.fl-stat-bar { position: absolute; inset-inline-start: 0; top: 16px; width: 4px; height: 26px; border-radius: 0 3px 3px 0; background: var(--dark-lamp); }
.tone-good .fl-stat-bar { background: var(--green); }
.tone-warn .fl-stat-bar { background: var(--amber); }
.tone-bad .fl-stat-bar { background: var(--red); }
.fl-stat-title { font-size: 0.85rem; color: var(--muted); }
.fl-stat-value { font-size: 1.9rem; font-weight: 800; line-height: 1.15; }
.tone-bad .fl-stat-value { color: var(--red); }
.fl-stat-hint { font-size: 0.78rem; color: var(--muted); }

.fl-grid { display: grid; grid-template-columns: minmax(0, 1.75fr) minmax(0, 1fr); gap: 18px; }
.fl-panel { background: var(--panel); border: 1px solid var(--rule); border-radius: 12px; padding: 16px 18px; min-width: 0; }
.fl-panel-head { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 12px; }
.fl-panel h2 { font-size: 1.05rem; font-weight: 700; margin: 0; }
.fl-link { color: var(--navy); font-size: 0.83rem; text-decoration: none; }
.fl-link:hover { text-decoration: underline; }
.fl-muted { color: var(--muted); font-size: 0.85rem; }
.fl-note { margin: 10px 0 0; }
.fl-empty { text-align: center; padding: 26px 10px; }
.fl-empty p { margin: 0 0 6px; }
.fl-empty .fl-btn { margin-top: 10px; }

/* machines: each one a stack light */
.fl-mlist { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 12px; }
.fl-machine { display: grid; grid-template-columns: 26px 1fr auto; gap: 12px; align-items: start; background: var(--page); border-radius: 10px; padding: 12px; border: 1px solid transparent; }
.fl-machine.st-red { border-color: var(--red); }
.tower { display: flex; flex-direction: column; align-items: center; gap: 3px; padding-top: 2px; }
.lamp { width: 18px; height: 16px; border-radius: 4px 4px 3px 3px; background: var(--dark-lamp); opacity: 0.55; }
.lamp.on { opacity: 1; }
.lamp.red.on { background: var(--red); box-shadow: 0 0 10px 1px rgba(210, 64, 53, 0.55); }
.lamp.amber.on { background: var(--amber); box-shadow: 0 0 10px 1px rgba(217, 154, 30, 0.5); }
.lamp.green.on { background: var(--green); box-shadow: 0 0 10px 1px rgba(46, 158, 87, 0.5); }
.pole { width: 4px; height: 12px; background: var(--dark-lamp); border-radius: 2px; }
@media (prefers-reduced-motion: no-preference) {
  .lamp.red.on { animation: blink 1.2s steps(2, jump-none) infinite; }
}
@keyframes blink { 50% { opacity: 0.45; box-shadow: none; } }
.fl-mname { font-weight: 700; }
.fl-mstate { font-size: 0.85rem; color: var(--muted); }
.st-green .fl-mstate { color: var(--green); }
.st-red .fl-mstate, .fl-malarm { color: var(--red); }
.fl-malarm, .fl-mjob, .fl-mprog, .fl-mwarn { font-size: 0.8rem; margin-top: 3px; }
.fl-mprog { color: var(--muted); }
.fl-mwarn { color: #8a6100; }
.fl-ring { --p: 0%; width: 50px; height: 50px; border-radius: 50%; display: grid; place-items: center; background: conic-gradient(var(--green) var(--p), var(--rule) 0); }
.fl-ring span { width: 38px; height: 38px; border-radius: 50%; background: var(--page); display: grid; place-items: center; font-size: 0.72rem; font-weight: 700; }

/* the planner's proposals */
.fl-bottleneck { background: rgba(210, 64, 53, 0.08); border-inline-start: 3px solid var(--red); padding: 8px 10px; border-radius: 6px; font-size: 0.88rem; margin: 0 0 10px; }
.fl-sugg { list-style: none; padding: 0; margin: 0; }
.fl-sugg li { padding: 10px 0; border-top: 1px solid var(--rule); }
.fl-sugg li:first-child { border-top: 0; padding-top: 2px; }
.fl-sugg p { margin: 4px 0; font-size: 0.86rem; line-height: 1.75; }
.fl-kind { font-size: 0.75rem; font-weight: 700; padding: 1px 8px; border-radius: 999px; background: var(--rule); }
.sev-1 .fl-kind { background: var(--red); color: #fff; }
.sev-2 .fl-kind { background: var(--amber); color: #2b1f00; }
.fl-fine { font-size: 0.75rem; color: var(--muted); margin: 10px 0 0; }

/* work-centre load: one bar per centre; the tick marks 100 % */
.fl-center { padding: 8px 0; }
.fl-cline { display: flex; gap: 10px; align-items: baseline; }
.fl-cname { font-weight: 700; color: inherit; text-decoration: none; flex: 1; }
.fl-cname:hover { color: var(--navy); }
.fl-clevel { font-size: 0.75rem; color: var(--muted); }
.lv-bottleneck .fl-clevel { color: var(--red); font-weight: 700; }
.fl-cpct { font-weight: 700; min-width: 56px; text-align: left; }
.fl-track { position: relative; height: 10px; background: var(--page); border-radius: 5px; margin-top: 6px; overflow: hidden; }
.fl-fill { position: absolute; inset-inline-start: 0; top: 0; bottom: 0; background: var(--green); border-radius: 5px; }
.lv-busy .fl-fill { background: var(--amber); }
.lv-bottleneck .fl-fill { background: var(--red); }
.fl-100 { position: absolute; top: -2px; bottom: -2px; inset-inline-start: 66.6%; width: 2px; background: var(--ink); opacity: 0.45; }
.fl-next { font-size: 0.78rem; color: var(--muted); margin-top: 4px; }

/* waiting for a person */
.fl-wait { display: flex; gap: 12px; align-items: center; padding: 9px 4px; color: inherit; text-decoration: none; border-top: 1px solid var(--rule); font-size: 0.9rem; }
.fl-wait:first-of-type { border-top: 0; }
.fl-wait:hover { color: var(--navy); }
.fl-wcount { min-width: 34px; height: 34px; border-radius: 8px; display: grid; place-items: center; font-weight: 800; background: var(--page); }
.fl-wait.tone-bad .fl-wcount { background: var(--red); color: #fff; }
.fl-wait.tone-warn .fl-wcount { background: var(--amber); color: #2b1f00; }

@media (max-width: 991px) {
  .fl { padding: 14px 12px 20px; border-radius: 0; }
  .fl-grid { grid-template-columns: 1fr; }
  .fl-strip { grid-template-columns: repeat(2, 1fr); }
  .fl-stat:nth-child(3) { border-inline-start: 0; }
  .fl-stat:nth-child(n + 3) { border-top: 1px solid var(--rule); }
  .fl-title { font-size: 1.25rem; }
}
</style>
