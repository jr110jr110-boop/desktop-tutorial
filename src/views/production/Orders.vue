<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import AwaitingInspection from "@/components/production/orders/AwaitingInspection.vue";
import DraftOrdersDialog from "@/components/production/orders/DraftOrdersDialog.vue";
import { orderIdOfOperation } from "@/components/production/orders/orderKit";
import { itemLabel, searchItems } from "@/components/production/master/masterKit";
import { getWorkOrders } from "@/api/InventoryAPIs";
import { getProductionOrders } from "@/api/ProductionAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize, usePagedList } from "@/composables/fn";
import { fail } from "@/composables/inventory";
import { badgeOfOrder, orderStatuses, persianOf, qtyText } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";

// Production orders (MfOrders). /production/operations/:id (the QC-request link) resolves the operation and opens its order.
const route = useRoute();
const router = useRouter();
const filters = ref({ LateOnly: null as boolean | null, Status: null as number | null, WorkOrderId: null as number | null, ItemId: null as number | null, OpenOnly: null as boolean | null, Contains: "" });
const tab = ref<"orders" | "qc">("orders");
const canInspect = hasRole(ProductionRoles.inspectors);
const item = ref<any>(null);
const list = usePagedList(getProductionOrders, filters);
const workOrders = ref<any[]>([]);
const drafting = ref(false);
const resolving = ref(false);
const canDraft = hasRole(ProductionRoles.planners);
const rows = computed(() => list.rows.value);

function pickItem(i: any) {
  item.value = i;
  filters.value.ItemId = i ? i.id : null;
}

onMounted(async () => {
  if (route.name === "MfOperation") {
    resolving.value = true;
    const orderId = await orderIdOfOperation(Number(route.params.id));
    resolving.value = false;
    if (orderId) return router.replace({ name: "MfOrder", params: { id: orderId } });
    fail("عملیات پیدا نشد؛ فهرست دستورهای تولید باز شد.");
  }
  const [w] = await getWorkOrders();
  workOrders.value = rowsOf(camelize(w));
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">دستورهای تولید</h1>
        <button v-if="canDraft" class="btn btn-sm btn-primary" @click="drafting = true">پیش‌نویس از دستور کار</button>
      </div>
      <p v-if="resolving" class="inv-muted">در حال یافتن عملیات...</p>
      <ul v-if="canInspect" class="nav nav-tabs mb-3">
        <li class="nav-item"><a :class="{ active: tab === 'orders' }" class="nav-link" href="#" @click.prevent="tab = 'orders'">دستورهای تولید</a></li>
        <li class="nav-item"><a :class="{ active: tab === 'qc' }" class="nav-link" href="#" @click.prevent="tab = 'qc'">در انتظار بازرسی</a></li>
      </ul>
      <AwaitingInspection v-if="canInspect && tab === 'qc'" />
      <template v-else>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا کالا" @keyup.enter="list.search" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in orderStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.WorkOrderId" class="form-select form-select-sm"><option :value="null">همه دستور کارها</option><option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option></select>
        <LookupBox :model-value="item" :fetch="searchItems" :label="itemLabel" placeholder="کالا" @pick="pickItem" />
        <label class="form-check mb-1"><input v-model="filters.OpenOnly" class="form-check-input" type="checkbox" :true-value="true" :false-value="null" /> <span class="form-check-label">فقط باز</span></label>
        <label class="form-check mb-1"><input v-model="filters.LateOnly" class="form-check-input" type="checkbox" :true-value="true" :false-value="null" /> <span class="form-check-label">فقط دیرکرد</span></label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>دستور کار</th><th>کالا</th><th>تعداد</th><th>نیاز</th><th>شروع برنامه</th><th>پایان برنامه</th><th>اولویت</th><th>پیشرفت</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="11" class="text-center inv-muted">دستور تولیدی پیدا نشد.</td></tr>
            <tr v-for="r in rows" :key="r.id">
              <td class="inv-ltr"><router-link :to="{ name: 'MfOrder', params: { id: r.id } }">{{ r.number }}</router-link></td>
              <td class="inv-ltr">{{ r.workOrderCode }}</td>
              <td><span class="inv-ltr">{{ r.itemCode }}</span> {{ r.itemTitle }}</td>
              <td>{{ qtyText(r.quantity) }} {{ r.unit }}</td>
              <td>{{ persianOf(r.needDateText, r.needDate) || "-" }}</td>
              <td>{{ persianOf(r.plannedStartText, r.plannedStart) || "-" }}</td>
              <td>{{ persianOf(r.plannedFinishText, r.plannedFinish) || "-" }}</td>
              <td>{{ r.priority ?? "-" }}</td>
              <td>{{ r.operationsDone }} از {{ r.operationCount }}</td>
              <td>
                <span :class="badgeOfOrder(r.status)" class="inv-badge">{{ orderStatuses[r.status] }}</span>
                <span v-if="r.isLate" class="inv-badge bad ms-1">{{ r.daysLate }} روز دیرکرد</span>
                <span v-if="r.kitShortage" class="inv-badge warn ms-1">کمبود کیت</span>
              </td>
              <td><router-link class="btn btn-sm btn-outline-primary" :to="{ name: 'MfOrder', params: { id: r.id } }">مشاهده</router-link></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      </template>
    </div>
    <DraftOrdersDialog v-if="drafting" @close="drafting = false" @done="drafting = false; list.load()" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
