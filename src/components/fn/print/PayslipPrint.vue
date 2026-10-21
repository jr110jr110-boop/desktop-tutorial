<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { distributionPayroll, money, monthNames, payrollItemKinds } from "@/composables/fn";

// Payslip (فیش حقوقی): confidential, printed for the employee only.
defineProps<{ p: any }>();
const earnings = (p: any) => (p.lines || []).filter((l: any) => l.kind === 1);
const deductions = (p: any) => (p.lines || []).filter((l: any) => l.kind === 2);
</script>

<template>
  <PrintForm
    class="q-form"
    title="فیش حقوقی"
    :fields="[
      { label: 'کد پرسنلی', value: p.personnelCode, ltr: true },
      { label: 'نام و نام خانوادگی', value: p.employeeName },
      { label: 'دوره', value: `${monthNames[p.month - 1]} ${p.year}` },
      { label: 'سمت', value: p.jobTitle },
      { label: 'واحد', value: p.unit },
      { label: 'شماره بیمه', value: p.insuranceNumber, ltr: true },
      { label: 'روز پرداختی', value: String(p.daysPaid) },
    ]"
    :signatures="[{ title: 'حقوق و دستمزد' }, { title: 'مدیر مالی' }, { title: 'دریافت‌کننده' }]"
    :distribution="distributionPayroll">
    <div class="row">
      <div class="col-6">
        <table>
          <thead><tr><th colspan="2">{{ payrollItemKinds[1] }}</th></tr></thead>
          <tbody>
            <tr v-for="l in earnings(p)" :key="l.rowNumber"><td>{{ l.title }}</td><td class="num inv-ltr">{{ money(l.amount) }}</td></tr>
            <tr><th>جمع مزایا</th><td class="num inv-ltr"><b>{{ money(p.grossEarnings) }}</b></td></tr>
          </tbody>
        </table>
      </div>
      <div class="col-6">
        <table>
          <thead><tr><th colspan="2">{{ payrollItemKinds[2] }}</th></tr></thead>
          <tbody>
            <tr v-for="l in deductions(p)" :key="l.rowNumber"><td>{{ l.title }}</td><td class="num inv-ltr">{{ money(l.amount) }}</td></tr>
            <tr><th>جمع کسور</th><td class="num inv-ltr"><b>{{ money((p.employeeInsurance || 0) + (p.tax || 0) + (p.otherDeductions || 0)) }}</b></td></tr>
          </tbody>
        </table>
      </div>
    </div>
    <table class="mt-2">
      <tbody>
        <tr><th>مشمول بیمه</th><td class="num inv-ltr">{{ money(p.insurableTotal) }}</td><th>مشمول مالیات</th><td class="num inv-ltr">{{ money(p.taxableTotal) }}</td></tr>
        <tr><th>خالص پرداختی (ریال)</th><td class="num inv-ltr" colspan="3"><b>{{ money(p.net) }}</b></td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
