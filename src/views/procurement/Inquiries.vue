<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { getInquiries } from "@/api/ProcurementAPIs";
import { fail, jalali } from "@/composables/inventory";
import { inquiryStatuses } from "@/composables/procurement";

const router = useRouter();
const rows = ref<any[]>([]);

onMounted(async () => {
  const [data, error] = await getInquiries();
  if (error) return fail(error);
  rows.value = data || [];
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">استعلام‌ها</h1>
        <router-link :to="{ name: 'ProcurementRequests' }" class="btn btn-sm btn-outline-secondary">درخواست‌های خرید</router-link>
      </div>
      <p class="inv-muted">استعلام جدید از صفحه یک درخواست خرید تاییدشده ساخته می‌شود.</p>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>تاریخ</th><th>درخواست خرید</th><th>پیشنهادها</th><th>وضعیت</th><th>سفارش خرید</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="6" class="text-center inv-muted">استعلامی نیست.</td></tr>
          <tr v-for="q in rows" :key="q.id" role="button" @click="router.push({ name: 'ProcurementInquiryEdit', params: { id: q.id } })">
            <td class="inv-ltr">{{ q.number }}</td>
            <td>{{ jalali(q.inquiryDate) }}</td>
            <td class="inv-ltr">{{ q.purchaseRequestNumber }}</td>
            <td>{{ q.quotes.length }} از {{ q.requiredQuotes }}</td>
            <td><span class="inv-badge" :class="{ ok: q.status === 2, warn: q.status <= 1, bad: q.status === 9 }">{{ inquiryStatuses[q.status] }}</span></td>
            <td class="inv-ltr">{{ q.purchaseOrderNumber }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
