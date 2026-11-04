<script setup lang="ts">
// Product tree: search a product, part, serial or lot and read its structure (as designed from the project copy of the BOM, as built from the serials and lots).
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import TraceNode from "@/components/trace/TraceNode.vue";
import { exportTraceTree, getTraceTree } from "@/api/TraceAPIs";
import { fail, ok, printPage } from "@/composables/inventory";

const route = useRoute();
const router = useRouter();
const q = ref(String(route.query.q || ""));
const depth = ref<number | null>(null);
const data = ref<any>(null);
const busy = ref(false);
let last: any = null;
const modeTitles: Record<string, string> = { product: "محصول", part: "قطعه", serial: "سریال", lot: "بچ", choices: "انتخاب" };

function params(extra: any = {}) {
  return { Q: q.value.trim(), Depth: depth.value || undefined, ...extra };
}

async function run(extra: any = {}) {
  if (!q.value.trim() && !extra.LotId && !extra.SerialUnitId) return fail("کد محصول، شماره قطعه، سریال یا بچ / ذوب را وارد کنید.");
  busy.value = true;
  last = params(extra);
  const [result, error] = await getTraceTree(last);
  busy.value = false;
  if (error) return fail(error);
  data.value = result;
  if (!result.root && !(result.choices || []).length) fail((result.notes || [])[0] || "چیزی پیدا نشد.");
  router.replace({ query: { ...route.query, q: q.value.trim() || undefined } });
}

function choose(c: any) {
  if (c.q) q.value = c.q;
  run({ WorkOrderId: c.workOrderId || undefined, SerialUnitId: c.serialUnitId || undefined, LotId: c.lotId || undefined, Kind: c.kind || undefined });
}

async function toExcel() {
  if (!last) return;
  const error = await exportTraceTree(last);
  if (error) fail(error);
  else ok("فایل اکسل ذخیره شد.");
}

if (q.value) run();
</script>

<template>
  <div>
    <div class="inv-filters mb-3 d-print-none">
      <input v-model="q" class="form-control form-control-sm inv-ltr" placeholder="کد محصول، شماره قطعه، سریال یا بچ / ذوب / شماره برچسب" @keydown.enter="run()" />
      <select v-model="depth" class="form-select form-select-sm" aria-label="عمق">
        <option :value="null">عمق پیش‌فرض</option>
        <option v-for="d in [2, 3, 4, 6, 8]" :key="d" :value="d">{{ d }} سطح</option>
      </select>
      <button class="btn btn-sm btn-primary" :disabled="busy" @click="run()">{{ busy ? "..." : "نمایش درخت" }}</button>
      <button v-if="data && data.root" class="btn btn-sm btn-outline-success" @click="toExcel">اکسل</button>
      <button v-if="data && data.root" class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
    </div>

    <div v-if="data && (data.choices || []).length" class="mb-3">
      <div class="inv-muted small mb-1">چند مورد پیدا شد؛ یکی را انتخاب کنید:</div>
      <div class="list-group">
        <button v-for="(c, i) in data.choices" :key="i" type="button" class="list-group-item list-group-item-action" @click="choose(c)">
          <strong>{{ c.title }}</strong> <span class="inv-muted small">{{ c.description }}</span>
        </button>
      </div>
    </div>

    <div v-if="data && data.root">
      <KsReportHeader title="درخت محصول و ردیابی" :subtitle="data.root.title" />
      <div class="small mb-2">
        <span class="inv-badge" :class="data.mode === 'product' ? 'trace-b-info' : 'trace-b-ok'">{{ modeTitles[data.mode] || data.mode }}</span>
        <span v-if="data.pricesMasked" class="inv-badge trace-b-warn ms-2">قیمت‌ها برای این نقش نمایش داده نمی‌شود</span>
        <span v-if="data.namesMasked" class="inv-badge trace-b-warn ms-2">نام تامین‌کننده و کارفرما محرمانه است</span>
        <span class="inv-muted ms-2">{{ data.nodeCount }} گره · عمق {{ data.maxDepth }}</span>
      </div>
      <div v-if="data.truncated" class="alert alert-warning py-1 small">درخت به سقف {{ data.maxParts }} قطعه یا عمق {{ data.maxDepth }} رسید و کوتاه شده است.</div>
      <ul v-if="(data.notes || []).length" class="small inv-muted">
        <li v-for="(n, i) in data.notes" :key="i">{{ n }}</li>
      </ul>
      <TraceNode :node="data.root" :level="0" :open="true" />
    </div>
  </div>
</template>

<style scoped lang="scss">
.trace-b-ok { background: #d1e7dd; color: #0f5132; }
.trace-b-warn { background: #fff3cd; color: #664d03; }
.trace-b-info { background: #e2e3e5; color: #41464b; }
</style>
