<script setup lang="ts">
// The single trace page: product tree, reverse trace (recall), label scan (serial / lot, assembly) and the quality view of serials and batches.
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import TraceTreePanel from "@/components/trace/TraceTreePanel.vue";
import TraceReversePanel from "@/components/trace/TraceReversePanel.vue";
import ScanTrace from "@/views/inventory/Trace.vue";
import QualityTrace from "@/views/quality/Traceability.vue";

const route = useRoute();
const router = useRouter();

const tabs = [
  { key: "tree", title: "درخت محصول" },
  { key: "reverse", title: "ردیابی معکوس (فراخوان)" },
  { key: "scan", title: "اسکن برچسب" },
  { key: "quality", title: "سریال و بچ (کیفیت)" },
];
// ?tab= wins; the old links (?serial= / ?lot= of the scan page, the quality route) land on their own tab
const tab = computed(() => {
  const asked = String(route.query.tab || "");
  if (tabs.some((t) => t.key === asked)) return asked;
  if (route.meta.tab) return String(route.meta.tab);
  if (route.query.serial || route.query.lot) return "scan";
  return "tree";
});

function show(key: string) {
  router.replace({ query: { tab: key } });
}
</script>

<template>
  <div class="inv-page ks-report">
    <div class="inv-card">
      <div class="inv-toolbar d-print-none">
        <h1 class="inv-title">ردیابی و شجره‌نامه</h1>
        <div class="btn-group btn-group-sm" role="tablist">
          <button v-for="t in tabs" :key="t.key" :class="tab === t.key ? 'btn-primary' : 'btn-outline-primary'" class="btn" role="tab" :aria-selected="tab === t.key" @click="show(t.key)">{{ t.title }}</button>
        </div>
      </div>
      <TraceTreePanel v-if="tab === 'tree'" />
      <TraceReversePanel v-else-if="tab === 'reverse'" />
      <ScanTrace v-else-if="tab === 'scan'" />
      <QualityTrace v-else />
    </div>
  </div>
</template>
