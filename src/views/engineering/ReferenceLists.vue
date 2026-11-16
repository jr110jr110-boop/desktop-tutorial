<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import { getEnReferenceLists } from "@/api/EngineeringAPIs";
import { enPaths, EnRoles, faDigits, faNum, refStatuses } from "@/composables/engineering";
import { usePagedList } from "@/composables/fn";
import { hasRole } from "@/composables/roles";

// The reference document lists (the definition of «complete»). Several approved lists, one per standard code, stand side by side.
const filters = ref({ Status: null as number | null, Contains: "" });
const list = usePagedList(getEnReferenceLists, filters, 25);
onMounted(list.load);
const current = computed(() => list.rows.value.filter((r) => r.isCurrent));
const canNew = hasRole(EnRoles.writers);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">فهرست مدارک مرجع</h1>
        <router-link v-if="canNew" :to="enPaths.reference(0)" class="btn btn-sm btn-primary">فهرست تازه</router-link>
      </div>
      <h2 v-if="current.length" class="h6">فهرست‌های تاییدشده و جاری</h2>
      <div v-if="current.length" class="en-tiles">
        <router-link v-for="r in current" :key="r.id" :to="enPaths.reference(r.id)" class="en-tile ok">
          <div class="t">{{ r.title }}</div><div class="v inv-ltr" style="font-size: 1.1rem">{{ r.code }}</div>
          <div class="n">ویرایش {{ faNum(r.revision, 0) }} — {{ faNum(r.requiredCount, 0) }} الزامی از {{ faNum(r.lineCount, 0) }} سطر</div>
        </router-link>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in refStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" style="max-width: 14rem" @keyup.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!list.rows.value.length" class="inv-muted" data-test="en-empty">هنوز فهرست مرجعی نیست؛ سیستم محتوای خودش را ندارد و مهندسی باید آن را بنویسد و تایید کند.</p>
      <div v-else class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>ویرایش</th><th>وضعیت</th><th>سطرها</th><th>الزامی</th><th>مرحله مناقصه</th><th>تهیه‌کننده</th></tr></thead>
          <tbody>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr"><router-link :to="enPaths.reference(r.id)">{{ r.code }}</router-link></td>
              <td>{{ r.title }}</td><td>{{ faNum(r.revision, 0) }}</td>
              <td><EnChip :status="r.status" :text="r.statusText" group="ref" /> <span v-if="r.isCurrent" class="inv-badge ok">جاری</span></td>
              <td>{{ faNum(r.lineCount, 0) }}</td><td>{{ faNum(r.requiredCount, 0) }}</td><td>{{ faNum(r.tenderStageCount, 0) }}</td>
              <td>{{ r.preparedByName }} <span class="small inv-muted">{{ faDigits(r.submittedAtText || "") }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>
