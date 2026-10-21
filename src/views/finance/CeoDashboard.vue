<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { getCeoDashboard } from "@/api/FinanceAPIs";
import { fail, jalali, num, rial } from "@/composables/inventory";

// One screen for the CEO: money, stock, procurement, quality and the alerts of the whole company (rial).
const router = useRouter();
const data = ref<any>(null);

async function load() {
  const [result, error] = await getCeoDashboard();
  if (error) return fail(error);
  data.value = result;
}

// the API writes some links in the plural; the pages of this front end use these paths
const linkFix: Record<string, string> = { "/inventory/ncrs": "/inventory/ncr" };
function open(link: string) {
  if (link) router.push(linkFix[link] || link);
}

const tiles = (d: any) => [
  { title: "ارزش موجودی انبار", value: rial(d.stockValue), link: "/finance/valuation" },
  { title: "کالاهای زیر حداقل موجودی", value: num(d.itemsBelowMinimum), link: "/inventory/stock", warn: d.itemsBelowMinimum > 0 },
  { title: "ردیف‌های در قرنطینه", value: num(d.quarantineLines), link: "/inventory/inspections" },
  { title: "درخواست خرید باز", value: num(d.openPurchaseRequests), sub: d.purchaseRequestsWaitingApproval + " در انتظار تایید", link: "/procurement/requests" },
  { title: "سفارش خرید باز", value: num(d.openPurchaseOrders), sub: rial(d.openPurchaseOrdersAmount) + " ریال", link: "/procurement/orders" },
  { title: "ردیف سفارش با تاخیر", value: num(d.lateOrderLines), link: "/procurement/orders", warn: d.lateOrderLines > 0 },
  { title: "دریافت‌شده فاکتورنشده", value: rial(d.receivedNotInvoiced), link: "/procurement/invoices" },
  { title: "فاکتور در انتظار تایید", value: num(d.invoicesWaitingApproval), link: "/procurement/invoices" },
  { title: "بدهی باز به تامین‌کنندگان", value: rial(d.payablesOpen), link: "/procurement/payments" },
  { title: "پرداخت در انتظار", value: num(d.paymentsWaitingApproval), sub: rial(d.paymentsWaitingAmount) + " ریال", link: "/procurement/payments" },
  { title: "پرداخت‌شده این ماه", value: rial(d.paidThisMonth), link: "/procurement/payments" },
  { title: "تامین‌کننده مسدود", value: num(d.blockedSuppliers), link: "/procurement/suppliers", warn: d.blockedSuppliers > 0 },
  { title: "ارزیابی سررسیدشده", value: num(d.supplierEvaluationsDue), link: "/procurement/suppliers" },
  { title: "بازرسی ورودی در انتظار", value: num(d.pendingInspections), link: "/inventory/inspections" },
  { title: "NCR باز", value: num(d.openNcrs), link: "/inventory/ncr", warn: d.openNcrs > 0 },
  { title: "درخواست کالای باز", value: num(d.openMaterialRequests), link: "/inventory/material-requests" },
  { title: "سند انبار در انتظار تایید", value: num(d.documentsWaitingApproval), link: "/inventory/documents" },
  { title: "رویداد حسابداری در صف", value: num(d.eventsPending), sub: d.lastEventSent ? "آخرین ارسال " + jalali(d.lastEventSent, true) : "", link: "/finance/events" },
  { title: "رویداد حسابداری ناموفق", value: num(d.eventsFailed), link: "/finance/events", warn: d.eventsFailed > 0 },
  { title: "CAPA عقب‌افتاده", value: num(d.overdueCapas), link: "/quality/capa", warn: d.overdueCapas > 0 },
  { title: "کالیبراسیون عقب‌افتاده", value: num(d.calibrationOverdue), link: "/quality/calibration", warn: d.calibrationOverdue > 0 },
  { title: "کالیبراسیون سررسید ۳۰ روز آینده", value: num(d.calibrationDue30), link: "/quality/calibration" },
  { title: "نقاط توقف (H/W) در انتظار", value: num(d.pendingHPoints), link: "/quality/itp", warn: d.pendingHPoints > 0 },
  { title: "گواهی‌های در آستانه انقضا", value: num(d.qualificationsLapsing), link: "/quality/personnel", warn: d.qualificationsLapsing > 0 },
  { title: "شکایت مشتری عقب‌افتاده", value: num(d.complaintsOverdue), link: "/quality/complaints", warn: d.complaintsOverdue > 0 },
  { title: "اعلام بازرسی شاهد سررسیدشده", value: num(d.witnessNoticesDue), link: "/quality/itp" },
  // project control (PMO)
  { title: "پروژه‌های فعال", value: num(d.pcActiveProjects), link: "/project-control/portfolio" },
  { title: "پروژه قرمز (SPI یا CPI)", value: num(d.pcProjectsRed), link: "/project-control/portfolio", warn: d.pcProjectsRed > 0 },
  { title: "پروژه زرد (SPI یا CPI)", value: num(d.pcProjectsAmber), link: "/project-control/portfolio", warn: d.pcProjectsAmber > 0 },
  { title: "فعالیت پروژه با تاخیر", value: num(d.pcLateActivities), link: "/project-control/activities", warn: d.pcLateActivities > 0 },
  { title: "درخواست تغییر باز", value: num(d.pcOpenChangeRequests), sub: d.pcOverdueChangeRequests + " از مهلت تحلیل گذشته", link: "/project-control/changes", warn: d.pcOverdueChangeRequests > 0 },
  { title: "ضمانت‌نامه در آستانه انقضا", value: num(d.pcGuaranteeAlerts), link: "/project-control/guarantees", warn: d.pcGuaranteeAlerts > 0 },
  { title: "مرحله پرداخت سررسیدشده", value: num(d.pcOverdueMilestones), link: "/project-control/milestones", warn: d.pcOverdueMilestones > 0 },
  { title: "گام شروع پروژه از مهلت گذشته", value: num(d.pcOverdueInitiationSteps), link: "/project-control/portfolio", warn: d.pcOverdueInitiationSteps > 0 },
  { title: "پاسخ کارفرما به مدارک عقب‌افتاده", value: num(d.pcOverdueClientResponses), link: "/project-control/transmittals", warn: d.pcOverdueClientResponses > 0 },
  { title: "جریمه تاخیر محاسبه‌شده (ریال)", value: rial(d.pcPenaltyExposure), link: "/project-control/penalties", warn: d.pcPenaltyExposure > 0 },
];

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div v-if="data" class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">داشبورد مدیرعامل</h1>
          <span class="inv-muted">به‌روز شده: {{ jalali(data.generatedAt, true) }} — مبالغ به ریال</span>
        </div>
        <button class="btn btn-sm btn-outline-secondary" @click="load">بازخوانی</button>
      </div>

      <div v-if="data.alerts.length" class="mb-3">
        <h2 class="inv-title mb-2">هشدارها</h2>
        <div
          v-for="(a, i) in data.alerts"
          :key="i"
          class="alert py-2 mb-1 d-flex justify-content-between"
          :class="{ 'alert-danger': a.level === 'danger', 'alert-warning': a.level === 'warning', 'alert-info': a.level === 'info' }">
          <span>{{ a.text }}</span>
          <a v-if="a.link" href="#" @click.prevent="open(a.link)">مشاهده</a>
        </div>
      </div>

      <div class="ceo-tiles mb-3">
        <button v-for="t in tiles(data)" :key="t.title" class="ceo-tile" :class="{ warn: t.warn }" @click="open(t.link)">
          <span class="ceo-value">{{ t.value }}</span>
          <span class="ceo-title">{{ t.title }}</span>
          <span v-if="t.sub" class="ceo-sub">{{ t.sub }}</span>
        </button>
      </div>

      <h2 class="inv-title mb-2">پروژه‌ها: قیمت قرارداد و هزینه</h2>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th>پروژه</th>
              <th>مبلغ قرارداد</th>
              <th>مواد</th>
              <th>پیمانکاری</th>
              <th>سایر</th>
              <th>هزینه واقعی</th>
              <th>تعهد باز</th>
              <th>هزینه به قیمت</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="!data.projects.length"><td colspan="8" class="text-center inv-muted">پروژه‌ای نیست.</td></tr>
            <tr v-for="p in data.projects" :key="p.contractId">
              <td><span class="inv-ltr">{{ p.code }}</span> {{ p.title }}</td>
              <td class="num">{{ rial(p.contractPrice) }}</td>
              <td class="num">{{ rial(p.materialCost) }}</td>
              <td class="num">{{ rial(p.subcontractCost) }}</td>
              <td class="num">{{ rial(p.otherCost) }}</td>
              <td class="num fw-bold">{{ rial(p.actualCost) }}</td>
              <td class="num">{{ rial(p.committed) }}</td>
              <td>
                <span v-if="p.costToPricePercent !== null" class="inv-badge" :class="p.costToPricePercent >= 100 ? 'bad' : p.costToPricePercent >= 90 ? 'warn' : 'ok'">
                  {{ num(p.costToPricePercent) }}٪
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style scoped>
.ceo-tiles {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(190px, 1fr));
  gap: 0.6rem;
}
.ceo-tile {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  text-align: right;
  padding: 0.7rem 0.8rem;
  border: 1px solid #dfe3e8;
  border-radius: 8px;
  background: #fff;
}
.ceo-tile.warn {
  border-color: #e0a800;
  background: #fff9e6;
}
.ceo-value {
  font-size: 1.25rem;
  font-weight: 700;
}
.ceo-title {
  font-size: 0.8rem;
  color: #555;
}
.ceo-sub {
  font-size: 0.72rem;
  color: #888;
}
</style>
