<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, rial } from "@/composables/inventory";
import { contractLabel, distributionPlanning, idx, pct } from "@/composables/projectControl";

// Monthly report for the CEO (no company form code yet).
defineProps<{ report: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش ماهانه مدیرعامل - کنترل پروژه"
    :fields="[
      { label: 'شماره گزارش', value: report.number, ltr: true },
      { label: 'پروژه', value: contractLabel(report.contractId) },
      { label: 'کد پروژه', value: report.c?.row?.projectCode, ltr: true },
      { label: 'تاریخ داده', value: jalali(report.dataDate) },
      { label: 'پیشرفت واقعی', value: pct(report.c?.row?.physicalPercent) },
      { label: 'پیشرفت برنامه‌ای', value: pct(report.c?.row?.plannedPercent) },
      { label: 'SPI', value: idx(report.c?.row?.spi) },
      { label: 'CPI', value: idx(report.c?.row?.cpi) },
      { label: 'پایان پیش‌بینی', value: jalali(report.c?.row?.forecastFinish) },
      { label: 'فعالیت تاخیردار', value: report.c?.row?.lateActivities },
      { label: 'تغییرات باز', value: report.c?.row?.openChanges },
      { label: 'هشدار ضمانت‌نامه', value: report.c?.row?.guaranteeAlerts },
      { label: 'مرحله پرداخت سررسیدشده', value: report.c?.row?.overdueMilestones },
      { label: 'گام شروع از مهلت گذشته', value: report.c?.row?.overdueInitiationSteps },
      { label: 'پاسخ کارفرما از مهلت گذشته', value: report.c?.row?.overdueClientResponses },
      { label: 'جریمه محاسبه‌شده (ریال)', value: rial(report.c?.row?.penaltyExposure) },
    ]"
    :signatures="[
      { title: 'مدیر برنامه‌ریزی و کنترل پروژه', date: jalali(report.generatedAt) },
      { title: 'مدیر پروژه (انتشار گزارش)', date: jalali(report.releasedAt) },
      { title: 'مدیرعامل (رویت)' },
    ]"
    :distribution="distributionPlanning">
    <table v-if="report.c?.evm">
      <thead><tr><th>BAC</th><th>PV</th><th>EV</th><th>AC</th><th>EAC</th><th>ETC</th><th>VAC</th></tr></thead>
      <tbody><tr><td>{{ rial(report.c.evm.bac) }}</td><td>{{ rial(report.c.evm.pv) }}</td><td>{{ rial(report.c.evm.ev) }}</td><td>{{ rial(report.c.evm.ac) }}</td><td>{{ report.c.evm.eacTypical != null ? rial(report.c.evm.eacTypical) : "-" }}</td><td>{{ report.c.evm.etcTypical != null ? rial(report.c.evm.etcTypical) : "-" }}</td><td>{{ report.c.evm.vacTypical != null ? rial(report.c.evm.vacTypical) : "-" }}</td></tr></tbody>
    </table>
  </PrintForm>
</template>
