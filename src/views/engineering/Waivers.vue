<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import EnWaiverCard from "@/components/engineering/EnWaiverCard.vue";
import { getEnWaivers } from "@/api/EngineeringAPIs";
import { waiverStatuses } from "@/composables/engineering";
import { usePagedList } from "@/composables/fn";

// The emergency waivers of the document lock, the ones that wait for the CEO first. /engineering/waivers/:id shows that one waiver on top.
const route = useRoute();
const filters = ref({ Status: null as number | null });
const list = usePagedList(getEnWaivers, filters, 25);
onMounted(list.load);
watch(() => route.params.id, list.load);
const focus = computed(() => Number(route.params.id) || null);
const rows = computed(() => [...list.rows.value].sort((a, b) => Number(b.id === focus.value) - Number(a.id === focus.value) || a.status - b.status));
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">معافیت‌های اضطراری</h1></div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in waiverStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!rows.length" class="inv-muted" data-test="en-empty">معافیتی ثبت نشده است.</p>
      <EnWaiverCard v-for="w in rows" :key="w.id" :waiver="w" @changed="list.load" />
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>
