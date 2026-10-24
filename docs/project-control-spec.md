# Project Control / PMO module (phase 3) - specification

Status: draft for company review. Scope: planning, project control, contract/commercial control, change, risk, reporting, closure for the steam-turbine / ORC / condenser / pump business. Basis: 17 company documents (procedures and one checklist) and a read-only review of the existing PMIS API (`/home/claude/innco/api`, ASP.NET Core, EF Core, SQL Server) and the quality brief (`/home/claude/innco/docs/quality-backend-brief.md`).

Conventions
- All dates printed in the procedures are Jalali (Persian calendar), e.g. 1402/07/22; they are quoted as printed.
- Names of signatories are deliberately omitted; only job titles are used.
- `[ILLEGIBLE]` = could not be read; `[NOT PROVIDED]` = the document refers to something (form, table, appendix) that is not in the folder; `[AMBIGUOUS]` = text readable but meaning not certain.
- ★ = provisional decision by the architect, to be confirmed by the company; implement as configurable data where noted.
- "Planning" = unit برنامه‌ریزی و کنترل پروژه (role PLANNING, id 11). "PMO deputy" = معاونت PMO (role PMO, id 1, described in code as «مدیریت»). Currency in the procedures is Rial (the text extraction renders "ریال" reversed as "ایر"; amounts below are read as Rial).

---

## 1. Inventory of the procedures

Signature blocks follow one layout: preparer(s) / verifier / approver. Throughout the set the approver is the CEO (مدیر عامل) and the verifier is the management representative (نماینده مدیریت) or, in WI-PL-001, the PMO deputy. Preparers are given per row. Revision = the number in the file code.

| # | Code | Rev | Issue date | Title (fa) | Title (en) | Purpose | Owner / preparer |
|---|------|-----|-----------|------------|-----------|---------|-------|
| 1 | WI-PL-001 | 01 | 1402/07/22 (rev 00: 1398/04/01) | دستورالعمل برنامه ریزی و کنترل پروژه | Project planning and control instruction | Activities of the planning and project control unit and of DCC | Planning manager; preparer: management representative |
| 2 | WI-PL-004 | 06 | 1402/06/05 | دستورالعمل تامین مواد و پیمانکاران دست دوم | Supply of materials and category-2 contractors (title says «دست دوم», WI-PL-001 says «دسته دوم») | Supply of materials and handling of category-2 suppliers (Commercial scope; only planning/control content summarised here) | Planning (header) / Commercial (scope); preparer: PMO deputy |
| 3 | WI-PL-005 | 02 | 1400/02/05 | دستورالعمل کدینگ پروژه | Project coding | One unique code per contract and per project (equipment) | Planning; preparers: PMO deputy, design & manufacturing deputy |
| 4 | WI-PL-006 | 01 | 1400/01/21 (rev 00: 1399/04/20) | دستورالعمل نحوه تهیه سند PMP (PEP) | Project management plan (PMP = PEP) | Execution, control, closure and baselines of a project | Project managers; preparers: PMO consultant, PMO deputy |
| 5 | WI-PL-007 | 01 | 1400/01/21 (rev 00: 1399/04/14) | دستورالعمل نحوه تهیه منشور پروژه | Project charter | Requirements, assumptions, constraints, economic case, risks, stakeholders, PM authority | PMO deputy; as #4 |
| 6 | WI-PL-008 | 00 | 1399/05/01 | دستورالعمل اختتام پروژه | Project closure | Closure of projects incl. lessons learned | Project managers; as #4 |
| 7 | WI-PL-009 | 02 | 1402/07/22 (rev 01: 1400/04/05 [AMBIGUOUS]; rev 00: 1399/04/29) | دستورالعمل برنامه ریزی حرفه ای به روش EPC | Professional planning by the EPC method | WBS, OBS, RBS, CBS, weights, baseline, PMS, earned value | PMO office; preparer: PMO deputy |
| 8 | WI-PL-010 | 00 | 1399/05/01 | دستورالعمل مدیریت ذی نفعان پروژه | Project stakeholder management | Identify, classify, manage stakeholders | Project managers; as #4 |
| 9 | WI-PL-011 | 00 | 1399/05/01 | دستورالعمل مدیریت ریسک پروژه | Project risk management | Risk management plan | Project managers; as #4 |
| 10 | WI-PL-012 | 00 | 1399/05/12 | دستورالعمل برنامه کیفیت پروژه | Project quality plan | Align project activities with goals and stakeholder requirements | Project managers with QA, QC; as #4 |
| 11 | WI-PL-013 | 02 | 1401/05/02 (rev 01: 1399/12/13; rev 00: 1399/06/04) | دستورالعمل کنترل یکپارچه تغییرات پروژه و مدیریت دعاوی | Integrated change control and claims | Control changes and manage claims | Planning; preparer: PMO deputy |
| 12 | WI-PL-014 | 00 | 1402/08/06 | دستورالعمل برنامه ریزی و کنترل تولید | Production planning and control | Plan people and production equipment to guarantee delivery | Planning; preparer: PMO deputy |
| 13 | PR-IP-001 | 02 | 1402/05/04 | روش اجرایی سنجش رضایت مشتریان | Customer satisfaction measurement | Handle customer opinions/complaints; measure satisfaction (ISO 10002) | Commercial manager |
| 14 | WI-IP-002 | 01 | 1402/03/21 (rev 00: 1398/02/15) | دستورالعمل بازنگری قرارداد | Contract review | Capture customer needs, review against capability, amend contracts | Commercial manager |
| 15 | PR-IP-005 | 07 | 1402/11/28 | روش اجرایی جامع بازرگانی داخلی | Comprehensive domestic commercial procedure | Classify commercial activities and interfaces | Domestic commercial; preparer: Commercial expert; verifier also Commercial deputy |
| 16 | WI-IP-005 | 01 | 1402/03/21 (rev 00: 1400/06/16) | دستورالعمل صدور / تمدید / ابطال ضمانتنامه | Guarantee issue / extension / cancellation | Bank/fund guarantees | Administrative manager; Commercial, Administrative, Finance |
| 17 | (no code) | 01 | blank | چک لیست مونتاژی توربین تایپ 3SY | 3SY turbine assembly checklist | Step-by-step assembly record with operator, inspector, QC and manager signatures | Assembly / QC; number and date fields blank |

Standards cited: ISO 9001:2015 everywhere; "PMBOK" (no edition) in WI-PL-004 to 014; ISO 10002:2014 in PR-IP-001.

Document-control anomalies (for QA): page totals in TOC/footers exceed the PDF page count in several documents (WI-PL-001 8 vs 7, WI-PL-005 6 vs 5, WI-PL-006 18 vs 15, WI-PL-007 7 vs 6, WI-PL-008 13 vs 11, WI-PL-013 appendix on p.9 vs 5 pages, WI-PL-014 appendix on p.9 vs 4 pages). The form attachments of WI-PL-013 and WI-PL-014 are therefore `[NOT PROVIDED]`.

---

## 2. Process per procedure

Notation: Role -> action -> record -> limit. "Automation" = the company's current office-automation system (letters/forms).

### 2.1 WI-PL-001 Project planning and control

A. Tender stage
1. Commercial sends count, codes and time needed per tender to Engineering planning so the engineering schedule can be set (Automation).

B. After a contract is won
2. Commercial announces the win with attachments (form: چک لیست ابلاغ پروژه, project announcement checklist) and sends technical documents to Planning.
3. Planning assigns the project code to the contract (per WI-PL-005) and records it in the "projects in progress" database; archive on the PMO share.
4. Planning sends the project announcement form (FO-PL-001) to the other units.
5. Planning sends a letter asking Engineering for the BOM / IFI revision.
6. If technical points must be finalised with the client, the kick-off meeting (K.O.M.) output is obtained from Engineering.
7. BOM (IFI) received from Engineering.
8. Planning is done per WI-PL-009.

C. Purchasing process (planning part)
1. Get BOM/AFC from Engineering. 2. Check warehouse stock in the warehouse software, record in a checklist. 3. Extract BOM shortages. 4. Register goods requests for shortages and contract-award requests for contractor-made items. 5. Purchasing itself follows the purchasing procedure, petty-cash regulation and warehouse instruction.
D. Contractor selection / in-house manufacturing: per the comprehensive domestic commercial procedure (PR-IP-005) and WI-PL-004.

E. Inspection
1. At every ITP stage that needs the client, Planning completes the "Inspection notification" form and sends it to the client (attend or be informed).
2. ITP tests are performed; stages without client attendance: documents are prepared and put in the final book. Stages with client attendance: handover is recorded in minutes with the client representative; minutes are numbered, archived, originals go in the final book.
3. For the release note an inspection notification is also issued by project control. **Packaging cannot start without approval of the release note.**

F. Shipment
1. Client is told to attend final handover (if the ITP says so) and the permit-to-ship minutes are obtained.
2. Warehouse issues the product warehouse receipt (رسید انبار).
3. Release note: prepared by Planning in 2 copies; QC compares items with the product and stamps/signs; the client reviews, stamps and signs; one copy stays in Planning, one with the client. **No shipping permit before client approval of the release note.**
4. Product documents: packing list (3 copies: project control archive, Commercial, client) prepared by Planning; the warehouse matches the exit goods request to the packing list; QC matches packed items to the list and signs; packaging photos archived by QC; Planning chases, via Commercial, the client-signed copy. Marking and shipping documents go inside the packaging. Delivery receipt (رسید تحویل کالا): 2 copies by Planning, one signed by the storekeeper archived, the other travels with the package; client-signed copy chased via Commercial. Spare parts: labels prepared.
5. Waybill (بارنامه) obtained and archived. 6. After shipment Planning writes to Commercial to prepare the progress statement (صورت وضعیت) for the client. 7. Final books collected (documents prepared during the project; QC/Planning).

G. Interface with units: get final drawings from Engineering; get manufacturing instructions and test steps per ITP and send them to manufacturing contractors; get coordinator reports and tell QC and Engineering when in-process tests are due; follow up and collect work reports of all units.

H. Daily / periodic activities
1. Collect unit plans and BOM, set the master project plan (meetings if needed). 2. Issue a weekly or bi-weekly executive plan per unit (Engineering, QC, procurement, manufacturing & assembly). 3. Follow the plan, give progress reports to unit managers, coordinate by priority, follow staff needs. 4. Check daily reports against the executive plan. 5. Discuss reasons for delay or non-performance with the unit. 6. Add daily reports to the master plan file and update project % progress. 7. All units send attachments and daily reports before the end of the same working day (Automation). 8. Weekly management report (Automation). 9. Complete the cost database (DB Cost). 10. Project meetings every two weeks; minutes (MOM) with decisions; follow earlier decisions. 11. Supervise timely execution; derive the delay layer of projects; fill and report from the Status/Position databases. 12. **Send progress reports to the client monthly.** 13. Tell Commercial to prepare a progress statement "after reaching the progress percentage" `[AMBIGUOUS: the percentage is not stated]`.

I. DCC (document control centre): archive and send outgoing letters; receive and archive incoming letters and forward to company e-mail for registration in Automation; management reports on document delays; create archive binders at project start and keep them updated.

Forms/records: FO-PL-001, project announcement checklist, inspection notification, release note, packing list, marking, shipping, delivery receipt, warehouse receipt, daily report, MOM, DB Cost, Status/Position databases, final book.

### 2.2 WI-PL-004 Supply of materials (planning/control content only)
Responsibility table (time limit in working days "d"): 1) finalise MTO - planning expert, PMO deputy approves, 1d; 2) material balance - 1d; 3) purchase request - 2d; 4) contract request for contractors - no time; 5) supplier selection - Commercial, PMO deputy; 6) supplier selection committee (PMO deputy, deputy manufacturing & production, QC, technical & engineering, Commercial) - 1d; 7) contract - Commercial, 1d.
Method: (1) set time priority of materials from the MTO prepared by Engineering and the project schedule; (2) compare MTO to warehouse stock, extract shortage (material balance); (3) issue goods requests to release stock and arrange manufacture of parts from existing material; (4) issue purchase requests for shortages and requests to buy manufacturing services for shortage parts (in the "P" phase of the schedule); (5) Commercial checks the vendor list (VL); within VL: inquiries, Planning/PMO chooses supplier; outside VL: RFQ (completed by Design & Manufacturing), RFP, committee. Selection within 24 h of technical and financial data. Approval bands: up to 500,000,000 Rial: financial = PMO deputy + Commercial manager, technical = deputy design & manufacturing / deputy manufacturing & production / QC; 500,000,000 to 5,000,000,000 Rial: same approvers as the first band (printed identically, see conflicts); above 5,000,000,000 Rial: CEO added to financial approval. Contract mandatory for parts and equipment above 150,000,000 Rial; contract in 2 copies, drafted within 24 h and sent to PMO deputy for completion. Follow-up (8): Planning follows time and quality of deliveries per contract; Commercial asks Planning for delivery status. Contractor payment statements (advance, during work, completion): Commercial attaches the QC quality status scan (فرم تاییدیه کنترل کیفی), the PMO deputy approves within 24 h, then Finance pays; financial signature required on all contractor contracts (rev 03); official invoice obtained where possible.

### 2.3 WI-PL-005 Coding
Contract code, 9 digits: contract year (3) + contract counter (2, restarts at 01 each year) + category (2 letters: MA = manufacturing, SP = spare parts, SE = services) + counter (2, increments only when the same category repeats within the year, otherwise 01). `[AMBIGUOUS: whether the 3-digit year is Jalali, e.g. 402 for 1402]`.
Project code (one per equipment), 9 characters: TYPE (2) + SIZE (3) + OPTION (2) + counter (2; increments only if the same equipment repeats in a contract, else 01).
TYPE: turbine TS/TT/TF/TL/TG/TZ/TB, lube oil LB, gearbox GB, unit control system UC, base plate BP, coupling CP, surface condenser SC, pump PU, generator EG. **No ORC code exists.** SIZE: turbine kW x 10; lube oil l/min; gearbox kW x 10; control system PLC/MMS/GLC combinations (PLC, MMS, GLC, PMS, GPL, PMG); base plate SCM/WGH/ILB; coupling kW x 10; condenser m3/h; pump discharge flange diameter mm; generator kW x 10. OPTION: turbine governor type PM/HM/PB/MB; lube oil drive arrangement EE/SE/TE/TM/EM/SM; gearbox PB/RA; control FS/RD/FR; base plate TB/TG/FT; coupling RT/FL/RP/EP/GT; condenser EE/SE/TE/ST; pump impeller diameter (cm); generator LV/MV. Owner: Planning. Rev 01 removed contract-code parts from the project code.

### 2.4 WI-PL-006 PMP (PEP)
Prepared in the planning phase, approved by management before the project starts; the project manager updates it and tracks changes; audience = all stakeholders. Chapters: overview of project (equipment, duration, cost, client, supervisor, consultant); charter summary and PM authority; integration (change management with a change control board); scope (statement, objectives, high-level requirements, deliverables, constraints, assumptions, in/out of scope, WBS, WBS dictionary); time (mandatory milestones, baseline master schedule used because details are unknown at planning, baseline detailed schedule, schedule control); cost (estimate, budget, cost baseline with overhead, cash flow, control); quality (plan, assurance, control); procurement (make/buy/partner table, long-lead items table: item, lead time, considerations; contract types; contract template; contract approval table: number, party, date, status, amount, approver, follow-up person; control; closure; PM supervises, purchasing per WI-PL-004); communications (reports table: name, description, producer, internal/external, distribution, time/place/access; meetings table; control); risk (roles, identification, register, risk breakdown structure, analysis, response, monitoring); resources (refer to RBS); stakeholders; approval signed by PM and PMO deputy; appendices (references, terms). Appendix A removed in rev 01. All tables are empty templates (the company-specific content is not in the document).

### 2.5 WI-PL-007 Charter
Initiated by the CEO in the initiation phase; prepared by PMO deputy; approved by CEO; **key output: delegating authority to the project manager.** Content (numbered): project overview (equipment and services, client, consultant, supervising body); scope description; high-level requirements table; major deliverables with time span; boundaries (the grey areas that would become disputes with the client); time baseline estimate and mandatory milestones; cost estimate; analysis of alternatives (Build / Buy / Partner), decided by the PM with CEO or delegate approval; assumptions; constraints; risks with initial responses; main external stakeholders only (government and others with significant effect); pre-assigned resources (normally people whose change is not in the PM's hands); selection of the PM and his duties, responsibilities and authority limits with the PM's signed acceptance ("I commit to deliver the scope in the predetermined time and cost"); approval signatures of senior managers (any later change requires their knowledge). Rev 01 removed sections 6-3/6-4.

### 2.6 WI-PL-008 Closure
Closure is mandatory for all projects. The standard level-1 WBS has a Close-Out summary task with three tasks: provisional handover, final handover, closure document. Steps:
1. Obtain approvals/certificates: checklist for provisional and permanent handover, completion certificate. 2. Settle unfinished contracts: physical completion confirmation, approval of final quantities, release of surplus budget, legal/administrative follow-up (settle with main/sub contractors, site clearance report, claims and work stoppages, return client property and classified documents, cost audit of contract, final documents and final statement, approval, payment, contractor performance report, contract closure report). 3. Settle open financial accounts (final statements, surplus budget, contractor clearance confirmation, project financial report). 4. Customer feedback report (satisfaction, strengths/weaknesses). 5. Final report, with baseline vs actual variance analysis in every applicable knowledge area. 6. Archive per document-control procedure. 7. Handover to operation/maintenance team by minutes plus application report where needed. 8. Review reasons for closure (completed, cancelled, other). 9. Close all management procedures: open activities, remaining risks (table in Appendix 2: risk area, solution presented, current state: occurred / resolved / still exists / transferred, result). 10. Technical tests at closure where needed. 11. Release resources, dismantle site.
Lessons learned (Appendix 3 table: row, category [e.g. cost, procurement, human resources], subject, problem/success, impact, recommendation to future projects, submitter name and role); sources: brainstorming, existing documents, engineering judgement; also lessons of earlier projects used and improvements proposed for future projects.
Post-project reviews: scope baseline performance (planned vs actual deliverables with cause of change); schedule baseline (planned vs actual duration; planned vs actual mandatory milestones, causes); cost baseline (planned vs actual cost, cause); resources; risks and consequences; quality; communications; stakeholders; procurement; success criteria vs original agreement.
Appendix 1 is a 21-step handover checklist under the general conditions of contract (شرایط عمومی پیمان, civil-works style, article numbers 22, 34, 35, 39-42, 51, 52): contractor requests provisional handover; consultant approves; employer sets date (max 20 days after consultant approval); as-built drawings; commission; defect list and rectification or provisional handover minutes (start of warranty); site dismantling; contractor's final statement within 1 month; consultant review max 3 months; employer review max 2 months; request for return of performance guarantee (art. 34) and 50 % of retention (art. 35); final adjustments (escalation, currency); employer's final account within 3 months; settlement; final handover after warranty; remaining retention; end.

### 2.7 WI-PL-009 Professional planning (EPC method)
Planning phase (owner/time as printed; "requester / preparer / approver"):
1. New project: Commercial deputy sends contract notice letter + project announcement checklist with full documents to PMO deputy.
2. Portfolio: decided and updated by PMO deputy and CEO.
3. Select project manager: prepared by PMO deputy and Engineering deputy, approved by CEO, 2 d; charter per WI-PL-007 (the text cites "WL-PL-007").
3-1. PEP: prepared by PM and planning unit, approved by PMO deputy, 7 d (per WI-PL-006).
4. KOM with client: PM with deputies of design & manufacturing, QC and Commercial attend; finalises project scope.
5. BOM request: Planning requests; Engineering prepares; deputy design & manufacturing approves; 3 d. Letter asks Engineering for BOM revision IFI to prepare the initial WBS.
6. Planning levels (3): Master Plan (PMO manager; all current contracts; relations and progress at macro level), Detail plan (per contract down to part-collection level, one format for all contracts), Check lists (all detail-plan activities broken down to operations/OPC; same structure, content varies). Planning flows Master -> checklist; updating flows checklist -> Master.
7. WBS in Master Plan (MSP software; level 1 only; per sample MSP file on the PMO share): 1 d.
8. WBS in Detail Plan: 3 d; one MSP file per contract; section **E** engineering deliverables per MDR (external = documents needing client approval, i.e. the whole VPIS list; internal = drawings, instructions, MTO, BOM, client inspections and ITP needed by units, down to turbine part collections); section **P** all supply/manufacture/QC/assembly done outside, grouped by method of production **MOP (10 groups)** `[NOT PROVIDED: group list]`, to part-collection level, without OPC steps; section **C** in-house manufacture, QC and assembly: QC steps = steps of the Inspection Test Plan; assembly steps = assembly and transport.
9. Check lists (2 d): an Excel file per WBS giving each part of a collection its OPC; E, P, C checklists of a project in one file; weight of a collection is divided among parts by importance and manufacturing cost.
10. OBS (1 d): in MSP OBS column or Excel.
11. RBS (1 d): labour (P: contract amount; C: man-hours), equipment (operating hours for supply of items), material (full specification, count, material, dimensions).
12. CBS (1 d): direct (contracts, purchase invoices), indirect (overhead), contingency (identified risks), management reserve (unidentifiable risks, set by management).
13. Relationships between activities (1 d) per the sample MSP file.
14. Weighting (1 d): top-down; expert judgement for upper levels down to part-collection level; below that planning experts use 50% time factor + 50% cost factor; minor activities linear.
15. Jalali date formula. 16. Progress-measurement fields: W.F (weight out of 10,000), Roll Up Weight, Weighted Physical % Complete, Related Tag No (project code per activity), P-or-C flag, Procurement Detail (summary task), Part Collection.
17. Baseline (1 d): after PM approval the schedule is approved by the managers of the related units (signatures under the schedule); then the baseline is created.
18. PMS has three parts: Received (how data comes in), calculations, Send (progress reports).
Monitoring & control (PMS):
19. Weekly: every Wednesday a one-week slice of the Gantt is given to each unit as an executive plan; the unit gives daily log sheets; percentages from log sheets enter the checklists. Engineering progress per MDR: **first issue 60 %, second issue 80 %, approved (client) 100 %**. Procurement: **not delivered 0 %; delivered to company warehouse with temporary receipt 70 %; QC approval and permanent receipt 100 %**. Construction: continuous from the assembly checklist.
20. Send executive plan: Engineering dates into MDR columns; P and C plans to the relevant managers.
21. Reminder of due activities 3 days ahead in unit daily meetings (2 h). 22. Due dates announced in morning meetings (4 h).
23. Units report status in the work-report form and executive-plan form (4 h). 24. For delayed items they add causes, preventive action and corrective action (a recovery plan that does not disturb other activities).
25. Earned value: fixed cost of each activity ("Task Budget"); all cost accrues at end; activities filtered by finish date in monthly buckets to draw the cumulative cash flow; actual cost queried weekly from the project's finance cost centre into AC. PV = planned % x Task Budget; BAC = sum of PV at contract completion; EV = weighted physical % x Task Budget; CV = EV - AC; CPI = EV / AC (cases >1, =1, <1).
26. SV = EV - PV; SPI = EV / PV; the three cases must be stated in the monthly management report. Weekly cumulative actual weighted % entered in an S-curve sheet "Actual Weighted % Complete"; period value = difference with the previous period; planned values in "Plan Weighted % Complete".
27. Entry rules: Procurement services 0/100 % (100 % when documents reach Planning). Procurement supply - contractors: must give a manufacturing schedule per the contractual deadline and periodic reports; QC part-approval report = manufacturing stage approved; part delivered to company warehouse = 70 %, full after QC approval; **if rework is needed it stays at 80 % until approved; if rejected 0 %**. Shelf items (purchase only): 0 % / 100 % at approved warehouse receipt.
28. Send: to the client per the PEP communication procedure; **monthly client report contains only Gantt chart, engineering document status and narrative reasons for delay; other indicators are not given to the client**. Internal reports: weekly format per sample file; periodic reports (daily/weekly/monthly, decided at project start) on time/cost progress and manager performance; sent to all internal stakeholders after PM approval; every Wednesday schedule updated from log sheets and the contract S-curve drawn. **Monthly management report** (prepared by planning expert, approved by planning manager, to CEO): 1) master plan picture and Gantt; 2) delay % of every contract and deadlines; 3) delay % of every project and deadlines; 4) delays split into procurement and construction by type; 5) % progress of each part collection at P and C level per equipment; 6) S-curve per project; 7) analysis of CPI and SPI (the text numbers two items "4").
29. Change management per WI-PL-013. If SPI < 1: meeting with the planning and project management strategic committee; corrective actions with the least plan change. When compensation cannot recover time and the difference exceeds 10 %, with PMO deputy approval the plan is rescheduled within the contractual time allowed by the client; if scope changes, the plan is re-planned, finalised after approval by PM and unit managers, and given to the client if needed.
Appendices: WL-PL-007 (charter), WL-PL-006 (PEP), WI-PL-013 (changes); sample MSP files on the PMO share (`[NOT PROVIDED]`).

### 2.8 WI-PL-010 Stakeholders
Steps: identify stakeholders (natural, legal, government, military, police), record characteristics, interests and ways of influence (Appendix 1 register: row, stakeholder, main expectations, e-mail, contact number, power high/low, interest high/low, communication strategy). Classify with a **power/interest grid**: high-high manage closely and keep satisfied; high power-low interest keep informed enough to satisfy; low power-high interest keep informed and reassured; low-low only monitor. Manage engagement (communication to maximise support, minimise resistance) and control engagement. Appendix 2 is an explanatory non-company example (charity project), no company requirement. Pareto note: ~20 % of stakeholders carry ~80 % of effect.

### 2.9 WI-PL-011 Risk
The project manager owns risk; any team member reports a suspected risk to the PM, who records it in the risk register form `[NOT PROVIDED]` (description, probability, effect on schedule, scope, quality, budget). Processes: plan, identify (iteratively over the lifecycle; prompts: activities without company expertise, tight estimates especially on critical activities, limited resources, many predecessors, long or multi-resource activities; brainstorming, experts, lessons learned), register, risk breakdown structure, qualitative analysis (probability x impact matrix `[NOT PROVIDED: scales]`), quantitative analysis (sensitivity, Monte Carlo: "a later step"), response (threats and opportunities), monitor and control. Sample list of 17 risks (WI-PL-006 has 16: it lacks the "missing or inaccurate site information" item).

### 2.10 WI-PL-012 Project quality plan
Product quality vs project quality. Quality criteria table (item, standard, unit, measurement method, accepted min/max; usually in the contract). Control types: documents, process (e.g. welding at sensitive points), equipment/items (casting, machining dimensions), human processes; a checklist per control item. Steps: study quality needs after the WBS; write a quality manual; execute per manual; audit (e.g. weld tests); immediate corrective action; reuse as template. Control before production (incoming material, drawings), during (materials used, execution rules incl. subassembly and final assembly, workshop drawings) and after (conformity with standards and drawings). Mostly guidance text; no concrete checklists.

### 2.11 WI-PL-013 Integrated change control and claims
Changes from the client (Change Order) or from the project team (Change Request) follow one path. Types: scope/WBS; schedule/priorities; assumptions (exchange rate, inflation); constraints (e.g. client AVL); project team; execution method; standards; client approval conditions; communication management plan and coordination procedure; progress measurement system (PMS). The change management procedure should be signed by both parties at contract start.
1. Identify: anyone who sees a need agrees with his manager and completes the **change request form FO-PO-011** and gives it to project control `[NOT PROVIDED]`. Requests are coded and archived in the PMO "Change Management" folder; all changes are recorded in the changes database and followed up by Planning until implemented and a Log Sheet is received.
2. Analyse and decide: Planning sends the request through Automation to the change control board; analysis covers all effects and is recorded on **FO-PO-015** (analysis and decision; merged form since rev 01) `[NOT PROVIDED]`. The board may invite project team members, consultants and others affected. **Maximum 2 working days from announcement to finished analysis.**
3. Implement: documents of an accepted change go to the affected unit; the planning expert updates scheduling per the approved change and assesses cost, time and resource effect on the main plan, proposing measures to the board to avoid deviation of budget, resources, completion time or critical path.
Claims: a claim = a demand by one party for reasons it considers valid (usually from instructions or changes inconsistent with the contract) that cannot be settled economically; financial claim, time claim or both. A claim agreed by both parties becomes a change order; otherwise negotiation, mediation, arbitration, litigation. At project start the planning expert, with the PMO deputy and planning manager, opens a claims file. Claims are quantified (cost and time, using contract general/special conditions, detailed schedule and CBS) and kept in the file; sent to the client as a letter only with approval of the PMO deputy and the design & manufacturing deputy. All claims must be reviewed at project end before the last progress statement is paid. The company may itself be claimed against; Planning keeps the statistics current but does not raise own claims until they can be used as a cross claim. Rev 02 removed the project-manager responsibilities.

### 2.12 WI-PL-014 Production planning and control
Make-to-order; push (PUSH) for semi-finished parts, pull (PULL) for final products. Inputs: yearly working calendar from the administrative unit; line capacity from product engineering; product BOM. Semi-finished: quantities from the sales forecast form; safety stock; monthly gross need; net need = gross - warehouse stock; split into buckets by capacity; derive material need; check stock before the material exit permit; urgent work order when stock < safety stock; receive approved production worksheet (کاربرگ تولید) and compute plan % progress; get a copy of each production work order before production ends. Final products: take the product order form from Commercial; order tracking list; prioritise by customer and warehouse delivery date; check product stock; analyse plan vs capacity and dates; delivery dates agreed with Commercial; reshuffle for new orders; inform Commercial if delivery slips for lack of capacity; items from product BOM; issue the production work order form with the material exit permission; copy back after production; daily check of product warehouse inputs against orders. Forms (production work order, product order, sales forecast, worksheet, tracking list) `[NOT PROVIDED]`.

### 2.13 PR-IP-001 Customer satisfaction
Opinions/complaints (oral, written, phone, e-mail) recorded by Commercial in **FO-IP-004** (customer opinion form); complaints handled per WI-IP-001 `[NOT PROVIDED]`. Customers identified from the Commercial database. Form sent every **6 months** to every customer who has received products (mandatory). Score: each topic has an importance coefficient; final score = importance x satisfaction; satisfaction % = sum(final scores) / sum(coefficients) `[AMBIGUOUS: exact formula wording damaged]`. **Below 70 %: urgent customers committee**; any parameter below average gets a corrective action. Satisfaction is a quality objective.

### 2.14 WI-IP-002 Contract review
Three headings: (1) review of the contract against customer requirements and company capability; (2) amendment / addendum (متمم); (3) records.
Standard products/services: company gives the customer full specifications; the "sales unit" (واحد فروش = Commercial) checks order completeness and capability to deliver; summarises for the CEO who accepts, rejects or asks for changes; if rejected, customer is told with reasons; if accepted, Commercial drafts contract or pro-forma, which the customer may amend in writing (re-reviewed) or discuss in joint meetings; then the final text. Special-order products (not in the company's process; new design and production plan): as above, with extra checks: customer needs defined in full as basis for planning; **planning unit able to meet the needs**; quality plan for the contract discussed with the customer. Amendments: customer proposal (quantity, time, price) -> sales unit -> CEO decides -> draft amendment approved by CEO and customer; company proposal -> reasons to CEO -> CEO decision -> sales prepares the amendment for the customer. Form: contract addendum form (فرم الحاقیه قرارداد, no code). Records: one set in the sales unit and one in DCC; amendments in 3 copies to customer, sales and Finance.

### 2.15 PR-IP-005 Comprehensive domestic commercial procedure (project-relevant parts)
- Contract-based purchases (13 steps): Commercial takes the approved list from Planning; labels from the warehouse (serials recorded in the contract); manufacturing drawings from Planning (**AFP** stamp for price inquiry; standard items may carry **AFC**); contractor from the vendor list; contract draft to PMO deputy for final approval; signed contract and guarantees received; contract to Finance; registered in the current-contracts database; advance statement approved by PMO deputy; QC approval form needed for the second payment; Planning follows goods at the contractor up to temporary receipt; storekeeper admits goods only with the approved purchase request; Commercial follows final QC approval and permanent receipt; **one invoice per contract at the end, several forbidden**; all recorded in the contractor-accounts database.
- Services by invoice: below 5 million Toman (read 50,000,000 Rial) may be paid from petty cash unless the contractor is a repeat/contract one; above that a statement is required.
- Clients/projects (rows 12-20): PM/control prepares charter, stakeholders, team, plans, budget; Commercial requests guarantees; kick-off meeting through DCC (scope, overall plan, inspections, third-party inspector, ITP, VPIS, contract appendices); scope growth beyond the technical proposal -> financial and time claims notified by Engineering/Planning to Commercial; closure per the closure procedure; handover minutes from project control to Commercial after dispatch; Commercial invoices after handover minutes and client letter confirming the amount; packing list to Commercial after shipment; **weekly guarantee status report (performance, advance, tender) to management**; guarantee cancellation after the warranty period; receivables followed until full collection; **FO-IP-007 daily receivables report to the CEO**; Finance gives monthly project profit-and-loss (cost of goods sold).
- Foreign suppliers: final price inquiries and pro-forma invoice from the requester; customs law and transaction-commission rules; foreign PO approved by CEO or board; production starts after payment confirmation; clearance via a trusted agent.
Forms: FO-IP-040 contract form, FO-AC-019 interim account summary, statement form, QC approval form, contractor-account and current-contract databases.

### 2.16 WI-IP-005 Guarantees
Issue (12 steps): Commercial asks Administrative (with the time needed); Administrative sends request and documents to the issuer (bank or fund), obtains contract and payment terms, CEO signs, Administrative requests cheque and funding (deposit and fee) from Finance, Finance issues cheque and funds, Administrative collects the guarantee, registers and scans it in administrative automation, passes to Commercial and Finance, hands the original to Commercial against receipt; Commercial posts it to the client and updates the guarantee database.
Extension (9 steps; only after the client's extension letter): Administrative sends the client's letter to the issuer, gets the issuer's letter, registers it; Commercial asks Planning about project status, informs Administrative in writing; Administrative asks Finance for CEO permission to pay the extension fee; Finance pays and sends receipt; guarantee database updated.
Cancellation (9 steps): Commercial asks Planning whether the project has finished after shipment; sends letter asking the client to cancel and return the guarantee; follows the client's return to the issuer; informs Administrative; Administrative follows refund of the deposit and cancellation of guarantee cheques; informs Finance; receives cancelled cheques, hands to Finance and informs Commercial; database updated.

### 2.17 3SY assembly checklist
Header: document number `[blank]`, date `[blank]`, Rev 01, client, equipment number, turbine model 3SY, turbine serial number, assembly start date, assembly end date. Columns: row, operation name, operator name, inspector name, operation date, remarks. Signature block at the end of each section page: QC, assembly manager, operator (the pages label QC/QC manager, assembly manager and operator). Sections: 1 rotor sub-assembly (21 steps: cleaning, shrink-fit of lock nut and disc with temperature/time report, blade and shroud assembly, trip ring and pin, thrust collars, pick-up gear, run-out report before balancing (QC), initial balance and final rotor balance (QC), reports attached); 2 casing sub-assembly (7 steps: deburring, lapping of seat surfaces with blue-check, torque of bolts per instruction, bore gauge on gland locations, reports (QC)); 3 bearing sub-assembly (5 steps: visual and UT bond check, blue-check, bore measurement); 4 rotor on casing (18 steps: alignment, cold clearance, back press, rotor float, rotation test); **section 5 is missing in the document (numbering jumps from 4 to 6)** `[AMBIGUOUS: omitted or lost]`; 6 turbine assembly (11 steps); 7 valve sub-assembly (20 steps incl. cold/hot fit, blue-check, stem run-out); 8 over-speed trip sub-assembly (5 steps); 9 other accessories (10 steps; steps 9-6 to 9-10 apply "if hydraulic governor is in the project scope"; Actuator step 7-19 "if in scope"). Torque values, clearances and acceptance limits are referred to "the instruction" and are `[NOT PROVIDED]`.

---

## 3. Forms catalogue

Fields are listed only where the content is visible in the set; otherwise `[NOT PROVIDED]`. "Sig" = signatures.

| Code / name | Used by | Known fields | Sig |
|---|---|---|---|
| FO-PL-001 Project announcement form (فرم اعلام پروژه); project announcement checklist (چک لیست ابلاغ پروژه) | WI-PL-001, 009 | `[NOT PROVIDED]`; checklist carries the attached documents | Commercial / Planning |
| Inspection notification | WI-PL-001 | `[NOT PROVIDED]`; per client inspection stage and for the release note | Planning |
| Release note | WI-PL-001 | items vs product; 2 copies | Planning prepares; QC stamp+sign; client stamp+sign |
| Packing list; marking; shipping docs; delivery receipt (رسید تحویل کالا); warehouse receipt (رسید انبار) | WI-PL-001 | packing list 3 copies (QC stamp at bottom); receipt 2 copies | Planning, QC, storekeeper, client |
| MOM (minutes); daily report; work report and executive-plan forms; MDR; schedule sign-off page | WI-PL-001, 009 | MDR: planned/actual dates and issue status; sign-off page: unit-manager signatures; daily forms: status, delay cause, preventive and corrective action | unit managers; PM |
| Weekly / monthly management report | WI-PL-009 | six items (2.7 step 28) | planning expert, planning manager |
| Charter (منشور پروژه); PMP/PEP | WI-PL-007, 006 | charter: 13 sections (2.5); PMP: 12 chapters with tables (2.4) | PM, senior managers, PMO deputy |
| Stakeholder register | WI-PL-010 App. 1 | row, stakeholder, main expectations, e-mail, phone, power H/L, interest H/L, strategy | - |
| Risk register | WI-PL-011 | description, probability, effect on schedule/scope/quality/budget `[form NOT PROVIDED]` | PM |
| Quality criteria table | WI-PL-012 | item, standard, value, unit, method, min, max | - |
| Risk closure table; lessons-learned table | WI-PL-008 App. 2, 3 | risk area, solution, state (occurred/resolved/still exists/transferred), result; category, subject, problem/success, impact, recommendation, submitter name and role | PM |
| FO-PO-011 change request; FO-PO-015 change analysis and decision (merged in rev 01); Log Sheet; claims file | WI-PL-013 | `[NOT PROVIDED]` | requester and his manager; change control board; implementing unit; PMO deputy + design & manufacturing deputy (claim letters) |
| Production forms (work order, product order, sales forecast, worksheet, tracking list) | WI-PL-014 | `[NOT PROVIDED]` | - |
| FO-IP-004 customer opinion form | PR-IP-001 | topics, importance coefficient, satisfaction score | - |
| Contract addendum form (no code) | WI-IP-002 | description; 3 copies | CEO, customer |
| FO-IP-040 contract form; FO-AC-019 interim account summary; statement form; QC approval form (فرم تاییدیه کنترل کیفی) | PR-IP-005, WI-PL-004 | `[NOT PROVIDED]`; statement stages advance / during work / completion | PMO deputy; QC |
| FO-IP-007 daily receivables report; receivables form; guarantee cancellation report; guarantee database | PR-IP-005, WI-IP-005 | by project: advances and settlements; guarantee fields in section 4 | Commercial |
| 3SY assembly checklist | assembly | see 2.17 | QC, assembly manager, operator |

Form-code conflict: change forms carry prefix `FO-PO` while planning documents use `PL` (5.3).

---

## 4. Existing PMIS code map

Repository `/home/claude/innco/api` (ASP.NET Core, EF Core, SQL Server). Entities `src/Entities/Model/NasbNiro/...`, services `src/Services/NasbNiro/...`.

### 4.1 What exists

| Area | Entity / service | Holds | Notes |
|---|---|---|---|
| Contract | `ContractEntity` (+files) | Code, SubCode, TenderCode, employer/internal order, contract and project numbers; ContractDate, AnnounceDate, StartDate, PayDate, ActualStartDate, TempDate (provisional handover), DeliverDate, GurantyDate (end of warranty), EndDate; Price, Duration, TPA, Site, Type, Title, Employer, SubEmployer, Master, ProjectCharterFileId, Status {Current, Done, Canceled, Stopped}, ForProvider/ForContract, Services | Code is free text (no WI-PL-005 generator); dates non-nullable (0001 when unknown); one Price (no currency, VAT, advance, retention, penalty) |
| Contract lines | `ContractEquipment` | EquipmentId + Count; validates remaining quantity when a work order is created | no price, delivery date, project code |
| Work order (دستور ساخت) | `WorkOrder`, `WorkOrderHistory` | Code, Title, Count, WeightPercent (manual share in contract), StartDate, Condition {Active, Stopped, Done, Canceled}, ContractId, EquipmentId | created by copying an Equipment template (BOM counts x work-order count); stop/resume shifts later dates over the holiday calendar |
| Product template | `Equipment`, `EquipmentAssembly` (tree), `EquipmentBom`, `EquipmentProcess` (+files) | BOM lines (position, title, count, unit, material, standard, dimensions as text, weights, predecessor); routing per BOM line: Type {Buy, Service, Assembly, FinalAssembly, Construction}, commodity codes, DocumentCode, WorkflowId, MachineId, Map/Master/Process durations (integer days), Sort, ProgramPrice, WeightPercent | copied, not referenced, per work order |
| Project instance | `AssemblyEntity`, `Bom`, `ProcessEntity` | assembly status machine (assembler -> QC -> final assembly -> QC -> PMO acceptance -> completed), planned/actual dates, AssemblerDuration, predecessor; process chain (new -> PMO accepted -> engineering workflow -> documents added -> engineer accepted -> master accept -> ready -> buy order -> completed), ProgramStart/End, ActualStart/End, ProgramPrice, WeightPercent | predecessors on Assembly and Bom only; process order = `Sort` |
| Legacy purchasing | `BuyOrder`, `Inquiry`, `BuyOrderProcesses` | inquiry -> technical score -> winner -> finance doc -> temporary receipt -> QC -> engineering -> commercial purchase -> warehouse final acceptance (100) | its finance acceptance still completes process progress |
| Progress | `ProgressReport`, `ReportsService`, `WorkOrderService.CalculateProgressLevelAsync`, SQL functions (`progress_level_queries.sql`) | per-process snapshot rows: planned/actual dates and prices, work-order and contract level progress and weight %, remaining days | see 4.2 |
| Charter | `ProjectCharterController` | attached file only | no structure |
| Guarantees | `ContractGuaranty`, `TenderGuaranty` | Title, Bank, No, RegDate, Price (string), Deposite (string), ChequeNo, SentDate, CancelDate, Extention (string), DueDate | mirrors the Commercial database; CRUD only |
| Statements | `ProvisionalStatement`, `ProStatService` | Number, Date, Price, AcceptedPrice, Type (string), Status (string) | not tied to progress; `AddProStatAsync` builds a BadRequest for a missing contract but does not return it |
| Tender | `Tender`, `TenderFile` | code, PMR number, client contact, dates, results | contract link only by `TenderCode` string |
| Calendar | `HolidaysDayOfWeek`, `HolidaysByDate`, `TransientDateTimeService` | weekly off-days, holiday dates; `GetNextValidDateAsync(date, days)` | one company calendar; no hours, shifts, resource calendars |
| Machine, workflow, kanban | `Machine`, `WorkflowGroup/Station`, `CardBoardService` | machine title and flag; per-user chain for engineering document approval; role-based boards of active work orders | machine has no capacity |
| Built modules | Inventory (`MaterialRequest` with `IssuedQuantity`, `SerialUnit`, `PartOperation`, `QcInspection`, `NonConformance`), Procurement (`PurchaseRequest` source Bom, `PurchaseOrder/Line` with DeliveryDate, ReceivedQuantity, `LatePenaltyPercentPerWeek`, guarantee fields), Finance (`CostEntry` by work order/contract and kind Material/Subcontract/Labour/Overhead/PurchasedService, `CostCenter`), CEO dashboard, quality brief (ITP per work order) | | Planning reads them, does not copy them |

Roles: PMO 1, Commercial 2, Financial 3, Engineering 4, QC 5, WareHouse 6, Assembler 7, Engineer 8, Construction 9, Security 10, Planning 11, WHSupervisor 12, CEO 13; QA 14 planned. No project-manager or DCC role.

### 4.2 What the current progress calculation does
- Process progress is a property `ProgressLevel`: 0 when new, 100 when completed, **50 for any other status** (not stored).
- BOM progress = completed processes / all processes x 100; assembly progress = unweighted average of its BOMs (`WorkOrderService.CalculateAssemblyProgressLevel`); there is a SQL function version (`CalculateBomProgressLevel`).
- A process becomes completed **when the buy order is accepted by finance** (`BuyOrderService.UpdateBuyOrderFinanceAcceptedAsync` calls `process.Completed()`), not at QC acceptance or warehouse receipt. A `ProgressReport` snapshot is written only when the last open process of a BOM completes this way.
- The snapshot computes: planned weight % = sum of `WeightPercent` of processes of the assembly's BOMs whose planned end is already past; actual weight % = sum of weights of completed processes of the BOM; work-order level progress = share of BOMs (count) whose processes all passed their planned/actual end; contract level = share of processes (count); contract weight % = latest per-work-order value x `WorkOrder.WeightPercent`.
- Price: planned = sum of `ProgramPrice` of past-due processes; actual = sum of accepted inquiry (supplier quote) prices of completed buy orders - not cost entries.
- Reports (`/ReportsController`) return the latest snapshot and the series of snapshots (a price series and a weight-percent series, shown as S-curve points).
- Scheduling (`WorkOrderService.SetStartDateAsync`): processes of a BOM are chained end-to-start in `Sort` order using integer working days (Map + Master + Process) over the holiday calendar.

### 4.3 Weak or missing (code-level findings)
1. Three different progress definitions (0/50/100; completed counts; weight sums) and none equals WI-PL-009 (60/80/100 for engineering, 70/100/80/0 for supply, continuous for assembly).
2. Progress is completed by finance acceptance of a buy order, independent of QC, warehouse receipt, ITP, assembly checklist or serial-unit status.
3. "Planned" progress is a step function of today's `ProgramEndDate`; there is **no baseline**. Dates are overwritten by rescheduling and by stop/resume delay shifts, so history is lost.
4. Snapshots are event-driven (end of a BOM) not periodic; no weekly (Wednesday) S-curve, no data date.
5. Bugs: `ContractActualStartDate` is computed with the same expression as the planned date; "ContractRemainingDays" returns days overdue (sign inverted and clamped); `SetStartDateAsync` ignores the predecessor BOM's end when starting a dependent BOM (computed `bomToUpdateStartDate` is unused; all BOMs start at the work-order start date); the "already done" test queries `Boms` with an assembly id; the predecessor queue loops forever if a predecessor never appears (cycle or other work order); no cycle detection on predecessor links.
6. Only finish-to-start; no lag, SS/FF/SF, constraints, float, critical path, calendars per resource, capacity, hours (durations are integer days).
7. `WeightPercent` has no validation (children need not sum to 100); work-order weight in contract is manual.
8. No EV/PV/AC/SPI/CPI/EAC; actual price is not from `CostEntry`; no budget (BAC) structure beyond `ProgramPrice`.
9. Contract: no project coding, review record, terms, penalties, variations, payment milestones; guarantees hold amounts as strings and have no expiry alerts or extension history; provisional statements are strings.
10. No change request, risk, stakeholder, communication, lessons, claims, closure, client-report, meeting or DCC records. Charter is only a file.
11. No link to the new modules: progress ignores warehouse issues, PO receipts, ITP, operations; procurement still driven by legacy `BuyOrder` status.

---

## 5. Gap analysis

Standards used: PMBOK 7 (principles and performance domains: stakeholders, team, development approach and life cycle, planning, project work, delivery, measurement, uncertainty), ISO 21502:2020 (governance, scope, schedule, resources, cost, risk, quality, procurement, change, communication, closure), ANSI/EIA-748 concepts, ISO 9001:2015 8.2.3 and 8.5, contract practice.

### 5.1 Gap table

Priority: H = first release, M = second, L = later.

| # | Area | Requirement (source) | PMIS today | Gap | Pri |
|---|------|-----|-----|-----|---|
| G1 | Governance, initiation | Charter with PM authority, PEP in 7 d, project code, kick-off (WI-PL-005/006/007/009) | charter file; free-text code | structured charter/PEP, code generator, initiation workflow with gates, PM role | H |
| G2 | Contract review | CEO decision, review, amendments in 3 copies (WI-IP-002) | none | ISO 8.2.3 review record, amendment register | H |
| G3 | Scope / WBS | WBS, dictionary, E/P/C structure (WI-PL-006/009) | assembly-BOM-process tree | WBS with E/P/C/Close, dictionary, scope statement | H |
| G4 | Schedule | relationships, calendars (WI-PL-009/014) | FS chain of BOMs, integer days, buggy | CPM with FS/SS/FF/SF, lag, float, critical path, cycle check, calendars, forecasts | H |
| G5 | Baselines | signed baseline; reschedule vs re-plan (WI-PL-009 17, 29) | none | versioned baselines with approval; re-baseline via change control | H |
| G6 | Progress | weights; 60/80/100; 70/100/80/0; continuous assembly | 0/50/100; finance trigger | weighted multi-method progress, automatic from modules, weekly snapshots | H |
| G7 | Earned value | PV/EV/AC, CPI, SPI, BAC, cash flow (WI-PL-009 25-26) | none | CBS baseline, EV from `CostEntry`, EAC/ETC/TCPI, earned schedule | H |
| G8 | Resources, capacity | OBS, RBS, line capacity, push/pull (WI-PL-009/014) | machine title | resource/calendar model, load vs capacity | M |
| G9 | Risk | register, matrix, RBS, contingency | none | register tied to schedule and CBS | M |
| G10 | Change control | FO-PO-011/015, board, 2-day analysis, claims (WI-PL-013) | none | workflow, what-if impact, claims, variation orders | H |
| G11 | Stakeholders, communication | register, plan, meetings, MOM | none | register, plan, action tracking | M |
| G12 | Client progress report | monthly; Gantt, document status, delay reasons (WI-PL-009 28) | none | generator with controlled content; PM release gate | H |
| G13 | Procurement | MTO, balance, requests, follow-up (WI-PL-004) | PR from BOM | need-by dates from CPM, PO dates into forecast, receipt-driven progress | H |
| G14 | Quality | ITP stages as schedule steps, inspection notice, release gate | quality brief | links to schedule/progress, notices, shipping gate | H |
| G15 | Contract management | guarantee lifecycle, payment statements, penalties, variations | string fields | typed guarantees with alerts, milestones, penalties, VOs | H |
| G16 | Shipping | release note, packing list, receipt, waybill | serial status | shipment record | M |
| G17 | Closure, lessons | mandatory closure, variance, lessons (WI-PL-008) | `Done` status | checklist, auto variance, lessons library | M |
| G18 | Customer satisfaction | 6-monthly survey, 70 % (PR-IP-001) | complaints only (quality) | scoring and alert | L |
| G19 | Assembly records | 3SY checklist, QC points | `PartOperation` | multi-signature checklist templates | M |
| G20 | DCC | letters, MDR, delay report | documents by id | MDR/transmittal and letter register | M |
| G21 | Product coding | no ORC code | - | new TYPE codes | L |

### 5.2 Contract-management practice (beyond the procedures)
- ISO 9001 8.2.3: review before commitment (and after changes) of requirements, differences resolved, ability to meet - records retained. WI-IP-002 has no checklist, no timing and no explicit review of delivery date, standards/ITP, guarantees, penalties and payment terms; Planning is only mentioned for special products.
- Guarantees: expiry date, extension dates and release trigger must be tracked; WI-IP-005 and PR-IP-005 ask for a weekly report but not for lead-time alerts; extension requires an inquiry to Planning, but there is no link to actual project dates.
- Payment milestones and progress invoices: PR-IP-005 ties invoicing to handover minutes and client confirmation letter; WI-PL-001 triggers a statement "after a progress %". Needed: milestone table (advance, stages, delivery, retention) with trigger and amount; final posting to finance later.
- Delay penalties: PO has `LatePenaltyPercentPerWeek`; client contracts need the same (rate, cap, grace) and a computed exposure.
- Variation orders: change orders and company amendments (WI-IP-002 6-2, WI-PL-013) need a register of price and time effect.

### 5.3 Conflicts and inconsistencies between procedures
1. **Progress crediting**: WI-PL-009 (60/80/100; 70/100; rework 80; reject 0; continuous assembly) differs from the code (0/50/100; finance acceptance). Decision needed which one the PMIS implements (spec: WI-PL-009).
2. **Release note**: WI-PL-001 has two gates (packaging cannot start without approved release note; no shipping before client approval of it) and says Planning prepares it with QC stamp and client signature; quality brief names the QC manager as release authority. Spec: one release-note record; QC signs; client counter-sign if contract demands; Planning drafts (computed).
3. **Client reporting**: WI-PL-001 says "monthly progress reports to the client" without content; WI-PL-009 limits content to Gantt, engineering documents status and delay reasons. The PMP communication table is empty (template). Spec follows WI-PL-009.
4. **Document codes**: change forms are FO-PO-011/015 and the contractor instruction WI-PO-004 uses PO, while all planning documents use PL; WI-PL-009 cites "WL-PL-006/007" (typo for WI). Department code PO is not explained.
5. **Supplier approval bands** (WI-PL-004): the first two bands (up to 500 million Rial; above 500 million to 5 billion) list identical approvers; the difference is not visible. PR-IP-005 uses Toman (5 million Toman petty-cash limit) while WI-PL-004 uses Rial (150 million Rial contract threshold). Invoice-only services between 50 and 150 million Rial are not covered.
6. **Invoices**: PR-IP-005 allows one invoice per contract at contract end (several forbidden); WI-PL-004 provides payment statements at advance, during work and completion with an invoice; both can hold if "statements" are not "invoices" - confirm.
7. **Change ownership**: WI-PL-006 makes the PM and the change control board the key actors; WI-PL-013 (rev 02) gives responsibility to Planning only and removed the PM's responsibilities. No board membership or authority limits by value are defined.
8. **Planning tool**: WI-PL-009 prescribes MS Project and Excel files on the PMO share; these become the PMIS functions and WI-PL-009 must be revised after go-live.
9. **Reporting rhythm**: WI-PL-001 weekly management report plus bi-weekly project meetings; WI-PL-009 weekly Wednesday update plus monthly report to CEO; daily log sheets by end of the same day (WI-PL-001) vs "daily/weekly/monthly decided at project start" (WI-PL-009). Compatible but undefined which report goes to whom.
10. **Cost accrual** (WI-PL-009 25): all cost accrues at activity end while EV is continuous; AC lags EV so CPI reads high until costs post. Also SPI = EV/PV converges to 1 at completion even for late projects (use earned schedule as a second measure).
11. **Make-to-order vs forecast** (WI-PL-014): "make to order" but semi-finished parts are planned from a sales forecast with safety stock.
12. **Terminology**: WI-IP-002 uses "sales unit"; other documents say Commercial. "دست دوم" vs "دسته دوم".
13. **Appendix 1 of WI-PL-008** is written for projects under the general conditions of contract (civil-works style with article numbers), which may not apply to supply of machinery.
14. **PMBOK edition** not stated; content follows the process-group / knowledge-area model, not PMBOK 7.
15. **WI-PL-006 vs WI-PL-011**: risk examples differ (16 vs 17 items); no probability/impact scale defined in either.
16. **Missing**: no procedure for project start-up meeting content, for client inspection notice timing (notice days appear only in the quality brief), for guarantee alert lead times, for progress-statement trigger percentages, or for escalation when the PMO deputy and CEO disagree.

---

## 6. Proposed data model and rules

### 6.1 Principles
- Extend `ContractEntity`, `WorkOrder`, `AssemblyEntity`, `Bom`, `ProcessEntity`, `EquipmentProcess`, `ContractGuaranty`, `ProvisionalStatement`, `Machine`, holiday tables; add new tables with prefix `Pc` (project control), following the existing patterns in `quality-backend-brief.md` (BaseEntity, `[Timestamp] RowVersion`, decimal precision and string lengths always set, restricted deletes, one SaveChanges per operation, ResponseDto, Persian messages, `NextNumberAsync` sequence kinds ★ 421-440, route prefix `api/v1/projectcontrol`, paged lists).
- The system computes; humans only approve at control gates and do physical work. Dashboards and reports are generated and read-only. Everything runs on the company server with no external call (reports generated as HTML/PDF on the server; Jalali date display, storage as date).
- Legacy fields are kept and kept in step (mirrors): after the CPM runs, `ProcessEntity.ProgramStartDate/EndDate` and `AssemblyEntity.StartDate/EndDate` are written from the current schedule so existing screens (CardBoard, reports) keep working. Existing `ProgressReport` rows stay as history.
- New roles ★: `PROJECTMANAGER` (id 15, مدیر پروژه), `DCC` (id 16, مدارک پروژه). Planning = existing PLANNING; PMO deputy = existing PMO.

### 6.2 Project and contract (extend, then add)
Extend `ContractEntity` (or 1:1 table `PcContractTerms`, chosen to avoid touching the large mapped entity):
- `ProjectCode` (WI-PL-005 contract code, 9 digits, generated: year(3) + counter(2, resets yearly) + category MA/SP/SE + repeat counter), category enum, `CurrencyCode`, `ValueExVat`, `VatPercent`, `AdvancePercent`, `RetentionPercent`, `LatePenaltyPercentPerWeek`, `PenaltyCapPercent`, `GraceDays`, `ChangeProcedureAgreed` (bool, signed copy document id), `ProjectManagerUserId`, `CalendarId`, `CurrentBaselineId`, `ClientReportDayOfMonth`, `DataDateWeekday` (default Wednesday ★).
- Project code per equipment (WI-PL-005 TYPE+SIZE+OPTION+counter): new column on `WorkOrder` (`EquipmentProjectCode`), generated from the equipment's attributes; the TYPE/OPTION tables (6 product types and option lists) stored as data (`PcCodingTable`), not code, so ORC and valves can be added.
- `PcProjectInitiation` (state machine, one per contract): AnnouncementReceived -> CodeAssigned -> UnitsNotified (FO-PL-001 drafted by the system) -> ManagerSelected (CEO gate, SLA 2 d) -> CharterApproved (CEO gate) -> PepApproved (PMO deputy gate, SLA 7 d) -> KomHeld -> BomRequested (SLA 3 d) -> WbsDrafted -> BaselineApproved. SLAs from WI-PL-009; overdue steps appear on the dashboard.
- `PcProjectCharter` (structured, per WI-PL-007 sections 1-13) and `PcProjectPlan` (PEP; structured where machine-usable: scope statement, in/out of scope, constraints, assumptions, milestones, long-lead rules, communication plan items, make/buy rows, approvals; rest as text sections). Existing `ProjectCharterFileId` remains for the signed scan.
- `PcContractReview` (per tender or contract; ISO 9001 8.2.3 checklist: requirements defined, differences resolved, capability (Planning check), delivery dates feasible, standards/ITP agreed, guarantees, penalties, payment terms, currency/exchange; kind {StandardProduct, SpecialOrder}; decision by CEO {Accept, Reject, Modify}; contract addendum link `PcContractAmendment` (number, type {Quantity, Time, Price}, origin {Customer, Company}, CEO approval, customer approval, 3 distribution rows)). Records kept as documents.
- `PcPaymentMilestone` (contract, seq, kind {Advance, Progress, Delivery, Retention, Other}, trigger {Date, ProgressPercent, MilestoneDone, ReleaseNote, ShipmentDelivered, HandoverMinutes, ClientLetter}, percent of value, amount, planned date, status {Planned, Due, StatementIssued, Invoiced, Paid}). Extend `ProvisionalStatement`: `MilestoneId`, `ProgressPercentAtDate`, `Status` as enum, approval fields. The system sets `Due` automatically when the trigger is met; Commercial prepares the statement (human); posting to finance is the later finance module (field `FinanceRef`).
- Guarantees: extend `ContractGuaranty` and `TenderGuaranty` with `GuaranteeType` {Bid, Advance, Performance, Warranty, Other}, `AmountDecimal`, `CurrencyCode`, `IssuerKind` {Bank, Fund}, `ValidFrom`, `ExpiryDate`, `ReleaseTrigger` {FinalDelivery, EndOfWarranty, ClientLetter}, `Status` {Requested, Issued, Active, ExtensionPending, Expired, Released, Cancelled}; new `PcGuaranteeEvent` (Issue / Extension / Cancellation with the step records of WI-IP-005: dates, document ids, fee, who). Old string fields stay; a one-time parser fills decimals, failures flagged. Alerts: 30, 14 and 7 days before `ExpiryDate` ★, and when the contract's warranty end is reached with the guarantee still active ("release due"); weekly guarantee report (PR-IP-005) is a generated read-only list.
- Delay penalties: `PcPenaltyAssessment` (contract, milestone/delivery, due date, actual date, late days minus grace, rate, cap, computed amount) - computed automatically; waiver/negotiation is a human decision with reason.
- Variation orders: `PcVariationOrder` (number VO-, contract, change request id, price delta, time delta, client approval ref/date, amendment id, status) feeding contract value and the re-baseline.

### 6.3 WBS (product -> assemblies -> parts + work packages)
New `PcWbsNode`: contract, parent, code, title, `NodeType` {Project, Phase, Product, Assembly, Part, WorkPackage, Milestone}, `Phase` {Engineering, Procurement, Construction, Testing, Delivery, Closeout}, source links (`WorkOrderId`, `AssemblyId`, `BomId`, `ProcessId` nullable), `Weight` (sums to 100 per parent), `BudgetAmount`, dictionary text, responsible unit (OBS), sort.
Generation (automatic, then human-approved as part of the baseline):
- Level 1 phases per WI-PL-009/008: E (engineering deliverables per MDR: external, internal), P (supply and outside manufacture, grouped by MOP), C (in-house: QC steps from ITP, assembly steps, transport), plus the standard Testing, Delivery and Close-Out (provisional handover, final handover, closure document).
- Under each phase, one node per work order (product, with project code), then per `AssemblyEntity` (tree via PredecessorId/Children), then per `Bom` (part), then work packages from `ProcessEntity` by `ProcessType` (Buy and Service -> P; Assembly, FinalAssembly, Construction -> C; engineering documents from `ProcessFile`/MDR -> E).
- ITP activities (quality module) become C-phase work packages of type Inspection; the 3SY-style assembly checklist steps become sub-activities of assembly packages.
- `PcEngineeringDeliverable` (MDR line): contract, document number, title, category {External (client approval), Internal}, planned dates, `IssueState` {NotIssued, FirstIssue, SecondIssue, Approved}, revision, transmittal number/date, client response code `[codes NOT PROVIDED]`, document id; Engineering/DCC users move the state (physical/administrative work); the system never infers approval.
Rules: R-WBS-1 sibling weights sum to 100 (error otherwise). R-WBS-2 weights default per WI-PL-009: upper levels entered by Planning (expert judgement, human) down to part collections; below that auto = 50 % time factor (duration share) + 50 % cost factor (budget share). R-WBS-3 a work-order quantity change regenerates affected nodes only through a change request after baseline.

### 6.4 Activities, dependencies, calendars (CPM)
- `PcActivity`: contract, `WbsNodeId`, `SourceType` {Process, Assembly, ItpActivity, EngineeringDeliverable, ChecklistStep, PurchaseOrderLine, Manual, Milestone}, `SourceId`, title, `DurationDays` (decimal, working days; process durations Map + Master + Process split into three activities: engineering/map, master approval gate, execution), `CalendarId`, constraint type/date, early/late start-finish, `TotalFloat`, `FreeFloat`, `IsCritical`, actual start/finish, `ProgressMethod`, weight, `BudgetAtCompletion`, status.
- `PcDependency`: predecessor, successor, `Type` {FS, SS, FF, SF}, `LagDays` (negative allowed), `Origin` {BomPredecessor, AssemblyPredecessor, ProcessSort, Procurement, Manual}. Existing predecessor links and `Sort` order are imported as FS, lag 0.
- Calendars: `PcCalendar` (name, working weekdays, hours per day) based on existing `HolidaysDayOfWeek`/`HolidaysByDate` (company calendar, entered yearly by Administrative per WI-PL-014 6-1); `PcCalendarException` for resource-specific calendars (machine, subcontractor).
- Engine: forward and backward pass, calendar-aware; **cycle detection when a link is saved** (replaces the infinite loop); forecast dates use actuals + remaining duration; critical path = zero (or less than a configurable threshold) float; scenario copies for what-if (change requests); result mirrored to legacy dates. Constraints: PO delivery date for supply activities, client-fixed dates for mandatory milestones.
- Planning levels: master plan = level 1 of all contracts; detail plan = per contract to part collection; check lists = operation level (assembly checklist/ITP). Roll-up from check list to master is automatic.
- Executive plan: every Wednesday ★ (configurable weekday) the system cuts a one-week window per unit and per activity type; reminders 3 days before due (WI-PL-009 21) appear on the unit board and morning-meeting list; no manual list-making.

### 6.5 Baselines and re-baselining
- `PcBaseline`: contract, number (0 = original), `Kind` {Master, Detailed, Cost}, status {Draft, ManagersSigned, Approved, Superseded}, reason, `ChangeRequestId` (required for number > 0), created/approved by; `PcBaselineSignature` (unit role, user, signedAt) - **all unit managers listed on the schedule sign (WI-PL-009 17), then PMO deputy approves**; `PcBaselineActivity` (frozen start, finish, duration, weight, budget); `PcBaselineCostPeriod` (period start, planned cumulative cost / PV).
- Rules: R-BL-1 original baseline can be created only when a draft schedule has no cycle, weights sum to 100, every activity has a calendar and budget. R-BL-2 variance is always shown versus the current approved baseline and the original. R-BL-3 **reschedule** (delay > 10 % that compensation cannot recover, within contractual time) needs PMO deputy approval; **re-plan** (scope change) needs PM and unit managers' signatures; both require a change request; the system proposes the new dates (what-if) and the human approves. R-BL-4 no deletion of old baselines. R-BL-5 stop/resume of a work order (existing `ChangeCondition`) creates a draft schedule shift, not a silent overwrite.

### 6.6 Progress measurement (methods and automation)
`ProgressMethod` per activity (stored; chosen from the work-package type by default):

| Method | Rule | Source (automatic) | Human gate |
|---|---|---|---|
| EngineeringIssue | first issue 60 %, second issue 80 %, client-approved 100 % (WI-PL-009 19) ★ configurable per document category | `PcEngineeringDeliverable.IssueState` | Engineering/DCC set issue state |
| SupplyReceipt | not delivered 0; temporary warehouse receipt 70; QC-approved permanent receipt 100; **rework held at 80 until approved; reject 0** (WI-PL-009 19, 27); shelf item 0/100 on approved warehouse receipt; partial quantities pro-rata | Warehouse documents (temporary vs permanent receipt, `QcInspection` accepted, `NonConformance` Rework/Reject); `PurchaseOrderLine.ReceivedQuantity` and `LastReceiptDate`; `MaterialRequestLine.IssuedQuantity` for issued material | QC inspection (existing gate) |
| ServiceDocs | 0 / 100 when documents reach Planning | Procurement `ServiceAcceptance` | acceptance (existing) |
| Contractor | contractor schedule and periodic reports; QC part-approval = manufacturing stage approved; delivery to warehouse 70, final on QC | as SupplyReceipt, plus contractor report records | QC |
| AssemblyChecklist | continuous: sum of weights of completed steps / total; steps from checklist template (e.g. 3SY, with sections 1-9) | checklist step results (see 6.12) | operator and QC sign-offs on steps (physical work and QC gate) |
| ItpPoints | share of ITP activities Done/Waived weighted by point type (H/W/R) | quality module ITP status | QC |
| Quantity | produced / planned quantity | serial unit status (`InProduction`, `InStock`, `Installed`, `Delivered`), `PartOperation` Done | - |
| Milestone | steps with weights (e.g. test run, packing, shipping) | linked events (release note released, shipment delivered, handover minutes) | the gate event |
| Manual | percent entered by unit manager with comment | - | PM or planning manager approves (only method that needs approval) |

Roll-up: `weighted physical %` of a node = sum over children (weight x %). `EV%` of the contract = root value. Weekly snapshot job (Wednesday ★ at a configured time, offline) writes `PcProgressSnapshot` (contract, node or activity id, data date, % complete, planned % (from baseline), AC, EV, PV, forecast finish) - **immutable**, giving the "Actual Weighted % Complete" and "Plan Weighted % Complete" series of WI-PL-009 26 and the S-curve; period % = difference between consecutive snapshots.
Rules: R-PR-1 progress may decrease only through an NCR (reject/rework) or a reversed warehouse/QC document, never by manual edit. R-PR-2 a Manual value without approval is shown in grey and excluded from EV. R-PR-3 overrides need a reason and are listed in the weekly report. R-PR-4 daily log sheets: `PcDailyReport` (unit, date, activity updates, delay reason code, preventive action, corrective action) are generated pre-filled from the day's module events; the unit manager confirms or adds exceptions; delay reasons are required only for activities flagged late by the system; due by end of working day.
Late flags: planned finish < today and progress < 100 %; forecast finish > baseline finish; separated into procurement and construction delays by `Phase`/type (WI-PL-009 28 item 4).

### 6.7 Cost baseline, EV/PV/AC and forecasts
- Cost baseline (CBS): `PcCostBaselineLine` per WBS node and baseline: `CostClass` {Direct, Indirect, Contingency, ManagementReserve}, resource class {Labour, Equipment, Material, Subcontract}, amount, period distribution. Budget of an activity = fixed cost + resource cost. Contingency is linked to risk entries; management reserve is set by CEO/PMO deputy (human) and excluded from EV BAC (kept as a separate line) ★.
- PV (per period): for each baseline activity, planned % at the data date x budget, where planned % follows the baseline dates (linear between start and finish; stepwise for milestone methods) - an alternative to the "accrue at end" rule (conflict 10); the PMIS shows both "accrue-at-end" (as in WI-PL-009) and "linear" and the company chooses ★ (default linear).
- EV = weighted physical % x budget at activity level, summed at any node.
- AC from `CostEntry` (existing): by `ContractId`/`WorkOrderId`, kind; mapped to nodes via the work order, and to activities only when a source document carries a process/buy-order/purchase-order-line link; otherwise AC is held at work-order level and spread by EV share (flagged). A nullable `WbsNodeId` on `CostEntry` ★ is proposed for finance to fill when known. Returns and reversals are already negative amounts.
- Committed cost (not part of AC): open PO line value = (quantity - received) x unit price from the procurement module; used for ETC.
- Metrics per node and contract: BAC, PV, EV, AC, CV = EV - AC, SV = EV - PV, CPI = EV / AC, SPI = EV / PV (WI-PL-009 formulas), plus earned schedule (ES, SPI(t)) because SPI converges to 1 at completion; forecast: EAC default = AC + (BAC - EV) / CPI ★ with alternatives AC + (BAC - EV) and AC + (BAC - EV) / (CPI x SPI) shown; bottom-up EAC = AC + committed + uncommitted remaining estimate; ETC = EAC - AC; VAC = BAC - EAC; TCPI.
- Thresholds ★ (configurable): SPI or CPI < 1.00 triggers "corrective action required" (WI-PL-009 29); amber < 0.95 and red < 0.90 on dashboards. When SPI < 1 the system opens a draft agenda item for the strategic committee meeting and lists the activities causing the variance.
- Cash flow: monthly buckets of PV by finish date (WI-PL-009 25), actual cash-out from `CostEntry`, cash-in from `PcPaymentMilestone` (planned and received), cumulative.
- Legacy `ProgressReport.ActualPrice` (supplier quote totals) is retained as "procurement committed by quotes" only.

### 6.8 Resources and capacity
- `PcResource` (kind {Labour, Machine, Subcontractor, Equipment}) linked to `Machine` (add `CapacityHoursPerDay`, `Efficiency`, `CalendarId`) and `Provider`; `PcResourceAssignment` (activity, resource, hours, cost rate). OBS/RBS views derive from these and `PcWbsNode.ResponsibleUnit`.
- Weekly load per resource from remaining assigned hours; overload flagged, levelling suggested as a draft (PM/Planning accepts). Durations must become hours (open question); until then days x calendar hours.
- Production planning (WI-PL-014): the work-order schedule is the pull plan for final products; for semi-finished parts the system nets gross need against stock and safety stock, splits by capacity and drafts weekly production work orders; material availability is checked before the material exit permission (existing `MaterialRequest`); an urgent work order is drafted when stock falls below safety stock; the daily check of product receipts against orders is a computed list. WI-PL-014 forms are generated from data once supplied.

### 6.9 Change control and claims
- `PcChangeRequest` (number CR-, contract, origin {ClientChangeOrder, InternalRequest}, type per WI-PL-013 list {Scope/WBS, Schedule, Assumption, Constraint (e.g. AVL), Team, Method, Standard, ClientApprovalCondition, CommunicationPlan, MeasurementSystem}, requester, requester's manager agreement (required), description, reason, status {Draft, Registered, InAnalysis, Decided, Implementing, Closed, Rejected}, due date = registration + 2 working days (WI-PL-013 6-1-2-1)). Content of FO-PO-011 `[to be mapped when the form is supplied]`.
- `PcChangeAnalysis` (FO-PO-015 merged form): impact on scope, schedule (days, critical path yes/no), cost, resources, quality/ITP, risk, procurement (open POs), client contract (price/time); recommendation; decision {Approve, Reject, Defer, MoreInfo}; board members and their votes/signatures. **The system drafts the impact automatically**: copies the current schedule/cost to a scenario, applies the requested change, computes delta days, delta cost, affected PO lines and ITP points; humans review and decide.
- Implementation: tasks to units, Log Sheet receipt as completion evidence, automatic update of WBS/activities in the scenario and, on approval, creation of the new baseline (R-BL-3); link to `PcVariationOrder` and `PcContractAmendment`.
- Rules: R-CH-1 changes after baseline only via an approved request. R-CH-2 requests unanswered after 2 working days appear on the dashboard and escalate to PMO deputy. R-CH-3 board = PM, Planning manager, affected unit managers, QC for quality impact, Commercial for contract impact; approval authority by value ★ (open question). R-CH-4 a record of the signed change management procedure per contract (`ChangeProcedureAgreed`).
- Claims: `PcClaim` (number, contract, direction {Against client, Against company}, issue, basis (contract clause), quantified days and amount computed from the schedule and CBS, status {Issue, Quantified, Notified, Negotiating, Settled, Withdrawn, Litigation}, notice letter only after approvals of PMO deputy and the design & manufacturing deputy, cross-claim flag). Opened automatically for every contract at project start (WI-PL-013 6-2-2-1). Closure rule: no final statement can be marked paid while a claim is open without a recorded review (R-CL-1).

### 6.10 Risk register
- `PcRisk`: contract, number, RBS category (internal/external), description, cause, probability and impact scores for schedule, scope, quality, cost (scale ★ 1-5; score = probability x highest impact), kind {Threat, Opportunity}, response {Avoid, Mitigate, Transfer, Accept, Exploit, Enhance, Share}, owner, due date, status {Identified, Analysed, Responding, Occurred, Closed}, residual score, contingency amount (CBS Contingency line), linked activities, NCR/CAPA and lesson ids. Quantitative analysis (sensitivity, Monte Carlo) is a later step, as the procedure says.
- Automatic draft suggestions for the PM, from WI-PL-011 prompts: activities with many predecessors (★ >= 3), long duration (★ > 30 working days), zero-float activities, overloaded resources, long-lead PO lines, activities flagged "no company experience"; seeded library of the sample risks.
- Closure table (WI-PL-008 App. 2) generated from open risks. Rules: R-RK-1 register exists after charter approval; R-RK-2 high-score risks without owner/response go on the dashboard; R-RK-3 contingency use needs PMO deputy, management reserve needs CEO.

### 6.11 Stakeholders, communication, client reports
- `PcStakeholder` (name/organisation, role, representative, expectations, contact, power H/L, interest H/L; strategy computed from the quadrant per WI-PL-010), `PcCommunicationItem` (report/meeting, producer, internal/external, distribution, time/place/access; seeded with client monthly report, weekly internal, monthly CEO report, KOM, bi-weekly meeting, daily unit meeting), `PcMeeting` + `PcActionItem` (decisions followed automatically, WI-PL-001 H10), `PcLetter` (number, direction, date, subject, party, contract, document id, action due) registered by DCC; the "documents delay report" is computed from `PcEngineeringDeliverable` and letters.
- Generated read-only reports, stored immutable as `PcReportIssue` (content hash): (1) **client monthly report** per WI-PL-009 28: Gantt (current plan with baseline bars), engineering document status, delay reasons from daily reports; **no SPI/CPI/cost** unless the contract setting says so; released by PM (gate), sent by DCC as a letter record; (2) internal weekly report; (3) monthly management report to the CEO (six items); (4) inspection notification and release-note/packing-list drafts (6.12-6.13); (5) weekly guarantee list and receivables inputs (FO-IP-007).
- CEO dashboard (existing) gains: contracts with SPI/CPI below thresholds, critical-path delays, overdue change requests, guarantees expiring in 30 days, payment milestones due, penalty exposure. The voice interface reads these endpoints only.

### 6.12 Procurement, warehouse, quality and assembly integration
- Procurement: planning-side steps of WI-PL-004 automated - MTO from the BOM (existing `PurchaseRequest` source Bom); material balance against warehouse stock (computed); shortages drafted as purchase requests with `NeededBy` = late start of the consuming activity minus lead time (new) - Planning approves (human gate); contractor items drafted as contract requests. PO `DeliveryDate` and line `DeliveryDate` become the forecast date of the supply activity; if later than need-by, the activity is flagged and the CPM shows schedule effect. Long-lead register = PR/PO lines whose lead time exceeds a threshold ★ (60 days). Supplier performance (delivery vs promised) feeds the existing supplier evaluation. Contractor payment stages (advance, QC-approved, final) are the procurement module's; Planning provides the QC status per part automatically.
- Warehouse: temporary vs permanent receipts drive supply progress; material issue to a work order (`MaterialRequestLine.IssuedQuantity`) starts the C-phase activities automatically (actual start); serial unit status `Delivered` completes the delivery activity.
- Quality: ITP activities (quality brief) are schedule activities of type Inspection with planned dates; H/W points whose notice (default 10 days Iran, 60 days abroad, from the brief) falls inside the next N days produce draft notices (human sends); NCR Major on a part marks the activity "on hold" and caps its progress per rule; ITP progress counts points. Release note: quality module owns the check; Planning's role is the shipping gate - R-SH-1 packaging activity cannot be started (actual start refused) and R-SH-2 shipment cannot be confirmed unless the work order's release note is Released (and client countersign recorded where the contract requires).
- Assembly checklist: new `AssemblyChecklistTemplate` (code per product model, e.g. 3SY, revision) and `AssemblyChecklistStep` (section, number, text, `Conditional` flag with condition text e.g. "hydraulic governor in scope", `QcPoint`, `RequiredReportType` {Shrink report, Run-out, Balance, Blue-check, Alignment, Cold clearance, Back press, Rotor float, Other}, weight, order). Run-time: `AssemblyChecklistRun` (work order, serial unit, start/end date) and `AssemblyChecklistResult` (step, operator, inspector, date, remarks, report attached, signatures); the three sign-offs (QC, assembly manager, operator) per section are stored. Where the step is an operation on a serial or lot it also creates/links the existing `PartOperation`. The 3SY template seeded from the document: **section 5 absent (confirm)**, numbering 1-9 with 4-18 steps as listed in 2.17; acceptance limits/torque values remain "per instruction" (no invented values).

### 6.13 Shipping and delivery (WI-PL-001)
`PcShipment` (work order, serial units/lots, release note id, packing list lines drafted by the system, QC packing verification with photos, marking/shipping documents, delivery receipt number/date and signed copy, waybill, status {Planned, Packed, Released, Shipped, Delivered, HandoverReceived}). Delivery completes only with receipt and handover minutes; it triggers Delivery payment milestones and a draft statement notice to Commercial.

### 6.14 Closure and lessons learned
- `PcCloseout` (contract; checklist rows from WI-PL-008: approvals/certificates, unfinished contracts settled, open financial accounts settled, customer feedback report, final report, archive, handover to operation/maintenance team, reasons for closure, activities closed, risks closed, technical tests, resources released, claims reviewed). Each row: owner, due date, evidence document, status. Optional template "contract under general conditions of contract" with the 21-step timers of Appendix 1 (deadlines computed from dates entered) - enabled per contract ★.
- Variance report (automatic): planned vs actual deliverables (baseline vs delivered), duration and milestones (baseline vs actual), cost (BAC/EAC/AC), causes pulled from change requests and delay reasons.
- `PcLessonLearned` (contract, category {Cost, Schedule, Procurement, Human resources, Quality, Scope, Contract, Technical, Other}, subject, problem or success, impact, recommendation, submitter role, link to risk/NCR/CAPA/change, product type tags, status {Draft, Approved}). At project creation and at risk identification the system shows approved lessons for the same product type and category.
- `PcCustomerSurvey` (PR-IP-001): customers with delivered products get a form every 6 months (computed due list), topics with importance coefficients, satisfaction %, below 70 % -> alert for the customers committee; the closure survey (WI-PL-008) uses the same form.
- Rules: R-CO-1 contract status Done requires all closure rows complete or waived by PMO deputy with reason; R-CO-2 guarantees must be Released or have a recorded reason before closure; R-CO-3 closure report is generated, not typed.

### 6.15 Automatic versus human approval

| Computed or drafted by the system | Human gate (role) |
|---|---|
| Contract/project codes; FO-PL-001 draft; initiation SLAs and reminders | CEO selects PM and approves charter; PMO deputy approves PEP; Commercial decisions in contract review; CEO accepts/rejects order |
| WBS skeleton from work orders/BOM/processes; default weights; checklist from template | PMO deputy approves top-level weights and WBS; unit managers sign baseline |
| CPM dates, float, critical path, forecasts, calendar-aware shifts | PM/PMO deputy approve reschedule or re-plan |
| Progress % from engineering issue state, warehouse receipts, QC acceptance, ITP, operations, serial status | Engineering/DCC set issue states; QC accepts/rejects; operators and QC sign checklist steps; unit manager confirms daily report exceptions; Manual progress needs PM approval |
| Weekly snapshots, S-curve, PV/EV/AC/CV/SV/CPI/SPI/EAC, cash flow | Threshold meeting on SPI/CPI < 1 (strategic committee) |
| Late/delay flags, delay-reason prompts, 3-day reminders, executive plan | Unit manager gives delay reason and corrective action |
| Change impact scenario (delta days, cost, PO and ITP effects) | Change board decision; PMO deputy and CEO approval by value |
| Guarantee expiry and extension reminders; weekly guarantee list | Administrative and Finance perform issue/extension/cancellation; CEO approves fee payment |
| Payment milestone due status; penalty computation | Commercial prepares statements; PMO deputy approves; Finance pays |
| Risk suggestions, matrix scores, closure table | PM owns and approves risks; PMO deputy/CEO approve contingency/reserve use |
| Client and CEO reports, notice drafts, release-note and packing-list drafts | PM approves release of client report; QC signs release note; client signs receipts |
| Variance report, lessons suggestions, survey due list | PM approves lessons; PMO deputy signs closure |

### 6.16 Phasing (suggested)
- 3a Foundation: roles, contract terms/coding, initiation, WBS, activities, calendars, CPM, baselines, executive plan, daily reports.
- 3b Control: progress methods and snapshots, EV/PV/AC, forecasts, client/management reports, guarantees and milestones, penalties, shipment gate.
- 3c Governance: change/claims/variation, risk, stakeholders/meetings/letters, resources and capacity, closure, lessons, customer survey, assembly checklists.

---

## 7. Open questions for the company

1. Which progress rules apply: WI-PL-009 (60/80/100; 70/100; rework 80; reject 0) or today's system behaviour? Confirm the percentages as configurable defaults.
2. Are client-approval states of engineering documents always first issue / second issue / approved? What are the client response codes?
3. Who approves manual progress (PM, planning manager, PMO deputy)?
4. What are the 10 MOP (method of production) groups?
5. Can the missing forms be supplied: FO-PL-001, FO-PO-011/015, Log Sheet, announcement checklist, inspection notification, production forms, risk register, MDR, contract addendum?
6. What does department code PO mean (FO-PO-011/015, WI-PO-004)? Rename to PL?
7. At what progress percentage or milestone does Planning ask Commercial for a progress statement (WI-PL-001 H13)? What payment stages are typical in client contracts?
8. Are delay-penalty terms (rate, cap, grace) always in client contracts? Typical values?
9. Are guarantee alerts at 30/14/7 days acceptable?
10. Does the 21-step general-conditions closure checklist (WI-PL-008 App. 1) apply to equipment contracts or only to site-installation contracts?
11. Approval authority by contract value for changes and re-baselining? Who sits on the change control board?
12. Is "PMO deputy" the system role PMO? Create project-manager and DCC roles (ids 15, 16 proposed)?
13. May SPI/CPI ever go to the client? Any contract demanding more than Gantt, engineering document status and delay reasons?
14. Probability and impact scales for the risk matrix (1-5 proposed)?
15. Cost accrual: keep "all cost at activity end" (WI-PL-009) or linear PV (proposed)? Is management reserve excluded from BAC?
16. Can Finance tag a `CostEntry` with a WBS node or process, or must actual cost stay at work-order level? Which labour and overhead rates feed AC?
17. Should operation durations be in hours for capacity planning, and where do routing hours come from?
18. What does section 5 of the 3SY checklist contain; document number and issue date? Do checklists exist for other turbines, ORC, condensers, pumps?
19. ORC and valves have no code in WI-PL-005: which TYPE codes? Is the 3-digit contract year Jalali (e.g. 402)?
20. Client inspection notice periods (10 days Iran, 60 abroad in the quality brief): same as in each ITP?
21. Who prepares the release note (Planning per WI-PL-001, QC per the quality brief)? Is client countersignature mandatory for every shipment?
22. Weekly snapshot day: Wednesday for all units, given the company calendar?
23. Should DCC letters be registered in the PMIS or stay on the current automation with imported references?
24. Is the WI-PL-004 approval table intentionally identical for the first two bands (up to 5 billion Rial)?
25. Customer survey: who sends it and who sees results in the PMIS?
