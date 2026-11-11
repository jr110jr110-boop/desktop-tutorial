using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Identity.AccountServices;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro.Pagination;

namespace MsTests
{
    /// <summary>FX2: a user whose sensitive role waits for the second person is told so (own requests only), and the administrator's user list shows the pending roles.</summary>
    public partial class IdentityTests
    {
        private const string Fx2Waiting = "نقش PMO شما منتظر تأیید نفر دوم است؛ پس از تأیید یک بار خارج و دوباره وارد شوید.";

        [TestMethod]
        public async Task Fx2_A_User_With_Only_A_Pending_Pmo_Role_Sees_The_Waiting_Warning_Until_The_Second_Person_Approves()
        {
            var h = new Host(Unique("fx2"));
            int second = await h.AddPmoAsync(Unique("pmo"));   // from two PMO users on, a PMO grant is a request
            string name = Unique("pmoonly");
            int user = await h.RegisterAsync(h.Admin, name, new[] { WarehouseRole, PmoRole });
            CollectionAssert.AreEqual(new[] { "WAREHOUSE" }, h.RolesOf(user), "PMO is not active before the second person approves");
            ClaimsPrincipal me = Signed(user, name, "WAREHOUSE");

            ResponseDto<System.Collections.Generic.List<OwnRoleRequestDto>> mine = await h.As(me, s => s.GetMyPendingRolesAsync(default).AsTask());
            Assert.IsTrue(mine.Success, mine.Message);
            Assert.AreEqual(1, mine.Data.Count);
            Assert.AreEqual("PMO", mine.Data[0].Role);
            Assert.AreEqual(Fx2Waiting, mine.Data[0].WarningText);

            // only the own requests: another user sees none
            ResponseDto<System.Collections.Generic.List<OwnRoleRequestDto>> others = await h.As(Signed(second, "pmo2", "PMO"), s => s.GetMyPendingRolesAsync(default).AsTask());
            Assert.AreEqual(0, others.Data.Count);
            Assert.IsFalse((await h.As(null, s => s.GetMyPendingRolesAsync(default).AsTask())).Success, "not signed in: nothing");

            // the user list of the administrator has the column
            ResponseDto<PagedList<ResponseUserDto>> users = await h.As(h.Admin, s => s.GetAllUsersAsync(new UserPaginationParams { Id = user }, default).AsTask());
            CollectionAssert.AreEqual(new[] { "PMO" }, users.Data.Single().PendingRoles);

            // the second person approves (after the receiver has chosen a password of his own)
            RoleGrantDto grant = (await GrantsAsync(h, h.Admin)).Single(g => g.UserId == user);
            await ChoosePasswordAsync(h, user, name);
            Assert.IsTrue((await ApproveAsync(h, Signed(second, "pmo2", "PMO"), grant.Id)).Success);
            Assert.AreEqual(0, (await h.As(me, s => s.GetMyPendingRolesAsync(default).AsTask())).Data.Count, "nothing waits any more");
            CollectionAssert.AreEqual(new[] { "PMO", "WAREHOUSE" }, h.RolesOf(user));
            Assert.AreEqual(0, (await h.As(h.Admin, s => s.GetAllUsersAsync(new UserPaginationParams { Id = user }, default).AsTask())).Data.Single().PendingRoles.Length);
        }
    }
}
