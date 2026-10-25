ترجمه فارسی سند اصلی (1405/08/03)؛ شناسه‌ها و کد به همان صورت انگلیسی مانده‌اند.

# تولید / کارگاه (MES) و نگهداری و تعمیرات: زیربنای فرانت

لایهٔ مشترک صفحه‌های «تولید و نگهداری». بک‌اند: `api/v1/production` (کنترلرها در `src/WebApi/Controllers/Production/ProductionControllers.cs` و `ProductionPartBControllers.cs`؛ در موارد اختلاف با `production-backend-brief.md`، مرجع صحیح کنترلر و سرویس‌ها هستند).

## فایل‌ها
- `src/api/ProductionAPIs.ts` برای هر endpoint یک تابع (تمام بخش A و B). قراردادها مانند `OfficeAPIs.ts`: هر تابع `[data, null]` یا `[null, error]` برمی‌گرداند، داده camelize شده است، فهرست‌ها صفحه‌بندی‌شده‌اند `{ data, totalItems }` (با `rowsOf()` / `usePagedList()` خوانده شوند)، و هر تابع یک توضیح (doc comment) دارد شامل فیلدهای DTO، رشته‌های مجاز action و اینکه چه کسانی می‌توانند آن را فراخوانی کنند. پارامترهای query به‌صورت PascalCase هستند (`PageNumber`، `PageSize`، `Contains`، `Status`، ...).
- `src/composables/production.ts` نگاشت برچسب‌های فارسی، توابع کمکی قالب‌بندی و توابع کمکی نشان (badge).
- `src/composables/roles.ts` شامل `Roles/RoleIds/roleTitles` + PRODUCTIONMANAGER (20)، SHOPSUPERVISOR (21)، OPERATOR (22)، MAINTENANCE (23) و `ProductionRoles`.
- `src/router/index.ts` مسیرهای زیر `/production/...`، `src/components/common/KsSidebar/KsSidebar.vue` بخش «تولید و نگهداری»، و `src/views/production/*.vue` صفحه‌های موقت (h1 + «در دست ساخت»).

## توابع API (ProductionAPIs.ts)
همهٔ توابع فهرستی GET یک `params?: ProductionPageParams` می‌گیرند (`PageNumber, PageSize, Contains` + فیلدهای query در کلاس query زبان C#).

| حوزه | تابع | Endpoint |
|---|---|---|
| مراکز کاری | `getWorkCenters(params)` `{Kind, IsActive}` | GET work-centers |
| | `getWorkCenter(id)` | GET work-centers/{id} |
| | `saveWorkCenter(dto)` | POST work-centers |
| | `saveWorkCenterRates({id, machineRateRial?, labourRateRial?})` | POST work-centers/rates |
| ماشین‌ها | `getProductionMachines(params)` `{WorkCenterId, Status}` | GET machines |
| | `saveMachineInfo(dto)` | POST machines/info |
| استانداردها | `getOperationStandards(params)` `{ItemId, ItemGroupId}` | GET operation-standards |
| | `saveOperationStandard(dto)` | POST operation-standards |
| مسیرهای ساخت | `getRoutings(params)` `{ItemId, ItemGroupId, Status}` | GET routings |
| | `getRouting(id)` | GET routings/{id} |
| | `saveRouting({itemId?, itemGroupId?, note?})` | POST routings (یک بازنگری پیش‌نویس باز می‌کند) |
| | `routingAction({id, action, rowVersion})` action `approve-production`, `approve-engineering`, `delete` | POST routings/action |
| کدهای علت | `getReasonCodes(params)` `{Kind, IsActive}` | GET reason-codes |
| | `saveReasonCode(dto)` | POST reason-codes |
| دستورهای تولید | `getProductionOrders(params)` `{Status, WorkOrderId, ContractId, ItemId, OpenOnly}` | GET orders |
| | `getProductionOrder(id)` | GET orders/{id} |
| | `draftProductionOrders({workOrderId?, processId?, itemId?})` | POST orders/draft |
| | `saveComponents({orderId, components[], serialsPlanned?, rowVersion})` | POST orders/components |
| | `getReleaseCheck(id)` | GET orders/{id}/release-check |
| | `releaseProductionOrder({id, overrideReason?, rowVersion?})` | POST orders/release |
| | `orderAction({id, action, reason?})` action `cancel`, `close`, `kit-override`, `refresh-readiness`, `refresh-routing` | POST orders/action |
| عملیات | `getProductionOperations(params)` `{OrderId, WorkCenterId, Status}` | GET operations |
| | `operationAction({id, action, reason?, documentId?})` action `skip`, `complete-outside` | POST operations/action |
| | `qcDecision({operationId, result 1..4, note?, itpActivityId?})` | POST operations/qc-decision |
| | `reworkOperation({operationId, ncrId, note?})` | POST operations/rework |
| ترمینال | `getMyOperations(params)` `{WorkCenterId, Code}` | GET terminal/my-operations |
| | `scanOperations({Code, ...})` | GET terminal/scan |
| | `startOperation({operationId, kind=2, machineId?, note?})` | POST terminal/start |
| | `pauseOperation({operationId, reasonCodeId?, note?})` | POST terminal/pause |
| | `reportQuantity({operationId, goodQty, scrapQty?, scrapReasonCodeId?, reworkQty?, note?})` | POST terminal/report-quantity |
| | `finishOperation(same as reportQuantity)` | POST terminal/finish |
| | `requestQc({operationId, note?})` | POST terminal/request-qc |
| | `reportMachineDown({machineId, operationId?, reasonCodeId?, description?})` | POST terminal/machine-down |
| | `getMaintenanceRequests(params)` `{MachineId, Status}` | GET maintenance-requests |
| ثبت زمان | `getBookings(params)` `{OperationId, OrderId, EmployeeId, From, To, RunningOnly}` | GET bookings |
| | `createBooking(dto)` (ذکر علت الزامی است) | POST bookings |
| | `editBooking(dto)` (ذکر علت الزامی است، rowVersion) | POST bookings/edit |
| | `getAttendanceDifferences({From, To, EmployeeId?, EmployeeIds?[], ToleranceMinutes?, PageNumber?, PageSize?})` (آرایه به‌صورت کلیدهای تکراری ارسال می‌شود) | GET attendance-differences |
| بهای تمام‌شده | `runCost(date?)` | POST cost/run |
| | `getCostRuns(params)` `{From, To}` | GET cost/runs |
| | `getCostPostings(params)` `{RunId, WorkOrderId, From, To}` | GET cost/postings |
| زمان‌بندی | `runSchedule(asOf?)` | POST schedule/run |
| | `getLoad({From?, Weeks?, WorkCenterId?, Threshold?})` بدون صفحه‌بندی (`LoadReportDto`) | GET load |
| | `getDispatch({WorkCenterId (required), ...paging})` | GET dispatch |
| نگهداری و تعمیرات | `getMaintenancePlans(params)` `{MachineId, IsActive}` | GET maintenance/plans |
| | `saveMaintenancePlan(dto)` | POST maintenance/plans |
| | `draftDueMaintenance(asOf?)` | POST maintenance/plans/draft-due |
| | `getMaintenanceOrders(params)` `{MachineId, Kind, Status, OpenOnly}` | GET maintenance/orders |
| | `getMaintenanceOrder(id)` | GET maintenance/orders/{id} |
| | `createMaintenanceOrder(dto)` | POST maintenance/orders |
| | `saveMaintenanceOrder(dto)` | POST maintenance/orders/save |
| | `maintenanceOrderAction({id, action, reason?, downtimeEnd?, rowVersion?})` action `start`, `complete`, `cancel`, `request-parts`, `draft-issue` | POST maintenance/orders/action |
| | `addMaintenanceLabour({orderId, employeeId?, workDate?, hours, note?})` | POST maintenance/labour |
| | `getMachineKpis(params)` `{From, To, WorkCenterId, MachineId}` | GET maintenance/kpis |
| | `getOee({From?, To?, WorkCenterId?, MachineId?})` آرایهٔ ساده از `OeeDto` | GET oee |
| داشبورد | `getProductionDashboard(asOf?)` | GET dashboard |

نام‌هایی که برای جلوگیری از تداخل با `InventoryAPIs.ts` با اسم اسمی endpoint تفاوت دارند: `getProductionMachines` (نه `getMachines`)، `getProductionOperations` (نه `getOperations`)، `getProductionOrders/Order`، `releaseProductionOrder`، `draftProductionOrders`.
تاریخ‌ها در dto/params رشتهٔ ISO هستند (`yyyy-mm-dd` یا ISO date-time)؛ برای نمایش از `dateText()` / فیلد `...Text` استفاده شود. بارگذاری فایل: از `uploadFile()` در `InventoryAPIs.ts` استفاده مجدد شود؛ سند دستورالعمل کار و سند بازگشت از برون‌سپاری شناسهٔ عددی هستند.

## نگاشت enumها (production.ts)، مقادیر عددی از `ProductionEnums.cs`
`workCenterKinds` ‏1 Machine .. 7 Outside، `machineStatuses` ‏1..4، `inspectionRequirements` ‏0 None، 1 Self، 2 Qc، 3 ItpPoint، `routingStatuses` ‏0..1، `orderStatuses` ‏0..5، `operationStatuses` ‏0..7 (Waiting, Ready, InProgress, Paused, Done, QcHold, Rejected, Skipped)، `bookingKinds` ‏1..5 (Setup, Run, Rework, Waiting, Cleaning)، `bookingSources` ‏1..2، `reasonKinds` ‏1..3 (Scrap, Downtime, Waiting)، `maintenanceRequestStatuses` ‏0..3، `maintenanceKinds` ‏1..2، `maintenanceOrderStatuses` ‏0..3، `maintenanceIntervalKinds` ‏1..2 (Days, RunningHours)، `qcResults` ‏1..4 (Accept, Reject, Rework, Hold؛ همان `QcResult` ماژول کیفیت).
فیلدهای با مقدار رشته‌ای: `dispatchStages` (Ready, Running, Next)، `differenceKinds` (Unbooked, OverBooked, NoAttendance)، `releaseCheckCodes` (routing, workcenters, drawings, itp, materials)، `machineStatusKeys` (وضعیت ماشین در داشبورد)، `productionActionTitles` (عنوان فارسی هر رشتهٔ action).
توابع کمکی: `hoursText(decimalHours)` -> `h:mm`، `minutesHoursText(min)`، `percentText(v, digits=1)` (null -> `-`)، `qtyText(v)`، `dateText(iso)`، `dateTimeText(iso)`، `persianOf(text, iso)`، `timeOf(iso)`، `badgeOfOrder/Operation/Machine/Routing/Maintenance(status)`، `badgeOfLoad(percent, threshold)`. تاریخ‌هایی که به کاربر نمایش داده می‌شوند همیشه جلالی هستند (`jalali()` از `inventory.ts`).

## ProductionRoles (roles.ts)
بازتاب `ProductionRoles` در `ProductionCommon.cs` و ثابت‌های درون سرویس‌ها. کنترلرها فقط به یک کاربر واردشده نیاز دارند؛ فراخوانی ردشده، HTTP 200 با `success=false` است.
| نام | نقش‌ها | کاربرد |
|---|---|---|
| manager | PRODUCTIONMANAGER | approve-production، صدور با استثنای کمبود |
| supervisor | SHOPSUPERVISOR | رد کردن (skip) یک عملیات، ویرایش ثبت زمان‌های یک روز بسته‌شده، کدهای علت |
| readers | PMO, CEO, PRODUCTIONMANAGER, SHOPSUPERVISOR, PLANNING, QC, QA, ENGINEERING, ENGINEER, PROJECTMANAGER, WHSUPERVISOR, WAREHOUSE, MAINTENANCE, FINANCEMANAGER, AUDITOR | GET برای routings، load، KPIs، OEE؛ همهٔ ثبت زمان‌ها را می‌بیند |
| masterReaders | readers + OPERATOR, ASSEMBLER, CONSTRUCTION | GET برای مراکز کاری، ماشین‌ها، استانداردها، کدهای علت، دستورهای تولید، عملیات، release-check، ثبت زمان‌ها (برای غیر-readers فقط موارد خودشان)، dispatch، درخواست‌ها/برنامه‌ها/دستورهای نگهداری و تعمیرات |
| masterEditors | PRODUCTIONMANAGER, PMO | ذخیرهٔ مرکز کاری |
| machineEditors | masterEditors + MAINTENANCE | ذخیرهٔ اطلاعات ماشین |
| reasonEditors | masterEditors + SHOPSUPERVISOR | ذخیرهٔ کد علت |
| standardEditors | PRODUCTIONMANAGER, ENGINEERING, ENGINEER, PMO | ذخیرهٔ استاندارد عملیات |
| routingWriters | PRODUCTIONMANAGER, ENGINEERING, ENGINEER, PMO, PLANNING | باز کردن / حذف بازنگری پیش‌نویس مسیر ساخت |
| routingApproveProduction / routingApproveEngineering (= engineering) | PRODUCTIONMANAGER / ENGINEERING, ENGINEER | تأیید دونفره (ایجادکننده نمی‌تواند تأیید کند؛ دو نفر متفاوت) |
| rateSetters | FINANCEMANAGER, FINANCIAL | نرخ‌های ساعتی مرکز کاری |
| rateViewers | FINANCEMANAGER, FINANCIAL, PRODUCTIONMANAGER, CEO, PMO, AUDITOR | نرخ‌ها در DTO مرکز کاری پر می‌شوند |
| supervisors | SHOPSUPERVISOR, PRODUCTIONMANAGER | release، close، kit-override، refresh-readiness، skip / complete-outside / rework، ایجاد + ویرایش ثبت زمان |
| planners | PRODUCTIONMANAGER, SHOPSUPERVISOR, PLANNING, PMO, ENGINEERING, ENGINEER | پیش‌نویس دستورها، اجزا، لغو (پیش‌نویس)، refresh-routing |
| terminal | OPERATOR, SHOPSUPERVISOR, PRODUCTIONMANAGER, ASSEMBLER, CONSTRUCTION | همهٔ فراخوانی‌های `terminal/*` به‌جز machine-down |
| machineDownReporters | terminal + MAINTENANCE | terminal/machine-down |
| inspectors | QC | operations/qc-decision |
| maintenanceWriters | MAINTENANCE, PRODUCTIONMANAGER | برنامه‌ها، draft-due، دستورها (create/save/action)، labour |
| scheduleRunners | PRODUCTIONMANAGER, SHOPSUPERVISOR, PLANNING, PMO | schedule/run |
| costRunners | FINANCEMANAGER, FINANCIAL, PRODUCTIONMANAGER | cost/run |
| costReaders | costRunners + CEO, AUDITOR, PMO | cost/runs، cost/postings |
| payrollSeers | PAYROLL, FINANCEMANAGER, CEO | مبلغ نیروی کار هر فرد پنهان نمی‌شود |
| attendanceCheck | SHOPSUPERVISOR, PRODUCTIONMANAGER, HR, CEO, PMO | attendance-differences |
| dashboard | PRODUCTIONMANAGER, SHOPSUPERVISOR, CEO, PMO, PLANNING, MAINTENANCE, QC, QA, FINANCEMANAGER, AUDITOR | GET dashboard |

## مسیرها (meta.roles در کروشه)
| Name | Path | View | roles |
|---|---|---|---|
| MfDashboard | /production/dashboard | Dashboard.vue | dashboard |
| MfWorkCenters | /production/work-centers (alias /production/machines) | WorkCenters.vue (مراکز کاری، ماشین‌ها، کدهای علت) | masterReaders |
| MfRoutings | /production/routings | Routings.vue (استانداردهای عملیات + بازنگری‌های مسیر ساخت) | readers |
| MfRouting | /production/routings/:id | Routings.vue | readers |
| MfOrders | /production/orders | Orders.vue | masterReaders |
| MfOrder | /production/orders/:id | OrderDetail.vue | masterReaders |
| MfOperation | /production/operations/:id | Orders.vue (پیوند درخواست QC) | masterReaders |
| MfTerminal | /production/terminal | Terminal.vue | terminal |
| MfBookings | /production/bookings | Bookings.vue (+ اختلاف‌های حضور و غیاب) | masterReaders |
| MfSchedule | /production/schedule (alias /production/load) | Schedule.vue (اجرا، گزارش بار کاری، فهرست ارسال کار + چاپ کارت کار) | masterReaders |
| MfMaintenance | /production/maintenance (alias /production/maintenance-orders) | Maintenance.vue | masterReaders |
| MfMaintenanceOrder | /production/maintenance-orders/:id | Maintenance.vue | masterReaders |
| MfMaintenanceRequest | /production/maintenance-requests/:id | Maintenance.vue | masterReaders |
| MfOee | /production/oee | Oee.vue | readers |
| MfCost | /production/cost | Cost.vue | costReaders |
مسیرهای اضافی همان پیوندهایی هستند که API در آیتم‌های صندوق ورودی و هشدارهای مدیرعامل می‌نویسد (ایراد ۷ را ببینید)؛ عامل‌های صفحه باید `route.params.id` را در Routings.vue، Orders.vue و Maintenance.vue بخوانند.
نوار کناری: بخش «تولید و نگهداری» (`productionLinks`، پیشوند نام مسیر `Mf`)، در صورت نداشتن نقش لازم آیتم‌ها `disabled` می‌شوند؛ پیوند ترمینال فقط برای نقش‌های ترمینال فعال است.

## ناسازگاری‌ها / نکاتی که باید دانست
1. توضیح `OrderActionDto` فقط `cancel, close, kit-override, refresh-routing` را فهرست کرده است؛ سرویس `refresh-readiness` را هم می‌پذیرد (سرپرستان). به `orderAction` اضافه شد.
2. `QcDecisionDto.Result` همان enum کیفیت `QcResult` است (1 accept، 2 reject، 3 rework، 4 hold) و enum تولید نیست.
3. `GET load`، `GET oee`، `GET dashboard` و GETهای تک‌رکورد، یک شیء / آرایهٔ ساده برمی‌گردانند، نه فهرست صفحه‌بندی‌شده. `GET oee` آرایه‌ای از `OeeDto` برمی‌گرداند (brief می‌گوید «به‌ازای هر مرکز کاری»).
4. نقش‌ها در brief در برابر سرویس‌ها: GET برای routings به `Readers` نیاز دارد (OPERATOR / ASSEMBLER / CONSTRUCTION رد می‌شوند) در حالی که مراکز کاری، ماشین‌ها، استانداردها، کدهای علت، دستورهای تولید، عملیات، dispatch و فهرست‌های نگهداری و تعمیرات `MasterReaders` را می‌پذیرند. `GET load`، KPIs و OEE به `Readers` نیاز دارند. اطلاعات ماشین را MAINTENANCE هم می‌تواند ویرایش کند؛ کدهای علت را هم SHOPSUPERVISOR. `terminal/machine-down` نیز MAINTENANCE را می‌پذیرد. brief دربارهٔ این‌ها چیزی نمی‌گوید.
5. ثبت زمان: `GET bookings` به نقش `MasterReaders` که در `Readers` نیست (اپراتورها) فقط ردیف‌های خود فراخواننده را برمی‌گرداند؛ آن‌ها به پروندهٔ پرسنلی نیاز دارند وگرنه فراخوانی شکست می‌خورد.
6. داشبورد مدیرعامل (`finance/ceo-dashboard`) فیلدهایی که تولید افزوده است: `productionLateOrders`، `productionMachinesDown`، `productionOverloadWeeks` (هفته‌های بالای 110 درصد در چهار هفتهٔ آینده)، `productionMaintenanceOverdue`؛ هشدارهای آن به `/production/orders`، `/production/machines`، `/production/load`، `/production/maintenance-orders` پیوند می‌دهند (همه به‌صورت alias در بالا مسیردهی شده‌اند). صفحهٔ داشبورد مدیرعامل در این زیربنا تغییر نکرده است.
7. پیوندهایی که API می‌نویسد: `/production/orders/{id}` (گیت صدور)، `/production/operations/{id}` (درخواست QC برای کاربران QC)، `/production/maintenance-requests/{id}`، `/production/maintenance-orders/{id}`، `/production/routings/{id}`. هیچ `GET operations/{id}` وجود ندارد و `OperationQuery` فیلتر id ندارد، پس صفحهٔ پشت `/production/operations/:id` فقط می‌تواند عملیات را از راه فهرست کردن پیدا کند (مثلاً با `Contains`/`Status=5` یعنی QC hold) و سپس دستور تولید آن را باز کند (`OperationDto.orderId`). یک `GET operations/{id}` یا `OperationQuery.Id` در بک‌اند این کار را دقیق می‌کند.
8. محرمانگی حقوق و دستمزد: `labourAmount` در اجراها / ثبت‌های بهای تمام‌شده برای نقش‌هایی که نباید هزینهٔ یک فرد را ببینند `null` با `labourHidden=true` است (FINANCEMANAGER، PAYROLL و CEO همیشه آن را می‌بینند). برای این حالت «محرمانه» نمایش داده شود.
9. `AttendanceDifferenceQuery` یک GET با فهرست (`EmployeeIds`) است؛ `getAttendanceDifferences` آن را به‌صورت کلیدهای تکراری ارسال می‌کند. `From` و `To` الزامی هستند.
10. `DashboardMachineDto.status` نام انگلیسی enum است (`Down`)؛ از `machineStatusKeys` استفاده شود.
11. کدهای علت: `ReasonKind` ‏1 scrap برای `scrapReasonCodeId` الزامی است؛ 2 downtime و 3 waiting برای `reasonCodeId` در pause / machine-down / booking هستند (سرویس نوع را بررسی می‌کند).
12. نقض قواعد، HTTP 200 با `success=false` است که توابع کمکی آن را به‌عنوان خطا برمی‌گردانند (پیام فارسی از سرور).
