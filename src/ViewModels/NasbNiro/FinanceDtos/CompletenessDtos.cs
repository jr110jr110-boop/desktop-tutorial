using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // Finance release F3, milestone 1: legal entities, audit trail, period close, voucher controls, migration from Sepidar.

    // ============================================================================================ legal entities

    public class LegalEntityDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Jurisdiction { get; set; }
        public string BaseCurrency { get; set; }
        public bool IsActive { get; set; }
        /// <summary>The default entity of the signed-in user.</summary>
        public bool IsDefault { get; set; }
    }

    public class SaveLegalEntityDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Jurisdiction { get; set; }
        public string BaseCurrency { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SetDefaultEntityDto
    {
        public int LegalEntityId { get; set; }
    }

    public class IntercompanyPairDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int EntityAId { get; set; }
        public int EntityBId { get; set; }
        public int AccountAId { get; set; }
        public string AccountACode { get; set; }
        public int AccountBId { get; set; }
        public string AccountBCode { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveIntercompanyPairDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public int EntityAId { get; set; }
        public int EntityBId { get; set; }
        public string AccountACode { get; set; }
        public string AccountBCode { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class IntercompanyPostDto
    {
        public int PairId { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        /// <summary>Required when the two entities have different currencies: units of the other entity's currency per one unit of the charger's currency.</summary>
        public decimal? Rate { get; set; }
        /// <summary>The entity that charges the other (revenue side); must be one of the pair's entities.</summary>
        public int ChargerEntityId { get; set; }
        /// <summary>Revenue (or other credit) account of the charging entity.</summary>
        public string CounterAccountACode { get; set; }
        /// <summary>Expense (or other debit) account of the charged entity.</summary>
        public string CounterAccountBCode { get; set; }
        public string Description { get; set; }
    }

    public class IntercompanyResultDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int PairId { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountA { get; set; }
        public decimal AmountB { get; set; }
        public decimal Rate { get; set; }
        public int ChargerEntityId { get; set; }
        public string Description { get; set; }
        public int EventAId { get; set; }
        public int EventBId { get; set; }
        public int? VoucherAId { get; set; }
        public int? VoucherBId { get; set; }
    }

    public class ConsolidationQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public bool IncludeDraft { get; set; }
        /// <summary>Translation rates to the presentation currency (the base currency of the main company), for example "2=1.1,3=0.9". Rates are inputs.</summary>
        public string Rates { get; set; }
    }

    public class ConsolidationEntityDto
    {
        public int EntityId { get; set; }
        public string Code { get; set; }
        public string Currency { get; set; }
        public decimal Rate { get; set; }
    }

    public class ConsolidationAmountDto
    {
        public int EntityId { get; set; }
        public decimal Amount { get; set; }
    }

    public class ConsolidationRowDto
    {
        public string AccountCode { get; set; }
        public string AccountTitle { get; set; }
        public List<ConsolidationAmountDto> ByEntity { get; set; } = new List<ConsolidationAmountDto>();
        public decimal Total { get; set; }
        public decimal Elimination { get; set; }
        public decimal Consolidated { get; set; }
    }

    public class ConsolidationPairDto
    {
        public int PairId { get; set; }
        public string Title { get; set; }
        public decimal BalanceA { get; set; }
        public decimal BalanceB { get; set; }
        /// <summary>BalanceA + BalanceB: zero when the two sides mirror each other.</summary>
        public decimal Difference { get; set; }
        public bool Matched { get; set; }
    }

    public class ConsolidationDto
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string PresentationCurrency { get; set; }
        public List<ConsolidationEntityDto> Entities { get; set; } = new List<ConsolidationEntityDto>();
        public List<ConsolidationRowDto> Rows { get; set; } = new List<ConsolidationRowDto>();
        public List<ConsolidationPairDto> Pairs { get; set; } = new List<ConsolidationPairDto>();
        /// <summary>Sum of the debit-minus-credit of all rows before eliminations (zero when every voucher balances).</summary>
        public decimal TotalBefore { get; set; }
        public decimal TotalEliminations { get; set; }
        public decimal TotalConsolidated { get; set; }
        public bool Balanced { get; set; }
    }

    // ============================================================================================ audit trail

    public class AuditQuery : PaginationParams
    {
        public string EntityType { get; set; }
        public int? EntityId { get; set; }
        public int? UserId { get; set; }
        public string Action { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class AuditLogDto
    {
        public int Id { get; set; }
        public string EntityType { get; set; }
        public int? EntityId { get; set; }
        public string Action { get; set; }
        public string Summary { get; set; }
        public string BeforeJson { get; set; }
        public string AfterJson { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset At { get; set; }
        public string Ip { get; set; }
    }

    // ============================================================================================ period close

    public class CloseItemDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        /// <summary>Green, Red or NotApplicable.</summary>
        public string Status { get; set; }
        public int Count { get; set; }
        public string Detail { get; set; }
    }

    public class CloseChecklistDto
    {
        public int PeriodId { get; set; }
        public string PeriodTitle { get; set; }
        public int LegalEntityId { get; set; }
        public List<CloseItemDto> Items { get; set; } = new List<CloseItemDto>();
        public int RedCount { get; set; }
        public bool AllGreen { get; set; }
        public bool Overridden { get; set; }
        public string OverrideReason { get; set; }
        public int? OverriddenByUserId { get; set; }
        public DateTimeOffset? OverriddenAt { get; set; }
        /// <summary>Whether the lock is enforced against the checklist (setting CloseChecklist.Enforce).</summary>
        public bool Enforced { get; set; }
        public bool CanLock { get; set; }
    }

    public class ChecklistOverrideDto
    {
        public string Reason { get; set; }
    }

    public class FinanceSettingDto
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string Title { get; set; }
    }

    public class SaveFinanceSettingDto
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    // ============================================================================================ voucher controls

    public class VoucherAttachmentDto
    {
        public int Id { get; set; }
        public int VoucherId { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; }
        public int AddedByUserId { get; set; }
        public DateTimeOffset AddedAt { get; set; }
    }

    public class AddAttachmentDto
    {
        public int VoucherId { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; }
    }

    public class AttachmentRuleDto
    {
        public int Id { get; set; }
        public int? AccountId { get; set; }
        public string AccountCode { get; set; }
        public FnVoucherType? VoucherType { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveAttachmentRuleDto
    {
        public int? Id { get; set; }
        public string AccountCode { get; set; }
        public FnVoucherType? VoucherType { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class ApprovalThresholdDto
    {
        public int Id { get; set; }
        public decimal MinAmount { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveApprovalThresholdDto
    {
        public int? Id { get; set; }
        public decimal MinAmount { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class VoucherIdDto
    {
        public int Id { get; set; }
        public DateTime? Date { get; set; }
    }

    public class VoucherTemplateLineDto
    {
        public int RowNumber { get; set; }
        public string AccountCode { get; set; }
        public int? Detail1Id { get; set; }
        public int? Detail2Id { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string Description { get; set; }
    }

    public class VoucherTemplateDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int DayOfMonth { get; set; }
        public bool IsActive { get; set; }
        public List<VoucherTemplateLineDto> Lines { get; set; } = new List<VoucherTemplateLineDto>();
    }

    public class SaveVoucherTemplateDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int DayOfMonth { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public List<VoucherTemplateLineDto> Lines { get; set; } = new List<VoucherTemplateLineDto>();
    }

    public class GenerateTemplatesDto
    {
        public int PeriodId { get; set; }
    }

    public class GenerateTemplatesResultDto
    {
        public int Drafted { get; set; }
        public int Skipped { get; set; }
        public List<int> VoucherIds { get; set; } = new List<int>();
        public List<string> Messages { get; set; } = new List<string>();
    }

    public class MassVoucherRowDto
    {
        /// <summary>Rows with the same group key form one voucher.</summary>
        public string Group { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public string AccountCode { get; set; }
        public string Detail1Code { get; set; }
        public string Detail2Code { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string LineDescription { get; set; }
    }

    public class MassVoucherImportDto
    {
        public bool DryRun { get; set; }
        public int? LegalEntityId { get; set; }
        public List<MassVoucherRowDto> Rows { get; set; } = new List<MassVoucherRowDto>();
    }

    public class MassVoucherResultDto
    {
        public bool DryRun { get; set; }
        public int Vouchers { get; set; }
        public int Lines { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<int> VoucherIds { get; set; } = new List<int>();
    }

    // ============================================================================================ migration from Sepidar

    public class SepidarAccountRowDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        /// <summary>1 = group, 2 = general (کل), 3 = subsidiary (معین).</summary>
        public int Level { get; set; } = 3;
        /// <summary>Debit, Credit or Either (also accepted: بدهکار, بستانکار).</summary>
        public string Nature { get; set; }
        /// <summary>Optional: map this Sepidar code to an existing PMIS account code instead of creating it.</summary>
        public string PmisCode { get; set; }
    }

    public class SepidarAccountImportDto
    {
        public List<SepidarAccountRowDto> Rows { get; set; } = new List<SepidarAccountRowDto>();
    }

    public class ImportRowReportDto
    {
        public int Row { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        /// <summary>Existing, Mapped, New, Error (accounts); Existing, New, Review, Error (details).</summary>
        public string Status { get; set; }
        public string PmisCode { get; set; }
        public string GuessedType { get; set; }
        public string Message { get; set; }
    }

    public class ImportReportDto
    {
        public bool DryRun { get; set; }
        public bool Committed { get; set; }
        public int Total { get; set; }
        public int Existing { get; set; }
        public int Mapped { get; set; }
        public int New { get; set; }
        public int Review { get; set; }
        public int Errors { get; set; }
        public List<ImportRowReportDto> Rows { get; set; } = new List<ImportRowReportDto>();
    }

    public class SepidarDetailRowDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        /// <summary>Manual correction of the guessed type (Person, Supplier, Customer, Employee, Bank, CashBox, PettyCash, Project, WorkOrder, CostCenter, Asset, Other).</summary>
        public string Type { get; set; }
    }

    /// <summary>Links imported Sepidar detail accounts to the PMIS parties so that opening balances and new postings share one detail (review 2, finding 9).</summary>
    public class FnDetailLinkRowDto
    {
        public string DetailCode { get; set; }
        /// <summary>Supplier, Customer or BankAccount (employees are payroll data and are not linked here).</summary>
        public string PartyType { get; set; }
        public int PartyId { get; set; }
    }

    public class FnDetailLinkDto
    {
        /// <summary>Match unlinked details by name, economic code, national id or IBAN as well.</summary>
        public bool Auto { get; set; }
        /// <summary>The manual correction list; it wins over the automatic match.</summary>
        public List<FnDetailLinkRowDto> Rows { get; set; } = new List<FnDetailLinkRowDto>();
    }

    public class FnDetailLinkResultRowDto
    {
        public string DetailCode { get; set; }
        public string Title { get; set; }
        public string PartyType { get; set; }
        public int? PartyId { get; set; }
        public string PartyName { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
    }

    public class FnDetailLinkReportDto
    {
        public bool DryRun { get; set; }
        public bool Committed { get; set; }
        public int Linked { get; set; }
        public int Ambiguous { get; set; }
        public int Unmatched { get; set; }
        public int Errors { get; set; }
        public List<FnDetailLinkResultRowDto> Rows { get; set; } = new List<FnDetailLinkResultRowDto>();
    }

    public class SepidarDetailImportDto
    {
        public List<SepidarDetailRowDto> Rows { get; set; } = new List<SepidarDetailRowDto>();
    }

    public class SepidarOpeningRowDto
    {
        public string AccountCode { get; set; }
        public string DetailCode { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class SepidarOpeningImportDto
    {
        public int? FiscalYearId { get; set; }
        public string Description { get; set; }
        public List<SepidarOpeningRowDto> Rows { get; set; } = new List<SepidarOpeningRowDto>();
    }

    public class OpeningReportDto
    {
        public bool DryRun { get; set; }
        public bool Committed { get; set; }
        public int Rows { get; set; }
        public List<string> UnknownAccounts { get; set; } = new List<string>();
        public List<string> UnknownDetails { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Difference { get; set; }
        public bool Balanced { get; set; }
        public bool AlreadyHasOpening { get; set; }
        public int? VoucherId { get; set; }
    }

    public class CreateImportedYearDto
    {
        public int Year { get; set; }
        public string Title { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class HistoricalVoucherRowDto
    {
        public DateTime Date { get; set; }
        public int Number { get; set; }
        public string Description { get; set; }
        public string AccountCode { get; set; }
        public string DetailCode { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class HistoricalImportDto
    {
        public int FiscalYearId { get; set; }
        public bool DryRun { get; set; }
        public List<HistoricalVoucherRowDto> Rows { get; set; } = new List<HistoricalVoucherRowDto>();
    }

    public class HistoricalReportDto
    {
        public bool DryRun { get; set; }
        public bool Committed { get; set; }
        public int Vouchers { get; set; }
        public int Lines { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class ParallelRowDto
    {
        public string AccountCode { get; set; }
        public string DetailCode { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class ParallelCompareDto
    {
        public int PeriodId { get; set; }
        public bool IncludeDraft { get; set; }
        public List<ParallelRowDto> Rows { get; set; } = new List<ParallelRowDto>();
    }

    public class ParallelDifferenceDto
    {
        public int Id { get; set; }
        public FnDiffKind Kind { get; set; }
        public string AccountCode { get; set; }
        public string AccountTitle { get; set; }
        public string DetailCode { get; set; }
        public decimal PmisDebit { get; set; }
        public decimal PmisCredit { get; set; }
        public decimal SepidarDebit { get; set; }
        public decimal SepidarCredit { get; set; }
        public string Explanation { get; set; }
    }

    public class ParallelRunDto
    {
        public int Id { get; set; }
        public int LegalEntityId { get; set; }
        public int PeriodId { get; set; }
        public string PeriodTitle { get; set; }
        public int Rows { get; set; }
        public int Matched { get; set; }
        public int PmisOnly { get; set; }
        public int SepidarOnly { get; set; }
        public int Differs { get; set; }
        public int Unexplained { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
        public bool SignedOff { get; set; }
        public int? SignedOffByUserId { get; set; }
        public DateTimeOffset? SignedOffAt { get; set; }
        public string SignOffNote { get; set; }
        public int? UnexplainedAtSignOff { get; set; }
        /// <summary>True when the period has confidential labour lines (R1p): the comparison leaves them out, so the labour accounts of Sepidar may differ by them; the single-run view only.</summary>
        public bool LabourHidden { get; set; }
        public List<ParallelDifferenceDto> Differences { get; set; } = new List<ParallelDifferenceDto>();
    }

    public class ExplainDifferenceDto
    {
        public int DifferenceId { get; set; }
        public string Explanation { get; set; }
    }

    public class SignOffDto
    {
        public string Note { get; set; }
    }

    public class ParallelStatusDto
    {
        public int RequiredMonths { get; set; }
        /// <summary>Consecutive latest periods that are signed off with zero unexplained differences.</summary>
        public int ConsecutiveCleanPeriods { get; set; }
        public bool GoLiveReady { get; set; }
        public List<ParallelPeriodStatusDto> Periods { get; set; } = new List<ParallelPeriodStatusDto>();
    }

    public class ParallelPeriodStatusDto
    {
        public int PeriodId { get; set; }
        public string PeriodTitle { get; set; }
        public int? RunId { get; set; }
        public bool SignedOff { get; set; }
        public int Unexplained { get; set; }
        public bool Clean { get; set; }
    }
}
