using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.QualityDtos;

namespace MsTests
{
    /// <summary>Package B: characteristic lists, the QC pick, «بدون نقشه», first article, as-built record, and the registrations (audit chain, safety profile).</summary>
    [TestClass]
    public class PackageBInspectionReleaseTests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Dcc = AppHost.Principal(31, "dcc.one", "DCC");
        private static readonly ClaimsPrincipal Reviewer = AppHost.Principal(32, "engineer.one", "ENGINEER");
        private static readonly ClaimsPrincipal Approver = AppHost.Principal(33, "engineering.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(34, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Maker = AppHost.Principal(41, "eng.maker", "ENGINEER");
        private static readonly ClaimsPrincipal Checker = AppHost.Principal(42, "qa.checker", "QA");
        private static readonly ClaimsPrincipal Inspector = AppHost.Principal(43, "qc.inspector", "QC");
        private static readonly ClaimsPrincipal Operator = AppHost.Principal(35, "operator.one", "OPERATOR");

        private static AppHost NewHost(string name)
        {
            var host = new AppHost("PKB_" + name + "_" + Guid.NewGuid().ToString("N"));
            host.Write(u =>
            {
                foreach (int id in new[] { 31, 32, 33, 34, 35, 41, 42, 43 }) AppHost.Add(u, new User { Id = id, UserName = "pkb.user" + id, Email = "pkb.user" + id + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
            return host;
        }

        private static CharLine L(int id, string no, string name, decimal nominal, decimal plus, decimal minus, CharClass cls = CharClass.Critical, int sample = 100, EquipmentType inst = EquipmentType.Other) =>
            new CharLine { Id = id, No = no, Name = name, Nominal = nominal, TolPlus = plus, TolMinus = minus, Class = cls, SamplePercent = sample, InstrumentClass = inst };

        // ------------------------------------------------------------------------------------------------ pure rules

        [TestMethod]
        public void PassIsComputedFromTheListToleranceAndTheEdgesCount()
        {
            Assert.IsTrue(CharacteristicRules.Pass(50m, 0.1m, 0.2m, 49.8m));
            Assert.IsTrue(CharacteristicRules.Pass(50m, 0.1m, 0.2m, 50.1m));
            Assert.IsFalse(CharacteristicRules.Pass(50m, 0.1m, 0.2m, 50.11m));
            Assert.IsFalse(CharacteristicRules.Pass(50m, 0.1m, 0.2m, 49.79m));
        }

        [TestMethod]
        public void ACharacteristicIsFoundByIdThenByDrawingNumberThenByUniqueName()
        {
            var lines = new List<CharLine> { L(1, "1", "Diameter", 50, 0.1m, 0.1m), L(2, "2", "Length", 100, 0.5m, 0.5m), L(3, "3", "Length", 120, 0.5m, 0.5m) };
            Assert.AreEqual(2, CharacteristicRules.Find(lines, 2, null).Id);
            Assert.IsNull(CharacteristicRules.Find(lines, 99, "Diameter"), "an unknown id is not rescued by the name");
            Assert.AreEqual(1, CharacteristicRules.Find(lines, null, "1").Id);
            Assert.AreEqual(1, CharacteristicRules.Find(lines, null, "diameter").Id);
            Assert.IsNull(CharacteristicRules.Find(lines, null, "Length"), "an ambiguous name is refused");
        }

        [TestMethod]
        public void TheInstrumentClassOfTheListIsChecked()
        {
            CharLine l = L(1, "1", "Diameter", 50, 0.1m, 0.1m, inst: EquipmentType.Other);
            Assert.IsNull(CharacteristicRules.InstrumentProblem(l, EquipmentType.Other, "X1"), "Other in the list means any class");
            CharLine strict = L(2, "2", "Bore", 20, 0.01m, 0.01m, inst: (EquipmentType)Enum.GetValues(typeof(EquipmentType)).Cast<EquipmentType>().First(x => x != EquipmentType.Other));
            Assert.IsNotNull(CharacteristicRules.InstrumentProblem(strict, EquipmentType.Other, "X1"));
            Assert.IsNull(CharacteristicRules.InstrumentProblem(strict, strict.InstrumentClass, "X2"));
        }

        [TestMethod]
        public void CriticalAndMajorAreAlwaysRequiredMinorOnlyWhenSampled()
        {
            Assert.IsTrue(CharacteristicRules.IsRequired(CharClass.Critical, 0));
            Assert.IsTrue(CharacteristicRules.IsRequired(CharClass.Major, 0));
            Assert.IsFalse(CharacteristicRules.IsRequired(CharClass.Minor, 0));
            Assert.IsTrue(CharacteristicRules.IsRequired(CharClass.Minor, 10));
        }

        [TestMethod]
        public void AnAcceptanceMissesTheRequiredCharacteristicsAndAFirstArticleNeedsEveryLine()
        {
            var lines = new List<CharLine> { L(1, "1", "Diameter", 50, 0.1m, 0.1m), L(2, "2", "Finish", 1, 0.1m, 0.1m, CharClass.Minor, 0) };
            var none = new List<CharValue>();
            Assert.AreEqual(1, CharacteristicRules.Missing(lines, none, 1m, false).Count);
            Assert.AreEqual(2, CharacteristicRules.Missing(lines, none, 1m, true).Count);
            var one = new List<CharValue> { new CharValue { CharacteristicId = 1, Measured = 50 } };
            Assert.AreEqual(0, CharacteristicRules.Missing(lines, one, 1m, false).Count);
            Assert.AreEqual(1, CharacteristicRules.Missing(lines, one, 1m, true).Count);
            var bad = new List<CharValue> { new CharValue { CharacteristicId = 1, Measured = 51 } };
            Assert.AreEqual(1, CharacteristicRules.Failed(lines, bad).Count);
        }

        [TestMethod]
        public void TheListHashChangesWithAnyValueAndNotWithTheOrderOfIdentity()
        {
            var a = new List<CharLine> { L(1, "1", "Diameter", 50, 0.1m, 0.1m) };
            var b = new List<CharLine> { L(9, "1", "Diameter", 50, 0.1m, 0.1m) };
            Assert.AreEqual(CharacteristicRules.Hash(a), CharacteristicRules.Hash(b), "database ids are not part of the content");
            var c = new List<CharLine> { L(1, "1", "Diameter", 50, 0.2m, 0.1m) };
            Assert.AreNotEqual(CharacteristicRules.Hash(a), CharacteristicRules.Hash(c));
        }

        [TestMethod]
        public void AFirstArticleCoversTheConfigurationAndTheThreePeopleAreDifferent()
        {
            Assert.AreEqual("d:3,5|r:7|b:9", FaiRules.Fingerprint(new[] { 5, 3, 5 }, 7, 9));
            Assert.AreNotEqual(FaiRules.Fingerprint(new[] { 3 }, 7, 9), FaiRules.Fingerprint(new[] { 4 }, 7, 9), "a new drawing revision is a new configuration");
            Assert.IsNotNull(FaiRules.SeparationProblem(1, null, 1, true));
            Assert.IsNull(FaiRules.SeparationProblem(1, null, 2, true));
            Assert.IsNotNull(FaiRules.SeparationProblem(1, 2, 1, false));
            Assert.IsNotNull(FaiRules.SeparationProblem(1, 2, 2, false));
            Assert.IsNull(FaiRules.SeparationProblem(1, 2, 3, false));
        }

        // ------------------------------------------------------------------------------------------------ fixtures

        private static int NewContract(AppHost host)
        {
            int id = 0;
            host.Write(u => { var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current }); u.SaveChanges(); id = c.Id; }, Seeder);
            return id;
        }

        private static int NewItem(AppHost host, string part)
        {
            int id = 0;
            host.Write(u => { var i = AppHost.Add(u, new InventoryItem { Code = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "part", PartNumber = part }); u.SaveChanges(); id = i.Id; }, Seeder);
            return id;
        }

        private static async Task<(int Deliverable, int Revision)> ReleasedDrawing(AppHost host, int contract, string part)
        {
            int doc = 0;
            host.Write(u => { var d = AppHost.Add(u, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "drawing", Discipline = "Mechanical", DocumentType = "Drawing" }); u.SaveChanges(); doc = d.Id; }, Seeder);
            var link = await host.As(Dcc, p => p.GetRequiredService<IEdmsService>().SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = part }, default));
            Assert.IsTrue(link.Success, link.Message);
            var rev = await host.As(Dcc, p => p.GetRequiredService<IEdmsService>().CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = doc, Reason = "first issue" }, default));
            Assert.IsTrue(rev.Success, rev.Message);
            Guid fid = Guid.Empty;
            host.Write(u => { var d = new Entities.Model.Document("files/" + Guid.NewGuid().ToString("N") + ".pdf", "application/pdf"); u.Set<Entities.Model.Document>().Add(d); u.SaveChanges(); fid = d.Id; }, Seeder);
            var f = await host.As(Dcc, p => p.GetRequiredService<IEdmsService>().AddFileAsync(new PcRevisionFileAddDto { RevisionId = rev.Data.Id, DocumentId = fid, FileName = "a.pdf", Role = PcRevisionFileRole.Pdf }, default));
            Assert.IsTrue(f.Success, f.Message);
            return (doc, rev.Data.Id);
        }

        private static async Task ReleaseRevision(AppHost host, int revisionId)
        {
            foreach (var step in new[] { (Dcc, "submit"), (Reviewer, "review"), (Approver, "approve"), (Pmo, "release") })
            {
                var r = await host.As(step.Item1, p => p.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = revisionId, Action = step.Item2 }, default));
                Assert.IsTrue(r.Success, step.Item2 + ": " + r.Message);
            }
        }

        private static Task<ResponseDto<T>> Chars<T>(AppHost host, ClaimsPrincipal who, Func<ICharacteristicService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<ICharacteristicService>()));

        private static SaveCharListDto TwoLines(int revisionId) => new SaveCharListDto
        {
            RevisionId = revisionId,
            Items = new List<SaveCharLineDto>
            {
                new SaveCharLineDto { No = "1", Name = "Diameter", Nominal = 50m, TolPlus = 0.1m, TolMinus = 0.1m, Unit = "mm", Class = CharClass.Critical },
                new SaveCharLineDto { No = "2", Name = "Length", Nominal = 100m, TolPlus = 0.5m, TolMinus = 0.5m, Unit = "mm", Class = CharClass.Major },
            },
        };

        private static async Task<CharListDto> ApprovedList(AppHost host, int revisionId)
        {
            var saved = await Chars(host, Maker, s => s.SaveAsync(TwoLines(revisionId), default));
            Assert.IsTrue(saved.Success, saved.Message);
            var sub = await Chars(host, Maker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "submit" }, default));
            Assert.IsTrue(sub.Success, sub.Message);
            var ok = await Chars(host, Checker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "approve" }, default));
            Assert.IsTrue(ok.Success, ok.Message);
            return ok.Data;
        }

        // ------------------------------------------------------------------------------------------------ the characteristic list

        [TestMethod]
        public async Task TheMakerNeverApprovesHisOwnListAndAnAiSeatNeverApproves()
        {
            AppHost host = NewHost("list_mc");
            int contract = NewContract(host);
            NewItem(host, "PN-L1");
            var d = await ReleasedDrawing(host, contract, "PN-L1");
            var saved = await Chars(host, Maker, s => s.SaveAsync(TwoLines(d.Revision), default));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Chars(host, Maker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "submit" }, default))).Success);
            Assert.IsFalse((await Chars(host, Maker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "approve" }, default))).Success, "the creator never approves");
            var seat = new AiActingUser { UserId = 90, UserName = "seat.qa", Roles = new[] { "QA", "PMO", "ENGINEERING" } };
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "QA", sp => sp.GetRequiredService<ICharacteristicService>().ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "approve" }, default));
            Assert.IsFalse(ai.Success, "an AI seat never approves");
            Assert.AreEqual(CharListStatus.Submitted, host.Read(u => u.Set<QcCharacteristicList>().Single(x => x.Id == saved.Data.Id).Status));
            var ok = await Chars(host, Checker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "approve" }, default));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.IsFalse(string.IsNullOrEmpty(ok.Data.ContentHash));
        }

        [TestMethod]
        public async Task AnApprovedListIsReleasedWithItsDrawingAndThenNeverChanges()
        {
            AppHost host = NewHost("list_rel");
            int contract = NewContract(host);
            NewItem(host, "PN-L2");
            var d = await ReleasedDrawing(host, contract, "PN-L2");
            CharListDto approved = await ApprovedList(host, d.Revision);
            Assert.AreEqual(CharListStatus.Approved, approved.Status, "the drawing is not in force yet");
            await ReleaseRevision(host, d.Revision);
            QcCharacteristicList stored = host.Read(u => u.Set<QcCharacteristicList>().Single(x => x.Id == approved.Id));
            Assert.AreEqual(CharListStatus.Released, stored.Status, "the EDMS release releases the approved list");
            var edit = TwoLines(d.Revision); edit.Id = approved.Id;
            Assert.IsFalse((await Chars(host, Maker, s => s.SaveAsync(edit, default))).Success, "a released list is immutable");
            Assert.IsFalse((await Chars(host, Maker, s => s.ActionAsync(new CharListActionDto { Id = approved.Id, Action = "void", Note = "mistake" }, default))).Success, "a released list is not voided");
            var again = TwoLines(d.Revision);
            var noReason = await Chars(host, Maker, s => s.SaveAsync(again, default));
            Assert.IsFalse(noReason.Success, "a new version needs a reason");
            again.Reason = "tolerance corrected after review";
            var v2 = await Chars(host, Maker, s => s.SaveAsync(again, default));
            Assert.IsTrue(v2.Success, v2.Message);
            Assert.AreEqual(2, v2.Data.Version);
        }

        [TestMethod]
        public async Task ThePartGetsTheReleasedLinesAndTheDrawingNumber()
        {
            AppHost host = NewHost("list_part");
            int contract = NewContract(host);
            int item = NewItem(host, "PN-L3");
            var d = await ReleasedDrawing(host, contract, "PN-L3");
            await ApprovedList(host, d.Revision);
            await ReleaseRevision(host, d.Revision);
            var part = await Chars(host, Inspector, s => s.ForItemAsync(item, default));
            Assert.IsTrue(part.Success, part.Message);
            Assert.IsTrue(part.Data.HasDrawing);
            Assert.IsTrue(part.Data.HasReleasedList);
            Assert.AreEqual(2, part.Data.Lines.Count);
        }

        [TestMethod]
        public async Task AValueWithMoreThanFourDecimalsIsRefusedBecauseTheHashIsStampedAtFourDecimals()
        {
            AppHost host = NewHost("list_dec");
            int contract = NewContract(host);
            NewItem(host, "PN-L9");
            var d = await ReleasedDrawing(host, contract, "PN-L9");
            var dto = TwoLines(d.Revision);
            dto.Items[0].Nominal = 50.12345m;
            Assert.IsFalse((await Chars(host, Maker, s => s.SaveAsync(dto, default))).Success);
            dto.Items[0].Nominal = 50.1234m;
            Assert.IsTrue((await Chars(host, Maker, s => s.SaveAsync(dto, default))).Success);
        }

        // ------------------------------------------------------------------------------------------------ the QC pick

        [TestMethod]
        public async Task ThePickerTakesToleranceFromTheListAndRefusesAnAcceptWithAFailedOrMissingValue()
        {
            AppHost host = NewHost("pick");
            int contract = NewContract(host);
            int item = NewItem(host, "PN-P1");
            var d = await ReleasedDrawing(host, contract, "PN-P1");
            CharListDto list = await ApprovedList(host, d.Revision);
            await ReleaseRevision(host, d.Revision);
            int dia = host.Read(u => u.Set<QcCharacteristic>().Single(c => c.ListId == list.Id && c.No == "1").Id);
            int len = host.Read(u => u.Set<QcCharacteristic>().Single(c => c.ListId == list.Id && c.No == "2").Id);
            int eq = 0;
            host.Write(u => { var e = AppHost.Add(u, new MeasuringEquipment { Code = "EQ-" + Guid.NewGuid().ToString("N").Substring(0, 5), Name = "gauge", Type = EquipmentType.Other }); u.SaveChanges(); eq = e.Id; }, Seeder);

            async Task<PickOutcome> Pick(QcResult result, params (int Id, decimal V)[] values)
            {
                return await host.As(Inspector, p => CharacteristicPicker.BuildAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), item, QcRecordType.Dimensional, null, 1m, result,
                    values.Select(v => new PickInput { CharacteristicId = v.Id, Measured = v.V, EquipmentId = eq }).ToList(), default));
            }

            PickOutcome good = await Pick(QcResult.Accept, (dia, 50.05m), (len, 100m));
            Assert.IsNull(good.Error, good.Error);
            Assert.IsTrue(good.Governed);
            Assert.IsTrue(good.Measurements.All(m => m.Pass));
            Assert.AreEqual(50m, good.Measurements[0].Nominal, "the nominal comes from the list");

            PickOutcome failed = await Pick(QcResult.Accept, (dia, 50.5m), (len, 100m));
            Assert.IsNotNull(failed.Error, "an out-of-tolerance value cannot be accepted");
            Assert.IsFalse(failed.Measurements[0].Pass, "pass is computed on the server");

            PickOutcome missing = await Pick(QcResult.Accept, (dia, 50m));
            Assert.IsNotNull(missing.Error, "a required characteristic is missing");

            PickOutcome unknown = await Pick(QcResult.Accept, (999999, 1m));
            Assert.IsNotNull(unknown.Error, "a characteristic outside the list is refused");

            PickOutcome reject = await Pick(QcResult.Reject, (dia, 50.5m));
            Assert.IsNull(reject.Error, "a reject with a failed value is allowed");
        }

        [TestMethod]
        public async Task ATypedMeasurementWithoutAReleasedListIsRefused()
        {
            AppHost host = NewHost("pick_none");
            int item = NewItem(host, "PN-P2");
            PickOutcome o = await host.As(Inspector, p => CharacteristicPicker.BuildAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), item, QcRecordType.Dimensional, null, 1m, QcResult.Accept,
                new List<PickInput> { new PickInput { Name = "Diameter", Measured = 50m, EquipmentId = 1 } }, default));
            Assert.IsNotNull(o.Error);
            Assert.IsFalse(o.Governed);
        }

        // ------------------------------------------------------------------------------------------------ «بدون نقشه»

        [TestMethod]
        public async Task TheNoDrawingFlagNeedsASecondHumanAndAVoidKeepsTheRow()
        {
            AppHost host = NewHost("nodraw");
            int item = NewItem(host, "PN-N1");
            Task<ResponseDto<T>> Nd<T>(ClaimsPrincipal who, Func<INoDrawingService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<INoDrawingService>()));
            Assert.IsFalse((await Nd(Maker, s => s.RequestAsync(new RequestNoDrawingDto { ItemId = item, Reason = "short" }, default))).Success, "the reason has a minimum length");
            var req = await Nd(Maker, s => s.RequestAsync(new RequestNoDrawingDto { ItemId = item, Reason = "a standard cut piece with no drawing at all" }, default));
            Assert.IsTrue(req.Success, req.Message);
            Assert.AreEqual(NoDrawingStatus.Pending, req.Data.Status);
            Assert.IsFalse((await Nd(Maker, s => s.ActionAsync(new NoDrawingActionDto { Id = req.Data.Id, Action = "approve" }, default))).Success, "the requester never activates the flag");
            var seat = new AiActingUser { UserId = 91, UserName = "seat.eng", Roles = new[] { "ENGINEERING", "PMO", "QA" } };
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "ENG", sp => sp.GetRequiredService<INoDrawingService>().ActionAsync(new NoDrawingActionDto { Id = req.Data.Id, Action = "approve" }, default));
            Assert.IsFalse(ai.Success, "an AI seat never activates the flag");
            Assert.IsFalse((await Nd(Maker, s => s.RequestAsync(new RequestNoDrawingDto { ItemId = item, Reason = "a standard cut piece with no drawing at all" }, default))).Success, "one live flag per part");
            var on = await Nd(Checker, s => s.ActionAsync(new NoDrawingActionDto { Id = req.Data.Id, Action = "approve" }, default));
            Assert.IsTrue(on.Success, on.Message);
            Assert.AreEqual(NoDrawingStatus.Active, on.Data.Status);
            Assert.IsFalse((await Nd(Checker, s => s.ActionAsync(new NoDrawingActionDto { Id = req.Data.Id, Action = "void" }, default))).Success, "a void needs a reason");
            var voided = await Nd(Checker, s => s.ActionAsync(new NoDrawingActionDto { Id = req.Data.Id, Action = "void", Note = "the drawing arrived" }, default));
            Assert.IsTrue(voided.Success, voided.Message);
            Assert.AreEqual(1, host.Read(u => u.Set<NoDrawingFlag>().Count(f => f.ItemId == item)), "voided, never deleted");
        }

        [TestMethod]
        public async Task TheMesDrawingsCheckNeedsAReleasedDrawingOrAnActiveFlag()
        {
            AppHost host = NewHost("mes_draw");
            int contract = NewContract(host);
            int item = 0, order = 0;
            host.Write(u =>
            {
                var w = AppHost.Add(u, new WorkOrder { Code = "WO-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "wo", ContractId = contract });
                var i = AppHost.Add(u, new InventoryItem { Code = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "part", PartNumber = "PN-M1" });
                u.SaveChanges();
                var o = AppHost.Add(u, new MfProductionOrder { Number = "MO-" + Guid.NewGuid().ToString("N").Substring(0, 5), WorkOrderId = w.Id, ContractId = contract, ItemId = i.Id, Quantity = 1, Unit = "EA" });
                u.SaveChanges();
                item = i.Id; order = o.Id;
            }, Seeder);
            async Task<bool> Passed() => (await host.As(Pmo, p => p.GetRequiredService<Services.NasbNiro.Production.IMfProductionService>().ReleaseCheckAsync(order, default))).Data.Checks.Single(c => c.Code == "drawings").Passed;
            var all = await host.As(Pmo, p => p.GetRequiredService<Services.NasbNiro.Production.IMfProductionService>().ReleaseCheckAsync(order, default));
            Assert.AreEqual(5, all.Data.Checks.Count, "the check count does not change");
            Assert.IsFalse(await Passed(), "no drawing and no flag: no release");
            var req = await host.As(Maker, p => p.GetRequiredService<INoDrawingService>().RequestAsync(new RequestNoDrawingDto { ItemId = item, Reason = "a standard cut piece with no drawing at all" }, default));
            Assert.IsFalse(await Passed(), "a pending flag does not count");
            Assert.IsTrue((await host.As(Checker, p => p.GetRequiredService<INoDrawingService>().ActionAsync(new NoDrawingActionDto { Id = req.Data.Id, Action = "approve" }, default))).Success);
            Assert.IsTrue(await Passed(), "an active flag counts");
        }

        // ------------------------------------------------------------------------------------------------ registrations

        [TestMethod]
        public void TheNewRecordTypesAreInTheAuditChainWithPersianLabels()
        {
            foreach (Type type in new[] { typeof(QcCharacteristicList), typeof(QcCharacteristic), typeof(NoDrawingFlag), typeof(QcFirstArticle), typeof(AsBuiltRecord) })
            {
                string label = AuditEntityLabels.Of(type.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(label), type.Name + " has no label");
                Assert.IsTrue(label.Any(c => c >= '؀' && c <= 'ۿ'), type.Name + " label is not Persian: " + label);
            }
        }

        [TestMethod]
        public void TheInspectionSettingsAreOnTheSafetyProfileWithTheirStrictValues()
        {
            var expected = new Dictionary<string, string>
            {
                { QualitySettingKeys.PkbFaiEnforce, "1" }, { QualitySettingKeys.PkbFaiOnNewRevision, "1" }, { QualitySettingKeys.PkbPcaEnabled, "1" }, { QualitySettingKeys.PkbPcaTolerance, "0" },
                { QualitySettingKeys.PkbSampleCritical, "100" }, { QualitySettingKeys.PkbSampleMajor, "100" }, { QualitySettingKeys.PkbSampleMinor, "10" }, { QualitySettingKeys.PkbNoDrawingReasonMin, "15" },
            };
            foreach (var kv in expected)
            {
                SafetySettingSpec spec = SafetyProfile.Find(SafetyStore.Quality, kv.Key);
                Assert.IsNotNull(spec, kv.Key + " is not on the safety profile");
                Assert.AreEqual(kv.Value, spec.Strict, kv.Key);
            }
            SafetySettingSpec tolerance = SafetyProfile.Find(SafetyStore.Quality, QualitySettingKeys.PkbPcaTolerance);
            Assert.IsTrue(SafetyProfile.IsWeaker(tolerance, "5"), "a looser quantity tolerance is a weakening");
            SafetySettingSpec fai = SafetyProfile.Find(SafetyStore.Quality, QualitySettingKeys.PkbFaiEnforce);
            Assert.IsTrue(SafetyProfile.IsWeaker(fai, "0"), "switching the first article off is a weakening");
        }

        // ------------------------------------------------------------------------------------------------ fix round 1

        [TestMethod]
        public void BulkMaterialIsSampledPerLotAndCountedPartsPerPiece()
        {
            Assert.IsTrue(CharacteristicRules.IsBulkUnit("kg"));
            Assert.IsFalse(CharacteristicRules.IsBulkUnit("عدد"));
            Assert.AreEqual(1m, CharacteristicRules.InspectionUnits("kg", 2500m, 1), "one sample per heat, not 2500");
            Assert.AreEqual(3m, CharacteristicRules.InspectionUnits("kg", 2500m, 3));
            Assert.AreEqual(40m, CharacteristicRules.InspectionUnits("عدد", 40m, 1), "counted parts by pieces");
        }

        [TestMethod]
        public void AMinorLineBelowTheFloorIsRefusedAndAZeroFloorAllowsIt()
        {
            Assert.IsNotNull(CharacteristicRules.LineProblem("1", "x", 0.1m, 0.1m, CharClass.Minor, 0, 100, 25, "r ", 10));
            Assert.IsNull(CharacteristicRules.LineProblem("1", "x", 0.1m, 0.1m, CharClass.Minor, 0, 100, 25, "r ", 0));
            Assert.IsNull(CharacteristicRules.LineProblem("1", "x", 0.1m, 0.1m, CharClass.Minor, 10, 100, 25, "r ", 10));
        }

        [TestMethod]
        public void PcaJudgesCountedPartsExactlyAndBulkWithTheBomAllowance()
        {
            Assert.IsNull(PcaRules.Judge(4m, 4m, 0m, 0m, false));
            Assert.IsNotNull(PcaRules.Judge(4m, 5m, 0m, 0m, false), "one bolt too many");
            Assert.IsNotNull(PcaRules.Judge(4m, 3m, 0m, 0m, false));
            Assert.IsNotNull(PcaRules.Judge(4m, 0m, 0m, 0m, false), "nothing in the genealogy");
            Assert.IsNull(PcaRules.Judge(100m, 104m, 0m, 0.05m, true), "within the scrap allowance");
            Assert.IsNotNull(PcaRules.Judge(100m, 106m, 0m, 0.05m, true));
            Assert.IsNotNull(PcaRules.Judge(100m, 90m, 0m, 0.05m, true), "less than the BOM is another material");
        }

        [TestMethod]
        public void TheFingerprintChangesWithTheListAndTheProductBom()
        {
            string a = FaiRules.Fingerprint(new[] { 1 }, 2, 3);
            string b = FaiRules.Fingerprint(new[] { 1 }, 2, 3, new[] { 7 }, 9);
            string c = FaiRules.Fingerprint(new[] { 1 }, 2, 3, new[] { 8 }, 9);
            string d = FaiRules.Fingerprint(new[] { 1 }, 2, 3, new[] { 7 }, 10);
            Assert.AreNotEqual(a, b);
            Assert.AreNotEqual(b, c, "a new list version means a new first article");
            Assert.AreNotEqual(b, d, "a new product BOM means a new first article");
        }

        [TestMethod]
        public async Task AnIncomingItemWithoutAListIsInspectedByDecisionOnlyAndWithAListByThePickerRules()
        {
            AppHost host = NewHost("incoming");
            int contract = NewContract(host);
            int bare = NewItem(host, "PN-IN0");
            IUnitOfWork Uow(IServiceProvider p) => p.GetRequiredService<IUnitOfWork>();

            PickOutcome none = await host.As(Inspector, p => CharacteristicPicker.BuildAsync(Uow(p), bare, QcRecordType.Dimensional, null, 5m, QcResult.Accept, new List<PickInput>(), default, true));
            Assert.IsNull(none.Error, "an item with no list is accepted on certificate/visual evidence: " + none.Error);
            PickOutcome typed = await host.As(Inspector, p => CharacteristicPicker.BuildAsync(Uow(p), bare, QcRecordType.Dimensional, null, 5m, QcResult.Accept,
                new List<PickInput> { new PickInput { Name = "Diameter", Measured = 50m, EquipmentId = 1 } }, default, true));
            Assert.IsNotNull(typed.Error, "no list means no typed measurement rows");
            PickOutcome rej = await host.As(Inspector, p => CharacteristicPicker.BuildAsync(Uow(p), bare, QcRecordType.Dimensional, null, 5m, QcResult.Reject, new List<PickInput>(), default, true));
            Assert.IsNull(rej.Error, "a reject needs no list either");

            int item = NewItem(host, "PN-IN1");
            var d = await ReleasedDrawing(host, contract, "PN-IN1");
            CharListDto list = await ApprovedList(host, d.Revision);
            await ReleaseRevision(host, d.Revision);
            int dia = host.Read(u => u.Set<QcCharacteristic>().Single(c => c.ListId == list.Id && c.No == "1").Id);
            PickOutcome partial = await host.As(Inspector, p => CharacteristicPicker.BuildAsync(Uow(p), item, QcRecordType.Dimensional, null, 1m, QcResult.Accept,
                new List<PickInput> { new PickInput { CharacteristicId = dia, Measured = 50m, EquipmentId = 1 } }, default, true));
            Assert.IsNotNull(partial.Error, "with a list the required characteristics must all be measured");
        }

        [TestMethod]
        public async Task ADrawingOfAPartCannotBeReleasedWithoutAnApprovedListButAnEmptyListWithAReasonDoes()
        {
            AppHost host = NewHost("h2");
            int contract = NewContract(host);
            NewItem(host, "PN-H2");
            var d = await ReleasedDrawing(host, contract, "PN-H2");
            foreach (var step in new[] { (Dcc, "submit"), (Reviewer, "review"), (Approver, "approve") })
                Assert.IsTrue((await host.As(step.Item1, p => p.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = d.Revision, Action = step.Item2 }, default))).Success);
            var refused = await host.As(Pmo, p => p.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = d.Revision, Action = "release" }, default));
            Assert.IsFalse(refused.Success, "no approved list: release refused");

            var empty = new SaveCharListDto { RevisionId = d.Revision, Items = new List<SaveCharLineDto>() };
            empty.Reason = "قطعه فقط از روی گواهی مواد پذیرفته می‌شود";
            var saved = await Chars(host, Maker, s => s.SaveAsync(empty, default));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Chars(host, Maker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "submit" }, default))).Success);
            Assert.IsFalse((await Chars(host, Maker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "approve" }, default))).Success, "maker-checker");
            Assert.IsTrue((await Chars(host, Checker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "approve" }, default))).Success);
            var ok = await host.As(Pmo, p => p.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = d.Revision, Action = "release" }, default));
            Assert.IsTrue(ok.Success, ok.Message);
        }

        [TestMethod]
        public async Task OnlyEngineeringMakesAListAndOnlyQualityChecksIt()
        {
            AppHost host = NewHost("roles");
            int contract = NewContract(host);
            NewItem(host, "PN-RL");
            var d = await ReleasedDrawing(host, contract, "PN-RL");
            ClaimsPrincipal qa = AppHost.Principal(42, "qa.checker", "QA");
            Assert.IsFalse((await Chars(host, qa, s => s.SaveAsync(TwoLines(d.Revision), default))).Success, "QA does not make lists");
            var saved = await Chars(host, Maker, s => s.SaveAsync(TwoLines(d.Revision), default));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Chars(host, Maker, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "submit" }, default))).Success);
            Assert.IsFalse((await Chars(host, Reviewer, s => s.ActionAsync(new CharListActionDto { Id = saved.Data.Id, Action = "approve" }, default))).Success, "an engineer does not check");
        }

        [TestMethod]
        public void TheNewSettingsAreOnTheSafetyProfile()
        {
            SafetySettingSpec bulk = SafetyProfile.Find(SafetyStore.Quality, "Pkb.Incoming.BulkSamplesPerLot");
            SafetySettingSpec extra = SafetyProfile.Find(SafetyStore.Quality, "Pkb.Pca.BulkExtraTolerancePercent");
            Assert.IsNotNull(bulk); Assert.IsNotNull(extra);
            Assert.AreEqual("1", bulk.Strict); Assert.AreEqual("0", extra.Strict);
        }
    }
}
