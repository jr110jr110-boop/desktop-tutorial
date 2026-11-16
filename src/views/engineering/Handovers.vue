<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import { getEnHandovers } from "@/api/EngineeringAPIs";
import { enPaths, faDigits, faNum, handoverStatuses } from "@/composables/engineering";
import { usePagedList } from "@/composables/fn";

// The handovers of the engineering documents to the PMO (one per case and round).
const route = useRoute();
const filters = ref({ Status: route.query.status !== undefined ? Number(route.query.status) : (null as number | null), OnlyOpen: false });
const list = usePagedList(getEnHandovers, filters, 25);
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">تحویل مدارک به PMO</h1></div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in handoverStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <label class="small"><input v-model="filters.OnlyOpen" type="checkbox" /> فقط باز</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!list.rows.value.length" class="inv-muted" data-test="en-empty">هنوز تحویلی ثبت نشده است؛ ایجنت مهندسی پس از برنده‌شدن پرونده، پیش‌نویس تحویل را می‌سازد.</p>
      <div v-else class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>پرونده</th><th>قرارداد</th><th>دور</th><th>وضعیت</th><th>الزامی کم</th><th>مجوز اضطرار</th></tr></thead>
          <tbody>
            <tr v-for="h in list.rows.value" :key="h.id">
              <td><router-link :to="enPaths.handover(h.id)">{{ faDigits(h.number) }}</router-link></td>
              <td><router-link :to="enPaths.case(h.caseId)">{{ faDigits(h.caseNumber) }}</router-link></td>
              <td>{{ faDigits(h.contractCode || "") }}</td><td>{{ faNum(h.round, 0) }}</td>
              <td><EnChip :status="h.status" :text="h.statusText" group="handover" /></td>
              <td><span :class="h.missingRequiredCount ? 'text-danger' : ''">{{ faNum(h.missingRequiredCount, 0) }} از {{ faNum(h.requiredCount, 0) }}</span></td>
              <td>{{ h.waiverNumber ? faDigits(h.waiverNumber) : "—" }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>
