// PB3: names of the attachment layer (the numbers are the enum values of the API).

export const AttachmentOwner = {
  PurchaseRequest: 1,
  PurchaseRequestLine: 2,
  Inquiry: 3,
  Quote: 4,
  PurchaseOrder: 5,
  WarehouseDocument: 6,
  QcInspection: 7,
  Ncr: 8,
  ProductionOperation: 9,
  PartOperation: 10,
  SupplierInvoice: 11,
  PaymentRequest: 12,
  TreasuryPayment: 13,
  ProjectCostEntry: 14,
  Contract: 15,
  Tender: 16,
  PettyCashSettlementLine: 17,
  InventoryItem: 18,
  InventoryLot: 19,
  SerialUnit: 20,
  BuyOrder: 21,
  Process: 22,
  EquipmentProcess: 23,
  TmTask: 24,
  PcProgressSubmission: 25,
  PcProjectPhoto: 26,
  PbBomHeader: 27,
  PbOpcRouting: 28,
} as const;

export const attachmentKinds: Record<number, string> = {
  1: "نقشه",
  2: "BOM",
  3: "مشخصات فنی",
  4: "دیتاشیت",
  5: "گزارش کنترل کیفیت",
  6: "گواهی",
  7: "برگه اندازه‌گیری",
  8: "صورتجلسه",
  9: "فاکتور / رسید",
  10: "فیش واریزی",
  11: "بارنامه / برگه تحویل",
  12: "عکس",
  13: "فرم",
  99: "سایر",
};

/** The default kind offered on each kind of record. */
export const defaultAttachmentKind: Record<number, number> = {
  1: 1, 2: 3, 3: 3, 4: 99, 5: 99, 6: 11, 7: 5, 8: 5, 9: 7, 10: 3, 11: 9, 12: 10, 13: 10, 14: 9, 15: 99, 16: 99, 17: 9, 18: 4, 19: 6, 20: 13, 21: 99, 22: 1, 23: 1, 24: 99, 25: 99, 26: 99,
};

export function fileSizeText(bytes: number | null | undefined): string {
  const b = Number(bytes) || 0;
  if (b < 1024) return b + " B";
  if (b < 1024 * 1024) return (b / 1024).toFixed(1) + " KB";
  return (b / 1024 / 1024).toFixed(1) + " MB";
}
