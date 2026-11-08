const list = (csv: string) => csv.split(",");

// Labels and role lists of the AI internal auditor (Package 9). The role lists mirror AusRoles in the API; the API checks again on every call.

export const AuditRoles = {
  readers: list("AUDITOR,QA,PMO,CEO,PLANNING,PROJECTMANAGER,QC,ENGINEERING,FINANCEMANAGER,PRODUCTIONMANAGER,COMMERCIAL,DCC"),
  /** every role that can be the owner of a finding sees its own findings */
  actioners: list("AUDITOR,QA,PMO,CEO,PLANNING,PROJECTMANAGER,QC,ENGINEERING,ENGINEER,FINANCEMANAGER,FINANCIAL,PRODUCTIONMANAGER,SHOPSUPERVISOR,COMMERCIAL,DCC,WAREHOUSE,WHSUPERVISOR,CONSTRUCTION,MAINTENANCE"),
  runners: list("AUDITOR,QA,PMO"),
  auditors: list("AUDITOR"),
};

export const findingStatuses: Record<number, string> = {
  1: "باز", 2: "اقدام پیشنهاد شده", 3: "بازبینی قبول شد (منتظر QA)", 4: "پیشنهاد QA (منتظر PMO)", 5: "پیشنهاد PMO (منتظر مدیرعامل)", 6: "بسته شد", 7: "باطل شد",
};
export const findingStatusClass: Record<number, string> = { 1: "bad", 2: "warn", 3: "warn", 4: "warn", 5: "warn", 6: "ok", 7: "muted" };
export const severities: Record<number, string> = { 1: "مشاهده", 2: "جزئی", 3: "عمده" };
export const severityClass: Record<number, string> = { 1: "muted", 2: "warn", 3: "bad" };
export const reportKinds: Record<number, string> = { 1: "روزانه", 2: "هفتگی (PMO و QA)", 3: "ماهانه (مدیرعامل)" };
export const frequencies: Record<number, string> = { 0: "پیوسته (روزانه، خودکار)", 1: "ماهانه", 2: "فصلی", 3: "شش‌ماهه", 4: "سالانه" };
export const programStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "تایید شده", 2: "جایگزین شد", 3: "رد شد" };
export const auditStatuses: Record<number, string> = { 0: "برنامه‌ریزی شده", 1: "در حال انجام", 2: "کامل شد", 3: "لغو شد" };
export const checkResults: Record<number, string> = { 0: "بررسی نشده", 1: "منطبق", 2: "نامنطبق", 3: "کاربرد ندارد" };
export const stepTitles: Record<string, string> = {
  qa: "پیشنهاد بستن (QA)", pmo: "پیشنهاد بستن (PMO)", ceo: "تایید نهایی و بستن (مدیرعامل)",
  "void-qa": "درخواست ابطال (QA)", "void-pmo": "ابطال (PMO)", "void-ceo": "ابطال (مدیرعامل)",
};
