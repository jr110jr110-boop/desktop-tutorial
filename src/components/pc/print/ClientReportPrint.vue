<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { contractLabel, distributionDcc, idx, milestoneLine, pct, phases } from "@/composables/projectControl";

// Monthly progress report for the client (no company form code yet). SPI and CPI appear only when the API put them in the content (contract terms allow it).
defineProps<{ report: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش پیشرفت ماهانه پروژه"
    :fields="[
      { label: 'شماره گزارش', value: report.number, ltr: true },
      { label: 'پروژه', value: report.c?.contractTitle || contractLabel(report.contractId) },
      { label: 'کد پروژه', value: report.c?.projectCode, ltr: true },
      { label: 'تاریخ داده', value: jalali(report.dataDate) },
      { label: 'پیشرفت واقعی', value: pct(report.c?.physicalPercent) },
      { label: 'پیشرفت برنامه‌ای', value: pct(report.c?.plannedPercent) },
      { label: 'پایان خط مبنا', value: jalali(report.c?.baselineFinish) },
      { label: 'پایان پیش‌بینی', value: jalali(report.c?.forecastFinish) },
      ...(report.c?.spi != null ? [{ label: 'SPI', value: idx(report.c.spi) }] : []),
      ...(report.c?.cpi != null ? [{ label: 'CPI', value: idx(report.c.cpi) }] : []),
      { label: 'مدارک ارسال‌شده / تاییدشده', value: `${report.c?.documentsSubmitted ?? 0} / ${report.c?.documentsApproved ?? 0}` },
      { label: 'پاسخ‌های کارفرما از مهلت گذشته', value: report.c?.documentsAwaitingResponse ?? 0 },
    ]"
    :signatures="[
      { title: 'مدیر برنامه‌ریزی و کنترل پروژه', date: jalali(report.generatedAt) },
      { title: 'مدیر پروژه (انتشار گزارش)', date: jalali(report.releasedAt) },
    ]"
    :distribution="distributionDcc">
    <table>
      <thead><tr><th>فاز</th><th>پیشرفت واقعی</th><th>پیشرفت برنامه‌ای</th><th>تعداد فعالیت</th></tr></thead>
      <tbody>
        <tr v-for="(p, i) in report.c?.phases || []" :key="i"><td>{{ phases[p.phase] || "—" }}</td><td>{{ pct(p.percent) }}</td><td>{{ pct(p.plannedPercent) }}</td><td>{{ p.activities }}</td></tr>
      </tbody>
    </table>
    <table v-if="report.c?.milestones?.length" style="margin-top: 8px">
      <thead><tr><th>مراحل پرداخت</th></tr></thead>
      <tbody><tr v-for="(m, i) in report.c.milestones" :key="i"><td>{{ milestoneLine(m) }}</td></tr></tbody>
    </table>
    <p v-if="!report.releasedAt" style="margin-top: 8px">این گزارش هنوز توسط مدیر پروژه منتشر نشده است.</p>
  </PrintForm>
</template>
