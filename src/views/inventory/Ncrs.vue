<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getNcrs } from "@/api/InventoryAPIs";
import { fail, jalali, ncrDispositions, ncrSeverities, ncrSources, ncrStatuses, num, printPage } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

const router = useRouter();
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const status = ref<number | null>(null);
const contains = ref("");
const canCreate = hasRole(InventoryRoles.qc);
const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

async function load() {
  const [data, error] = await getNcrs({ Status: status.value, Contains: contains.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

function search() {
  page.value = 1;
  load();
}

function open(id: number | string) {
  router.push({ name: "InventoryNcrEdit", params: { id } });
}

function statusClass(s: number) {
  return s === 3 ? "ok" : s === 4 ? "bad" : "warn";
}

onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="گزارش‌های عدم انطباق (NCR)" :subtitle="status === null ? 'همه' : ncrStatuses[status]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">گزارش‌های عدم انطباق (PR-QC-002)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canCreate" class="btn btn-sm btn-primary" @click="open('new')">+ گزارش عدم انطباق</button>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in ncrStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="contains" class="form-control form-control-sm" placeholder="شماره، کالا، سریال، تامین‌کننده" @keydown.enter="search" />
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th>شماره</th>
              <th>تاریخ</th>
              <th>منبع</th>
              <th>کالا</th>
              <th>مقدار</th>
              <th>نوع</th>
              <th>تصمیم</th>
              <th>تامین‌کننده / دستور ساخت</th>
              <th>وضعیت</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="!rows.length">
              <td class="text-center inv-muted" colspan="9">گزارشی نیست.</td>
            </tr>
            <tr v-for="r in rows" :key="r.id" role="button" :class="{ 'text-muted': r.status === 4 }" @click="open(r.id)">
              <td class="inv-ltr">{{ r.number }}</td>
              <td>{{ jalali(r.reportDate) }}</td>
              <td>{{ ncrSources[r.source] }}</td>
              <td>
                <span class="inv-ltr">{{ r.itemCode }}</span> — {{ r.itemTitle }}
              </td>
              <td class="num">{{ num(r.quantity) }}</td>
              <td>{{ ncrSeverities[r.severity] }}</td>
              <td>{{ ncrDispositions[r.disposition] }}</td>
              <td>{{ r.providerName || r.workOrderCode }}</td>
              <td><span class="inv-badge" :class="statusClass(r.status)">{{ ncrStatuses[r.status] }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="inv-pager">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--; load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }} ({{ total }} گزارش)</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++; load()">بعدی</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
