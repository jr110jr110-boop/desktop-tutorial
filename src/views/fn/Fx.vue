<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import { getCurrencies, getExchangeRates, getFxExposure, getRevaluations, runRevaluation, saveCurrency, saveExchangeRate, settleForeign } from "@/api/FnAPIs";
import { fail, jalali, num, ok, today } from "@/composables/inventory";
import { money, moneyOrDash, parseAmount, usePagedList, yesNo } from "@/composables/fn";
import { rateSources, rateTypes, usePeriods } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Foreign currency: currencies (finance manager), exchange rates, period-end revaluation (dry run first), realised difference at settlement and the exposure report.
const tab = ref("rates");
const tabs = [{ key: "rates", title: "نرخ ارز" }, { key: "currencies", title: "ارزها" }, { key: "reval", title: "تسعیر پایان دوره" }, { key: "settle", title: "تسویه ارزی" }, { key: "exposure", title: "گزارش ریسک ارزی" }];
const { periods, load: loadPeriods, label } = usePeriods();
const canRate = hasRole(FnRoles.treasuryAndAccountants);
const canManage = hasRole(FnRoles.managers);
const canRun = hasRole(FnRoles.accountants);
const busy = ref(false);

const currencies = ref<any[]>([]);
const cForm = ref<any>(null);
async function loadCurrencies() {
  const [d, error] = await getCurrencies();
  if (error) return fail(error);
  currencies.value = d || [];
}
async function saveC() {
  const f = cForm.value;
  if (!String(f.code).trim() || !String(f.title).trim()) return fail("کد و عنوان ارز را وارد کنید.");
  busy.value = true;
  const [, error] = await saveCurrency({ code: String(f.code).trim().toUpperCase(), title: f.title, decimals: Number(f.decimals) || 0, isBase: f.isBase });
  busy.value = false;
  if (error) return fail(error);
  ok("ارز ذخیره شد.");
  cForm.value = null;
  loadCurrencies();
}

const filters = ref<any>({ CurrencyCode: "", RateType: null as number | null, From: null as string | null, To: null as string | null });
const rates = usePagedList(getExchangeRates, filters);
const rForm = ref<any>(null);
const blankRate = () => ({ currencyCode: "", date: today(), buy: "", sell: "", average: "", source: 1, rateType: 1, note: "" });
async function saveR() {
  const f = rForm.value;
  if (!f.currencyCode || !f.date) return fail("ارز و تاریخ را وارد کنید.");
  const average = parseAmount(f.average), buy = parseAmount(f.buy), sell = parseAmount(f.sell);
  if (!average && !(buy && sell)) return fail("نرخ میانگین یا هر دو نرخ خرید و فروش را وارد کنید.");
  busy.value = true;
  const [, error] = await saveExchangeRate({ currencyCode: f.currencyCode, date: f.date, buy, sell, average, source: Number(f.source), rateType: Number(f.rateType), note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("نرخ ذخیره شد.");
  rForm.value = null;
  rates.search();
}

// revaluation
const rv = ref<{ periodId: number | null; rateType: number | null; reverse: boolean }>({ periodId: null, rateType: null, reverse: true });
const rvResult = ref<any>(null);
const history = ref<any[]>([]);
async function loadHistory() {
  const [d] = await getRevaluations(null);
  history.value = d || [];
}
async function reval(dryRun: boolean) {
  if (!rv.value.periodId) return fail("دوره را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await runRevaluation({ periodId: rv.value.periodId, dryRun, reverse: rv.value.reverse, rateType: rv.value.rateType });
  busy.value = false;
  if (error) return fail(error);
  rvResult.value = d;
  ok(dryRun ? "پیش‌نمایش تسعیر آماده است." : "تسعیر ثبت شد.");
  if (!dryRun) loadHistory();
}

// settlement
const sForm = ref<any>({ date: today(), direction: "Receivable", accountCode: "", detailCode: "", counterAccountCode: "", counterDetailCode: "", currencyCode: "", currencyAmount: "", bookRate: "", settleRate: "", description: "" });
const sResult = ref<any>(null);
async function settle() {
  const f = sForm.value;
  const amount = parseAmount(f.currencyAmount), book = parseAmount(f.bookRate), rate = parseAmount(f.settleRate);
  if (!f.date || !f.accountCode || !f.counterAccountCode || !f.currencyCode || !amount || !book || !rate) return fail("تاریخ، حساب‌ها، ارز، مبلغ و دو نرخ را وارد کنید.");
  busy.value = true;
  const [d, error] = await settleForeign({ ...f, detailCode: f.detailCode || null, counterDetailCode: f.counterDetailCode || null, currencyAmount: amount, bookRate: book, settleRate: rate, description: f.description || null });
  busy.value = false;
  if (error) return fail(error);
  sResult.value = d;
  ok("تسویه ثبت شد.");
}

// exposure
const asOf = ref<string | null>(null);
const exposure = ref<any>(null);
async function loadExposure() {
  const [d, error] = await getFxExposure(asOf.value);
  if (error) return fail(error);
  exposure.value = d;
}

onMounted(() => {
  loadCurrencies();
  rates.load();
  loadPeriods();
  loadHistory();
  loadExposure();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">ارز و تسعیر</h1>
        <button v-if="canRate && tab === 'rates'" class="btn btn-sm btn-primary" @click="rForm = blankRate()">ثبت نرخ</button>
        <button v-if="canManage && tab === 'currencies'" class="btn btn-sm btn-primary" @click="cForm = { code: '', title: '', decimals: 2, isBase: false }">ارز جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'rates'">
        <div class="inv-filters mb-3">
          <select v-model="filters.CurrencyCode" class="form-select form-select-sm"><option value="">همه ارزها</option><option v-for="c in currencies" :key="c.code" :value="c.code">{{ c.code }} - {{ c.title }}</option></select>
          <select v-model="filters.RateType" class="form-select form-select-sm"><option :value="null">همه انواع نرخ</option><option v-for="(t, k) in rateTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
          <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
          <button class="btn btn-sm btn-secondary" @click="rates.search">جستجو</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>ارز</th><th>تاریخ</th><th>نوع نرخ</th><th>خرید</th><th>فروش</th><th>میانگین (ریال)</th><th>منبع</th><th>توضیح</th></tr></thead>
          <tbody>
            <tr v-if="!rates.rows.value.length"><td colspan="8" class="text-center inv-muted">نرخی ثبت نشده است.</td></tr>
            <tr v-for="r in rates.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.currencyCode }}</td><td>{{ jalali(r.date) }}</td><td>{{ rateTypes[r.rateType] }}</td><td class="fn-num">{{ num(r.buy) }}</td><td class="fn-num">{{ num(r.sell) }}</td><td class="fn-num">{{ num(r.average) }}</td><td>{{ rateSources[r.source] }}</td><td>{{ r.note }}</td>
            </tr>
          </tbody>
        </table>
        <QPager :page="rates.page.value" :total="rates.total.value" @go="rates.go" />
      </template>

      <template v-else-if="tab === 'currencies'">
        <table class="table table-sm">
          <thead><tr><th>کد</th><th>عنوان</th><th>اعشار</th><th>ارز پایه</th><th v-if="canManage"></th></tr></thead>
          <tbody>
            <tr v-for="c in currencies" :key="c.code"><td class="inv-ltr">{{ c.code }}</td><td>{{ c.title }}</td><td>{{ c.decimals }}</td><td>{{ yesNo(c.isBase) }}</td><td v-if="canManage"><button class="btn btn-sm btn-outline-secondary" @click="cForm = { ...c }">ویرایش</button></td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'reval'">
        <div class="inv-filters mb-3">
          <select v-model="rv.periodId" class="form-select form-select-sm"><option :value="null">دوره...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
          <select v-model="rv.rateType" class="form-select form-select-sm"><option :value="null">نوع نرخ طبق تنظیمات</option><option v-for="(t, k) in rateTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <label class="form-check small"><input v-model="rv.reverse" class="form-check-input" type="checkbox" /> برگشت خودکار در دوره بعد</label>
          <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="reval(true)">پیش‌نمایش تسعیر</button>
          <button v-if="canRun" :disabled="busy || !rvResult || !rvResult.dryRun" class="btn btn-sm btn-primary" @click="reval(false)">ثبت سند تسعیر</button>
        </div>
        <div v-if="rvResult">
          <p>
            <span :class="rvResult.dryRun ? 'warn' : 'ok'" class="inv-badge">{{ rvResult.dryRun ? "پیش‌نمایش" : "ثبت شد" }}</span>
            سود تسعیر: <b>{{ money(rvResult.totalGain) }}</b> - زیان تسعیر: <b>{{ money(rvResult.totalLoss) }}</b>
            <span v-if="rvResult.voucherId"> - سند {{ rvResult.voucherId }}</span>
          </p>
          <p v-if="rvResult.warning" class="alert alert-warning py-2">{{ rvResult.warning }}</p>
          <div class="table-responsive">
            <table class="table table-sm">
              <thead><tr><th>حساب</th><th>تفصیلی</th><th>ارز</th><th>مانده ارزی</th><th>مانده دفتری (ریال)</th><th>نرخ</th><th>مانده جدید (ریال)</th><th>تفاوت (ریال)</th></tr></thead>
              <tbody>
                <tr v-if="!rvResult.lines.length"><td colspan="8" class="text-center inv-muted">قلم ارزی برای تسعیر نیست.</td></tr>
                <tr v-for="(l, i) in rvResult.lines" :key="i">
                  <td class="inv-ltr">{{ l.accountCode }}</td><td class="inv-ltr">{{ l.detailCode }}</td><td class="inv-ltr">{{ l.currencyCode }}</td><td class="fn-num">{{ num(l.currencyBalance) }}</td><td class="fn-num">{{ money(l.bookBalance) }}</td><td class="fn-num">{{ num(l.rate) }}</td><td class="fn-num">{{ money(l.newBalance) }}</td><td class="fn-num">{{ moneyOrDash(l.difference) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
        <div class="fn-section">تسعیرهای ثبت‌شده</div>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>زمان</th><th>سود</th><th>زیان</th><th>سند</th><th>سند برگشتی</th></tr></thead>
          <tbody>
            <tr v-if="!history.length"><td colspan="6" class="text-center inv-muted">تسعیری ثبت نشده است.</td></tr>
            <tr v-for="h in history" :key="h.id"><td class="inv-ltr">{{ h.number }}</td><td>{{ jalali(h.createdAt, true) }}</td><td class="fn-num">{{ money(h.totalGain) }}</td><td class="fn-num">{{ money(h.totalLoss) }}</td><td>{{ h.voucherId }}</td><td>{{ h.reversalVoucherId }}</td></tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'settle'">
        <p class="inv-muted">تفاوت نرخ در زمان تسویه یک دریافتنی یا پرداختنی ارزی (تفاوت تحقق‌یافته) با سند خودکار ثبت می‌شود.</p>
        <div class="row g-2 mb-2">
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ *</label><FnDate v-model="sForm.date" :disabled="!canRun" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نوع *</label><select v-model="sForm.direction" :disabled="!canRun" class="form-select form-select-sm"><option value="Receivable">دریافتنی ارزی</option><option value="Payable">پرداختنی ارزی</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">حساب ارزی *</label><input v-model="sForm.accountCode" :disabled="!canRun" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تفصیلی</label><input v-model="sForm.detailCode" :disabled="!canRun" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">حساب مقابل (بانک) *</label><input v-model="sForm.counterAccountCode" :disabled="!canRun" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">تفصیلی مقابل</label><input v-model="sForm.counterDetailCode" :disabled="!canRun" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">ارز *</label><input v-model="sForm.currencyCode" :disabled="!canRun" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">مبلغ ارزی *</label><input v-model="sForm.currencyAmount" :disabled="!canRun" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نرخ دفتری *</label><input v-model="sForm.bookRate" :disabled="!canRun" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">نرخ تسویه *</label><input v-model="sForm.settleRate" :disabled="!canRun" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">شرح</label><input v-model="sForm.description" :disabled="!canRun" class="form-control form-control-sm" /></div>
        </div>
        <button v-if="canRun" :disabled="busy" class="btn btn-sm btn-primary" @click="settle">ثبت تسویه</button>
        <p v-if="sResult" class="alert alert-success py-2 mt-3">
          تسویه <span class="inv-ltr">{{ sResult.number }}</span>: مبلغ دفتری {{ money(sResult.bookBase) }}، مبلغ تسویه {{ money(sResult.settledBase) }}، تفاوت {{ money(sResult.difference) }} ریال ({{ sResult.difference >= 0 ? "سود" : "زیان" }}). سند {{ sResult.voucherId }}.
        </p>
      </template>

      <template v-else>
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="asOf" /></div>
          <button class="btn btn-sm btn-secondary" @click="loadExposure">نمایش</button>
        </div>
        <template v-if="exposure">
          <p class="inv-muted">وضعیت تا تاریخ {{ jalali(exposure.asOf) }}. مانده‌ها به مبلغ ارزی هر ارز است.</p>
          <table class="table table-sm">
            <thead><tr><th>ارز</th><th>دریافتنی</th><th>پرداختنی</th><th>خالص</th><th>دریافتنی (ریال)</th><th>پرداختنی (ریال)</th><th>اعتبار اسنادی باز</th><th>خالص با اعتبار اسنادی</th></tr></thead>
            <tbody>
              <tr v-if="!exposure.rows.length"><td colspan="8" class="text-center inv-muted">قلم ارزی نیست.</td></tr>
              <tr v-for="r in exposure.rows" :key="r.currencyCode">
                <td class="inv-ltr">{{ r.currencyCode }}</td><td class="fn-num">{{ num(r.receivables) }}</td><td class="fn-num">{{ num(r.payables) }}</td><td class="fn-num">{{ num(r.net) }}</td>
                <td class="fn-num">{{ money(r.baseReceivables) }}</td><td class="fn-num">{{ money(r.basePayables) }}</td><td class="fn-num">{{ num(r.openLetterOfCredit) }}</td><td class="fn-num"><b>{{ num(r.netWithLetterOfCredit) }}</b></td>
              </tr>
            </tbody>
          </table>
        </template>
      </template>
    </div>

    <div v-if="cForm" class="inv-modal-backdrop screen-only" @click.self="cForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ارز</h2>
        <div class="mb-2"><label class="form-label small mb-0">کد *</label><input v-model="cForm.code" class="form-control form-control-sm inv-ltr" /></div>
        <div class="mb-2"><label class="form-label small mb-0">عنوان *</label><input v-model="cForm.title" class="form-control form-control-sm" /></div>
        <div class="mb-2"><label class="form-label small mb-0">تعداد اعشار</label><input v-model.number="cForm.decimals" class="form-control form-control-sm" type="number" min="0" max="6" /></div>
        <label class="form-check small"><input v-model="cForm.isBase" class="form-check-input" type="checkbox" /> ارز پایه</label>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="cForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveC">ذخیره</button></div>
      </div>
    </div>

    <div v-if="rForm" class="inv-modal-backdrop screen-only" @click.self="rForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ثبت نرخ ارز</h2>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">ارز *</label><select v-model="rForm.currencyCode" class="form-select form-select-sm"><option value="">-</option><option v-for="c in currencies" :key="c.code" :value="c.code">{{ c.code }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ *</label><FnDate v-model="rForm.date" /></div>
          <div class="col-4"><label class="form-label small mb-0">خرید</label><input v-model="rForm.buy" class="form-control form-control-sm fn-num" /></div>
          <div class="col-4"><label class="form-label small mb-0">فروش</label><input v-model="rForm.sell" class="form-control form-control-sm fn-num" /></div>
          <div class="col-4"><label class="form-label small mb-0">میانگین</label><input v-model="rForm.average" class="form-control form-control-sm fn-num" /></div>
          <div class="col-6"><label class="form-label small mb-0">نوع نرخ</label><select v-model="rForm.rateType" class="form-select form-select-sm"><option v-for="(t, k) in rateTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">منبع</label><select v-model="rForm.source" class="form-select form-select-sm"><option v-for="(t, k) in rateSources" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="rForm.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="rForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveR">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
