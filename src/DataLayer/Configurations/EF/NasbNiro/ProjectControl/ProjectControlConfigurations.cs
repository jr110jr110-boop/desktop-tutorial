using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.ProjectControl
{
    // Project control (PMO) module tables. All start with "Pc"; legacy tables are not changed (side tables).
    // Deletes are restricted except for child lines, which go with their header.
    public static class ProjectControlModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddProjectControlConfigurations(this ModelBuilder builder)
        {
            // ------------------------------------------------------------------ settings, calendars
            builder.Entity<PcSetting>(b =>
            {
                b.ToTable("PcSettings");
                b.Property(x => x.Key).HasMaxLength(100).IsRequired();
                b.HasIndex(x => x.Key).IsUnique();
                b.Property(x => x.Value).HasPrecision(18, 4);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Source).HasMaxLength(Title);
            });
            builder.Entity<PcCalendar>(b =>
            {
                b.ToTable("PcCalendars");
                b.Property(x => x.Name).HasMaxLength(Name).IsRequired();
                b.Property(x => x.HoursPerDay).HasPrecision(5, 2);
            });
            builder.Entity<PcCalendarException>(b =>
            {
                b.ToTable("PcCalendarExceptions");
                b.Property(x => x.Note).HasMaxLength(Title);
                b.HasIndex(x => new { x.CalendarId, x.Date }).IsUnique();
                b.HasOne(x => x.Calendar).WithMany(x => x.Exceptions).HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ contract terms, coding, initiation
            builder.Entity<PcContractTerms>(b =>
            {
                b.ToTable("PcContractTerms");
                b.HasIndex(x => x.ContractId).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.ProjectCode).HasMaxLength(20);
                b.HasIndex(x => x.ProjectCode).IsUnique().HasFilter("[Deleted] = 0 AND [ProjectCode] IS NOT NULL");
                b.Property(x => x.CurrencyCode).HasMaxLength(10);
                b.Property(x => x.ValueExVat).HasPrecision(22, 2);
                b.Property(x => x.VatPercent).HasPrecision(9, 4);
                b.Property(x => x.AdvancePercent).HasPrecision(9, 4);
                b.Property(x => x.RetentionPercent).HasPrecision(9, 4);
                b.Property(x => x.LatePenaltyPercentPerWeek).HasPrecision(9, 4);
                b.Property(x => x.PenaltyCapPercent).HasPrecision(9, 4);
                b.Property(x => x.ManagementReserve).HasPrecision(22, 2);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.CurrentBaseline).WithMany().HasForeignKey(x => x.CurrentBaselineId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcWorkOrderInfo>(b =>
            {
                b.ToTable("PcWorkOrderInfos");
                b.HasIndex(x => x.WorkOrderId).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.EquipmentProjectCode).HasMaxLength(20);
                b.HasIndex(x => x.EquipmentProjectCode).IsUnique().HasFilter("[Deleted] = 0 AND [EquipmentProjectCode] IS NOT NULL");
                b.Property(x => x.TypeCode).HasMaxLength(10);
                b.Property(x => x.SizeCode).HasMaxLength(10);
                b.Property(x => x.OptionCode).HasMaxLength(10);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcCodingTable>(b =>
            {
                b.ToTable("PcCodingTables");
                b.Property(x => x.Table).HasMaxLength(20).IsRequired();
                b.Property(x => x.Family).HasMaxLength(Code);
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => new { x.Table, x.Family, x.Code });
            });
            builder.Entity<PcProjectInitiation>(b =>
            {
                b.ToTable("PcProjectInitiations");
                b.HasIndex(x => x.ContractId).IsUnique().HasFilter(NotDeleted);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcInitiationStepRecord>(b =>
            {
                b.ToTable("PcInitiationSteps");
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => new { x.InitiationId, x.Step }).IsUnique();
                b.HasOne(x => x.Initiation).WithMany(x => x.Steps).HasForeignKey(x => x.InitiationId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcProjectCharter>(b =>
            {
                b.ToTable("PcProjectCharters");
                b.HasIndex(x => x.ContractId).IsUnique().HasFilter(NotDeleted);
                foreach (var p in new[] { nameof(PcProjectCharter.Overview), nameof(PcProjectCharter.ScopeDescription), nameof(PcProjectCharter.Requirements), nameof(PcProjectCharter.Deliverables),
                    nameof(PcProjectCharter.Boundaries), nameof(PcProjectCharter.TimeBaselineEstimate), nameof(PcProjectCharter.CostEstimate), nameof(PcProjectCharter.AlternativesAnalysis),
                    nameof(PcProjectCharter.Assumptions), nameof(PcProjectCharter.Constraints), nameof(PcProjectCharter.Risks), nameof(PcProjectCharter.Stakeholders),
                    nameof(PcProjectCharter.PreAssignedResources), nameof(PcProjectCharter.ManagerAuthority) })
                    b.Property(p).HasMaxLength(LongText);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcProjectPlan>(b =>
            {
                b.ToTable("PcProjectPlans");
                b.HasIndex(x => x.ContractId).IsUnique().HasFilter(NotDeleted);
                foreach (var p in new[] { nameof(PcProjectPlan.Overview), nameof(PcProjectPlan.ScopeStatement), nameof(PcProjectPlan.InScope), nameof(PcProjectPlan.OutOfScope),
                    nameof(PcProjectPlan.Constraints), nameof(PcProjectPlan.Assumptions), nameof(PcProjectPlan.Milestones), nameof(PcProjectPlan.LongLeadRules),
                    nameof(PcProjectPlan.CommunicationPlan), nameof(PcProjectPlan.MakeBuyPartner), nameof(PcProjectPlan.QualityPlan), nameof(PcProjectPlan.ChangeManagement),
                    nameof(PcProjectPlan.OtherSections) })
                    b.Property(p).HasMaxLength(LongText);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcContractReview>(b =>
            {
                b.ToTable("PcContractReviews");
                b.Property(x => x.TenderCode).HasMaxLength(Code);
                b.Property(x => x.Notes).HasMaxLength(LongText);
                b.Property(x => x.DecisionNote).HasMaxLength(Text);
                b.HasIndex(x => x.ContractId);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcContractAmendment>(b =>
            {
                b.ToTable("PcContractAmendments");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Description).HasMaxLength(LongText);
                b.Property(x => x.PriceDelta).HasPrecision(22, 2);
                b.Property(x => x.CustomerApprovalReference).HasMaxLength(Title);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcAmendmentCopy>(b =>
            {
                b.ToTable("PcAmendmentCopies");
                b.Property(x => x.Recipient).HasMaxLength(Name);
                b.HasOne(x => x.Amendment).WithMany(x => x.Copies).HasForeignKey(x => x.AmendmentId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcPaymentMilestone>(b =>
            {
                b.ToTable("PcPaymentMilestones");
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.TriggerPercent).HasPrecision(9, 4);
                b.Property(x => x.PercentOfValue).HasPrecision(9, 4);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.FinanceRef).HasMaxLength(Title);
                b.Property(x => x.ClientLetterRef).HasMaxLength(Title);
                b.HasIndex(x => new { x.ContractId, x.Seq });
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcGuaranteeInfo>(b =>
            {
                b.ToTable("PcGuaranteeInfos");
                b.HasIndex(x => new { x.SourceKind, x.SourceId }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.ExpiryDate);
                b.Property(x => x.AmountDecimal).HasPrecision(22, 2);
                b.Property(x => x.CurrencyCode).HasMaxLength(10);
                b.Property(x => x.ParseNote).HasMaxLength(Text);
            });
            builder.Entity<PcGuaranteeEvent>(b =>
            {
                b.ToTable("PcGuaranteeEvents");
                b.Property(x => x.Fee).HasPrecision(22, 2);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne(x => x.GuaranteeInfo).WithMany(x => x.Events).HasForeignKey(x => x.GuaranteeInfoId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcPenaltyAssessment>(b =>
            {
                b.ToTable("PcPenaltyAssessments");
                b.Property(x => x.Subject).HasMaxLength(Title);
                b.Property(x => x.RatePercentPerWeek).HasPrecision(9, 4);
                b.Property(x => x.CapPercent).HasPrecision(9, 4);
                b.Property(x => x.BaseAmount).HasPrecision(22, 2);
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.DecisionReason).HasMaxLength(Text);
                b.HasIndex(x => x.ContractId);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcVariationOrder>(b =>
            {
                b.ToTable("PcVariationOrders");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Description).HasMaxLength(LongText);
                b.Property(x => x.PriceDelta).HasPrecision(22, 2);
                b.Property(x => x.ClientApprovalReference).HasMaxLength(Title);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ WBS, activities, baselines
            builder.Entity<PcWbsNode>(b =>
            {
                b.ToTable("PcWbsNodes");
                b.Property(x => x.Code).HasMaxLength(60).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Weight).HasPrecision(9, 4);
                b.Property(x => x.BudgetAmount).HasPrecision(22, 2);
                b.Property(x => x.Dictionary).HasMaxLength(LongText);
                b.Property(x => x.ResponsibleUnit).HasMaxLength(30);
                b.HasIndex(x => new { x.ContractId, x.Code });
                b.HasIndex(x => x.ParentId);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcActivity>(b =>
            {
                b.ToTable("PcActivities");
                b.Property(x => x.Code).HasMaxLength(60).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.ResponsibleUnit).HasMaxLength(30);
                b.Property(x => x.Weight).HasPrecision(9, 4);
                b.Property(x => x.BudgetAtCompletion).HasPrecision(22, 2);
                b.Property(x => x.PlannedQuantity).HasPrecision(18, 4);
                b.Property(x => x.PercentCache).HasPrecision(9, 4);
                b.Property(x => x.OnHoldReason).HasMaxLength(Text);
                b.Property(x => x.LateKind).HasMaxLength(Code);
                b.HasIndex(x => new { x.ContractId, x.Code });
                b.HasIndex(x => x.WbsNodeId);
                b.HasIndex(x => x.PurchaseOrderLineId);
                b.HasIndex(x => x.ItpActivityId);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.WbsNode).WithMany().HasForeignKey(x => x.WbsNodeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDependency>(b =>
            {
                b.ToTable("PcDependencies");
                b.HasIndex(x => new { x.PredecessorId, x.SuccessorId }).IsUnique();
                b.HasIndex(x => x.ContractId);
                b.HasOne(x => x.Predecessor).WithMany().HasForeignKey(x => x.PredecessorId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Successor).WithMany().HasForeignKey(x => x.SuccessorId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcBaseline>(b =>
            {
                b.ToTable("PcBaselines");
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.Property(x => x.Bac).HasPrecision(22, 2);
                b.Property(x => x.ManagementReserve).HasPrecision(22, 2);
                b.HasIndex(x => new { x.ContractId, x.Number }).IsUnique().HasFilter(NotDeleted);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcBaselineSignature>(b =>
            {
                b.ToTable("PcBaselineSignatures");
                b.Property(x => x.UnitRole).HasMaxLength(30).IsRequired();
                b.HasOne(x => x.Baseline).WithMany(x => x.Signatures).HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcBaselineActivity>(b =>
            {
                b.ToTable("PcBaselineActivities");
                b.Property(x => x.Code).HasMaxLength(60);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Weight).HasPrecision(9, 4);
                b.Property(x => x.Budget).HasPrecision(22, 2);
                b.Property(x => x.ResponsibleUnit).HasMaxLength(30);
                b.HasIndex(x => new { x.BaselineId, x.ActivityId }).IsUnique();
                b.HasOne(x => x.Baseline).WithMany(x => x.Activities).HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcBaselineCostPeriod>(b =>
            {
                b.ToTable("PcBaselineCostPeriods");
                b.Property(x => x.PlannedCumulative).HasPrecision(22, 2);
                b.HasOne(x => x.Baseline).WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcCostBaselineLine>(b =>
            {
                b.ToTable("PcCostBaselineLines");
                b.Property(x => x.Amount).HasPrecision(22, 2);
                b.Property(x => x.Description).HasMaxLength(Title);
                b.HasIndex(x => x.ContractId);
            });

            // ------------------------------------------------------------------ progress, snapshots, daily reports
            builder.Entity<PcManualProgress>(b =>
            {
                b.ToTable("PcManualProgress");
                b.Property(x => x.Percent).HasPrecision(9, 4);
                b.Property(x => x.Comment).HasMaxLength(Text);
                b.Property(x => x.DecisionNote).HasMaxLength(Text);
                b.HasIndex(x => x.ActivityId);
                b.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcProgressSnapshot>(b =>
            {
                b.ToTable("PcProgressSnapshots");
                b.HasIndex(x => new { x.ContractId, x.DataDate }).IsUnique();
                b.Property(x => x.PhysicalPercent).HasPrecision(9, 4);
                b.Property(x => x.PlannedPercent).HasPrecision(9, 4);
                b.Property(x => x.Bac).HasPrecision(22, 2);
                b.Property(x => x.Pv).HasPrecision(22, 2);
                b.Property(x => x.Ev).HasPrecision(22, 2);
                b.Property(x => x.Ac).HasPrecision(22, 2);
                b.Property(x => x.Cpi).HasPrecision(18, 4);
                b.Property(x => x.Spi).HasPrecision(18, 4);
                b.Property(x => x.Hash).HasMaxLength(100);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcProgressSnapshotLine>(b =>
            {
                b.ToTable("PcProgressSnapshotLines");
                b.Property(x => x.Percent).HasPrecision(9, 4);
                b.Property(x => x.PlannedPercent).HasPrecision(9, 4);
                b.Property(x => x.Budget).HasPrecision(22, 2);
                b.Property(x => x.Ev).HasPrecision(22, 2);
                b.Property(x => x.Pv).HasPrecision(22, 2);
                b.HasIndex(x => x.SnapshotId);
                b.HasOne(x => x.Snapshot).WithMany(x => x.Lines).HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcDailyReport>(b =>
            {
                b.ToTable("PcDailyReports");
                b.Property(x => x.Unit).HasMaxLength(30);
                b.HasIndex(x => new { x.ContractId, x.Unit, x.ReportDate });
            });
            builder.Entity<PcDailyReportLine>(b =>
            {
                b.ToTable("PcDailyReportLines");
                b.Property(x => x.EventText).HasMaxLength(Text);
                b.Property(x => x.Percent).HasPrecision(9, 4);
                b.Property(x => x.DelayReasonCode).HasMaxLength(Code);
                b.Property(x => x.DelayReason).HasMaxLength(Text);
                b.Property(x => x.PreventiveAction).HasMaxLength(Text);
                b.Property(x => x.CorrectiveAction).HasMaxLength(Text);
                b.HasOne(x => x.Report).WithMany(x => x.Lines).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ change control
            builder.Entity<PcChangeRequest>(b =>
            {
                b.ToTable("PcChangeRequests");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.ManagerAgreement).HasMaxLength(Name);
                b.Property(x => x.Description).HasMaxLength(LongText);
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.Property(x => x.ScenarioJson).HasColumnType("nvarchar(max)");
                b.Property(x => x.ImpactJson).HasColumnType("nvarchar(max)");
                b.Property(x => x.RequestedPriceDelta).HasPrecision(22, 2);
                b.Property(x => x.ImpactDeltaCost).HasPrecision(22, 2);
                b.Property(x => x.ImpactScope).HasMaxLength(Text);
                b.Property(x => x.ImpactResources).HasMaxLength(Text);
                b.Property(x => x.ImpactQuality).HasMaxLength(Text);
                b.Property(x => x.ImpactRisk).HasMaxLength(Text);
                b.Property(x => x.ImpactProcurement).HasMaxLength(Text);
                b.Property(x => x.ImpactClientContract).HasMaxLength(Text);
                b.Property(x => x.Recommendation).HasMaxLength(Text);
                b.Property(x => x.DecisionNote).HasMaxLength(Text);
                b.Property(x => x.DecidedByRole).HasMaxLength(30);
                b.HasIndex(x => new { x.ContractId, x.Status });
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Baseline).WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcChangeBoardMember>(b =>
            {
                b.ToTable("PcChangeBoardMembers");
                b.Property(x => x.Role).HasMaxLength(30);
                b.Property(x => x.Comment).HasMaxLength(Text);
                b.HasOne(x => x.ChangeRequest).WithMany(x => x.Board).HasForeignKey(x => x.ChangeRequestId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcApprovalAuthority>(b =>
            {
                b.ToTable("PcApprovalAuthorities");
                b.Property(x => x.Role).HasMaxLength(30).IsRequired();
                b.HasIndex(x => x.Role).IsUnique();
                b.Property(x => x.MaxAmount).HasPrecision(22, 2);
                b.Property(x => x.Note).HasMaxLength(Text);
            });
            builder.Entity<PcClaim>(b =>
            {
                b.ToTable("PcClaims");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Issue).HasMaxLength(LongText);
                b.Property(x => x.Basis).HasMaxLength(Text);
                b.Property(x => x.AmountQuantified).HasPrecision(22, 2);
                b.Property(x => x.ReviewNote).HasMaxLength(Text);
                b.HasIndex(x => x.ContractId);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ MDR / MDL
            builder.Entity<PcEngineeringDeliverable>(b =>
            {
                b.ToTable("PcEngineeringDeliverables");
                b.Property(x => x.DocumentNumber).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.StandardSource).HasMaxLength(Title);
                b.Property(x => x.Discipline).HasMaxLength(Name);
                b.Property(x => x.DocumentType).HasMaxLength(Name);
                b.Property(x => x.Revision).HasMaxLength(Code);
                b.Property(x => x.LastTransmittalNumber).HasMaxLength(Code);
                b.Property(x => x.ClientResponseCode).HasMaxLength(10);
                b.HasIndex(x => new { x.ContractId, x.WorkOrderId });
                b.HasIndex(x => new { x.ContractId, x.DocumentNumber });
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcClientResponseCode>(b =>
            {
                b.ToTable("PcClientResponseCodes");
                b.Property(x => x.Code).HasMaxLength(10).IsRequired();
                b.HasIndex(x => x.Code).IsUnique();
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Source).HasMaxLength(Title);
            });
            builder.Entity<PcTransmittal>(b =>
            {
                b.ToTable("PcTransmittals");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Reference).HasMaxLength(Title);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcTransmittalLine>(b =>
            {
                b.ToTable("PcTransmittalLines");
                b.Property(x => x.Revision).HasMaxLength(Code);
                b.Property(x => x.ResponseCode).HasMaxLength(10);
                b.Property(x => x.ResponseNote).HasMaxLength(Text);
                b.HasIndex(x => x.DeliverableId);
                b.HasOne(x => x.Transmittal).WithMany(x => x.Lines).HasForeignKey(x => x.TransmittalId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Deliverable).WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcMdrTemplate>(b =>
            {
                b.ToTable("PcMdrTemplates");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.StandardSource).HasMaxLength(Title);
                b.Property(x => x.Note).HasMaxLength(Text);
            });
            builder.Entity<PcMdrTemplateLine>(b =>
            {
                b.ToTable("PcMdrTemplateLines");
                b.Property(x => x.DocumentType).HasMaxLength(Name);
                b.Property(x => x.Discipline).HasMaxLength(Name);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.StandardSource).HasMaxLength(Title);
                b.HasOne(x => x.Template).WithMany(x => x.Lines).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ reports, shipment
            builder.Entity<PcReportIssue>(b =>
            {
                b.ToTable("PcReportIssues");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique();
                b.Property(x => x.Hash).HasMaxLength(100);
                b.Property(x => x.Content).HasColumnType("nvarchar(max)");
                b.Property(x => x.ControlledRevision).HasMaxLength(Code);
                b.HasIndex(x => new { x.ContractId, x.Kind, x.DataDate });
            });
            builder.Entity<PcShipment>(b =>
            {
                b.ToTable("PcShipments");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.PackingList).HasMaxLength(LongText);
                b.Property(x => x.PackingVerification).HasMaxLength(Text);
                b.Property(x => x.ShippingDocuments).HasMaxLength(Text);
                b.Property(x => x.Waybill).HasMaxLength(Title);
                b.Property(x => x.DeliveryReceiptNumber).HasMaxLength(Title);
                b.Property(x => x.HandoverMinutesNumber).HasMaxLength(Title);
                b.HasIndex(x => x.WorkOrderId);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
