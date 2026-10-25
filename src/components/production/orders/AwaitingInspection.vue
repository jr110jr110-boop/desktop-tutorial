<script setup lang="ts">
import { onMounted, ref } from "vue";
import QcDecisionDialog from "./QcDecisionDialog.vue";
import QPager from "@/components/quality/QPager.vue";
import { getProductionOperations, getProductionOrder } from "@/api/ProductionAPIs";
import { camelize, usePagedList } from "@/composables/fn";
import { fail } from "@/composables/inventory";
import { inspectionRequirements, persianOf } from "@/composables/production";

// Operations that wait for inspection (status 5, QC hold). Each row opens its order; the QC inspector can decide right here.
const filters = ref({ Status: 5 });
const list = usePagedList(getProductionOperations, filters, 50);
const deciding = ref<null | { operation: any; order: any }>(null);
const busy = ref(false);

async function decide(op: any) {
  busy.value = true;
  const [d, error] = await getProductionOrder(op.orderId);
  busy.value = false;
  if (error) return fail(error);
  deciding.value = { operation: op, order: camelize(d) };
}
function done() {
  deciding.value = null;
  list.load();
}
onMounted(() => list.load());
</script>

<template>
  <div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>برگه کار</th><th>دستور تولید</th><th>مرحله</th><th>مرکز کار</th><th>نوع بازرسی</th><th>زمان درخواست</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">عملیاتی در انتظار بازرسی نیست.</td></tr>
          <tr v-for="o in list.rows.value" :key="o.id">
            <td class="inv-ltr">{{ o.jobCard }}</td>
            <td class="inv-ltr"><router-link :to="{ name: 'MfOrder', params: { id: o.orderId } }">{{ o.orderNumber }}</router-link></td>
            <td>{{ o.sequence }} - {{ o.title }}</td>
            <td>{{ o.workCenterTitle || o.workCenterCode }}</td>
            <td>{{ inspectionRequirements[o.inspectionRequirement] }}</td>
            <td>{{ persianOf(o.qcRequestedAtText, o.qcRequestedAt, true) || "-" }}</td>
            <td class="text-nowrap">
              <router-link class="btn btn-sm btn-outline-secondary me-1" :to="{ name: 'MfOrder', params: { id: o.orderId } }">دستور تولید</router-link>
              <button :disabled="busy" class="btn btn-sm btn-primary" @click="decide(o)">تصمیم بازرسی</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
    <QcDecisionDialog v-if="deciding" :operation="deciding.operation" :quantity="deciding.order.quantity" :work-order-id="deciding.order.workOrderId" @close="deciding = null" @done="done" />
  </div>
</template>
