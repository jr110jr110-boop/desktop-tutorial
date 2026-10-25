<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { getCeoDashboard } from "@/api/FinanceAPIs";
import { fail, jalali, num, rial } from "@/composables/inventory";
import { labourText } from "@/composables/service";

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
  { title: "مصوبات جلسه از موعد گذشته", value: num(d.overdueDecisions), link: "/office/decisions", warn: d.overdueDecisions > 0 },
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
  // production (MES)
  { title: "سفارش تولید با تاخیر", value: num(d.productionLateOrders), link: "/production/dashboard", warn: d.productionLateOrders > 0 },
  { title: "ماشین خراب", value: num(d.productionMachinesDown), link: "/production/dashboard", warn: d.productionMachinesDown > 0 },
  { title: "هفته‌های پرباری (۴ هفته آینده، بیش از ۱۱۰٪)", value: num(d.productionOverloadWeeks), link: "/production/schedule", warn: d.productionOverloadWeeks > 0 },
  { title: "نگهداری و تعمیرات عقب‌افتاده", value: num(d.productionMaintenanceOverdue), link: "/production/maintenance", warn: d.productionMaintenanceOverdue > 0 },
];

/** «—» for an empty value (a nullable field before any result exists). */
const dash = (v: number | null | undefined, text: (x: number) => string) => (v === null || v === undefined ? "—" : text(Number(v)));

// sales: pipeline and the gates waiting for the CEO / credit (the API sends the sales* fields)
const salesTiles = (d: any) => [
  { title: "استعلام باز", value: num(d.salesOpenInquiries), link: "/sales/inquiries" },
  { title: "ارزش استعلام‌های باز (ریال)", value: rial(d.salesPipelineValue), link: "/sales/inquiries" },
  { title: "استعلام منتظر تصمیم شرکت یا عدم شرکت", value: num(d.salesBidDecisionsWaiting), link: "/sales/bid-decisions", warn: d.salesBidDecisionsWaiting > 0 },
  { title: "پیشنهاد منتظر تایید", value: num(d.salesProposalsWaitingApproval), link: "/sales/proposals", warn: d.salesProposalsWaitingApproval > 0 },
  { title: "پیشنهاد نزدیک به مهلت ارسال (۳ روز)", value: num(d.salesProposalsDueSoon), link: "/sales/proposals", warn: d.salesProposalsDueSoon > 0 },
  { title: "پیشنهاد کمتر از بهای تمام‌شده", value: num(d.salesBelowCostProposals), link: "/sales/proposals", warn: d.salesBelowCostProposals > 0 },
  { title: "سفارش فروش منتظر اعتبار", value: num(d.salesOrdersWaitingCredit), link: "/sales/orders", warn: d.salesOrdersWaitingCredit > 0 },
  { title: "نرخ موفقیت پیشنهادها", value: dash(d.salesHitRatePercent, (x) => num(x) + "٪"), link: "/sales/dashboard" },
];

// after-sales service: requests, SLA, warranty, goodwill and satisfaction (the service* fields; the warranty cost is payroll-protected)
const serviceTiles = (d: any) => [
  { title: "درخواست خدمات باز", value: num(d.serviceOpenRequests), link: "/service/requests" },
  { title: "درخواست خارج از SLA", value: num(d.serviceSlaBreaches), link: "/service/requests", warn: d.serviceSlaBreaches > 0 },
  { title: "واحد در گارانتی", value: num(d.serviceUnitsInWarranty), link: "/service/units" },
  { title: "هزینه گارانتی از ابتدای سال (ریال)", value: d.serviceWarrantyCostHidden ? labourText(null, true) : dash(d.serviceWarrantyCostYtd, rial), link: "/service/orders" },
  { title: "حسن نیت منتظر تصمیم مدیرعامل", value: num(d.serviceGoodwillWaiting), link: "/service/orders", warn: d.serviceGoodwillWaiting > 0 },
  { title: "میانگین رضایت (از ۵)", value: dash(d.serviceSurveyAverageScore, (x) => x.toFixed(2)), link: "/service/surveys" },
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

      <h2 class="inv-title mb-2">فروش</h2>
      <div class="ceo-tiles mb-3">
        <button v-for="t in salesTiles(data)" :key="t.title" class="ceo-tile" :class="{ warn: t.warn }" @click="open(t.link)">
          <span class="ceo-value">{{ t.value }}</span>
          <span class="ceo-title">{{ t.title }}</span>
        </button>
      </div>

      <h2 class="inv-title mb-2">خدمات پس از فروش</h2>
      <div class="ceo-tiles mb-3">
        <button v-for="t in serviceTiles(data)" :key="t.title" class="ceo-tile" :class="{ warn: t.warn }" @click="open(t.link)">
          <span class="ceo-value">{{ t.value }}</span>
          <span class="ceo-title">{{ t.title }}</span>
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
