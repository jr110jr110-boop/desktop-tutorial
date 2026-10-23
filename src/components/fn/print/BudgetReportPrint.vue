<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { distributionFinance, monthNames, money } from "@/composables/fn";
import { budgetGroupBys } from "@/composables/fnF3";

// Budget against actual (گزارش بودجه و عملکرد) for the months of the window.
defineProps<{ data: any; title: string }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش بودجه در برابر عملکرد"
    :fields="[
      { label: 'بودجه', value: title },
      { label: 'از ماه', value: monthNames[data.fromPeriod - 1] },
      { label: 'تا ماه', value: monthNames[data.toPeriod - 1] },
      { label: 'گروه‌بندی', value: budgetGroupBys[data.groupBy] || data.groupBy },
      { label: 'روش پیش‌بینی', value: data.forecastMethod === 'RunRate' ? 'روند جاری' : 'بودجه باقی‌مانده' },
      { label: 'جمع بودجه (ریال)', value: money(data.totalBudget) },
      { label: 'جمع عملکرد (ریال)', value: money(data.totalActual) },
      { label: 'جمع انحراف (ریال)', value: money(data.totalVariance) },
      { label: 'عملکرد خارج از بودجه (ریال)', value: money(data.unbudgetedActual) },
    ]"
    :signatures="[{ title: 'تهیه‌کننده (حسابداری)' }, { title: 'مدیر مالی' }, { title: 'مدیرعامل' }]"
    :distribution="distributionFinance">
    <table>
      <thead><tr><th>ردیف</th><th>کد</th><th>عنوان</th><th>بودجه</th><th>عملکرد</th><th>انحراف</th><th>درصد انحراف</th><th>بودجه کل سال</th><th>پیش‌بینی پایان سال</th></tr></thead>
      <tbody>
        <tr v-for="(r, i) in data.rows" :key="r.key">
          <td>{{ i + 1 }}</td><td class="inv-ltr">{{ r.key }}</td><td>{{ r.title }}</td><td class="num inv-ltr">{{ money(r.budget) }}</td><td class="num inv-ltr">{{ money(r.actual) }}</td>
          <td class="num inv-ltr">{{ money(r.variance) }}</td><td class="num inv-ltr">{{ Number(r.variancePercent).toFixed(1) }}%</td><td class="num inv-ltr">{{ money(r.fullYearBudget) }}</td><td class="num inv-ltr">{{ money(r.forecast) }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
