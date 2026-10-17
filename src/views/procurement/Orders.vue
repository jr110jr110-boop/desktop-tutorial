<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getProviders, rowsOf } from "@/api/InventoryAPIs";
import { getPurchaseOrders } from "@/api/ProcurementAPIs";
import { fail, jalali, printPage, rial } from "@/composables/inventory";
import { orderStatuses, purchaseKinds } from "@/composables/procurement";
import { hasRole, ProcurementRoles } from "@/composables/roles";

const router = useRouter();
const rows = ref<any[]>([]);
const providers = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const filters = ref({ Status: null as number | null, ProviderId: null as number | null, Contains: "" });
const canCreate = hasRole(ProcurementRoles.commercial);
const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

async function load() {
  const [data, error] = await getPurchaseOrders({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

function search() {
  page.value = 1;
  load();
}

onMounted(() => {
  getProviders().then(([l]) => (providers.value = rowsOf(l)));
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فهرست سفارش‌های خرید" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">سفارش‌های خرید</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canCreate" class="btn btn-sm btn-primary" @click="router.push({ name: 'ProcurementOrderEdit', params: { id: 'new' } })">سفارش خرید جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره سفارش" @keydown.enter="search" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in orderStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.ProviderId" class="form-select form-select-sm" @change="search">
          <option :value="null">همه تامین‌کنندگان</option>
          <option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead>
          <tr><th>شماره</th><th>تاریخ</th><th>تامین‌کننده</th><th>نوع</th><th>جمع (ریال)</th><th>دریافت‌شده</th><th>فاکتور شده</th><th>پرداخت شده</th><th>وضعیت</th></tr>
        </thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">سفارشی نیست.</td></tr>
          <tr v-for="o in rows" :key="o.id" role="button" @click="router.push({ name: 'ProcurementOrderEdit', params: { id: o.id } })">
            <td class="inv-ltr">{{ o.number }}</td>
            <td>{{ jalali(o.orderDate) }}</td>
            <td>{{ o.providerName }}</td>
            <td>{{ purchaseKinds[o.kind] }}</td>
            <td class="num">{{ rial(o.totalAmount) }}</td>
            <td class="num">{{ rial(o.receivedAmount) }}</td>
            <td class="num">{{ rial(o.invoicedAmount) }}</td>
            <td class="num">{{ rial(o.paidAmount) }}</td>
            <td><span class="inv-badge" :class="{ ok: o.status >= 1 && o.status <= 5, warn: o.status === 0, bad: o.status === 9 }">{{ orderStatuses[o.status] }}</span></td>
          </tr>
        </tbody>
      </table>
      <div class="d-flex gap-2 align-items-center d-print-none">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--, load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }}</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++, load()">بعدی</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
