<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { certificateResults, certificateTypes, distributionQc } from "@/composables/quality";

// Verification sheet of a material certificate (no company form code yet).
defineProps<{ cert: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="برگه بررسی گواهی مواد"
    :fields="[
      { label: 'کد کالا', value: cert.itemCode, ltr: true },
      { label: 'نام کالا', value: cert.itemTitle },
      { label: 'شماره بچ', value: cert.lotNumber, ltr: true },
      { label: 'شماره ذوب (Heat)', value: cert.heatNumber || '-', ltr: true },
      { label: 'نوع گواهی', value: certificateTypes[cert.type] },
      { label: 'شماره گواهی', value: cert.number, ltr: true },
      { label: 'صادرکننده', value: cert.issuer },
      { label: 'نتیجه بررسی', value: certificateResults[cert.result] },
    ]"
    :notes="[
      { label: 'خلاصه مشخصات گواهی', value: cert.summary || '-' },
      { label: 'توضیح بررسی', value: cert.verificationNote || '-' },
    ]"
    :signatures="[
      { title: 'ثبت‌کننده گواهی', name: cert.creatorName },
      { title: 'تایید کنترل کیفیت', hint: '(غیر از ثبت‌کننده)', name: cert.verifiedByName, date: jalali(cert.verifiedAt) },
    ]"
    :distribution="distributionQc" />
</template>
