<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { distributionFinance, money, yesNo } from "@/composables/fn";

// Work-order cost sheet (برگه بهای تمام‌شده دستور ساخت).
defineProps<{ sheet: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="برگه بهای تمام‌شده دستور ساخت"
    :fields="[
      { label: 'دستور ساخت', value: sheet.code },
      { label: 'تعداد', value: sheet.quantity },
      { label: 'بسته‌شده', value: yesNo(sheet.isClosed) },
      { label: 'بهای واحد (ریال)', value: money(sheet.unitCost) },
    ]"
    :signatures="[{ title: 'تهیه‌کننده (حسابداری بهای تمام‌شده)' }, { title: 'مدیر تولید' }, { title: 'مدیر مالی' }]"
    :distribution="distributionFinance">
    <table>
      <thead><tr><th>ردیف</th><th>جزء بها</th><th>مبلغ (ریال)</th></tr></thead>
      <tbody>
        <tr><td>1</td><td>مواد مستقیم</td><td class="num inv-ltr">{{ money(sheet.material) }}</td></tr>
        <tr><td>2</td><td>دستمزد مستقیم</td><td class="num inv-ltr">{{ money(sheet.labour) }}</td></tr>
        <tr><td>3</td><td>سربار جذب‌شده</td><td class="num inv-ltr">{{ money(sheet.overhead) }}</td></tr>
        <tr><td>4</td><td>پیمانکاری</td><td class="num inv-ltr">{{ money(sheet.subcontract) }}</td></tr>
        <tr><td></td><td><b>جمع بهای تمام‌شده</b></td><td class="num inv-ltr"><b>{{ money(sheet.totalCost) }}</b></td></tr>
        <tr><td></td><td>مانده کار در جریان ساخت</td><td class="num inv-ltr">{{ money(sheet.wipBalance) }}</td></tr>
        <tr><td></td><td>دستمزد ثبت‌شده در دفتر</td><td class="num inv-ltr">{{ money(sheet.labourInLedger) }}</td></tr>
        <tr><td></td><td>منتقل‌شده به انبار محصول</td><td class="num inv-ltr">{{ money(sheet.transferredOut) }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
