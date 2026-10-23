<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionFinance, yesNo } from "@/composables/fn";
import { closeStatuses } from "@/composables/fnF3";

// Period close checklist (فهرست کنترل بستن دوره).
defineProps<{ data: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="فهرست کنترل بستن دوره مالی"
    :fields="[
      { label: 'دوره', value: data.periodTitle },
      { label: 'موارد قرمز', value: data.redCount },
      { label: 'الزام کنترل', value: yesNo(data.enforced) },
      { label: 'عبور با علت', value: yesNo(data.overridden) },
    ]"
    :notes="data.overridden ? [{ label: 'علت عبور مدیر مالی', value: data.overrideReason }, { label: 'زمان', value: jalali(data.overriddenAt, true) }] : []"
    :signatures="[{ title: 'تهیه‌کننده (حسابداری)' }, { title: 'مدیر مالی' }, { title: 'مدیرعامل' }]"
    :distribution="distributionFinance">
    <table>
      <thead><tr><th>ردیف</th><th>مورد کنترل</th><th>وضعیت</th><th>تعداد</th><th>توضیح</th></tr></thead>
      <tbody>
        <tr v-for="(it, i) in data.items" :key="it.key"><td>{{ i + 1 }}</td><td>{{ it.title }}</td><td>{{ closeStatuses[it.status] || it.status }}</td><td>{{ it.count }}</td><td>{{ it.detail }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
