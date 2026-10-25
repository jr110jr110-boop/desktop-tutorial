<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import OrderDetail from "@/components/sales/orders/OrderDetail.vue";
import OrderEditor from "@/components/sales/orders/OrderEditor.vue";
import { customerLabel, searchCustomers } from "@/components/sales/orders/salesKit";
import { getSlOrder, getSlOrders } from "@/api/SalesAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail } from "@/composables/inventory";
import { SalesRoles, hasRole } from "@/composables/roles";
import { badgeOfSalesOrder, orderStatuses, persianOf, rialText } from "@/composables/sales";

// Sales orders (SlOrders). /sales/orders/:id (also the link of the inbox items) opens the order detail.
const route = useRoute();
const router = useRouter();
const filters = ref({ Status: null as number | null, CustomerId: null as number | null, CreditPending: null as boolean | null, Contains: "" });
const customer = ref<any>(null);
const list = usePagedList(getSlOrders, filters);
const canWrite = hasRole(SalesRoles.writers);
const order = ref<any>(null);
const creating = ref(false);
const rows = computed(() => list.rows.value);

function pickCustomer(c: any) {
  customer.value = c;
  filters.value.CustomerId = c ? c.id : null;
}

async function loadOrder() {
  const id = Number(route.params.id);
  if (!id) {
    order.value = null;
    return;
  }
  const [data, error] = await getSlOrder(id);
  if (error) {
    fail(error);
    order.value = null;
    return;
  }
  order.value = camelize(data);
}

function onCreated(o: any) {
  creating.value = false;
  router.push({ name: "SlOrder", params: { id: o.id } });
}

watch(() => route.params.id, async () => {
  await loadOrder();
  if (!route.params.id) list.load();
});
onMounted(async () => {
  if (route.params.id) await loadOrder();
  else list.load();
});
</script>

<template>
  <div class="inv-page">
    <OrderDetail v-if="route.params.id && order" :order="order" @update="order = $event" @reload="loadOrder" />
    <p v-else-if="route.params.id" class="inv-muted">در حال بارگذاری سفارش...</p>

    <OrderEditor v-else-if="creating" :order="null" @saved="onCreated" @close="creating = false" />

    <div v-else class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">سفارش‌های فروش</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="creating = true">سفارش فروش جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره سفارش" @keyup.enter="list.search" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in orderStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <LookupBox :model-value="customer" :fetch="searchCustomers" :label="customerLabel" placeholder="مشتری" @pick="pickCustomer" />
        <label class="form-check mb-1"><input v-model="filters.CreditPending" class="form-check-input" type="checkbox" :true-value="true" :false-value="null" /> <span class="form-check-label">منتظر تایید سقف اعتبار</span></label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تاریخ</th><th>مشتری</th><th>شماره سفارش مشتری</th><th>تحویل درخواستی</th><th>مبلغ خالص (ریال)</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="8" class="text-center inv-muted">سفارش فروشی پیدا نشد.</td></tr>
            <tr v-for="r in rows" :key="r.id">
              <td class="inv-ltr"><router-link :to="{ name: 'SlOrder', params: { id: r.id } }">{{ r.number }}</router-link></td>
              <td>{{ persianOf(r.orderDateText, r.orderDate) }}</td>
              <td>{{ r.customerName }}</td>
              <td class="inv-ltr">{{ r.customerOrderNumber || "-" }}</td>
              <td>{{ persianOf(r.requestedDeliveryDateText, r.requestedDeliveryDate) || "-" }}</td>
              <td>{{ rialText(r.netAmount) }}</td>
              <td>
                <span :class="badgeOfSalesOrder(r.status)" class="inv-badge">{{ orderStatuses[r.status] }}</span>
                <span v-if="r.status === 1 && r.creditApprovalRequired && !r.creditApprovedAt" class="inv-badge bad ms-1">منتظر سقف اعتبار</span>
              </td>
              <td><router-link class="btn btn-sm btn-outline-primary" :to="{ name: 'SlOrder', params: { id: r.id } }">مشاهده</router-link></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
