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
8. فایل‌های نامه: فقط `GET letters/{id}/attachments/{documentId}` (در کنترلر راستی‌آزمایی شده؛ اگر فراخواننده مجاز به خواندن کامل نامه نباشد 404). تصمیم R1 اعمال می‌شود: دبیرخانه نامه‌های محرمانه و سری را فقط به‌صورت فراداده می‌بیند (`metadataOnly=true`)، بخش 4.6 گزارش منسوخ است.
9. کنترلرها فقط `[Authorize]` دارند (به‌علاوه‌ی نقش‌ها روی `decisions/check-overdue` و `push` ناشناس)؛ هر قاعده‌ی نقش دیگر در سرویس‌هاست، بنابراین فراخوانی ردشده HTTP 200 با `success=false` است (helperها آن را به‌عنوان خطا برمی‌گردانند).
10. سرور کلیدهای dictionary را نیز camel-case می‌کند (`values` یک درخواست، `byKind`)؛ بنابراین نام فیلدهای درخواست باید lowerCamel باشد (نوع‌های seed چنین هستند).
11. مقادیر `link` کارتابل که API می‌نویسد: `/office/letters/{id}`, `/office/leaves/{id}`, `/office/meetings/{id}`, `/office/requests/{id}`, `/office/delegations`, `/office/me`; همه به مسیرهای بالا ارجاع می‌شوند.
