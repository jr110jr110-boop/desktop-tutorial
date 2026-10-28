import { call, clean } from "@/api/ApiClient";

// Quality module, QC and QA (api/v1/quality/...): ITP, inspection records, test reports, calibration, personnel and welding,
// material certificates, CAPA, complaints, audits, management review, release notes, document control and settings.
// Every write answers with the saved record. Roles are checked inside the services (see composables/roles.ts, QualityRoles).
const base = "quality/";

export const getQualityDashboard = () => call("get", base + "dashboard");
export const getQualitySettings = () => call("get", base + "settings");
export const saveQualitySetting = (key: string, value: number) => call("post", base + "settings", { key, value });

// users for the owner / auditor / recipient lists: the slim picker list (id, userName, name, lastName) that every signed-in user may read.
// The full user list (Accounts/get-all-users) is for the PMO only.
export const getUsers = (contains?: string) => call("get", "Accounts/users/lookup", undefined, clean({ Contains: contains, PageNumber: 1, PageSize: 200 }));

// ITP
export const getItpTemplates = (params: any) => call("get", base + "itp-templates", undefined, clean(params));
export const getItpTemplate = (id: number) => call("get", base + "itp-templates/" + id);
export const getItps = (params: any) => call("get", base + "itps", undefined, clean(params));
export const getItp = (id: number) => call("get", base + "itps/" + id);
export const getItpByWorkOrder = (workOrderId: number) => call("get", base + "itps/by-work-order/" + workOrderId);
export const getPendingPoints = (params: any) => call("get", base + "itps/pending-points", undefined, clean(params));
export const ensureItp = (dto: any) => call("post", base + "itps/ensure", dto);
export const saveItp = (dto: any) => call("post", base + "itps", dto);
/** action: submit, client-approve (reference, date), use, close, revise (reason). */
export const itpAction = (id: number, action: string, extra: any = {}) => call("post", base + "itps/action", { id, action, ...extra });
export const saveItpActivity = (dto: any) => call("post", base + "itps/activity", dto);
/** action: notify, plan, done, not-applicable (note required). The API has no "waive" action although its comment lists one. */
export const itpActivityAction = (id: number, action: string, extra: any = {}) => call("post", base + "itps/activity/action", { id, action, ...extra });
export const addItpRelease = (dto: any) => call("post", base + "itps/release", dto);
export const setItpLimits = (dto: any) => call("post", base + "itps/limits", dto);

// inspection records
export const getQcRecords = (params: any) => call("get", base + "records", undefined, clean(params));
export const getQcRecord = (id: number) => call("get", base + "records/" + id);
export const saveQcRecord = (dto: any) => call("post", base + "records", dto);

// test reports
export const getTestReports = (params: any) => call("get", base + "reports", undefined, clean(params));
export const getTestReport = (id: number) => call("get", base + "reports/" + id);
export const getReportStandards = (type: number) => call("get", base + "reports/standards/" + type);
export const saveTestReport = (dto: any) => call("post", base + "reports", dto);
/** action: submit, approve, reject (reason). */
export const testReportAction = (id: number, action: string, reason?: string) => call("post", base + "reports/action", { id, action, reason: reason || null });

// calibration
export const getEquipmentList = (params: any) => call("get", base + "equipment", undefined, clean(params));
export const getEquipment = (id: number) => call("get", base + "equipment/" + id);
export const getEquipmentDue = (params: any) => call("get", base + "equipment/due", undefined, clean(params));
export const saveEquipment = (dto: any) => call("post", base + "equipment", dto);
/** action: out-of-service (reason), return-to-service. */
export const equipmentAction = (id: number, action: string, reason?: string) => call("post", base + "equipment/action", { id, action, reason: reason || null });
export const addCalibration = (dto: any) => call("post", base + "equipment/calibration", dto);
export const getCalibrationIntervals = () => call("get", base + "calibration/intervals");
export const saveCalibrationInterval = (equipmentType: number, months: number, source?: string) => call("post", base + "calibration/intervals", { equipmentType, months, source: source || null });
export const getOotCases = (params: any) => call("get", base + "out-of-tolerance", undefined, clean(params));
export const getOotCase = (id: number) => call("get", base + "out-of-tolerance/" + id);
export const assessOot = (dto: any) => call("post", base + "out-of-tolerance/assess", dto);
/**
 * R1q D40: links the accepted new inspection that re-inspected a document assessed as "re-inspect".
 * LinkOotReinspectionDto { itemId (the affected document line of the case), kind: "QcRecord" | "TestReport" | "QcInspection", recordId }. Answer: the case (OotCaseDto).
 */
export const linkOotReinspection = (dto: any) => call("post", base + "out-of-tolerance/reinspection", dto);
export const closeOot = (id: number, note?: string) => call("post", base + "out-of-tolerance/close", { id, note: note || null });

// persons, qualifications, WPS, PQR, weld log
export const getPersons = (params: any) => call("get", base + "persons", undefined, clean(params));
export const getPerson = (id: number) => call("get", base + "persons/" + id);
export const savePerson = (dto: any) => call("post", base + "persons", dto);
export const getQualifications = (params: any) => call("get", base + "qualifications", undefined, clean(params));
export const getContinuity = (params: any) => call("get", base + "qualifications/continuity", undefined, clean(params));
export const saveQualification = (dto: any) => call("post", base + "qualifications", dto);
/** action: revoke (reason). */
export const qualificationAction = (id: number, action: string, reason?: string) => call("post", base + "qualifications/action", { id, action, reason: reason || null });
export const getWpsList = (params: any) => call("get", base + "wps", undefined, clean(params));
export const getWps = (id: number) => call("get", base + "wps/" + id);
export const saveWps = (dto: any) => call("post", base + "wps", dto);
/** action: approve-qc, approve-engineering, obsolete. */
export const wpsAction = (id: number, action: string) => call("post", base + "wps/action", { id, action });
export const getPqrs = () => call("get", base + "pqrs");
export const savePqr = (dto: any) => call("post", base + "pqrs", dto);
export const pqrAction = (id: number, action: string) => call("post", base + "pqrs/action", { id, action });
export const getWeldLogs = (params: any) => call("get", base + "weld-logs", undefined, clean(params));
export const saveWeldLog = (dto: any) => call("post", base + "weld-logs", dto);

// material certificates and traceability
export const getCertificates = (params: any) => call("get", base + "certificates", undefined, clean(params));
export const saveCertificate = (dto: any) => call("post", base + "certificates", dto);
export const verifyCertificate = (id: number, accept: boolean, note?: string) => call("post", base + "certificates/verify", { id, accept, note: note || null });
export const getTraceability = (params: any) => call("get", base + "traceability", undefined, clean(params));

// CAPA and complaints
export const getCapas = (params: any) => call("get", base + "capas", undefined, clean(params));
export const getOverdueCapas = (params: any) => call("get", base + "capas/overdue", undefined, clean(params));
export const getCapa = (id: number) => call("get", base + "capas/" + id);
export const saveCapa = (dto: any) => call("post", base + "capas", dto);
/** action: complete-action (actionId, note or evidence), implemented, effectiveness (note, passed), close, cancel (note). */
export const capaAction = (id: number, action: string, extra: any = {}) => call("post", base + "capas/action", { id, action, ...extra });
/**
 * Runs the nightly late-CAPA escalation now (QA and PMO; the same method the 04:00 job calls).
 * Answer: CapaEscalationResultDto { escalated, ceoNotified, firstRun, recorded, skipped }.
 */
export const escalateCapas = () => call("post", base + "capas/escalate");
export const getComplaints = (params: any) => call("get", base + "complaints", undefined, clean(params));
export const getComplaint = (id: number) => call("get", base + "complaints/" + id);
export const saveComplaint = (dto: any) => call("post", base + "complaints", dto);
/** action: review (severity), link-ncr (ncrId), create-capa, respond (response), close. */
export const complaintAction = (id: number, action: string, extra: any = {}) => call("post", base + "complaints/action", { id, action, ...extra });

// audits and management review
export const getAuditPrograms = () => call("get", base + "audit-programs");
export const saveAuditProgram = (dto: any) => call("post", base + "audit-programs", dto);
export const getAudits = (params: any) => call("get", base + "audits", undefined, clean(params));
export const getAudit = (id: number) => call("get", base + "audits/" + id);
export const saveAudit = (dto: any) => call("post", base + "audits", dto);
export const saveAuditFinding = (dto: any) => call("post", base + "audits/findings", dto);
/** action: done (reportDocumentId), close. */
export const auditAction = (id: number, action: string, reportDocumentId?: string | null) => call("post", base + "audits/action", { id, action, reportDocumentId: reportDocumentId || null });
export const getReviews = (params: any) => call("get", base + "management-reviews", undefined, clean(params));
export const getReview = (id: number) => call("get", base + "management-reviews/" + id);
export const createReview = (dto: any) => call("post", base + "management-reviews", dto);
export const saveReviewInput = (dto: any) => call("post", base + "management-reviews/input", dto);
export const saveReviewDecision = (dto: any) => call("post", base + "management-reviews/decision", dto);
/** action: redraft, hold, close (minutesDocumentId). */
export const reviewAction = (id: number, action: string, minutesDocumentId?: string | null) => call("post", base + "management-reviews/action", { id, action, minutesDocumentId: minutesDocumentId || null });
/**
 * Follow-up of one decision of a closed review (R1q D23, review M17). ReviewDecisionActionDto { decisionId, action, note (at most 1000 characters) }.
 * action: done (the responsible person reports the work done; a reviewer who is not the responsible person confirms it), return (a reviewer sends a reported decision back, note = reason),
 * cancel (QA, PMO, CEO; never the owner; note = reason), reopen (note = reason). Answer: the review (ReviewDto).
 */
export const reviewDecisionAction = (decisionId: number, action: string, note?: string) => call("post", base + "management-reviews/decision/action", { decisionId, action, note: note || null });

// release notes and the final data book
export const getReleaseNotes = (params: any) => call("get", base + "release-notes", undefined, clean(params));
export const getReleaseNote = (id: number) => call("get", base + "release-notes/" + id);
export const saveReleaseNote = (dto: any) => call("post", base + "release-notes", dto);
/** action: release, cancel (reason). */
export const releaseNoteAction = (id: number, action: string, reason?: string) => call("post", base + "release-notes/action", { id, action, reason: reason || null });
export const getReleaseChecks = (workOrderId: number) => call("get", base + `work-orders/${workOrderId}/release-checks`);
/** variant: company (every entry) or client (only what the client receives). */
export const getDataBook = (workOrderId: number, variant: string) => call("get", base + `work-orders/${workOrderId}/data-book`, undefined, { variant });

// document control
export const getControlledDocuments = (params: any) => call("get", base + "documents", undefined, clean(params));
export const getMasterList = (params: any) => call("get", base + "documents/master-list", undefined, clean(params));
export const checkDocumentCode = (type: number, department: string, serial?: number | null) => call("get", base + "documents/check-code", undefined, clean({ type, department, serial }));
export const getControlledDocument = (id: number) => call("get", base + "documents/" + id);
export const saveControlledDocument = (dto: any) => call("post", base + "documents", dto);
export const saveRevision = (dto: any) => call("post", base + "documents/revisions", dto);
/** action: review, approve (effectiveDate), obsolete, distribute (recipientUserIds, recipientUnits), acknowledge (distributionId). */
export const revisionAction = (id: number, action: string, extra: any = {}) => call("post", base + "documents/revisions/action", { id, action, ...extra });
export const getRetention = () => call("get", base + "retention");
export const saveRetention = (dto: any) => call("post", base + "retention", dto);
