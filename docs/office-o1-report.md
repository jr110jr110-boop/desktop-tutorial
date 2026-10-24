# Office automation release O1: backend report

Branch `innco-net10-build`, last commit 97f4c54. CI: smoke 1725 passed, 0 failed on SQL 2022 and 2025 Express; install checks 0 FAIL (first, update, restore). Migrations: `OfficeAutomation`, `OfficeLetterNumberIndex`. The packaged front build was replaced with front commit f3e664c in the first push.

## 1. What was built

- **Roles:** SECRETARIAT (26, "دبیرخانه") and HR (27, "اداری و منابع انسانی"), claims 2600+ and 2700+. Every signed-in user can draft letters and requests and read their own inbox.
- **Organisation:** units (root unit "شرکت" is the only seed), positions (holder, personnel link, manager flag, signing authority), delegation to a deputy with "on behalf of" recorded. Letters and referrals go to positions, so a deputy inherits the inbox.
- **Inbox and notifications:** `INotificationService` (idempotent by source key, closed when the gate is passed, e-mail failure ignored, never blocks a gate). Wired into: purchase request (planning, management), payment request, voucher finalise (one aggregate item), payroll run, CAPA, release note, ITP hold point, baseline, change request, transmittal response. Automatic vouchers are covered only by the aggregate "vouchers waiting to be finalised" item.
- **Letters:** incoming, outgoing, internal. Draft, approval chain of positions, signature (stored signature image, timestamp, content hash; no edit after signing), numbering by rule, register, send with delivery method and receipt, referral tree, confidentiality (Normal, Confidential, Secret), templates, external parties, search, legacy archive import.
- **Meetings:** agenda, attendees, minutes, decisions that land in the owner position's inbox once the chair approves the minutes, overdue alert to the chair (once per decision), CEO dashboard counter `overdueDecisions` and alert, link from the quality ManagementReview (`OfMeetingId`).
- **Attendance:** shifts, assignments, devices with hashed API key, clock import and device push, manual events (HR, with reason), policy, deterministic daily computation (delay, early leave, overtime, night, holiday work, leave and mission), hosted job at about 01:00 (`Office:Jobs=false` switches it off), monthly close into `FnAttendanceSheet`, worked-hours query for the production time-booking check.
- **Leave, mission, overtime, forgotten clock:** draft, unit manager approves, HR records, virtual leave balance.
- **General requests:** engine plus three seed types (vehicle, IT support, training).

### Rules worth knowing
- Persian dates in everything a person reads (`...Text` fields, letter numbers); API date fields stay ISO.
- Rule violations answer HTTP 200 with `success=false`; only the device push answers HTTP 401.
- A letter the caller may not see is answered as "not found" (existence not revealed).
- Maker-checker: the writer cannot approve their own letter or request; the employee cannot approve or record their own leave.
- Document numbers are assigned only when the document is saved successfully; a refused call (including save-and-submit in one call) takes no number.
- Default letter number pattern `{yy}/{kind}/{unit}/{seq:5}` where `{kind}` is و (incoming), ص (outgoing), د (internal); one counter per rule and Persian year; unique per kind. A rule of a unit overrides the general rule. Imported legacy letters keep their text number and do not use the sequence.
- Attendance rules (all in `AttendanceMath`): first and last clock event of the day are the presence interval; worked = span minus break; delay and early leave beyond the grace are counted in full; overtime = time after shift end, capped by the policy maximum (or approved pre-approval), rounded down; night = overlap with the night window; all worked minutes on a non-working day are holiday work; a single event = incomplete day (month close refuses unless `acceptIncompleteAsAbsent`).
- Leave balance = carried forward + entitled − used + monthly rate × months since the last booked month − approved or recorded leave of later months. Rates `LeaveEntitlementMonthly` (fallback 2.5) and `LeaveCarryForwardMax` (fallback 0) come from the payroll rate table.
- Months whose payroll run is Approved or later are locked for leave, manual clock events and month close.

## 2. Endpoints

All routes are under `api/v1/office`. Lists are paged (`data.data[]`, `currentPage`, `totalItems`) and take `PageNumber`, `PageSize`, `Contains` (class `OfficePageQuery`). Enums are numbers. `AllowedActions` on a record tells the front which buttons to show.

### Organisation, me
| Route | Roles | DTOs |
|---|---|---|
| GET `units` | any | `OfficePageQuery` returns `UnitDto` (id, code, title, parentId, managerPositionId, positionCount, isActive) |
| POST `units` | HR, PMO, CEO | `SaveUnitDto` (id, code, title, parentId, isActive) returns `UnitDto` |
| GET `positions` | any | `PositionQuery` (unitId, userId, signersOnly) returns `PositionDto` (id, title, unitId, userId, userName, employeeId, isManager, canSign, isActive, holderHasSignature) |
| POST `positions` | HR, PMO, CEO | `SavePositionDto` (id, title, unitId, userId, employeeId, isManager, canSign, isActive) |
| GET `delegations` | any | `DelegationQuery` returns `DelegationDto` (positionId, deputyUserId, fromDate, toDate, fromDateText, toDateText, current, reason) |
| POST `delegations` | holder of the position, or HR, PMO, CEO | `SaveDelegationDto` (positionId, deputyUserId, fromDate, toDate, reason) |
| POST `delegations/{id}/cancel` | holder or creator, or HR, PMO, CEO | none |
| GET `org/tree` | any | `OrgNodeDto` (unit with positions and children) |
| GET `me` | any | `MyOfficeDto` (userId, positions[`MyPositionDto` positionId, title, onBehalf, canSign, isManager], hasSignature, employeeId) |
| POST `me/signature` | any | `SaveSignatureDto` (documentId of an uploaded file) returns `MyOfficeDto` |

### Inbox
| Route | Roles | DTOs |
|---|---|---|
| GET `inbox` | any (own items: own position, delegated position, own user, own role) | `InboxQuery` (status, includeClosed, kind, sourceModule, sourceKey) returns `InboxItemDto` (id, kind, title, sourceModule, sourceType, sourceId, sourceKey, link, dueDate, dueDateText, overdue, priority, status, createdAtText, recipientPositionTitle, recipientRole, closedReason) |
| GET `inbox/counts` | any | `InboxCountsDto` (unread, open, overdue, byKind) |
| POST `inbox/act` | any | `InboxActionDto` (id, action: read, done) |

### Letters
| Route | Roles | DTOs |
|---|---|---|
| GET `letters` | any (only letters the caller may see) | `LetterQuery` (kind, status, classification, number, subject, party, dateFrom, dateTo, relatedKind, relatedRecordId, contains, mineOnly, waitingForMe) returns `LetterListDto` (id, kind, number, date, dateText, subject, status, classification, urgency, imported, metadataOnly, senderName, recipientNames) |
| GET `letters/{id}` | parties, referral tree, CEO; SECRETARIAT for numbered non-secret letters | `LetterDto` = list fields plus bodyHtml, sender and signer positions, incomingNumber, approvals[`LetterApprovalDto`], recipients[`RecipientDto`], attachments, links[`RecordLinkDto`], referrals[`ReferralDto` with children and entries], signed fields, contentHash, delivery fields, allowedActions, rowVersion |
| POST `letters` | any (incoming: SECRETARIAT only) | `SaveLetterDto` (id, kind, date, subject, bodyHtml, senderPositionId, senderPartyId, classification, urgency, incomingNumber, incomingDate, signerPositionId, paperCopy, replyToLetterId, templateId, recipients, attachments, links, register, rowVersion) |
| POST `letters/action` | by step | `LetterActionDto` (id, action: submit, approve, return, sign, register, send, receipt, archive, cancel; approvalChain, actingPositionId, comment, deliveryMethod, date, receipt, receiptDocumentId, rowVersion). submit, approve, return, sign: the positions involved; register, send, receipt, archive: SECRETARIAT; cancel: writer (unnumbered) or SECRETARIAT (numbered, reason required) |
| GET `letters/{id}/verify` | full access to the letter | `LetterVerifyDto` (signed, valid, storedHash, computedHash) |
| POST `letters/refer` | a holder of the letter | `ReferLetterDto` (letterId, parentReferralId, fromPositionId, toPositionIds, instruction, action 1 action / 2 information / 3 comment, dueDate) |
| POST `letters/referral-action` | the receiving position (cancel: sender or SECRETARIAT) | `ReferralActionDto` (id, action: comment, close, cancel; text, actingPositionId) |
| GET, POST `parties` | any | `ExternalPartyDto` / `SaveExternalPartyDto` (name, kind, providerId, customerId, address, phone, email, isActive) |
| GET `numbering-rules`, POST | read any; write SECRETARIAT, PMO, CEO | `NumberingRuleDto` (kind, unitId, unitCode, pattern, isActive, preview) / `SaveNumberingRuleDto`. Tokens: {yyyy} {yy} {seq:n} {unit} {kind}; {seq} is required |
| GET, POST `letter-templates` | read any; write SECRETARIAT, PMO, CEO | `LetterTemplateDto` (title, letterKind, headerHtml, footerHtml, bodyHtml, approvalChain, signerPositionId, isActive) |
| POST `letters/import` | SECRETARIAT | `ImportLettersDto` (rows[`ImportLetterRowDto` number, date Persian or ISO, kind, subject, party, fileNames, documentIds] or csv) returns `ImportLettersResultDto` (imported, skipped, errors, letterIds) |

### Meetings and decisions
| Route | Roles | DTOs |
|---|---|---|
| GET `meetings`, `meetings/{id}` | creator, chair, secretary, attendees, decision owners, CEO | `MeetingQuery` returns `MeetingListDto`; `MeetingDto` adds agenda, attendees, decisions, links, allowedActions |
| POST `meetings` | any | `SaveMeetingDto` (id, title, kind, startAt, endAt, place, chairPositionId, secretaryPositionId, agenda, attendees, links, decisions[`DecisionDto` text, ownerPositionId, dueDate, agendaSeq]) |
| POST `meetings/action` | creator, chair, secretary; approve-minutes: chair only; sign-minutes: present attendee | `MeetingActionDto` (id, action: hold, submit-minutes, approve-minutes, sign-minutes, cancel; reason, presentAttendeeIds) |
| GET `decisions` | decisions of meetings with approved minutes that the caller may see | `DecisionQuery` (status, overdueOnly, mineOnly, meetingId, ownerPositionId) returns `DecisionDto` (overdue, dueDateText, status, doneNote) |
| POST `decisions/action` | done: owner position (or deputy); cancel: chair, creator, CEO | `DecisionActionDto` (id, action: done, cancel; note, evidenceDocumentId) |
| POST `decisions/check-overdue` | SECRETARIAT, PMO, CEO, HR | `OverdueCheckResultDto` (overdueDecisions, alertsCreated) |

### Attendance (`api/v1/office/attendance`)
| Route | Roles | DTOs |
|---|---|---|
| GET, POST `shifts` | read any; write HR, PMO, CEO | `ShiftDto` / `SaveShiftDto` (title, start "HH:mm", end, breakMinutes, nightStart, nightEnd, workDays 0 = Sunday .. 6 = Saturday, isActive; scheduledMinutes) |
| GET, POST `shift-assignments` | read any; write HR, PMO, CEO | `ShiftAssignmentQuery`, `SaveShiftAssignmentDto` (employeeId, shiftId, fromDate, toDate) |
| GET, POST `devices` | write HR, PMO, CEO | `SaveDeviceDto` (code, title, location, isActive, rotateKey) returns `DeviceKeyDto` (device, apiKey shown once, 48 hex) |
| GET `clock` | HR, CEO all; others own | `ClockQuery` returns `ClockEventDto` |
| POST `clock/manual` | HR (reason required) | `ManualClockDto` (employeeId, timestamp, direction, reason) |
| POST `clock/import` | HR | `ImportClockDto` (deviceCode, rows[`ClockRowDto` code, date, time, device, direction] or csv) returns `ClockImportResultDto` |
| POST `push` | anonymous; headers `X-Device-Code`, `X-Device-Key`; HTTP 401 on a bad code or key | `DevicePushDto` (events[employeeCode, timestamp, direction]) returns `DevicePushResultDto` (accepted, duplicates, rejected, errors) |
| GET, POST `policy` | write HR, PMO, CEO | `AttendancePolicyDto` (graceMinutes, maxOvertimeMinutesPerDay, overtimeNeedsPreApproval, roundingMinutes) |
| POST `compute` | HR, PMO, CEO | `ComputeDaysDto` (fromDate, toDate, employeeId) returns `ComputeResultDto` |
| GET `days` | HR, CEO all; others own | `DayQuery` returns `AttendanceDayDto` (status, workedMinutes, delayMinutes, earlyLeaveMinutes, overtimeMinutes, nightMinutes, holidayMinutes, absenceDays, paidLeaveDays, missionDays, nonWorkingDay, dateText) |
| POST `close-month` | HR | `CloseMonthDto` (year, month, acceptIncompleteAsAbsent) returns `CloseMonthResultDto` (sheets, incompleteDays, rows[`SheetRowDto`]) |
| GET `month/{year}/{month}` | any | `MonthStatusDto` (closed, sheets, lockedByPayroll) |
| POST `worked-hours` | HR, CEO, PMO | `WorkedHoursQuery` returns `WorkedHoursDto` (for the production time-booking check) |

### Leave, mission, overtime
| Route | Roles | DTOs |
|---|---|---|
| GET `leaves`, `leaves/{id}` | own; unit manager and HR, CEO see theirs | `LeaveQuery` returns `LeaveDto` (number, kind, fromDate, toDate, fromTime, toTime, hours, days, status, approverPositionId, allowedActions, rowVersion) |
| POST `leaves` | the employee (HR for others) | `SaveLeaveDto` (id, employeeId, kind 1 daily .. 7 forgotten clock, fromDate, toDate, fromTime, toTime, hours, direction, reason, attachmentDocumentId, submit, rowVersion) |
| POST `leaves/action` | submit, cancel: employee or HR; approve: manager position of the unit (or deputy); record: HR; reject: approver | `LeaveActionDto` (id, action, comment, actingPositionId, rowVersion) |
| GET `leaves/balance` | own; HR, CEO any | query employeeId, year, month returns `LeaveBalanceDto` (carriedForward, entitled, used, entitlementRate, projectedEntitlement, pendingDays, available) |

### General requests
| Route | Roles | DTOs |
|---|---|---|
| GET `request-types` | any (inactive types: admins) | `RequestTypeDto` (title, fields[`RequestFieldDto` name, label, type text/number/date/select, required, options], approvalChain, targetNote) |
| POST `request-types` | HR, PMO, CEO | `SaveRequestTypeDto` |
| GET `requests`, `requests/{id}` | requester, approvers, admins | `RequestQuery` returns `RequestDto` (number, values, approvals, status, allowedActions) |
| POST `requests` | any | `SaveRequestDto` (id, typeId, values, submit) |
| POST `requests/action` | submit, cancel: requester; approve, reject: the position whose turn it is; complete: HR, PMO, CEO | `RequestActionDto` (id, action, comment) |

Other: `GET finance/ceo-dashboard` now has `overdueDecisions`. Quality ManagementReview DTOs (`ReviewListDto`, `CreateReviewDto`) carry `ofMeetingId`.

## 3. Device push 401: the cause

The rejected pushes were not a wrong key or an inactive device. The device list in the CI log showed `GATE-1` active, and the key from registration was the one sent. The only change that fixed it was reading `X-Device-Code` and `X-Device-Key` straight from `Request.Headers` instead of `[FromHeader]` parameters on the action. So the cause was the model binding of the hyphenated header parameters on this anonymous action, not the key check. This is inferred from the fix; I did not capture the binder's internal state.

## 4. Decisions to confirm with the company

1. **Letter number format.** Default is `{yy}/{kind}/{unit}/{seq:5}` with و ص د; the administration manager should give the real format (editable per kind and per unit with no code change).
2. **Attendance device model and format.** Built generic: CSV or Excel rows (personnel code, date, time, device) and an HTTP push. The personnel code on the device must equal `FnEmployee.PersonnelCode`. The real model and its export or SDK are needed to connect it directly.
3. **Rahkaran archive export.** Only a manifest (number, date, kind, subject, party, file names) plus uploaded files is supported. The real export format is unknown.
4. **Attendance policy values.** Only grace 0 is seeded. Needed: grace minutes, maximum daily overtime, whether overtime needs pre-approval, rounding, and the real shifts, weekly days off and company holiday calendar (default weekly off is Friday).
5. **Leave rules.** Monthly entitlement (fallback 2.5 days), carry-forward limit (fallback 0), whether sick leave counts against the annual balance (currently it does), and the hourly-leave day length (shift end minus start, without the break).
6. **Confidential letters.** SECRETARIAT sees Normal and Confidential numbered letters in full and Secret ones as metadata only. Confirm this reading of the brief.
7. **Gate coverage.** Only the gates listed above create inbox items. Other gates, and individual automatic vouchers, are not covered.
8. **Month close.** Incomplete days (a single clock event) block the close unless HR accepts them as absence.

## 5. Notes for later
- Full-text search on letters is `LIKE` on subject and body; SQL Server full-text can be added as a deployment option.
- E-mail and SMS delivery of notifications is not used yet; the inbox is the only channel.
- Leave requests saved as drafts and then refused at submit can still leave a draft behind only when saved without `submit`; a save-and-submit call that is refused saves nothing.

## R1 status (stopped on instruction; last commit ae0ebcc, CI run 40118232662 was still running and is NOT verified)

Basis: code on branch `innco-net10-build`, commits 2e44b29 (fixes), 3ad8cf3 (migration `OfficeReviewFixes1`), ae0ebcc (smoke fixes). Build is green locally and in CI. The previous CI run (3ad8cf3) gave 1859 passed / 7 failed on both SQL 2022 and 2025 Express; the 7 failures were all in the new R1 checks and ae0ebcc fixes them, but that is unconfirmed. Install checks were green on 3ad8cf3.

### Company decision recorded (DECIDED)
The secretariat does not read Confidential or Secret letters. It gets register data only (number, date, kind, classification, parties, delivery fields) and can register, number, send, cancel and record delivery. No "default" marker remains in code.

### Findings (smoke names start with "office r1 ...")
| Finding | State | Smoke check |
|---|---|---|
| C1 files of letters | fixed: `[Authorize]` on download and file info (front uses a Bearer token on every call), letter files only through `GET office/letters/{id}/attachments/{documentId}`, generic download returns 404 for letter files, change/delete refused once the letter is not a draft, SHA-256 of each file stored at signing and recomputed by verify. Also fixed `change-file` binding (documentId in query, file in body) | C1: anonymous 401, change-file refused (with control), bytes unchanged, 404 for non-party, tamper -> verify false + names the file |
| H1/H3 confidential visibility and search | fixed per decision above | H1/H3 checks (metadata only, subject/body search finds nothing for secretariat, finds it for the writer) |
| H2 secretariat on secret letters | fixed | H2: register and send of a secret letter, dispatch item has number only |
| H4 holder/deputy assignment | fixed (HR only; CEO only for his own or a vacant position; never self; deputy by others is pending until the holder or CEO approves; inbox alerts; audited). Not implemented as a smoke check: "deputy never sees a Secret letter dated before the delegation start" (letter dates are always today) | H4 checks |
| H5 numbering | fixed: collision refused at rule save, duplicate number skipped forward in the same transaction | H5 checks |
| H6 overtime without limit | fixed | H6 (450 minutes, restored to 120) |
| H7 shifts over midnight | fixed (boundary between shifts) | H7 (afternoon 605 min, night 690 min / 240 overtime) |
| H8 sick leave | fixed (separate counter through to payroll sheet) | H8 checks |
| M1 cancelled referral | fixed | M1 checks |
| M2 leave over month end | fixed | M2 (pending 2, used 2.25) |
| M3 locked month on approve/reject/record | fixed | M3 |
| M4 future/locked clock events | fixed (import and push) | M4 |
| M5 gates: vouchers, payroll, NCR, CAPA | fixed; extra change in ae0ebcc: `finalize-up-to` also sweeps the voucher gate when nothing is left | M5 checks |
| M6 gate items closed by hand | fixed | M6 |
| M7 audit | fixed: delegation, position, numbering rule, policy, manual/imported clock events, attendance sheets | M7 checks |
| M8 date values of requests | fixed | vehicle request check |
| 5 weak checks | replaced: verify, Persian row date, inbox counts, policy, confidential invisibility | as above |

### Disagreements / deviations
- The CEO may also assign a vacant position (needed to place the HR position first).
- The deputy rule for Secret letters applies to issued letters only.
- Clock audit covers Manual and Import events, not Device punches (too many).
- The review's H4 smoke (HR delegating to himself) is replaced by a non-self deputy plus a refused self-delegation.
- Audit rows for "Added" entities have no entity id (id unknown before the save); the smoke finds them by type and content. Possible duplicate "Added" audit rows were seen for numbering rules; cause not investigated.

### Half-done / open
- CI on ae0ebcc not read: both smoke summaries and install must be checked (branches `innco-ci-smoke-2022`, `innco-ci-smoke-2025-express`, `innco-ci-install`). If a check still fails it is in `.github/scripts/inventory-smoke.sh` (office R1 blocks, from the line "R1: confidential letters" to the end of the office section).
- Unused private helper `Counts` in `src/Services/NasbNiro/Office/LeaveService.cs` can be removed.
- Possible duplicate audit rows: `src/DataLayer/Context/ApplicationDbContext.cs` / `FinanceAuditTrail.cs`.

### R1 CI result on ae0ebcc (read afterwards; both SQL 2022 and 2025 Express failed, install-test and package passed)
1863 passed, 3 failed, all in new R1 checks:
- "C1: the stored bytes of the attachment are unchanged ..." and "C1: after the bytes ... are swapped on disk verify fails": the smoke still does not find the file on disk (`ls src/WebApi/UploadedDocuments/<id>*` returned nothing; the app's content root is probably elsewhere). The service-side behaviour is not shown to be wrong; the file lookup in `.github/scripts/inventory-smoke.sh` (variable `OATTF`) needs the real path (find it with `find / -name "<documentId>*"` in the smoke).
- "M5: so the voucher gate is closed": `vouchers:finalise` is still open after `finalize-up-to`; the sweep added in `src/Services/NasbNiro/Finance/GlService.cs` did not close it (check whether a Checked voucher still exists, e.g. the Tir/Mordad payroll vouchers, so the gate is correctly still open and the precondition is wrong).
Everything else in the R1 blocks passed. Nothing further was pushed.
