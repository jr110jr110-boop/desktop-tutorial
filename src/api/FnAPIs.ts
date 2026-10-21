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
