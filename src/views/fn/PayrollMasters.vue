<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import { getPayrollItems, getPayrollRates, getPayrollSetup, getTaxBrackets, getWorkshops, savePayrollItem, savePayrollRate, saveTaxBrackets, saveWorkshop } from "@/api/FnAPIs";
import { camelize, jalaliNow, money, parseAmount, payrollCalcs, payrollItemKinds, usePagedList, yesNo } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Payroll master data (confidential): set-up check, insurance workshops, payroll items, the rate table (legal values with their source) and the income tax brackets.
const tab = ref("setup");
const tabs = [{ key: "setup", title: "بررسی راه‌اندازی" }, { key: "workshops", title: "کارگاه‌های بیمه" }, { key: "items", title: "اقلام حقوق" }, { key: "rates", title: "جدول نرخ‌ها" }, { key: "brackets", title: "پله‌های مالیات حقوق" }];
const year = ref(String(jalaliNow().year));
const canWrite = hasRole(FnRoles.payrollUsers);
const busy = ref(false);
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const yearNumber = computed(() => parseAmount(year.value) || 0);

const setup = ref<any>(null);
async function loadSetup() {
  const [d, error] = await getPayrollSetup(yearNumber.value);
  if (error) return fail(error);
  setup.value = camelize(d);
}

// workshops
const workshops = ref<any[]>([]);
const wForm = ref<any>(null);
async function loadWorkshops() {
  const [d, error] = await getWorkshops();
  if (error) return fail(error);
  workshops.value = rowsOf(camelize(d));
}
async function saveW() {
  const f = wForm.value;
  if (!String(f.code).trim() || !String(f.name).trim()) return fail("کد و نام کارگاه را وارد کنید.");
  busy.value = true;
  const [, error] = await saveWorkshop({ ...f });
  busy.value = false;
  if (error) return fail(error);
  ok("کارگاه ذخیره شد.");
  wForm.value = null;
  loadWorkshops();
}

// items
const items = ref<any[]>([]);
const iForm = ref<any>(null);
async function loadItems() {
  const [d, error] = await getPayrollItems();
  if (error) return fail(error);
  items.value = rowsOf(camelize(d));
}
const blankItem = () => ({ id: null, code: "", title: "", kind: 1, insurable: false, taxable: false, calculation: 1, rateKey: "", variant: "", itemCodes: "", formula: "", factor: "1", factor2: "0", proRata: false, condition: "", reducesTaxable: false, accountRole: "", order: "10", isActive: true, source: "", verify: false });
async function saveI() {
  const f = iForm.value;
  if (!String(f.code).trim() || !String(f.title).trim()) return fail("کد و عنوان قلم را وارد کنید.");
  if (!String(f.source).trim()) return fail("منبع (ماده قانون یا مصوبه) را وارد کنید.");
  busy.value = true;
  const [, error] = await savePayrollItem({
    ...f, code: String(f.code).trim().toUpperCase(), kind: Number(f.kind), calculation: Number(f.calculation), factor: parseAmount(f.factor) ?? 1, factor2: parseAmount(f.factor2) || 0, order: parseAmount(f.order) || 0,
    rateKey: f.rateKey || null, variant: f.variant || null, itemCodes: f.itemCodes || null, formula: f.formula || null, condition: f.condition || null, accountRole: f.accountRole || null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("قلم ذخیره شد.");
  iForm.value = null;
  loadItems();
}

// rates
const rFilters = ref({ Year: null as number | null, Key: "" });
const rates = usePagedList(getPayrollRates, rFilters);
const rForm = ref<any>(null);
async function saveR() {
  const f = rForm.value;
  const value = parseAmount(f.value);
  if (!String(f.key).trim()) return fail("کلید نرخ را وارد کنید.");
  if (value === null || value === undefined || isNaN(value as number)) return fail("مقدار را وارد کنید.");
  if (!String(f.source).trim()) return fail("منبع قانونی مقدار را وارد کنید.");
  busy.value = true;
  const [, error] = await savePayrollRate({ year: parseAmount(f.year) || 0, key: String(f.key).trim(), value, source: f.source, verify: !!f.verify, note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("نرخ ذخیره شد.");
  rForm.value = null;
  rates.load();
}

// tax brackets
const brackets = ref<any[]>([]);
async function loadBrackets() {
  const [d, error] = await getTaxBrackets(yearNumber.value);
  if (error) return fail(error);
  brackets.value = rowsOf(camelize(d)).map((b: any) => ({ fromAmount: String(b.fromAmount), toAmount: b.toAmount === null || b.toAmount === undefined ? "" : String(b.toAmount), ratePercent: String(b.ratePercent) }));
}
async function saveB() {
  const list = brackets.value.map((b) => ({ fromAmount: parseAmount(b.fromAmount) || 0, toAmount: String(b.toAmount).trim() === "" ? null : parseAmount(b.toAmount), ratePercent: parseAmount(b.ratePercent) || 0 }));
  busy.value = true;
  const [, error] = await saveTaxBrackets(yearNumber.value, list);
  busy.value = false;
  if (error) return fail(error);
  ok("پله‌های مالیات ذخیره شد.");
  loadBrackets();
}

function openTab() {
  if (tab.value === "setup") loadSetup();
  if (tab.value === "workshops") loadWorkshops();
  if (tab.value === "items") loadItems();
  if (tab.value === "rates") rates.load();
  if (tab.value === "brackets") loadBrackets();
}
function setTab(k: string) {
  tab.value = k;
  openTab();
}
onMounted(openTab);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">تنظیمات حقوق و دستمزد</h1></div>
      <PcTabs :model-value="tab" :tabs="tabs" @update:model-value="setTab" />

      <template v-if="tab === 'setup' || tab === 'brackets'">
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">سال</label><input v-model="year" class="form-control form-control-sm fn-num" style="width: 6rem" /></div>
          <button class="btn btn-sm btn-secondary align-self-end" @click="tab === 'setup' ? loadSetup() : loadBrackets()">نمایش</button>
        </div>
      </template>

      <template v-if="tab === 'setup' && setup">
        <div :class="setup.ok ? 'alert-success' : 'alert-warning'" class="alert py-2">{{ setup.ok ? "تنظیمات حقوق این سال کامل است." : "تنظیمات حقوق این سال کامل نیست؛ تا تکمیل آن محاسبه حقوق انجام نمی‌شود." }}</div>
        <div v-if="setup.missingRateKeys?.length" class="mb-2"><b>نرخ‌های ثبت‌نشده:</b> <span v-for="k in setup.missingRateKeys" :key="k" class="inv-badge bad me-1 inv-ltr">{{ k }}</span></div>
        <div v-if="setup.taxBracketsMissing" class="mb-2"><span class="inv-badge bad">پله‌های مالیات حقوق این سال ثبت نشده است.</span></div>
        <div v-if="setup.items?.length" class="mb-2"><b>اقلام نیازمند توجه:</b><ul class="mb-0"><li v-for="(i, n) in setup.items" :key="n">{{ i }}</li></ul></div>
      </template>

      <template v-if="tab === 'workshops'">
        <div class="mb-2"><button v-if="canWrite" class="btn btn-sm btn-primary" @click="wForm = { id: null, code: '', name: '', employerName: '', address: '', branch: '', isContractor: false, contractRow: '', rateSet: '', isActive: true }">کارگاه جدید</button></div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>کد کارگاه</th><th>نام</th><th>کارفرما</th><th>شعبه</th><th>پیمانکاری</th><th>ردیف پیمان</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!workshops.length"><td colspan="8" class="text-center inv-muted">کارگاهی ثبت نشده است.</td></tr>
              <tr v-for="w in workshops" :key="w.id"><td class="inv-ltr">{{ w.code }}</td><td>{{ w.name }}</td><td>{{ w.employerName }}</td><td>{{ w.branch }}</td><td>{{ yesNo(w.isContractor) }}</td><td class="inv-ltr">{{ w.contractRow }}</td><td><span :class="w.isActive ? 'ok' : 'bad'" class="inv-badge">{{ w.isActive ? "فعال" : "غیرفعال" }}</span></td><td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="wForm = { ...w }">ویرایش</button></td></tr>
            </tbody>
          </table>
        </div>
      </template>

      <template v-if="tab === 'items'">
        <div class="mb-2"><button v-if="canWrite" class="btn btn-sm btn-primary" @click="iForm = blankItem()">قلم جدید</button></div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>ترتیب</th><th>کد</th><th>عنوان</th><th>نوع</th><th>روش محاسبه</th><th>مشمول بیمه</th><th>مشمول مالیات</th><th>منبع</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!items.length"><td colspan="10" class="text-center inv-muted">قلمی ثبت نشده است.</td></tr>
              <tr v-for="i in items" :key="i.id">
                <td>{{ i.order }}</td><td class="inv-ltr">{{ i.code }}</td><td>{{ i.title }}</td><td>{{ payrollItemKinds[i.kind] }}</td><td>{{ payrollCalcs[i.calculation] }}</td><td>{{ yesNo(i.insurable) }}</td><td>{{ yesNo(i.taxable) }}</td>
                <td>{{ i.source }}<span v-if="i.verify" class="inv-badge warn me-1">بررسی شود</span></td><td><span :class="i.isActive ? 'ok' : 'bad'" class="inv-badge">{{ i.isActive ? "فعال" : "غیرفعال" }}</span></td>
                <td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="iForm = { ...i, factor: String(i.factor), factor2: String(i.factor2), order: String(i.order), rateKey: i.rateKey || '', variant: i.variant || '', itemCodes: i.itemCodes || '', formula: i.formula || '', condition: i.condition || '', accountRole: i.accountRole || '' }">ویرایش</button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>

      <template v-if="tab === 'rates'">
        <div class="inv-filters mb-3">
          <input v-model.number="rFilters.Year" class="form-control form-control-sm fn-num" placeholder="سال (۰ = بلندمدت)" style="max-width: 11rem" />
          <input v-model="rFilters.Key" class="form-control form-control-sm inv-ltr" placeholder="کلید" />
          <button class="btn btn-sm btn-secondary" @click="rates.search">جستجو</button>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="rForm = { year: year, key: '', value: '', source: '', verify: false, note: '' }">نرخ جدید</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>سال</th><th>کلید</th><th>مقدار</th><th>منبع قانونی</th><th>توضیح</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!rates.rows.value.length"><td colspan="6" class="text-center inv-muted">نرخی ثبت نشده است.</td></tr>
              <tr v-for="r in rates.rows.value" :key="r.id"><td>{{ r.year || "بلندمدت" }}</td><td class="inv-ltr">{{ r.key }}</td><td class="fn-num">{{ money(r.value) }}</td><td>{{ r.source }}<span v-if="r.verify" class="inv-badge warn me-1">بررسی شود</span></td><td>{{ r.note }}</td><td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="rForm = { year: String(r.year), key: r.key, value: String(r.value), source: r.source || '', verify: r.verify, note: r.note || '' }">ویرایش</button></td></tr>
            </tbody>
          </table>
        </div>
        <QPager :page="rates.page.value" :total="rates.total.value" @go="rates.go" />
      </template>

      <template v-if="tab === 'brackets'">
        <div class="table-responsive">
          <table class="table table-sm" style="max-width: 640px">
            <thead><tr><th>از (ریال)</th><th>تا (ریال، خالی = بدون سقف)</th><th>نرخ (٪)</th><th v-if="canWrite"></th></tr></thead>
            <tbody>
              <tr v-for="(b, i) in brackets" :key="i">
                <td><input v-model="b.fromAmount" :disabled="!canWrite" class="form-control form-control-sm fn-num" /></td>
                <td><input v-model="b.toAmount" :disabled="!canWrite" class="form-control form-control-sm fn-num" /></td>
                <td><input v-model="b.ratePercent" :disabled="!canWrite" class="form-control form-control-sm fn-num" /></td>
                <td v-if="canWrite"><button class="btn btn-sm btn-link text-danger p-0" @click="brackets.splice(i, 1)">حذف</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-if="canWrite" class="d-flex gap-2"><button class="btn btn-sm btn-outline-secondary" @click="brackets.push({ fromAmount: '', toAmount: '', ratePercent: '' })">پله جدید</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveB">ذخیره پله‌ها</button></div>
      </template>
    </div>

    <div v-if="wForm" class="inv-modal-backdrop screen-only" @click.self="wForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">کارگاه بیمه</h2>
        <div class="row g-2">
          <div class="col-4"><label class="form-label small mb-0">کد کارگاه *</label><input v-model="wForm.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-8"><label class="form-label small mb-0">نام *</label><input v-model="wForm.name" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">کارفرما</label><input v-model="wForm.employerName" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">شعبه بیمه</label><input v-model="wForm.branch" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">نشانی</label><input v-model="wForm.address" class="form-control form-control-sm" /></div>
          <div class="col-6"><label class="form-label small mb-0">ردیف پیمان</label><input v-model="wForm.contractRow" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">مجموعه نرخ</label><input v-model="wForm.rateSet" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12 d-flex gap-3"><label class="form-check small"><input v-model="wForm.isContractor" class="form-check-input" type="checkbox" /> کارگاه پیمانکاری</label><label class="form-check small"><input v-model="wForm.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="wForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveW">ذخیره</button></div>
      </div>
    </div>

    <div v-if="iForm" class="inv-modal-backdrop screen-only" @click.self="iForm = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ iForm.id ? "ویرایش قلم حقوق" : "قلم حقوق جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">کد (انگلیسی بزرگ) *</label><input v-model="iForm.code" :disabled="!!iForm.id" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-5"><label class="form-label small mb-0">عنوان *</label><input v-model="iForm.title" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">ترتیب</label><input v-model="iForm.order" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">ضریب</label><input v-model="iForm.factor" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع</label><select v-model="iForm.kind" class="form-select form-select-sm"><option v-for="(t, k) in payrollItemKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">روش محاسبه</label><select v-model="iForm.calculation" class="form-select form-select-sm"><option v-for="(t, k) in payrollCalcs" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">شرط (Married, HasChildren, HardAndHarmful)</label><input v-model="iForm.condition" class="form-control form-control-sm inv-ltr" /></div>
          <div v-if="[1, 2, 3, 6].includes(Number(iForm.calculation))" class="col-md-4"><label class="form-label small mb-0">کلید جدول نرخ‌ها</label><input v-model="iForm.rateKey" class="form-control form-control-sm inv-ltr" /></div>
          <div v-if="[3, 7].includes(Number(iForm.calculation))" class="col-md-4"><label class="form-label small mb-0">نوع ({{ Number(iForm.calculation) === 3 ? "OVERTIME، NIGHT، HOLIDAY" : "LOAN، ADVANCE" }})</label><input v-model="iForm.variant" class="form-control form-control-sm inv-ltr" /></div>
          <div v-if="Number(iForm.calculation) === 4" class="col-md-4"><label class="form-label small mb-0">کدهای اقلام مبنا (یا @INSURABLE)</label><input v-model="iForm.itemCodes" class="form-control form-control-sm inv-ltr" /></div>
          <div v-if="Number(iForm.calculation) === 5" class="col-md-8"><label class="form-label small mb-0">فرمول</label><input v-model="iForm.formula" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نقش حساب بستانکار (برای کسور و ذخایر)</label><input v-model="iForm.accountRole" class="form-control form-control-sm inv-ltr" placeholder="InsurancePayable" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">منبع (ماده قانون یا مصوبه) *</label><input v-model="iForm.source" class="form-control form-control-sm" /></div>
          <div class="col-12 d-flex flex-wrap gap-3">
            <label class="form-check small"><input v-model="iForm.insurable" class="form-check-input" type="checkbox" /> مشمول بیمه</label>
            <label class="form-check small"><input v-model="iForm.taxable" class="form-check-input" type="checkbox" /> مشمول مالیات</label>
            <label class="form-check small"><input v-model="iForm.proRata" class="form-check-input" type="checkbox" /> متناسب با کارکرد</label>
            <label class="form-check small"><input v-model="iForm.reducesTaxable" class="form-check-input" type="checkbox" /> کاهنده مبنای مالیات</label>
            <label class="form-check small"><input v-model="iForm.verify" class="form-check-input" type="checkbox" /> نیاز به راستی‌آزمایی</label>
            <label class="form-check small"><input v-model="iForm.isActive" class="form-check-input" type="checkbox" /> فعال</label>
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="iForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveI">ذخیره</button></div>
      </div>
    </div>

    <div v-if="rForm" class="inv-modal-backdrop screen-only" @click.self="rForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">نرخ</h2>
        <div class="row g-2">
          <div class="col-4"><label class="form-label small mb-0">سال (۰ = بلندمدت)</label><input v-model="rForm.year" class="form-control form-control-sm fn-num" /></div>
          <div class="col-8"><label class="form-label small mb-0">کلید *</label><input v-model="rForm.key" class="form-control form-control-sm inv-ltr" /><div v-if="rForm.key === 'BonusGrossCapDaysOfMinimumWage'" class="inv-muted small">سقف ناخالص عیدی برحسب روز حداقل مزد روزانه؛ مبنای محاسبه سقف در اجرای عیدی (مقدار باید راستی‌آزمایی شود).</div></div>
          <div class="col-12"><label class="form-label small mb-0">مقدار *</label><input v-model="rForm.value" class="form-control form-control-sm fn-num" /></div>
          <div class="col-12"><label class="form-label small mb-0">منبع قانونی *</label><input v-model="rForm.source" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="rForm.note" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-check small"><input v-model="rForm.verify" class="form-check-input" type="checkbox" /> نیاز به راستی‌آزمایی</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="rForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveR">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
