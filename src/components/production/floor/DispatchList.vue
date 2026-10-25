<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { getDispatch } from "@/api/ProductionAPIs";
import { usePagedList } from "@/composables/fn";
import { badgeOfOperation, dispatchStages, hoursText, operationStatuses, persianOf, qtyText } from "@/composables/production";
import { loadWorkCenters } from "./floorCommon";

// Dispatch list of one work centre: Ready, Running and Next operations in priority order, with the printable A4 job cards.
const workCenters = ref<any[]>([]);
const wcId = ref<number | null>(null);
const filters = ref<any>({ WorkCenterId: null });
const list = usePagedList(getDispatch, filters, 100);
const emit = defineEmits(["print"]);

const title = () => workCenters.value.find((w) => w.id === wcId.value)?.title || "";
function show() {
  if (!wcId.value) return;
  filters.value = { WorkCenterId: wcId.value };
  list.search();
}
function printAll() {
  emit("print", list.rows.value.slice(), title());
}
onMounted(async () => {
  workCenters.value = await loadWorkCenters();
});
</script>

<template>
  <div>
    <div class="inv-filters mb-3 screen-only">
      <select v-model="wcId" class="form-select form-select-sm" @change="show"><option :value="null">مرکز کار را انتخاب کنید</option><option v-for="w in workCenters" :key="w.id" :value="w.id">{{ w.title }}</option></select>
      <button class="btn btn-sm btn-secondary" @click="show">نمایش</button>
      <button :disabled="!list.rows.value.length" class="btn btn-sm btn-primary" @click="printAll">چاپ کارت کار</button>
    </div>
    <div class="table-responsive screen-only">
      <table class="table table-sm table-hover">
        <thead><tr><th>اولویت</th><th>نوبت</th><th>برگه کار</th><th>کالا</th><th>مرحله</th><th>تعداد</th><th>راه‌اندازی</th><th>تولید</th><th>شروع برنامه</th><th>پایان برنامه</th><th>نیاز</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="12" class="text-center inv-muted">{{ wcId ? "کاری در صف این مرکز نیست." : "یک مرکز کار انتخاب کنید." }}</td></tr>
          <tr v-for="r in list.rows.value" :key="r.operationId" :class="{ 'table-danger': r.orderLate }">
            <td>{{ r.priority ?? "-" }}</td>
            <td>{{ dispatchStages[r.stage] || r.stage }}</td>
            <td class="inv-ltr">{{ r.jobCard }}</td>
            <td>{{ r.itemTitle || r.itemCode }}</td>
            <td>{{ r.sequence }} - {{ r.title }}</td>
            <td class="fn-num">{{ qtyText(r.quantity) }} {{ r.unit }}</td>
            <td class="fn-num">{{ hoursText(r.setupHoursPlanned) }}</td>
            <td class="fn-num">{{ hoursText(r.runHoursPlanned) }}</td>
            <td class="inv-ltr">{{ persianOf(r.plannedStartText, r.plannedStart) }}</td>
            <td class="inv-ltr">{{ persianOf(r.plannedFinishText, r.plannedFinish) }}</td>
            <td class="inv-ltr">{{ r.needDateText }}</td>
            <td><span :class="badgeOfOperation(r.status)" class="inv-badge">{{ operationStatuses[r.status] }}</span></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager class="screen-only" :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
  </div>
</template>
