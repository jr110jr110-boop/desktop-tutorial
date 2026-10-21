<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { getEmployeeYear, getInsuranceList, getPayrollEmployees, getPayrollTaxList, getWorkshops } from "@/api/FnAPIs";
import { camelize, jalaliNow, money, monthNames, parseAmount } from "@/composables/fn";
import { fail, jalali } from "@/composables/inventory";

// Payroll reports (confidential): the monthly social-insurance list per workshop, the monthly salary-tax list and the yearly summary of one employee.
const tab = ref("insurance");
const tabs = [{ key: "insurance", title: "لیست بیمه" }, { key: "tax", title: "لیست مالیات حقوق" }, { key: "employee", title: "خلاصه سالانه کارمند" }];
const now = jalaliNow();
const year = ref(String(now.year));
const month = ref(now.month);
const workshopId = ref<number | null>(null);
const workshops = ref<any[]>([]);
const employee = ref<any>(null);
const insurance = ref<any>(null);
const taxList = ref<any>(null);
const summary = ref<any>(null);
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchEmployees = async (t: string) => rowsOf(camelize((await getPayrollEmployees({ Contains: t, PageSize: 15 }))[0]));
const employeeLabel = (e: any) => `${e.personnelCode} - ${e.firstName || ""} ${e.lastName || ""}`.trim();

async function show() {
  const y = parseAmount(year.value);
  if (!y) return fail("سال را وارد کنید.");
  if (tab.value === "insurance") {
    const [d, error] = await getInsuranceList(y, month.value, workshopId.value);
    if (error) return fail(error);
    insurance.value = d;
  } else if (tab.value === "tax") {
    const [d, error] = await getPayrollTaxList(y, month.value);
    if (error) return fail(error);
    taxList.value = d;
  } else {
    if (!employee.value) return fail("کارمند را انتخاب کنید.");
    const [d, error] = await getEmployeeYear(employee.value.id, y);
    if (error) return fail(error);
    summary.value = d;
  }
}
onMounted(async () => {
  workshops.value = rowsOf((await getWorkshops())[0]);
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">گزارش‌های حقوق و بیمه (محرمانه)</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <div class="inv-filters mb-3">
        <div><label class="form-label small mb-0">سال</label><input v-model="year" class="form-control form-control-sm fn-num" style="width: 6rem" /></div>
        <div v-if="tab !== 'employee'"><label class="form-label small mb-0">ماه</label><select v-model="month" class="form-select form-select-sm"><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select></div>
        <div v-if="tab === 'insurance'"><label class="form-label small mb-0">کارگاه</label><select v-model="workshopId" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="w in workshops" :key="w.id" :value="w.id">{{ w.name }}</option></select></div>
        <div v-if="tab === 'employee'" style="min-width: 16rem"><label class="form-label small mb-0">کارمند</label><LookupBox v-model="employee" :fetch="searchEmployees" :label="employeeLabel" placeholder="کد پرسنلی یا نام" /></div>
        <button class="btn btn-sm btn-secondary align-self-end" @click="show">نمایش</button>
      </div>

      <div v-if="tab === 'insurance' && insurance" class="table-responsive">
        <p class="small">کارگاه {{ insurance.workshopName }} <span class="inv-ltr">{{ insurance.workshopCode }}</span> - {{ insurance.employees }} نفر</p>
        <table class="table table-sm">
          <thead><tr><th>کد پرسنلی</th><th>شماره بیمه</th><th>کد ملی</th><th>نام</th><th>نام پدر</th><th>تولد</th><th>کد شغل</th><th>روز</th><th>دستمزد روزانه</th><th>دستمزد ماهانه</th><th>مزایا</th><th>جمع مشمول</th><th>مشمول سقف‌دار</th><th>سهم کارمند</th><th>سهم کارفرما</th><th>بیکاری</th></tr></thead>
          <tbody>
            <tr v-for="r in insurance.rows" :key="r.personnelCode">
              <td class="inv-ltr">{{ r.personnelCode }}</td><td class="inv-ltr">{{ r.insuranceNumber }}</td><td class="inv-ltr">{{ r.nationalCode }}</td><td>{{ r.firstName }} {{ r.lastName }}</td><td>{{ r.fatherName }}</td><td>{{ r.birthDate ? jalali(r.birthDate) : "" }}</td><td class="inv-ltr">{{ r.jobCode }}</td><td>{{ r.daysWorked }}</td>
              <td class="fn-num">{{ money(r.dailyWage) }}</td><td class="fn-num">{{ money(r.monthlyWage) }}</td><td class="fn-num">{{ money(r.benefits) }}</td><td class="fn-num">{{ money(r.insurableTotal) }}</td><td class="fn-num">{{ money(r.insurableCapped) }}</td><td class="fn-num">{{ money(r.employeeShare) }}</td><td class="fn-num">{{ money(r.employerShare) }}</td><td class="fn-num">{{ money(r.unemployment) }}</td>
            </tr>
          </tbody>
          <tfoot><tr class="fn-total"><td colspan="11">جمع</td><td class="fn-num">{{ money(insurance.totalInsurable) }}</td><td></td><td class="fn-num">{{ money(insurance.totalEmployeeShare) }}</td><td class="fn-num">{{ money(insurance.totalEmployerShare) }}</td><td class="fn-num">{{ money(insurance.totalUnemployment) }}</td></tr></tfoot>
        </table>
      </div>

      <div v-if="tab === 'tax' && taxList" class="table-responsive">
        <p class="small">{{ taxList.employees }} نفر</p>
        <table class="table table-sm">
          <thead><tr><th>کد پرسنلی</th><th>کد ملی</th><th>نام</th><th>مشمول مالیات</th><th>بیمه سهم کارمند</th><th>مالیات</th></tr></thead>
          <tbody>
            <tr v-for="r in taxList.rows" :key="r.personnelCode"><td class="inv-ltr">{{ r.personnelCode }}</td><td class="inv-ltr">{{ r.nationalCode }}</td><td>{{ r.firstName }} {{ r.lastName }}</td><td class="fn-num">{{ money(r.taxableTotal) }}</td><td class="fn-num">{{ money(r.employeeInsurance) }}</td><td class="fn-num">{{ money(r.tax) }}</td></tr>
          </tbody>
          <tfoot><tr class="fn-total"><td colspan="3">جمع</td><td class="fn-num">{{ money(taxList.totalTaxable) }}</td><td></td><td class="fn-num">{{ money(taxList.totalTax) }}</td></tr></tfoot>
        </table>
      </div>

      <div v-if="tab === 'employee' && summary" class="table-responsive">
        <table class="table table-sm" style="max-width: 520px">
          <tbody>
            <tr><th>کارمند</th><td>{{ summary.personnelCode }} - {{ summary.employeeName }}</td></tr>
            <tr><th>سال</th><td>{{ summary.year }}</td></tr>
            <tr><th>تعداد ماه‌های ثبت‌شده</th><td>{{ summary.months }}</td></tr>
            <tr><th>جمع ناخالص</th><td class="fn-num">{{ money(summary.gross) }}</td></tr>
            <tr><th>بیمه سهم کارمند</th><td class="fn-num">{{ money(summary.employeeInsurance) }}</td></tr>
            <tr><th>مالیات</th><td class="fn-num">{{ money(summary.tax) }}</td></tr>
            <tr><th>سایر کسور</th><td class="fn-num">{{ money(summary.otherDeductions) }}</td></tr>
            <tr class="fn-total"><th>خالص پرداختی</th><td class="fn-num">{{ money(summary.net) }}</td></tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
