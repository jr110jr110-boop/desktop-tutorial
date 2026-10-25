ترجمه فارسی سند اصلی (1405/08/03)؛ شناسه‌ها و کد به همان صورت انگلیسی مانده‌اند.

# ماژول کیفیت (QC + QA)، انتشار ۱ — خلاصه‌ی فنی بک‌اند

تحلیل مبنا: /home/claude/innco/docs/quality-module-spec.md (بخش‌های ۲، ۴، ۵ و پیوست‌ها را بخوانید). این خلاصه‌ی فنی، لایه‌ی تصمیم‌گیری روی آن سند است: هر جا با هم تفاوت دارند، این خلاصه‌ی فنی ارجح است. موارد علامت‌دار ★ تصمیم‌های موقتی‌اند که معمار بر اساس استانداردها گرفته و شرکت می‌تواند بعداً تغییرشان دهد؛ در جاهایی که ذکر شده، آن‌ها را به‌صورت داده‌ی قابل پیکربندی پیاده‌سازی کنید، نه ثابت‌های کدشده.

## قواعد پایه
- مخزن کد: /home/claude/desktop-tutorial (ASP.NET Core روی .NET 10، EF Core 10، SQL Server). کد در `src/`. دقیقاً از الگوهای «موجود» پیروی کنید؛ ابتدا مطالعه کنید:
  - موجودیت‌ها: `src/Entities/Model/NasbNiro/Inventory/*.cs`، `.../Procurement/ProcurementEntities.cs`، `.../Finance/FinanceEntities.cs` (BaseEntity با Deleted/CreateDate/CreatorUserId…؛ `[Timestamp] RowVersion`).
  - پیکربندی EF: `src/DataLayer/Configurations/EF/NasbNiro/Inventory/ProcurementConfigurations.cs` (سبک متد افزونه؛ فراخوانی از `InventoryModelBuilderExtensions.AddInventoryConfigurations`). جدول‌های جدید با پیشوند `Qa` (مثلاً `QaItps`). حذف را محدود کنید مگر برای سطرهای فرزند (Cascade). دقت اعشاری (Decimal precision) همیشه تنظیم شود. طول رشته‌ها همیشه تنظیم شود.
  - سرویس‌ها: `src/Services/NasbNiro/Procurement/ProcurementService.cs` و `PayablesService.cs` (ResponseDto از طریق `ResponseDtoHelper.Ok/BadRequest/NotFound`، `IInventoryUserContext` برای UserId/IsInRole(CSV)، `InventoryNumbers.NextNumberAsync(_uow, sequenceKind, prefix, date, ct)` برای شماره‌ها — انواع توالی جدید را در بازه‌ی 401..420 انتخاب کنید، `InventoryUsers.GetNamesAsync` + `.Name()` برای نام کاربران، پیام‌های خطا به فارسی، بررسی‌های تفکیک وظایف «غیر از سازنده»).
  - کد کیفیتِ موجود که باید «گسترش» یابد، نه تکرار: `Services/NasbNiro/Inventory/InventoryQualityService.cs` (QcInspection ورودی، NonConformance همراه با تأییدها/امتیاز ارفاقی/کمیته/اجرا/بستن)، موجودیت‌ها در `Entities/Model/NasbNiro/Inventory/InventoryWorkflow.cs`.
  - کنترلرها: سبک `src/WebApi/Controllers/Procurement/ProcurementControllers.cs`؛ پیشوند مسیر `api/v1/quality`. فهرست‌های صفحه‌بندی‌شده `.ToPaginatedResultSafe()` برمی‌گردانند.
  - DI: `src/IocConfig/AddCustomServicesExtensions.cs`.
  - نقش‌ها: `src/Entities/Identity/Constants/RoleConstants.cs` + `PermissionHelper.cs` (با HasData بذردهی می‌شود؛ ConcurrencyStamp برای شناسه‌های جدید به‌صورت خودکار جایگزین می‌شود). نام نقش‌ها در JWT با حروف بزرگ است.
  - پیوست‌ها: ذخیره‌سازی فایل موجود از فیلدهای `Guid? ...DocumentId` استفاده می‌کند (به QcInspection.ReportDocumentId نگاه کنید) — همان را به‌کار ببرید، API بارگذاری جدید نسازید.
- بدون تراکنش (EnableRetryOnFailure)؛ در هر عملیات یک SaveChanges؛ پیش از تغییر هر چیز، اعتبارسنجی کامل انجام شود.
- هر endpoint فهرست صفحه‌بندی‌شده باشد؛ هر endpoint نوشتن، DTO ذخیره‌شده را برگرداند.
- بدون اسرار (secret) و بدون نام افراد واقعی. پیام‌های رو به کاربر فارسی؛ کد/توضیحات کد انگلیسی.

## نقش جدید
- ★ `QA` با شناسه‌ی 14، عنوان فارسی «تضمین کیفیت» (کنترل مدارک، ممیزی‌ها، مدیریت CAPA، بازنگری مدیریت). Claims: Traceability، Warehouse (فقط خواندن). به RoleConstants.GetResponseRoles و PermissionHelper اضافه شود (شناسه‌ی claim ها از 1400 به بعد).
- مجموعه‌های نقش (کلاس ثابت‌ها `QualityRoles`): Inspectors = "QC"; QualityManagers = "QC,QA,PMO"; Engineering = "ENGINEERING,ENGINEER"; Readers = "PMO,CEO,QC,QA,ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER,PLANNING,WAREHOUSE,WHSUPERVISOR,COMMERCIAL".

## دامنه‌ی انتشار ۱ (همه‌ی این موارد ساخته شود)

### A. ITP — طرح بازرسی و آزمون (WI-QC-004، شکاف G-08)
- `ItpTemplate` (code, title, productType enum {SteamTurbine, OrcUnit, Condenser, Pump, LubeOilSystem, Valve, Other}, applicableCodes text, revision, isActive) + `ItpTemplateActivity` (seq, group/stage, activity, testType enum (به بند 5.2 سند مبنا + RT, PMI, Hardness, Hydro, RunTest, Performance, Balancing, Painting, Dimensional, VisualWeld, DocumentReview, Packing, Other مراجعه کنید)، acceptanceReference، pointVendor/pointTpi/pointClient enum {None, H, W, R, RA, SW, A}، documentProduced، `requiredReportType` (از نوع TestReportType، nullable)، فهرست `parameters` پیش‌فرض (name, unit, min, max, source) که به‌صورت سطرهای فرزند `ItpTemplateParameter` ذخیره می‌شود).
- بذر (seed) از طریق یک متد سرویس `EnsureSeedAsync`، idempotent، که در اولین GET قالب‌ها فراخوانی می‌شود — نه HasData — یک قالب «Steam turbine — WI-QC-004» از پیوست A سند مبنا و ★ قالب‌های حداقلی برای Pump (API 610: آزمون هیدرواستاتیک 1.5 × MAWP، نگهداری ≥ 30 دقیقه؛ آزمون کارکرد مکانیکی؛ NPSH/عملکرد به‌صورت W)، Condenser (ASME VIII-1: آزمون هیدرو طبق UG-99 با 1.3 × MAWP × نسبت تنش، RT/UT جوش‌های لب‌به‌لب طبق UW-51/52، ابعادی) و Lube oil system (API 614: هیدرو لوله‌کشی/مخازن، پاکیزگی/فلاشینگ، آزمون کارکرد). `source` پارامترهای بذرشده را با شماره‌ی بند مشخص کنید. حدود ارتعاش و دمای یاتاقان در آزمون کارکرد: هیچ مقداری ساختگی نباشد — سطرهای پارامتر با حدود null و source «توسط مهندسی تعیین شود».
- `Itp` برای هر دستور کار (WorkOrderId الزامی، ContractId مشتق‌شده)، کپی‌شده از قالب، revision، وضعیت {Draft, Submitted, ClientApproved, InUse, Closed}، ارجاع/تاریخ تأیید کارفرما، روزهای اطلاع‌رسانی برای نقاط W (پیش‌فرض 10 داخل ایران / 60 خارج، قابل ویرایش برای هر ITP)، کپی‌های `ItpActivity` (+ پارامترها) با وضعیت {Planned, Notified, Done, Waived, NotApplicable}، تاریخ برنامه‌ریزی‌شده، تاریخ ارسال اطلاعیه، سطرهای `ItpPointRelease` (طرف V/TPI/C، اقدام {Witnessed, Reviewed, Approved, Waived}، متن شخص/سازمان، تاریخ، ارجاع). قواعد: نقطه‌ی H نمی‌تواند Done شود مگر آنکه گزارش مرتبط Approved باشد و برای هر طرفی که در آن فعالیت H دارد یک سطر release وجود داشته باشد؛ نقطه‌ی W: اگر تاریخ اطلاعیه + روزهای اطلاع‌رسانی بگذرد و شاهدی حاضر نشود، سیستم اجازه‌ی ادامه می‌دهد و «witness waived by notice» را ثبت می‌کند؛ ویرایش ITP پس از ClientApproved یک revision جدید ایجاد می‌کند (تاریخچه نگه داشته شود).

### B. رکوردهای بازرسی (PR-QC-003، WI-QC-003/009/011/012)
- `QcRecord` (شماره QIR-، نوع {InProcess, Final, Dimensional, FirstArticle, ContractorShop, Layout}، workOrderId، itpActivityId (nullable)، itemId/lotId/serial به‌صورت متن، عملیات/مرحله به‌صورت متن، شماره نقشه + revision، بازرس (کاربر)، تاریخ، نتیجه {Accept, Reject, Rework, Hold}، ncrId، پیوست‌ها، یادداشت) + `QcMeasurement` (characteristic, nominal, tolPlus, tolMinus, measured, unit, equipmentId → قبول/رد در سمت سرور محاسبه می‌شود).
- قاعده: هر equipmentId استفاده‌شده باید وضعیت Calibrated داشته و nextDue ≥ تاریخ رکورد باشد، وگرنه با پیام رد شود (ISO 7.1.5).
- نتیجه‌ی Reject → ایجاد خودکار NCR از طریق سرویس/موجودیت NCR موجود (Source InProcess/Final) و اتصال آن.
- بازرسی توسط خودِ اپراتور ★: فقط برای فعالیت‌هایی که در ITP نیستند مجاز است (بررسی inspectorRole: رکوردهای متصل به ITP فقط نقش QC).

### C. گزارش‌های آزمون (بند 5.4 سند مبنا، شکاف‌های G-02، G-03، G-09، G-10)
- یک سرآیند عمومی `TestReport` (شماره TR-، نوع enum {PT, MT, UT, RT, Hardness, PMI, Hydro, RunTest, Performance, Balancing, Painting, Dimensional, Other}، workOrderId، itpActivityId، itemId/serial، procedureRef + revision، codeEdition، کاربر آزمونگر (examiner)، examinerQualificationId (برای PT/MT/UT/RT الزامی: در تاریخ گزارش معتبر، روش منطبق، سطح ≥ II)، تاریخ، متن شرایط محیطی، وضعیت {Draft, Submitted, Approved, Rejected}، نتیجه‌ی محاسبه‌شده، approvedBy (QC، نه آزمونگر)، سطرهای شاهد (طرف، نام، تاریخ)) + سطرهای `TestReportValue` (parameter, unit, min, max, measured، pass محاسبه‌شده؛ هنگام اتصال از پارامترهای فعالیت ITP کپی می‌شود) + سطرهای `TestReportIndication` (location, type, size, orientation, accepted) + `TestReportEquipment` (equipmentId؛ همان قاعده‌ی کالیبراسیون بخش B).
- اعتبارسنجی مختص هر نوع (سمت سرور) بر اساس پیوست B سند مبنا — مثلاً PT دما 5–52 °C، زمان ماند (dwell)، lux ≥ 1000، سازگاری با تاریکی ≥ 5 دقیقه؛ حدود MT؛ RunTest تریپ اضافه‌سرعت 110 % ± 2 % (3 تریپ)؛ Hydro: فشار اندازه‌گیری‌شده ≥ فشار الزامی (پارامتر) و زمان نگهداری ≥ زمان الزامی؛ استاندارد Balancing برابر ISO 21940-11 ★ (جایگزین ISO 1940-1 منسوخ‌شده، با همان گریدها). به‌صورت داده‌محور پیاده‌سازی شود: حدود از سطرهای مقدار می‌آیند؛ فقط چند قاعده‌ی سخت (تجهیز کالیبره، آزمونگر واجد صلاحیت، دست‌کم یک سطر مقدار، همه‌ی مقادیر قبول یا یک NCR متصل باشد).
- Approve: هنگامی که همه‌ی گزارش‌های الزامی Approved باشند، فعالیت ITP متصل را Done می‌کند.

### D. کالیبراسیون (WI-QC-001، شکاف G-05، ISO 10012)
- ★ کدهای فرم: FO-QC-007…011 (متن اصلی WI-QC-001) را به‌کار ببرید؛ تعارض را ثبت کنید.
- `MeasuringEquipment` (کد INN-IST-nnn خودکار، نام، نوع/کلاس enum {DimensionalHandTool, Gauge, PressureGauge, TemperatureInstrument, ElectricalMeter, NdtEquipment, TestStandInstrument, Other}، maker, model, serial, location, range, resolution/accuracy, requiresCalibration, وضعیت {Calibrated, DueSoon, Overdue, OutOfService, NotRequired}، intervalMonths, lastCalibrated, nextDue, correctionTableDocumentId).
- ★ فواصل پیش‌فرض برای هر کلاس (جدول قابل پیکربندی `CalibrationIntervalDefault`): ابزار دستی 12، گیج‌ها 12، فشارسنج‌ها 6، دما 12، الکتریکی 12، تجهیزات NDT 12 (لوکس‌متر/UV 12، بررسی بار بلند کردن یوک 12)، ابزار دقیق میز آزمون 12.
- `CalibrationEvent` (تجهیز، نام آزمایشگاه + پرچم اعتبار (ISO/IEC 17025)، شماره گواهی + سند، بیانیه‌ی ردیابی‌پذیری الزامی، قبول/رد as-found، قبول/رد as-left، adjusted، تاریخ، next due محاسبه‌شده). فقط نقش QC.
- خارج از تلرانس: وقتی as-found = fail → یک `OutOfToleranceCase` ایجاد شود که به‌طور خودکار هر QcRecord/TestReport را که از آغاز آخرین کالیبراسیون درست از آن تجهیز استفاده کرده فهرست می‌کند، با یک ارزیابی الزامی برای هر رکورد متأثر (بدون اثر / بازرسی مجدد / NCR) پیش از آنکه پرونده بسته شود (ISO 7.1.5.2).
- endpoint برای فهرست سررسیدها (30 روز آینده / معوق).

### E. پرسنل واجد صلاحیت و جوشکاری (WI-QC-010، شکاف‌های G-01، G-03، G-17؛ ASME IX)
- `QualifiedPerson` (نام، کد پرسنلی، کارفرما (own/contractor/provider id)، مهر/شناسه، userId اختیاری).
- `Qualification` (شخص، kind {Welder, WeldingOperator, NdtPersonnel, Inspector}، روش/فرایند (SMAW, GTAW, …, PT/MT/UT/RT/VT)، سطح (NDT: I/II/III ★ طرح ASNT SNT-TC-1A با written practice؛ ISO 9712 نیز قابل انتخاب)، متن دامنه (P-no, F-no, ضخامت، وضعیت‌ها)، شماره‌ی گواهی + سند، صدور، انقضا، وضعیت محاسبه‌شده).
- `Wps` (شماره، revision، فرایند، P-no فلز پایه، F-no/A-no فیلر، بازه‌ی ضخامت، وضعیت‌ها، پیش‌گرم، interpass، PWHT، گاز، وضعیت، تأییدشده توسط QC + مهندسی (دو نفر متفاوت)) و `Pqr` (شماره، ویرایش آیین‌نامه، سند نتایج آزمون، تأییدشده)؛ یک WPS دست‌کم به یک PQR تأییدشده نیاز دارد.
- `WeldLog` (تاریخ، workOrder، قلم/سریال، شماره‌ی اتصال، WPS، صلاحیت جوشکار، بچ فیلر، پیش‌گرم/interpass اندازه‌گیری‌شده، نتیجه‌ی VT، شناسه‌های گزارش NDT). قواعد: صلاحیت جوشکار باید فرایند WPS را پوشش دهد و معتبر باشد؛ تداوم ASME IX QW-322 — اگر جوشکار در آن فرایند بیش از 6 ماه WeldLog نداشته باشد، صلاحیت «Lapsed» می‌شود و تا تمدید (آزمون جدید) قابل استفاده نیست. endpoint تداوم که جوشکارانی را که ظرف 30 روز منقضی می‌شوند فهرست می‌کند.

### F. گواهی‌های مواد و ردیابی‌پذیری (شکاف G-11، EN 10204)
- `MaterialCertificate` (lotId، نوع EN 10204 {T22, T31, T32}، شماره، صادرکننده، سند، متن خلاصه‌ی شیمیایی/مکانیکی، verifiedBy QC، verifiedAt، نتیجه).
- `RequiredCertificateType` (nullable) را به InventoryItem اضافه کنید؛ وقتی قلم آن را دارد، پذیرش بازرسی ورودیِ موجود (InventoryQualityService.RecordInspectionAsync) باید شکست بخورد مگر آنکه لات گواهی تأییدشده از همان نوع یا بالاتر داشته باشد (3.2 > 3.1 > 2.2). lot.CertificateNumber/Type همگام نگه داشته شود.
- endpoint پرس‌وجوی ردیابی‌پذیری: بر اساس سریال یا لات → لات، heat، گواهی، رسیدها، NCRها، گزارش‌های آزمون، weld logها (همراه با جوشکار)، تاریخچه‌ی مراحل (شجره‌نامه‌ی برچسب موجود).

### G. افزوده‌های NCR (استفاده‌ی مجدد از NonConformance موجود)
- فیلدهای افزوده: DetectedStage (از Source دوباره استفاده شود)، DueDate (★ پیش‌فرض: Minor 7 روز، Major 14 روز)، RootCauseMethod {FiveWhy, Fishbone, EightD, Other}، CapaId، CostImpact، TimeImpact، CustomerReturnRef، AffectedEquipmentId.
- ★ Use-as-is (Concession) با CustomerApprovalRequired=true بدون CustomerApprovalReference قابل اجرا نیست (تا حدی از قبل وجود دارد — بررسی کنید). ★ NCR از نوع Major کمیته‌ای با ≥ 3 عضو (QC، مهندسی، ساخت) لازم دارد.
- ★ کدهای رسمی فرم: FO-QC-013 = NCR محصول؛ FO-QA-017 = CAPA (FO-QA-023 بازنشسته شد).

### H. CAPA (شکاف G-06، ISO 10.2)
- `Capa` (شماره CAPA-، منبع {Ncr, AuditFinding, ManagementReview, CustomerComplaint, DataAnalysis, Risk, Other} + sourceId، مسئله، مهار (containment)، rootCauseMethod + متن تحلیل، کاربر مالک (owner)، dueDate، وضعیت {Open, ActionsDefined, Implemented, EffectivenessCheck, Closed, Cancelled}، effectivenessCriteria، effectivenessDueDate (★ پیش‌فرض اجرا + 90 روز)، effectivenessResult، closedBy (نقش QA/مدیر QC، نه مالک)) + `CapaAction` (توضیح، مالک، موعد، doneAt، سند شواهد).
- endpoint فهرست معوق‌ها.

### I. برگه‌ی آزادسازی و دیتابوک نهایی (شکاف‌های G-12، C-09؛ ISO 8.6)
- ★ مرجع آزادسازی: مدیر QC (نقش QC) برگه‌ی آزادسازی و گواهی انطباق را امضا می‌کند؛ امضای کارفرما/TPI برای هر نقطه‌ی ITP ثبت می‌شود.
- `ReleaseNote` (شماره RN-، workOrderId، متن اقلام/سریال‌ها، وضعیت {Draft, Released, Cancelled}، snapshot بررسی‌ها). آزادسازی رد می‌شود مگر آنکه: ITP دستور کار ClientApproved/InUse باشد و هر فعالیت H و W در وضعیت Done/Waived/NotApplicable باشد؛ هیچ NCR بازی برای دستور کار نباشد مگر آن‌هایی که با Concession تأیید شده‌اند؛ هر فعالیت ITP با requiredReportType گزارش Approved داشته باشد؛ فهرست final data book هیچ ورودی اجباریِ ناموجود نداشته باشد.
- اسناد CustomerDelivery موجود (InventoryDocumentType.CustomerDelivery) برای یک دستور کار: تأیید نهایی علاوه بر آن به یک برگه‌ی آزادسازی Released برای همان دستور کار نیاز دارد (بررسی را در مسیر InventoryDocumentService.CheckBeforeConfirm اضافه کنید؛ تأیید بارگیری QC موجود را حفظ کنید).
- فهرست `FinalDataBook` در صورت نیاز تولید می‌شود (جدول لازم نیست، یا جدول کش‌شده): بخش‌ها (ITP + تأییدها، گواهی‌های مواد، weld logها و WPS/PQR و صلاحیت جوشکاران، گزارش‌های NDT، ابعادی، گزارش‌های آزمون (هیدرو، کارکرد، بالانسینگ، عملکرد)، رنگ‌آمیزی، فهرست NCR/امتیاز ارفاقی، گواهی‌های کالیبراسیون تجهیزات استفاده‌شده، برگه‌ی آزادسازی)، هر ورودی با شناسه‌ی منبع، شماره، وضعیت، شناسه‌ی سند و پرچم missing.

### J. کنترل مدارک (ISO 7.5، ISO 15489؛ شکاف G-18)
- `ControlledDocument` (کد TYPE-DEPT-NNN، نوع {PR, WI, FO, BL, Drawing, Spec, Other}، واحد/دپارتمان، عنوان، currentRevision، وضعیت {Draft, InReview, Approved, Effective, Obsolete}، effectiveDate، retentionClass، واحد مسئول) + `DocumentRevisionRecord` (revision، changeSummary، شناسه‌ی سند فایل، preparedBy، reviewedBy، approvedBy — سه کاربر متفاوت ★؛ تأییدشده → مؤثر؛ revision قبلی به‌طور خودکار Obsolete شود) + `DocumentDistribution` (revision، گیرنده‌ی کاربر یا واحد، sentAt، acknowledgedAt).
- `RetentionRule` (نوع رکورد، سال یا دائمی) ★ بذرشده: NCR/CAPA 10 سال، گزارش‌های آزمون و بازرسی و final data book = دائمی (عمر محصول)، کالیبراسیون 10 سال، ممیزی و بازنگری مدیریت 10 سال، DCF دائمی، نقشه‌ها دائمی.
- endpoint فهرست جامع (revisionهای مؤثر) + بررسی کد تکراری.

### K. ممیزی داخلی و بازنگری مدیریت (ISO 9.2، 9.3؛ شکاف‌های G-15، G-16)
- `AuditProgram` (سال، دامنه) + `Audit` (شماره AUD-، واحد ممیزی‌شونده، معیارها (متن بندهای ISO 9001/API Q1)، plannedDate، کاربر leadAuditor، تیم، وضعیت {Planned, Done, Closed}، سند گزارش) — قاعده: ممیز نمی‌تواند واحد خودش را ممیزی کند (نقش/واحد کاربر را با واحد ممیزی‌شونده مقایسه کنید ★ بر اساس نام نقش). + `AuditFinding` (بند، نوع {MajorNC, MinorNC, Observation, Opportunity}، شواهد، capaId (برای NC الزامی)، موعد).
- `ManagementReview` (تاریخ، حاضران، سطرهای `ManagementReviewInput` برای هر دسته‌ی ورودی ISO 9.3.2 (فهرست بذر) با متن، سطرهای `ManagementReviewDecision` → CAPA اختیاری)، سند صورت‌جلسه (FO-QA-022).

### L. شکایت‌های مشتری (ISO 9.1.2، 10.2)
- `CustomerComplaint` (شماره CC-، contractId/workOrderId، تاریخ دریافت، کانال، شرح، شدت، responseDue (★ 10 روز)، وضعیت، ncrId، capaId، متن پاسخ، closedAt).

### M. endpoint داشبورد کیفیت
- `GET api/v1/quality/dashboard`: NCRهای باز بر اساس شدت/سن، CAPA معوق، کالیبراسیون معوق/سررسید 30 روزه، صلاحیت‌های lapsed/در حال انقضا، نقاط H در انتظار ITP برای هر دستور کار، برگه‌های آزادسازی در انتظار، شکایت‌های باز. همچنین تعداد CAPA معوق، کالیبراسیون معوق و نقاط H در انتظار را به داشبورد مدیرعامل موجود (`CeoDashboardService` + DTO، به‌علاوه‌ی هشدارها) اضافه کنید.

## انتشار ۲ (نه اکنون)
ثبت ریسک/PFMEA، شاخص‌های تحلیل داده، 5S، پیشنهادها، ماتریس شایستگی، ثبت ذی‌نفعان.

## تست‌ها — `.github/scripts/inventory-smoke.sh` را گسترش دهید (بخش کیفیت را پیش از «roles of the new users» اضافه کنید)
از helperهای موجود (`call`, `as`, `check`, `val`) و کاربران/توکن‌های موجود (QC_TOKEN, ENG_TOKEN, PROD_TOKEN, ADMIN_TOKEN …) استفاده کنید؛ یک کاربر QA (نقش 14) بسازید. دست‌کم این‌ها را پوشش دهید: قالب‌های بذر؛ ITP از قالب برای یک دستور کار (قرارداد/دستور کار را همان‌طور که تست‌های موجود می‌سازند بسازید، یا اگر اسکریپت از قبل یکی دارد از آن استفاده کنید)؛ تجهیز + کالیبراسیون؛ رد گزارش با تجهیز معوق؛ تأیید گزارش PT توسط یک کاربر QC دیگر (کاربر QC دوم بسازید)؛ نقطه‌ی H بدون گزارش تأییدشده و بدون releaseها نمی‌تواند Done شود؛ صلاحیت جوشکار + تأیید WPS/PQR توسط دو نفر + weld log؛ تداوم lapsed (تاریخ weld log بیش از 6 ماه پس از قبلی)؛ پرونده‌ی خارج از تلرانس گزارش متأثر را فهرست می‌کند؛ NCR → CAPA → اثربخشی → بستن توسط غیرمالک؛ برگه‌ی آزادسازی تا وقتی H در انتظار است رد و پس از آن پذیرفته می‌شود؛ تحویل به مشتری بدون برگه‌ی آزادسازی مسدود است؛ تأیید revision مدرک → قبلی obsolete؛ یافته‌ی NC ممیزی به CAPA نیاز دارد؛ داشبورد کیفیت؛ شمارنده‌های جدید داشبورد مدیرعامل. هر check باید چیزی معنادار را ادعا (assert) کند.

## حلقه‌ی CI (در این وظیفه مسئول آن هستید)
- شاخه‌ی `innco-net10-build`، ریموت origin. CI روی Windows بیلد می‌کند، تست‌ها را اجرا می‌کند، بررسی مدل EF، سپس smoke tests روی SQL 2022 و 2025، سپس تست‌های بسته‌بندی/نصب. در هر زمان فقط یک اجرا (`cancel-in-progress`) — تا وقتی اجرای قبلی‌تان هنوز لازم است هرگز push نکنید؛ در طول کار شما کس دیگری push نخواهد کرد.
- روند migration: یک نام (مثلاً `QualityModule`) را در `.github/migration-name.txt` بگذارید، commit و push کنید؛ منتظر خلاصه‌ی نتیجه روی شاخه‌ی `innco-ci-results` بمانید (`ci-output/summary.txt` حاوی sha است؛ `ci-output/2-build.log` خطاها را دارد)؛ وقتی build=success باشد حاوی `ci-output/migration/*` است — آن فایل‌ها را در `src/DataLayer/Migrations/` کپی کنید، `.github/migration-name.txt` را به `Net10ModelCheck` برگردانید، commit و push کنید؛ سپس منتظر نتایج smoke روی شاخه‌های `innco-ci-smoke-2022` و `innco-ci-smoke-2025-express` بمانید (`smoke-output/7-inventory.txt`، شکست‌ها در `7-inventory-fail.txt`؛ موضوع commit شامل sha است). اگر پس از commit شدن migration موجودیت‌ها را تغییر دادید، به همین روش یک migration کوچک جدید بسازید (migrationهای commit‌شده را هرگز ویرایش نکنید).
- انتظار: از یک فرمان Bash با `run_in_background: true` و یک حلقه‌ی until که هر 30–60 ثانیه `git fetch` را poll می‌کند استفاده کنید؛ از sleep طولانی در foreground استفاده نکنید. برای دیدن وضعیت اجرا از `gh run list -R jr110jr110-boop/desktop-tutorial --branch innco-net10-build -L 1` استفاده کنید.
- پیام‌های commit با این خطوط پایان می‌یابند:
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_0162jHsRTsijMa6de7z2YbQk
- هرگز اسرار موجود در appsettings*.json را چاپ یا تغییر ندهید؛ هرگز force-push نکنید؛ هرگز به شاخه‌هایی غیر از innco-net10-build دست نزنید (CI شاخه‌های نتیجه را خودش می‌نویسد).
- انجام‌شده = build، تست‌ها، بررسی EF سبز و smoke با 0 شکست روی هر دو نسخه‌ی SQL.

## ضمیمه — اصول طراحی شرکت (الزام‌آور)
- **انسان فقط در گیت‌های کنترلی و کار فیزیکی.** سیستم هر چه را بتواند پیش‌نویس می‌کند؛ انسان‌ها بررسی، اصلاح و تأیید می‌کنند. به‌طور مشخص:
  - وقتی یک دستور کار بدون ITP وجود دارد، `EnsureItpAsync(workOrderId)` یکی را از قالب منطبق با نوع محصول پیش‌نویس می‌کند (endpoint + هنگام ایجاد اولین QcRecord/TestReport برای آن دستور کار فراخوانی می‌شود).
  - نتیجه‌ی Reject در هر بازرسی/آزمون، NCR را به‌طور خودکار پیش‌نویس می‌کند (در خلاصه‌ی فنی آمده است)؛ یک NCR از نوع Major، یک CAPA متصل را به‌طور خودکار پیش‌نویس می‌کند (وضعیت Open، مالک خالی) تا QA تخصیص دهد.
  - کالیبراسیونِ سررسید در 30 روز / معوق، تداوم جوشکاری که ظرف 30 روز lapsed می‌شود، CAPA معوق، مهلت‌های اطلاعیه‌ی نقاط W و مهلت‌های پاسخ به شکایت توسط سیستم محاسبه می‌شوند و در داشبورد کیفیت و در هشدارهای داشبورد مدیرعامل ظاهر می‌شوند — بدون فهرست دستی.
  - فهرست final data book و بررسی‌های برگه‌ی آزادسازی محاسبه می‌شوند، هرگز تایپ نمی‌شوند.
- **اول آفلاین.** همه‌چیز روی سرور خود شرکت و بدون اینترنت اجرا می‌شود: بدون فراخوانی HTTP خارجی، بدون CDN، بدون سرویس ابری در این ماژول.
- **گزارش‌های فقط‌خواندنی.** داشبوردها و گزارش‌ها از روی رکوردها محاسبه می‌شوند؛ جدول گزارش قابل‌ویرایش وجود ندارد.
