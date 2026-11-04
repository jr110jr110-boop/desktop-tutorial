<script setup lang="ts">
// Shop-floor viewer (E6, E11): only the latest RELEASED revision of the documents linked to the order's part / operation; earlier revisions are never shown here.
// Printing goes through an HTML wrapper that carries the "controlled copy" stamp (no PDF library on the server). B Zar, RTL, A4.
import { onMounted, ref, watch } from "vue";
import { getControlledCopy, getShopDocuments } from "@/api/EdmsAPIs";
import { http } from "@/api/ApiClient";
import { downloadFile } from "@/api/InventoryAPIs";
import { fail } from "@/composables/inventory";
import { jalali } from "@/composables/inventory";

const props = defineProps<{ orderId: number; operationId?: number | null }>();
const docs = ref<any[]>([]);
const loaded = ref(false);

async function load() {
  if (!props.orderId) return;
  const [d, e] = await getShopDocuments(props.orderId, props.operationId);
  loaded.value = true;
  if (e) return fail(e);
  docs.value = d || [];
}

const esc = (s: any) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c] as string);

async function controlledPrint(doc: any, file: any) {
  const [stamp, e] = await getControlledCopy(doc.revisionId, file.documentId, true);
  if (e) return fail(e);
  let url = "";
  try {
    const r = await http.get("Documents", { params: { DocumentId: file.documentId }, responseType: "blob" });
    url = URL.createObjectURL(r.data);
  } catch {
    return fail("بارگیری فایل ممکن نشد.");
  }
  const w = window.open("", "_blank");
  if (!w) return fail("پنجره‌ی چاپ مسدود شد.");
  w.document.write(`<!doctype html><html dir="rtl" lang="fa"><head><meta charset="utf-8"><title>${esc(stamp.documentNumber)}</title>
<style>@page{size:A4;margin:12mm}body{font-family:"B Zar","IRANYekan",sans-serif;margin:0}
.stamp{border:2px solid #b00020;color:#b00020;padding:6px 10px;margin-bottom:8px;font-weight:bold;display:flex;justify-content:space-between;gap:12px}
iframe,img{width:100%;height:250mm;border:0;object-fit:contain}</style></head><body>
<div class="stamp"><span>کپی کنترل‌شده</span><span dir="ltr">${esc(stamp.documentNumber)} / Rev ${esc(stamp.code)}</span><span>${esc(stamp.stampText)}</span></div>
<iframe src="${url}"></iframe></body></html>`);
  w.document.close();
  setTimeout(() => w.print(), 800);
}

onMounted(load);
watch(() => [props.orderId, props.operationId], load);
</script>

<template>
  <div v-if="loaded" class="inv-card screen-only">
    <h3 class="h6">مدارک فنی منتشرشده</h3>
    <p class="small inv-muted mb-2">فقط آخرین بازنگری منتشرشده‌ی مدارک مرتبط با قطعه و مرحله نمایش داده می‌شود. چاپ با مهر «کپی کنترل‌شده» انجام می‌شود.</p>
    <p v-if="!docs.length" class="text-danger small mb-0">سند منتشرشده‌ای برای این قطعه یا مرحله نیست.</p>
    <table v-else class="table table-sm mb-0">
      <thead><tr><th>شماره</th><th>عنوان</th><th>بازنگری</th><th>انتشار</th><th>فایل</th></tr></thead>
      <tbody>
        <tr v-for="d in docs" :key="d.revisionId">
          <td class="inv-ltr">{{ d.documentNumber }}</td><td>{{ d.title }}</td><td class="inv-ltr"><b>{{ d.code }}</b></td><td>{{ jalali(d.releasedAt) }}</td>
          <td><div v-for="f in d.files" :key="f.id" class="d-flex gap-2 align-items-center"><a href="#" @click.prevent="downloadFile(f.documentId, f.fileName)">{{ f.fileName }}</a><button class="btn btn-sm btn-outline-secondary py-0" @click="controlledPrint(d, f)">چاپ کنترل‌شده</button></div></td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
