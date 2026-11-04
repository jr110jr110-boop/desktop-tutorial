<script setup lang="ts">
// Commitments forecast (R14, R18): what the open orders, the selected inquiries and the unpaid statements will cost, week by week or month by month.
import { computed, onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import DashChart from "@/components/dashboards/DashChart.vue";
import { getCommitments } from "@/api/DashboardsAPIs";
import { fail, jalali } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";

const period = ref("week");
const count = ref(8);
const d = ref<any>(null);
const busy = ref(false);
const money = (v: number) => Math.round(v || 0).toLocaleString("fa-IR");
const sources: Record<string, string> = { order: "سفارش خرید", inquiry: "استعلام", statement: "صورت‌وضعیت" };

async function load() {
  busy.value = true;
  const [r, e] = await getCommitments({ contractId: pcContractId.value, period: period.value, count: count.value });
  busy.value = false;
  if (e) return fail(e);
  d.value = r;
}
const chart = computed(() => d.value && {
  key: "forecast", title: "", kind: "bar", masked: d.value.pricesMasked,
  labels: d.value.buckets.map((b: any) => b.label),
  series: [
    { name: "سفارش‌ها", values: d.value.buckets.map((b: any) => b.openOrders) },
    { name: "استعلام‌ها", values: d.value.buckets.map((b: any) => b.inquiries) },
    { name: "صورت‌وضعیت‌ها", values: d.value.buckets.map((b: any) => b.statements) },
  ],
});
function changePeriod() { count.value = period.value === "week" ? 8 : 6; load(); }
onMounted(load);
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="پیش‌بینی تعهدات هزینه" subtitle="سفارش‌های باز، استعلام‌ها و صورت‌وضعیت‌ها" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div><h1 class="inv-title screen-only">پیش‌بینی تعهدات هزینه</h1><span class="inv-muted d-print-none">مبلغ باقی‌مانده‌ی سفارش‌ها در تاریخ تحویل، ارزش انتخاب‌شده‌ی استعلام‌های بدون سفارش و مبلغ قابل‌پرداخت صورت‌وضعیت‌های پرداخت‌نشده. سررسیدگذشته‌ها در اولین دوره آمده است.</span></div>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <ContractSelect allow-all @change="load" />
          <select v-model="period" class="form-select form-select-sm" style="width: 110px" @change="changePeriod"><option value="week">هفتگی</option><option value="month">ماهانه</option></select>
          <input v-model.number="count" type="number" min="1" max="52" class="form-control form-control-sm" style="width: 80px" @change="load" />
          <button class="btn btn-sm btn-outline-secondary" :disabled="busy" @click="load">به‌روزرسانی</button>
        </div>
      </div>
      <template v-if="d">
        <p v-if="d.pricesMasked" class="inv-muted">مبالغ برای نقش شما نمایش داده نمی‌شود.</p>
        <template v-else>
          <div class="d-flex flex-wrap gap-3 mb-3">
            <span>کل: <b>{{ money(d.total) }}</b></span><span>سفارش‌ها: <b>{{ money(d.openOrdersTotal) }}</b></span><span>استعلام‌ها: <b>{{ money(d.inquiriesTotal) }}</b></span>
            <span>صورت‌وضعیت‌ها: <b>{{ money(d.statementsTotal) }}</b></span><span :class="d.overdue > 0 ? 'text-danger' : ''">سررسیدگذشته (جدا از بازه‌ها): <b>{{ money(d.overdue) }}</b></span><span>کل با سررسیدگذشته: <b>{{ money(d.totalWithOverdue) }}</b></span><span v-if="d.excludedForeignOrders" class="text-warning">سفارش ارزی بدون نرخ (خارج از جمع): <b>{{ d.excludedForeignOrders }}</b></span>
          </div>
          <DashChart v-if="chart" :chart="chart" :key="period + count + d.total" />
          <div class="table-responsive mt-3">
            <table class="table table-sm">
              <thead><tr><th>از</th><th>تا</th><th>سفارش‌ها</th><th>استعلام‌ها</th><th>صورت‌وضعیت‌ها</th><th>جمع</th></tr></thead>
              <tbody><tr v-for="b in d.buckets" :key="b.from"><td>{{ jalali(b.from) }}</td><td>{{ jalali(b.to) }}</td><td>{{ money(b.openOrders) }}</td><td>{{ money(b.inquiries) }}</td><td>{{ money(b.statements) }}</td><td><b>{{ money(b.total) }}</b></td></tr></tbody>
            </table>
          </div>
          <h2 class="h6 mt-3">اقلام</h2>
          <div class="table-responsive">
            <table class="table table-sm">
              <thead><tr><th>منبع</th><th>شماره</th><th>تاریخ</th><th>مبلغ</th></tr></thead>
              <tbody>
                <tr v-if="!d.items.length"><td colspan="4" class="text-center inv-muted">تعهدی نیست.</td></tr>
                <tr v-for="(i, k) in d.items" :key="k"><td>{{ sources[i.source] }}</td><td><router-link :to="i.link" class="inv-ltr">{{ i.number }}</router-link></td><td>{{ jalali(i.date) }}</td><td>{{ money(i.amount) }}</td></tr>
              </tbody>
            </table>
          </div>
        </template>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
