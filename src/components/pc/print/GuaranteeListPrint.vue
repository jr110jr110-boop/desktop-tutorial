<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, rial } from "@/composables/inventory";
import { contractLabel, distributionCommercial, guaranteeIssuers, guaranteeReleases, guaranteeStatuses, guaranteeTypes } from "@/composables/projectControl";

// Status list of the guarantees of a project (no company form code yet).
defineProps<{ rows: any[]; contractId: number | null }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="فهرست وضعیت ضمانت‌نامه‌ها"
    :fields="[{ label: 'پروژه', value: contractLabel(contractId) }, { label: 'تعداد', value: rows.length }]"
    :signatures="[{ title: 'مدیر بازرگانی' }, { title: 'مدیر مالی' }]"
    :distribution="distributionCommercial">
    <table>
      <thead><tr><th>ردیف</th><th>ضمانت‌نامه</th><th>شماره</th><th>نوع</th><th>صادرکننده</th><th>مبلغ (ریال)</th><th>پایان اعتبار</th><th>روز مانده</th><th>شرط آزادسازی</th><th>وضعیت</th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="10">ضمانت‌نامه‌ای ثبت نشده.</td></tr>
        <tr v-for="(r, i) in rows" :key="r.id">
          <td>{{ i + 1 }}</td><td>{{ r.title }} ({{ r.bank }})</td><td class="inv-ltr">{{ r.no }}</td><td>{{ guaranteeTypes[r.guaranteeType] }}</td><td>{{ guaranteeIssuers[r.issuerKind] }}</td>
          <td>{{ r.amountDecimal != null ? rial(r.amountDecimal) : "-" }}</td><td>{{ jalali(r.expiryDate) }}</td><td>{{ r.daysToExpiry }}</td><td>{{ guaranteeReleases[r.releaseTrigger] }}</td><td>{{ guaranteeStatuses[r.status] }}</td>
        </tr>
      </tbody>
    </table>
  </PrintForm>
</template>
