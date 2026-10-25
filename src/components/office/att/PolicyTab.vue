<script setup lang="ts">
// Attendance policy: grace, daily and monthly overtime caps, overtime pre-approval, rounding and the coefficient of the delay deduction.
import { onMounted, ref } from "vue";
import { getAttendancePolicy, saveAttendancePolicy } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";

const form = ref<any>(null);
const busy = ref(false);

async function load() {
  const [d, error] = await getAttendancePolicy();
  if (error) return fail(error);
  form.value = {
    graceMinutes: String(d?.graceMinutes ?? 15), max: d?.maxOvertimeMinutesPerDay == null ? "" : String(d.maxOvertimeMinutesPerDay), pre: d?.overtimeNeedsPreApproval === true, rounding: String(d?.roundingMinutes ?? 0),
    monthlyCapHours: String(Math.round(((d?.monthlyOvertimeCapMinutes ?? 1320) / 60) * 100) / 100), factor: String(d?.delayPenaltyFactor ?? 3),
  };
}
async function save() {
  const f = form.value;
  const grace = Number(f.graceMinutes), rounding = Number(f.rounding);
  const max = String(f.max).trim() === "" ? null : Number(f.max);
  if (!Number.isInteger(grace) || grace < 0) return fail("مهلت تاخیر نامعتبر است.");
  if (max !== null && (!Number.isInteger(max) || max < 0 || max > 720)) return fail("سقف اضافه‌کار روزانه باید بین ۰ و ۷۲۰ دقیقه باشد.");
  if (!Number.isInteger(rounding) || rounding < 0 || rounding > 60) return fail("گرد کردن باید بین ۰ و ۶۰ دقیقه باشد.");
  const capMinutes = Math.round(Number(f.monthlyCapHours) * 60), factor = Number(f.factor);
  if (!String(f.monthlyCapHours).trim() || !Number.isFinite(capMinutes) || capMinutes < 0 || capMinutes > 44640) return fail("سقف اضافه‌کار ماهانه باید بین ۰ و ۷۴۴ ساعت باشد.");
  if (!String(f.factor).trim() || !Number.isFinite(factor) || factor < 0 || factor > 10) return fail("ضریب کسر تاخیر باید بین ۰ و ۱۰ باشد.");
  busy.value = true;
  const [, error] = await saveAttendancePolicy({ graceMinutes: grace, maxOvertimeMinutesPerDay: max, overtimeNeedsPreApproval: f.pre, roundingMinutes: rounding, monthlyOvertimeCapMinutes: capMinutes, delayPenaltyFactor: Math.round(factor * 100) / 100 });
  busy.value = false;
  if (error) return fail(error);
  ok("سیاست حضور و غیاب ذخیره شد. برای اعمال روی روزهای گذشته، محاسبه را دوباره اجرا کنید.");
}
onMounted(load);
</script>

<template>
  <div v-if="form" style="max-width: 640px">
    <div class="alert alert-info small py-2">قواعد شرکت پیش‌فرض این سیاست است: مهلت ۱۵ دقیقه، کسر تاخیر بیش از مهلت با ضریب ۳ و سقف اضافه‌کار ماهانه ۲۲ ساعت. سقف ۲۲ ساعت فقط اضافه‌کار روز کاری را در بر می‌گیرد (ساعت‌های تعطیل، جمعه و شب‌کاری سقف ندارد) و تعجیل (خروج زودتر) کسر نمی‌شود. در سیستم‌های قدیمی که هنوز مقدارهای اولیه (مهلت ۰ و اضافه‌کار با تایید قبلی) را داشتند، این قواعد یک بار خودکار اعمال و در سابقه ثبت شده است.</div>
    <div class="row g-3">
      <div class="col-md-6"><label class="form-label small mb-0">مهلت تاخیر و تعجیل (دقیقه)</label><input v-model="form.graceMinutes" class="form-control form-control-sm fn-num" /><div class="inv-muted small">پیش‌فرض شرکت ۱۵ دقیقه. تاخیر یا تعجیل بیش از مهلت، کامل شمرده می‌شود (۱۴ دقیقه تاخیر بدون کسر، ۱۶ دقیقه با کسر).</div></div>
      <div class="col-md-6"><label class="form-label small mb-0">ضریب کسر تاخیر</label><input v-model="form.factor" class="form-control form-control-sm fn-num" /><div class="inv-muted small">پیش‌فرض ۳: تاخیر بیش از مهلت، به اندازه‌ی «کل تاخیر × ضریب» از کارکرد کم می‌شود (۱۶ دقیقه تاخیر = ۴۸ دقیقه کسر). کسر فقط یک بار و به شکل بخشی از روز غیبت در فیش حقوقی می‌آید. ۰ یعنی بدون کسر.</div></div>
      <div class="col-md-6"><label class="form-label small mb-0">سقف اضافه‌کار ماهانه (ساعت)</label><input v-model="form.monthlyCapHours" class="form-control form-control-sm fn-num" /><div class="inv-muted small">پیش‌فرض ۲۲ ساعت؛ اضافه‌کار بیش از آن در ماه به حقوق نمی‌رسد و نیاز به تایید قبلی ندارد.</div></div>
      <div class="col-md-6"><label class="form-label small mb-0">سقف اضافه‌کار روزانه (دقیقه)</label><input v-model="form.max" class="form-control form-control-sm fn-num" placeholder="خالی = بدون سقف" /><div class="inv-muted small">اختیاری، حداکثر ۷۲۰ دقیقه؛ سقف ماهانه جداگانه اعمال می‌شود.</div></div>
      <div class="col-md-6"><label class="form-label small mb-0">گرد کردن اضافه‌کار به پایین (دقیقه)</label><input v-model="form.rounding" class="form-control form-control-sm fn-num" /><div class="inv-muted small">۰ یا ۱ یعنی بدون گرد کردن.</div></div>
      <div class="col-md-6 d-flex align-items-center"><label class="small"><input v-model="form.pre" type="checkbox" class="form-check-input me-1" />اضافه‌کار نیازمند تایید قبلی است</label></div>
    </div>
    <div class="mt-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
  </div>
</template>
