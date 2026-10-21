<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionFinance, money, moneyOrDash, voucherStatuses, voucherTypes } from "@/composables/fn";

// Accounting voucher (سند حسابداری): header, lines with account / detail, totals, the three signatures of the control chain.
defineProps<{ v: any }>();
const sum = (rows: any[], key: string) => (rows || []).reduce((s, r) => s + (Number(r[key]) || 0), 0);
</script>

<template>
  <PrintForm
    class="q-form"
    title="سند حسابداری"
    :fields="[
      { label: 'شماره سند', value: v.number ?? 'قطعی نشده', ltr: true },
      { label: 'شماره موقت', value: v.temporaryNumber, ltr: true },
      { label: 'تاریخ', value: jalali(v.date) },
      { label: 'نوع', value: voucherTypes[v.type] },
      { label: 'وضعیت', value: voucherStatuses[v.status] },
      { label: 'سال مالی', value: v.fiscalYearTitle },
      { label: 'مبدا', value: v.sourceNumber || v.sourceType || '-' },
      { label: 'مبلغ کل (ریال)', value: money(v.totalAmount) },
    ]"
    :notes="[{ label: 'شرح سند', value: v.description || '-' }]"
    :signatures="[
      { title: 'تهیه‌کننده', name: v.creatorName, date: jalali(v.createdAt) },
      { title: 'کنترل‌کننده', name: v.checkedByName, date: jalali(v.checkedAt) },
      { title: 'مدیر مالی (قطعی‌کننده)', name: v.finalizedByName, date: jalali(v.finalizedAt) },
    ]"
    :distribution="distributionFinance">
    <table>
      <thead><tr><th>ردیف</th><th>کد حساب</th><th>عنوان حساب</th><th>تفصیلی</th><th>شرح</th><th>بدهکار (ریال)</th><th>بستانکار (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(l, i) in v.lines" :key="i">
          <td>{{ i + 1 }}</td>
          <td class="inv-ltr">{{ l.accountCode }}</td>
          <td>{{ l.accountTitle }}</td>
          <td>{{ [l.detail1Title, l.detail2Title].filter(Boolean).join(" / ") }}</td>
          <td>{{ l.description }}</td>
          <td class="num inv-ltr">{{ moneyOrDash(l.debit) }}</td>
          <td class="num inv-ltr">{{ moneyOrDash(l.credit) }}</td>
        </tr>
        <tr>
          <td colspan="5"><b>جمع</b></td>
          <td class="num inv-ltr"><b>{{ money(sum(v.lines, "debit")) }}</b></td>
          <td class="num inv-ltr"><b>{{ money(sum(v.lines, "credit")) }}</b></td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
