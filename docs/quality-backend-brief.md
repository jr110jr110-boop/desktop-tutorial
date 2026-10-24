# Quality module (QC + QA), release 1 — backend brief

Source analysis: /home/claude/innco/docs/quality-module-spec.md (read sections 2, 4, 5 and the appendices). This brief is the decision layer on top of it: where they differ, THIS brief wins. Items marked ★ are provisional decisions taken by the architect per the standards and may be changed later by the company — implement them as configurable data where noted, not hard-coded.

## Ground rules
- Repository: /home/claude/desktop-tutorial (ASP.NET Core on .NET 10, EF Core 10, SQL Server). Code in `src/`. Follow the EXISTING patterns exactly; study first:
  - Entities: `src/Entities/Model/NasbNiro/Inventory/*.cs`, `.../Procurement/ProcurementEntities.cs`, `.../Finance/FinanceEntities.cs` (BaseEntity with Deleted/CreateDate/CreatorUserId…; `[Timestamp] RowVersion`).
  - EF config: `src/DataLayer/Configurations/EF/NasbNiro/Inventory/ProcurementConfigurations.cs` (extension method style; called from `InventoryModelBuilderExtensions.AddInventoryConfigurations`). New tables prefix `Qa` (e.g. `QaItps`). Restrict deletes except child lines (Cascade). Decimal precision always set. String lengths always set.
  - Services: `src/Services/NasbNiro/Procurement/ProcurementService.cs` and `PayablesService.cs` (ResponseDto via `ResponseDtoHelper.Ok/BadRequest/NotFound`, `IInventoryUserContext` for UserId/IsInRole(CSV), `InventoryNumbers.NextNumberAsync(_uow, sequenceKind, prefix, date, ct)` for numbers — pick new sequence kinds 401..420, `InventoryUsers.GetNamesAsync` + `.Name()` for user names, Persian error messages, segregation-of-duties checks "not the creator").
  - Existing quality code to EXTEND, not duplicate: `Services/NasbNiro/Inventory/InventoryQualityService.cs` (incoming QcInspection, NonConformance with approvals/concession/committee/execution/close), entities in `Entities/Model/NasbNiro/Inventory/InventoryWorkflow.cs`.
  - Controllers: `src/WebApi/Controllers/Procurement/ProcurementControllers.cs` style; route prefix `api/v1/quality`. Paged lists return `.ToPaginatedResultSafe()`.
  - DI: `src/IocConfig/AddCustomServicesExtensions.cs`.
  - Roles: `src/Entities/Identity/Constants/RoleConstants.cs` + `PermissionHelper.cs` (seeded with HasData; ConcurrencyStamp fallback is automatic for new ids). Role names in JWT are upper case.
  - Attachments: existing file storage uses `Guid? ...DocumentId` fields (see QcInspection.ReportDocumentId) — reuse that, no new upload API.
- No transactions (EnableRetryOnFailure); one SaveChanges per operation; validate fully before changing anything.
- Every list endpoint paged; every write endpoint returns the saved DTO.
- No secrets, no real people's names. Persian user-facing messages; English code/comments.

## New role
- ★ `QA` id 14, Persian "تضمین کیفیت" (document control, audits, CAPA administration, management review). Claims: Traceability, Warehouse (read). Add to RoleConstants.GetResponseRoles and PermissionHelper (claim ids 1400+).
- Role sets (constants class `QualityRoles`): Inspectors = "QC"; QualityManagers = "QC,QA,PMO"; Engineering = "ENGINEERING,ENGINEER"; Readers = "PMO,CEO,QC,QA,ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER,PLANNING,WAREHOUSE,WHSUPERVISOR,COMMERCIAL".

## Scope of release 1 (build all of these)

### A. ITP — inspection & test plan (WI-QC-004, gap G-08)
- `ItpTemplate` (code, title, productType enum {SteamTurbine, OrcUnit, Condenser, Pump, LubeOilSystem, Valve, Other}, applicableCodes text, revision, isActive) + `ItpTemplateActivity` (seq, group/stage, activity, testType enum (see spec 5.2 + RT, PMI, Hardness, Hydro, RunTest, Performance, Balancing, Painting, Dimensional, VisualWeld, DocumentReview, Packing, Other), acceptanceReference, pointVendor/pointTpi/pointClient enum {None, H, W, R, RA, SW, A}, documentProduced, `requiredReportType` (nullable TestReportType), list of default `parameters` (name, unit, min, max, source) stored as child rows `ItpTemplateParameter`).
- Seed (via a service method `EnsureSeedAsync`, idempotent, called on first GET of templates — NOT HasData) one template "Steam turbine — WI-QC-004" from Appendix A of the spec, and ★ minimal templates for Pump (API 610: hydrostatic test 1.5 × MAWP, hold ≥ 30 min; mechanical run test; NPSH/performance as W), Condenser (ASME VIII-1: hydro per UG-99 1.3 × MAWP × stress ratio, RT/UT of butt welds per UW-51/52, dimensional), Lube oil system (API 614: hydro of piping/vessels, cleanliness/flushing, run test). Mark seeded parameters' `source` with the clause. Run-test vibration and bearing-temperature limits: NO invented values — parameter rows with null limits and source "توسط مهندسی تعیین شود".
- `Itp` per work order (WorkOrderId required, ContractId derived), copied from a template, revision, status {Draft, Submitted, ClientApproved, InUse, Closed}, client approval reference/date, notice days for W points (default 10 Iran / 60 abroad, editable per ITP), `ItpActivity` copies (+ parameters) with status {Planned, Notified, Done, Waived, NotApplicable}, planned date, notification sent date, `ItpPointRelease` rows (party V/TPI/C, action {Witnessed, Reviewed, Approved, Waived}, person/organisation text, date, reference). Rules: H point cannot be Done unless the linked report is Approved AND every party that has H on that activity has a release row; W point: if notification date + notice days passed with no witness, the system allows proceeding and records "witness waived by notice"; ITP edits after ClientApproved create a new revision (keep history).

### B. Inspection records (PR-QC-003, WI-QC-003/009/011/012)
- `QcRecord` (number QIR-, type {InProcess, Final, Dimensional, FirstArticle, ContractorShop, Layout}, workOrderId, itpActivityId nullable, itemId/lotId/serial text, operation/stage text, drawing number + revision, inspector (user), date, result {Accept, Reject, Rework, Hold}, ncrId, attachments, note) + `QcMeasurement` (characteristic, nominal, tolPlus, tolMinus, measured, unit, equipmentId → pass computed server side).
- Rule: every equipmentId used must be status Calibrated and nextDue ≥ record date, otherwise reject with message (ISO 7.1.5).
- Reject result → auto-create NCR via existing NCR service/entity (Source InProcess/Final) and link.
- Self-inspection by operators ★: allowed only for activities not on the ITP (inspectorRole check: ITP-linked records only role QC).

### C. Test reports (spec 5.4, gaps G-02, G-03, G-09, G-10)
- One generic `TestReport` header (number TR-, type enum {PT, MT, UT, RT, Hardness, PMI, Hydro, RunTest, Performance, Balancing, Painting, Dimensional, Other}, workOrderId, itpActivityId, itemId/serial, procedureRef + revision, codeEdition, examiner user, examinerQualificationId (required for PT/MT/UT/RT: valid on report date, method matches, level ≥ II), date, environment text, status {Draft, Submitted, Approved, Rejected}, result computed, approvedBy (QC, not the examiner), witness rows (party, name, date)) + `TestReportValue` rows (parameter, unit, min, max, measured, pass computed; copied from ITP activity parameters when linked) + `TestReportIndication` rows (location, type, size, orientation, accepted) + `TestReportEquipment` (equipmentId; same calibration rule as B).
- Type-specific validation (server side) from Appendix B of the spec — e.g. PT temperature 5–52 °C, dwell, lux ≥ 1000, dark adaptation ≥ 5 min; MT limits; RunTest overspeed trip 110 % ± 2 % (3 trips); Hydro: measured pressure ≥ required (parameter) and hold time ≥ required; Balancing standard ISO 21940-11 ★ (replaces withdrawn ISO 1940-1, same grades). Implement as data-driven: limits come from the value rows; only a few hard rules (equipment calibrated, examiner qualified, at least one value row, all values pass or an NCR is linked).
- Approve: sets linked ITP activity Done when all required reports Approved.

### D. Calibration (WI-QC-001, gap G-05, ISO 10012)
- ★ Form codes: use FO-QC-007…011 (the body of WI-QC-001); record the conflict.
- `MeasuringEquipment` (code INN-IST-nnn auto, name, type/class enum {DimensionalHandTool, Gauge, PressureGauge, TemperatureInstrument, ElectricalMeter, NdtEquipment, TestStandInstrument, Other}, maker, model, serial, location, range, resolution/accuracy, requiresCalibration, status {Calibrated, DueSoon, Overdue, OutOfService, NotRequired}, intervalMonths, lastCalibrated, nextDue, correctionTableDocumentId).
- ★ Default intervals per class (configurable table `CalibrationIntervalDefault`): hand tools 12, gauges 12, pressure gauges 6, temperature 12, electrical 12, NDT equipment 12 (UV/lux meters 12, yoke lift check 12), test-stand instruments 12.
- `CalibrationEvent` (equipment, lab name + accreditation (ISO/IEC 17025) flag, certificate no. + document, traceability statement required, as-found pass/fail, as-left pass/fail, adjusted, date, next due computed). Only role QC.
- Out-of-tolerance: when as-found = fail → create `OutOfToleranceCase` listing automatically every QcRecord/TestReport that used the equipment since its previous good calibration, with a required assessment per affected record (no impact / re-inspect / NCR) before the case can be closed (ISO 7.1.5.2).
- Endpoint for due list (next 30 days / overdue).

### E. Qualified personnel & welding (WI-QC-010, gaps G-01, G-03, G-17; ASME IX)
- `QualifiedPerson` (name, personnel code, employer (own/contractor/provider id), stamp/ID, optional userId).
- `Qualification` (person, kind {Welder, WeldingOperator, NdtPersonnel, Inspector}, method/process (SMAW, GTAW, …, PT/MT/UT/RT/VT), level (NDT: I/II/III ★ scheme ASNT SNT-TC-1A with written practice; ISO 9712 also selectable), scope text (P-no, F-no, thickness, positions), certificate no + document, issued, expiry, status computed).
- `Wps` (number, revision, process, base P-no, filler F-no/A-no, thickness range, positions, preheat, interpass, PWHT, gas, status, approved by QC + Engineering (two different people)) and `Pqr` (number, code edition, test results document, approved); a WPS needs at least one approved PQR.
- `WeldLog` (date, workOrder, item/serial, joint no, WPS, welder qualification, filler batch, preheat/interpass measured, VT result, NDT report ids). Rules: welder qualification must cover the WPS process and be valid; ASME IX QW-322 continuity — if the welder has no WeldLog in that process for > 6 months, the qualification is "Lapsed" and cannot be used until renewed (new test). Continuity endpoint listing welders lapsing within 30 days.

### F. Material certificates & traceability (gap G-11, EN 10204)
- `MaterialCertificate` (lotId, EN 10204 type {T22, T31, T32}, number, issuer, document, chemistry/mechanical summary text, verifiedBy QC, verifiedAt, result).
- Add `RequiredCertificateType` (nullable) to InventoryItem; when the item has it, the existing incoming inspection acceptance (InventoryQualityService.RecordInspectionAsync) must fail unless the lot has a verified certificate of that type or higher (3.2 > 3.1 > 2.2). Keep lot.CertificateNumber/Type in sync.
- Traceability query endpoint: by serial or lot → lot, heat, certificate, receipts, NCRs, test reports, weld logs (with welder), stage history (existing label genealogy).

### G. NCR additions (reuse existing NonConformance)
- Add fields: DetectedStage (reuse Source), DueDate (★ default: Minor 7 days, Major 14 days), RootCauseMethod {FiveWhy, Fishbone, EightD, Other}, CapaId, CostImpact, TimeImpact, CustomerReturnRef, AffectedEquipmentId.
- ★ Use-as-is (Concession) with CustomerApprovalRequired=true cannot be executed without CustomerApprovalReference (already partly there — verify). ★ Major NCR requires committee with ≥ 3 members (QC, Engineering, Manufacturing).
- ★ Official form codes: FO-QC-013 = product NCR; FO-QA-017 = CAPA (FO-QA-023 retired).

### H. CAPA (gap G-06, ISO 10.2)
- `Capa` (number CAPA-, source {Ncr, AuditFinding, ManagementReview, CustomerComplaint, DataAnalysis, Risk, Other} + sourceId, problem, containment, rootCauseMethod + analysis text, owner user, dueDate, status {Open, ActionsDefined, Implemented, EffectivenessCheck, Closed, Cancelled}, effectivenessCriteria, effectivenessDueDate (★ default implemented + 90 days), effectivenessResult, closedBy (role QA/QC manager, NOT the owner)) + `CapaAction` (description, owner, due, doneAt, evidence document).
- Overdue list endpoint.

### I. Release note and final data book (gaps G-12, C-09; ISO 8.6)
- ★ Release authority: QC manager (role QC) signs the release note and certificate of conformity; client/TPI sign-offs are recorded per ITP points.
- `ReleaseNote` (number RN-, workOrderId, items/serials text, status {Draft, Released, Cancelled}, checks snapshot). Release is refused unless: ITP of the work order is ClientApproved/InUse and every H and W activity is Done/Waived/NotApplicable; no open NCR for the work order except Concession-approved ones; every ITP activity with requiredReportType has an Approved report; final data book index has no missing mandatory entry.
- Existing CustomerDelivery documents (InventoryDocumentType.CustomerDelivery) for a work order: confirmation additionally requires a Released release note for that work order (add the check in InventoryDocumentService.CheckBeforeConfirm path; keep the existing QC loading approval).
- `FinalDataBook` index generated on demand (no table needed, or a cached table): sections (ITP + approvals, material certificates, weld logs & WPS/PQR & welder qualifications, NDT reports, dimensional, test reports (hydro, run, balancing, performance), painting, NCR/concession list, calibration certificates of equipment used, release note), each entry with source id, number, status, document id, missing flag.

### J. Document control (ISO 7.5, ISO 15489; gap G-18)
- `ControlledDocument` (code TYPE-DEPT-NNN, type {PR, WI, FO, BL, Drawing, Spec, Other}, department, title, currentRevision, status {Draft, InReview, Approved, Effective, Obsolete}, effectiveDate, retentionClass, responsible unit) + `DocumentRevisionRecord` (revision, changeSummary, file document id, preparedBy, reviewedBy, approvedBy — three different users ★; approved → effective; previous revision → Obsolete automatically) + `DocumentDistribution` (revision, recipient user or unit, sentAt, acknowledgedAt).
- `RetentionRule` (record type, years or permanent) ★ seeded: NCR/CAPA 10y, test & inspection reports and final data book = permanent (product life), calibration 10y, audit & management review 10y, DCF permanent, drawings permanent.
- Master-list endpoint (effective revisions) + duplicate-code check.

### K. Internal audit and management review (ISO 9.2, 9.3; gaps G-15, G-16)
- `AuditProgram` (year, scope) + `Audit` (number AUD-, auditedUnit, criteria (ISO 9001/API Q1 clauses text), plannedDate, leadAuditor user, team, status {Planned, Done, Closed}, report document) — rule: an auditor cannot audit his own unit (compare user's role/unit with audited unit ★ by role name). + `AuditFinding` (clause, type {MajorNC, MinorNC, Observation, Opportunity}, evidence, capaId (required for NC), due).
- `ManagementReview` (date, attendees, `ManagementReviewInput` rows for every ISO 9.3.2 input category (seed list) with text, `ManagementReviewDecision` rows → optional CAPA), minutes document (FO-QA-022).

### L. Customer complaints (ISO 9.1.2, 10.2)
- `CustomerComplaint` (number CC-, contractId/workOrderId, received date, channel, description, severity, responseDue (★ 10 days), status, ncrId, capaId, response text, closedAt).

### M. Quality dashboard endpoint
- `GET api/v1/quality/dashboard`: open NCR by severity/age, overdue CAPA, calibration overdue/due 30d, lapsed/expiring qualifications, ITP H points pending per work order, release notes pending, complaints open. Also add the counts of overdue CAPA, overdue calibration and pending H points to the existing CEO dashboard (`CeoDashboardService` + DTO, plus alerts).

## Release 2 (NOT now)
Risk register/PFMEA, data analysis KPIs, 5S, suggestions, competence matrix, stakeholder register.

## Tests — extend `.github/scripts/inventory-smoke.sh` (append a quality section before "roles of the new users")
Use existing helpers (`call`, `as`, `check`, `val`) and existing users/tokens (QC_TOKEN, ENG_TOKEN, PROD_TOKEN, ADMIN_TOKEN …); create a QA user (role 14). Cover at least: seed templates; ITP from template for a work order (create a contract/work order the same way existing tests do, or reuse one if the script already has one); equipment + calibration; report rejected with overdue equipment; PT report approved by a different QC user (create a second QC user); H point cannot be Done without approved report and releases; welder qualification + WPS/PQR approval by two people + weld log; lapsed continuity (weld log date > 6 months after previous); out-of-tolerance case lists affected report; NCR → CAPA → effectiveness → close by non-owner; release note refused while H pending and accepted after; customer delivery blocked without release note; document revision approve → previous obsolete; audit finding NC requires CAPA; quality dashboard; CEO dashboard new counts. Every check must assert something meaningful.

## CI loop (you own it for this task)
- Branch `innco-net10-build`, remote origin. CI builds on Windows, runs tests, EF model check, then smoke tests on SQL 2022 and 2025, then packaging/install tests. Only one run at a time (`cancel-in-progress`) — never push while your previous run is still needed; nobody else will push during your task.
- Migration flow: put a name (e.g. `QualityModule`) in `.github/migration-name.txt`, commit, push; wait for the result summary on branch `innco-ci-results` (`ci-output/summary.txt` contains the sha; `ci-output/2-build.log` has errors); when build=success it contains `ci-output/migration/*` — copy those files into `src/DataLayer/Migrations/`, set `.github/migration-name.txt` back to `Net10ModelCheck`, commit, push; then wait for smoke results on branches `innco-ci-smoke-2022` and `innco-ci-smoke-2025-express` (`smoke-output/7-inventory.txt`, failures in `7-inventory-fail.txt`; the commit subject contains the sha). If you change entities after the migration was committed, generate a new small migration the same way (never edit committed migrations).
- Waiting: use a Bash command with `run_in_background: true` and an until-loop polling `git fetch` every 30–60 s; do not use long foreground sleeps. Use `gh run list -R jr110jr110-boop/desktop-tutorial --branch innco-net10-build -L 1` to see run state.
- Commit messages end with:
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_0162jHsRTsijMa6de7z2YbQk
- Never print or change secrets in appsettings*.json; never force-push; never touch branches other than innco-net10-build (CI writes the result branches itself).
- Done = build, tests, EF check green and smoke 0 failed on both SQL versions.

## Addendum — company design principles (binding)
- **Humans only at control gates and physical work.** The system drafts everything it can; people check, correct and approve. Concretely:
  - When a work order exists without an ITP, `EnsureItpAsync(workOrderId)` drafts one from the template matching the product type (endpoint + called when a QcRecord/TestReport is first created for that work order).
  - A Reject result on any inspection/test auto-drafts the NCR (already in the brief); a Major NCR auto-drafts a linked CAPA (status Open, owner empty) for QA to assign.
  - Calibration due within 30 days / overdue, welder continuity lapsing within 30 days, CAPA overdue, W-point notice deadlines and complaint response deadlines are computed by the system and appear in the quality dashboard and in the CEO dashboard alerts — no manual lists.
  - The final data book index and release-note checks are computed, never typed.
- **Offline first.** Everything runs on the company's own server with no internet: no external HTTP calls, no CDN, no cloud service in this module.
- **Read-only reports.** Dashboards and reports are computed from records; there is no editable report table.
