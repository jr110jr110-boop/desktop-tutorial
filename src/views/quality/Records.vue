<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import { getQcRecords } from "@/api/QualityAPIs";
import { jalali, printPage } from "@/composables/inventory";
import { qcResults, recordTypes, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Inspection records (PR-QC-003): in-process, final, dimensional, first article, contractor shop.
const route = useRoute();
const router = useRouter();
const filters = ref({ Type: null as number | null, Result: null as number | null, Contains: "", WorkOrderId: route.query.workOrderId ? Number(route.query.workOrderId) : null });
const list = usePaged(getQcRecords, filters);
const canCreate = hasRole(QualityRoles.recordWriters);
onMounted(list.load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فهرست سوابق بازرسی" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">سوابق بازرسی</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canCreate" class="btn btn-sm btn-primary" @click="router.push({ name: 'QualityRecordEdit', params: { id: 'new' } })">بازرسی جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره، سریال یا دستور ساخت" @keydown.enter="list.search()" />
        <select v-model="filters.Type" class="form-select form-select-sm" @change="list.search()">
          <option :value="null">همه انواع</option>
          <option v-for="(t, k) in recordTypes" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.Result" class="form-select form-select-sm" @change="list.search()">
          <option :value="null">همه نتیجه‌ها</option>
          <option v-for="(t, k) in qcResults" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <WorkOrderSelect v-model="filters.WorkOrderId" @change="list.search()" />
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>نوع</th><th>تاریخ</th><th>دستور ساخت</th><th>کالا</th><th>سریال</th><th>بازرس</th><th>نتیجه</th><th>NCR</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">سابقه‌ای نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id" role="button" @click="router.push({ name: 'QualityRecordEdit', params: { id: r.id } })">
            <td class="inv-ltr">{{ r.number }}</td>
            <td>{{ recordTypes[r.type] }}</td>
            <td>{{ jalali(r.recordDate) }}</td>
            <td class="inv-ltr">{{ r.workOrderCode }}</td>
            <td class="inv-ltr">{{ r.itemCode }}</td>
            <td class="inv-ltr">{{ r.serialNumber }}</td>
            <td>{{ r.inspectorName }} <span v-if="r.selfInspection" class="inv-badge info">خودبازرسی</span></td>
            <td><span :class="{ ok: r.result === 1, bad: r.result === 2, warn: r.result === 3 || r.result === 4 }" class="inv-badge">{{ qcResults[r.result] }}</span></td>
            <td class="inv-ltr">{{ r.ncrNumber }}</td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
