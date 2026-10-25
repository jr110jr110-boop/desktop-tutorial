<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getPurchaseRequests } from "@/api/ProcurementAPIs";
import { isSeatDraft, SEAT_DRAFT_TEXT } from "@/composables/ai";
import { fail, jalali, printPage, rial } from "@/composables/inventory";
import { purchaseKinds, requestSources, requestStatuses } from "@/composables/procurement";
import { hasRole, ProcurementRoles } from "@/composables/roles";

const router = useRouter();
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const filters = ref({ Status: null as number | null, Contains: "" });
const canCreate = hasRole(ProcurementRoles.requesters);
const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

async function load() {
  const [data, error] = await getPurchaseRequests({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

function search() {
  page.value = 1;
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فهرست درخواست‌های خرید" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">درخواست‌های خرید</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canCreate" class="btn btn-sm btn-primary" @click="router.push({ name: 'ProcurementRequestEdit', params: { id: 'new' } })">درخواست خرید جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا شرح" @keydown.enter="search" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in requestStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead>
          <tr><th>شماره</th><th>تاریخ</th><th>نوع</th><th>منبع</th><th>دستور ساخت / پروژه</th><th>برآورد (ریال)</th><th>درخواست‌کننده</th><th>وضعیت</th></tr>
        </thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="8" class="text-center inv-muted">درخواستی نیست.</td></tr>
          <tr v-for="r in rows" :key="r.id" role="button" @click="router.push({ name: 'ProcurementRequestEdit', params: { id: r.id } })">
            <td><span class="inv-ltr">{{ r.number }}</span><span v-if="isSeatDraft(r)" class="inv-badge info ms-1" title="این پیش‌نویس را دستیار هوشمند ساخته است؛ بررسی و ارسال با برنامه‌ریزی است.">{{ SEAT_DRAFT_TEXT }}</span></td>
            <td>{{ jalali(r.requestDate) }}</td>
            <td>{{ purchaseKinds[r.kind] }}</td>
            <td>{{ requestSources[r.source] }}</td>
            <td class="inv-ltr">{{ r.workOrderCode }} {{ r.contractCode }}</td>
            <td class="num">{{ rial(r.estimatedAmount) }}</td>
            <td>{{ r.creatorName }}</td>
            <td><span class="inv-badge" :class="{ ok: r.status === 2 || r.status === 3, warn: r.status === 0 || r.status === 1, bad: r.status === 9 }">{{ requestStatuses[r.status] }}</span></td>
          </tr>
        </tbody>
      </table>
      <div class="d-flex gap-2 align-items-center d-print-none">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--, load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }}</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++, load()">بعدی</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
