import { rial } from "@/composables/inventory";

// Names and helpers of the sales module (release S1 part A). Enum values are numbers in the JSON and match
// Entities/Model/NasbNiro/Sales/SalesEnums.cs exactly. Everything a person reads is Persian (Jalali); the API JSON stays ISO. Money is rial only: no currency is shown anywhere.
// Date, hours and percent helpers are the ones of production.ts (re-exported here so a sales page imports from one place).

export { dateText, dateTimeText, hoursText, persianOf, percentText, qtyText } from "@/composables/production";
export { jalali, rial } from "@/composables/inventory";

// ---------------------------------------------------------------- customers and CRM
export const customerTypes: Record<number, string> = { 1: "مصرف‌کننده نهایی", 2: "پیمانکار EPC", 3: "توزیع‌کننده", 4: "دولتی" };
export const industries: Record<number, string> = { 1: "نفت و گاز", 2: "پتروشیمی", 3: "نیروگاه", 4: "فولاد", 5: "قند و شکر", 9: "سایر" };
export const customerStatuses: Record<number, string> = { 1: "مشتری بالقوه", 2: "فعال", 3: "غیرفعال" };
export const activityKinds: Record<number, string> = { 1: "تماس تلفنی", 2: "جلسه", 3: "ایمیل", 4: "بازدید", 5: "نامه" };
export const reasonKinds: Record<number, string> = { 1: "دلیل برد", 2: "دلیل باخت", 3: "دلیل عدم شرکت" };
/** SlLinkSource: what a legacy link points at. */
export const linkSources: Record<number, string> = { 1: "قرارداد", 2: "مناقصه" };
export const linkMethods: Record<number, string> = { 1: "تطبیق خودکار", 2: "دستی", 3: "از پیشنهاد برنده" };

// ---------------------------------------------------------------- inquiries, bid decision
/** What the company sells (the first six equal ItpProductType). Also used by the service module. */
export const productTypes: Record<number, string> = { 1: "توربین بخار", 2: "واحد ORC", 3: "کندانسور", 4: "پمپ", 5: "سیستم روغن روانکاری", 6: "شیر", 7: "قطعات یدکی", 8: "خدمات", 9: "سایر" };
export const inquiryStatuses: Record<number, string> = { 1: "دریافت‌شده", 2: "در حال بررسی", 3: "عدم شرکت", 4: "در حال تهیه پیشنهاد", 5: "ارسال‌شده", 6: "برنده", 7: "بازنده", 8: "لغوشده" };
export const bidStatuses: Record<number, string> = { 1: "پیش‌نویس چک‌لیست", 2: "منتظر تصمیم مدیرعامل", 3: "شرکت در مناقصه", 4: "عدم شرکت" };
export const sanctionChecks: Record<number, string> = { 0: "بررسی نشده", 1: "بدون مانع", 2: "دارای نگرانی" };
/** Decision strings of POST bid-decisions/decide. */
export const bidDecisions: Record<string, string> = { bid: "شرکت در مناقصه", nobid: "عدم شرکت" };

// ---------------------------------------------------------------- estimates, proposals
export const estimateStatuses: Record<number, string> = { 1: "پیش‌نویس", 2: "نهایی", 3: "جایگزین‌شده" };
export const costKinds: Record<number, string> = { 1: "مواد و قطعات", 2: "دستمزد ساخت", 3: "مهندسی", 4: "پیمانکار فرعی", 5: "بسته‌بندی و حمل" };
/** Where the unit cost of an estimate line / a price suggestion came from. */
export const costSources: Record<number, string> = { 0: "دستی", 1: "میانگین بهای انبار", 2: "آخرین خرید", 3: "آخرین سفارش خرید", 4: "نرخ مرکز کار" };
export const proposalStatuses: Record<number, string> = { 1: "پیش‌نویس", 2: "تأییدشده", 3: "ارسال‌شده", 4: "بازنگری‌شده", 5: "برنده", 6: "بازنده", 7: "لغوشده" };
export const proposalResults: Record<string, string> = { won: "برنده", lost: "بازنده" };

// ---------------------------------------------------------------- price lists, quotations, sales orders
export const priceListStatuses: Record<number, string> = { 1: "پیش‌نویس", 2: "تأییدشده", 3: "بازنشسته" };
export const quotationStatuses: Record<number, string> = { 1: "پیش‌نویس", 2: "ارسال‌شده", 3: "پذیرفته‌شده", 4: "ردشده", 5: "منقضی‌شده", 6: "لغوشده" };
export const orderStatuses: Record<number, string> = { 1: "پیش‌نویس", 2: "تأییدشده", 3: "تحویل بخشی", 4: "تحویل‌شده", 5: "فاکتورشده", 6: "بسته‌شده", 7: "لغوشده" };

/** Persian title of an action string (every sales action endpoint). */
export const salesActionTitles: Record<string, string> = {
  review: "شروع بررسی", cancel: "لغو", finalize: "نهایی کردن", revise: "بازنگری جدید", approve: "تایید", submit: "ارسال", retire: "بازنشسته کردن",
  send: "ارسال برای مشتری", accept: "پذیرش", reject: "رد", expire: "منقضی کردن", confirm: "تایید سفارش", close: "بستن", bid: "شرکت در مناقصه", nobid: "عدم شرکت",
  won: "برنده", lost: "بازنده",
};

/** SlSettingDto.key -> Persian title (the server also sends a title; this is the fallback). */
export const salesSettingTitles: Record<string, string> = {
  ProposalApprovalThresholdRial: "سقف تایید فروش (ریال): پیشنهاد بالاتر از آن فقط با مدیرعامل تایید می‌شود",
};

/** Shown instead of a payroll-protected amount (the API sends null with a *Hidden flag). */
export const PROTECTED_TEXT = "محرمانه (حقوق)";

/** Rial amount for a table cell: thousands separators; null/undefined shows a dash. No currency is shown. */
export function rialText(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return "-";
  return rial(value);
}

/** A protected amount: «محرمانه (حقوق)» when the API hid it, otherwise rial. */
export function protectedRialText(value: number | null | undefined, hidden: boolean | null | undefined): string {
  return hidden ? PROTECTED_TEXT : rialText(value);
}

/** Badge classes ("ok", "warn", "bad", "info", "") as in the office module. */
export function badgeOfInquiry(status: number): string {
  return status === 8 || status === 7 || status === 3 ? "bad" : status === 6 ? "ok" : status === 5 || status === 4 ? "info" : "warn";
}
export function badgeOfBid(status: number): string {
  return status === 3 ? "ok" : status === 4 ? "bad" : status === 2 ? "warn" : "";
}
export function badgeOfEstimate(status: number): string {
  return status === 2 ? "ok" : status === 3 ? "" : "warn";
}
export function badgeOfProposal(status: number): string {
  return status === 5 ? "ok" : status === 6 || status === 7 ? "bad" : status === 3 ? "info" : status === 2 ? "info" : status === 4 ? "" : "warn";
}
export function badgeOfPriceList(status: number): string {
  return status === 2 ? "ok" : status === 3 ? "" : "warn";
}
export function badgeOfQuotation(status: number): string {
  return status === 3 ? "ok" : status === 4 || status === 5 || status === 6 ? "bad" : status === 2 ? "info" : "warn";
}
export function badgeOfSalesOrder(status: number): string {
  return status === 7 ? "bad" : status >= 4 ? "ok" : status >= 2 ? "info" : "warn";
}
