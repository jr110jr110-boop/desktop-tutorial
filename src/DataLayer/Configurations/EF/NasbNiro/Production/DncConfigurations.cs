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
                b.Property(x => x.CamSystem).HasMaxLength(Code);
                b.HasOne<MfPostProcessor>().WithMany().HasForeignKey(x => x.PostProcessorId).OnDelete(DeleteBehavior.Restrict);
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
            // P2 part B: live monitoring
            builder.Entity<MfMachineMonitor>(b =>
            {
                b.ToTable("MfMachineMonitors");
                b.HasIndex(x => x.MachineId).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Host).HasMaxLength(Name);
                b.Property(x => x.LastError).HasMaxLength(Text);
                b.Property(x => x.LastProgramName).HasMaxLength(Name);
                b.Property(x => x.MismatchAlertOpen).HasMaxLength(20);
                b.Property(x => x.Note).HasMaxLength(Text);
                // P2 part E: the data link and the start interlock
                b.Property(x => x.DataHost).HasMaxLength(Name);
                b.Property(x => x.DataCredentialKey).HasMaxLength(Code);
                b.Property(x => x.PlcMap).HasMaxLength(Text);
                b.Property(x => x.ProgramFolder).HasMaxLength(Name);
                b.Property(x => x.ResultFile).HasMaxLength(Name);
                b.Property(x => x.Capabilities).HasMaxLength(LongText);
                b.Property(x => x.DataError).HasMaxLength(Text);
                b.Property(x => x.LastProgramSha256).HasMaxLength(64).IsUnicode(false);
                b.Property(x => x.InterlockHost).HasMaxLength(Name);
                b.Property(x => x.StartBlockReason).HasMaxLength(Text);
                b.Property(x => x.InterlockError).HasMaxLength(Text);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfMachineStateEvent>(b =>
            {
                b.ToTable("MfMachineStateEvents");
                b.HasIndex(x => new { x.MachineId, x.StartAt });
                b.HasIndex(x => new { x.MachineId, x.EndAt });
                b.Property(x => x.AlarmCode).HasMaxLength(Code);
                b.Property(x => x.AlarmText).HasMaxLength(Text);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfMaintenanceRequest>().WithMany().HasForeignKey(x => x.MaintenanceRequestId).OnDelete(DeleteBehavior.Restrict);
            });
            // P2 part C: checklists, staff performance events, cameras
            builder.Entity<MfChecklistTemplate>(b =>
            {
                b.ToTable("MfChecklistTemplates");
                b.HasIndex(x => x.OperationTemplateId).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.RequiredToFinish).HasDefaultValue(true).HasSentinel(true);
                b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<OperationTemplate>().WithMany().HasForeignKey(x => x.OperationTemplateId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfChecklistItem>(b =>
            {
                b.ToTable("MfChecklistItems");
                b.Property(x => x.Text).HasMaxLength(Text).IsRequired();
                b.Property(x => x.Nominal).HasPrecision(18, 4);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(20);
                b.Property(x => x.Mandatory).HasDefaultValue(true).HasSentinel(true);
                b.Property(x => x.Characteristic).HasMaxLength(Code);
                b.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
            });
            builder.Entity<MfChecklistResult>(b =>
            {
                b.ToTable("MfChecklistResults");
                b.HasIndex(x => x.OperationId).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.ReviewNote).HasMaxLength(Text);
                b.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.ResultId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfChecklistTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfChecklistAnswer>(b =>
            {
                b.ToTable("MfChecklistAnswers");
                b.HasIndex(x => new { x.ResultId, x.ItemId }).IsUnique();
                b.Property(x => x.Value).HasPrecision(18, 4);
                b.Property(x => x.Text).HasMaxLength(Text);
                b.HasOne<MfChecklistItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<HrPerformanceEvent>(b =>
            {
                b.ToTable("HrPerformanceEvents");
                b.HasIndex(x => new { x.EmployeeId, x.EventDate });
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Description).HasMaxLength(LongText);
                b.Property(x => x.DecisionNote).HasMaxLength(Text);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<NonConformance>().WithMany().HasForeignKey(x => x.NcrId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfCamera>(b =>
            {
                b.ToTable("MfCameras");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.SnapshotUrl).HasMaxLength(Text);
                b.Property(x => x.StreamUrl).HasMaxLength(Text);
                b.Property(x => x.CredentialKey).HasMaxLength(Code);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfNcRun>(b =>
            {
                b.ToTable("MfNcRuns");
                b.HasIndex(x => x.OperationId);
                b.HasIndex(x => new { x.MachineId, x.StartedAt });
                b.HasIndex(x => x.RevisionId);
                b.Property(x => x.VerifiedSha256).HasMaxLength(64);
                b.HasOne<MfGateOverride>().WithMany().HasForeignKey(x => x.OverrideId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfNcTransfer>().WithMany().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ P2 part E: the guarded machining process
            builder.Entity<MfMachineActivity>(b =>
            {
                b.ToTable("MfMachineActivities");
                b.HasIndex(x => new { x.MachineId, x.At });
                b.HasIndex(x => new { x.OperationId, x.At });
                b.Property(x => x.Value).HasMaxLength(Name);
                b.Property(x => x.Text).HasMaxLength(Text);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfGateOverride>(b =>
            {
                b.ToTable("MfGateOverrides");
                b.HasIndex(x => new { x.MachineId, x.OperationId, x.Gate });
                b.Property(x => x.Gate).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Reason).HasMaxLength(Text).IsRequired();
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfPostProcessor>(b =>
            {
                b.ToTable("MfPostProcessors");
                b.HasIndex(x => new { x.Code, x.Version }).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.CamSystem).HasMaxLength(Code).IsRequired();
                b.Property(x => x.FileName).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
                b.Property(x => x.ChangeNote).HasMaxLength(Text);
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfNcAttachment>(b =>
            {
                b.ToTable("MfNcAttachments");
                b.HasIndex(x => new { x.RevisionId, x.Kind });
                b.Property(x => x.FileName).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
                b.HasOne<MfNcRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfCamTool>(b =>
            {
                b.ToTable("MfCamTools");
                b.HasIndex(x => new { x.RevisionId, x.ToolNumber }).IsUnique();
                b.Property(x => x.Name).HasMaxLength(Name);
                b.Property(x => x.Diameter).HasPrecision(18, 4);
                b.Property(x => x.Length).HasPrecision(18, 4);
                b.HasOne<MfNcRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<MfMeasurement>(b =>
            {
                b.ToTable("MfMeasurements");
                b.HasIndex(x => new { x.OperationId, x.Characteristic, x.At });
                b.HasIndex(x => new { x.MachineId, x.At });
                b.Property(x => x.Characteristic).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Value).HasPrecision(18, 4);
                b.Property(x => x.Device).HasMaxLength(Name);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfMeasureStation>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfMeasureStation>(b =>
            {
                b.ToTable("MfMeasureStations");
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.KeyHash).HasMaxLength(64);
                b.Property(x => x.AllowedAddresses).HasMaxLength(Name);
                b.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
