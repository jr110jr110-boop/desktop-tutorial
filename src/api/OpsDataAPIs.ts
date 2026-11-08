import { call, clean } from "@/api/ApiClient";

// PB8: operational data control (plausibility gates, data-quality page, project engineering readiness gate).
const b = "opsdata/";

export const getOpsFindings = (params: any) => call("get", b + "findings", undefined, clean(params));
export const getOpsSummary = (unit?: number | null) => call("get", b + "summary", undefined, clean({ unit }));
export const getOpsRules = () => call("get", b + "rules");
export const evaluateOpsDocument = (documentType: string, documentId: number) => call("get", b + "evaluate", undefined, { documentType, documentId });
/** resolution: "corrected" (needs correctiveDocumentId) or "verified" (the data is right; a supervisor, not the creator). */
export const resolveOpsFinding = (id: number, resolution: string, reason: string, correctiveDocumentId?: number | null) =>
  call("post", b + "findings/resolve", { id, resolution, reason, correctiveDocumentId: correctiveDocumentId || null });
export const getOpsExceptions = (params: any) => call("get", b + "exceptions", undefined, clean(params));
export const requestOpsException = (findingId: number, justification: string) => call("post", b + "exceptions", { findingId, justification });
export const decideOpsException = (id: number, approve: boolean, reason: string) => call("post", b + "exceptions/decide", { id, approve, reason });

export const getReadiness = (params: { workOrderId?: number | null; productId?: number | null }) => call("get", b + "readiness", undefined, clean(params));
export const getReadinessSummary = () => call("get", b + "readiness/summary");
export const getReadinessObligations = (workOrderId?: number | null, openOnly = true) => call("get", b + "readiness/obligations", undefined, clean({ workOrderId, openOnly }));
export const getReadinessExceptions = (workOrderId?: number | null) => call("get", b + "readiness/exceptions", undefined, clean({ workOrderId }));
export const requestReadinessException = (workOrderId: number, productId: number, justification: string) => call("post", b + "readiness/exceptions", { workOrderId, productId, justification });
export const decideReadinessException = (id: number, approve: boolean, reason: string) => call("post", b + "readiness/exceptions/decide", { id, approve, reason });
