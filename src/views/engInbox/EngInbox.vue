<script setup lang="ts">
// ENG-F1 «صندوق ورودی مهندسی»: what the engineering unit dropped into the server folder, what the engineering agent found wrong (and why it was refused) and what it did with each package.
// Read by EngInboxRoles.read; scan now and dismiss by ENGINEERING and PMO. The agent applies a package to the product bank only through the normal BOM and routing drafts (people still sign them).
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import { downloadEngInboxFile, dismissEngInboxPackage, getEngInboxPackage, getEngInboxPackages, getEngInboxStatus, scanEngInbox } from "@/api/EngInboxAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fa, faDigits, jalali } from "@/composables/agents";
import { fail, ok } from "@/composables/inventory";
import { EngInboxRoles, hasRole } from "@/composables/roles";

const route = useRoute();
const canOperate = hasRole(EngInboxRoles.operate);

const statusTitles: Record<number, string> = { 1: "دریافت‌شده", 2: "ردشده", 3: "اعمال‌شده", 4: "متوقف در اعمال", 5: "جایگزین‌شده", 6: "بسته‌شده" };
const kindTitles: Record<number, string> = { 1: "پلتفرم", 2: "پروژه" };
const makeBuyTitles: Record<number, string> = { 1: "ساخت", 2: "خرید", 3: "تأمین‌شده توسط کارفرما", 4: "پیمانکاری فرعی" };
// the status of the BOM (PbBomStatus) and of the routing (PbRoutingStatus) arrive as numbers and are named here
const bomStatusTitles: Record<number, string> = { 0: "پیش‌نویس", 1: "در حال بررسی", 2: "تأییدشده", 3: "منتشرشده", 4: "جایگزین‌شده", 5: "منسوخ" };
const routingStatusTitles: Record<number, string> = { 0: "پیش‌نویس", 1: "در حال بررسی", 2: "تأییدشده", 3: "جایگزین‌شده" };

const status = ref<any | null>(null);
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 20;
const filterStatus = ref<number | "">("");
const search = ref("");
const loading = ref(false);
const scanning = ref(false);
const selected = ref<any | null>(null);
const detailLoading = ref(false);
const tab = ref<"findings" | "bom" | "opc" | "docs" | "mdl">("findings");
const dismissReason = ref("");
const dismissing = ref(false);

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));
const countOf = (s: number) => status.value?.counts?.find((c: any) => c.status === s)?.count ?? 0;
const canDismissSelected = computed(() => canOperate && selected.value?.canDismiss);

let seq = 0;
async function loadStatus() {
  const [s, e] = await getEngInboxStatus();
  if (e) fail(e);
  else status.value = s;
}

async function loadList() {
  const mine = ++seq;
  loading.value = true;
  const [r, e] = await getEngInboxPackages({ Status: filterStatus.value === "" ? undefined : filterStatus.value, Search: search.value || undefined, PageNumber: page.value, PageSize: pageSize });
  if (mine !== seq) return;
  loading.value = false;
  if (e) return fail(e);
  rows.value = rowsOf(r);
  total.value = r?.totalItems ?? rows.value.length;
}

async function reload() {
  await Promise.all([loadStatus(), loadList()]);
  if (selected.value) await openPackage(selected.value.id, false);
}

function applyFilter() {
  page.value = 1;
  loadList();
}

function go(p: number) {
  page.value = Math.min(Math.max(1, p), pages.value);
  loadList();
}

async function scan() {
  scanning.value = true;
  const [r, e] = await scanEngInbox();
  scanning.value = false;
  if (e) return fail(e);
  if (r?.notConfigured) fail(r.note || "پوشهٔ صندوق ورودی در دسترس نیست.");
  else ok(r?.note || "بررسی پوشه انجام شد.");
  await reload();
}

async function openPackage(id: number, resetTab = true) {
  detailLoading.value = true;
  const [d, e] = await getEngInboxPackage(id);
  detailLoading.value = false;
  if (e) return fail(e);
  selected.value = d;
  if (resetTab) {
    tab.value = "findings";
    dismissReason.value = "";
  }
}

async function dismiss() {
  if (!selected.value) return;
  dismissing.value = true;
  const [, e] = await dismissEngInboxPackage({ id: selected.value.id, reason: dismissReason.value, rowVersion: selected.value.rowVersion });
  dismissing.value = false;
  if (e) return fail(e);
  ok("بسته بسته شد.");
  dismissReason.value = "";
  await reload();
}

async function download(which: "template" | "sample") {
  const error = await downloadEngInboxFile(which);
  if (error) fail(error);
}

// the BOM as an indented tree: every line sits under its parent (the root first); lines whose parent is unknown are shown last at level zero
const bomTree = computed<any[]>(() => {
  const lines: any[] = selected.value?.bomLines || [];
  const byParent = new Map<string, any[]>();
  for (const l of lines) {
    const key = l.parentCode || "";
    if (!byParent.has(key)) byParent.set(key, []);
    byParent.get(key)!.push(l);
  }
  const out: { line: any; depth: number }[] = [];
  const seen = new Set<any>();
  const walk = (parent: string, depth: number) => {
    for (const l of byParent.get(parent) || []) {
      if (seen.has(l)) continue;
      seen.add(l);
      out.push({ line: l, depth });
      walk(l.partCode, depth + 1);
    }
  };
  walk("", 0);
  for (const l of lines) if (!seen.has(l)) out.push({ line: l, depth: 0 });
  return out;
});

const statusClass = (s: number) => ({ 1: "info", 2: "bad", 3: "good", 4: "warn", 5: "muted", 6: "muted" } as Record<number, string>)[s] || "";

onMounted(async () => {
  await reload();
  const q = Number(route.query.package);
  if (Number.isInteger(q) && q > 0) await openPackage(q);
});
</script>

<template>
  <div class="inv-page eng-inbox">
    <div class="ei-card">
      <div class="ei-head">
        <div>
          <h1>صندوق ورودی مهندسی</h1>
          <p>واحد مهندسی بستهٔ اسناد را در پوشهٔ سرور می‌گذارد؛ ایجنت مهندسی آن را می‌خواند، خطاها را با علت می‌نویسد و BOM و مسیر ساخت را به‌صورت پیش‌نویس در بانک محصول می‌سازد. امضا و تأیید با انسان است.</p>
        </div>
        <div class="ei-actions">
          <button v-if="canOperate" :disabled="scanning || !status || !status.canScan" :title="status?.scanDisabledReason || ''" class="ei-btn primary" type="button" @click="scan">{{ scanning ? "در حال بررسی..." : "اسکن اکنون" }}</button>
          <button class="ei-btn" type="button" @click="download('template')">دریافت قالب خالی</button>
          <button class="ei-btn" type="button" @click="download('sample')">دریافت بستهٔ نمونه</button>
        </div>
      </div>
      <div v-if="status" class="ei-source">
        <span>پوشه: <bdi class="inv-ltr" dir="ltr">{{ status.location }}</bdi></span>
        <span v-if="!status.isConfigured" class="ei-badge bad">{{ status.notConfiguredReason }}</span>
        <span v-if="status.lastScanAt" class="inv-muted">آخرین بررسی: {{ jalali(status.lastScanAt, true) }} — {{ status.lastScanNote }}</span>
        <span v-else class="inv-muted">هنوز بررسی انجام نشده است.</span>
      </div>
      <p v-if="status && canOperate && status.scanDisabledReason" class="ei-warn">{{ status.scanDisabledReason }}</p>
      <p v-if="status && status.applyPausedReason" class="ei-warn">{{ status.applyPausedReason }}</p>
      <div v-if="status" class="ei-tiles">
        <button v-for="s in [1, 2, 4, 3, 5, 6]" :key="s" :class="['ei-tile', statusClass(s), { on: filterStatus === s }]" type="button" @click="filterStatus = filterStatus === s ? '' : s; applyFilter()">
          <b>{{ fa(countOf(s)) }}</b><span>{{ statusTitles[s] }}</span>
        </button>
      </div>
    </div>

    <div class="ei-card">
      <form class="ei-filter" @submit.prevent="applyFilter">
        <input v-model="search" placeholder="جست‌وجو: پلتفرم، استاندارد، قرارداد، پروژه یا عنوان" type="search" />
        <select v-model="filterStatus" @change="applyFilter">
          <option value="">همهٔ وضعیت‌ها</option>
          <option v-for="(t, k) in statusTitles" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <button class="ei-btn" type="submit">اعمال</button>
      </form>
      <div v-if="loading" class="ei-empty">در حال بارگذاری...</div>
      <div v-else-if="!rows.length" class="ei-empty">بسته‌ای برای نمایش نیست.</div>
      <div v-else class="ei-scroll">
        <table class="ei-table">
          <thead>
            <tr><th>نوع</th><th>پلتفرم / پروژه</th><th>استاندارد</th><th>نسخه</th><th>وضعیت</th><th>خطا / هشدار</th><th>BOM / مسیر / سند</th><th>زمان دریافت</th></tr>
          </thead>
          <tbody>
            <tr v-for="p in rows" :key="p.id" :class="{ on: selected?.id === p.id }" @click="openPackage(p.id)">
              <td>{{ kindTitles[p.kind] || p.kind }}</td>
              <td><bdi class="inv-ltr" dir="ltr">{{ p.kind === 1 ? p.platformCode : p.projectCode || p.contractCode }}</bdi></td>
              <td><bdi class="inv-ltr" dir="ltr">{{ p.standard }}</bdi></td>
              <td>{{ fa(p.packageVersion) }}</td>
              <td><span :class="['ei-badge', statusClass(p.status)]">{{ statusTitles[p.status] || p.status }}</span></td>
              <td>{{ fa(p.errorCount) }} / {{ fa(p.warningCount) }}</td>
              <td>{{ fa(p.bomCount) }} / {{ fa(p.opcCount) }} / {{ fa(p.docCount) }}</td>
              <td>{{ jalali(p.ingestedAt, true) }}</td>
            </tr>
          </tbody>
        </table>
        <div class="ei-pager">
          <button :disabled="page <= 1" class="ei-btn" type="button" @click="go(page - 1)">قبلی</button>
          <span>صفحهٔ {{ fa(page) }} از {{ fa(pages) }} ({{ fa(total) }} بسته)</span>
          <button :disabled="page >= pages" class="ei-btn" type="button" @click="go(page + 1)">بعدی</button>
        </div>
      </div>
    </div>

    <div v-if="selected" class="ei-card">
      <div class="ei-head">
        <div>
          <h2>{{ selected.title || selected.platformCode || selected.projectCode }}</h2>
          <p class="inv-muted">
            {{ kindTitles[selected.kind] }} — نسخهٔ {{ fa(selected.packageVersion) }} — <span :class="['ei-badge', statusClass(selected.status)]">{{ statusTitles[selected.status] }}</span>
            <template v-if="selected.preparedByText"> — تهیه‌کننده: {{ selected.preparedByText }}</template>
          </p>
          <p v-if="selected.statusNote">{{ selected.statusNote }}</p>
          <p v-if="selected.applyBlockReason" class="ei-warn">{{ selected.applyBlockReason }}</p>
          <p v-if="selected.dismissReason" class="inv-muted">علت بستن: {{ selected.dismissReason }}</p>
        </div>
        <div class="ei-links inv-muted">
          <div v-if="selected.productCode">محصول: <bdi class="inv-ltr" dir="ltr">{{ selected.productCode }}</bdi></div>
          <div v-if="selected.bomCode">BOM: <router-link v-if="selected.bomHeaderId" :to="{ name: 'ProductBankBomEdit', params: { id: selected.bomHeaderId } }"><bdi class="inv-ltr" dir="ltr">{{ selected.bomCode }}</bdi></router-link><bdi v-else class="inv-ltr" dir="ltr">{{ selected.bomCode }}</bdi> — ویرایش {{ fa(selected.bomRevision) }} ({{ bomStatusTitles[selected.bomStatusCode] || "" }})</div>
          <div v-if="selected.routingPartNumber">مسیر ساخت: <router-link v-if="selected.routingId" :to="{ name: 'ProductBankRoutingEdit', params: { id: selected.routingId } }"><bdi class="inv-ltr" dir="ltr">{{ selected.routingPartNumber }}</bdi></router-link><bdi v-else class="inv-ltr" dir="ltr">{{ selected.routingPartNumber }}</bdi> — ویرایش {{ fa(selected.routingRevision) }} ({{ routingStatusTitles[selected.routingStatusCode] || "" }})</div>
          <div v-if="selected.storedPath">پوشهٔ بایگانی: <bdi class="inv-ltr" dir="ltr">{{ selected.storedPath }}</bdi></div>
        </div>
      </div>

      <div class="ei-tabs">
        <button :class="{ on: tab === 'findings' }" type="button" @click="tab = 'findings'">خطاها و هشدارها ({{ fa(selected.findings?.length || 0) }})</button>
        <button :class="{ on: tab === 'bom' }" type="button" @click="tab = 'bom'">BOM ({{ fa(selected.bomLines?.length || 0) }})</button>
        <button :class="{ on: tab === 'opc' }" type="button" @click="tab = 'opc'">مسیر ساخت ({{ fa(selected.opcLines?.length || 0) }})</button>
        <button :class="{ on: tab === 'docs' }" type="button" @click="tab = 'docs'">اسناد ({{ fa(selected.documents?.length || 0) }})</button>
        <button :class="{ on: tab === 'mdl' }" type="button" @click="tab = 'mdl'">MDL ({{ fa(selected.mdlLines?.length || 0) }})</button>
      </div>

      <div v-if="detailLoading" class="ei-empty">در حال بارگذاری...</div>
      <div v-else class="ei-scroll">
        <template v-if="tab === 'findings'">
          <div v-if="!selected.findings?.length" class="ei-empty">خطا یا هشداری ثبت نشده است.</div>
          <table v-else class="ei-table">
            <thead><tr><th>نوع</th><th>برگه</th><th>سطر</th><th>پیام</th></tr></thead>
            <tbody>
              <tr v-for="(f, i) in selected.findings" :key="i">
                <td><span :class="['ei-badge', f.severity === 'error' ? 'bad' : 'warn']">{{ f.severity === "error" ? "خطا" : "هشدار" }}</span></td>
                <td>{{ f.sheet }}</td>
                <td>{{ f.row ? fa(f.row) : "" }}</td>
                <td>{{ faDigits(f.message) }}</td>
              </tr>
            </tbody>
          </table>
        </template>
        <table v-else-if="tab === 'bom'" class="ei-table">
          <thead><tr><th>سطح</th><th>پدر</th><th>کد قطعه</th><th>شرح</th><th>تعداد</th><th>واحد</th><th>ساخت/خرید</th><th>جنس</th><th>نقشه</th></tr></thead>
          <tbody>
            <tr v-for="(n, i) in bomTree" :key="i">
              <td>{{ fa(n.line.level) }}</td>
              <td><bdi class="inv-ltr" dir="ltr">{{ n.line.parentCode }}</bdi></td>
              <td :style="{ paddingInlineStart: 10 + n.depth * 18 + 'px' }"><bdi class="inv-ltr" dir="ltr">{{ n.line.partCode }}</bdi></td>
              <td>{{ n.line.description }}</td>
              <td>{{ fa(n.line.qty) }}</td>
              <td>{{ n.line.unit }}</td>
              <td>{{ makeBuyTitles[n.line.makeBuy] || "" }}</td>
              <td>{{ n.line.material }}</td>
              <td><bdi class="inv-ltr" dir="ltr">{{ n.line.drawingNumber }} {{ n.line.drawingRevision }}</bdi></td>
            </tr>
          </tbody>
        </table>
        <table v-else-if="tab === 'opc'" class="ei-table">
          <thead><tr><th>قطعه</th><th>عملیات</th><th>شرح</th><th>مرکز کار</th><th>راه‌اندازی (دقیقه)</th><th>اجرا (دقیقه)</th><th>بازرسی</th><th>ویژه</th><th>پیش‌نیاز</th></tr></thead>
          <tbody>
            <tr v-for="(o, i) in selected.opcLines" :key="i">
              <td><bdi class="inv-ltr" dir="ltr">{{ o.partCode }}</bdi></td>
              <td>{{ fa(o.operationNumber) }}</td>
              <td>{{ o.description }}</td>
              <td><bdi class="inv-ltr" dir="ltr">{{ o.workCenterCode }}</bdi></td>
              <td>{{ fa(o.setupMinutes) }}</td>
              <td>{{ fa(o.runMinutes) }}</td>
              <td>{{ o.isInspection ? "بله" : "" }}</td>
              <td>{{ o.isSpecialProcess ? "بله" : "" }}</td>
              <td>{{ faDigits(o.predecessors) }}</td>
            </tr>
          </tbody>
        </table>
        <table v-else-if="tab === 'docs'" class="ei-table">
          <thead><tr><th>شمارهٔ سند</th><th>عنوان</th><th>ویرایش</th><th>نوع</th><th>الزامی</th><th>فایل</th><th>اندازه (بایت)</th><th>SHA-256</th></tr></thead>
          <tbody>
            <tr v-for="(d, i) in selected.documents" :key="i">
              <td><bdi class="inv-ltr" dir="ltr">{{ d.number }}</bdi></td>
              <td>{{ d.title }}</td>
              <td>{{ d.revision }}</td>
              <td>{{ d.docType }}</td>
              <td>{{ d.isRequired ? "بله" : "" }}</td>
              <td><bdi class="inv-ltr" dir="ltr">{{ d.fileName }}</bdi></td>
              <td>{{ d.fileSize != null ? fa(d.fileSize) : "" }}</td>
              <td><bdi class="inv-ltr ei-hash" dir="ltr">{{ d.fileSha256 }}</bdi></td>
            </tr>
          </tbody>
        </table>
        <table v-else class="ei-table">
          <thead><tr><th>شمارهٔ سند</th><th>عنوان</th><th>نوع</th><th>موعد تعهد</th></tr></thead>
          <tbody>
            <tr v-for="(m, i) in selected.mdlLines" :key="i">
              <td><bdi class="inv-ltr" dir="ltr">{{ m.number }}</bdi></td>
              <td>{{ m.title }}</td>
              <td>{{ m.docType }}</td>
              <td>{{ m.commitDate ? jalali(m.commitDate) : "" }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <form v-if="canDismissSelected" class="ei-dismiss" @submit.prevent="dismiss">
        <label>بستن بسته (علت دست‌کم ۱۵ نویسه)</label>
        <textarea v-model="dismissReason" rows="2" />
        <button :disabled="dismissing || dismissReason.trim().length < 15" class="ei-btn" type="submit">{{ dismissing ? "..." : "بستن بسته" }}</button>
      </form>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" scoped>
.eng-inbox {
  --ei-card: #ffffff;
  --ei-line: #d7dde3;
  --ei-text: #1f2a33;
  --ei-muted: #6b7a88;
  --ei-primary: #1f6f8b;
  --ei-good: #1f8a4c;
  --ei-bad: #c0392b;
  --ei-warn: #b9770e;
}
:global(.dark) .eng-inbox,
:global([data-theme="dark"]) .eng-inbox {
  --ei-card: #1d252d;
  --ei-line: #34414d;
  --ei-text: #e4eaef;
  --ei-muted: #98a6b3;
}
.ei-card { background: var(--ei-card); border: 1px solid var(--ei-line); border-radius: 12px; padding: 16px; margin-bottom: 16px; color: var(--ei-text); }
.ei-head { display: flex; flex-wrap: wrap; gap: 16px; justify-content: space-between; align-items: flex-start; }
.ei-head h1, .ei-head h2 { margin: 0 0 6px; }
.ei-head p { margin: 4px 0; max-width: 62ch; }
.ei-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.ei-source { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-top: 12px; }
.ei-tiles { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 14px; }
.ei-tile { display: flex; flex-direction: column; min-width: 110px; padding: 10px 14px; border: 1px solid var(--ei-line); border-radius: 10px; background: transparent; color: inherit; cursor: pointer; text-align: start;
  b { font-size: 1.4rem; }
  span { color: var(--ei-muted); font-size: .85rem; }
  &.on { border-color: var(--ei-primary); box-shadow: 0 0 0 1px var(--ei-primary); }
  &.bad b { color: var(--ei-bad); }
  &.warn b { color: var(--ei-warn); }
  &.good b { color: var(--ei-good); }
}
.ei-btn { padding: 7px 14px; border: 1px solid var(--ei-line); border-radius: 8px; background: transparent; color: inherit; cursor: pointer;
  &.primary { background: var(--ei-primary); border-color: var(--ei-primary); color: #fff; }
  &:disabled { opacity: .5; cursor: not-allowed; }
}
.ei-filter { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 12px;
  input, select { padding: 7px 10px; border: 1px solid var(--ei-line); border-radius: 8px; background: transparent; color: inherit; }
  input { flex: 1 1 260px; min-width: 0; }
}
.ei-empty { padding: 24px; text-align: center; color: var(--ei-muted); }
.ei-scroll { overflow-x: auto; }
.ei-table { width: 100%; border-collapse: collapse; font-size: .9rem;
  th, td { padding: 8px 10px; border-bottom: 1px solid var(--ei-line); text-align: start; white-space: nowrap; }
  td:nth-child(n+4) { white-space: normal; }
  tbody tr { cursor: pointer; }
  tbody tr.on, tbody tr:hover { background: rgba(31, 111, 139, .08); }
}
.ei-hash { font-size: .75rem; word-break: break-all; }
.ei-badge { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: .8rem; border: 1px solid var(--ei-line);
  &.bad { color: var(--ei-bad); border-color: var(--ei-bad); }
  &.good { color: var(--ei-good); border-color: var(--ei-good); }
  &.warn { color: var(--ei-warn); border-color: var(--ei-warn); }
  &.info { color: var(--ei-primary); border-color: var(--ei-primary); }
}
.ei-warn { color: var(--ei-warn); }
.ei-pager { display: flex; gap: 12px; align-items: center; justify-content: center; margin-top: 12px; }
.ei-tabs { display: flex; flex-wrap: wrap; gap: 6px; margin: 14px 0 10px;
  button { padding: 6px 12px; border: 1px solid var(--ei-line); border-radius: 8px; background: transparent; color: inherit; cursor: pointer;
    &.on { background: var(--ei-primary); border-color: var(--ei-primary); color: #fff; } }
}
.ei-dismiss { display: flex; flex-direction: column; gap: 8px; margin-top: 16px; max-width: 520px;
  textarea { padding: 8px; border: 1px solid var(--ei-line); border-radius: 8px; background: transparent; color: inherit; }
}
@media (max-width: 640px) { .ei-card { padding: 12px; } }
</style>
