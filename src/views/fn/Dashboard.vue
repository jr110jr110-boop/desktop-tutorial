<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { getFinanceDashboard } from "@/api/FnAPIs";
import { fail } from "@/composables/inventory";
import { money } from "@/composables/fn";

// Finance dashboard: cash position, cheques due, vouchers waiting, receivables and payables, VAT estimate, profit of the year.
const d = ref<any>(null);
const loading = ref(true);

async function load() {
  loading.value = true;
  const [data, error] = await getFinanceDashboard();
  loading.value = false;
  if (error) return fail(error);
  d.value = data;
}

const tiles = computed(() => {
  const x = d.value;
  if (!x) return [];
  return [
    { t: "صندوق‌ها", v: money(x.cashTotal), to: "FnTreasuryAccounts" },
    { t: "حساب‌های بانکی", v: money(x.bankTotal), to: "FnTreasuryAccounts" },
    { t: "تنخواه‌ها", v: money(x.pettyCashTotal), to: "FnTreasuryAccounts" },
    { t: `چک‌های دریافتی سررسید ۳ روز آینده (${x.receivedChequesDue3Days})`, v: money(x.receivedChequesDue3DaysAmount), to: "FnCheques", kind: x.receivedChequesDue3Days ? "warn" : "" },
    { t: `چک‌های صادرشده سررسید ۷ روز آینده (${x.issuedChequesDue7Days})`, v: money(x.issuedChequesDue7DaysAmount), to: "FnCheques", kind: x.issuedChequesDue7Days ? "warn" : "" },
    { t: "اسناد منتظر کنترل", v: x.vouchersWaitingCheck, to: "FnVouchers", kind: x.vouchersWaitingCheck ? "warn" : "ok" },
    { t: "اسناد منتظر قطعی‌شدن", v: x.vouchersWaitingFinalize, to: "FnVouchers", kind: x.vouchersWaitingFinalize ? "warn" : "ok" },
    { t: "رویدادهای ثبت‌نشده", v: x.unpostedEvents, to: "FnVouchers", kind: x.unpostedEvents ? "bad" : "ok" },
    { t: "دوره‌های مالی باز", v: x.openPeriods, to: "FnYears" },
    { t: "مغایرت‌گیری‌های تاییدنشده", v: x.unapprovedReconciliations, to: "FnReconciliations", kind: x.unapprovedReconciliations ? "warn" : "ok" },
    { t: "جمع اختلاف مغایرت‌گیری بانکی", v: money(x.reconciliationDifference), to: "FnReconciliations", kind: x.reconciliationDifference ? "bad" : "ok" },
    { t: "مطالبات باز", v: money(x.receivablesOpen), to: "FnAgeing" },
    { t: "مطالبات سررسیدگذشته", v: money(x.receivablesOverdue), to: "FnAgeing", kind: x.receivablesOverdue ? "bad" : "ok" },
    { t: "بدهی‌های باز به تامین‌کنندگان", v: money(x.payablesOpen), to: "FnPayments" },
    { t: "بدهی سررسید ۳۰ روز آینده", v: money(x.payablesDue30), to: "FnPayments", kind: x.payablesDue30 ? "warn" : "" },
    { t: "برآورد ارزش افزوده پرداختنی", v: money(x.vatPayableEstimate), to: "FnTax" },
    { t: "سود خالص از ابتدای سال", v: money(x.netProfitYtd), to: "FnReportIncome" },
    { t: "کمبودهای تنظیمات حسابداری", v: x.setupProblems, to: "FnSetupCheck", kind: x.setupProblems ? "bad" : "ok" },
  ];
});

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">داشبورد مالی</h1>
        <button :disabled="loading" class="btn btn-sm btn-outline-secondary" @click="load">تازه‌سازی</button>
      </div>
      <p v-if="!d && !loading" class="inv-muted">اطلاعاتی دریافت نشد.</p>
      <template v-if="d">
        <div v-if="d.alerts && d.alerts.length" class="mb-3">
          <div v-for="(a, i) in d.alerts" :key="i" class="alert alert-warning py-1 mb-1">{{ a }}</div>
        </div>
        <div class="fn-tiles">
          <router-link v-for="(t, i) in tiles" :key="i" :class="t.kind" :to="{ name: t.to }" class="fn-tile">
            <div class="t">{{ t.t }}</div>
            <div class="v">{{ t.v }}</div>
          </router-link>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
