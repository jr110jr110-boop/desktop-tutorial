using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Common.Extentions;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Identity.Constants;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Contracts.Identity;
using Services.Identity.AccountServices;
using Services.Resources;
using Services.Token;
using ViewModels.Identity.UserDtos;
using WebApi.Extensions;

namespace MsTests
{
    /// <summary>
    /// R1a2: the tests of the second security review. Independent approvers and the bootstrap of the second PMO user (H1), the security stamp that ends access tokens at once (H2),
    /// one-use refresh tokens (L3), sign out, the answers that do not tell which names exist (L2), what the audit trail keeps of an unknown name (M5), the lock-out recovery (M1),
    /// temporary passwords that run out (L7), the seeded administrator and the forced change at an upgrade (M7), the keyed hash of masked values (M4).
    /// Every test that depends on the number of PMO users or on the sessions of a user runs on a host of its own.
    /// </summary>
    public partial class IdentityTests
    {
        // ---------------------------------------------------------------------------------------------------- helpers

        private static Task<ResponseDto<ResponseLoginUserDto>> LoginAsync(Host h, string name, string password = GoodPassword, IPAddress from = null, string forwardedFor = null) =>
            h.From(from ?? ClientIp, null, s => s.LoginAsync(new LoginUserDto { UserName = name, Password = password }, default).AsTask(), forwardedFor);

        private static async Task<ResponseLoginUserDto> SignInAsync(Host h, string name, string password = GoodPassword)
        {
            ResponseDto<ResponseLoginUserDto> r = await LoginAsync(h, name, password);
            Assert.IsTrue(r.Success, name + ": " + r.Message);
            return r.Data;
        }

        private static Task<ResponseDto<ResponseLoginUserDto>> RefreshAsync(Host h, string refreshToken) =>
            h.As(null, s => s.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = refreshToken }, default).AsTask());

        private static Task<ResponseDto<RoleGrantDto>> ApproveAsync(Host h, ClaimsPrincipal who, int grantId) =>
            h.As(who, s => s.ApproveRoleGrantAsync(grantId, default).AsTask());

        private static async Task<List<RoleGrantDto>> GrantsAsync(Host h, ClaimsPrincipal who)
        {
            ResponseDto<List<RoleGrantDto>> r = await h.As(who, s => s.GetRoleGrantsAsync(default).AsTask());
            Assert.IsTrue(r.Success, r.Message);
            return r.Data;
        }

        private static List<FnAuditLog> Trail(Host h) => h.Query(uow => uow.Set<FnAuditLog>().AsNoTracking().OrderBy(a => a.Id).ToList());

        private static bool InTrail(Host h, string type, string action, Func<FnAuditLog, bool> where = null) =>
            Trail(h).Any(a => a.EntityType == type && a.Action == action && (where == null || where(a)));

        private static ClaimsPrincipal Pmo(int id, string name = "pmo") => Signed(id, name, "PMO");

        private static Task<ResponseDto<string>> SetRolesAsync(Host h, ClaimsPrincipal by, int userId, params int[] roleIds) =>
            h.As(by, s => s.ChangeRoleAsync(new ChangeRoleDto { UserId = userId, RoleIds = roleIds }, default).AsTask());

        // ---------------------------------------------------------------------------------------------------- H1: the bootstrap of the second PMO user

        [TestMethod]
        public async Task The_Second_Pmo_User_Is_Given_At_Once_With_One_Audit_Row_And_An_Alert_To_The_Ceo()
        {
            var h = new Host(Unique("boot"));
            string second = Unique("pmo");
            ResponseDto<ResponseUserDto> created = await h.As(h.Admin, s => s.RegisterAsync(NewUser(second, new[] { PmoRole }, GoodPassword), default).AsTask());
            Assert.IsTrue(created.Success, created.Message);
            Assert.AreEqual(AccountServiceMessages.PmoBootstrapApplied, created.Message);
            int secondId = created.Data.Id;
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(secondId), "fewer than two active PMO users: given at once");

            FnAuditLog row = Trail(h).Single(a => a.EntityType == "Identity.Roles" && a.Action == "Bootstrap");
            Assert.AreEqual(secondId, row.EntityId);
            Assert.AreEqual(h.AdminId, row.UserId);
            Assert.AreEqual("10.1.2.3", row.Ip);

            OfInboxItem alert = h.Query(uow => uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceModule == "Identity").ToList()).Single(i => i.RecipientRole == "CEO");
            StringAssert.Contains(alert.Title, second);
            Assert.AreEqual(OfInboxKind.Alert, alert.Kind);

            // the record that the approval rules read: made by the bootstrap
            UserClaim record = h.Query(uow => uow.Set<UserClaim>().AsNoTracking().Single(c => c.UserId == secondId && c.ClaimType == IdentityRules.RoleGrantedClaimType));
            Assert.IsTrue(RoleGrantRecord.TryParse(record.ClaimValue, out RoleGrantRecord parsed));
            Assert.IsTrue(parsed.Bootstrap);
            Assert.AreEqual(h.AdminId, parsed.GrantedBy);

            // two active PMO users now exist: the next PMO grant is a request that another PMO user must approve
            ResponseDto<ResponseUserDto> next = await h.As(h.Admin, s => s.RegisterAsync(NewUser(Unique("pmo"), new[] { PmoRole }, GoodPassword), default).AsTask());
            Assert.IsTrue(next.Success, next.Message);
            Assert.AreEqual(0, h.RolesOf(next.Data.Id).Length, "no second bootstrap");
            Assert.AreEqual("PMO", (await GrantsAsync(h, h.Admin)).Single(g => g.UserId == next.Data.Id).Role);
            Assert.AreEqual(1, Trail(h).Count(a => a.Action == "Bootstrap"), "exactly one bootstrap row");
            Assert.AreEqual(1, h.Query(uow => uow.Set<OfInboxItem>().Count(i => i.SourceModule == "Identity" && i.RecipientRole == "CEO")), "and one alert");
        }

        [TestMethod]
        public async Task The_Bootstrap_Opens_Again_Only_When_Fewer_Than_Two_Active_Pmo_Users_Are_Left()
        {
            var h = new Host(Unique("boot"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            int third = (await h.As(h.Admin, s => s.RegisterAsync(NewUser(Unique("p3"), new[] { PmoRole }, GoodPassword), default).AsTask())).Data.Id;
            RoleGrantDto grant = (await GrantsAsync(h, Pmo(p2))).Single(g => g.UserId == third);
            Assert.IsFalse((await ApproveAsync(h, h.Admin, grant.Id)).Success, "the requester cannot approve");
            ResponseDto<RoleGrantDto> approved = await ApproveAsync(h, Pmo(p2), grant.Id);
            Assert.IsTrue(approved.Success, approved.Message);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(third));
            Assert.AreEqual(0, Trail(h).Count(a => a.Action == "Bootstrap"));

            // a PMO user who is not active does not count
            h.Write(uow =>
            {
                foreach (User u in uow.Set<User>().Where(u => u.Id == p2 || u.Id == third)) u.IsActive = false;
            });
            string again = Unique("p4");
            ResponseDto<ResponseUserDto> created = await h.As(h.Admin, s => s.RegisterAsync(NewUser(again, new[] { PmoRole }, GoodPassword), default).AsTask());
            Assert.IsTrue(created.Success, created.Message);
            CollectionAssert.AreEqual(new[] { "PMO" }, h.RolesOf(created.Data.Id), "only the administrator is active: the bootstrap applies again");
            Assert.AreEqual(1, Trail(h).Count(a => a.Action == "Bootstrap"));
        }

        [TestMethod]
        public async Task A_Fresh_Pmo_User_Approves_Only_After_The_Minimum_Days()
        {
            var h = new Host(Unique("days"));
            int pmo2 = (await h.As(h.Admin, s => s.RegisterAsync(NewUser(Unique("pmo"), new[] { PmoRole }, GoodPassword), default).AsTask())).Data.Id;
            int user = await h.RegisterAsync(h.Admin, Unique("fin"), new[] { WarehouseRole, FinanceManagerRole });
            RoleGrantDto grant = (await GrantsAsync(h, h.Admin)).Single(g => g.UserId == user);
            ClaimsPrincipal second = Pmo(pmo2, "pmo2");

            // Identity:ApproverMinDays is 1 by default: a PMO user of a minute ago is not an approver yet
            RoleGrantDto seen = (await GrantsAsync(h, second)).Single(g => g.Id == grant.Id);
            Assert.IsFalse(seen.CanApprove);
            Assert.AreEqual(AccountServiceMessages.GrantApproverTooNew, seen.CannotApproveReason);
            ResponseDto<RoleGrantDto> early = await ApproveAsync(h, second, grant.Id);
            Assert.IsFalse(early.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, early.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.GrantApproverTooNew, early.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(user));
            Assert.IsTrue(InTrail(h, "Identity.Access", "Refused", a => a.UserId == pmo2 && a.EntityId == user), "the refused approval is in the trail");

            h.AgeGrantRecord(pmo2, 2);
            Assert.IsTrue((await GrantsAsync(h, second)).Single(g => g.Id == grant.Id).CanApprove);
            ResponseDto<RoleGrantDto> late = await ApproveAsync(h, second, grant.Id);
            Assert.IsTrue(late.Success, late.Message);
            CollectionAssert.AreEqual(new[] { "FINANCEMANAGER", "WAREHOUSE" }, h.RolesOf(user));
        }

        [TestMethod]
        public async Task A_Pmo_User_Whose_Role_The_Requester_Gave_Cannot_Approve_What_The_Requester_Asks()
        {
            var h = new Host(Unique("indep"));
            int p2 = await h.AddPmoAsync(Unique("p2"));                             // long-standing, like the seeded administrator (no record)
            int p3 = await h.AddPmoAsync(Unique("p3"), grantedBy: p2, ageDays: 30); // his PMO role was given on the request of p2
            int p4 = await h.AddPmoAsync(Unique("p4"), grantedBy: h.AdminId, ageDays: 30);
            int user = await h.RegisterAsync(h.Admin, Unique("u"), new[] { WarehouseRole });

            Assert.IsTrue((await SetRolesAsync(h, Pmo(p2, "p2"), user, WarehouseRole, CeoRole)).Success);
            RoleGrantDto grant = (await GrantsAsync(h, Pmo(p3, "p3"))).Single(g => g.UserId == user);
            Assert.IsFalse(grant.CanApprove);
            Assert.AreEqual(AccountServiceMessages.GrantApproverGrantedByRequester, grant.CannotApproveReason);
            ResponseDto<RoleGrantDto> refused = await ApproveAsync(h, Pmo(p3, "p3"), grant.Id);
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.GrantApproverGrantedByRequester, refused.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(user));
            Assert.IsTrue(InTrail(h, "Identity.Access", "Refused", a => a.UserId == p3 && a.EntityId == user));

            // an independent PMO user can (p4's role was given on the request of somebody else), and so can the administrator
            Assert.IsTrue((await GrantsAsync(h, Pmo(p4, "p4"))).Single(g => g.Id == grant.Id).CanApprove);
            Assert.IsTrue((await GrantsAsync(h, h.Admin)).Single(g => g.Id == grant.Id).CanApprove);
            ResponseDto<RoleGrantDto> approved = await ApproveAsync(h, Pmo(p4, "p4"), grant.Id);
            Assert.IsTrue(approved.Success, approved.Message);
            CollectionAssert.AreEqual(new[] { "CEO", "WAREHOUSE" }, h.RolesOf(user));

            // who asked and who approved is kept with the role
            UserClaim record = h.Query(uow => uow.Set<UserClaim>().AsNoTracking().Single(c => c.UserId == user && c.ClaimType == IdentityRules.RoleGrantedClaimType));
            Assert.IsTrue(RoleGrantRecord.TryParse(record.ClaimValue, out RoleGrantRecord parsed));
            Assert.AreEqual("CEO", parsed.Role);
            Assert.AreEqual(p2, parsed.GrantedBy);
            Assert.AreEqual(p4, parsed.ApprovedBy);
            Assert.IsFalse(parsed.Bootstrap);
        }

        [TestMethod]
        public async Task The_Pmo_User_Made_By_The_Bootstrap_Is_Not_Held_To_The_Made_By_The_Requester_Rule()
        {
            var h = new Host(Unique("indep"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            int boot = await h.AddPmoAsync(Unique("boot"), grantedBy: p2, bootstrap: true, ageDays: 30);
            int user = await h.RegisterAsync(h.Admin, Unique("u"), new[] { WarehouseRole });
            Assert.IsTrue((await SetRolesAsync(h, Pmo(p2, "p2"), user, WarehouseRole, FinanceManagerRole)).Success);
            RoleGrantDto grant = (await GrantsAsync(h, Pmo(boot, "boot"))).Single(g => g.UserId == user);
            Assert.IsTrue(grant.CanApprove, grant.CannotApproveReason);
            Assert.IsTrue((await ApproveAsync(h, Pmo(boot, "boot"), grant.Id)).Success);
            CollectionAssert.AreEqual(new[] { "FINANCEMANAGER", "WAREHOUSE" }, h.RolesOf(user));
        }

        // ---------------------------------------------------------------------------------------------------- H1: the reset of a PMO user is a request

        [TestMethod]
        public async Task The_Reset_Of_A_Pmo_Or_Sensitive_Role_Holder_Is_A_Request_That_A_Different_Pmo_User_Approves()
        {
            var h = new Host(Unique("reset"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            string p2Name = h.Query(uow => uow.Set<User>().Single(u => u.Id == p2).UserName);

            // the administrator and p2 are the only PMO users: there is nobody to approve, so no request is made
            ResponseDto<AdminResetPasswordResultDto> nobody = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = p2 }, default).AsTask());
            Assert.IsFalse(nobody.Success);
            Assert.AreEqual(AccountServiceMessages.ResetNoApprover, nobody.Message);

            int p3 = await h.AddPmoAsync(Unique("p3"));
            ResponseDto<AdminResetPasswordResultDto> request = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = p2 }, default).AsTask());
            Assert.IsTrue(request.Success, request.Message);
            Assert.AreEqual(AccountServiceMessages.ResetNeedsApproval, request.Message);
            Assert.IsTrue(request.Data.Pending);
            Assert.IsFalse(request.Data.MustChangePassword);
            Assert.IsNull(request.Data.TemporaryPassword);
            Assert.IsTrue(request.Data.GrantId > 0);
            Assert.IsTrue((await LoginAsync(h, p2Name)).Success, "nothing was reset yet: the old password still works");

            // a second request is refused, and the user is told in the inbox
            ResponseDto<AdminResetPasswordResultDto> twice = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = p2 }, default).AsTask());
            Assert.IsFalse(twice.Success);
            Assert.AreEqual(AccountServiceMessages.ResetAlreadyPending, twice.Message);
            Assert.IsTrue(h.Query(uow => uow.Set<OfInboxItem>().Any(i => i.RecipientUserId == p2 && i.Title == AccountServiceMessages.ResetRequestedInbox)), "the target is told");
            Assert.IsTrue(InTrail(h, "Identity.Password", "ResetRequested", a => a.UserId == h.AdminId && a.EntityId == p2));

            // it is listed as a reset; the requester and the target cannot approve it
            RoleGrantDto listed = (await GrantsAsync(h, Pmo(p3, "p3"))).Single(g => g.Id == request.Data.GrantId);
            Assert.AreEqual("reset", listed.Kind);
            Assert.AreEqual(IdentityRules.ResetRoleLabel, listed.Role);
            Assert.IsTrue(listed.CanApprove);
            Assert.AreEqual(AccountServiceMessages.GrantSameApprover, (await ApproveAsync(h, h.Admin, listed.Id)).Message);
            Assert.AreEqual(AccountServiceMessages.GrantApproverIsTarget, (await ApproveAsync(h, Pmo(p2, "p2"), listed.Id)).Message);
            Assert.IsTrue((await LoginAsync(h, p2Name)).Success, "still nothing was reset");

            // the third PMO user approves: the temporary password is shown to him once, the old password and the sessions are gone
            ResponseLoginUserDto session = await SignInAsync(h, p2Name);
            ResponseDto<RoleGrantDto> approved = await ApproveAsync(h, Pmo(p3, "p3"), listed.Id);
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(AccountServiceMessages.ResetApproved, approved.Message);
            string temporary = approved.Data.TemporaryPassword;
            Assert.IsNull(PasswordPolicy.Problem(temporary));
            Assert.IsFalse((await LoginAsync(h, p2Name)).Success, "the old password is dead");
            Assert.IsFalse((await RefreshAsync(h, session.RefreshToken)).Success, "and so are the sessions");
            ResponseDto<ResponseLoginUserDto> viaTemporary = await LoginAsync(h, p2Name, temporary);
            Assert.IsFalse(viaTemporary.Success);
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, viaTemporary.Data.Code);
            Assert.IsFalse((await GrantsAsync(h, Pmo(p3, "p3"))).Any(g => g.Id == listed.Id), "the request is gone");
            Assert.IsTrue(h.Query(uow => uow.Set<OfInboxItem>().Any(i => i.RecipientUserId == p2 && i.Title == AccountServiceMessages.ResetDoneInbox)));
            Assert.IsTrue(InTrail(h, "Identity.Password", "AdminReset", a => a.UserId == p3 && a.EntityId == p2), "the approver is the actor of the reset");
            Assert.IsTrue((await h.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = p2Name, TemporaryPassword = temporary, NewPassword = "Brand-New-2029w" }, default).AsTask())).Success);

            // a holder of a sensitive role (not a PMO user) is the same; an ordinary user is reset at once; a request can be taken back
            int fin = await h.RegisterAsync(h.Admin, Unique("fin"), new[] { WarehouseRole });
            h.Write(uow => uow.Set<UserRole>().Add(new UserRole { UserId = fin, RoleId = FinanceManagerRole }));
            ResponseDto<AdminResetPasswordResultDto> finRequest = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = fin }, default).AsTask());
            Assert.IsTrue(finRequest.Success, finRequest.Message);
            Assert.IsTrue(finRequest.Data.Pending);
            Assert.IsTrue((await h.As(h.Admin, s => s.RejectRoleGrantAsync(finRequest.Data.GrantId.Value, default).AsTask())).Success);
            int plain = await h.RegisterAsync(h.Admin, Unique("plain"), new[] { WarehouseRole });
            ResponseDto<AdminResetPasswordResultDto> direct = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = plain }, default).AsTask());
            Assert.IsTrue(direct.Success, direct.Message);
            Assert.IsFalse(direct.Data.Pending);
            Assert.IsTrue(direct.Data.MustChangePassword);
            Assert.IsFalse(string.IsNullOrEmpty(direct.Data.TemporaryPassword));
        }

        // ---------------------------------------------------------------------------------------------------- L1: pending requests

        [TestMethod]
        public async Task An_Edit_That_No_Longer_Asks_For_A_Sensitive_Role_Withdraws_The_Pending_Request()
        {
            var h = new Host(Unique("l1"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            int u = await h.RegisterAsync(h.Admin, Unique("u"), new[] { WarehouseRole, CeoRole });
            RoleGrantDto grant = (await GrantsAsync(h, Pmo(p2))).Single(g => g.UserId == u);

            ResponseDto<string> dropped = await SetRolesAsync(h, h.Admin, u, WarehouseRole);
            Assert.IsTrue(dropped.Success);
            StringAssert.Contains(dropped.Message, AccountServiceMessages.GrantWithdrawnByEdit, "the answer says that the request is gone");
            StringAssert.Contains(dropped.Message, "CEO");
            Assert.IsFalse((await GrantsAsync(h, Pmo(p2))).Any(g => g.UserId == u), "the request is withdrawn");
            ResponseDto<RoleGrantDto> late = await ApproveAsync(h, Pmo(p2), grant.Id);
            Assert.IsFalse(late.Success);
            Assert.AreEqual(AccountServiceMessages.GrantNotFound, late.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(u));
            Assert.IsTrue(InTrail(h, "Identity.Roles", "GrantRejected", a => a.EntityId == u && a.UserId == h.AdminId));

            // the edit-user screen does the same
            int v = await h.RegisterAsync(h.Admin, Unique("v"), new[] { WarehouseRole, FinanceManagerRole });
            Assert.IsTrue((await GrantsAsync(h, Pmo(p2))).Any(g => g.UserId == v));
            Assert.IsTrue((await h.As(h.Admin, s => s.UpdateUserAsync(Edit(v, "v@example.com", new[] { WarehouseRole }), default).AsTask())).Success);
            Assert.IsFalse((await GrantsAsync(h, Pmo(p2))).Any(g => g.UserId == v));

            // an edit that still asks for the role leaves the request where it is
            int w = await h.RegisterAsync(h.Admin, Unique("w"), new[] { WarehouseRole, CeoRole });
            ResponseDto<string> kept = await SetRolesAsync(h, h.Admin, w, WarehouseRole, CeoRole);
            Assert.IsTrue(kept.Success);
            Assert.IsFalse(kept.Message.Contains(AccountServiceMessages.GrantWithdrawnByEdit), "nothing was withdrawn, so the answer does not say so");
            Assert.AreEqual(1, (await GrantsAsync(h, Pmo(p2))).Count(g => g.UserId == w));
        }

        [TestMethod]
        public async Task An_Approval_Checks_Again_That_The_User_Is_Active_And_The_Requester_Is_Still_A_Pmo_User()
        {
            var h = new Host(Unique("l1"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            int p3 = await h.AddPmoAsync(Unique("p3"));

            int inactive = await h.RegisterAsync(h.Admin, Unique("u"), new[] { WarehouseRole, CeoRole });
            RoleGrantDto first = (await GrantsAsync(h, Pmo(p2))).Single(g => g.UserId == inactive);
            h.Write(uow => uow.Set<User>().Single(u => u.Id == inactive).IsActive = false);
            ResponseDto<RoleGrantDto> r1 = await ApproveAsync(h, Pmo(p2), first.Id);
            Assert.IsFalse(r1.Success);
            Assert.AreEqual(AccountServiceMessages.GrantVoid, r1.Message);
            Assert.IsFalse((await GrantsAsync(h, Pmo(p2))).Any(g => g.Id == first.Id), "the void request is removed");
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(inactive));
            Assert.IsTrue(InTrail(h, "Identity.Roles", "GrantVoid", a => a.EntityId == inactive));

            // p3 asks, then loses the PMO role: his request cannot be approved any more
            int target = await h.RegisterAsync(h.Admin, Unique("t"), new[] { WarehouseRole });
            Assert.IsTrue((await SetRolesAsync(h, Pmo(p3, "p3"), target, WarehouseRole, FinanceManagerRole)).Success);
            RoleGrantDto second = (await GrantsAsync(h, Pmo(p2))).Single(g => g.UserId == target);
            h.Write(uow => uow.Set<UserRole>().RemoveRange(uow.Set<UserRole>().Where(r => r.UserId == p3 && r.RoleId == PmoRole).ToList()));
            ResponseDto<RoleGrantDto> r2 = await ApproveAsync(h, Pmo(p2), second.Id);
            Assert.IsFalse(r2.Success);
            Assert.AreEqual(AccountServiceMessages.GrantVoid, r2.Message);
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(target));
        }

        // ---------------------------------------------------------------------------------------------------- H2: the security stamp

        [TestMethod]
        public async Task An_Access_Token_Ends_At_Once_When_The_Password_Is_Reset_Or_Changed()
        {
            var h = new Host(Unique("stamp"));
            string name = Unique("u");
            int id = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole });
            ResponseLoginUserDto session = await SignInAsync(h, name);
            Assert.IsNull(await h.TokenProblemAsync(session.AccessToken), "a fresh token is accepted");

            ResponseDto<AdminResetPasswordResultDto> reset = await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id }, default).AsTask());
            Assert.IsTrue(reset.Success, reset.Message);
            StringAssert.Contains(await h.TokenProblemAsync(session.AccessToken) ?? "", "before the last change", "the old token is refused at once after the reset");

            ResponseDto<ResponseLoginUserDto> chosen = await h.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = reset.Data.TemporaryPassword, NewPassword = "Brand-New-2029w" }, default).AsTask());
            Assert.IsTrue(chosen.Success, chosen.Message);
            Assert.IsNotNull(await h.TokenProblemAsync(session.AccessToken));
            Assert.IsNull(await h.TokenProblemAsync(chosen.Data.AccessToken), "the token made by the change is good");

            ResponseDto<ResponseLoginUserDto> changed = await h.As(Signed(id, name, "WAREHOUSE"), s => s.ChangePasswordAsync(new ChangePasswordDto { CurrentPassword = "Brand-New-2029w", NewPassword = "Another-2031xyz" }, default).AsTask());
            Assert.IsTrue(changed.Success, changed.Message);
            Assert.IsNotNull(await h.TokenProblemAsync(chosen.Data.AccessToken), "a change of the password ends the other tokens");
            Assert.IsNull(await h.TokenProblemAsync(changed.Data.AccessToken));
        }

        [TestMethod]
        public async Task An_Access_Token_Ends_At_Once_When_A_Role_Is_Taken_Away_Or_Given_By_Approval_And_The_Refresh_Token_Brings_A_Good_One()
        {
            var h = new Host(Unique("stamp"));
            int p2 = await h.AddPmoAsync(Unique("p2"));
            string name = Unique("u");
            int id = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole, RoleConstants.EngineerResponseRole.Id });
            ResponseLoginUserDto session = await SignInAsync(h, name);
            Assert.IsNull(await h.TokenProblemAsync(session.AccessToken));

            Assert.IsTrue((await SetRolesAsync(h, h.Admin, id, WarehouseRole)).Success);
            Assert.IsNotNull(await h.TokenProblemAsync(session.AccessToken), "a role was taken away: the old token is refused");
            ResponseDto<ResponseLoginUserDto> refreshed = await RefreshAsync(h, session.RefreshToken);
            Assert.IsTrue(refreshed.Success, refreshed.Message);
            Assert.IsNull(await h.TokenProblemAsync(refreshed.Data.AccessToken), "the refresh token brings a token for the roles he has now");

            // an ordinary role is given at once: the stamp changes too
            Assert.IsTrue((await SetRolesAsync(h, h.Admin, id, WarehouseRole, RoleConstants.EngineerResponseRole.Id)).Success);
            Assert.IsNotNull(await h.TokenProblemAsync(refreshed.Data.AccessToken));

            // a sensitive role given by approval
            ResponseDto<ResponseLoginUserDto> again = await RefreshAsync(h, refreshed.Data.RefreshToken);
            Assert.IsTrue(again.Success, again.Message);
            Assert.IsNull(await h.TokenProblemAsync(again.Data.AccessToken));
            Assert.IsTrue((await SetRolesAsync(h, h.Admin, id, WarehouseRole, RoleConstants.EngineerResponseRole.Id, CeoRole)).Success);
            Assert.IsNull(await h.TokenProblemAsync(again.Data.AccessToken), "a request alone changes nothing");
            RoleGrantDto grant = (await GrantsAsync(h, Pmo(p2))).Single(g => g.UserId == id);
            Assert.IsTrue((await ApproveAsync(h, Pmo(p2), grant.Id)).Success);
            Assert.IsNotNull(await h.TokenProblemAsync(again.Data.AccessToken), "the approved role ends the old token");
        }

        [TestMethod]
        public async Task Deleting_The_User_And_Signing_Out_End_The_Access_Tokens_At_Once()
        {
            var h = new Host(Unique("stamp"));
            string gone = Unique("gone");
            int goneId = await h.RegisterAsync(h.Admin, gone, new[] { WarehouseRole });
            ResponseLoginUserDto goneSession = await SignInAsync(h, gone);
            Assert.IsNull(await h.TokenProblemAsync(goneSession.AccessToken));
            Assert.IsTrue((await h.As(h.Admin, s => s.DeleteUserAsync(goneId, default))).Success);
            Assert.IsNotNull(await h.TokenProblemAsync(goneSession.AccessToken));

            // sign out by the refresh token alone (the access token has expired), then both are dead
            string quit = Unique("quit");
            int quitId = await h.RegisterAsync(h.Admin, quit, new[] { WarehouseRole });
            ResponseLoginUserDto quitSession = await SignInAsync(h, quit);
            ResponseDto<bool> loggedOut = await h.As(null, s => s.LogoutAsync(null, quitSession.RefreshToken, default).AsTask());
            Assert.IsTrue(loggedOut.Success);
            Assert.IsNotNull(await h.TokenProblemAsync(quitSession.AccessToken), "the access token ends with the sign-out");
            ResponseDto<ResponseLoginUserDto> refresh = await RefreshAsync(h, quitSession.RefreshToken);
            Assert.IsFalse(refresh.Success, "and the refresh token is gone");
            Assert.AreEqual(HttpStatusCode.Unauthorized, refresh.HttpStatusCode);
            Assert.IsTrue(InTrail(h, "Identity.SignIn", "Logout", a => a.EntityId == quitId));
            Assert.IsTrue((await LoginAsync(h, quit)).Success, "he can sign in again");
        }

        [TestMethod]
        public async Task A_Sign_Out_Ignores_A_Refresh_Token_That_Belongs_To_Somebody_Else()
        {
            var h = new Host(Unique("logout"));
            string a = Unique("a");
            string b = Unique("b");
            int aId = await h.RegisterAsync(h.Admin, a, new[] { WarehouseRole });
            await h.RegisterAsync(h.Admin, b, new[] { WarehouseRole });
            ResponseLoginUserDto sessionA = await SignInAsync(h, a);
            ResponseLoginUserDto sessionB = await SignInAsync(h, b);

            // A signs out, but presents the refresh token of B: his own sessions end, B's stay
            Assert.IsTrue((await h.As(Signed(aId, a, "WAREHOUSE"), s => s.LogoutAsync(aId.ToString(), sessionB.RefreshToken, default).AsTask())).Success);
            Assert.IsNotNull(await h.TokenProblemAsync(sessionA.AccessToken));
            Assert.IsFalse((await RefreshAsync(h, sessionA.RefreshToken)).Success);
            Assert.IsNull(await h.TokenProblemAsync(sessionB.AccessToken), "B was not signed out");
            Assert.IsTrue((await RefreshAsync(h, sessionB.RefreshToken)).Success);

            // an empty or made-up token signs nobody out and does not fail
            Assert.IsTrue((await h.As(null, s => s.LogoutAsync(null, "not-a-token", default).AsTask())).Success);
            Assert.IsTrue((await h.As(null, s => s.LogoutAsync(null, null, default).AsTask())).Success);
        }

        [TestMethod]
        public async Task An_Account_That_Must_Change_The_Password_Passes_Only_The_Calls_That_Finish_The_Change()
        {
            var h = new Host(Unique("flag"));
            string name = Unique("u");
            int id = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole });
            ResponseLoginUserDto session = await SignInAsync(h, name);

            // a token that was made before the flag was set (the flag does not change the stamp)
            h.Write(uow => uow.Set<UserClaim>().Add(new UserClaim { UserId = id, ClaimType = IdentityRules.MustChangePasswordClaimType, ClaimValue = "1" }));
            StringAssert.Contains(await h.TokenProblemAsync(session.AccessToken, "/api/v1/Contracts") ?? "", "new password");
            Assert.IsNull(await h.TokenProblemAsync(session.AccessToken, "/api/v1/Accounts/change-temporary-password"));
            Assert.IsNull(await h.TokenProblemAsync(session.AccessToken, "/API/V1/ACCOUNTS/LOGOUT"));
            ResponseDto<ResponseLoginUserDto> refresh = await RefreshAsync(h, session.RefreshToken);
            Assert.IsFalse(refresh.Success, "no new token while the password must be changed");
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, refresh.Data.Code);
        }

        [TestMethod]
        public void The_Token_Check_Refuses_Inactive_Deleted_Unstamped_And_Must_Change_Accounts()
        {
            const string key = "token-check-key-0123456789-0123456789-abcdef";
            string claim = TokenStamp.Compute("stamp-1", key);
            Assert.AreEqual(32, claim.Length);
            Assert.IsFalse(claim.Any(ch => ch == '+' || ch == '/' || ch == '='), "URL-safe");
            Assert.IsFalse(claim.Contains("stamp-1"), "the raw stamp is never in the token");

            var ok = new JwtAccountStatus.Status { IsActive = true, SecurityStamp = "stamp-1" };
            Assert.IsNull(JwtAccountStatus.Evaluate(ok, claim, key, "/api/v1/Contracts"));
            Assert.IsNotNull(JwtAccountStatus.Evaluate(null, claim, key, "/x"), "the user is gone");
            Assert.IsNotNull(JwtAccountStatus.Evaluate(new JwtAccountStatus.Status { IsActive = false, SecurityStamp = "stamp-1" }, claim, key, "/x"));
            Assert.IsNotNull(JwtAccountStatus.Evaluate(new JwtAccountStatus.Status { IsActive = true, IsDeleted = true, SecurityStamp = "stamp-1" }, claim, key, "/x"));
            Assert.IsNotNull(JwtAccountStatus.Evaluate(ok, null, key, "/x"), "a token without the claim (made before R1a2)");
            Assert.IsNotNull(JwtAccountStatus.Evaluate(ok, "", key, "/x"));
            Assert.IsNotNull(JwtAccountStatus.Evaluate(new JwtAccountStatus.Status { IsActive = true, SecurityStamp = "stamp-2" }, claim, key, "/x"), "the stamp changed");
            Assert.IsNotNull(JwtAccountStatus.Evaluate(ok, TokenStamp.Compute("stamp-1", key + "x"), key, "/x"), "made with another key");
            Assert.IsNotNull(JwtAccountStatus.Evaluate(ok, claim + "A", key, "/x"));

            var mustChange = new JwtAccountStatus.Status { IsActive = true, SecurityStamp = "stamp-1", MustChangePassword = true };
            Assert.IsNotNull(JwtAccountStatus.Evaluate(mustChange, claim, key, "/api/v1/Contracts"));
            Assert.IsNotNull(JwtAccountStatus.Evaluate(mustChange, claim, key, null));
            Assert.IsNull(JwtAccountStatus.Evaluate(mustChange, claim, key, "/api/v1/Accounts/change-temporary-password"));
            Assert.IsNull(JwtAccountStatus.Evaluate(mustChange, claim, key, "/API/v1/accounts/LOGOUT"));
            Assert.IsNotNull(JwtAccountStatus.Evaluate(mustChange, claim, key, "/api/v1/Accounts/change-password"), "change-password needs the current password of a normal session");
        }

        // ---------------------------------------------------------------------------------------------------- L3: refresh tokens work once

        [TestMethod]
        public async Task A_Refresh_Token_Works_Once_And_A_Late_Second_Use_Ends_Every_Session_Of_The_User()
        {
            var h = new Host(Unique("reuse"));
            string name = Unique("re");
            int id = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole });
            ResponseLoginUserDto first = await SignInAsync(h, name);

            ResponseDto<ResponseLoginUserDto> second = await RefreshAsync(h, first.RefreshToken);
            Assert.IsTrue(second.Success, second.Message);
            Assert.AreNotEqual(first.RefreshToken, second.Data.RefreshToken);

            // a moment later it is a double click: refused, nothing else happens
            ResponseDto<ResponseLoginUserDto> echo = await RefreshAsync(h, first.RefreshToken);
            Assert.IsFalse(echo.Success);
            Assert.AreEqual(HttpStatusCode.Unauthorized, echo.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.InvalidRefreshToken, echo.Message);
            ResponseDto<ResponseLoginUserDto> third = await RefreshAsync(h, second.Data.RefreshToken);
            Assert.IsTrue(third.Success, "the new pair was not touched: " + third.Message);
            Assert.IsNull(await h.TokenProblemAsync(third.Data.AccessToken));
            Assert.IsFalse(InTrail(h, "Identity.SignIn", "TokenReuse"));

            // after the grace time the use of a used token means that it was copied
            h.Write(uow =>
            {
                foreach (CustomUserToken used in uow.Set<CustomUserToken>().Where(t => t.UserId == id && t.AccessTokenHash == "").ToList())
                    used.AccessTokenExpiresDateTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            });
            ResponseDto<ResponseLoginUserDto> theft = await RefreshAsync(h, first.RefreshToken);
            Assert.IsFalse(theft.Success);
            Assert.AreEqual(HttpStatusCode.Unauthorized, theft.HttpStatusCode);
            Assert.AreEqual(AccountServiceMessages.RefreshTokenReused, theft.Message);
            Assert.IsFalse((await RefreshAsync(h, third.Data.RefreshToken)).Success, "every refresh token of the user is gone");
            Assert.IsNotNull(await h.TokenProblemAsync(third.Data.AccessToken), "and every access token has ended");
            Assert.IsTrue(InTrail(h, "Identity.SignIn", "TokenReuse", a => a.EntityId == id));
            Assert.IsTrue((await LoginAsync(h, name)).Success, "the real user signs in again");
        }

        [TestMethod]
        public async Task A_Refresh_Token_That_Is_Unknown_Is_Logged_At_Information_Only()
        {
            var h = new Host(Unique("loglevel"));
            int before = h.Logs.Entries.Count;
            ResponseDto<ResponseLoginUserDto> r = await RefreshAsync(h, "not-a-token.at-all");
            Assert.IsFalse(r.Success);
            var added = h.Logs.Entries.Skip(before).Where(e => e.Category.EndsWith("AccountService")).ToList();
            Assert.IsTrue(added.Any(e => e.Level == LogLevel.Information && e.Text.Contains("refresh token")), "an Information entry says that a token was refused");
            Assert.IsFalse(added.Any(e => e.Level >= LogLevel.Warning), "anonymous callers cannot fill the log with warnings");
        }

        // ---------------------------------------------------------------------------------------------------- L2: the same answer for every kind of unusable account

        [TestMethod]
        public async Task Unknown_Inactive_And_Deleted_Accounts_Get_The_Answer_Of_A_Wrong_Password_Until_The_Right_Password_Is_Given()
        {
            var h = new Host(Unique("l2"));
            string ordinary = Unique("ord");
            string inactive = Unique("ina");
            string deleted = Unique("del");
            await h.RegisterAsync(h.Admin, ordinary, new[] { WarehouseRole });
            int inactiveId = await h.RegisterAsync(h.Admin, inactive, new[] { WarehouseRole });
            int deletedId = await h.RegisterAsync(h.Admin, deleted, new[] { WarehouseRole });
            h.Write(uow => uow.Set<User>().Single(u => u.Id == inactiveId).IsActive = false);
            Assert.IsTrue((await h.As(h.Admin, s => s.DeleteUserAsync(deletedId, default))).Success);

            ResponseDto<ResponseLoginUserDto> wrong = await LoginAsync(h, ordinary, "Wrong-Pass-2026");
            foreach (ResponseDto<ResponseLoginUserDto> answer in new[]
            {
                await LoginAsync(h, Unique("nobody")),
                await LoginAsync(h, inactive, "Wrong-Pass-2026"),
                await LoginAsync(h, deleted, "Wrong-Pass-2026"),
            })
            {
                Assert.IsFalse(answer.Success);
                Assert.AreEqual(wrong.Message, answer.Message);
                Assert.AreEqual(wrong.HttpStatusCode, answer.HttpStatusCode);
                Assert.IsNull(answer.Data);
            }

            // only somebody who knows the password hears why it does not work
            Assert.AreEqual(AccountServiceMessages.AccountIsNotActive, (await LoginAsync(h, inactive)).Message);
            Assert.AreEqual(AccountServiceMessages.AccountIsDeleted, (await LoginAsync(h, deleted)).Message);

            // the temporary-password call answers the same way
            ChangeTemporaryPasswordDto Probe(string user, string password) => new ChangeTemporaryPasswordDto { UserName = user, TemporaryPassword = password, NewPassword = "Brand-New-2029w" };
            ResponseDto<ResponseLoginUserDto> tempWrong = await h.As(null, s => s.ChangeTemporaryPasswordAsync(Probe(ordinary, "Wrong-Pass-2026"), default).AsTask());
            Assert.AreEqual(wrong.Message, tempWrong.Message);
            Assert.AreEqual(wrong.Message, (await h.As(null, s => s.ChangeTemporaryPasswordAsync(Probe(Unique("nobody"), GoodPassword), default).AsTask())).Message);
            Assert.AreEqual(wrong.Message, (await h.As(null, s => s.ChangeTemporaryPasswordAsync(Probe(inactive, "Wrong-Pass-2026"), default).AsTask())).Message);
            Assert.AreEqual(AccountServiceMessages.AccountIsNotActive, (await h.As(null, s => s.ChangeTemporaryPasswordAsync(Probe(inactive, GoodPassword), default).AsTask())).Message);
        }

        // ---------------------------------------------------------------------------------------------------- M5 and M1: unknown names

        [TestMethod]
        public async Task A_Password_Typed_Into_The_Name_Field_Never_Reaches_The_Audit_Trail()
        {
            var h = new Host(Unique("m5"));
            UnknownUserTally.Reset();
            try
            {
                string typed = "My-Real-Passw0rd-2026";
                await LoginAsync(h, typed, "x");
                UnknownUserTally.Reset();
                string typedAgain = "Another-Secret-4711";
                await h.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = typedAgain, TemporaryPassword = "a", NewPassword = "Brand-New-2029w" }, default).AsTask());
                UnknownUserTally.Reset();
                await LoginAsync(h, "seat.secret-" + typed, "x");

                List<FnAuditLog> log = Trail(h);
                foreach (string secret in new[] { typed, typedAgain, "Passw0rd-2026", "Secret-4711" })
                    Assert.IsFalse(log.Any(a => (a.Summary ?? "").Contains(secret) || (a.BeforeJson ?? "").Contains(secret) || (a.AfterJson ?? "").Contains(secret) || (a.EntityType ?? "").Contains(secret)),
                        "'" + secret + "' is nowhere in the whole trail");
                FnAuditLog row = log.Single(a => a.EntityType == "Identity.SignIn" && a.Action == "Failure" && a.UserId == 0 && (a.Summary ?? "").Contains("«My»"));
                StringAssert.Contains(row.Summary, typed.Length + " ");
                Assert.IsTrue(log.Any(a => (a.Summary ?? "").Contains("«An»")), "the second call keeps its first two characters as well");
                Assert.IsTrue(log.Any(a => a.Action == "Blocked" && !(a.Summary ?? "").Contains(typed)), "the blocked seat name is kept short as well");
            }
            finally
            {
                UnknownUserTally.Reset();
            }
        }

        [TestMethod]
        public void The_Hint_Of_An_Unknown_Name_Is_Its_Length_And_Its_First_Two_Characters()
        {
            Assert.AreEqual("(خالی)", IdentityRules.NameHint(null));
            Assert.AreEqual("(خالی)", IdentityRules.NameHint(""));
            Assert.AreEqual("1 نویسه، با شروع «a»", IdentityRules.NameHint("a"));
            Assert.AreEqual("21 نویسه، با شروع «Su»", IdentityRules.NameHint("Sup3r-Secret-Passw0rd"));
            Assert.IsFalse(IdentityRules.NameHint("Sup3r-Secret-Passw0rd").Contains("Secret"));
        }

        [TestMethod]
        public void Attempts_With_Unknown_Names_Are_Counted_Per_Address_And_Minute_And_The_First_And_Every_Tenth_Power_Get_A_Row()
        {
            UnknownUserTally.Reset();
            try
            {
                var t = new DateTimeOffset(2030, 1, 1, 12, 0, 5, TimeSpan.Zero);
                Assert.AreEqual(1, UnknownUserTally.Add("10.0.0.1", t));
                Assert.AreEqual(2, UnknownUserTally.Add("10.0.0.1", t.AddSeconds(10)));
                Assert.AreEqual(1, UnknownUserTally.Add("10.0.0.2", t), "another address has its own count");
                Assert.AreEqual(1, UnknownUserTally.Add("10.0.0.1", t.AddMinutes(1)), "a new minute starts again");
                Assert.AreEqual(2, UnknownUserTally.Add("10.0.0.1", t.AddMinutes(1)));

                foreach (int worthy in new[] { 1, 10, 100, 1000, 10000 }) Assert.IsTrue(UnknownUserTally.IsRowWorthy(worthy), worthy.ToString());
                foreach (int plain in new[] { 0, 2, 3, 9, 11, 20, 99, 101, 110, 200, 1001 }) Assert.IsFalse(UnknownUserTally.IsRowWorthy(plain), plain.ToString());
            }
            finally
            {
                UnknownUserTally.Reset();
            }
        }

        [TestMethod]
        public async Task A_Flood_Of_Unknown_Names_Writes_Far_Fewer_Rows_Than_Attempts()
        {
            var h = new Host(Unique("flood"));
            UnknownUserTally.Reset();
            try
            {
                for (int i = 0; i < 25; i++) await LoginAsync(h, "ghost-" + i);
                int rows = Trail(h).Count(a => a.EntityType == "Identity.SignIn" && a.UserId == 0);
                Assert.IsTrue(rows >= 1 && rows < 8, rows + " rows for 25 attempts");

                // another address is counted on its own and its row carries its address
                await h.From(IPAddress.Parse("10.9.9.9"), null, s => s.LoginAsync(new LoginUserDto { UserName = "ghost-x", Password = "x" }, default).AsTask());
                Assert.IsTrue(Trail(h).Any(a => a.EntityType == "Identity.SignIn" && a.Ip == "10.9.9.9" && a.Action == "Failure"));
            }
            finally
            {
                UnknownUserTally.Reset();
            }
        }

        [TestMethod]
        public async Task A_Locked_Pmo_Account_Can_Sign_In_From_The_Server_Itself_But_Not_Through_A_Proxy_And_Other_Accounts_Cannot()
        {
            var h = new Host(Unique("loop"));
            string pmoName = Unique("pmo");
            string whName = Unique("wh");
            int pmoId = await h.AddPmoAsync(pmoName);
            await h.RegisterAsync(h.Admin, whName, new[] { WarehouseRole });
            for (int i = 0; i < 5; i++)
            {
                await LoginAsync(h, pmoName, "Wrong-Pass-" + i + "xx");
                await LoginAsync(h, whName, "Wrong-Pass-" + i + "xx");
            }
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, (await LoginAsync(h, pmoName)).Message);
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, (await LoginAsync(h, whName)).Message);

            // somebody at the server: the PMO account is let in with the right password (and unlocked); an ordinary account is not
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, (await LoginAsync(h, whName, GoodPassword, IPAddress.Loopback)).Message);
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, (await LoginAsync(h, pmoName, GoodPassword, IPAddress.Loopback, forwardedFor: "203.0.113.9")).Message, "a call that came through a proxy is not the server itself");
            Assert.IsFalse((await LoginAsync(h, pmoName, "Wrong-Pass-2026", IPAddress.Loopback)).Success, "a wrong password does not get in");
            ResponseDto<ResponseLoginUserDto> inside = await LoginAsync(h, pmoName, GoodPassword, IPAddress.Loopback);
            Assert.IsTrue(inside.Success, inside.Message);
            Assert.IsTrue(InTrail(h, "Identity.SignIn", "LoopbackSignIn", a => a.EntityId == pmoId && a.Ip == "127.0.0.1"));
            Assert.IsTrue((await LoginAsync(h, pmoName)).Success, "the lock was lifted for the network too");
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, (await LoginAsync(h, whName)).Message, "the other account is still locked");
        }

        [TestMethod]
        public async Task Wrong_Guesses_From_The_Server_Itself_Count_And_Lock_The_Pmo_Account_Against_The_Network()
        {
            var h = new Host(Unique("loop"));
            string pmoName = Unique("pmo");
            await h.AddPmoAsync(pmoName);
            for (int i = 0; i < 5; i++) await LoginAsync(h, pmoName, "Wrong-Pass-" + i + "xx", IPAddress.Loopback);
            Assert.AreEqual(AccountServiceMessages.AccountIsLockedOut, (await LoginAsync(h, pmoName)).Message, "a guess from the server is not free");
            Assert.IsTrue((await LoginAsync(h, pmoName, GoodPassword, IPAddress.Loopback)).Success, "the person at the server still gets in");
        }

        // ---------------------------------------------------------------------------------------------------- L7 and M7: temporary passwords and the seeded administrator

        [TestMethod]
        public async Task A_Temporary_Password_Of_A_Reset_Runs_Out_And_A_New_Reset_Starts_The_Time_Again()
        {
            var h = new Host(Unique("l7"));
            string name = Unique("u");
            int id = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole });
            string temporary = (await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id }, default).AsTask())).Data.TemporaryPassword;
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, (await LoginAsync(h, name, temporary)).Data.Code);

            // the password was set 25 hours ago (Identity:TemporaryPasswordHours is 24)
            h.Write(uow => uow.Set<UserClaim>().Single(c => c.UserId == id && c.ClaimType == IdentityRules.MustChangePasswordClaimType).ClaimValue = MustChangeFlag.ForReset(DateTimeOffset.Now.AddHours(-25)));
            ResponseDto<ResponseLoginUserDto> late = await LoginAsync(h, name, temporary);
            Assert.IsFalse(late.Success);
            Assert.AreEqual(AccountServiceMessages.TemporaryPasswordExpiredCode, late.Data.Code);
            Assert.AreEqual(AccountServiceMessages.TemporaryPasswordExpired, late.Message);
            Assert.AreEqual(HttpStatusCode.Unauthorized, late.HttpStatusCode);
            ResponseDto<ResponseLoginUserDto> change = await h.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = temporary, NewPassword = "Brand-New-2029w" }, default).AsTask());
            Assert.IsFalse(change.Success);
            Assert.AreEqual(AccountServiceMessages.TemporaryPasswordExpiredCode, change.Data.Code);
            Assert.AreEqual(0, h.Query(uow => uow.Set<CustomUserToken>().Count(t => t.UserId == id)), "no tokens were made");

            // a new reset starts the time again
            string fresh = (await h.As(h.Admin, s => s.AdminResetPasswordAsync(new AdminResetPasswordDto { UserId = id }, default).AsTask())).Data.TemporaryPassword;
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, (await LoginAsync(h, name, fresh)).Data.Code);
            Assert.IsTrue((await h.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = fresh, NewPassword = "Brand-New-2029w" }, default).AsTask())).Success);
        }

        [TestMethod]
        public void Only_The_Flag_Of_A_Reset_Runs_Out()
        {
            DateTimeOffset now = new DateTimeOffset(2030, 5, 1, 12, 0, 0, TimeSpan.Zero);
            Assert.IsFalse(MustChangeFlag.IsExpired(MustChangeFlag.ForReset(now.AddHours(-23)), now, 24));
            Assert.IsTrue(MustChangeFlag.IsExpired(MustChangeFlag.ForReset(now.AddHours(-24)), now, 24));
            Assert.IsTrue(MustChangeFlag.IsExpired(MustChangeFlag.ForReset(now.AddDays(-30)), now, 24));
            Assert.IsFalse(MustChangeFlag.IsExpired(MustChangeFlag.ForReset(now.AddDays(-30)), now, 0), "0 switches the limit off");
            Assert.IsFalse(MustChangeFlag.IsExpired(MustChangeFlag.Seed, now, 24), "the installer's password never runs out");
            Assert.IsFalse(MustChangeFlag.IsExpired(MustChangeFlag.Upgrade, now, 24), "nor the upgrade's");
            Assert.IsFalse(MustChangeFlag.IsExpired("1", now, 24), "nor the flag of before R1a2");
            Assert.IsFalse(MustChangeFlag.IsExpired(null, now, 24));
            Assert.IsFalse(MustChangeFlag.IsExpired("reset:not-a-date", now, 24));
        }

        [TestMethod]
        public async Task The_Seeded_Administrator_Chooses_His_Own_Password_At_The_First_Sign_In()
        {
            var h = new Host(Unique("seed"));
            ResponseDto<ResponseLoginUserDto> first = await LoginAsync(h, "Admin", AdminPassword);
            Assert.IsFalse(first.Success);
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, first.Data.Code);
            Assert.AreEqual("seed", h.Query(uow => uow.Set<UserClaim>().Single(c => c.UserId == h.AdminId && c.ClaimType == IdentityRules.MustChangePasswordClaimType).ClaimValue));

            Assert.IsFalse((await h.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = "Admin", TemporaryPassword = AdminPassword, NewPassword = "weak" }, default).AsTask())).Success);
            ResponseDto<ResponseLoginUserDto> chosen = await h.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = "Admin", TemporaryPassword = AdminPassword, NewPassword = "Own-Chosen-Pass-2026" }, default).AsTask());
            Assert.IsTrue(chosen.Success, chosen.Message);
            Assert.IsFalse(string.IsNullOrEmpty(chosen.Data.AccessToken));
            Assert.IsFalse((await LoginAsync(h, "Admin", AdminPassword)).Success, "the installer's password is dead");
            Assert.IsTrue((await LoginAsync(h, "Admin", "Own-Chosen-Pass-2026")).Success);
        }

        [TestMethod]
        public async Task The_Administrator_Is_Not_Forced_When_The_Seed_Setting_Says_So()
        {
            var h = new Host(Unique("seed"), new Dictionary<string, string> { ["AdminUserSeed:MustChangePassword"] = "false" });
            ResponseDto<ResponseLoginUserDto> direct = await LoginAsync(h, "Admin", AdminPassword);
            Assert.IsTrue(direct.Success, direct.Message);
            Assert.IsFalse(h.Query(uow => uow.Set<UserClaim>().Any(c => c.UserId == h.AdminId && c.ClaimType == IdentityRules.MustChangePasswordClaimType)));
        }

        [TestMethod]
        public async Task The_Upgrade_Forces_A_New_Password_Once_For_Users_With_A_Password_And_Not_For_The_Seats()
        {
            // an installation of before R1a2: the step is off while the users are made, then it runs at the "first start after the upgrade"
            var h = new Host(Unique("upgrade"), new Dictionary<string, string> { ["Identity:ForcePasswordChangeAtUpgrade"] = "false" });
            Assert.AreEqual(0, Trail(h).Count(a => a.Action == "UpgradeForced"));
            string name = Unique("old");
            int id = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole });
            h.Provider.RunScopedService<IApplicationUserManager>(m =>
                Assert.IsTrue(m.CreateAsync(new User { UserName = "seat.upgrade-test", Email = "seat@example.com", EmailConfirmed = true, LockoutEnabled = true, IsActive = true }, GoodPassword).GetAwaiter().GetResult().Succeeded));
            int seatId = h.Query(uow => uow.Set<User>().Single(u => u.UserName == "seat.upgrade-test").Id);
            Assert.IsTrue((await LoginAsync(h, name)).Success, "before the upgrade step he signs in with his old password");

            h.Configuration["Identity:ForcePasswordChangeAtUpgrade"] = "true";
            using (IServiceScope scope = h.Provider.CreateScope())
                scope.ServiceProvider.GetRequiredService<IIdentityDbInitializer>().SeedData();

            List<UserClaim> flags = h.Query(uow => uow.Set<UserClaim>().AsNoTracking().Where(c => c.ClaimType == IdentityRules.MustChangePasswordClaimType).ToList());
            Assert.AreEqual(MustChangeFlag.Upgrade, flags.Single(c => c.UserId == id).ClaimValue);
            Assert.AreEqual(MustChangeFlag.Seed, flags.Single(c => c.UserId == h.AdminId).ClaimValue, "the administrator keeps the flag he had");
            Assert.IsFalse(flags.Any(c => c.UserId == seatId), "an AI seat has no password to change");
            Assert.AreEqual(1, Trail(h).Count(a => a.Action == "UpgradeForced"), "the marker row");

            ResponseDto<ResponseLoginUserDto> login = await LoginAsync(h, name);
            Assert.IsFalse(login.Success);
            Assert.AreEqual(AccountServiceMessages.MustChangePasswordCode, login.Data.Code);
            Assert.IsTrue((await h.As(null, s => s.ChangeTemporaryPasswordAsync(new ChangeTemporaryPasswordDto { UserName = name, TemporaryPassword = GoodPassword, NewPassword = "Brand-New-2029w" }, default).AsTask())).Success);
            Assert.IsTrue((await LoginAsync(h, name, "Brand-New-2029w")).Success);

            // it ran once: a user made later is not flagged by the next start
            string later = Unique("later");
            int laterId = await h.RegisterAsync(h.Admin, later, new[] { WarehouseRole });
            using (IServiceScope scope = h.Provider.CreateScope())
                scope.ServiceProvider.GetRequiredService<IIdentityDbInitializer>().SeedData();
            Assert.IsFalse(h.Query(uow => uow.Set<UserClaim>().Any(c => c.UserId == laterId && c.ClaimType == IdentityRules.MustChangePasswordClaimType)));
            Assert.AreEqual(1, Trail(h).Count(a => a.Action == "UpgradeForced"));
        }

        [TestMethod]
        public void A_New_Installation_Writes_The_Upgrade_Marker_Without_Flagging_Anybody_But_The_Seeded_Administrator()
        {
            var h = new Host(Unique("fresh"));
            Assert.AreEqual(1, Trail(h).Count(a => a.Action == "UpgradeForced"));
            Assert.AreEqual(1, h.Query(uow => uow.Set<UserClaim>().Count(c => c.ClaimType == IdentityRules.MustChangePasswordClaimType)), "only the administrator, by the seed");
            StringAssert.Contains(Trail(h).Single(a => a.Action == "UpgradeForced").Summary, "نصب تازه");
        }

        [TestMethod]
        public void The_Server_Does_Not_Start_Outside_Development_With_The_Signing_Key_That_Ships_With_The_Source()
        {
            IConfiguration Config(string key) => new ConfigurationBuilder().AddInMemoryCollection(new[] { new KeyValuePair<string, string>("BearerTokens:Key", key) }).Build();

            Assert.AreEqual(0, StartupSecurityChecks.Problems(Config(StartupSecurityChecks.ShippedCiKey), isDevelopment: true).Count, "Development is left alone");
            Assert.AreEqual(1, StartupSecurityChecks.Problems(Config(StartupSecurityChecks.ShippedCiKey), isDevelopment: false).Count);
            Assert.AreEqual(1, StartupSecurityChecks.Problems(Config("ci-only-anything"), isDevelopment: false).Count, "any key that says CI-ONLY");
            Assert.AreEqual(1, StartupSecurityChecks.Problems(Config(""), isDevelopment: false).Count, "an empty key");
            Assert.AreEqual(1, StartupSecurityChecks.Problems(Config(null), isDevelopment: false).Count);
            Assert.AreEqual(0, StartupSecurityChecks.Problems(Config("a-private-key-made-by-the-installer-0123456789abcdef"), isDevelopment: false).Count);
            Assert.AreEqual(0, StartupSecurityChecks.Problems(null, isDevelopment: false).Count);
        }

        // ---------------------------------------------------------------------------------------------------- M4: the keyed hash of masked values

        [TestMethod]
        public async Task Masked_Values_Get_A_Keyed_Hash_Only_When_A_Key_Is_Set()
        {
            string Hash(string key, string value)
            {
                AuditHmac.Configure(key);
                return AuditHmac.Compute(value);
            }

            try
            {
                Assert.IsNull(Hash(null, "0012345678"), "no key, no hash");
                Assert.IsNull(Hash("  ", "0012345678"));
                Assert.IsFalse(AuditHmac.IsConfigured);
                string a = Hash("secret-key-one", "0012345678");
                Assert.AreEqual(32, a.Length);
                Assert.AreEqual(a, Hash("secret-key-one", " 0012 345678 "), "spaces do not matter");
                Assert.AreNotEqual(a, Hash("secret-key-two", "0012345678"), "another key, another hash");
                Assert.AreNotEqual(a, Hash("secret-key-one", "0012345679"));
                string unsalted = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("0012345678"))).Substring(0, 16).ToLowerInvariant();
                Assert.IsFalse(a.Contains(unsalted), "it is not the unsalted hash of before");
                Assert.IsNull(Hash("secret-key-one", null));

                // in the trail: the masked text, plus the keyed hash next to it when a key is set
                var withoutKey = new Host(Unique("hmac"));
                AuditHmac.Configure(null);
                string noKey = Unique("nokey");
                await withoutKey.RegisterAsync(withoutKey.Admin, noKey, new[] { WarehouseRole });
                FnAuditLog plain = Trail(withoutKey).Single(r => r.EntityType == "User" && r.Action == "Added" && (r.AfterJson ?? "").Contains(noKey));
                StringAssert.Contains(plain.AfterJson, "\"NationalCode\":\"****5678\"");
                Assert.IsFalse(plain.AfterJson.Contains("NationalCodeHash") || plain.AfterJson.Contains("PhoneNumberHash"), "only the masked text");

                AuditHmac.Configure("secret-key-one");
                string withKey = Unique("withkey");
                await withoutKey.RegisterAsync(withoutKey.Admin, withKey, new[] { WarehouseRole });
                FnAuditLog keyed = Trail(withoutKey).Single(r => r.EntityType == "User" && r.Action == "Added" && (r.AfterJson ?? "").Contains(withKey));
                StringAssert.Contains(keyed.AfterJson, "\"NationalCodeHash\":\"" + a + "\"");
                StringAssert.Contains(keyed.AfterJson, "\"PhoneNumberHash\"");
                Assert.IsFalse(keyed.AfterJson.Contains("0012345678"));
            }
            finally
            {
                AuditHmac.Configure(null);
            }
        }
    }
}
