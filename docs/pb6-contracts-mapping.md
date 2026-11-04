# PB6: mapping of the contracts system (پیوست ۶) and progress statements (پیوست ۷) onto the existing modules

The new tables extend what the PMIS already has; nothing was rebuilt. Table prefixes: `Ct*` = contracts system, `Ps*` = progress statements. Money is rial only (D7).

| Need of the appendix | Existing module that already does part of it | What PB6 adds |
|---|---|---|
| Deal request (cartable 1) | Sales proposal, project contract (`ContractEntity`), project-control terms (`PcContractTerms`) | `CtDealRequest` (+ `CtDealStep`, `CtHistory`): form kind (consultant / employer), deal level, chain of approval by level (small: commercial; medium: + finance manager; large and strategic: + CEO). A step may name the person who decides; he must hold the role. The maker never approves. A return opens a new round. |
| Tender run and winner (cartable 2) | Legacy `Tender`, purchase inquiry with quotes | `CtTenderRun` + `CtTenderParticipant`: method, guarantee, participants, disqualification with a reason. One person selects the winner (a reason is required when it is not the lowest offer), another confirms. |
| Contract draft and exchange (cartable 3) | Purchase order as supplier contract, FO-IP-040-04 print | `CtContractDraft`: rounds of exchange with the other party, comments, agreement, signing with a contract number, notification. A draft that rests on a tender needs a confirmed winner. |
| Changes cartable | `PurchaseOrderAddendum` (PB6 addenda of supplier contracts) | One list of the changes (`/contracts-system/changes`). |
| CBS linked to WBS | `PcWbsNode`, `PcActivity` (weight, percent) | `CtCbsItem` with the WBS node; progress of a CBS item = weighted percent of the activities under the node; claims beyond the earned value are warned about on the statement. |
| Progress statement (پیوست ۷ + company form) | `PaymentRequest` of type Progress, `NonConformance` | `PsStatement` (+ lines, previous payments, deductions). Commercial prepares; technical office, supervision and the project manager of the contract authorise; payment is requested through `IPayablesService` (its caps apply) and recorded after the treasury has paid. |
| Nonconformity deductions | NCR (`NonConformance`) | `PsDeduction` of kind Nonconformity, linked to the NCR. |
| Legal deductions | none | `PsLegalRate`: the system ships **no rates**. One person types a rate with its source, another verifies it; only verified rates are usable. |
| Cash flow, analytics | Payment terms of orders, payment requests | Monthly plan against actual, per order or contract; analytics by contract, period, status and CBS item. |
| Petty cash | Petty cash, settlements, payments (finance) | `FnPettyCashRequest`, `FnPettyCashSummary` (FO-AC-005-07) + lines with evidence rules; forms FO-AC-003-02 and FO-HR-025-02. |

Attachments (cheque images, payment slips, evidence) use the generic `RecordAttachment` layer with new owner types 27..35.
