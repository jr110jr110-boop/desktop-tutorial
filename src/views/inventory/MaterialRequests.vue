<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getMaterialRequests } from "@/api/InventoryAPIs";
import { fail, jalali, materialRequestStatuses, printPage } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

const router = useRouter();
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const status = ref<number | null>(null);
const contains = ref("");
const canWrite = hasRole(InventoryRoles.materialRequest);
const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

async function load() {
  const [data, error] = await getMaterialRequests({ Status: status.value, Contains: contains.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

function search() {
  page.value = 1;
  load();
}

function open(id: number | string) {
  router.push({ name: "InventoryMaterialRequestEdit", params: { id } });
}

function statusClass(s: number) {
  return s === 2 ? "ok" : s === 3 ? "bad" : s === 4 ? "" : "warn";
}

onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="درخواست‌های کالا" :subtitle="status === null ? 'همه' : materialRequestStatuses[status]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">درخواست کالا از انبار</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="open('new')">+ درخواست کالا</button>
        </div>
      </div>
      <p class="inv-muted screen-only">
        حواله فقط بر اساس درخواستی صادر می‌شود که برنامه‌ریزی و کنترل پروژه و مدیر تولید آن را تایید کرده باشند، و هیچ‌وقت بیشتر از مانده درخواست نیست.
      </p>
      <div class="inv-filters mb-3">
        <select v-model="status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in materialRequestStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="contains" class="form-control form-control-sm" placeholder="شماره، کالا، دستور ساخت" @keydown.enter="search" />
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th>شماره</th>
              <th>تاریخ</th>
              <th>دستور ساخت / محل مصرف</th>
              <th>انبار</th>
              <th>ردیف‌ها</th>
              <th>برنامه‌ریزی</th>
              <th>تولید</th>
              <th>وضعیت</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="!rows.length">
              <td class="text-center inv-muted" colspan="8">درخواستی نیست.</td>
            </tr>
            <tr v-for="r in rows" :key="r.id" role="button" @click="open(r.id)">
              <td class="inv-ltr">{{ r.number || "پیش‌نویس" }}</td>
              <td>{{ jalali(r.requestDate) }}</td>
              <td>{{ r.workOrderCode || r.purpose }}</td>
              <td>{{ r.warehouseTitle }}</td>
              <td>{{ r.linesCount }}</td>
              <td>{{ r.planningApproved ? "✓" : "" }}</td>
              <td>{{ r.manufacturingApproved ? "✓" : "" }}</td>
              <td><span class="inv-badge" :class="statusClass(r.status)">{{ materialRequestStatuses[r.status] }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="inv-pager">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--; load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }} ({{ total }} درخواست)</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++; load()">بعدی</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
