<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { computed } from "vue";
import { getItem, getKardex, getWarehouses } from "@/api/InventoryAPIs";
import { documentTypes, fail, jalali, num } from "@/composables/inventory";

const route = useRoute();
const item = ref<any>(null);
const warehouses = ref<any[]>([]);
const filters = ref({ WarehouseId: null as number | null, From: "", To: "" });
const kardex = ref<any>(null);
const loading = ref(false);

const subtitle = computed(() => {
  if (!kardex.value) return "";
  const parts = [`${kardex.value.itemCode} - ${kardex.value.itemTitle}`];
  const w = warehouses.value.find((x) => x.id === filters.value.WarehouseId);
  parts.push(w ? "انبار: " + w.title : "همه انبارها");
  if (filters.value.From) parts.push("از " + jalali(filters.value.From));
  if (filters.value.To) parts.push("تا " + jalali(filters.value.To));
  return parts.join(" · ");
});

function printPage() {
  window.print();
}

async function load() {
  if (!item.value) return;
  loading.value = true;
  const [data, error] = await getKardex({ ItemId: item.value.id, ...filters.value });
  loading.value = false;
  if (error) return fail(error);
  kardex.value = data;
}

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = w || [];
  const id = Number(route.query.itemId);
  if (id) {
    const [data] = await getItem(id);
    if (data) {
      item.value = data;
      load();
    }
  }
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :subtitle="subtitle" title="کاردکس کالا" />
    <div class="inv-card">
      <div class="inv-toolbar screen-only"><h1 class="inv-title">کاردکس کالا</h1></div>
      <div class="inv-filters mb-3">
        <div style="min-width: 320px"><ItemPicker v-model="item" :active-only="false" @select="load" /></div>
        <select v-model="filters.WarehouseId" class="form-select form-select-sm" @change="load">
          <option :value="null">همه انبارها</option>
          <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.title }}</option>
        </select>
        <div><label class="form-label small mb-0">از</label><input v-model="filters.From" class="form-control form-control-sm" type="date" @change="load" /></div>
        <div><label class="form-label small mb-0">تا</label><input v-model="filters.To" class="form-control form-control-sm" type="date" @change="load" /></div>
        <button class="btn btn-sm btn-outline-secondary d-print-none" @click="printPage">چاپ</button>
      </div>

      <div v-if="loading" class="text-center">در حال بارگذاری...</div>
      <template v-else-if="kardex">
        <p class="small mb-2">
          <span class="inv-ltr">{{ kardex.itemCode }}</span> — {{ kardex.itemTitle }} ·
          مانده اول دوره: <b>{{ num(kardex.openingBalance) }}</b> · مانده پایان: <b>{{ num(kardex.closingBalance) }}</b> {{ kardex.unit }}
        </p>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>تاریخ</th><th>سند</th><th>نوع</th><th>انبار</th><th>محل</th><th>بچ</th><th>سریال</th><th>وارده</th><th>صادره</th><th>مانده</th><th>فی</th></tr></thead>
            <tbody>
              <tr v-if="!kardex.rows.length"><td class="text-center inv-muted" colspan="11">گردشی در این بازه نیست.</td></tr>
              <tr v-for="(r, i) in kardex.rows" :key="i" :class="{ 'text-danger': r.isReversal }">
                <td>{{ jalali(r.date) }}</td>
                <td><router-link :to="{ name: 'InventoryDocumentEdit', params: { id: r.documentId } }" class="inv-ltr">{{ r.documentNumber }}</router-link></td>
                <td>{{ documentTypes[r.documentType] }}<span v-if="r.isReversal"> (ابطال)</span></td>
                <td>{{ r.warehouseTitle }}</td>
                <td class="inv-ltr">{{ r.locationCode }}</td>
                <td class="inv-ltr">{{ r.lotNumber }}</td>
                <td class="inv-ltr">{{ r.serialNumber }}</td>
                <td>{{ r.in ? num(r.in) : "" }}</td>
                <td>{{ r.out ? num(r.out) : "" }}</td>
                <td class="fw-bold">{{ num(r.balance) }}</td>
                <td>{{ num(r.unitPrice) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
      <div v-else class="inv-muted">کالا را انتخاب کنید.</div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
