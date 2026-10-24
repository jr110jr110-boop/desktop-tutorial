using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Finance
{
    // Finance release F3 (completeness against Sepidar / Hamkaran class systems). Tables start with "Fn".
    // Milestone 1: legal entities, audit trail, period-close checklist, voucher controls, migration from Sepidar and the parallel run.

    // ------------------------------------------------------------------------------------------ legal entities

    /// <summary>A legal entity of the group (the Iranian company now, a German one later). The first row is the main company.</summary>
    public class FnLegalEntity
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        /// <summary>IR or DE.</summary>
        public string Jurisdiction { get; set; }
        public string BaseCurrency { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    /// <summary>Per-user settings (for example the default legal entity).</summary>
    public class FnUserSetting
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>Maps two entities' accounts that mirror each other (due from / due to).</summary>
    public class FnIntercompanyPair
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int EntityAId { get; set; }
        public FnLegalEntity EntityA { get; set; }
        public int EntityBId { get; set; }
        public FnLegalEntity EntityB { get; set; }
        /// <summary>Account of entity A holding what B owes (or A owes).</summary>
        public int AccountAId { get; set; }
        public FnAccount AccountA { get; set; }
        public int AccountBId { get; set; }
        public FnAccount AccountB { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>One intercompany posting: two mirrored accounting events, one in each entity.</summary>
    public class FnIntercompanyPost
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int PairId { get; set; }
        public FnIntercompanyPair Pair { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        /// <summary>The amount in the currency of entity A / entity B (equal to Amount when the currencies are equal); Rate = units of the other currency per one unit of the charger's.</summary>
        public decimal AmountA { get; set; }
        public decimal AmountB { get; set; }
        public decimal Rate { get; set; } = 1m;
        /// <summary>The entity that charges the other (revenue side).</summary>
        public int ChargerEntityId { get; set; }
        public int CounterAccountAId { get; set; }
        public int CounterAccountBId { get; set; }
        public string Description { get; set; }
        public int EventAId { get; set; }
        public int EventBId { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ audit trail

    /// <summary>Append-only: rows are never changed or deleted.</summary>
    public class FnAuditLog
    {
        public int Id { get; set; }
        public string EntityType { get; set; }
        public int? EntityId { get; set; }
        /// <summary>Added, Modified or Deleted.</summary>
        public string Action { get; set; }
        public string Summary { get; set; }
        /// <summary>Changed fields before the change, as JSON (personal data is masked).</summary>
        public string BeforeJson { get; set; }
        public string AfterJson { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset At { get; set; }
        public string Ip { get; set; }
    }

    // ------------------------------------------------------------------------------------------ period close

    /// <summary>The override of a period-close checklist (items are computed, never stored).</summary>
    public class FnCloseChecklist
    {
        public int Id { get; set; }
        public int PeriodId { get; set; }
        public FnPeriod Period { get; set; }
        public string OverrideReason { get; set; }
        public int OverriddenByUserId { get; set; }
        public DateTimeOffset OverriddenAt { get; set; }
        /// <summary>The red items that were overridden, for the record.</summary>
        public string RedItems { get; set; }
    }

    // ------------------------------------------------------------------------------------------ voucher controls

    public class FnVoucherAttachment
    {
        public int Id { get; set; }
        public int VoucherId { get; set; }
        public FnVoucher Voucher { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; }
        public int AddedByUserId { get; set; }
        public DateTimeOffset AddedAt { get; set; }
    }

    /// <summary>A voucher touching this account (or of this type) needs at least one attachment before it is checked and finalised. Data, none seeded.</summary>
    public class FnAttachmentRule
    {
        public int Id { get; set; }
        public int? AccountId { get; set; }
        public FnAccount Account { get; set; }
        public FnVoucherType? VoucherType { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Vouchers of at least this amount need the CEO's approval before they are finalised. Input ★, none seeded.</summary>
    public class FnApprovalThreshold
    {
        public int Id { get; set; }
        public decimal MinAmount { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class FnVoucherTemplate
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        /// <summary>Day of the Persian month the instance is dated (clamped to the month length).</summary>
        public int DayOfMonth { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<FnVoucherTemplateLine> Lines { get; set; }
    }

    public class FnVoucherTemplateLine
    {
        public int Id { get; set; }
        public int TemplateId { get; set; }
        public FnVoucherTemplate Template { get; set; }
        public int RowNumber { get; set; }
        public int AccountId { get; set; }
        public FnAccount Account { get; set; }
        public int? Detail1Id { get; set; }
        public int? Detail2Id { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string Description { get; set; }
    }

    /// <summary>Which template was drafted for which period (one instance per template and period).</summary>
    public class FnTemplateInstance
    {
        public int Id { get; set; }
        public int TemplateId { get; set; }
        public FnVoucherTemplate Template { get; set; }
        public int PeriodId { get; set; }
        public int VoucherId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ migration from Sepidar

    /// <summary>A Sepidar account code and the PMIS account it maps to.</summary>
    public class FnMigrationAccountMap
    {
        public int Id { get; set; }
        public string SepidarCode { get; set; }
        public int AccountId { get; set; }
        public FnAccount Account { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    public enum FnDiffKind { Matched = 0, PmisOnly = 1, SepidarOnly = 2, AmountDiffers = 3 }

    /// <summary>One comparison of a Sepidar trial balance with the PMIS ledger for a period, and its sign-off.</summary>
    public class FnParallelRun
    {
        public int Id { get; set; }
        public int LegalEntityId { get; set; }
        public int PeriodId { get; set; }
        public FnPeriod Period { get; set; }
        public bool IncludeDraft { get; set; }
        public int Rows { get; set; }
        public int Matched { get; set; }
        public int PmisOnly { get; set; }
        public int SepidarOnly { get; set; }
        public int Differs { get; set; }
        public int Unexplained { get; set; }
        public int UploadedByUserId { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
        public int? SignedOffByUserId { get; set; }
        public DateTimeOffset? SignedOffAt { get; set; }
        public string SignOffNote { get; set; }
        /// <summary>Unexplained differences at the moment of the sign-off.</summary>
        public int? UnexplainedAtSignOff { get; set; }
        public ICollection<FnParallelDifference> Differences { get; set; }
    }

    public class FnParallelDifference
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public FnParallelRun Run { get; set; }
        public FnDiffKind Kind { get; set; }
        public string AccountCode { get; set; }
        public string AccountTitle { get; set; }
        public string DetailCode { get; set; }
        public decimal PmisDebit { get; set; }
        public decimal PmisCredit { get; set; }
        public decimal SepidarDebit { get; set; }
        public decimal SepidarCredit { get; set; }
        public string Explanation { get; set; }
        public int? ExplainedByUserId { get; set; }
        public DateTimeOffset? ExplainedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ full voucher export for Sepidar (fallback, section 1b)

    /// <summary>One column of the voucher export file. The layout is data: the official import layout of the receiving system may differ.</summary>
    public class FnExportLayoutColumn
    {
        public int Id { get; set; }
        /// <summary>VoucherNo, Date, Description, LineDescription, AccountCode, DetailCode, Debit, Credit, CostCenter or Project.</summary>
        public string Key { get; set; }
        public string Title { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>The Sepidar code of a detail account (tafsili).</summary>
    public class FnDetailSepidarMap
    {
        public int Id { get; set; }
        public int DetailAccountId { get; set; }
        public FnDetailAccount DetailAccount { get; set; }
        public string SepidarCode { get; set; }
    }

    public enum FnExportStatus { Active = 1, Superseded = 2 }

    /// <summary>One export of the final vouchers of a period. The file itself is kept so that it can be downloaded again byte for byte.</summary>
    public class FnVoucherExport
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int LegalEntityId { get; set; }
        public int PeriodId { get; set; }
        public FnPeriod Period { get; set; }
        public FnExportStatus Status { get; set; }
        public int Vouchers { get; set; }
        public int Rows { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        /// <summary>SHA-256 of the file content (hex).</summary>
        public string Sha256 { get; set; }
        public string Content { get; set; }
        public int ExportedByUserId { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public int? SupersededByExportId { get; set; }
        public string SupersedeReason { get; set; }
    }
}
