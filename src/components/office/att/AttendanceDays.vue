<script setup lang="ts">
// Daily results of the attendance computation: filters, the table and the totals row of the rows shown.
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import { getAttendanceDays } from "@/api/OfficeAPIs";
import { jalaliNow, monthRange, usePagedList } from "@/composables/fn";
import { dayStatuses, minutesText, timeOf } from "@/composables/office";
import EmployeePick from "./EmployeePick.vue";
import { dayBadge, daysShort } from "./attCommon";

const props = defineProps<{ canAll: boolean }>();
const now = jalaliNow();
const range = monthRange(now.year, now.month);
const employee = ref<any>(null);
const from = ref<string | null>(range.from);
const to = ref<string | null>(range.to);
const status = ref<number | "">("");
const filters = ref<any>({});
const list = usePagedList((p: any) => getAttendanceDays(p), filters, 62);

function search() {
  filters.value = { EmployeeId: employee.value?.id || null, From: from.value, To: to.value, Status: status.value === "" ? null : status.value };
  return list.search();
}
const sum = (key: string) => list.rows.value.reduce((t: number, r: any) => t + (Number(r[key]) || 0), 0);
const minuteCols: [string, string][] = [["workedMinutes", "کارکرد"], ["delayMinutes", "تاخیر"], ["earlyLeaveMinutes", "تعجیل"], ["overtimeMinutes", "اضافه‌کار"], ["nightMinutes", "شب‌کاری"], ["holidayMinutes", "تعطیل‌کاری"]];
const dayCols: [string, string][] = [["absenceDays", "غیبت"], ["paidLeaveDays", "مرخصی با حقوق"], ["sickLeaveDays", "استعلاجی"], ["unpaidLeaveDays", "بدون حقوق"], ["missionDays", "ماموریت"]];
const colCount = computed(() => 5 + (props.canAll ? 1 : 0) + minuteCols.length + dayCols.length);
const partial = computed(() => list.total.value > list.rows.value.length);
onMounted(search);
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <div v-if="canAll" style="min-width: 220px"><label class="form-label small mb-0">کارمند</label><EmployeePick v-model="employee" placeholder="همه کارکنان" /></div>
      <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="from" /></div>
      <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="to" /></div>
      <div><label class="form-label small mb-0">وضعیت</label><select v-model="status" class="form-select form-select-sm"><option value="">همه</option><option v-for="(t, k) in dayStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
      <button class="btn btn-sm btn-secondary align-self-end" @click="search">نمایش</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead>
          <tr>
            <th>تاریخ</th><th v-if="canAll">کارمند</th><th>وضعیت</th><th>شیفت</th><th>ورود</th><th>خروج</th>
            <th v-for="c in minuteCols" :key="c[0]">{{ c[1] }}</th><th v-for="c in dayCols" :key="c[0]">{{ c[1] }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td :colspan="colCount" class="text-center inv-muted">نتیجه‌ای در این بازه نیست. اگر محاسبه انجام نشده، از زبانه «محاسبه» استفاده کنید.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.employeeId + r.date">
            <td class="inv-ltr">{{ r.dateText }}</td>
            <td v-if="canAll">{{ r.employeeName }}<div class="inv-muted small inv-ltr">{{ r.personnelCode }}</div></td>
            <td><span :class="dayBadge(r.status)" class="inv-badge">{{ dayStatuses[r.status] }}</span><span v-if="r.nonWorkingDay" class="inv-badge warn ms-1" title="روز غیرکاری (تعطیل یا خارج از روزهای کاری شیفت)">غیرکاری</span><div v-if="r.note" class="inv-muted small">{{ r.note }}</div></td>
            <td>{{ r.shiftTitle }}</td><td class="inv-ltr">{{ timeOf(r.firstIn) || "-" }}</td><td class="inv-ltr">{{ timeOf(r.lastOut) || "-" }}</td>
            <td v-for="c in minuteCols" :key="c[0]" class="fn-num">{{ minutesText(r[c[0]]) }}</td>
            <td v-for="c in dayCols" :key="c[0]" class="fn-num">{{ daysShort(r[c[0]]) }}</td>
          </tr>
          <tr v-if="list.rows.value.length" class="fn-total">
            <td :colspan="canAll ? 6 : 5"><b>جمع {{ partial ? "ردیف‌های این صفحه" : "بازه" }}</b></td>
            <td v-for="c in minuteCols" :key="c[0]" class="fn-num"><b>{{ minutesText(sum(c[0])) }}</b></td>
            <td v-for="c in dayCols" :key="c[0]" class="fn-num"><b>{{ daysShort(sum(c[0])) }}</b></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :page-size="list.pageSize" :total="list.total.value" @go="list.go" />
    <p class="inv-muted small mb-0">زمان‌ها به صورت ساعت:دقیقه نمایش داده می‌شود. روز تک‌تردد «ناقص» است و در بستن ماه باید اصلاح یا به عنوان غیبت پذیرفته شود.</p>
  </div>
</template>
