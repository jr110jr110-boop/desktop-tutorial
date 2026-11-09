<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { ecClasses, ecUrgencies, targetKinds } from "@/composables/engChange";

// Printed engineering change request (A4, B Zar through PrintForm).
defineProps<{ req: any }>();
</script>

<template>
  <PrintForm
    title="درخواست تغییر مهندسی (ECR)"
    :fields="[
      { label: 'شماره', value: req.number, ltr: true },
      { label: 'تاریخ', value: jalali(req.createdAt) },
      { label: 'عنوان', value: req.title },
      { label: 'وضعیت', value: req.statusText },
      { label: 'کلاس', value: ecClasses[req.classification] },
      { label: 'فوریت', value: ecUrgencies[req.urgency] },
      { label: 'درخواست‌دهنده', value: req.requestedByName },
      { label: 'دستور تغییر', value: req.orderNumber || '-', ltr: true },
    ]"
    :notes="[
      { label: 'دلیل تغییر', value: req.reason },
      { label: 'شرح تغییر پیشنهادی', value: req.description || '-' },
      { label: 'نتیجه تصمیم', value: req.decisionNote || '-' },
    ]"
    :signatures="[
      { title: 'درخواست‌دهنده', name: req.requestedByName, date: jalali(req.submittedAt || req.createdAt) },
      { title: 'تصمیم مهندسی', hint: '(غیر از درخواست‌دهنده)', name: req.decidedByName, date: req.decidedAt ? jalali(req.decidedAt) : '' },
    ]"
    distribution="توزیع نسخ: ۱- مهندسی ۲- تضمین کیفیت ۳- برنامه‌ریزی و کنترل پروژه ۴- تولید">
    <table>
      <thead><tr><th>نوع</th><th>مورد متاثر</th><th>توضیح</th></tr></thead>
      <tbody>
        <tr v-for="(t, i) in req.targets" :key="i"><td>{{ targetKinds[t.kind] }}</td><td>{{ t.reference || t.partNumber }}</td><td>{{ t.note }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
