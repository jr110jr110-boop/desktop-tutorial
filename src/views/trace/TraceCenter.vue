<script setup lang="ts">
// The single trace page: product tree, reverse trace (recall), label scan (serial / lot, assembly) and the quality view of serials and batches.
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import TraceTreePanel from "@/components/trace/TraceTreePanel.vue";
import TraceReversePanel from "@/components/trace/TraceReversePanel.vue";
import ScanTrace from "@/views/inventory/Trace.vue";
import QualityTrace from "@/views/quality/Traceability.vue";
import { canTraceQuality, canTraceScan, canTraceTree } from "@/composables/traceAccess";

const route = useRoute();
const router = useRouter();

const allowed: Record<string, () => boolean> = { tree: canTraceTree, reverse: canTraceTree, scan: canTraceScan, quality: canTraceQuality };
const tabs = [
  { key: "tree", title: "درخت محصول" },
  { key: "reverse", title: "ردیابی معکوس (فراخوان)" },
  { key: "scan", title: "اسکن برچسب" },
  { key: "quality", title: "سریال و بچ (کیفیت)" },
].filter((t) => allowed[t.key]());
// ?tab= wins; the old links (?serial= / ?lot= of the scan page, the quality route) land on their own tab; a tab the user may not open is never chosen
const tab = computed(() => {
  const open = (key: string) => tabs.some((t) => t.key === key);
  const asked = String(route.query.tab || "");
  if (open(asked)) return asked;
  if (route.meta.tab && open(String(route.meta.tab))) return String(route.meta.tab);
  if ((route.query.serial || route.query.lot) && open("scan")) return "scan";
  return tabs.length ? tabs[0].key : "";
});

function show(key: string) {
  // the search text of the tree and the reverse tab is kept while the user moves between tabs
  const keep: Record<string, any> = {};
  if (route.query.q) keep.q = route.query.q;
  if (route.query.rq) keep.rq = route.query.rq;
  router.replace({ query: { ...keep, tab: key } });
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
