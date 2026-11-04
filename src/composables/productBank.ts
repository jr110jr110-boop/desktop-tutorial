const list = (csv: string) => csv.split(",");

// Labels and role lists of the product bank (PB1). The role lists mirror PbRoles in the API; the API checks again on every call.

export const bomStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در حال بررسی", 2: "تایید شده", 3: "منتشر شده", 4: "جایگزین شده", 5: "باطل" };
export const bomStatusClass: Record<number, string> = { 0: "warn", 1: "warn", 2: "ok", 3: "ok", 4: "", 5: "bad" };
export const signatureTitles: Record<number, string> = { 0: "تهیه‌کننده", 1: "کنترل‌کننده", 2: "کنترل کیفیت", 3: "کنترل ساخت", 4: "تاییدکننده" };
export const signatureRoles: Record<number, string> = { 1: "مهندسی", 2: "کنترل کیفیت", 3: "ساخت", 4: "مهندسی یا PMO" };
export const makeOrBuy: Record<number, string> = { 1: "ساخت (MPL)", 2: "خرید (PL)", 3: "تامین مشتری (CS)", 4: "پیمانکار (SC)" };
export const makeOrBuyShort: Record<number, string> = { 1: "MPL", 2: "PL", 3: "CS", 4: "SC" };
export const stageKinds: Record<number, string> = { 1: "عملیات", 2: "کنترل کیفیت", 3: "حمل", 4: "انبار", 5: "توقف بازرسی" };
/** OPC symbols: circle = operation, square = quality control, arrow = transport, triangle = storage (inspection hold shown as a diamond). */
export const stageSymbols: Record<number, string> = { 1: "○", 2: "□", 3: "→", 4: "△", 5: "◇" };
export const routingStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "در حال بررسی", 2: "تایید شده", 3: "جایگزین شده" };
export const mappingStatuses: Record<number, string> = { 0: "پیش‌نویس (منتظر تایید مهندسی)", 1: "تایید شد", 2: "رد شد" };
export const mappingSources: Record<number, string> = { 1: "پیشنهاد خودکار", 2: "ورود از Excel", 3: "دستی" };
export const changeKinds: Record<number, string> = { 1: "افزودن", 2: "حذف", 3: "جایگزینی", 4: "تغییر تعداد" };
export const changeStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال شد", 2: "تایید شد", 3: "رد شد", 4: "اعمال شد", 5: "لغو شد" };
export const evidenceKinds = ["DIM", "VT", "UT", "MT", "PT", "NDT", "TENSILE", "HARDNESS", "IMPACT", "CHEM", "HYDRO", "BALANCE", "RUNOUT"];
export const wbsKinds: Record<number, string> = { 1: "خلاصه", 2: "فعالیت", 3: "نقطه عطف" };
export const linkTypes: Record<number, string> = { 0: "FF", 1: "FS", 2: "SF", 3: "SS" };

export const PbRoles = {
  coding: list("PMO,PLANNING,ENGINEERING,ENGINEER"),
  writers: list("PMO,ENGINEERING,ENGINEER"),
  engineering: list("ENGINEERING,PMO"),
  planning: list("PLANNING,PMO"),
  requesters: list("PMO,ENGINEERING,ENGINEER,PLANNING,PROJECTMANAGER,CONSTRUCTION,PRODUCTIONMANAGER"),
  publish: list("PMO,ENGINEERING,ENGINEER,PLANNING,PRODUCTIONMANAGER"),
  admin: list("PMO"),
  /** Everyone who opens the product bank screens: the writers, the requesters and the reviewers (QC, shop). */
  readers: list("PMO,ENGINEERING,ENGINEER,PLANNING,PROJECTMANAGER,CONSTRUCTION,PRODUCTIONMANAGER,SHOPSUPERVISOR,QC,QA"),
};

export const revisionText = (n: number | null | undefined) => (n === null || n === undefined ? "" : String(n).padStart(2, "0"));
export const num = (v: number | null | undefined) => (v === null || v === undefined ? "" : String(Math.round(v * 10000) / 10000));
