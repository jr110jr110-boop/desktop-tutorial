# Office automation (replaces Rahkaran office automation and attendance), release O1 — backend brief

Ground rules, CI loop and design principles: as in /home/claude/innco/docs/quality-backend-brief.md — binding. Prefix `Of`, routes `api/v1/office`, sequence kinds 531–560, constants `OfficeRoles`. Migration `OfficeAutomation`. Depends on finance F2 `FnEmployee` (personnel) and feeds `FnAttendanceSheet`.

Existing: no letter, minutes, attendance, notification or inbox entity exists. Only the legacy `CardBoard` (role-based task view of legacy statuses), `IEmailSender`/`ISmsSender` (identity), quality `ControlledDocument`/`DocumentDistribution`, project-control `PcTransmittal` (project documents only — keep it; letters do not replace transmittals).

## Roles ★
`SECRETARIAT` 26 "دبیرخانه", `HR` 27 "اداری و منابع انسانی". Every user can draft letters and requests and see their own inbox. Claims 2600+/2700+.

## 1. Organisation
- `OfUnit` (organisation chart: code, title, parent, manager position) and `OfPosition` (title, unit, user (current holder), employee link, isManager, signing authority flag). Letters and referrals go to **positions**, not people, so a replacement or deputy inherits the inbox. `OfDelegation` (position, deputy user, from/to) — the deputy sees and acts on the position's inbox with "on behalf of" recorded.
- Seed: only a root unit "شرکت" — the rest is entered by HR (no people names in seed).

## 2. Unified inbox and notifications (used by all modules)
- `OfInboxItem` (recipient position or user, kind {Letter, Referral, Approval, Task, Alert, MeetingAction}, title, source module/type/id, link, due date, priority, status {Unread, Read, Done, Cancelled}, created). A `INotificationService.NotifyAsync(...)` used by the other modules: when a gate needs someone (PR waiting approval, voucher to finalise, NCR committee, CR overdue, payroll approval …) an inbox item is created and closed automatically when the gate is passed (idempotent by source key). Add calls in the existing modules for their main gates (procurement approvals, payment approval, voucher finalise, NCR/CAPA, ITP H point, change request, baseline approval, transmittal response due, release note).
- Delivery channels: in-app (always), internal SMTP email (optional, uses existing `IEmailSender` only if configured; offline-safe: failures never block), SMS off by default.
- Legacy `CardBoard` keeps working; the front shows both until the legacy statuses are retired.

## 3. Letters (مکاتبات)
- `OfLetter` (kind {Incoming, Outgoing, Internal}, number (assigned at registration/signature), date, subject, body (rich text HTML sanitized), sender (position for outgoing/internal; external party for incoming: customer, supplier (Provider), or free organisation with `OfExternalParty`), recipients (positions or external parties) with type {To, Cc}, classification {Normal, Confidential, Secret}, urgency {Normal, Urgent, Immediate}, incoming reference number and date (incoming), attachments (existing document storage ids), related records (contract, project, purchase order, tender/inquiry, service request …), reply-to letter, follow-up letter, status {Draft, InApproval, Signed, Registered, Sent, Received, Archived, Cancelled}, signature position + signed at, paper copy flag, delivery method {Hand, Post, Courier, Email, Fax}, delivery receipt).
- Numbering: `OfNumberingRule` (kind, unit (optional), pattern with tokens `{yyyy}` Persian year, `{yy}`, `{seq:n}`, `{unit}`, `{kind}` e.g. "{yy}/{unit}/{seq:5}" ★ default until the admin manager gives the company format; sequence reset yearly). Incoming letters numbered at registration by SECRETARIAT; outgoing/internal at signature. Numbers never reused; a cancelled number stays in the register as cancelled.
- Workflow: Draft (anyone) → InApproval (approval chain of positions defined by the drafter or a template, each approves/returns with comment) → Signed (signing position only; signature = the signer's stored signature image + server timestamp + hash of the letter content stored; any change after signing is impossible) → Registered/Sent by SECRETARIAT (outgoing) or delivered to inboxes (internal).
- Referral (ارجاع): any holder of a letter can refer it to positions with instruction text, action type {ForAction, ForInformation, ForComment}, due date; the receiver records action/comment; the referral tree is kept; a referral is Done when the receiver closes it (with note for ForAction).
- Access: Normal visible to sender, recipients, referral tree, SECRETARIAT and CEO; Confidential/Secret only to explicit parties and CEO (SECRETARIAT sees metadata only for Secret ★).
- Letter templates `OfLetterTemplate` (title, header/footer HTML with company letterhead, body placeholders). Printing/PDF is done by the front (B Zar A4); backend returns structured JSON.
- Search: by number, subject, party, date, related record, full text on subject/body (SQL Server full-text if available, else LIKE).
- **Import of the legacy archive**: endpoint for SECRETARIAT that imports a folder export (a manifest Excel: number, date, kind, subject, party, file name(s)) + uploaded files into Archived letters with flag `Imported` (number kept as text, not consuming the new sequence). Rahkaran export format unknown → manifest approach ★.

## 4. Meetings and minutes (صورتجلسات)
- `OfMeeting` (title, kind {Management, Project, Technical, Quality, Customer, Other}, date/time, place, chair, secretary, attendees (positions/users + external names), agenda items, related records, status {Planned, Held, MinutesDraft, MinutesApproved, Cancelled}).
- Minutes: text per agenda item + decisions `OfDecision` (text, owner position, due date, status {Open, Done, Cancelled}, done note, evidence attachment). Minutes approved by the chair (and attendees sign electronically, optional). Decisions appear in owners' inboxes, overdue decisions alert the chair and the CEO dashboard ("مصوبات معوق").
- The quality ManagementReview and project meetings link to `OfMeeting` (nullable FK) — no duplication of decisions logic in those modules for new records.

## 5. Attendance, leave, mission, overtime (حضور و غیاب)
- `OfShift` (title, start, end, break minutes, night hours window, days), `OfShiftAssignment` (employee, shift, from/to).
- `OfClockEvent` (employee, timestamp, device id, direction {In, Out, Unknown}, source {Device, Import, Manual}). Import from attendance devices: generic file import (CSV/Excel: card/personnel code, date, time, device) + an HTTP push endpoint for devices/middleware on the LAN (API key per device stored hashed). Manual events only by HR with reason (audit).
- Requests `OfLeaveRequest` {DailyLeave, HourlyLeave, SickLeave, UnpaidLeave, Mission (daily/hourly), Overtime (pre-approval), ForgotClock}: employee drafts, unit manager position approves, HR records; balance check (leave balance from `FnLeaveBalance`); sick leave needs an attachment.
- Daily attendance computation (hosted service 01:00 ★ and on demand): per employee per day: shift, first in / last out pairing, worked hours, delay, early leave, absence, overtime (only approved or within policy), night hours, holiday work (company holidays + Fridays per calendar), leave/mission applied. Policy table `OfAttendancePolicy` (grace minutes, max overtime per day, overtime needs pre-approval yes/no, rounding) — inputs, no seeded values except grace 0.
- Monthly close (HR, then FINANCEMANAGER not needed): creates/updates `FnAttendanceSheet` rows for payroll (days worked, overtime/night/holiday hours, leave days, absence days, mission days) — payroll uses these; after the payroll run is Approved the month is locked here too.
- Booking check: production time bookings vs attendance hours (production P1 reads this).

## 6. General requests (simple workflow forms)
- `OfRequestType` (title, fields JSON schema, approval chain of positions, target module note) and `OfRequest` (type, requester, values, status, approvals) — for small internal requests (vehicle, IT, training, document copy). Release O1 includes the engine and three seed types without company-specific data ★: "درخواست خودرو", "درخواست پشتیبانی فناوری اطلاعات", "درخواست آموزش".

## Tests (smoke section "office")
Units/positions, delegation; letter draft → approval chain → sign (non-signer cannot sign) → number by rule pattern exact → registered; edit after sign rejected; referral tree and closing; confidential letter invisible to a non-party; incoming registration numbering; legacy import of 2 letters keeps their numbers and does not consume the sequence; meeting → minutes → decisions in owner inbox → overdue alert; inbox items created by a procurement approval gate and closed when approved; clock import → daily computation exact on a known shift (delay, overtime, night hours); leave request approval reduces balance; monthly close writes FnAttendanceSheet with exact values; device push with wrong key rejected.
