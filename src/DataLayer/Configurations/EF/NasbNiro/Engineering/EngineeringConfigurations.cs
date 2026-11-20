using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Engineering
{
    // ENG-E1 (engineering module, external workflow): thirteen new tables, no change of an existing table. No migration is written here.
    public static class EngineeringModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddEngineeringConfigurations(this ModelBuilder builder)
        {
            builder.Entity<EnReferenceList>(b =>
            {
                b.ToTable("EnReferenceLists");
                b.Property(x => x.Code).HasMaxLength(60).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.StandardSource).HasMaxLength(200);
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.Property(x => x.SourceFileName).HasMaxLength(260);
                b.HasIndex(x => new { x.Code, x.Revision }).IsUnique().HasFilter(NotDeleted);
                // one Approved list per code
                b.HasIndex(x => x.Code).IsUnique().HasFilter("[Status] = 2 AND [Deleted] = 0").HasDatabaseName("UX_EnReferenceLists_OneApproved");
                b.HasIndex(x => x.Status);
                b.HasMany(x => x.Lines).WithOne(x => x.List).HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<EnReferenceLine>(b =>
            {
                b.ToTable("EnReferenceLines");
                b.Property(x => x.DocumentType).HasMaxLength(100).IsRequired();
                b.Property(x => x.Discipline).HasMaxLength(100);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.StandardClause).HasMaxLength(300);
                b.Property(x => x.Units).HasMaxLength(120);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.ListId);
                b.HasQueryFilter(x => !x.Voided);   // a removed line of a submitted list is kept for the audit trail and never read
            });

            builder.Entity<EnPlatformDossier>(b =>
            {
                b.ToTable("EnPlatformDossiers");
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.Property(x => x.ReferenceCode).HasMaxLength(60).IsRequired();
                b.Property(x => x.StandardBasis).HasMaxLength(500);
                b.Property(x => x.ConfirmedHash).HasMaxLength(64);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => new { x.ProductId, x.ReferenceCode }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasOne<PbProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<EnReferenceList>().WithMany().HasForeignKey(x => x.ReferenceListId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(x => x.Documents).WithOne(x => x.Dossier).HasForeignKey(x => x.DossierId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<EnPlatformDocument>(b =>
            {
                b.ToTable("EnPlatformDocuments");
                b.Property(x => x.DocumentType).HasMaxLength(100).IsRequired();
                b.Property(x => x.Discipline).HasMaxLength(100);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Revision).HasMaxLength(30);
                b.Property(x => x.NotApplicableReason).HasMaxLength(500);
                b.Property(x => x.Units).HasMaxLength(120);
                b.HasIndex(x => x.DossierId);
                b.HasOne<EnReferenceLine>().WithMany().HasForeignKey(x => x.ReferenceLineId).OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<EnCase>(b =>
            {
                b.ToTable("EnCases");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.CustomerRequirementNote).HasMaxLength(2000);
                b.Property(x => x.ApprovedGapHash).HasMaxLength(64);
                b.Property(x => x.Kind).HasDefaultValue(EnCaseKind.Turbine);
                b.Property(x => x.OpinionText).HasMaxLength(2000);
                b.Property(x => x.HandoverListCode).HasMaxLength(60);
                b.Property(x => x.ChangeNote).HasMaxLength(1000);
                b.Property(x => x.ReferenceCode).HasMaxLength(60);
                b.Property(x => x.StandardEvidence).HasMaxLength(500);
                b.Property(x => x.ProposedChangeNote).HasMaxLength(1000);
                b.Property(x => x.ProposalBasis).HasMaxLength(2000);
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.Property(x => x.ClassificationHash).HasMaxLength(64);
                b.Property(x => x.ReturnReason).HasMaxLength(1000);
                b.Property(x => x.TenderHash).HasMaxLength(64);
                b.Property(x => x.ConceptNote).HasMaxLength(2000);
                b.Property(x => x.CharterHash).HasMaxLength(64);
                b.Property(x => x.CharterReviewNote).HasMaxLength(1000);
                b.Property(x => x.ClosedReason).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.InquiryId).IsUnique().HasFilter("[InquiryId] IS NOT NULL AND [Deleted] = 0 AND [Status] < 11").HasDatabaseName("UX_EnCases_Inquiry");
                b.HasIndex(x => x.ContractId).IsUnique().HasFilter("[ContractId] IS NOT NULL AND [Deleted] = 0 AND [Status] < 11").HasDatabaseName("UX_EnCases_Contract");
                b.HasIndex(x => x.Status);
                b.HasOne<SlInquiry>().WithMany().HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PbProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcTenderPackage>().WithMany().HasForeignKey(x => x.TenderPackageId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<EnCaseEvent>(b =>
            {
                b.ToTable("EnCaseEvents");
                b.Property(x => x.Kind).HasMaxLength(40).IsRequired();
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => x.CaseId);
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<EnClarification>(b =>
            {
                b.ToTable("EnClarifications");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Question).HasMaxLength(4000).IsRequired();
                b.Property(x => x.QuestionAttachmentIds).HasMaxLength(400);
                b.Property(x => x.ClientLetterRef).HasMaxLength(100);
                b.Property(x => x.Answer).HasMaxLength(4000);
                b.Property(x => x.AnswerAttachmentIds).HasMaxLength(400);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.CaseId, x.Status });
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<EnClarification>().WithMany().HasForeignKey(x => x.FollowsId).OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<EnHandover>(b =>
            {
                b.ToTable("EnHandovers");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.Property(x => x.ReturnReason).HasMaxLength(1000);
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.CaseId, x.Round }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<EnWaiver>().WithMany().HasForeignKey(x => x.WaiverId).OnDelete(DeleteBehavior.NoAction);
                b.HasMany(x => x.Lines).WithOne(x => x.Handover).HasForeignKey(x => x.HandoverId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<EnHandoverLine>(b =>
            {
                b.ToTable("EnHandoverLines");
                b.Property(x => x.DocumentType).HasMaxLength(100).IsRequired();
                b.Property(x => x.Discipline).HasMaxLength(100);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Revision).HasMaxLength(30);
                b.Property(x => x.LinkPath).HasMaxLength(300);
                b.Property(x => x.Units).HasMaxLength(120);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.HandoverId);
            });

            builder.Entity<EnWaiver>(b =>
            {
                b.ToTable("EnWaivers");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                // one live waiver (requested or approved) per handover
                b.HasIndex(x => x.HandoverId).IsUnique().HasFilter("[Status] IN (0, 1) AND [Deleted] = 0").HasDatabaseName("UX_EnWaivers_OneLive");
                b.HasOne<EnHandover>().WithMany().HasForeignKey(x => x.HandoverId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<EnDocumentDebt>(b =>
            {
                b.ToTable("EnDocumentDebts");
                b.Property(x => x.DocumentType).HasMaxLength(100).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.VoidReason).HasMaxLength(500);
                b.HasIndex(x => new { x.Status, x.DueAt });
                b.HasIndex(x => x.CaseId);
                b.HasIndex(x => new { x.WaiverId, x.HandoverLineId }).IsUnique().HasFilter(NotDeleted);
                b.HasOne<EnWaiver>().WithMany().HasForeignKey(x => x.WaiverId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.HandoverLine).WithMany().HasForeignKey(x => x.HandoverLineId).OnDelete(DeleteBehavior.NoAction);
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<EnDistribution>(b =>
            {
                b.ToTable("EnDistributions");
                b.Property(x => x.Roles).HasMaxLength(200).IsRequired();
                b.Property(x => x.ScopeNote).HasMaxLength(2000);
                b.HasIndex(x => new { x.HandoverId, x.Unit }).IsUnique().HasFilter(NotDeleted);
                b.HasOne<EnHandover>().WithMany().HasForeignKey(x => x.HandoverId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<EnDeviation>(b =>
            {
                b.ToTable("EnDeviations");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.TargetType).HasMaxLength(60).IsRequired();
                b.Property(x => x.StandardCode).HasMaxLength(60).IsRequired();
                b.Property(x => x.StandardClause).HasMaxLength(300);
                b.Property(x => x.Requirement).HasMaxLength(300).IsRequired();
                b.Property(x => x.Instruction).HasMaxLength(2000).IsRequired();
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.TargetType, x.TargetId, x.Status });
                b.HasIndex(x => x.Status);
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
                b.HasOne<PbProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<EnStandardGap>(b =>
            {
                b.ToTable("EnStandardGaps");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.BaseCode).HasMaxLength(60);
                b.Property(x => x.TargetCode).HasMaxLength(60).IsRequired();
                b.Property(x => x.Summary).HasMaxLength(2000);
                b.Property(x => x.EngineeringImpactNote).HasMaxLength(2000);
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.CaseId, x.Status });
                b.HasMany(x => x.Lines).WithOne(x => x.Gap).HasForeignKey(x => x.GapId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
                b.HasOne<PbProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<EnPlatformRegistration>(b =>
            {
                b.ToTable("EnPlatformRegistrations");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.ReferenceCode).HasMaxLength(60).IsRequired();
                b.Property(x => x.StandardBasis).HasMaxLength(500).IsRequired();
                b.Property(x => x.Rationale).HasMaxLength(2000).IsRequired();
                b.Property(x => x.ContentHash).HasMaxLength(100).IsRequired();
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => new { x.ProductId, x.ReferenceCode, x.Status });
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
            });
            builder.Entity<EnInfoRequest>(b =>
            {
                b.ToTable("EnInfoRequests");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Question).HasMaxLength(2000).IsRequired();
                b.Property(x => x.Answer).HasMaxLength(2000);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.CaseId, x.Status });
                b.HasIndex(x => x.Status);
                b.HasOne<EnCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
                b.HasOne<EnHandover>().WithMany().HasForeignKey(x => x.HandoverId).OnDelete(DeleteBehavior.NoAction);
            });
            builder.Entity<EnStandardGapLine>(b =>
            {
                b.ToTable("EnStandardGapLines");
                b.Property(x => x.DocumentType).HasMaxLength(100).IsRequired();
                b.Property(x => x.Discipline).HasMaxLength(100);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.HasIndex(x => x.GapId);
            });
        }
    }
}
