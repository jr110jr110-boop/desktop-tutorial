<script setup lang="ts">
// A4 RTL (B Zar) service report of a service order: facts, engineers, tasks, time sheet hours, measurements, parts, findings and recommendations, with the customer's signature box.
// No money and no labour amounts are printed (labour is payroll-protected); hours only.
import PrintForm from "@/components/print/PrintForm.vue";
import { dateText, hoursText, orderKinds, qtyText, timeKinds, warrantyDecisions } from "@/composables/service";

defineProps<{ o: any }>();
const distribution = "توزیع نسخ: ۱- مشتری ۲- خدمات پس از فروش ۳- بایگانی";
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش خدمات پس از فروش"
    :fields="[
      { label: 'شماره دستور', value: o.number, ltr: true },
      { label: 'نوع', value: orderKinds[o.kind] },
      { label: 'مشتری', value: o.customerName },
      { label: 'واحد (tag)', value: o.unitTag, ltr: true },
      { label: 'درخواست مرتبط', value: o.requestNumber || '-', ltr: true },
      { label: 'قرارداد LTSA', value: o.ltsaNumber || '-', ltr: true },
      { label: 'شروع برنامه', value: dateText(o.plannedStart) || o.plannedStartText || '-' },
      { label: 'پایان برنامه', value: dateText(o.plannedEnd) || o.plannedEndText || '-' },
      { label: 'شروع کار', value: o.startedAtText || '-' },
      { label: 'تصمیم گارانتی', value: warrantyDecisions[o.decision] },
      { label: 'کارکرد واحد (ساعت)', value: o.runningHoursReading === null || o.runningHoursReading === undefined ? '-' : qtyText(o.runningHoursReading) },
      { label: 'جمع ساعت کار و سفر', value: hoursText(o.totalHours) },
    ]"
    :notes="[
      { label: 'عنوان', value: o.title },
      { label: 'یافته‌ها', value: o.findings || '-' },
      { label: 'توصیه‌ها', value: o.recommendations || '-' },
    ]"
    :signatures="[{ title: 'مهندس خدمات' }, { title: 'مدیر خدمات پس از فروش' }, { title: 'نماینده مشتری (مهر و امضا)' }]"
    :distribution="distribution">
    <table v-if="(o.engineers || []).length" class="mb-2">
      <thead><tr><th>مهندسان</th></tr></thead>
      <tbody><tr v-for="e in o.engineers" :key="e.employeeId"><td>{{ e.name }}</td></tr></tbody>
    </table>
    <table v-if="(o.tasks || []).length" class="mb-2">
      <thead><tr><th style="width: 2.5rem">ردیف</th><th>وظیفه</th><th style="width: 5rem">انجام شد</th></tr></thead>
      <tbody><tr v-for="t in o.tasks" :key="t.id"><td>{{ t.rowNumber }}</td><td>{{ t.description }}</td><td>{{ t.isDone ? "بله" : "خیر" }}</td></tr></tbody>
    </table>
    <table v-if="(o.timeEntries || []).length" class="mb-2">
      <thead><tr><th>تاریخ</th><th>مهندس</th><th>نوع</th><th>ساعت</th><th>توضیح</th></tr></thead>
      <tbody><tr v-for="t in o.timeEntries" :key="t.id"><td>{{ t.workDateText }}</td><td>{{ t.employeeName }}</td><td>{{ timeKinds[t.kind] }}</td><td>{{ hoursText(t.hours) }}</td><td>{{ t.note }}</td></tr></tbody>
    </table>
    <table v-if="(o.measurements || []).length" class="mb-2">
      <thead><tr><th>اندازه‌گیری</th><th>مقدار</th><th>واحد</th><th>توضیح</th></tr></thead>
      <tbody><tr v-for="m in o.measurements" :key="m.id"><td>{{ m.name }}</td><td>{{ qtyText(m.value) }}</td><td>{{ m.unit }}</td><td>{{ m.note }}</td></tr></tbody>
    </table>
    <table v-if="(o.parts || []).some((p: any) => (p.lines || []).length)">
      <thead><tr><th>سند قطعات</th><th>کد</th><th>قطعه</th><th>تعداد</th></tr></thead>
      <tbody>
        <template v-for="p in o.parts" :key="p.documentId">
          <tr v-for="(l, i) in p.lines" :key="p.documentId + '-' + i"><td>{{ p.number || "-" }}</td><td>{{ l.itemCode }}</td><td>{{ l.itemName }}</td><td>{{ qtyText(l.quantity) }}</td></tr>
        </template>
      </tbody>
    </table>
  </PrintForm>
</template>
