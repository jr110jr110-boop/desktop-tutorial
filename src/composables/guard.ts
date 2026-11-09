// Names and roles of the guarded machining process (production release P2 part E). Enum values match Entities/Model/NasbNiro/Production (MonitorEntities.cs, GuardEntities.cs) exactly;
// the role lists mirror GuardRoles in Services/NasbNiro/Production/Dnc/MfGuardService.cs (the service decides; the page only hides what would be refused).
import { DncRoles } from "@/composables/dnc";

export const GuardRoles = {
  readers: DncRoles.readers,
  editors: DncRoles.linkEditors,
  testers: DncRoles.linkTesters,
  /** only the production manager opens a gate once, with a written reason */
  overriders: ["PRODUCTIONMANAGER"],
  supervisors: DncRoles.supervisors,
  stationEditors: [...DncRoles.linkEditors, "QC"],
};

export const dataLinks: Record<number, string> = { 0: "بدون اتصال داده", 1: "LSV2 (هایدن‌هاین، شبکه)", 2: "گیت‌وی لبه (فانوک FOCAS و مانند آن)" };
export const execModes: Record<number, string> = { 0: "نامعلوم", 1: "دستی", 2: "MDI", 3: "تک‌بلوک", 4: "خودکار", 5: "دیگر" };
export const programStates: Record<number, string> = { 0: "نامعلوم", 1: "در حال اجرا", 2: "متوقف", 3: "پایان‌یافته", 4: "قطع‌شده", 5: "خطا", 6: "آماده" };
export const gateTitles: Record<string, string> = {
  program: "هویت برنامه روی کنترلر", tools: "جدول ابزار ماشین", "prove-out": "اثبات اجرای اول", interlock: "ارتباط داده با کنترلر", measurement: "اندازه‌گیری با دستگاه",
};
export const postStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "منتظر تأیید", 2: "تأییدشده (جاری)", 3: "بازنشسته", 4: "ردشده" };
export const postActionTitles: Record<string, string> = { submit: "ارسال برای تأیید", withdraw: "حذف پیش‌نویس", approve: "تأیید", reject: "رد", retire: "بازنشسته کردن" };
export const attachmentKinds: Record<number, string> = { 1: "گزارش شبیه‌سازی", 2: "برگه راه‌اندازی" };

/** The colour of a gate or a time line line: ok (green), warn (amber), bad (red), info (blue), muted. */
export const gateTone = (state: string) => (state === "ok" ? "ok" : state === "blocked" ? "bad" : state === "pending" ? "warn" : "muted");
export const gateIcon = (state: string) => (state === "ok" ? "✓" : state === "blocked" ? "✕" : state === "pending" ? "!" : "–");
