<script setup lang="ts">
// One warehouse document: header, lines (with lot / serial entry and barcode scanning), save / confirm / cancel.
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import PrintForm from "@/components/print/PrintForm.vue";
import {
  approveDocument, cancelDocument, confirmDocument, deleteDocument, downloadFile, exitCheckDocument, getContracts, getDocument,
  getItemByCode, getLots, getMaterialRequest, getMaterialRequests, getOpenPurchaseOrders, getProviders, getPurchaseOrderForReceipt,
  getSerials, getWarehouses, getWorkOrders, rowsOf, saveDocument, uploadFile,
} from "@/api/InventoryAPIs";
import {
  documentStatuses, documentTypes, fail, formCodes, incomingTypes, jalali, labelCode, leavesPremisesTypes, lineDirections,
  needsApprovalTypes, num, ok, openLabels, outgoingTypes, parseScan, printPage, purchaseOrderTypes, rial, sepidarStatuses,
  transferTypes, today, trackingModes, typeOrder, warehouseKinds, distributionText,
} from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

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

const exitNote = ref<string | null>(null);
const orders = ref<any[]>([]);
const requests = ref<any[]>([]);
const orderLines = ref<any[]>([]);
const printMode = ref<"main" | "minutes">("main");
const quotes = ref<{ buyer: string; amount: number | null }[]>([]);

const isNew = computed(() => !doc.value?.id);
const isDraft = computed(() => doc.value && doc.value.status === 0);
const type = computed<number>(() => doc.value?.type);
const incoming = computed(() => doc.value && incomingTypes.includes(type.value));
const transfer = computed(() => doc.value && transferTypes.includes(type.value));
const conversion = computed(() => type.value === 12);
const leaves = computed(() => leavesPremisesTypes.includes(type.value));
const scrapSale = computed(() => type.value === 14);
const needsApprovals = computed(() => needsApprovalTypes.includes(type.value));
const customerDelivery = computed(() => type.value === 11);
const issue = computed(() => type.value === 4);
const usesOrder = computed(() => purchaseOrderTypes.includes(type.value));
const needsProvider = computed(() => [1, 5, 9, 10].includes(type.value));
const needsWorkOrder = computed(() => [2, 3].includes(type.value));
const needsContract = computed(() => [11, 17].includes(type.value));
const fromSystem = computed(() => doc.value && (doc.value.ncrId || doc.value.inspectionId || doc.value.stockCountId));

const source = computed(() => warehouses.value.find((w) => w.id === doc.value?.warehouseId));
const destination = computed(() => warehouses.value.find((w) => w.id === doc.value?.destinationWarehouseId));
// a receipt into a quarantine warehouse is released to a destination after the inspection
const quarantineReceipt = computed(() => incoming.value && source.value?.kind === 5);
const hasDestination = computed(() => transfer.value || conversion.value || quarantineReceipt.value);
const destinationLabel = computed(() =>
  quarantineReceipt.value ? "انبار مقصد پس از بازرسی" : conversion.value ? "انبار مرحله بعد (خالی = همین انبار)" : "انبار مقصد"
);
const pettyCash = computed(() => type.value === 1 && !doc.value?.purchaseOrderId);
const orderLocked = computed(() => !!doc.value?.purchaseOrderId);

const canEdit = hasRole(InventoryRoles.documents);
const canApproveWarehouse = hasRole(InventoryRoles.approveWarehouse);
const canApprovePlanning = hasRole(InventoryRoles.approvePlanning);
const canApproveQuality = hasRole(InventoryRoles.approveQuality);
const canApproveFinance = hasRole(InventoryRoles.finance);
const canExitCheck = hasRole(InventoryRoles.exitCheck);

/** Lines that bring stock in: a plain receipt, or the "next stage" side of a stage conversion. */
function lineIncoming(line: any): boolean {
  return incoming.value || (conversion.value && line.direction === 2);
}

function locationsFor(line: any): any[] {
  return conversion.value && line.direction === 2 ? (destination.value || source.value)?.locations || [] : source.value?.locations || [];
}

function emptyDoc(type: number) {
  return {
    id: null, type, number: null, status: 0, documentDate: today(), warehouseId: null, destinationWarehouseId: null,
    providerId: null, workOrderId: null, contractId: null, buyOrderId: null, purchaseOrderId: null, salesOrderId: null as number | null, serviceOrderId: null as number | null, referenceNumber: "", description: "", lines: [],
    materialRequestId: null, driverName: "", vehiclePlate: "", receiverName: "", destination: "", buyerName: "", minutesNumber: "",
    weighbridgeDocumentId: null, depositReference: "", priceQuotes: "",
  };
}

/** A line as edited on this page; `item` is the picked item record. */
function newLine(item: any = null) {
  return {
    key: Math.random().toString(36).slice(2), item, quantity: item && item.trackingMode === 2 ? 0 : 1, lotId: null,
    lotNumber: "", heatNumber: "", certificateNumber: "", locationId: null, destinationLocationId: null, unitPrice: null,
    description: "", serialText: "", direction: 0, sourceRowNumber: null as number | null, purchaseOrderLineId: null as number | null,
  };
}

function setDoc(dto: any) {
  doc.value = fromDto(dto);
  parseQuotes(dto.priceQuotes);
  doc.value.lines.forEach(loadLots);
  if (doc.value.purchaseOrderId && doc.value.status === 0) loadOrderLines();
}

async function loadOrderLines() {
  const [order] = await getPurchaseOrderForReceipt(doc.value.purchaseOrderId);
  orderLines.value = (order?.lines || []).filter((l: any) => l.itemId);
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
      direction: l.direction || 0, sourceRowNumber: l.sourceRowNumber, purchaseOrderLineId: l.purchaseOrderLineId, salesOrderLineId: l.salesOrderLineId ?? null,
    })),
  };
}

function toDto() {
  const d = doc.value;
  return {
    id: d.id, type: d.type, documentDate: d.documentDate, warehouseId: d.warehouseId,
    destinationWarehouseId: hasDestination.value ? d.destinationWarehouseId : null,
    providerId: needsProvider.value ? d.providerId : null, workOrderId: d.workOrderId, contractId: d.contractId,
    buyOrderId: d.buyOrderId, purchaseOrderId: usesOrder.value ? d.purchaseOrderId : null,
    // sales and after-sales links (S1): kept on every save so a delivery or issue never loses its order
    salesOrderId: d.salesOrderId ?? null, serviceOrderId: d.serviceOrderId ?? null,
    referenceNumber: d.referenceNumber, description: d.description,
    materialRequestId: issue.value ? d.materialRequestId : null,
    driverName: d.driverName, vehiclePlate: d.vehiclePlate, receiverName: d.receiverName, destination: d.destination,
    buyerName: d.buyerName, minutesNumber: d.minutesNumber, weighbridgeDocumentId: d.weighbridgeDocumentId || null,
    depositReference: d.depositReference, priceQuotes: scrapSale.value ? quotesText() : null,
    lines: d.lines.filter((l: any) => l.item).map((l: any) => ({
      direction: conversion.value ? l.direction : 0,
      sourceRowNumber: conversion.value && l.direction === 2 ? l.sourceRowNumber : null,
      purchaseOrderLineId: usesOrder.value && d.purchaseOrderId ? l.purchaseOrderLineId : null,
      salesOrderLineId: d.salesOrderId ? l.salesOrderLineId ?? null : null,
      itemId: l.item.id, quantity: Number(l.quantity) || 0, lotId: l.item.trackingMode === 1 ? l.lotId : null,
      lotNumber: l.item.trackingMode === 1 && !l.lotId ? l.lotNumber : null, heatNumber: l.heatNumber || null,
      certificateNumber: l.certificateNumber || null, locationId: l.locationId, destinationLocationId: transfer.value ? l.destinationLocationId : null,
      unitPrice: l.unitPrice === "" ? null : l.unitPrice, description: l.description,
      serialNumbers: l.item.trackingMode === 2 ? serials(l) : [],
    })),
  };
}

/** Price quotes of the scrap sale are kept as text: "buyer: amount | buyer: amount | ...". */
function quotesText(): string {
  return quotes.value
    .filter((q) => q.buyer.trim() || q.amount)
    .map((q) => `${q.buyer.trim()}: ${q.amount ?? ""}`)
    .join(" | ");
}

function parseQuotes(text: string | null) {
  quotes.value = (text || "")
    .split(/[|\n؛]/)
    .map((x) => x.trim())
    .filter(Boolean)
    .map((x) => {
      const k = x.lastIndexOf(":");
      return k < 0 ? { buyer: x, amount: null } : { buyer: x.slice(0, k).trim(), amount: Number(x.slice(k + 1).replace(/[,٬\s]/g, "")) || null };
    });
  while (quotes.value.length < 3) quotes.value.push({ buyer: "", amount: null });
}

const quoteCount = computed(() => quotes.value.filter((q) => q.buyer.trim() && q.amount).length);

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
  setDoc(data);
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
  setDoc(data);
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
  setDoc(data);
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

function onTypeChange() {
  if (!doc.value) return;
  const lines = doc.value.lines;
  if (type.value === 12) {
    lines.forEach((l: any) => (l.direction = l.direction || 1));
    if (!lines.some((l: any) => l.direction === 2)) addConversionLine(2);
  } else {
    lines.forEach((l: any) => {
      l.direction = 0;
      l.sourceRowNumber = null;
    });
    doc.value.purchaseOrderId = null;
  }
}

async function printForm(mode: "main" | "minutes" = "main") {
  printMode.value = mode;
  await nextTick();
  printPage();
}

async function approve(kind: number) {
  if (isDraft.value && !(await save(true))) return;
  busy.value = true;
  const [data, error] = await approveDocument(doc.value.id, kind);
  busy.value = false;
  if (error) return fail(error);
  setDoc(data);
  ok("تایید ثبت شد.");
}

async function exitCheck() {
  busy.value = true;
  const [data, error] = await exitCheckDocument(doc.value.id, exitNote.value || "");
  busy.value = false;
  if (error) return fail(error);
  exitNote.value = null;
  setDoc(data);
  ok("خروج کالا از شرکت ثبت شد.");
}

async function uploadWeighbridge(event: any) {
  const input = event.target;
  const file = input.files && input.files[0];
  if (!file) return;
  busy.value = true;
  const [id, error] = await uploadFile(file, "قبض باسکول " + (doc.value.number || ""));
  busy.value = false;
  input.value = "";
  if (error) return fail(error);
  doc.value.weighbridgeDocumentId = id;
  ok("تصویر قبض باسکول بارگذاری شد؛ سند را ذخیره کنید.");
}

/** Items of an order line come without the tracking mode, so the item is read again by its code. */
async function fillFromOrder() {
  if (!doc.value.purchaseOrderId) return;
  busy.value = true;
  const [order, error] = await getPurchaseOrderForReceipt(doc.value.purchaseOrderId);
  if (error) {
    busy.value = false;
    return fail(error);
  }
  doc.value.providerId = order.providerId;
  doc.value.workOrderId = doc.value.workOrderId || order.workOrderId;
  doc.value.contractId = doc.value.contractId || order.contractId;
  orderLines.value = (order.lines || []).filter((l: any) => l.itemId);
  const lines: any[] = [];
  for (const ol of orderLines.value) {
    const open = type.value === 1 ? ol.openQuantity : ol.receivedQuantity;
    if (!(open > 0)) continue;
    const [item] = await getItemByCode(ol.itemCode);
    if (!item) continue;
    const line = newLine(item);
    line.quantity = item.trackingMode === 2 ? 0 : open;
    line.unitPrice = ol.unitPrice;
    line.purchaseOrderLineId = ol.id;
    line.description = ol.description || "";
    lines.push(line);
    loadLots(line);
  }
  busy.value = false;
  if (!lines.length) return fail(type.value === 1 ? "همه اقلام این سفارش دریافت شده است." : "از این سفارش چیزی دریافت نشده است.");
  doc.value.lines = lines;
}

async function onOrderChange() {
  orderLines.value = [];
  if (!doc.value.purchaseOrderId) {
    doc.value.lines.forEach((l: any) => (l.purchaseOrderLineId = null));
    return;
  }
  await fillFromOrder();
}

/** Lines of the chosen order that match the item of a line. */
function orderLinesOf(line: any): any[] {
  return orderLines.value.filter((ol) => line.item && ol.itemCode === line.item.code);
}

function onOrderLine(line: any) {
  const ol = orderLines.value.find((x) => x.id === line.purchaseOrderLineId);
  if (ol && type.value === 1) line.unitPrice = ol.unitPrice;
}

async function fillFromRequest() {
  if (!doc.value.materialRequestId) return fail("درخواست کالا را انتخاب کنید.");
  const [req, error] = await getMaterialRequest(doc.value.materialRequestId);
  if (error) return fail(error);
  doc.value.workOrderId = req.workOrderId;
  if (req.warehouseId && !doc.value.warehouseId) doc.value.warehouseId = req.warehouseId;
  doc.value.lines = req.lines
    .filter((r: any) => r.remaining > 0)
    .map((r: any) => {
      const line = newLine({ id: r.itemId, code: r.itemCode, title: r.itemTitle, unit: r.unit, trackingMode: r.trackingMode });
      if (r.trackingMode !== 2) line.quantity = r.remaining;
      loadLots(line);
      return line;
    });
  if (!doc.value.lines.length) fail("همه اقلام این درخواست حواله شده است.");
}

/** Stage conversion: the rows an "in" row can come from. */
function addConversionLine(direction: number) {
  const line = newLine();
  line.direction = direction;
  doc.value.lines.push(line);
}

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

const partyName = computed(() => {
  const d = doc.value;
  return providers.value.find((p) => p.id === d?.providerId)?.name || d?.providerName || "";
});

/** Short fields of the printed form (the same box as the company's receipt / issue / delivery forms). */
const printFields = computed(() => {
  const d = doc.value;
  if (!d) return [];
  const list: any[] = [
    { label: "شماره", value: d.number || "پیش‌نویس", ltr: true },
    { label: "تاریخ", value: jalali(d.documentDate) },
    { label: "زمان ثبت", value: jalali(d.createDate, true) },
    { label: transfer.value ? "انبار مبدا" : "انبار", value: source.value?.title || d.warehouseTitle || "-" },
  ];
  if (hasDestination.value) list.push({ label: destinationLabel.value, value: destination.value?.title || d.destinationWarehouseTitle || "-" });
  if (needsProvider.value) list.push({ label: [9, 10].includes(d.type) ? "پیمانکار" : "تامین‌کننده", value: partyName.value || "-" });
  if (d.purchaseOrderNumber) list.push({ label: "سفارش خرید", value: d.purchaseOrderNumber, ltr: true });
  else if (pettyCash.value) list.push({ label: "نوع خرید", value: "خرید تنخواه (بدون سفارش خرید)" });
  if (d.materialRequestNumber) list.push({ label: "شماره درخواست کالا", value: d.materialRequestNumber, ltr: true });
  if (d.ncrNumber) list.push({ label: "گزارش عدم انطباق", value: d.ncrNumber, ltr: true });
  list.push({ label: "شماره مرجع", value: d.referenceNumber || "-", ltr: true });
  list.push({ label: "دستور ساخت", value: workOrders.value.find((w) => w.id === d.workOrderId)?.code || d.workOrderCode || "-" });
  list.push({ label: "پروژه / قرارداد", value: contracts.value.find((c) => c.id === d.contractId)?.code || d.contractCode || "-" });
  if (leaves.value) {
    list.push({ label: "راننده", value: d.driverName || "-" });
    list.push({ label: "پلاک", value: d.vehiclePlate || "-" });
    list.push({ label: "تحویل‌گیرنده", value: d.receiverName || "-" });
  }
  if (scrapSale.value) {
    list.push({ label: "خریدار", value: d.buyerName || "-" });
    list.push({ label: "صورت‌جلسه فروش (FO-WH-020)", value: d.minutesNumber || "-" });
    list.push({ label: "واریز به حساب شرکت", value: d.depositReference || "-" });
  }
  return list;
});

const printNotes = computed(() => {
  const d = doc.value;
  const list: any[] = [];
  if (leaves.value && d.destination) list.push({ label: "مقصد / آدرس", value: d.destination });
  if (d.description) list.push({ label: "توضیح", value: d.description });
  if (d.status === 2) list.push({ label: "باطل‌شده", value: jalali(d.cancelledAt, true) + " — " + (d.cancelReason || "") });
  return list;
});

/**
 * Signature boxes follow the approvals the API enforces: adjustments and opening balances = warehouse supervisor + planning manager,
 * scrap sale = warehouse supervisor (confirms) + finance + security, delivery to the customer = QC loading approval + security.
 */
const printSignatures = computed(() => {
  const d = doc.value;
  if (!d) return [];
  const keeper = { title: "انباردار", name: d.creatorName, date: jalali(d.createDate) };
  const confirmed = { name: d.confirmedByName, date: jalali(d.confirmedAt) };
  const guard = { title: "نگهبانی (کنترل خروج)", name: d.exitCheckedByName, date: jalali(d.exitCheckedAt) };
  switch (d.type) {
    case 1:
    case 17:
      return [{ title: "تحویل‌دهنده" }, { ...keeper, ...confirmed }, { title: "کنترل کیفیت" }, { title: "بازرگانی" }];
    case 2:
    case 3:
      return [{ title: "تحویل‌دهنده (تولید)" }, { ...keeper, ...confirmed }, { title: "کنترل کیفیت" }];
    case 4:
      return [{ title: "درخواست‌کننده" }, { title: "تحویل‌گیرنده" }, { ...keeper, ...confirmed }, { title: "برنامه‌ریزی و کنترل پروژه" }];
    case 5:
      return [{ ...keeper, ...confirmed }, { title: "تحویل‌گیرنده (تامین‌کننده)" }, { title: "بازرگانی" }, guard];
    case 9:
      return [{ ...keeper, ...confirmed }, { title: "پیمانکار / حمل‌کننده" }, { title: "برنامه‌ریزی و کنترل پروژه" }, guard];
    case 10:
      return [{ ...keeper, ...confirmed }, { title: "پیمانکار / حمل‌کننده" }, { title: "برنامه‌ریزی و کنترل پروژه" }];
    case 11:
      return [
        { title: "تحویل‌گیرنده" },
        { title: "تحویل‌دهنده (سرپرست انبار)", name: d.confirmedByName, date: jalali(d.confirmedAt) },
        { title: "تایید جهت بارگیری و حمل (کنترل کیفیت)", name: d.qcLoadingApprovedByName, date: jalali(d.qcLoadingApprovedAt) },
        guard,
      ];
    case 6:
    case 16:
      return [{ title: "انبار مبدا" }, { title: "حمل‌کننده" }, { title: "انبار مقصد" }];
    case 7:
    case 8:
    case 15:
      return [
        { ...keeper },
        { title: "سرپرست انبار", name: d.warehouseApprovedByName, date: jalali(d.warehouseApprovedAt) },
        { title: "مدیر برنامه‌ریزی و کنترل پروژه", name: d.planningApprovedByName, date: jalali(d.planningApprovedAt) },
      ];
    case 12:
      return [{ title: "تولید" }, { ...keeper, ...confirmed }, { title: "کنترل کیفیت" }];
    case 13:
      return [{ title: "تحویل‌دهنده (کارگاه)" }, { ...keeper, ...confirmed }, { title: "کنترل کیفیت" }];
    case 14:
      return [
        { title: "سرپرست انبار", name: d.confirmedByName, date: jalali(d.confirmedAt) },
        { title: "مالی", name: d.financeApprovedByName, date: jalali(d.financeApprovedAt) },
        guard,
      ];
    default:
      return [{ title: "تهیه‌کننده" }, { ...keeper }, { title: "تایید مدیر" }];
  }
});

const printCode = computed(() => formCodes[doc.value?.type] || "");

/** Statement of the issue / delivery forms: who received what, written once and filled from the document. */
const statement = computed(() => {
  const d = doc.value;
  if (!d || ![4, 5, 9, 11].includes(d.type)) return "";
  const who = d.receiverName || "..................";
  return `اینجانب ${who} اقلام فوق (${num(totalQty.value)} واحد در ${(d.lines || []).filter((l: any) => l.item).length} ردیف) را در تاریخ ${jalali(d.documentDate)} ` +
    `${d.vehiclePlate ? "با خودرو به پلاک " + d.vehiclePlate + " " : ""}به همراه راننده ${d.driverName || ".........."} تحویل گرفتم${d.destination ? " و به مقصد " + d.destination + " حمل می‌کنم" : ""}.`;
});

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = (w || []).filter((x: any) => x.isActive);
  getProviders().then(([p]) => (providers.value = rowsOf(p)));
  getWorkOrders().then(([p]) => (workOrders.value = rowsOf(p)));
  getContracts().then(([p]) => (contracts.value = rowsOf(p)));
  getMaterialRequests({ Status: 2, PageSize: 200 }).then(([p]) => (requests.value = rowsOf(p)));
  getOpenPurchaseOrders().then(([p]) => (orders.value = rowsOf(p)));
  if (route.params.id === "new") {
    doc.value = emptyDoc(Number(route.query.type) || 1);
    parseQuotes("");
    if (warehouses.value.length === 1) doc.value.warehouseId = warehouses.value[0].id;
    if (route.query.type === "12") {
      addConversionLine(1);
      addConversionLine(2);
    } else addLine();
    if (route.query.request) {
      doc.value.type = 4;
      doc.value.materialRequestId = Number(route.query.request);
      await fillFromRequest();
    }
    if (route.query.salesOrder) doc.value.salesOrderId = Number(route.query.salesOrder);
    if (route.query.serviceOrder) doc.value.serviceOrderId = Number(route.query.serviceOrder);
    if (route.query.order && doc.value.type === 1) {
      doc.value.purchaseOrderId = Number(route.query.order);
      await onOrderChange();
    }
  } else {
    const [data, error] = await getDocument(Number(route.params.id));
    if (error) return fail(error);
    setDoc(data);
  }
});
</script>

<template>
  <div v-if="doc" class="inv-page">
    <!-- printed forms: receipt / issue / delivery (001-04-FO-WH, 002-04-FO-WH), scrap (FO-WH-001, FO-WH-002), others have no company code -->
    <PrintForm
      v-if="printMode === 'main'"
      :title="documentTypes[doc.type]"
      :form-code="printCode"
      :fields="printFields"
      :notes="printNotes"
      :signatures="printSignatures"
      :distribution="distributionText">
      <table>
        <thead>
          <tr>
            <th>ردیف</th>
            <th v-if="conversion">جهت</th>
            <th>کد کالا</th>
            <th>شرح کالا</th>
            <th>واحد</th>
            <th>مقدار</th>
            <th>بچ / ذوب / گواهی</th>
            <th>سریال‌ها</th>
            <th>{{ transfer ? "محل مبدا ← مقصد" : "محل" }}</th>
            <th v-if="incoming || scrapSale">فی (ریال)</th>
            <th v-if="incoming || scrapSale">مبلغ (ریال)</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in doc.lines.filter((x) => x.item)" :key="l.key">
            <td>{{ i + 1 }}</td>
            <td v-if="conversion">{{ lineDirections[l.direction] }}<span v-if="l.sourceRowNumber"> (از ردیف {{ l.sourceRowNumber }})</span></td>
            <td class="inv-ltr">{{ l.item.code }}</td>
            <td>
              {{ l.item.title }}
              <div v-if="l.description" class="small">{{ l.description }}</div>
            </td>
            <td>{{ l.item.unit }}</td>
            <td class="num">{{ num(l.quantity) }}</td>
            <td>
              <div v-if="lotNumberOf(l)">بچ <span class="inv-ltr">{{ lotNumberOf(l) }}</span></div>
              <div v-if="l.heatNumber">ذوب <span class="inv-ltr">{{ l.heatNumber }}</span></div>
              <div v-if="l.certificateNumber">گواهی <span class="inv-ltr">{{ l.certificateNumber }}</span></div>
            </td>
            <td><div v-for="sn in serials(l)" :key="sn" class="inv-ltr">{{ sn }}</div></td>
            <td class="inv-ltr">{{ locationText(l) }}</td>
            <td v-if="incoming || scrapSale" class="num">{{ l.unitPrice ? rial(l.unitPrice) : "" }}</td>
            <td v-if="incoming || scrapSale" class="num">{{ l.unitPrice ? rial(l.quantity * l.unitPrice) : "" }}</td>
          </tr>
        </tbody>
        <tfoot>
          <tr>
            <td :colspan="conversion ? 5 : 4">جمع</td>
            <td class="num">{{ num(totalQty) }}</td>
            <td :colspan="incoming || scrapSale ? 3 : 3"></td>
            <td v-if="incoming || scrapSale"></td>
            <td v-if="incoming || scrapSale" class="num">{{ totalAmount ? rial(totalAmount) : "" }}</td>
          </tr>
        </tfoot>
      </table>
      <template v-if="statement" #statement>{{ statement }}</template>
    </PrintForm>

    <!-- FO-WH-020: minutes of the scrap sale (the three price quotes and the winner) -->
    <PrintForm
      v-if="printMode === 'minutes' && scrapSale"
      title="صورت‌جلسه فروش ضایعات"
      form-code="FO-WH-020"
      :fields="[
        { label: 'شماره صورت‌جلسه', value: doc.minutesNumber, ltr: true },
        { label: 'شماره سند انبار', value: doc.number || 'پیش‌نویس', ltr: true },
        { label: 'تاریخ', value: jalali(doc.documentDate) },
        { label: 'انبار ضایعات', value: source?.title || doc.warehouseTitle },
        { label: 'خریدار', value: doc.buyerName },
        { label: 'واریز به حساب شرکت', value: doc.depositReference },
        { label: 'جمع کل (ریال)', value: totalAmount ? rial(totalAmount) : '-' },
      ]"
      :signatures="[
        { title: 'سرپرست انبار', name: doc.confirmedByName, date: jalali(doc.confirmedAt) },
        { title: 'مالی', name: doc.financeApprovedByName, date: jalali(doc.financeApprovedAt) },
        { title: 'نگهبانی', name: doc.exitCheckedByName, date: jalali(doc.exitCheckedAt) },
      ]"
      :distribution="distributionText">
      <table>
        <thead>
          <tr><th>ردیف</th><th>کد کالا</th><th>شرح</th><th>واحد</th><th>مقدار</th><th>فی (ریال)</th><th>مبلغ (ریال)</th></tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in doc.lines.filter((x) => x.item)" :key="l.key">
            <td>{{ i + 1 }}</td>
            <td class="inv-ltr">{{ l.item.code }}</td>
            <td>{{ l.item.title }}</td>
            <td>{{ l.item.unit }}</td>
            <td class="num">{{ num(l.quantity) }}</td>
            <td class="num">{{ l.unitPrice ? rial(l.unitPrice) : "" }}</td>
            <td class="num">{{ l.unitPrice ? rial(l.quantity * l.unitPrice) : "" }}</td>
          </tr>
        </tbody>
      </table>
      <table class="mt-2">
        <thead><tr><th style="width: 8%">ردیف</th><th>خریدار (استعلام قیمت)</th><th style="width: 30%">مبلغ پیشنهادی (ریال)</th></tr></thead>
        <tbody>
          <tr v-for="(q, i) in quotes.filter((x) => x.buyer || x.amount)" :key="i">
            <td>{{ i + 1 }}</td>
            <td>{{ q.buyer }}</td>
            <td class="num">{{ q.amount ? rial(q.amount) : "" }}</td>
          </tr>
        </tbody>
      </table>
    </PrintForm>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">
            {{ documentTypes[doc.type] }}
            <span v-if="doc.number" class="inv-ltr ms-2">{{ doc.number }}</span>
          </h1>
          <span class="inv-badge me-1" :class="{ warn: doc.status === 0, ok: doc.status === 1, bad: doc.status === 2 }">{{ documentStatuses[doc.status] }}</span>
          <span v-if="doc.status > 0" class="inv-badge">سپیدار: {{ sepidarStatuses[doc.sepidarStatus] }}</span>
          <span v-if="source" class="inv-badge ms-1">{{ warehouseKinds[source.kind] }}</span>
          <span v-if="leaves && doc.status === 1" class="inv-badge ms-1" :class="doc.exitCheckedAt ? 'ok' : 'warn'">
            {{ doc.exitCheckedAt ? "خروج ثبت شد" : "منتظر کنترل خروج" }}
          </span>
        </div>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <router-link :to="{ name: 'InventoryDocuments' }" class="btn btn-sm btn-outline-secondary">فهرست اسناد</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printLabels">چاپ برچسب</button>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="printForm('main')">
            چاپ سند{{ printCode ? " (" + printCode + ")" : "" }}
          </button>
          <button v-if="!isNew && scrapSale" class="btn btn-sm btn-outline-secondary" @click="printForm('minutes')">چاپ صورت‌جلسه (FO-WH-020)</button>
          <template v-if="isDraft && canEdit">
            <button v-if="!isNew" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="removeDoc">حذف پیش‌نویس</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save()">ذخیره پیش‌نویس</button>
            <button :disabled="busy" class="btn btn-sm btn-success" @click="confirmDoc">تایید سند</button>
          </template>
          <button
            v-if="doc.status === 1 && leaves && !doc.exitCheckedAt && canExitCheck"
            :disabled="busy"
            class="btn btn-sm btn-outline-dark"
            @click="exitNote = ''">
            ثبت خروج (نگهبانی)
          </button>
          <button v-if="doc.status === 1 && !doc.exitCheckedAt && type !== 16 && canEdit" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="cancelReason = ''">
            ابطال
          </button>
        </div>
      </div>

      <div v-if="fromSystem" class="alert alert-info small py-2">
        این سند از
        <template v-if="doc.ncrId">
          گزارش عدم انطباق
          <router-link :to="{ name: 'InventoryNcrEdit', params: { id: doc.ncrId } }" class="inv-ltr">{{ doc.ncrNumber }}</router-link>
        </template>
        <template v-else-if="doc.inspectionId">نتیجه بازرسی ورودی</template>
        <template v-else>نتیجه شمارش موجودی</template>
        ساخته شده و ردیف‌های آن تغییر نمی‌کند.
      </div>

      <!-- approvals panel: adjustments and opening balances (kinds 1 and 2), delivery to the customer (kind 4), scrap sale (kind 6) -->
      <div v-if="isDraft && needsApprovals && !doc.stockCountId" class="alert alert-warning small py-2 d-flex flex-wrap gap-2 align-items-center">
        <span>سند تعدیل پیش از تایید، تایید سرپرست انبار و مدیر برنامه‌ریزی و کنترل پروژه را لازم دارد (هر دو غیر از ثبت‌کننده و با هم متفاوت).</span>
        <span class="inv-badge" :class="{ ok: doc.warehouseApprovedAt }">سرپرست انبار: {{ doc.warehouseApprovedByName || "—" }}</span>
        <span class="inv-badge" :class="{ ok: doc.planningApprovedAt }">برنامه‌ریزی: {{ doc.planningApprovedByName || "—" }}</span>
        <button v-if="!isNew && !doc.warehouseApprovedAt && canApproveWarehouse" :disabled="busy" class="btn btn-sm btn-outline-success" @click="approve(1)">تایید سرپرست انبار</button>
        <button v-if="!isNew && !doc.planningApprovedAt && canApprovePlanning" :disabled="busy" class="btn btn-sm btn-outline-success" @click="approve(2)">تایید برنامه‌ریزی</button>
        <span class="inv-muted">هر تغییری در پیش‌نویس، تاییدها را پاک می‌کند.</span>
      </div>
      <div v-if="isDraft && customerDelivery" class="alert alert-warning small py-2 d-flex flex-wrap gap-2 align-items-center">
        <span>تحویل به مشتری پس از تایید بارگیری کنترل کیفیت تایید می‌شود.</span>
        <span class="inv-badge" :class="{ ok: doc.qcLoadingApprovedAt }">QC: {{ doc.qcLoadingApprovedByName || "—" }}</span>
        <button v-if="!isNew && !doc.qcLoadingApprovedAt && canApproveQuality" :disabled="busy" class="btn btn-sm btn-outline-success" @click="approve(4)">تایید بارگیری (QC)</button>
      </div>
      <div v-if="isDraft && scrapSale" class="alert alert-warning small py-2 d-flex flex-wrap gap-2 align-items-center">
        <span>فروش ضایعات پیش از خروج، تایید مالی (قیمت و واریز به حساب شرکت) را لازم دارد؛ شماره واریز و استعلام سه خریدار باید قبلاً ثبت و ذخیره شده باشد.</span>
        <span class="inv-badge" :class="{ ok: doc.financeApprovedAt }">مالی: {{ doc.financeApprovedByName || "—" }}</span>
        <button v-if="!isNew && !doc.financeApprovedAt && canApproveFinance" :disabled="busy" class="btn btn-sm btn-outline-success" @click="approve(6)">تایید مالی</button>
      </div>
      <div v-if="isDraft && pettyCash" class="alert alert-secondary small py-2">
        خرید تنخواه: رسید بدون سفارش خرید فقط برای خرید تنخواه‌ای است؛ فی همه ردیف‌ها لازم است و جمع مبلغ نباید از سقف خرید تنخواه بیشتر باشد.
        برای خرید عادی سفارش خرید ابلاغ‌شده را انتخاب کنید.
      </div>

      <div v-if="doc.status === 2" class="alert alert-danger small py-2">
        باطل‌شده {{ jalali(doc.cancelledAt, true) }} توسط {{ doc.cancelledByName }} — دلیل: {{ doc.cancelReason }}
      </div>

      <div class="row g-2">
        <div v-if="isNew" class="col-md-3">
          <label class="form-label small mb-0">نوع سند</label>
          <select v-model="doc.type" class="form-select form-select-sm" @change="onTypeChange">
            <option v-for="k in typeOrder" :key="k" :value="k">{{ documentTypes[k] }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="doc.documentDate" :disabled="!isDraft" class="form-control form-control-sm" type="date" />
          <span class="inv-muted">{{ jalali(doc.documentDate) }}</span>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">{{ transfer ? "انبار مبدا" : "انبار" }} *</label>
          <select v-model="doc.warehouseId" :disabled="!isDraft || fromSystem" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }} ({{ warehouseKinds[w.kind] }})</option>
          </select>
        </div>
        <div v-if="hasDestination" class="col-md-3">
          <label class="form-label small mb-0">{{ destinationLabel }}{{ transfer ? " *" : "" }}</label>
          <select v-model="doc.destinationWarehouseId" :disabled="!isDraft" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
          </select>
        </div>
        <div v-if="usesOrder" class="col-md-4">
          <label class="form-label small mb-0">سفارش خرید{{ type === 1 ? " (خالی = خرید تنخواه)" : "" }}</label>
          <select v-model="doc.purchaseOrderId" :disabled="!isDraft" class="form-select form-select-sm" @change="onOrderChange">
            <option :value="null">{{ type === 1 ? "بدون سفارش خرید (خرید تنخواه)" : "بدون سفارش خرید" }}</option>
            <option v-if="doc.purchaseOrderId && !orders.find((o) => o.id === doc.purchaseOrderId)" :value="doc.purchaseOrderId">{{ doc.purchaseOrderNumber }}</option>
            <option v-for="o in orders" :key="o.id" :value="o.id">{{ o.number }} — {{ o.providerName }} ({{ rial(o.totalAmount) }})</option>
          </select>
        </div>
        <div v-if="needsProvider" class="col-md-3">
          <label class="form-label small mb-0">{{ [9, 10].includes(type) ? "پیمانکار *" : "تامین‌کننده" }}</label>
          <select v-model="doc.providerId" :disabled="!isDraft || orderLocked" class="form-select form-select-sm">
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
          <label class="form-label small mb-0">قرارداد / پروژه{{ needsContract ? " *" : "" }}</label>
          <select v-model="doc.contractId" :disabled="!isDraft" class="form-select form-select-sm">
            <option :value="null">-</option>
            <option v-for="c in contracts" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option>
          </select>
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">شماره مرجع</label>
          <input v-model="doc.referenceNumber" :disabled="!isDraft" class="form-control form-control-sm" placeholder="سفارش، بارنامه..." />
        </div>
        <div v-if="issue" class="col-md-5">
          <label class="form-label small mb-0">درخواست کالای تاییدشده *</label>
          <div class="d-flex gap-1">
            <select v-model="doc.materialRequestId" :disabled="!isDraft" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-if="doc.materialRequestId && !requests.find((r) => r.id === doc.materialRequestId)" :value="doc.materialRequestId">{{ doc.materialRequestNumber }}</option>
              <option v-for="r in requests" :key="r.id" :value="r.id">{{ r.number }} — {{ r.workOrderCode || r.purpose }}</option>
            </select>
            <button v-if="isDraft" class="btn btn-sm btn-outline-primary text-nowrap" @click="fillFromRequest">ردیف‌ها از درخواست</button>
          </div>
        </div>
        <div class="col-md-12">
          <label class="form-label small mb-0">توضیح</label>
          <input v-model="doc.description" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
      </div>

      <!-- goods leave the company: the guard compares the load with the lines -->
      <div v-if="leaves" class="row g-2 mt-1">
        <div class="col-12 small fw-bold">خروج از شرکت (برگه خروج)</div>
        <div class="col-md-3">
          <label class="form-label small mb-0">نام راننده *</label>
          <input v-model="doc.driverName" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
        <div class="col-md-2">
          <label class="form-label small mb-0">شماره پلاک *</label>
          <input v-model="doc.vehiclePlate" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">تحویل‌گیرنده *</label>
          <input v-model="doc.receiverName" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">مقصد</label>
          <input v-model="doc.destination" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
        <div v-if="doc.exitCheckedAt" class="col-12 inv-muted">
          خروج ثبت شد: {{ doc.exitCheckedByName }} {{ jalali(doc.exitCheckedAt, true) }} {{ doc.exitCheckNote || "" }}
        </div>
      </div>

      <!-- scrap sale (WI-WH-004): the money only goes to the company account -->
      <div v-if="scrapSale" class="row g-2 mt-1">
        <div class="col-12 small fw-bold">فروش ضایعات (WI-WH-004) — وجه فقط به حساب شرکت واریز می‌شود</div>
        <div class="col-md-3">
          <label class="form-label small mb-0">خریدار *</label>
          <input v-model="doc.buyerName" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">شماره صورت‌جلسه FO-WH-020 *</label>
          <input v-model="doc.minutesNumber" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">شماره واریز به حساب شرکت *</label>
          <input v-model="doc.depositReference" :disabled="!isDraft" class="form-control form-control-sm" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">تصویر قبض باسکول *</label>
          <div class="d-flex gap-1 align-items-center">
            <input v-if="isDraft" accept="image/*,application/pdf" class="form-control form-control-sm" type="file" @change="uploadWeighbridge" />
            <button v-if="doc.weighbridgeDocumentId" class="btn btn-sm btn-link p-0 text-nowrap" @click="downloadFile(doc.weighbridgeDocumentId, 'weighbridge')">دیدن قبض</button>
          </div>
        </div>
        <div class="col-12">
          <label class="form-label small mb-1">استعلام قیمت از خریداران (حداقل سه خریدار) — {{ quoteCount }} مورد</label>
          <div v-for="(q, i) in quotes" :key="i" class="d-flex gap-2 mb-1">
            <input v-model="q.buyer" :disabled="!isDraft" class="form-control form-control-sm" placeholder="نام خریدار / شرکت بازیافت" />
            <input v-model.number="q.amount" :disabled="!isDraft" class="form-control form-control-sm inv-ltr" placeholder="مبلغ پیشنهادی (ریال)" style="max-width: 220px" type="number" />
            <button v-if="isDraft && quotes.length > 3" class="btn btn-sm btn-link text-danger p-0" @click="quotes.splice(i, 1)">×</button>
          </div>
          <button v-if="isDraft" class="btn btn-sm btn-outline-primary" @click="quotes.push({ buyer: '', amount: null })">+ خریدار</button>
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
          <template v-if="conversion">
            <button class="btn btn-sm btn-outline-primary" @click="addConversionLine(1)">+ خروج مرحله قبل</button>
            <button class="btn btn-sm btn-outline-primary" @click="addConversionLine(2)">+ ورود مرحله بعد</button>
          </template>
          <button v-else class="btn btn-sm btn-outline-primary" @click="addLine">+ ردیف</button>
        </div>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-bordered">
          <thead>
            <tr>
              <th style="width: 2rem">#</th>
              <th v-if="conversion" style="width: 9rem">جهت</th>
              <th style="min-width: 240px">کالا</th>
              <th style="width: 7rem">مقدار</th>
              <th style="min-width: 200px">بچ / سریال</th>
              <th style="min-width: 110px">{{ transfer ? "محل مبدا" : "محل" }}</th>
              <th v-if="transfer" style="min-width: 110px">محل مقصد</th>
              <th v-if="usesOrder && doc.purchaseOrderId" style="min-width: 140px">ردیف سفارش *</th>
              <th v-if="incoming || conversion || scrapSale" style="width: 8rem">فی (ریال)</th>
              <th>توضیح</th>
              <th v-if="isDraft" class="d-print-none"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(line, i) in doc.lines" :key="line.key">
              <td>{{ i + 1 }}</td>
              <td v-if="conversion">
                <select v-model="line.direction" :disabled="!isDraft" class="form-select form-select-sm mb-1">
                  <option :value="1">{{ lineDirections[1] }}</option>
                  <option :value="2">{{ lineDirections[2] }}</option>
                </select>
                <select v-if="line.direction === 2" v-model="line.sourceRowNumber" :disabled="!isDraft" class="form-select form-select-sm">
                  <option :value="null">از ردیف *</option>
                  <template v-for="(c, k) in doc.lines" :key="c.key">
                    <option v-if="c.direction === 1" :value="k + 1">ردیف {{ k + 1 }}</option>
                  </template>
                </select>
              </td>
              <td>
                <ItemPicker v-if="isDraft && !fromSystem && !line.purchaseOrderLineId" v-model="line.item" @select="onItem(line)" />
                <span v-else><span class="inv-ltr">{{ line.item?.code }}</span> — {{ line.item?.title }}</span>
                <div v-if="line.item" class="inv-muted">{{ trackingModes[line.item.trackingMode] }} · {{ line.item.unit }}</div>
              </td>
              <td>
                <input
                  v-model.number="line.quantity"
                  :disabled="!isDraft || (line.item && line.item.trackingMode === 2 && !(lineIncoming(line) && !serials(line).length))"
                  class="form-control form-control-sm"
                  min="0"
                  step="any"
                  type="number" />
              </td>
              <td>
                <template v-if="line.item && line.item.trackingMode === 1">
                  <template v-if="lineIncoming(line)">
                    <select v-if="isDraft && (lotOptions[line.item.id] || []).length" v-model="line.lotId" class="form-select form-select-sm mb-1">
                      <option :value="null">بچ جدید ↓</option>
                      <option v-for="lot in lotOptions[line.item.id]" :key="lot.id" :value="lot.id">{{ lot.lotNumber }} ({{ lot.heatNumber }})</option>
                    </select>
                    <template v-if="!line.lotId">
                      <input v-model="line.lotNumber" :disabled="!isDraft" class="form-control form-control-sm inv-ltr mb-1" placeholder="شماره بچ (خالی = برچسب خودکار)" />
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
                    :placeholder="lineIncoming(line) ? 'هر سریال در یک خط (خالی = برچسب خودکار)' : 'هر سریال در یک خط'"
                    rows="2"
                    @input="lineIncoming(line) && !serials(line).length ? null : syncSerialQty(line)"></textarea>
                  <button v-if="isDraft && !lineIncoming(line)" class="btn btn-sm btn-link p-0" @click="openSerialPicker(line)">انتخاب از موجودی</button>
                </template>
                <span v-else class="inv-muted">-</span>
              </td>
              <td>
                <select v-if="isDraft" v-model="line.locationId" class="form-select form-select-sm">
                  <option :value="null">{{ lineIncoming(line) ? "بدون محل" : "خودکار" }}</option>
                  <option v-for="l in locationsFor(line)" :key="l.id" :value="l.id">{{ l.code }}</option>
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
              <td v-if="usesOrder && doc.purchaseOrderId">
                <select v-model="line.purchaseOrderLineId" :disabled="!isDraft" class="form-select form-select-sm" @change="onOrderLine(line)">
                  <option :value="null">-</option>
                  <option v-for="ol in orderLinesOf(line)" :key="ol.id" :value="ol.id">ردیف {{ ol.rowNumber }} (باز {{ num(ol.openQuantity) }})</option>
                </select>
              </td>
              <td v-if="incoming || conversion || scrapSale">
                <input
                  v-model.number="line.unitPrice"
                  :disabled="!isDraft || (type === 1 && !!doc.purchaseOrderId) || (conversion && line.direction !== 2)"
                  class="form-control form-control-sm"
                  type="number" />
              </td>
              <td><input v-model="line.description" :disabled="!isDraft" class="form-control form-control-sm" /></td>
              <td v-if="isDraft" class="d-print-none"><button class="btn btn-sm btn-link text-danger p-0" @click="removeLine(i)">×</button></td>
            </tr>
          </tbody>
          <tfoot>
            <tr><td></td><td class="text-end small">جمع مقدار</td><td>{{ num(totalQty) }}</td><td :colspan="10"></td></tr>
          </tfoot>
        </table>
      </div>
      <p v-if="type === 1 && doc.purchaseOrderId && isDraft" class="inv-muted mb-0">فی هر ردیف از سفارش خرید گرفته می‌شود و قابل تغییر نیست؛ دریافت بیش از مقدار باز سفارش (با تلرانس تنظیمات تدارکات) مجاز نیست.</p>
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

    <!-- exit check by the guard -->
    <div v-if="exitNote !== null" class="inv-modal-backdrop" @click.self="exitNote = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ثبت خروج کالا — {{ doc.number }}</h2>
        <p class="small">
          راننده {{ doc.driverName }} · پلاک {{ doc.vehiclePlate }} · تحویل‌گیرنده {{ doc.receiverName }}. بار را با ردیف‌های سند مقایسه کنید.
        </p>
        <textarea v-model="exitNote" class="form-control form-control-sm" placeholder="توضیح (اختیاری)" rows="2"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="exitNote = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-dark" @click="exitCheck">خروج تایید شد</button>
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
