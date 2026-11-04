// Labels and role lists of the material balance (PB2). The role lists mirror BalanceRoles in the API; the API checks again on every call.

export const requirementKinds: Record<number, string> = { 1: "خرید", 2: "ماده اولیه قطعهٔ ساخت", 3: "ماده اولیه کدگذاری نشده" };
export const reservationStatuses: Record<number, string> = { 1: "فعال", 2: "آزادشده", 3: "کاملاً حواله‌شده" };
export const reservationStatusClass: Record<number, string> = { 1: "info", 2: "", 3: "ok" };
export const substituteStatuses: Record<number, string> = { 0: "در انتظار تایید", 1: "تایید شده", 2: "رد شده", 3: "لغو شده" };
export const substituteStatusClass: Record<number, string> = { 0: "warn", 1: "ok", 2: "bad", 3: "" };
export const substituteScopes: Record<number, string> = { 1: "عمومی", 2: "مخصوص پروژه" };
export const priorityTitles: Record<number, string> = { 0: "کم", 1: "عادی", 2: "بالا", 3: "فوری" };
export const diffTitles: Record<string, string> = { Added: "افزوده", Removed: "حذف‌شده", Changed: "تغییر کرده" };

const list = (csv: string) => csv.split(",");
export const BalanceRoles = {
  planning: list("PMO,PLANNING"),
  substituteProposers: list("PMO,PLANNING,ENGINEERING,ENGINEER"),
  substituteApprovers: list("ENGINEERING,PMO"),
  /** Everyone who opens the balance screens. */
  readers: list("PMO,PLANNING,ENGINEERING,ENGINEER,PROJECTMANAGER,COMMERCIAL,WAREHOUSE,WHSUPERVISOR,CONSTRUCTION,QC,CEO"),
};

export const qty = (v: number | null | undefined) => (v === null || v === undefined ? "" : String(Math.round(v * 10000) / 10000));
