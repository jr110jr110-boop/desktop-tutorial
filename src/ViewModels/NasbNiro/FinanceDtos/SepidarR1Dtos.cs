using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // R1t D6: the answer of the tax office through Sepidar, and the tie-out of a package to the ledger.

    /// <summary>
    /// What an acknowledgement import did. The tax status written on an invoice is a number:
    /// 0 = not sent, 1 = sent and waiting for the answer, 2 = accepted (the tax id is filled), 3 = rejected.
    /// </summary>
    public class OutboxAckResultDto
    {
        public int PackageId { get; set; }
        public string Number { get; set; }
        public FnOutboxStatus Status { get; set; }
        /// <summary>Rows read from the file.</summary>
        public int Rows { get; set; }
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        /// <summary>Rows that say the invoice is sent and still waiting.</summary>
        public int Pending { get; set; }
        /// <summary>Rows that repeated what was already recorded (nothing was written for them).</summary>
        public int Unchanged { get; set; }
        /// <summary>Invoices of the package that no row has answered yet (invoice packages only).</summary>
        public int Unanswered { get; set; }
        public string Message { get; set; }
    }

    public class OutboxTieOutRowDto
    {
        /// <summary>The account role: VatSales, Vat, InsurancePayable or PayrollTaxPayable.</summary>
        public string Key { get; set; }
        public string Label { get; set; }
        public string AccountCode { get; set; }
        public string AccountTitle { get; set; }
        /// <summary>The total in the package, as it was prepared.</summary>
        public decimal PackageAmount { get; set; }
        /// <summary>The movement of the account in the period that the package is about, in the direction that account normally grows (final and checked vouchers; cash settlements, opening and closing vouchers left out).</summary>
        public decimal LedgerAmount { get; set; }
        /// <summary>The part of the ledger movement that comes from the documents of the package (sales invoices or payroll runs).</summary>
        public decimal LedgerFromSource { get; set; }
        /// <summary>The part that comes from anything else (manual vouchers, asset sales ...).</summary>
        public decimal LedgerOther { get; set; }
        /// <summary>Payments and receipts that moved the account in the period: shown for information, not part of the comparison.</summary>
        public decimal SettlementMovement { get; set; }
        /// <summary>Ledger minus package.</summary>
        public decimal Difference { get; set; }
        /// <summary>The ledger part from the package's own documents minus package: zero means the package agrees with what the system posted for those documents.</summary>
        public decimal DifferenceFromSource { get; set; }
        public bool Ties { get; set; }
    }

    public class OutboxTieOutDto
    {
        public int PackageId { get; set; }
        public string Number { get; set; }
        public FnOutboxKind Kind { get; set; }
        public FnOutboxStatus Status { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        /// <summary>True when every row has no difference.</summary>
        public bool Ties { get; set; }
        public decimal TotalAbsoluteDifference { get; set; }
        public List<OutboxTieOutRowDto> Rows { get; set; } = new List<OutboxTieOutRowDto>();
        public List<string> Notes { get; set; } = new List<string>();
    }
}
