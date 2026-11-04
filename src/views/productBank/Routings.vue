<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getRoutings } from "@/api/ProductBankAPIs";
import { fail, jalali, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { PbRoles, revisionText, routingStatuses } from "@/composables/productBank";

// OPC routings (Operation Process Chart) of parts and assemblies, one row per revision.
const router = useRouter();
const rows = ref<any[]>([]);
const part = ref("");
const canWrite = hasRole(PbRoles.writers);

async function load() {
  const [data, error] = await getRoutings({ partNumber: part.value });
  if (error) return fail(error);
  rows.value = data || [];
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="چارت‌های فرآیند عملیات (OPC)" subtitle="مسیر ساخت قطعات" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">چارت‌های فرآیند عملیات (OPC)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="router.push({ name: 'ProductBankRoutingEdit', params: { id: 'new' } })">+ چارت جدید</button>
        </div>
      </div>
      <p class="inv-muted screen-only">هر چارت پس از تایید، مرحله‌های خود را به مسیر ساخت (MES) منتقل می‌کند؛ مرحله‌های انبارشی برای هر قطعه کالای مرحله می‌گیرند (شماره قطعه + مرحله).</p>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="part" class="form-control form-control-sm inv-ltr" placeholder="شماره قطعه" @keyup.enter="load" />
        <button class="btn btn-sm btn-outline-secondary" @click="load">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره قطعه / مجموعه</th><th>عنوان</th><th>نسخه</th><th>وضعیت</th><th>تعداد مرحله</th><th>انتقال به MES</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="7">چارتی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.partNumber }}</td>
            <td>{{ r.title }}</td>
            <td class="inv-ltr">{{ revisionText(r.revision) }}</td>
            <td><span class="inv-badge" :class="r.status === 2 ? 'ok' : r.status === 3 ? '' : 'warn'">{{ routingStatuses[r.status] }}</span></td>
            <td>{{ r.stepCount }}</td>
            <td>{{ jalali(r.publishedToMesAt) || "-" }}</td>
            <td class="d-print-none"><router-link :to="{ name: 'ProductBankRoutingEdit', params: { id: r.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
