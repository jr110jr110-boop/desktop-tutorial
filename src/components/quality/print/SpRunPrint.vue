<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, num } from "@/composables/inventory";
import { distributionQc, spReviewStatuses, spRunResults } from "@/composables/quality";

// Run record of a special process (Package C): the qualification and its procedure, the furnace or line, the operator, the instruments, every parameter with its limits and the verdict of the server,
// the chart and certificate references and the two-person review. A4, B Zar through PrintForm.
defineProps<{ run: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="برگه ثبت اجرای فرایند ویژه"
    :fields="[
      { label: 'شماره', value: run.number, ltr: true },
      { label: 'فرایند', value: run.typeTitle },
      { label: 'صلاحیت فرایند', value: run.qualificationNumber || (run.isSubcontract ? 'پیمانکار' : '-'), ltr: true },
      { label: 'روش اجرایی', value: run.procedureText || '-', ltr: true },
      { label: 'دستور تولید', value: run.orderNumber || '-', ltr: true },
      { label: 'گام', value: run.operationText || '-', ltr: true },
      { label: 'قطعه', value: (run.itemCode || '') + ' ' + (run.itemTitle || '') },
      { label: 'تعداد', value: num(run.quantity) },
      { label: 'بچ / سریال', value: [run.lotNumber, run.serialNumbers].filter(Boolean).join(' / ') || '-', ltr: true },
      { label: run.isSubcontract ? 'پیمانکار' : 'دستگاه (کوره/خط)', value: run.isSubcontract ? run.supplierName : run.machineTitle },
      { label: 'اپراتور', value: run.isSubcontract ? '-' : (run.operatorName || '-') + (run.operatorStamp ? ' (' + run.operatorStamp + ')' : '') },
      { label: 'آغاز / پایان', value: jalali(run.startedAt) + ' تا ' + jalali(run.endedAt) },
      { label: 'نتیجه', value: spRunResults[run.result] },
      { label: 'بازبینی', value: spReviewStatuses[run.reviewStatus] },
    ]"
    :notes="[
      { label: 'ابزارهای اندازه‌گیری', value: (run.instruments || []).map((i: any) => i.code + ' ' + (i.title || '')).join('، ') || '-' },
      { label: 'نمودار / گواهی', value: (run.chartFileName || (run.chartDocumentId ? 'نمودار پیوست شده' : 'بدون نمودار')) + (run.certificateNumber ? ' | گواهی ' + run.certificateNumber : '') },
      { label: 'عدم انطباق', value: run.ncrNumber || '-' },
      { label: 'توضیح', value: run.note || '-' },
    ]"
    :signatures="[
      { title: 'ثبت‌کننده اجرا', name: run.creatorName, date: jalali(run.createDate) },
      { title: 'بازبین QA/QC', hint: '(غیر از ثبت‌کننده و اپراتور)', name: run.reviewedByName, date: run.reviewedAt ? jalali(run.reviewedAt) : '' },
    ]"
    :distribution="distributionQc">
    <table>
      <thead><tr><th>پارامتر</th><th>واحد</th><th>حداقل</th><th>حداکثر</th><th>مقدار ثبت‌شده</th><th>حکم</th></tr></thead>
      <tbody>
        <tr v-if="!(run.values || []).length"><td colspan="6">پارامتری ثبت نشده.</td></tr>
        <tr v-for="(v, i) in run.values || []" :key="i">
          <td>{{ v.name }}</td><td>{{ v.unit }}</td><td>{{ v.min === null || v.min === undefined ? "-" : num(v.min) }}</td><td>{{ v.max === null || v.max === undefined ? "-" : num(v.max) }}</td><td>{{ num(v.value) }}</td><td>{{ v.pass ? "قبول" : "مردود" }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
