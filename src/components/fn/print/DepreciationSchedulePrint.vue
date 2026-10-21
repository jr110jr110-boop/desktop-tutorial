<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { distributionFinance, money, monthNames } from "@/composables/fn";

// Depreciation schedule of the year by asset class and by asset (approved monthly runs only).
defineProps<{ data: any }>();
</script>

<template>
  <PrintForm
    class="q-form"
    title="جدول استهلاک دارایی‌های ثابت"
    :fields="[{ label: 'سال مالی', value: String(data.year) }, { label: 'جمع استهلاک سال (ریال)', value: money(data.total) }]"
    :signatures="[{ title: 'تهیه‌کننده (حسابدار)' }, { title: 'مدیر مالی' }]"
    :distribution="distributionFinance">
    <h3 class="h6">به تفکیک رده</h3>
    <table>
      <thead><tr><th>رده</th><th v-for="m in monthNames" :key="m">{{ m }}</th><th>جمع</th></tr></thead>
      <tbody>
        <tr v-for="c in data.classes" :key="c.classCode"><td>{{ c.classCode }}</td><td v-for="(v, i) in c.months" :key="i" class="num inv-ltr">{{ money(v) }}</td><td class="num inv-ltr">{{ money(c.total) }}</td></tr>
      </tbody>
    </table>
    <h3 class="h6 mt-3">به تفکیک دارایی</h3>
    <table>
      <thead><tr><th>کد</th><th>دارایی</th><th v-for="m in monthNames" :key="m">{{ m }}</th><th>جمع</th></tr></thead>
      <tbody>
        <tr v-for="a in data.assets" :key="a.assetId"><td class="inv-ltr">{{ a.assetCode }}</td><td>{{ a.assetTitle }}</td><td v-for="(v, i) in a.months" :key="i" class="num inv-ltr">{{ money(v) }}</td><td class="num inv-ltr">{{ money(a.total) }}</td></tr>
      </tbody>
    </table>
  </PrintForm>
</template>
