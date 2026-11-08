import { call, clean, http, saveBlob } from "@/api/ApiClient";

// Package A1: design and development control (ISO 9001 8.3, ISO 10007): design plan, design input register, design reviews (review / verification / validation), the product design dossier
// and the gate status of a BOM or a drawing revision (api/v1/product-bank/design/...). The API checks the role and the segregation of duties of every step again.

const base = "product-bank/design/";

// plans
/** DesignPlanQuery { ProductId?, ProductRevision?, Status?, Contains?, PageNumber, PageSize } */
export const getDesignPlans = (params: any) => call("get", base + "plans", undefined, clean(params));
export const getDesignPlan = (id: number) => call("get", base + "plans/" + id);
/** SaveDesignPlanDto { id?, productId, productRevision, title, scope, validationRequired, validationWaiverReason, stages[{ kind, responsibleRole, responsibleUserId, plannedStart, plannedEnd, note }], interfaces[{ fromGroup, toGroup, subject, responsibleRole, responsibleUserId }], rowVersion } */
export const saveDesignPlan = (dto: any) => call("post", base + "plans", dto);
/** Action: submit, return (note), approve, new-version, void (note), inputs-complete (note, notApplicableKinds). */
export const designPlanAction = (dto: { id: number; action: string; note?: string | null; notApplicableKinds?: number[] }) => call("post", base + "plans/action", dto);
/** Action: start, complete, not-required. */
export const designStageAction = (dto: { planId: number; stageId: number; action: string; note?: string | null }) => call("post", base + "plans/stage-action", dto);
export const getDesignDossier = (productId: number) => call("get", base + "plans/dossier/" + productId);
/** kind: bom (the BOM header id) or drawing (the drawing revision id). */
export const getDesignGate = (kind: "bom" | "drawing", id: number) => call("get", base + `plans/gate/${kind}/${id}`);

// inputs
/** DesignInputQuery { ProductId?, ProductRevision?, Kind?, Status?, IncludeInactive?, Contains? } */
export const getDesignInputs = (params: any) => call("get", base + "inputs", undefined, clean(params));
export const getDesignInput = (id: number) => call("get", base + "inputs/" + id);
/** SaveDesignInputDto { id?, productId, productRevision, kind, statement, source, acceptanceCriterion, standardEdition, previousDesignRef, refText, refRevisionText, failureConsequence, rowVersion } */
export const saveDesignInput = (dto: any) => call("post", base + "inputs", dto);
/** Action: conflict (note, withInputId), resolve (note, a second person), void (note). */
export const designInputAction = (dto: { id: number; action: string; note?: string | null; withInputId?: number | null }) => call("post", base + "inputs/action", dto);
export const copyDesignInputs = (productId: number, fromRevision: number, toRevision: number) => call("post", base + "inputs/copy", undefined, { productId, fromRevision, toRevision });

async function download(url: string, params: any, fileName: string): Promise<string | null> {
  try {
    const response = await http.get(url, { params: clean(params), responseType: "blob" });
    if (response.data?.type?.includes("json")) return JSON.parse(await response.data.text())?.message || "خطا";
    saveBlob(response.data, fileName);
    return null;
  } catch (e: any) {
    return e?.message || "خطا";
  }
}
export const downloadDesignInputTemplate = () => download(base + "inputs/template", {}, "design-inputs-template.xlsx");
export const exportDesignInputs = (productId: number, revision: number) => download(base + "inputs/export", { productId, revision }, `design-inputs-${productId}-${revision}.xlsx`);
/** preview = true only reads the file and judges every row; preview = false creates the inputs, but none when one row is wrong. */
export function importDesignInputs(productId: number, productRevision: number, preview: boolean, file: File) {
  const form = new FormData();
  form.append("ProductId", String(productId));
  form.append("ProductRevision", String(productRevision));
  form.append("Preview", preview ? "true" : "false");
  form.append("file", file);
  return call("post", base + "inputs/import", form);
}

// reviews
/** DesignReviewQuery { ProductId?, ProductRevision?, PlanId?, Type?, Status?, BomHeaderId?, Contains?, PageNumber, PageSize } */
export const getDesignReviews = (params: any) => call("get", base + "reviews", undefined, clean(params));
export const getDesignReview = (id: number) => call("get", base + "reviews/" + id);
export const getDesignMatrix = (id: number) => call("get", base + `reviews/${id}/matrix`);
/** SaveDesignReviewDto { id?, planId, type (1 review, 2 verification, 3 validation), title, scheduledDate, inputIds[], outputs[{ kind (1 drawing, 2 BOM, 3 OPC), revisionId, bomHeaderId, opcRoutingId }], participants[{ userId, roleText }], previousReviewId, rowVersion } */
export const saveDesignReview = (dto: any) => call("post", base + "reviews", dto);
/** Action: hold (minutes, heldDate), close (outcome 1 accepted, 2 accepted with conditions, 3 rejected; note), void (note), repeat. */
export const designReviewAction = (dto: { id: number; action: string; note?: string | null; heldDate?: string | null; minutes?: string | null; outcome?: number }) => call("post", base + "reviews/action", dto);
/** SaveDesignValidationDto { reviewId, method (1 test, 2 trial, 3 simulation), evidence, customerAcceptanceRef, customerAcceptanceNotApplicable, customerAcceptanceNote } */
export const saveDesignValidation = (dto: any) => call("post", base + "reviews/validation", dto);
/** SaveDesignFindingDto { reviewId, id?, description, ownerUserId, dueDate } */
export const saveDesignFinding = (dto: any) => call("post", base + "reviews/findings", dto);
/** Action: done (the owner), close (another person), void (a third person). */
export const designFindingAction = (dto: { id: number; action: string; note?: string | null }) => call("post", base + "reviews/findings/action", dto);
/** SaveDesignTraceDto { reviewId, rows[{ inputId, outputId, result (0 pending, 1 passed, 2 failed), evidence, evidenceRef }] }: judged by an independent participant. */
export const saveDesignTraces = (dto: { reviewId: number; rows: any[] }) => call("post", base + "reviews/traces", dto);
