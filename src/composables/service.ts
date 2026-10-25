import { PROTECTED_TEXT, productTypes, rialText } from "@/composables/sales";

// Names and helpers of the after-sales service module (release S1 part B). Enum values are numbers in the JSON and match
// Entities/Model/NasbNiro/AfterSales/ServiceEnums.cs exactly. Everything a person reads is Persian (Jalali); the API JSON stays ISO. Money is rial only.
// Date, hours, percent and rial helpers are the ones of production.ts / inventory.ts (re-exported so a service page imports from one place).

export { dateText, dateTimeText, hoursText, persianOf, percentText, qtyText } from "@/composables/production";
export { jalali, rial } from "@/composables/inventory";
export { PROTECTED_TEXT, protectedRialText, rialText, productTypes } from "@/composables/sales";

export const priorities: Record<number, string> = { 1: "بحرانی", 2: "بالا", 3: "عادی", 4: "پایین" };
export const requestTypes: Record<number, string> = { 1: "خرابی", 2: "بازرسی", 3: "راه‌اندازی", 4: "اورهال", 5: "قطعات یدکی", 6: "پرسش فنی", 7: "شکایت" };
export const requestStatuses: Record<number, string> = { 1: "باز", 2: "واگذارشده", 3: "در حال انجام", 4: "منتظر مشتری", 5: "رفع‌شده", 6: "بسته‌شده" };
/** Where the warranty period of an installed unit came from. */
export const warrantySources: Record<number, string> = { 0: "ندارد", 1: "ماه گارانتی از تحویل", 2: "تضمین گارانتی قرارداد", 3: "دستی" };
export const unitStatuses: Record<number, string> = { 1: "در گارانتی", 2: "خارج از گارانتی", 3: "تحت قرارداد LTSA", 4: "از رده خارج" };
export const orderKinds: Record<number, string> = { 1: "میدانی (در سایت)", 2: "کارگاهی" };
export const orderStatuses: Record<number, string> = { 1: "پیش‌نویس سیستم", 2: "برنامه‌ریزی‌شده", 3: "در حال انجام", 4: "بسته‌شده", 5: "لغوشده" };
/** SvWarrantyDecision: 0 is "not decided yet". */
export const warrantyDecisions: Record<number, string> = { 0: "تصمیم‌گیری نشده", 1: "گارانتی", 2: "قابل‌فاکتور", 3: "حسن نیت" };
export const timeKinds: Record<number, string> = { 1: "سفر", 2: "کار", 3: "انتظار" };
export const contractStatuses: Record<number, string> = { 1: "پیش‌نویس", 2: "فعال", 3: "پایان‌یافته", 4: "لغوشده" };
export const spareSources: Record<number, string> = { 1: "دستی", 2: "فهرست قطعات یدکی (MDR)" };
export const surveyKinds: Record<number, string> = { 1: "پس از تحویل", 2: "پس از خدمات" };
export const surveyStatuses: Record<number, string> = { 1: "پیش‌نویس سیستم", 2: "ارسال‌شده", 3: "پاسخ‌داده‌شده", 4: "ردشده" };
/** InventoryDocumentType / InventoryDocumentStatus of the parts documents of a service order are in inventory.ts (documentTypes / documentStatuses). */

/** Persian title of an action string (units, requests, orders, contracts, surveys). */
export const serviceActionTitles: Record<string, string> = {
  confirm: "تایید", decommission: "از رده خارج کردن", reactivate: "فعال‌سازی دوباره",
  assign: "واگذاری", progress: "شروع رسیدگی", wait: "منتظر مشتری", resolve: "رفع مشکل", close: "بستن",
  start: "شروع", cancel: "لغو", activate: "فعال‌سازی", send: "ارسال برای مشتری", skip: "رد کردن",
};

/** SvSettingDto.key -> Persian title (the server also sends a title; this is the fallback). */
export const serviceSettingTitles: Record<string, string> = {
  CostCenterCode: "کد مرکز هزینه پس از فروش (پیش‌فرض AFTERSALES)",
  LabourWorkCenterCode: "کد مرکز کاری که نرخ ساعتی دستمزد استاندارد آن برای مهندسان بدون فیش حقوقی تاییدشده به کار می‌رود",
  SurveyLeadDays: "روز بعد از تحویل برای پیش‌نویس نظرسنجی (پیش‌فرض ۳۰)",
  VisitLeadDays: "روز قبل از موعد بازدید LTSA برای پیش‌نویس دستور خدمات (پیش‌فرض ۳۰)",
};

/** Amount that may be hidden (labourAmount / totalCost of an order, warrantyCost of the dashboard and KPIs): «محرمانه (حقوق)» when hidden. */
export function labourText(value: number | null | undefined, hidden: boolean | null | undefined): string {
  return hidden ? PROTECTED_TEXT : rialText(value);
}

/** Persian month yyyymm (the labour aggregate period) as "1405/07"; the API also sends periodText. */
export function periodText(period: number | null | undefined): string {
  if (!period) return "";
  const s = String(period);
  return s.length === 6 ? `${s.slice(0, 4)}/${s.slice(4)}` : s;
}

/** Product type name; kept here so a service page needs no sales import. */
export const productTypeName = (t: number | null | undefined) => (t ? productTypes[t] || String(t) : "");

/** Badge classes ("ok", "warn", "bad", "info", "") as in the office module. */
export function badgeOfPriority(priority: number): string {
  return priority === 1 ? "bad" : priority === 2 ? "warn" : priority === 3 ? "info" : "";
}
export function badgeOfRequest(status: number, breached = false): string {
  return breached ? "bad" : status === 6 || status === 5 ? "ok" : status === 4 ? "warn" : status === 1 ? "warn" : "info";
}
export function badgeOfUnit(status: number): string {
  return status === 1 ? "ok" : status === 3 ? "info" : status === 4 ? "" : "warn";
}
export function badgeOfServiceOrder(status: number): string {
  return status === 5 ? "bad" : status === 4 ? "ok" : status === 3 ? "info" : "warn";
}
export function badgeOfDecision(decision: number): string {
  return decision === 1 ? "ok" : decision === 2 ? "info" : decision === 3 ? "warn" : "";
}
export function badgeOfServiceContract(status: number): string {
  return status === 2 ? "ok" : status === 4 ? "bad" : status === 3 ? "" : "warn";
}
export function badgeOfSurvey(status: number): string {
  return status === 3 ? "ok" : status === 4 ? "" : status === 2 ? "info" : "warn";
}
/** 1..5 score colour. */
export function badgeOfScore(score: number | null | undefined): string {
  return score === null || score === undefined ? "" : score >= 4 ? "ok" : score >= 3 ? "warn" : "bad";
}
