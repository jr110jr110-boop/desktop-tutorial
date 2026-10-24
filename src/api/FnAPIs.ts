import { call, clean, http, saveBlob } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Accounting module F1 + F2 (api/v1/finance/gl, treasury, receivables, tax, dashboard, assets, payroll, sepidar, employees).
// Every answer is { success, message, data }. The reports are read through camelize(): some report endpoints answer with PascalCase keys.
const gl = "finance/gl/";
const tr = "finance/treasury/";
const rc = "finance/receivables/";
const tx = "finance/tax/";
const as = "finance/assets/";
const pr = "finance/payroll/";
const sp = "finance/sepidar/";

type Method = "get" | "post" | "put" | "delete";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

/** Downloads a file answered by GET (CSV / Excel). Returns an error message, or null when the file was saved. */
async function download(url: string, params: any, defaultName: string): Promise<string | null> {
  try {
    const response = await http.get(url, { params: clean(params), responseType: "blob" });
    const type = response.headers["content-type"] || "";
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return json.message || "خطا";
    }
    const disposition = response.headers["content-disposition"] || "";
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    saveBlob(response.data, match ? decodeURIComponent(match[1]) : defaultName);
    return null;
  } catch (error: any) {
    if (error?.response?.status === 403) return "شما اجازه این کار را ندارید.";
    return error?.message || "خطا";
  }
}

// ---------------------------------------------------------------- general ledger
export const getFiscalYears = (legalEntityId?: number | null) => get(gl + "years", { legalEntityId });
export const ensureFiscalYear = (date: string) => post(gl + "years/ensure", { date });
/** action: lock, reopen (reason required), close */
export const periodAction = (id: number, action: string, reason?: string) => post(gl + "periods/action", { id, action, reason: reason || null });
export const closeFiscalYear = (id: number, overrideReconciliation: boolean, reason?: string) =>
  post(gl + `years/${id}/close`, { overrideReconciliation, reason: reason || null });

export const getAccounts = (params: any) => get(gl + "accounts", params);
export const saveAccount = (dto: any) => post(gl + "accounts", dto);
export const deleteAccount = (id: number) => fcall("delete", gl + "accounts/" + id);
export const getDetailAccounts = (params: any) => get(gl + "details", params);
export const saveDetailAccount = (dto: any) => post(gl + "details", dto);

export const getVouchers = (params: any) => get(gl + "vouchers", params);
export const getVoucher = (id: number) => get(gl + "vouchers/" + id);
export const saveVoucher = (dto: any) => post(gl + "vouchers", dto);
export const deleteVoucher = (id: number) => fcall("delete", gl + "vouchers/" + id);
/** action: check, uncheck, finalize, reverse (date, note), ceo-approve */
export const voucherAction = (id: number, action: string, extra: { date?: string | null; note?: string } = {}) =>
  post(gl + "vouchers/action", { id, action, date: extra.date || null, note: extra.note || null });
export const finalizeUpTo = (upTo: string, fiscalYearId?: number | null) => post(gl + "vouchers/finalize-up-to", { upTo, fiscalYearId: fiscalYearId || null });
export const postAccountingEvents = () => post(gl + "post-events");
export const importOpening = (dto: any) => post(gl + "opening/import", dto);
export const getSetupCheck = () => get(gl + "setup-check");

export type LedgerReport = "journal" | "general-ledger" | "trial-balance" | "account-review" | "balance-sheet" | "income-statement" | "project-profitability";
export const getLedgerReport = (report: LedgerReport, params: any) => get(gl + "reports/" + report, params);
export const getLedgerReconciliation = (oldVoucherDays = 30) => get(gl + "reports/reconciliation", { oldVoucherDays });
export const exportLedgerReport = (report: string, params: any) => download(gl + "reports/export/" + report, params, report + ".csv");

// ---------------------------------------------------------------- treasury
export const getBankAccounts = (params: any) => get(tr + "bank-accounts", params);
export const saveBankAccount = (dto: any) => post(tr + "bank-accounts", dto);
export const getCashBoxes = (params: any) => get(tr + "cash-boxes", params);
export const saveCashBox = (dto: any) => post(tr + "cash-boxes", dto);
export const getPettyCashes = (params: any) => get(tr + "petty-cash", params);
export const savePettyCash = (dto: any) => post(tr + "petty-cash", dto);
export const getPettyCandidates = (id: number, params: any) => get(tr + `petty-cash/${id}/candidates`, params);
export const getSettlements = (params: any) => get(tr + "settlements", params);
export const saveSettlement = (dto: any) => post(tr + "settlements", dto);
/** action: approve, cancel */
export const docAction = (path: string, dto: { id: number; action: string; note?: string; date?: string | null; bankAccountId?: number | null; providerId?: number | null; reference?: string }) =>
  post(path, {
    id: dto.id, action: dto.action, note: dto.note || null, date: dto.date || null, bankAccountId: dto.bankAccountId || null, providerId: dto.providerId || null, reference: dto.reference || null,
  });
export const settlementAction = (dto: any) => docAction(tr + "settlements/action", dto);

export const getPayableRequests = (params: any) => get(tr + "payable-requests", params);
export const getPayments = (params: any) => get(tr + "payments", params);
export const getPayment = (id: number) => get(tr + "payments/" + id);
export const savePayment = (dto: any) => post(tr + "payments", dto);
/** action: approve (finance manager), post (reference = bank tracking number), cancel */
export const paymentAction = (dto: any) => docAction(tr + "payments/action", dto);

export const getReceipts = (params: any) => get(tr + "receipts", params);
export const getReceipt = (id: number) => get(tr + "receipts/" + id);
export const saveReceipt = (dto: any) => post(tr + "receipts", dto);
export const receiptAction = (dto: any) => docAction(tr + "receipts/action", dto);

export const getReceivedCheques = (params: any) => get(tr + "received-cheques", params);
/** action: deposit (bankAccountId), clear, bounce, return, endorse (providerId) */
export const receivedChequeAction = (dto: any) => docAction(tr + "received-cheques/action", dto);
export const getChequeBooks = (params: any) => get(tr + "cheque-books", params);
export const saveChequeBook = (dto: any) => post(tr + "cheque-books", dto);
export const getIssuedCheques = (params: any) => get(tr + "issued-cheques", params);
/** action: clear, cancel (note), void (note) */
export const issuedChequeAction = (dto: any) => docAction(tr + "issued-cheques/action", dto);

export const getGuarantees = (params: any) => get(tr + "guarantees", params);
export const saveGuarantee = (dto: any) => post(tr + "guarantees", dto);
/** action: return, collect */
export const guaranteeAction = (dto: any) => docAction(tr + "guarantees/action", dto);

export const importStatement = (dto: any) => post(tr + "statements/import", dto);
export const getStatements = (params: any) => get(tr + "statements", params);
export const getStatement = (id: number) => get(tr + "statements/" + id);
export const autoMatchStatement = (id: number) => post(tr + `statements/${id}/auto-match`);
/** kind: cheque, payment, receipt, fee, interest */
export const matchStatementLine = (statementId: number, lineId: number, kind: string, targetId?: number | null) =>
  post(tr + "statements/match", { statementId, lineId, kind, targetId: targetId || null });
export const getReconciliations = (params: any) => get(tr + "reconciliations", params);
export const createReconciliation = (statementId: number, asOfDate?: string | null) => post(tr + "reconciliations", { statementId, asOfDate: asOfDate || null });
/** action: refresh, approve */
export const reconciliationAction = (dto: any) => docAction(tr + "reconciliations/action", dto);
export const getCashPosition = () => get(tr + "cash-position");

// ---------------------------------------------------------------- receivables
export const getCustomers = (params: any) => get(rc + "customers", params);
export const saveCustomer = (dto: any) => post(rc + "customers", dto);
export const getSalesInvoices = (params: any) => get(rc + "invoices", params);
export const getSalesInvoice = (id: number) => get(rc + "invoices/" + id);
export const saveSalesInvoice = (dto: any) => post(rc + "invoices", dto);
/** action: approve, cancel */
export const invoiceAction = (dto: any) => docAction(rc + "invoices/action", dto);
export const draftInvoicesFromMilestones = () => post(rc + "invoices/draft-from-milestones");
export const getAgeing = (asOf?: string | null) => get(rc + "ageing", { asOf });
export const getCustomerStatement = (id: number) => get(rc + `customers/${id}/statement`);

// ---------------------------------------------------------------- tax
export const getTaxRates = (params: any) => get(tx + "rates", params);
export const saveTaxRate = (dto: any) => post(tx + "rates", dto);
export const getVatSeason = (year: number, season: number) => get(tx + "vat-season", { year, season });
export const getTaxpayerSales = (params: any) => get(tx + "taxpayer-sales", params);

// ---------------------------------------------------------------- dashboard
export const getFinanceDashboard = () => get("finance/dashboard");

// ---------------------------------------------------------------- assets
export const getAssetSetupCheck = () => get(as + "setup-check");
export const getAssetClasses = () => get(as + "classes");
export const saveAssetClass = (dto: any) => post(as + "classes", dto);
export const getAssets = (params: any) => get(as, params);
export const getAsset = (id: number) => get(as + id);
export const saveAsset = (dto: any) => post(as, dto);
/** action: approve, commission, idle-from, idle-to, transfer, improve */
export const assetAction = (dto: any) => post(as + "action", dto);
export const getDepreciationRuns = (params: any) => get(as + "depreciation/runs", params);
export const getDepreciationRun = (id: number) => get(as + "depreciation/runs/" + id);
export const prepareDepreciation = (periodId: number) => post(as + "depreciation/prepare/" + periodId);
/** action: approve (finance manager), delete */
export const depreciationAction = (id: number, action: string, note?: string) => post(as + "depreciation/action", { id, action, note: note || null });
export const getDisposals = (params: any) => get(as + "disposals", params);
export const saveDisposal = (dto: any) => post(as + "disposals", dto);
export const disposalAction = (dto: any) => docAction(as + "disposals/action", dto);
export const getDepreciationSchedule = (year: number) => get(as + "reports/depreciation-schedule", { year });
export const getAssetMovement = (year: number) => get(as + "reports/movement", { year });
export const getAssetHandover = (employeeId?: number | null, custodian?: string) => get(as + "reports/handover", { employeeId, custodian });
export const getEmployeeDirectory = (contains?: string) => get("finance/employees/directory", { contains });

// ---------------------------------------------------------------- payroll (confidential)
export const getPayrollSetup = (year: number) => get(pr + "setup", { year });
export const getWorkshops = () => get(pr + "workshops");
export const saveWorkshop = (dto: any) => post(pr + "workshops", dto);
export const getPayrollEmployees = (params: any) => get(pr + "employees", params);
export const getPayrollEmployee = (id: number) => get(pr + "employees/" + id);
export const savePayrollEmployee = (dto: any) => post(pr + "employees", dto);
export const getLoans = (params: any) => get(pr + "loans", params);
export const saveLoan = (dto: any) => post(pr + "loans", dto);
export const getPayrollItems = () => get(pr + "items");
export const savePayrollItem = (dto: any) => post(pr + "items", dto);
export const getPayrollRates = (params: any) => get(pr + "rates", params);
export const savePayrollRate = (dto: any) => post(pr + "rates", dto);
export const getTaxBrackets = (year: number) => get(pr + "tax-brackets", { year });
export const saveTaxBrackets = (year: number, brackets: any[]) => post(pr + "tax-brackets", { year, brackets });
export const getAttendance = (params: any) => get(pr + "attendance", params);
export const saveAttendance = (dto: any) => post(pr + "attendance", dto);
export const importAttendance = (year: number, month: number, rows: any[]) => post(pr + "attendance/import", { year, month, rows });
export const getLeaveBalances = (year: number) => get(pr + "leave-balances", { year });
export const getPayrollRuns = (params: any) => get(pr + "runs", params);
export const getPayrollRun = (id: number) => get(pr + "runs/" + id);
export const createPayrollRun = (year: number, month: number, workshopId?: number | null, note?: string) =>
  post(pr + "runs", { year, month, workshopId: workshopId || null, note: note || null });
/** step: calculate, approve (finance manager), post */
export const payrollRunStep = (id: number, step: "calculate" | "approve" | "post") => post(pr + `runs/${id}/${step}`);
export const deletePayrollRun = (id: number) => fcall("delete", pr + "runs/" + id);
export const getPayslips = (params: any) => get(pr + "payslips", params);
export const getPayslip = (id: number) => get(pr + "payslips/" + id);
export const getInsuranceList = (year: number, month: number, workshopId?: number | null) => get(pr + "reports/insurance-list", { year, month, workshopId });
export const getPayrollTaxList = (year: number, month: number) => get(pr + "reports/tax-list", { year, month });
export const getEmployeeYear = (employeeId: number, year: number) => get(pr + "reports/employee-year", { employeeId, year });
export const getBankTransfer = (id: number) => get(pr + `runs/${id}/bank-transfer`);
export const downloadBankTransferFile = (id: number) => download(pr + `runs/${id}/bank-transfer/file`, {}, `payroll-transfer-${id}.xlsx`);

// ---------------------------------------------------------------- Sepidar outbox
export const getOutboxPackages = (params: any) => get(sp + "packages", params);
export const getOutboxPackage = (id: number) => get(sp + "packages/" + id);
export const generateOutbox = (dto: any) => post(sp + "generate", dto);
/** action: approve, send, mark-sent, mark-accepted, mark-rejected */
export const outboxAction = (id: number, action: string, note?: string, reference?: string) => post(sp + "action", { id, action, note: note || null, reference: reference || null });
export const downloadOutboxPackage = (id: number) => download(sp + `packages/${id}/export`, {}, `sepidar-package-${id}.xlsx`);
export const getOutboxSummary = () => get(sp + "summary");

// ---------------------------------------------------------------- F3: controls, audit, migration, Sepidar export
const ad = "finance/audit";
const ent = "finance/entities";
const mg = "finance/migration/";
const se = "finance/sepidar-export/";
const fxb = "finance/fx/";
const bd = "finance/budgets";
const co = "finance/costing/";
const rr = "finance/receivables/revenue-recognition";
const df = "finance/deferrals";
const ln = "finance/loans";
const st = "finance/statements/";
const tc = "finance/tax/";
const cl = "finance/collections/";

// audit trail (FINANCEMANAGER, CEO, AUDITOR)
export const getAuditTrail = (params: any) => get(ad, params);

// period close checklist, settings, attachments, templates, copy, mass import
export const getCloseChecklist = (periodId: number) => get(gl + "close-checklist/" + periodId);
export const overrideChecklist = (periodId: number, reason: string) => post(gl + `close-checklist/${periodId}/override`, { reason });
export const getFinanceSettings = () => get(gl + "settings");
export const saveFinanceSetting = (key: string, value: string) => post(gl + "settings", { key, value });
export const getVoucherAttachments = (voucherId: number) => get(gl + `vouchers/${voucherId}/attachments`);
export const addVoucherAttachment = (voucherId: number, documentId: string, title: string) => post(gl + "attachments", { voucherId, documentId, title });
export const removeVoucherAttachment = (id: number) => fcall("delete", gl + "attachments/" + id);
export const getAttachmentRules = () => get(gl + "attachment-rules");
export const saveAttachmentRule = (dto: any) => post(gl + "attachment-rules", dto);
export const getApprovalThresholds = () => get(gl + "approval-thresholds");
export const saveApprovalThreshold = (dto: any) => post(gl + "approval-thresholds", dto);
export const copyVoucher = (id: number, date?: string | null) => post(gl + "vouchers/copy", { id, date: date || null });
export const getVoucherTemplates = () => get(gl + "templates");
export const saveVoucherTemplate = (dto: any) => post(gl + "templates", dto);
export const generateVoucherTemplates = (periodId: number) => post(gl + "templates/generate", { periodId });
export const massImportVouchers = (dryRun: boolean, rows: any[], legalEntityId?: number | null) => post(gl + "vouchers/mass-import", { dryRun, legalEntityId: legalEntityId || null, rows });

// legal entities, intercompany, consolidation
export const getLegalEntities = () => get(ent);
export const saveLegalEntity = (dto: any) => post(ent, dto);
export const setDefaultEntity = (legalEntityId: number) => post(ent + "/default", { legalEntityId });
export const getIntercompanyPairs = () => get(ent + "/pairs");
export const saveIntercompanyPair = (dto: any) => post(ent + "/pairs", dto);
export const postIntercompany = (dto: any) => post(ent + "/intercompany/post", dto);
export const getConsolidation = (params: any) => get(ent + "/consolidation", params);

// migration from Sepidar (commit = false is the dry run)
export const importSepidarAccounts = (rows: any[], commit: boolean) => fcall("post", mg + "accounts?commit=" + commit, { rows });
export const importSepidarDetails = (rows: any[], commit: boolean) => fcall("post", mg + "details?commit=" + commit, { rows });
export const importSepidarOpening = (dto: any, commit: boolean) => fcall("post", mg + "opening?commit=" + commit, dto);
export const createImportedYear = (dto: any) => post(mg + "imported-years", dto);
export const importHistoricalVouchers = (dto: any) => post(mg + "vouchers/import", dto);
export const compareParallel = (dto: any) => post(mg + "parallel-run/compare", dto);
export const getParallelRuns = (periodId?: number | null) => get(mg + "parallel-run", { periodId });
export const getParallelStatus = (legalEntityId?: number | null) => get(mg + "parallel-run/status", { legalEntityId });
export const getParallelRun = (id: number) => get(mg + "parallel-run/" + id);
export const explainParallelDifference = (runId: number, differenceId: number, explanation: string) => post(mg + `parallel-run/${runId}/explain`, { differenceId, explanation });
export const signOffParallelRun = (runId: number, note: string) => post(mg + `parallel-run/${runId}/sign-off`, { note });

// full voucher export for Sepidar
export const getSepidarExportStatus = () => get(se + "status");
export const getExportLayout = () => get(se + "layout");
export const saveExportLayout = (columns: any[]) => post(se + "layout", { columns });
export const getExportAccountMapping = (onlyUnmapped: boolean) => get(se + "account-mapping", { onlyUnmapped });
export const saveExportAccountMapping = (code: string, sepidarCode: string) => post(se + "account-mapping", { code, sepidarCode });
export const getExportDetailMapping = (onlyUnmapped: boolean) => get(se + "detail-mapping", { onlyUnmapped });
export const saveExportDetailMapping = (code: string, sepidarCode: string) => post(se + "detail-mapping", { code, sepidarCode });
export const getExportPreview = (periodId: number) => get(se + "preview/" + periodId);
export const exportSepidarPeriod = (periodId: number, supersedeReason?: string) => post(se + "export", { periodId, supersedeReason: supersedeReason || null });
export const getSepidarExports = (periodId?: number | null) => get(se + "exports", { periodId });
export const downloadSepidarExport = (id: number) => download(se + `exports/${id}/file`, {}, `sepidar-vouchers-${id}.csv`);

// ---------------------------------------------------------------- F3: foreign currency
export const getCurrencies = () => get(fxb + "currencies");
export const saveCurrency = (dto: any) => post(fxb + "currencies", dto);
export const getExchangeRates = (params: any) => get(fxb + "rates", params);
export const saveExchangeRate = (dto: any) => post(fxb + "rates", dto);
export const runRevaluation = (dto: any) => post(fxb + "revaluation", dto);
export const getRevaluations = (periodId?: number | null) => get(fxb + "revaluations", { periodId });
export const settleForeign = (dto: any) => post(fxb + "settlement", dto);
export const getFxExposure = (asOf?: string | null) => get(fxb + "exposure", { asOf });
export const getLettersOfCredit = () => get(fxb + "letters-of-credit");
export const getLetterOfCredit = (id: number) => get(fxb + "letters-of-credit/" + id);
export const saveLetterOfCredit = (dto: any) => post(fxb + "letters-of-credit", dto);
/** action: margin, fee, documents, pay, expire, cancel */
export const letterOfCreditAction = (dto: any) => post(fxb + "letters-of-credit/action", dto);

// ---------------------------------------------------------------- F3: budgets
export const getBudgets = (fiscalYearId?: number | null) => get(bd, { fiscalYearId });
export const getBudget = (id: number) => get(bd + "/" + id);
export const saveBudget = (dto: any) => post(bd, dto);
export const importBudgetCsv = (budgetId: number, csv: string) => post(bd + "/import", { budgetId, csv });
export const downloadBudgetCsv = (id: number) => download(bd + `/${id}/export`, {}, `budget-${id}.csv`);
export const approveBudget = (id: number) => post(bd + `/${id}/approve`);
export const reviseBudget = (id: number) => post(bd + `/${id}/revise`);
export const getBudgetReport = (params: any) => get(bd + "/report", params);
export const checkCommitment = (dto: any) => post(bd + "/commitment-check", dto);
export const checkPurchaseRequestCommitment = (id: number, overrideReason?: string) => get(bd + `/commitment-check/purchase-request/${id}`, { overrideReason });

// ---------------------------------------------------------------- F3: costing and revenue recognition
export const getOverheadRates = () => get(co + "overhead-rates");
export const saveOverheadRate = (dto: any) => post(co + "overhead-rates", dto);
export const getAllocationKeys = () => get(co + "allocation-keys");
export const saveAllocationKey = (dto: any) => post(co + "allocation-keys", dto);
export const getWorkOrderBases = (periodId: number) => get(co + "workorder-bases/" + periodId);
export const saveWorkOrderBase = (dto: any) => post(co + "workorder-bases", dto);
export const runAllocation = (periodId: number, dryRun: boolean) => post(co + "allocate/" + periodId, { dryRun });
export const getWorkOrderCostSheet = (id: number) => get(co + `workorders/${id}/cost-sheet`);
export const closeWorkOrder = (id: number, dto: any) => post(co + `workorders/${id}/close`, dto);
export const reopenWorkOrder = (id: number, dto: any) => post(co + `workorders/${id}/reopen`, dto);
export const getStandardCosts = () => get(co + "standard-costs");
export const saveStandardCost = (dto: any) => post(co + "standard-costs", dto);
export const getContractRevenue = () => get(rr + "/contracts");
export const saveContractRevenue = (dto: any) => post(rr + "/contracts", dto);
export const runRevenueRecognition = (periodId: number, dryRun: boolean, inputs: any[]) => post(rr + "/" + periodId, { dryRun, inputs });

// ---------------------------------------------------------------- F3: deferrals, provisions, loans
export const getDeferrals = () => get(df);
export const getDeferral = (id: number) => get(df + "/" + id);
export const saveDeferral = (dto: any) => post(df, dto);
export const runDeferralAmortisation = (periodId: number) => post(df + "/run/" + periodId);
export const approveDeferralEntry = (id: number) => post(df + `/entries/${id}/approve`);
export const cancelDeferral = (id: number) => post(df + `/${id}/cancel`);
export const runReversingAccruals = (periodId: number) => post(df + "/reversing/run/" + periodId);
export const getProvisions = (asOf?: string | null) => get(df + "/provisions", { asOf });
export const saveProvision = (dto: any) => post(df + "/provisions", dto);
export const trueUpProvision = (dto: any) => post(df + "/provisions/true-up", dto);
export const provisionMovement = (dto: any) => post(df + "/provisions/movement", dto);

export const getFacilities = () => get(ln);
export const getFacility = (id: number) => get(ln + "/" + id);
export const saveFacility = (dto: any) => post(ln, dto);
/** action: disburse, cancel */
export const facilityAction = (dto: any) => post(ln + "/action", dto);
export const payInstallment = (dto: any) => post(ln + "/pay", dto);
export const runLoanAccrual = (periodId: number) => post(ln + "/accrual/run/" + periodId);
export const approveLoanAccrual = (id: number) => post(ln + `/accrual/${id}/approve`);
export const saveFacilityLimit = (dto: any) => post(ln + "/limits", dto);
export const getFacilityUtilisation = () => get(ln + "/utilisation");

// ---------------------------------------------------------------- F3: statements, tax books, collections
export const getCashFlow = (params: any) => get(st + "cash-flow", params);
export const getEquityChanges = (params: any) => get(st + "equity-changes", params);
export const getRatios = (params: any) => get(st + "ratios", params);
export const getStatementDrill = (account: string, params: any) => get(st + "drill", { ...params, account });
export const getStatementViews = () => get(st + "views");
export const saveStatementView = (dto: any) => post(st + "views", dto);

export const getVatDetail = (year: number, season: number) => get(tc + "vat-season/detail", { year, season });
export const getTaxAdjustments = (fiscalYearId: number) => get(tc + "adjustments", { fiscalYearId });
export const saveTaxAdjustment = (dto: any) => post(tc + "adjustments", dto);
export const deleteTaxAdjustment = (id: number) => fcall("delete", tc + "adjustments/" + id);
export const getTaxableProfit = (params: any) => get(tc + "taxable-profit", params);
export const provisionTaxableProfit = (dto: any) => post(tc + "taxable-profit/provision", dto);

export const getBadDebt = (params: any) => get(cl + "bad-debt", params);
export const postBadDebt = (dto: any) => post(cl + "bad-debt", dto);
export const getCreditHolds = () => get(cl + "holds");
export const setCreditHold = (customerId: number, active: boolean, reason: string) => post(cl + "holds", { customerId, active, reason });
export const getDunning = () => get(cl + "dunning");
export const runDunning = (asOf?: string | null) => fcall("post", cl + "dunning/run" + (asOf ? "?asOf=" + asOf : ""));
export const approveDunning = (id: number) => post(cl + `dunning/${id}/approve`);
export const getPaymentProposals = () => get(cl + "payment-proposals");
export const getPaymentProposal = (id: number) => get(cl + "payment-proposals/" + id);
export const createPaymentProposal = (asOf: string | null, cashLimit: number | null) => post(cl + "payment-proposals", { asOf, cashLimit });
export const approvePaymentProposal = (id: number) => post(cl + `payment-proposals/${id}/approve`);

/** Supplier advances not yet recovered (procurement controller; the finance roles that read procurement can open it). */
export const getOpenSupplierAdvances = () => get("procurement/suppliers/open-advances");

// ---------------------------------------------------------------- F3b: completeness and payroll settlements
const cm = "finance/completeness/";
const ps = "finance/payroll-settlements/";
export const getDirectCashFlow = (params: any) => get(cm + "cash-flow-direct", params);
/** kind: BalanceSheet | IncomeStatement */
export const getComparative = (kind: string, fiscalYearId: number | null, includeDraft: boolean) => get(cm + "comparative", { kind, fiscalYearId, includeDraft });
export const getNotes = (fiscalYearId?: number | null) => get(cm + "notes", { fiscalYearId });
export const getBankGuarantees = (direction?: number | null) => get(cm + "guarantees", { direction });
export const saveBankGuarantee = (dto: any) => post(cm + "guarantees", dto);
/** action: extend, reduce, release, expire, claim */
export const bankGuaranteeAction = (dto: any) => post(cm + "guarantees/action", dto);
export const saveGuaranteeLimit = (dto: any) => post(cm + "guarantees/limits", dto);
export const getGuaranteeExposure = (asOf?: string | null) => get(cm + "guarantees/exposure", { asOf });
export const getClearances = (providerId?: number | null, asOf?: string | null) => get(cm + "clearances", { providerId, asOf });
export const saveClearance = (dto: any) => post(cm + "clearances", dto);
export const getRetentionStatus = (providerId: number, contractId?: number | null, asOf?: string | null) => get(cm + "retention/status", { providerId, contractId, asOf });
export const releaseRetention = (dto: any) => post(cm + "retention/release", dto);
export const saveEstimate = (dto: any) => post(cm + "estimates", dto);
export const getPlannedVsActual = (workOrderId: number) => get(cm + `work-orders/${workOrderId}/planned-vs-actual`);
export const getSupplierNotes = (providerId?: number | null) => get(cm + "supplier-notes", { providerId });
export const saveSupplierNote = (dto: any) => post(cm + "supplier-notes", dto);
/** action: approve, cancel */
export const supplierNoteAction = (id: number, action: string, note?: string) => post(cm + "supplier-notes/action", { id, action, note: note || null });
export const reconcileSupplierStatement = (dto: any) => post(cm + "supplier-statements", dto);
export const getSupplierStatement = (id: number) => get(cm + "supplier-statements/" + id);
export const getConfirmations = (customerId?: number | null) => get(cm + "confirmations", { customerId });
export const issueConfirmation = (customerId: number, asOf: string) => post(cm + "confirmations", { customerId, asOf });
/** action: confirm, dispute */
export const respondConfirmation = (dto: any) => post(cm + "confirmations/respond", dto);

export const getTerminations = () => get(ps + "terminations");
export const saveTermination = (dto: any) => post(ps + "terminations", dto);
export const terminationAction = (id: number, action: string, note?: string) => post(ps + "terminations/action", { id, action, note: note || null });
export const getBonusRuns = () => get(ps + "bonus-runs");
export const saveBonusRun = (dto: any) => post(ps + "bonus-runs", dto);
export const bonusRunAction = (id: number, action: string, note?: string) => post(ps + "bonus-runs/action", { id, action, note: note || null });
export const getPayrollTaxReconciliation = (year: number) => get(ps + "tax-reconciliation", { year });
