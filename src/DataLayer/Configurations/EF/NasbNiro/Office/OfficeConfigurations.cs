using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Office
{
    // Office automation (release O1) tables. All start with "Of". Deletes are restricted except for child lines, which go with their header.
    public static class OfficeModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddOfficeConfigurations(this ModelBuilder builder)
        {
            // ------------------------------------------------------------------ organisation
            builder.Entity<OfUnit>(b =>
            {
                b.ToTable("OfUnits");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.HasOne<OfUnit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.ManagerPositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfPosition>(b =>
            {
                b.ToTable("OfPositions");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.HasIndex(x => x.UnitId);
                b.HasIndex(x => x.UserId);
                b.HasOne<OfUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfDelegation>(b =>
            {
                b.ToTable("OfDelegations");
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.HasIndex(x => x.DeputyUserId);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfUserSignature>(b =>
            {
                b.ToTable("OfUserSignatures");
                b.HasIndex(x => x.UserId).IsUnique();
            });

            // ------------------------------------------------------------------ inbox
            builder.Entity<OfInboxItem>(b =>
            {
                b.ToTable("OfInboxItems");
                b.Property(x => x.RecipientRole).HasMaxLength(Code);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.SourceModule).HasMaxLength(Code);
                b.Property(x => x.SourceType).HasMaxLength(Code);
                b.Property(x => x.SourceKey).HasMaxLength(Name);
                b.Property(x => x.Link).HasMaxLength(Text);
                b.Property(x => x.ClosedReason).HasMaxLength(Text);
                b.HasIndex(x => x.SourceKey);
                b.HasIndex(x => new { x.RecipientUserId, x.Status });
                b.HasIndex(x => new { x.RecipientPositionId, x.Status });
                b.HasIndex(x => new { x.RecipientRole, x.Status });
            });

            // ------------------------------------------------------------------ letters
            builder.Entity<OfExternalParty>(b =>
            {
                b.ToTable("OfExternalParties");
                b.Property(x => x.Name).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Address).HasMaxLength(Text);
                b.Property(x => x.Phone).HasMaxLength(Code);
                b.Property(x => x.Email).HasMaxLength(Name);
            });
            builder.Entity<OfLetter>(b =>
            {
                b.ToTable("OfLetters");
                b.Property(x => x.Number).HasMaxLength(Name);
                b.HasIndex(x => new { x.Kind, x.Number }).IsUnique().HasFilter("[Deleted] = 0 AND [Number] IS NOT NULL AND [Imported] = 0");
                b.HasIndex(x => new { x.Kind, x.Status });
                b.HasIndex(x => x.Date);
                b.Property(x => x.Subject).HasMaxLength(Title).IsRequired();
                b.Property(x => x.BodyHtml).HasColumnType("nvarchar(max)");
                b.Property(x => x.IncomingNumber).HasMaxLength(Name);
                b.Property(x => x.ContentHash).HasMaxLength(Name);
                b.Property(x => x.SignatureFileHash).HasMaxLength(Code);
                b.Property(x => x.DeliveryReceipt).HasMaxLength(Text);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.SenderPositionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.SignerPositionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfExternalParty>().WithMany().HasForeignKey(x => x.SenderPartyId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfNumberingRule>().WithMany().HasForeignKey(x => x.NumberRuleId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfLetter>().WithMany().HasForeignKey(x => x.ReplyToLetterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfLetter>().WithMany().HasForeignKey(x => x.FollowUpLetterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfLetterTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfLetterRecipient>(b =>
            {
                b.ToTable("OfLetterRecipients");
                b.HasIndex(x => x.PositionId);
                b.HasOne(x => x.Letter).WithMany(x => x.Recipients).HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfExternalParty>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfLetterApproval>(b =>
            {
                b.ToTable("OfLetterApprovals");
                b.Property(x => x.Comment).HasMaxLength(Text);
                b.HasIndex(x => x.PositionId);
                b.HasOne(x => x.Letter).WithMany(x => x.Approvals).HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfLetterAttachment>(b =>
            {
                b.ToTable("OfLetterAttachments");
                b.Property(x => x.FileName).HasMaxLength(Title);
                b.Property(x => x.FileHash).HasMaxLength(Code);
                b.HasOne(x => x.Letter).WithMany(x => x.Attachments).HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<OfRecordLink>(b =>
            {
                b.ToTable("OfRecordLinks");
                b.Property(x => x.Text).HasMaxLength(Text);
                b.HasIndex(x => new { x.Kind, x.RecordId });
                b.HasOne(x => x.Letter).WithMany(x => x.Links).HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Meeting).WithMany(x => x.Links).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<OfLetterTemplate>(b =>
            {
                b.ToTable("OfLetterTemplates");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.HeaderHtml).HasColumnType("nvarchar(max)");
                b.Property(x => x.FooterHtml).HasColumnType("nvarchar(max)");
                b.Property(x => x.BodyHtml).HasColumnType("nvarchar(max)");
                b.Property(x => x.ApprovalChain).HasMaxLength(Text);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.SignerPositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfNumberingRule>(b =>
            {
                b.ToTable("OfNumberingRules");
                b.Property(x => x.Pattern).HasMaxLength(Name).IsRequired();
                b.HasOne<OfUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfNumberCounter>(b =>
            {
                b.ToTable("OfNumberCounters");
                b.HasIndex(x => new { x.RuleId, x.Year }).IsUnique();
                b.HasOne<OfNumberingRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfReferral>(b =>
            {
                b.ToTable("OfReferrals");
                b.Property(x => x.Instruction).HasMaxLength(LongText);
                b.Property(x => x.DoneNote).HasMaxLength(LongText);
                b.HasIndex(x => x.ToPositionId);
                b.HasOne(x => x.Letter).WithMany(x => x.Referrals).HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<OfReferral>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.FromPositionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.ToPositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfReferralEntry>(b =>
            {
                b.ToTable("OfReferralEntries");
                b.Property(x => x.Text).HasMaxLength(LongText);
                b.HasOne(x => x.Referral).WithMany(x => x.Entries).HasForeignKey(x => x.ReferralId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ meetings
            builder.Entity<OfMeeting>(b =>
            {
                b.ToTable("OfMeetings");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Place).HasMaxLength(Name);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.HasIndex(x => x.Status);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.ChairPositionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.SecretaryPositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfMeetingAgenda>(b =>
            {
                b.ToTable("OfMeetingAgenda");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Minutes).HasColumnType("nvarchar(max)");
                b.HasOne(x => x.Meeting).WithMany(x => x.Agenda).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<OfMeetingAttendee>(b =>
            {
                b.ToTable("OfMeetingAttendees");
                b.Property(x => x.ExternalName).HasMaxLength(Name);
                b.HasOne(x => x.Meeting).WithMany(x => x.Attendees).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfDecision>(b =>
            {
                b.ToTable("OfDecisions");
                b.Property(x => x.Text).HasMaxLength(LongText).IsRequired();
                b.Property(x => x.DoneNote).HasMaxLength(LongText);
                b.HasIndex(x => new { x.Status, x.DueDate });
                b.HasIndex(x => x.OwnerPositionId);
                b.HasOne(x => x.Meeting).WithMany(x => x.Decisions).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.OwnerPositionId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ attendance
            builder.Entity<OfShift>(b =>
            {
                b.ToTable("OfShifts");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
            });
            builder.Entity<OfShiftAssignment>(b =>
            {
                b.ToTable("OfShiftAssignments");
                b.HasIndex(x => new { x.EmployeeId, x.FromDate });
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfShift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfDevice>(b =>
            {
                b.ToTable("OfDevices");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Location).HasMaxLength(Name);
                b.Property(x => x.ApiKeyHash).HasMaxLength(Name).IsRequired();
            });
            builder.Entity<OfClockEvent>(b =>
            {
                b.ToTable("OfClockEvents");
                b.Property(x => x.DeviceCode).HasMaxLength(Code);
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.HasIndex(x => new { x.EmployeeId, x.Timestamp });
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfAttendancePolicy>(b => b.ToTable("OfAttendancePolicies"));
            builder.Entity<OfAttendanceDay>(b =>
            {
                b.ToTable("OfAttendanceDays");
                b.Property(x => x.PaidLeaveDays).HasPrecision(9, 4);
                b.Property(x => x.SickLeaveDays).HasPrecision(9, 4);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfAttendanceMonth>(b =>
            {
                b.ToTable("OfAttendanceMonths");
                b.HasIndex(x => new { x.Year, x.Month }).IsUnique();
            });
            builder.Entity<OfLeaveRequest>(b =>
            {
                b.ToTable("OfLeaveRequests");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Hours).HasPrecision(9, 2);
                b.Property(x => x.Days).HasPrecision(9, 4);
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.Property(x => x.ApproverComment).HasMaxLength(Text);
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.HasIndex(x => new { x.EmployeeId, x.FromDate });
                b.HasIndex(x => x.Status);
                b.HasOne<FnEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.ApproverPositionId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ general requests
            builder.Entity<OfRequestType>(b =>
            {
                b.ToTable("OfRequestTypes");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.HasIndex(x => x.Title).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.FieldsJson).HasColumnType("nvarchar(max)");
                b.Property(x => x.ApprovalChain).HasMaxLength(Text);
                b.Property(x => x.TargetNote).HasMaxLength(Text);
            });
            builder.Entity<OfRequest>(b =>
            {
                b.ToTable("OfRequests");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.ValuesJson).HasColumnType("nvarchar(max)");
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => x.RequesterUserId);
                b.HasOne<OfRequestType>().WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OfRequestApproval>(b =>
            {
                b.ToTable("OfRequestApprovals");
                b.Property(x => x.Comment).HasMaxLength(Text);
                b.HasIndex(x => x.PositionId);
                b.HasOne(x => x.Request).WithMany(x => x.Approvals).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<OfPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
