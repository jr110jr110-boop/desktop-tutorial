<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import {
  createBalanceDrafts, decideReservationRelease, getBalanceDiff, getBalanceProjects, getBalanceRun, getBalanceRuns, getBalanceSettings, getLatestBalance, getReservations, requestReservationRelease,
  runBalance, runKits, saveBalanceSetting, updateReservation,
} from "@/api/ProjectBalanceAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { BalanceRoles, diffTitles, priorityTitles, qty, requirementKinds, reservationStatusClass, reservationStatuses } from "@/composables/projectBalance";

// The automatic material balance of a project: the project copy of the BOM (all levels, with the approved deviations) is netted against stock, reservations, requests and orders.
// The balance never submits or approves a purchase request: it only drafts them, and planning sends them like any other request.
const router = useRouter();
const route = useRoute();
const projects = ref<any[]>([]);
const workOrderId = ref<number | null>(null);
const run = ref<any>(null);
const runs = ref<any[]>([]);
const diff = ref<any>(null);
const reservations = ref<any[]>([]);
const settings = ref<any[]>([]);
const tab = ref<"lines" | "diff" | "reservations" | "settings">("lines");
const onlyShort = ref(false);
const contains = ref("");
const busy = ref(false);
const loading = ref(false);
const drafts = ref<any>(null);
const kits = ref<any>(null);
const canPlan = hasRole(BalanceRoles.planning);
const edit = ref<any>(null);
const settingEdit = ref<Record<string, string>>({});

const project = computed(() => projects.value.find((p) => p.workOrderId === workOrderId.value));
const lines = computed(() => {
  let rows = (run.value?.lines || []) as any[];
  if (onlyShort.value) rows = rows.filter((l) => l.shortQty > 0);
  const text = contains.value.trim().toLowerCase();
  if (text) rows = rows.filter((l) => [l.itemCode, l.itemTitle, l.partNumber, l.partName, l.wbsNodeCode].some((v) => (v || "").toLowerCase().includes(text)));
  return rows;
});
const totalShort = computed(() => ((run.value?.lines || []) as any[]).filter((l) => l.shortQty > 0).length);

async function loadProjects() {
  const [data, error] = await getBalanceProjects();
  if (error) return fail(error);
  projects.value = data || [];
}
async function load() {
  run.value = null; diff.value = null; drafts.value = null; runs.value = [];
  if (!workOrderId.value) return;
  loading.value = true;
  const [latest] = await getLatestBalance(workOrderId.value);
  run.value = latest || null;
  const [list] = await getBalanceRuns(workOrderId.value);
  runs.value = list || [];
  loading.value = false;
  if (tab.value === "reservations") loadReservations();
}
async function openRun(id: number) {
  const [data, error] = await getBalanceRun(id);
  if (error) return fail(error);
  run.value = data;
  diff.value = null;
}
async function execute(createDrafts: boolean) {
  busy.value = true;
  const [data, error] = await runBalance({ workOrderId: workOrderId.value, createDrafts });
  busy.value = false;
  if (error) return fail(error);
  run.value = data.runs?.[0] || run.value;
  drafts.value = data.drafts?.[0] || null;
  (data.notes || []).forEach((n: string) => ok(n));
  ok("بالانس مواد اجرا شد.");
  await loadProjects();
  const [list] = await getBalanceRuns(workOrderId.value!);
  runs.value = list || [];
}
async function makeDrafts() {
  if (!run.value) return;
  busy.value = true;
  const [data, error] = await createBalanceDrafts(run.value.id);
  busy.value = false;
  if (error) return fail(error);
  drafts.value = data;
  ok("پیش‌نویس درخواست‌های خرید ساخته یا به‌روز شد؛ ارسال آن‌ها با برنامه‌ریزی است.");
  openRun(run.value.id);
  loadProjects();
}
async function showDiff() {
  if (!run.value) return;
  const [data, error] = await getBalanceDiff(run.value.id);
  if (error) return fail(error);
  diff.value = data;
  tab.value = "diff";
}
async function loadReservations() {
  if (!workOrderId.value) return;
  const [data, error] = await getReservations({ workOrderId: workOrderId.value });
  if (error) return fail(error);
  reservations.value = data || [];
}
async function loadSettings() {
  const [data, error] = await getBalanceSettings();
  if (error) return fail(error);
  settings.value = data || [];
  settingEdit.value = Object.fromEntries((data || []).map((s: any) => [s.key, s.value]));
}
function pick(t: typeof tab.value) {
  tab.value = t;
  if (t === "reservations") loadReservations();
  if (t === "settings") loadSettings();
  if (t === "diff" && !diff.value) showDiff();
}
async function saveReservation() {
  const e = edit.value;
  busy.value = true;
  const [, error] = await updateReservation({ id: e.id, quantity: e.quantity, isHard: e.isHard, needDate: e.needDate || null, reason: e.reason });
  busy.value = false;
  if (error) return fail(error);
  edit.value = null;
  ok("رزرو ویرایش شد؛ دلیل در سابقهٔ تغییرات می‌ماند.");
  loadReservations();
}
async function askRelease(r: any) {
  const reason = window.prompt("دلیل آزادسازی رزرو");
  if (!reason || !reason.trim()) return;
  const [, error] = await requestReservationRelease(r.id, reason);
  if (error) return fail(error);
  ok("درخواست آزادسازی ثبت شد؛ فرد دیگری باید تایید کند.");
  loadReservations();
}
async function decide(r: any, approve: boolean) {
  let reason: string | undefined;
  if (!approve) {
    reason = window.prompt("دلیل رد درخواست آزادسازی") || "";
    if (!reason.trim()) return;
  }
  const [, error] = await decideReservationRelease(r.id, approve, reason);
  if (error) return fail(error);
  ok(approve ? "رزرو آزاد شد." : "درخواست آزادسازی رد شد.");
  loadReservations();
}
async function setSetting(key: string) {
  const [, error] = await saveBalanceSetting(key, settingEdit.value[key]);
  if (error) return fail(error);
  ok("تنظیم ذخیره شد.");
  loadSettings();
}
async function kitRun() {
  busy.value = true;
  const [data, error] = await runKits();
  busy.value = false;
  if (error) return fail(error);
  kits.value = data;
  ok("کیت‌های پای‌کار بررسی شد.");
}
const openRequest = (id: number) => router.push({ name: "ProcurementRequestEdit", params: { id } }).catch(() => undefined);
const rowClass = (l: any) => (l.shortQty > 0 ? "table-danger" : l.kind === 3 ? "table-warning" : "");

onMounted(async () => {
  await loadProjects();
  const q = Number(route.query.workOrderId || 0);
  if (q) { workOrderId.value = q; await load(); }
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="بالانس خودکار مواد پروژه" :subtitle="project ? project.workOrderCode : ''" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">بالانس خودکار مواد پروژه</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canPlan" :disabled="busy" class="btn btn-sm btn-outline-secondary" title="پیش‌نویس درخواست کیت پای‌کار برای فعالیت‌هایی که تا چند روز دیگر شروع می‌شوند" @click="kitRun">کیت پای‌کار ۷ روزه</button>
        </div>
      </div>

      <div class="inv-filters mb-3 d-print-none">
        <select v-model="workOrderId" class="form-select form-select-sm" style="max-width: 420px" @change="load">
          <option :value="null">پروژه (دستور کار) را انتخاب کنید</option>
          <option v-for="p in projects" :key="p.workOrderId" :value="p.workOrderId">
            {{ p.contractCode }} / {{ p.workOrderCode }} — {{ p.workOrderTitle }}{{ p.latestRunNumber ? " · اجرای " + p.latestRunNumber + " · کمبود " + p.shortLineCount : "" }}
          </option>
        </select>
        <button v-if="canPlan" :disabled="busy || !workOrderId" class="btn btn-sm btn-primary" @click="execute(false)">اجرای بالانس</button>
        <button v-if="canPlan" :disabled="busy || !workOrderId" class="btn btn-sm btn-outline-primary" title="اجرا و ساخت یا به‌روزرسانی پیش‌نویس درخواست‌های خرید" @click="execute(true)">اجرا و ساخت پیش‌نویس خرید</button>
      </div>
      <p v-if="!projects.length" class="inv-muted">پروژه‌ای که BOM آن از بانک محصول گرفته شده باشد وجود ندارد.</p>

      <div v-if="kits" class="alert alert-info py-2 small">
        {{ kits.drafted }} کیت پیش‌نویس ساخته شد ({{ kits.skipped }} فعالیت رد شد): <span v-for="n in kits.requests" :key="n" class="inv-ltr me-2">{{ n }}</span>
        <div v-for="(t, i) in kits.notes" :key="i">{{ t }}</div>
      </div>

      <template v-if="workOrderId">
        <p v-if="loading" class="inv-muted">در حال بارگذاری...</p>
        <p v-else-if="!run" class="inv-muted">برای این پروژه هنوز بالانسی اجرا نشده است.</p>

        <template v-if="run">
          <p class="small mb-2">
            اجرای <b>{{ run.runNumber }}</b> · {{ jalali(run.runAt, true) }} · {{ run.runByName || "کار خودکار" }} · عامل: {{ run.trigger }} ·
            BOM <span class="inv-ltr">{{ run.bomCode }}</span> نسخه {{ run.bomRevision }} · {{ run.lineCount }} ردیف ·
            <span :class="totalShort ? 'inv-badge bad' : 'inv-badge ok'">{{ totalShort }} ردیف دارای کمبود</span>
            <span v-if="run.notCodedCount" class="inv-badge warn ms-1">{{ run.notCodedCount }} ماده اولیه کدگذاری نشده</span>
            <span v-if="!run.isLatest" class="inv-badge warn ms-1">اجرای قدیمی‌تر</span>
          </p>
          <div v-if="drafts" class="alert alert-success py-2 small">
            پیش‌نویس خرید: {{ drafts.created }} ساخته، {{ drafts.updated }} به‌روز، {{ drafts.removedEmpty }} خالی حذف شد.
            <a v-for="r in drafts.requests" :key="r.id" href="#" class="inv-ltr me-2" @click.prevent="openRequest(r.id)">{{ r.number }} ({{ r.lineCount }} ردیف، {{ r.linkedDocuments }} مدرک)</a>
            <div v-for="(t, i) in drafts.notes" :key="i">{{ t }}</div>
          </div>

          <ul class="nav nav-tabs mb-3 d-print-none">
            <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'lines' }" href="#" @click.prevent="pick('lines')">نتیجه</a></li>
            <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'diff' }" href="#" @click.prevent="pick('diff')">تفاوت با اجرای قبل</a></li>
            <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'reservations' }" href="#" @click.prevent="pick('reservations')">رزروها</a></li>
            <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'settings' }" href="#" @click.prevent="pick('settings')">تنظیمات</a></li>
          </ul>

          <div v-if="tab === 'lines'">
            <div class="inv-filters mb-2 d-print-none">
              <input v-model="contains" class="form-control form-control-sm" style="max-width: 240px" placeholder="جست‌وجو کد، نام یا شماره قطعه" />
              <label class="small"><input v-model="onlyShort" type="checkbox" /> فقط کمبودها</label>
              <select v-if="runs.length > 1" class="form-select form-select-sm" style="max-width: 220px" @change="openRun(Number(($event.target as HTMLSelectElement).value))">
                <option v-for="r in runs" :key="r.id" :value="r.id" :selected="r.id === run.id">اجرای {{ r.runNumber }} — {{ jalali(r.runAt) }}</option>
              </select>
              <button v-if="canPlan && run.isLatest" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="makeDrafts">ساخت/به‌روزرسانی پیش‌نویس خرید</button>
            </div>
            <div class="table-responsive">
              <table class="table table-sm table-hover">
                <thead>
                  <tr><th>کالا</th><th>گره WBS</th><th>تاریخ سفارش</th><th>نیاز تا</th><th>اولویت</th><th>خام</th><th>پرت</th><th>نیاز</th><th>حواله‌شده</th><th>رزرو</th><th>موجود</th><th>در راه (PR)</th><th>در راه (PO)</th><th>جایگزین</th><th>کمبود</th><th>درخواست</th></tr>
                </thead>
                <tbody>
                  <tr v-if="!lines.length"><td colspan="16" class="text-center inv-muted">ردیفی نیست.</td></tr>
                  <tr v-for="l in lines" :key="l.id" :class="rowClass(l)">
                    <td>
                      <span class="inv-ltr fw-bold">{{ l.itemCode || l.partNumber }}</span> {{ l.itemTitle || l.partName }}
                      <span class="inv-muted small">({{ requirementKinds[l.kind] }}) {{ l.unit }}</span>
                      <div v-if="l.note" class="small inv-muted">{{ l.note }}</div>
                    </td>
                    <td class="inv-ltr small">{{ l.wbsNodeCode }}<span v-if="l.activityCode"> · {{ l.activityCode }}</span></td>
                    <td class="text-nowrap">{{ jalali(l.needDate) }}</td>
                    <td class="text-nowrap">{{ jalali(l.useByDate) }}</td>
                    <td><span class="inv-badge" :class="l.priority >= 2 ? 'bad' : ''">{{ priorityTitles[l.priority] }}</span></td>
                    <td>{{ qty(l.grossQty) }}</td>
                    <td>{{ qty(l.scrapQty) }}</td>
                    <td class="fw-bold">{{ qty(l.requiredQty) }}</td>
                    <td>{{ qty(l.issuedQty) }}</td>
                    <td>{{ qty(l.reservedQty) }}</td>
                    <td>{{ qty(l.onHandQty) }}</td>
                    <td>{{ qty(l.onOrderPrQty) }}</td>
                    <td>{{ qty(l.onOrderPoQty) }}</td>
                    <td>{{ qty(l.substituteQty) }}<span v-if="l.substituteItemCode" class="inv-ltr small"> ({{ l.substituteItemCode }})</span></td>
                    <td><b :class="l.shortQty > 0 ? 'text-danger' : ''">{{ qty(l.shortQty) }}</b></td>
                    <td><a v-if="l.purchaseRequestId" href="#" class="inv-ltr" @click.prevent="openRequest(l.purchaseRequestId)">{{ l.purchaseRequestNumber || l.purchaseRequestId }}</a></td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p class="inv-muted small mb-0">
              نیاز = خام + پرت (ضریب پرت BOM). کمبود = نیاز − حواله‌شده − رزرو − موجودی آزاد − درخواست‌های خرید باز − سفارش‌هایی که تا زمان مصرف می‌رسند − جایگزین تاییدشده.
              موجودی فقط از انبارهای شمرده‌شده (قرنطینه و موجودی/رزرو پروژه‌های دیگر کسر می‌شود).
            </p>
          </div>

          <div v-if="tab === 'diff'">
            <p v-if="!diff" class="inv-muted">برای این اجرا اجرای قبلی نیست.</p>
            <template v-else>
              <p class="small">اجرای {{ diff.fromRunNumber }} ← {{ diff.toRunNumber }}: {{ diff.added }} افزوده، {{ diff.removed }} حذف، {{ diff.changed }} تغییر.</p>
              <table class="table table-sm">
                <thead><tr><th>تغییر</th><th>کالا</th><th>نیاز قبلی</th><th>نیاز جدید</th><th>کمبود قبلی</th><th>کمبود جدید</th><th>رزرو قبلی</th><th>رزرو جدید</th></tr></thead>
                <tbody>
                  <tr v-if="!diff.rows.length"><td colspan="8" class="text-center inv-muted">تفاوتی نیست.</td></tr>
                  <tr v-for="(d, i) in diff.rows" :key="i">
                    <td><span class="inv-badge" :class="d.change === 'Added' ? 'ok' : d.change === 'Removed' ? 'bad' : 'warn'">{{ diffTitles[d.change] || d.change }}</span></td>
                    <td><span class="inv-ltr fw-bold">{{ d.itemCode || d.partNumber }}</span> {{ d.partName }}</td>
                    <td>{{ qty(d.oldRequired) }}</td><td>{{ qty(d.newRequired) }}</td><td>{{ qty(d.oldShort) }}</td><td>{{ qty(d.newShort) }}</td><td>{{ qty(d.oldReserved) }}</td><td>{{ qty(d.newReserved) }}</td>
                  </tr>
                </tbody>
              </table>
            </template>
          </div>

          <div v-if="tab === 'reservations'">
            <div class="table-responsive">
              <table class="table table-sm table-hover">
                <thead><tr><th>کالا</th><th>مقدار</th><th>حواله‌شده</th><th>مانده</th><th>نوع</th><th>وضعیت</th><th>نیاز تا</th><th>دلیل / آخرین تغییر</th><th class="d-print-none"></th></tr></thead>
                <tbody>
                  <tr v-if="!reservations.length"><td colspan="9" class="text-center inv-muted">رزروی نیست.</td></tr>
                  <tr v-for="r in reservations" :key="r.id">
                    <td><span class="inv-ltr fw-bold">{{ r.itemCode }}</span> {{ r.itemTitle }}<span v-if="r.substituteForItemCode" class="inv-muted small"> (جایگزین {{ r.substituteForItemCode }})</span><span v-if="r.lotNumber" class="inv-ltr small"> · {{ r.lotNumber }}</span></td>
                    <td>{{ qty(r.quantity) }} {{ r.unit }}</td>
                    <td>{{ qty(r.issuedQuantity) }}</td>
                    <td>{{ qty(r.remainingQuantity) }}</td>
                    <td><span class="inv-badge" :class="r.isHard ? 'bad' : ''">{{ r.isHard ? "سخت" : "نرم" }}</span></td>
                    <td><span class="inv-badge" :class="reservationStatusClass[r.status]">{{ reservationStatuses[r.status] }}</span><span v-if="r.releaseRequestedAt" class="inv-badge warn ms-1">درخواست آزادسازی: {{ r.releaseRequestedByName }}</span></td>
                    <td class="text-nowrap">{{ jalali(r.needDate) }}</td>
                    <td class="small">{{ r.releaseReason || r.reason }}</td>
                    <td class="d-print-none text-nowrap">
                      <button v-if="r.canEdit" class="btn btn-sm btn-outline-secondary" @click="edit = { ...r, reason: '' }">ویرایش</button>
                      <button v-if="r.canRequestRelease" class="btn btn-sm btn-outline-warning ms-1" @click="askRelease(r)">درخواست آزادسازی</button>
                      <button v-if="r.canApproveRelease" class="btn btn-sm btn-outline-success ms-1" @click="decide(r, true)">تایید آزادسازی</button>
                      <button v-if="r.canApproveRelease" class="btn btn-sm btn-outline-danger ms-1" @click="decide(r, false)">رد</button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p class="inv-muted small mb-0">حواله از رزرو پروژهٔ دیگر تا آزادسازی تاییدشده رد می‌شود. درخواست‌دهندهٔ آزادسازی خودش آن را تایید نمی‌کند.</p>
          </div>

          <div v-if="tab === 'settings'">
            <table class="table table-sm">
              <thead><tr><th>تنظیم</th><th>مقدار</th><th>توضیح</th><th class="d-print-none"></th></tr></thead>
              <tbody>
                <tr v-for="s in settings" :key="s.key">
                  <td class="inv-ltr small">{{ s.key }}</td>
                  <td><input v-model="settingEdit[s.key]" class="form-control form-control-sm inv-ltr" :disabled="!canPlan" style="min-width: 120px" /></td>
                  <td class="small">{{ s.title }}<div class="inv-muted">{{ s.source }}</div></td>
                  <td class="d-print-none"><button v-if="canPlan" class="btn btn-sm btn-outline-primary" :disabled="settingEdit[s.key] === s.value" @click="setSetting(s.key)">ذخیره</button></td>
                </tr>
              </tbody>
            </table>
          </div>
        </template>
      </template>
    </div>

    <div v-if="edit" class="inv-modal-backdrop d-print-none" @click.self="edit = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">ویرایش رزرو {{ edit.itemCode }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">مقدار</label><input v-model.number="edit.quantity" type="number" min="0" step="any" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع رزرو</label><select v-model="edit.isHard" class="form-select form-select-sm"><option :value="true">سخت</option><option :value="false">نرم</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">نیاز تا</label><input v-model="edit.needDate" type="date" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">دلیل تغییر *</label><textarea v-model="edit.reason" rows="2" class="form-control form-control-sm"></textarea></div>
        </div>
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy || !edit.reason || !edit.reason.trim()" class="btn btn-sm btn-primary" @click="saveReservation">ذخیره</button>
          <button class="btn btn-sm btn-outline-secondary" @click="edit = null">انصراف</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
