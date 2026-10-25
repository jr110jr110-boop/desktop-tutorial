<script setup lang="ts">
// Maintenance KPIs per machine (MTBF, MTTR, availability) for a Persian date range.
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import FnDate from "@/components/fn/FnDate.vue";
import { getMachineKpis } from "@/api/ProductionAPIs";
import { usePagedList } from "@/composables/fn";
import { dateText, hoursText, percentText } from "@/composables/production";
import { lastDays } from "./mntCommon";

const props = defineProps({ machines: { type: Array as () => any[], default: () => [] } });
const range = lastDays(90);
const filters = ref<any>({ From: range.from, To: range.to, MachineId: null });
const list = usePagedList(getMachineKpis, filters);
onMounted(list.load);
</script>

<template>
  <div>
    <div class="inv-filters mb-3 align-items-end">
      <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
      <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
      <select v-model="filters.MachineId" class="form-select form-select-sm">
        <option :value="null">همه ماشین‌ها</option>
        <option v-for="m in props.machines" :key="m.machineId" :value="m.machineId">{{ m.title }}</option>
      </select>
      <button class="btn btn-sm btn-secondary" @click="list.search">نمایش</button>
    </div>
    <p class="inv-muted small">دوره: {{ dateText(filters.From) || "-" }} تا {{ dateText(filters.To) || "-" }}</p>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>ماشین</th><th>مرکز کار</th><th>ساعت برنامه‌ای</th><th>ساعت توقف</th><th>دسترس‌پذیری</th><th>تعداد خرابی</th><th>MTBF (ساعت)</th><th>MTTR (ساعت)</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">داده‌ای نیست.</td></tr>
          <tr v-for="k in list.rows.value" :key="k.machineId">
            <td>{{ k.machineTitle }}</td><td class="inv-ltr">{{ k.workCenterCode }}</td><td>{{ hoursText(k.plannedHours) }}</td><td>{{ hoursText(k.downtimeHours) }}</td>
            <td>{{ percentText(k.availabilityPercent) }}</td><td>{{ k.breakdowns }}</td>
            <td>{{ k.mtbfHours === null ? "-" : hoursText(k.mtbfHours) }}</td><td>{{ k.mttrHours === null ? "-" : hoursText(k.mttrHours) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    <p class="inv-muted small mb-0">MTBF = ساعت کارکرد ÷ تعداد خرابی؛ MTTR = ساعت تعمیر ÷ خرابی‌های بسته‌شده. بدون خرابی، MTBF خالی است.</p>
  </div>
</template>
