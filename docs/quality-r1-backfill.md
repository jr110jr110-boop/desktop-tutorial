# Quality R1 review fixes: data after the `QualityR1` migration

The migration only adds columns (nullable, or with a default). Rows that exist before the upgrade need the steps below.
Every statement is commented out on purpose: read the `SELECT` first, then run the `UPDATE` you agree with.
Table names are the EF names; enum values are given in brackets. All columns the model change added are listed at the end.

## 1. Server time zone

Back-date windows, witness notice periods, expiry and "today" all use the server's local date. The host (or container) must run in
`Asia/Tehran` (`TZ=Asia/Tehran`; Windows: `Iran Standard Time`). In UTC the server's "today" is still yesterday between 00:00 and 03:30 Tehran time, so a user's
date of today looks like the future and is refused.

## 2. Released release notes without lines

The delivery gate now covers every delivery line with a line of a Released note of the work order. A note released before lines existed covers nothing,
so deliveries of its work order stop. Do not invent lines in SQL: QC cancels the note and releases a new one with its lines (quantities and serial numbers).

```sql
-- SELECT n.Id, n.Number, n.WorkOrderId FROM QaReleaseNotes n
--  WHERE n.Deleted = 0 AND n.Status = 1 /* Released */
--    AND NOT EXISTS (SELECT 1 FROM QaReleaseNoteLines l WHERE l.ReleaseNoteId = n.Id);
```

## 3. Management review decisions

Decisions now have a status (Open 0, Done 1, Cancelled 2). Old rows are Open. A decision with a CAPA follows the CAPA: closed (4) is Done, cancelled (5) is Cancelled.

```sql
-- UPDATE d SET d.Status = CASE c.Status WHEN 4 THEN 1 ELSE 2 END,
--              d.DoneAt = COALESCE(c.ClosedAt, c.CancelledAt), d.DoneByUserId = COALESCE(c.ClosedByUserId, c.CancelledByUserId),
--              d.DoneNote = N'Followed the CAPA (backfill)'
--   FROM QaReviewDecisions d JOIN QaCapas c ON c.Id = d.CapaId
--  WHERE d.Status = 0 AND c.Status IN (4 /* Closed */, 5 /* Cancelled */);
-- Decisions without a CAPA stay Open: the responsible person reports them done, QA or PMO confirms.
```

## 4. CAPA

* `Mandatory` is fixed at creation from now on. Old rows read false; set it by the same rule the code uses.
* `Iteration` (default 1) and `EscalationLevel` (default 0) are already defaulted by the column definitions; nothing to do.
* Do NOT insert the setting row `CapaEscalationBootstrapped`. When it is absent the first nightly run (or the first `capas/escalate` call) records the escalation level
  of the CAPAs that are already late without telling QA or the CEO, then writes the marker. Insert the row beforehand only if the whole late backlog should be announced on the first night.

```sql
-- UPDATE QaCapas SET Mandatory = 1 WHERE Deleted = 0 AND (AutoDrafted = 1 OR Source IN (1 /* Ncr */, 2 /* AuditFinding */));
-- Optional, only if the backlog must be announced: INSERT INTO QaSettings ([Key], Value, Title, Source) VALUES (N'CapaEscalationBootstrapped', 1, N'marker', N'system');
```

## 5. Settings outside their range

Reading already forces every stored value into its range (a fraction is cut), so nothing breaks; this only makes the stored rows equal what the screen shows.
Ranges: NcrMinorDueDays 1-180, NcrMajorDueDays 1-180, CapaEffectivenessDays 1-730, ComplaintResponseDays 1-90, ItpNoticeDaysIran 1-90, ItpNoticeDaysAbroad 1-180,
WeldContinuityMonths 1-12, CalibrationWarnDays 1-180, ContinuityWarnDays 1-180, MajorNcrCommitteeMin 2-10, MaxBackdateDays 0-30, EffectivenessWaitDays 7-365,
MinNoticeDays 1-30, IncomingNcrDueDays 1-30, CapaCeoEscalationDays 1-90, ExpiryWarnDays 1-365.

```sql
-- UPDATE s SET s.Value = CASE WHEN CAST(s.Value AS int) < r.Lo THEN r.Lo WHEN CAST(s.Value AS int) > r.Hi THEN r.Hi ELSE CAST(s.Value AS int) END
--   FROM QaSettings s JOIN (VALUES
--     (N'NcrMinorDueDays',1,180),(N'NcrMajorDueDays',1,180),(N'CapaEffectivenessDays',1,730),(N'ComplaintResponseDays',1,90),(N'ItpNoticeDaysIran',1,90),
--     (N'ItpNoticeDaysAbroad',1,180),(N'WeldContinuityMonths',1,12),(N'CalibrationWarnDays',1,180),(N'ContinuityWarnDays',1,180),(N'MajorNcrCommitteeMin',2,10),
--     (N'MaxBackdateDays',0,30),(N'EffectivenessWaitDays',7,365),(N'MinNoticeDays',1,30),(N'IncomingNcrDueDays',1,30),(N'CapaCeoEscalationDays',1,90),(N'ExpiryWarnDays',1,365)
--   ) r ([Key], Lo, Hi) ON r.[Key] = s.[Key];
```

## 6. Rework / Repair NCRs and "re-inspect" out-of-tolerance items

A Rework or Repair NCR is closed only with the accepted re-inspection that belongs to it (`ReinspectionKind` + `ReinspectionRecordId`, checked against the NCR, its lot, serials, dates and
the closer), and an out-of-tolerance item assessed "Reinspect" keeps its case open until a new record is linked (`out-of-tolerance/reinspection`). Old rows have no link, so they cannot be closed through the screen.
No date-based exemption is coded (it would be a hidden bypass). Decide per row: QC links the real re-inspection through `ncr/close` / the OOT link call (preferred), or, for rows
whose evidence exists but was never linked, the DBA sets the link after QC has confirmed it.

```sql
-- SELECT n.Id, n.Number, n.CreateDate FROM InvNonConformances n
--  WHERE n.Deleted = 0 AND n.Disposition IN (2 /* Rework */, 3 /* Repair */) AND n.Status IN (1 /* Approved */, 2 /* Executed */) AND n.ReinspectionRecordId IS NULL;
-- SELECT i.Id, i.CaseId, i.SourceNumber FROM QaOotItems i JOIN QaOotCases c ON c.Id = i.CaseId
--  WHERE c.Status = 0 /* Open */ AND i.Assessment = 2 /* Reinspect */ AND i.ReinspectionRecordId IS NULL;
-- After QC confirmed a row: UPDATE InvNonConformances SET ReinspectionKind = N'Record' /* or Inspection / Report */, ReinspectionRecordId = <id> WHERE Id = <ncr id>;
```

## 7. Opening-balance drafts with a zero price

A draft opening balance (type 15) must carry a price above zero on every line, or the finance manager must save it with `allowZeroPrice` (stored as `ZeroPriceApprovedAt/ByUserId`;
any later save by someone else clears it). Old drafts with unpriced lines are refused at confirm until the finance manager re-saves them. Only `import-opening` and the finance manager's save set the approval; never set it in SQL.

```sql
-- SELECT d.Id, d.Number FROM InvDocuments d
--  WHERE d.Deleted = 0 AND d.Type = 15 AND d.Status = 0 AND d.ZeroPriceApprovedByUserId IS NULL
--    AND EXISTS (SELECT 1 FROM InvDocumentLines l WHERE l.DocumentId = d.Id AND ISNULL(l.UnitPrice, 0) <= 0);
```

## 8. Pending item quality changes

A request now remembers the gates the item had when it was made (`Old*`), and QA's decision is refused when the item has changed since. Pending rows from before have empty `Old*`
values (false / null), which would make every one of them look stale or restore the wrong state. Either stamp them with the item's current gates (QA decides on what the item is now) or reject them so the requester asks again.

```sql
-- UPDATE c SET c.OldRequiresIncomingInspection = i.RequiresIncomingInspection, c.OldRequiredCertificateType = i.RequiredCertificateType, c.OldShelfLifeDays = i.ShelfLifeDays
--   FROM InvItemQualityChanges c JOIN InvItems i ON i.Id = c.ItemId WHERE c.Deleted = 0 AND c.Status = 0 /* Pending */;
-- or: UPDATE InvItemQualityChanges SET Status = 2 /* Rejected */, DecidedAt = SYSDATETIMEOFFSET(), DecisionNote = N'Rejected by the upgrade: request again' WHERE Deleted = 0 AND Status = 0;
```

## 9. Inspection and test plans, and evidence files

* Plans saved with `NoticeDays` below `MinNoticeDays` (default 1) are already read at the minimum; the stored value can stay. To make the rows honest:
  `-- UPDATE QaItps SET NoticeDays = 1 WHERE NoticeDays < 1;` (change it for the company's own minimum).
* A file proves a plan activity only if it was uploaded lately by a QC, QA or engineering user. `Documents.UploadedByUserId` is new (null for every old file) and old files have
  `RegisterDate` 0001-01-01 (unknown), so they can never be used as proof of an activity that is not done yet; QC uploads the file again. Activities already Done keep their evidence.
* Plans in use keep working. Their client approval is now recorded by someone who did not prepare, revise or submit them; `EditorUserIds` is empty on old rows, which only means "nobody recorded so far".

## Columns added by this review round (beyond the first R1 report)

| Table | Column | Type |
|---|---|---|
| Documents | UploadedByUserId | int null |
| InvDocuments | ZeroPriceApprovedAt, ZeroPriceApprovedByUserId, WriteOffNcrId | datetimeoffset null, int null, int null |
| InvQcInspections | MeasurementsJson | nvarchar(max) null |
| InvItemQualityChanges | OldRequiresIncomingInspection, OldRequiredCertificateType, OldShelfLifeDays | bit not null default 0, int null, int null |
| InvStockCounts | QualityApprovedAt, QualityApprovedByUserId | datetimeoffset null, int null |
| QaEquipment | CalibrationWaiverRequestReason, CalibrationWaiverRequestedByUserId, CalibrationWaiverRequestedAt | nvarchar(Text) null, int null, datetimeoffset null |
| QaItps | EditorUserIds | nvarchar(Text) null |
| QaItpActivities | NotApplicableLetterId, NotApplicablePending; new index on EvidenceDocumentId | int null, bit not null default 0 |
| QaCapas | Mandatory, EditorUserIds | bit not null default 0, nvarchar(Text) null |
| QaReviewDecisions | DoneRequestedAt, DoneRequestedByUserId, DoneRequestNote | datetimeoffset null, int null, nvarchar(Text) null |
