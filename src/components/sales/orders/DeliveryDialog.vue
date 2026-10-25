<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { approveDocument, confirmDocument, getWarehouses, saveDocument } from "@/api/InventoryAPIs";
import { todayIso } from "./salesKit";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { InventoryRoles, hasRole } from "@/composables/roles";
import { qtyText } from "@/composables/sales";

// Customer-delivery document of a sales order. The draft is created here with the fields the API takes (SalesOrderId and SalesOrderLineId per line), and the QC loading
// approval and the confirm are done here too; the inventory document page can also open a new delivery already linked to the order (link below). Items with lot or serial tracking need the lot / serial details the API asks for; the API message is shown if they are missing.
const props = defineProps<{ order: any }>();
const emit = defineEmits(["close", "changed"]);

const warehouses = ref<any[]>([]);
const warehouseId = ref<number | null>(null);
const description = ref("");
const busy = ref(false);
const created = ref<any>(null);
const canCreate = hasRole(InventoryRoles.documents);
const canQc = hasRole(InventoryRoles.approveQuality);

const rows = ref<any[]>(
  props.order.lines
    .map((l: any) => ({ id: l.id, itemId: l.itemId, itemCode: l.itemCode, itemName: l.itemName, unit: l.unit, remaining: Number(l.quantity) - Number(l.deliveredQuantity), quantity: Number(l.quantity) - Number(l.deliveredQuantity) }))
    .filter((r: any) => r.remaining > 0)
);
const total = computed(() => rows.value.reduce((s, r) => s + (Number(r.quantity) || 0), 0));

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = (w || []).filter((x: any) => x.isActive);
  if (warehouses.value.length === 1) warehouseId.value = warehouses.value[0].id;
});

async function create() {
  if (!warehouseId.value) return fail("انبار را انتخاب کنید.");
  const lines = rows.value.filter((r) => Number(r.quantity) > 0);
  if (!lines.length) return fail("تعداد تحویل حداقل یک ردیف را وارد کنید.");
  const over = lines.find((r) => Number(r.quantity) > r.remaining);
  if (over) return fail(`تعداد تحویل ${over.itemName} از مانده سفارش بیشتر است.`);
  busy.value = true;
  const [data, error] = await saveDocument({
    type: 11, documentDate: todayIso(), warehouseId: warehouseId.value, salesOrderId: props.order.id, description: description.value,
    lines: lines.map((r) => ({ itemId: r.itemId, quantity: Number(r.quantity), salesOrderLineId: r.id, direction: 0, serialNumbers: [] })),
  });
  busy.value = false;
  if (error) return fail(error);
  created.value = camelize(data);
  ok("پیش‌نویس سند تحویل به مشتری ساخته شد.");
}

async function qcApprove() {
  busy.value = true;
  const [data, error] = await approveDocument(created.value.id, 4);
  busy.value = false;
  if (error) return fail(error);
  created.value = camelize(data);
  ok("تایید بارگیری ثبت شد.");
}

async function confirm_() {
  if (!confirm("سند تحویل تایید شود؟ موجودی کم می‌شود و پیش‌نویس فاکتور ساخته می‌شود.")) return;
  busy.value = true;
  const [data, error] = await confirmDocument(created.value.id);
  busy.value = false;
  if (error) return fail(error);
  created.value = camelize(data);
  ok(`سند ${created.value.number} تایید شد.`);
  emit("changed");
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 760px">
      <h2 class="inv-title mb-2">سند تحویل به مشتری برای سفارش {{ order.number }}</h2>

      <template v-if="!created">
        <p v-if="!rows.length" class="inv-muted">همهٔ ردیف‌های سفارش تحویل شده است.</p>
        <template v-else>
          <div class="row g-2 mb-2">
            <div class="col-md-5"><label class="form-label small mb-0">انبار</label>
              <select v-model="warehouseId" class="form-select form-select-sm"><option :value="null">انتخاب کنید</option><option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.title }}</option></select>
            </div>
            <div class="col-md-7"><label class="form-label small mb-0">توضیح</label><input v-model="description" class="form-control form-control-sm" /></div>
          </div>
          <div class="table-responsive">
            <table class="table table-sm">
              <thead><tr><th>کالا</th><th>مانده سفارش</th><th style="width: 140px">تعداد این تحویل</th></tr></thead>
              <tbody>
                <tr v-for="r in rows" :key="r.id">
                  <td><span class="inv-ltr">{{ r.itemCode }}</span> {{ r.itemName }}</td>
                  <td>{{ qtyText(r.remaining) }} {{ r.unit }}</td>
                  <td><input v-model.number="r.quantity" class="form-control form-control-sm inv-ltr" type="number" min="0" :max="r.remaining" step="any" /></td>
                </tr>
              </tbody>
              <tfoot><tr><th colspan="2" class="text-end">جمع تعداد</th><th>{{ qtyText(total) }}</th></tr></tfoot>
            </table>
          </div>
          <p v-if="!canCreate" class="small text-danger">ساخت سند انبار نقش انباردار، سرپرست انبار یا PMO را لازم دارد.</p>
        </template>
        <p v-if="canCreate" class="small mt-2 mb-0">
          <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: 'new' }, query: { type: '11', salesOrder: String(order.id) } }">باز کردن در صفحه سند انبار</router-link>
          <span class="inv-muted"> (سند تحویل جدید با پیوند به این سفارش؛ ردیف‌ها را آنجا انتخاب کنید)</span>
        </p>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button>
          <button v-if="rows.length" :disabled="busy || !canCreate" class="btn btn-sm btn-primary" @click="create">ساخت پیش‌نویس سند تحویل</button>
        </div>
      </template>

      <template v-else>
        <p class="mb-1">سند تحویل <b class="inv-ltr">{{ created.number || "(پیش‌نویس)" }}</b> ساخته شد.</p>
        <p class="small">
          مراحل بعد: تایید بارگیری توسط کنترل کیفیت
          <span class="inv-badge" :class="{ ok: created.qcLoadingApprovedAt }">{{ created.qcLoadingApprovedAt ? "ثبت شد" : "ثبت نشده" }}</span>
          و سپس تایید سند. پس از تایید، مانده و رزرو سفارش به‌روز و پیش‌نویس فاکتور ساخته می‌شود.
        </p>
        <div v-if="created.status === 0" class="d-flex flex-wrap gap-2">
          <button v-if="!created.qcLoadingApprovedAt && canQc" :disabled="busy" class="btn btn-sm btn-outline-success" @click="qcApprove">تایید بارگیری (QC)</button>
          <button v-if="canCreate" :disabled="busy" class="btn btn-sm btn-success" @click="confirm_">تایید سند تحویل</button>
        </div>
        <p v-else class="text-success">سند تایید شد.</p>
        <p class="small mt-2">
          <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: created.id } }">باز کردن در صفحه سند انبار</router-link>
        </p>
        <div class="d-flex justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close'); emit('changed')">بستن</button></div>
      </template>
    </div>
  </div>
</template>
