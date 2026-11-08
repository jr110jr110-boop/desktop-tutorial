// Names, roles and helpers of the DNC (production release P2 part A). Enum values are numbers in the JSON and match Entities/Model/NasbNiro/Production/DncEntities.cs exactly;
// the role lists mirror DncRoles in Services/NasbNiro/Production/Dnc/DncCommon.cs (the service decides; the page only hides what would be refused).

const list = (csv: string) => csv.split(",");
const masterReaders = "PMO,CEO,PRODUCTIONMANAGER,SHOPSUPERVISOR,PLANNING,QC,QA,ENGINEERING,ENGINEER,PROJECTMANAGER,WHSUPERVISOR,WAREHOUSE,MAINTENANCE,FINANCEMANAGER,AUDITOR,OPERATOR,ASSEMBLER,CONSTRUCTION";
const terminal = "OPERATOR,SHOPSUPERVISOR,PRODUCTIONMANAGER,ASSEMBLER,CONSTRUCTION";

export const DncRoles = {
  /** the library, transfers, runs, the terminal panel */
  readers: list(masterReaders),
  /** open programs and upload revisions (the CAM programmers) */
  writers: list("PRODUCTIONMANAGER,ENGINEERING,ENGINEER,PMO"),
  /** approve or reject a revision (never its author) */
  approvers: list("PRODUCTIONMANAGER,SHOPSUPERVISOR,ENGINEERING,ENGINEER"),
  /** send a program and confirm it on the control */
  senders: list(terminal),
  /** fetch a program back from a machine */
  receivers: list("SHOPSUPERVISOR,PRODUCTIONMANAGER,ENGINEERING,ENGINEER"),
  /** run the transfer queue now, cancel or retry anybody's transfer */
  supervisors: list("SHOPSUPERVISOR,PRODUCTIONMANAGER"),
  /** machine links and gateway keys */
  linkEditors: list("PRODUCTIONMANAGER,PMO"),
  /** test the link of a machine */
  linkTesters: list("PRODUCTIONMANAGER,PMO,MAINTENANCE,SHOPSUPERVISOR"),
};

export const controllerFamilies: Record<number, string> = {
  0: "نامشخص", 1: "فانوک", 2: "زیمنس", 3: "هایدن‌هاین (گفت‌وگویی)", 4: "هایدن‌هاین (ISO)", 5: "میتسوبیشی", 6: "هاس", 7: "مزاک", 8: "اوکوما", 9: "فاگور", 10: "فیلیپس / میل‌پلاس", 99: "سایر",
};
export const linkKinds: Record<number, string> = { 1: "دستی (حافظه USB)", 2: "پوشه شبکه", 3: "FTP کنترلر", 4: "مبدل سریال به شبکه (RS-232)", 5: "گیت‌وی لبه (مثل جعبه الکسیو)" };
export const lineEndings: Record<number, string> = { 1: "CR LF (ویندوز)", 2: "LF", 3: "CR" };
export const programStatuses: Record<number, string> = { 1: "فعال", 2: "منسوخ" };
export const revisionStatuses: Record<number, string> = { 0: "پیش‌نویس", 1: "منتظر تأیید", 2: "تأییدشده (جاری)", 3: "جایگزین‌شده", 4: "ردشده", 5: "پس‌گرفته" };
export const revisionSources: Record<number, string> = { 1: "بارگذاری", 2: "دریافت از ماشین" };
export const transferDirections: Record<number, string> = { 1: "به ماشین", 2: "از ماشین" };
export const transferStatuses: Record<number, string> = { 0: "در صف", 1: "در حال انتقال", 2: "تحویل به کنترلر", 3: "تأیید اپراتور", 4: "خطا", 5: "لغو شده", 6: "دریافت‌شده" };
export const revisionActionTitles: Record<string, string> = { submit: "ارسال برای تأیید", approve: "تأیید", reject: "رد", withdraw: "پس‌گرفتن" };

export function badgeOfRevision(status: number): string {
  return status === 2 ? "ok" : status === 1 ? "info" : status === 0 ? "warn" : status === 4 ? "bad" : "";
}
export function badgeOfTransfer(status: number): string {
  return status === 3 || status === 6 ? "ok" : status === 4 ? "bad" : status === 2 ? "info" : status === 5 ? "" : "warn";
}

/** Bytes as a short Persian-friendly size (1.2 KB). */
export function sizeText(bytes: number | null | undefined): string {
  if (!bytes) return "-";
  if (bytes < 1024) return bytes + " B";
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB";
  return (bytes / 1024 / 1024).toFixed(2) + " MB";
}

/** The class of a line of a difference ("+ ", "- ", "  ", "…"). */
export function diffClass(line: string): string {
  return line.startsWith("+ ") ? "dnc-add" : line.startsWith("- ") ? "dnc-del" : line === "…" ? "dnc-gap" : "";
}

/**
 * Starting values of a machine link for the company's machines (from their plates and control panels, 1405/08/17): three Heidenhain TNC 4xx behind serial converters and a lathe.
 * Only a suggestion for the form; nothing is saved without the production manager.
 */
export const linkPresets: Record<string, any> = {
  "Heidenhain TNC 426/430 (Mikron VCP 710)": { controllerFamily: 3, controllerModel: "TNC 426/430", linkKind: 4, port: 4001, programNameFormat: "{number}", fileExtension: ".H", lineEnding: 1, axes: "XYZ", maxSpindleRpm: 12000, baudRate: 115200, requirePercent: false, isLathe: false },
  "Heidenhain TNC 4xx (Reiden، محورهای X Y Z B W)": { controllerFamily: 3, controllerModel: "TNC 4xx", linkKind: 4, port: 4001, programNameFormat: "{number}", fileExtension: ".H", lineEnding: 1, axes: "XYZBW", baudRate: 115200, requirePercent: false, isLathe: false },
  "Heidenhain TNC 4xx (Deckel Maho)": { controllerFamily: 3, controllerModel: "TNC 4xx", linkKind: 4, port: 4001, programNameFormat: "{number}", fileExtension: ".H", lineEnding: 1, axes: "XYZ", baudRate: 115200, requirePercent: false, isLathe: false },
  "تراش با کنترلر فانوک (CMZ TBI520)": { controllerFamily: 1, controllerModel: "Fanuc", linkKind: 4, port: 4001, programNameFormat: "O{number}", fileExtension: "", lineEnding: 1, axes: "XZ", baudRate: 9600, requirePercent: true, isLathe: true },
};
