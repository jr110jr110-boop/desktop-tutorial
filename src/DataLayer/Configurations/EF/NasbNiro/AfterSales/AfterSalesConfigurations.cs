using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.AfterSales
{
    // After-sales service, release S1 part B. Tables start with "Sv"; the survey keeps the "Sl" prefix. Deletes are restricted except for child lines, which go with their header.
    public static class AfterSalesModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddAfterSalesConfigurations(this ModelBuilder builder)
        {
            builder.Entity<SvSetting>(b =>
            {
                b.ToTable("SvSettings");
                b.Property(x => x.Key).HasMaxLength(Name).IsRequired();
                b.HasIndex(x => x.Key).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Text).HasMaxLength(Name);
                b.Property(x => x.Title).HasMaxLength(Title);
            });
            builder.Entity<SvSlaRule>(b =>
            {
                b.ToTable("SvSlaRules");
                b.HasIndex(x => x.Priority).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.ResponseHours).HasPrecision(9, 2);
                b.Property(x => x.ResolutionHours).HasPrecision(9, 2);
            });

            // ------------------------------------------------------------------ installed base
            builder.Entity<SvInstalledUnit>(b =>
            {
                b.ToTable("SvInstalledUnits");
                b.Property(x => x.Tag).HasMaxLength(Name).IsRequired();
                b.HasIndex(x => x.Tag).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.CustomerId);
                b.HasIndex(x => x.ShipmentId);
                b.HasIndex(x => x.SerialUnitId);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.RunningHours).HasPrecision(18, 2);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcShipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SerialUnit>().WithMany().HasForeignKey(x => x.SerialUnitId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SvUnitSpare>(b =>
            {
                b.ToTable("SvUnitSpares");
                b.HasIndex(x => x.UnitId);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne<SvInstalledUnit>().WithMany(u => u.Spares).HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ requests
            builder.Entity<SvServiceRequest>(b =>
            {
                b.ToTable("SvServiceRequests");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.CustomerId);
                b.HasIndex(x => x.UnitId);
                b.Property(x => x.Channel).HasMaxLength(Name);
                b.Property(x => x.Description).HasMaxLength(LongText);
                b.Property(x => x.Resolution).HasMaxLength(LongText);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SlContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SvInstalledUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            });
            // the complaint points to its request (the only key between the two; the request finds the complaint by it). One complaint per request.
            builder.Entity<CustomerComplaint>(b =>
            {
                b.HasIndex(x => x.ServiceRequestId).IsUnique().HasFilter("[ServiceRequestId] IS NOT NULL");
                b.HasOne(x => x.ServiceRequest).WithMany().HasForeignKey(x => x.ServiceRequestId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlActivity>(b =>
            {
                b.HasOne<SvServiceRequest>().WithMany().HasForeignKey(x => x.ServiceRequestId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ orders
            builder.Entity<SvServiceOrder>(b =>
            {
                b.ToTable("SvServiceOrders");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.UnitId);
                b.HasIndex(x => x.RequestId);
                // the orders whose labour waits for the month-end run (round 3: an order taken into a pool leaves the index)
                b.HasIndex(x => x.LabourPendingPeriod).HasFilter("[LabourPendingPeriod] IS NOT NULL AND [LabourAggregatedAt] IS NULL");
                // one visit order for a unit and a visit date of an agreement (the job is idempotent)
                b.HasIndex(x => new { x.LtsaContractId, x.UnitId, x.PlannedStart }).IsUnique().HasFilter("[Deleted] = 0 AND [LtsaContractId] IS NOT NULL AND [Status] <> 5");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.TravelNote).HasMaxLength(Text);
                b.Property(x => x.CostCenterCode).HasMaxLength(Code);
                b.Property(x => x.DecisionNote).HasMaxLength(Text);
                b.Property(x => x.GoodwillReason).HasMaxLength(Text);
                b.Property(x => x.Findings).HasMaxLength(LongText);
                b.Property(x => x.Recommendations).HasMaxLength(LongText);
                b.Property(x => x.RunningHoursReading).HasPrecision(18, 2);
                b.Property(x => x.LabourHours).HasPrecision(18, 4);
                b.Property(x => x.LabourAmount).HasPrecision(22, 2);
                b.Property(x => x.LabourPayrollCost).HasPrecision(28, 10);
                b.Property(x => x.LabourPooledAmount).HasPrecision(22, 2);
                b.Property(x => x.PartsAmount).HasPrecision(22, 2);
                b.Property(x => x.BillableLabourRateRial).HasPrecision(22, 2);
                b.Property(x => x.DraftNote).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.HasOne<SvServiceRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SvInstalledUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SvContract>().WithMany().HasForeignKey(x => x.LtsaContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.WarrantyEvent).WithMany().HasForeignKey(x => x.WarrantyEventId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SvOrderEngineer>(b =>
            {
                b.ToTable("SvOrderEngineers");
                b.HasIndex(x => new { x.OrderId, x.EmployeeId }).IsUnique();
                b.HasOne<SvServiceOrder>().WithMany(o => o.Engineers).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SvOrderTask>(b =>
            {
                b.ToTable("SvOrderTasks");
                b.HasIndex(x => x.OrderId);
                b.Property(x => x.Description).HasMaxLength(Text).IsRequired();
                b.HasOne<SvServiceOrder>().WithMany(o => o.Tasks).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<SvTimeEntry>(b =>
            {
                b.ToTable("SvTimeEntries");
                b.HasIndex(x => x.OrderId);
                b.HasIndex(x => x.EmployeeId);
                b.Property(x => x.Hours).HasPrecision(9, 2);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne<SvServiceOrder>().WithMany(o => o.TimeEntries).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SvMeasurement>(b =>
            {
                b.ToTable("SvMeasurements");
                b.HasIndex(x => x.OrderId);
                b.Property(x => x.Name).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Value).HasPrecision(22, 6);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne<SvServiceOrder>().WithMany(o => o.Measurements).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            });

            // parts: the warehouse documents that name the order
            builder.Entity<InventoryDocument>(b =>
            {
                b.HasIndex(x => x.ServiceOrderId);
                b.HasOne<SvServiceOrder>().WithMany().HasForeignKey(x => x.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ agreements
            builder.Entity<SvContract>(b =>
            {
                b.ToTable("SvContracts");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.CustomerId);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Scope).HasMaxLength(LongText);
                b.Property(x => x.PriceRial).HasPrecision(22, 2);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SvContractUnit>(b =>
            {
                b.ToTable("SvContractUnits");
                b.HasIndex(x => new { x.ContractId, x.UnitId }).IsUnique();
                b.HasOne<SvContract>().WithMany(c => c.Units).HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<SvInstalledUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ surveys
            builder.Entity<SlSurvey>(b =>
            {
                b.ToTable("SlSurveys");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.CustomerId);
                b.HasIndex(x => x.ServiceOrderId).IsUnique().HasFilter("[Deleted] = 0 AND [ServiceOrderId] IS NOT NULL");
                b.HasIndex(x => x.ShipmentId).IsUnique().HasFilter("[Deleted] = 0 AND [ShipmentId] IS NOT NULL");
                b.Property(x => x.Comments).HasMaxLength(LongText);
                b.Property(x => x.AverageScore).HasPrecision(5, 2);
                b.Property(x => x.SkipReason).HasMaxLength(Text);
                b.Property(x => x.RecordedByRole).HasMaxLength(Code);
                b.Property(x => x.VerifyNote).HasMaxLength(Text);
                // R1p D25: one complaint draft per survey, so a re-saved answer never opens a second one
                b.HasIndex(x => x.ComplaintId).IsUnique().HasFilter("[ComplaintId] IS NOT NULL");
                b.HasOne(x => x.Complaint).WithMany().HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnCustomer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcShipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SvServiceOrder>().WithMany().HasForeignKey(x => x.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<SlSurveyAnswer>(b =>
            {
                b.ToTable("SlSurveyAnswers");
                b.HasIndex(x => x.SurveyId);
                b.Property(x => x.Question).HasMaxLength(Text).IsRequired();
                b.HasOne<SlSurvey>().WithMany(s => s.Answers).HasForeignKey(x => x.SurveyId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
