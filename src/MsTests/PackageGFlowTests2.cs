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
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.Finance;
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
            var told = h.Read(u => u.Set<OfInboxItem>().Where(i => i.SourceKey == PoDrawingCitation.Key(lineA) && i.Status != OfInboxStatus.Done).Select(i => new { i.RecipientRole, i.RecipientUserId }).ToList());
            Assert.AreEqual(1, told.Count(t => t.RecipientRole == "ENGINEERING"), "engineering is told");
            Assert.AreEqual(1, told.Count(t => t.RecipientRole == null && t.RecipientUserId != null), "the buyer of the order is told once, by name");
            Assert.AreEqual(0, told.Count(t => t.RecipientRole == "COMMERCIAL"), "no second item for the buyer through the commercial role");
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
            Put(h, new OpsReadinessException { WorkOrderId = 1, ProductId = 1, ItemCodes = "RD-1", Justification = "j", RequestedByUserId = 21, RequestedAt = DateTimeOffset.Now, Status = OpsReadinessExceptionStatus.Pending, PmoUserId = 50, PmoAt = DateTimeOffset.Now });
            Put(h, new OpsReadinessException { WorkOrderId = 3, ProductId = 1, ItemCodes = "RD-3", Justification = "still waits for the PMO", RequestedByUserId = 21, RequestedAt = DateTimeOffset.Now, Status = OpsReadinessExceptionStatus.Pending });
            Put(h, new AusFinding { Number = "AF-3", RuleCode = "AUD-X-001", Title = "void waits for the CEO", Status = AusFindingStatus.Open, ProcessCode = "QUALITY", ObjectType = "X", ObjectKey = "k9", DueDate = DateTime.Today, VoidReason = "r", VoidQaUserId = 60, VoidPmoUserId = 50 });
            Put(h, new OpsReadinessException { WorkOrderId = 2, ProductId = 1, ItemCodes = "RD-2", Justification = "j", RequestedByUserId = 21, RequestedAt = DateTimeOffset.Now, Status = OpsReadinessExceptionStatus.Approved });
            Put(h, new SafetySettingChange { Store = SafetyStore.Quality, Key = "Gl.Po.CiteDrawing", OldValue = "1", NewValue = "0", Weakening = true, Reason = "r", RequestedByUserId = 50, RequestedAt = DateTimeOffset.Now, Status = SafetyChangeStatus.Pending });
            Put(h, new ChainBreakAcceptance { BreakRunId = 1, BreakKind = "record-drift", Reason = "r", RequestedByUserId = 50, RequestedAt = DateTimeOffset.Now, QaUserId = 60, QaAt = DateTimeOffset.Now, PmoUserId = 50, PmoAt = DateTimeOffset.Now, Status = SafetyChangeStatus.Pending });
            Put(h, new OfInboxItem { RecipientRole = "CEO", Kind = OfInboxKind.Approval, Title = "approve x", Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now, SourceKey = "k", SourceModule = "m", SourceType = "t" });
            Put(h, new OfInboxItem { RecipientRole = "CEO", Kind = OfInboxKind.Approval, Title = "done x", Status = OfInboxStatus.Done, CreatedAt = DateTimeOffset.Now, SourceKey = "k2", SourceModule = "m", SourceType = "t" });
            var inbox = await h.As(Ceo, sp => sp.GetRequiredService<IGoLiveOverviewService>().CeoInboxAsync(Ct));
            Assert.IsTrue(inbox.Success, inbox.Message);
            Assert.AreEqual(7, inbox.Data.Groups.Count);
            foreach (string key in new[] { "findings", "finding-void", "acceptances", "readiness", "safety", "chain", "inbox" })
                Assert.AreEqual(1, inbox.Data.Groups.Single(g => g.Key == key).Count, key + " (a readiness exception the PMO has not decided is not the CEO's yet)");
            Assert.AreEqual(7, inbox.Data.Total);
            // the other roles (PMO, auditor) do not read the details of the CEO inbox
            Assert.IsFalse((await h.As(Pmo, sp => sp.GetRequiredService<IGoLiveOverviewService>().CeoInboxAsync(Ct))).Success);
            Assert.IsFalse((await h.As(Auditor, sp => sp.GetRequiredService<IGoLiveOverviewService>().CeoInboxAsync(Ct))).Success);
            Assert.IsTrue(inbox.Data.Groups.Single(g => g.Key == "findings").Items.Any(i => i.Title.StartsWith("AF-1 ")));
            Assert.IsFalse(inbox.Data.Groups.Single(g => g.Key == "findings").Items.Any(i => i.Title.StartsWith("AF-2 ")));
        }

        // ------------------------------------------------------------------------------------------------ record drift

        internal sealed class RawOne<T> : DbContext where T : class
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

        // ------------------------------------------------------------------------------------------------ fix round 1

        [TestMethod]
        public Task A_Direct_Change_Of_A_Work_Order_Creation_Date_Is_Found() => Tamper<WorkOrder>(h =>
        {
            int c = Put(h, new ContractEntity { Code = "C-W", Title = "c", Status = ContractStatus.Current }).Id;
            return Put(h, new WorkOrder { Code = "WO-W", Title = "wo", ContractId = c, CreateDate = DateTimeOffset.Now }).Id;
        }, w => w.CreateDate = DateTimeOffset.MinValue, "CreateDate");

        [TestMethod]
        public Task A_Direct_Change_Of_A_Po_Line_Unit_Price_Is_Found() => Tamper<PurchaseOrderLine>(h =>
        {
            var po = Put(h, new PurchaseOrder { Number = "PO-U", Status = PurchaseOrderStatus.Draft, OrderDate = DateTime.Today });
            return Put(h, new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 1, Quantity = 2, UnitPrice = 10 }).Id;
        }, l => l.UnitPrice = 1, "UnitPrice");

        [TestMethod]
        public async Task The_First_Verification_After_The_Update_Takes_The_Newly_Covered_Records_As_Its_Starting_Point_Once_And_A_Later_Change_Is_Still_Found()
        {
            AppHost h = Host("baseline");
            int c = Put(h, new ContractEntity { Code = "C-B", Title = "c", Status = ContractStatus.Current }).Id;
            int id = Put(h, new WorkOrder { Code = "WO-B", Title = "wo", ContractId = c, CreateDate = DateTimeOffset.Now }).Id;
            Assert.AreEqual("Ok", (await Incremental(h)).Status);
            RawOne<WorkOrder>.Table = h.Read(u => ((DbContext)u).Model.FindEntityType(typeof(WorkOrder)).GetTableName());
            void Behind(DateTimeOffset when) { using var raw = new RawOne<WorkOrder>(h.RawOptions<RawOne<WorkOrder>>()); raw.Set<WorkOrder>().Find(id).CreateDate = when; raw.SaveChanges(); }
            Behind(DateTimeOffset.Now.AddYears(-3));   // a historic gap that exists on the server before the update
            Put(h, new GlActivation { StartAt = DateTimeOffset.Now });

            var rehearsal = await h.As(Pmo, sp => sp.GetRequiredService<IGoLiveOverviewService>().DriftRehearsalAsync(Ct));
            Assert.IsTrue(rehearsal.Success, rehearsal.Message);
            Assert.IsTrue(rehearsal.Data.BaselineDue);
            Assert.AreEqual(1, rehearsal.Data.Tables.Single(t => t.EntityType == "WorkOrder").Differing);
            Assert.IsFalse((await h.As(Buyer, sp => sp.GetRequiredService<IGoLiveOverviewService>().DriftRehearsalAsync(Ct))).Success, "not for every role");
            Assert.AreEqual(0, h.Read(u => u.Set<FnAuditLog>().Count(l => l.EntityType == "RecordDriftBaseline")), "the dry run writes nothing");

            Assert.AreEqual("Ok", (await Incremental(h)).Status, "the historic gap is not a break of the chain on the first run");
            Assert.IsNotNull(h.Read(u => u.Set<GlActivation>().Single().DriftBaselineAt));
            Assert.AreEqual(1, h.Read(u => u.Set<FnAuditLog>().Count(l => l.EntityType == "RecordDriftBaseline")));
            var after = await h.As(Pmo, sp => sp.GetRequiredService<IGoLiveOverviewService>().DriftRehearsalAsync(Ct));
            Assert.IsFalse(after.Data.BaselineDue);
            Assert.AreEqual(0, after.Data.TotalDiffering);

            Behind(DateTimeOffset.Now.AddYears(-9));   // a new change behind the application after the starting point
            AuditChainRunDto found = await Incremental(h);
            Assert.AreEqual("Broken", found.Status);
            Assert.AreEqual("record-drift", found.BreakKind);
            Assert.AreEqual("WorkOrder", found.BreakEntityType);
        }

        // PO citation

        private (int Contract, int Doc, int Rev, int Po) PoWorld(AppHost h, string tag)
        {
            int contract = Put(h, new ContractEntity { Code = "C-" + tag, Title = "c", Status = ContractStatus.Current }).Id;
            int doc = Put(h, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-" + tag, Title = "t" }).Id;
            int rev = Put(h, new PcDocumentRevision { DeliverableId = doc, Code = "A", Sequence = 1, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(-5) }).Id;
            int po = Put(h, new PurchaseOrder { Number = "PO-" + tag, Status = PurchaseOrderStatus.Released, OrderDate = DateTime.Today, CreatorUserId = 46 }).Id;
            return (contract, doc, rev, po);
        }

        [TestMethod]
        public async Task A_Line_Whose_Item_Changes_To_A_Part_Without_Drawing_Loses_The_Old_Citation_And_An_Obsolete_Drawing_Makes_The_Line_Stale()
        {
            AppHost h = Host("cite2");
            var w = PoWorld(h, "C2");
            int withDrawing = Put(h, new InventoryItem { Code = "IT-C2A", Title = "a", PartNumber = "PN-C2A" }).Id;
            int without = Put(h, new InventoryItem { Code = "IT-C2B", Title = "b", PartNumber = "PN-C2B" }).Id;
            Put(h, new PcDocumentLink { DeliverableId = w.Doc, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-C2A" });
            int line = Put(h, new PurchaseOrderLine { PurchaseOrderId = w.Po, RowNumber = 1, ItemId = withDrawing, Quantity = 1 }).Id;
            h.Write(u => { var l = u.Set<PurchaseOrderLine>().Single(x => x.Id == line); Assert.IsTrue(PoDrawingCitation.CiteAsync(u, l, null, Ct).GetAwaiter().GetResult()); u.SaveChanges(); }, Seeder);
            Assert.AreEqual("DWG-C2", h.Read(u => u.Set<PurchaseOrderLine>().Single(x => x.Id == line).DrawingNumber));

            // the drawing is made obsolete: no revision is in force any more, the line is stale and cannot be "confirmed"
            h.Write(u => { u.Set<PcDocumentRevision>().Single(x => x.Id == w.Rev).Status = PcDocStatus.Obsolete; u.SaveChanges(); }, Seeder);
            var stale = h.Read(u => PoDrawingCitation.StaleAsync(u, null, Ct).GetAwaiter().GetResult());
            Assert.AreEqual(1, stale.Count);
            Assert.AreEqual(0, stale[0].CurrentRevisionId);
            var confirm = await h.As(Buyer, sp => sp.GetRequiredService<IPoCitationService>().ConfirmAsync(new GlPoConfirmDto { LineId = line, Reference = "supplier letter 12" }, Ct));
            Assert.IsFalse(confirm.Success, "an obsolete drawing has no revision to confirm");
            h.Write(u => { u.Set<PcDocumentRevision>().Single(x => x.Id == w.Rev).Status = PcDocStatus.Released; u.SaveChanges(); }, Seeder);

            // the item of the draft line is changed to a part with no drawing: the earlier citation must go
            h.Write(u =>
            {
                var l = u.Set<PurchaseOrderLine>().Single(x => x.Id == line);
                l.ItemId = without;
                Assert.IsTrue(PoDrawingCitation.CiteAsync(u, l, null, Ct).GetAwaiter().GetResult());
                u.SaveChanges();
            }, Seeder);
            var cleared = h.Read(u => u.Set<PurchaseOrderLine>().Single(x => x.Id == line));
            Assert.IsNull(cleared.DrawingRevisionId);
            Assert.IsNull(cleared.DrawingNumber);
            Assert.IsNull(cleared.DrawingRevision);
        }

        [TestMethod]
        public async Task The_One_Time_Job_Cites_The_Open_Lines_Of_The_Old_System_And_The_Uncited_List_Shows_What_Is_Left()
        {
            AppHost h = Host("backfill");
            var w = PoWorld(h, "BF");
            int item = Put(h, new InventoryItem { Code = "IT-BF", Title = "a", PartNumber = "PN-BF" }).Id;
            Put(h, new PcDocumentLink { DeliverableId = w.Doc, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-BF" });
            int line = Put(h, new PurchaseOrderLine { PurchaseOrderId = w.Po, RowNumber = 1, ItemId = item, Quantity = 1 }).Id;
            var before = await h.As(Buyer, sp => sp.GetRequiredService<IPoCitationService>().UncitedLinesAsync(new GlCandidateQuery(), Ct));
            Assert.IsTrue(before.Success, before.Message);
            Assert.AreEqual(1, before.Data.Total);
            Assert.AreEqual("IT-BF", before.Data.Items.Single().ItemCode);
            Assert.IsFalse((await h.As(AppHost.Principal(90, "user.one", "SALES"), sp => sp.GetRequiredService<IPoCitationService>().UncitedLinesAsync(new GlCandidateQuery(), Ct))).Success);
            int cited = 0;
            h.Write(u => { cited = PoDrawingCitation.BackfillAsync(u, null, Ct).GetAwaiter().GetResult(); }, Seeder);
            Assert.AreEqual(1, cited);
            Assert.AreEqual("DWG-BF", h.Read(u => u.Set<PurchaseOrderLine>().Single(x => x.Id == line).DrawingNumber));
            h.Write(u => { cited = PoDrawingCitation.BackfillAsync(u, null, Ct).GetAwaiter().GetResult(); }, Seeder);
            Assert.AreEqual(0, cited, "idempotent");
            Assert.AreEqual(0, (await h.As(Buyer, sp => sp.GetRequiredService<IPoCitationService>().UncitedLinesAsync(new GlCandidateQuery(), Ct))).Data.Total);
        }

        [TestMethod]
        public void A_Part_With_Several_Drawings_Cites_The_One_Linked_To_The_Item_Itself()
        {
            AppHost h = Host("primary");
            int contract = Put(h, new ContractEntity { Code = "C-PR", Title = "c", Status = ContractStatus.Current }).Id;
            int item = Put(h, new InventoryItem { Code = "IT-PR", Title = "a", PartNumber = "PN-PR" }).Id;
            int d1 = Put(h, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-A", Title = "t" }).Id;
            int d2 = Put(h, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-Z", Title = "t" }).Id;
            foreach (int d in new[] { d1, d2 }) Put(h, new PcDocumentRevision { DeliverableId = d, Code = "A", Sequence = 1, Status = PcDocStatus.Released, ReleasedAt = DateTimeOffset.Now.AddDays(-5) });
            Put(h, new PcDocumentLink { DeliverableId = d1, Kind = PcDocLinkKind.PartNumber, PartNumber = "PN-PR" });
            Put(h, new PcDocumentLink { DeliverableId = d2, Kind = PcDocLinkKind.Item, ItemId = item });
            var primary = h.Read(u => PoDrawingCitation.Primary(EdmsResolver.DrawingsForItemAsync(u, item, null, Ct).GetAwaiter().GetResult()));
            Assert.AreEqual("DWG-Z", primary.Number, "the drawing linked to the item itself comes before the lower number");
        }
    }
}
