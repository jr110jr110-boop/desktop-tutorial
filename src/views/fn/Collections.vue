<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { approveDunning, approvePaymentProposal, createPaymentProposal, getBadDebt, getCreditHolds, getCustomers, getDunning, getOpenSupplierAdvances, getPaymentProposal, getPaymentProposals, postBadDebt, runDunning, setCreditHold } from "@/api/FnAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { money, parseAmount, yesNo } from "@/composables/fn";
import { agingBuckets, draftApproved } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Receivable and payable controls: bad-debt provision by ageing bucket, credit holds, dunning drafts, supplier payment proposals and the open supplier advances.
const tab = ref("baddebt");
const canWrite = hasRole(FnRoles.accountants);
const canManage = hasRole(FnRoles.managers);
const canPropose = hasRole(FnRoles.treasuryAndAccountants);
const busy = ref(false);
// the procurement controller does not list the auditor among its readers
const canAdvances = hasRole(["FINANCIAL", "FINANCEMANAGER", "TREASURY", "CEO", "PMO"]);
const tabs = [{ key: "baddebt", title: "ذخیره مطالبات مشکوک‌الوصول" }, { key: "holds", title: "ممنوعیت فروش اعتباری" }, { key: "dunning", title: "یادآوری مطالبات" }, { key: "proposals", title: "پیشنهاد پرداخت" }, { key: "advances", title: "پیش‌پرداخت‌های باز تامین‌کنندگان", hidden: !canAdvances }];

// bad debt (rates are financial inputs: no defaults)
const bd = ref<any>({ asOf: null, current: "", d3160: "", d6190: "", over90: "" });
const bdResult = ref<any>(null);
async function bdRun(post: boolean) {
  const f = bd.value;
  const rates = [f.current, f.d3160, f.d6190, f.over90].map((x) => parseAmount(x));
  if (rates.some((r) => r === null || r === undefined)) return fail("نرخ ذخیره هر چهار گروه سنی را وارد کنید.");
  busy.value = true;
  const body = { asOf: f.asOf || null, currentRate: rates[0], days31To60Rate: rates[1], days61To90Rate: rates[2], over90Rate: rates[3] };
  const [d, error] = post ? await postBadDebt({ ...body, post: true }) : await getBadDebt({ AsOf: body.asOf, CurrentRate: rates[0], Days31To60Rate: rates[1], Days61To90Rate: rates[2], Over90Rate: rates[3] });
  busy.value = false;
  if (error) return fail(error);
  bdResult.value = d;
  if (post) ok("ذخیره مطالبات مشکوک‌الوصول ثبت شد.");
}

// holds
const holds = ref<any[]>([]);
const customer = ref<any>(null);
const holdReason = ref("");
const searchCustomers = async (t: string) => {
  const [d] = await getCustomers({ Contains: t, PageSize: 15 });
  return Array.isArray(d) ? d : d?.data || [];
};
async function loadHolds() {
  const [d, error] = await getCreditHolds();
  if (error) return fail(error);
  holds.value = d || [];
}
async function hold(customerId: number, active: boolean, reason: string) {
  if (active && !reason.trim()) return fail("علت ممنوعیت را بنویسید.");
  busy.value = true;
  const [, error] = await setCreditHold(customerId, active, reason);
  busy.value = false;
  if (error) return fail(error);
  ok(active ? "مشتری در فهرست ممنوعیت قرار گرفت." : "ممنوعیت برداشته شد.");
  customer.value = null;
  holdReason.value = "";
  loadHolds();
}

// dunning
const dunning = ref<any[]>([]);
const dunDate = ref<string | null>(null);
async function loadDunning() {
  const [d, error] = await getDunning();
  if (error) return fail(error);
  dunning.value = d || [];
}
async function dunRun() {
  busy.value = true;
  const [d, error] = await runDunning(dunDate.value);
  busy.value = false;
  if (error) return fail(error);
  ok(`${(d || []).length} یادآوری پیش‌نویس ساخته شد.`);
  loadDunning();
}
async function dunApprove(x: any) {
  const [, error] = await approveDunning(x.id);
  if (error) return fail(error);
  ok("یادآوری تایید شد؛ ارسال توسط ماژول نامه‌ها انجام می‌شود.");
  loadDunning();
}

// payment proposals
const proposals = ref<any[]>([]);
const proposal = ref<any>(null);
const pp = ref<any>({ asOf: null, cashLimit: "" });
async function loadProposals() {
  const [d, error] = await getPaymentProposals();
  if (error) return fail(error);
  proposals.value = d || [];
}
async function openProposal(id: number) {
  const [d, error] = await getPaymentProposal(id);
  if (error) return fail(error);
  proposal.value = d;
}
async function makeProposal() {
  busy.value = true;
  const [d, error] = await createPaymentProposal(pp.value.asOf || null, parseAmount(pp.value.cashLimit));
  busy.value = false;
  if (error) return fail(error);
  ok("پیشنهاد پرداخت ساخته شد.");
  proposal.value = d;
  loadProposals();
}
async function approveProposal() {
  busy.value = true;
  const [d, error] = await approvePaymentProposal(proposal.value.id);
  busy.value = false;
  if (error) return fail(error);
  ok("پیشنهاد پرداخت تایید شد.");
  proposal.value = d;
  loadProposals();
}

// open supplier advances
const advances = ref<any[]>([]);
async function loadAdvances() {
  if (!canAdvances) return;
  const [d, error] = await getOpenSupplierAdvances();
  if (error) return fail(error);
  advances.value = d || [];
}

onMounted(() => {
  loadHolds();
  loadDunning();
  loadProposals();
  loadAdvances();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">کنترل مطالبات و پرداخت‌ها</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'baddebt'">
        <p class="inv-muted">نرخ ذخیره هر گروه سنی ورودی مالی است و پیش‌فرض ندارد؛ پس از تعیین نرخ‌ها توسط مدیر مالی وارد کنید.</p>
        <div class="row g-2 mb-2">
          <div class="col-md-2"><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="bd.asOf" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نرخ جاری (درصد)</label><input v-model="bd.current" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نرخ ۳۱ تا ۶۰ روز</label><input v-model="bd.d3160" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نرخ ۶۱ تا ۹۰ روز</label><input v-model="bd.d6190" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نرخ بیش از ۹۰ روز</label><input v-model="bd.over90" class="form-control form-control-sm" /></div>
          <div class="col-md-2 d-flex align-items-end gap-1"><button :disabled="busy" class="btn btn-sm btn-secondary" @click="bdRun(false)">محاسبه</button><button v-if="canWrite" :disabled="busy || !bdResult" class="btn btn-sm btn-primary" @click="bdRun(true)">ثبت سند</button></div>
        </div>
        <template v-if="bdResult">
          <table class="table table-sm">
            <thead><tr><th>گروه سنی</th><th>مطالبات</th><th>نرخ (درصد)</th><th>ذخیره لازم</th></tr></thead>
            <tbody><tr v-for="b in bdResult.buckets" :key="b.bucket"><td>{{ agingBuckets[b.bucket] || b.bucket }}</td><td class="fn-num">{{ money(b.receivable) }}</td><td class="fn-num">{{ b.ratePercent }}</td><td class="fn-num">{{ money(b.required) }}</td></tr></tbody>
          </table>
          <p>
            ذخیره لازم: <b>{{ money(bdResult.required) }}</b> - مانده قبلی: <b>{{ money(bdResult.previousBalance) }}</b> - تغییر: <b>{{ money(bdResult.change) }}</b> ریال
            <span v-if="bdResult.number"> - شماره {{ bdResult.number }}</span><span v-if="bdResult.voucherId"> - سند {{ bdResult.voucherId }}</span>
          </p>
        </template>
      </template>

      <template v-else-if="tab === 'holds'">
        <div v-if="canWrite" class="row g-2 mb-3">
          <div class="col-md-4"><label class="form-label small mb-0">مشتری</label><LookupBox v-model="customer" :fetch="searchCustomers" :label="(c: any) => c.name" placeholder="نام مشتری" /></div>
          <div class="col-md-5"><label class="form-label small mb-0">علت</label><input v-model="holdReason" class="form-control form-control-sm" /></div>
          <div class="col-md-3 d-flex align-items-end"><button :disabled="busy || !customer" class="btn btn-sm btn-danger" @click="hold(customer.id, true, holdReason)">ممنوعیت فروش اعتباری</button></div>
        </div>
        <table class="table table-sm">
          <thead><tr><th>مشتری</th><th>علت</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!holds.length"><td colspan="3" class="text-center inv-muted">مشتری ممنوع‌شده‌ای نیست.</td></tr>
            <tr v-for="h in holds" :key="h.customerId"><td>{{ h.customerName }}</td><td>{{ h.reason }}</td><td v-if="canWrite"><button :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="hold(h.customerId, false, h.reason || '')">برداشتن ممنوعیت</button></td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'dunning'">
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="dunDate" /></div>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="dunRun">ساخت یادآوری‌های بیش از ۳۰ روز</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>مشتری</th><th>تاریخ</th><th>گروه سنی</th><th>مبلغ (ریال)</th><th>متن</th><th>وضعیت</th><th v-if="canManage"></th></tr></thead>
          <tbody>
            <tr v-if="!dunning.length"><td colspan="8" class="text-center inv-muted">یادآوری‌ای نیست.</td></tr>
            <tr v-for="d in dunning" :key="d.id"><td class="inv-ltr">{{ d.number }}</td><td>{{ d.customerName }}</td><td>{{ jalali(d.asOf) }}</td><td>{{ agingBuckets[d.bucket] || d.bucket }}</td><td class="fn-num">{{ money(d.amount) }}</td><td>{{ d.text }}</td><td>{{ draftApproved[d.status] }}</td><td v-if="canManage"><button v-if="d.status === 0" class="btn btn-sm btn-success" @click="dunApprove(d)">تایید</button></td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'proposals'">
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">تا سررسید</label><FnDate v-model="pp.asOf" /></div>
          <div><label class="form-label small mb-0">سقف نقدینگی (ریال)</label><input v-model="pp.cashLimit" class="form-control form-control-sm fn-num" /></div>
          <button v-if="canPropose" :disabled="busy" class="btn btn-sm btn-primary" @click="makeProposal">ساخت پیشنهاد پرداخت</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تاریخ</th><th>سقف نقدینگی</th><th>جمع پیشنهادی</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!proposals.length"><td colspan="5" class="text-center inv-muted">پیشنهادی نیست.</td></tr>
            <tr v-for="p in proposals" :key="p.id" style="cursor: pointer" @click="openProposal(p.id)"><td class="inv-ltr">{{ p.number }}</td><td>{{ jalali(p.asOf) }}</td><td class="fn-num">{{ money(p.cashLimit) }}</td><td class="fn-num">{{ money(p.total) }}</td><td>{{ draftApproved[p.status] }}</td></tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <table class="table table-sm">
          <thead><tr><th>تامین‌کننده</th><th>سفارش خرید</th><th>پیش‌پرداخت</th><th>بازیابی‌شده</th><th>باز</th></tr></thead>
          <tbody>
            <tr v-if="!advances.length"><td colspan="5" class="text-center inv-muted">پیش‌پرداخت بازی نیست.</td></tr>
            <tr v-for="a in advances" :key="a.purchaseOrderId"><td>{{ a.providerName }}</td><td class="inv-ltr">{{ a.orderNumber }}</td><td class="fn-num">{{ money(a.advanced) }}</td><td class="fn-num">{{ money(a.recovered) }}</td><td class="fn-num">{{ money(a.open) }}</td></tr>
          </tbody>
        </table>
      </template>
    </div>

    <div v-if="proposal" class="inv-modal-backdrop screen-only" @click.self="proposal = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">پیشنهاد پرداخت <span class="inv-ltr">{{ proposal.number }}</span> <span class="inv-badge">{{ draftApproved[proposal.status] }}</span></h2>
        <p>تا {{ jalali(proposal.asOf) }} - سقف نقدینگی {{ money(proposal.cashLimit) }} - جمع پیشنهادی <b>{{ money(proposal.total) }}</b> ریال</p>
        <div class="table-responsive" style="max-height: 360px; overflow: auto">
          <table class="table table-sm">
            <thead><tr><th>تامین‌کننده</th><th>فاکتور</th><th>سررسید</th><th>مبلغ</th><th>شامل پرداخت</th></tr></thead>
            <tbody><tr v-for="l in proposal.lines" :key="l.supplierInvoiceId"><td>{{ l.providerName }}</td><td class="inv-ltr">{{ l.invoiceNumber }}</td><td>{{ jalali(l.dueDate) }}</td><td class="fn-num">{{ money(l.amount) }}</td><td>{{ yesNo(l.included) }}</td></tr></tbody>
          </table>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-2"><button class="btn btn-sm btn-outline-secondary" @click="proposal = null">بستن</button><button v-if="canManage && proposal.status === 0" :disabled="busy" class="btn btn-sm btn-success" @click="approveProposal">تایید (مدیر مالی)</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
