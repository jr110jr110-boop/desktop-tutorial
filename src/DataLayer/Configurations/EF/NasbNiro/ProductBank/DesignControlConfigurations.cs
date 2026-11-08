using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Inventory
{
    // Package A1 (design and development control): ten new tables, no change to an existing table. No migration is written here. Tables start with "PbDesign".
    public static class DesignControlModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddDesignControlConfigurations(this ModelBuilder builder)
        {
            builder.Entity<PbDesignPlan>(b =>
            {
                b.ToTable("PbDesignPlans");
                b.Property(x => x.Number).HasMaxLength(40).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Scope).HasMaxLength(2000);
                b.Property(x => x.ValidationWaiverReason).HasMaxLength(1000);
                b.Property(x => x.ReturnNote).HasMaxLength(1000);
                b.Property(x => x.InputsCompleteNote).HasMaxLength(1000);
                b.Property(x => x.NotApplicableKinds).HasMaxLength(50);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.Property(x => x.ContentHash).HasMaxLength(80);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.ProductId, x.ProductRevision, x.Version }).IsUnique().HasFilter(NotDeleted);
                // one plan in force and one plan in preparation per product revision
                b.HasIndex(x => new { x.ProductId, x.ProductRevision }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] = 2").HasDatabaseName("UX_PbDesignPlans_ApprovedPerRevision");
                b.HasIndex(x => new { x.ProductId, x.ProductRevision }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] IN (0, 1)").HasDatabaseName("UX_PbDesignPlans_OpenPerRevision");
                b.HasMany(x => x.Stages).WithOne(x => x.Plan).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Interfaces).WithOne(x => x.Plan).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PbDesignStage>(b =>
            {
                b.ToTable("PbDesignStages");
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.ResponsibleRole).HasMaxLength(40);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => new { x.PlanId, x.Seq }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.PlanId, x.Kind });
            });
            builder.Entity<PbDesignInterface>(b =>
            {
                b.ToTable("PbDesignInterfaces");
                b.Property(x => x.FromGroup).HasMaxLength(100).IsRequired();
                b.Property(x => x.ToGroup).HasMaxLength(100).IsRequired();
                b.Property(x => x.Subject).HasMaxLength(500).IsRequired();
                b.Property(x => x.ResponsibleRole).HasMaxLength(40);
                b.HasIndex(x => x.PlanId);
            });
            builder.Entity<PbDesignInput>(b =>
            {
                b.ToTable("PbDesignInputs");
                b.Property(x => x.Code).HasMaxLength(40).IsRequired();
                b.Property(x => x.Statement).HasMaxLength(1000).IsRequired();
                b.Property(x => x.Source).HasMaxLength(500).IsRequired();
                b.Property(x => x.AcceptanceCriterion).HasMaxLength(1000).IsRequired();
                b.Property(x => x.StandardEdition).HasMaxLength(200);
                b.Property(x => x.PreviousDesignRef).HasMaxLength(300);
                b.Property(x => x.RefText).HasMaxLength(500);
                b.Property(x => x.RefRevisionText).HasMaxLength(100);
                b.Property(x => x.FailureConsequence).HasMaxLength(1000);
                b.Property(x => x.ConflictNote).HasMaxLength(1000);
                b.Property(x => x.ConflictResolution).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.ProductId, x.ProductRevision, x.Status });
            });
            builder.Entity<PbDesignReview>(b =>
            {
                b.ToTable("PbDesignReviews");
                b.Property(x => x.Number).HasMaxLength(40).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Minutes).HasMaxLength(4000);
                b.Property(x => x.OutcomeNote).HasMaxLength(2000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.Property(x => x.ValidationEvidence).HasMaxLength(2000);
                b.Property(x => x.CustomerAcceptanceRef).HasMaxLength(500);
                b.Property(x => x.CustomerAcceptanceNote).HasMaxLength(1000);
                b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.ProductId, x.ProductRevision, x.Type, x.Status });
                b.HasIndex(x => x.PlanId);
                b.HasMany(x => x.Participants).WithOne(x => x.Review).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Inputs).WithOne(x => x.Review).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Outputs).WithOne(x => x.Review).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Findings).WithOne(x => x.Review).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Traces).WithOne(x => x.Review).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PbDesignReviewParticipant>(b =>
            {
                b.ToTable("PbDesignReviewParticipants");
                b.Property(x => x.RoleText).HasMaxLength(100);
                b.HasIndex(x => new { x.ReviewId, x.UserId }).IsUnique().HasFilter(NotDeleted);
            });
            builder.Entity<PbDesignReviewInput>(b =>
            {
                b.ToTable("PbDesignReviewInputs");
                b.HasIndex(x => new { x.ReviewId, x.InputId }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.InputId);
            });
            builder.Entity<PbDesignReviewOutput>(b =>
            {
                b.ToTable("PbDesignReviewOutputs");
                b.Property(x => x.Label).HasMaxLength(300);
                b.Property(x => x.RevisionText).HasMaxLength(100);
                b.Property(x => x.ContentHash).HasMaxLength(80);
                b.HasIndex(x => new { x.ReviewId, x.Kind, x.RevisionId, x.BomHeaderId, x.OpcRoutingId }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.Kind, x.BomHeaderId });
                b.HasIndex(x => new { x.Kind, x.RevisionId });
            });
            builder.Entity<PbDesignTrace>(b =>
            {
                b.ToTable("PbDesignTraces");
                b.Property(x => x.Evidence).HasMaxLength(1000);
                b.Property(x => x.EvidenceRef).HasMaxLength(300);
                b.HasIndex(x => new { x.ReviewId, x.InputId, x.OutputId }).IsUnique().HasFilter(NotDeleted);
            });
            builder.Entity<PbDesignFinding>(b =>
            {
                b.ToTable("PbDesignFindings");
                b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
                b.Property(x => x.DoneNote).HasMaxLength(1000);
                b.Property(x => x.CloseNote).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.ReviewId, x.Seq }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.OwnerUserId, x.Status });
            });
        }
    }
}
