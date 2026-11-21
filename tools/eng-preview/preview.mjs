// Visual self-review of the engineering module (ENG-E1): serves the built front end (dist/), signs in with a fake token for the role of each shot, answers every api/v1 call
// from the fixtures below (built from the real DTO shapes, EngineeringDtos/*.cs) and saves one screenshot per page and major state.
//   NODE_OPTIONS=--max-old-space-size=2500 npx vite build && node tools/eng-preview/preview.mjs [word ...]
// Output: ENG_SHOTS (default tools/eng-preview/out). Nothing here talks to a real server. Playwright comes from /opt/npm-tools (or the project) and Chrome from /usr/bin/google-chrome.
import { createRequire } from "module";
import { createServer } from "http";
import { existsSync, mkdirSync, readFileSync, statSync } from "fs";
import { dirname, extname, join, normalize, resolve } from "path";
import { fileURLToPath } from "url";

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require("playwright")); } catch { ({ chromium } = createRequire("/opt/npm-tools/package.json")("playwright")); }
const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "../..");
const dist = join(root, "dist");
const out = process.env.ENG_SHOTS || join(here, "out");
mkdirSync(out, { recursive: true });
const types = { ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".css": "text/css", ".ttf": "font/ttf", ".woff": "font/woff", ".woff2": "font/woff2", ".png": "image/png", ".svg": "image/svg+xml", ".json": "application/json" };
const server = createServer((req, res) => {
  let file = normalize(join(dist, decodeURIComponent((req.url || "/").split("?")[0])));
  if (!file.startsWith(dist)) return res.writeHead(403).end();
  if (!existsSync(file) || statSync(file).isDirectory()) file = join(dist, "index.html");
  res.writeHead(200, { "Content-Type": types[extname(file)] || "application/octet-stream" });
  res.end(readFileSync(file));
});
await new Promise((ok) => server.listen(0, "127.0.0.1", ok));
const base = `http://127.0.0.1:${server.address().port}`;

// ---------------------------------------------------------------- fixtures (DTO shapes)
const ok = (data) => JSON.stringify({ success: true, message: "", data, httpStatusCode: 200 });
const refuse = (message) => JSON.stringify({ success: false, message, data: null, httpStatusCode: 200 });
const page = (rows) => ({ data: rows, totalItems: rows.length, currentPage: 1, itemsPerPage: 25, totalPages: 1 });
const NO = "شما مجوز این کار را ندارید.";
const flags = (names, on = false, why = NO) => Object.fromEntries(names.flatMap((n) => [["can" + n, on], [n[0].toLowerCase() + n.slice(1) + "BlockedReason", on ? null : why]]));

const overviewEmpty = { stalledTasksCount: 0, stalledTasks: [], controlStarted: true, controlStartText: "", casesByStatus: [], casesByClassification: [], platformsByDossierStatus: [], platformsTotal: 0, platformsIncomplete: 0, openClarifications: 0, handoversWaiting: [],
  openDebts: 0, overdueDebts: 0, waiversAwaitingCeo: 0, oldestWaiverWaitingHours: 0, distributionsUnacknowledged: 0, casesAtDocumentRisk: 0, awardedCasesAtDocumentRisk: 0, deviationsAwaiting: 0,
  standardsDeviationsOpen: 0, deviationsAwaitingCeo: 0, deviationsAwaitingManager: 0, oldestDeviationWaitingHours: 0, oldestRegistrationWaitingHours: 0, standardGapsOpen: 0, infoRequestsOpen: 0, registrationsOpen: 0 };
const overviewAlarm = { ...overviewEmpty, stalledTasksCount: 1, stalledTasks: [{ taskId: 5, title: "ثبت استاندارد دیتاشیت اصلی پلتفرم 99.11", waitingRolesText: "مدیر مهندسی، مهندس", failed: false, dueAt: "2026-11-20T09:00:00+03:30", dueAtText: "", link: "/engineering/platforms/1" }], casesByStatus: [{ key: 2, text: "منتظر تایید مدیر مهندسی", count: 2 }, { key: 4, text: "تهیه بسته فنی مناقصه", count: 3 }, { key: 7, text: "در مهندسی", count: 4 }],
  casesByClassification: [{ key: 1, text: "پلتفرم (توربین ساخته‌شده)", count: 6 }, { key: 2, text: "طراحی جدید", count: 3 }],
  platformsByDossierStatus: [{ status: 0, statusText: "ارزیابی‌نشده", count: 3, missingRequired: 0 }, { status: 1, statusText: "ناقص", count: 4, missingRequired: 27 }, { status: 2, statusText: "کامل‌شده، منتظر تایید مدیر مهندسی", count: 1, missingRequired: 0 }, { status: 3, statusText: "کامل", count: 5, missingRequired: 0 }],
  platformsTotal: 13, platformsIncomplete: 4, openClarifications: 3, handoversWaiting: [{ key: 1, text: "ارسال‌شده به PMO", count: 2 }, { key: 2, text: "برگشت‌خورده", count: 1 }], openDebts: 5, overdueDebts: 2,
  waiversAwaitingCeo: 2, oldestWaiverWaitingHours: 53, distributionsUnacknowledged: 4, casesAtDocumentRisk: 3, awardedCasesAtDocumentRisk: 1, deviationsAwaiting: 2, standardsDeviationsOpen: 3, deviationsAwaitingCeo: 1, deviationsAwaitingManager: 1, oldestDeviationWaitingHours: 30, oldestRegistrationWaitingHours: 20, standardGapsOpen: 1, infoRequestsOpen: 2, registrationsOpen: 1 };

const caseBase = { id: 12, kind: 1, kindText: "توربین", number: "ENG-1405-0012", title: "توربین بخار پروژه نمونه", inquiryId: 4, inquiryNumber: "INQ-1405-0004", contractId: null, contractCode: null, customerName: "شرکت نمونه",
  customerRequirementNote: "مطابق دیتاشیت پیوست", status: 2, statusText: "منتظر تایید مدیر مهندسی", classification: 0, classificationText: "تعیین‌نشده", productId: null, productCode: null, proposedClassification: 1,
  proposedClassificationText: "پلتفرم (توربین ساخته‌شده)", proposedProductId: 7, proposedProductCode: "ST-120", proposedWithChanges: true, proposedChangeNote: "تغییر در محفظه یاتاقان", proposalBasis: "ظرفیت و فشار بخار با پلتفرم ST-120 برابر است؛ فقط محفظه یاتاقان تغییر می‌کند.",
  proposedByAgent: true, proposedByName: "ایجنت مهندسی", proposedAtText: "1405/08/20 09:10", platformIncomplete: true, platformMissingRequired: 6, platformWarning: "۶ مدرک الزامی پلتفرم ST-120 برای استاندارد API 611 موجود نیست.",
  standardGap: { id: 3, number: "GAP-1405-0003", status: 0, statusText: "پیش‌نویس", summary: "۲ مدرک افزوده و ۱ مدرک تغییر الزام نسبت به استاندارد پلتفرم" }, tenderPackageCode: null, tenderDocumentCount: 0, tenderStale: false,
  charterReviewOutcome: 0, charterReviewOutcomeText: "بررسی نشده", charterExists: true, charterStale: false, openClarifications: 1, openHandoverId: null, referenceCode: "API 611", standardEvidence: "دیتاشیت پیوست ۳، بند ۴-۲", standardMissing: false, standardNotice: null,
  events: [{ kind: "opened", atText: "1405/08/19 08:00", userName: "ایجنت مهندسی", note: null }, { kind: "proposed", atText: "1405/08/20 09:10", userName: "ایجنت مهندسی", note: "پیشنهاد پلتفرم ST-120" }], rowVersion: "AAAA",
  tenderDocuments: [], documentRisk: null, documentRiskAwarded: false, canAskClarification: true,
  ...flags(["Propose", "Change", "Confirm", "Approve", "Return", "Reclassify", "SaveConcept", "ReleaseTender", "CharterReview", "Cancel", "SetStandard", "SaveOpinion", "AddTenderDocument", "ProposeRegistration"]),
  addTenderDocumentBlockedReason: "پرونده در مرحلهٔ تهیه بسته فنی مناقصه نیست.", proposeRegistrationBlockedReason: "برای پیشنهاد ثبت، تحویل مدارک باید منتشر شده باشد." };
const gapDto = { id: 3, number: "GAP-1405-0003", caseId: 12, caseNumber: "ENG-1405-0012", baseCode: "API 612", targetCode: "API 611", baseRevision: 2, targetRevision: 1, status: 0, statusText: "پیش‌نویس", addedCount: 2, removedCount: 0, requiredChangedCount: 1, reusableCount: 9,
  summary: "۲ مدرک افزوده و ۱ مدرک تغییر الزام نسبت به استاندارد پلتفرم", engineeringImpactNote: null, rowVersion: "BBBB", ...flags(["Confirm"]),
  lines: [{ seq: 1, kind: 1, kindText: "افزوده", documentType: "DWG", discipline: "مکانیک", title: "نقشه محفظه یاتاقان جدید", baseRequired: false, targetRequired: true }, { seq: 2, kind: 1, kindText: "افزوده", documentType: "CALC", discipline: "مکانیک", title: "محاسبه ارتعاش پیچشی", baseRequired: false, targetRequired: true },
    { seq: 3, kind: 3, kindText: "تغییر الزام", documentType: "RPT", discipline: "کیفیت", title: "گزارش تست عملکرد", baseRequired: false, targetRequired: true }, { seq: 4, kind: 0, kindText: "بدون تغییر", documentType: "DWG", discipline: "مکانیک", title: "نقشه مونتاژ کلی", baseRequired: true, targetRequired: true }] };
const clar = { id: 5, number: "CLR-1405-0005", caseId: 12, caseNumber: "ENG-1405-0012", question: "فشار ورودی بخار در دیتاشیت ۴۰ بار است یا ۴۵ بار؟", askedByName: "مهندس طراح", askedAtText: "1405/08/21 10:00", status: 1, statusText: "نزد بازرگانی",
  sentToClientByName: null, clientLetterRef: null, answer: null, rowVersion: "CCCC", ...flags(["Save", "Submit", "Send", "Answer", "Close", "Reask", "Void"]) };
const dossier = { id: 9, referenceCode: "API 611", standardBasis: "دیتاشیت اصلی توربین ST-120، بند ۲", otherDossiers: [{ id: 10, referenceCode: "API 612", status: 3, statusText: "کامل" }], nativeStandardMissing: false, productId: 7, productCode: "ST-120", productTitle: "توربین بخار ۱۲۰ کیلووات",
  referenceListId: 2, referenceRevision: 1, referenceTitle: "فهرست مرجع API 611", stale: false, status: 1, statusText: "ناقص", requiredCount: 4, availableCount: 2, missingCount: 2, tenderStageMissingCount: 1, note: null, rowVersion: "DDDD",
  ...flags(["RecordStandard", "Assess", "Supply", "Complete", "Confirm", "Return"]),
  lines: [{ id: 101, seq: 1, documentType: "DWG", discipline: "مکانیک", title: "نقشه مونتاژ کلی", required: true, tenderStage: true, state: 1, stateText: "موجود", documentId: "11111111-1111-1111-1111-111111111111", revision: "B", systemProven: false, suppliedByName: "مهندس طراح", suppliedAtText: "1405/08/10", rowVersion: "r1" },
    { id: 102, seq: 2, documentType: "CALC", discipline: "مکانیک", title: "محاسبه ارتعاش پیچشی", required: true, tenderStage: false, state: 0, stateText: "موجود نیست", rowVersion: "r2" },
    { id: 103, seq: 3, documentType: "RPT", discipline: "کیفیت", title: "گزارش تست عملکرد", required: true, tenderStage: true, state: 0, stateText: "موجود نیست", rowVersion: "r3" },
    { id: 104, seq: 4, documentType: "BOM", discipline: "کیفیت", title: "فهرست مواد", required: true, tenderStage: false, state: 1, stateText: "موجود", systemProven: true, rowVersion: "r4" }] };
const refList = { id: 2, code: "API 611", title: "فهرست مرجع API 611", standardSource: "API 611 چاپ ششم", revision: 1, status: 0, statusText: "پیش‌نویس", isCurrent: false, preparedByName: "مهندس طراح", note: "پیش‌نویس بررسی‌نشده", lineCount: 3, requiredCount: 2, tenderStageCount: 1, rowVersion: "EEEE",
  ...flags(["Edit", "Submit", "Approve", "Return", "Revise", "Void"]), lines: [{ id: 1, seq: 1, documentType: "DWG", discipline: "مکانیک", title: "نقشه مونتاژ کلی", required: true, tenderStage: true, clientSubmittal: true, standardClause: "5.1", units: "" },
    { id: 2, seq: 2, documentType: "CALC", discipline: "مکانیک", title: "محاسبه ارتعاش پیچشی", required: true, tenderStage: false, clientSubmittal: false, standardClause: "6.2", units: "Production,Qc" }, { id: 3, seq: 3, documentType: "BOM", discipline: "تولید", title: "فهرست مواد", required: false, tenderStage: false, clientSubmittal: false, standardClause: "", units: "" }] };
const waiver = { id: 4, number: "WVR-1405-0004", handoverId: 21, handoverNumber: "HND-1405-0021", caseId: 12, caseNumber: "ENG-1405-0012", reason: "تحویل توربین به مشتری دو روز دیگر است و دو مدرک الزامی پیش از آن کامل نمی‌شود؛ مدارک تا ۴۸ ساعت آینده تحویل می‌شود.",
  missingLines: [{ docType: "CALC", title: "محاسبه ارتعاش پیچشی" }, { docType: "RPT", title: "گزارش تست عملکرد" }], missingSnapshot: ["CALC|محاسبه ارتعاش پیچشی", "RPT|گزارش تست عملکرد"], dossierUnconfirmed: false, debtHours: 48, debtDueAt: null, debtDueAtText: null, status: 0, statusText: "درخواست‌شده", requestedByName: "مدیر مهندسی", requestedAtText: "1405/08/25 08:30", waitingHours: 53,
  used: false, covers: true, coverageNote: null, rowVersion: "WWWW", ...flags(["Decide"]) };
const handover = { id: 21, number: "HND-1405-0021", caseId: 12, caseNumber: "ENG-1405-0012", contractId: 3, contractCode: "CN-1405-003", classification: 1, classificationText: "پلتفرم (توربین ساخته‌شده)", round: 1, status: 0, statusText: "پیش‌نویس", preparedByName: "ایجنت مهندسی", preparedByAgent: true,
  complete: false, missingRequiredCount: 2, requiredCount: 6, platformDossierConfirmed: true, lockMessage: "ابلاغ به اجرا قفل است: ۲ مدرک الزامی هنوز موجود نیست. پس از پذیرش PMO، مدیر مهندسی مجوز اضطرار می‌خواهد و فقط مدیرعامل تصمیم می‌گیرد.", incompleteNotice: "این بسته ناقص است: ۲ مدرک الزامی هنوز موجود نیست. رفت‌وبرگشت با PMO ادامه دارد؛ ابلاغ به اجرا فقط با مدارک کامل یا مجوز اضطرار مدیرعامل ممکن است.", waiverId: null, rowVersion: "HHHH",
  ...flags(["Refresh", "Submit", "RequestWaiver", "Accept", "Return", "Release", "Void"]), requestWaiverBlockedReason: "مجوز اضطرار فقط پس از پایان رفت‌وبرگشت مدارک با PMO (پذیرش PMO) درخواست می‌شود؛ در طول رفت‌وبرگشت از مدیرعامل تاییدی گرفته نمی‌شود.", waiver: null, debts: [],
  canDraftDistribution: false, draftDistributionBlockedReason: "ردیف‌های توزیع فقط برای تحویل پذیرفته‌شده ساخته می‌شود.", canDistribute: false, distributeBlockedReason: "فقط تحویل ابلاغ‌شده توزیع می‌شود.",
  lines: [{ id: 1, seq: 1, documentType: "DWG", discipline: "مکانیک", title: "نقشه مونتاژ کلی", required: true, state: 1, stateText: "موجود", documentId: "11111111-1111-1111-1111-111111111111", revision: "B", units: "", forMyUnit: false },
    { id: 2, seq: 2, documentType: "CALC", discipline: "مکانیک", title: "محاسبه ارتعاش پیچشی", required: true, state: 0, stateText: "موجود نیست", units: "Production", forMyUnit: false },
    { id: 3, seq: 3, documentType: "BOM", discipline: "تولید", title: "فهرست مواد", required: true, state: 1, stateText: "موجود", linkPath: "/product-bank/boms", units: "Production,Procurement", forMyUnit: false }],
  distribution: [] };
const handoverReleased = { ...handover, canDraftDistribution: false, draftDistributionBlockedReason: "ردیف‌های توزیع این تحویل قبلاً ساخته شده است.", canDistribute: false, distributeBlockedReason: "شما مجوز این کار را ندارید.", status: 4, statusText: "منتشرشده", complete: true, missingRequiredCount: 0, lockMessage: null, waiverId: 4, waiverNumber: "WVR-1405-0004", waiver: { ...waiver, status: 1, statusText: "تاییدشده", debtHours: 36, debtDueAt: "2026-11-17T10:00:00+03:30", debtDueAtText: "1405/08/27 10:00", decidedByName: "مدیرعامل", decidedAtText: "1405/08/25 22:00", decisionNote: "مهلت از ۴۸ به ۳۶ ساعت کم شد.", canDecide: false },
  ...flags(["Submit", "RequestWaiver", "Accept", "Return", "Release", "Void"]), debts: [{ id: 1, handoverLineId: 2, productId: 7, referenceCode: "API 611", documentType: "CALC", title: "محاسبه ارتعاش پیچشی", dueAt: "2026-11-17T10:00:00+03:30", status: 0, statusText: "باز", overdue: false }, { id: 2, handoverLineId: 3, productId: 7, referenceCode: "API 611", documentType: "RPT", title: "گزارش تست عملکرد", dueAt: "2026-11-14T10:00:00+03:30", status: 0, statusText: "باز", overdue: true },
    { id: 3, handoverLineId: null, productId: 7, referenceCode: "API 611", documentType: "DOSSIER-CONFIRM", title: "تایید کامل بودن پرونده پلتفرم", dueAt: "2026-11-17T10:00:00+03:30", status: 0, statusText: "باز", overdue: false }],
  distribution: [{ id: 1, handoverId: 21, unit: 3, unitText: "تولید", status: 1, statusText: "ابلاغ‌شده", scopeNote: "ساخت محفظه یاتاقان جدید", documentCount: 3, canEditScope: false, editScopeBlockedReason: "پس از انتشار یادداشت دامنه ویرایش نمی‌شود.", acknowledged: false, rowVersion: "d1", canAcknowledge: true },
    { id: 2, handoverId: 21, unit: 2, unitText: "کنترل کیفیت", status: 1, statusText: "ابلاغ‌شده", scopeNote: null, documentCount: 2, canEditScope: false, acknowledged: true, acknowledgedByName: "بازرس نمونه", acknowledgedAtText: "1405/08/27", rowVersion: "d2", canAcknowledge: false, acknowledgeBlockedReason: "این واحد قبلاً دریافت را تایید کرده است." }] };
const deviation = { id: 6, number: "DEV-1405-0006", kind: 1, kindText: "کاربرد ندارد", targetType: "EnPlatformDocument", targetId: 103, caseId: null, productId: 7, productCode: "ST-120", standardCode: "API 611", standardClause: "7.3", requirement: "گزارش تست عملکرد (RPT)",
  instruction: "این توربین در کارگاه تست نمی‌شود؛ تست در محل نصب انجام می‌شود.", requestedByName: "مهندس طراح", requestedAtText: "1405/08/24 11:00", managerApprovedByName: "مدیر مهندسی", managerApprovedAtText: "1405/08/25 09:00", status: 1, statusText: "تایید مدیر مهندسی", waitingHours: 30, rowVersion: "VVVV", ...flags(["Decide"]) };
const registration = { id: 2, number: "REG-1405-0002", caseId: 12, caseNumber: "ENG-1405-0012", productId: 7, productCode: "ST-120", referenceCode: "API 611", standardBasis: "دیتاشیت پیوست ۳", rationale: "این طراحی جدید برای پروژه‌های بعدی هم قابل استفاده است.", status: 0, statusText: "درخواست‌شده", proposedByName: "مدیر مهندسی", proposedAtText: "1405/08/26", waitingHours: 20, rowVersion: "GGGG", ...flags(["Decide", "Withdraw"]) };
const info = { id: 8, number: "INF-1405-0008", handoverId: 21, handoverNumber: "HND-1405-0021", caseId: 12, caseNumber: "ENG-1405-0012", contractCode: "ENGZ-K", contractTitle: "ساخت توربین", unit: 3, unitText: "تولید", question: "ترتیب مونتاژ محفظه یاتاقان در نقشه مشخص نیست.", askedByName: "سرپرست کارگاه", askedAtText: "1405/08/27", status: 0, statusText: "باز", answerHint: "پاسخ نباید شامل قیمت باشد.", rowVersion: "IIII", ...flags(["Answer", "Close", "Void"]) };
const debts = [{ id: 1, waiverId: 4, waiverNumber: "WVR-1405-0004", handoverLineId: 2, caseId: 12, caseNumber: "ENG-1405-0012", productId: 7, referenceCode: "API 611", documentType: "CALC", title: "محاسبه ارتعاش پیچشی", dueAt: "2026-11-17T10:00:00+03:30", status: 0, statusText: "باز", overdue: false, overdueDays: 0 },
  { id: 2, waiverId: 4, waiverNumber: "WVR-1405-0004", handoverLineId: 3, caseId: 12, caseNumber: "ENG-1405-0012", productId: 7, referenceCode: "API 611", documentType: "RPT", title: "گزارش تست عملکرد", dueAt: "2026-11-14T10:00:00+03:30", status: 0, statusText: "باز", overdue: true, overdueDays: 2 },
  { id: 3, waiverId: 4, waiverNumber: "WVR-1405-0004", handoverLineId: null, caseId: 12, caseNumber: "ENG-1405-0012", productId: 7, referenceCode: "API 611", documentType: "DOSSIER-CONFIRM", title: "تایید کامل بودن پرونده پلتفرم", dueAt: "2026-11-17T10:00:00+03:30", status: 0, statusText: "باز", overdue: false, overdueDays: 0 },
  { id: 4, waiverId: 3, waiverNumber: "WVR-1405-0003", handoverLineId: 5, caseId: 11, caseNumber: "ENG-1405-0011", productId: 8, referenceCode: "API 612", documentType: "BOM", title: "فهرست مواد", dueAt: "2026-11-10T10:00:00+03:30", status: 2, statusText: "ابطال‌شده", voidReason: "تحویل توسط PMO برگشت خورد: فهرست مواد ناقص است.", overdue: false, overdueDays: 0 }];
const ceoDash = { enPlatformsIncomplete: 4, enPlatformsTotal: 13, enOpenDebts: 5, enOverdueDebts: 2, enWaiversAwaitingCeo: 2, enOldestWaiverWaitingHours: 53, enCasesAtDocumentRisk: 3, enAwardedCasesAtDocumentRisk: 1, enPlatformsAlarm: true, enDeviationsAwaiting: 2, enStandardsDeviationsOpen: 3, enStandardGapsOpen: 1, enPlatformRegistrationsAwaiting: 1 };

/** The scenario state: a shot can override answers (by a path prefix) and make a POST refuse. */
let over = {};
function answer(path, method, body) {
  for (const [k, v] of Object.entries(over)) if (path === k || path.startsWith(k + "?")) return typeof v === "function" ? v(body, method) : v;
  if (method === "POST") return { __ok: {} };
  const m = (re) => path.match(re);
  if (path === "engineering/overview") return overviewAlarm;
  if (path === "engineering/cases") return page([caseBase, { ...caseBase, id: 13, number: "ENG-1405-0013", title: "قطعه یدکی پمپ", kind: 2, kindText: "قطعه یدکی", status: 4, statusText: "تهیه بسته فنی مناقصه", classificationText: "پلتفرم (توربین ساخته‌شده)", platformIncomplete: false, documentRisk: "۲ مدرک الزامی کم است و ۴ روز به موعد مانده است.", documentRiskAwarded: false }, { ...caseBase, id: 14, number: "ENG-1405-0014", title: "توربین بخار پروژه برنده", status: 7, statusText: "در مهندسی", documentRisk: "۳ مدرک الزامی کم است.", documentRiskAwarded: true }]);
  if (m(/^engineering\/cases\/\d+$/)) return caseBase;
  if (m(/charter-view$/)) return { caseId: 12, caseNumber: caseBase.number, charterExists: true, overview: "کلیات پروژه نمونه", scopeDescription: "تامین و نصب توربین", requirements: "مطابق دیتاشیت", priceNotice: "قیمت‌ها در این نما نیست." };
  if (path === "engineering/clarifications") return page([clar]);
  if (m(/^engineering\/clarifications\/\d+$/)) return clar;
  if (path === "engineering/handovers") return page([handover]);
  if (m(/^engineering\/handovers\/\d+$/)) return handover;
  if (path === "engineering/platform-registrations") return page([]);
  if (m(/^engineering\/standard-gaps\/\d+$/)) return gapDto;
  if (path === "engineering/standard-gaps") return page([{ ...gapDto, lines: [] }]);
  if (path === "engineering/platforms") return page([{ productId: 7, productCode: "ST-120", productTitle: "توربین بخار ۱۲۰", dossierId: 9, referenceCode: "API 611", status: 1, statusText: "ناقص", requiredCount: 4, availableCount: 2, missingCount: 2, tenderStageMissingCount: 1, stale: false },
    { productId: 7, productCode: "ST-120", productTitle: "توربین بخار ۱۲۰", dossierId: 10, referenceCode: "API 612", status: 3, statusText: "کامل", requiredCount: 5, availableCount: 5, missingCount: 0, tenderStageMissingCount: 0, stale: false },
    { productId: 8, productCode: "ST-200", productTitle: "توربین بخار ۲۰۰", dossierId: null, referenceCode: null, nativeStandardMissing: true, status: 0, statusText: "ارزیابی‌نشده", requiredCount: 0, availableCount: 0, missingCount: 0, tenderStageMissingCount: 0, stale: false }]);
  if (path === "engineering/platforms/summary") return { products: 13, notAssessed: 3, incomplete: 4, completing: 1, complete: 5, stale: 0, hasApprovedReference: true, referenceRevision: 1 };
  if (m(/^engineering\/platforms\/\d+/)) return dossier;
  if (path === "engineering/deviations") return page([deviation]);
  if (path === "engineering/reference/lists") return page([{ ...refList, lines: [] }, { ...refList, id: 3, code: "API 612", title: "فهرست مرجع API 612", status: 2, statusText: "تاییدشده", isCurrent: true }]);
  if (path === "engineering/reference/approved-codes") return [{ code: "API 612", title: "فهرست مرجع API 612" }, { code: "API 611", title: "فهرست مرجع API 611" }];
  if (m(/^engineering\/reference\/lists\/\d+$/)) return refList;
  if (path === "engineering/waivers") return page([waiver]);
  if (path === "engineering/debts") return page(debts);
  if (path === "engineering/info-requests") return page([info]);
  if (path === "finance/ceo-dashboard") return ceoDash;
  if (path === "ceo/overview") { try { return JSON.parse(readFileSync(join(root, "src/fixtures/ceo/overview.json"), "utf8")); } catch { return {}; } }
  return {};
}

// ---------------------------------------------------------------- shots
const shots = [
  { name: "01-overview-empty", path: "/engineering", role: "ENGINEERING", over: { "engineering/overview": overviewEmpty } },
  { name: "02-overview-alarm", path: "/engineering", role: "CEO" },
  { name: "03-cases-list", path: "/engineering/cases", role: "ENGINEERING" },
  { name: "04-cases-empty", path: "/engineering/cases", role: "PMO", over: { "engineering/cases": page([]) } },
  { name: "05-case-awaiting-manager", path: "/engineering/cases/12", role: "ENGINEERING", over: { "engineering/cases/12": { ...caseBase, canConfirm: true, confirmBlockedReason: null, canReturn: true, returnBlockedReason: null, canSetStandard: true, setStandardBlockedReason: null } } },
  { name: "06-case-awaiting-ceo", path: "/engineering/cases/12", role: "CEO", over: { "engineering/cases/12": { ...caseBase, documentRisk: "۲ مدرک الزامی تحویل کم است و ۴ روز به موعد مانده است.", documentRiskAwarded: true, status: 3, statusText: "منتظر تایید مدیرعامل", engConfirmedByName: "مدیر مهندسی", engConfirmedAtText: "1405/08/22", canApprove: true, approveBlockedReason: null, canReturn: true, returnBlockedReason: null, confirmBlockedReason: "تایید مدیر مهندسی انجام شده است." } } },
  { name: "07-case-disabled-with-reason", path: "/engineering/cases/12", role: "ENGINEERING", over: { "engineering/cases/12": { ...caseBase, confirmBlockedReason: "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف: سازنده یا ویرایشگر تأییدکننده نیست)." } } },
  { name: "08-case-spare-parts", path: "/engineering/cases/13", role: "ENGINEERING", over: { "engineering/cases/13": { ...caseBase, id: 13, kind: 2, kindText: "قطعه یدکی", status: 1, statusText: "در حال تعیین نوع", opinionText: null, standardGap: null, platformIncomplete: false, canSaveOpinion: true, saveOpinionBlockedReason: null } } },
  { name: "09-platforms-list", path: "/engineering/platforms", role: "ENGINEERING" },
  { name: "10-platform-detail-deviation-requested", path: "/engineering/platforms/7?code=API%20611", role: "ENGINEERING", over: { "engineering/deviations": page([deviation]), "engineering/platforms/7": { ...dossier, canSupply: true, supplyBlockedReason: null } } },
  { name: "11-reference-list-draft", path: "/engineering/reference-lists/2", role: "ENGINEERING", over: { "engineering/reference/lists/2": { ...refList, canEdit: true, editBlockedReason: null, canSubmit: true, submitBlockedReason: null } } },
  { name: "12-reference-lists", path: "/engineering/reference-lists", role: "CEO" },
  { name: "13-handover-lock-b", path: "/engineering/handovers/21", role: "ENGINEERING", over: { "engineering/handovers/21": { ...handover, canRequestWaiver: true, requestWaiverBlockedReason: null } } },
  { name: "14-handover-waiver-waiting-ceo", path: "/engineering/handovers/21", role: "CEO", over: { "engineering/handovers/21": { ...handover, status: 0, waiverId: 4, waiver, lockMessage: handover.lockMessage } } },
  { name: "15-handover-released-distribution", path: "/engineering/handovers/21", role: "QC", over: { "engineering/handovers/21": handoverReleased, "engineering/info-requests": page([info]) } },
  { name: "16-waiver-dialog-refused", path: "/engineering/handovers/21", role: "ENGINEERING", over: { "engineering/handovers/21": { ...handover, canRequestWaiver: true, requestWaiverBlockedReason: null }, "engineering/waivers/request": () => refuse("مهلت تأمین مدرک از ۷۲ ساعت بیشتر نیست.") },
    act: async (p) => { await p.click("text=درخواست مجوز اضطرار"); await p.fill("textarea", "تحویل فردا است و مدارک رسیدن ندارد؛ مدارک تا دو روز دیگر می‌رسد."); await p.fill("[data-test=en-hours]", "72"); await p.click(".inv-modal >> text=تایید"); await p.waitForTimeout(500); } },
  { name: "17-debts", path: "/engineering/debts", role: "PMO" },
  { name: "18-waivers", path: "/engineering/waivers", role: "CEO", over: { "engineering/waivers": page([{ ...waiver, canDecide: true, decideBlockedReason: null }]) } },
  { name: "19-deviations", path: "/engineering/deviations", role: "CEO", over: { "engineering/deviations": page([{ ...deviation, canDecide: true, decideBlockedReason: null }]) } },
  { name: "20-standard-gaps", path: "/engineering/standard-gaps/3", role: "ENGINEERING", over: { "engineering/standard-gaps/3": { ...gapDto, canConfirm: true, confirmBlockedReason: null } } },
  { name: "21-registrations", path: "/engineering/platform-registrations", role: "CEO", over: { "engineering/platform-registrations": page([{ ...registration, canDecide: true, decideBlockedReason: null }]) } },
  { name: "22-info-requests-pmo", path: "/engineering/info-requests", role: "PMO", over: { "engineering/info-requests": page([{ ...info, canAnswer: true, answerBlockedReason: null }]) } },
  { name: "23-ceo-home-engineering-tiles", path: "/ceo", role: "CEO", fullPage: false },
  { name: "25-ceo-home-alarm-band", path: "/ceo", role: "CEO", fullPage: false },
  { name: "26-waiver-request-dialog-hours", path: "/engineering/handovers/21", role: "ENGINEERING", over: { "engineering/handovers/21": { ...handover, canRequestWaiver: true, requestWaiverBlockedReason: null } },
    act: async (p) => { await p.click("text=درخواست مجوز اضطرار"); await p.fill("textarea", "تحویل فردا است و مدارک رسیدن ندارد؛ مدارک تا دو روز دیگر می‌رسد."); await p.fill("[data-test=en-hours]", "48"); await p.waitForTimeout(200); } },
  { name: "27-spare-parts-confirm-dialog", path: "/engineering/cases/13", role: "ENGINEERING", over: { "engineering/cases/13": { ...caseBase, id: 13, kind: 2, kindText: "قطعه یدکی", status: 1, statusText: "در حال تعیین نوع", opinionText: "نظر فنی کامل برای قطعه یدکی پمپ با شرح مواد و استاندارد", standardGap: null, platformIncomplete: false, canConfirm: true, confirmBlockedReason: null } },
    act: async (p) => { await p.click("button:has-text('تایید مدیر مهندسی')"); await p.waitForTimeout(400); } },
  { name: "28-reference-list-upload-dialog", path: "/engineering/reference-lists/2", role: "ENGINEERING", over: { "engineering/reference/lists/2": { ...refList, canEdit: true, editBlockedReason: null, canSubmit: true, submitBlockedReason: null } },
    act: async (p) => { await p.click("[data-test=en-ref-upload]"); await p.waitForTimeout(300); } },
  { name: "29-waiver-ceo-lower-hours-dialog", path: "/engineering/waivers", role: "CEO", over: { "engineering/waivers": page([{ ...waiver, canDecide: true, decideBlockedReason: null }]) },
    act: async (p) => { await p.click("button:has-text('تایید مجوز اضطرار')"); await p.fill("[data-test=en-hours]", "36"); await p.waitForTimeout(300); } },
  { name: "30-waiver-by-id-not-on-first-page", path: "/engineering/waivers/44", role: "CEO", over: { "engineering/waivers/44": { ...waiver, id: 44, number: "WVR-1405-0044", canDecide: true, decideBlockedReason: null } } },
  { name: "31-waiver-approved-deadline", path: "/engineering/waivers", role: "ENGINEERING", over: { "engineering/waivers": page([{ ...handoverReleased.waiver, canDecide: false, decideBlockedReason: "فقط مدیرعامل تصمیم می‌گیرد." }]) } },
  { name: "32-case-tender-documents-risk-add", path: "/engineering/cases/12", role: "ENGINEERING", over: { "engineering/cases/12": { ...caseBase, status: 4, statusText: "تهیه بسته فنی مناقصه", classification: 1, classificationText: "پلتفرم (توربین ساخته‌شده)", productId: 7, productCode: "ST-120", tenderPackageCode: "TP-1405-0012", tenderDocumentCount: 2,
      documentRisk: "۲ مدرک الزامی کم است و ۴ روز به موعد مانده است.", documentRiskAwarded: false, conceptNote: "طرح مفهومی بر پایه پلتفرم ST-120",
      tenderDocuments: [{ documentId: "22222222-2222-2222-2222-222222222222", title: "طرح مفهومی محفظه یاتاقان", revision: "A", issuedAtText: "1405/08/24", addedByAgent: false }, { documentId: "33333333-3333-3333-3333-333333333333", title: "برگه اطلاعات توربین", revision: "B", issuedAtText: "1405/08/22", addedByAgent: true }],
      canAddTenderDocument: true, addTenderDocumentBlockedReason: null, canProposeRegistration: false } } },
  { name: "33-case-awarded-risk-red-add-disabled", path: "/engineering/cases/12", role: "ENGINEERING", over: { "engineering/cases/12": { ...caseBase, status: 7, statusText: "در مهندسی", documentRisk: "۳ مدرک الزامی تحویل کم است و ۲ روز به موعد مانده است.", documentRiskAwarded: true, canAskClarification: false, canCancel: false, cancelBlockedReason: "تا زمانی که قرارداد جاری است پرونده لغو نمی‌شود.", proposeRegistrationBlockedReason: "برای پیشنهاد ثبت، تحویل مدارک باید منتشر شده باشد." } } },
  { name: "34-handover-pmo-distribution-draft-button", path: "/engineering/handovers/21", role: "PMO", over: { "engineering/handovers/21": { ...handoverReleased, status: 3, statusText: "پذیرفته‌شده", waiverId: null, waiver: null, debts: [], distribution: [], canRelease: true, releaseBlockedReason: null, canDraftDistribution: true, draftDistributionBlockedReason: null, canDistribute: false, distributeBlockedReason: "فقط تحویل ابلاغ‌شده توزیع می‌شود." } } },
  { name: "35-handover-pmo-distribute-button", path: "/engineering/handovers/21", role: "PMO", over: { "engineering/handovers/21": { ...handoverReleased, canDistribute: true, distributeBlockedReason: null, distribution: handoverReleased.distribution.map((x) => ({ ...x, status: 0, statusText: "پیش‌نویس (منتظر ابلاغ PMO)", canEditScope: true, editScopeBlockedReason: null, canAcknowledge: false, acknowledgeBlockedReason: "ابلاغ این واحد هنوز ارسال نشده است." })) } } },
  { name: "36-handover-engineering-no-distribution-buttons", path: "/engineering/handovers/21", role: "ENGINEERING", over: { "engineering/handovers/21": { ...handoverReleased, canRefresh: false, refreshBlockedReason: "تحویل پیش‌نویس نیست.", distribution: [] } } },
  { name: "37-confirm-dialog-before-accept", path: "/engineering/handovers/21", role: "PMO", over: { "engineering/handovers/21": { ...handover, status: 1, statusText: "ارسال‌شده", canAccept: true, acceptBlockedReason: null } },
    act: async (p) => { await p.click("button:has-text('پذیرش')"); await p.waitForTimeout(300); } },
  { name: "38-case-engineering-asks-pmo", path: "/engineering/cases/12", role: "ENGINEERING", over: { "engineering/cases/12": { ...caseBase, contractId: 3, contractCode: "CN-1405-003" }, "engineering/info-requests": page([{ ...info, unit: 7, unitText: "مهندسی", handoverId: null, handoverNumber: null, askedByName: "مهندس طراح", question: "کدام ویرایش دیتاشیت کارفرما ملاک طراحی نهایی است؟", canAnswer: false, canClose: false, canVoid: true }]) } },
  { name: "24-forbidden-info-requests-for-sales", path: "/engineering/info-requests", role: "SALES" },
];
const wanted = process.argv.slice(2);
const list = wanted.length ? shots.filter((s) => wanted.some((w) => s.name.includes(w))) : shots;
const executablePath = existsSync("/usr/bin/google-chrome") ? "/usr/bin/google-chrome" : undefined;
const browser = await chromium.launch({ executablePath, args: ["--no-sandbox"] });
const problems = [];
for (const s of list) {
  over = s.over || {};
  const context = await browser.newContext({ viewport: { width: 1360, height: 900 }, locale: "fa-IR", timezoneId: "Asia/Tehran" });
  await context.addInitScript(([role]) => {
    localStorage.setItem("token", "preview.token.value");
    localStorage.setItem("refreshToken", "preview");
    localStorage.setItem("tokenExpireDate", "4102444800");
    localStorage.setItem("userRole", role);
    localStorage.setItem("accessList", "");
  }, [s.role]);
  const pg = await context.newPage();
  const errors = [];
  pg.on("pageerror", (e) => errors.push("JS: " + e.message));
  pg.on("response", (r) => { if (r.url().includes("/api/v1/") && r.status() >= 400) errors.push(`HTTP ${r.status()} ${r.url()}`); });
  await pg.route("**/api/v1/**", (route) => {
    const req = route.request();
    const path = new URL(req.url()).pathname.replace(/^.*\/api\/v1\//, "");
    let body = null;
    try { body = JSON.parse(req.postData() || "null"); } catch {}
    const a = answer(path, req.method(), body);
    const text = typeof a === "string" ? a : a && a.__ok ? ok(a.__ok) : ok(a);
    route.fulfill({ status: 200, contentType: "application/json", body: text });
  });
  await pg.goto(base + s.path, { waitUntil: "networkidle" }).catch(() => {});
  await pg.waitForTimeout(700);
  if (s.act) await s.act(pg).catch((e) => errors.push("ACT: " + e.message));
  await pg.screenshot({ path: join(out, s.name + ".png"), fullPage: s.fullPage !== false });
  const url = pg.url().replace(base, "");
  console.log(`${s.name}  ->  ${url}${errors.length ? "  PROBLEMS: " + errors.join(" | ") : ""}`);
  if (errors.length) problems.push(s.name);
  await context.close();
}
await browser.close();
server.close();
console.log(problems.length ? "shots with problems: " + problems.join(", ") : "all shots without JS or HTTP errors");
