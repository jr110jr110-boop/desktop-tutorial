<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { getSlWinLoss } from "@/api/SalesAPIs";
import { fail } from "@/composables/inventory";
import { percentText, rialText } from "@/composables/sales";

// Win / loss statistics by product type, customer and reason; optional range of the result date.
const from = ref<string | null>(null);
const to = ref<string | null>(null);
const d = ref<any>(null);
const loading = ref(false);

async function load() {
  loading.value = true;
  const [x, error] = await getSlWinLoss({ From: from.value, To: to.value });
  loading.value = false;
  if (error) return fail(error);
  d.value = x;
}
onMounted(load);
const groups = (): [string, string, any[]][] => [["بر اساس نوع محصول", "محصول", d.value?.byProduct || []], ["بر اساس مشتری", "مشتری", d.value?.byCustomer || []], ["بر اساس دلیل", "دلیل", d.value?.byReason || []]];
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <div><label class="form-label small mb-0">از تاریخ نتیجه</label><FnDate v-model="from" /></div>
      <div><label class="form-label small mb-0">تا تاریخ نتیجه</label><FnDate v-model="to" /></div>
      <button :disabled="loading" class="btn btn-sm btn-secondary align-self-end" @click="load">نمایش</button>
    </div>
    <template v-if="d">
      <div class="d-flex gap-3 mb-3">
        <div class="border rounded p-2"><div class="small text-muted">برد</div><div class="fs-4">{{ d.won }}</div></div>
        <div class="border rounded p-2"><div class="small text-muted">باخت</div><div class="fs-4">{{ d.lost }}</div></div>
        <div class="border rounded p-2"><div class="small text-muted">نرخ موفقیت</div><div class="fs-4">{{ d.hitRatePercent === null || d.hitRatePercent === undefined ? "-" : percentText(d.hitRatePercent) }}</div></div>
      </div>
      <div v-for="[title, head, rows] in groups()" :key="title" class="mb-3">
        <h2 class="inv-title mb-1">{{ title }}</h2>
        <table class="table table-sm table-hover">
          <thead><tr><th>{{ head }}</th><th>برد</th><th>باخت</th><th>نرخ موفقیت</th><th>ارزش بردها (ریال)</th><th>ارزش باخت‌ها (ریال)</th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="6" class="text-center inv-muted">داده‌ای نیست.</td></tr>
            <tr v-for="r in rows" :key="r.key"><td>{{ r.name || "-" }}</td><td>{{ r.won }}</td><td>{{ r.lost }}</td><td>{{ r.hitRatePercent === null || r.hitRatePercent === undefined ? "-" : percentText(r.hitRatePercent) }}</td><td class="inv-ltr">{{ rialText(r.wonValue) }}</td><td class="inv-ltr">{{ rialText(r.lostValue) }}</td></tr>
          </tbody>
        </table>
      </div>
    </template>
    <p v-else class="inv-muted">در حال بارگذاری...</p>
  </div>
</template>
