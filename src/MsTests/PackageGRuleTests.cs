using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.AuditSeat;

namespace MsTests
{
    // Package G rules of the AUDIT catalogue (one test per rule; the names follow the code, see Every_Rule_Of_The_Catalogue_Has_Its_Own_Test_Here)
    public partial class PackageP9RuleTests
    {
        private static GlLegacyUse Use(GlLegacyKind kind, string key, int? wo = null, string number = "N-1") => new GlLegacyUse { Kind = kind, ObjectKey = key, WorkOrderId = wo, Number = number, Note = "legacy", UsedAt = DateTimeOffset.Now, UsedByUserId = 21 };

        [TestMethod]
        public void AUD_CO_001()
        {
            var h = Host();
            var bad = Put(h, Use(GlLegacyKind.PcaNotApplicable, "ReleaseNote:1", 5, "RN-1"));
            var adopted = Put(h, Use(GlLegacyKind.PcaNotApplicable, "ReleaseNote:2", 6, "RN-2"));
            Put(h, new PbProjectBaseline { WorkOrderId = 6, ProductId = 1, BomHeaderId = 1, IsCurrent = true });
            Only(Run(h, "AUD-CO-001"), "GlLegacyUse", bad.Id);
        }

        [TestMethod]
        public void AUD_CO_002()
        {
            var h = Host();
            Put(h, Use(GlLegacyKind.PcaNotApplicable, "ReleaseNote:1", 5));
            var bad = Put(h, Use(GlLegacyKind.QcAcceptWithoutList, "QcRecord:1", null, "QIR-1"));
            Only(Run(h, "AUD-CO-002"), "GlLegacyUse", bad.Id);
        }

        [TestMethod]
        public void AUD_CO_003()
        {
            var h = Host();
            Put(h, Use(GlLegacyKind.QcAcceptWithoutList, "QcRecord:1", null, "QIR-1"));
            var bad = Put(h, Use(GlLegacyKind.FaiNotOpened, "MfProductionOrder:MO-1", null, "MO-1"));
            Only(Run(h, "AUD-CO-003"), "GlLegacyUse", bad.Id);
        }

        [TestMethod]
        public void AUD_PO_004()
        {
            var h = Host();
            var doc = Put(h, new PcEngineeringDeliverable { ContractId = 1, DocumentNumber = "DWG-9", Title = "t" });
            var r1 = Put(h, new PcDocumentRevision { DeliverableId = doc.Id, Code = "A", Sequence = 1, Status = PcDocStatus.Superseded });
            var r2 = Put(h, new PcDocumentRevision { DeliverableId = doc.Id, Code = "B", Sequence = 2, Status = PcDocStatus.Released });
            var po = Put(h, new PurchaseOrder { Number = "PO-1", Status = PurchaseOrderStatus.Released, OrderDate = DateTime.Today, Lines = new List<PurchaseOrderLine>() });
            var bad = Put(h, new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 1, Quantity = 1, DrawingDeliverableId = doc.Id, DrawingRevisionId = r1.Id, DrawingNumber = "DWG-9", DrawingRevision = "A" });
            Put(h, new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 2, Quantity = 1, DrawingDeliverableId = doc.Id, DrawingRevisionId = r1.Id, DrawingNumber = "DWG-9", DrawingRevision = "A", SupplierConfirmedRevisionId = r2.Id, SupplierConfirmedRevision = "B", SupplierConfirmedRef = "letter 1" });
            Put(h, new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 3, Quantity = 1, DrawingDeliverableId = doc.Id, DrawingRevisionId = r2.Id, DrawingNumber = "DWG-9", DrawingRevision = "B" });
            Only(Run(h, "AUD-PO-004"), "PurchaseOrderLine", bad.Id);
        }
    }
}
