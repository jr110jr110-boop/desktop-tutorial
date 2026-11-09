import { call, clean, rowsOf } from "@/api/ApiClient";

// Package 9: the AI internal auditor (api/v1/audit): status, rule catalogue, runs, findings and their closure chain, reports, programme, process audits.
// The seat only reads; a finding is closed by QA, PMO and the CEO (three distinct persons) and never by the owner or an AI seat. The API checks every step again.

export { rowsOf };
const base = "audit/";

export const getStatus = () => call("get", base + "status");
export const getRules = () => call("get", base + "rules");
export const getRuns = (limit = 30) => call("get", base + "runs", undefined, clean({ limit }));
export const getRun = (id: number) => call("get", base + "runs/" + id);
export const runNow = (note?: string | null) => call("post", base + "runs", { note: note || null });

/** AusFindingQuery { Status?, Severity?, ProcessCode?, RuleCode?, Text?, OpenOnly?, Overdue?, Mine?, Limit? } */
export const getFindings = (params: any = {}) => call("get", base + "findings", undefined, clean(params));
export const getFinding = (id: number) => call("get", base + "findings/" + id);
/** AusActionDto { findingId, rootCause, correction, correctiveAction, capaId? } */
export const proposeAction = (dto: any) => call("post", base + "findings/action", dto);
/** step: qa, pmo, ceo, void-qa, void-pmo, void-ceo; refuse=true returns the finding to Open (a reason is required) */
export const stepFinding = (findingId: number, step: string, refuse = false, note?: string | null) => call("post", base + "findings/step", { findingId, step, refuse, note: note || null });
/** AusManualFindingDto { auditId, checkId?, title, description, standard, clause, severity (1..3), ownerUserId?, objectNumber? } */
export const createManualFinding = (dto: any) => call("post", base + "findings/manual", dto);
export const manualRecheck = (findingId: number, passed: boolean, note?: string | null) => call("post", base + "findings/manual-recheck", { findingId, passed, note: note || null });

export const getReports = (kind?: number | null, limit = 30) => call("get", base + "reports", undefined, clean({ kind, limit }));
export const getReport = (id: number) => call("get", base + "reports/" + id);
export const generateReport = (kind: number, day?: string | null) => call("post", base + "reports", { kind, day: day || null });

/** accepted deviations: AusAcceptanceProposeDto { ruleCode, objectKey, objectNumber?, reason, expiresOn }; step: pmo, ceo (refuse needs a reason) */
export const getAcceptances = () => call("get", base + "acceptances");
export const proposeAcceptance = (dto: any) => call("post", base + "acceptances", dto);
export const stepAcceptance = (acceptanceId: number, step: string, refuse = false, note?: string | null) => call("post", base + "acceptances/step", { acceptanceId, step, refuse, note: note || null });

export const getPrograms = () => call("get", base + "programs");
export const getProgram = (id: number) => call("get", base + "programs/" + id);
export const saveProgram = (dto: any) => call("post", base + "programs", dto);
export const defaultProgram = (year: number) => call("post", base + "programs/default/" + year);
/** action: submit (PMO who prepared it), approve / reject (QA, another person) */
export const programAction = (id: number, action: string, note?: string | null) => call("post", base + "programs/action", { id, action, note: note || null });

export const getAudits = (limit = 50) => call("get", base + "process-audits", undefined, clean({ limit }));
export const getAudit = (id: number) => call("get", base + "process-audits/" + id);
/** AusAuditSaveDto { programItemId?, processCode, title, plannedDate, leadAuditorUserId, auditeeUserId?, auditeeUnit? } */
export const createAudit = (dto: any) => call("post", base + "process-audits", dto);
/** action: start, complete (summary), cancel */
export const auditAction = (id: number, action: string, summary?: string | null) => call("post", base + "process-audits/action", { id, action, summary: summary || null });
/** result: 1 conform, 2 non-conform, 3 not applicable */
export const saveCheck = (checkId: number, result: number, note?: string | null) => call("post", base + "process-audits/checks", { checkId, result, note: note || null });
