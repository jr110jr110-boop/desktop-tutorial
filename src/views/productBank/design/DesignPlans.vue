<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getDesignPlans } from "@/api/DesignControlAPIs";
import { getProducts } from "@/api/ProductBankAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { DesignRoles, planStatusClass, planStatuses } from "@/composables/designControl";

// Package A1: the design plans of the products (ISO 9001 8.3.2): one plan per product and revision, prepared by engineering and approved by another person.
const router = useRouter();
const rows = ref<any[]>([]);
const products = ref<any[]>([]);
const status = ref<number | null>(null);
const productId = ref<number | null>(null);
const text = ref("");
const canMake = hasRole(DesignRoles.planMakers);

async function load() {
  const [data, error] = await getDesignPlans({ Status: status.value, ProductId: productId.value, Contains: text.value, PageSize: 200 });
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
onMounted(async () => {
  const [p] = await getProducts();
  products.value = p || [];
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="برنامه‌های طراحی" :subtitle="status === null ? 'همه وضعیت‌ها' : planStatuses[status]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">برنامه‌های طراحی محصولات</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canMake" class="btn btn-sm btn-primary" @click="router.push({ name: 'DesignPlan', params: { id: 'new' } })">+ برنامه طراحی جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="productId" class="form-select form-select-sm" @change="load">
          <option :value="null">همه محصولات</option>
          <option v-for="p in products" :key="p.id" :value="p.id">{{ p.code }} — {{ p.title }}</option>
        </select>
        <select v-model="status" class="form-select form-select-sm" @change="load">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in planStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="text" class="form-control form-control-sm" placeholder="شماره، عنوان یا کد محصول" @keyup.enter="load" />
        <button class="btn btn-sm btn-outline-secondary" @click="load">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead>
          <tr><th>شماره</th><th>محصول</th><th>بازنگری</th><th>نسخه برنامه</th><th>عنوان</th><th>صحه‌گذاری</th><th>وضعیت</th><th>تایید در</th><th class="d-print-none"></th></tr>
        </thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="9">برنامه طراحی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.number }}</td>
            <td><span class="inv-ltr">{{ r.productCode }}</span> {{ r.productTitle }}</td>
            <td class="inv-ltr">{{ r.productRevisionText }}</td>
            <td>{{ r.version }}</td>
            <td>{{ r.title }}</td>
            <td>{{ r.validationRequired ? "لازم" : "معاف" }}</td>
            <td><span class="inv-badge" :class="planStatusClass[r.status]">{{ planStatuses[r.status] }}</span></td>
            <td>{{ jalali(r.approvedAt) }}</td>
            <td class="d-print-none text-nowrap">
              <router-link :to="{ name: 'DesignPlan', params: { id: r.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link>
              <router-link :to="{ name: 'DesignDossier', params: { productId: r.productId } }" class="btn btn-sm btn-outline-secondary py-0 ms-1">پرونده طراحی</router-link>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../../inventory/inventory.scss"></style>
