import moment from "moment-jalaali";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

moment.loadPersian({ dialect: "persian-modern", usePersianDigits: false });

export const trackingModes: Record<number, string> = {
  0: "بدون ردیابی",
  1: "بچ / شماره ذوب",
  2: "سریال",
};

export const warehouseKinds: Record<number, string> = {
  0: "عمومی",
  1: "مواد اولیه",
  2: "قطعات",
  3: "در جریان ساخت",
  4: "محصول ساخته‌شده",
  5: "قرنطینه / بازرسی",
  6: "ضایعات",
};

export const documentTypes: Record<number, string> = {
  1: "رسید خرید",
  2: "رسید ساخت",
  3: "برگشت از تولید",
  4: "حواله",
  5: "برگشت به تامین‌کننده",
  6: "انتقال",
  7: "تعدیل افزایشی",
  8: "تعدیل کاهشی",
};

export const incomingTypes = [1, 2, 3, 7];
export const outgoingTypes = [4, 5, 8];

export const documentStatuses: Record<number, string> = {
  0: "پیش‌نویس",
  1: "تاییدشده",
  2: "باطل‌شده",
};

export const sepidarStatuses: Record<number, string> = {
  0: "لازم نیست",
  1: "در انتظار",
  2: "خروجی گرفته شد",
  3: "ارسال شد",
  4: "خطا",
};

export const serialStatuses: Record<number, string> = {
  1: "در تولید",
  2: "در انبار",
  3: "نصب‌شده در مجموعه",
  4: "تحویل مشتری",
  5: "برگشت به تامین‌کننده",
  9: "ضایعات",
};

export const operationStatuses: Record<number, string> = {
  0: "شروع نشده",
  1: "در حال انجام",
  2: "انجام شد",
  3: "رد شد",
};

export const qcStatuses: Record<number, string> = {
  0: "QC لازم ندارد",
  1: "منتظر QC",
  2: "تایید QC",
  3: "رد QC",
  4: "تایید مشروط",
};

export function jalali(value: string | null | undefined, withTime = false): string {
  if (!value) return "";
  const m = moment(value);
  if (!m.isValid()) return "";
  return m.format(withTime ? "jYYYY/jMM/jDD HH:mm" : "jYYYY/jMM/jDD");
}

export function today(): string {
  return moment().format("YYYY-MM-DD");
}

export function num(value: number | null | undefined): string {
  if (value === null || value === undefined) return "";
  return Number(value).toLocaleString("en-US", { maximumFractionDigits: 4 });
}

export function ok(message: string) {
  showSuccessNotification(message);
}

export function fail(error: any) {
  showErrorNotification(error?.message || String(error || "خطا"));
}

/** Text written into the barcode / QR of a label, read back by the scan boxes. */
export const labelCode = {
  item: (itemCode: string) => `I/${itemCode}`,
  serial: (itemCode: string, serial: string) => `S/${itemCode}/${serial}`,
  lot: (itemCode: string, lot: string) => `L/${itemCode}/${lot}`,
  location: (warehouseCode: string, locationCode: string) => `LOC/${warehouseCode}/${locationCode}`,
};

export interface ScanResult {
  kind: "item" | "serial" | "lot" | "location" | "text";
  itemCode?: string;
  value?: string;
  warehouseCode?: string;
  text: string;
}

export function parseScan(raw: string): ScanResult {
  const text = (raw || "").trim();
  const parts = text.split("/");
  const kind = parts.length >= 2 ? parts[0].toUpperCase() : "";
  if (kind === "S" && parts.length >= 3) return { kind: "serial", itemCode: parts[1], value: parts.slice(2).join("/"), text };
  if (kind === "L" && parts.length >= 3) return { kind: "lot", itemCode: parts[1], value: parts.slice(2).join("/"), text };
  if (kind === "LOC" && parts.length >= 3) return { kind: "location", warehouseCode: parts[1], value: parts.slice(2).join("/"), text };
  if (kind === "I") return { kind: "item", itemCode: parts.slice(1).join("/"), text };
  return { kind: "text", text };
}

/** Labels to print are handed to the label page (opened in a new tab) through localStorage. */
export interface Label {
  code: string;
  title: string;
  lines: string[];
}

export function openLabels(labels: Label[], router: any) {
  try {
    localStorage.setItem("inventoryLabels", JSON.stringify(labels));
  } catch {
    /* storage blocked: the label page shows nothing */
  }
  const url = router.resolve({ name: "InventoryLabels" }).href;
  window.open(url, "_blank");
}
