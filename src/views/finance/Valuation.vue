<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getValuation } from "@/api/FinanceAPIs";
import { fail, num, printPage, rial } from "@/composables/inventory";
import { roleTitle } from "@/composables/finance";

// Stock valuation at the moving average cost, and its reconciliation with the balance of the accounting events.
const data = ref<any>(null);
const contains = ref("");
const loading = ref(false);
const limit = ref(300);

async function load() {
  loading.value = true;
  const [result, error] = await getValuation(contains.value);
  loading.value = false;
  if (error) return fail(error);
  data.value = result;
  limit.value = 300;
}

const shown = computed(() => (data.value?.items || []).slice(0, limit.value));
const difference = computed(() => (data.value ? Number(data.value.totalValue) - Number(data.value.totalEventsBalance) : 0));

onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="ارزش‌گذاری موجودی انبار" subtitle="میانگین موزون متحرک، مبالغ به ریال" />
    <div v-if="data" class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">ارزش‌گذاری موجودی و مغایرت‌گیری با حسابداری</h1>
        <div class="d-flex gap-2 d-print-none">
          <input v-model="contains" class="form-control form-control-sm" placeholder="کد یا نام کالا" @keydown.enter="load" />
          <button class="btn btn-sm btn-secondary" @click="load">جستجو</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <div class="d-flex flex-wrap gap-2 mb-3">
        <span class="inv-badge">ارزش موجودی انبار: {{ rial(data.totalValue) }}</span>
        <span class="inv-badge">مانده رویدادهای حسابداری: {{ rial(data.totalEventsBalance) }}</span>
        <span class="inv-badge" :class="Math.abs(difference) < 1 ? 'ok' : 'bad'">اختلاف: {{ rial(difference) }}</span>
      </div>

      <h2 class="inv-title mb-2">مغایرت‌گیری انبار و حسابداری</h2>
      <table class="table table-sm table-bordered mb-4">
        <thead>
          <tr><th>حساب</th><th>ارزش در انبار</th><th>مانده رویدادها</th><th>اختلاف</th></tr>
        </thead>
        <tbody>
          <tr v-if="!data.reconciliation.length"><td colspan="4" class="text-center inv-muted">ردیفی نیست.</td></tr>
          <tr v-for="r in data.reconciliation" :key="r.accountRole" :class="{ 'table-danger': Math.abs(r.difference) >= 1 }">
            <td>{{ roleTitle(r.accountRole) }} <span class="inv-ltr inv-muted">{{ r.accountRole }}</span></td>
            <td class="num">{{ rial(r.stockValue) }}</td>
            <td class="num">{{ rial(r.eventsBalance) }}</td>
            <td class="num fw-bold">{{ rial(r.difference) }}</td>
          </tr>
        </tbody>
      </table>

      <h2 class="inv-title mb-2">ارزش هر کالا</h2>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr><th>کد</th><th>شرح</th><th>واحد</th><th>موجودی</th><th>میانگین بها</th><th>ارزش</th><th>آخرین قیمت خرید</th></tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td colspan="7" class="text-center">در حال بارگذاری...</td></tr>
            <tr v-for="i in shown" :key="i.itemId">
              <td class="inv-ltr">{{ i.itemCode }}</td>
              <td>{{ i.itemTitle }}</td>
              <td>{{ i.unit }}</td>
              <td class="num">{{ num(i.quantity) }}</td>
              <td class="num">{{ rial(i.averageCost) }}</td>
              <td class="num">{{ rial(i.value) }}</td>
              <td class="num">{{ rial(i.lastPurchasePrice) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <button v-if="data.items.length > limit" class="btn btn-sm btn-outline-secondary d-print-none" @click="limit += 300">
        نمایش بیشتر ({{ data.items.length - limit }} کالا)
      </button>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
