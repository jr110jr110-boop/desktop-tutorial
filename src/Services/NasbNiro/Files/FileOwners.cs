using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Common.IdentityToolkit;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Services.NasbNiro.Finance;
using Ct = Entities.Model.NasbNiro.Contracts;
using Inv = Entities.Model.NasbNiro.Inventory;
using Pc = Entities.Model.NasbNiro.ProjectControl;
using Pr = Entities.Model.NasbNiro.Procurement;
using Q = Entities.Model.NasbNiro.Quality;
using Sl = Entities.Model.NasbNiro.Sales;
using Sv = Entities.Model.NasbNiro.AfterSales;

namespace Services.NasbNiro.Files
{
    /// <summary>
    /// R1t D8: one record kind that can hold the id of a stored file, with the roles that may read the file through that record.
    /// <c>Roles</c> is the read set of the module that owns the record (the same set its own read endpoints use); null means the module has no read rule of its own
    /// (the legacy contract, tender, equipment and inventory screens are open to every signed-in user, so their files are too).
    /// </summary>
    internal sealed class FileOwnerProbe
    {
        public FileOwnerProbe(string kind, string roles, Func<IUnitOfWork, Guid, CancellationToken, Task<bool>> holds)
        {
            Kind = kind;
            Roles = roles;
            Holds = holds;
        }

        /// <summary>The class name of the record kind (a unit test pins the list against the model).</summary>
        public string Kind { get; }

        public string Roles { get; }

        public Func<IUnitOfWork, Guid, CancellationToken, Task<bool>> Holds { get; }
    }

    /// <summary>
    /// R1t D8: the columns of the model that point at a stored file, written out one by one (a test fails when a Guid column that looks like a file reference is not listed here
    /// or in <see cref="Special"/>). The records whose rule needs the owner's id (letters, leave requests, meeting decisions, outbox packages, the signature image of a user)
    /// are handled by <see cref="FileAccessService"/> itself.
    /// </summary>
    internal static class FileOwners
    {
        private static FileOwnerProbe Probe<T>(string roles, Func<Guid, Expression<Func<T, bool>>> match) where T : class =>
            new FileOwnerProbe(typeof(T).Name, roles,
                (uow, id, ct) => uow.Set<T>().AsNoTracking().IgnoreQueryFilters().AnyAsync(match(id), ct));

        /// <summary>
        /// The simple owners, in the order they are asked: the cheap and common ones first (a profile photo is fetched for every avatar on a screen).
        /// The search stops at the first record whose read set contains the caller.
        /// </summary>
        public static readonly IReadOnlyList<FileOwnerProbe> Simple = new List<FileOwnerProbe>
        {
            // open: the legacy modules read through [Authorize] only
            Probe<Entities.Identity.User>(null, id => x => x.ProfilePhotoId == id),
            Probe<Ct.ContractFile>(null, id => x => x.DocumentId == id),
            Probe<Ct.ContractEntity>(null, id => x => x.ProjectCharterFileId == id),
            Probe<Ct.BuyOrderFile>(null, id => x => x.DocumentId == id),
            Probe<Ct.BuyOrder>(null, id => x => x.LogShitId == id || x.TimeSheet == id),
            Probe<Ct.Inquiry>(null, id => x => x.TechnicalDoc == id || x.FinancialDoc == id),
            Probe<Ct.ProcessFile>(null, id => x => x.DocumentId == id),
            Probe<Entities.Model.NasbNiro.Equipments.EquipmentProcessFile>(null, id => x => x.DocumentId == id),
            Probe<Entities.Model.NasbNiro.Tenders.TenderFile>(null, id => x => x.DocumentId == id),
            Probe<Inv.InventoryDocument>(null, id => x => x.WeighbridgeDocumentId == id),
            Probe<Inv.InventoryLot>(null, id => x => x.CertificateDocumentId == id),
            Probe<Inv.PartOperationFile>(null, id => x => x.DocumentId == id),
            Probe<Inv.QcInspection>(null, id => x => x.ReportDocumentId == id),

            // finance: the readers of the ledger; the clearance letter of a contractor also goes to the payroll readers (it is about the insurance of the people)
            Probe<FnVoucherAttachment>(FinanceRoles.Readers, id => x => x.DocumentId == id),
            Probe<FnPettyCashSettlementLine>(FinanceRoles.Readers, id => x => x.DocumentId == id),
            Probe<FnInsuranceClearance>(FinanceRoles.Readers + "," + FinanceRoles.PayrollReaders, id => x => x.DocumentId == id),

            // quality
            Probe<Q.QcRecord>(Quality.QualityRoles.Readers, id => x => x.AttachmentDocumentId == id),
            Probe<Q.TestReport>(Quality.QualityRoles.Readers, id => x => x.DocumentId == id),
            Probe<Q.MaterialCertificate>(Quality.QualityRoles.Readers, id => x => x.DocumentId == id),
            Probe<Q.CapaAction>(Quality.QualityRoles.Readers, id => x => x.EvidenceDocumentId == id),
            Probe<Q.DocumentRevisionRecord>(Quality.QualityRoles.Readers, id => x => x.FileDocumentId == id),
            Probe<Q.Audit>(Quality.QualityRoles.Readers, id => x => x.ReportDocumentId == id),
            Probe<Q.ManagementReview>(Quality.QualityRoles.Readers, id => x => x.MinutesDocumentId == id),
            Probe<Q.MeasuringEquipment>(Quality.QualityRoles.Readers, id => x => x.CorrectionTableDocumentId == id),
            Probe<Q.CalibrationEvent>(Quality.QualityRoles.Readers, id => x => x.CertificateDocumentId == id),
            Probe<Q.Qualification>(Quality.QualityRoles.Readers, id => x => x.CertificateDocumentId == id),
            Probe<Q.Pqr>(Quality.QualityRoles.Readers, id => x => x.ResultsDocumentId == id),
            Probe<Q.ItpActivity>(Quality.QualityRoles.Readers, id => x => x.EvidenceDocumentId == id),

            // project control
            Probe<Pc.PcContractTerms>(ProjectControl.ProjectControlRoles.Readers, id => x => x.ChangeProcedureDocumentId == id),
            Probe<Pc.PcGuaranteeEvent>(ProjectControl.ProjectControlRoles.Readers, id => x => x.DocumentId == id),
            Probe<Pc.PcEngineeringDeliverable>(ProjectControl.ProjectControlRoles.Readers, id => x => x.DocumentId == id),
            Probe<Pc.PcShipment>(ProjectControl.ProjectControlRoles.Readers, id => x => x.PackingPhotoDocumentId == id || x.SignedReceiptDocumentId == id),

            // procurement, sales, after-sales
            Probe<Pr.ServiceAcceptance>(Procurement.ProcurementRoles.Readers, id => x => x.DocumentId == id),
            Probe<Pr.SupplierInvoice>(Procurement.ProcurementRoles.Readers, id => x => x.DocumentId == id),
            Probe<Pr.SupplierQuote>(Procurement.ProcurementRoles.Readers, id => x => x.DocumentId == id),
            Probe<Sl.SlProposal>(Sales.SalesRoles.Readers, id => x => x.TechnicalDocumentId == id),
            Probe<Sl.SlProposal>(Sales.SalesRoles.CommercialReaders, id => x => x.CommercialDocumentId == id),
            // the files of a sales RFQ are kept as a list of ids separated by commas
            Probe<Sl.SlInquiry>(Sales.SalesRoles.Readers, id => { string text = id.ToString(); return x => x.AttachmentIds != null && x.AttachmentIds.Contains(text); }),
            Probe<Sv.SvServiceOrder>(AfterSales.SvRoles.OrderReaders, id => x => x.ReportDocumentId == id),
        };

        /// <summary>The record kinds that <see cref="FileAccessService"/> asks one by one because their rule needs the id of the owner.</summary>
        public static readonly string[] Special =
        {
            nameof(OfLetterAttachment), nameof(OfLetter), nameof(OfUserSignature), nameof(OfLeaveRequest), nameof(OfDecision), nameof(FnOutboxPackage),
        };

        /// <summary>The roles that read an outbox package: the payroll packages hold salary data and go to the payroll readers only (the same split as the outbox service).</summary>
        public static string OutboxRoles(FnOutboxKind kind) =>
            kind == FnOutboxKind.PayrollInsuranceList || kind == FnOutboxKind.PayrollTaxList ? FinanceRoles.PayrollReaders : FinanceRoles.OutboxReaders;

        /// <summary>The letters that hold the file as an attachment, as the signature image or as the delivery receipt.</summary>
        public static async Task<List<int>> LetterIdsAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
        {
            List<int> ids = await uow.Set<OfLetterAttachment>().AsNoTracking().Where(a => a.DocumentId == id).Select(a => a.LetterId).ToListAsync(ct);
            ids.AddRange(await uow.Set<OfLetter>().AsNoTracking().IgnoreQueryFilters().Where(l => l.SignatureDocumentId == id || l.DeliveryReceiptDocumentId == id).Select(l => l.Id).ToListAsync(ct));
            return ids.Distinct().ToList();
        }
    }

    /// <summary>
    /// R1t D8: files that must not change or disappear once a decision rests on them. The letter and inspection plan locks are the ones the document service has always applied
    /// (a letter's hash covers the bytes of its files); the others are new: the attachment of a final voucher, the receipts of an approved or posted petty cash settlement,
    /// the scanned clearance letter of a contractor, and the attachment of an approved leave request. Every text ends with the same words, so a caller can recognise a lock.
    /// </summary>
    internal static class FileLocks
    {
        public const string LetterText = "این فایل بخشی از یک نامه ارسال‌شده یا امضای ثبت‌شده است و تغییر یا حذف آن مجاز نیست.";
        public const string ItpText = "این فایل مدرک انجام یک فعالیت برنامه بازرسی و آزمون است و تغییر یا حذف آن مجاز نیست.";
        public const string VoucherText = "این فایل پیوست یک سند حسابداری قطعی است و تغییر یا حذف آن مجاز نیست.";
        public const string SettlementText = "این فایل مدرک یک تسویه تنخواه تایید یا ثبت‌شده است و تغییر یا حذف آن مجاز نیست.";
        public const string ClearanceText = "این فایل نامه مفاصاحساب بیمه یک پیمانکار است و تغییر یا حذف آن مجاز نیست.";
        public const string LeaveText = "این فایل پیوست یک درخواست مرخصی یا ماموریت تایید‌شده است و تغییر یا حذف آن مجاز نیست.";

        /// <summary>Every reason, in the order the document service checks them; null when the file may change.</summary>
        public static async Task<string> ReasonAsync(IUnitOfWork uow, Guid id, CancellationToken ct = default)
        {
            bool letter = await uow.Set<OfLetterAttachment>().AnyAsync(a => a.DocumentId == id && a.Letter.Status != OfLetterStatus.Draft, ct)
                || await uow.Set<OfLetter>().AnyAsync(l => l.SignatureDocumentId == id || l.DeliveryReceiptDocumentId == id, ct)
                || await uow.Set<OfUserSignature>().AnyAsync(s => s.ImageDocumentId == id, ct);
            if (letter) return LetterText;
            if (await uow.Set<Q.ItpActivity>().AnyAsync(a => a.EvidenceDocumentId == id, ct)) return ItpText;
            return await RecordLockAsync(uow, id, ct);
        }

        /// <summary>The locks that R1t adds on top of the letter and inspection plan locks.</summary>
        public static async Task<string> RecordLockAsync(IUnitOfWork uow, Guid id, CancellationToken ct = default)
        {
            if (await uow.Set<FnVoucherAttachment>().AnyAsync(a => a.DocumentId == id && a.Voucher.Status == FnVoucherStatus.Final, ct)) return VoucherText;
            if (await uow.Set<FnPettyCashSettlementLine>().AnyAsync(l => l.DocumentId == id && (l.Settlement.Status == FnDocStatus.Approved || l.Settlement.Status == FnDocStatus.Posted), ct)) return SettlementText;
            if (await uow.Set<FnInsuranceClearance>().AnyAsync(c => c.DocumentId == id, ct)) return ClearanceText;
            if (await uow.Set<OfLeaveRequest>().AnyAsync(r => r.AttachmentDocumentId == id && (r.Status == OfLeaveStatus.Approved || r.Status == OfLeaveStatus.Recorded), ct)) return LeaveText;
            return null;
        }
    }

    /// <summary>R1t D8: the SHA-256 of the stored bytes of a file, kept in <see cref="Document.Sha256"/> at upload and at every replacement, and the audit row of a replacement.</summary>
    internal static class FileIntegrity
    {
        /// <summary>SHA-256 of the file as Base64 (the form the letters use for their attachments); null when there is no file at the path.</summary>
        public static async Task<string> Sha256Async(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using (FileStream stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(await sha.ComputeHashAsync(stream));
        }

        /// <summary>
        /// The signed-in user of the request, read from the HTTP context that the database context already holds for the audit trail
        /// (so the document service needs no new constructor argument); 0 outside a request.
        /// </summary>
        public static int CallerId(IUnitOfWork uow)
        {
            IHttpContextAccessor accessor = (uow as DbContext)?.GetService<IHttpContextAccessor>();
            return int.TryParse(accessor?.HttpContext?.User?.Identity?.GetUserId(), out int id) ? id : 0;
        }

        /// <summary>
        /// The row of the append-only trail for a replaced file: who, when, and the SHA-256 of the bytes before and after. It is only added to the context,
        /// so it is saved with the replacement itself. <paramref name="oldSha256"/> is the hash stored at the previous upload (null for a file uploaded before the hash was kept).
        /// </summary>
        public static void AuditReplaced(IUnitOfWork uow, Document document, string oldSha256, string newSha256)
        {
            string name = document.Name ?? document.Code ?? document.Id.ToString();
            AuditNotes.Add(uow, CallerId(uow), "Document", null, "FileReplaced", "جایگزینی فایل «" + name + "» (" + document.Id + ")",
                new { documentId = document.Id, sha256 = oldSha256 }, new { documentId = document.Id, sha256 = newSha256 });
        }
    }
}
