<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import QPager from "@/components/quality/QPager.vue";
import { finalizeUpTo, getFiscalYears, getVouchers, importOpening, postAccountingEvents } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { badgeOfVoucher, money, usePagedList, voucherStatuses, voucherTypes } from "@/composables/fn";
import { cellNumber, cellText, readExcel, type ExcelField, type ExcelSheet } from "@/composables/fnExcel";
import { FnRoles, hasRole } from "@/composables/roles";

// Vouchers: list with filters; finalise up to a date (finance manager), post the accounting events as auto vouchers, import the opening balance.
const router = useRouter();
const years = ref<any[]>([]);
const filters = ref({ FiscalYearId: null as number | null, From: null as string | null, To: null as string | null, Status: null as number | null, Type: null as number | null, Contains: "" });
const list = usePagedList(getVouchers, filters);
const dialog = ref<"" | "finalize">("");
const result = ref<any>(null);
const opening = ref<{ sheet: ExcelSheet; description: string; fiscalYearId: number | null } | null>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);
const canFinalize = hasRole(FnRoles.managers);
const canJobs = hasRole(FnRoles.systemJobs);
const pageTotal = computed(() => list.rows.value.reduce((s, v) => s + (v.totalAmount || 0), 0));

const openingFields: ExcelField[] = [
  { key: "accountCode", label: "کد حساب *", words: ["کد حساب", "حساب", "کد معین", "account", "accountcode"] },
  { key: "detailCode", label: "کد تفصیلی", words: ["کد تفصیلی", "تفصیلی", "detail", "detailcode"] },
  { key: "debit", label: "بدهکار", words: ["بدهکار", "debit"] },
  { key: "credit", label: "بستانکار", words: ["بستانکار", "credit"] },
];

async function loadYears() {
  const [data] = await getFiscalYears();
  years.value = data || [];
}

async function onFile(event: any) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  try {
    const sheet = await readExcel(file, openingFields);
    if (sheet.map.accountCode < 0) return fail("ستون «کد حساب» در فایل پیدا نشد.");
    opening.value = { sheet, description: "سند افتتاحیه (ورود از سیستم قبلی)", fiscalYearId: filters.value.FiscalYearId };
  } catch (e: any) {
    fail("فایل خوانده نشد. فایل باید xlsx باشد. " + (e?.message || ""));
  }
}

async function runOpening() {
  const o = opening.value!;
  const rows = o.sheet.rows
    .map((r) => ({ accountCode: cellText(o.sheet, r, "accountCode"), detailCode: cellText(o.sheet, r, "detailCode"), debit: cellNumber(o.sheet, r, "debit") || 0, credit: cellNumber(o.sheet, r, "credit") || 0 }))
    .filter((r) => r.accountCode);
  if (!rows.length) return fail("ردیفی برای ورود نیست.");
  busy.value = true;
  const [data, error] = await importOpening({ fiscalYearId: o.fiscalYearId, description: o.description, rows });
  busy.value = false;
  if (error) return fail(error);
  ok("سند افتتاحیه به صورت پیش‌نویس ساخته شد.");
  opening.value = null;
  list.load();
  if (data?.id) router.push({ name: "FnVoucherEdit", params: { id: data.id } });
}

async function runFinalize(v: any) {
  dialog.value = "";
  busy.value = true;
  const [data, error] = await finalizeUpTo(v.upTo, filters.value.FiscalYearId);
  busy.value = false;
  if (error) return fail(error);
  result.value = { kind: "finalize", ...data };
  ok(`${data.finalized} سند قطعی شد.`);
  list.load();
}

async function runPostEvents() {
  busy.value = true;
  const [data, error] = await postAccountingEvents();
  busy.value = false;
  if (error) return fail(error);
  result.value = { kind: "events", ...data };
  ok("ثبت رویدادهای حسابداری انجام شد.");
  list.load();
}

onMounted(() => {
  loadYears();
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">اسناد حسابداری</h1>
        <div class="d-flex gap-2 flex-wrap">
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="router.push({ name: 'FnVoucherEdit', params: { id: 'new' } })">سند جدید</button>
          <button v-if="canJobs" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="runPostEvents">ثبت رویدادهای حسابداری</button>
          <button v-if="canFinalize" class="btn btn-sm btn-outline-secondary" @click="dialog = 'finalize'">قطعی‌کردن تا تاریخ</button>
          <label v-if="canWrite" class="btn btn-sm btn-outline-secondary mb-0">سند افتتاحیه از Excel<input accept=".xlsx,.xls" class="d-none" type="file" @change="onFile" /></label>
        </div>
      </div>

      <div class="inv-filters mb-3">
        <select v-model="filters.FiscalYearId" class="form-select form-select-sm"><option :value="null">سال مالی جاری</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select>
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in voucherStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Type" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in voucherTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شرح یا شماره مبدا" @keydown.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>

      <div v-if="result" class="alert alert-info py-2">
        <template v-if="result.kind === 'finalize'">
          {{ result.finalized }} سند قطعی شد<span v-if="result.firstNumber"> (شماره {{ result.firstNumber }} تا {{ result.lastNumber }})</span>.
          <ul v-if="result.skipped && result.skipped.length" class="mb-0 mt-1"><li v-for="(s, i) in result.skipped" :key="i">{{ s }}</li></ul>
        </template>
        <template v-else>
          ثبت‌شده: {{ result.posted }}، ثبت با تاخیر: {{ result.latePosted }}، بدون نیاز به سند: {{ result.notRequired }}، ناموفق: {{ result.failed }}.
          <ul v-if="result.errors && result.errors.length" class="mb-0 mt-1"><li v-for="(s, i) in result.errors" :key="i">{{ s }}</li></ul>
        </template>
        <button class="btn btn-sm btn-link" @click="result = null">بستن</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>شماره موقت</th><th>تاریخ</th><th>نوع</th><th>وضعیت</th><th>شرح</th><th>مبدا</th><th>مبلغ (ریال)</th><th>ایجادکننده</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">سندی نیست.</td></tr>
            <tr v-for="v in list.rows.value" :key="v.id" :class="{ 'fn-auto': v.type === 3 }" style="cursor: pointer" @click="router.push({ name: 'FnVoucherEdit', params: { id: v.id } })">
              <td class="fn-num">{{ v.number ?? "-" }}</td>
              <td class="fn-num">{{ v.temporaryNumber }}</td>
              <td>{{ jalali(v.date) }}</td>
              <td>
                <span :class="v.type === 3 ? 'warn' : v.type === 5 ? 'bad' : 'info'" class="inv-badge">{{ voucherTypes[v.type] }}</span>
                <span v-if="v.latePosting" class="inv-badge warn me-1">ثبت با تاخیر</span>
                <span v-if="v.isImported" class="inv-badge me-1">واردشده</span>
              </td>
              <td><span :class="badgeOfVoucher(v.status)" class="inv-badge">{{ voucherStatuses[v.status] }}</span></td>
              <td>{{ v.description }}</td>
              <td class="inv-ltr">{{ v.sourceNumber || v.sourceType || "" }}</td>
              <td class="fn-num">{{ money(v.totalAmount) }}</td>
              <td>{{ v.creatorName }}</td>
            </tr>
          </tbody>
          <tfoot v-if="list.rows.value.length"><tr class="fn-total"><td colspan="7">جمع این صفحه</td><td class="fn-num">{{ money(pageTotal) }}</td><td></td></tr></tfoot>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <PromptModal
      v-if="dialog === 'finalize'"
      :fields="[{ key: 'upTo', label: 'قطعی‌کردن اسناد کنترل‌شده تا تاریخ', type: 'date', required: true, value: today() }]"
      confirm-text="قطعی کن"
      hint="اسناد کنترل‌شده تا این تاریخ به ترتیب تاریخ شماره قطعی می‌گیرند. سندی که ایجادکننده‌اش خود شما باشید قطعی نمی‌شود."
      title="قطعی‌کردن اسناد"
      @cancel="dialog = ''"
      @confirm="runFinalize" />

    <div v-if="opening" class="inv-modal-backdrop screen-only" @click.self="opening = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">ورود سند افتتاحیه از Excel</h2>
        <p class="inv-muted">فایل: {{ opening.sheet.fileName }} - {{ opening.sheet.rows.length }} ردیف. ستون‌ها: کد حساب، کد تفصیلی، بدهکار، بستانکار.</p>
        <div class="row g-2 mb-2">
          <div class="col-md-6"><label class="form-label small mb-0">سال مالی</label><select v-model="opening.fiscalYearId" class="form-select form-select-sm"><option :value="null">سال مالی جاری</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">شرح سند</label><input v-model="opening.description" class="form-control form-control-sm" /></div>
        </div>
        <div class="table-responsive" style="max-height: 280px">
          <table class="table table-sm">
            <thead><tr><th>کد حساب</th><th>کد تفصیلی</th><th>بدهکار</th><th>بستانکار</th></tr></thead>
            <tbody>
              <tr v-for="(r, i) in opening.sheet.rows.slice(0, 30)" :key="i">
                <td class="inv-ltr">{{ cellText(opening.sheet, r, "accountCode") }}</td>
                <td class="inv-ltr">{{ cellText(opening.sheet, r, "detailCode") }}</td>
                <td class="fn-num">{{ money(cellNumber(opening.sheet, r, "debit")) }}</td>
                <td class="fn-num">{{ money(cellNumber(opening.sheet, r, "credit")) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="opening = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="runOpening">ساخت سند افتتاحیه</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
