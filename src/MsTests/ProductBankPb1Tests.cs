using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductBankDtos;

namespace MsTests
{
    /// <summary>
    /// PB1: the item code of a part at a stage, the BOM revisions (released = read-only, new revision, diff, where-used, five different signers), the mapping of legacy codes
    /// approved by engineering, the MSPDI import, the OPC routings and the copy of a product into a project with its deviations.
    /// </summary>
    [TestClass]
    public class ProductBankPb1Tests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(2, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Eng1 = AppHost.Principal(11, "engineer.one", "ENGINEERING");
        private static readonly ClaimsPrincipal Eng2 = AppHost.Principal(12, "engineer.two", "ENGINEERING");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(13, "qc.one", "QC");
        private static readonly ClaimsPrincipal Shop = AppHost.Principal(14, "shop.one", "CONSTRUCTION");
        private static readonly ClaimsPrincipal Plan1 = AppHost.Principal(15, "planning.one", "PLANNING");
        private static readonly ClaimsPrincipal Plan2 = AppHost.Principal(16, "planning.two", "PLANNING");
        private static readonly ClaimsPrincipal Sales = AppHost.Principal(17, "sales.one", "COMMERCIAL");

        private static AppHost NewHost(string name) => new AppHost("Pb1_" + name + "_" + Guid.NewGuid().ToString("N"));
        private static readonly CancellationToken Ct = CancellationToken.None;

        private static Task<T> Run<T>(AppHost host, ClaimsPrincipal who, Func<IServiceProvider, Task<T>> call) => host.As(who, call);

        private static SavePbBomLineDto Line(int no, string pn, string name, decimal qty, PbMakeOrBuy mob = PbMakeOrBuy.Make) =>
            new SavePbBomLineDto { ItemNumber = no, PartNumber = pn, PartName = name, Qty = qty, Unit = "عدد", Material = "WCB", MakeOrBuy = mob, NetWeight = 10m, GrossWeight = 12m };

        private static SavePbBomDto Bom(params SavePbBomLineDto[] lines) =>
            new SavePbBomDto { AssemblyPartNumber = "99.11.00.000.2", Title = "BOM نمونه", Lines = lines.ToList() };

        /// <summary>Signs a BOM revision through the whole block and releases it.</summary>
        private static async Task<PbBomDto> ReleaseAsync(AppHost host, SavePbBomDto dto)
        {
            var saved = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(dto, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct))).Success);
            foreach (var (who, step) in new[] { (Eng2, PbBomSignature.Checked), (Qc, PbBomSignature.QcChecked), (Shop, PbBomSignature.ManufacturingChecked), (Pmo, PbBomSignature.Approved) })
            {
                var s = await Run(host, who, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = saved.Data.Id, Step = step }, Ct));
                Assert.IsTrue(s.Success, step + ": " + s.Message);
            }
            var rel = await Run(host, Pmo2, p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsTrue(rel.Success, rel.Message);
            return rel.Data;
        }

        // ------------------------------------------------------------------------------------------ item coding by part number

        [TestMethod]
        public async Task StageItemCode_IsPartNumberSeparatorAndStage_AndTitleIsDerived()
        {
            AppHost host = NewHost("code");
            var part = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.001.2", PartTitle = "Wheel case", Material = "WCB" }, Ct));
            Assert.IsTrue(part.Success, part.Message);
            Assert.AreEqual("99.11.01.001.2", part.Data.Code, "the part itself: code = part number");

            var stage = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.001.2", StageCode2 = "RL" }, Ct));
            Assert.IsTrue(stage.Success, stage.Message);
            Assert.AreEqual("99.11.01.001.2-RL", stage.Data.Code, "code = part number + separator + stage");
            StringAssert.Contains(stage.Data.Title, "Wheel case");
            InventoryItem row = host.Read(u => u.Set<InventoryItem>().Single(i => i.Code == "99.11.01.001.2-RL"));
            Assert.AreEqual("RL", row.StageCode2);
            Assert.AreEqual("99.11.01.001.2", row.PartNumber);
            Assert.IsNotNull(row.PartItemId, "the stage item points at the item of the part");

            var again = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.001.2", StageCode2 = "RL" }, Ct));
            Assert.IsFalse(again.Success, "one item per part and stage");
            var unknown = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.001.2", StageCode2 = "ZZ" }, Ct));
            Assert.IsFalse(unknown.Success, "a stage that is not in the dictionary");
        }

        [TestMethod]
        public async Task StageSeparator_IsASetting_AndOnlyPlanningChangesIt()
        {
            AppHost host = NewHost("sep");
            var denied = await Run(host, Sales, p => p.GetRequiredService<IPbCodingService>().SaveSettingAsync(new SavePbSettingDto { Key = PbSettingKeys.StageSeparator, Value = "/" }, Ct));
            Assert.IsFalse(denied.Success);
            var ok = await Run(host, Plan1, p => p.GetRequiredService<IPbCodingService>().SaveSettingAsync(new SavePbSettingDto { Key = PbSettingKeys.StageSeparator, Value = "/" }, Ct));
            Assert.IsTrue(ok.Success, ok.Message);
            var preview = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().PreviewAsync("99.11.01.002.2", "CT", "Casing cover", Ct));
            Assert.AreEqual("99.11.01.002.2/CT", preview.Data.Code);
        }

        [TestMethod]
        public async Task StageDictionary_HasNoDuplicateCodes_AndPersianTitles()
        {
            AppHost host = NewHost("stages");
            var stages = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().GetStagesAsync(true, Ct));
            Assert.IsTrue(stages.Data.Count >= 35);
            Assert.AreEqual(stages.Data.Count, stages.Data.Select(s => s.Code).Distinct().Count(), "RM no longer stands for two stages");
            Assert.IsTrue(stages.Data.All(s => s.Code.Length == 2 && !string.IsNullOrWhiteSpace(s.TitleFa)));
            Assert.IsTrue(stages.Data.Any(s => s.Code == "RW") && stages.Data.Any(s => s.Code == "RG"));
        }

        [TestMethod]
        public async Task NearDuplicate_SameTitleUnderAnotherPartNumber_NeedsConfirmation()
        {
            AppHost host = NewHost("dup");
            Assert.IsTrue((await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.003.2", PartTitle = "Governor housing" }, Ct))).Success);
            var near = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.004.2", PartTitle = "Governor housing" }, Ct));
            Assert.IsFalse(near.Success, "the same title under another part number is a warning that stops until it is confirmed");
            var confirmed = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.004.2", PartTitle = "Governor housing", ConfirmNearDuplicate = true }, Ct));
            Assert.IsTrue(confirmed.Success, confirmed.Message);
        }

        // ------------------------------------------------------------------------------------------ BOM revisions

        [TestMethod]
        public async Task ReleasedRevision_IsReadOnly_AndNewRevisionCopiesIt()
        {
            AppHost host = NewHost("immutable");
            PbBomDto r0 = await ReleaseAsync(host, Bom(Line(1, "99.11.01.001.2", "Wheel case", 1), Line(2, "99.11.01.002.2", "Casing cover", 2)));
            Assert.AreEqual(PbBomStatus.Released, r0.Status);
            Assert.AreEqual(0, r0.Revision);

            var edit = Bom(Line(1, "99.11.01.001.2", "Wheel case", 5));
            edit.Id = r0.Id;
            var refused = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(edit, Ct));
            Assert.IsFalse(refused.Success, "a released revision is not edited");
            Assert.AreEqual(2, host.Read(u => u.Set<PbBomLine>().Count(l => l.BomHeaderId == r0.Id && !l.Deleted)), "the lines are untouched");
            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().DeleteDraftAsync(r0.Id, Ct))).Success, "and not deleted");

            var rev = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().NewRevisionAsync(new PbBomActionDto { Id = r0.Id, Note = "تغییر تعداد" }, Ct));
            Assert.IsTrue(rev.Success, rev.Message);
            Assert.AreEqual(1, rev.Data.Revision);
            Assert.AreEqual(PbBomStatus.Draft, rev.Data.Status);
            Assert.AreEqual(2, rev.Data.Lines.Count(l => !l.IsHeading), "the new revision starts as a copy");
            Assert.AreEqual(r0.Id, rev.Data.SupersedesId);
            var second = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().NewRevisionAsync(new PbBomActionDto { Id = r0.Id, Note = "دوباره" }, Ct));
            Assert.IsFalse(second.Success, "one open revision at a time");
        }

        [TestMethod]
        public async Task Signatures_AreDistinctUsers_InOrder_WithTheRightRoles()
        {
            AppHost host = NewHost("sign");
            Assert.IsTrue((await Run(host, Plan1, p => p.GetRequiredService<IPbCodingService>().SaveSettingAsync(new SavePbSettingDto { Key = PbSettingKeys.DistinctReviewers, Value = "true" }, Ct))).Success);
            var saved = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(Bom(Line(1, "99.11.01.001.2", "Wheel case", 1)), Ct));
            await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct));
            int id = saved.Data.Id;

            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.Checked }, Ct))).Success, "the preparer does not check his own BOM");
            Assert.IsFalse((await Run(host, Qc, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.QcChecked }, Ct))).Success, "out of order");
            Assert.IsFalse((await Run(host, Sales, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.Checked }, Ct))).Success, "wrong role");
            Assert.IsTrue((await Run(host, Eng2, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.Checked }, Ct))).Success);
            Assert.IsTrue((await Run(host, Qc, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.QcChecked }, Ct))).Success);
            Assert.IsTrue((await Run(host, Shop, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.ManufacturingChecked }, Ct))).Success);

            // Eng2 already checked: he cannot also approve, even though ENGINEERING may approve
            var twice = await Run(host, Eng2, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.Approved }, Ct));
            Assert.IsFalse(twice.Success);
            Assert.IsTrue((await Run(host, Pmo, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.Approved }, Ct))).Success);
            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = id }, Ct))).Success, "the preparer does not release");
            Assert.IsTrue((await Run(host, Pmo2, p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = id }, Ct))).Success);

            PbBomHeader h = host.Read(u => u.Set<PbBomHeader>().Single(x => x.Id == id));
            Assert.AreEqual(5, new[] { h.PreparedByUserId, h.CheckedByUserId.Value, h.QcCheckedByUserId.Value, h.ManufacturingCheckedByUserId.Value, h.ApprovedByUserId.Value }.Distinct().Count());
        }

        [TestMethod]
        public async Task AiSeat_NeverSignsOrReleases()
        {
            AppHost host = NewHost("ai");
            var saved = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(Bom(Line(1, "99.11.01.001.2", "Wheel case", 1)), Ct));
            await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct));
            var ai = new Services.NasbNiro.Ai.AiActingUser { UserId = 99, UserName = "ai.engineer", DisplayName = "ai", Roles = new[] { "ENGINEERING" } };
            var result = await host.As(AppHost.Principal(99, "ai.engineer", "ENGINEERING"), p =>
                Services.NasbNiro.Ai.AiActing.RunAsync(p.GetRequiredService<IServiceScopeFactory>(), ai, "TEST", async sp =>
                    await sp.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = saved.Data.Id, Step = PbBomSignature.Checked }, Ct)));
            Assert.IsFalse(result.Success);
            Assert.IsNull(host.Read(u => u.Set<PbBomHeader>().Single(x => x.Id == saved.Data.Id).CheckedByUserId));
        }

        [TestMethod]
        public async Task Diff_ShowsAddedRemovedAndChangedLines()
        {
            AppHost host = NewHost("diff");
            PbBomDto r0 = await ReleaseAsync(host, Bom(Line(1, "99.11.01.001.2", "Wheel case", 1), Line(2, "99.11.01.002.2", "Casing cover", 2), Line(3, "99.11.01.003.2", "Governor housing", 1)));
            var rev = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().NewRevisionAsync(new PbBomActionDto { Id = r0.Id, Note = "تغییر" }, Ct));
            var edit = Bom(Line(1, "99.11.01.001.2", "Wheel case", 1), Line(2, "99.11.01.002.2", "Casing cover", 4), Line(4, "99.11.01.004.2", "Bearing cap", 1));
            edit.Id = rev.Data.Id; edit.ChangeReason = "تغییر";
            var saved = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(edit, Ct));
            Assert.IsTrue(saved.Success, saved.Message);

            var diff = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().DiffAsync(r0.Id, rev.Data.Id, Ct));
            Assert.IsTrue(diff.Success, diff.Message);
            CollectionAssert.AreEqual(new[] { "99.11.01.004.2" }, diff.Data.Added.Select(a => a.PartNumber).ToArray());
            CollectionAssert.AreEqual(new[] { "99.11.01.003.2" }, diff.Data.Removed.Select(a => a.PartNumber).ToArray());
            Assert.AreEqual(1, diff.Data.Changed.Count);
            Assert.AreEqual("99.11.01.002.2", diff.Data.Changed[0].PartNumber);
            Assert.IsTrue(diff.Data.Changed[0].Fields.Any(f => f.Old == "2" && f.New == "4"), "the quantity change is shown with both values");
            Assert.AreEqual(1, diff.Data.Unchanged);
        }

        [TestMethod]
        public async Task WhereUsed_ListsTheRevisionsThatUseAPart()
        {
            AppHost host = NewHost("where");
            PbBomDto r0 = await ReleaseAsync(host, Bom(Line(1, "99.11.01.001.2", "Wheel case", 1), Line(2, "99.11.01.002.2", "Casing cover", 2)));
            var used = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().WhereUsedAsync("99.11.01.002.2", Ct));
            Assert.IsTrue(used.Success);
            Assert.AreEqual(1, used.Data.Rows.Count);
            Assert.AreEqual(2m, used.Data.Rows[0].Qty);
            Assert.AreEqual(r0.Id, used.Data.Rows[0].BomId);
            Assert.AreEqual(0, (await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().WhereUsedAsync("99.11.09.999.2", Ct))).Data.Rows.Count);
        }

        [TestMethod]
        public async Task BomChanges_AreInTheAuditTrail()
        {
            AppHost host = NewHost("audit");
            await ReleaseAsync(host, Bom(Line(1, "99.11.01.001.2", "Wheel case", 1)));
            Assert.IsTrue(host.Read(u => u.Set<Entities.Model.NasbNiro.Finance.FnAuditLog>().Any(l => l.EntityType == nameof(PbBomHeader) && l.AfterJson.Contains("Released"))), "the release is in the trail");
        }

        // ------------------------------------------------------------------------------------------ mapping of legacy codes

        [TestMethod]
        public async Task Mapping_IsApprovedByEngineeringNotTheProposer_AndSetsTheLegacyCode()
        {
            AppHost host = NewHost("mapping");
            await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.001.2", PartTitle = "Wheel case" }, Ct));
            var import = await Run(host, Plan1, p => p.GetRequiredService<IPbCodingService>().ImportMappingsAsync(new List<PbImportMappingRowDto>
            {
                new PbImportMappingRowDto { LegacyCode = "02101001000010", LegacyTitle = "Wheel case raw", PartNumber = "99.11.01.001.2", StageCode2 = "RW" },
                new PbImportMappingRowDto { LegacyCode = "02101001000010", PartNumber = "99.11.01.001.2" },
                new PbImportMappingRowDto { LegacyCode = "", PartNumber = "99.11.01.001.2" },
            }, Ct));
            Assert.AreEqual(1, import.Data.Created);
            Assert.AreEqual(2, import.Data.Skipped, "a second live mapping for the same old code, and a row without a code");

            PbLegacyMapping m = host.Read(u => u.Set<PbLegacyMapping>().Single());
            Assert.AreEqual(PbMappingStatus.Draft, m.Status);
            Assert.AreEqual(100m, m.Confidence);

            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbCodingService>().DecideMappingAsync(new PbDecideDto { Id = m.Id, Approve = true }, Ct))).Success, "the importer does not approve (and planning is not engineering)");
            Assert.IsFalse((await Run(host, Sales, p => p.GetRequiredService<IPbCodingService>().DecideMappingAsync(new PbDecideDto { Id = m.Id, Approve = true }, Ct))).Success);
            Assert.IsNull(host.Read(u => u.Set<InventoryItem>().Single(i => i.Code == "99.11.01.001.2").LegacyCode), "a draft changes nothing");

            var approved = await Run(host, Eng2, p => p.GetRequiredService<IPbCodingService>().DecideMappingAsync(new PbDecideDto { Id = m.Id, Approve = true }, Ct));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(PbMappingStatus.Approved, approved.Data.Status);
            var stageItem = host.Read(u => u.Set<InventoryItem>().Single(i => i.PartNumber == "99.11.01.001.2" && i.StageCode2 == "RW"));
            Assert.AreEqual("02101001000010", stageItem.LegacyCode, "the 14-digit code stays as the legacy code of the stage item");
            Assert.AreEqual("99.11.01.001.2-RW", stageItem.Code);
        }

        [TestMethod]
        public async Task Mapping_ProposerCannotApproveEvenWithTheRole()
        {
            AppHost host = NewHost("mapping2");
            await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.001.2", PartTitle = "Wheel case" }, Ct));
            await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().ImportMappingsAsync(new List<PbImportMappingRowDto> { new PbImportMappingRowDto { LegacyCode = "02101001000010", PartNumber = "99.11.01.001.2" } }, Ct));
            int id = host.Read(u => u.Set<PbLegacyMapping>().Single().Id);
            var own = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().DecideMappingAsync(new PbDecideDto { Id = id, Approve = true }, Ct));
            Assert.IsFalse(own.Success, "maker and checker are different people");
        }

        [TestMethod]
        public async Task SuggestedMapping_HasAConfidence_AndIsADraft()
        {
            AppHost host = NewHost("suggest");
            host.Write(u =>
            {
                AppHost.Add(u, new InventoryItem { Code = "02101001000010", LegacyCode = "02101001000010", Title = "Wheel case -قطعه خام ریخته گری", GroupCode = "02", Unit = "عدد", IsActive = true });
                AppHost.Add(u, new InventoryItem { Code = "06005962000000", LegacyCode = "06005962000000", Title = "چسب لاکتایت 270", GroupCode = "06", Unit = "عدد", IsActive = true });
                u.SaveChanges();
            }, Pmo);
            await ReleaseAsync(host, Bom(Line(1, "99.11.01.001.2", "Wheel case FY", 1)));
            var sug = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().SuggestMappingsAsync(Ct));
            Assert.IsTrue(sug.Success, sug.Message);
            var list = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().GetMappingsAsync(new PbMappingQuery { PageSize = 50 }, Ct));
            var rows = list.Data.ToList();
            Assert.AreEqual(1, rows.Count, "only the wheel case matches; the glue does not");
            Assert.AreEqual(PbMappingSource.Suggested, rows[0].Source);
            Assert.AreEqual(PbMappingStatus.Draft, rows[0].Status);
            Assert.IsTrue(rows[0].Confidence >= 50m && rows[0].Confidence <= 100m);
            Assert.AreEqual("RW", rows[0].StageCode2, "the description says raw part");
        }

        // ------------------------------------------------------------------------------------------ MSPDI

        private const string Mspdi = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Project xmlns=""http://schemas.microsoft.com/project"">
  <Name>Sample</Name><MinutesPerDay>480</MinutesPerDay>
  <Tasks>
    <Task><UID>0</UID><ID>0</ID><Name>Sample</Name><OutlineLevel>0</OutlineLevel></Task>
    <Task><UID>1</UID><ID>1</ID><Name>Casing</Name><OutlineLevel>1</OutlineLevel><Summary>1</Summary><Duration>PT120H0M0S</Duration><Start>2026-01-04T08:00:00</Start></Task>
    <Task><UID>2</UID><ID>2</ID><Name>Wheel case 99.11.01.001.2 MPL</Name><OutlineLevel>2</OutlineLevel><Summary>1</Summary><Duration>PT120H0M0S</Duration><Start>2026-01-04T08:00:00</Start></Task>
    <Task><UID>3</UID><ID>3</ID><Name>E</Name><OutlineLevel>3</OutlineLevel><Duration>PT48H0M0S</Duration><Start>2026-01-04T08:00:00</Start></Task>
    <Task><UID>4</UID><ID>4</ID><Name>P</Name><OutlineLevel>3</OutlineLevel><Duration>PT60H0M0S</Duration><Start>2026-01-10T08:00:00</Start>
      <PredecessorLink><PredecessorUID>3</PredecessorUID><Type>1</Type><LinkLag>4800</LinkLag></PredecessorLink></Task>
    <Task><UID>5</UID><ID>5</ID><Name>C</Name><OutlineLevel>3</OutlineLevel><Duration>PT12H0M0S</Duration><Start>2026-01-20T08:00:00</Start>
      <PredecessorLink><PredecessorUID>4</PredecessorUID><Type>1</Type><LinkLag>0</LinkLag></PredecessorLink></Task>
    <Task><UID>6</UID><ID>6</ID><Name>Done</Name><OutlineLevel>1</OutlineLevel><Milestone>1</Milestone><Duration>PT0H0M0S</Duration><Start>2026-01-25T08:00:00</Start></Task>
  </Tasks>
  <Resources><Resource><UID>1</UID><Name>CNC Milling</Name></Resource></Resources>
  <Assignments><Assignment><TaskUID>5</TaskUID><ResourceUID>1</ResourceUID></Assignment></Assignments>
</Project>";

        [TestMethod]
        public void MspdiParser_ReadsOutlineDurationsLinksAndResources()
        {
            var (tasks, name, error) = PbMspdi.Parse(Mspdi);
            Assert.IsNull(error);
            Assert.AreEqual("Sample", name);
            Assert.AreEqual(6, tasks.Count, "the project summary row (UID 0) is not a task");
            Assert.AreEqual(15m, tasks.Single(t => t.Uid == 1).DurationDays, "PT120H at 480 minutes a day = 15 days");
            Assert.AreEqual(6m, tasks.Single(t => t.Uid == 3).DurationDays);
            Assert.AreEqual(1.5m, tasks.Single(t => t.Uid == 5).DurationDays);
            var p = tasks.Single(t => t.Uid == 4);
            Assert.AreEqual(1, p.Predecessors.Count);
            Assert.AreEqual(3, p.Predecessors[0].PredecessorUid);
            Assert.AreEqual(PbLinkType.FinishToStart, p.Predecessors[0].Type);
            Assert.AreEqual(1m, p.Predecessors[0].LagDays, "LinkLag 4800 tenths of a minute = 480 minutes = 1 day");
            Assert.IsTrue(tasks.Single(t => t.Uid == 6).IsMilestone);
            CollectionAssert.AreEqual(new[] { "CNC Milling" }, tasks.Single(t => t.Uid == 5).Resources.ToArray());
            Assert.AreEqual("E", PbMspdi.PhaseOf("E"));
            Assert.AreEqual("99.11.01.001.2", PbMspdi.PartNumberOf("Wheel case 99.11.01.001.2 MPL"));
        }

        [TestMethod]
        public void MspdiParser_RefusesBrokenXml_AndForeignFiles()
        {
            Assert.IsNotNull(PbMspdi.Parse("<Project").Error);
            Assert.IsNotNull(PbMspdi.Parse("<html/>").Error);
            Assert.IsNotNull(PbMspdi.Parse("").Error);
            Assert.IsNotNull(PbMspdi.Parse("<!DOCTYPE x [<!ENTITY a \"b\">]><Project xmlns=\"http://schemas.microsoft.com/project\"/>").Error, "a DTD is refused");
        }

        [TestMethod]
        public async Task MspdiImport_MakesATemplate_ApprovedByEngineeringThenAnotherPlanner()
        {
            AppHost host = NewHost("tpl");
            var product = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().SaveProductAsync(new SavePbProductDto { Code = "99.11.00.000.2", Title = "Test product" }, Ct));
            var imp = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().ImportMspdiAsync(new PbImportMspdiDto { ProductId = product.Data.Id, Xml = Mspdi }, Ct));
            Assert.IsTrue(imp.Success, imp.Message);
            Assert.AreEqual(6, imp.Data.Nodes.Count);
            Assert.AreEqual(2, imp.Data.Links.Count);
            Assert.IsFalse(imp.Data.IsApproved);
            Assert.AreEqual(15m, imp.Data.TotalDurationDays);
            var wheel = imp.Data.Nodes.Single(n => n.Title.StartsWith("Wheel case"));
            Assert.AreEqual("99.11.01.001.2", wheel.PartNumber);
            Assert.AreEqual(PbMakeOrBuy.Make, wheel.MakeOrBuy);
            Assert.AreEqual("E", imp.Data.Nodes.Single(n => n.Title == "E").PhaseCode);
            Assert.AreEqual("CNC_MILLING", imp.Data.Nodes.Single(n => n.Title == "C").ResourceClass);
            Assert.AreEqual(PbWbsNodeKind.Milestone, imp.Data.Nodes.Single(n => n.Title == "Done").Kind);
            Assert.AreEqual(wheel.Id, imp.Data.Nodes.Single(n => n.Title == "E").ParentId, "the outline becomes the tree");

            int id = imp.Data.Id;
            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().ApproveTemplateAsync(id, Ct))).Success, "the importer approves nothing");
            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().ApproveTemplateAsync(id, Ct))).Success, "engineering signs first");
            Assert.IsTrue((await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApproveTemplateAsync(id, Ct))).Success);
            Assert.IsFalse((await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApproveTemplateAsync(id, Ct))).Success, "not twice by the same person");
            var done = await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().ApproveTemplateAsync(id, Ct));
            Assert.IsTrue(done.Success, done.Message);
            Assert.IsTrue(done.Data.IsApproved);
        }

        [TestMethod]
        public async Task MspdiImport_BadFile_IsRefused_WithAMessage()
        {
            AppHost host = NewHost("tplbad");
            var product = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().SaveProductAsync(new SavePbProductDto { Code = "99.11.00.000.2", Title = "Test product" }, Ct));
            var imp = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().ImportMspdiAsync(new PbImportMspdiDto { ProductId = product.Data.Id, Xml = "not xml" }, Ct));
            Assert.IsFalse(imp.Success);
            Assert.AreEqual(0, host.Read(u => u.Set<PbWbsTemplate>().Count()));
        }

        // ------------------------------------------------------------------------------------------ OPC routings

        [TestMethod]
        public async Task OpcRouting_ThreePeople_StageItemsAndPublishToMes()
        {
            AppHost host = NewHost("opc");
            var dto = new SavePbOpcDto
            {
                PartNumber = "99.11.01.001.2", Title = "Wheel case",
                Steps = new List<SavePbOpcStepDto>
                {
                    new SavePbOpcStepDto { Seq = 1, StageCode2 = "CA", PartName = "Wheel case", StandardHours = 1.5m },
                    new SavePbOpcStepDto { Seq = 2, StageCode2 = "QC", RequiredEvidence = "DIM,UT" },
                    new SavePbOpcStepDto { Seq = 3, StageCode2 = "RG", ResourceClass = "CNC_MILLING", StandardHours = 2.5m },
                },
            };
            var saved = await Run(host, Eng1, p => p.GetRequiredService<IPbRoutingService>().SaveAsync(dto, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue(saved.Data.Steps.Single(s => s.StageCode2 == "QC").IsQcPoint);
            var bad = new SavePbOpcDto { PartNumber = "99.11.01.009.2", Steps = new List<SavePbOpcStepDto> { new SavePbOpcStepDto { Seq = 1, StageCode2 = "QC", RequiredEvidence = "XRAY" } } };
            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbRoutingService>().SaveAsync(bad, Ct))).Success, "unknown evidence kind");

            var sub = await Run(host, Eng1, p => p.GetRequiredService<IPbRoutingService>().SubmitAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsTrue(sub.Success, sub.Message);
            Assert.AreEqual("99.11.01.001.2-CA", sub.Data.Steps.Single(s => s.StageCode2 == "CA").StageItemCode, "a stockable stage gets the stage item");
            Assert.IsNull(sub.Data.Steps.Single(s => s.StageCode2 == "QC").StageItemId, "a quality step holds no stock");

            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbRoutingService>().CheckAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct))).Success, "the preparer does not check");
            Assert.IsTrue((await Run(host, Eng2, p => p.GetRequiredService<IPbRoutingService>().CheckAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct))).Success);
            Assert.IsFalse((await Run(host, Eng2, p => p.GetRequiredService<IPbRoutingService>().ApproveAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct))).Success, "the checker does not approve");
            var approved = await Run(host, Pmo, p => p.GetRequiredService<IPbRoutingService>().ApproveAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(PbRoutingStatus.Approved, approved.Data.Status);

            var edit = new SavePbOpcDto { Id = saved.Data.Id, PartNumber = dto.PartNumber, Steps = dto.Steps };
            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbRoutingService>().SaveAsync(edit, Ct))).Success, "an approved revision is read-only");

            var published = await Run(host, Pmo, p => p.GetRequiredService<IPbRoutingService>().PublishToMesAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsTrue(published.Success, published.Message);
            Assert.AreEqual(2, published.Data.OperationsWritten, "casting and rough machining; the quality step is a hold point");
            Assert.IsTrue(host.Read(u => u.Set<OperationTemplate>().Any(o => o.Description != null && o.Description.StartsWith(PbRoutingService.OpcTag))), "the MES routing is generated, not a parallel engine");
        }

        // ------------------------------------------------------------------------------------------ the FY seed

        [TestMethod]
        public async Task FySeed_InstallsTheRealData_AndIsIdempotent()
        {
            AppHost host = NewHost("seed");
            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbSeedService>().InstallFyAsync(Ct))).Success, "only the PMO installs it");
            Assert.IsFalse((await Run(host, Pmo, p => p.GetRequiredService<IPbSeedService>().InstallFyAsync(Ct))).Success, "demo data is refused while the DemoData setting is off");
            Assert.AreEqual(0, host.Read(u => u.Set<PbProduct>().Count()));
            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbCodingService>().SaveSettingAsync(new SavePbSettingDto { Key = PbSettingKeys.DemoData, Value = "true" }, Ct))).Success, "only the PMO turns demo data on");
            Assert.IsTrue((await Run(host, Pmo, p => p.GetRequiredService<IPbCodingService>().SaveSettingAsync(new SavePbSettingDto { Key = PbSettingKeys.DemoData, Value = "true" }, Ct))).Success);
            var first = await Run(host, Pmo, p => p.GetRequiredService<IPbSeedService>().InstallFyAsync(Ct));
            Assert.IsTrue(first.Success, first.Message);
            Assert.IsFalse(first.Data.AlreadyInstalled);
            Assert.AreEqual(7, first.Data.Routings, "the seven OPC charts");
            Assert.IsTrue(first.Data.BomLines >= 85, "the BOM rows of the document");
            Assert.IsTrue(first.Data.TemplateNodes >= 300);
            Assert.IsTrue(first.Data.Mappings >= 1, "mapping drafts are suggested from the item lists");

            PbBomHeader bom = host.Read(u => u.Set<PbBomHeader>().Single());
            Assert.AreEqual(PbBomStatus.Released, bom.Status);
            Assert.AreEqual("16.11.00.000.2-BOM", bom.Code);
            Assert.AreEqual(0, bom.Revision);
            Assert.IsTrue(bom.ImportedSigned);
            Assert.IsNull(bom.CheckedByUserId, "the paper signatures are roles, not people");
            Assert.IsTrue(host.Read(u => u.Set<PbOpcRouting>().All(r => r.Status == PbRoutingStatus.Approved)));
            Assert.IsFalse(host.Read(u => u.Set<PbWbsTemplate>().Single().IsApproved), "the template was never signed: it stays a Draft");
            Assert.IsTrue(host.Read(u => u.Set<PbOpcStep>().Where(s => s.StageCode2 == "CA" || s.StageCode2 == "RG").All(s => s.StandardHours.HasValue && s.StandardHours > 0)), "the shop steps have a flagged standard time");
            int tplId = host.Read(u => u.Set<PbWbsTemplate>().Single().Id);
            Assert.IsTrue((await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApproveTemplateAsync(tplId, Ct))).Success);
            Assert.IsTrue((await Run(host, Plan2, p => p.GetRequiredService<IPbProductService>().ApproveTemplateAsync(tplId, Ct))).Data.IsApproved);
            Assert.IsTrue(host.Read(u => u.Set<PbLegacyMapping>().All(m => m.Status == PbMappingStatus.Draft)), "nothing is approved by the seed");

            int items = host.Read(u => u.Set<InventoryItem>().Count());
            var second = await Run(host, Pmo, p => p.GetRequiredService<IPbSeedService>().InstallFyAsync(Ct));
            Assert.IsTrue(second.Data.AlreadyInstalled);
            Assert.AreEqual(items, host.Read(u => u.Set<InventoryItem>().Count()));
        }

        // ------------------------------------------------------------------------------------------ the project copy and deviations

        private static (int ContractId, int WorkOrderId) NewWorkOrder(AppHost host, int count)
        {
            int contract = 0, wo = 0;
            host.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current });
                u.SaveChanges();
                var w = AppHost.Add(u, new WorkOrder { Code = "W1", Title = "turbine", Count = count, ContractId = c.Id, EquipmentId = 1 });
                u.SaveChanges();
                contract = c.Id; wo = w.Id;
            }, Pmo);
            return (contract, wo);
        }

        private static async Task<(int ProductId, PbBomDto Bom)> NewProductWithBomAsync(AppHost host)
        {
            var product = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().SaveProductAsync(new SavePbProductDto { Code = "99.11.00.000.2", Title = "Test product" }, Ct));
            PbBomDto bom = await ReleaseAsync(host, Bom(Line(1, "99.11.01.001.2", "Wheel case", 1), Line(2, "99.11.01.002.2", "Casing cover", 2), Line(3, "70.04.06.052.1", "Hex screw", 8, PbMakeOrBuy.Purchase)));
            return (product.Data.Id, bom);
        }

        [TestMethod]
        public async Task Instantiate_CopiesTheReleasedRevision_AndRecordsTheSource()
        {
            AppHost host = NewHost("copy");
            var (productId, bom) = await NewProductWithBomAsync(host);
            var (_, wo) = NewWorkOrder(host, 3);
            Assert.IsFalse((await Run(host, Sales, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId }, Ct))).Success, "role");

            var res = await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId }, Ct));
            Assert.IsTrue(res.Success, res.Message);
            Assert.AreEqual(bom.Code, res.Data.SourceBomCode);
            Assert.AreEqual(0, res.Data.SourceBomRevision);
            Assert.AreEqual(bom.Id, res.Data.BomHeaderId);
            Assert.AreEqual(3, res.Data.Count);
            var screw = res.Data.Lines.Single(l => l.PartNumber == "70.04.06.052.1");
            Assert.AreEqual(8m, screw.Qty);
            Assert.AreEqual(24m, screw.TotalQty, "the quantity for the whole work order");
            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId }, Ct))).Success, "once per work order");

            // a later revision does not change the project's copy
            var rev = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().NewRevisionAsync(new PbBomActionDto { Id = bom.Id, Note = "x" }, Ct));
            Assert.IsTrue(rev.Success);
            var again = await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().GetBaselineAsync(wo, Ct));
            Assert.AreEqual(3, again.Data.Lines.Count);
            Assert.AreEqual(0, again.Data.SourceBomRevision);
        }

        [TestMethod]
        public async Task Instantiate_WithoutAReleasedBom_IsRefused()
        {
            AppHost host = NewHost("copynobom");
            var product = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().SaveProductAsync(new SavePbProductDto { Code = "99.11.00.000.2", Title = "Test product" }, Ct));
            await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(Bom(Line(1, "99.11.01.001.2", "Wheel case", 1)), Ct));
            var (_, wo) = NewWorkOrder(host, 1);
            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = product.Data.Id }, Ct))).Success, "a draft BOM is not a bank product");
        }

        [TestMethod]
        public async Task Deviation_NeedsEngineeringAndPlanning_NeitherTheRequester_ThenChangesOnlyTheProjectCopy()
        {
            AppHost host = NewHost("dev");
            var (productId, bom) = await NewProductWithBomAsync(host);
            var (_, wo) = NewWorkOrder(host, 2);
            var baseline = (await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId }, Ct))).Data;
            var target = baseline.Lines.Single(l => l.PartNumber == "99.11.01.002.2");

            var noReason = await Run(host, Shop, p => p.GetRequiredService<IPbProductService>().SaveChangeAsync(new SavePbChangeDto { BaselineId = baseline.Id, Kind = PbChangeKind.Quantity, TargetLineId = target.Id, NewQty = 5 }, Ct));
            Assert.IsFalse(noReason.Success, "a reason is required");
            var saved = await Run(host, Shop, p => p.GetRequiredService<IPbProductService>().SaveChangeAsync(new SavePbChangeDto { BaselineId = baseline.Id, Kind = PbChangeKind.Quantity, TargetLineId = target.Id, NewQty = 5, Reason = "نیاز پروژه" }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            int id = saved.Data.Id;
            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().SubmitChangeAsync(new PbChangeActionDto { Id = id }, Ct))).Success, "only the requester sends it");
            Assert.IsTrue((await Run(host, Shop, p => p.GetRequiredService<IPbProductService>().SubmitChangeAsync(new PbChangeActionDto { Id = id }, Ct))).Success);

            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().ApproveChangeEngineeringAsync(new PbChangeActionDto { Id = id }, Ct))).Success, "planning is not engineering");
            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().ApplyChangeAsync(new PbChangeActionDto { Id = id }, Ct))).Success, "not approved yet");
            { var ae = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().ApproveChangeEngineeringAsync(new PbChangeActionDto { Id = id }, Ct)); Assert.IsTrue(ae.Success, ae.Message); }
            Assert.IsFalse((await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApproveChangeEngineeringAsync(new PbChangeActionDto { Id = id }, Ct))).Success, "engineering signed already");
            var both = await Run(host, Plan2, p => p.GetRequiredService<IPbProductService>().ApproveChangePlanningAsync(new PbChangeActionDto { Id = id }, Ct));
            Assert.IsTrue(both.Success, both.Message);
            Assert.AreEqual(PbChangeStatus.Approved, both.Data.Status);

            var applied = await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApplyChangeAsync(new PbChangeActionDto { Id = id }, Ct));
            Assert.IsTrue(applied.Success, applied.Message);
            var now = (await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().GetBaselineAsync(wo, Ct))).Data;
            Assert.AreEqual(10m, now.Lines.Single(l => l.PartNumber == "99.11.01.002.2").TotalQty, "5 x 2 work orders");
            Assert.AreEqual(2m, host.Read(u => u.Set<PbBomLine>().Single(l => l.BomHeaderId == bom.Id && l.PartNumber == "99.11.01.002.2").Qty), "the bank BOM is untouched");
        }

        [TestMethod]
        public async Task Deviation_RequesterCannotApproveWithBothRoles()
        {
            AppHost host = NewHost("devself");
            var (productId, _) = await NewProductWithBomAsync(host);
            var (_, wo) = NewWorkOrder(host, 1);
            var baseline = (await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId }, Ct))).Data;
            var both = AppHost.Principal(40, "eng.and.planning", "ENGINEERING", "PLANNING");
            var saved = await Run(host, both, p => p.GetRequiredService<IPbProductService>().SaveChangeAsync(new SavePbChangeDto { BaselineId = baseline.Id, Kind = PbChangeKind.Add, PartNumber = "70.10.01.101.1", PartName = "Eye bolt", NewQty = 1, Reason = "نیاز" }, Ct));
            await Run(host, both, p => p.GetRequiredService<IPbProductService>().SubmitChangeAsync(new PbChangeActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsFalse((await Run(host, both, p => p.GetRequiredService<IPbProductService>().ApproveChangeEngineeringAsync(new PbChangeActionDto { Id = saved.Data.Id }, Ct))).Success);
            Assert.IsFalse((await Run(host, both, p => p.GetRequiredService<IPbProductService>().ApproveChangePlanningAsync(new PbChangeActionDto { Id = saved.Data.Id }, Ct))).Success);
        }

        [TestMethod]
        public async Task Deviation_AfterThePcBaselineIsCurrent_NeedsAnApprovedChangeRequest()
        {
            AppHost host = NewHost("devpc");
            var (productId, _) = await NewProductWithBomAsync(host);
            var (contract, wo) = NewWorkOrder(host, 1);
            var baseline = (await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId }, Ct))).Data;
            var target = baseline.Lines.First(l => l.PartNumber == "99.11.01.001.2");
            var saved = await Run(host, Shop, p => p.GetRequiredService<IPbProductService>().SaveChangeAsync(new SavePbChangeDto { BaselineId = baseline.Id, Kind = PbChangeKind.Remove, TargetLineId = target.Id, Reason = "حذف" }, Ct));
            int id = saved.Data.Id;
            await Run(host, Shop, p => p.GetRequiredService<IPbProductService>().SubmitChangeAsync(new PbChangeActionDto { Id = id }, Ct));
            await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().ApproveChangeEngineeringAsync(new PbChangeActionDto { Id = id }, Ct));
            await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().ApproveChangePlanningAsync(new PbChangeActionDto { Id = id }, Ct));

            int crId = 0;
            host.Write(u =>
            {
                AppHost.Add(u, new PcContractTerms { ContractId = contract, ProjectCode = "P1", CurrentBaselineId = 1 });
                var cr = AppHost.Add(u, new PcChangeRequest { Number = "CR-1", ContractId = contract, Status = PcChangeStatus.InAnalysis, Decision = PcChangeDecision.None, RequesterUserId = 17, Description = "deviation " + saved.Data.Number });
                u.SaveChanges();
                crId = cr.Id;
            }, Pmo);

            var none = await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApplyChangeAsync(new PbChangeActionDto { Id = id }, Ct));
            Assert.IsFalse(none.Success, "the PC baseline is current: a change request is needed");
            var open = await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApplyChangeAsync(new PbChangeActionDto { Id = id, ChangeRequestId = crId }, Ct));
            Assert.IsFalse(open.Success, "the change request is not decided yet");

            host.Write(u => { var cr = u.Set<PcChangeRequest>().Single(x => x.Id == crId); cr.Status = PcChangeStatus.Decided; cr.Decision = PcChangeDecision.Approve; u.SaveChanges(); }, Pmo);
            var ok = await Run(host, Eng2, p => p.GetRequiredService<IPbProductService>().ApplyChangeAsync(new PbChangeActionDto { Id = id, ChangeRequestId = crId }, Ct));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(crId, ok.Data.ChangeRequestId);
            Assert.IsTrue((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().GetBaselineAsync(wo, Ct))).Data.Lines.Single(l => l.Id == target.Id).IsRemoved);
        }

        // ------------------------------------------------------------------------------------------ review fixes

        [TestMethod]
        public async Task Signers_PreparerAlwaysRefused_OthersMayRepeatByDefault()
        {
            AppHost host = NewHost("signdef");
            var saved = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(Bom(Line(1, "99.11.01.001.2", "Wheel case", 1)), Ct));
            await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct));
            int id = saved.Data.Id;
            Assert.IsTrue((await Run(host, Eng2, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.Checked }, Ct))).Success);
            Assert.IsTrue((await Run(host, Qc, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.QcChecked }, Ct))).Success);
            Assert.IsTrue((await Run(host, Shop, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.ManufacturingChecked }, Ct))).Success);
            Assert.IsTrue((await Run(host, Eng2, p => p.GetRequiredService<IPbBomService>().SignAsync(new PbSignDto { Id = id, Step = PbBomSignature.Approved }, Ct))).Success, "with the setting off the same person may sign two blocks");
            var prep = AppHost.Principal(11, "engineer.one", "ENGINEERING", "PMO");
            Assert.IsFalse((await Run(host, prep, p => p.GetRequiredService<IPbBomService>().ReleaseAsync(new PbBomActionDto { Id = id }, Ct))).Success, "the preparer never releases, whatever the setting");
        }

        [TestMethod]
        public async Task PartNumberShape_IsEnforcedEverywhere()
        {
            AppHost host = NewHost("shape");
            foreach (string bad in new[] { "bad code!", "99.11.01.001.2-RW", "", new string('9', 41) })
            {
                var r = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = bad, PartTitle = "x" }, Ct));
                Assert.IsFalse(r.Success, "refused: " + bad);
            }
            var line = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(Bom(Line(1, "bad code!", "x", 1)), Ct));
            Assert.IsFalse(line.Success, "a BOM line with a bad part number");
            var self = await Run(host, Eng1, p => p.GetRequiredService<IPbBomService>().SaveAsync(Bom(Line(1, "99.11.00.000.2", "itself", 1)), Ct));
            Assert.IsFalse(self.Success, "an assembly cannot contain itself");
        }

        [TestMethod]
        public async Task ExistingItem_WithCodeOrMescCodeEqualToThePartNumber_IsAdopted_AndTheBackfillRunsOnce()
        {
            AppHost host = NewHost("adopt");
            host.Write(u =>
            {
                AppHost.Add(u, new InventoryItem { Code = "99.11.07.003.2", Title = "old", GroupCode = "02", Unit = "عدد", IsActive = true });
                AppHost.Add(u, new InventoryItem { Code = "X-OLD-1", MescCode = "99.11.07.001.2", Title = "old one", GroupCode = "02", Unit = "عدد", IsActive = true });
                AppHost.Add(u, new InventoryItem { Code = "X-OLD-2", MescCode = "99.11.07.002.2", Title = "old two", GroupCode = "02", Unit = "عدد", IsActive = true });
                u.SaveChanges();
            }, Pmo);
            var created = await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.07.003.2", PartTitle = "x" }, Ct));
            Assert.IsTrue(created.Success, created.Message);
            Assert.AreEqual(1, host.Read(u => u.Set<InventoryItem>().Count(i => i.Code == "99.11.07.003.2")), "no duplicate item");
            Assert.AreEqual("99.11.07.003.2", host.Read(u => u.Set<InventoryItem>().Single(i => i.Code == "99.11.07.003.2").PartNumber));

            Assert.IsFalse((await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().AdoptPartNumbersAsync(Ct))).Success, "the PMO runs the backfill");
            var first = await Run(host, Pmo, p => p.GetRequiredService<IPbCodingService>().AdoptPartNumbersAsync(Ct));
            Assert.IsTrue(first.Success, first.Message);
            Assert.AreEqual(2, first.Data.Adopted);
            Assert.AreEqual("99.11.07.001.2", host.Read(u => u.Set<InventoryItem>().Single(i => i.Code == "X-OLD-1").PartNumber));
            var second = await Run(host, Pmo, p => p.GetRequiredService<IPbCodingService>().AdoptPartNumbersAsync(Ct));
            Assert.IsTrue(second.Data.AlreadyDone);
            Assert.AreEqual(0, second.Data.Adopted);
        }

        [TestMethod]
        public async Task Mapping_WithTwoLegacyCodesForOnePart_KeepsBothApart()
        {
            AppHost host = NewHost("map2");
            await Run(host, Eng1, p => p.GetRequiredService<IPbCodingService>().CreateItemAsync(new PbCreateItemDto { PartNumber = "99.11.01.001.2", PartTitle = "Wheel case" }, Ct));
            var import = await Run(host, Plan1, p => p.GetRequiredService<IPbCodingService>().ImportMappingsAsync(new List<PbImportMappingRowDto>
            {
                new PbImportMappingRowDto { LegacyCode = "02101001000010", PartNumber = "99.11.01.001.2", StageCode2 = "RW" },
                new PbImportMappingRowDto { LegacyCode = "02101001000011", PartNumber = "99.11.01.001.2", StageCode2 = "RW" },
            }, Ct));
            Assert.AreEqual(2, import.Data.Created);
            foreach (int id in host.Read(u => u.Set<PbLegacyMapping>().Select(m => m.Id).ToList()))
                Assert.IsTrue((await Run(host, Eng2, p => p.GetRequiredService<IPbCodingService>().DecideMappingAsync(new PbDecideDto { Id = id, Approve = true }, Ct))).Success);
            string legacy = host.Read(u => u.Set<InventoryItem>().Single(i => i.PartNumber == "99.11.01.001.2" && i.StageCode2 == "RW").LegacyCode);
            Assert.IsFalse(legacy.Contains(","), "the codes are never joined into one field: " + legacy);
            Assert.AreEqual(2, host.Read(u => u.Set<PbLegacyMapping>().Count(m => m.Status == PbMappingStatus.Approved)), "both stay in the mapping table");
        }

        [TestMethod]
        public async Task Alternative_IsNotCountedTwice_InTheProjectCopy()
        {
            AppHost host = NewHost("alt");
            var product = await Run(host, Eng1, p => p.GetRequiredService<IPbProductService>().SaveProductAsync(new SavePbProductDto { Code = "99.11.00.000.2", Title = "Test product" }, Ct));
            var alt = Line(1, "99.11.01.011.2", "Wheel CCW", 1); alt.AlternativeOfIndex = 0;
            await ReleaseAsync(host, Bom(Line(1, "99.11.01.001.2", "Wheel CW", 1), alt, Line(2, "99.11.01.002.2", "Casing cover", 2)));
            var (_, wo) = NewWorkOrder(host, 1);
            var res = await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = product.Data.Id }, Ct));
            Assert.IsTrue(res.Success, res.Message);
            Assert.AreEqual(2, res.Data.Lines.Count, "the alternative is one line with the main part, not a second quantity");
        }

        [TestMethod]
        public async Task LockedPlan_InstantiationNeedsAnApprovedChangeRequestThatNamesTheWorkOrder_UsedOnce()
        {
            AppHost host = NewHost("locked");
            var (productId, _) = await NewProductWithBomAsync(host);
            var (contract, wo) = NewWorkOrder(host, 1);
            int wo2 = 0, crOther = 0, cr = 0;
            host.Write(u =>
            {
                AppHost.Add(u, new PcContractTerms { ContractId = contract, ProjectCode = "P1", CurrentBaselineId = 1 });
                var w2 = AppHost.Add(u, new WorkOrder { Code = "W2", Title = "second", Count = 1, ContractId = contract, EquipmentId = 1 });
                var other = AppHost.Add(u, new PcChangeRequest { Number = "CR-0", ContractId = contract, Status = PcChangeStatus.Decided, Decision = PcChangeDecision.Approve, RequesterUserId = 17, Description = "something else" });
                var good = AppHost.Add(u, new PcChangeRequest { Number = "CR-1", ContractId = contract, Status = PcChangeStatus.Decided, Decision = PcChangeDecision.Approve, RequesterUserId = 17, Description = "add the product to W1 and W2" });
                u.SaveChanges();
                wo2 = w2.Id; crOther = other.Id; cr = good.Id;
            }, Pmo);
            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId }, Ct))).Success, "locked plan: a change request is needed");
            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId, ChangeRequestId = crOther }, Ct))).Success, "it must name the work order");
            var ok = await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo, ProductId = productId, ChangeRequestId = cr }, Ct));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(cr, ok.Data.ChangeRequestId);
            Assert.IsFalse((await Run(host, Plan1, p => p.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = wo2, ProductId = productId, ChangeRequestId = cr }, Ct))).Success, "one request is consumed by one instantiation");
        }

        [TestMethod]
        public async Task Publish_IsRefusedWithoutStandardHours_AndWritesNothing()
        {
            AppHost host = NewHost("nohours");
            var dto = new SavePbOpcDto { PartNumber = "99.11.01.001.2", Title = "Wheel case", Steps = new List<SavePbOpcStepDto> { new SavePbOpcStepDto { Seq = 1, StageCode2 = "CA", PartName = "Wheel case" } } };
            var saved = await Run(host, Eng1, p => p.GetRequiredService<IPbRoutingService>().SaveAsync(dto, Ct));
            await Run(host, Eng1, p => p.GetRequiredService<IPbRoutingService>().SubmitAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct));
            await Run(host, Eng2, p => p.GetRequiredService<IPbRoutingService>().CheckAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsTrue((await Run(host, Pmo, p => p.GetRequiredService<IPbRoutingService>().ApproveAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct))).Success);
            var pub = await Run(host, Pmo, p => p.GetRequiredService<IPbRoutingService>().PublishToMesAsync(new PbOpcActionDto { Id = saved.Data.Id }, Ct));
            Assert.IsFalse(pub.Success);
            Assert.AreEqual(0, host.Read(u => u.Set<OperationTemplate>().Count()));
        }

        private static string OneTask(string duration, string link = "") =>
            "<Project xmlns=\"http://schemas.microsoft.com/project\"><MinutesPerDay>480</MinutesPerDay><Tasks>"
            + "<Task><UID>1</UID><ID>1</ID><Name>A</Name><OutlineLevel>1</OutlineLevel><Duration>" + duration + "</Duration></Task>"
            + "<Task><UID>2</UID><ID>2</ID><Name>B</Name><OutlineLevel>1</OutlineLevel><Duration>PT8H0M0S</Duration>" + link + "</Task></Tasks></Project>";

        [TestMethod]
        public void MspdiParser_MissingTypeIsFinishToStart_BadDurationIsAnError_PercentLagIsReported()
        {
            var noType = PbMspdi.ParseWithWarnings(OneTask("PT8H0M0S", "<PredecessorLink><PredecessorUID>1</PredecessorUID></PredecessorLink>"));
            Assert.IsNull(noType.Error);
            Assert.AreEqual(PbLinkType.FinishToStart, noType.Tasks.Single(t => t.Uid == 2).Predecessors[0].Type);
            Assert.IsNotNull(PbMspdi.ParseWithWarnings(OneTask("three days")).Error, "text that is not a duration is an error, not zero");
            var percent = PbMspdi.ParseWithWarnings(OneTask("PT8H0M0S", "<PredecessorLink><PredecessorUID>1</PredecessorUID><Type>1</Type><LinkLag>50</LinkLag><LagFormat>19</LagFormat></PredecessorLink>"));
            Assert.IsNull(percent.Error);
            Assert.AreEqual(0m, percent.Tasks.Single(t => t.Uid == 2).Predecessors[0].LagDays);
            Assert.IsTrue(percent.Warnings.Count >= 1, "a percent lag is reported");
        }

        [TestMethod]
        public void NewEntities_AreInTheAuditTrail_AndTheModelHasTheIndexes()
        {
            AppHost host = NewHost("model");
            var audited = (Dictionary<Type, string[]>)typeof(ApplicationDbContext).Assembly.GetType("DataLayer.Context.FinanceAuditTrail")
                .GetField("Audited", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            foreach (Type type in new[] { typeof(PbBomHeader), typeof(PbBomLine), typeof(PbOpcRouting), typeof(PbLegacyMapping), typeof(PbProjectBomChange), typeof(PbStage), typeof(PbWbsTemplate), typeof(PbProjectBaseline) })
                Assert.IsTrue(audited.ContainsKey(type), type.Name + " is audited");
            var item = host.Model.FindEntityType(typeof(InventoryItem));
            Assert.IsTrue(item.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "PartNumber", "StageCode2" })), "one item per part number and stage");
            var bomIndex = host.Model.FindEntityType(typeof(PbBomHeader)).GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Code", "Revision" }));
            Assert.IsTrue(bomIndex);
        }
    }
}
