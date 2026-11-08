<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { getFirstArticles } from "@/api/QualityAPIs";
import { jalali } from "@/composables/inventory";
import { faiStatuses, usePaged } from "@/composables/quality";

// Package B (FAI): the first article inspections. One is opened automatically when the first production order of a part after a new released drawing / BOM / routing revision is released.
const router = useRouter();
const filters = ref({ Status: null as number | null, Contains: "" });
const list = usePaged(getFirstArticles, filters);
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">بازرسی نمونه اول (FAI)</h1></div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا کد قطعه" @keydown.enter="list.search()" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in faiStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>قطعه</th><th>دستور تولید</th><th>دستور ساخت</th><th>علت</th><th>وضعیت</th><th>تاریخ</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">موردی نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id" role="button" @click="router.push({ name: 'QualityFirstArticleEdit', params: { id: r.id } })">
            <td class="inv-ltr">{{ r.number }}</td><td><span class="inv-ltr">{{ r.itemCode }}</span> {{ r.itemTitle }}</td><td class="inv-ltr">{{ r.productionOrderNumber }}</td><td class="inv-ltr">{{ r.workOrderCode }}</td>
            <td class="small">{{ r.trigger }}</td>
            <td><span :class="{ ok: r.status === 3, warn: r.status >= 0 && r.status <= 2, bad: r.status >= 4 }" class="inv-badge">{{ faiStatuses[r.status] }}</span></td>
            <td>{{ jalali(r.createDate) }}</td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
