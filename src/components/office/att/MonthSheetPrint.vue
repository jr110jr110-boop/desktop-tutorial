<script setup lang="ts">
// Printed monthly attendance sheet (A4, right to left, B Zar): one row per employee, as written into the payroll sheet by the close.
import PrintForm from "@/components/print/PrintForm.vue";
import { jalaliTitle } from "@/composables/fn";
import { days2 } from "./attCommon";

const props = defineProps<{ result: any }>();
const cols: [string, string][] = [["daysWorked", "کارکرد"], ["overtimeHours", "اضافه‌کار"], ["nightHours", "شب‌کاری"], ["holidayHours", "تعطیل‌کاری"], ["paidLeaveDays", "مرخصی با حقوق"], ["sickLeaveDays", "استعلاجی"], ["unpaidLeaveDays", "بدون حقوق"], ["absenceDays", "غیبت"], ["missionDays", "ماموریت"]];
const total = (key: string) => (props.result.rows || []).reduce((t: number, r: any) => t + (Number(r[key]) || 0), 0);
</script>

<template>
  <PrintForm
    class="q-form att-sheet"
    title="برگه کارکرد ماهانه"
    :fields="[
      { label: 'دوره', value: jalaliTitle(result.year, result.month) },
      { label: 'تعداد کارکنان', value: String(result.sheets) },
      ...(result.incompleteDays == null ? [] : [{ label: 'روزهای ناقص (تک‌تردد)', value: String(result.incompleteDays) }]),
    ]"
    :signatures="[{ title: 'اداری و منابع انسانی' }, { title: 'حقوق و دستمزد' }, { title: 'مدیرعامل' }]"
    distribution="نسخه ۱: حقوق و دستمزد - نسخه ۲: بایگانی اداری">
    <table class="att-table">
      <thead>
        <tr><th>ردیف</th><th>کد پرسنلی</th><th>نام</th><th v-for="c in cols" :key="c[0]">{{ c[1] }}</th></tr>
      </thead>
      <tbody>
        <tr v-for="(r, i) in result.rows" :key="r.employeeId"><td>{{ i + 1 }}</td><td class="inv-ltr">{{ r.personnelCode }}</td><td>{{ r.employeeName }}</td><td v-for="c in cols" :key="c[0]" class="num inv-ltr">{{ days2(r[c[0]]) }}</td></tr>
        <tr><th colspan="3">جمع</th><td v-for="c in cols" :key="c[0]" class="num inv-ltr"><b>{{ days2(total(c[0])) }}</b></td></tr>
      </tbody>
    </table>
    <p v-if="result.rows?.length" class="att-note">اضافه‌کار، شب‌کاری و تعطیل‌کاری به ساعت و بقیه ستون‌ها به روز است.</p>
  </PrintForm>
</template>

<style scoped>
.att-table { width: 100%; border-collapse: collapse; font-size: 9pt; }
.att-table th, .att-table td { border: 1px solid #444; padding: 2px 4px; text-align: center; }
.att-table td:nth-child(3) { text-align: right; }
.att-note { font-size: 8pt; margin-top: 4px; }
</style>
