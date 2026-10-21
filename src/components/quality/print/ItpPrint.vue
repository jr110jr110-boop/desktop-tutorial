<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { activityPoints, activityStatuses, distributionQc, itpStatuses, testTypes } from "@/composables/quality";

// Inspection and test plan of a work order (no company form code yet).
defineProps<{ itp: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="برنامه بازرسی و آزمون (ITP)"
    :fields="[
      { label: 'شماره', value: itp.number, ltr: true },
      { label: 'ویرایش', value: itp.revision, ltr: true },
      { label: 'دستور ساخت', value: itp.workOrderCode },
      { label: 'قرارداد', value: itp.contractCode },
      { label: 'وضعیت', value: itpStatuses[itp.status] },
      { label: 'قالب مبنا', value: itp.templateCode || '-', ltr: true },
      { label: 'مرجع تایید کارفرما', value: itp.clientApprovalReference || '-', ltr: true },
      { label: 'تاریخ تایید کارفرما', value: jalali(itp.clientApprovalDate) || '-' },
      { label: 'مهلت اعلام بازرسی (روز)', value: itp.noticeDays },
    ]"
    :signatures="[{ title: 'تهیه‌کننده (کنترل کیفیت)' }, { title: 'مهندسی (حدود پذیرش)' }, { title: 'تایید کارفرما / بازرس ثالث', hint: itp.clientApprovalReference || '' }]"
    :distribution="distributionQc">
    <table>
      <thead><tr><th>ردیف</th><th>مرحله</th><th>فعالیت</th><th>نوع آزمون</th><th>مرجع پذیرش</th><th>نقاط کنترل (V/TPI/C)</th><th>مدرک تولیدی</th><th>وضعیت</th></tr></thead>
      <tbody>
        <tr v-for="a in itp.activities" :key="a.id">
          <td class="inv-ltr">{{ a.itemNo }}</td><td>{{ a.stage }}</td><td>{{ a.activity }}</td><td>{{ testTypes[a.testType] }}</td><td>{{ a.acceptanceReference }}</td>
          <td class="inv-ltr">{{ activityPoints(a) }}</td><td>{{ a.documentProduced }}</td><td>{{ activityStatuses[a.status] }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
