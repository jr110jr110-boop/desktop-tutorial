# Independent review 1 of office automation O1 (commit 97f4c54) — findings to fix (milestone OR1)

Reviewer: independent (read-only, no build). Paths are relative to `src/Services/NasbNiro/Office/` unless shown. Every fix needs a smoke check that FAILS on the old behaviour, with exact values. Persian calendar only. Migration as needed (`OfficeReviewFixes1`).

Checked and found correct (no action): device keys (24 random bytes, SHA-256 stored, per device, `FixedTimeEquals`, 401 on unknown device, wrong key or missing key: AttendanceService.cs:443-453). Manual clock events are HR-only, need a reason, and are refused in a payroll-locked month (AttendanceService.cs:290-302). Cancelled letters keep their number. Legacy import keeps its numbers as text and does not touch the counter. `{yy}`/`{yyyy}` are Persian years. Persian month boundaries come from `PersianCal` (29/30 Esfand by leap year). The monthly close writes `FnAttendanceSheet` field by field and skips payroll-locked employees (AttendanceService.Days.cs:245-296). Payroll approval refuses sheets changed after the calculation. `OfficeGates.SyncAsync` and `NotificationService` swallow every error, and every call site runs after the gate's own save. Items are idempotent per (source key, recipient) and are closed on pass/reject for PR, payment request, payroll approve, CAPA, release note, baseline, CR, ITP H point and transmittal.

## Critical

C1. **Signed letters are not immutable, and confidential attachments can be downloaded without logging in.**
- The hash covers only the attachment ids, not the file bytes (LetterService.cs:405).
- Any logged-in user can replace the bytes of any document through `PUT api/v1/documents/change-file`. There is no owner or reference check (WebApi/Controllers/DocumentsController.cs:36-42 → Services/Services/DocumentService.cs:89-114).
- `GET api/v1/documents?DocumentId=` has no `[Authorize]` and no global fallback policy (DocumentsController.cs:54-69).
- `LetterDto` gives every full reader the attachment ids and `SignatureDocumentId`.

Example: Secret outgoing letter L, signed with attachment A (contract annex). User X, who is not a party but once saw A's id in a list or log, (1) downloads A anonymously, and (2) uploads a different annex over A, or a different image over the signer's signature image. `GET letters/L/verify` still answers `valid:true`.

FIX:
- At signing, hash the SHA-256 of every attachment file and of the signature image into `ContentHash`.
- Refuse `ChangeFileAsync`/`UpdateDocumentAsync` for any document referenced by an `OfLetterAttachment` of a non-Draft letter, by `OfLetter.SignatureDocumentId` or `DeliveryReceiptDocumentId`, or by an `OfUserSignature` (in general: owner only).
- Serve letter files only through `GET office/letters/{id}/attachments/{documentId}`, which requires `AccessOf == Full`.
- Put `[Authorize]` on the generic download (check the other modules' front-end use first).

SMOKE:
- Anonymous `GET documents?DocumentId=<attachment of OL2>` → 401.
- QC user `PUT documents/change-file` on that id → refused, and the bytes are unchanged.
- A non-party GET of `office/letters/OL2/attachments/<id>` → 404. The writer → 200.
- After a forced byte change in the test DB (or a direct storage swap), `verify` → `valid:false`.

## High

H1. **Every SECRETARIAT user reads the full text of every numbered Confidential letter.** `AccessOf` gives SECRETARIAT `Full` whenever `Number != null` and the class is not Secret (LetterService.cs:83). `Visible` lists such letters to SECRETARIAT (LetterService.cs:99). The brief says Confidential and Secret go only to explicit parties and the CEO. Example: internal Confidential letter OL2 (expert → CEO position) is returned with its body to every SECRETARIAT user.
FIX: Confidential → `Meta` for SECRETARIAT, as for Secret (number, date, kind, status, delivery data). Secretariat users who are a party (creator, recipient, referral) keep Full.
SMOKE: `as OSEC GET letters/OL2` → success, `.data.metadataOnly == true`, `.data.bodyHtml == null`, subject masked.

H2. **A Secret outgoing letter can never be registered, sent or cancelled, and its subject leaks to all SECRETARIAT users.**
- `LetterActionAsync` and `ReferAsync` require `Access.Full` (LetterService.Actions.cs:33, 248). SECRETARIAT has only `Meta` for Secret, so register/send/receipt/archive/cancel return "نامه پیدا نشد".
- A numbered letter may be cancelled only by SECRETARIAT (Actions.cs:230).
- The dispatch item goes to role SECRETARIAT with `Number — Subject` in its title (Actions.cs:163).

Example: Secret outgoing letter signed → status Signed forever. Every SECRETARIAT inbox shows "نامه امضا شده برای ثبت و ارسال: 05/ص/OPS/00004 — <secret subject>".

FIX: allow register/send/receipt/archive/cancel at `Meta` level for SECRETARIAT, and return the external recipients and delivery data in the Meta DTO. Use a neutral dispatch title for Confidential and Secret ("نامه سری شماره …").
SMOKE: Secret outgoing letter signed → `as OSEC` register, then send with method 2 → status 4. Inbox item `letter:<id>:dispatch` has a title without the subject. `as OSEC GET letters/<id>` → `bodyHtml == null`.

H3. **Search works as a content oracle on letters the user may see only as metadata.** `Subject`, `Contains` (subject + `BodyHtml`) and `Party` filters run on the whole `Visible` set, including Meta rows (LetterService.cs:162-180). Example: SECRETARIAT `GET letters?Contains=مناقصه` returns Secret letter S (masked subject) only when its body contains the word. Repeated queries reveal the text.
FIX: apply the subject, body and party filters only to rows the user has Full access to. In the query: `(Full condition) && filter` OR `(Meta condition) && Number filter only`.
SMOKE: Secret letter whose body contains `RAREWORD123` → `as OSEC GET letters?Contains=RAREWORD123` → 0 rows. As a party → 1 row.

H4. **Delegation and holder abuse: an org admin can take over any position silently.**
- `SaveDelegationAsync` lets any OrgAdmin (HR, PMO, CEO) make any user, including himself, the deputy of any position. The holder does not consent, no alert is sent and nothing is audited (OrgInboxService.cs:246-265).
- `SavePositionAsync` lets HR set itself as the holder of any position (OrgInboxService.cs:189).
- Access is checked against current positions, so the deputy immediately gets Full access to every past Confidential and Secret letter of the position. He can also sign for it (Actions.cs:141).

Example: an HR user delegates the CEO position to himself for one day, reads every Secret letter ever addressed to the CEO position, and signs as the CEO's position.

FIX:
- A delegation created by somebody other than the holder needs the holder's or the CEO's approval (status Pending until then). The deputy must not be the creator.
- The reason is required. An inbox alert goes to the holder.
- `OfDelegation` and `OfPosition.UserId` go into the audit trail.
- Optional ★: deputies see Secret letters only when they were received during the delegation period.

SMOKE: `as OHR` delegation of OP_MGR to OHRID → stays pending, and `as OHR GET office/me` has no on-behalf position. After `as OMGR` approves → the position appears. `OMGR` has an Alert item `delegation:<id>`.

H5. **A letter number can collide, and then that unit cannot sign or register any letter, permanently.** `NextNumberAsync` keeps one counter per rule and year (LetterService.cs:367-383). `SaveRuleAsync` accepts a unit rule whose pattern can produce numbers that the general rule already issued (LetterService.Admin.cs:96-125). The unique index (Kind, Number) then throws. `FinishAsync` maps the error to "دوباره تلاش کنید" (Actions.cs:21), but the counter increment rolls back too, so every retry produces the same number. A concurrent increment (RowVersion on the counter) also only fails, with no retry.

Example: the general outgoing rule `{yy}/{kind}/{unit}/{seq:5}` has issued 05/ص/OPS/00003 and 05/ص/OPS/00007 (the counter is shared by all units). The admin adds an OPS rule with the same pattern. OPS letters 00001 and 00002 go through. 00003 hits the unique index, and from then on every OPS outgoing signature fails.

FIX:
- On save, a unit rule must contain a literal prefix or token that the general rule of the kind cannot produce, or the save is refused. Simplest: refuse a unit rule whose pattern equals the general pattern unless it has an extra literal.
- In `NextNumberAsync`, skip numbers that already exist: loop `Last++` while (Kind, Number) exists. The skipped values were issued to other letters, so this creates no gap in the rule's own register.
- Retry two or three times on `DbUpdateConcurrencyException` and on a unique violation of the counter or the number.

SMOKE: sign 2 OPS outgoing letters under the general rule, add an OPS rule with the same pattern → refused with a message. Second case: patterns with a literal → a third OPS letter signs, and the number is exact.

H6. **Overtime is zero when the policy needs no pre-approval and has no daily maximum.** `cap = OvertimeNeedsPreApproval ? Approved : (Max ?? Approved)` (AttendanceMath.cs:178): with pre-approval off, no maximum and no approved request, the cap is 0. Example: shift 08:00-16:00, policy `overtimeNeedsPreApproval:false, maxOvertimeMinutesPerDay:null`, clock 08:00 / 19:00 → overtime 0, expected 180. Payroll pays no overtime.
FIX: `cap = needsPre ? approved : int.MaxValue`, then `Math.Min(cap, Max)` when Max is set.
SMOKE: that policy and day → `overtimeMinutes == 180`. Restore the policy afterwards.

H7. **Clock events after midnight on a day shift, and after +3 h on a night shift, are given to the wrong day or dropped.**
- A day shift's window is the calendar day (AttendanceMath.cs:115-121). The events are filtered by that window (AttendanceService.Days.cs:124-125).
- Day shift 14:00-22:00, in 13:55, out 00:30: day D has 1 event → Incomplete. With `acceptIncompleteAsAbsent` that becomes 1 absence day. Day D+1 then runs from 00:30 to 22:05: 1265 minutes worked and 330 night minutes.
- Night shift 22:00-06:00 with approved overtime until 10:00: the window ends at 09:00, so the 10:00 out is in no window and is lost. Day D becomes Incomplete.

FIX: build the windows per employee from consecutive shift starts. Day D runs from (start of D − 3 h, or the midpoint between the end of D−1 and the start of D) to the same point before D+1. At minimum, extend the day-shift window to `end + max(OT, 6 h)` when the next day's shift starts later than that. Never leave a gap between two consecutive windows.
SMOKE: shift 14:00-22:00, events D 13:55 and D+1 00:30 → D: worked 605 (13:55-00:30 = 635, minus the 30-minute break), Present, lastOut D+1 00:30. D+1: no 00:30 event. Night shift 22:00-06:00 with 4 h approved overtime, out 10:00 → D overtime 240.

H8. **Sick leave reduces the annual leave balance.** `UsesBalance` excludes SickLeave (LeaveService.cs:58), but `BalanceAsync` counts approved sick leave in `PendingDays` (LeaveService.cs:153-157). The daily computation turns sick days into `PaidLeaveDays` (AttendanceMath.cs:143-145, AttendanceService.Days.cs:144-145). Payroll approval then adds `PaidLeaveDays` to `FnLeaveBalance.Used` (Finance/PayrollRunService.cs:380, 396). Example: 3 working days of sick leave → available balance −3 now, and `Used` +3 after payroll. Sick leave is paid by insurance and is not taken from the annual leave.
FIX: a separate `SickLeaveDays` on `OfAttendanceDay`, and on `FnAttendanceSheet` (or have payroll read it as its own input). `PaidLeaveDays` holds only DailyLeave and HourlyLeave. Pending counts only `UsesBalance` kinds.
SMOKE: approved sick leave of 2 days → balance unchanged. The month close puts 2 in the sick field and 0 in `paidLeaveDays`. After payroll approval, `FnLeaveBalance.used` is unchanged.

## Medium

M1. **A cancelled referral keeps granting Full access.** `AccessOf` and `Visible` accept any referral, cancelled ones included (LetterService.cs:80, 104). Example: a Confidential letter misdirected by referral to position P; the referral is cancelled; P still reads the letter.
FIX: ignore `Status == Cancelled` for the target position (keep access for the creator of the referral).
SMOKE: refer OL2 to OP_CHK, cancel → `as OCHK GET letters/OL2` → 404.

M2. **The leave balance loses days across a month boundary.** `PendingDays` counts a request only when the month of its FromDate is after `LastMonth` (LeaveService.cs:157), while payroll books only the days inside each month. Example: leave 1405/04/30-1405/05/02 (3 days). After Tir payroll, Used is +1, and the request is no longer pending because month 4 ≤ 4. Available is overstated by 2 until Mordad payroll, so a second leave beyond the real balance is accepted.
FIX: pending = the days of approved requests that fall in months after `LastMonth` (split per day by the calendar).
SMOKE: that case → after Tir approval, `available` = before − 3 exactly.

M3. **Leave approve, record and reject ignore the payroll lock** (LeaveService.cs:498-533). Submit and cancel do check it. Example: leave submitted in Shahrivar, payroll for Shahrivar approved, then the leave is approved. The days stay absent in the approved payroll, and the leave is never deducted, because pending skips months ≤ LastMonth.
FIX: refuse approve/record/reject when FromDate or ToDate is in a locked month.
SMOKE: approving such a leave → message "حقوق این ماه تایید شده".

M4. **Clock import and device push write into payroll-locked months and accept future timestamps** (AttendanceService.cs:387-417, 443-475). The events are stored but the days are not recomputed, so the clock records no longer match the approved payroll.
FIX: in `StoreAsync`, reject rows in locked months (count them as rejected with a reason) and timestamps later than now + 5 minutes.
SMOKE: push one event on 2026-09-01 (Shahrivar, locked) and one for tomorrow → `accepted == 0`, `rejected == 2`.

M5. **Missing or stale gate items.**
- (a) Automatic vouchers created Checked by the posting job (Finance/EventPostingService.cs:153) never call `SweepVouchersAsync`, so FINANCEMANAGER gets no item until somebody touches a manual voucher.
- (b) `ReopenCalculatedRunsAsync` sets Calculated runs back to Draft without closing `payroll:<id>` (Finance/PayrollMasterService.cs:81-86). The approval item stays open, and approving gives "فقط اجرای محاسبه‌شده".
- (c) The NCR committee and approval gates of `InventoryQualityService` have no notify call (the brief lists NCR).
- (d) An auto-drafted CAPA (Open, no owner) creates no item for QA (Quality/CapaService.cs:272 waits only for Implemented or EffectivenessCheck).

FIX: sweep after the posting job's save. Sync `payroll:<id>` inside the reopen. Add `ncr:<id>` (committee roles) and `capa-assign:<id>` (QA).
SMOKE: post an automatic event → `vouchers:finalise` is open for FINANCEMANAGER. Change a rate after a calculation → the `payroll:<id>` item is Done. A Major NCR → an item for the committee.

M6. **Gate Task items can be closed by hand and are never recreated.** "done" is allowed for kind Task and Alert (OrgInboxService.cs:429-434). `letter:<id>:dispatch`, `leave-record:<id>` and `req-complete:<id>` are role Tasks. Example: one SECRETARIAT user closes the dispatch item; the signed letter is then waiting in no inbox.
FIX: refuse manual "done" for items whose `SourceKey` belongs to a gate. Or make these items kind Approval.
SMOKE: `as OSEC POST inbox/act {done}` on `letter:<id>:dispatch` → refused.

M7. **Office records are not audited.** `FinanceAuditTrail.Audited` (DataLayer/Context/FinanceAuditTrail.cs:21-73) leaves out `OfClockEvent` (manual and import, where the brief requires an audit), `OfDelegation`, `OfPosition` (UserId, CanSign), `OfNumberingRule`, `OfAttendancePolicy` and `FnAttendanceSheet` (payroll input).
FIX: add them. For the sheet, audit the day and hour fields only.
SMOKE: a manual clock event and a policy change → each has an audit row with the user and the Persian date.

M8. **Date fields of general requests are shown in the Gregorian calendar.** A value entered as 1405/06/10 is stored and returned as "2026-09-01" (RequestService.cs:297-300, returned at :216-222).
FIX: store the ISO date, and return the value Persian-formatted (or add a `ValuesText` map with Persian dates).
SMOKE: vehicle request with date "1405/06/10" → `.data.values.<field> == "1405/06/10"` (or `valuesText`).

## Vacuous or weak smoke checks (replace)

| Line in .github/scripts/inventory-smoke.sh | Check | Problem | Replace with |
|---|---|---|---|
| 3111 | "inbox counts are returned" | `.data.open >= 0` is always true | exact `open` and `unread` for OEMP after a known set of items |
| 2888 | "the stored hash matches" | `verify` recomputes the hash with the same function, so `valid` is true by construction | the tamper case of C1 (`valid:false`) |
| 3066 | "Persian-dated row landed in Mordad" | the regex `2026-08-1[0-2]` accepts an off-by-one or off-by-two conversion | the exact Gregorian date of the Persian row |
| 2951-2954 | confidential invisibility | tested only with QC_TOKEN; no SECRETARIAT, Secret, search or attachment case | the H1, H2, H3 and C1 checks |
| 3017 | policy | only `max:120`, so H6 is never exercised | the H6 check |
