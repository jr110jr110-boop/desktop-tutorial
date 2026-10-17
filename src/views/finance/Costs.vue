<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getWorkOrderCosts } from "@/api/FinanceAPIs";
import { getContracts, rowsOf } from "@/api/InventoryAPIs";
import { fail, printPage, rial } from "@/composables/inventory";

// Cost of every work order and project: material, subcontract, services, labour, overhead.
const rows = ref<any[]>([]);
const contracts = ref<any[]>([]);
const contractId = ref<number | null>(null);
const loading = ref(false);

async function load() {
  loading.value = true;
  const [data, error] = await getWorkOrderCosts(contractId.value);
  loading.value = false;
  if (error) return fail(error);
  rows.value = data || [];
}

const columns = ["material", "subcontract", "services", "labour", "overhead", "total"] as const;
const totals = computed(() => {
  const t: Record<string, number> = {};
  columns.forEach((c) => (t[c] = rows.value.reduce((s, r) => s + (Number(r[c]) || 0), 0)));
  return t;
});

onMounted(async () => {
  getContracts().then(([list]) => (contracts.value = rowsOf(list)));
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="بهای تمام‌شده دستورهای ساخت و پروژه‌ها" subtitle="مبالغ به ریال" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">بهای تمام‌شده دستورهای ساخت و پروژه‌ها</h1>
        <div class="d-flex gap-2 d-print-none">
          <select v-model="contractId" class="form-select form-select-sm" style="min-width: 260px" @change="load">
            <option :value="null">همه قراردادها</option>
            <option v-for="c in contracts" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option>
          </select>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th>قرارداد / پروژه</th>
              <th>دستور ساخت</th>
              <th>مرکز هزینه</th>
              <th>مواد</th>
              <th>پیمانکاری</th>
              <th>خدمات</th>
              <th>دستمزد</th>
              <th>سربار</th>
              <th>جمع</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td colspan="9" class="text-center">در حال بارگذاری...</td></tr>
            <tr v-else-if="!rows.length"><td colspan="9" class="text-center inv-muted">هزینه‌ای ثبت نشده است.</td></tr>
            <tr v-for="(r, i) in rows" :key="i">
              <td>
                <span class="inv-ltr">{{ r.contractCode }}</span> {{ r.contractTitle }}
              </td>
              <td>
                <span class="inv-ltr">{{ r.workOrderCode }}</span> {{ r.workOrderTitle }}
              </td>
              <td class="inv-ltr">{{ r.costCenter }}</td>
              <td class="num">{{ rial(r.material) }}</td>
              <td class="num">{{ rial(r.subcontract) }}</td>
              <td class="num">{{ rial(r.services) }}</td>
              <td class="num">{{ rial(r.labour) }}</td>
              <td class="num">{{ rial(r.overhead) }}</td>
              <td class="num fw-bold">{{ rial(r.total) }}</td>
            </tr>
          </tbody>
          <tfoot v-if="rows.length">
            <tr class="fw-bold">
              <td colspan="3">جمع کل</td>
              <td v-for="c in columns" :key="c" class="num">{{ rial(totals[c]) }}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
