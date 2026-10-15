<script setup lang="ts">
// Work queue of manufacturing steps: what is waiting for QC, in progress or rejected, across all parts.
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getOperations, getWorkOrders, qcOperation, rowsOf } from "@/api/InventoryAPIs";
import { fail, jalali, ok, operationStatuses, qcStatuses } from "@/composables/inventory";

const router = useRouter();
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const workOrders = ref<any[]>([]);
const filters = ref({ QcStatus: 1 as number | null, Status: null as number | null, WorkOrderId: null as number | null, Contains: "" });
const qc = ref<any>(null);

async function load() {
  const [data, error] = await getOperations({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

function search() {
  page.value = 1;
  load();
}

function open(op: any) {
  router.push({ name: "InventoryTrace", query: op.serialUnitId ? { serial: op.serialUnitId } : { lot: op.lotId } });
}

function printReport() {
  window.print();
}

async function saveQc() {
  const [, error] = await qcOperation({ id: qc.value.op.id, status: qc.value.status, note: qc.value.note });
  if (error) return fail(error);
  ok("ثبت شد.");
  qc.value = null;
  load();
}

onMounted(() => {
  getWorkOrders().then(([w]) => (workOrders.value = rowsOf(w)));
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="گزارش مراحل ساخت و کنترل کیفیت" :subtitle="filters.QcStatus !== null ? qcStatuses[filters.QcStatus] : 'همه وضعیت‌ها'" />
    <div class="inv-card">
      <div class="inv-toolbar screen-only"><h1 class="inv-title">کارتابل مراحل ساخت و QC</h1><button class="btn btn-sm btn-outline-secondary" @click="printReport">چاپ</button></div>
      <div class="inv-filters mb-3">
        <select v-model="filters.QcStatus" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌های QC</option>
          <option v-for="(t, k) in qcStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌های مرحله</option>
          <option v-for="(t, k) in operationStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.WorkOrderId" class="form-select form-select-sm" @change="search">
          <option :value="null">همه دستورهای ساخت</option>
          <option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
        </select>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="سریال، بچ، کالا یا مرحله" @keydown.enter="search" />
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>قطعه</th><th>سریال / بچ</th><th>مرحله</th><th>وضعیت</th><th>QC</th><th>پایان</th><th>اپراتور</th><th>نتیجه</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="9">موردی نیست.</td></tr>
            <tr v-for="op in rows" :key="op.id">
              <td>{{ op.itemTitle }} <span class="inv-muted inv-ltr">{{ op.itemCode }}</span></td>
              <td><a class="inv-ltr" href="#" @click.prevent="open(op)">{{ op.serialNumber || op.lotNumber }}</a></td>
              <td>{{ op.sequence }}. {{ op.title }}</td>
              <td>{{ operationStatuses[op.status] }}</td>
              <td>{{ qcStatuses[op.qcStatus] }}</td>
              <td>{{ jalali(op.finishedAt, true) }}</td>
              <td>{{ op.operatorName }}</td>
              <td>{{ op.result }}</td>
              <td><button v-if="op.status === 2 && op.qcStatus === 1" class="btn btn-sm btn-warning py-0" @click="qc = { op, status: 2, note: '' }">تصمیم QC</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="inv-pager">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--; load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }} ({{ total }} مرحله)</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++; load()">بعدی</button>
      </div>
    </div>

    <div v-if="qc" class="inv-modal-backdrop" @click.self="qc = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">تصمیم QC — {{ qc.op.title }} ({{ qc.op.serialNumber || qc.op.lotNumber }})</h2>
        <p v-if="qc.op.result" class="small">نتیجه ثبت‌شده: {{ qc.op.result }}</p>
        <div class="d-flex gap-3 mb-2">
          <div v-for="s in [2, 4, 3]" :key="s" class="form-check">
            <input :id="'q' + s" v-model="qc.status" :value="s" class="form-check-input" type="radio" />
            <label :for="'q' + s" class="form-check-label small">{{ qcStatuses[s] }}</label>
          </div>
        </div>
        <textarea v-model="qc.note" class="form-control form-control-sm" placeholder="توضیح (برای رد یا تایید مشروط الزامی است)" rows="3"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="qc = null">انصراف</button>
          <button class="btn btn-sm btn-primary" @click="saveQc">ثبت</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
