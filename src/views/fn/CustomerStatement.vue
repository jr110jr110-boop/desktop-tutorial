<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import { getCustomerStatement } from "@/api/FnAPIs";
import { camelize, money } from "@/composables/fn";
import { fail, jalali } from "@/composables/inventory";

// Account statement of one customer: invoices (debit), receipts and credit notes (credit) with the running balance.
const route = useRoute();
const data = ref<any>(null);
onMounted(async () => {
  const [d, error] = await getCustomerStatement(Number(route.params.id));
  if (error) return fail(error);
  data.value = camelize(d);
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">صورت‌حساب مشتری {{ data?.customerName }}</h1>
        <div class="d-flex gap-2"><router-link :to="{ name: 'FnCustomers' }" class="btn btn-sm btn-outline-secondary">مشتریان</router-link></div>
      </div>
      <div v-if="data" class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>نوع سند</th><th>شماره</th><th>شرح</th><th>بدهکار (ریال)</th><th>بستانکار (ریال)</th><th>مانده (ریال)</th></tr></thead>
          <tbody>
            <tr v-if="!data.rows?.length"><td colspan="7" class="text-center inv-muted">گردشی نیست.</td></tr>
            <tr v-for="(r, i) in data.rows" :key="i"><td>{{ jalali(r.date) }}</td><td>{{ r.kind }}</td><td class="inv-ltr">{{ r.number }}</td><td>{{ r.description }}</td><td class="fn-num">{{ r.debit ? money(r.debit) : "" }}</td><td class="fn-num">{{ r.credit ? money(r.credit) : "" }}</td><td class="fn-num">{{ money(r.balance) }}</td></tr>
          </tbody>
          <tfoot><tr class="fn-total"><td colspan="6">مانده نهایی</td><td class="fn-num">{{ money(data.balance) }}</td></tr></tfoot>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
