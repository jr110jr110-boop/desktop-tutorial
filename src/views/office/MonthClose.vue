<script setup lang="ts">
// Monthly close (بستن ماه): status of a Persian month for everybody who may read it, closing for HR only.
// The close recomputes the days and writes the monthly sheets that payroll reads. The printed sheet is of the result of the close just made.
import { computed, nextTick, onMounted, ref } from "vue";
import MonthSheetPrint from "@/components/office/att/MonthSheetPrint.vue";
import { days2 } from "@/components/office/att/attCommon";
import { closeAttendanceMonth, confirmAttendanceMonth, getAttendanceMonth, rejectAttendanceMonth } from "@/api/OfficeAPIs";
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
/** the finance manager may also reopen a confirmed month */
const isFinanceManager = hasRole(["FINANCEMANAGER"]);
const rejecting = ref<{ reason: string } | null>(null);
const statusTitles: Record<string, string> = { None: "آماده‌سازی نشده", Prepared: "آماده‌شده، منتظر تایید", Confirmed: "تاییدشده", Rejected: "ردشده، منتظر آماده‌سازی دوباره" };
const statusKind = computed(() => (status.value?.status === "Confirmed" ? "ok" : status.value?.status === "Rejected" ? "bad" : status.value?.status === "Prepared" ? "warn" : ""));
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
  ok(`ماه آماده شد؛ برگه کارکرد ${d?.sheets ?? 0} نفر ثبت شد. تایید نهایی با مدیر مالی است (یا کاربر دیگری از منابع انسانی اگر سیاست اجازه دهد).`);
  const [s] = await getAttendanceMonth(yearNumber.value, month.value);
  if (s) status.value = s;
}
async function confirmMonth() {
  busy.value = true;
  const [d, error] = await confirmAttendanceMonth(yearNumber.value, month.value);
  busy.value = false;
  if (error) return fail(error);
  status.value = d;
  ok("حضور و غیاب این ماه تایید شد و حقوق می‌تواند از آن استفاده کند.");
}
async function rejectMonth() {
  const reason = rejecting.value?.reason.trim() || "";
  if (!reason) return fail("دلیل رد را بنویسید.");
  busy.value = true;
  const [d, error] = await rejectAttendanceMonth(yearNumber.value, month.value, reason);
  busy.value = false;
  if (error) return fail(error);
  rejecting.value = null;
  result.value = null;
  status.value = d;
  ok("ماه به منابع انسانی برگردانده شد.");
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
        <div :class="statusKind || (status.closed ? 'ok' : 'warn')" class="fn-tile"><div class="t">وضعیت {{ jalaliTitle(status.year, status.month) }}</div><div class="v">{{ status.status ? statusTitles[status.status] || status.status : status.closed ? "بسته‌شده" : "باز" }}</div></div>
        <div class="fn-tile"><div class="t">آخرین آماده‌سازی</div><div class="v inv-ltr">{{ status.closedAtText || "-" }}</div><div class="inv-muted small">{{ status.preparedByName }}</div></div>
        <div v-if="status.status === 'Confirmed'" class="fn-tile ok"><div class="t">تاییدکننده</div><div class="v">{{ status.confirmedByName || "-" }}</div><div class="inv-muted small inv-ltr">{{ status.confirmedAtText }}</div></div>
        <div class="fn-tile"><div class="t">برگه‌های کارکرد</div><div class="v">{{ status.sheets }}</div></div>
        <div :class="status.lockedByPayroll ? 'bad' : 'ok'" class="fn-tile"><div class="t">قفل حقوق</div><div class="v">{{ status.lockedByPayroll ? "حقوق تایید شده (قفل)" : "قفل نیست" }}</div></div>
      </div>
      <p v-if="status?.lockedByPayroll" class="small text-danger">حقوق این ماه تایید شده است؛ بستن دوباره ماه برگه‌ای را تغییر نمی‌دهد.</p>
      <div v-if="status?.status === 'Rejected' && status.rejectReason" class="alert alert-danger small py-2">دلیل رد: {{ status.rejectReason }}</div>
      <div v-if="status?.snapshotNote" class="alert alert-secondary small py-2">{{ status.snapshotNote }}</div>
      <div v-else-if="status?.snapshotMatches === false" class="alert alert-danger small py-2">ارقام برگه‌های کارکرد با آنچه هنگام آماده‌سازی ثبت شد یکی نیست؛ تایید ممکن نیست و ماه باید رد و دوباره آماده شود.</div>
      <div v-if="status?.snapshotHash" class="inv-muted small mb-2">مهر snapshot: <span class="inv-ltr">{{ status.snapshotHash }}</span><span v-if="status.snapshotMatches === true" class="inv-badge ok ms-2">با برگه‌ها یکی است</span></div>
      <div v-if="status?.policy" class="inv-muted small mb-2">سیاست هنگام آماده‌سازی: مهلت {{ status.policy.graceMinutes }} دقیقه، تایید پیشینی اضافه‌کار {{ status.policy.overtimeRequiresApproval ? "روشن" : "خاموش" }}، کسر خروج زودهنگام {{ status.policy.earlyLeaveDeduction ? "روشن" : "خاموش" }}، تایید ماه توسط همکار منابع انسانی {{ status.policy.monthConfirmByHrPeer ? "مجاز" : "غیرمجاز" }}.</div>
      <div v-if="status?.pendingRequests?.length" class="alert alert-warning small py-2">
        <div class="fw-bold">{{ status.pendingRequests.length }} درخواست این ماه هنوز تصمیم‌گیری نشده است و تا تعیین تکلیف، تایید ماه ممکن نیست:</div>
        <ul class="mb-0"><li v-for="r in status.pendingRequests" :key="r.kind + r.id">{{ r.text }}</li></ul>
      </div>
      <div v-if="status?.noWorkshop?.length" class="alert alert-warning small py-2">کارگاه بیمه ندارند (محاسبه حقوق آن‌ها تا تعیین کارگاه ممکن نیست): {{ status.noWorkshop.map((e: any) => e.employeeName + " (" + e.personnelCode + ")").join("، ") }}</div>

      <div v-if="status?.canConfirm || (isFinanceManager && status?.status === 'Confirmed') || (canClose && status?.status === 'Prepared')" class="border rounded p-3 mb-3">
        <div class="fw-bold small mb-2">مرحله دوم: تایید ماه</div>
        <p class="inv-muted small mb-2">آماده‌کننده‌ی ماه نمی‌تواند همان ماه را تایید کند. تایید با مدیر مالی است (یا کاربر دیگری از منابع انسانی، اگر مدیرعامل در سیاست اجازه داده باشد). رد کردن ماه را برای آماده‌سازی دوباره به منابع انسانی برمی‌گرداند.</p>
        <button v-if="status.canConfirm" :disabled="busy || status.snapshotMatches === false" class="btn btn-sm btn-success me-2" @click="confirmMonth">تایید ماه</button>
        <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="rejecting = { reason: '' }">{{ status.status === "Confirmed" ? "بازگشایی ماه (رد)" : "رد ماه" }}</button>
      </div>

      <div v-if="canClose" class="border rounded p-3 mb-3">
        <label class="small d-block mb-2"><input v-model="accept" type="checkbox" class="form-check-input me-1" />روزهای ناقص به عنوان غیبت پذیرفته شود</label>
        <p class="inv-muted small mb-2">روز ناقص روزی است که فقط یک تردد دارد. بدون این گزینه، تا اصلاح ترددها (ثبت دستی یا درخواست فراموشی تردد) بستن ماه انجام نمی‌شود.</p>
        <button :disabled="busy || status?.status === 'Confirmed'" class="btn btn-sm btn-primary" @click="confirming = true">آماده‌سازی ماه (مرحله اول)</button>
      </div>
      <p v-else class="inv-muted small">آماده‌سازی ماه فقط توسط منابع انسانی انجام می‌شود و تایید آن با مدیر مالی است.</p>

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
        <div v-if="result.gapDays?.length" class="small mb-2"><div class="fw-bold">روزهای بدون شیفت میان تخصیص‌ها (غایب حساب شد)</div><ul><li v-for="g in result.gapDays" :key="g.employeeId">{{ g.employeeName }} ({{ g.personnelCode }}): {{ g.count }} روز، {{ g.dateTexts.join("، ") }}</li></ul></div>
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

    <div v-if="rejecting" class="inv-modal-backdrop screen-only" @click.self="rejecting = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ status?.status === "Confirmed" ? "بازگشایی" : "رد" }} {{ jalaliTitle(yearNumber, month) }}</h2>
        <label class="form-label small mb-0">دلیل *</label>
        <textarea v-model="rejecting.reason" class="form-control form-control-sm" rows="3"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="rejecting = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-danger" @click="rejectMonth">ثبت</button></div>
      </div>
    </div>

    <div v-if="confirming" class="inv-modal-backdrop screen-only" @click.self="confirming = false">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">آماده‌سازی {{ jalaliTitle(yearNumber, month) }}</h2>
        <ul class="small">
          <li>نتیجه روزانه همه کارکنان این ماه دوباره محاسبه می‌شود.</li>
          <li>برگه کارکرد ماهانه هر کارمند (کارکرد، اضافه‌کار، شب‌کاری، تعطیل‌کاری، مرخصی، غیبت و ماموریت) نوشته یا جایگزین می‌شود و ورودی محاسبه حقوق همین ماه است.</li>
          <li v-if="accept" class="text-danger">هر روز ناقص (تک‌تردد) به عنوان یک روز غیبت حساب می‌شود و از حقوق کسر می‌شود.</li>
          <li v-else>اگر روز ناقصی باشد ماه بسته نمی‌شود و ابتدا باید ترددها اصلاح شود.</li>
          <li>برای کارکنانی که حقوق ماه آن‌ها تایید شده، برگه تغییر نمی‌کند.</li>
          <li>این مرحله‌ی اول است: برگه‌ها نوشته و قفل می‌شوند و ارقام ثابت (snapshot) می‌شوند؛ حقوق فقط پس از تایید مرحله‌ی دوم می‌تواند از ماه استفاده کند.</li>
          <li>بستن ماه جاری یا ماهی که تمام نشده ممکن نیست. بستن دوباره، برگه‌های قبلی را به‌روز می‌کند.</li>
        </ul>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-outline-secondary" @click="confirming = false">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="close">تایید و آماده‌سازی ماه</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
