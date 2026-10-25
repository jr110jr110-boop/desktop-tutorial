<script setup lang="ts">
import { computed, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { getAttendanceDifferences } from "@/api/ProductionAPIs";
import { camelize, parseAmount, usePagedList } from "@/composables/fn";
import { fail } from "@/composables/inventory";
import { differenceKinds, hoursText, persianOf } from "@/composables/production";
import { employeeLabel, searchEmployees } from "./floorCommon";

// Attendance hours against booked hours per employee and day. Differences above the tolerance are listed by the server; the page marks the large ones.
const from = ref<string | null>(null);
const to = ref<string | null>(null);
const tolerance = ref<string>("15");
const picked = ref<any[]>([]);
const adding = ref<any>(null);
const asked = ref(false);

const filters = computed(() => ({ From: from.value, To: to.value, ToleranceMinutes: parseAmount(tolerance.value) ?? 15, EmployeeIds: picked.value.map((p) => p.id) }));
const list = usePagedList(async (p) => {
  if (!p.From || !p.To) return [null, new Error("بازه تاریخ را وارد کنید.")];
  return getAttendanceDifferences(p);
}, filters, 50);

function add(e: any) {
  if (e && !picked.value.some((p) => p.id === e.id)) picked.value.push(e);
  adding.value = null;
}
async function search() {
  asked.value = true;
  if (!from.value || !to.value) return fail({ message: "بازه تاریخ را وارد کنید." });
  list.search();
}
const toleranceHours = computed(() => (parseAmount(tolerance.value) ?? 15) / 60);
const strong = (r: any) => Math.abs(Number(r.differenceHours) || 0) > toleranceHours.value * 2;
void camelize;
</script>

<template>
  <div>
    <div class="inv-filters mb-2">
      <FnDate v-model="from" placeholder="از تاریخ" />
      <FnDate v-model="to" placeholder="تا تاریخ" />
      <input v-model="tolerance" class="form-control form-control-sm fn-num" placeholder="تلورانس (دقیقه)" style="max-width: 9rem" title="تلورانس به دقیقه" />
      <div style="min-width: 15rem"><LookupBox v-model="adding" :fetch="searchEmployees" :label="employeeLabel" placeholder="افزودن کارمند به بررسی" @update:model-value="add" /></div>
      <button class="btn btn-sm btn-secondary" @click="search">بررسی</button>
    </div>
    <div v-if="picked.length" class="mb-2">
      <span v-for="p in picked" :key="p.id" class="inv-badge info me-1">{{ p.name }} <a href="#" @click.prevent="picked = picked.filter((x) => x.id !== p.id)">×</a></span>
      <span class="inv-muted small">(بدون انتخاب: همه کارمندانی که ثبت زمان یا حضور دارند)</span>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>کارمند</th><th>تاریخ</th><th>حضور</th><th>زمان ثبت‌شده</th><th>اختلاف</th><th>نوع اختلاف</th><th>توضیح</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">{{ asked ? "اختلافی بیش از تلورانس نیست." : "بازه را انتخاب و بررسی کنید." }}</td></tr>
          <tr v-for="(r, i) in list.rows.value" :key="i" :class="{ 'table-danger': strong(r), 'table-warning': !strong(r) }">
            <td>{{ r.employeeName }}</td>
            <td class="inv-ltr">{{ persianOf(r.dateText, r.date) }}</td>
            <td class="fn-num">{{ hoursText(r.attendanceHours) }}</td>
            <td class="fn-num">{{ hoursText(r.bookedHours) }}</td>
            <td class="fn-num"><b>{{ hoursText(r.differenceHours) }}</b></td>
            <td>{{ differenceKinds[r.kind] || r.kind }}</td>
            <td class="small">{{ r.message }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
  </div>
</template>
