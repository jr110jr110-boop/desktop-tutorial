<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getRequests, rowsOf } from "@/api/EngChangeAPIs";
import { fail, jalali, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { EcRoles, ecClasses, ecUrgencies, ecrStatuses, statusClass } from "@/composables/engChange";

// Engineering change requests (ECR): why, what is affected, class and urgency. An engineering manager (not the originator) decides; the order (ECO) is made from an approved request.
const router = useRouter();
const rows = ref<any[]>([]);
const status = ref<number | null>(null);
const text = ref("");
const part = ref("");
const canRaise = hasRole(EcRoles.raisers);

async function load() {
  const [data, error] = await getRequests({ Status: status.value, Contains: text.value, PartNumber: part.value });
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="درخواست‌های تغییر مهندسی (ECR)" :subtitle="status === null ? 'همه وضعیت‌ها' : ecrStatuses[status]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">درخواست‌های تغییر مهندسی (ECR)</h1>
        <div class="d-flex gap-2 d-print-none">
          <router-link :to="{ name: 'EngChangeOrders' }" class="btn btn-sm btn-outline-secondary">دستورهای تغییر (ECO)</router-link>
          <router-link :to="{ name: 'EngChangeHistory' }" class="btn btn-sm btn-outline-secondary">تاریخچه تغییر قطعه</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canRaise" class="btn btn-sm btn-primary" @click="router.push({ name: 'EngChangeRequestEdit', params: { id: 'new' } })">+ درخواست جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="status" class="form-select form-select-sm" @change="load">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in ecrStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="text" class="form-control form-control-sm" placeholder="شماره یا عنوان" @keyup.enter="load" />
        <input v-model="part" class="form-control form-control-sm inv-ltr" placeholder="شماره قطعه" @keyup.enter="load" />
        <button class="btn btn-sm btn-outline-secondary" @click="load">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>عنوان</th><th>کلاس</th><th>فوریت</th><th>قطعات</th><th>درخواست‌دهنده</th><th>تاریخ</th><th>وضعیت</th><th>دستور (ECO)</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="10">درخواستی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.number }}</td>
            <td>{{ r.title }}</td>
            <td>{{ r.classification === 1 ? "I" : "II" }}</td>
            <td>{{ ecUrgencies[r.urgency] }}</td>
            <td class="inv-ltr">{{ r.parts }}</td>
            <td>{{ r.requestedByName }}</td>
            <td>{{ jalali(r.createdAt) }}</td>
            <td><span class="inv-badge" :class="statusClass[r.status]">{{ r.statusText }}</span></td>
            <td class="inv-ltr"><router-link v-if="r.orderId" :to="{ name: 'EngChangeOrderEdit', params: { id: r.orderId } }">{{ r.orderNumber }}</router-link></td>
            <td class="d-print-none"><router-link :to="{ name: 'EngChangeRequestEdit', params: { id: r.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td>
          </tr>
        </tbody>
      </table>
      <p class="inv-muted small mb-0">{{ ecClasses[1] }}؛ {{ ecClasses[2] }}.</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
