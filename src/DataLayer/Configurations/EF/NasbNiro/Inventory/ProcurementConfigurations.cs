using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Inventory
{
    // Procurement (تدارکات) tables. All start with "Proc"; no existing table is changed.
    public static class ProcurementModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddProcurementConfigurations(this ModelBuilder builder)
        {
            builder.Entity<PurchaseRequest>(b =>
            {
                b.ToTable("ProcPurchaseRequests");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.CostCenter).HasMaxLength(Code);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.BoardResolution).HasMaxLength(Title);
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.Property(x => x.EstimatedAmount).HasPrecision(22, 2);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PurchaseRequestLine>(b =>
            {
                b.ToTable("ProcPurchaseRequestLines");
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.Property(x => x.TechnicalRequirements).HasMaxLength(LongText);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.EstimatedUnitPrice).HasPrecision(22, 4);
                b.HasOne(x => x.PurchaseRequest).WithMany(x => x.Lines).HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PurchaseInquiry>(b =>
            {
                b.ToTable("ProcInquiries");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.FewerQuotesReason).HasMaxLength(Text);
                b.Property(x => x.SelectionReason).HasMaxLength(Text);
                b.Property(x => x.CommitteeMinutes).HasMaxLength(Title);
                b.HasOne(x => x.PurchaseRequest).WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SupplierQuote>(b =>
            {
                b.ToTable("ProcQuotes");
                b.Property(x => x.QuoteNumber).HasMaxLength(Code);
                b.Property(x => x.PaymentTerms).HasMaxLength(Text);
                b.Property(x => x.TechnicalNote).HasMaxLength(Text);
                b.Property(x => x.Notes).HasMaxLength(Text);
                b.Property(x => x.VatPercent).HasPrecision(9, 4);
                b.Property(x => x.TechnicalScore).HasPrecision(9, 2);
                b.Property(x => x.TotalAmount).HasPrecision(22, 2);
                b.HasOne(x => x.Inquiry).WithMany(x => x.Quotes).HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SupplierQuoteLine>(b =>
            {
                b.ToTable("ProcQuoteLines");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.UnitPrice).HasPrecision(22, 4);
                b.HasOne(x => x.Quote).WithMany(x => x.Lines).HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.RequestLine).WithMany().HasForeignKey(x => x.RequestLineId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PurchaseOrder>(b =>
            {
                b.ToTable("ProcPurchaseOrders");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.Currency).HasMaxLength(10);
                b.Property(x => x.ExchangeRate).HasPrecision(22, 4);
                b.Property(x => x.Incoterms).HasMaxLength(Code);
                b.Property(x => x.PaymentTerms).HasMaxLength(Text);
                b.Property(x => x.VatPercent).HasPrecision(9, 4);
                b.Property(x => x.AdvancePercent).HasPrecision(9, 4);
                b.Property(x => x.AdvanceGuarantee).HasMaxLength(Title);
                b.Property(x => x.PerformanceGuarantee).HasMaxLength(Title);
                b.Property(x => x.RetentionPercent).HasPrecision(9, 4);
                b.Property(x => x.ContractNumber).HasMaxLength(Code);
                b.Property(x => x.Requirements).HasMaxLength(LongText);
                b.Property(x => x.DeliveryPlace).HasMaxLength(Title);
                b.Property(x => x.LatePenaltyPercentPerWeek).HasPrecision(9, 4);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.Property(x => x.TotalAmount).HasPrecision(22, 2);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Inquiry).WithMany().HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PurchaseOrderLine>(b =>
            {
                b.ToTable("ProcPurchaseOrderLines");
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.UnitPrice).HasPrecision(22, 4);
                b.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
                b.Property(x => x.InvoicedQuantity).HasPrecision(18, 4);
                b.HasOne(x => x.PurchaseOrder).WithMany(x => x.Lines).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.RequestLine).WithMany().HasForeignKey(x => x.RequestLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ServiceAcceptance>(b =>
            {
                b.ToTable("ProcServiceAcceptances");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Notes).HasMaxLength(Text);
                b.HasOne(x => x.PurchaseOrderLine).WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SupplierInvoice>(b =>
            {
                b.ToTable("ProcSupplierInvoices");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.InvoiceNumber).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => new { x.ProviderId, x.InvoiceNumber });
                b.Property(x => x.NetAmount).HasPrecision(22, 2);
                b.Property(x => x.VatAmount).HasPrecision(22, 2);
                b.Property(x => x.TotalAmount).HasPrecision(22, 2);
                b.Property(x => x.PriceVariance).HasPrecision(22, 2);
                b.Property(x => x.MatchNote).HasMaxLength(LongText);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SupplierInvoiceLine>(b =>
            {
                b.ToTable("ProcSupplierInvoiceLines");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.UnitPrice).HasPrecision(22, 4);
                b.HasOne(x => x.Invoice).WithMany(x => x.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.OrderLine).WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PaymentRequest>(b =>
            {
                b.ToTable("ProcPaymentRequests");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.Deductions).HasPrecision(22, 2);
                b.Property(x => x.DeductionNote).HasMaxLength(Text);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.PaymentReference).HasMaxLength(100);
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SupplierProfile>(b =>
            {
                b.ToTable("ProcSupplierProfiles");
                b.HasIndex(x => x.ProviderId).IsUnique();
                b.Property(x => x.Score).HasPrecision(9, 2);
                b.Property(x => x.Categories).HasMaxLength(Text);
                b.Property(x => x.BlockReason).HasMaxLength(Text);
                b.Property(x => x.SepidarCode).HasMaxLength(Code);
                b.Property(x => x.NationalId).HasMaxLength(20);
                b.Property(x => x.EconomicCode).HasMaxLength(20);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SupplierEvaluation>(b =>
            {
                b.ToTable("ProcSupplierEvaluations");
                b.Property(x => x.Score).HasPrecision(9, 2);
                b.Property(x => x.Notes).HasMaxLength(LongText);
                b.HasIndex(x => x.ProviderId);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SupplierEvaluationLine>(b =>
            {
                b.ToTable("ProcSupplierEvaluationLines");
                b.Property(x => x.Question).HasMaxLength(Text).IsRequired();
                b.Property(x => x.Weight).HasPrecision(9, 2);
                b.Property(x => x.Score).HasPrecision(9, 2);
                b.HasOne(x => x.Evaluation).WithMany(x => x.Lines).HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProcurementSetting>(b =>
            {
                b.ToTable("ProcSettings");
                b.Property(x => x.Key).HasMaxLength(100).IsRequired();
                b.HasIndex(x => x.Key).IsUnique();
                b.Property(x => x.Value).HasPrecision(22, 2);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Source).HasMaxLength(Title);
            });
        }
    }
}
