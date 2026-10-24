<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import ReportTables from "@/components/fn/ReportTables.vue";
import ReportPrint from "@/components/fn/print/ReportPrint.vue";
import DrillModal from "@/components/fn/DrillModal.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import { getCashFlow, getEquityChanges, getFiscalYears, getLedgerReport, getRatios, getStatementViews, saveStatementView } from "@/api/FnAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { printSheet } from "@/composables/fn";

// Financial statements beyond the balance sheet and the income statement: cash flow (indirect), changes in equity, ratios, and the
// comparative statement of two fiscal years (the server has no comparative endpoint: the two statements are fetched and lined up here).
const titles: Record<string, string> = { "cash-flow": "صورت جریان وجوه نقد", equity: "صورت تغییرات حقوق صاحبان سرمایه", ratios: "نسبت‌های مالی", comparative: "صورت‌های مالی تطبیقی" };
const tab = ref("cash-flow");
const tabs = Object.entries(titles).map(([key, title]) => ({ key, title }));
const years = ref<any[]>([]);
const filters = ref<any>({ FiscalYearId: null, From: null, To: null, IncludeDraft: false });
const cmp = ref<any>({ statement: "balance-sheet", yearA: null as number | null, yearB: null as number | null });
const data = ref<any>(null);
const loading = ref(false);
const printing = ref(false);
const drill = ref<string | null>(null);
const views = ref<any[]>([]);
const saving = ref(false);

const yearTitle = (id: number | null) => years.value.find((y) => y.id === id)?.title || "سال مالی جاری";
const params = () => ({ FiscalYearId: filters.value.FiscalYearId, From: filters.value.From, To: filters.value.To, IncludeDraft: filters.value.IncludeDraft });

function lineRows(report: any) {
  return (report?.lines || []).map((l: any) => ({ code: l.code, title: l.title, section: l.section || "", isTotal: !!l.isTotal, amount: Number(l.amount) || 0 }));
}
/** Lines of the two statements matched by section, code and title; a line missing in one year counts as zero. */
function joinStatements(a: any, b: any, labelA: string, labelB: string) {
  const rowsA = lineRows(a), rowsB = lineRows(b);
  const sections: { name: string; rows: any[] }[] = [];
  const put = (l: any, side: "a" | "b") => {
    let s = sections.find((x) => x.name === l.section);
    if (!s) sections.push((s = { name: l.section, rows: [] }));
    let r = s.rows.find((x) => x.code === l.code && x.title === l.title);
    if (!r) s.rows.push((r = { code: l.code, title: l.title, isTotal: l.isTotal, a: 0, b: 0 }));
    r[side] = l.amount;
  };
  rowsA.forEach((l: any) => put(l, "a"));
  rowsB.forEach((l: any) => put(l, "b"));
  return { labelA, labelB, sections };
}

async function load() {
  loading.value = true;
  data.value = null;
  if (tab.value === "comparative") {
    if (!cmp.value.yearA || !cmp.value.yearB || cmp.value.yearA === cmp.value.yearB) {
      loading.value = false;
      return fail("دو سال مالی متفاوت را انتخاب کنید.");
    }
    const [[a, ea], [b, eb]] = await Promise.all([
      getLedgerReport(cmp.value.statement, { FiscalYearId: cmp.value.yearA, IncludeDraft: filters.value.IncludeDraft }),
      getLedgerReport(cmp.value.statement, { FiscalYearId: cmp.value.yearB, IncludeDraft: filters.value.IncludeDraft }),
    ]);
    loading.value = false;
    if (ea || eb) return fail(ea || eb);
    data.value = joinStatements(a, b, yearTitle(cmp.value.yearA), yearTitle(cmp.value.yearB));
    return;
  }
  const fn = tab.value === "cash-flow" ? getCashFlow : tab.value === "equity" ? getEquityChanges : getRatios;
  const [d, error] = await fn(params());
  loading.value = false;
  if (error) return fail(error);
  data.value = d;
}

function switchTab() {
  data.value = null;
  load();
}

const printFields = computed(() => {
  if (tab.value === "comparative") return [{ label: "صورت", value: cmp.value.statement === "balance-sheet" ? "ترازنامه" : "صورت سود و زیان" }, { label: "سال اول", value: yearTitle(cmp.value.yearA) }, { label: "سال دوم", value: yearTitle(cmp.value.yearB) }, { label: "اسناد", value: filters.value.IncludeDraft ? "قطعی و غیرقطعی" : "فقط قطعی" }];
  const f = filters.value;
  return [
    { label: "سال مالی", value: yearTitle(f.FiscalYearId) },
    { label: "از تاریخ", value: f.From ? jalali(f.From) : data.value?.from ? jalali(data.value.from) : "ابتدای سال" },
    { label: "تا تاریخ", value: f.To ? jalali(f.To) : data.value?.to ? jalali(data.value.to) : "پایان سال" },
    { label: "اسناد", value: f.IncludeDraft ? "قطعی و غیرقطعی" : "فقط قطعی" },
  ];
});
async function print() {
  printing.value = true;
  await nextTick();
  printSheet();
}

async function loadViews() {
  const [d] = await getStatementViews();
  views.value = (d || []).filter((v: any) => v.reportKey === "statements");
}
function pickView(i: string) {
  if (i !== "") applyView(views.value[Number(i)]);
}
function applyView(v: any) {
  try {
    const j = JSON.parse(v.filtersJson || "{}");
    tab.value = titles[j.tab] ? j.tab : "cash-flow";
    filters.value = { FiscalYearId: j.FiscalYearId ?? null, From: j.From ?? null, To: j.To ?? null, IncludeDraft: !!j.IncludeDraft };
    if (j.cmp) cmp.value = j.cmp;
    load();
  } catch {
    fail("نمای ذخیره‌شده قابل خواندن نیست.");
  }
}
async function confirmSave(v: any) {
  saving.value = false;
  const [, error] = await saveStatementView({ reportKey: "statements", title: v.title, filtersJson: JSON.stringify({ tab: tab.value, ...filters.value, cmp: cmp.value }), columnsJson: null, shared: v.shared === "1" });
  if (error) return fail(error);
  ok("نما ذخیره شد.");
  loadViews();
}

onMounted(async () => {
  const [y] = await getFiscalYears();
  years.value = y || [];
  loadViews();
  load();
});
</script>

<template>
  <div class="inv-page">
    <ReportPrint v-if="printing && data" :data="data" :fields="printFields" :kind="tab" :title="titles[tab]" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">صورت‌های مالی</h1>
        <div class="d-flex gap-2">
          <select class="form-select form-select-sm" @change="pickView(($event.target as HTMLSelectElement).value)"><option value="">نماهای ذخیره‌شده...</option><option v-for="(v, i) in views" :key="v.id" :value="i">{{ v.title }}</option></select>
          <button class="btn btn-sm btn-outline-secondary" @click="saving = true">ذخیره نما</button>
          <button :disabled="!data" class="btn btn-sm btn-outline-secondary" @click="print">چاپ</button>
        </div>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" @update:model-value="switchTab" />

      <div class="inv-filters mb-3">
        <template v-if="tab !== 'comparative'">
          <select v-model="filters.FiscalYearId" class="form-select form-select-sm"><option :value="null">سال مالی جاری</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select>
          <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
          <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
        </template>
        <template v-else>
          <select v-model="cmp.statement" class="form-select form-select-sm"><option value="balance-sheet">ترازنامه</option><option value="income-statement">صورت سود و زیان</option></select>
          <select v-model="cmp.yearA" class="form-select form-select-sm"><option :value="null">سال اول...</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select>
          <select v-model="cmp.yearB" class="form-select form-select-sm"><option :value="null">سال دوم...</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select>
        </template>
        <label class="form-check small"><input v-model="filters.IncludeDraft" class="form-check-input" type="checkbox" /> با اسناد غیرقطعی</label>
        <button :disabled="loading" class="btn btn-sm btn-primary" @click="load">{{ loading ? "..." : "نمایش گزارش" }}</button>
      </div>

      <div class="table-responsive"><ReportTables :data="data" :kind="tab" @drill="drill = $event" /></div>
    </div>

    <DrillModal v-if="drill" :account="drill" :params="params()" @close="drill = null" />
    <PromptModal v-if="saving" title="ذخیره نمای گزارش" :fields="[{ key: 'title', label: 'عنوان', required: true }, { key: 'shared', label: 'اشتراک با دیگران', type: 'select', options: [{ value: '0', text: 'خیر' }, { value: '1', text: 'بله' }], value: '0' }]" @confirm="confirmSave" @cancel="saving = false" />
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
