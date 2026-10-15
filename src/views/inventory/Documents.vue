<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getDocuments, getWarehouses } from "@/api/InventoryAPIs";
import { documentStatuses, documentTypes, fail, jalali, num, sepidarStatuses } from "@/composables/inventory";

const router = useRouter();
const docs = ref<any[]>([]);
const warehouses = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 30;
const loading = ref(false);
const filters = ref({ Contains: "", Type: null as number | null, Status: null as number | null, WarehouseId: null as number | null, From: "", To: "" });

async function load() {
  loading.value = true;
  const [data, error] = await getDocuments({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
  loading.value = false;
  if (error) return fail(error);
  docs.value = data.data;
  total.value = data.totalItems;
}

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

function search() {
  page.value = 1;
  load();
}

function open(d: any) {
  router.push({ name: "InventoryDocumentEdit", params: { id: d.id } });
}

function create(type: number) {
  router.push({ name: "InventoryDocumentEdit", params: { id: "new" }, query: { type } });
}

function printReport() {
  window.print();
}

function statusClass(s: number) {
  return s === 1 ? "ok" : s === 2 ? "bad" : "warn";
}

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = w || [];
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فهرست اسناد انبار" :subtitle="filters.Type ? documentTypes[filters.Type] : 'همه انواع'" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">اسناد انبار (رسید، حواله، انتقال)</h1>
        <div class="dropdown">
          <div class="d-flex flex-wrap gap-1 d-print-none">
            <button class="btn btn-sm btn-outline-secondary" @click="printReport">چاپ فهرست</button>
            <button v-for="(t, k) in documentTypes" :key="k" class="btn btn-sm btn-outline-primary" @click="create(Number(k))">+ {{ t }}</button>
          </div>
        </div>
      </div>

      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره سند، شماره مرجع، توضیح" @keydown.enter="search" />
        <select v-model="filters.Type" class="form-select form-select-sm" @change="search">
          <option :value="null">همه انواع</option>
          <option v-for="(t, k) in documentTypes" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in documentStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.WarehouseId" class="form-select form-select-sm" @change="search">
          <option :value="null">همه انبارها</option>
          <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.title }}</option>
        </select>
        <div><label class="form-label small mb-0">از تاریخ</label><input v-model="filters.From" class="form-control form-control-sm" type="date" @change="search" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><input v-model="filters.To" class="form-control form-control-sm" type="date" @change="search" /></div>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th>شماره</th><th>نوع</th><th>تاریخ</th><th>انبار</th><th>طرف حساب / دستور کار</th><th>مرجع</th>
              <th>ردیف</th><th>مقدار کل</th><th>وضعیت</th><th>سپیدار</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td class="text-center" colspan="10">در حال بارگذاری...</td></tr>
            <tr v-else-if="!docs.length"><td class="text-center inv-muted" colspan="10">سندی پیدا نشد.</td></tr>
            <tr v-for="d in docs" :key="d.id" role="button" @click="open(d)">
              <td class="inv-ltr">{{ d.number || "(پیش‌نویس)" }}</td>
              <td>{{ documentTypes[d.type] }}</td>
              <td>{{ jalali(d.documentDate) }}</td>
              <td>
                {{ d.warehouseTitle }}
                <span v-if="d.destinationWarehouseTitle"> ← {{ d.destinationWarehouseTitle }}</span>
              </td>
              <td>{{ d.providerName || d.workOrderCode || d.contractCode }}</td>
              <td>{{ d.referenceNumber }}</td>
              <td>{{ d.linesCount }}</td>
              <td>{{ num(d.totalQuantity) }}</td>
              <td><span class="inv-badge" :class="statusClass(d.status)">{{ documentStatuses[d.status] }}</span></td>
              <td><span class="inv-badge" :class="{ warn: d.sepidarStatus === 1, ok: d.sepidarStatus >= 2 && d.sepidarStatus <= 3, bad: d.sepidarStatus === 4 }">{{ sepidarStatuses[d.sepidarStatus] }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="inv-pager">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--; load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }} ({{ total }} سند)</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++; load()">بعدی</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
