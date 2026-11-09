using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.AuditSeat;
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
            // two releases of the same work order are ONE observation; a work order that got a baseline is cleared
            Put(h, Use(GlLegacyKind.PcaNotApplicable, "ReleaseNote:1", 5, "RN-1"));
            Put(h, Use(GlLegacyKind.PcaNotApplicable, "ReleaseNote:3", 5, "RN-3"));
            Put(h, Use(GlLegacyKind.PcaNotApplicable, "ReleaseNote:2", 6, "RN-2"));
            Put(h, new PbProjectBaseline { WorkOrderId = 6, ProductId = 1, BomHeaderId = 1, IsCurrent = true });
            Only(Run(h, "AUD-CO-001"), "WorkOrder", 5);
            Assert.AreEqual(AusSeverity.Observation, AusCatalog.Find("AUD-CO-001").Severity, "an observation, not a finding that needs three signatures per use");
            Assert.IsFalse(AusRecheckModeOf("AUD-CO-001"), "State mode: it clears itself when the baseline exists");
        }

        private static bool AusRecheckModeOf(string code) => AusCatalog.Find(code).Mode == AusRecheckMode.Event;

        [TestMethod]
        public void AUD_CO_002()
        {
            var h = Host();
            Put(h, Use(GlLegacyKind.PcaNotApplicable, "ReleaseNote:1", 5));
            // two QC acceptances on the same drawing revision are one observation; a revision that has a released list is cleared
            var u1 = Use(GlLegacyKind.QcAcceptWithoutList, "QcRecord:1", null, "QIR-1"); u1.RevisionId = 71;
            var u2 = Use(GlLegacyKind.QcAcceptWithoutList, "QcRecord:2", null, "QIR-2"); u2.RevisionId = 71;
            var u3 = Use(GlLegacyKind.QcAcceptWithoutList, "QcRecord:3", null, "QIR-3"); u3.RevisionId = 72;
            Put(h, u1); Put(h, u2); Put(h, u3);
            Put(h, new Entities.Model.NasbNiro.Quality.QcCharacteristicList { RevisionId = 72, Status = Entities.Model.NasbNiro.Quality.CharListStatus.Released });
            Only(Run(h, "AUD-CO-002"), "PcDocumentRevision", 71);
            Assert.AreEqual(AusSeverity.Observation, AusCatalog.Find("AUD-CO-002").Severity);
            Assert.IsFalse(AusRecheckModeOf("AUD-CO-002"));
        }

        [TestMethod]
        public void AUD_CO_003()
        {
            var h = Host();
            Put(h, Use(GlLegacyKind.QcAcceptWithoutList, "QcRecord:1", null, "QIR-1"));
            Put(h, new GlActivation { StartAt = DateTimeOffset.Now });
            // part 1: its only drawing was released before the cut-over, no list: two orders = ONE observation. Part 2: its drawing is new (after the cut-over): nothing.
            var i1 = Put(h, new Entities.Model.NasbNiro.Inventory.InventoryItem { Code = "IT-CO1", Title = "p1", PartNumber = "PN-CO1" });
            var i2 = Put(h, new Entities.Model.NasbNiro.Inventory.InventoryItem { Code = "IT-CO2", Title = "p2", PartNumber = "PN-CO2" });
            var d1 = Put(h, new PcEngineeringDeliverable { ContractId = 1, DocumentNumber = "DWG-CO1", Title = "t" });
            var d2 = Put(h, new PcEngineeringDeliverable { ContractId = 1, DocumentNumber = "DWG-CO2", Title = "t" });
            Put(h, new PcDocumentRevision { DeliverableId = d1.Id, Code = "A", Sequence = 1, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(-30) });
            Put(h, new PcDocumentRevision { DeliverableId = d2.Id, Code = "A", Sequence = 1, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(5) });
            Put(h, new PcDocumentLink { DeliverableId = d1.Id, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-CO1" });
            Put(h, new PcDocumentLink { DeliverableId = d2.Id, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-CO2" });
            var a = Use(GlLegacyKind.FaiNotOpened, "MfProductionOrder:MO-1", null, "MO-1"); a.ItemId = i1.Id;
            var b = Use(GlLegacyKind.FaiNotOpened, "MfProductionOrder:MO-2", null, "MO-2"); b.ItemId = i1.Id;
            var c = Use(GlLegacyKind.FaiNotOpened, "MfProductionOrder:MO-3", null, "MO-3"); c.ItemId = i2.Id;
            Put(h, a); Put(h, b); Put(h, c);
            Only(Run(h, "AUD-CO-003"), "InventoryItem", i1.Id);
            Assert.AreEqual(AusSeverity.Observation, AusCatalog.Find("AUD-CO-003").Severity);
            Assert.IsFalse(AusRecheckModeOf("AUD-CO-003"));
        }

        [TestMethod]
        public void AUD_CO_004()
        {
            var h = Host();
            Put(h, new GlActivation { StartAt = DateTimeOffset.Now });
            // released after the cut-over, linked to a part, no list: a Major finding. Released before the cut-over (legacy), and a client document: nothing.
            var d1 = Put(h, new PcEngineeringDeliverable { ContractId = 1, DocumentNumber = "DWG-CO41", Title = "t" });
            var d2 = Put(h, new PcEngineeringDeliverable { ContractId = 1, DocumentNumber = "DWG-CO42", Title = "t" });
            var d3 = Put(h, new PcEngineeringDeliverable { ContractId = 1, DocumentNumber = "DWG-CO43", Title = "t", ReceivedFromClient = true });
            var r1 = Put(h, new PcDocumentRevision { DeliverableId = d1.Id, Code = "00", Sequence = 1, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(1) });
            Put(h, new PcDocumentRevision { DeliverableId = d2.Id, Code = "00", Sequence = 1, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(-30) });
            Put(h, new PcDocumentRevision { DeliverableId = d3.Id, Code = "00", Sequence = 1, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(1) });
            foreach (var d in new[] { d1, d2, d3 }) Put(h, new PcDocumentLink { DeliverableId = d.Id, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-" + d.Id });
            Only(Run(h, "AUD-CO-004"), "PcDocumentRevision", r1.Id);
            Assert.AreEqual(AusSeverity.Major, AusCatalog.Find("AUD-CO-004").Severity);
            Assert.IsFalse(AusRecheckModeOf("AUD-CO-004"));
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
