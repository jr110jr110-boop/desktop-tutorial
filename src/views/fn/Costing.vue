<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ContractPick from "@/components/fn/ContractPick.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import CostSheetPrint from "@/components/fn/print/CostSheetPrint.vue";
import { closeWorkOrder, getAllocationKeys, getContractRevenue, getOverheadRates, getStandardCosts, getWorkOrderBases, getWorkOrderCostSheet, reopenWorkOrder, runAllocation, runRevenueRecognition, saveAllocationKey, saveContractRevenue, saveOverheadRate, saveStandardCost, saveWorkOrderBase } from "@/api/FnAPIs";
import { fail, jalali, num, ok, today } from "@/composables/inventory";
import { money, parseAmount, printSheet, yesNo } from "@/composables/fn";
import { overheadBases, revenueMethods, usePeriods } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Costing: overhead rates and allocation keys, work-order bases, the period allocation / absorption run (dry run first), standard costs, work-order close-out with the cost sheet, and percentage-of-completion revenue.
const tab = ref("overhead");
const tabs = [{ key: "overhead", title: "نرخ سربار" }, { key: "keys", title: "کلیدهای تسهیم" }, { key: "run", title: "تسهیم و جذب سربار" }, { key: "wo", title: "بستن دستور ساخت" }, { key: "std", title: "بهای استاندارد" }, { key: "poc", title: "درآمد درصد پیشرفت" }];
const { periods, load: loadPeriods, label } = usePeriods();
const canWrite = hasRole(FnRoles.accountants);
const canManage = hasRole(FnRoles.managers);
const busy = ref(false);
const printing = ref(false);

// overhead rates
const rates = ref<any[]>([]);
const rForm = ref<any>(null);
async function loadRates() {
  const [d, error] = await getOverheadRates();
  if (error) return fail(error);
  rates.value = d || [];
}
async function saveRate() {
  const f = rForm.value;
  if (!String(f.costCenterCode).trim() || !f.validFrom || !(parseAmount(f.rate) || 0)) return fail("مرکز هزینه، نرخ و تاریخ شروع را وارد کنید.");
  busy.value = true;
  const [, error] = await saveOverheadRate({ id: f.id || null, costCenterCode: String(f.costCenterCode).trim(), base: Number(f.base), rate: parseAmount(f.rate), validFrom: f.validFrom, validTo: f.validTo || null, source: f.source || null, verify: f.verify });
  busy.value = false;
  if (error) return fail(error);
  ok("نرخ سربار ذخیره شد.");
  rForm.value = null;
  loadRates();
}

// allocation keys
const keys = ref<any[]>([]);
const kForm = ref<any>(null);
const basisNames: Record<string, string> = { Percent: "درصد", Area: "مساحت", Headcount: "تعداد نفرات", KWh: "کیلووات‌ساعت", Other: "سایر" };
async function loadKeys() {
  const [d, error] = await getAllocationKeys();
  if (error) return fail(error);
  keys.value = d || [];
}
async function saveKey() {
  const f = kForm.value;
  if (!String(f.fromCostCenterCode).trim() || !String(f.toCostCenterCode).trim() || !f.validFrom || !(parseAmount(f.value) || 0)) return fail("مرکز مبدأ، مقصد، مقدار و تاریخ شروع را وارد کنید.");
  busy.value = true;
  const [, error] = await saveAllocationKey({ id: f.id || null, fromCostCenterCode: String(f.fromCostCenterCode).trim(), toCostCenterCode: String(f.toCostCenterCode).trim(), basis: f.basis, value: parseAmount(f.value), validFrom: f.validFrom, validTo: f.validTo || null, sequence: Number(f.sequence) || 0, source: f.source || null });
  busy.value = false;
  if (error) return fail(error);
  ok("کلید تسهیم ذخیره شد.");
  kForm.value = null;
  loadKeys();
}

// bases and allocation run
const periodId = ref<number | null>(null);
const bases = ref<any[]>([]);
const bForm = ref<any>(null);
const result = ref<any>(null);
async function loadBases() {
  if (!periodId.value) return;
  const [d, error] = await getWorkOrderBases(periodId.value);
  if (error) return fail(error);
  bases.value = d || [];
}
async function saveBase() {
  const f = bForm.value;
  if (!parseAmount(f.workOrderId) || !String(f.costCenterCode).trim() || !periodId.value) return fail("دوره، دستور ساخت و مرکز هزینه را وارد کنید.");
  busy.value = true;
  const [, error] = await saveWorkOrderBase({ id: f.id || null, workOrderId: parseAmount(f.workOrderId), costCenterCode: String(f.costCenterCode).trim(), periodId: periodId.value, base: Number(f.base), quantity: parseAmount(f.quantity) || 0 });
  busy.value = false;
  if (error) return fail(error);
  ok("مبنای سربار ذخیره شد.");
  bForm.value = null;
  loadBases();
}
async function allocate(dryRun: boolean) {
  if (!periodId.value) return fail("دوره را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await runAllocation(periodId.value, dryRun);
  busy.value = false;
  if (error) return fail(error);
  result.value = d;
  ok(dryRun ? "پیش‌نمایش تسهیم آماده است." : "تسهیم و جذب سربار ثبت شد.");
}

// work order close
const woId = ref("");
const sheet = ref<any>(null);
const dialog = ref<null | "close" | "reopen">(null);
async function loadSheet() {
  const id = parseAmount(woId.value);
  if (!id) return fail("شناسه دستور ساخت را وارد کنید.");
  const [d, error] = await getWorkOrderCostSheet(id);
  if (error) return fail(error);
  sheet.value = d;
}
async function confirmWo(v: any) {
  const kind = dialog.value;
  dialog.value = null;
  busy.value = true;
  const dto = { quantity: parseAmount(v.quantity) || null, date: v.date || today(), reason: v.reason || null };
  const [d, error] = kind === "close" ? await closeWorkOrder(sheet.value.workOrderId, dto) : await reopenWorkOrder(sheet.value.workOrderId, dto);
  busy.value = false;
  if (error) return fail(error);
  closeDoc.value = d;
  ok(kind === "close" ? "دستور ساخت بسته شد و سند صادر شد." : "دستور ساخت دوباره باز شد.");
  loadSheet();
}
const closeDoc = ref<any>(null);
async function printSheetNow() {
  printing.value = true;
  await nextTick();
  printSheet();
}

// standard costs
const std = ref<any[]>([]);
const sForm = ref<any>(null);
async function loadStd() {
  const [d, error] = await getStandardCosts();
  if (error) return fail(error);
  std.value = d || [];
}
async function saveStd() {
  const f = sForm.value;
  if (!parseAmount(f.itemId) || !f.validFrom || parseAmount(f.cost) === null) return fail("کالا، بهای استاندارد و تاریخ شروع را وارد کنید.");
  busy.value = true;
  const [, error] = await saveStandardCost({ itemId: parseAmount(f.itemId), cost: parseAmount(f.cost), validFrom: f.validFrom, source: f.source || null });
  busy.value = false;
  if (error) return fail(error);
  ok("بهای استاندارد ذخیره شد.");
  sForm.value = null;
  loadStd();
}

// percentage of completion
const contracts = ref<any[]>([]);
const cForm = ref<any>(null);
const pocInputs = ref<any[]>([]);
const pocResult = ref<any>(null);
const progressSources: Record<string, string> = { ProjectControl: "کنترل پروژه", Manual: "دستی" };
const progressMeasures: Record<string, string> = { Physical: "پیشرفت فیزیکی", CostToCost: "نسبت هزینه" };
async function loadContracts() {
  const [d, error] = await getContractRevenue();
  if (error) return fail(error);
  contracts.value = d || [];
  pocInputs.value = contracts.value.filter((c) => c.method === 1).map((c) => ({ contractId: c.contractId, progressPercent: "", estimatedCost: "" }));
}
async function saveContract() {
  const f = cForm.value;
  if (!f.contractId) return fail("پروژه را انتخاب کنید.");
  busy.value = true;
  const [, error] = await saveContractRevenue({ contractId: f.contractId, method: Number(f.method), contractPrice: parseAmount(f.contractPrice), progressSource: f.progressSource, progressMeasure: f.progressMeasure });
  busy.value = false;
  if (error) return fail(error);
  ok("روش شناسایی درآمد ذخیره شد.");
  cForm.value = null;
  loadContracts();
}
async function runPoc(dryRun: boolean) {
  if (!periodId.value) return fail("دوره را انتخاب کنید.");
  const inputs = pocInputs.value.filter((i) => i.progressPercent !== "" || i.estimatedCost !== "").map((i) => ({ contractId: i.contractId, progressPercent: parseAmount(i.progressPercent), estimatedCost: parseAmount(i.estimatedCost) }));
  busy.value = true;
  const [d, error] = await runRevenueRecognition(periodId.value, dryRun, inputs);
  busy.value = false;
  if (error) return fail(error);
  pocResult.value = d;
  ok(dryRun ? "پیش‌نمایش شناسایی درآمد آماده است." : "درآمد دوره شناسایی و ثبت شد.");
}

onMounted(() => {
  loadPeriods();
  loadRates();
  loadKeys();
  loadStd();
  loadContracts();
});
</script>

<template>
  <div class="inv-page">
    <CostSheetPrint v-if="printing && sheet" :sheet="sheet" />
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">بهای تمام‌شده و درآمد پروژه</h1>
        <button v-if="canWrite && tab === 'overhead'" class="btn btn-sm btn-primary" @click="rForm = { costCenterCode: '', base: 1, rate: '', validFrom: today(), validTo: null, source: '', verify: true }">نرخ جدید</button>
        <button v-if="canWrite && tab === 'keys'" class="btn btn-sm btn-primary" @click="kForm = { fromCostCenterCode: '', toCostCenterCode: '', basis: 'Percent', value: '', validFrom: today(), validTo: null, sequence: 1, source: '' }">کلید جدید</button>
        <button v-if="canWrite && tab === 'std'" class="btn btn-sm btn-primary" @click="sForm = { itemId: '', cost: '', validFrom: today(), source: '' }">بهای جدید</button>
        <button v-if="canManage && tab === 'poc'" class="btn btn-sm btn-primary" @click="cForm = { contractId: null, method: 1, contractPrice: '', progressSource: 'ProjectControl', progressMeasure: 'Physical' }">تعیین روش پروژه</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'overhead'">
        <table class="table table-sm">
          <thead><tr><th>مرکز هزینه</th><th>مبنا</th><th>نرخ (ریال)</th><th>از تاریخ</th><th>تا تاریخ</th><th>منبع</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!rates.length"><td colspan="7" class="text-center inv-muted">نرخی ثبت نشده است.</td></tr>
            <tr v-for="r in rates" :key="r.id"><td class="inv-ltr">{{ r.costCenterCode }}</td><td>{{ overheadBases[r.base] }}</td><td class="fn-num">{{ money(r.rate) }}</td><td>{{ jalali(r.validFrom) }}</td><td>{{ jalali(r.validTo) }}</td><td>{{ r.source }}</td><td v-if="canWrite"><button class="btn btn-sm btn-outline-secondary" @click="rForm = { ...r, rate: String(r.rate), validTo: r.validTo || null }">ویرایش</button></td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'keys'">
        <table class="table table-sm">
          <thead><tr><th>ترتیب</th><th>از مرکز</th><th>به مرکز</th><th>مبنا</th><th>مقدار</th><th>از تاریخ</th><th>تا تاریخ</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!keys.length"><td colspan="8" class="text-center inv-muted">کلیدی ثبت نشده است.</td></tr>
            <tr v-for="k in keys" :key="k.id"><td>{{ k.sequence }}</td><td class="inv-ltr">{{ k.fromCostCenterCode }}</td><td class="inv-ltr">{{ k.toCostCenterCode }}</td><td>{{ basisNames[k.basis] || k.basis }}</td><td class="fn-num">{{ num(k.value) }}</td><td>{{ jalali(k.validFrom) }}</td><td>{{ jalali(k.validTo) }}</td><td v-if="canWrite"><button class="btn btn-sm btn-outline-secondary" @click="kForm = { ...k, value: String(k.value) }">ویرایش</button></td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'run'">
        <div class="inv-filters mb-3">
          <select v-model="periodId" class="form-select form-select-sm" @change="loadBases"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="allocate(true)">پیش‌نمایش</button>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="allocate(false)">ثبت تسهیم و جذب</button>
          <button v-if="canWrite && periodId" class="btn btn-sm btn-outline-secondary" @click="bForm = { workOrderId: '', costCenterCode: '', base: 1, quantity: '' }">مبنای دستور ساخت</button>
        </div>
        <h2 class="inv-subtitle">مبنای سربار دستورهای ساخت در دوره</h2>
        <table class="table table-sm">
          <thead><tr><th>دستور ساخت</th><th>مرکز هزینه</th><th>مبنا</th><th>مقدار</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!bases.length"><td colspan="5" class="text-center inv-muted">مبنایی برای این دوره ثبت نشده است.</td></tr>
            <tr v-for="b in bases" :key="b.id"><td>{{ b.workOrderId }}</td><td class="inv-ltr">{{ b.costCenterCode }}</td><td>{{ overheadBases[b.base] }}</td><td class="fn-num">{{ num(b.quantity) }}</td><td v-if="canWrite"><button class="btn btn-sm btn-outline-secondary" @click="bForm = { ...b, workOrderId: String(b.workOrderId), quantity: String(b.quantity) }">ویرایش</button></td></tr>
          </tbody>
        </table>
        <template v-if="result">
          <h2 class="inv-subtitle">نتیجه <span v-if="result.dryRun" class="inv-badge warn">پیش‌نمایش</span><span v-else class="inv-badge ok">ثبت‌شده {{ result.number }}</span></h2>
          <p>
            تسهیم‌شده: <b>{{ money(result.totalAllocated) }}</b> - جذب‌شده: <b>{{ money(result.totalAbsorbed) }}</b> - انحراف: <b>{{ money(result.variance) }}</b> ریال
            <span v-if="result.allocationVoucherId"> - سند تسهیم {{ result.allocationVoucherId }}</span><span v-if="result.absorptionVoucherId"> - سند جذب {{ result.absorptionVoucherId }}</span><span v-if="result.varianceVoucherId"> - سند انحراف {{ result.varianceVoucherId }}</span>
          </p>
          <table class="table table-sm">
            <thead><tr><th>مرکز هزینه</th><th>تولیدی</th><th>هزینه خود</th><th>دریافتی</th><th>تسهیم‌شده</th><th>واقعی</th><th>جذب‌شده</th><th>انحراف</th></tr></thead>
            <tbody>
              <tr v-for="c in result.centers" :key="c.costCenterCode"><td class="inv-ltr">{{ c.costCenterCode }}</td><td>{{ yesNo(c.isProduction) }}</td><td class="fn-num">{{ money(c.ownCost) }}</td><td class="fn-num">{{ money(c.received) }}</td><td class="fn-num">{{ money(c.allocated) }}</td><td class="fn-num">{{ money(c.actual) }}</td><td class="fn-num">{{ money(c.absorbed) }}</td><td class="fn-num">{{ money(c.variance) }}</td></tr>
            </tbody>
          </table>
          <table v-if="result.steps.length" class="table table-sm">
            <thead><tr><th>از مرکز</th><th>به مرکز</th><th>مبلغ (ریال)</th></tr></thead>
            <tbody><tr v-for="(s, i) in result.steps" :key="i"><td class="inv-ltr">{{ s.from }}</td><td class="inv-ltr">{{ s.to }}</td><td class="fn-num">{{ money(s.amount) }}</td></tr></tbody>
          </table>
          <table v-if="result.absorptions.length" class="table table-sm">
            <thead><tr><th>دستور ساخت</th><th>مرکز</th><th>مبنا</th><th>مقدار</th><th>نرخ</th><th>مبلغ جذب</th></tr></thead>
            <tbody><tr v-for="(a, i) in result.absorptions" :key="i"><td>{{ a.workOrderId }}</td><td class="inv-ltr">{{ a.costCenterCode }}</td><td>{{ overheadBases[a.base] }}</td><td class="fn-num">{{ num(a.quantity) }}</td><td class="fn-num">{{ money(a.rate) }}</td><td class="fn-num">{{ money(a.amount) }}</td></tr></tbody>
          </table>
        </template>
      </template>

      <template v-else-if="tab === 'wo'">
        <div class="inv-filters mb-3">
          <input v-model="woId" class="form-control form-control-sm" placeholder="شناسه دستور ساخت" style="max-width: 200px" @keyup.enter="loadSheet" />
          <button class="btn btn-sm btn-secondary" @click="loadSheet">نمایش برگه بها</button>
          <button v-if="sheet" class="btn btn-sm btn-outline-secondary" @click="printSheetNow">چاپ</button>
        </div>
        <template v-if="sheet">
          <h2 class="inv-subtitle">دستور ساخت <span class="inv-ltr">{{ sheet.code }}</span> <span :class="sheet.isClosed ? 'ok' : 'warn'" class="inv-badge">{{ sheet.isClosed ? "بسته" : "باز" }}</span></h2>
          <table class="table table-sm" style="max-width: 640px">
            <tbody>
              <tr><td>تعداد</td><td class="fn-num">{{ sheet.quantity }}</td></tr>
              <tr><td>مواد مستقیم</td><td class="fn-num">{{ money(sheet.material) }}</td></tr>
              <tr><td>دستمزد مستقیم</td><td class="fn-num">{{ money(sheet.labour) }}</td></tr>
              <tr><td>سربار جذب‌شده</td><td class="fn-num">{{ money(sheet.overhead) }}</td></tr>
              <tr><td>پیمانکاری</td><td class="fn-num">{{ money(sheet.subcontract) }}</td></tr>
              <tr class="fn-total"><td>جمع بهای تمام‌شده</td><td class="fn-num">{{ money(sheet.totalCost) }}</td></tr>
              <tr><td>بهای واحد</td><td class="fn-num">{{ money(sheet.unitCost) }}</td></tr>
              <tr><td>مانده کار در جریان ساخت</td><td class="fn-num">{{ money(sheet.wipBalance) }}</td></tr>
              <tr><td>دستمزد ثبت‌شده در دفتر</td><td class="fn-num">{{ money(sheet.labourInLedger) }}</td></tr>
              <tr><td>منتقل‌شده به انبار محصول</td><td class="fn-num">{{ money(sheet.transferredOut) }}</td></tr>
            </tbody>
          </table>
          <div class="d-flex gap-2">
            <button v-if="canWrite && !sheet.isClosed" :disabled="busy" class="btn btn-sm btn-primary" @click="dialog = 'close'">بستن دستور ساخت</button>
            <button v-if="canManage && sheet.isClosed" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = 'reopen'">بازگشایی (مدیر مالی)</button>
          </div>
          <p v-if="closeDoc" class="mt-2">سند بستن {{ closeDoc.number }}: به انبار محصول {{ money(closeDoc.toFinishedGoods) }} - انحراف {{ money(closeDoc.variance) }} - بهای واحد {{ money(closeDoc.unitCost) }} ریال - شماره سند حسابداری {{ closeDoc.voucherId }}</p>
        </template>
      </template>

      <template v-else-if="tab === 'std'">
        <table class="table table-sm">
          <thead><tr><th>شناسه کالا</th><th>بهای استاندارد (ریال)</th><th>از تاریخ</th><th>منبع</th></tr></thead>
          <tbody>
            <tr v-if="!std.length"><td colspan="4" class="text-center inv-muted">بهای استانداردی ثبت نشده است.</td></tr>
            <tr v-for="s in std" :key="s.id"><td>{{ s.itemId }}</td><td class="fn-num">{{ money(s.cost) }}</td><td>{{ jalali(s.validFrom) }}</td><td>{{ s.source }}</td></tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <h2 class="inv-subtitle">روش شناسایی درآمد پروژه‌ها</h2>
        <table class="table table-sm">
          <thead><tr><th>پروژه</th><th>روش</th><th>مبلغ قرارداد</th><th>منبع پیشرفت</th><th>روش اندازه‌گیری</th><th v-if="canManage"></th></tr></thead>
          <tbody>
            <tr v-if="!contracts.length"><td colspan="6" class="text-center inv-muted">پروژه‌ای تعریف نشده است.</td></tr>
            <tr v-for="c in contracts" :key="c.contractId"><td>{{ c.contractId }}</td><td>{{ revenueMethods[c.method] }}</td><td class="fn-num">{{ money(c.contractPrice) }}</td><td>{{ progressSources[c.progressSource] }}</td><td>{{ progressMeasures[c.progressMeasure] }}</td><td v-if="canManage"><button class="btn btn-sm btn-outline-secondary" @click="cForm = { ...c, contractPrice: c.contractPrice ? String(c.contractPrice) : '' }">ویرایش</button></td></tr>
          </tbody>
        </table>
        <h2 class="inv-subtitle">اجرای شناسایی درآمد دوره</h2>
        <div class="inv-filters mb-2">
          <select v-model="periodId" class="form-select form-select-sm"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="runPoc(true)">پیش‌نمایش</button>
          <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="runPoc(false)">ثبت درآمد دوره</button>
        </div>
        <p v-if="pocInputs.length" class="inv-muted">در صورت خالی بودن، پیشرفت از منبع تعیین‌شده خوانده می‌شود؛ برای ورود دستی درصد پیشرفت و برآورد هزینه تا پایان را وارد کنید.</p>
        <table v-if="pocInputs.length" class="table table-sm">
          <thead><tr><th>پروژه</th><th>درصد پیشرفت (دستی)</th><th>برآورد هزینه تا پایان (ریال)</th></tr></thead>
          <tbody><tr v-for="i in pocInputs" :key="i.contractId"><td>{{ i.contractId }}</td><td><input v-model="i.progressPercent" class="form-control form-control-sm fn-num" /></td><td><input v-model="i.estimatedCost" class="form-control form-control-sm fn-num" /></td></tr></tbody>
        </table>
        <template v-if="pocResult">
          <p>
            <span v-if="pocResult.dryRun" class="inv-badge warn">پیش‌نمایش</span><span v-else class="inv-badge ok">ثبت‌شده {{ pocResult.number }}</span>
            جمع درآمد این دوره: <b>{{ money(pocResult.totalRevenue) }}</b> ریال <span v-if="pocResult.voucherId">- سند {{ pocResult.voucherId }}</span>
          </p>
          <table class="table table-sm">
            <thead><tr><th>پروژه</th><th>پیشرفت</th><th>مبلغ قرارداد</th><th>برآورد هزینه</th><th>درآمد تا امروز</th><th>درآمد ثبت‌شده</th><th>درآمد این دوره</th><th>دارایی قراردادی</th><th>بدهی قراردادی</th><th>ذخیره زیان</th><th>تغییر ذخیره</th></tr></thead>
            <tbody>
              <tr v-for="r in pocResult.rows" :key="r.contractId"><td>{{ r.contractId }}</td><td class="fn-num">{{ Number(r.progressPercent).toFixed(1) }}%</td><td class="fn-num">{{ money(r.contractPrice) }}</td><td class="fn-num">{{ money(r.estimatedCost) }}</td><td class="fn-num">{{ money(r.earnedToDate) }}</td><td class="fn-num">{{ money(r.revenueInLedger) }}</td><td class="fn-num">{{ money(r.revenueThisRun) }}</td><td class="fn-num">{{ money(r.contractAsset) }}</td><td class="fn-num">{{ money(r.contractLiability) }}</td><td class="fn-num">{{ money(r.lossProvision) }}</td><td class="fn-num">{{ money(r.lossProvisionChange) }}</td></tr>
            </tbody>
          </table>
        </template>
      </template>
    </div>

    <PromptModal
      v-if="dialog"
      :title="dialog === 'close' ? 'بستن دستور ساخت' : 'بازگشایی دستور ساخت'"
      :fields="[{ key: 'quantity', label: 'تعداد تولیدشده (خالی = تعداد دستور)', type: 'number' }, { key: 'date', label: 'تاریخ', type: 'date', value: today() }, { key: 'reason', label: 'علت', type: 'textarea', required: dialog === 'reopen' }]"
      @confirm="confirmWo"
      @cancel="dialog = null" />

    <div v-if="rForm" class="inv-modal-backdrop screen-only" @click.self="rForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">نرخ سربار</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">کد مرکز هزینه *</label><input v-model="rForm.costCenterCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مبنای جذب</label><select v-model="rForm.base" class="form-select form-select-sm"><option v-for="(t, k) in overheadBases" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">نرخ به ازای هر واحد مبنا (ریال) *</label><input v-model="rForm.rate" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">منبع نرخ</label><input v-model="rForm.source" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="rForm.validFrom" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="rForm.validTo" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="rForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveRate">ذخیره</button></div>
      </div>
    </div>

    <div v-if="kForm" class="inv-modal-backdrop screen-only" @click.self="kForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">کلید تسهیم</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">از مرکز هزینه *</label><input v-model="kForm.fromCostCenterCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">به مرکز هزینه *</label><input v-model="kForm.toCostCenterCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">مبنا</label><select v-model="kForm.basis" class="form-select form-select-sm"><option v-for="(t, k) in basisNames" :key="k" :value="k">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">مقدار *</label><input v-model="kForm.value" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">ترتیب اجرا</label><input v-model="kForm.sequence" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="kForm.validFrom" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="kForm.validTo" /></div>
          <div class="col-12"><label class="form-label small mb-0">منبع</label><input v-model="kForm.source" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="kForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveKey">ذخیره</button></div>
      </div>
    </div>

    <div v-if="bForm" class="inv-modal-backdrop screen-only" @click.self="bForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">مبنای سربار دستور ساخت</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">شناسه دستور ساخت *</label><input v-model="bForm.workOrderId" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">کد مرکز هزینه *</label><input v-model="bForm.costCenterCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مبنا</label><select v-model="bForm.base" class="form-select form-select-sm"><option v-for="(t, k) in overheadBases" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">مقدار مبنا</label><input v-model="bForm.quantity" class="form-control form-control-sm fn-num" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="bForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveBase">ذخیره</button></div>
      </div>
    </div>

    <div v-if="sForm" class="inv-modal-backdrop screen-only" @click.self="sForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">بهای استاندارد کالا</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">شناسه کالا *</label><input v-model="sForm.itemId" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">بهای استاندارد (ریال) *</label><input v-model="sForm.cost" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="sForm.validFrom" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">منبع</label><input v-model="sForm.source" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="sForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveStd">ذخیره</button></div>
      </div>
    </div>

    <div v-if="cForm" class="inv-modal-backdrop screen-only" @click.self="cForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">روش شناسایی درآمد پروژه</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">پروژه *</label><ContractPick v-model="cForm.contractId" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">روش</label><select v-model="cForm.method" class="form-select form-select-sm"><option v-for="(t, k) in revenueMethods" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">مبلغ قرارداد (ریال)</label><input v-model="cForm.contractPrice" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">منبع پیشرفت</label><select v-model="cForm.progressSource" class="form-select form-select-sm"><option v-for="(t, k) in progressSources" :key="k" :value="k">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">روش اندازه‌گیری</label><select v-model="cForm.progressMeasure" class="form-select form-select-sm"><option v-for="(t, k) in progressMeasures" :key="k" :value="k">{{ t }}</option></select></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="cForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveContract">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
