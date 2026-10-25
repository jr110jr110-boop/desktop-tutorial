<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import RialInput from "@/components/sales/orders/RialInput.vue";
import { isoDay, todayIso } from "@/components/sales/orders/salesKit";
import { itemLabel, searchItems } from "@/components/production/master/masterKit";
import { getSlPriceList, getSlPriceLists, priceListAction, saveSlPriceList, suggestSlPrices } from "@/api/SalesAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { SalesRoles, hasRole } from "@/composables/roles";
import { badgeOfPriceList, costSources, persianOf, priceListStatuses, qtyText, rialText, salesActionTitles } from "@/composables/sales";

// Price lists (SlPriceLists): validity dates, lines (item, unit price, minimum quantity), cost-plus suggestions the user accepts per line,
// approval (maker-checker, the API refuses the creator/editor) and retirement.
const filters = ref({ Status: null as number | null, Contains: "" });
const list = usePagedList(getSlPriceLists, filters);
const canWrite = hasRole(SalesRoles.writers);
const canApprove = hasRole(SalesRoles.priceListApprovers);
const canSuggest = hasRole(SalesRoles.commercialReaders);

const form = ref<any>(null);
const busy = ref(false);
const markup = ref<number | null>(null);
const pickerKey = ref(0);

const isNew = computed(() => !form.value?.id);
const editable = computed(() => canWrite && (isNew.value || form.value?.status === 1));

function blank() {
  return { id: null, number: "", title: "", validFrom: todayIso(), validTo: null as string | null, note: "", status: 1, rowVersion: null, approvedAtText: "", lines: [] as any[] };
}
function fromDto(d: any) {
  return {
    id: d.id, number: d.number, title: d.title, validFrom: isoDay(d.validFrom), validTo: isoDay(d.validTo), note: d.note || "", status: d.status, rowVersion: d.rowVersion,
    approvedAtText: persianOf(d.approvedAtText, d.approvedAt), validFromText: d.validFromText, validToText: d.validToText,
    lines: (d.lines || []).map((l: any) => ({ itemId: l.itemId, itemCode: l.itemCode, itemName: l.itemName, unitPrice: l.unitPrice, minQuantity: l.minQuantity, suggest: null as any })),
  };
}

function create() {
  form.value = blank();
  markup.value = null;
}
async function open(id: number) {
  const [data, error] = await getSlPriceList(id);
  if (error) return fail(error);
  form.value = fromDto(camelize(data));
  markup.value = null;
}
function close() {
  form.value = null;
  list.load();
}

function addItem(item: any) {
  if (!item) return;
  pickerKey.value++;
  if (form.value.lines.some((l: any) => l.itemId === item.id && Number(l.minQuantity) === 0)) return fail("این کالا با حداقل تعداد صفر قبلاً در لیست آمده است.");
  form.value.lines.push({ itemId: item.id, itemCode: item.code, itemName: item.title, unitPrice: null, minQuantity: 0, suggest: null });
}
function removeLine(i: number) {
  form.value.lines.splice(i, 1);
}

async function suggest() {
  if (!form.value.lines.length) return fail("ابتدا کالاها را به لیست اضافه کنید.");
  if (markup.value === null || markup.value === undefined || (markup.value as any) === "") return fail("درصد سود را وارد کنید؛ سیستم درصدی را فرض نمی‌کند.");
  busy.value = true;
  const [data, error] = await suggestSlPrices({ itemIds: [...new Set<number>(form.value.lines.map((l: any) => l.itemId))], markupPercent: Number(markup.value) });
  busy.value = false;
  if (error) return fail(error);
  const byItem = new Map<number, any>((camelize(data) as any[]).map((s: any) => [s.itemId, s]));
  for (const l of form.value.lines) l.suggest = byItem.get(l.itemId) || null;
  ok("پیشنهاد قیمت آماده شد؛ هر ردیف را جداگانه بپذیرید.");
}
function accept(l: any) {
  if (l.suggest?.suggestedPrice == null) return;
  l.unitPrice = Math.round(Number(l.suggest.suggestedPrice));
  l.suggest = null;
}

async function save() {
  const f = form.value;
  if (!f.title.trim()) return fail("عنوان لیست قیمت را وارد کنید.");
  if (!f.lines.length) return fail("لیست قیمت حداقل یک ردیف لازم دارد.");
  busy.value = true;
  const [data, error] = await saveSlPriceList({
    id: f.id, title: f.title.trim(), validFrom: f.validFrom, validTo: f.validTo, note: f.note, rowVersion: f.rowVersion,
    lines: f.lines.map((l: any) => ({ itemId: l.itemId, unitPrice: Number(l.unitPrice) || 0, minQuantity: Number(l.minQuantity) || 0 })),
  });
  busy.value = false;
  if (error) return fail(error);
  form.value = fromDto(camelize(data));
  ok("لیست قیمت ذخیره شد.");
}

async function act(action: "approve" | "retire") {
  if (action === "retire" && !confirm("لیست قیمت بازنشسته شود؟ از این پس در قیمت‌گذاری استفاده نمی‌شود.")) return;
  busy.value = true;
  const [data, error] = await priceListAction({ id: form.value.id, action, rowVersion: form.value.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  form.value = fromDto(camelize(data));
  ok(action === "approve" ? "لیست قیمت تایید و فعال شد." : "لیست قیمت بازنشسته شد.");
}

onMounted(() => list.load());
</script>

<template>
  <div class="inv-page">
    <div v-if="!form" class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">لیست‌های قیمت</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="create">لیست قیمت جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا عنوان" @keyup.enter="list.search" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in priceListStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>عنوان</th><th>از تاریخ</th><th>تا تاریخ</th><th>تعداد ردیف</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">لیست قیمتی پیدا نشد.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td>
              <td>{{ r.title }}</td>
              <td>{{ persianOf(r.validFromText, r.validFrom) || "-" }}</td>
              <td>{{ persianOf(r.validToText, r.validTo) || "بدون پایان" }}</td>
              <td>{{ (r.lines || []).length }}</td>
              <td><span :class="badgeOfPriceList(r.status)" class="inv-badge">{{ priceListStatuses[r.status] }}</span></td>
              <td><button class="btn btn-sm btn-outline-primary" @click="open(r.id)">مشاهده</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-else class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">{{ isNew ? "لیست قیمت جدید" : "لیست قیمت " + form.number }}</h1>
        <span v-if="!isNew" :class="badgeOfPriceList(form.status)" class="inv-badge">{{ priceListStatuses[form.status] }}</span>
        <button class="btn btn-sm btn-outline-secondary ms-auto" @click="close">بازگشت به فهرست</button>
      </div>

      <div class="row g-2 mb-3">
        <div class="col-md-5"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" :disabled="!editable" class="form-control form-control-sm" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">شروع اعتبار</label><FnDate v-model="form.validFrom" :disabled="!editable" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">پایان اعتبار (اختیاری)</label><FnDate v-model="form.validTo" :disabled="!editable" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" :disabled="!editable" class="form-control form-control-sm" /></div>
      </div>
      <p v-if="form.approvedAtText" class="small inv-muted">تایید در {{ form.approvedAtText }}</p>

      <div v-if="editable" class="inv-filters mb-2">
        <LookupBox :key="pickerKey" :model-value="null" :fetch="searchItems" :label="itemLabel" placeholder="افزودن کالا" @pick="addItem" />
        <div v-if="canSuggest" class="d-flex gap-2 align-items-center">
          <label class="small mb-0 text-nowrap">درصد سود</label>
          <input v-model.number="markup" class="form-control form-control-sm inv-ltr" style="width: 90px" type="number" min="0" step="any" />
          <button :disabled="busy" class="btn btn-sm btn-outline-primary text-nowrap" @click="suggest">پیشنهاد قیمت بر پایه بها</button>
        </div>
      </div>

      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>#</th><th>کالا</th><th style="width: 190px">قیمت واحد (ریال)</th><th style="width: 140px">حداقل تعداد</th><th>پیشنهاد بر پایه بها</th><th v-if="editable"></th></tr></thead>
          <tbody>
            <tr v-if="!form.lines.length"><td :colspan="editable ? 6 : 5" class="text-center inv-muted">ردیفی ثبت نشده است.</td></tr>
            <tr v-for="(l, i) in form.lines" :key="i">
              <td>{{ i + 1 }}</td>
              <td><span class="inv-ltr">{{ l.itemCode }}</span> {{ l.itemName }}</td>
              <td><RialInput v-if="editable" v-model="l.unitPrice" /><span v-else>{{ rialText(l.unitPrice) }}</span></td>
              <td><input v-if="editable" v-model.number="l.minQuantity" class="form-control form-control-sm inv-ltr" type="number" min="0" step="any" /><span v-else>{{ qtyText(l.minQuantity) }}</span></td>
              <td>
                <template v-if="l.suggest">
                  <template v-if="l.suggest.suggestedPrice != null">
                    <b>{{ rialText(l.suggest.suggestedPrice) }}</b>
                    <span class="inv-muted small"> (بها {{ rialText(l.suggest.unitCost) }}، {{ costSources[l.suggest.costSource] }})</span>
                    <button v-if="editable" class="btn btn-sm btn-outline-success ms-2" @click="accept(l)">پذیرش</button>
                  </template>
                  <span v-else class="inv-muted small">{{ l.suggest.note || "بهای کالا مشخص نیست." }}</span>
                </template>
              </td>
              <td v-if="editable"><button class="btn btn-sm btn-outline-danger" @click="removeLine(i)">حذف</button></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="d-flex flex-wrap gap-2 mt-3">
        <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        <button v-if="!isNew && form.status === 1 && canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="act('approve')">{{ salesActionTitles.approve }} و فعال‌سازی</button>
        <button v-if="!isNew && form.status === 2 && canApprove" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('retire')">غیرفعال‌سازی ({{ salesActionTitles.retire }})</button>
      </div>
      <p v-if="!isNew && form.status === 1" class="small inv-muted mt-2">تایید لیست قیمت را فردی غیر از کسی که آن را ساخته یا ویرایش کرده انجام می‌دهد؛ ذخیرهٔ دوباره، تایید را از ویرایشگر می‌گیرد.</p>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
