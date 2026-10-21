<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionFinance, docStatuses, invoiceTypes, money } from "@/composables/fn";

// Sales invoice / credit note (فاکتور فروش): seller sees customer, lines, VAT and the contract deductions down to the receivable amount.
defineProps<{ inv: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    :title="inv.type === 6 ? 'اعلامیه بستانکار' : 'فاکتور فروش'"
    :fields="[
      { label: 'شماره', value: inv.number, ltr: true },
      { label: 'تاریخ', value: jalali(inv.date) },
      { label: 'نوع', value: invoiceTypes[inv.type] },
      { label: 'وضعیت', value: docStatuses[inv.status] },
      { label: 'خریدار', value: inv.customerName },
      { label: 'سررسید', value: inv.dueDate ? jalali(inv.dueDate) : '' },
      { label: 'شناسه یکتای مالیاتی', value: inv.taxId, ltr: true },
    ]"
    :notes="inv.description ? [{ label: 'توضیح', value: inv.description }] : []"
    :signatures="[{ title: 'تهیه‌کننده', name: inv.creatorName }, { title: 'تاییدکننده', name: inv.approvedByName }, { title: 'مهر و امضای خریدار' }]"
    :distribution="distributionFinance">
    <table>
      <thead><tr><th>ردیف</th><th>شرح</th><th>مقدار</th><th>واحد</th><th>فی (ریال)</th><th>تخفیف</th><th>مبلغ پس از تخفیف</th><th>مالیات ارزش افزوده</th><th>جمع (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(l, i) in inv.lines" :key="i">
          <td>{{ i + 1 }}</td><td>{{ l.description }}</td><td class="num inv-ltr">{{ l.quantity }}</td><td>{{ l.unit }}</td><td class="num inv-ltr">{{ money(l.unitPrice) }}</td>
          <td class="num inv-ltr">{{ money(l.discount) }}</td><td class="num inv-ltr">{{ money(l.amount) }}</td><td class="num inv-ltr">{{ money(l.vatAmount) }}</td><td class="num inv-ltr">{{ money(l.total) }}</td>
        </tr>
      </tbody>
    </table>
    <table class="mt-2">
      <tbody>
        <tr><th>جمع پس از تخفیف</th><td class="num inv-ltr">{{ money(inv.netAmount) }}</td></tr>
        <tr><th>مالیات ارزش افزوده</th><td class="num inv-ltr">{{ money(inv.vatAmount) }}</td></tr>
        <tr><th>جمع کل فاکتور</th><td class="num inv-ltr">{{ money(inv.grossAmount) }}</td></tr>
        <tr v-if="inv.advanceRecovery"><th>کسر پیش‌دریافت</th><td class="num inv-ltr">{{ money(inv.advanceRecovery) }}</td></tr>
        <tr v-if="inv.performanceRetention"><th>سپرده حسن انجام کار ({{ inv.performanceRetentionPercent }}%)</th><td class="num inv-ltr">{{ money(inv.performanceRetention) }}</td></tr>
        <tr v-if="inv.insuranceRetention"><th>سپرده بیمه ({{ inv.insuranceRetentionPercent }}%)</th><td class="num inv-ltr">{{ money(inv.insuranceRetention) }}</td></tr>
        <tr v-if="inv.withholding"><th>مالیات تکلیفی ({{ inv.withholdingPercent }}%)</th><td class="num inv-ltr">{{ money(inv.withholding) }}</td></tr>
        <tr><th>مبلغ قابل دریافت (ریال)</th><td class="num inv-ltr"><b>{{ money(inv.receivableAmount) }}</b></td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
