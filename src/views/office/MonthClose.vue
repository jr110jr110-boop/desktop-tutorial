<script setup lang="ts">
// Monthly close (بستن ماه): status of a Persian month for everybody who may read it, closing for HR only.
// The close recomputes the days and writes the monthly sheets that payroll reads. The printed sheet is of the result of the close just made.
import { computed, nextTick, onMounted, ref } from "vue";
import MonthSheetPrint from "@/components/office/att/MonthSheetPrint.vue";
import { days2 } from "@/components/office/att/attCommon";
import { closeAttendanceMonth, getAttendanceMonth } from "@/api/OfficeAPIs";
import { jalaliNow, jalaliTitle, monthNames, parseAmount, printSheet } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole, OfficeRoles } from "@/composables/roles";

const now = jalaliNow();
// the month that just ended is the one that is normally closed
const startYear = now.month === 1 ? now.year - 1 : now.year;
const startMonth = now.month === 1 ? 12 : now.month - 1;
const year = ref(String(startYear));
const month = ref(startMonth);
const canClose = hasRole(OfficeRoles.monthClose);
const status = ref<any>(null);
const result = ref<any>(null);
const accept = ref(false);
const confirming = ref(false);
const busy = ref(false);
const yearNumber = computed(() => parseAmount(year.value) || 0);
const cols: [string, string][] = [["daysWorked", "کارکرد (روز)"], ["overtimeHours", "اضافه‌کار (ساعت)"], ["nightHours", "شب‌کاری (ساعت)"], ["holidayHours", "تعطیل‌کاری (ساعت)"], ["paidLeaveDays", "مرخصی با حقوق"], ["sickLeaveDays", "استعلاجی"], ["unpaidLeaveDays", "بدون حقوق"], ["absenceDays", "غیبت"], ["missionDays", "ماموریت"]];

async function load() {
  result.value = null;
  if (yearNumber.value < 1300 || yearNumber.value > 1600) return fail("سال نامعتبر است.");
  const [d, error] = await getAttendanceMonth(yearNumber.value, month.value);
  if (error) {
    status.value = null;
    return fail(error);
  }
  status.value = d;
}
async function close() {
  busy.value = true;
  const [d, error] = await closeAttendanceMonth(yearNumber.value, month.value, accept.value);
  busy.value = false;
  confirming.value = false;
  if (error) return fail(error);
  result.value = d;
  ok(`ماه بسته شد؛ برگه کارکرد ${d?.sheets ?? 0} نفر ثبت شد.`);
  const [s] = await getAttendanceMonth(yearNumber.value, month.value);
  if (s) status.value = s;
}
// the sheet shown and printed: the fresh close result of this session, else the saved sheet of the closed month
const sheet = computed(() => (result.value ? result.value : status.value?.closed && status.value.rows?.length ? { year: status.value.year, month: status.value.month, sheets: status.value.rows.length, incompleteDays: null, rows: status.value.rows } : null));
async function print() {
  await nextTick();
  printSheet();
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <MonthSheetPrint v-if="sheet" :result="sheet" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">بستن ماه حضور و غیاب</h1><router-link :to="{ name: 'OfAttendance' }" class="btn btn-sm btn-outline-secondary">حضور و غیاب</router-link></div>
      <div class="inv-filters mb-3">
        <div><label class="form-label small mb-0">سال</label><input v-model="year" class="form-control form-control-sm fn-num" style="width: 6rem" /></div>
        <div><label class="form-label small mb-0">ماه</label><select v-model="month" class="form-select form-select-sm"><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select></div>
        <button class="btn btn-sm btn-secondary align-self-end" @click="load">نمایش وضعیت</button>
      </div>

      <div v-if="status" class="fn-tiles mb-3">
        <div :class="status.closed ? 'ok' : 'warn'" class="fn-tile"><div class="t">وضعیت {{ jalaliTitle(status.year, status.month) }}</div><div class="v">{{ status.closed ? "بسته‌شده" : "باز" }}</div></div>
        <div class="fn-tile"><div class="t">آخرین بستن</div><div class="v inv-ltr">{{ status.closedAtText || "-" }}</div></div>
        <div class="fn-tile"><div class="t">برگه‌های کارکرد</div><div class="v">{{ status.sheets }}</div></div>
        <div :class="status.lockedByPayroll ? 'bad' : 'ok'" class="fn-tile"><div class="t">قفل حقوق</div><div class="v">{{ status.lockedByPayroll ? "حقوق تایید شده (قفل)" : "قفل نیست" }}</div></div>
      </div>
      <p v-if="status?.lockedByPayroll" class="small text-danger">حقوق این ماه تایید شده است؛ بستن دوباره ماه برگه‌ای را تغییر نمی‌دهد.</p>

      <div v-if="canClose" class="border rounded p-3 mb-3">
        <label class="small d-block mb-2"><input v-model="accept" type="checkbox" class="form-check-input me-1" />روزهای ناقص به عنوان غیبت پذیرفته شود</label>
        <p class="inv-muted small mb-2">روز ناقص روزی است که فقط یک تردد دارد. بدون این گزینه، تا اصلاح ترددها (ثبت دستی یا درخواست فراموشی تردد) بستن ماه انجام نمی‌شود.</p>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="confirming = true">بستن ماه</button>
      </div>
      <p v-else class="inv-muted small">بستن ماه فقط توسط واحد اداری و منابع انسانی انجام می‌شود.</p>

      <template v-if="!result && sheet">
        <div class="d-flex align-items-center gap-2 mb-2">
          <h2 class="inv-title mb-0">برگه کارکرد ثبت‌شده {{ jalaliTitle(sheet.year, sheet.month) }}</h2>
          <button class="btn btn-sm btn-outline-secondary" @click="print">چاپ برگه کارکرد</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>کد پرسنلی</th><th>کارمند</th><th v-for="c in cols" :key="c[0]">{{ c[1] }}</th></tr></thead>
            <tbody>
              <tr v-for="r in sheet.rows" :key="r.employeeId"><td class="inv-ltr">{{ r.personnelCode }}</td><td>{{ r.employeeName }}</td><td v-for="c in cols" :key="c[0]" class="fn-num">{{ days2(r[c[0]]) }}</td></tr>
            </tbody>
          </table>
        </div>
      </template>
      <template v-if="result">
        <div class="d-flex align-items-center gap-2 mb-2">
          <h2 class="inv-title mb-0">نتیجه بستن {{ jalaliTitle(result.year, result.month) }}</h2>
          <button class="btn btn-sm btn-outline-secondary" @click="print">چاپ برگه کارکرد</button>
        </div>
        <div class="fn-tiles mb-2">
          <div class="fn-tile ok"><div class="t">برگه ثبت‌شده</div><div class="v">{{ result.sheets }}</div></div>
          <div :class="result.incompleteDays ? 'warn' : ''" class="fn-tile"><div class="t">روز ناقص</div><div class="v">{{ result.incompleteDays }}</div></div>
          <div :class="result.skippedLocked ? 'bad' : ''" class="fn-tile"><div class="t">ردشده به علت قفل حقوق</div><div class="v">{{ result.skippedLocked }}</div></div>
        </div>
        <ul v-if="result.warnings?.length" class="small text-danger"><li v-for="(w, i) in result.warnings" :key="i">{{ w }}</li></ul>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>کد پرسنلی</th><th>کارمند</th><th v-for="c in cols" :key="c[0]">{{ c[1] }}</th></tr></thead>
            <tbody>
              <tr v-if="!result.rows?.length"><td :colspan="cols.length + 2" class="text-center inv-muted">ردیفی نیست.</td></tr>
              <tr v-for="r in result.rows" :key="r.employeeId"><td class="inv-ltr">{{ r.personnelCode }}</td><td>{{ r.employeeName }}</td><td v-for="c in cols" :key="c[0]" class="fn-num">{{ days2(r[c[0]]) }}</td></tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>

    <div v-if="confirming" class="inv-modal-backdrop screen-only" @click.self="confirming = false">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">بستن {{ jalaliTitle(yearNumber, month) }}</h2>
        <ul class="small">
          <li>نتیجه روزانه همه کارکنان این ماه دوباره محاسبه می‌شود.</li>
          <li>برگه کارکرد ماهانه هر کارمند (کارکرد، اضافه‌کار، شب‌کاری، تعطیل‌کاری، مرخصی، غیبت و ماموریت) نوشته یا جایگزین می‌شود و ورودی محاسبه حقوق همین ماه است.</li>
          <li v-if="accept" class="text-danger">هر روز ناقص (تک‌تردد) به عنوان یک روز غیبت حساب می‌شود و از حقوق کسر می‌شود.</li>
          <li v-else>اگر روز ناقصی باشد ماه بسته نمی‌شود و ابتدا باید ترددها اصلاح شود.</li>
          <li>برای کارکنانی که حقوق ماه آن‌ها تایید شده، برگه تغییر نمی‌کند.</li>
          <li>بستن ماه جاری یا ماهی که تمام نشده ممکن نیست. بستن دوباره، برگه‌های قبلی را به‌روز می‌کند.</li>
        </ul>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-outline-secondary" @click="confirming = false">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="close">تایید و بستن ماه</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
