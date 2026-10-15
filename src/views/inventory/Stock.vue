<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getBalances, getGroups, getItemStocks, getWarehouses } from "@/api/InventoryAPIs";
import { fail, num, trackingModes } from "@/composables/inventory";

const tab = ref<"items" | "detail">("items");
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const loading = ref(false);
const warehouses = ref<any[]>([]);
const groups = ref<any[]>([]);
const filters = ref({ Contains: "", WarehouseId: null as number | null, ItemGroupId: null as number | null, LowStockOnly: false });

async function load() {
  loading.value = true;
  const params = { ...filters.value, PageNumber: page.value, PageSize: pageSize };
  const [data, error] = tab.value === "items" ? await getItemStocks(params) : await getBalances(params);
  loading.value = false;
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

function search() {
  page.value = 1;
  load();
}

const subtitle = computed(() => {
  const parts = [tab.value === "items" ? "جمع هر کالا" : "به تفکیک انبار، محل و بچ"];
  const w = warehouses.value.find((x) => x.id === filters.value.WarehouseId);
  if (w) parts.push("انبار: " + w.title);
  const g = groups.value.find((x) => x.id === filters.value.ItemGroupId);
  if (g) parts.push("گروه: " + g.title);
  if (filters.value.LowStockOnly) parts.push("فقط زیر نقطه سفارش");
  return parts.join(" · ");
});

function printReport() {
  window.print();
}

function show(t: "items" | "detail") {
  tab.value = t;
  search();
}

onMounted(async () => {
  const [[w], [g]] = await Promise.all([getWarehouses(), getGroups()]);
  warehouses.value = w || [];
  groups.value = g || [];
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :subtitle="subtitle" title="گزارش موجودی انبار" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">موجودی انبار</h1>
        <div class="d-flex gap-2 d-print-none">
        <button class="btn btn-sm btn-outline-secondary" @click="printReport">چاپ</button>
        <div class="btn-group btn-group-sm">
          <button :class="tab === 'items' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="show('items')">جمع هر کالا</button>
          <button :class="tab === 'detail' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="show('detail')">به تفکیک انبار، محل و بچ</button>
        </div>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا شرح کالا" @keydown.enter="search" />
        <select v-model="filters.WarehouseId" class="form-select form-select-sm" @change="search">
          <option :value="null">همه انبارها</option>
          <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.title }}</option>
        </select>
        <select v-model="filters.ItemGroupId" class="form-select form-select-sm" @change="search">
          <option :value="null">همه گروه‌ها</option>
          <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option>
        </select>
        <div v-if="tab === 'items'" class="form-check">
          <input id="low" v-model="filters.LowStockOnly" class="form-check-input" type="checkbox" @change="search" />
          <label class="form-check-label small" for="low">فقط زیر نقطه سفارش</label>
        </div>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>

      <div class="table-responsive">
        <table v-if="tab === 'items'" class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>شرح</th><th>گروه</th><th>ردیابی</th><th>موجودی</th><th>واحد</th><th>نقطه سفارش</th><th></th></tr></thead>
          <tbody>
            <tr v-if="loading"><td class="text-center" colspan="8">در حال بارگذاری...</td></tr>
            <tr v-else-if="!rows.length"><td class="text-center inv-muted" colspan="8">موردی نیست.</td></tr>
            <tr v-for="r in rows" :key="r.itemId" :class="{ 'table-danger': r.isLow }">
              <td class="inv-ltr">{{ r.itemCode }}</td>
              <td>{{ r.itemTitle }}</td>
              <td>{{ r.itemGroupTitle }}</td>
              <td>{{ trackingModes[r.trackingMode] }}</td>
              <td class="fw-bold">{{ num(r.onHand) }}</td>
              <td>{{ r.unit }}</td>
              <td>{{ num(r.minStock) }}</td>
              <td><router-link :to="{ name: 'InventoryKardex', query: { itemId: r.itemId } }" class="btn btn-sm btn-link p-0">کاردکس</router-link></td>
            </tr>
          </tbody>
        </table>
        <table v-else class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>شرح</th><th>انبار</th><th>محل</th><th>بچ</th><th>شماره ذوب</th><th>مقدار</th><th>واحد</th></tr></thead>
          <tbody>
            <tr v-if="loading"><td class="text-center" colspan="8">در حال بارگذاری...</td></tr>
            <tr v-else-if="!rows.length"><td class="text-center inv-muted" colspan="8">موردی نیست.</td></tr>
            <tr v-for="(r, i) in rows" :key="i">
              <td class="inv-ltr">{{ r.itemCode }}</td>
              <td>{{ r.itemTitle }}</td>
              <td>{{ r.warehouseTitle }}</td>
              <td class="inv-ltr">{{ r.locationCode }}</td>
              <td>
                <router-link v-if="r.lotId" :to="{ name: 'InventoryTrace', query: { lot: r.lotId } }" class="inv-ltr">{{ r.lotNumber }}</router-link>
              </td>
              <td class="inv-ltr">{{ r.heatNumber }}</td>
              <td class="fw-bold">{{ num(r.quantity) }}</td>
              <td>{{ r.unit }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="inv-pager">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--; load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }} ({{ total }} ردیف)</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++; load()">بعدی</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
