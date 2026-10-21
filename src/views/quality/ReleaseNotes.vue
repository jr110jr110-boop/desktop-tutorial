<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import { getReleaseNotes } from "@/api/QualityAPIs";
import { jalali, printPage } from "@/composables/inventory";
import { releaseNoteStatuses, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Release notes (ISO 9001 8.6): one per work order; the checks are computed, never typed.
const route = useRoute();
const router = useRouter();
const filters = ref({ WorkOrderId: route.query.workOrderId ? Number(route.query.workOrderId) : null, Status: null as number | null, Contains: "" });
const list = usePaged(getReleaseNotes, filters);
const canCreate = hasRole(QualityRoles.managers);
onMounted(list.load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="برگه‌های ترخیص" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">برگه‌های ترخیص و گواهی انطباق</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canCreate" class="btn btn-sm btn-primary" @click="router.push({ name: 'QualityReleaseNoteEdit', params: { id: 'new' }, query: filters.WorkOrderId ? { workOrderId: filters.WorkOrderId } : {} })">برگه ترخیص جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا دستور ساخت" @keydown.enter="list.search()" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in releaseNoteStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <WorkOrderSelect v-model="filters.WorkOrderId" @change="list.search()" />
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>دستور ساخت</th><th>وضعیت</th><th>تاریخ صدور</th><th>گواهی انطباق</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="6" class="text-center inv-muted">برگه‌ای نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id" role="button" @click="router.push({ name: 'QualityReleaseNoteEdit', params: { id: r.id } })">
            <td class="inv-ltr">{{ r.number }}</td><td class="inv-ltr">{{ r.workOrderCode }}</td>
            <td><span :class="{ ok: r.status === 1, bad: r.status === 2, warn: r.status === 0 }" class="inv-badge">{{ releaseNoteStatuses[r.status] }}</span></td>
            <td>{{ jalali(r.releasedAt) }}</td><td class="inv-ltr">{{ r.certificateOfConformityNumber }}</td>
            <td class="d-print-none"><router-link :to="{ name: 'QualityDataBook', params: { workOrderId: r.workOrderId } }" @click.stop>دیتابوک</router-link></td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
