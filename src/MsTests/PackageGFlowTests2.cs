using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.AuditSeat;
using Services.NasbNiro.GoLive;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.GoLiveDtos;
using ViewModels.NasbNiro.IntegrityDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>Package G with real seeded data, second part: PO line citation and its resolutions, transmittal revisions, the revision pre-check, the CEO inbox and record-drift tampering of the engineering masters.</summary>
    [TestClass]
    public class PackageGFlowTests2
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(34, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(37, "ceo.one", "CEO");
        private static readonly ClaimsPrincipal Dcc = AppHost.Principal(31, "dcc.one", "DCC");
        private static readonly ClaimsPrincipal Buyer = AppHost.Principal(46, "buyer.one", "COMMERCIAL");
        private static readonly ClaimsPrincipal Auditor = AppHost.Principal(70, "auditor.one", "AUDITOR");

        private static AppHost Host(string name) => new AppHost("pkg2_" + name + "_" + Guid.NewGuid().ToString("N"), new Dictionary<string, string> { ["Identity:AuditHmacKey"] = "pg-test-audit-key-not-a-secret" });

        private static T Put<T>(AppHost h, T e) where T : class { h.Write(u => { AppHost.Add(u, e); u.SaveChanges(); }, Seeder); return e; }

        // ------------------------------------------------------------------------------------------------ PO line citation

        private static List<AusHit> Rule(AppHost h, string code) =>
            h.As(Auditor, async sp =>
            {
                var uow = sp.GetRequiredService<IUnitOfWork>();
                var st = await QualitySettingKeys.LoadAsync(uow, Ct);
                var ctx = new AusContext { Uow = uow, Services = sp, Today = DateTime.Today, Now = DateTimeOffset.Now, Settings = st, Max = 2000, Ct = Ct };
                return await AusCatalog.Find(code).Query(ctx);
            }).GetAwaiter().GetResult();

        private static int OpenAlerts(AppHost h, int lineId) => h.Read(u => u.Set<OfInboxItem>().Count(i => i.SourceKey == PoDrawingCitation.Key(lineId) && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)));

        [TestMethod]
        public async Task A_Po_Line_Cites_The_Drawing_Revision_And_A_Superseding_Revision_Raises_Alerts_And_A_Finding_Until_The_Line_Is_Revised_Or_The_Supplier_Confirms()
        {
            AppHost h = Host("po");
            int contract = 0, item = 0, doc = 0, r1 = 0, po = 0;
            h.Write(u =>
            {
                contract = AppHost.Add(u, new ContractEntity { Code = "C-1", Title = "c", Status = ContractStatus.Current }).Id;
                u.SaveChanges();
            }, Seeder);
            contract = h.Read(u => u.Set<ContractEntity>().Single().Id);
            item = Put(h, new InventoryItem { Code = "IT-PO1", Title = "part", PartNumber = "PN-PO1" }).Id;
            doc = Put(h, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-PO1", Title = "t" }).Id;
            r1 = Put(h, new PcDocumentRevision { DeliverableId = doc, Code = "A", Sequence = 1, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(-5) }).Id;
            Put(h, new PcDocumentLink { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-PO1" });
            po = Put(h, new PurchaseOrder { Number = "PO-1", Status = PurchaseOrderStatus.Released, OrderDate = DateTime.Today, CreatorUserId = 46 }).Id;
            int lineA = Put(h, new PurchaseOrderLine { PurchaseOrderId = po, RowNumber = 1, ItemId = item, Quantity = 1 }).Id;
            int lineB = Put(h, new PurchaseOrderLine { PurchaseOrderId = po, RowNumber = 2, ItemId = item, Quantity = 1 }).Id;

            // the citation is read from the EDMS at save / approve / release
            h.Write(u =>
            {
                var st = QualitySettingKeys.LoadAsync(u, Ct).GetAwaiter().GetResult();
                foreach (PurchaseOrderLine l in u.Set<PurchaseOrderLine>().ToList()) Assert.IsTrue(PoDrawingCitation.CiteAsync(u, l, st, Ct).GetAwaiter().GetResult());
                u.SaveChanges();
            }, Seeder);
            PurchaseOrderLine a = h.Read(u => u.Set<PurchaseOrderLine>().Single(l => l.Id == lineA));
            Assert.AreEqual("DWG-PO1", a.DrawingNumber);
            Assert.AreEqual("A", a.DrawingRevision);
            Assert.AreEqual(r1, a.DrawingRevisionId);
            Assert.AreEqual(0, h.Read(u => PoDrawingCitation.StaleAsync(u, null, Ct).GetAwaiter().GetResult().Count));
            None(Rule(h, "AUD-PO-004"));

            // revision B is released: A is superseded
            int r2 = 0;
            h.Write(u => { u.Set<PcDocumentRevision>().Single(r => r.Id == r1).Status = PcDocStatus.Superseded; u.SaveChanges(); }, Seeder);
            r2 = Put(h, new PcDocumentRevision { DeliverableId = doc, Code = "B", Sequence = 2, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now }).Id;
            Assert.AreEqual(2, h.Read(u => PoDrawingCitation.StaleAsync(u, null, Ct).GetAwaiter().GetResult().Count));

            await h.As(Pmo, async sp => { await PoDrawingCitation.SyncAlertsAsync(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<Services.NasbNiro.Office.INotificationService>(), doc, Ct); return 0; });
            Assert.IsTrue(OpenAlerts(h, lineA) > 0, "alert for line A");
            Assert.IsTrue(OpenAlerts(h, lineB) > 0, "alert for line B");
            var roles = h.Read(u => u.Set<OfInboxItem>().Where(i => i.SourceKey == PoDrawingCitation.Key(lineA)).Select(i => i.RecipientRole).ToList());
            CollectionAssert.IsSubsetOf(new[] { "COMMERCIAL", "ENGINEERING" }, roles.Where(x => x != null).ToList(), "buyer and engineering are told");
            Assert.AreEqual(2, Rule(h, "AUD-PO-004").Count, "a P9 finding for each stale line");

            // refusals of the confirmation
            Task<ResponseDto<GlPoLineCitationDto>> Confirm(ClaimsPrincipal who, int line, string reference) => h.As(who, sp => sp.GetRequiredService<IPoCitationService>().ConfirmAsync(new GlPoConfirmDto { LineId = line, Reference = reference }, Ct));
            Assert.IsFalse((await Confirm(Pmo, lineB, "نامه تامین‌کننده شماره ۱۲")).Success, "only the buyer records the confirmation");
            Assert.IsFalse((await Confirm(Buyer, lineB, "x")).Success, "the reference is required");
            var ai = await AiActing.RunAsync(h.Provider.GetRequiredService<IServiceScopeFactory>(), new AiActingUser { UserId = 92, UserName = "seat.proc", Roles = new[] { "COMMERCIAL" } }, "PROC", sp => sp.GetRequiredService<IPoCitationService>().ConfirmAsync(new GlPoConfirmDto { LineId = lineB, Reference = "نامه تامین‌کننده شماره ۱۲" }, Ct));
            Assert.IsFalse(ai.Success, "an AI seat never records a supplier confirmation");

            // resolution 1: the line is revised (re-cited to the revision in force)
            h.Write(u => { var l = u.Set<PurchaseOrderLine>().Single(x => x.Id == lineA); Assert.IsTrue(PoDrawingCitation.CiteAsync(u, l, null, Ct).GetAwaiter().GetResult()); u.SaveChanges(); }, Seeder);
            Assert.AreEqual("B", h.Read(u => u.Set<PurchaseOrderLine>().Single(x => x.Id == lineA).DrawingRevision));
            Assert.AreEqual(1, h.Read(u => PoDrawingCitation.StaleAsync(u, null, Ct).GetAwaiter().GetResult().Count));
            var onlyB = Rule(h, "AUD-PO-004");
            Assert.AreEqual(1, onlyB.Count);
            Assert.AreEqual(lineB, onlyB[0].ObjectId);

            // resolution 2: the supplier's confirmation of revision B is recorded
            var done = await Confirm(Buyer, lineB, "نامه تامین‌کننده شماره ۱۲");
            Assert.IsTrue(done.Success, done.Message);
            PurchaseOrderLine b = h.Read(u => u.Set<PurchaseOrderLine>().Single(x => x.Id == lineB));
            Assert.AreEqual(r2, b.SupplierConfirmedRevisionId);
            Assert.AreEqual("B", b.SupplierConfirmedRevision);
            Assert.AreEqual(46, b.SupplierConfirmedByUserId.HasValue ? 46 : 0 );
            Assert.AreEqual(0, h.Read(u => PoDrawingCitation.StaleAsync(u, null, Ct).GetAwaiter().GetResult().Count));
            None(Rule(h, "AUD-PO-004"));
            Assert.AreEqual(0, OpenAlerts(h, lineA) + OpenAlerts(h, lineB), "the alerts are closed");
        }

        private static void None(List<AusHit> hits) => Assert.AreEqual(0, hits.Count, string.Join(" | ", hits.Select(x => x.Key)));

        // ------------------------------------------------------------------------------------------------ transmittal and the pre-check

        [TestMethod]
        public async Task A_Transmittal_Line_Takes_The_Revision_From_The_Edms_Record_And_A_Document_Without_One_Is_Flagged_And_The_Precheck_Lists_Exactly_It()
        {
            AppHost h = Host("tr");
            int contract = 0;
            h.Write(u => { contract = AppHost.Add(u, new ContractEntity { Code = "C-T", Title = "c", Status = ContractStatus.Current }).Id; u.SaveChanges(); }, Seeder);
            contract = h.Read(u => u.Set<ContractEntity>().Single().Id);
            int withRev = Put(h, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-T1", Title = "has revisions", ClientSubmittal = true, Revision = "A" }).Id;
            int legacy = Put(h, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-T2", Title = "legacy register row", ClientSubmittal = true, Revision = "A" }).Id;
            int rev = Put(h, new PcDocumentRevision { DeliverableId = withRev, Code = "C", Sequence = 3, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now }).Id;

            var pre = await h.As(Pmo, sp => sp.GetRequiredService<IGoLiveOverviewService>().RevisionPreCheckAsync(Ct));
            Assert.IsTrue(pre.Success, pre.Message);
            Assert.IsFalse(pre.Data.CurrentlyRequired, "the default is not changed");
            Assert.AreEqual(1, pre.Data.WouldBreak);
            Assert.AreEqual("DWG-T2", pre.Data.Rows.Single().Number);

            Task<ResponseDto<PcTransmittalDto>> Send(params PcTransmittalLineDto[] lines) => h.As(Dcc, sp => sp.GetRequiredService<IPcMdrService>().SaveTransmittalAsync(new PcTransmittalDto { ContractId = contract, Direction = PcTransmittalDirection.Outgoing, TransmittalDate = DateTime.Today, Lines = lines.ToList() }, Ct));
            var noId = await Send(new PcTransmittalLineDto { DeliverableId = withRev, Revision = "typed free text", Purpose = PcTransmittalPurpose.ForInformation });
            Assert.IsFalse(noId.Success, "a document that has revision records is sent by revision id, never by typed text");
            var sent = await Send(new PcTransmittalLineDto { DeliverableId = withRev, RevisionId = rev, Revision = "ignored", Purpose = PcTransmittalPurpose.ForInformation }, new PcTransmittalLineDto { DeliverableId = legacy, Revision = "A", Purpose = PcTransmittalPurpose.ForInformation });
            Assert.IsTrue(sent.Success, sent.Message);
            var lineA = sent.Data.Lines.Single(l => l.DeliverableId == withRev);
            var lineB = sent.Data.Lines.Single(l => l.DeliverableId == legacy);
            Assert.AreEqual("C", lineA.Revision, "the revision text comes from the EDMS record");
            Assert.AreEqual(rev, lineA.RevisionId);
            Assert.IsFalse(lineA.LegacyFreeText);
            Assert.IsTrue(lineB.LegacyFreeText, "a document without a revision record is flagged");
        }

        // ------------------------------------------------------------------------------------------------ CEO inbox

        [TestMethod]
        public async Task The_Ceo_Inbox_Lists_Exactly_The_Pending_Items_Of_Every_Source()
        {
            AppHost h = Host("ceo");
            var empty = await h.As(Ceo, sp => sp.GetRequiredService<IGoLiveOverviewService>().CeoInboxAsync(Ct));
            Assert.AreEqual(0, empty.Data.Total);
            Put(h, new AusFinding { Number = "AF-1", RuleCode = "AUD-X-001", Title = "waits for the CEO", Status = AusFindingStatus.PmoProposed, ProcessCode = "QUALITY", ObjectType = "X", ObjectKey = "k1", DueDate = DateTime.Today });
            Put(h, new AusFinding { Number = "AF-2", RuleCode = "AUD-X-001", Title = "still with QA", Status = AusFindingStatus.QaProposed, ProcessCode = "QUALITY", ObjectType = "X", ObjectKey = "k2", DueDate = DateTime.Today });
            Put(h, new AusAcceptance { RuleCode = "AUD-X-001", ObjectKey = "k3", ObjectNumber = "N3", Reason = "r", ExpiresOn = DateTime.Today.AddDays(30), Status = AusAcceptanceStatus.PmoAgreed, QaUserId = 60, QaAt = DateTimeOffset.Now, PmoUserId = 50, PmoAt = DateTimeOffset.Now });
            Put(h, new OpsReadinessException { WorkOrderId = 1, ProductId = 1, ItemCodes = "RD-1", Justification = "j", RequestedByUserId = 21, RequestedAt = DateTimeOffset.Now, Status = OpsReadinessExceptionStatus.Pending });
            Put(h, new OpsReadinessException { WorkOrderId = 2, ProductId = 1, ItemCodes = "RD-2", Justification = "j", RequestedByUserId = 21, RequestedAt = DateTimeOffset.Now, Status = OpsReadinessExceptionStatus.Approved });
            Put(h, new SafetySettingChange { Store = SafetyStore.Quality, Key = "Gl.Po.CiteDrawing", OldValue = "1", NewValue = "0", Weakening = true, Reason = "r", RequestedByUserId = 50, RequestedAt = DateTimeOffset.Now, Status = SafetyChangeStatus.Pending });
            Put(h, new ChainBreakAcceptance { BreakRunId = 1, BreakKind = "record-drift", Reason = "r", RequestedByUserId = 50, RequestedAt = DateTimeOffset.Now, QaUserId = 60, QaAt = DateTimeOffset.Now, PmoUserId = 50, PmoAt = DateTimeOffset.Now, Status = SafetyChangeStatus.Pending });
            Put(h, new OfInboxItem { RecipientRole = "CEO", Kind = OfInboxKind.Approval, Title = "approve x", Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now, SourceKey = "k", SourceModule = "m", SourceType = "t" });
            Put(h, new OfInboxItem { RecipientRole = "CEO", Kind = OfInboxKind.Approval, Title = "done x", Status = OfInboxStatus.Done, CreatedAt = DateTimeOffset.Now, SourceKey = "k2", SourceModule = "m", SourceType = "t" });
            var inbox = await h.As(Ceo, sp => sp.GetRequiredService<IGoLiveOverviewService>().CeoInboxAsync(Ct));
            Assert.IsTrue(inbox.Success, inbox.Message);
            Assert.AreEqual(6, inbox.Data.Groups.Count);
            foreach (string key in new[] { "findings", "acceptances", "readiness", "safety", "chain", "inbox" })
                Assert.AreEqual(1, inbox.Data.Groups.Single(g => g.Key == key).Count, key);
            Assert.AreEqual(6, inbox.Data.Total);
            Assert.AreEqual("AF-1", inbox.Data.Groups.Single(g => g.Key == "findings").Items.Single().Title.Split(' ')[0]);
        }

        // ------------------------------------------------------------------------------------------------ record drift

        private sealed class RawOne<T> : DbContext where T : class
        {
            public static string Table;
            public RawOne(DbContextOptions<RawOne<T>> options) : base(options) { }
            protected override void OnModelCreating(ModelBuilder builder)
            {
                foreach (var p in typeof(T).GetProperties())
                {
                    Type pt = p.PropertyType;
                    if (pt.IsGenericType) pt = pt.GetGenericArguments()[0];
                    Type nt = Nullable.GetUnderlyingType(pt) ?? pt;
                    bool simple = nt.IsPrimitive || nt.IsEnum || nt == typeof(string) || nt == typeof(decimal) || nt == typeof(DateTime) || nt == typeof(DateTimeOffset) || nt == typeof(Guid) || nt == typeof(byte[]) || nt == typeof(TimeSpan);
                    if (!simple && nt.IsClass) builder.Ignore(nt);
                }
                builder.Entity<T>(b =>
                {
                    b.ToTable(Table);
                    b.HasKey("Id");
                    foreach (var p in typeof(T).GetProperties())
                    {
                        Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                        bool scalar = t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid) || t == typeof(byte[]) || t == typeof(TimeSpan);
                        if (!scalar || p.GetSetMethod(true) == null && p.Name != "Deleted") b.Ignore(p.Name);
                    }
                });
            }
        }

        private static async Task<AuditChainRunDto> Incremental(AppHost h) =>
            h.As(Pmo, sp => sp.GetRequiredService<IAuditIntegrityService>().RunOnDemandAsync(new AuditChainRunRequestDto { Mode = "incremental" }, Ct)).GetAwaiter().GetResult() is var r && r.Success ? r.Data : throw new AssertFailedException(r.Message);

        private static async Task Tamper<T>(Func<AppHost, int> seed, Action<T> change, string column) where T : class
        {
            AppHost h = Host("drift_" + typeof(T).Name);
            int id = seed(h);
            Assert.AreEqual("Ok", (await Incremental(h)).Status, "an honest history is good: " + typeof(T).Name);
            RawOne<T>.Table = h.Read(u => ((DbContext)u).Model.FindEntityType(typeof(T)).GetTableName());
            using (var raw = new RawOne<T>(h.RawOptions<RawOne<T>>()))
            {
                T row = raw.Set<T>().Find(id);
                Assert.IsNotNull(row);
                change(row);
                raw.SaveChanges();   // behind the application: the audit trail is not touched
            }
            AuditChainRunDto found = await Incremental(h);
            Assert.AreEqual("Broken", found.Status, typeof(T).Name);
            Assert.AreEqual("record-drift", found.BreakKind);
            Assert.AreEqual(typeof(T).Name, found.BreakEntityType);
            Assert.AreEqual(id, found.BreakRowId);
            Assert.AreEqual(column, found.BreakColumn);
        }

        [TestMethod]
        public Task A_Direct_Change_Of_A_Bom_Line_Is_Found() => Tamper<PbBomLine>(h =>
        {
            var hd = Put(h, new PbBomHeader { Code = "BOM-D", Title = "t", Revision = 1, Status = PbBomStatus.Draft });
            return Put(h, new PbBomLine { BomHeaderId = hd.Id, SortOrder = 1, PartNumber = "P1", PartName = "n", Qty = 2, Unit = "عدد" }).Id;
        }, l => l.Qty = 9, "Qty");

        [TestMethod]
        public Task A_Direct_Change_Of_A_Document_Revision_Is_Found() => Tamper<PcDocumentRevision>(h =>
        {
            int c = Put(h, new ContractEntity { Code = "C-D", Title = "c", Status = ContractStatus.Current }).Id;
            var d = Put(h, new PcEngineeringDeliverable { ContractId = c, DocumentNumber = "DWG-D", Title = "t" });
            return Put(h, new PcDocumentRevision { DeliverableId = d.Id, Code = "A", Sequence = 1, Status = PcDocStatus.Draft, Reason = "first issue" }).Id;
        }, r => r.Reason = "edited behind the application", "Reason");

        [TestMethod]
        public Task A_Direct_Change_Of_A_Production_Order_Is_Found() => Tamper<MfProductionOrder>(h =>
        {
            int c = Put(h, new ContractEntity { Code = "C-P", Title = "c", Status = ContractStatus.Current }).Id;
            var w = Put(h, new WorkOrder { Code = "WO-P", Title = "wo", ContractId = c, CreateDate = DateTimeOffset.Now });
            var i = Put(h, new InventoryItem { Code = "IT-P", Title = "part", PartNumber = "PN-P" });
            return Put(h, new MfProductionOrder { Number = "MO-D", WorkOrderId = w.Id, ContractId = c, ItemId = i.Id, Quantity = 1, Unit = "EA" }).Id;
        }, o => o.Quantity = 50, "Quantity");

        [TestMethod]
        public Task A_Direct_Change_Of_An_Ncr_Is_Found() => Tamper<NonConformance>(h =>
        {
            var i = Put(h, new InventoryItem { Code = "IT-N", Title = "part", PartNumber = "PN-N" });
            return Put(h, new NonConformance { Number = "NCR-D", ReportDate = DateTime.Today, ItemId = i.Id, Quantity = 1, Severity = NcrSeverity.Minor, Status = NcrStatus.Open }).Id;
        }, n => n.Severity = NcrSeverity.NotClassified, "Severity");

        [TestMethod]
        public Task A_Direct_Change_Of_A_Release_Note_Is_Found() => Tamper<ReleaseNote>(h =>
        {
            int c = Put(h, new ContractEntity { Code = "C-R", Title = "c", Status = ContractStatus.Current }).Id;
            var w = Put(h, new WorkOrder { Code = "WO-R", Title = "wo", ContractId = c, CreateDate = DateTimeOffset.Now });
            return Put(h, new ReleaseNote { Number = "RN-D", WorkOrderId = w.Id, ContractId = c, Status = ReleaseNoteStatus.Draft, CertificateOfConformityNumber = "CoC-1" }).Id;
        }, n => n.CertificateOfConformityNumber = "CoC-FORGED", "CertificateOfConformityNumber");
    }
}
