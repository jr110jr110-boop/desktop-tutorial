// Names for the procurement pages (enums of the API).
export const requestSources: Record<number, string> = { 1: "BOM محصول", 2: "نقطه سفارش", 3: "درخواست مستقیم", 4: "کسری مواد" };
export const purchaseKinds: Record<number, string> = { 1: "کالا", 2: "خدمات", 3: "پیمانکاری" };
export const requestStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال شد", 2: "تایید شد", 3: "سفارش داده شد", 4: "بسته شد", 9: "رد شد" };
export const inquiryStatuses: Record<number, string> = { 0: "باز", 1: "تامین‌کننده انتخاب شد", 2: "تایید شد", 9: "ابطال" };
export const orderStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تایید شد", 2: "ابلاغ شد", 3: "بخشی دریافت شد", 4: "دریافت کامل", 5: "بسته شد", 9: "ابطال" };
export const invoiceStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "مطابق (تطبیق سه‌طرفه)", 2: "نیازمند تایید", 3: "تایید شد", 9: "ابطال" };
export const paymentTypes: Record<number, string> = { 1: "پیش‌پرداخت", 2: "صورت‌وضعیت", 3: "فاکتور", 4: "تسویه نهایی" };
export const paymentStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تایید شد", 2: "پرداخت شد", 9: "رد شد" };
export const supplierGrades: Record<number, string> = { 0: "ارزیابی نشده", 1: "A", 2: "B", 3: "C", 4: "D" };
export const evaluationKinds: Record<number, string> = { 1: "اولیه", 2: "ثانویه", 3: "دوره‌ای", 4: "پایان قرارداد" };

/** Grade D and blocked suppliers are on the unapproved list (FO-PO-009); the others are on the approved list (FO-PO-008). */
export const isUnapproved = (s: any) => s.blocked || s.grade === 4;

/**
 * Questions of the evaluation forms (FO-PO-003..006), weight W. The API only stores the questions it is given, so the
 * company can change this list here; the final score is sum(S x W) / sum(W) and a question marked N/A is left out.
 */
export const evaluationQuestions: Record<number, { question: string; weight: number }[]> = {
  1: [
    { question: "سابقه و تجربه کاری در زمینه کالا / خدمت", weight: 3 },
    { question: "توان مالی و اعتبار", weight: 2 },
    { question: "گواهینامه‌ها و استانداردهای کیفیت (ISO و ...)", weight: 3 },
    { question: "نمونه کار یا بازدید از کارگاه", weight: 3 },
    { question: "قیمت نسبت به بازار", weight: 3 },
  ],
  2: [
    { question: "کیفیت کالا / خدمت تحویل‌شده", weight: 4 },
    { question: "رعایت زمان تحویل", weight: 3 },
    { question: "قیمت و شرایط پرداخت", weight: 2 },
    { question: "پاسخگویی و پشتیبانی", weight: 2 },
  ],
  3: [
    { question: "کیفیت (مرجوعی و گزارش عدم انطباق دوره)", weight: 4 },
    { question: "رعایت زمان تحویل", weight: 4 },
    { question: "قیمت و شرایط پرداخت", weight: 2 },
    { question: "پاسخگویی و پشتیبانی", weight: 2 },
    { question: "رعایت الزامات ایمنی و مستندات (گواهی مواد، برگه بازرسی)", weight: 2 },
  ],
  4: [
    { question: "کیفیت نهایی کار", weight: 4 },
    { question: "رعایت زمان‌بندی قرارداد", weight: 3 },
    { question: "رعایت شرایط مالی قرارداد", weight: 2 },
    { question: "پشتیبانی پس از تحویل", weight: 2 },
  ],
};

/** Final score as the API computes it: sum(S x W) / sum(W) over the answered questions. */
export function evaluationScore(lines: { weight: number; score: number | null }[]): number | null {
  const answered = lines.filter((l) => l.score !== null && l.score !== undefined && (l.score as any) !== "");
  const w = answered.reduce((s, l) => s + (Number(l.weight) || 0), 0);
  if (!w) return null;
  return Math.round((answered.reduce((s, l) => s + Number(l.score) * (Number(l.weight) || 0), 0) / w) * 10) / 10;
}

export function gradeOfScore(score: number | null): number {
  if (score === null) return 0;
  return score >= 85 ? 1 : score >= 65 ? 2 : score >= 45 ? 3 : 4;
}
