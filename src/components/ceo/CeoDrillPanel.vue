<script setup lang="ts">
// The detail table behind a KPI or a chart (GET ceo/drill) in a slide-over from the left edge. Columns are formatted by kind (rial with separators, percent, Shamsi date,
// status with its shape, link to the record), sorted on the page by a click on the heading, and paged by the API (50 rows). The other tables of the same section are one click away.
// «پرسش از دستیار درباره این جدول» opens the assistant with a question about this table already written (nothing is sent until the CEO sends it).
// params: the filter of an assistant's openDrill action (voice mode, §7.1), sent to ceo/drill with the key and named above the table, with «نمایش همه» to drop it.
import { computed, nextTick, ref, watch } from "vue";
import { useRouter } from "vue-router";
import { getCeoDrill, type CeoColumnKind, type CeoDrillTable } from "@/api/CeoAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";
import { askAboutTable, faDigits, faNum, latinNumber, openableLink, PROTECTED_TEXT, rialFull, rialShort, statusOfCell } from "@/composables/ceo";

const props = defineProps({
  open: { type: Boolean, default: false },
  drillKey: { type: String, default: "" },
  /** the drill keys of the same section, with their titles: [{ key, title }] */
  related: { type: Array as () => { key: string; title: string }[], default: () => [] },
  /** px kept free at the left edge (the assistant drawer when it is open beside the page) */
  offset: { type: Number, default: 0 },
  /** the drill parameters of an assistant's action (null = none) */
  params: { type: Object as () => Record<string, any> | null, default: null },
  /** those parameters in words */
  filterText: { type: String, default: "" },
});
const emit = defineEmits<{ (e: "close"): void; (e: "ask", question: string): void; (e: "change", key: string): void; (e: "clear-params"): void }>();

const router = useRouter();
const PAGE = 50;
const table = ref<CeoDrillTable | null>(null);
const loading = ref(false);
const error = ref("");
const page = ref(1);
const sortKey = ref<string | null>(null);
const sortDir = ref<1 | -1>(1);
const panel = ref<HTMLElement | null>(null);
const closeBtn = ref<HTMLButtonElement | null>(null);
let seq = 0;
let opener: HTMLElement | null = null;

async function load() {
  if (!props.drillKey) return;
  const mine = ++seq;
  loading.value = true;
  error.value = "";
  const [data, err] = await getCeoDrill(props.drillKey, { ...(props.params || {}), top: PAGE, page: page.value });
  if (mine !== seq) return; // an older answer arrived after a newer request
  loading.value = false;
  if (err) {
    error.value = err.message || "جدول خوانده نشد.";
    return;
  }
  table.value = data;
}

watch(
  () => [props.open, props.drillKey, JSON.stringify(props.params || {})],
  async ([open], before) => {
    if (!open) return;
    if (!before?.[0]) opener = document.activeElement as HTMLElement | null;
    page.value = 1;
    sortKey.value = null;
    table.value = null;
    await load();
    await nextTick();
    closeBtn.value?.focus();
  }
);
function go(delta: number) {
  page.value += delta;
  sortKey.value = null;
  load();
}
function close() {
  emit("close");
  nextTick(() => opener?.focus?.());
}

// ---------------------------------------------------------------- sorting (on the rows of this page)
const columns = computed(() => table.value?.columns || []);
const sortable = (kind: CeoColumnKind) => kind !== "link";
function sortBy(key: string) {
  if (sortKey.value === key) sortDir.value = sortDir.value === 1 ? -1 : 1;
  else {
    sortKey.value = key;
    const kind = columns.value.find((c) => c.key === key)?.kind;
    sortDir.value = kind === "rial" || kind === "number" || kind === "percent" ? -1 : 1;
  }
}
const statusRank: Record<string, number> = { danger: 0, warn: 1, info: 2, neutral: 3, ok: 4 };
function sortValue(kind: CeoColumnKind, v: any): number | string {
  if (v === null || v === undefined || v === "") return kind === "text" || kind === "date" ? "" : Number.NEGATIVE_INFINITY;
  if (kind === "rial" || kind === "number" || kind === "percent") return Number(typeof v === "string" ? latinNumber(v) : v);
  if (kind === "date") return latinNumber(String(v));
  if (kind === "status") return statusRank[statusOfCell(v).status] ?? 9;
  return String(v);
}
const rows = computed(() => {
  const list = [...(table.value?.rows || [])];
  if (!sortKey.value) return list;
  const kind = columns.value.find((c) => c.key === sortKey.value)?.kind || "text";
  const key = sortKey.value;
  return list.sort((a, b) => {
    const x = sortValue(kind, a[key]);
    const y = sortValue(kind, b[key]);
    if (typeof x === "number" && typeof y === "number") return (x - y) * sortDir.value;
    return String(x).localeCompare(String(y), "fa") * sortDir.value;
  });
});
const ariaSort = (key: string) => (sortKey.value === key ? (sortDir.value === 1 ? "ascending" : "descending") : "none");

// ---------------------------------------------------------------- cells
const isNumeric = (kind: CeoColumnKind) => kind === "rial" || kind === "number" || kind === "percent";
function cellText(kind: CeoColumnKind, v: any): string {
  if (v === null || v === undefined || v === "") return "—";
  switch (kind) {
    case "rial":
      return faNum(Math.round(Number(v)), 0);
    case "number":
      return faNum(Number(v), 2);
    case "percent":
      return faNum(Number(v), 1) + "٪";
    case "date":
      return faDigits(String(v));
    default:
      return String(v);
  }
}
const pages = computed(() => Math.max(1, Math.ceil((table.value?.totalCount || 0) / (table.value?.pageSize || PAGE))));
const moduleLink = computed(() => openableLink(router, table.value?.link));
const rialColumn = computed(() => columns.value.some((c) => c.kind === "rial"));

// ---------------------------------------------------------------- keyboard: Esc closes, Tab stays inside the panel
function onKey(e: KeyboardEvent) {
  if (e.key === "Escape") {
    e.preventDefault();
    close();
    return;
  }
  if (e.key !== "Tab" || !panel.value) return;
  const focusables = Array.from(panel.value.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])')).filter((el) => el.offsetParent !== null);
  if (!focusables.length) return;
  const first = focusables[0];
  const last = focusables[focusables.length - 1];
  if (e.shiftKey && document.activeElement === first) {
    e.preventDefault();
    last.focus();
  } else if (!e.shiftKey && document.activeElement === last) {
    e.preventDefault();
    first.focus();
  }
}
</script>

<template>
  <div class="drill-root">
    <transition name="ceo-scrim">
      <div v-if="open" class="drill-scrim" @click="close" />
    </transition>
    <transition name="ceo-slide">
      <aside v-if="open" ref="panel" :style="{ left: offset + 'px', width: `min(940px, calc(100vw - ${offset}px))` }" aria-labelledby="ceo-drill-title" aria-modal="true" class="drill-panel" dir="rtl" role="dialog" @keydown="onKey">
        <header class="drill-head">
          <div class="drill-titles">
            <h2 id="ceo-drill-title" class="drill-title">{{ table?.title || related.find((r) => r.key === drillKey)?.title || "جزئیات" }}</h2>
            <p v-if="table" class="drill-sub">
              <span>{{ faNum(table.totalCount, 0) }} ردیف</span>
              <span v-if="table.asOfText">وضعیت تا {{ faDigits(table.asOfText) }}</span>
              <span v-if="rialColumn">مبالغ به ریال</span>
            </p>
          </div>
          <button ref="closeBtn" aria-label="بستن جزئیات" class="ceo-icon-btn" title="بستن (Esc)" type="button" @click="close"><CeoIcon name="close" /></button>
        </header>

        <div class="drill-actions">
          <button :disabled="!table || table.hidden" class="ceo-btn" type="button" @click="table && emit('ask', askAboutTable(table.title))">
            <CeoIcon :size="16" name="assistant" />
            پرسش از دستیار درباره‌ی این جدول
          </button>
          <a v-if="moduleLink" :href="router.resolve(moduleLink).href" class="ceo-btn ghost" @click.prevent="router.push(moduleLink); close()">
            <CeoIcon :size="16" name="open" />
            باز کردن صفحه‌ی ماژول
          </a>
        </div>

        <nav v-if="related.length > 1" aria-label="جدول‌های دیگر این بخش" class="drill-related">
          <button v-for="r in related" :key="r.key" :aria-current="r.key === drillKey ? 'true' : undefined" :class="{ active: r.key === drillKey }" class="ceo-chip" type="button" @click="r.key !== drillKey && emit('change', r.key)">{{ r.title }}</button>
        </nav>

        <p v-if="params && filterText" class="drill-filter">
          <span>فیلتر دستیار: {{ filterText }}</span>
          <button class="ceo-chip" type="button" @click="emit('clear-params')">نمایش همه</button>
        </p>

        <p v-if="table?.note" class="drill-note">{{ table.note }}</p>

        <div class="drill-body">
          <div v-if="loading && !table" aria-label="در حال بارگذاری" class="drill-skeleton">
            <span v-for="i in 8" :key="i" class="ceo-skel" :style="{ width: 100 - (i % 3) * 12 + '%' }" />
          </div>
          <div v-else-if="error" class="drill-state error">
            <p>{{ error }}</p>
            <button class="ceo-btn ghost" type="button" @click="load">تلاش دوباره</button>
          </div>
          <div v-else-if="table && table.hidden" class="drill-state">
            <CeoIcon :size="22" name="lock" />
            <strong>{{ PROTECTED_TEXT }}</strong>
            <p>این جدول مبالغ دستمزد دارد و برای نقش شما نمایش داده نمی‌شود.</p>
          </div>
          <div v-else-if="table && !table.rows.length" class="drill-state">
            <p>ردیفی برای نمایش نیست؛ هیچ رکوردی در این جدول با شرایط امروز جور نیست.</p>
          </div>
          <div v-else-if="table" :class="{ refreshing: loading }" class="drill-table-wrap">
            <table class="drill-table">
              <thead>
                <tr>
                  <th v-for="c in columns" :key="c.key" :aria-sort="sortable(c.kind) ? ariaSort(c.key) : undefined" :class="{ num: isNumeric(c.kind) }" scope="col">
                    <button v-if="sortable(c.kind)" class="sort-btn" type="button" @click="sortBy(c.key)">
                      {{ c.title }}
                      <CeoIcon :name="sortKey === c.key ? (sortDir === 1 ? 'sort-asc' : 'sort-desc') : 'sort'" :size="14" class="sort-icon" />
                    </button>
                    <span v-else>{{ c.title }}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(r, i) in rows" :key="i">
                  <td v-for="c in columns" :key="c.key" :class="{ num: isNumeric(c.kind) }" :title="c.kind === 'rial' && r[c.key] !== null && r[c.key] !== undefined ? rialShort(r[c.key]) + ' — ' + rialFull(r[c.key]) : undefined">
                    <template v-if="c.kind === 'status'">
                      <span :class="'st-' + statusOfCell(r[c.key]).status" class="cell-status"><CeoIcon :name="statusOfCell(r[c.key]).status === 'info' ? 'info' : statusOfCell(r[c.key]).status" :size="15" />{{ statusOfCell(r[c.key]).text }}</span>
                    </template>
                    <template v-else-if="c.kind === 'link'">
                      <a v-if="openableLink(router, r[c.key])" :href="router.resolve(openableLink(router, r[c.key]) || '/').href" class="cell-link" @click.prevent="router.push(openableLink(router, r[c.key]) || '/'); close()">
                        باز کردن <CeoIcon :size="14" name="open" />
                      </a>
                      <span v-else class="cell-muted">—</span>
                    </template>
                    <template v-else>{{ cellText(c.kind, r[c.key]) }}</template>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <footer v-if="table && !table.hidden && pages > 1" class="drill-pager">
          <button :disabled="page <= 1 || loading" class="ceo-btn ghost" type="button" @click="go(-1)">صفحه‌ی قبل</button>
          <span>صفحه‌ی {{ faNum(page, 0) }} از {{ faNum(pages, 0) }}</span>
          <button :disabled="page >= pages || loading" class="ceo-btn ghost" type="button" @click="go(1)">صفحه‌ی بعد</button>
        </footer>
      </aside>
    </transition>
  </div>
</template>

<style scoped>
.drill-scrim {
  position: fixed;
  inset: 0;
  z-index: 1044;
  background: var(--ceo-scrim);
}
.drill-panel {
  position: fixed;
  top: 0;
  bottom: 0;
  z-index: 1045;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 20px 24px 16px;
  background: var(--ceo-card);
  color: var(--ceo-ink);
  border-right: 1px solid var(--ceo-line);
  box-shadow: 18px 0 48px rgba(10, 18, 26, 0.18);
  text-align: right;
  font-family: IRANYekan, Tahoma, sans-serif;
}
.drill-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}
.drill-title {
  margin: 0;
  font-size: 20px;
  line-height: 30px;
  font-weight: 600;
  color: var(--ceo-ink);
}
.drill-sub {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  margin: 2px 0 0;
  font-size: 12px;
  line-height: 18px;
  color: var(--ceo-muted);
}
.drill-actions,
.drill-related {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.drill-filter {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 10px;
  margin: 0;
  padding: 6px 10px;
  border-right: 3px solid var(--ceo-primary);
  border-radius: 4px;
  background: var(--ceo-primary-soft);
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-ink);
}
.drill-note {
  margin: 0;
  font-size: 13px;
  line-height: 20px;
  color: var(--ceo-muted);
  max-width: 80ch;
}
.drill-body {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.drill-table-wrap {
  flex: 0 1 auto;
  min-height: 0;
  overflow: auto;
  border: 1px solid var(--ceo-line);
  border-radius: 6px;
}
.drill-table-wrap.refreshing {
  opacity: 0.55;
}
.drill-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
  line-height: 20px;
}
.drill-table th,
.drill-table td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--ceo-line);
  text-align: right;
  vertical-align: middle;
  color: var(--ceo-ink);
}
.drill-table thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--ceo-surface);
  color: var(--ceo-muted);
  font-weight: 500;
  white-space: nowrap;
}
.drill-table .num {
  text-align: left;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.drill-table tbody tr:hover td {
  background: var(--ceo-card-hover);
}
.sort-btn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 0;
  border: 0;
  background: none;
  color: inherit;
  font: inherit;
  cursor: pointer;
}
.sort-icon {
  opacity: 0.55;
}
th[aria-sort="ascending"] .sort-icon,
th[aria-sort="descending"] .sort-icon {
  opacity: 1;
  color: var(--ceo-primary);
}
.cell-status {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  white-space: nowrap;
}
.cell-status.st-ok :deep(.ceo-icon) {
  color: var(--ceo-ok);
}
.cell-status.st-warn :deep(.ceo-icon) {
  color: var(--ceo-warn);
}
.cell-status.st-danger :deep(.ceo-icon) {
  color: var(--ceo-danger);
}
.cell-status.st-info :deep(.ceo-icon) {
  color: var(--ceo-info);
}
.cell-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--ceo-primary);
  text-decoration: none;
  white-space: nowrap;
}
.cell-link:hover {
  text-decoration: underline;
}
.cell-muted {
  color: var(--ceo-muted);
}
.drill-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 40px 16px;
  text-align: center;
  color: var(--ceo-muted);
  border: 1px dashed var(--ceo-line);
  border-radius: 6px;
}
.drill-state strong {
  color: var(--ceo-ink);
  font-weight: 500;
}
.drill-state p {
  margin: 0;
}
.drill-state.error p {
  color: var(--ceo-danger-ink);
}
.drill-skeleton {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 8px 0;
}
.drill-skeleton .ceo-skel {
  height: 18px;
}
.drill-pager {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  font-size: 13px;
  color: var(--ceo-muted);
}
@media (max-width: 640px) {
  .drill-panel {
    padding: 16px;
  }
}
</style>
