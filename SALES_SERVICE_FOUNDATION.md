# فروش و خدمات پس از فروش: زیربنای فرانت

لایهٔ مشترک صفحه‌های «فروش» و «خدمات پس از فروش». بک‌اند: `api/v1/sales` (`src/WebApi/Controllers/Sales/SalesControllers.cs`) و `api/v1/service` (`src/WebApi/Controllers/Service/ServiceControllers.cs`)؛ در موارد اختلاف با `sales-service-backend-brief.md`، مرجع صحیح کنترلرها، DTOها و سرویس‌ها هستند. شناسه‌ها و کد به همان صورت انگلیسی مانده‌اند.

## فایل‌ها
- `src/api/SalesAPIs.ts` و `src/api/ServiceAPIs.ts`: برای هر endpoint یک تابع (فروش 55 و خدمات 36 endpoint). قراردادها مانند `ProductionAPIs.ts`: هر تابع `[data, null]` یا `[null, error]` برمی‌گرداند، داده camelize شده است، فهرست‌ها صفحه‌بندی‌شده‌اند `{ data, totalItems }` (با `rowsOf()` / `usePagedList()`)، و هر تابع یک doc comment دارد شامل فیلدهای DTO، رشته‌های action و اینکه چه کسانی می‌توانند آن را فراخوانی کنند. پارامترهای query به‌صورت PascalCase هستند (`PageNumber`، `PageSize`، `Contains`، `Status`، ...). نقض قواعد، HTTP 200 با `success=false` است و به‌عنوان خطا برمی‌گردد (پیام فارسی از سرور).
- `src/composables/sales.ts` و `src/composables/service.ts`: نگاشت برچسب فارسی همهٔ enumها با مقادیر عددی دقیق، عنوان فارسی action‌ها، عنوان کلیدهای تنظیمات، توابع نشان (badge) و توابع کمکی. توابع `dateText`، `dateTimeText`، `hoursText`، `percentText`، `persianOf`، `qtyText` (از `production.ts`) و `jalali`، `rial` (از `inventory.ts`) از هر دو فایل دوباره صادر می‌شوند. توابع جدید: `rialText(v)` (null -> `-`)، `protectedRialText(v, hidden)`، `labourText(v, hidden)` (نمایش «محرمانه (حقوق)» وقتی `hidden`)، `periodText(yyyymm)` و ثابت `PROTECTED_TEXT`.
- `src/composables/roles.ts`: `SALES` (24، «فروش و بازاریابی»)، `SERVICE` (25، «خدمات پس از فروش») در `Roles` / `RoleIds` / `roleTitles` و آبجکت‌های `SalesRoles` و `ServiceRoles`.
- `src/router/index.ts`: مسیرهای `/sales/...` و `/service/...`؛ `src/components/common/KsSidebar/KsSidebar.vue`: بخش‌های «فروش» (پیشوند نام مسیر `Sl`) و «خدمات پس از فروش» (پیشوند `Sv`)، در صورت نداشتن نقش لازم آیتم‌ها `disabled` می‌شوند؛ `src/views/sales/*.vue` و `src/views/service/*.vue` صفحه‌های موقت (h1 + «در دست ساخت»).
- قواعد الزام‌آور: فقط ریال (هیچ ارز یا واحد پولی نمایش داده نشود)، تاریخ‌های نمایشی فقط جلالی (فیلدهای `...Text` یا `dateText()`)، مبالغ محافظت‌شدهٔ حقوق و دستمزد `null` با پرچم `*Hidden` می‌آیند و باید «محرمانه (حقوق)» نشان داده شوند. بارگذاری فایل: `uploadFile()` در `InventoryAPIs.ts` شناسهٔ GUID برمی‌گرداند که برای `reportDocumentId`، `technicalDocumentId` و `commercialDocumentId` مناسب است.

## توابع API فروش (SalesAPIs.ts)
همهٔ GETهای فهرستی `params?: SalesPageParams` می‌گیرند (`PageNumber, PageSize, Contains` + فیلدهای query).

| حوزه | تابع | Endpoint |
|---|---|---|
| مشتریان | `getSlCustomers(params)` `{Status, Type, OwnerUserId}` | GET customers |
| | `getSlCustomer(id)` | GET customers/{id} |
| | `saveSlCustomer(dto)` | POST customers |
| | `setSlCredit({customerId, creditLimitRial?})` | POST customers/credit |
| مخاطبان | `getSlContacts(params)` `{CustomerId}` / `saveSlContact(dto)` | GET / POST contacts |
| سایت‌ها | `getSlSites(params)` `{CustomerId}` / `saveSlSite(dto)` | GET / POST sites |
| فعالیت‌ها | `getSlActivities(params)` `{CustomerId, InquiryId, ProposalId}` / `saveSlActivity(dto)` | GET / POST activities |
| پیوند قدیمی | `linkSlLegacy()` (بدون بدنه) | POST customers/link-legacy |
| | `linkSlManual({sourceType, sourceId, customerId})` | POST customers/link |
| | `getSlLinks(params)` `{CustomerId, SourceType}` | GET customers/links |
| | `getSlUnlinked()` آرایهٔ ساده | GET customers/unlinked |
| تنظیمات | `getSlSettings()` آرایهٔ ساده / `saveSlSetting({key, value?})` | GET / POST settings |
| کدهای علت | `getSlReasonCodes(params)` `{Kind, IsActive}` / `saveSlReasonCode(dto)` | GET / POST reason-codes |
| استعلام | `getSlInquiries(params)` `{Status, CustomerId, ProductType}` | GET inquiries |
| | `getSlInquiry(id)` | GET inquiries/{id} |
| | `saveSlInquiry(dto)` | POST inquiries |
| | `inquiryAction({id, action, reason?, rowVersion?})` action `review`, `cancel` | POST inquiries/action |
| تصمیم مناقصه | `getSlBidDecisions(params)` `{Status, InquiryId}` | GET bid-decisions |
| | `getSlBidDecision(inquiryId)` (شناسهٔ استعلام، نه شناسهٔ تصمیم) | GET bid-decisions/{inquiryId} |
| | `saveSlBidDecision(dto)` (چک‌لیست) | POST bid-decisions |
| | `submitSlBidDecision({inquiryId, rowVersion?})` | POST bid-decisions/submit |
| | `decideSlBid({inquiryId, decision, reason?, rowVersion?})` decision `bid`, `nobid` | POST bid-decisions/decide |
| برآورد | `getSlEstimates(params)` `{InquiryId, Status}` / `getSlEstimate(id)` / `saveSlEstimate(dto)` | GET estimates، GET estimates/{id}، POST estimates |
| | `slEstimateFromEquipment({inquiryId, equipments[], overheadPercent?, contingencyPercent?, targetMarginPercent?})` | POST estimates/from-equipment |
| | `estimateAction({id, action, rowVersion?})` action `finalize`, `revise` | POST estimates/action |
| پیشنهاد قیمت | `getSlProposals(params)` `{Status, InquiryId, CustomerId}` / `getSlProposal(id)` / `saveSlProposal(dto)` | GET proposals، GET proposals/{id}، POST proposals |
| | `proposalAction({id, action, note?, rowVersion?})` action `approve`, `submit`, `revise`, `cancel` | POST proposals/action |
| | `slProposalResult({id, result, reasonId?, competitor?, competitorPrice?, lessonsLearned?, rowVersion?})` result `won`، `lost` | POST proposals/result |
| | `getSlWinLoss({From?, To?})` بدون صفحه‌بندی | GET statistics/win-loss |
| فهرست قیمت | `getSlPriceLists(params)` `{Status}` / `getSlPriceList(id)` / `saveSlPriceList(dto)` | GET price-lists، GET price-lists/{id}، POST price-lists |
| | `priceListAction({id, action, rowVersion?})` action `approve`, `retire` | POST price-lists/action |
| | `suggestSlPrices({itemIds[], markupPercent?})` آرایهٔ ساده | POST price-lists/suggest |
| پیش‌فاکتور | `getSlQuotations(params)` `{Status, CustomerId}` / `getSlQuotation(id)` / `saveSlQuotation(dto)` | GET quotations، GET quotations/{id}، POST quotations |
| | `quotationAction({id, action, rowVersion?})` action `send`, `accept`, `reject`, `expire`, `cancel` | POST quotations/action |
| | `slQuotationToOrder({quotationId, customerOrderNumber?, requestedDeliveryDate?})` | POST quotations/to-order |
| سفارش فروش | `getSlOrders(params)` `{Status, CustomerId, CreditPending}` / `getSlOrder(id)` / `saveSlOrder(dto)` | GET orders، GET orders/{id}، POST orders |
| | `slOrderAction({id, action, reason?, rowVersion?})` action `confirm`, `cancel`, `close` | POST orders/action |
| | `creditApproveSlOrder({id, note?, rowVersion?})` | POST orders/credit-approve |
| | `draftSlInvoice({orderId})` | POST orders/draft-invoice |
| داشبورد | `getSlDashboard(asOf?)` | GET dashboard |

## توابع API خدمات (ServiceAPIs.ts)
| حوزه | تابع | Endpoint |
|---|---|---|
| تنظیمات | `getSvSettings()` آرایهٔ ساده / `saveSvSetting({key, text})` | GET / POST settings |
| جدول SLA | `getSvSlaRules()` آرایهٔ ساده / `saveSvSlaRule({priority, responseHours, resolutionHours?, isActive?})` | GET / POST sla-rules |
| واحدها | `getSvUnits(params)` `{CustomerId, Status, NeedsConfirm, AsOf}` | GET units |
| | `getSvUnit(id, asOf?)` | GET units/{id} |
| | `getSvAsBuilt(id)` | GET units/{id}/as-built |
| | `saveSvUnit(dto)` | POST units |
| | `svUnitAction({id, action, note?, rowVersion?})` action `confirm`, `decommission`, `reactivate` | POST units/action |
| درخواست‌ها | `getSvRequests(params)` `{Status, Priority, CustomerId, UnitId, OpenOnly, Breached, AsOf}` | GET requests |
| | `getSvRequest(id, asOf?)` | GET requests/{id} |
| | `saveSvRequest(dto)` | POST requests |
| | `svRequestAction({id, action, userId?, resolution?, at?, rowVersion?})` action `assign`, `progress`, `wait`, `resolve`, `close` | POST requests/action |
| دستورهای خدمات | `getSvOrders(params)` `{Status, RequestId, UnitId, Decision}` | GET orders |
| | `getSvOrder(id)` / `saveSvOrder(dto)` | GET orders/{id}، POST orders |
| | `svOrderAction({id, action, reason?, rowVersion?})` action `confirm`, `start`, `cancel`, `close` | POST orders/action |
| | `saveSvTimeEntry({orderId, employeeId, workDate?, hours, kind, note?})` / `removeSvTimeEntry({orderId, rowId})` | POST orders/time-entries، POST orders/time-entries/remove |
| | `saveSvMeasurement({orderId, name, value, unit, note?})` / `removeSvMeasurement({orderId, rowId})` | POST orders/measurements، POST orders/measurements/remove |
| | `decideSvOrder({orderId, decision, causeIsMisuse?, note?, rowVersion?})` decision 1..3 | POST orders/decision |
| | `requestSvGoodwill({orderId, reason})` | POST orders/goodwill-request |
| | `draftSvInvoice({id, billableLabourRateRial?})` (id = شناسهٔ دستور) | POST orders/draft-invoice |
| قراردادها (LTSA) | `getSvContracts(params)` `{Status, CustomerId}` / `getSvContract(id)` / `saveSvContract(dto)` | GET contracts، GET contracts/{id}، POST contracts |
| | `svContractAction({id, action, rowVersion?})` action `activate`, `cancel` | POST contracts/action |
| کارها | `runSvJobs(asOf?)` | POST jobs/run |
| دستمزد محرمانه | `runSvLabourAggregate(asOf?)` | POST labour/aggregate |
| نظرسنجی | `getSvSurveys(params)` `{Status, Kind, CustomerId}` / `getSvSurvey(id)` | GET surveys، GET surveys/{id} |
| | `svSurveyAction({id, action, rowVersion?})` action `send`, `skip` | POST surveys/action |
| | `answerSvSurvey({id, scores[], comments?, rowVersion?})` | POST surveys/answer |
| داشبورد و KPI | `getSvDashboard(asOf?)` / `getSvKpis({From?, To?, AsOf?})` بدون صفحه‌بندی | GET dashboard، GET kpis |

نکته: مسیرهای دستورهای خدمات زیر `api/v1/service/orders` هستند (کنترلر جدا)، و `uploadFile()` موجود برای بارگذاری گزارش امضاشده کافی است.

## نگاشت enumها
مقادیر عددی از `SalesEnums.cs` و `ServiceEnums.cs`.

### sales.ts
| نگاشت | مقادیر |
|---|---|
| `customerTypes` | 1 EndUser، 2 Epc، 3 Distributor، 4 Government |
| `industries` | 1 OilGas، 2 Petrochemical، 3 Power، 4 Steel، 5 Sugar، 9 Other |
| `customerStatuses` | 1 Prospect، 2 Active، 3 Inactive |
| `activityKinds` | 1 Call، 2 Meeting، 3 Email، 4 Visit، 5 Letter |
| `productTypes` | 1 SteamTurbine، 2 OrcUnit، 3 Condenser، 4 Pump، 5 LubeOilSystem، 6 Valve، 7 SpareParts، 8 Service، 9 Other |
| `inquiryStatuses` | 1 Received، 2 UnderReview، 3 NoBid، 4 Bidding، 5 Submitted، 6 Won، 7 Lost، 8 Cancelled |
| `bidStatuses` | 1 Draft، 2 WaitingDecision، 3 Bid، 4 NoBid |
| `sanctionChecks` | 0 NotChecked، 1 Clear، 2 Concern |
| `estimateStatuses` | 1 Draft، 2 Final، 3 Superseded |
| `costKinds` | 1 Material، 2 Labour، 3 Engineering، 4 Subcontract، 5 PackingTransport |
| `costSources` | 0 Manual، 1 AverageCost، 2 LastPurchase، 3 LastPurchaseOrder، 4 WorkCenterRate |
| `proposalStatuses` | 1 Draft، 2 Approved، 3 Submitted، 4 Revised، 5 Won، 6 Lost، 7 Cancelled |
| `reasonKinds` | 1 Won، 2 Lost، 3 NoBid |
| `linkSources` / `linkMethods` | 1 Contract، 2 Tender / 1 LegacyMatch، 2 Manual، 3 FromProposal |
| `priceListStatuses` | 1 Draft، 2 Approved، 3 Retired |
| `quotationStatuses` | 1 Draft، 2 Sent، 3 Accepted، 4 Rejected، 5 Expired، 6 Cancelled |
| `orderStatuses` | 1 Draft، 2 Confirmed، 3 PartlyDelivered، 4 Delivered، 5 Invoiced، 6 Closed، 7 Cancelled |

نگاشت‌های رشته‌ای: `bidDecisions` (`bid`, `nobid`)، `proposalResults` (`won`, `lost`)، `salesActionTitles` (عنوان فارسی هر action)، `salesSettingTitles`.
توابع badge: `badgeOfInquiry/Bid/Estimate/Proposal/PriceList/Quotation/SalesOrder`.

### service.ts
| نگاشت | مقادیر |
|---|---|
| `priorities` | 1 Critical، 2 High، 3 Normal، 4 Low |
| `requestTypes` | 1 Breakdown، 2 Inspection، 3 Commissioning، 4 Overhaul، 5 SpareParts، 6 TechnicalQuery، 7 Complaint |
| `requestStatuses` | 1 Open، 2 Assigned، 3 InProgress، 4 WaitingCustomer، 5 Resolved، 6 Closed |
| `warrantySources` | 0 None، 1 Months، 2 Guarantee، 3 Manual |
| `unitStatuses` | 1 InWarranty، 2 OutOfWarranty، 3 UnderLtsa، 4 Decommissioned |
| `orderKinds` | 1 Field، 2 Workshop |
| `orderStatuses` | 1 Draft، 2 Planned، 3 InProgress، 4 Closed، 5 Cancelled |
| `warrantyDecisions` | 0 None، 1 Warranty، 2 Billable، 3 Goodwill |
| `timeKinds` | 1 Travel، 2 Work، 3 Waiting |
| `contractStatuses` | 1 Draft، 2 Active، 3 Expired، 4 Cancelled |
| `spareSources` | 1 Manual، 2 MdrList |
| `surveyKinds` | 1 Delivery، 2 Service |
| `surveyStatuses` | 1 Drafted، 2 Sent، 3 Answered، 4 Skipped |

`productTypes` از `sales.ts` دوباره صادر می‌شود. دیگر موارد: `serviceActionTitles`، `serviceSettingTitles`، `productTypeName()` و توابع badge `badgeOfPriority/Request/Unit/ServiceOrder/Decision/ServiceContract/Survey/Score`. وضعیت و نوع اسناد قطعات (`InventoryDocumentType/Status`) را `documentTypes` و `documentStatuses` در `inventory.ts` پوشش می‌دهند.

## نقش‌ها (roles.ts)
کنترلرها فقط به یک کاربر واردشده نیاز دارند؛ فراخوانی ردشده، HTTP 200 با `success=false` است. تفکیک وظایف (نویسندهٔ سند نمی‌تواند همان را تأیید کند) را API بررسی می‌کند.

### SalesRoles (بازتاب `SalesCommon.cs` و بررسی‌های درون سرویس‌ها)
| نام | نقش‌ها | کاربرد |
|---|---|---|
| sales / ceo | SALES / CEO | |
| readers | SALES, CEO, PMO, COMMERCIAL, FINANCEMANAGER, FINANCIAL, PROJECTMANAGER, PLANNING, AUDITOR, SERVICE | GET مشتریان، مخاطبان، سایت‌ها، فعالیت‌ها، پیوندها، تنظیمات، کدهای علت، استعلام‌ها، تصمیم‌های مناقصه، فهرست‌های قیمت، پیش‌فاکتورها، سفارش‌ها، داشبورد |
| commercialReaders | SALES, CEO, PMO, COMMERCIAL, FINANCEMANAGER, AUDITOR | GET برآوردها، پیشنهادها، آمار برد و باخت، پیشنهاد قیمت (suggest) |
| masterWriters | SALES, PMO | ذخیرهٔ مشتری، مخاطب، سایت، کد علت |
| writers | SALES | استعلام، چک‌لیست و ارسال تصمیم مناقصه، برآورد، پیشنهاد (ذخیره، ارسال، بازنگری، لغو)، فهرست قیمت (ذخیره)، پیش‌فاکتور، سفارش، پیش‌نویس فاکتور |
| activityWriters | SALES, SERVICE | ذخیرهٔ فعالیت (SERVICE فقط فعالیت‌های خودش را ویرایش می‌کند) |
| proposalApprovers | SALES, CEO | approve و ثبت نتیجه؛ در صورت زیر بهای تمام‌شده، بالاتر از سقف تأیید یا تعیین‌نشدن سقف، فقط CEO |
| priceListApprovers | SALES, CEO, FINANCEMANAGER | approve و retire فهرست قیمت (فرد غیر از ویرایشگر) |
| creditSetters | FINANCEMANAGER | POST customers/credit |
| creditViewers | FINANCEMANAGER, FINANCIAL | `creditLimitRial` در DTO مشتری پر می‌شود (برای دیگران null) |
| identityChangers | FINANCEMANAGER, FINANCIAL | تغییر نام، کد اقتصادی، شناسه ملی مشتری موجود |
| creditApprovers | FINANCEMANAGER | POST orders/credit-approve |
| contractMakers | SALES, CEO | proposals/result با `won` (ساخت قرارداد) |
| bidDeciders | CEO | POST bid-decisions/decide |
| settingSetters | CEO | POST settings |
| legacyLinkers | SALES, PMO, CEO | customers/link-legacy و customers/link |

### ServiceRoles (بازتاب `SvCommon.cs` و سرویس‌ها)
| نام | نقش‌ها | کاربرد |
|---|---|---|
| service / ceo | SERVICE / CEO | |
| readers | SERVICE, CEO, PMO, SALES, COMMERCIAL, FINANCEMANAGER, FINANCIAL, PROJECTMANAGER, QA, QC, AUDITOR | همهٔ GETها |
| writers | SERVICE | همهٔ نوشتن‌ها: واحد، درخواست، دستور (ذخیره، action، زمان، اندازه‌گیری)، قرارداد، تصمیم گارانتی و قابل‌فاکتور، درخواست حسن نیت، پیش‌نویس فاکتور |
| settingSetters | CEO | POST settings |
| slaWriters | SERVICE, CEO | POST sla-rules |
| goodwillDeciders | CEO | تصمیم حسن نیت (Goodwill) |
| jobRunners | SERVICE, CEO | POST jobs/run |
| labourRunners | FINANCEMANAGER, CEO | POST labour/aggregate |
| surveyWorkers | SERVICE, SALES | send، skip و answer نظرسنجی |
| payrollSeers | PAYROLL, FINANCEMANAGER, CEO | `labourAmount` و `totalCost` دستور، `warrantyCost*` داشبورد و KPI؛ برای دیگران null با `*Hidden` |

## مسیرها (meta.roles در کروشه؛ نام مسیر / View)
### فروش (پیشوند `Sl`، View در `src/views/sales/`)
| Name | Path | View | roles |
|---|---|---|---|
| SlDashboard | /sales/dashboard | Dashboard.vue | readers |
| SlCustomers، SlCustomer | /sales/customers، /sales/customers/:id | Customers.vue (مشتریان، مخاطبان، سایت‌ها، فعالیت‌ها، پیوند قدیمی) | readers |
| SlInquiries | /sales/inquiries | Inquiries.vue | readers |
| SlInquiry | /sales/inquiries/:id | InquiryDetail.vue (تصمیم شرکت در مناقصه) | readers |
| SlBidDecisions | /sales/bid-decisions | Inquiries.vue | readers |
| SlBidDecision | /sales/bid-decisions/:id | InquiryDetail.vue (`id` = شناسهٔ استعلام) | readers |
| SlEstimates / SlEstimate | /sales/estimates، /sales/estimates/:id | Estimates.vue / EstimateEdit.vue | commercialReaders |
| SlProposals، SlProposal | /sales/proposals، /sales/proposals/:id | Proposals.vue (تأیید، نتیجه، Won به قرارداد) | commercialReaders |
| SlPriceLists | /sales/price-lists | PriceLists.vue | readers |
| SlQuotations | /sales/quotations | Quotations.vue | readers |
| SlOrders، SlOrder | /sales/orders، /sales/orders/:id | Orders.vue (تأیید اعتبار، تحویل‌ها، پیش‌نویس فاکتور) | readers |
| SlSettings | /sales/settings | Settings.vue (سقف تأیید، کدهای علت) | readers |

### خدمات پس از فروش (پیشوند `Sv`، View در `src/views/service/`)
| Name | Path | View | roles |
|---|---|---|---|
| SvDashboard | /service/dashboard | Dashboard.vue | readers |
| SvUnits، SvUnit | /service/units، /service/units/:id | Units.vue (پایگاه نصب‌شده + as-built) | readers |
| SvRequests | /service/requests | Requests.vue | readers |
| SvRequest | /service/requests/:id | RequestDetail.vue | readers |
| SvOrders | /service/orders | Orders.vue | readers |
| SvOrder | /service/orders/:id | OrderDetail.vue (تصمیم، زمان، قطعات، اندازه‌گیری، بستن با گزارش امضاشده) | readers |
| SvContracts، SvContract | /service/contracts، /service/contracts/:id | Contracts.vue (LTSA) | readers |
| SvSurveys، SvSurvey | /service/surveys، /service/surveys/:id | Surveys.vue | readers |
| SvSettings | /service/settings | Settings.vue (جدول SLA، روزهای پیش‌نویس) | readers |
| SvLabour | /service/labour | Labour.vue (اجرای ماهانهٔ دستمزد محرمانه) | labourRunners |

مسیرهای اضافی همان پیوندهایی‌اند که بک‌اند در آیتم‌های صندوق ورودی و هشدارهای مدیرعامل می‌نویسد: `/sales/bid-decisions/{inquiryId}`، `/sales/proposals/{id}`، `/sales/orders/{id}`، `/sales/inquiries`، `/sales/proposals`، `/sales/bid-decisions`، `/service/units/{id}`، `/service/requests[/{id}]`، `/service/orders[/{id}]`، `/service/surveys/{id}`، `/service/contracts/{id}`. صفحه‌ها باید `route.params.id` را بخوانند.

## ناسازگاری‌ها / نکاتی که باید دانست
1. تأیید سفارش بالاتر از سقف اعتبار: `orders/action` با `confirm` در این حالت `success=false` (با علت) برمی‌گرداند، اما سرور همان لحظه `creditApprovalRequired=true` را ذخیره و گیت مدیر مالی را باز کرده است؛ صفحه باید پیام را نشان دهد و سفارش را دوباره بخواند (دکمهٔ «تأیید اعتبار» برای FINANCEMANAGER).
2. تصمیم مناقصه با شناسهٔ استعلام کار می‌کند (`GET bid-decisions/{inquiryId}`، `SlBidActionDto.InquiryId`) و پیوند صندوق ورودی `/sales/bid-decisions/{inquiryId}` نیز همان را دارد؛ مسیر `SlBidDecision` به `InquiryDetail.vue` می‌رود و `:id` شناسهٔ استعلام است.
3. `SlQuotationActionDto` در توضیح خود فقط `send, accept, reject, cancel` را فهرست کرده است؛ سرویس `expire` را هم می‌پذیرد (پیش‌فاکتور ارسال‌شدهٔ گذشته از اعتبار). به `quotationAction` اضافه شد.
4. نقش‌ها: GET برآورد، پیشنهاد، آمار برد و باخت و پیشنهاد قیمت به `commercialReaders` نیاز دارد (کوچک‌تر از `readers`: PROJECTMANAGER، PLANNING، FINANCIAL و SERVICE رد می‌شوند)، اما GET مشتری، استعلام، فهرست قیمت، پیش‌فاکتور و سفارش `readers` را می‌پذیرد. منوی «برآوردها» و «پیشنهادهای قیمت» برای بقیه `disabled` است. brief دربارهٔ این تفکیک چیزی نمی‌گوید.
5. `SlProposalService.ResultAsync` در ابتدا `proposalApprovers` (SALES, CEO) را می‌خواهد، سپس برای `won` دوباره `contractMakers` (همان دو نقش) را؛ عملاً هر دو یکسان‌اند.
6. تأیید پیشنهاد: اگر قیمت از بهای تمام‌شدهٔ برآورد کمتر باشد (`belowCost`)، بالاتر از سقف باشد یا سقف تعیین نشده باشد، فقط CEO تأیید می‌کند (`needsCeo`)؛ تأیید زیر بهای تمام‌شده نیاز به `note` دارد؛ فردی که پیشنهاد یا برآورد را ویرایش کرده نمی‌تواند تأیید کند (پیام «تفکیک وظایف»). تا زمانی که مدیرعامل `ProposalApprovalThresholdRial` را تعیین نکند، فقط مدیرعامل تأیید می‌کند. تأیید فقط وقتی ممکن است که برآورد هنوز `Final` باشد.
7. `GET customers/unlinked`، `GET settings` (فروش و خدمات)، `GET sla-rules`، `POST price-lists/suggest`، `GET statistics/win-loss`، `GET dashboard`، `GET kpis`، `GET units/{id}/as-built` و GETهای تک‌رکورد، شیء یا آرایهٔ ساده برمی‌گردانند نه فهرست صفحه‌بندی‌شده. `win-loss` و `kpis` پارامتر صفحه‌بندی ندارند.
8. `SlSettingDto.value` عدد (ریال) است ولی `SvSettingDto` مقدار متنی `text` با `effective` (مقدار جاری، شامل پیش‌فرض) دارد. تنها کلید فروش `ProposalApprovalThresholdRial` است؛ کلیدهای خدمات `CostCenterCode`، `LabourWorkCenterCode`، `SurveyLeadDays`، `VisitLeadDays`.
9. SLA: مقدار پیش‌فرضی در جدول SLA نیست؛ اولویتی که ردیف ندارد، `slaMissing=true` و موعد خالی دارد. `breached`، `responseBreached` و `resolutionBreached` به `AsOf` وابسته‌اند.
10. بستن دستور خدمات (`close`) فقط برای دستور «در حال انجام» است و `reportDocumentId` (گزارش امضاشده) را الزامی می‌کند؛ همچنین تصمیم گارانتی غیر از `None` لازم است (به‌جز بازدید LTSA). `reportDocumentId` را با `saveSvOrder` ثبت کنید، سپس `close`. تصمیم Warranty/Billable را نویسندهٔ دستور نمی‌تواند خودش بگیرد (دو نفر)؛ Goodwill ابتدا `requestSvGoodwill` و سپس فقط تصمیم CEO.
11. محرمانگی حقوق: `SvOrderDto.labourAmount` و `totalCost` برای نقش‌های خارج از `payrollSeers` `null` با `labourHidden=true` است؛ `labourAggregatePending` یعنی دستمزد این دستور در ثبت ماهانهٔ تجمیعی می‌آید (`labourAggregatePeriod` به‌صورت yyyymm). `SvDashboardDto.warrantyCostYtd` با `warrantyCostHidden` و `SvProductKpiDto.warrantyCost` با `warrantyCostHidden` همین رفتار را دارند. در داشبورد مدیرعامل `serviceWarrantyCostYtd` / `serviceWarrantyCostHidden` است.
12. `labour/aggregate` فقط FINANCEMANAGER و CEO را می‌پذیرد (نه SERVICE)، و `jobs/run` فقط SERVICE و CEO؛ هر دو برای `asOf` در آینده رد می‌شوند مگر تنظیم سرور `Service:AllowFutureAsOf` روشن باشد.
13. `SvDraftInvoiceDto.id` شناسهٔ دستور خدمات است (نه فاکتور)، در حالی که `SlDraftInvoiceDto` فیلد `orderId` دارد.
14. `SvAsBuiltDto.trace` درخت as-built را از ردیابی انبار می‌خواند و برای واحد بدون `serialUnitId` خالی است.
15. داشبورد مدیرعامل (`finance/ceo-dashboard`) فیلدهایی که فروش و خدمات افزوده‌اند: `salesOpenInquiries`، `salesPipelineValue`، `salesBidDecisionsWaiting`، `salesProposalsWaitingApproval`، `salesProposalsDueSoon`، `salesBelowCostProposals`، `salesOrdersWaitingCredit`، `salesHitRatePercent`، `serviceOpenRequests`، `serviceSlaBreaches`، `serviceUnitsInWarranty`، `serviceWarrantyCostYtd`، `serviceWarrantyCostHidden`، `serviceGoodwillWaiting`، `serviceSurveyAverageScore`؛ هشدارهای آن به `/sales/inquiries`، `/sales/proposals`، `/sales/bid-decisions`، `/service/requests`، `/service/orders` پیوند می‌دهند (همه مسیردهی شده‌اند). صفحهٔ داشبورد مدیرعامل در این زیربنا تغییر نکرده است.
16. پیوند صندوق ورودی `/service/surveys/{id}` برای نظرسنجی و `/service/units/{id}` برای واحد پیش‌نویس سیستم هر دو به صفحهٔ فهرست همان بخش مسیردهی شده‌اند؛ صفحه باید `route.params.id` را بخواند و آیتم را باز کند. اگر کاربر نقش لازم را نداشته باشد، روتر او را به داشبورد برمی‌گرداند.
17. نقض قواعد، HTTP 200 با `success=false` است که `call()` آن را به‌عنوان خطا برمی‌گرداند (پیام فارسی از سرور).
