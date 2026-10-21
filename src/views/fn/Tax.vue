<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import { getTaxpayerSales, getTaxRates, getVatSeason, saveTaxRate } from "@/api/FnAPIs";
import { camelize, invoiceTypes, jalaliNow, money, parseAmount, seasonNames, taxKinds, usePagedList } from "@/composables/fn";
import { fail, jalali, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Tax rate tables (data, not code), the VAT season summary and the taxpayer sales list that is handed over to the tax system through Sepidar.
const tab = ref("rates");
const tabs = [{ key: "rates", title: "جدول نرخ‌ها" }, { key: "vat", title: "خلاصه ارزش افزوده فصلی" }, { key: "sales", title: "فهرست فروش مودیان" }];
const rates = usePagedList(getTaxRates, ref({}));
const form = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);

const now = jalaliNow();
const vatYear = ref(String(now.year));
const vatSeason = ref(Math.ceil(now.month / 3));
const vat = ref<any>(null);
const sFilters = ref({ From: null as string | null, To: null as string | null });
const sales = usePagedList(getTaxpayerSales, sFilters);

async function saveRate() {
  const f = form.value;
  const rate = parseAmount(f.rate);
  if (rate === null || rate === undefined || isNaN(rate as number)) return fail("نرخ را وارد کنید.");
  if (!f.effectiveFrom) return fail("تاریخ شروع اعتبار را وارد کنید.");
  busy.value = true;
  const [, error] = await saveTaxRate({ id: f.id, kind: Number(f.kind), rate, effectiveFrom: f.effectiveFrom, effectiveTo: f.effectiveTo, source: f.source || null, verify: !!f.verify, note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("نرخ ذخیره شد.");
  form.value = null;
  rates.load();
}

async function loadVat() {
  const y = parseAmount(vatYear.value);
  if (!y) return fail("سال را وارد کنید.");
  const [d, error] = await getVatSeason(y, vatSeason.value);
  if (error) return fail(error);
  vat.value = camelize(d);
}

onMounted(() => {
  rates.load();
  sales.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">مالیات</h1></div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'rates'">
        <div class="mb-2"><button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = { id: null, kind: 1, rate: '', effectiveFrom: null, effectiveTo: null, source: '', verify: false, note: '' }">نرخ جدید</button></div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>نوع</th><th>نرخ (٪)</th><th>از تاریخ</th><th>تا تاریخ</th><th>مأخذ</th><th>نیاز به راستی‌آزمایی</th><th>توضیح</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!rates.rows.value.length"><td colspan="8" class="text-center inv-muted">نرخی ثبت نشده است.</td></tr>
              <tr v-for="r in rates.rows.value" :key="r.id">
                <td>{{ taxKinds[r.kind] }}</td><td class="fn-num">{{ r.rate }}</td><td>{{ jalali(r.effectiveFrom) }}</td><td>{{ r.effectiveTo ? jalali(r.effectiveTo) : "" }}</td><td>{{ r.source }}</td>
                <td><span v-if="r.verify" class="inv-badge warn">بررسی شود</span></td><td>{{ r.note }}</td>
                <td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="form = { ...r, rate: String(r.rate), effectiveFrom: String(r.effectiveFrom).slice(0, 10), effectiveTo: r.effectiveTo ? String(r.effectiveTo).slice(0, 10) : null }">ویرایش</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="rates.page.value" :total="rates.total.value" @go="rates.go" />
      </template>

      <template v-if="tab === 'vat'">
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">سال</label><input v-model="vatYear" class="form-control form-control-sm fn-num" style="width: 6rem" /></div>
          <div><label class="form-label small mb-0">فصل</label><select v-model="vatSeason" class="form-select form-select-sm"><option v-for="(t, k) in seasonNames" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <button class="btn btn-sm btn-secondary align-self-end" @click="loadVat">نمایش</button>
        </div>
        <template v-if="vat">
          <p class="small inv-muted">{{ jalali(vat.from) }} تا {{ jalali(vat.to) }}</p>
          <div class="fn-tiles mb-3">
            <div class="fn-tile"><div class="t">فروش (خالص)</div><div class="v">{{ money(vat.salesNet) }}</div></div>
            <div class="fn-tile"><div class="t">مالیات فروش</div><div class="v">{{ money(vat.salesVat) }}</div></div>
            <div class="fn-tile"><div class="t">خرید (خالص)</div><div class="v">{{ money(vat.purchasesNet) }}</div></div>
            <div class="fn-tile"><div class="t">مالیات خرید</div><div class="v">{{ money(vat.purchasesVat) }}</div></div>
            <div class="fn-tile"><div class="t">ارزش افزوده قابل پرداخت</div><div class="v">{{ money(vat.vatPayable) }}</div></div>
          </div>
          <div class="table-responsive">
            <table class="table table-sm">
              <thead><tr><th>سمت</th><th>طرف حساب</th><th>کد اقتصادی</th><th>مبلغ خالص</th><th>مالیات</th><th>تعداد سند</th></tr></thead>
              <tbody>
                <tr v-for="(r, i) in vat.rows" :key="i"><td>{{ r.side === "Sales" ? "فروش" : r.side === "Purchases" ? "خرید" : r.side }}</td><td>{{ r.partyName }}</td><td class="inv-ltr">{{ r.economicCode }}</td><td class="fn-num">{{ money(r.netAmount) }}</td><td class="fn-num">{{ money(r.vatAmount) }}</td><td>{{ r.documents }}</td></tr>
              </tbody>
            </table>
          </div>
        </template>
      </template>

      <template v-if="tab === 'sales'">
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="sFilters.From" /></div>
          <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="sFilters.To" /></div>
          <button class="btn btn-sm btn-secondary align-self-end" @click="sales.search">نمایش</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>شماره</th><th>تاریخ</th><th>نوع</th><th>مشتری</th><th>کد اقتصادی</th><th>شناسه ملی</th><th>کد پستی</th><th>مبلغ خالص</th><th>مالیات</th><th>جمع</th><th>شناسه یکتا</th><th>ارسال</th></tr></thead>
            <tbody>
              <tr v-if="!sales.rows.value.length"><td colspan="12" class="text-center inv-muted">موردی نیست.</td></tr>
              <tr v-for="s in sales.rows.value" :key="s.invoiceId">
                <td class="inv-ltr">{{ s.number }}</td><td>{{ jalali(s.date) }}</td><td>{{ invoiceTypes[s.type] }}</td><td>{{ s.customerName }}</td><td class="inv-ltr">{{ s.economicCode }}</td><td class="inv-ltr">{{ s.nationalId }}</td><td class="inv-ltr">{{ s.postalCode }}</td>
                <td class="fn-num">{{ money(s.netAmount) }}</td><td class="fn-num">{{ money(s.vatAmount) }}</td><td class="fn-num">{{ money(s.grossAmount) }}</td><td class="inv-ltr">{{ s.taxId }}</td>
                <td><span :class="s.taxSentStatus ? 'ok' : 'warn'" class="inv-badge">{{ s.taxSentStatus ? "ارسال‌شده" : "ارسال‌نشده" }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="sales.page.value" :total="sales.total.value" @go="sales.go" />
      </template>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش نرخ" : "نرخ جدید" }}</h2>
        <div class="row g-2">
          <div class="col-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in taxKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">نرخ (٪) *</label><input v-model="form.rate" class="form-control form-control-sm fn-num" /></div>
          <div class="col-6"><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="form.effectiveFrom" /></div>
          <div class="col-6"><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="form.effectiveTo" /></div>
          <div class="col-12"><label class="form-label small mb-0">مأخذ (قانون / بخشنامه)</label><input v-model="form.source" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-check small"><input v-model="form.verify" class="form-check-input" type="checkbox" /> نیاز به راستی‌آزمایی با مرجع رسمی</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveRate">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
