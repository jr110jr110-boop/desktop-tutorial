<script setup lang="ts">
import { computed, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import RialInput from "./RialInput.vue";
import { customerLabel, isoDay, lineAmount, searchCustomers, todayIso } from "./salesKit";
import { itemLabel, searchItems } from "@/components/production/master/masterKit";
import { saveSlOrder } from "@/api/SalesAPIs";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { rialText } from "@/composables/sales";

// Draft sales order editor (new order or a draft opened for edit). An empty unit price makes the server use the approved price list.
const props = defineProps<{ order: any | null }>();
const emit = defineEmits(["saved", "close"]);

const o = props.order;
const form = ref<any>({
  id: o?.id ?? null,
  customer: o ? { id: o.customerId, name: o.customerName } : null,
  contactId: o?.contactId ?? null,
  orderDate: o ? isoDay(o.orderDate) : todayIso(),
  customerOrderNumber: o?.customerOrderNumber || "",
  requestedDeliveryDate: o ? isoDay(o.requestedDeliveryDate) : null,
  rowVersion: o?.rowVersion ?? null,
  lines: (o?.lines || []).map((l: any) => ({ itemId: l.itemId, itemCode: l.itemCode, itemName: l.itemName, unit: l.unit, description: l.description || "", quantity: l.quantity, unitPrice: l.unitPrice, discountPercent: l.discountPercent })),
});
const busy = ref(false);
const pickerKey = ref(0);
const total = computed(() => form.value.lines.reduce((s: number, l: any) => s + lineAmount(l), 0));
/** An order made from a quotation keeps its customer (the server refuses a change). */
const customerLocked = !!o?.quotationId;

function addItem(item: any) {
  if (!item) return;
  pickerKey.value++;
  form.value.lines.push({ itemId: item.id, itemCode: item.code, itemName: item.title, unit: item.unit, description: "", quantity: 1, unitPrice: null, discountPercent: 0 });
}

async function save() {
  const f = form.value;
  if (!f.customer) return fail("مشتری را انتخاب کنید.");
  if (!f.lines.length) return fail("حداقل یک ردیف لازم است.");
  busy.value = true;
  const [data, error] = await saveSlOrder({
    id: f.id, orderDate: f.orderDate, customerId: f.customer.id, contactId: f.contactId, customerOrderNumber: f.customerOrderNumber, requestedDeliveryDate: f.requestedDeliveryDate, rowVersion: f.rowVersion,
    lines: f.lines.map((l: any) => ({ itemId: l.itemId, description: l.description, quantity: Number(l.quantity) || 0, unitPrice: l.unitPrice === null || l.unitPrice === undefined ? null : Number(l.unitPrice), discountPercent: Number(l.discountPercent) || 0 })),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("سفارش فروش ذخیره شد.");
  emit("saved", camelize(data));
}
</script>

<template>
  <div class="inv-card">
    <div class="inv-toolbar">
      <h1 class="inv-title">{{ form.id ? "ویرایش سفارش فروش " + (order?.number || "") : "سفارش فروش جدید" }}</h1>
      <button class="btn btn-sm btn-outline-secondary ms-auto" @click="emit('close')">انصراف</button>
    </div>
    <div class="row g-2 mb-3">
      <div class="col-md-4"><label class="form-label small mb-0">مشتری</label><LookupBox v-model="form.customer" :disabled="customerLocked" :fetch="searchCustomers" :label="customerLabel" placeholder="مشتری" /></div>
      <div class="col-md-2"><label class="form-label small mb-0">تاریخ سفارش</label><FnDate v-model="form.orderDate" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">شماره سفارش مشتری</label><input v-model="form.customerOrderNumber" class="form-control form-control-sm" /></div>
      <div class="col-md-3"><label class="form-label small mb-0">تاریخ تحویل درخواستی</label><FnDate v-model="form.requestedDeliveryDate" /></div>
    </div>
    <div class="mb-2" style="max-width: 420px"><LookupBox :key="pickerKey" :model-value="null" :fetch="searchItems" :label="itemLabel" placeholder="افزودن کالا" @pick="addItem" /></div>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>#</th><th>کالا</th><th>شرح</th><th style="width: 110px">تعداد</th><th style="width: 180px">قیمت واحد (ریال)</th><th style="width: 100px">تخفیف (٪)</th><th>مبلغ (ریال)</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!form.lines.length"><td colspan="8" class="text-center inv-muted">ردیفی ثبت نشده است.</td></tr>
          <tr v-for="(l, i) in form.lines" :key="i">
            <td>{{ i + 1 }}</td>
            <td><span class="inv-ltr">{{ l.itemCode }}</span> {{ l.itemName }}</td>
            <td><input v-model="l.description" class="form-control form-control-sm" /></td>
            <td><input v-model.number="l.quantity" class="form-control form-control-sm inv-ltr" type="number" min="0" step="any" /></td>
            <td><RialInput v-model="l.unitPrice" placeholder="از لیست قیمت" /></td>
            <td><input v-model.number="l.discountPercent" class="form-control form-control-sm inv-ltr" type="number" min="0" max="100" step="any" /></td>
            <td>{{ l.unitPrice == null ? "از لیست قیمت" : rialText(lineAmount(l)) }}</td>
            <td><button class="btn btn-sm btn-outline-danger" @click="form.lines.splice(i, 1)">حذف</button></td>
          </tr>
        </tbody>
        <tfoot><tr><th colspan="6" class="text-end">جمع ردیف‌های قیمت‌دار (ریال)</th><th>{{ rialText(total) }}</th><th></th></tr></tfoot>
      </table>
    </div>
    <button :disabled="busy" class="btn btn-sm btn-primary mt-2" @click="save">ذخیره</button>
  </div>
</template>
