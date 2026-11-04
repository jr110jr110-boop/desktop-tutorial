// Labels and small helpers of task management and the project registers (Persian texts; numbers are the API enums).
export const taskKinds: Record<number, string> = { 1: "اداری", 2: "پروژه‌ای", 3: "کارگاهی", 4: "شخصی", 5: "مصوبه جلسه" };
export const taskStatuses: Record<number, string> = { 0: "باز", 1: "در حال انجام", 2: "در انتظار تایید", 3: "ارجاع‌شده", 4: "بسته", 5: "رد شده", 6: "معلق" };
export const taskStatusClass: Record<number, string> = { 0: "", 1: "info", 2: "warn", 3: "info", 4: "ok", 5: "bad", 6: "warn" };
export const priorities: Record<number, string> = { 1: "کم", 2: "عادی", 3: "بالا", 4: "بحرانی" };
export const importances: Record<number, string> = { 1: "کم", 2: "متوسط", 3: "زیاد" };
export const classifications: Record<number, string> = { 0: "عادی", 1: "محرمانه", 2: "سری" };
export const assigneeRoles: Record<number, string> = { 1: "مسئول", 2: "مشارکت‌کننده", 3: "ناظر" };
export const assigneeStates: Record<number, string> = { 0: "فعال", 1: "انجام شد", 2: "رد شد", 3: "منتقل شد", 4: "پس گرفته شد" };
export const recurrences: Record<number, string> = { 0: "بدون تکرار", 1: "روزانه", 2: "هفتگی", 3: "ماهانه", 4: "سالانه" };
export const historyKinds: Record<number, string> = { 1: "ایجاد", 2: "ویرایش", 3: "وضعیت", 4: "ارجاع", 5: "انتقال", 6: "یادداشت", 7: "چک‌لیست", 8: "پیوست", 9: "پیوند", 10: "پیشرفت", 11: "یادآوری", 12: "ناظر" };
export const cartables: [string, string][] = [
  ["mine", "کارتابل من"], ["assigned-to-me", "ارجاع‌شده به من"], ["assigned-by-me", "ارجاع‌داده من"], ["overdue", "معوق"],
  ["waiting", "در انتظار تایید من"], ["unit", "کارتابل واحد / سمت"], ["watching", "تحت نظر"], ["done", "انجام‌شده"], ["all", "همه"],
];
export const actionTexts: Record<string, string> = { start: "شروع", complete: "اعلام انجام", approve: "تایید و بستن", return: "بازگشت برای اصلاح", reject: "رد", suspend: "تعلیق", resume: "ادامه", reopen: "بازگشایی" };
/** index = DayOfWeek of the API (0 = Sunday) */
export const weekDays = ["یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"];

// progress approval
export const submissionStates: Record<number, string> = { 0: "پیش‌نویس", 1: "در جریان تایید", 2: "تایید شد", 3: "بازگشت‌خورده" };
export const submissionStateClass: Record<number, string> = { 0: "", 1: "warn", 2: "ok", 3: "bad" };
export const stepStates: Record<number, string> = { 0: "در انتظار", 1: "تایید", 2: "بازگشت" };
export const approvalStates: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده", 2: "تایید شد", 3: "رد شد" };
export const approvalStateClass: Record<number, string> = { 0: "", 1: "warn", 2: "ok", 3: "bad" };
export const chainRoles: [string, string][] = [
  ["PROJECTMANAGER", "مدیر پروژه"], ["PLANNING", "برنامه‌ریزی"], ["PMO", "دفتر مدیریت پروژه"], ["CEO", "مدیرعامل"], ["ENGINEERING", "مهندسی"], ["QC", "کنترل کیفیت"], ["COMMERCIAL", "بازرگانی"],
];

// registers
export const riskResponses: Record<number, string> = { 1: "اجتناب", 2: "کاهش", 3: "انتقال", 4: "پذیرش", 5: "ارجاع به بالاتر", 6: "بهره‌برداری", 7: "تقویت", 8: "اشتراک" };
export const riskStatuses: Record<number, string> = { 0: "شناسایی‌شده", 1: "ارزیابی‌شده", 2: "در حال پاسخ", 3: "رخ داده", 4: "بسته" };
export const riskLevels: Record<string, string> = { low: "کم", medium: "متوسط", high: "زیاد", critical: "بحرانی" };
export const issueStatuses: Record<number, string> = { 0: "باز", 1: "در حال رسیدگی", 2: "حل‌شده", 3: "بسته" };
export const engagements: Record<number, string> = { 1: "بی‌اطلاع", 2: "مقاوم", 3: "خنثی", 4: "حامی", 5: "پیشرو" };
export const deliverableStatuses: Record<number, string> = { 0: "برنامه‌ریزی‌شده", 1: "در حال انجام", 2: "تحویل‌شده", 3: "پذیرفته‌شده", 4: "ردشده" };
export const evaluationParties: Record<number, string> = { 1: "پیمانکار", 2: "مشاور" };
export const phases: Record<number, string> = { 1: "مهندسی", 2: "تدارکات", 3: "ساخت", 4: "آزمون", 5: "تحویل", 6: "اختتام" };
export const phaseDecisions: Record<number, string> = { 1: "ادامه", 2: "ادامه با شرط", 3: "توقف" };
export const weeklyStates: Record<number, string> = { 0: "پیش‌نویس", 1: "منتشر شد" };
export const manpowerKinds: Record<number, string> = { 1: "نیروی داخلی", 2: "پیمانکار" };
export const machineOwnerships: Record<number, string> = { 1: "شرکتی", 2: "اجاره‌ای" };
export const hseKinds: Record<number, string> = { 1: "شبه‌حادثه", 2: "کمک‌های اولیه", 3: "حادثه با ازکارافتادگی", 4: "خسارت مالی", 5: "زیست‌محیطی", 6: "مشاهده ایمنی" };

/** "high" for a score from 1 to 25 against the default limits (the API sends the real level text). */
export const riskClass = (level: string) => (level === "critical" ? "bad" : level === "high" ? "warn" : level === "medium" ? "info" : "ok");
