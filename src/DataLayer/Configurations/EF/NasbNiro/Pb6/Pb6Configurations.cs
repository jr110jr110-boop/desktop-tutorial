using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Pb6
{
    // PB6: procurement completion (archive, revisions, addenda, guarantees, payment terms, the legacy bridge), the contracts system (Ct), progress statements (Ps) and the petty-cash flow (Fn...).
    // Child rows go with their header; every other delete is restricted. Numbers are unique among the rows that are not deleted.
    public static class Pb6ModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddPb6Configurations(this ModelBuilder builder)
        {
            builder.Entity<PurchaseRequest>(b =>
            {
                b.Property(x => x.ReturnReason).HasMaxLength(Text);
                b.Property(x => x.VehicleUnit).HasMaxLength(Name);
                b.Property(x => x.VehicleType).HasMaxLength(Name);
                b.Property(x => x.VehicleOrigin).HasMaxLength(Title);
                b.Property(x => x.VehicleDestination).HasMaxLength(Title);
                b.Property(x => x.Priority).HasDefaultValue(PurchaseRequestPriority.Normal);
                b.Property(x => x.FormType).HasDefaultValue(PurchaseFormType.GoodsProject);
                b.Property(x => x.Revision).HasDefaultValue(1);
                b.HasIndex(x => x.FormType);
                b.HasIndex(x => x.PreviousRequestId);
                b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.RelatedOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PurchaseRequest>().WithMany().HasForeignKey(x => x.SupersededByRequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.RelatedOrderId);
                b.HasIndex(x => x.SupersededByRequestId);
            });
            builder.Entity<PurchaseInquiry>(b =>
            {
                b.Property(x => x.ReturnReason).HasMaxLength(Text);
                b.Property(x => x.PlanningNote).HasMaxLength(Text);
                b.Property(x => x.PlanningBudgetAmount).HasPrecision(22, 2);
                b.Property(x => x.Revision).HasDefaultValue(1);
                b.Property(x => x.BudgetOverrideNote).HasMaxLength(Text);
                b.Property(x => x.BudgetOverrideAmount).HasPrecision(22, 2);
                b.HasIndex(x => x.PreviousInquiryId);
                b.HasOne<PurchaseInquiry>().WithMany().HasForeignKey(x => x.SupersededByInquiryId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.SupersededByInquiryId);
            });

            builder.Entity<PurchaseRequestRevision>(b =>
            {
                b.ToTable("ProcRequestRevisions");
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.Property(x => x.Hash).HasMaxLength(100);
                b.Property(x => x.EstimatedAmount).HasPrecision(22, 2);
                b.HasIndex(x => new { x.PurchaseRequestId, x.Revision }).IsUnique();
                b.HasOne(x => x.PurchaseRequest).WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<ProcArchiveRecord>(b =>
            {
                b.ToTable("ProcArchive");
                b.Property(x => x.ArchiveReason).HasMaxLength(Text);
                b.Property(x => x.RestoreReason).HasMaxLength(Text);
                b.HasIndex(x => new { x.OwnerType, x.OwnerId }).IsUnique();
            });
            builder.Entity<ProcBuyOrderBridge>(b =>
            {
                b.ToTable("ProcBuyOrderBridges");
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => x.BuyOrderId).IsUnique().HasFilter(NotDeleted);
                b.HasOne(x => x.PurchaseRequest).WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PurchaseOrderAddendum>(b =>
            {
                b.ToTable("ProcAddenda");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.PurchaseOrderId, x.Sequence }).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.Property(x => x.AmountBefore).HasPrecision(22, 2);
                b.Property(x => x.AmountAfter).HasPrecision(22, 2);
                b.Property(x => x.MaxVolumeChangePercent).HasPrecision(9, 2);
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PurchaseOrderAddendumLine>(b =>
            {
                b.ToTable("ProcAddendumLines");
                b.Property(x => x.QuantityBefore).HasPrecision(18, 4);
                b.Property(x => x.QuantityAfter).HasPrecision(18, 4);
                b.Property(x => x.UnitPriceBefore).HasPrecision(22, 4);
                b.Property(x => x.UnitPriceAfter).HasPrecision(22, 4);
                b.HasOne(x => x.Addendum).WithMany(x => x.Lines).HasForeignKey(x => x.AddendumId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.OrderLine).WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SupplierGuarantee>(b =>
            {
                b.ToTable("ProcGuarantees");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.GuaranteeNumber).HasMaxLength(Code);
                b.Property(x => x.Issuer).HasMaxLength(Name);
                b.Property(x => x.ReleaseConditions).HasMaxLength(Text);
                b.Property(x => x.ReleaseReason).HasMaxLength(Text);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.HasIndex(x => x.ExpiryDate);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PurchaseOrderPaymentTerm>(b =>
            {
                b.ToTable("ProcPaymentTerms");
                b.Property(x => x.Percent).HasPrecision(9, 4);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.Condition).HasMaxLength(Text);
                b.HasIndex(x => new { x.PurchaseOrderId, x.RowNumber }).IsUnique();
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            });

            // ---------------------------------------------------------------------------------------------- contracts system
            builder.Entity<CtDealRequest>(b =>
            {
                b.ToTable("CtDealRequests");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Subject).HasMaxLength(LongText);
                b.Property(x => x.FundingSource).HasMaxLength(Name);
                b.Property(x => x.OrganisationalUnit).HasMaxLength(Name);
                b.Property(x => x.ContractTypeTitle).HasMaxLength(Name);
                b.Property(x => x.SelectionMethodTitle).HasMaxLength(Name);
                b.Property(x => x.PartyName).HasMaxLength(Title);
                b.Property(x => x.EmployerRank).HasMaxLength(Name);
                b.Property(x => x.EmployerField).HasMaxLength(Name);
                b.Property(x => x.ReturnReason).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.Property(x => x.EstimatedAmount).HasPrecision(22, 2);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<CtDealStep>(b =>
            {
                b.ToTable("CtDealSteps");
                b.Property(x => x.Role).HasMaxLength(Code);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne(x => x.DealRequest).WithMany(x => x.Steps).HasForeignKey(x => x.DealRequestId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<CtHistory>(b =>
            {
                b.ToTable("CtHistory");
                b.Property(x => x.OwnerType).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Action).HasMaxLength(Code);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => new { x.OwnerType, x.OwnerId });
            });
            builder.Entity<CtTenderRun>(b =>
            {
                b.ToTable("CtTenderRuns");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Notes).HasMaxLength(Text);
                b.Property(x => x.WinnerReason).HasMaxLength(Text);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.Property(x => x.GuaranteeAmount).HasPrecision(22, 2);
                b.HasOne(x => x.DealRequest).WithMany().HasForeignKey(x => x.DealRequestId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<CtTenderParticipant>(b =>
            {
                b.ToTable("CtTenderParticipants");
                b.Property(x => x.CompanyName).HasMaxLength(Title);
                b.Property(x => x.Notes).HasMaxLength(Text);
                b.Property(x => x.DisqualifyReason).HasMaxLength(Text);
                b.Property(x => x.OfferAmount).HasPrecision(22, 2);
                b.Property(x => x.TechnicalScore).HasPrecision(9, 2);
                b.HasOne(x => x.TenderRun).WithMany(x => x.Participants).HasForeignKey(x => x.TenderRunId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<CtContractDraft>(b =>
            {
                b.ToTable("CtContractDrafts");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.ContractNumber).HasMaxLength(Code);
                b.HasIndex(x => x.ContractNumber).IsUnique().HasFilter("[Deleted] = 0 AND [ContractNumber] IS NOT NULL");
                b.Property(x => x.CounterpartyName).HasMaxLength(Title);
                b.Property(x => x.ContractKindTitle).HasMaxLength(Name);
                b.Property(x => x.TemplateCode).HasMaxLength(Code);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Subject).HasMaxLength(LongText);
                b.Property(x => x.PaymentTerms).HasMaxLength(LongText);
                b.Property(x => x.SpecialConditions).HasMaxLength(LongText);
                b.Property(x => x.CounterpartyComments).HasMaxLength(LongText);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.Property(x => x.ContractAmount).HasPrecision(22, 2);
                b.Property(x => x.AdvancePercent).HasPrecision(9, 4);
                b.Property(x => x.RetentionPercent).HasPrecision(9, 4);
                b.Property(x => x.PerformanceGuaranteePercent).HasPrecision(9, 4);
                b.HasOne<CtDealRequest>().WithMany().HasForeignKey(x => x.DealRequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<CtTenderRun>().WithMany().HasForeignKey(x => x.TenderRunId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Contracts.ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.DealRequestId);
                b.HasIndex(x => x.PurchaseOrderId);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<CtCbsItem>(b =>
            {
                b.ToTable("CtCbsItems");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.HasIndex(x => new { x.PurchaseOrderId, x.Code });
                b.HasIndex(x => new { x.ContractId, x.Code });
                b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Contracts.ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });

            // ---------------------------------------------------------------------------------------------- progress statements
            builder.Entity<PsStatement>(b =>
            {
                b.ToTable("PsStatements");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.PurchaseOrderId, x.Serial }).IsUnique().HasFilter("[Deleted] = 0 AND [PurchaseOrderId] IS NOT NULL");
                b.HasIndex(x => new { x.ContractId, x.Serial }).HasFilter("[Deleted] = 0 AND [ContractId] IS NOT NULL AND [PurchaseOrderId] IS NULL");
                b.HasIndex(x => x.Status);
                b.Property(x => x.Subject).HasMaxLength(Title);
                b.HasOne<Entities.Model.NasbNiro.Contracts.ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PsStatement>().WithMany().HasForeignKey(x => x.ProvisionalStatementId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.ProvisionalStatementId);
                b.HasIndex(x => x.PaymentRequestId);
                b.Property(x => x.SupportingDocuments).HasMaxLength(Text);
                b.Property(x => x.CounterpartyComments).HasMaxLength(LongText);
                b.Property(x => x.ChequeNumber).HasMaxLength(Code);
                b.Property(x => x.ReturnReason).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                foreach (string money in new[] { nameof(PsStatement.ContractAmount), nameof(PsStatement.AddendaAmount), nameof(PsStatement.PreviousPayments), nameof(PsStatement.NonconformityDeductions),
                    nameof(PsStatement.LegalDeductions), nameof(PsStatement.GrossThisPeriod), nameof(PsStatement.PayableThisPeriod), nameof(PsStatement.TechnicalApprovedAmount),
                    nameof(PsStatement.SupervisionApprovedAmount), nameof(PsStatement.ExpectedAmount), nameof(PsStatement.ReceivedAmount), nameof(PsStatement.PaymentVariance) })
                    b.Property(money).HasPrecision(22, 2);
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PsStatementLine>(b =>
            {
                b.ToTable("PsStatementLines");
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.SerialOrDrawing).HasMaxLength(Name);
                b.Property(x => x.ProjectCode).HasMaxLength(Code);
                b.Property(x => x.EquipmentCode).HasMaxLength(Code);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.ProgressPercent).HasPrecision(9, 2);
                foreach (string money in new[] { nameof(PsStatementLine.UnitPrice), nameof(PsStatementLine.LineTotal), nameof(PsStatementLine.AdvanceAmount), nameof(PsStatementLine.AfterDeliveryQcAmount),
                    nameof(PsStatementLine.GoodPerformanceAmount), nameof(PsStatementLine.AfterFinalTestsAmount), nameof(PsStatementLine.ThisPeriodAmount) })
                    b.Property(money).HasPrecision(22, 2);
                b.HasOne(x => x.Statement).WithMany(x => x.Lines).HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<CtCbsItem>().WithMany().HasForeignKey(x => x.CbsItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.CbsItemId);
                b.HasIndex(x => x.OrderLineId);
            });
            builder.Entity<PsPreviousPayment>(b =>
            {
                b.ToTable("PsPreviousPayments");
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.PaymentDocNumber).HasMaxLength(Code);
                b.Property(x => x.MethodTitle).HasMaxLength(Name);
                b.Property(x => x.PaymentTitle).HasMaxLength(Title);
                b.Property(x => x.StatementSerial).HasMaxLength(Code);
                b.HasOne(x => x.Statement).WithMany(x => x.PreviousPaymentRows).HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PsDeduction>(b =>
            {
                b.ToTable("PsDeductions");
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.ItemTitle).HasMaxLength(Title);
                b.Property(x => x.SerialNumber).HasMaxLength(Name);
                b.Property(x => x.Basis).HasMaxLength(Name);
                b.Property(x => x.DeviationType).HasMaxLength(Name);
                b.Property(x => x.LetterNumber).HasMaxLength(Code);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.UnitPrice).HasPrecision(22, 2);
                b.Property(x => x.Percent).HasPrecision(9, 4);
                b.Property(x => x.BaseAmount).HasPrecision(22, 2);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.HasOne(x => x.Statement).WithMany(x => x.Deductions).HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<Entities.Model.NasbNiro.Inventory.NonConformance>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PsLegalRate>().WithMany().HasForeignKey(x => x.LegalRateId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.NcrId);
                b.HasIndex(x => x.LegalRateId);
            });
            builder.Entity<PsLegalRate>(b =>
            {
                b.ToTable("PsLegalRates");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Percent).HasPrecision(9, 4);
                b.Property(x => x.BasisNote).HasMaxLength(Text);
                b.Property(x => x.SourceReference).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
            });
            builder.Entity<PsCashPlanItem>(b =>
            {
                b.ToTable("PsCashPlan");
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => x.PurchaseOrderId);
                b.HasIndex(x => x.ContractId);
            });

            // ---------------------------------------------------------------------------------------------- petty-cash flow
            builder.Entity<FnPettyCashRequest>(b =>
            {
                b.ToTable("FnPettyCashRequests");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.Property(x => x.PaymentReference).HasMaxLength(Name);
                b.HasOne(x => x.PettyCash).WithMany().HasForeignKey(x => x.PettyCashId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnPettyCashSummary>(b =>
            {
                b.ToTable("FnPettyCashSummaries");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.PettyCashId, x.PeriodFrom });
                b.Property(x => x.TotalInWords).HasMaxLength(Title);
                b.Property(x => x.Notes).HasMaxLength(Text);
                b.Property(x => x.ReturnReason).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                foreach (string money in new[] { nameof(FnPettyCashSummary.PreviousBalance), nameof(FnPettyCashSummary.Charge), nameof(FnPettyCashSummary.TotalAmount), nameof(FnPettyCashSummary.Balance) })
                    b.Property(money).HasPrecision(22, 2);
                b.HasOne(x => x.PettyCash).WithMany().HasForeignKey(x => x.PettyCashId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnPettyCashSummaryLine>(b =>
            {
                b.ToTable("FnPettyCashSummaryLines");
                b.Property(x => x.InvoiceNumber).HasMaxLength(Code);
                b.Property(x => x.PrApprovalNumber).HasMaxLength(Code);
                b.Property(x => x.WarehouseReceiptNumber).HasMaxLength(Code);
                b.Property(x => x.ProjectCode).HasMaxLength(Code);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.RecipientName).HasMaxLength(Title);
                b.Property(x => x.TrackingNumber).HasMaxLength(Name);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.HasOne(x => x.Summary).WithMany(x => x.Lines).HasForeignKey(x => x.SummaryId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<FnPettyCashSettlementLine>().WithMany().HasForeignKey(x => x.SettlementLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PurchaseRequest>().WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Inventory.InventoryDocument>().WithMany().HasForeignKey(x => x.ReceiptDocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.SettlementLineId);   // not unique: a rejected summary keeps its rows, the service refuses a second live use
                b.HasIndex(x => x.PurchaseRequestId);
                b.HasIndex(x => x.ReceiptDocumentId);
            });
            builder.Entity<FnPettyCashVehicleForm>(b =>
            {
                b.ToTable("FnPettyCashVehicleForms");
                b.Property(x => x.Unit).HasMaxLength(Name);
                b.Property(x => x.VehicleType).HasMaxLength(Name);
                b.Property(x => x.Origin).HasMaxLength(Title);
                b.Property(x => x.Destination).HasMaxLength(Title);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.HasIndex(x => x.LineId).IsUnique();
                b.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnPettyCashMiscForm>(b =>
            {
                b.ToTable("FnPettyCashMiscForms");
                b.Property(x => x.RequestingUnit).HasMaxLength(Name);
                b.Property(x => x.ProjectCode).HasMaxLength(Code);
                b.Property(x => x.WhyNoInvoice).HasMaxLength(Text);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.HasIndex(x => x.LineId).IsUnique();
                b.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
