<script setup lang="ts">
import { computed } from "vue";
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, rial } from "@/composables/inventory";
import { toWeeklyResources } from "@/api/ResourceAPIs";
import { faNum } from "@/composables/ceo";
import { resourceUnits } from "@/composables/resources";
import { contractLabel, distributionPlanning, idx, pct, phases } from "@/composables/projectControl";

// Weekly internal report (no company form code yet).
const props = defineProps<{ report: any }>();
// the section «منابع»: quantities of the project's resources, never amounts; a report made before the section existed has none
const resources = computed(() => toWeeklyResources(props.report?.auto?.resources ?? props.report?.c?.resources));
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
    <table v-if="resources" style="margin-top: 8px" data-test="weekly-print-resources">
      <thead><tr><th colspan="4">منابع</th></tr></thead>
      <tbody>
        <tr><th>بیش‌تخصیص باز</th><td>{{ faNum(resources.openOverloads, 0) }}</td><th>پذیرفته‌شده / گذشته بدون اصلاح</th><td>{{ faNum(resources.acceptedOverloads, 0) }} / {{ faNum(resources.lapsedOverloads, 0) }}</td></tr>
        <tr><th>طرح منابع منتظر تایید</th><td>{{ faNum(resources.plansWaitingApproval, 0) }}</td><th>منابع بدون نرخ مصوب</th><td>{{ faNum(resources.resourcesWithoutRate, 0) }}</td></tr>
        <tr v-if="resources.top.length"><th>پرکارترین منابع</th><td colspan="3"><span v-for="(t, i) in resources.top" :key="t.resourceId"><template v-if="i">، </template>{{ t.code }} {{ t.title }} ({{ faNum(t.load, 2) }} {{ resourceUnits[t.unit] }})</span></td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
