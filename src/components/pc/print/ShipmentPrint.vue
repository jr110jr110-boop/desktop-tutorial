<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { contractLabel, distributionShipping, shipmentStatuses } from "@/composables/projectControl";

// Packing list of a shipment (no company form code yet). The signatures follow the gates of the API: packing, quality release note, planning release, receipt.
defineProps<{ s: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="فهرست بسته‌بندی و برگه ارسال محموله"
    :fields="[
      { label: 'شماره محموله', value: s.number, ltr: true },
      { label: 'پروژه', value: contractLabel(s.contractId) },
      { label: 'دستور ساخت', value: s.workOrderCode, ltr: true },
      { label: 'وضعیت', value: shipmentStatuses[s.status] },
      { label: 'برگه ترخیص کیفی', value: s.releaseNoteId ? '#' + s.releaseNoteId : '' },
      { label: 'بارنامه', value: s.waybill, ltr: true },
      { label: 'تاریخ ارسال', value: jalali(s.shippedAt) },
      { label: 'رسید تحویل', value: s.deliveryReceiptNumber, ltr: true },
      { label: 'تاریخ تحویل', value: jalali(s.deliveredAt) },
      { label: 'صورتجلسه تحویل', value: s.handoverMinutesNumber, ltr: true },
    ]"
    :notes="[
      { label: 'نتیجه بازبینی بسته‌بندی', value: s.packingVerification },
      { label: 'اسناد حمل', value: s.shippingDocuments },
    ]"
    :signatures="[{ title: 'سرپرست انبار (بسته‌بندی)' }, { title: 'مدیر کنترل کیفیت (برگه ترخیص)' }, { title: 'مدیر برنامه‌ریزی و کنترل پروژه (مجوز ارسال)' }, { title: 'تحویل‌گیرنده' }]"
    :distribution="distributionShipping">
    <table>
      <thead><tr><th>ردیف</th><th>شرح اقلام بسته‌بندی‌شده</th></tr></thead>
      <tbody><tr v-for="(line, i) in String(s.packingList || '').split('\n').filter((x) => x.trim())" :key="i"><td>{{ i + 1 }}</td><td>{{ line }}</td></tr></tbody>
    </table>
  </PrintForm>
</template>
