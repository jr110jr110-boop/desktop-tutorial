<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getCashPosition } from "@/api/FnAPIs";
import { fail, jalali } from "@/composables/inventory";
import { camelize, money } from "@/composables/fn";

// Cash position today plus the weekly forecast from cheques, approved payment requests and receivables.
const p = ref<any>(null);
onMounted(async () => {
  const [d, error] = await getCashPosition();
  if (error) return fail(error);
  p.value = camelize(d);
});
const kindTitle: Record<string, string> = { Bank: "حساب بانکی", Cash: "صندوق", Petty: "تنخواه", BankAccount: "حساب بانکی", CashBox: "صندوق", PettyCash: "تنخواه" };
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">وضعیت نقدینگی</h1></div>
      <template v-if="p">
        <div v-if="p.alerts?.length" class="mb-3"><div v-for="(a, i) in p.alerts" :key="i" class="alert alert-warning py-1 mb-1">{{ a }}</div></div>
        <div class="fn-tiles mb-3">
          <div class="fn-tile"><div class="t">صندوق‌ها</div><div class="v">{{ money(p.cash) }}</div></div>
          <div class="fn-tile"><div class="t">بانک‌ها</div><div class="v">{{ money(p.banks) }}</div></div>
          <div class="fn-tile"><div class="t">تنخواه‌ها</div><div class="v">{{ money(p.pettyCash) }}</div></div>
          <div class="fn-tile"><div class="t">جمع نقدینگی</div><div class="v">{{ money(p.total) }}</div></div>
          <div class="fn-tile"><div class="t">چک دریافتی در صندوق</div><div class="v">{{ money(p.receivedChequesInBox) }}</div></div>
          <div class="fn-tile"><div class="t">چک دریافتی نزد بانک</div><div class="v">{{ money(p.receivedChequesDeposited) }}</div></div>
          <div class="fn-tile"><div class="t">چک صادره معوق</div><div class="v">{{ money(p.issuedChequesOutstanding) }}</div></div>
          <div class="fn-tile"><div class="t">درخواست پرداخت تاییدشده</div><div class="v">{{ money(p.approvedPaymentRequests) }}</div></div>
        </div>
        <h2 class="inv-title mb-2">موجودی حساب‌ها</h2>
        <div class="table-responsive mb-3">
          <table class="table table-sm"><thead><tr><th>نوع</th><th>عنوان</th><th>مانده (ریال)</th></tr></thead>
            <tbody><tr v-for="a in p.accounts" :key="a.kind + a.id"><td>{{ kindTitle[a.kind] || a.kind }}</td><td>{{ a.title }}</td><td class="fn-num">{{ money(a.balance) }}</td></tr></tbody></table>
        </div>
        <h2 class="inv-title mb-2">پیش‌بینی هفتگی</h2>
        <div class="table-responsive">
          <table class="table table-sm"><thead><tr><th>هفته</th><th>از</th><th>تا</th><th>ورودی</th><th>خروجی</th><th>خالص</th><th>تجمعی</th></tr></thead>
            <tbody><tr v-for="w in p.forecast" :key="w.week"><td>{{ w.week }}</td><td>{{ jalali(w.from) }}</td><td>{{ jalali(w.to) }}</td><td class="fn-num">{{ money(w.inflows) }}</td><td class="fn-num">{{ money(w.outflows) }}</td><td class="fn-num">{{ money(w.net) }}</td><td class="fn-num"><span :class="w.cumulative < 0 ? 'bad' : 'ok'" class="inv-badge">{{ money(w.cumulative) }}</span></td></tr></tbody></table>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
