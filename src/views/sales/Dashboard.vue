<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import { getSlDashboard } from "@/api/SalesAPIs";
import { fail } from "@/composables/inventory";
import { inquiryStatuses, percentText, persianOf, rialText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";

// Sales dashboard (SlDashboard): pipeline value by stage and product, hit rate, proposals due this week, quotations expiring and the gates waiting for someone.
const router = useRouter();
const asOf = ref<string | null>(null);
const d = ref<any>(null);
const loading = ref(false);
const canMoney = hasRole(SalesRoles.commercialReaders);
const arr = (v: any): any[] => (Array.isArray(v) ? v : []);
const maxOf = (rows: any[]) => Math.max(1, ...rows.map((r) => Number(r.value) || 0));

async function load() {
  loading.value = true;
  const [data, error] = await getSlDashboard(asOf.value);
  loading.value = false;
  if (error) return fail(error);
  d.value = data;
}
const tiles = computed(() => {
  const x = d.value || {};
  return [
    { title: "استعلام‌های باز", value: x.openInquiries, to: "/sales/inquiries" },
    { title: "ارزش پیشنهادهای در جریان (ریال)", value: rialText(x.pipelineValue), to: canMoney ? "/sales/proposals" : null },
    { title: "نرخ موفقیت", value: x.hitRatePercent === null || x.hitRatePercent === undefined ? "-" : percentText(x.hitRatePercent), sub: `برد ${x.won ?? 0} - باخت ${x.lost ?? 0}`, to: canMoney ? "/sales/proposals" : null },
    { title: "پیشنهادهای با مهلت این هفته", value: x.proposalsDueThisWeekCount, warn: x.proposalsDueThisWeekCount > 0, to: "/sales/inquiries" },
    { title: "پیش‌فاکتورهای در آستانه انقضا", value: x.quotationsExpiringCount, warn: x.quotationsExpiringCount > 0, to: "/sales/quotations" },
    { title: "منتظر تصمیم مدیرعامل (مناقصه)", value: x.bidDecisionsWaiting, warn: x.bidDecisionsWaiting > 0, to: "/sales/bid-decisions" },
    { title: "پیشنهاد منتظر تایید", value: x.proposalsWaitingApproval, warn: x.proposalsWaitingApproval > 0, to: canMoney ? "/sales/proposals" : null },
    { title: "سفارش منتظر تایید اعتبار", value: x.ordersWaitingCredit, warn: x.ordersWaitingCredit > 0, to: "/sales/orders" },
    { title: "سفارش‌های باز", value: x.openOrders, to: "/sales/orders" },
  ];
});
const go = (to: string | null) => to && router.push(to);
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">داشبورد فروش</h1>
          <span v-if="d" class="inv-muted">وضعیت تا {{ persianOf(d.asOfText, d.asOf) }}</span>
        </div>
        <div class="d-flex gap-2 align-items-end">
          <div><label class="form-label small mb-0">تاریخ مبنا (خالی = امروز)</label><FnDate v-model="asOf" /></div>
          <button :disabled="loading" class="btn btn-sm btn-outline-secondary" @click="load">بازخوانی</button>
        </div>
      </div>
      <template v-if="d">
        <div class="sl-tiles mb-4">
          <button v-for="t in tiles" :key="t.title" :class="{ warn: t.warn }" class="sl-tile" @click="go(t.to)">
            <span class="sl-value">{{ t.value ?? 0 }}</span><span class="sl-title">{{ t.title }}</span><span v-if="t.sub" class="sl-sub">{{ t.sub }}</span>
          </button>
        </div>
        <div class="row g-3">
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">ارزش پیشنهادها بر حسب مرحله</h2>
            <table class="table table-sm">
              <thead><tr><th>مرحله</th><th>تعداد</th><th>ارزش (ریال)</th><th style="width: 30%"></th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.pipelineByStage).length"><td colspan="4" class="text-center inv-muted">داده‌ای نیست.</td></tr>
                <tr v-for="r in arr(d.pipelineByStage)" :key="r.name"><td>{{ r.name }}</td><td>{{ r.count }}</td><td class="inv-ltr">{{ rialText(r.value) }}</td><td><div class="sl-bar" :style="{ width: ((Number(r.value) || 0) / maxOf(arr(d.pipelineByStage))) * 100 + '%' }" /></td></tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">ارزش پیشنهادها بر حسب محصول</h2>
            <table class="table table-sm">
              <thead><tr><th>محصول</th><th>تعداد</th><th>ارزش (ریال)</th><th style="width: 30%"></th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.pipelineByProduct).length"><td colspan="4" class="text-center inv-muted">داده‌ای نیست.</td></tr>
                <tr v-for="r in arr(d.pipelineByProduct)" :key="r.name"><td>{{ r.name }}</td><td>{{ r.count }}</td><td class="inv-ltr">{{ rialText(r.value) }}</td><td><div class="sl-bar" :style="{ width: ((Number(r.value) || 0) / maxOf(arr(d.pipelineByProduct))) * 100 + '%' }" /></td></tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">پیشنهادهای با مهلت این هفته</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>استعلام</th><th>عنوان</th><th>مشتری</th><th>مهلت</th><th>وضعیت</th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.proposalsDueThisWeek).length"><td colspan="5" class="text-center inv-muted">موردی نیست.</td></tr>
                <tr v-for="r in arr(d.proposalsDueThisWeek)" :key="r.inquiryId" class="sl-link" @click="router.push({ name: 'SlInquiry', params: { id: r.inquiryId } })">
                  <td class="inv-ltr">{{ r.inquiryNumber }}</td><td>{{ r.title }}</td><td>{{ r.customerName }}</td><td>{{ persianOf(r.dueDateText, r.dueDate) }}</td><td>{{ typeof r.status === "number" ? inquiryStatuses[r.status] : r.status }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">پیش‌فاکتورهای در آستانه انقضا</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>شماره</th><th>مشتری</th><th>اعتبار تا</th><th>مبلغ (ریال)</th></tr></thead>
              <tbody>
                <tr v-if="!arr(d.quotationsExpiring).length"><td colspan="4" class="text-center inv-muted">موردی نیست.</td></tr>
                <tr v-for="r in arr(d.quotationsExpiring)" :key="r.quotationId" class="sl-link" @click="router.push('/sales/quotations')">
                  <td class="inv-ltr">{{ r.number }}</td><td>{{ r.customerName }}</td><td>{{ persianOf(r.validUntilText, r.validUntil) }}</td><td class="inv-ltr">{{ rialText(r.netAmount) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>
      <p v-else-if="loading" class="inv-muted">در حال بارگذاری...</p>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
<style scoped>
.sl-tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 0.6rem; }
.sl-tile { display: flex; flex-direction: column; gap: 0.15rem; text-align: right; padding: 0.7rem 0.8rem; border: 1px solid #dfe3e8; border-radius: 8px; background: #fff; cursor: pointer; }
.sl-tile.warn { border-color: #e0a800; background: #fff9e6; }
.sl-value { font-size: 1.4rem; font-weight: 700; word-break: break-all; }
.sl-title { font-size: 0.85rem; color: #555; }
.sl-sub { font-size: 0.75rem; color: #777; }
.sl-link { cursor: pointer; }
.sl-bar { height: 10px; background: #4a7fc1; border-radius: 3px; min-width: 2px; }
</style>
