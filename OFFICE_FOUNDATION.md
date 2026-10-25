ترجمه فارسی سند اصلی (1405/08/03)؛ شناسه‌ها و کد به همان صورت انگلیسی مانده‌اند.

# اتوماسیون اداری: پایه‌ی فرانت

لایه‌ی مشترک صفحه‌های اتوماسیون اداری. بک‌اند: `api/v1/office` (کنترلرها در `src/WebApi/Controllers/Office/OfficeControllers.cs`؛ هر جا با `office-o1-report.md` تفاوت داشته باشد، مرجع کنترلر است).

## فایل‌ها
- `src/api/OfficeAPIs.ts` توابع API (همه `[data, null]` یا `[null, error]` برمی‌گردانند؛ داده camelize شده است؛ فهرست‌ها صفحه‌بندی‌شده‌اند `{ data, totalItems }`، از `rowsOf()` / `usePagedList()` استفاده کنید).
- `src/composables/office.ts` نقشه‌های برچسب فارسی، `can(record, action)`، helperهای تاریخ.
- `src/composables/roles.ts` `Roles/RoleIds/roleTitles` + SECRETARIAT (26) و HR (27)، `OfficeRoles`.
- `src/router/index.ts` مسیرها، `src/components/common/KsSidebar/*` بخش منو، `src/views/office/*.vue` استاب‌ها.

## توابع API (OfficeAPIs.ts)
سازمان / من: `getUnits(params)`, `saveUnit(dto)`, `getPositions(params)`, `savePosition(dto)`, `getDelegations(params)`, `saveDelegation(dto)`, `cancelDelegation(id)`, `approveDelegation(id)`, `rejectDelegation(id)`, `getOrgTree()`, `getMyOffice()`, `saveMySignature(documentId)`.
کارتابل: `getInbox(params)`, `getInboxCounts()`, `inboxAction(id, "read"|"unread"|"done")`.
نامه‌ها: `getLetters(params)`, `getLetter(id)`, `saveLetter(dto)`, `letterAction({id, action, approvalChain?, actingPositionId?, comment?, deliveryMethod?, date?, receipt?, receiptDocumentId?, rowVersion?})`, `verifyLetter(id)`, `referLetter(dto)`, `referralAction(id, "comment"|"close"|"cancel", text?, actingPositionId?)`, `downloadLetterFile(letterId, documentId, fileName?)` (blob احراز هویت‌شده، فایل را ذخیره می‌کند، متن خطا یا null برمی‌گرداند)، `openLetterFile(letterId, documentId)` (blob object URL برای پیش‌نمایش؛ فراخواننده آن را revoke می‌کند)، `getParties(params)`, `saveParty(dto)`, `getNumberingRules(params)`, `saveNumberingRule(dto)`, `getLetterTemplates(params)`, `saveLetterTemplate(dto)`, `importLetters({rows?, csv?})`.
جلسات: `getMeetings(params)`, `getMeeting(id)`, `saveMeeting(dto)`, `meetingAction({id, action, reason?, actingPositionId?, presentAttendeeIds?, rowVersion?})`, `getDecisions(params)`, `decisionAction({id, action:"done"|"cancel", note?, evidenceDocumentId?, actingPositionId?})`, `checkOverdueDecisions()`.
حضور و غیاب (`office/attendance/`): `getShifts()`, `saveShift(dto)`, `getShiftAssignments(params)`, `saveShiftAssignment(dto)`, `getDevices()`, `saveDevice(dto)` (کلید API را یک‌بار برمی‌گرداند)، `getClockEvents(params)`, `addManualClock(dto)`, `importClock({deviceCode?, rows?, csv?})`, `getAttendancePolicy()`, `saveAttendancePolicy(dto)`, `computeAttendance({fromDate, toDate, employeeId?})`, `getAttendanceDays(params)`, `closeAttendanceMonth(year, month, acceptIncompleteAsAbsent)`, `getAttendanceMonth(year, month)`, `getWorkedHours(employeeIds, from, to)`. مسیر `push` دستگاه برای دستگاه‌های شبکه‌ی محلی (LAN) است (ناشناس، X-Device-Code / X-Device-Key) و عمداً در دسترس قرار داده نشده است.
مرخصی: `getLeaves(params)`, `getLeave(id)`, `saveLeave(dto)`, `leaveAction(id, action, comment?, actingPositionId?, rowVersion?)`, `getLeaveBalance({employeeId?, year?, month?})`.
درخواست‌ها: `getRequestTypes()`, `saveRequestType(dto)`, `getRequests(params)`, `getRequest(id)`, `saveRequest(dto)`, `requestAction(id, action, comment?, rowVersion?)`.
هر تابع یک doc comment دارد که فیلدهای DTO، رشته‌های اقدام مجاز و افراد مجاز به فراخوانی را بیان می‌کند. بارگذاری فایل: از `uploadFile(file, title)` در `InventoryAPIs.ts` استفاده‌ی مجدد کنید (شناسه‌ی سند را برمی‌گرداند)، سپس شناسه را در DTO بدهید.

## نقشه‌های enum (office.ts)، مقادیر عددی از `OfficeEnums.cs`
`inboxKinds` 1 Letter..6 MeetingAction (`inboxKindKeys` کلیدهای انگلیسی `counts.byKind` را نگاشت می‌کند)، `inboxStatuses` 0..3، `urgencies` 0..2، `letterKinds` 1..3، `letterStatuses` 0..7، `classifications` 0..2، `recipientTypes` 1..2، `deliveryMethods` 1..5، `partyKinds` 1..3، `relatedKinds` 1..7 و 99، `approvalStatuses` 0..2، `referralActions` 1..3، `referralStatuses` 0..2، `delegationStatuses` 0..2، `meetingKinds` 1..6، `meetingStatuses` 0..4، `decisionStatuses` 0..2، `clockDirections` 0..2، `clockSources` 1..3، `dayStatuses` 1..6، `weekDays` (0 = یکشنبه (Sunday) .. 6 = شنبه (Saturday)) + `weekOrder`، `leaveKinds` 1..7، `leaveStatuses` 0..5، `requestStatuses` 0..5، `requestFieldTypes`، `actionTitles` (عنوان فارسی برای هر رشته‌ی اقدام).
Helperها: `can(record, action)`, `dateText(iso)`, `dateTimeText(iso)`, `persianOf(text, iso)`, `minutesText(min)`, `timeOf(iso)`, `badgeOfLetter/Leave/Request/Meeting/Decision(status)`. تاریخ‌هایی که به کاربر نمایش داده می‌شوند همیشه جلالی هستند (`jalali()` از `inventory.ts`)؛ فیلد `...Text` از API را ترجیح دهید.

## OfficeRoles (roles.ts)
| Name | Roles | Used for |
|---|---|---|
| everyone | [] (بدون بررسی) | کارتابل، نامه‌ها (پیش‌نویس‌ها)، جلسات، درخواست‌ها، مرخصی و روزهای خود فرد |
| orgAdmins | HR, PMO, CEO | واحدها، پست‌ها، انواع درخواست، سیاست، شیفت‌ها، تفویض اختیار دیگران |
| letterAdmins | SECRETARIAT, PMO, CEO | قواعد شماره‌گذاری، قالب‌های نامه |
| secretariat | SECRETARIAT | ثبت/ارسال/رسید/بایگانی، نامه‌های وارده، واردسازی نامه‌های قدیمی |
| hr | HR | ثبت دستی ساعت، واردسازی ساعت، ثبت مرخصی، تخصیص دارندگان |
| attendanceAdmins | HR, PMO, CEO | شیفت‌ها، تخصیص‌ها، دستگاه‌ها، سیاست |
| attendanceReaders | HR, CEO | همه‌ی روزها و رویدادهای ساعت، محاسبه |
| monthReaders | HR, CEO, PAYROLL, FINANCEMANAGER | وضعیت ماه |
| monthClose | HR | بستن ماه |
| workedHours | HR, CEO, PMO, PROJECTMANAGER, PLANNING, CONSTRUCTION, FINANCEMANAGER | ساعات کارکرد |
| overdueCheck | SECRETARIAT, PMO, CEO, HR | decisions/check-overdue |
| ceo | CEO | |
حقوق مدیر واحد / رئیس جلسه / تأییدکننده به پست‌های کاربر بستگی دارد، نه به نقش‌ها: از `allowedActions` استفاده کنید.

## مسیرها (meta.roles داخل کروشه)
OfInbox /office/inbox, OfLetters /office/letters, OfLetter /office/letters/:id ("new"), OfLetterAdmin /office/letter-admin [letterAdmins], OfOrg /office/org (alias /office/delegations), OfMe /office/me, OfMeetings /office/meetings, OfMeeting /office/meetings/:id, OfDecisions /office/decisions, OfAttendance /office/attendance, OfAttendanceSetup /office/attendance-setup [attendanceAdmins], OfMonthClose /office/month-close [monthReaders], OfLeaves /office/leaves, OfRequests /office/requests, OfRequestTypes /office/request-types [orgAdmins].
مسیرهای اضافی (با همان فایل‌های view) برای پیوندهای کارتابل که API می‌نویسد: OfLeave /office/leaves/:id (Leaves.vue) و OfRequest /office/requests/:id (Requests.vue). عامل‌های صفحه باید `route.params.id` را در Leaves.vue / Requests.vue بخوانند. آیتم‌های کارتابل از ماژول‌های دیگر `link` خود را دارند.

## نوار کناری
بخش «اتوماسیون اداری» با همه‌ی صفحه‌ها، فیلترشده بر اساس نقش مانند سایر بخش‌ها (`disabled` وقتی نقش را ندارد). آیتم کارتابل یک شمارنده‌ی قرمز از `open` در `GET office/inbox/counts` نشان می‌دهد (هنگام mount بارگذاری می‌شود، هر 60 ثانیه تازه می‌شود). این کار به یک `badge?: number` کوچک در نوع `Links` و یک `<span>` در `KsSidebarLink.vue` نیاز داشت.

## ناهم‌خوانی‌ها: گزارش در برابر کنترلرها
1. مسیرهای غایب در گزارش: `POST delegations/{id}/approve` و `POST delegations/{id}/reject` (دارنده یا CEO؛ نه سازنده). به‌صورت `approveDelegation` / `rejectDelegation` افزوده شد.
2. `GET shifts` و `GET devices` آرایه‌ی ساده برمی‌گردانند، نه فهرست صفحه‌بندی‌شده. `GET devices` و `GET shift-assignments` برای غیرمدیران رد می‌شوند (HR, PMO, CEO؛ تخصیص‌ها از طریق AttendanceReaders برای HR/CEO نیز)؛ گزارش برای shift-assignments «خواندن برای همه» می‌گوید.
3. `POST attendance/compute`: سرویس فقط HR و CEO را مجاز می‌داند (AttendanceReaders)؛ گزارش می‌گوید HR, PMO, CEO.
4. `GET attendance/month/{y}/{m}`: HR, CEO, PAYROLL, FINANCEMANAGER (گزارش: همه).
5. `POST attendance/worked-hours`: HR, CEO, PMO, PROJECTMANAGER, PLANNING, CONSTRUCTION, FINANCEMANAGER (گزارش: HR, CEO, PMO).
6. `LetterQuery`/`MeetingQuery` و مانند آن `contains` اختصاصی ندارند؛ `Contains` از کلاس پایه‌ی `OfficePageQuery` می‌آید.
7. `DelegationDto` همچنین `status` دارد (0 تأییدشده، 1 در انتظار، 2 ردشده)، `isActive`، positionTitle، deputyName، createdByName، decidedByName، decidedAtText؛ `DelegationQuery` دارای `currentOnly` است. `LetterVerifyDto` دارای `changedDocumentIds` است. `LetterAttachmentDto` برابر `{ documentId, fileName }` است. `InboxItemDto.createdAt/readAt/closedAt` وجود دارند.
8. فایل‌های نامه: فقط `GET letters/{id}/attachments/{documentId}` (در کنترلر راستی‌آزمایی شده؛ اگر فراخواننده مجاز به خواندن کامل نامه نباشد 404). قاعده‌ی دبیرخانه (تصمیم R2، جایگزین «تصمیم R1» و بخش 4.6 گزارش که منسوخ هستند): دبیرخانه نامه‌های عادی و محرمانه را کامل می‌خواند (متن، پیوست‌ها، امضا و هش)، اما نامه‌ی سری برایش فقط داده‌ی دفتر ثبت است (`metadataOnly=true`: شماره، تاریخ، نوع، طبقه‌بندی، طرف‌ها، تحویل؛ بدون موضوع، متن و فایل). در فهرست نامه‌ی سری `linkCode` می‌آید ولی `linkTitle` برای دبیرخانه خالی است. صندلی هوش مصنوعی OFFICE-DRAFT فقط نامه‌ی عادی را می‌بیند.
9. کنترلرها فقط `[Authorize]` دارند (به‌علاوه‌ی نقش‌ها روی `decisions/check-overdue` و `push` ناشناس)؛ هر قاعده‌ی نقش دیگر در سرویس‌هاست، بنابراین فراخوانی ردشده HTTP 200 با `success=false` است (helperها آن را به‌عنوان خطا برمی‌گردانند).
10. سرور کلیدهای dictionary را نیز camel-case می‌کند (`values` یک درخواست، `byKind`)؛ بنابراین نام فیلدهای درخواست باید lowerCamel باشد (نوع‌های seed چنین هستند).
11. مقادیر `link` کارتابل که API می‌نویسد: `/office/letters/{id}`, `/office/leaves/{id}`, `/office/meetings/{id}`, `/office/requests/{id}`, `/office/delegations`, `/office/me`; همه به مسیرهای بالا ارجاع می‌شوند.

## قابلیت‌های R2 (پاسخ‌های شرکت)
- شماره‌گذاری نامه: وارده `INN-و-{code}{yyyy}-{seq}`، صادره `INN-{code}{yyyy}-{seq}`، داخلی `INN-د-{code}{yyyy}-{seq}`. `{code}` کد مناقصه یا پروژه‌ی پیوندخورده است و بدون پیوند خالی می‌ماند (بدون خط تیره‌ی اضافه). قاعده‌ی یک واحد قاعده‌ی پیش‌فرض را بازنویسی می‌کند. قواعد پیش‌فرض هنگام راه‌اندازی و در مسیرهای نوشتن ساخته می‌شوند، `GET numbering-rules` چیزی نمی‌نویسد.
- پیوند نامه به مناقصه یا پروژه (فقط یکی، فقط تا پیش از شماره‌گذاری): `tenderId` یا `contractId`. گزینه‌ها از `GET office/letters/link-options` (تابع `getLinkOptions()`) می‌آیند: `{ tenders: [{id, code, title, problem}], contracts: [...] }` با کدی که سرور حساب کرده است (کد مناقصه؛ کد پروژه و در نبودش کد قرارداد). کد فقط حروف انگلیسی، رقم و خط تیره (تا ۴۰ نویسه) باشد؛ گزینه‌ی دارای `problem` پذیرفته نمی‌شود. کدها با رقم لاتین نمایش داده می‌شوند.
- واردسازی PDF آرشیو راهکاران: `importLetterFiles` (حداکثر ۳۰۰ فایل، هر کدام ۵۰ مگابایت، مجموعا ۶۰۰ مگابایت؛ فرانت پیش از ارسال بررسی می‌کند). هر فایل جدا بررسی می‌شود و خطای یکی مانع بقیه نیست. ارسال دوباره‌ی همان نام فایل رد می‌شود (`already-attached`)، اما نام دیگر برای همان نامه پیوست دوم می‌سازد.
- واردسازی اکسل PW Kara: `previewClockFile` و `importClockFile` (فایل تا ۲۰ مگابایت، ۲٬۰۰۰٬۰۰۰ سلول و ۲۵۶ ستون؛ فایل خراب پیام فارسی می‌دهد). ستون‌بندی با نام ذخیره می‌شود (`saveClockImportProfile`). `dateOrder` (`YMD`، `DMY`، `MDY`) ترتیب تاریخ سال‌آخری را مشخص می‌کند؛ پیش‌فرض برای تاریخ شمسی/سال‌اولی `YMD` و در غیر این صورت `DMY`. تاریخ مبهم بدون انتخاب ترتیب کل بارگذاری را رد می‌کند و چیزی ذخیره نمی‌شود؛ پیش‌نمایش تاریخ شمسی هر نمونه را نشان می‌دهد (`sampleDates`). سال مبنای 1904 اکسل و ق.ظ / ب.ظ فارسی پشتیبانی می‌شوند. بارگذاری دوباره‌ی همان فایل چیزی را دوبار ثبت نمی‌کند.
- سیاست حضور (`AttendancePolicyDto`): مهلت ۱۵ دقیقه، تاخیر بیش از مهلت با ضریب ۳ (`delayPenaltyFactor`) کسر می‌شود، سقف اضافه‌کار ماهانه ۱۳۲۰ دقیقه = ۲۲ ساعت (`monthlyOvertimeCapMinutes`). مقدار خالی (`null`) برای این دو فیلد هنگام ذخیره مقدار ذخیره‌شده را نگه می‌دارد. نصب‌های قدیمی که مقدار اولیه (مهلت ۰ و اضافه‌کار با تایید قبلی) داشتند یک بار خودکار به قواعد شرکت ارتقا می‌یابند و در سابقه ثبت می‌شود.
- تاخیر در حقوق: کسر تاخیر فقط یک بار و به‌صورت کسری از روز داخل `absenceDays` فیش می‌آید. مبنای روز، دقایق برنامه‌ای شیفت است (پایان منهای شروع منهای استراحت؛ ۴۵۰ دقیقه برای شیفت اداری)، پس مرخصی ساعتی ۲ ساعته ۱۲۰/۴۵۰ = ۰٫۲۶۶۷ روز است. سقف ۲۲ ساعت فقط اضافه‌کار روز کاری را می‌گیرد (تعطیلی، جمعه و شب‌کاری سقف ندارد) و تعجیل کسر نمی‌شود.
