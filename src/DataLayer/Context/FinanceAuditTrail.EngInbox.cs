using Entities.Model.NasbNiro.EngInbox;

namespace DataLayer.Context
{
    /// <summary>
    /// ENG-F1: the engineering inbox joins the tamper-evident chain like the other engineering records: who dismissed a package and with what reason, what status the agent gave it and what it created in the product bank.
    /// The line tables are structured copies of the package (their SHA-256 is the content hash of the package) and the scan-state row is a clock: both stay out of the chain to keep its volume down.
    /// In its own file so that it merges cleanly with the other packages.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddEngInbox()
        {
            AuditPd(typeof(EngInboxPackage), new[]
            {
                "SourceName", "Kind", "PlatformCode", "Standard", "ContractCode", "ProjectCode", "PackageVersion", "Title", "ContentHash", "OriginalPath", "StoredPath", "Status", "StatusNote", "RejectCode", "ErrorCount", "WarningCount",
                "BomCount", "OpcCount", "DocCount", "MdlCount", "SupersededByPackageId", "ApplyBlockReason", "AppliedAt", "ProductId", "BomHeaderId", "RoutingId", "LinkedContractId", "LinkedWorkOrderId", "DismissedByUserId", "DismissReason", "OriginatorUserId",
            }, "بستهٔ صندوق ورودی مهندسی");
            AuditPd(typeof(EngInboxAppliedItem), new[] { "PackageId", "Kind", "EntityId" }, "رکورد ساخته‌شده از بستهٔ صندوق ورودی مهندسی");
            AuditPd(typeof(EngInboxDocRef), new[] { "PackageId", "Number", "Title", "Revision", "DocType", "IsRequired", "FileName", "FileSize", "FileSha256" }, "مدرک مرجع بستهٔ صندوق ورودی مهندسی");
        }
    }
}
