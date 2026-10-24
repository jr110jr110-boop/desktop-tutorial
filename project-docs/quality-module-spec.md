# Quality Module (QC + QA) Specification for PMIS
Source: review of INNCO (Iran Nasb Niroo Co., steam turbines / ORC / condensers / pumps) quality-management documents.
Scope read: PR-QA-001..005, PR-QC-002-04, PR-QC-003-00, WI-QA-001..006, WI-QC-001..014 (28 procedures; 12 WI-QC files exist in two copies, read one and compared).
Output type: specification, not code. Persian is used only for form titles and role titles. No personal names are reproduced.

---

## 0. Reading notes and reliability of the source

1. **Two copies of most WI-QC files** (names ending `..pdf` and `.pdf`). Word-level comparison of the two copies of WI-QC-001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011, 012 found **no substantive difference** (only text-extraction/encoding artefacts; the non-dotted copies embed diagrams as images). WI-QC-013 and WI-QC-014 exist only as `..pdf`. The `..` copies were used as the text base.
2. **Text extraction quality.** The PR-QA, PR-QC and non-dotted WI-QC copies have a font that corrupts Persian digits and drops Latin digits in codes (for example the digit "2" and "5" are swapped, form numbers are lost). For PR-QA-001..005 and PR-QC-002/003 I therefore read form codes and critical numbers from rendered page images (appendix pages, page 1, and key body pages) and from OCR of the Latin codes. Where a number could not be verified it is marked **[unverified]**.
3. **Form templates themselves are not in the reviewed files.** Only procedures were provided. Field lists in section 3 are therefore (a) printed in the procedure (marked *printed*) or (b) inferred from the procedure text (marked *inferred*). No form layout was invented.
4. API Spec Q1 clause numbers used in section 4 follow the clause references given in the brief (10th ed.); they should be confirmed against the licensed copy before the gap list is issued to the company. ISO 9001:2015, ASME IX / VIII-1 / V, EN 10204 and ISO 10012 references are cited at the level of detail I am confident of.
5. Several procedures that the reviewed ones **depend on are not in the set** and could not be read: quality-records control, document control, traceability/identification, corrective & preventive action, internal audit, continual improvement, calibration (the "calibration procedure" cited in PR-QC-002), contract review (WI-IP), customer complaints (WI-IP), customer satisfaction (WI-IP/PR-IP), scrap (WI-WH-004), control-plan documents ("طرح کنترل مواد اولیه"). Gaps that might be closed by those documents are flagged "may exist elsewhere".

---

## 1. Inventory of procedures

Revision/date = as printed on the cover and footer (Jalali calendar). Where file name, cover code and revision table disagree this is noted and listed again in section 4.B.

| Code (as printed) | Rev | Issue date | Title (Persian / English) | Purpose (one line) | Forms used (code - title) | Roles involved |
|---|---|---|---|---|---|---|
| PR-QA-001-00 | 00 | 1397/11/25 | روش اجرایی بازنگری مدیریت / Management review | Define how the QMS is reviewed by top management at least yearly | FO-QA-022 صورتجلسه بازنگری مدیریت; FO-QA-017 فرم شناسایی موارد نامنطبق و اقدامات اصلاحی | نماینده مدیریت, مدیر عامل, واحدهای ذیربط, کمیته بازنگری |
| PR-QA-002-00 | 00 | 1398/05/01 | روش اجرایی ارزیابی ریسک / Risk assessment | Identify and score risks (RPN) per process and per improvement plan | FO-QA-037 لیست کارگروه های شرکت; FO-QA-038 فرم شناسایی و ارزیابی ریسک; FO-QA-020 فرم پایش و اندازه گیری; tables A/B/C (S/O/D scoring, **not included in file**) | مدیر تضمین کیفیت, مدیر عامل, کارگروه فرآیند |
| PR-QA-003-00 | 00 | 1398/05/01 | روش اجرایی مدیریت دانش / Knowledge management | Identify, acquire, develop, distribute, keep, evaluate knowledge (incl. from customers, inspectors, auditors) | FO-QA-039, 040, 041, 042, 043, 044, 045, 046, 047, FO-QA-005, FO-QA-018, FO-QA-023, FO-QA-034, FO-HR-018, FO-TR-015, FO-TR-020, FO-IP-033, FO-IP-037, FO-SA-003; WI-QA-001, WI-IP-001/002 | مدیر QA, کمیته فنی, کمیته R&D, مدیر آموزش, مدیر QC, مدیر بازرگانی |
| PR-QA-004-00 | 00 | 1398/05/01 | روش اجرایی برنامه ریزی استراتژیک / Strategic planning (SWOT) | Annual SWOT-based strategy and objectives | FO-HR-018 صورتجلسه; FO-QA-029 ماتریس ارزیابی عوامل داخلی/خارجی; FO-QA-028 ماتریس تجزیه و تحلیل SWOT | کمیته استراتژیک, مدیر عامل, مدیر QA |
| PR-QA-005-00 | 00 | 1398/05/01 | روش اجرایی الزامات و انتظارات ذینفعان / Interested parties | Identify stakeholders' needs and monitor them | FO-QA-048 جدول الزامات و انتظارات ذینفعان; FO-IP-037 فرم نظرسنجی مشتریان; FO-HR-001 نظرسنجی از رضایت شغلی کارکنان; WI-IP-004 (also cited as PR-IP-004) روش اجرایی رضایت مشتری | کارگروه ذینفعان, مدیر عامل, مدیر QA, واحد HSE, واحد مالی, R&D |
| PR-QC-002-04 | 04 | 1401/05/22 | روش اجرایی کنترل محصول نامنطبق / Control of nonconforming product | Identify, document, evaluate, segregate and disposition NC material/parts/products | FO-QC-013 فرم کنترل محصول نامنطبق; (rev 04 removed the returned-product report form) | بازرس کنترل کیفیت, مدیر کنترل کیفیت, مدیر طراحی و مهندسی, مدیر ساخت و مونتاژ, معاونت طراحی و ساخت, کمیته فنی, انبار, کنترل پروژه |
| PR-QC-003-00 | 01 (footer; code says 00) | 1398/02/16 | روش اجرایی پایش و اندازه گیری کیفیت محصول / Product monitoring and measurement | Incoming, in-process, final inspection, periodic layout inspection and records | Legacy codes: 03FM057 فرم بررسی کیفی نمونه اولیه; 03FM058 فرم ترخیص اضطراری; 03FM059 Layout Inspection; plus un-coded: فرم بازرسی مواد خام, فرم رسید انبار, Manufacturing Process Sheet, فرم کنترل محصول نهایی (Test Report), فرم ثبت نتایج آزمونها و تستهای آزمایشگاهی, مجوز خروج کالا; refs 01PR02 (quality records), 03PR05 (NC product) | مدیر کنترل کیفیت, انباردار, مدیر تولید, مسئول آزمایشگاه, مدیر برنامه ریزی |
| WI-QA-001-00 | 00 | 1398/02/07 | دستورالعمل نظام پیشنهادها / Suggestion system | Employee suggestion intake, evaluation, reward | FO-QA-031 فرم اجرای پیشنهاد; 032 فرم اعلام نتیجه پیشنهاد; 033 فرم پرداخت پاداش پیشنهاد; 034 فرم ارائه پیشنهاد; 035 فرم ارزیابی پیشنهاد | کمیته عالی/فرعی نظام پیشنهادها, دبیر, دبیرخانه, کارشناسان, مدیر عامل |
| WI-QA-002-00 | 00 (history shows rev 01) | 1398/05/10 | دستورالعمل 5S / 5S | Housekeeping system and its audit | FO-QA-049-00 چک لیست 5S | مسئول 5S, مدیر تضمین کیفیت (secretary of 5S committee), مسئول کنترل مدارک و آموزش (auditor) |
| WI-QA-003-01 | 01 | 1398/05/10 | دستورالعمل نظام انگیزشی کارکنان / Employee motivation | Six-monthly satisfaction survey and suggestions | FO-HR-001 فرم نظرسنجی از رضایت شغلی کارکنان; WI-QA-001 | مدیر QA, مدیر اداری, مدیران واحدها, مدیر عامل |
| WI-QA-004-00 | 00 | 1398/05/10 | دستورالعمل کمیته ها / Committees | Rules for running committees, minutes, quorum | FO-HR-018 صورتجلسه; FO-QA-051 جدول کمیته ها | دبیر کمیته, مدیر عامل |
| WI-QA-005-01 | 01 (cover code reads **WI-EN-005-00**) | 1397/10/01 | دستورالعمل FMEA / PFMEA | Process FMEA with S/O/D rating tables | Appendix lists legacy codes 03PR01 روش اجرایی تکوین کیفیت محصول, 03FM034 فرم FMEA, 03FM012 فرم FMEA (طراحی) | مدیر فنی (owner), مدیر QC, مدیر تولید, مدیر QA (team members) |
| WI-QA-006-00 | 00 | 1402/08/06 | دستورالعمل تجزیه و تحلیل داده ها / Data analysis | Six-monthly data collection, analysis (Pareto), decisions | none coded (a "data needed for analysis" table is embedded as an image) | نماینده مدیریت, مدیر عامل, ممیزین, مسئولین پایش, تدارکات |
| WI-QC-001-02 | 02 | 1400/02/15 | دستورالعمل کالیبراسیون / Calibration | Control of measuring equipment calibration, labelling, out-of-tolerance reaction | Body: FO-QC-007, 008, 009, 010, 011; appendix: FO-QA-007..011 (**mismatch**) | مسئول کالیبراسیون, مدیر QC, بازرگانی, واحدها |
| WI-QC-002-01 | 01 | 1399/01/20 | دستورالعمل PT / Liquid penetrant testing | PT method per ASME V Art.6 / ASTM E-165 | FO-QC-016 LIQUID PENETRANT REPORT | شخص دارای مدرک Level II, مدیر کنترل کیفیت |
| WI-QC-003-02 | 02 | 1400/02/15 | دستورالعمل کنترل ابعادی / Dimensional control | Dimensional inspection against AFC/AFP-stamped drawings | FO-QC-14 (Dimensional Control Form, DCF) | مدیر QC, مدیر مهندسی, انبار |
| WI-QC-004-01 | 01 | 1397/10/01 | دستورالعمل QCP / Quality control plan (generic ITP) | Inspection plan with H/W/R/SW/A points for turbine parts and delivery | none coded (the QCP is itself the template) | Vendor (INNCO), TPI (GL), Client (NIOEC) |
| WI-QC-005-02 | 02 | 1400/02/15 | دستورالعمل UT / Ultrasonic testing | UT method statement | none | Level II, مدیر کنترل کیفیت |
| WI-QC-006-01 | 01 | 1397/10/01 | دستورالعمل روش انجام تست عملکردی مکانیکی / Mechanical run test | No-load mechanical running test, governor and overspeed trip setting (API 611 6.3.3) | none | مدیر QC or delegate |
| WI-QC-007-01 | 01 | 1397/10/01 | دستورالعمل Performance Test | Simple ASME-based steam-flow/enthalpy-drop performance test | Data sheet printed in the WI (no code) | مدیر QC, نماینده کارفرما |
| WI-QC-008-00 | 00 (history rev 01) | 1399/01/20 | دستورالعمل MT / Magnetic particle testing | MT (yoke, continuous, visible/fluorescent) | FO-QC-017 MAGNETIC PARTICLE TEST | Level II (ASNT SNT-TC-1A), مدیر QC |
| WI-QC-009-00 | 00 | 1399/01/20 | دستورالعمل بررسی نمونه های اولیه و مشکوک / First-article and suspect samples | Check first samples and changed-material samples from suppliers | none (results kept in a binder) | تدارکات, انباردار, کنترل کیفیت |
| WI-QC-010-00 | 00 | 1400/08/01 | دستورالعمل کنترل کیفیت فرآیند جوشکاری / Welding process quality control | Catalogue of weld defects and causes; ensure WPS/WQT | "فرم کنترل کیفیت قطعات جوشکاری شده" (no code) | مدیر QC or delegate |
| WI-QC-011-00 | 00 (cover code prints **WI-QC-000-00**; file name says "Casting Control", title says shell dimensional control) | 1400/08/01 | دستورالعمل کنترل ابعاد پوسته / Casing (shell) dimensional control | Item list for dimensional/geometric checks of Inlet, Exhaust, Governor housing, Exhaust support, Cover (platforms TY, BSY, 3SY) | "فرم ثبت کنترل کیفیت CASTING" (no code) | فرد کنترل کننده قطعه |
| WI-QC-012-00 | 00 | 1401/06/27 | دستورالعمل کنترل کیفیت قطعات در محل / In-place QC of parts (outsourced manufacturing) | QC of parts made in-house stage by stage and at contractor premises | FO-QC-018-00 فرم صدور حکم بازرسی; FO-WH-001 فرم رسید کالا; FO-WH-007 فرم رسید موقت کالا; FO-WH-002 فرم حواله خروج کالا; WI-WH-004 دستورالعمل ضایعات | واحد ساخت, انباردار, کارشناس کنترل کیفیت, واحد PMO, مدیر ساخت |
| WI-QC-013-00 | 00 | 1402/04/03 | دستور العمل جامع واحد کنترل کیفیت / Comprehensive QC unit instruction | QC unit activities, workflow, interfaces, archiving | none | کارشناسان QC, انبار, تولید, مونتاژ, مهندسی, برنامه ریزی |
| WI-QC-014 (cover code **WI-WH-001-04**) | 04 | 1402/04/10 | دستورالعمل جامع انبار / Comprehensive warehouse instruction | Receipt, quarantine, QC status, storage, issue, supplier return, security | System "Rai Dana" (ERP/warehouse software, "نرم افزار رای دانا"); آیین نامه تنخواه گردانی BL-007-07 | سرپرست انبار, برنامه ریزی و کنترل پروژه, بازرگانی, QC, انتظامات |

**Document control block seen on every procedure (needed for the document-control module):** code; issue date; responsible unit (مسئولیت اجرا); distribution list of copies (توزیع نسخ); prepared by / approved 1 / approved 2 / authorised by (role, name, date-and-signature); validity stamp "مهر اعتبار دارد" (and "مستندات تحت کنترل" stamp on a scanned original); table of contents; revision log (row, page revised, previous revision no./date, new revision no./date, summary); running footer (department, revision date, revision number, page x of y).

---

## 2. Process descriptions

Notation: **[Rec]** record produced, **[Appr]** approval, **[Hold]** hold point, **[Time]** limit, **[Ret]** retention, **[No.]** numbering rule.

### 2.1 PR-QC-002-04 Nonconforming product (core QC/NCR process)

Definitions: NC product = does not meet defined requirements. Suspect/unidentifiable product = no confidence in approval, classified as "status not determined" (يادآوري 2). **Major** NC = cannot be corrected, or correction costs heavy money/time, severity not forgivable, permanent effect on the part, may or may not affect final turbine performance. **Minor** NC = technically low weight, correction or rejection cheap in cost and time, mostly reworkable.

**A. Incoming material (6.1)**
1. Every incoming consignment is inspected by the QC inspector against the temporary general receipt (رسید موقت عمومی) issued by Warehouse. [Rec: temporary receipt with QC status]
2. If NC is found, the consignment is rejected at first stage; after the QC manager confirms the rejection he informs the manager of design & engineering. QC manager classifies the NC as Major or Minor.
3. Minor: no technical committee; QC, Engineering and Manufacturing/Assembly managers may close the NC form with **stamp and signature**. [Appr] 3 managers. Disposition choices on the form: **approve (accept), rework (اصلاحی), reject (مردود)** (6.1.1).
4. Major: referred to the **Technical Committee (کمیته فنی)** (or Deputy of Design & Manufacturing). Committee decides disposition first, then approves a corrective action to prevent recurrence, names responsible person and collects signatures; the item is closed at committee level. QC manager follows up execution, effectiveness, and reports to top management.
5. QC and Engineering managers must state the **root cause** on the NC form in one of six categories: **manpower, material, methods, equipment, drawing, environment** (حالت نیروی انسانی، متریال، روش ها، تجهیزات، نقشه، محیط), and also record the cause "from personal viewpoint" in a separate field. Engineering manager describes corrective and preventive action to avoid recurrence.
6. After QC decision:
   - Approved: parts released to Warehouse by signing the temporary receipt, wait for assembly.
   - Rework: parts are **quarantined in the QC workshop**, Manufacturing and Project Control are informed, Project Control arranges rework with the contractor, then parts return to QC for re-verification of the corrective action.
   - Rejected: if INNCO owns the part -> to the scrap store; warehouse decides final disposition; Warehouse may not hand rejected parts to Manufacturing/Assembly; to reuse them, coordination and approval from QC and Deputy of Design & Manufacturing is needed. If a contractor owns the part -> Warehouse returns it to the contractor.

**B. In-process (6.2)**
1. QC inspector sees NC during fabrication/assembly -> tells QC manager -> QC manager decides type -> NC form completed and referred to Engineering manager -> Engineering decides on the rework method (based on control plans) and passes it to Manufacturing/Assembly.
2. QC inspector re-inspects after rework; if the defect persists the NC form is completed again (re-issue).
3. Note 1: if the defect is not removed, Manufacturing may continue **only under a concession (مجوز ارفاقی) issued by the Deputy of Design & Manufacturing**, requested by Manufacturing through QC; the concession may be issued at the start of disposition or after rework.
4. Note 2: unidentifiable/suspect product is "status not determined".
5. Note 3: inspection tools must be calibrated per the calibration procedure; the inspector reports tool defects **in writing** to the QC manager who arranges calibration.
6. After rework the in-process inspector re-inspects and releases if within acceptance range.

**C. Final product (6.3)**
1. If no rework is possible, the NC report goes to the Deputy of Design & Manufacturing. If the Deputy, in coordination with the senior manager, decides to ship, it is possible **only by a written concession (مجوز ارفاقی)** from the Deputy.
2. Remark 1: NC found by other units during fabrication is reported by that unit's manager to the QC manager; confirmed by QC manager (or in-process inspector); NC form completed; Warehouse informed; part placed at the NC location.
3. Remark 2: for **customer-returned products** a QC report is prepared (but the revision history says the returned-product report form was deleted in rev 04 -> no form).

**Records / retention / numbering:** only FO-QC-013 is named. No numbering rule, no timing limit for decision, no retention period stated. Distribution of the procedure: QA, QC, Warehouse, Design & Engineering.

### 2.2 PR-QC-003 Product monitoring and measurement

1. **Pre-purchase samples (6.1.1):** Supply unit sends sample + "فرم بررسی کیفیت نمونه ها" to QC. QC manager inspects against the **raw material control plan (طرح کنترل مواد اولیه)**, records in inspection forms, writes accept/reject on the sample review form and informs Supply. [Rec: form 03FM057 legacy code]
2. **Incoming inspection (6.1.2):**
   - Warehouse keeper puts the consignment in **quarantine**, issues the warehouse receipt and sends it to QC manager.
   - QC samples per control plan, inspects per "برنامه کنترل مواد و قطعات", records in "فرم بازرسی مواد خام".
   - QC manager enters the final result in the warehouse receipt and returns it to the keeper.
   - If source inspection was done at the supplier, approved lots get a **company QC sticker**; results and place of inspection recorded; on arrival QC verifies stickers and visual damage.
   - **Emergency release (ترخیص اضطراری):** if production urgency or long tests require it, QC manager must obtain permission from the **Planning manager** on the emergency release form (03FM058) and send a copy to the Production manager.
3. **In-process (6.2.1):** at start, Production manager sets up the process per **Manufacturing Process Sheet** (MPS); **operators** carry out in-line control; QC controls the product at end of line; all in-process inspections recorded on the related form.
4. **Final inspection (6.2.2):** per batch, the **laboratory in-charge** inspects per control plan and product standard, performs all customer-required tests, records on "فرم کنترل محصول نهایی (Test Report)". Failures follow the NC procedure. When delivering product to Warehouse the QC manager confirms all tests/inspections were done and signs the MPS.
5. **Layout Inspection (6.3):** comprehensive functional control per form 03FM059 per standard and customer requirements, **yearly**; results reviewed; corrective/preventive action as needed; results and periods recorded on "فرم ثبت نتایج آزمونها و تستهای آزمایشگاهی".
6. **Records (6.4):** kept per quality-records procedure (01PR02); must be retrievable by **production code** per traceability procedure and show accept/reject against acceptance criteria. After verifying completeness per quality plan the QC manager issues the **release permit** by signing **"مجوز خروج کالا"** for the product warehouse to ship to customers.
Not stated: sampling plan content, acceptance levels, retention years, numbering.

### 2.3 WI-QC-001-02 Calibration

Responsibility: calibration unit under QC manager: monitor timing of calibration of all units' instruments per schedule; set calibration intervals; issue certificates; verify availability of standards/methods; verify suitability of equipment and conditions; install/replace labels; send equipment to calibration companies.
Steps (as in the table):
1. Calibration officer distributes the units' equipment list (FO-QC-008 in body) and gets data.
2. Distributes the "equipment needing calibration" form (FO-QC-009) and gets data.
3. QC manager compiles complete list in the **calibration control form for devices and equipment** (FO-QC-007).
4. QC manager codes all equipment; code pattern **INN-IST-nnn** (example "caliper, controller QC" = INN-IST-009).
4-1. Calibration period is determined from: manufacturer recommendation, previous measurement records, repair records, importance/extent of use, environmental conditions (temperature, humidity, dust).
5. Commercial unit selects calibration reference (lab) -> "evaluation of suppliers" record.
6. Commercial prepares a calibration request letter to the lab.
7. Calibration done by sending a lab expert to the factory or sending the equipment to the lab.
8. QC manager receives the equipment and **calibration certificate**.
9. Calibration label prepared (label shows: equipment name, calibration code, calibration time, expiry time).
10. QC manager installs the label near/on the instrument; 10-1 if correction table exists the table or the values are posted beside the device.
11. Equipment handed over to units on the "calibrated equipment hand-over form" (FO-QC-010).
12. If a user reports a measuring error, or at "monthly control", QC unit prepares an **NC form for products** in which that device had influence in production or assembly.
13. Instrument is re-calibrated.
14. Equipment that needs no calibration is labelled **NC (No Calibration)**.
15. Equipment due and waiting is labelled **WC (Waiting for Calibration)**.
16. Only the calibration officer may fit or remove labels.
17. Calibration officer prepares the **equipment identity file** (FO-QC-011) for all calibrated equipment.
Retention of calibration records: not stated. Intervals: no table; fixed numbers only appear in other WIs (light meters max 1 year in WI-QC-002; yoke lifting force check yearly and Lux meter yearly in WI-QC-008).

### 2.4 WI-QC-002-01 Liquid penetrant (PT)

- Person: trained, **Level II** certified; approval by QC manager or delegate. References ASME V Art.6, ASTM E-165; acceptance per ASME V 2013 Art.6 and ASME VIII-1, and API 687 table 1.8.1 (also cited as acceptable).
- Equipment: cleaner, penetrant, developer, visible-light meter, UV (black light) meter and source, thermometer.
- Surface preparation: clean of oil/grease/water; sandblast allowed if pores not covered; dry before test.
- **Limits:** part/penetrant temperature **5 to 52 °C**; dwell "maximum ten minutes" (as printed); wash water max **43 °C**, pressure max **50 psi**; interpretation window **10-60 min** after developer; white light min **1000 lux**; UV intensity min **100** (units not printed; 100 µW/cm2 is typical); dark adaptation min **5 min**; light meters calibrated at most **1 year**.
- Reporting: form **FO-QC-016 LIQUID PENETRANT REPORT**; all indications (rejected and not), type (linear/round), location and size (length/diameter/orientation) marked on drawing or photo.
- A table lists "essential / non-essential" variables (16 items: penetrant family, surface prep, application method, removal method, emulsifier concentration/time, developer method, dry times, dwell reduction, developer time increase, light level, temperature range, performance demonstration, personnel qualification, part form/size/extent, post-cleaning).
- Not stated: who signs the report, retention, numbering, comparator/reference-block use, procedure qualification.

### 2.5 WI-QC-003-02 Dimensional control

1. Latest drawing version per the warehouse temporary receipt list is taken from the project-code folder, stamped **AFC** (Approved For Construction) or **AFP** by the Engineering unit manager; control parameters are defined.
2. The **DCF** (Dimensional Control Form, FO-QC-14) is prepared according to work stage and drawing requirements; inspection is done.
3. NC -> follow the NC procedure.
Table "Reference documents, distribution, retention": Inspection Drawing (code = Drawing Code): distribution Design, QC, Archive, retention **permanent (دائمی)**; FO-QC-14 Dimensional Control Form: distribution Manufacturing, Design, QC; retention **permanent**.
Approval role: QC manager (prepared), approved by deputy design & manufacturing and management representative.

### 2.6 WI-QC-004-01 QC Plan (ITP template)

Content is in English. Terms: **A** Approve (work may proceed after approval of documents/reports); **R** Document review (inspector reviews certificates/documents/reports only); **SW** Spot witness (initial stage / random); **W** Witness (must notify date **10 days before** operation in Iran, **60 days** if outside Iran; if no action, work can proceed); **H** Hold point (presence required, mandatory for continuing). Legend additionally defines: CHT chemical test, MET mechanical test, DT dimensional, VT visual, UT, MT, PT, HT hydrostatic, NDT, HAT hardness, PII painting inspection, FW full witness, FT fit-up, X execution/establish, RC random check, TPI third party inspector, MC management consulting, M vendor's responsibility, V vendor, P purchaser, C client, RE report, RA review & acceptance. References: ASME II, V, VIII, IX; client spec; API 611; ANSI; vendor spec for labelling; ASTM; ISO.
Table 1 (component-level; columns Task, Component, QC status, Acceptance criteria, surveillance V/TPI/C, Remark): Blade (MT, visual), Disk (UT, MT on machined disk, visual), Shaft (UT if repair, MT, visual), Casing (visual check of rough cast per MSS SP-55, MT, UT if repair), Valve (MT, visual per MSS SP-55, UT if repair). V=H, TPI=W, C=W for all rows except Valve visual inspection, where TPI and Client = R. All remarked "Major Part".
Table 2 (stage-level; columns: inspection and test, ref. acceptance doc/criteria, INNCO(vendor) / GL(TPI) / NIOEC(client), Document): full seed data is in Appendix A.
Important: this is a generic turbine ITP with a specific client (NIOEC) and TPI (GL); the plan is not per work order and covers no pump/condenser.

### 2.7 WI-QC-005-02 UT
Person: Level II certified; approval by QC manager or delegate. References ASME V Art.5; ASTM E 114, 164, 213, 214, 587, 1316; Mil-STD-410; NAS-410. Steps: confirm instrument calibration; choose probe (flat, angle, conical); set sound velocity and frequency using standard tables; couplant; scan; interpretation by trained person only. Surface must be non-porous and machined-finish; on castings machined ferritic surfaces only. No temperature limit but sound velocity variation must be considered. **No acceptance criteria, report form, calibration block, scan plan, sensitivity setting or recording threshold is stated.**

### 2.8 WI-QC-006-01 Mechanical run test (no load, before shipping, at factory stand, factory steam)
References API 611 clause 6.3.3, IPS-M-PM-240. Prerequisites: steam paths and pipes clean; casing and valves hydro-tested; parts NDT passed; steam flow for pressure lubrication measured; oil filtration **25 micron**. Steps: mount on stand, close steam flanges; check oil levels; keep hand valves open; check controls/trip/safety devices; install temperature, pressure, speed, vibration sensors; governor valve fully open via governor setting handle; open main steam valve slowly, reach **900 rpm**; step up in **10 % of continuous speed** steps to minimum operating speed, rated speed, maximum continuous speed and slightly below trip; measure and record vibration at each speed; **run 4 hours, record oil temperature, shaft speed and vibration every 20 minutes**; confirm rotation direction and exhaust position with arrow; check governor and speed control; **overspeed trip at 110 % of continuous speed, 3 consecutive trips, accuracy +-2 %**; check sentinel warning valve. No form; no vibration/temperature acceptance limits; no signatories.

### 2.9 WI-QC-007-01 Performance test
Based on a customer need; simple ASME method (steam flow from enthalpy drop); notes API 611 6.3.4.1 says the test is not routine for API 611 turbines; reference API 612. Performed under load as early as possible after start-up and **not later than 90 days; test duration 2 hours**; instruments and maximum allowable uncertainty: pressure gauges P1-P3 +-1 % FS, thermocouples T1-T4 +-3 F, torque meter +-2 %, flow meters F1-F3 +-5 %, rpm. Instruments must be calibrated before the test. Pressure above 35 psia by Bourdon gauge, below by calibrated absolute transducer; two thermocouples at 90 degrees for inlet; calculation form with fields: initial pressure/temperature/enthalpy, exhaust pressure/temperature/enthalpy/superheat, gland leak-off flows with P/T/h, torque meter/speed power to driven machine (HP), mechanical losses (gear, turbine; supplied by INNCO), heat balance lines 1-5, throttle steam flow. Approval: QC manager or delegate **and client representative**.

### 2.10 WI-QC-008-00 MT
Level II per ASNT SNT-TC-1A; certificate valid to expiry even if qualification procedure changes. References ASME V (2010), VIII-1, VIII-2 (2010), ASTM E1444, E709, AWS D1.1. Technique: AC yoke, continuous method, wet (visible/fluorescent) or dry. Limits: coating non-magnetic max **0.004 in**; contrast paint max **0.002 in**; particle concentration **1.2-2.4 %** (visible) or **0.1-0.4 %** (fluorescent) by settling test, checked before use or every **8 h**; suspension max **43 C**, ambient max **57 C**, surface-to-suspension difference max **14 C**; yoke lifting force min **10 lb (4.5 kg)**, checked **yearly** and after repair; field adequacy with pie gauge/Ketos ring/magnetic rubber **before each test**; each area examined **twice at approx. perpendicular directions**; L/D rule 15 and max span 15 in; white light min **1000 lux**; fluorescent ambient light max **21.5 lux** (as printed); lux meter calibrated yearly and after repair (or before use if unused >= 1 year); relevant indication > **1.5 mm**; demagnetise to **+-3 gauss** (+-2 gauss per API 687 8.2.4.2) measured with gauss meter; exceptions listed. Acceptance table: B31.1/B31.3, ASME VIII-1 (linear relevant > 1/16 in; rounded > 3/16 in; 4+ rounded aligned) and AWS D1.1, and API 687 Table 1.8.1 (severity A/B/C by component stress class). Report: **FO-QC-017**.

### 2.11 WI-QC-009-00 First-article / suspect samples
1. Supply unit hands first or special samples to warehouse delivery. 2. Warehouse keeper informs QC **verbally**. 3-4. QC receives and tests samples in lab or QC against indices, standards, methods and drawings. 5. Also tested for assembly fit. 6. Results recorded in a **binder**. 7. **Identification label** on sample: entry date, name of material/part, supplier name, material code, status, name and signature of reviewer. 8. Results recorded and kept. 9. Rejected samples are kept in the lab and **not returned to the maker**. 10. After approval of the first sample the supplier ships the consignment. 11. Results kept for "......" years (**blank**). 12. Special samples get full tests.
Definitions: first sample = first-time item from a valid supplier; special sample = supplier/contractor changed material, production characteristics or method.

### 2.12 WI-QC-010-00 Welding process quality control
Content: a table of weld defects with causes and consequences (overlap/over-roll, slag inclusion, undercut, porosity, lack of fusion, misalignment, lack of penetration (printed twice), weld cracking incl. hot/cold cracks and shrinkage stress). Only operative requirement: "because welding is a special process, at least conformity to the **WPS** and a **valid welder qualification (WQT)** must be ensured; request the **PQR** when needed". Appendix: "فرم کنترل کیفیت قطعات جوشکاری شده" (no code). No acceptance criteria, no steps, no records beyond the form.

### 2.13 WI-QC-011-00 Casing dimensional control
Five sub-checklists, each starting "assume the part was made separately; highlighted (red) items need a control method to be determined":
- 6-1 INLET (17 items), 6-2 EXHAUST (19 items), 6-3 GOVERNOR HOUSING (13 items), 6-4 EXHAUST SUPPORT (14 items), 6-5 COVER (10 items). Checks: hole position relative to each other via template (شابلون) fixed with pin and screw; hole position relative to shaft axis; parallelism, perpendicularity, flatness (dial indicator on machine, blade/feeler with light gap), roughness of surfaces to be scraped (بلوچک), remaining dimensions from control drawing. Platform-specific items: 3SY (thrust bearing wall) vs BSY/TY (anti-friction bearing wall).
Responsible: the person controlling the part. Reference: control drawings. Form "ثبت کنترل کیفیت CASTING" (no code). **There is no casting inspection content (foundry, chemistry, NDT, repair) despite the file name.**

### 2.14 WI-QC-012-00 In-place QC (in-house and contractor shop)
1. **In-house:** after each fabrication stage per the **OPC** (operation process chart) the Manufacturing unit delivers the part to Warehouse; keeper issues the temporary receipt and hands the part to QC; QC inspects and records the result **in the warehouse software**; if approved Warehouse issues the permanent receipt. If the process continues, QC approval equals the **issue voucher and permission to continue** by Manufacturing. If rejected and repairable, Warehouse issues an issue voucher to Manufacturing; if not repairable -> scrap instruction WI-WH-004.
2. **Contractor shop** (example "Zagros factory"): after each stage per OPC the **PMO unit** completes the "in-place inspection form" and sends it to QC and Warehouse; a QC person **attends the contractor workshop**. Warehouse documents must be complete; after issuing the temporary receipt the labels go to the QC expert to stick on the part (or to the Manufacturing manager if sticking is impossible). After inspection the QC expert records the quality status in the warehouse software. Every unit must bring the previous-process label when handing parts to the warehouse; surplus material returns to the warehouse.
Forms: FO-QC-018-00 (inspection order), FO-WH-001/007/002.

### 2.15 WI-QC-013-00 QC unit comprehensive instruction
Activities: (1) monitor incoming items per prepared **control plans**; (2) monitor production with approved and calibrated instruments, **immediately order production stop** on error; (3) interfaces: Warehouse (receiving/handing parts), Production (control during fabrication), Assembly (control at each assembly step), Engineering (feedback of defects), Planning (daily coordination of projects); (4) archive: dimensional control results on forms filed in project binder, **scan control forms into the database**; NC records archived in project binder after finalisation and scanned into database; assembly/production control forms; **photographs** of control steps stored in the system; **overhaul reports of turbines and the final book** prepared.

### 2.16 WI-QC-014 (WI-WH-001-04) Warehouse flow relevant to QC
Request in the "Rai Dana" software (BOM requests by Planning, reorder-point requests by Warehouse) -> check/approve by Planning and Manufacturing management -> purchase request -> inquiry registered by Commercial -> inquiry approval (petty-cash regulation) -> purchase order (entry permit becomes visible to keeper) -> receipt: keeper checks against entry permit; labels are issued; for parts made in-house the keeper **links the previous-stage serial to the new serial**; location recorded -> **temporary receipt into quarantine with barcode on all items, presented to QC** -> project items (parts and materials) inspected by QC; consumables/tools inspected by the requester and received directly -> QC enters quality data in the software -> permanent receipt for approved items; non-approved: send for rework with **NCR** form or **return voucher to supplier**; replacement follows the same purchase request. Other: separate store for returned/repair products; periodic stock audit by the internal-audit team against the system list; issue voucher security rules (names, signatures, destination, plate, security check); **loading of all products must be coordinated with QC, which may block the shipment**; contractor signature with name, fingerprint and stamp.

### 2.17 PR-QA-001-00 Management review
1. Meeting **at least once a year (twice a year is said to be better)**; members = review committee of managers and unit heads selected by the CEO. 2. Minutes on FO-QA-022, copies to concerned units. 3. Inputs: follow-up of previous decisions; internal audit results; process performance and product conformity; corrective-action results; preventive-action results; customer feedback; management representative and production-improvement reports; changes affecting the system; statistical reports (output, stoppages, scrap, NC, goods in/out, product dispatch, raw material in/stock); other topics. 4. Each decision gets a corrective action planned and executed, listed on FO-QA-017; Management Representative (QA unit) is responsible for follow-up. 5. Outputs: executing decisions; closer to customer requirements; resource allocation; improved QMS effectiveness. **Not stated:** minutes deadline, due dates for actions, retention.

### 2.18 PR-QA-002-00 Risk assessment (RPN)
Part A (processes): QA manager defines company processes and the OPC charts; CEO and QA manager set workgroups (QA, customers, QC, administration & HR, production, planning, commercial, R&D, warehouse, technical & engineering, training, HSE) on FO-QA-037; workgroups meet **yearly** and fill tables (activity, description, risk, risk effects, opportunities, causes, current controls, responsible, Plan B, RPN); S, O, D scored with tables A, B, C (**not included in the file**); RPN determined; annual review (FO-QA-038). Criticality: **level 1** normal (all three factors <= a threshold printed as 6, RPN low), **level 2** semi-critical (at most one factor above the threshold, RPN low), **level 3** critical (at least 2 of 3 above the threshold and RPN high; immediate preventive action). The risk table columns: activity description, risk, cause, severity, occurrence, detection, RPN, risk effects, opportunity, control action, responsible, effectiveness evaluation method, corrective action. Part B: risks of improvement programs recorded on FO-QA-020; re-assessed when programs change. After risk table, the quality-control process may change and must be updated. **RPN limit values are not given**.

### 2.19 PR-QA-003..005 and WI-QA-001..004 (supporting QMS processes, summary)
- **PR-QA-003 knowledge**: skills identification form per employee reviewed by technical committee (CEO, QA, line manager), skills chart prepared by QA manager, approved by CEO, reviewed yearly; internal knowledge via interviews; external knowledge from inspectors (managers record lessons in minutes; QA manager controls), consultants/contractors (knowledge deliverable in contract; final report), trainers (notes), QMS auditors (QA manager records learning and NC resolution in minutes after the audit); customer knowledge: main customers list (FO-QA-005), customer requirements booklet (FO-QA-044), complaint registration (FO-IP-033 with a "knowledge learned" field), customer voice form (FO-IP-037) analysed by QA, customer inspection team report (FO-QA-045); development via suggestions, proposals by new hires after a probation period (digits garbled **[unverified: about 30 working days]**), seminars; distribution (FO-QA-047), knowledge utilisation chart (FO-TR-015), yearly evaluation by QA manager and technical committee; knowledge report is an input to management review.
- **PR-QA-004 strategy**: strategic committee yearly (best in last month of previous year or Farvardin); minutes; SWOT internal/external factor matrices with scores 1-4 and weights summing to 1; weighted average below a threshold (printed 2.5 [unverified]) = weaknesses exceed strengths / threats exceed opportunities; SO/ST/WO/WT strategies; annual objectives.
- **PR-QA-005 stakeholders**: employees (6-monthly survey FO-HR-001; yearly stakeholder workgroup), shareholders (board minutes, productivity report by QA manager), environment (HSE control charts of water, power, gas; environmental complaints), customers (customer satisfaction WI-IP-004, FO-IP-037, complaints -> customer requirements booklet; reviewed at least yearly), government bodies (accounting informs CEO; R&D checks the national standards institute yearly for latest product standard); summary in FO-QA-048.
- **WI-QA-001 suggestions**: 31-step flow; reviews within **1 month**; appeal within **1 week**; trial implementation **1-3 months**; groups by yearly profit (A < 70 M rial; B 70-250 M; special > 250 M); reward formula and caps (printed in Persian units; not needed for the quality module); committee quorum two-thirds.
- **WI-QA-002 5S**: audit by checklist scoring 0-3 per question; corrective action if a question scores >= 2 (as printed; direction ambiguous) or total < **75 %** with formula sum(Xi)/(3n) x 100; form FO-QA-049-00.
- **WI-QA-003 motivation**: 6-monthly satisfaction form, collected within **2 days**; five-level scoring; rating bands >= 90 % excellent, 75-89 % good, 65-74 % average, <= 64 % weak (as printed).
- **WI-QA-004 committees**: secretary appointed by CEO; schedule yearly (FO-QA-051); agenda at least **1 week** before; quorum **60 %** [unverified digits "60"]; resolutions need 60 % favourable; 3 consecutive or 5 interleaved unexcused absences per year = resignation, replacement within **2 weeks**; minutes fields: number/date, time/place, attendees, absentees, summary/resolutions, responsible, deadline, signatures (form FO-HR-018).

### 2.20 WI-QA-005-01 PFMEA
Applies to all new/changed products, process changes, and periodically. Team led by Technical manager with QC, Production and QA managers; **team not larger than 6**. Form fields: FMEA number (per document coding), product name, responsibility, team members, revision date, preparation date, operation code (from OPC), operation name, potential failure mode, potential effects, severity (1-10), class (critical/key/major/important), potential causes, occurrence (1-10), current controls, detection (1-10), RPN = S x O x D, recommended actions, responsibility/target date, actions taken, new RPN. Action rule: customer sets max RPN; otherwise act on the top **10-20 %** RPN and on any S or O of 9 or 10; repeat until acceptable; review period **one year** (QC manager may change). Rating tables 1-3 are automotive in wording (vehicle, assembly line) and use Ppk bands for occurrence, and A/B/C detection method columns.

### 2.21 WI-QA-006-00 Data analysis
Six-monthly (start of 1st and 2nd half-year). Data-registrar records in the data location; analyst prepares a report with a suitable chart; distribution per table; main reviewer decides: (a) no special case or **no comment within 5 days** -> archive as quality record; (b) special case -> corrective action with the Management Representative; (c) improvement -> continual improvement procedure. Pareto 80/20 explained with an example. Table of data (image):
| Report | Collection method | Period | Factors | Data registrar | Distribution | Analyst | Main reviewer |
|---|---|---|---|---|---|---|---|
| Internal audit results | audit report forms | 6 months | NC by standard clause and unit | auditors (ممیزین) | CEO, Management Rep | Management Rep | Management Rep and CEO |
| Nonconformity of products and services | NC registration/follow-up forms | 6 months | NC of products/services by NC type | persons named in the NC minutes | CEO, Management Rep | Management Rep | Management Rep and CEO |
| Achievement of objectives | objectives and programs form | 6 months | degree of achievement | Management Rep | CEO, Management Rep | Management Rep | CEO |
| Monitoring and measurement results | process monitoring result form | 6 months | process monitoring results | process monitoring owners | CEO, Management Rep | Management Rep | Management Rep |
| Customer complaints, surveys, suggestions | survey forms; complaint registration forms | 6 months | complaints by type; satisfaction by question | designated persons | CEO | Management Rep | Management Rep |
| Supplier performance | evaluation forms | 6 months | supplier status by type of service/goods received | designated persons | CEO | Procurement | Management Rep |

---

## 3. Forms catalogue

Legend: Source = procedure that cites the form. Signers/Distribution as printed, else "not stated". "Fields" printed or inferred.

### 3.1 Quality-control forms (FO-QC-xxx and legacy codes)

| Code | Title (Persian) / English | Source | Fields (printed / inferred) | Signers | Distribution / retention |
|---|---|---|---|---|---|
| FO-QC-007 (body) = FO-QA-008 (appendix) | فرم کنترل کالیبراسیون دستگاه ها و تجهیزات / Calibration control register | WI-QC-001 | Inferred: equipment code (INN-IST-nnn), name, owner unit, location, calibration date, due date, interval, lab, certificate no., status (calibrated / NC / WC), correction table ref. | QC manager (compiles) | not stated |
| FO-QC-008 (body) = FO-QA-009 (appendix) | فرم لیست تجهیزات واحدها / Units' equipment list | WI-QC-001 | Inferred: unit, equipment name/model/serial, range | Unit, calibration officer | not stated |
| FO-QC-009 (body) = FO-QA-007 (appendix) | فرم تجهیزات نیاز به کالیبراسیون / Equipment needing calibration | WI-QC-001 | Inferred: equipment, range, accuracy needed, proposed interval | Unit, calibration officer | not stated |
| FO-QC-010 (= FO-QA-010) | فرم تحویل تجهیز کالیبره / Handover of calibrated equipment | WI-QC-001 | Inferred: equipment code, receiving unit, date, certificate ref. | QC manager, receiving unit | not stated |
| FO-QC-011 (= FO-QA-011) | فرم شناسنامه تجهیزات کالیبراسیون / Calibration equipment identity file | WI-QC-001 | Inferred: full identity and history of the instrument; one file per calibrated instrument | Calibration officer | not stated |
| Calibration label | برچسب کالیبراسیون | WI-QC-001 | *Printed:* equipment name, calibration code, calibration time, expiry time; variants **NC** and **WC** | Calibration officer only | affixed to device |
| FO-QC-013 | فرم کنترل محصول نامنطبق / Nonconforming product form (NCR) | PR-QC-002 | *Printed/inferred:* NC description, location (incoming/in-process/final), Major/Minor, root cause category (6 values) + personal-view cause, disposition (approve/rework/reject), corrective & preventive action by Engineering manager, technical committee decision (Major), concession ref., stamp and signatures of QC, Engineering, Manufacturing/Assembly managers; re-issue after failed rework | QC inspector, QC manager, Engineering manager, Manufacturing/Assembly manager; Technical Committee for Major | Procedure copies to QA, QC, Warehouse, Design & Eng.; form retention not stated |
| FO-QC-14 (printed with 2 digits) | Dimensional Control Form (DCF) | WI-QC-003 | Inferred: drawing code/revision, item, nominal, tolerance, measured, result, instrument ID; prepared per work stage | QC | Manufacturing, Design, QC; **permanent** |
| FO-QC-016 | LIQUID PENETRANT REPORT | WI-QC-002 | *Printed:* all indications (rejected and not), type (linear/round), location and size marked on drawing or photo. *Inferred:* part, process, penetrant batch, temperatures, times, light level, personnel level | Level II examiner; approval QC manager | not stated |
| FO-QC-017 | MAGNETIC PARTICLE TEST report | WI-QC-008 | Same marking rules as 016. *Inferred:* yoke ID and lift check, particle type/concentration, light level, demagnetisation reading | Level II examiner; approval QC manager | not stated |
| FO-QC-018-00 | فرم صدور حکم بازرسی / Inspection order | WI-QC-012 | Inferred: part, project, supplier, stage, inspector, date | PMO/QC | not stated |
| (no code) UT report | - | WI-QC-005 | **No form exists**; suggested minimum fields in section 5 | - | - |
| 03FM057 (legacy) | فرم بررسی کیفی نمونه اولیه / First-sample quality review | PR-QC-003 | Inferred: item, supplier, test results, accept/reject | QC manager | Supply |
| 03FM058 (legacy) | فرم ترخیص اضطراری / Emergency release | PR-QC-003 | Inferred: item, reason, authorisation by Planning manager | QC manager, Planning manager | one copy to Production manager |
| 03FM059 (legacy) | Layout Inspection | PR-QC-003 | Inferred: full functional/dimensional layout per standard and customer requirement, yearly | QC | not stated |
| (no code) | فرم بازرسی مواد خام / Raw-material inspection | PR-QC-003 | not visible | QC | not stated |
| (no code) | Manufacturing Process Sheet (MPS) | PR-QC-003 | process parameters, in-process inspections, QC manager final signature before delivery to Warehouse | Production manager, QC manager | not stated |
| (no code) | فرم کنترل محصول نهایی (Test Report) | PR-QC-003 | final batch tests | Lab in-charge, QC manager | not stated |
| (no code) | فرم ثبت نتایج آزمونها و تستهای آزمایشگاهی | PR-QC-003 | test results with periods | QC | not stated |
| (no code) | مجوز خروج کالا / Goods exit permit | PR-QC-003 | release signature | QC manager | product warehouse |
| (no code) | فرم کنترل کیفیت قطعات جوشکاری شده | WI-QC-010 | not visible | - | - |
| (no code) | فرم ثبت کنترل کیفیت CASTING | WI-QC-011 | probably list of items in 6-1..6-5 | - | - |
| (no code) | Performance test data sheet | WI-QC-007 | *Printed:* initial steam P/T/h; exhaust P/T/h/superheat; gland leak-off 1 and 2 (flow, P, T, h); torque meter/speed power (HP); mechanical losses (gear, turbine); heat balance items 1-5; throttle steam flow | Test engineer, QC manager/delegate, client representative | not stated |
| (no code) | Sample identification label | WI-QC-009 | *Printed:* entry date, name of material/part, supplier, material code, status, reviewer name and signature | QC | on sample |
| (no code) | Inspection / test sticker ("بر چسب کنترل کیفیت") | PR-QC-003 | on lots inspected at supplier | QC | - |
| FO-QC-015, FO-QC-012 | **Not cited in any file** (gap in the FO-QC sequence) | - | - | - | - |

### 3.2 Warehouse / outsourcing forms used by QC

| Code | Title | Source | Remark |
|---|---|---|---|
| FO-WH-001 | فرم رسید کالا (goods receipt / permanent receipt) | WI-QC-012 | permanent receipt issued after QC approval |
| FO-WH-007 | فرم رسید موقت کالا (temporary receipt) | WI-QC-012 | QC status recorded in warehouse software |
| FO-WH-002 | فرم حواله خروج کالا (issue voucher) | WI-QC-012 | signature rules in WI-QC-014 |
| WI-WH-004 | دستورالعمل ضایعات (scrap) | WI-QC-012 | not read |
| "NCR" | form accompanying goods returned for rework | WI-QC-014 | code not given; may be FO-QC-013 |
| "حواله مرجوعی" | return voucher to supplier | WI-QC-014 | code not given |

### 3.3 Quality-assurance forms (FO-QA-xxx and others)

| Code | Title | Source | Fields / notes | Signers / distribution |
|---|---|---|---|---|
| FO-QA-005 | لیست اصلی مشتریان | PR-QA-003 | name, address, phone, products | QA manager |
| FO-QA-007..011 | see 3.1 (calibration; appendix numbering) | WI-QC-001 | conflicts with body | - |
| FO-QA-017 | فرم شناسایی موارد نامنطبق و اقدامات اصلاحی (NC & CAPA) | PR-QA-001 | CA per management review decision | QA unit follows up |
| FO-QA-018 | فرم گزارش ممیزی داخلی (internal audit report) | PR-QA-003 | fields not visible | auditors |
| FO-QA-020 | فرم پایش و اندازه گیری | PR-QA-002 | monitoring and measurement of objectives/programs; risks of programs | process workgroup |
| FO-QA-022 | صورتجلسه بازنگری مدیریت | PR-QA-001 | minutes | MR |
| FO-QA-023 | فرم شناسایی موارد نامنطبق و اقدامات اصلاحی | PR-QA-003 | **same title as FO-QA-017, different code** | - |
| FO-QA-028 | ماتریس تجزیه و تحلیل SWOT | PR-QA-004 | strategies SO, ST, WO, WT | QA manager |
| FO-QA-029 | ماتریس ارزیابی عوامل داخلی / خارجی | PR-QA-004 | factor, score 1-4, weight, weighted score, average | QA manager |
| FO-QA-031..035 | suggestion forms (اجرای پیشنهاد / اعلام نتیجه / پرداخت پاداش / ارائه پیشنهاد / ارزیابی پیشنهاد) | WI-QA-001 | FO-QA-035 max 100 points, 20,000 rial per point, work-relation and education coefficients | suggestion committee |
| FO-QA-037 | لیست کارگروه های شرکت | PR-QA-002 | 12 workgroups | CEO, QA |
| FO-QA-038 | فرم شناسایی و ارزیابی ریسک | PR-QA-002 | 13 columns listed in 2.18 | workgroup |
| FO-QA-039 | شناسایی اطلاعات و میزان آگاهی کارکنان | PR-QA-003 | per-employee knowledge | technical committee |
| FO-QA-040 | چارت شناسایی مهارت کارکنان | PR-QA-003 | skills chart | QA manager, CEO |
| FO-QA-041 | فرم مصاحبه با افراد متخصص | PR-QA-003 | interview | - |
| FO-QA-042 | صورتجلسه بهره گیری دانش | PR-QA-003 | - | - |
| FO-QA-043 | ثبت نظرات و پیشنهادات مدرس | PR-QA-003 | - | training |
| FO-QA-044 | کتابچه الزامات مشتریان | PR-QA-003, PR-QA-005 | **customer requirement register** - highly relevant to QC | QA |
| FO-QA-045 | گزارش ارزیابی مشتری | PR-QA-003 | customer-site inspection report | QC/QA |
| FO-QA-046 | ثبت دانش کسب شده ارائه پروپوزال | PR-QA-003 | - | - |
| FO-QA-047 | توزیع و نگهداری دانش | PR-QA-003 | - | QA |
| FO-QA-048 | جدول الزامات و انتظارات ذینفعان | PR-QA-005 | - | stakeholder workgroup |
| FO-QA-049-00 | چک لیست 5S | WI-QA-002 | score 0-3 per question | auditor |
| FO-QA-051 | جدول کمیته ها | WI-QA-004 | committee, secretary, period | - |
| FO-HR-001 | نظرسنجی از رضایت شغلی کارکنان | WI-QA-003, PR-QA-005 | 5-level scale | - |
| FO-HR-018 | صورتجلسه | several | number/date, time/place, attendees, absentees, summary/resolutions, responsible, deadline, signatures | members |
| FO-IP-033 | ثبت شکایت مشتریان | PR-QA-003 | includes a "new knowledge" field | - |
| FO-IP-037 | فرم نظرسنجی مشتریان (also "جمع آوری صدای مشتری") | PR-QA-003/005 | - | - |
| FO-TR-015 / FO-TR-020 | چارت بهره گیری از دانش / گزارش کار | PR-QA-003 | - | - |
| FO-SA-003 | حواله فروش | PR-QA-003 | - | - |
| FO-FO-047 | typo of FO-QA-047 (OCR) | PR-QA-003 body | - | - |

### 3.4 Missing forms (needed by the procedures but no code or form found)
M-01 UT report; M-02 hydrostatic test report; M-03 mechanical run test report (with vibration/temperature log every 20 min); M-04 performance test report with a code; M-05 balancing report; M-06 rotor run-out / cold clearance record; M-07 painting/blasting inspection report; M-08 welding: WPS, PQR, WPQ/WQT register, welder continuity log, weld map, VT report, RT report, PWHT record, consumables control; M-09 material/MTC verification record (EN 10204 type, heat no.); M-10 casting inspection record; M-11 hardness test report, PMI; M-12 source inspection report at supplier; M-13 customer returned product report (deleted in PR-QC-002 rev 04); M-14 CAPA record with root-cause analysis and effectiveness check; M-15 internal audit programme, plan, checklist (only report FO-QA-018 referenced); M-16 customer complaint (WI-IP, not read); M-17 release note and certificate of conformity templates; M-18 final data book index (MDR); M-19 NDT personnel qualification register; M-20 concession request/approval; M-21 incoming inspection per control plan (the plans themselves are not in the set).

---

## 4. Gap and conflict analysis

Severity: H high (certification/customer-audit risk), M medium, L low. Clause references: ISO 9001:2015 = "ISO"; API Spec Q1 10th ed. = "Q1" (clause numbers as given in the brief); ASME IX, VIII-1, V; EN 10204; API 610/611/612/614; ISO 10012; ISO 19011.

### 4.A Gaps against standards

| ID | Sev | Gap (what is missing or weak) | Source | Clause |
|---|---|---|---|---|
| G-01 | H | **Welding control is a defect catalogue, not a control process.** No WPS/PQR/WPQ lifecycle, no welder ID/stamp, no welder continuity (6-month rule), no consumable control (storage, baking, issue), no preheat/interpass/PWHT records, no weld map or traceability to welder, no VT/NDT percentage per joint class, no weld-repair procedure, form has no code. | WI-QC-010 | ISO 8.5.1 f) special-process validation; Q1 5.7.1.3 process validation; ASME IX QW-200 (WPS/PQR), QW-300 (WPQ), QW-322 (renewal after 6 months without welding in the process); ASME VIII-1 UW-28/UW-29/UW-51/UW-52 where pressure parts apply; API 611/610 welding and weld-repair clauses |
| G-02 | H | **No hydrostatic test procedure or report**; only a line in the QCP and a pre-requisite note in WI-QC-006. No test pressure rule, hold time, gauge calibration, test medium/temperature, leakage criteria, witness. | WI-QC-004, 006 | Q1 5.7.6; API 611 6.3 / API 610 8.3 (hydrostatic test); ASME VIII-1 UG-99 for condensers/vessels; ISO 8.6 |
| G-03 | H | **NDT methods incomplete and not controlled as a system**: RT absent (needed for butt welds of pressure parts and castings), no hardness/PMI, no UT acceptance criteria/report/blocks, PT dwell limit stated as a maximum, different ASME V editions in PT (2013) and MT (2010), NDT personnel qualification only "Level II" (SNT-TC-1A named only in MT), no written practice, no procedure qualification/approval by Level III. | WI-QC-002, 005, 008 | ISO 7.2, 8.5.1; Q1 5.7.1.3 (special processes), 5.7.6; ASME V Art.2/5/6/7; ASME VIII-1 UW-51/52, UG-93; ISO 9712 / ASNT SNT-TC-1A |
| G-04 | H | **No casting/foundry quality control.** WI-QC-011 ("Casting Control") covers only dimensional checks of casings. No foundry approval, chemistry/mechanical certification, heat treatment records, casting NDT or weld-repair records; only a line in the QCP ("Casting material" -> ASME II/ASTM, EN 10204 certificate, type unspecified). | WI-QC-011, WI-QC-004 | ISO 8.4 external provider control, 8.5.1, 8.5.2; Q1 5.6 purchasing, 5.7.6; EN 10204 2.2/3.1/3.2 (type must be chosen by purchase order); ASTM A216/A217 etc. as applicable |
| G-05 | H | **Calibration system lacks metrological basics:** no equipment register data model in a controlled form (conflicting form codes), no interval table, no acceptance limits/criteria for calibration results, no traceability requirement to national/international standards or accredited labs, no "out-of-tolerance impact assessment" beyond one line ("monthly control" is undefined), no environmental conditions, no handling of in-house standards, no software validation, no list of equipment excluded. | WI-QC-001 | ISO 7.1.5.2 (traceability; assess validity of earlier results when found out of tolerance); Q1 5.8 (MME calibration); ISO 10012 (metrological confirmation and intervals) |
| G-06 | H | **Corrective action lacks structure and timing.** Root cause is a 6-category pick-list plus an opinion field; no analysis method (5-why, 8D), no due dates, no effectiveness-check date, no link to NCR trends, CAPA procedure not in the set; management review uses FO-QA-017 while PR-QA-003 cites FO-QA-023 for the same title. | PR-QC-002, PR-QA-001 | ISO 10.2; Q1 5.10 (nonconformity) and CAPA with root cause |
| G-07 | H | **NC disposition: no customer concession path, no formal "use as is/repair" distinction, no regulator/customer notification.** Concession is signed by the Deputy of Design & Manufacturing only; the QCP gives the client Hold/Witness rights on the same activities. Repair is limited to "rework". Rejected goods have no scrap/mutilation record. No decision time limit. | PR-QC-002 | ISO 8.7.1, 8.7.2 (retain records of concession, authority, description); Q1 5.10; EN 10204 3.2 / client spec |
| G-08 | H | **No per-work-order ITP.** The QCP is a generic turbine template tied to one client (NIOEC) and TPI (GL). No ITPs exist for pumps (API 610), condensers (ASME VIII/TEMA), ORC units, auxiliaries (API 614 where oil systems), or valves beyond a row. No revision/approval of ITP by client before work (A point on the QCP is "approval of vendor documents"). | WI-QC-004 | ISO 8.1, 8.5.1 a) documented information, b) monitoring; Q1 5.7.6 (inspection plans), 5.6 (flow-down); API 611/610 inspection and test sections |
| G-09 | H | **Mechanical run and performance tests have no records form or acceptance criteria**: vibration limits and bearing-temperature limits are not stated; "site" wording in the QCP conflicts with "factory stand" in WI-QC-006; trip test 110 % +-2 % is stated but no pass/fail record. | WI-QC-006, 007, 004 | API 611 6.3.3 / API 612 / API 610 8.3.4; ISO 8.6 (evidence of conformity) |
| G-10 | H | **Balancing and rotor checks only in the QCP, with a withdrawn standard**: "ISO 1940-1 (Grade 1.0)" is superseded by ISO 21940-11; there is no balancing procedure, report, residual unbalance record, or runout/cold clearance form. | WI-QC-004 | API 611 / 617-type balancing requirement; ISO 21940-11; ISO 8.6 |
| G-11 | M | **Material identification and traceability is not defined**: requirement for EN 10204 certificates exists only in the QCP without type (2.2/3.1/3.2); no heat-number-to-part linkage; traceability procedure not available; warehouse links serial numbers of previous stage to new serial (WI-QC-014) but without link to material certificate, NDT report or welder. | WI-QC-004, 014, PR-QC-003 | ISO 8.5.2; Q1 5.7 (identification/traceability); EN 10204 |
| G-12 | M | **Release authority unclear.** PR-QC-003: QC manager signs the "مجوز خروج کالا". WI-QC-004: Release note is "R" for INNCO, "H" for TPI, "R" for client; certificate of conformity is "H" for INNCO. No person is named as authorised to release against the final data book. WI-QC-013 lists "final book" only as an activity. | PR-QC-003, WI-QC-004, 013 | ISO 8.6 (person authorising release, no release before planned arrangements satisfied); Q1 5.7.6 |
| G-13 | M | **Incoming inspection plans are not in the set;** records not coded; sample-approval form is legacy-coded; emergency release is authorised by Planning, not Engineering/QC, and no condition for recall/identification of the released product is described. | PR-QC-003 | ISO 8.4.2, 8.6, 8.7; Q1 5.6 |
| G-14 | M | **Outsourced manufacturing (contractor shop) control is thin:** QC attendance and "in-place inspection form" but no supplier requirements flow-down, no source-inspection hold points, no approved-supplier list criteria tied to quality performance, no right-of-access clause, no review of contractor's own records. | WI-QC-012, WI-QC-014 | ISO 8.4.1-8.4.3; Q1 5.6 (purchasing, supplier evaluation) |
| G-15 | M | **Internal audit procedure missing from the set;** only the FO-QA-018 audit report form is referenced; no programme, audit criteria, auditor independence, competence, follow-up timing. | PR-QA-003 only | ISO 9.2; Q1 6.4; ISO 19011 clauses 5 (programme), 6 (conduct), 7 (competence) |
| G-16 | M | **Management review inputs incomplete:** no resource adequacy, effectiveness of actions on risks and opportunities, supplier/external provider performance, objectives achievement as explicit inputs; outputs do not require decisions on improvement opportunities and resource needs in a recorded way; no deadline for minutes; WI-QA-006 table does supply some of these data (objectives, supplier performance) but is not linked in PR-QA-001. | PR-QA-001, WI-QA-006 | ISO 9.3.2 (a-f), 9.3.3 |
| G-17 | M | **Competence (ISO 7.2):** no competence matrix for inspectors/NDT/welders; knowledge/skills chart (PR-QA-003) is not tied to authorisation to inspect; MT note about certificate validity is the only NDT competence rule. | PR-QA-003, WI-QC-008 | ISO 7.2; Q1 personnel competence; ISO 9712 / SNT-TC-1A |
| G-18 | M | **Document control and records retention are inconsistent:** retention stated only for the DCF ("permanent") and drawings; blank retention in WI-QC-009; no retention for NC, NDT, calibration or test records; quality-records procedure (01PR02) uses a legacy code and is not in the set. | WI-QC-003, 009, PR-QC-003 | ISO 7.5.2, 7.5.3; Q1 documents and records control |
| G-19 | M | **Management of change** is only a review input; PFMEA is triggered by process change but there is no MoC for ITP, procedures, WPS or material/supplier changes during a project. | PR-QA-001, WI-QA-005 | ISO 6.3, 8.5.6; Q1 5.11 (management of change) |
| G-20 | M | **Data analysis is six-monthly and not defined as including product test/NDT results,** rework rates by operation, first-pass yield or NDT rejection rates, customer return data; no KPI set. | WI-QA-006 | ISO 9.1.1, 9.1.3 |
| G-21 | M | **Special-process validation for PT/UT/MT is by personnel only;** no process validation record, no equipment verification (UV intensity meters, UT instrument linearity checks, pie-gauge/reference blocks) tied to the calibration register. | WI-QC-002/005/008 | ISO 8.5.1 f); Q1 5.7.1.3 |
| G-22 | M | **Dimensional control is not under measurement-system control:** no gauge R&R/uncertainty statement, no tolerance reference, DCF has no code consistency ("FO-QC-14"). | WI-QC-003, 011 | ISO 7.1.5; ISO 10012 |
| G-23 | L | **Customer complaints/returns:** procedures WI-IP-001/002/004 not in set; returned product form deleted in PR-QC-002 rev 04 while note 2 still requires a QC report. | PR-QC-002, PR-QA-003/005 | ISO 9.1.2, 10.2 |
| G-24 | L | **PFMEA tables are automotive-derived** (vehicle, assembly line, Ppk bands) and do not match turbine/pump manufacture; PR-QA-002 and WI-QA-005 use different RPN decision rules. | WI-QA-005, PR-QA-002 | ISO 6.1 |
| G-25 | L | **Packaging, preservation and delivery** (QCP 6-1..6-3) have no procedure/records; preservation is not defined. | WI-QC-004 | ISO 8.5.4; API 611 shipping preparation |
| G-26 | L | **Test-equipment dependence for performance test** (instrument uncertainty list exists) but no link to calibration register and no reference to uncertainty calculation. | WI-QC-007 | ISO 7.1.5.2 |

### 4.B Internal conflicts and inconsistencies

| ID | Conflict | Where | Resolution needed |
|---|---|---|---|
| C-01 | Calibration forms: body lists FO-QC-007 (control form), 008 (units' list), 009 (needing calibration), 010, 011; appendix lists FO-QA-007 (needing calibration), 008 (control form), 009 (units' list), 010, 011 with **different department prefix and permuted numbers**. | WI-QC-001 | Decide one code per form |
| C-02 | NC forms: FO-QC-013 "فرم کنترل محصول نامنطبق" (product NCR), FO-QA-017 and FO-QA-023 "فرم شناسایی موارد نامنطبق و اقدامات اصلاحی" (two codes, same title), and an unnamed "NCR" in WI-QC-014. | PR-QC-002, PR-QA-001, PR-QA-003, WI-QC-014 | Define NCR vs CAPA record |
| C-03 | Who inspects in-process: PR-QC-003 says control is done by the **operator** and QC controls at end of line; PR-QC-002 and WI-QC-013 say QC inspectors inspect during manufacture and can stop production; QCP marks all H points for the vendor. | PR-QC-003 6.2.1 vs PR-QC-002 6.2 vs WI-QC-013 | Decide self-inspection vs QC inspection and which points are H |
| C-04 | NC disposition authority: Minor = Engineering + Manufacturing/Assembly managers close the form; Major = Technical Committee; concession = Deputy of Design & Manufacturing; the engineering manager defines rework method "based on control plans" (not provided); final product with NC = Deputy + senior manager. Role titles for the deputy differ between documents (معاونت طراحی و ساخت / طراحی و مهندسی / مدیر فنی و مهندسی). | PR-QC-002, WI-QC-001/003/004/006/007/008/010 | Fix role names and authority matrix |
| C-05 | Document codes versus content: WI-QA-005-01 prints WI-EN-005-00; WI-QC-011-00 prints WI-QC-000-00 and file name "Casting Control" vs title "dimensional control of casing"; WI-QC-014 file is WI-WH-001-04; PR-QC-003-00 footer shows rev 01; WI-QA-002-00, WI-QC-008-00 history shows rev 01; WI-QC-001-02 revision log dates (1397/12/01) differ from footer (1398/01/20). | several | Master list clean-up |
| C-06 | Form-code scheme: old codes (01PR02, 03PR05, 03FM034, 03FM057-059) coexist with new (FO-QC-xxx); FO-QC-14 printed with 2 digits; "FO-QC-018-00" and "FO-QA-049-00" include revision suffix, others do not; WI-IP-001 cited for both contract review and customer complaints; PR-IP-004 vs WI-IP-004. | many | Single coding scheme |
| C-07 | Retention: "permanent" (DCF), "......" blank (WI-QC-009), not stated elsewhere. | WI-QC-003, 009 | Retention schedule |
| C-08 | Test standards: PT uses ASME V 2013, MT uses ASME V/VIII 2010; performance test refers to API 612 while mechanical run refers to API 611; QCP Table 2 says "(site)" for mechanical run, instrument check, vibration, performance (hot) while WI-QC-006 says tests happen in the factory before shipping. | WI-QC-002/008/006/007/004 | Decide edition and location |
| C-09 | Release note vs release permit vs certificate of conformity are listed in three documents with different owners (see G-12). | PR-QC-003, WI-QC-004 | One release record |
| C-10 | Returned products: PR-QC-002 rev 04 deleted the returned product report form but remark 2 still requires a QC report. | PR-QC-002 | Provide form or remove remark |
| C-11 | Dimensional control appears in WI-QC-003 (DCF), WI-QC-011 (casting item lists with "Casting" form) and PR-QC-003 layout inspection (03FM059) with no precedence. | three docs | Merge or hierarchy |
| C-12 | Risk thresholds: PR-QA-002 levels (RPN scores > 6) vs WI-QA-005 (top 10-20 % RPN or S/O 9-10). | PR-QA-002, WI-QA-005 | Single rule |
| C-13 | Frequencies differ: management review yearly (twice optimal), data analysis six-monthly, risk annual, calibration labels per schedule, PFMEA annual, layout inspection annual. | several | Calendar in system |
| C-14 | PR-QC-003 says "operators control during production" while WI-QC-013 says QC "monitors, measures and orders stop". | see C-03 | |
| C-15 | WI-QC-012 form: body says PMO completes "فرم بازرسی در محل", appendix lists "فرم صدور حکم بازرسی FO-QC-018-00". | WI-QC-012 | One form |

### 4.C Paper-only or verbal steps that need a system record

| ID | Step | Source | System record |
|---|---|---|---|
| P-01 | Warehouse informs QC **verbally** of first samples | WI-QC-009 | Task/notification with timestamp |
| P-02 | Sample test results in a binder; labels handwritten | WI-QC-009 | Sample record, label print |
| P-03 | Stamp and signature to close minor NC | PR-QC-002 | Electronic signature, stamp image optional |
| P-04 | Technical committee approval on paper | PR-QC-002 | Committee decision object with attendees |
| P-05 | Concession on paper | PR-QC-002 | Concession record linked to NCR |
| P-06 | Calibration labels, correction tables posted at the instrument | WI-QC-001 | Printed label from register; QR code |
| P-07 | Dimensional forms filed in project binder, then scanned into a "database" | WI-QC-013 | Native inspection records |
| P-08 | NC forms scanned after finalisation | WI-QC-013 | Native NCR |
| P-09 | Quality status already recorded in the warehouse software ("Rai Dana") by QC | WI-QC-012, 014 | Integration or replacement; keep quarantine/approved/rejected status and barcodes |
| P-10 | Emergency release by Planning on paper | PR-QC-003 | Conditional release record |
| P-11 | Written notice of faulty instrument to QC manager | PR-QC-002 | Equipment defect report |
| P-12 | Final book and overhaul report built manually | WI-QC-013 | Final data book generator |
| P-13 | Minutes, committees, surveys on paper | PR-QA-001, WI-QA-004 | Meeting module |

### 4.D Mapping of requirements to the standards (for the requirements trace)

| Topic | ISO 9001 | API Q1 | Other |
|---|---|---|---|
| Calibration | 7.1.5.2 | 5.8 | ISO 10012 |
| Competence | 7.2 | personnel | ISO 9712/SNT-TC-1A |
| Documents/records | 7.5 | documents/records | - |
| Controlled production, special processes | 8.5.1 | 5.7.1.3 | ASME IX, ASME V |
| Traceability | 8.5.2 | identification | EN 10204 |
| Release | 8.6 | 5.7.6 | API 611/610/612 |
| NC outputs | 8.7 | 5.10 | - |
| Monitoring/analysis | 9.1.3 | - | - |
| Internal audit | 9.2 | 6.4 | ISO 19011 |
| Management review | 9.3 | management review | - |
| Corrective action | 10.2 | CAPA/root cause | - |
| Purchasing/suppliers | 8.4 | 5.6 | EN 10204 |
| Change control | 6.3, 8.5.6 | 5.11 | - |

---

## 5. Proposed data model

Conventions: `PK` primary key, `FK` foreign key. Tag `[src]` = procedure the requirement comes from; `[new]` = introduced by this specification to close a gap (with gap ID). All entities carry `created_at, created_by, updated_at, updated_by, status`; records that are signed carry `signature(user, datetime, role)`.

### 5.1 Reference data
- **Role** (Persian title, English title). Seed: مدیر عامل, نماینده مدیریت, مدیر تضمین کیفیت, مدیر کنترل کیفیت, بازرس کنترل کیفیت, کارشناس کنترل کیفیت, مسئول کالیبراسیون, معاونت طراحی و ساخت, مدیر فنی و مهندسی, مدیر ساخت و مونتاژ, مدیر برنامه ریزی و کنترل پروژه, مدیر بازرگانی, انباردار / سرپرست انبار, مسئول آزمایشگاه, کمیته فنی, ممیز, مسئول 5S. [src all]
- **AuthorityMatrix** (role, action, limit): minor NC disposition (Eng + Mfg), Major NC (Technical Committee), concession (Deputy design & manufacturing), emergency release (Planning manager), goods exit (QC manager), approval of ITP (QC manager + Engineering), calibration label (calibration officer only). [PR-QC-002, PR-QC-003, WI-QC-001]
- **Party** (customer / TPI / supplier / contractor): type, name, approved flag, evaluation scores. TPI example GL, client example NIOEC. [WI-QC-004, WI-QC-001 step 5, WI-QA-006]
- **CodingScheme** (document type PR/WI/FO + department QA/QC/HR/IP/WH/EN/TR/SA + sequence + revision). [all]

### 5.2 Product, work order, and plan
- **WorkOrder** (PK, project code, customer, product type = steam turbine / ORC / condenser / pump / valve, platform TY/BSY/3SY, applicable codes [API 611/612/610/614, ASME VIII], client spec ref.).
- **ProductItem** (WorkOrder FK, part type: blade, disk, shaft, casing(inlet/exhaust/governor housing/exhaust support/cover), valve, rotor, assembly; serial no.; `parent_serial_id` to model "link previous stage serial to new serial" [WI-QC-014 8-2]; drawing no.; drawing revision; status AFC/AFP stamp [WI-QC-003]).
- **ITPTemplate** (name, product type, applicable codes, revision, approval) seeded with Appendix A. [WI-QC-004]
- **ITP** (WorkOrder FK, template FK, revision, status draft/client-approved/in-use, client approval date, parties V/TPI/C). [new G-08]
- **ITPActivity** (ITP FK, seq, component or stage group, activity description, test type enum {CHT, MET, DT, VT, UT, MT, PT, RT [new], HT, HAT, PMI [new], PII, FT, performance, run test, balancing, doc review, packing}, acceptance criteria / reference document, **point_vendor, point_TPI, point_client** enum {H, W, R, SW, A, RA, FW, RC, X, RE, none}, document produced (certificate type, report type), remark (e.g. "Major part"), `notice_days` (10 in Iran, 60 abroad) for W points, planned date, linked process route step). [WI-QC-004]
- **HoldPointRelease** (ITPActivity FK, party, action {waived, witnessed, reviewed, approved}, person, date, notice sent date, `no_response_proceed` flag auto-computed from notice period). [WI-QC-004 definition of W]
- **RouteStep / OPC** (WorkOrder, operation code, name, in-house vs contractor, linked ITP activities). [WI-QC-012 OPC, WI-QA-005]

### 5.3 Inspection records
- **InspectionRecord** (PK, type {incoming, sample/first-article, in-process, final, layout, in-place at contractor}, ITPActivity FK (nullable), ProductItem FK, Material lot FK, inspector (user), inspector level/qualification FK, date, drawing code/revision [WI-QC-003], result {accept, reject, rework, hold}, linked NCR FK, attachments (photos [WI-QC-013]), `stage_label_id` (warehouse label), temp receipt no., permanent receipt no. [WI-QC-012/014]).
- **InspectionMeasurement** (InspectionRecord FK, characteristic, nominal, tol+, tol-, measured, unit, instrument FK (calibration register), pass/fail). Covers DCF FO-QC-14 and WI-QC-011 item lists (position of holes by template, parallelism, perpendicularity, flatness, roughness, other dimensions). [WI-QC-003/011]
- **CheckItemTemplate** (family: Inlet, Exhaust, Governor housing, Exhaust support, Cover, platform applicability 3SY vs TY/BSY, item text, method: template/dial indicator/feeler-light, "red highlighted = method to be determined" flag). Seed from WI-QC-011 (73 items). [WI-QC-011]
- **IncomingInspection** (extends InspectionRecord): PO, supplier, material control plan ref, sampling per plan, source-inspected flag + QC sticker no. + place [PR-QC-003 6.1.2], emergency release FK, MTC verification (see 5.6). 
- **EmergencyRelease** (inspection FK, reason, authoriser (Planning manager), copy to Production manager, recall/trace plan [new G-13]). [PR-QC-003, form 03FM058]
- **SampleRecord** (first/special type [WI-QC-009 5-1/5-2], supplier, material code, test results, status, label data, storage location, retention years [blank in source], disposition: kept in lab if rejected). 
- **LayoutInspection** (product, standard/customer requirement, period yearly, results, actions). [PR-QC-003 6.3]
- **InspectionOrder** (FO-QC-018: contractor, part, stage, inspector assigned, date) and **ContractorStageReport** (PMO in-place form). [WI-QC-012]

### 5.4 Test reports
Common header **TestReport**: PK, report number (see 5.12), type enum, WorkOrder, ProductItem, procedure ref + revision, edition of code, examiner (user), examiner qualification (method, level, certificate expiry), equipment used (FK to calibration register; calibrated-until date validated at save), environmental conditions, date, result, acceptance criteria ref, drawing/photo attachments with indication marking, witness signatures (V/TPI/C), approval by QC manager or delegate.
Type-specific tables:
- **PTReport** [WI-QC-002, FO-QC-016]: penetrant system (type/batch), cleaner, developer, surface prep, application and removal methods, part and penetrant temperature (limit 5-52 C), dwell time, developer time, interpretation time (limit 10-60 min), white-light lux (>= 1000), UV intensity, dark adaptation (>= 5 min), water temp/pressure, light meter calibration due; list of **Indication** (id, type linear/round, location, size, orientation, accepted/rejected, drawing/photo coordinates); essential variable change check (16 items).
- **MTReport** [WI-QC-008, FO-QC-017]: yoke ID, lift-check date, particles (wet/dry, visible/fluorescent), concentration test (1.2-2.4 % / 0.1-0.4 %, last check <= 8 h), suspension temp (<= 43 C), ambient (<= 57 C), delta T (<= 14 C), coating thickness (<= 0.004 in), contrast paint (<= 0.002 in), field adequacy method (pie gauge/Ketos/rubber), two perpendicular passes done, lux, demagnetisation reading (+-3 / +-2 gauss), acceptance code (ASME VIII-1, B31.x, AWS D1.1, API 687 severity A/B/C), indications list.
- **UTReport** [new G-03]: instrument, probe, frequency, velocity, calibration block, DAC/sensitivity, couplant, scan plan, recording level, indications (position, depth, amplitude, length), acceptance code.
- **RTReport** [new G-03]: technique, source, film/digital, IQI, density, sketch, interpretation.
- **HydroTestReport** [new G-02]: test pressure basis (design pressure x factor per applicable code), medium, temperature, gauge IDs with calibration, hold time, leak observations, witnesses.
- **MechanicalRunTestReport** [WI-QC-006]: turbine and stand IDs, steam conditions, oil filtration (25 micron), step table (rpm, 10 % steps from 900 rpm; vibration per probe; bearing temperatures), 4-hour log every 20 minutes (oil temperature, shaft speed, vibration), rotation direction/exhaust position, governor check, **overspeed trip**: setpoint 110 % of continuous speed, 3 trips, deviation +-2 %, sentinel warning valve check, acceptance limits (**to be supplied by company**), result.
- **PerformanceTestReport** [WI-QC-007]: instruments with ID and uncertainty (P1-P3 +-1 % FS, T1-T4 +-3 F, torque +-2 %, flow +-5 %), start-up date and 90-day limit, duration 2 h, measured P/T/h at inlet/exhaust/glands, torque and speed, mechanical losses, computed steam flow, client representative signature.
- **BalancingReport** [new G-10]: standard (ISO 21940-11), grade, planes, residual unbalance, speed.
- **RunoutClearanceRecord** [WI-QC-004 4-2, 4-3]: cold clearance table values, rotor run-out per API 611.
- **DimensionalReport**: alias of InspectionMeasurement set. [WI-QC-003/011]
- **PaintingReport** [WI-QC-004 2-1/2-2]: blast grade (ISO 8501), primer/top coat DFT, adhesion (ASTM D3359 etc.), holiday test.
- **HardnessReport / PMIReport** [new G-03].

### 5.5 Nonconformance (NCR) - reuse existing NCR, add the following
Existing NCR is to be reused; fields missing relative to PR-QC-002-04:
- `severity` {Major, Minor} with definition text; `detected_stage` {incoming, in-process, final, customer return, supplier shop}; `item_status` {NC, suspect/status not determined}.
- `root_cause_category` {manpower, material, methods, equipment, drawing, environment} + `root_cause_opinion` + `root_cause_method` [new].
- `disposition` {accept as is, rework/repair, reject, return to supplier, scrap}; `use_as_is` requires `concession_id`.
- `reviewers`: QC manager classification, Engineering manager, Manufacturing/Assembly manager (minor), `technical_committee_decision_id` (major).
- `concession` entity: requester (via QC), authoriser (Deputy), scope, conditions, validity, at-start or after-rework flag, **client approval required flag** [new G-07].
- `quarantine_location`, `warehouse_return_voucher`, `contractor_owner` flag (return to contractor vs scrap store). 
- `rework_route` (by Engineering, per control plan), `reinspection_id`, `reissue_count`.
- `affected_instrument_id` (from calibration out-of-tolerance) [WI-QC-001 step 12].
- `linked_capa_id`, `effectiveness_verified_by/date`, `reported_to_senior_management` flag [PR-QC-002 6.1.1 Major].
- `customer_return_ref`. `cost_impact`, `time_impact` [Major definition].
- Timing: `date_raised`, `date_decision`, `due_date` [new; no limit in source].

### 5.6 Material certification
- **MaterialLot** (heat/lot no., supplier, PO, spec/grade, quantity, receipt (temporary/permanent), quarantine status, location, barcode). [WI-QC-014]
- **MaterialCertificate** (lot FK, EN 10204 type {2.2, 3.1, 3.2}, issuer, certificate no., chemistry, mechanical values (hardness, tensile), verified by QC, verification result, required type per PO/ITP, attached file). [WI-QC-004 1-1/1-2; new G-04, G-11]
- **Traceability** (ProductItem FK, MaterialLot FK, weld joint FK, NDT report FK, welder FK) [new G-11].

### 5.7 CAPA
**CAPA** (PK, source {NCR, audit finding, management review decision (FO-QA-017), data analysis (WI-QA-006), customer complaint, risk review}, problem statement, containment, root cause analysis (method, 5-why/8D text, category), corrective actions (task list with owner, due date), preventive action, responsible, effectiveness check (criteria, due date, result), closure approval, link to RPN/risk entry (for recurrence). Sources: PR-QA-001, PR-QC-002, WI-QA-006; method and dates are [new G-06].

### 5.8 Calibration
- **Equipment** (PK, code per INN-IST-nnn, name, type, manufacturer, model, serial, owner unit, location, range, accuracy/resolution, `calibration_required` {yes, NC}, `status` {calibrated, WC, NC, out of service, quarantined}, interval months + basis {manufacturer recommendation, previous records, repair history, importance, environment}, last/next due dates, correction table attachment, identity file (FO-QC-011)). [WI-QC-001 steps 3-4, 14-17]
- **CalibrationEvent** (equipment FK, lab (Party, accreditation [new]), request letter, date, on-site vs lab, certificate no. and file, standard/traceability reference [new], as-found data, as-left data, pass/fail, adjustment flag, label printed). [WI-QC-001 steps 5-10]
- **CalibrationLabel** (code, name, calibration date, expiry date, variant {calibrated, NC, WC}); only the calibration officer role may print/replace. [WI-QC-001 16]
- **HandoverRecord** (FO-QC-010). 
- **OutOfToleranceEvent** (equipment FK, reported by user or by monthly control, from-date of last good calibration, affected inspections/test reports/products (auto query on InspectionRecord/TestReport using equipment), NCR created, re-calibration date). [WI-QC-001 12-13; new G-05 for impact analysis]
- Rules: test report save blocked when equipment is WC/out of date; lux meters annual, yoke lift check annual, light meters max 1 year, instrument classes in performance test with stated uncertainty. [WI-QC-002/008/007]
- **CalibrationSchedule** (monthly view, overdue notifications).

### 5.9 Welding and NDT personnel
- **WPS** (no., revision, process, base metal P-no., filler F-no./A-no., thickness range, position, preheat, interpass, PWHT, shielding gas, PQR FK, approval). **PQR** (no., test record, test results, code edition). **Welder** (ID/stamp, name), **WPQ/WQT** (welder, process, material, thickness, position, date, expiry/limits, test report), **WelderContinuityLog** (welder, process, date last welded; alert when > 6 months without welding in the process) - [src: WI-QC-010 final paragraph; new G-01; ASME IX QW-322].
- **WeldJoint** (ProductItem, joint no., weld map ref., WPS FK, welder FK, filler batch, preheat/interpass records, VT result, NDT ordered/results, repair count, PWHT). **WeldConsumable** (type, batch, storage/baking record) [new G-01].
- **WeldedPartInspectionForm** (the unnamed "فرم کنترل کیفیت قطعات جوشکاری شده") mapped to WeldJoint inspection.
- **NDTPersonnel** (name, method, level, scheme (ASNT SNT-TC-1A / ISO 9712), certification date, expiry, employer's written practice ref); rule from WI-QC-008 note: certificate valid until expiry even if the qualification procedure changes. [WI-QC-002/005/008; new G-03]
- **InspectorAuthorisation** (user, activities allowed, evidence, valid until). [new G-17]

### 5.10 Document control
- **Document** (code, type, title (fa/en), responsible unit, revision, issue date, status {draft, approved, effective, obsolete}, prepared/approved1/approved2/authorised role + signature, validity stamp, distribution list, retention class). [all procedures' cover and footer]
- **DocumentRevision** (document, revision no., page changed, previous revision no./date, change summary, effective date). [revision logs]
- **Distribution** (document, recipient role/unit, copy no., issued date, returned/destroyed date). [cover "توزیع نسخ"]
- **MasterList** view: effective revision per document; flags for code mismatches (C-05) and form numbering conflicts (C-01/C-06).
- **DrawingStamp** (drawing, revision, stamp AFC/AFP, stamped by Engineering manager, date); QC screen shows only latest stamped revision, from project folder. [WI-QC-003]
- **RecordRetention** (record type, retention years or permanent, owner, disposal). Seed: DCF permanent, inspection drawing permanent; others to be provided by the company. [WI-QC-003, WI-QC-009]

### 5.11 Audit, review, improvement
- **AuditProgramme** (year, scope by process/unit/standard clause, auditors, schedule, criteria). **AuditPlan**, **AuditChecklist** (items from ISO 9001 / Q1 / internal), **AuditFinding** (clause, grade, evidence, NCR/CAPA FK, due date, closure); report form FO-QA-018; 5S checklist FO-QA-049 as special audit (score 0-3, total % = sum(Xi) / (3n) x 100, threshold 75 %). [PR-QA-003 FO-QA-018, WI-QA-002; new G-15]
- **ManagementReview** (date, attendees, inputs by category (all inputs listed in PR-QA-001 + [new G-16] resource adequacy, supplier performance, risk-action effectiveness), decisions (each generates a CAPA via FO-QA-017), minutes FO-QA-022, distribution, follow-up). Frequency: at least yearly; second meeting optional. [PR-QA-001]
- **DataAnalysisReport** (period H1/H2, six report types from WI-QA-006 table, data registrar, analyst, main reviewer, distribution, 5-day comment window, decision {no action, corrective action, improvement}); automated Pareto. [WI-QA-006]
- **RiskRegister** (process, activity, risk, cause, effects, opportunity, S, O, D, RPN, level 1-3, control, plan B, responsible, effectiveness method, review date, FMEA link) and **PFMEA** with the 20 columns of 2.20. [PR-QA-002, WI-QA-005]
- **Objective / Programme** (FO-QA-020), **StakeholderRequirement** (FO-QA-048), **Committee/Meeting/Resolution** (WI-QA-004), **Suggestion** (WI-QA-001, FO-QA-031..035), **Survey** (FO-HR-001). Low priority for the first release.
- **CompetenceRecord** (FO-QA-039/040 skills identification chart).

### 5.12 Customer complaints, release, final data book
- **CustomerComplaint** (WI-IP source; fields: customer, product, WorkOrder, description, type, received date, analysis, NCR/CAPA link, lessons-learned field [PR-QA-003]), **CustomerRequirement** (FO-QA-044), **CustomerInspectionReport** (FO-QA-045), **ReturnedProduct** (replace deleted form; [new]).
- **ReleaseNote** (WorkOrder, products, ITP completion check (all H/W points cleared), open NCRs zero or concessions attached, test reports present, final data book complete, authorised by (QC manager - to be confirmed), date, client/TPI sign-off per ITP 6-5/6-6, certificate of conformity to MR & SPEC, goods exit permit (مجوز خروج کالا), packing list, loading control by QC with block right [WI-QC-014]).
- **FinalDataBook / MDR index** (WorkOrder, index of documents, source entity, status, revision): ITP and approvals, material certificates (EN 10204 type), NDT reports, welding records, dimensional reports, test reports (hydro, run, balance, performance), painting, NCR/concession list, calibration certificates of instruments used (derived), drawings as built, packing list, release note, certificate of conformity, "2-year spare part list and commissioning spares check" [WI-QC-004 6-3, 6-4].

### 5.13 Numbering rules (proposed because none are given)
Source documents give no numbering rules for inspection/test reports, NCRs or certificates; only document code format TYPE-DEPT-NNN-RR, equipment code INN-IST-NNN and production code as the retrieval key for inspection records (PR-QC-003 6.4). Proposed: `{TYPE}-{ProjectCode}-{Seq}` for reports, NCR `{ProjectCode}-NCR-{Seq}`, auto-generated and immutable; to be confirmed by the company (open question 6).

### 5.14 Workflow states summary
- Incoming: Received (quarantine) -> Under QC -> Accepted / Rejected / Rework / Emergency-released -> Permanent receipt. [PR-QC-003, WI-QC-014]
- NCR: Raised -> Classified (Major/Minor) -> Dispositioned -> (Concession) -> Rework -> Re-inspected -> Closed -> Effectiveness verified.
- Calibration: Due -> WC -> Calibrated -> In service; or NC.
- ITP activity: Planned -> Notified (W: 10/60 days) -> Executed -> Released by each party.

---

## 6. Open questions for the company

1. Which form codes are correct for calibration (FO-QC-007..011 or FO-QA-007..011), and which code is the official NCR (FO-QC-013, FO-QA-017, FO-QA-023)? Is there an FO-QC-012 or FO-QC-015 (UT report?)
2. Are there approved procedures (not in this set) for CAPA, internal audit, document control, quality records, traceability, calibration, supplier evaluation, welding (WPS/PQR/WPQ), hydro test, balancing, painting, packing?
3. Who is authorised to release a product (release note vs goods exit permit vs certificate of conformity)? Is client or TPI release required on every project?
4. Which codes and editions are contractually applicable (API 611, 612, 610, 614, ASME VIII, TEMA, ASME V edition)? Is the ISO 1940-1 grade 1.0 balancing requirement still the client's wording?
5. What are the acceptance limits for mechanical run (vibration, bearing temperature) and which stand/steam is used (factory or site)?
6. What numbering is wanted for test reports, NCRs, concessions, certificates and serial numbers? What retention periods apply (fill the blank in WI-QC-009)?
7. Is in-process control done by operators or by QC inspectors, and which activities are H points for INNCO?
8. Do pumps, condensers and ORC units have their own QCP/ITP, and is a pressure-vessel authorised inspector involved?
9. Is "Rai Dana" the system of record for warehouse/QC status, and should PMIS replace or integrate with it?
10. What are the intended calibration intervals per instrument class and the NDT personnel qualification scheme (SNT-TC-1A written practice or ISO 9712)?
11. Should the Technical Committee composition and quorum be fixed in the system? Who may approve a concession toward the client?
12. Should PMIS also host the non-quality QMS items (suggestion system, 5S, knowledge management, SWOT, stakeholder register) in release 1 or later?

---

## Appendix A - Seed data for ITP template (from WI-QC-004-01, as read from page images)

Party columns: **V** = INNCO (vendor), **TPI** = third party (GL), **C** = Client (NIOEC). Points: H hold, W witness, R document review, RA review and acceptance, SW spot witness.

**Table 1 - component tests (acceptance criteria column as printed; all remarks "Major Part")**
| No. | Component | Test | Acceptance | V | TPI | C |
|---|---|---|---|---|---|---|
| 1 | Blade | MT; visual | approved procedure; drawing | H | W | W |
| 2 | Disk | UT; MT on machined disk; visual | approved procedure; drawing | H | W | W |
| 3 | Shaft | UT (if repair occurred); MT; visual | approved procedure; drawing | H | W | W |
| 4 | Casing | Visual check of rough cast (MSS SP-55); MT; UT (if repair occurred) | MSS SP-55; approved procedure | H | W | W |
| 5 | Valve | MT; visual (MSS SP-55); UT (if repair occurred) | approved procedure; MSS SP-55 | H (all three) | MT W, visual R, UT W | MT W, visual R, UT W |

**Table 2 - stage activities** (V / TPI / C)
1 Material inspection
- 1-1 Material certificate (chemical composition and mechanical test: hardness, tensile) - ASME Sec II, ASTM - H / RA / RA - document: certificate EN 10204.
- 1-2 Casting material - ASME Sec II, ASTM - H / W / RA.
2 Inspection and test before fabrication
- 2-1 Sandblasting visual inspection - ISO 8501-02 and approved procedure - H / W / RA.
- 2-2 Painting: primer and top layers (thickness, adherence, RAL inspection) - SSPC / IPS-E-TP-100 / NIOEC SP-80-02 / ASTM D3359, D7091, E797 - H / H / W.
3 NDT
- 3-1 MT - ASTM E 709, ASME Sec V Art.7 - H / W / RA.
- 3-2 UT - ASME Sec V Art.5 - H / W / RA.
4 Inspection and test after fabrication
- 4-1 Repair (if any) - ASME Sec VIII D1 and Sec IX - H / W / W.
- 4-2 Cold clearance - cold clearance table - H / RA / RA.
- 4-3 Rotor run-out check - API 611 - H / H / H.
- 4-4 Rotor balancing - ISO 1940-1 grade 1.0 - H / H / H.
- 4-5 Outline dimensional check - outline drawing - H / H / R.
5 Inspection and test
- 5-1 Hydrostatic test - API 611, approved procedure - H / H / W.
- 5-2 Instrument check (site) - NEMA SM23 - H / H / W.
- 5-3 Mechanical running test (site), no load - API 611 - H / H / W.
- 5-4 Vibration and temperature check (site) - API 611 and NEMA - H / H / W.
- 5-5 Appearance and dimensional inspection - INNCO outline drawings - H / H / W.
- 5-6 Performance test (in the site, hot) - approved procedure - H / H / W.
6 Packing and delivery
- 6-1 Final inspection - General Arrangement drawing - H / H / W.
- 6-2 Packing and shipping - approved procedure - H / H / W.
- 6-3 Commissioning and 2-year spare part check - spare part list - H / H / W - document: packing list.
- 6-4 Final book - final book - H / RA / R.
- 6-5 Release note - (none) - R / H / R.
- 6-6 Certificate of conformity to MR and SPEC - (none) - H / R / R.

## Appendix B - Technical limits stated in the WIs (for validation rules in the software)

| Item | Limit | Source |
|---|---|---|
| PT part/penetrant temperature | 5-52 C | WI-QC-002 |
| PT dwell | maximum 10 min (as printed) | WI-QC-002 |
| PT interpretation window | 10-60 min after developer | WI-QC-002 |
| PT white light | >= 1000 lux; UV >= 100 (unit not printed) | WI-QC-002 |
| PT wash water | <= 43 C, <= 50 psi | WI-QC-002 |
| PT dark adaptation | >= 5 min | WI-QC-002 |
| Light meter calibration | <= 1 year | WI-QC-002 |
| MT suspension / ambient / delta | 43 C / 57 C / 14 C max | WI-QC-008 |
| MT concentration | 1.2-2.4 % visible, 0.1-0.4 % fluorescent; recheck <= 8 h | WI-QC-008 |
| MT yoke lift | >= 10 lb; check yearly and after repair | WI-QC-008 |
| MT relevant indication | > 1.5 mm | WI-QC-008 |
| Demagnetisation residual | +-3 gauss (+-2 per API 687 8.2.4.2) | WI-QC-008 |
| Mechanical run | 900 rpm start; 10 % steps; 4 h; log every 20 min; oil filtration 25 micron; overspeed 110 %, 3 trips, +-2 % | WI-QC-006 |
| Performance test | within 90 days of start-up under load; 2 h; instrument uncertainties as 2.9 | WI-QC-007 |
| W point notice | 10 days (Iran) / 60 days (abroad) | WI-QC-004 |
| Data analysis comment window | 5 days | WI-QA-006 |
| Management review | >= once a year | PR-QA-001 |
| Risk review | yearly | PR-QA-002 |
| PFMEA review | yearly; team <= 6 | WI-QA-005 |
| Calibration labels | NC, WC variants; only calibration officer handles | WI-QC-001 |
