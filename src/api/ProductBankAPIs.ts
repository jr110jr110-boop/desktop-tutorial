import { call, clean, rowsOf } from "@/api/ApiClient";

// PB1: part-number item coding, BOM master with revisions, OPC routings, product bank and project deviations (api/v1/product-bank/...).
// The shared client (token on every request, [data, error] results) lives in ApiClient.ts.

export { rowsOf };
const base = "product-bank/";

// coding: stage dictionary, settings, item by part number, mapping of legacy codes
export const getStages = (includeInactive = false) => call("get", base + "coding/stages", undefined, clean({ includeInactive: includeInactive || null }));
/** SavePbStageDto { id?, code (two letters), titleFa, titleEn, kind (1 operation, 2 QC, 3 transport, 4 storage, 5 inspection hold), isStockable, isActive, sortOrder } */
export const saveStage = (dto: any) => call("post", base + "coding/stages", dto);
export const getSettings = () => call("get", base + "coding/settings");
export const saveSetting = (dto: any) => call("post", base + "coding/settings", dto);
export const previewCode = (partNumber: string, stageCode2?: string | null, title?: string | null) => call("get", base + "coding/preview", undefined, clean({ partNumber, stageCode2, title }));
/** PbCreateItemDto { partNumber, stageCode2?, partTitle?, unit?, material?, standard?, confirmNearDuplicate } */
/** One-time upgrade step (PMO): old items whose code or MESC code is a part number get the part number. */
export const adoptPartNumbers = () => call("post", base + "coding/adopt-part-numbers");
export const createItem = (dto: any) => call("post", base + "coding/items", dto);
/** PbMappingQuery { Status? (0 draft, 1 approved, 2 rejected), Contains?, MinConfidence?, PageNumber, PageSize } */
export const getMappings = (params: any) => call("get", base + "coding/mappings", undefined, clean(params));
export const importMappings = (rows: any[]) => call("post", base + "coding/mappings/import", rows);
export const suggestMappings = () => call("post", base + "coding/mappings/suggest");
export const saveMapping = (dto: any) => call("post", base + "coding/mappings", dto);
/** PbDecideDto { id, approve, note }: engineering, never the person who proposed it. */
export const decideMapping = (dto: any) => call("post", base + "coding/mappings/decide", dto);

// BOM master
export const getBoms = (params: any) => call("get", base + "boms", undefined, clean(params));
export const getBom = (id: number) => call("get", base + "boms/" + id);
export const saveBom = (dto: any) => call("post", base + "boms", dto);
export const submitBom = (id: number) => call("post", base + "boms/submit", { id });
export const returnBom = (id: number, note: string) => call("post", base + "boms/return", { id, note });
/** step: 1 checked, 2 QC checked, 3 manufacturing checked, 4 approved (always in this order, always a different person). */
export const signBom = (id: number, step: number, note?: string) => call("post", base + "boms/sign", { id, step, note });
export const releaseBom = (id: number, effectiveFrom?: string | null) => call("post", base + "boms/release", { id, effectiveFrom: effectiveFrom || null });
export const newBomRevision = (id: number, note: string) => call("post", base + "boms/new-revision", { id, note });
export const obsoleteBom = (id: number, note: string) => call("post", base + "boms/obsolete", { id, note });
export const deleteBomDraft = (id: number) => call("delete", base + "boms/" + id);
export const diffBoms = (from: number, to: number) => call("get", base + "boms/diff", undefined, { from, to });
export const whereUsed = (partNumber: string) => call("get", base + "boms/where-used", undefined, { partNumber });

// OPC routings
export const getRoutings = (params: any) => call("get", base + "routings", undefined, clean(params));
export const getRouting = (id: number) => call("get", base + "routings/" + id);
export const saveRouting = (dto: any) => call("post", base + "routings", dto);
export const submitRouting = (id: number) => call("post", base + "routings/submit", { id });
export const returnRouting = (id: number, note: string) => call("post", base + "routings/return", { id, note });
export const checkRouting = (id: number) => call("post", base + "routings/check", { id });
export const approveRouting = (id: number) => call("post", base + "routings/approve", { id });
export const newRoutingRevision = (id: number, note: string) => call("post", base + "routings/new-revision", { id, note });
export const publishRouting = (id: number) => call("post", base + "routings/publish", { id });
export const deleteRoutingDraft = (id: number) => call("delete", base + "routings/" + id);

// product bank: products, WBS templates (MSPDI), seed
export const getProducts = () => call("get", base + "products");
export const saveProduct = (dto: any) => call("post", base + "products", dto);
export const getProductBank = (id: number) => call("get", base + "products/" + id);
export const importMspdi = (dto: { productId: number; title?: string; xml: string }) => call("post", base + "templates/import-mspdi", dto);
export const getTemplate = (id: number) => call("get", base + "templates/" + id);
export const approveTemplate = (id: number) => call("post", base + "templates/" + id + "/approve");
export const seedFy = () => call("post", base + "seed-fy");

// project copy and deviations
/** On a contract whose project-control plan is locked, changeRequestId is an approved change request that names the work order (used once). */
export const instantiateProduct = (workOrderId: number, productId: number, changeRequestId?: number | null) => call("post", base + "projects/instantiate", { workOrderId, productId, changeRequestId: changeRequestId || null });
/** Generates the project-control WBS (nodes, activities, dependencies) from the approved template; needs the accepted contract review. */
export const generateWbs = (workOrderId: number, changeRequestId?: number | null) => call("post", base + "projects/generate-wbs", { workOrderId, changeRequestId: changeRequestId || null });
export const getBaseline = (workOrderId: number) => call("get", base + "projects/work-order/" + workOrderId);
/** SavePbChangeDto { id?, baselineId, kind (1 add, 2 remove, 3 replace, 4 quantity), targetLineId?, partNumber?, partName?, newQty?, unit?, material?, makeOrBuy?, reason } */
export const saveChange = (dto: any) => call("post", base + "projects/changes", dto);
export const submitChange = (id: number) => call("post", base + "projects/changes/submit", { id });
export const approveChangeEngineering = (id: number) => call("post", base + "projects/changes/approve-engineering", { id });
export const approveChangePlanning = (id: number) => call("post", base + "projects/changes/approve-planning", { id });
export const rejectChange = (id: number, note: string) => call("post", base + "projects/changes/reject", { id, note });
export const applyChange = (id: number, changeRequestId?: number | null) => call("post", base + "projects/changes/apply", { id, changeRequestId: changeRequestId || null });
export const cancelChange = (id: number) => call("post", base + "projects/changes/cancel", { id });
