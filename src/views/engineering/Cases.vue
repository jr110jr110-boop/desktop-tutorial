<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import { getEnCases } from "@/api/EngineeringAPIs";
import { caseKinds, caseStatuses, classifications, enPaths, faDigits } from "@/composables/engineering";
import { usePagedList } from "@/composables/fn";

// The engineering cases (one per controlled inquiry or contract). The filters come from the overview cards (?status=, ?classification=).
const route = useRoute();
const q = (k: string) => (route.query[k] !== undefined && route.query[k] !== "" ? Number(route.query[k]) : null);
const filters = ref({ Status: q("status"), Classification: q("classification"), Contains: "", OnlyOpen: false });
const list = usePagedList(getEnCases, filters, 25);
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">پرونده‌های مهندسی</h1></div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in caseStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.Classification" class="form-select form-select-sm" aria-label="نوع">
          <option :value="null">همه نوع‌ها</option>
          <option v-for="(t, k) in classifications" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا عنوان" style="max-width: 14rem" @keyup.enter="list.search" />
        <label class="small"><input v-model="filters.OnlyOpen" type="checkbox" /> فقط باز</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!list.rows.value.length" class="inv-muted" data-test="en-empty">پرونده‌ای با این شرایط نیست. پرونده مهندسی را ایجنت مهندسی برای هر استعلام و قرارداد کنترل‌شده باز می‌کند.</p>
      <div v-else class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>عنوان</th><th>نوع پرونده</th><th>مشتری</th><th>وضعیت</th><th>طبقه‌بندی</th><th>شفاف‌سازی باز</th><th>هشدار</th></tr></thead>
          <tbody>
            <tr v-for="c in list.rows.value" :key="c.id">
              <td><router-link :to="enPaths.case(c.id)">{{ faDigits(c.number) }}</router-link></td>
              <td>{{ c.title }}</td>
              <td>{{ c.kindText || caseKinds[c.kind] }}</td>
              <td>{{ c.customerName }}</td>
              <td><EnChip :status="c.status" :text="c.statusText" group="case" /></td>
              <td>{{ c.classificationText }}</td>
              <td>{{ faDigits(String(c.openClarifications || 0)) }}</td>
              <td><span v-if="c.platformIncomplete" class="inv-badge warn">مدارک پلتفرم ناقص</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>
