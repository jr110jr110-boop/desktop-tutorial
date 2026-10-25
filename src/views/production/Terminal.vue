<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { finishOperation, getMyOperations, getProductionMachines, pauseOperation, reportMachineDown, reportQuantity, requestQc, scanOperations, startOperation } from "@/api/ProductionAPIs";
import { camelize, latinDigits, parseAmount } from "@/composables/fn";
import { parseScan } from "@/composables/inventory";
import { badgeOfOperation, bookingKinds, operationStatuses, qtyText } from "@/composables/production";
import { clockText, loadReasons, loadWorkCenters, rowsOf } from "@/components/production/floor/floorCommon";

// Shop floor terminal (tablet): my operations as big cards, scan of a job card / serial label, start, pause, quantity, finish, QC request, machine down.
// The server decides every rule; its Persian message is shown big. Quantities are added to the booking of the operator (each report is an increment).
const ops = ref<any[]>([]);
const loaded = ref(false);
const scanText = ref("");
const scanInput = ref<HTMLInputElement | null>(null);
const scanned = ref<string>("");
const workCenters = ref<any[]>([]);
const wcId = ref<number | null>(null);
const reasons = ref<any[]>([]);
const busy = ref(false);
const banner = ref<null | { kind: "ok" | "bad"; text: string }>(null);
const now = ref(Date.now());

type Dialog = null | { type: "start" | "switch" | "pause" | "qty" | "finish" | "down" | "qc"; op: any | null };
const dialog = ref<Dialog>(null);
const machines = ref<any[]>([]);
const f = ref({ kind: 2, machineId: null as number | null, reasonId: null as number | null, good: "", scrap: "", rework: "", scrapReasonId: null as number | null, note: "", description: "" });

const downReasons = computed(() => reasons.value.filter((r) => r.kind === 2 || r.kind === 3));
const scrapReasons = computed(() => reasons.value.filter((r) => r.kind === 1));
const running = computed(() => ops.value.find((o) => o.myRunning) || null);
const kindsForStart = [1, 2, 3, 4, 5];

let refreshTimer: any = null;
let clockTimer: any = null;

function say(kind: "ok" | "bad", text: string) {
  banner.value = { kind, text };
}
function failText(error: any) {
  say("bad", error?.message || String(error || "خطا"));
}

async function load() {
  const [d, error] = await getMyOperations({ WorkCenterId: wcId.value, PageSize: 100 });
  loaded.value = true;
  if (error) return failText(error);
  scanned.value = "";
  ops.value = rowsOf(camelize(d));
}

async function refreshQuiet() {
  // the 30 s refresh never interrupts a dialog or a call in progress, and keeps the scan result on screen
  if (dialog.value || busy.value || scanned.value) return;
  const [d, error] = await getMyOperations({ WorkCenterId: wcId.value, PageSize: 100 });
  if (!error) ops.value = rowsOf(camelize(d));
}

async function scan() {
  const raw = latinDigits(scanText.value).trim();
  scanText.value = "";
  if (!raw) return;
  // labels written by the warehouse are S/<item>/<serial> or L/<item>/<lot>; the server searches the serial / lot itself
  const parsed = parseScan(raw);
  const code = parsed.kind === "serial" || parsed.kind === "lot" ? (parsed as any).value : raw;
  busy.value = true;
  const [d, error] = await scanOperations({ Code: code, PageSize: 50 });
  busy.value = false;
  scanInput.value?.focus();
  if (error) return failText(error);
  ops.value = rowsOf(camelize(d));
  scanned.value = raw;
  banner.value = null;
}

function clearScan() {
  scanned.value = "";
  load();
}

function canQc(op: any) {
  return op.inspectionRequirement === 2 || op.inspectionRequirement === 3;
}
function started(op: any) {
  return op.status === 2 || op.status === 3;
}
function remaining(op: any) {
  return Math.max(0, (Number(op.expectedQty) || 0) - (Number(op.goodQty) || 0) - (Number(op.scrapQty) || 0));
}
function elapsed(op: any) {
  const since = op.myRunningSince ? new Date(op.myRunningSince) : null;
  return since && !Number.isNaN(since.getTime()) ? clockText((now.value - since.getTime()) / 1000) : "";
}

async function openDialog(type: NonNullable<Dialog>["type"], op: any | null) {
  banner.value = null;
  f.value = { kind: 2, machineId: null, reasonId: null, good: type === "finish" && op ? String(remaining(op)) : "", scrap: "", rework: "", scrapReasonId: null, note: "", description: "" };
  machines.value = [];
  dialog.value = { type, op };
  if (type === "start" || type === "switch" || type === "down") {
    const [d] = await getProductionMachines({ WorkCenterId: op?.workCenterId, Status: type === "down" ? undefined : 1, IsActive: true, PageSize: 200 });
    machines.value = rowsOf(camelize(d)).filter((m: any) => m.isActive !== false);
    if (type !== "down" && machines.value.length === 1) f.value.machineId = machines.value[0].machineId;
  }
  if (type === "down" && !machines.value.length) {
    // no machine in the work centre of the operation (or no operation): all active machines
    const [d] = await getProductionMachines({ IsActive: true, PageSize: 300 });
    machines.value = rowsOf(camelize(d)).filter((m: any) => m.isActive !== false);
  }
}

function bump(key: "good" | "scrap" | "rework", delta: number) {
  const v = (parseAmount(f.value[key]) || 0) + delta;
  f.value[key] = String(Math.max(0, v));
}

async function submit() {
  const dlg = dialog.value;
  if (!dlg) return;
  const op = dlg.op;
  busy.value = true;
  let result: [any, any] = [null, null];
  const qty = () => ({ operationId: op.id, goodQty: parseAmount(f.value.good) || 0, scrapQty: parseAmount(f.value.scrap) || 0, reworkQty: parseAmount(f.value.rework) || 0, scrapReasonCodeId: f.value.scrapReasonId, note: f.value.note });
  if (dlg.type === "start" || dlg.type === "switch") result = await startOperation({ operationId: op.id, kind: f.value.kind, machineId: f.value.machineId, note: f.value.note });
  else if (dlg.type === "pause") result = await pauseOperation({ operationId: op.id, reasonCodeId: f.value.reasonId, note: f.value.note });
  else if (dlg.type === "qty") result = await reportQuantity(qty());
  else if (dlg.type === "finish") result = await finishOperation(qty());
  else if (dlg.type === "qc") result = await requestQc({ operationId: op.id, note: f.value.note });
  else if (dlg.type === "down") {
    if (!f.value.machineId) {
      busy.value = false;
      return say("bad", "ماشین را انتخاب کنید.");
    }
    result = await reportMachineDown({ machineId: f.value.machineId, operationId: op?.id ?? null, reasonCodeId: f.value.reasonId, description: f.value.description });
  }
  busy.value = false;
  if (result[1]) return failText(result[1]);
  const doneText: Record<string, string> = { start: "کار شروع شد.", switch: "نوع کار تغییر کرد.", pause: "کار متوقف شد.", qty: "تعداد ثبت شد.", finish: "عملیات پایان یافت.", qc: "درخواست بازرسی ثبت شد.", down: "خرابی ماشین گزارش شد." };
  dialog.value = null;
  say("ok", doneText[dlg.type]);
  await load();
}

onMounted(async () => {
  workCenters.value = await loadWorkCenters();
  reasons.value = (await loadReasons()).map((r) => ({ ...r }));
  await load();
  scanInput.value?.focus();
  refreshTimer = setInterval(refreshQuiet, 30000);
  clockTimer = setInterval(() => (now.value = Date.now()), 1000);
});
onBeforeUnmount(() => {
  clearInterval(refreshTimer);
  clearInterval(clockTimer);
});
</script>

<template>
  <div class="tm-page">
    <div class="tm-top">
      <h1 class="tm-title">ترمینال کارگاه</h1>
      <form class="tm-scan" @submit.prevent="scan">
        <input ref="scanInput" v-model="scanText" autocomplete="off" class="tm-input inv-ltr" inputmode="text" placeholder="اسکن برگه کار یا سریال" />
        <button :disabled="busy" class="tm-btn tm-btn-primary" type="submit">جستجو</button>
      </form>
      <select v-model="wcId" class="tm-input tm-select" @change="load">
        <option :value="null">همه مراکز کار</option>
        <option v-for="w in workCenters" :key="w.id" :value="w.id">{{ w.title }}</option>
      </select>
      <button class="tm-btn tm-btn-warn" @click="openDialog('down', running)">خرابی ماشین</button>
      <button class="tm-btn" @click="load">به‌روزرسانی</button>
    </div>

    <div v-if="banner" :class="banner.kind" class="tm-banner" @click="banner = null">{{ banner.text }}</div>
    <div v-if="scanned" class="tm-scanned">
      نتیجه جستجوی <b class="inv-ltr">{{ scanned }}</b>
      <button class="tm-btn tm-btn-sm" @click="clearScan">بازگشت به کارهای من</button>
    </div>

    <div v-if="loaded && !ops.length" class="tm-empty">{{ scanned ? "کاری پیدا نشد." : "کار آماده‌ای برای شما نیست." }}</div>

    <div class="tm-grid">
      <div v-for="op in ops" :key="op.id" :class="{ running: op.myRunning }" class="tm-card">
        <div class="tm-card-head">
          <span class="tm-job inv-ltr">{{ op.jobCard }}</span>
          <span :class="badgeOfOperation(op.status)" class="inv-badge tm-badge">{{ operationStatuses[op.status] }}</span>
        </div>
        <div class="tm-item">{{ op.itemTitle || op.itemCode }}</div>
        <div class="tm-step">مرحله {{ op.sequence }}: {{ op.title }}</div>
        <div class="tm-meta">
          <span>مرکز کار: {{ op.workCenterTitle || op.workCenterCode }}</span>
          <span v-if="op.needDateText">نیاز: {{ op.needDateText }}</span>
        </div>
        <div class="tm-qty">
          <div><small>تعداد</small><b>{{ qtyText(op.expectedQty) }}</b></div>
          <div><small>سالم</small><b class="good">{{ qtyText(op.goodQty) }}</b></div>
          <div><small>ضایعات</small><b class="scrap">{{ qtyText(op.scrapQty) }}</b></div>
        </div>
        <div v-if="op.myRunning" class="tm-timer inv-ltr">{{ elapsed(op) }}</div>
        <div class="tm-actions">
          <template v-if="op.myRunning">
            <button class="tm-btn tm-btn-warn" @click="openDialog('pause', op)">توقف</button>
            <button class="tm-btn" @click="openDialog('switch', op)">تغییر نوع کار</button>
            <button class="tm-btn" @click="openDialog('qty', op)">ثبت تعداد</button>
            <button class="tm-btn tm-btn-ok" @click="openDialog('finish', op)">پایان</button>
            <button v-if="canQc(op)" class="tm-btn" @click="openDialog('qc', op)">درخواست بازرسی</button>
          </template>
          <template v-else>
            <button class="tm-btn tm-btn-primary" @click="openDialog('start', op)">شروع</button>
            <template v-if="started(op)">
              <template v-if="op.hasMyBooking">
                <button class="tm-btn" @click="openDialog('qty', op)">ثبت تعداد</button>
                <button class="tm-btn tm-btn-ok" @click="openDialog('finish', op)">پایان</button>
              </template>
              <button v-if="canQc(op)" class="tm-btn" @click="openDialog('qc', op)">درخواست بازرسی</button>
            </template>
          </template>
        </div>
      </div>
    </div>

    <div v-if="dialog" class="tm-backdrop" @click.self="dialog = null">
      <div class="tm-dialog">
        <h2 class="tm-dtitle">
          {{ ({ start: "شروع کار", "switch": "تغییر نوع کار", pause: "توقف کار", qty: "ثبت تعداد", finish: "پایان عملیات", qc: "درخواست بازرسی", down: "گزارش خرابی ماشین" })[dialog.type] }}
          <small v-if="dialog.op" class="inv-ltr"> {{ dialog.op.jobCard }}</small>
        </h2>
        <div v-if="banner && banner.kind === 'bad'" class="tm-banner bad" @click="banner = null">{{ banner.text }}</div>

        <template v-if="dialog.type === 'start' || dialog.type === 'switch'">
          <div class="tm-label">نوع کار</div>
          <div class="tm-choices">
            <button v-for="k in kindsForStart" :key="k" :class="{ on: f.kind === k }" class="tm-btn" @click="f.kind = k">{{ bookingKinds[k] }}</button>
          </div>
          <template v-if="machines.length">
            <div class="tm-label">ماشین</div>
            <div class="tm-choices">
              <button v-for="m in machines" :key="m.machineId" :class="{ on: f.machineId === m.machineId }" class="tm-btn" @click="f.machineId = f.machineId === m.machineId ? null : m.machineId">{{ m.title }}</button>
            </div>
          </template>
        </template>

        <template v-if="dialog.type === 'pause'">
          <div class="tm-label">دلیل توقف</div>
          <div class="tm-choices">
            <button v-for="r in downReasons" :key="r.id" :class="{ on: f.reasonId === r.id }" class="tm-btn" @click="f.reasonId = f.reasonId === r.id ? null : r.id">{{ r.title }}</button>
          </div>
        </template>

        <template v-if="dialog.type === 'qty' || dialog.type === 'finish'">
          <div v-if="dialog.op" class="tm-hint">باقی‌مانده تا تعداد سفارش: <b>{{ qtyText(remaining(dialog.op)) }}</b></div>
          <div v-for="row in [{ k: 'good', t: 'سالم' }, { k: 'scrap', t: 'ضایعات' }, { k: 'rework', t: 'دوباره‌کاری' }]" :key="row.k" class="tm-stepper">
            <span class="tm-label">{{ row.t }}</span>
            <button class="tm-btn tm-round" @click="bump(row.k as any, -1)">-</button>
            <input v-model="(f as any)[row.k]" class="tm-input tm-num inv-ltr" inputmode="decimal" placeholder="0" />
            <button class="tm-btn tm-round" @click="bump(row.k as any, 1)">+</button>
          </div>
          <template v-if="(parseAmount(f.scrap) || 0) > 0">
            <div class="tm-label">دلیل ضایعات</div>
            <div class="tm-choices">
              <button v-for="r in scrapReasons" :key="r.id" :class="{ on: f.scrapReasonId === r.id }" class="tm-btn" @click="f.scrapReasonId = r.id">{{ r.title }}</button>
            </div>
          </template>
        </template>

        <template v-if="dialog.type === 'down'">
          <div class="tm-label">ماشین</div>
          <div class="tm-choices">
            <button v-for="m in machines" :key="m.machineId" :class="{ on: f.machineId === m.machineId }" class="tm-btn" @click="f.machineId = m.machineId">{{ m.title }}</button>
          </div>
          <div v-if="downReasons.length" class="tm-label">دلیل</div>
          <div class="tm-choices">
            <button v-for="r in downReasons" :key="r.id" :class="{ on: f.reasonId === r.id }" class="tm-btn" @click="f.reasonId = f.reasonId === r.id ? null : r.id">{{ r.title }}</button>
          </div>
          <div class="tm-label">شرح خرابی</div>
          <textarea v-model="f.description" class="tm-input" rows="3"></textarea>
        </template>

        <template v-if="dialog.type !== 'down'">
          <div class="tm-label">یادداشت (اختیاری)</div>
          <input v-model="f.note" class="tm-input" />
        </template>

        <div class="tm-dfoot">
          <button class="tm-btn" @click="dialog = null">انصراف</button>
          <button :disabled="busy" class="tm-btn tm-btn-primary" @click="submit">تایید</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.tm-page { padding: 1rem; font-size: 1.25rem; direction: rtl; }
.tm-top { display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: center; margin-bottom: 1rem; }
.tm-title { font-size: 1.8rem; margin: 0; }
.tm-scan { display: flex; gap: 0.5rem; flex: 1 1 22rem; }
.tm-input { font-size: 1.4rem; min-height: 3.5rem; padding: 0.4rem 0.9rem; border: 2px solid #ced4da; border-radius: 0.75rem; width: 100%; background: #fff; }
.tm-select { width: auto; min-width: 14rem; }
.tm-btn { font-size: 1.35rem; min-height: 3.5rem; min-width: 6rem; padding: 0.4rem 1.2rem; border: 2px solid #adb5bd; border-radius: 0.75rem; background: #f8f9fa; cursor: pointer; touch-action: manipulation; }
.tm-btn:active { filter: brightness(0.9); }
.tm-btn:disabled { opacity: 0.5; }
.tm-btn-sm { min-height: 2.5rem; font-size: 1rem; }
.tm-btn-primary { background: #0d6efd; border-color: #0d6efd; color: #fff; }
.tm-btn-ok { background: #198754; border-color: #198754; color: #fff; }
.tm-btn-warn { background: #ffc107; border-color: #ffc107; color: #000; }
.tm-btn.on { background: #0d6efd; border-color: #0d6efd; color: #fff; }
.tm-round { min-width: 3.5rem; padding: 0; font-size: 2rem; }
.tm-banner { padding: 1rem 1.25rem; border-radius: 0.75rem; font-size: 1.5rem; font-weight: 600; margin-bottom: 1rem; cursor: pointer; }
.tm-banner.ok { background: #d1e7dd; color: #0f5132; }
.tm-banner.bad { background: #f8d7da; color: #842029; }
.tm-scanned { margin-bottom: 1rem; display: flex; gap: 1rem; align-items: center; }
.tm-empty { text-align: center; padding: 3rem; font-size: 1.6rem; color: #6c757d; }
.tm-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(21rem, 1fr)); gap: 1rem; }
.tm-card { border: 2px solid #dee2e6; border-radius: 1rem; padding: 1rem; background: #fff; display: flex; flex-direction: column; gap: 0.5rem; }
.tm-card.running { border-color: #198754; box-shadow: 0 0 0 4px rgba(25, 135, 84, 0.25); background: #f3fbf6; }
.tm-card-head { display: flex; justify-content: space-between; align-items: center; }
.tm-job { font-size: 1.5rem; font-weight: 700; }
.tm-badge { font-size: 1rem; padding: 0.3rem 0.7rem; }
.tm-item { font-size: 1.5rem; font-weight: 600; }
.tm-step { font-size: 1.3rem; }
.tm-meta { display: flex; flex-wrap: wrap; gap: 0.5rem 1.2rem; font-size: 1.05rem; color: #495057; }
.tm-qty { display: grid; grid-template-columns: repeat(3, 1fr); text-align: center; gap: 0.5rem; }
.tm-qty small { display: block; color: #6c757d; font-size: 1rem; }
.tm-qty b { font-size: 1.8rem; }
.tm-qty .good { color: #198754; }
.tm-qty .scrap { color: #dc3545; }
.tm-timer { text-align: center; font-size: 2.6rem; font-weight: 700; color: #198754; font-variant-numeric: tabular-nums; }
.tm-actions { display: flex; flex-wrap: wrap; gap: 0.6rem; margin-top: auto; }
.tm-actions .tm-btn { flex: 1 1 auto; }
.tm-backdrop { position: fixed; inset: 0; background: rgba(0, 0, 0, 0.55); display: flex; align-items: center; justify-content: center; z-index: 1050; padding: 1rem; }
.tm-dialog { background: #fff; border-radius: 1rem; padding: 1.25rem; width: min(46rem, 100%); max-height: 94vh; overflow-y: auto; }
.tm-dtitle { font-size: 1.7rem; margin-bottom: 1rem; }
.tm-label { font-size: 1.15rem; color: #495057; margin: 0.8rem 0 0.3rem; }
.tm-choices { display: flex; flex-wrap: wrap; gap: 0.6rem; }
.tm-hint { font-size: 1.2rem; margin-bottom: 0.5rem; }
.tm-stepper { display: flex; align-items: center; gap: 0.6rem; margin-bottom: 0.5rem; }
.tm-stepper .tm-label { width: 8rem; margin: 0; }
.tm-num { text-align: center; font-size: 2rem; max-width: 10rem; }
.tm-dfoot { display: flex; justify-content: space-between; gap: 1rem; margin-top: 1.2rem; }
.tm-dfoot .tm-btn { flex: 1; }
</style>
