using System;
using System.Linq;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1: the reference list and the platform dossiers. Maker is never checker, a seat never decides, a change after the confirmation voids it, the system proves what it knows.</summary>
    [TestClass]
    public class EngineeringReferenceTests
    {
        [TestMethod]
        public async Task A_Reference_List_Is_Approved_By_A_Manager_Who_Did_Not_Touch_It()
        {
            AppHost host = NewHost("ref1");
            EnReferenceListDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref(), Ct)));
            Assert.AreEqual(1, d.Revision);
            Assert.IsTrue(d.CanSubmit);
            Assert.IsFalse(d.CanApprove);
            Ok(await RefAction(host, Eng1, d.Id, "submit"));
            Refused(await RefAction(host, Eng1, d.Id, "approve"));                      // an engineer has no approval role
            Refused(await host.As(Nobody, p => p.GetRequiredService<IEnReferenceService>().GetListAsync(d.Id, Ct)));
            // a manager who edited the list cannot approve it
            EnReferenceListDto other = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("VDDR-TWO"), Ct)));
            Ok(await RefAction(host, Mgr2, other.Id, "submit"));
            Refused(await RefAction(host, Mgr2, other.Id, "approve"), "شخص دیگری");
            EnReferenceListDto approved = Ok(await RefAction(host, Mgr1, d.Id, "approve"));
            Assert.AreEqual((int)EnRefStatus.Approved, approved.Status);
            Assert.IsTrue(approved.IsCurrent);
            Assert.AreEqual(2101, approved.ApprovedByUserId);
        }

        [TestMethod]
        public async Task A_Seat_Neither_Submits_Nor_Approves_A_Reference_List()
        {
            AppHost host = NewHost("ref2");
            var seat = await SeatAsync(host);
            EnReferenceListDto d = Ok(await host.As(seat, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref(), Ct)));   // drafting is allowed
            Refused(await RefAction(host, seat, d.Id, "submit"), "انسان");
            Ok(await RefAction(host, Eng1, d.Id, "submit"));
            var seatCeo = await SeatAsync(host, "AG-ENG", "CEO");
            Refused(await RefAction(host, seatCeo, d.Id, "approve"), "انسان");
            Assert.AreEqual(0, host.Read(u => u.Set<EnReferenceList>().Count(l => l.Status == EnRefStatus.Approved)));
        }

        [TestMethod]
        public async Task A_New_Revision_Supersedes_The_Approved_One_And_Only_One_Stays_Current()
        {
            AppHost host = NewHost("ref3");
            EnReferenceListDto first = await ApprovedList(host);
            EnReferenceListDto draft = Ok(await RefAction(host, Eng2, first.Id, "revise"));
            Assert.AreEqual(2, draft.Revision);
            Refused(await RefAction(host, Eng2, first.Id, "revise"), "بازنگری باز");
            Refused(await RefAction(host, Eng2, draft.Id, "approve"));
            Ok(await RefAction(host, Eng2, draft.Id, "submit"));
            Refused(await RefAction(host, Mgr1, draft.Id, "return", "کوتاه"), "15");
            Ok(await RefAction(host, Mgr1, draft.Id, "return", "فهرست باید رشتهٔ برق را هم شامل شود"));
            Ok(await RefAction(host, Eng2, draft.Id, "submit"));
            Ok(await RefAction(host, Mgr1, draft.Id, "approve"));
            Assert.AreEqual(1, host.Read(u => u.Set<EnReferenceList>().Count(l => l.Status == EnRefStatus.Approved)));
            Assert.AreEqual((int)EnRefStatus.Superseded, host.Read(u => (int)u.Set<EnReferenceList>().Single(l => l.Id == first.Id).Status));
            Assert.AreEqual(2, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().GetCurrentAsync(null, Ct))).Revision);
        }

        [TestMethod]
        public async Task A_Draft_Is_Edited_Only_While_It_Is_A_Draft()
        {
            AppHost host = NewHost("ref4");
            EnReferenceListDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref(), Ct)));
            EnSaveReferenceDto edit = Ref(); edit.Id = d.Id; edit.RowVersion = d.RowVersion; edit.Lines.RemoveAt(3);
            EnReferenceListDto edited = Ok(await host.As(Eng2, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(edit, Ct)));
            Assert.AreEqual(3, edited.LineCount);
            Ok(await RefAction(host, Eng1, d.Id, "submit"));
            edit.RowVersion = null;
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(edit, Ct)), "پیش‌نویس");
            EnSaveReferenceDto empty = Ref(); empty.Lines.Clear();
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(empty, Ct)), "یک ردیف");
        }

        [TestMethod]
        public async Task A_Platform_Is_Complete_Only_After_A_Manager_Who_Did_Not_Touch_It_Confirms_And_A_Change_Voids_It()
        {
            AppHost host = NewHost("plat1");
            int product = Product(host);
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", ReferenceCode = Std, StandardBasis = "دیتاشیت پروژهٔ نمونه، بند ۴" }, Ct)), "فهرست مدارک مرجع");   // no approved list: no dossier either
            await ApprovedList(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            Assert.AreEqual((int)EnDossierStatus.Incomplete, d.Status);
            Assert.AreEqual(3, d.RequiredCount);
            Assert.AreEqual(3, d.MissingCount);
            Assert.AreEqual(2, d.TenderStageMissingCount);
            Assert.IsFalse(d.CanComplete);
            Refused(await Dossier(host, Eng1, product, "complete"), "الزامی");
            // a file of somebody else, a wrong type and a missing file are refused
            Guid foreign = File(host, 2112);
            Refused(await Supply(host, Eng1, d.Lines[0].Id, foreign), "خود شما");
            Refused(await Supply(host, Eng1, d.Lines[0].Id, File(host, 2111, ".exe")));
            Refused(await Supply(host, Eng1, d.Lines[0].Id, null));
            Refused(await Supply(host, Eng1, d.Lines[0].Id, null, true, "کوتاه"), "30");
            foreach (EnDossierLineDto l in d.Lines.Where(x => x.Required))
                d = Ok(await Supply(host, Eng1, l.Id, File(host, 2111)));
            Assert.AreEqual(0, d.MissingCount);
            Refused(await Dossier(host, Mgr1, product, "confirm"), "کامل‌شده");
            Ok(await Dossier(host, Eng1, product, "complete"));
            Refused(await Dossier(host, Eng1, product, "confirm"));                       // an engineer cannot confirm
            var seatMgr = await SeatAsync(host, "AG-ENG", "ENGINEERING");
            Refused(await Dossier(host, seatMgr, product, "confirm"), "انسان");
            d = Ok(await Dossier(host, Mgr1, product, "confirm"));
            Assert.AreEqual((int)EnDossierStatus.Complete, d.Status);
            Assert.IsTrue(await host.As(Eng1, p => EnPlatformRules2.IsComplete(p, product)));
            // a later change of one line voids the confirmation
            d = Ok(await Supply(host, Eng2, d.Lines.First(x => x.Required).Id, File(host, 2112)));
            Assert.AreEqual((int)EnDossierStatus.Incomplete, d.Status);
            Assert.IsFalse(await host.As(Eng1, p => EnPlatformRules2.IsComplete(p, product)));
        }

        [TestMethod]
        public async Task The_Maker_Of_A_Dossier_Cannot_Confirm_It_And_A_Return_Opens_The_Line()
        {
            AppHost host = NewHost("plat2");
            int product = Product(host);
            await ApprovedList(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            foreach (EnDossierLineDto l in d.Lines.Where(x => x.Required))
                d = Ok(await Supply(host, Mgr1, l.Id, File(host, 2101)));
            Ok(await Dossier(host, Mgr1, product, "complete"));
            Refused(await Dossier(host, Mgr1, product, "confirm"), "شخص دیگری");
            Refused(await Dossier(host, Mgr2, product, "return", "کوتاه"), "15");
            d = Ok(await Dossier(host, Mgr2, product, "return", "نقشهٔ مونتاژ قدیمی است و باید دوباره بارگذاری شود", d.Lines.First(x => x.Required).Id));
            Assert.AreEqual((int)EnDossierStatus.Incomplete, d.Status);
            Assert.AreEqual(1, d.MissingCount);
        }

        [TestMethod]
        public async Task The_System_Proves_A_Released_Bom_And_An_Approved_Routing_Without_A_File()
        {
            AppHost host = NewHost("plat3");
            int product = Product(host);
            await ApprovedList(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            Assert.AreEqual(3, d.MissingCount);
            host.Write(u =>
            {
                AppHost.Add(u, new PbBomHeader { ProductId = product, AssemblyPartNumber = "99.11.00.000.2", Revision = 0, Status = PbBomStatus.Released });
                AppHost.Add(u, new PbOpcRouting { ProductId = product, PartNumber = "99.11.00.000.2", Revision = 0, Status = PbRoutingStatus.Approved });
                u.SaveChanges();
            }, Seeder);
            d = Ok(await Dossier(host, Eng1, product, "assess"));
            Assert.AreEqual(1, d.MissingCount, "only the drawing is left");
            Assert.IsTrue(d.Lines.Where(l => l.DocumentType == "BOM" || l.DocumentType == "OPC").All(l => l.SystemProven && l.State == 1));
            Refused(await Supply(host, Eng1, d.Lines.First(l => l.DocumentType == "BOM").Id, File(host, 2111)), "سامانه");
            // a Draft BOM is not a proof
            host.Write(u => { foreach (PbBomHeader b in u.Set<PbBomHeader>().ToList()) b.Status = PbBomStatus.Draft; u.SaveChanges(); }, Seeder);
            d = Ok(await Dossier(host, Eng1, product, "assess"));
            Assert.AreEqual(2, d.MissingCount);
        }

        [TestMethod]
        public async Task A_New_Reference_Revision_Makes_The_Dossier_Stale_And_The_Assessment_Merges_Without_Losing_Files()
        {
            AppHost host = NewHost("plat4");
            int product = Product(host);
            EnReferenceListDto first = await ApprovedList(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            Guid kept = File(host, 2111);
            d = Ok(await Supply(host, Eng1, d.Lines.First(l => l.DocumentType == "DWG").Id, kept));
            EnReferenceListDto draft = Ok(await RefAction(host, Eng2, first.Id, "revise"));
            EnSaveReferenceDto edit = Ref(); edit.Id = draft.Id; edit.RowVersion = draft.RowVersion; edit.Lines.Add(new EnReferenceLineDto { DocumentType = "CALC", Title = "محاسبات", Required = true });
            Ok(await host.As(Eng2, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(edit, Ct)));
            Ok(await RefAction(host, Eng2, draft.Id, "submit"));
            Ok(await RefAction(host, Mgr1, draft.Id, "approve"));
            d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(product, Std, Ct)));
            Assert.IsTrue(d.Stale);
            d = Ok(await Dossier(host, Eng1, product, "assess"));
            Assert.IsFalse(d.Stale);
            Assert.AreEqual(4, d.RequiredCount);
            Assert.AreEqual(kept, d.Lines.First(l => l.DocumentType == "DWG").DocumentId);
            Assert.AreEqual(2, d.ReferenceRevision);
        }
    }

    internal static class EnPlatformRules2
    {
        public static Task<bool> IsComplete(IServiceProvider p, int productId, string code = "VDDR-TEST") =>
            Services.NasbNiro.Engineering.EnPlatformRules.IsCompleteAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), productId, code, System.Threading.CancellationToken.None);
    }
}
