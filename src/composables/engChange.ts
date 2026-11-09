const list = (csv: string) => csv.split(",");

// Labels and role lists of the engineering change (Package A2). The role lists mirror EcRules in the API; the API checks again on every call.

export const ecrStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "منتظر تصمیم مهندسی", 2: "تایید شده", 3: "رد شده", 4: "لغو شده" };
export const ecoStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در حال تایید", 2: "تایید شده، در حال اجرا", 3: "رد شده", 4: "بسته شده", 5: "لغو شده" };
export const statusClass: Record<number, string> = { 0: "warn", 1: "warn", 2: "ok", 3: "bad", 4: "ok", 5: "bad" };
export const ecClasses: Record<number, string> = { 1: "کلاس I (فرم، جا، کارکرد، ایمنی، دید مشتری)", 2: "کلاس II" };
export const ecUrgencies: Record<number, string> = { 1: "عادی", 2: "فوری", 3: "بحرانی" };
export const targetKinds: Record<number, string> = { 1: "محصول", 2: "قطعه (شماره قطعه)", 3: "BOM", 4: "چارت OPC", 5: "نقشه (سند)", 6: "مسیر ساخت (MES)" };
export const changeKinds: Record<number, string> = { 1: "BOM", 2: "چارت OPC", 3: "نقشه", 4: "مسیر ساخت", 5: "فهرست مشخصه‌ها" };
export const changeActions: Record<number, string> = { 1: "بازنگری جدید", 2: "منسوخ شود" };
export const impactCategories: Record<number, string> = {
  1: "کاربرد در BOM محصولات", 2: "پایه پروژه‌ها", 3: "دستورهای تولید و مسیر ساخت", 4: "موجودی و کار در جریان",
  5: "درخواست و سفارش خرید باز", 6: "نقاط کنترل کیفیت و فهرست مشخصه‌ها", 7: "گام‌های فرایند ویژه", 8: "واحدهای تحویل‌شده",
};
export const dispositions: Record<number, string> = {
  1: "استفاده بدون تغییر", 2: "بازکاری", 3: "اسقاط", 4: "برگشت به تامین‌کننده", 5: "تکمیل با بازنگری قدیم", 6: "تغییر به بازنگری جدید", 7: "اصلاح واحد تحویل‌شده (Retrofit)",
};
export const taskStatuses: Record<number, string> = { 0: "باز", 1: "انجام شده، منتظر تایید QA", 2: "تایید شده" };

export const EcRoles = {
  raisers: list("ENGINEERING,ENGINEER,QA,QC,PMO,PRODUCTIONMANAGER,CONSTRUCTION,SHOPSUPERVISOR,PLANNING"),
  approvers: list("ENGINEERING"),
  orderMakers: list("ENGINEERING,ENGINEER,PMO"),
  readers: list("PMO,CEO,ENGINEERING,ENGINEER,QA,QC,PLANNING,PROJECTMANAGER,PRODUCTIONMANAGER,CONSTRUCTION,SHOPSUPERVISOR,AUDITOR,DCC,COMMERCIAL,PROCUREMENT,WAREHOUSE"),
};
