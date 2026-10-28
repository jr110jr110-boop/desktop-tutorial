# ProductionR1 data backfill (labour cost: standard rate, one gated variance a month)

Run this **once**, after the `ProductionR1` migration has been applied and **before the first cost run or month-end labour run of the new release**: start the new release with the
nightly jobs off (`Production:Jobs=false`, `Service:Jobs=false`), run the statements, then switch the jobs on. It touches only the rows that the previous release wrote.
All statements are plain SQL (SQL Server; they also run unchanged on SQLite, which is how they were tested), they change nothing but the new `LabourProtected` flags and the two new order
columns of step 4, and they can be repeated: a second run changes nothing more. Step 4 cannot be caught up later: until it has been run the month-end run of the after-sales labour refuses
(it names the waiting orders and their months, and the nightly run skips and logs), and once a month-end run has taken an order, its labour is in the books.

## What it is for

The previous release absorbed production labour at the employee's own hourly pay, per work order, WBS node and work centre, and hid the amount from most roles when only one or two
payroll-rated people stood behind it. The new release absorbs labour at a **standard rate that finance sets and that is not derived from anybody's pay** (hours x the labour rate of the
work centre; see the rule at the top of `MfLabourService` and `SvLabourService`); the difference to the payroll cost of the payroll-rated people is one company-level variance line a module
and month, published only when at least five payroll-rated people with hours stand behind it and nobody dominates it. No new amount is one person's pay. The rows written before it still hold
personal-rate amounts. The statements below flag the **cost entries** and the **ledger lines of the old after-sales month postings** with `LabourProtected`, so that the readers that hide
the quarter-end variance (vouchers, ledger, statements, drill-down, budget, consolidation, cost report, both Sepidar exports) hide them from every role except PAYROLL, FINANCEMANAGER and CEO,
and convert the after-sales orders that still wait for the old monthly posting. The remaining old rows are covered by **read-side masks of the new release** (see "What covers the rest").

## Before you start

1. Take a backup. Run the statements in one transaction and look at the counts before you commit.
2. The old release's own month-end run of the after-sales labour should have been done for every ended Persian month (it posted the `ServiceLabourMonth` rows that step 2 and 3 flag); orders it
   still holds back are converted by step 4.
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

4. Two values for the batch in which you run steps 2 and 4 (put the two lines at the top of that batch; `DECLARE` is valid for one batch only):

```tsql
DECLARE @Center nvarchar(64) = N'AFTERSALES';      -- the after-sales cost centre code: setting CostCenterCode of the after-sales settings (default AFTERSALES)
DECLARE @StandardRate decimal(18, 2) = 0;          -- the standard hourly labour rate of the after-sales labour work centre (its LabourRateRial) in force when the waiting orders closed
```

## The rule the SQL copies

The old release called a group protected when the number of its employees paid through payroll (`PayrollEmployeeCount`) was 1 or 2 (`MfCostMask.IsProtected`: payroll-rated employees
greater than 0 and the smaller of employees and payroll-rated employees below 3; the payroll-rated employees are a subset of the employees, so this is the same as 1 or 2). The cost run wrote one posting per day, work order, WBS node and work centre
with that count and one labour cost entry per posting (`SourceType = 'MfCostRun'`, `SourceNumber` = the run number, same work order, WBS node, day and amount).
A run of the new release is recognised by its bookings: the new cost run stores `AbsorbedLabour` on every payroll-rated booking it posts, the old one never did, so the guard
`NOT EXISTS (booking of the run with AbsorbedLabour)` keeps the statement away from the new rows, also when it is repeated later.

The old after-sales close held back the labour of an order with 1 or 2 payroll-rated engineers and the old month-end run posted it as `ServiceLabourMonth` rows
(`SourceId` = the Persian month as yyyymm, `SourceNumber` = `SVL-yyyymm`) for the orders of the month (`LabourPendingPeriod` = the month, `LabourAggregatedAt` set, not deleted):

* **cost entries**, one per contract and cost centre; the share of a contract with 1 or 2 payroll-rated engineers had been merged into the entry without a contract of the same cost centre. An entry is
  tested by the payroll-rated engineers with hours on **the orders that feed it** (the orders of its contract and cost centre; for the entry without a contract, the orders without a contract plus
  the orders of the contracts whose own share was too small), not by the people of the whole month;
* **one event**: a debit of the warranty provision (warranty orders) and/or of the goodwill expense (goodwill orders), a credit of the service labour absorbed. Its credit line (and a merged debit line)
  is the total of the month's warranty and goodwill orders; a split month shows each kind on its own debit line. The event is tested by the engineers of the **warranty and goodwill orders together**, and, when it
  is split, by those of **each kind**: if any of the groups has 1 or 2 payroll-rated engineers, **all lines of the event are flagged** (a debit and a credit of the same total, so the books of a reader who does not see them still balance).

Orders with a labour amount of zero add no amount to any entry and are not counted. A month that has a row in `MfLabourPools` was closed by the new release, which sets the flags itself, so the statements skip it.

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

## Step 2 - after-sales: the old monthly posting (cost entries and event lines)

Needs `@Center` (see "Before you start"). The first statement flags the cost entries, each tested by the engineers of the orders that feed it; the second flags every line of an event whose
groups (all warranty and goodwill orders of the month; and, for a split month, each kind) have 1 or 2 payroll-rated engineers.

```sql
UPDATE FinCostEntries
SET LabourProtected = 1
WHERE SourceType = 'ServiceLabourMonth'
  AND LabourProtected = 0
  AND NOT EXISTS (SELECT 1 FROM MfLabourPools mp WHERE mp.Module = 1 AND mp.Period = FinCostEntries.SourceId)     -- MfLabourModule.Service = 1
  AND (
        -- an entry of one contract and cost centre: the engineers on the orders of that contract and cost centre
        (FinCostEntries.ContractId IS NOT NULL
         AND (SELECT COUNT(DISTINCT CASE WHEN t.RateFromPayroll = 1 AND t.Hours > 0 THEN t.EmployeeId END)
              FROM SvServiceOrders o
              JOIN SvTimeEntries t ON t.OrderId = o.Id
              WHERE o.Deleted = 0 AND o.LabourAggregatedAt IS NOT NULL AND o.LabourPendingPeriod = FinCostEntries.SourceId AND o.LabourAmount <> 0
                AND o.ContractId = FinCostEntries.ContractId
                AND COALESCE(o.CostCenterCode, @Center) = FinCostEntries.CostCenter) BETWEEN 1 AND 2)
        OR
        -- the entry without a contract of a cost centre: the orders without a contract, and the orders of a contract whose own share was too small to stand alone
        (FinCostEntries.ContractId IS NULL
         AND (SELECT COUNT(DISTINCT CASE WHEN t.RateFromPayroll = 1 AND t.Hours > 0 THEN t.EmployeeId END)
              FROM SvServiceOrders o
              JOIN SvTimeEntries t ON t.OrderId = o.Id
              WHERE o.Deleted = 0 AND o.LabourAggregatedAt IS NOT NULL AND o.LabourPendingPeriod = FinCostEntries.SourceId AND o.LabourAmount <> 0
                AND COALESCE(o.CostCenterCode, @Center) = FinCostEntries.CostCenter
                AND (o.ContractId IS NULL
                     OR (SELECT COUNT(DISTINCT CASE WHEN t2.RateFromPayroll = 1 AND t2.Hours > 0 THEN t2.EmployeeId END)
                         FROM SvServiceOrders o2
                         JOIN SvTimeEntries t2 ON t2.OrderId = o2.Id
                         WHERE o2.Deleted = 0 AND o2.LabourAggregatedAt IS NOT NULL AND o2.LabourPendingPeriod = o.LabourPendingPeriod AND o2.LabourAmount <> 0
                           AND o2.ContractId = o.ContractId
                           AND COALESCE(o2.CostCenterCode, @Center) = COALESCE(o.CostCenterCode, @Center)) BETWEEN 1 AND 2)) BETWEEN 1 AND 2)
      );

UPDATE FinAccountingEventLines
SET LabourProtected = 1
WHERE LabourProtected = 0
  AND EXISTS (
        SELECT 1
        FROM FinAccountingEvents e
        WHERE e.Id = FinAccountingEventLines.EventId
          AND e.SourceType = 'ServiceLabourMonth'
          AND NOT EXISTS (SELECT 1 FROM MfLabourPools mp WHERE mp.Module = 1 AND mp.Period = e.SourceId)
          AND (
                -- the warranty and goodwill orders of the month together (SvWarrantyDecision.Warranty = 1, Goodwill = 3): the credit line and a merged debit line are their total
                (SELECT COUNT(DISTINCT CASE WHEN t.RateFromPayroll = 1 AND t.Hours > 0 THEN t.EmployeeId END)
                 FROM SvServiceOrders o
                 JOIN SvTimeEntries t ON t.OrderId = o.Id
                 WHERE o.Deleted = 0 AND o.LabourAggregatedAt IS NOT NULL AND o.LabourPendingPeriod = e.SourceId AND o.LabourAmount <> 0 AND o.Decision IN (1, 3)) BETWEEN 1 AND 2
                OR
                -- a split month shows each kind on its own debit line: either kind alone may be a small group
                (EXISTS (SELECT 1 FROM FinAccountingEventLines a WHERE a.EventId = e.Id AND a.AccountRole = 'WarrantyProvision' AND a.Debit > 0)
                 AND EXISTS (SELECT 1 FROM FinAccountingEventLines b WHERE b.EventId = e.Id AND b.AccountRole = 'WarrantyExpense' AND b.Debit > 0)
                 AND ((SELECT COUNT(DISTINCT CASE WHEN t.RateFromPayroll = 1 AND t.Hours > 0 THEN t.EmployeeId END)
                       FROM SvServiceOrders o
                       JOIN SvTimeEntries t ON t.OrderId = o.Id
                       WHERE o.Deleted = 0 AND o.LabourAggregatedAt IS NOT NULL AND o.LabourPendingPeriod = e.SourceId AND o.LabourAmount <> 0 AND o.Decision = 1) BETWEEN 1 AND 2
                      OR (SELECT COUNT(DISTINCT CASE WHEN t.RateFromPayroll = 1 AND t.Hours > 0 THEN t.EmployeeId END)
                          FROM SvServiceOrders o
                          JOIN SvTimeEntries t ON t.OrderId = o.Id
                          WHERE o.Deleted = 0 AND o.LabourAggregatedAt IS NOT NULL AND o.LabourPendingPeriod = e.SourceId AND o.LabourAmount <> 0 AND o.Decision = 3) BETWEEN 1 AND 2))
              )
      );
```

## Step 3 - voucher lines of the flagged after-sales events

A voucher is built from its event, one voucher line per event line, so when an event was flagged in step 2 **all lines of its voucher** are flagged: the flagged debit equals the flagged credit
and the vouchers of a reader who does not see them stay balanced. This step is limited to the events of the old after-sales month postings.

```sql
UPDATE FnVoucherLines
SET LabourProtected = 1
WHERE LabourProtected = 0
  AND EXISTS (
        SELECT 1
        FROM FnVouchers v
        JOIN FinAccountingEvents e ON e.Id = v.AccountingEventId
        WHERE v.Id = FnVoucherLines.VoucherId
          AND e.SourceType = 'ServiceLabourMonth'
          AND EXISTS (SELECT 1 FROM FinAccountingEventLines l WHERE l.EventId = e.Id AND l.LabourProtected = 1)
      );
```

## Step 4 - closed service orders still waiting for the old monthly posting

The old close posted such an order's parts but not its labour, which was to come with the monthly posting; the new month-end run knows only orders that carry a payroll cost, so each waiting order gets one.
The stored labour of the order is the hours of the payroll-rated engineers at their own pay plus the hours of the engineers without a payslip at the standard rate. The second part, S (the hours of the
engineers without a payslip x `@StandardRate`), is not payroll cost: it is taken out of `LabourPayrollCost` and **kept** as `LabourPooledAmount = -S` (an amount of zero or less is also what keeps
the read-side mask on the order, see below), so the whole stored labour reaches the books with the variance line of its month (payroll cost - (-S) = the stored labour) and the first run of the month
takes the order like any other.

Needs `@StandardRate` (see "Before you start"), in the same batch:

```sql
UPDATE SvServiceOrders
SET LabourPayrollCost = LabourAmount
        - COALESCE((SELECT SUM(t.Hours) FROM SvTimeEntries t
                    WHERE t.OrderId = SvServiceOrders.Id AND t.Hours > 0 AND COALESCE(t.RateFromPayroll, 0) = 0), 0) * @StandardRate,
    LabourPooledAmount = -(COALESCE((SELECT SUM(t.Hours) FROM SvTimeEntries t
                                     WHERE t.OrderId = SvServiceOrders.Id AND t.Hours > 0 AND COALESCE(t.RateFromPayroll, 0) = 0), 0) * @StandardRate)
WHERE Deleted = 0
  AND Status = 4                                  -- SvOrderStatus.Closed
  AND LabourPendingPeriod IS NOT NULL
  AND LabourAggregatedAt IS NULL
  AND LabourPayrollCost IS NULL;
```

What happens next: at the end of the month the run takes these orders into the pool of their month like any other order (a month that does not pass the publication gate is carried like any other). Their labour reaches
the books as the month's variance line (labour rate variance against the service labour absorbed), not as a debit on the warranty provision or the goodwill expense, and without the contract: finance may
reclassify that by a manual voucher if it wants the old split.

## Check the result (read-only)

```sql
SELECT 'cost entries' AS What, COUNT(*) AS Flagged FROM FinCostEntries WHERE LabourProtected = 1
UNION ALL SELECT 'event lines', COUNT(*) FROM FinAccountingEventLines WHERE LabourProtected = 1
UNION ALL SELECT 'voucher lines', COUNT(*) FROM FnVoucherLines WHERE LabourProtected = 1;
```

Then open one flagged after-sales voucher as the accountant (FINANCIAL): its protected lines must be missing and the voucher must say that labour is hidden; the same voucher as FINANCEMANAGER shows every line.

## What covers the rest (read-side masks of the new release)

The ledger lines of the old **production** cost runs are not flagged by any statement: the `WIP` debit of a posting holds its labour plus the machine overhead and the daily `LabourAbsorbed`
credit is the total of the labour of the day for every posting, protected or not, so flagging them would leave a hidden debit that is not equal to the hidden credit, and a reader who does not see
the flagged lines would meet vouchers that do not balance. The amounts of such rows are hidden where the new release reads them, by markers that only old rows have (a row of the new release never trips them):

* **runs** (`production/cost/runs`): a run with `MfCostRuns.LabourProtected = 1` shows no labour amount to a reader outside PAYROLL, FINANCEMANAGER and CEO;
* **postings** (`production/cost/postings`): a posting of such a run with 1 or 2 payroll-rated employees shows none;
* **cost sheet**: `TransferredOut` of a work order with flagged labour is hidden together with the rest of its cost sheet;
* **after-sales orders**: an order with no pooled amount, or one of zero or less (the converted orders of step 4), and with 1 or 2 payroll-rated engineers is `legacyProtected`: its labour and total cost are
  zero for those readers, in the order list and card, the service dashboard and KPIs, and the CEO dashboard (the helpers `MfLegacyLabour.IsProtected` and `SvLegacyLabour.IsProtected`).

## What this does not do

* **Over-masking.** Postings are matched to cost entries by run number, work order, WBS node, day and amount. Two postings of one work order, WBS node and day with the same
  amount but different work centres cannot be told apart, so both are flagged when either is protected. Nothing is ever flagged by mistake outside the old runs and months; the worst case is a line too many.
* **The ledger lines of old production runs stay visible** (see above). A reader of the ledger can still divide the `WIP` line of a small posting by its hours. If the company wants them hidden as well,
  the finance manager posts a one-off reclassification voucher (a flagged pair) and the old days are left as they are; the statements do not do it for the reason above.
* **Old months have no pool rows.** The first month the new release closes starts the pools. Bookings the old cost run already posted keep their personal-rate cost entry and never enter a pool; they have no
  variance. An after-sales order the old release posted at its close (not held back) is not touched.
* **No undo.** After the statements, a flag cannot be told from one the new release set itself, so the way back is the backup taken before them.
