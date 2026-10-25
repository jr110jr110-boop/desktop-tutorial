import { call, clean } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// After-sales service (api/v1/service, release S1 part B): settings and SLA table, the installed base, service requests, service orders, LTSA contracts, jobs, the monthly protected-labour posting,
// satisfaction surveys, the dashboard and the KPIs. Every answer is { success, message, data }; a rule violation answers HTTP 200 with success=false (the helpers return it as the error).
// Lists are paged ({ data: [], currentPage, totalItems }; read them with rowsOf() or usePagedList()), except the single-record GETs, GET settings, GET sla-rules, GET units/{id}/as-built,
// GET dashboard and GET kpis, which answer one object or a plain array. Enums are numbers (see composables/service.ts). Money is in rial. Dates in the JSON are ISO; the "...Text" fields are Persian (Jalali).
// Payroll-protected amounts (labourAmount, totalCost of an order, warrantyCost*) are null with a *Hidden flag for roles outside ServiceRoles.payrollSeers: show «محرمانه (حقوق)».
// The controllers need only a signed-in user; the "Who" part of each comment is checked inside the services (SvRoles in composables/roles.ts).
// Concurrency: send the rowVersion you read; a stale one is refused with a Persian message.
// Controller routes: api/v1/service (settings, sla-rules, units, requests, contracts, jobs, labour, surveys, dashboard, kpis) and api/v1/service/orders (everything about service orders).
const sv = "service/";

type Method = "get" | "post" | "put" | "delete";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

/** Paging + free-text filter of every list: SlPageQuery { PageNumber (1), PageSize (25, max 500), Contains }. usePagedList() adds PageNumber and PageSize. */
export type ServicePageParams = { PageNumber?: number; PageSize?: number; Contains?: string | null; [key: string]: any };

// ---------------------------------------------------------------- settings and SLA
/**
 * PLAIN ARRAY of SvSettingDto { key, title, text (saved value), effective (the value in force, the default when nothing was saved) }. Keys: CostCenterCode (default AFTERSALES), LabourWorkCenterCode (no default),
 * SurveyLeadDays (default 30), VisitLeadDays (default 30). Who: ServiceRoles.readers.
 */
export const getSvSettings = () => get(sv + "settings");
/** SvSaveSettingDto { key, text }. Returns SvSettingDto. Who: ServiceRoles.settingSetters (CEO). */
export const saveSvSetting = (dto: { key: string; text: string | null }) => post(sv + "settings", dto);
/**
 * PLAIN ARRAY of SvSlaRuleDto { id, priority (1 critical .. 4 low), priorityText, responseHours, resolutionHours (null = none), isActive }. No hours are seeded: a priority without a rule gets no due time (slaMissing on the request).
 * Who: readers.
 */
export const getSvSlaRules = () => get(sv + "sla-rules");
/** SvSaveSlaRuleDto { priority (1..4), responseHours, resolutionHours?, isActive? }: one row per priority (upsert). Returns SvSlaRuleDto. Who: ServiceRoles.slaWriters (SERVICE, CEO). */
export const saveSvSlaRule = (dto: { priority: number; responseHours: number; resolutionHours?: number | null; isActive?: boolean | null }) => post(sv + "sla-rules", dto);

// ---------------------------------------------------------------- installed units
/**
 * params: SvUnitQuery { CustomerId, Status 1..4, NeedsConfirm (system drafts waiting for confirmation), ProductType (SlProductType number 1..9), AsOf } + paging.
 * SvUnitDto { id, tag, title, serialUnitId, serialNumber, productType, productTypeText, customerId, customerName, siteId, siteTitle, contractId, contractCode, workOrderId, workOrderCode, shipmentId, shipmentNumber,
 * handoverDate(Text), commissioningDate(Text), warrantyMonths, warrantyStart(Text), warrantyEnd(Text), warrantySource (0 none, 1 months, 2 guarantee, 3 manual), status, statusText, inWarranty, runningHours,
 * runningHoursAt(Text), decommissioned, isConfirmed, confirmedAtText, note, spares: [{ id, itemId, itemCode, itemName, quantity, source (1 manual, 2 MDR list), note }], spareListDeliverableId,
 * spareListDocumentNumber, spareListDocumentId (guid), spareListSource, spareListSourceText, touchedByMe (the current user created or edited it: the backend refuses their confirm), rowVersion }. Who: readers.
 */
export const getSvUnits = (params?: ServicePageParams) => get(sv + "units", params);
/** One SvUnitDto as of AsOf (ISO date, optional; the status is computed from the dates). Who: readers. */
export const getSvUnit = (id: number, asOf?: string | null) => get(sv + "units/" + id, { AsOf: asOf });
/**
 * SvAsBuiltDto { unitId, tag, trace: SerialTraceDto { unit, lot, ancestors[], children[], lotComponents[], assemblyHistory[], operations[], movements[] } } (the assembly tree of the serial unit through traceability;
 * trace is empty when the unit has no serial unit). Who: readers.
 */
export const getSvAsBuilt = (id: number) => get(sv + `units/${id}/as-built`);
/**
 * SvSaveUnitDto { id?, tag, title, serialUnitId?, productType?, customerId?, siteId?, contractId?, workOrderId?, handoverDate?, commissioningDate?, warrantyMonths? (from the handover; when empty and no manual dates,
 * the end date of the contract's warranty guarantee is used), warrantyStart?, warrantyEnd?, note, spares?: [{ itemId, quantity, note }] (manual lines), rowVersion }. Returns SvUnitDto. Who: ServiceRoles.writers (SERVICE).
 */
export const saveSvUnit = (dto: any) => post(sv + "units", dto);
/** SvUnitActionDto { id, action, note?, rowVersion }. action: confirm (the system draft made at handover), decommission, reactivate. Returns SvUnitDto. Who: writers (SERVICE). */
export const svUnitAction = (dto: { id: number; action: "confirm" | "decommission" | "reactivate"; note?: string | null; rowVersion?: string | null }) =>
  post(sv + "units/action", { id: dto.id, action: dto.action, note: dto.note || null, rowVersion: dto.rowVersion || null });

// ---------------------------------------------------------------- service requests
/**
 * params: SvRequestQuery { Status 1..6, Priority 1..4, CustomerId, UnitId, OpenOnly, Breached (SLA), AsOf } + paging.
 * SvRequestDto { id, number, customerId, customerName, contactId, unitId, unitTag, channel, receivedAt(Text), description, type (1..7), typeText, priority, priorityText, productNonconformity, status, statusText,
 * responseDue(Text), resolutionDue(Text), slaMissing, assignedToUserId, assignedToName, firstResponseAt(Text), resolvedAt(Text), resolution, responseBreached, resolutionBreached, breached, complaintId, complaintNumber,
 * orderCount, rowVersion }. Who: readers.
 */
export const getSvRequests = (params?: ServicePageParams) => get(sv + "requests", params);
/** One SvRequestDto as of AsOf (ISO date, optional; the SLA breach flags are computed against it). Who: readers. */
export const getSvRequest = (id: number, asOf?: string | null) => get(sv + "requests/" + id, { AsOf: asOf });
/**
 * SvSaveRequestDto { id?, customerId, contactId?, unitId?, channel, receivedAt? (now; the SLA clock starts here), description, type (1..7), priority (1..4), productNonconformity, rowVersion }.
 * A Complaint type, or a product nonconformity, makes a quality CustomerComplaint linked both ways. Returns SvRequestDto. Who: writers (SERVICE).
 */
export const saveSvRequest = (dto: any) => post(sv + "requests", dto);
/**
 * SvRequestActionDto { id, action, userId?, resolution?, at?, rowVersion }. action: assign (userId), progress, wait (waiting for the customer), resolve (resolution text), close.
 * at = when it happened (now when empty; lets the office enter a response given earlier). Returns SvRequestDto. Who: writers (SERVICE).
 */
export const svRequestAction = (dto: { id: number; action: "assign" | "progress" | "wait" | "resolve" | "close"; userId?: number | null; resolution?: string | null; at?: string | null; rowVersion?: string | null }) =>
  post(sv + "requests/action", { id: dto.id, action: dto.action, userId: dto.userId ?? null, resolution: dto.resolution || null, at: dto.at || null, rowVersion: dto.rowVersion || null });

// ---------------------------------------------------------------- service orders (api/v1/service/orders)
/**
 * params: SvOrderQuery { Status 1..5, RequestId, UnitId, ContractId (service orders of an LTSA contract), Decision 0..3 } + paging.
 * SvOrderDto { id, number, requestId, requestNumber, unitId, unitTag, customerId, customerName, contractId, ltsaContractId, ltsaNumber, kind (1 field, 2 workshop), status, statusText, title, plannedStart(Text), plannedEnd(Text),
 * travelNote, startedAt(Text), costCenterCode, decision (0 none, 1 warranty, 2 billable, 3 goodwill), decisionText, causeIsMisuse, decisionNote, decidedAtText, decidedByUserId, goodwillRequested, goodwillReason,
 * warrantyTestedOn(Text), reportDocumentId (guid; the signed report), findings, recommendations, runningHoursReading, closedAtText,
 * engineers: [{ employeeId, name }], tasks: [{ id, rowNumber, description, isDone }],
 * timeEntries: [{ id, employeeId, employeeName, workDate(Text), hours, kind (1 travel, 2 work, 3 waiting), kindText, note }], measurements: [{ id, name, value, unit, note }],
 * parts: [{ documentId, number, type, status, lines: [{ itemId, itemCode, itemName, quantity }] }], travelHours, workHours, waitingHours, totalHours,
 * labourHours, labourAmount (NULL when labourHidden), labourHidden, labourAggregatePending, labourAggregatePeriod (yyyymm), partsAmount, totalCost (null when labour is hidden), costPosted, billableLabourRateRial,
 * invoiceId, invoiceNumber, draftNote, touchedByMe (the current user created or edited it: the backend refuses their warranty / billable decision), rowVersion }. Who: readers.
 */
export const getSvOrders = (params?: ServicePageParams) => get(sv + "orders", params);
/** One SvOrderDto with engineers, tasks, time entries, measurements and parts. Who: readers. */
export const getSvOrder = (id: number) => get(sv + "orders/" + id);
/**
 * SvSaveOrderDto { id?, requestId?, unitId, kind (1 field default / 2 workshop), title, plannedStart?, plannedEnd?, travelNote, engineerEmployeeIds?: number[], tasks?: [{ description, isDone }],
 * billableLabourRateRial? (rial per billable hour: an input), findings, recommendations, runningHoursReading?, reportDocumentId? (guid of the signed report file), rowVersion }. Returns SvOrderDto.
 * Who: writers (SERVICE).
 */
export const saveSvOrder = (dto: any) => post(sv + "orders", dto);
/**
 * SvOrderActionDto { id, action, reason?, rowVersion }. action: confirm (an LTSA visit drafted by the system becomes planned), start, cancel (reason required), close (in progress only; needs the customer-signed report attached via reportDocumentId and a warranty decision, an LTSA visit excepted).
 * Returns SvOrderDto. Who: writers (SERVICE).
 */
export const svOrderAction = (dto: { id: number; action: "confirm" | "start" | "cancel" | "close"; reason?: string | null; rowVersion?: string | null }) =>
  post(sv + "orders/action", { id: dto.id, action: dto.action, reason: dto.reason || null, rowVersion: dto.rowVersion || null });
/** SvSaveTimeEntryDto { orderId, employeeId, workDate? (today), hours, kind (1 travel, 2 work, 3 waiting), note }. Adds one time sheet row. Returns SvOrderDto. Who: writers (SERVICE). */
export const saveSvTimeEntry = (dto: { orderId: number; employeeId: number; workDate?: string | null; hours: number; kind: number; note?: string | null }) => post(sv + "orders/time-entries", dto);
/** SvRemoveRowDto { orderId, rowId }. Returns SvOrderDto. Who: writers (SERVICE). */
export const removeSvTimeEntry = (dto: { orderId: number; rowId: number }) => post(sv + "orders/time-entries/remove", dto);
/** SvSaveMeasurementDto { orderId, name, value, unit, note }: a free measurement row (vibration, temperature, pressure ...). Returns SvOrderDto. Who: writers (SERVICE). */
export const saveSvMeasurement = (dto: { orderId: number; name: string; value: number; unit: string; note?: string | null }) => post(sv + "orders/measurements", dto);
/** SvRemoveRowDto { orderId, rowId }. Returns SvOrderDto. Who: writers (SERVICE). */
export const removeSvMeasurement = (dto: { orderId: number; rowId: number }) => post(sv + "orders/measurements/remove", dto);
/**
 * SvDecisionDto { orderId, decision (1 warranty, 2 billable, 3 goodwill), causeIsMisuse, note, rowVersion }. Warranty only for a unit in warranty and a cause that is not misuse; the decider must be a different person from
 * whoever wrote the order. Goodwill needs a goodwill request first (requestSvGoodwill) and is the CEO's alone (once taken, only the CEO changes it). Returns SvOrderDto.
 * Who: Warranty / Billable: writers (SERVICE); Goodwill: ServiceRoles.goodwillDeciders (CEO).
 */
export const decideSvOrder = (dto: { orderId: number; decision: number; causeIsMisuse?: boolean; note?: string | null; rowVersion?: string | null }) =>
  post(sv + "orders/decision", { orderId: dto.orderId, decision: dto.decision, causeIsMisuse: !!dto.causeIsMisuse, note: dto.note || null, rowVersion: dto.rowVersion || null });
/** SvGoodwillRequestDto { orderId, reason }: SERVICE asks the CEO for a goodwill decision (reason required); sends the CEO a gate. Returns SvOrderDto. Who: writers (SERVICE). */
export const requestSvGoodwill = (dto: { orderId: number; reason: string }) => post(sv + "orders/goodwill-request", dto);
/** SvDraftInvoiceDto { id (the ORDER id), billableLabourRateRial? (needed when not entered on the order) }: drafts the sales invoice of a closed billable order (finance confirms). Returns SvOrderDto. Who: writers (SERVICE). */
export const draftSvInvoice = (dto: { id: number; billableLabourRateRial?: number | null }) => post(sv + "orders/draft-invoice", dto);

// ---------------------------------------------------------------- LTSA contracts
/**
 * params: SvContractQuery { Status 1..4, CustomerId } + paging.
 * SvContractDto { id, number, title, customerId, customerName, status, statusText, startDate(Text), endDate(Text), scope, visitIntervalMonths, priceRial, nextVisitDate(Text), unitIds[], unitTags[], draftedVisits, touchedByMe (the current user created or edited it: the backend refuses their activation), rowVersion }.
 * Who: readers.
 */
export const getSvContracts = (params?: ServicePageParams) => get(sv + "contracts", params);
/** One SvContractDto. Who: readers. */
export const getSvContract = (id: number) => get(sv + "contracts/" + id);
/** SvSaveContractDto { id?, title, customerId, startDate, endDate, scope, visitIntervalMonths, priceRial?, unitIds?: number[], rowVersion }. Returns SvContractDto. Who: writers (SERVICE). */
export const saveSvContract = (dto: any) => post(sv + "contracts", dto);
/** SvContractActionDto { id, action, rowVersion }. action: activate (by somebody who did not write it), cancel. Returns SvContractDto. Who: writers (SERVICE). */
export const svContractAction = (dto: { id: number; action: "activate" | "cancel"; rowVersion?: string | null }) =>
  post(sv + "contracts/action", { id: dto.id, action: dto.action, rowVersion: dto.rowVersion || null });

// ---------------------------------------------------------------- jobs and the monthly protected-labour posting
/**
 * SvJobsRunDto { asOf? } (today when empty; a future day is refused unless the server switch Service:AllowFutureAsOf is on). Runs the nightly jobs by hand: drafts LTSA visits (VisitLeadDays before the due date),
 * expires ended agreements, raises SLA breach alerts, drafts surveys. Returns SvJobsResultDto { asOf, visitsDrafted, contractsExpired, breachAlerts, surveysDrafted, notes[] }. Who: ServiceRoles.jobRunners (SERVICE, CEO).
 */
export const runSvJobs = (asOf?: string | null) => post(sv + "jobs/run", { asOf: asOf || null });
/**
 * SvJobsRunDto { asOf? }. Posts the protected labour of closed service orders as ONE accounting event per ended Persian month, without order numbers (idempotent: an order is taken once).
 * Returns SvLabourAggregateResultDto { asOf, periods: [{ period (yyyymm), periodText, orders, eventAmount, costAmount, eventId, postedAt (ISO) }] }. Who: ServiceRoles.labourRunners (FINANCEMANAGER, CEO).
 */
export const runSvLabourAggregate = (asOf?: string | null) => post(sv + "labour/aggregate", { asOf: asOf || null });
/**
 * PLAIN ARRAY of SvLabourPeriodDto { period (yyyymm), periodText, orders, eventAmount, costAmount, eventId (the accounting event), postedAt (ISO) }: every month posted so far, newest first. Who: ServiceRoles.labourRunners (FINANCEMANAGER, CEO).
 */
export const getSvLabourHistory = () => get(sv + "labour/history");

// ---------------------------------------------------------------- satisfaction surveys
/**
 * params: SlSurveyQuery { Status 1..4, Kind 1 delivery / 2 service, CustomerId } + paging.
 * SlSurveyDto { id, number, kind, kindText, customerId, customerName, contractId, shipmentId, serviceOrderId, draftedOn(Text), status, statusText, sentAtText, answeredAtText, comments, averageScore,
 * answers: [{ id, rowNumber, question, score (1..5 or null) }], rowVersion }. Who: readers.
 */
export const getSvSurveys = (params?: ServicePageParams) => get(sv + "surveys", params);
/** One SlSurveyDto with its answers. Who: readers. */
export const getSvSurvey = (id: number) => get(sv + "surveys/" + id);
/** SlSurveyActionDto { id, action, rowVersion }. action: send (the system drafted it), skip. Returns SlSurveyDto. Who: ServiceRoles.surveyWorkers (SERVICE, SALES). */
export const svSurveyAction = (dto: { id: number; action: "send" | "skip"; rowVersion?: string | null }) =>
  post(sv + "surveys/action", { id: dto.id, action: dto.action, rowVersion: dto.rowVersion || null });
/** SlSurveyAnswerSaveDto { id, scores: [{ rowNumber, score 1..5 }] (one per question), comments, rowVersion }: records the customer's answers. Returns SlSurveyDto. Who: surveyWorkers (SERVICE, SALES). */
export const answerSvSurvey = (dto: { id: number; scores: { rowNumber: number; score: number }[]; comments?: string | null; rowVersion?: string | null }) =>
  post(sv + "surveys/answer", { id: dto.id, scores: dto.scores, comments: dto.comments || null, rowVersion: dto.rowVersion || null });

// ---------------------------------------------------------------- dashboard and KPIs
/**
 * params: AsOf (ISO date, optional). SvDashboardDto { asOf, asOfText, openRequests, openByPriority: [{ priority, priorityText, count }], slaBreaches, unitsInWarranty, unitsUnderLtsa, unitsOutOfWarranty, unitsToConfirm,
 * warrantyCostYtd (null when hidden), warrantyCostHidden, warrantyOrdersYtd, openOrders, ordersWaitingClose, goodwillWaiting, surveysDrafted, surveyAverageScore }. Who: readers.
 */
export const getSvDashboard = (asOf?: string | null) => get(sv + "dashboard", { AsOf: asOf });
/**
 * params: SvKpiQuery { From, To, AsOf } (ISO dates; NO paging). Answers one SvKpiDto { asOf, requestsCounted, firstResponseHours, resolutionHours (both null without data),
 * repeatFailures: [{ unitId, unitTag, failures, repeats }], byProduct: [{ productType, productTypeText, units, runningHours, failures, mtbfHours, warrantyOrders, warrantyCost (null when hidden), warrantyCostHidden }],
 * totalFailures, totalRunningHours, mtbfHours }. Who: readers.
 */
export const getSvKpis = (params?: { From?: string | null; To?: string | null; AsOf?: string | null }) => get(sv + "kpis", params);
