<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionTreasury, docStatuses, instruments, money } from "@/composables/fn";

// Receipt document (سند دریافت): payer, the instruments received (cash, transfer, cheque with its details), the settled invoices.
const props = defineProps<{ r: any; banks: any[]; boxes: any[] }>();
const bankTitle = (id: number | null) => props.banks.find((b) => b.id === id)?.title || "";
const boxTitle = (id: number | null) => props.boxes.find((b) => b.id === id)?.title || "";
</script>

<template>
  <PrintForm
    class="q-form"
    title="سند دریافت وجه"
    :fields="[
      { label: 'شماره', value: r.number, ltr: true },
      { label: 'تاریخ', value: jalali(r.date) },
      { label: 'دریافت از', value: r.customerName || r.payerName },
      { label: 'وضعیت', value: docStatuses[r.status] },
      { label: 'جمع دریافتی (ریال)', value: money(r.totalAmount) },
      { label: 'تخصیص‌یافته به فاکتورها (ریال)', value: money(r.allocatedAmount) },
    ]"
    :notes="r.description ? [{ label: 'بابت', value: r.description }] : []"
    :signatures="[{ title: 'تهیه‌کننده', name: r.creatorName }, { title: 'تاییدکننده (مالی)' }, { title: 'ثبت‌کننده نهایی', date: jalali(r.postedAt) }]"
    :distribution="distributionTreasury">
    <table>
      <thead><tr><th>ردیف</th><th>روش</th><th>حساب بانکی / صندوق</th><th>مشخصات چک</th><th>سررسید</th><th>مبلغ (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(l, i) in r.lines" :key="i">
          <td>{{ i + 1 }}</td>
          <td>{{ instruments[l.instrument] }}</td>
          <td>{{ bankTitle(l.bankAccountId) || boxTitle(l.cashBoxId) }}</td>
          <td>
            <template v-if="l.instrument === 3">سریال <span class="inv-ltr">{{ l.chequeSerial }}</span> - صیادی <span class="inv-ltr">{{ l.chequeSayadId }}</span> - {{ l.chequeBank }} {{ l.chequeBranch }} - {{ l.chequeHolder }}</template>
            <template v-else><span class="inv-ltr">{{ l.reference }}</span></template>
          </td>
          <td>{{ l.chequeDueDate ? jalali(l.chequeDueDate) : "" }}</td>
          <td class="num inv-ltr">{{ money(l.amount) }}</td>
        </tr>
      </tbody>
    </table>
    <table v-if="r.allocations && r.allocations.length" class="mt-2">
      <thead><tr><th>ردیف</th><th>فاکتور فروش</th><th>مبلغ تسویه‌شده (ریال)</th></tr></thead>
      <tbody><tr v-for="(a, i) in r.allocations" :key="i"><td>{{ i + 1 }}</td><td class="inv-ltr">{{ a.invoiceNumber }}</td><td class="num inv-ltr">{{ money(a.amount) }}</td></tr></tbody>
    </table>
  </PrintForm>
</template>
