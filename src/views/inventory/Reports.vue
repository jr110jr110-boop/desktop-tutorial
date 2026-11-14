<script setup lang="ts">
import FnDate from "@/components/fn/FnDate.vue";
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { downloadWarehouseReport, getGroups, getReportCatalog, getWarehouseReport, getWarehouses } from "@/api/InventoryAPIs";
import { fail, jalali, num, ok, rial, warehouseKinds } from "@/composables/inventory";

// PB5: the warehouse reports (one catalogue, filters, print and Excel export). Money columns are removed by the API for roles that do not see prices.
const route = useRoute();
const catalog = ref<any[]>([]);
const warehouses = ref<any[]>([]);
const groups = ref<any[]>([]);
const report = ref<any>(null);
const loading = ref(false);
const filters = ref<any>({
  Report: "",
  DateFrom: "",
  DateTo: "",
  WarehouseId: null,
  WorkOrderId: null,
  ItemId: null,
  ItemGroupId: null,
  ProviderId: null,
  ContractId: null,
  Platform: "",
  EquipmentType: "",
  ValveModel: "",
  Contains: "",
  RequestNumber: "",
  Kind: null,
  Bucket: null,
});

const current = computed(() => catalog.value.find((c) => c.key === filters.value.Report));
const uses = (name: string) => !!current.value && current.value.filters.includes(name);

async function load() {
  if (!filters.value.Report) return;
  loading.value = true;
  const [data, error] = await getWarehouseReport(filters.value);
  loading.value = false;
  if (error) {
    report.value = null;
    return fail(error);
  }
  report.value = data;
}

async function toExcel() {
  const message = await downloadWarehouseReport(filters.value);
  if (message) fail(message);
  else ok("فایل Excel ذخیره شد.");
}

function pick(key: string) {
  filters.value.Report = key;
  report.value = null;
  load();
}

function cell(column: any, row: any) {
  const v = row[column.key];
  if (v === null || v === undefined) return "";
  if (column.type === "money") return rial(v);
  if (column.type === "number") return num(v);
  if (column.type === "date") return jalali(v);
  return String(v);
}

function printNow() {
  window.print();
}

const alignEnd = (column: any) => column.type === "money" || column.type === "number";

onMounted(async () => {
  const [[c], [w], [g]] = await Promise.all([getReportCatalog(), getWarehouses(), getGroups()]);
  catalog.value = c || [];
  warehouses.value = w || [];
  groups.value = g || [];
  const wanted = String(route.query.report || "");
  if (wanted && catalog.value.some((x) => x.key === wanted)) pick(wanted);
  else if (catalog.value.length) filters.value.Report = catalog.value[0].key;
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :subtitle="report?.filters || ''" :title="report?.title || 'گزارش‌های انبار'" />
    <div class="inv-card">
      <div class="inv-toolbar d-print-none">
        <h1 class="inv-title">گزارش‌های انبار</h1>
        <div class="d-flex gap-2">
          <button :disabled="!report" class="btn btn-sm btn-outline-secondary" @click="toExcel">خروجی Excel</button>
          <button :disabled="!report" class="btn btn-sm btn-outline-secondary" @click="printNow">چاپ</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="filters.Report" class="form-select form-select-sm" @change="pick(filters.Report)">
          <option v-for="c in catalog" :key="c.key" :value="c.key">{{ c.title }}</option>
        </select>
        <template v-if="uses('date')">
          <FnDate v-model="filters.DateFrom" placeholder="از تاریخ" />
          <FnDate v-model="filters.DateTo" placeholder="تا تاریخ" />
        </template>
        <select v-if="uses('warehouse')" v-model="filters.WarehouseId" class="form-select form-select-sm">
          <option :value="null">همه انبارها</option>
          <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.title }}</option>
        </select>
        <select v-if="uses('kind')" v-model="filters.Kind" class="form-select form-select-sm">
          <option :value="null">همه نوع انبار</option>
          <option v-for="(t, k) in warehouseKinds" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-if="uses('project')" v-model.number="filters.WorkOrderId" class="form-control form-control-sm inv-ltr" placeholder="شناسه دستور کار" type="number" />
        <select v-if="uses('group')" v-model="filters.ItemGroupId" class="form-select form-select-sm">
          <option :value="null">همه گروه‌ها</option>
          <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option>
        </select>
        <input v-if="uses('item')" v-model.number="filters.ItemId" class="form-control form-control-sm inv-ltr" placeholder="شناسه کالا" type="number" />
        <input v-if="uses('provider')" v-model.number="filters.ProviderId" class="form-control form-control-sm inv-ltr" placeholder="شناسه تامین‌کننده" type="number" />
        <input v-if="uses('contract')" v-model.number="filters.ContractId" class="form-control form-control-sm inv-ltr" placeholder="شناسه قرارداد" type="number" />
        <template v-if="uses('platform')">
          <input v-model="filters.Platform" class="form-control form-control-sm" placeholder="سکو" />
          <input v-model="filters.EquipmentType" class="form-control form-control-sm" placeholder="نوع تجهیز" />
          <input v-model="filters.ValveModel" class="form-control form-control-sm" placeholder="مدل شیر" />
        </template>
        <input v-if="uses('request')" v-model="filters.RequestNumber" class="form-control form-control-sm inv-ltr" placeholder="شماره درخواست (PR-... یا MR-...)" />
        <select v-if="uses('bucket')" v-model="filters.Bucket" class="form-select form-select-sm">
          <option :value="null">همه رده‌های راکد</option>
          <option :value="1">کندگردش (۶۰ روز)</option>
          <option :value="2">راکد (۹۰ روز)</option>
          <option :value="3">راکد مرده (۱۸۰ روز)</option>
        </select>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا شرح" @keydown.enter="load" />
        <button class="btn btn-sm btn-secondary" @click="load">نمایش</button>
      </div>

      <p v-if="report?.pricesMasked" class="inv-muted small">ستون‌های مبلغ برای نقش شما نمایش داده نمی‌شود.</p>
      <p v-if="report?.note" class="inv-muted small">{{ report.note }}</p>
      <div v-if="loading" class="inv-muted">در حال بارگذاری...</div>
      <div v-else-if="report" class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr><th v-for="c in report.columns" :key="c.key">{{ c.title }}</th></tr>
          </thead>
          <tbody>
            <tr v-for="(row, i) in report.rows" :key="i">
              <td v-for="c in report.columns" :key="c.key" :class="{ 'text-end inv-ltr': alignEnd(c) }">{{ cell(c, row) }}</td>
            </tr>
            <tr v-if="!report.rows.length"><td :colspan="report.columns.length" class="inv-muted text-center">موردی نیست.</td></tr>
          </tbody>
          <tfoot v-if="report.totals && Object.keys(report.totals).length">
            <tr class="fw-bold">
              <td v-for="(c, i) in report.columns" :key="c.key" :class="{ 'text-end inv-ltr': alignEnd(c) }">
                <template v-if="i === 0">جمع</template>
                <template v-else-if="report.totals[c.key] !== undefined">{{ c.type === 'money' ? rial(report.totals[c.key]) : num(report.totals[c.key]) }}</template>
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
      <div v-if="report" class="inv-muted small mt-2">تولید شده در {{ jalali(report.generatedAt, true) }}</div>
    </div>
  </div>
</template>

<style lang="scss">
/* L7: A4 print of a warehouse report, B Zar, header from KsReportHeader (see report.scss); wide tables shrink instead of overflowing. */
@media print {
  @page { size: A4 landscape; margin: 12mm; }
  .inv-page.ks-report { font-family: "B Zar", "BZar", Tahoma, sans-serif; direction: rtl; }
  .inv-page.ks-report table { width: 100%; font-size: 9pt; }
  .inv-page.ks-report .inv-card { border: 0; box-shadow: none; padding: 0; }
}
</style>
