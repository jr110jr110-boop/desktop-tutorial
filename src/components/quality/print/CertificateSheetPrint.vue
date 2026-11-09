<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, num } from "@/composables/inventory";
import { certificateResults, certificateTypes, certRowStatuses, certValueKinds, distributionQc } from "@/composables/quality";

// Verification sheet of a structured EN 10204 certificate (Package C): the heat number against the lot, every chemistry and mechanical value against the limits of the approved material specification, the signatory
// or the inspector, the server verdict and the problems that block the acceptance of the receipt. A4, B Zar through PrintForm.
defineProps<{ sheet: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="برگه راستی‌آزمایی گواهی مواد (EN 10204)"
    :fields="[
      { label: 'کد کالا', value: sheet.certificate.itemCode, ltr: true },
      { label: 'نام کالا', value: sheet.certificate.itemTitle },
      { label: 'شماره بچ', value: sheet.certificate.lotNumber, ltr: true },
      { label: 'ذوب بچ', value: sheet.certificate.heatNumber || '-', ltr: true },
      { label: 'ذوب روی گواهی', value: sheet.certificate.certHeatNumber || '-', ltr: true },
      { label: 'نوع گواهی', value: certificateTypes[sheet.certificate.type] },
      { label: 'شماره گواهی', value: sheet.certificate.number, ltr: true },
      { label: 'گرید / استاندارد', value: (sheet.certificate.grade || '-') + ' / ' + (sheet.certificate.standard || '-'), ltr: true },
      { label: 'مشخصات ماده', value: sheet.spec ? sheet.spec.grade + ' ' + sheet.spec.standard + ' (نسخه ' + sheet.spec.version + ')' : 'تاییدشده‌ای نیست', ltr: true },
      { label: sheet.certificate.type === 3 ? 'سازمان / بازرس' : 'امضاکننده سازنده', value: sheet.certificate.type === 3 ? [sheet.certificate.inspectorOrganisation, sheet.certificate.inspectorName, sheet.certificate.inspectorStampNo].filter(Boolean).join(' - ') || '-' : sheet.certificate.manufacturerSignatory || '-' },
      { label: 'حکم سرور', value: certRowStatuses[sheet.certificate.evaluation] },
      { label: 'نتیجه بررسی QC', value: certificateResults[sheet.certificate.result] },
    ]"
    :notes="[
      { label: 'پذیرش رسید', value: sheet.receiptAcceptable ? 'گواهی شرایط پذیرش رسید را دارد.' : 'پذیرش رسید با این گواهی ممکن نیست.' },
      { label: 'ایرادها', value: (sheet.problems || []).join(' | ') || '-' },
    ]"
    :signatures="[
      { title: 'ثبت‌کننده گواهی', name: sheet.certificate.creatorName },
      { title: 'تایید کنترل کیفیت', hint: '(غیر از ثبت‌کننده)', name: sheet.certificate.verifiedByName, date: sheet.certificate.verifiedAt ? jalali(sheet.certificate.verifiedAt) : '' },
    ]"
    :distribution="distributionQc">
    <table>
      <thead><tr><th>نوع</th><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>مقدار گواهی</th><th>حکم</th></tr></thead>
      <tbody>
        <tr v-if="!(sheet.certificate.values || []).length"><td colspan="7">مقداری روی گواهی ثبت نشده است.</td></tr>
        <tr v-for="(v, i) in sheet.certificate.values || []" :key="i">
          <td>{{ certValueKinds[v.kind] }}</td><td>{{ v.name }}</td><td>{{ v.unit }}</td>
          <td>{{ v.min === null || v.min === undefined ? "-" : num(v.min) }}</td><td>{{ v.max === null || v.max === undefined ? "-" : num(v.max) }}</td><td>{{ num(v.value) }}</td><td>{{ certRowStatuses[v.status] }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
