import "@/views/inventory/inventory.scss";
import "@/views/engineering/engineering.scss";
import { faDigits, faNum } from "@/composables/ceo";
import { jalali } from "@/composables/inventory";

// ENG-E1: names, role lists and labels of the engineering module. The role lists mirror EnRoles (Services/NasbNiro/Engineering/EnCommon.cs) exactly; the API checks every call again.
// Access decisions use hasRole() / isRole() only (never currentRole()). Enum values are numbers in the JSON; the DTOs also carry their Persian text, the maps below are for filters.
export { jalali, faDigits, faNum };

const list = (csv: string) => csv.split(",");

export const EnRoles = {
  /** The engineering manager or his deputy. */
  manager: list("ENGINEERING"),
  /** The engineers and the manager. */
  writers: list("ENGINEERING,ENGINEER"),
  ceo: list("CEO"),
  standardSetters: list("SALES,COMMERCIAL,ENGINEERING,ENGINEER"),
  commercial: list("SALES,COMMERCIAL"),
  pmo: list("PMO"),
  pmoSide: list("PMO,PROJECTMANAGER"),
  /** Every list, detail and the overview of the module (EnRoles.Readers). */
  readers: list("ENGINEERING,ENGINEER,PMO,CEO,PROJECTMANAGER,PLANNING,SALES,COMMERCIAL,DCC,QA,QC,PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION,WAREHOUSE,WHSUPERVISOR,AUDITOR"),
  /** The questions of the units to the PMO: the six units (EnUnits.UnitRoles) and the PMO, the CEO and the auditor. */
  infoReaders: list("QA,QC,PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION,COMMERCIAL,WAREHOUSE,WHSUPERVISOR,PLANNING,PMO,CEO,AUDITOR"),
} as const;

/** EnUnit: the six units that receive the documents (value, Persian name). */
export const enUnits: { value: number; text: string; code: string }[] = [
  { value: 1, text: "تضمین کیفیت", code: "Qa" },
  { value: 2, text: "کنترل کیفیت", code: "Qc" },
  { value: 3, text: "تولید", code: "Production" },
  { value: 4, text: "تدارکات", code: "Procurement" },
  { value: 5, text: "انبار", code: "Warehouse" },
  { value: 6, text: "برنامه‌ریزی", code: "Planning" },
];

export const caseKinds: Record<number, string> = { 1: "توربین", 2: "قطعه یدکی", 3: "خدمات" };
export const classifications: Record<number, string> = { 0: "تعیین‌نشده", 1: "پلتفرم (توربین ساخته‌شده)", 2: "طراحی جدید" };
export const caseStatuses: Record<number, string> = {
  1: "در حال تعیین نوع",
  2: "منتظر تایید مدیر مهندسی",
  3: "منتظر تایید مدیرعامل",
  4: "تهیه بسته فنی مناقصه",
  5: "بسته فنی به بازرگانی ابلاغ شد",
  6: "برنده شده",
  7: "در مهندسی",
  8: "تحویل به PMO",
  9: "پذیرفته‌شده توسط PMO",
  10: "منتشرشده",
  11: "بسته‌شده (بازنده یا عدم شرکت)",
  12: "لغوشده",
};
export const clarStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "نزد بازرگانی", 2: "ارسال‌شده به کارفرما", 3: "پاسخ‌داده‌شده", 4: "بسته", 5: "ابطال‌شده" };
export const dossierStatuses: Record<number, string> = { 0: "ارزیابی‌نشده", 1: "ناقص", 2: "کامل‌شده، منتظر تایید مدیر مهندسی", 3: "کامل" };
export const lineStates: Record<number, string> = { 0: "موجود نیست", 1: "موجود", 2: "کاربرد ندارد" };
export const refStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در انتظار تایید", 2: "تاییدشده", 3: "جایگزین‌شده", 4: "ابطال‌شده" };
export const handoverStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده به PMO", 2: "برگشت‌خورده", 3: "پذیرفته‌شده", 4: "منتشرشده", 5: "ابطال‌شده" };
export const waiverStatuses: Record<number, string> = { 0: "درخواست‌شده", 1: "تاییدشده", 2: "ردشده", 4: "ابطال‌شده" };
export const debtStatuses: Record<number, string> = { 0: "باز", 1: "تسویه‌شده", 2: "ابطال‌شده" };
export const distStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ابلاغ‌شده" };
export const deviationKinds: Record<number, string> = { 1: "کاربرد ندارد", 2: "تسهیل فهرست مرجع", 3: "اضطرار" };
export const deviationStatuses: Record<number, string> = { 0: "درخواست‌شده", 1: "تایید مدیر مهندسی", 2: "تاییدشده", 3: "ردشده", 4: "ابطال‌شده" };
export const gapStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده", 2: "ابطال‌شده" };
export const gapKinds: Record<number, string> = { 0: "بدون تغییر", 1: "افزوده", 2: "حذف‌شده", 3: "تغییر الزام" };
export const gapKindKeys: Record<number, string> = { 0: "Same", 1: "Added", 2: "Removed", 3: "RequiredChanged" };
export const regStatuses: Record<number, string> = { 0: "درخواست‌شده", 1: "تاییدشده", 2: "ردشده", 3: "ابطال‌شده" };
export const infoStatuses: Record<number, string> = { 0: "باز", 1: "پاسخ‌داده‌شده", 2: "بسته", 3: "پس‌گرفته‌شده" };

/** Colour of a status chip: "ok" | "warn" | "bad" | "info". */
export function chipOf(group: string, status: number | null | undefined): string {
  const s = Number(status);
  switch (group) {
    case "case":
      return s === 11 || s === 12 ? "bad" : s === 2 || s === 3 ? "warn" : s === 10 || s === 9 ? "ok" : "info";
    case "clar":
      return s === 4 ? "ok" : s === 5 ? "bad" : s === 1 || s === 2 ? "warn" : "info";
    case "dossier":
      return s === 3 ? "ok" : s === 1 ? "bad" : s === 2 ? "warn" : "info";
    case "line":
      return s === 1 ? "ok" : s === 0 ? "bad" : "info";
    case "ref":
      return s === 2 ? "ok" : s === 1 ? "warn" : s === 4 ? "bad" : "info";
    case "handover":
      return s === 4 || s === 3 ? "ok" : s === 2 || s === 5 ? "bad" : s === 1 ? "warn" : "info";
    case "waiver":
      return s === 1 ? "ok" : s === 2 ? "bad" : s === 0 ? "warn" : "info";
    case "debt":
      return s === 1 ? "ok" : s === 0 ? "warn" : "info";
    case "deviation":
      return s === 2 ? "ok" : s === 3 ? "bad" : s === 0 || s === 1 ? "warn" : "info";
    case "gap":
      return s === 1 ? "ok" : s === 2 ? "bad" : "warn";
    case "reg":
      return s === 1 ? "ok" : s === 2 ? "bad" : s === 0 ? "warn" : "info";
    case "info":
      return s === 0 ? "warn" : s === 1 ? "info" : s === 2 ? "ok" : "bad";
    default:
      return "info";
  }
}

/** Hours of waiting as text: «۳ روز و ۴ ساعت». The waiting never expires; the text only says how long. */
export function waitText(hours: number | null | undefined): string {
  const h = Math.max(0, Math.floor(Number(hours) || 0));
  if (h < 1) return "کمتر از یک ساعت";
  const d = Math.floor(h / 24);
  const r = h % 24;
  if (d === 0) return `${faNum(r, 0)} ساعت`;
  return r ? `${faNum(d, 0)} روز و ${faNum(r, 0)} ساعت` : `${faNum(d, 0)} روز`;
}

/** Hours from now to an ISO date-time (negative = past). */
export function hoursUntil(iso: string | null | undefined): number | null {
  if (!iso) return null;
  const t = new Date(iso).getTime();
  return Number.isFinite(t) ? Math.round((t - Date.now()) / 3600000) : null;
}

/** Shamsi date and time of an ISO date-time. */
export const stamp = (iso: string | null | undefined) => faDigits(jalali(iso, true));

/** Percent of the required lines that are available (for the progress bars); null when nothing is required. */
export function completeness(required: number, available: number): number | null {
  return required > 0 ? Math.max(0, Math.min(100, Math.round((available / required) * 100))) : null;
}

/** The paths of the module (also the links the server writes into agent steps and the inbox). */
export const enPaths = {
  overview: "/engineering",
  cases: "/engineering/cases",
  case: (id: number | string) => `/engineering/cases/${id}`,
  platforms: "/engineering/platforms",
  platform: (productId: number | string, code?: string | null) => `/engineering/platforms/${productId}` + (code ? `?code=${encodeURIComponent(code)}` : ""),
  references: "/engineering/reference-lists",
  reference: (id: number | string) => `/engineering/reference-lists/${id}`,
  handovers: "/engineering/handovers",
  handover: (id: number | string) => `/engineering/handovers/${id}`,
  debts: "/engineering/debts",
  deviations: "/engineering/deviations",
  deviation: (id: number | string) => `/engineering/deviations/${id}`,
  gaps: "/engineering/standard-gaps",
  gap: (id: number | string) => `/engineering/standard-gaps/${id}`,
  registrations: "/engineering/platform-registrations",
  waivers: "/engineering/waivers",
  infoRequests: "/engineering/info-requests",
};

/** Text of a server refusal (the Error of the API helpers) or a fallback. */
export function errText(error: any, fallback = "عملیات انجام نشد."): string {
  return error?.message ? String(error.message) : fallback;
}

/** The id returned by the document upload (a bare id, or { data } / { id }). */
export function uploadedId(data: any): string | null {
  if (!data) return null;
  if (typeof data === "string") return data;
  const v = data.data ?? data.id ?? data.documentId;
  return typeof v === "string" ? v : v && typeof v === "object" ? uploadedId(v) : null;
}
