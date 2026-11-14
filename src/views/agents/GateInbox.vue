<script setup lang="ts">
// «کارتابل گیت»: what the agents prepared and now waits for the signed-in person (a decision, physical work or information only a person has). The agents do the work; the person only checks,
// edits and approves in the existing page of the document (the link), or records the physical work. «برگشت به ایجنت» sends the task back with a reason (POST agents/inbox/return).
// Every signed-in person; the service shows only the tasks of his roles. Refreshes every 60 s while the browser tab is visible.
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import PromptModal from "@/components/fn/PromptModal.vue";
import { getAgentInbox, getAgentInboxCounts, returnAgentTask } from "@/api/AgentAPIs";
import { rowsOf } from "@/api/ApiClient";
import { agentTitleOf, ageText, badgeOfState, fa, jalali, rolesText, stationTitles, startAutoRefresh, stepKinds, taskLink, tabOfState } from "@/composables/agents";
import { fail, ok } from "@/composables/inventory";

const router = useRouter();
const counts = ref({ humanGate: 0, physical: 0, input: 0, total: 0, overdue: 0 });
const rows = ref<any[]>([]);
const loading = ref(false);
const loaded = ref(false);
/** 0 = all, otherwise the kind of the tab: 2 HumanGate, 3 Physical, 4 Input */
const tab = ref(0);
const station = ref<number | null>(null);
const dialog = ref<any | null>(null);
const busy = ref(false);
/** the server's Persian refusal of a return, shown under the row it belongs to */
const refused = ref<Record<number, string>>({});

const tabs = computed(() => [
  { kind: 0, title: "همه", n: counts.value.total },
  { kind: 2, title: "تأیید و امضا", n: counts.value.humanGate },
  { kind: 3, title: "کار فیزیکی", n: counts.value.physical },
  { kind: 4, title: "ورود اطلاعات", n: counts.value.input },
]);

let seq = 0;
async function load() {
  const mine = ++seq;
  loading.value = true;
  const params: any = { PageNumber: 1, PageSize: 0 };
  if (tab.value) params.Kind = tab.value;
  if (station.value) params.Station = station.value;
  const [[list, listError], [cnt, cntError]] = await Promise.all([getAgentInbox(params), getAgentInboxCounts()]);
  if (mine !== seq) return;
  loading.value = false;
  loaded.value = true;
  if (!cntError && cnt) counts.value = { humanGate: cnt.humanGate || 0, physical: cnt.physical || 0, input: cnt.input || 0, total: cnt.total || 0, overdue: cnt.overdue || 0 };
  if (listError) return fail(listError);
  rows.value = rowsOf(list);
}
function pick(kind: number) {
  tab.value = kind;
  load();
}

function rowClass(r: any) {
  const t = tabOfState(r.state);
  return [t === 2 ? "gate" : t === 3 ? "physical" : t === 4 ? "input" : "", r.overdue ? "late" : ""];
}
function kindText(r: any) {
  const t = tabOfState(r.state);
  return t ? stepKinds[t] : stepKinds[r.kind] || "";
}
function kindChip(r: any) {
  const t = tabOfState(r.state);
  return t === 2 ? "warn" : t === 3 ? "slate" : t === 4 ? "violet" : "info";
}
function open(r: any) {
  const to = taskLink(router, r.link);
  if (to) router.push(to);
}

async function sendBack(values: { reason?: string }) {
  const r = dialog.value;
  if (!r || busy.value) return;
  const reason = String(values.reason || "").trim();
  if (!reason) return fail("علت برگشت به ایجنت را بنویسید.");
  busy.value = true;
  const [, error] = await returnAgentTask({ id: r.id, reason });
  busy.value = false;
  dialog.value = null;
  if (error) {
    // the server's own Persian message (not your gate, already closed, no agent found, the document refused ...)
    refused.value = { ...refused.value, [r.id]: error.message || "برگشت به ایجنت انجام نشد." };
    return fail(error);
  }
  ok("کار به ایجنت برگشت.");
  const next = { ...refused.value };
  delete next[r.id];
  refused.value = next;
  await load();
}

let stop: (() => void) | null = null;
onMounted(() => {
  load();
  stop = startAutoRefresh(load);
});
onBeforeUnmount(() => stop && stop());
</script>

<template>
  <div class="inv-page ag">
    <div class="ag-card ag-hero">
      <div style="flex: 1 1 360px; min-width: 0">
        <h1>کارتابل گیت</h1>
        <p>ایجنت‌ها کار را انجام می‌دهند؛ شما اینجا فقط بررسی، ویرایش و تأیید می‌کنید یا کار فیزیکی را ثبت می‌کنید.</p>
      </div>
      <div class="ag-tiles">
        <div class="ag-tile gate"><b>{{ fa(counts.humanGate) }}</b><span>تأیید و امضا</span></div>
        <div class="ag-tile physical"><b>{{ fa(counts.physical) }}</b><span>کار فیزیکی</span></div>
        <div class="ag-tile input"><b>{{ fa(counts.input) }}</b><span>ورود اطلاعات</span></div>
        <div :class="{ bad: counts.overdue > 0 }" class="ag-tile"><b>{{ fa(counts.overdue) }}</b><span>با تأخیر</span></div>
      </div>
    </div>

    <div class="ag-card">
      <div class="inv-toolbar">
        <div class="ag-tabs mb-0" role="tablist">
          <button v-for="t in tabs" :key="t.kind" :class="{ on: tab === t.kind }" class="ag-tab" role="tab" @click="pick(t.kind)">
            {{ t.title }}<em>{{ fa(t.n) }}</em>
          </button>
        </div>
        <div>
          <select v-model="station" aria-label="ایستگاه" class="form-select form-select-sm" @change="load">
            <option :value="null">همه ایستگاه‌ها</option>
            <option v-for="(s, i) in stationTitles" :key="i" :value="i + 1">{{ fa(i + 1) }}. {{ s }}</option>
          </select>
        </div>
      </div>

      <div v-if="loading && !loaded" class="ag-empty">در حال بارگذاری...</div>
      <div v-else-if="!rows.length" class="ag-empty">کاری در انتظار شما نیست. ایجنت‌ها هر گامی را که به تأیید یا اقدام شما نیاز داشته باشد اینجا می‌آورند.</div>

      <div v-for="r in rows" :key="r.id" :class="rowClass(r)" class="ag-row">
        <div class="ag-row-head">
          <span class="ag-chip">{{ r.stationTitle }}</span>
          <span class="ag-row-title">{{ r.title }}</span>
          <span v-if="r.targetNumber" class="inv-ltr small">{{ r.targetNumber }}</span>
          <span :class="kindChip(r)" class="ag-chip">{{ kindText(r) }}</span>
          <span v-if="r.overdue" class="ag-chip bad">با تأخیر</span>
          <span v-else-if="r.dueAt" class="ag-chip">مهلت: {{ jalali(r.dueAt, true) }}</span>
          <span v-if="r.isFallback" class="ag-chip warn">ایجنت خاموش است؛ این گام دستی است</span>
        </div>
        <div class="ag-row-meta">
          <span>ایجنت: {{ agentTitleOf(r.agentCode) }}</span>
          <span>·</span>
          <span :title="jalali(r.createdAt, true)">{{ ageText(r.ageHours) }}</span>
          <template v-if="r.gateRoles"><span>·</span><span>گیت: {{ rolesText(r.gateRoles) }}</span></template>
          <span :class="badgeOfState(r.state)" class="ag-chip">{{ r.state === 4 ? "منتظر تأیید" : r.state === 5 ? "منتظر اقدام فیزیکی" : "منتظر اطلاعات" }}</span>
        </div>
        <div v-if="r.summary || r.outputNumber" class="ag-prepared">
          <b>آماده‌شده توسط ایجنت</b><span v-if="r.outputNumber"> — شماره: <span class="inv-ltr">{{ r.outputNumber }}</span></span>
          <div v-if="r.summary">{{ r.summary }}</div>
        </div>
        <div v-if="r.returnReason" class="ag-note info">علت برگشت قبلی: {{ r.returnReason }}</div>
        <div v-if="r.lastError" class="ag-note bad">آخرین خطای ایجنت: {{ r.lastError }}</div>
        <div v-if="refused[r.id]" class="ag-note warn">{{ refused[r.id] }}</div>
        <div class="ag-actions">
          <button :disabled="!taskLink(router, r.link)" :title="taskLink(router, r.link) ? '' : 'صفحه این سند در دسترس نیست.'" class="btn btn-sm btn-primary" @click="open(r)">باز کردن سند</button>
          <button :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="dialog = r">برگشت به ایجنت</button>
        </div>
      </div>
    </div>

    <PromptModal
      v-if="dialog"
      :fields="[{ key: 'reason', label: 'علت برگشت به ایجنت', type: 'textarea', required: true, maxlength: 1000 }]"
      confirm-text="برگشت به ایجنت"
      hint="ایجنت گام خود را با توجه به این علت دوباره انجام می‌دهد."
      title="برگشت به ایجنت"
      @cancel="dialog = null"
      @confirm="sendBack" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="./agents.scss"></style>
