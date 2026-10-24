# Sales, proposals and after-sales service, release S1 — backend brief

Ground rules, CI loop and design principles: as in /home/claude/innco/docs/quality-backend-brief.md — binding. Prefix `Sl` (sales) and `Sv` (service), routes `api/v1/sales` and `api/v1/service`, sequence kinds 501–530, constants `SalesRoles`. Migration `SalesService`.

## Existing code to reuse
- Customer master: `FnCustomer` (finance F1) — becomes THE customer master (extend it; do not use Provider or the free-text Employer). Add a nullable `CustomerId` FK to `ContractEntity` (side table if cleaner: `SlContractCustomer`) and to `Tender`; a one-time matcher links existing contracts/tenders by Employer/Company text (exact normalised match; unmatched listed for a human).
- `Tender` (+ files, guaranties), `PcGuaranteeInfo`, `PcContractReview` (ISO 8.2.3), `PcContractTerms`, `PcPaymentMilestone`, `FnSalesInvoice`, `CustomerComplaint` and `Capa` (quality), `SerialUnit`/`AssemblyLink` (as-built), `CustomerDelivery` document, Equipment templates (BOM + processes), ItemCost / last PO prices, routing standards (production P1).

## Roles ★
`SALES` 24 "فروش و بازاریابی", `SERVICE` 25 "خدمات پس از فروش". Claims 2400+/2500+.

## 1. Customers and contacts (CRM basic)
- Extend `FnCustomer` (side table `SlCustomerProfile`): type {EndUser, EPC, Distributor, Government}, industry {Oil&Gas, Petrochemical, Power, Steel, Sugar, Other}, country, city, status {Prospect, Active, Inactive}, owner (sales user), credit limit (finance sets), payment terms days.
- `SlContact` (customer, name, job title, phone, email, isPrimary), `SlSite` (customer, title, address, country, coordinates text).
- `SlActivity` (customer, contact, kind {Call, Meeting, Email, Visit, Letter}, date, subject, notes, next action date, links to inquiry/proposal/service request).

## 2. Inquiry → bid decision → proposal → result
- `SlInquiry` (RFQ register: number, received date, customer, contact, channel, client reference, title, product type {SteamTurbine, OrcUnit, Condenser, Pump, LubeOilSystem, Valve, SpareParts, Service, Other}, quantity, required delivery, bid due date, documents (attachments), status {Received, UnderReview, NoBid, Bidding, Submitted, Won, Lost, Cancelled}).
- Bid/no-bid gate `SlBidDecision`: checklist (technical capability, capacity at required time (reads production load), commercial risk, payment terms, guarantees needed, sanctions/export check flag, margin expectation), decision by CEO ★ (SALES prepares). No-bid needs reason.
- On Bidding, the system drafts the `Tender` record (legacy screens keep working) and links it.
- `SlEstimate` (cost estimate, revisions): lines from an equipment template (BOM lines × quantity × unit cost: ItemCost average → last PO price → manual, source shown per line), labour hours from routing standards × work-centre rates, engineering hours × rate (input), subcontract, packing/transport, guarantees cost (rate × amount × months), financing cost, overhead %, contingency %, risk items, currency and exchange rate (input), target margin % → price. All percentages are inputs (no seeded values). Price below cost + overhead requires CEO approval.
- `SlProposal` (number, revision, inquiry, estimate revision, technical proposal document, commercial proposal document, validity days, price, currency, delivery weeks, payment terms text, guarantees required, deviations list, status {Draft, Approved, Submitted, Revised, Won, Lost, Cancelled}); approval: SALES manager ★ (= SALES + CEO above a value threshold set in settings — no seeded threshold; default "CEO approves all" until set).
- Result: Won/Lost with reason code, competitor and competitor price (optional), lessons note. Win/loss statistics per product type and customer.
- **Won → contract**: endpoint converts proposal to `ContractEntity` (+ `PcContractTerms` from proposal: value, currency, VAT %, advance %, retention %, penalties, client review days) + customer link + `ContractEquipment` lines from the estimate's equipment templates + drafts `PcContractReview` (Kind from product) and `PcProjectInitiation` — existing project control then takes over. Humans approve the contract review as today.

## 3. Spare parts and small sales
- `SlPriceList` (currency, valid from/to, lines: item, unit price, min qty) — prices are inputs; the system suggests cost-plus from ItemCost with a markup input.
- `SlQuotation` (lightweight, for spare parts/services: customer, lines item/qty/price/discount, validity) → `SlSalesOrder` (status {Draft, Confirmed, PartlyDelivered, Delivered, Invoiced, Closed, Cancelled}, lines with reserved qty) → delivery via existing `CustomerDelivery` document (allow `SalesOrderId` as alternative to `ContractId`; QC loading approval rule unchanged) → `FnSalesInvoice` drafted by the system on delivery (finance approves). Credit-limit check on confirm (customer open receivables + this order > limit → finance manager approval).
- Stock reservation per sales order line (reduces "available" in the procurement/production availability checks).

## 4. After-sales service
- `SvInstalledUnit` (installed base): serial unit (SerialUnit of the delivered product), customer, site, work order/contract, product type, tag number at site, commissioning date, warranty start/end (from contract terms: warranty months input or the guarantee's end), running hours (updated by service visits), status {InWarranty, OutOfWarranty, UnderLtsa, Decommissioned}, as-built configuration = AssemblyLink tree (read through trace API), spare parts recommendation list (from the MDR "spare parts list" document of the work order + manual lines).
- Auto-created when the PcShipment of a work order reaches HandoverReceived (system draft, SERVICE confirms site/tag).
- `SvServiceRequest` (ticket: number, customer, contact, unit, channel, received at, description, kind {Breakdown, Inspection, Commissioning, Overhaul, SpareParts, Technical query, Complaint}, priority, response due (SLA hours by priority — table, no seeded values; default none), status {Open, Assigned, InProgress, WaitingCustomer, Resolved, Closed}). If kind Complaint or a product nonconformity is found → the system creates the quality `CustomerComplaint` (existing) linked both ways.
- `SvServiceOrder` (field or workshop job: request, unit, engineers (users/employees), planned dates, travel, tasks, status, warranty decision {Warranty, Billable, Goodwill} — Warranty only if the unit is InWarranty and the cause is not misuse (SERVICE manager decides; CEO for Goodwill), time sheets (`SvTimeEntry`: employee, date, hours, kind {Travel, Work, Waiting}), parts used (existing Issue documents referencing the service order + customer delivery for parts left at site), measurements (vibration, temperatures, pressures as free rows with unit), service report document signed by the customer (attachment), findings and recommendations). Closing requires the signed report attachment.
- Costs: time × employee hourly cost + parts → `CostEntry` with cost centre "after-sales" and the contract; Warranty → accounting event to the warranty provision role (finance adds account mapping: provision 30404 "ذخیره تضمین محصولات" ★ and expense 80109 ★ — add to the chart seed); Billable → `FnSalesInvoice` drafted.
- `SvContract` (LTSA / service agreement): customer, units, period, scope, visit frequency, price; hosted service drafts visits as service orders when due (−30 days ★).
- KPIs: first response time, resolution time, repeat failures per unit, warranty cost per product type, MTBF from field failures (installed base).

## 5. Customer satisfaction (ISO 9001 9.1.2)
- `SlSurvey` (customer, contract/service order, sent date, questions with 1–5 scores, comments); hosted service drafts a survey after HandoverReceived (+30 days ★) and after each closed service order; results feed the quality management review input (existing ManagementReview: add a read of the average score).

## 6. Dashboards
Sales dashboard: pipeline value by stage and product, hit rate, proposals due this week, quotations expiring; service dashboard: open requests by priority, SLA breaches, units in warranty, warranty cost YTD; CEO dashboard additions + alerts (bid due in 3 days not submitted, SLA breach, proposal below-cost waiting).

## Tests (smoke section "sales and service")
Customer + contact; inquiry → bid decision (SALES cannot approve as CEO) → estimate from an equipment template with exact sum (test unit costs) → proposal approve → Won → contract created with terms and contract equipment and a drafted contract review; legacy contract matcher links by employer text; spare-parts quotation → sales order → credit limit block → delivery → invoice drafted with exact totals; installed unit created on shipment handover; service request (complaint) creates CustomerComplaint; warranty service order cannot be Warranty for an out-of-warranty unit; close without signed report rejected; warranty cost posted to provision role; LTSA visit drafted; survey drafted; dashboards present.
