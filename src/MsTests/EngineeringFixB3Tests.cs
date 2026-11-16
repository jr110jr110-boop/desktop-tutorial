using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1 fix round B3: the project-initiation seat (AG-INIT) holds no approving role; it still drafts and sends the unit notices because the service identifies that one seat by its seat user name.</summary>
    [TestClass]
    public class EngineeringFixB3Tests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        private static void SetStatus(AppHost host, int handover, EnHandoverStatus status) =>
            host.Write(u => { u.Set<EnHandover>().First(x => x.Id == handover).Status = status; u.SaveChanges(); }, Seeder);

        [TestMethod]
        public void The_Init_Seat_Holds_Exactly_Planning_And_No_Agent_Seat_Holds_An_Approving_Role()
        {
            CollectionAssert.AreEqual(new[] { "PLANNING" }, AiSeatCatalog.Find("AG-INIT").Roles);
            Assert.AreEqual(AiSeatNames.UserName("AG-INIT"), SeatApprovalGuard.InitUserName);
            string[] approving = { "CEO", "PMO", "FINANCEMANAGER", "PROJECTMANAGER", "QA" };
            foreach (string code in new[] { "AG-MASTER", "AG-SALES", "AG-CONTRACT", "AG-INIT", "AG-PLAN", "AG-ENG", "AG-PROC", "AG-WH", "AG-MF", "AG-QC", "AG-SHIP", "AG-SERVICE", "AG-FIN" })
                foreach (string role in approving) CollectionAssert.DoesNotContain(AiSeatCatalog.Find(code).Roles, role, code);
        }

        [TestMethod]
        public async Task B3_The_Init_Seat_Alone_Drafts_And_Sends_The_Notices_Beside_A_Human_Pmo_And_Decides_Nothing()
        {
            AppHost host = Started("b3init");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            ClaimsPrincipal init = await SeatAsync(host, "AG-INIT", "PLANNING"), engSeat = await SeatAsync(host, "AG-ENG", "ENGINEER");

            // decisions: refused for the init seat, whatever the status
            SetStatus(host, h.Id, EnHandoverStatus.Submitted);
            Refused(await Act(host, init, "accept", d => d.Id = h.Id));
            SetStatus(host, h.Id, EnHandoverStatus.Accepted);
            Refused(await Act(host, init, "release", d => d.Id = h.Id));

            // draft-distribution: the engineering seat and a human engineer are refused, the init seat passes
            Refused(await Act(host, engSeat, "draft-distribution", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, Eng1, "draft-distribution", d => d.Id = h.Id), "PMO");
            EnHandoverDto drafted = Ok(await Act(host, init, "draft-distribution", d => d.Id = h.Id));
            Assert.IsTrue(drafted.Distribution.Count > 0);
            Assert.IsTrue(host.Read(u => u.Set<EnCaseEvent>().Any(e => e.CaseId == caseId && e.Kind == "distribution-drafted")));

            // editing a notice text and the receipt stay human
            int row = drafted.Distribution.First().Id;
            Refused(await Act(host, init, "edit-scope", d => { d.DistributionId = row; d.ScopeNote = "متن تازه"; }));
            Refused(await Act(host, init, "acknowledge", d => d.DistributionId = row));

            // distribute: only a released handover; the engineering seat and a human engineer are refused, the init seat passes
            SetStatus(host, h.Id, EnHandoverStatus.Released);
            Refused(await Act(host, engSeat, "distribute", d => d.Id = h.Id), "PMO");
            Refused(await Act(host, Eng1, "distribute", d => d.Id = h.Id), "PMO");
            Ok(await Act(host, init, "distribute", d => d.Id = h.Id));
            Assert.IsTrue(host.Read(u => u.Set<EnDistribution>().Where(d => d.HandoverId == h.Id).All(d => d.NoticeSentAt != null)));
            Refused(await Act(host, init, "acknowledge", d => d.DistributionId = row));
        }

        [TestMethod]
        public async Task B3_A_Human_Pmo_Still_Passes_And_A_Seat_That_Merely_Carries_The_Pmo_Claim_Does_Not()
        {
            AppHost host = Started("b3pmo");
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            SetStatus(host, h.Id, EnHandoverStatus.Accepted);
            Refused(await Act(host, await SeatAsync(host, "AG-ENG", "PMO"), "draft-distribution", d => d.Id = h.Id));
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
        }

        [TestMethod]
        public void B3_The_Init_Seat_Writes_The_Distribution_Rows_And_Never_Another_Engineering_Table()
        {
            AppHost host = NewHost("b3guard");
            ClaimsPrincipal init = AppHost.Principal(99, "seat.ag-init", "PLANNING");
            host.Write(u =>
            {
                AppHost.Add(u, new EnDistribution { HandoverId = 1, CaseId = 1, ContractId = 1, Unit = EnUnit.Qa, Roles = "QA", Status = EnDistStatus.Draft, ScopeNote = "x" });
                AppHost.Add(u, new EnCaseEvent { CaseId = 1, Kind = "distribution-drafted", At = DateTimeOffset.Now });
                u.SaveChanges();
            }, init);
            Assert.AreEqual(1, host.Read(u => u.Set<EnDistribution>().Count()));
            string[] allowed = { nameof(EnDistribution), nameof(EnCaseEvent), nameof(EnHandover), nameof(EnCase) };
            foreach (Type type in host.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => t != null && t.Namespace == "Entities.Model.NasbNiro.Engineering" && t.Name.StartsWith("En", StringComparison.Ordinal) && !allowed.Contains(t.Name)))
            {
                var ex = Assert.ThrowsException<InvalidOperationException>(() => host.Write(u => { u.Entry(Activator.CreateInstance(type)).State = EntityState.Added; u.SaveChanges(); }, init), type.Name);
                StringAssert.Contains(ex.Message, SeatApprovalGuard.InitMessage, type.Name);
            }
        }
    }
}
