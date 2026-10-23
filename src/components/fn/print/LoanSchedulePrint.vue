<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionFinance, money } from "@/composables/fn";
import { facilityTypes, installmentStatuses, loanStatuses, scheduleKinds } from "@/composables/fnF3";

// Loan / facility repayment schedule (جدول بازپرداخت تسهیلات).
defineProps<{ loan: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="جدول بازپرداخت تسهیلات"
    :fields="[
      { label: 'شماره', value: loan.number },
      { label: 'بانک', value: loan.bankName },
      { label: 'نوع', value: facilityTypes[loan.facilityType] },
      { label: 'شماره قرارداد', value: loan.contractNo },
      { label: 'مبلغ (ریال)', value: money(loan.amount) },
      { label: 'نرخ سود (درصد)', value: loan.ratePercent },
      { label: 'روش بازپرداخت', value: scheduleKinds[loan.scheduleKind] },
      { label: 'تعداد اقساط', value: loan.installmentCount },
      { label: 'تاریخ شروع', value: jalali(loan.startDate) },
      { label: 'تاریخ پایان', value: jalali(loan.endDate) },
      { label: 'جمع سود (ریال)', value: money(loan.totalInterest) },
      { label: 'اصل باقی‌مانده (ریال)', value: money(loan.outstandingPrincipal) },
      { label: 'سود تعلق‌گرفته پرداخت‌نشده (ریال)', value: money(loan.accruedInterestPayable) },
      { label: 'وضعیت', value: loanStatuses[loan.status] },
    ]"
    :signatures="[{ title: 'تهیه‌کننده (خزانه‌داری)' }, { title: 'مدیر مالی' }, { title: 'مدیرعامل' }]"
    :distribution="distributionFinance">
    <table>
      <thead><tr><th>قسط</th><th>سررسید</th><th>اصل</th><th>سود</th><th>جمع قسط</th><th>وضعیت</th><th>تاریخ پرداخت</th><th>جریمه</th></tr></thead>
      <tbody>
        <tr v-for="i in loan.installments" :key="i.id">
          <td>{{ i.sequence }}</td><td>{{ jalali(i.dueDate) }}</td><td class="num inv-ltr">{{ money(i.principal) }}</td><td class="num inv-ltr">{{ money(i.interest) }}</td><td class="num inv-ltr">{{ money(i.total) }}</td><td>{{ installmentStatuses[i.status] }}</td><td>{{ jalali(i.paidDate) }}</td><td class="num inv-ltr">{{ money(i.penaltyPaid) }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
