using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Newtonsoft.Json.Serialization;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.InquiryDtos;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.ProcurementDtos;
using ViewModels.NasbNiro.ProviderDtos;
using ViewModels.NasbNiro.ProvisionalStatementDtos;
using ViewModels.NasbNiro.TenderDtos;
using WebApi.Controllers;

namespace WebApi.Extensions
{
    /// <summary>
    /// R1a2 (M8, L-6): the sensitive fields of the reads that stay open to every signed-in user (the card board, the dashboard and the lists need the records),
    /// but whose values are only for the roles that work with them:
    /// <list type="bullet">
    /// <item><description>the bank accounts of a provider (account number, SHABA, card number) and the IBAN of a supplier profile (<c>procurement/suppliers</c>): <see cref="LegacyRoles.ProviderBankReaders"/>; everybody else gets the number masked to its last four characters (<c>****1234</c>);</description></item>
    /// <item><description>the prices of the contract (<c>price</c>), of a provisional statement (<c>price</c>, <c>acceptedPrice</c>), of an inquiry (<c>price</c>), the finance price of a buy order (<c>financePrice</c>) and the finance price in the supplier-return report of a non-conformance report: <see cref="LegacyRoles.PriceReaders"/>; everybody else gets <c>null</c>;</description></item>
    /// <item><description>the price text of a contract guaranty and of a tender guaranty (<c>price</c>, "500,000,000"): <see cref="LegacyRoles.GuarantyPriceReaders"/> (the price roles and the roles that write guaranties); everybody else gets <c>null</c>.</description></item>
    /// </list>
    /// The rule is applied where the answer is written (<see cref="LegacyFieldContractResolver"/>, part of the JSON settings of MVC), so it holds for every
    /// action that returns one of these DTOs, also when it sits inside another one (an inquiry carries its provider with the accounts, the dashboard carries buy orders, a contract carries its guaranties),
    /// and the DTO objects themselves are not changed. Writers of these records are in the lists, so the answer to their own call is complete (and a writer who reads a record and sends it back does not erase a value).
    /// </summary>
    public static class LegacyFieldAccess
    {
        public static bool InAny(ClaimsPrincipal user, string roles) =>
            user != null && roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(user.IsInRole);

        public static bool CanSeeBankAccounts(ClaimsPrincipal user) => InAny(user, LegacyRoles.ProviderBankReaders);

        public static bool CanSeePrices(ClaimsPrincipal user) => InAny(user, LegacyRoles.PriceReaders);

        public static bool CanSeeGuarantyPrices(ClaimsPrincipal user) => InAny(user, LegacyRoles.GuarantyPriceReaders);

        /// <summary>The last four characters behind four stars; a value of four characters or fewer is hidden completely; nothing stays nothing.</summary>
        public static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= 4 ? "****" : "****" + value.Substring(value.Length - 4);
        }

        /// <summary>
        /// The members of one DTO type that are hidden from the roles outside <paramref name="Readers"/>. <paramref name="Mask"/>: the text is masked to its last four characters (it must be a string);
        /// otherwise the value is left out (null; the member is a nullable number, or a string for the price text of a guaranty).
        /// </summary>
        public sealed record HiddenMembers(Type Type, string[] Members, string Readers, bool Mask);

        /// <summary>The members of the DTOs that are hidden from the roles outside their list (the unit test checks every entry against the DTO types).</summary>
        public static readonly IReadOnlyList<HiddenMembers> Hidden = new List<HiddenMembers>
        {
            new(typeof(ResponseProviderAccountDto), new[] { nameof(ResponseProviderAccountDto.No), nameof(ResponseProviderAccountDto.SHABA), nameof(ResponseProviderAccountDto.Card) }, LegacyRoles.ProviderBankReaders, true),
            new(typeof(SupplierProfileDto), new[] { nameof(SupplierProfileDto.Iban) }, LegacyRoles.ProviderBankReaders, true),
            new(typeof(ResponseContractDto), new[] { nameof(ResponseContractDto.Price) }, LegacyRoles.PriceReaders, false),
            new(typeof(ResponseProStatDto), new[] { nameof(ResponseProStatDto.Price), nameof(ResponseProStatDto.AcceptedPrice) }, LegacyRoles.PriceReaders, false),
            new(typeof(ResponseInquiryDto), new[] { nameof(ResponseInquiryDto.Price) }, LegacyRoles.PriceReaders, false),
            new(typeof(ResponseBuyOrderDto), new[] { nameof(ResponseBuyOrderDto.FinancePrice) }, LegacyRoles.PriceReaders, false),
            new(typeof(ResponseBuyOrderCardBoardDto), new[] { nameof(ResponseBuyOrderCardBoardDto.FinancePrice) }, LegacyRoles.PriceReaders, false),
            new(typeof(SupplierReturnReportDto), new[] { nameof(SupplierReturnReportDto.FinancePrice) }, LegacyRoles.PriceReaders, false),
            new(typeof(ResponseContractGuarantyDto), new[] { nameof(ResponseContractGuarantyDto.Price) }, LegacyRoles.GuarantyPriceReaders, false),
            new(typeof(ResponseTenderGuarantyDto), new[] { nameof(ResponseTenderGuarantyDto.Price) }, LegacyRoles.GuarantyPriceReaders, false),
        };
    }

    /// <summary>
    /// The JSON contract of MVC with the rule of <see cref="LegacyFieldAccess"/> built in: a hidden member is read through a value provider that asks for the
    /// user of the current request every time, so the cached contract can be shared by all requests. With no user at all (no HTTP request) the members are hidden.
    /// </summary>
    public sealed class LegacyFieldContractResolver : DefaultContractResolver
    {
        private readonly Func<ClaimsPrincipal> _currentUser;

        public LegacyFieldContractResolver(Func<ClaimsPrincipal> currentUser)
        {
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            NamingStrategy = new CamelCaseNamingStrategy(true, true, true);
        }

        protected override IList<JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
        {
            IList<JsonProperty> properties = base.CreateProperties(type, memberSerialization);
            foreach (JsonProperty property in properties)
            {
                foreach (LegacyFieldAccess.HiddenMembers hidden in LegacyFieldAccess.Hidden)
                {
                    if (hidden.Type.IsAssignableFrom(type) && hidden.Members.Contains(property.UnderlyingName, StringComparer.Ordinal))
                    {
                        property.ValueProvider = new HidingValueProvider(property.ValueProvider, hidden.Readers, hidden.Mask, _currentUser);
                        break;
                    }
                }
            }
            return properties;
        }

        private sealed class HidingValueProvider : IValueProvider
        {
            private readonly IValueProvider _inner;
            private readonly string _readers;
            private readonly bool _mask;
            private readonly Func<ClaimsPrincipal> _currentUser;

            public HidingValueProvider(IValueProvider inner, string readers, bool mask, Func<ClaimsPrincipal> currentUser)
            {
                _inner = inner;
                _readers = readers;
                _mask = mask;
                _currentUser = currentUser;
            }

            public object GetValue(object target)
            {
                object value = _inner.GetValue(target);
                if (value == null) return null;
                if (LegacyFieldAccess.InAny(_currentUser(), _readers)) return value;
                return _mask ? LegacyFieldAccess.Mask(value as string) : null;
            }

            public void SetValue(object target, object value) => _inner.SetValue(target, value);
        }
    }
}
