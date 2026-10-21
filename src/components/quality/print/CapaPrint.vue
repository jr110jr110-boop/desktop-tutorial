<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { capaSources, capaStatuses, distributionQa, qualityFormCodes, rootCauseMethods } from "@/composables/quality";

// FO-QA-017: nonconformity and corrective / preventive action form.
defineProps<{ capa: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="فرم شناسایی موارد نامنطبق و اقدامات اصلاحی و پیشگیرانه (CAPA)"
    :form-code="qualityFormCodes.capa"
    :fields="[
      { label: 'شماره', value: capa.number, ltr: true },
      { label: 'منبع', value: capaSources[capa.source] + (capa.sourceNumber ? ' - ' + capa.sourceNumber : '') },
      { label: 'وضعیت', value: capaStatuses[capa.status] },
      { label: 'مسئول (مالک)', value: capa.ownerName },
      { label: 'مهلت اقدام', value: jalali(capa.dueDate) },
      { label: 'مهلت بررسی اثربخشی', value: jalali(capa.effectivenessDueDate) },
      { label: 'روش تحلیل علت', value: rootCauseMethods[capa.rootCauseMethod] },
      { label: 'تاریخ اجرا', value: jalali(capa.implementedAt) },
    ]"
    :notes="[
      { label: 'شرح مسئله', value: capa.problem },
      { label: 'اقدام مهار / اصلاح فوری', value: capa.containment || '-' },
      { label: 'تحلیل علت ریشه‌ای', value: capa.rootCauseAnalysis || '-' },
      { label: 'معیار اثربخشی', value: capa.effectivenessCriteria || '-' },
      { label: 'نتیجه بررسی اثربخشی', value: capa.effectivenessResult || '-' },
      { label: 'توضیح بستن', value: capa.closeNote || '-' },
    ]"
    :signatures="[
      { title: 'مسئول اقدام (مالک CAPA)', name: capa.ownerName, date: jalali(capa.implementedAt) },
      { title: 'تایید اثربخشی', hint: '(غیر از مالک - تضمین کیفیت / کنترل کیفیت)' },
      { title: 'بستن توسط تضمین کیفیت', hint: '(غیر از مالک)', name: capa.closedByName, date: jalali(capa.closedAt) },
    ]"
    :distribution="distributionQa">
    <table>
      <thead><tr><th>ردیف</th><th>اقدام</th><th>مسئول</th><th>مهلت</th><th>تاریخ انجام</th><th>توضیح شواهد</th></tr></thead>
      <tbody>
        <tr v-if="!capa.actions.length"><td colspan="6">اقدامی تعریف نشده است.</td></tr>
        <tr v-for="(a, i) in capa.actions" :key="a.id">
          <td>{{ i + 1 }}</td><td>{{ a.description }}</td><td>{{ a.ownerName }}</td><td>{{ jalali(a.dueDate) }}</td><td>{{ jalali(a.doneAt) }}</td><td>{{ a.evidenceNote }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
