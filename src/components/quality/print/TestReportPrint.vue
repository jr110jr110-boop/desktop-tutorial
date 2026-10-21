<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionQc, qualityFormCodes, releaseParties, reportResults, reportTypes } from "@/composables/quality";

// FO-QC-016 (PT) and FO-QC-017 (MT) have company codes; every other test report type has none yet.
const props = defineProps<{ rep: any }>();
const code = () => (props.rep.type === 1 ? qualityFormCodes.pt : props.rep.type === 2 ? qualityFormCodes.mt : "");
</script>

<template>
  <PrintForm
    class="q-form"
    :title="'گزارش آزمون - ' + reportTypes[rep.type]"
    :form-code="code()"
    :fields="[
      { label: 'شماره گزارش', value: rep.number, ltr: true },
      { label: 'تاریخ', value: jalali(rep.reportDate) },
      { label: 'دستور ساخت', value: rep.workOrderCode || '-' },
      { label: 'کد کالا / قطعه', value: rep.itemCode || '-', ltr: true },
      { label: 'سریال', value: rep.serialNumber || '-', ltr: true },
      { label: 'روش اجرایی / ویرایش', value: (rep.procedureRef || '-') + ' / ' + (rep.procedureRevision || '-'), ltr: true },
      { label: 'ویرایش آیین‌نامه', value: rep.codeEdition || '-', ltr: true },
      { label: 'آزمونگر', value: rep.examinerName },
      { label: 'گواهی آزمونگر', value: rep.examinerQualificationNumber || '-', ltr: true },
      { label: 'شرایط محیطی', value: rep.environment || '-' },
      { label: 'نتیجه', value: reportResults[rep.result] },
      { label: 'NCR مرتبط', value: rep.ncrNumber || '-', ltr: true },
    ]"
    :notes="[{ label: 'توضیحات', value: rep.note || '-' }]"
    :signatures="[
      { title: 'آزمونگر (دارای صلاحیت)', name: rep.examinerName, date: jalali(rep.submittedAt) },
      { title: 'تایید مدیر کنترل کیفیت', hint: '(غیر از آزمونگر)', name: rep.approvedByName, date: jalali(rep.approvedAt) },
    ]"
    :distribution="distributionQc">
    <table v-if="rep.values.length">
      <thead><tr><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>مقدار اندازه‌گیری</th><th>نتیجه</th></tr></thead>
      <tbody>
        <tr v-for="v in rep.values" :key="v.id">
          <td>{{ v.parameter }}</td><td>{{ v.unit }}</td><td>{{ v.min ?? "-" }}</td><td>{{ v.max ?? "-" }}</td><td>{{ v.measured ?? "" }}</td>
          <td>{{ v.pass === true ? "قبول" : v.pass === false ? "مردود" : "-" }}</td>
        </tr>
      </tbody>
    </table>
    <table v-if="rep.indications.length" class="mt-1">
      <thead><tr><th>ردیف</th><th>محل</th><th>نوع (خطی / گرد)</th><th>اندازه</th><th>جهت</th><th>پذیرفته</th></tr></thead>
      <tbody>
        <tr v-for="(x, i) in rep.indications" :key="x.id"><td>{{ i + 1 }}</td><td>{{ x.location }}</td><td>{{ x.type }}</td><td>{{ x.size }}</td><td>{{ x.orientation }}</td><td>{{ x.accepted ? "بله" : "خیر" }}</td></tr>
      </tbody>
    </table>
    <table v-if="rep.equipment.length" class="mt-1">
      <thead><tr><th>تجهیز</th><th>نام</th><th>سررسید کالیبراسیون</th></tr></thead>
      <tbody><tr v-for="e in rep.equipment" :key="e.equipmentId"><td class="inv-ltr">{{ e.code }}</td><td>{{ e.name }}</td><td>{{ jalali(e.nextDue) }}</td></tr></tbody>
    </table>
    <table v-if="rep.witnesses.length" class="mt-1">
      <thead><tr><th>شاهد</th><th>طرف</th><th>تاریخ</th></tr></thead>
      <tbody><tr v-for="(w, i) in rep.witnesses" :key="i"><td>{{ w.name }}</td><td>{{ releaseParties[w.party] }}</td><td>{{ jalali(w.witnessDate) }}</td></tr></tbody>
    </table>
  </PrintForm>
</template>
