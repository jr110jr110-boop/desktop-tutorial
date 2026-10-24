<script setup lang="ts">
// The tables of the ledger reports (screen and print use the same component): journal, general ledger, account review,
// trial balance (2 / 4 / 6 columns), balance sheet, income statement, project profitability, reconciliation check.
import { computed } from "vue";
import { jalali } from "@/composables/inventory";
import { money, moneyOrDash } from "@/composables/fn";

const props = defineProps<{ kind: string; data: any; columns?: number }>();
/** Clicking an account code of a statement asks the page to show the ledger entries behind it. */
const emit = defineEmits<{ (e: "drill", code: string): void }>();
const sectionNames: Record<string, string> = { Operating: "فعالیت‌های عملیاتی", Investing: "فعالیت‌های سرمایه‌گذاری", Financing: "فعالیت‌های تامین مالی" };
const pct = (v: any) => (v === null || v === undefined ? "-" : Number(v).toFixed(2));

/** Statement lines grouped by their section (the API orders them). */
const sections = computed(() => {
  const out: { name: string; lines: any[] }[] = [];
  for (const l of props.data?.lines || []) {
    const name = l.section || "";
    let s = out.find((x) => x.name === name);
    if (!s) {
      s = { name, lines: [] };
      out.push(s);
    }
    s.lines.push(l);
  }
  return out;
});
const cols = computed(() => props.columns || 4);
</script>

<template>
  <div v-if="data">
    <!-- journal -->
    <table v-if="kind === 'journal'" class="table table-sm">
      <thead><tr><th>تاریخ</th><th>کد</th><th>شرح (حساب کل / سند)</th><th>بدهکار (ریال)</th><th>بستانکار (ریال)</th></tr></thead>
      <tbody>
        <tr v-if="!data.length"><td colspan="5" class="text-center inv-muted">گردشی در این بازه نیست.</td></tr>
        <template v-for="d in data" :key="d.date">
          <tr v-for="(a, i) in d.accounts" :key="d.date + a.code">
            <td>{{ i === 0 ? jalali(d.date) : "" }}</td>
            <td class="inv-ltr">{{ a.code }}</td>
            <td>{{ a.title }}</td>
            <td class="fn-num">{{ moneyOrDash(a.debit) }}</td>
            <td class="fn-num">{{ moneyOrDash(a.credit) }}</td>
          </tr>
          <tr v-for="s in d.vouchers" :key="'v' + s.voucherId">
            <td></td>
            <td class="inv-muted">سند {{ s.number ?? "موقت" }}</td>
            <td class="inv-muted">{{ s.description }}</td>
            <td class="inv-muted fn-num" colspan="2">{{ money(s.amount) }}</td>
          </tr>
          <tr class="fn-total"><td colspan="3">جمع روز {{ jalali(d.date) }}</td><td class="fn-num">{{ money(d.totalDebit) }}</td><td class="fn-num">{{ money(d.totalCredit) }}</td></tr>
        </template>
      </tbody>
    </table>

    <!-- general ledger / account review -->
    <template v-else-if="kind === 'general-ledger' || kind === 'account-review'">
      <p class="mb-1"><b>{{ data.code }}</b> - {{ data.title }}</p>
      <table class="table table-sm">
        <thead><tr><th>تاریخ</th><th>سند</th><th>حساب</th><th>تفصیلی</th><th>شرح</th><th>بدهکار</th><th>بستانکار</th><th>مانده</th></tr></thead>
        <tbody>
          <tr class="fn-total"><td colspan="7">مانده ابتدای دوره</td><td class="fn-num">{{ money(data.opening) }}</td></tr>
          <tr v-for="(r, i) in data.rows" :key="i">
            <td>{{ jalali(r.date) }}</td>
            <td class="fn-num">{{ r.voucherNumber ?? "موقت" }}</td>
            <td class="inv-ltr">{{ r.accountCode }}</td>
            <td>{{ r.detailTitle }}</td>
            <td>{{ r.description }}</td>
            <td class="fn-num">{{ moneyOrDash(r.debit) }}</td>
            <td class="fn-num">{{ moneyOrDash(r.credit) }}</td>
            <td class="fn-num">{{ money(r.balance) }}</td>
          </tr>
          <tr class="fn-total"><td colspan="5">جمع گردش و مانده پایان</td><td class="fn-num">{{ money(data.totalDebit) }}</td><td class="fn-num">{{ money(data.totalCredit) }}</td><td class="fn-num">{{ money(data.closing) }}</td></tr>
        </tbody>
      </table>
      <p class="inv-muted mb-0">مانده مثبت = بدهکار، منفی = بستانکار.</p>
    </template>

    <!-- trial balance -->
    <table v-else-if="kind === 'trial-balance'" class="table table-sm">
      <thead>
        <tr>
          <th rowspan="2">کد</th><th rowspan="2">عنوان</th>
          <th v-if="cols >= 6" colspan="2">مانده ابتدای دوره</th>
          <th colspan="2">گردش دوره</th>
          <th v-if="cols >= 4" colspan="2">مانده پایان</th>
        </tr>
        <tr>
          <template v-if="cols >= 6"><th>بدهکار</th><th>بستانکار</th></template>
          <th>بدهکار</th><th>بستانکار</th>
          <template v-if="cols >= 4"><th>بدهکار</th><th>بستانکار</th></template>
        </tr>
      </thead>
      <tbody>
        <tr v-if="!(data.rows || []).length"><td :colspan="2 + (cols >= 6 ? 2 : 0) + 2 + (cols >= 4 ? 2 : 0)" class="text-center inv-muted">گردشی در این بازه نیست.</td></tr>
        <tr v-for="r in data.rows" :key="r.code">
          <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td>
          <template v-if="cols >= 6"><td class="fn-num">{{ moneyOrDash(r.openingDebit) }}</td><td class="fn-num">{{ moneyOrDash(r.openingCredit) }}</td></template>
          <td class="fn-num">{{ moneyOrDash(r.debit) }}</td><td class="fn-num">{{ moneyOrDash(r.credit) }}</td>
          <template v-if="cols >= 4"><td class="fn-num">{{ moneyOrDash(r.closingDebit) }}</td><td class="fn-num">{{ moneyOrDash(r.closingCredit) }}</td></template>
        </tr>
        <tr class="fn-total">
          <td colspan="2">جمع {{ data.balanced ? "(متوازن)" : "(نامتوازن)" }}</td>
          <template v-if="cols >= 6"><td class="fn-num">{{ money(data.openingDebit) }}</td><td class="fn-num">{{ money(data.openingCredit) }}</td></template>
          <td class="fn-num">{{ money(data.totalDebit) }}</td><td class="fn-num">{{ money(data.totalCredit) }}</td>
          <template v-if="cols >= 4"><td class="fn-num">{{ money(data.closingDebit) }}</td><td class="fn-num">{{ money(data.closingCredit) }}</td></template>
        </tr>
      </tbody>
    </table>

    <!-- balance sheet / income statement -->
    <template v-else-if="kind === 'balance-sheet' || kind === 'income-statement'">
      <table class="table table-sm">
        <thead><tr><th style="width: 110px">کد</th><th>شرح</th><th style="width: 200px">مبلغ (ریال)</th></tr></thead>
        <tbody>
          <template v-for="s in sections" :key="s.name">
            <tr v-if="s.name"><td colspan="3"><b>{{ s.name }}</b></td></tr>
            <tr v-for="l in s.lines" :key="s.name + l.code + l.title" :class="{ 'fn-total': l.isTotal }">
              <td class="inv-ltr"><a v-if="l.code && !l.isTotal" class="screen-only" href="#" @click.prevent="emit('drill', l.code)">{{ l.code }}</a><span :class="{ 'print-only': l.code && !l.isTotal }">{{ l.code }}</span></td><td>{{ l.title }}</td><td class="fn-num">{{ money(l.amount) }}</td>
            </tr>
          </template>
        </tbody>
      </table>
      <table v-if="kind === 'balance-sheet'" class="table table-sm">
        <tbody>
          <tr><td>جمع دارایی‌ها</td><td class="fn-num">{{ money(data.totalAssets) }}</td></tr>
          <tr><td>جمع بدهی‌ها</td><td class="fn-num">{{ money(data.totalLiabilities) }}</td></tr>
          <tr><td>جمع حقوق صاحبان سرمایه</td><td class="fn-num">{{ money(data.totalEquity) }}</td></tr>
          <tr class="fn-total"><td>وضعیت تراز</td><td>{{ data.balanced ? "متوازن" : "نامتوازن" }}</td></tr>
        </tbody>
      </table>
      <table v-else class="table table-sm">
        <tbody>
          <tr><td>سود ناخالص</td><td class="fn-num">{{ money(data.grossProfit) }}</td></tr>
          <tr><td>سود عملیاتی</td><td class="fn-num">{{ money(data.operatingProfit) }}</td></tr>
          <tr><td>سود قبل از مالیات</td><td class="fn-num">{{ money(data.profitBeforeTax) }}</td></tr>
          <tr class="fn-total"><td>سود خالص</td><td class="fn-num">{{ money(data.netProfit) }}</td></tr>
        </tbody>
      </table>
    </template>

    <!-- cash-flow statement (indirect method) -->
    <template v-else-if="kind === 'cash-flow'">
      <table class="table table-sm">
        <thead><tr><th>بخش</th><th>شرح</th><th style="width: 200px">مبلغ (ریال)</th><th>حساب‌های مرتبط</th></tr></thead>
        <tbody>
          <tr><td></td><td>سود خالص دوره</td><td class="fn-num">{{ money(data.netProfit) }}</td><td></td></tr>
          <tr v-for="l in data.lines" :key="l.key">
            <td>{{ sectionNames[l.section] || l.section }}</td><td>{{ l.title }}</td><td class="fn-num">{{ money(l.amount) }}</td>
            <td class="inv-ltr"><template v-for="(a, i) in l.accounts" :key="a"><a class="screen-only" href="#" @click.prevent="emit('drill', a)">{{ a }}</a><span class="print-only">{{ a }}</span><span v-if="i < l.accounts.length - 1">، </span></template></td>
          </tr>
          <tr class="fn-total"><td colspan="2">جریان نقد عملیاتی</td><td class="fn-num">{{ money(data.operating) }}</td><td></td></tr>
          <tr class="fn-total"><td colspan="2">جریان نقد سرمایه‌گذاری</td><td class="fn-num">{{ money(data.investing) }}</td><td></td></tr>
          <tr class="fn-total"><td colspan="2">جریان نقد تامین مالی</td><td class="fn-num">{{ money(data.financing) }}</td><td></td></tr>
          <tr class="fn-total"><td colspan="2">خالص تغییر در وجه نقد</td><td class="fn-num">{{ money(data.netChange) }}</td><td></td></tr>
          <tr><td colspan="2">وجه نقد ابتدای دوره</td><td class="fn-num">{{ money(data.openingCash) }}</td><td></td></tr>
          <tr class="fn-total"><td colspan="2">وجه نقد پایان دوره</td><td class="fn-num">{{ money(data.closingCash) }}</td><td></td></tr>
        </tbody>
      </table>
      <p :class="data.ties ? '' : 'text-danger'">{{ data.ties ? "مانده نقد صورت جریان با ترازنامه مطابقت دارد." : "مانده نقد صورت جریان با ترازنامه مطابقت ندارد؛ مغایرت را بررسی کنید." }}</p>
    </template>

    <!-- changes in equity -->
    <template v-else-if="kind === 'equity'">
      <table class="table table-sm">
        <thead><tr><th>شرح</th><th>مانده ابتدای دوره</th><th>تغییرات</th><th>مانده پایان دوره</th></tr></thead>
        <tbody>
          <tr v-for="r in data.rows" :key="r.key"><td>{{ r.title }}</td><td class="fn-num">{{ money(r.opening) }}</td><td class="fn-num">{{ money(r.movement) }}</td><td class="fn-num">{{ money(r.closing) }}</td></tr>
          <tr class="fn-total"><td>جمع حقوق صاحبان سرمایه</td><td class="fn-num">{{ money(data.openingEquity) }}</td><td class="fn-num">{{ money(Number(data.closingEquity) - Number(data.openingEquity)) }}</td><td class="fn-num">{{ money(data.closingEquity) }}</td></tr>
          <tr><td>سود خالص دوره</td><td colspan="3" class="fn-num">{{ money(data.netProfit) }}</td></tr>
          <tr><td>حقوق صاحبان سرمایه در ترازنامه</td><td colspan="3" class="fn-num">{{ money(data.balanceSheetEquity) }}</td></tr>
        </tbody>
      </table>
      <p :class="data.ties ? '' : 'text-danger'">{{ data.ties ? "جمع صورت تغییرات با ترازنامه مطابقت دارد." : "جمع صورت تغییرات با ترازنامه مطابقت ندارد." }}</p>
    </template>

    <!-- financial ratios -->
    <template v-else-if="kind === 'ratios'">
      <p class="mb-1">درآمد: <b>{{ money(data.revenue) }}</b> - بهای تمام‌شده فروش: <b>{{ money(data.costOfSales) }}</b> - سود خالص: <b>{{ money(data.netProfit) }}</b> - تعداد روز دوره: <b>{{ data.days }}</b></p>
      <table class="table table-sm">
        <thead><tr><th>نسبت</th><th style="width: 140px">مقدار</th><th>فرمول</th></tr></thead>
        <tbody><tr v-for="r in data.ratios" :key="r.key"><td>{{ r.title }}</td><td class="fn-num">{{ pct(r.value) }}</td><td>{{ r.formula }}</td></tr></tbody>
      </table>
    </template>

    <!-- comparative statement: two fiscal years side by side -->
    <template v-else-if="kind === 'comparative'">
      <table class="table table-sm">
        <thead><tr><th style="width: 110px">کد</th><th>شرح</th><th style="width: 170px">{{ data.labelA }}</th><th style="width: 170px">{{ data.labelB }}</th><th style="width: 170px">تغییر</th><th style="width: 90px">درصد تغییر</th></tr></thead>
        <tbody>
          <template v-for="s in data.sections" :key="s.name">
            <tr v-if="s.name"><td colspan="6"><b>{{ s.name }}</b></td></tr>
            <tr v-for="r in s.rows" :key="s.name + r.code + r.title" :class="{ 'fn-total': r.isTotal }">
              <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td class="fn-num">{{ money(r.a) }}</td><td class="fn-num">{{ money(r.b) }}</td><td class="fn-num">{{ money(r.b - r.a) }}</td><td class="fn-num">{{ r.a ? ((((r.b - r.a) / Math.abs(r.a)) * 100).toFixed(1) + "%") : "-" }}</td>
            </tr>
          </template>
        </tbody>
      </table>
    </template>

    <!-- project profitability -->
    <table v-else-if="kind === 'project-profitability'" class="table table-sm">
      <thead><tr><th>کد</th><th>پروژه</th><th>درآمد</th><th>بهای تمام‌شده</th><th>حاشیه سود</th><th>درصد</th><th>بهای زیرحساب بها</th><th>اختلاف بها</th></tr></thead>
      <tbody>
        <tr v-if="!data.length"><td colspan="8" class="text-center inv-muted">پروژه‌ای نیست.</td></tr>
        <tr v-for="(r, i) in data" :key="i">
          <td class="inv-ltr">{{ r.contractCode || "-" }}</td><td>{{ r.contractTitle || "بدون پروژه" }}</td>
          <td class="fn-num">{{ money(r.revenue) }}</td><td class="fn-num">{{ money(r.cost) }}</td><td class="fn-num">{{ money(r.margin) }}</td>
          <td class="fn-num">{{ r.marginPercent === null || r.marginPercent === undefined ? "-" : Number(r.marginPercent).toFixed(1) + "%" }}</td>
          <td class="fn-num">{{ money(r.costEntries) }}</td><td class="fn-num" :class="{ 'text-danger': r.costDifference }">{{ money(r.costDifference) }}</td>
        </tr>
      </tbody>
    </table>

    <!-- reconciliation check -->
    <template v-else-if="kind === 'reconciliation'">
      <p>
        وضعیت کلی: <span :class="data.clean ? 'ok' : 'bad'" class="inv-badge">{{ data.clean ? "بدون مغایرت" : "مغایرت دارد" }}</span>
        رویدادهای ثبت‌نشده: <b>{{ data.unpostedEvents }}</b> - اسناد قطعی‌نشده قدیمی‌تر از {{ data.oldVoucherDays }} روز: <b>{{ data.oldNonFinalVouchers }}</b>
      </p>
      <table class="table table-sm">
        <thead><tr><th>مورد</th><th>دفتر کل</th><th>زیرحساب</th><th>اختلاف</th><th>وضعیت</th><th>توضیح</th></tr></thead>
        <tbody>
          <tr v-for="r in data.rows" :key="r.key" :class="{ 'table-danger': !r.ok }">
            <td>{{ r.title }}</td><td class="fn-num">{{ money(r.ledger) }}</td><td class="fn-num">{{ money(r.subLedger) }}</td><td class="fn-num">{{ money(r.difference) }}</td>
            <td><span :class="r.ok ? 'ok' : 'bad'" class="inv-badge">{{ r.ok ? "مطابق" : "مغایر" }}</span></td><td>{{ r.note }}</td>
          </tr>
        </tbody>
      </table>
    </template>
  </div>
</template>
