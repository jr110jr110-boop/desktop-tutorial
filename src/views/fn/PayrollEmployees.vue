<script setup lang="ts">
import UserPick from "@/components/office/wf/UserPick.vue";
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import ApprovalTable from "@/components/common/ApprovalTable.vue";
import type { ApprovalAction, ApprovalColumn } from "@/components/common/approvalTypes";
import { employeeChangeAction, getEmployeeChanges, getEmployeeWages, getLeaveOpenings, getLoans, getPayrollEmployee, getPayrollEmployees, getWorkshops, importLeaveOpenings, leaveOpeningAction, loanAction, saveLeaveOpening, saveLoan, savePayrollEmployee } from "@/api/FnAPIs";
import { getCostCenters } from "@/api/FinanceAPIs";
import { camelize, employmentTypes, genders, jalaliNow, loanKinds, maritalStatuses, money, monthNames, parseAmount, usePagedList } from "@/composables/fn";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Payroll employees and loans / advances (confidential: PAYROLL, FINANCEMANAGER and CEO only; changes by PAYROLL and FINANCEMANAGER).
const tab = ref("employees");
const tabs = [{ key: "employees", title: "پرسنل" }, { key: "changes", title: "درخواست‌های تغییر پرسنل" }, { key: "loans", title: "وام و مساعده" }, { key: "openings", title: "مانده اول دوره مرخصی" }];
const eFilters = ref({ WorkshopId: null as number | null, IsActive: true as boolean | null, Contains: "" });
const employees = usePagedList(getPayrollEmployees, eFilters);
const lFilters = ref({ EmployeeId: null as number | null, IsActive: null as boolean | null });
const loans = usePagedList(getLoans, lFilters);
const workshops = ref<any[]>([]);
const costCenters = ref<any[]>([]);
const form = ref<any>(null);
const loanForm = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.payrollUsers);
/** the people who may decide the requests of this page: payroll, the finance manager and the CEO (the API refuses the one who made the request, and a record of his own) */
const canDecide = hasRole(["PAYROLL", "FINANCEMANAGER", "CEO"]);
const isFinanceManager = hasRole(["FINANCEMANAGER", "CEO"]);
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchEmployees = async (t: string) => rowsOf(camelize((await getPayrollEmployees({ Contains: t, PageSize: 15, IsActive: true }))[0]));
const employeeLabel = (e: any) => `${e.personnelCode} - ${e.firstName} ${e.lastName}`;

const blankEmployee = () => ({ id: null, personnelCode: "", firstName: "", lastName: "", fatherName: "", nationalCode: "", birthDate: null, birthPlace: "", gender: 1, maritalStatus: 1, childrenCount: 0, insuranceNumber: "", insuranceJobCode: "", workshopId: null, hireDate: today(), endDate: null, employmentType: 1, jobTitle: "", unit: "", costCenterId: null, bankIban: "", dailyWage: "", exemptionFlags: "", hardAndHarmful: false, userId: null, isActive: true, identityCardNumber: "", nationality: "", changeReason: "", wageEffectiveYear: "", wageEffectiveMonth: "" as number | "" });

async function edit(row: any) {
  const [d, error] = await getPayrollEmployee(row.id);
  if (error) return fail(error);
  const e = camelize(d);
  form.value = { ...e, birthDate: e.birthDate ? String(e.birthDate).slice(0, 10) : null, hireDate: String(e.hireDate).slice(0, 10), endDate: e.endDate ? String(e.endDate).slice(0, 10) : null, dailyWage: String(e.dailyWage), bankIban: e.bankIban || "", exemptionFlags: e.exemptionFlags || "", changeReason: "", wageEffectiveYear: "", wageEffectiveMonth: "", identityCardNumber: e.identityCardNumber || "", nationality: e.nationality || "", storedWage: String(e.dailyWage), wages: [] as any[] };
  if (e.id) {
    const [w] = await getEmployeeWages(e.id);
    form.value.wages = Array.isArray(w) ? w : [];
  }
}
async function save() {
  const f = form.value;
  const wage = parseAmount(f.dailyWage);
  if (!String(f.personnelCode).trim() || !String(f.firstName).trim() || !String(f.lastName).trim()) return fail("کد پرسنلی، نام و نام خانوادگی را وارد کنید.");
  if (!wage || wage <= 0) return fail("مزد روزانه را وارد کنید.");
  // a guarded field (daily wage, exemption flags, children, hard-and-harmful, IBAN) changes only through a request that a second person approves: the reason is mandatory
  const guarded = !f.id || String(f.storedWage) !== String(f.dailyWage);
  if (f.id && guarded && !String(f.changeReason).trim()) return fail("برای تغییر مزد روزانه (و فیلدهای محافظت‌شده) دلیل تغییر را بنویسید؛ تغییر پس از تایید نفر دوم اعمال می‌شود.");
  busy.value = true;
  const [data, error] = await savePayrollEmployee({
    identityCardNumber: f.identityCardNumber || null, nationality: f.nationality || null, changeReason: String(f.changeReason || "").trim() || null,
    wageEffectiveYear: parseAmount(String(f.wageEffectiveYear)) || null, wageEffectiveMonth: f.wageEffectiveMonth ? Number(f.wageEffectiveMonth) : null,
    id: f.id, personnelCode: f.personnelCode, firstName: f.firstName, lastName: f.lastName, fatherName: f.fatherName || null, nationalCode: f.nationalCode, birthDate: f.birthDate, birthPlace: f.birthPlace || null,
    gender: Number(f.gender), maritalStatus: Number(f.maritalStatus), childrenCount: parseAmount(String(f.childrenCount)) || 0, insuranceNumber: f.insuranceNumber || null, insuranceJobCode: f.insuranceJobCode || null,
    workshopId: f.workshopId, hireDate: f.hireDate, endDate: f.endDate, employmentType: Number(f.employmentType), jobTitle: f.jobTitle || null, unit: f.unit || null, costCenterId: f.costCenterId,
    defaultWorkOrderId: f.defaultWorkOrderId || null, bankIban: f.bankIban ? String(f.bankIban).replace(/\s/g, "").toUpperCase() : null, dailyWage: wage, exemptionFlags: f.exemptionFlags || null, hardAndHarmful: !!f.hardAndHarmful,
    userId: f.userId || null, qualifiedPersonId: f.qualifiedPersonId || null, isActive: !!f.isActive,
  });
  busy.value = false;
  if (error) return fail(error);
  ok(data?.pendingApproval || data?.pendingChangeId ? "ذخیره شد؛ کارمند جدید یا تغییر فیلدهای محافظت‌شده تا تایید نفر دوم (پرسنل/مدیر مالی) در حقوق اعمال نمی‌شود." : "اطلاعات پرسنل ذخیره شد.");
  form.value = null;
  employees.load();
}

const now = jalaliNow();
const blankLoan = () => ({ id: null, employee: null as any, kind: 1, title: "", principal: "", installmentAmount: "", firstYear: String(now.year), firstMonth: now.month, isActive: true });
function editLoan(l: any) {
  loanForm.value = { id: l.id, employee: { id: l.employeeId, personnelCode: l.personnelCode, firstName: "", lastName: "" }, kind: l.kind, title: l.title || "", principal: String(l.principal), installmentAmount: String(l.installmentAmount), firstYear: String(l.firstYear), firstMonth: l.firstMonth, isActive: l.isActive, started: l.balance !== l.principal };
}
async function saveL() {
  const f = loanForm.value;
  if (!f.employee) return fail("کارمند را انتخاب کنید.");
  const principal = parseAmount(f.principal), inst = parseAmount(f.installmentAmount);
  if (!principal || !inst) return fail("مبلغ وام و قسط را وارد کنید.");
  busy.value = true;
  const [, error] = await saveLoan({ id: f.id, employeeId: f.employee.id, kind: Number(f.kind), title: f.title || null, principal, installmentAmount: inst, firstYear: parseAmount(f.firstYear) || 0, firstMonth: Number(f.firstMonth), isActive: !!f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  loanForm.value = null;
  loans.load();
}

const loanApproval: Record<string, string> = { Pending: "در انتظار تایید مدیر مالی", Approved: "تاییدشده", Rejected: "ردشده" };
const loanReject = ref<any>(null);
async function decideLoan(l: any, action: "approve" | "reject", note: string | null = null) {
  if (action === "reject" && note === null) { loanReject.value = { loan: l, note: "" }; return; }
  busy.value = true;
  const [, error] = await loanAction(l.id, action, note);
  busy.value = false;
  loanReject.value = null;
  if (error) fail(error);
  else ok(action === "approve" ? "وام تایید شد." : "وام رد شد.");
  loans.load();
}

// requests: employee changes and opening leave balances
const changeCols: ApprovalColumn[] = [
  { key: "employeeName", title: "کارمند", text: (r) => `${r.employeeName} (${r.personnelCode})` },
  { key: "kind", title: "نوع", text: (r) => (r.kind === "Create" ? "کارمند جدید" : "تغییر") },
  { key: "change", title: "تغییر", text: (r) => changeText(r) },
  { key: "reason", title: "دلیل" },
  { key: "proposedByName", title: "درخواست‌دهنده" },
  { key: "status", title: "وضعیت", text: (r) => ({ Pending: "در انتظار", Approved: "تاییدشده", Rejected: "ردشده" } as any)[r.status] || r.status, badge: (r) => (r.status === "Approved" ? "ok" : r.status === "Rejected" ? "bad" : "warn") },
  { key: "decision", title: "تصمیم", text: (r) => (r.decidedByName ? `${r.decidedByName}${r.decisionNote ? ": " + r.decisionNote : ""}` : "") },
];
function changeText(r: any): string {
  const c = r.current || {}, p = r.proposed || {};
  const parts: string[] = [];
  if (r.kind === "Create" || c.dailyWage !== p.dailyWage) parts.push(`مزد روزانه ${money(c.dailyWage)} ← ${money(p.dailyWage)}${r.wageEffectiveFrom ? " از " + jalali(r.wageEffectiveFrom) : ""}`);
  if (r.kind === "Create" || c.childrenCount !== p.childrenCount) parts.push(`فرزند ${c.childrenCount ?? "-"} ← ${p.childrenCount}`);
  if (r.kind === "Create" || c.hardAndHarmful !== p.hardAndHarmful) parts.push(`سخت و زیان‌آور ${p.hardAndHarmful ? "بله" : "خیر"}`);
  if (r.kind === "Create" || (c.exemptionFlags || "") !== (p.exemptionFlags || "")) parts.push(`پرچم معافیت «${p.exemptionFlags || "-"}»`);
  if (r.kind === "Create" || (c.bankIban || "") !== (p.bankIban || "")) parts.push(`شبا ${p.bankIban || "-"}`);
  return parts.join("، ");
}
const decisionActions = (row: any): ApprovalAction[] =>
  row.status === "Pending" && canDecide
    ? [{ key: "approve", label: "تایید", btn: "btn-success", reason: "optional", done: "تایید شد." }, { key: "reject", label: "رد", btn: "btn-outline-danger", reason: true, hint: "دلیل رد را بنویسید.", done: "رد شد." }]
    : [];
const openingActions = (row: any): ApprovalAction[] => (row.status === "Pending" && isFinanceManager ? decisionActions(row) : []);
const runChange = (row: any, a: string, n: string | null) => employeeChangeAction(row.id, a as any, n);
const runOpening = (row: any, a: string, n: string | null) => leaveOpeningAction(row.id, a as any, n);
const openingCols: ApprovalColumn[] = [
  { key: "employeeName", title: "کارمند", text: (r) => `${r.employeeName} (${r.personnelCode})` },
  { key: "asOf", title: "تا ابتدای ماه", text: (r) => `${r.year}/${String(r.month).padStart(2, "0")}`, ltr: true },
  { key: "carriedDays", title: "روز انتقالی" },
  { key: "note", title: "یادداشت" },
  { key: "enteredByName", title: "واردکننده" },
  { key: "status", title: "وضعیت", text: (r) => ({ Pending: "در انتظار مدیر مالی", Approved: "تاییدشده", Rejected: "ردشده" } as any)[r.status] || r.status, badge: (r) => (r.status === "Approved" ? "ok" : r.status === "Rejected" ? "bad" : "warn") },
  { key: "decision", title: "تصمیم", text: (r) => (r.decidedByName ? `${r.decidedByName}${r.decisionNote ? ": " + r.decisionNote : ""}` : "") },
];
const opening = ref<any>(null);
const openingList = ref<InstanceType<typeof ApprovalTable> | null>(null);
async function saveOpening() {
  const f = opening.value;
  if (!f.employee) return fail("کارمند را انتخاب کنید.");
  const days = Number(String(f.carriedDays).replace(/,/g, ""));
  if (!Number.isFinite(days) || days < 0) return fail("روز انتقالی نامعتبر است.");
  busy.value = true;
  const [, error] = await saveLeaveOpening({ employeeId: f.employee.id, year: parseAmount(String(f.year)) || 0, month: Number(f.month), carriedDays: days, note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("ثبت شد؛ تا تایید مدیر مالی در محاسبه نمی‌آید.");
  opening.value = null;
  openingList.value?.reload();
}
async function importOpenings(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0];
  if (!file) return;
  const rows: any[] = [];
  const lines = (await file.text()).split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  for (const line of lines) {
    const [code, year, month, days, note] = line.split(/[,;\t]/).map((x) => x.trim());
    if (!/^\d+$/.test(year || "")) continue; // a heading row
    rows.push({ personnelCode: code, year: Number(year), month: Number(month), carriedDays: Number(days), note: note || null });
  }
  (e.target as HTMLInputElement).value = "";
  if (!rows.length) return fail("ردیفی خوانده نشد. ستون‌ها: کد پرسنلی، سال، ماه، روز انتقالی، یادداشت (CSV).");
  const [d, error] = await importLeaveOpenings(rows);
  if (error) return fail(error);
  ok(`${d?.imported ?? 0} ردیف ثبت شد و منتظر تایید مدیر مالی است.${d?.errors?.length ? " " + d.errors.length + " خطا." : ""}`);
  if (d?.errors?.length) fail(d.errors.slice(0, 5).join(" | "));
  openingList.value?.reload();
}

onMounted(async () => {
  workshops.value = rowsOf(camelize((await getWorkshops())[0]));
  costCenters.value = (await getCostCenters())[0] || [];
  employees.load();
  loans.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">پرسنل و وام (محرمانه)</h1>
        <button v-if="canWrite && tab === 'employees'" class="btn btn-sm btn-primary" @click="form = blankEmployee()">کارمند جدید</button>
        <button v-if="canWrite && tab === 'loans'" class="btn btn-sm btn-primary" @click="loanForm = blankLoan()">وام / مساعده جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'employees'">
        <div class="inv-filters mb-3">
          <select v-model="eFilters.WorkshopId" class="form-select form-select-sm"><option :value="null">همه کارگاه‌ها</option><option v-for="w in workshops" :key="w.id" :value="w.id">{{ w.name }}</option></select>
          <select v-model="eFilters.IsActive" class="form-select form-select-sm"><option :value="true">فعال</option><option :value="false">غیرفعال</option><option :value="null">همه</option></select>
          <input v-model="eFilters.Contains" class="form-control form-control-sm" placeholder="کد پرسنلی، نام یا کد ملی" @keydown.enter="employees.search" />
          <button class="btn btn-sm btn-secondary" @click="employees.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>کد پرسنلی</th><th>نام</th><th>کارگاه</th><th>نوع استخدام</th><th>سمت</th><th>تاریخ استخدام</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!employees.rows.value.length"><td colspan="8" class="text-center inv-muted">پرسنلی نیست.</td></tr>
              <tr v-for="e in employees.rows.value" :key="e.id">
                <td class="inv-ltr">{{ e.personnelCode }}</td><td>{{ e.firstName }} {{ e.lastName }}</td><td>{{ e.workshopName }}</td><td>{{ employmentTypes[e.employmentType] }}</td><td>{{ e.jobTitle }}</td><td>{{ jalali(e.hireDate) }}</td>
                <td><span :class="e.isActive ? 'ok' : 'bad'" class="inv-badge">{{ e.isActive ? "فعال" : "غیرفعال" }}</span><span v-if="e.pendingApproval" class="inv-badge warn ms-1" title="تا تایید نفر دوم در حقوق نمی‌آید">در انتظار تایید</span><span v-else-if="e.pendingChangeId" class="inv-badge warn ms-1" title="مقادیر نمایش‌داده‌شده تاییدشده‌اند">تغییر در انتظار</span></td>
                <td><button class="btn btn-sm btn-outline-secondary" @click="edit(e)">{{ canWrite ? "ویرایش" : "مشاهده" }}</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="employees.page.value" :total="employees.total.value" @go="employees.go" />
      </template>

      <template v-if="tab === 'changes'">
        <p class="inv-muted small">کارمند جدید و تغییر مزد روزانه، پرچم‌های معافیت، تعداد فرزند، مشاغل سخت و زیان‌آور و شبا «درخواست» است؛ حقوق فقط مقدار تاییدشده را می‌خواند. کسی که کارمند یا تغییر را ثبت یا ویرایش کرده، و خود کارمند، نمی‌تواند تصمیم بگیرد؛ رکورد مدیر مالی را مدیرعامل تصمیم می‌گیرد.</p>
        <ApprovalTable :fetcher="getEmployeeChanges" :filters="{ Status: 0 }" :columns="changeCols" :actions="decisionActions" :run="runChange" empty-text="درخواست تغییری در انتظار نیست." @changed="employees.load()" />
      </template>

      <template v-if="tab === 'openings'">
        <div class="d-flex gap-2 mb-2 flex-wrap">
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="opening = { employee: null, year: String(now.year), month: now.month, carriedDays: '', note: '' }">ثبت مانده اول دوره</button>
          <label v-if="canWrite" class="btn btn-sm btn-outline-secondary mb-0">ورود از فایل CSV<input type="file" accept=".csv,.txt" class="d-none" @change="importOpenings" /></label>
        </div>
        <p class="inv-muted small">مانده‌ی روز مرخصی انتقالی تا ابتدای یک ماه. ثبت با پرسنل است و تایید با مدیر مالی (نه واردکننده). ستون‌های فایل: کد پرسنلی، سال، ماه، روز انتقالی، یادداشت.</p>
        <ApprovalTable ref="openingList" :fetcher="getLeaveOpenings" :filters="{}" :columns="openingCols" :actions="openingActions" :run="runOpening" empty-text="مانده‌ای ثبت نشده است." />
      </template>

      <template v-if="tab === 'loans'">
        <div class="inv-filters mb-3">
          <select v-model="lFilters.IsActive" class="form-select form-select-sm" @change="loans.search"><option :value="null">همه</option><option :value="true">فعال</option><option :value="false">غیرفعال</option></select>
        </div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>کد پرسنلی</th><th>نوع</th><th>عنوان</th><th>مبلغ</th><th>قسط</th><th>مانده</th><th>شروع کسر</th><th>وضعیت</th><th>تایید</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!loans.rows.value.length"><td colspan="10" class="text-center inv-muted">وام یا مساعده‌ای نیست.</td></tr>
              <tr v-for="l in loans.rows.value" :key="l.id">
                <td class="inv-ltr">{{ l.personnelCode }}</td><td>{{ loanKinds[l.kind] }}</td><td>{{ l.title }}</td><td class="fn-num">{{ money(l.principal) }}</td><td class="fn-num">{{ money(l.installmentAmount) }}</td><td class="fn-num">{{ money(l.balance) }}</td>
                <td>{{ monthNames[l.firstMonth - 1] }} {{ l.firstYear }}</td><td><span :class="l.isActive ? 'ok' : 'bad'" class="inv-badge">{{ l.isActive ? "فعال" : "غیرفعال" }}</span></td>
                <td><span :class="l.approvalStatus === 'Approved' ? 'ok' : l.approvalStatus === 'Rejected' ? 'bad' : 'warn'" class="inv-badge">{{ loanApproval[l.approvalStatus] || l.approvalStatus }}</span><div class="inv-muted small">{{ l.createdByName }}<template v-if="l.approvedByName"> / {{ l.approvedByName }}</template></div><div v-if="l.rejectReason" class="inv-muted small">{{ l.rejectReason }}</div></td>
                <td class="text-nowrap">
                  <button v-if="canWrite" class="btn btn-sm btn-outline-secondary me-1" @click="editLoan(l)">ویرایش</button>
                  <template v-if="l.approvalStatus === 'Pending' && isFinanceManager"><button class="btn btn-sm btn-success me-1" @click="decideLoan(l, 'approve')">تایید</button><button class="btn btn-sm btn-outline-danger" @click="decideLoan(l, 'reject')">رد</button></template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="loans.page.value" :total="loans.total.value" @go="loans.go" />
      </template>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "اطلاعات کارمند" : "کارمند جدید" }}</h2>
        <fieldset :disabled="!canWrite" class="border-0 p-0">
          <div class="row g-2">
            <div class="col-md-3"><label class="form-label small mb-0">کد پرسنلی *</label><input v-model="form.personnelCode" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">نام *</label><input v-model="form.firstName" class="form-control form-control-sm" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">نام خانوادگی *</label><input v-model="form.lastName" class="form-control form-control-sm" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">نام پدر</label><input v-model="form.fatherName" class="form-control form-control-sm" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">کد ملی *</label><input v-model="form.nationalCode" class="form-control form-control-sm inv-ltr" maxlength="10" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">تاریخ تولد</label><FnDate v-model="form.birthDate" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">محل تولد</label><input v-model="form.birthPlace" class="form-control form-control-sm" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">جنسیت</label><select v-model="form.gender" class="form-select form-select-sm"><option v-for="(t, k) in genders" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-3"><label class="form-label small mb-0">وضعیت تاهل</label><select v-model="form.maritalStatus" class="form-select form-select-sm"><option v-for="(t, k) in maritalStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-3"><label class="form-label small mb-0">تعداد فرزند</label><input v-model="form.childrenCount" class="form-control form-control-sm fn-num" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">شماره بیمه</label><input v-model="form.insuranceNumber" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">کد شغل بیمه</label><input v-model="form.insuranceJobCode" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-4"><label class="form-label small mb-0">کارگاه بیمه</label><select v-model="form.workshopId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="w in workshops.filter((x) => x.isActive)" :key="w.id" :value="w.id">{{ w.name }}</option></select></div>
            <div class="col-md-4"><label class="form-label small mb-0">نوع استخدام</label><select v-model="form.employmentType" class="form-select form-select-sm"><option v-for="(t, k) in employmentTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-4"><label class="form-label small mb-0">مرکز هزینه</label><select v-model="form.costCenterId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="c in costCenters" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select></div>
            <div class="col-md-3"><label class="form-label small mb-0">سمت</label><input v-model="form.jobTitle" class="form-control form-control-sm" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">واحد</label><input v-model="form.unit" class="form-control form-control-sm" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">تاریخ استخدام *</label><FnDate v-model="form.hireDate" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">تاریخ پایان کار</label><FnDate v-model="form.endDate" /></div>
            <div class="col-md-4"><label class="form-label small mb-0">مزد روزانه (ریال) *</label><input v-model="form.dailyWage" class="form-control form-control-sm fn-num" autocomplete="off" /></div>
            <div class="col-md-5"><label class="form-label small mb-0">شماره شبا</label><input v-model="form.bankIban" class="form-control form-control-sm inv-ltr" autocomplete="off" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">پرچم‌های معافیت</label><input v-model="form.exemptionFlags" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">شماره شناسنامه / کارت ملی</label><input v-model="form.identityCardNumber" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">تابعیت</label><input v-model="form.nationality" class="form-control form-control-sm" /></div>
            <div v-if="form.id" class="col-md-3"><label class="form-label small mb-0">مزد جدید از سال</label><input v-model="form.wageEffectiveYear" class="form-control form-control-sm fn-num" placeholder="پیش‌فرض: ماه جاری" /></div>
            <div v-if="form.id" class="col-md-3"><label class="form-label small mb-0">و ماه</label><select v-model="form.wageEffectiveMonth" class="form-select form-select-sm"><option value="">ماه جاری</option><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select></div>
            <div v-if="form.id" class="col-12"><label class="form-label small mb-0">دلیل تغییر (برای مزد، فرزند، معافیت، سخت و زیان‌آور و شبا الزامی است)</label><input v-model="form.changeReason" class="form-control form-control-sm" /></div>
            <div v-if="form.wages?.length" class="col-12"><div class="small fw-bold">سابقه مزد روزانه</div><div class="small"><span v-for="w in form.wages" :key="w.id || w.effectiveFrom" class="inv-badge info me-1">{{ jalali(w.effectiveFrom) }}: {{ money(w.dailyWage) }}{{ w.isBaseline ? " (پایه)" : "" }}</span></div></div>
            <div class="col-12 col-md-6"><label class="form-label small mb-0">کاربر سامانه (برای مرخصی، کارکرد و ثبت زمان خود فرد)</label><UserPick v-model="form.userId" :fallback-text="form.userId ? 'کاربر شماره ' + form.userId : ''" /></div>
            <div class="col-12 d-flex gap-3"><label class="form-check small"><input v-model="form.hardAndHarmful" class="form-check-input" type="checkbox" /> مشاغل سخت و زیان‌آور</label><label class="form-check small"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
          </div>
        </fieldset>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">بستن</button><button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>

    <div v-if="opening" class="inv-modal-backdrop screen-only" @click.self="opening = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">مانده اول دوره‌ی مرخصی</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">کارمند *</label><LookupBox v-model="opening.employee" :fetch="searchEmployees" :label="employeeLabel" placeholder="کد پرسنلی یا نام" /></div>
          <div class="col-4"><label class="form-label small mb-0">سال</label><input v-model="opening.year" class="form-control form-control-sm fn-num" /></div>
          <div class="col-4"><label class="form-label small mb-0">ماه</label><select v-model="opening.month" class="form-select form-select-sm"><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select></div>
          <div class="col-4"><label class="form-label small mb-0">روز انتقالی *</label><input v-model="opening.carriedDays" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">یادداشت</label><input v-model="opening.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="opening = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveOpening">ثبت</button></div>
      </div>
    </div>

    <ReasonDialog v-if="loanReject" title="رد وام یا مساعده" hint="دلیل رد را بنویسید." :busy="busy" @confirm="(t: string) => decideLoan(loanReject.loan, 'reject', t)" @close="loanReject = null" />

    <div v-if="loanForm" class="inv-modal-backdrop screen-only" @click.self="loanForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">وام / مساعده</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">کارمند *</label><LookupBox v-model="loanForm.employee" :disabled="!!loanForm.id" :fetch="searchEmployees" :label="employeeLabel" :fallback-text="loanForm.employee?.personnelCode" placeholder="کد پرسنلی یا نام" /></div>
          <div class="col-6"><label class="form-label small mb-0">نوع</label><select v-model="loanForm.kind" class="form-select form-select-sm"><option v-for="(t, k) in loanKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">عنوان</label><input v-model="loanForm.title" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">مبلغ (ریال) *</label><input v-model="loanForm.principal" :disabled="loanForm.started" class="form-control form-control-sm fn-num" autocomplete="off" /></div>
          <div class="col-6"><label class="form-label small mb-0">مبلغ قسط (ریال) *</label><input v-model="loanForm.installmentAmount" :disabled="loanForm.started" class="form-control form-control-sm fn-num" autocomplete="off" /></div>
          <div class="col-6"><label class="form-label small mb-0">سال شروع کسر</label><input v-model="loanForm.firstYear" :disabled="loanForm.started" class="form-control form-control-sm fn-num" /></div>
          <div class="col-6"><label class="form-label small mb-0">ماه شروع کسر</label><select v-model="loanForm.firstMonth" :disabled="loanForm.started" class="form-select form-select-sm"><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select></div>
          <div class="col-12"><label class="form-check small"><input v-model="loanForm.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <p v-if="loanForm.started" class="inv-muted small mt-2 mb-0">از این وام قسطی کسر شده است؛ فقط فعال یا غیرفعال کردن آن ممکن است.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="loanForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveL">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
