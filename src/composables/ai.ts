import type { Router } from "vue-router";

// Names and helpers of the Local AI module (release A1). Enum values are numbers in the JSON and match Entities/Model/NasbNiro/Ai/AiEntities.cs exactly.
// Everything a person reads is Persian (Jalali); the API JSON stays ISO. The AI never approves anything: nothing in this module offers an approve action on behalf of the AI.

export { dateText, dateTimeText, persianOf } from "@/composables/production";
export { jalali } from "@/composables/inventory";

/** AiEndpointKind. */
export const endpointKinds: Record<number, string> = { 1: "گفت‌وگو", 2: "بردارسازی", 3: "گفتار به متن", 4: "متن به گفتار" };
/** AiNoteStatus. */
export const noteStatuses: Record<number, string> = { 1: "جدید", 2: "پذیرفته", 3: "ردشده" };
/** AiSeatRunStatus. */
export const runStatuses: Record<number, string> = { 1: "در حال اجرا", 2: "موفق", 3: "ناموفق", 4: "ردشده (مدل در دسترس نبود)" };

export function badgeOfNote(status: number): string {
  return status === 2 ? "ok" : status === 3 ? "bad" : "info";
}
export function badgeOfRun(status: number): string {
  return status === 2 ? "ok" : status === 3 ? "bad" : status === 4 ? "warn" : "info";
}

/** AiNoteTypes (target types of a note). */
export const noteTypes: Record<string, string> = {
  QualityNcr: "گزارش عدم انطباق",
  FinanceJournal: "سند حسابداری",
  BankLine: "ردیف صورت‌حساب بانکی",
  SupplierIbanChange: "تغییر شبا تامین‌کننده",
  PurchaseRfq: "استعلام خرید",
  OfficeLetter: "نامه",
  OfficeMeeting: "جلسه",
  PcWeeklyReport: "گزارش هفتگی پروژه",
  CeoBrief: "گزارش روزانه مدیرعامل",
};
/** The target types of the financial anomaly seat. */
export const financeNoteTypes = ["FinanceJournal", "BankLine", "SupplierIbanChange"];
/** FIN-ANOMALY rule codes. */
export const ruleTitles: Record<string, string> = { DUPLICATE: "سند یا ردیف تکراری", ROUND: "مبلغ رُند و بزرگ", OFFDAY: "ثبت در روز تعطیل", UNMATCHED: "ردیف بانکی تطبیق‌نخورده", IBAN: "تغییر شبا" };

/** AiSeatCodes -> Persian title (the server also sends a title; this is the fallback and the filter list). */
export const seatTitles: Record<string, string> = {
  "CEO-BRIEF": "گزارش روزانه‌ی مدیرعامل",
  "PMO-WEEKLY": "روایت گزارش هفتگی پروژه‌ها",
  "WH-REORDER": "پیش‌نویس درخواست خرید اقلام زیر حداقل",
  "PROC-COMPARE": "یادداشت مقایسه‌ی استعلام‌ها",
  "QC-NCR-ASSIST": "دستیار گزارش عدم انطباق",
  "FIN-ANOMALY": "بازبینی ناهنجاری‌های مالی",
  "OFFICE-DRAFT": "پیش‌نویس پاسخ نامه و خلاصه‌ی صورت‌جلسه",
  EVAL: "ارزیابی هوش مصنوعی",
};
export const seatTitleOf = (code: string | null | undefined, title?: string | null) => title || (code ? seatTitles[code] : "") || code || "";

/** AiAuditKinds. */
export const auditKinds: Record<string, string> = {
  chat: "گفت‌وگوی متنی",
  voice: "گفت‌وگوی صوتی",
  tool: "فراخوانی ابزار",
  unverified: "پاسخ تأییدنشده",
  degraded: "حالت کاهش‌یافته",
  failover: "جابه‌جایی به سرور پشتیبان",
  admin: "تغییر مدیریتی",
  seat: "اجرای صندلی",
  note: "تصمیم درباره یادداشت",
  eval: "ارزیابی",
};

/**
 * The links the API writes for a record do not always equal the paths of this front end (query form «?id=» instead of «/:id», other prefixes).
 * Returns a path that exists in the router, or null when the page is missing (the chip is then shown without a link).
 */
const idPaths: Record<string, string> = {
  "/inventory/ncr": "/inventory/ncr",
  "/fn/gl/vouchers": "/fn/vouchers",
  "/procurement/inquiries": "/procurement/inquiries",
  "/office/letters": "/office/letters",
  "/office/meetings": "/office/meetings",
};
const pathFix: Record<string, string> = {
  "/fn/treasury/bank-statements": "/fn/treasury/statements",
  "/inventory/ncrs": "/inventory/ncr",
};
export function frontLink(router: Router, link: string | null | undefined): string | null {
  if (!link) return null;
  let target = String(link).trim();
  if (!target.startsWith("/")) return null;
  const m = /^([^?#]+)\?id=(\d+)$/.exec(target);
  if (m && idPaths[m[1]]) target = idPaths[m[1]] + "/" + m[2];
  else if (pathFix[target]) target = pathFix[target];
  try {
    const resolved = router.resolve(target);
    if (!resolved.matched.length || resolved.matched.some((r) => r.path.includes("pathMatch"))) return null;
    return target;
  } catch {
    return null;
  }
}

/** Texts of the assistant: the server writes the same words into the answer when the model could not be verified or is down. */
export const NOT_VERIFIED_TEXT = "پاسخ مدل قابل راستی‌آزمایی نبود؛ نتایج مستقیم سامانه نمایش داده شد";
export const DEGRADED_TEXT = "تحلیل هوشمند موقتاً در دسترس نیست؛ داشبورد اصلی همچنان در دسترس است.";

/**
 * Reads a background job again and again (a seat run, an evaluation) until it has finished. tick() returns true when the job is finished (or must not be read any more).
 * The next read starts only after the previous one has answered and 2 seconds have passed (no overlapping requests). Returns the function that stops it (call it when the page is left).
 */
export function startPolling(tick: () => Promise<boolean>, ms = 2000, maxMs = 15 * 60 * 1000): () => void {
  let stopped = false;
  let timer: ReturnType<typeof setTimeout> | null = null;
  const began = Date.now();
  const step = async () => {
    if (stopped) return;
    let finished = false;
    try {
      finished = await tick();
    } catch {
      finished = true;
    }
    if (stopped || finished || Date.now() - began > maxMs) return;
    timer = setTimeout(step, ms);
  };
  timer = setTimeout(step, ms);
  return () => {
    stopped = true;
    if (timer) clearTimeout(timer);
  };
}

/**
 * A purchase request made by an AI seat (WH-REORDER drafts). The request DTO has no flag for it: the seat user is created with the last name «(صندلی هوش مصنوعی)» and the user name starts with
 * «seat.», and creatorName is "name last name". So the badge is shown when the creator name carries that marker.
 */
export const SEAT_MARK = "صندلی هوش مصنوعی";
/** The server marks a purchase request drafted by an AI seat (createdBySeat); the creator name is only a fallback for older answers. */
export const isSeatDraft = (request: { createdBySeat?: boolean; creatorName?: string | null } | null | undefined) =>
  !!request?.createdBySeat || (!!request?.creatorName && String(request.creatorName).includes(SEAT_MARK));
export const SEAT_DRAFT_TEXT = "پیش‌نویس دستیار انبار";
