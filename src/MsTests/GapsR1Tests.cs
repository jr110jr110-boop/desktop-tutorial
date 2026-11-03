using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.QualityDtos;

namespace MsTests
{
    /// <summary>
    /// R1-gaps: the per-caller action flags of the NCR, the ITP and the release note are computed by the predicates the actions enforce; the operation list filters by NCR and rework and
    /// says whether a machine is required; the release of a guarantee has two actions; the settings saves name their roles on the controller as well as in the service.
    /// </summary>
    [TestClass]
    public class GapsR1Tests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal QaCreator = AppHost.Principal(31, "qa.creator", "QA");
        private static readonly ClaimsPrincipal QaSecond = AppHost.Principal(32, "qa.second", "QA");
        private static readonly ClaimsPrincipal QcEditor = AppHost.Principal(33, "qc.editor", "QC");
        private static readonly ClaimsPrincipal Reader = AppHost.Principal(34, "plain.reader", "PMO");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(41, "commercial", "COMMERCIAL");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(42, "pmo", "PMO");
        private static readonly ClaimsPrincipal Accountant = AppHost.Principal(43, "accountant", "FINANCIAL");

        private static readonly Lazy<AppHost> SharedHost = new Lazy<AppHost>(() => new AppHost("Gaps_" + Guid.NewGuid().ToString("N")));
        private static AppHost H => SharedHost.Value;

        // ------------------------------------------------------------------------------------------ NCR

        private static int NewNcr(NcrStatus status, int creator, string editors = null)
        {
            int id = 0;
            H.Write(u =>
            {
                var item = AppHost.Add(u, new InventoryItem { Code = "NCR-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "item", Unit = "عدد" });
                u.SaveChanges();
                var n = AppHost.Add(u, new NonConformance
                {
                    ItemId = item.Id, Number = "NCR-G-" + Guid.NewGuid().ToString("N").Substring(0, 6), ReportDate = DateTime.Today, Status = status, Severity = NcrSeverity.Minor,
                    Disposition = NcrDisposition.Rework, CreatorUserId = creator, EditorUserIds = editors, Quantity = 1,
                });
                u.SaveChanges();
                id = n.Id;
            }, Seeder);
            return id;
        }

        private static Task<ResponseDto<NcrDto>> GetNcr(ClaimsPrincipal who, int id) =>
            H.As(who, p => p.GetRequiredService<IInventoryQualityService>().GetNcrAsync(id, CancellationToken.None));

        [TestMethod]
        public async Task An_Ncr_Read_By_Its_Creator_Cannot_Be_Signed_And_By_A_Second_Qa_User_Can()
        {
            int id = NewNcr(NcrStatus.Open, 31);
            NcrDto byCreator = (await GetNcr(QaCreator, id)).Data;
            Assert.AreEqual(31, byCreator.CreatorUserId);
            Assert.IsFalse(byCreator.CanSign);
            Assert.IsFalse(byCreator.CanSignQuality);
            NcrDto bySecond = (await GetNcr(QaSecond, id)).Data;
            Assert.IsTrue(bySecond.CanSign);
            Assert.IsTrue(bySecond.CanSignQuality);
            Assert.IsFalse(bySecond.CanSignEngineering, "a QA user does not hold the engineering role");

            // the action says the same as the flag
            ResponseDto<NcrDto> refused = await H.As(QaCreator, p => p.GetRequiredService<IInventoryQualityService>().ApproveNcrAsync(new ApproveNcrDto { Id = id, Kind = ApprovalKind.Quality }, CancellationToken.None));
            Assert.IsFalse(refused.Success);
            ResponseDto<NcrDto> signed = await H.As(QaSecond, p => p.GetRequiredService<IInventoryQualityService>().ApproveNcrAsync(new ApproveNcrDto { Id = id, Kind = ApprovalKind.Quality }, CancellationToken.None));
            Assert.IsTrue(signed.Success, signed.Message);
            NcrDto after = (await GetNcr(QaSecond, id)).Data;
            Assert.IsFalse(after.CanSignQuality, "the same user signs a part once only");
        }

        [TestMethod]
        public async Task An_Ncr_Editor_Is_A_Maker_And_The_Flags_Follow_Status_And_Role()
        {
            int id = NewNcr(NcrStatus.Open, 31, ",33,");
            NcrDto byEditor = (await GetNcr(QcEditor, id)).Data;
            CollectionAssert.AreEqual(new[] { 33 }, byEditor.EditorUserIds);
            Assert.IsFalse(byEditor.CanSign, "an editor is a maker");
            Assert.IsFalse(byEditor.CanCancel || byEditor.CanClose);
            Assert.IsTrue((await GetNcr(QaCreator, id)).Data.CanCancel, "the QA manager may cancel a report he raised");
            Assert.IsFalse((await GetNcr(Reader, id)).Data.CanSign, "a PMO user is not a signer of any part");

            int executed = NewNcr(NcrStatus.Executed, 31);
            Assert.IsFalse((await GetNcr(QaSecond, executed)).Data.CanClose, "the root cause and corrective action are not recorded");
            H.Write(u =>
            {
                NonConformance n = u.Set<NonConformance>().Single(x => x.Id == executed);
                n.CauseCategory = NcrCauseCategory.Manpower; n.RootCause = "r"; n.CorrectiveAction = "a";
                u.SaveChanges();
            }, Seeder);
            Assert.IsTrue((await GetNcr(QaSecond, executed)).Data.CanClose);
            Assert.IsFalse((await GetNcr(QaCreator, executed)).Data.CanClose, "the creator does not close his own report");
            Assert.IsFalse((await GetNcr(QaSecond, executed)).Data.CanCancel, "only an open report is cancelled");
        }

        // ------------------------------------------------------------------------------------------ ITP

        private static int NewItp(ItpStatus status, int creator, int? submittedBy, string editors)
        {
            int id = 0;
            H.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract" });
                u.SaveChanges();
                var w = AppHost.Add(u, new WorkOrder { Code = "W-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "work order", ContractId = c.Id });
                u.SaveChanges();
                var itp = AppHost.Add(u, new Itp { Number = "ITP-G", WorkOrderId = w.Id, ContractId = c.Id, Status = status, CreatorUserId = creator, SubmittedByUserId = submittedBy, EditorUserIds = editors, NoticeDays = 10 });
                u.SaveChanges();
                id = itp.Id;
            }, Seeder);
            return id;
        }

        [TestMethod]
        public async Task The_Client_Approval_Flag_Of_An_Itp_Follows_The_Maker_Checker_Rule_Of_The_Action()
        {
            int id = NewItp(ItpStatus.Submitted, 31, 31, ",31,");
            ItpDto byMaker = (await H.As(QaCreator, p => p.GetRequiredService<IItpService>().GetItpAsync(id, CancellationToken.None))).Data;
            Assert.AreEqual(31, byMaker.CreatorUserId);
            Assert.AreEqual(31, byMaker.SubmittedByUserId);
            CollectionAssert.AreEqual(new[] { 31 }, byMaker.EditorUserIds);
            Assert.IsFalse(byMaker.CanApprove);
            ItpDto bySecond = (await H.As(QaSecond, p => p.GetRequiredService<IItpService>().GetItpAsync(id, CancellationToken.None))).Data;
            Assert.IsTrue(bySecond.CanApprove);
            Assert.IsFalse(bySecond.CanSubmit, "the plan is submitted already");
            Assert.IsFalse(bySecond.CanClose);

            ResponseDto<ItpDto> refused = await H.As(QaCreator, p => p.GetRequiredService<IItpService>().ItpActionAsync(new ItpActionDto { Id = id, Action = "client-approve", Reference = "L-1" }, CancellationToken.None));
            Assert.IsFalse(refused.Success, "the action refuses what the flag says");
            ResponseDto<ItpDto> approved = await H.As(QaSecond, p => p.GetRequiredService<IItpService>().ItpActionAsync(new ItpActionDto { Id = id, Action = "client-approve", Reference = "L-1" }, CancellationToken.None));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.IsFalse(approved.Data.CanApprove, "an approved plan is not approved again");
            Assert.IsTrue(approved.Data.CanUse);
            Assert.IsTrue(approved.Data.CanRevise);
        }

        // ------------------------------------------------------------------------------------------ release note

        [TestMethod]
        public async Task A_Release_Note_Says_Who_Made_It_The_Tracking_Mode_Of_Its_Items_And_What_The_Caller_May_Do()
        {
            int noteId = 0;
            H.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-RN", Title = "contract" });
                u.SaveChanges();
                var w = AppHost.Add(u, new WorkOrder { Code = "W-RN", Title = "work order", ContractId = c.Id });
                var item = AppHost.Add(u, new InventoryItem { Code = "RN-1", Title = "serial item", Unit = "عدد", TrackingMode = ItemTrackingMode.Serial });
                u.SaveChanges();
                var note = AppHost.Add(u, new ReleaseNote { Number = "RN-G", WorkOrderId = w.Id, ContractId = c.Id, Status = ReleaseNoteStatus.Draft, CreatorUserId = 31, EditorUserIds = ",31,33," });
                u.SaveChanges();
                u.Set<ReleaseNoteLine>().Add(new ReleaseNoteLine { ReleaseNoteId = note.Id, RowNumber = 1, ItemId = item.Id, Quantity = 1, SerialNumbers = "S1" });
                u.SaveChanges();
                noteId = note.Id;
            }, Seeder);
            ReleaseNoteDto dto = (await H.As(AppHost.Principal(35, "qc.third", "QC"), p => p.GetRequiredService<IReleaseService>().GetNoteAsync(noteId, CancellationToken.None))).Data;
            Assert.AreEqual(31, dto.CreatorUserId);
            CollectionAssert.AreEqual(new[] { 31, 33 }, dto.EditorUserIds);
            Assert.AreEqual(ItemTrackingMode.Serial, dto.Lines.Single().TrackingMode);
            Assert.IsFalse(dto.CanSign, "the checks of an empty work order do not pass, so nothing can be released");
            Assert.IsTrue(dto.CanCancel);
        }

        [TestMethod]
        public void The_Release_Note_Predicates_Hold_Role_State_And_Maker()
        {
            Func<string, bool> qc = r => r.Split(',').Contains("QC");
            Func<string, bool> none = r => false;
            var draft = new ReleaseNote { Status = ReleaseNoteStatus.Draft, CreatorUserId = 5, EditorUserIds = ",6," };
            Assert.IsNull(ReleaseNoteRules.SignProblem(draft, 7, qc));
            Assert.IsNotNull(ReleaseNoteRules.SignProblem(draft, 5, qc), "the drafter");
            Assert.IsNotNull(ReleaseNoteRules.SignProblem(draft, 6, qc), "an editor");
            Assert.IsNotNull(ReleaseNoteRules.SignProblem(draft, 7, none), "the role");
            Assert.IsNotNull(ReleaseNoteRules.SignProblem(new ReleaseNote { Status = ReleaseNoteStatus.Released }, 7, qc), "the state");
            Assert.IsNotNull(ReleaseNoteRules.CancelProblem(new ReleaseNote { Status = ReleaseNoteStatus.Cancelled }, r => true));
            Assert.IsNull(ReleaseNoteRules.CancelProblem(new ReleaseNote { Status = ReleaseNoteStatus.Released }, r => true));
        }

        // ------------------------------------------------------------------------------------------ operations

        [TestMethod]
        public async Task Operations_Filter_By_Ncr_And_Rework_And_Say_Whether_A_Machine_Is_Required()
        {
            int ncrId = NewNcr(NcrStatus.Approved, 31);
            int otherNcr = NewNcr(NcrStatus.Approved, 31);
            int orderId = 0, reworkId = 0, rejectedId = 0, plainId = 0, centreWithMachine = 0, centreWithout = 0;
            H.Write(u =>
            {
                var withMachine = AppHost.Add(u, new MfWorkCenter { Code = "MF-GM", Title = "with machine", Kind = MfWorkCenterKind.Machine, IsActive = true, RequiresMachine = true });
                var without = AppHost.Add(u, new MfWorkCenter { Code = "MF-GN", Title = "without machine", Kind = MfWorkCenterKind.Machine, IsActive = true, RequiresMachine = true });
                var manual = AppHost.Add(u, new MfWorkCenter { Code = "MF-GH", Title = "manual", Kind = MfWorkCenterKind.ManualStation, IsActive = true, RequiresMachine = false });
                var order = AppHost.Add(u, new MfProductionOrder { Number = "PO-G-1", WorkOrderId = 1, ContractId = 1, Status = MfOrderStatus.InProgress, Quantity = 5 });
                u.SaveChanges();
                u.Set<MfMachineInfo>().Add(new MfMachineInfo { MachineId = 9001, WorkCenterId = withMachine.Id, Status = MfMachineStatus.Available });
                var rejected = AppHost.Add(u, new MfOperation { OrderId = order.Id, Sequence = 10, Title = "turning", WorkCenterId = withMachine.Id, Status = MfOperationStatus.Rejected, NcrId = ncrId });
                var rework = AppHost.Add(u, new MfOperation { OrderId = order.Id, Sequence = 11, Title = "rework", WorkCenterId = without.Id, Status = MfOperationStatus.Ready, IsRework = true, NcrId = ncrId });
                var otherRework = AppHost.Add(u, new MfOperation { OrderId = order.Id, Sequence = 12, Title = "rework other", WorkCenterId = manual.Id, Status = MfOperationStatus.Ready, IsRework = true, NcrId = otherNcr });
                var plain = AppHost.Add(u, new MfOperation { OrderId = order.Id, Sequence = 20, Title = "plain", WorkCenterId = withMachine.Id, Status = MfOperationStatus.Ready });
                u.SaveChanges();
                orderId = order.Id; rejectedId = rejected.Id; reworkId = rework.Id; plainId = plain.Id; centreWithMachine = withMachine.Id; centreWithout = without.Id;
            }, Seeder);

            Task<List<OperationDto>> Query(OperationQuery q) => H.As(Pmo, async p => (await p.GetRequiredService<IMfProductionService>().GetOperationsAsync(q, CancellationToken.None)).Data.ToList());

            List<OperationDto> reworkOfNcr = await Query(new OperationQuery { OrderId = orderId, NcrId = ncrId, IsRework = true });
            CollectionAssert.AreEqual(new[] { reworkId }, reworkOfNcr.Select(o => o.Id).ToArray(), "exactly the rework operations of that NCR");
            List<OperationDto> allOfNcr = await Query(new OperationQuery { OrderId = orderId, NcrId = ncrId });
            CollectionAssert.AreEquivalent(new[] { rejectedId, reworkId }, allOfNcr.Select(o => o.Id).ToArray());
            List<OperationDto> normal = await Query(new OperationQuery { OrderId = orderId, IsRework = false });
            CollectionAssert.AreEquivalent(new[] { rejectedId, plainId }, normal.Select(o => o.Id).ToArray());

            List<OperationDto> all = await Query(new OperationQuery { OrderId = orderId });
            Assert.IsTrue(all.Single(o => o.Id == plainId).MachineRequired, "the centre requires a machine and has one");
            Assert.IsFalse(all.Single(o => o.Id == reworkId).MachineRequired, "a centre without a machine has nothing to choose");
            Assert.IsFalse(all.Where(o => o.Title == "rework other").Single().MachineRequired, "the centre was told not to require one");

            // the order card lists the same operations with the same flag
            OrderDto order = (await H.As(Pmo, p => p.GetRequiredService<IMfProductionService>().GetOrderAsync(orderId, CancellationToken.None))).Data;
            Assert.IsTrue(order.Operations.Single(o => o.Id == plainId).MachineRequired);
            Assert.AreNotEqual(0, centreWithMachine + centreWithout);
        }

        // ------------------------------------------------------------------------------------------ guarantee release in two actions

        private static int _source = 19000;

        private static int NewGuarantee()
        {
            int id = 0;
            int source = Interlocked.Increment(ref _source);
            H.Write(u =>
            {
                var g = AppHost.Add(u, new PcGuaranteeInfo
                {
                    SourceKind = PcGuaranteeSource.ContractGuaranty, SourceId = source, ContractId = 1, GuaranteeType = PcGuaranteeType.Performance, IssuerKind = PcGuaranteeIssuer.Bank,
                    ReleaseTrigger = PcGuaranteeRelease.FinalDelivery, Status = PcGuaranteeStatus.Active, ExpiryDate = DateTime.Today.AddDays(60), CurrencyCode = "IRR", AmountDecimal = 1000m,
                });
                u.SaveChanges();
                id = g.Id;
            }, Seeder);
            return id;
        }

        private static Task<ResponseDto<PcGuaranteeDto>> Request(ClaimsPrincipal who, int id) =>
            H.As(who, p => p.GetRequiredService<IPcCommercialService>().RequestGuaranteeReleaseAsync(new PcGuaranteeReleaseDto { GuaranteeId = id, ReleaseDate = DateTime.Today, Note = "bank letter 12" }, CancellationToken.None));

        private static Task<ResponseDto<PcGuaranteeDto>> Confirm(ClaimsPrincipal who, int id) =>
            H.As(who, p => p.GetRequiredService<IPcCommercialService>().ConfirmGuaranteeReleaseAsync(new PcGuaranteeReleaseDto { GuaranteeId = id }, CancellationToken.None));

        private static Task<ResponseDto<PcGuaranteeDto>> Alias(ClaimsPrincipal who, int id) =>
            H.As(who, p => p.GetRequiredService<IPcCommercialService>().ReleaseGuaranteeAsync(new PcGuaranteeReleaseDto { GuaranteeId = id, ReleaseDate = DateTime.Today, Note = "bank letter 13" }, CancellationToken.None));

        [TestMethod]
        public async Task The_Release_Is_Requested_By_One_User_And_Confirmed_By_Another()
        {
            int id = NewGuarantee();
            Assert.IsFalse((await Confirm(Pmo, id)).Success, "nothing waits to be confirmed");

            ResponseDto<PcGuaranteeDto> asked = await Request(Commercial, id);
            Assert.IsTrue(asked.Success, asked.Message);
            Assert.AreNotEqual(PcGuaranteeStatus.Released, asked.Data.Status);
            Assert.IsTrue(asked.Data.ReleaseRequestOpen);
            Assert.IsFalse(asked.Data.CanRequestRelease, "a request waits");
            Assert.IsFalse(asked.Data.CanConfirmRelease, "the requester is never the confirmer");

            Assert.IsFalse((await Request(Pmo, id)).Success, "a second request does not overwrite the first");
            ResponseDto<PcGuaranteeDto> self = await Confirm(Commercial, id);
            Assert.IsFalse(self.Success);
            StringAssert.Contains(self.Message, "تفکیک وظایف");
            Assert.IsFalse((await Confirm(Accountant, id)).Success, "FINANCIAL is not a closing role");

            ResponseDto<PcGuaranteeDto> confirmed = await Confirm(Pmo, id);
            Assert.IsTrue(confirmed.Success, confirmed.Message);
            Assert.AreEqual(PcGuaranteeStatus.Released, confirmed.Data.Status);
            Assert.AreEqual(1, confirmed.Data.Events.Count(e => e.Kind == PcGuaranteeEventKind.Release));
            Assert.AreEqual("bank letter 12", confirmed.Data.Events.Single(e => e.Kind == PcGuaranteeEventKind.Release).Note, "the confirmation records the note of the request");
            Assert.IsFalse((await Request(Pmo, id)).Success, "a released guarantee is not released again");
        }

        [TestMethod]
        public async Task The_Old_Combined_Release_Call_Still_Works_As_A_Deprecated_Alias()
        {
            int id = NewGuarantee();
            Assert.IsTrue((await Alias(Commercial, id)).Data.ReleaseRequestOpen, "no request is open: the alias requests");
            Assert.IsFalse((await Alias(Commercial, id)).Success, "the same user cannot confirm");
            ResponseDto<PcGuaranteeDto> done = await Alias(Pmo, id);
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual(PcGuaranteeStatus.Released, done.Data.Status);

            // a request made with the new action is confirmed by the alias too
            int second = NewGuarantee();
            Assert.IsTrue((await Request(Commercial, second)).Success);
            Assert.AreEqual(PcGuaranteeStatus.Released, (await Alias(Pmo, second)).Data.Status);
        }

        [TestMethod]
        public void The_Release_Steps_Are_Routes_With_The_Roles_Of_The_Release()
        {
            Type controller = typeof(WebApi.Controllers.ProjectControl.ProjectControlCommercialController);
            foreach ((string action, string route) in new[] { ("RequestGuaranteeReleaseAsync", "guarantees/request-release"), ("ConfirmGuaranteeReleaseAsync", "guarantees/confirm-release") })
            {
                MethodInfo method = controller.GetMethod(action);
                Assert.IsNotNull(method, action);
                Assert.AreEqual(route, method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Template);
                CollectionAssert.AreEquivalent(new[] { "COMMERCIAL", "FINANCEMANAGER", "PMO" }, RolesOf(method));
            }
        }

        // ------------------------------------------------------------------------------------------ settings saves

        private static string[] RolesOf(MethodInfo method) =>
            method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).SelectMany(a => (a.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray();

        [TestMethod]
        public void The_Settings_Saves_Name_The_Roles_Their_Service_Enforces()
        {
            Assembly web = typeof(WebApi.Controllers.BaseApiController).Assembly;
            void Check(string controller, string action, string serviceRoles)
            {
                Type type = web.GetTypes().Single(t => t.Name == controller);
                MethodInfo method = type.GetMethod(action);
                Assert.IsNotNull(method, controller + "." + action);
                CollectionAssert.AreEqual(serviceRoles.Split(',').OrderBy(r => r, StringComparer.Ordinal).ToArray(), RolesOf(method), controller + "." + action);
            }
                        Check("ProjectControlSetupController", "SaveSettingsAsync", "CEO,PMO");
            Check("ServiceRequestsController", "SaveSettingAsync", "CEO");
            Check("SalesCrmController", "SaveSettingAsync", "CEO");
            Check("QualityControlController", "SaveSettingAsync", "CEO,PMO,QA");
            Check("GlControlsController", "SaveSettingAsync", "FINANCEMANAGER");
            Check("GlControlsController", "SaveThresholdAsync", "FINANCEMANAGER");
            Check("ProcurementController", "SaveSettingAsync", "CEO,PMO");
        }
    }
}
