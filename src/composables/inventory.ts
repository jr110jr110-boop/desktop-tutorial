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
  7: "پیمانکار (انبار مجازی)",
  8: "اموال مشتری / مرجوعی",
  9: "مدل‌های ریخته‌گری",
};

/** InventoryDocumentType of the API. */
export const documentTypes: Record<number, string> = {
  1: "رسید خرید",
  2: "رسید ساخت",
  3: "برگشت از تولید",
  4: "حواله",
  5: "برگشت به تامین‌کننده",
  6: "انتقال",
  7: "تعدیل افزایشی",
  8: "تعدیل کاهشی",
  9: "ارسال به پیمانکار",
  10: "برگشت از پیمانکار",
  11: "تحویل به مشتری",
  12: "تبدیل مرحله",
  13: "رسید ضایعات",
  14: "فروش ضایعات",
  15: "موجودی اول دوره",
  16: "ترخیص از قرنطینه",
  17: "رسید اموال مشتری",
};

export const incomingTypes = [1, 2, 3, 7, 13, 15, 17];
export const outgoingTypes = [4, 5, 8, 11, 14];
export const transferTypes = [6, 9, 10, 16];
/** Goods leave the premises: driver, vehicle and receiver are written and the guard checks the exit. */
export const leavesPremisesTypes = [5, 9, 11, 14];
/** Adjustments and opening balances need the warehouse supervisor and the planning manager. */
export const needsApprovalTypes = [7, 8, 15];
/** Made by the system (inspection or NCR), never typed by a user. */
export const systemOnlyTypes = [16];
/** Types that can reference a purchase order. */
export const purchaseOrderTypes = [1, 5];
/** Order of the "new document" buttons. */
export const typeOrder = [1, 2, 3, 4, 5, 6, 9, 10, 11, 12, 13, 14, 15, 17, 7, 8];

/** Not decided by the company: the label stays "to be set by quality assurance". */
export const NO_FORM_CODE = "کد فرم: (توسط تضمین کیفیت تعیین شود)";

/** Form codes of the company for the warehouse documents (the others have no code yet). */
export const formCodes: Record<number, string> = {
  1: "001-04-FO-WH",
  2: "001-04-FO-WH",
  3: "001-04-FO-WH",
  15: "001-04-FO-WH",
  17: "001-04-FO-WH",
  4: "002-04-FO-WH",
  9: "002-04-FO-WH",
  11: "002-04-FO-WH",
  5: "002-04-FO-WH",
  13: "FO-WH-001",
  14: "FO-WH-002",
};

export const distributionText = "توزیع نسخ: ۱- انبار  ۲- بازرگانی  ۳- برنامه‌ریزی و کنترل پروژه";

/** DocumentLineDirection: only a stage conversion has it. */
export const lineDirections: Record<number, string> = {
  1: "خروج مرحله قبل",
  2: "ورود مرحله بعد",
};

/** ApprovalKind of the API. */
export const approvalKinds: Record<number, string> = {
  1: "سرپرست انبار",
  2: "مدیر برنامه‌ریزی و کنترل پروژه",
  3: "مدیر تولید",
  4: "کنترل کیفیت",
  5: "مدیر مهندسی",
  6: "واحد مالی",
};

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
  6: "تبدیل به مرحله بعد",
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

export const inspectionStatuses: Record<number, string> = { 0: "منتظر بازرسی", 1: "بازرسی شد" };
export const ncrSources: Record<number, string> = { 1: "بازرسی ورودی", 2: "حین ساخت", 3: "بازرسی نهایی", 4: "مشتری", 5: "پیمانکار" };
export const ncrSeverities: Record<number, string> = { 0: "تعیین نشده", 1: "جزئی (Minor)", 2: "عمده (Major)" };
export const ncrCauses: Record<number, string> = { 0: "—", 1: "نیروی انسانی", 2: "مواد", 3: "روش", 4: "تجهیزات", 5: "نقشه", 6: "محیط" };
export const ncrDispositions: Record<number, string> = {
  0: "تعیین نشده",
  1: "استفاده با مجوز ارفاقی",
  2: "دوباره‌کاری",
  3: "تعمیر (با مجوز ارفاقی)",
  4: "ضایعات",
  5: "مرجوعی به تامین‌کننده / پیمانکار",
};
export const ncrStatuses: Record<number, string> = { 0: "باز", 1: "تصمیم تایید شد", 2: "اجرا شد (پیگیری اقدام اصلاحی)", 3: "بسته شد", 4: "ابطال" };
export const materialRequestStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال شد (منتظر تایید)", 2: "تایید شد", 3: "رد شد", 4: "بسته شد" };
export const itemRequestStatuses: Record<number, string> = { 0: "منتظر بررسی برنامه‌ریزی", 1: "تایید و کد داده شد", 2: "رد شد" };
export const countKinds: Record<number, string> = { 1: "شمارش ادواری", 2: "انبارگردانی سالانه" };
export const countStatuses: Record<number, string> = { 0: "در حال شمارش", 1: "ارسال برای تایید", 2: "تایید شد", 3: "ثبت شد", 4: "ابطال" };
export const codingSchemes: Record<string, string> = {
  Group: "گروه کالا (رقم ۱ و ۲)",
  Assembly: "محدوده مجموعه‌ها (جدول ۴)",
  PartStatus: "وضعیت قطعه (جدول ۳)",
  MaterialShape: "مشخصه فیزیکی متریال",
  MaterialGrade: "جنس متریال",
  MaterialType: "نوع متریال",
  MaterialStatus: "وضعیت متریال",
  ProductType: "نوع محصول",
  ModelPart: "بخش مدل (M/P/C)",
  ModelMaterial: "جنس مدل",
  ToolCategory: "دسته ابزار و ابزار دقیق",
  ConsumableCategory: "دسته ملزومات",
  AssetCategory: "دسته اموال",
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

/** Amounts in rial: whole numbers with thousands separators. */
export function rial(value: number | null | undefined): string {
  if (value === null || value === undefined) return "";
  return Math.round(Number(value)).toLocaleString("en-US");
}

export function num(value: number | null | undefined): string {
  if (value === null || value === undefined) return "";
  return Number(value).toLocaleString("en-US", { maximumFractionDigits: 4 });
}

export function printPage() {
  window.print();
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
