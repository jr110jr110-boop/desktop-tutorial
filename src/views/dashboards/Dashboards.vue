<script setup lang="ts">
// The management dashboards hub: the dashboards of the signed-in role, each rendered by the generic widget component. Read only.
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import DashboardWidgets from "@/components/dashboards/DashboardWidgets.vue";
import OpsWidget from "@/components/opsData/OpsWidget.vue";
import { getDashboard, getDashboardIndex } from "@/api/DashboardsAPIs";
import { fail } from "@/composables/inventory";
import { pcContractId } from "@/composables/projectControl";

const route = useRoute();
const router = useRouter();
const index = ref<any[]>([]);
const data = ref<any>(null);
const busy = ref(false);
const key = computed(() => (route.params.key as string) || index.value[0]?.key || "");
const current = computed(() => index.value.find((d) => d.key === key.value));
// PB8: the data-quality widget of the unit (1 warehouse, 2 production, 3 quality, 4 finance); the CEO and PMO dashboards show all units and the readiness.
const opsUnits: Record<string, number> = { warehouse: 1, production: 2, quality: 3, finance: 4 };
const opsAll = computed(() => key.value === "ceo" || key.value === "pmo" || key.value === "portfolio");

async function load() {
  data.value = null;
  if (!key.value) return;
  if (current.value?.needsContract && !pcContractId.value) return;
  busy.value = true;
  const [d, e] = await getDashboard(key.value, current.value?.needsContract ? pcContractId.value : null);
  busy.value = false;
  if (e) return fail(e);
  data.value = d;
}
onMounted(async () => {
  const [d, e] = await getDashboardIndex();
  if (e) return fail(e);
  index.value = d || [];
  await load();
});
watch([key, pcContractId], load);
const open = (k: string) => router.push("/dashboards/" + k);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="داشبوردهای مدیریتی" :subtitle="current?.title" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title screen-only">{{ current?.title || "داشبوردهای مدیریتی" }}</h1>
          <span class="inv-muted d-print-none">{{ current?.description }}</span>
        </div>
        <div class="d-flex gap-2 d-print-none"><ContractSelect v-if="current?.needsContract" /><button class="btn btn-sm btn-outline-secondary" @click="load">به‌روزرسانی</button></div>
      </div>
      <div class="d-flex flex-wrap gap-1 mb-3 d-print-none">
        <button v-for="d in index" :key="d.key" class="btn btn-sm" :class="d.key === key ? 'btn-primary' : 'btn-outline-secondary'" @click="open(d.key)">{{ d.title }}</button>
      </div>
      <OpsWidget v-if="opsUnits[key] || opsAll" :key="key" :unit="opsUnits[key] ?? null" :readiness="opsAll" />
      <p v-if="!index.length" class="inv-muted">داشبوردی برای نقش شما تعریف نشده است.</p>
      <p v-else-if="current?.needsContract && !pcContractId" class="inv-muted">برای این داشبورد یک پروژه انتخاب کنید.</p>
      <p v-else-if="busy" class="inv-muted">در حال بارگذاری…</p>
      <DashboardWidgets v-else-if="data" :dashboard="data" />
      <p v-if="data" class="inv-muted small mt-3 mb-0">به‌روزشده: {{ new Date(data.generatedAt).toLocaleString("fa-IR") }}</p>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
