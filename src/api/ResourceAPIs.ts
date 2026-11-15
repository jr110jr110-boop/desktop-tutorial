import { call, clean, rowsOf } from "@/api/ApiClient";
import { getActivities, getCalendars, getChanges } from "@/api/ProjectControlAPIs";

// Project control, priority 4 (phases 4a + 4b): the resource pool, the standard rates, the resource plan with its assignments, the budget view and the resources of a baseline
// (api/v1/projectcontrol/resources, resource-rates, resource-plans, baselines/{id}/resources; ProjectControlResourceController in the API).
//
// THIS IS THE ONLY FILE THAT KNOWS THE FIELD NAMES OF THE API. Every answer is read once by a mapper below into the view models of this file (the pages and components use only those),
// and every request body is built here from the same view models. When a DTO field is renamed on the server, only the mappers and the request builders of this file change.
// Enums are numbers in the JSON (PcResourceEntities.cs); their Persian names are in composables/resources.ts.
// A rate or an amount that could show one person's pay arrives as null with a hidden flag (project rule 6); the view models keep `rate: null, rateHidden: true` and the pages write «محرمانه».

const base = "projectcontrol/";

/** The same shape as the `call` helper of ApiClient: [value, null] or [null, error]. */
export type ApiResult<T> = [T, null] | [null, Error];

const num = (v: any): number => (v === null || v === undefined || v === "" ? 0 : Number(v));
const numOrNull = (v: any): number | null => (v === null || v === undefined || v === "" ? null : Number(v));
const str = (v: any): string => (v === null || v === undefined ? "" : String(v));
const strOrNull = (v: any): string | null => (v === null || v === undefined || v === "" ? null : String(v));

async function typed<T>(promise: Promise<[any, any]>, map: (raw: any) => T): Promise<ApiResult<T>> {
  const [data, error] = await promise;
  if (error) return [null, error];
  return [map(data), null];
}

// ---------------------------------------------------------------- view models

export interface Resource {
  id: number;
  code: string;
  title: string;
  kind: number;
  unit: number;
  capacity: number;
  calendarId: number | null;
  calendarName: string | null;
  responsibleUnit: string | null;
  employeeId: number | null;
  workCenterId: number | null;
  providerId: number | null;
  rateResourceId: number | null;
  rateResourceCode: string | null;
  isActive: boolean;
  notes: string | null;
  /** on an assignment of some plan: it can be deactivated but not deleted */
  hasAssignments: boolean;
  /** the resource may carry a rate of its own (not an employee-linked one) */
  canCarryRate: boolean;
  /** the approved rate that applies today; null when there is none or when it is hidden */
  effectiveRate: number | null;
  rateHidden: boolean;
  rowVersion: string | null;
}

/** What the form sends for a resource (id null: a new one). */
export interface ResourceInput {
  id: number | null;
  code: string;
  title: string;
  kind: number;
  unit: number;
  capacity: number;
  calendarId: number | null;
  responsibleUnit: string | null;
  employeeId: number | null;
  workCenterId: number | null;
  providerId: number | null;
  rateResourceId: number | null;
  isActive: boolean;
  notes: string | null;
  rowVersion: string | null;
}

export interface ResourceQuery {
  kind?: number | null;
  isActive?: boolean | null;
  contains?: string | null;
}

export interface ResourceRate {
  id: number;
  resourceId: number;
  resourceCode: string;
  resourceTitle: string;
  rate: number | null;
  rateHidden: boolean;
  effectiveFrom: string;
  status: number;
  makerUserId: number;
  makerName: string | null;
  submittedAt: string | null;
  approvedByUserId: number | null;
  approvedByName: string | null;
  approvedAt: string | null;
  rejectedAt: string | null;
  rejectReason: string | null;
  rowVersion: string | null;
}

export interface RateQuery {
  resourceId?: number | null;
  status?: number | null;
}

export interface RateInput {
  id: number | null;
  resourceId: number;
  rate: number;
  /** yyyy-mm-dd */
  effectiveFrom: string;
  rowVersion: string | null;
}

export interface PlanAssignment {
  id: number;
  planId: number;
  activityId: number;
  activityCode: string;
  activityTitle: string;
  resourceId: number;
  resourceCode: string;
  resourceTitle: string;
  resourceKind: number;
  resourceUnit: number;
  quantity: number;
  distribution: number;
  responsibleRole: string | null;
  /** 1 agent, 2 human, 3 import */
  source: number;
}

export interface ResourcePlan {
  id: number;
  contractId: number;
  contractCode: string;
  version: number;
  status: number;
  makerUserId: number;
  makerName: string | null;
  preparedByAgent: boolean;
  changeRequestId: number | null;
  submittedAt: string | null;
  approvedAt: string | null;
  approvedByUserId: number | null;
  approvedByName: string | null;
  returnedAt: string | null;
  returnReason: string | null;
  /** the project manager of the contract (the one person who approves); null: the PMO approves */
  approverUserId: number | null;
  /** who will really decide: the project manager, or null (the PMO) when the contract has none or the manager made or edited the plan */
  deciderUserId: number | null;
  deciderName: string | null;
  /** the server says the plan can be edited now (draft or returned) */
  editable: boolean;
  activitiesNeedingResource: number;
  unassignedCount: number;
  markedUnassignedActivityIds: number[];
  assignments: PlanAssignment[];
  rowVersion: string | null;
}

/** One line of the bulk save of the resources of an activity. */
export interface AssignmentLineInput {
  resourceId: number;
  quantity: number;
  distribution: number | null;
  responsibleRole?: string | null;
}

export interface BudgetLine {
  assignmentId: number;
  activityId: number;
  activityCode: string;
  resourceId: number;
  resourceCode: string;
  resourceTitle: string;
  resourceClass: number;
  quantity: number;
  rate: number | null;
  amount: number | null;
  rateHidden: boolean;
  noApprovedRate: boolean;
  note: string | null;
}

export interface BudgetGroup {
  resourceId: number | null;
  resourceCode: string | null;
  resourceTitle: string | null;
  resourceClass: number;
  quantity: number;
  amount: number;
  lineCount: number;
}

export interface Budget {
  planId: number;
  contractId: number;
  asOf: string;
  lines: BudgetLine[];
  byResource: BudgetGroup[];
  byClass: BudgetGroup[];
  total: number;
  /** lines without an approved rate (not in the totals) */
  noRateCount: number;
  /** lines left out of the totals for this reader because their cost could show one person's pay */
  hiddenLineCount: number;
  rateHidden: boolean;
}

export interface BaselineResource {
  baselineId: number;
  activityId: number;
  activityCode: string;
  resourceId: number;
  resourceCode: string;
  quantity: number;
  distribution: number;
  rateAtBaseline: number | null;
  rateHidden: boolean;
}

// ---------------------------------------------------------------- mappers (the one place of field names)

function toResource(r: any): Resource {
  return {
    id: num(r.id), code: str(r.code), title: str(r.title), kind: num(r.kind), unit: num(r.unit), capacity: num(r.capacity),
    calendarId: numOrNull(r.calendarId), calendarName: strOrNull(r.calendarName), responsibleUnit: strOrNull(r.responsibleUnit),
    employeeId: numOrNull(r.employeeId), workCenterId: numOrNull(r.workCenterId), providerId: numOrNull(r.providerId),
    rateResourceId: numOrNull(r.rateResourceId), rateResourceCode: strOrNull(r.rateResourceCode),
    isActive: !!r.isActive, notes: strOrNull(r.notes), hasAssignments: !!r.hasAssignments, canCarryRate: !!r.canCarryRate,
    effectiveRate: numOrNull(r.effectiveRate), rateHidden: !!r.rateHidden, rowVersion: strOrNull(r.rowVersion),
  };
}

function toRate(r: any): ResourceRate {
  return {
    id: num(r.id), resourceId: num(r.resourceId), resourceCode: str(r.resourceCode), resourceTitle: str(r.resourceTitle), rate: numOrNull(r.rate), rateHidden: !!r.rateHidden,
    effectiveFrom: str(r.effectiveFrom), status: num(r.status), makerUserId: num(r.makerUserId), makerName: strOrNull(r.makerName), submittedAt: strOrNull(r.submittedAt),
    approvedByUserId: numOrNull(r.approvedByUserId), approvedByName: strOrNull(r.approvedByName), approvedAt: strOrNull(r.approvedAt), rejectedAt: strOrNull(r.rejectedAt),
    rejectReason: strOrNull(r.rejectReason), rowVersion: strOrNull(r.rowVersion),
  };
}

function toAssignment(r: any): PlanAssignment {
  return {
    id: num(r.id), planId: num(r.planId), activityId: num(r.activityId), activityCode: str(r.activityCode), activityTitle: str(r.activityTitle), resourceId: num(r.resourceId),
    resourceCode: str(r.resourceCode), resourceTitle: str(r.resourceTitle), resourceKind: num(r.resourceKind), resourceUnit: num(r.resourceUnit), quantity: num(r.quantity),
    distribution: num(r.distribution) || 1, responsibleRole: strOrNull(r.responsibleRole), source: num(r.source),
  };
}

function toPlan(r: any): ResourcePlan {
  return {
    id: num(r.id), contractId: num(r.contractId), contractCode: str(r.contractCode), version: num(r.version), status: num(r.status), makerUserId: num(r.makerUserId),
    makerName: strOrNull(r.makerName), preparedByAgent: !!r.preparedByAgent, changeRequestId: numOrNull(r.changeRequestId), submittedAt: strOrNull(r.submittedAt),
    approvedAt: strOrNull(r.approvedAt), approvedByUserId: numOrNull(r.approvedByUserId), approvedByName: strOrNull(r.approvedByName), returnedAt: strOrNull(r.returnedAt),
    returnReason: strOrNull(r.returnReason), approverUserId: numOrNull(r.approverUserId), deciderUserId: numOrNull(r.deciderUserId), deciderName: strOrNull(r.deciderName), editable: !!r.editable, activitiesNeedingResource: num(r.activitiesNeedingResource),
    unassignedCount: num(r.unassignedCount), markedUnassignedActivityIds: Array.isArray(r.markedUnassignedActivityIds) ? r.markedUnassignedActivityIds.map(Number) : [],
    assignments: Array.isArray(r.assignments) ? r.assignments.map(toAssignment) : [], rowVersion: strOrNull(r.rowVersion),
  };
}

function toBudgetGroup(r: any): BudgetGroup {
  return { resourceId: numOrNull(r.resourceId), resourceCode: strOrNull(r.resourceCode), resourceTitle: strOrNull(r.resourceTitle), resourceClass: num(r.resourceClass), quantity: num(r.quantity), amount: num(r.amount), lineCount: num(r.lineCount) };
}

function toBudget(r: any): Budget {
  return {
    planId: num(r.planId), contractId: num(r.contractId), asOf: str(r.asOf),
    lines: (Array.isArray(r.lines) ? r.lines : []).map((l: any): BudgetLine => ({
      assignmentId: num(l.assignmentId), activityId: num(l.activityId), activityCode: str(l.activityCode), resourceId: num(l.resourceId), resourceCode: str(l.resourceCode),
      resourceTitle: str(l.resourceTitle), resourceClass: num(l.resourceClass), quantity: num(l.quantity), rate: numOrNull(l.rate), amount: numOrNull(l.amount),
      rateHidden: !!l.rateHidden, noApprovedRate: !!l.noApprovedRate, note: strOrNull(l.note),
    })),
    byResource: (Array.isArray(r.byResource) ? r.byResource : []).map(toBudgetGroup), byClass: (Array.isArray(r.byClass) ? r.byClass : []).map(toBudgetGroup),
    total: num(r.total), noRateCount: num(r.noRateCount), hiddenLineCount: num(r.hiddenLineCount), rateHidden: !!r.rateHidden,
  };
}

function toBaselineResource(r: any): BaselineResource {
  return {
    baselineId: num(r.baselineId), activityId: num(r.activityId), activityCode: str(r.activityCode), resourceId: num(r.resourceId), resourceCode: str(r.resourceCode),
    quantity: num(r.quantity), distribution: num(r.distribution) || 1, rateAtBaseline: numOrNull(r.rateAtBaseline), rateHidden: !!r.rateHidden,
  };
}

// ---------------------------------------------------------------- the pool

export const getResources = (q: ResourceQuery = {}): Promise<ApiResult<Resource[]>> =>
  typed(call("get", base + "resources", undefined, clean({ PageSize: 0, Kind: q.kind, IsActive: q.isActive, Contains: q.contains })), (d) => rowsOf(d).map(toResource));

export const saveResource = (r: ResourceInput): Promise<ApiResult<Resource>> =>
  typed(call("post", base + "resources", {
    id: r.id || 0, code: r.code, title: r.title, kind: r.kind, unit: r.unit, capacity: r.capacity, calendarId: r.calendarId, responsibleUnit: r.responsibleUnit,
    employeeId: r.employeeId, workCenterId: r.workCenterId, providerId: r.providerId, rateResourceId: r.rateResourceId, isActive: r.isActive, notes: r.notes, rowVersion: r.rowVersion,
  }), toResource);

export const setResourceActive = (id: number, active: boolean): Promise<ApiResult<Resource>> =>
  typed(call("post", base + "resources/" + id + (active ? "/activate" : "/deactivate")), toResource);

export const deleteResource = (id: number): Promise<ApiResult<boolean>> => typed(call("delete", base + "resources/" + id), () => true);

/** The editable copy of a resource for the form. */
export const resourceInput = (r: Resource | null): ResourceInput =>
  r
    ? { id: r.id, code: r.code, title: r.title, kind: r.kind, unit: r.unit, capacity: r.capacity, calendarId: r.calendarId, responsibleUnit: r.responsibleUnit, employeeId: r.employeeId,
        workCenterId: r.workCenterId, providerId: r.providerId, rateResourceId: r.rateResourceId, isActive: r.isActive, notes: r.notes, rowVersion: r.rowVersion }
    : { id: null, code: "", title: "", kind: 1, unit: 1, capacity: 1, calendarId: null, responsibleUnit: null, employeeId: null, workCenterId: null, providerId: null, rateResourceId: null, isActive: true, notes: null, rowVersion: null };

// ---------------------------------------------------------------- the standard rates

export const getRates = (q: RateQuery = {}): Promise<ApiResult<ResourceRate[]>> =>
  typed(call("get", base + "resource-rates", undefined, clean({ PageSize: 0, ResourceId: q.resourceId, Status: q.status })), (d) => rowsOf(d).map(toRate));

/** Proposes a rate (a draft), or changes a draft one (id set). */
export const saveRate = (r: RateInput): Promise<ApiResult<ResourceRate>> =>
  typed(call("post", base + "resource-rates", { id: r.id, resourceId: r.resourceId, rate: r.rate, effectiveFrom: r.effectiveFrom, rowVersion: r.rowVersion }), toRate);

/** action: submit (the maker), approve, reject (a second person; the reason is the note). */
export const rateAction = (id: number, action: "submit" | "approve" | "reject", note?: string | null): Promise<ApiResult<ResourceRate>> =>
  typed(call("post", base + "resource-rates/action", { id, action, note: note || null }), toRate);

// ---------------------------------------------------------------- the plan

/** The plan of a contract: the open one, else the newest approved one, or the named version. A contract without a plan answers 404 (see `isNoPlan`). */
export const getPlan = (contractId: number, version?: number | null): Promise<ApiResult<ResourcePlan>> =>
  typed(call("get", base + "resource-plans/contract/" + contractId, undefined, clean({ version })), toPlan);

export const getPlanVersions = (contractId: number): Promise<ApiResult<ResourcePlan[]>> =>
  typed(call("get", base + "resource-plans/contract/" + contractId + "/versions"), (d) => rowsOf(d).map(toPlan));

/** True when the error is the answer «no resource plan for this contract yet» (HTTP 404). */
export const isNoPlan = (error: any): boolean => error?.response?.status === 404;

export const openPlan = (contractId: number, changeRequestId?: number | null): Promise<ApiResult<ResourcePlan>> =>
  typed(call("post", base + "resource-plans/open", { contractId, changeRequestId: changeRequestId || null }), toPlan);

/** Bulk upsert of the resources of one activity; removeOthers deletes the rows of the activity that are not listed. */
export const saveAssignments = (planId: number, activityId: number, lines: AssignmentLineInput[], removeOthers = false): Promise<ApiResult<ResourcePlan>> =>
  typed(call("post", base + "resource-plans/assignments", {
    planId, activityId, removeOthers, lines: lines.map((l) => ({ resourceId: l.resourceId, quantity: l.quantity, distribution: l.distribution, responsibleRole: l.responsibleRole ?? null })),
  }), toPlan);

export const deleteAssignment = (id: number): Promise<ApiResult<ResourcePlan>> => typed(call("delete", base + "resource-plans/assignments/" + id), toPlan);

/** Marks (or, with unmark, un-marks) activities as deliberately without a resource; allRemaining marks every activity that is still unassigned. */
export const markUnassigned = (planId: number, opts: { activityIds?: number[]; allRemaining?: boolean; unmark?: boolean }): Promise<ApiResult<ResourcePlan>> =>
  typed(call("post", base + "resource-plans/mark-unassigned", { planId, activityIds: opts.activityIds ?? null, allRemaining: !!opts.allRemaining, unmark: !!opts.unmark }), toPlan);

/** action: submit (the planner), approve (the project manager of the contract), return (the project manager; the reason is the note). */
export const planAction = (plan: ResourcePlan, action: "submit" | "approve" | "return", note?: string | null): Promise<ApiResult<ResourcePlan>> =>
  typed(call("post", base + "resource-plans/action", { id: plan.id, action, note: note || null, rowVersion: plan.rowVersion }), toPlan);

/** Throws away an open plan opened by mistake (no change request, no baseline); the reason is optional. The answer is the plan as it was. */
export const discardPlan = (planId: number, reason?: string | null): Promise<ApiResult<ResourcePlan>> =>
  typed(call("post", base + "resource-plans/discard", { id: planId, reason: reason || null }), toPlan);

export const getBudget = (planId: number, asOf?: string | null): Promise<ApiResult<Budget>> =>
  typed(call("get", base + "resource-plans/" + planId + "/budget", undefined, clean({ asOf })), toBudget);

// ---------------------------------------------------------------- baseline

export const getBaselineResources = (baselineId: number): Promise<ApiResult<BaselineResource[]>> =>
  typed(call("get", base + "baselines/" + baselineId + "/resources"), (d) => rowsOf(d).map(toBaselineResource));

// ---------------------------------------------------------------- lookups of the pages (the reads of other modules a page here needs)

/** An activity of the contract, as the plan table needs it. Only the project control readers may read the activity list (the finance and audit readers see the assigned activities only). */
export interface PlanActivity {
  id: number;
  code: string;
  title: string;
  wbsCode: string | null;
  durationDays: number;
  responsibleUnit: string | null;
}

export const getPlanActivities = (contractId: number): Promise<ApiResult<PlanActivity[]>> =>
  typed(getActivities({ ContractId: contractId }), (d) =>
    rowsOf(d).map((a: any): PlanActivity => ({ id: num(a.id), code: str(a.code), title: str(a.title), wbsCode: strOrNull(a.wbsCode), durationDays: num(a.durationDays), responsibleUnit: strOrNull(a.responsibleUnit) })));

export interface CalendarOption {
  id: number;
  name: string;
}

/** The working calendars for the resource form (read when the form opens; planning and the PMO, who edit the pool, may read them). */
export const getCalendarOptions = (): Promise<ApiResult<CalendarOption[]>> =>
  typed(getCalendars(), (d) => rowsOf(d).map((c: any): CalendarOption => ({ id: num(c.id), name: str(c.name) })));

/** A change request that may reopen the resource plan after a baseline: approved and being carried out (the server also needs that it built its own baseline and that no plan uses it yet). */
export interface OpenableChange {
  id: number;
  number: string;
  description: string;
}

export const getOpenableChanges = (contractId: number): Promise<ApiResult<OpenableChange[]>> =>
  typed(getChanges({ ContractId: contractId, Status: 4 }), (d) =>
    rowsOf(d).filter((c: any) => c.decision === undefined || Number(c.decision) === 1).map((c: any): OpenableChange => ({ id: num(c.id), number: str(c.number), description: str(c.description) })));

// ---------------------------------------------------------------- phase 4c: the load of the resources, the over-allocations and the levelling proposals
// (api/v1/projectcontrol/resource-load, resource-overloads, leveling-proposals). No money anywhere in these answers. Dates are ISO (yyyy-mm-dd); the pages write them in Shamsi.

export interface LoadItem {
  contractId: number;
  contractCode: string;
  activityId: number;
  activityCode: string;
  activityTitle: string;
  load: number;
  /** the load comes from a plan that is not approved yet («پیش‌نویس») */
  isDraft: boolean;
}

export interface LoadBucket {
  start: string;
  end: string;
  load: number;
  draftLoad: number;
  capacity: number;
  /** the sum of the daily excess over the capacity */
  over: number;
  items: LoadItem[];
}

export interface LoadRow {
  resourceId: number;
  code: string;
  title: string;
  kind: number;
  unit: number;
  capacity: number;
  peakLoad: number;
  totalOver: number;
  buckets: LoadBucket[];
}

export interface ResourceLoad {
  from: string;
  to: string;
  bucket: "day" | "week";
  includeOpen: boolean;
  resources: LoadRow[];
}

export interface LoadQuery {
  resourceId?: number | null;
  /** yyyy-mm-dd */
  from?: string | null;
  to?: string | null;
  bucket?: "day" | "week";
  includeOpen?: boolean;
}

export interface LevelingProposal {
  id: number;
  overloadId: number;
  contractId: number;
  contractCode: string;
  activityId: number;
  activityCode: string;
  activityTitle: string;
  /** 1 within the float, 2 beyond the float */
  kind: number;
  currentStart: string;
  currentFinish: string;
  proposedStart: string;
  proposedFinish: string;
  shiftDays: number;
  criticalImpactDays: number;
  needsChangeRequest: boolean;
  note: string;
  /** 1 proposed, 2 applied, 3 rejected, 4 superseded, 5 closed without a revert */
  status: number;
  preparedByAgent: boolean;
  /** applied and not yet confirmed by the second person */
  pendingSecondPerson: boolean;
  reverted: boolean;
  applierUserId: number | null;
  applierName: string | null;
  appliedAt: string | null;
  confirmedByUserId: number | null;
  confirmedByName: string | null;
  confirmedAt: string | null;
  rejectedByUserId: number | null;
  rejectedByName: string | null;
  rejectedAt: string | null;
  rejectReason: string | null;
  createDate: string;
  rowVersion: string | null;
  /** what the server tells THIS caller: null means the action is allowed now, else the Persian reason (the rules the action enforces, the lock of the project and applier != confirmer included) */
  applyBlockedReason: string | null;
  rejectBlockedReason: string | null;
  confirmBlockedReason: string | null;
  closeBlockedReason: string | null;
}

export interface ResourceOverload {
  id: number;
  resourceId: number;
  resourceCode: string;
  resourceTitle: string;
  resourceUnit: number;
  fromDate: string;
  toDate: string;
  peakLoad: number;
  peakCapacity: number;
  /** 1 open, 2 resolved, 3 accepted, 4 lapsed (the days passed without a fix) */
  status: number;
  detectedAt: string;
  resolvedAt: string | null;
  acceptedByUserId: number | null;
  acceptedByName: string | null;
  acceptedAt: string | null;
  acceptReason: string | null;
  proposals: LevelingProposal[];
  rowVersion: string | null;
  /** null: the caller may accept it now; else the Persian reason */
  acceptBlockedReason: string | null;
}

/** One page of a paged list. */
export interface Paged<T> {
  rows: T[];
  page: number;
  totalItems: number;
  totalPages: number;
}

export interface OverloadQuery {
  resourceId?: number | null;
  status?: number | null;
  page?: number;
  pageSize?: number;
}

export interface ProposalQuery {
  overloadId?: number | null;
  contractId?: number | null;
  status?: number | null;
}

function toLoadBucket(b: any): LoadBucket {
  return {
    start: str(b.start).slice(0, 10), end: str(b.end).slice(0, 10), load: num(b.load), draftLoad: num(b.draftLoad), capacity: num(b.capacity), over: num(b.over),
    items: (Array.isArray(b.items) ? b.items : []).map((i: any): LoadItem => ({
      contractId: num(i.contractId), contractCode: str(i.contractCode), activityId: num(i.activityId), activityCode: str(i.activityCode), activityTitle: str(i.activityTitle), load: num(i.load), isDraft: !!i.isDraft,
    })),
  };
}

function toResourceLoad(r: any): ResourceLoad {
  return {
    from: str(r.from).slice(0, 10), to: str(r.to).slice(0, 10), bucket: r.bucket === "week" ? "week" : "day", includeOpen: !!r.includeOpen,
    resources: (Array.isArray(r.resources) ? r.resources : []).map((x: any): LoadRow => ({
      resourceId: num(x.resourceId), code: str(x.code), title: str(x.title), kind: num(x.kind), unit: num(x.unit), capacity: num(x.capacity), peakLoad: num(x.peakLoad), totalOver: num(x.totalOver),
      buckets: (Array.isArray(x.buckets) ? x.buckets : []).map(toLoadBucket),
    })),
  };
}

function toProposal(r: any): LevelingProposal {
  return {
    id: num(r.id), overloadId: num(r.overloadId), contractId: num(r.contractId), contractCode: str(r.contractCode), activityId: num(r.activityId), activityCode: str(r.activityCode), activityTitle: str(r.activityTitle),
    kind: num(r.kind), currentStart: str(r.currentStart).slice(0, 10), currentFinish: str(r.currentFinish).slice(0, 10), proposedStart: str(r.proposedStart).slice(0, 10), proposedFinish: str(r.proposedFinish).slice(0, 10),
    shiftDays: num(r.shiftDays), criticalImpactDays: num(r.criticalImpactDays), needsChangeRequest: !!r.needsChangeRequest, note: str(r.note), status: num(r.status), preparedByAgent: !!r.preparedByAgent,
    pendingSecondPerson: !!r.pendingSecondPerson, reverted: !!r.reverted, applierUserId: numOrNull(r.applierUserId), applierName: strOrNull(r.applierName), appliedAt: strOrNull(r.appliedAt),
    confirmedByUserId: numOrNull(r.confirmedByUserId), confirmedByName: strOrNull(r.confirmedByName), confirmedAt: strOrNull(r.confirmedAt), rejectedByUserId: numOrNull(r.rejectedByUserId),
    rejectedByName: strOrNull(r.rejectedByName), rejectedAt: strOrNull(r.rejectedAt), rejectReason: strOrNull(r.rejectReason), createDate: str(r.createDate), rowVersion: strOrNull(r.rowVersion),
    applyBlockedReason: strOrNull(r.applyBlockedReason), rejectBlockedReason: strOrNull(r.rejectBlockedReason), confirmBlockedReason: strOrNull(r.confirmBlockedReason), closeBlockedReason: strOrNull(r.closeBlockedReason),
  };
}

function toOverload(r: any): ResourceOverload {
  return {
    id: num(r.id), resourceId: num(r.resourceId), resourceCode: str(r.resourceCode), resourceTitle: str(r.resourceTitle), resourceUnit: num(r.resourceUnit) || 1, fromDate: str(r.fromDate).slice(0, 10), toDate: str(r.toDate).slice(0, 10),
    peakLoad: num(r.peakLoad), peakCapacity: num(r.peakCapacity), status: num(r.status), detectedAt: str(r.detectedAt), resolvedAt: strOrNull(r.resolvedAt), acceptedByUserId: numOrNull(r.acceptedByUserId),
    acceptedByName: strOrNull(r.acceptedByName), acceptedAt: strOrNull(r.acceptedAt), acceptReason: strOrNull(r.acceptReason), proposals: (Array.isArray(r.proposals) ? r.proposals : []).map(toProposal), rowVersion: strOrNull(r.rowVersion),
    acceptBlockedReason: strOrNull(r.acceptBlockedReason),
  };
}

/** The load of one resource (or of all) over the period, against the capacity. The period is at most 370 days; the server refuses a longer one in Persian. */
export const getResourceLoad = (q: LoadQuery = {}): Promise<ApiResult<ResourceLoad>> =>
  typed(call("get", base + "resource-load", undefined, clean({ ResourceId: q.resourceId, From: q.from, To: q.to, Bucket: q.bucket, IncludeOpen: q.includeOpen ? true : undefined })), toResourceLoad);

const toPage = <T>(d: any, map: (r: any) => T): Paged<T> => ({
  rows: rowsOf(d).map(map), page: num(d?.currentPage) || 1, totalItems: num(d?.totalItems ?? d?.totalCount) || rowsOf(d).length, totalPages: num(d?.totalPages) || 1,
});

/** One page of the over-allocations (never the whole list). */
export const getOverloads = (q: OverloadQuery = {}): Promise<ApiResult<Paged<ResourceOverload>>> =>
  typed(call("get", base + "resource-overloads", undefined, clean({ PageNumber: q.page || 1, PageSize: q.pageSize || 10, ResourceId: q.resourceId, Status: q.status })), (d) => toPage(d, toOverload));

/** One over-allocation by id (the link of a gate inbox task). */
export const getOverload = (id: number): Promise<ApiResult<ResourceOverload>> => typed(call("get", base + "resource-overloads/" + id), toOverload);

/** The PMO accepts an over-allocation on purpose; the reason is mandatory. */
export const acceptOverload = (o: ResourceOverload, reason: string): Promise<ApiResult<ResourceOverload>> =>
  typed(call("post", base + "resource-overloads/accept", { id: o.id, reason, rowVersion: o.rowVersion }), toOverload);

/** One levelling proposal by id (the link of a gate inbox task). */
export const getProposal = (id: number): Promise<ApiResult<LevelingProposal>> => typed(call("get", base + "leveling-proposals/" + id), toProposal);

/** apply and reject (the planner), confirm, refuse and close without a revert (a second person, the PMO); reject, refuse and close need a reason. */
export const proposalAction = (p: LevelingProposal, action: "apply" | "reject" | "confirm" | "refuse" | "close", reason?: string | null): Promise<ApiResult<LevelingProposal>> =>
  typed(call("post", base + "leveling-proposals/action", { id: p.id, action, reason: reason || null, rowVersion: p.rowVersion }), toProposal);
