<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import { getAttendance, getLeaveBalances, getPayrollEmployees, importAttendance, saveAttendance } from "@/api/FnAPIs";
import { camelize, jalaliNow, monthNames, parseAmount, usePagedList } from "@/composables/fn";
import { cellNumber, cellText, readExcel } from "@/composables/fnExcel";
import type { ExcelField, ExcelSheet } from "@/composables/fnExcel";
import { fail, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Monthly attendance (days, overtime, leave ...), its Excel import and the leave balances. The numbers are the input of the payroll run.
const tab = ref("attendance");
const tabs = [{ key: "attendance", title: "کارکرد ماهانه" }, { key: "import", title: "ورود از اکسل" }, { key: "leave", title: "مانده مرخصی" }];
const now = jalaliNow();
const year = ref(String(now.year));
const month = ref(now.month);
const canWrite = hasRole(FnRoles.payrollUsers);
const busy = ref(false);
const filters = computed(() => ({ Year: parseAmount(year.value) || 0, Month: month.value }));
const list = usePagedList((p: any) => getAttendance({ ...p, ...filters.value }), ref({}));
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);

const numericFields: [string, string][] = [["daysWorked", "روز کارکرد"], ["overtimeHours", "اضافه‌کار (ساعت)"], ["nightHours", "شب‌کاری (ساعت)"], ["holidayHours", "تعطیل‌کاری (ساعت)"], ["paidLeaveDays", "مرخصی با حقوق (روز)"], ["unpaidLeaveDays", "مرخصی بدون حقوق (روز)"], ["absenceDays", "غیبت (روز)"], ["missionDays", "ماموریت (روز)"]];
const form = ref<any>(null);
const searchEmployees = async (t: string) => rowsOf(camelize((await getPayrollEmployees({ Contains: t, PageSize: 15, IsActive: true }))[0]));
const employeeLabel = (e: any) => `${e.personnelCode} - ${e.firstName || ""} ${e.lastName || ""}`.trim();

function edit(a: any | null) {
  form.value = a
    ? { employee: { id: a.employeeId, personnelCode: a.personnelCode, firstName: a.employeeName, lastName: "" }, shiftType: a.shiftType, ...Object.fromEntries(numericFields.map(([k]) => [k, String(a[k] ?? 0)])), inputs: Object.entries(a.inputs || {}).map(([k, v]) => ({ key: k, value: String(v) })) }
    : { employee: null, shiftType: 0, ...Object.fromEntries(numericFields.map(([k]) => [k, "0"])), inputs: [] };
}
async function save() {
  const f = form.value;
  if (!f.employee) return fail("کارمند را انتخاب کنید.");
  const inputs: Record<string, number> = {};
  for (const i of f.inputs) if (String(i.key).trim()) inputs[String(i.key).trim()] = parseAmount(i.value) || 0;
  busy.value = true;
  const [, error] = await saveAttendance({ employeeId: f.employee.id, year: filters.value.Year, month: month.value, shiftType: Number(f.shiftType) || 0, inputs, ...Object.fromEntries(numericFields.map(([k]) => [k, parseAmount(f[k]) || 0])) });
  busy.value = false;
  if (error) return fail(error);
  ok("کارکرد ذخیره شد.");
  form.value = null;
  list.load();
}

// Excel import: fixed columns plus any column whose header is an English code (it becomes an input of the payroll items).
const fields: ExcelField[] = [
  { key: "personnelCode", label: "کد پرسنلی *", words: ["کد پرسنلی", "پرسنلی", "personnelcode", "code"] },
  { key: "daysWorked", label: "روز کارکرد", words: ["کارکرد", "روز کارکرد", "days", "daysworked"] },
  { key: "overtimeHours", label: "اضافه‌کار", words: ["اضافه کار", "اضافه‌کار", "اضافه کاری", "overtime"] },
  { key: "nightHours", label: "شب‌کاری", words: ["شب کاری", "شب‌کاری", "night"] },
  { key: "holidayHours", label: "تعطیل‌کاری", words: ["تعطیل کاری", "تعطیل‌کاری", "holiday"] },
  { key: "paidLeaveDays", label: "مرخصی با حقوق", words: ["مرخصی با حقوق", "مرخصی", "paidleave"] },
  { key: "unpaidLeaveDays", label: "مرخصی بدون حقوق", words: ["مرخصی بدون حقوق", "unpaidleave"] },
  { key: "absenceDays", label: "غیبت", words: ["غیبت", "absence"] },
  { key: "missionDays", label: "ماموریت", words: ["ماموریت", "mission"] },
];
const sheet = ref<ExcelSheet | null>(null);
const result = ref<any>(null);
async function pickFile(e: any) {
  const file = e.target.files?.[0];
  e.target.value = "";
  if (!file) return;
  result.value = null;
  try {
    sheet.value = await readExcel(file, fields);
  } catch {
    fail("فایل خوانده نشد. فایل باید xlsx باشد.");
  }
}
const extraColumns = computed(() => (sheet.value ? sheet.value.headers.map((h, i) => ({ h, i })).filter((c) => /^[A-Za-z][A-Za-z0-9_]*$/.test(c.h) && !Object.values(sheet.value!.map).includes(c.i)) : []));
async function runImport() {
  const s = sheet.value!;
  if (s.map.personnelCode < 0) return fail("ستون کد پرسنلی را مشخص کنید.");
  const rows = s.rows
    .map((r) => {
      const inputs: Record<string, number> = {};
      for (const c of extraColumns.value) {
        const v = parseAmount(String(r[c.i] ?? ""));
        if (v) inputs[c.h] = v;
      }
      const row: any = { personnelCode: cellText(s, r, "personnelCode"), year: filters.value.Year, month: month.value, inputs };
      for (const [k] of numericFields) row[k] = cellNumber(s, r, k) ?? 0;
      return row;
    })
    .filter((r) => r.personnelCode);
  if (!rows.length) return fail("ردیفی برای ورود نیست.");
  busy.value = true;
  const [d, error] = await importAttendance(filters.value.Year, month.value, rows);
  busy.value = false;
  if (error) return fail(error);
  result.value = d;
  ok(`${d?.imported ?? 0} ردیف کارکرد وارد شد.`);
  list.search();
}

const leave = ref<any[]>([]);
async function loadLeave() {
  const [d, error] = await getLeaveBalances(filters.value.Year);
  if (error) return fail(error);
  leave.value = rowsOf(camelize(d));
}
function show() {
  list.search();
  if (tab.value === "leave") loadLeave();
}
function setTab(k: string) {
  tab.value = k;
  if (k === "leave") loadLeave();
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">کارکرد و مرخصی (محرمانه)</h1>
        <button v-if="canWrite && tab === 'attendance'" class="btn btn-sm btn-primary" @click="edit(null)">ثبت کارکرد</button>
      </div>
      <div class="inv-filters mb-3">
        <div><label class="form-label small mb-0">سال</label><input v-model="year" class="form-control form-control-sm fn-num" style="width: 6rem" /></div>
        <div v-if="tab !== 'leave'"><label class="form-label small mb-0">ماه</label><select v-model="month" class="form-select form-select-sm"><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select></div>
        <button class="btn btn-sm btn-secondary align-self-end" @click="show">نمایش</button>
      </div>
      <PcTabs :model-value="tab" :tabs="tabs" @update:model-value="setTab" />

      <template v-if="tab === 'attendance'">
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>کد پرسنلی</th><th>نام</th><th v-for="f in numericFields" :key="f[0]">{{ f[1] }}</th><th>ورودی‌ها</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!list.rows.value.length"><td :colspan="numericFields.length + 4" class="text-center inv-muted">کارکردی برای این ماه ثبت نشده است.</td></tr>
              <tr v-for="a in list.rows.value" :key="a.id">
                <td class="inv-ltr">{{ a.personnelCode }}</td><td>{{ a.employeeName }}</td><td v-for="f in numericFields" :key="f[0]" class="fn-num">{{ a[f[0]] }}</td>
                <td class="inv-ltr small">{{ Object.keys(a.inputs || {}).join("، ") }}</td>
                <td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="edit(a)">ویرایش</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      </template>

      <template v-if="tab === 'import'">
        <p class="inv-muted small">ردیف اول فایل عنوان ستون‌هاست. ستون‌هایی که عنوان آن‌ها کد انگلیسی است (مثلاً BONUS) به‌عنوان ورودی اقلام حقوق خوانده می‌شوند. کارکرد ماه و سال انتخاب‌شده در بالا ثبت می‌شود.</p>
        <label v-if="canWrite" class="btn btn-sm btn-outline-primary">انتخاب فایل xlsx<input accept=".xlsx,.xls" class="d-none" type="file" @change="pickFile" /></label>
        <div v-if="sheet" class="mt-3">
          <p class="small">فایل: {{ sheet.fileName }} - {{ sheet.rows.length }} ردیف - {{ monthNames[month - 1] }} {{ year }}</p>
          <div class="row g-2">
            <div v-for="f in fields" :key="f.key" class="col-md-3"><label class="form-label small mb-0">{{ f.label }}</label><select v-model="sheet.map[f.key]" class="form-select form-select-sm"><option :value="-1">- ندارد -</option><option v-for="(h, i) in sheet.headers" :key="i" :value="i">{{ h || "ستون " + (i + 1) }}</option></select></div>
          </div>
          <p v-if="extraColumns.length" class="small mt-2 mb-0">ورودی‌های اقلام: <span v-for="c in extraColumns" :key="c.i" class="inv-badge info me-1 inv-ltr">{{ c.h }}</span></p>
          <button :disabled="busy" class="btn btn-sm btn-primary mt-3" @click="runImport">ورود کارکرد</button>
        </div>
        <div v-if="result" class="mt-3">
          <span class="inv-badge ok me-1">{{ result.imported }} ردیف وارد شد</span><span :class="result.errors?.length ? 'bad' : 'ok'" class="inv-badge">{{ result.errors?.length || 0 }} خطا</span>
          <ul v-if="result.errors?.length" class="small mt-2"><li v-for="(e, i) in result.errors" :key="i">{{ e }}</li></ul>
        </div>
      </template>

      <template v-if="tab === 'leave'">
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>کد پرسنلی</th><th>سال</th><th>مانده از سال قبل</th><th>استحقاق</th><th>استفاده‌شده</th><th>مانده</th></tr></thead>
            <tbody>
              <tr v-if="!leave.length"><td colspan="6" class="text-center inv-muted">اطلاعاتی نیست.</td></tr>
              <tr v-for="l in leave" :key="l.employeeId"><td class="inv-ltr">{{ l.personnelCode }}</td><td>{{ l.year }}</td><td class="fn-num">{{ l.carriedForward }}</td><td class="fn-num">{{ l.entitled }}</td><td class="fn-num">{{ l.used }}</td><td class="fn-num">{{ l.balance }}</td></tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">کارکرد {{ monthNames[month - 1] }} {{ year }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">کارمند *</label><LookupBox v-model="form.employee" :fetch="searchEmployees" :label="employeeLabel" placeholder="کد پرسنلی یا نام" /></div>
          <div v-for="f in numericFields" :key="f[0]" class="col-md-3"><label class="form-label small mb-0">{{ f[1] }}</label><input v-model="form[f[0]]" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12">
            <label class="form-label small mb-0">ورودی‌های اقلام حقوق (کد قلم و مقدار)</label>
            <div v-for="(i, n) in form.inputs" :key="n" class="d-flex gap-2 mb-1"><input v-model="i.key" class="form-control form-control-sm inv-ltr" placeholder="کد" /><input v-model="i.value" class="form-control form-control-sm fn-num" placeholder="مقدار" /><button class="btn btn-sm btn-link text-danger" @click="form.inputs.splice(n, 1)">حذف</button></div>
            <button class="btn btn-sm btn-outline-secondary" @click="form.inputs.push({ key: '', value: '' })">ورودی جدید</button>
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
