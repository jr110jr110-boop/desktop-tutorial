using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;

namespace MsTests
{
    /// <summary>Shared pieces of the finance tests that run the real services on the in-memory database (R1t).</summary>
    internal static class FinanceTestSupport
    {
        public static readonly ClaimsPrincipal Fin = AppHost.Principal(31, "fin.one", "FINANCIAL");
        public static readonly ClaimsPrincipal Fm = AppHost.Principal(32, "fm.one", "FINANCEMANAGER");
        public static readonly ClaimsPrincipal Fm2 = AppHost.Principal(36, "fm.two", "FINANCEMANAGER");
        public static readonly ClaimsPrincipal Tr = AppHost.Principal(33, "tr.one", "TREASURY");
        public static readonly ClaimsPrincipal Tr2 = AppHost.Principal(34, "tr.two", "TREASURY");
        public static readonly ClaimsPrincipal FinTr = AppHost.Principal(35, "fin.tr", "FINANCIAL", "TREASURY");
        public static readonly ClaimsPrincipal Ceo = AppHost.Principal(37, "ceo.one", "CEO");
        public static readonly ClaimsPrincipal Auditor = AppHost.Principal(38, "auditor.one", "AUDITOR");

        /// <summary>
        /// A new host on its own in-memory database. The finance seed remembers in a process-wide flag that it has run, so a second database would never be seeded:
        /// the flag is cleared first, and the seed of this database runs on its first finance call.
        /// </summary>
        public static AppHost NewHost(string prefix)
        {
            FieldInfo done = typeof(FinanceSeed).GetField("_done", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(done, "FinanceSeed._done was renamed: update the test support");
            done.SetValue(null, false);
            return new AppHost(prefix + "_" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>A valid Iranian IBAN for a 22-digit account part (ISO 7064 mod 97-10 check digits).</summary>
        public static string Iban(string digits22)
        {
            BigInteger n = BigInteger.Parse(digits22 + "182700");   // I = 18, R = 27, check digits 00
            int check = 98 - (int)(n % 97);
            return "IR" + check.ToString("00") + digits22;
        }

        public static T Ok<T>(ResponseDto<T> r)
        {
            Assert.IsTrue(r.Success, r.Message);
            return r.Data;
        }

        public static async Task<int> BankAsync(AppHost host, string digits22)
        {
            BankAccountDto b = Ok(await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().SaveBankAccountAsync(new SaveBankAccountDto
            {
                Title = "حساب جاری " + digits22.Substring(0, 3), BankName = "بانک آزمون", AccountNumber = digits22, Iban = Iban(digits22), CurrencyCode = "IRR",
            }, CancellationToken.None)));
            return b.Id;
        }

        public static async Task<decimal> BalanceAsync(AppHost host, int bankId)
        {
            PagedList<BankAccountDto> list = Ok(await host.As(Tr, sp => sp.GetRequiredService<ITreasuryService>().GetBankAccountsAsync(new TreasuryQuery { PageSize = 0 }, CancellationToken.None)));
            return list.First(b => b.Id == bankId).Balance;
        }
    }
}
