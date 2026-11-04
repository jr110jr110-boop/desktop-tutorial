<script setup lang="ts">
// Reverse trace (recall): from a raw-material lot or heat number, a supplier, a purchase order or request, down to every part, assembly, serial, project and delivery.
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { exportReverseTrace, getReverseTrace } from "@/api/TraceAPIs";
import { fail, jalali, num, ok, printPage } from "@/composables/inventory";

const route = useRoute();
const router = useRouter();
const q = ref(String(route.query.rq || ""));
const kind = ref("");
const depth = ref<number | null>(null);
const data = ref<any>(null);
const busy = ref(false);
let last: any = null;

const kinds: Record<string, string> = { "": "تشخیص خودکار", lot: "بچ", heat: "شماره ذوب", supplier: "تامین‌کننده", po: "سفارش خرید", pr: "درخواست خرید", mr: "درخواست مواد", serial: "سریال" };
const levels = computed(() => {
  const map = new Map<number, any[]>();
  for (const n of data.value?.nodes || []) {
    if (!map.has(n.level)) map.set(n.level, []);
    map.get(n.level)!.push(n);
  }
  return [...map.entries()].sort((a, b) => a[0] - b[0]);
});

async function run() {
  if (!q.value.trim()) return fail("شماره بچ / ذوب، تامین‌کننده، سفارش یا درخواست خرید را وارد کنید.");
  busy.value = true;
  last = { Q: q.value.trim(), Kind: kind.value || undefined, Depth: depth.value || undefined };
  const [result, error] = await getReverseTrace(last);
  busy.value = false;
  if (error) return fail(error);
  data.value = result;
  if (!(result.roots || []).length) fail((result.notes || [])[0] || "چیزی پیدا نشد.");
  router.replace({ query: { ...route.query, rq: q.value.trim() } });
}

async function toExcel() {
  if (!last) return;
  const error = await exportReverseTrace(last);
  if (error) fail(error);
  else ok("فایل اکسل ذخیره شد.");
}

function nodeText(n: any) {
  return n.serialNumber ? "سریال " + n.serialNumber : n.lotNumber ? "بچ " + n.lotNumber : n.itemCode;
}

if (q.value) run();
</script>

<template>
  <div>
    <div class="inv-filters mb-3 d-print-none">
      <input v-model="q" class="form-control form-control-sm inv-ltr" placeholder="شماره بچ / ذوب، تامین‌کننده، سفارش یا درخواست خرید" @keydown.enter="run" />
      <select v-model="kind" class="form-select form-select-sm" aria-label="نوع">
        <option v-for="(t, k) in kinds" :key="k" :value="k">{{ t }}</option>
      </select>
      <select v-model="depth" class="form-select form-select-sm" aria-label="عمق">
        <option :value="null">عمق پیش‌فرض</option>
        <option v-for="d in [1, 2, 3, 5, 10, 15]" :key="d" :value="d">{{ d }} سطح</option>
      </select>
      <button class="btn btn-sm btn-primary" :disabled="busy" @click="run">{{ busy ? "..." : "ردیابی معکوس" }}</button>
      <button v-if="data && data.roots.length" class="btn btn-sm btn-outline-success" @click="toExcel">اکسل</button>
      <button v-if="data && data.roots.length" class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
    </div>

    <div v-if="data && data.roots.length">
      <KsReportHeader title="ردیابی معکوس (فراخوان)" :subtitle="data.query" />
      <div v-if="data.truncated" class="alert alert-warning py-1 small">نتیجه به سقف عمق ({{ data.maxDepth }}) یا تعداد گره‌ها رسید و کوتاه شده است.</div>
      <ul v-if="(data.notes || []).length" class="small inv-muted">
        <li v-for="(n, i) in data.notes" :key="i">{{ n }}</li>
      </ul>

      <div class="row g-2 mb-3 text-center">
        <div v-for="c in [['مبدا', data.counts.roots], ['گره', data.counts.nodes], ['کالا', data.counts.items], ['بچ', data.counts.lots], ['سریال', data.counts.serials], ['محصول نهایی', data.counts.terminals], ['پروژه', data.counts.projects], ['تحویل به مشتری', data.counts.deliveries]]" :key="c[0]" class="col-6 col-md-3 col-lg">
          <div class="border rounded p-2"><div class="h5 mb-0">{{ c[1] }}</div><div class="small inv-muted">{{ c[0] }}</div></div>
        </div>
      </div>

      <h2 class="h6">مبداها</h2>
      <div class="table-responsive mb-3">
        <table class="table table-sm">
          <thead><tr><th>کالا</th><th>بچ</th><th>ذوب</th><th>گواهی</th><th>تامین‌کننده</th><th>رسید</th><th>سفارش</th><th>درخواست</th><th>مقدار</th><th>تاریخ</th></tr></thead>
          <tbody>
            <tr v-for="r in data.roots" :key="r.key">
              <td class="inv-ltr">{{ r.itemCode }} <span class="inv-muted">{{ r.itemTitle }}</span></td>
              <td class="inv-ltr">{{ r.lotNumber || r.serialNumber }}</td><td class="inv-ltr">{{ r.heatNumber }}</td><td class="inv-ltr">{{ r.certificateNumber }}</td>
              <td>{{ r.providerName }}</td><td class="inv-ltr">{{ r.receiptNumber }}</td><td class="inv-ltr">{{ r.purchaseOrderNumber }}</td><td class="inv-ltr">{{ r.purchaseRequestNumber }}</td>
              <td>{{ num(r.receivedQuantity) }}</td><td>{{ jalali(r.receivedDate) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2 class="h6">مصرف در سطوح بعدی</h2>
      <div v-for="[level, nodes] in levels" :key="level" class="mb-2">
        <div class="small inv-muted">سطح {{ level }}</div>
        <div class="table-responsive">
          <table class="table table-sm mb-1">
            <thead><tr><th>کالا</th><th>مرحله</th><th>شناسه</th><th>از</th><th>دستور تولید</th><th>سند</th><th>مقدار</th><th>پروژه</th></tr></thead>
            <tbody>
              <tr v-for="n in nodes" :key="n.key" :class="{ 'table-warning': n.terminal }">
                <td class="inv-ltr">{{ n.itemCode }} <span class="inv-muted">{{ n.itemTitle }}</span></td>
                <td class="inv-ltr">{{ n.stageCode }}</td><td class="inv-ltr">{{ nodeText(n) }}</td><td class="inv-ltr">{{ n.fromKey }}</td>
                <td class="inv-ltr">{{ n.orderNumber }}</td><td class="inv-ltr">{{ n.documentNumber }}</td><td>{{ num(n.quantity) }}</td><td class="inv-ltr">{{ n.workOrderCode }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <h2 class="h6">پروژه‌ها و مشتریان</h2>
      <div v-if="!data.projects.length" class="inv-muted small mb-2">پروژه‌ای پیدا نشد.</div>
      <ul v-else class="small">
        <li v-for="p in data.projects" :key="p.workOrderId">{{ p.workOrderCode }} {{ p.workOrderTitle }} · قرارداد {{ p.contractCode }} · کارفرما: {{ p.customer }} ({{ p.nodeCount }} گره)</li>
      </ul>

      <h2 class="h6">تحویل به مشتری</h2>
      <div v-if="!data.deliveries.length" class="inv-muted small">تحویلی پیدا نشد.</div>
      <div v-else class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>سند</th><th>تاریخ</th><th>کالا</th><th>سریال / بچ</th><th>مقدار</th><th>پروژه</th><th>کارفرما</th><th>مقصد</th></tr></thead>
          <tbody>
            <tr v-for="d in data.deliveries" :key="d.documentId + d.nodeKey">
              <td class="inv-ltr">{{ d.documentNumber }}</td><td>{{ jalali(d.documentDate) }}</td><td class="inv-ltr">{{ d.itemCode }}</td><td class="inv-ltr">{{ d.serialNumber || d.lotNumber }}</td>
              <td>{{ num(d.quantity) }}</td><td class="inv-ltr">{{ d.workOrderCode }}</td><td>{{ d.customer }}</td><td>{{ d.destination }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>
