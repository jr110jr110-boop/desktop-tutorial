import "@/views/inventory/inventory.scss";
import "@/views/engineering/engineering.scss";
import { faDigits, faNum } from "@/composables/ceo";
import { ref } from "vue";
import { saveBlob } from "@/api/ApiClient";
import { getEnApprovedCodes } from "@/api/EngineeringAPIs";
import { addDocument } from "@/api/Document";
import { getDocument } from "@/api/DocumentAPIs";
import type { EnField } from "@/components/engineering/EnPrompt.vue";
import { fail, jalali, ok } from "@/composables/inventory";

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
  /** Approves a reference list (never whoever edited it). */
  refApprovers: list("ENGINEERING,CEO"),
  standardSetters: list("SALES,COMMERCIAL,ENGINEERING,ENGINEER"),
  commercial: list("SALES,COMMERCIAL"),
  pmo: list("PMO"),
  pmoSide: list("PMO,PROJECTMANAGER"),
  /** Every list, detail and the overview of the module (EnRoles.Readers). */
  readers: list("ENGINEERING,ENGINEER,PMO,CEO,PROJECTMANAGER,PLANNING,SALES,COMMERCIAL,DCC,QA,QC,PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION,WAREHOUSE,WHSUPERVISOR,AUDITOR"),
  /** The questions to the PMO: the six units and engineering (EnUnits.UnitRoles) and the PMO, the CEO and the auditor. */
  infoReaders: list("ENGINEERING,ENGINEER,QA,QC,PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION,COMMERCIAL,WAREHOUSE,WHSUPERVISOR,PLANNING,PMO,CEO,AUDITOR"),
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

/** Engineering asks the PMO as a unit of its own (EnUnit.Engineering = 7). It only asks: it is never a distribution unit and never in a document's unit list, so it is not in enUnits. */
export const engineeringUnit = { value: 7, text: "مهندسی", code: "Engineering" };
/** The units that may ask the PMO: the six distribution units and engineering. */
export const enAskUnits: { value: number; text: string; code: string }[] = [...enUnits, engineeringUnit];

/** Persian names of units from a stored value: a code, a comma-separated list (any case, any separator) or empty (= all units). Never an English name. */
export function unitsText(csv: string | null | undefined): string {
  const parts = String(csv ?? "").split(/[,،;]/).map((x) => x.trim().toLowerCase()).filter(Boolean);
  if (!parts.length) return "همهٔ واحدها";
  return parts.map((c) => enUnits.find((u) => u.code.toLowerCase() === c || String(u.value) === c)?.text ?? (/[\u0600-\u06FF]/.test(c) ? c : "واحد نامشخص")).join("، ");
}
/** The unit of a single code (case-insensitive) or its number, as Persian text. */
export const unitName = (v: string | number | null | undefined) =>
  v === null || v === undefined || v === "" ? "—" : String(v).trim().toLowerCase() === "7" || String(v).trim().toLowerCase() === "engineering" || v === engineeringUnit.text ? engineeringUnit.text : unitsText(String(v));
/** The role names that make a user a member of a unit (EnUnits.UnitRoles). */
export const unitRoles: Record<number, string[]> = {
  1: ["QA"], 2: ["QC"], 3: ["PRODUCTIONMANAGER", "SHOPSUPERVISOR", "CONSTRUCTION"], 4: ["COMMERCIAL"], 5: ["WAREHOUSE", "WHSUPERVISOR"], 6: ["PLANNING"], 7: ["ENGINEERING", "ENGINEER"],
};

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
export const deviationKinds: Record<number, string> = { 1: "کاربرد ندارد", 2: "تسهیل فهرست MDL", 3: "مجوز اضطرار" };
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
    case "dist":
      return s === 1 ? "ok" : "warn";
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

/** Office hours between two instants (employer rule 14: 08:00-19:00 Iran time UTC+03:30, Saturday to Thursday; same math as the server OfficeClock.Between). */
export function officeHoursBetween(fromMs: number, toMs: number): number {
  if (toMs <= fromMs) return 0;
  const OFF = 3.5 * 3600000, H = 3600000, DAY = 24 * H;
  const isWork = (localMs: number) => new Date(localMs).getUTCDay() !== 5;   // Friday only is outside
  const norm = (t: number): number => {
    let day = Math.floor(t / DAY) * DAY;
    if (isWork(day)) {
      if (t < day + 8 * H) return day + 8 * H;
      if (t < day + 19 * H) return t;
    }
    day += DAY;
    while (!isWork(day)) day += DAY;
    return day + 8 * H;
  };
  const b = toMs + OFF;
  let t = norm(fromMs + OFF), total = 0;
  while (t < b) {
    const close = Math.floor(t / DAY) * DAY + 19 * H;
    total += (Math.min(b, close) - t) / H;
    t = norm(close);
  }
  return total;
}

/** Office hours from now to an ISO date-time (negative = past, rounded); null when there is no date. */
export function officeHoursUntil(iso: string | null | undefined): number | null {
  if (!iso) return null;
  const t = new Date(iso).getTime();
  if (!Number.isFinite(t)) return null;
  const now = Date.now();
  return Math.round(t >= now ? officeHoursBetween(now, t) : -officeHoursBetween(t, now));
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

/** The dialog that asks for the data of an action. `ask()` opens it; `run` gets the typed values and answers the API result; a refusal stays inside the dialog (the server's text). */
export function useEnDialog(reload: () => Promise<any> | void) {
  const dlg = ref<null | { title: string; fields: EnField[]; confirmText?: string; hint?: string; okText: string; run: (v: any) => Promise<any> }>(null);
  const error = ref("");
  const busy = ref(false);
  function ask(d: { title: string; fields: EnField[]; confirmText?: string; hint?: string; okText?: string; run: (v: any) => Promise<any> }) {
    error.value = "";
    dlg.value = { okText: "انجام شد.", ...d };
  }
  async function confirm(values: any) {
    if (!dlg.value || busy.value) return;
    busy.value = true;
    let e: any = null;
    try {
      [, e] = await dlg.value.run(values);
    } catch (x: any) {
      e = new Error(x?.message || "عملیات انجام نشد.");
    } finally {
      busy.value = false;
    }
    if (e) {
      error.value = errText(e);
      return;
    }
    ok(dlg.value.okText);
    dlg.value = null;
    await reload();
  }
  return { dlg, error, busy, ask, confirm, close: () => (dlg.value = null) };
}

/** How many one-click writes are running right now; EnAction disables every button meanwhile so a second click (or a second «بله») cannot send the write again. */
export const enWriting = ref(0);

/** An action without a dialog: runs it, shows the server's refusal as a toast, reloads. */
export async function runEn(run: () => Promise<any>, okText: string, reload: () => Promise<any> | void): Promise<boolean> {
  enWriting.value++;
  try {
    const [, e] = await run();
    if (e) {
      fail(e);
      return false;
    }
    ok(okText);
    await reload();
    return true;
  } finally {
    enWriting.value--;
  }
}

/** Persian names of the event kinds of a case (the server writes a code; an unknown code is shown as it is). */
export const eventKinds: Record<string, string> = {
  "tender-document-added": "مدرک به بسته فنی مناقصه افزوده شد", "debts-voided": "بدهی‌های مدارک باطل شد",
  opened: "پرونده باز شد", analysed: "تحلیل انجام شد", proposed: "پیشنهاد ثبت شد", changed: "پیشنهاد اصلاح شد", "eng-confirmed": "تایید مدیر مهندسی", "ceo-approved": "تایید مدیرعامل",
  "returned-by-manager": "برگشت توسط مدیر مهندسی", "returned-by-ceo": "برگشت توسط مدیرعامل", "reclassify-requested": "درخواست تغییر طبقه‌بندی", "concept-saved": "یادداشت طرح مفهومی ذخیره شد",
  "opinion-saved": "نظر فنی ثبت شد", "standard-set": "استاندارد از دیتاشیت ثبت شد", "standard-changed": "استاندارد تغییر کرد", "standard-gap-built": "گزارش تفاوت دو استاندارد ساخته شد",
  "standard-gap-confirmed": "گزارش تفاوت دو استاندارد تایید شد", "tender-package-created": "بسته فنی مناقصه ساخته شد", "tender-released": "بسته فنی منتشر شد", "tender-voided": "بسته فنی باطل شد",
  awarded: "پرونده برنده شد", closed: "پرونده بسته شد", cancelled: "پرونده لغو شد", "handover-drafted": "پیش‌نویس تحویل ساخته شد", "handover-submitted": "تحویل به PMO ارسال شد",
  "handover-accepted": "PMO تحویل را پذیرفت", "handover-returned": "PMO تحویل را برگرداند", "handover-released": "تحویل منتشر شد", "handover-void": "تحویل باطل شد",
  "distribution-drafted": "ابلاغ واحدها آماده شد", "distribution-note-edited": "یادداشت دامنه واحد ویرایش شد", "info-request-void": "پرسش واحد پس گرفته شد", "platform-registered": "پلتفرم ثبت شد",
};

/** Uploads a file through the documents API and answers its id (or the error). */
export async function uploadDoc(file: File): Promise<[string | null, Error | null]> {
  const form = new FormData();
  form.append("file", file);
  const res = await addDocument(form, "engineering", encodeURIComponent(file.name), "engineering");
  const [data, error] = res || [null, new Error("بارگذاری فایل ممکن نشد.")];
  if (error) return [null, error as Error];
  // A refusal (size, type) comes with HTTP 200 and { success: false, message }: the server's text is shown whatever the status.
  if (data && typeof data === "object" && data.success === false) return [null, new Error(data.message || "بارگذاری فایل ممکن نشد.")];
  const id = uploadedId(data);
  return id ? [id, null] : [null, new Error("شناسه فایل بارگذاری‌شده خوانده نشد.")];
}

/** Downloads a stored file with the authorised request (blob). */
export async function downloadDoc(id: string, name = "document") {
  const res = await getDocument(id);
  const [blob, error] = res || [null, new Error("دریافت فایل ممکن نشد.")];
  if (error || !blob) return fail(error || "دریافت فایل ممکن نشد.");
  saveBlob(blob as Blob, name);
}

/** Hours 00..23 for the deadline selector. */
export const hourOptions = Array.from({ length: 24 }, (_, h) => ({ value: h, text: faDigits(String(h).padStart(2, "0")) + ":۰۰" }));
/** Splits an ISO date-time into { date: yyyy-mm-dd, hour } in local time (the Shamsi date field takes yyyy-mm-dd). */
export function splitDue(iso: string | null | undefined, fallbackHours = 72): { date: string; hour: number } {
  let d = iso ? new Date(iso) : new Date(Date.now() + fallbackHours * 3600000);
  if (!Number.isFinite(d.getTime())) d = new Date(Date.now() + fallbackHours * 3600000);
  const p = (n: number) => String(n).padStart(2, "0");
  return { date: `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`, hour: d.getHours() };
}
/** ISO date-time of a date (yyyy-mm-dd) and an hour, or null when the date is empty or invalid. */
export function joinDue(date: string | null | undefined, hour: number | string | null | undefined): string | null {
  if (!date) return null;
  const d = new Date(`${date}T${String(Number(hour) || 0).padStart(2, "0")}:00:00`);
  return Number.isFinite(d.getTime()) ? d.toISOString() : null;
}

/** The single item an inbox link points to (/…/:id): read by its id, so it shows even when it is not on the first page of the list. */
export function useFocusItem(getId: () => number | null, fetchOne: (id: number) => Promise<any>) {
  const item = ref<any>(null);
  async function load() {
    const id = getId();
    item.value = null;
    if (!id) return;
    const [d, e] = await fetchOne(id);
    item.value = e || !d ? null : d;
  }
  /** The loaded rows with the focused item first (and not twice). */
  const merge = (rows: any[]) => (item.value ? [item.value, ...rows.filter((r) => r.id !== item.value.id)] : rows);
  return { item, load, merge };
}

/** The standards to choose from (every approved reference list: code and title); `keep` is a code already stored that stays selectable. */
export async function loadStandardOptions(keep?: string | null): Promise<{ value: string; text: string }[]> {
  const [d, e] = await getEnApprovedCodes();
  if (e) fail(e);
  const options = ((d as any[]) || []).map((x) => ({ value: x.code as string, text: `${x.code} — ${x.title}` }));
  if (keep && !options.some((o) => o.value === keep)) options.unshift({ value: keep, text: keep });
  return options;
}
