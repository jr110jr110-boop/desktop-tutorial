<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { createPayrollRun, deletePayrollRun, downloadBankTransferFile, getBankTransfer, getPayrollRuns, getWorkshops, payrollRunStep } from "@/api/FnAPIs";
import { camelize, jalaliNow, money, monthNames, parseAmount, payrollStatuses, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Payroll runs (confidential). Create -> calculate (payroll officer) -> approve (finance manager, another person) -> post to the ledger -> bank transfer file.
const now = jalaliNow();
const filters = ref({ Year: null as number | null, Status: null as number | null });
const list = usePagedList(getPayrollRuns, filters);
const workshops = ref<any[]>([]);
const form = ref<null | { year: string; month: number; workshopId: number | null; note: string }>(null);
const warnings = ref<{ run: any; items: string[] } | null>(null);
const transfer = ref<null | { run: any; rows: any[] }>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.payrollUsers);
const canApprove = hasRole(FnRoles.managers);
const badge = (s: number) => (s >= 3 ? "ok" : s === 2 ? "info" : "warn");
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);

async function create() {
  const f = form.value!;
  busy.value = true;
  const [, error] = await createPayrollRun(parseAmount(f.year) || 0, f.month, f.workshopId, f.note);
  busy.value = false;
  if (error) return fail(error);
  ok("اجرای حقوق ساخته شد؛ اکنون محاسبه کنید.");
  form.value = null;
  list.search();
}

async function step(run: any, name: "calculate" | "approve" | "post") {
  busy.value = true;
  const [d, error] = await payrollRunStep(run.id, name);
  busy.value = false;
  if (error) return fail(error);
  if (name === "calculate") {
    const w = camelize(d)?.warnings || [];
    ok("حقوق محاسبه شد.");
    if (w.length) warnings.value = { run, items: w };
  } else ok(name === "approve" ? "اجرای حقوق تایید شد." : "اجرای حقوق در دفاتر ثبت شد.");
  list.load();
}

async function remove(run: any) {
  if (!confirm("اجرای حقوق حذف شود؟")) return;
  const [, error] = await deletePayrollRun(run.id);
  if (error) return fail(error);
  ok("حذف شد.");
  list.load();
}

async function showTransfer(run: any) {
  const [d, error] = await getBankTransfer(run.id);
  if (error) return fail(error);
  transfer.value = { run, rows: rowsOf(camelize(d)) };
}

async function getFile() {
  const message = await downloadBankTransferFile(transfer.value!.run.id);
  if (message) fail(message);
}

onMounted(async () => {
  workshops.value = rowsOf(camelize((await getWorkshops())[0]));
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">اجرای حقوق و دستمزد (محرمانه)</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = { year: String(now.year), month: now.month, workshopId: null, note: '' }">اجرای جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model.number="filters.Year" class="form-control form-control-sm fn-num" placeholder="سال" style="max-width: 8rem" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in payrollStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>دوره</th><th>تعداد</th><th>ناخالص</th><th>بیمه سهم کارمند</th><th>مالیات</th><th>سایر کسور</th><th>خالص پرداختی</th><th>سهم کارفرما</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="11" class="text-center inv-muted">اجرایی نیست.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ monthNames[r.month - 1] }} {{ r.year }}</td><td>{{ r.employeeCount }}</td>
              <td class="fn-num">{{ money(r.totalGross) }}</td><td class="fn-num">{{ money(r.totalEmployeeInsurance) }}</td><td class="fn-num">{{ money(r.totalTax) }}</td><td class="fn-num">{{ money(r.totalOtherDeductions) }}</td>
              <td class="fn-num">{{ money(r.totalNet) }}</td><td class="fn-num">{{ money(r.totalEmployerInsurance) }}</td>
              <td><span :class="badge(r.status)" class="inv-badge">{{ payrollStatuses[r.status] }}</span></td>
              <td class="text-nowrap">
                <router-link :to="{ name: 'FnPayslips', query: { runId: r.id } }" class="btn btn-sm btn-link p-0 me-2">فیش‌ها</router-link>
                <button v-if="r.status >= 2" class="btn btn-sm btn-link p-0 me-2" @click="showTransfer(r)">فایل پرداخت بانکی</button>
                <template v-if="canWrite && r.status <= 1">
                  <button :disabled="busy" class="btn btn-sm btn-outline-primary me-1" @click="step(r, 'calculate')">{{ r.status === 0 ? "محاسبه" : "محاسبه مجدد" }}</button>
                  <button class="btn btn-sm btn-outline-danger me-1" @click="remove(r)">حذف</button>
                </template>
                <button v-if="canApprove && r.status === 1" :disabled="busy" class="btn btn-sm btn-success me-1" @click="step(r, 'approve')">تایید</button>
                <button v-if="canWrite && r.status === 2" :disabled="busy" class="btn btn-sm btn-success" @click="step(r, 'post')">ثبت در دفاتر</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      <p class="inv-muted small mb-0">تایید فقط توسط مدیر مالی و غیر از محاسبه‌کننده انجام می‌شود. اگر کارکرد یا نرخ‌ها پس از محاسبه تغییر کند، تایید رد می‌شود و باید دوباره محاسبه شود.</p>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">اجرای جدید حقوق</h2>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">سال</label><input v-model="form.year" class="form-control form-control-sm fn-num" /></div>
          <div class="col-6"><label class="form-label small mb-0">ماه</label><select v-model="form.month" class="form-select form-select-sm"><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">کارگاه بیمه (خالی = همه)</label><select v-model="form.workshopId" class="form-select form-select-sm"><option :value="null">همه کارگاه‌ها</option><option v-for="w in workshops.filter((x) => x.isActive)" :key="w.id" :value="w.id">{{ w.name }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ایجاد</button></div>
      </div>
    </div>

    <div v-if="warnings" class="inv-modal-backdrop screen-only" @click.self="warnings = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">هشدارهای محاسبه</h2>
        <ul class="small"><li v-for="(w, i) in warnings.items" :key="i">{{ w }}</li></ul>
        <div class="text-end"><button class="btn btn-sm btn-outline-secondary" @click="warnings = null">بستن</button></div>
      </div>
    </div>

    <div v-if="transfer" class="inv-modal-backdrop screen-only" @click.self="transfer = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">فهرست پرداخت بانکی - {{ monthNames[transfer.run.month - 1] }} {{ transfer.run.year }}</h2>
        <div class="table-responsive" style="max-height: 50vh; overflow-y: auto">
          <table class="table table-sm">
            <thead><tr><th>کد پرسنلی</th><th>نام</th><th>شماره شبا</th><th>مبلغ (ریال)</th></tr></thead>
            <tbody>
              <tr v-for="(r, i) in transfer.rows" :key="i"><td class="inv-ltr">{{ r.personnelCode }}</td><td>{{ r.name }}</td><td class="inv-ltr">{{ r.iban }}</td><td class="fn-num">{{ money(r.amount) }}</td></tr>
            </tbody>
            <tfoot><tr class="fn-total"><td colspan="3">جمع</td><td class="fn-num">{{ money(transfer.rows.reduce((s, r) => s + (r.amount || 0), 0)) }}</td></tr></tfoot>
          </table>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-primary" @click="getFile">دریافت فایل اکسل</button><button class="btn btn-sm btn-outline-secondary" @click="transfer = null">بستن</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
