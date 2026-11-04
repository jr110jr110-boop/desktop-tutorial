import { call, clean, rowsOf } from "@/api/ApiClient";

// PB2: the automatic material balance, project reservations, substitutes and the draft purchase requests (api/v1/project-balance/...).
// The shared client (token on every request, [data, error] results) lives in ApiClient.ts.

export { rowsOf };
const base = "project-balance/";

export const getBalanceProjects = () => call("get", base + "projects");
/** BalanceRunRequestDto { workOrderId?, contractId?, createDrafts } */
export const runBalance = (dto: any) => call("post", base + "run", dto);
export const getBalanceRuns = (workOrderId: number) => call("get", base + "work-order/" + workOrderId + "/runs");
export const getLatestBalance = (workOrderId: number) => call("get", base + "work-order/" + workOrderId + "/latest");
export const getBalanceRun = (id: number) => call("get", base + "runs/" + id);
export const getBalanceDiff = (id: number, against?: number | null) => call("get", base + "runs/" + id + "/diff", undefined, clean({ against }));
/** Creates or updates the draft purchase requests of a run (never submits them). */
export const createBalanceDrafts = (id: number) => call("post", base + "runs/" + id + "/drafts");
export const getNodeMaterial = (nodeId: number) => call("get", base + "wbs/" + nodeId + "/material");
export const getBalanceSettings = () => call("get", base + "settings");
export const saveBalanceSetting = (key: string, value: string) => call("post", base + "settings", { key, value });
export const runKits = () => call("post", base + "kits/run");

// reservations
/** ReservationQuery { workOrderId?, contractId?, itemId?, status?, contains? } */
export const getReservations = (params: any) => call("get", base + "reservations", undefined, clean(params));
/** UpdateReservationDto { id, quantity?, isHard?, warehouseId?, lotId?, serialUnitId?, needDate?, reason } - the reason is required. */
export const updateReservation = (dto: any) => call("post", base + "reservations/update", dto);
export const requestReservationRelease = (id: number, reason: string) => call("post", base + "reservations/release-request", { id, reason });
/** approve: true releases; false sends the request back (a reason is required). Never the person who asked. */
/** A planner lets the balance reserve the item for the project again after a release (a reason is required). */
export const reEnableReservation = (id: number, reason: string) => call("post", base + "reservations/re-enable", { id, reason });
export const decideReservationRelease = (id: number, approve: boolean, reason?: string) => call("post", base + "reservations/release-decision", { id, approve, reason });

// substitutes
/** SubstituteQuery { itemId?, status? } */
export const getSubstitutes = (params: any = {}) => call("get", base + "substitutes", undefined, clean(params));
/** SaveSubstituteDto { id?, itemId, substituteItemId, ratio, scope (1 global, 2 project), workOrderId?, validFrom?, validTo?, reason } */
export const saveSubstitute = (dto: any) => call("post", base + "substitutes/save", dto);
/** action: approve, reject or revoke; approve is never by the proposer. */
export const decideSubstitute = (id: number, action: "approve" | "reject" | "revoke", note?: string) => call("post", base + "substitutes/decide", { id, action, note });
