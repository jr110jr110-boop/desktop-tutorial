using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Office;

namespace ViewModels.NasbNiro.OfficeDtos
{
    public class ExternalPartyDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public OfPartyKind Kind { get; set; }
        public int? ProviderId { get; set; }
        public int? CustomerId { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveExternalPartyDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public OfPartyKind Kind { get; set; }
        public int? ProviderId { get; set; }
        public int? CustomerId { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class RecipientDto
    {
        public OfRecipientType Type { get; set; } = OfRecipientType.To;
        public int? PositionId { get; set; }
        public string PositionTitle { get; set; }
        public int? PartyId { get; set; }
        public string PartyName { get; set; }
    }

    public class LetterApprovalDto
    {
        public int Round { get; set; }
        public int Seq { get; set; }
        public int PositionId { get; set; }
        public string PositionTitle { get; set; }
        public OfApprovalStatus Status { get; set; }
        public string ActedByName { get; set; }
        public bool OnBehalf { get; set; }
        public string Comment { get; set; }
        public DateTimeOffset? ActedAt { get; set; }
        public string ActedAtText { get; set; }
    }

    public class LetterAttachmentDto
    {
        public Guid DocumentId { get; set; }
        public string FileName { get; set; }
    }

    public class RecordLinkDto
    {
        public OfRelatedKind Kind { get; set; }
        public int? RecordId { get; set; }
        public string Text { get; set; }
    }

    public class ReferralDto
    {
        public int Id { get; set; }
        public int LetterId { get; set; }
        public int? ParentId { get; set; }
        public int? FromPositionId { get; set; }
        public string FromPositionTitle { get; set; }
        public int ToPositionId { get; set; }
        public string ToPositionTitle { get; set; }
        public string Instruction { get; set; }
        public OfReferralAction Action { get; set; }
        public DateTime? DueDate { get; set; }
        public string DueDateText { get; set; }
        public OfReferralStatus Status { get; set; }
        public string CreatedByName { get; set; }
        public bool OnBehalf { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CreatedAtText { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public string DoneByName { get; set; }
        public string DoneNote { get; set; }
        public List<ReferralEntryDto> Entries { get; set; } = new List<ReferralEntryDto>();
        public List<ReferralDto> Children { get; set; } = new List<ReferralDto>();
    }

    public class ReferralEntryDto
    {
        public string UserName { get; set; }
        public bool OnBehalf { get; set; }
        public string Text { get; set; }
        public DateTimeOffset At { get; set; }
        public string AtText { get; set; }
    }

    public class LetterQuery : OfficePageQuery
    {
        public OfLetterKind? Kind { get; set; }
        public OfLetterStatus? Status { get; set; }
        public OfClassification? Classification { get; set; }
        public string Number { get; set; }
        public string Subject { get; set; }
        public string Party { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public OfRelatedKind? RelatedKind { get; set; }
        public int? RelatedRecordId { get; set; }

        /// <summary>Only letters I wrote (drafts and sent).</summary>
        public bool? MineOnly { get; set; }

        /// <summary>Only letters waiting for my approval or signature.</summary>
        public bool? WaitingForMe { get; set; }
    }

    public class LetterListDto
    {
        public int Id { get; set; }
        public OfLetterKind Kind { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public string DateText { get; set; }
        public string Subject { get; set; }
        public OfLetterStatus Status { get; set; }
        public OfClassification Classification { get; set; }
        public OfUrgency Urgency { get; set; }
        public string SenderName { get; set; }
        public string RecipientNames { get; set; }
        public bool Imported { get; set; }

        /// <summary>Confidential or Secret letter seen by the secretariat: only the register data is returned (no subject, body, attachments or referral notes).</summary>
        public bool MetadataOnly { get; set; }
    }

    public class LetterDto : LetterListDto
    {
        public string BodyHtml { get; set; }
        public int? SenderPositionId { get; set; }
        public int? SenderPartyId { get; set; }
        public string IncomingNumber { get; set; }
        public DateTime? IncomingDate { get; set; }
        public string IncomingDateText { get; set; }
        public int? SignerPositionId { get; set; }
        public string SignerPositionTitle { get; set; }
        public int ApprovalRound { get; set; }
        public DateTimeOffset? SignedAt { get; set; }
        public string SignedAtText { get; set; }
        public string SignedByName { get; set; }
        public string SignedOnBehalfOfPosition { get; set; }
        public Guid? SignatureDocumentId { get; set; }
        public string ContentHash { get; set; }
        public bool PaperCopy { get; set; }
        public OfDelivery? DeliveryMethod { get; set; }
        public DateTime? SentDate { get; set; }
        public string SentDateText { get; set; }
        public string DeliveryReceipt { get; set; }
        public Guid? DeliveryReceiptDocumentId { get; set; }
        public int? ReplyToLetterId { get; set; }
        public int? FollowUpLetterId { get; set; }
        public int? TemplateId { get; set; }
        public string CancelReason { get; set; }
        public string CreatedByName { get; set; }
        public List<RecipientDto> Recipients { get; set; } = new List<RecipientDto>();
        public List<LetterApprovalDto> Approvals { get; set; } = new List<LetterApprovalDto>();
        public List<LetterAttachmentDto> Attachments { get; set; } = new List<LetterAttachmentDto>();
        public List<RecordLinkDto> Links { get; set; } = new List<RecordLinkDto>();
        public List<ReferralDto> Referrals { get; set; } = new List<ReferralDto>();

        /// <summary>What the signed-in user may do now: save, submit, approve, return, sign, register, send, refer, cancel, archive.</summary>
        public List<string> AllowedActions { get; set; } = new List<string>();

        public string RowVersion { get; set; }
    }

    public class SaveLetterDto
    {
        public int? Id { get; set; }
        public OfLetterKind Kind { get; set; } = OfLetterKind.Outgoing;
        public DateTime? Date { get; set; }
        public string Subject { get; set; }
        public string BodyHtml { get; set; }

        /// <summary>The position the letter is sent from (outgoing and internal): one of the positions the user acts for.</summary>
        public int? SenderPositionId { get; set; }

        /// <summary>Incoming letters: the sending organisation.</summary>
        public int? SenderPartyId { get; set; }

        public OfClassification Classification { get; set; }
        public OfUrgency Urgency { get; set; }
        public string IncomingNumber { get; set; }
        public DateTime? IncomingDate { get; set; }
        public int? SignerPositionId { get; set; }
        public bool PaperCopy { get; set; }
        public int? ReplyToLetterId { get; set; }
        public int? TemplateId { get; set; }
        public List<RecipientDto> Recipients { get; set; } = new List<RecipientDto>();
        public List<LetterAttachmentDto> Attachments { get; set; } = new List<LetterAttachmentDto>();
        public List<RecordLinkDto> Links { get; set; } = new List<RecordLinkDto>();

        /// <summary>Incoming letters only (secretariat): they are numbered at once and delivered.</summary>
        public bool Register { get; set; }

        public string RowVersion { get; set; }
    }

    public class LetterActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, approve, return, sign, register, send, receipt, archive, cancel.</summary>
        public string Action { get; set; }

        /// <summary>submit: the approval chain (positions in order); empty uses the template chain.</summary>
        public List<int> ApprovalChain { get; set; }

        /// <summary>The position the user acts for (approve, return, sign). Needed when the user has several.</summary>
        public int? ActingPositionId { get; set; }

        public string Comment { get; set; }
        public OfDelivery? DeliveryMethod { get; set; }
        public DateTime? Date { get; set; }
        public string Receipt { get; set; }
        public Guid? ReceiptDocumentId { get; set; }
        public string RowVersion { get; set; }
    }

    public class LetterVerifyDto
    {
        public int LetterId { get; set; }
        public string Number { get; set; }
        public bool Signed { get; set; }
        public bool Valid { get; set; }
        public string StoredHash { get; set; }
        public string ComputedHash { get; set; }

        /// <summary>Files (attachments or the signature image) whose bytes no longer match what was signed.</summary>
        public List<Guid> ChangedDocumentIds { get; set; } = new List<Guid>();
    }

    public class ReferLetterDto
    {
        public int LetterId { get; set; }
        public int? ParentReferralId { get; set; }
        public int? FromPositionId { get; set; }
        public List<int> ToPositionIds { get; set; } = new List<int>();
        public string Instruction { get; set; }
        public OfReferralAction Action { get; set; } = OfReferralAction.ForAction;
        public DateTime? DueDate { get; set; }
    }

    public class ReferralActionDto
    {
        public int Id { get; set; }

        /// <summary>comment, close or cancel.</summary>
        public string Action { get; set; }
        public string Text { get; set; }
        public int? ActingPositionId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ numbering and templates

    public class NumberingRuleDto
    {
        public int Id { get; set; }
        public OfLetterKind Kind { get; set; }
        public int? UnitId { get; set; }
        public string UnitCode { get; set; }
        public string Pattern { get; set; }
        public bool IsActive { get; set; }

        /// <summary>The next number this rule would give today (not reserved).</summary>
        public string Preview { get; set; }
    }

    public class SaveNumberingRuleDto
    {
        public int? Id { get; set; }
        public OfLetterKind Kind { get; set; }
        public int? UnitId { get; set; }
        public string Pattern { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class LetterTemplateDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public OfLetterKind? LetterKind { get; set; }
        public string HeaderHtml { get; set; }
        public string FooterHtml { get; set; }
        public string BodyHtml { get; set; }
        public List<int> ApprovalChain { get; set; } = new List<int>();
        public int? SignerPositionId { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveLetterTemplateDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public OfLetterKind? LetterKind { get; set; }
        public string HeaderHtml { get; set; }
        public string FooterHtml { get; set; }
        public string BodyHtml { get; set; }
        public List<int> ApprovalChain { get; set; } = new List<int>();
        public int? SignerPositionId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ------------------------------------------------------------------------------------------ legacy archive import

    public class ImportLetterRowDto
    {
        public string Number { get; set; }

        /// <summary>Persian (1403/05/17) or ISO date.</summary>
        public string Date { get; set; }
        public string Kind { get; set; }
        public string Subject { get; set; }
        public string Party { get; set; }
        public List<string> FileNames { get; set; } = new List<string>();

        /// <summary>Documents already uploaded through the existing upload API, one per file name.</summary>
        public List<Guid> DocumentIds { get; set; } = new List<Guid>();
    }

    public class ImportLettersDto
    {
        /// <summary>Rows of the manifest Excel (read by the front).</summary>
        public List<ImportLetterRowDto> Rows { get; set; } = new List<ImportLetterRowDto>();

        /// <summary>Or the manifest as CSV text: number, date, kind, subject, party, file names separated by |.</summary>
        public string Csv { get; set; }
    }

    public class ImportLettersResultDto
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<int> LetterIds { get; set; } = new List<int>();
    }
}
