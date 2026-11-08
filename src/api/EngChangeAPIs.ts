import { call, clean, rowsOf } from "@/api/ApiClient";

// Package A2: engineering change requests (ECR) and orders (ECO), the computed impact, dispositions, tasks, history (api/v1/eng-change/...).
// The shared client (token on every request, [data, error] results) lives in ApiClient.ts.

export { rowsOf };
const base = "eng-change/";

// ---- requests
/** EcRequestQuery { Status?, Contains?, PartNumber? } */
export const getRequests = (params: any = {}) => call("get", base + "requests", undefined, clean(params));
export const getRequest = (id: number) => call("get", base + "requests/" + id);
/** SaveEcRequestDto { id?, title, reason, description?, classification (1 Class I, 2 Class II), urgency (1..3), targets: [{ kind, partNumber?, productId?, bomHeaderId?, opcRoutingId?, deliverableId?, routingRevisionId?, note? }] } */
export const saveRequest = (dto: any) => call("post", base + "requests", dto);
/** action: submit, approve (ENGINEERING, not the originator), reject, cancel */
export const requestAction = (id: number, action: string, note?: string | null) => call("post", base + "requests/action", { id, action, note: note || null });

// ---- orders
export const getOrders = (params: any = {}) => call("get", base + "orders", undefined, clean(params));
export const getOrder = (id: number) => call("get", base + "orders/" + id);
/** SaveEcOrderDto { id?, requestId? (new), title, summary, effectiveFromOrderNumber, effectiveFromSerial, effectiveFromDate, effectivityNote } */
export const saveOrder = (dto: any) => call("post", base + "orders", dto);
/** kind: 1 BOM, 2 OPC, 3 drawing revision, 4 MES routing revision; action: 1 new revision, 2 obsolete */
export const addChange = (orderId: number, kind: number, targetId: number, action = 1, note?: string | null) => call("post", base + "orders/changes", { orderId, kind, targetId, action, note: note || null });
export const removeChange = (orderId: number, changeId: number) => call("post", base + "orders/changes/remove", { orderId, changeId });
/** Makes a draft copy of the revision in force (BOM or OPC) and names it in the order. */
export const newRevisionOf = (orderId: number, kind: number, sourceId: number, note?: string | null) => call("post", base + "orders/new-revision", { orderId, kind, sourceId, note: note || null });
export const saveDisposition = (dto: any) => call("post", base + "orders/dispositions", dto);
export const bulkDisposition = (dto: any) => call("post", base + "orders/dispositions/bulk", dto);
/** action: refresh-impact, submit, approve-engineering, approve-qa, return, reject, cancel, close */
export const orderAction = (id: number, action: string, note?: string | null) => call("post", base + "orders/action", { id, action, note: note || null });

// ---- tasks
/** action: done (owner), verify (QA, not the person who did it) */
export const taskAction = (id: number, action: string, note?: string | null) => call("post", base + "tasks/action", { id, action, note: note || null });
export const getMyTasks = () => call("get", base + "tasks/mine");

// ---- history and gate
export const getHistory = (partNumber: string) => call("get", base + "history", undefined, { partNumber });
export const gateCheck = (kind: number, targetId: number, action = 1) => call("get", base + "gate-check", undefined, { kind, targetId, action });
