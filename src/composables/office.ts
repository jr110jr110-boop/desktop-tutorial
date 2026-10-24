import { jalali } from "@/composables/inventory";

// Names and helpers of the office automation module (release O1). Enum values are numbers in the JSON and match
// Entities/Model/NasbNiro/Office/OfficeEnums.cs exactly. Everything a person reads is Persian (Jalali); the API JSON stays ISO.

export const inboxKinds: Record<number, string> = { 1: "نامه", 2: "ارجاع", 3: "تایید", 4: "کار", 5: "هشدار", 6: "اقدام جلسه" };
/** byKind of GET inbox/counts is keyed by the English enum name. */
export const inboxKindKeys: Record<string, number> = { Letter: 1, Referral: 2, Approval: 3, Task: 4, Alert: 5, MeetingAction: 6 };
export const inboxStatuses: Record<number, string> = { 0: "خوانده‌نشده", 1: "خوانده‌شده", 2: "انجام‌شده", 3: "لغوشده" };
/** Urgency of a letter and priority of an inbox item. */
export const urgencies: Record<number, string> = { 0: "عادی", 1: "فوری", 2: "آنی" };

export const letterKinds: Record<number, string> = { 1: "وارده", 2: "صادره", 3: "داخلی" };
export const letterStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در جریان تایید", 2: "امضاشده", 3: "ثبت‌شده", 4: "ارسال‌شده", 5: "دریافت‌شده", 6: "بایگانی‌شده", 7: "باطل‌شده" };
export const classifications: Record<number, string> = { 0: "عادی", 1: "محرمانه", 2: "سری" };
export const recipientTypes: Record<number, string> = { 1: "گیرنده", 2: "رونوشت" };
export const deliveryMethods: Record<number, string> = { 1: "دستی", 2: "پست", 3: "پیک", 4: "ایمیل", 5: "فکس" };
export const partyKinds: Record<number, string> = { 1: "مشتری", 2: "تامین‌کننده", 3: "سازمان" };
export const relatedKinds: Record<number, string> = { 1: "قرارداد", 2: "پروژه", 3: "سفارش خرید", 4: "مناقصه", 5: "استعلام", 6: "درخواست خدمت", 7: "دستور ساخت", 99: "سایر" };
/** Step of an approval chain (letters and requests). */
export const approvalStatuses: Record<number, string> = { 0: "در انتظار", 1: "تاییدشده", 2: "برگشت‌خورده" };
export const referralActions: Record<number, string> = { 1: "جهت اقدام", 2: "جهت اطلاع", 3: "جهت اظهارنظر" };
export const referralStatuses: Record<number, string> = { 0: "در انتظار", 1: "انجام‌شده", 2: "لغوشده" };
export const delegationStatuses: Record<number, string> = { 0: "تاییدشده", 1: "در انتظار تایید", 2: "ردشده" };

export const meetingKinds: Record<number, string> = { 1: "مدیریتی", 2: "پروژه", 3: "فنی", 4: "کیفیت", 5: "مشتری", 6: "سایر" };
export const meetingStatuses: Record<number, string> = { 0: "برنامه‌ریزی‌شده", 1: "برگزارشده", 2: "صورتجلسه پیش‌نویس", 3: "صورتجلسه تاییدشده", 4: "لغوشده" };
export const decisionStatuses: Record<number, string> = { 0: "باز", 1: "انجام‌شده", 2: "لغوشده" };

export const clockDirections: Record<number, string> = { 0: "نامشخص", 1: "ورود", 2: "خروج" };
export const clockSources: Record<number, string> = { 1: "دستگاه", 2: "وارد کردن فایل", 3: "دستی" };
export const dayStatuses: Record<number, string> = { 1: "حاضر", 2: "غایب", 3: "مرخصی", 4: "ماموریت", 5: "تعطیل", 6: "ناقص (تک‌تردد)" };
/** Weekday numbers of shift.workDays: 0 = Sunday .. 6 = Saturday (Iran works 6, 0, 1, 2, 3). */
export const weekDays: Record<number, string> = { 0: "یکشنبه", 1: "دوشنبه", 2: "سه‌شنبه", 3: "چهارشنبه", 4: "پنجشنبه", 5: "جمعه", 6: "شنبه" };
/** Saturday first, the order of the Iranian week. */
export const weekOrder = [6, 0, 1, 2, 3, 4, 5];

export const leaveKinds: Record<number, string> = { 1: "مرخصی روزانه", 2: "مرخصی ساعتی", 3: "مرخصی استعلاجی", 4: "مرخصی بدون حقوق", 5: "ماموریت", 6: "اضافه‌کار", 7: "فراموشی تردد" };
export const leaveStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده", 2: "تاییدشده", 3: "ثبت‌شده در حضور و غیاب", 4: "ردشده", 5: "لغوشده" };
export const requestStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در جریان تایید", 2: "تاییدشده", 3: "ردشده", 4: "لغوشده", 5: "انجام‌شده" };
/** Field types of a request type form. */
export const requestFieldTypes: Record<string, string> = { text: "متن", number: "عدد", date: "تاریخ", select: "انتخابی" };

/** Persian label of an action string of allowedActions (letters, meetings, leave, requests, inbox, referrals, decisions). */
export const actionTitles: Record<string, string> = {
  save: "ذخیره", submit: "ارسال برای تایید", approve: "تایید", return: "برگشت", sign: "امضا", register: "ثبت و شماره‌گذاری", send: "ارسال", receipt: "ثبت رسید",
  archive: "بایگانی", refer: "ارجاع", cancel: "ابطال", reject: "رد", record: "ثبت در حضور و غیاب", complete: "انجام شد", hold: "برگزاری جلسه",
  "submit-minutes": "ارسال صورتجلسه", "approve-minutes": "تایید صورتجلسه", "sign-minutes": "امضای صورتجلسه", done: "انجام شد", read: "خوانده شد", unread: "خوانده‌نشده", comment: "اظهارنظر", close: "بستن",
};

/** True when the record (LetterDto, MeetingDto, LeaveDto, RequestDto) lists the action in allowedActions. The API checks again on every call. */
export function can(record: { allowedActions?: string[] } | null | undefined, action: string): boolean {
  return !!record && Array.isArray(record.allowedActions) && record.allowedActions.includes(action);
}

/** Persian date (1405/08/03) of an ISO date or date-time; empty when there is none. */
export function dateText(value: string | null | undefined): string {
  return jalali(value, false);
}

/** Persian date and time (1405/08/03 14:05) of an ISO date-time. */
export function dateTimeText(value: string | null | undefined): string {
  return jalali(value, true);
}

/** The "...Text" field of the API when it is there (already Persian), otherwise the Persian form of the ISO value. */
export function persianOf(text: string | null | undefined, iso?: string | null, withTime = false): string {
  if (text) return text;
  return jalali(iso, withTime);
}

/** Minutes as "h:mm" (for example 95 -> 1:35); zero and empty show a dash. */
export function minutesText(minutes: number | null | undefined): string {
  const m = Math.round(Number(minutes) || 0);
  if (!m) return "-";
  return `${Math.floor(m / 60)}:${String(m % 60).padStart(2, "0")}`;
}

/** Time "HH:mm" of an ISO date-time. */
export function timeOf(value: string | null | undefined): string {
  if (!value) return "";
  const match = /T(\d{2}:\d{2})/.exec(String(value));
  return match ? match[1] : "";
}

export function badgeOfLetter(status: number): string {
  return status === 7 ? "bad" : status >= 3 ? "ok" : status === 2 ? "info" : "warn";
}
export function badgeOfLeave(status: number): string {
  return status === 4 || status === 5 ? "bad" : status >= 2 ? "ok" : status === 1 ? "info" : "warn";
}
export function badgeOfRequest(status: number): string {
  return status === 3 || status === 4 ? "bad" : status >= 2 ? "ok" : status === 1 ? "info" : "warn";
}
export function badgeOfMeeting(status: number): string {
  return status === 4 ? "bad" : status === 3 ? "ok" : status >= 1 ? "info" : "warn";
}
export function badgeOfDecision(status: number): string {
  return status === 2 ? "bad" : status === 1 ? "ok" : "warn";
}
