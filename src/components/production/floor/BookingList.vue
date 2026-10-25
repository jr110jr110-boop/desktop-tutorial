<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import BookingEditor from "./BookingEditor.vue";
import { getBookings } from "@/api/ProductionAPIs";
import { usePagedList } from "@/composables/fn";
import { bookingKinds, bookingSources, minutesHoursText, persianOf, qtyText } from "@/composables/production";
import { hasRole, ProductionRoles } from "@/composables/roles";
import { employeeLabel, loadWorkCenters, orderLabel, rowsOf, searchEmployees, searchOrders } from "./floorCommon";

// Bookings list. Readers see everybody; an operator gets only his own rows from the server, so the employee filter is hidden for him.
const canAll = hasRole(ProductionRoles.readers);
const canWrite = hasRole(ProductionRoles.supervisors);
const isSupervisor = hasRole(ProductionRoles.supervisor);

const employee = ref<any>(null);
const order = ref<any>(null);
const wcId = ref<number | null>(null);
const from = ref<string | null>(null);
const to = ref<string | null>(null);
const runningOnly = ref(false);
const workCenters = ref<any[]>([]);

const filters = computed(() => ({ EmployeeId: employee.value?.id ?? null, OrderId: order.value?.id ?? null, WorkCenterId: wcId.value, From: from.value, To: to.value, RunningOnly: runningOnly.value || null }));
const list = usePagedList(getBookings, filters, 50);
const editor = ref<null | { mode: "create" | "edit"; row?: any }>(null);

function saved() {
  editor.value = null;
  list.load();
}

onMounted(async () => {
  workCenters.value = await loadWorkCenters();
  list.load();
});
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <div v-if="canAll" style="min-width: 14rem"><LookupBox v-model="employee" :fetch="searchEmployees" :label="employeeLabel" placeholder="کارمند" /></div>
      <select v-model="wcId" class="form-select form-select-sm"><option :value="null">همه مراکز کار</option><option v-for="w in workCenters" :key="w.id" :value="w.id">{{ w.title }}</option></select>
      <div style="min-width: 14rem"><LookupBox v-model="order" :fetch="searchOrders" :label="orderLabel" placeholder="دستور تولید" /></div>
      <FnDate v-model="from" placeholder="از تاریخ" />
      <FnDate v-model="to" placeholder="تا تاریخ" />
      <label class="small"><input v-model="runningOnly" type="checkbox" /> فقط در حال اجرا</label>
      <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      <button v-if="canWrite" class="btn btn-sm btn-primary" @click="editor = { mode: 'create' }">ثبت زمان برای دیگری</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead>
          <tr><th>کارمند</th><th>دستور / مرحله</th><th>ماشین</th><th>نوع</th><th>شروع</th><th>پایان</th><th>مدت</th><th>سالم</th><th>ضایعات</th><th>دوباره‌کاری</th><th>منبع</th><th>وضعیت</th><th v-if="canWrite"></th></tr>
        </thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td :colspan="canWrite ? 13 : 12" class="text-center inv-muted">ثبت زمانی نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id">
            <td>{{ r.employeeName }}</td>
            <td><span class="inv-ltr">{{ r.jobCard || r.orderNumber }}</span> <small class="inv-muted">{{ r.operationTitle }}</small></td>
            <td>{{ r.machineTitle }}</td>
            <td>{{ bookingKinds[r.kind] }}</td>
            <td class="inv-ltr text-nowrap">{{ persianOf(r.startAtText, r.startAt, true) }}</td>
            <td class="inv-ltr text-nowrap">{{ r.running ? "در حال اجرا" : persianOf(r.endAtText, r.endAt, true) }}</td>
            <td class="fn-num">{{ minutesHoursText(r.minutes) }}</td>
            <td class="fn-num">{{ qtyText(r.goodQty) }}</td>
            <td class="fn-num">{{ qtyText(r.scrapQty) }}</td>
            <td class="fn-num">{{ qtyText(r.reworkQty) }}</td>
            <td><span :title="r.supervisorReason || r.editReason">{{ bookingSources[r.source] }}</span></td>
            <td><span v-if="r.running" class="inv-badge info">در حال اجرا</span><span v-else-if="r.dayClosed" class="inv-badge warn" title="روز بسته شده؛ تغییر فقط توسط سرپرست کارگاه">روز بسته</span><span v-if="r.costPosted" class="inv-badge ok ms-1" title="هزینه این ثبت در حسابداری ثبت شده و اصلاح نمی‌شود">هزینه ثبت شده</span></td>
            <td v-if="canWrite" class="text-nowrap">
              <button v-if="!r.running && !r.costPosted && (!r.dayClosed || isSupervisor)" class="btn btn-sm btn-outline-primary" @click="editor = { mode: 'edit', row: r }">{{ r.dayClosed ? "اصلاح روز بسته" : "ویرایش" }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
    <p class="inv-muted small mb-0">ثبت‌هایی که هزینه آن‌ها در حسابداری ثبت شده ویرایش نمی‌شوند؛ سامانه هنگام ویرایش پیام لازم را نشان می‌دهد.</p>
    <BookingEditor v-if="editor" :mode="editor.mode" :row="editor.row" @close="editor = null" @saved="saved" />
  </div>
</template>
