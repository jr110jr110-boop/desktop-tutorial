using Entities.Model.NasbNiro.Ai;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Ai
{
    // Local AI, release A1. All tables start with "Ai". Nothing cascades: conversations and audit rows are kept.
    public static class AiModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;

        public static void AddAiConfigurations(this ModelBuilder builder)
        {
            builder.Entity<AiModelEndpoint>(b =>
            {
                b.ToTable("AiModelEndpoints");
                b.Property(x => x.Name).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Url).HasMaxLength(Text).IsRequired();
                b.Property(x => x.ModelName).HasMaxLength(Name).IsRequired();
                b.Property(x => x.LastError).HasMaxLength(Text);
                b.HasIndex(x => new { x.Kind, x.Priority });
            });
            builder.Entity<AiPromptVersion>(b =>
            {
                b.ToTable("AiPromptVersions");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Text).IsRequired();
                b.HasIndex(x => new { x.Code, x.Version }).IsUnique();
                b.HasIndex(x => x.Code).IsUnique().HasFilter("[IsActive] = 1"); // one active version per code
            });
            builder.Entity<AiConversation>(b =>
            {
                b.ToTable("AiConversations");
                b.Property(x => x.Title).HasMaxLength(Title);
                b.HasIndex(x => new { x.UserId, x.LastAt });
            });
            builder.Entity<AiMessage>(b =>
            {
                b.ToTable("AiMessages");
                b.Property(x => x.ToolName).HasMaxLength(Code);
                b.Property(x => x.Model).HasMaxLength(Name);
                b.HasIndex(x => new { x.ConversationId, x.Id });
                b.HasOne(x => x.Conversation).WithMany(c => c.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<AiToolCallLog>(b =>
            {
                b.ToTable("AiToolCallLogs");
                b.Property(x => x.ToolName).HasMaxLength(Code).IsRequired();
                b.Property(x => x.ArgumentsJson).HasMaxLength(LongText);
                b.Property(x => x.ResultHash).HasMaxLength(64);
                b.HasIndex(x => x.ConversationId);
                b.HasIndex(x => x.MessageId);
                b.HasOne(x => x.Message).WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<AiAuditEvent>(b =>
            {
                b.ToTable("AiAuditEvents");
                b.Property(x => x.SeatCode).HasMaxLength(Code);
                b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
                b.Property(x => x.Model).HasMaxLength(Name);
                b.Property(x => x.Detail).HasMaxLength(LongText);
                b.Property(x => x.PromptHash).HasMaxLength(64);
                b.HasIndex(x => x.At);
                b.HasIndex(x => new { x.Kind, x.At });
                b.HasIndex(x => x.UserId);
            });
            builder.Entity<AiDocumentChunk>(b =>
            {
                b.ToTable("AiDocumentChunks");
                b.Property(x => x.SourceType).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Revision).HasMaxLength(Code);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Text).HasMaxLength(LongText).IsRequired();
                b.Property(x => x.Embedding).HasColumnType("varbinary(max)");
                b.Property(x => x.Model).HasMaxLength(Name);
                b.HasIndex(x => new { x.SourceType, x.SourceId });
            });
            builder.Entity<AiSeat>(b =>
            {
                b.ToTable("AiSeats");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.ServiceUserName).HasMaxLength(100);
                b.Property(x => x.Roles).HasMaxLength(500);
                b.Property(x => x.Schedule).HasMaxLength(100);
                b.Property(x => x.PromptVersionCode).HasMaxLength(Code);
                b.Property(x => x.OutputKind).HasMaxLength(Code);
                b.HasIndex(x => x.Code).IsUnique();
            });
            builder.Entity<AiSeatRun>(b =>
            {
                b.ToTable("AiSeatRuns");
                b.Property(x => x.Outputs).HasMaxLength(LongText);
                b.Property(x => x.Error).HasMaxLength(LongText);
                b.HasIndex(x => new { x.SeatId, x.StartedAt });
                // at most one run of a seat is Running (Status 1): the database, not a read before a write, keeps two runs apart
                b.HasIndex(x => x.SeatId).IsUnique().HasFilter("[Status] = 1").HasDatabaseName("IX_AiSeatRuns_SeatId_Running");
                b.HasOne(x => x.Seat).WithMany().HasForeignKey(x => x.SeatId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<AiEvalCase>(b =>
            {
                b.ToTable("AiEvalCases");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Question).HasMaxLength(Text).IsRequired();
                b.Property(x => x.ExpectedTools).HasMaxLength(500);
                b.Property(x => x.ExpectedFacts).HasMaxLength(2000);
                b.HasIndex(x => x.Code).IsUnique();
            });
            builder.Entity<AiEvalRun>(b =>
            {
                b.ToTable("AiEvalRuns");
                b.Property(x => x.Model).HasMaxLength(Name);
                b.Property(x => x.Detail).HasColumnType("nvarchar(max)");
                // at most one evaluation is running (FinishedAt empty)
                b.HasIndex(x => x.FinishedAt).IsUnique().HasFilter("[FinishedAt] IS NULL").HasDatabaseName("IX_AiEvalRuns_Running");
            });
            builder.Entity<AiNote>(b =>
            {
                b.ToTable("AiNotes");
                b.Property(x => x.SeatCode).HasMaxLength(Code).IsRequired();
                b.Property(x => x.TargetType).HasMaxLength(Code).IsRequired();
                b.Property(x => x.RuleCode).HasMaxLength(Code);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Text).HasColumnType("nvarchar(max)");
                b.Property(x => x.DecisionNote).HasMaxLength(Text);
                // one note per seat, target and rule: the seats are idempotent (a note without a target is not unique)
                b.HasIndex(x => new { x.SeatCode, x.TargetType, x.TargetId, x.RuleCode }).IsUnique().HasFilter("[TargetId] IS NOT NULL");
                b.HasIndex(x => new { x.TargetType, x.TargetId });
                b.HasIndex(x => new { x.Status, x.CreatedAt });
            });
        }
    }
}
