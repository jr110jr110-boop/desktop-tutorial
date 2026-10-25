using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Sales;
using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Sales
{
    // Sales, release S1 part A. All tables start with "Sl". Deletes are restricted except for child lines, which go with their header.
    public static class SalesModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddSalesConfigurations(this ModelBuilder builder)
        {
            builder.Entity<SlSetting>(b =>
            {
                b.ToTable("SlSettings");
                b.Property(x => x.Key).HasMaxLength(Name).IsRequired();
                b.HasIndex(x => x.Key).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Value).HasPrecision(22, 4);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Note).HasMaxLength(Text);
            });

            // ------------------------------------------------------------------ customers
            builder.Entity<SlCustomerProfile>(b =>
            {
                b.ToTable("SlCustomerProfiles");
                b.HasIndex(x => x.CustomerId).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.OwnerUserId);
                b.Property(x => x.Country).HasMaxLength(Name);
                b.Property(x => x.City).HasMaxLength(Name);
                b.Property(x => x.CreditLimitRial).HasPrecision(22, 2);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlContact>(b =>
            {
                b.ToTable("SlContacts");
                b.HasIndex(x => x.CustomerId);
                b.Property(x => x.FullName).HasMaxLength(Name).IsRequired();
                b.Property(x => x.JobTitle).HasMaxLength(Name);
                b.Property(x => x.Phone).HasMaxLength(Code);
                b.Property(x => x.Email).HasMaxLength(Name);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlSite>(b =>
            {
                b.ToTable("SlSites");
                b.HasIndex(x => x.CustomerId);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Address).HasMaxLength(Text);
                b.Property(x => x.Country).HasMaxLength(Name);
                b.Property(x => x.CoordinatesText).HasMaxLength(Name);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlActivity>(b =>
            {
                b.ToTable("SlActivities");
                b.HasIndex(x => x.CustomerId);
                b.HasIndex(x => x.InquiryId);
                b.HasIndex(x => x.ProposalId);
                b.HasIndex(x => x.ServiceRequestId);
                b.Property(x => x.Subject).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Notes).HasMaxLength(LongText);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlInquiry>().WithMany().HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlProposal>().WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlCustomerLink>(b =>
            {
                b.ToTable("SlCustomerLinks");
                b.HasIndex(x => new { x.SourceType, x.SourceId }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.CustomerId);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ inquiry and bid decision
            builder.Entity<SlInquiry>(b =>
            {
                b.ToTable("SlInquiries");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.CustomerId);
                b.Property(x => x.Channel).HasMaxLength(Name);
                b.Property(x => x.CustomerReference).HasMaxLength(Name);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.AttachmentIds).HasMaxLength(Text);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Tender>().WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlBidDecision>(b =>
            {
                b.ToTable("SlBidDecisions");
                b.HasIndex(x => x.InquiryId).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.TechnicalNote).HasMaxLength(Text);
                b.Property(x => x.CapacityNote).HasMaxLength(Text);
                b.Property(x => x.CommercialRiskNote).HasMaxLength(Text);
                b.Property(x => x.PaymentTermsNote).HasMaxLength(Text);
                b.Property(x => x.GuaranteesNote).HasMaxLength(Text);
                b.Property(x => x.SanctionNote).HasMaxLength(Text);
                b.Property(x => x.ExpectedMarginPercent).HasPrecision(9, 4);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.Property(x => x.DecisionReason).HasMaxLength(Text);
                b.HasOne<SlInquiry>().WithMany().HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlReasonCode>(b =>
            {
                b.ToTable("SlReasonCodes");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => new { x.Kind, x.Code }).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
            });

            // ------------------------------------------------------------------ estimate
            builder.Entity<SlEstimate>(b =>
            {
                b.ToTable("SlEstimates");
                b.Property(x => x.Number).HasMaxLength(Name).IsRequired();
                b.HasIndex(x => new { x.InquiryId, x.Revision }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.Note).HasMaxLength(Text);
                foreach (var p in new[]
                {
                    nameof(SlEstimate.OverheadPercent), nameof(SlEstimate.ContingencyPercent), nameof(SlEstimate.TargetMarginPercent), nameof(SlEstimate.GuaranteeMonths),
                    nameof(SlEstimate.GuaranteeRatePercentPerMonth), nameof(SlEstimate.FinancingMonths), nameof(SlEstimate.FinancingRatePercentPerMonth),
                })
                    b.Property(p).HasPrecision(9, 4);
                foreach (var p in new[]
                {
                    nameof(SlEstimate.GuaranteeAmount), nameof(SlEstimate.FinancingAmount), nameof(SlEstimate.MaterialCost), nameof(SlEstimate.LabourCost), nameof(SlEstimate.EngineeringCost),
                    nameof(SlEstimate.SubcontractCost), nameof(SlEstimate.PackingTransportCost), nameof(SlEstimate.DirectCost), nameof(SlEstimate.GuaranteeCost), nameof(SlEstimate.FinancingCost),
                    nameof(SlEstimate.RiskCost), nameof(SlEstimate.ContingencyCost), nameof(SlEstimate.CostBeforeOverhead), nameof(SlEstimate.OverheadCost), nameof(SlEstimate.TotalCost), nameof(SlEstimate.Price),
                })
                    b.Property(p).HasPrecision(22, 2);
                b.Property(x => x.MissingInputs).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.HasOne<SlInquiry>().WithMany().HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlEstimateEquipment>(b =>
            {
                b.ToTable("SlEstimateEquipments");
                b.HasIndex(x => x.EstimateId);
                b.HasOne<SlEstimate>().WithMany(e => e.Equipments).HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlEstimateLine>(b =>
            {
                b.ToTable("SlEstimateLines");
                b.HasIndex(x => x.EstimateId);
                b.Property(x => x.Description).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.UnitCost).HasPrecision(22, 4);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne<SlEstimate>().WithMany(e => e.Lines).HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<EquipmentBom>().WithMany().HasForeignKey(x => x.EquipmentBomId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlEstimateRisk>(b =>
            {
                b.ToTable("SlEstimateRisks");
                b.HasIndex(x => x.EstimateId);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.ProbabilityPercent).HasPrecision(9, 4);
                b.Property(x => x.ImpactAmount).HasPrecision(22, 2);
                b.Property(x => x.ExpectedCost).HasPrecision(22, 2);
                b.HasOne<SlEstimate>().WithMany(e => e.Risks).HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ proposal
            builder.Entity<SlProposal>(b =>
            {
                b.ToTable("SlProposals");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => new { x.Number, x.Revision }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.InquiryId);
                b.HasIndex(x => x.Status);
                b.Property(x => x.Price).HasPrecision(22, 2);
                b.Property(x => x.PaymentTermsText).HasMaxLength(Text);
                b.Property(x => x.GuaranteesText).HasMaxLength(Text);
                b.Property(x => x.DeviationsText).HasMaxLength(LongText);
                foreach (var p in new[]
                {
                    nameof(SlProposal.VatPercent), nameof(SlProposal.AdvancePercent), nameof(SlProposal.RetentionPercent), nameof(SlProposal.LatePenaltyPercentPerWeek), nameof(SlProposal.PenaltyCapPercent),
                })
                    b.Property(p).HasPrecision(9, 4);
                b.Property(x => x.EstimateTotalCost).HasPrecision(22, 2);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.Property(x => x.ApprovalNote).HasMaxLength(Text);
                b.Property(x => x.CustomerReference).HasMaxLength(Name);
                b.Property(x => x.Competitor).HasMaxLength(Name);
                b.Property(x => x.CompetitorPrice).HasPrecision(22, 2);
                b.Property(x => x.LessonsLearned).HasMaxLength(LongText);
                b.HasOne<SlInquiry>().WithMany().HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlEstimate>().WithMany().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlReasonCode>().WithMany().HasForeignKey(x => x.ResultReasonId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ price lists, quotations, sales orders
            builder.Entity<SlPriceList>(b =>
            {
                b.ToTable("SlPriceLists");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Note).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
            });
            builder.Entity<SlPriceListLine>(b =>
            {
                b.ToTable("SlPriceListLines");
                b.HasIndex(x => x.PriceListId);
                b.Property(x => x.UnitPrice).HasPrecision(22, 4);
                b.Property(x => x.MinQuantity).HasPrecision(18, 4);
                b.HasOne<SlPriceList>().WithMany(p => p.Lines).HasForeignKey(x => x.PriceListId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlQuotation>(b =>
            {
                b.ToTable("SlQuotations");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.CustomerId);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.Property(x => x.NetAmount).HasPrecision(22, 2);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlPriceList>().WithMany().HasForeignKey(x => x.PriceListId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlQuotationLine>(b =>
            {
                b.ToTable("SlQuotationLines");
                b.HasIndex(x => x.QuotationId);
                b.Property(x => x.Description).HasMaxLength(Title);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.UnitPrice).HasPrecision(22, 4);
                b.Property(x => x.DiscountPercent).HasPrecision(9, 4);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.HasOne<SlQuotation>().WithMany(q => q.Lines).HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlSalesOrder>(b =>
            {
                b.ToTable("SlSalesOrders");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.CustomerId);
                b.Property(x => x.CustomerOrderNumber).HasMaxLength(Name);
                b.Property(x => x.NetAmount).HasPrecision(22, 2);
                b.Property(x => x.CreditReason).HasMaxLength(Text);
                b.Property(x => x.CreditExcess).HasPrecision(22, 2);
                b.Property(x => x.CreditApprovedNetAmount).HasPrecision(22, 2);
                b.Property(x => x.CreditApprovalNote).HasMaxLength(Text);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlQuotation>().WithMany().HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlSalesOrderLine>(b =>
            {
                b.ToTable("SlSalesOrderLines");
                b.HasIndex(x => x.OrderId);
                b.HasIndex(x => x.ItemId);
                b.Property(x => x.Description).HasMaxLength(Title);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.UnitPrice).HasPrecision(22, 4);
                b.Property(x => x.DiscountPercent).HasPrecision(9, 4);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.ReservedQuantity).HasPrecision(18, 4);
                b.Property(x => x.DeliveredQuantity).HasPrecision(18, 4);
                b.Property(x => x.InvoicedQuantity).HasPrecision(18, 4);
                b.HasOne<SlSalesOrder>().WithMany(o => o.Lines).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlOrderDelivery>(b =>
            {
                b.ToTable("SlOrderDeliveries");
                b.HasIndex(x => x.SalesOrderId);
                b.HasIndex(x => x.DocumentId).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.DraftNote).HasMaxLength(Text);
                b.HasOne<SlSalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnSalesInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            });

            // delivery documents of the warehouse module name the order they deliver
            builder.Entity<InventoryDocument>(b =>
            {
                b.HasIndex(x => x.SalesOrderId);
                b.HasOne<SlSalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<InventoryDocumentLine>(b =>
            {
                b.HasOne<SlSalesOrderLine>().WithMany().HasForeignKey(x => x.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
