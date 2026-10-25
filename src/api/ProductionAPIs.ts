import { call, clean } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

// Production / shop floor (MES) and maintenance (api/v1/production, release P1 parts A and B). Every answer is { success, message, data }; a rule violation
// answers HTTP 200 with success=false (the helpers return it as the error). Lists are paged ({ data: [], currentPage, totalItems }; read them with rowsOf() or usePagedList()),
// except GET load, GET oee, GET dashboard and GET maintenance/orders/{id}, which answer one object or a plain array.
// Enums are numbers (see composables/production.ts for the labels). Dates in the JSON are ISO (yyyy-mm-dd or ISO date-time); the "...Text" fields are Persian (Jalali).
// The controllers need only a signed-in user; the "Who" part of each doc comment is checked inside the services (ProductionRoles in composables/roles.ts).
// Rejected calls come back as errors. Concurrency: send the rowVersion you read; a stale one is refused with a Persian message.
//
// Query parameter names are the PascalCase property names of the C# query classes (ASP.NET binds them case-insensitively).
// Payroll cost is hidden from roles that may not see it: labourAmount is null with labourHidden=true (cost runs and postings).
const pr = "production/";

type Method = "get" | "post" | "put" | "delete";
async function fcall(method: Method, url: string, body?: any, params?: any): Promise<[any, null] | [null, Error | any]> {
  const [data, error] = await call(method, url, body, params);
  return error ? [null, error] : [camelize(data), null];
}
const get = (url: string, params?: any) => fcall("get", url, undefined, clean(params));
const post = (url: string, body?: any) => fcall("post", url, body);

/** Paging + free-text filter of every list: MfPageQuery { PageNumber (1), PageSize (25, max 500), Contains }. usePagedList() adds PageNumber and PageSize. */
export type ProductionPageParams = { PageNumber?: number; PageSize?: number; Contains?: string | null; [key: string]: any };

// ---------------------------------------------------------------- work centres, machines
/**
 * params: WorkCenterQuery { Kind 1..7, IsActive } + paging.
 * WorkCenterDto { id, code, title, kind, calendarId, calendarName, shiftsPerDay, hoursPerShift, efficiencyPercent, utilisationPercent, parallelResources, machineRateRial, labourRateRial,
 * costCenter, queueDaysDefault, isActive, machineCount, rowVersion }. The rates are null for roles that may not see them (rate viewers: FINANCEMANAGER, FINANCIAL, PRODUCTIONMANAGER, CEO, PMO, AUDITOR).
 * Who: ProductionRoles.workCenterReaders (masterReaders + FINANCIAL).
 */
export const getWorkCenters = (params?: ProductionPageParams) => get(pr + "work-centers", params);
/** One WorkCenterDto. Who: workCenterReaders. */
export const getWorkCenter = (id: number) => get(pr + "work-centers/" + id);
/**
 * SaveWorkCenterDto { id?, code, title, kind (1..7), calendarId?, shiftsPerDay (1), hoursPerShift (8), efficiencyPercent (100), utilisationPercent (85), parallelResources (1),
 * costCenter, queueDaysDefault, isActive (true), rowVersion }. The rates are NOT part of this DTO. Who: ProductionRoles.masterEditors (PRODUCTIONMANAGER, PMO).
 */
export const saveWorkCenter = (dto: any) => post(pr + "work-centers", dto);
/** SaveWorkCenterRatesDto { id, machineRateRial?, labourRateRial? } (hourly rates in rial, finance only). Who: ProductionRoles.rateSetters (FINANCEMANAGER, FINANCIAL). */
export const saveWorkCenterRates = (dto: { id: number; machineRateRial?: number | null; labourRateRial?: number | null }) => post(pr + "work-centers/rates", dto);

/**
 * params: MachineQuery { WorkCenterId, Status 1..4 } + paging.
 * MachineDto { machineId, title, isActive, infoId, workCenterId, workCenterCode, assetId, maker, model, serialNumber, capabilities, status }.
 * Machines themselves are created elsewhere (the existing machine page); here only the production side-data is edited. Who: masterReaders.
 */
export const getProductionMachines = (params?: ProductionPageParams) => get(pr + "machines", params);
/** SaveMachineInfoDto { machineId, workCenterId?, assetId?, maker, model, serialNumber, capabilities, status (1 available) }. Who: masterEditors + MAINTENANCE. */
export const saveMachineInfo = (dto: any) => post(pr + "machines/info", dto);

// ---------------------------------------------------------------- operation standards, routings
/**
 * params: OperationStandardQuery { ItemId, ItemGroupId } + paging. One row per routing step (OperationTemplate), with the standard filled or empty.
 * OperationStandardDto { operationTemplateId, itemId, itemCode, itemGroupId, itemGroupCode, sequence, templateTitle, requiresQc, id (null = no standard yet), workCenterId, workCenterCode,
 * setupHours, runHoursPerUnit, moveDays, queueDays, isOutside, defaultSupplierId, defaultSupplierName, outsideLeadDays, workInstructionDocumentId, drawingReferenceRule,
 * requiredQualificationKind, requiredQualificationProcess, inspectionRequirement (0..3), itpTestType, firstArticleRequired }. Who: masterReaders.
 */
export const getOperationStandards = (params?: ProductionPageParams) => get(pr + "operation-standards", params);
/**
 * SaveOperationStandardDto { operationTemplateId, workCenterId?, setupHours, runHoursPerUnit, moveDays, queueDays, isOutside, defaultSupplierId?, outsideLeadDays, workInstructionDocumentId?,
 * drawingReferenceRule, requiredQualificationKind, requiredQualificationProcess, inspectionRequirement (0 none, 1 self, 2 QC, 3 ITP point), itpTestType?, firstArticleRequired }.
 * Who: ProductionRoles.standardEditors (PRODUCTIONMANAGER, ENGINEERING, ENGINEER, PMO).
 */
export const saveOperationStandard = (dto: any) => post(pr + "operation-standards", dto);

/**
 * params: RoutingQuery { ItemId, ItemGroupId, Status 0 draft / 1 approved } + paging.
 * RoutingRevisionDto { id, itemId, itemCode, itemTitle, itemGroupId, itemGroupCode, revision, status, note, contentHash, contentMatches, createdByName, createdAtText,
 * productionApprovedByName, productionApprovedAtText, engineeringApprovedByName, engineeringApprovedAtText, approvedAtText, steps: RoutingStepDto[], rowVersion }.
 * RoutingStepDto { templateId, sequence, title, requiresQc, standardId, workCenterCode, setupHours, runHoursPerUnit, isOutside, inspectionRequirement }. (The list rows carry the same fields.)
 * Who: ProductionRoles.readers (the list answers "not allowed" to OPERATOR, ASSEMBLER, CONSTRUCTION).
 */
export const getRoutings = (params?: ProductionPageParams) => get(pr + "routings", params);
/** One RoutingRevisionDto with its steps. Who: readers. */
export const getRouting = (id: number) => get(pr + "routings/" + id);
/**
 * SaveRoutingRevisionDto { itemId?, itemGroupId?, note }: opens a new DRAFT revision for an item or an item group (the steps are the OperationTemplate rows of the item/group).
 * Who: ProductionRoles.routingWriters (PRODUCTIONMANAGER, ENGINEERING, ENGINEER, PMO, PLANNING).
 */
export const saveRouting = (dto: { itemId?: number | null; itemGroupId?: number | null; note?: string | null }) => post(pr + "routings", dto);
/**
 * RoutingActionDto { id, action, rowVersion }. action: approve-production, approve-engineering, delete (draft only).
 * Two-person approval: approve-production only PRODUCTIONMANAGER, approve-engineering only ENGINEERING / ENGINEER; the creator may not approve and the two approvals come from two different people;
 * every step needs a saved standard. delete: routingWriters. Show the buttons only for a draft (status 0).
 */
export const routingAction = (dto: { id: number; action: "approve-production" | "approve-engineering" | "delete"; rowVersion?: string | null }) =>
  post(pr + "routings/action", { id: dto.id, action: dto.action, rowVersion: dto.rowVersion || null });

// ---------------------------------------------------------------- reason codes
/** params: ReasonCodeQuery { Kind 1 scrap / 2 downtime / 3 waiting, IsActive } + paging. ReasonCodeDto { id, code, title, kind, isActive }. Who: masterReaders (operators read them on the terminal). */
export const getReasonCodes = (params?: ProductionPageParams) => get(pr + "reason-codes", params);
/** SaveReasonCodeDto { id?, code, title, kind (1..3), isActive (true) }. Who: masterEditors + SHOPSUPERVISOR. */
export const saveReasonCode = (dto: any) => post(pr + "reason-codes", dto);

// ---------------------------------------------------------------- production orders
/**
 * params: OrderQuery { Status 0..5, WorkOrderId, ContractId, ItemId, OpenOnly, LateOnly } + paging.
 * OrderListDto { id, number, workOrderId, workOrderCode, contractId, processId, itemId, itemCode, itemTitle, quantity, unit, status, routingRevisionId, priority (1 = most urgent), totalFloatDays,
 * needDate, needDateText, plannedStart(Text), plannedFinish(Text), actualStartText, actualFinishText, operationCount, operationsDone, autoDrafted, isLate, daysLate, kitReadyDateText,
 * kitShortage, scheduleNote, scheduledAtText }. Who: masterReaders.
 */
export const getProductionOrders = (params?: ProductionPageParams) => get(pr + "orders", params);
/**
 * OrderDto = list fields + bomId, activityId, serialsPlanned, kitRequestId, kitNumber, kitIssued, kitOverrideReason, releaseOverride, releaseOverrideReason, shortageText, releasedByName,
 * releasedAtText, productionReceiptId, productionReceiptNumber, cancelReason, note, components: [{ itemId, itemCode, itemTitle, quantity, unit }], operations: OperationDto[],
 * release: ReleaseCheckResultDto (only while the order is a draft), rowVersion. Who: masterReaders.
 */
export const getProductionOrder = (id: number) => get(pr + "orders/" + id);
/**
 * DraftOrdersDto { workOrderId?, processId?, itemId? }: the system normally drafts the orders itself; this drafts them by hand for a work order or one process
 * (itemId only when the process output code matches no item). Returns DraftOrdersResultDto { drafted, skipped, orderIds[], notes[] }. Who: ProductionRoles.planners.
 */
export const draftProductionOrders = (dto: { workOrderId?: number | null; processId?: number | null; itemId?: number | null }) => post(pr + "orders/draft", dto);
/** SaveComponentsDto { orderId, components: [{ itemId, quantity }], serialsPlanned, rowVersion }: replaces the component list of a DRAFT order. Returns OrderDto. Who: planners. */
export const saveComponents = (dto: { orderId: number; components: { itemId: number; quantity: number }[]; serialsPlanned?: string | null; rowVersion?: string | null }) => post(pr + "orders/components", dto);
/**
 * ReleaseCheckResultDto { orderId, canRelease, canReleaseWithOverride, checks: [{ code (routing, workcenters, drawings, itp, materials), title, passed, message, overridable }],
 * shortages: [{ itemId, itemCode, itemTitle, required, onHand, reserved, incoming, available, short }] }. Only the materials check is overridable. Who: masterReaders.
 */
export const getReleaseCheck = (id: number) => get(pr + `orders/${id}/release-check`);
/**
 * ReleaseOrderDto { id, overrideReason?, rowVersion }. Releases a draft order and drafts the kit material request. A shortage list is released only by PRODUCTIONMANAGER and needs overrideReason.
 * Who: ProductionRoles.supervisors (SHOPSUPERVISOR, PRODUCTIONMANAGER). Returns OrderDto.
 */
export const releaseProductionOrder = (dto: { id: number; overrideReason?: string | null; rowVersion?: string | null }) =>
  post(pr + "orders/release", { id: dto.id, overrideReason: dto.overrideReason || null, rowVersion: dto.rowVersion || null });
/**
 * OrderActionDto { id, action, reason? }. action:
 *  cancel (reason required; draft by planners, released-and-not-started by supervisors), close (completed order; supervisors),
 *  kit-override (start without the full kit; reason required; supervisors), refresh-readiness (re-evaluates which operations are Ready; supervisors),
 *  refresh-routing (draft only: reloads the operations from the approved routing; planners). Returns OrderDto.
 * (The DTO comment of the backend lists only cancel, close, kit-override, refresh-routing; the service also accepts refresh-readiness.)
 */
export const orderAction = (dto: { id: number; action: "cancel" | "close" | "kit-override" | "refresh-readiness" | "refresh-routing"; reason?: string | null }) =>
  post(pr + "orders/action", { id: dto.id, action: dto.action, reason: dto.reason || null });

/**
 * params: OperationQuery { Id, OrderId, WorkCenterId, Status 0..7 } + paging.
 * OperationDto { id, orderId, orderNumber, jobCard, sequence, title, workCenterId, workCenterCode, workCenterTitle, setupHoursPlanned, runHoursPlanned, plannedStart(Text), plannedFinish(Text),
 * status, goodQty, scrapQty, reworkQty, inspectionRequirement, itpTestType, itpActivityId, qcAccepted, qcRequestedAtText, qcRecordId, qcRecordNumber, ncrId, ncrNumber, isOutside, outsideSupplierId,
 * purchaseRequestId, purchaseRequestNumber, purchaseRequestLineId, purchaseOrderLineId, subcontractIssueId, subcontractIssueNumber, isRework, reworkOfOperationId, skipReason, startedAtText,
 * finishedAtText, note, runningBookings, hoursBooked }. Who: masterReaders.
 */
export const getProductionOperations = (params?: ProductionPageParams) => get(pr + "operations", params);
/**
 * OperationActionDto { id, action, reason?, documentId? }. action: skip (reason required; only SHOPSUPERVISOR; a Waiting or Ready operation), complete-outside (an outside operation; documentId = the
 * confirmed return-from-contractor document; supervisors). Returns OperationDto.
 */
export const operationAction = (dto: { id: number; action: "skip" | "complete-outside"; reason?: string | null; documentId?: number | null }) =>
  post(pr + "operations/action", { id: dto.id, action: dto.action, reason: dto.reason || null, documentId: dto.documentId ?? null });
/**
 * QcDecisionDto { operationId, result, note?, itpActivityId? }. result: 1 accept, 2 reject, 3 rework, 4 hold (QcResult of the quality module). itpActivityId only for an ITP-point operation
 * (found by the test type when empty). Who: ProductionRoles.inspectors (QC only). Returns OperationDto.
 */
export const qcDecision = (dto: { operationId: number; result: 1 | 2 | 3 | 4; note?: string | null; itpActivityId?: number | null }) =>
  post(pr + "operations/qc-decision", { operationId: dto.operationId, result: dto.result, note: dto.note || null, itpActivityId: dto.itpActivityId ?? null });
/** ReworkOperationDto { operationId, ncrId, note? }: makes a rework operation for an operation rejected in inspection (status 6, not itself a rework). Who: supervisors. Returns OperationDto. */
export const reworkOperation = (dto: { operationId: number; ncrId: number; note?: string | null }) => post(pr + "operations/rework", dto);

// ---------------------------------------------------------------- shop-floor terminal
/**
 * params: TerminalQuery { WorkCenterId, Code } + paging. The operations the signed-in operator can work on now (Ready, InProgress, Paused; outside operations excluded).
 * TerminalOperationDto = OperationDto + itemCode, itemTitle, orderQuantity, expectedQty (order quantity, or the rework quantity of a rework operation), unit, orderPriority, needDateText, myRunning, myRunningSince (ISO), myRunningSinceText, hasMyBooking (the operator has any booking on the operation).
 * Who: ProductionRoles.terminal (OPERATOR, SHOPSUPERVISOR, PRODUCTIONMANAGER, ASSEMBLER, CONSTRUCTION). The user must be linked to a personnel record.
 */
export const getMyOperations = (params?: ProductionPageParams) => get(pr + "terminal/my-operations", params);
/** params: TerminalQuery { Code } = job card (order number or order/sequence), serial number or part label scanned from a label. Same rows as getMyOperations. Who: terminal. */
export const scanOperations = (params: ProductionPageParams) => get(pr + "terminal/scan", params);
/** StartOperationDto { operationId, kind (1 setup, 2 run default, 3 rework, 4 waiting, 5 cleaning), machineId?, note? }. Starts a booking; a running booking of the same person is stopped first. Who: terminal. Returns TerminalOperationDto. */
export const startOperation = (dto: { operationId: number; kind?: number; machineId?: number | null; note?: string | null }) =>
  post(pr + "terminal/start", { operationId: dto.operationId, kind: dto.kind ?? 2, machineId: dto.machineId ?? null, note: dto.note || null });
/** PauseOperationDto { operationId, reasonCodeId? (downtime or waiting reason), note? }. Stops the running booking. Who: terminal. */
export const pauseOperation = (dto: { operationId: number; reasonCodeId?: number | null; note?: string | null }) => post(pr + "terminal/pause", dto);
/** ReportQuantityDto { operationId, goodQty, scrapQty, scrapReasonCodeId? (required when scrapQty > 0, a scrap reason), reworkQty, note? }. Who: terminal. */
export const reportQuantity = (dto: { operationId: number; goodQty: number; scrapQty?: number; scrapReasonCodeId?: number | null; reworkQty?: number; note?: string | null }) =>
  post(pr + "terminal/report-quantity", { operationId: dto.operationId, goodQty: dto.goodQty, scrapQty: dto.scrapQty || 0, scrapReasonCodeId: dto.scrapReasonCodeId ?? null, reworkQty: dto.reworkQty || 0, note: dto.note || null });
/** FinishOperationDto: same fields as ReportQuantityDto; finishes the operation (goes to QC hold when the routing requires inspection). Who: terminal. */
export const finishOperation = (dto: { operationId: number; goodQty: number; scrapQty?: number; scrapReasonCodeId?: number | null; reworkQty?: number; note?: string | null }) =>
  post(pr + "terminal/finish", { operationId: dto.operationId, goodQty: dto.goodQty, scrapQty: dto.scrapQty || 0, scrapReasonCodeId: dto.scrapReasonCodeId ?? null, reworkQty: dto.reworkQty || 0, note: dto.note || null });
/** RequestQcDto { operationId, note? }: puts a QC request in the inbox of the QC users. Who: terminal. */
export const requestQc = (dto: { operationId: number; note?: string | null }) => post(pr + "terminal/request-qc", dto);
/**
 * MachineDownDto { machineId, operationId?, reasonCodeId?, description }: reports a breakdown (creates a maintenance request and tells MAINTENANCE).
 * Returns MaintenanceRequestDto. Who: terminal roles + MAINTENANCE (ProductionRoles.machineDownReporters).
 */
export const reportMachineDown = (dto: { machineId: number; operationId?: number | null; reasonCodeId?: number | null; description?: string | null }) => post(pr + "terminal/machine-down", dto);

/**
 * params: MaintenanceRequestQuery { MachineId, Status 0 open / 1 in progress / 2 done / 3 cancelled } + paging.
 * MaintenanceRequestDto { id, number, machineId, machineTitle, workCenterId, operationId, reasonCodeId, reportedByUserId, reportedByName, reportedAt, reportedAtText, description, status }. Who: masterReaders.
 */
export const getMaintenanceRequests = (params?: ProductionPageParams) => get(pr + "maintenance-requests", params);

// ---------------------------------------------------------------- time bookings, attendance check
/**
 * params: BookingQuery { OperationId, OrderId, EmployeeId, WorkCenterId, From, To (ISO dates), RunningOnly } + paging. Readers see everybody, any other reader (an operator) sees only his own bookings.
 * BookingDto { id, operationId, orderNumber, jobCard, operationTitle, employeeId, employeeName, machineId, machineTitle, kind, startAt, startAtText, endAt, endAtText, minutes, hours, goodQty, scrapQty,
 * scrapReasonCodeId, reworkQty, reasonCodeId, note, source (1 terminal / 2 supervisor), supervisorReason, editReason, running, dayClosed, costPosted, rowVersion }. Who: masterReaders (HR is refused).
 */
export const getBookings = (params?: ProductionPageParams) => get(pr + "bookings", params);
/**
 * SaveBookingDto { operationId, employeeId, kind (1..5), startAt, endAt (ISO date-time, not in the future, at most 16 hours, no overlap with another booking of the person), machineId?, goodQty, scrapQty,
 * scrapReasonCodeId? (needed when scrapQty > 0), reworkQty, reasonCodeId?, note, reason (REQUIRED: why somebody else books) }. A completed booking made by a supervisor for another person.
 * Who: ProductionRoles.supervisors.
 */
export const createBooking = (dto: any) => post(pr + "bookings", dto);
/**
 * EditBookingDto { id, kind?, startAt?, endAt?, machineId?, goodQty?, scrapQty?, scrapReasonCodeId?, reworkQty?, note?, reason (REQUIRED), rowVersion }. Not for a running booking and not after the cost was posted.
 * After the day is closed (dayClosed=true) only SHOPSUPERVISOR may edit. Who: supervisors.
 */
export const editBooking = (dto: any) => post(pr + "bookings/edit", dto);
/**
 * params: AttendanceDifferenceQuery { From, To (required, ISO dates), EmployeeId?, EmployeeIds[]? (more people to check), ToleranceMinutes (15), PageNumber, PageSize (50) }; the array is sent as repeated keys.
 * AttendanceDifferenceDto { employeeId, employeeName, date, dateText, attendanceHours, bookedHours, differenceHours, kind (Unbooked | OverBooked | NoAttendance), message }.
 * Who: ProductionRoles.attendanceCheck (SHOPSUPERVISOR, PRODUCTIONMANAGER, HR, CEO, PMO).
 */
export const getAttendanceDifferences = (params: { From: string; To: string; EmployeeId?: number | null; EmployeeIds?: number[]; ToleranceMinutes?: number; PageNumber?: number; PageSize?: number }) => {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v === null || v === undefined || v === "") return;
    if (Array.isArray(v)) v.forEach((x) => q.append(k, String(x)));
    else q.append(k, String(v));
  });
  return fcall("get", pr + "attendance-differences", undefined, q);
};

// ---------------------------------------------------------------- daily cost posting (part B)
/**
 * RunCostDto { date? }: posts the closed bookings that started that day (empty: every unposted closed booking of the days before today) to the cost entries and the accounting event.
 * CostRunResultDto { runs: CostRunDto[], bookingsPosted, bookingsSkipped (a rate is missing), zeroHourBookings, labourRateFromPayroll, labourRateFromWorkCenter, labourAmount (null + labourHidden), labourHidden,
 * overheadAmount, errors[], message }. Who: ProductionRoles.costRunners (FINANCEMANAGER, FINANCIAL, PRODUCTIONMANAGER).
 */
export const runCost = (date?: string | null) => post(pr + "cost/run", { date: date || null });
/**
 * params: CostRunQuery { From, To } + paging. CostRunDto { id, number, postingDate, postingDateText, runAtText, bookingCount, labourHours, machineHours, labourAmount (null with labourHidden), labourHidden,
 * overheadAmount, accountingEventId }. Who: ProductionRoles.costReaders.
 */
export const getCostRuns = (params?: ProductionPageParams) => get(pr + "cost/runs", params);
/**
 * params: CostPostingQuery { RunId, WorkOrderId, From, To } + paging. CostPostingDto { id, runId, runNumber, postingDate, postingDateText, workOrderId, workOrderCode, contractId, wbsNodeId, wbsCode,
 * workCenterId, workCenterCode, bookingCount, labourHours, machineHours, labourAmount (null with labourHidden: fewer than three employees in the row and the caller is not PAYROLL, FINANCEMANAGER, CEO),
 * labourHidden, overheadAmount }. Who: costReaders.
 */
export const getCostPostings = (params?: ProductionPageParams) => get(pr + "cost/postings", params);

// ---------------------------------------------------------------- scheduling, load, dispatch (part B)
/**
 * RunScheduleDto { asOf? } (a past or future "today", for planning from a chosen day). Re-runnable and deterministic: places operations of Released and InProgress orders on the work-centre calendars,
 * writes planned dates, late flags and the forecast finish to project control.
 * ScheduleResultDto { id, asOf, asOfText, orders, operations, lateOrders, unscheduled, forecastsPushed, items: ScheduledOrderDto[] }.
 * ScheduledOrderDto { orderId, number, priority, needDate(Text), plannedStart(Text), plannedFinish(Text), isLate, daysLate, kitReadyDate(Text), kitShortage, note, operations: ScheduledOperationDto[] }.
 * ScheduledOperationDto { operationId, jobCard, sequence, title, workCenterId, workCenterCode, status, hours, plannedStart(Text), plannedFinish(Text), note }.
 * Who: ProductionRoles.scheduleRunners (PRODUCTIONMANAGER, SHOPSUPERVISOR, PLANNING, PMO).
 */
export const runSchedule = (asOf?: string | null) => post(pr + "schedule/run", { asOf: asOf || null });
/**
 * params: LoadQuery { From (default today; weeks start on Saturday), Weeks (12), WorkCenterId, Threshold (percent, 100) }. NOT paged.
 * LoadReportDto { from, fromText, weeks, threshold, rows: [{ workCenterId, code, title, cells: LoadCellDto[] }], overloads: LoadCellDto[] (worst first) }.
 * LoadCellDto { workCenterId, workCenterCode, weekStart, weekStartText, capacityHours, loadHours (demand, unlimited capacity), scheduledHours (finite-capacity plan), loadPercent (null: load without capacity), overloaded }.
 * Who: ProductionRoles.readers.
 */
export const getLoad = (params?: { From?: string | null; Weeks?: number; WorkCenterId?: number | null; Threshold?: number }) => get(pr + "load", params);
/**
 * params: DispatchQuery { WorkCenterId (required) } + paging. The Ready, Running and Next operations of a work centre in priority order (the data of the printable job cards; the front prints).
 * DispatchRowDto { operationId, orderId, orderNumber, jobCard, priority, sequence, title, status, stage (Ready | Running | Next), itemCode, itemTitle, quantity, unit, setupHoursPlanned, runHoursPlanned,
 * plannedStart(Text), plannedFinish(Text), needDateText, orderLate }. Who: masterReaders.
 */
export const getDispatch = (params: ProductionPageParams & { WorkCenterId: number }) => get(pr + "dispatch", params);

// ---------------------------------------------------------------- maintenance (part B)
/**
 * params: MaintenancePlanQuery { MachineId, IsActive } + paging. MaintenancePlanDto { id, machineId, machineTitle, task, intervalKind (1 days / 2 running hours), intervalValue, startDate(Text), lastDoneAt(Text),
 * nextDueDate(Text), runningHoursSince, checklist, estimatedHours, isActive, openOrderId, rowVersion }. Who: masterReaders.
 */
export const getMaintenancePlans = (params?: ProductionPageParams) => get(pr + "maintenance/plans", params);
/** SaveMaintenancePlanDto { id?, machineId, task, intervalKind (1 days default), intervalValue, startDate?, checklist, estimatedHours, isActive (true), rowVersion }. Who: ProductionRoles.maintenanceWriters (MAINTENANCE, PRODUCTIONMANAGER). */
export const saveMaintenancePlan = (dto: any) => post(pr + "maintenance/plans", dto);
/** DraftDueDto { asOf? }: drafts maintenance orders for the plans that are due (7 days ahead). DraftDueResultDto { asOf, plansChecked, drafted, orders: MaintenanceOrderListDto[] }. Who: maintenanceWriters. */
export const draftDueMaintenance = (asOf?: string | null) => post(pr + "maintenance/plans/draft-due", { asOf: asOf || null });
/**
 * params: MaintenanceOrderQuery { MachineId, Kind 1 planned / 2 breakdown, Status 0..3, OpenOnly } + paging.
 * MaintenanceOrderListDto { id, number, machineId, machineTitle, workCenterId, kind, status, planId, requestId, task, dueDate(Text), overdue, estimatedHours, downtimeStart(Text), downtimeEnd(Text), costCenter }. Who: masterReaders.
 */
export const getMaintenanceOrders = (params?: ProductionPageParams) => get(pr + "maintenance/orders", params);
/**
 * MaintenanceOrderDto = list fields + checklist, cause, actionTaken, materialRequestId/Number, issueDocumentId/Number, startedAtText, doneAtText, cancelReason, downtimeHours, labourHours,
 * labour: [{ id, employeeId, employeeName, workDate(Text), hours, note }], parts: [{ itemId, itemCode, itemTitle, quantity, note }], rowVersion. Who: masterReaders.
 */
export const getMaintenanceOrder = (id: number) => get(pr + "maintenance/orders/" + id);
/** CreateMaintenanceOrderDto { machineId, kind (1 planned default / 2 breakdown), task, dueDate?, estimatedHours, checklist, costCenter, requestId? (breakdown report), downtimeStart? }. Who: maintenanceWriters. */
export const createMaintenanceOrder = (dto: any) => post(pr + "maintenance/orders", dto);
/** SaveMaintenanceOrderDto { id, task, dueDate?, estimatedHours?, checklist, costCenter, downtimeStart?, downtimeEnd?, cause, actionTaken, parts?: [{ itemId, quantity, note }] (replaces the list while open), rowVersion }. Who: maintenanceWriters. */
export const saveMaintenanceOrder = (dto: any) => post(pr + "maintenance/orders/save", dto);
/**
 * MaintenanceOrderActionDto { id, action, reason?, downtimeEnd?, rowVersion }. action: start, complete, cancel (reason required), request-parts (drafts the material request of the spare parts),
 * draft-issue (drafts the issue against the approved request). Only an open order (status 0 or 1) takes an action. Who: maintenanceWriters. Returns MaintenanceOrderDto.
 */
export const maintenanceOrderAction = (dto: { id: number; action: "start" | "complete" | "cancel" | "request-parts" | "draft-issue"; reason?: string | null; downtimeEnd?: string | null; rowVersion?: string | null }) =>
  post(pr + "maintenance/orders/action", { id: dto.id, action: dto.action, reason: dto.reason || null, downtimeEnd: dto.downtimeEnd || null, rowVersion: dto.rowVersion || null });
/** AddMaintenanceLabourDto { orderId, employeeId? (default: the personnel record of the signed-in user), workDate?, hours, note }. Who: maintenanceWriters. */
export const addMaintenanceLabour = (dto: { orderId: number; employeeId?: number | null; workDate?: string | null; hours: number; note?: string | null }) => post(pr + "maintenance/labour", dto);
/**
 * params: KpiQuery { From, To, WorkCenterId, MachineId } + paging. MachineKpiDto { machineId, machineTitle, workCenterId, workCenterCode, plannedHours, downtimeHours, availabilityPercent, breakdowns,
 * mtbfHours (null without a breakdown), mttrHours }. Who: ProductionRoles.readers.
 */
export const getMachineKpis = (params?: ProductionPageParams) => get(pr + "maintenance/kpis", params);
/**
 * params: KpiQuery { From, To, WorkCenterId, MachineId }. Answers a PLAIN ARRAY (not paged) of OeeDto { workCenterId, workCenterCode, workCenterTitle, plannedHours, downtimeHours, bookedRunHours, standardHours,
 * goodQty, scrapQty, availabilityPercent, performancePercent, qualityPercent, oeePercent } (the last three can be null). Who: readers.
 */
export const getOee = (params?: { From?: string | null; To?: string | null; WorkCenterId?: number | null; MachineId?: number | null }) => get(pr + "oee", params);

// ---------------------------------------------------------------- dashboard
/**
 * params: DashboardQuery { AsOf? }. ProductionDashboardDto { asOf, asOfText, lateOrders, lateOrderList: [{ orderId, number, itemCode, daysLate, needDateText, plannedFinishText }], qcHoldOverTwoDays,
 * qcHoldList: [{ operationId, jobCard, title, daysOnHold, requestedAtText }], overloadWeeks, overloadList: LoadCellDto[], kitShortages, kitShortageList (as lateOrderList), machinesDown,
 * machinesDownList: [{ machineId, title, workCenterCode, status }], maintenanceOverdue, monthGoodQty, monthScrapQty, scrapRatePercent, lastScheduleRun, lastScheduleRunText, oee: OeeDto[], oeePercent }.
 * Who: ProductionRoles.dashboard.
 */
export const getProductionDashboard = (asOf?: string | null) => get(pr + "dashboard", { AsOf: asOf });
