<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRoute } from "vue-router";
import GuardMachines from "@/components/production/guard/GuardMachines.vue";
import GuardTimeline from "@/components/production/guard/GuardTimeline.vue";
import GuardOverrides from "@/components/production/guard/GuardOverrides.vue";
import GuardStations from "@/components/production/guard/GuardStations.vue";

// Production release P2 part E: the guarded machining process. What every operator does on a machine is read from the control itself (mode, program, overrides, tool), the start button
// works only for a guarded booking, the production manager opens a gate only once with a written reason, and gauges report their values directly. /production/guard?machineId= opens the
// time line of one machine (the inbox link of an unapproved program, a missing prove-out or an override).
const route = useRoute();
const queryMachine = computed(() => (route.query.machineId ? Number(route.query.machineId) : null));
const tab = ref<"machines" | "timeline" | "overrides" | "stations">(queryMachine.value ? "timeline" : "machines");
const timelineMachine = ref<number | null>(queryMachine.value);
watch(queryMachine, (id) => {
  if (id) {
    timelineMachine.value = id;
    tab.value = "timeline";
  }
});
function openTimeline(machineId: number) {
  timelineMachine.value = machineId;
  tab.value = "timeline";
}
const tabs: [string, string][] = [["machines", "ماشین‌ها"], ["timeline", "خط زمانی ماشین"], ["overrides", "مجوزهای یک‌باره"], ["stations", "ایستگاه‌های اندازه‌گیری"]];
</script>

<template>
  <div class="gd" dir="rtl">
    <header class="gd-head">
      <div>
        <h1 class="gd-title">پایش اپراتور و قفل استارت</h1>
        <p class="gd-sub">هر کاری که روی ماشین انجام می‌شود از خود کنترلر خوانده می‌شود، نه از گفته اپراتور. استارت ماشین فقط برای کار ثبت‌شده و بررسی‌شده آزاد است.</p>
      </div>
    </header>
    <nav class="gd-tabs" role="tablist">
      <button v-for="[k, t] in tabs" :key="k" :aria-selected="tab === k" :class="{ on: tab === k }" class="gd-tab" role="tab" @click="tab = k as any">{{ t }}</button>
    </nav>
    <GuardMachines v-if="tab === 'machines'" @timeline="openTimeline" />
    <GuardTimeline v-else-if="tab === 'timeline'" v-model:machine-id="timelineMachine" />
    <GuardOverrides v-else-if="tab === 'overrides'" />
    <GuardStations v-else />
  </div>
</template>

<style>
/* shared by the guard page and its parts (tokens of the shop-floor overview) */
.gd {
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
[data-theme="darkMode"] .gd {
  --ink: #e4e8e5;
  --muted: #9aa6a0;
  --page: #1b2227;
  --panel: #232c32;
  --rule: #34404a;
  --navy: #7fb2e0;
  --dark-lamp: #3a454d;
}
.gd-head { margin-bottom: 14px; }
.gd-title { font-size: 1.5rem; font-weight: 800; margin: 0; }
.gd-sub { margin: 4px 0 0; color: var(--muted); font-size: 0.86rem; max-width: 70ch; }
.gd-tabs { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 16px; }
.gd-tab { border: 1px solid var(--rule); background: var(--panel); color: var(--ink); border-radius: 999px; padding: 6px 16px; font-size: 0.88rem; cursor: pointer; }
.gd-tab.on { background: var(--navy); border-color: var(--navy); color: #fff; }
.gd-tab:focus-visible, .gd-btn:focus-visible { outline: 3px solid var(--navy); outline-offset: 2px; }
.gd-panel { background: var(--panel); border: 1px solid var(--rule); border-radius: 12px; padding: 16px 18px; }
.gd-muted { color: var(--muted); font-size: 0.85rem; }
.gd-btn { border: 1px solid var(--navy); color: var(--navy); background: transparent; padding: 5px 13px; border-radius: 8px; font-size: 0.84rem; cursor: pointer; }
.gd-btn:hover:not(:disabled) { background: var(--navy); color: #fff; }
.gd-btn.solid { background: var(--navy); color: #fff; }
.gd-btn.danger { border-color: var(--red); color: var(--red); }
.gd-btn.danger:hover:not(:disabled) { background: var(--red); color: #fff; }
.gd-btn:disabled { opacity: 0.5; cursor: default; }
.gd-ltr { direction: ltr; unicode-bidi: embed; font-family: Consolas, "Courier New", monospace; }
.gd-dot { display: inline-block; width: 9px; height: 9px; border-radius: 50%; background: var(--dark-lamp); vertical-align: middle; }
.gd-dot.ok { background: var(--green); }
.gd-dot.warn { background: var(--amber); }
.gd-dot.bad { background: var(--red); }
.gd-dot.info { background: var(--navy); }
.gd-chip { display: inline-block; padding: 1px 9px; border-radius: 999px; font-size: 0.78rem; background: var(--page); border: 1px solid var(--rule); }
.gd-chip.ok { color: var(--green); border-color: var(--green); }
.gd-chip.warn { color: var(--amber); border-color: var(--amber); }
.gd-chip.bad { color: var(--red); border-color: var(--red); }
.gd-empty { text-align: center; padding: 28px 10px; color: var(--muted); }
.gd table { width: 100%; border-collapse: collapse; font-size: 0.88rem; }
.gd th { text-align: start; color: var(--muted); font-weight: 600; padding: 6px 8px; border-bottom: 1px solid var(--rule); }
.gd td { padding: 7px 8px; border-bottom: 1px solid var(--rule); vertical-align: top; }
</style>
