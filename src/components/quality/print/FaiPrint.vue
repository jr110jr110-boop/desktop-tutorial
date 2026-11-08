<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, num } from "@/composables/inventory";
import { distributionQc, faiStatuses } from "@/composables/quality";

// First article inspection report (FAI): the configuration it covers, every characteristic with its limits and measured value, and the three signatures (measurer, QC acceptor, QA approver). A4, B Zar through PrintForm.
defineProps<{ fai: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش بازرسی نمونه اول (FAI)"
    :fields="[
      { label: 'شماره', value: fai.number, ltr: true },
      { label: 'وضعیت', value: faiStatuses[fai.status] },
      { label: 'کد قطعه', value: fai.itemCode, ltr: true },
      { label: 'نام قطعه', value: fai.itemTitle },
      { label: 'دستور تولید', value: fai.productionOrderNumber || '-', ltr: true },
      { label: 'دستور ساخت', value: fai.workOrderCode || '-', ltr: true },
      { label: 'سابقه بازرسی', value: fai.qcRecordNumber || '-', ltr: true },
      { label: 'تاریخ', value: jalali(fai.createDate) },
    ]"
    :notes="[{ label: 'پیکربندی تحت پوشش', value: fai.fingerprintText || '-' }, { label: 'علت الزام', value: fai.trigger || '-' }]"
    :signatures="[
      { title: 'اندازه‌گیرنده (بازرس QC)', name: fai.measuredByName, date: fai.measuredAt ? jalali(fai.measuredAt) : '' },
      { title: 'پذیرش QC (بازرس دیگر)', name: fai.qcAcceptedByName, date: fai.qcAcceptedAt ? jalali(fai.qcAcceptedAt) : '' },
      { title: 'تایید QA', name: fai.qaApprovedByName, date: fai.qaApprovedAt ? jalali(fai.qaApprovedAt) : '' },
    ]"
    :distribution="distributionQc">
    <table>
      <thead><tr><th>شماره</th><th>مشخصه</th><th>رده</th><th>اسمی</th><th>حد پایین</th><th>حد بالا</th><th>واحد</th><th>ابزار</th><th>اندازه‌گیری‌شده</th><th>نتیجه</th></tr></thead>
      <tbody>
        <tr v-if="!fai.lines.length"><td colspan="10">مشخصه‌ای ثبت نشده.</td></tr>
        <tr v-for="l in fai.lines" :key="l.characteristicId">
          <td>{{ l.no }}</td><td>{{ l.name }}</td><td>{{ l.classText }}</td><td>{{ num(l.nominal) }}</td><td>{{ num(l.lower) }}</td><td>{{ num(l.upper) }}</td><td>{{ l.unit }}</td>
          <td class="inv-ltr">{{ l.equipmentCode }}</td><td>{{ l.measured === null || l.measured === undefined ? "" : num(l.measured) }}</td><td>{{ l.pass === null || l.pass === undefined ? "" : l.pass ? "قبول" : "مردود" }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
