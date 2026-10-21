<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import { getTestReports } from "@/api/QualityAPIs";
import { jalali, printPage } from "@/composables/inventory";
import { reportResults, reportStatuses, reportTypes, resultBadge, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Test reports of every type (PT, MT, UT, RT, hardness, PMI, hydro, run test, performance, balancing, painting, dimensional).
const route = useRoute();
const router = useRouter();
const filters = ref({ Type: null as number | null, Status: null as number | null, Contains: "", WorkOrderId: route.query.workOrderId ? Number(route.query.workOrderId) : null });
const list = usePaged(getTestReports, filters);
const canCreate = hasRole(QualityRoles.inspectors);
onMounted(list.load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فهرست گزارش‌های آزمون" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">گزارش‌های آزمون</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canCreate" class="btn btn-sm btn-primary" @click="router.push({ name: 'QualityReportEdit', params: { id: 'new' } })">گزارش آزمون جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره، سریال یا دستور ساخت" @keydown.enter="list.search()" />
        <select v-model="filters.Type" class="form-select form-select-sm" @change="list.search()">
          <option :value="null">همه انواع</option>
          <option v-for="(t, k) in reportTypes" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in reportStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <WorkOrderSelect v-model="filters.WorkOrderId" @change="list.search()" />
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>نوع</th><th>تاریخ</th><th>دستور ساخت</th><th>وضعیت</th><th>نتیجه</th><th>NCR</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">گزارشی نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id" role="button" @click="router.push({ name: 'QualityReportEdit', params: { id: r.id } })">
            <td class="inv-ltr">{{ r.number }}</td>
            <td>{{ reportTypes[r.type] }}</td>
            <td>{{ jalali(r.reportDate) }}</td>
            <td class="inv-ltr">{{ r.workOrderCode }}</td>
            <td><span :class="{ ok: r.status === 2, warn: r.status === 1, bad: r.status === 3 }" class="inv-badge">{{ reportStatuses[r.status] }}</span></td>
            <td><span :class="resultBadge(r.result)" class="inv-badge">{{ reportResults[r.result] }}</span></td>
            <td class="inv-ltr">{{ r.ncrNumber }}</td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
