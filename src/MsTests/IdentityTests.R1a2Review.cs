using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Extentions;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Contracts.Identity;
using Services.Identity.AccountServices;
using Services.Resources;
using ViewModels.Identity.Settings;
using ViewModels.Identity.UserDtos;

namespace MsTests
{
    /// <summary>
    /// R1a2, independent review of R1a2 (the fixes): AI seats are no PMO users for the administration of users (H-1); taking the PMO role away from a user and deleting a PMO user are
    /// requests that a different PMO user approves, a PMO user may step down himself unless he is the last one (M-1); a sensitive role waits until its receiver has chosen a password
    /// of his own, and a former holder is handled as a holder for a week when his password is reset (M-2); the recovery from the server itself has a cap (L-1); sign-ins with the name of
    /// a seat are counted like unknown names (L-3); the target of a reset or removal cannot reject it (L-4); the upgrade writes records for the PMO users of before R1a2 (L-5);
    /// the answers of register-user, update-user and edit-user-roles say which requests wait (L-9).
    /// Every test that depends on the number of PMO users runs on a host of its own.
    /// </summary>
    public partial class IdentityTests
    {
        // ---------------------------------------------------------------------------------------------------- helpers

        /// <summary>The password the receivers of a sensitive role choose for themselves in these tests.</summary>
        private const string ChosenPassword = "Own-Chosen-2031m";

        /// <summary>
        /// R1a2 (M-2): the receiver of a sensitive role chooses a password of his own with "change password" (he gives the current one). The PMO user who registered him chose the first
        /// password, so the approval of his role waits for this.
        /// </summary>
        private static async Task ChoosePasswordAsync(Host h, int id, string name, string current = GoodPassword, string next = ChosenPassword)
        {
            ResponseDto<ResponseLoginUserDto> r = await h.As(Signed(id, name, "WAREHOUSE"),
                s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = current, NewPassword = next }, default).AsTask());
            Assert.IsTrue(r.Success, name + ": " + r.Message);
        }

        /// <summary>The service user of an AI seat that holds the PMO role (the weekly-report seat does), active, put in directly the way the system's own setup does.</summary>
        private static int AddSeatWithPmo(Host h, string seatName = "seat.pmo-weekly")
        {
            h.Provider.RunScopedService<IApplicationUserManager>(m =>
                Assert.IsTrue(m.CreateAsync(new User { UserName = seatName, Email = seatName + "@example.com", EmailConfirmed = true, LockoutEnabled = true, IsActive = true }, GoodPassword).GetAwaiter().GetResult().Succeeded));
            int id = h.Query(uow => uow.Set<User>().Single(u => u.UserName == seatName).Id);
            h.Write(uow => uow.Set<UserRole>().Add(new UserRole { UserId = id, RoleId = PmoRole }));
            return id;
        }

        /// <summary>The latest record of the role <paramref name="role"/> of a user (also one that is marked as removed), or null.</summary>
        private static RoleGrantRecord RecordOf(Host h, int userId, string role = "PMO")
        {
            List<string> values = h.Query(uow => uow.Set<UserClaim>().AsNoTracking()
                .Where(c => c.UserId == userId && c.ClaimType == IdentityRules.RoleGrantedClaimType)
                .Select(c => c.ClaimValue)
                .ToList());
            RoleGrantRecord found = null;
            foreach (string value in values)
                if (RoleGrantRecord.TryParse(value, out RoleGrantRecord record) && record.Role == role && (found == null || record.At >= found.At))
                    found = record;
            return found;
        }

        /// <summary>Runs the seed of the identity database again, as the next start of the server does.</summary>
        private static void RunSeed(Host h)
        {
            using (IServiceScope scope = h.Provider.CreateScope())
                scope.ServiceProvider.GetRequiredService<IIdentityDbInitializer>().SeedData();
        }

        private static bool Told(Host h, int userId, string title) =>
            h.Query(uow => uow.Set<OfInboxItem>().Any(i => i.RecipientUserId == userId && i.Title == title));

        // ---------------------------------------------------------------------------------------------------- pure rules

        [TestMethod]
        public void Taking_The_Pmo_Role_Away_Is_Planned_As_A_Request_And_Only_A_Lone_Pmo_Removal_Is_A_Step_Down()
        {
            IdentityRules.RolePlan plan = IdentityRules.PlanRoles(new[] { "PMO", "WAREHOUSE", "HR" }, new[] { "warehouse" }, null);
            CollectionAssert.AreEqual(new[] { "PMO" }, plan.RevokeNeedsApproval);
            CollectionAssert.AreEqual(new[] { "HR" }, plan.Remove, "every other role is taken away at once, sensitive or not");
            Assert.IsFalse(plan.IsEmpty);
            Assert.IsFalse(plan.IsOnlyPmoRemoval, "another role goes too");

            Assert.IsTrue(IdentityRules.PlanRoles(new[] { "PMO", "WAREHOUSE" }, new[] { "WAREHOUSE" }, null).IsOnlyPmoRemoval);
            Assert.IsTrue(IdentityRules.PlanRoles(new[] { "PMO" }, new string[0], null).IsOnlyPmoRemoval);
            Assert.IsFalse(IdentityRules.PlanRoles(new[] { "PMO" }, new[] { "WAREHOUSE" }, null).IsOnlyPmoRemoval, "a role is given as well");
            Assert.IsFalse(IdentityRules.PlanRoles(new[] { "PMO" }, new[] { "PMO", "CEO" }, null).IsOnlyPmoRemoval, "nothing is taken away");
            Assert.IsTrue(IdentityRules.PlanRoles(new[] { "PMO" }, new[] { "pmo" }, null).IsEmpty, "the same role in another case is nothing to do");

            // the bootstrap flag lets the PMO role be given at once, and only that role
            plan = IdentityRules.PlanRoles(new string[0], new[] { "PMO", "CEO" }, null, pmoBootstrap: true);
            CollectionAssert.AreEqual(new[] { "PMO" }, plan.AddNow);
            CollectionAssert.AreEqual(new[] { "CEO" }, plan.NeedApproval);
            plan = IdentityRules.PlanRoles(new string[0], new[] { "PMO" }, null);
            CollectionAssert.AreEqual(new[] { "PMO" }, plan.NeedApproval, "without the bootstrap it is a request");
        }

        [TestMethod]
        public void A_Record_Of_A_Grant_Keeps_Who_Asked_Who_Approved_When_And_What_Became_Of_It()
        {
            var at = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.FromHours(3.5));
            var record = new RoleGrantRecord { Role = "pmo", GrantedBy = 4, ApprovedBy = 9, At = at, Bootstrap = true, RemovedAt = at.AddDays(2) };
            Assert.IsTrue(RoleGrantRecord.TryParse(record.ToClaimValue(), out RoleGrantRecord back));
            Assert.AreEqual("PMO", back.Role);
            Assert.AreEqual(4, back.GrantedBy);
            Assert.AreEqual(9, back.ApprovedBy);
            Assert.AreEqual(at, back.At);
            Assert.IsTrue(back.Bootstrap);
            Assert.IsTrue(back.IsRemoved);
            Assert.AreEqual(at.AddDays(2), back.RemovedAt);
            Assert.IsFalse(back.Legacy);

            var held = new RoleGrantRecord { Role = "CEO", GrantedBy = 4, ApprovedBy = 9, At = at };
            Assert.IsTrue(RoleGrantRecord.TryParse(held.ToClaimValue(), out back));
            Assert.IsFalse(back.IsRemoved);
            Assert.IsNull(back.RemovedAt);

            // the record the upgrade writes names nobody; a record of a real grant must name who asked for it
            var legacy = new RoleGrantRecord { Role = "PMO", At = at, Legacy = true };
            Assert.IsTrue(RoleGrantRecord.TryParse(legacy.ToClaimValue(), out back));
            Assert.IsTrue(back.Legacy);
            Assert.AreEqual(0, back.GrantedBy);
            Assert.IsFalse(RoleGrantRecord.TryParse(new RoleGrantRecord { Role = "PMO", GrantedBy = 0, At = at }.ToClaimValue(), out _));

            foreach (string bad in new[] { null, "", "x", "{}", "{\"role\":\"PMO\"}", "{\"role\":\"PMO\",\"by\":3,\"at\":\"nope\"}", "{\"role\":\"PMO\",\"by\":3,\"at\":\"2026-10-26T00:00:00Z\",\"removedAt\":\"nope\"}" })
                Assert.IsFalse(RoleGrantRecord.TryParse(bad, out _), bad ?? "null");
        }

        [TestMethod]
        public void A_Request_Of_The_Kind_Revoke_Round_Trips_And_Is_Told_Apart_From_A_Role_And_A_Reset()
        {
            var at = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

            var removal = new PendingRoleGrant { Role = "PMO", RequestedBy = 3, RequestedAt = at, Kind = IdentityRules.KindRevoke };
            Assert.IsTrue(PendingRoleGrant.TryParse(removal.ToClaimValue(), out PendingRoleGrant back));
            Assert.IsTrue(back.IsRevoke);
            Assert.IsFalse(back.IsUserDelete);
            Assert.IsFalse(back.IsReset);
            Assert.IsFalse(back.IsRole);
            Assert.AreEqual("PMO", back.Role);

            var deletion = new PendingRoleGrant { Role = IdentityRules.DeleteRoleLabel, RequestedBy = 3, RequestedAt = at, Kind = IdentityRules.KindRevoke };
            Assert.IsTrue(PendingRoleGrant.TryParse(deletion.ToClaimValue(), out back));
            Assert.IsTrue(back.IsRevoke);
            Assert.IsTrue(back.IsUserDelete);

            var reset = new PendingRoleGrant { Role = IdentityRules.ResetRoleLabel, RequestedBy = 3, RequestedAt = at, Kind = IdentityRules.KindReset };
            Assert.IsTrue(PendingRoleGrant.TryParse(reset.ToClaimValue(), out back));
            Assert.IsTrue(back.IsReset);
            Assert.IsFalse(back.IsRevoke);
            Assert.IsFalse(back.IsRole);

            Assert.IsTrue(PendingRoleGrant.TryParse(new PendingRoleGrant { Role = "ceo", RequestedBy = 3, RequestedAt = at }.ToClaimValue(), out back));
            Assert.IsTrue(back.IsRole);
            Assert.AreEqual(IdentityRules.KindRole, back.Kind);

            // a row written before R1a2 has no kind: it is a role
            Assert.IsTrue(PendingRoleGrant.TryParse("{\"role\":\"CEO\",\"by\":3,\"at\":\"2026-10-26T00:00:00Z\"}", out back));
            Assert.IsTrue(back.IsRole);
        }

        [TestMethod]
        public void A_Sensitive_Role_Taken_Away_Less_Than_A_Week_Ago_Still_Counts_For_A_Reset()
        {
            var now = new DateTimeOffset(2030, 5, 10, 12, 0, 0, TimeSpan.Zero);
            RoleGrantRecord Removed(string role, double daysAgo) => new RoleGrantRecord { Role = role, GrantedBy = 1, At = now.AddDays(-30), RemovedAt = now.AddDays(-daysAgo) };

            CollectionAssert.AreEqual(new[] { "PMO" }, IdentityRules.RecentlySensitiveRoles(new[] { Removed("PMO", 2) }, now));
            Assert.IsTrue(IdentityRules.HeldSensitiveRecently(new[] { Removed("CEO", 6.9) }, now));
            Assert.IsFalse(IdentityRules.HeldSensitiveRecently(new[] { Removed("CEO", 7) }, now), "a week ago is over");
            Assert.IsFalse(IdentityRules.HeldSensitiveRecently(new[] { Removed("CEO", 8) }, now));
            Assert.IsFalse(IdentityRules.HeldSensitiveRecently(new[] { Removed("WAREHOUSE", 1) }, now), "an ordinary role does not count");
            Assert.IsFalse(IdentityRules.HeldSensitiveRecently(new[] { new RoleGrantRecord { Role = "CEO", GrantedBy = 1, At = now.AddDays(-1) } }, now), "a role that is still held is not a recent loss");
            Assert.IsFalse(IdentityRules.HeldSensitiveRecently(null, now));
            Assert.IsFalse(IdentityRules.HeldSensitiveRecently(new RoleGrantRecord[] { null }, now));
            CollectionAssert.AreEqual(new[] { "CEO", "HR" }, IdentityRules.RecentlySensitiveRoles(new[] { Removed("HR", 1), Removed("CEO", 2), Removed("CEO", 3) }, now), "each name once, in order");
        }

        [TestMethod]
        public void The_Password_May_Be_Known_To_The_Pmo_User_Until_The_Receiver_Has_Changed_It_Himself()
        {
            Assert.IsFalse(IdentityRules.PasswordMayBeKnownToPmo(0, 0), "no registration in the trail (a user of before the trail)");
            Assert.IsFalse(IdentityRules.PasswordMayBeKnownToPmo(0, 9));
            Assert.IsTrue(IdentityRules.PasswordMayBeKnownToPmo(5, 0));
            Assert.IsTrue(IdentityRules.PasswordMayBeKnownToPmo(5, 3), "he changed it before the last time a PMO user set it");
            Assert.IsFalse(IdentityRules.PasswordMayBeKnownToPmo(5, 6), "he changed it after");
        }

        [TestMethod]
        public void The_Approver_Rules_Treat_A_Legacy_Record_Like_No_Record()
        {
            var now = new DateTimeOffset(2030, 5, 10, 12, 0, 0, TimeSpan.Zero);
            var legacy = new RoleGrantRecord { Role = "PMO", GrantedBy = 0, At = now, Legacy = true }; // written a moment ago by the upgrade
            Assert.IsNull(IdentityRules.ApprovalProblem(5, 6, 7, legacy, 3, now));

            var fresh = new RoleGrantRecord { Role = "PMO", GrantedBy = 2, ApprovedBy = 3, At = now };
            Assert.AreEqual("ApproverTooNew", IdentityRules.ApprovalProblem(5, 6, 7, fresh, 3, now));
            Assert.IsNull(IdentityRules.ApprovalProblem(5, 6, 7, fresh, 3, now.AddDays(3)));
            Assert.IsNull(IdentityRules.ApprovalProblem(5, 6, 7, fresh, 0, now), "0 switches the minimum off");

            var madeByRequester = new RoleGrantRecord { Role = "PMO", GrantedBy = 5, ApprovedBy = 3, At = now.AddDays(-30) };
            Assert.AreEqual("ApproverGrantedByRequester", IdentityRules.ApprovalProblem(5, 6, 7, madeByRequester, 3, now));
            madeByRequester.Bootstrap = true;
            Assert.IsNull(IdentityRules.ApprovalProblem(5, 6, 7, madeByRequester, 3, now), "the bootstrap is exempt");

            Assert.AreEqual("SameApprover", IdentityRules.ApprovalProblem(5, 5, 7, legacy, 3, now), "the basic problems come first");
            Assert.AreEqual("ApproverIsReceiver", IdentityRules.ApprovalProblem(5, 7, 7, legacy, 3, now));
            Assert.IsNull(IdentityRules.ApprovalProblem(5, 6, 7, null, 3, now), "no record at all: the seeded administrator");
        }

        [TestMethod]
        public void Wrong_Passwords_From_The_Server_Are_Counted_Per_User_Inside_A_Five_Minute_Window()
        {
            LoopbackFailureTally.Reset();
            try
            {
                var t = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
                Assert.IsTrue(LoopbackFailureTally.RecoveryOpen("1:a", t));
                for (int i = 1; i <= IdentityRules.LoopbackFailureCap; i++)
                    Assert.AreEqual(i, LoopbackFailureTally.Add("1:a", t.AddSeconds(i)));
                Assert.IsTrue(LoopbackFailureTally.RecoveryOpen("1:a", t.AddSeconds(30)), "the cap itself is still allowed");
                Assert.AreEqual(IdentityRules.LoopbackFailureCap + 1, LoopbackFailureTally.Add("1:a", t.AddSeconds(31)));
                Assert.IsFalse(LoopbackFailureTally.RecoveryOpen("1:a", t.AddSeconds(32)), "one more ends the recovery");
                Assert.IsTrue(LoopbackFailureTally.RecoveryOpen("2:b", t.AddSeconds(32)), "another user has his own count");

                // the wrong passwords leave the window; after five quiet minutes the recovery is open again
                Assert.AreEqual(IdentityRules.LoopbackFailureCap + 1, LoopbackFailureTally.Count("1:a", t.AddMinutes(4)));
                Assert.AreEqual(0, LoopbackFailureTally.Count("1:a", t.AddMinutes(6)));
                Assert.IsTrue(LoopbackFailureTally.RecoveryOpen("1:a", t.AddMinutes(6)));

                // a right password forgets the wrong ones
                LoopbackFailureTally.Add("3:c", t);
                LoopbackFailureTally.Add("3:c", t);
                Assert.AreEqual(2, LoopbackFailureTally.Count("3:c", t));
                LoopbackFailureTally.Clear("3:c");
                Assert.AreEqual(0, LoopbackFailureTally.Count("3:c", t));
            }
            finally
            {
                LoopbackFailureTally.Reset();
            }
        }

        // ---------------------------------------------------------------------------------------------------- H-1: AI seats are no PMO users for the administration of users

        [TestMethod]
        public async Task A_Seat_That_Holds_The_Pmo_Role_Does_Not_Stop_The_Bootstrap_Of_The_Second_Pmo_User()
        {
            var h = new Host(Unique("seat"));
            int seat = AddSeatWithPmo(h);
            CollectionAssert.Contains(h.RolesOf(seat), "PMO");

            // the administrator and the seat are "two" PMO users to a naive count; the seat is not counted, so the second PMO user is still given at once
            ResponseDto<ResponseUserDto> created = await h.As(h.Admin, s => s.RegisterAsync(NewUser(Unique("pmo"), new[] { PmoRole }, GoodPassword), default).AsTask());
            Assert.IsTrue(created.Success, created.Message);
            Assert.AreEqual(AccountServiceMessages.PmoBootstrapApplied, created.Message);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(created.Data.Id));
            Assert.AreEqual(1, Trail(h).Count(a => a.Action == "Bootstrap"));

            // two real PMO users now: the next PMO grant is a request again (the seat did not make it three, and did not make it none)
            ResponseDto<ResponseUserDto> third = await h.As(h.Admin, s => s.RegisterAsync(NewUser(Unique("pmo"), new[] { PmoRole }, GoodPassword), default).AsTask());
            Assert.IsTrue(third.Success, third.Message);
            Assert.AreEqual(0, h.RolesOf(third.Data.Id).Length);
            Assert.IsTrue(third.Data.Pending);
            Assert.AreEqual(1, Trail(h).Count(a => a.Action == "Bootstrap"));
        }

        [TestMethod]
        public async Task A_Seat_Is_Never_An_Approver_And_Never_Makes_A_Request_Look_Approvable()
        {
            var h = new Host(Unique("seat"));
            int seat = AddSeatWithPmo(h);
            ClaimsPrincipal seatPrincipal = Signed(seat, "seat.pmo-weekly", "PMO");
            int p2 = await h.AddPmoAsync(Unique("p2")); // the administrator, p2 and the seat hold the PMO role; only two of them are people

            // nobody but the seat could approve these, so they are not requested
            ResponseDto<AdminResetPasswordResultDto> reset = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = p2 }, default).AsTask());
            Assert.IsFalse(reset.Success);
            Assert.AreEqual(AccountServiceMessages.ResetNoApprover, reset.Message);
            ResponseDto<ChangeRoleResultDto> revoke = await SetRolesAsync(h, h.Admin, p2, WarehouseRole);
            Assert.IsFalse(revoke.Success);
            Assert.AreEqual(AccountServiceMessages.RevokeNoApprover, revoke.Message);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(p2), "nothing was changed");
            ResponseDto<bool> delete = await h.As(h.Admin, s => s.DeleteUserAsync(p2, default));
            Assert.IsFalse(delete.Success);
            Assert.AreEqual(AccountServiceMessages.RevokeNoApprover, delete.Message);
            Assert.IsFalse(h.Query(uow => uow.Set<User>().Single(u => u.Id == p2).IsDeleted));

            // the seat itself administers nothing and approves nothing
            int user = await h.RegisterAsync(h.Admin, Unique("u"), new[] { WarehouseRole, CeoRole });
            RoleGrantDto grant = (await GrantsAsync(h, h.Admin)).Single(g => g.UserId == user);
            ResponseDto<RoleGrantDto> bySeat = await ApproveAsync(h, seatPrincipal, grant.Id);
            Assert.IsFalse(bySeat.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, bySeat.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.OnlyPmo, bySeat.Message);
            ResponseDto<List<RoleGrantDto>> listed = await h.As(seatPrincipal, s => s.GetRoleGrantsAsync(default).AsTask());
            Assert.IsFalse(listed.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, listed.HttpStatusCode);
            Assert.IsFalse((await h.As(seatPrincipal, s => s.RegisterAsync(NewUser(Unique("x"), new[] { WarehouseRole }, GoodPassword), default).AsTask())).Success);
            Assert.IsFalse((await h.As(seatPrincipal, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = user }, default).AsTask())).Success);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(user));

            // a real PMO user is enough: with a third person the same requests are made
            int p3 = await h.AddPmoAsync(Unique("p3"));
            Assert.IsTrue((await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = p2 }, default).AsTask())).Data.Pending);
            Assert.IsTrue((await GrantsAsync(h, Pmo(p3))).Any(g => g.UserId == p2 && g.CanApprove));
        }

        // ---------------------------------------------------------------------------------------------------- M-1: taking the PMO role away and deleting a PMO user are requests

        [TestMethod]
        public async Task Taking_The_Pmo_Role_Away_Waits_For_A_Different_Pmo_User()
        {
            var h = new Host(Unique("revoke"));
            string p2Name = Unique("p2");
            int p2 = await h.AddPmoAsync(p2Name);
            int p3 = await h.AddPmoAsync(Unique("p3"));

            // the administrator asks for p2 to be a warehouse user only: the ordinary role is given at once, the removal of the PMO role waits
            ResponseDto<ChangeRoleResultDto> asked = await SetRolesAsync(h, h.Admin, p2, WarehouseRole);
            Assert.IsTrue(asked.Success, asked.Message);
            StringAssert.Contains(asked.Message, AccountServiceMessages.RevokePending);
            Assert.IsTrue(asked.Data.Pending);
            Assert.IsTrue(asked.Data.GrantId > 0);
            CollectionAssert.AreEqual(new[] { asked.Data.GrantId.Value }, asked.Data.GrantIds);
            CollectionAssert.AreEqual(new[] { "PMO", "WAREHOUSE" }, h.RolesOf(p2), "the PMO role is still there");
            Assert.IsTrue(InTrail(h, "Identity.Roles", "RevokeRequested", a => a.UserId == h.AdminId && a.EntityId == p2));
            Assert.IsTrue(Told(h, p2, AccountServiceMessages.RevokeRequestedInbox), "the user is told in his inbox");
            ResponseLoginUserDto session = await SignInAsync(h, p2Name); // after the ordinary role was added (that changed his stamp)

            // it is listed as a request of the kind "revoke"; the requester and the target cannot approve it and the target cannot reject it
            RoleGrantDto listed = (await GrantsAsync(h, Pmo(p3, "p3"))).Single(g => g.Id == asked.Data.GrantId.Value);
            Assert.AreEqual("revoke", listed.Kind);
            Assert.AreEqual("PMO", listed.Role);
            Assert.AreEqual(p2, listed.UserId);
            Assert.IsTrue(listed.CanApprove, listed.CannotApproveReason);
            Assert.AreEqual(AccountServiceMessages.GrantSameApprover, (await ApproveAsync(h, h.Admin, listed.Id)).Message);
            Assert.AreEqual(AccountServiceMessages.GrantApproverIsTarget, (await ApproveAsync(h, Pmo(p2, p2Name), listed.Id)).Message);
            ResponseDto<bool> selfReject = await h.As(Pmo(p2, p2Name), s => s.RejectRoleGrantAsync(listed.Id, default).AsTask());
            Assert.IsFalse(selfReject.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, selfReject.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.CannotRejectOwnRequest, selfReject.Message);
            CollectionAssert.AreEqual(new[] { "PMO", "WAREHOUSE" }, h.RolesOf(p2));

            // asking again does not make a second request, and changes nothing else
            ResponseDto<ChangeRoleResultDto> again = await SetRolesAsync(h, h.Admin, p2, WarehouseRole);
            Assert.IsTrue(again.Success, again.Message);
            Assert.IsTrue(again.Data.Pending);
            Assert.AreEqual(1, (await GrantsAsync(h, Pmo(p3, "p3"))).Count(g => g.UserId == p2));
            Assert.IsNull(await h.TokenProblemAsync(session.AccessToken), "nothing changed, so his token is as good as before");

            // the approval takes the role away and ends the access token that carried it
            ResponseDto<RoleGrantDto> approved = await ApproveAsync(h, Pmo(p3, "p3"), listed.Id);
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(AccountServiceMessages.RevokeApproved, approved.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(p2));
            Assert.IsNotNull(await h.TokenProblemAsync(session.AccessToken));
            Assert.IsFalse((await GrantsAsync(h, Pmo(p3, "p3"))).Any(g => g.Id == listed.Id), "the request is gone");
            Assert.IsTrue(InTrail(h, "Identity.Roles", "RevokeApproved", a => a.UserId == p3 && a.EntityId == p2));
            Assert.IsTrue(RecordOf(h, p2).IsRemoved, "the record stays, marked as removed");
            Assert.IsFalse((await h.As(Signed(p2, p2Name, "WAREHOUSE"), s => s.GetRoleGrantsAsync(default).AsTask())).Success, "p2 administers nothing now");
        }

        [TestMethod]
        public async Task An_Edit_That_Keeps_The_Pmo_Role_Withdraws_The_Request_To_Take_It_Away()
        {
            var h = new Host(Unique("revoke"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            int p3 = await h.AddPmoAsync(Unique("p3"));
            int grantId = (await SetRolesAsync(h, h.Admin, p2, WarehouseRole)).Data.GrantId.Value;
            Assert.IsTrue((await GrantsAsync(h, Pmo(p3))).Any(g => g.Id == grantId));

            // the edit-user screen with the PMO role kept withdraws it, and says so
            ResponseDto<ResponseUserDto> edited = await h.As(h.Admin, s => s.UpdateUserAsync(Edit(p2, "p2-edit@example.com", new[] { PmoRole, WarehouseRole }), default).AsTask());
            Assert.IsTrue(edited.Success, edited.Message);
            StringAssert.Contains(edited.Message, AccountServiceMessages.RevokeWithdrawnByEdit);
            Assert.IsFalse(edited.Data.Pending);
            Assert.IsFalse((await GrantsAsync(h, Pmo(p3))).Any(g => g.Id == grantId));
            Assert.AreEqual(AccountServiceMessages.GrantNotFound, (await ApproveAsync(h, Pmo(p3), grantId)).Message);
            CollectionAssert.AreEqual(new[] { "PMO", "WAREHOUSE" }, h.RolesOf(p2));
            Assert.IsTrue(InTrail(h, "Identity.Roles", "GrantRejected", a => a.EntityId == p2 && a.UserId == h.AdminId));
        }

        [TestMethod]
        public async Task A_Request_To_Take_The_Pmo_Role_Away_Is_Void_After_Seven_Days_And_A_New_One_Can_Follow()
        {
            var h = new Host(Unique("revoke"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            int p3 = await h.AddPmoAsync(Unique("p3"));
            int grantId = (await SetRolesAsync(h, h.Admin, p2, WarehouseRole)).Data.GrantId.Value;
            h.Write(uow => uow.Set<UserClaim>().Single(c => c.Id == grantId).ClaimValue =
                new PendingRoleGrant { Role = "PMO", RequestedBy = h.AdminId, RequestedAt = DateTimeOffset.Now.AddDays(-8), Kind = IdentityRules.KindRevoke }.ToClaimValue());

            Assert.IsFalse((await GrantsAsync(h, Pmo(p3))).Any(g => g.Id == grantId), "an expired request is not listed");
            ResponseDto<RoleGrantDto> late = await ApproveAsync(h, Pmo(p3), grantId);
            Assert.IsFalse(late.Success);
            Assert.AreEqual(AccountServiceMessages.GrantExpired, late.Message);
            CollectionAssert.AreEqual(new[] { "PMO", "WAREHOUSE" }, h.RolesOf(p2));

            ResponseDto<ChangeRoleResultDto> again = await SetRolesAsync(h, h.Admin, p2, WarehouseRole);
            Assert.IsTrue(again.Success, again.Message);
            Assert.IsTrue(again.Data.Pending);
            Assert.AreNotEqual(grantId, again.Data.GrantId.Value);
        }

        [TestMethod]
        public async Task A_Request_To_Take_The_Pmo_Role_Away_Does_Not_Need_The_User_To_Be_Active()
        {
            var h = new Host(Unique("revoke"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            int p3 = await h.AddPmoAsync(Unique("p3"));
            int grantId = (await SetRolesAsync(h, h.Admin, p2, WarehouseRole)).Data.GrantId.Value;

            // an inactive PMO user is exactly who one may want to remove; a request for a sensitive role would be void here
            h.Write(uow => uow.Set<User>().Single(u => u.Id == p2).IsActive = false);
            Assert.IsTrue((await GrantsAsync(h, Pmo(p3))).Single(g => g.Id == grantId).CanApprove);
            ResponseDto<RoleGrantDto> approved = await ApproveAsync(h, Pmo(p3), grantId);
            Assert.IsTrue(approved.Success, approved.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(p2));
        }

        [TestMethod]
        public async Task Deleting_A_Pmo_User_Is_A_Request_That_A_Different_Pmo_User_Approves_Or_Rejects()
        {
            var h = new Host(Unique("revoke"));
            string p2Name = Unique("p2");
            int p2 = await h.AddPmoAsync(p2Name);
            int p3 = await h.AddPmoAsync(Unique("p3"));
            int p4 = await h.AddPmoAsync(Unique("p4"));
            ResponseLoginUserDto session = await SignInAsync(h, p2Name);

            ResponseDto<bool> asked = await h.As(h.Admin, s => s.DeleteUserAsync(p2, default));
            Assert.IsTrue(asked.Success, asked.Message);
            Assert.IsFalse(asked.Data, "nothing was deleted");
            Assert.AreEqual(AccountServiceMessages.DeleteNeedsApproval, asked.Message);
            Assert.IsFalse(h.Query(uow => uow.Set<User>().Single(u => u.Id == p2).IsDeleted));
            Assert.IsNull(await h.TokenProblemAsync(session.AccessToken), "his session goes on");
            Assert.IsTrue(InTrail(h, "Identity.User", "RevokeRequested", a => a.UserId == h.AdminId && a.EntityId == p2));
            Assert.IsTrue(Told(h, p2, AccountServiceMessages.RevokeRequestedInbox));

            ResponseDto<bool> twice = await h.As(h.Admin, s => s.DeleteUserAsync(p2, default));
            Assert.IsFalse(twice.Success);
            Assert.AreEqual(AccountServiceMessages.DeleteAlreadyPending, twice.Message);

            RoleGrantDto listed = (await GrantsAsync(h, Pmo(p3))).Single(g => g.UserId == p2);
            Assert.AreEqual("revoke", listed.Kind);
            Assert.AreEqual(IdentityRules.DeleteRoleLabel, listed.Role);
            Assert.IsTrue(listed.CanApprove, listed.CannotApproveReason);
            Assert.AreEqual(AccountServiceMessages.GrantSameApprover, (await ApproveAsync(h, h.Admin, listed.Id)).Message);
            Assert.AreEqual(AccountServiceMessages.CannotRejectOwnRequest, (await h.As(Pmo(p2, p2Name), s => s.RejectRoleGrantAsync(listed.Id, default).AsTask())).Message);

            ResponseDto<RoleGrantDto> approved = await ApproveAsync(h, Pmo(p3), listed.Id);
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(AccountServiceMessages.RevokeApproved, approved.Message);
            Assert.IsTrue(h.Query(uow => uow.Set<User>().Single(u => u.Id == p2).IsDeleted));
            Assert.IsNotNull(await h.TokenProblemAsync(session.AccessToken), "the deleted user's token ends at once");
            Assert.IsFalse((await LoginAsync(h, p2Name)).Success);
            Assert.IsFalse((await RefreshAsync(h, session.RefreshToken)).Success);
            Assert.IsTrue(InTrail(h, "Identity.User", "Deleted", a => a.UserId == p3 && a.EntityId == p2), "the approver is the actor of the deletion");

            // any other PMO user may reject (take back) such a request
            Assert.IsFalse((await h.As(h.Admin, s => s.DeleteUserAsync(p3, default))).Data);
            RoleGrantDto second = (await GrantsAsync(h, Pmo(p4))).Single(g => g.UserId == p3);
            Assert.IsTrue((await h.As(Pmo(p4), s => s.RejectRoleGrantAsync(second.Id, default).AsTask())).Success);
            Assert.IsFalse(h.Query(uow => uow.Set<User>().Single(u => u.Id == p3).IsDeleted));
            Assert.IsFalse((await GrantsAsync(h, Pmo(p4))).Any(g => g.Id == second.Id));
        }

        [TestMethod]
        public async Task A_Request_To_Take_The_Role_Away_Or_To_Delete_Nobody_Could_Approve_Is_Not_Made()
        {
            var h = new Host(Unique("revoke"));
            int p2 = await h.AddPmoAsync(Unique("p2")); // the administrator and p2 are the only PMO users
            ResponseDto<bool> delete = await h.As(h.Admin, s => s.DeleteUserAsync(p2, default));
            Assert.IsFalse(delete.Success);
            Assert.AreEqual(AccountServiceMessages.RevokeNoApprover, delete.Message);
            ResponseDto<ChangeRoleResultDto> revoke = await SetRolesAsync(h, h.Admin, p2, WarehouseRole);
            Assert.IsFalse(revoke.Success);
            Assert.AreEqual(AccountServiceMessages.RevokeNoApprover, revoke.Message);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(p2), "the check comes before any change: nothing was given or taken");
            Assert.AreEqual(0, (await GrantsAsync(h, h.Admin)).Count);

            // a PMO user who is not active is not an approver either
            int p3 = await h.AddPmoAsync(Unique("p3"));
            h.Write(uow => uow.Set<User>().Single(u => u.Id == p3).IsActive = false);
            Assert.AreEqual(AccountServiceMessages.RevokeNoApprover, (await h.As(h.Admin, s => s.DeleteUserAsync(p2, default))).Message);
        }

        [TestMethod]
        public async Task A_Pmo_User_May_Step_Down_Himself_Unless_He_Is_The_Last_Active_One()
        {
            var h = new Host(Unique("step"));
            string p2Name = Unique("p2");
            int p2 = await h.AddPmoAsync(p2Name);
            int p3 = await h.AddPmoAsync(Unique("p3"));

            // a request to take his role away is open; stepping down makes it moot
            int pendingId = (await SetRolesAsync(h, h.Admin, p2, WarehouseRole)).Data.GrantId.Value;
            ResponseLoginUserDto session = await SignInAsync(h, p2Name);
            ResponseDto<ChangeRoleResultDto> step = await SetRolesAsync(h, Pmo(p2, p2Name), p2, WarehouseRole);
            Assert.IsTrue(step.Success, step.Message);
            Assert.AreEqual(AccountServiceMessages.SteppedDown, step.Message);
            Assert.AreEqual("WAREHOUSE", step.Data.Roles);
            Assert.IsFalse(step.Data.Pending);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(p2));
            Assert.IsTrue(InTrail(h, "Identity.Roles", "SteppedDown", a => a.UserId == p2 && a.EntityId == p2));
            Assert.IsNotNull(await h.TokenProblemAsync(session.AccessToken), "the access token that carried the PMO role ends");
            Assert.IsFalse((await GrantsAsync(h, Pmo(p3))).Any(g => g.Id == pendingId), "the request to take the role away is moot");
            Assert.IsTrue(RecordOf(h, p2).IsRemoved, "and the record of the role stays, marked as removed");

            // the edit-user screen does the same, and saves the other details as well
            ResponseDto<ResponseUserDto> viaEdit = await h.As(Pmo(p3, "p3"), s => s.UpdateUserAsync(Edit(p3, "p3-new@example.com", new int[0]), default).AsTask());
            Assert.IsTrue(viaEdit.Success, viaEdit.Message);
            Assert.AreEqual(AccountServiceMessages.SteppedDown, viaEdit.Message);
            Assert.AreEqual(0, h.RolesOf(p3).Length);
            Assert.AreEqual("p3-new@example.com", h.Query(uow => uow.Set<User>().Single(u => u.Id == p3).Email));

            // the administrator is the last active PMO user now: he cannot step down, by either screen, and the refusal is in the trail
            ResponseDto<ChangeRoleResultDto> last = await SetRolesAsync(h, h.Admin, h.AdminId);
            Assert.IsFalse(last.Success);
            Assert.AreEqual(AccountServiceMessages.LastPmoCannotStepDown, last.Message);
            ResponseDto<ResponseUserDto> lastEdit = await h.As(h.Admin, s => s.UpdateUserAsync(Edit(h.AdminId, "admin-new@example.com", new int[0]), default).AsTask());
            Assert.IsFalse(lastEdit.Success);
            Assert.AreEqual(AccountServiceMessages.LastPmoCannotStepDown, lastEdit.Message);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(h.AdminId));
            Assert.AreNotEqual("admin-new@example.com", h.Query(uow => uow.Set<User>().Single(u => u.Id == h.AdminId).Email), "nothing of the refused edit was saved");
            Assert.IsTrue(InTrail(h, "Identity.Access", "Refused", a => a.UserId == h.AdminId && a.EntityId == h.AdminId));
        }

        [TestMethod]
        public async Task A_Seat_Does_Not_Make_The_Last_Real_Pmo_User_Able_To_Step_Down()
        {
            var h = new Host(Unique("step"));
            AddSeatWithPmo(h);
            ResponseDto<ChangeRoleResultDto> last = await SetRolesAsync(h, h.Admin, h.AdminId);
            Assert.IsFalse(last.Success);
            Assert.AreEqual(AccountServiceMessages.LastPmoCannotStepDown, last.Message);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(h.AdminId));
        }

        // ---------------------------------------------------------------------------------------------------- M-2: the receiver chooses his own password first

        [TestMethod]
        public async Task The_Approval_Of_A_Sensitive_Role_Waits_Until_The_Receiver_Has_Chosen_A_Password_Of_His_Own()
        {
            var h = new Host(Unique("own"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            string name = Unique("fin");
            int id = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole, FinanceManagerRole });
            RoleGrantDto grant = (await GrantsAsync(h, Pmo(p2))).Single(g => g.UserId == id);

            // the PMO user who registered him chose his password, so nobody can approve the role yet
            Assert.IsFalse(grant.CanApprove);
            Assert.AreEqual(AccountServiceMessages.GrantReceiverMustChangeOwnPassword, grant.CannotApproveReason);
            ResponseDto<RoleGrantDto> early = await ApproveAsync(h, Pmo(p2), grant.Id);
            Assert.IsFalse(early.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, early.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.GrantReceiverMustChangeOwnPassword, early.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(id));
            Assert.IsTrue(InTrail(h, "Identity.Access", "Refused", a => a.UserId == p2 && a.EntityId == id), "the refused approval is in the trail");
            Assert.IsTrue(Told(h, id, AccountServiceMessages.GrantNeedsOwnPasswordInbox), "the receiver is told in his inbox why the role waits");

            // a wrong current password changes nothing; with the right one he chooses his own and the role can be approved
            ResponseDto<ResponseLoginUserDto> wrong = await h.As(Signed(id, name, "WAREHOUSE"),
                s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = "Wrong-Pass-2026", NewPassword = ChosenPassword }, default).AsTask());
            Assert.IsFalse(wrong.Success);
            Assert.IsFalse((await GrantsAsync(h, Pmo(p2))).Single(g => g.Id == grant.Id).CanApprove);
            await ChoosePasswordAsync(h, id, name);
            RoleGrantDto ready = (await GrantsAsync(h, Pmo(p2))).Single(g => g.Id == grant.Id);
            Assert.IsTrue(ready.CanApprove, ready.CannotApproveReason);
            Assert.IsNull(ready.CannotApproveReason);
            ResponseDto<RoleGrantDto> approved = await ApproveAsync(h, Pmo(p2), grant.Id);
            Assert.IsTrue(approved.Success, approved.Message);
            CollectionAssert.AreEqual(new[] { "FINANCEMANAGER", "WAREHOUSE" }, h.RolesOf(id));
        }

        [TestMethod]
        public async Task A_Password_A_Pmo_User_Set_Closes_The_Gate_Again_Until_The_Receiver_Has_Changed_It()
        {
            var h = new Host(Unique("own"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            string name = Unique("u");
            int id = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole, CeoRole });
            RoleGrantDto grant = (await GrantsAsync(h, Pmo(p2))).Single(g => g.UserId == id);
            await ChoosePasswordAsync(h, id, name);
            Assert.IsTrue((await GrantsAsync(h, Pmo(p2))).Single(g => g.Id == grant.Id).CanApprove);

            // an administrator sets a temporary password (the user is an ordinary one so far: at once); the PMO user knows it
            ResponseDto<AdminResetPasswordResultDto> reset = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id }, default).AsTask());
            Assert.IsTrue(reset.Success, reset.Message);
            Assert.IsFalse(reset.Data.Pending);
            RoleGrantDto closed = (await GrantsAsync(h, Pmo(p2))).Single(g => g.Id == grant.Id);
            Assert.IsFalse(closed.CanApprove);
            Assert.AreEqual(AccountServiceMessages.GrantReceiverMustChangeOwnPassword, closed.CannotApproveReason);
            Assert.IsFalse((await ApproveAsync(h, Pmo(p2), grant.Id)).Success);

            // he finishes the temporary password with one of his own: open again
            ResponseDto<ResponseLoginUserDto> chosen = await h.As(null, s => s.ChangeTemporaryPasswordAsync(
                new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = reset.Data.TemporaryPassword, NewPassword = "Brand-New-2029w" }, default).AsTask());
            Assert.IsTrue(chosen.Success, chosen.Message);
            Assert.IsTrue((await GrantsAsync(h, Pmo(p2))).Single(g => g.Id == grant.Id).CanApprove);
            Assert.IsTrue((await ApproveAsync(h, Pmo(p2), grant.Id)).Success);
            CollectionAssert.AreEqual(new[] { "CEO", "WAREHOUSE" }, h.RolesOf(id));
        }

        [TestMethod]
        public async Task The_Gate_Is_For_Roles_Only_A_Removal_Or_A_Reset_Does_Not_Wait_For_The_Target_To_Change_His_Password()
        {
            var h = new Host(Unique("own"));
            int p2 = await h.AddPmoAsync(Unique("p2")); // registered by the administrator; neither p2 nor p4 ever changed the password the administrator chose
            int p3 = await h.AddPmoAsync(Unique("p3"));
            int p4 = await h.AddPmoAsync(Unique("p4"));

            int removal = (await SetRolesAsync(h, h.Admin, p2, WarehouseRole)).Data.GrantId.Value;
            int reset = (await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = p4 }, default).AsTask())).Data.GrantId.Value;
            List<RoleGrantDto> listed = await GrantsAsync(h, Pmo(p3));
            Assert.IsTrue(listed.Single(g => g.Id == removal).CanApprove, "a removal does not wait for the target");
            Assert.IsTrue(listed.Single(g => g.Id == reset).CanApprove, "nor does a reset");
            Assert.IsTrue((await ApproveAsync(h, Pmo(p3), removal)).Success);
            Assert.IsTrue((await ApproveAsync(h, Pmo(p3), reset)).Success);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(p2));
        }

        [TestMethod]
        public async Task A_Reset_Is_Still_A_Request_For_A_Week_After_A_Sensitive_Role_Was_Taken_Away()
        {
            var h = new Host(Unique("recent"));
            int p2 = await h.AddPmoAsync(Unique("p2")); // an approver for the requests below
            string name = Unique("fin");
            int fin = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole, FinanceManagerRole });
            await ChoosePasswordAsync(h, fin, name);
            Assert.IsTrue((await ApproveAsync(h, Pmo(p2), (await GrantsAsync(h, Pmo(p2))).Single(g => g.UserId == fin).Id)).Success);
            Assert.IsFalse(RecordOf(h, fin, "FINANCEMANAGER").IsRemoved, "a role that is held has a record that is not marked");

            // the role is taken away at once, as before: the record stays, marked
            Assert.IsTrue((await SetRolesAsync(h, h.Admin, fin, WarehouseRole)).Success);
            RoleGrantRecord marked = RecordOf(h, fin, "FINANCEMANAGER");
            Assert.IsTrue(marked.IsRemoved);
            Assert.AreEqual(h.AdminId, marked.GrantedBy, "who asked for it is still known");
            Assert.AreEqual(p2, marked.ApprovedBy);

            // a reset of this user is a request, as if he still held the role; the ordinary reset is for a user who held none
            ResponseDto<AdminResetPasswordResultDto> request = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = fin }, default).AsTask());
            Assert.IsTrue(request.Success, request.Message);
            Assert.IsTrue(request.Data.Pending);
            Assert.AreEqual(AccountServiceMessages.ResetNeedsApproval, request.Message);
            Assert.IsNull(request.Data.TemporaryPassword);
            Assert.IsTrue((await h.As(h.Admin, s => s.RejectRoleGrantAsync(request.Data.GrantId.Value, default).AsTask())).Success);

            // a week after the removal the reset is immediate again
            h.Write(uow =>
            {
                foreach (UserClaim c in uow.Set<UserClaim>().Where(c => c.UserId == fin && c.ClaimType == IdentityRules.RoleGrantedClaimType).ToList())
                    if (RoleGrantRecord.TryParse(c.ClaimValue, out RoleGrantRecord r) && r.IsRemoved)
                    {
                        r.RemovedAt = DateTimeOffset.Now.AddDays(-8);
                        c.ClaimValue = r.ToClaimValue();
                    }
            });
            ResponseDto<AdminResetPasswordResultDto> direct = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = fin }, default).AsTask());
            Assert.IsTrue(direct.Success, direct.Message);
            Assert.IsFalse(direct.Data.Pending);
            Assert.IsFalse(string.IsNullOrEmpty(direct.Data.TemporaryPassword));

            // a sensitive role that has no record (given before the records existed) leaves a marked one when it is taken away
            int old = await h.RegisterAsync(h.Admin, Unique("old"), new[] { WarehouseRole });
            h.Write(uow => uow.Set<UserRole>().Add(new UserRole { UserId = old, RoleId = FinanceManagerRole }));
            Assert.IsNull(RecordOf(h, old, "FINANCEMANAGER"));
            Assert.IsTrue((await SetRolesAsync(h, h.Admin, old, WarehouseRole)).Success);
            RoleGrantRecord tombstone = RecordOf(h, old, "FINANCEMANAGER");
            Assert.IsNotNull(tombstone);
            Assert.IsTrue(tombstone.IsRemoved);
            Assert.IsTrue(tombstone.Legacy);
            Assert.AreEqual(0, tombstone.GrantedBy);
            ResponseDto<AdminResetPasswordResultDto> oldRequest = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = old }, default).AsTask());
            Assert.IsTrue(oldRequest.Data.Pending, "the reset of a former holder without a record is a request too");
        }

        [TestMethod]
        public async Task A_Pmo_User_Who_Stepped_Down_Is_Handled_Like_A_Pmo_User_When_His_Password_Is_Reset()
        {
            var h = new Host(Unique("recent"));
            string p2Name = Unique("p2");
            int p2 = await h.AddPmoAsync(p2Name);
            await h.AddPmoAsync(Unique("p3"));
            Assert.IsTrue((await SetRolesAsync(h, Pmo(p2, p2Name), p2)).Success, "a step-down");
            Assert.AreEqual(0, h.RolesOf(p2).Length);

            ResponseDto<AdminResetPasswordResultDto> request = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = p2 }, default).AsTask());
            Assert.IsTrue(request.Success, request.Message);
            Assert.IsTrue(request.Data.Pending, "he held the PMO role less than a week ago");
        }

        // ---------------------------------------------------------------------------------------------------- L-1: the recovery from the server itself has a cap

        [TestMethod]
        public async Task More_Than_Ten_Wrong_Passwords_From_The_Server_End_The_Recovery_For_That_User()
        {
            LoopbackFailureTally.Reset();
            try
            {
                var h = new Host(Unique("cap"));
                string withinName = Unique("pmo");
                string beyondName = Unique("pmo");
                int withinId = await h.AddPmoAsync(withinName);
                int beyondId = await h.AddPmoAsync(beyondName);

                // ten wrong guesses are still inside the cap: the right password opens the locked account from the server
                for (int i = 0; i < IdentityRules.LoopbackFailureCap; i++)
                    await LoginAsync(h, withinName, "Wrong-Pass-" + i + "xx", IPAddress.Loopback);
                Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, (await LoginAsync(h, withinName)).Message, "the guesses locked the account for the network");
                Assert.IsTrue((await LoginAsync(h, withinName, GoodPassword, IPAddress.Loopback)).Success, "the cap holds, the person at the server gets in");
                Assert.AreEqual(0, Trail(h).Count(a => a.Action == "LoopbackCapped" && a.EntityId == withinId));

                // one more than the cap: the recovery is over for him, the server is held back by the lock like anybody else
                for (int i = 0; i <= IdentityRules.LoopbackFailureCap; i++)
                    await LoginAsync(h, beyondName, "Wrong-Pass-" + i + "xx", IPAddress.Loopback);
                Assert.AreEqual(1, Trail(h).Count(a => a.Action == "LoopbackCapped" && a.EntityId == beyondId), "one row, when the cap was passed");
                ResponseDto<ResponseLoginUserDto> held = await LoginAsync(h, beyondName, GoodPassword, IPAddress.Loopback);
                Assert.IsFalse(held.Success);
                Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, held.Message);
                await LoginAsync(h, beyondName, "Wrong-Pass-2026", IPAddress.Loopback);
                await LoginAsync(h, beyondName, "Wrong-Pass-2027", IPAddress.Loopback);
                Assert.AreEqual(1, Trail(h).Count(a => a.Action == "LoopbackCapped" && a.EntityId == beyondId), "and no row for every attempt after it");

                // the other PMO user is not touched by his count, and once the window has passed the recovery is open again
                Assert.IsTrue((await LoginAsync(h, withinName, GoodPassword, IPAddress.Loopback)).Success);
                LoopbackFailureTally.Reset();
                Assert.IsTrue((await LoginAsync(h, beyondName, GoodPassword, IPAddress.Loopback)).Success, "the window passed");
                Assert.IsTrue(InTrail(h, "Identity.SignIn", "LoopbackSignIn", a => a.EntityId == beyondId));
            }
            finally
            {
                LoopbackFailureTally.Reset();
            }
        }

        // ---------------------------------------------------------------------------------------------------- L-3 and L-4

        [TestMethod]
        public async Task Sign_Ins_With_The_Name_Of_A_Seat_Are_Counted_Like_Unknown_Names_And_Never_Reach_The_Trail_Whole()
        {
            var h = new Host(Unique("l3"));
            UnknownUserTally.Reset();
            try
            {
                for (int i = 0; i < 25; i++)
                {
                    ResponseDto<ResponseLoginUserDto> r = await LoginAsync(h, "seat.ceo-brief", "x");
                    Assert.IsFalse(r.Success);
                    Assert.AreEqual(AccountServiceMessages.InvalidLoginData, r.Message);
                }
                List<FnAuditLog> rows = Trail(h).Where(a => a.EntityType == "Identity.SignIn" && (a.Summary ?? "").Contains("نام کاربری سرویس")).ToList();
                Assert.IsTrue(rows.Count >= 1 && rows.Count < 8, rows.Count + " rows for 25 attempts");
                Assert.IsFalse(Trail(h).Any(a => a.Action == "Blocked"), "no row of its own for every attempt");
                Assert.IsTrue(rows.All(a => a.UserId == 0 && a.EntityId == null));
                Assert.IsFalse(rows.Any(a => a.Summary.Contains("ceo-brief")), "only the length and the first two characters");
                StringAssert.Contains(rows[0].Summary, "«se»");
                StringAssert.Contains(rows[0].Summary, "14 ");
            }
            finally
            {
                UnknownUserTally.Reset();
            }
        }

        [TestMethod]
        public async Task The_Target_Of_A_Reset_Cannot_Reject_It_But_Any_Other_Pmo_User_Can_And_A_Role_For_Himself_He_May()
        {
            var h = new Host(Unique("l4"));
            string p2Name = Unique("p2");
            int p2 = await h.AddPmoAsync(p2Name);
            int p3 = await h.AddPmoAsync(Unique("p3"));

            ResponseDto<AdminResetPasswordResultDto> request = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = p2 }, default).AsTask());
            Assert.IsTrue(request.Success && request.Data.Pending, request.Message);
            int grantId = request.Data.GrantId.Value;

            ResponseDto<bool> own = await h.As(Pmo(p2, p2Name), s => s.RejectRoleGrantAsync(grantId, default).AsTask());
            Assert.IsFalse(own.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, own.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.CannotRejectOwnRequest, own.Message);
            Assert.IsTrue((await GrantsAsync(h, Pmo(p3))).Any(g => g.Id == grantId), "the request is still there");
            Assert.IsTrue(InTrail(h, "Identity.Access", "Refused", a => a.UserId == p2 && a.EntityId == p2));

            // a request to give him a role is different: rejecting it only takes power away
            ResponseDto<ChangeRoleResultDto> roleRequest = await SetRolesAsync(h, h.Admin, p2, PmoRole, CeoRole);
            Assert.IsTrue(roleRequest.Data.Pending);
            Assert.IsTrue((await h.As(Pmo(p2, p2Name), s => s.RejectRoleGrantAsync(roleRequest.Data.GrantId.Value, default).AsTask())).Success);

            // any other PMO user may reject the reset
            Assert.IsTrue((await h.As(Pmo(p3), s => s.RejectRoleGrantAsync(grantId, default).AsTask())).Success);
            Assert.IsFalse((await GrantsAsync(h, Pmo(p3))).Any(g => g.Id == grantId));
        }

        // ---------------------------------------------------------------------------------------------------- L-5: records for the PMO users of before R1a2

        [TestMethod]
        public async Task The_First_Start_After_The_Upgrade_Writes_A_Legacy_Record_For_Every_Real_Pmo_User_Who_Has_None()
        {
            // an installation of before R1a2: the step is off while its users are made, then it runs at the "first start after the upgrade"
            var h = new Host(Unique("legacy"), new Dictionary<string, string> { ["Identity:LegacyPmoRecordsAtUpgrade"] = "false" });
            Assert.AreEqual(0, Trail(h).Count(a => a.Action == "LegacyPmoRecord"), "switched off: not even the marker");
            string old1Name = Unique("old1");
            int old1 = await h.AddPmoAsync(old1Name);
            int old2 = await h.AddPmoAsync(Unique("old2"));
            int recorded = await h.AddPmoAsync(Unique("rec"), grantedBy: old1, ageDays: 3); // has a record already: left alone
            int seat = AddSeatWithPmo(h);                                                    // an AI seat: none
            int gone = await h.AddPmoAsync(Unique("gone"));
            h.Write(uow => uow.Set<User>().Single(u => u.Id == gone).IsDeleted = true);       // a deleted user: none

            h.Configuration["Identity:LegacyPmoRecordsAtUpgrade"] = "true";
            RunSeed(h);

            foreach (int id in new[] { h.AdminId, old1, old2 })
            {
                RoleGrantRecord legacy = RecordOf(h, id);
                Assert.IsNotNull(legacy, "user " + id);
                Assert.IsTrue(legacy.Legacy);
                Assert.AreEqual(0, legacy.GrantedBy);
                Assert.IsFalse(legacy.Bootstrap);
                Assert.IsFalse(legacy.IsRemoved);
            }
            Assert.IsNull(RecordOf(h, seat), "an AI seat gets none");
            Assert.IsNull(RecordOf(h, gone), "nor a deleted user");
            RoleGrantRecord kept = RecordOf(h, recorded);
            Assert.IsFalse(kept.Legacy, "a record that exists is left alone");
            Assert.AreEqual(old1, kept.GrantedBy);

            // the trail: one row per user and one summary row without a user, which is the marker
            List<FnAuditLog> rows = Trail(h).Where(a => a.Action == "LegacyPmoRecord").ToList();
            CollectionAssert.AreEquivalent(new[] { h.AdminId, old1, old2 }, rows.Where(a => a.EntityId != null).Select(a => a.EntityId.Value).ToArray());
            FnAuditLog marker = rows.Single(a => a.EntityId == null);
            StringAssert.Contains(marker.Summary, "3 مدیر");
            StringAssert.Contains(marker.Summary, old1Name);
            Assert.IsTrue(rows.All(a => a.UserId == 0));

            // it ran once
            RunSeed(h);
            Assert.AreEqual(rows.Count, Trail(h).Count(a => a.Action == "LegacyPmoRecord"));
            Assert.AreEqual(1, h.Query(uow => uow.Set<UserClaim>().Count(c => c.UserId == old1 && c.ClaimType == IdentityRules.RoleGrantedClaimType)));

            // a legacy record is not "too new" although it was written a moment ago
            string userName = Unique("u");
            int user = await h.RegisterAsync(h.Admin, userName, new[] { WarehouseRole, CeoRole });
            await ChoosePasswordAsync(h, user, userName);
            RoleGrantDto grant = (await GrantsAsync(h, Pmo(old1, old1Name))).Single(g => g.UserId == user);
            Assert.IsTrue(grant.CanApprove, grant.CannotApproveReason);
            Assert.IsTrue((await ApproveAsync(h, Pmo(old1, old1Name), grant.Id)).Success);
            Assert.AreEqual(old1, RecordOf(h, user, "CEO").ApprovedBy);
        }

        [TestMethod]
        public void A_New_Installation_Gets_The_Legacy_Marker_Only_And_The_Step_Is_On_By_Default()
        {
            Assert.IsTrue(new IdentitySecurityOptions().LegacyPmoRecordsAtUpgrade);
            var h = new Host(Unique("fresh"));
            List<FnAuditLog> rows = Trail(h).Where(a => a.Action == "LegacyPmoRecord").ToList();
            Assert.AreEqual(1, rows.Count);
            Assert.IsNull(rows[0].EntityId);
            StringAssert.Contains(rows[0].Summary, "نصب تازه");
            Assert.IsNull(RecordOf(h, h.AdminId), "the seeded administrator needs none: no record means held for long, nobody's doing");
        }

        // ---------------------------------------------------------------------------------------------------- L-9: the answers say which requests wait

        [TestMethod]
        public async Task The_Answers_Of_Register_Update_And_Edit_Roles_Say_Which_Requests_Wait_For_A_Second_Pmo_User()
        {
            var h = new Host(Unique("l9"));
            ResponseDto<ResponseUserDto> created = await h.As(h.Admin, s => s.RegisterAsync(NewUser(Unique("u"), new[] { WarehouseRole, CeoRole, FinanceManagerRole }, GoodPassword), default).AsTask());
            Assert.IsTrue(created.Success, created.Message);
            Assert.IsTrue(created.Data.Pending);
            Assert.AreEqual(2, created.Data.GrantIds.Length);
            Assert.AreEqual((int?)created.Data.GrantIds[0], created.Data.GrantId);
            int id = created.Data.Id;
            CollectionAssert.AreEquivalent((await GrantsAsync(h, h.Admin)).Where(g => g.UserId == id).Select(g => g.Id).ToArray(), created.Data.GrantIds);

            // an update that keeps both requests reports them again
            ResponseDto<ResponseUserDto> kept = await h.As(h.Admin, s => s.UpdateUserAsync(Edit(id, "kept@example.com", new[] { WarehouseRole, CeoRole, FinanceManagerRole }), default).AsTask());
            Assert.IsTrue(kept.Success, kept.Message);
            Assert.IsTrue(kept.Data.Pending);
            CollectionAssert.AreEqual(created.Data.GrantIds, kept.Data.GrantIds);

            // edit-user-roles: the roles the user holds now, and the requests that are left
            ResponseDto<ChangeRoleResultDto> one = await SetRolesAsync(h, h.Admin, id, WarehouseRole, CeoRole);
            Assert.IsTrue(one.Success, one.Message);
            Assert.AreEqual("WAREHOUSE", one.Data.Roles);
            Assert.IsTrue(one.Data.Pending);
            Assert.AreEqual(1, one.Data.GrantIds.Length);
            Assert.AreEqual((int?)one.Data.GrantIds[0], one.Data.GrantId);
            ResponseDto<ChangeRoleResultDto> none = await SetRolesAsync(h, h.Admin, id, WarehouseRole);
            Assert.IsFalse(none.Data.Pending);
            Assert.IsNull(none.Data.GrantId);
            Assert.AreEqual(0, none.Data.GrantIds.Length);

            // a plain read does not fill them
            ResponseDto<ResponseUserDto> read = await h.As(h.Admin, s => s.GetResponseUserDtoByIdAsync(new GetUserDto { Id = id }, default).AsTask());
            Assert.IsFalse(read.Data.Pending);
            Assert.IsNull(read.Data.GrantId);
            Assert.IsNull(read.Data.GrantIds);
        }
    }
}
