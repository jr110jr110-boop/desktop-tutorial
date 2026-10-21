<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { getAgeing } from "@/api/FnAPIs";
import { camelize, money } from "@/composables/fn";
import { fail, jalali, today } from "@/composables/inventory";

// Receivables ageing by customer: current, 31-60, 61-90 and over 90 days after the due date.
const asOf = ref<string | null>(today());
const data = ref<any>(null);
async function load() {
  const [d, error] = await getAgeing(asOf.value);
  if (error) return fail(error);
  data.value = camelize(d);
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">سنی مطالبات</h1></div>
      <div class="inv-filters mb-3">
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="asOf" /></div>
        <button class="btn btn-sm btn-secondary align-self-end" @click="load">نمایش</button>
      </div>
      <div v-if="data" class="table-responsive">
        <p class="small inv-muted">وضعیت تا {{ jalali(data.asOf) }}</p>
        <table class="table table-sm">
          <thead><tr><th>مشتری</th><th>جاری</th><th>۳۱ تا ۶۰ روز</th><th>۶۱ تا ۹۰ روز</th><th>بیش از ۹۰ روز</th><th>جمع (ریال)</th><th></th></tr></thead>
          <tbody>
            <tr v-for="r in data.rows" :key="r.customerId">
              <td>{{ r.customerName }}</td><td class="fn-num">{{ money(r.current) }}</td><td class="fn-num">{{ money(r.days31To60) }}</td><td class="fn-num">{{ money(r.days61To90) }}</td>
              <td class="fn-num"><span :class="r.over90 ? 'bad' : ''" class="inv-badge">{{ money(r.over90) }}</span></td><td class="fn-num">{{ money(r.total) }}</td>
              <td><router-link :to="{ name: 'FnCustomerStatement', params: { id: r.customerId } }" class="btn btn-sm btn-link p-0">صورت‌حساب</router-link></td>
            </tr>
            <tr v-if="!data.rows?.length"><td colspan="7" class="text-center inv-muted">مطالبه بازی نیست.</td></tr>
          </tbody>
          <tfoot><tr class="fn-total"><td>جمع کل</td><td class="fn-num">{{ money(data.current) }}</td><td class="fn-num">{{ money(data.days31To60) }}</td><td class="fn-num">{{ money(data.days61To90) }}</td><td class="fn-num">{{ money(data.over90) }}</td><td class="fn-num">{{ money(data.total) }}</td><td></td></tr></tfoot>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
