<script setup lang="ts">
// One node of the product tree (recursive): title, status, facts, files and the records it links to. Nodes below the first levels start collapsed.
import { computed, ref } from "vue";
import { useRouter } from "vue-router";
import { downloadFile } from "@/api/InventoryAPIs";
import { jalali, num } from "@/composables/inventory";

const props = defineProps<{ node: any; level: number; open?: boolean }>();
const router = useRouter();
const expanded = ref(props.open !== undefined ? props.open : props.level < 2);

const kindTitles: Record<string, string> = {
  product: "محصول", part: "قطعه", stage: "مرحله OPC", order: "دستور تولید", operation: "عملیات", material: "ماده اولیه", lot: "بچ", serial: "سریال",
  receipt: "رسید خرید", po: "سفارش خرید", inquiry: "استعلام", request: "درخواست خرید", purchase: "زنجیره خرید", inspection: "بازرسی", ncr: "عدم انطباق", delivery: "تحویل به مشتری",
};
const basisTitles: Record<string, string> = { asDesigned: "طراحی", asBuilt: "ساخت‌شده" };
const kindTitle = computed(() => kindTitles[props.node.kind] || props.node.kind);
const hasBody = computed(() => (props.node.children || []).length > 0 || (props.node.facts || []).length > 0 || (props.node.files || []).length > 0);

function go() {
  const l = props.node.link;
  if (!l) return;
  switch (l.type) {
    case "document": return router.push({ name: "InventoryDocumentEdit", params: { id: l.id } });
    case "po": return router.push({ name: "ProcurementOrderEdit", params: { id: l.id } });
    case "inquiry": return router.push({ name: "ProcurementInquiryEdit", params: { id: l.id } });
    case "request": return router.push({ name: "ProcurementRequestEdit", params: { id: l.id } });
    case "ncr": return router.push({ name: "InventoryNcrEdit", params: { id: l.id } });
    case "inspection": return router.push({ name: "InventoryInspections", query: { Contains: l.number } });
    case "mesOrder": return router.push({ name: "MfOrder", params: { id: l.id } });
    case "lot": return router.push({ name: "InventoryTrace", query: { lot: l.id } });
    case "serial": return router.push({ name: "InventoryTrace", query: { serial: l.id } });
    case "item": return router.push({ name: "InventoryItems", query: { Contains: l.number } });
  }
}
function dossier() {
  router.push({ name: "ProcurementDossier", query: { number: props.node.dossierNumber } });
}
</script>

<template>
  <div class="trace-node" :class="'trace-' + (node.tone || 'info')">
    <div class="trace-head">
      <button v-if="hasBody" type="button" class="trace-toggle d-print-none" :aria-expanded="expanded" @click="expanded = !expanded">{{ expanded ? "−" : "+" }}</button>
      <span v-else class="trace-toggle trace-toggle-empty"></span>
      <span class="inv-badge" :class="'trace-b-' + (node.tone || 'info')">{{ kindTitle }}</span>
      <strong class="trace-title">{{ node.title }}</strong>
      <span v-if="node.code && node.code !== node.title" class="inv-ltr inv-muted small">{{ node.code }}</span>
      <span v-if="node.status" class="small">{{ node.status }}</span>
      <span v-if="node.basis" class="inv-muted small">({{ basisTitles[node.basis] || node.basis }})</span>
      <span v-if="node.quantity !== null && node.quantity !== undefined" class="small">{{ num(node.quantity) }} {{ node.unit }}</span>
      <span v-if="node.date" class="inv-muted small">{{ jalali(node.date) }}</span>
      <button v-if="node.link && node.link.type" type="button" class="btn btn-sm btn-link p-0 d-print-none" @click="go">باز کردن {{ node.link.number || "" }}</button>
      <button v-if="node.dossierNumber" type="button" class="btn btn-sm btn-link p-0 d-print-none" @click="dossier">پرونده خرید {{ node.dossierNumber }}</button>
    </div>
    <div v-show="expanded" class="trace-body">
      <ul v-if="(node.facts || []).length" class="trace-facts">
        <li v-for="(f, i) in node.facts" :key="i"><span class="inv-muted">{{ f.label }}:</span> <span>{{ f.value }}</span></li>
      </ul>
      <ul v-if="(node.files || []).length" class="trace-files">
        <li v-for="f in node.files" :key="f.id">
          <button type="button" class="btn btn-sm btn-link p-0" @click="downloadFile(f.documentId, f.fileName)">{{ f.title || f.fileName }}</button>
          <span class="inv-muted small"> {{ f.kindTitle }}<template v-if="f.revision"> ویرایش {{ f.revision }}</template><template v-if="f.ownerTitle"> · {{ f.ownerTitle }} {{ f.ownerNumber }}</template></span>
          <span v-if="f.propagated" class="inv-badge trace-b-info">از درخواست خرید</span>
          <span v-if="f.legacy" class="inv-badge trace-b-warn">قدیمی</span>
        </li>
      </ul>
      <div v-if="node.truncated" class="trace-b-warn inv-badge">فهرست کوتاه شده است؛ برای دیدن بقیه جست‌وجو را دقیق‌تر کنید.</div>
      <TraceNode v-for="c in node.children" :key="c.key" :node="c" :level="level + 1" />
    </div>
  </div>
</template>

<style scoped lang="scss">
.trace-node { border-inline-start: 2px solid var(--bs-border-color, #dee2e6); margin-inline-start: 0.75rem; padding-inline-start: 0.5rem; margin-block: 0.25rem; }
.trace-head { display: flex; flex-wrap: wrap; align-items: center; gap: 0.4rem; }
.trace-toggle { width: 1.4rem; height: 1.4rem; line-height: 1; border: 1px solid var(--bs-border-color, #dee2e6); background: transparent; border-radius: 4px; padding: 0; }
.trace-toggle-empty { border-color: transparent; }
.trace-body { padding-block: 0.25rem; }
.trace-facts, .trace-files { list-style: none; margin: 0.25rem 0; padding: 0; font-size: 0.85rem; }
.trace-ok { border-inline-start-color: #198754; }
.trace-warn { border-inline-start-color: #fd7e14; }
.trace-bad { border-inline-start-color: #dc3545; }
.trace-b-ok { background: #d1e7dd; color: #0f5132; }
.trace-b-warn { background: #fff3cd; color: #664d03; }
.trace-b-bad { background: #f8d7da; color: #842029; }
.trace-b-info { background: #e2e3e5; color: #41464b; }
@media print {
  .trace-body { display: block !important; }
}
</style>
