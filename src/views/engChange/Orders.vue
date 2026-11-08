<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getMyTasks, getOrders, rowsOf } from "@/api/EngChangeAPIs";
import { fail, jalali, printPage } from "@/composables/inventory";
import { ecoStatuses, statusClass, taskStatuses } from "@/composables/engChange";

// Engineering change orders (ECO): the content, the computed impact, the dispositions, the effectivity and the tasks. Approved by ENGINEERING and QA, two different people.
const rows = ref<any[]>([]);
const mine = ref<any[]>([]);
const status = ref<number | null>(null);
const text = ref("");
const part = ref("");

async function load() {
  const [data, error] = await getOrders({ Status: status.value, Contains: text.value, PartNumber: part.value });
  if (error) return fail(error);
  rows.value = rowsOf(data);
  const [t] = await getMyTasks();
  mine.value = rowsOf(t);
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="دستورهای تغییر مهندسی (ECO)" :subtitle="status === null ? 'همه وضعیت‌ها' : ecoStatuses[status]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">دستورهای تغییر مهندسی (ECO)</h1>
        <div class="d-flex gap-2 d-print-none">
          <router-link :to="{ name: 'EngChangeRequests' }" class="btn btn-sm btn-outline-secondary">درخواست‌ها (ECR)</router-link>
          <router-link :to="{ name: 'EngChangeHistory' }" class="btn btn-sm btn-outline-secondary">تاریخچه تغییر قطعه</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <div v-if="mine.length" class="alert alert-warning py-2 d-print-none">
        <strong>تکلیف‌های باز شما:</strong>
        <ul class="mb-0">
          <li v-for="t in mine" :key="t.id">{{ t.title }} — مهلت {{ jalali(t.dueDate) }} <span v-if="t.overdue" class="text-danger">(گذشته از مهلت)</span> — {{ taskStatuses[t.status] }}</li>
        </ul>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="status" class="form-select form-select-sm" @change="load">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in ecoStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="text" class="form-control form-control-sm" placeholder="شماره یا عنوان" @keyup.enter="load" />
        <input v-model="part" class="form-control form-control-sm inv-ltr" placeholder="شماره قطعه" @keyup.enter="load" />
        <button class="btn btn-sm btn-outline-secondary" @click="load">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>درخواست</th><th>عنوان</th><th>نقطه اجرا</th><th>قطعات</th><th>وضعیت</th><th>تکلیف باز</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="8">دستور تغییری ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.number }}</td>
            <td class="inv-ltr">{{ r.requestNumber }}</td>
            <td>{{ r.title }}</td>
            <td>{{ r.effectivity }}</td>
            <td class="inv-ltr">{{ r.parts }}</td>
            <td><span class="inv-badge" :class="statusClass[r.status]">{{ r.statusText }}</span></td>
            <td>{{ r.openTasks }}</td>
            <td class="d-print-none"><router-link :to="{ name: 'EngChangeOrderEdit', params: { id: r.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
