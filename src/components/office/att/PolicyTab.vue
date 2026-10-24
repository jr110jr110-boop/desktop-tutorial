<script setup lang="ts">
// Attendance policy: grace, overtime cap, overtime pre-approval and rounding.
import { onMounted, ref } from "vue";
import { getAttendancePolicy, saveAttendancePolicy } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";

const form = ref<any>(null);
const busy = ref(false);

async function load() {
  const [d, error] = await getAttendancePolicy();
  if (error) return fail(error);
  form.value = { graceMinutes: String(d?.graceMinutes ?? 0), max: d?.maxOvertimeMinutesPerDay == null ? "" : String(d.maxOvertimeMinutesPerDay), pre: d?.overtimeNeedsPreApproval !== false, rounding: String(d?.roundingMinutes ?? 0) };
}
async function save() {
  const f = form.value;
  const grace = Number(f.graceMinutes), rounding = Number(f.rounding);
  const max = String(f.max).trim() === "" ? null : Number(f.max);
  if (!Number.isInteger(grace) || grace < 0) return fail("مهلت تاخیر نامعتبر است.");
  if (max !== null && (!Number.isInteger(max) || max < 0 || max > 720)) return fail("سقف اضافه‌کار روزانه باید بین ۰ و ۷۲۰ دقیقه باشد.");
  if (!Number.isInteger(rounding) || rounding < 0 || rounding > 60) return fail("گرد کردن باید بین ۰ و ۶۰ دقیقه باشد.");
  busy.value = true;
  const [, error] = await saveAttendancePolicy({ graceMinutes: grace, maxOvertimeMinutesPerDay: max, overtimeNeedsPreApproval: f.pre, roundingMinutes: rounding });
  busy.value = false;
  if (error) return fail(error);
  ok("سیاست حضور و غیاب ذخیره شد. برای اعمال روی روزهای گذشته، محاسبه را دوباره اجرا کنید.");
}
onMounted(load);
</script>

<template>
  <div v-if="form" style="max-width: 640px">
    <div class="row g-3">
      <div class="col-md-6"><label class="form-label small mb-0">مهلت تاخیر و تعجیل (دقیقه)</label><input v-model="form.graceMinutes" class="form-control form-control-sm fn-num" /><div class="inv-muted small">تاخیر یا تعجیل بیش از مهلت، کامل شمرده می‌شود.</div></div>
      <div class="col-md-6"><label class="form-label small mb-0">سقف اضافه‌کار روزانه (دقیقه)</label><input v-model="form.max" class="form-control form-control-sm fn-num" placeholder="خالی = بدون سقف" /><div class="inv-muted small">حداکثر ۷۲۰ دقیقه.</div></div>
      <div class="col-md-6"><label class="form-label small mb-0">گرد کردن اضافه‌کار به پایین (دقیقه)</label><input v-model="form.rounding" class="form-control form-control-sm fn-num" /><div class="inv-muted small">۰ یا ۱ یعنی بدون گرد کردن.</div></div>
      <div class="col-md-6 d-flex align-items-center"><label class="small"><input v-model="form.pre" type="checkbox" class="form-check-input me-1" />اضافه‌کار نیازمند تایید قبلی است</label></div>
    </div>
    <div class="mt-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
  </div>
</template>
