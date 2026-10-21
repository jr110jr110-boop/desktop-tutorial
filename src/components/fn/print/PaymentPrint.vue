<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionTreasury, docStatuses, instruments, money, paymentPurposes } from "@/composables/fn";

// Payment document (سند پرداخت): payee, purpose, the paid requests, the payment lines (cash, transfer, cheque) and the signatures of the chain.
const props = defineProps<{ p: any; banks: any[]; boxes: any[] }>();
const bankTitle = (id: number | null) => props.banks.find((b) => b.id === id)?.title || "";
const boxTitle = (id: number | null) => props.boxes.find((b) => b.id === id)?.title || "";
</script>

<template>
  <PrintForm
    class="q-form"
    title="سند پرداخت"
    :fields="[
      { label: 'شماره', value: p.number, ltr: true },
      { label: 'تاریخ', value: jalali(p.date) },
      { label: 'بابت', value: paymentPurposes[p.purpose] },
      { label: 'وضعیت', value: docStatuses[p.status] },
      { label: 'پرداخت به', value: p.payeeName },
      { label: 'مبلغ ناخالص (ریال)', value: money(p.grossAmount) },
      { label: 'کسورات (ریال)', value: money(p.deductionAmount) },
      { label: 'مبلغ خالص پرداختی (ریال)', value: money(p.netAmount) },
    ]"
    :notes="p.description ? [{ label: 'توضیح', value: p.description }] : []"
    :signatures="[
      { title: 'تهیه‌کننده (خزانه‌داری)', name: p.creatorName },
      { title: 'تاییدکننده (مدیر مالی)' },
      { title: 'ثبت‌کننده نهایی', name: p.postedByName, date: jalali(p.postedAt) },
      { title: 'دریافت‌کننده' },
    ]"
    :distribution="distributionTreasury">
    <table v-if="p.requests && p.requests.length">
      <thead><tr><th>ردیف</th><th>شماره درخواست پرداخت</th><th>مبلغ (ریال)</th><th>کسورات (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(r, i) in p.requests" :key="r.paymentRequestId"><td>{{ i + 1 }}</td><td class="inv-ltr">{{ r.paymentRequestNumber }}</td><td class="num inv-ltr">{{ money(r.amount) }}</td><td class="num inv-ltr">{{ money(r.deductions) }}</td></tr>
      </tbody>
    </table>
    <table class="mt-2">
      <thead><tr><th>ردیف</th><th>روش</th><th>حساب بانکی / صندوق</th><th>شماره پیگیری / سریال چک</th><th>سررسید چک</th><th>مبلغ (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(l, i) in p.lines" :key="i">
          <td>{{ i + 1 }}</td>
          <td>{{ instruments[l.instrument] }}</td>
          <td>{{ bankTitle(l.bankAccountId) || boxTitle(l.cashBoxId) }}</td>
          <td class="inv-ltr">{{ l.chequeSerial || l.reference }}</td>
          <td>{{ l.chequeDueDate ? jalali(l.chequeDueDate) : "" }}</td>
          <td class="num inv-ltr">{{ money(l.amount) }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
