<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import JDate from "@/components/pc/JDate.vue";
import WorkOrderOfContract from "@/components/pc/WorkOrderOfContract.vue";
import { getShipments, saveShipment, shipmentAction } from "@/api/ProjectControlAPIs";
import { getReleaseNotes } from "@/api/QualityAPIs";
import { rowsOf } from "@/api/ApiClient";
import { getDocuments, uploadFile } from "@/api/InventoryAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import ShipmentLinesEditor from "@/components/pc/ShipmentLinesEditor.vue";
import ShipmentPrint from "@/components/pc/print/ShipmentPrint.vue";
import { contractLabel, pcContractId, shipmentStatuses, usePcPrint } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Shipments: planned -> packed (needs a released quality release note) -> released by planning -> shipped -> delivered -> handover minutes.
const { job, run } = usePcPrint();
const rows = ref<any[]>([]);
const status = ref<number | null>(null);
const form = ref<any>(null);
const act = ref<any>(null);
const gates = ref<Record<number, string>>({});
const deliveryDocs = ref<any[]>([]);
const busy = ref(false);
const canPack = hasRole(ProjectControlRoles.shipmentPack);
const canRelease = hasRole(ProjectControlRoles.planning);
const canDeliver = hasRole(ProjectControlRoles.shipmentDeliver);
const canHandover = hasRole(ProjectControlRoles.shipmentHandover);
const canCheck = hasRole(["QC", "QA", "PMO"]);

async function load() {
  if (!pcContractId.value) return;
  const [d, e] = await getShipments({ ContractId: pcContractId.value, Status: status.value });
  if (e) return fail(e);
  rows.value = rowsOf(d);
  checkGates();
}
/** Gate: a released quality release note for the work order. The quality list may be closed to the role: then the gate is shown as unknown. */
async function checkGates() {
  const ids = [...new Set(rows.value.filter((r) => r.status === 0).map((r) => r.workOrderId))];
  for (const id of ids) {
    const [d, e] = await getReleaseNotes({ WorkOrderId: id, Status: 1 });
    gates.value[id] = e ? "unknown" : rowsOf(d).length ? "ok" : "bad";
  }
}
/** Lines as the server takes them (the editor keeps helper fields on them). */
const linesOf = (f: any) => (f.lines || []).map((l: any) => ({ itemId: l.itemId || null, serialUnitId: l.serialUnitId || null, quantity: Number(l.quantity) || 0, description: l.description || null }));
const linesOk = (f: any) => (f.lines || []).every((l: any) => Number(l.quantity) > 0 && (l.itemId || l.serialUnitId || (l.description || "").trim()));
function edit(r: any) {
  form.value = { id: r.id, workOrderId: r.workOrderId, packingList: r.packingList || "", lines: (r.lines || []).map((l: any) => ({ ...l, kind: l.serialUnitId ? "serial" : l.itemId ? "item" : "text" })) };
}
async function save() {
  if (!linesOk(form.value)) return fail("هر ردیف محموله باید تعداد بیشتر از صفر و کالا، شماره سریال یا شرح داشته باشد.");
  busy.value = true;
  // an edit sends the id, the work order and the packing list as they are; a request without lines would keep the saved lines
  const [, e] = await saveShipment({ id: form.value.id, workOrderId: form.value.workOrderId, packingList: form.value.packingList, lines: linesOf(form.value), contractId: pcContractId.value });
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد.");
  form.value = null;
  load();
}
const fieldsOf: Record<string, [string, string][]> = {
  pack: [["packingList", "فهرست بسته‌بندی"], ["packingVerification", "نتیجه بازبینی بسته‌بندی"]],
  "check-packing": [["packingVerification", "نتیجه بازبینی بسته‌بندی"]],
  commission: [],
  release: [],
  ship: [["waybill", "شماره بارنامه"], ["shippingDocuments", "اسناد حمل"]],
  deliver: [["deliveryReceiptNumber", "شماره رسید تحویل"]],
  handover: [["handoverMinutesNumber", "شماره صورتجلسه تحویل"]],
};
const titleOf: Record<string, string> = { "check-packing": "بازبینی بسته‌بندی (کنترل کیفیت)", commission: "تاریخ راه‌اندازی", pack: "بسته‌بندی", release: "مجوز ارسال (برنامه‌ریزی)", ship: "ارسال", deliver: "تحویل", handover: "صورتجلسه تحویل" };
function ask(r: any, action: string) {
  act.value = { r, action, date: today(), packingList: r.packingList || "", packingVerification: "", waybill: "", shippingDocuments: "", deliveryReceiptNumber: "", handoverMinutesNumber: "", inventoryDocumentId: r.inventoryDocumentId ?? null, photo: null, receiptFile: null };
  if (action === "ship") loadDeliveryDocs(r);
}
/** Why the act dialog cannot be submitted yet (shown as a hint). */
const actHint = () => {
  const a = act.value;
  if (!a) return "";
  if (a.action === "deliver") return !(a.deliveryReceiptNumber || "").trim() ? "شماره رسید تحویل را وارد کنید." : !a.receiptFile ? "برای ثبت تحویل، فایل رسید امضاشده را انتخاب کنید." : "";
  if (a.action === "ship") return !a.inventoryDocumentId ? "سند تحویل به مشتری تاییدشده انبار را انتخاب کنید." : "";
  return "";
};
/** Confirmed customer-delivery documents (type 11) of the work order: the ship step ties the shipment to one of them. */
async function loadDeliveryDocs(r: any) {
  deliveryDocs.value = [];
  const [d] = await getDocuments({ Type: 11, Status: 1, WorkOrderId: r.workOrderId, PageNumber: 1, PageSize: 100 });
  deliveryDocs.value = rowsOf(d);
}
function pickFile(e: Event, key: string) {
  const input = e.target as HTMLInputElement;
  act.value[key] = input.files && input.files[0] ? input.files[0] : null;
}
/** Uploads the chosen file; returns its document id, null when none was chosen, or false after a failure (already shown). */
async function upload(file: File | null, title: string): Promise<string | null | false> {
  if (!file) return null;
  const [id, e] = await uploadFile(file, title);
  if (e) {
    fail(e);
    return false;
  }
  return id;
}
async function doAct() {
  const a = act.value;
  busy.value = true;
  let photoId: string | null = null;
  let receiptId: string | null = null;
  if (a.action === "pack" || a.action === "deliver") {
    const up = a.action === "pack" ? await upload(a.photo, "عکس بسته‌بندی " + a.r.number) : await upload(a.receiptFile, "رسید امضاشده تحویل " + a.r.number);
    if (up === false) {
      busy.value = false;
      return;
    }
    if (a.action === "pack") photoId = up;
    else receiptId = up;
  }
  const [, e] = await shipmentAction({ packingPhotoDocumentId: photoId, signedReceiptDocumentId: receiptId, id: a.r.id, action: a.action, date: a.date, packingList: a.packingList, packingVerification: a.packingVerification, waybill: a.waybill, shippingDocuments: a.shippingDocuments, deliveryReceiptNumber: a.deliveryReceiptNumber, handoverMinutesNumber: a.handoverMinutesNumber, inventoryDocumentId: a.action === "ship" ? Number(a.inventoryDocumentId) || null : null });
  busy.value = false;
  if (e) return fail(e);
  ok("انجام شد.");
  act.value = null;
  load();
}
const gateText = (r: any) => {
  if (r.status === 0) return gates.value[r.workOrderId] === "ok" ? "برگه ترخیص کیفی صادر شده؛ بسته‌بندی مجاز است" : gates.value[r.workOrderId] === "bad" ? "برگه ترخیص کیفی صادرشده ندارد؛ بسته‌بندی بسته است" : "وضعیت برگه ترخیص را کیفیت تعیین می‌کند";
  if (r.status === 1) return "منتظر مجوز ارسال برنامه‌ریزی";
  if (r.status === 2) return "مجوز ارسال صادر شده";
  return "";
};
onMounted(load);
watch(pcContractId, load);
defineExpose({ rows });
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="محموله‌ها و ارسال" :subtitle="contractLabel(pcContractId)" />
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title screen-only">محموله‌ها و ارسال</h1><div class="d-print-none"><ContractSelect /></div></div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <div class="inv-filters mb-2 d-print-none">
          <select v-model="status" class="form-select form-select-sm" @change="load"><option :value="null">همه وضعیت‌ها</option><option v-for="(n, k) in shipmentStatuses" :key="k" :value="Number(k)">{{ n }}</option></select>
          <button v-if="canPack" class="btn btn-sm btn-primary" @click="form = { workOrderId: null, packingList: '', lines: [] }">محموله جدید</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>دستور ساخت</th><th>وضعیت</th><th>کنترل ترخیص</th><th>بارنامه</th><th>تحویل</th><th>صورتجلسه</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="8" class="text-center inv-muted">محموله‌ای نیست.</td></tr>
            <tr v-for="r in rows" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td><td class="inv-ltr">{{ r.workOrderCode }}<div class="small inv-muted">{{ (r.lines || []).length }} ردیف</div></td>
              <td><span :class="r.status >= 4 ? 'ok' : r.status === 0 ? 'info' : 'warn'" class="inv-badge">{{ shipmentStatuses[r.status] }}</span></td>
              <td class="small"><span :class="r.status === 0 && gates[r.workOrderId] === 'bad' ? 'text-danger' : ''">{{ gateText(r) }}</span><span v-if="r.releaseNoteId"> (برگه #{{ r.releaseNoteId }})</span></td>
              <td class="small"><span class="inv-ltr">{{ r.waybill }}</span> {{ jalali(r.shippedAt) }}</td>
              <td class="small"><span class="inv-ltr">{{ r.deliveryReceiptNumber }}</span><span v-if="r.signedReceiptDocumentId" title="رسید امضاشده پیوست است"> (رسید پیوست)</span> {{ jalali(r.deliveredAt) }}</td>
              <td class="small"><span class="inv-ltr">{{ r.handoverMinutesNumber }}</span> {{ jalali(r.handoverAt) }}</td>
              <td class="d-print-none text-nowrap">
                <button v-if="canPack && r.status === 0" class="btn btn-sm btn-outline-secondary me-1" @click="edit(r)">اقلام محموله</button>
                <button v-if="canPack && r.status === 0" class="btn btn-sm btn-outline-primary me-1" @click="ask(r, 'pack')">بسته‌بندی</button>
                <button v-if="canCheck && r.status === 1 && r.packedAt && !r.packingCheckedAt" class="btn btn-sm btn-outline-primary me-1" title="باید غیر از بسته‌بندی‌کننده باشد" @click="ask(r, 'check-packing')">بازبینی بسته‌بندی</button>
                <button v-if="canRelease && r.status === 1" class="btn btn-sm btn-outline-primary me-1" @click="ask(r, 'release')">مجوز ارسال</button>
                <button v-if="canPack && r.status === 2" class="btn btn-sm btn-outline-primary me-1" @click="ask(r, 'ship')">ارسال</button>
                <button v-if="canDeliver && r.status === 3" class="btn btn-sm btn-outline-primary me-1" @click="ask(r, 'deliver')">تحویل</button>
                <button v-if="canHandover && r.status === 4" class="btn btn-sm btn-outline-success me-1" @click="ask(r, 'handover')">صورتجلسه</button>
                <button v-if="canHandover && r.status === 5 && !r.commissionedAt" class="btn btn-sm btn-outline-success me-1" @click="ask(r, 'commission')">راه‌اندازی</button>
                <button class="btn btn-sm btn-outline-secondary" @click="run('shipment', r)">چاپ فهرست بسته‌بندی</button>
              </td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>
    <ShipmentPrint v-if="job?.kind === 'shipment'" :s="job.data" />
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 760px">
        <h3 class="h6">{{ form.id ? "اقلام محموله" : "محموله جدید" }}</h3>
        <label class="form-label small mb-0">دستور ساخت</label><WorkOrderOfContract v-model="form.workOrderId" :contract-id="pcContractId" :disabled="!!form.id" placeholder="انتخاب کنید" class="mb-2" />
        <label class="form-label small mb-0">فهرست بسته‌بندی</label><textarea v-model="form.packingList" class="form-control form-control-sm mb-3" rows="4"></textarea>
        <ShipmentLinesEditor v-model="form.lines" :work-order-id="form.workOrderId" />
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy || !form.workOrderId" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
    <div v-if="act" class="inv-modal-backdrop" @click.self="act = null">
      <div class="inv-modal">
        <h3 class="h6">{{ titleOf[act.action] }} — <span class="inv-ltr">{{ act.r.number }}</span></h3>
        <div v-for="f in fieldsOf[act.action]" :key="f[0]" class="mb-2"><label class="form-label small mb-0">{{ f[1] }}</label><textarea v-if="f[0] === 'packingList'" v-model="act[f[0]]" class="form-control form-control-sm" rows="3"></textarea><input v-else v-model="act[f[0]]" class="form-control form-control-sm" /></div>
        <div v-if="['ship', 'deliver', 'handover', 'commission'].includes(act.action)" class="mb-2"><label class="form-label small mb-0">تاریخ</label><JDate v-model="act.date" /></div>
        <div v-if="act.action === 'ship'" class="mb-2">
          <label class="form-label small mb-0">سند تحویل به مشتری انبار (تأییدشده)</label>
          <select v-model="act.inventoryDocumentId" class="form-select form-select-sm"><option :value="null">انتخاب کنید</option><option v-for="d in deliveryDocs" :key="d.id" :value="d.id">{{ d.number }} — {{ jalali(d.documentDate) }}</option></select>
          <div v-if="!deliveryDocs.length" class="small inv-muted">سند «تحویل به مشتری» تاییدشده‌ای برای این دستور ساخت نیست؛ ابتدا انبار آن را تایید کند.</div>
        </div>
        <p v-if="act.action === 'pack'" class="small mb-2" :class="(act.r.lines || []).length ? '' : 'text-danger'">{{ (act.r.lines || []).length ? `اقلام محموله: ${act.r.lines.length} ردیف` : "این محموله ردیفی ندارد؛ ابتدا با «اقلام محموله» ردیف‌ها را ثبت کنید." }}</p>
        <div v-if="act.action === 'pack'" class="mb-2"><label class="form-label small mb-0">عکس بسته‌بندی (اختیاری)</label><input type="file" class="form-control form-control-sm" accept="image/*,.pdf" @change="pickFile($event, 'photo')" /></div>
        <div v-if="act.action === 'deliver'" class="mb-2"><label class="form-label small mb-0">رسید امضاشده تحویل (الزامی)</label><input type="file" class="form-control form-control-sm" accept="image/*,.pdf" @change="pickFile($event, 'receiptFile')" /></div>
        <p v-if="actHint()" class="small text-danger mb-2">{{ actHint() }}</p>
        <p v-if="act.action === 'release'" class="small">مجوز ارسال را برنامه‌ریزی می‌دهد و مشروط به معتبر ماندن برگه ترخیص کیفی است.</p>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="act = null">انصراف</button><button :disabled="busy || !!actHint() || (act.action === 'pack' && !(act.r.lines || []).length)" class="btn btn-sm btn-primary" @click="doAct">ثبت</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
