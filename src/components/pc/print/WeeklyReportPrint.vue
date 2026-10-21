<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, rial } from "@/composables/inventory";
import { contractLabel, distributionPlanning, idx, pct, phases } from "@/composables/projectControl";

// Weekly internal report (no company form code yet).
defineProps<{ report: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="گزارش هفتگی داخلی پروژه"
    :fields="[
      { label: 'شماره گزارش', value: report.number, ltr: true },
      { label: 'پروژه', value: contractLabel(report.contractId) },
      { label: 'تاریخ داده', value: jalali(report.dataDate) },
      { label: 'پیشرفت واقعی', value: pct(report.c?.progress?.physicalPercent) },
      { label: 'پیشرفت برنامه‌ای', value: pct(report.c?.progress?.plannedPercent) },
      { label: 'SPI', value: idx(report.c?.evm?.spi) },
      { label: 'CPI', value: idx(report.c?.evm?.cpi) },
      { label: 'پایان پیش‌بینی', value: jalali(report.c?.evm?.forecastFinish) },
      { label: 'فعالیت‌های دارای تاخیر', value: report.c?.lateActivities?.length ?? 0 },
      { label: 'تغییرات باز', value: report.c?.openChanges ?? 0 },
      { label: 'هشدار ضمانت‌نامه', value: report.c?.guaranteeAlerts ?? 0 },
    ]"
    :notes="[{ label: 'کد فعالیت‌های دارای تاخیر', value: (report.c?.lateActivities || []).join('، ') || '-' }]"
    :signatures="[
      { title: 'مدیر برنامه‌ریزی و کنترل پروژه', date: jalali(report.generatedAt) },
      { title: 'مدیر پروژه', date: jalali(report.releasedAt) },
    ]"
    :distribution="distributionPlanning">
    <table>
      <thead><tr><th>فاز</th><th>واقعی</th><th>برنامه‌ای</th></tr></thead>
      <tbody><tr v-for="(p, i) in report.c?.progress?.byPhase || []" :key="i"><td>{{ phases[p.phase] || "—" }}</td><td>{{ pct(p.percent) }}</td><td>{{ pct(p.plannedPercent) }}</td></tr></tbody>
    </table>
    <table v-if="report.c?.evm" style="margin-top: 8px">
      <thead><tr><th>BAC</th><th>PV</th><th>EV</th><th>AC</th><th>EAC</th><th>VAC</th></tr></thead>
      <tbody><tr><td>{{ rial(report.c.evm.bac) }}</td><td>{{ rial(report.c.evm.pv) }}</td><td>{{ rial(report.c.evm.ev) }}</td><td>{{ rial(report.c.evm.ac) }}</td><td>{{ report.c.evm.eacTypical != null ? rial(report.c.evm.eacTypical) : "-" }}</td><td>{{ report.c.evm.vacTypical != null ? rial(report.c.evm.vacTypical) : "-" }}</td></tr></tbody>
    </table>
  </PrintForm>
</template>
