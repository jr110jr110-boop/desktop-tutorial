<script setup lang="ts">
// The resources frozen with a baseline (the assignments of the approved resource plan and the standard rate of the day): read only.
import { ref, watch } from "vue";
import { getBaselineResources } from "@/api/ResourceAPIs";
import type { BaselineResource } from "@/api/ResourceAPIs";
import { fail } from "@/composables/inventory";
import { distributions, qty, rateText } from "@/composables/resources";

const props = defineProps({ baselineId: { type: Number, required: true } });
const rows = ref<BaselineResource[] | null>(null);
const busy = ref(false);

async function load() {
  busy.value = true;
  const [data, error] = await getBaselineResources(props.baselineId);
  busy.value = false;
  if (error) return fail(error);
  rows.value = data;
}
watch(() => props.baselineId, () => { rows.value = null; });
</script>

<template>
  <div class="mt-3">
    <div class="d-flex flex-wrap gap-2 align-items-center">
      <b class="small">منابع خط مبنا</b>
      <button :disabled="busy" class="btn btn-sm btn-outline-primary d-print-none" @click="load">{{ rows ? "خواندن دوباره" : "نمایش منابع خط مبنا" }}</button>
      <span v-if="rows" class="inv-muted small">{{ qty(rows.length) }} ردیف</span>
    </div>
    <div v-if="rows" class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>فعالیت</th><th>منبع</th><th>مقدار</th><th>توزیع</th><th>نرخ در زمان خط مبنا (ریال)</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="5" class="text-center inv-muted">برای این خط مبنا منبعی منجمد نشده است (طرح منابع تاییدشده‌ای نبوده است).</td></tr>
          <tr v-for="r in rows" :key="r.activityId + '-' + r.resourceId">
            <td class="inv-ltr">{{ r.activityCode }}</td><td class="inv-ltr">{{ r.resourceCode }}</td><td>{{ qty(r.quantity) }}</td><td>{{ distributions[r.distribution] }}</td>
            <td :class="r.rateHidden ? 'inv-muted' : r.rateAtBaseline === null ? 'text-danger' : ''">{{ rateText(r.rateAtBaseline, r.rateHidden) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
