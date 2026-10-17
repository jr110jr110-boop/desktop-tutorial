// Names for the finance pages (AccountingEventType, AccountingEventStatus).
export const eventTypes: Record<number, string> = {
  1: "رسید خرید",
  2: "برگشت به تامین‌کننده",
  3: "مصرف مواد (حواله)",
  4: "برگشت از تولید",
  5: "رسید ساخت",
  6: "اضافات انبار",
  7: "کسورات انبار",
  8: "موجودی اول دوره",
  9: "تحویل به مشتری",
  10: "رسید ضایعات",
  11: "فروش ضایعات",
  12: "تبدیل مرحله",
  20: "فاکتور تامین‌کننده",
  21: "پیش‌پرداخت تامین‌کننده",
  22: "درخواست پرداخت",
  99: "برگشت (ابطال)",
};

export const eventStatuses: Record<number, string> = {
  0: "در انتظار ارسال",
  1: "ارسال شد",
  2: "ارسال ناموفق",
  3: "خروجی فایل گرفته شد",
  9: "نیاز به سند ندارد",
};

/** Where the number of an event leads to, by the source type the API writes. */
export function sourceRoute(sourceType: string, sourceId: number) {
  if (sourceType === "InventoryDocument") return { name: "InventoryDocumentEdit", params: { id: sourceId } };
  if (sourceType === "SupplierInvoice") return { name: "ProcurementInvoices", query: { id: sourceId } };
  if (sourceType === "PaymentRequest") return { name: "ProcurementPayments", query: { id: sourceId } };
  return null;
}

/** Account roles of the voucher lines, in the words the accountant knows. */
export const accountRoleTitles: Record<string, string> = {
  Inventory: "موجودی کالا",
  GRNI: "کالای دریافتی فاکتورنشده",
  WIP: "کالای در جریان ساخت",
  MaterialExpense: "هزینه مواد مصرفی",
  COGS: "بهای تمام‌شده کالای فروش‌رفته",
  InventoryGain: "اضافات انبار",
  InventoryLoss: "کسورات انبار",
  OpeningBalance: "تراز افتتاحیه موجودی",
  ScrapRecovery: "بازیافت ضایعات",
  ScrapCost: "بهای تمام‌شده ضایعات",
  ScrapRevenue: "درآمد فروش ضایعات",
  Bank: "بانک",
  InventoryRevaluation: "تعدیل ارزش موجودی",
  AccountsPayable: "حساب‌های پرداختنی",
  PPV: "اختلاف قیمت خرید",
  VAT: "مالیات بر ارزش افزوده خرید",
  SupplierAdvance: "پیش‌پرداخت تامین‌کننده",
  PurchasedServices: "خدمات و پیمانکاری خریداری‌شده",
};

export function roleTitle(role: string): string {
  if (accountRoleTitles[role]) return accountRoleTitles[role];
  const [main, group] = String(role || "").split(".");
  return accountRoleTitles[main] ? `${accountRoleTitles[main]} (گروه ${group})` : role;
}
