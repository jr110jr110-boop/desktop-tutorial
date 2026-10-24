<script setup lang="ts">
// Clock events list; HR can add a manual event (with a mandatory reason) and import events from a file.
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import { addManualClock, getClockEvents } from "@/api/OfficeAPIs";
import { jalaliNow, monthRange, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { clockDirections, clockSources } from "@/composables/office";
import ClockImport from "./ClockImport.vue";
import EmployeePick from "./EmployeePick.vue";
import TimeField from "./TimeField.vue";
import { stamp } from "./attCommon";

defineProps<{ canAll: boolean; isHr: boolean }>();
const now = jalaliNow();
const range = monthRange(now.year, now.month);
const employee = ref<any>(null);
const from = ref<string | null>(range.from);
const to = ref<string | null>(range.to);
const source = ref<number | "">("");
const filters = ref<any>({});
const list = usePagedList((p: any) => getClockEvents(p), filters);
function search() {
  filters.value = { EmployeeId: employee.value?.id || null, From: from.value, To: to.value, Source: source.value === "" ? null : source.value };
  return list.search();
}

// manual event
const manual = ref<any>(null);
const busy = ref(false);
const openManual = () => (manual.value = { employee: null, date: null, time: "", direction: 1, reason: "" });
async function saveManual() {
  const f = manual.value;
  if (!f.employee) return fail("کارمند را انتخاب کنید.");
  if (!f.date) return fail("تاریخ را وارد کنید.");
  if (!f.time) return fail("ساعت تردد را وارد کنید (مثل 08:15).");
  if (!f.reason.trim()) return fail("دلیل ثبت دستی الزامی است.");
  busy.value = true;
  const [, error] = await addManualClock({ employeeId: f.employee.id, timestamp: stamp(f.date, f.time), direction: Number(f.direction), reason: f.reason.trim() });
  busy.value = false;
  if (error) return fail(error);
  ok("تردد ثبت شد. برای اعمال در نتیجه روزانه، محاسبه را اجرا کنید.");
  manual.value = null;
  list.load();
}
const importing = ref(false);
function imported() {
  list.search();
}
onMounted(search);
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <div v-if="canAll" style="min-width: 220px"><label class="form-label small mb-0">کارمند</label><EmployeePick v-model="employee" placeholder="همه کارکنان" /></div>
      <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="from" /></div>
      <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="to" /></div>
      <div><label class="form-label small mb-0">منبع</label><select v-model="source" class="form-select form-select-sm"><option value="">همه</option><option v-for="(t, k) in clockSources" :key="k" :value="Number(k)">{{ t }}</option></select></div>
      <button class="btn btn-sm btn-secondary align-self-end" @click="search">نمایش</button>
      <button v-if="isHr" class="btn btn-sm btn-primary align-self-end" @click="openManual">ثبت دستی تردد</button>
      <button v-if="isHr" class="btn btn-sm btn-outline-primary align-self-end" @click="importing = true">ورود از فایل</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>تاریخ و ساعت</th><th v-if="canAll">کارمند</th><th>جهت</th><th>منبع</th><th>دستگاه</th><th>دلیل</th><th>ثبت‌کننده</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td :colspan="canAll ? 7 : 6" class="text-center inv-muted">تردد ثبت‌شده‌ای نیست.</td></tr>
          <tr v-for="e in list.rows.value" :key="e.id">
            <td class="inv-ltr">{{ e.timestampText }}</td>
            <td v-if="canAll">{{ e.employeeName }}<div class="inv-muted small inv-ltr">{{ e.personnelCode }}</div></td>
            <td>{{ clockDirections[e.direction] }}</td>
            <td><span :class="e.source === 3 ? 'warn' : 'info'" class="inv-badge">{{ clockSources[e.source] }}</span></td>
            <td class="inv-ltr">{{ e.deviceCode }}</td><td>{{ e.reason }}</td><td>{{ e.createdByName }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="manual" class="inv-modal-backdrop" @click.self="manual = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ثبت دستی تردد</h2>
        <p class="inv-muted small">ثبت دستی در سابقه تغییرات نگهداری می‌شود. ماهی که حقوق آن تایید شده یا زمان آینده پذیرفته نمی‌شود.</p>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">کارمند *</label><EmployeePick v-model="manual.employee" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ *</label><FnDate v-model="manual.date" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">ساعت *</label><TimeField v-model="manual.time" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">جهت *</label><select v-model="manual.direction" class="form-select form-select-sm"><option :value="1">ورود</option><option :value="2">خروج</option><option :value="0">نامشخص</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">دلیل *</label><textarea v-model="manual.reason" class="form-control form-control-sm" rows="2" placeholder="مثلا خرابی دستگاه یا فراموشی تردد با تایید مدیر واحد"></textarea></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="manual = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveManual">ثبت</button></div>
      </div>
    </div>

    <ClockImport v-if="importing" @close="importing = false" @done="imported" />
  </div>
</template>
