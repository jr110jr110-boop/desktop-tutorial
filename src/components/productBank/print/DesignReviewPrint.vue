<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { findingStatuses, inputKinds, outputKinds, reviewOutcomes, reviewStatuses, reviewTypes, revisionText } from "@/composables/designControl";

// Design review minutes (ISO 9001 8.3.4): the review, its participants (who is independent), the inputs and outputs it covers, the findings with owner and due date, the outcome and the signatures. A4, B Zar through PrintForm.
defineProps<{ review: any }>();
const distribution = "توزیع نسخ: ۱- مهندسی ۲- تضمین کیفیت ۳- پرونده طراحی محصول";
</script>

<template>
  <PrintForm
    class="q-form"
    :title="'صورتجلسه ' + reviewTypes[review.type]"
    :fields="[
      { label: 'شماره', value: review.number, ltr: true },
      { label: 'وضعیت', value: reviewStatuses[review.status] },
      { label: 'محصول', value: review.productCode, ltr: true },
      { label: 'بازنگری محصول', value: revisionText(review.productRevision), ltr: true },
      { label: 'برنامه طراحی', value: review.planNumber, ltr: true },
      { label: 'تاریخ برگزاری', value: jalali(review.heldDate) },
      { label: 'نتیجه', value: reviewOutcomes[review.outcome] },
      { label: 'تاریخ بستن', value: jalali(review.closedAt) },
    ]"
    :notes="[{ label: 'عنوان', value: review.title }, { label: 'صورتجلسه', value: review.minutes || '-' }, { label: 'شرح نتیجه', value: review.outcomeNote || '-' }]"
    :signatures="[
      { title: 'برگزارکننده', name: review.heldByName, date: review.heldAt ? jalali(review.heldAt) : '' },
      { title: 'بسته‌کننده (غیر از تهیه‌کننده خروجی‌ها)', name: review.closedByName, date: review.closedAt ? jalali(review.closedAt) : '' },
    ]"
    :distribution="distribution">
    <table>
      <thead><tr><th colspan="3">شرکت‌کنندگان</th></tr><tr><th>نام</th><th>نقش</th><th>مستقل از تهیه‌کنندگان</th></tr></thead>
      <tbody><tr v-for="p in review.participants" :key="p.id"><td>{{ p.name }}</td><td>{{ p.roleText }}</td><td>{{ p.independent ? "بله" : "خیر" }}</td></tr></tbody>
    </table>
    <table>
      <thead><tr><th colspan="3">خروجی‌های بازبینی‌شده</th></tr><tr><th>نوع</th><th>خروجی</th><th>بازنگری</th></tr></thead>
      <tbody><tr v-for="o in review.outputs" :key="o.id"><td>{{ outputKinds[o.kind] }}</td><td>{{ o.label }}</td><td>{{ o.revisionText }}</td></tr></tbody>
    </table>
    <table>
      <thead><tr><th colspan="5">ورودی‌های طراحی تحت پوشش</th></tr><tr><th>کد</th><th>نوع</th><th>الزام</th><th>معیار پذیرش</th><th>پوشش</th></tr></thead>
      <tbody>
        <tr v-for="i in review.inputs" :key="i.inputId"><td>{{ i.code }}</td><td>{{ inputKinds[i.kind] }}</td><td>{{ i.statement }}</td><td>{{ i.acceptanceCriterion }}</td><td>{{ i.covered ? "بله" : "خیر" }}</td></tr>
      </tbody>
    </table>
    <table>
      <thead><tr><th colspan="5">یافته‌ها و اقدام‌ها</th></tr><tr><th>#</th><th>شرح</th><th>مالک</th><th>مهلت</th><th>وضعیت</th></tr></thead>
      <tbody>
        <tr v-if="!review.findings.length"><td colspan="5">یافته‌ای ثبت نشده است.</td></tr>
        <tr v-for="f in review.findings" :key="f.id"><td>{{ f.seq }}</td><td>{{ f.description }}</td><td>{{ f.ownerName }}</td><td>{{ jalali(f.dueDate) }}</td><td>{{ findingStatuses[f.status] }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
