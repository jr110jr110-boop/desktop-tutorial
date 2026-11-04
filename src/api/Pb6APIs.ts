import { call, clean, http, saveBlob } from "@/api/ApiClient";

// PB6: procurement completion (return/revision, archive, bridge, addenda, guarantees, payment terms, reports), contracts system, progress statements, petty-cash flow.
const proc = "procurement/";
const ct = "contracts-system/";
const ps = "progress-statements/";
const pc = "petty-cash-flow/";

const act = (url: string, id: number, action: string, extra: any = {}) => call("post", url, { id, action, ...extra });

// ---- procurement
export const returnRequest = (id: number, note: string) => call("post", proc + "requests/return", { id, action: "return", note });
export const reviseRequest = (id: number, note?: string) => call("post", proc + "requests/revise", { id, action: "revise", note: note || null });
export const getRequestChain = (id: number) => call("get", proc + `requests/${id}/chain`);
export const getArchive = (params: any) => call("get", proc + "archive", undefined, clean(params));
export const archiveDocument = (ownerType: number, ownerId: number, reason: string) => call("post", proc + "archive", { ownerType, ownerId, reason });
export const restoreDocument = (ownerType: number, ownerId: number, reason: string) => call("post", proc + "archive/restore", { ownerType, ownerId, reason });
export const getLegacyBuyOrders = (openOnly = true) => call("get", proc + "legacy-buy-orders", undefined, { openOnly });
export const convertBuyOrder = (dto: any) => call("post", proc + "legacy-buy-orders/convert", dto);
export const getAddenda = (purchaseOrderId?: number | null) => call("get", proc + "addenda", undefined, clean({ purchaseOrderId }));
export const saveAddendum = (dto: any) => call("post", proc + "addenda", dto);
/** action: submit, approve, approve-ceo, reject (note). */
export const addendumAction = (id: number, action: string, note?: string) => act(proc + "addenda/action", id, action, { note: note || null });
export const getGuarantees = (params: any) => call("get", proc + "guarantees", undefined, clean(params));
export const saveGuarantee = (dto: any) => call("post", proc + "guarantees", dto);
/** action: request-release, release, reject-release, claim. */
export const guaranteeAction = (id: number, action: string, note?: string) => act(proc + "guarantees/action", id, action, { note: note || null });
export const runGuaranteeAlerts = () => call("post", proc + "guarantees/run-alerts");
export const getPaymentTerms = (orderId: number) => call("get", proc + `orders/${orderId}/payment-terms`);
export const savePaymentTerms = (dto: any) => call("post", proc + "payment-terms", dto);
export const confirmTermCondition = (id: number, note?: string) => act(proc + "payment-terms/confirm", id, "confirm", { note: note || null });
export const requestTermPayment = (termId: number, invoiceId?: number | null, note?: string) => call("post", proc + "payment-terms/request-payment", { termId, invoiceId: invoiceId || null, note: note || null });
export const getContractPrint = (orderId: number) => call("get", proc + `orders/${orderId}/contract-print`);
export const getReportCatalog = () => call("get", proc + "reports/catalog");
export const getProcurementReport = (params: any) => call("get", proc + "reports", undefined, clean(params));
export async function exportProcurementReport(params: any): Promise<string | null> {
  try {
    const response = await http.get(proc + "reports/export", { params: clean(params), responseType: "blob" });
    if ((response.headers["content-type"] || "").includes("application/json")) return JSON.parse(await (response.data as Blob).text()).message || "خطا";
    saveBlob(response.data, `procurement-${params.report || "report"}.xlsx`);
    return null;
  } catch (error: any) {
    return error?.message || "خطا";
  }
}

// ---- contracts system
export const getDeals = (params: any = {}) => call("get", ct + "deals", undefined, clean(params));
export const getDeal = (id: number) => call("get", ct + "deals/" + id);
export const saveDeal = (dto: any) => call("post", ct + "deals", dto);
/** action: submit, approve, return (note), reject (note). */
export const dealAction = (id: number, action: string, note?: string) => act(ct + "deals/action", id, action, { note: note || null });
export const getTenders = (params: any = {}) => call("get", ct + "tenders", undefined, clean(params));
export const getTender = (id: number) => call("get", ct + "tenders/" + id);
export const saveTender = (dto: any) => call("post", ct + "tenders", dto);
export const selectTenderWinner = (tenderRunId: number, participantId: number, reason?: string) => call("post", ct + "tenders/select-winner", { tenderRunId, participantId, reason: reason || null });
/** action: confirm-winner, reopen (note), cancel (note). */
export const tenderAction = (id: number, action: string, note?: string) => act(ct + "tenders/action", id, action, { note: note || null });
export const getDrafts = (params: any = {}) => call("get", ct + "drafts", undefined, clean(params));
export const getDraft = (id: number) => call("get", ct + "drafts/" + id);
export const getDraftPrint = (id: number) => call("get", ct + `drafts/${id}/print`);
export const saveDraft = (dto: any) => call("post", ct + "drafts", dto);
/** action: exchange, comments (note), agree, sign (reference = contract number), notify, cancel (note). */
export const draftAction = (id: number, action: string, note?: string, reference?: string) => act(ct + "drafts/action", id, action, { note: note || null, reference: reference || null });
export const getContractChanges = (params: any = {}) => call("get", ct + "changes", undefined, clean(params));
export const getContractMapping = () => call("get", ct + "mapping");

// ---- progress statements
export const getStatements = (params: any = {}) => call("get", ps, undefined, clean(params));
export const getStatement = (id: number) => call("get", ps + id);
export const getStatementPrint = (id: number) => call("get", ps + `${id}/print`);
export const saveStatement = (dto: any) => call("post", ps, dto);
/** action: prepare, technical-approve (amount), supervision-approve (amount), authorise-payment, request-payment, send (date), return (note), reject (note). */
export const statementAction = (id: number, action: string, extra: any = {}) => act(ps + "action", id, action, extra);
export const recordStatementPayment = (dto: any) => call("post", ps + "payment", dto);
export const saveStatementCounterparty = (dto: any) => call("post", ps + "counterparty", dto);
export const getLegalRates = () => call("get", ps + "legal-rates");
export const saveLegalRate = (dto: any) => call("post", ps + "legal-rates", dto);
/** action: verify, retire (note). */
export const legalRateAction = (id: number, action: string, note?: string) => act(ps + "legal-rates/action", id, action, { note: note || null });
export const getCbs = (params: any) => call("get", ps + "cbs", undefined, clean(params));
export const saveCbs = (dto: any) => call("post", ps + "cbs", dto);
export const getCashFlow = (params: any) => call("get", ps + "cash-flow", undefined, clean(params));
export const saveCashPlan = (dto: any) => call("post", ps + "cash-flow", dto);
export const getStatementAnalytics = (params: any = {}) => call("get", ps + "analytics", undefined, clean(params));

// ---- petty-cash flow
export const getPettyRequests = (params: any = {}) => call("get", pc + "requests", undefined, clean(params));
export const savePettyRequest = (dto: any) => call("post", pc + "requests", dto);
/** action: submit, control, approve, reject (note), create-payment (bankAccountId), confirm-topup. */
export const pettyRequestAction = (id: number, action: string, extra: any = {}) => act(pc + "requests/action", id, action, extra);
export const getPettySummaries = (params: any = {}) => call("get", pc + "summaries", undefined, clean(params));
export const getPettySummary = (id: number) => call("get", pc + "summaries/" + id);
export const previewPettySummary = (dto: any) => call("post", pc + "summaries/preview", dto);
export const savePettySummary = (dto: any) => call("post", pc + "summaries", dto);
/** action: submit, control, approve, final, return (note), reject (note), sign-misc (lineId). */
export const pettySummaryAction = (id: number, action: string, extra: any = {}) => act(pc + "summaries/action", id, action, extra);

// ---- inquiry planning step (D5) and return / new version of an inquiry
/** action: approve-planning (amount = approved budget, days = approved delivery days), return (note), revise. */
export const inquiryPlanningAction = (id: number, action: string, extra: { note?: string | null; amount?: number | null; days?: number | null } = {}) =>
  call("post", proc + "inquiries/action", { id, action, note: extra.note || null, amount: extra.amount ?? null, days: extra.days ?? null });
