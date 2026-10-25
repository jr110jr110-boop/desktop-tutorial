<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import RialInput from "@/components/sales/orders/RialInput.vue";
import { customerLabel, isoDay, lineAmount, loadApprovedPriceLists, priceFromLists, searchCustomers, todayIso } from "@/components/sales/orders/salesKit";
import { itemLabel, searchItems } from "@/components/production/master/masterKit";
import { getSlQuotation, getSlQuotations, quotationAction, saveSlQuotation, slQuotationToOrder } from "@/api/SalesAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { SalesRoles, hasRole } from "@/composables/roles";
import { badgeOfQuotation, persianOf, qtyText, quotationStatuses, rialText, salesActionTitles } from "@/composables/sales";

// Light quotations (SlQuotations) for spare parts and services. Unit prices are prefilled from the approved price lists valid on the quotation date.
const filters = ref({ Status: null as number | null, CustomerId: null as number | null, Contains: "" });
const filterCustomer = ref<any>(null);
const list = usePagedList(getSlQuotations, filters);
const canWrite = hasRole(SalesRoles.writers);

const form = ref<any>(null);
const busy = ref(false);
const pickerKey = ref(0);
const priceLists = ref<any[]>([]);
const orderDlg = ref<{ customerOrderNumber: string; requestedDeliveryDate: string | null } | null>(null);

const isNew = computed(() => !form.value?.id);
const editable = computed(() => canWrite && (isNew.value || form.value?.status === 1));
const total = computed(() => (form.value ? form.value.lines.reduce((s: number, l: any) => s + lineAmount(l), 0) : 0));
const pastValidity = computed(() => !!form.value?.validUntil && form.value.validUntil < todayIso());
/** Price lists offered in the selector: approved ones (the server uses any valid approved list when none is chosen). */
const choosable = computed(() => priceLists.value);

function pickFilterCustomer(c: any) {
  filterCustomer.value = c;
  filters.value.CustomerId = c ? c.id : null;
}

function blank() {
  return { id: null, number: "", status: 1, customer: null as any, contactId: null, quotationDate: todayIso(), validUntil: null as string | null, priceListId: null as number | null, note: "", salesOrderId: null as number | null, rowVersion: null, lines: [] as any[] };
}
function fromDto(d: any) {
  return {
    id: d.id, number: d.number, status: d.status, customer: { id: d.customerId, name: d.customerName }, contactId: d.contactId, quotationDate: isoDay(d.quotationDate), validUntil: isoDay(d.validUntil),
    priceListId: d.priceListId, note: d.note || "", salesOrderId: d.salesOrderId, rowVersion: d.rowVersion, netAmount: d.netAmount,
    lines: (d.lines || []).map((l: any) => ({ itemId: l.itemId, itemCode: l.itemCode, itemName: l.itemName, unit: l.unit, description: l.description || "", quantity: l.quantity, unitPrice: l.unitPrice, discountPercent: l.discountPercent, fromList: "" })),
  };
}

function create() {
  form.value = blank();
}
async function open(id: number) {
  const [data, error] = await getSlQuotation(id);
  if (error) return fail(error);
  form.value = fromDto(camelize(data));
}
function close() {
  form.value = null;
  list.load();
}

/** Fills the unit price of a line from the price lists (the user may overwrite it). */
function reprice(l: any) {
  const hit = priceFromLists(priceLists.value, l.itemId, Number(l.quantity) || 0, form.value.quotationDate || todayIso(), form.value.priceListId);
  if (hit) {
    l.unitPrice = hit.price;
    l.fromList = hit.priceListNumber;
  } else l.fromList = "";
}
function addItem(item: any) {
  if (!item) return;
  pickerKey.value++;
  const l = { itemId: item.id, itemCode: item.code, itemName: item.title, unit: item.unit, description: "", quantity: 1, unitPrice: null as number | null, discountPercent: 0, fromList: "" };
  reprice(l);
  form.value.lines.push(l);
}
function repriceAll() {
  form.value.lines.forEach(reprice);
}
function removeLine(i: number) {
  form.value.lines.splice(i, 1);
}

async function save() {
  const f = form.value;
  if (!f.customer) return fail("مشتری را انتخاب کنید.");
  if (!f.validUntil) return fail("تاریخ پایان اعتبار را وارد کنید.");
  if (!f.lines.length) return fail("حداقل یک ردیف لازم است.");
  busy.value = true;
  const [data, error] = await saveSlQuotation({
    id: f.id, quotationDate: f.quotationDate, customerId: f.customer.id, contactId: f.contactId, validUntil: f.validUntil, priceListId: f.priceListId, note: f.note, rowVersion: f.rowVersion,
    lines: f.lines.map((l: any) => ({ itemId: l.itemId, description: l.description, quantity: Number(l.quantity) || 0, unitPrice: l.unitPrice === null || l.unitPrice === undefined ? null : Number(l.unitPrice), discountPercent: Number(l.discountPercent) || 0 })),
  });
  busy.value = false;
  if (error) return fail(error);
  form.value = fromDto(camelize(data));
  ok("پیشنهاد قیمت ذخیره شد.");
}

async function act(action: "send" | "accept" | "reject" | "expire" | "cancel") {
  if ((action === "cancel" || action === "reject") && !confirm(`${salesActionTitles[action]}؟`)) return;
  busy.value = true;
  const [data, error] = await quotationAction({ id: form.value.id, action, rowVersion: form.value.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  form.value = fromDto(camelize(data));
  ok("انجام شد.");
}

async function toOrder() {
  const d = orderDlg.value!;
  busy.value = true;
  const [data, error] = await slQuotationToOrder({ quotationId: form.value.id, customerOrderNumber: d.customerOrderNumber.trim() || null, requestedDeliveryDate: d.requestedDeliveryDate });
  busy.value = false;
  if (error) return fail(error);
  const order = camelize(data) as any;
  orderDlg.value = null;
  ok(`سفارش فروش ${order.number} ساخته شد.`);
  await open(form.value.id);
}

onMounted(async () => {
  list.load();
  if (canWrite) priceLists.value = await loadApprovedPriceLists();
});
</script>

<template>
  <div class="inv-page">
    <div v-if="!form" class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">پیش‌فاکتورها</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="create">پیش‌فاکتور جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره" @keyup.enter="list.search" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in quotationStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <LookupBox :model-value="filterCustomer" :fetch="searchCustomers" :label="customerLabel" placeholder="مشتری" @pick="pickFilterCustomer" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تاریخ</th><th>مشتری</th><th>اعتبار تا</th><th>مبلغ خالص (ریال)</th><th>وضعیت</th><th>سفارش فروش</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">پیش‌فاکتوری پیدا نشد.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td>
              <td>{{ persianOf(r.quotationDateText, r.quotationDate) }}</td>
              <td>{{ r.customerName }}</td>
              <td>{{ persianOf(r.validUntilText, r.validUntil) }}</td>
              <td>{{ rialText(r.netAmount) }}</td>
              <td><span :class="badgeOfQuotation(r.status)" class="inv-badge">{{ quotationStatuses[r.status] }}</span></td>
              <td><router-link v-if="r.salesOrderId" :to="{ name: 'SlOrder', params: { id: r.salesOrderId } }">مشاهده سفارش</router-link><span v-else class="inv-muted">-</span></td>
              <td><button class="btn btn-sm btn-outline-primary" @click="open(r.id)">مشاهده</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-else class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">{{ isNew ? "پیش‌فاکتور جدید" : "پیش‌فاکتور " + form.number }}</h1>
        <span v-if="!isNew" :class="badgeOfQuotation(form.status)" class="inv-badge">{{ quotationStatuses[form.status] }}</span>
        <button class="btn btn-sm btn-outline-secondary ms-auto" @click="close">بازگشت به فهرست</button>
      </div>

      <div class="row g-2 mb-3">
        <div class="col-md-4"><label class="form-label small mb-0">مشتری</label><LookupBox v-model="form.customer" :disabled="!editable" :fetch="searchCustomers" :label="customerLabel" placeholder="مشتری" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="form.quotationDate" :disabled="!editable" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">اعتبار تا</label><FnDate v-model="form.validUntil" :disabled="!editable" /></div>
        <div class="col-md-4">
          <label class="form-label small mb-0">لیست قیمت</label>
          <select v-model="form.priceListId" :disabled="!editable" class="form-select form-select-sm" @change="repriceAll">
            <option :value="null">هر لیست تاییدشدهٔ معتبر</option>
            <option v-for="p in choosable" :key="p.id" :value="p.id">{{ p.number }} - {{ p.title }}</option>
          </select>
        </div>
        <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" :disabled="!editable" class="form-control form-control-sm" /></div>
      </div>

      <div v-if="editable" class="inv-filters mb-2">
        <LookupBox :key="pickerKey" :model-value="null" :fetch="searchItems" :label="itemLabel" placeholder="افزودن کالا یا خدمت" @pick="addItem" />
        <button class="btn btn-sm btn-outline-secondary text-nowrap" @click="repriceAll">قیمت‌گذاری دوباره از لیست قیمت</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>#</th><th>کالا</th><th>شرح</th><th style="width: 110px">تعداد</th><th style="width: 180px">قیمت واحد (ریال)</th><th style="width: 100px">تخفیف (٪)</th><th>مبلغ (ریال)</th><th v-if="editable"></th></tr></thead>
          <tbody>
            <tr v-if="!form.lines.length"><td :colspan="editable ? 8 : 7" class="text-center inv-muted">ردیفی ثبت نشده است.</td></tr>
            <tr v-for="(l, i) in form.lines" :key="i">
              <td>{{ i + 1 }}</td>
              <td><span class="inv-ltr">{{ l.itemCode }}</span> {{ l.itemName }}</td>
              <td><input v-if="editable" v-model="l.description" class="form-control form-control-sm" /><span v-else>{{ l.description }}</span></td>
              <td><input v-if="editable" v-model.number="l.quantity" class="form-control form-control-sm inv-ltr" type="number" min="0" step="any" @change="reprice(l)" /><span v-else>{{ qtyText(l.quantity) }} {{ l.unit }}</span></td>
              <td>
                <RialInput v-if="editable" v-model="l.unitPrice" />
                <span v-else>{{ rialText(l.unitPrice) }}</span>
                <div v-if="l.fromList" class="small inv-muted">از لیست {{ l.fromList }}</div>
              </td>
              <td><input v-if="editable" v-model.number="l.discountPercent" class="form-control form-control-sm inv-ltr" type="number" min="0" max="100" step="any" /><span v-else>{{ qtyText(l.discountPercent) }}</span></td>
              <td>{{ rialText(lineAmount(l)) }}</td>
              <td v-if="editable"><button class="btn btn-sm btn-outline-danger" @click="removeLine(i)">حذف</button></td>
            </tr>
          </tbody>
          <tfoot><tr><th :colspan="6" class="text-end">جمع خالص (ریال)</th><th>{{ rialText(total) }}</th><th v-if="editable"></th></tr></tfoot>
        </table>
      </div>

      <div class="d-flex flex-wrap gap-2 mt-3 align-items-center">
        <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        <template v-if="!isNew && canWrite">
          <button v-if="form.status === 1" :disabled="busy" class="btn btn-sm btn-success" @click="act('send')">{{ salesActionTitles.send }}</button>
          <button v-if="form.status === 2" :disabled="busy" class="btn btn-sm btn-success" @click="act('accept')">{{ salesActionTitles.accept }}</button>
          <button v-if="form.status === 2" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('reject')">{{ salesActionTitles.reject }}</button>
          <button v-if="form.status === 2 && pastValidity" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act('expire')">{{ salesActionTitles.expire }}</button>
          <button v-if="form.status === 1 || form.status === 2" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('cancel')">{{ salesActionTitles.cancel }}</button>
          <button v-if="form.status === 3 && !form.salesOrderId" :disabled="busy" class="btn btn-sm btn-primary" @click="orderDlg = { customerOrderNumber: '', requestedDeliveryDate: null }">تبدیل به سفارش فروش</button>
        </template>
        <router-link v-if="form.salesOrderId" class="btn btn-sm btn-outline-primary" :to="{ name: 'SlOrder', params: { id: form.salesOrderId } }">مشاهده سفارش فروش</router-link>
      </div>
    </div>

    <div v-if="orderDlg" class="inv-modal-backdrop" @click.self="orderDlg = null">
      <div class="inv-modal" style="max-width: 460px">
        <h2 class="inv-title mb-2">تبدیل به سفارش فروش</h2>
        <label class="form-label small mb-0">شماره سفارش مشتری (اختیاری)</label>
        <input v-model="orderDlg.customerOrderNumber" class="form-control form-control-sm mb-2" />
        <label class="form-label small mb-0">تاریخ تحویل درخواستی (اختیاری)</label>
        <FnDate v-model="orderDlg.requestedDeliveryDate" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="orderDlg = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="toOrder">ساخت سفارش</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
