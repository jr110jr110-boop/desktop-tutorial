import moment from "moment-jalaali";
import { ref, type Ref } from "vue";
import { fail } from "@/composables/inventory";

// Names and helpers of the accounting module (releases F1 and F2). Enum values are numbers in the JSON (FinanceDtos, Entities/Finance).

export const yearStatuses: Record<number, string> = { 0: "باز", 1: "در حال بستن", 2: "بسته" };
export const periodStatuses: Record<number, string> = { 0: "باز", 1: "قفل", 2: "بسته" };
export const accountLevels: Record<number, string> = { 1: "گروه", 2: "کل", 3: "معین" };
export const accountNatures: Record<number, string> = { 1: "بدهکار", 2: "بستانکار", 3: "هر دو" };
export const accountTypes: Record<number, string> = { 1: "دارایی", 2: "بدهی", 3: "حقوق صاحبان سرمایه", 4: "درآمد", 5: "هزینه", 6: "انتظامی" };
export const detailTypes: Record<number, string> = {
  1: "شخص", 2: "تامین‌کننده", 3: "مشتری", 4: "کارمند", 5: "بانک", 6: "صندوق", 7: "تنخواه", 8: "پروژه", 9: "دستور ساخت", 10: "مرکز هزینه", 11: "دارایی", 99: "سایر",
};
export const voucherTypes: Record<number, string> = { 1: "افتتاحیه", 2: "عادی", 3: "خودکار", 4: "اختتامیه", 5: "برگشتی", 6: "بستن حساب‌های موقت" };
export const voucherStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "کنترل‌شده", 2: "قطعی" };
export const taxKinds: Record<number, string> = { 1: "ارزش افزوده", 2: "مالیات علی‌الحساب پیمان", 3: "کسر بیمه پیمان", 4: "سپرده حسن انجام کار", 9: "سایر" };
export const docStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده", 2: "ثبت‌شده", 3: "باطل" };
export const instruments: Record<number, string> = { 1: "نقد", 2: "حواله بانکی", 3: "چک", 4: "کارت", 5: "تهاتر" };
export const paymentPurposes: Record<number, string> = { 1: "پرداخت به تامین‌کننده", 2: "شارژ تنخواه", 3: "پرداخت حقوق", 4: "پرداخت وام یا مساعده کارمند", 5: "پرداخت عیدی", 6: "پرداخت تسویه پایان خدمت" };
export const receivedChequeStatuses: Record<number, string> = { 1: "در صندوق", 2: "واگذار به بانک", 3: "وصول‌شده", 4: "برگشت‌خورده", 5: "عودت به صادرکننده", 6: "خرج‌شده" };
export const issuedChequeStatuses: Record<number, string> = { 0: "برگ سفید", 1: "صادرشده", 2: "پاس‌شده", 3: "ابطال‌شده", 4: "خراب" };
export const guaranteeStatuses: Record<number, string> = { 1: "نزد ما", 2: "مسترد", 3: "وصول‌شده" };
export const guaranteeKinds: Record<number, string> = { 1: "چک", 2: "سفته" };
export const guaranteeDirections: Record<number, string> = { 1: "دریافتی", 2: "صادره" };
export const statementMatches: Record<number, string> = { 0: "تطبیق‌نخورده", 1: "چک", 2: "پرداخت", 3: "دریافت", 4: "سند", 5: "دستی" };
export const reconciliationStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده" };
export const invoiceTypes: Record<number, string> = { 1: "پیش‌دریافت", 2: "صورت‌وضعیت", 3: "صورت‌حساب نهایی", 4: "فروش کالا", 5: "ارائه خدمات", 6: "اعلامیه بستانکار", 7: "آزادسازی حسن انجام کار" };
/** Typed deductions of a supplier payment request (FnDeductionKind). */
export const deductionKinds: Record<number, string> = { 1: "مالیات علی‌الحساب", 2: "کسر بیمه", 3: "حسن انجام کار", 4: "جریمه تاخیر", 5: "بازیابی پیش‌پرداخت", 9: "سایر" };
export const depreciationMethods: Record<number, string> = { 1: "خط مستقیم", 2: "نزولی" };
export const depreciationStarts: Record<number, string> = { 1: "ماه بهره‌برداری", 2: "ماه بعد" };
export const assetStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در بهره‌برداری", 2: "بلااستفاده", 3: "در دست ساخت", 4: "خارج‌شده" };
export const assetSources: Record<number, string> = { 1: "ثبت دستی", 2: "ردیف فاکتور تامین‌کننده", 3: "رسید", 4: "افتتاحیه" };
export const assetHistoryKinds: Record<number, string> = {
  1: "تحصیل", 2: "بهره‌برداری", 3: "انتقال", 4: "شروع بلااستفاده", 5: "پایان بلااستفاده", 6: "بهسازی", 7: "تجدید ارزیابی", 8: "خروج", 9: "ویرایش",
};
export const runStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده" };
export const disposalKinds: Record<number, string> = { 1: "فروش", 2: "اسقاط" };
export const proceedsMethods: Record<number, string> = { 0: "بدون وجه", 1: "نقد", 2: "بانک", 3: "مطالبات" };
export const employmentTypes: Record<number, string> = { 1: "رسمی", 2: "قراردادی", 3: "ساعتی", 4: "روزمزد" };
export const genders: Record<number, string> = { 1: "مرد", 2: "زن" };
export const maritalStatuses: Record<number, string> = { 1: "مجرد", 2: "متاهل" };
export const loanKinds: Record<number, string> = { 1: "وام", 2: "مساعده" };
export const payrollItemKinds: Record<number, string> = { 1: "مزایا", 2: "کسور", 3: "سهم کارفرما", 4: "اطلاعاتی", 5: "ذخیره" };
export const payrollCalcs: Record<number, string> = {
  1: "مبلغ ثابت ماهانه", 2: "به ازای روز کارکرد", 3: "به ازای ساعت", 4: "درصد از اقلام", 5: "فرمول", 6: "ورودی (حضور و غیاب)", 7: "قسط وام / مساعده", 8: "مالیات حقوق",
};
export const payrollStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "محاسبه‌شده", 2: "تاییدشده", 3: "ثبت‌شده در دفاتر", 4: "پرداخت‌شده" };
export const outboxKinds: Record<number, string> = {
  1: "فاکتورهای فروش (سامانه مودیان)", 2: "اظهارنامه ارزش افزوده فصلی", 3: "لیست بیمه حقوق", 4: "لیست مالیات حقوق",
};
export const outboxStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "آماده", 2: "خروجی گرفته شد", 3: "ارسال‌شده", 4: "پذیرفته‌شده", 5: "ردشده", 6: "جایگزین‌شده" };
export const monthNames = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
export const seasonNames: Record<number, string> = { 1: "بهار", 2: "تابستان", 3: "پاییز", 4: "زمستان" };

/** Report layout titles (the statement lines of the API carry their own titles). */
export const NO_FORM_CODE = "کد فرم: (توسط تضمین کیفیت تعیین شود)";
export const distributionFinance = "توزیع نسخ: ۱- مالی ۲- مدیریت";
export const distributionTreasury = "توزیع نسخ: ۱- خزانه‌داری ۲- مالی";
export const distributionPayroll = "توزیع نسخ: ۱- حقوق و دستمزد ۲- مالی (محرمانه)";

export function badgeOfDoc(status: number): string {
  return status === 2 ? "ok" : status === 3 ? "bad" : status === 1 ? "info" : "warn";
}
export function badgeOfVoucher(status: number): string {
  return status === 2 ? "ok" : status === 1 ? "info" : "warn";
}

/** Date value (yyyy-mm-dd) of an API date. */
export function iso(value: string | null | undefined): string | null {
  return value ? String(value).slice(0, 10) : null;
}

const persianDigits = "۰۱۲۳۴۵۶۷۸۹";
const arabicDigits = "٠١٢٣٤٥٦٧٨٩";
/** Persian and Arabic digits to 0-9; the thousands separators are removed. */
export function latinDigits(text: string): string {
  return String(text ?? "")
    .replace(/[۰-۹]/g, (d) => String(persianDigits.indexOf(d)))
    .replace(/[٠-٩]/g, (d) => String(arabicDigits.indexOf(d)))
    .replace(/[٬,،]/g, "")
    .replace(/٫/g, ".");
}

/** An amount typed by a user (digits of any script, separators allowed) as a number; empty text gives null. */
export function parseAmount(text: any): number | null {
  if (text === null || text === undefined || text === "") return null;
  if (typeof text === "number") return text;
  const clean = latinDigits(String(text)).trim();
  if (clean === "") return null;
  const n = Number(clean);
  return Number.isFinite(n) ? n : null;
}

/** Jalali text "1404/07/15" (any digits) to yyyy-mm-dd; null when it is not a real date. */
export function jalaliToIso(text: string): string | null {
  const m = /^(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})$/.exec(latinDigits(text).trim());
  if (!m) return null;
  const y = Number(m[1]), mo = Number(m[2]), d = Number(m[3]);
  if (mo < 1 || mo > 12 || d < 1 || d > 31) return null;
  if (!(moment as any).jIsValid(y, mo, d)) return null;
  return moment(`${y}/${mo}/${d}`, "jYYYY/jM/jD").format("YYYY-MM-DD");
}

/** Current Persian year / month. */
export function jalaliNow(): { year: number; month: number; day: number } {
  const m = moment();
  return { year: m.jYear(), month: m.jMonth() + 1, day: m.jDate() };
}

/** First and last day (yyyy-mm-dd) of a Persian month. */
export function monthRange(year: number, month: number): { from: string; to: string } {
  const start = moment(`${year}/${month}/1`, "jYYYY/jM/jD");
  const days = (moment as any).jDaysInMonth(year, month - 1);
  return { from: start.format("YYYY-MM-DD"), to: moment(`${year}/${month}/${days}`, "jYYYY/jM/jD").format("YYYY-MM-DD") };
}

/** Persian year of an ISO date. */
export function jalaliYearOf(value: string): number {
  return moment(value).jYear();
}

export function jalaliTitle(year: number, month: number): string {
  return `${monthNames[month - 1] || month} ${year}`;
}

/** Amount for a table: whole rial with thousands separators; negative numbers in parentheses are not used, the minus sign is kept. */
export function money(value: number | null | undefined): string {
  if (value === null || value === undefined || value === ("" as any)) return "";
  const n = Math.round(Number(value));
  if (!Number.isFinite(n)) return "";
  return n.toLocaleString("en-US");
}

/** Debit / credit cell: zero shows a dash. */
export function moneyOrDash(value: number | null | undefined): string {
  return value ? money(value) : "-";
}

/** API objects may come back with PascalCase keys (some report endpoints); the first letter of every key is lowered. Input dictionaries are kept as they are. */
export function camelize<T = any>(value: any): T {
  if (Array.isArray(value)) return value.map((v) => camelize(v)) as any;
  if (value && typeof value === "object" && !(value instanceof Date)) {
    const out: any = {};
    Object.keys(value).forEach((k) => {
      const key = k.charAt(0).toLowerCase() + k.slice(1);
      out[key] = key === "inputs" ? value[k] : camelize(value[k]);
    });
    return out;
  }
  return value;
}

/** Paged list helper (the API answers { data, totalItems }); like usePaged of the quality pages, with a page size option and a fixed extra query. */
export function usePagedList(fetcher: (params: any) => Promise<any>, filters: Ref<any>, pageSize = 50) {
  const rows = ref<any[]>([]);
  const total = ref(0);
  const page = ref(1);
  const loading = ref(false);
  async function load() {
    loading.value = true;
    const [data, error] = await fetcher({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
    loading.value = false;
    if (error) return fail(error);
    const d = camelize(data);
    rows.value = Array.isArray(d) ? d : d?.data || [];
    total.value = Array.isArray(d) ? d.length : d?.totalItems ?? rows.value.length;
  }
  function search() {
    page.value = 1;
    return load();
  }
  function go(delta: number) {
    page.value += delta;
    return load();
  }
  return { rows, total, page, loading, load, search, go, pageSize };
}

/**
 * Prints the .print-form of the page: while the browser prints, the body gets a class that hides the rest of the page
 * (same mechanism as the quality forms, see inventory.scss).
 */
export function printSheet() {
  document.body.classList.add("q-form-print");
  const clear = () => document.body.classList.remove("q-form-print");
  window.addEventListener("afterprint", clear, { once: true });
  try {
    window.print();
  } finally {
    setTimeout(clear, 1500);
  }
}

/** A line the user can see but never has to type: Persian text for a boolean. */
export const yesNo = (v: any) => (v ? "بله" : "خیر");

/** Sum of a number property over rows. */
export function sumOf(rows: any[], key: string): number {
  return (rows || []).reduce((s, r) => s + (Number(r?.[key]) || 0), 0);
}

/** Errors thrown while reading a response must never expose salaries; only counts are logged. */
export function quietError(error: any): string {
  return error?.message || "خطا";
}
