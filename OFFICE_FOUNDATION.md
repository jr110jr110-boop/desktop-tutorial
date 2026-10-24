# Office automation: front foundation

Shared layer for the pages of اتوماسیون اداری. Backend: `api/v1/office` (controllers in `src/WebApi/Controllers/Office/OfficeControllers.cs`; the controller is the truth where it differs from `office-o1-report.md`).

## Files
- `src/api/OfficeAPIs.ts` API functions (all return `[data, null]` or `[null, error]`; data is camelized; lists are paged `{ data, totalItems }`, use `rowsOf()` / `usePagedList()`).
- `src/composables/office.ts` Persian label maps, `can(record, action)`, date helpers.
- `src/composables/roles.ts` `Roles/RoleIds/roleTitles` + SECRETARIAT (26) and HR (27), `OfficeRoles`.
- `src/router/index.ts` routes, `src/components/common/KsSidebar/*` menu section, `src/views/office/*.vue` stubs.

## API functions (OfficeAPIs.ts)
Organisation / me: `getUnits(params)`, `saveUnit(dto)`, `getPositions(params)`, `savePosition(dto)`, `getDelegations(params)`, `saveDelegation(dto)`, `cancelDelegation(id)`, `approveDelegation(id)`, `rejectDelegation(id)`, `getOrgTree()`, `getMyOffice()`, `saveMySignature(documentId)`.
Inbox: `getInbox(params)`, `getInboxCounts()`, `inboxAction(id, "read"|"unread"|"done")`.
Letters: `getLetters(params)`, `getLetter(id)`, `saveLetter(dto)`, `letterAction({id, action, approvalChain?, actingPositionId?, comment?, deliveryMethod?, date?, receipt?, receiptDocumentId?, rowVersion?})`, `verifyLetter(id)`, `referLetter(dto)`, `referralAction(id, "comment"|"close"|"cancel", text?, actingPositionId?)`, `downloadLetterFile(letterId, documentId, fileName?)` (authenticated blob, saves the file, returns error text or null), `openLetterFile(letterId, documentId)` (blob object URL for preview; caller revokes), `getParties(params)`, `saveParty(dto)`, `getNumberingRules(params)`, `saveNumberingRule(dto)`, `getLetterTemplates(params)`, `saveLetterTemplate(dto)`, `importLetters({rows?, csv?})`.
Meetings: `getMeetings(params)`, `getMeeting(id)`, `saveMeeting(dto)`, `meetingAction({id, action, reason?, actingPositionId?, presentAttendeeIds?, rowVersion?})`, `getDecisions(params)`, `decisionAction({id, action:"done"|"cancel", note?, evidenceDocumentId?, actingPositionId?})`, `checkOverdueDecisions()`.
Attendance (`office/attendance/`): `getShifts()`, `saveShift(dto)`, `getShiftAssignments(params)`, `saveShiftAssignment(dto)`, `getDevices()`, `saveDevice(dto)` (returns the API key once), `getClockEvents(params)`, `addManualClock(dto)`, `importClock({deviceCode?, rows?, csv?})`, `getAttendancePolicy()`, `saveAttendancePolicy(dto)`, `computeAttendance({fromDate, toDate, employeeId?})`, `getAttendanceDays(params)`, `closeAttendanceMonth(year, month, acceptIncompleteAsAbsent)`, `getAttendanceMonth(year, month)`, `getWorkedHours(employeeIds, from, to)`. The device `push` route is for LAN devices (anonymous, X-Device-Code / X-Device-Key) and is deliberately not exposed.
Leave: `getLeaves(params)`, `getLeave(id)`, `saveLeave(dto)`, `leaveAction(id, action, comment?, actingPositionId?, rowVersion?)`, `getLeaveBalance({employeeId?, year?, month?})`.
Requests: `getRequestTypes()`, `saveRequestType(dto)`, `getRequests(params)`, `getRequest(id)`, `saveRequest(dto)`, `requestAction(id, action, comment?, rowVersion?)`.
Each function carries a doc comment with the DTO fields, the allowed action strings and who may call it. File upload: reuse `uploadFile(file, title)` of `InventoryAPIs.ts` (returns the document id), then pass the id in the DTO.

## Enum maps (office.ts), numeric values from `OfficeEnums.cs`
`inboxKinds` 1 Letter..6 MeetingAction (`inboxKindKeys` maps the English keys of `counts.byKind`), `inboxStatuses` 0..3, `urgencies` 0..2, `letterKinds` 1..3, `letterStatuses` 0..7, `classifications` 0..2, `recipientTypes` 1..2, `deliveryMethods` 1..5, `partyKinds` 1..3, `relatedKinds` 1..7 and 99, `approvalStatuses` 0..2, `referralActions` 1..3, `referralStatuses` 0..2, `delegationStatuses` 0..2, `meetingKinds` 1..6, `meetingStatuses` 0..4, `decisionStatuses` 0..2, `clockDirections` 0..2, `clockSources` 1..3, `dayStatuses` 1..6, `weekDays` (0 = Sunday .. 6 = Saturday) + `weekOrder`, `leaveKinds` 1..7, `leaveStatuses` 0..5, `requestStatuses` 0..5, `requestFieldTypes`, `actionTitles` (Persian title for every action string).
Helpers: `can(record, action)`, `dateText(iso)`, `dateTimeText(iso)`, `persianOf(text, iso)`, `minutesText(min)`, `timeOf(iso)`, `badgeOfLetter/Leave/Request/Meeting/Decision(status)`. Dates shown to the user are always Jalali (`jalali()` of `inventory.ts`); prefer the API `...Text` field.

## OfficeRoles (roles.ts)
| Name | Roles | Used for |
|---|---|---|
| everyone | [] (no check) | inbox, letters (drafts), meetings, requests, own leave and days |
| orgAdmins | HR, PMO, CEO | units, positions, request types, policy, shifts, delegations of others |
| letterAdmins | SECRETARIAT, PMO, CEO | numbering rules, letter templates |
| secretariat | SECRETARIAT | register/send/receipt/archive, incoming letters, legacy import |
| hr | HR | manual clock, clock import, leave record, assigning holders |
| attendanceAdmins | HR, PMO, CEO | shifts, assignments, devices, policy |
| attendanceReaders | HR, CEO | all days and clock events, compute |
| monthReaders | HR, CEO, PAYROLL, FINANCEMANAGER | month status |
| monthClose | HR | close-month |
| workedHours | HR, CEO, PMO, PROJECTMANAGER, PLANNING, CONSTRUCTION, FINANCEMANAGER | worked-hours |
| overdueCheck | SECRETARIAT, PMO, CEO, HR | decisions/check-overdue |
| ceo | CEO | |
Unit manager / chair / approver rights depend on the user's positions, not on roles: use `allowedActions`.

## Routes (meta.roles in brackets)
OfInbox /office/inbox, OfLetters /office/letters, OfLetter /office/letters/:id ("new"), OfLetterAdmin /office/letter-admin [letterAdmins], OfOrg /office/org (alias /office/delegations), OfMe /office/me, OfMeetings /office/meetings, OfMeeting /office/meetings/:id, OfDecisions /office/decisions, OfAttendance /office/attendance, OfAttendanceSetup /office/attendance-setup [attendanceAdmins], OfMonthClose /office/month-close [monthReaders], OfLeaves /office/leaves, OfRequests /office/requests, OfRequestTypes /office/request-types [orgAdmins].
Extra routes (same view files) for the inbox links the API writes: OfLeave /office/leaves/:id (Leaves.vue) and OfRequest /office/requests/:id (Requests.vue). The page agents must read `route.params.id` in Leaves.vue / Requests.vue. Inbox items from other modules carry their own `link`.

## Sidebar
Section "اتوماسیون اداری" with all pages, filtered by role like the other sections (`disabled` when the role lacks). The inbox item shows a red counter of `open` from `GET office/inbox/counts` (loaded on mount, refreshed every 60 s). It needed a small `badge?: number` on the `Links` type and one `<span>` in `KsSidebarLink.vue`.

## Mismatches: report vs controllers
1. Routes missing from the report: `POST delegations/{id}/approve` and `POST delegations/{id}/reject` (holder or CEO; not the creator). Added as `approveDelegation` / `rejectDelegation`.
2. `GET shifts` and `GET devices` answer plain arrays, not paged lists. `GET devices` and `GET shift-assignments` are refused for non-admins (HR, PMO, CEO; assignments also HR/CEO via AttendanceReaders); the report says "read any" for shift-assignments.
3. `POST attendance/compute`: service allows HR and CEO only (AttendanceReaders); the report says HR, PMO, CEO.
4. `GET attendance/month/{y}/{m}`: HR, CEO, PAYROLL, FINANCEMANAGER (report: any).
5. `POST attendance/worked-hours`: HR, CEO, PMO, PROJECTMANAGER, PLANNING, CONSTRUCTION, FINANCEMANAGER (report: HR, CEO, PMO).
6. `LetterQuery`/`MeetingQuery` etc. have no own `contains`; `Contains` comes from the base `OfficePageQuery`.
7. `DelegationDto` also has `status` (0 approved, 1 pending, 2 rejected), `isActive`, positionTitle, deputyName, createdByName, decidedByName, decidedAtText; `DelegationQuery` has `currentOnly`. `LetterVerifyDto` has `changedDocumentIds`. `LetterAttachmentDto` is `{ documentId, fileName }`. `InboxItemDto.createdAt/readAt/closedAt` exist.
8. Letter files: only `GET letters/{id}/attachments/{documentId}` (verified in the controller; 404 if the caller may not read the letter in full). The R1 decision applies: the secretariat sees Confidential and Secret letters as metadata only (`metadataOnly=true`), report section 4.6 is outdated.
9. The controllers carry only `[Authorize]` (plus roles on `decisions/check-overdue` and anonymous `push`); every other role rule is in the services, so a refused call is HTTP 200 with `success=false` (the helpers return it as the error).
10. The server camel-cases dictionary keys too (`values` of a request, `byKind`); request field names should therefore be lowerCamel (the seed types are).
11. Inbox `link` values written by the API: `/office/letters/{id}`, `/office/leaves/{id}`, `/office/meetings/{id}`, `/office/requests/{id}`, `/office/delegations`, `/office/me`; all resolve to routes above.
