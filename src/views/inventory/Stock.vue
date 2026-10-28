<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getBalances, getExpiringLots, getGroups, getItemStocks, getWarehouses } from "@/api/InventoryAPIs";
import { fail, jalali, num, trackingModes } from "@/composables/inventory";

// "items": the total of each item; "detail": by warehouse, location and lot; "expiry": the lots on hand that are expired or about to expire (R1q D32)
const tab = ref<"items" | "detail" | "expiry">("items");
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const loading = ref(false);
const warehouses = ref<any[]>([]);
const groups = ref<any[]>([]);
const filters = ref({ Contains: "", WarehouseId: null as number | null, ItemGroupId: null as number | null, LowStockOnly: false });
// expiry tab: empty days = the company setting (quality settings, ExpiryWarnDays)
const expiry = ref<{ WithinDays: number | null; OnlyExpired: boolean }>({ WithinDays: null, OnlyExpired: false });

async function load() {
  loading.value = true;
  const params = { ...filters.value, PageNumber: page.value, PageSize: pageSize };
  const [data, error] =
    tab.value === "items"
      ? await getItemStocks(params)
      : tab.value === "expiry"
      ? await getExpiringLots({ WithinDays: expiry.value.WithinDays, OnlyExpired: expiry.value.OnlyExpired || null, PageNumber: page.value, PageSize: pageSize })
      : await getBalances(params);
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

const daysLeftText = (days: number) => (days < 0 ? `${num(-days)} روز از انقضا گذشته` : days === 0 ? "امروز منقضی می‌شود" : `${num(days)} روز مانده`);

const subtitle = computed(() => {
  if (tab.value === "expiry") {
    return ["بچ‌های منقضی یا نزدیک انقضا", expiry.value.OnlyExpired ? "فقط منقضی‌شده" : expiry.value.WithinDays !== null && expiry.value.WithinDays !== ("" as any) ? `تا ${expiry.value.WithinDays} روز آینده` : "تا مهلت هشدار تنظیمات کیفیت"].join(" · ");
  }
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

function show(t: "items" | "detail" | "expiry") {
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
          <button :class="tab === 'expiry' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="show('expiry')">انقضای بچ‌ها</button>
        </div>
        </div>
      </div>
      <div v-if="tab === 'expiry'" class="inv-filters mb-3">
        <div class="d-flex align-items-center gap-1">
          <label class="small text-nowrap" for="expiryDays">تا چند روز آینده</label>
          <input id="expiryDays" v-model.number="expiry.WithinDays" class="form-control form-control-sm inv-ltr" max="3650" min="0" placeholder="پیش‌فرض تنظیمات" style="width: 9rem" type="number" @keydown.enter="search" />
        </div>
        <div class="form-check">
          <input id="onlyExpired" v-model="expiry.OnlyExpired" class="form-check-input" type="checkbox" @change="search" />
          <label class="form-check-label small" for="onlyExpired">فقط منقضی‌شده</label>
        </div>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
        <span class="inv-muted small">بچ‌های دارای موجودی که تاریخ انقضایشان گذشته یا نزدیک است؛ کالای انبار ضایعات و اموال مشتری در فهرست نیست.</span>
      </div>
      <div v-else class="inv-filters mb-3">
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
        <table v-if="tab === 'expiry'" class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>شرح</th><th>بچ</th><th>شماره ذوب</th><th>تاریخ انقضا</th><th>وضعیت</th><th>موجودی</th><th>محل نگهداری</th><th>مجوز ارفاقی</th></tr></thead>
          <tbody>
            <tr v-if="loading"><td class="text-center" colspan="9">در حال بارگذاری...</td></tr>
            <tr v-else-if="!rows.length"><td class="text-center inv-muted" colspan="9">بچ منقضی یا نزدیک انقضا نیست.</td></tr>
            <tr v-for="r in rows" :key="r.lotId" :class="{ 'table-danger': r.expired && !r.concessionNcrNumber }">
              <td class="inv-ltr">{{ r.itemCode }}</td>
              <td>{{ r.itemTitle }}</td>
              <td><router-link :to="{ name: 'InventoryTrace', query: { lot: r.lotId } }" class="inv-ltr">{{ r.lotNumber }}</router-link></td>
              <td class="inv-ltr">{{ r.heatNumber }}</td>
              <td>{{ jalali(r.expiryDate) }}</td>
              <td>
                <span class="inv-badge" :class="r.expired ? 'bad' : 'warn'">{{ r.expired ? "منقضی" : "نزدیک انقضا" }}</span>
                <div class="small inv-muted">{{ daysLeftText(r.daysLeft) }}</div>
              </td>
              <td class="fw-bold">{{ num(r.onHand) }} <span class="fw-normal">{{ r.unit }}</span></td>
              <td class="small">{{ r.locations }}</td>
              <td class="inv-ltr">
                <span v-if="r.concessionNcrNumber" class="inv-badge info" title="استفاده پس از انقضا با این مجوز ارفاقی معتبر مجاز است">{{ r.concessionNcrNumber }}</span>
                <span v-else-if="r.expired" class="inv-muted">ندارد</span>
              </td>
            </tr>
          </tbody>
        </table>
        <table v-else-if="tab === 'items'" class="table table-sm table-hover">
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
