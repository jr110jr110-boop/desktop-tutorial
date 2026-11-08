<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { getCharLists } from "@/api/QualityAPIs";
import { jalali } from "@/composables/inventory";
import { charListStatuses, usePaged } from "@/composables/quality";

// Package B (H5): the characteristic lists of the drawing revisions. A list is made from the revision row of the document panel (EDMS), approved by a second person and released with the drawing.
const router = useRouter();
const filters = ref({ Status: null as number | null, Contains: "" });
const list = usePaged(getCharLists, filters);
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">فهرست مشخصه‌های نقشه</h1>
        <router-link :to="{ name: 'ProjectControlEdms' }" class="btn btn-sm btn-outline-secondary">مدارک مهندسی (ساخت فهرست از ردیف بازنگری)</router-link>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره نقشه" @keydown.enter="list.search()" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in charListStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره نقشه</th><th>بازنگری</th><th>نسخه فهرست</th><th>مشخصه‌ها</th><th>وضعیت</th><th>تاریخ انتشار</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="6" class="text-center inv-muted">فهرستی نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id" role="button" @click="router.push({ name: 'QualityCharacteristicEdit', params: { id: r.id } })">
            <td class="inv-ltr">{{ r.documentNumber }}</td>
            <td class="inv-ltr">{{ r.revisionCode }}</td>
            <td>{{ r.version }}</td>
            <td>{{ r.itemCount }}</td>
            <td><span :class="{ ok: r.status === 3, warn: r.status === 1 || r.status === 2, bad: r.status === 5 }" class="inv-badge">{{ charListStatuses[r.status] }}</span></td>
            <td>{{ r.releasedAt ? jalali(r.releasedAt) : "—" }}</td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
