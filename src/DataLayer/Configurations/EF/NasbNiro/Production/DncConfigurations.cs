using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Production
{
    // Production release P2 part A: DNC. All tables are new (additive only). Deletes are restricted; the content row goes with its revision.
    public static class DncModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddDncConfigurations(this ModelBuilder builder)
        {
            builder.Entity<MfMachineConnection>(b =>
            {
                b.ToTable("MfMachineConnections");
                b.HasIndex(x => x.MachineId).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.ControllerModel).HasMaxLength(Name);
                b.Property(x => x.Host).HasMaxLength(Name);
                b.Property(x => x.RemotePath).HasMaxLength(Text);
                b.Property(x => x.CredentialKey).HasMaxLength(Code);
                b.Property(x => x.ProgramNameFormat).HasMaxLength(Code);
                b.Property(x => x.FileExtension).HasMaxLength(10);
                b.Property(x => x.Axes).HasMaxLength(20);
                b.Property(x => x.GatewayKeyHash).HasMaxLength(64);
                b.Property(x => x.AllowedAddresses).HasMaxLength(Text);
                b.Property(x => x.Note).HasMaxLength(Text);
                // the default is the value itself (see MfWorkCenter.RequiresMachine): without the sentinel a false would never be stored
                b.Property(x => x.RequireConfirmedProgram).HasDefaultValue(true).HasSentinel(true);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfNcProgram>(b =>
            {
                b.ToTable("MfNcPrograms");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => x.OperationTemplateId);
                b.HasIndex(x => x.ItemId);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OperationTemplate>().WithMany().HasForeignKey(x => x.OperationTemplateId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcRevision>().WithMany().HasForeignKey(x => x.CurrentRevisionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfNcRevision>(b =>
            {
                b.ToTable("MfNcRevisions");
                b.HasIndex(x => new { x.ProgramId, x.Revision }).IsUnique();
                b.Property(x => x.FileName).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
                b.Property(x => x.ProgramNumber).HasMaxLength(Code);
                b.Property(x => x.ChangeNote).HasMaxLength(Text);
                b.Property(x => x.Findings).HasMaxLength(LongText);
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.HasOne<MfNcProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcRevision>().WithMany().HasForeignKey(x => x.BaseRevisionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfNcContent>(b =>
            {
                b.ToTable("MfNcContents");
                b.HasIndex(x => x.RevisionId).IsUnique();
                b.Property(x => x.Bytes).IsRequired();
                b.HasOne(x => x.Revision).WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<MfNcTransfer>(b =>
            {
                b.ToTable("MfNcTransfers");
                b.HasIndex(x => new { x.MachineId, x.Status });
                b.HasIndex(x => x.OperationId);
                b.Property(x => x.TargetName).HasMaxLength(Name);
                b.Property(x => x.LastError).HasMaxLength(Text);
                b.Property(x => x.SentSha256).HasMaxLength(64);
                b.Property(x => x.ConfirmNote).HasMaxLength(Text);
                b.Property(x => x.RequestNote).HasMaxLength(Text);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Revision).WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfNcRun>(b =>
            {
                b.ToTable("MfNcRuns");
                b.HasIndex(x => x.OperationId);
                b.HasIndex(x => new { x.MachineId, x.StartedAt });
                b.HasIndex(x => x.RevisionId);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcTransfer>().WithMany().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
