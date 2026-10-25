ترجمه فارسی سند اصلی (1405/08/03)؛ شناسه‌ها و کد به همان صورت انگلیسی مانده‌اند.

# انتشار O1 اتوماسیون اداری: گزارش بک‌اند

شاخه `innco-net10-build`، آخرین کامیت 97f4c54. CI: smoke تعداد 1725 passed، 0 failed روی SQL 2022 و 2025 Express؛ install checks تعداد 0 FAIL (first, update, restore). مایگریشن‌ها: `OfficeAutomation`، `OfficeLetterNumberIndex`. build بسته‌بندی‌شده فرانت در push نخست با کامیت فرانت f3e664c جایگزین شد.

## 1. آنچه ساخته شد

- **نقش‌ها:** SECRETARIAT (26، «دبیرخانه») و HR (27، «اداری و منابع انسانی»)، claimها 2600+ و 2700+. هر کاربر واردشده می‌تواند نامه و درخواست پیش‌نویس کند و صندوق ورودی خود را بخواند.
- **سازمان:** واحدها (واحد ریشه «شرکت» تنها seed است)، سمت‌ها (دارنده، پیوند پرسنلی، پرچم مدیر، اختیار امضا)، نیابت به یک نایب با ثبت «از طرف». نامه‌ها و ارجاع‌ها به سمت‌ها می‌روند، بنابراین نایب صندوق ورودی را به ارث می‌برد.
- **صندوق ورودی و اعلان‌ها:** `INotificationService` (idempotent بر اساس کلید مبدأ، با عبور از گیت بسته می‌شود، خطای ایمیل نادیده گرفته می‌شود، هرگز گیتی را مسدود نمی‌کند). متصل به: درخواست خرید (برنامه‌ریزی، مدیریت)، درخواست پرداخت، نهایی‌سازی سند حسابداری (یک آیتم تجمیعی)، اجرای حقوق و دستمزد، CAPA، release note، نقطه توقف ITP، baseline، درخواست تغییر، پاسخ transmittal. سندهای حسابداری خودکار فقط با آیتم تجمیعی «vouchers waiting to be finalised» پوشش داده می‌شوند.
- **نامه‌ها:** وارده، صادره، داخلی. پیش‌نویس، زنجیره تأیید سمت‌ها، امضا (تصویر امضای ذخیره‌شده، مهر زمانی، هش محتوا؛ پس از امضا ویرایش ممکن نیست)، شماره‌گذاری بر اساس قاعده، ثبت، ارسال با روش تحویل و رسید، درخت ارجاع، محرمانگی (عادی، محرمانه، سری)، قالب‌ها، طرف‌های خارجی، جستجو، درون‌ریزی آرشیو قدیمی.
- **جلسه‌ها:** دستور جلسه، حاضران، صورتجلسه، مصوبه‌هایی که پس از تأیید صورتجلسه توسط رئیس جلسه در صندوق ورودی سمت مسئول می‌نشینند، هشدار معوقه به رئیس جلسه (یک بار برای هر مصوبه)، شمارنده `overdueDecisions` و هشدار در داشبورد مدیرعامل (CEO)، پیوند از ManagementReview کیفیت (`OfMeetingId`).
- **حضور و غیاب:** شیفت‌ها، تخصیص‌ها، دستگاه‌ها با کلید API هش‌شده، درون‌ریزی ساعت و push دستگاه، رویدادهای دستی (HR، با دلیل)، سیاست، محاسبه روزانه قطعی (تأخیر، تعجیل در خروج، اضافه‌کار، شب‌کاری، کار در تعطیلات، مرخصی و ماموریت)، job hosted در حدود ساعت 01:00 (`Office:Jobs=false` آن را خاموش می‌کند)، بستن ماهانه به `FnAttendanceSheet`، کوئری ساعات کارکرد برای بررسی ثبت زمان تولید.
- **مرخصی، ماموریت، اضافه‌کار، ساعت فراموش‌شده:** پیش‌نویس، تأیید مدیر واحد، ثبت توسط HR، مانده مرخصی مجازی.
- **درخواست‌های عمومی:** موتور به‌علاوه سه نوع seed (خودرو، پشتیبانی فناوری اطلاعات، آموزش).

### قواعد شایسته دانستن
- تاریخ شمسی در هر چیزی که انسان می‌خواند (فیلدهای `...Text`، شماره‌های نامه)؛ فیلدهای تاریخ API به‌صورت ISO می‌مانند.
- نقض قواعد با HTTP 200 و `success=false` پاسخ داده می‌شود؛ فقط push دستگاه با HTTP 401 پاسخ می‌دهد.
- نامه‌ای که فراخواننده مجاز به دیدن آن نیست به‌صورت «پیدا نشد» پاسخ داده می‌شود (وجود آن فاش نمی‌شود).
- Maker-checker: نویسنده نمی‌تواند نامه یا درخواست خود را تأیید کند؛ کارمند نمی‌تواند مرخصی خود را تأیید یا ثبت کند.
- شماره سندها فقط وقتی تخصیص می‌یابند که سند با موفقیت ذخیره شود؛ فراخوانی ردشده (از جمله ذخیره-و-ثبت در یک فراخوانی) شماره‌ای نمی‌گیرد.
- الگوی پیش‌فرض شماره نامه `{yy}/{kind}/{unit}/{seq:5}` که در آن `{kind}` برابر و (وارده)، ص (صادره)، د (داخلی) است؛ یک شمارنده برای هر قاعده و سال شمسی؛ یکتا برای هر نوع. قاعده یک واحد بر قاعده عمومی غلبه می‌کند. نامه‌های قدیمی درون‌ریزی‌شده شماره متنی خود را نگه می‌دارند و از شماره ترتیبی استفاده نمی‌کنند.
- قواعد حضور و غیاب (همه در `AttendanceMath`): اولین و آخرین رویداد ساعت روز بازه حضور است؛ کارکرد = بازه منهای استراحت؛ تأخیر و تعجیل در خروج فراتر از تلورانس به‌طور کامل شمرده می‌شود؛ اضافه‌کار = زمان پس از پایان شیفت، با سقف حداکثر سیاست (یا تأیید پیشین)، گرد به پایین؛ شب‌کاری = هم‌پوشانی با پنجره شب؛ همه دقیقه‌های کارکرد در روز غیرکاری کار در تعطیلات است؛ یک رویداد منفرد = روز ناقص (بستن ماه رد می‌کند مگر `acceptIncompleteAsAbsent`).
- مانده مرخصی = انتقالی + استحقاق − استفاده‌شده + نرخ ماهانه × ماه‌های سپری‌شده از آخرین ماه ثبت‌شده − مرخصی تأییدشده یا ثبت‌شده ماه‌های بعد. نرخ‌های `LeaveEntitlementMonthly` (fallback ‏2.5) و `LeaveCarryForwardMax` (fallback ‏0) از جدول نرخ‌های حقوق و دستمزد می‌آیند.
- ماه‌هایی که اجرای حقوق و دستمزد آنها Approved یا بالاتر است برای مرخصی، رویدادهای ساعت دستی و بستن ماه قفل هستند.

## 2. Endpointها

همه مسیرها زیر `api/v1/office` هستند. فهرست‌ها صفحه‌بندی‌شده‌اند (`data.data[]`، `currentPage`، `totalItems`) و `PageNumber`، `PageSize`، `Contains` را می‌گیرند (کلاس `OfficePageQuery`). enumها عدد هستند. `AllowedActions` روی یک رکورد به فرانت می‌گوید کدام دکمه‌ها را نشان دهد.

### سازمان، من
| Route | نقش‌ها | DTOs |
|---|---|---|
| GET `units` | همه | `OfficePageQuery` مقدار `UnitDto` (id, code, title, parentId, managerPositionId, positionCount, isActive) برمی‌گرداند |
| POST `units` | HR, PMO, CEO | `SaveUnitDto` (id, code, title, parentId, isActive) مقدار `UnitDto` برمی‌گرداند |
| GET `positions` | همه | `PositionQuery` (unitId, userId, signersOnly) مقدار `PositionDto` (id, title, unitId, userId, userName, employeeId, isManager, canSign, isActive, holderHasSignature) برمی‌گرداند |
| POST `positions` | HR, PMO, CEO | `SavePositionDto` (id, title, unitId, userId, employeeId, isManager, canSign, isActive) |
| GET `delegations` | همه | `DelegationQuery` مقدار `DelegationDto` (positionId, deputyUserId, fromDate, toDate, fromDateText, toDateText, current, reason) برمی‌گرداند |
| POST `delegations` | دارنده سمت، یا HR, PMO, CEO | `SaveDelegationDto` (positionId, deputyUserId, fromDate, toDate, reason) |
| POST `delegations/{id}/cancel` | دارنده یا ایجادکننده، یا HR, PMO, CEO | ندارد |
| GET `org/tree` | همه | `OrgNodeDto` (واحد همراه با سمت‌ها و فرزندان) |
| GET `me` | همه | `MyOfficeDto` (userId, positions[`MyPositionDto` positionId, title, onBehalf, canSign, isManager], hasSignature, employeeId) |
| POST `me/signature` | همه | `SaveSignatureDto` (documentId یک فایل بارگذاری‌شده) مقدار `MyOfficeDto` برمی‌گرداند |

### صندوق ورودی
| Route | نقش‌ها | DTOs |
|---|---|---|
| GET `inbox` | همه (آیتم‌های خود: سمت خود، سمت نیابتی، کاربر خود، نقش خود) | `InboxQuery` (status, includeClosed, kind, sourceModule, sourceKey) مقدار `InboxItemDto` (id, kind, title, sourceModule, sourceType, sourceId, sourceKey, link, dueDate, dueDateText, overdue, priority, status, createdAtText, recipientPositionTitle, recipientRole, closedReason) برمی‌گرداند |
| GET `inbox/counts` | همه | `InboxCountsDto` (unread, open, overdue, byKind) |
| POST `inbox/act` | همه | `InboxActionDto` (id, action: read, done) |

### نامه‌ها
| Route | نقش‌ها | DTOs |
|---|---|---|
| GET `letters` | همه (فقط نامه‌هایی که فراخواننده مجاز به دیدن آنهاست) | `LetterQuery` (kind, status, classification, number, subject, party, dateFrom, dateTo, relatedKind, relatedRecordId, contains, mineOnly, waitingForMe) مقدار `LetterListDto` (id, kind, number, date, dateText, subject, status, classification, urgency, imported, metadataOnly, senderName, recipientNames) برمی‌گرداند |
| GET `letters/{id}` | طرف‌ها، درخت ارجاع، CEO؛ SECRETARIAT برای نامه‌های شماره‌دار غیرسری | `LetterDto` = فیلدهای فهرست به‌علاوه bodyHtml، سمت‌های فرستنده و امضاکننده، incomingNumber، approvals[`LetterApprovalDto`]، recipients[`RecipientDto`]، attachments، links[`RecordLinkDto`]، referrals[`ReferralDto` همراه با children و entries]، فیلدهای امضا، contentHash، فیلدهای تحویل، allowedActions، rowVersion |
| POST `letters` | همه (وارده: فقط SECRETARIAT) | `SaveLetterDto` (id, kind, date, subject, bodyHtml, senderPositionId, senderPartyId, classification, urgency, incomingNumber, incomingDate, signerPositionId, paperCopy, replyToLetterId, templateId, recipients, attachments, links, register, rowVersion) |
| POST `letters/action` | بر حسب مرحله | `LetterActionDto` (id, action: submit, approve, return, sign, register, send, receipt, archive, cancel; approvalChain, actingPositionId, comment, deliveryMethod, date, receipt, receiptDocumentId, rowVersion). submit, approve, return, sign: سمت‌های درگیر؛ register, send, receipt, archive: SECRETARIAT؛ cancel: نویسنده (بدون شماره) یا SECRETARIAT (شماره‌دار، ذکر دلیل الزامی) |
| GET `letters/{id}/verify` | دسترسی کامل به نامه | `LetterVerifyDto` (signed, valid, storedHash, computedHash) |
| POST `letters/refer` | یکی از دارندگان نامه | `ReferLetterDto` (letterId, parentReferralId, fromPositionId, toPositionIds, instruction, action 1 اقدام / 2 اطلاع / 3 نظر، dueDate) |
| POST `letters/referral-action` | سمت دریافت‌کننده (cancel: فرستنده یا SECRETARIAT) | `ReferralActionDto` (id, action: comment, close, cancel; text, actingPositionId) |
| GET, POST `parties` | همه | `ExternalPartyDto` / `SaveExternalPartyDto` (name, kind, providerId, customerId, address, phone, email, isActive) |
| GET `numbering-rules`, POST | خواندن: همه؛ نوشتن: SECRETARIAT, PMO, CEO | `NumberingRuleDto` (kind, unitId, unitCode, pattern, isActive, preview) / `SaveNumberingRuleDto`. توکن‌ها: {yyyy} {yy} {seq:n} {unit} {kind}؛ {seq} الزامی است |
| GET, POST `letter-templates` | خواندن: همه؛ نوشتن: SECRETARIAT, PMO, CEO | `LetterTemplateDto` (title, letterKind, headerHtml, footerHtml, bodyHtml, approvalChain, signerPositionId, isActive) |
| POST `letters/import` | SECRETARIAT | `ImportLettersDto` (rows[`ImportLetterRowDto` number, date شمسی یا ISO، kind, subject, party, fileNames, documentIds] یا csv) مقدار `ImportLettersResultDto` (imported, skipped, errors, letterIds) برمی‌گرداند |

### جلسه‌ها و مصوبه‌ها
| Route | نقش‌ها | DTOs |
|---|---|---|
| GET `meetings`, `meetings/{id}` | ایجادکننده، رئیس جلسه، دبیر، حاضران، مسئولان مصوبه، CEO | `MeetingQuery` مقدار `MeetingListDto` برمی‌گرداند؛ `MeetingDto` موارد agenda، attendees، decisions، links، allowedActions را اضافه می‌کند |
| POST `meetings` | همه | `SaveMeetingDto` (id, title, kind, startAt, endAt, place, chairPositionId, secretaryPositionId, agenda, attendees, links, decisions[`DecisionDto` text, ownerPositionId, dueDate, agendaSeq]) |
| POST `meetings/action` | ایجادکننده، رئیس جلسه، دبیر؛ approve-minutes: فقط رئیس جلسه؛ sign-minutes: حاضر در جلسه | `MeetingActionDto` (id, action: hold, submit-minutes, approve-minutes, sign-minutes, cancel; reason, presentAttendeeIds) |
| GET `decisions` | مصوبه‌های جلساتی که صورتجلسه آنها تأیید شده و فراخواننده مجاز به دیدن آنهاست | `DecisionQuery` (status, overdueOnly, mineOnly, meetingId, ownerPositionId) مقدار `DecisionDto` (overdue, dueDateText, status, doneNote) برمی‌گرداند |
| POST `decisions/action` | done: سمت مسئول (یا نایب)؛ cancel: رئیس جلسه، ایجادکننده، CEO | `DecisionActionDto` (id, action: done, cancel; note, evidenceDocumentId) |
| POST `decisions/check-overdue` | SECRETARIAT, PMO, CEO, HR | `OverdueCheckResultDto` (overdueDecisions, alertsCreated) |

### حضور و غیاب (`api/v1/office/attendance`)
| Route | نقش‌ها | DTOs |
|---|---|---|
| GET, POST `shifts` | خواندن: همه؛ نوشتن: HR, PMO, CEO | `ShiftDto` / `SaveShiftDto` (title, start "HH:mm", end, breakMinutes, nightStart, nightEnd, workDays 0 = یکشنبه .. 6 = شنبه، isActive؛ scheduledMinutes) |
| GET, POST `shift-assignments` | خواندن: همه؛ نوشتن: HR, PMO, CEO | `ShiftAssignmentQuery`, `SaveShiftAssignmentDto` (employeeId, shiftId, fromDate, toDate) |
| GET, POST `devices` | نوشتن: HR, PMO, CEO | `SaveDeviceDto` (code, title, location, isActive, rotateKey) مقدار `DeviceKeyDto` (device, apiKey فقط یک بار نمایش داده می‌شود، 48 hex) برمی‌گرداند |
| GET `clock` | HR, CEO همه؛ دیگران موارد خود | `ClockQuery` مقدار `ClockEventDto` برمی‌گرداند |
| POST `clock/manual` | HR (ذکر دلیل الزامی) | `ManualClockDto` (employeeId, timestamp, direction, reason) |
| POST `clock/import` | HR | `ImportClockDto` (deviceCode, rows[`ClockRowDto` code, date, time, device, direction] یا csv) مقدار `ClockImportResultDto` برمی‌گرداند |
| POST `push` | ناشناس؛ هدرهای `X-Device-Code`, `X-Device-Key`؛ HTTP 401 برای کد یا کلید نادرست | `DevicePushDto` (events[employeeCode, timestamp, direction]) مقدار `DevicePushResultDto` (accepted, duplicates, rejected, errors) برمی‌گرداند |
| GET, POST `policy` | نوشتن: HR, PMO, CEO | `AttendancePolicyDto` (graceMinutes, maxOvertimeMinutesPerDay, overtimeNeedsPreApproval, roundingMinutes) |
| POST `compute` | HR, PMO, CEO | `ComputeDaysDto` (fromDate, toDate, employeeId) مقدار `ComputeResultDto` برمی‌گرداند |
| GET `days` | HR, CEO همه؛ دیگران موارد خود | `DayQuery` مقدار `AttendanceDayDto` (status, workedMinutes, delayMinutes, earlyLeaveMinutes, overtimeMinutes, nightMinutes, holidayMinutes, absenceDays, paidLeaveDays, missionDays, nonWorkingDay, dateText) برمی‌گرداند |
| POST `close-month` | HR | `CloseMonthDto` (year, month, acceptIncompleteAsAbsent) مقدار `CloseMonthResultDto` (sheets, incompleteDays, rows[`SheetRowDto`]) برمی‌گرداند |
| GET `month/{year}/{month}` | همه | `MonthStatusDto` (closed, sheets, lockedByPayroll) |
| POST `worked-hours` | HR, CEO, PMO | `WorkedHoursQuery` مقدار `WorkedHoursDto` برمی‌گرداند (برای بررسی ثبت زمان تولید) |

### مرخصی، ماموریت، اضافه‌کار
| Route | نقش‌ها | DTOs |
|---|---|---|
| GET `leaves`, `leaves/{id}` | موارد خود؛ مدیر واحد و HR، CEO موارد مربوط به خود را می‌بینند | `LeaveQuery` مقدار `LeaveDto` (number, kind, fromDate, toDate, fromTime, toTime, hours, days, status, approverPositionId, allowedActions, rowVersion) برمی‌گرداند |
| POST `leaves` | کارمند (HR برای دیگران) | `SaveLeaveDto` (id, employeeId, kind 1 روزانه .. 7 ساعت فراموش‌شده، fromDate, toDate, fromTime, toTime, hours, direction, reason, attachmentDocumentId, submit, rowVersion) |
| POST `leaves/action` | submit, cancel: کارمند یا HR؛ approve: سمت مدیر واحد (یا نایب)؛ record: HR؛ reject: تأییدکننده | `LeaveActionDto` (id, action, comment, actingPositionId, rowVersion) |
| GET `leaves/balance` | موارد خود؛ HR، CEO هر کسی | کوئری employeeId, year, month مقدار `LeaveBalanceDto` (carriedForward, entitled, used, entitlementRate, projectedEntitlement, pendingDays, available) برمی‌گرداند |

### درخواست‌های عمومی
| Route | نقش‌ها | DTOs |
|---|---|---|
| GET `request-types` | همه (انواع غیرفعال: مدیران) | `RequestTypeDto` (title, fields[`RequestFieldDto` name, label, type text/number/date/select, required, options], approvalChain, targetNote) |
| POST `request-types` | HR, PMO, CEO | `SaveRequestTypeDto` |
| GET `requests`, `requests/{id}` | درخواست‌کننده، تأییدکنندگان، مدیران | `RequestQuery` مقدار `RequestDto` (number, values, approvals, status, allowedActions) برمی‌گرداند |
| POST `requests` | همه | `SaveRequestDto` (id, typeId, values, submit) |
| POST `requests/action` | submit, cancel: درخواست‌کننده؛ approve, reject: سمتی که نوبت اوست؛ complete: HR, PMO, CEO | `RequestActionDto` (id, action, comment) |

سایر موارد: `GET finance/ceo-dashboard` اکنون `overdueDecisions` را دارد. DTOهای ManagementReview کیفیت (`ReviewListDto`، `CreateReviewDto`) فیلد `ofMeetingId` را حمل می‌کنند.

## 3. علت 401 در push دستگاه

push های ردشده به‌علت کلید نادرست یا دستگاه غیرفعال نبودند. فهرست دستگاه‌ها در لاگ CI نشان می‌داد `GATE-1` فعال است و کلیدی که ارسال می‌شد همان کلید زمان ثبت بود. تنها تغییری که مشکل را حل کرد خواندن `X-Device-Code` و `X-Device-Key` مستقیماً از `Request.Headers` به‌جای پارامترهای `[FromHeader]` روی action بود. بنابراین علت، model binding پارامترهای هدر دارای خط تیره روی این action ناشناس بود، نه بررسی کلید. این نتیجه از روی رفع استنباط شده است؛ وضعیت داخلی binder را ثبت نکردم.

## 4. تصمیم‌ها برای تأیید با شرکت

1. **قالب شماره نامه.** پیش‌فرض `{yy}/{kind}/{unit}/{seq:5}` با و ص د است؛ مدیر اداری باید قالب واقعی را اعلام کند (قابل ویرایش برای هر نوع و هر واحد بدون تغییر کد).
2. **مدل و قالب دستگاه حضور و غیاب.** عمومی ساخته شده: ردیف‌های CSV یا Excel (کد پرسنلی، تاریخ، ساعت، دستگاه) و push از طریق HTTP. کد پرسنلی روی دستگاه باید برابر `FnEmployee.PersonnelCode` باشد. برای اتصال مستقیم، مدل واقعی و خروجی یا SDK آن لازم است.
3. **خروجی آرشیو راهکاران.** فقط یک مانیفست (شماره، تاریخ، نوع، موضوع، طرف، نام فایل‌ها) به‌علاوه فایل‌های بارگذاری‌شده پشتیبانی می‌شود. قالب واقعی خروجی نامعلوم است.
4. **مقادیر سیاست حضور و غیاب.** فقط تلورانس 0 seed شده است. لازم است: دقیقه‌های تلورانس، حداکثر اضافه‌کار روزانه، اینکه اضافه‌کار نیاز به تأیید پیشین دارد یا نه، گرد کردن، و شیفت‌های واقعی، روزهای تعطیل هفتگی و تقویم تعطیلات شرکت (تعطیل هفتگی پیش‌فرض جمعه است).
5. **قواعد مرخصی.** استحقاق ماهانه (fallback ‏2.5 روز)، سقف انتقال (fallback ‏0)، اینکه مرخصی استعلاجی از مانده سالانه کسر شود یا نه (فعلاً می‌شود)، و طول روز مرخصی ساعتی (پایان شیفت منهای شروع، بدون استراحت).
6. **نامه‌های محرمانه.** SECRETARIAT نامه‌های شماره‌دار عادی و محرمانه را کامل و نامه‌های سری را فقط به‌صورت فراداده می‌بیند. این برداشت از دستور کار تأیید شود.
7. **پوشش گیت‌ها.** فقط گیت‌های فهرست‌شده در بالا آیتم صندوق ورودی ایجاد می‌کنند. گیت‌های دیگر و سندهای حسابداری خودکار منفرد پوشش داده نمی‌شوند.
8. **بستن ماه.** روزهای ناقص (یک رویداد ساعت) بستن را مسدود می‌کنند مگر HR آنها را به‌عنوان غیبت بپذیرد.

## 5. یادداشت‌ها برای بعد
- جستجوی متن کامل نامه‌ها `LIKE` روی موضوع و متن است؛ full-text در SQL Server می‌تواند به‌عنوان گزینه استقرار اضافه شود.
- تحویل ایمیل و پیامک اعلان‌ها هنوز استفاده نمی‌شود؛ صندوق ورودی تنها کانال است.
- درخواست‌های مرخصی ذخیره‌شده به‌عنوان پیش‌نویس و سپس ردشده در submit فقط وقتی می‌توانند پیش‌نویسی بر جای بگذارند که بدون `submit` ذخیره شده باشند؛ فراخوانی ذخیره-و-ثبتِ ردشده چیزی ذخیره نمی‌کند.

## وضعیت R1 (به دستور متوقف شد؛ آخرین کامیت ae0ebcc، اجرای CI شماره 40118232662 هنوز در حال اجرا بود و تأیید نشده است)

مبنا: کد روی شاخه `innco-net10-build`، کامیت‌های 2e44b29 (رفع‌ها)، 3ad8cf3 (مایگریشن `OfficeReviewFixes1`)، ae0ebcc (رفع smoke). build به‌صورت محلی و در CI سبز است. اجرای CI قبلی (3ad8cf3) روی هر دو SQL 2022 و 2025 Express نتیجه 1859 passed / 7 failed داد؛ هر 7 شکست در بررسی‌های جدید R1 بودند و ae0ebcc آنها را رفع می‌کند، اما این تأیید نشده است. install checks روی 3ad8cf3 سبز بودند.

### تصمیم شرکت ثبت‌شد (DECIDED)
دبیرخانه نامه‌های محرمانه یا سری را نمی‌خواند. فقط داده‌های دفتر ثبت (شماره، تاریخ، نوع، طبقه‌بندی، طرف‌ها، فیلدهای تحویل) را می‌گیرد و می‌تواند ثبت، شماره‌گذاری، ارسال، ابطال و ثبت تحویل را انجام دهد. هیچ نشانگر «default» در کد باقی نمانده است.

### یافته‌ها (نام‌های smoke با "office r1 ..." شروع می‌شوند)
| یافته | وضعیت | smoke check |
|---|---|---|
| C1 فایل‌های نامه‌ها | رفع شد: `[Authorize]` روی دانلود و اطلاعات فایل (فرانت در هر فراخوانی از Bearer token استفاده می‌کند)، فایل‌های نامه فقط از طریق `GET office/letters/{id}/attachments/{documentId}`، دانلود عمومی برای فایل‌های نامه 404 برمی‌گرداند، تغییر/حذف پس از اینکه نامه پیش‌نویس نیست رد می‌شود، SHA-256 هر فایل هنگام امضا ذخیره و در verify دوباره محاسبه می‌شود. همچنین binding مربوط به `change-file` رفع شد (documentId در query، فایل در body) | C1: ناشناس 401، change-file رد می‌شود (با کنترل)، بایت‌ها بدون تغییر، 404 برای غیرطرف، دست‌کاری -> verify false + نام فایل را می‌گوید |
| H1/H3 دید و جستجوی محرمانه | رفع شد طبق تصمیم بالا | بررسی‌های H1/H3 (فقط فراداده، جستجوی موضوع/متن برای دبیرخانه چیزی پیدا نمی‌کند، برای نویسنده پیدا می‌کند) |
| H2 دبیرخانه روی نامه‌های سری | رفع شد | H2: ثبت و ارسال نامه سری، آیتم ارسال فقط شماره را دارد |
| H4 تعیین دارنده/نایب | رفع شد (فقط HR؛ CEO فقط برای سمت خود یا سمت خالی؛ هرگز برای خود؛ نایب تعیین‌شده توسط دیگران تا تأیید دارنده یا CEO در انتظار است؛ هشدارهای صندوق ورودی؛ ممیزی‌شده). به‌صورت smoke check پیاده نشد: "deputy never sees a Secret letter dated before the delegation start" (تاریخ نامه‌ها همیشه امروز است) | بررسی‌های H4 |
| H5 شماره‌گذاری | رفع شد: تصادم هنگام ذخیره قاعده رد می‌شود، شماره تکراری در همان تراکنش به جلو پرش می‌کند | بررسی‌های H5 |
| H6 اضافه‌کار بدون حد | رفع شد | H6 (450 دقیقه، بازگردانده به 120) |
| H7 شیفت‌های عبوری از نیمه‌شب | رفع شد (مرز بین شیفت‌ها) | H7 (بعدازظهر 605 دقیقه، شب 690 دقیقه / 240 اضافه‌کار) |
| H8 مرخصی استعلاجی | رفع شد (شمارنده جدا تا برگه حقوق و دستمزد) | بررسی‌های H8 |
| M1 ارجاع ابطال‌شده | رفع شد | بررسی‌های M1 |
| M2 مرخصی در پایان ماه | رفع شد | M2 (pending 2، used 2.25) |
| M3 ماه قفل‌شده در تأیید/رد/ثبت | رفع شد | M3 |
| M4 رویدادهای ساعت آینده/قفل‌شده | رفع شد (درون‌ریزی و push) | M4 |
| M5 گیت‌ها: سندهای حسابداری، حقوق و دستمزد، NCR، CAPA | رفع شد؛ تغییر اضافی در ae0ebcc: `finalize-up-to` وقتی چیزی باقی نمانده گیت سند حسابداری را نیز sweep می‌کند | بررسی‌های M5 |
| M6 آیتم‌های گیت که دستی بسته می‌شوند | رفع شد | M6 |
| M7 ممیزی | رفع شد: نیابت، سمت، قاعده شماره‌گذاری، سیاست، رویدادهای ساعت دستی/درون‌ریزی‌شده، برگه‌های حضور و غیاب | بررسی‌های M7 |
| M8 مقادیر تاریخ درخواست‌ها | رفع شد | بررسی درخواست خودرو |
| 5 بررسی ضعیف | جایگزین شدند: verify، تاریخ ردیف شمسی، شمارش صندوق ورودی، سیاست، نامرئی بودن محرمانه | مطابق بالا |

### اختلاف‌نظرها / انحراف‌ها
- CEO نیز می‌تواند یک سمت خالی را تعیین کند (برای قرار دادن سمت HR در ابتدا لازم است).
- قاعده نایب برای نامه‌های سری فقط برای نامه‌های صادرشده اعمال می‌شود.
- ممیزی ساعت رویدادهای Manual و Import را پوشش می‌دهد، نه تردد Device (بسیار زیاد).
- smoke مربوط به H4 در بازبینی (HR که به خودش نیابت می‌دهد) با یک نایب غیر از خود به‌علاوه یک نیابت‌به‌خود ردشده جایگزین شد.
- ردیف‌های ممیزی برای موجودیت‌های "Added" شناسه موجودیت ندارند (شناسه پیش از ذخیره معلوم نیست)؛ smoke آنها را با نوع و محتوا پیدا می‌کند. ردیف‌های ممیزی "Added" احتمالاً تکراری برای قواعد شماره‌گذاری دیده شد؛ علت بررسی نشد.

### نیمه‌تمام / باز
- CI روی ae0ebcc خوانده نشده است: هر دو خلاصه smoke و install باید بررسی شوند (شاخه‌های `innco-ci-smoke-2022`، `innco-ci-smoke-2025-express`، `innco-ci-install`). اگر بررسی‌ای هنوز شکست می‌خورد در `.github/scripts/inventory-smoke.sh` است (بلوک‌های office R1، از خط "R1: confidential letters" تا پایان بخش office).
- helper خصوصی استفاده‌نشده `Counts` در `src/Services/NasbNiro/Office/LeaveService.cs` را می‌توان حذف کرد.
- ردیف‌های ممیزی احتمالاً تکراری: `src/DataLayer/Context/ApplicationDbContext.cs` / `FinanceAuditTrail.cs`.

### نتیجه CI برای R1 روی ae0ebcc (بعداً خوانده شد؛ هر دو SQL 2022 و 2025 Express شکست خوردند، install-test و package موفق بودند)
1863 passed، 3 failed، همه در بررسی‌های جدید R1:
- "C1: the stored bytes of the attachment are unchanged ..." و "C1: after the bytes ... are swapped on disk verify fails": smoke هنوز فایل را روی دیسک پیدا نمی‌کند (`ls src/WebApi/UploadedDocuments/<id>*` چیزی برنگرداند؛ content root برنامه احتمالاً جای دیگری است). نشان داده نشده که رفتار سمت سرویس اشتباه باشد؛ جستجوی فایل در `.github/scripts/inventory-smoke.sh` (متغیر `OATTF`) به مسیر واقعی نیاز دارد (آن را با `find / -name "<documentId>*"` در smoke پیدا کنید).
- "M5: so the voucher gate is closed": `vouchers:finalise` پس از `finalize-up-to` هنوز باز است؛ sweep اضافه‌شده در `src/Services/NasbNiro/Finance/GlService.cs` آن را نبست (بررسی شود که آیا هنوز سند حسابداری Checked وجود دارد، مثلاً سندهای حقوق و دستمزد تیر/مرداد، تا گیت به‌درستی هنوز باز باشد و پیش‌شرط اشتباه باشد).
همه موارد دیگر در بلوک‌های R1 موفق شدند. چیز دیگری push نشد.
