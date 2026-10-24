using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Production
{
    // Production / shop floor (MES), release P1 part A. All tables start with "Mf". Deletes are restricted except for child lines, which go with their header.
    public static class ProductionModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddProductionConfigurations(this ModelBuilder builder)
        {
            // ------------------------------------------------------------------ master data
            builder.Entity<MfWorkCenter>(b =>
            {
                b.ToTable("MfWorkCenters");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.HoursPerShift).HasPrecision(9, 2);
                b.Property(x => x.EfficiencyPercent).HasPrecision(9, 2);
                b.Property(x => x.UtilisationPercent).HasPrecision(9, 2);
                b.Property(x => x.MachineRateRial).HasPrecision(18, 2);
                b.Property(x => x.LabourRateRial).HasPrecision(18, 2);
                b.Property(x => x.QueueDaysDefault).HasPrecision(9, 2);
                b.Property(x => x.CostCenter).HasMaxLength(Code);
                b.HasOne<PcCalendar>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfMachineInfo>(b =>
            {
                b.ToTable("MfMachineInfos");
                b.HasIndex(x => x.MachineId).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.WorkCenterId);
                b.Property(x => x.Maker).HasMaxLength(Name);
                b.Property(x => x.Model).HasMaxLength(Name);
                b.Property(x => x.SerialNumber).HasMaxLength(Name);
                b.Property(x => x.Capabilities).HasMaxLength(Text);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnAsset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfOperationStandard>(b =>
            {
                b.ToTable("MfOperationStandards");
                b.HasIndex(x => x.OperationTemplateId).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.SetupHours).HasPrecision(9, 2);
                b.Property(x => x.RunHoursPerUnit).HasPrecision(9, 4);
                b.Property(x => x.MoveDays).HasPrecision(9, 2);
                b.Property(x => x.QueueDays).HasPrecision(9, 2);
                b.Property(x => x.DrawingReferenceRule).HasMaxLength(Name);
                b.Property(x => x.RequiredQualificationKind).HasMaxLength(Code);
                b.Property(x => x.RequiredQualificationProcess).HasMaxLength(Code);
                b.HasOne<OperationTemplate>().WithMany().HasForeignKey(x => x.OperationTemplateId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Provider>().WithMany().HasForeignKey(x => x.DefaultSupplierId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.WorkInstructionDocumentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfRoutingRevision>(b =>
            {
                b.ToTable("MfRoutingRevisions");
                b.HasIndex(x => new { x.ItemId, x.Revision }).IsUnique().HasFilter("[Deleted] = 0 AND [ItemId] IS NOT NULL");
                b.HasIndex(x => new { x.ItemGroupId, x.Revision }).IsUnique().HasFilter("[Deleted] = 0 AND [ItemGroupId] IS NOT NULL");
                b.Property(x => x.Note).HasMaxLength(Text);
                b.Property(x => x.ContentHash).HasMaxLength(Name);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ItemGroup>().WithMany().HasForeignKey(x => x.ItemGroupId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfReasonCode>(b =>
            {
                b.ToTable("MfReasonCodes");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => new { x.Kind, x.Code }).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
            });

            // ------------------------------------------------------------------ production orders
            builder.Entity<MfProductionOrder>(b =>
            {
                b.ToTable("MfProductionOrders");
                b.Property(x => x.Number).HasMaxLength(Name).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.WorkOrderId);
                b.HasIndex(x => x.ProcessId);
                b.HasIndex(x => x.Status);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.Property(x => x.SerialsPlanned).HasMaxLength(LongText);
                b.Property(x => x.ReleaseOverrideReason).HasMaxLength(Text);
                b.Property(x => x.ShortageText).HasMaxLength(LongText);
                b.Property(x => x.KitOverrideReason).HasMaxLength(Text);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ProcessEntity>().WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Bom>().WithMany().HasForeignKey(x => x.BomId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfRoutingRevision>().WithMany().HasForeignKey(x => x.RoutingRevisionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.KitRequest).WithMany().HasForeignKey(x => x.KitRequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryDocument>().WithMany().HasForeignKey(x => x.ProductionReceiptId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfOrderComponent>(b =>
            {
                b.ToTable("MfOrderComponents");
                b.HasIndex(x => x.OrderId);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.HasOne<MfProductionOrder>().WithMany(o => o.Components).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfOperation>(b =>
            {
                b.ToTable("MfOperations");
                b.HasIndex(x => new { x.OrderId, x.Sequence });
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.WorkCenterId);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.SetupHoursPlanned).HasPrecision(9, 2);
                b.Property(x => x.RunHoursPlanned).HasPrecision(12, 4);
                b.Property(x => x.GoodQty).HasPrecision(18, 4);
                b.Property(x => x.ScrapQty).HasPrecision(18, 4);
                b.Property(x => x.ReworkQty).HasPrecision(18, 4);
                b.Property(x => x.SkipReason).HasMaxLength(Text);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne<MfProductionOrder>().WithMany(o => o.Operations).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<OperationTemplate>().WithMany().HasForeignKey(x => x.OperationTemplateId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Provider>().WithMany().HasForeignKey(x => x.OutsideSupplierId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.ReworkOfOperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.QcRecord).WithMany().HasForeignKey(x => x.QcRecordId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Ncr).WithMany().HasForeignKey(x => x.NcrId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ItpActivity>().WithMany().HasForeignKey(x => x.ItpActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Procurement.PurchaseRequest>().WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Procurement.PurchaseRequestLine>().WithMany().HasForeignKey(x => x.PurchaseRequestLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Procurement.PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryDocument>().WithMany().HasForeignKey(x => x.SubcontractIssueId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfOperationPart>(b =>
            {
                b.ToTable("MfOperationParts");
                b.HasIndex(x => x.OperationId);
                b.HasIndex(x => x.PartOperationId);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.PartOperation).WithMany().HasForeignKey(x => x.PartOperationId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ time booking and maintenance requests
            builder.Entity<MfTimeBooking>(b =>
            {
                b.ToTable("MfTimeBookings");
                b.HasIndex(x => x.OperationId);
                b.HasIndex(x => new { x.EmployeeId, x.StartAt });
                // one running booking per employee, guaranteed by the database as well
                b.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("[Deleted] = 0 AND [EndAt] IS NULL").HasDatabaseName("IX_MfTimeBookings_OneRunning");
                b.Property(x => x.Hours).HasPrecision(9, 2);
                b.Property(x => x.GoodQty).HasPrecision(18, 4);
                b.Property(x => x.ScrapQty).HasPrecision(18, 4);
                b.Property(x => x.ReworkQty).HasPrecision(18, 4);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.Property(x => x.SupervisorReason).HasMaxLength(Text);
                b.Property(x => x.EditReason).HasMaxLength(Text);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfReasonCode>().WithMany().HasForeignKey(x => x.ScrapReasonCodeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfReasonCode>().WithMany().HasForeignKey(x => x.ReasonCodeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MfMaintenanceRequest>(b =>
            {
                b.ToTable("MfMaintenanceRequests");
                b.Property(x => x.Number).HasMaxLength(Name).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.MachineId, x.Status });
                b.Property(x => x.ReportedAtText).HasMaxLength(Code);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<MfReasonCode>().WithMany().HasForeignKey(x => x.ReasonCodeId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
