import { call, clean } from "@/api/ApiClient";

// ENG-E1: the engineering module (api/v1/engineering/*; EnCaseController, EnHandoverController, EnPlatformController, EnReferenceController, EnDeviationController,
// EnStandardGapController, EnRegistrationController, EnInfoRequestController, EnOverviewController). Every function answers [data, null] or [null, Error]; a refusal of the
// server (success = false) arrives as an Error whose message is the server's Persian text, shown as it is.
// The JSON is camelCase. A list answers { data: [...], totalItems }. Role checks are on the server; the pages read the can<Action> / <action>BlockedReason flags of the DTOs.

const base = "engineering/";

/** Query for every paged list: PageNumber and PageSize plus the filters of that list. */
export type EnQuery = Record<string, any>;

// ---- overview
/** The counters of the engineering control (counts only). */
export const getEnOverview = () => call("get", base + "overview");

// ---- cases
export const getEnCases = (q: EnQuery) => call("get", base + "cases", undefined, clean(q));
export const getEnCase = (id: number) => call("get", base + "cases/" + id);
/** The charter of the case without any price (the only charter view engineering reads). */
export const getEnCharterView = (id: number) => call("get", base + "cases/" + id + "/charter-view");
/** body: { id, action: propose|change|confirm|return|approve|reclassify|save-concept|add-tender-document|release-tender|charter-confirm|charter-return|cancel|set-standard|save-opinion, rowVersion, ... }
 *  add-tender-document: { documentId (an uploaded file), title }; set-standard: { referenceCode (at most 60), standardEvidence, standardAttachmentId, note }. */
export const postEnCaseAction = (body: any) => call("post", base + "cases/action", body);

// ---- clarifications
export const getEnClarifications = (q: EnQuery) => call("get", base + "clarifications", undefined, clean(q));
export const getEnClarification = (id: number) => call("get", base + "clarifications/" + id);
export const saveEnClarification = (body: any) => call("post", base + "clarifications", body);
/** body: { id, action: submit|send|answer|close|reask|void, clientLetterRef, answer, question, note, rowVersion } */
export const postEnClarificationAction = (body: any) => call("post", base + "clarifications/action", body);

// ---- platform dossiers (one per product and standard)
export const getEnPlatforms = (q: EnQuery) => call("get", base + "platforms", undefined, clean(q));
export const getEnPlatformSummary = () => call("get", base + "platforms/summary");
export const getEnDossier = (productId: number, code?: string | null) => call("get", base + "platforms/" + productId, undefined, clean({ code }));
/** body: { productId, referenceCode, standardBasis, action: native-standard|assess|complete|confirm|return, note, rowVersion } */
export const postEnDossierAction = (body: any) => call("post", base + "platforms/action", body);
/** body: { lineId, documentId, controlledDocumentId, revision, notApplicable, notApplicableReason, rowVersion } */
export const postEnSupplyLine = (body: any) => call("post", base + "platforms/supply", body);

// ---- reference document lists
export const getEnReferenceLists = (q: EnQuery) => call("get", base + "reference/lists", undefined, clean(q));
export const getEnReferenceList = (id: number) => call("get", base + "reference/lists/" + id);
export const getEnReferenceCurrent = (code: string) => call("get", base + "reference/current", undefined, { code });
export const saveEnReferenceList = (body: any) => call("post", base + "reference/lists", body);
/** body: { id, action: submit|approve|return|void|revise, note, rowVersion } */
export const postEnReferenceAction = (body: any) => call("post", base + "reference/lists/action", body);

// ---- handovers, waivers, debts, distribution
export const getEnHandovers = (q: EnQuery) => call("get", base + "handovers", undefined, clean(q));
/** The handover DTO carries canDraftDistribution / draftDistributionBlockedReason and canDistribute / distributeBlockedReason (the same checks as the two actions; the page only reads them). */
export const getEnHandover = (id: number) => call("get", base + "handovers/" + id);
/** body: { id | caseId | distributionId, action: draft|refresh|submit|accept|return|release|void|draft-distribution|edit-scope|distribute|acknowledge, note, scopeNote, rowVersion }
 *  draft-distribution and distribute are the PMO's only (the PMO user or the PMO-side agent). */
export const postEnHandoverAction = (body: any) => call("post", base + "handovers/action", body);
export const getEnWaivers = (q: EnQuery) => call("get", base + "waivers", undefined, clean(q));
export const getEnWaiver = (id: number) => call("get", base + "waivers/" + id);
/** body: { handoverId, reason (min 50), debtHours (whole hours 1..72, counted from the CEO's approval) } */
export const requestEnWaiver = (body: any) => call("post", base + "waivers/request", body);
/** body: { id, decision: approve|reject, note, debtHours (optional; the CEO may only lower it), rowVersion } */
export const decideEnWaiver = (body: any) => call("post", base + "waivers/decide", body);
export const getEnDebts = (q: EnQuery) => call("get", base + "debts", undefined, clean(q));
export const getEnDistributions = (handoverId?: number | null, onlyOpen = false) => call("get", base + "distributions", undefined, clean({ handoverId, onlyOpen }));

// ---- standards deviations
/** filters: Status, CaseId, ProductId, OnlyOpen, PageNumber, PageSize (at most 500). */
export const getEnDeviations = (q: EnQuery) => call("get", base + "deviations", undefined, clean(q));
export const getEnDeviation = (id: number) => call("get", base + "deviations/" + id);
/** body: { id, decision: approve|reject, note, rowVersion } */
export const decideEnDeviation = (body: any) => call("post", base + "deviations/decide", body);

// ---- standard gaps
export const getEnGaps = (q: EnQuery) => call("get", base + "standard-gaps", undefined, clean(q));
export const getEnGap = (id: number) => call("get", base + "standard-gaps/" + id);
/** body: { id, engineeringImpactNote (min 30), rowVersion } */
export const confirmEnGap = (body: any) => call("post", base + "standard-gaps/confirm", body);

// ---- platform registrations (a New design registered as a platform)
export const getEnRegistrations = (q: EnQuery) => call("get", base + "platform-registrations", undefined, clean(q));
export const getEnRegistration = (id: number) => call("get", base + "platform-registrations/" + id);
/** body: { caseId, productId, referenceCode, standardBasis, rationale } */
export const proposeEnRegistration = (body: any) => call("post", base + "platform-registrations", body);
/** body: { id, decision: approve|reject|withdraw, note, rowVersion } */
export const decideEnRegistration = (body: any) => call("post", base + "platform-registrations/decide", body);

// ---- questions of the units to the PMO
export const getEnInfoRequests = (q: EnQuery) => call("get", base + "info-requests", undefined, clean(q));
export const getEnInfoRequest = (id: number) => call("get", base + "info-requests/" + id);
/** body: { handoverId, caseId, unit (1..6), question } */
export const askEnInfo = (body: any) => call("post", base + "info-requests", body);
/** body: { id, action: answer|close|void, answer, rowVersion } */
export const postEnInfoAction = (body: any) => call("post", base + "info-requests/action", body);
