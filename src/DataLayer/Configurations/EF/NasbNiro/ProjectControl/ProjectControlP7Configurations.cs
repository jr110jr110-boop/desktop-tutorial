using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.ProjectControl
{
    // Package P-B7a: progress-approval cartable, PMBOK registers and the extended daily report. New tables and new columns of PcDailyReport.
    public static class ProjectControlP7ModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddProjectControlP7Configurations(this ModelBuilder builder)
        {
            // ------------------------------------------------------------------ progress-approval cartable
            builder.Entity<PcProgressChainLevel>(b =>
            {
                b.ToTable("PcProgressChainLevels");
                b.Property(x => x.Title).HasMaxLength(100).IsRequired();
                b.Property(x => x.Role).HasMaxLength(30);
                b.HasIndex(x => new { x.ContractId, x.Level }).IsUnique();
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcProgressSubmission>(b =>
            {
                b.ToTable("PcProgressSubmissions");
                b.Property(x => x.Number).HasMaxLength(40);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Percent).HasPrecision(9, 4);
                b.Property(x => x.PreviousPercent).HasPrecision(9, 4);
                b.Property(x => x.ActualCost).HasPrecision(22, 2);
                b.Property(x => x.CostNote).HasMaxLength(1000);
                b.Property(x => x.Comment).HasMaxLength(2000);
                b.Property(x => x.ResultNote).HasMaxLength(1000);
                b.HasIndex(x => new { x.ContractId, x.State });
                b.HasIndex(x => x.ActivityId);
                b.HasIndex(x => x.TaskId);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Tasks.TmTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcProgressApproval>(b =>
            {
                b.ToTable("PcProgressApprovals");
                b.Property(x => x.Title).HasMaxLength(100);
                b.Property(x => x.Role).HasMaxLength(30);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => new { x.SubmissionId, x.Round, x.Level }).IsUnique();
                b.HasOne(x => x.Submission).WithMany(x => x.Steps).HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcProgressSubmissionHistory>(b =>
            {
                b.ToTable("PcProgressSubmissionHistory");
                b.Property(x => x.Action).HasMaxLength(30);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.Property(x => x.ChangesJson).HasMaxLength(2000);
                b.HasIndex(x => new { x.SubmissionId, x.At });
                b.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ registers
            builder.Entity<PcRisk>(b =>
            {
                b.ToTable("PcRisks");
                b.Property(x => x.Number).HasMaxLength(40);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Cause).HasMaxLength(1000);
                b.Property(x => x.Event).HasMaxLength(1000);
                b.Property(x => x.Effect).HasMaxLength(1000);
                b.Property(x => x.Category).HasMaxLength(60);
                b.Property(x => x.ResponsePlan).HasMaxLength(2000);
                b.Property(x => x.ClosureNote).HasMaxLength(1000);
                b.HasIndex(x => new { x.ContractId, x.Number }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.ContractId, x.Status });
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.OwnerPositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcIssue>(b =>
            {
                b.ToTable("PcIssues");
                b.Property(x => x.Number).HasMaxLength(40);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Description).HasMaxLength(2000);
                b.Property(x => x.Category).HasMaxLength(60);
                b.Property(x => x.DelayReasonCode).HasMaxLength(60);
                b.Property(x => x.Resolution).HasMaxLength(2000);
                b.HasIndex(x => new { x.ContractId, x.Number }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.ContractId, x.Status });
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.OwnerPositionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcStakeholder>(b =>
            {
                b.ToTable("PcStakeholders");
                b.Property(x => x.Name).HasMaxLength(200).IsRequired();
                b.Property(x => x.Organization).HasMaxLength(200);
                b.Property(x => x.RoleTitle).HasMaxLength(200);
                b.Property(x => x.Category).HasMaxLength(60);
                b.Property(x => x.Expectations).HasMaxLength(1000);
                b.Property(x => x.CommunicationNeed).HasMaxLength(1000);
                b.Property(x => x.CommunicationFrequency).HasMaxLength(100);
                b.HasIndex(x => x.ContractId);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.OwnerPositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcLesson>(b =>
            {
                b.ToTable("PcLessons");
                b.Property(x => x.Number).HasMaxLength(40);
                b.Property(x => x.Category).HasMaxLength(60);
                b.Property(x => x.WhatHappened).HasMaxLength(2000);
                b.Property(x => x.Cause).HasMaxLength(2000);
                b.Property(x => x.Recommendation).HasMaxLength(2000);
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.HasIndex(x => new { x.ContractId, x.Number }).IsUnique().HasFilter(NotDeleted);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcProjectDeliverable>(b =>
            {
                b.ToTable("PcProjectDeliverables");
                b.Property(x => x.Code).HasMaxLength(60);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Description).HasMaxLength(2000);
                b.Property(x => x.AcceptanceNote).HasMaxLength(1000);
                b.HasIndex(x => new { x.ContractId, x.Status });
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcWbsNode>().WithMany().HasForeignKey(x => x.WbsNodeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcEngineeringDeliverable>().WithMany().HasForeignKey(x => x.MdrDeliverableId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcProjectPhoto>(b =>
            {
                b.ToTable("PcProjectPhotos");
                b.Property(x => x.Caption).HasMaxLength(500);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.ContractId, x.TakenDate });
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcWbsNode>().WithMany().HasForeignKey(x => x.WbsNodeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcPartyEvaluation>(b =>
            {
                b.ToTable("PcPartyEvaluations");
                b.Property(x => x.Number).HasMaxLength(40);
                b.Property(x => x.PartyName).HasMaxLength(200);
                b.Property(x => x.Grade).HasMaxLength(5);
                b.Property(x => x.Comment).HasMaxLength(2000);
                b.Property(x => x.TotalScore).HasPrecision(9, 2);
                b.HasIndex(x => new { x.ContractId, x.Number }).IsUnique().HasFilter(NotDeleted);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcPartyEvaluationScore>(b =>
            {
                b.ToTable("PcPartyEvaluationScores");
                b.Property(x => x.Criterion).HasMaxLength(200).IsRequired();
                b.Property(x => x.Weight).HasPrecision(9, 2);
                b.Property(x => x.Score).HasPrecision(5, 2);
                b.Property(x => x.Comment).HasMaxLength(500);
                b.HasOne(x => x.Evaluation).WithMany(x => x.Scores).HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcWeeklyReport>(b =>
            {
                b.ToTable("PcWeeklyReports");
                b.Property(x => x.Number).HasMaxLength(40);
                b.Property(x => x.Summary).HasMaxLength(4000);
                b.Property(x => x.NextWeekPlan).HasMaxLength(4000);
                b.HasIndex(x => new { x.ContractId, x.WeekStart }).IsUnique().HasFilter(NotDeleted);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcPhaseEvaluation>(b =>
            {
                b.ToTable("PcPhaseEvaluations");
                b.Property(x => x.OverallScore).HasPrecision(5, 2);
                b.Property(x => x.Achievements).HasMaxLength(2000);
                b.Property(x => x.Problems).HasMaxLength(2000);
                b.Property(x => x.Recommendation).HasMaxLength(2000);
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.HasIndex(x => new { x.ContractId, x.Phase });
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ daily site report
            builder.Entity<PcDailyReport>(b =>
            {
                b.Property(x => x.Weather).HasMaxLength(100);
                b.Property(x => x.Notes).HasMaxLength(4000);
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
            });
            builder.Entity<PcDailyManpower>(b =>
            {
                b.ToTable("PcDailyManpower");
                b.Property(x => x.Title).HasMaxLength(200);
                b.Property(x => x.ActivityType).HasMaxLength(200);
                b.Property(x => x.Note).HasMaxLength(500);
                b.Property(x => x.PersonDays).HasPrecision(9, 2);
                b.HasIndex(x => x.ReportId);
                b.HasOne(x => x.Report).WithMany(x => x.Manpower).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcDailyMaterial>(b =>
            {
                b.ToTable("PcDailyMaterials");
                b.Property(x => x.Description).HasMaxLength(300);
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Note).HasMaxLength(500);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.HasIndex(x => x.ReportId);
                b.HasOne(x => x.Report).WithMany(x => x.Materials).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcDailyMachinery>(b =>
            {
                b.ToTable("PcDailyMachinery");
                b.Property(x => x.Name).HasMaxLength(200);
                b.Property(x => x.Note).HasMaxLength(500);
                b.Property(x => x.Hours).HasPrecision(9, 2);
                b.HasIndex(x => x.ReportId);
                b.HasOne(x => x.Report).WithMany(x => x.Machinery).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<PcDailyObstacle>(b =>
            {
                b.ToTable("PcDailyObstacles");
                b.Property(x => x.Category).HasMaxLength(60);
                b.Property(x => x.Description).HasMaxLength(1000);
                b.Property(x => x.ImpactHours).HasPrecision(9, 2);
                b.HasIndex(x => x.ReportId);
                b.HasOne(x => x.Report).WithMany(x => x.Obstacles).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDailyHse>(b =>
            {
                b.ToTable("PcDailyHse");
                b.Property(x => x.Description).HasMaxLength(1000);
                b.Property(x => x.ActionTaken).HasMaxLength(1000);
                b.HasIndex(x => x.ReportId);
                b.HasOne(x => x.Report).WithMany(x => x.HseEvents).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
