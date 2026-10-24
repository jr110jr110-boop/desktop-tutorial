# Project control (PMO) module, release 1 — backend brief

Binding decision layer on top of the analysis /home/claude/innco/docs/project-control-spec.md (read sections 4, 5.3 and 6 fully; section 6 is the detailed design). Where they differ, THIS brief wins. ★ = provisional architect decision per the standards; implement as configurable data, not hard-coded.

## Ground rules
- Same as /home/claude/innco/docs/quality-backend-brief.md sections "Ground rules", "CI loop" and the Addendum (read them; they are binding here too). Repository is /home/claude/desktop-tutorial (NOT /home/claude/innco/api, which the spec mentions — that is an older local copy; read code only from desktop-tutorial).
- Table prefix `Pc`, route prefix `api/v1/projectcontrol`, number sequence kinds 421–440, constants class `ProjectControlRoles`.
- The quality module (prefix `Qa`, route `api/v1/quality`) is already in the codebase: reuse its ITP, release note, document control and NCR — never duplicate.
- Legacy entities (ContractEntity, WorkOrder, AssemblyEntity, Bom, ProcessEntity, ContractGuaranty, TenderGuaranty, ProvisionalStatement, Machine, holidays) are extended with nullable columns or 1:1 side tables (prefer side tables for ContractEntity: `PcContractTerms`). Existing screens and the existing progress report must keep working (mirror computed dates back to legacy date fields as spec 6.1 says). Do not delete legacy behaviour; where legacy code completes a process on finance acceptance of a buy order, leave it but make the new progress engine independent of it.
- Fix the legacy scheduling bugs listed in spec 4.3 item 5 only where the new engine replaces them (cycle detection is mandatory on any predecessor link save, including the legacy ones).

## Roles ★
- New: `PROJECTMANAGER` id 15 "مدیر پروژه"; `DCC` id 16 "کنترل مدارک پروژه". (QA id 14 exists from the quality module.) Claims ids 1500+/1600+ (Contract, WorkOrder, Traceability read).
- Planning manager = PLANNING; PMO deputy = PMO; CEO = CEO.

## Decisions on the spec's conflicts and questions ★
1. Progress rules: WI-PL-009 values as configurable defaults (engineering 60/80/100; supply 0/70/100, rework held at 80, reject 0). Supply reaches 100 only on QC-accepted permanent receipt (or receipt without inspection), never on finance acceptance.
2. Release note: the quality module's ReleaseNote is the single record (QC signs). Planning owns the shipping gate (packing start / shipment confirm require Released).
3. PV accrual: linear between baseline start and finish (default); "accrue at end" (WI-PL-009) selectable per contract. AC from `CostEntry`; add nullable `WbsNodeId` to CostEntry. Management reserve excluded from BAC.
4. Manual progress: proposed by the unit manager or PM, approved by PLANNING (not the proposer); unapproved manual values are excluded from EV.
5. SPI/CPI never appear in the client report unless the contract setting `ClientReportShowsIndices` is true (default false).
6. Thresholds: amber < 0.95, red < 0.90, corrective action required < 1.00 (configurable).
7. Data date / weekly snapshot weekday: Wednesday (configurable per contract).
8. Guarantee expiry alerts 30/14/7 days (configurable).
9. Change approval authority by value and change-board composition: configurable table `PcApprovalAuthority` (role, max amount, max delay days); seed: PROJECTMANAGER up to 0 (recommend only), PMO up to 5,000,000,000 rial and 30 days, CEO above. Board default = PM, Planning manager, affected unit managers, QC if quality impact, Commercial if contract impact.
10. Risk scale 1–5 (release 2).
11. Product coding (WI-PL-005): coding tables as data (`PcCodingTable`) so ORC and valve codes can be added later; seed only what WI-PL-005 states; ORC/valve rows left empty with a note.
12. MS Project: PMIS replaces it; provide MS Project XML export of the schedule (read-only) for clients — import not needed.

## Engineering documents — MDR / MDL (company decision, binding)
- `PcEngineeringDeliverable` of the spec becomes the **MDR line** (full internal master document register). Add: `ClientSubmittal` (bool → the line is part of the **MDL**, the client list), `StandardSource` (text, e.g. "API 611 VDDR", "added per standard"), `Discipline`, `DocumentType`, `Required` (bool), client response code (configurable list `PcClientResponseCode`, seed ★: A Approved, B Approved with comments, C Revise and resubmit, D Rejected, I For information), transmittal rows `PcTransmittal` (number, date, direction, lines with document + revision + purpose {ForApproval, ForInformation, AsBuilt}, response code/date, response due = sent + client review days (contract setting, default 14 ★)).
- `PcMdrTemplate` per product type (SteamTurbine, OrcUnit, Condenser, Pump, LubeOilSystem, Valve, Other) with template lines. Seed ★ from the vendor drawing and data requirement lists of API 611 / API 612 (steam turbines), API 610 (pumps), API 614 (lube/seal oil systems) and ASME VIII + HEI (condensers) as far as known to you (typical items: general arrangement/outline drawing, cross-section, P&IDs (lube oil, steam, seal), electrical/instrument schematics and lists, foundation/loading data, rotor dynamics/lateral and torsional analysis, performance curves/data sheets, material list, weld procedures and NDE procedures, ITP, hydro/run test procedures and reports, mechanical run test data, spare parts list, installation/operation/maintenance manual, preservation/packing procedure, certificates (material, conformity), progress reports, final data book). Every seeded line: `StandardSource` = the standard name + "(to be verified against licensed copy)" — do NOT cite clause or annex numbers you are not certain about. Template lines are editable data.
- Per work order the system drafts the MDR from the template on WBS generation; Engineering/DCC edit and approve (gate); MDL = MDR lines with ClientSubmittal.
- Engineering progress (EngineeringIssue method) reads the MDR line state; for MDL lines "Approved" means client code A or B; for internal lines "Approved" means internal approval.
- Final books: extend the quality module's final data book index so `variant=company` lists MDR lines (with their latest revision document) plus quality records, and `variant=client` lists MDL lines plus the client-relevant quality records. Store each generated book as a controlled record in the quality module's document control under the project (`ControlledDocument` type `FinalBook`, linked to contract/work order) — the generator creates a new revision each time it is issued.

## Release 1 scope (build)
Spec phases 3a + 3b plus change control and variation orders:
- 6.2 project/contract terms, project coding, initiation state machine with SLAs, structured charter/PEP (sections as text fields are fine), contract review (ISO 8.2.3) and amendments, payment milestones (+ ProvisionalStatement link), guarantees (typed, events, alerts, one-time parser of the string amount fields with failures flagged), penalty assessment, variation orders.
- 6.3 WBS generation and rules; 6.4 activities, dependencies (FS/SS/FF/SF, lag), calendars, CPM engine (forward/backward pass, floats, critical path, cycle detection, forecasts) and mirror to legacy dates; weekly executive plan list.
- 6.5 baselines with signatures and approval, re-baseline only via approved change request.
- 6.6 progress methods except AssemblyChecklist (release 2) and resources; weekly immutable snapshots (a hosted background service that runs on the server at the configured weekday/time, offline; plus an endpoint PMO can call to take a snapshot now); daily report pre-fill.
- 6.7 cost baseline, PV/EV/AC, CV/SV/CPI/SPI, earned schedule, EAC variants, ETC/VAC/TCPI, cash flow buckets.
- 6.9 change requests with automatic impact scenario, claims register (basic), variation orders.
- 6.11 generated client monthly report, internal weekly and CEO monthly report as JSON endpoints plus stored immutable issues (`PcReportIssue` with hash); MS Project XML export.
- 6.12 integrations listed there (need-by dates on PR drafts, PO delivery dates as forecasts, receipt/QC/NCR-driven supply progress, issue-driven actual start, ITP activities as schedule activities and W-point notice drafts, shipping gate).
- 6.13 shipment record with gate.
- CEO dashboard additions (spec 6.11 last bullet) + alerts.

## Release 2 (NOT now)
Risk register, stakeholders/communication/meetings/letters (letters belong to office automation), resources and capacity/production planning (WI-PL-014), closure and lessons, customer survey, assembly checklist templates (3SY).

## Tests
Extend `.github/scripts/inventory-smoke.sh` with a project-control section (before "roles of the new users"): create contract + work order through the existing APIs (look how the API creates them: ContractController, WorkOrderController, Equipment templates); generate WBS, MDR from template (MDL subset), activities; CPM results checked on a small known network (assert exact dates and critical path); cycle rejected; baseline signatures/approval; progress from a PO receipt (70 temporary/quarantine → 100 after QC accept), NCR rework caps at 80; engineering progress 60/80/100 via MDR state and transmittal codes; manual progress excluded until approved by a different user; snapshot taken and immutable; EV/PV/AC/CPI/SPI numbers asserted on a simple case; change request impact scenario then approval creates baseline 1; guarantee alert listed; payment milestone becomes Due on its trigger; penalty computed; shipping gate blocked without released release note; client report has no indices; MS Project XML export valid XML; CEO dashboard new fields. Assertions must be exact where the math is deterministic.
