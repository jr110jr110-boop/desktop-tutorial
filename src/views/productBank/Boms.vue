<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getBoms, rowsOf } from "@/api/ProductBankAPIs";
import { fail, jalali, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { PbRoles, bomStatusClass, bomStatuses, revisionText } from "@/composables/productBank";

// Bills of material of the product bank: one row per revision (00, 01 ...).
const router = useRouter();
const rows = ref<any[]>([]);
const status = ref<number | null>(null);
const text = ref("");
const canWrite = hasRole(PbRoles.writers);

async function load() {
  const [data, error] = await getBoms({ Status: status.value, Contains: text.value, PageSize: 200 });
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فهرست مواد (BOM)" :subtitle="status === null ? 'همه نسخه‌ها' : bomStatuses[status]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">فهرست مواد (BOM) محصولات</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="router.push({ name: 'ProductBankBomEdit', params: { id: 'new' } })">+ BOM جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="status" class="form-select form-select-sm" @change="load">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in bomStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="text" class="form-control form-control-sm" placeholder="کد، عنوان یا شماره قطعه مجموعه" @keyup.enter="load" />
        <button class="btn btn-sm btn-outline-secondary" @click="load">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead>
          <tr><th>کد BOM</th><th>نسخه</th><th>عنوان</th><th>وضعیت</th><th>تعداد ردیف</th><th>تاریخ سند</th><th>اجرا از</th><th class="d-print-none"></th></tr>
        </thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="8">BOM ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.code }}</td>
            <td class="inv-ltr">{{ revisionText(r.revision) }}</td>
            <td>{{ r.title }}</td>
            <td><span class="inv-badge" :class="bomStatusClass[r.status]">{{ bomStatuses[r.status] }}</span></td>
            <td>{{ r.lineCount }}</td>
            <td>{{ jalali(r.documentDate) }}</td>
            <td>{{ jalali(r.effectiveFrom) }}</td>
            <td class="d-print-none"><router-link :to="{ name: 'ProductBankBomEdit', params: { id: r.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
