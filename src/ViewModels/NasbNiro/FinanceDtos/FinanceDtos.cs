using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.FinanceDtos
{
    public class AccountingEventQuery : PaginationParams
    {
        public AccountingEventStatus? Status { get; set; }
        public AccountingEventType? Type { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string Contains { get; set; }
        public bool IncludeNotRequired { get; set; }
    }

    public class AccountingEventDto
    {
        public int Id { get; set; }
        public AccountingEventType Type { get; set; }
        public DateTime EventDate { get; set; }
        public AccountingEventStatus Status { get; set; }
        public string SourceType { get; set; }
        public int SourceId { get; set; }
        public string SourceNumber { get; set; }
        public string Description { get; set; }
        public Guid Key { get; set; }
        public DateTimeOffset? SentAt { get; set; }
        public string ExternalReference { get; set; }
        public string Error { get; set; }
        public int? ReversesEventId { get; set; }
        public decimal Amount { get; set; }
        public List<AccountingEventLineDto> Lines { get; set; } = new List<AccountingEventLineDto>();
    }

    public class AccountingEventLineDto
    {
        public int RowNumber { get; set; }
        public string AccountRole { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public int? ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public string CostCenter { get; set; }
        public string Description { get; set; }
    }

    public class AccountMappingDto
    {
        public int? Id { get; set; }
        public string Role { get; set; }
        public string Title { get; set; }
        public string SepidarAccountCode { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class CostCenterDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string SepidarCode { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class FinanceCheckDto
    {
        public bool Ready { get; set; }
        public bool SepidarApiConfigured { get; set; }
        public int PendingEvents { get; set; }
        public List<string> UnmappedRoles { get; set; } = new List<string>();
        public List<string> UnmappedProviders { get; set; } = new List<string>();
        public int UnmappedProjects { get; set; }
        public int ItemsWithoutSepidarCode { get; set; }
        public int WarehousesWithoutSepidarCode { get; set; }
    }

    public class ExportEventsDto
    {
        public List<int> EventIds { get; set; } = new List<int>();
        public bool MarkExported { get; set; }
    }

    public class MarkEventsDto
    {
        public List<int> EventIds { get; set; } = new List<int>();
        public AccountingEventStatus Status { get; set; }
        public string ExternalReference { get; set; }
    }

    public class WorkOrderCostDto
    {
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public string ContractTitle { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string WorkOrderTitle { get; set; }
        public string CostCenter { get; set; }
        public decimal Material { get; set; }
        public decimal Subcontract { get; set; }
        public decimal Services { get; set; }
        public decimal Labour { get; set; }
        public decimal Overhead { get; set; }
        public decimal Total { get; set; }
    }

    public class ItemValuationDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal AverageCost { get; set; }
        public decimal Value { get; set; }
        public decimal? LastPurchasePrice { get; set; }
    }

    public class ReconciliationRowDto
    {
        public string AccountRole { get; set; }
        public decimal StockValue { get; set; }
        public decimal EventsBalance { get; set; }
        public decimal Difference { get; set; }
    }

    public class ValuationDto
    {
        public decimal TotalValue { get; set; }
        public decimal TotalEventsBalance { get; set; }
        public List<ItemValuationDto> Items { get; set; } = new List<ItemValuationDto>();
        public List<ReconciliationRowDto> Reconciliation { get; set; } = new List<ReconciliationRowDto>();
    }
}
