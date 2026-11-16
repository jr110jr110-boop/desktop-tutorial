<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnGapView from "@/components/engineering/EnGapView.vue";
import { getEnGap, getEnGaps } from "@/api/EngineeringAPIs";
import { enPaths, faDigits, faNum, gapStatuses } from "@/composables/engineering";
import { fail } from "@/composables/inventory";
import { usePagedList } from "@/composables/fn";

// The standard-gap reports (what changes when a project's standard differs from the platform's): made by the main agent (AG-MASTER), confirmed by the head of engineering.
const route = useRoute();
const router = useRouter();
const filters = ref({ Status: null as number | null, OnlyOpen: false });
const list = usePagedList(getEnGaps, filters, 25);
const detail = ref<any>(null);
const selected = computed(() => Number(route.params.id) || null);
async function loadDetail() {
  detail.value = null;
  if (!selected.value) return;
  const [d, e] = await getEnGap(selected.value);
  if (e) return fail(e);
  detail.value = d;
}
async function reload() {
  await list.load();
  await loadDetail();
}
onMounted(reload);
watch(selected, loadDetail);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">تفاوت استاندارد</h1></div>
      <EnGapView v-if="detail" :gap="detail" @changed="reload" />
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in gapStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <label class="small"><input v-model="filters.OnlyOpen" type="checkbox" /> فقط باز</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!list.rows.value.length" class="inv-muted" data-test="en-empty">گزارش تفاوت دو استانداردی نیست؛ ایجنت اصلی وقتی استاندارد پروژه با استاندارد پلتفرم فرق کند آن را می‌سازد.</p>
      <div v-else class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>پرونده</th><th>از</th><th>به</th><th>وضعیت</th><th>افزوده</th><th>حذف</th><th>تغییر الزام</th></tr></thead>
          <tbody>
            <tr v-for="g in list.rows.value" :key="g.id" style="cursor: pointer" @click="router.push(enPaths.gap(g.id))">
              <td>{{ faDigits(g.number) }}</td><td>{{ faDigits(g.caseNumber) }}</td><td class="inv-ltr">{{ g.baseCode }}</td><td class="inv-ltr">{{ g.targetCode }}</td>
              <td><EnChip :status="g.status" :text="g.statusText" group="gap" /></td><td>{{ faNum(g.addedCount, 0) }}</td><td>{{ faNum(g.removedCount, 0) }}</td><td>{{ faNum(g.requiredChangedCount, 0) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>
