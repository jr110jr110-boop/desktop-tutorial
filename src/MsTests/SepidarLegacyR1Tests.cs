using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using WebApi.Filters;

namespace MsTests
{
    /// <summary>R1t D19: the legacy Sepidar CSV channels are off unless Sepidar:LegacyCsv is on, and then for the finance manager alone.</summary>
    [TestClass]
    public class SepidarLegacyR1Tests
    {
        [TestMethod]
        public void The_Switch_Is_Off_Unless_The_Setting_Says_True()
        {
            foreach (string off in new string[] { null, "", " ", "false", "False", "0", "no", "off", "maybe" })
                Assert.IsFalse(SepidarLegacy.ParseFlag(off), "'" + off + "'");
            foreach (string on in new[] { "true", "True", " TRUE ", "1", "yes", "on" })
                Assert.IsTrue(SepidarLegacy.ParseFlag(on), "'" + on + "'");

            IConfiguration missing = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>()).Build();
            Assert.IsFalse(SepidarLegacy.Enabled(missing));
            Assert.IsFalse(SepidarLegacy.Enabled(null));
            // the environment variable form of the key (Sepidar__LegacyCsv) reaches the same key
            IConfiguration on2 = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["Sepidar:LegacyCsv"] = "true" }).Build();
            Assert.IsTrue(SepidarLegacy.Enabled(on2));
        }

        [TestMethod]
        public void Refusal_Names_The_Switch_First_And_Then_The_Role()
        {
            Assert.AreEqual(SepidarLegacy.DisabledText, SepidarLegacy.Refusal(false, true));
            Assert.AreEqual(SepidarLegacy.DisabledText, SepidarLegacy.Refusal(false, false));
            Assert.AreEqual(SepidarLegacy.ManagerOnlyText, SepidarLegacy.Refusal(true, false));
            Assert.IsNull(SepidarLegacy.Refusal(true, true));
            Assert.AreEqual("FINANCEMANAGER", SepidarLegacy.Roles);
        }

        [TestMethod]
        public void The_Setting_Is_Off_In_The_Shipped_Appsettings()
        {
            IConfiguration shipped = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: false, reloadOnChange: false).Build();
            Assert.IsNotNull(shipped["Sepidar:LegacyCsv"], "the key is written out so that it can be found");
            Assert.IsFalse(SepidarLegacy.Enabled(shipped));
        }

        private static (IActionResult Result, bool Reached) Run(string setting, params string[] roles)
        {
            var services = new ServiceCollection();
            var values = new Dictionary<string, string>();
            if (setting != null) values["Sepidar:LegacyCsv"] = setting;
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "5") };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider(), User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
            var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: new object());
            bool reached = false;
            // no IUnitOfWork is registered: the audit step is skipped, the gate itself is what is tested here
            new LegacySepidarCsvAttribute("test").OnActionExecutionAsync(context, () =>
            {
                reached = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            }).GetAwaiter().GetResult();
            return (context.Result, reached);
        }

        [TestMethod]
        public void The_Filter_Turns_Everybody_Away_While_The_Setting_Is_Off()
        {
            foreach (string setting in new string[] { null, "false" })
            {
                (IActionResult result, bool reached) = Run(setting, "FINANCEMANAGER");
                Assert.IsFalse(reached);
                var refused = (ObjectResult)result;
                Assert.AreEqual(403, refused.StatusCode);
                var body = (ResponseDto<object>)refused.Value;
                Assert.IsFalse(body.Success);
                Assert.AreEqual(SepidarLegacy.DisabledText, body.Message);
            }
        }

        [TestMethod]
        public void With_The_Setting_On_Only_The_Finance_Manager_Gets_Through()
        {
            (IActionResult manager, bool managerReached) = Run("true", "FINANCEMANAGER");
            Assert.IsNull(manager);
            Assert.IsTrue(managerReached);
            foreach (string role in new[] { "PMO", "FINANCIAL", "CEO", "TREASURY", "AUDITOR" })
            {
                (IActionResult result, bool reached) = Run("true", role);
                Assert.IsFalse(reached, role);
                Assert.AreEqual(403, ((ObjectResult)result).StatusCode, role);
                Assert.AreEqual(SepidarLegacy.ManagerOnlyText, ((ResponseDto<object>)((ObjectResult)result).Value).Message, role);
            }
        }

        [TestMethod]
        public void The_Three_Legacy_Channels_Are_Marked_And_Their_Role_Attribute_Is_The_Finance_Manager_Only()
        {
            var channels = new[]
            {
                (typeof(WebApi.Controllers.SepidarLinkController), "ExportAsync"),
                (typeof(WebApi.Controllers.FinanceLinkController), "ExportAsync"),
                (typeof(WebApi.Controllers.FinanceLinkController), "MarkAsync"),
            };
            foreach ((Type controller, string action) in channels)
            {
                MethodInfo method = controller.GetMethod(action);
                Assert.IsNotNull(method, controller.Name + "." + action);
                Assert.IsNotNull(Attribute.GetCustomAttribute(method, typeof(LegacySepidarCsvAttribute)), controller.Name + "." + action + " is not marked");
                var roles = (AuthorizeAttribute)Attribute.GetCustomAttribute(method, typeof(AuthorizeAttribute));
                Assert.IsNotNull(roles, controller.Name + "." + action);
                Assert.AreEqual("FINANCEMANAGER", roles.Roles, controller.Name + "." + action);
            }
        }

        [TestMethod]
        public void A_Use_Of_The_Channel_Writes_A_Row_Of_The_Trail()
        {
            AppHost host = FinanceTestSupport.NewHost("SepidarLegacyR1");
            host.Write(uow =>
            {
                SepidarLegacy.RecordUse(uow, 32, "events-export");
                uow.SaveChanges();
            }, FinanceTestSupport.Fm);
            FnAuditLog row = host.Read(uow => uow.Set<FnAuditLog>().Single(a => a.EntityType == "SepidarLegacyCsv"));
            Assert.AreEqual(32, row.UserId);
            Assert.AreEqual("LegacyCsv", row.Action);
            StringAssert.Contains(row.Summary, "کانال قدیمی");
            StringAssert.Contains(row.AfterJson, "events-export");
        }
    }
}
