<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionQc, wpsStatuses } from "@/composables/quality";

// WPS (with its PQRs) or a single PQR; none has a company form code yet.
defineProps<{ wps?: any; pqr?: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    v-if="wps"
    title="دستورالعمل جوشکاری (WPS)"
    :fields="[
      { label: 'شماره', value: wps.number, ltr: true },
      { label: 'ویرایش', value: wps.revision, ltr: true },
      { label: 'فرایند', value: wps.process },
      { label: 'گروه فلز پایه (P-No)', value: wps.basePNo },
      { label: 'فلز پرکننده (F-No / A-No)', value: (wps.fillerFNo || '-') + ' / ' + (wps.fillerANo || '-') },
      { label: 'ضخامت (mm)', value: (wps.thicknessMin ?? '-') + ' تا ' + (wps.thicknessMax ?? '-') },
      { label: 'وضعیت‌های جوشکاری', value: wps.positions || '-' },
      { label: 'پیشگرم حداقل (C)', value: wps.preheatMinC ?? '-' },
      { label: 'بین‌پاس حداکثر (C)', value: wps.interpassMaxC ?? '-' },
      { label: 'PWHT', value: wps.pwht || '-' },
      { label: 'گاز محافظ', value: wps.shieldingGas || '-' },
      { label: 'وضعیت', value: wpsStatuses[wps.status] },
    ]"
    :notes="[{ label: 'توضیحات', value: wps.note || '-' }]"
    :signatures="[
      { title: 'تایید کنترل کیفیت', name: wps.qcApprovedByName, date: jalali(wps.qcApprovedAt) },
      { title: 'تایید مهندسی', hint: '(غیر از تایید‌کننده QC و تهیه‌کننده)', name: wps.engineeringApprovedByName, date: jalali(wps.engineeringApprovedAt) },
    ]"
    :distribution="distributionQc">
    <table>
      <thead><tr><th>PQR</th><th>ویرایش آیین‌نامه</th><th>فرایند</th><th>تایید مهندسی</th></tr></thead>
      <tbody>
        <tr v-if="!(wps.pqrs || []).length"><td colspan="4">PQR مرتبطی ثبت نشده.</td></tr>
        <tr v-for="p in wps.pqrs || []" :key="p.id"><td class="inv-ltr">{{ p.number }}</td><td class="inv-ltr">{{ p.codeEdition }}</td><td>{{ p.process }}</td><td>{{ p.approved ? p.approvedByName + " - " + jalali(p.approvedAt) : "تایید نشده" }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
  <PrintForm
    class="q-form"
    v-else-if="pqr"
    title="گزارش تایید صلاحیت روش جوشکاری (PQR)"
    :fields="[
      { label: 'شماره', value: pqr.number, ltr: true },
      { label: 'ویرایش آیین‌نامه', value: pqr.codeEdition, ltr: true },
      { label: 'فرایند', value: pqr.process },
      { label: 'وضعیت', value: pqr.approved ? 'تاییدشده' : 'تایید نشده' },
    ]"
    :notes="[{ label: 'توضیحات', value: pqr.note || '-' }]"
    :signatures="[{ title: 'تایید مهندسی', hint: '(غیر از ثبت‌کننده)', name: pqr.approvedByName, date: jalali(pqr.approvedAt) }]"
    :distribution="distributionQc" />
</template>
