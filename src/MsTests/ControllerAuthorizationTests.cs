using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MsTests
{
    /// <summary>Every API action answers only to a signed-in user, except the few routes that are anonymous by design (login, logout, the API settings, the attendance device push with its own key).</summary>
    [TestClass]
    public class ControllerAuthorizationTests
    {
        private static readonly HashSet<string> AnonymousByDesign = new HashSet<string>(StringComparer.Ordinal)
        {
            "AccountsController.LoginAsync", "AccountsController.LogoutAsync", "AccountsController.RefreshTokenAsync",
            "ApiSettingsController.Get", "OfficeAttendanceController.PushAsync",
        };

        [TestMethod]
        public void EveryActionIsProtectedOrAnonymousByDesign()
        {
            Assembly api = typeof(WebApi.Controllers.BaseApiController).Assembly;
            var unprotected = new List<string>();
            var anonymous = new List<string>();
            foreach (Type controller in api.GetTypes().Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)))
            {
                bool classAuthorized = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
                foreach (MethodInfo action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any()) continue;
                    string name = controller.Name + "." + action.Name;
                    bool anon = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any() || controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
                    bool authorized = classAuthorized || action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
                    if (anon) anonymous.Add(name);
                    else if (!authorized) unprotected.Add(name);
                }
            }
            Assert.AreEqual(0, unprotected.Count, "actions without [Authorize]: " + string.Join(", ", unprotected));
            string[] unexpected = anonymous.Where(a => !AnonymousByDesign.Contains(a)).ToArray();
            Assert.AreEqual(0, unexpected.Length, "anonymous actions that are not on the allow-list: " + string.Join(", ", unexpected));
        }
    }
}
