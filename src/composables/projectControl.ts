import { ref } from "vue";
import { NO_FORM_CODE_TEXT } from "@/composables/quality";

// Names and helpers for the project control (PMO) pages. Enums of the API (PcEnums.cs) are numbers in the JSON.
export { NO_FORM_CODE_TEXT };

export const categories: Record<number, string> = { 1: "ساخت (MA)", 2: "قطعات یدکی (SP)", 3: "خدمات (SE)" };
export const initiationSteps: Record<number, string> = {
  1: "دریافت ابلاغ قرارداد", 2: "صدور کد قرارداد", 3: "اطلاع‌رسانی به واحدها", 4: "انتخاب مدیر پروژه", 5: "تایید منشور پروژه",
  6: "تایید برنامه اجرایی (PEP)", 7: "جلسه شروع با کارفرما", 8: "درخواست BOM", 9: "پیش‌نویس WBS", 10: "تایید خط مبنا",
};
export const gateRoleTitles: Record<string, string> = {
  COMMERCIAL: "بازرگانی", PLANNING: "برنامه‌ریزی", CEO: "مدیرعامل", PMO: "معاون PMO", PROJECTMANAGER: "مدیر پروژه", SYSTEM: "سیستم",
};
export const reviewKinds: Record<number, string> = { 1: "محصول استاندارد", 2: "سفارش خاص" };
export const reviewDecisions: Record<number, string> = { 0: "در انتظار", 1: "پذیرش", 2: "رد", 3: "نیاز به اصلاح" };
export const amendmentTypes: Record<number, string> = { 1: "تعداد", 2: "زمان", 3: "قیمت" };
export const amendmentOrigins: Record<number, string> = { 1: "کارفرما", 2: "شرکت" };
export const accrualModes: Record<number, string> = { 1: "خطی در مدت فعالیت", 2: "در پایان فعالیت" };
/** DataDateWeekday: 0 = Sunday ... 6 = Saturday. */
export const weekdays: Record<number, string> = { 0: "یکشنبه", 1: "دوشنبه", 2: "سه‌شنبه", 3: "چهارشنبه", 4: "پنجشنبه", 5: "جمعه", 6: "شنبه" };

export const milestoneKinds: Record<number, string> = { 1: "پیش‌پرداخت", 2: "پیشرفت", 3: "تحویل", 4: "حسن انجام کار", 9: "سایر" };
export const milestoneTriggers: Record<number, string> = { 1: "تاریخ", 2: "درصد پیشرفت", 3: "انجام نقطه عطف", 4: "برگه ترخیص", 5: "تحویل محموله", 6: "صورتجلسه تحویل", 7: "نامه کارفرما" };
export const milestoneStatuses: Record<number, string> = { 0: "برنامه‌ریزی‌شده", 1: "سررسید شده", 2: "صورت‌وضعیت صادر شد", 3: "فاکتور شد", 4: "پرداخت شد" };
export const guaranteeTypes: Record<number, string> = { 1: "شرکت در مناقصه", 2: "پیش‌پرداخت", 3: "حسن انجام کار", 4: "دوره تضمین", 9: "سایر" };
export const guaranteeStatuses: Record<number, string> = { 1: "درخواست شد", 2: "صادر شد", 3: "فعال", 4: "در انتظار تمدید", 5: "منقضی", 6: "آزاد شد", 7: "ابطال" };
export const guaranteeReleases: Record<number, string> = { 1: "تحویل نهایی", 2: "پایان دوره تضمین", 3: "نامه کارفرما" };
export const guaranteeIssuers: Record<number, string> = { 1: "بانک", 2: "صندوق" };
export const guaranteeEventKinds: Record<number, string> = { 1: "صدور", 2: "تمدید", 3: "ابطال" };
export const guaranteeSources: Record<number, string> = { 1: "ضمانت‌نامه قرارداد", 2: "ضمانت‌نامه مناقصه" };
export const penaltyStatuses: Record<number, string> = { 0: "محاسبه‌شده", 1: "بخشوده", 2: "مصالحه‌شده" };
export const variationStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده", 2: "تایید کارفرما", 3: "ردشده" };

export const wbsTypes: Record<number, string> = { 1: "پروژه", 2: "فاز", 3: "محصول", 4: "مجموعه", 5: "قطعه", 6: "بسته کاری", 7: "نقطه عطف" };
export const phases: Record<number, string> = { 1: "مهندسی", 2: "تامین", 3: "ساخت", 4: "آزمون", 5: "تحویل", 6: "اختتام" };
export const sourceTypes: Record<number, string> = { 1: "فرآیند ساخت", 2: "مجموعه", 3: "فعالیت ITP", 4: "مدرک مهندسی", 5: "مرحله چک‌لیست", 6: "ردیف سفارش خرید", 7: "دستی", 8: "نقطه عطف" };
export const dependencyTypes: Record<number, string> = { 1: "FS", 2: "SS", 3: "FF", 4: "SF" };
export const dependencyOrigins: Record<number, string> = { 1: "BOM", 2: "مجموعه", 3: "ترتیب فرآیند", 4: "تامین", 5: "دستی" };
export const constraints: Record<number, string> = {
  0: "بدون قید", 1: "شروع نه زودتر از", 2: "پایان نه زودتر از", 3: "شروع نه دیرتر از", 4: "پایان نه دیرتر از", 5: "باید شروع شود در", 6: "باید پایان یابد در",
};
export const progressMethods: Record<number, string> = {
  0: "دستی", 1: "ارسال مدرک مهندسی", 2: "رسید تامین", 3: "مدارک خدمات", 4: "پیمانکار", 5: "چک‌لیست مونتاژ", 6: "نقاط ITP", 7: "مقداری", 8: "نقطه عطف",
};
export const manualMethods = [0, 4, 7];
export const activityStatuses: Record<number, string> = { 0: "شروع نشده", 1: "در حال انجام", 2: "انجام شد", 3: "متوقف" };
export const milestoneEvents: Record<number, string> = { 0: "—", 1: "صدور برگه ترخیص", 2: "ارسال محموله", 3: "تحویل محموله", 4: "دریافت صورتجلسه تحویل" };

export const baselineKinds: Record<number, string> = { 1: "اصلی", 2: "تفصیلی", 3: "هزینه" };
export const baselineStatuses: Record<number, string> = { 0: "پیش‌نویس (در انتظار امضا)", 1: "امضای مدیران کامل", 2: "تاییدشده", 3: "جایگزین‌شده" };
export const costClasses: Record<number, string> = { 1: "مستقیم", 2: "غیرمستقیم", 3: "ذخیره احتمالی", 4: "ذخیره مدیریتی" };
export const resourceClasses: Record<number, string> = { 1: "نیروی انسانی", 2: "تجهیزات", 3: "مواد", 4: "پیمانکاری" };
export const costKinds: Record<number, string> = { 1: "مواد", 2: "پیمانکاری", 3: "دستمزد", 4: "سربار", 5: "خدمات خریداری‌شده" };
export const manualStates: Record<number, string> = { 0: "در انتظار تایید", 1: "تایید شد", 2: "رد شد" };

export const changeOrigins: Record<number, string> = { 1: "دستور تغییر کارفرما", 2: "درخواست داخلی" };
export const changeTypes: Record<number, string> = {
  1: "محدوده / WBS", 2: "زمان‌بندی و اولویت‌ها", 3: "مفروضات", 4: "محدودیت‌ها", 5: "تیم پروژه", 6: "روش اجرا", 7: "استانداردها",
  8: "شرایط تایید کارفرما", 9: "برنامه ارتباطات", 10: "سیستم اندازه‌گیری پیشرفت",
};
export const changeStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ثبت‌شده", 2: "در تحلیل", 3: "تصمیم‌گیری شد", 4: "در حال اجرا", 5: "بسته شد", 6: "ردشده" };
export const changeDecisions: Record<number, string> = { 0: "—", 1: "تایید", 2: "رد", 3: "تعویق", 4: "نیاز به اطلاعات بیشتر" };
export const votes: Record<number, string> = { 0: "رای نداده", 1: "موافق", 2: "مخالف" };
export const claimDirections: Record<number, string> = { 1: "علیه کارفرما", 2: "علیه شرکت" };
export const claimStatuses: Record<number, string> = { 0: "طرح موضوع", 1: "کمّی‌سازی شد", 2: "اعلام شد", 3: "در مذاکره", 4: "تسویه شد", 5: "انصراف", 6: "دعوی حقوقی" };

export const issueStates: Record<number, string> = { 0: "صادر نشده", 1: "ارسال اول", 2: "ارسال دوم", 3: "تایید شده" };
export const transmittalDirections: Record<number, string> = { 1: "ارسالی", 2: "دریافتی" };
export const transmittalPurposes: Record<number, string> = { 1: "جهت تایید", 2: "جهت اطلاع", 3: "As-Built" };

export const reportKinds: Record<number, string> = { 1: "گزارش ماهانه کارفرما", 2: "گزارش هفتگی داخلی", 3: "گزارش ماهانه مدیرعامل", 4: "کتاب نهایی (شرکتی)", 5: "کتاب نهایی (کارفرما)" };
export const shipmentStatuses: Record<number, string> = { 0: "برنامه‌ریزی‌شده", 1: "بسته‌بندی شد", 2: "مجوز ارسال", 3: "ارسال شد", 4: "تحویل شد", 5: "صورتجلسه تحویل دریافت شد" };

/** Roles that appear on the baseline signature list and the change board, as the job title of the manager. */
export const unitManagerTitles: Record<string, string> = {
  ENGINEERING: "مدیر مهندسی", ENGINEER: "مهندس", CONSTRUCTION: "مدیر تولید", ASSEMBLER: "مسئول مونتاژ", QC: "مدیر کنترل کیفیت", QA: "مدیر تضمین کیفیت",
  COMMERCIAL: "مدیر بازرگانی", WAREHOUSE: "انباردار", WHSUPERVISOR: "سرپرست انبار", PLANNING: "مدیر برنامه‌ریزی و کنترل پروژه",
  PROJECTMANAGER: "مدیر پروژه", PMO: "معاون PMO", FINANCIAL: "مدیر مالی", CEO: "مدیرعامل", DCC: "کنترل مدارک پروژه",
};
export const unitTitle = (role: string) => unitManagerTitles[String(role || "").toUpperCase()] || role;

/** Form codes the procedures name. Every other printed form has no company code yet (never invent one). */
export const pcFormCodes = { changeRequest: "FO-PO-011", changeAnalysis: "FO-PO-015" } as const;
export const distributionPlanning = "توزیع نسخ: ۱- برنامه‌ریزی و کنترل پروژه ۲- مدیر پروژه ۳- واحدهای ذی‌ربط";
export const distributionDcc = "توزیع نسخ: ۱- کنترل مدارک پروژه ۲- مهندسی ۳- مدیر پروژه ۴- کارفرما";
export const distributionCommercial = "توزیع نسخ: ۱- بازرگانی ۲- مالی ۳- معاون PMO";
export const distributionShipping = "توزیع نسخ (۳ نسخه): ۱- برنامه‌ریزی ۲- کنترل کیفیت ۳- انبار / کارفرما";

// ---------------------------------------------------------------- numbers and colours
export const pct = (v: number | null | undefined, digits = 1): string => (v === null || v === undefined ? "—" : Number(v).toLocaleString("en-US", { maximumFractionDigits: digits, minimumFractionDigits: 0 }) + "٪");
export const idx = (v: number | null | undefined): string => (v === null || v === undefined ? "—" : Number(v).toFixed(2));
export const fx = (v: number | null | undefined, digits = 1): string => (v === null || v === undefined ? "—" : Number(v).toLocaleString("en-US", { maximumFractionDigits: digits }));

/** green / amber / red / none, as sent by the API (SpiStatus, CpiStatus), to the badge class of the pages. */
export const statusBadge = (s: string | null | undefined): string => (s === "red" ? "bad" : s === "amber" ? "warn" : s === "green" ? "ok" : "");
export const statusText = (s: string | null | undefined): string => (s === "red" ? "قرمز" : s === "amber" ? "زرد" : s === "green" ? "سبز" : "—");

/** Colour of an index when only the number is known: the API default thresholds are 0.95 (amber) and 0.90 (red). */
export function indexBadge(v: number | null | undefined, amber = 0.95, red = 0.9): string {
  if (v === null || v === undefined) return "";
  return v < red ? "bad" : v < amber ? "warn" : "ok";
}

export function dateOnly(value: string | null | undefined): string | null {
  return value ? value.slice(0, 10) : null;
}

export const addDays = (iso: string, days: number): string => {
  const d = new Date(iso.slice(0, 10) + "T00:00:00Z");
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
};

export const dayDiff = (a: string, b: string): number =>
  Math.round((new Date(b.slice(0, 10) + "T00:00:00Z").getTime() - new Date(a.slice(0, 10) + "T00:00:00Z").getTime()) / 86400000);

// ---------------------------------------------------------------- the contract (project) chosen on the pages
const KEY = "pcContractId";
function readStored(): number | null {
  try {
    const v = Number(localStorage.getItem(KEY));
    return v > 0 ? v : null;
  } catch {
    return null;
  }
}
/** One chosen contract for all the project control pages (kept in the browser only as a convenience). */
export const pcContractId = ref<number | null>(readStored());
export function setPcContract(id: number | null) {
  pcContractId.value = id;
  try {
    if (id) localStorage.setItem(KEY, String(id));
    else localStorage.removeItem(KEY);
  } catch {
    /* storage blocked: the choice lives only in this tab */
  }
}

/** Rows of the API contract list are used to show the code and the title of the chosen project. */
export const contractsCache = ref<any[]>([]);
export const contractLabel = (id: number | null | undefined): string => {
  const c = contractsCache.value.find((x) => x.id === id);
  return c ? `${c.code} - ${c.title}` : id ? "#" + id : "";
};

/**
 * The content of a stored report is JSON written by the API with the default serializer (property names start with a capital
 * letter, unlike the rest of the API). Keys are turned into camel case so the pages read one style.
 */
export function camelKeys(value: any): any {
  if (Array.isArray(value)) return value.map(camelKeys);
  if (value && typeof value === "object") {
    const out: any = {};
    Object.keys(value).forEach((k) => (out[k.charAt(0).toLowerCase() + k.slice(1)] = camelKeys(value[k])));
    return out;
  }
  return value;
}
export function parseContent(text: string | null | undefined): any {
  if (!text) return null;
  try {
    return camelKeys(JSON.parse(text));
  } catch {
    return null;
  }
}

/** Milestone lines of the client report are written as "title: Planned" with the English status name. */
export const milestoneStatusNames: Record<string, string> = { Planned: "برنامه‌ریزی‌شده", Due: "سررسید شده", StatementIssued: "صورت‌وضعیت صادر شد", Invoiced: "فاکتور شد", Paid: "پرداخت شد" };
export function milestoneLine(text: string): string {
  const i = String(text || "").lastIndexOf(": ");
  if (i < 0) return text;
  const name = text.slice(i + 2).trim();
  return text.slice(0, i) + ": " + (milestoneStatusNames[name] || name);
}

/** Sums a number field of rows. */
export const sumOf = (rows: any[], key: string): number => rows.reduce((s, r) => s + (Number(r[key]) || 0), 0);
