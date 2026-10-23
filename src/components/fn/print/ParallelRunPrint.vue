<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionFinance, money, yesNo } from "@/composables/fn";
import { diffKinds } from "@/composables/fnF3";

// Parallel-run comparison report (گزارش مقایسه موازی PMIS و سپیدار) with the sign-off.
defineProps<{ run: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش مقایسه دوره موازی با سپیدار"
    :fields="[
      { label: 'دوره', value: run.periodTitle },
      { label: 'تاریخ بارگذاری', value: jalali(run.uploadedAt, true) },
      { label: 'ردیف‌ها', value: run.rows },
      { label: 'مطابق', value: run.matched },
      { label: 'فقط در PMIS', value: run.pmisOnly },
      { label: 'فقط در سپیدار', value: run.sepidarOnly },
      { label: 'مبلغ متفاوت', value: run.differs },
      { label: 'توضیح‌داده‌نشده', value: run.unexplained },
      { label: 'امضای مدیر مالی', value: yesNo(run.signedOff) },
    ]"
    :notes="run.signedOff ? [{ label: 'یادداشت تایید', value: run.signOffNote }, { label: 'زمان تایید', value: jalali(run.signedOffAt, true) }] : []"
    :signatures="[{ title: 'تهیه‌کننده (حسابداری)' }, { title: 'مدیر مالی' }, { title: 'مدیرعامل' }]"
    :distribution="distributionFinance">
    <table>
      <thead><tr><th>ردیف</th><th>کد حساب</th><th>عنوان</th><th>تفصیلی</th><th>نوع اختلاف</th><th>بدهکار PMIS</th><th>بستانکار PMIS</th><th>بدهکار سپیدار</th><th>بستانکار سپیدار</th><th>توضیح</th></tr></thead>
      <tbody>
        <tr v-for="(d, i) in run.differences" :key="d.id">
          <td>{{ i + 1 }}</td><td class="inv-ltr">{{ d.accountCode }}</td><td>{{ d.accountTitle }}</td><td class="inv-ltr">{{ d.detailCode }}</td><td>{{ diffKinds[d.kind] }}</td>
          <td class="num inv-ltr">{{ money(d.pmisDebit) }}</td><td class="num inv-ltr">{{ money(d.pmisCredit) }}</td><td class="num inv-ltr">{{ money(d.sepidarDebit) }}</td><td class="num inv-ltr">{{ money(d.sepidarCredit) }}</td><td>{{ d.explanation }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
