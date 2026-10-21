<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionQa, qualityFormCodes, reviewInputCategories, reviewStatuses } from "@/composables/quality";

// FO-QA-022: management review minutes.
defineProps<{ review: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="صورتجلسه بازنگری مدیریت"
    :form-code="qualityFormCodes.managementReview"
    :fields="[
      { label: 'تاریخ جلسه', value: jalali(review.reviewDate) },
      { label: 'وضعیت', value: reviewStatuses[review.status] },
    ]"
    :notes="[{ label: 'حاضرین', value: review.attendees || '-' }]"
    :signatures="[{ title: 'مدیرعامل' }, { title: 'نماینده مدیریت (تضمین کیفیت)' }]"
    :distribution="distributionQa">
    <table>
      <thead><tr><th style="width: 28%">ورودی</th><th>خلاصه</th></tr></thead>
      <tbody><tr v-for="i in review.inputs" :key="i.id"><td>{{ i.categoryName || reviewInputCategories[i.category] }}</td><td style="white-space: pre-wrap">{{ i.text }}</td></tr></tbody>
    </table>
    <table class="mt-1">
      <thead><tr><th>ردیف</th><th>تصمیم</th><th>مسئول</th><th>مهلت</th><th>CAPA</th></tr></thead>
      <tbody>
        <tr v-if="!review.decisions.length"><td colspan="5">تصمیمی ثبت نشده.</td></tr>
        <tr v-for="(d, i) in review.decisions" :key="d.id"><td>{{ i + 1 }}</td><td style="white-space: pre-wrap">{{ d.text }}</td><td>{{ d.responsibleName }}</td><td>{{ jalali(d.dueDate) }}</td><td class="inv-ltr">{{ d.capaNumber }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
