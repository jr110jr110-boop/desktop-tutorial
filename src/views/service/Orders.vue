<script setup lang="ts">
// Service orders (SvOrders): field / workshop orders with status and warranty decision; a new order (also from a request: ?requestId=) opens its page.
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import OrderEditor from "@/components/service/OrderEditor.vue";
import { getSvOrders } from "@/api/ServiceAPIs";
import { usePagedList } from "@/composables/fn";
import { hasRole, ServiceRoles } from "@/composables/roles";
import { badgeOfDecision, badgeOfServiceOrder, orderKinds, orderStatuses, warrantyDecisions } from "@/composables/service";

const route = useRoute();
const router = useRouter();
const canWrite = hasRole(ServiceRoles.writers);
const filters = ref<any>({ Status: null, Decision: null, Contains: "" });
const list = usePagedList(getSvOrders, computed(() => filters.value) as any);
const creating = ref(!!route.query.requestId && canWrite);
const requestId = Number(route.query.requestId) || null;
const unitId = Number(route.query.unitId) || null;

function saved(o: any) {
  creating.value = false;
  if (o?.id) router.push("/service/orders/" + o.id);
  else list.load();
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">دستورهای خدمات</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="creating = true">دستور جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <div><label class="form-label small mb-0">وضعیت</label>
          <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in orderStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">تصمیم گارانتی</label>
          <select v-model="filters.Decision" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in warrantyDecisions" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">شماره یا عنوان</label><input v-model="filters.Contains" class="form-control form-control-sm" @keyup.enter="list.search" /></div>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>عنوان</th><th>مشتری</th><th>واحد</th><th>نوع</th><th>برنامه</th><th>مهندسان</th><th>وضعیت</th><th>تصمیم</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="10" class="text-center inv-muted">دستوری نیست.</td></tr>
            <tr v-for="o in list.rows.value" :key="o.id">
              <td class="inv-ltr">{{ o.number }}</td><td>{{ o.title }}</td><td>{{ o.customerName }}</td><td class="inv-ltr">{{ o.unitTag }}</td><td>{{ orderKinds[o.kind] }}</td>
              <td>{{ o.plannedStartText || "-" }} <span v-if="o.plannedEndText">تا {{ o.plannedEndText }}</span></td>
              <td>{{ (o.engineers || []).length }}</td>
              <td><span :class="badgeOfServiceOrder(o.status)" class="inv-badge">{{ orderStatuses[o.status] }}</span><span v-if="o.ltsaNumber" class="inv-badge info ms-1">LTSA</span></td>
              <td><span :class="badgeOfDecision(o.decision)" class="inv-badge">{{ warrantyDecisions[o.decision] }}</span><span v-if="o.goodwillRequested && o.decision !== 3" class="inv-badge warn ms-1">منتظر مدیرعامل</span></td>
              <td><button class="btn btn-sm btn-outline-primary" @click="router.push('/service/orders/' + o.id)">باز کردن</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
    </div>
    <OrderEditor v-if="creating" :request-id="requestId" :unit-id="unitId" @cancel="creating = false" @saved="saved" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
