import { call, clean, http, saveBlob, fileErrorText } from "@/api/ApiClient";

// Project control (PMO) module (api/v1/projectcontrol/...): contract terms and initiation, WBS and schedule, baselines,
// progress and earned value, payments / guarantees / penalties, engineering documents, change control and reports.
// Reads need a reader role; every write checks its role in the service (see composables/roles.ts, ProjectControlRoles).
// Paged answers carry { data, totalItems }, plain answers are the record itself.
const base = "projectcontrol/";

// ---------------------------------------------------------------- setup
export const getTerms = (contractId: number) => call("get", base + "terms/" + contractId);
export const saveTerms = (dto: any) => call("post", base + "terms", dto);
/** category: 1 MA (manufacturing), 2 SP (spare parts), 3 SE (services). */
export const generateProjectCode = (contractId: number, category: number) => call("post", base + "terms/generate-code", { contractId, category });
export const getCoding = (table: string) => call("get", base + "coding/" + table);
export const saveCoding = (dto: any) => call("post", base + "coding", dto);
export const getWorkOrderInfos = (contractId: number) => call("get", base + "work-order-info/" + contractId);
export const saveWorkOrderInfo = (dto: any) => call("post", base + "work-order-info", dto);

export const getInitiation = (contractId: number) => call("get", base + "initiation/" + contractId);
export const startInitiation = (contractId: number) => call("post", base + "initiation/start/" + contractId);
export const initiationAction = (dto: any) => call("post", base + "initiation/action", dto);
export const getOverdueInitiation = () => call("get", base + "initiation/overdue");

export const getCharter = (contractId: number) => call("get", base + "charter/" + contractId);
export const saveCharter = (dto: any) => call("post", base + "charter", dto);
/** step: accept (the project manager takes the authority), approve (CEO). */
export const charterAction = (step: "accept" | "approve", contractId: number, note?: string) => call("post", base + "charter/" + step, { contractId, managerAccepted: step === "accept", note: note || null });
export const getPlan = (contractId: number) => call("get", base + "plan/" + contractId);
export const savePlan = (dto: any) => call("post", base + "plan", dto);
export const approvePlan = (contractId: number, note?: string) => call("post", base + "plan/approve", { contractId, note: note || null });

export const getReviews = (params: any) => call("get", base + "reviews", undefined, clean(params));
export const saveReview = (dto: any) => call("post", base + "reviews", dto);
/**
 * action: capability-check (planning), accept (PMO and COMMERCIAL, two different people; slot pmo / commercial, empty = by the role of the user),
 * decide (CEO: decision 2 reject or 3 modify, the reason in note; the acceptance itself is never the CEO's and a re-review after a change is accepted only).
 */
export const reviewAction = (id: number, action: string, decision = 0, note?: string, slot?: string | null) => call("post", base + "reviews/action", { id, action, decision, note: note || null, slot: slot || null });
export const getAmendments = (params: any) => call("get", base + "amendments", undefined, clean(params));
export const saveAmendment = (dto: any) => call("post", base + "amendments", dto);
/** action: ceo-approve, customer-approve (reference, date), copy-sent (copyId, date). */
export const amendmentAction = (dto: any) => call("post", base + "amendments/action", dto);

// ---------------------------------------------------------------- schedule
export const getCalendars = (params: any = {}) => call("get", base + "calendars", undefined, clean({ PageSize: 100, ...params }));
export const saveCalendar = (dto: any) => call("post", base + "calendars", dto);
export const getWbs = (params: any) => call("get", base + "wbs", undefined, clean({ PageSize: 0, ...params }));
export const saveWbsNode = (dto: any) => call("post", base + "wbs", dto);
export const deleteWbsNode = (id: number) => call("delete", base + "wbs/" + id);
export const approveWbsNode = (id: number) => call("post", base + "wbs/" + id + "/approve");
export const generateWbs = (contractId: number, workOrderIds?: number[]) => call("post", base + "wbs/generate", { contractId, workOrderIds: workOrderIds && workOrderIds.length ? workOrderIds : null });
export const autoWeights = (contractId: number) => call("post", base + "wbs/auto-weights/" + contractId);

export const getActivities = (params: any) => call("get", base + "activities", undefined, clean({ PageSize: 0, ...params }));
export const getActivity = (id: number) => call("get", base + "activities/" + id);
export const saveActivity = (dto: any) => call("post", base + "activities", dto);
export const deleteActivity = (id: number) => call("delete", base + "activities/" + id);
/** action: start, finish (date), hold (reason), resume. */
export const activityAction = (id: number, action: string, date?: string | null, reason?: string | null) => call("post", base + "activities/action", { id, action, date: date || null, reason: reason || null });
export const getDependencies = (params: any) => call("get", base + "dependencies", undefined, clean({ PageSize: 0, ...params }));
export const saveDependency = (dto: any) => call("post", base + "dependencies", dto);
export const deleteDependency = (id: number) => call("delete", base + "dependencies/" + id);
export const runCpm = (contractId: number, forecast: boolean, mirror = true, dataDate?: string | null) => call("post", base + "cpm/run", { contractId, forecast, mirror, dataDate: dataDate || null });
export const getExecutivePlan = (params: any) => call("get", base + "executive-plan", undefined, clean({ PageSize: 0, ...params }));
export const getNeedBy = (params: any) => call("get", base + "need-by", undefined, clean({ PageSize: 0, ...params }));
export const applyNeedBy = (contractId: number) => call("post", base + "need-by/apply", { contractId });
export const generateItpActivities = (contractId: number, workOrderId: number) => call("post", base + "itp/generate", { contractId, workOrderId });
export const getNotices = (params: any) => call("get", base + "notices", undefined, clean({ PageSize: 0, ...params }));

/** MS Project XML: the API answers the XML text itself (application/xml), or JSON { success: false } when it refuses. */
export async function downloadMsProject(contractId: number, fileName: string): Promise<string | null> {
  try {
    const response = await http.get(base + "export/msproject/" + contractId, { responseType: "blob" });
    const type = String(response.headers["content-type"] || "");
    if (type.includes("application/json")) {
      const json = JSON.parse(await (response.data as Blob).text());
      return json.message || "خروجی ساخته نشد.";
    }
    saveBlob(response.data, fileName);
    return null;
  } catch (error: any) {
    return fileErrorText(error);
  }
}

// ---------------------------------------------------------------- baselines and cost lines
export const getBaselines = (params: any) => call("get", base + "baselines", undefined, clean(params));
export const getBaseline = (id: number) => call("get", base + "baselines/" + id);
export const createBaseline = (contractId: number, reason?: string) => call("post", base + "baselines", { contractId, reason: reason || null });
/** action: sign (unit manager, all remaining signatures of the role), approve (PMO deputy). */
export const baselineAction = (id: number, action: "sign" | "approve", note?: string) => call("post", base + "baselines/action", { id, action, note: note || null });
export const getCostLines = (params: any) => call("get", base + "cost-lines", undefined, clean({ PageSize: 0, ...params }));
export const saveCostLine = (dto: any) => call("post", base + "cost-lines", dto);
export const deleteCostLine = (id: number) => call("delete", base + "cost-lines/" + id);

// ---------------------------------------------------------------- progress and earned value
export const getProgress = (contractId: number) => call("get", base + "progress/" + contractId);
export const getManualProgress = (params: any) => call("get", base + "manual-progress", undefined, clean({ PageSize: 200, ...params }));
export const proposeManualProgress = (activityId: number, percent: number, comment?: string) => call("post", base + "manual-progress", { activityId, percent, comment: comment || null });
export const decideManualProgress = (id: number, approve: boolean, note?: string) => call("post", base + "manual-progress/decide", { id, approve, note: note || null });
export const getEvm = (contractId: number, asOf?: string | null) => call("get", base + "evm/" + contractId, undefined, clean({ asOf }));
export const getCashFlow = (contractId: number) => call("get", base + "cash-flow/" + contractId);
export const getCostEntries = (params: any) => call("get", base + "costs", undefined, clean({ PageSize: 200, ...params }));
export const addCostEntry = (dto: any) => call("post", base + "costs", dto);
export const getSnapshots = (params: any) => call("get", base + "snapshots", undefined, clean({ PageSize: 200, ...params }));
export const getSnapshot = (id: number) => call("get", base + "snapshots/" + id);
export const takeSnapshot = (contractId: number, dataDate?: string | null) => call("post", base + "snapshots", { contractId, dataDate: dataDate || null });
export const getDailyReport = (params: any) => call("get", base + "daily-report", undefined, clean(params));
export const saveDailyReport = (dto: any) => call("post", base + "daily-report", dto);
export const confirmDailyReport = (id: number) => call("post", base + "daily-report/" + id + "/confirm");

// ---------------------------------------------------------------- commercial
export const getMilestones = (params: any) => call("get", base + "milestones", undefined, clean({ PageSize: 0, ...params }));
export const saveMilestone = (dto: any) => call("post", base + "milestones", dto);
export const deleteMilestone = (id: number) => call("delete", base + "milestones/" + id);
/** action: statement (provisionalStatementId), invoiced (reference), paid (reference), client-letter (reference). */
export const milestoneAction = (dto: any) => call("post", base + "milestones/action", dto);
export const evaluateMilestones = (contractId: number) => call("post", base + "milestones/evaluate/" + contractId);
export const getGuarantees = (params: any) => call("get", base + "guarantees", undefined, clean({ PageSize: 200, ...params }));
export const parseGuarantees = () => call("post", base + "guarantees/parse");
export const saveGuarantee = (dto: any) => call("post", base + "guarantees", dto);
export const addGuaranteeEvent = (dto: any) => call("post", base + "guarantees/events", dto);
/** POST guarantees/release { guaranteeId, releaseDate (not in the future), note (the letter of the client or the bank), documentId? }: COMMERCIAL, FINANCEMANAGER, PMO. */
export const releaseGuarantee = (dto: any) => call("post", base + "guarantees/release", dto);
/** R1a2 M-3: release and cancellation share one request slot; the first call records the request, a second person confirms with the same call. This takes the open request back (the one who asked, or PMO). */
export const withdrawGuaranteeRequest = (guaranteeId: number) => call("post", base + "guarantees/withdraw", { guaranteeId });
export const getGuaranteeAlerts = (contractId?: number | null) => call("get", base + "guarantees/alerts", undefined, clean({ contractId }));
export const computePenalties = (contractId: number) => call("post", base + "penalties/compute/" + contractId);
export const getPenalties = (params: any) => call("get", base + "penalties", undefined, clean({ PageSize: 0, ...params }));
/** status: 1 waived, 2 negotiated (amount); the CEO decides. */
export const decidePenalty = (id: number, status: number, reason: string, amount?: number | null) => call("post", base + "penalties/decide", { id, status, reason, amount: amount ?? null });
export const getVariations = (params: any) => call("get", base + "variations", undefined, clean({ PageSize: 0, ...params }));
export const saveVariation = (dto: any) => call("post", base + "variations", dto);
/** action: submit (COMMERCIAL), approve (PMO or CEO; reference, date and one of clientLetterId / clientDocumentId), reject (PMO or CEO; note = the reason). */
export const variationAction = (dto: any) => call("post", base + "variations/action", dto);

// ---------------------------------------------------------------- engineering documents
export const draftMdr = (contractId: number, workOrderId: number) => call("post", base + "mdr/draft", { contractId, workOrderId });
export const getDeliverables = (params: any) => call("get", base + "mdr", undefined, clean({ PageSize: 0, ...params }));
export const saveDeliverable = (dto: any) => call("post", base + "mdr", dto);
/** action: first-issue, second-issue (revision), internal-approve, link-document (documentId / controlledDocumentId). */
export const deliverableAction = (dto: any) => call("post", base + "mdr/action", dto);
export const getMdrTemplates = () => call("get", base + "mdr-templates", undefined, { PageSize: 200 });
export const saveMdrTemplate = (dto: any) => call("post", base + "mdr-templates", dto);
export const getResponseCodes = () => call("get", base + "response-codes");
export const saveResponseCode = (dto: any) => call("post", base + "response-codes", dto);
export const getTransmittals = (params: any) => call("get", base + "transmittals", undefined, clean({ PageSize: 200, ...params }));
export const getTransmittal = (id: number) => call("get", base + "transmittals/" + id);
export const saveTransmittal = (dto: any) => call("post", base + "transmittals", dto);
export const recordTransmittalResponse = (dto: any) => call("post", base + "transmittals/response", dto);
export const generateFinalBook = (workOrderId: number, variant: "company" | "client", allowIncomplete: boolean) => call("post", base + "final-books", { workOrderId, variant, allowIncomplete });
export const getFinalBooks = (params: any) => call("get", base + "final-books", undefined, clean({ PageSize: 200, ...params }));

// ---------------------------------------------------------------- change control and claims
export const getChanges = (params: any) => call("get", base + "changes", undefined, clean({ PageSize: 200, ...params }));
export const getChange = (id: number) => call("get", base + "changes/" + id);
export const saveChange = (dto: any) => call("post", base + "changes", dto);
/** action: register, analyze (impact texts), recommend (note), vote (vote, boardRole), decide (decision, note), close. */
export const changeAction = (dto: any) => call("post", base + "changes/action", dto);
export const getAuthorities = () => call("get", base + "approval-authorities");
export const saveAuthority = (dto: any) => call("post", base + "approval-authorities", dto);
export const getClaims = (params: any) => call("get", base + "claims", undefined, clean({ PageSize: 200, ...params }));
export const saveClaim = (dto: any) => call("post", base + "claims", dto);
/** action: quantify, notify, negotiate, settle, withdraw, litigate, approve-pmo, approve-design. */
export const claimAction = (dto: any) => call("post", base + "claims/action", dto);

// ---------------------------------------------------------------- shipments, reports, portfolio
export const getShipments = (params: any) => call("get", base + "shipments", undefined, clean({ PageSize: 200, ...params }));
export const saveShipment = (dto: any) => call("post", base + "shipments", dto);
/** action: pack, release, ship, deliver, handover. */
export const shipmentAction = (dto: any) => call("post", base + "shipments/action", dto);
export const getReports = (params: any) => call("get", base + "reports", undefined, clean({ PageSize: 200, ...params }));
export const getReport = (id: number) => call("get", base + "reports/" + id);
export const generateReport = (contractId: number, kind: number, dataDate?: string | null) => call("post", base + "reports/generate", { contractId, kind, dataDate: dataDate || null });
export const releaseReport = (id: number) => call("post", base + "reports/" + id + "/release");
export const getPortfolio = () => call("get", base + "portfolio", undefined, { PageSize: 0 });
export const getPcDashboard = () => call("get", base + "dashboard");

// ---------------------------------------------------------------- R1c: settings, cost check, past snapshots, final book approval, baseline activities
/** PcSettingDto[] { key, title, source (a source starting with the star is a provisional default), value, defaultValue, isDefault, confirmed, min, max, wholeNumber }. Who: readers. */
export const getPcSettings = () => call("get", base + "settings");
/** PcSettingsSaveDto { items: [{ key, value }] }. Who: PMO and CEO only (the service refuses others in Persian). */
export const savePcSettings = (items: { key: string; value: number }[]) => call("put", base + "settings", { items });
/** PcCostCheckDto { costEntryId, note? }: the second FINANCIAL person checks a manual cost entry; only then it counts as actual cost. */
export const checkCostEntry = (costEntryId: number, note?: string | null) => call("post", base + "costs/check", { costEntryId, note: note || null });
/** params: ContractId, State (0 requested, 1 approved, 2 rejected, 3 used), paging. */
export const getPastSnapshotRequests = (params: any) => call("get", base + "snapshots/past-requests", undefined, clean({ PageSize: 50, ...params }));
/** PcPastSnapshotRequestDto { contractId, dataDate (ISO), reason }: planning asks for a snapshot with a data date in the past. */
export const requestPastSnapshot = (dto: { contractId: number; dataDate: string; reason: string }) => call("post", base + "snapshots/past-requests", dto);
/** PcPastSnapshotDecisionDto { id, approve, note? }: the PMO deputy decides; after approval planning takes the snapshot with that data date (used once). */
export const decidePastSnapshot = (id: number, approve: boolean, note?: string | null) => call("post", base + "snapshots/past-requests/decide", { id, approve, note: note || null });
/** A final-book revision becomes effective when someone other than its preparer approves it (PMO or QC). */
export const approveFinalBook = (id: number) => call("post", base + "final-books/" + id + "/approve");
/** The frozen activities of a baseline (dates, budget, weight, calendar, constraint as approved). */
export const getBaselineActivities = (id: number) => call("get", base + "baselines/" + id + "/activities");
