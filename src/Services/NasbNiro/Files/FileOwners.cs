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
    /// R1t D8: the columns of the model that point at a stored file are found from the EF model by R1a2's <c>DocumentLinks</c> (a new file column cannot be missed); this class only says
    /// which roles may READ a file through the record that holds it. A record type that is not in the map fails closed: its files are read by the uploader and the PMO only.
    /// <c>null</c> roles mean the module has no read rule of its own (the legacy contract, tender, equipment and inventory screens are open to every signed-in user, so their files are too).
    /// A change of an owned file is never possible through the generic endpoints (R1a2 referrer lock), so there is no write map.
    /// </summary>
    internal static class FileOwners
    {
        /// <summary>Key: the class name of the record type, or "Type.Property" when one type holds files with different read sets.</summary>
        public static readonly IReadOnlyDictionary<string, string> ReadRoles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // open: the legacy modules read through [Authorize] only
            ["User"] = null, ["ContractFile"] = null, ["ContractEntity"] = null, ["BuyOrderFile"] = null, ["BuyOrder"] = null, ["Inquiry"] = null, ["ProcessFile"] = null,
            ["EquipmentProcessFile"] = null, ["TenderFile"] = null, ["InventoryDocument"] = null, ["InventoryLot"] = null, ["PartOperationFile"] = null, ["QcInspection"] = null,

            // finance: the readers of the ledger; the clearance letter of a contractor also goes to the payroll readers (it is about the insurance of the people)
            ["FnVoucherAttachment"] = FinanceRoles.Readers,
            ["FnPettyCashSettlementLine"] = FinanceRoles.Readers,
            ["FnInsuranceClearance"] = FinanceRoles.Readers + "," + FinanceRoles.PayrollReaders,

            // quality
            ["QcRecord"] = Quality.QualityRoles.Readers, ["TestReport"] = Quality.QualityRoles.Readers, ["MaterialCertificate"] = Quality.QualityRoles.Readers, ["CapaAction"] = Quality.QualityRoles.Readers,
            ["DocumentRevisionRecord"] = Quality.QualityRoles.Readers, ["Audit"] = Quality.QualityRoles.Readers, ["ManagementReview"] = Quality.QualityRoles.Readers,
            ["MeasuringEquipment"] = Quality.QualityRoles.Readers, ["CalibrationEvent"] = Quality.QualityRoles.Readers, ["Qualification"] = Quality.QualityRoles.Readers,
            ["Pqr"] = Quality.QualityRoles.Readers, ["ItpActivity"] = Quality.QualityRoles.Readers,

            // project control
            ["PcContractTerms"] = ProjectControl.ProjectControlRoles.Readers, ["PcGuaranteeEvent"] = ProjectControl.ProjectControlRoles.Readers,
            ["PcEngineeringDeliverable"] = ProjectControl.ProjectControlRoles.Readers, ["PcShipment"] = ProjectControl.ProjectControlRoles.Readers, ["PcVariationOrder"] = ProjectControl.ProjectControlRoles.Readers,
            // P-B7a: the explaining files of a progress report and the project photos are read by the project readers (a task attachment follows the task, see Special)
            ["PcProgressSubmission"] = ProjectControl.ProjectControlRoles.Readers, ["PcProjectPhoto"] = ProjectControl.ProjectControlRoles.Readers,

            // procurement, sales, after-sales
            ["ServiceAcceptance"] = Procurement.ProcurementRoles.Readers, ["SupplierInvoice"] = Procurement.ProcurementRoles.Readers, ["SupplierQuote"] = Procurement.ProcurementRoles.Readers,
            ["SlProposal"] = Sales.SalesRoles.Readers, ["SlProposal.CommercialDocumentId"] = Sales.SalesRoles.CommercialReaders,
            ["SlInquiry"] = Sales.SalesRoles.Readers,
            ["SvServiceOrder"] = AfterSales.SvRoles.OrderReaders,
        };

        /// <summary>The read roles for a link, or false when the record type is not in the map (the caller then fails closed).</summary>
        public static bool TryRoles(string entity, string property, out string roles) =>
            ReadRoles.TryGetValue(entity + "." + property, out roles) || ReadRoles.TryGetValue(entity, out roles);

        /// <summary>The record kinds that <see cref="FileAccessService"/> asks one by one because their rule needs the id of the owner.</summary>
        public static readonly string[] Special =
        {
            nameof(OfLetterAttachment), nameof(OfLetter), nameof(OfUserSignature), nameof(OfLeaveRequest), nameof(OfDecision), nameof(FnOutboxPackage), "TmTaskAttachment",
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
    }
}
