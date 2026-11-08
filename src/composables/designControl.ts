const list = (csv: string) => csv.split(",");

// Labels and role lists of the design and development control (Package A1). The role lists mirror DesignRoles in the API; the API checks again on every call.

export const DesignRoles = {
  /** Prepares a plan. */
  planMakers: list("ENGINEERING,ENGINEER"),
  /** Approves a plan (never the preparer); a plan that waives the validation is approved by QA only. */
  planApprovers: list("ENGINEERING,QA"),
  inputWriters: list("ENGINEERING,ENGINEER,QA,PMO"),
  /** Resolves a conflict of inputs and declares the inputs complete (a second person). */
  resolvers: list("ENGINEERING,QA,PMO"),
  reviewMakers: list("ENGINEERING,ENGINEER,QA,PMO"),
  reviewClosers: list("ENGINEERING,QA,PMO"),
  qa: ["QA"],
  /** Everyone who opens the design screens. */
  readers: list("PMO,ENGINEERING,ENGINEER,PLANNING,PROJECTMANAGER,CONSTRUCTION,PRODUCTIONMANAGER,SHOPSUPERVISOR,QC,QA"),
};

export const planStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "ارسال‌شده", 2: "تایید شده", 3: "جایگزین شده", 4: "باطل" };
export const planStatusClass: Record<number, string> = { 0: "warn", 1: "warn", 2: "ok", 3: "", 4: "bad" };
export const stageKinds: Record<number, string> = { 1: "ورودی‌های طراحی", 2: "طراحی", 3: "بازبینی طراحی", 4: "تایید طراحی (Verification)", 5: "صحه‌گذاری طراحی (Validation)", 6: "انتشار" };
export const stageStatuses: Record<number, string> = { 0: "شروع نشده", 1: "در حال انجام", 2: "پایان یافته", 3: "لازم نیست" };
export const stageStatusClass: Record<number, string> = { 0: "", 1: "warn", 2: "ok", 3: "" };
export const inputKinds: Record<number, string> = {
  1: "الزام کارکردی", 2: "الزام عملکردی", 3: "الزام قانونی و مقرراتی", 4: "استاندارد و آیین‌نامه", 5: "طراحی قبلی مشابه", 6: "مشخصات مشتری (قرارداد)", 7: "پیامد بالقوه شکست",
};
export const inputStatuses: Record<number, string> = { 0: "معتبر", 1: "تعارض", 2: "جایگزین شده", 3: "باطل" };
export const inputStatusClass: Record<number, string> = { 0: "ok", 1: "bad", 2: "", 3: "" };
export const reviewTypes: Record<number, string> = { 1: "بازبینی طراحی (Review)", 2: "تایید طراحی (Verification)", 3: "صحه‌گذاری طراحی (Validation)" };
export const reviewStatuses: Record<number, string> = { 0: "برنامه‌ریزی شده", 1: "برگزار شده", 2: "بسته شده", 3: "باطل" };
export const reviewStatusClass: Record<number, string> = { 0: "warn", 1: "warn", 2: "ok", 3: "bad" };
export const reviewOutcomes: Record<number, string> = { 0: "-", 1: "پذیرفته", 2: "پذیرفته با شرط", 3: "رد شده" };
export const outcomeClass: Record<number, string> = { 0: "", 1: "ok", 2: "warn", 3: "bad" };
export const outputKinds: Record<number, string> = { 1: "نقشه / سند (EDMS)", 2: "BOM محصول", 3: "مسیر ساخت (OPC)" };
export const traceResults: Record<number, string> = { 0: "قضاوت نشده", 1: "قبول", 2: "مردود" };
export const traceClass: Record<number, string> = { 0: "", 1: "ok", 2: "bad" };
export const findingStatuses: Record<number, string> = { 0: "باز", 1: "انجام شد (در انتظار بستن)", 2: "بسته شد", 3: "باطل" };
export const findingClass: Record<number, string> = { 0: "warn", 1: "warn", 2: "ok", 3: "" };
export const validationMethods: Record<number, string> = { 1: "آزمون روی محصول", 2: "آزمایش میدانی", 3: "شبیه‌سازی" };

export const revisionText = (n: number | null | undefined) => (n === null || n === undefined ? "" : String(n).padStart(2, "0"));
