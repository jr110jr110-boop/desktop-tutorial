<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, rial } from "@/composables/inventory";
import { contractLabel, distributionCommercial, milestoneKinds, milestoneStatuses, milestoneTriggers } from "@/composables/projectControl";

// Request to issue a payment statement for a milestone that has fallen due (no company form code yet).
defineProps<{ m: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="درخواست صدور صورت‌وضعیت"
    :fields="[
      { label: 'پروژه', value: contractLabel(m.contractId) },
      { label: 'ردیف مرحله', value: m.seq },
      { label: 'عنوان مرحله', value: m.title },
      { label: 'نوع', value: milestoneKinds[m.kind] },
      { label: 'محرک سررسید', value: milestoneTriggers[m.trigger] },
      { label: 'تاریخ سررسید', value: jalali(m.dueAt || m.plannedDate) },
      { label: 'مبلغ (ریال)', value: rial(m.amount) },
      { label: 'وضعیت', value: milestoneStatuses[m.status] },
      { label: 'شماره صورت‌وضعیت', value: m.provisionalStatementId, ltr: true },
      { label: 'مرجع مالی (فاکتور)', value: m.financeRef, ltr: true },
      { label: 'نامه کارفرما', value: m.clientLetterRef, ltr: true },
    ]"
    :signatures="[{ title: 'مدیر بازرگانی' }, { title: 'مدیر مالی' }]"
    :distribution="distributionCommercial" />
</template>
