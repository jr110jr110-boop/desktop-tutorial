using System;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.AuditSeat;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.ProductBankDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// Package G, CI fix round: a drawing that is linked to no part is released without a characteristic list and without a design verification; linking it to a part afterwards must not be a way round both gates.
    /// A link of a drawing that has a revision in force is checked like a release when that revision was released at or after the cut-over instant; a revision released before the cut-over is legacy and may be linked.
    /// </summary>
    public partial class PackageA1DesignControlTests
    {
        private const string LinkPart = "99.11.01.001.2";

        private static void CutOverAt(AppHost h, DateTimeOffset at) => h.Write(u => { AppHost.Add(u, new GlActivation { StartAt = at }); u.SaveChanges(); }, Seeder);

        private static Task<Common.Results.ResponseDto<PcDocLinkDto>> LinkToPart(AppHost h, int deliverable, string part) =>
            h.As(Dcc, p => p.GetRequiredService<IEdmsService>().SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = deliverable, Kind = PcDocLinkKind.PartNumber, PartNumber = part }, Ct));

        private static async Task ApprovedEmptyList(AppHost h, int revisionId)
        {
            var saved = await h.As(ListMaker, p => p.GetRequiredService<ICharacteristicService>().SaveAsync(new ViewModels.NasbNiro.QualityDtos.SaveCharListDto { RevisionId = revisionId, Reason = "این نقشه مشخصه قابل اندازه‌گیری ندارد (آزمون)" }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            foreach (var step in new[] { (ListMaker, "submit"), (ListChecker, "approve") })
                Assert.IsTrue((await h.As(step.Item1, p => p.GetRequiredService<ICharacteristicService>().ActionAsync(new ViewModels.NasbNiro.QualityDtos.CharListActionDto { Id = saved.Data.Id, Action = step.Item2 }, Ct))).Success);
        }

        [TestMethod]
        public async Task ReleasingAnUnlinkedDrawingAfterTheCutOverAndLinkingItLaterIsRefusedWithWhatIsMissing()
        {
            AppHost h = NewHost("linkgate1");
            await Ready(h);
            CutOverAt(h, DateTimeOffset.Now.AddDays(-1));
            var d = await DrawingRevision(h, "x", false);
            var released = await ReleaseDrawing(h, d.Revision);
            Assert.IsTrue(released.Success, "a drawing of no part is controlled by the EDMS review only: " + released.Message);
            var link = await LinkToPart(h, d.Deliverable, LinkPart);
            Assert.IsFalse(link.Success, "the way round both gates is closed");
            StringAssert.Contains(link.Message, "فهرست مشخصه");
            StringAssert.Contains(link.Message, "تایید طراحی");
            Assert.AreEqual(0, h.Read(u => u.Set<PcDocumentLink>().Count(l => l.DeliverableId == d.Deliverable)), "nothing was linked");
        }

        [TestMethod]
        public async Task ALinkIsRefusedForTheMissingVerificationAloneWhenTheListExists()
        {
            AppHost h = NewHost("linkgate2");
            await Ready(h);
            CutOverAt(h, DateTimeOffset.Now.AddDays(-1));
            var d = await DrawingRevision(h, "x", false);
            Assert.IsTrue((await ReleaseDrawing(h, d.Revision)).Success);
            await ApprovedEmptyList(h, d.Revision);
            var link = await LinkToPart(h, d.Deliverable, LinkPart);
            Assert.IsFalse(link.Success);
            StringAssert.Contains(link.Message, "تایید طراحی");
            Assert.IsFalse(link.Message.Contains("فهرست مشخصه‌های تاییدشده برای این بازنگری وجود ندارد"), "the list exists: only the verification is missing");
        }

        [TestMethod]
        public async Task ARevisionReleasedBeforeTheCutOverIsLegacyAndMayBeLinked()
        {
            AppHost h = NewHost("linkgate3");
            await Ready(h);
            CutOverAt(h, DateTimeOffset.Now.AddDays(30));
            var d = await DrawingRevision(h, "x", false);
            Assert.IsTrue((await ReleaseDrawing(h, d.Revision)).Success);
            var link = await LinkToPart(h, d.Deliverable, LinkPart);
            Assert.IsTrue(link.Success, link.Message);
        }

        [TestMethod]
        public async Task ADrawingThatPassedBothGatesMayBeLinkedToAnotherPart()
        {
            AppHost h = NewHost("linkgate4");
            var (product, plan, bom) = await Ready(h);
            CutOverAt(h, DateTimeOffset.Now.AddDays(-1));
            var d = await DrawingRevision(h, LinkPart, true);
            Assert.IsFalse((await ReleaseDrawing(h, d.Revision)).Success, "linked first: the release asks for the verification");
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, new SaveDesignOutputDto { Kind = DesignOutputKind.Drawing, RevisionId = d.Revision }));
            await Judge(h, held);
            Assert.IsTrue((await Close(h, held.Id)).Success);
            var released = await ReleaseDrawing(h, d.Revision, true);
            Assert.IsTrue(released.Success, released.Message);
            var link = await LinkToPart(h, d.Deliverable, "99.11.01.002.2");
            Assert.IsTrue(link.Success, "list and verification exist: " + link.Message);
        }
    }
}
