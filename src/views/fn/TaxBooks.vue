<script setup lang="ts">
import { onMounted, ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { deleteTaxAdjustment, getFiscalYears, getTaxableProfit, getTaxAdjustments, getVatDetail, provisionTaxableProfit, saveTaxAdjustment } from "@/api/FnAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { jalaliNow, money, parseAmount, seasonNames, yesNo } from "@/composables/fn";
import { taxAdjustmentKinds } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";

// Tax books: the seasonal VAT transactions list (sales and purchases with the carry-forward), the tax adjustments of a fiscal year and the taxable-profit report with the optional tax provision voucher.
const tab = ref("vat");
const tabs = [{ key: "vat", title: "دفتر مالیات بر ارزش افزوده فصلی" }, { key: "adjust", title: "تعدیلات مالیاتی" }, { key: "profit", title: "سود مشمول مالیات" }];
const canWrite = hasRole(FnRoles.accountants);
const busy = ref(false);
const years = ref<any[]>([]);
const now = jalaliNow();

// VAT
const vatYear = ref(String(now.year));
const vatSeason = ref(Math.ceil(now.month / 3));
const vat = ref<any>(null);
const sideNames: Record<string, string> = { Sales: "فروش", Purchases: "خرید" };
const sourceNames: Record<string, string> = { SalesInvoice: "فاکتور فروش", CreditNote: "اعتبار فروش", SupplierInvoice: "فاکتور خرید" };
async function loadVat() {
  const y = parseAmount(vatYear.value);
  if (!y) return fail("سال را وارد کنید.");
  const [d, error] = await getVatDetail(y, vatSeason.value);
  if (error) return fail(error);
  vat.value = d;
}

// adjustments and profit
const fiscalYearId = ref<number | null>(null);
const adjustments = ref<any[]>([]);
const form = ref<any>(null);
const rate = ref("");
const profit = ref<any>(null);
async function loadAdjustments() {
  if (!fiscalYearId.value) return;
  const [d, error] = await getTaxAdjustments(fiscalYearId.value);
  if (error) return fail(error);
  adjustments.value = d || [];
}
async function saveAdj() {
  const f = form.value;
  if (!fiscalYearId.value || !String(f.description).trim() || parseAmount(f.amount) === null) return fail("سال مالی، شرح و مبلغ را وارد کنید.");
  busy.value = true;
  const [, error] = await saveTaxAdjustment({ id: f.id || null, fiscalYearId: fiscalYearId.value, kind: Number(f.kind), description: f.description, amount: parseAmount(f.amount), source: f.source || null, verify: f.verify });
  busy.value = false;
  if (error) return fail(error);
  ok("تعدیل ذخیره شد.");
  form.value = null;
  loadAdjustments();
}
async function removeAdj(a: any) {
  const [, error] = await deleteTaxAdjustment(a.id);
  if (error) return fail(error);
  ok("تعدیل حذف شد.");
  loadAdjustments();
}
async function loadProfit() {
  if (!fiscalYearId.value) return fail("سال مالی را انتخاب کنید.");
  const [d, error] = await getTaxableProfit({ FiscalYearId: fiscalYearId.value, RatePercent: parseAmount(rate.value) });
  if (error) return fail(error);
  profit.value = d;
}
async function provision() {
  if (!parseAmount(rate.value)) return fail("نرخ مالیات را وارد کنید.");
  busy.value = true;
  const [d, error] = await provisionTaxableProfit({ fiscalYearId: fiscalYearId.value, ratePercent: parseAmount(rate.value), createProvision: true });
  busy.value = false;
  if (error) return fail(error);
  profit.value = d;
  ok("ذخیره مالیات ثبت شد.");
}

onMounted(async () => {
  const [y] = await getFiscalYears();
  years.value = y || [];
  fiscalYearId.value = years.value.find((x: any) => x.isCurrent)?.id || years.value[0]?.id || null;
  loadVat();
  loadAdjustments();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">دفاتر مالیاتی</h1>
        <button v-if="canWrite && tab === 'adjust'" class="btn btn-sm btn-primary" @click="form = { kind: 1, description: '', amount: '', source: '', verify: true }">تعدیل جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'vat'">
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">سال (شمسی)</label><input v-model="vatYear" class="form-control form-control-sm" style="max-width: 100px" /></div>
          <div><label class="form-label small mb-0">فصل</label><select v-model="vatSeason" class="form-select form-select-sm"><option v-for="(t, k) in seasonNames" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <button class="btn btn-sm btn-primary" @click="loadVat">نمایش</button>
        </div>
        <template v-if="vat">
          <p>از {{ jalali(vat.from) }} تا {{ jalali(vat.to) }}</p>
          <table class="table table-sm" style="max-width: 560px">
            <tbody>
              <tr><td>فروش خالص</td><td class="fn-num">{{ money(vat.salesNet) }}</td></tr>
              <tr><td>مالیات فروش</td><td class="fn-num">{{ money(vat.salesVat) }}</td></tr>
              <tr><td>خرید خالص</td><td class="fn-num">{{ money(vat.purchasesNet) }}</td></tr>
              <tr><td>مالیات خرید</td><td class="fn-num">{{ money(vat.purchasesVat) }}</td></tr>
              <tr><td>مالیات قابل پرداخت فصل</td><td class="fn-num">{{ money(vat.vatPayable) }}</td></tr>
              <tr><td>اعتبار منتقل‌شده از فصل قبل</td><td class="fn-num">{{ money(vat.carryIn) }}</td></tr>
              <tr class="fn-total"><td>خالص قابل پرداخت</td><td class="fn-num">{{ money(vat.netPayable) }}</td></tr>
              <tr><td>اعتبار منتقل‌شده به فصل بعد</td><td class="fn-num">{{ money(vat.carryOut) }}</td></tr>
            </tbody>
          </table>
          <table class="table table-sm">
            <thead><tr><th>سمت</th><th>تاریخ</th><th>شماره سند</th><th>طرف حساب</th><th>کد اقتصادی</th><th>مبلغ خالص</th><th>مالیات</th><th>منبع</th></tr></thead>
            <tbody>
              <tr v-if="!vat.rows.length"><td colspan="8" class="text-center inv-muted">تراکنشی در این فصل نیست.</td></tr>
              <tr v-for="(r, i) in vat.rows" :key="i"><td>{{ sideNames[r.side] || r.side }}</td><td>{{ jalali(r.date) }}</td><td class="inv-ltr">{{ r.documentNumber }}</td><td>{{ r.partyName }}</td><td class="inv-ltr">{{ r.economicCode }}</td><td class="fn-num">{{ money(r.netAmount) }}</td><td class="fn-num">{{ money(r.vatAmount) }}</td><td>{{ sourceNames[r.sourceType] || r.sourceType }}</td></tr>
            </tbody>
          </table>
        </template>
      </template>

      <template v-else>
        <div class="inv-filters mb-3">
          <select v-model="fiscalYearId" class="form-select form-select-sm" @change="loadAdjustments"><option :value="null">سال مالی...</option><option v-for="y in years" :key="y.id" :value="y.id">{{ y.title }}</option></select>
          <template v-if="tab === 'profit'">
            <input v-model="rate" class="form-control form-control-sm" placeholder="نرخ مالیات (درصد)" style="max-width: 160px" />
            <button class="btn btn-sm btn-primary" @click="loadProfit">محاسبه</button>
            <button v-if="canWrite" :disabled="busy || !profit" class="btn btn-sm btn-outline-primary" @click="provision">ثبت ذخیره مالیات</button>
          </template>
        </div>

        <table v-if="tab === 'adjust'" class="table table-sm">
          <thead><tr><th>نوع</th><th>شرح</th><th>مبلغ (ریال)</th><th>منبع</th><th>نیازمند تایید</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!adjustments.length"><td colspan="6" class="text-center inv-muted">تعدیلی ثبت نشده است.</td></tr>
            <tr v-for="a in adjustments" :key="a.id"><td>{{ taxAdjustmentKinds[a.kind] }}</td><td>{{ a.description }}</td><td class="fn-num">{{ money(a.amount) }}</td><td>{{ a.source }}</td><td>{{ yesNo(a.verify) }}</td><td v-if="canWrite" class="text-nowrap"><button class="btn btn-sm btn-outline-secondary me-1" @click="form = { ...a, amount: String(a.amount) }">ویرایش</button><button class="btn btn-sm btn-outline-danger" @click="removeAdj(a)">حذف</button></td></tr>
          </tbody>
        </table>

        <template v-else-if="profit">
          <table class="table table-sm" style="max-width: 560px">
            <tbody>
              <tr><td>سود دفتری قبل از مالیات</td><td class="fn-num">{{ money(profit.bookProfitBeforeTax) }}</td></tr>
              <tr><td>جمع تعدیلات مالیاتی</td><td class="fn-num">{{ money(profit.adjustments) }}</td></tr>
              <tr class="fn-total"><td>سود مشمول مالیات</td><td class="fn-num">{{ money(profit.taxableProfit) }}</td></tr>
              <tr><td>نرخ مالیات</td><td class="fn-num">{{ profit.ratePercent ?? "-" }}</td></tr>
              <tr><td>مالیات محاسبه‌شده</td><td class="fn-num">{{ money(profit.taxAmount) }}</td></tr>
              <tr v-if="profit.provisionVoucherId"><td>سند ذخیره مالیات</td><td>{{ profit.provisionVoucherId }}</td></tr>
            </tbody>
          </table>
          <table class="table table-sm">
            <thead><tr><th>نوع</th><th>شرح</th><th>مبلغ</th><th>منبع</th></tr></thead>
            <tbody><tr v-for="a in profit.items" :key="a.id"><td>{{ taxAdjustmentKinds[a.kind] }}</td><td>{{ a.description }}</td><td class="fn-num">{{ money(a.amount) }}</td><td>{{ a.source }}</td></tr></tbody>
          </table>
          <p class="inv-muted">نرخ مالیات ورودی مالی است و پیش‌فرض ندارد؛ آن را از مدیر مالی بگیرید.</p>
        </template>
      </template>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">تعدیل مالیاتی</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in taxAdjustmentKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">مبلغ (ریال؛ منفی = کاهنده) *</label><input v-model="form.amount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">شرح *</label><input v-model="form.description" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">منبع (ماده قانون / بخشنامه)</label><input v-model="form.source" class="form-control form-control-sm" /></div>
          <div class="col-12"><label><input v-model="form.verify" type="checkbox" /> نیازمند تایید مشاور مالیاتی</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveAdj">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
