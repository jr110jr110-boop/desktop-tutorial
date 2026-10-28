# ProductionR1 data backfill (labour cost: pooled average rate)

Run this **once**, after the `ProductionR1` migration has been applied and **before the first cost run or month-end labour run of the new release**: start the new release with the
nightly jobs off (`Production:Jobs=false`, `Service:Jobs=false`), run the statements, then switch the jobs on. It touches only the rows that the previous release wrote.
All statements are plain SQL (SQL Server; they also run unchanged on SQLite, which is how they were tested), they change nothing but the new `LabourProtected` flags and the two new order
columns of step 5, and they can be repeated: a second run flags nothing more. Step 5 cannot be caught up later: once a month-end run has taken a waiting order, its labour is in the books at zero.

## What it is for

The previous release absorbed production labour at the employee's own hourly pay, per work order, WBS node and work centre, and hid the amount from most roles when only one or two
payroll-rated people stood behind it. The new release absorbs labour at a pooled average rate, so no new amount is one person's pay (see the rule at the top of `MfLabourService`
and `SvLabourService`). The rows written before it still hold personal-rate amounts. Step 1 to 4 flag them with `LabourProtected` so that the same readers that hide the
quarter-end true-up (vouchers, ledger, statements, drill-down, cost report, Sepidar events export) hide them from every role except PAYROLL, FINANCEMANAGER and CEO.
Step 5 makes the service orders that were still waiting for the old monthly posting joinable to the new month-end run.

## Before you start

1. Take a backup. Run the statements in one transaction and look at the counts before you commit.
2. The old release's own month-end run of the after-sales labour should have been done for every ended Persian month (it posted the `ServiceLabourMonth` rows that step 3 flags).
3. Check what there is to do (both queries are read-only):

```sql
-- cost runs written by the old release: none of their bookings carries AbsorbedLabour (the new cost run always sets it for a payroll-rated booking)
SELECT COUNT(*) AS OldCostRuns
FROM MfCostRuns r
WHERE NOT EXISTS (SELECT 1 FROM MfTimeBookings b WHERE b.CostRunId = r.Id AND b.AbsorbedLabour IS NOT NULL);

-- closed service orders whose labour the old release held back for its monthly posting and has not posted yet (SvOrderStatus.Closed = 4)
SELECT COUNT(*) AS WaitingServiceOrders
FROM SvServiceOrders
WHERE Deleted = 0 AND Status = 4 AND LabourPendingPeriod IS NOT NULL AND LabourAggregatedAt IS NULL AND LabourPayrollCost IS NULL;
```

## The rule the SQL copies

The old release called a group protected when the number of its employees paid through payroll (`PayrollEmployeeCount`) was 1 or 2 (`MfCostMask.IsProtected`: payroll-rated employees
greater than 0 and the smaller of employees and payroll-rated employees below 3, which is the same as 1 or 2). The cost run wrote one posting per day, work order, WBS node and work centre
with that count, one labour cost entry per posting (`SourceType = 'MfCostRun'`, `SourceNumber` = the run number, same work order, WBS node, day and amount) and one accounting event
per day (`SourceType = 'MfCostRun'`): a `WIP` debit line per posting (labour plus overhead, work order only) and one `LabourAbsorbed` credit line of the day (the labour total).
A run of the new release is recognised by its bookings: the new cost run stores `AbsorbedLabour` on every payroll-rated booking it posts, the old one never did, so the guard
`NOT EXISTS (booking of the run with AbsorbedLabour)` keeps every statement away from the new rows, also when it is repeated later.

The old after-sales close held back the labour of an order with 1 or 2 payroll-rated engineers and the old month-end run posted it as `ServiceLabourMonth` rows
(`SourceId` = the Persian month as yyyymm, `SourceNumber` = `SVL-yyyymm`): cost entries per contract and cost centre, and one event (warranty provision or goodwill expense debit, service labour
absorbed credit). A month is flagged when the orders it took (`LabourPendingPeriod` = the month, `LabourAggregatedAt` set, not deleted) had one or two distinct payroll-rated engineers with hours
(`COUNT(DISTINCT CASE WHEN RateFromPayroll = 1 AND Hours > 0 THEN EmployeeId END) BETWEEN 1 AND 2`). A month that has a row in `MfLabourPools` was closed by the new release, which
sets the flag itself, so the statements skip it.

## Step 1 - production labour cost entries

```sql
UPDATE FinCostEntries
SET LabourProtected = 1
WHERE Kind = 3                         -- CostKind.Labour
  AND SourceType = 'MfCostRun'
  AND LabourProtected = 0
  AND EXISTS (
        SELECT 1
        FROM MfCostPostings p
        JOIN MfCostRuns r ON r.Id = p.RunId
        WHERE r.Number = FinCostEntries.SourceNumber
          AND p.WorkOrderId = FinCostEntries.WorkOrderId
          AND COALESCE(p.WbsNodeId, 0) = COALESCE(FinCostEntries.WbsNodeId, 0)
          AND p.PostingDate = FinCostEntries.EntryDate
          AND p.LabourAmount = FinCostEntries.Amount
          AND p.PayrollEmployeeCount BETWEEN 1 AND 2
          AND NOT EXISTS (SELECT 1 FROM MfTimeBookings b WHERE b.CostRunId = r.Id AND b.AbsorbedLabour IS NOT NULL)
      );
```

## Step 2 - production event lines

The `WIP` line of a protected posting (it holds the labour plus the machine overhead of that posting, so the whole line is flagged) and the `LabourAbsorbed` credit of every day that
had a protected posting (it is the sum of the labour of the day).

```sql
UPDATE FinAccountingEventLines
SET LabourProtected = 1
WHERE AccountRole = 'WIP'
  AND LabourProtected = 0
  AND EXISTS (
        SELECT 1
        FROM FinAccountingEvents e
        JOIN MfCostRuns r ON r.Number = e.SourceNumber
        JOIN MfCostPostings p ON p.RunId = r.Id
        WHERE e.Id = FinAccountingEventLines.EventId
          AND e.SourceType = 'MfCostRun'
          AND p.WorkOrderId = FinAccountingEventLines.WorkOrderId
          AND p.PayrollEmployeeCount BETWEEN 1 AND 2
          AND p.LabourAmount + p.OverheadAmount = FinAccountingEventLines.Debit + FinAccountingEventLines.Credit
          AND NOT EXISTS (SELECT 1 FROM MfTimeBookings b WHERE b.CostRunId = r.Id AND b.AbsorbedLabour IS NOT NULL)
      );

UPDATE FinAccountingEventLines
SET LabourProtected = 1
WHERE AccountRole = 'LabourAbsorbed'
  AND LabourProtected = 0
  AND EXISTS (
        SELECT 1
        FROM FinAccountingEvents e
        JOIN MfCostRuns r ON r.Number = e.SourceNumber
        WHERE e.Id = FinAccountingEventLines.EventId
          AND e.SourceType = 'MfCostRun'
          AND EXISTS (SELECT 1 FROM MfCostPostings p WHERE p.RunId = r.Id AND p.PayrollEmployeeCount BETWEEN 1 AND 2)
          AND NOT EXISTS (SELECT 1 FROM MfTimeBookings b WHERE b.CostRunId = r.Id AND b.AbsorbedLabour IS NOT NULL)
      );
```

## Step 3 - after-sales: the old monthly posting

```sql
UPDATE FinCostEntries
SET LabourProtected = 1
WHERE SourceType = 'ServiceLabourMonth'
  AND LabourProtected = 0
  AND NOT EXISTS (SELECT 1 FROM MfLabourPools mp WHERE mp.Module = 1 AND mp.Period = FinCostEntries.SourceId)     -- MfLabourModule.Service = 1
  AND (SELECT COUNT(DISTINCT CASE WHEN t.RateFromPayroll = 1 AND t.Hours > 0 THEN t.EmployeeId END)
       FROM SvServiceOrders o
       JOIN SvTimeEntries t ON t.OrderId = o.Id
       WHERE o.Deleted = 0 AND o.LabourAggregatedAt IS NOT NULL AND o.LabourPendingPeriod = FinCostEntries.SourceId) BETWEEN 1 AND 2;

UPDATE FinAccountingEventLines
SET LabourProtected = 1
WHERE LabourProtected = 0
  AND EXISTS (
        SELECT 1
        FROM FinAccountingEvents e
        WHERE e.Id = FinAccountingEventLines.EventId
          AND e.SourceType = 'ServiceLabourMonth'
          AND NOT EXISTS (SELECT 1 FROM MfLabourPools mp WHERE mp.Module = 1 AND mp.Period = e.SourceId)
          AND (SELECT COUNT(DISTINCT CASE WHEN t.RateFromPayroll = 1 AND t.Hours > 0 THEN t.EmployeeId END)
               FROM SvServiceOrders o
               JOIN SvTimeEntries t ON t.OrderId = o.Id
               WHERE o.Deleted = 0 AND o.LabourAggregatedAt IS NOT NULL AND o.LabourPendingPeriod = e.SourceId) BETWEEN 1 AND 2
      );
```

## Step 4 - voucher lines of every flagged event line

The vouchers are built one voucher line per event line (`EventPostingService`), so a voucher line is the line of the same event with the same account role, work order and amounts.
Run this after steps 2 and 3; it also covers the lines the new release flagged itself (it changes nothing there).

```sql
UPDATE FnVoucherLines
SET LabourProtected = 1
WHERE LabourProtected = 0
  AND EXISTS (
        SELECT 1
        FROM FnVouchers v
        JOIN FinAccountingEventLines l ON l.EventId = v.AccountingEventId
        WHERE v.Id = FnVoucherLines.VoucherId
          AND l.LabourProtected = 1
          AND l.AccountRole = FnVoucherLines.SourceRole
          AND COALESCE(l.WorkOrderId, 0) = COALESCE(FnVoucherLines.WorkOrderId, 0)
          AND l.Debit = FnVoucherLines.Debit
          AND l.Credit = FnVoucherLines.Credit
      );
```

## Step 5 - closed service orders still waiting for the old monthly posting

The old close posted such an order's parts but not its labour, which was to come with the monthly posting. The new month-end run knows only orders with a payroll cost, so give the waiting order one:
the whole stored labour counts as the cost of its pool and nothing was absorbed (`LabourPooledAmount = 0`), so the first month-end run posts the labour as part of the company-level true-up of its month.
The standard-rate part of the order (hours of engineers without a payslip) is taken out first, because it is not payroll cost: set `@StandardRate` to the hourly labour rate of the after-sales
labour work centre that was in force when the orders closed (the work centre named in the after-sales settings; its `LabourRateRial`).

Run the `DECLARE` and the `UPDATE` in the same batch:

```tsql
DECLARE @StandardRate decimal(18, 2) = 0;     -- replace by the standard hourly labour rate of the after-sales labour work centre
```

```sql
UPDATE SvServiceOrders
SET LabourPooledAmount = 0,
    LabourPayrollCost = LabourAmount
        - COALESCE((SELECT SUM(t.Hours) FROM SvTimeEntries t
                    WHERE t.OrderId = SvServiceOrders.Id AND t.Hours > 0 AND COALESCE(t.RateFromPayroll, 0) = 0), 0) * @StandardRate
WHERE Deleted = 0
  AND Status = 4                                  -- SvOrderStatus.Closed
  AND LabourPendingPeriod IS NOT NULL
  AND LabourAggregatedAt IS NULL
  AND LabourPayrollCost IS NULL;
```

What happens next: at the end of the month the run takes these orders into the pool of their month like any other order. Their labour reaches the books as the pool's true-up line (labour rate variance
against the service labour absorbed), not as a debit on the warranty provision or the goodwill expense, and without the contract: finance may reclassify that by a manual voucher if it wants the old split.

## Check the result (read-only)

```sql
SELECT 'cost entries' AS What, COUNT(*) AS Flagged FROM FinCostEntries WHERE LabourProtected = 1
UNION ALL SELECT 'event lines', COUNT(*) FROM FinAccountingEventLines WHERE LabourProtected = 1
UNION ALL SELECT 'voucher lines', COUNT(*) FROM FnVoucherLines WHERE LabourProtected = 1;
```

Then open one flagged voucher as the accountant (FINANCIAL): its protected lines must be missing and the voucher must say that labour is hidden; the same voucher as FINANCEMANAGER shows every line.

## What this does not do

* **Over-masking.** Postings are matched to cost entries and event lines by run number, work order, WBS node, day and amount. Two postings of one work order, WBS node and day with the same
  amount but different work centres cannot be told apart, so both are flagged when either is protected. A flagged `WIP` line also hides the machine overhead inside it. A voucher line is flagged
  when it equals a flagged event line of its event. Nothing is ever flagged by mistake outside the old runs and months; the worst case is a line too many.
* **Cost-run and posting lists.** `production/cost/runs` and `production/cost/postings` no longer mask amounts (the new amounts are pooled), so they still show the personal-rate amount of an old posting to the roles
  that may read them (FINANCIAL, PRODUCTIONMANAGER, AUDITOR and PMO besides the payroll readers). The same holds for the labour amount of an after-sales order closed by the old release with one or two payroll-rated engineers. The backfill flags the books (cost entries, event lines,
  voucher lines), not these lists. If the live database holds such rows, ask for a read-side mask keyed on `MfCostRuns.LabourProtected` (runs) and `PayrollEmployeeCount` of those runs (postings), and
  on `LabourPayrollCost IS NULL AND LabourPayrollEmployeeCount BETWEEN 1 AND 2` (orders).
* **Old months have no pool rows.** The first month the new release closes starts the pooled rate; until then labour is absorbed at the standard rate (`Production:StandardLabourRate`, else the work-centre
  labour rate; after-sales: the labour work centre of its settings). Bookings the old cost run already posted keep their personal-rate cost entry and never enter a pool; they have no true-up.
* **No undo.** After the statements, a flag cannot be told from one the new release set itself, so the way back is the backup taken before them.
