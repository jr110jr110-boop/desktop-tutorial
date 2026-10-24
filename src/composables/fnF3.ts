import { ref, type Ref } from "vue";
import { getFiscalYears } from "@/api/FnAPIs";
import { fail, jalali } from "@/composables/inventory";

// Names and helpers of the accounting module, release F3 (Entities/Finance/Completeness*.cs). Enum values are numbers in the JSON.

export const diffKinds: Record<number, string> = { 0: "مطابق", 1: "فقط در PMIS", 2: "فقط در سپیدار", 3: "مبلغ متفاوت" };
export const exportStatuses: Record<number, string> = { 1: "فعال", 2: "جایگزین‌شده" };
export const rateSources: Record<number, string> = { 1: "دستی", 2: "بانک مرکزی", 3: "بازار" };
export const rateTypes: Record<number, string> = { 1: "رسمی", 2: "توافقی", 3: "آزاد" };
export const lcStatuses: Record<number, string> = { 1: "گشایش‌شده", 2: "اسناد دریافت‌شده", 3: "پرداخت‌شده", 4: "منقضی", 5: "لغوشده" };
export const lcPostingKinds: Record<number, string> = { 1: "سپرده", 2: "کارمزد", 3: "تسویه", 4: "آزادسازی سپرده" };
export const budgetKinds: Record<number, string> = { 1: "سالانه", 2: "بازنگری" };
export const budgetStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده", 2: "جایگزین‌شده" };
export const commitModes: Record<number, string> = { 0: "بدون کنترل", 1: "هشدار", 2: "مسدودکننده" };
export const overheadBases: Record<number, string> = { 1: "ساعت کار نیروی انسانی", 2: "ساعت ماشین", 3: "هزینه دستمزد مستقیم", 4: "هزینه مواد مستقیم" };
export const revenueMethods: Record<number, string> = { 0: "هنگام صدور فاکتور", 1: "درصد پیشرفت" };
export const deferralKinds: Record<number, string> = { 1: "پیش‌پرداخت هزینه", 2: "درآمد انتقالی (پیش‌دریافت)" };
export const deferralStatuses: Record<number, string> = { 1: "فعال", 2: "تکمیل‌شده", 3: "لغوشده" };
export const provisionKinds: Record<number, string> = { 1: "ضمانت", 2: "حقوقی", 3: "پاداش", 4: "مرخصی", 9: "سایر" };
export const facilityTypes: Record<number, string> = { 1: "وام", 2: "مرابحه", 3: "اضافه‌برداشت", 4: "لیزینگ" };
export const scheduleKinds: Record<number, string> = { 1: "اصل مساوی", 2: "اقساط مساوی", 3: "دلخواه" };
export const loanStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "فعال", 2: "تسویه‌شده", 3: "لغوشده" };
export const installmentStatuses: Record<number, string> = { 0: "پرداخت‌نشده", 1: "پرداخت‌شده" };
export const draftApproved: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده" };
export const taxAdjustmentKinds: Record<number, string> = { 1: "هزینه غیرقابل قبول مالیاتی", 2: "معافیت", 3: "زیان قابل انتقال", 9: "سایر" };
export const agingBuckets: Record<string, string> = { Current: "جاری (تا ۳۰ روز)", "31-60": "۳۱ تا ۶۰ روز", "61-90": "۶۱ تا ۹۰ روز", Over90: "بیش از ۹۰ روز" };
export const closeStatuses: Record<string, string> = { Green: "سبز", Red: "قرمز", Yellow: "هشدار", NotApplicable: "نامربوط" };
export const importRowStatuses: Record<string, string> = { Existing: "موجود", Mapped: "نگاشت‌شده", New: "جدید", Review: "نیازمند بررسی", Error: "خطا" };
export const budgetGroupBys: Record<string, string> = { account: "حساب", costcenter: "مرکز هزینه", contract: "پروژه" };
export const detailImportTypes: Record<string, string> = { Person: "شخص", Supplier: "تامین‌کننده", Customer: "مشتری", Employee: "کارمند", Bank: "بانک", Cash: "صندوق", PettyCash: "تنخواه", Project: "پروژه", WorkOrder: "دستور ساخت", CostCenter: "مرکز هزینه", Asset: "دارایی", Other: "سایر" };
export const auditActions: Record<string, string> = {
  Create: "ایجاد", Update: "ویرایش", Delete: "حذف", Approve: "تایید", Post: "ثبت", Cancel: "ابطال", Reverse: "برگشت", Finalize: "قطعی‌سازی", Check: "کنترل", Lock: "قفل", Close: "بستن", Reopen: "بازگشایی", Override: "عبور از کنترل",
};
export const NO_ENTITY = "شرکت اصلی";

/** Footers of the F3 print forms. */
export const distributionAudit = "توزیع نسخ: ۱- حسابرس ۲- مدیر مالی ۳- مدیرعامل";

/** Status text of a checklist or import line: unknown values are shown as they are. */
export const nameOf = (names: Record<string | number, string>, key: any): string => names[key] ?? String(key ?? "");

/** Rows of the periods of every fiscal year, newest first: the pick lists of the period based screens. */
export function usePeriods() {
  const years: Ref<any[]> = ref([]);
  const periods: Ref<any[]> = ref([]);
  async function load() {
    const [data, error] = await getFiscalYears();
    if (error) return fail(error);
    years.value = data || [];
    periods.value = (data || []).flatMap((y: any) => (y.periods || []).map((p: any) => ({ ...p, yearTitle: y.title, fiscalYearId: y.id })));
  }
  const label = (p: any) => `${p.title} - ${p.yearTitle}`;
  return { years, periods, load, label };
}

/** A plain-text table (paste or file) to rows of cells: tab, semicolon or comma separated; a header row is dropped when the first cell is not a number or a code. */
export function parseTable(text: string): string[][] {
  return String(text || "")
    .replace(/\r/g, "")
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length)
    .map((line) => (line.includes("\t") ? line.split("\t") : line.includes(";") ? line.split(";") : line.split(",")).map((c) => c.trim()));
}

/** Free text from the API (audit summaries, JSON images) may carry ISO dates: they are shown as Persian dates, never as Gregorian ones. */
export function jalaliInText(text: string | null | undefined): string {
  if (!text) return "";
  return String(text).replace(/\b(\d{4})-(\d{2})-(\d{2})(?:[T ][0-9:.]+(?:Z|[+-]\d{2}:\d{2})?)?/g, (m) => jalali(m.slice(0, 10)) || m);
}

// ---- F3b
export const guaranteeDirections: Record<number, string> = { 1: "صادرشده (ما به دیگران)", 2: "دریافتی (از دیگران به ما)" };
export const guaranteeKinds: Record<number, string> = { 1: "حسن انجام کار", 2: "پیش‌پرداخت", 3: "شرکت در مناقصه", 9: "سایر" };
export const guaranteeStatuses: Record<number, string> = { 1: "فعال", 2: "آزادشده", 3: "منقضی", 4: "مطالبه‌شده" };
export const guaranteeEvents: Record<number, string> = { 1: "تمدید", 2: "آزادسازی", 3: "مطالبه", 4: "کاهش", 5: "انقضا" };
export const settlementStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده", 2: "باطل" };
export const supplierNoteKinds: Record<number, string> = { 1: "یادداشت بستانکار خرید", 2: "یادداشت بدهکار خرید" };
export const confirmationStatuses: Record<number, string> = { 1: "صادرشده، منتظر پاسخ", 2: "تاییدشده", 3: "مغایرت اعلام‌شده" };
export const matchKinds: Record<string, string> = { Matched: "مطابق", AmountDifference: "اختلاف مبلغ", Unmatched: "بدون تطبیق", Invoice: "فاکتور", Payment: "پرداخت", Note: "یادداشت", Cheque: "چک" };
export const cashSections: Record<string, string> = { Operating: "عملیاتی", Investing: "سرمایه‌گذاری", Financing: "تامین مالی" };
