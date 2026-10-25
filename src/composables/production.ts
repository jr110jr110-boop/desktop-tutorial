import { jalali } from "@/composables/inventory";

// Names and helpers of the production / shop floor (MES) and maintenance module (release P1). Enum values are numbers in the JSON and match
// Entities/Model/NasbNiro/Production/ProductionEnums.cs (and QcResult of QualityEnums.cs) exactly. Everything a person reads is Persian (Jalali); the API JSON stays ISO.

export const workCenterKinds: Record<number, string> = { 1: "ماشین‌کاری", 2: "ایستگاه دستی", 3: "جوشکاری", 4: "سالن مونتاژ", 5: "میز آزمون", 6: "رنگ‌کاری", 7: "بیرونی (پیمانکار)" };
export const machineStatuses: Record<number, string> = { 1: "آماده به کار", 2: "خراب", 3: "در تعمیر و نگهداری", 4: "از رده خارج" };
/** What an operation needs before it counts as done. */
export const inspectionRequirements: Record<number, string> = { 0: "بدون بازرسی", 1: "بازرسی خود اپراتور", 2: "بازرسی کنترل کیفیت", 3: "نقطه ITP" };
export const routingStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تاییدشده" };
export const orderStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "صادرشده", 2: "در حال اجرا", 3: "تکمیل‌شده", 4: "بسته‌شده", 5: "لغوشده" };
export const operationStatuses: Record<number, string> = { 0: "در انتظار", 1: "آماده", 2: "در حال انجام", 3: "متوقف", 4: "انجام‌شده", 5: "در انتظار بازرسی", 6: "مردود در بازرسی", 7: "رد مرحله (اجرا نشده)" };
export const bookingKinds: Record<number, string> = { 1: "راه‌اندازی", 2: "تولید", 3: "دوباره‌کاری", 4: "انتظار", 5: "نظافت" };
export const bookingSources: Record<number, string> = { 1: "ترمینال", 2: "سرپرست" };
export const reasonKinds: Record<number, string> = { 1: "ضایعات", 2: "توقف", 3: "انتظار" };
export const maintenanceRequestStatuses: Record<number, string> = { 0: "باز", 1: "در حال انجام", 2: "انجام‌شده", 3: "لغوشده" };
export const maintenanceKinds: Record<number, string> = { 1: "برنامه‌ریزی‌شده", 2: "خرابی" };
export const maintenanceOrderStatuses: Record<number, string> = { 0: "باز", 1: "در حال انجام", 2: "انجام‌شده", 3: "لغوشده" };
export const maintenanceIntervalKinds: Record<number, string> = { 1: "روز", 2: "ساعت کارکرد" };
/** QcResult of the quality module, used by POST operations/qc-decision. */
export const qcResults: Record<number, string> = { 1: "قبول", 2: "رد", 3: "دوباره‌کاری", 4: "نگه‌داشتن" };

/** DispatchRowDto.stage (text in the JSON). */
export const dispatchStages: Record<string, string> = { Ready: "آماده شروع", Running: "در حال انجام", Next: "نوبت بعدی" };
/** AttendanceDifferenceDto.kind (text in the JSON). */
export const differenceKinds: Record<string, string> = { Unbooked: "حاضر بدون ثبت زمان", OverBooked: "ثبت زمان بیش از حضور", NoAttendance: "ثبت زمان بدون حضور" };
/** ReleaseCheckDto.code (text in the JSON). */
export const releaseCheckCodes: Record<string, string> = { routing: "مسیر ساخت", workcenters: "مراکز کار", drawings: "نقشه‌ها", itp: "طرح بازرسی (ITP)", materials: "مواد و قطعات" };
/** DashboardMachineDto.status (text in the JSON; the dashboard lists only machines that are down). */
export const machineStatusKeys: Record<string, string> = { Available: "آماده به کار", Down: "خراب", UnderMaintenance: "در تعمیر و نگهداری", Retired: "از رده خارج" };

/** Persian title of an action string (orders, operations, routings, maintenance orders). */
export const productionActionTitles: Record<string, string> = {
  cancel: "لغو", close: "بستن", "kit-override": "شروع بدون کیت کامل", "refresh-readiness": "بررسی دوباره آمادگی مراحل", "refresh-routing": "به‌روزرسانی مسیر ساخت",
  skip: "رد کردن مرحله", "complete-outside": "پایان کار بیرونی", "approve-production": "تایید تولید", "approve-engineering": "تایید مهندسی", delete: "حذف",
  start: "شروع", complete: "تکمیل", "request-parts": "درخواست قطعات یدکی", "draft-issue": "پیش‌نویس حواله قطعات",
};

/** Hours (decimal, as the API sends them) as "h:mm" (1.5 -> 1:30); zero and empty show a dash. */
export function hoursText(hours: number | null | undefined): string {
  const total = Math.round((Number(hours) || 0) * 60);
  if (!total) return "-";
  const sign = total < 0 ? "-" : "";
  const m = Math.abs(total);
  return `${sign}${Math.floor(m / 60)}:${String(m % 60).padStart(2, "0")}`;
}

/** Minutes as "h:mm" (the booking `minutes` field); zero and empty show a dash. */
export function minutesHoursText(minutes: number | null | undefined): string {
  return hoursText((Number(minutes) || 0) / 60);
}

/** Percent with at most one decimal and the % sign (82.5 -> "82.5%"); null/undefined (no data) shows a dash. */
export function percentText(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return "-";
  const n = Number(value);
  const text = n.toFixed(digits).replace(/\.0+$/, "").replace(/(\.\d*?)0+$/, "$1");
  return text + "%";
}

/** Quantity without trailing zeros (12.000 -> "12", 2.50 -> "2.5"); empty shows a dash. */
export function qtyText(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return "-";
  return String(Math.round(Number(value) * 1000) / 1000);
}

/** Persian date (1405/08/03) of an ISO date or date-time; empty when there is none. Never shows a Gregorian date. */
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

/** Time "HH:mm" of an ISO date-time. */
export function timeOf(value: string | null | undefined): string {
  if (!value) return "";
  const match = /T(\d{2}:\d{2})/.exec(String(value));
  return match ? match[1] : "";
}

/** Badge classes ("ok", "warn", "bad", "info") as in the office module. */
export function badgeOfOrder(status: number): string {
  return status === 5 ? "bad" : status >= 3 ? "ok" : status >= 1 ? "info" : "warn";
}
export function badgeOfOperation(status: number): string {
  return status === 6 ? "bad" : status === 4 ? "ok" : status === 5 || status === 3 ? "warn" : status === 2 || status === 1 ? "info" : "";
}
export function badgeOfMachine(status: number): string {
  return status === 2 ? "bad" : status === 3 ? "warn" : status === 4 ? "" : "ok";
}
export function badgeOfRouting(status: number): string {
  return status === 1 ? "ok" : "warn";
}
export function badgeOfMaintenance(status: number): string {
  return status === 3 ? "bad" : status === 2 ? "ok" : status === 1 ? "info" : "warn";
}
/** OEE and load percent: colour by level (load above 100 is overloaded). */
export function badgeOfLoad(percent: number | null | undefined, threshold = 100): string {
  if (percent === null || percent === undefined) return "bad";
  return percent > threshold ? "bad" : percent > threshold * 0.85 ? "warn" : "ok";
}
