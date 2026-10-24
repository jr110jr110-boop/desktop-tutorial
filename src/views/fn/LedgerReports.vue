<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import ReportTables from "@/components/fn/ReportTables.vue";
import ReportPrint from "@/components/fn/print/ReportPrint.vue";
import DrillModal from "@/components/fn/DrillModal.vue";
import { exportLedgerReport, getAccounts, getDetailAccounts, getFiscalYears, getLedgerReconciliation, getLedgerReport, type LedgerReport } from "@/api/FnAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { printSheet } from "@/composables/fn";

// One page for the ledger reports; the menu opens it with the kind of the report (journal, general ledger, account review,
// trial balance, balance sheet, income statement, project profitability, reconciliation check).
const props = defineProps<{ kind: string }>();

const titles: Record<string, string> = {
  journal: "دفتر روزنامه",
  "general-ledger": "دفتر کل",
  "account-review": "مرور حساب (کل، معین، تفصیلی)",
  "trial-balance": "تراز آزمایشی",
  "balance-sheet": "ترازنامه",
  "income-statement": "صورت سود و زیان",
  "project-profitability": "سودآوری پروژه‌ها",
  reconciliation: "کنترل مغایرت دفاتر با زیرحساب‌ها",
};
const years = ref<any[]>([]);
const filters = ref<any>({ FiscalYearId: null, From: null, To: null, IncludeDraft: false, Level: 1, Columns: 4, ExcludeClosing: false, AccountId: null, DetailId: null, OldVoucherDays: 30 });
const account = ref<any>(null);
const detail = ref<any>(null);
const data = ref<any>(null);
const printData = ref<any>(null);
const page = ref(1);
const total = ref(0);
const loading = ref(false);
const printing = ref(false);
const drill = ref<string | null>(null);
const pageSize = 100;

const paged = computed(() => ["journal", "general-ledger", "account-review", "project-profitability"].includes(props.kind));
const needsAccount = computed(() => props.kind === "general-ledger" || props.kind === "account-review");
const accountLabel = (a: any) => `${a.code} - ${a.title}`;
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchAccounts = async (t: string) => rowsOf((await getAccounts({ Contains: t, PageSize: 15 }))[0]);
const searchDetails = async (t: string) => rowsOf((await getDetailAccounts({ Contains: t, PageSize: 15 }))[0]);

function params(all: boolean) {
  const f = filters.value;
  const p: any = { FiscalYearId: f.FiscalYearId, From: f.From, To: f.To, IncludeDraft: f.IncludeDraft };
  if (props.kind === "trial-balance") Object.assign(p, { Level: f.Level, Columns: f.Columns, ExcludeClosing: f.ExcludeClosing });
  if (needsAccount.value) Object.assign(p, { AccountId: account.value?.id || null, DetailId: props.kind === "account-review" ? detail.value?.id || null : null });
  if (paged.value) Object.assign(p, all ? { PageSize: 0, PageNumber: 1 } : { PageSize: pageSize, PageNumber: page.value });
  return p;
}

/** The part of the answer the tables show: journal and profitability are lists, the others one object. */
function shape(raw: any) {
  if (props.kind === "journal" || props.kind === "project-profitability") return rowsOf(raw);
  return raw;
}

async function fetchReport(all: boolean) {
  if (props.kind === "reconciliation") return getLedgerReconciliation(Number(filters.value.OldVoucherDays) || 30);
  return getLedgerReport(props.kind as LedgerReport, params(all));
}

async function load() {
  if (needsAccount.value && !account.value && !detail.value) {
    data.value = null;
    return;
  }
  loading.value = true;
  const [raw, error] = await fetchReport(false);
  loading.value = false;
  if (error) {
    data.value = null;
    return fail(error);
  }
  data.value = shape(raw);
  if (props.kind === "general-ledger" || props.kind === "account-review") total.value = raw?.totalRows ?? 0;
  else if (paged.value) total.value = raw?.totalItems ?? (data.value?.length || 0);
}

function search() {
  page.value = 1;
  load();
}
function go(delta: number) {
  page.value += delta;
  load();
}

async function exportCsv() {
  const message = await exportLedgerReport(props.kind, { ...params(true), OldVoucherDays: filters.value.OldVoucherDays });
  if (message) fail(message);
  else ok("فایل CSV ذخیره شد.");
}

async function print() {
  if (!data.value) return fail("ابتدا گزارش را بگیرید.");
  if (paged.value) {
    const [raw, error] = await fetchReport(true);
    if (error) return fail(error);
    printData.value = shape(raw);
  } else printData.value = data.value;
  printing.value = true;
  await nextTick();
  printSheet();
}

const yearTitle = computed(() => years.value.find((y) => y.id === filters.value.FiscalYearId)?.title || "سال مالی جاری");
const printFields = computed(() => {
  const f = filters.value;
  const out: { label: string; value: any }[] = [
    { label: "سال مالی", value: yearTitle.value },
    { label: "از تاریخ", value: f.From ? jalali(f.From) : "ابتدای سال" },
    { label: "تا تاریخ", value: f.To ? jalali(f.To) : "پایان سال" },
    { label: "اسناد", value: f.IncludeDraft ? "قطعی و غیرقطعی" : "فقط قطعی" },
  ];
  if (props.kind === "trial-balance") out.push({ label: "سطح", value: ["", "کل", "معین", "تفصیلی"][f.Level] }, { label: "ستون‌ها", value: f.Columns });
  if (account.value) out.push({ label: "حساب", value: accountLabel(account.value) });
  if (detail.value) out.push({ label: "تفصیلی", value: accountLabel(detail.value) });
  return out;
});

watch(
  () => props.kind,
  () => {
    data.value = null;
    page.value = 1;
    total.value = 0;
    printing.value = false;
    if (!needsAccount.value) load();
  }
);

onMounted(async () => {
  const [y] = await getFiscalYears();
  years.value = y || [];
  if (!needsAccount.value) load();
});
</script>

<template>
  <div class="inv-page">
    <ReportPrint v-if="printing && printData" :columns="Number(filters.Columns)" :data="printData" :fields="printFields" :kind="kind" :title="titles[kind]" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">{{ titles[kind] }}</h1>
        <div class="d-flex gap-2">
          <button :disabled="!data" class="btn btn-sm btn-outline-secondary" @click="print">چاپ</button>
          <button :disabled="!data" class="btn btn-sm btn-outline-secondary" @click="exportCsv">خروجی CSV</button>
        </div>
      </div>

      <div class="inv-filters mb-3">
        <template v-if="kind !== 'reconciliation'">
          <select v-model="filters.FiscalYearId" class="form-select form-select-sm"><option :value="null">سال مالی جاری</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select>
          <div v-if="kind !== 'balance-sheet'"><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
          <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
          <label class="form-check small"><input v-model="filters.IncludeDraft" class="form-check-input" type="checkbox" /> با اسناد غیرقطعی</label>
        </template>
        <template v-if="kind === 'trial-balance'">
          <select v-model="filters.Level" class="form-select form-select-sm"><option :value="1">سطح کل</option><option :value="2">سطح معین</option><option :value="3">سطح تفصیلی</option></select>
          <select v-model="filters.Columns" class="form-select form-select-sm"><option :value="2">۲ ستونی</option><option :value="4">۴ ستونی</option><option :value="6">۶ ستونی</option></select>
          <label class="form-check small"><input v-model="filters.ExcludeClosing" class="form-check-input" type="checkbox" /> بدون اسناد اختتامیه</label>
        </template>
        <template v-if="needsAccount">
          <div style="min-width: 260px"><label class="form-label small mb-0">حساب</label><LookupBox v-model="account" :fetch="searchAccounts" :label="accountLabel" placeholder="کد یا نام حساب" /></div>
          <div v-if="kind === 'account-review'" style="min-width: 260px"><label class="form-label small mb-0">تفصیلی</label><LookupBox v-model="detail" :fetch="searchDetails" :label="accountLabel" placeholder="تفصیلی" /></div>
        </template>
        <div v-if="kind === 'reconciliation'"><label class="form-label small mb-0">اسناد قطعی‌نشده قدیمی‌تر از (روز)</label><input v-model.number="filters.OldVoucherDays" class="form-control form-control-sm" type="number" min="1" /></div>
        <button :disabled="loading" class="btn btn-sm btn-primary" @click="search">{{ loading ? "..." : "نمایش گزارش" }}</button>
      </div>

      <p v-if="needsAccount && !data" class="inv-muted">حساب{{ kind === "account-review" ? " یا تفصیلی" : "" }} را انتخاب و «نمایش گزارش» را بزنید.</p>
      <div class="table-responsive"><ReportTables :columns="Number(filters.Columns)" :data="data" :kind="kind" @drill="drill = $event" /></div>
      <DrillModal v-if="drill" :account="drill" :params="{ FiscalYearId: filters.FiscalYearId, From: filters.From, To: filters.To, IncludeDraft: filters.IncludeDraft }" @close="drill = null" />
      <QPager v-if="paged && data" :page="page" :page-size="pageSize" :total="total" @go="go" />
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
