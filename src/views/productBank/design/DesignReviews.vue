<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getDesignReviews } from "@/api/DesignControlAPIs";
import { getProducts } from "@/api/ProductBankAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { DesignRoles, outcomeClass, reviewOutcomes, reviewStatusClass, reviewStatuses, reviewTypes, revisionText } from "@/composables/designControl";

// Package A1: the design reviews (ISO 9001 8.3.4): review, verification and validation. A BOM or a part drawing is released only after a closed, accepted verification covers it.
const router = useRouter();
const rows = ref<any[]>([]);
const products = ref<any[]>([]);
const productId = ref<number | null>(null);
const type = ref<number | null>(null);
const status = ref<number | null>(null);
const text = ref("");
const canMake = hasRole(DesignRoles.reviewMakers);

async function load() {
  const [data, error] = await getDesignReviews({ ProductId: productId.value, Type: type.value, Status: status.value, Contains: text.value, PageSize: 200 });
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
    <KsReportHeader title="بازبینی‌های طراحی" :subtitle="type === null ? 'همه انواع' : reviewTypes[type]" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">بازبینی، تایید و صحه‌گذاری طراحی</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canMake" class="btn btn-sm btn-primary" @click="router.push({ name: 'DesignReview', params: { id: 'new' } })">+ بازبینی جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="productId" class="form-select form-select-sm" @change="load">
          <option :value="null">همه محصولات</option>
          <option v-for="p in products" :key="p.id" :value="p.id">{{ p.code }} — {{ p.title }}</option>
        </select>
        <select v-model="type" class="form-select form-select-sm" @change="load">
          <option :value="null">همه انواع</option>
          <option v-for="(t, k) in reviewTypes" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="status" class="form-select form-select-sm" @change="load">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in reviewStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="text" class="form-control form-control-sm" placeholder="شماره، عنوان یا کد محصول" @keyup.enter="load" />
        <button class="btn btn-sm btn-outline-secondary" @click="load">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>نوع</th><th>محصول</th><th>بازنگری</th><th>عنوان</th><th>وضعیت</th><th>نتیجه</th><th>یافته باز</th><th>بسته شده در</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="10" class="text-center inv-muted">بازبینی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.number }}</td><td>{{ reviewTypes[r.type] }}</td><td class="inv-ltr">{{ r.productCode }}</td><td class="inv-ltr">{{ revisionText(r.productRevision) }}</td><td>{{ r.title }}</td>
            <td><span class="inv-badge" :class="reviewStatusClass[r.status]">{{ reviewStatuses[r.status] }}</span></td>
            <td><span v-if="r.status === 2" class="inv-badge" :class="outcomeClass[r.outcome]">{{ reviewOutcomes[r.outcome] }}</span></td>
            <td>{{ r.openFindings }}</td><td>{{ jalali(r.closedAt) }}</td>
            <td class="d-print-none"><router-link :to="{ name: 'DesignReview', params: { id: r.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../../inventory/inventory.scss"></style>
