<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionFinance, money } from "@/composables/fn";

// Asset handover (تحویل اموال): the assets in the care of one person, signed by the custodian and the finance officer.
defineProps<{ data: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="صورت‌جلسه تحویل اموال"
    :fields="[
      { label: 'تحویل‌گیرنده (امین)', value: data.custodianName },
      { label: 'سمت', value: data.custodianTitle },
      { label: 'تاریخ چاپ', value: jalali(data.printedOn) },
      { label: 'تعداد اقلام', value: String((data.assets || []).length) },
    ]"
    :signatures="[{ title: 'تحویل‌دهنده (مالی)' }, { title: 'تحویل‌گیرنده (امین)', name: data.custodianName }, { title: 'مدیر مربوط' }]"
    :distribution="distributionFinance">
    <table>
      <thead><tr><th>ردیف</th><th>کد دارایی</th><th>شرح</th><th>سریال</th><th>محل</th><th>تاریخ بهره‌برداری</th><th>بها (ریال)</th></tr></thead>
      <tbody>
        <tr v-for="(a, i) in data.assets" :key="a.id"><td>{{ i + 1 }}</td><td class="inv-ltr">{{ a.code }}</td><td>{{ a.title }}</td><td class="inv-ltr">{{ a.serial }}</td><td>{{ a.location }}</td><td>{{ a.inUseDate ? jalali(a.inUseDate) : "" }}</td><td class="num inv-ltr">{{ money(a.cost) }}</td></tr>
      </tbody>
    </table>
    <p class="mt-2">اینجانب اقلام بالا را سالم و کامل تحویل گرفتم و مسئولیت نگهداری آن را می‌پذیرم.</p>
  </PrintForm>
</template>
