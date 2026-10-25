# Production / shop floor (MES) and maintenance: front foundation

Shared layer for the pages of «تولید و نگهداری». Backend: `api/v1/production` (controllers in `src/WebApi/Controllers/Production/ProductionControllers.cs` and `ProductionPartBControllers.cs`; the controller and the services are the truth where they differ from `production-backend-brief.md`).

## Files
- `src/api/ProductionAPIs.ts` one function per endpoint (all of part A and B). Same conventions as `OfficeAPIs.ts`: each returns `[data, null]` or `[null, error]`, data is camelized, lists are paged `{ data, totalItems }` (read with `rowsOf()` / `usePagedList()`), every function has a doc comment with the DTO fields, the allowed action strings and who may call it. Query parameters are PascalCase (`PageNumber`, `PageSize`, `Contains`, `Status`, ...).
- `src/composables/production.ts` Persian label maps, formatting helpers, badge helpers.
- `src/composables/roles.ts` `Roles/RoleIds/roleTitles` + PRODUCTIONMANAGER (20), SHOPSUPERVISOR (21), OPERATOR (22), MAINTENANCE (23), and `ProductionRoles`.
- `src/router/index.ts` routes under `/production/...`, `src/components/common/KsSidebar/KsSidebar.vue` section «تولید و نگهداری», `src/views/production/*.vue` stubs (h1 + «در دست ساخت»).

## API functions (ProductionAPIs.ts)
All GET list functions take `params?: ProductionPageParams` (`PageNumber, PageSize, Contains` + the query fields of the C# query class).

| Area | Function | Endpoint |
|---|---|---|
| Work centres | `getWorkCenters(params)` `{Kind, IsActive}` | GET work-centers |
| | `getWorkCenter(id)` | GET work-centers/{id} |
| | `saveWorkCenter(dto)` | POST work-centers |
| | `saveWorkCenterRates({id, machineRateRial?, labourRateRial?})` | POST work-centers/rates |
| Machines | `getProductionMachines(params)` `{WorkCenterId, Status}` | GET machines |
| | `saveMachineInfo(dto)` | POST machines/info |
| Standards | `getOperationStandards(params)` `{ItemId, ItemGroupId}` | GET operation-standards |
| | `saveOperationStandard(dto)` | POST operation-standards |
| Routings | `getRoutings(params)` `{ItemId, ItemGroupId, Status}` | GET routings |
| | `getRouting(id)` | GET routings/{id} |
| | `saveRouting({itemId?, itemGroupId?, note?})` | POST routings (opens a draft revision) |
| | `routingAction({id, action, rowVersion})` action `approve-production`, `approve-engineering`, `delete` | POST routings/action |
| Reason codes | `getReasonCodes(params)` `{Kind, IsActive}` | GET reason-codes |
| | `saveReasonCode(dto)` | POST reason-codes |
| Orders | `getProductionOrders(params)` `{Status, WorkOrderId, ContractId, ItemId, OpenOnly}` | GET orders |
| | `getProductionOrder(id)` | GET orders/{id} |
| | `draftProductionOrders({workOrderId?, processId?, itemId?})` | POST orders/draft |
| | `saveComponents({orderId, components[], serialsPlanned?, rowVersion})` | POST orders/components |
| | `getReleaseCheck(id)` | GET orders/{id}/release-check |
| | `releaseProductionOrder({id, overrideReason?, rowVersion?})` | POST orders/release |
| | `orderAction({id, action, reason?})` action `cancel`, `close`, `kit-override`, `refresh-readiness`, `refresh-routing` | POST orders/action |
| Operations | `getProductionOperations(params)` `{OrderId, WorkCenterId, Status}` | GET operations |
| | `operationAction({id, action, reason?, documentId?})` action `skip`, `complete-outside` | POST operations/action |
| | `qcDecision({operationId, result 1..4, note?, itpActivityId?})` | POST operations/qc-decision |
| | `reworkOperation({operationId, ncrId, note?})` | POST operations/rework |
| Terminal | `getMyOperations(params)` `{WorkCenterId, Code}` | GET terminal/my-operations |
| | `scanOperations({Code, ...})` | GET terminal/scan |
| | `startOperation({operationId, kind=2, machineId?, note?})` | POST terminal/start |
| | `pauseOperation({operationId, reasonCodeId?, note?})` | POST terminal/pause |
| | `reportQuantity({operationId, goodQty, scrapQty?, scrapReasonCodeId?, reworkQty?, note?})` | POST terminal/report-quantity |
| | `finishOperation(same as reportQuantity)` | POST terminal/finish |
| | `requestQc({operationId, note?})` | POST terminal/request-qc |
| | `reportMachineDown({machineId, operationId?, reasonCodeId?, description?})` | POST terminal/machine-down |
| | `getMaintenanceRequests(params)` `{MachineId, Status}` | GET maintenance-requests |
| Bookings | `getBookings(params)` `{OperationId, OrderId, EmployeeId, From, To, RunningOnly}` | GET bookings |
| | `createBooking(dto)` (reason required) | POST bookings |
| | `editBooking(dto)` (reason required, rowVersion) | POST bookings/edit |
| | `getAttendanceDifferences({From, To, EmployeeId?, EmployeeIds?[], ToleranceMinutes?, PageNumber?, PageSize?})` (array sent as repeated keys) | GET attendance-differences |
| Cost | `runCost(date?)` | POST cost/run |
| | `getCostRuns(params)` `{From, To}` | GET cost/runs |
| | `getCostPostings(params)` `{RunId, WorkOrderId, From, To}` | GET cost/postings |
| Schedule | `runSchedule(asOf?)` | POST schedule/run |
| | `getLoad({From?, Weeks?, WorkCenterId?, Threshold?})` NOT paged (`LoadReportDto`) | GET load |
| | `getDispatch({WorkCenterId (required), ...paging})` | GET dispatch |
| Maintenance | `getMaintenancePlans(params)` `{MachineId, IsActive}` | GET maintenance/plans |
| | `saveMaintenancePlan(dto)` | POST maintenance/plans |
| | `draftDueMaintenance(asOf?)` | POST maintenance/plans/draft-due |
| | `getMaintenanceOrders(params)` `{MachineId, Kind, Status, OpenOnly}` | GET maintenance/orders |
| | `getMaintenanceOrder(id)` | GET maintenance/orders/{id} |
| | `createMaintenanceOrder(dto)` | POST maintenance/orders |
| | `saveMaintenanceOrder(dto)` | POST maintenance/orders/save |
| | `maintenanceOrderAction({id, action, reason?, downtimeEnd?, rowVersion?})` action `start`, `complete`, `cancel`, `request-parts`, `draft-issue` | POST maintenance/orders/action |
| | `addMaintenanceLabour({orderId, employeeId?, workDate?, hours, note?})` | POST maintenance/labour |
| | `getMachineKpis(params)` `{From, To, WorkCenterId, MachineId}` | GET maintenance/kpis |
| | `getOee({From?, To?, WorkCenterId?, MachineId?})` plain array of `OeeDto` | GET oee |
| Dashboard | `getProductionDashboard(asOf?)` | GET dashboard |

Names that differ from the endpoint noun to avoid clashes with `InventoryAPIs.ts`: `getProductionMachines` (not `getMachines`), `getProductionOperations` (not `getOperations`), `getProductionOrders/Order`, `releaseProductionOrder`, `draftProductionOrders`.
Dates in dto/params are ISO strings (`yyyy-mm-dd` or ISO date-time); show them with `dateText()` / the `...Text` field. File uploads: reuse `uploadFile()` of `InventoryAPIs.ts`; the work-instruction document and the outside-return document are numeric ids.

## Enum maps (production.ts), numeric values from `ProductionEnums.cs`
`workCenterKinds` 1 Machine .. 7 Outside, `machineStatuses` 1..4, `inspectionRequirements` 0 None, 1 Self, 2 Qc, 3 ItpPoint, `routingStatuses` 0..1, `orderStatuses` 0..5, `operationStatuses` 0..7 (Waiting, Ready, InProgress, Paused, Done, QcHold, Rejected, Skipped), `bookingKinds` 1..5 (Setup, Run, Rework, Waiting, Cleaning), `bookingSources` 1..2, `reasonKinds` 1..3 (Scrap, Downtime, Waiting), `maintenanceRequestStatuses` 0..3, `maintenanceKinds` 1..2, `maintenanceOrderStatuses` 0..3, `maintenanceIntervalKinds` 1..2 (Days, RunningHours), `qcResults` 1..4 (Accept, Reject, Rework, Hold; `QcResult` of the quality module).
String-valued fields: `dispatchStages` (Ready, Running, Next), `differenceKinds` (Unbooked, OverBooked, NoAttendance), `releaseCheckCodes` (routing, workcenters, drawings, itp, materials), `machineStatusKeys` (dashboard machine status), `productionActionTitles` (Persian title of every action string).
Helpers: `hoursText(decimalHours)` -> `h:mm`, `minutesHoursText(min)`, `percentText(v, digits=1)` (null -> `-`), `qtyText(v)`, `dateText(iso)`, `dateTimeText(iso)`, `persianOf(text, iso)`, `timeOf(iso)`, `badgeOfOrder/Operation/Machine/Routing/Maintenance(status)`, `badgeOfLoad(percent, threshold)`. Dates shown to the user are always Jalali (`jalali()` of `inventory.ts`).

## ProductionRoles (roles.ts)
Mirrors `ProductionRoles` of `ProductionCommon.cs` and the constants in the services. The controllers only need a signed-in user; a refused call is HTTP 200 with `success=false`.
| Name | Roles | Used for |
|---|---|---|
| manager | PRODUCTIONMANAGER | approve-production, release with shortage override |
| supervisor | SHOPSUPERVISOR | skip an operation, edit bookings of a closed day, reason codes |
| readers | PMO, CEO, PRODUCTIONMANAGER, SHOPSUPERVISOR, PLANNING, QC, QA, ENGINEERING, ENGINEER, PROJECTMANAGER, WHSUPERVISOR, WAREHOUSE, MAINTENANCE, FINANCEMANAGER, AUDITOR | GET routings, load, KPIs, OEE; sees all bookings |
| masterReaders | readers + OPERATOR, ASSEMBLER, CONSTRUCTION | GET work centres, machines, standards, reason codes, orders, operations, release-check, bookings (own only for non-readers), dispatch, maintenance requests/plans/orders |
| masterEditors | PRODUCTIONMANAGER, PMO | save work centre |
| machineEditors | masterEditors + MAINTENANCE | save machine info |
| reasonEditors | masterEditors + SHOPSUPERVISOR | save reason code |
| standardEditors | PRODUCTIONMANAGER, ENGINEERING, ENGINEER, PMO | save operation standard |
| routingWriters | PRODUCTIONMANAGER, ENGINEERING, ENGINEER, PMO, PLANNING | open / delete a draft routing revision |
| routingApproveProduction / routingApproveEngineering (= engineering) | PRODUCTIONMANAGER / ENGINEERING, ENGINEER | two-person approval (creator may not approve; two different people) |
| rateSetters | FINANCEMANAGER, FINANCIAL | work-centre hourly rates |
| rateViewers | FINANCEMANAGER, FINANCIAL, PRODUCTIONMANAGER, CEO, PMO, AUDITOR | the rates are filled in the work-centre DTO |
| supervisors | SHOPSUPERVISOR, PRODUCTIONMANAGER | release, close, kit-override, refresh-readiness, skip / complete-outside / rework, create + edit bookings |
| planners | PRODUCTIONMANAGER, SHOPSUPERVISOR, PLANNING, PMO, ENGINEERING, ENGINEER | draft orders, components, cancel (draft), refresh-routing |
| terminal | OPERATOR, SHOPSUPERVISOR, PRODUCTIONMANAGER, ASSEMBLER, CONSTRUCTION | all `terminal/*` calls except machine-down |
| machineDownReporters | terminal + MAINTENANCE | terminal/machine-down |
| inspectors | QC | operations/qc-decision |
| maintenanceWriters | MAINTENANCE, PRODUCTIONMANAGER | plans, draft-due, orders (create/save/action), labour |
| scheduleRunners | PRODUCTIONMANAGER, SHOPSUPERVISOR, PLANNING, PMO | schedule/run |
| costRunners | FINANCEMANAGER, FINANCIAL, PRODUCTIONMANAGER | cost/run |
| costReaders | costRunners + CEO, AUDITOR, PMO | cost/runs, cost/postings |
| payrollSeers | PAYROLL, FINANCEMANAGER, CEO | individual labour amount is not hidden |
| attendanceCheck | SHOPSUPERVISOR, PRODUCTIONMANAGER, HR, CEO, PMO | attendance-differences |
| dashboard | PRODUCTIONMANAGER, SHOPSUPERVISOR, CEO, PMO, PLANNING, MAINTENANCE, QC, QA, FINANCEMANAGER, AUDITOR | GET dashboard |

## Routes (meta.roles in brackets)
| Name | Path | View | roles |
|---|---|---|---|
| MfDashboard | /production/dashboard | Dashboard.vue | dashboard |
| MfWorkCenters | /production/work-centers (alias /production/machines) | WorkCenters.vue (work centres, machines, reason codes) | masterReaders |
| MfRoutings | /production/routings | Routings.vue (operation standards + routing revisions) | readers |
| MfRouting | /production/routings/:id | Routings.vue | readers |
| MfOrders | /production/orders | Orders.vue | masterReaders |
| MfOrder | /production/orders/:id | OrderDetail.vue | masterReaders |
| MfOperation | /production/operations/:id | Orders.vue (QC request link) | masterReaders |
| MfTerminal | /production/terminal | Terminal.vue | terminal |
| MfBookings | /production/bookings | Bookings.vue (+ attendance differences) | masterReaders |
| MfSchedule | /production/schedule (alias /production/load) | Schedule.vue (run, load report, dispatch + job card print) | masterReaders |
| MfMaintenance | /production/maintenance (alias /production/maintenance-orders) | Maintenance.vue | masterReaders |
| MfMaintenanceOrder | /production/maintenance-orders/:id | Maintenance.vue | masterReaders |
| MfMaintenanceRequest | /production/maintenance-requests/:id | Maintenance.vue | masterReaders |
| MfOee | /production/oee | Oee.vue | readers |
| MfCost | /production/cost | Cost.vue | costReaders |
The extra paths are the links the API writes into inbox items and CEO alerts (see mismatch 7); the page agents must read `route.params.id` in Routings.vue, Orders.vue and Maintenance.vue.
Sidebar: section «تولید و نگهداری» (`productionLinks`, route-name prefix `Mf`), items `disabled` when the role lacks; the terminal link is enabled for the terminal roles only.

## Mismatches / things to know
1. `OrderActionDto` comment lists `cancel, close, kit-override, refresh-routing`; the service also accepts `refresh-readiness` (supervisors). Added to `orderAction`.
2. `QcDecisionDto.Result` is the quality enum `QcResult` (1 accept, 2 reject, 3 rework, 4 hold), not a production enum.
3. `GET load`, `GET oee`, `GET dashboard` and the single-record GETs answer the object / a plain array, not a paged list. `GET oee` returns an array of `OeeDto` (the brief says "per work centre").
4. Roles of the brief vs services: routings GET needs `Readers` (OPERATOR / ASSEMBLER / CONSTRUCTION are refused) while work centres, machines, standards, reason codes, orders, operations, dispatch, maintenance lists accept `MasterReaders`. `GET load`, KPIs and OEE need `Readers`. Machine info is also editable by MAINTENANCE; reason codes also by SHOPSUPERVISOR. `terminal/machine-down` also accepts MAINTENANCE. The brief says nothing of these.
5. Bookings: `GET bookings` returns only the caller's own rows to a `MasterReaders` role that is not in `Readers` (operators); they need a personnel record or the call fails.
6. CEO dashboard (`finance/ceo-dashboard`) fields added by production: `productionLateOrders`, `productionMachinesDown`, `productionOverloadWeeks` (weeks above 110 % in the next four weeks), `productionMaintenanceOverdue`; its alerts link to `/production/orders`, `/production/machines`, `/production/load`, `/production/maintenance-orders` (all routed as aliases above). The CEO dashboard page itself is not changed by this foundation.
7. Links written by the API: `/production/orders/{id}` (release gate), `/production/operations/{id}` (QC request for QC users), `/production/maintenance-requests/{id}`, `/production/maintenance-orders/{id}`, `/production/routings/{id}`. There is NO `GET operations/{id}` and `OperationQuery` has no id filter, so the page behind `/production/operations/:id` can only resolve an operation by listing (for example `Contains`/`Status=5` QC hold) and then open its order (`OperationDto.orderId`). A backend `GET operations/{id}` or `OperationQuery.Id` would make this exact.
8. Payroll privacy: `labourAmount` of cost runs / postings is `null` with `labourHidden=true` for roles that must not see a single person's cost (FINANCEMANAGER, PAYROLL, CEO always see it). Show «محرمانه» for that case.
9. `AttendanceDifferenceQuery` is a GET with a list (`EmployeeIds`); `getAttendanceDifferences` sends it as repeated keys. `From` and `To` are required.
10. `DashboardMachineDto.status` is the English enum name (`Down`); use `machineStatusKeys`.
11. Reason codes: `ReasonKind` 1 scrap is required for `scrapReasonCodeId`; 2 downtime and 3 waiting are for pause / machine-down / booking `reasonCodeId` (the service checks the kind).
12. Rule violations are HTTP 200 with `success=false`, which the helpers return as the error (a Persian message from the server).
