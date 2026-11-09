<script setup lang="ts">
// Shop-floor viewer (E6, E11): only the latest revision in force (released / as-built) of the documents linked to the order's part / operation; earlier revisions are never shown here.
// The floor roles have no raw download: a copy is always the printed "controlled copy" with its stamp (revision, Shamsi date, user), shown in a page that uses the report print rules (B Zar, RTL, A4).
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { getShopDocuments, printControlledCopy } from "@/api/EdmsAPIs";
import { http } from "@/api/ApiClient";
import { downloadFile } from "@/api/InventoryAPIs";
import { preparePrint, stampPrint } from "@/api/GoLiveAPIs";
import { fail, jalali } from "@/composables/inventory";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

const props = defineProps<{ orderId: number; operationId?: number | null }>();
const docs = ref<any[]>([]);
const loaded = ref(false);
const copy = ref<{ stamp: any; url: string; image: boolean; stamped?: boolean } | null>(null);
const canRaw = computed(() => hasRole(ProjectControlRoles.docReaders));

async function load() {
  if (!props.orderId) return;
  const [d, e] = await getShopDocuments(props.orderId, props.operationId);
  loaded.value = true;
  if (e) return fail(e);
  docs.value = d || [];
}

function close() {
  if (copy.value) URL.revokeObjectURL(copy.value.url);
  copy.value = null;
  document.body.classList.remove("edms-printing");
}

async function fetchBlob(documentId: string): Promise<Blob | null> {
  try {
    return (await http.get("Documents", { params: { DocumentId: documentId }, responseType: "blob" })).data;
  } catch {
    fail("بارگیری فایل ممکن نشد.");
    return null;
  }
}

async function controlledPrint(doc: any, file: any) {
  const isPdf = /\.pdf$/i.test(file.fileName || "");
  if (isPdf) {
    // Package G (fix round 1): the server checks that the revision is in force, numbers the copy, records the print and stamps the stored file itself
    const [prep, pe] = await preparePrint({ revisionId: doc.revisionId, documentId: file.documentId });
    if (pe) return fail(pe);
    const out = await stampPrint(prep.printId, prep.header, prep.footer, prep.controlled);
    if (out) {
      copy.value = { stamp: { stampText: prep.header, stampFooter: prep.footer }, url: URL.createObjectURL(out), image: false, stamped: true };
      return;
    }
    // the server could not stamp this file: the HTML stamp of the prepared (already recorded) print over the file
    const blob = await fetchBlob(file.documentId);
    if (!blob) return;
    copy.value = { stamp: { stampText: prep.header, stampFooter: prep.footer }, url: URL.createObjectURL(blob), image: false, stamped: false };
    return;
  }
  // other files (images...): the stamp of the page, logged by a POST (a GET never writes)
  const [stamp, e] = await printControlledCopy(doc.revisionId, file.documentId);
  if (e) return fail(e);
  const blob = await fetchBlob(file.documentId);
  if (!blob) return;
  copy.value = { stamp, url: URL.createObjectURL(blob), image: (blob.type || "").startsWith("image/"), stamped: false };
}

function doPrint() {
  document.body.classList.add("edms-printing");
  const clear = () => document.body.classList.remove("edms-printing");
  window.addEventListener("afterprint", clear, { once: true });
  setTimeout(() => window.print(), 300);
}

onMounted(load);
onBeforeUnmount(close);
watch(() => [props.orderId, props.operationId], load);
</script>

<template>
  <div v-if="loaded" class="inv-card screen-only">
    <h3 class="h6">مدارک فنی منتشرشده</h3>
    <p class="small inv-muted mb-2">فقط آخرین بازنگری معتبر (منتشرشده یا As-Built) مدارک مرتبط با قطعه و مرحله نمایش داده می‌شود. نسخه‌ی کاغذی فقط با مهر «کپی کنترل‌شده» چاپ می‌شود.</p>
    <p v-if="!docs.length" class="text-danger small mb-0">سند منتشرشده‌ای برای این قطعه یا مرحله نیست.</p>
    <table v-else class="table table-sm mb-0">
      <thead><tr><th>شماره</th><th>عنوان</th><th>بازنگری</th><th>انتشار</th><th>فایل</th></tr></thead>
      <tbody>
        <tr v-for="d in docs" :key="d.revisionId">
          <td class="inv-ltr">{{ d.documentNumber }}</td><td>{{ d.title }}</td><td class="inv-ltr"><b>{{ d.code }}</b></td><td>{{ jalali(d.releasedAt) }}</td>
          <td>
            <div v-for="f in d.files" :key="f.id" class="d-flex gap-2 align-items-center">
              <span class="inv-ltr small">{{ f.fileName }}</span>
              <button class="btn btn-sm btn-outline-secondary py-0" @click="controlledPrint(d, f)">چاپ کنترل‌شده</button>
              <a v-if="canRaw" href="#" class="small" @click.prevent="downloadFile(f.documentId, f.fileName)">فایل اصلی</a>
            </div>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
  <Teleport to="body">
    <div v-if="copy" class="edms-copy ks-report">
      <div class="edms-copy-bar">
        <button class="btn btn-sm btn-primary" @click="doPrint">چاپ</button>
        <button class="btn btn-sm btn-secondary" @click="close">بستن</button>
      </div>
      <div v-if="!copy.stamped" class="edms-stamp"><b>نسخه کنترل‌شده</b><span>{{ copy.stamp.stampText }}</span><small>{{ copy.stamp.stampFooter }}</small></div>
      <img v-if="copy.image" :src="copy.url" class="edms-file" alt="" />
      <iframe v-else :src="copy.url" class="edms-file"></iframe>
    </div>
  </Teleport>
</template>

<style lang="scss">
.edms-copy { position: fixed; inset: 0; z-index: 2000; overflow: auto; background: #fff; color: #000; padding: 12px; }
.edms-copy-bar { display: flex; gap: 8px; margin-bottom: 8px; }
.edms-stamp { border: 2px solid #b00020; color: #b00020; padding: 6px 10px; margin-bottom: 8px; display: flex; flex-direction: column; gap: 2px; }
.edms-file { width: 100%; height: 250mm; border: 0; object-fit: contain; }
@media print {
  body.edms-printing > *:not(.edms-copy) { display: none !important; }
  .edms-copy { position: static; padding: 0; overflow: visible; }
  .edms-copy-bar { display: none; }
  @page { size: A4; margin: 12mm; }
}
</style>
