<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import PayslipPrint from "@/components/fn/print/PayslipPrint.vue";
import QPager from "@/components/quality/QPager.vue";
import { getPayrollEmployees, getPayslip, getPayslips } from "@/api/FnAPIs";
import { camelize, money, monthNames, payrollItemKinds, payrollStatuses, printSheet, usePagedList } from "@/composables/fn";
import { fail } from "@/composables/inventory";

// Payslips (confidential): list by run, month or employee, the detail and the printed form. No salary figure is written to the console.
const route = useRoute();
const filters = ref({ RunId: route.query.runId ? Number(route.query.runId) : (null as number | null), EmployeeId: null as number | null, Year: null as number | null, Month: null as number | null });
const list = usePagedList(getPayslips, filters);
const employee = ref<any>(null);
const detail = ref<any>(null);
const printed = ref<any>(null);
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchEmployees = async (t: string) => rowsOf(camelize((await getPayrollEmployees({ Contains: t, PageSize: 15 }))[0]));
const employeeLabel = (e: any) => `${e.personnelCode} - ${e.firstName || ""} ${e.lastName || ""}`.trim();

function search() {
  filters.value.EmployeeId = employee.value?.id || null;
  list.search();
}
async function open(id: number, print = false) {
  const [d, error] = await getPayslip(id);
  if (error) return fail(error);
  if (print) {
    printed.value = d;
    await nextTick();
    printSheet();
  } else detail.value = d;
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <PayslipPrint v-if="printed" :p="printed" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">فیش‌های حقوقی (محرمانه)</h1><router-link :to="{ name: 'FnPayrollRuns' }" class="btn btn-sm btn-outline-secondary">اجراهای حقوق</router-link></div>
      <div class="inv-filters mb-3">
        <input v-model.number="filters.Year" class="form-control form-control-sm fn-num" placeholder="سال" style="max-width: 7rem" />
        <select v-model="filters.Month" class="form-select form-select-sm"><option :value="null">همه ماه‌ها</option><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select>
        <div style="min-width: 15rem"><LookupBox v-model="employee" :fetch="searchEmployees" :label="employeeLabel" placeholder="کارمند" /></div>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
        <button v-if="filters.RunId" class="btn btn-sm btn-outline-secondary" @click="filters.RunId = null; search()">همه اجراها</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>کد پرسنلی</th><th>نام</th><th>دوره</th><th>روز پرداختی</th><th>ناخالص</th><th>بیمه</th><th>مالیات</th><th>خالص</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="10" class="text-center inv-muted">فیشی نیست.</td></tr>
            <tr v-for="p in list.rows.value" :key="p.id">
              <td class="inv-ltr">{{ p.personnelCode }}</td><td>{{ p.employeeName }}</td><td>{{ monthNames[p.month - 1] }} {{ p.year }}</td><td>{{ p.daysPaid }}</td>
              <td class="fn-num">{{ money(p.grossEarnings) }}</td><td class="fn-num">{{ money(p.employeeInsurance) }}</td><td class="fn-num">{{ money(p.tax) }}</td><td class="fn-num">{{ money(p.net) }}</td>
              <td>{{ payrollStatuses[p.runStatus] }}</td>
              <td class="text-nowrap"><button class="btn btn-sm btn-link p-0 me-2" @click="open(p.id)">مشاهده</button><button class="btn btn-sm btn-link p-0" @click="open(p.id, true)">چاپ</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ detail.employeeName }} - {{ monthNames[detail.month - 1] }} {{ detail.year }}</h2>
        <table class="table table-sm">
          <thead><tr><th>کد</th><th>عنوان</th><th>نوع</th><th>مبنا</th><th>مبلغ (ریال)</th></tr></thead>
          <tbody>
            <tr v-for="l in detail.lines" :key="l.rowNumber"><td class="inv-ltr">{{ l.itemCode }}</td><td>{{ l.title }}</td><td>{{ payrollItemKinds[l.kind] }}</td><td class="fn-num">{{ l.base ? money(l.base) : "" }}</td><td class="fn-num">{{ money(l.amount) }}</td></tr>
          </tbody>
          <tfoot>
            <tr class="fn-total"><td colspan="4">ناخالص</td><td class="fn-num">{{ money(detail.grossEarnings) }}</td></tr>
            <tr class="fn-total"><td colspan="4">خالص پرداختی</td><td class="fn-num">{{ money(detail.net) }}</td></tr>
          </tfoot>
        </table>
        <div class="text-end"><button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
