<script setup lang="ts">
// One warehouse document: header, lines (with lot / serial entry and barcode scanning), save / confirm / cancel.
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import {
  cancelDocument, confirmDocument, deleteDocument, getContracts, getDocument, getItemByCode, getLots,
  getProviders, getSerials, getWarehouses, getWorkOrders, rowsOf, saveDocument,
} from "@/api/InventoryAPIs";
import {
  documentStatuses, documentTypes, fail, incomingTypes, jalali, labelCode, num, ok, openLabels, outgoingTypes,
  parseScan, sepidarStatuses, today, trackingModes,
} from "@/composables/inventory";

const route = useRoute();
const router = useRouter();

const doc = ref<any>(null);
const warehouses = ref<any[]>([]);
const providers = ref<any[]>([]);
const workOrders = ref<any[]>([]);
const contracts = ref<any[]>([]);
const busy = ref(false);
const scanText = ref("");
const lotOptions = ref<Record<number, any[]>>({});
const serialPicker = ref<any>(null);
const cancelReason = ref<string | null>(null);

const isNew = computed(() => !doc.value?.id);
const isDraft = computed(() => doc.value && doc.value.status === 0);
const incoming = computed(() => doc.value && incomingTypes.includes(doc.value.type));
const outgoing = computed(() => doc.value && outgoingTypes.includes(doc.value.type));
const transfer = computed(() => doc.value && doc.value.type === 6);
const needsProvider = computed(() => doc.value && [1, 5].includes(doc.value.type));
const needsWorkOrder = computed(() => doc.value && [2, 3, 4].includes(doc.value.type));

const source = computed(() => warehouses.value.find((w) => w.id === doc.value?.warehouseId));
const destination = computed(() => warehouses.value.find((w) => w.id === doc.value?.destinationWarehouseId));

function emptyDoc(type: number) {
  return {
    id: null, type, number: null, status: 0, documentDate: today(), warehouseId: null, destinationWarehouseId: null,
    providerId: null, workOrderId: null, contractId: null, buyOrderId: null, referenceNumber: "", description: "", lines: [],
  };
}

/** A line as edited on this page; `item` is the picked item record. */
function newLine(item: any = null) {
  return {
    key: Math.random().toString(36).slice(2), item, quantity: item && item.trackingMode === 2 ? 0 : 1, lotId: null,
    lotNumber: "", heatNumber: "", certificateNumber: "", locationId: null, destinationLocationId: null, unitPrice: null,
    description: "", serialText: "",
  };
}

function fromDto(d: any) {
  return {
    ...d,
    documentDate: (d.documentDate || "").slice(0, 10),
    lines: d.lines.map((l: any) => ({
      ...newLine({ id: l.itemId, code: l.itemCode, title: l.itemTitle, unit: l.unit, trackingMode: l.trackingMode }),
      quantity: l.quantity, lotId: l.lotId, lotNumber: l.lotNumber || "", heatNumber: l.heatNumber || "",
      certificateNumber: l.certificateNumber || "", locationId: l.locationId, destinationLocationId: l.destinationLocationId,
      unitPrice: l.unitPrice, description: l.description || "", serialText: (l.serialNumbers || []).join("\n"),
      locationCode: l.locationCode, destinationLocationCode: l.destinationLocationCode,
    })),
  };
}

function toDto() {
  const d = doc.value;
  return {
    id: d.id, type: d.type, documentDate: d.documentDate, warehouseId: d.warehouseId,
    destinationWarehouseId: transfer.value ? d.destinationWarehouseId : null,
    providerId: needsProvider.value ? d.providerId : null, workOrderId: d.workOrderId, contractId: d.contractId,
    buyOrderId: d.buyOrderId, referenceNumber: d.referenceNumber, description: d.description,
    lines: d.lines.filter((l: any) => l.item).map((l: any) => ({
      itemId: l.item.id, quantity: Number(l.quantity) || 0, lotId: l.item.trackingMode === 1 ? l.lotId : null,
      lotNumber: l.item.trackingMode === 1 && !l.lotId ? l.lotNumber : null, heatNumber: l.heatNumber || null,
      certificateNumber: l.certificateNumber || null, locationId: l.locationId, destinationLocationId: transfer.value ? l.destinationLocationId : null,
      unitPrice: l.unitPrice === "" ? null : l.unitPrice, description: l.description,
      serialNumbers: l.item.trackingMode === 2 ? serials(l) : [],
    })),
  };
}

function serials(line: any): string[] {
  return (line.serialText || "").split(/[\n,،]+/).map((s: string) => s.trim()).filter(Boolean);
}

function syncSerialQty(line: any) {
  if (line.item && line.item.trackingMode === 2) line.quantity = serials(line).length;
}

async function loadLots(line: any) {
  if (!line.item || line.item.trackingMode !== 1 || lotOptions.value[line.item.id]) return;
  const [data] = await getLots({ ItemId: line.item.id, PageSize: 200 });
  lotOptions.value[line.item.id] = rowsOf(data);
}

function onItem(line: any) {
  line.lotId = null;
  line.serialText = "";
  line.quantity = line.item && line.item.trackingMode === 2 ? 0 : 1;
  loadLots(line);
}

function addLine() {
  doc.value.lines.push(newLine());
}

function removeLine(i: number) {
  doc.value.lines.splice(i, 1);
}

/** A scanned label adds to the document: item → +1, serial → that piece, lot → a line for that lot, location → last line. */
async function onScan() {
  const scan = parseScan(scanText.value);
  scanText.value = "";
  if (scan.kind === "location") {
    const last = doc.value.lines[doc.value.lines.length - 1];
    const wh = transfer.value && destination.value?.code === scan.warehouseCode ? destination.value : source.value;
    const loc = wh?.locations.find((l: any) => l.code === scan.value);
    if (!last || !loc) return fail("این محل در انبار سند نیست یا ردیفی وجود ندارد.");
    if (wh === destination.value) last.destinationLocationId = loc.id;
    else last.locationId = loc.id;
    return;
  }
  const code = scan.itemCode || scan.text;
  if (!code) return;
  const [item, error] = await getItemByCode(code);
  if (error || !item) return fail(`کالای ${code} پیدا نشد.`);
  if (scan.kind === "serial") {
    let line = doc.value.lines.find((l: any) => l.item && l.item.id === item.id);
    if (!line) {
      line = newLine(item);
      doc.value.lines.push(line);
    }
    if (serials(line).includes(scan.value)) return fail(`سریال ${scan.value} قبلاً اضافه شده است.`);
    line.serialText = [...serials(line), scan.value].join("\n");
    syncSerialQty(line);
    return;
  }
  if (scan.kind === "lot") {
    const line = newLine(item);
    doc.value.lines.push(line);
    await loadLots(line);
    const lot = (lotOptions.value[item.id] || []).find((x: any) => x.lotNumber === scan.value);
    if (lot && !incoming.value) line.lotId = lot.id;
    else line.lotNumber = scan.value || "";
    return;
  }
  const same = doc.value.lines.find((l: any) => l.item && l.item.id === item.id && item.trackingMode === 0);
  if (same) same.quantity = Number(same.quantity) + 1;
  else {
    const line = newLine(item);
    doc.value.lines.push(line);
    loadLots(line);
  }
}

async function openSerialPicker(line: any) {
  const [data, error] = await getSerials({ ItemId: line.item.id, WarehouseId: doc.value.warehouseId, Status: 2, PageSize: 500 });
  if (error) return fail(error);
  const chosen = serials(line);
  serialPicker.value = { line, rows: rowsOf(data).map((s: any) => ({ ...s, checked: chosen.includes(s.serialNumber) })) };
}

function applySerialPicker() {
  const p = serialPicker.value;
  p.line.serialText = p.rows.filter((r: any) => r.checked).map((r: any) => r.serialNumber).join("\n");
  syncSerialQty(p.line);
  serialPicker.value = null;
}

async function save(silent = false): Promise<boolean> {
  if (!doc.value.warehouseId) {
    fail("انبار را انتخاب کنید.");
    return false;
  }
  busy.value = true;
  const [data, error] = await saveDocument(toDto());
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  doc.value = fromDto(data);
  if (!silent) ok("پیش‌نویس ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "InventoryDocumentEdit", params: { id: data.id } });
  return true;
}

async function confirmDoc() {
  if (!confirm("سند تایید شود؟ پس از تایید، موجودی تغییر می‌کند و سند فقط با ابطال برمی‌گردد.")) return;
  if (!(await save(true))) return;
  busy.value = true;
  const [data, error] = await confirmDocument(doc.value.id);
  busy.value = false;
  if (error) return fail(error);
  doc.value = fromDto(data);
  ok(`سند ${data.number} تایید شد.`);
}

async function cancelDoc() {
  const reason = (cancelReason.value || "").trim();
  if (!reason) return fail("دلیل ابطال را بنویسید.");
  busy.value = true;
  const [data, error] = await cancelDocument(doc.value.id, reason);
  busy.value = false;
  if (error) return fail(error);
  cancelReason.value = null;
  doc.value = fromDto(data);
  ok("سند باطل شد و موجودی برگشت.");
}

async function removeDoc() {
  if (!confirm("پیش‌نویس حذف شود؟")) return;
  const [, error] = await deleteDocument(doc.value.id);
  if (error) return fail(error);
  ok("حذف شد.");
  router.push({ name: "InventoryDocuments" });
}

function printLabels() {
  const labels: any[] = [];
  doc.value.lines.forEach((l: any) => {
    if (!l.item) return;
    if (l.item.trackingMode === 2) {
      serials(l).forEach((s) => labels.push({ code: labelCode.serial(l.item.code, s), title: l.item.title, lines: [l.item.code, "S/N " + s] }));
    } else if (l.item.trackingMode === 1) {
      const lot = l.lotNumber || (lotOptions.value[l.item.id] || []).find((x: any) => x.id === l.lotId)?.lotNumber;
      if (lot) labels.push({ code: labelCode.lot(l.item.code, lot), title: l.item.title, lines: [l.item.code, "Lot " + lot, l.heatNumber ? "Heat " + l.heatNumber : ""].filter(Boolean) });
    } else {
      labels.push({ code: labelCode.item(l.item.code), title: l.item.title, lines: [l.item.code] });
    }
  });
  if (!labels.length) return fail("برچسبی برای چاپ نیست.");
  openLabels(labels, router);
}

function printDoc() {
  window.print();
}

const signatures = computed(() => {
  const t = doc.value?.type;
  if (t === 1) return ["تحویل‌دهنده (تامین‌کننده)", "انباردار", "کنترل کیفیت", "تایید مالی"];
  if (t === 2 || t === 3) return ["تحویل‌دهنده (تولید)", "انباردار", "کنترل کیفیت"];
  if (t === 4) return ["درخواست‌کننده", "تحویل‌گیرنده", "انباردار", "مدیر تولید"];
  if (t === 5) return ["انباردار", "تحویل‌گیرنده (تامین‌کننده)", "تایید بازرگانی"];
  if (t === 6) return ["انبار مبدا", "حمل‌کننده", "انبار مقصد"];
  return ["تهیه‌کننده", "انباردار", "تایید مدیر"];
});

function lotNumberOf(l: any): string {
  if (!l.item || l.item.trackingMode !== 1) return "";
  return l.lotNumber || (lotOptions.value[l.item.id] || []).find((x: any) => x.id === l.lotId)?.lotNumber || "";
}

function locationText(l: any): string {
  const src = (source.value?.locations || []).find((x: any) => x.id === l.locationId)?.code || l.locationCode || "";
  const dst = (destination.value?.locations || []).find((x: any) => x.id === l.destinationLocationId)?.code || l.destinationLocationCode || "";
  return transfer.value ? [src, dst].filter(Boolean).join(" ← ") : src;
}

const totalAmount = computed(() =>
  (doc.value?.lines || []).reduce((s: number, l: any) => s + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0), 0)
);

const totalQty = computed(() => (doc.value?.lines || []).reduce((s: number, l: any) => s + (Number(l.quantity) || 0), 0));

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = (w || []).filter((x: any) => x.isActive);
  getProviders().then(([p]) => (providers.value = rowsOf(p)));
  getWorkOrders().then(([p]) => (workOrders.value = rowsOf(p)));
  getContracts().then(([p]) => (contracts.value = rowsOf(p)));
  if (route.params.id === "new") {
    doc.value = emptyDoc(Number(route.query.type) || 1);
    if (warehouses.value.length === 1) doc.value.warehouseId = warehouses.value[0].id;
    addLine();
  } else {
    const [data, error] = await getDocument(Number(route.params.id));
    if (error) return fail(error);
    doc.value = fromDto(data);
    doc.value.lines.forEach(loadLots);
  }
});
</script>

<template>
  <div v-if="doc" class="inv-page">
    <!-- printed form of the document -->
    <div class="print-only ks-report">
      <KsReportHeader :code="doc.number || 'پیش‌نویس'" :subtitle="documentStatuses[doc.status]" :print-only="false" :title="documentTypes[doc.type]" />
      <table class="mb-3">
        <tbody>
          <tr>
            <td><b>تاریخ:</b> {{ jalali(doc.documentDate) }}</td>
            <td><b>{{ transfer ? "انبار مبدا" : "انبار" }}:</b> {{ source?.title || doc.warehouseTitle }}</td>
            <td v-if="transfer"><b>انبار مقصد:</b> {{ destination?.title || doc.destinationWarehouseTitle }}</td>
            <td v-if="needsProvider"><b>تامین‌کننده:</b> {{ providers.find((p) => p.id === doc.providerId)?.name || doc.providerName || "-" }}</td>
          </tr>
          <tr>
            <td><b>دستور ساخت:</b> {{ workOrders.find((w) => w.id === doc.workOrderId)?.code || doc.workOrderCode || "-" }}</td>
            <td><b>قرارداد:</b> {{ contracts.find((c) => c.id === doc.contractId)?.code || doc.contractCode || "-" }}</td>
            <td><b>شماره مرجع:</b> <span class="inv-ltr">{{ doc.referenceNumber || "-" }}</span></td>
            <td v-if="transfer || needsProvider"></td>
          </tr>
        </tbody>
      </table>
      <table>
        <thead>
          <tr>
            <th>ردیف</th><th>کد کالا</th><th>شرح کالا</th><th>واحد</th><th>مقدار</th><th>بچ / ذوب / گواهی</th><th>سریال‌ها</th>
            <th>{{ transfer ? "محل مبدا ← مقصد" : "محل" }}</th>
            <th v-if="incoming">فی (ریال)</th><th v-if="incoming">مبلغ (ریال)</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in doc.lines.filter((x) => x.item)" :key="l.key">
            <td>{{ i + 1 }}</td>
            <td class="inv-ltr">{{ l.item.code }}</td>
            <td>{{ l.item.title }}<div v-if="l.description" class="small">{{ l.description }}</div></td>
            <td>{{ l.item.unit }}</td>
            <td class="num">{{ num(l.quantity) }}</td>
            <td>
              <div v-if="lotNumberOf(l)">بچ <span class="inv-ltr">{{ lotNumberOf(l) }}</span></div>
              <div v-if="l.heatNumber">ذوب <span class="inv-ltr">{{ l.heatNumber }}</span></div>
              <div v-if="l.certificateNumber">گواهی <span class="inv-ltr">{{ l.certificateNumber }}</span></div>
            </td>
            <td><div v-for="sn in serials(l)" :key="sn" class="inv-ltr">{{ sn }}</div></td>
            <td class="inv-ltr">{{ locationText(l) }}</td>
            <td v-if="incoming" class="num">{{ num(l.unitPrice) }}</td>
            <td v-if="incoming" class="num">{{ l.unitPrice ? num(l.quantity * l.unitPrice) : "" }}</td>
          </tr>
        </tbody>
        <tfoot>
          <tr>
            <td colspan="4">جمع</td>
            <td class="num">{{ num(totalQty) }}</td>
            <td :colspan="incoming ? 4 : 3"></td>
            <td v-if="incoming" class="num">{{ totalAmount ? num(totalAmount) : "" }}</td>
          </tr>
        </tfoot>
      </table>
      <p v-if="doc.description" class="mt-2"><b>توضیح:</b> {{ doc.description }}</p>
      <p v-if="doc.status === 2" class="mt-2"><b>باطل‌شده:</b> {{ jalali(doc.cancelledAt, true) }} — {{ doc.cancelReason }}</p>
      <p v-if="doc.confirmedAt" class="small mt-2">ثبت: {{ doc.creatorName }} {{ jalali(doc.createDate, true) }} · تایید: {{ doc.confirmedByName }} {{ jalali(doc.confirmedAt, true) }}</p>
      <div :style="`--cols: ${signatures.length}`" class="ks-signatures">
        <div v-for="s in signatures" :key="s">{{ s }}</div>
      </div>
    </div>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">
            {{ documentTypes[doc.type] }}
            <span v-if="doc.number" class="inv-ltr ms-2">{{ doc.number }}</span>
          </h1>
          <span class="inv-badge me-1" :class="{ warn: doc.status === 0, ok: doc.status === 1, bad: doc.status === 2 }">{{ documentStatuses[doc.status] }}</span>
          <span v-if="doc.status > 0" class="inv-badge">سپیدار: {{ sepidarStatuses[doc.sepidarStatus] }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <router-link :to="{ name: 'InventoryDocuments' }" class="btn btn-sm btn-outline-secondary">فهرست اسناد</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printLabels">چاپ برچسب</button>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="printDoc">چاپ سند</button>
          <template v-if="isDraft">
            <button v-if="!isNew" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="removeDoc">حذف پیش‌نویس</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save()">ذخیره پیش‌نویس</button>
            <button :disabled="busy" class="btn btn-sm btn-success" @click="confirmDoc">تایید سند</button>
          </template>
          <button v-if="doc.status === 1" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="cancelReason = ''">ابطال</button>
        </div>
      </div>

      <div v-if="doc.status === 2" class="alert alert-danger small py-2">
        باطل‌شده {{ jalali(doc.cancelledAt, true) }} توسط {{ doc.cancelledByName }} — دلیل: {{ doc.cancelReason }}
      </div>

      <div class="row g-2">
        <div v-if="isNew" class="col-md-3">
          <label class="form-label small mb-0">نوع سند</label>
          <select v-model="doc.type" class="form-select form-select-sm">
            <option v-for="(t, k) in documentTypes" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="doc.documentDate" :disabled="!isDraft" class="form-control form-control-sm" type="date" />
          <span class="inv-muted">{{ jalali(doc.documentDate) }}</span>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">{{ transfer ? "انبار مبدا" : "انبار" }} *</label>
          <select v-model="doc.warehouseId" :disabled="!isDraft" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
          </select>
        </div>
        <div v-if="transfer" class="col-md-3">
          <label class="form-label small mb-0">انبار مقصد *</label>
          <select v-model="doc.destinationWarehouseId" :disabled="!isDraft" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
          </select>
        </div>
        <div v-if="needsProvider" class="col-md-3">
          <label class="form-label small mb-0">تامین‌کننده</label>
          <select v-model="doc.providerId" :disabled="!isDraft" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">دستور ساخت{{ needsWorkOrder ? "" : " (اختیاری)" }}</label>
          <select v-model="doc.workOrderId" :disabled="!isDraft" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">قرارداد / پروژه</label>
          <select v-model="doc.contractId" :disabled="!isDraft" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="c in contracts" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">شماره مرجع</label>
          <input v-model="doc.referenceNumber" :disabled="!isDraft" class="form-control form-control-sm" placeholder="سفارش، بارنامه..." />
        </div>
        <div class="col-md-12">
          <label class="form-label small mb-0">توضیح</label>
          <input v-model="doc.description" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
      </div>
    </div>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">ردیف‌ها</h2>
        <div v-if="isDraft" class="d-flex gap-2 align-items-center d-print-none">
          <input
            v-model="scanText"
            class="form-control form-control-sm inv-scan"
            placeholder="اسکن بارکد / QR"
            style="width: 240px"
            @keydown.enter.prevent="onScan" />
          <button class="btn btn-sm btn-outline-primary" @click="addLine">+ ردیف</button>
        </div>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-bordered">
          <thead>
            <tr>
              <th style="width: 2rem">#</th>
              <th style="min-width: 240px">کالا</th>
              <th style="width: 7rem">مقدار</th>
              <th style="min-width: 200px">بچ / سریال</th>
              <th style="min-width: 110px">{{ transfer ? "محل مبدا" : "محل" }}</th>
              <th v-if="transfer" style="min-width: 110px">محل مقصد</th>
              <th v-if="incoming" style="width: 8rem">فی (ریال)</th>
              <th>توضیح</th>
              <th v-if="isDraft" class="d-print-none"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(line, i) in doc.lines" :key="line.key">
              <td>{{ i + 1 }}</td>
              <td>
                <ItemPicker v-if="isDraft" v-model="line.item" @select="onItem(line)" />
                <span v-else><span class="inv-ltr">{{ line.item?.code }}</span> — {{ line.item?.title }}</span>
                <div v-if="line.item" class="inv-muted">{{ trackingModes[line.item.trackingMode] }} · {{ line.item.unit }}</div>
              </td>
              <td>
                <input
                  v-model.number="line.quantity"
                  :disabled="!isDraft || (line.item && line.item.trackingMode === 2)"
                  class="form-control form-control-sm"
                  min="0"
                  step="any"
                  type="number" />
              </td>
              <td>
                <template v-if="line.item && line.item.trackingMode === 1">
                  <template v-if="incoming">
                    <select v-if="isDraft && (lotOptions[line.item.id] || []).length" v-model="line.lotId" class="form-select form-select-sm mb-1">
                      <option :value="null">بچ جدید ↓</option>
                      <option v-for="lot in lotOptions[line.item.id]" :key="lot.id" :value="lot.id">{{ lot.lotNumber }} ({{ lot.heatNumber }})</option>
                    </select>
                    <template v-if="!line.lotId">
                      <input v-model="line.lotNumber" :disabled="!isDraft" class="form-control form-control-sm inv-ltr mb-1" placeholder="شماره بچ *" />
                      <input v-model="line.heatNumber" :disabled="!isDraft" class="form-control form-control-sm inv-ltr mb-1" placeholder="شماره ذوب (Heat)" />
                      <input v-model="line.certificateNumber" :disabled="!isDraft" class="form-control form-control-sm inv-ltr" placeholder="شماره گواهی (MTC)" />
                    </template>
                  </template>
                  <select v-else v-model="line.lotId" :disabled="!isDraft" class="form-select form-select-sm">
                    <option :value="null">انتخاب بچ *</option>
                    <option v-for="lot in lotOptions[line.item.id]" :key="lot.id" :value="lot.id">
                      {{ lot.lotNumber }} — موجودی {{ num(lot.onHand) }}
                    </option>
                  </select>
                  <span v-if="!isDraft && line.lotNumber" class="inv-ltr">{{ line.lotNumber }}</span>
                </template>
                <template v-else-if="line.item && line.item.trackingMode === 2">
                  <textarea
                    v-model="line.serialText"
                    :disabled="!isDraft"
                    class="form-control form-control-sm inv-ltr"
                    placeholder="هر سریال در یک خط"
                    rows="2"
                    @input="syncSerialQty(line)"></textarea>
                  <button v-if="isDraft && !incoming" class="btn btn-sm btn-link p-0" @click="openSerialPicker(line)">انتخاب از موجودی</button>
                </template>
                <span v-else class="inv-muted">-</span>
              </td>
              <td>
                <select v-if="isDraft" v-model="line.locationId" class="form-select form-select-sm">
                  <option :value="null">{{ incoming ? "بدون محل" : "خودکار" }}</option>
                  <option v-for="l in source?.locations || []" :key="l.id" :value="l.id">{{ l.code }}</option>
                </select>
                <span v-else class="inv-ltr">{{ line.locationCode }}</span>
              </td>
              <td v-if="transfer">
                <select v-if="isDraft" v-model="line.destinationLocationId" class="form-select form-select-sm">
                  <option :value="null">بدون محل</option>
                  <option v-for="l in destination?.locations || []" :key="l.id" :value="l.id">{{ l.code }}</option>
                </select>
                <span v-else class="inv-ltr">{{ line.destinationLocationCode }}</span>
              </td>
              <td v-if="incoming"><input v-model.number="line.unitPrice" :disabled="!isDraft" class="form-control form-control-sm" type="number" /></td>
              <td><input v-model="line.description" :disabled="!isDraft" class="form-control form-control-sm" /></td>
              <td v-if="isDraft" class="d-print-none"><button class="btn btn-sm btn-link text-danger p-0" @click="removeLine(i)">×</button></td>
            </tr>
          </tbody>
          <tfoot>
            <tr><td></td><td class="text-end small">جمع مقدار</td><td>{{ num(totalQty) }}</td><td :colspan="10"></td></tr>
          </tfoot>
        </table>
      </div>
      <p v-if="isDraft" class="inv-muted mb-0 d-print-none">
        با اسکن برچسب کالا (I/…) یک واحد، برچسب سریال (S/…) همان قطعه، برچسب بچ (L/…) یک ردیف بچ، و برچسب محل (LOC/…) محل ردیف آخر ثبت می‌شود.
        موجودی فقط با «تایید سند» تغییر می‌کند.
      </p>
      <div v-if="doc.status > 0" class="inv-muted mt-2">
        ثبت: {{ doc.creatorName }} {{ jalali(doc.createDate, true) }}
        <span v-if="doc.confirmedAt"> · تایید: {{ doc.confirmedByName }} {{ jalali(doc.confirmedAt, true) }}</span>
      </div>
    </div>

    <!-- serial picker -->
    <div v-if="serialPicker" class="inv-modal-backdrop" @click.self="serialPicker = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">سریال‌های موجود {{ serialPicker.line.item.code }} در {{ source?.title }}</h2>
        <div style="max-height: 360px; overflow-y: auto">
          <div v-if="!serialPicker.rows.length" class="inv-muted">قطعه‌ای در این انبار نیست.</div>
          <div v-for="s in serialPicker.rows" :key="s.id" class="form-check">
            <input :id="'sp' + s.id" v-model="s.checked" class="form-check-input" type="checkbox" />
            <label :for="'sp' + s.id" class="form-check-label small">
              <span class="inv-ltr">{{ s.serialNumber }}</span>
              <span class="inv-muted"> {{ s.locationCode }} {{ s.lotNumber ? "· بچ " + s.lotNumber : "" }}</span>
            </label>
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="serialPicker = null">انصراف</button>
          <button class="btn btn-sm btn-primary" @click="applySerialPicker">انتخاب</button>
        </div>
      </div>
    </div>

    <!-- cancel -->
    <div v-if="cancelReason !== null" class="inv-modal-backdrop" @click.self="cancelReason = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ابطال سند {{ doc.number }}</h2>
        <p class="small">موجودی با سند برگشت اصلاح می‌شود و اگر سند به سپیدار رفته باشد، برگشت آن هم برای سپیدار ارسال می‌شود.</p>
        <textarea v-model="cancelReason" class="form-control form-control-sm" placeholder="دلیل ابطال *" rows="3"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="cancelReason = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-danger" @click="cancelDoc">ابطال سند</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
