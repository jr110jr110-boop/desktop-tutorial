<script setup lang="ts">
// Computes the daily attendance results of a date range (HR, CEO). The server also does it every night; this is for corrections.
import { ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { computeAttendance } from "@/api/OfficeAPIs";
import { jalaliNow, monthRange } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import EmployeePick from "./EmployeePick.vue";

const now = jalaliNow();
const range = monthRange(now.year, now.month);
const from = ref<string | null>(range.from);
const to = ref<string | null>(range.to);
const employee = ref<any>(null);
const busy = ref(false);
const result = ref<any>(null);

async function run() {
  if (!from.value || !to.value) return fail("بازه تاریخ را وارد کنید.");
  busy.value = true;
  const [d, error] = await computeAttendance({ fromDate: from.value, toDate: to.value, employeeId: employee.value?.id || null });
  busy.value = false;
  if (error) return fail(error);
  result.value = d;
  ok("محاسبه انجام شد.");
}
</script>

<template>
  <div>
    <p class="inv-muted small">نتیجه روزانه از ترددها، شیفت، مرخصی و ماموریت محاسبه می‌شود. روزهای ماه‌هایی که حقوق آن تایید شده تغییر نمی‌کند.</p>
    <div class="inv-filters mb-3">
      <div><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="from" /></div>
      <div><label class="form-label small mb-0">تا تاریخ *</label><FnDate v-model="to" /></div>
      <div style="min-width: 220px"><label class="form-label small mb-0">کارمند (اختیاری)</label><EmployeePick v-model="employee" placeholder="همه کارکنان" /></div>
      <button :disabled="busy" class="btn btn-sm btn-primary align-self-end" @click="run">محاسبه</button>
    </div>
    <div v-if="result" class="fn-tiles">
      <div class="fn-tile ok"><div class="t">روز محاسبه‌شده</div><div class="v">{{ result.days }}</div></div>
      <div class="fn-tile"><div class="t">تعداد کارکنان</div><div class="v">{{ result.employees }}</div></div>
      <div :class="result.skippedLocked ? 'warn' : ''" class="fn-tile"><div class="t">ردشده به علت قفل حقوق</div><div class="v">{{ result.skippedLocked }}</div></div>
      <div :class="result.employeesWithoutShift ? 'bad' : ''" class="fn-tile"><div class="t">کارمند بدون شیفت</div><div class="v">{{ result.employeesWithoutShift }}</div></div>
    </div>
    <p v-if="result?.employeesWithoutShift" class="small text-danger mt-2">برای کارکنان بدون شیفت روزی محاسبه نمی‌شود؛ شیفت را در «تنظیمات حضور و غیاب» تخصیص دهید.</p>
  </div>
</template>
