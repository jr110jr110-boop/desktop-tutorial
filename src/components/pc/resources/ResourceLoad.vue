<script setup lang="ts">
// «بار منابع»: the load of the resources over all projects against their capacity (the histogram). The load comes from the approved resource plans of all live projects, spread over the working days of each activity;
// the optional layer shows the plans that are still being made («پیش‌نویس»). A click on a bar lists the activities of every project that make the load of that day or week.
import { computed, onMounted, ref, watch } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LoadChart from "@/components/pc/resources/LoadChart.vue";
import { getResourceLoad, getResources } from "@/api/ResourceAPIs";
import type { LoadBucket, LoadRow, Resource, ResourceLoad } from "@/api/ResourceAPIs";
import { fail, today } from "@/composables/inventory";
import { DRAFT_LAYER, faDate, loadNum, resourceLabel, resourceUnits } from "@/composables/resources";

const props = defineProps({ resourceId: { type: Number, default: null } });
const emit = defineEmits<{ (e: "overloads", resourceId: number): void }>();

const MAX_DAYS = 370;
const addDays = (iso: string, n: number) => {
  const d = new Date(iso + "T00:00:00Z");
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
};
const dayCount = (a: string, b: string) => Math.round((Date.parse(b + "T00:00:00Z") - Date.parse(a + "T00:00:00Z")) / 86400000) + 1;

const resources = ref<Resource[]>([]);
const resourceFilter = ref<number | null>(props.resourceId);
const from = ref(today());
const to = ref(addDays(today(), 59));
const bucket = ref<"day" | "week">("day");
const includeOpen = ref(false);
const data = ref<ResourceLoad | null>(null);
const busy = ref(false);
const selected = ref<string | null>(null);

const problem = computed(() => {
  if (!from.value || !to.value) return "بازه را کامل کنید.";
  if (to.value < from.value) return "تاریخ پایان از تاریخ شروع زودتر است.";
  if (dayCount(from.value, to.value) > MAX_DAYS) return "بازه‌ی نمایش حداکثر ۳۷۰ روز است؛ بازه‌ی کوتاه‌تری انتخاب کنید.";
  return null;
});

async function loadResources() {
  const [list, error] = await getResources({ isActive: true });
  if (error) return fail(error);
  resources.value = list;
}
// Requests are numbered: an answer that is not the newest request's is ignored, so a slow answer never overwrites a newer one.
let sequence = 0;
const loadedFor = ref<number | null>(null);
async function load() {
  const mine = ++sequence;
  selected.value = null;
  if (problem.value) { data.value = null; busy.value = false; return; }
  busy.value = true;
  const asked = resourceFilter.value;
  const [result, error] = await getResourceLoad({ resourceId: asked, from: from.value, to: to.value, bucket: bucket.value, includeOpen: includeOpen.value });
  if (mine !== sequence) return;
  busy.value = false;
  if (error) { data.value = null; return fail(error); }
  data.value = result;
  loadedFor.value = asked;
}

const rows = computed<LoadRow[]>(() => data.value?.resources ?? []);
const current = computed<LoadRow | null>(() => (resourceFilter.value && loadedFor.value === resourceFilter.value ? rows.value.find((r) => r.resourceId === resourceFilter.value) ?? null : null));
const unitOf = (r: LoadRow) => resourceUnits[r.unit] || "";
const pickedBucket = computed<LoadBucket | null>(() => current.value?.buckets.find((b) => b.start === selected.value) ?? null);
const byProject = computed(() => {
  const b = pickedBucket.value;
  if (!b) return [];
  const groups = new Map<string, { code: string; items: typeof b.items; total: number }>();
  for (const i of b.items) {
    const key = i.contractCode || "—";
    const g = groups.get(key) ?? { code: key, items: [], total: 0 };
    g.items.push(i);
    g.total += i.load;
    groups.set(key, g);
  }
  return [...groups.values()].sort((a, c) => c.total - a.total || a.code.localeCompare(c.code));
});
const bucketText = (b: LoadBucket) => (bucket.value === "week" ? `${faDate(b.start)} تا ${faDate(b.end)}` : faDate(b.start));

onMounted(() => {
  loadResources();
  load();
});
watch([resourceFilter, from, to, bucket, includeOpen], load);
watch(() => props.resourceId, (v) => { resourceFilter.value = v; });
</script>

<template>
  <div>
    <div class="inv-filters d-print-none">
      <select v-model.number="resourceFilter" class="form-select form-select-sm" style="max-width: 240px" aria-label="منبع">
        <option :value="null">همه منابع دارای بار</option>
        <option v-for="r in resources" :key="r.id" :value="r.id">{{ resourceLabel(r) }}</option>
      </select>
      <label class="small d-flex align-items-center gap-1">از <FnDate v-model="from" /></label>
      <label class="small d-flex align-items-center gap-1">تا <FnDate v-model="to" /></label>
      <select v-model="bucket" class="form-select form-select-sm" style="max-width: 120px" aria-label="بازه">
        <option value="day">روزانه</option>
        <option value="week">هفتگی</option>
      </select>
      <label class="form-check small mb-0"><input v-model="includeOpen" type="checkbox" class="form-check-input" /> نمایش طرح‌های {{ DRAFT_LAYER }}</label>
    </div>
    <p v-if="problem" class="inv-muted text-danger">{{ problem }}</p>
    <p v-else-if="includeOpen" class="small inv-muted">طرحِ در دست تهیه (پیش‌نویس، ارسال‌شده یا برگشت‌خورده) هر پروژه جای طرح تاییدشده‌ی همان پروژه را می‌گیرد و هاشورخورده نشان داده می‌شود.</p>

    <template v-if="!problem">
      <div v-if="busy" class="inv-muted">در حال بارگذاری…</div>

      <div v-if="!resourceFilter && data && loadedFor === null" class="table-responsive">
        <table class="table table-sm table-hover align-middle">
          <thead><tr><th>منبع</th><th>ظرفیت</th><th>اوج بار</th><th>جمع بیش‌تخصیص</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="5" class="text-center inv-muted">در این بازه برای هیچ منبعی بار ثبت نشده است.</td></tr>
            <tr v-for="r in rows" :key="r.resourceId">
              <td><span class="inv-ltr">{{ r.code }}</span> {{ r.title }}</td>
              <td>{{ loadNum(r.capacity) }} <span class="inv-muted small">{{ unitOf(r) }}</span></td>
              <td>{{ loadNum(r.peakLoad) }}</td>
              <td><span :class="r.totalOver > 0 ? 'inv-badge bad' : 'inv-muted'">{{ r.totalOver > 0 ? loadNum(r.totalOver) : "ندارد" }}</span></td>
              <td class="d-print-none"><button class="btn btn-sm btn-outline-primary" @click="resourceFilter = r.resourceId">هیستوگرام</button></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-else-if="current">
        <div class="d-flex flex-wrap gap-3 align-items-center mb-2">
          <strong><span class="inv-ltr">{{ current.code }}</span> {{ current.title }}</strong>
          <span class="inv-muted small">ظرفیت هر روز کاری: {{ loadNum(current.capacity) }} {{ unitOf(current) === "ساعت" ? "نفر (× ساعت کار روزانه)" : unitOf(current) }}</span>
          <span v-if="current.totalOver > 0" class="inv-badge bad">جمع بیش‌تخصیص: {{ loadNum(current.totalOver) }} {{ unitOf(current) }}</span>
          <button v-if="current.totalOver > 0" class="btn btn-sm btn-outline-danger d-print-none" @click="emit('overloads', current.resourceId)">بیش‌تخصیص‌ها و پیشنهادها</button>
        </div>
        <LoadChart :buckets="current.buckets" :unit-text="unitOf(current)" :weekly="bucket === 'week'" :selected="selected" @select="selected = $event" />
        <div v-if="pickedBucket" class="mt-3">
          <h2 class="inv-title">{{ bucketText(pickedBucket) }}: بار {{ loadNum(pickedBucket.load) }} از ظرفیت {{ loadNum(pickedBucket.capacity) }}</h2>
          <p v-if="pickedBucket.over > 0" class="text-danger small mb-1">بیش‌تخصیص این بازه: {{ loadNum(pickedBucket.over) }} {{ unitOf(current) }}</p>
          <div v-if="!byProject.length" class="inv-muted">فعالیتی در این بازه این منبع را نمی‌خواهد.</div>
          <div v-for="g in byProject" :key="g.code" class="mb-2">
            <div class="fw-bold">پروژه {{ g.code }} <span class="inv-muted small">— {{ loadNum(g.total) }} {{ unitOf(current) }}</span></div>
            <ul class="mb-0 small">
              <li v-for="i in g.items" :key="i.activityId + (i.isDraft ? 'd' : '')">
                <span class="inv-ltr">{{ i.activityCode }}</span> {{ i.activityTitle }} — {{ loadNum(i.load) }} {{ unitOf(current) }}
                <span v-if="i.isDraft" class="inv-badge info">{{ DRAFT_LAYER }}</span>
              </li>
            </ul>
          </div>
        </div>
        <p v-else class="small inv-muted mt-2">برای دیدن فعالیت‌های سازنده‌ی بار هر روز یا هفته، روی ستون آن بزنید.</p>
      </div>
      <div v-else-if="resourceFilter && data" class="inv-muted">منبع در این بازه پیدا نشد.</div>
    </template>
  </div>
</template>
