using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Common.Results;
using Entities.Identity.Constants;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ViewModels.NasbNiro;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.InquiryDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProviderDtos;
using ViewModels.NasbNiro.ProvisionalStatementDtos;
using WebApi.Controllers;
using WebApi.Extensions;

namespace MsTests
{
    /// <summary>
    /// R1a2 (M8): the legacy reads stay open to every signed-in user, but the bank accounts of a provider (account number, SHABA, card) are written in full only to
    /// PMO, COMMERCIAL, FINANCIAL, FINANCEMANAGER and TREASURY (the others get <c>****1234</c>), and the contract price, the price and the accepted price of a
    /// provisional statement, the price of an inquiry and the finance price of a buy order only to PMO, CEO, COMMERCIAL, FINANCIAL, FINANCEMANAGER, PLANNING,
    /// PROJECTMANAGER and SALES (the others get null). The rule is part of the JSON contract of MVC, so it also holds for the DTOs inside other DTOs.
    /// </summary>
    [TestClass]
    public class LegacyFieldAccessTests
    {
        private static readonly string[] AllRoles = RoleConstants.GetRoles().Select(r => r.Name).ToArray();
        private static readonly string[] BankReaders = LegacyRoles.ProviderBankReaders.Split(',');
        private static readonly string[] PriceReaders = LegacyRoles.PriceReaders.Split(',');

        private static ClaimsPrincipal User(params string[] roles) => AppHost.Principal(5, "someone", roles);

        /// <summary>The settings of Startup (camelCase, nulls written) with the rule for the given user.</summary>
        private static JsonSerializerSettings For(ClaimsPrincipal user)
        {
            var settings = new JsonSerializerSettings();
            ApiJson.Apply(settings);
            ApiJson.HideSensitiveFields(settings, () => user);
            return settings;
        }

        private static JToken Written<T>(T value, ClaimsPrincipal user) =>
            JToken.Parse(JsonConvert.SerializeObject(new ResponseDto<T>(true, null, value), For(user)))["data"];

        private static decimal? Number(JToken token) => token == null || token.Type == JTokenType.Null ? (decimal?)null : (decimal)token;

        private static ResponseProviderDto Provider() => new ResponseProviderDto
        {
            Id = 3, Name = "supplier", CategoryId = 1,
            ProviderAccounts = new[]
            {
                new ResponseProviderAccountDto { Id = 1, Name = "holder", Bank = "Melli", No = "0123456789", SHABA = "IR120170000000123456789012", Card = "6037991234567890" },
                new ResponseProviderAccountDto { Id = 2, Name = "short", Bank = "Saman", No = "1234", SHABA = null, Card = "" },
            },
        };

        // ------------------------------------------------------------------------------------------------ the lists

        [TestMethod]
        public void TheTwoListsNameSeededRolesOnly()
        {
            var seeded = new HashSet<string>(AllRoles, StringComparer.OrdinalIgnoreCase);
            foreach (string role in BankReaders.Concat(PriceReaders))
                Assert.IsTrue(seeded.Contains(role), role + " is not a seeded role: nobody would ever be allowed through it");
            CollectionAssert.AreEquivalent(new[] { "PMO", "COMMERCIAL", "FINANCIAL", "FINANCEMANAGER", "TREASURY" }, BankReaders);
            CollectionAssert.AreEquivalent(new[] { "PMO", "CEO", "COMMERCIAL", "FINANCIAL", "FINANCEMANAGER", "PLANNING", "PROJECTMANAGER", "SALES" }, PriceReaders);
        }

        [TestMethod]
        public void EveryHiddenMemberExistsOnItsDtoWithTheRightType()
        {
            Assert.AreEqual(6, LegacyFieldAccess.Hidden.Count);
            foreach ((Type type, string[] members, bool bank) in LegacyFieldAccess.Hidden)
                foreach (string member in members)
                {
                    PropertyInfo property = type.GetProperty(member);
                    Assert.IsNotNull(property, type.Name + "." + member + " was renamed: the field would be shown to everybody again");
                    if (bank) Assert.AreEqual(typeof(string), property.PropertyType, type.Name + "." + member);
                    else Assert.IsTrue(property.PropertyType == typeof(decimal?), type.Name + "." + member + " must be nullable so that it can be left out");
                }
        }

        // ------------------------------------------------------------------------------------------------ prices

        [TestMethod]
        public void ThePricesAreNullForEveryRoleOutsideTheListAndWrittenForTheRolesInIt()
        {
            var failures = new List<string>();
            foreach (string role in AllRoles)
            {
                bool allowed = PriceReaders.Contains(role);
                ClaimsPrincipal user = User(role);
                decimal? Expect(decimal value) => allowed ? value : (decimal?)null;
                void Check(string what, JToken token, decimal? expected)
                {
                    decimal? actual = Number(token);
                    if (actual != expected) failures.Add(role + " / " + what + ": " + (actual?.ToString() ?? "null") + " instead of " + (expected?.ToString() ?? "null"));
                }

                Check("contract price", Written(new ResponseContractDto { Id = 1, Code = "C-1", Price = 5000000m }, user)["price"], Expect(5000000m));
                JToken statement = Written(new ResponseProStatDto { Id = 1, ContractId = 1, Price = 700m, AcceptedPrice = 650m }, user);
                Check("statement price", statement["price"], Expect(700m));
                Check("statement accepted price", statement["acceptedPrice"], Expect(650m));
                Check("inquiry price", Written(new ResponseInquiryDto { Id = 1, Price = 90m }, user)["price"], Expect(90m));
                Check("buy order finance price", Written(new ResponseBuyOrderDto { Id = 1, FinancePrice = 321m }, user)["financePrice"], Expect(321m));
                Check("card board finance price", Written(new ResponseBuyOrderCardBoardDto { Id = 1, FinancePrice = 654m }, user)["financePrice"], Expect(654m));
            }
            Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
        }

        [TestMethod]
        public void TheOtherFieldsOfThoseDtosAreNotTouchedAndTheNamesStayCamelCase()
        {
            ClaimsPrincipal outsider = User("WAREHOUSE");
            JToken contract = Written(new ResponseContractDto { Id = 9, Code = "C-9", Title = "Plant", Price = 10m, Employer = "client" }, outsider);
            Assert.AreEqual(9, (int)contract["id"]);
            Assert.AreEqual("C-9", (string)contract["code"]);
            Assert.AreEqual("Plant", (string)contract["title"]);
            Assert.AreEqual("client", (string)contract["employer"]);
            Assert.AreEqual(JTokenType.Null, contract["price"].Type, "the member is still there, with null: the front shows an empty cell and does not break");

            // what the rule does not name stays as it was, for everybody: the guaranty of a contract, the buy order's count
            JToken order = Written(new ResponseBuyOrderDto { Id = 2, Title = "pump", Count = 3, FinanceNumber = "F-1", FinancePrice = 5m }, outsider);
            Assert.AreEqual(3, (int)order["count"]);
            Assert.AreEqual("F-1", (string)order["financeNumber"]);
        }

        [TestMethod]
        public void ARoleInTheListIsEnoughEvenWithOtherRolesAndNoRoleMeansNothing()
        {
            var contract = new ResponseContractDto { Id = 1, Code = "C-1", Price = 42m };
            Assert.AreEqual(42m, Number(Written(contract, User("WAREHOUSE", "QC", "SALES"))["price"]));
            Assert.IsNull(Number(Written(contract, User("WAREHOUSE", "QC"))["price"]));
            Assert.IsNull(Number(Written(contract, User())["price"]), "a signed-in user without any role");
            Assert.IsNull(Number(Written(contract, null)["price"]), "no user at all (no HTTP request): the safe answer is to hide");
            Assert.IsNull(Number(Written(contract, User("pmo"))["price"]), "role names are compared exactly as [Authorize(Roles = ...)] compares them (the token carries them in upper case)");
        }

        // ------------------------------------------------------------------------------------------------ bank accounts

        [TestMethod]
        public void TheBankAccountsAreMaskedForEveryRoleOutsideTheListAndWrittenForTheRolesInIt()
        {
            var failures = new List<string>();
            foreach (string role in AllRoles)
            {
                bool allowed = BankReaders.Contains(role);
                JToken accounts = Written(Provider(), User(role))["providerAccounts"];
                JToken first = accounts[0], second = accounts[1];
                string Expect(string full, string masked) => allowed ? full : masked;
                void Check(string what, JToken token, string expected)
                {
                    string actual = token.Type == JTokenType.Null ? null : (string)token;
                    if (actual != expected) failures.Add(role + " / " + what + ": " + (actual ?? "null") + " instead of " + (expected ?? "null"));
                }

                Check("account number", first["no"], Expect("0123456789", "****6789"));
                Check("SHABA", first["shaba"], Expect("IR120170000000123456789012", "****9012"));
                Check("card", first["card"], Expect("6037991234567890", "****7890"));
                Check("short number", second["no"], Expect("1234", "****"));
                Check("missing SHABA stays missing", second["shaba"], null);
                Check("empty card stays empty", second["card"], "");
                // the holder and the bank are not part of the rule
                Check("holder", first["name"], "holder");
                Check("bank", first["bank"], "Melli");
            }
            Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
        }

        [TestMethod]
        public void TheRuleAlsoHoldsForTheDtosInsideOtherDtosAndLists()
        {
            ClaimsPrincipal outsider = User("WAREHOUSE");
            ClaimsPrincipal reader = User("COMMERCIAL");

            // an inquiry carries its provider, with the accounts
            var inquiry = new ResponseInquiryDto { Id = 1, Price = 80m, Provider = Provider() };
            JToken seenByOutsider = Written(inquiry, outsider);
            Assert.AreEqual(JTokenType.Null, seenByOutsider["price"].Type);
            Assert.AreEqual("****9012", (string)seenByOutsider["provider"]["providerAccounts"][0]["shaba"]);
            JToken seenByReader = Written(inquiry, reader);
            Assert.AreEqual(80m, Number(seenByReader["price"]));
            Assert.AreEqual("IR120170000000123456789012", (string)seenByReader["provider"]["providerAccounts"][0]["shaba"]);

            // a page of providers and the vendor list (a plain list)
            var page = new PaginatedResult<ResponseProviderDto> { Data = new[] { Provider(), Provider() }, CurrentPage = 1, TotalItems = 2, TotalPages = 1, ItemsPerPage = 10 };
            JToken pageSeen = Written(page, outsider);
            Assert.AreEqual(2, pageSeen["data"].Count());
            Assert.IsTrue(pageSeen["data"].All(p => (string)p["providerAccounts"][0]["card"] == "****7890"));
            JToken vendorList = Written((IEnumerable<ResponseProviderDto>)new List<ResponseProviderDto> { Provider() }, outsider);
            Assert.AreEqual("****6789", (string)vendorList[0]["providerAccounts"][0]["no"]);

            // the dashboard of a user carries buy orders
            var dashboard = new ResponseDashboardDto { UserBuyOrders = new[] { new ResponseBuyOrderDto { Id = 1, FinancePrice = 12m } } };
            Assert.AreEqual(JTokenType.Null, Written(dashboard, outsider)["userBuyOrders"][0]["financePrice"].Type);
            Assert.AreEqual(12m, Number(Written(dashboard, reader)["userBuyOrders"][0]["financePrice"]));
        }

        [TestMethod]
        public void TheDtoObjectsThemselvesAreNotChangedAndTheWriterOfARecordSeesItInFull()
        {
            ResponseProviderDto provider = Provider();
            var contract = new ResponseContractDto { Id = 1, Code = "C-1", Price = 99m };
            Written(provider, User("WAREHOUSE"));
            Written(contract, User("WAREHOUSE"));
            Assert.AreEqual("IR120170000000123456789012", provider.ProviderAccounts[0].SHABA, "the object is the same for the next request (a shared instance must not be changed)");
            Assert.AreEqual(99m, contract.Price);

            // the roles that may write these records are all in the lists, so the answer to their own call is complete
            foreach (string writer in LegacyRoles.Purchasing.Split(','))
                Assert.IsTrue(BankReaders.Contains(writer), writer + " writes provider accounts but could not read them back");
            foreach (string writer in LegacyRoles.ContractWriters.Split(',').Concat(LegacyRoles.ProvisionalStatementWriters.Split(',')).Concat(LegacyRoles.Purchasing.Split(',')))
                Assert.IsTrue(PriceReaders.Contains(writer), writer + " writes contracts, statements or inquiries but could not read the price back");
        }

        [TestMethod]
        public void TheFullAnswerOfAReaderIsExactlyWhatTheSettingsOfBeforeWrote()
        {
            // for a role in both lists nothing changes: the same text, member by member, as the plain settings of ApiJson give
            var inquiry = new ResponseInquiryDto { Id = 1, Price = 80m, Count = 2, Provider = Provider() };
            string plain = JsonConvert.SerializeObject(new ResponseDto<ResponseInquiryDto>(true, null, inquiry), ApiJson.Create());
            string reader = JsonConvert.SerializeObject(new ResponseDto<ResponseInquiryDto>(true, null, inquiry), For(User("PMO")));
            Assert.AreEqual(plain, reader);
        }
    }
}
