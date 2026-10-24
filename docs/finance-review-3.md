# Independent review 3 of finance R2 + F3b + scope flags (commit 95bbfe6) — findings to fix (milestone R3)

Reviewer: independent (read-only, no build). Paths relative to src/Services/NasbNiro/Finance/ unless shown. Rial-only / one-entity scope respected: FX and intercompany logic itself not reviewed. Every fix needs a smoke check that FAILS on the old behaviour with exact numbers. Persian calendar only. Migration as needed (FinanceReviewFixes3).

## Status of review-2 findings (non-FX)

| # | Status | Note |
|---|---|---|
| 1 | Fixed in code, smoke missing | Bad debt reads all years incl. closing+opening (GlService.Collections.cs:82). True-up (GlService.Deferrals.cs:298) also counts Draft vouchers — align with bad debt (exclude drafts). No smoke across a year close. |
| 3 | Partly | Returns fixed and tested. Cancelled production receipts still break `TransferredOut` (new H4). |
| 4 | Partly | Cost side added (GlService.Costing.cs:485-509) but PoC work orders are now costed twice (C1) and absorbed overhead never reaches PoC cost of sales (C1). Smoke checks revenue only, not 70101/10520. |
| 5 | Fixed, with a reversal gap | Relief on delivery works and is tested (smoke 2684-2710). Cancelling the delivery does not restore the counters (H3). |
| 6 | Partly | Initiator recorded and excluded from finalising (smoke ok). Loan disbursement and instalments, LC margin/fee/pay and guarantee fees still credit the bank directly from a Checked auto voucher (counts in bank balances immediately) instead of the treasury approval flow. Reopen of a close-out (GlService.Costing.cs:384) still has creator 0. |
| 8 | Verified-fixed | No renumbering; opening voucher number 1 only when the year has no finals; one SaveChanges; audit row (GlService.cs:262-286). |
| 9 | Verified-fixed (no smoke) | LinkDetailsAsync + DetailResolver uses LinkedEntityId; comparison maps through FnDetailSepidarMap. Add the smoke from review 2. |
| 10 | Partly | Gate on PR and PO approval (ProcurementService.cs:310,839) with open commitments; not on payment approval. GateAsync fails OPEN if CheckAsync returns an error (GlService.Budget.cs:363). |
| 11 | Verified-fixed | Closed periods only; dimensions from line fields (SepidarFullExportService.cs:194,217-253). |
| 12 | Verified-fixed | Only Paid advances (PayablesService.cs:345); smoke r2 f12. |
| 13 | Verified-fixed (no new smoke) | Settlement uses the GRNI role (TreasuryService.Bank.cs:155); reconciliation excludes drafts (LedgerReportService.cs:416). |
| 14 | Fixed in code, smoke partial | Instalment expenses only the unaccrued part. Current-portion run has a tracker bug (M1). No smoke on 80202 total after accrual + instalment. |
| 15 | Verified-fixed | Only Final originals (GlService.Deferrals.cs:177). |
| 16 | Verified-fixed | Unique indexes (CompletenessConfigurationsM2.cs:126,136,161). F3b runs lack them (M9). |
| 18 | Verified-fixed | Warning returned; smoke r2 f18. |
| 19 | Verified-fixed | Codes on purchase rows, carry across years, sign validation (GlService.TaxCompleteness.cs:62-80, 98-99). |
| 20 | Verified-fixed | CSV/Excel/audit/file names Persian; smoke r2 f20. |
| 21 | Partly | FnPaymentLine (PayeeIban, amount), FnLoanInstallment and all F3b records (FnInsuranceClearance, FnBankGuarantee, FnSupplierNote, FnTermination, FnBonusRun, FnRetentionRelease) are not in DataLayer/Context/FinanceAuditTrail.cs:21-61. "CEO grants AUDITOR per period" not built (state it). |
| 22 | Verified-fixed | Cash-flow "Other" + exact sections, VAT carry exact, loan accrual exact, close-out FG/returns. New vacuous F3b checks listed under V. |
| 23 | Verified-fixed | Old path creates a draft treasury payment; BONUS vs ANNUAL_BONUS split; smoke r2 f23. |

## Scope flags (Finance:MultiEntity / Finance:ForeignCurrency)
Blocked correctly: currency/rate save, revaluation, FX settle, non-IRR LC/loan/bank, currency fields on any voucher line (manual, mass import, templates, Sepidar migration all go through BuildLinesAsync, GlService.cs:556), treasury currency amounts (TreasuryService.cs:541), new legal entity, pairs, intercompany. Rial paths not broken (smoke "scope:" block). Two holes — see H7 and M2.

## Critical

C1. **Percentage-of-completion work orders are expensed twice.** The PoC run credits WIP with ContractId only (GlService.Costing.cs:508); `BuildCostAsync` reads the WIP balance by WorkOrderId (GlService.Costing.cs:290) so it still sees the cost, and CloseWorkOrderAsync (no PoC check) moves it to 10503; CustomerDelivery then relieves 10503 to 70101 (CostingService.cs:361-381). Example: WO of PoC contract C, material 100 into WIP (WO+C). PoC run: Dr 70101 100 / Cr 10520 100. Close-out: Dr 10503 100 / Cr 10520 100. Delivery: Dr 70101 100 / Cr 10503 100. Cost of sales 200, WIP −100. Second defect in the same path: overhead absorption lines carry WorkOrderId but no ContractId (GlService.Costing.cs:246), so absorbed overhead (e.g. 30) never leaves WIP in the PoC run: margin overstated by 30 and WIP keeps 30 forever.
FIX: a WO whose contract is PoC is not closed to finished goods (refuse close-out; on delivery no FG relief) — or the PoC run credits WIP per work order (WorkOrderId + ContractId) so the WO balance is zero. Derive ContractId from the work order on every auto line that has WorkOrderId (AddAutoVoucherAsync), as EventPostingService.cs:188 does.
SMOKE: PoC contract with one WO: issue 100, absorb overhead 30; PoC run at 50% → 70101 +130 exactly, 10520 for the contract 0; close-out of that WO refused (or posts 0); delivery → 70101 still +130.

## High

H1. **Supplier credit notes do not reduce what may be paid.** Invoice payment limit is `invoice.TotalAmount` (Procurement/PayablesService.cs:341); posted FnSupplierNote is ignored. Invoice 1,100,000 (1,000,000+VAT), credit note 220,000 → ledger payable 880,000, payment request for 1,100,000 accepted and paid → supplier owes us 220,000 by mistake.
FIX: limit = TotalAmount − posted credit notes + posted debit notes on that invoice − earlier requests.
SMOKE: after the credit note, a request for 1,100,000 is refused; 880,000 accepted.

H2. **AP and bank reconciliations (and so the year-close gate) are broken by F3/F3b postings.** AP sub-ledger = approved invoices − paid requests (LedgerReportService.cs:442-446): supplier notes (Dr/Cr 30101) and retention releases (Cr 30101, GlService.F3b.Clearance.cs:126) are missing. Bank sub-ledger = posted events with PartyType BankAccount (LedgerReportService.cs:465): loan disbursement/instalments, LC margin/fee/pay, guarantee fees are direct auto vouchers, not events. Example: rial loan of 3,000,000 disbursed → bank ledger +3,000,000, sub-ledger 0 → difference 3,000,000; CloseYearAsync then needs an override, which also hides real differences.
FIX: AP sub-ledger adds posted supplier notes and retention releases (or compare to the per-document open items); bank sub-ledger includes the bank lines of these auto vouchers (or tag them PartyType/PartyId via a source list).
SMOKE: after the existing loan, LC, guarantee, credit-note and retention-release tests, reconciliation rows `ap` and `bank` are Ok with Difference 0.

H3. **Cancelling a delivery of a closed job loses the FG relief.** ReverseDocumentAsync (CostingService.cs:468-523) reverses the event but never decrements `DeliveredQuantity` / `RelievedAmount` (set at CostingService.cs:379-380). Close 2 units, FG 1,000; deliver 1 (relief 500); cancel (GL back: 10503 = 1,000); deliver 2 units again → take = min(2, 1) = 1, relief 500; 500 stays in 10503 forever, cost of sales short 500; reconciliation `closeOutHeld` = 0 while 10503 holds 500; reopen blocked (GlService.Costing.cs:376) though nothing is delivered. Same when the delivery event is Pending and becomes NotRequired.
FIX: store relief per delivery (document id, qty, amount); the reversal subtracts them.
SMOKE: close 2 / FG 1,000, deliver 1, cancel, deliver 2 → 10503 back to its start and 70101 +1,000 exactly.

H4. **Cancelled production receipt still counts as transferred.** `transferred` takes only vouchers of ProductionReceipt events or WorkOrderClose (GlService.Costing.cs:293-296); the reversal of a cancelled receipt is a Reversal event and is left out. Sheet 180 (material 100, labour 50, overhead 30), receipt 60 posted then cancelled: transferred = 60, FG = 120, false variance 60.
FIX: include Reversal events whose ReversesEventId is a ProductionReceipt event.
SMOKE: that scenario → FG 180, variance 0.

H5. **F3b balances read only the current fiscal year, so before the previous year is closed they miss everything brought forward.** LedgerYearAsync + `FiscalYearId == year.Id` in ProvisionBalanceAsync (GlService.F3b.Payroll.cs:38), retention held (GlService.F3b.Clearance.cs:73), supplier statement (GlService.F3b.Purchasing.cs:199), customer confirmation (GlService.F3b.Purchasing.cs:332), payables note (GlService.F3b.Notes.cs:83). Iranian books are closed months after year end. Example: 40201 = 500,000,000 at end-1404, 1404 not closed yet; termination on 1405/02/10 with severance 81,000,000 and 1405 accruals 20,000,000 → only 20,000,000 from the provision, 61,000,000 expensed again (80103), provision overstated 61,000,000. A confirmation letter in 1405/02 shows the customer only 1405 movements.
FIX: same rule as review-2 fix 1: all years of the entity up to the date, excluding drafts, including closing and opening vouchers (they cancel).
SMOKE: post in year N, do not close N, act in N+1: provision used, retention held, confirmation and statement balances equal the cumulative balance.

H6. **Bonus (عیدی) and termination net pay have no payment path.** Both credit 30201 (GlService.F3b.Payroll.cs:192, 339) but FnPaymentPurpose has only Supplier/PettyCash/Payroll-run/EmployeeLoan (Entities/Model/NasbNiro/Finance/LedgerEntities.cs:369) and the payroll purpose requires `TotalNet == run.TotalNet` (TreasuryService.cs:651). 30201 never clears, or is cleared by a manual voucher outside treasury/IBAN controls.
FIX: payment purposes BonusRun and Termination (amount = net, per-employee IBAN transfer file, same approve→post flow).
SMOKE: pay the bonus run 101,900,000 through treasury → 30201 back to its start.

H7. **Rial LC payment still multiplies by a typed rate** (GlService.Fx.cs:457-460). Rial LC 200,000,000, rate 1.1 entered → Dr 30101 220,000,000 / Cr bank 220,000,000: 20,000,000 overpaid, no difference booked (carryBase = settled).
FIX: when the LC currency is IRR (always, while ForeignCurrency is off) ignore/refuse any rate ≠ 1; settled = Amount.
SMOKE: rial LC pay with rate 1.1 refused (or posts exactly 200,000,000).

H8. **Annual bonus can be paid twice and is not capped.** (a) ANNUAL_BONUS payroll item (F2Seed.cs:87, Dr 30402) and the bonus run (GlService.F3b.Payroll.cs:313-347) can both pay the same year; no cross-check. (b) Statutory (verify with finance): the labour law caps عیدی at 90 days of minimum wage; the code uses `CapDays × MinimumDailyWage` only as the tax threshold and never caps the gross (GlService.F3b.Payroll.cs:239-243); the tax exemption for عیدی is a separate figure (1/12 of the annual salary exemption). Daily wage 10,000,000, cap 311,700,000: gross 600,000,000 paid instead of 311,700,000. Smoke "f3b bonus: gross 66,000,000 ... above 45,000,000 is taxed" fixes the current interpretation in place.
FIX: refuse the bonus run for employees with an ANNUAL_BONUS line in a posted run of that year (and vice versa); separate rate keys for the gross cap and the tax exemption, both prorated by days employed.
SMOKE: exact gross at the cap; double payment refused both ways.

H9. **Comparative inventory note shows the prior year as zero once it is closed.** InventoryByGroupAsync (GlService.F3b.Notes.cs:110) does not exclude the Closing voucher dated the prior year end. 10501 = 500,000,000 at end-1404 (closed) → prior column 0.
FIX: exclude FnVoucherType.Closing (and YearEndTemporaryClose) as the payables note does.
SMOKE: close a year with inventory, notes of the next year show the exact prior balance.

## Medium

M1. Current-portion tracker is set on a DRAFT (GlService.Loans.cs:375); deleting the draft (allowed, Type Normal: GlService.cs:648) leaves `CurrentPortionReclassified` = due, so the next run posts nothing and 40101/30501 stay mis-split (e.g. 1,200,000 never reclassified). FIX: derive the reclassified amount from final/checked LoanCurrentPortion vouchers, or reset on delete. SMOKE: run, delete draft, run again → same draft amount.
M2. Flag hole: with MultiEntity off the main entity can still change BaseCurrency/Jurisdiction while it has no vouchers (GlService.Entities.cs:36-63); with BaseCurrency "EUR" an IRR loan becomes "foreign" (GlService.Loans.cs:141-145) and posts currency lines and sets the account CurrencyAllowed — bypassing ForeignCurrency. FIX: while the flags are off, force IR / IRR on the main entity. SMOKE: saving the main entity with EUR is refused.
M3. Insurance clearance gate is weak: DocumentId is any GUID, not checked against stored files (GlService.F3b.Clearance.cs:59); a clearance can be edited/revoked/back-dated after it was used for a release with no audit (not in the audit list); only the performance retention is gated — the 5% insurance retention (InsuranceRetentionPayable), which the Social Security Act ties to the clearance, can be released by any voucher; retention deductions carry no ContractId (TreasuryService.cs:607) so the per-contract status always shows 0 held. FIX: verify the file exists, lock a used clearance, audit it, gate the insurance retention too, carry ContractId on deduction lines. SMOKE: fake GUID → not valid; edit of a used clearance refused.
M4. Termination: leave and notice pay go gross to 30201 (GlService.F3b.Payroll.cs:186-193); income tax and insurance on leave encashment are never booked (note text says "separately") → net payable overstated. FIX: compute tax/insurance on leave+notice (severance exempt) or require them as inputs; credit 30203/30202. SMOKE: exact tax line on a leave pay of 6,000,000.
M5. Non-recoverable VAT: on a capital line it goes to price variance 70108 (PayablesService.cs:244-248), not into the asset cost; a supplier credit note on such an invoice credits input VAT 10403 that was never debited (GlService.F3b.Purchasing.cs:163) and lowers deductible VAT in the return (TaxService.cs:150-157). Invoice 1,000,000 + VAT 100,000 all non-recoverable, credit note 100,000 + 10,000 → 10403 −10,000 and return −10,000. FIX: capitalise on capital lines; credit note follows the original invoice's recoverable share. SMOKE: both numbers.
M6. Payables ageing note treats the opening voucher as one invoice dated day 1 (GlService.F3b.Notes.cs:83-99): an invoice of 1404/09/01 shows as "Current" on 1405/01/15. Provisions note lists only FnProvision movements (GlService.F3b.Notes.cs:158), not 30402/30403/40201, 10309, 30404. FIX: age brought-forward items by their source date (carry the supplier open items) and build the provisions note from the ledger accounts.
M7. Direct cash flow misclassifies mixed vouchers (GlService.F3b.CashFlow.cs:100-115): every non-cash line of a voucher that touches cash is taken as a cash counterpart. Payroll voucher Dr 80101 1,000 / Cr 30203 200 / Cr bank 800 → OperatingCosts −1,000 and Taxes +200 (a "receipt"). Totals still tie. FIX: allocate the cash amount only to the lines on the opposite side, proportionally, or classify by the voucher's source type.
M8. Tax provision draft is created for the full amount on every run (GlService.TaxCompleteness.cs:137-150): two runs → two 500,000,000 drafts; both can be finalised. FIX: post the difference to the 30401 balance for the year; refuse while a draft exists.
M9. Races without DB constraints: two concurrent bonus runs for one year, two terminations for one employee, two retention releases each seeing the full releasable amount (checks are AnyAsync/Sum only). FIX: filtered unique indexes (FnBonusRun.Year where not cancelled; FnTermination.EmployeeId where not cancelled) and a row lock / concurrency token on the retention status.
M10. Payroll tax reconciliation: LedgerWithheld sums credits only and excludes Reversal vouchers (GlService.F3b.Payroll.cs:381-384); a reversed payroll run keeps its original credit → TiesToLedger false although both sides are right. FIX: net credits minus debits of reversal vouchers.
M11. Commitment gate fails open (GlService.Budget.cs:363: `if (!r.Success) return (true, null)`); not called on payment approval. FIX: fail closed in Block mode; add the payment gate.

## V. Vacuous smoke checks (would pass on wrong behaviour)
- 2651 direct cash flow: netChange = closing − opening, = indirect, = sum of rows — all identities of double entry. Assert exact rows (e.g. Loans receipts +3,000,000 from the rial loan, Suppliers payments = the known payment, Taxes 0 receipts).
- 2652 comparative: change == current − prior is an identity. Assert prior amounts equal the prior-year balance sheet lines exactly (e.g. 10101 prior = known value).
- 2655 notes: totals equal the sum of own rows; loan split equals outstanding by construction. Assert an exact prior inventory balance (catches H9) and an exact payable bucket.
- 2627 tax reconciliation: totals of own rows; PE1 only `>= 1046000`. Assert exact recomputed tax, difference and `tiesToLedger == true`.
- No smoke for review-2 fixes 1 (year close), 4 cost side, 9, 13, 14 (80202 total), 15, 19.

Top fixes in order: C1, H1, H2, H5, H6.
