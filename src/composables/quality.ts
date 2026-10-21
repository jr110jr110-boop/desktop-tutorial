import { ref, type Ref } from "vue";
import { fail } from "@/composables/inventory";

// Names for the quality pages (enums of the API, QualityEnums.cs). Values are numbers in the JSON.
export const productTypes: Record<number, string> = { 1: "توربین بخار", 2: "واحد ORC", 3: "کندانسور", 4: "پمپ", 5: "سیستم روغن روانکاری", 6: "شیر", 9: "سایر" };
export const testTypes: Record<number, string> = {
  1: "آزمون شیمیایی", 2: "آزمون مکانیکی", 3: "ابعادی", 4: "بازرسی چشمی", 5: "UT", 6: "MT", 7: "PT", 8: "RT", 9: "PMI", 10: "سختی‌سنجی",
  11: "هیدرواستاتیک", 12: "تست کارکرد مکانیکی", 13: "تست عملکرد", 14: "بالانس", 15: "رنگ و پوشش", 16: "بازرسی چشمی جوش", 17: "بررسی مدارک", 18: "بسته‌بندی", 99: "سایر",
};
export const points: Record<number, string> = { 0: "—", 1: "H", 2: "W", 3: "R", 4: "RA", 5: "SW", 6: "A" };
export const pointHints: Record<number, string> = {
  0: "بدون نقطه کنترل", 1: "H - توقف (Hold)", 2: "W - شاهد (Witness)", 3: "R - بررسی مدارک (Review)", 4: "RA - بررسی و پذیرش", 5: "SW - شاهد نمونه‌ای", 6: "A - تایید (Approve)",
};
export const reportTypes: Record<number, string> = {
  1: "PT - مایع نافذ", 2: "MT - ذرات مغناطیسی", 3: "UT - فراصوتی", 4: "RT - رادیوگرافی", 5: "سختی‌سنجی", 6: "PMI", 7: "هیدرواستاتیک", 8: "تست کارکرد مکانیکی",
  9: "تست عملکرد", 10: "بالانس", 11: "رنگ و پوشش", 12: "ابعادی", 99: "سایر",
};
export const itpStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده برای کارفرما", 2: "تاییدشده کارفرما", 3: "در حال اجرا", 4: "بسته‌شده" };
export const activityStatuses: Record<number, string> = { 0: "برنامه‌ریزی‌شده", 1: "اعلام‌شده", 2: "انجام‌شده", 3: "معاف", 4: "غیرقابل‌اجرا" };
export const releaseParties: Record<number, string> = { 1: "شرکت (V)", 2: "بازرس ثالث (TPI)", 3: "کارفرما (C)" };
export const releaseActions: Record<number, string> = { 1: "شاهد بود", 2: "بررسی کرد", 3: "تایید کرد", 4: "معاف کرد" };
export const recordTypes: Record<number, string> = { 1: "حین ساخت", 2: "نهایی", 3: "ابعادی (DCF)", 4: "نمونه اول", 5: "کارگاه پیمانکار", 6: "لی‌اوت" };
export const qcResults: Record<number, string> = { 1: "قبول", 2: "رد", 3: "دوباره‌کاری", 4: "توقف" };
export const reportStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده", 2: "تاییدشده", 3: "ردشده" };
export const reportResults: Record<number, string> = { 0: "ارزیابی نشده", 1: "قبول", 2: "مردود" };
export const equipmentTypes: Record<number, string> = { 1: "ابزار دستی ابعادی", 2: "گیج", 3: "فشارسنج", 4: "دماسنج", 5: "تجهیز الکتریکی", 6: "تجهیز NDT", 7: "ابزار دقیق میز تست", 99: "سایر" };
export const equipmentStatuses: Record<number, string> = { 0: "کالیبره", 1: "نزدیک سررسید", 2: "سررسیدگذشته", 3: "خارج از رده", 4: "نیاز به کالیبراسیون ندارد" };
export const ootStatuses: Record<number, string> = { 0: "باز", 1: "بسته" };
export const ootAssessments: Record<number, string> = { 0: "در انتظار ارزیابی", 1: "بدون اثر", 2: "بازرسی مجدد", 3: "عدم انطباق (NCR)" };
export const qualificationKinds: Record<number, string> = { 1: "جوشکار", 2: "اپراتور جوشکاری", 3: "پرسنل NDT", 4: "بازرس" };
export const ndtLevels: Record<number, string> = { 0: "—", 1: "I", 2: "II", 3: "III" };
export const qualificationSchemes: Record<number, string> = { 1: "ASME IX", 2: "ASNT SNT-TC-1A", 3: "ISO 9712", 9: "سایر" };
export const employerKinds: Record<number, string> = { 1: "شرکت", 2: "پیمانکار", 3: "تامین‌کننده" };
export const qualificationStates: Record<number, string> = { 0: "معتبر", 1: "نزدیک انقضا / قطع تداوم", 2: "منقضی", 3: "قطع تداوم (نیاز به آزمون مجدد)", 4: "ابطال‌شده" };
export const wpsStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده", 2: "منسوخ" };
export const vtResults: Record<number, string> = { 0: "در انتظار", 1: "قبول", 2: "رد" };
export const certificateTypes: Record<number, string> = { 1: "EN 10204 - 2.2", 2: "EN 10204 - 3.1", 3: "EN 10204 - 3.2" };
export const certificateResults: Record<number, string> = { 0: "در انتظار تایید QC", 1: "پذیرفته شد", 2: "رد شد" };
export const rootCauseMethods: Record<number, string> = { 0: "—", 1: "۵ چرا", 2: "استخوان ماهی", 3: "8D", 9: "سایر" };
export const capaSources: Record<number, string> = { 1: "عدم انطباق (NCR)", 2: "یافته ممیزی", 3: "بازنگری مدیریت", 4: "شکایت مشتری", 5: "تحلیل داده", 6: "ریسک", 9: "سایر" };
export const capaStatuses: Record<number, string> = { 0: "باز (ناقص)", 1: "اقدام‌ها تعریف شد", 2: "اجرا شد", 3: "اثربخشی تایید شد", 4: "بسته شد", 5: "لغو شد" };
export const releaseNoteStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "صادر شد", 2: "لغو شد" };
export const complaintStatuses: Record<number, string> = { 0: "دریافت شد", 1: "در حال بررسی", 2: "پاسخ داده شد", 3: "بسته شد" };
export const documentTypes: Record<number, string> = { 1: "رویه (PR)", 2: "دستورالعمل (WI)", 3: "فرم (FO)", 4: "BL", 5: "نقشه", 6: "مشخصات فنی", 9: "سایر" };
export const documentStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در بررسی", 2: "تاییدشده", 3: "معتبر", 4: "منسوخ" };
export const auditStatuses: Record<number, string> = { 0: "برنامه‌ریزی‌شده", 1: "انجام‌شده", 2: "بسته‌شده" };
export const findingTypes: Record<number, string> = { 1: "عدم انطباق عمده", 2: "عدم انطباق جزئی", 3: "مشاهده", 4: "فرصت بهبود" };
export const reviewStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "برگزار شد", 2: "بسته شد" };
export const severities: Record<number, string> = { 0: "تعیین نشده", 1: "جزئی (Minor)", 2: "عمده (Major)" };
export const alertLevelClass: Record<string, string> = { danger: "alert-danger", warning: "alert-warning", info: "alert-info" };

/** Settings keys of the API (QualitySettingKeys) are shown with the title the API sends; this is the unit hint. */
export const NO_FORM_CODE_TEXT = "کد فرم: (توسط تضمین کیفیت تعیین شود)";

/** Codes of the company forms. Only these exist; every other printed form has no code and says so (never invent one). */
export const qualityFormCodes = {
  ncr: "FO-QC-013",
  capa: "FO-QA-017",
  calibrationControl: "FO-QC-007",
  unitEquipmentList: "FO-QC-008",
  calibrationNeeds: "FO-QC-009",
  calibrationHandover: "FO-QC-010",
  equipmentIdentity: "FO-QC-011",
  pt: "FO-QC-016",
  mt: "FO-QC-017",
  dimensional: "FO-QC-14",
  inspectionOrder: "FO-QC-018",
  auditReport: "FO-QA-018",
  managementReview: "FO-QA-022",
} as const;

export const distributionQc = "توزیع نسخ: ۱- کنترل کیفیت ۲- مهندسی ۳- تولید ۴- تضمین کیفیت";
export const distributionQa = "توزیع نسخ: ۱- تضمین کیفیت ۲- مدیریت ۳- واحدهای ذی‌ربط";

/** Date value for <input type="date">. */
export function dateInput(value: string | null | undefined): string | null {
  return value ? value.slice(0, 10) : null;
}

export function badgeOf(kind: "ok" | "warn" | "bad" | "info" | ""): string {
  return kind;
}

/** Colour of a computed state (equipment, qualification, result). */
export const equipmentBadge = (s: number) => (s === 0 || s === 4 ? "ok" : s === 1 ? "warn" : "bad");
export const qualificationBadge = (s: number) => (s === 0 ? "ok" : s === 1 ? "warn" : "bad");
export const resultBadge = (r: number) => (r === 1 ? "ok" : r === 2 ? "bad" : "");

/** Text of a point pair "V:H T:None C:W" sent by the pending points endpoint. */
const pointNames: Record<string, string> = { None: "—", H: "H", W: "W", R: "R", RA: "RA", SW: "SW", A: "A" };
export function pointsText(raw: string): string {
  return (raw || "")
    .split(" ")
    .map((p) => {
      const [party, value] = p.split(":");
      return `${party}:${pointNames[value] || value}`;
    })
    .join("  ");
}

/** Points of an activity as short text, for example "V:H  C:W". */
export function activityPoints(a: any): string {
  const parts = [] as string[];
  if (a.pointVendor) parts.push("V:" + points[a.pointVendor]);
  if (a.pointTpi) parts.push("TPI:" + points[a.pointTpi]);
  if (a.pointClient) parts.push("C:" + points[a.pointClient]);
  return parts.join("  ") || "—";
}

/** Paged list helper: the API answers { data, totalItems }. */
export function usePaged(fetcher: (params: any) => Promise<any>, filters: Ref<any>, pageSize = 50) {
  const rows = ref<any[]>([]);
  const total = ref(0);
  const page = ref(1);
  const loading = ref(false);
  async function load() {
    loading.value = true;
    const [data, error] = await fetcher({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
    loading.value = false;
    if (error) return fail(error);
    rows.value = Array.isArray(data) ? data : data?.data || [];
    total.value = Array.isArray(data) ? data.length : data?.totalItems ?? rows.value.length;
  }
  function search() {
    page.value = 1;
    return load();
  }
  function go(delta: number) {
    page.value += delta;
    return load();
  }
  const pages = () => Math.max(1, Math.ceil(total.value / pageSize));
  return { rows, total, page, loading, load, search, go, pages };
}

export const reviewInputCategories: Record<number, string> = {
  1: "وضعیت اقدامات جلسات قبل", 2: "تغییر در موضوعات داخلی و خارجی", 3: "رضایت مشتری و بازخورد", 4: "میزان تحقق اهداف کیفیت",
  5: "عملکرد فرایندها و انطباق محصول", 6: "عدم انطباق‌ها و اقدامات اصلاحی", 7: "نتایج پایش و اندازه‌گیری", 8: "نتایج ممیزی‌ها",
  9: "عملکرد تامین‌کنندگان بیرونی", 10: "کفایت منابع", 11: "اثربخشی اقدامات مقابله با ریسک", 12: "فرصت‌های بهبود",
};
