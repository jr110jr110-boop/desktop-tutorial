<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { complaintStatuses, distributionQa, severities } from "@/composables/quality";

// Customer complaint form (no company form code yet).
defineProps<{ c: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="فرم ثبت و پیگیری شکایت مشتری"
    :fields="[
      { label: 'شماره', value: c.number, ltr: true },
      { label: 'تاریخ دریافت', value: jalali(c.receivedDate) },
      { label: 'کانال', value: c.channel || '-' },
      { label: 'دستور ساخت', value: c.workOrderCode || '-' },
      { label: 'شدت', value: severities[c.severity] },
      { label: 'وضعیت', value: complaintStatuses[c.status] },
      { label: 'مهلت پاسخ', value: jalali(c.responseDue) },
      { label: 'تاریخ پاسخ', value: jalali(c.respondedAt) || '-' },
      { label: 'NCR مرتبط', value: c.ncrNumber || '-', ltr: true },
      { label: 'CAPA مرتبط', value: c.capaNumber || '-', ltr: true },
    ]"
    :notes="[
      { label: 'شرح شکایت', value: c.description },
      { label: 'پاسخ به مشتری', value: c.response || '-' },
    ]"
    :signatures="[{ title: 'ثبت‌کننده شکایت' }, { title: 'مدیر تضمین کیفیت', date: jalali(c.closedAt) }]"
    :distribution="distributionQa" />
</template>
