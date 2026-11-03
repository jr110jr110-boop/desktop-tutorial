using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t D7: a loan, a letter of credit or a bank guarantee never moves money by itself. The module that records the facility drafts a treasury document for each cash leg
    /// (a payment when money leaves the bank, a receipt when it comes in); the finance manager approves it and a treasury user other than the person who recorded it settles it.
    /// Settling builds the ledger side of the leg (this interface) and treasury adds the bank side, so the bank book, the cash position and the reconciliation see every leg.
    /// </summary>
    public interface ICashLegService
    {
        /// <summary>The ledger side of a leg that treasury is settling, and the change of the facility record that the leg completes. Nothing is saved here.</summary>
        Task<CashLegSettlement> SettleAsync(CashLegRequest leg, CancellationToken cancellationToken);

        /// <summary>A pending leg is cancelled: the facility record goes back to the state before the leg was drafted. Nothing is saved here.</summary>
        Task ReleaseAsync(CashLegRequest leg, CancellationToken cancellationToken);
    }

    /// <summary>The treasury document of a cash leg, as the settlement sees it.</summary>
    public sealed class CashLegRequest
    {
        public string SourceType { get; set; }
        public int SourceId { get; set; }
        public string Action { get; set; }
        public string Data { get; set; }
        /// <summary>True when money comes into the bank account (a receipt), false when it leaves (a payment).</summary>
        public bool Inflow { get; set; }
        public decimal BankAmount { get; set; }
        public int BankAccountId { get; set; }
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; }
        /// <summary>The person who recorded the facility action (the creator of the treasury document).</summary>
        public int RecordedByUserId { get; set; }
    }

    public sealed class CashLegSettlement
    {
        public string Error { get; set; }
        public AccountingEventType EventType { get; set; } = AccountingEventType.FacilityLeg;
        public string Text { get; set; }
        /// <summary>Every line of the event except the bank line (treasury adds that one).</summary>
        public List<AccountingEventLine> CounterLines { get; } = new List<AccountingEventLine>();

        public static CashLegSettlement Fail(string error) => new CashLegSettlement { Error = error };
    }

    /// <summary>The names under which treasury documents point back at the facility record.</summary>
    public static class CashLegSources
    {
        public const string Loan = "Loan";
        public const string LetterOfCredit = "LetterOfCredit";
        public const string BankGuarantee = "BankGuarantee";

        public static bool IsFacility(string sourceType) => sourceType == Loan || sourceType == LetterOfCredit || sourceType == BankGuarantee;
    }
}
