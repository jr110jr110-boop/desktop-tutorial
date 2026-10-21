using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Providers;

namespace Entities.Model.NasbNiro.Finance
{
    // Cost accounting (حسابداری صنعتی) inside PMIS and the link to Sepidar.
    // PMIS keeps the sub-ledgers (stock value, cost of each work order and project, purchase commitments, supplier
    // invoices); every event that changes money is written as a balanced accounting event (debit = credit) with
    // account roles. Sepidar receives the events; a mapping table turns roles into Sepidar account codes.

    /// <summary>Weighted average cost of an item over all company-owned warehouses (customer property excluded).</summary>
    public class ItemCost
    {
        public int Id { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        /// <summary>Quantity valued (company-owned stock).</summary>
        public decimal Quantity { get; set; }

        /// <summary>Total value, rial.</summary>
        public decimal Value { get; set; }

        public decimal AverageCost { get; set; }
        public decimal? LastPurchasePrice { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum CostKind
    {
        Material = 1,
        Subcontract = 2,
        Labour = 3,
        Overhead = 4,
        PurchasedService = 5,
    }

    /// <summary>One cost charged to a work order (دستور ساخت) and its project (contract).</summary>
    public class CostEntry
    {
        public int Id { get; set; }
        public DateTime EntryDate { get; set; }
        public CostKind Kind { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        /// <summary>Project control: the WBS node this cost belongs to, when finance knows it (nullable).</summary>
        public int? WbsNodeId { get; set; }

        /// <summary>Cost centre code (when the cost is not for a work order, for example maintenance).</summary>
        public string CostCenter { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Rial; negative for returns and reversals.</summary>
        public decimal Amount { get; set; }

        /// <summary>"InventoryDocument", "SupplierInvoice", "Timesheet"...</summary>
        public string SourceType { get; set; }
        public int SourceId { get; set; }
        public string SourceNumber { get; set; }
        public string Description { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    public enum AccountingEventType
    {
        PurchaseReceipt = 1,
        SupplierReturn = 2,
        MaterialIssue = 3,
        ProductionReturn = 4,
        ProductionReceipt = 5,
        InventoryGain = 6,
        InventoryLoss = 7,
        OpeningBalance = 8,
        CustomerDelivery = 9,
        ScrapReceipt = 10,
        ScrapSale = 11,
        StageConversion = 12,
        SupplierInvoice = 20,
        SupplierAdvance = 21,
        PaymentRequest = 22,
        Reversal = 99,
    }

    public enum AccountingEventStatus
    {
        /// <summary>Waiting to be sent to Sepidar.</summary>
        Pending = 0,
        Sent = 1,
        Failed = 2,
        /// <summary>Exported to a file and entered in Sepidar by hand.</summary>
        Exported = 3,
        /// <summary>Has a zero amount or nothing to send.</summary>
        NotRequired = 9,
    }

    /// <summary>A balanced accounting event for Sepidar (one future voucher, سند حسابداری).</summary>
    public class AccountingEvent
    {
        public int Id { get; set; }
        public AccountingEventType Type { get; set; }
        public DateTime EventDate { get; set; }
        public AccountingEventStatus Status { get; set; }

        /// <summary>"InventoryDocument", "SupplierInvoice", "PurchaseOrder"...</summary>
        public string SourceType { get; set; }
        public int SourceId { get; set; }
        public string SourceNumber { get; set; }

        /// <summary>The event this one reverses (cancelled document).</summary>
        public int? ReversesEventId { get; set; }

        public string Description { get; set; }

        /// <summary>Unique key so an event is never sent twice (idempotency for the Sepidar API).</summary>
        public Guid Key { get; set; }

        public DateTimeOffset? SentAt { get; set; }
        public string ExternalReference { get; set; }
        public string Error { get; set; }
        public int Attempts { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }

        public ICollection<AccountingEventLine> Lines { get; set; }
    }

    public class AccountingEventLine
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public AccountingEvent Event { get; set; }

        public int RowNumber { get; set; }

        /// <summary>Account role, for example "Inventory", "Inventory.02", "GRNI", "WIP", "COGS" (see AccountRoles).</summary>
        public string AccountRole { get; set; }

        public decimal Debit { get; set; }
        public decimal Credit { get; set; }

        // detail accounts (تفصیلی)
        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public string CostCenter { get; set; }
        public int? ItemId { get; set; }
        public decimal? Quantity { get; set; }

        public string Description { get; set; }
    }

    /// <summary>Which Sepidar account (کل/معین) each account role posts to; detail accounts come from SepidarMapping.</summary>
    public class AccountMapping
    {
        public int Id { get; set; }
        public string Role { get; set; }
        public string Title { get; set; }
        public string SepidarAccountCode { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Cost centres (مراکز هزینه) for costs that are not charged to a work order.</summary>
    public class CostCenter
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string SepidarCode { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
